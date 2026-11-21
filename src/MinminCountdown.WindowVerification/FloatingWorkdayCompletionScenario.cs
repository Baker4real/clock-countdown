using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #317：下班收尾期间悬浮窗底部改为鼓励语并退出自动化树，不再重复金额；
// 收尾卡内金额承载隐私遮罩切换（指针含移出取消、键盘 Tab/Enter/Space 与自动化 Invoke），
// 遮罩态子树与 ToolTip 不泄密、焦点环规范且状态不持久化；零薪资显示鼓励文案且不是假遮罩按钮；
// 下班起点、北京时间次日零点终点、筛选与日历未覆盖当年守卫均保持不变。
internal static class FloatingWorkdayCompletionScenario
{
    private const decimal FixtureMonthlySalary = 4910.04m; // 2030-07 有 23 个工作日，日薪精确 213.48 元
    private const string DailyCapDigits = "213.48";
    private const string DailyCapText = "今日已赚 ￥213.48";
    private const string ZeroDayEarningsText = "今日已赚 ￥0.00";
    private const string UserCountdownName = "七月用户倒计时";

    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    private static readonly DateTimeOffset WorkEndUtc =
        new DateTimeOffset(2030, 7, 15, 18, 0, 0, BeijingOffset).ToUniversalTime();
    private static readonly DateTimeOffset BeforeWorkEndUtc = WorkEndUtc.AddSeconds(-1);
    private static readonly DateTimeOffset BeforeMidnightUtc =
        new DateTimeOffset(2030, 7, 15, 23, 59, 59, BeijingOffset).ToUniversalTime();
    private static readonly DateTimeOffset NextDayMidnightUtc =
        new DateTimeOffset(2030, 7, 16, 0, 0, 0, BeijingOffset).ToUniversalTime();
    private static readonly DateTimeOffset NonWorkdayEveningUtc =
        new DateTimeOffset(2030, 7, 20, 19, 0, 0, BeijingOffset).ToUniversalTime(); // Saturday
    private static readonly DateTimeOffset UncoveredYearEveningUtc =
        new DateTimeOffset(2031, 7, 15, 19, 0, 0, BeijingOffset).ToUniversalTime();
    private static readonly DateTimeOffset FutureCountdownTargetUtc =
        new DateTimeOffset(2030, 7, 25, 12, 0, 0, BeijingOffset).ToUniversalTime();

    internal static void Run()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-workday-completion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var clock = new MutableClock(BeforeWorkEndUtc);
        FloatingWindow? floating = null;
        var failures = new List<string>();

        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var initialPreferences = workspace.Snapshot.Settings.Preferences;
            Apply(
                workspace,
                new SavePreferencesChange(
                    "Second",
                    FixtureMonthlySalary,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)),
                System.Windows.Threading.Dispatcher.CurrentDispatcher);
            Apply(
                workspace,
                new SaveCountdownChange(
                    null,
                    UserCountdownName,
                    FutureCountdownTargetUtc,
                    Array.Empty<string>()),
                System.Windows.Threading.Dispatcher.CurrentDispatcher);

            var savedSettingsBytes = File.ReadAllBytes(settingsPath);
            var savedCanonicalSettings = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);

            var year2030Json = BuildWeekdayCalendarJson(2030);
            using var calendar = new HolidayCalendar(
                tempRoot,
                (year, _) => year == 2030
                    ? Task.FromResult(year2030Json)
                    : Task.FromException<string>(new HttpRequestException($"no fixture for {year}")));

            floating = new FloatingWindow(
                workspace,
                calendar,
                new NoopManagerHost(),
                AppContext.BaseDirectory);
            Show(floating);
            WaitForTask(calendar.RefreshAsync(BeforeWorkEndUtc, CancellationToken.None), floating);

            var rows = RequiredByName<StackPanel>(floating, "FloatingRows");
            var bottomIncome = RequiredByName<TextBlock>(floating, "FloatingIncome");
            var bottomEncouragement = RequiredByName<SilentTextBlock>(floating, "FloatingEncouragement");
            var bottomFocusRing = RequiredByName<Border>(floating, "FloatingIncomeFocusRing");
            var floatingRootPeer = UIElementAutomationPeer.CreatePeerForElement(floating)
                ?? throw new InvalidOperationException("悬浮窗缺少根 AutomationPeer。");

            void At(DateTimeOffset utcNow)
            {
                clock.UtcNow = utcNow;
                floating.UpdateTime(utcNow);
                Settle(floating);
            }

            bool HasCompletionCard() => VisualDescendants<TextBlock>(rows).Any(block => block.Text == "下班啦");
            Border? CompletionCard() => rows.Children.Count > 0 ? rows.Children[0] as Border : null;

            // 1. 下班前一秒：仍显示距离下班与底栏金额，不显示收尾卡。
            At(BeforeWorkEndUtc);
            Check(
                failures,
                "下班前一秒保持距离下班且底栏显示金额",
                !HasCompletionCard() &&
                VisualDescendants<TextBlock>(rows).Any(block => block.Text == "距离下班") &&
                bottomIncome.Visibility == Visibility.Visible &&
                bottomIncome.Text.StartsWith("今日已赚 ￥", StringComparison.Ordinal) &&
                bottomEncouragement.Visibility == Visibility.Collapsed);

            // 2. 精确到达下班时刻：主项切换为收尾卡，底栏改为鼓励语并退出自动化树，不含金额文字。
            At(WorkEndUtc);
            var card = CompletionCard();
            var completionAmount = card is null
                ? null
                : VisualDescendants<InteractiveStatusTextBlock>(card).SingleOrDefault();
            var cardFocusRing = completionAmount?.Parent is Grid amountHost
                ? amountHost.Children.OfType<Border>().SingleOrDefault()
                : null;

            Check(
                failures,
                "下班时刻显示收尾卡且主卡不含倒计时分格或 Control 控件，用户倒计时保持次项与数量",
                HasCompletionCard() &&
                card is not null &&
                !VisualDescendants<Grid>(card).Any(grid => Equals(grid.Tag, FloatingWindow.RemainingBlocksTag)) &&
                !VisualDescendants<Control>(card).Any() &&
                floating.FloatingCountdownCount == 1 &&
                rows.Children.Count == 2);
            CheckBottomIsEncouragementWithoutAmount(
                failures,
                floating,
                floatingRootPeer,
                bottomIncome,
                bottomEncouragement,
                bottomFocusRing,
                "正薪资收尾期间底栏");
            Check(
                failures,
                "收尾卡内金额使用可调用元素并具备焦点环、手型光标与同步可访问名",
                completionAmount is not null &&
                cardFocusRing is not null &&
                string.Equals(completionAmount.Text, DailyCapText, StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetName(completionAmount), DailyCapText, StringComparison.Ordinal) &&
                completionAmount.IsInvokable &&
                completionAmount.Focusable &&
                KeyboardNavigation.GetIsTabStop(completionAmount) &&
                completionAmount.FocusVisualStyle is null &&
                completionAmount.Cursor == Cursors.Hand &&
                IsTransparent(cardFocusRing.BorderBrush));

            if (completionAmount is not null && cardFocusRing is not null)
            {
                var completionPeer = UIElementAutomationPeer.CreatePeerForElement(completionAmount);
                Check(
                    failures,
                    "收尾卡金额在自动化树中角色为 Button 且提供 IInvokeProvider",
                    completionPeer is not null &&
                    completionPeer.IsControlElement() &&
                    completionPeer.IsContentElement() &&
                    completionPeer.GetAutomationControlType() == AutomationControlType.Button &&
                    completionPeer.GetPattern(PatternInterface.Invoke) is IInvokeProvider &&
                    string.Equals(completionPeer.GetName(), DailyCapText, StringComparison.Ordinal));

                // 3. 指针取消路径：无按下释放、按下后移出都不切换遮罩。
                RaiseMouse(completionAmount, UIElement.MouseLeftButtonUpEvent);
                Settle(floating);
                Check(
                    failures,
                    "收尾卡金额未按下直接释放鼠标不切换遮罩",
                    string.Equals(completionAmount.Text, DailyCapText, StringComparison.Ordinal));

                RaiseMouse(completionAmount, UIElement.MouseLeftButtonDownEvent);
                completionAmount.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = UIElement.MouseLeaveEvent
                });
                RaiseMouse(completionAmount, UIElement.MouseLeftButtonUpEvent);
                Settle(floating);
                Check(
                    failures,
                    "收尾卡金额按下后移出再释放不切换遮罩",
                    string.Equals(completionAmount.Text, DailyCapText, StringComparison.Ordinal));

                // 4. 指针点击切换遮罩：无残留焦点环、底栏仍为鼓励语、自动化子树与 ToolTip 零泄露。
                RaiseMouse(completionAmount, UIElement.MouseLeftButtonDownEvent);
                RaiseMouse(completionAmount, UIElement.MouseLeftButtonUpEvent);
                Settle(floating);
                Check(
                    failures,
                    "指针点击收尾卡金额切换为遮罩且不残留鼠标焦点环",
                    string.Equals(completionAmount.Text, FloatingBottomStatus.HiddenEarningsText, StringComparison.Ordinal) &&
                    string.Equals(AutomationProperties.GetName(completionAmount), FloatingBottomStatus.HiddenEarningsText, StringComparison.Ordinal) &&
                    !FocusVisualState.GetIsKeyboardFocusVisible(completionAmount) &&
                    IsTransparent(cardFocusRing.BorderBrush));
                CheckBottomIsEncouragementWithoutAmount(
                    failures,
                    floating,
                    floatingRootPeer,
                    bottomIncome,
                    bottomEncouragement,
                    bottomFocusRing,
                    "指针遮罩后收尾底栏");
                CheckSubtreeMaskedWithoutLeak(
                    failures,
                    floating,
                    floatingRootPeer,
                    DailyCapDigits,
                    "指针点击遮罩后自动化子树与 ToolTip");

                // 5. 键盘 Tab 聚焦点亮 2 DIP 暖色焦点环，Enter 切回未遮罩、Space 再次遮罩。
                RequiredByName<Control>(floating, "FloatingNotesButton").Focus();
                floating.Activate();
                completionAmount.Focus();
                var tabReached = completionAmount.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)) &&
                    Keyboard.FocusedElement is UIElement previous &&
                    !ReferenceEquals(previous, completionAmount) &&
                    previous.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) &&
                    completionAmount.IsKeyboardFocused;
                Settle(floating);
                var activeBrush = floating.FindResource("ActiveBrush") as Brush;
                Check(
                    failures,
                    "键盘 Tab 可进入收尾卡金额并点亮 2 DIP ActiveBrush 焦点环（单层绘制）",
                    tabReached &&
                    completionAmount.IsKeyboardFocused &&
                    FocusVisualState.GetIsKeyboardFocusVisible(completionAmount) &&
                    KeyboardFocusPolicy.KeyboardFocusDrawnOnce(completionAmount) &&
                    cardFocusRing.BorderThickness == new Thickness(2) &&
                    BrushColorEquals(cardFocusRing.BorderBrush, activeBrush));

                var generationBeforeKey = floating.FloatingRowsRebuildGenerationForValidation;
                WindowValidationInput.RaiseKey(completionAmount, Key.Enter);
                Settle(floating);
                Check(
                    failures,
                    "键盘 Enter 切回未遮罩金额且保持行实例、焦点与 2 DIP 焦点环",
                    string.Equals(completionAmount.Text, DailyCapText, StringComparison.Ordinal) &&
                    string.Equals(UIElementAutomationPeer.CreatePeerForElement(completionAmount)?.GetName(), DailyCapText, StringComparison.Ordinal) &&
                    floating.FloatingRowsRebuildGenerationForValidation == generationBeforeKey &&
                    completionAmount.IsKeyboardFocused &&
                    BrushColorEquals(cardFocusRing.BorderBrush, activeBrush));

                WindowValidationInput.RaiseKey(completionAmount, Key.Space);
                Settle(floating);
                Check(
                    failures,
                    "键盘 Space 再次遮罩且自动化子树与 ToolTip 不含未遮罩金额",
                    string.Equals(completionAmount.Text, FloatingBottomStatus.HiddenEarningsText, StringComparison.Ordinal));
                CheckSubtreeMaskedWithoutLeak(
                    failures,
                    floating,
                    floatingRootPeer,
                    DailyCapDigits,
                    "键盘 Space 遮罩后自动化子树与 ToolTip");

                // 6. 收尾期间 tick 原位刷新保留卡片实例、遮罩与焦点。
                At(WorkEndUtc.AddSeconds(1));
                Check(
                    failures,
                    "收尾期间 tick 原位刷新保留卡片实例与遮罩且底栏仍为鼓励语",
                    ReferenceEquals(CompletionCard(), card) &&
                    floating.FloatingRowsRebuildGenerationForValidation == generationBeforeKey &&
                    string.Equals(completionAmount.Text, FloatingBottomStatus.HiddenEarningsText, StringComparison.Ordinal) &&
                    bottomIncome.Visibility == Visibility.Collapsed &&
                    bottomEncouragement.Visibility == Visibility.Visible);

                // 7. 切到便签工作区共用运行期遮罩，切回倒计时恢复收尾卡遮罩与底栏鼓励语。
                floating.ShowWorkspace(WorkspaceKind.Notes);
                Settle(floating);
                Check(
                    failures,
                    "收尾期间切到便签页后底栏共用同一遮罩态且子树不泄密",
                    bottomIncome.Visibility == Visibility.Visible &&
                    string.Equals(bottomIncome.Text, FloatingBottomStatus.HiddenEarningsText, StringComparison.Ordinal) &&
                    bottomEncouragement.Visibility == Visibility.Collapsed);
                CheckSubtreeMaskedWithoutLeak(
                    failures,
                    floating,
                    floatingRootPeer,
                    DailyCapDigits,
                    "切到便签页遮罩态自动化子树");

                floating.ShowWorkspace(WorkspaceKind.Countdowns);
                At(WorkEndUtc.AddMinutes(30));
                var rebuiltCard = CompletionCard();
                var rebuiltAmount = rebuiltCard is null
                    ? null
                    : VisualDescendants<InteractiveStatusTextBlock>(rebuiltCard).SingleOrDefault();
                Check(
                    failures,
                    "切回倒计时页后收尾卡保留遮罩且底栏回到鼓励语",
                    rebuiltAmount is not null &&
                    string.Equals(rebuiltAmount.Text, FloatingBottomStatus.HiddenEarningsText, StringComparison.Ordinal) &&
                    bottomIncome.Visibility == Visibility.Collapsed &&
                    bottomEncouragement.Visibility == Visibility.Visible);
                CheckSubtreeMaskedWithoutLeak(
                    failures,
                    floating,
                    floatingRootPeer,
                    DailyCapDigits,
                    "切回倒计时页遮罩态自动化子树");

                // 8. 遮罩状态不持久化：设置字节不变，且新建悬浮窗实例默认恢复显示未遮罩金额。
                Check(
                    failures,
                    "收尾遮罩切换、tick 与切页均不改写设置文件",
                    File.ReadAllBytes(settingsPath).SequenceEqual(savedSettingsBytes) &&
                    string.Equals(
                        SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                        savedCanonicalSettings,
                        StringComparison.Ordinal));

                FloatingWindow? freshInstance = null;
                try
                {
                    var reloadedWorkspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
                    freshInstance = new FloatingWindow(
                        reloadedWorkspace,
                        calendar,
                        new NoopManagerHost(),
                        AppContext.BaseDirectory);
                    Show(freshInstance);
                    freshInstance.UpdateTime(WorkEndUtc.AddMinutes(30));
                    Settle(freshInstance);
                    var freshRows = RequiredByName<StackPanel>(freshInstance, "FloatingRows");
                    var freshCard = freshRows.Children.Count > 0 ? freshRows.Children[0] as Border : null;
                    var freshAmount = freshCard is null
                        ? null
                        : VisualDescendants<InteractiveStatusTextBlock>(freshCard).SingleOrDefault();
                    var freshBottomIncome = RequiredByName<TextBlock>(freshInstance, "FloatingIncome");
                    var freshBottomEncouragement = RequiredByName<SilentTextBlock>(freshInstance, "FloatingEncouragement");
                    Check(
                        failures,
                        "新悬浮窗实例在收尾期间默认显示未遮罩金额且底栏为鼓励语",
                        freshAmount is not null &&
                        string.Equals(freshAmount.Text, DailyCapText, StringComparison.Ordinal) &&
                        freshBottomIncome.Visibility == Visibility.Collapsed &&
                        string.IsNullOrEmpty(freshBottomIncome.Text) &&
                        freshBottomEncouragement.Visibility == Visibility.Visible &&
                        string.Equals(
                            freshBottomEncouragement.Text,
                            FloatingBottomStatus.CountdownEncouragementText,
                            StringComparison.Ordinal));
                }
                finally
                {
                    if (freshInstance is not null)
                    {
                        freshInstance.Close();
                        WindowValidationInput.Pump(freshInstance.Dispatcher);
                    }
                }

                // 9. 自动化 IInvokeProvider 在收尾卡金额上可来回切换遮罩。
                if (rebuiltAmount is not null)
                {
                    var invokeProvider = UIElementAutomationPeer.CreatePeerForElement(rebuiltAmount)
                        ?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                    Check(failures, "重建后收尾卡金额继续提供 IInvokeProvider", invokeProvider is not null);
                    invokeProvider?.Invoke();
                    Settle(floating);
                    Check(
                        failures,
                        "自动化 Invoke 将收尾卡金额切回未遮罩",
                        string.Equals(rebuiltAmount.Text, DailyCapText, StringComparison.Ordinal) &&
                        string.Equals(
                            UIElementAutomationPeer.CreatePeerForElement(rebuiltAmount)?.GetName(),
                            DailyCapText,
                            StringComparison.Ordinal));
                }
            }

            // 10. 月薪为零：收尾卡显示鼓励文案，不伪造金额，也不暴露金额遮罩按钮。
            Apply(
                workspace,
                new SavePreferencesChange(
                    "Second",
                    0m,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)),
                floating.Dispatcher);
            At(WorkEndUtc.AddHours(1));
            var zeroSalaryCard = CompletionCard();
            var zeroSalarySubtext = zeroSalaryCard is null
                ? null
                : VisualDescendants<InteractiveStatusTextBlock>(zeroSalaryCard).SingleOrDefault();
            var zeroSalaryRing = zeroSalarySubtext?.Parent is Grid zeroHost
                ? zeroHost.Children.OfType<Border>().SingleOrDefault()
                : null;
            var zeroSalaryPeer = zeroSalarySubtext is null
                ? null
                : UIElementAutomationPeer.CreatePeerForElement(zeroSalarySubtext);
            Check(
                failures,
                "月薪为零时收尾卡显示鼓励文案、不伪造金额且不暴露金额遮罩按钮",
                HasCompletionCard() &&
                zeroSalarySubtext is not null &&
                zeroSalaryRing is not null &&
                string.Equals(
                    zeroSalarySubtext.Text,
                    FloatingBottomStatus.CountdownEncouragementText,
                    StringComparison.Ordinal) &&
                !VisualDescendants<TextBlock>(rows).Any(block =>
                    block.Text.Contains("今日已赚", StringComparison.Ordinal) ||
                    block.Text.Contains("￥", StringComparison.Ordinal) ||
                    block.Text.Contains("¥••••", StringComparison.Ordinal)) &&
                !zeroSalarySubtext.IsInvokable &&
                !zeroSalarySubtext.Focusable &&
                !KeyboardNavigation.GetIsTabStop(zeroSalarySubtext) &&
                !zeroSalarySubtext.IsHitTestVisible &&
                zeroSalarySubtext.Cursor is null &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(zeroSalarySubtext)) &&
                zeroSalaryPeer is not null &&
                zeroSalaryPeer.GetAutomationControlType() == AutomationControlType.Text &&
                zeroSalaryPeer.GetPattern(PatternInterface.Invoke) is null &&
                IsTransparent(zeroSalaryRing.BorderBrush));
            if (zeroSalarySubtext is not null)
            {
                RaiseMouse(zeroSalarySubtext, UIElement.MouseLeftButtonDownEvent);
                RaiseMouse(zeroSalarySubtext, UIElement.MouseLeftButtonUpEvent);
                WindowValidationInput.RaiseKey(zeroSalarySubtext, Key.Enter);
                WindowValidationInput.RaiseKey(zeroSalarySubtext, Key.Space);
                Settle(floating);
                Check(
                    failures,
                    "月薪为零时点击或按键收尾卡鼓励文案不会触发假遮罩切换",
                    string.Equals(
                        zeroSalarySubtext.Text,
                        FloatingBottomStatus.CountdownEncouragementText,
                        StringComparison.Ordinal));
            }

            CheckBottomIsEncouragementWithoutAmount(
                failures,
                floating,
                floatingRootPeer,
                bottomIncome,
                bottomEncouragement,
                bottomFocusRing,
                "月薪为零收尾期间底栏");

            // 恢复正薪资后收尾卡恢复可调用金额。
            Apply(
                workspace,
                new SavePreferencesChange(
                    "Second",
                    FixtureMonthlySalary,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)),
                floating.Dispatcher);
            At(WorkEndUtc.AddHours(1));
            var restoredAmount = CompletionCard() is { } restoredCard
                ? VisualDescendants<InteractiveStatusTextBlock>(restoredCard).SingleOrDefault()
                : null;
            Check(
                failures,
                "恢复正薪资后收尾卡金额恢复为可调用遮罩入口且底栏保持鼓励语",
                restoredAmount is not null &&
                restoredAmount.IsInvokable &&
                string.Equals(restoredAmount.Text, DailyCapText, StringComparison.Ordinal) &&
                bottomIncome.Visibility == Visibility.Collapsed &&
                bottomEncouragement.Visibility == Visibility.Visible);

            // 11. 任意筛选激活抑制收尾卡（含倒计时筛选与便签搜索），此时底栏恢复承载金额入口；清除筛选恢复收尾。
            workspace.ApplyFilter(workspace.Snapshot.Filter with { UntaggedOnly = true });
            At(WorkEndUtc.AddHours(1));
            Check(
                failures,
                "无标签筛选激活时收尾卡退出且底栏恢复显示金额入口",
                !HasCompletionCard() &&
                floating.FloatingCountdownCount == 1 &&
                bottomIncome.Visibility == Visibility.Visible &&
                string.Equals(bottomIncome.Text, DailyCapText, StringComparison.Ordinal) &&
                UIElementAutomationPeer.CreatePeerForElement(bottomIncome)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider);

            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { NoteSearch = "便签词" });
            At(WorkEndUtc.AddHours(1));
            Check(
                failures,
                "仅便签搜索筛选激活时收尾卡同样退出且底栏显示金额",
                !HasCompletionCard() &&
                bottomIncome.Visibility == Visibility.Visible &&
                string.Equals(bottomIncome.Text, DailyCapText, StringComparison.Ordinal));

            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            At(WorkEndUtc.AddHours(1));
            Check(
                failures,
                "清除筛选后收尾卡恢复且底栏回到鼓励语",
                HasCompletionCard() &&
                bottomIncome.Visibility == Visibility.Collapsed &&
                bottomEncouragement.Visibility == Visibility.Visible);

            // 12. 北京时间次日零点结束收尾、非工作日与未覆盖年份均不显示收尾。
            At(BeforeMidnightUtc);
            Check(
                failures,
                "北京时间 23:59:59 仍处于当日收尾期间且底栏为鼓励语",
                HasCompletionCard() &&
                bottomIncome.Visibility == Visibility.Collapsed &&
                bottomEncouragement.Visibility == Visibility.Visible);

            At(NextDayMidnightUtc);
            Check(
                failures,
                "北京时间次日 00:00:00 结束昨日收尾，恢复距离下班与归零底栏金额",
                !HasCompletionCard() &&
                VisualDescendants<TextBlock>(rows).Any(block => block.Text == "距离下班") &&
                bottomIncome.Visibility == Visibility.Visible &&
                string.Equals(bottomIncome.Text, ZeroDayEarningsText, StringComparison.Ordinal) &&
                bottomEncouragement.Visibility == Visibility.Collapsed);

            At(NonWorkdayEveningUtc);
            Check(
                failures,
                "非工作日下班后时段不显示收尾卡",
                !HasCompletionCard());

            clock.UtcNow = UncoveredYearEveningUtc;
            WaitForTask(calendar.RefreshAsync(UncoveredYearEveningUtc, CancellationToken.None), floating);
            floating.UpdateTime(UncoveredYearEveningUtc);
            Settle(floating);
            Check(
                failures,
                "节假日日历不覆盖当年时不显示收尾卡，底栏显示日历未就绪状态",
                !HasCompletionCard() &&
                bottomIncome.Visibility == Visibility.Visible &&
                string.Equals(bottomIncome.Text, FloatingBottomStatus.CalendarUnavailableText, StringComparison.Ordinal) &&
                UIElementAutomationPeer.CreatePeerForElement(bottomIncome)?.GetPattern(PatternInterface.Invoke) is null &&
                bottomEncouragement.Visibility == Visibility.Collapsed);

            Check(
                failures,
                "整个收尾验证流程结束后设置文件与初始夹具保持一致",
                File.ReadAllBytes(settingsPath).SequenceEqual(savedSettingsBytes) &&
                string.Equals(
                    SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                    savedCanonicalSettings,
                    StringComparison.Ordinal));

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(string.Join("；", failures));
            }
        }
        finally
        {
            if (floating is not null)
            {
                floating.Close();
                WindowValidationInput.Pump(floating.Dispatcher);
            }

            DeleteSettingsFiles(settingsPath);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void CheckBottomIsEncouragementWithoutAmount(
        ICollection<string> failures,
        FloatingWindow floating,
        AutomationPeer rootPeer,
        TextBlock bottomIncome,
        SilentTextBlock bottomEncouragement,
        Border bottomFocusRing,
        string context)
    {
        var bottomPeer = UIElementAutomationPeer.CreatePeerForElement(bottomIncome);
        var bottomHost = bottomIncome.Parent as DependencyObject;
        var bottomTexts = bottomHost is null
            ? Array.Empty<string>()
            : VisualDescendants<TextBlock>(bottomHost).Select(block => block.Text ?? string.Empty).ToArray();
        Check(
            failures,
            $"{context}显示鼓励语、不含金额文字且焦点环透明",
            bottomEncouragement.Visibility == Visibility.Visible &&
            !bottomEncouragement.IsHitTestVisible &&
            string.Equals(bottomEncouragement.Text, FloatingBottomStatus.CountdownEncouragementText, StringComparison.Ordinal) &&
            string.Equals(floating.IncomeText, FloatingBottomStatus.CountdownEncouragementText, StringComparison.Ordinal) &&
            bottomIncome.Visibility == Visibility.Collapsed &&
            string.IsNullOrEmpty(bottomIncome.Text) &&
            string.IsNullOrEmpty(AutomationProperties.GetName(bottomIncome)) &&
            !bottomIncome.Focusable &&
            !KeyboardNavigation.GetIsTabStop(bottomIncome) &&
            !bottomIncome.IsHitTestVisible &&
            IsTransparent(bottomFocusRing.BorderBrush) &&
            bottomTexts.All(text =>
                !text.Contains("今日已赚", StringComparison.Ordinal) &&
                !text.Contains(DailyCapDigits, StringComparison.Ordinal) &&
                !text.Contains("¥••••", StringComparison.Ordinal)));
        Check(
            failures,
            $"{context}按 #316 鼓励语规则退出自动化树",
            UIElementAutomationPeer.CreatePeerForElement(bottomEncouragement) is null &&
            !RawTreeContains(rootPeer, bottomEncouragement) &&
            (bottomPeer is null || (
                !bottomPeer.IsControlElement() &&
                !bottomPeer.IsContentElement() &&
                bottomPeer.GetPattern(PatternInterface.Invoke) is null &&
                string.IsNullOrEmpty(bottomPeer.GetName()) &&
                string.IsNullOrEmpty(bottomPeer.GetHelpText()))));
    }

    private static void CheckSubtreeMaskedWithoutLeak(
        ICollection<string> failures,
        FloatingWindow floating,
        AutomationPeer rootPeer,
        string forbiddenDigits,
        string context)
    {
        var pending = new Queue<AutomationPeer>();
        var seen = new HashSet<AutomationPeer>();
        var sawMaskedPeer = false;
        pending.Enqueue(rootPeer);
        while (pending.TryDequeue(out var peer))
        {
            if (!seen.Add(peer))
            {
                continue;
            }

            var name = peer.GetName() ?? string.Empty;
            var texts = new List<string>
            {
                name,
                peer.GetHelpText() ?? string.Empty,
                peer.GetItemStatus() ?? string.Empty
            };
            if (peer.GetPattern(PatternInterface.Text) is ITextProvider textProvider)
            {
                texts.Add(textProvider.DocumentRange.GetText(-1) ?? string.Empty);
            }

            if (peer.GetPattern(PatternInterface.Value) is IValueProvider valueProvider)
            {
                texts.Add(valueProvider.Value ?? string.Empty);
            }

            if (string.Equals(name, FloatingBottomStatus.HiddenEarningsText, StringComparison.Ordinal))
            {
                sawMaskedPeer = true;
            }

            if (texts.Any(value => value.Contains(forbiddenDigits, StringComparison.Ordinal)))
            {
                failures.Add($"{context}：自动化子树泄露了未遮罩金额「{forbiddenDigits}」");
                return;
            }

            foreach (var child in peer.GetChildren() ?? Enumerable.Empty<AutomationPeer>())
            {
                pending.Enqueue(child);
            }
        }

        if (!sawMaskedPeer)
        {
            failures.Add($"{context}：自动化树未提供遮罩金额名称");
        }

        foreach (var element in VisualDescendants<FrameworkElement>(floating))
        {
            var tooltip = Convert.ToString(element.ToolTip, CultureInfo.InvariantCulture) ?? string.Empty;
            if (tooltip.Contains(forbiddenDigits, StringComparison.Ordinal))
            {
                failures.Add($"{context}：ToolTip 泄露了未遮罩金额「{forbiddenDigits}」");
                return;
            }
        }
    }

    private static bool RawTreeContains(AutomationPeer root, UIElement target)
    {
        var pending = new Queue<AutomationPeer>();
        var seen = new HashSet<AutomationPeer>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var peer))
        {
            if (!seen.Add(peer))
            {
                continue;
            }

            if (peer is UIElementAutomationPeer uiPeer && ReferenceEquals(uiPeer.Owner, target))
            {
                return true;
            }

            foreach (var child in peer.GetChildren() ?? Enumerable.Empty<AutomationPeer>())
            {
                pending.Enqueue(child);
            }
        }

        return false;
    }

    private static void RaiseMouse(UIElement element, RoutedEvent routedEvent)
    {
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = routedEvent
        });
    }

    private static bool IsTransparent(Brush? brush) =>
        brush is null || (brush is SolidColorBrush solid && solid.Color.A == 0);

    private static bool BrushColorEquals(Brush? actual, Brush? expected) =>
        actual is SolidColorBrush actualSolid &&
        expected is SolidColorBrush expectedSolid &&
        actualSolid.Color == expectedSolid.Color;

    private static void WaitForTask(Task task, FloatingWindow floating)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            WindowValidationInput.Pump(floating.Dispatcher);
            Thread.Sleep(5);
        }

        if (!task.IsCompleted)
        {
            throw new TimeoutException("等待节假日日历刷新任务超时。");
        }

        WindowValidationInput.Pump(floating.Dispatcher);
    }

    private static string BuildWeekdayCalendarJson(int year)
    {
        var builder = new StringBuilder();
        builder.Append("{\"days\":[");
        var date = new DateOnly(year, 1, 1);
        var first = true;
        while (date.Year == year)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            var isWorkday = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            builder.Append("{\"date\":\"")
                .Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append("\",\"is_workday\":")
                .Append(isWorkday ? "true" : "false")
                .Append('}');
            date = date.AddDays(1);
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static void Show(FloatingWindow floating)
    {
        floating.ShowInTaskbar = false;
        floating.ShowActivated = false;
        floating.WindowStartupLocation = WindowStartupLocation.Manual;
        floating.Left = -20_000;
        floating.Top = -20_000;
        floating.Opacity = 0;
        floating.Topmost = false;
        var contentRendered = false;
        EventHandler rendered = (_, _) => contentRendered = true;
        floating.ContentRendered += rendered;
        try
        {
            floating.Show();
            var firstFrame = WindowValidationInput.PumpRenderedFrames(
                floating.Dispatcher,
                2000,
                () => contentRendered);
            Settle(floating);
            if (!contentRendered || !firstFrame.Completed)
            {
                throw new InvalidOperationException("悬浮窗下班收尾验证场景未完成真实首帧。");
            }
        }
        finally
        {
            floating.ContentRendered -= rendered;
        }
    }

    private static void Settle(FloatingWindow floating)
    {
        WindowValidationInput.Pump(floating.Dispatcher);
        floating.UpdateLayout();
        WindowValidationInput.Pump(floating.Dispatcher);
    }

    private static T RequiredByName<T>(Window window, string elementName) where T : FrameworkElement
    {
        var matches = VisualDescendants<T>(window)
            .Where(element => string.Equals(element.Name, elementName, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"悬浮窗下班收尾控件定位失败：{elementName}，matches={matches.Length}。");
    }

    private static void Check(ICollection<string> failures, string description, bool passed)
    {
        if (!passed)
        {
            failures.Add(description);
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow.ToUniversalTime();
    }

    private sealed class NoopManagerHost : IManagerWorkspaceHost
    {
        public void Reveal(WorkspaceKind workspace)
        {
        }

        public void RevealPreferences(WorkspaceKind workspace)
        {
        }
    }
}
