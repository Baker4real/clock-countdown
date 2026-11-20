using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #316：以真实悬浮窗验证底部状态由两个互斥公开元素承载——鼓励语无自动化 peer 且退出自动化树，
// 金额与节假日日历状态留在自动化树中且可访问名与可见文案同步；金额遮罩不向自动化子树泄露，
// 指针/键盘/Invoke 均可切换；三模式反复切换后仍保持一致；直播区播报走公开 provider 探测。
internal static class FloatingBottomStatusAutomationScenario
{
    private const decimal FixtureMonthlySalary = 4910.04m; // 2030-07 有 23 个工作日，日薪精确 213.48 元
    private const string InitialAmountDigits = "53.37";
    private const string InitialAmountText = "今日已赚 ￥53.37";
    private const string LaterAmountDigits = "106.74";
    private const string LaterAmountText = "今日已赚 ￥106.74";
    private const string LoadingStatusText = "节假日日历正在加载";

    private static readonly DateTimeOffset QuarterDayUtc =
        new DateTimeOffset(2030, 7, 15, 11, 15, 0, TimeSpan.FromHours(8)).ToUniversalTime();
    private static readonly DateTimeOffset HalfDayUtc =
        new DateTimeOffset(2030, 7, 15, 13, 30, 0, TimeSpan.FromHours(8)).ToUniversalTime();
    private static readonly DateTimeOffset UnavailableYearUtc =
        new DateTimeOffset(2031, 7, 15, 11, 15, 0, TimeSpan.FromHours(8)).ToUniversalTime();

    internal static void Run()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-bottom-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var clock = new MutableClock(QuarterDayUtc);
        TaskCompletionSource<string>? pendingYear2032 = null;
        FloatingWindow? floating = null;
        var failures = new List<string>();

        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var initialPreferences = workspace.Snapshot.Settings.Preferences;
            Apply(
                workspace,
                new SavePreferencesChange(
                    initialPreferences.DisplayPrecision,
                    FixtureMonthlySalary,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)),
                System.Windows.Threading.Dispatcher.CurrentDispatcher);

            var year2030Json = BuildWeekdayCalendarJson(2030);
            using var calendar = new HolidayCalendar(
                tempRoot,
                (year, _) => year switch
                {
                    2030 => Task.FromResult(year2030Json),
                    2032 => (pendingYear2032 ??= new TaskCompletionSource<string>(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task,
                    _ => Task.FromException<string>(new HttpRequestException($"no fixture for {year}"))
                });

            floating = new FloatingWindow(
                workspace,
                calendar,
                new NoopManagerHost(),
                AppContext.BaseDirectory);
            Show(floating);

            var statusElement = RequiredByName<TextBlock>(floating, "FloatingIncome");
            var encouragementElement = RequiredByName<SilentTextBlock>(floating, "FloatingEncouragement");
            var focusRing = RequiredByName<Border>(floating, "FloatingIncomeFocusRing");
            var floatingRootPeer = UIElementAutomationPeer.CreatePeerForElement(floating)
                ?? throw new InvalidOperationException("悬浮窗缺少根 AutomationPeer。");

            // 鼓励语元素永远不产生自动化 peer。
            Check(
                failures,
                "鼓励语元素自身不产生 AutomationPeer",
                UIElementAutomationPeer.CreatePeerForElement(encouragementElement) is null &&
                !RawTreeContains(floatingRootPeer, encouragementElement));

            // 1. 节假日日历加载中（初始 Pending 状态）
            floating.UpdateTime(QuarterDayUtc);
            Settle(floating);
            CheckStatusMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                LoadingStatusText,
                "日历初始加载中");

            // 2. 切入日历就绪 + 正月薪 -> 金额模式（未遮罩）
            WaitForTask(calendar.RefreshAsync(QuarterDayUtc, CancellationToken.None), floating);
            floating.UpdateTime(QuarterDayUtc);
            Settle(floating);
            CheckEarningsMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                InitialAmountText,
                "日历就绪后进入未遮罩金额模式");

            // 指针取消路径：无按下释放、按下后移出都不切换遮罩。
            RaiseMouse(statusElement, UIElement.MouseLeftButtonUpEvent);
            Settle(floating);
            Check(
                failures,
                "未按下直接释放鼠标不切换遮罩",
                string.Equals(statusElement.Text, InitialAmountText, StringComparison.Ordinal));

            RaiseMouse(statusElement, UIElement.MouseLeftButtonDownEvent);
            statusElement.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = UIElement.MouseLeaveEvent
            });
            RaiseMouse(statusElement, UIElement.MouseLeftButtonUpEvent);
            Settle(floating);
            Check(
                failures,
                "按下后移出区域再释放不切换遮罩",
                string.Equals(statusElement.Text, InitialAmountText, StringComparison.Ordinal));

            // 指针点击切换为遮罩态：子树与 ToolTip 均不含未遮罩金额，鼠标点击不残留键盘焦点环。
            RaiseMouse(statusElement, UIElement.MouseLeftButtonDownEvent);
            RaiseMouse(statusElement, UIElement.MouseLeftButtonUpEvent);
            Settle(floating);
            CheckEarningsMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                FloatingBottomStatus.HiddenEarningsText,
                "指针点击切换到金额遮罩态");
            Check(
                failures,
                "指针点击切换遮罩后不残留键盘焦点环",
                IsTransparent(focusRing.BorderBrush));
            CheckSubtreeDoesNotContain(
                failures,
                floating,
                floatingRootPeer,
                new[] { InitialAmountDigits, LaterAmountDigits },
                "指针遮罩态自动化子树与 ToolTip");

            // 键盘 Enter 切换回未遮罩态，并点亮 2 DIP 暖色焦点环。
            floating.Activate();
            statusElement.Focus();
            Settle(floating);
            WindowValidationInput.RaiseKey(statusElement, Key.Enter);
            Settle(floating);
            CheckEarningsMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                InitialAmountText,
                "键盘 Enter 切回未遮罩金额");
            Check(
                failures,
                "键盘 Enter 操作后焦点环可见且为 2 DIP",
                statusElement.IsKeyboardFocused &&
                focusRing.BorderThickness == new Thickness(2) &&
                !IsTransparent(focusRing.BorderBrush));

            // 键盘 Space 再次遮罩，并推进时间确认新金额同样不泄露。
            WindowValidationInput.RaiseKey(statusElement, Key.Space);
            clock.UtcNow = HalfDayUtc;
            floating.UpdateTime(HalfDayUtc);
            Settle(floating);
            CheckEarningsMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                FloatingBottomStatus.HiddenEarningsText,
                "键盘 Space 遮罩并在时间推进后保持遮罩");
            CheckSubtreeDoesNotContain(
                failures,
                floating,
                floatingRootPeer,
                new[] { InitialAmountDigits, LaterAmountDigits },
                "键盘 Space 遮罩推进时间后自动化子树");

            // 自动化 IInvokeProvider 切换回未遮罩，读到最新金额。
            var invokeProvider = UIElementAutomationPeer.CreatePeerForElement(statusElement)
                ?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            Check(failures, "金额模式提供 IInvokeProvider", invokeProvider is not null);
            invokeProvider?.Invoke();
            Settle(floating);
            CheckEarningsMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                LaterAmountText,
                "自动化 Invoke 切回未遮罩最新金额");

            // 重新遮罩，再开始三模式往返切换。
            WindowValidationInput.RaiseKey(statusElement, Key.Space);
            Settle(floating);
            CheckEarningsMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                FloatingBottomStatus.HiddenEarningsText,
                "三模式往返前进入遮罩金额");

            // 3. 金额 -> 鼓励语（倒计时与便签双工作区）
            Apply(
                workspace,
                new SavePreferencesChange(
                    initialPreferences.DisplayPrecision,
                    0m,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)),
                floating.Dispatcher);
            floating.UpdateTime(HalfDayUtc);
            Settle(floating);
            CheckEncouragementMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                focusRing,
                FloatingBottomStatus.CountdownEncouragementText,
                new[] { InitialAmountDigits, LaterAmountDigits, "今日已赚" },
                "倒计时工作区鼓励语模式");

            floating.ShowWorkspace(WorkspaceKind.Notes);
            floating.UpdateTime(HalfDayUtc);
            Settle(floating);
            CheckEncouragementMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                focusRing,
                FloatingBottomStatus.NotesEncouragementText,
                new[] { InitialAmountDigits, LaterAmountDigits, "今日已赚" },
                "便签工作区鼓励语模式");

            // 4. 鼓励语 -> 日历未就绪（零薪资与正薪资均保持日历状态）
            floating.ShowWorkspace(WorkspaceKind.Countdowns);
            clock.UtcNow = UnavailableYearUtc;
            WaitForTask(calendar.RefreshAsync(UnavailableYearUtc, CancellationToken.None), floating);
            floating.UpdateTime(UnavailableYearUtc);
            Settle(floating);
            CheckStatusMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                FloatingBottomStatus.CalendarUnavailableText,
                "零薪资下进入日历未就绪状态");

            Apply(
                workspace,
                new SavePreferencesChange(
                    initialPreferences.DisplayPrecision,
                    FixtureMonthlySalary,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)),
                floating.Dispatcher);
            floating.UpdateTime(UnavailableYearUtc);
            Settle(floating);
            CheckStatusMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                FloatingBottomStatus.CalendarUnavailableText,
                "正薪资下保持日历未就绪状态");

            // 5. 日历未就绪 -> 日历加载中（异步下载挂起态）
            var loadingYearUtc = new DateTimeOffset(2032, 7, 15, 11, 15, 0, TimeSpan.FromHours(8)).ToUniversalTime();
            clock.UtcNow = loadingYearUtc;
            var pendingRefresh = calendar.RefreshAsync(loadingYearUtc, CancellationToken.None);
            floating.UpdateTime(loadingYearUtc);
            Settle(floating);
            CheckStatusMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                LoadingStatusText,
                "切到加载中年份显示日历加载状态");

            // 6. 日历加载中 -> 切回就绪年份（遮罩态仍保留且不泄密），再切鼓励语 -> 金额 -> 未遮罩
            clock.UtcNow = HalfDayUtc;
            WaitForTask(calendar.RefreshAsync(HalfDayUtc, CancellationToken.None), floating);
            pendingYear2032?.TrySetCanceled();
            WaitForTask(pendingRefresh, floating);
            floating.UpdateTime(HalfDayUtc);
            Settle(floating);
            CheckEarningsMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                FloatingBottomStatus.HiddenEarningsText,
                "往返切回就绪金额后保留遮罩且可访问名同步");
            CheckSubtreeDoesNotContain(
                failures,
                floating,
                floatingRootPeer,
                new[] { InitialAmountDigits, LaterAmountDigits },
                "往返切回遮罩金额后自动化子树");

            Apply(
                workspace,
                new SavePreferencesChange(
                    initialPreferences.DisplayPrecision,
                    0m,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)),
                floating.Dispatcher);
            floating.UpdateTime(HalfDayUtc);
            Settle(floating);
            CheckEncouragementMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                focusRing,
                FloatingBottomStatus.CountdownEncouragementText,
                new[] { InitialAmountDigits, LaterAmountDigits, "今日已赚" },
                "二次切入鼓励语模式");

            Apply(
                workspace,
                new SavePreferencesChange(
                    initialPreferences.DisplayPrecision,
                    FixtureMonthlySalary,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)),
                floating.Dispatcher);
            floating.UpdateTime(HalfDayUtc);
            Settle(floating);
            WindowValidationInput.RaiseKey(statusElement, Key.Space);
            Settle(floating);
            CheckEarningsMode(
                failures,
                floating,
                floatingRootPeer,
                statusElement,
                encouragementElement,
                LaterAmountText,
                "三模式往返后键盘切回未遮罩金额且名称同步");

            // 7. 直播区播报（公开 AutomationPeer.ProviderFromPeer 探测路径）：折叠错误行变为可见并播报，同文案不重复播报。
            VerifyLiveRegionAnnouncement(floating, failures);

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(string.Join("；", failures));
            }
        }
        finally
        {
            pendingYear2032?.TrySetCanceled();
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

    private static void VerifyLiveRegionAnnouncement(FloatingWindow floating, ICollection<string> failures)
    {
        var announced = new List<(string Name, string Text, AutomationLiveSetting Live)>();
        void OnAnnounced(TextBlock block) => announced.Add((
            AutomationProperties.GetName(block) ?? string.Empty,
            block.Text ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        UiAccessibility.Announced += OnAnnounced;
        try
        {
            floating.ShowWorkspace(WorkspaceKind.Notes);
            Settle(floating);
            var toggle = RequiredByName<Button>(floating, "FloatingAddNoteToggle");
            var addButton = RequiredByName<Button>(floating, "FloatingAddNote");
            var errorBlock = RequiredByName<TextBlock>(floating, "FloatingNoteTextError");

            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Settle(floating);
            addButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Settle(floating);

            var expectedError = errorBlock.Text;
            var errorPeer = UIElementAutomationPeer.FromElement(errorBlock);
            Check(
                failures,
                "直播区折叠错误行显露后经公开路径播报并建立自动化名与父子连接",
                !string.IsNullOrWhiteSpace(expectedError) &&
                errorBlock.Visibility == Visibility.Visible &&
                errorPeer is not null &&
                errorPeer.GetParent() is not null &&
                string.Equals(errorPeer.GetName(), expectedError, StringComparison.Ordinal) &&
                announced.Any(entry =>
                    string.Equals(entry.Text, expectedError, StringComparison.Ordinal) &&
                    string.Equals(entry.Name, expectedError, StringComparison.Ordinal) &&
                    entry.Live == AutomationLiveSetting.Polite));

            var countAfterFirst = announced.Count;
            UiAccessibility.Announce(errorBlock, expectedError);
            Settle(floating);
            Check(
                failures,
                "直播区相同错误文案不重复播报",
                announced.Count == countAfterFirst);
        }
        finally
        {
            UiAccessibility.Announced -= OnAnnounced;
        }
    }

    private static void CheckEarningsMode(
        ICollection<string> failures,
        FloatingWindow floating,
        AutomationPeer rootPeer,
        TextBlock statusElement,
        SilentTextBlock encouragementElement,
        string expectedText,
        string context)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(statusElement);
        Check(
            failures,
            $"{context}：两元素互斥显示且金额文案一致",
            statusElement.Visibility == Visibility.Visible &&
            encouragementElement.Visibility == Visibility.Collapsed &&
            string.IsNullOrEmpty(encouragementElement.Text) &&
            string.Equals(statusElement.Text, expectedText, StringComparison.Ordinal) &&
            string.Equals(floating.IncomeText, expectedText, StringComparison.Ordinal));
        Check(
            failures,
            $"{context}：金额元素留在自动化树中、角色为 Button、可访问名同步且支持 Invoke/键盘焦点",
            peer is not null &&
            peer.IsControlElement() &&
            peer.IsContentElement() &&
            peer.GetAutomationControlType() == AutomationControlType.Button &&
            peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider &&
            string.Equals(peer.GetName(), expectedText, StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetName(statusElement), expectedText, StringComparison.Ordinal) &&
            statusElement.Focusable &&
            KeyboardNavigation.GetIsTabStop(statusElement) &&
            RawTreeContains(rootPeer, statusElement) &&
            UIElementAutomationPeer.CreatePeerForElement(encouragementElement) is null &&
            !RawTreeContains(rootPeer, encouragementElement));
    }

    private static void CheckStatusMode(
        ICollection<string> failures,
        FloatingWindow floating,
        AutomationPeer rootPeer,
        TextBlock statusElement,
        SilentTextBlock encouragementElement,
        string expectedText,
        string context)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(statusElement);
        Check(
            failures,
            $"{context}：两元素互斥显示且日历状态文案一致",
            statusElement.Visibility == Visibility.Visible &&
            encouragementElement.Visibility == Visibility.Collapsed &&
            string.IsNullOrEmpty(encouragementElement.Text) &&
            string.Equals(statusElement.Text, expectedText, StringComparison.Ordinal) &&
            string.Equals(floating.IncomeText, expectedText, StringComparison.Ordinal));
        Check(
            failures,
            $"{context}：日历状态留在自动化树中、角色为 Text、名称同步且不可调用/不可 Tab 聚焦",
            peer is not null &&
            peer.IsControlElement() &&
            peer.IsContentElement() &&
            peer.GetAutomationControlType() == AutomationControlType.Text &&
            peer.GetPattern(PatternInterface.Invoke) is null &&
            string.Equals(peer.GetName(), expectedText, StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetName(statusElement), expectedText, StringComparison.Ordinal) &&
            !statusElement.Focusable &&
            !KeyboardNavigation.GetIsTabStop(statusElement) &&
            RawTreeContains(rootPeer, statusElement) &&
            UIElementAutomationPeer.CreatePeerForElement(encouragementElement) is null &&
            !RawTreeContains(rootPeer, encouragementElement));
    }

    private static void CheckEncouragementMode(
        ICollection<string> failures,
        FloatingWindow floating,
        AutomationPeer rootPeer,
        TextBlock statusElement,
        SilentTextBlock encouragementElement,
        Border focusRing,
        string expectedEncouragement,
        IReadOnlyList<string> forbiddenInAutomationTree,
        string context)
    {
        var statusPeer = UIElementAutomationPeer.CreatePeerForElement(statusElement);
        Check(
            failures,
            $"{context}：两元素互斥显示，鼓励语可见不可命中，状态元素折叠清空且无焦点环",
            encouragementElement.Visibility == Visibility.Visible &&
            !encouragementElement.IsHitTestVisible &&
            string.Equals(encouragementElement.Text, expectedEncouragement, StringComparison.Ordinal) &&
            string.Equals(floating.IncomeText, expectedEncouragement, StringComparison.Ordinal) &&
            statusElement.Visibility == Visibility.Collapsed &&
            string.IsNullOrEmpty(statusElement.Text) &&
            string.IsNullOrEmpty(AutomationProperties.GetName(statusElement)) &&
            !statusElement.Focusable &&
            !KeyboardNavigation.GetIsTabStop(statusElement) &&
            !statusElement.IsHitTestVisible &&
            IsTransparent(focusRing.BorderBrush));
        Check(
            failures,
            $"{context}：鼓励语无 peer 且不在自动化树中，折叠状态元素退出控件与内容视图",
            UIElementAutomationPeer.CreatePeerForElement(encouragementElement) is null &&
            !RawTreeContains(rootPeer, encouragementElement) &&
            (statusPeer is null || (
                !statusPeer.IsControlElement() &&
                !statusPeer.IsContentElement() &&
                statusPeer.GetPattern(PatternInterface.Invoke) is null &&
                string.IsNullOrEmpty(statusPeer.GetName()) &&
                string.IsNullOrEmpty(statusPeer.GetHelpText()))));

        var allForbidden = forbiddenInAutomationTree.Append(expectedEncouragement).ToArray();
        CheckSubtreeDoesNotContain(failures, floating, rootPeer, allForbidden, context);
    }

    private static void CheckSubtreeDoesNotContain(
        ICollection<string> failures,
        FloatingWindow floating,
        AutomationPeer rootPeer,
        IReadOnlyList<string> forbiddenSnippets,
        string context)
    {
        var pending = new Queue<AutomationPeer>();
        var seen = new HashSet<AutomationPeer>();
        pending.Enqueue(rootPeer);
        while (pending.TryDequeue(out var peer))
        {
            if (!seen.Add(peer))
            {
                continue;
            }

            var texts = new List<string>
            {
                peer.GetName() ?? string.Empty,
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

            foreach (var snippet in forbiddenSnippets)
            {
                if (texts.Any(value => value.Contains(snippet, StringComparison.Ordinal)))
                {
                    failures.Add($"{context}：自动化子树泄露了「{snippet}」");
                    return;
                }
            }

            foreach (var child in peer.GetChildren() ?? Enumerable.Empty<AutomationPeer>())
            {
                pending.Enqueue(child);
            }
        }

        foreach (var element in VisualDescendants<FrameworkElement>(floating))
        {
            var tooltip = Convert.ToString(element.ToolTip, CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (var snippet in forbiddenSnippets)
            {
                if (tooltip.Contains(snippet, StringComparison.Ordinal))
                {
                    failures.Add($"{context}：ToolTip 泄露了「{snippet}」");
                    return;
                }
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
                throw new InvalidOperationException("悬浮窗底部状态自动化场景未完成真实首帧。");
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
            : throw new InvalidOperationException($"悬浮窗底部状态控件定位失败：{elementName}，matches={matches.Length}。");
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
