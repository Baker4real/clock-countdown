using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #320：沿 #297 的真实窗口、公共视觉树与自动化 peer seam 验收稍后提醒菜单按钮、两行动作区与懒创建自定义输入。
internal static class ReminderCenterSnoozeMenuScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 10, 1, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedMenuHeaders =
    [
        "10 分钟后",
        "30 分钟后",
        "1 小时后",
        "明天 09:00",
        "自定义"
    ];

    private static string? _captureDirectory;
    private static bool _captureDirectoryInitialized;

    internal static void RunTwoRowLayoutAndLazyCustomSnooze()
    {
        var captureDirectory = GetCaptureDirectory();
        RunLayoutAndLazyCreationScenario(captureDirectory);
        RunInvalidCustomSnoozeErrorAdjacencyScenario(captureDirectory);
    }

    internal static void RunSnoozeMenuBehaviorAndDomainSemantics()
    {
        var captureDirectory = GetCaptureDirectory();
        RunDeliveryGateCloseGuardAndMenuInteractionScenario(captureDirectory);
        RunAllPresetsAndFailureRetryScenario();
    }

    private static string? GetCaptureDirectory()
    {
        if (!_captureDirectoryInitialized)
        {
            _captureDirectory = CreateCaptureDirectory();
            _captureDirectoryInitialized = true;
        }

        return _captureDirectory;
    }

    private static void RunLayoutAndLazyCreationScenario(string? captureDirectory)
    {
        var noteKey = new ActiveReminderOccurrenceKey("snooze-layout-note-rule", FixtureUtc.AddMinutes(-5));
        var countdownKey = new ActiveReminderOccurrenceKey("snooze-layout-countdown-rule", FixtureUtc.AddMinutes(-2));
        var extraNoteKey = new ActiveReminderOccurrenceKey("snooze-layout-note-extra", FixtureUtc.AddMinutes(-1));
        var center = new ReminderCenterWindow();
        try
        {
            center.AddEntry(new ReminderCenterItem(
                noteKey,
                new NoteRecord(
                    "snooze-layout-note",
                    "核对发版前接口回归结果",
                    "2038-07-10",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                0,
                0));
            center.AddEntry(new ReminderCenterItem(
                countdownKey,
                new CountdownReminderSource(new CountdownRecord(
                    "snooze-layout-countdown",
                    "季度汇报截止",
                    FixtureUtc.AddDays(2),
                    Array.Empty<string>())),
                1,
                0));
            center.AddEntry(new ReminderCenterItem(
                extraNoteKey,
                new NoteRecord(
                    "snooze-layout-note-extra-record",
                    "同步下周排期确认",
                    "2038-07-10",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                2,
                0));

            PrepareOffscreen(center);
            center.Show();
            center.SetDeliveryReady(noteKey);
            center.SetDeliveryReady(countdownKey);
            center.SetDeliveryReady(extraNoteKey);
            center.UpdateTime(FixtureUtc);
            Settle(center);

            Require(Math.Abs(center.Width - 460) < 0.01 && Math.Abs(center.MinWidth - 400) < 0.01,
                $"提醒中心默认宽与最小宽应为 460/400 DIP，实际为 {center.Width:0.##}/{center.MinWidth:0.##}。");
            var scroller = VisualDescendants<ScrollViewer>(center)
                .Single(candidate => AutomationProperties.GetName(candidate) == "提醒条目列表");
            Require(scroller.ScrollableHeight > 0,
                "三条目夹具应形成真实纵向滚动条，以验证带滚动条时两行布局仍不折行不裁切。");

            // 不得再存在独立的「稍后提醒时间」下拉，且不得预先创建自定义稍后输入控件。
            Require(!VisualDescendants<ComboBox>(center).Any(),
                "条目初始状态不得存在独立时长下拉或预创建的自定义时间下拉控件。");
            Require(!VisualDescendants<DatePicker>(center).Any(),
                "条目初始状态不得预先创建自定义日期选择器控件。");
            Require(!VisualDescendants<Button>(center).Any(button =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(button),
                        "ReminderEntryCustomSnoozeConfirm",
                        StringComparison.Ordinal)),
                "条目初始状态不得预先创建自定义稍后确认按钮。");

            VerifyTwoRowGeometryAtWidth(center, 460, noteKey, countdownKey);
            CaptureVisual(center, center.Content as FrameworkElement, captureDirectory, "460-two-rows");

            center.Width = 400;
            Settle(center);
            VerifyTwoRowGeometryAtWidth(center, 400, noteKey, countdownKey);
            CaptureVisual(center, center.Content as FrameworkElement, captureDirectory, "400-min-width-two-rows");

            // 第一次选择「自定义」时才懒创建日期、小时、分钟与确认按钮。
            center.Width = 460;
            Settle(center);
            var noteRow = FindRow(center, noteKey);
            var countdownRow = FindRow(center, countdownKey);
            var snoozeButton = Action(noteRow, "稍后提醒：");
            var snoozeMenu = RequireSnoozeMenu(snoozeButton);

            SelectMenuItemByHeader(center, snoozeButton, snoozeMenu, "自定义");
            Settle(center);

            var customDate = VisualDescendants<DatePicker>(noteRow)
                .SingleOrDefault(picker => AutomationProperties.GetName(picker).StartsWith("自定义稍后日期：", StringComparison.Ordinal));
            var customHour = VisualDescendants<ComboBox>(noteRow)
                .SingleOrDefault(combo => AutomationProperties.GetName(combo).StartsWith("自定义稍后小时：", StringComparison.Ordinal));
            var customMinute = VisualDescendants<ComboBox>(noteRow)
                .SingleOrDefault(combo => AutomationProperties.GetName(combo).StartsWith("自定义稍后分钟：", StringComparison.Ordinal));
            var customConfirm = VisualDescendants<Button>(noteRow)
                .SingleOrDefault(button => string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderEntryCustomSnoozeConfirm",
                    StringComparison.Ordinal));
            Require(customDate is { IsVisible: true } &&
                    customHour is { IsVisible: true } &&
                    customMinute is { IsVisible: true } &&
                    customConfirm is { IsVisible: true },
                "首次选择「自定义」后必须懒创建并展开日期、小时、分钟与确认按钮。");
            Require(!VisualDescendants<DatePicker>(countdownRow).Any() &&
                    !VisualDescendants<ComboBox>(countdownRow).Any(),
                "仅选中「自定义」的条目应创建自定义输入，其他条目仍不得预创建。");

            // 再次选择「自定义」复用同一批控件实例，不重复创建。
            SelectMenuItemByHeader(center, snoozeButton, snoozeMenu, "自定义");
            Settle(center);
            Require(ReferenceEquals(customDate, VisualDescendants<DatePicker>(noteRow).Single()) &&
                    ReferenceEquals(customHour, VisualDescendants<ComboBox>(noteRow)
                        .Single(combo => AutomationProperties.GetName(combo).StartsWith("自定义稍后小时：", StringComparison.Ordinal))) &&
                    ReferenceEquals(customMinute, VisualDescendants<ComboBox>(noteRow)
                        .Single(combo => AutomationProperties.GetName(combo).StartsWith("自定义稍后分钟：", StringComparison.Ordinal))) &&
                    ReferenceEquals(customConfirm, VisualDescendants<Button>(noteRow)
                        .Single(button => string.Equals(
                            AutomationProperties.GetAutomationId(button),
                            "ReminderEntryCustomSnoozeConfirm",
                            StringComparison.Ordinal))),
                "再次选择「自定义」应复用已创建的控件，不得重复创建。");

            // 自定义区域在 460 与 400 宽下均不裁切、不重叠；未填时间直接确认时错误紧邻输入下方。
            VerifyCustomControlsGeometry(noteRow, customDate!, customHour!, customMinute!, customConfirm!, "460");
            center.Width = 400;
            Settle(center);
            VerifyCustomControlsGeometry(noteRow, customDate!, customHour!, customMinute!, customConfirm!, "400");

            center.Width = 460;
            Settle(center);
            Require(WindowValidationInput.InvokeButton(customConfirm!), "无法触发自定义稍后确认按钮。");
            Settle(center);
            var missingError = VisualDescendants<TextBlock>(noteRow).SingleOrDefault(text =>
                string.Equals(
                    AutomationProperties.GetAutomationId(text),
                    "ReminderEntryCustomError",
                    StringComparison.Ordinal));
            Require(missingError is { IsVisible: true } &&
                    missingError.Text.Contains("请选择完整", StringComparison.Ordinal),
                "自定义时间不完整时必须在输入区域旁显示错误提示。");
            VerifyErrorAdjacentBelowCustomInputs(noteRow, customDate!, customHour!, customMinute!, customConfirm!, missingError!);
        }
        finally
        {
            center.ForceClose();
            WindowValidationInput.Pump(center.Dispatcher);
        }
    }

    private static void RunInvalidCustomSnoozeErrorAdjacencyScenario(string? captureDirectory)
    {
        var dueUtc = FixtureUtc;
        var settingsPath = CreateTempSettingsPath("custom-error");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Require(workspace.Apply(new SaveNoteChange(
                null,
                "自定义过去时间错误位置探针",
                new DateTime(2038, 7, 10),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法创建自定义错误位置探针便签。");
            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ?? throw new InvalidOperationException("提醒中心未打开。");
            PrepareOffscreen(center);
            Settle(center);

            var row = FindRow(center, key);
            var snoozeButton = Action(row, "稍后提醒：");
            var snoozeMenu = RequireSnoozeMenu(snoozeButton);
            SelectMenuItemByHeader(center, snoozeButton, snoozeMenu, "自定义");
            Settle(center);

            var customDate = VisualDescendants<DatePicker>(row).Single();
            var customHour = VisualDescendants<ComboBox>(row)
                .Single(combo => AutomationProperties.GetName(combo).StartsWith("自定义稍后小时：", StringComparison.Ordinal));
            var customMinute = VisualDescendants<ComboBox>(row)
                .Single(combo => AutomationProperties.GetName(combo).StartsWith("自定义稍后分钟：", StringComparison.Ordinal));
            var customConfirm = VisualDescendants<Button>(row)
                .Single(button => string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderEntryCustomSnoozeConfirm",
                    StringComparison.Ordinal));

            var pastBeijing = ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(-10));
            customDate.SelectedDate = pastBeijing.Date;
            customHour.SelectedIndex = pastBeijing.Hour;
            customMinute.SelectedIndex = pastBeijing.Minute;
            Settle(center);

            var beforeSnapshot = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            Require(WindowValidationInput.InvokeButton(customConfirm), "无法点击自定义稍后确认按钮。");
            Settle(center);

            var error = VisualDescendants<TextBlock>(row).SingleOrDefault(text =>
                string.Equals(
                    AutomationProperties.GetAutomationId(text),
                    "ReminderEntryCustomError",
                    StringComparison.Ordinal));
            Require(error is { IsVisible: true } &&
                    string.Equals(error.Text, "自定义稍后时间必须晚于当前北京时间。", StringComparison.Ordinal),
                $"自定义过去时间未显示预期错误文案，实际为「{error?.Text ?? "<null>"}」。");
            VerifyErrorAdjacentBelowCustomInputs(row, customDate, customHour, customMinute, customConfirm, error!);
            Require(coordinator.ActiveEntryCount == 1 &&
                    center.IsSnoozeEnabled(key) &&
                    SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeSnapshot,
                "无效自定义稍后时间改变了条目或设置状态，或错误地禁用了修正。");

            Keyboard.ClearFocus();
            Settle(center);
            CaptureVisual(center, center.Content as FrameworkElement, captureDirectory, "custom-snooze-expanded-error");

            // 改为未来时间并确认，立即提交自定义稍后并收起空中心。
            var futureBeijing = ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(45));
            customDate.SelectedDate = futureBeijing.Date;
            customHour.SelectedIndex = futureBeijing.Hour;
            customMinute.SelectedIndex = futureBeijing.Minute;
            Settle(center);
            Require(WindowValidationInput.InvokeButton(customConfirm), "修正为未来时间后无法点击确认按钮。");
            Settle(center);

            var state = workspace.Snapshot.Settings.ReminderStates[rule.Id];
            Require(coordinator.ActiveEntryCount == 0 &&
                    coordinator.ActiveWindowCount == 0 &&
                    state.ActiveSnooze?.OccurrenceKey == ReminderOccurrenceKey.Create(rule, dueUtc).ToString() &&
                    state.ActiveSnooze.SnoozeUntilUtc == dueUtc.AddMinutes(45),
                "修正自定义未来时间后确认按钮未完成持久化稍后并关闭空中心。");
        }
        finally
        {
            coordinator?.Dispose();
            DeleteSettingsFiles(settingsPath);
        }
    }

    private static void RunDeliveryGateCloseGuardAndMenuInteractionScenario(string? captureDirectory)
    {
        var dueUtc = FixtureUtc;
        var settingsPath = CreateTempSettingsPath("menu-interaction");
        var blocker = settingsPath + ".tmp";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Require(workspace.Apply(new SaveNoteChange(
                null,
                "菜单交互与送达门探针",
                new DateTime(2038, 7, 10),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法创建菜单交互探针便签。");
            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);

            // 1. 送达前持久化失败：不抢焦点、禁用处置（含稍后菜单按钮）、打开来源可用、有条目拒绝关闭。
            Directory.CreateDirectory(blocker);
            var openSourceCalled = 0;
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                logger: null,
                openSource: (_, _) => openSourceCalled++);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ?? throw new InvalidOperationException("提醒中心未打开。");
            PrepareOffscreen(center);
            Settle(center);

            Require(!center.ShowActivated && !center.IsActive && !center.Topmost &&
                    (Keyboard.FocusedElement is not DependencyObject focused || !center.IsAncestorOf(focused)),
                "提醒中心到达时不得抢占激活态或键盘焦点。");
            Require(center.IsFailureVisible(key) &&
                    !center.IsAcknowledgeEnabled(key) &&
                    !center.IsSnoozeEnabled(key) &&
                    !center.IsCompleteEnabled(key) &&
                    center.IsOpenSourceEnabled(key),
                "送达确认前必须禁用知道了、稍后提醒与完成便签，并保留打开来源可用。");
            var row = FindRow(center, key);
            var snoozeButton = Action(row, "稍后提醒：");
            var snoozeMenu = RequireSnoozeMenu(snoozeButton);
            Require(!snoozeButton.IsEnabled && !snoozeMenu.IsOpen,
                "送达失败期间稍后菜单按钮必须保持禁用且菜单关闭。");
            Require(WindowValidationInput.InvokeButton(Action(row, "打开便签来源：")),
                "送达失败期间无法激活「打开来源」。");
            Settle(center);
            Require(openSourceCalled == 1,
                "送达失败期间「打开来源」必须始终可用。");
            center.Close();
            Settle(center);
            Require(center.IsVisible && coordinator.ActiveEntryCount == 1,
                "有条目时提醒中心必须拒绝直接关闭。");

            // 解除阻塞并重试送达成功，动作全部恢复可用。
            DeleteDirectoryIfEmpty(blocker);
            clock.UtcNow = dueUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            Settle(center);
            Require(center.IsAcknowledgeEnabled(key) &&
                    center.IsSnoozeEnabled(key) &&
                    center.IsCompleteEnabled(key) &&
                    snoozeButton.IsEnabled,
                "送达重试成功后处置与稍后菜单按钮未恢复启用。");

            // 2. 激活后通过键盘 Enter 打开菜单，验证共享暖粉样式、五项文案、AutomationControlType、高对比、方向键与 Esc 回焦。
            ActivateForKeyboard(center);
            Require(Keyboard.Focus(snoozeButton) == snoozeButton, "无法将键盘焦点移至稍后提醒菜单按钮。");
            Settle(center);
            Require(KeyboardFocusPolicy.KeyboardFocusDrawnOnce(snoozeButton),
                "稍后提醒菜单按钮键盘聚焦时未保持单层常驻焦点环。");

            WindowValidationInput.RaiseKey(snoozeButton, Key.Enter);
            Settle(center);
            Require(snoozeMenu.IsOpen, "在稍后提醒菜单按钮上按 Enter 没有打开稍后菜单。");
            Require(workspace.Snapshot.Settings.ReminderStates[rule.Id].ActiveSnooze is null &&
                    coordinator.ActiveEntryCount == 1,
                "仅打开菜单不应触发稍后提醒。");

            IDisposable? menuInput = null;
            try
            {
                var items = snoozeMenu.Items.OfType<MenuItem>().ToArray();
                Require(items.Length == ExpectedMenuHeaders.Length &&
                        items.Select(static item => item.Header as string).SequenceEqual(ExpectedMenuHeaders, StringComparer.Ordinal),
                    $"稍后菜单五项文案不匹配：实际=[{string.Join(", ", items.Select(static item => item.Header))}]。");

                // 自动化角色：Menu 与 MenuItem。
                var menuPeer = UIElementAutomationPeer.CreatePeerForElement(snoozeMenu);
                Require(menuPeer is not null && menuPeer.GetAutomationControlType() == AutomationControlType.Menu,
                    $"稍后菜单自动化角色应为 Menu，实际为 {menuPeer?.GetAutomationControlType()}。");
                foreach (var item in items)
                {
                    item.ApplyTemplate();
                    var itemPeer = UIElementAutomationPeer.CreatePeerForElement(item);
                    Require(itemPeer is not null && itemPeer.GetAutomationControlType() == AutomationControlType.MenuItem,
                        $"稍后菜单项「{item.Header}」自动化角色应为 MenuItem，实际为 {itemPeer?.GetAutomationControlType()}。");
                }

                // 共享暖粉样式 token。
                Require(ReferenceEquals(snoozeMenu.Background, Application.Current.FindResource("CanvasBrush")) &&
                        ReferenceEquals(snoozeMenu.BorderBrush, Application.Current.FindResource("ControlBorderBrush")),
                    "稍后菜单未使用共享暖粉表面与边界画刷。");
                var surfaceBorder = VisualDescendants<Border>(snoozeMenu).FirstOrDefault();
                var expectedOverlayRadius = (CornerRadius)Application.Current.FindResource("RadiusOverlay");
                Require(surfaceBorder is not null && surfaceBorder.CornerRadius == expectedOverlayRadius,
                    $"稍后菜单表面圆角应消费 RadiusOverlay({expectedOverlayRadius})，实际为 {surfaceBorder?.CornerRadius}。");
                var expectedBodySize = (double)Application.Current.FindResource("TypeBodyFontSize");
                var expectedFontSource = ((FontFamily)Application.Current.FindResource("UiFontFamily")).Source;
                Require(items.All(item =>
                        Math.Abs(item.FontSize - expectedBodySize) < 0.01 &&
                        string.Equals(item.FontFamily.Source, expectedFontSource, StringComparison.Ordinal)),
                    "稍后菜单项未消费共享字体与正文字号 token。");

                menuInput = WindowValidationInput.AcquireContextMenuInputForValidation(snoozeMenu);
                // 若打开时焦点已在首项，直接验证；否则按 Down 进入首项。
                if (!items[0].IsKeyboardFocused)
                {
                    Keyboard.Focus(snoozeMenu);
                    Settle(center);
                    WindowValidationInput.RaiseKey(snoozeMenu, Key.Down);
                    Settle(center);
                }
                Require(items[0].IsKeyboardFocused && items[0].IsHighlighted &&
                        ReferenceEquals(items[0].Background, Application.Current.FindResource("MainActionBrush")) &&
                        ReferenceEquals(items[0].Foreground, Application.Current.FindResource("MainActionTextBrush")),
                    $"稍后菜单首项未获得键盘高亮或未使用共享主行动高亮色：focused={Keyboard.FocusedElement}。");

                CaptureVisual(center, snoozeMenu, captureDirectory, "snooze-menu-open");

                // 方向键上下遍历全部五项。
                for (var index = 1; index < items.Length; index++)
                {
                    WindowValidationInput.RaiseKey(items[index - 1], Key.Down);
                    Settle(center);
                    Require(items[index].IsKeyboardFocused && items[index].IsHighlighted,
                        $"方向键 Down 未从第 {index} 项移到「{items[index].Header}」，当前焦点={Keyboard.FocusedElement}。");
                }
                WindowValidationInput.RaiseKey(items[^1], Key.Up);
                Settle(center);
                Require(items[^2].IsKeyboardFocused && items[^2].IsHighlighted,
                    $"方向键 Up 未从「自定义」回到「明天 09:00」，当前焦点={Keyboard.FocusedElement}。");

                // 打开期间跟随系统高对比度。
                var dayMenuBackground = ((SolidColorBrush)snoozeMenu.Background).Color;
                using (ApplicationTheme.OverrideHighContrastForValidation(true))
                {
                    Settle(center);
                    Require(snoozeMenu.Background is SolidColorBrush hcBg && hcBg.Color == SystemColors.WindowColor &&
                            items[^2].Background is SolidColorBrush hcHighlight && hcHighlight.Color == SystemColors.HighlightColor,
                        "稍后菜单打开期间未跟随系统对比度切换。");
                }
                Settle(center);
                Require(snoozeMenu.Background is SolidColorBrush restoredBg && restoredBg.Color == dayMenuBackground,
                    "关闭系统对比度后稍后菜单未恢复暖粉日间配色。");

                // Esc 关闭菜单并把焦点还给稍后提醒菜单按钮，不误触发任何稍后操作，也不抢跑到「知道了」。
                var focusedBeforeEsc = items[^2];
                WindowValidationInput.RaiseKey(focusedBeforeEsc, Key.Escape);
                Settle(center);
                Require(!snoozeMenu.IsOpen, "按 Esc 未关闭稍后菜单。");
                Require(ReferenceEquals(Keyboard.FocusedElement, snoozeButton),
                    $"按 Esc 关闭稍后菜单后焦点应回到稍后提醒菜单按钮，实际焦点为 {Keyboard.FocusedElement}。");
                Require(KeyboardFocusPolicy.KeyboardFocusDrawnOnce(snoozeButton),
                    "按 Esc 回到稍后提醒菜单按钮后未保持单层常驻焦点环。");
                Require(coordinator.ActiveEntryCount == 1 &&
                        workspace.Snapshot.Settings.ReminderStates[rule.Id].ActiveSnooze is null,
                    "按 Esc 取消菜单不应触发稍后提醒。");
            }
            finally
            {
                if (snoozeMenu.IsOpen)
                {
                    snoozeMenu.IsOpen = false;
                    Settle(center);
                }
                menuInput?.Dispose();
            }
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(blocker);
            DeleteSettingsFiles(settingsPath);
        }
    }

    private static void RunAllPresetsAndFailureRetryScenario()
    {
        var baseUtc = FixtureUtc;
        // 四个预设档位各自验证「一次选择即立即稍后」以及精确的稍后截止时刻。
        var presets = new (string Header, int ArrowStepsFromFirst, Func<DateTimeOffset, DateTimeOffset> ExpectedUntilUtc)[]
        {
            ("10 分钟后", 0, now => now.AddMinutes(10)),
            ("30 分钟后", 1, now => now.AddMinutes(30)),
            ("1 小时后", 2, now => now.AddMinutes(60)),
            ("明天 09:00", 3, now =>
            {
                var nextDay = ChinaStandardTime.ToBeijingTime(now).Date.AddDays(1);
                return CountdownFormatting.ToUtc(nextDay, 9, 0, 0);
            })
        };

        foreach (var (header, steps, expectedUntil) in presets)
        {
            var settingsPath = CreateTempSettingsPath($"preset-{steps}");
            ReminderDeliveryCoordinator? coordinator = null;
            try
            {
                var clock = new MutableClock(baseUtc.AddMinutes(-1));
                var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
                Require(workspace.Apply(new SaveNoteChange(
                    null,
                    $"预设稍后探针-{header}",
                    new DateTime(2038, 7, 10),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(baseUtc, 15))).Succeeded,
                    $"无法创建预设探针：{header}。");
                var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
                var key = new ActiveReminderOccurrenceKey(rule.Id, baseUtc);

                clock.UtcNow = baseUtc;
                coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
                coordinator.Tick(baseUtc);
                var center = coordinator.ActiveWindow ?? throw new InvalidOperationException("提醒中心未打开。");
                PrepareOffscreen(center);
                Settle(center);

                // 模拟用户在到点 2 分钟后执行预设稍后：计算必须相对成功动作时刻。
                var actionUtc = baseUtc.AddMinutes(2);
                clock.UtcNow = actionUtc;
                ActivateForKeyboard(center);
                var row = FindRow(center, key);
                var snoozeButton = Action(row, "稍后提醒：");
                var snoozeMenu = RequireSnoozeMenu(snoozeButton);
                Require(Keyboard.Focus(snoozeButton) == snoozeButton, $"无法聚焦 {header} 菜单按钮。");
                Settle(center);

                WindowValidationInput.RaiseKey(snoozeButton, Key.Enter);
                Settle(center);
                Require(snoozeMenu.IsOpen, $"Enter 未打开 {header} 稍后菜单。");
                IDisposable? menuInput = null;
                try
                {
                    menuInput = WindowValidationInput.AcquireContextMenuInputForValidation(snoozeMenu);
                    var items = snoozeMenu.Items.OfType<MenuItem>().ToArray();
                    if (!items[0].IsKeyboardFocused)
                    {
                        Keyboard.Focus(snoozeMenu);
                        Settle(center);
                        WindowValidationInput.RaiseKey(snoozeMenu, Key.Down);
                        Settle(center);
                    }
                    for (var step = 0; step < steps; step++)
                    {
                        WindowValidationInput.RaiseKey(items[step], Key.Down);
                        Settle(center);
                    }
                    var targetItem = items[steps];
                    Require(string.Equals(targetItem.Header as string, header, StringComparison.Ordinal) &&
                            targetItem.IsKeyboardFocused,
                        $"方向键未能停在预期项「{header}」，当前聚焦={Keyboard.FocusedElement}。");
                    WindowValidationInput.RaiseKey(targetItem, Key.Enter);
                    Settle(center);
                }
                finally
                {
                    menuInput?.Dispose();
                }

                var expectedOccurrenceKey = ReminderOccurrenceKey.Create(rule, baseUtc).ToString();
                var expectedUntilUtc = expectedUntil(actionUtc);
                var state = workspace.Snapshot.Settings.ReminderStates[rule.Id];
                var persisted = new SettingsStore(clock: clock).Load(settingsPath).Settings.ReminderStates[rule.Id];
                Require(coordinator.ActiveEntryCount == 0 &&
                        coordinator.ActiveWindowCount == 0 &&
                        state.ActiveSnooze?.OccurrenceKey == expectedOccurrenceKey &&
                        state.ActiveSnooze.SnoozeUntilUtc == expectedUntilUtc &&
                        persisted == state,
                    $"选择「{header}」未通过一次选择完成立即稍后或持久化时刻不符：" +
                    $"actualUntil={state.ActiveSnooze?.SnoozeUntilUtc:O}，expectedUntil={expectedUntilUtc:O}。");
            }
            finally
            {
                coordinator?.Dispose();
                DeleteSettingsFiles(settingsPath);
            }
        }

        // 预设稍后写盘失败时保留条目、禁用处置、保留打开来源、不改设置并按 5 秒退避重试成功。
        var retrySettingsPath = CreateTempSettingsPath("preset-retry");
        var blocker = retrySettingsPath + ".tmp";
        ReminderDeliveryCoordinator? retryCoordinator = null;
        try
        {
            var clock = new MutableClock(baseUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(retrySettingsPath, new SettingsStore(clock: clock));
            Require(workspace.Apply(new SaveNoteChange(
                null,
                "预设稍后失败重试探针",
                new DateTime(2038, 7, 10),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateRepeating(baseUtc, 15))).Succeeded,
                "无法创建预设失败重试探针。");
            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, baseUtc);

            clock.UtcNow = baseUtc;
            retryCoordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            retryCoordinator.Tick(baseUtc);
            var center = retryCoordinator.ActiveWindow ?? throw new InvalidOperationException("提醒中心未打开。");
            PrepareOffscreen(center);
            Settle(center);

            var beforeFailure = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var diskBeforeFailure = File.ReadAllText(retrySettingsPath);
            Directory.CreateDirectory(blocker);

            var row = FindRow(center, key);
            var snoozeButton = Action(row, "稍后提醒：");
            var snoozeMenu = RequireSnoozeMenu(snoozeButton);
            SelectMenuItemByHeader(center, snoozeButton, snoozeMenu, "30 分钟后");
            Settle(center);

            Require(retryCoordinator.ActiveEntryCount == 1 &&
                    center.IsFailureVisible(key) &&
                    !center.IsAcknowledgeEnabled(key) &&
                    !center.IsSnoozeEnabled(key) &&
                    !center.IsCompleteEnabled(key) &&
                    center.IsOpenSourceEnabled(key) &&
                    retryCoordinator.HasPendingPersistenceRetry &&
                    retryCoordinator.NextPersistenceRetryUtc == baseUtc.AddSeconds(5) &&
                    SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeFailure &&
                    File.ReadAllText(retrySettingsPath) == diskBeforeFailure,
                "预设稍后写盘失败未保留条目与原状态，或未进入 5 秒退避重试。");

            DeleteDirectoryIfEmpty(blocker);
            clock.UtcNow = baseUtc.AddSeconds(5);
            retryCoordinator.Tick(clock.UtcNow);
            Settle(center);
            var retriedState = workspace.Snapshot.Settings.ReminderStates[rule.Id];
            Require(retryCoordinator.ActiveEntryCount == 0 &&
                    retryCoordinator.ActiveWindowCount == 0 &&
                    retriedState.ActiveSnooze?.OccurrenceKey == ReminderOccurrenceKey.Create(rule, baseUtc).ToString() &&
                    retriedState.ActiveSnooze.SnoozeUntilUtc == clock.UtcNow.AddMinutes(30),
                "预设稍后退避重试成功后未持久化 30 分钟稍后并关闭空中心。");
        }
        finally
        {
            retryCoordinator?.Dispose();
            DeleteDirectoryIfEmpty(blocker);
            DeleteSettingsFiles(retrySettingsPath);
        }
    }

    private static void VerifyTwoRowGeometryAtWidth(
        ReminderCenterWindow center,
        double expectedWidth,
        ActiveReminderOccurrenceKey noteKey,
        ActiveReminderOccurrenceKey countdownKey)
    {
        Require(Math.Abs(center.ActualWidth - expectedWidth) <= 1.0,
            $"提醒中心实际宽度应为 {expectedWidth}，实际为 {center.ActualWidth:0.##}。");

        foreach (var (key, isNote) in new[] { (noteKey, true), (countdownKey, false) })
        {
            var row = FindRow(center, key);
            var primaryRow = VisualDescendants<WrapPanel>(row).Single(panel =>
                string.Equals(
                    AutomationProperties.GetAutomationId(panel),
                    "ReminderEntryActionsPrimaryRow",
                    StringComparison.Ordinal));
            var secondaryRow = VisualDescendants<WrapPanel>(row).Single(panel =>
                string.Equals(
                    AutomationProperties.GetAutomationId(panel),
                    "ReminderEntryActionsSecondaryRow",
                    StringComparison.Ordinal));

            var primaryButtons = primaryRow.Children.OfType<Button>().ToArray();
            Require(primaryRow.Children.Count == 2 && primaryButtons.Length == 2,
                $"宽度 {expectedWidth} 下第一行应固定为「知道了、稍后提醒」两枚按钮，实际子元素={primaryRow.Children.Count}。");
            var acknowledge = primaryButtons[0];
            var snooze = primaryButtons[1];
            Require(AutomationProperties.GetName(acknowledge).StartsWith("知道了：", StringComparison.Ordinal) &&
                    AutomationProperties.GetName(snooze).StartsWith("稍后提醒：", StringComparison.Ordinal),
                $"宽度 {expectedWidth} 下第一行顺序必须为「知道了、稍后提醒」。");

            var secondaryButtons = secondaryRow.Children.OfType<Button>().ToArray();
            var expectedSecondaryCount = isNote ? 2 : 1;
            Require(secondaryRow.Children.Count == expectedSecondaryCount && secondaryButtons.Length == expectedSecondaryCount,
                $"宽度 {expectedWidth} 下第二行动作数量不符：expected={expectedSecondaryCount}，actual={secondaryRow.Children.Count}。");
            if (isNote)
            {
                Require(AutomationProperties.GetName(secondaryButtons[0]).StartsWith("完成便签：", StringComparison.Ordinal) &&
                        AutomationProperties.GetName(secondaryButtons[1]).StartsWith("打开便签来源：", StringComparison.Ordinal),
                    $"宽度 {expectedWidth} 下便签第二行顺序必须为「完成便签、打开来源」。");
            }
            else
            {
                Require(AutomationProperties.GetName(secondaryButtons[0]).StartsWith("打开倒计时来源：", StringComparison.Ordinal),
                    $"宽度 {expectedWidth} 下倒计时第二行必须只有「打开来源」。");
            }

            var contentPanel = primaryRow.Parent as Panel
                ?? throw new InvalidOperationException("动作行不在卡片内容面板内。");
            var contentRect = new Rect(0, 0, contentPanel.ActualWidth, contentPanel.ActualHeight);

            var ackBounds = Bounds(acknowledge, contentPanel);
            var snoozeBounds = Bounds(snooze, contentPanel);
            Require(Math.Abs(ackBounds.Top - snoozeBounds.Top) <= 1.0 &&
                    Math.Abs(ackBounds.Bottom - snoozeBounds.Bottom) <= 1.0,
                $"宽度 {expectedWidth} 下第一行折成多行：ack={ackBounds}，snooze={snoozeBounds}。");
            Require(ackBounds.Right <= snoozeBounds.Left + 0.5,
                $"宽度 {expectedWidth} 下第一行按钮重叠：ack={ackBounds}，snooze={snoozeBounds}。");
            Require(ackBounds.Left >= -0.5 && snoozeBounds.Right <= contentRect.Right + 0.5,
                $"宽度 {expectedWidth} 下第一行被裁切：ack={ackBounds}，snooze={snoozeBounds}，contentWidth={contentRect.Right:0.##}。");

            var secondaryRowBounds = Bounds(secondaryRow, contentPanel);
            Require(secondaryRowBounds.Top >= Math.Max(ackBounds.Bottom, snoozeBounds.Bottom) - 0.5,
                $"宽度 {expectedWidth} 下第二行与第一行纵向重叠：row1Bottom={Math.Max(ackBounds.Bottom, snoozeBounds.Bottom):0.##}，row2Top={secondaryRowBounds.Top:0.##}。");

            var secBounds = secondaryButtons.Select(button => Bounds(button, contentPanel)).ToArray();
            for (var index = 0; index < secBounds.Length; index++)
            {
                Require(secBounds[index].Left >= -0.5 && secBounds[index].Right <= contentRect.Right + 0.5,
                    $"宽度 {expectedWidth} 下第二行第 {index} 项被裁切：bounds={secBounds[index]}，contentWidth={contentRect.Right:0.##}。");
                if (index > 0)
                {
                    Require(Math.Abs(secBounds[index].Top - secBounds[0].Top) <= 1.0 &&
                            Math.Abs(secBounds[index].Bottom - secBounds[0].Bottom) <= 1.0,
                        $"宽度 {expectedWidth} 下第二行折成多行：{string.Join(" | ", secBounds)}。");
                    Require(secBounds[index - 1].Right <= secBounds[index].Left + 0.5,
                        $"宽度 {expectedWidth} 下第二行按钮重叠：{secBounds[index - 1]} vs {secBounds[index]}。");
                }
            }

            // 第二行居中且不拉伸。
            var expectedSecondaryWidth = isNote
                ? secondaryButtons[0].ActualWidth + secondaryButtons[0].Margin.Right + secondaryButtons[1].ActualWidth
                : secondaryButtons[0].ActualWidth;
            var leftGap = secondaryRowBounds.Left;
            var rightGap = contentRect.Right - secondaryRowBounds.Right;
            Require(secondaryRow.HorizontalAlignment == HorizontalAlignment.Center &&
                    Math.Abs(secondaryRow.ActualWidth - expectedSecondaryWidth) <= 0.5 &&
                    secondaryRow.ActualWidth < contentRect.Width - 1.0 &&
                    Math.Abs(leftGap - rightGap) <= 1.5,
                $"宽度 {expectedWidth} 下第二行未居中或发生拉伸：actualW={secondaryRow.ActualWidth:0.##}，" +
                $"expectedW={expectedSecondaryWidth:0.##}，contentW={contentRect.Width:0.##}，leftGap={leftGap:0.##}，rightGap={rightGap:0.##}。");
        }
    }

    private static void VerifyCustomControlsGeometry(
        Border row,
        DatePicker customDate,
        ComboBox customHour,
        ComboBox customMinute,
        Button customConfirm,
        string label)
    {
        var contentPanel = VisualDescendants<WrapPanel>(row)
            .Single(panel => string.Equals(
                AutomationProperties.GetAutomationId(panel),
                "ReminderEntryActionsPrimaryRow",
                StringComparison.Ordinal))
            .Parent as Panel
            ?? throw new InvalidOperationException("找不到卡片内容面板。");
        var contentWidth = contentPanel.ActualWidth;
        var controls = new FrameworkElement[] { customDate, customHour, customMinute, customConfirm };
        var rects = controls.Select(control => Bounds(control, contentPanel)).ToArray();
        for (var i = 0; i < rects.Length; i++)
        {
            Require(rects[i].Width > 0 && rects[i].Height > 0 &&
                    rects[i].Left >= -0.5 && rects[i].Right <= contentWidth + 0.5,
                $"宽度 {label} 下自定义稍后控件 #{i} 被裁切：rect={rects[i]}，contentWidth={contentWidth:0.##}。");
            for (var j = i + 1; j < rects.Length; j++)
            {
                var intersection = Rect.Intersect(rects[i], rects[j]);
                var overlaps = !intersection.IsEmpty && intersection.Width > 0.5 && intersection.Height > 0.5;
                Require(!overlaps,
                    $"宽度 {label} 下自定义稍后控件 #{i} 与 #{j} 重叠：{rects[i]} vs {rects[j]}。");
            }
        }
    }

    private static void VerifyErrorAdjacentBelowCustomInputs(
        Border row,
        DatePicker customDate,
        ComboBox customHour,
        ComboBox customMinute,
        Button customConfirm,
        TextBlock error)
    {
        var inputsBottom = new FrameworkElement[] { customDate, customHour, customMinute, customConfirm }
            .Max(control => Bounds(control, row).Bottom);
        var errorBounds = Bounds(error, row);
        Require(errorBounds.Top >= inputsBottom - 0.5 && errorBounds.Top <= inputsBottom + 16,
            $"自定义稍后错误未紧邻输入控件下方：inputsBottom={inputsBottom:0.##}，errorTop={errorBounds.Top:0.##}。");
        Require(errorBounds.Right <= row.ActualWidth - row.Padding.Right - row.BorderThickness.Right + 0.5,
            $"自定义稍后错误超出卡片右缘：errorRight={errorBounds.Right:0.##}，rowWidth={row.ActualWidth:0.##}。");
    }

    private static ContextMenu RequireSnoozeMenu(Button snoozeButton)
    {
        var menu = snoozeButton.ContextMenu;
        Require(menu is not null, "稍后提醒按钮必须挂载共享暖粉 ContextMenu。");
        return menu!;
    }

    private static void SelectMenuItemByHeader(Window window, Button snoozeButton, ContextMenu snoozeMenu, string header)
    {
        ActivateForKeyboard(window);
        Require(WindowValidationInput.InvokeButton(snoozeButton), "无法激活稍后提醒菜单按钮。");
        Settle(window);
        Require(snoozeMenu.IsOpen, $"激活稍后提醒菜单按钮后菜单未打开（目标项：{header}）。");
        var item = snoozeMenu.Items.OfType<MenuItem>()
            .SingleOrDefault(candidate => string.Equals(candidate.Header as string, header, StringComparison.Ordinal));
        Require(item is not null, $"稍后菜单缺少「{header}」项。");
        if (UIElementAutomationPeer.CreatePeerForElement(item!)?.GetPattern(PatternInterface.Invoke)
            is System.Windows.Automation.Provider.IInvokeProvider invokeProvider)
        {
            invokeProvider.Invoke();
        }
        else
        {
            item!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        Settle(window);
    }

    private static Border FindRow(ReminderCenterWindow center, ActiveReminderOccurrenceKey key) =>
        VisualDescendants<Border>(center)
            .Single(candidate => candidate.Tag is ActiveReminderOccurrenceKey candidateKey && candidateKey == key);

    private static Button Action(DependencyObject row, string automationNamePrefix) =>
        VisualDescendants<Button>(row).Single(candidate =>
            AutomationProperties.GetName(candidate)?.StartsWith(automationNamePrefix, StringComparison.Ordinal) == true);

    private static void ActivateForKeyboard(Window window)
    {
        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetActiveWindow(handle);
        }
        Settle(window);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static void PrepareOffscreen(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;
        window.Top = -20_000;
        window.Opacity = 0;
        window.Topmost = false;
    }

    private static string CreateTempSettingsPath(string tag) =>
        Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-snooze-menu-{tag}-{Guid.NewGuid():N}.settings.json");

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string? CreateCaptureDirectory()
    {
        var explicitDirectory = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_EVIDENCE_320_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            var resolved = Path.GetFullPath(explicitDirectory);
            Directory.CreateDirectory(resolved);
            return resolved;
        }

        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        var directory = Path.Combine(
            Path.GetFullPath(sourceRoot),
            "logs",
            "ui-captures",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-reminder-snooze-menu");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CaptureVisual(Window window, FrameworkElement? target, string? directory, string state)
    {
        if (directory is null || target is null)
        {
            return;
        }

        TextOptions.SetTextRenderingMode(target, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(target, TextFormattingMode.Ideal);
        window.Opacity = 0.01;
        Settle(window);
        target.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(target);
        var dpiX = dpi.PixelsPerInchX > 0 ? dpi.PixelsPerInchX : 96;
        var dpiY = dpi.PixelsPerInchY > 0 ? dpi.PixelsPerInchY : 96;
        var width = Math.Max(1, (int)Math.Ceiling(target.ActualWidth * dpiX / 96.0));
        var height = Math.Max(1, (int)Math.Ceiling(target.ActualHeight * dpiY / 96.0));
        var bitmap = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        bitmap.Render(target);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var outputPath = Path.Combine(directory, $"reminder-center-{state}.png");
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow.ToUniversalTime();
    }
}
