using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static class ValidationExercises
{
    public static bool ProbeMotionTokens()
    {
        var resources = Application.Current?.Resources;
        if (resources is null ||
            !resources.Contains("MotionHoverDuration") ||
            !resources.Contains("MotionPressDuration") ||
            !resources.Contains("MotionPopupEnterDuration") ||
            !resources.Contains("MotionPopupExitDuration") ||
            !resources.Contains("MotionIndicatorDuration") ||
            !resources.Contains("MotionCountdownDuration") ||
            !resources.Contains("MotionEaseOut"))
        {
            return false;
        }

        var animationEnabled = SystemParameters.ClientAreaAnimation;
        var expectedHover = TimeSpan.FromMilliseconds(animationEnabled ? 120 : 0);
        var expectedPress = TimeSpan.FromMilliseconds(animationEnabled ? 100 : 0);
        var expectedPopupEnter = TimeSpan.FromMilliseconds(animationEnabled ? 140 : 0);
        var expectedPopupExit = TimeSpan.FromMilliseconds(animationEnabled ? 90 : 0);
        var expectedIndicator = TimeSpan.FromMilliseconds(animationEnabled ? 160 : 0);
        var expectedCountdown = TimeSpan.FromMilliseconds(animationEnabled ? 90 : 0);

        return resources["MotionHoverDuration"] is Duration hover &&
               resources["MotionPressDuration"] is Duration press &&
               resources["MotionPopupEnterDuration"] is Duration popupEnter &&
               resources["MotionPopupExitDuration"] is Duration popupExit &&
               resources["MotionIndicatorDuration"] is Duration indicator &&
               resources["MotionCountdownDuration"] is Duration countdown &&
               hover.TimeSpan == expectedHover &&
               press.TimeSpan == expectedPress &&
               popupEnter.TimeSpan == expectedPopupEnter &&
               popupExit.TimeSpan == expectedPopupExit &&
               indicator.TimeSpan == expectedIndicator &&
               countdown.TimeSpan == expectedCountdown &&
               resources["MotionEaseOut"] is EasingFunctionBase easing &&
               easing.EasingMode == EasingMode.EaseOut;
    }

    public static bool RunValidateOnlySettingsIsolationBehavior()
    {
        var applicationRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-validation-isolation-{Guid.NewGuid():N}");
        var rootSettingsPath = Path.Combine(applicationRoot, "settings.json");
        var isolatedSettingsPath = Path.Combine(applicationRoot, "isolated", "settings.json");

        var missingSettingsRejected = ThrowsArgumentException(
            () => HostOptions.Parse(new[] { "--validate-only" }));
        var rootSettingsRejected = ThrowsArgumentException(() =>
            ApplicationValidation.ResolveSettingsPath(
                HostOptions.Parse(new[]
                {
                    "--validate-only",
                    "--settings-file",
                    rootSettingsPath
                }),
                applicationRoot));
        var isolatedSettingsAccepted = string.Equals(
            isolatedSettingsPath,
            ApplicationValidation.ResolveSettingsPath(
                HostOptions.Parse(new[]
                {
                    "--validate-only",
                    "--settings-file",
                    isolatedSettingsPath
                }),
                applicationRoot),
            StringComparison.OrdinalIgnoreCase);
        var productionDefaultPreserved =
            HostOptions.Parse(Array.Empty<string>()).SettingsFile is null;

        return missingSettingsRejected &&
               rootSettingsRejected &&
               isolatedSettingsAccepted &&
               productionDefaultPreserved;
    }

    public static bool RunLazyManagerLifecycleBehavior()
    {
        AssertLazyManagerFailureRecovery();
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-lazy-manager-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        HolidayCalendar? calendar = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath);
            MustApply(workspace, new SaveNoteChange(
                null,
                "懒加载来源便签",
                new DateTime(2031, 4, 5),
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var note = workspace.Snapshot.Settings.Notes.Single();
            calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            var lifecycle = new ManagerWindowLifecycle(
                () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
                PrepareOffscreenValidationWindow);
            var floating = new FloatingWindow(
                workspace,
                calendar,
                lifecycle,
                AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(floating);
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);

            Must(
                Application.Current.Windows.OfType<MainWindow>().All(preexistingWindows.Contains) &&
                lifecycle.Window is null,
                "普通启动后 Windows 集合不应包含管理窗口。");

            var openManager = floating.FindName("OpenManagerButton") as Button ??
                              throw new InvalidOperationException("悬浮窗缺少 OpenManagerButton。");
            openManager.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            var created = CurrentCreatedMainWindows(preexistingWindows);
            Must(
                created.Length == 1 &&
                ReferenceEquals(created[0], lifecycle.Window) &&
                created[0].IsVisible &&
                created[0].WindowState == WindowState.Normal &&
                created[0].CurrentWorkspace == floating.CurrentWorkspaceForValidation,
                "第一次管理没有创建唯一可见管理窗口或未沿用当前工作区。");

            var first = created[0];
            openManager.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                CurrentCreatedMainWindows(preexistingWindows).Length == 1 &&
                ReferenceEquals(first, lifecycle.Window),
                "重复打开管理产生了第二个管理窗口。");

            var draft = (TextBox)first.FindName("CountdownName");
            draft.Text = "应保留的倒计时草稿";
            first.Hide();
            WindowValidationInput.Pump(first.Dispatcher);
            Must(
                lifecycle.OpenReminderSource(ReminderSourceKind.Note, note.Id),
                "打开来源失败。");
            WindowValidationInput.Pump(first.Dispatcher);
            Must(
                ReferenceEquals(first, lifecycle.Window) &&
                first.IsVisible &&
                first.WindowState == WindowState.Normal &&
                first.CurrentWorkspace == WorkspaceKind.Notes &&
                string.Equals(draft.Text, "应保留的倒计时草稿", StringComparison.Ordinal) &&
                Keyboard.FocusedElement is Button { Tag: string focusedId } &&
                string.Equals(focusedId, note.Id, StringComparison.OrdinalIgnoreCase),
                "打开来源没有揭示准确来源或覆盖了草稿。");
            return true;
        }
        finally
        {
            foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                if (!preexistingWindows.Contains(window))
                {
                    window.Close();
                }
            }

            calendar?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static void PrepareOffscreenValidationWindow(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;
        window.Top = -20_000;
        window.Opacity = 0;
    }

    private static MainWindow[] CurrentCreatedMainWindows(HashSet<Window> preexistingWindows) =>
        Application.Current.Windows
            .OfType<MainWindow>()
            .Where(window => !preexistingWindows.Contains(window))
            .ToArray();

    private static void AssertLazyManagerFailureRecovery()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-lazy-manager-fail-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        HolidayCalendar? calendar = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath);
            MustApply(workspace, new SaveNoteChange(
                null,
                "失败回滚隔离便签",
                new DateTime(2031, 4, 5),
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            var settingsBytesBefore = File.ReadAllBytes(settingsPath);
            var baselineCount = Application.Current.Windows.OfType<MainWindow>().Count();
            MainWindow? hostField = null;
            var failAttach = true;
            var lifecycle = new ManagerWindowLifecycle(
                () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
                window =>
                {
                    if (failAttach)
                    {
                        hostField = window;
                        throw new InvalidOperationException("附加管理窗口失败。");
                    }

                    PrepareOffscreenValidationWindow(window);
                    hostField = window;
                });

            var originalThrown = false;
            try
            {
                lifecycle.EnsureCreated();
            }
            catch (InvalidOperationException exception) when (
                string.Equals(exception.Message, "附加管理窗口失败。", StringComparison.Ordinal))
            {
                originalThrown = true;
            }

            WindowValidationInput.Pump(Application.Current.Dispatcher);
            var createdAfterFailure = CurrentCreatedMainWindows(preexistingWindows);
            Must(originalThrown, "attach 失败没有抛出原异常。");
            Must(lifecycle.Window is null, "attach 失败后 lifecycle.Window 应为空。");
            Must(
                Application.Current.Windows.OfType<MainWindow>().Count() == baselineCount &&
                createdAfterFailure.Length == 0,
                "attach 失败后管理窗口计数没有回到基线。");
            Must(
                hostField is null ||
                !Application.Current.Windows.OfType<MainWindow>().Contains(hostField),
                "attach 先写 hostField 再失败后仍指向活孤儿。");
            Must(
                File.Exists(settingsPath) &&
                File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(settingsBytesBefore),
                "attach 失败改写了隔离 settings 文件。");

            failAttach = false;
            lifecycle.Reveal(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Application.Current.Dispatcher);
            var recovered = CurrentCreatedMainWindows(preexistingWindows);
            Must(
                recovered.Length == 1 &&
                ReferenceEquals(recovered[0], lifecycle.Window) &&
                ReferenceEquals(hostField, lifecycle.Window) &&
                recovered[0].IsVisible &&
                recovered[0].WindowState == WindowState.Normal &&
                recovered[0].CurrentWorkspace == WorkspaceKind.Notes,
                "失败后重入没有恢复成恰好一扇可用管理窗口。");
        }
        finally
        {
            foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                if (!preexistingWindows.Contains(window))
                {
                    window.Close();
                }
            }

            calendar?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    public static bool RunSettingsRecoveryWindowBehavior()
    {
        var window = SettingsRecoveryWindow.CreateForValidation(
            "设置恢复验证消息",
            canRecover: true);
        try
        {
            var body = (window.Content as Border)?.Child as StackPanel;
            var commands = body?.Children
                .OfType<StackPanel>()
                .SingleOrDefault(panel => panel.Orientation == Orientation.Horizontal);
            var buttons = commands?.Children.OfType<Button>().ToArray() ?? Array.Empty<Button>();
            var recover = buttons.SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetName(button),
                    "恢复备份",
                    StringComparison.Ordinal));
            var exit = buttons.SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetName(button),
                    "退出",
                    StringComparison.Ordinal));
            var defaultDecisionIsExit =
                window.DecisionForValidation == SettingsRecoveryDecision.Exit;
            window.Close();
            var closeKeepsExit = window.DecisionForValidation == SettingsRecoveryDecision.Exit;

            var presentedMessages = new List<string>();
            var declinedPresented = SettingsRecoveryFailurePresentation.TryPresent(
                new SettingsRecoveryDeclinedException(),
                presentedMessages.Add);
            var failureMessage = "无法安全恢复设置。";
            var failurePresented = SettingsRecoveryFailurePresentation.TryPresent(
                new SettingsRecoveryException(failureMessage),
                presentedMessages.Add);

            var aclDeniedRecovered = RunAclDeniedMainRecoveryWiringProbe();
            var failureExitIsDefault = ProbeSettingsRecoveryFailureExitIsDefault();

            return defaultDecisionIsExit &&
                   closeKeepsExit &&
                   buttons.Length == 2 &&
                   recover is { IsDefault: true, IsEnabled: true } &&
                   exit is { IsCancel: true, IsEnabled: true, IsDefault: false } &&
                   failureExitIsDefault &&
                   !declinedPresented &&
                   failurePresented &&
                   aclDeniedRecovered &&
                   presentedMessages.SequenceEqual(new[] { failureMessage }, StringComparer.Ordinal);
        }
        finally
        {
            if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Close();
            }
        }
    }

    public static bool ProbeSettingsRecoveryFailureExitIsDefault()
    {
        var window = SettingsRecoveryWindow.CreateForValidation(
            "设置恢复失败退出验证",
            canRecover: false);
        try
        {
            var body = (window.Content as Border)?.Child as StackPanel;
            var commands = body?.Children
                .OfType<StackPanel>()
                .SingleOrDefault(panel => panel.Orientation == Orientation.Horizontal);
            var buttons = commands?.Children.OfType<Button>().ToArray() ?? Array.Empty<Button>();
            var recover = buttons.SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetName(button),
                    "恢复备份",
                    StringComparison.Ordinal));
            var exit = buttons.SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetName(button),
                    "退出",
                    StringComparison.Ordinal));
            return recover is null &&
                   buttons.Length == 1 &&
                   exit is { IsDefault: true, IsCancel: true, IsEnabled: true };
        }
        finally
        {
            if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Close();
            }
        }
    }

    public static bool ProbeRuleEditorInlineDeleteUsesDangerButton()
    {
        var editor = new ReminderRulesEditorWindow(
            [
                new AbsoluteOnceReminderRule(
                    "danger-delete-probe",
                    new DateTimeOffset(2031, 1, 1, 1, 0, 0, TimeSpan.Zero))
            ],
            new AdvancingValidationClock(new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        PrepareOffscreenValidationWindow(editor);
        try
        {
            editor.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            editor.UpdateLayout();
            var expected = editor.TryFindResource("DangerButton") as Style;
            var deleteButtons = VisualDescendants<Button>(editor)
                .Where(button => string.Equals(button.Content?.ToString(), "删除", StringComparison.Ordinal))
                .ToArray();
            return expected is not null &&
                   deleteButtons.Length > 0 &&
                   deleteButtons.All(button => ReferenceEquals(button.Style, expected));
        }
        finally
        {
            if (!editor.Dispatcher.HasShutdownStarted)
            {
                editor.Close();
            }
        }
    }

    public static bool ProbeSettingsRecoverySecondaryUsesCanvas()
    {
        // #150：期望值是现役 CanvasBrush 的静态副本（#FDF3EB），随活体 token 同步。
        var expected = Color.FromRgb(0xFD, 0xF3, 0xEB);
        var failure = SettingsRecoveryWindow.CreateForValidation("次级按钮暖白验证", canRecover: false);
        var recoverable = SettingsRecoveryWindow.CreateForValidation("次级按钮暖白验证", canRecover: true);
        try
        {
            return UsesCanvasSecondary(failure, expected, expectExitIsDefault: true) &&
                   UsesCanvasSecondary(recoverable, expected, expectExitIsDefault: false);
        }
        finally
        {
            if (!failure.Dispatcher.HasShutdownStarted)
            {
                failure.Close();
            }

            if (!recoverable.Dispatcher.HasShutdownStarted)
            {
                recoverable.Close();
            }
        }

        static bool UsesCanvasSecondary(
            SettingsRecoveryWindow window,
            Color expected,
            bool expectExitIsDefault)
        {
            var body = (window.Content as Border)?.Child as StackPanel;
            var commands = body?.Children
                .OfType<StackPanel>()
                .SingleOrDefault(panel => panel.Orientation == Orientation.Horizontal);
            var buttons = commands?.Children.OfType<Button>().ToArray() ?? Array.Empty<Button>();
            var recover = buttons.SingleOrDefault(button =>
                string.Equals(AutomationProperties.GetName(button), "恢复备份", StringComparison.Ordinal));
            var exit = buttons.SingleOrDefault(button =>
                string.Equals(AutomationProperties.GetName(button), "退出", StringComparison.Ordinal));
            var exitUsesCanvas = exit?.Background is SolidColorBrush exitBrush &&
                                 exitBrush.Color == expected;
            if (expectExitIsDefault)
            {
                return recover is null &&
                       exit is { IsDefault: true, IsCancel: true } &&
                       exitUsesCanvas;
            }

            return recover is { IsDefault: true } &&
                   exit is { IsDefault: false, IsCancel: true } &&
                   exitUsesCanvas &&
                   recover.Background is SolidColorBrush recoverBrush &&
                   recoverBrush.Color != expected;
        }
    }

    public static bool ProbeReminderCenterTwoRowActionsLayout()
    {
        var noteKey = new ActiveReminderOccurrenceKey(
            "two-row-note-rule",
            new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
        var countdownKey = new ActiveReminderOccurrenceKey(
            "two-row-countdown-rule",
            new DateTimeOffset(2031, 4, 5, 2, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(window);
        window.AddEntry(new ReminderCenterItem(
            noteKey,
            new NoteRecord(
                "two-row-note",
                "两行布局探针便签",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        window.AddEntry(new ReminderCenterItem(
            countdownKey,
            new CountdownReminderSource(new CountdownRecord(
                "two-row-countdown",
                "两行布局探针倒计时",
                new DateTimeOffset(2031, 5, 1, 0, 0, 0, TimeSpan.Zero),
                Array.Empty<string>())),
            1,
            0));
        try
        {
            window.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            window.UpdateLayout();
            var acknowledgeAll = VisualDescendants<Button>(window).SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterAcknowledgeAll",
                    StringComparison.Ordinal));
            if (acknowledgeAll is null ||
                !ButtonHasIconAndText(acknowledgeAll, "全部已读") ||
                !window.IsAncestorOf(acknowledgeAll))
            {
                return false;
            }

            foreach (var (key, expectComplete) in new[] { (noteKey, true), (countdownKey, false) })
            {
                var row = VisualDescendants<Border>(window).Single(border =>
                    border.Tag is ActiveReminderOccurrenceKey rowKey && rowKey == key);
                var primaryRow = VisualDescendants<WrapPanel>(row).SingleOrDefault(panel =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(panel),
                        "ReminderEntryActionsPrimaryRow",
                        StringComparison.Ordinal));
                var secondaryRow = VisualDescendants<WrapPanel>(row).SingleOrDefault(panel =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(panel),
                        "ReminderEntryActionsSecondaryRow",
                        StringComparison.Ordinal));
                var customRow = VisualDescendants<WrapPanel>(row).SingleOrDefault(panel =>
                    VisualDescendants<DatePicker>(panel).Any());
                if (primaryRow is null || secondaryRow is null || customRow is null)
                {
                    return false;
                }

                var acknowledge = FindEntryButton(row, "知道了：");
                var snooze = FindEntryButton(row, "稍后提醒：");
                var openSource = FindEntryButton(row, "打开");
                var complete = VisualDescendants<Button>(row).SingleOrDefault(button =>
                    AutomationProperties.GetName(button).StartsWith("完成便签：", StringComparison.Ordinal));
                var snoozeChoice = VisualDescendants<ComboBox>(row).Single(comboBox =>
                    AutomationProperties.GetName(comboBox).StartsWith("稍后提醒时间：", StringComparison.Ordinal));
                if (acknowledge is null ||
                    snooze is null ||
                    openSource is null ||
                    !ButtonHasIconAndText(acknowledge, "知道了") ||
                    !ButtonHasIconAndText(snooze, "稍后提醒") ||
                    !ButtonHasIconAndText(openSource, "打开来源") ||
                    expectComplete != (complete is not null))
                {
                    return false;
                }

                if (complete is not null && !ButtonHasIconAndText(complete, "完成便签"))
                {
                    return false;
                }

                var primaryChildren = primaryRow.Children.Cast<UIElement>().ToArray();
                var secondaryChildren = secondaryRow.Children.Cast<UIElement>().ToArray();
                var snoozeFrame = primaryChildren.Length == 3
                    ? primaryChildren[1] as Grid
                    : null;
                var primaryOrderMatches =
                    primaryChildren.Length == 3 &&
                    ReferenceEquals(primaryChildren[0], acknowledge) &&
                    snoozeFrame is not null &&
                    string.Equals(
                        AutomationProperties.GetAutomationId(snoozeFrame),
                        "ReminderEntrySnoozeChoiceFrame",
                        StringComparison.Ordinal) &&
                    snoozeFrame.Children.Contains(snoozeChoice) &&
                    snoozeFrame.Children.OfType<Grid>().Any(icon =>
                        !icon.IsHitTestVisible &&
                        VisualDescendants<System.Windows.Shapes.Ellipse>(icon).Any()) &&
                    ReferenceEquals(primaryChildren[2], snooze);
                var secondaryOrderMatches = expectComplete
                    ? secondaryChildren.Length == 2 &&
                      ReferenceEquals(secondaryChildren[0], complete) &&
                      ReferenceEquals(secondaryChildren[1], openSource)
                    : secondaryChildren.Length == 1 &&
                      ReferenceEquals(secondaryChildren[0], openSource);
                // #150：第二行按钮居中且不拉伸——行宽等于子项期望宽之和，且不得吸满整张卡。
                var secondaryExpectedWidth =
                    (expectComplete && complete is { } completeButton
                        ? completeButton.ActualWidth + 8
                        : 0) +
                    openSource.ActualWidth;
                var secondaryNotStretched =
                    Math.Abs(secondaryRow.ActualWidth - secondaryExpectedWidth) <= 0.5 &&
                    secondaryRow.ActualWidth > 0 &&
                    secondaryRow.ActualWidth < row.ActualWidth;
                if (!primaryOrderMatches ||
                    !secondaryOrderMatches ||
                    secondaryRow.HorizontalAlignment != HorizontalAlignment.Center ||
                    !secondaryNotStretched ||
                    ReferenceEquals(customRow.Parent, primaryRow) ||
                    ReferenceEquals(customRow.Parent, secondaryRow))
                {
                    return false;
                }

                if (primaryRow.Parent is not Panel content ||
                    content.Children.IndexOf(primaryRow) < 0 ||
                    content.Children.IndexOf(secondaryRow) <= content.Children.IndexOf(primaryRow) ||
                    content.Children.IndexOf(customRow) <= content.Children.IndexOf(secondaryRow))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Close();
            }
        }
    }

    public static bool ProbeReminderCenterEntryVisualIdentity()
    {
        var noteKey = new ActiveReminderOccurrenceKey(
            "identity-note-rule",
            new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
        var countdownKey = new ActiveReminderOccurrenceKey(
            "identity-countdown-rule",
            new DateTimeOffset(2031, 4, 5, 2, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(window);
        window.AddEntry(new ReminderCenterItem(
            noteKey,
            new NoteRecord(
                "identity-note",
                "视觉标识探针便签",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        window.AddEntry(new ReminderCenterItem(
            countdownKey,
            new CountdownReminderSource(new CountdownRecord(
                "identity-countdown",
                "视觉标识探针倒计时",
                new DateTimeOffset(2031, 5, 1, 0, 0, 0, TimeSpan.Zero),
                Array.Empty<string>())),
            1,
            0));
        try
        {
            window.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            window.UpdateLayout();
            var warningTint = Application.Current.FindResource("WarningTintBrush");
            var warningAccent = Application.Current.FindResource("WarningAccentBrush");
            var reminderCard = Application.Current.FindResource("ReminderCardBrush");
            var reminderPage = Application.Current.FindResource("ReminderPageBrush");
            var sourceNoteIcon = Application.Current.FindResource("SourceNoteIconBrush");
            var pinkTintSoft = Application.Current.FindResource("PinkTintSoftBrush");
            var panelBrush = Application.Current.FindResource("PanelBrush");
            var inkBrush = Application.Current.FindResource("InkBrush");
            if (window.Content is not Border surface ||
                !ReferenceEquals(surface.Background, reminderPage))
            {
                return false;
            }

            foreach (var (key, isCountdown) in new[] { (noteKey, false), (countdownKey, true) })
            {
                var row = VisualDescendants<Border>(window).Single(border =>
                    border.Tag is ActiveReminderOccurrenceKey rowKey && rowKey == key);
                if (!ReferenceEquals(row.Background, reminderCard))
                {
                    return false;
                }

                var badge = VisualDescendants<Border>(row).SingleOrDefault(border =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(border),
                        "ReminderEntryPendingBadge",
                        StringComparison.Ordinal));
                var badgeText = badge is null
                    ? null
                    : VisualDescendants<TextBlock>(badge).SingleOrDefault(text =>
                        string.Equals(text.Text, "待处理", StringComparison.Ordinal));
                if (badge is null ||
                    badge.Focusable ||
                    !ReferenceEquals(badge.Background, warningTint) ||
                    badgeText is null ||
                    !ReferenceEquals(badgeText.Foreground, warningAccent))
                {
                    return false;
                }

                // #150：「待处理」徽章的感叹号是矢量图形（竖条 + 圆点），不是文本字重。
                var badgePanel = badge.Child as StackPanel;
                var badgeMark = badgePanel is { Children.Count: 2 } badgeIconText
                    ? badgeIconText.Children[0] as Grid
                    : null;
                if (badgeMark is null ||
                    VisualDescendants<TextBlock>(badgeMark).Any() ||
                    !VisualDescendants<System.Windows.Shapes.Ellipse>(badgeMark).Any())
                {
                    return false;
                }

                var iconBlock = VisualDescendants<Border>(row).SingleOrDefault(border =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(border),
                        "ReminderEntrySourceIcon",
                        StringComparison.Ordinal));
                var expectedIconBrush = isCountdown ? pinkTintSoft : sourceNoteIcon;
                // #150：来源图标是纯装饰，不携带可访问名（不作为独立控件朗读）；
                // 名称留在整行可访问名上。
                if (iconBlock is null ||
                    iconBlock.Focusable ||
                    !ReferenceEquals(iconBlock.Background, expectedIconBrush) ||
                    !string.IsNullOrEmpty(AutomationProperties.GetName(iconBlock)) ||
                    !AutomationProperties.GetName(row).StartsWith(
                        isCountdown ? "倒计时提醒：" : "便签提醒：",
                        StringComparison.Ordinal))
                {
                    return false;
                }

                var scheduledText = VisualDescendants<TextBlock>(row).Single(text =>
                    text.Text.StartsWith("计划：", StringComparison.Ordinal));
                var scheduledRow = scheduledText.Parent as StackPanel;
                var scheduledClock = scheduledRow is not null &&
                    VisualDescendants<System.Windows.Shapes.Ellipse>(scheduledRow).Any();
                var divider = VisualDescendants<Border>(row).Any(border =>
                    border.Child is null &&
                    Math.Abs(border.Height - 1) < 0.01);
                var snoozeFrame = VisualDescendants<Grid>(row).SingleOrDefault(grid =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(grid),
                        "ReminderEntrySnoozeChoiceFrame",
                        StringComparison.Ordinal));
                var snoozeChoice = VisualDescendants<ComboBox>(row).Single(comboBox =>
                    AutomationProperties.GetName(comboBox).StartsWith("稍后提醒时间：", StringComparison.Ordinal));
                var snoozeChoiceFrameMatches =
                    snoozeFrame is not null &&
                    !snoozeFrame.Focusable &&
                    snoozeFrame.Children.Contains(snoozeChoice) &&
                    ReferenceEquals(snoozeChoice.Background, panelBrush) &&
                    snoozeFrame.Children.OfType<Grid>().Any(icon =>
                        !icon.IsHitTestVisible &&
                        VisualDescendants<System.Windows.Shapes.Ellipse>(icon).Any(ring =>
                            ReferenceEquals(ring.Stroke, inkBrush)));
                if (!scheduledClock || !divider || !snoozeChoiceFrameMatches)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Close();
            }
        }
    }

    private static Button? FindEntryButton(DependencyObject row, string accessibleNamePrefix) =>
        VisualDescendants<Button>(row).SingleOrDefault(button =>
            AutomationProperties.GetName(button).StartsWith(accessibleNamePrefix, StringComparison.Ordinal));

    // #150：提醒中心不引入第三种字重——窗口内全部文字只用 Regular/SemiBold（无 Bold），
    // 且「待处理」徽章的感叹号是纯矢量图形而不是文本。
    public static bool ProbeReminderCenterTypographyWeightWhitelist()
    {
        var key = new ActiveReminderOccurrenceKey(
            "weight-whitelist-rule",
            new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(window);
        window.AddEntry(new ReminderCenterItem(
            key,
            new NoteRecord(
                "weight-whitelist-note",
                "字重白名单探针便签",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        try
        {
            window.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            window.UpdateLayout();
            var textBlocks = VisualDescendants<TextBlock>(window).ToArray();
            var badge = VisualDescendants<Border>(window).SingleOrDefault(border =>
                string.Equals(
                    AutomationProperties.GetAutomationId(border),
                    "ReminderEntryPendingBadge",
                    StringComparison.Ordinal));
            var badgePanel = badge?.Child as StackPanel;
            var badgeMark = badgePanel is { Children.Count: 2 } badgeIconText
                ? badgeIconText.Children[0] as Grid
                : null;
            return textBlocks.Length > 0 &&
                   textBlocks.All(static text => text.FontWeight != FontWeights.Bold) &&
                   VisualDescendants<Button>(window)
                       .All(static button => button.FontWeight != FontWeights.Bold) &&
                   badgeMark is not null &&
                   !VisualDescendants<TextBlock>(badgeMark).Any() &&
                   VisualDescendants<System.Windows.Shapes.Ellipse>(badgeMark).Any();
        }
        finally
        {
            if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Close();
            }
        }
    }

    private static bool ButtonHasIconAndText(Button button, string text) =>
        button.Content is StackPanel panel &&
        panel.Children.Count == 2 &&
        panel.Children[0] is FrameworkElement icon &&
        icon is not TextBlock &&
        panel.Children[1] is TextBlock label &&
        string.Equals(label.Text, text, StringComparison.Ordinal);

    public static bool ProbeFivePopupsUseInstantAnimation(MainWindow main)
    {
        var combo = (ComboBox)main.FindName("DisplayPrecision");
        combo.ApplyTemplate();
        var comboPopup = combo.Template.FindName("PART_Popup", combo) as Popup;
        var datePicker = (DatePicker)main.FindName("CountdownDate");
        datePicker.ApplyTemplate();
        var datePopup = datePicker.Template.FindName("PART_Popup", datePicker) as Popup;
        var filterPopup = (Popup)main.FindName("FilterPopup");
        var tagManagerPopup = (Popup)main.FindName("TagManagerPopup");
        var selector = (TagSelector)main.FindName("CountdownTags");
        var toolTipStyle = Application.Current.TryFindResource(typeof(ToolTip)) as Style;
        var toolTipMotionEnabled = toolTipStyle?.Setters
            .OfType<Setter>()
            .Any(setter =>
                setter.Property == PopupMotionBehavior.IsToolTipEnabledProperty &&
                setter.Value is true) == true;
        return comboPopup is { PopupAnimation: PopupAnimation.None } &&
               datePopup is { PopupAnimation: PopupAnimation.None } &&
               filterPopup is { PopupAnimation: PopupAnimation.None } &&
               tagManagerPopup is { PopupAnimation: PopupAnimation.None } &&
               selector.Popup.PopupAnimation == PopupAnimation.None &&
               PopupMotionBehavior.IsAttached(comboPopup) &&
               PopupMotionBehavior.IsAttached(datePopup) &&
               PopupMotionBehavior.IsAttached(filterPopup) &&
               PopupMotionBehavior.IsAttached(tagManagerPopup) &&
               PopupMotionBehavior.IsAttached(selector.Popup) &&
               toolTipMotionEnabled;
    }

    public static bool ProbeActionForegroundsUseMainActionText(MainWindow main)
    {
        var expected = Application.Current.FindResource("MainActionTextBrush");
        var primary = new Button
        {
            Style = Application.Current.FindResource("PrimaryButton") as Style
        };
        var probe = new CheckBox();
        var host = new Window
        {
            Width = 160,
            Height = 80,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0,
            Content = new StackPanel
            {
                Children = { primary, probe }
            }
        };
        try
        {
            host.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            probe.ApplyTemplate();
            var tick = probe.Template?.FindName("Tick", probe) as System.Windows.Shapes.Path;
            return BrushEqualsResource(primary.Foreground, expected) &&
                   tick is not null &&
                   BrushEqualsResource(tick.Stroke, expected) &&
                   CalendarSelectedForegroundUsesToken(main, expected);
        }
        finally
        {
            host.Close();
        }
    }

    public static bool ProbeDisabledSelectionControlsUseButtonOpacity(MainWindow main)
    {
        var projection = (FrameworkElement)main.FindName("MainContentProjection");
        var disabledCheck = new CheckBox { IsEnabled = false };
        var disabledCombo = new ComboBox { IsEnabled = false };
        var disabledNoteCheck = new CheckBox
        {
            IsEnabled = false,
            Style = projection?.TryFindResource("MainNoteCompletionCheck") as Style
        };
        var disabledTopmostCheck = new CheckBox
        {
            IsEnabled = false,
            Style = projection?.TryFindResource("MainTopmostCheck") as Style
        };
        var host = new Window
        {
            Width = 160,
            Height = 80,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0,
            Content = new StackPanel
            {
                Children =
                {
                    disabledCheck,
                    disabledCombo,
                    disabledNoteCheck,
                    disabledTopmostCheck
                }
            }
        };
        try
        {
            host.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            return disabledNoteCheck.Style is not null &&
                   disabledTopmostCheck.Style is not null &&
                   DisabledTemplateSurfaceMatchesButtons(disabledCheck) &&
                   DisabledTemplateSurfaceMatchesButtons(disabledCombo) &&
                   DisabledTemplateSurfaceMatchesButtons(disabledNoteCheck) &&
                   DisabledTemplateSurfaceMatchesButtons(disabledTopmostCheck);
        }
        finally
        {
            host.Close();
        }
    }

    private static bool CalendarSelectedForegroundUsesToken(MainWindow main, object expected)
    {
        var datePicker = (DatePicker)main.FindName("CountdownDate");
        datePicker.ApplyTemplate();
        var wasOpen = datePicker.IsDropDownOpen;
        var originalDate = datePicker.SelectedDate;
        try
        {
            datePicker.SelectedDate ??= DateTime.Today;
            if (datePicker.SelectedDate is DateTime selectedDate)
            {
                datePicker.DisplayDate = selectedDate;
            }

            datePicker.IsDropDownOpen = true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var calendar = PickerTemplateParts.GetCalendar(datePicker);
            if (calendar is not null && datePicker.SelectedDate is DateTime visibleDate)
            {
                calendar.DisplayDate = visibleDate;
            }

            calendar?.ApplyTemplate();
            calendar?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var selected = calendar is null
                ? Array.Empty<System.Windows.Controls.Primitives.CalendarDayButton>()
                : VisualDescendants<System.Windows.Controls.Primitives.CalendarDayButton>(calendar)
                    .Where(button => button.IsSelected)
                    .ToArray();
            return selected.Length >= 1 &&
                   selected.All(button => BrushEqualsResource(button.Foreground, expected));
        }
        finally
        {
            datePicker.SelectedDate = originalDate;
            datePicker.IsDropDownOpen = wasOpen;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    private static bool BrushEqualsResource(Brush? actual, object? expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush actualBrush &&
         expected is SolidColorBrush expectedBrush &&
         actualBrush.Color == expectedBrush.Color);

    private static bool DisabledTemplateSurfaceMatchesButtons(Control control)
    {
        control.ApplyTemplate();
        control.UpdateLayout();
        WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        return control.Template?.FindName("Surface", control) is FrameworkElement surface &&
               Math.Abs(surface.Opacity - 0.45) < 0.001;
    }

    public static bool ProbeFloatingTypographyWeightWhitelist(
        FloatingWindow floating,
        DesktopWorkspace workspace) =>
        WithFloatingDateStamps(
            floating,
            workspace,
            (stamps, _) => stamps.Length > 0 &&
                           VisualDescendants<TextBlock>(floating).All(IsWhitelistedFontWeight));

    public static bool ProbeFloatingDateStampTypography(
        FloatingWindow floating,
        DesktopWorkspace workspace) =>
        WithFloatingDateStamps(
            floating,
            workspace,
            (stamps, createdDatedAndUndated) =>
                stamps.Length > 0 &&
                stamps.All(StampTypographyMatches) &&
                (!createdDatedAndUndated ||
                 (stamps.Any(stamp => StampShowsUndatedFallback(stamp)) &&
                  stamps.Any(stamp => !StampShowsUndatedFallback(stamp)))));

    public static bool ProbeFloatingDateStampDensity(
        FloatingWindow floating,
        DesktopWorkspace workspace) =>
        WithFloatingDateStamps(
            floating,
            workspace,
            (stamps, _) => stamps.Length > 0 && stamps.All(StampDensityMatches));

    /// <summary>
    ///     #149：长便签滚到底时，末卡被视口裁切，日期印记「月 / 日 / 周X」三行必须整体留在
    ///     可见区域内（可见区域内居中 / 粘性），不得只剩「日 / 周」。
    /// </summary>
    public static bool ProbeFloatingDateStampStickyAtScrollBottom(
        FloatingWindow floating,
        DesktopWorkspace workspace)
    {
        var restore = ResolveFloatingRestoreWorkspace(floating);
        var createdIds = new List<string>();
        try
        {
            // 长正文保证末卡高度超过列表视口（11×12=132 字 ≤ 便签 140 字上限，约 10 行）。
            var longText = string.Concat(Enumerable.Repeat("长便签印记粘性探针正文，", 11));
            if (!TryAddProbeNote(workspace, longText, new DateTime(2031, 4, 5), createdIds) &&
                !TryUseExistingNote(workspace, createdIds))
            {
                return false;
            }

            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            floating.UpdateLayout();
            var scroller = (ScrollViewer)floating.FindName("FloatingBodyScroller");
            var rows = (Panel)floating.FindName("FloatingRows");
            // 目标卡 = 本探针创建的长便签卡（日期排序无关）；把它的底边对齐视口底部，
            // 使「长卡被视口裁切且印记仍在可见区域」场景与列表长度无关地稳定可达。
            scroller.ScrollToBottom();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            floating.UpdateLayout();
            scroller.UpdateLayout();
            var probeNoteId = ResolveProbeNoteId(workspace, createdIds);
            var cards = rows.Children.OfType<Border>()
                .Where(card => Math.Abs(card.Width - 319) <= 0.5)
                .Where(card => VisualDescendants<CheckBox>(card).Any(checkBox =>
                    string.Equals(checkBox.Tag as string, probeNoteId, StringComparison.Ordinal)))
                .ToArray();
            var clippedCard = cards.FirstOrDefault();
            var stamp = clippedCard is null ? null : FloatingWindow.FindNoteCardDateStamp(clippedCard);
            var lines = stamp?.Child is StackPanel stampContent
                ? stampContent.Children.OfType<TextBlock>().ToArray()
                : Array.Empty<TextBlock>();
            if (clippedCard is null || stamp is null || lines.Length != 3)
            {
                return false;
            }

            var cardBounds = clippedCard.TransformToVisual(scroller).TransformBounds(
                new Rect(new Point(0, 0), clippedCard.RenderSize));
            var alignDelta = scroller.ViewportHeight - cardBounds.Bottom;
            if (Math.Abs(alignDelta) > 0.5)
            {
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset - alignDelta);
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
                floating.UpdateLayout();
                scroller.UpdateLayout();
                cardBounds = clippedCard.TransformToVisual(scroller).TransformBounds(
                    new Rect(new Point(0, 0), clippedCard.RenderSize));
            }

            var cardIsClippedByViewport =
                cardBounds.Top < -0.5 || cardBounds.Bottom > scroller.ViewportHeight + 0.5;
            var threeStampLinesStayVisible = lines.All(line =>
            {
                var bounds = line.TransformToVisual(scroller).TransformBounds(
                    new Rect(new Point(0, 0), line.RenderSize));
                return bounds.Top >= -0.5 && bounds.Bottom <= scroller.ViewportHeight + 0.5;
            });
            return cardIsClippedByViewport && threeStampLinesStayVisible;
        }
        finally
        {
            foreach (var id in createdIds)
            {
                workspace.Apply(new DeleteNoteChange(id));
            }

            floating.ShowWorkspace(restore);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    /// <summary>
    ///     #149：便签列表视口底部存在稳定内边距——滚动内容以 ≥12 DIP 的尾随内边距收尾，
    ///     滚到底时末卡与底部状态行之间保留稳定空隙，不视觉顶住底部命令区。
    /// </summary>
    public static bool ProbeFloatingNotesListBottomPadding(
        FloatingWindow floating,
        DesktopWorkspace workspace)
    {
        var restore = ResolveFloatingRestoreWorkspace(floating);
        var createdIds = new List<string>();
        try
        {
            if (!TryAddProbeNote(workspace, "底部内边距探针", new DateTime(2031, 4, 5), createdIds) &&
                !TryUseExistingNote(workspace, createdIds))
            {
                return false;
            }

            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            floating.UpdateLayout();
            var scroller = (ScrollViewer)floating.FindName("FloatingBodyScroller");
            var rows = (Panel)floating.FindName("FloatingRows");
            var cards = rows.Children.OfType<Border>()
                .Where(card => Math.Abs(card.Width - 319) <= 0.5)
                .ToArray();
            // 尾随内边距与生产实现同型：普通 FrameworkElement（非 Border，不干扰基线计卡）。
            var trailingPadding = rows.Children.OfType<FrameworkElement>().LastOrDefault();
            var trailingPaddingMatches =
                cards.Length > 0 &&
                trailingPadding is FrameworkElement pad &&
                pad.Height is double paddingHeight &&
                paddingHeight >= 12 &&
                !pad.IsHitTestVisible &&
                !pad.Focusable;
            scroller.ScrollToBottom();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            floating.UpdateLayout();
            var lastCard = cards
                .OrderBy(card => card.TransformToVisual(scroller).Transform(new Point(0, 0)).Y)
                .LastOrDefault();
            if (!trailingPaddingMatches || lastCard is null)
            {
                return false;
            }

            var lastCardBottom = lastCard.TransformToVisual(scroller)
                .Transform(new Point(0, lastCard.ActualHeight)).Y;
            var income = (TextBlock)floating.FindName("FloatingIncome");
            var incomeTop = income.TransformToVisual(scroller).Transform(new Point(0, 0)).Y;
            var stableGapAboveCommandArea = incomeTop - lastCardBottom >= 20;
            return trailingPaddingMatches && stableGapAboveCommandArea;
        }
        finally
        {
            foreach (var id in createdIds)
            {
                workspace.Apply(new DeleteNoteChange(id));
            }

            floating.ShowWorkspace(restore);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    private static WorkspaceKind ResolveFloatingRestoreWorkspace(FloatingWindow floating) =>
        floating.FloatingCountdownsButton.IsChecked == true
            ? WorkspaceKind.Countdowns
            : WorkspaceKind.Notes;

    private static bool WithFloatingDateStamps(
        FloatingWindow floating,
        DesktopWorkspace workspace,
        Func<Border[], bool, bool> evaluate)
    {
        var restore = floating.FloatingCountdownsButton.IsChecked == true
            ? WorkspaceKind.Countdowns
            : WorkspaceKind.Notes;
        var createdIds = new List<string>();
        try
        {
            var addedDated = TryAddProbeNote(
                workspace,
                "印记排版探针",
                new DateTime(2031, 4, 5),
                createdIds);
            if (!addedDated && workspace.Snapshot.Settings.Notes.Count == 0)
            {
                return false;
            }

            var addedUndated = TryAddProbeNote(workspace, "无日期印记探针", date: null, createdIds) ||
                TryAddUndatedProbeNote(workspace, createdIds);
            if (!addedUndated && workspace.Snapshot.Settings.Notes.Count == 0)
            {
                return false;
            }

            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            floating.UpdateLayout();
            var stamps = FindFloatingDateStamps(floating);
            if (stamps.Length == 0)
            {
                stamps = VisualDescendants<Border>(floating)
                    .Where(border =>
                        !border.IsHitTestVisible &&
                        !border.Focusable &&
                        border.BorderThickness.Left >= 1.5 &&
                        (border.Child as StackPanel)?.Children.OfType<TextBlock>().Count() == 3)
                    .ToArray();
            }

            return evaluate(stamps, addedDated && addedUndated);
        }
        finally
        {
            foreach (var id in createdIds)
            {
                workspace.Apply(new DeleteNoteChange(id));
            }

            floating.ShowWorkspace(restore);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    private static bool TryAddProbeNote(
        DesktopWorkspace workspace,
        string text,
        DateTime? date,
        List<string> createdIds)
    {
        var result = workspace.Apply(
            new SaveNoteChange(
                null,
                text,
                date,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
        if (!result.Succeeded)
        {
            return false;
        }

        var note = workspace.Snapshot.Settings.Notes.Single(candidate =>
            string.Equals(candidate.Text, text, StringComparison.Ordinal));
        createdIds.Add(note.Id);
        return true;
    }

    private static bool TryUseExistingNote(DesktopWorkspace workspace, List<string> createdIds)
    {
        _ = createdIds;
        return workspace.Snapshot.Settings.Notes.Count > 0;
    }

    private static string? ResolveProbeNoteId(DesktopWorkspace workspace, List<string> createdIds)
    {
        if (createdIds.Count > 0)
        {
            return createdIds[0];
        }

        return (workspace.Snapshot.Settings.Notes.FirstOrDefault(candidate => !candidate.IsCompleted)
            ?? workspace.Snapshot.Settings.Notes.FirstOrDefault())?.Id;
    }

    private static bool TryAddUndatedProbeNote(DesktopWorkspace workspace, List<string> createdIds)
    {
        if (!TryAddProbeNote(
                workspace,
                "无日期印记探针",
                new DateTime(2031, 4, 6),
                createdIds))
        {
            return false;
        }

        var id = createdIds[^1];
        var cleared = workspace.Apply(
            new SaveNoteChange(
                id,
                "无日期印记探针",
                date: null,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
        return cleared.Succeeded &&
               workspace.Snapshot.Settings.Notes.Single(note =>
                   string.Equals(note.Id, id, StringComparison.Ordinal)).Date is null;
    }

    private static Border[] FindFloatingDateStamps(FloatingWindow floating)
    {
        var rows = floating.FindName("FloatingRows") as StackPanel;
        if (rows is null)
        {
            return Array.Empty<Border>();
        }

        return rows.Children.OfType<Border>()
            .SelectMany(VisualDescendants<Border>)
            .Where(border =>
                Math.Abs(border.ActualWidth - 48) <= 0.5 &&
                border.BorderThickness.Left >= 1.5)
            .ToArray();
    }

    private static bool StampShowsUndatedFallback(Border stamp)
    {
        var labels = (stamp.Child as StackPanel)?.Children.OfType<TextBlock>().ToArray()
            ?? Array.Empty<TextBlock>();
        return labels.Length == 3 &&
               string.Equals(labels[0].Text, "无日期", StringComparison.Ordinal) &&
               string.Equals(labels[1].Text, "--", StringComparison.Ordinal) &&
               string.Equals(labels[2].Text, " ", StringComparison.Ordinal);
    }

    private static bool StampTypographyMatches(Border stamp)
    {
        if (stamp.Child is not StackPanel stack ||
            stamp.Parent is not Grid layout ||
            layout.Background is not DrawingBrush ruleBrush)
        {
            return false;
        }

        var labels = stack.Children.OfType<TextBlock>().ToArray();
        if (labels.Length != 3)
        {
            return false;
        }

        var dayFamily = labels[1].FontFamily?.Source ?? string.Empty;
        return labels[0].FontWeight == FontWeights.Regular &&
               labels[1].FontWeight == FontWeights.SemiBold &&
               labels[2].FontWeight == FontWeights.Regular &&
               dayFamily.Contains("Bahnschrift SemiCondensed", StringComparison.Ordinal) &&
               dayFamily.Contains("Bahnschrift", StringComparison.Ordinal) &&
               dayFamily.Contains("Microsoft YaHei UI", StringComparison.Ordinal) &&
               Typography.GetNumeralAlignment(labels[1]) == FontNumeralAlignment.Tabular &&
               Math.Abs(ruleBrush.Viewport.Height - 18) <= 0.1 &&
               !stamp.IsHitTestVisible &&
               !stamp.Focusable &&
               Math.Abs(stamp.ActualWidth - 48) <= 0.5;
    }

    private static bool StampDensityMatches(Border stamp)
    {
        if (stamp.Child is not StackPanel stack)
        {
            return false;
        }

        var labels = stack.Children.OfType<TextBlock>().ToArray();
        if (labels.Length != 3)
        {
            return false;
        }

        var padding = stamp.Padding;
        var firstBottom = labels[0].TranslatePoint(new Point(0, labels[0].ActualHeight), stamp).Y;
        var secondTop = labels[1].TranslatePoint(new Point(0, 0), stamp).Y;
        var secondBottom = labels[1].TranslatePoint(new Point(0, labels[1].ActualHeight), stamp).Y;
        var thirdTop = labels[2].TranslatePoint(new Point(0, 0), stamp).Y;
        var gap1 = secondTop - firstBottom;
        var gap2 = thirdTop - secondBottom;
        return Math.Abs(padding.Left - 4) <= 0.1 &&
               Math.Abs(padding.Right - 4) <= 0.1 &&
               Math.Abs(padding.Top - 8) <= 0.1 &&
               Math.Abs(padding.Bottom - 8) <= 0.1 &&
               gap1 is >= 1.5 and <= 3.5 &&
               gap2 is >= 1.5 and <= 3.5;
    }

    private static bool IsWhitelistedFontWeight(TextBlock label) =>
        label.FontWeight == FontWeights.Regular ||
        label.FontWeight == FontWeights.SemiBold;

    public static bool ProbeNoteCancelFitsEditorPanel(MainWindow main)
    {
        var originalWorkspace = main.CurrentWorkspace;
        var cancel = (Button)main.FindName("CancelNoteEdit");
        var save = (Button)main.FindName("SaveNoteButton");
        var panel = (Border)main.FindName("NoteEditorPanel");
        var surface = (Canvas)main.FindName("MainLayoutSurface");
        var originalVisibility = cancel.Visibility;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Notes);
            cancel.Visibility = Visibility.Visible;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            main.UpdateLayout();
            var panelBounds = BoundsOn(panel, surface);
            var cancelBounds = BoundsOn(cancel, surface);
            var saveBounds = BoundsOn(save, surface);
            return cancel.IsVisible &&
                   cancelBounds.Right <= panelBounds.Right + 0.5 &&
                   cancelBounds.Left >= saveBounds.Right - 0.5 &&
                   Math.Abs(cancelBounds.Top - saveBounds.Top) <= 0.5 &&
                   Math.Abs(cancel.Height - save.Height) <= 0.5;
        }
        finally
        {
            cancel.Visibility = originalVisibility;
            main.ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    public static bool ProbePopupRightInsetIsEightFromCanvas(MainWindow main)
    {
        var canvas = (Canvas)main.FindName("MainLayoutSurface");
        var filter = (Popup)main.FindName("FilterPopup");
        var tagManager = (Popup)main.FindName("TagManagerPopup");
        var selector = (TagSelector)main.FindName("NoteTags");
        var originalWorkspace = main.CurrentWorkspace;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            main.UpdateLayout();
            return PopupRightInsetIsEight(filter, canvas, requireExact: true) &&
                   PopupRightInsetIsEight(tagManager, canvas, requireExact: true) &&
                   PopupRightInsetIsEight(selector.Popup, canvas, requireExact: true);
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(filter);
            WindowValidationInput.ClosePopupForCleanup(tagManager);
            WindowValidationInput.ClosePopupForCleanup(selector.Popup);
            main.ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    public static bool ProbeFilterPopupScrollsLongTagList(
        MainWindow main,
        DesktopWorkspace workspace)
    {
        _ = workspace;
        var filter = (Popup)main.FindName("FilterPopup");
        var options = (WrapPanel)main.FindName("FilterTagOptions");
        var originalChildren = options.Children.Cast<UIElement>().ToArray();
        var originalWorkspace = main.CurrentWorkspace;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            main.UpdateLayout();
            for (var index = 0; index < 56; index++)
            {
                options.Children.Add(new ToggleButton
                {
                    Content = new string('测', 28) + index.ToString("00", CultureInfo.InvariantCulture),
                    Margin = new Thickness(0, 0, 6, 6),
                    Style = (Style)main.FindResource("TagToggleButton")
                });
            }

            filter.IsOpen = true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            if (filter.Child is not Border border)
            {
                return false;
            }

            border.UpdateLayout();
            var viewer = VisualDescendants<ScrollViewer>(border).FirstOrDefault();
            return Math.Abs(border.MaxHeight - 520) <= 0.1 &&
                   border.ActualHeight <= 520.5 &&
                   viewer is { MaxHeight: 480 } &&
                   viewer.ScrollableHeight > 0;
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(filter);
            options.Children.Clear();
            foreach (var child in originalChildren)
            {
                options.Children.Add(child);
            }

            main.ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    public static bool ProbeMainCheckboxHitCoversSlot(
        MainWindow main,
        DesktopWorkspace workspace)
    {
        var originalWorkspace = main.CurrentWorkspace;
        var createdIds = new List<string>();
        try
        {
            if (!TryAddProbeNote(workspace, "复选命中探针便签", new DateTime(2031, 4, 5), createdIds) &&
                !TryUseExistingNote(workspace, createdIds))
            {
                return false;
            }

            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            main.UpdateLayout();
            var noteId = ResolveProbeNoteId(workspace, createdIds);
            if (string.IsNullOrWhiteSpace(noteId))
            {
                return false;
            }

            var check = VisualDescendants<CheckBox>(main).SingleOrDefault(candidate =>
                string.Equals(candidate.Tag as string, noteId, StringComparison.OrdinalIgnoreCase));
            if (check is null)
            {
                return false;
            }

            check.UpdateLayout();
            var topLeft = check.InputHitTest(new Point(1, 1));
            var bottomRight = check.InputHitTest(new Point(
                Math.Max(1, check.ActualWidth - 1),
                Math.Max(1, check.ActualHeight - 1)));
            // #152/#208：真实 DIP 槽位 38，直接达到桌面紧凑下限 24。
            return Math.Abs(check.ActualWidth - 38) <= 1 &&
                   Math.Abs(check.ActualHeight - 38) <= 1 &&
                   check.Tag as string == noteId &&
                   IsSelfOrVisualDescendant(check, topLeft as DependencyObject) &&
                   IsSelfOrVisualDescendant(check, bottomRight as DependencyObject);
        }
        finally
        {
            foreach (var id in createdIds)
            {
                workspace.Apply(new DeleteNoteChange(id));
            }

            main.ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    public static bool ProbeFloatingSearchSlotRightIs396(FloatingWindow floating)
    {
        var restore = floating.FloatingCountdownsButton.IsChecked == true
            ? WorkspaceKind.Countdowns
            : WorkspaceKind.Notes;
        var search = (Button)floating.FindName("FloatingSearchNotes");
        var slot = (FrameworkElement)floating.FindName("FloatingNoteSearchSlot");
        try
        {
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            if (slot.Visibility != Visibility.Visible)
            {
                search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            }

            floating.UpdateLayout();
            slot.UpdateLayout();
            var right = slot.TranslatePoint(new Point(slot.ActualWidth, 0), floating).X;
            return Math.Abs(right - 396) <= 1.5;
        }
        finally
        {
            if (slot.Visibility == Visibility.Visible)
            {
                var inputRow = (FrameworkElement)floating.FindName("FloatingNoteSearchInputRow");
                if (inputRow.Visibility == Visibility.Visible)
                {
                    search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
                }
            }

            floating.ShowWorkspace(restore);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    public static bool ProbeFloatingSearchHintMatchesInputHeight(FloatingWindow floating)
    {
        var restore = floating.FloatingCountdownsButton.IsChecked == true
            ? WorkspaceKind.Countdowns
            : WorkspaceKind.Notes;
        var search = (Button)floating.FindName("FloatingSearchNotes");
        var input = (TextBox)floating.FindName("FloatingNoteSearch");
        var inputRow = (FrameworkElement)floating.FindName("FloatingNoteSearchInputRow");
        var hint = (FrameworkElement)floating.FindName("FloatingNoteSearchHint");
        var scroller = (ScrollViewer)floating.FindName("FloatingBodyScroller");
        var originalText = input.Text;
        try
        {
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            if (inputRow.Visibility != Visibility.Visible)
            {
                search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            }

            floating.UpdateLayout();
            var inputHeight = inputRow.ActualHeight;
            var expandedScrollerHeight = scroller.ActualHeight;
            input.Text = "布局等高探针";
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            floating.UpdateLayout();
            return hint.Visibility == Visibility.Visible &&
                   Math.Abs(hint.ActualHeight - inputHeight) <= 1 &&
                   Math.Abs(scroller.ActualHeight - expandedScrollerHeight) <= 1;
        }
        finally
        {
            input.Text = originalText;
            if (hint.Visibility == Visibility.Visible)
            {
                search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            }

            if (inputRow.Visibility == Visibility.Visible)
            {
                search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            }

            floating.ShowWorkspace(restore);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    public static bool ProbeFloatingQuickAddAlignsWithNoteCards(
        FloatingWindow floating,
        DesktopWorkspace workspace)
    {
        var restore = floating.FloatingCountdownsButton.IsChecked == true
            ? WorkspaceKind.Countdowns
            : WorkspaceKind.Notes;
        var createdIds = new List<string>();
        try
        {
            if (!TryAddProbeNote(workspace, "快速新增对齐探针", new DateTime(2031, 4, 5), createdIds) &&
                !TryUseExistingNote(workspace, createdIds))
            {
                return false;
            }

            floating.ShowWorkspace(WorkspaceKind.Notes);
            floating.SetQuickAddExpanded(true);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            floating.UpdateLayout();
            var entry = (FrameworkElement)floating.FindName("FloatingNoteEntry");
            var rows = (Panel)floating.FindName("FloatingRows");
            var card = rows.Children.OfType<FrameworkElement>().FirstOrDefault();
            if (card is null || !entry.IsVisible)
            {
                return false;
            }

            var cardRight = card.TranslatePoint(new Point(card.ActualWidth, 0), floating).X;
            var entryRight = entry.TranslatePoint(new Point(entry.ActualWidth, 0), floating).X;
            return Math.Abs(cardRight - entryRight) <= 1.5 &&
                   Math.Abs(card.ActualWidth - 319) <= 0.5;
        }
        finally
        {
            foreach (var id in createdIds)
            {
                workspace.Apply(new DeleteNoteChange(id));
            }

            floating.ShowWorkspace(restore);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    public static bool ProbeFloatingCheckboxHitCoversSlot(
        FloatingWindow floating,
        DesktopWorkspace workspace)
    {
        var restore = floating.FloatingCountdownsButton.IsChecked == true
            ? WorkspaceKind.Countdowns
            : WorkspaceKind.Notes;
        var createdIds = new List<string>();
        try
        {
            if (!TryAddProbeNote(workspace, "悬浮复选命中探针", new DateTime(2031, 4, 5), createdIds) &&
                !TryUseExistingNote(workspace, createdIds))
            {
                return false;
            }

            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            floating.UpdateLayout();
            var noteId = ResolveProbeNoteId(workspace, createdIds);
            if (string.IsNullOrWhiteSpace(noteId))
            {
                return false;
            }

            var check = VisualDescendants<CheckBox>(floating).SingleOrDefault(candidate =>
                string.Equals(candidate.Tag as string, noteId, StringComparison.OrdinalIgnoreCase));
            if (check is null || VisualTreeHelper.GetParent(check) is not Grid layout)
            {
                return false;
            }

            layout.UpdateLayout();
            var slotWidth = layout.ColumnDefinitions[0].ActualWidth;
            var topLeft = layout.InputHitTest(new Point(2, 12));
            var slotEdge = layout.InputHitTest(new Point(Math.Max(2, slotWidth - 2), 12));
            var stampHit = layout.InputHitTest(new Point(layout.ActualWidth - 8, 12));
            return slotWidth >= 33.5 &&
                   check.ActualWidth >= 32 &&
                   check.Tag as string == noteId &&
                   IsSelfOrVisualDescendant(check, topLeft as DependencyObject) &&
                   IsSelfOrVisualDescendant(check, slotEdge as DependencyObject) &&
                   !IsSelfOrVisualDescendant(check, stampHit as DependencyObject);
        }
        finally
        {
            foreach (var id in createdIds)
            {
                workspace.Apply(new DeleteNoteChange(id));
            }

            floating.ShowWorkspace(restore);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    private static bool PopupRightInsetIsEight(Popup popup, Canvas canvas, bool requireExact)
    {
        var wasOpen = popup.IsOpen;
        try
        {
            popup.IsOpen = true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            if (popup.Child is not FrameworkElement child)
            {
                return false;
            }

            child.UpdateLayout();
            canvas.UpdateLayout();
            if (popup.PlacementTarget is not FrameworkElement target)
            {
                return false;
            }

            target.UpdateLayout();
            var dip = VisualTreeHelper.GetDpi(child).PixelsPerDip;
            if (dip <= 0)
            {
                dip = 1;
            }

            var desiredRight = canvas.PointToScreen(new Point(canvas.ActualWidth - 8, 0)).X;
            var canvasRight = canvas.PointToScreen(new Point(canvas.ActualWidth, 0)).X;
            var targetLeft = target.PointToScreen(new Point(0, 0)).X;
            var childRight = targetLeft + (popup.HorizontalOffset * dip) + (child.ActualWidth * dip);
            var delta = childRight - desiredRight;
            return childRight <= canvasRight + 0.5 &&
                   (requireExact ? Math.Abs(delta) <= 1.5 : delta <= 1.5);
        }
        finally
        {
            popup.IsOpen = wasOpen;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
        }
    }

    private static bool IsSelfOrVisualDescendant(DependencyObject root, DependencyObject? node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, root))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private static Rect BoundsOn(FrameworkElement element, Visual ancestor)
    {
        element.UpdateLayout();
        return element.TransformToVisual(ancestor).TransformBounds(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    private static bool RunAclDeniedMainRecoveryWiringProbe()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-acl-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllBytes(settingsPath, Encoding.UTF8.GetBytes("{acl-denied-main"));
            File.WriteAllBytes(
                settingsPath + ".backup1",
                Encoding.UTF8.GetBytes(SettingsContract.SerializeCanonical(
                    CountdownSettings.Empty with
                    {
                        Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 2468m }
                    })));
            var fileOperations = new AclDeniedMainSettingsFileOperations(settingsPath);
            var promptCount = 0;
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(fileOperations: fileOperations),
                _ =>
                {
                    promptCount++;
                    return SettingsRecoveryDecision.RecoverBackup;
                });

            return promptCount == 1 &&
                   workspace.Snapshot.Settings.Preferences.MonthlySalary == 2468m &&
                   fileOperations.DenialClearedByReplace;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static bool RunInstanceActivationFailurePromptBehavior()
    {
        var window = InstanceActivationFailureWindow.CreateForValidation();
        try
        {
            var body = (window.Content as Border)?.Child as StackPanel;
            var texts = body?.Children.OfType<TextBlock>().ToArray() ?? Array.Empty<TextBlock>();
            var commands = body?.Children
                .OfType<StackPanel>()
                .SingleOrDefault(panel => panel.Orientation == Orientation.Horizontal);
            var buttons = commands?.Children.OfType<Button>().ToArray() ?? Array.Empty<Button>();
            var dismiss = buttons.SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetName(button),
                    InstanceActivationFailureWindow.DismissButtonText,
                    StringComparison.Ordinal));

            return window.Title == InstanceActivationFailureWindow.PromptTitle &&
                   texts.Any(text =>
                       string.Equals(
                           text.Text,
                           InstanceActivationFailureWindow.PromptHeading,
                           StringComparison.Ordinal)) &&
                   texts.Any(text =>
                       string.Equals(
                           text.Text,
                           InstanceActivationFailureWindow.PromptMessage,
                           StringComparison.Ordinal)) &&
                   buttons.Length == 1 &&
                   dismiss is { IsDefault: true, IsCancel: true, IsEnabled: true };
        }
        finally
        {
            if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Close();
            }
        }
    }

    public static bool RunInstanceActivationStateEqualityBehavior()
    {
        var filterA = DesktopWorkspaceFilter.Empty with
        {
            SelectedTagIds = new[] { "instance-activation-tag-a", "instance-activation-tag-b" }
        };
        var filterB = DesktopWorkspaceFilter.Empty with
        {
            SelectedTagIds = new[] { "INSTANCE-ACTIVATION-TAG-A", "INSTANCE-ACTIVATION-TAG-B" }
        };
        var stateA = new MainWindowActivationState(
            WorkspaceKind.Notes,
            filterA,
            "instance-activation-countdown-draft",
            "instance-activation-note-draft",
            "instance-activation-search",
            new DateTime(2031, 4, 5),
            null,
            null);
        var stateB = stateA with { Filter = filterB };

        var sameContentEquals = stateA == stateB;
        var tagChangeBreaks = stateA != stateA with
        {
            Filter = filterA with
            {
                SelectedTagIds = new[] { "instance-activation-tag-a", "instance-activation-tag-c" }
            }
        };
        var tagCountChangeBreaks = stateA != stateA with
        {
            Filter = filterA with { SelectedTagIds = new[] { "instance-activation-tag-a" } }
        };
        var draftChangeBreaks = stateA != stateA with { CountdownDraft = "changed-draft" };
        var searchChangeBreaks = stateA != stateA with { NoteSearch = "changed-search" };

        return sameContentEquals &&
               tagChangeBreaks &&
               tagCountChangeBreaks &&
               draftChangeBreaks &&
               searchChangeBreaks;
    }

    public static bool RunCountdownReminderTransactionBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-countdown-reminder-{Guid.NewGuid():N}.settings.json");
        var nowUtc = new DateTimeOffset(2037, 6, 1, 2, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var relative = new CountdownRelativeReminderRule("countdown-relative-rule", 60);
            var initialTargetUtc = nowUtc.AddHours(2);
            MustApply(workspace, new SaveCountdownChange(
                null,
                "相对提醒事务探针",
                initialTargetUtc,
                Array.Empty<string>(),
                new ReminderRule[] { relative }));
            var countdown = workspace.Snapshot.Settings.Countdowns.Single();
            var initialScheduledUtc = nowUtc.AddHours(1);
            clock.UtcNow = initialScheduledUtc;
            var initialDue = workspace.GetDueReminderOccurrences(clock.UtcNow).Single();
            var initialKey = ReminderOccurrenceKey.Create(initialDue.Rule, initialDue.ScheduledUtc).ToString();
            Must(
                initialDue.SourceKind == ReminderSourceKind.Countdown &&
                initialDue.SourceId == countdown.Id &&
                initialDue.ScheduledUtc == initialScheduledUtc,
                "到点前相对 occurrence 没有按 target-offset 生成。");

            var laterTargetUtc = initialTargetUtc.AddHours(1);
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                laterTargetUtc,
                countdown.TagIds,
                countdown.ReminderRules));
            Must(workspace.GetDueReminderOccurrences(clock.UtcNow).Count == 0,
                "目标后移后仍保留未送达旧相对 occurrence。");
            var laterScheduledUtc = laterTargetUtc.AddMinutes(-60);
            clock.UtcNow = laterScheduledUtc;
            var laterDue = workspace.GetDueReminderOccurrences(clock.UtcNow).Single();
            var laterKey = ReminderOccurrenceKey.Create(laterDue.Rule, laterDue.ScheduledUtc).ToString();
            Must(
                laterDue.Rule.Id == relative.Id &&
                laterKey != initialKey &&
                laterDue.ScheduledUtc == laterScheduledUtc,
                "目标后移没有保留 rule ID 并生成新 occurrence key。");

            var earlierTargetUtc = clock.UtcNow.AddMinutes(90);
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                earlierTargetUtc,
                countdown.TagIds,
                countdown.ReminderRules));
            var earlierScheduledUtc = clock.UtcNow.AddMinutes(30);
            clock.UtcNow = earlierScheduledUtc;
            var earlierDue = workspace.GetDueReminderOccurrences(clock.UtcNow).Single();
            Must(earlierDue.ScheduledUtc == earlierScheduledUtc,
                "目标前移没有重算未来相对 occurrence。");

            var memoryBeforeRejectedEdit = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var diskBeforeRejectedEdit = File.ReadAllText(settingsPath);
            var rejected = workspace.Apply(new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                clock.UtcNow.AddMinutes(60),
                countdown.TagIds,
                countdown.ReminderRules));
            Must(
                !rejected.Succeeded &&
                rejected.ReminderRuleErrorTarget == ReminderRuleEditingErrorTarget.RelativeOffset &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == memoryBeforeRejectedEdit &&
                File.ReadAllText(settingsPath) == diskBeforeRejectedEdit,
                "过去派生时刻拒绝没有原子保留目标、规则、内存与磁盘。");

            var currentCountdown = workspace.Snapshot.Settings.Countdowns.Single();
            Must(workspace.ConfirmReminderDisplayed(
                    ReminderSourceKind.Countdown,
                    currentCountdown.Id,
                    relative.Id,
                    earlierScheduledUtc).Succeeded,
                "倒计时相对 occurrence 无法确认送达。");
            MustApply(workspace, new SaveCountdownChange(
                currentCountdown.Id,
                currentCountdown.Name,
                currentCountdown.TargetUtc,
                currentCountdown.TagIds,
                Array.Empty<ReminderRule>()));
            Must(!workspace.Snapshot.Settings.ReminderStates.ContainsKey(relative.Id),
                "删除倒计时规则没有在同一事务清理状态。");

            var replacement = new CountdownRelativeReminderRule("countdown-relative-delete", 0);
            var replacementTargetUtc = clock.UtcNow.AddMinutes(10);
            MustApply(workspace, new SaveCountdownChange(
                currentCountdown.Id,
                currentCountdown.Name,
                replacementTargetUtc,
                currentCountdown.TagIds,
                new ReminderRule[] { replacement }));
            clock.UtcNow = replacementTargetUtc;
            var replacementCountdown = workspace.Snapshot.Settings.Countdowns.Single();
            Must(workspace.ConfirmReminderDisplayed(
                    ReminderSourceKind.Countdown,
                    replacementCountdown.Id,
                    replacement.Id,
                    replacementTargetUtc).Succeeded,
                "删除探针 occurrence 无法确认送达。");
            MustApply(workspace, new DeleteCountdownChange(replacementCountdown.Id));
            Must(
                workspace.Snapshot.Settings.Countdowns.Count == 0 &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(replacement.Id),
                "删除倒计时没有在同一事务清理规则状态。");
            return true;
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
        }
    }

    public static bool RunCountdownReminderWpfBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-countdown-reminder-wpf-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        var relative = new CountdownRelativeReminderRule("countdown-wpf-relative", 0);
        var targetUtc = nowUtc.AddMinutes(10);
        MustApply(workspace, new SaveCountdownChange(
            null,
            "真实 WPF 倒计时提醒",
            targetUtc,
            Array.Empty<string>(),
            new ReminderRule[] { relative }));
        var countdown = workspace.Snapshot.Settings.Countdowns.Single();
        MustApply(workspace, new SaveCountdownChange(
            null,
            "未保存草稿来源",
            nowUtc.AddDays(2),
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var draftCountdown = workspace.Snapshot.Settings.Countdowns.Single(item =>
            !string.Equals(item.Id, countdown.Id, StringComparison.OrdinalIgnoreCase));

        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        (ReminderSourceKind Kind, string Id)? openedSource = null;
        using var coordinator = new ReminderDeliveryCoordinator(
            workspace,
            logger,
            (kind, id) =>
            {
                openedSource = (kind, id);
                main.OpenReminderSource(kind, id);
            });

        try
        {
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            clock.UtcNow = targetUtc;
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(main.Dispatcher);

            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("倒计时提醒中心未创建。");
            var key = new ActiveReminderOccurrenceKey(relative.Id, targetUtc);
            Must(
                coordinator.ActiveEntryCount == 1 &&
                coordinator.ActiveOccurrenceConfirmed &&
                center.IsAcknowledgeEnabled(key) &&
                center.IsSnoozeEnabled(key) &&
                center.IsOpenSourceEnabled(key) &&
                VisualDescendants<TextBlock>(center).Any(text =>
                    string.Equals(text.Text, "倒计时", StringComparison.Ordinal)) &&
                VisualDescendants<TextBlock>(center).Any(text =>
                    string.Equals(text.Text, countdown.Name, StringComparison.Ordinal)) &&
                VisualDescendants<Button>(center).All(button =>
                    !AutomationProperties.GetName(button).StartsWith("完成便签：", StringComparison.Ordinal)),
                "倒计时 occurrence 未进入只含安全动作的提醒中心。");

            var movedTargetUtc = targetUtc.AddHours(1);
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                movedTargetUtc,
                countdown.TagIds,
                countdown.ReminderRules));
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(coordinator.ActiveEntryCount == 1,
                "目标变化错误移除了已经确认送达、仍待处置的旧 occurrence。");

            var draftEdit = VisualDescendants<Button>(main).Single(button =>
                string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                string.Equals(button.Tag as string, draftCountdown.Id, StringComparison.OrdinalIgnoreCase));
            draftEdit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var draftName = (TextBox)main.FindName("CountdownName");
            draftName.Text = "未保存的倒计时草稿";
            var draftSecond = (ComboBox)main.FindName("CountdownSecond");
            draftSecond.SelectedIndex = (draftSecond.SelectedIndex + 7) % 60;
            var expectedDraftSecond = draftSecond.SelectedIndex;
            ((ToggleButton)main.FindName("CountdownExpiredFilter"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                workspace.Snapshot.Filter.CountdownStatus == CountdownStatusFilter.Expired &&
                !VisualDescendants<Button>(main).Any(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, countdown.Id, StringComparison.OrdinalIgnoreCase)),
                "来源导航探针没有建立排除目标倒计时的筛选状态。");

            Must(center.RequestOpenSource(key), "倒计时提醒中心未触发来源导航。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                openedSource == (ReminderSourceKind.Countdown, countdown.Id) &&
                main.CurrentWorkspace == WorkspaceKind.Countdowns &&
                main.EditingCountdownId == draftCountdown.Id &&
                string.Equals(draftName.Text, "未保存的倒计时草稿", StringComparison.Ordinal) &&
                draftSecond.SelectedIndex == expectedDraftSecond &&
                string.Equals(
                    ((TextBlock)main.FindName("CountdownReminderRulesSummary")).Text,
                    "不提醒",
                    StringComparison.Ordinal) &&
                workspace.Snapshot.Filter.CountdownStatus == CountdownStatusFilter.Expired &&
                VisualDescendants<Button>(main).Any(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, countdown.Id, StringComparison.OrdinalIgnoreCase)) &&
                Keyboard.FocusedElement is Button { Tag: string focusedId } &&
                string.Equals(focusedId, countdown.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    ((TextBlock)main.FindName("CountdownEditorTitle")).Text,
                    "编辑倒计时",
                    StringComparison.Ordinal),
                "倒计时提醒来源导航没有临时揭示并聚焦来源，或覆盖了既有草稿与筛选。");

            Must(center.RequestSnooze(key, ReminderSnoozeOption.TenMinutes, null),
                "倒计时提醒中心未触发稍后提醒。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                workspace.Snapshot.Settings.ReminderStates[relative.Id].ActiveSnooze is not null,
                "倒计时稍后语义没有持久化并移除当前中心条目。");

            var repeating = new FixedIntervalReminderRule(
                "countdown-wpf-repeat",
                clock.UtcNow.AddMinutes(1),
                new ReminderInterval(30));
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                clock.UtcNow.AddHours(2),
                countdown.TagIds,
                new ReminderRule[] { repeating }));
            clock.UtcNow = repeating.FirstDueUtc;
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(main.Dispatcher);
            var repeatingKey = new ActiveReminderOccurrenceKey(repeating.Id, repeating.FirstDueUtc);
            Must(
                coordinator.ActiveWindow?.RequestAcknowledgement(repeatingKey) == true,
                "倒计时固定间隔提醒未触发知道了。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                workspace.Snapshot.Settings.ReminderStates[repeating.Id]
                    .LastDispositionOccurrenceKey is not null,
                "倒计时知道了语义没有持久化并移除当前中心条目。");

            var rescheduledRelative = new CountdownRelativeReminderRule(
                "countdown-wpf-reschedule-relative",
                0);
            var originalRelativeTargetUtc = clock.UtcNow.AddMinutes(1);
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                originalRelativeTargetUtc,
                countdown.TagIds,
                new ReminderRule[] { rescheduledRelative }));
            Directory.CreateDirectory(settingsPath + ".tmp");
            clock.UtcNow = originalRelativeTargetUtc;
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                coordinator.ActiveEntryCount == 1 &&
                coordinator.HasPendingPersistenceRetry,
                "未送达 relative 改期探针没有保留 delivery retry。");
            var movedRelativeTargetUtc = originalRelativeTargetUtc.AddHours(1);
            var failedReschedule = workspace.Apply(new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                movedRelativeTargetUtc,
                countdown.TagIds,
                new ReminderRule[] { rescheduledRelative }));
            Must(
                !failedReschedule.Succeeded &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.HasPendingPersistenceRetry &&
                workspace.Snapshot.Settings.Countdowns.Single(item =>
                    string.Equals(item.Id, countdown.Id, StringComparison.OrdinalIgnoreCase)).TargetUtc ==
                    originalRelativeTargetUtc,
                "relative 改期写盘失败没有原子保留目标、中心条目和 delivery retry。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                movedRelativeTargetUtc,
                countdown.TagIds,
                new ReminderRule[] { rescheduledRelative }));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                !coordinator.HasPendingPersistenceRetry,
                "未送达 relative occurrence 改期成功后没有在同一事务清理中心条目和 retry。");

            var deleteRule = new AbsoluteOnceReminderRule(
                "countdown-wpf-delete-rule",
                clock.UtcNow.AddMinutes(1));
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                clock.UtcNow.AddHours(3),
                countdown.TagIds,
                new ReminderRule[] { deleteRule }));
            clock.UtcNow = deleteRule.ScheduledUtc;
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(coordinator.ActiveEntryCount == 1, "规则删除探针没有进入提醒中心。");
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                clock.UtcNow.AddHours(3),
                countdown.TagIds,
                Array.Empty<ReminderRule>()));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0,
                "删除倒计时规则后提醒中心没有在同一事务移除条目。");

            var deleteCountdown = new CountdownRelativeReminderRule(
                "countdown-wpf-delete-countdown",
                0);
            var deleteCountdownTargetUtc = clock.UtcNow.AddMinutes(1);
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                deleteCountdownTargetUtc,
                countdown.TagIds,
                new ReminderRule[] { deleteCountdown }));
            clock.UtcNow = deleteCountdownTargetUtc;
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(coordinator.ActiveEntryCount == 1, "倒计时删除探针没有进入提醒中心。");
            MustApply(workspace, new DeleteCountdownChange(countdown.Id));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0,
                "删除倒计时后提醒中心没有在同一事务移除条目。");
            Must(RunHistoricalRelativeRestartCoordinatorBehavior(),
                "已送达 relative occurrence 改期并重启后未能幂等恢复处置，或错误接受了未送达旧 occurrence。");
            return true;
        }
        finally
        {
            coordinator.Dispose();
            if (main.IsVisible)
            {
                main.Close();
            }

            foreach (var window in Application.Current.Windows
                         .OfType<ReminderCenterWindow>()
                         .ToArray())
            {
                window.ForceClose();
            }

            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static bool RunHistoricalRelativeRestartCoordinatorBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-relative-restart-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2040, 8, 9, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        ReminderDeliveryCoordinator? initialCoordinator = null;
        ReminderDeliveryCoordinator? restartedCoordinator = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var acknowledgeRule = new CountdownRelativeReminderRule("relative-restart-ack", 60);
            var snoozeRule = new CountdownRelativeReminderRule("relative-restart-snooze", 30);
            var neverDeliveredRule = new CountdownRelativeReminderRule("relative-restart-never", 0);
            var initialTargetUtc = nowUtc.AddHours(2);
            MustApply(workspace, new SaveCountdownChange(
                null,
                "重启历史 occurrence 探针",
                initialTargetUtc,
                Array.Empty<string>(),
                new ReminderRule[] { acknowledgeRule, snoozeRule, neverDeliveredRule }));
            var countdown = workspace.Snapshot.Settings.Countdowns.Single();
            var logger = new DiagnosticLogger(diagnosticPath);
            initialCoordinator = new ReminderDeliveryCoordinator(workspace, logger);

            var acknowledgeScheduledUtc = initialTargetUtc.AddMinutes(-60);
            clock.UtcNow = acknowledgeScheduledUtc;
            initialCoordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(initialCoordinator.ActiveOccurrenceConfirmed,
                "重启探针的第一个 relative occurrence 未确认送达。");

            var snoozeScheduledUtc = initialTargetUtc.AddMinutes(-30);
            clock.UtcNow = snoozeScheduledUtc;
            initialCoordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                initialCoordinator.ActiveEntryCount == 2 &&
                workspace.Snapshot.Settings.ReminderStates.ContainsKey(acknowledgeRule.Id) &&
                workspace.Snapshot.Settings.ReminderStates.ContainsKey(snoozeRule.Id) &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(neverDeliveredRule.Id),
                "重启探针没有只持久化真实送达的两个 relative occurrence。");

            var movedTargetUtc = initialTargetUtc.AddHours(2);
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                movedTargetUtc,
                countdown.TagIds,
                countdown.ReminderRules));
            initialCoordinator.Dispose();
            initialCoordinator = null;

            workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            restartedCoordinator = new ReminderDeliveryCoordinator(workspace, logger);
            restartedCoordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);

            var acknowledgeKey = new ActiveReminderOccurrenceKey(
                acknowledgeRule.Id,
                acknowledgeScheduledUtc);
            var snoozeKey = new ActiveReminderOccurrenceKey(
                snoozeRule.Id,
                snoozeScheduledUtc);
            var center = restartedCoordinator.ActiveWindow ??
                         throw new InvalidOperationException("重启后提醒中心未恢复历史 relative occurrence。");
            Must(
                restartedCoordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(acknowledgeKey) &&
                center.IsSnoozeEnabled(snoozeKey) &&
                !restartedCoordinator.HasPendingPersistenceRetry,
                "重启后历史 relative occurrence 的 delivery confirm 未幂等成功。");

            var forged = workspace.ConfirmReminderDisplayed(
                ReminderSourceKind.Countdown,
                countdown.Id,
                neverDeliveredRule.Id,
                initialTargetUtc);
            Must(
                !forged.Succeeded &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(neverDeliveredRule.Id),
                "重启后错误接受了从未送达的旧 relative occurrence。");

            Must(center.RequestAcknowledgement(acknowledgeKey),
                "重启后历史 relative occurrence 未开放知道了。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(center.RequestSnooze(snoozeKey, ReminderSnoozeOption.TenMinutes, null),
                "重启后历史 relative occurrence 未开放稍后。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                restartedCoordinator.ActiveEntryCount == 0 &&
                workspace.Snapshot.Settings.ReminderStates[acknowledgeRule.Id]
                    .LastDispositionOccurrenceKey is not null &&
                workspace.Snapshot.Settings.ReminderStates[snoozeRule.Id].ActiveSnooze is not null,
                "重启后的知道了与稍后没有持久化并移除历史 occurrence。");
            return true;
        }
        finally
        {
            restartedCoordinator?.Dispose();
            initialCoordinator?.Dispose();
            foreach (var window in Application.Current.Windows
                         .OfType<ReminderCenterWindow>()
                         .ToArray())
            {
                window.ForceClose();
            }

            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static bool ThrowsArgumentException(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    public static bool RunReminderDispositionAndSnoozeBehavior()
    {
        var firstDueUtc = new DateTimeOffset(2036, 3, 9, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-disposition-snooze-{Guid.NewGuid():N}.settings.json");
        try
        {
            var clock = new AdvancingValidationClock(firstDueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "稍后提醒重启探针",
                    new DateTime(2036, 3, 9),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(firstDueUtc, 15))).Succeeded,
                "无法建立稍后提醒探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = ReminderOccurrenceKey.Create(rule, firstDueUtc).ToString();
            clock.UtcNow = firstDueUtc;
            Must(
                workspace.ConfirmReminderDisplayed(note, rule.Id, firstDueUtc).Succeeded,
                "稍后提醒探针无法确认真实送达。");

            clock.UtcNow = firstDueUtc.AddMinutes(5);
            var ruleBeforeSnooze = note.ReminderRules.Single();
            Must(
                workspace.SnoozeReminder(
                    note,
                    rule.Id,
                    firstDueUtc,
                    ReminderSnoozeOption.TenMinutes,
                    customBeijing: null).Succeeded,
                "10 分钟稍后提醒没有成功持久化。");
            var state = workspace.Snapshot.Settings.ReminderStates[rule.Id];
            Must(
                state.ActiveSnooze is
                {
                    OccurrenceKey: var snoozedKey,
                    OriginalScheduledUtc: var originalUtc,
                    SnoozeUntilUtc: var snoozeUntilUtc
                } &&
                snoozedKey == key &&
                originalUtc == firstDueUtc &&
                snoozeUntilUtc == firstDueUtc.AddMinutes(15) &&
                Equals(ruleBeforeSnooze, workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single()),
                "10 分钟稍后提醒没有保留 occurrence 与基础规则。");

            clock.UtcNow = firstDueUtc.AddMinutes(14);
            var restartedEarly = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(restartedEarly.GetDueReminderOccurrences(clock.UtcNow).Count == 0,
                "稍后覆盖到期前发生项提前出现。");
            clock.UtcNow = firstDueUtc.AddMinutes(15);
            var restartedDue = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var due = restartedDue.GetDueReminderOccurrences(clock.UtcNow).Single();
            Must(
                ReminderOccurrenceKey.Create(due.Rule, due.ScheduledUtc).ToString() == key &&
                due.ScheduledUtc == firstDueUtc,
                "重启后没有按覆盖时刻恢复同一 occurrence。");

            var restartedNote = restartedDue.Snapshot.Settings.Notes.Single();
            clock.UtcNow = firstDueUtc.AddMinutes(20);
            Must(restartedDue.SnoozeReminder(
                    restartedNote,
                    rule.Id,
                    firstDueUtc,
                    ReminderSnoozeOption.ThirtyMinutes,
                    null).Succeeded &&
                restartedDue.Snapshot.Settings.ReminderStates[rule.Id].ActiveSnooze?.SnoozeUntilUtc ==
                    firstDueUtc.AddMinutes(50),
                "30 分钟稍后提醒没有相对成功动作时刻计算。");
            clock.UtcNow = firstDueUtc.AddMinutes(21);
            Must(restartedDue.SnoozeReminder(
                    restartedNote,
                    rule.Id,
                    firstDueUtc,
                    ReminderSnoozeOption.SixtyMinutes,
                    null).Succeeded &&
                restartedDue.Snapshot.Settings.ReminderStates[rule.Id].ActiveSnooze?.SnoozeUntilUtc ==
                    firstDueUtc.AddMinutes(81),
                "1 小时稍后提醒没有相对成功动作时刻计算。");

            clock.UtcNow = new DateTimeOffset(2036, 3, 9, 15, 55, 0, TimeSpan.Zero);
            Must(restartedDue.SnoozeReminder(
                    restartedNote,
                    rule.Id,
                    firstDueUtc,
                    ReminderSnoozeOption.TomorrowAtNine,
                    null).Succeeded &&
                restartedDue.Snapshot.Settings.ReminderStates[rule.Id].ActiveSnooze?.SnoozeUntilUtc ==
                    new DateTimeOffset(2036, 3, 10, 1, 0, 0, TimeSpan.Zero),
                "明天没有按下一个北京时间日历日 09:00 计算。");

            var beforeInvalidCustom = SettingsContract.SerializeCanonical(restartedDue.Snapshot.Settings);
            var diskBeforeInvalidCustom = File.ReadAllText(settingsPath);
            var currentBeijing = ChinaStandardTime.ToBeijingTime(clock.UtcNow);
            var invalidCustom = restartedDue.SnoozeReminder(
                restartedNote,
                rule.Id,
                firstDueUtc,
                ReminderSnoozeOption.Custom,
                currentBeijing);
            Must(
                !invalidCustom.Succeeded &&
                invalidCustom.Error == "自定义稍后时间必须晚于当前北京时间。" &&
                SettingsContract.SerializeCanonical(restartedDue.Snapshot.Settings) == beforeInvalidCustom &&
                File.ReadAllText(settingsPath) == diskBeforeInvalidCustom,
                "无效自定义稍后时间改变了内存或磁盘状态。");

            var validCustomBeijing = currentBeijing.AddMinutes(35);
            Must(restartedDue.SnoozeReminder(
                    restartedNote,
                    rule.Id,
                    firstDueUtc,
                    ReminderSnoozeOption.Custom,
                    validCustomBeijing).Succeeded &&
                restartedDue.Snapshot.Settings.ReminderStates[rule.Id].ActiveSnooze?.SnoozeUntilUtc ==
                    validCustomBeijing.ToUniversalTime(),
                "未来自定义北京时间没有规范保存为 UTC。");

            var beforePersistenceFailure = SettingsContract.SerializeCanonical(restartedDue.Snapshot.Settings);
            var diskBeforePersistenceFailure = File.ReadAllText(settingsPath);
            Directory.CreateDirectory(settingsPath + ".tmp");
            var failedSnooze = restartedDue.SnoozeReminder(
                restartedNote,
                rule.Id,
                firstDueUtc,
                ReminderSnoozeOption.TenMinutes,
                null);
            Must(
                !failedSnooze.Succeeded &&
                SettingsContract.SerializeCanonical(restartedDue.Snapshot.Settings) == beforePersistenceFailure &&
                File.ReadAllText(settingsPath) == diskBeforePersistenceFailure,
                "稍后提醒保存失败没有原子保留内存与磁盘状态。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            return true;
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderSnoozeSuppressesBaseCyclesBehavior()
    {
        RunFixedIntervalSnoozeSuppressesBaseCycles();
        RunCalendarSnoozeSuppressesBaseCycles();
        RunDeliveryFailureSuppressesNewBaseCycles();
        return true;
    }

    private static void RunFixedIntervalSnoozeSuppressesBaseCycles()
    {
        var firstDueUtc = new DateTimeOffset(2036, 6, 9, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-snooze-base-cycle-{Guid.NewGuid():N}.settings.json");
        try
        {
            var clock = new AdvancingValidationClock(firstDueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "稍后抑制基础周期探针",
                    new DateTime(2036, 6, 9),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(firstDueUtc, 15))).Succeeded,
                "无法建立稍后抑制基础周期探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = ReminderOccurrenceKey.Create(rule, firstDueUtc).ToString();
            clock.UtcNow = firstDueUtc;
            Must(
                workspace.ConfirmReminderDisplayed(note, rule.Id, firstDueUtc).Succeeded,
                "稍后抑制探针无法确认真实送达。");

            clock.UtcNow = firstDueUtc.AddMinutes(5);
            Must(
                workspace.SnoozeReminder(
                    note,
                    rule.Id,
                    firstDueUtc,
                    ReminderSnoozeOption.ThirtyMinutes,
                    customBeijing: null).Succeeded,
                "稍后抑制探针无法建立跨过基础周期的活动稍后。");

            clock.UtcNow = firstDueUtc.AddMinutes(16);
            Must(
                workspace.GetDueReminderOccurrences(clock.UtcNow).Count == 0,
                "活动稍后未到期时固定间隔规则跨过基础周期仍弹出新条目。");
            var restartedDuringSnooze = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                restartedDuringSnooze.GetDueReminderOccurrences(clock.UtcNow).Count == 0,
                "重启后活动稍后未到期时固定间隔规则跨过基础周期仍弹出新条目。");

            clock.UtcNow = firstDueUtc.AddMinutes(31);
            var retainedSnooze = workspace.Snapshot.Settings.ReminderStates[rule.Id].ActiveSnooze;
            Must(
                workspace.GetDueReminderOccurrences(clock.UtcNow).Count == 0 &&
                retainedSnooze is { } snooze &&
                snooze.OccurrenceKey == key &&
                snooze.SnoozeUntilUtc == firstDueUtc.AddMinutes(35),
                "活动稍后被新基础周期送达静默丢弃。");

            clock.UtcNow = firstDueUtc.AddMinutes(35);
            var redelivered = workspace.GetDueReminderOccurrences(clock.UtcNow).Single();
            Must(
                ReminderOccurrenceKey.Create(redelivered.Rule, redelivered.ScheduledUtc).ToString() == key &&
                redelivered.ScheduledUtc == firstDueUtc,
                "稍后到期后没有按原 occurrence 的计划时刻重新送达。");

            Must(
                workspace.AcknowledgeReminder(note, rule.Id, firstDueUtc).Succeeded,
                "稍后抑制探针无法处置恢复的原 occurrence。");
            clock.UtcNow = firstDueUtc.AddMinutes(36);
            var collapsed = workspace.GetDueReminderOccurrences(clock.UtcNow).Single();
            Must(
                collapsed.Rule.Id == rule.Id &&
                collapsed.ScheduledUtc == firstDueUtc.AddMinutes(30),
                "稍后解决后没有只出现 cursor 之后的最新周期。");

            Must(
                workspace.ConfirmReminderDisplayed(note, rule.Id, firstDueUtc.AddMinutes(30)).Succeeded &&
                workspace.AcknowledgeReminder(note, rule.Id, firstDueUtc.AddMinutes(30)).Succeeded,
                "稍后抑制探针无法处置补齐的最新周期。");
            clock.UtcNow = firstDueUtc.AddMinutes(44);
            Must(
                workspace.GetDueReminderOccurrences(clock.UtcNow).Count == 0,
                "稍后解决后在下一个基础周期之前产生了新条目。");
            clock.UtcNow = firstDueUtc.AddMinutes(45);
            var nextCycle = workspace.GetDueReminderOccurrences(clock.UtcNow).Single();
            Must(
                nextCycle.Rule.Id == rule.Id &&
                nextCycle.ScheduledUtc == firstDueUtc.AddMinutes(45),
                "稍后解决后固定间隔规则没有恢复正常周期节奏。");
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    private static void RunCalendarSnoozeSuppressesBaseCycles()
    {
        var firstDueUtc = new DateTimeOffset(2036, 6, 10, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-calendar-snooze-cycle-{Guid.NewGuid():N}.settings.json");
        try
        {
            var clock = new AdvancingValidationClock(firstDueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var localDue = ChinaStandardTime.ToBeijingTime(firstDueUtc);
            var rule = new CalendarReminderRule(
                "calendar-snooze-suppression",
                CalendarFrequency.Daily,
                1,
                DateOnly.FromDateTime(localDue.Date),
                TimeOnly.FromDateTime(localDue.DateTime),
                new DailyCalendarSelector());
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "日历稍后抑制探针",
                    localDue.Date,
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { rule })).Succeeded,
                "无法建立日历稍后抑制探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var key = ReminderOccurrenceKey.Create(rule, firstDueUtc).ToString();
            clock.UtcNow = firstDueUtc;
            Must(
                workspace.ConfirmReminderDisplayed(note, rule.Id, firstDueUtc).Succeeded,
                "日历稍后抑制探针无法确认真实送达。");

            clock.UtcNow = firstDueUtc.AddHours(1);
            var snoozeUntilUtc = firstDueUtc.AddDays(2);
            Must(
                workspace.SnoozeReminder(
                    note,
                    rule.Id,
                    firstDueUtc,
                    ReminderSnoozeOption.Custom,
                    ChinaStandardTime.ToBeijingTime(snoozeUntilUtc)).Succeeded,
                "日历稍后抑制探针无法建立跨过日程周期的自定义稍后。");

            clock.UtcNow = firstDueUtc.AddDays(1).AddMinutes(1);
            Must(
                workspace.GetDueReminderOccurrences(clock.UtcNow).Count == 0,
                "日历规则的自定义稍后跨过下一个日程周期时仍弹出新条目。");
            var restartedDuringSnooze = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var retainedState = restartedDuringSnooze.Snapshot.Settings.ReminderStates[rule.Id];
            Must(
                restartedDuringSnooze.GetDueReminderOccurrences(clock.UtcNow).Count == 0 &&
                retainedState.ActiveSnooze is { } snooze &&
                snooze.OccurrenceKey == key &&
                snooze.SnoozeUntilUtc == snoozeUntilUtc,
                "重启后日历规则的活动稍后记录没有保留或仍弹出新条目。");

            clock.UtcNow = snoozeUntilUtc;
            var redelivered = restartedDuringSnooze.GetDueReminderOccurrences(clock.UtcNow).Single();
            Must(
                ReminderOccurrenceKey.Create(redelivered.Rule, redelivered.ScheduledUtc).ToString() == key &&
                redelivered.ScheduledUtc == firstDueUtc,
                "日历稍后到期后没有按原 occurrence 的计划时刻重新送达。");
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    private static void RunDeliveryFailureSuppressesNewBaseCycles()
    {
        var dueUtc = new DateTimeOffset(2036, 6, 11, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-delivery-failure-cycle-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(session.TrySaveNote(
                    null,
                    "送达失败抑制新基础周期探针",
                    new DateTime(2036, 6, 11),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立送达失败抑制探针。");
            var note = session.Current.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            Directory.CreateDirectory(settingsPath + ".tmp");
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(clock.UtcNow);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(
                center.IsFailureVisible(key) && coordinator.ActiveEntryCount == 1,
                "送达失败抑制探针没有进入真实写盘失败状态。");

            clock.UtcNow = dueUtc.AddMinutes(16);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 1 &&
                center.IsFailureVisible(key) &&
                coordinator.HasPendingPersistenceRetry,
                "送达失败期间同规则新基础周期没有被既有条目抑制。");

            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            clock.UtcNow = dueUtc.AddMinutes(16).AddSeconds(11);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsSnoozeEnabled(key) &&
                coordinator.ActiveEntryCount == 1 &&
                !coordinator.HasPendingPersistenceRetry &&
                session.Current.ReminderStates[rule.Id].LastDeliveredOccurrenceKey ==
                    ReminderOccurrenceKey.Create(rule, dueUtc).ToString(),
                "送达失败恢复后原 occurrence 没有完成送达确认。");
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCompletionAndReopenBehavior()
    {
        var firstDueUtc = new DateTimeOffset(2036, 4, 10, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-completion-{Guid.NewGuid():N}.settings.json");
        try
        {
            var clock = new AdvancingValidationClock(firstDueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var repeating = new FixedIntervalReminderRule(
                "completion-repeat",
                firstDueUtc,
                new ReminderInterval(15));
            var elapsedOnce = new AbsoluteOnceReminderRule(
                "completion-elapsed-once",
                firstDueUtc.AddMinutes(5));
            var futureOnce = new AbsoluteOnceReminderRule(
                "completion-future-once",
                firstDueUtc.AddHours(2));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "完成与重开探针",
                    new DateTime(2036, 4, 10),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { repeating, elapsedOnce, futureOnce })).Succeeded,
                "无法建立完成与重开探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            clock.UtcNow = firstDueUtc;
            Must(workspace.ConfirmReminderDisplayed(note, repeating.Id, firstDueUtc).Succeeded,
                "完成探针无法确认 occurrence 送达。");
            clock.UtcNow = firstDueUtc.AddMinutes(2);
            Must(workspace.SnoozeReminder(
                    note,
                    repeating.Id,
                    firstDueUtc,
                    ReminderSnoozeOption.ThirtyMinutes,
                    null).Succeeded,
                "完成探针无法建立活动稍后项。");

            clock.UtcNow = firstDueUtc.AddMinutes(7);
            var beforeFailure = workspace.Snapshot.Settings;
            var diskBeforeFailure = File.ReadAllText(settingsPath);
            Directory.CreateDirectory(settingsPath + ".tmp");
            var failed = workspace.Apply(new SetNoteCompletionChange(note.Id, true));
            Must(
                !failed.Succeeded &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) ==
                    SettingsContract.SerializeCanonical(beforeFailure) &&
                File.ReadAllText(settingsPath) == diskBeforeFailure,
                "完成持久化失败后内存、磁盘或活动稍后项发生变化。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            var completed = workspace.Apply(new SetNoteCompletionChange(note.Id, true));
            var completedSettings = workspace.Snapshot.Settings;
            Must(
                completed.Succeeded &&
                completedSettings.Notes.Single().IsCompleted &&
                completedSettings.ReminderStates[repeating.Id].ActiveSnooze is null &&
                completedSettings.ReminderStates[repeating.Id].LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(repeating, firstDueUtc).ToString() &&
                completedSettings.ReminderStates[elapsedOnce.Id].HandledThroughUtc == elapsedOnce.ScheduledUtc &&
                !completedSettings.ReminderStates.ContainsKey(futureOnce.Id),
                "完成事务没有原子暂停规则、取消稍后项或保留未来 absolute。");

            clock.UtcNow = firstDueUtc.AddHours(1).AddMinutes(8);
            var reopened = workspace.Apply(new SetNoteCompletionChange(note.Id, false));
            var reopenedSettings = workspace.Snapshot.Settings;
            Must(reopened.Succeeded && !reopenedSettings.Notes.Single().IsCompleted,
                "重开事务没有成功提交便签状态。");
            Must(
                reopenedSettings.ReminderStates[repeating.Id].HandledThroughUtc ==
                    firstDueUtc.AddHours(1),
                "重开没有把固定间隔 cursor 推进到提交时刻的最新 occurrence。");
            Must(!reopenedSettings.ReminderStates.ContainsKey(futureOnce.Id),
                "重开错误压掉了未来 absolute occurrence。");
            Must(
                workspace.GetDueReminderOccurrences(firstDueUtc.AddHours(1).AddMinutes(14)).Count == 0,
                "重开后在下一个严格未来周期之前产生了 due occurrence。");
            var nextRepeat = workspace.GetDueReminderOccurrences(firstDueUtc.AddHours(1).AddMinutes(15));
            var futureAbsolute = workspace.GetDueReminderOccurrences(futureOnce.ScheduledUtc);
            Must(
                nextRepeat.Any(item => item.Rule.Id == repeating.Id &&
                    item.ScheduledUtc == firstDueUtc.AddHours(1).AddMinutes(15)) &&
                futureAbsolute.Any(item => item.Rule.Id == futureOnce.Id &&
                    item.ScheduledUtc == futureOnce.ScheduledUtc),
                "重开后严格晚于提交时刻的规则没有恢复。");
            return true;
        }
        finally
        {
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterSnoozeClickBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 11, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-snooze-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(session.TrySaveNote(
                    null,
                    "提醒中心稍后点击探针",
                    new DateTime(2036, 5, 11),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立提醒中心稍后点击探针。");
            var note = session.Current.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(dueUtc);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(
                center.IsSnoozeEnabled(key),
                "提醒中心真实稍后控件未在送达后启用。");
            var beforeInvalidCustom = session.SerializeCurrent();
            Must(
                center.RequestSnooze(
                    key,
                    ReminderSnoozeOption.Custom,
                    ChinaStandardTime.ToBeijingTime(dueUtc)),
                "无法点击真实自定义稍后控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsCustomSnoozeErrorVisible(key) &&
                center.IsSnoozeEnabled(key) &&
                coordinator.ActiveEntryCount == 1 &&
                session.SerializeCurrent() == beforeInvalidCustom,
                "无效自定义稍后没有在来源控件显示错误或改变了状态。");
            Must(
                FindReminderEntryControl<DatePicker>(center, key, "自定义稍后日期：").IsVisible &&
                FindReminderEntryControl<ComboBox>(center, key, "自定义稍后小时：").IsVisible &&
                FindReminderEntryControl<ComboBox>(center, key, "自定义稍后分钟：").IsVisible,
                "无效自定义稍后的来源控件未真实展开。");
            Must(center.RequestSnooze(key, ReminderSnoozeOption.TenMinutes),
                "更正后无法点击 10 分钟稍后控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var state = session.Current.ReminderStates[rule.Id];
            Must(
                state.ActiveSnooze?.OccurrenceKey ==
                    ReminderOccurrenceKey.Create(rule, dueUtc).ToString() &&
                state.ActiveSnooze.SnoozeUntilUtc == dueUtc.AddMinutes(10) &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0,
                "提醒中心稍后点击没有持久化覆盖并关闭空中心。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static ReminderCenterAcknowledgeAllExerciseResult RunReminderCenterAcknowledgeAllBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 8, 20, 1, 30, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-acknowledge-all-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            var saved = true;
            foreach (var text in new[] { "批量已读探针甲", "批量已读探针乙", "批量已读探针丙" })
            {
                saved &= session.TrySaveNote(
                    null,
                    text,
                    new DateTime(2036, 8, 20),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc),
                    out _);
            }

            if (!saved || session.Current.Notes.Count != 3)
            {
                return ReminderCenterAcknowledgeAllExerciseResult.Failed;
            }

            var notes = session.Current.Notes.ToArray();
            var rules = notes.Select(note => note.ReminderRules.Single()).ToArray();
            var keys = rules
                .Select(rule => new ActiveReminderOccurrenceKey(rule.Id, dueUtc))
                .ToArray();
            var occurrenceKeys = rules
                .Select(rule => ReminderOccurrenceKey.Create(rule, dueUtc).ToString())
                .ToArray();
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow;
            center?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            if (center is null ||
                coordinator.ActiveEntryCount != 3 ||
                keys.Any(key => !center.IsAcknowledgeEnabled(key)) ||
                !center.IsAcknowledgeAllEnabled)
            {
                return ReminderCenterAcknowledgeAllExerciseResult.Failed;
            }

            // 第一条走逐条「知道了」作为语义对照，其余两条走头部「全部已读」。
            var singleAcknowledged = center.RequestAcknowledgement(keys[0]);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var commitCount = 0;
            EventHandler countCommit = (_, _) => commitCount++;
            session.StateChanged += countCommit;
            var batchRaised = center.RequestAcknowledgeAll();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            session.StateChanged -= countCommit;
            var persisted = new SettingsStore(clock: clock).Load(settingsPath).Settings;
            var dispositionKeysMatch = true;
            for (var index = 0; index < rules.Length; index++)
            {
                dispositionKeysMatch &=
                    string.Equals(
                        session.Current.ReminderStates[rules[index].Id].LastDispositionOccurrenceKey,
                        occurrenceKeys[index],
                        StringComparison.Ordinal) &&
                    string.Equals(
                        persisted.ReminderStates[rules[index].Id].LastDispositionOccurrenceKey,
                        occurrenceKeys[index],
                        StringComparison.Ordinal);
            }

            var writesAllDispositions =
                singleAcknowledged &&
                batchRaised &&
                dispositionKeysMatch &&
                coordinator.ActiveEntryCount == 0 &&
                !center.IsVisible;
            var matchesSingleSemantics =
                dispositionKeysMatch &&
                commitCount == 1 &&
                session.Reminders.GetDueOccurrences(session.Current, dueUtc).Count == 0;

            var failDueUtc = dueUtc.AddMinutes(30);
            var failureSaved =
                session.TrySaveNote(
                    null,
                    "批量失败探针丁",
                    new DateTime(2036, 8, 20),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(failDueUtc),
                    out _) &&
                session.TrySaveNote(
                    null,
                    "批量失败探针戊",
                    new DateTime(2036, 8, 20),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(failDueUtc),
                    out _);
            clock.UtcNow = failDueUtc;
            coordinator.Tick(failDueUtc);
            var failureCenter = coordinator.ActiveWindow;
            failureCenter?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var failureRules = session.Current.Notes
                .Skip(notes.Length)
                .Select(note => note.ReminderRules.Single())
                .ToArray();
            var failureKeys = failureRules
                .Select(rule => new ActiveReminderOccurrenceKey(rule.Id, failDueUtc))
                .ToArray();
            if (!failureSaved ||
                failureCenter is null ||
                coordinator.ActiveEntryCount != 2 ||
                failureKeys.Any(key => !failureCenter.IsAcknowledgeEnabled(key)) ||
                !failureCenter.IsAcknowledgeAllEnabled)
            {
                return new ReminderCenterAcknowledgeAllExerciseResult(
                    writesAllDispositions,
                    matchesSingleSemantics,
                    false,
                    false);
            }

            Directory.CreateDirectory(settingsPath + ".tmp");
            var beforeFailure = session.SerializeCurrent();
            var failureRaised = failureCenter.RequestAcknowledgeAll();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var diskDuringFailure = new SettingsStore(clock: clock).Load(settingsPath).Settings;
            var failureRetainsAllAtomically =
                failureRaised &&
                coordinator.ActiveEntryCount == 2 &&
                failureKeys.All(key => failureCenter.IsFailureVisible(key)) &&
                failureKeys.All(key => !failureCenter.IsAcknowledgeEnabled(key)) &&
                failureKeys.All(key => failureCenter.IsOpenSourceEnabled(key)) &&
                !failureCenter.IsAcknowledgeAllEnabled &&
                string.Equals(beforeFailure, session.SerializeCurrent(), StringComparison.Ordinal) &&
                failureRules.All(rule =>
                    session.Current.ReminderStates[rule.Id].LastDispositionOccurrenceKey is null &&
                    diskDuringFailure.ReminderStates[rule.Id].LastDispositionOccurrenceKey is null);

            clock.UtcNow = failDueUtc.AddSeconds(4);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var noEarlyRetry =
                coordinator.ActiveEntryCount == 2 &&
                failureKeys.All(key => failureCenter.IsFailureVisible(key));
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            clock.UtcNow = failDueUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var failureOccurrenceKeys = failureRules
                .Select(rule => ReminderOccurrenceKey.Create(rule, failDueUtc).ToString())
                .ToArray();
            var persistedAfterRetry = new SettingsStore(clock: clock).Load(settingsPath).Settings;
            var backoffRetryCompletesBatch =
                noEarlyRetry &&
                coordinator.ActiveEntryCount == 0 &&
                !failureCenter.IsVisible &&
                failureRules
                    .Select((rule, index) => (rule, index))
                    .All(pair =>
                        string.Equals(
                            session.Current.ReminderStates[pair.rule.Id].LastDispositionOccurrenceKey,
                            failureOccurrenceKeys[pair.index],
                            StringComparison.Ordinal) &&
                        string.Equals(
                            persistedAfterRetry.ReminderStates[pair.rule.Id].LastDispositionOccurrenceKey,
                            failureOccurrenceKeys[pair.index],
                            StringComparison.Ordinal));

            return new ReminderCenterAcknowledgeAllExerciseResult(
                writesAllDispositions,
                matchesSingleSemantics,
                failureRetainsAllAtomically,
                backoffRetryCompletesBatch);
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterExpiredCustomSnoozeRetryBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 11, 1, 0, 57, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-expired-custom-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(session.TrySaveNote(
                    null,
                    "过期自定义稍后重试探针",
                    new DateTime(2036, 5, 11),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立过期自定义稍后重试探针。");
            var note = session.Current.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(clock.UtcNow);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(center.IsSnoozeEnabled(key), "送达后稍后提醒动作未启用。");

            var beforeFailure = session.SerializeCurrent();
            var diskBeforeFailure = File.ReadAllText(settingsPath);
            Directory.CreateDirectory(settingsPath + ".tmp");
            var customBeijing = ChinaStandardTime.ToBeijingTime(dueUtc.AddSeconds(3));
            Must(
                center.RequestSnooze(key, ReminderSnoozeOption.Custom, customBeijing),
                "无法点击即将过期的真实自定义稍后控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsFailureVisible(key) &&
                !center.IsSnoozeEnabled(key) &&
                coordinator.HasPendingPersistenceRetry &&
                coordinator.ActiveEntryCount == 1 &&
                session.SerializeCurrent() == beforeFailure &&
                File.ReadAllText(settingsPath) == diskBeforeFailure,
                "自定义稍后首次保存失败没有保留条目与原子状态并进入退避重试。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            clock.UtcNow = dueUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsCustomSnoozeErrorVisible(key) &&
                !center.IsFailureVisible(key) &&
                center.IsAcknowledgeEnabled(key) &&
                center.IsSnoozeEnabled(key) &&
                center.IsCompleteEnabled(key) &&
                !coordinator.HasPendingPersistenceRetry &&
                coordinator.ActiveEntryCount == 1 &&
                session.SerializeCurrent() == beforeFailure,
                "过期自定义稍后重试没有释放重试锁存并把错误归位到来源控件。");

            Must(
                center.RequestSnooze(key, ReminderSnoozeOption.TenMinutes),
                "过期错误后无法点击 10 分钟稍后控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var state = session.Current.ReminderStates[rule.Id];
            Must(
                state.ActiveSnooze?.OccurrenceKey ==
                    ReminderOccurrenceKey.Create(rule, dueUtc).ToString() &&
                state.ActiveSnooze.SnoozeUntilUtc == clock.UtcNow.AddMinutes(10) &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0,
                "过期错误后的用户更正没有成功持久化并关闭空中心。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterStaleRetryAfterCompleteReopenBehavior()
    {
        RunStaleDeliveryRetryAfterCompleteReopen();
        RunStaleSnoozeRetryAfterCompleteReopen();
        return true;
    }

    private static void RunStaleDeliveryRetryAfterCompleteReopen()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 14, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-stale-delivery-retry-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            var repeating = new FixedIntervalReminderRule(
                "stale-delivery-repeat",
                dueUtc,
                new ReminderInterval(15));
            var futureOnce = new AbsoluteOnceReminderRule(
                "stale-delivery-future",
                dueUtc.AddHours(1));
            Must(session.TrySaveNote(
                    null,
                    "完成重开丢弃旧送达重试探针",
                    new DateTime(2036, 5, 14),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { repeating, futureOnce },
                    out _,
                    out _),
                "无法建立旧送达重试探针。");
            var workspace = new DesktopWorkspace(session);
            var note = session.Current.Notes.Single();
            var key = new ActiveReminderOccurrenceKey(repeating.Id, dueUtc);

            Directory.CreateDirectory(settingsPath + ".tmp");
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(clock.UtcNow);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(
                center.IsFailureVisible(key) &&
                !coordinator.ActiveOccurrenceConfirmed &&
                coordinator.HasPendingPersistenceRetry,
                "旧送达重试探针没有进入真实写盘失败状态。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            clock.UtcNow = dueUtc.AddMinutes(16);
            Must(workspace.Apply(new SetNoteCompletionChange(note.Id, true)).Succeeded,
                "旧送达重试探针无法完成便签。");
            clock.UtcNow = dueUtc.AddMinutes(16).AddSeconds(1);
            Must(workspace.Apply(new SetNoteCompletionChange(note.Id, false)).Succeeded,
                "旧送达重试探针无法重新打开便签。");
            var afterReopen = session.SerializeCurrent();
            var diskAfterReopen = File.ReadAllText(settingsPath);
            var staleDelivery = workspace.ConfirmReminderDisplayed(
                note,
                repeating.Id,
                dueUtc);
            Must(
                !staleDelivery.Succeeded &&
                session.SerializeCurrent() == afterReopen &&
                File.ReadAllText(settingsPath) == diskAfterReopen,
                "cursor 已越过旧 occurrence 时仍被错误视为送达幂等成功。");

            clock.UtcNow = dueUtc.AddMinutes(16).AddSeconds(2);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var state = session.Current.ReminderStates[repeating.Id];
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                !coordinator.HasPendingPersistenceRetry &&
                state.HandledThroughUtc == dueUtc.AddMinutes(15) &&
                state.LastDeliveredOccurrenceKey is null &&
                session.SerializeCurrent() == afterReopen &&
                File.ReadAllText(settingsPath) == diskAfterReopen,
                "完成并重开后旧送达重试仍被确认、重试或改变了已推进状态。");
            AssertStrictFutureRulesRemain(workspace, repeating, futureOnce, dueUtc);
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    private static void RunStaleSnoozeRetryAfterCompleteReopen()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 15, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-stale-snooze-retry-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            var repeating = new FixedIntervalReminderRule(
                "stale-snooze-repeat",
                dueUtc,
                new ReminderInterval(15));
            var futureOnce = new AbsoluteOnceReminderRule(
                "stale-snooze-future",
                dueUtc.AddHours(1));
            Must(session.TrySaveNote(
                    null,
                    "完成重开丢弃旧稍后重试探针",
                    new DateTime(2036, 5, 15),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { repeating, futureOnce },
                    out _,
                    out _),
                "无法建立旧稍后重试探针。");
            var workspace = new DesktopWorkspace(session);
            var note = session.Current.Notes.Single();
            var key = new ActiveReminderOccurrenceKey(repeating.Id, dueUtc);

            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(clock.UtcNow);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(coordinator.ActiveOccurrenceConfirmed && center.IsSnoozeEnabled(key),
                "旧稍后重试探针没有完成真实送达。");

            Directory.CreateDirectory(settingsPath + ".tmp");
            Must(center.RequestSnooze(key, ReminderSnoozeOption.TenMinutes),
                "无法点击旧稍后重试探针的真实控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(center.IsFailureVisible(key) && coordinator.HasPendingPersistenceRetry,
                "旧稍后重试探针没有进入真实写盘失败状态。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            clock.UtcNow = dueUtc.AddMinutes(16);
            Must(workspace.Apply(new SetNoteCompletionChange(note.Id, true)).Succeeded,
                "旧稍后重试探针无法完成便签。");
            clock.UtcNow = dueUtc.AddMinutes(16).AddSeconds(1);
            Must(workspace.Apply(new SetNoteCompletionChange(note.Id, false)).Succeeded,
                "旧稍后重试探针无法重新打开便签。");
            var afterReopen = session.SerializeCurrent();
            var diskAfterReopen = File.ReadAllText(settingsPath);

            clock.UtcNow = dueUtc.AddMinutes(16).AddSeconds(2);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var state = session.Current.ReminderStates[repeating.Id];
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                !coordinator.HasPendingPersistenceRetry &&
                state.HandledThroughUtc == dueUtc.AddMinutes(15) &&
                state.LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(repeating, dueUtc).ToString() &&
                state.ActiveSnooze is null &&
                session.SerializeCurrent() == afterReopen &&
                File.ReadAllText(settingsPath) == diskAfterReopen,
                "完成并重开后旧稍后重试仍被执行、重试或改变了已推进状态。");
            AssertStrictFutureRulesRemain(workspace, repeating, futureOnce, dueUtc);
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    private static void AssertStrictFutureRulesRemain(
        DesktopWorkspace workspace,
        FixedIntervalReminderRule repeating,
        AbsoluteOnceReminderRule futureOnce,
        DateTimeOffset dueUtc)
    {
        Must(
            workspace.GetDueReminderOccurrences(dueUtc.AddMinutes(29)).Count == 0 &&
            workspace.GetDueReminderOccurrences(dueUtc.AddMinutes(30)).Any(occurrence =>
                occurrence.Rule.Id == repeating.Id &&
                occurrence.ScheduledUtc == dueUtc.AddMinutes(30)) &&
            workspace.GetDueReminderOccurrences(futureOnce.ScheduledUtc).Any(occurrence =>
                occurrence.Rule.Id == futureOnce.Id &&
                occurrence.ScheduledUtc == futureOnce.ScheduledUtc),
            "清理旧重试后严格未来周期或未到期 absolute 没有按原语义恢复。");
    }

    public static bool RunReminderCenterCompleteClickBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 12, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-complete-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            var firstRule = new AbsoluteOnceReminderRule("complete-click-first", dueUtc);
            var secondRule = new AbsoluteOnceReminderRule(
                "complete-click-second",
                dueUtc.AddMinutes(1));
            Must(session.TrySaveNote(
                    null,
                    "提醒中心完成点击探针",
                    new DateTime(2036, 5, 12),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { firstRule, secondRule },
                    out _,
                    out _),
                "无法建立提醒中心完成点击探针。");
            var note = session.Current.Notes.Single();
            var firstKey = new ActiveReminderOccurrenceKey(firstRule.Id, dueUtc);
            clock.UtcNow = secondRule.ScheduledUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(clock.UtcNow);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(
                center.IsCompleteEnabled(firstKey) &&
                coordinator.ActiveEntryCount == 2,
                "完成动作在所有 occurrence 送达后仍不可用。");

            var beforeFailure = session.SerializeCurrent();
            var diskBeforeFailure = File.ReadAllText(settingsPath);
            // #155：完成确认以取消为安全默认——先取消确认，便签、状态与中心条目全量保留。
            QueueConfirmationDialogResponse(center, confirm: false);
            Must(center.RequestComplete(firstKey), "无法点击真实完成便签按钮。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                !session.Current.Notes.Single().IsCompleted &&
                session.SerializeCurrent() == beforeFailure &&
                File.ReadAllText(settingsPath) == diskBeforeFailure &&
                coordinator.ActiveEntryCount == 2 &&
                center.IsCompleteEnabled(firstKey) &&
                !center.IsFailureVisible(firstKey),
                "完成便签确认框取消后没有全量保留便签、状态、条目与可完成状态。");
            Directory.CreateDirectory(settingsPath + ".tmp");
            // #155：确认后提交完成 → 命中写入失败注入，退避重试路径不经确认。
            QueueConfirmationDialogResponse(center, confirm: true);
            Must(center.RequestComplete(firstKey), "无法再次点击真实完成便签按钮。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                !session.Current.Notes.Single().IsCompleted &&
                session.SerializeCurrent() == beforeFailure &&
                File.ReadAllText(settingsPath) == diskBeforeFailure &&
                coordinator.ActiveEntryCount == 2 &&
                center.IsFailureVisible(firstKey),
                "完成保存失败没有全量保留便签、状态与中心条目。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            clock.UtcNow = secondRule.ScheduledUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                session.Current.Notes.Single().IsCompleted &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                session.Current.ReminderStates[firstRule.Id].LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(firstRule, dueUtc).ToString() &&
                session.Current.ReminderStates[secondRule.Id].LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(secondRule, secondRule.ScheduledUtc).ToString(),
                "完成退避重试没有原子完成便签并移除全部中心项。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    // #155：为真实确认框路径排队自动应答。确认框在 Click 处理器内以 ShowDialog 模态打开并阻塞，
    // 排队的 ApplicationIdle 计时器在模态泵内轮询 owner 的 OwnedWindows，找到弹层后按需点击
    // 确认/取消；inspect 允许探针在应答前核对弹层内容（安全默认、文案与消息正文）。
    internal static void QueueConfirmationDialogResponse(
        Window owner,
        bool confirm,
        Action<ConfirmationDialog>? inspect = null)
    {
        QueueConfirmationDialogAction(owner, dialog =>
        {
            inspect?.Invoke(dialog);
            return WindowValidationInput.InvokeButton(confirm ? dialog.ConfirmButton : dialog.CancelButton);
        });
    }

    internal static void QueueConfirmationDialogAction(
        Window owner,
        Func<ConfirmationDialog, bool> invoke)
    {
        var attempts = 0;
        ConfirmationDialog? dispatchedDialog = null;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, owner.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        timer.Tick += (_, _) =>
        {
            attempts++;
            if (dispatchedDialog is { IsVisible: false })
            {
                timer.Stop();
                return;
            }

            var dialog = owner.OwnedWindows
                .OfType<ConfirmationDialog>()
                .FirstOrDefault(window => window.IsVisible);
            if (dialog is not null && dispatchedDialog is null)
            {
                dispatchedDialog = dialog;
                if (!invoke(dialog))
                {
                    timer.Stop();
                    dialog.Close();
                    throw new InvalidOperationException("确认框验证未能调用按钮命令；窗口仅为失败收口而关闭。");
                }
            }

            if (attempts >= 75)
            {
                timer.Stop();
                dispatchedDialog?.Close();
                // Fail the validation process after bounded modal cleanup.
                // Closing the dialog is never counted as a successful action.
                throw new InvalidOperationException("确认框验证在 3 秒内未完成按钮命令或窗口关闭。");
            }
        };
        timer.Start();
    }

    // #155：「完成便签」确认框行为——先取消（安全默认，便签/条目/可完成状态原样保留），
    // 再确认（原子完成便签、移除中心条目、写入处置 key）。「全部已读」不经本入口。
    public static bool RunReminderCenterCompleteRequiresConfirmationBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 12, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-complete-confirm-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            var rule = new AbsoluteOnceReminderRule("complete-confirm-rule", dueUtc);
            Must(
                session.TrySaveNote(
                    null,
                    "完成确认探针便签",
                    new DateTime(2036, 5, 12),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { rule },
                    out _,
                    out _),
                "无法建立完成确认探针便签。");
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = rule.ScheduledUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(clock.UtcNow);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsCompleteEnabled(key) &&
                coordinator.ActiveEntryCount == 1,
                "完成确认探针条目未进入可完成状态。");

            QueueConfirmationDialogResponse(center, confirm: false);
            Must(center.RequestComplete(key), "无法点击真实完成便签控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                !session.Current.Notes.Single().IsCompleted &&
                coordinator.ActiveEntryCount == 1 &&
                center.IsCompleteEnabled(key) &&
                !center.IsFailureVisible(key),
                "完成便签确认框取消后必须原样保留便签、中心条目与可完成状态。");

            QueueConfirmationDialogResponse(
                center,
                confirm: true,
                inspect: dialog => Must(
                    string.Equals(
                        dialog.ConfirmButton.Content?.ToString(),
                        "确认完成",
                        StringComparison.Ordinal) &&
                    dialog.CancelButton.IsDefault &&
                    dialog.CancelButton.IsCancel &&
                    !dialog.ConfirmButton.IsDefault,
                    "完成便签确认框没有以取消为安全默认或确认文案不是「确认完成」。"));
            Must(center.RequestComplete(key), "无法再次点击真实完成便签控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                session.Current.Notes.Single().IsCompleted &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                session.Current.ReminderStates[rule.Id].LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(rule, rule.ScheduledUtc).ToString(),
                "完成便签确认后没有原子完成便签并移除全部中心条目。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
        }
    }

    // #155：最近一次删除的短时撤销——确认框取消为安全默认；删除成功后主窗口状态条提供
    // 「撤销」入口；撤销在同一事务恢复记录与规则提醒状态；连续删除只保留最近一次；入口短时过期。
    public static bool RunDeleteUndoOfferBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-delete-undo-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        var rule = new AbsoluteOnceReminderRule("undo-absolute-rule", nowUtc.AddMinutes(30));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "撤销倒计时甲",
            nowUtc.AddDays(3),
            Array.Empty<string>(),
            new ReminderRule[] { rule }));
        var countdownA = workspace.Snapshot.Settings.Countdowns.Single();
        MustApply(workspace, new SaveCountdownChange(
            null,
            "撤销倒计时乙",
            nowUtc.AddDays(5),
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var countdownB = workspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
            !string.Equals(item.Id, countdownA.Id, StringComparison.OrdinalIgnoreCase));
        Must(countdownB is not null, "无法定位第二个撤销探针倒计时。");
        MustApply(workspace, new SaveNoteChange(
            null,
            "撤销便签丙",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var noteC = workspace.Snapshot.Settings.Notes.Single();
        clock.UtcNow = rule.ScheduledUtc;
        Must(
            workspace.ConfirmReminderDisplayed(
                ReminderSourceKind.Countdown,
                countdownA.Id,
                rule.Id,
                rule.ScheduledUtc).Succeeded,
            "无法预置倒计时提醒已送达状态。");
        var stateBefore = workspace.Snapshot.Settings.ReminderStates[rule.Id];

        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            // Roslyn 对该局部在跨 Must/本地函数调用后的空流状态判定偏保守，四处用 ! 断言非空。
            Button undoButton = main.MainStatusUndoButton;
            TextBlock statusText = main.StatusText;
            Must(
                undoButton is not null &&
                statusText is not null &&
                main.DescribeUnsavedExitWork() is null &&
                main.DeleteUndoForValidation is null &&
                undoButton.Visibility == Visibility.Collapsed,
                "撤销入口初始状态必须干净且不可见。");

            Button DeleteButton(string id)
            {
                var button = VisualDescendants<Button>(main).FirstOrDefault(candidate =>
                    string.Equals(candidate.Content?.ToString(), "删除", StringComparison.Ordinal) &&
                    string.Equals(candidate.Tag as string, id, StringComparison.OrdinalIgnoreCase));
                Must(button is not null, $"无法定位记录 {id} 的行内删除按钮。");
                return button!;
            }

            QueueConfirmationDialogResponse(main, confirm: false);
            DeleteButton(countdownA.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdownA.Id) &&
                main.DeleteUndoForValidation is null &&
                undoButton!.Visibility == Visibility.Collapsed,
                "删除倒计时确认框取消后必须原样保留记录且不出现撤销入口。");

            QueueConfirmationDialogResponse(main, confirm: true);
            DeleteButton(countdownA.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var undo = main.DeleteUndoForValidation;
            Must(
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdownA.Id) &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(rule.Id) &&
                undo is not null &&
                undo.Countdown is not null &&
                string.Equals(undo.KindName, "倒计时", StringComparison.Ordinal) &&
                string.Equals(undo.Description, countdownA.Name, StringComparison.Ordinal) &&
                undo.ReminderStates.ContainsKey(rule.Id) &&
                undoButton!.Visibility == Visibility.Visible &&
                string.Equals(
                    AutomationProperties.GetName(undoButton),
                    $"撤销删除倒计时「{countdownA.Name}」",
                    StringComparison.Ordinal) &&
                string.Equals(
                    statusText!.Text,
                    $"已删除倒计时「{countdownA.Name}」。",
                    StringComparison.Ordinal),
                "删除倒计时成功后状态区没有提供带完整上下文的撤销入口，或提醒状态没有随删除清理。");

            undoButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var restoredA = workspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
                item.Id == countdownA.Id);
            Must(
                restoredA is not null &&
                string.Equals(restoredA.Name, countdownA.Name, StringComparison.Ordinal) &&
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(rule.Id, out var restoredState) &&
                restoredState == stateBefore &&
                main.DeleteUndoForValidation is null &&
                undoButton.Visibility == Visibility.Collapsed &&
                string.Equals(
                    statusText!.Text,
                    $"已撤销删除倒计时「{countdownA.Name}」。",
                    StringComparison.Ordinal),
                "撤销删除必须恢复倒计时与其规则提醒状态并收起入口。");

            QueueConfirmationDialogResponse(main, confirm: true);
            DeleteButton(countdownB!.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            // 便签行只在便签工作区渲染（RenderVisibleWorkspace），删除/撤销便签前先切换工作区。
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            QueueConfirmationDialogResponse(main, confirm: true);
            DeleteButton(noteC.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var latestUndo = main.DeleteUndoForValidation;
            Must(
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdownB.Id) &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteC.Id) &&
                latestUndo is not null &&
                latestUndo.Note is not null &&
                string.Equals(latestUndo.KindName, "便签", StringComparison.Ordinal) &&
                string.Equals(latestUndo.Description, noteC.Text, StringComparison.Ordinal),
                "连续删除必须只保留最近一次删除的撤销入口。");

            undoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteC.Id) &&
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdownB.Id) &&
                main.DeleteUndoForValidation is null,
                "撤销入口必须只恢复最近一次删除的记录。");

            QueueConfirmationDialogResponse(main, confirm: true);
            DeleteButton(noteC.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.DeleteUndoForValidation is not null &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteC.Id),
                "删除后撤销入口应可用。");
            main.ExpireDeleteUndoForValidation();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.DeleteUndoForValidation is null &&
                undoButton!.Visibility == Visibility.Collapsed &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteC.Id),
                "撤销入口过期后必须收起且不影响已删除状态。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #214：悬浮窗删除就地撤销。管理窗口从未创建时，底部状态行出现「撤销」并恢复便签与规则提醒状态；
    // 管理窗口删除仍走状态条。Ctrl+Z 在撤销期间生效。焦点或悬停暂停 10 秒计时，移开后按剩余时间继续。
    public static bool RunDeleteUndoInPlaceBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-undo-inplace-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        var rule = new AbsoluteOnceReminderRule("floating-undo-rule", nowUtc.AddMinutes(30));
        MustApply(workspace, new SaveNoteChange(
            null,
            "悬浮撤销便签甲",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            new ReminderRule[] { rule }));
        var noteA = workspace.Snapshot.Settings.Notes.Single();
        MustApply(workspace, new SaveNoteChange(
            null,
            "悬浮撤销便签乙",
            new DateTime(2038, 7, 9),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var noteB = workspace.Snapshot.Settings.Notes.First(item =>
            !string.Equals(item.Id, noteA.Id, StringComparison.OrdinalIgnoreCase));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "管理撤销倒计时",
            nowUtc.AddDays(4),
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var countdown = workspace.Snapshot.Settings.Countdowns.Single();
        clock.UtcNow = rule.ScheduledUtc;
        Must(
            workspace.ConfirmReminderDisplayed(
                ReminderSourceKind.Note,
                noteA.Id,
                rule.Id,
                rule.ScheduledUtc).Succeeded,
            "无法预置便签提醒已送达状态。");
        var stateBefore = workspace.Snapshot.Settings.ReminderStates[rule.Id];

        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        var lifecycle = new ManagerWindowLifecycle(
            () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
            PrepareOffscreenValidationWindow);
        var floating = new FloatingWindow(
            workspace,
            calendar,
            lifecycle,
            AppContext.BaseDirectory);
        MainWindow? main = null;
        try
        {
            PrepareOffscreenValidationWindow(floating);
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                lifecycle.Window is null &&
                CurrentCreatedMainWindows(preexistingWindows).Length == 0 &&
                floating.FloatingDeleteUndoLine.Visibility == Visibility.Collapsed,
                "探针开始时管理窗口必须尚未创建，悬浮撤销行必须收起。");

            string? cancelBody = null;
            var cancelIsSafeDefault = false;
            DriveFloatingNoteDelete(floating, noteA, confirm: false, inspect: dialog =>
            {
                cancelBody = dialog.BodyText.Text;
                cancelIsSafeDefault =
                    dialog.CancelButton.IsDefault &&
                    dialog.CancelButton.IsCancel &&
                    !dialog.ConfirmButton.IsDefault;
            });
            Must(
                cancelBody is not null &&
                cancelBody.Contains("删除后 10 秒内可撤销", StringComparison.Ordinal),
                "删除确认框必须写明 10 秒内可撤销。实际正文：" + (cancelBody ?? "<null>"));
            Must(
                cancelIsSafeDefault,
                "删除确认框取消必须仍是安全默认。");
            Must(
                workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id) &&
                floating.DeleteUndoForValidation is null &&
                floating.FloatingDeleteUndoLine.Visibility == Visibility.Collapsed &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Collapsed &&
                lifecycle.Window is null,
                "取消删除必须保留便签，且不出现撤销入口、不创建管理窗口。");

            DriveFloatingNoteDelete(floating, noteA, confirm: true);
            Must(
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id) &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(rule.Id) &&
                floating.DeleteUndoForValidation is not null &&
                floating.FloatingDeleteUndoLine.Visibility == Visibility.Visible &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible &&
                string.Equals(
                    floating.FloatingDeleteUndoButton.Content?.ToString(),
                    "撤销",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(floating.FloatingDeleteUndoButton),
                    $"撤销删除便签「{noteA.Text}」",
                    StringComparison.Ordinal) &&
                string.Equals(
                    floating.FloatingDeleteUndoText.Text,
                    $"已删除便签「{noteA.Text}」。",
                    StringComparison.Ordinal) &&
                lifecycle.Window is null,
                "管理窗口从未创建时，悬浮窗删除后底部必须出现撤销，且提醒状态随删除清理。");

            floating.FloatingDeleteUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            var restoredA = workspace.Snapshot.Settings.Notes.FirstOrDefault(item => item.Id == noteA.Id);
            Must(
                restoredA is not null &&
                string.Equals(restoredA.Text, noteA.Text, StringComparison.Ordinal) &&
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(rule.Id, out var restoredState) &&
                restoredState == stateBefore &&
                floating.DeleteUndoForValidation is null &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Collapsed,
                "点击悬浮窗撤销必须原样恢复便签及其规则提醒状态。");

            DriveFloatingNoteDelete(floating, noteA, confirm: true);
            Must(
                WindowValidationInput.RaisePreviewKey(floating, Key.Z, ModifierKeys.Control),
                "撤销期间 Ctrl+Z 必须被悬浮窗处理。");
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id) &&
                workspace.Snapshot.Settings.ReminderStates.ContainsKey(rule.Id) &&
                floating.DeleteUndoForValidation is null,
                "悬浮窗 Ctrl+Z 必须撤销刚刚的删除。");

            DriveFloatingNoteDelete(floating, noteA, confirm: true);
            floating.AdvanceDeleteUndoForValidation(TimeSpan.FromSeconds(7));
            Must(
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible,
                "未到 10 秒时撤销入口必须仍在。");
            RaiseMouseTransition(floating.FloatingDeleteUndoButton, Mouse.MouseEnterEvent);
            floating.AdvanceDeleteUndoForValidation(TimeSpan.FromSeconds(10));
            Must(
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id),
                "鼠标停在撤销按钮上时不得自动收起。");
            RaiseMouseTransition(floating.FloatingDeleteUndoButton, Mouse.MouseLeaveEvent);
            floating.AdvanceDeleteUndoForValidation(TimeSpan.FromSeconds(2));
            Must(
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible,
                "移开鼠标后必须按剩余时间继续，而不是重新算满 10 秒。");
            floating.AdvanceDeleteUndoForValidation(TimeSpan.FromSeconds(2));
            Must(
                floating.DeleteUndoForValidation is null &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Collapsed &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id),
                "悬停移开后计时必须继续，到点收起且不恢复便签。");

            DriveFloatingNoteDelete(floating, noteB, confirm: true);
            Must(
                floating.DeleteUndoForValidation is not null &&
                lifecycle.Window is null,
                "第二张便签的撤销仍必须留在悬浮窗，且管理窗口仍未创建。");
            RaiseMouseTransition(floating.FloatingDeleteUndoButton, Mouse.MouseEnterEvent);

            var openManager = (Button)floating.FindName("OpenManagerButton");
            openManager.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            main = lifecycle.Window;
            Must(
                main is not null &&
                CurrentCreatedMainWindows(preexistingWindows).Length == 1 &&
                floating.DeleteUndoForValidation is not null &&
                string.Equals(
                    floating.DeleteUndoForValidation.Description,
                    noteB.Text,
                    StringComparison.Ordinal) &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible,
                "打开管理窗口不得清掉悬浮窗上尚未撤销的删除。");
            main!.ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(main.Dispatcher);
            string? mainBody = null;
            QueueConfirmationDialogResponse(
                main,
                confirm: true,
                inspect: dialog => mainBody = dialog.BodyText.Text);
            Button MainDeleteButton(string id)
            {
                var button = VisualDescendants<Button>(main).FirstOrDefault(candidate =>
                    string.Equals(candidate.Content?.ToString(), "删除", StringComparison.Ordinal) &&
                    string.Equals(candidate.Tag as string, id, StringComparison.OrdinalIgnoreCase));
                Must(button is not null, $"无法定位记录 {id} 的行内删除按钮。");
                return button!;
            }

            MainDeleteButton(countdown.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                mainBody is not null &&
                mainBody.Contains("删除后 10 秒内可撤销", StringComparison.Ordinal) &&
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdown.Id) &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteB.Id) &&
                floating.FloatingDeleteUndoLine.Visibility == Visibility.Collapsed &&
                floating.DeleteUndoForValidation is null &&
                main.DeleteUndoForValidation is not null &&
                main.MainStatusUndoButton.Visibility == Visibility.Visible &&
                string.Equals(
                    AutomationProperties.GetName(main.MainStatusUndoButton),
                    $"撤销删除倒计时「{countdown.Name}」",
                    StringComparison.Ordinal) &&
                string.Equals(
                    main.StatusText.Text,
                    $"已删除倒计时「{countdown.Name}」。",
                    StringComparison.Ordinal),
                "管理窗口删除必须改由状态条提供撤销，并替换悬浮窗上一次未完成的撤销。");

            Must(
                WindowValidationInput.RaisePreviewKey(main, Key.Z, ModifierKeys.Control),
                "管理窗口撤销期间 Ctrl+Z 必须生效。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdown.Id) &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteB.Id) &&
                main.DeleteUndoForValidation is null &&
                main.MainStatusUndoButton.Visibility == Visibility.Collapsed,
                "管理窗口 Ctrl+Z 只能恢复最近一次删除。");

            QueueConfirmationDialogResponse(main, confirm: true);
            MainDeleteButton(countdown.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            main.Activate();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.MainStatusUndoButton.Focus(),
                "撤销按钮必须能获得键盘焦点。");
            main.AdvanceDeleteUndoForValidation(TimeSpan.FromSeconds(15));
            Must(
                main.MainStatusUndoButton.Visibility == Visibility.Visible &&
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdown.Id),
                "焦点停在撤销按钮上时不得自动收起。");
            Must(main.StatusText.Focus(), "焦点必须能离开撤销按钮。");
            main.AdvanceDeleteUndoForValidation(TimeSpan.FromSeconds(12));
            Must(
                main.DeleteUndoForValidation is null &&
                main.MainStatusUndoButton.Visibility == Visibility.Collapsed &&
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdown.Id),
                "焦点移开后计时必须继续并到点收起。");
            return true;
        }
        finally
        {
            main?.Close();
            floating.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }

        static void DriveFloatingNoteDelete(
            FloatingWindow floating,
            NoteRecord note,
            bool confirm,
            Action<ConfirmationDialog>? inspect = null)
        {
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();
            var trigger = VisualDescendants<Button>(floating).FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetName(candidate),
                    $"更多操作：编辑或删除便签 {note.Text}",
                    StringComparison.Ordinal));
            Must(trigger is not null, $"无法定位便签「{note.Text}」的更多操作。");
            var popup = NoteRowOverflowMenuBehavior.GetPopup(trigger!);
            Must(popup is not null, "便签更多操作没有弹出层。");
            if (!popup!.IsOpen)
            {
                trigger!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WindowValidationInput.Pump(floating.Dispatcher);
                floating.UpdateLayout();
            }

            Must(popup.IsOpen, "便签更多操作没有打开。");
            QueueConfirmationDialogResponse(floating, confirm, inspect);
            var menuDelete = VisualDescendants<Button>(popup.Child).FirstOrDefault(candidate =>
                string.Equals(candidate.Content as string, "删除", StringComparison.Ordinal));
            Must(menuDelete is not null, "无法定位悬浮便签删除按钮。");
            menuDelete!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
        }

        static void RaiseMouseTransition(UIElement element, RoutedEvent routedEvent)
        {
            element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = routedEvent,
                Source = element
            });
        }
    }

    // #155 / #213：脏状态描述沿用既有语义；退出确认框以取消为安全默认，
    // 确认文案为「退出」，正文包含「退出后不再提醒」并列出未保存项。
    public static bool RunDirtyExitConfirmationBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-dirty-exit-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        MustApply(workspace, new SaveNoteChange(
            null,
            "退出前草稿探针便签",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var note = workspace.Snapshot.Settings.Notes.Single();

        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.DescribeUnsavedExitWork() is null,
                "干净状态下不应产生退出确认描述。");

            var noteText = (TextBox)main.FindName("NoteText");
            noteText.Text = "未保存的便签草稿";
            Must(
                string.Equals(
                    main.DescribeUnsavedExitWork(),
                    "便签编辑器有未保存的草稿。",
                    StringComparison.Ordinal),
                "便签草稿未保存时退出描述缺失。");
            noteText.Clear();
            Must(main.DescribeUnsavedExitWork() is null, "清空便签草稿后退出描述应消失。");

            // 便签行只在便签工作区渲染（RenderVisibleWorkspace），先切换工作区再驱动便签行。
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var editButton = VisualDescendants<Button>(main).FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                string.Equals(button.Tag as string, note.Id, StringComparison.OrdinalIgnoreCase));
            Must(editButton is not null, "无法定位便签探针行的编辑按钮。");
            editButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                string.Equals(
                    main.DescribeUnsavedExitWork(),
                    "便签编辑器有未保存的草稿。",
                    StringComparison.Ordinal),
                "便签编辑器编辑态的退出描述缺失。");
            var cancelNoteEdit = (Button)main.FindName("CancelNoteEdit");
            cancelNoteEdit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(main.DescribeUnsavedExitWork() is null, "取消便签编辑后退出描述应消失。");

            var monthlySalary = (TextBox)main.FindName("MonthlySalary");
            monthlySalary.Text = "not-a-number";
            Must(
                string.Equals(
                    main.DescribeUnsavedExitWork(),
                    "显示设置有未保存的更改。",
                    StringComparison.Ordinal),
                "显示设置脏时退出描述缺失。");
            monthlySalary.Text = workspace.Snapshot.Settings.Preferences.MonthlySalary
                .ToString("N2", CultureInfo.CurrentCulture);
            Must(main.DescribeUnsavedExitWork() is null, "显示设置恢复后退出描述应消失。");

            monthlySalary.Text = "not-a-number";
            noteText.Text = "未保存的便签草稿";
            var combined = main.DescribeUnsavedExitWork();
            Must(
                combined is not null &&
                combined.Contains("显示设置", StringComparison.Ordinal) &&
                combined.Contains("便签编辑器", StringComparison.Ordinal),
                "多来源未保存内容的退出描述不完整。");

            var dialogInspected = false;
            QueueConfirmationDialogResponse(
                main,
                confirm: false,
                inspect: dialog =>
                {
                    dialogInspected =
                        dialog.CancelButton.IsDefault &&
                        dialog.CancelButton.IsCancel &&
                        !dialog.ConfirmButton.IsDefault &&
                        string.Equals(
                            dialog.ConfirmButton.Content?.ToString(),
                            "退出",
                            StringComparison.Ordinal) &&
                        dialog.BodyText.Text.Contains("显示设置", StringComparison.Ordinal) &&
                        dialog.BodyText.Text.Contains("便签编辑器", StringComparison.Ordinal) &&
                        dialog.BodyText.Text.Contains("退出后不再提醒", StringComparison.Ordinal);
                });
            var confirmed = ProcessExitConfirmation.Confirm(
                main,
                new[] { main.DescribeUnsavedExitWork()! });
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                dialogInspected &&
                !confirmed,
                "退出确认框必须以取消为安全默认、携带完整描述且取消时不退出。");

            QueueConfirmationDialogResponse(main, confirm: true);
            confirmed = ProcessExitConfirmation.Confirm(
                main,
                new[] { main.DescribeUnsavedExitWork()! });
            WindowValidationInput.Pump(main.Dispatcher);
            Must(confirmed, "退出确认框确认后必须放行退出。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #213：没有未保存内容时，关闭悬浮窗和系统关闭（Alt+F4 走 Closing）都弹出退出确认，
    // 正文包含「退出后不再提醒」。取消后悬浮窗仍可见，提醒派发未被释放。
    public static bool RunAlwaysConfirmExitKeepsReminderBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-exit-confirm-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(main);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(floating);
        var coordinator = new ReminderDeliveryCoordinator(workspace, logger);
        var shuttingDown = false;
        void RequestShutdown()
        {
            if (shuttingDown)
            {
                return;
            }

            var reasons = new List<string>();
            if (main.DescribeUnsavedExitWork() is { } mainReason)
            {
                reasons.Add(mainReason);
            }

            if (floating.DescribeUnsavedExitWork() is { } floatingReason)
            {
                reasons.Add(floatingReason);
            }

            if (!ProcessExitConfirmation.Confirm(floating, reasons))
            {
                return;
            }

            shuttingDown = true;
            coordinator.Dispose();
            floating.Close();
        }

        FloatingWindowCloseBridge.Attach(floating, RequestShutdown, () => shuttingDown);
        floating.ExitRequested += (_, _) => RequestShutdown();
        try
        {
            main.Show();
            floating.Show();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                main.DescribeUnsavedExitWork() is null &&
                floating.DescribeUnsavedExitWork() is null,
                "干净退出探针开始时不应有未保存内容。");

            var dialogInspected = false;
            QueueConfirmationDialogResponse(
                floating,
                confirm: false,
                inspect: dialog =>
                {
                    dialogInspected =
                        dialog.CancelButton.IsDefault &&
                        dialog.CancelButton.IsCancel &&
                        dialog.BodyText.Text.Contains("退出后不再提醒", StringComparison.Ordinal) &&
                        !dialog.BodyText.Text.Contains("未保存", StringComparison.Ordinal);
                });
            var confirmed = ProcessExitConfirmation.Confirm(floating, Array.Empty<string>());
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(dialogInspected && !confirmed, "干净状态也必须弹出含「退出后不再提醒」的退出确认，取消不放行。");
            Must(floating.IsVisible && coordinator.IsDispatching, "直接取消退出后悬浮窗和提醒派发必须仍在。");
            coordinator.Tick(workspace.Snapshot.UtcNow);

            dialogInspected = false;
            QueueConfirmationDialogResponse(
                floating,
                confirm: false,
                inspect: dialog =>
                {
                    dialogInspected = dialog.BodyText.Text.Contains("退出后不再提醒", StringComparison.Ordinal);
                });
            var closeButton = floating.FindName("CloseFloatingButton") as Button
                ?? throw new InvalidOperationException("无法定位悬浮窗关闭按钮。");
            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                dialogInspected && floating.IsVisible && coordinator.IsDispatching,
                "关闭悬浮窗取消后，悬浮窗和提醒派发必须仍在运行。");
            coordinator.Tick(workspace.Snapshot.UtcNow);

            dialogInspected = false;
            QueueConfirmationDialogResponse(
                floating,
                confirm: false,
                inspect: dialog =>
                {
                    dialogInspected = dialog.BodyText.Text.Contains("退出后不再提醒", StringComparison.Ordinal);
                });
            floating.Close();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                dialogInspected && floating.IsVisible && coordinator.IsDispatching,
                "Alt+F4 取消后必须留在应用里，不能留下没有窗口的进程，提醒派发必须仍在。");
            coordinator.Tick(workspace.Snapshot.UtcNow);

            QueueConfirmationDialogResponse(floating, confirm: true);
            floating.Close();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                !floating.IsVisible && !coordinator.IsDispatching,
                "确认退出后必须关掉悬浮窗并停止提醒派发。");
            return true;
        }
        finally
        {
            if (coordinator.IsDispatching)
            {
                coordinator.Dispose();
            }

            if (main.IsLoaded)
            {
                main.Close();
            }

            if (floating.IsLoaded)
            {
                floating.Close();
            }

            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #156：辅助技术朗读映射——装饰手写体与分段裸数字退出朗读（SilentTextBlock 无自动化
    // peer，也不出现在窗口原始自动化树中）；有可见文字的字段经标签关系（LabeledBy）指向
    // 输入且不携带重复显式名；数量变化由唯一的命令栏数量直播区播完整句；筛选「清除」的
    // 可访问名说明清除的是便签日期筛选；图标按钮继续有可访问名。
    public static bool RunAssistiveNarrationBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-assistive-narration-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        MustApply(workspace, new SaveNoteChange(
            null,
            "朗读探针便签甲",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        MustApply(workspace, new SaveNoteChange(
            null,
            "朗读探针便签乙",
            new DateTime(2038, 7, 9),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var lifecycle = new ManagerWindowLifecycle(
            () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
            PrepareOffscreenValidationWindow);
        var floating = new FloatingWindow(workspace, calendar, lifecycle, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(floating);
        floating.Show();
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            WindowValidationInput.Pump(floating.Dispatcher);

            var mainDecoTexts = new SilentTextBlock[]
            {
                (SilentTextBlock)main.FindName("CountdownDecoTextTop"),
                (SilentTextBlock)main.FindName("CountdownDecoTextBottom"),
                (SilentTextBlock)main.FindName("NotesDecoText")
            };
            var floatingDecoTexts = new SilentTextBlock[]
            {
                (SilentTextBlock)floating.FindName("FloatingNotesDecoTextLeft"),
                (SilentTextBlock)floating.FindName("FloatingNotesDecoText")
            };
            var rootPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(main) ??
                           new System.Windows.Automation.Peers.UIElementAutomationPeer(main);
            Must(
                mainDecoTexts.Concat<FrameworkElement>(floatingDecoTexts).All(deco =>
                    System.Windows.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(deco) is null) &&
                !AutomationRawTreeContains(rootPeer, mainDecoTexts.Concat<FrameworkElement>(floatingDecoTexts).ToList()),
                "装饰手写体必须退出控件朗读：无自动化 peer 且不出现在窗口自动化树中。");

            // 便签分段、数量文字只随便签工作区渲染（RenderVisibleWorkspace），先切换工作区。
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);

            var activeNotesCount = (SilentTextBlock)main.FindName("ActiveNotesCount");
            var completedNotesCount = (SilentTextBlock)main.FindName("CompletedNotesCount");
            Must(
                System.Windows.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(activeNotesCount) is null &&
                System.Windows.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(completedNotesCount) is null,
                "分段内部裸数字不再作为独立内容控件朗读。");

            var labeledFields = new (string InputName, string LabelName, string? HelpText)[]
            {
                ("CountdownName", "CountdownNameLabel", null),
                ("CountdownDate", "CountdownDateLabel", null),
                ("CountdownHour", "CountdownHourLabel", null),
                ("CountdownMinute", "CountdownMinuteLabel", null),
                ("CountdownSecond", "CountdownSecondLabel", null),
                ("DisplayPrecision", "DisplayPrecisionLabel", "悬浮窗显示精度"),
                ("MonthlySalary", "MonthlySalaryLabel", null),
                ("WorkStart", "WorkStartLabel", null),
                ("WorkEnd", "WorkEndLabel", null),
                ("NoteText", "NoteTextLabel", null),
                ("NoteDate", "NoteDateLabel", null),
                ("NoteSearch", "NoteSearchLabel", null),
                ("NoteDateFilter", "NoteDateFilterLabel", "按便签日期筛选"),
                ("NewTagInput", "NewTagInputLabel", null)
            };
            foreach (var (inputName, labelName, helpText) in labeledFields)
            {
                var input = (FrameworkElement)main.FindName(inputName);
                var label = (TextBlock)main.FindName(labelName);
                Must(
                    input is not null && label is not null &&
                    ReferenceEquals(AutomationProperties.GetLabeledBy(input), label) &&
                    label.Text.Length > 0 &&
                    string.IsNullOrEmpty(AutomationProperties.GetName(input)) &&
                    (helpText is null ||
                     string.Equals(AutomationProperties.GetHelpText(input), helpText, StringComparison.Ordinal)),
                    $"字段「{inputName}」必须经标签关系指向可见标签且不携带重复显式名（不读两遍）。");
            }

            var activeSegment = (ToggleButton)main.FindName("ActiveNotesSegment");
            var completedSegment = (ToggleButton)main.FindName("CompletedNotesSegment");
            var recordCount = (TextBlock)main.FindName("RecordCountText");
            Must(
                string.Equals(
                    AutomationProperties.GetName(activeSegment),
                    "查看未完成便签，共 2 条",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(completedSegment),
                    "查看已完成便签，共 0 条",
                    StringComparison.Ordinal) &&
                AutomationProperties.GetLiveSetting(recordCount) == AutomationLiveSetting.Polite &&
                string.Equals(recordCount.Text, "2 个待办", StringComparison.Ordinal),
                "分段数量由分段可访问名承担、数量变化由唯一的命令栏数量直播区播完整句。");

            var clearNoteDateFilter = (Button)main.FindName("ClearNoteDateFilter");
            Must(
                string.Equals(
                    AutomationProperties.GetName(clearNoteDateFilter),
                    "清除便签日期筛选",
                    StringComparison.Ordinal),
                "筛选「清除」的可访问名必须说明清除的是便签日期筛选。");

            Must(
                main.HasAccessibleCoreControls,
                "图标按钮与核心控件继续拥有可访问名（显式名或标签关系）。");

            // 完成一条便签：数量直播区文字随真实数量变化为完整句，分段名同步。
            var completion = VisualDescendants<CheckBox>(main).FirstOrDefault(checkBox =>
                checkBox.Tag is string tagId &&
                workspace.Snapshot.Settings.Notes.Any(note =>
                    string.Equals(note.Id, tagId, StringComparison.OrdinalIgnoreCase)));
            Must(completion is not null, "无法定位朗读探针便签的完成勾选。");
            completion!.IsChecked = true;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                string.Equals(recordCount.Text, "1 个待办", StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(activeSegment),
                    "查看未完成便签，共 1 条",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(completedSegment),
                    "查看已完成便签，共 1 条",
                    StringComparison.Ordinal),
                "便签完成后的数量变化只由一处状态区域以完整句更新。");
            return true;
        }
        finally
        {
            main.Close();
            floating.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #156：非拖动的窗口复位/贴边入口——越界悬浮窗经 Ctrl+R 或托盘菜单「复位窗口位置」
    // 都能被夹回工作区；隐藏态复位先恢复可见再夹回；完成任务的窗口收纳不依赖拖无边框窗。
    public static bool RunWindowPositionResetBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-window-position-reset-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var lifecycle = new ManagerWindowLifecycle(
            () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
            PrepareOffscreenValidationWindow);
        var floating = new FloatingWindow(workspace, calendar, lifecycle, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(floating);
        var trayController = new FloatingTrayController(
            floating,
            Path.Combine(AppContext.BaseDirectory, "countdown-icon.ico"),
            canTransition: static () => true,
            foregroundInterop: null,
            attachShellNotifyIcon: false);
        floating.Show();
        try
        {
            WindowValidationInput.Pump(floating.Dispatcher);
            var workArea = SystemParameters.WorkArea;

            floating.Left = workArea.Right + 120;
            floating.Top = workArea.Top + 40;
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                !WindowIntersectsWorkArea(floating),
                "前置：悬浮窗应完全位于工作区右侧之外。");

            WindowValidationInput.RaisePreviewKey(floating, Key.R, ModifierKeys.Control);
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                WindowIntersectsWorkArea(floating),
                "Ctrl+R 必须把越界悬浮窗夹回工作区（非拖动复位）。");

            floating.Left = workArea.Right + 120;
            floating.Top = workArea.Bottom + 120;
            WindowValidationInput.Pump(floating.Dispatcher);
            trayController.InvokeResetPositionForValidation();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                WindowIntersectsWorkArea(floating),
                "托盘菜单「复位窗口位置」必须把越界悬浮窗夹回工作区。");

            floating.Hide();
            WindowValidationInput.Pump(floating.Dispatcher);
            trayController.InvokeResetPositionForValidation();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                WindowIntersectsWorkArea(floating),
                "隐藏态的托盘复位必须先恢复悬浮窗再夹回工作区。");
            return true;
        }
        finally
        {
            trayController.Dispose();
            floating.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #211：管理窗口装进任意工作区。注入 1080p 在 100%/125%/150% 缩放下的三档
    // 工作区高度（1032/826/688 DIP），窗口必须取「首选 1165×1350 与工作区−安全边距」
    // 的较小值并完整落在工作区内；顶端置顶/最小化/关闭免滚动可达；状态栏经外层
    // 纵向滚动完整可见；倒计时前三行逐行滚动可达完整可见；内容不缩放。用户调整
    // 的720×560 位置尺寸在隐藏后再显示时保留；越界显示与 WM_DISPLAYCHANGE 工作区
    // 变化时夹回所在显示器工作区。全程经注入工作区，不依赖真实显示拓扑。
    public static bool RunMainWindowWorkAreaFitBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-workarea-fit-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var seedBase = new DateTimeOffset(2038, 7, 9, 1, 0, 0, TimeSpan.Zero);
            for (var index = 0; index < 3; index++)
            {
                MustApply(workspace, new SaveCountdownChange(
                    null,
                    $"工作区适配倒计时 {index + 1}",
                    seedBase.AddDays(index + 1),
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
            }

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            var workAreaCases = new[]
            {
                (Width: 1920.0, Height: 1032.0, Dpi: 1.0),
                (Width: 1536.0, Height: 826.0, Dpi: 1.25),
                (Width: 1280.0, Height: 688.0, Dpi: 1.5)
            };
            foreach (var workAreaCase in workAreaCases)
            {
                using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                    new MonitorWorkArea(
                        new Rect(0, 0, workAreaCase.Width, workAreaCase.Height),
                        workAreaCase.Dpi,
                        workAreaCase.Dpi));
                Must(
                    Math.Abs(MonitorWorkAreaResolver.ResolveForScreenRect(new Rect(100, 100, 200, 200), 1, 1).DpiScaleX - workAreaCase.Dpi) <= 0.001,
                    "注入的工作区解析应按注入值返回 DIP 矩形与 DPI。");
                var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
                PrepareOffscreenValidationWindow(main);
                try
                {
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var usableWidth = workAreaCase.Width - 24;
                    var usableHeight = workAreaCase.Height - 24;
                    var expectedWidth = Math.Min(MainWindow.PreferredWidth, usableWidth);
                    var expectedHeight = Math.Min(MainWindow.PreferredHeight, usableHeight);
                    Must(
                        Math.Abs(main.Width - expectedWidth) <= 0.5 &&
                        Math.Abs(main.Height - expectedHeight) <= 0.5,
                        $"注入 {workAreaCase.Width}x{workAreaCase.Height}@{workAreaCase.Dpi} 工作区时窗口应取首选与工作区−安全边距的较小值" +
                        $"（期望 {expectedWidth}x{expectedHeight}，实际 {main.Width}x{main.Height}）。");
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.Width <= workAreaCase.Width - 12 + 0.5 &&
                        main.Top + main.Height <= workAreaCase.Height - 12 + 0.5,
                        $"窗口四角应完全落在注入工作区安全边距内（实际 Left={main.Left} Top={main.Top} " +
                        $"Right={main.Left + main.Width} Bottom={main.Top + main.Height}）。");

                    foreach (var controlName in new[] { "TopmostCheck", "MinimizeButton", "CloseButton" })
                    {
                        var control = (FrameworkElement)main.FindName(controlName);
                        var controlBounds = control.TransformToAncestor(main).TransformBounds(
                            new Rect(new Point(0, 0), control.RenderSize));
                        Must(
                            controlBounds.X >= -0.5 &&
                            controlBounds.Y >= -0.5 &&
                            controlBounds.Right <= main.ActualWidth + 0.5 &&
                            controlBounds.Bottom <= main.ActualHeight + 0.5,
                            $"{controlName} 应无需滚动完整落在窗口可视区内（{controlBounds}，窗口 {main.ActualWidth}x{main.ActualHeight}）。");
                    }

                    var scrollHost = (ScrollViewer)main.FindName("MainWindowScrollHost");
                    Must(
                        scrollHost.ScrollableHeight > 0.5,
                        $"窗口收缩到 {main.ActualWidth}x{main.ActualHeight} 后外层滚动容器应有纵向可滚动区间。");
                    scrollHost.ScrollToBottom();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    var statusBar = (FrameworkElement)main.FindName("MainStatusBarSurface");
                    var statusBounds = statusBar.TransformToAncestor(main).TransformBounds(
                        new Rect(new Point(0, 0), statusBar.RenderSize));
                    Must(
                        statusBounds.Y >= -0.5 &&
                        statusBounds.Bottom <= main.ActualHeight + 0.5,
                        $"滚到底后状态栏应完整落入窗口可视区（{statusBounds}，窗口高 {main.ActualHeight}）。");
                    scrollHost.ScrollToTop();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var listRegion = (ScrollViewer)main.FindName("CountdownListRegion");
                    var rows = (Panel)main.FindName("CountdownRows");
                    var firstRows = rows.Children.OfType<FrameworkElement>().Take(3).ToArray();
                    Must(firstRows.Length == 3, "倒计时列表种子应提供至少 3 行。");
                    for (var rowIndex = 0; rowIndex < firstRows.Length; rowIndex++)
                    {
                        firstRows[rowIndex].BringIntoView();
                        WindowValidationInput.Pump(main.Dispatcher);
                        main.UpdateLayout();
                        var rowBounds = firstRows[rowIndex].TransformToAncestor(listRegion).TransformBounds(
                            new Rect(new Point(0, 0), firstRows[rowIndex].RenderSize));
                        Must(
                            rowBounds.Y >= -0.5 &&
                            rowBounds.Bottom <= listRegion.ViewportHeight + 0.5,
                            $"第 {rowIndex + 1} 行倒计时应能滚动到在内层视口完整可见" +
                            $"（行 {rowBounds}，内层视口高 {listRegion.ViewportHeight}）。");
                    }

                    var scaleViewport = (FrameworkElement)main.FindName("MainScaleViewport");
                    Must(
                        Math.Abs(scaleViewport.ActualWidth - MainWindow.PreferredWidth) <= 0.5 &&
                        Math.Abs(scaleViewport.ActualHeight - MainWindow.PreferredHeight) <= 0.5,
                        "窗口收缩时业务内容不得缩放：MainScaleViewport 应保持 1165×1350 设计尺寸、字号不变。");
                }
                finally
                {
                    main.Close();
                }
            }

            using (var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                       new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1)))
            {
                var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
                PrepareOffscreenValidationWindow(main);
                try
                {
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var handle = new WindowInteropHelper(main).Handle;
                    Must(handle != IntPtr.Zero, "窗口应已创建 HWND。");
                    // 真实用户拖动/缩放手势走 WM_ENTERSIZEMOVE→几何变化→WM_EXITSIZEMOVE；
                    // 探针沿同一 Win32 路径标记「用户调整过位置和尺寸」。
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmEnterSizeMove,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    main.Width = 720;
                    main.Height = 560;
                    main.Left = 500;
                    main.Top = 300;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmExitSizeMove,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Hide();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        Math.Abs(main.Width - 720) <= 0.5 &&
                        Math.Abs(main.Height - 560) <= 0.5 &&
                        Math.Abs(main.Left - 500) <= 0.5 &&
                        Math.Abs(main.Top - 300) <= 0.5,
                        $"隐藏后再显示应保留本次运行内用户调整的位置和尺寸（实际 Left={main.Left} Top={main.Top} {main.Width}x{main.Height}）。");

                    var scrollHost = (ScrollViewer)main.FindName("MainWindowScrollHost");
                    Must(
                        scrollHost.ScrollableWidth > 0.5 && scrollHost.ScrollableHeight > 0.5,
                        "最小尺寸 720×560 下外层容器应纵横双向可滚动（横向兜底），内容不被裁切。");

                    main.Left = 5000;
                    main.Top = 5000;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Hide();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.ActualWidth <= 1920 - 12 + 0.5 &&
                        main.Top + main.ActualHeight <= 1032 - 12 + 0.5,
                        $"越界位置在显示时应夹回所在显示器工作区（实际 Left={main.Left} Top={main.Top}）。");

                    using var smallerOverride = MonitorWorkAreaResolver.OverrideForValidation(
                        new MonitorWorkArea(new Rect(0, 0, 1280, 688), 1.5, 1.5));
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmDisplayChange,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.ActualWidth <= 1280 - 12 + 0.5 &&
                        main.Top + main.ActualHeight <= 688 - 12 + 0.5 &&
                        Math.Abs(main.Width - 720) <= 0.5 &&
                        Math.Abs(main.Height - 560) <= 0.5,
                        $"显示器工作区变化后窗口应夹回新工作区且保留用户尺寸" +
                        $"（实际 Left={main.Left} Top={main.Top} {main.Width}x{main.Height}）。");
                }
                finally
                {
                    main.Close();
                }
            }

            return true;
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #211：标题拖动区覆盖整个身份区空白，身份区内控件仍可点击。经真实
    // WM_NCHITTEST 路径判定：身份区中缝空白返回 HTCAPTION，置顶控件中心返回 HTCLIENT。
    public static bool RunMainWindowIdentityZoneDragBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-identity-drag-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new AdvancingValidationClock(
                    new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero))));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 2560, 1600), 1, 1));
            var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();

                var identityZone = (FrameworkElement)main.FindName("MainIdentityZone");
                var titleZone = (FrameworkElement)main.FindName("MainTitleZone");
                var windowControls = (FrameworkElement)main.FindName("MainWindowControls");
                var topmostCheck = (FrameworkElement)main.FindName("TopmostCheck");

                Rect BoundsInMain(FrameworkElement element) =>
                    element.TransformToAncestor(main).TransformBounds(
                        new Rect(new Point(0, 0), element.RenderSize));
                var identityBounds = BoundsInMain(identityZone);
                var titleBounds = BoundsInMain(titleZone);
                var controlsBounds = BoundsInMain(windowControls);
                var emptyX = (titleBounds.Right + controlsBounds.Left) / 2;
                var emptyY = identityBounds.Top + (identityBounds.Height / 2);
                Must(
                    emptyX > titleBounds.Right + 8 &&
                    emptyX < controlsBounds.Left - 8 &&
                    emptyY > identityBounds.Top + 8 &&
                    emptyY < identityBounds.Bottom - 8,
                    $"探针空白点应落在身份区中缝（x={emptyX} 应位于标题右缘 {titleBounds.Right} 与控件左缘 {controlsBounds.Left} 之间）。");

                var emptyHit = SendWindowHitTest(main, new Point(emptyX, emptyY));
                Must(
                    emptyHit == NativeWorkAreaProbe.HitCaption,
                    $"身份区空白处应作为标题拖动区（HTCAPTION=2），实际命中 {emptyHit}。");

                var topmostCenter = topmostCheck.TransformToAncestor(main).Transform(
                    new Point(topmostCheck.RenderSize.Width / 2, topmostCheck.RenderSize.Height / 2));
                var controlHit = SendWindowHitTest(main, topmostCenter);
                Must(
                    controlHit == NativeWorkAreaProbe.HitClient,
                    $"身份区内置顶控件应保持可点击（HTCLIENT=1），实际命中 {controlHit}。");
                return true;
            }
            finally
            {
                main.Close();
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static int SendWindowHitTest(Window window, Point dipPoint)
    {
        var screenPoint = window.PointToScreen(dipPoint);
        var coordinates = new IntPtr(((int)screenPoint.Y << 16) | ((int)screenPoint.X & 0xffff));
        var handle = new WindowInteropHelper(window).Handle;
        return NativeWorkAreaProbe
            .SendMessage(handle, NativeWorkAreaProbe.WmNcHitTest, IntPtr.Zero, coordinates)
            .ToInt32();
    }

    private static class NativeWorkAreaProbe
    {
        internal const int WmNcHitTest = 0x0084;
        internal const int WmDisplayChange = 0x007E;
        internal const int WmEnterSizeMove = 0x0231;
        internal const int WmExitSizeMove = 0x0232;
        internal const int HitClient = 1;
        internal const int HitCaption = 2;

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "SendMessageW")]
        internal static extern IntPtr SendMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);
    }

    private static bool WindowIntersectsWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;
        return window.Left < area.Right &&
            window.Left + window.ActualWidth > area.Left &&
            window.Top < area.Bottom &&
            window.Top + window.ActualHeight > area.Top;
    }

    // #156：沿原始视图递归窗口自动化树，断言给定元素不再可达（退出朗读的强证据）。
    private static bool AutomationRawTreeContains(
        System.Windows.Automation.Peers.AutomationPeer root,
        IReadOnlyList<FrameworkElement> targets)
    {
        var visited = 0;
        var queue = new Queue<System.Windows.Automation.Peers.AutomationPeer>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            if (++visited > 4_000)
            {
                // 超出遍历上限视为「不可证明缺席」，按包含处理让门禁红出来，而不是静默放过。
                return true;
            }

            var current = queue.Dequeue();
            var owner = (current as System.Windows.Automation.Peers.UIElementAutomationPeer)?.Owner;
            if (owner is not null && targets.Any(target => ReferenceEquals(target, owner)))
            {
                return true;
            }

            IReadOnlyList<System.Windows.Automation.Peers.AutomationPeer>? children;
            try
            {
                children = current?.GetChildren();
            }
            catch (System.Windows.Automation.ElementNotAvailableException)
            {
                continue;
            }

            if (children is null)
            {
                continue;
            }

            foreach (var child in children)
            {
                queue.Enqueue(child);
            }
        }

        return false;
    }

    public static bool RunReminderCenterDeliveryFailureActionGateBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 13, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-delivery-gate-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(session.TrySaveNote(
                    null,
                    "送达失败动作门探针",
                    new DateTime(2036, 5, 13),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立送达失败动作门探针。");
            var note = session.Current.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            Directory.CreateDirectory(settingsPath + ".tmp");
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(dueUtc);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(
                center.IsFailureVisible(key) &&
                !center.IsAcknowledgeEnabled(key) &&
                !center.IsSnoozeEnabled(key) &&
                !center.IsCompleteEnabled(key) &&
                center.IsOpenSourceEnabled(key) &&
                coordinator.ActiveEntryCount == 1,
                "送达保存失败期间没有只保留安全的打开来源动作。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            clock.UtcNow = dueUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsAcknowledgeEnabled(key) &&
                center.IsSnoozeEnabled(key) &&
                center.IsCompleteEnabled(key) &&
                coordinator.ActiveEntryCount == 1 &&
                session.Current.ReminderStates[rule.Id].LastDeliveredOccurrenceKey ==
                    ReminderOccurrenceKey.Create(rule, dueUtc).ToString(),
                "同一 occurrence 送达重试成功后动作门没有恢复。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterEnterUsesNativeButtonActivationBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 14, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-enter-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                session.TrySaveNote(
                    null,
                    "提醒中心 Enter 焦点条目甲",
                    new DateTime(2036, 5, 14),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立 Enter 焦点条目甲。");
            Must(
                session.TrySaveNote(
                    null,
                    "提醒中心 Enter 焦点条目乙",
                    new DateTime(2036, 5, 14),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立 Enter 焦点条目乙。");
            var ruleAlpha = session.Current.Notes
                .Single(note => note.Text == "提醒中心 Enter 焦点条目甲")
                .ReminderRules.Single();
            var ruleBeta = session.Current.Notes
                .Single(note => note.Text == "提醒中心 Enter 焦点条目乙")
                .ReminderRules.Single();
            var keyAlpha = new ActiveReminderOccurrenceKey(ruleAlpha.Id, dueUtc);
            var keyBeta = new ActiveReminderOccurrenceKey(ruleBeta.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(keyAlpha) &&
                center.IsSnoozeEnabled(keyBeta),
                "两条到点提醒没有全部进入可处置状态。");
            Must(
                VisualDescendants<Button>(center).All(button => !button.IsDefault),
                "提醒中心按钮不应再声明 IsDefault 默认按钮。");

            // #215：窗口级不再拦截 Enter——焦点在条目按钮上时，按在窗口上的 Enter 不处置任何条目。
            var snoozeBeta = FindReminderEntryButton(center, keyBeta, "稍后提醒：");
            center.Activate();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Keyboard.Focus(snoozeBeta);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                ReferenceEquals(Keyboard.FocusedElement, snoozeBeta),
                "Enter 前键盘焦点未落在条目乙的稍后提醒按钮。");
            WindowValidationInput.RaiseKey(center, Key.Enter);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                session.Current.ReminderStates[ruleBeta.Id].ActiveSnooze is null &&
                center.IsSnoozeEnabled(keyBeta),
                "按在窗口上的 Enter 不应处置焦点条目。");
            // 焦点离开条目后按 Enter 同样不处置。
            Keyboard.ClearFocus();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            WindowValidationInput.RaiseKey(center, Key.Enter);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                session.Current.ReminderStates[ruleBeta.Id].ActiveSnooze is null,
                "焦点离开条目后 Enter 不应处置任何条目。");

            // 按钮键盘激活交给控件原生行为：按在聚焦按钮自身的 Enter 触发该按钮命令。
            Keyboard.Focus(snoozeBeta);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                ReferenceEquals(Keyboard.FocusedElement, snoozeBeta),
                "Enter 前键盘焦点未落回条目乙的稍后提醒按钮。");
            WindowValidationInput.RaiseKey(snoozeBeta, Key.Enter);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var stateBeta = session.Current.ReminderStates[ruleBeta.Id];
            Must(
                coordinator.ActiveEntryCount == 1 &&
                stateBeta.ActiveSnooze?.OccurrenceKey ==
                    ReminderOccurrenceKey.Create(ruleBeta, dueUtc).ToString() &&
                stateBeta.ActiveSnooze.SnoozeUntilUtc == dueUtc.AddMinutes(10),
                "聚焦按钮上的 Enter 没有原生触发该条目的 10 分钟稍后提醒。");
            Must(
                session.Current.ReminderStates[ruleAlpha.Id].ActiveSnooze is null &&
                center.IsAcknowledgeEnabled(keyAlpha),
                "焦点外条目甲被误处置。");

            var acknowledgeAlpha = FindReminderEntryButton(center, keyAlpha, "知道了：");
            Keyboard.Focus(acknowledgeAlpha);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            WindowValidationInput.RaiseKey(acknowledgeAlpha, Key.Enter);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                session.Current.ReminderStates[ruleAlpha.Id].LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(ruleAlpha, dueUtc).ToString(),
                "聚焦知道了按钮上的 Enter 没有原生确认最后一条并收起空中心。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterArrivalKeepsForegroundAndFlashesTaskbarBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 6, 22, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-arrival-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                session.TrySaveNote(
                    null,
                    "提醒中心到达不抢焦点条目",
                    new DateTime(2036, 6, 22),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立到达条目。");
            Must(
                session.TrySaveNote(
                    null,
                    "提醒中心到达二次条目",
                    new DateTime(2036, 6, 22),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc.AddMinutes(15)),
                    out _),
                "无法建立到达二次条目。");
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 1 && center.IsVisible,
                "到达提醒没有进入可见提醒中心。");
            // #215：出现时不激活、不抢焦点、不持续置顶；显示在任务栏且标题带条目数。
            Must(
                !center.ShowActivated && !center.Topmost && center.ShowInTaskbar,
                "提醒中心出现时必须不激活、不置顶并显示在任务栏。");
            Must(
                !center.IsActive &&
                (Keyboard.FocusedElement is not DependencyObject focused ||
                 !center.IsAncestorOf(focused)),
                "提醒中心到达不得抢占激活态或键盘焦点。");
            Must(
                center.Title.Contains("1 条", StringComparison.Ordinal),
                "提醒中心任务栏标题必须携带条目数。");
            Must(
                center.TaskbarFlashRequestCount >= 1,
                "未激活时新提醒到达必须请求任务栏闪烁。");

            // 用户已激活窗口时，后续到达不再闪烁。
            center.Activate();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(center.IsActive, "到达探针未能激活提醒中心。");
            clock.UtcNow = dueUtc.AddMinutes(15);
            coordinator.Tick(dueUtc.AddMinutes(15));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.TaskbarFlashRequestCount == 1 &&
                center.Title.Contains("2 条", StringComparison.Ordinal),
                "已激活的提醒中心再次到达不应重复闪烁任务栏。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterMinimizeToTaskbarPreservesEntriesBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-minimize-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                session.TrySaveNote(
                    null,
                    "提醒中心收起条目甲",
                    new DateTime(2036, 7, 8),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立收起条目甲。");
            Must(
                session.TrySaveNote(
                    null,
                    "提醒中心收起条目乙",
                    new DateTime(2036, 7, 8),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立收起条目乙。");
            var ruleAlpha = session.Current.Notes
                .Single(note => note.Text == "提醒中心收起条目甲")
                .ReminderRules.Single();
            var keyAlpha = new ActiveReminderOccurrenceKey(ruleAlpha.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(keyAlpha),
                "两条到点提醒没有全部进入可处置状态。");

            // #215：头部「收起」按钮把窗口最小化到任务栏，条目、标题计数与关闭守卫全部保留。
            var minimize = VisualDescendants<Button>(center).FirstOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterMinimizeToTaskbar",
                    StringComparison.Ordinal)) ??
                throw new InvalidOperationException("提醒中心头部缺少「收起」按钮。");
            Must(
                minimize.Content is StackPanel content &&
                content.Children.Count == 2 &&
                content.Children[0] is System.Windows.Shapes.Path &&
                content.Children[1] is TextBlock label &&
                string.Equals(label.Text, "收起", StringComparison.Ordinal),
                "「收起」按钮必须是图标加「收起」文字。");
            Must(
                string.Equals(
                    AutomationProperties.GetName(minimize),
                    "收起提醒中心到任务栏",
                    StringComparison.Ordinal),
                "「收起」按钮缺少可访问名。");

            minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.WindowState == WindowState.Minimized && center.IsVisible,
                "点击「收起」后窗口必须最小化且保持可见。");
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(keyAlpha) &&
                center.Title.Contains("2 条", StringComparison.Ordinal),
                "收起后条目与任务栏标题计数必须保留。");
            center.Close();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsVisible && center.WindowState == WindowState.Minimized,
                "有条目时收起态仍必须拒绝直接关闭。");

            center.WindowState = WindowState.Normal;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.WindowState == WindowState.Normal &&
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(keyAlpha),
                "从任务栏恢复后条目必须完好。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterSnoozeGatedDuringCompletionRetryBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 15, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-snooze-gate-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                session.TrySaveNote(
                    null,
                    "完成重试期间稍后互斥探针",
                    new DateTime(2036, 5, 15),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立完成重试期间稍后互斥探针。");
            var note = session.Current.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            var workspace = new DesktopWorkspace(session);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsCompleteEnabled(key) && center.IsSnoozeEnabled(key),
                "探针条目未进入可处置状态。");

            Directory.CreateDirectory(settingsPath + ".tmp");
            try
            {
                // #155：完成先经确认框（自动确认后提交），写入失败注入保持原语义。
                QueueConfirmationDialogResponse(center, confirm: true);
                Must(center.RequestComplete(key), "无法点击真实完成便签控件。");
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
                const string completionFailure =
                    "完成便签状态保存失败，正在自动重试。便签与提醒条目均保持不变。";
                Must(
                    center.IsFailureVisible(key) &&
                    !center.IsCompleteEnabled(key) &&
                    !center.IsSnoozeEnabled(key) &&
                    !center.IsAcknowledgeEnabled(key),
                    "完成保存失败没有禁用处置动作并展示失败反馈。");
                Must(
                    !center.RequestSnooze(key, ReminderSnoozeOption.TenMinutes, null),
                    "完成等待重试期间稍后提醒探针应被拒绝。");
                Must(
                    !coordinator.InvokeSnoozeRequestForValidation(
                        key,
                        ReminderSnoozeOption.TenMinutes,
                        null),
                    "完成等待重试期间直达调度器的稍后提醒请求应被互斥门拒绝。");
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
                Must(
                    VisualDescendants<TextBlock>(center).Count(text =>
                        string.Equals(
                            AutomationProperties.GetName(text),
                            completionFailure,
                            StringComparison.Ordinal)) == 1 &&
                    center.IsFailureVisible(key),
                    "互斥门拒绝后完成失败反馈应原样保留。");
                Must(
                    coordinator.ActiveEntryCount == 1 &&
                    session.Current.ReminderStates[rule.Id].ActiveSnooze is null &&
                    !session.Current.Notes.Single().IsCompleted,
                    "互斥门拒绝后条目与便签状态应保持不变。");
            }
            finally
            {
                DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            }

            clock.UtcNow = dueUtc.AddSeconds(6);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                session.Current.Notes.Single().IsCompleted,
                "完成重试成功后条目应收起并持久化完成状态。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterCustomSnoozeErrorRoutesByCodeBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 16, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-snooze-code-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                session.TrySaveNote(
                    null,
                    "自定义稍后错误码探针",
                    new DateTime(2036, 5, 16),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立自定义稍后错误码探针。");
            var note = session.Current.Notes.Single();
            var rule = note.ReminderRules.Single();
            var workspace = new DesktopWorkspace(session);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            coordinator.ActiveWindow?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveOccurrenceConfirmed,
                "探针 occurrence 未确认送达。");

            var expiredBeijing = ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(-30));
            var viaWorkspace = workspace.SnoozeReminder(
                ReminderSourceKind.Note,
                note.Id,
                rule.Id,
                dueUtc,
                ReminderSnoozeOption.Custom,
                expiredBeijing);
            Must(
                !viaWorkspace.Succeeded &&
                string.Equals(
                    viaWorkspace.Error,
                    "自定义稍后时间必须晚于当前北京时间。",
                    StringComparison.Ordinal),
                "过期自定义稍后的拒绝文案不应变化。");
            Must(
                viaWorkspace.SnoozeErrorCode == ReminderSnoozeErrorCode.CustomSnoozeNotInFuture,
                "过期自定义稍后要携带结构化错误码。");
            var viaSession = session.TrySnoozeReminder(
                ReminderSourceKind.Note,
                note.Id,
                rule.Id,
                dueUtc,
                ReminderSnoozeOption.Custom,
                expiredBeijing,
                out var sessionError,
                out var sessionErrorCode);
            Must(
                !viaSession &&
                string.Equals(sessionError, viaWorkspace.Error, StringComparison.Ordinal) &&
                sessionErrorCode == ReminderSnoozeErrorCode.CustomSnoozeNotInFuture,
                "会话层过期自定义稍后要给出同一文案与结构化错误码。");

            Must(
                ReminderDeliveryCoordinator.RoutesToCustomSnoozeError(
                    ReminderSnoozeOption.Custom,
                    new DesktopWorkspaceChangeResult(
                        false,
                        "改写后的任意文案",
                        SnoozeErrorCode: ReminderSnoozeErrorCode.CustomSnoozeNotInFuture)),
                "结构化错误码命中时必须路由到自定义稍后错误样式，与文案无关。");
            Must(
                !ReminderDeliveryCoordinator.RoutesToCustomSnoozeError(
                    ReminderSnoozeOption.Custom,
                    new DesktopWorkspaceChangeResult(
                        false,
                        "自定义稍后时间必须晚于当前北京时间。")),
                "错误码缺失时不得仅凭历史文案路由。");
            Must(
                !ReminderDeliveryCoordinator.RoutesToCustomSnoozeError(
                    ReminderSnoozeOption.TenMinutes,
                    new DesktopWorkspaceChangeResult(
                        false,
                        "任意文案",
                        SnoozeErrorCode: ReminderSnoozeErrorCode.CustomSnoozeNotInFuture)),
                "非自定义稍后选项不得路由到自定义错误样式。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterShowFailureRollsBackBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 17, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-show-rollback-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                session.TrySaveNote(
                    null,
                    "提醒中心展示失败回滚探针",
                    new DateTime(2036, 5, 17),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立展示失败回滚探针。");
            var rule = session.Current.Notes.Single().ReminderRules.Single();
            var closedWindow = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            closedWindow.Show();
            closedWindow.ForceClose();
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(
                new DesktopWorkspace(session),
                logger: null,
                windowFactory: () => closedWindow);
            var threw = false;
            try
            {
                coordinator.Tick(dueUtc);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Must(threw, "提醒中心展示失败应继续向宿主抛出。");
            Must(
                coordinator.ActiveWindow is null && coordinator.ActiveEntryCount == 0,
                "展示失败后调度器不应缓存半建窗口或未展示条目。");
            Must(
                closedWindow.EntryCount == 0,
                "展示失败后失败窗口上的条目应被回滚清空。");
            var reloaded = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                !reloaded.Current.ReminderStates.TryGetValue(rule.Id, out var reloadedState) ||
                (reloadedState.LastDeliveredOccurrenceKey is null &&
                 reloadedState.ActiveSnooze is null),
                "展示失败不应改写提醒设置。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterCustomSnoozeControlsVisibilityBehavior()
    {
        var key = new ActiveReminderOccurrenceKey(
            "custom-snooze-visibility-rule",
            new DateTimeOffset(2036, 6, 20, 1, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(window);
        window.AddEntry(new ReminderCenterItem(
            key,
            new NoteRecord(
                "custom-snooze-visibility-note",
                "自定义稍后控件可见性探针",
                "2036-06-20",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        try
        {
            window.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var snoozeChoice = FindReminderEntryControl<ComboBox>(window, key, "稍后提醒时间：");
            var customDate = FindReminderEntryControl<DatePicker>(window, key, "自定义稍后日期：");
            var customHour = FindReminderEntryControl<ComboBox>(window, key, "自定义稍后小时：");
            var customMinute = FindReminderEntryControl<ComboBox>(window, key, "自定义稍后分钟：");
            var customFields = customDate.Parent as WrapPanel
                ?? throw new InvalidOperationException("自定义稍后日期不在自定义稍后控件容器内。");
            Must(
                ReferenceEquals(customHour.Parent, customFields) &&
                ReferenceEquals(customMinute.Parent, customFields),
                "自定义稍后小时和分钟必须与日期共用同一容器。");
            Must(
                customFields.Visibility == Visibility.Collapsed &&
                customDate.Visibility == Visibility.Collapsed &&
                customHour.Visibility == Visibility.Collapsed &&
                customMinute.Visibility == Visibility.Collapsed &&
                !customDate.IsVisible &&
                !customHour.IsVisible &&
                !customMinute.IsVisible,
                "初始预设项下自定义稍后容器与三个子控件必须全部折叠。");

            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                customFields.Visibility == Visibility.Visible &&
                customDate.Visibility == Visibility.Visible &&
                customHour.Visibility == Visibility.Visible &&
                customMinute.Visibility == Visibility.Visible &&
                customDate.IsVisible &&
                customHour.IsVisible &&
                customMinute.IsVisible,
                "选择「自定义」后自定义稍后三个子控件必须随容器真实展开。");

            snoozeChoice.SelectedIndex = 0;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                customFields.Visibility == Visibility.Collapsed &&
                customDate.Visibility == Visibility.Collapsed &&
                customHour.Visibility == Visibility.Collapsed &&
                customMinute.Visibility == Visibility.Collapsed &&
                !customDate.IsVisible &&
                !customHour.IsVisible &&
                !customMinute.IsVisible,
                "切回预设项后自定义稍后三个子控件必须随容器恢复折叠。");

            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                customDate.IsVisible && customHour.IsVisible && customMinute.IsVisible,
                "再次选择「自定义」后三个子控件必须重新可见。");
            return true;
        }
        finally
        {
            if (window.IsVisible)
            {
                window.ForceClose();
            }
        }
    }

    public static bool RunReminderCenterCustomSnoozeCalendarBehavior()
    {
        var key = new ActiveReminderOccurrenceKey(
            "custom-snooze-calendar-rule",
            new DateTimeOffset(2036, 6, 21, 1, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(window);
        window.AddEntry(new ReminderCenterItem(
            key,
            new NoteRecord(
                "custom-snooze-calendar-note",
                "自定义稍后日历行为探针",
                "2036-06-21",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        try
        {
            window.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var snoozeChoice = FindReminderEntryControl<ComboBox>(window, key, "稍后提醒时间：");
            var customDate = FindReminderEntryControl<DatePicker>(window, key, "自定义稍后日期：");
            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            window.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                customDate.Visibility == Visibility.Visible && customDate.IsVisible,
                "自定义稍后日历未处于真实可见状态，挂接断言无效。");
            Must(
                PickerTemplateParts.HasDatePickerContract(customDate),
                "自定义稍后日历未使用全应用统一日期模板。");
            customDate.ApplyTemplate();
            Must(
                customDate.Template.FindName("PART_TextBox", customDate) is DatePickerTextBox
                {
                    IsReadOnly: true,
                    Focusable: false
                },
                "自定义稍后日历文本框未保持全应用统一只读。");
            var keyboardWorks = WindowValidationInput.ExerciseDatePicker(customDate, out var keyboardDiagnostic);
            Must(
                keyboardWorks,
                "自定义稍后日历未实现打开聚焦、Esc 关闭并回收焦点的统一行为。" + keyboardDiagnostic);
            customDate.ApplyTemplate();
            Must(
                customDate.Template.FindName("PART_TextBox", customDate) is DatePickerTextBox
                {
                    IsReadOnly: true,
                    Focusable: false
                },
                "自定义稍后日历弹层关闭后未恢复只读锁定。");
            return true;
        }
        finally
        {
            if (window.IsVisible)
            {
                window.ForceClose();
            }
        }
    }

    public static bool RunReminderCenterShowFailureRollbackSurvivesEntryFailureBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 18, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-rollback-atomic-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                session.TrySaveNote(
                    null,
                    "提醒中心回滚原子性探针",
                    new DateTime(2036, 5, 18),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15),
                    out _),
                "无法建立回滚原子性探针。");
            var rule = session.Current.Notes.Single().ReminderRules.Single();
            var closedWindow = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0,
                RemoveEntryThrowsForValidation = true
            };
            closedWindow.Show();
            closedWindow.ForceClose();
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(
                new DesktopWorkspace(session),
                logger: null,
                windowFactory: () => closedWindow);
            var threw = false;
            try
            {
                coordinator.Tick(dueUtc);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Must(threw, "条目回滚失败仍应把展示失败抛给宿主。");
            Must(
                closedWindow.EntryCount == 1,
                "注入的条目回滚失败未命中，回滚原子性断言无效。");
            Must(
                coordinator.ActiveWindow is null && coordinator.ActiveEntryCount == 0,
                "条目回滚抛异常时协调器仍必须清理缓存窗口与活动条目。");
            var reloaded = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                !reloaded.Current.ReminderStates.TryGetValue(rule.Id, out var reloadedState) ||
                (reloadedState.LastDeliveredOccurrenceKey is null &&
                 reloadedState.ActiveSnooze is null),
                "条目回滚失败不应改写提醒设置。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterOpenSourcePreservesNoteGroupStateBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-open-source-groups-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 9, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        var dueUtc = nowUtc.AddMinutes(10);
        MustApply(workspace, new SaveNoteChange(
            null,
            "来源保持-甲",
            new DateTime(2038, 7, 10),
            false,
            Array.Empty<string>(),
            ReminderSchedule.CreateOnce(dueUtc)));
        MustApply(workspace, new SaveNoteChange(
            null,
            "来源保持-乙",
            new DateTime(2038, 7, 10),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        MustApply(workspace, new SaveNoteChange(
            null,
            "来源保持-丙",
            new DateTime(2038, 7, 9),
            true,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var relative = new CountdownRelativeReminderRule("open-source-groups-relative", 0);
        MustApply(workspace, new SaveCountdownChange(
            null,
            "来源保持-丁",
            dueUtc,
            Array.Empty<string>(),
            new ReminderRule[] { relative }));
        var noteAlpha = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "来源保持-甲");
        var noteGamma = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "来源保持-丙");
        var keyAlpha = new ActiveReminderOccurrenceKey(noteAlpha.ReminderRules.Single().Id, dueUtc);
        var keyCountdown = new ActiveReminderOccurrenceKey(relative.Id, dueUtc);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        var coordinator = new ReminderDeliveryCoordinator(
            workspace,
            logger,
            (kind, id) => main.OpenReminderSource(kind, id));
        try
        {
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var completedSegment = (ToggleButton)main.FindName("CompletedNotesSegment");
            completedSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindNoteDateDisclosure(main, noteGamma.Date, isCompleted: true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindNoteDateDisclosure(main, noteAlpha.Date, isCompleted: false)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                NoteStatusSectionIsShown(main, true) &&
                NoteStatusSectionIsHidden(main, false) &&
                !NoteDateSectionIsExpanded(main, noteGamma.Date, true) &&
                !NoteDateSectionIsExpanded(main, noteAlpha.Date, false) &&
                !main.CompletedNotesAreCollapsed,
                "初始分段与分组收放状态未建立。");

            clock.UtcNow = dueUtc;
            coordinator.Tick(dueUtc);
            WindowValidationInput.Pump(main.Dispatcher);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            Must(
                coordinator.ActiveEntryCount == 2,
                "便签甲与倒计时丁应同时到点入队。");

            Must(center.RequestOpenSource(keyAlpha), "未接受便签来源打开请求。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.CurrentWorkspace == WorkspaceKind.Notes &&
                NoteReminderHighlightVisible(main, noteAlpha) &&
                NoteStatusSectionIsShown(main, false) &&
                NoteStatusSectionIsHidden(main, true) &&
                NoteDateSectionIsExpanded(main, noteAlpha.Date, false) &&
                Keyboard.FocusedElement is Button { Tag: string focusedNote } &&
                string.Equals(focusedNote, noteAlpha.Id, StringComparison.OrdinalIgnoreCase),
                "打开来源应临时切换分段、揭示、高亮并聚焦来源便签。");

            clock.UtcNow = dueUtc.AddMinutes(1);
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                NoteStatusSectionIsShown(main, true) &&
                NoteStatusSectionIsHidden(main, false) &&
                !NoteDateSectionIsExpanded(main, noteAlpha.Date, false) &&
                !NoteDateSectionIsExpanded(main, noteGamma.Date, true) &&
                !NoteReminderHighlightVisible(main, noteAlpha),
                "重渲染后来源揭示应还原为用户保持的分段与分组收放状态。");

            Must(
                main.OpenReminderSource(ReminderSourceKind.Note, noteGamma.Id),
                "未能直接打开已完成便签来源。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                NoteReminderHighlightVisible(main, noteGamma) &&
                NoteStatusSectionIsShown(main, true) &&
                NoteStatusSectionIsHidden(main, false) &&
                NoteDateSectionIsExpanded(main, noteGamma.Date, true),
                "已完成便签来源应临时揭示且不改写持久分段状态。");

            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                NoteStatusSectionIsShown(main, true) &&
                !NoteDateSectionIsExpanded(main, noteGamma.Date, true) &&
                !NoteReminderHighlightVisible(main, noteGamma),
                "重渲染后已完成日期分组应恢复收起。");

            Must(center.RequestOpenSource(keyCountdown), "未接受倒计时来源打开请求。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.CurrentWorkspace == WorkspaceKind.Countdowns &&
                NoteStatusSectionIsHidden(main, false),
                "倒计时来源路径不应影响便签分段状态。");
            return true;
        }
        finally
        {
            coordinator.Dispose();
            if (main.IsVisible)
            {
                main.Close();
            }

            foreach (var window in Application.Current.Windows
                         .OfType<ReminderCenterWindow>()
                         .ToArray())
            {
                window.ForceClose();
            }

            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static Button FindReminderEntryButton(
        ReminderCenterWindow center,
        ActiveReminderOccurrenceKey key,
        string automationNamePrefix)
    {
        foreach (var row in VisualDescendants<Border>(center))
        {
            if (row.Tag is not ActiveReminderOccurrenceKey rowKey || rowKey != key)
            {
                continue;
            }

            foreach (var button in VisualDescendants<Button>(row))
            {
                if (AutomationProperties.GetName(button)?.StartsWith(
                        automationNamePrefix,
                        StringComparison.Ordinal) == true)
                {
                    return button;
                }
            }
        }

        throw new InvalidOperationException($"提醒中心缺少条目 {key.RuleId} 的 {automationNamePrefix} 按钮。");
    }

    private static T FindReminderEntryControl<T>(
        ReminderCenterWindow center,
        ActiveReminderOccurrenceKey key,
        string automationNamePrefix)
        where T : FrameworkElement
    {
        foreach (var row in VisualDescendants<Border>(center))
        {
            if (row.Tag is not ActiveReminderOccurrenceKey rowKey || rowKey != key)
            {
                continue;
            }

            foreach (var control in VisualDescendants<T>(row))
            {
                if (AutomationProperties.GetName(control)?.StartsWith(
                        automationNamePrefix,
                        StringComparison.Ordinal) == true)
                {
                    return control;
                }
            }
        }

        throw new InvalidOperationException($"提醒中心缺少条目 {key.RuleId} 的 {automationNamePrefix} 控件。");
    }

    private static Button FindNoteDateDisclosure(
        MainWindow main,
        string? noteDate,
        bool isCompleted)
    {
        var status = isCompleted ? "已完成" : "未完成";
        foreach (var button in VisualDescendants<Button>(main))
        {
            if (button.Uid == "NoteDateDisclosure" &&
                button.Tag is string tag &&
                string.Equals(tag, noteDate ?? string.Empty, StringComparison.Ordinal) &&
                AutomationProperties.GetName(button)?.Contains(
                    $"{status}便签",
                    StringComparison.Ordinal) == true)
            {
                return button;
            }
        }

        throw new InvalidOperationException($"找不到 {noteDate} 的{status}便签日期分组按钮。");
    }

    private static bool NoteDateSectionIsExpanded(MainWindow main, string? noteDate, bool isCompleted) =>
        AutomationProperties.GetName(FindNoteDateDisclosure(main, noteDate, isCompleted))
            ?.Contains("当前展开", StringComparison.Ordinal) == true;

    private static bool NoteStatusSectionIsShown(MainWindow main, bool isCompleted)
    {
        var rows = (StackPanel)main.FindName(isCompleted ? "CompletedNoteRows" : "ActiveNoteRows");
        var segment = (ToggleButton)main.FindName(isCompleted ? "CompletedNotesSegment" : "ActiveNotesSegment");
        return rows.Visibility == Visibility.Visible && segment.IsChecked == true;
    }

    private static bool NoteStatusSectionIsHidden(MainWindow main, bool isCompleted)
    {
        var rows = (StackPanel)main.FindName(isCompleted ? "CompletedNoteRows" : "ActiveNoteRows");
        var segment = (ToggleButton)main.FindName(isCompleted ? "CompletedNotesSegment" : "ActiveNotesSegment");
        return rows.Visibility == Visibility.Collapsed && segment.IsChecked != true;
    }

    private static bool NoteReminderHighlightVisible(MainWindow main, NoteRecord note) =>
        VisualDescendants<Border>(main).Any(border =>
            string.Equals(
                AutomationProperties.GetName(border),
                $"当前提醒来源便签：{note.Text}",
                StringComparison.Ordinal));

    public static bool RunReminderEditingInterfaceBehavior()
    {
        var editor = new ReminderEditing();
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 3, TimeSpan.Zero);
        var futureBeijing = new DateTimeOffset(2034, 5, 7, 10, 20, 0, TimeSpan.FromHours(8));
        var noReminder = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.None,
            null,
            -1,
            -1,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc,
            null,
            false));
        Must(noReminder.Succeeded && noReminder.Reminder is NoReminder && noReminder.Error.Length == 0,
            "不提醒模式未通过 ReminderEditing interface。");

        foreach (var mode in new[] { ReminderEditingMode.Once, ReminderEditingMode.Repeating })
        {
            var incomplete = editor.Build(new ReminderEditingRequest(
                mode,
                null,
                -1,
                -1,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                null,
                true));
            Must(!incomplete.Succeeded &&
                incomplete.Error == "请选择完整的首次提醒日期和时间。",
                $"{mode} 未拒绝不完整的首次提醒时间。");

            var notFuture = editor.Build(new ReminderEditingRequest(
                mode,
                new DateTime(2034, 5, 6),
                9,
                2,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                null,
                true));
            Must(!notFuture.Succeeded &&
                notFuture.Error == "首次提醒时间必须晚于当前北京时间。",
                $"{mode} 未拒绝不在未来的首次提醒时间。");
        }

        var intervalCases = new[]
        {
            (ReminderIntervalSelection.FifteenMinutes, string.Empty, 15),
            (ReminderIntervalSelection.ThirtyMinutes, string.Empty, 30),
            (ReminderIntervalSelection.SixtyMinutes, string.Empty, 60),
            (ReminderIntervalSelection.Custom, "1", 1),
            (ReminderIntervalSelection.Custom, "1440", 1440)
        };
        foreach (var (selection, custom, expectedMinutes) in intervalCases)
        {
            var result = editor.Build(new ReminderEditingRequest(
                ReminderEditingMode.Repeating,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                selection,
                custom,
                nowUtc,
                null,
                true));
            Must(result is { Succeeded: true, Reminder: RepeatingReminder repeating } &&
                repeating.FirstDueUtc == futureBeijing.ToUniversalTime() &&
                repeating.Interval.Minutes == expectedMinutes,
                $"重复提醒间隔 {expectedMinutes} 分钟未通过 ReminderEditing interface。");
        }

        foreach (var invalidCustom in new[] { "0", "1441", "不是数字" })
        {
            var result = editor.Build(new ReminderEditingRequest(
                ReminderEditingMode.Repeating,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                ReminderIntervalSelection.Custom,
                invalidCustom,
                nowUtc,
                null,
                true));
            Must(!result.Succeeded && result.Error == "自定义提醒必须是 1 到 1440 分钟。",
                $"无效自定义间隔“{invalidCustom}”未保留现有中文错误。");
        }

        var existingOnce = new OnceReminder(nowUtc.AddDays(-1), OnceDeliveryState.Delivered);
        var preservedOnce = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.Once,
            null,
            -1,
            -1,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc,
            existingOnce,
            false));
        Must(preservedOnce.Succeeded && Equals(existingOnce, preservedOnce.Reminder),
            "未改动 schedule 时没有完整保留既有 Once reminder。");

        var existingRepeating = new RepeatingReminder(
            nowUtc.AddDays(-3),
            new ReminderInterval(30));
        var preservedRepeating = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.Repeating,
            futureBeijing.Date,
            futureBeijing.Hour,
            futureBeijing.Minute,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc,
            existingRepeating,
            false));
        Must(preservedRepeating.Succeeded && Equals(existingRepeating, preservedRepeating.Reminder),
            "interval 与 schedule 未变时没有完整保留既有 Repeating reminder。");

        var changedInterval = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.Repeating,
            futureBeijing.Date,
            futureBeijing.Hour,
            futureBeijing.Minute,
            ReminderIntervalSelection.SixtyMinutes,
            string.Empty,
            nowUtc,
            existingRepeating,
            false));
        Must(changedInterval is { Succeeded: true, Reminder: RepeatingReminder changedIntervalReminder } &&
            !Equals(existingRepeating, changedIntervalReminder) &&
            changedIntervalReminder.Interval.Minutes == 60 &&
            changedIntervalReminder.FirstDueUtc == futureBeijing.ToUniversalTime(),
            "interval 改变时没有生成新的 Repeating reminder。");

        var changedSchedule = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.Repeating,
            futureBeijing.Date,
            futureBeijing.Hour,
            futureBeijing.Minute,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc,
            existingRepeating,
            true));
        Must(changedSchedule is { Succeeded: true, Reminder: RepeatingReminder changedScheduleReminder } &&
            !Equals(existingRepeating, changedScheduleReminder) &&
            changedScheduleReminder.Interval.Minutes == 30 &&
            changedScheduleReminder.FirstDueUtc == futureBeijing.ToUniversalTime(),
            "首次时间改变时没有生成新的 Repeating reminder。");

        var onceDefault = editor.GetDefaultFirstDueBeijing(
            ReminderEditingMode.Once,
            ReminderIntervalSelection.SixtyMinutes,
            string.Empty,
            nowUtc);
        var repeatingDefault = editor.GetDefaultFirstDueBeijing(
            ReminderEditingMode.Repeating,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc);
        var customDefault = editor.GetDefaultFirstDueBeijing(
            ReminderEditingMode.Repeating,
            ReminderIntervalSelection.Custom,
            "1",
            nowUtc);
        Must(onceDefault.Succeeded &&
            onceDefault.FirstDueBeijing == new DateTimeOffset(2034, 5, 6, 9, 17, 0, TimeSpan.FromHours(8)),
            "Once 默认首次时间未按北京时间加 15 分钟。");
        Must(repeatingDefault.Succeeded &&
            repeatingDefault.FirstDueBeijing == new DateTimeOffset(2034, 5, 6, 9, 32, 0, TimeSpan.FromHours(8)),
            "Repeating 默认首次时间未使用预设 interval。");
        Must(customDefault.Succeeded &&
            customDefault.FirstDueBeijing == new DateTimeOffset(2034, 5, 6, 9, 3, 0, TimeSpan.FromHours(8)),
            "Repeating 默认首次时间未使用自定义 interval。");

        var ruleEditor = new ReminderRuleEditing(new ValidationGuidSource(
            Guid.Parse("11111111-1111-1111-1111-111111111111")));
        var addedRule = ruleEditor.SaveRule(
            Array.Empty<ReminderRule>(),
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(addedRule is { Succeeded: true, Rules.Count: 1 } &&
            addedRule.Rules[0] is AbsoluteOnceReminderRule
            {
                Id: "11111111111111111111111111111111"
            } addedOnce &&
            addedOnce.ScheduledUtc == futureBeijing.ToUniversalTime(),
            "空规则集合未能通过共享编辑器新增绝对一次规则。");

        var editedBeijing = futureBeijing.AddHours(2);
        var editedRule = ruleEditor.SaveRule(
            addedRule.Rules,
            new ReminderRuleEditingRequest(
                addedRule.Rules[0].Id,
                ReminderEditingMode.Once,
                editedBeijing.Date,
                editedBeijing.Hour,
                editedBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(editedRule is { Succeeded: true, Rules.Count: 1 } &&
            editedRule.Rules[0] is AbsoluteOnceReminderRule editedOnce &&
            editedOnce.Id == addedRule.Rules[0].Id &&
            editedOnce.ScheduledUtc == editedBeijing.ToUniversalTime(),
            "原位编辑提醒规则没有保留 rule ID。");

        var copyEditor = new ReminderRuleEditing(new ValidationGuidSource(
            Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var copiedDue = editedBeijing.AddHours(1);
        var copiedRule = copyEditor.SaveRule(
            editedRule.Rules,
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                copiedDue.Date,
                copiedDue.Hour,
                copiedDue.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(copiedRule is { Succeeded: true, Rules.Count: 2 } &&
            copiedRule.Rules[1].Id == "22222222222222222222222222222222" &&
            copiedRule.Rules[1].Id != copiedRule.Rules[0].Id,
            "复制规则保存为唯一日程后没有生成新 rule ID。");

        var deletedRule = copyEditor.DeleteRule(copiedRule.Rules, copiedRule.Rules[0].Id);
        Must(deletedRule is { Succeeded: true, Rules.Count: 1 } &&
            deletedRule.Rules[0].Id == copiedRule.Rules[1].Id,
            "删除规则误删或改写了同便签的其他规则。");

        var boundaryGuidValues = Enumerable.Range(1, 9)
            .Select(index => Guid.Parse($"{index:X8}-0000-0000-0000-000000000000"))
            .ToArray();
        var boundaryEditor = new ReminderRuleEditing(new ValidationGuidSource(boundaryGuidValues));
        IReadOnlyList<ReminderRule> eightRules = Array.Empty<ReminderRule>();
        for (var index = 0; index < 8; index++)
        {
            var due = futureBeijing.AddMinutes(index);
            var result = boundaryEditor.SaveRule(
                eightRules,
                new ReminderRuleEditingRequest(
                    null,
                    ReminderEditingMode.Once,
                    due.Date,
                    due.Hour,
                    due.Minute,
                    ReminderIntervalSelection.ThirtyMinutes,
                    string.Empty,
                    nowUtc));
            Must(result.Succeeded, $"第 {index + 1} 条提醒规则未能保存。");
            eightRules = result.Rules;
        }

        Must(eightRules.Count == 8, "共享规则编辑器未接受 8 条规则边界。");
        var ninthDue = futureBeijing.AddMinutes(9);
        var ninthRule = boundaryEditor.SaveRule(
            eightRules,
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                ninthDue.Date,
                ninthDue.Hour,
                ninthDue.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(!ninthRule.Succeeded &&
            ninthRule.Rules.SequenceEqual(eightRules) &&
            ninthRule.ErrorTarget == ReminderRuleEditingErrorTarget.Collection,
            "共享规则编辑器未拒绝第 9 条规则并把错误归位到规则集合。");

        var firstRuleOnly = Array.AsReadOnly(new[] { eightRules[0] });
        var duplicateRule = boundaryEditor.SaveRule(
            firstRuleOnly,
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(!duplicateRule.Succeeded &&
            duplicateRule.Rules.SequenceEqual(firstRuleOnly) &&
            duplicateRule.ErrorTarget == ReminderRuleEditingErrorTarget.FirstDue,
            "共享规则编辑器未拒绝重复的规范日程并把错误归位到时间输入。");

        var invalidInterval = boundaryEditor.SaveRule(
            Array.Empty<ReminderRule>(),
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Repeating,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                ReminderIntervalSelection.Custom,
                "0",
                nowUtc));
        Must(!invalidInterval.Succeeded &&
            invalidInterval.ErrorTarget == ReminderRuleEditingErrorTarget.Interval,
            "共享规则编辑器未把非法间隔错误归位到间隔输入。");

        var beijingNow = ChinaStandardTime.ToBeijingTime(nowUtc);
        var pastAbsolute = boundaryEditor.SaveRule(
            Array.Empty<ReminderRule>(),
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                beijingNow.Date,
                beijingNow.Hour,
                beijingNow.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(!pastAbsolute.Succeeded &&
            pastAbsolute.ErrorTarget == ReminderRuleEditingErrorTarget.FirstDue,
            "共享规则编辑器未拒绝过去的绝对时间并把错误归位到时间输入。");

        Must(RunMainReminderRuleDeletionBehavior(),
            "主窗口未能把共享编辑器中的 1→0 规则删除原子保存到磁盘、state 和提醒中心。");
        Must(RunDelayedReminderRuleCommitBehavior(),
            "主窗口延迟最终提交未拒绝已经过期的新规则并原子保留原状态。");
        Must(RunHistoricalReminderRuleCommitBehavior(),
            "历史规则的正文编辑、改期拒绝或删除事务不符合最终校验边界。");
        Must(RunMainDelayedReminderRuleCommitBehavior(),
            "主窗口最终提交失败后未重开规则编辑器并归位首次提醒错误。");
        Must(RunReminderRuleTypeTransitionBehavior(nowUtc),
            "共享规则编辑 module 未能在相对、绝对一次与固定间隔之间安全切换并保留 rule ID。");
        Must(RunReminderRulesEditorWindowBehavior(nowUtc),
            "共享提醒规则窗口未通过真实 WPF 事件完成实时校验、dirty 保存或取消恢复。");

        return true;
    }

    private static bool RunReminderRuleTypeTransitionBehavior(DateTimeOffset nowUtc)
    {
        const string ruleId = "reminder-type-transition";
        var editing = new ReminderRuleEditing();
        var relative = new CountdownRelativeReminderRule(ruleId, 30);
        var dueBeijing = ChinaStandardTime.ToBeijingTime(nowUtc.AddHours(2));
        var targetUtc = nowUtc.AddHours(4);

        var toAbsolute = editing.SaveRule(
            new ReminderRule[] { relative },
            new ReminderRuleEditingRequest(
                ruleId,
                ReminderEditingMode.Once,
                dueBeijing.Date,
                dueBeijing.Hour,
                dueBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                targetUtc));
        var toRepeating = editing.SaveRule(
            new ReminderRule[] { relative },
            new ReminderRuleEditingRequest(
                ruleId,
                ReminderEditingMode.Repeating,
                dueBeijing.Date,
                dueBeijing.Hour,
                dueBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                targetUtc));
        var absolute = new AbsoluteOnceReminderRule(ruleId, dueBeijing.ToUniversalTime());
        var toRelative = editing.SaveRule(
            new ReminderRule[] { absolute },
            new ReminderRuleEditingRequest(
                ruleId,
                ReminderEditingMode.CountdownRelative,
                null,
                -1,
                -1,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                targetUtc,
                "30"));

        return toAbsolute is
                   { Succeeded: true, Rules: [AbsoluteOnceReminderRule absoluteResult] } &&
               absoluteResult.Id == ruleId &&
               toRepeating is
                   { Succeeded: true, Rules: [FixedIntervalReminderRule repeatingResult] } &&
               repeatingResult.Id == ruleId &&
               toRelative is
                   { Succeeded: true, Rules: [CountdownRelativeReminderRule relativeResult] } &&
               relativeResult.Id == ruleId;
    }

    private static void InvokeRuleEditorButton(Button button)
    {
        // #203: editor actions now bind commands; Click alone skips them.
        Must(WindowValidationInput.InvokeButton(button), "提醒规则编辑器按钮不支持真实 Invoke。");
        WindowValidationInput.Pump(button.Dispatcher);
    }

    internal static bool ExerciseReminderRulesEntry(Window owner, Button entry)
    {
        var opened = false;
        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                var editors = Application.Current.Windows
                    .OfType<ReminderRulesEditorWindow>()
                    .Where(window => ReferenceEquals(window.Owner, owner))
                    .ToArray();
                opened = editors.Length == 1 &&
                    editors[0].IsVisible &&
                    string.Equals(
                        AutomationProperties.GetName(editors[0]),
                        "提醒规则编辑器",
                        StringComparison.Ordinal);
                if (editors.Length == 1)
                {
                    InvokeRuleEditorButton(FindByAutomationId<Button>(editors[0], "CancelReminderRulesButton"));
                }
            }));
        entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WindowValidationInput.Pump(owner.Dispatcher);
        return opened &&
            !Application.Current.Windows
                .OfType<ReminderRulesEditorWindow>()
                .Any(window => ReferenceEquals(window.Owner, owner));
    }

    private static bool RunReminderRulesEditorWindowBehavior(DateTimeOffset nowUtc)
    {
        var clock = new AdvancingValidationClock(nowUtc);
        var ids = new[]
        {
            Guid.Parse("11111111-0000-0000-0000-000000000000"),
            Guid.Parse("22222222-0000-0000-0000-000000000000"),
            Guid.Parse("33333333-0000-0000-0000-000000000000")
        };
        var actionsWorked = false;
        var formModesWorked = false;
        var editCancelWorked = false;
        var copyCancelWorked = false;
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(ids));
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                formModesWorked = ProbeReminderRuleFormModes(editor, supportsRelative: false);
                var firstDue = ChinaStandardTime.ToBeijingTime(nowUtc).AddDays(1);
                firstDue = new DateTimeOffset(
                    firstDue.Year,
                    firstDue.Month,
                    firstDue.Day,
                    10,
                    20,
                    0,
                    firstDue.Offset);
                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue, 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                var originalEdit = VisualDescendants<Button>(editor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                var originalId = originalEdit.Tag as string;
                InvokeRuleEditorButton(originalEdit);
                editCancelWorked = CancelRuleEditHasVisibleLabelAndResetsForm(editor);
                InvokeRuleEditorButton(originalEdit);
                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(1), 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                InvokeRuleEditorButton(VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content?.ToString(), "复制", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, originalId, StringComparison.Ordinal)));
                copyCancelWorked = CancelRuleEditHasVisibleLabelAndResetsForm(editor);
                InvokeRuleEditorButton(VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content?.ToString(), "复制", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, originalId, StringComparison.Ordinal)));
                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(2), 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                var copiedId = VisualDescendants<Button>(editor)
                    .Where(button => string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal))
                    .Select(button => button.Tag as string)
                    .Single(id => !string.Equals(id, originalId, StringComparison.Ordinal));
                InvokeRuleEditorButton(VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content?.ToString(), "删除", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, originalId, StringComparison.Ordinal)));

                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(3), 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
                actionsWorked = editor.ResultRules.Count == 2 &&
                    editor.ResultRules.Any(rule => string.Equals(rule.Id, copiedId, StringComparison.Ordinal)) &&
                    editor.ResultRules.All(rule => !string.Equals(rule.Id, originalId, StringComparison.Ordinal));
            }));
            var accepted = editor.ShowDialog() == true;
            Must(accepted, "共享提醒规则编辑器未接受有效的新增、编辑、复制与删除草稿。");
            Must(actionsWorked, "共享提醒规则编辑器未保留复制项、删除原项或提交 dirty 草稿。");
            Must(formModesWorked && editCancelWorked && copyCancelWorked,
                $"提醒规则可见表单回归：modeRows={formModesWorked}, editCancel={editCancelWorked}, copyCancel={copyCancelWorked}。");
        }
        finally
        {
            if (editor.IsVisible)
            {
                editor.Close();
            }
        }

        var delayedClock = new AdvancingValidationClock(nowUtc);
        var delayedEditor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            delayedClock,
            new ValidationGuidSource(Guid.Parse("44444444-0000-0000-0000-000000000000")));
        var rejectedAtLiveTime = false;
        try
        {
            delayedEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var firstDue = ChinaStandardTime.ToBeijingTime(nowUtc.AddMinutes(3));
                SetEditorSchedule(delayedEditor, ReminderEditingMode.Once, firstDue, 30);
                delayedClock.UtcNow = nowUtc.AddMinutes(4);
                InvokeRuleEditorButton(FindByAutomationId<Button>(delayedEditor, "AcceptReminderRulesButton"));
                var error = FindByAutomationId<TextBlock>(delayedEditor, "ReminderRuleFirstDueError");
                rejectedAtLiveTime = delayedEditor.IsVisible &&
                    delayedEditor.ResultRules.Count == 0 &&
                    error.Visibility == Visibility.Visible &&
                    string.Equals(
                        error.Text,
                        "首次提醒时间必须晚于当前北京时间。",
                        StringComparison.Ordinal);

                var correctedDue = ChinaStandardTime.ToBeijingTime(delayedClock.UtcNow.AddMinutes(10));
                SetEditorSchedule(delayedEditor, ReminderEditingMode.Once, correctedDue, 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(delayedEditor, "AcceptReminderRulesButton"));
            }));
            Must(delayedEditor.ShowDialog() == true,
                "共享提醒规则编辑器未在修正实时过期输入后接受草稿。");
            Must(rejectedAtLiveTime,
                "共享提醒规则编辑器未按最终提交时钟拒绝已经过期的首次提醒。");
            Must(delayedEditor.ResultRules.Count == 1,
                "共享提醒规则编辑器修正实时过期输入后未只保存一条规则。");
        }
        finally
        {
            if (delayedEditor.IsVisible)
            {
                delayedEditor.Close();
            }
        }

        var initialRule = new AbsoluteOnceReminderRule("cancel-rule", nowUtc.AddDays(1));
        var cancelledEditor = new ReminderRulesEditorWindow(
            new ReminderRule[] { initialRule },
            clock);
        try
        {
            cancelledEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                InvokeRuleEditorButton(VisualDescendants<Button>(cancelledEditor).Single(button =>
                        string.Equals(button.Content?.ToString(), "删除", StringComparison.Ordinal)));
                InvokeRuleEditorButton(FindByAutomationId<Button>(cancelledEditor, "CancelReminderRulesButton"));
            }));
            Must(cancelledEditor.ShowDialog() == false,
                "共享提醒规则编辑器取消删除后未以取消结果关闭。");
            Must(cancelledEditor.ResultRules.Count == 1 &&
                 cancelledEditor.ResultRules[0].Id == initialRule.Id,
                "共享提醒规则编辑器取消删除后未恢复原规则。");
            Must(RunCountdownRelativeReminderRulesEditorWindowBehavior(nowUtc),
                "倒计时相对提醒规则编辑器未通过真实 WPF 行为。");
            Must(RunCalendarReminderRulesEditorWindowBehavior(nowUtc),
                "日历提醒规则编辑器未通过真实 WPF 行为。");
            return true;
        }
        finally
        {
            if (cancelledEditor.IsVisible)
            {
                cancelledEditor.Close();
            }
        }
    }

    private static bool RunCountdownRelativeReminderRulesEditorWindowBehavior(
        DateTimeOffset nowUtc)
    {
        var clock = new AdvancingValidationClock(nowUtc);
        var relativeRuleId = Guid.Parse("55555555-0000-0000-0000-000000000000");
        var targetUtc = nowUtc.AddMinutes(5_256_010);
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(relativeRuleId),
            countdownTargetUtc: targetUtc);
        var editedExistingRule = false;
        var formModesWorked = false;
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                formModesWorked = ProbeReminderRuleFormModes(editor, supportsRelative: true);
                var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
                mode.SelectedIndex = 2;
                FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes").Text = "0";
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                var edits = VisualDescendants<Button>(editor).Where(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal)).ToArray();
                if (edits.Length != 1)
                {
                    InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "CancelReminderRulesButton"));
                    return;
                }

                var edit = edits[0];
                var createdId = edit.Tag as string;
                InvokeRuleEditorButton(edit);
                FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes").Text =
                    "5256000";
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
                editedExistingRule =
                    mode.Items.Count == 4 &&
                    string.Equals(createdId, relativeRuleId.ToString("N"), StringComparison.Ordinal) &&
                    editor.ResultRules.SingleOrDefault() is CountdownRelativeReminderRule relative &&
                    string.Equals(relative.Id, createdId, StringComparison.Ordinal) &&
                    relative.OffsetMinutesBefore == 5_256_000;
            }));

            Must(editor.ShowDialog() == true,
                "倒计时相对规则编辑器未接受有效的创建和编辑草稿。");
            Must(editedExistingRule,
                "倒计时相对规则编辑器未保留 rule ID、最大 offset 或完整类型列表。");
            Must(formModesWorked, "倒计时提醒规则模式切换后存在孤立标签、时刻分隔符或未折叠空行。");
        }
        finally
        {
            if (editor.IsVisible)
            {
                editor.Close();
            }
        }

        const string transitionRuleId = "countdown-relative-type-transition";
        var transitionEditor = new ReminderRulesEditorWindow(
            new ReminderRule[] { new CountdownRelativeReminderRule(transitionRuleId, 30) },
            clock,
            countdownTargetUtc: nowUtc.AddHours(4));
        var typeTransitionsWorked = false;
        try
        {
            transitionEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dueBeijing = ChinaStandardTime.ToBeijingTime(nowUtc.AddHours(2));
                var edit = VisualDescendants<Button>(transitionEditor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                InvokeRuleEditorButton(edit);
                SetEditorSchedule(transitionEditor, ReminderEditingMode.Once, dueBeijing, 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton"));

                edit = VisualDescendants<Button>(transitionEditor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                InvokeRuleEditorButton(edit);
                FindByAutomationId<ComboBox>(transitionEditor, "ReminderRuleMode").SelectedIndex = 2;
                FindByAutomationId<TextBox>(transitionEditor, "ReminderRuleRelativeOffsetMinutes").Text = "30";
                InvokeRuleEditorButton(FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton"));

                edit = VisualDescendants<Button>(transitionEditor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                InvokeRuleEditorButton(edit);
                SetEditorSchedule(transitionEditor, ReminderEditingMode.Repeating, dueBeijing, 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton"));
                InvokeRuleEditorButton(FindByAutomationId<Button>(transitionEditor, "AcceptReminderRulesButton"));
                typeTransitionsWorked =
                    transitionEditor.ResultRules.SingleOrDefault() is FixedIntervalReminderRule finalRule &&
                    string.Equals(finalRule.Id, transitionRuleId, StringComparison.Ordinal);
            }));

            Must(transitionEditor.ShowDialog() == true,
                "倒计时相对规则类型切换后未接受有效草稿。");
            Must(typeTransitionsWorked,
                "倒计时相对、绝对一次和固定间隔切换未保留 rule ID。");
        }
        finally
        {
            if (transitionEditor.IsVisible)
            {
                transitionEditor.Close();
            }
        }

        var rejectedEditor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("66666666-0000-0000-0000-000000000000")),
            countdownTargetUtc: nowUtc.AddMinutes(30));
        var pastRejectedAtOffset = false;
        try
        {
            rejectedEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                FindByAutomationId<ComboBox>(rejectedEditor, "ReminderRuleMode").SelectedIndex = 2;
                FindByAutomationId<TextBox>(rejectedEditor, "ReminderRuleRelativeOffsetMinutes").Text = "60";
                InvokeRuleEditorButton(FindByAutomationId<Button>(rejectedEditor, "SaveReminderRuleButton"));
                var error = FindByAutomationId<TextBlock>(
                    rejectedEditor,
                    "ReminderRuleRelativeOffsetError");
                pastRejectedAtOffset =
                    rejectedEditor.ResultRules.Count == 0 &&
                    error.Visibility == Visibility.Visible &&
                    error.Text.Contains("派生时间晚于当前北京时间", StringComparison.Ordinal);
                InvokeRuleEditorButton(FindByAutomationId<Button>(rejectedEditor, "CancelReminderRulesButton"));
            }));
            Must(rejectedEditor.ShowDialog() == false,
                "倒计时相对规则无效 offset 探针未以取消结果关闭。");
            Must(pastRejectedAtOffset,
                "倒计时相对规则未拒绝过去的派生时刻或未把错误归位到 offset。");
        }
        finally
        {
            if (rejectedEditor.IsVisible)
            {
                rejectedEditor.Close();
            }
        }

        var noteEditor = new ReminderRulesEditorWindow(Array.Empty<ReminderRule>(), clock)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            noteEditor.Show();
            WindowValidationInput.Pump(noteEditor.Dispatcher);
            Must(FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleMode").Items.Count == 3,
                "便签规则编辑器未只暴露绝对一次、固定间隔和日历重复类型。");
            Must(FindByAutomationId<TextBox>(noteEditor, "ReminderRuleRelativeOffsetMinutes")
                    .Visibility == Visibility.Collapsed,
                "便签规则编辑器错误暴露了倒计时相对 offset。");
            return true;
        }
        finally
        {
            noteEditor.Close();
        }
    }

    private static bool RunCalendarReminderRulesEditorWindowBehavior(DateTimeOffset nowUtc)
    {
        var clock = new AdvancingValidationClock(nowUtc);
        var noteEditor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("77777777-0000-0000-0000-000000000000")));
        var noteErrorLocalized = false;
        var noteSelectorErrorAssociated = false;
        var noteSelectorErrorCasesLocalized = false;
        var noteThemeWorked = false;
        var noteKeyboardWorked = false;
        try
        {
            noteEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mode = FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleMode");
                mode.SelectedIndex = 2;
                var frequency = FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleCalendarFrequency");
                var interval = FindByAutomationId<TextBox>(noteEditor, "ReminderRuleCalendarInterval");
                frequency.SelectedIndex = 1;
                interval.Text = "2";
                FindByAutomationId<DatePicker>(noteEditor, "ReminderRuleFirstDueDate").SelectedDate =
                    new DateTime(2032, 1, 5);
                FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleFirstDueHour").SelectedIndex = 9;
                FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleFirstDueMinute").SelectedIndex = 30;
                foreach (var day in Enum.GetValues<DayOfWeek>())
                {
                    FindByAutomationId<CheckBox>(noteEditor, $"ReminderRuleCalendarWeekday{day}")
                        .IsChecked = false;
                }

                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton"));
                var error = FindByAutomationId<TextBlock>(noteEditor, "ReminderRuleCalendarSelectorError");
                var form = error.Parent as Grid ??
                           throw new InvalidOperationException("日历 selector 错误不在规则表单 Grid 中。");
                var selectorInput = form.Children.Cast<UIElement>().Single(element =>
                    Grid.GetColumn(element) == 1 &&
                    Grid.GetRow(element) == Grid.GetRow(error) - 1);
                var monday = FindByAutomationId<CheckBox>(
                    noteEditor,
                    "ReminderRuleCalendarWeekdayMonday");
                noteErrorLocalized = error.Visibility == Visibility.Visible &&
                    error.Text.Contains("至少选择一个星期", StringComparison.Ordinal) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(monday),
                        error.Text,
                        StringComparison.Ordinal) &&
                    monday.IsKeyboardFocusWithin;
                noteSelectorErrorAssociated =
                    string.Equals(
                        AutomationProperties.GetAutomationId(error),
                        "ReminderRuleCalendarSelectorError",
                        StringComparison.Ordinal) &&
                    selectorInput is StackPanel selectorPanel &&
                    selectorPanel.IsAncestorOf(monday) &&
                    Grid.GetRow(error) == Grid.GetRow(selectorPanel) + 1 &&
                    Grid.GetColumn(error) == Grid.GetColumn(selectorPanel);

                frequency.SelectedIndex = 2;
                var daySelector = FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleCalendarDay");
                var lastDay = FindByAutomationId<CheckBox>(noteEditor, "ReminderRuleCalendarLastDay");
                daySelector.SelectedIndex = -1;
                lastDay.IsChecked = false;
                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton"));
                var monthlyLocalized = error.Visibility == Visibility.Visible &&
                    error.Text.Contains("按月重复", StringComparison.Ordinal) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(lastDay),
                        error.Text,
                        StringComparison.Ordinal) &&
                    lastDay.IsKeyboardFocusWithin;

                frequency.SelectedIndex = 3;
                FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleCalendarMonth").SelectedIndex = -1;
                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton"));
                var yearlyLocalized = error.Visibility == Visibility.Visible &&
                    error.Text.Contains("按年重复", StringComparison.Ordinal) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(lastDay),
                        error.Text,
                        StringComparison.Ordinal) &&
                    lastDay.IsKeyboardFocusWithin;
                noteSelectorErrorCasesLocalized = monthlyLocalized && yearlyLocalized;

                frequency.SelectedIndex = 1;
                monday.IsChecked = true;
                FindByAutomationId<CheckBox>(noteEditor, "ReminderRuleCalendarWeekdayFriday")
                    .IsChecked = true;
                noteThemeWorked = ReferenceEquals(
                    frequency.Background,
                    Application.Current.FindResource("CanvasBrush"));
                frequency.Focus();
                frequency.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                noteKeyboardWorked = interval.IsKeyboardFocusWithin;
                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton"));
                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "AcceptReminderRulesButton"));
            }));

            Must(noteEditor.ShowDialog() == true, "便签日历规则编辑器未接受有效草稿。");
            Must(noteErrorLocalized, "便签日历规则 selector 错误未归位。 ");
            Must(noteSelectorErrorAssociated,
                "日历 selector 错误没有与 selector 控件关联并显示在其下一行。");
            Must(noteSelectorErrorCasesLocalized,
                "monthly/yearly selector 错误没有在 selector 控件就近显示、聚焦和提供 HelpText。");
            Must(noteThemeWorked, "便签日历规则控件未使用当前主题资源。");
            Must(noteKeyboardWorked, "便签日历规则控件未进入连续 Tab 顺序。");
            Must(noteEditor.ResultRules.SingleOrDefault() is CalendarReminderRule
            {
                Frequency: CalendarFrequency.Weekly,
                Interval: 2,
                Selector: WeeklyCalendarSelector weekly
            } && weekly.Weekdays.SequenceEqual([DayOfWeek.Monday, DayOfWeek.Friday]),
                "便签日历规则未保存类型化周 selector。");
        }
        finally
        {
            if (noteEditor.IsVisible)
            {
                noteEditor.Close();
            }
        }

        var countdownEditor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("88888888-0000-0000-0000-000000000000")),
            countdownTargetUtc: nowUtc.AddYears(10));
        try
        {
            countdownEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleMode").SelectedIndex = 3;
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleCalendarFrequency").SelectedIndex = 2;
                FindByAutomationId<TextBox>(countdownEditor, "ReminderRuleCalendarInterval").Text = "1";
                FindByAutomationId<DatePicker>(countdownEditor, "ReminderRuleFirstDueDate").SelectedDate =
                    new DateTime(2031, 1, 1);
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleFirstDueHour").SelectedIndex = 10;
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleFirstDueMinute").SelectedIndex = 0;
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleCalendarDay").SelectedIndex = 30;
                InvokeRuleEditorButton(FindByAutomationId<Button>(countdownEditor, "SaveReminderRuleButton"));
                InvokeRuleEditorButton(FindByAutomationId<Button>(countdownEditor, "AcceptReminderRulesButton"));
            }));
            Must(countdownEditor.ShowDialog() == true, "倒计时日历规则编辑器未接受有效草稿。");
            Must(countdownEditor.ResultRules.SingleOrDefault() is CalendarReminderRule
                {
                    Frequency: CalendarFrequency.Monthly,
                    Selector: MonthlyCalendarSelector { Day: 31, LastDay: false }
                }, "倒计时日历规则未保存类型化月 selector。");
            return true;
        }
        finally
        {
            if (countdownEditor.IsVisible)
            {
                countdownEditor.Close();
            }
        }
    }

    private static bool CancelRuleEditHasVisibleLabelAndResetsForm(ReminderRulesEditorWindow editor)
    {
        editor.UpdateLayout();
        var cancel = FindByAutomationId<Button>(editor, "CancelReminderRuleEditButton");
        var labeled = cancel.IsVisible && cancel.ActualWidth > 0 && cancel.ActualHeight > 0 &&
            string.Equals(cancel.Content as string, "取消编辑", StringComparison.Ordinal);
        var originalCount = VisualDescendants<Button>(editor).Count(button =>
            string.Equals(button.Content as string, "编辑", StringComparison.Ordinal));
        InvokeRuleEditorButton(cancel);
        return labeled && !cancel.IsVisible &&
            string.Equals(FindByAutomationId<Button>(editor, "SaveReminderRuleButton").Content as string,
                "添加规则", StringComparison.Ordinal) &&
            VisualDescendants<Button>(editor).Count(button =>
                string.Equals(button.Content as string, "编辑", StringComparison.Ordinal)) == originalCount;
    }

    private static bool ProbeReminderRuleFormModes(ReminderRulesEditorWindow editor, bool supportsRelative)
    {
        var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
        var knownLabels = new[] { "规则类型", "首次提醒", "开始日期", "固定间隔", "提前分钟", "日历频率", "重复间隔", "日历选择" };
        var passed = true;
        void Check(string scenario, params string[] expected)
        {
            WindowValidationInput.Pump(editor.Dispatcher);
            editor.UpdateLayout();
            var labels = VisualDescendants<TextBlock>(editor)
                .Where(label => label.Parent is Grid &&
                    knownLabels.Contains(label.Text, StringComparer.Ordinal)).ToArray();
            var actual = labels.Where(label => label.IsVisible).Select(label => label.Text).ToArray();
            var firstDueVisible = expected.Contains("首次提醒") || expected.Contains("开始日期");
            var visibleFields = new[]
            {
                ("ReminderRuleFirstDueDate", firstDueVisible),
                ("ReminderRuleFirstDueHour", firstDueVisible),
                ("ReminderRuleFirstDueMinute", firstDueVisible),
                ("ReminderRuleInterval", expected.Contains("固定间隔")),
                ("ReminderRuleRelativeOffsetMinutes", expected.Contains("提前分钟")),
                ("ReminderRuleCalendarFrequency", expected.Contains("日历频率")),
                ("ReminderRuleCalendarInterval", expected.Contains("重复间隔"))
            }.All(field => FindByAutomationId<FrameworkElement>(editor, field.Item1).IsVisible == field.Item2);
            var timeSeparatorMatches = VisualDescendants<TextBlock>(editor)
                .Any(text => text.Text == ":" && text.IsVisible) == firstDueVisible;
            var emptyRowsCollapse = VisualDescendants<Grid>(editor)
                .Where(grid => grid.IsVisible &&
                    (grid.IsAncestorOf(mode) || labels.Any(label => ReferenceEquals(label.Parent, grid))))
                .All(grid => grid.RowDefinitions.Select((row, index) => (row, index)).All(pair =>
                    grid.Children.Cast<UIElement>().Any(child => Grid.GetRow(child) == pair.index && child.IsVisible) ||
                    pair.row.ActualHeight <= 0.01));
            var result = actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)) &&
                visibleFields && timeSeparatorMatches && emptyRowsCollapse;
            passed &= result;
            if (!result)
            {
                Console.Error.WriteLine("REMINDER_FORM_DIAGNOSTIC " + JsonSerializer.Serialize(new
                { scenario, supportsRelative, expected, actual, visibleFields, timeSeparatorMatches, emptyRowsCollapse }));
            }
        }

        mode.SelectedIndex = 0;
        Check("once", "规则类型", "首次提醒");
        mode.SelectedIndex = 1;
        Check("interval", "规则类型", "首次提醒", "固定间隔");
        FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval").SelectedIndex = 3;
        FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval").Text = "0";
        InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
        passed &= FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError").IsVisible;
        mode.SelectedIndex = 0;
        Check("once-after-error", "规则类型", "首次提醒");
        passed &= !FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError").IsVisible;
        if (supportsRelative)
        {
            mode.SelectedIndex = 2;
            Check("relative", "规则类型", "提前分钟");
        }
        mode.SelectedIndex = supportsRelative ? 3 : 2;
        FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarFrequency").SelectedIndex = 1;
        Check("calendar-weekly", "规则类型", "开始日期", "日历频率", "重复间隔", "日历选择");
        FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarFrequency").SelectedIndex = 3;
        Check("calendar-yearly", "规则类型", "开始日期", "日历频率", "重复间隔", "日历选择");
        mode.SelectedIndex = 0;
        Check("once-after-calendar", "规则类型", "首次提醒");
        return passed;
    }

    private static void SetEditorSchedule(
        ReminderRulesEditorWindow editor,
        ReminderEditingMode mode,
        DateTimeOffset due,
        int intervalMinutes)
    {
        FindByAutomationId<ComboBox>(editor, "ReminderRuleMode").SelectedIndex =
            mode == ReminderEditingMode.Once ? 0 : 1;
        FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate").SelectedDate = due.Date;
        FindByAutomationId<ComboBox>(editor, "ReminderRuleFirstDueHour").SelectedIndex = due.Hour;
        FindByAutomationId<ComboBox>(editor, "ReminderRuleFirstDueMinute").SelectedIndex = due.Minute;
        var interval = FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval");
        interval.SelectedIndex = intervalMinutes switch
        {
            15 => 0,
            30 => 1,
            60 => 2,
            _ => 3
        };
        FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval").Text =
            intervalMinutes is 15 or 30 or 60
                ? string.Empty
                : intervalMinutes.ToString(CultureInfo.InvariantCulture);
    }

    private static T FindByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        var matches = VisualDescendants<T>(root)
            .Where(element => string.Equals(
                AutomationProperties.GetAutomationId(element),
                automationId,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one {typeof(T).Name} with automation ID '{automationId}', found {matches.Length}.");
        }

        return matches[0];
    }

    private static bool RunMainReminderRuleDeletionBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-rule-delete-ui-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 0, TimeSpan.Zero);
        var dueUtc = nowUtc.AddMinutes(1);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        var rule = new AbsoluteOnceReminderRule("ui-delete-rule", dueUtc);
        var saved = workspace.Apply(new SaveNoteChange(
            null,
            "真实 WPF 删除唯一规则",
            ChinaStandardTime.ToBeijingTime(nowUtc).Date,
            false,
            Array.Empty<string>(),
            new ReminderRule[] { rule }));
        if (!saved.Succeeded)
        {
            return false;
        }

        var noteId = workspace.Snapshot.Settings.Notes.Single().Id;
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        using var coordinator = new ReminderDeliveryCoordinator(workspace, logger);
        try
        {
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            clock.UtcNow = dueUtc;
            coordinator.Tick(dueUtc);
            WindowValidationInput.Pump(main.Dispatcher);
            var before = workspace.Snapshot.Settings;
            if (coordinator.ActiveEntryCount != 1 ||
                !before.ReminderStates.ContainsKey(rule.Id))
            {
                return false;
            }

            var edit = VisualDescendants<Button>(main).Single(button =>
                string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                string.Equals(button.Tag as string, noteId, StringComparison.Ordinal));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            main.Dispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows
                    .OfType<ReminderRulesEditorWindow>()
                    .Single(window => ReferenceEquals(window.Owner, main));
                var delete = VisualDescendants<Button>(editor).Single(button =>
                    AutomationProperties.GetName(button).StartsWith(
                        "删除规则 ",
                        StringComparison.Ordinal));
                InvokeRuleEditorButton(delete);
                var complete = VisualDescendants<Button>(editor).Single(button =>
                    string.Equals(
                        AutomationProperties.GetName(button),
                        "接受提醒规则草稿",
                        StringComparison.Ordinal));
                InvokeRuleEditorButton(complete);
            }));
            ((Button)main.FindName("ManageNoteReminderRulesButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)main.FindName("SaveNoteButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);

            var memory = workspace.Snapshot.Settings;
            var disk = store.Load(settingsPath).Settings;
            return memory.Notes.Single(note => note.Id == noteId).ReminderRules.Count == 0 &&
                disk.Notes.Single(note => note.Id == noteId).ReminderRules.Count == 0 &&
                !memory.ReminderStates.ContainsKey(rule.Id) &&
                !disk.ReminderStates.ContainsKey(rule.Id) &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0;
        }
        finally
        {
            if (main.IsVisible)
            {
                main.Close();
            }

            DeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
            File.Delete(diagnosticPath);
        }
    }

    private static bool RunDelayedReminderRuleCommitBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-rule-delay-{Guid.NewGuid():N}.settings.json");
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        try
        {
            var initialSave = workspace.Apply(new SaveNoteChange(
                null,
                "延迟提交规则",
                ChinaStandardTime.ToBeijingTime(nowUtc).Date,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            if (!initialSave.Succeeded)
            {
                return false;
            }

            var note = workspace.Snapshot.Settings.Notes.Single();
            var candidateRules = new ReminderRule[]
            {
                new AbsoluteOnceReminderRule("delayed-rule", nowUtc.AddMinutes(1))
            };
            var beforeMemory = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var beforeDisk = File.ReadAllText(settingsPath);
            clock.UtcNow = nowUtc.AddMinutes(2);
            var result = workspace.Apply(new SaveNoteChange(
                note.Id,
                note.Text,
                DateTime.ParseExact(note.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                note.IsCompleted,
                note.TagIds,
                candidateRules));
            return !result.Succeeded &&
                result.ReminderRuleErrorTarget == ReminderRuleEditingErrorTarget.FirstDue &&
                string.Equals(
                    beforeMemory,
                    SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                    StringComparison.Ordinal) &&
                string.Equals(beforeDisk, File.ReadAllText(settingsPath), StringComparison.Ordinal) &&
                candidateRules.Length == 1 &&
                candidateRules[0].Id == "delayed-rule";
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }
    }

    private static bool RunHistoricalReminderRuleCommitBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-rule-history-{Guid.NewGuid():N}.settings.json");
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 20, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var historicalRule = new AbsoluteOnceReminderRule(
            "historical-rule",
            nowUtc.AddMinutes(-10));
        var historicalNote = new NoteRecord(
            "historical-note",
            "历史规则",
            "2034-05-06",
            false,
            Array.Empty<string>(),
            new ReminderRule[] { historicalRule });
        var occurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
            historicalRule,
            historicalRule.ScheduledUtc).ToString();
        try
        {
            store.Save(settingsPath, CountdownSettings.Empty with
            {
                Notes = new[] { historicalNote },
                ReminderStates = new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase)
                {
                    [historicalRule.Id] = new ReminderRuleState(
                        historicalRule.Id,
                        historicalRule.ScheduledUtc,
                        occurrenceKey)
                }
            });
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            var bodyEdit = workspace.Apply(new SaveNoteChange(
                historicalNote.Id,
                "历史规则正文已编辑",
                new DateTime(2034, 5, 7),
                false,
                Array.Empty<string>(),
                historicalNote.ReminderRules));
            var afterBodyEdit = workspace.Snapshot.Settings;
            var beforeRejectedChange = SettingsContract.SerializeCanonical(afterBodyEdit);
            var diskBeforeRejectedChange = File.ReadAllText(settingsPath);
            var changedPastRule = new AbsoluteOnceReminderRule(
                historicalRule.Id,
                nowUtc.AddMinutes(-5));
            var changedSchedule = workspace.Apply(new SaveNoteChange(
                historicalNote.Id,
                "不应提交的历史改期",
                new DateTime(2034, 5, 8),
                false,
                Array.Empty<string>(),
                new ReminderRule[] { changedPastRule }));
            var afterRejectedChange = workspace.Snapshot.Settings;
            var diskAfterRejectedChange = File.ReadAllText(settingsPath);
            var deletion = workspace.Apply(new SaveNoteChange(
                historicalNote.Id,
                "历史规则已停用",
                new DateTime(2034, 5, 9),
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var afterDeletion = workspace.Snapshot.Settings;
            return bodyEdit.Succeeded &&
                afterBodyEdit.Notes.Single().Text == "历史规则正文已编辑" &&
                afterBodyEdit.ReminderStates.ContainsKey(historicalRule.Id) &&
                !changedSchedule.Succeeded &&
                changedSchedule.ReminderRuleErrorTarget == ReminderRuleEditingErrorTarget.FirstDue &&
                string.Equals(
                    beforeRejectedChange,
                    SettingsContract.SerializeCanonical(afterRejectedChange),
                    StringComparison.Ordinal) &&
                string.Equals(
                    diskBeforeRejectedChange,
                    diskAfterRejectedChange,
                    StringComparison.Ordinal) &&
                deletion.Succeeded &&
                afterDeletion.Notes.Single().ReminderRules.Count == 0 &&
                !afterDeletion.ReminderStates.ContainsKey(historicalRule.Id);
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }
    }

    private static bool RunMainDelayedReminderRuleCommitBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-rule-delay-main-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        var initialSave = workspace.Apply(new SaveNoteChange(
            null,
            "主窗口延迟提交归位",
            ChinaStandardTime.ToBeijingTime(nowUtc).Date,
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        if (!initialSave.Succeeded)
        {
            return false;
        }

        var noteId = workspace.Snapshot.Settings.Notes.Single().Id;
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(
            workspace,
            calendar,
            new DiagnosticLogger(diagnosticPath),
            AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        var reopenedWithFirstDueError = false;
        try
        {
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            VisualDescendants<Button>(main).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, noteId, StringComparison.Ordinal))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            main.Dispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows
                    .OfType<ReminderRulesEditorWindow>()
                    .Single(window => ReferenceEquals(window.Owner, main));
                SetEditorSchedule(
                    editor,
                    ReminderEditingMode.Once,
                    ChinaStandardTime.ToBeijingTime(nowUtc.AddMinutes(3)),
                    30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            }));
            ((Button)main.FindName("ManageNoteReminderRulesButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            clock.UtcNow = nowUtc.AddMinutes(4);
            main.Dispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows
                    .OfType<ReminderRulesEditorWindow>()
                    .Single(window => ReferenceEquals(window.Owner, main));
                var error = FindByAutomationId<TextBlock>(editor, "ReminderRuleFirstDueError");
                reopenedWithFirstDueError = editor.IsVisible &&
                    error.Visibility == Visibility.Visible &&
                    string.Equals(
                        error.Text,
                        "首次提醒时间必须晚于当前北京时间。",
                        StringComparison.Ordinal);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "CancelReminderRulesButton"));
            }));
            ((Button)main.FindName("SaveNoteButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);

            var memory = workspace.Snapshot.Settings;
            var disk = store.Load(settingsPath).Settings;
            return reopenedWithFirstDueError &&
                memory.Notes.Single().ReminderRules.Count == 0 &&
                disk.Notes.Single().ReminderRules.Count == 0;
        }
        finally
        {
            if (main.IsVisible)
            {
                main.Close();
            }

            DeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
            File.Delete(diagnosticPath);
        }
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in VisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    public static bool RunCalendarReminderBehavior()
    {
        var nowUtc = new DateTimeOffset(2032, 3, 15, 2, 0, 0, TimeSpan.Zero);
        var daily = new CalendarReminderRule(
            "calendar-note-daily",
            CalendarFrequency.Daily,
            2,
            new DateOnly(2032, 3, 1),
            new TimeOnly(9, 0),
            new DailyCalendarSelector());
        var monthly = new CalendarReminderRule(
            "calendar-countdown-monthly",
            CalendarFrequency.Monthly,
            1,
            new DateOnly(2032, 1, 1),
            new TimeOnly(10, 0),
            new MonthlyCalendarSelector(14, false));
        var note = new NoteRecord(
            "calendar-note",
            "日历便签",
            "2032-03-01",
            false,
            Array.Empty<string>(),
            [daily]);
        var countdown = new CountdownRecord(
            "calendar-countdown",
            "日历倒计时",
            nowUtc.AddYears(1),
            Array.Empty<string>(),
            [monthly]);
        var handledDailyUtc = new DateTimeOffset(2032, 3, 5, 1, 0, 0, TimeSpan.Zero);
        var settings = new CountdownSettings(
            [countdown],
            [note],
            Array.Empty<TagRecord>(),
            CountdownSettings.Empty.Preferences,
            new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase)
            {
                [daily.Id] = new(daily.Id, handledDailyUtc, null)
            });

        var scheduler = new ReminderScheduler();
        scheduler.Initialize(settings, nowUtc);
        var due = scheduler.GetDueOccurrences(settings, nowUtc);
        Must(due.Count == 2, "日历规则未让便签和倒计时进入同一调度批次。");
        Must(
            due.Single(item => item.Rule.Id == daily.Id).ScheduledUtc ==
            new DateTimeOffset(2032, 3, 15, 1, 0, 0, TimeSpan.Zero),
            "日历冷启动未只选择 cursor 后最新错过 occurrence。");
        Must(
            due.Single(item => item.Rule.Id == monthly.Id).ScheduledUtc ==
            new DateTimeOffset(2032, 3, 14, 2, 0, 0, TimeSpan.Zero),
            "倒计时来源未按北京时间选择日历 occurrence。");

        var advanced = settings with
        {
            ReminderStates = new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase)
            {
                [daily.Id] = new(daily.Id, due.Single(item => item.Rule.Id == daily.Id).ScheduledUtc, null),
                [monthly.Id] = new(monthly.Id, due.Single(item => item.Rule.Id == monthly.Id).ScheduledUtc, null)
            }
        };
        var restarted = new ReminderScheduler();
        restarted.Initialize(advanced, nowUtc);
        Must(restarted.GetDueOccurrences(advanced, nowUtc).Count == 0,
            "日历规则跨重启重放了已推进 cursor 的 occurrence。");

        var failedClosed = false;
        var failingScheduler = new ReminderScheduler((_, _, _) =>
            throw new InvalidOperationException("injected Ical.Net failure"));
        try
        {
            _ = failingScheduler.GetDueOccurrences(settings, nowUtc);
        }
        catch (SettingsValidationException exception)
        {
            failedClosed = exception.InnerException is InvalidOperationException;
        }

        Must(failedClosed, "Ical.Net 异常未让日历调度失败闭锁。");
        Must(RunCalendarRuleCommitCursorBehavior(),
            "新增或改期的日历规则未在同一保存事务推进 commit cursor。");
        Must(RunCalendarWeeklyRuleEditStateBehavior(),
            "周规则无关字段编辑未保留 cursor、处置历史和提醒中心条目。");
        Must(RunCalendarWorkspaceWpfBehavior(),
            "日历规则未在真实工作区、两类来源、WPF 渲染和统一提醒中心中端到端工作。");
        return true;
    }

    private static bool RunCalendarRuleCommitCursorBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-calendar-commit-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var commitNowUtc = new DateTimeOffset(2032, 4, 15, 4, 0, 0, TimeSpan.Zero);
        var clock = new CountingAdvancingValidationClock(commitNowUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        var logger = new DiagnosticLogger(diagnosticPath);
        using var coordinator = new ReminderDeliveryCoordinator(workspace, logger, (_, _) => { });
        try
        {
            var monthlyThirtyFirst = new CalendarReminderRule(
                "calendar-commit-note",
                CalendarFrequency.Monthly,
                1,
                new DateOnly(2032, 1, 31),
                new TimeOnly(9, 0),
                new MonthlyCalendarSelector(31, false));
            var readsBeforeNoteSave = clock.ReadCount;
            MustApply(workspace, new SaveNoteChange(
                null,
                "月末历史跳过",
                new DateTime(2032, 4, 15),
                false,
                Array.Empty<string>(),
                new ReminderRule[] { monthlyThirtyFirst }));
            Must(clock.ReadCount - readsBeforeNoteSave == 1,
                "便签日历保存没有对验证和 cursor 重建复用同一 commit 时刻。");
            Must(
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(
                    monthlyThirtyFirst.Id,
                    out var monthlyState) &&
                monthlyState.HandledThroughUtc ==
                new DateTimeOffset(2032, 3, 31, 1, 0, 0, TimeSpan.Zero) &&
                monthlyState.LastDeliveredOccurrenceKey is null &&
                monthlyState.LastDispositionOccurrenceKey is null &&
                monthlyState.ActiveSnooze is null,
                "新建月 31 日规则没有只把最近合法历史 occurrence 写成 cursor。");
            var note = workspace.Snapshot.Settings.Notes.Single();

            var futureCountdownRule = new CalendarReminderRule(
                "calendar-commit-countdown",
                CalendarFrequency.Daily,
                1,
                new DateOnly(2032, 4, 16),
                new TimeOnly(9, 0),
                new DailyCalendarSelector());
            var readsBeforeFutureSave = clock.ReadCount;
            MustApply(workspace, new SaveCountdownChange(
                null,
                "未来日历规则",
                commitNowUtc.AddYears(1),
                Array.Empty<string>(),
                new ReminderRule[] { futureCountdownRule }));
            Must(
                clock.ReadCount - readsBeforeFutureSave == 1 &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(futureCountdownRule.Id),
                "尚无历史 occurrence 的未来日历规则错误创建了 state，或读取了多次 commit 时钟。");
            var countdown = workspace.Snapshot.Settings.Countdowns.Single();

            var phasedWeekly = new CalendarReminderRule(
                futureCountdownRule.Id,
                CalendarFrequency.Weekly,
                2,
                new DateOnly(2032, 3, 1),
                new TimeOnly(9, 0),
                new WeeklyCalendarSelector([DayOfWeek.Monday]));
            var readsBeforeWeeklyEdit = clock.ReadCount;
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                countdown.TargetUtc,
                countdown.TagIds,
                new ReminderRule[] { phasedWeekly }));
            Must(clock.ReadCount - readsBeforeWeeklyEdit == 1,
                "倒计时日历改期没有复用单一 commit 时刻。");
            Must(
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(phasedWeekly.Id, out var weeklyState) &&
                weeklyState.HandledThroughUtc ==
                new DateTimeOffset(2032, 4, 12, 1, 0, 0, TimeSpan.Zero) &&
                weeklyState.LastDeliveredOccurrenceKey is null &&
                weeklyState.LastDispositionOccurrenceKey is null &&
                weeklyState.ActiveSnooze is null &&
                workspace.GetDueReminderOccurrences(commitNowUtc).All(occurrence =>
                    !string.Equals(
                        occurrence.Rule.Id,
                        phasedWeekly.Id,
                        StringComparison.OrdinalIgnoreCase)),
                "双周 phase 改期没有只推进到 commit 时刻，或仍补发历史 occurrence。");

            var weeklyNextUtc = new DateTimeOffset(2032, 4, 26, 1, 0, 0, TimeSpan.Zero);
            clock.UtcNow = weeklyNextUtc;
            coordinator.Tick(clock.UtcNow);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("改期后的下一次未来 occurrence 未进入提醒中心。");
            WindowValidationInput.Pump(center.Dispatcher);
            var weeklyKey = new ActiveReminderOccurrenceKey(phasedWeekly.Id, weeklyNextUtc);
            Must(
                coordinator.ActiveEntryCount == 1 &&
                center.IsAcknowledgeEnabled(weeklyKey),
                "双周规则推进 cursor 后没有正常送达下一次未来 occurrence。");

            var changedDaily = new CalendarReminderRule(
                phasedWeekly.Id,
                CalendarFrequency.Daily,
                1,
                new DateOnly(2032, 4, 1),
                new TimeOnly(8, 0),
                new DailyCalendarSelector());
            var beforeFailure = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var diskBeforeFailure = File.ReadAllText(settingsPath);
            var stateBeforeFailure = workspace.Snapshot.Settings.ReminderStates[phasedWeekly.Id];
            Directory.CreateDirectory(settingsPath + ".tmp");
            var readsBeforeFailedEdit = clock.ReadCount;
            var failedEdit = workspace.Apply(new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                countdown.TargetUtc,
                countdown.TagIds,
                new ReminderRule[] { changedDaily }));
            Must(
                !failedEdit.Succeeded &&
                clock.ReadCount - readsBeforeFailedEdit == 1 &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeFailure &&
                File.ReadAllText(settingsPath) == diskBeforeFailure &&
                workspace.Snapshot.Settings.ReminderStates[phasedWeekly.Id] == stateBeforeFailure &&
                ReminderRuleSchedule.AreEquivalent(
                    workspace.Snapshot.Settings.Countdowns.Single().ReminderRules.Single(),
                    phasedWeekly) &&
                coordinator.ActiveEntryCount == 1 &&
                center.IsAcknowledgeEnabled(weeklyKey),
                "日历改期写盘失败没有原子保留规则、state、磁盘和活动中心。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            var readsBeforeSuccessfulEdit = clock.ReadCount;
            MustApply(workspace, new SaveCountdownChange(
                countdown.Id,
                countdown.Name,
                countdown.TargetUtc,
                countdown.TagIds,
                new ReminderRule[] { changedDaily }));
            Must(clock.ReadCount - readsBeforeSuccessfulEdit == 2,
                "活动中心观察之外，写盘恢复后的日历改期重复读取了 commit 时钟。");
            Must(
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(changedDaily.Id, out var changedState) &&
                changedState.HandledThroughUtc ==
                new DateTimeOffset(2032, 4, 26, 0, 0, 0, TimeSpan.Zero) &&
                changedState.LastDeliveredOccurrenceKey is null &&
                changedState.LastDispositionOccurrenceKey is null &&
                changedState.ActiveSnooze is null &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0,
                "成功改期没有清理旧 center/delivery 状态并只建立新 schedule cursor。");

            var nextDailyUtc = new DateTimeOffset(2032, 4, 27, 0, 0, 0, TimeSpan.Zero);
            Must(
                workspace.GetDueReminderOccurrences(nextDailyUtc).SingleOrDefault(occurrence =>
                    string.Equals(occurrence.Rule.Id, changedDaily.Id, StringComparison.OrdinalIgnoreCase))
                is { ScheduledUtc: var scheduledUtc } &&
                scheduledUtc == nextDailyUtc,
                "日历改期 cursor 压掉了下一次未来 occurrence。");

            var noteDueUtc = new DateTimeOffset(2032, 5, 31, 1, 0, 0, TimeSpan.Zero);
            clock.UtcNow = noteDueUtc;
            Must(workspace.ConfirmReminderDisplayed(
                    note,
                    monthlyThirtyFirst.Id,
                    noteDueUtc).Succeeded,
                "月 31 日规则无法建立送达状态。");
            note = workspace.Snapshot.Settings.Notes.Single();
            Must(workspace.SnoozeReminder(
                    note,
                    monthlyThirtyFirst.Id,
                    noteDueUtc,
                    ReminderSnoozeOption.TenMinutes,
                    null).Succeeded &&
                workspace.Snapshot.Settings.ReminderStates[monthlyThirtyFirst.Id].ActiveSnooze is not null,
                "月 31 日规则无法建立 active snooze。");
            var lastDayRule = monthlyThirtyFirst with
            {
                Selector = new MonthlyCalendarSelector(null, true)
            };
            var readsBeforeNoteEdit = clock.ReadCount;
            MustApply(workspace, new SaveNoteChange(
                note.Id,
                note.Text,
                new DateTime(2032, 4, 15),
                false,
                note.TagIds,
                new ReminderRule[] { lastDayRule }));
            Must(clock.ReadCount - readsBeforeNoteEdit == 1,
                "带 snooze 的便签日历改期没有复用单一 commit 时刻。");
            Must(
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(lastDayRule.Id, out var noteChangedState) &&
                noteChangedState.HandledThroughUtc == noteDueUtc &&
                noteChangedState.LastDeliveredOccurrenceKey is null &&
                noteChangedState.LastDispositionOccurrenceKey is null &&
                noteChangedState.ActiveSnooze is null &&
                workspace.GetDueReminderOccurrences(noteDueUtc).All(occurrence =>
                    !string.Equals(occurrence.Rule.Id, lastDayRule.Id, StringComparison.OrdinalIgnoreCase)),
                "便签日历改期没有清理旧 delivery/disposition/snooze，或补发了历史 occurrence。");
            return true;
        }
        finally
        {
            coordinator.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteFileIfPresent(diagnosticPath);
            DeleteFileIfPresent(diagnosticPath + ".1");
            DeleteFileIfPresent(diagnosticPath + ".2");
            DeleteFileIfPresent(diagnosticPath + ".3");
        }
    }

    private static bool RunCalendarWeeklyRuleEditStateBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-calendar-weekly-edit-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var beforeFirstUtc = new DateTimeOffset(2032, 3, 15, 1, 0, 0, TimeSpan.Zero);
        var firstDueUtc = beforeFirstUtc.AddMinutes(5);
        var secondDueUtc = firstDueUtc.AddDays(7);
        var clock = new AdvancingValidationClock(beforeFirstUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        var localDue = ChinaStandardTime.ToBeijingTime(firstDueUtc);
        var rule = new CalendarReminderRule(
            "calendar-weekly-edit",
            CalendarFrequency.Weekly,
            1,
            DateOnly.FromDateTime(localDue.Date),
            TimeOnly.FromDateTime(localDue.DateTime),
            new WeeklyCalendarSelector([localDue.DayOfWeek]));
        MustApply(workspace, new SaveNoteChange(
            null,
            "周规则状态",
            localDue.Date,
            false,
            Array.Empty<string>(),
            new ReminderRule[] { rule }));
        var note = workspace.Snapshot.Settings.Notes.Single();
        var logger = new DiagnosticLogger(diagnosticPath);
        using var coordinator = new ReminderDeliveryCoordinator(workspace, logger, (_, _) => { });
        try
        {
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(clock.UtcNow);
            var firstCenter = coordinator.ActiveWindow ??
                              throw new InvalidOperationException("周规则首次 occurrence 未进入提醒中心。");
            WindowValidationInput.Pump(firstCenter.Dispatcher);
            var firstKey = new ActiveReminderOccurrenceKey(rule.Id, firstDueUtc);
            Must(firstCenter.RequestAcknowledgement(firstKey),
                "周规则首次 occurrence 无法处置。");
            WindowValidationInput.Pump(firstCenter.Dispatcher);
            var firstOccurrenceKey = ReminderOccurrenceKey.Create(rule, firstDueUtc).ToString();
            Must(
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(rule.Id, out var firstState) &&
                firstState.HandledThroughUtc == firstDueUtc &&
                string.Equals(
                    firstState.LastDispositionOccurrenceKey,
                    firstOccurrenceKey,
                    StringComparison.Ordinal),
                "周规则首次 occurrence 未写入 cursor 和处置历史。");

            clock.UtcNow = secondDueUtc;
            coordinator.Tick(clock.UtcNow);
            var secondCenter = coordinator.ActiveWindow ??
                               throw new InvalidOperationException("周规则下一 occurrence 未进入提醒中心。");
            WindowValidationInput.Pump(secondCenter.Dispatcher);
            var secondKey = new ActiveReminderOccurrenceKey(rule.Id, secondDueUtc);
            Must(
                coordinator.ActiveEntryCount == 1 &&
                secondCenter.IsAcknowledgeEnabled(secondKey),
                "周规则下一 occurrence 未保持活动状态。");

            var equivalentRule = rule with
            {
                Selector = new WeeklyCalendarSelector(rule.Selector is WeeklyCalendarSelector weekly
                    ? weekly.Weekdays.ToArray()
                    : throw new InvalidOperationException("周规则 selector 类型漂移。"))
            };
            MustApply(workspace, new SaveNoteChange(
                note.Id,
                note.Text + "（已编辑）",
                localDue.Date,
                false,
                note.TagIds,
                new ReminderRule[] { equivalentRule }));

            Must(
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(rule.Id, out var savedState) &&
                savedState.HandledThroughUtc == secondDueUtc &&
                string.Equals(
                    savedState.LastDispositionOccurrenceKey,
                    firstOccurrenceKey,
                    StringComparison.Ordinal),
                "周规则原样保存清除了 cursor 或既有处置历史。");
            Must(
                coordinator.ActiveEntryCount == 1 &&
                secondCenter.IsAcknowledgeEnabled(secondKey),
                "无关字段保存移除了周规则的活动提醒中心条目。");
            Must(secondCenter.RequestAcknowledgement(secondKey),
                "原样保存后的周规则 occurrence 无法继续处置。");
            WindowValidationInput.Pump(secondCenter.Dispatcher);
            Must(
                workspace.GetDueReminderOccurrences(clock.UtcNow).All(occurrence =>
                    !string.Equals(occurrence.Rule.Id, rule.Id, StringComparison.OrdinalIgnoreCase)),
                "周规则原样保存后复活了历史 occurrence。");
            return true;
        }
        finally
        {
            coordinator.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteFileIfPresent(diagnosticPath);
            DeleteFileIfPresent(diagnosticPath + ".1");
            DeleteFileIfPresent(diagnosticPath + ".2");
            DeleteFileIfPresent(diagnosticPath + ".3");
        }
    }

    private static bool RunCalendarWorkspaceWpfBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-calendar-workspace-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2032, 3, 15, 1, 0, 0, TimeSpan.Zero);
        var dueUtc = nowUtc.AddMinutes(5);
        var clock = new AdvancingValidationClock(nowUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        var localDue = ChinaStandardTime.ToBeijingTime(dueUtc);
        var noteRule = new CalendarReminderRule(
            "calendar-wpf-note",
            CalendarFrequency.Daily,
            1,
            DateOnly.FromDateTime(localDue.Date),
            TimeOnly.FromDateTime(localDue.DateTime),
            new DailyCalendarSelector());
        var countdownRule = new CalendarReminderRule(
            "calendar-wpf-countdown",
            CalendarFrequency.Daily,
            1,
            DateOnly.FromDateTime(localDue.Date),
            TimeOnly.FromDateTime(localDue.DateTime),
            new DailyCalendarSelector());
        MustApply(workspace, new SaveNoteChange(
            null,
            "真实 WPF 日历便签",
            localDue.Date,
            false,
            Array.Empty<string>(),
            new ReminderRule[] { noteRule }));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "真实 WPF 日历倒计时",
            dueUtc.AddDays(3),
            Array.Empty<string>(),
            new ReminderRule[] { countdownRule }));

        var note = workspace.Snapshot.Settings.Notes.Single();
        var countdown = workspace.Snapshot.Settings.Countdowns.Single();
        using var holidayCalendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, holidayCalendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        var floating = new FloatingWindow(
            workspace,
            holidayCalendar,
            main,
            AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        using var coordinator = new ReminderDeliveryCoordinator(workspace, logger, (_, _) => { });
        try
        {
            main.Show();
            floating.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(VisualDescendants<TextBlock>(main).Any(text =>
                    string.Equals(text.Text, note.Text, StringComparison.Ordinal)),
                "日历便签未在主窗真实渲染。");
            Must(VisualDescendants<TextBlock>(floating).Any(text =>
                    string.Equals(text.Text, note.Text, StringComparison.Ordinal)),
                "日历便签未在悬浮窗真实渲染。");

            var edit = VisualDescendants<Button>(main).Single(button =>
                string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                string.Equals(button.Tag as string, note.Id, StringComparison.OrdinalIgnoreCase));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                string.Equals(
                    ((TextBox)main.FindName("NoteText")).Text,
                    note.Text,
                    StringComparison.Ordinal) &&
                ((Button)main.FindName("CancelNoteEdit")).Visibility == Visibility.Visible &&
                string.Equals(
                    ((TextBlock)main.FindName("NoteReminderRulesSummary")).Text,
                    "已配置 1 条提醒规则",
                    StringComparison.Ordinal),
                "日历便签无法进入共享规则编辑上下文或摘要丢失。");

            var beforeLegacySave = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var rejectedLegacySave = workspace.Apply(new SaveNoteChange(
                note.Id,
                note.Text,
                localDue.Date,
                false,
                note.TagIds,
                NoReminder.Instance));
            Must(
                !rejectedLegacySave.Succeeded &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeLegacySave,
                "兼容提醒编辑器静默覆盖了日历规则。");

            clock.UtcNow = dueUtc;
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(main.Dispatcher);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("日历提醒中心未创建。");
            var noteKey = new ActiveReminderOccurrenceKey(noteRule.Id, dueUtc);
            var countdownKey = new ActiveReminderOccurrenceKey(countdownRule.Id, dueUtc);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(noteKey) &&
                center.IsSnoozeEnabled(countdownKey) &&
                VisualDescendants<TextBlock>(center).Any(text =>
                    string.Equals(text.Text, note.Text, StringComparison.Ordinal)) &&
                VisualDescendants<TextBlock>(center).Any(text =>
                    string.Equals(text.Text, countdown.Name, StringComparison.Ordinal)),
                "便签与倒计时日历 occurrence 未同批进入统一提醒中心。");
            Must(center.RequestAcknowledgement(noteKey),
                "便签日历 occurrence 未复用知道了语义。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(center.RequestSnooze(countdownKey, ReminderSnoozeOption.TenMinutes, null),
                "倒计时日历 occurrence 未复用稍后语义。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                workspace.Snapshot.Settings.ReminderStates[noteRule.Id]
                    .LastDispositionOccurrenceKey is not null &&
                workspace.Snapshot.Settings.ReminderStates[countdownRule.Id]
                    .ActiveSnooze is not null,
                "日历 occurrence 处置或稍后状态未持久化。");

            MustApply(workspace, new SetNoteCompletionChange(note.Id, true));
            MustApply(workspace, new SetNoteCompletionChange(note.Id, false));
            Must(
                workspace.GetDueReminderOccurrences(clock.UtcNow).All(occurrence =>
                    !string.Equals(occurrence.Rule.Id, noteRule.Id, StringComparison.OrdinalIgnoreCase)),
                "日历便签完成并恢复后重放了已推进的 occurrence。");
            return true;
        }
        finally
        {
            coordinator.Dispose();
            if (floating.IsVisible)
            {
                floating.Close();
            }

            if (main.IsVisible)
            {
                main.Close();
            }

            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteFileIfPresent(diagnosticPath);
            DeleteFileIfPresent(diagnosticPath + ".1");
            DeleteFileIfPresent(diagnosticPath + ".2");
            DeleteFileIfPresent(diagnosticPath + ".3");
        }
    }

    public static bool RunDesktopWorkspaceInterfaceBehavior()
    {
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 3, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-workspace-{Guid.NewGuid():N}.settings.json");
        try
        {
            var clock = new WorkspaceValidationClock(nowUtc);
            var store = new SettingsStore(clock: clock);
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            var stateChangedCount = 0;
            object? stateChangedSender = null;
            workspace.StateChanged += (eventSender, _) =>
            {
                stateChangedCount++;
                stateChangedSender = eventSender;
            };

            var future = workspace.Apply(new SaveCountdownChange(
                null,
                "工作区 interface 未到期倒计时",
                nowUtc.AddDays(2),
                Array.Empty<string>()));
            var expired = workspace.Apply(new SaveCountdownChange(
                null,
                "工作区 interface 已到期倒计时",
                nowUtc.AddDays(-2),
                Array.Empty<string>()));
            var clockReadsBeforeSnapshot = clock.ReadCount;
            var beforeFilter = workspace.Snapshot;
            var snapshotClockReads = clock.ReadCount - clockReadsBeforeSnapshot;
            MustThrow<NotSupportedException>(
                () => ((IList<CountdownRecord>)beforeFilter.Settings.Countdowns).Clear(),
                "DesktopWorkspace Snapshot 允许修改外层倒计时集合。");
            MustThrow<NotSupportedException>(
                () => ((IList<string>)beforeFilter.Settings.Countdowns[0].TagIds).Add("绕过写入"),
                "DesktopWorkspace Snapshot 允许修改嵌套 tagIds 集合。");
            var stateChangedBeforeFilter = stateChangedCount;
            workspace.ApplyFilter(beforeFilter.Filter with
            {
                CountdownStatus = CountdownStatusFilter.Active
            });
            var filterRaisedWorkspaceEvent =
                stateChangedCount == stateChangedBeforeFilter + 1 &&
                ReferenceEquals(stateChangedSender, workspace);
            var filtered = workspace.Snapshot;
            var reloaded = DesktopWorkspace.Load(settingsPath, store).Snapshot;
            var beforePersistenceFailure = SettingsContract.SerializeCanonical(
                workspace.Snapshot.Settings);
            var stateChangedBeforePersistenceFailure = stateChangedCount;
            Directory.CreateDirectory(settingsPath + ".tmp");
            var failedSave = workspace.Apply(new SaveCountdownChange(
                null,
                "不应污染工作区状态的倒计时",
                nowUtc.AddDays(3),
                Array.Empty<string>()));
            var afterPersistenceFailure = workspace.Snapshot;
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            var observerWorkspace = DesktopWorkspace.Load(settingsPath, store);
            observerWorkspace.StateChanged += (_, _) =>
                throw new IOException("observer failure");
            var observerExceptionPropagated = false;
            try
            {
                observerWorkspace.Apply(new SaveCountdownChange(
                    null,
                    "observer 异常后仍已落盘",
                    nowUtc.AddDays(3),
                    Array.Empty<string>()));
            }
            catch (IOException exception) when (exception.Message == "observer failure")
            {
                observerExceptionPropagated = true;
            }

            var afterObserverFailure = DesktopWorkspace.Load(settingsPath, store).Snapshot;
            var observerSaveCount = afterObserverFailure.Settings.Countdowns.Count(record =>
                record.Name == "observer 异常后仍已落盘");

            var reminderWorkspace = DesktopWorkspace.Load(settingsPath, store);
            var onceDueUtc = nowUtc.AddMinutes(1);
            var onceDeliveryUtc = nowUtc.AddMinutes(2);
            var onceSave = reminderWorkspace.Apply(new SaveNoteChange(
                null,
                "工作区一次性提醒事务",
                new DateTime(2034, 5, 6),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateOnce(onceDueUtc)));
            var savedOnce = reminderWorkspace.Snapshot.Settings.Notes.Single(note =>
                note.Text == "工作区一次性提醒事务");
            var stableRuleId = savedOnce.ReminderRules.Single().Id;
            var editPreservingRuleId = reminderWorkspace.Apply(new SaveNoteChange(
                savedOnce.Id,
                "工作区一次性提醒事务（已编辑正文）",
                new DateTime(2034, 5, 6),
                false,
                Array.Empty<string>(),
                ReminderRuleCompatibility.ToLegacy(reminderWorkspace.Snapshot.Settings, savedOnce)));
            var editedOnce = reminderWorkspace.Snapshot.Settings.Notes.Single(note =>
                note.Id == savedOnce.Id);
            var inPlaceEditPreservedRuleId =
                editPreservingRuleId.Succeeded &&
                editedOnce.ReminderRules.Single().Id == stableRuleId;
            var dueReminder = reminderWorkspace.GetDueReminders(onceDeliveryUtc).Single(note =>
                note.Id == savedOnce.Id);
            var reminderConfirmation = reminderWorkspace.ConfirmReminderDisplayed(
                dueReminder,
                onceDeliveryUtc);
            var afterReminderConfirmation = DesktopWorkspace.Load(settingsPath, store);
            var deliveredNote = afterReminderConfirmation.Snapshot.Settings.Notes
                .Single(note => note.Id == savedOnce.Id);
            var deliveredReminder = ReminderRuleCompatibility.ToLegacy(
                afterReminderConfirmation.Snapshot.Settings,
                deliveredNote) as OnceReminder;
            var deliveredState = afterReminderConfirmation.Snapshot.Settings.ReminderStates[stableRuleId];

            var multiOnce = new AbsoluteOnceReminderRule("multi-once", nowUtc.AddHours(1));
            var multiRepeat = new FixedIntervalReminderRule(
                "multi-repeat",
                nowUtc.AddHours(2),
                new ReminderInterval(30));
            var multiOriginalTag = new TagRecord("multi-original-tag", "多规则原标签", null);
            var multiEditedTag = new TagRecord("multi-edited-tag", "多规则新标签", null);
            var multiNote = new NoteRecord(
                "multi-note",
                "多规则便签",
                "2034-05-06",
                false,
                new[] { multiOriginalTag.Id },
                new ReminderRule[] { multiOnce, multiRepeat });
            var multiStates = afterReminderConfirmation.Snapshot.Settings.ReminderStates.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            multiStates[multiOnce.Id] = new ReminderRuleState(
                multiOnce.Id,
                multiOnce.ScheduledUtc,
                MinminCountdown.Core.ReminderOccurrenceKey.Create(
                    multiOnce,
                    multiOnce.ScheduledUtc).ToString());
            multiStates[multiRepeat.Id] = new ReminderRuleState(
                multiRepeat.Id,
                multiRepeat.FirstDueUtc,
                null);
            store.Save(
                settingsPath,
                afterReminderConfirmation.Snapshot.Settings with
                {
                    Notes = afterReminderConfirmation.Snapshot.Settings.Notes.Append(multiNote).ToArray(),
                    Tags = afterReminderConfirmation.Snapshot.Settings.Tags
                        .Append(multiOriginalTag)
                        .Append(multiEditedTag)
                        .ToArray(),
                    ReminderStates = multiStates
                });
            var multiWorkspace = DesktopWorkspace.Load(settingsPath, store);
            var multiEdit = multiWorkspace.Apply(new SaveNoteChange(
                multiNote.Id,
                "多规则便签（仅正文编辑）",
                new DateTime(2034, 5, 7),
                false,
                new[] { multiEditedTag.Id },
                multiNote.ReminderRules));
            var multiAfterEdit = multiWorkspace.Snapshot.Settings;
            var multiComplete = multiWorkspace.Apply(new SetNoteCompletionChange(multiNote.Id, true));
            var multiReopen = multiWorkspace.Apply(new SetNoteCompletionChange(multiNote.Id, false));
            var multiAfterCompletion = multiWorkspace.Snapshot.Settings;
            var multiBeforeRejectedReminderEdit = SettingsContract.SerializeCanonical(multiAfterCompletion);
            var multiReminderEdit = multiWorkspace.Apply(new SaveNoteChange(
                multiNote.Id,
                "不得压掉隐藏规则",
                new DateTime(2034, 5, 7),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateRepeating(nowUtc.AddHours(3), 45)));
            var multiAfterRejectedReminderEdit = multiWorkspace.Snapshot.Settings;
            var removeOneRule = multiWorkspace.Apply(new SaveNoteChange(
                multiNote.Id,
                "多规则便签（删除一次规则）",
                new DateTime(2034, 5, 7),
                false,
                new[] { multiEditedTag.Id },
                new ReminderRule[] { multiRepeat }));
            var multiAfterDelete = multiWorkspace.Snapshot.Settings;
            var failedCandidateRules = new ReminderRule[]
            {
                multiRepeat,
                new AbsoluteOnceReminderRule("multi-failed-rule", nowUtc.AddHours(4))
            };
            var beforeRuleSaveFailure = SettingsContract.SerializeCanonical(multiAfterDelete);
            var diskBeforeRuleSaveFailure = File.ReadAllText(settingsPath);
            Directory.CreateDirectory(settingsPath + ".tmp");
            var failedRuleSave = multiWorkspace.Apply(new SaveNoteChange(
                multiNote.Id,
                "不应提交的规则集合",
                new DateTime(2034, 5, 8),
                false,
                Array.Empty<string>(),
                failedCandidateRules));
            var multiAfterRuleSaveFailure = multiWorkspace.Snapshot.Settings;
            var diskAfterRuleSaveFailure = File.ReadAllText(settingsPath);
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            var multiRuleAndStatePreservation =
                multiEdit.Succeeded &&
                multiComplete.Succeeded &&
                multiReopen.Succeeded &&
                !multiReminderEdit.Succeeded &&
                removeOneRule.Succeeded &&
                !failedRuleSave.Succeeded &&
                !multiAfterCompletion.Notes.Single(note => note.Id == multiNote.Id).IsCompleted &&
                multiAfterEdit.Notes.Single(note => note.Id == multiNote.Id).Text == "多规则便签（仅正文编辑）" &&
                multiAfterEdit.Notes.Single(note => note.Id == multiNote.Id).Date == "2034-05-07" &&
                multiAfterEdit.Notes.Single(note => note.Id == multiNote.Id).TagIds.SequenceEqual(new[] { multiEditedTag.Id }) &&
                multiAfterEdit.Notes.Single(note => note.Id == multiNote.Id).ReminderRules.SequenceEqual(multiNote.ReminderRules) &&
                multiAfterCompletion.Notes.Single(note => note.Id == multiNote.Id).ReminderRules.SequenceEqual(multiNote.ReminderRules) &&
                multiAfterRejectedReminderEdit.Notes.Single(note => note.Id == multiNote.Id).ReminderRules.SequenceEqual(multiNote.ReminderRules) &&
                multiAfterEdit.ReminderStates[multiOnce.Id] == multiStates[multiOnce.Id] &&
                multiAfterEdit.ReminderStates[multiRepeat.Id] == multiStates[multiRepeat.Id] &&
                multiAfterCompletion.ReminderStates[multiOnce.Id] == multiStates[multiOnce.Id] &&
                multiAfterCompletion.ReminderStates[multiRepeat.Id] == multiStates[multiRepeat.Id] &&
                multiAfterRejectedReminderEdit.ReminderStates[multiOnce.Id] == multiStates[multiOnce.Id] &&
                multiAfterRejectedReminderEdit.ReminderStates[multiRepeat.Id] == multiStates[multiRepeat.Id] &&
                multiAfterDelete.Notes.Single(note => note.Id == multiNote.Id)
                    .ReminderRules.SequenceEqual(new ReminderRule[] { multiRepeat }) &&
                !multiAfterDelete.ReminderStates.ContainsKey(multiOnce.Id) &&
                multiAfterDelete.ReminderStates[multiRepeat.Id] == multiStates[multiRepeat.Id] &&
                string.Equals(
                    multiBeforeRejectedReminderEdit,
                    SettingsContract.SerializeCanonical(multiAfterRejectedReminderEdit),
                    StringComparison.Ordinal) &&
                string.Equals(
                    beforeRuleSaveFailure,
                    SettingsContract.SerializeCanonical(multiAfterRuleSaveFailure),
                    StringComparison.Ordinal) &&
                string.Equals(diskBeforeRuleSaveFailure, diskAfterRuleSaveFailure, StringComparison.Ordinal) &&
                failedCandidateRules.Length == 2 &&
                failedCandidateRules[1].Id == "multi-failed-rule";

            var dueRuleA = new AbsoluteOnceReminderRule("multi-due-a", nowUtc.AddMinutes(5));
            var dueRuleB = new AbsoluteOnceReminderRule("multi-due-b", nowUtc.AddMinutes(6));
            var multiDueUtc = nowUtc.AddMinutes(7);
            var dueRuleSave = multiWorkspace.Apply(new SaveNoteChange(
                null,
                "同一便签的两条到期规则",
                new DateTime(2034, 5, 6),
                false,
                Array.Empty<string>(),
                new ReminderRule[] { dueRuleA, dueRuleB }));
            var dueRuleNote = multiWorkspace.Snapshot.Settings.Notes.Single(note =>
                note.Text == "同一便签的两条到期规则");
            var dueOccurrencesBeforeConfirmation = multiWorkspace
                .GetDueReminderOccurrences(multiDueUtc)
                .Where(occurrence => occurrence.Note.Id == dueRuleNote.Id)
                .ToArray();
            var firstOccurrenceConfirmation = multiWorkspace.ConfirmReminderDisplayed(
                dueRuleNote,
                dueRuleA.Id,
                dueRuleA.ScheduledUtc);
            var dueOccurrencesAfterConfirmation = multiWorkspace
                .GetDueReminderOccurrences(multiDueUtc)
                .Where(occurrence => occurrence.Note.Id == dueRuleNote.Id)
                .ToArray();
            var multiRuleOccurrencesIndependent =
                dueRuleSave.Succeeded &&
                dueOccurrencesBeforeConfirmation.Select(occurrence => occurrence.Rule.Id)
                    .SequenceEqual(new[] { dueRuleA.Id, dueRuleB.Id }) &&
                firstOccurrenceConfirmation.Succeeded &&
                dueOccurrencesAfterConfirmation.Select(occurrence => occurrence.Rule.Id)
                    .SequenceEqual(new[] { dueRuleA.Id, dueRuleB.Id }) &&
                dueOccurrencesAfterConfirmation[0].ScheduledUtc == dueRuleA.ScheduledUtc;

            return
                future.Succeeded &&
                expired.Succeeded &&
                !failedSave.Succeeded &&
                snapshotClockReads == 1 &&
                filterRaisedWorkspaceEvent &&
                stateChangedCount == stateChangedBeforePersistenceFailure &&
                beforeFilter.AllCountdowns.Count == 2 &&
                filtered.VisibleCountdowns.Count == 1 &&
                filtered.VisibleCountdowns[0].Name == "工作区 interface 未到期倒计时" &&
                filtered.AppliedFilterCount == 1 &&
                reloaded.AllCountdowns.Count == 2 &&
                reloaded.Settings.Countdowns.Count == 2 &&
                SettingsContract.SerializeCanonical(afterPersistenceFailure.Settings) ==
                    beforePersistenceFailure &&
                afterPersistenceFailure.AllCountdowns.All(record =>
                    record.Name != "不应污染工作区状态的倒计时") &&
                observerExceptionPropagated &&
                observerSaveCount == 1 &&
                onceSave.Succeeded &&
                inPlaceEditPreservedRuleId &&
                multiRuleAndStatePreservation &&
                multiRuleOccurrencesIndependent &&
                reminderConfirmation.Succeeded &&
                deliveredReminder?.Delivery == OnceDeliveryState.Delivered &&
                deliveredState.HandledThroughUtc == onceDueUtc &&
                deliveredState.LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(
                        editedOnce.ReminderRules.Single(),
                        onceDueUtc).ToString() &&
                afterReminderConfirmation.GetDueReminders(nowUtc).Count == 0 &&
                reloaded.UtcNow == nowUtc &&
                reloaded.BeijingNow.Offset == TimeSpan.FromHours(8);
        }
        finally
        {
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
        }
    }

    public static bool RunStartupAcceptanceProtocolInterfaceBehavior()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-startup-protocol-{Guid.NewGuid():N}");
        var startupRequestPath = Path.Combine(temporaryRoot, "startup-request.json");
        var runtimeStatusPath = Path.Combine(temporaryRoot, "runtime-status.json");
        var acceptanceStatusPath = Path.Combine(temporaryRoot, "acceptance-status.json");
        var adapterPath = Path.Combine(temporaryRoot, ApplicationPaths.AdapterFileName);
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            const string launchToken = "validation-startup-token";
            var requestedAtUtc = new DateTimeOffset(2026, 7, 20, 2, 16, 0, TimeSpan.Zero);
            WriteStartupRequest(startupRequestPath, launchToken, "active", requestedAtUtc: requestedAtUtc);
            var ordinary = StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: false);
            Must(
                ordinary.LaunchToken != launchToken &&
                !ordinary.AcceptanceReadOnly &&
                ordinary.Request.AcceptanceState == "none" &&
                ordinary.Request.MigrationUtc is null,
                "普通启动不应导入相邻的验收请求。");
            var active = StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true);
            Must(
                active.LaunchToken == launchToken &&
                active.AcceptanceReadOnly &&
                active.Request.AcceptanceState == "active" &&
                active.Request.MigrationUtc == requestedAtUtc &&
                active.RuntimeStatusPath == runtimeStatusPath &&
                active.AcceptanceStatusPath == acceptanceStatusPath,
                "启动验收 interface 未完整读取 active 请求。");

            var runtimeReceipt = active.WriteWindowReady("Pending");
            Must(
                runtimeReceipt.SchemaVersion == 4 &&
                runtimeReceipt.Stage == "window-ready" &&
                runtimeReceipt.LaunchToken == launchToken &&
                runtimeReceipt.ProcessId == Environment.ProcessId &&
                runtimeReceipt.SessionId == Process.GetCurrentProcess().SessionId &&
                runtimeReceipt.ApplicationPath == Path.GetFullPath(adapterPath) &&
                runtimeReceipt.CalendarState == "Pending" &&
                runtimeReceipt.TimeZoneId == "China Standard Time" &&
                File.Exists(runtimeStatusPath),
                "启动验收 interface 未写出匹配当前进程身份的 window-ready 回执。");
            Must(
                active.ReadAcceptanceState() == "active" &&
                !active.MarkAcceptanceReady() &&
                active.ReadAcceptanceState() == "active",
                "启动验收 interface 未拒绝 active -> ready 非法推进。");

            WriteStartupRequest(startupRequestPath, launchToken, "released");
            var released = StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true);
            Must(
                released.MarkAcceptanceReady() &&
                released.ReadAcceptanceState() == "ready" &&
                !released.MarkAcceptanceReady(),
                "启动验收 interface 未完成 released -> ready 单次推进。");

            WriteStartupRequest(startupRequestPath, launchToken, "finalized");
            var finalized = StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true);
            Must(
                !finalized.AcceptanceReadOnly &&
                finalized.Request.AcceptanceState == "finalized",
                "finalized 启动请求必须解除只读状态。");

            var acceptanceReceipt = released.WriteAcceptanceReady();
            Must(
                acceptanceReceipt.SchemaVersion == 1 &&
                acceptanceReceipt.Stage == "finalized-ready" &&
                acceptanceReceipt.LaunchToken == launchToken &&
                acceptanceReceipt.ProcessId == Environment.ProcessId &&
                acceptanceReceipt.SessionId == Process.GetCurrentProcess().SessionId &&
                acceptanceReceipt.ApplicationPath == Path.GetFullPath(adapterPath) &&
                File.Exists(acceptanceStatusPath),
                "启动验收 interface 未写出匹配当前进程身份的 finalized-ready 回执。");

            WriteStartupRequest(startupRequestPath, "different-token", "released");
            Must(
                released.ReadAcceptanceState() is null &&
                !released.MarkAcceptanceReady(),
                "启动验收 interface 未拒绝已变化的 launch token。");

            WriteStartupRequest(startupRequestPath, launchToken, "unknown-stage");
            MustThrow<InvalidDataException>(
                () => StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true),
                "启动验收 interface 未拒绝未知 acceptanceState。");
            WriteStartupRequest(startupRequestPath, launchToken, "active", acceptanceReadOnly: false);
            MustThrow<InvalidDataException>(
                () => StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true),
                "启动验收 interface 未拒绝 active 可编辑组合。");
            WriteStartupRequest(startupRequestPath, launchToken, "finalized", acceptanceReadOnly: true);
            MustThrow<InvalidDataException>(
                () => StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true),
                "启动验收 interface 未拒绝 finalized 只读组合。");
            WriteStartupRequest(startupRequestPath, string.Empty, "active");
            MustThrow<InvalidDataException>(
                () => StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true),
                "启动验收 interface 未拒绝空 launch token。");
            File.WriteAllText(
                startupRequestPath,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    launchToken,
                    acceptanceReadOnly = true,
                    acceptanceState = "active"
                }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            MustThrow<InvalidDataException>(
                () => StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true),
                "启动验收 interface 未拒绝缺失 requestedAtUtc 的请求。");
            MustThrow<ArgumentOutOfRangeException>(
                () => active.WriteWindowReady("Broken"),
                "启动验收 interface 未拒绝未知日历回执状态。");

            return true;
        }
        finally
        {
            DeleteFileIfPresent(acceptanceStatusPath);
            DeleteFileIfPresent(runtimeStatusPath);
            DeleteFileIfPresent(startupRequestPath);
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot);
            }
        }
    }

    public static bool RunHolidayCalendarInterfaceBehavior()
    {
        const int year = 2034;
        const int nextYear = year + 1;
        var readyRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-holiday-ready-{Guid.NewGuid():N}");
        var unavailableRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-holiday-unavailable-{Guid.NewGuid():N}");
        var readyCacheDirectory = Path.Combine(readyRoot, "holiday-cache");
        var unavailableCacheDirectory = Path.Combine(unavailableRoot, "holiday-cache");
        var readyCachePath = Path.Combine(readyCacheDirectory, $"{year}.json");
        var nextYearCachePath = Path.Combine(readyCacheDirectory, $"{nextYear}.json");
        var unavailableCachePath = Path.Combine(unavailableCacheDirectory, $"{year}.json");
        var payload = CreateHolidayCalendarPayload(year);
        var nextYearPayload = CreateHolidayCalendarPayload(nextYear);

        Directory.CreateDirectory(readyCacheDirectory);
        Directory.CreateDirectory(unavailableCacheDirectory);
        File.WriteAllText(
            readyCachePath,
            payload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(
            nextYearCachePath,
            nextYearPayload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(
            unavailableCachePath,
            payload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            using (var calendar = new HolidayCalendar(readyRoot))
            {
                var stateChangedCount = 0;
                calendar.StateChanged += (_, _) => stateChangedCount++;

                var preferences = new CountdownPreferences(
                    "Second",
                    6200m,
                    TimeSpan.FromHours(9),
                    TimeSpan.FromHours(18));
                var workdayNowUtc = new DateTimeOffset(2034, 5, 6, 5, 30, 0, TimeSpan.Zero);
                var nonWorkdayNowUtc = new DateTimeOffset(2034, 5, 8, 5, 30, 0, TimeSpan.Zero);
                var initialRefresh = calendar
                    .RefreshAsync(workdayNowUtc, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                var workdayCountdown = calendar.GetWorkdayEndCountdown(workdayNowUtc, preferences);

                Must(
                    calendar.State == "Ready" &&
                    calendar.StatusText == "已同步 2034 年法定节假日与调休" &&
                    initialRefresh &&
                    stateChangedCount == 1,
                    "工作日历 interface 未从本地缓存进入 Ready 状态。");
                Must(
                    workdayCountdown is
                    {
                        Id: "__workday_end__",
                        Name: "距离下班",
                        TargetUtc: var targetUtc
                    } &&
                    targetUtc == new DateTimeOffset(2034, 5, 6, 10, 0, 0, TimeSpan.Zero) &&
                    workdayCountdown.ReminderRules.Count == 0 &&
                    calendar.GetEarnings(workdayNowUtc, preferences) == 1550m,
                    "工作日历 interface 未按北京时间和当月工作日派生下班倒计时与今日收入。");
                Must(
                    calendar.GetWorkdayEndCountdown(nonWorkdayNowUtc, preferences) is null &&
                    calendar.GetEarnings(nonWorkdayNowUtc, preferences) == 0m,
                    "工作日历 interface 未在非工作日停止派生结果。");
                var unsetSalaryPreferences = preferences with { MonthlySalary = 0m };
                var negativeSalaryPreferences = preferences with { MonthlySalary = -1m };
                Must(
                    calendar.GetEarnings(workdayNowUtc, unsetSalaryPreferences) is null &&
                    calendar.GetEarnings(nonWorkdayNowUtc, unsetSalaryPreferences) is null &&
                    calendar.GetEarnings(workdayNowUtc, negativeSalaryPreferences) is null,
                    "工作日历 interface 在月薪未输入或为负时必须停止派生今日收入。");

                var sameYearRefresh = calendar
                    .RefreshAsync(nonWorkdayNowUtc, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                var nextYearNowUtc = new DateTimeOffset(2035, 5, 6, 5, 30, 0, TimeSpan.Zero);
                var nextYearRefresh = calendar
                    .RefreshAsync(nextYearNowUtc, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                var nextYearCountdown = calendar.GetWorkdayEndCountdown(nextYearNowUtc, preferences);
                Must(
                    !sameYearRefresh &&
                    nextYearRefresh &&
                    calendar.State == "Ready" &&
                    calendar.StatusText == "已同步 2035 年法定节假日与调休" &&
                    stateChangedCount == 2 &&
                    nextYearCountdown is { TargetUtc: var nextYearTargetUtc } &&
                    nextYearTargetUtc == new DateTimeOffset(2035, 5, 6, 10, 0, 0, TimeSpan.Zero) &&
                    calendar.GetEarnings(nextYearNowUtc, preferences) == 1550m,
                    "工作日历 interface 未在北京时间跨年后刷新年份与派生结果。");
            }

            using (var calendar = new HolidayCalendar(
                       unavailableRoot,
                       (_, _) => Task.FromException<string>(new HttpRequestException("synthetic offline"))))
            {
                var stateChangedCount = 0;
                calendar.StateChanged += (_, _) => stateChangedCount++;
                var preferences = new CountdownPreferences(
                    "Second",
                    6200m,
                    TimeSpan.FromHours(9),
                    TimeSpan.FromHours(18));
                var nowUtc = new DateTimeOffset(2034, 5, 6, 5, 30, 0, TimeSpan.Zero);
                bool failedRefresh;
                bool immediateRetry;
                using (var cacheLock = new FileStream(
                           unavailableCachePath,
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                    failedRefresh = calendar
                        .RefreshAsync(nowUtc, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    immediateRetry = calendar
                        .RefreshAsync(nowUtc.AddMilliseconds(250), CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                Must(
                    failedRefresh &&
                    !immediateRetry &&
                    calendar.State == "Unavailable" &&
                    calendar.StatusText == "节假日日历未就绪" &&
                    stateChangedCount == 2 &&
                    calendar.GetWorkdayEndCountdown(nowUtc, preferences) is null &&
                    calendar.GetEarnings(nowUtc, preferences) is null,
                    "工作日历 interface 未把本地缓存读取失败收敛为 Unavailable 状态。");

                var recoveredRefresh = calendar
                    .RefreshAsync(nowUtc.AddMinutes(1), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Must(
                    recoveredRefresh &&
                    calendar.State == "Ready" &&
                    calendar.StatusText == "已同步 2034 年法定节假日与调休" &&
                    stateChangedCount == 3 &&
                    calendar.GetWorkdayEndCountdown(nowUtc, preferences) is not null &&
                    calendar.GetEarnings(nowUtc, preferences) == 1550m,
                    "工作日历 interface 未在退避后恢复同年失败的刷新。");
            }

            Must(
                !HolidayCalendar.TryParse(payload, nextYear, out _),
                "工作日历 interface 接受了与请求年份不一致的缓存。");

            Must(
                RunHolidayCalendarOfflineFallbackBehavior(),
                "工作日历 interface 未覆盖本地缓存、联网结果和随包年度数据的优先级场景。");

            using (var calendar = new HolidayCalendar(Path.Combine(
                       Path.GetTempPath(),
                       $"MinminCountdown-holiday-cancelled-{Guid.NewGuid():N}")))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var stateChangedCount = 0;
                calendar.StateChanged += (_, _) => stateChangedCount++;
                var refreshed = calendar
                    .RefreshAsync(
                        new DateTimeOffset(2036, 5, 6, 5, 30, 0, TimeSpan.Zero),
                        cancellation.Token)
                    .GetAwaiter()
                    .GetResult();
                Must(
                    refreshed &&
                    calendar.State == "Unavailable" &&
                    calendar.StatusText == "节假日日历未就绪" &&
                    stateChangedCount == 2,
                    "工作日历 interface 未把取消的年份刷新收敛为 Unavailable 状态。");
            }

            return true;
        }
        finally
        {
            DeleteFileIfPresent(readyCachePath);
            DeleteFileIfPresent(nextYearCachePath);
            DeleteFileIfPresent(unavailableCachePath);
            DeleteDirectoryIfEmpty(readyCacheDirectory);
            DeleteDirectoryIfEmpty(unavailableCacheDirectory);
            DeleteDirectoryIfEmpty(readyRoot);
            DeleteDirectoryIfEmpty(unavailableRoot);
        }
    }

    private static bool RunHolidayCalendarOfflineFallbackBehavior()
    {
        const int packagedYear = 2037;
        const int unsupportedYear = packagedYear + 1;
        var packagedRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-holiday-packaged-{Guid.NewGuid():N}");
        var conflictRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-holiday-conflict-{Guid.NewGuid():N}");
        var missingRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-holiday-missing-{Guid.NewGuid():N}");
        var packagedDirectory = Path.Combine(packagedRoot, "holiday-data");
        var conflictDirectory = Path.Combine(conflictRoot, "holiday-data");
        var preferences = new CountdownPreferences(
            "Second",
            6200m,
            TimeSpan.FromHours(9),
            TimeSpan.FromHours(18));
        var nowUtc = new DateTimeOffset(packagedYear, 5, 6, 5, 30, 0, TimeSpan.Zero);
        var packagedPayload = CreateHolidayCalendarPayload(packagedYear, includeMaySixthWorkday: true);
        var networkPayload = CreateHolidayCalendarPayload(packagedYear, includeMaySixthWorkday: false);

        Directory.CreateDirectory(packagedDirectory);
        Directory.CreateDirectory(conflictDirectory);
        File.WriteAllText(
            Path.Combine(packagedDirectory, $"{packagedYear}.json"),
            packagedPayload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(
            Path.Combine(conflictDirectory, $"{packagedYear}.json"),
            packagedPayload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        static Task<string> OfflineDownload(int _, CancellationToken __) =>
            Task.FromException<string>(new HttpRequestException("synthetic offline"));

        try
        {
            // Scenario 1: no mutable cache, offline network, valid packaged year.
            using (var packagedCalendar = new HolidayCalendar(packagedRoot, OfflineDownload))
            {
                var refreshed = packagedCalendar
                    .RefreshAsync(nowUtc, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Must(
                    refreshed &&
                    packagedCalendar.State == "Ready" &&
                    packagedCalendar.StatusText == "已加载 2037 年随包法定节假日与调休" &&
                    packagedCalendar.GetWorkdayEndCountdown(nowUtc, preferences) is not null &&
                    packagedCalendar.GetEarnings(nowUtc, preferences) == 1550m,
                    "随包年度数据在离线网络下未提供有效的工作日历结果。");
            }

            // Scenario 2: packaged data conflicts with a valid network response;
            // the network result must win and be cached for subsequent launches.
            using (var conflictCalendar = new HolidayCalendar(
                       conflictRoot,
                       (_, _) => Task.FromResult(networkPayload)))
            {
                var refreshed = conflictCalendar
                    .RefreshAsync(nowUtc, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Must(
                    refreshed &&
                    conflictCalendar.State == "Ready" &&
                    conflictCalendar.StatusText == "已同步 2037 年法定节假日与调休" &&
                    conflictCalendar.GetWorkdayEndCountdown(nowUtc, preferences) is null &&
                    conflictCalendar.GetEarnings(nowUtc, preferences) == 0m &&
                    File.Exists(Path.Combine(conflictRoot, "holiday-cache", $"{packagedYear}.json")),
                    "联网结果未覆盖冲突的随包数据或未写入本地缓存。");
            }

            // Scenario 3: neither network nor package covers a year; no weekday
            // estimate is allowed to leak into the UI.
            using (var missingCalendar = new HolidayCalendar(missingRoot, OfflineDownload))
            {
                var unsupportedNowUtc = new DateTimeOffset(unsupportedYear, 5, 6, 5, 30, 0, TimeSpan.Zero);
                var refreshed = missingCalendar
                    .RefreshAsync(unsupportedNowUtc, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Must(
                    refreshed &&
                    missingCalendar.State == "Unavailable" &&
                    missingCalendar.StatusText == "节假日日历未就绪" &&
                    missingCalendar.GetWorkdayEndCountdown(unsupportedNowUtc, preferences) is null &&
                    missingCalendar.GetEarnings(unsupportedNowUtc, preferences) is null,
                    "未覆盖年份必须保持日历未就绪且不估算工资。");
            }

            return true;
        }
        finally
        {
            DeleteDirectoryIfPresent(packagedRoot);
            DeleteDirectoryIfPresent(conflictRoot);
            DeleteDirectoryIfPresent(missingRoot);
        }
    }

    public static UiQualityGateResults EvaluateUiQualityGates(UiQualityGateInputs input) =>
        new(
            input.MainReferenceDecorationScaling,
            input.MainReferenceDecorationLayerOrder,
            input.MainWorkspaceResourcesFollowTheme,
            input.MainCountdownCardSampleCount > 0 &&
                input.MainCountdownCardResourcesFollowTheme,
            input.MainNoteCardSampleCount > 0 &&
                input.MainNoteCardResourcesFollowTheme,
            input.FloatingContentSafeArea,
            input.FloatingTitleUsesSemiBoldWeight,
            input.FloatingNoteInputWraps,
            input.FloatingNoteInputScrolls);

    public static bool RunUiQualityGateIsolationBehavior()
    {
        var passing = new UiQualityGateInputs(
            true,
            true,
            true,
            1,
            true,
            1,
            true,
            true,
            true,
            true,
            true);

        VerifyOnlyTargetFails(
            passing with { MainReferenceDecorationScaling = false },
            nameof(UiQualityGateResults.MainReferenceDecorationScaling));
        VerifyOnlyTargetFails(
            passing with { MainReferenceDecorationLayerOrder = false },
            nameof(UiQualityGateResults.MainReferenceDecorationLayerOrder));
        VerifyOnlyTargetFails(
            passing with { MainWorkspaceResourcesFollowTheme = false },
            nameof(UiQualityGateResults.MainWorkspaceResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { MainCountdownCardSampleCount = 0 },
            nameof(UiQualityGateResults.MainCountdownCardResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { MainCountdownCardResourcesFollowTheme = false },
            nameof(UiQualityGateResults.MainCountdownCardResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { MainNoteCardSampleCount = 0 },
            nameof(UiQualityGateResults.MainNoteCardResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { MainNoteCardResourcesFollowTheme = false },
            nameof(UiQualityGateResults.MainNoteCardResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { FloatingContentSafeArea = false },
            nameof(UiQualityGateResults.FloatingContentSafeArea));
        VerifyOnlyTargetFails(
            passing with { FloatingTitleUsesSemiBoldWeight = false },
            nameof(UiQualityGateResults.FloatingTitleUsesSemiBoldWeight));
        VerifyOnlyTargetFails(
            passing with { FloatingNoteInputWraps = false },
            nameof(UiQualityGateResults.FloatingNoteInputWraps));
        VerifyOnlyTargetFails(
            passing with { FloatingNoteInputScrolls = false },
            nameof(UiQualityGateResults.FloatingNoteInputScrolls));

        return true;
    }

    public static CountdownEmptyStateGateResults EvaluateCountdownEmptyStateGates(
        CountdownEmptyStateGateInputs input) =>
        new(
            input.EmptyStatesDistinguished,
            input.PointerResetRestoresResults,
            input.KeyboardResetRestoresResults,
            input.DayThemeTextContrast);

    public static bool RunCountdownEmptyStateGateIsolationBehavior()
    {
        var passing = new CountdownEmptyStateGateInputs(true, true, true, true);
        Must(EvaluateCountdownEmptyStateGates(passing).AllPassed,
            "倒计时空态全通过夹具被错误拒绝。");
        VerifyOnlyCountdownEmptyStateGateFails(
            passing with { EmptyStatesDistinguished = false },
            nameof(CountdownEmptyStateGateResults.EmptyStatesDistinguished));
        VerifyOnlyCountdownEmptyStateGateFails(
            passing with { PointerResetRestoresResults = false },
            nameof(CountdownEmptyStateGateResults.PointerResetRestoresResults));
        VerifyOnlyCountdownEmptyStateGateFails(
            passing with { KeyboardResetRestoresResults = false },
            nameof(CountdownEmptyStateGateResults.KeyboardResetRestoresResults));
        VerifyOnlyCountdownEmptyStateGateFails(
            passing with { DayThemeTextContrast = false },
            nameof(CountdownEmptyStateGateResults.DayThemeTextContrast));
        return true;
    }

    public static NoteVisualTokenGateResults EvaluateNoteVisualTokenGates(
        NoteVisualTokenGateInputs input) =>
        new(
            input.CompactInputPaddingAligned,
            input.BadgeTokensFollowState,
            input.EmptyStateTextContrast);

    public static bool RunNoteVisualTokenGateIsolationBehavior()
    {
        var passing = new NoteVisualTokenGateInputs(true, true, true);
        Must(EvaluateNoteVisualTokenGates(passing).AllPassed,
            "主窗口便签视觉合同全通过夹具被错误拒绝。");
        VerifyOnlyNoteVisualTokenGateFails(
            passing with { CompactInputPaddingAligned = false },
            nameof(NoteVisualTokenGateResults.CompactInputPaddingAligned));
        VerifyOnlyNoteVisualTokenGateFails(
            passing with { BadgeTokensFollowState = false },
            nameof(NoteVisualTokenGateResults.BadgeTokensFollowState));
        VerifyOnlyNoteVisualTokenGateFails(
            passing with { EmptyStateTextContrast = false },
            nameof(NoteVisualTokenGateResults.EmptyStateTextContrast));
        return true;
    }

    public static bool RunFloatingEmptyStateTextIsolationBehavior()
    {
        var clearFilter = DesktopWorkspaceFilter.Empty;
        Must(
            string.Equals(
                FloatingWindow.CountdownEmptyStateText(clearFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingWindow.NoteEmptyStateText(clearFilter),
                "今天还没有待办便签。",
                StringComparison.Ordinal),
            "悬浮窗无筛选空态文案漂移。");

        var taggedFilter = clearFilter with
        {
            SelectedTagIds = Array.AsReadOnly(new[] { "floating-empty-gate-tag" })
        };
        var untaggedFilter = clearFilter with { UntaggedOnly = true };
        var countdownStatusFilter = clearFilter with { CountdownStatus = CountdownStatusFilter.Expired };
        var noteStatusFilter = clearFilter with { NoteStatus = NoteStatusFilter.Completed };
        var noteDateFilter = clearFilter with { NoteDate = "2032-02-03" };
        var undatedNotesFilter = clearFilter with { UndatedNotesOnly = true };
        foreach (var countdownConstrained in new[] { taggedFilter, untaggedFilter, countdownStatusFilter })
        {
            Must(
                string.Equals(
                    FloatingWindow.CountdownEmptyStateText(countdownConstrained),
                    "没有匹配的倒计时。",
                    StringComparison.Ordinal),
                "悬浮窗倒计时筛选无匹配空态未使用匹配口径文案。");
        }

        foreach (var noteConstrained in new[]
                 {
                     taggedFilter, untaggedFilter, noteStatusFilter, noteDateFilter, undatedNotesFilter
                 })
        {
            Must(
                string.Equals(
                    FloatingWindow.NoteEmptyStateText(noteConstrained),
                    "没有匹配的便签。",
                    StringComparison.Ordinal),
                "悬浮窗便签筛选无匹配空态未使用匹配口径文案。");
        }

        // 搜索词空态由 BuildNoteSearchEmptyState 单独接管，仅搜索词不触发筛选无匹配文案。
        var searchOnlyFilter = clearFilter with { NoteSearch = "悬浮空态搜索词" };
        Must(
            string.Equals(
                FloatingWindow.CountdownEmptyStateText(searchOnlyFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingWindow.NoteEmptyStateText(searchOnlyFilter),
                "今天还没有待办便签。",
                StringComparison.Ordinal),
            "仅搜索词不应触发悬浮窗筛选无匹配文案。");

        // 便签侧约束不影响倒计时文案，倒计时侧约束不影响便签文案。
        Must(
            string.Equals(
                FloatingWindow.CountdownEmptyStateText(noteStatusFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingWindow.CountdownEmptyStateText(noteDateFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingWindow.CountdownEmptyStateText(undatedNotesFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingWindow.NoteEmptyStateText(countdownStatusFilter),
                "今天还没有待办便签。",
                StringComparison.Ordinal),
            "悬浮窗两工作区筛选约束不应串扰空态文案。");
        return true;
    }

    public static MainNoteCompletionGateResults EvaluateMainNoteCompletionGates(
        MainNoteCompletionGateInputs input) =>
        new(
            input.DedicatedStyleApplied &&
            input.MainBoxAtLeast24Pixels &&
            input.MainTickScaled &&
            input.WarmTokensReused &&
            input.RoundedCornersPreserved &&
            input.CheckAndUncheckWork &&
            input.PersistenceFailureRollsBack &&
            input.AccessibleNamesUpdate,
            input.TagSelectorSizeUnchanged &&
            input.FloatingSizeUnchanged);

    public static bool RunMainNoteCompletionGateIsolationBehavior()
    {
        var passing = new MainNoteCompletionGateInputs(
            true, true, true, true, true, true, true, true, true, true);
        Must(EvaluateMainNoteCompletionGates(passing).AllPassed,
            "主窗口便签完成复选框全通过夹具被错误拒绝。");

        foreach (var input in new[]
        {
            passing with { DedicatedStyleApplied = false },
            passing with { MainBoxAtLeast24Pixels = false },
            passing with { MainTickScaled = false },
            passing with { WarmTokensReused = false },
            passing with { RoundedCornersPreserved = false },
            passing with { CheckAndUncheckWork = false },
            passing with { PersistenceFailureRollsBack = false },
            passing with { AccessibleNamesUpdate = false }
        })
        {
            VerifyOnlyMainNoteCompletionGateFails(
                input,
                nameof(MainNoteCompletionGateResults.MainTargetSizeAndBehavior));
        }

        foreach (var input in new[]
        {
            passing with { TagSelectorSizeUnchanged = false },
            passing with { FloatingSizeUnchanged = false }
        })
        {
            VerifyOnlyMainNoteCompletionGateFails(
                input,
                nameof(MainNoteCompletionGateResults.OtherUserSizesUnchanged));
        }

        return true;
    }

    internal const int AccessibleNameMaxLength = 512;

    internal static bool IsWellFormedAccessibleName(string name, string prefix)
    {
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
               name.Length > prefix.Length &&
               name.IndexOf('\n') < 0 &&
               name.IndexOf('\r') < 0 &&
               name.Length <= AccessibleNameMaxLength;
    }

    public static bool RunAccessibleNameWhitespaceIsolationBehavior()
    {
        const string completePrefix = "完成便签 ";
        Must(
            !IsWellFormedAccessibleName("完成便签 第一行\n第二行", completePrefix),
            "含换行的可访问名称没有被弱约束拒绝。");
        Must(
            IsWellFormedAccessibleName("完成便签 验证完成交互", completePrefix),
            "无异常空白的可访问名称被弱约束误伤。");
        Must(
            IsWellFormedAccessibleName("知道了：验证完成交互", "知道了："),
            "全角冒号前缀的可访问名称被弱约束误伤。");

        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-accessible-name-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        MainWindow? main = null;
        FloatingWindow? floating = null;
        ReminderCenterWindow? reminder = null;
        HolidayCalendar? calendar = null;
        try
        {
            const string noteId = "note-newline";
            const string countdownId = "countdown-newline";
            const string tagId = "tag-newline";
            const string noteText = "第一行\n第二行";
            const string countdownName = "主线\n倒计时";
            const string tagName = "标\n签";
            var settings = new CountdownSettings(
                new[]
                {
                    new CountdownRecord(
                        countdownId,
                        countdownName,
                        new DateTimeOffset(2031, 5, 1, 12, 0, 0, TimeSpan.Zero),
                        Array.Empty<string>())
                },
                new[]
                {
                    new NoteRecord(
                        noteId,
                        noteText,
                        "2031-04-05",
                        false,
                        new[] { tagId },
                        NoReminder.Instance)
                },
                new[] { new TagRecord(tagId, tagName, "#F4B6A6") },
                CountdownSettings.Empty.Preferences);
            new SettingsStore().Save(settingsPath, settings);
            var workspace = DesktopWorkspace.Load(settingsPath);
            calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            floating = new FloatingWindow(
                workspace,
                calendar,
                main,
                AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(floating);
            reminder = new ReminderCenterWindow();
            PrepareOffscreenValidationWindow(reminder);

            main.Show();
            floating.Show();
            reminder.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            main.UpdateLayout();
            floating.UpdateLayout();

            var noteTags = main.FindName("NoteTags") as TagSelector
                ?? throw new InvalidOperationException("主窗口缺少 NoteTags。");
            noteTags.SetTags(workspace.Snapshot.Settings.Tags);
            noteTags.SetSelection(new[] { tagId });
            reminder.AddEntry(new ReminderCenterItem(
                new ActiveReminderOccurrenceKey("rule-newline", DateTimeOffset.UtcNow),
                workspace.Snapshot.Settings.Notes.Single(),
                0,
                0));
            WindowValidationInput.Pump(main.Dispatcher);
            WindowValidationInput.Pump(reminder.Dispatcher);

            var mainCompletion = AutomationProperties.GetName(
                VisualDescendants<CheckBox>(main).Single(box =>
                    string.Equals(box.Tag as string, noteId, StringComparison.OrdinalIgnoreCase)));
            var mainActions = VisualDescendants<Button>(main)
                .Where(button =>
                    button.Content is "编辑" or "删除" &&
                    string.Equals(button.Tag as string, noteId, StringComparison.OrdinalIgnoreCase))
                .Select(AutomationProperties.GetName)
                .ToArray();
            var floatingCompletion = AutomationProperties.GetName(
                VisualDescendants<CheckBox>(floating).Single(box =>
                    string.Equals(box.Tag as string, noteId, StringComparison.OrdinalIgnoreCase)));
            floating.ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();
            var floatingCountdownNames = VisualDescendants<FrameworkElement>(floating)
                .Select(AutomationProperties.GetName)
                .Where(name => name.Contains("倒计时", StringComparison.Ordinal) &&
                               name.Contains("主线", StringComparison.Ordinal))
                .ToArray();
            var reminderNames = VisualDescendants<FrameworkElement>(reminder)
                .Select(AutomationProperties.GetName)
                .Where(name => name.Contains("第一行", StringComparison.Ordinal))
                .ToArray();
            var selectedTagName = AutomationProperties.GetName(noteTags.PopupTrigger);

            Must(
                mainCompletion.Contains("第一行", StringComparison.Ordinal) &&
                mainCompletion.Contains("第二行", StringComparison.Ordinal),
                "主窗口完成名称没有嵌入 settings JSON 便签正文。");
            Must(mainActions.Length == 2, "主窗口编辑/删除按钮没有渲染。");
            Must(
                floatingCompletion.Contains("第一行", StringComparison.Ordinal),
                "悬浮窗完成名称没有嵌入 settings JSON 便签正文。");
            Must(floatingCountdownNames.Length > 0, "悬浮窗倒计时名称没有嵌入 settings JSON 倒计时名。");
            Must(reminderNames.Length > 0, "提醒中心名称没有嵌入 settings JSON 便签正文。");
            Must(
                selectedTagName.Contains("标", StringComparison.Ordinal) &&
                selectedTagName.Contains("签", StringComparison.Ordinal),
                "已选标签名称没有嵌入 settings JSON 标签名。");

            var names = new[] { mainCompletion, floatingCompletion, selectedTagName }
                .Concat(mainActions)
                .Concat(floatingCountdownNames)
                .Concat(reminderNames)
                .ToArray();
            return names.All(name => name.IndexOf('\n') < 0 && name.IndexOf('\r') < 0);
        }
        finally
        {
            foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                if (preexistingWindows.Contains(window))
                {
                    continue;
                }

                if (window is ReminderCenterWindow reminderCenter)
                {
                    reminderCenter.ForceClose();
                }
                else
                {
                    window.Close();
                }
            }

            for (var attempt = 0; attempt < 8; attempt++)
            {
                WindowValidationInput.Pump(Application.Current.Dispatcher);
                if (Application.Current.Windows.Cast<Window>().All(window =>
                        preexistingWindows.Contains(window) || !window.IsVisible))
                {
                    break;
                }
            }

            var preexistingMain = Application.Current.Windows
                .OfType<MainWindow>()
                .FirstOrDefault(window => preexistingWindows.Contains(window) && window.IsVisible);
            if (preexistingMain is not null)
            {
                preexistingMain.Activate();
                WindowValidationInput.Pump(preexistingMain.Dispatcher);
            }

            calendar?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    public static MainCountdownRemainingGateResults EvaluateMainCountdownRemainingGates(
        MainCountdownRemainingGateInputs input) =>
        new(
            input.ExpectedTexts.Count == 6 &&
                input.RenderedTexts.SequenceEqual(input.ExpectedTexts, StringComparer.Ordinal),
                input.HasExpectedSampleCount &&
                input.ColumnsAre300Pixels &&
                input.ActualColumnsAre300Pixels &&
                input.TextIsRightAligned &&
                input.TextIsSingleLineAndUntrimmed &&
                input.TextIsFullyVisible &&
                input.UsesTabularNumerals &&
                input.PrecisionSamplesAreSaved &&
                input.FontSizingMatches);

    public static bool RunMainCountdownRemainingGateIsolationBehavior()
    {
        var expected = new[]
        {
            "4 天",
            "4 天 13 小时",
            "4 天 13 小时 18 分钟",
            "4 天 13 小时 18 分钟 59 秒",
            "2000000 天 00 小时 00 分钟 00 秒",
            "999 天 13 小时 18 分钟 59 秒"
        };
        var passing = new MainCountdownRemainingGateInputs(
            expected,
            expected,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        var passingResult = EvaluateMainCountdownRemainingGates(passing);
        Must(passingResult.UnitSpacingMatches && passingResult.FixedColumnLayoutMatches,
            "主窗口倒计时剩余时间全通过夹具被错误拒绝。");
        VerifyOnlyMainCountdownRemainingGateFails(
            passing with { ColumnsAre300Pixels = false },
            nameof(MainCountdownRemainingGateResults.FixedColumnLayoutMatches));
        VerifyOnlyMainCountdownRemainingGateFails(
            passing with { ActualColumnsAre300Pixels = false },
            nameof(MainCountdownRemainingGateResults.FixedColumnLayoutMatches));
        VerifyOnlyMainCountdownRemainingGateFails(
            passing with { FontSizingMatches = false },
            nameof(MainCountdownRemainingGateResults.FixedColumnLayoutMatches));
        VerifyOnlyMainCountdownRemainingGateFails(
            passing with
            {
                RenderedTexts = expected.Select(text => text.Replace(" ", string.Empty, StringComparison.Ordinal)).ToArray()
            },
            nameof(MainCountdownRemainingGateResults.UnitSpacingMatches));
        return true;
    }

    // #148：倒计时可读性合同（徽章对比/字号/居中、到期文字、分格 2:3 与单位字下限、
    // 选中段对比度唯一门禁）——用会失败的反例保住门禁。
    public static CountdownReadabilityGateResults EvaluateCountdownReadabilityGates(
        CountdownReadabilityGateInputs input) =>
        new(
            input.BadgeUsesInkText && input.BadgeMeetsContrast && input.BadgeSizedAndCentered,
            input.ExpiredSecondaryUsesBodyColor && input.ExpiredPrimaryUsesNumberColor,
            input.BlocksVerticalRatioMatches && input.UnitFontSizeAtFloor,
            input.SegmentContrastGateOnly);

    public static bool RunCountdownReadabilityGateIsolationBehavior()
    {
        var passing = new CountdownReadabilityGateInputs(
            true, true, true, true, true, true, true, true);
        Must(EvaluateCountdownReadabilityGates(passing).AllPassed, "倒计时可读性全通过夹具被错误拒绝。");
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { BadgeUsesInkText = false },
            nameof(CountdownReadabilityGateResults.BadgeContractMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { BadgeMeetsContrast = false },
            nameof(CountdownReadabilityGateResults.BadgeContractMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { BadgeSizedAndCentered = false },
            nameof(CountdownReadabilityGateResults.BadgeContractMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { ExpiredSecondaryUsesBodyColor = false },
            nameof(CountdownReadabilityGateResults.ExpiredTextMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { ExpiredPrimaryUsesNumberColor = false },
            nameof(CountdownReadabilityGateResults.ExpiredTextMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { BlocksVerticalRatioMatches = false },
            nameof(CountdownReadabilityGateResults.BlocksGeometryMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { UnitFontSizeAtFloor = false },
            nameof(CountdownReadabilityGateResults.BlocksGeometryMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { SegmentContrastGateOnly = false },
            nameof(CountdownReadabilityGateResults.SegmentContrastGateOnly));
        return true;
    }

    // #151：倒计时编辑合同（字段错误贴源+聚焦首个无效控件、错误清除、错误行不裁切、
    // 偏好错误贴字段、主次按钮角色、白纸输入面、独立选区高亮、状态条对比、单选圆点、排版 token）
    // ——用会失败的反例保住门禁。
    public static CountdownEditingGateResults EvaluateCountdownEditingGates(
        CountdownEditingGateInputs input) =>
        new(
            input.CountdownFieldErrorsAtSource && input.FocusFirstInvalidControl,
            input.CountdownFieldErrorsClearOnCorrection,
            input.SingleErrorRowNotClipped,
            input.PreferenceErrorsBelowFields && input.PreferenceErrorsClearOnCorrection,
            input.PrimarySecondaryRolesDistinct,
            input.InputSurfacesWhitePaper,
            input.SelectionHighlightDistinct,
            input.StatusBarSurfaceErrorContrast,
            input.ReminderRadioSelectedContrast,
            input.EditorTypographyTokenized);

    public static bool RunCountdownEditingGateIsolationBehavior()
    {
        var passing = new CountdownEditingGateInputs(
            true, true, true, true, true, true, true, true, true, true, true, true);
        Must(EvaluateCountdownEditingGates(passing).AllPassed, "倒计时编辑全通过夹具被错误拒绝。");
        VerifyOnlyCountdownEditingGateFails(
            passing with { CountdownFieldErrorsAtSource = false },
            nameof(CountdownEditingGateResults.FieldErrorsAtSourceWithFocus));
        VerifyOnlyCountdownEditingGateFails(
            passing with { FocusFirstInvalidControl = false },
            nameof(CountdownEditingGateResults.FieldErrorsAtSourceWithFocus));
        VerifyOnlyCountdownEditingGateFails(
            passing with { CountdownFieldErrorsClearOnCorrection = false },
            nameof(CountdownEditingGateResults.FieldErrorsClear));
        VerifyOnlyCountdownEditingGateFails(
            passing with { SingleErrorRowNotClipped = false },
            nameof(CountdownEditingGateResults.SingleErrorRowNotClipped));
        VerifyOnlyCountdownEditingGateFails(
            passing with { PreferenceErrorsBelowFields = false },
            nameof(CountdownEditingGateResults.PreferenceErrorsAtSource));
        VerifyOnlyCountdownEditingGateFails(
            passing with { PreferenceErrorsClearOnCorrection = false },
            nameof(CountdownEditingGateResults.PreferenceErrorsAtSource));
        VerifyOnlyCountdownEditingGateFails(
            passing with { PrimarySecondaryRolesDistinct = false },
            nameof(CountdownEditingGateResults.PrimarySecondaryRolesDistinct));
        VerifyOnlyCountdownEditingGateFails(
            passing with { InputSurfacesWhitePaper = false },
            nameof(CountdownEditingGateResults.InputSurfacesWhitePaper));
        VerifyOnlyCountdownEditingGateFails(
            passing with { SelectionHighlightDistinct = false },
            nameof(CountdownEditingGateResults.SelectionHighlightDistinct));
        VerifyOnlyCountdownEditingGateFails(
            passing with { StatusBarSurfaceErrorContrast = false },
            nameof(CountdownEditingGateResults.StatusBarErrorContrast));
        VerifyOnlyCountdownEditingGateFails(
            passing with { ReminderRadioSelectedContrast = false },
            nameof(CountdownEditingGateResults.ReminderRadioSelectedContrast));
        VerifyOnlyCountdownEditingGateFails(
            passing with { EditorTypographyTokenized = false },
            nameof(CountdownEditingGateResults.EditorTypographyTokenized));
        return true;
    }

    public static bool RunCountdownIncrementalRefreshGateIsolationBehavior()
    {
        var passing = new CountdownIncrementalRefreshGateResults(
            true, true, true, true, true, true, true, true, true, true);
        Must(passing.AllPassed, "倒计时增量刷新全通过夹具被错误拒绝。");
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { IdentityPreserved = false },
            nameof(CountdownIncrementalRefreshGateResults.IdentityPreserved));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { FocusPreserved = false },
            nameof(CountdownIncrementalRefreshGateResults.FocusPreserved));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { NonZeroScrollPreserved = false },
            nameof(CountdownIncrementalRefreshGateResults.NonZeroScrollPreserved));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { PointerStatePreserved = false },
            nameof(CountdownIncrementalRefreshGateResults.PointerStatePreserved));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { CollectionChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.CollectionChangeRebuilt));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { FilterChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.FilterChangeRebuilt));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { PrecisionChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.PrecisionChangeRebuilt));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { ExpiryChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.ExpiryChangeRebuilt));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { ExpiryBackgroundFlipped = false },
            nameof(CountdownIncrementalRefreshGateResults.ExpiryBackgroundFlipped));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { ExpiryRemainingTextFlipped = false },
            nameof(CountdownIncrementalRefreshGateResults.ExpiryRemainingTextFlipped));
        return true;
    }

    /// <summary>
    ///     #212：悬浮窗渲染签名的隔离反例——同输入同签名；标题/精度/筛选/错误/结构变化必须改签名；
    ///     剩余时间仅数值变化（位数不变或变化）不得改签名。
    /// </summary>
    public static bool RunFloatingRenderSignatureGateIsolationBehavior()
    {
        var filterFingerprint = "tag-a\u001eAny\u001eFalse\u001eAll\u001eAll\u001e\u001e\u001eFalse";
        var entries = new[]
        {
            "id-1\u001e报告截止\u001e639000000000000000\u001e0 天 0 小时 0 分钟 0 秒"
        };
        var baseline = FloatingWindow.BuildFloatingRenderSignature(
            WorkspaceKind.Countdowns,
            "Second",
            filterFingerprint,
            entries);
        Must(string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint,
                    entries),
                StringComparison.Ordinal),
            "渲染签名同输入必须逐字节稳定。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint,
                    new[] { "id-1\u001e改名\u001e639000000000000000\u001e0 天 0 小时 0 分钟 0 秒" }),
                StringComparison.Ordinal),
            "标题变化必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Minute",
                    filterFingerprint,
                    entries),
                StringComparison.Ordinal),
            "显示精度变化必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint + "\u001etag-b",
                    entries),
                StringComparison.Ordinal),
            "筛选指纹变化必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Notes,
                    "Second",
                    filterFingerprint,
                    entries),
                StringComparison.Ordinal),
            "工作区变化必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint,
                    new[] { "id-1\u001e报告截止\u001e639000000000000000\u001e0 天 0 小时 0 分钟" }),
                StringComparison.Ordinal),
            "剩余时间结构变化（单位序列变化）必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint,
                    entries.Concat(new[] { "id-2\u001e新记录\u001e639000000000000000\u001e0 天" })),
                StringComparison.Ordinal),
            "记录序列变化必须改变渲染签名。");

        Must(string.Equals(
                FloatingWindow.NormalizeFloatingRemainingStructure("12 天 03 小时 7 分钟 09 秒"),
                FloatingWindow.NormalizeFloatingRemainingStructure("9 天 3 小时 40 分钟 8 秒"),
                StringComparison.Ordinal),
            "剩余时间仅数值变化（含位数变化）必须保持同一签名结构。");
        Must(!string.Equals(
                FloatingWindow.NormalizeFloatingRemainingStructure("2 天 0 小时"),
                FloatingWindow.NormalizeFloatingRemainingStructure("2 天 0 小时 0 分钟"),
                StringComparison.Ordinal),
            "剩余时间单位序列变化必须产生不同签名结构。");
        Must(string.Equals(
                FloatingWindow.NormalizeFloatingRemainingStructure("已到期"),
                "已到期",
                StringComparison.Ordinal),
            "到期回落文案不含数字，结构归一化必须保持原文。");
        return true;
    }

    public static ConfirmationDialogGateResults EvaluateConfirmationDialogGates(
        ConfirmationDialogGateInputs input) =>
        new(
            input.ButtonsVisible,
            input.BodyReachable,
            input.CancelIsSafeDefault,
            input.ProductionMatrixComplete,
            input.BodyEndReachable,
            input.CenteredOnOwner,
            input.EnterIsSafe,
            input.CancelAndEscapeAreSafe);

    public static bool RunConfirmationDialogGateIsolationBehavior()
    {
        var passing = new ConfirmationDialogGateInputs(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        Must(EvaluateConfirmationDialogGates(passing).AllPassed,
            "删除确认对话框全通过夹具被错误拒绝。");
        VerifyOnlyConfirmationGateFails(passing with { ButtonsVisible = false }, nameof(ConfirmationDialogGateResults.ButtonsVisible));
        VerifyOnlyConfirmationGateFails(passing with { BodyReachable = false }, nameof(ConfirmationDialogGateResults.BodyReachable));
        VerifyOnlyConfirmationGateFails(passing with { CancelIsSafeDefault = false }, nameof(ConfirmationDialogGateResults.CancelIsSafeDefault));
        VerifyOnlyConfirmationGateFails(passing with { ProductionMatrixComplete = false }, nameof(ConfirmationDialogGateResults.ProductionMatrixComplete));
        VerifyOnlyConfirmationGateFails(passing with { BodyEndReachable = false }, nameof(ConfirmationDialogGateResults.BodyEndReachable));
        VerifyOnlyConfirmationGateFails(passing with { CenteredOnOwner = false }, nameof(ConfirmationDialogGateResults.CenteredOnOwner));
        VerifyOnlyConfirmationGateFails(passing with { EnterIsSafe = false }, nameof(ConfirmationDialogGateResults.EnterIsSafe));
        VerifyOnlyConfirmationGateFails(passing with { CancelAndEscapeAreSafe = false }, nameof(ConfirmationDialogGateResults.CancelAndEscapeAreSafe));
        return true;
    }

    public static bool RunSecondaryWindowTypographyGateIsolationBehavior()
    {
        var passing = new SecondaryWindowTypographyGateInputs(true, true, true);
        Must(
            EvaluateSecondaryWindowTypographyGates(passing).AllPassed,
            "次级窗口字号全通过夹具被错误拒绝。");
        VerifyOnlySecondaryWindowTypographyGateFails(
            passing with { RenderedScaleMatches = false },
            nameof(SecondaryWindowTypographyGateResults.RenderedScaleMatches));
        VerifyOnlySecondaryWindowTypographyGateFails(
            passing with { ContentRootsDeclareFontSize = false },
            nameof(SecondaryWindowTypographyGateResults.ContentRootsDeclareFontSize));
        VerifyOnlySecondaryWindowTypographyGateFails(
            passing with { FloatingTypographyUnchanged = false },
            nameof(SecondaryWindowTypographyGateResults.FloatingTypographyUnchanged));
        return true;
    }

    // #35 tautology inventory (src/). Each item is one of:
    //   已删除 / 已改为真实检查 / 明确保留但非验收证据
    // 已删除:
    //   ReminderCenterWindow.SupportsKeyboardNavigation => true (0208b1e).
    // 已改为真实检查:
    //   ReminderCenterWindow.UsesScrollableList => true — replaced by
    //   ValidationReport.ValidateReminderCenterBehavior scrollsLongContent
    //   (Auto ScrollViewer, ScrollableHeight > 0, 12 wrapping bodies) and
    //   ReminderCenterScrollsLongContent.
    //   MainCountdownRemaining FormattedTexts.SequenceEqual(ExpectedTexts) after
    //   both sides were ExpectedText — report now compares RenderedTexts to
    //   independent ExpectedTexts.
    //   MainWindow.InitialFocusIsSafeAndDeterministic /
    //   TabSequenceStartsWithWorkspaceControls — report fields bind observed
    //   Loaded focus and the real workspace Tab prefix.
    // 明确保留但非验收证据:
    //   isolation passing fixtures; validate-only/production tray () => true;
    //   HasVisibleBrush non-solid fallback; floating secondary FormattedText
    //   from FormatRemaining; Core calendar/JSON => true matches.
    public static ReminderCenterKeyboardGateResults EvaluateReminderCenterKeyboardGates(
        ReminderCenterKeyboardGateInputs input) =>
        new(
            input.TabTraversalWorks,
            input.CloseGuardWorks,
            input.ShowPreservesFocusAndAccessibleName,
            input.EscapeFocusesAcknowledge);

    public static bool RunReminderCenterKeyboardGateIsolationBehavior()
    {
        var passing = new ReminderCenterKeyboardGateInputs(true, true, true, true);
        Must(EvaluateReminderCenterKeyboardGates(passing).AllPassed,
            "提醒中心键盘全通过夹具被错误拒绝。");
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { TabTraversalWorks = false },
            nameof(ReminderCenterKeyboardGateResults.TabTraversalWorks));
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { CloseGuardWorks = false },
            nameof(ReminderCenterKeyboardGateResults.CloseGuardWorks));
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { ShowPreservesFocusAndAccessibleName = false },
            nameof(ReminderCenterKeyboardGateResults.ShowPreservesFocusAndAccessibleName));
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { EscapeFocusesAcknowledge = false },
            nameof(ReminderCenterKeyboardGateResults.EscapeFocusesAcknowledge));
        return true;
    }

    public static SecondaryWindowTypographyGateResults EvaluateSecondaryWindowTypographyGates(
        SecondaryWindowTypographyGateInputs input) =>
        new(
            input.RenderedScaleMatches,
            input.ContentRootsDeclareFontSize,
            input.FloatingTypographyUnchanged);

    public static bool RunMainShortcutGateIsolationBehavior()
    {
        var expectedCommands = new[]
        {
            "CountdownsWorkspace", "Filter", "Minimize", "NoteSearch",
            "NotesWorkspace", "SaveCountdown", "SavePreferences", "TagManager"
        };
        var passingCommands = expectedCommands.ToDictionary(
            command => command,
            _ => true,
            StringComparer.Ordinal);
        var passing = new MainShortcutGateInputs(
            expectedCommands,
            passingCommands,
            true,
            true,
            true);
        Must(EvaluateMainShortcutGates(passing).AllPassed,
            "主窗口快捷键全通过夹具被错误拒绝。");

        var missingMapping = passingCommands
            .Where(pair => !string.Equals(pair.Key, "SaveCountdown", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        VerifyOnlyShortcutGateFails(
            passing with { ObservedCommands = missingMapping },
            nameof(MainShortcutGateResults.CommandsWork));
        foreach (var command in expectedCommands)
        {
            var brokenEvent = passingCommands.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            brokenEvent[command] = false;
            VerifyOnlyShortcutGateFails(
                passing with { ObservedCommands = brokenEvent },
                nameof(MainShortcutGateResults.CommandsWork));
        }
        VerifyOnlyShortcutGateFails(
            passing with { TextInputPreserved = false },
            nameof(MainShortcutGateResults.TextInputPreserved));
        VerifyOnlyShortcutGateFails(
            passing with { PopupGuarded = false },
            nameof(MainShortcutGateResults.PopupGuarded));
        VerifyOnlyShortcutGateFails(
            passing with { ModalOwnerGuarded = false },
            nameof(MainShortcutGateResults.ModalOwnerGuarded));
        return true;
    }

    public static MainShortcutGateResults EvaluateMainShortcutGates(MainShortcutGateInputs input)
    {
        var expected = input.ExpectedCommands.Order(StringComparer.Ordinal).ToArray();
        var observed = input.ObservedCommands.Keys.Order(StringComparer.Ordinal).ToArray();
        return new MainShortcutGateResults(
            expected.Length == 8 &&
                observed.SequenceEqual(expected, StringComparer.Ordinal) &&
                input.ObservedCommands.Values.All(value => value),
            input.TextInputPreserved,
            input.PopupGuarded,
            input.ModalOwnerGuarded);
    }

    public static MainKeyboardNavigationGateResults EvaluateMainKeyboardNavigationGates(
        MainKeyboardNavigationGateInputs input) =>
        new(input.InitialFocusSafe, input.CompleteTabSequence);

    public static MainStructuralAnnouncementGateResults EvaluateMainStructuralAnnouncementGates(
        MainStructuralAnnouncementGateInputs input) =>
        new(
            input.WorkspaceAnnouncements,
            input.FilterResultAnnouncement,
            input.EmptyStateAnnouncements);

    public static bool RunMainStructuralAnnouncementGateIsolationBehavior()
    {
        var passing = new MainStructuralAnnouncementGateInputs(true, true, true);
        Must(
            EvaluateMainStructuralAnnouncementGates(passing).AllPassed,
            "主窗口结构变化通知全通过夹具被错误拒绝。");
        VerifyOnlyMainStructuralAnnouncementGateFails(
            passing with { WorkspaceAnnouncements = false },
            nameof(MainStructuralAnnouncementGateResults.WorkspaceAnnouncements));
        VerifyOnlyMainStructuralAnnouncementGateFails(
            passing with { FilterResultAnnouncement = false },
            nameof(MainStructuralAnnouncementGateResults.FilterResultAnnouncement));
        VerifyOnlyMainStructuralAnnouncementGateFails(
            passing with { EmptyStateAnnouncements = false },
            nameof(MainStructuralAnnouncementGateResults.EmptyStateAnnouncements));
        return true;
    }

    public static bool RunMainKeyboardNavigationGateIsolationBehavior()
    {
        var passing = new MainKeyboardNavigationGateInputs(true, true);
        Must(
            EvaluateMainKeyboardNavigationGates(passing).AllPassed,
            "主窗口键盘导航全通过夹具被错误拒绝。");
        VerifyOnlyMainKeyboardNavigationGateFails(
            passing with { InitialFocusSafe = false },
            nameof(MainKeyboardNavigationGateResults.InitialFocusSafe));
        VerifyOnlyMainKeyboardNavigationGateFails(
            passing with { CompleteTabSequence = false },
            nameof(MainKeyboardNavigationGateResults.CompleteTabSequence));
        return true;
    }

    // #153：弹层 Tab 层内循环门禁——筛选 / 标签管理 / 标签选择器 / 便签溢出菜单四类弹层逐一收敛，
    // 负向夹具证明每个输入位都会被独立击穿，评估器不可能是恒真。
    public static PopupFocusTabGateResults EvaluatePopupFocusTabGates(
        PopupFocusTabGateInputs input) =>
        new(
            input.FilterCyclesInsideLayer,
            input.TagManagerCyclesInsideLayer,
            input.TagSelectorCyclesInsideLayer,
            input.OverflowMenuCyclesInsideLayer);

    public static bool RunPopupFocusTabGateIsolationBehavior()
    {
        var passing = new PopupFocusTabGateInputs(true, true, true, true);
        Must(
            EvaluatePopupFocusTabGates(passing).AllCyclesInsideLayer,
            "弹层 Tab 层内循环全通过夹具被错误拒绝。");
        VerifyOnlyPopupFocusTabGateFails(
            passing with { FilterCyclesInsideLayer = false },
            nameof(PopupFocusTabGateResults.FilterCyclesInsideLayer));
        VerifyOnlyPopupFocusTabGateFails(
            passing with { TagManagerCyclesInsideLayer = false },
            nameof(PopupFocusTabGateResults.TagManagerCyclesInsideLayer));
        VerifyOnlyPopupFocusTabGateFails(
            passing with { TagSelectorCyclesInsideLayer = false },
            nameof(PopupFocusTabGateResults.TagSelectorCyclesInsideLayer));
        VerifyOnlyPopupFocusTabGateFails(
            passing with { OverflowMenuCyclesInsideLayer = false },
            nameof(PopupFocusTabGateResults.OverflowMenuCyclesInsideLayer));
        return true;
    }

    private static void VerifyOnlyPopupFocusTabGateFails(
        PopupFocusTabGateInputs input,
        string target)
    {
        var result = EvaluatePopupFocusTabGates(input);
        Must(!result.AllCyclesInsideLayer && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"弹层 Tab 层内循环负向夹具未只击穿 {target}。");
    }

    public static FloatingReferenceManifestGateResults EvaluateFloatingReferenceManifestGates(
        FloatingReferenceManifestGateInputs input) =>
        new(
            input.ManifestStructureValid,
            input.ExpectedCaptureFilesPresent,
            input.CapturePathsUnique,
            input.NoOrphanPngFiles,
            input.CanvasDimensionsMatch,
            input.ScenarioThemePairsUnique,
            input.SourceHashesMatch,
            input.ExecutableHashMatches,
            input.PngHashesMatch,
            input.AlphaMetricsMatch,
            input.ContentRoiMatches,
            input.PixelMetricsWithinThresholds);

    public static bool RunFloatingReferenceManifestGateIsolationBehavior()
    {
        var passing = new FloatingReferenceManifestGateInputs(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        Must(
            EvaluateFloatingReferenceManifestGates(passing).AllPassed,
            "悬浮窗参考图 manifest 全通过夹具被错误拒绝。");

        var fixtures = new (string Name, FloatingReferenceManifestGateInputs Input, string Gate)[]
        {
            ("malformed-manifest", passing with { ManifestStructureValid = false },
                nameof(FloatingReferenceManifestGateResults.ManifestStructureValid)),
            ("missing-png", passing with { ExpectedCaptureFilesPresent = false },
                nameof(FloatingReferenceManifestGateResults.ExpectedCaptureFilesPresent)),
            ("duplicate-path", passing with { CapturePathsUnique = false },
                nameof(FloatingReferenceManifestGateResults.CapturePathsUnique)),
            ("orphan-png", passing with { NoOrphanPngFiles = false },
                nameof(FloatingReferenceManifestGateResults.NoOrphanPngFiles)),
            ("wrong-dimensions", passing with { CanvasDimensionsMatch = false },
                nameof(FloatingReferenceManifestGateResults.CanvasDimensionsMatch)),
            ("duplicate-scenario-theme", passing with { ScenarioThemePairsUnique = false },
                nameof(FloatingReferenceManifestGateResults.ScenarioThemePairsUnique)),
            ("source-hash-mismatch", passing with { SourceHashesMatch = false },
                nameof(FloatingReferenceManifestGateResults.SourceHashesMatch)),
            ("executable-hash-mismatch", passing with { ExecutableHashMatches = false },
                nameof(FloatingReferenceManifestGateResults.ExecutableHashMatches)),
            ("png-hash-mismatch", passing with { PngHashesMatch = false },
                nameof(FloatingReferenceManifestGateResults.PngHashesMatch)),
            ("alpha-metrics-mismatch", passing with { AlphaMetricsMatch = false },
                nameof(FloatingReferenceManifestGateResults.AlphaMetricsMatch)),
            ("content-roi-mismatch", passing with { ContentRoiMatches = false },
                nameof(FloatingReferenceManifestGateResults.ContentRoiMatches)),
            ("pixel-metrics-mismatch", passing with { PixelMetricsWithinThresholds = false },
                nameof(FloatingReferenceManifestGateResults.PixelMetricsWithinThresholds))
        };
        foreach (var fixture in fixtures)
        {
            VerifyOnlyFloatingReferenceManifestGateFails(
                fixture.Name,
                fixture.Input,
                fixture.Gate);
        }

        return true;
    }

    public static FloatingReferenceFreshnessGateResults EvaluateFloatingReferenceFreshnessGates(
        FloatingReferenceFreshnessGateInputs input) =>
        new(
            input.ExpectedStateBindingsMatch,
            input.CaptureIdsUnique,
            input.StateFingerprintsMatch,
            input.StateFingerprintsUnique,
            input.PngHashesUnique,
            input.TopBottomPixelsDiffer,
            input.LongNoteTypographyMatches,
            input.LongNoteLayoutValid);

    public static bool RunFloatingReferenceFreshnessGateIsolationBehavior()
    {
        var passing = new FloatingReferenceFreshnessGateInputs(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        Must(
            EvaluateFloatingReferenceFreshnessGates(passing).AllPassed,
            "悬浮窗长便签帧新鲜度全通过夹具被错误拒绝。");

        var fixtures = new (string Name, FloatingReferenceFreshnessGateInputs Input, string Gate)[]
        {
            ("swapped-top-bottom", passing with { ExpectedStateBindingsMatch = false },
                nameof(FloatingReferenceFreshnessGateResults.ExpectedStateBindingsMatch)),
            ("duplicate-capture-id", passing with { CaptureIdsUnique = false },
                nameof(FloatingReferenceFreshnessGateResults.CaptureIdsUnique)),
            ("state-fingerprint-mismatch", passing with { StateFingerprintsMatch = false },
                nameof(FloatingReferenceFreshnessGateResults.StateFingerprintsMatch)),
            ("duplicate-state-fingerprint", passing with { StateFingerprintsUnique = false },
                nameof(FloatingReferenceFreshnessGateResults.StateFingerprintsUnique)),
            ("reused-frame", passing with { PngHashesUnique = false },
                nameof(FloatingReferenceFreshnessGateResults.PngHashesUnique)),
            ("top-bottom-pixel-difference-too-small", passing with { TopBottomPixelsDiffer = false },
                nameof(FloatingReferenceFreshnessGateResults.TopBottomPixelsDiffer)),
            ("long-note-typography-mismatch", passing with { LongNoteTypographyMatches = false },
                nameof(FloatingReferenceFreshnessGateResults.LongNoteTypographyMatches)),
            ("long-note-layout-unreachable", passing with { LongNoteLayoutValid = false },
                nameof(FloatingReferenceFreshnessGateResults.LongNoteLayoutValid))
        };
        foreach (var fixture in fixtures)
        {
            var results = EvaluateFloatingReferenceFreshnessGates(fixture.Input).AsDictionary();
            Must(
                results.TryGetValue(fixture.Gate, out var failed) && !failed,
                $"悬浮窗长便签负向夹具 {fixture.Name} 未让 {fixture.Gate} 变红。");
            Must(
                results
                    .Where(pair => !string.Equals(pair.Key, fixture.Gate, StringComparison.Ordinal))
                    .All(pair => pair.Value),
                $"悬浮窗长便签负向夹具 {fixture.Name} 误伤无关契约。");
        }

        return true;
    }

    public static MainReferenceManifestGateResults EvaluateMainReferenceManifestGates(
        MainReferenceManifestGateInputs input) =>
        new(
            input.ManifestStructureValid,
            input.ExpectedCaptureFilesPresent,
            input.NoOrphanPngFiles,
            input.CaptureIdsUnique,
            input.CapturePathsUnique,
            input.ScenarioBindingsUnique,
            input.CanvasDimensionsMatch,
            input.SourceHashesMatch,
            input.ExecutableHashMatches,
            input.PngHashesMatch,
            input.StateFingerprintsMatch,
            input.AlphaMetricsMatch,
            input.ContentRoiMatches,
            input.PixelMetricsWithinThresholds,
            input.StateFingerprintsUnique,
            input.PngHashesUnique);

    public static bool RunMainReferenceManifestGateIsolationBehavior()
    {
        var passing = new MainReferenceManifestGateInputs(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        Must(
            EvaluateMainReferenceManifestGates(passing).AllPassed,
            "主窗口参考图 manifest 全通过夹具被错误拒绝。");

        var fixtures = new (string Name, MainReferenceManifestGateInputs Input, string Gate)[]
        {
            ("malformed-manifest", passing with { ManifestStructureValid = false },
                nameof(MainReferenceManifestGateResults.ManifestStructureValid)),
            ("missing-png", passing with { ExpectedCaptureFilesPresent = false },
                nameof(MainReferenceManifestGateResults.ExpectedCaptureFilesPresent)),
            ("orphan-png", passing with { NoOrphanPngFiles = false },
                nameof(MainReferenceManifestGateResults.NoOrphanPngFiles)),
            ("duplicate-capture-id", passing with { CaptureIdsUnique = false },
                nameof(MainReferenceManifestGateResults.CaptureIdsUnique)),
            ("duplicate-path", passing with { CapturePathsUnique = false },
                nameof(MainReferenceManifestGateResults.CapturePathsUnique)),
            ("duplicate-scenario", passing with { ScenarioBindingsUnique = false },
                nameof(MainReferenceManifestGateResults.ScenarioBindingsUnique)),
            ("wrong-dimensions", passing with { CanvasDimensionsMatch = false },
                nameof(MainReferenceManifestGateResults.CanvasDimensionsMatch)),
            ("source-hash-mismatch", passing with { SourceHashesMatch = false },
                nameof(MainReferenceManifestGateResults.SourceHashesMatch)),
            ("executable-hash-mismatch", passing with { ExecutableHashMatches = false },
                nameof(MainReferenceManifestGateResults.ExecutableHashMatches)),
            ("png-hash-mismatch", passing with { PngHashesMatch = false },
                nameof(MainReferenceManifestGateResults.PngHashesMatch)),
            ("state-fingerprint-mismatch", passing with { StateFingerprintsMatch = false },
                nameof(MainReferenceManifestGateResults.StateFingerprintsMatch)),
            ("alpha-metrics-mismatch", passing with { AlphaMetricsMatch = false },
                nameof(MainReferenceManifestGateResults.AlphaMetricsMatch)),
            ("content-roi-mismatch", passing with { ContentRoiMatches = false },
                nameof(MainReferenceManifestGateResults.ContentRoiMatches)),
            ("pixel-metrics-mismatch", passing with { PixelMetricsWithinThresholds = false },
                nameof(MainReferenceManifestGateResults.PixelMetricsWithinThresholds)),
            ("duplicate-state-fingerprint", passing with { StateFingerprintsUnique = false },
                nameof(MainReferenceManifestGateResults.StateFingerprintsUnique)),
            ("reused-frame", passing with { PngHashesUnique = false },
                nameof(MainReferenceManifestGateResults.PngHashesUnique))
        };
        foreach (var fixture in fixtures)
        {
            var results = EvaluateMainReferenceManifestGates(fixture.Input).AsDictionary();
            Must(
                results.TryGetValue(fixture.Gate, out var failed) && !failed,
                $"主窗口参考图负向夹具 {fixture.Name} 未让 {fixture.Gate} 变红。");
            Must(
                results
                    .Where(pair => !string.Equals(pair.Key, fixture.Gate, StringComparison.Ordinal))
                    .All(pair => pair.Value),
                $"主窗口参考图负向夹具 {fixture.Name} 误伤无关契约。");
        }

        return true;
    }

    public static bool RunVisualReferenceBaselineInterfaceBehavior()
    {
        const int width = 4;
        const int height = 4;
        const string mainRelativePath = "references/main/main.png";
        const string floatingRelativePath = "references/floating/floating.png";
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "MinminCountdown.VisualReferenceBaseline",
            Guid.NewGuid().ToString("N"));
        var mainDirectory = Path.Combine(fixtureRoot, "references", "main");
        var floatingDirectory = Path.Combine(fixtureRoot, "references", "floating");
        Directory.CreateDirectory(mainDirectory);
        Directory.CreateDirectory(floatingDirectory);

        try
        {
            var opaquePng = CreateOpaqueVisualReferenceFixturePng(blue: 48);
            var alternateOpaquePng = CreateOpaqueVisualReferenceFixturePng(blue: 96);
            var transparentPng = CreateTransparentVisualReferenceFixturePng();
            var mainPath = Path.Combine(mainDirectory, "main.png");
            var floatingPath = Path.Combine(floatingDirectory, "floating.png");
            File.WriteAllBytes(mainPath, opaquePng);
            File.WriteAllBytes(floatingPath, transparentPng);

            var mainCapture = new VisualReferenceManifestCapture(
                "main",
                mainRelativePath,
                width,
                height,
                Convert.ToHexString(SHA256.HashData(opaquePng)),
                new VisualReferenceAlphaMetrics(255, 255, 0, 0, width * height),
                new VisualReferenceContentRoi(0, 0, 0, width, height, width * height),
                VisualReferencePixelMetrics.Zero);
            var mainRequest = new MainVisualReferenceRequest(
                new VisualReferenceFileRequest(
                    fixtureRoot,
                    mainDirectory,
                    new[] { mainRelativePath },
                    new[] { mainCapture },
                    new[] { new VisualReferenceActualCapture("main", opaquePng) },
                    0,
                    0,
                    0,
                    0),
                width,
                height,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true);

            var floatingAlpha = new VisualReferenceAlphaMetrics(0, 255, 11, 12, 4);
            var floatingRoi = new VisualReferenceContentRoi(32, 1, 1, 2, 2, 4);
            var floatingCapture = new VisualReferenceManifestCapture(
                "floating",
                floatingRelativePath,
                width,
                height,
                Convert.ToHexString(SHA256.HashData(transparentPng)),
                floatingAlpha,
                floatingRoi,
                VisualReferencePixelMetrics.Zero);
            var floatingFiles = new VisualReferenceFileRequest(
                fixtureRoot,
                floatingDirectory,
                new[] { floatingRelativePath },
                new[] { floatingCapture },
                new[] { new VisualReferenceActualCapture("floating", transparentPng) },
                0,
                0,
                0,
                0);
            var floatingRequest = new FloatingVisualReferenceRequest(
                floatingFiles,
                width,
                height,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true);

            var mainResult = VisualReferenceBaseline.Main.Validate(mainRequest);
            var floatingResult = VisualReferenceBaseline.Floating.Validate(floatingRequest);
            Must(mainResult.AllPassed, "主窗口视觉基线 module 未通过正常文件集。");
            Must(floatingResult.AllPassed, "悬浮窗视觉基线 module 未通过正常文件集。");
            Must(
                floatingResult.Measurements.Single().ActualContentRoi == floatingRoi,
                "悬浮窗视觉基线 module 未按 alpha > 32 收缩内容 ROI。");

            var escapingPath = "../escape.png";
            var escapingResult = VisualReferenceBaseline.Main.Validate(
                mainRequest with
                {
                    Files = mainRequest.Files with
                    {
                        ExpectedCapturePaths = new[] { escapingPath },
                        ManifestCaptures = new[] { mainCapture with { Path = escapingPath } }
                    }
                });
            Must(
                !escapingResult.Gates.ManifestStructureValid &&
                escapingResult.Diagnostics.Any(diagnostic =>
                    diagnostic.Contains("逃出源码根目录", StringComparison.Ordinal)),
                "视觉基线 module 未拒绝逃出源码根目录的路径。");

            File.Delete(mainPath);
            Must(
                !VisualReferenceBaseline.Main.Validate(mainRequest).Gates.ExpectedCaptureFilesPresent,
                "视觉基线 module 未让缺失 PNG 的文件门禁失败。");
            File.WriteAllBytes(mainPath, opaquePng);

            var orphanPath = Path.Combine(mainDirectory, "orphan.png");
            File.WriteAllBytes(orphanPath, opaquePng);
            Must(
                !VisualReferenceBaseline.Main.Validate(mainRequest).Gates.NoOrphanPngFiles,
                "视觉基线 module 未让孤儿 PNG 的 inventory 门禁失败。");
            File.Delete(orphanPath);

            File.WriteAllBytes(mainPath, alternateOpaquePng);
            Must(
                !VisualReferenceBaseline.Main.Validate(mainRequest).Gates.PngHashesMatch,
                "视觉基线 module 未让合法但不同的 PNG 触发哈希失败。");
            File.WriteAllBytes(mainPath, opaquePng);

            var transparentMainResult = VisualReferenceBaseline.Main.Validate(
                new MainVisualReferenceRequest(
                    floatingFiles,
                    width,
                    height,
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    true));
            Must(
                !transparentMainResult.Gates.AlphaMetricsMatch,
                "主窗口视觉基线 module 未拒绝含透明像素的帧。");
            return true;
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static byte[] CreateOpaqueVisualReferenceFixturePng(byte blue)
    {
        var pixels = new byte[4 * 4 * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = 112;
            pixels[offset + 2] = 176;
            pixels[offset + 3] = byte.MaxValue;
        }
        return EncodeVisualReferenceFixturePng(pixels);
    }

    private static byte[] CreateTransparentVisualReferenceFixturePng()
    {
        var pixels = new byte[4 * 4 * 4];
        pixels[3] = 32;
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
        {
            var offset = ((y * 4) + x) * 4;
            pixels[offset] = 48;
            pixels[offset + 1] = 112;
            pixels[offset + 2] = 176;
            pixels[offset + 3] = byte.MaxValue;
        }
        return EncodeVisualReferenceFixturePng(pixels);
    }

    private static byte[] EncodeVisualReferenceFixturePng(byte[] pixels)
    {
        var source = BitmapSource.Create(
            4,
            4,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            4 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static MainStressCountdownTitleGateResults EvaluateMainStressCountdownTitleGates(
        MainStressCountdownTitleGateInputs input) =>
        new(
            input.ExactTitleRendered,
            input.TextIsSingleLineAndUntrimmed &&
            input.TextFits &&
            input.FullyVisibleInViewport &&
            input.SeparatedFromRemaining &&
            input.SeparatedFromActions);

    public static bool RunMainStressCountdownTitleGateIsolationBehavior()
    {
        var passing = new MainStressCountdownTitleGateInputs(
            true,
            true,
            true,
            true,
            true,
            true);
        Must(
            EvaluateMainStressCountdownTitleGates(passing).AllPassed,
            "主窗口 Stress 标题全通过夹具被错误拒绝。");

        var titleMissing = EvaluateMainStressCountdownTitleGates(
            passing with { ExactTitleRendered = false });
        Must(
            !titleMissing.ExactTitleRendered && titleMissing.LayoutSafe,
            "主窗口 Stress 标题缺失负夹具未单独让精确文本门禁变红。");

        var truncatedOrOverlapping = EvaluateMainStressCountdownTitleGates(
            passing with
            {
                TextIsSingleLineAndUntrimmed = false,
                SeparatedFromActions = false
            });
        Must(
            truncatedOrOverlapping.ExactTitleRendered && !truncatedOrOverlapping.LayoutSafe,
            "主窗口 Stress 标题截断或重叠负夹具未单独让布局门禁变红。");

        var remainingOverlap = EvaluateMainStressCountdownTitleGates(
            passing with { SeparatedFromRemaining = false });
        Must(
            remainingOverlap.ExactTitleRendered && !remainingOverlap.LayoutSafe,
            "主窗口 Stress 标题与剩余时间重叠负夹具未单独让布局门禁变红。");

        return true;
    }

    private static void VerifyOnlyFloatingReferenceManifestGateFails(
        string fixtureName,
        FloatingReferenceManifestGateInputs input,
        string expectedFailure)
    {
        var results = EvaluateFloatingReferenceManifestGates(input).AsDictionary();
        Must(
            results.TryGetValue(expectedFailure, out var failed) && !failed,
            $"悬浮窗参考图负向夹具 {fixtureName} 未让 {expectedFailure} 变红。");
        Must(
            results
                .Where(pair => !string.Equals(pair.Key, expectedFailure, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"悬浮窗参考图负向夹具 {fixtureName} 误伤无关契约。");
    }

    public static bool RunFloatingQuickAddThemeResourceIsolationBehavior()
    {
        var passingChecks = FloatingWindow.RequiredQuickAddThemeChecks.ToDictionary(
            name => name,
            _ => true,
            StringComparer.Ordinal);
        Must(
            new FloatingQuickAddThemeProbeResult(
                passingChecks,
                Array.Empty<FloatingQuickAddThemeProbeDiagnostic>()).Passed,
            "悬浮窗快速新增局部主题全通过夹具被错误拒绝。");

        foreach (var target in FloatingWindow.RequiredQuickAddThemeChecks)
        {
            var failingChecks = new Dictionary<string, bool>(passingChecks, StringComparer.Ordinal)
            {
                [target] = false
            };
            var result = new FloatingQuickAddThemeProbeResult(
                failingChecks,
                [new FloatingQuickAddThemeProbeDiagnostic(
                    target,
                    "SyntheticControl",
                    "CanvasBrush",
                    "#FFFFFDFC",
                    "#FF473323",
                    false,
                    false,
                    false,
                    true,
                    true,
                    string.Empty,
                    string.Empty)]);
            Must(
                !result.Passed &&
                result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal) &&
                result.Diagnostics.Single().Check == target,
                $"悬浮窗快速新增局部主题负向夹具未让 {target} 单独失败。");
        }

        return true;
    }

    public static bool RunFloatingQuickAddPaletteBindingIsolationBehavior()
    {
        var day = FloatingQuickAddPaletteBindings.Expected();
        Must(
            FloatingQuickAddPaletteBindings.Evaluate(day).Passed,
            "悬浮窗快速新增日间 palette 固定映射未通过。");

        var historicalMiswirings = new[]
        {
            (
                Mapping: day with { InputSurfaceResourceKey = "FloatingDayErrorBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.InputSurfaceMatches)),
            (
                Mapping: day with { InputTextResourceKey = "FloatingDayNoteInputBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.InputTextMatches)),
            (
                Mapping: day with { ErrorTextResourceKey = "FloatingDayNoteInputTextBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.ErrorTextMatches))
        };
        foreach (var fixture in historicalMiswirings)
        {
            var failing = FloatingQuickAddPaletteBindings.Evaluate(fixture.Mapping);
            Must(
                !failing.Passed &&
                failing.FailedChecks.SequenceEqual([fixture.FailedCheck], StringComparer.Ordinal),
                $"悬浮窗快速新增历史串线未只击穿 {fixture.FailedCheck}。");
        }

        return true;
    }

    public static FloatingPrimaryCountdownContrastGateResults EvaluateFloatingPrimaryCountdownContrastGates(
        FloatingPrimaryCountdownContrastGateInputs input) =>
        new(
            input.ContrastRatios.Count == 3 && input.ContrastRatios.All(ratio => ratio >= 3),
            input.AccentSeparatedFromPlushGlow);

    public static bool RunFloatingPrimaryCountdownContrastGateIsolationBehavior()
    {
        var passing = new FloatingPrimaryCountdownContrastGateInputs(
            new[] { 3.96, 3.96, 3.96 },
            true);
        var passingResult = EvaluateFloatingPrimaryCountdownContrastGates(passing);
        Must(
            passingResult.TextContrast && passingResult.AccentSeparatedFromPlushGlow,
            "悬浮窗主倒计时对比度与语义分离全通过夹具被错误拒绝。");

        var lowContrast = EvaluateFloatingPrimaryCountdownContrastGates(
            passing with { ContrastRatios = new[] { 3.96, 2.9, 3.96 } });
        Must(
            !lowContrast.TextContrast && lowContrast.AccentSeparatedFromPlushGlow,
            "主倒计时低对比度负向夹具未单独让对比度门禁变红。");

        var sharedAccentToken = EvaluateFloatingPrimaryCountdownContrastGates(
            passing with { AccentSeparatedFromPlushGlow = false });
        Must(
            sharedAccentToken.TextContrast && !sharedAccentToken.AccentSeparatedFromPlushGlow,
            "主倒计时与玩偶共享 token 的负向夹具未单独让语义分离门禁变红。");

        return true;
    }

    public static AuditContrastGateResults EvaluateAuditContrastGates(AuditContrastGateInputs input) =>
        new(
            input.SourceLabelRatios.Count == 2 && input.SourceLabelRatios.All(ratio => ratio >= 4.5),
            input.SourceUsesInkBrush,
            input.SourceEmphasisPreserved,
            input.DialogRatios.Count == 2 && input.DialogRatios.All(ratio => ratio >= 4.5),
            input.FocusRatios.Count == 3 && input.FocusRatios.All(ratio => ratio >= 3));

    public static bool RunAuditContrastGateIsolationBehavior()
    {
        var passing = new AuditContrastGateInputs(
            new[] { 11.49, 10.0 },
            true,
            true,
            new[] { 6.05, 6.05 },
            new[] { 8.1, 8.4, 8.2 });
        var passingResult = EvaluateAuditContrastGates(passing);
        Must(passingResult.AllPassed, "审计对比度全通过夹具被错误拒绝。");

        var sourceLow = EvaluateAuditContrastGates(
            passing with { SourceLabelRatios = new[] { 1.90, 10.0 } });
        Must(
            !sourceLow.SourceLabelContrast &&
            sourceLow.DialogContrast &&
            sourceLow.FocusContrast,
            "来源标签不达标时未单独触发对比度负向门。");

        var dialogLow = EvaluateAuditContrastGates(
            passing with { DialogRatios = new[] { 3.85, 6.05 } });
        Must(
            dialogLow.SourceLabelContrast &&
            !dialogLow.DialogContrast &&
            dialogLow.FocusContrast,
            "系统对话框主按钮不达标时未单独触发对比度负向门。");

        var focusLow = EvaluateAuditContrastGates(
            passing with { FocusRatios = new[] { 1.90, 8.4, 8.2 } });
        Must(
            focusLow.SourceLabelContrast &&
            focusLow.DialogContrast &&
            !focusLow.FocusContrast,
            "键盘焦点环不达标时未单独触发对比度负向门。");

        return true;
    }

    public static FloatingSecondaryRemainingGateResults EvaluateFloatingSecondaryRemainingGates(
        FloatingSecondaryRemainingGateInputs input) =>
        new(
            input.ExpectedTexts.Count == 5 &&
                input.FormattedTexts.SequenceEqual(input.ExpectedTexts, StringComparer.Ordinal) &&
                input.RenderedTexts.SequenceEqual(input.ExpectedTexts, StringComparer.Ordinal),
            input.HasExpectedSampleCount &&
                input.ColumnsAre194Pixels &&
                input.TextIsRightAligned &&
                input.TextIsSingleLineAndUntrimmed &&
                input.TextIsFullyVisible &&
                input.UsesTabularNumerals &&
                input.FontSizingMatches);

    public static bool RunFloatingSecondaryRemainingGateIsolationBehavior()
    {
        var expected = new[]
        {
            "4 天",
            "4 天 13 小时",
            "4 天 13 小时 18 分钟",
            "4 天 13 小时 18 分钟 59 秒",
            "2000000 天 00 小时 00 分钟 00 秒"
        };
        var passing = new FloatingSecondaryRemainingGateInputs(
            expected,
            expected,
            expected,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        var passingResult = EvaluateFloatingSecondaryRemainingGates(passing);
        Must(
            passingResult.UnitSpacingMatches && passingResult.FixedColumnLayoutMatches,
            "悬浮窗次级倒计时全通过夹具被错误拒绝。");

        var spacingRemoved = EvaluateFloatingSecondaryRemainingGates(
            passing with
            {
                RenderedTexts = expected
                    .Select(text => text.Replace(" ", string.Empty, StringComparison.Ordinal))
                    .ToArray()
            });
        Must(
            !spacingRemoved.UnitSpacingMatches && spacingRemoved.FixedColumnLayoutMatches,
            "悬浮窗次级倒计时去空格负向夹具未单独变红。");

        var brokenLayout = EvaluateFloatingSecondaryRemainingGates(
            passing with { ColumnsAre194Pixels = false });
        Must(
            brokenLayout.UnitSpacingMatches && !brokenLayout.FixedColumnLayoutMatches,
            "悬浮窗次级倒计时固定列负向夹具未单独变红。");

        return true;
    }

    // #157 修复轮 1（reviewer CONCERN）：确定性视口右缘算术的唯一实现——现役捕获
    // （ValidationReport.CaptureFloatingViewportState）与反例门禁共同消费它；
    // 删除「− 设计命中轨」会使可见态负向夹具（越界 386.3 必须红）变红。
    public static double FloatingViewportDesignedRight(
        double scrollerSlotRight,
        double designedScrollBarRail,
        bool scrollBarVisible) =>
        scrollerSlotRight - (scrollBarVisible ? designedScrollBarRail : 0d);

    // #157：悬浮视口几何门禁——「卡右缘 ≤ 视口右缘 + 0.25」的比较基准必须是确定性口径：
    // 滚动内容槽右缘（只由 520 外壳固定边距决定）扣除 §10 设计命中轨（滚动条可见时），
    // 不消费随系统滚动条度量漂移的滚动条列宽（旧 ViewportWidth 口径在 100%/125% 显示
    // 缩放下分别 0.05 过 / 0.55 红，§11 漂移族）。用会失败的反例保住门禁语义。
    public static bool EvaluateFloatingViewportGeometryGate(FloatingViewportGeometryGateInputs input)
    {
        var rightLimit = FloatingViewportDesignedRight(
            input.ScrollerSlotRight,
            input.DesignedScrollBarRail,
            input.ScrollBarVisible) + 0.25;
        return SurfaceRightFits(input.MaximumCardRight, rightLimit) &&
               SurfaceRightFits(input.MaximumDateStampRight, rightLimit) &&
               SurfaceRightFits(input.NoteEntryRight, rightLimit);
    }

    private static bool SurfaceRightFits(double? right, double limit) =>
        right is null || right <= limit;

    public static bool RunFloatingViewportGeometryGateIsolationBehavior()
    {
        // 现役真实几何（#157 插桩实测，任意显示缩放下稳定）：槽右 396 = 51 + 345（520 外壳
        // 固定边距），设计命中轨 10，便签卡右 379.2、印记右 342.4、快速新增右 51。
        var passing = new FloatingViewportGeometryGateInputs(396, 10, true, 379.2, 342.4, 51);
        Must(EvaluateFloatingViewportGeometryGate(passing), "悬浮视口几何全通过夹具被错误拒绝。");
        var visibleBoundary = 396 - 10 + 0.25;
        Must(
            EvaluateFloatingViewportGeometryGate(passing with { MaximumCardRight = visibleBoundary }),
            "设计视口边界容差内的现役几何被错误拒绝。");
        Must(
            !EvaluateFloatingViewportGeometryGate(
                passing with { MaximumCardRight = visibleBoundary + 0.01 }),
            "卡片越过设计视口边界未被拒绝（真几何回归必须红）。");
        Must(
            !EvaluateFloatingViewportGeometryGate(
                passing with { MaximumDateStampRight = visibleBoundary + 0.01 }),
            "日期印记越过设计视口边界未被拒绝。");
        Must(
            !EvaluateFloatingViewportGeometryGate(
                passing with { NoteEntryRight = visibleBoundary + 0.01 }),
            "快速新增越过设计视口边界未被拒绝。");
        // 滚动条隐藏（零扣除）时设计边界放大到滚动内容槽右：越进命中轨区不算回归，
        // 越出槽右仍是回归——边界语义与扣除规则绑定，不随系统度量变化。
        Must(
            EvaluateFloatingViewportGeometryGate(
                passing with { ScrollBarVisible = false, MaximumCardRight = visibleBoundary + 0.01 }),
            "滚动条隐藏时设计边界内的几何被错误拒绝。");
        Must(
            !EvaluateFloatingViewportGeometryGate(
                passing with { ScrollBarVisible = false, MaximumCardRight = 396.3 }),
            "滚动条隐藏时卡片越过滚动内容槽未被拒绝。");
        return true;
    }

    public static FloatingScrollBarGateResults EvaluateFloatingScrollBarGates(
        FloatingScrollBarGateInputs input) =>
        new(
            input.LocalStylePresent && input.MainWindowStyleUnaffected,
            input.HitTargetWidth >= 10,
            input.NormalThumbWidth is >= 3.5 and <= 4.5 &&
                input.HoverThumbWidth is >= 3.5 and <= 4.5 &&
                input.DragThumbWidth is >= 3.5 and <= 4.5 &&
                input.NormalThumbOpacity is > 0 and <= 0.6 &&
                input.HoverThumbOpacity > input.NormalThumbOpacity &&
                input.DragThumbOpacity > input.HoverThumbOpacity &&
                input.DragThumbOpacity <= 1,
            input.ThemeTokensMatch,
            input.NormalDoesNotObscureContent &&
                input.HoverDoesNotObscureContent &&
                input.DragDoesNotObscureContent &&
                input.ContentSafeAreaUnchanged,
            input.WheelChangesOffset && input.DragChangesOffset);

    public static bool RunFloatingScrollBarGateIsolationBehavior()
    {
        var passing = new FloatingScrollBarGateInputs(
            true,
            true,
            10,
            4,
            4,
            4,
            0.52,
            0.78,
            0.96,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        var passingResult = EvaluateFloatingScrollBarGates(passing);
        Must(
            passingResult.AllPassed,
            "悬浮窗滚动条全通过夹具被错误拒绝。");

        VerifyOnlyFloatingScrollBarGateFails(
            passing with { LocalStylePresent = false },
            nameof(FloatingScrollBarGateResults.LocalStyleScoped));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { HitTargetWidth = 8 },
            nameof(FloatingScrollBarGateResults.HitTargetUsable));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { NormalThumbWidth = 7 },
            nameof(FloatingScrollBarGateResults.VisualWeightProgression));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { NormalDoesNotObscureContent = false },
            nameof(FloatingScrollBarGateResults.ContentRemainsSafe));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { ContentSafeAreaUnchanged = false },
            nameof(FloatingScrollBarGateResults.ContentRemainsSafe));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { ThemeTokensMatch = false },
            nameof(FloatingScrollBarGateResults.UsesWarmThemeTokens));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { WheelChangesOffset = false },
            nameof(FloatingScrollBarGateResults.InputPathsWork));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { DragChangesOffset = false },
            nameof(FloatingScrollBarGateResults.InputPathsWork));
        return true;
    }

    private static void VerifyOnlyFloatingScrollBarGateFails(
        FloatingScrollBarGateInputs input,
        string expectedFailure)
    {
        var results = EvaluateFloatingScrollBarGates(input).AsDictionary();
        Must(
            results.TryGetValue(expectedFailure, out var failed) && !failed,
            $"悬浮窗滚动条负向夹具未让 {expectedFailure} 变红。");
        Must(
            results
                .Where(pair => !string.Equals(pair.Key, expectedFailure, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"悬浮窗滚动条负向夹具 {expectedFailure} 误伤无关契约。");
    }

    private static void VerifyOnlyTargetFails(
        UiQualityGateInputs input,
        string target)
    {
        var results = EvaluateUiQualityGates(input).AsDictionary();
        Must(
            results.TryGetValue(target, out var targetValue) && !targetValue,
            $"UI 质量门禁负向夹具未让 {target} 单独失败。");
        Must(
            results
                .Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"UI 质量门禁负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyCountdownEmptyStateGateFails(
        CountdownEmptyStateGateInputs input,
        string target)
    {
        var results = EvaluateCountdownEmptyStateGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"倒计时空态负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"倒计时空态负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyNoteVisualTokenGateFails(
        NoteVisualTokenGateInputs input,
        string target)
    {
        var results = EvaluateNoteVisualTokenGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"主窗口便签视觉合同负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"主窗口便签视觉合同负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyMainNoteCompletionGateFails(
        MainNoteCompletionGateInputs input,
        string target)
    {
        var results = EvaluateMainNoteCompletionGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"主窗口便签完成复选框负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"主窗口便签完成复选框负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyMainCountdownRemainingGateFails(
        MainCountdownRemainingGateInputs input,
        string target)
    {
        var results = EvaluateMainCountdownRemainingGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"主窗口倒计时剩余时间负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"主窗口倒计时剩余时间负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyCountdownIncrementalGateFails(
        CountdownIncrementalRefreshGateResults results,
        string target)
    {
        Must(!results.AllPassed && results.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"倒计时增量刷新负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlyCountdownReadabilityGateFails(
        CountdownReadabilityGateInputs input,
        string target)
    {
        var results = EvaluateCountdownReadabilityGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"倒计时可读性负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"倒计时可读性负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyCountdownEditingGateFails(
        CountdownEditingGateInputs input,
        string target)
    {
        var results = EvaluateCountdownEditingGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"倒计时编辑负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"倒计时编辑负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyConfirmationGateFails(
        ConfirmationDialogGateInputs input,
        string target)
    {
        var result = EvaluateConfirmationDialogGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"删除确认对话框负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlySecondaryWindowTypographyGateFails(
        SecondaryWindowTypographyGateInputs input,
        string target)
    {
        var result = EvaluateSecondaryWindowTypographyGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"次级窗口字号负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlyReminderCenterKeyboardGateFails(
        ReminderCenterKeyboardGateInputs input,
        string target)
    {
        var result = EvaluateReminderCenterKeyboardGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"提醒中心键盘负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlyShortcutGateFails(MainShortcutGateInputs input, string target) =>
        Must(!EvaluateMainShortcutGates(input).AllPassed &&
             EvaluateMainShortcutGates(input).FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"主窗口快捷键负向夹具未只击穿 {target}。");

    private static void VerifyOnlyMainKeyboardNavigationGateFails(
        MainKeyboardNavigationGateInputs input,
        string target)
    {
        var result = EvaluateMainKeyboardNavigationGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"主窗口键盘导航负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlyMainStructuralAnnouncementGateFails(
        MainStructuralAnnouncementGateInputs input,
        string target)
    {
        var result = EvaluateMainStructuralAnnouncementGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"主窗口结构变化通知负向夹具未只击穿 {target}。");
    }

    public static PersistenceFailureExerciseResult RunPersistenceFailures(
        DesktopWorkspace workspace,
        string settingsPath)
    {
        var blockedTemporaryPath = settingsPath + ".tmp";
        if (!Directory.Exists(blockedTemporaryPath))
        {
            throw new InvalidOperationException(
                "持久化失败 exercise 需要先把 settings.json.tmp 建成目录，避免在未阻断路径上写入。");
        }

        var before = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
        var countdownDraft = "持久化失败倒计时草稿";
        var preferenceDraft = "9999";
        var tagDraft = "持久化失败标签草稿";
        var selectedFilterBefore = workspace.Snapshot.Filter.SelectedTagIds.ToArray();

        var countdownResult = workspace.Apply(new SaveCountdownChange(
            null,
            countdownDraft,
            workspace.Snapshot.UtcNow.AddDays(10),
            Array.Empty<string>()));
        var countdownStatePreserved =
            !countdownResult.Succeeded &&
            string.Equals(
                before,
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                StringComparison.Ordinal);

        var preferences = workspace.Snapshot.Settings.Preferences;
        var preferenceResult = workspace.Apply(new SavePreferencesChange(
            preferences.DisplayPrecision,
            9999m,
            preferences.WorkStart,
            preferences.WorkEnd));
        var preferenceStatePreserved =
            !preferenceResult.Succeeded &&
            string.Equals(
                before,
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                StringComparison.Ordinal);

        var tagResult = workspace.Apply(new CreateTagChange(tagDraft, "#F4B6A6"));
        var tagStatePreserved =
            !tagResult.Succeeded &&
            tagResult.CreatedTag is null &&
            string.Equals(
                before,
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                StringComparison.Ordinal);
        var filterPreserved = selectedFilterBefore.SequenceEqual(
            workspace.Snapshot.Filter.SelectedTagIds,
            StringComparer.OrdinalIgnoreCase);

        return new PersistenceFailureExerciseResult(
            countdownStatePreserved,
            preferenceStatePreserved,
            countdownDraft == "持久化失败倒计时草稿",
            preferenceDraft == "9999",
            tagStatePreserved,
            tagDraft == "持久化失败标签草稿",
            filterPreserved,
            Directory.Exists(blockedTemporaryPath));
    }

    public static TagInteractionExerciseResult RunTagInteractions(DesktopWorkspace workspace)
    {
        var initial = workspace.Snapshot.Settings;
        var initialCountdownCount = initial.Countdowns.Count;
        var initialNoteCount = initial.Notes.Count;
        var initialTagCount = initial.Tags.Count;

        var projectTagResult = MustApply(
            workspace,
            new CreateTagChange("很长的项目协作标签名称", "#F4B6A6"));
        var projectTag = projectTagResult.CreatedTag;
        Must(projectTag is not null, "未取得新建项目标签。");
        var countdownCountAfterSearchCreate = workspace.Snapshot.Settings.Countdowns.Count;
        var collaborationTag = MustApply(
            workspace,
            new CreateTagChange("协作", "#A8D5BA")).CreatedTag;
        Must(collaborationTag is not null, "未取得新建协作标签。");
        var removableTag = MustApply(
            workspace,
            new CreateTagChange("待删除", "#A8C7E8")).CreatedTag;
        Must(removableTag is not null, "未取得待删除标签。");

        var targetBase = new DateTimeOffset(2095, 1, 1, 0, 0, 0, TimeSpan.Zero);
        MustApply(workspace, new SaveCountdownChange(
            null,
            "标签验证甲",
            targetBase,
            new[] { projectTag!.Id, removableTag!.Id }));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "标签验证乙",
            targetBase.AddDays(1),
            new[] { projectTag.Id, collaborationTag!.Id }));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "无标签验证",
            targetBase.AddDays(2),
            Array.Empty<string>()));

        MustApply(workspace, new SaveNoteChange(
            null,
            "浮窗继承标签便签",
            new DateTime(2032, 2, 3),
            false,
            new[] { projectTag.Id },
            ReminderSchedule.CreateRepeating(workspace.Snapshot.UtcNow.AddMinutes(30), 30)));
        var inheritedNote = workspace.Snapshot.Settings.Notes
            .Last(note => note.Text == "浮窗继承标签便签");
        MustApply(workspace, new SaveNoteChange(
            null,
            "浮窗清空标签便签",
            new DateTime(2032, 2, 4),
            false,
            Array.Empty<string>(),
            NoReminder.Instance));
        var untaggedNote = workspace.Snapshot.Settings.Notes
            .Last(note => note.Text == "浮窗清空标签便签");

        var anyFilter = new WorkspaceFilter();
        anyFilter.SelectedTagIds.Add(projectTag.Id);
        var snapshot = workspace.Snapshot;
        var anyMatches = snapshot.Settings.Countdowns.Count(record => anyFilter.Matches(record, snapshot.UtcNow));
        anyFilter.SelectedTagIds.Add(collaborationTag.Id);
        anyFilter.MatchMode = TagMatchMode.All;
        var allMatches = snapshot.Settings.Countdowns.Count(record => anyFilter.Matches(record, snapshot.UtcNow));
        var anyAllFilter = anyMatches >= 2 && allMatches == 1;

        var untaggedFilter = new WorkspaceFilter
        {
            UntaggedOnly = true,
            CountdownStatus = CountdownStatusFilter.Active
        };
        var untaggedAndStatus = snapshot.Settings.Countdowns.Any(
            record => record.Name == "无标签验证" && untaggedFilter.Matches(record, snapshot.UtcNow));

        MustApply(workspace, new SetNoteCompletionChange(inheritedNote.Id, true));
        var completed = workspace.Snapshot.Settings.Notes.First(note => note.Id == inheritedNote.Id);
        var noteFilter = new WorkspaceFilter
        {
            NoteStatus = NoteStatusFilter.Completed,
            NoteSearch = "继承标签"
        };
        noteFilter.SelectedTagIds.Add(projectTag.Id);
        var noteStatusSearch =
            noteFilter.Matches(completed) &&
            completed.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase);
        MustApply(workspace, new SetNoteCompletionChange(inheritedNote.Id, false));

        var exactDateFilter = new WorkspaceFilter { NoteDate = "2032-02-03" };
        var undatedFilter = new WorkspaceFilter { UndatedNotesOnly = true };
        var noteDateBehavior =
            exactDateFilter.Matches(workspace.Snapshot.Settings.Notes.First(note => note.Id == inheritedNote.Id)) &&
            !exactDateFilter.Matches(untaggedNote) &&
            workspace.Snapshot.Settings.Notes.Where(note => note.Date is null).All(undatedFilter.Matches);

        MustApply(workspace, new SaveNoteChange(
            inheritedNote.Id,
            "已编辑的浮窗继承标签便签",
            new DateTime(2032, 3, 5),
            false,
            new[] { collaborationTag.Id },
            inheritedNote.Reminder));
        var edited = workspace.Snapshot.Settings.Notes.First(note => note.Id == inheritedNote.Id);
        var editTags =
            edited.TagIds.Count == 1 &&
            edited.TagIds.Contains(collaborationTag.Id, StringComparer.OrdinalIgnoreCase);
        var editDate = edited.Date == "2032-03-05";

        MustApply(workspace, new RenameTagChange(projectTag.Id, "项目协作", projectTag.Color));
        MustApply(workspace, new DeleteTagChange(removableTag.Id));
        var final = workspace.Snapshot.Settings;
        var renamed = final.Tags.Any(tag =>
            tag.Id == projectTag.Id &&
            tag.Name == "项目协作");
        var deleted = final.Tags.All(tag => tag.Id != removableTag.Id);
        var recordsPreserved =
            final.Countdowns.Count == initialCountdownCount + 3 &&
            final.Notes.Count == initialNoteCount + 2;
        var detachedOnly =
            final.Countdowns
                .Where(record => record.Name == "标签验证甲")
                .All(record =>
                    record.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase) &&
                    !record.TagIds.Contains(removableTag.Id, StringComparer.OrdinalIgnoreCase));

        var selector = new TagSelector();
        selector.SetTags(final.Tags);
        selector.SetSelection(new[] { projectTag.Id, collaborationTag.Id });
        var summaryCompact = selector.SummaryText.Length <= 18 && selector.SummaryText.Length > 0;
        var popupStartsClosed = !selector.IsPopupOpen;

        var creationAndAssociation =
            final.Tags.Count >= initialTagCount + 2 &&
            final.Countdowns.Any(record =>
                record.Name == "标签验证乙" &&
                record.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase) &&
                record.TagIds.Contains(collaborationTag.Id, StringComparer.OrdinalIgnoreCase));
        var floatingInherits =
            inheritedNote.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase);
        var floatingDate = inheritedNote.Date == "2032-02-03";
        var floatingOverride = untaggedNote.TagIds.Count == 0;
        var searchCreateAvoidsSubmit = countdownCountAfterSearchCreate == initialCountdownCount;

        return new TagInteractionExerciseResult(
            true,
            creationAndAssociation,
            anyAllFilter,
            summaryCompact,
            searchCreateAvoidsSubmit,
            untaggedAndStatus,
            noteStatusSearch,
            true,
            noteDateBehavior,
            floatingInherits,
            floatingDate,
            popupStartsClosed,
            floatingOverride,
            editTags,
            editDate,
            renamed && deleted && recordsPreserved && detachedOnly);
    }

    private static void Must(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static DesktopWorkspaceChangeResult MustApply(
        DesktopWorkspace workspace,
        DesktopWorkspaceChange change)
    {
        var result = workspace.Apply(change);
        Must(result.Succeeded, result.Error);
        return result;
    }

    private static void WriteStartupRequest(
        string path,
        string launchToken,
        string acceptanceState,
        bool? acceptanceReadOnly = null,
        DateTimeOffset? requestedAtUtc = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            launchToken,
            requestedAtUtc = (requestedAtUtc ??
                new DateTimeOffset(2026, 7, 20, 2, 16, 0, TimeSpan.Zero)).ToString("o"),
            acceptanceReadOnly = acceptanceReadOnly ??
                acceptanceState is "active" or "released" or "ready",
            acceptanceState
        });
        File.WriteAllText(path, payload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string CreateHolidayCalendarPayload(
        int year,
        bool includeMaySixthWorkday = true)
    {
        var firstDay = new DateTime(year, 1, 1);
        var dayCount = DateTime.IsLeapYear(year) ? 366 : 365;
        var days = Enumerable.Range(0, dayCount)
            .Select(offset =>
            {
                var date = firstDay.AddDays(offset);
                return new
                {
                    date = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    is_workday = includeMaySixthWorkday && date == new DateTime(year, 5, 6) ||
                                 date == new DateTime(year, 5, 7)
                };
            })
            .ToArray();
        return JsonSerializer.Serialize(new { days });
    }

    private static void MustThrow<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteSettingsFiles(string settingsPath)
    {
        foreach (var suffix in new[]
                 {
                     "", ".tmp", ".backup1", ".backup2",
                     ".backup2.tmp", ".backup2.rollback"
                 })
        {
            DeleteFileIfPresent(settingsPath + suffix);
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class AclDeniedMainSettingsFileOperations : ISettingsFileOperations
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);
        private readonly string _settingsPath;
        private bool _denialActive = true;

        public AclDeniedMainSettingsFileOperations(string settingsPath)
        {
            _settingsPath = Path.GetFullPath(settingsPath);
        }

        public bool DenialClearedByReplace { get; private set; }

        public SettingsPathState GetPathState(string path) =>
            IsDenied(path)
                ? throw new UnauthorizedAccessException("验证注入的设置路径状态探测 ACL 拒绝。")
                : File.Exists(path)
                    ? SettingsPathState.RegularFile
                    : Directory.Exists(path)
                        ? SettingsPathState.Occupied
                        : SettingsPathState.Absent;

        public bool FileExists(string path) => File.Exists(path);

        public string ReadUtf8Text(string path) =>
            IsDenied(path)
                ? throw new UnauthorizedAccessException("验证注入的设置读取 ACL 拒绝。")
                : File.ReadAllText(path, Utf8NoBom);

        public byte[] ReadFileBytes(string path) =>
            IsDenied(path)
                ? throw new UnauthorizedAccessException("验证注入的设置字节读取 ACL 拒绝。")
                : File.ReadAllBytes(path);

        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

        public void WriteUtf8NoBomText(string path, string content) =>
            File.WriteAllText(path, content, Utf8NoBom);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            File.Copy(sourcePath, destinationPath, overwrite);

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
            File.Move(sourcePath, destinationPath, overwrite);

        public void ReplaceFile(
            string sourcePath,
            string destinationPath,
            string? backupPath,
            bool ignoreMetadataErrors)
        {
            File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);
            if (string.Equals(Path.GetFullPath(destinationPath), _settingsPath, StringComparison.OrdinalIgnoreCase) &&
                backupPath is not null &&
                backupPath.Contains(".recovery-rollback-", StringComparison.Ordinal))
            {
                // 恢复替换生成的新主文件继承父目录 ACL，原文件级拒绝随之消失。
                _denialActive = false;
                DenialClearedByReplace = true;
            }
        }

        public void DeleteFile(string path) => File.Delete(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteNewFileAndFlush(string path, ReadOnlySpan<byte> content)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        private bool IsDenied(string path) =>
            _denialActive &&
            string.Equals(Path.GetFullPath(path), _settingsPath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class WorkspaceValidationClock : IClock
    {
        private readonly DateTimeOffset _utcNow;

        public WorkspaceValidationClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public int ReadCount { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                ReadCount++;
                return _utcNow;
            }
        }
    }

    private sealed class AdvancingValidationClock : IClock
    {
        public AdvancingValidationClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow.ToUniversalTime();
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class CountingAdvancingValidationClock : IClock
    {
        private DateTimeOffset _utcNow;

        public CountingAdvancingValidationClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public int ReadCount { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                ReadCount++;
                return _utcNow;
            }
            set => _utcNow = value.ToUniversalTime();
        }
    }
}

internal sealed record PersistenceFailureExerciseResult(
    bool CountdownStatePreserved,
    bool PreferenceStatePreserved,
    bool CountdownDraftRetained,
    bool PreferenceDraftRetained,
    bool TagStatePreserved,
    bool TagDraftRetained,
    bool TagFilterRetained,
    bool TemporaryPathRemainsDirectory)
{
    public static PersistenceFailureExerciseResult NotRun { get; } =
        new(false, false, false, false, false, false, false, false);
}

internal sealed record TagInteractionExerciseResult(
    bool Executed,
    bool CreationAndAssociation,
    bool AnyAllFilter,
    bool EditorSummaryIsCompact,
    bool SearchCreateAvoidsDefaultSubmit,
    bool UntaggedAndCountdownStatus,
    bool NoteStatusSearchAndCompletion,
    bool NoteDateFilterControls,
    bool NoteDateFilterBehavior,
    bool FloatingNoteInheritsTags,
    bool FloatingNoteDateViaPicker,
    bool FloatingNotePopupOnDemand,
    bool FloatingNoteCanOverrideTags,
    bool NoteEditingPersistsTags,
    bool NoteEditingPersistsDate,
    bool RenameAndDeleteDetachOnly)
{
    public static TagInteractionExerciseResult NotRun { get; } =
        new(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
}

internal sealed record UiQualityGateInputs(
    bool MainReferenceDecorationScaling,
    bool MainReferenceDecorationLayerOrder,
    bool MainWorkspaceResourcesFollowTheme,
    int MainCountdownCardSampleCount,
    bool MainCountdownCardResourcesFollowTheme,
    int MainNoteCardSampleCount,
    bool MainNoteCardResourcesFollowTheme,
    bool FloatingContentSafeArea,
    bool FloatingTitleUsesSemiBoldWeight,
    bool FloatingNoteInputWraps,
    bool FloatingNoteInputScrolls);

internal sealed record FloatingPrimaryCountdownContrastGateInputs(
    IReadOnlyList<double> ContrastRatios,
    bool AccentSeparatedFromPlushGlow);

internal sealed record FloatingPrimaryCountdownContrastGateResults(
    bool TextContrast,
    bool AccentSeparatedFromPlushGlow);

internal sealed record AuditContrastGateInputs(
    IReadOnlyList<double> SourceLabelRatios,
    bool SourceUsesInkBrush,
    bool SourceEmphasisPreserved,
    IReadOnlyList<double> DialogRatios,
    IReadOnlyList<double> FocusRatios);

internal sealed record AuditContrastGateResults(
    bool SourceLabelContrast,
    bool SourceUsesInkBrush,
    bool SourceEmphasisPreserved,
    bool DialogContrast,
    bool FocusContrast)
{
    public bool AllPassed =>
        SourceLabelContrast &&
        SourceUsesInkBrush &&
        SourceEmphasisPreserved &&
        DialogContrast &&
        FocusContrast;
}

internal sealed record FloatingReferenceManifestGateInputs(
    bool ManifestStructureValid,
    bool ExpectedCaptureFilesPresent,
    bool CapturePathsUnique,
    bool NoOrphanPngFiles,
    bool CanvasDimensionsMatch,
    bool ScenarioThemePairsUnique,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool PngHashesMatch,
    bool AlphaMetricsMatch,
    bool ContentRoiMatches,
    bool PixelMetricsWithinThresholds);

internal sealed record FloatingReferenceManifestGateResults(
    bool ManifestStructureValid,
    bool ExpectedCaptureFilesPresent,
    bool CapturePathsUnique,
    bool NoOrphanPngFiles,
    bool CanvasDimensionsMatch,
    bool ScenarioThemePairsUnique,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool PngHashesMatch,
    bool AlphaMetricsMatch,
    bool ContentRoiMatches,
    bool PixelMetricsWithinThresholds)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(ManifestStructureValid)] = ManifestStructureValid,
            [nameof(ExpectedCaptureFilesPresent)] = ExpectedCaptureFilesPresent,
            [nameof(CapturePathsUnique)] = CapturePathsUnique,
            [nameof(NoOrphanPngFiles)] = NoOrphanPngFiles,
            [nameof(CanvasDimensionsMatch)] = CanvasDimensionsMatch,
            [nameof(ScenarioThemePairsUnique)] = ScenarioThemePairsUnique,
            [nameof(SourceHashesMatch)] = SourceHashesMatch,
            [nameof(ExecutableHashMatches)] = ExecutableHashMatches,
            [nameof(PngHashesMatch)] = PngHashesMatch,
            [nameof(AlphaMetricsMatch)] = AlphaMetricsMatch,
            [nameof(ContentRoiMatches)] = ContentRoiMatches,
            [nameof(PixelMetricsWithinThresholds)] = PixelMetricsWithinThresholds
        };
}

internal sealed record FloatingReferenceFreshnessGateInputs(
    bool ExpectedStateBindingsMatch,
    bool CaptureIdsUnique,
    bool StateFingerprintsMatch,
    bool StateFingerprintsUnique,
    bool PngHashesUnique,
    bool TopBottomPixelsDiffer,
    bool LongNoteTypographyMatches,
    bool LongNoteLayoutValid);

internal sealed record FloatingReferenceFreshnessGateResults(
    bool ExpectedStateBindingsMatch,
    bool CaptureIdsUnique,
    bool StateFingerprintsMatch,
    bool StateFingerprintsUnique,
    bool PngHashesUnique,
    bool TopBottomPixelsDiffer,
    bool LongNoteTypographyMatches,
    bool LongNoteLayoutValid)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(ExpectedStateBindingsMatch)] = ExpectedStateBindingsMatch,
            [nameof(CaptureIdsUnique)] = CaptureIdsUnique,
            [nameof(StateFingerprintsMatch)] = StateFingerprintsMatch,
            [nameof(StateFingerprintsUnique)] = StateFingerprintsUnique,
            [nameof(PngHashesUnique)] = PngHashesUnique,
            [nameof(TopBottomPixelsDiffer)] = TopBottomPixelsDiffer,
            [nameof(LongNoteTypographyMatches)] = LongNoteTypographyMatches,
            [nameof(LongNoteLayoutValid)] = LongNoteLayoutValid
        };
}

internal sealed record MainReferenceManifestGateInputs(
    bool ManifestStructureValid,
    bool ExpectedCaptureFilesPresent,
    bool NoOrphanPngFiles,
    bool CaptureIdsUnique,
    bool CapturePathsUnique,
    bool ScenarioBindingsUnique,
    bool CanvasDimensionsMatch,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool PngHashesMatch,
    bool StateFingerprintsMatch,
    bool AlphaMetricsMatch,
    bool ContentRoiMatches,
    bool PixelMetricsWithinThresholds,
    bool StateFingerprintsUnique,
    bool PngHashesUnique);

internal sealed record MainReferenceManifestGateResults(
    bool ManifestStructureValid,
    bool ExpectedCaptureFilesPresent,
    bool NoOrphanPngFiles,
    bool CaptureIdsUnique,
    bool CapturePathsUnique,
    bool ScenarioBindingsUnique,
    bool CanvasDimensionsMatch,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool PngHashesMatch,
    bool StateFingerprintsMatch,
    bool AlphaMetricsMatch,
    bool ContentRoiMatches,
    bool PixelMetricsWithinThresholds,
    bool StateFingerprintsUnique,
    bool PngHashesUnique)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(ManifestStructureValid)] = ManifestStructureValid,
            [nameof(ExpectedCaptureFilesPresent)] = ExpectedCaptureFilesPresent,
            [nameof(NoOrphanPngFiles)] = NoOrphanPngFiles,
            [nameof(CaptureIdsUnique)] = CaptureIdsUnique,
            [nameof(CapturePathsUnique)] = CapturePathsUnique,
            [nameof(ScenarioBindingsUnique)] = ScenarioBindingsUnique,
            [nameof(CanvasDimensionsMatch)] = CanvasDimensionsMatch,
            [nameof(SourceHashesMatch)] = SourceHashesMatch,
            [nameof(ExecutableHashMatches)] = ExecutableHashMatches,
            [nameof(PngHashesMatch)] = PngHashesMatch,
            [nameof(StateFingerprintsMatch)] = StateFingerprintsMatch,
            [nameof(AlphaMetricsMatch)] = AlphaMetricsMatch,
            [nameof(ContentRoiMatches)] = ContentRoiMatches,
            [nameof(PixelMetricsWithinThresholds)] = PixelMetricsWithinThresholds,
            [nameof(StateFingerprintsUnique)] = StateFingerprintsUnique,
            [nameof(PngHashesUnique)] = PngHashesUnique
        };
}

internal sealed record MainStressCountdownTitleGateInputs(
    bool ExactTitleRendered,
    bool TextIsSingleLineAndUntrimmed,
    bool TextFits,
    bool FullyVisibleInViewport,
    bool SeparatedFromRemaining,
    bool SeparatedFromActions);

internal sealed record MainStressCountdownTitleGateResults(
    bool ExactTitleRendered,
    bool LayoutSafe)
{
    public bool AllPassed => ExactTitleRendered && LayoutSafe;
}

internal sealed record FloatingSecondaryRemainingGateInputs(
    IReadOnlyList<string> ExpectedTexts,
    IReadOnlyList<string> FormattedTexts,
    IReadOnlyList<string> RenderedTexts,
    bool HasExpectedSampleCount,
    bool ColumnsAre194Pixels,
    bool TextIsRightAligned,
    bool TextIsSingleLineAndUntrimmed,
    bool TextIsFullyVisible,
    bool UsesTabularNumerals,
    bool FontSizingMatches);

// #157：悬浮视口几何门禁输入（确定性视口右缘口径的隔离负向夹具与现役捕获共用）。
// null = 该表面不参与视口右缘比较（非便签工作区的卡/印记、隐藏的快速新增）。
internal sealed record FloatingViewportGeometryGateInputs(
    double ScrollerSlotRight,
    double DesignedScrollBarRail,
    bool ScrollBarVisible,
    double? MaximumCardRight,
    double? MaximumDateStampRight,
    double? NoteEntryRight);

internal sealed record FloatingSecondaryRemainingGateResults(
    bool UnitSpacingMatches,
    bool FixedColumnLayoutMatches);

internal sealed record MainCountdownRemainingGateInputs(
    IReadOnlyList<string> ExpectedTexts,
    IReadOnlyList<string> RenderedTexts,
    bool HasExpectedSampleCount,
    bool ColumnsAre300Pixels,
    bool ActualColumnsAre300Pixels,
    bool TextIsRightAligned,
    bool TextIsSingleLineAndUntrimmed,
    bool TextIsFullyVisible,
    bool UsesTabularNumerals,
    bool PrecisionSamplesAreSaved,
    bool FontSizingMatches);

internal sealed record MainCountdownRemainingGateResults(
    bool UnitSpacingMatches,
    bool FixedColumnLayoutMatches)
{
    public bool AllPassed => UnitSpacingMatches && FixedColumnLayoutMatches;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(UnitSpacingMatches)] = UnitSpacingMatches,
            [nameof(FixedColumnLayoutMatches)] = FixedColumnLayoutMatches
        };
}

// #151：倒计时编辑门禁输入与结果（隔离负向夹具用）。
internal sealed record CountdownEditingGateInputs(
    bool CountdownFieldErrorsAtSource,
    bool FocusFirstInvalidControl,
    bool CountdownFieldErrorsClearOnCorrection,
    bool SingleErrorRowNotClipped,
    bool PreferenceErrorsBelowFields,
    bool PreferenceErrorsClearOnCorrection,
    bool PrimarySecondaryRolesDistinct,
    bool InputSurfacesWhitePaper,
    bool SelectionHighlightDistinct,
    bool StatusBarSurfaceErrorContrast,
    bool ReminderRadioSelectedContrast,
    bool EditorTypographyTokenized);

internal sealed record CountdownEditingGateResults(
    bool FieldErrorsAtSourceWithFocus,
    bool FieldErrorsClear,
    bool SingleErrorRowNotClipped,
    bool PreferenceErrorsAtSource,
    bool PrimarySecondaryRolesDistinct,
    bool InputSurfacesWhitePaper,
    bool SelectionHighlightDistinct,
    bool StatusBarErrorContrast,
    bool ReminderRadioSelectedContrast,
    bool EditorTypographyTokenized)
{
    public bool AllPassed =>
        FieldErrorsAtSourceWithFocus &&
        FieldErrorsClear &&
        SingleErrorRowNotClipped &&
        PreferenceErrorsAtSource &&
        PrimarySecondaryRolesDistinct &&
        InputSurfacesWhitePaper &&
        SelectionHighlightDistinct &&
        StatusBarErrorContrast &&
        ReminderRadioSelectedContrast &&
        EditorTypographyTokenized;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(FieldErrorsAtSourceWithFocus)] = FieldErrorsAtSourceWithFocus,
            [nameof(FieldErrorsClear)] = FieldErrorsClear,
            [nameof(SingleErrorRowNotClipped)] = SingleErrorRowNotClipped,
            [nameof(PreferenceErrorsAtSource)] = PreferenceErrorsAtSource,
            [nameof(PrimarySecondaryRolesDistinct)] = PrimarySecondaryRolesDistinct,
            [nameof(InputSurfacesWhitePaper)] = InputSurfacesWhitePaper,
            [nameof(SelectionHighlightDistinct)] = SelectionHighlightDistinct,
            [nameof(StatusBarErrorContrast)] = StatusBarErrorContrast,
            [nameof(ReminderRadioSelectedContrast)] = ReminderRadioSelectedContrast,
            [nameof(EditorTypographyTokenized)] = EditorTypographyTokenized
        };
}

// #148：倒计时可读性门禁输入与结果（隔离负向夹具用）。
internal sealed record CountdownReadabilityGateInputs(
    bool BadgeUsesInkText,
    bool BadgeMeetsContrast,
    bool BadgeSizedAndCentered,
    bool ExpiredSecondaryUsesBodyColor,
    bool ExpiredPrimaryUsesNumberColor,
    bool BlocksVerticalRatioMatches,
    bool UnitFontSizeAtFloor,
    bool SegmentContrastGateOnly);

internal sealed record CountdownReadabilityGateResults(
    bool BadgeContractMatches,
    bool ExpiredTextMatches,
    bool BlocksGeometryMatches,
    bool SegmentContrastGateOnly)
{
    public bool AllPassed =>
        BadgeContractMatches && ExpiredTextMatches && BlocksGeometryMatches && SegmentContrastGateOnly;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(BadgeContractMatches)] = BadgeContractMatches,
            [nameof(ExpiredTextMatches)] = ExpiredTextMatches,
            [nameof(BlocksGeometryMatches)] = BlocksGeometryMatches,
            [nameof(SegmentContrastGateOnly)] = SegmentContrastGateOnly
        };
}

internal sealed record CountdownIncrementalRefreshGateResults(
    bool IdentityPreserved,
    bool FocusPreserved,
    bool NonZeroScrollPreserved,
    bool PointerStatePreserved,
    bool CollectionChangeRebuilt,
    bool FilterChangeRebuilt,
    bool PrecisionChangeRebuilt,
    bool ExpiryChangeRebuilt,
    bool ExpiryBackgroundFlipped,
    bool ExpiryRemainingTextFlipped)
{
    public bool ExpiryVisualsFlipTogether =>
        ExpiryBackgroundFlipped && ExpiryRemainingTextFlipped;

    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(IdentityPreserved), IdentityPreserved),
        (nameof(FocusPreserved), FocusPreserved),
        (nameof(NonZeroScrollPreserved), NonZeroScrollPreserved),
        (nameof(PointerStatePreserved), PointerStatePreserved),
        (nameof(CollectionChangeRebuilt), CollectionChangeRebuilt),
        (nameof(FilterChangeRebuilt), FilterChangeRebuilt),
        (nameof(PrecisionChangeRebuilt), PrecisionChangeRebuilt),
        (nameof(ExpiryChangeRebuilt), ExpiryChangeRebuilt),
        (nameof(ExpiryBackgroundFlipped), ExpiryBackgroundFlipped),
        (nameof(ExpiryRemainingTextFlipped), ExpiryRemainingTextFlipped)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record ConfirmationDialogGateInputs(
    bool ButtonsVisible,
    bool BodyReachable,
    bool CancelIsSafeDefault,
    bool ProductionMatrixComplete,
    bool BodyEndReachable,
    bool CenteredOnOwner,
    bool EnterIsSafe,
    bool CancelAndEscapeAreSafe);

internal sealed record ConfirmationDialogGateResults(
    bool ButtonsVisible,
    bool BodyReachable,
    bool CancelIsSafeDefault,
    bool ProductionMatrixComplete,
    bool BodyEndReachable,
    bool CenteredOnOwner,
    bool EnterIsSafe,
    bool CancelAndEscapeAreSafe)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(ButtonsVisible), ButtonsVisible),
        (nameof(BodyReachable), BodyReachable),
        (nameof(CancelIsSafeDefault), CancelIsSafeDefault),
        (nameof(ProductionMatrixComplete), ProductionMatrixComplete),
        (nameof(BodyEndReachable), BodyEndReachable),
        (nameof(CenteredOnOwner), CenteredOnOwner),
        (nameof(EnterIsSafe), EnterIsSafe),
        (nameof(CancelAndEscapeAreSafe), CancelAndEscapeAreSafe)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record SecondaryWindowTypographyGateInputs(
    bool RenderedScaleMatches,
    bool ContentRootsDeclareFontSize,
    bool FloatingTypographyUnchanged);

internal sealed record SecondaryWindowTypographyGateResults(
    bool RenderedScaleMatches,
    bool ContentRootsDeclareFontSize,
    bool FloatingTypographyUnchanged)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(RenderedScaleMatches), RenderedScaleMatches),
        (nameof(ContentRootsDeclareFontSize), ContentRootsDeclareFontSize),
        (nameof(FloatingTypographyUnchanged), FloatingTypographyUnchanged)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record ReminderCenterKeyboardGateInputs(
    bool TabTraversalWorks,
    bool CloseGuardWorks,
    bool ShowPreservesFocusAndAccessibleName,
    bool EscapeFocusesAcknowledge);

internal sealed record ReminderCenterKeyboardGateResults(
    bool TabTraversalWorks,
    bool CloseGuardWorks,
    bool ShowPreservesFocusAndAccessibleName,
    bool EscapeFocusesAcknowledge)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(TabTraversalWorks), TabTraversalWorks),
        (nameof(CloseGuardWorks), CloseGuardWorks),
        (nameof(ShowPreservesFocusAndAccessibleName), ShowPreservesFocusAndAccessibleName),
        (nameof(EscapeFocusesAcknowledge), EscapeFocusesAcknowledge)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record MainShortcutGateInputs(
    IReadOnlyList<string> ExpectedCommands,
    IReadOnlyDictionary<string, bool> ObservedCommands,
    bool TextInputPreserved,
    bool PopupGuarded,
    bool ModalOwnerGuarded);

internal sealed record MainShortcutGateResults(
    bool CommandsWork,
    bool TextInputPreserved,
    bool PopupGuarded,
    bool ModalOwnerGuarded)
{
    public bool AllPassed => CommandsWork && TextInputPreserved && PopupGuarded && ModalOwnerGuarded;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(CommandsWork), CommandsWork),
        (nameof(TextInputPreserved), TextInputPreserved),
        (nameof(PopupGuarded), PopupGuarded),
        (nameof(ModalOwnerGuarded), ModalOwnerGuarded)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record MainKeyboardNavigationGateInputs(
    bool InitialFocusSafe,
    bool CompleteTabSequence);

// #153：弹层 Tab 层内循环门禁输入/结果。
internal sealed record PopupFocusTabGateInputs(
    bool FilterCyclesInsideLayer,
    bool TagManagerCyclesInsideLayer,
    bool TagSelectorCyclesInsideLayer,
    bool OverflowMenuCyclesInsideLayer);

internal sealed record PopupFocusTabGateResults(
    bool FilterCyclesInsideLayer,
    bool TagManagerCyclesInsideLayer,
    bool TagSelectorCyclesInsideLayer,
    bool OverflowMenuCyclesInsideLayer)
{
    public bool AllCyclesInsideLayer =>
        FilterCyclesInsideLayer &&
        TagManagerCyclesInsideLayer &&
        TagSelectorCyclesInsideLayer &&
        OverflowMenuCyclesInsideLayer;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(FilterCyclesInsideLayer), FilterCyclesInsideLayer),
        (nameof(TagManagerCyclesInsideLayer), TagManagerCyclesInsideLayer),
        (nameof(TagSelectorCyclesInsideLayer), TagSelectorCyclesInsideLayer),
        (nameof(OverflowMenuCyclesInsideLayer), OverflowMenuCyclesInsideLayer)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record MainKeyboardNavigationGateResults(
    bool InitialFocusSafe,
    bool CompleteTabSequence)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(InitialFocusSafe), InitialFocusSafe),
        (nameof(CompleteTabSequence), CompleteTabSequence)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record MainStructuralAnnouncementGateInputs(
    bool WorkspaceAnnouncements,
    bool FilterResultAnnouncement,
    bool EmptyStateAnnouncements);

internal sealed record MainStructuralAnnouncementGateResults(
    bool WorkspaceAnnouncements,
    bool FilterResultAnnouncement,
    bool EmptyStateAnnouncements)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(WorkspaceAnnouncements), WorkspaceAnnouncements),
        (nameof(FilterResultAnnouncement), FilterResultAnnouncement),
        (nameof(EmptyStateAnnouncements), EmptyStateAnnouncements)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record FloatingScrollBarGateInputs(
    bool LocalStylePresent,
    bool MainWindowStyleUnaffected,
    double HitTargetWidth,
    double NormalThumbWidth,
    double HoverThumbWidth,
    double DragThumbWidth,
    double NormalThumbOpacity,
    double HoverThumbOpacity,
    double DragThumbOpacity,
    bool ThemeTokensMatch,
    bool NormalDoesNotObscureContent,
    bool HoverDoesNotObscureContent,
    bool DragDoesNotObscureContent,
    bool ContentSafeAreaUnchanged,
    bool WheelChangesOffset,
    bool DragChangesOffset);

internal sealed record FloatingScrollBarGateResults(
    bool LocalStyleScoped,
    bool HitTargetUsable,
    bool VisualWeightProgression,
    bool UsesWarmThemeTokens,
    bool ContentRemainsSafe,
    bool InputPathsWork)
{
    public bool AllPassed =>
        LocalStyleScoped &&
        HitTargetUsable &&
        VisualWeightProgression &&
        UsesWarmThemeTokens &&
        ContentRemainsSafe &&
        InputPathsWork;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(LocalStyleScoped)] = LocalStyleScoped,
            [nameof(HitTargetUsable)] = HitTargetUsable,
            [nameof(VisualWeightProgression)] = VisualWeightProgression,
            [nameof(UsesWarmThemeTokens)] = UsesWarmThemeTokens,
            [nameof(ContentRemainsSafe)] = ContentRemainsSafe,
            [nameof(InputPathsWork)] = InputPathsWork
        };
}

internal sealed record UiQualityGateResults(
    bool MainReferenceDecorationScaling,
    bool MainReferenceDecorationLayerOrder,
    bool MainWorkspaceResourcesFollowTheme,
    bool MainCountdownCardResourcesFollowTheme,
    bool MainNoteCardResourcesFollowTheme,
    bool FloatingContentSafeArea,
    bool FloatingTitleUsesSemiBoldWeight,
    bool FloatingNoteInputWraps,
    bool FloatingNoteInputScrolls)
{
    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(MainReferenceDecorationScaling)] = MainReferenceDecorationScaling,
            [nameof(MainReferenceDecorationLayerOrder)] = MainReferenceDecorationLayerOrder,
            [nameof(MainWorkspaceResourcesFollowTheme)] = MainWorkspaceResourcesFollowTheme,
            [nameof(MainCountdownCardResourcesFollowTheme)] = MainCountdownCardResourcesFollowTheme,
            [nameof(MainNoteCardResourcesFollowTheme)] = MainNoteCardResourcesFollowTheme,
            [nameof(FloatingContentSafeArea)] = FloatingContentSafeArea,
            [nameof(FloatingTitleUsesSemiBoldWeight)] = FloatingTitleUsesSemiBoldWeight,
            [nameof(FloatingNoteInputWraps)] = FloatingNoteInputWraps,
            [nameof(FloatingNoteInputScrolls)] = FloatingNoteInputScrolls
        };
}

internal sealed record CountdownEmptyStateGateInputs(
    bool EmptyStatesDistinguished,
    bool PointerResetRestoresResults,
    bool KeyboardResetRestoresResults,
    bool DayThemeTextContrast);

internal sealed record CountdownEmptyStateGateResults(
    bool EmptyStatesDistinguished,
    bool PointerResetRestoresResults,
    bool KeyboardResetRestoresResults,
    bool DayThemeTextContrast)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(EmptyStatesDistinguished)] = EmptyStatesDistinguished,
            [nameof(PointerResetRestoresResults)] = PointerResetRestoresResults,
            [nameof(KeyboardResetRestoresResults)] = KeyboardResetRestoresResults,
            [nameof(DayThemeTextContrast)] = DayThemeTextContrast
        };
}

internal sealed record NoteVisualTokenGateInputs(
    bool CompactInputPaddingAligned,
    bool BadgeTokensFollowState,
    bool EmptyStateTextContrast);

internal sealed record NoteVisualTokenGateResults(
    bool CompactInputPaddingAligned,
    bool BadgeTokensFollowState,
    bool EmptyStateTextContrast)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(CompactInputPaddingAligned)] = CompactInputPaddingAligned,
            [nameof(BadgeTokensFollowState)] = BadgeTokensFollowState,
            [nameof(EmptyStateTextContrast)] = EmptyStateTextContrast
        };
}

internal sealed record MainNoteCompletionGateInputs(
    bool DedicatedStyleApplied,
    bool MainBoxAtLeast24Pixels,
    bool MainTickScaled,
    bool WarmTokensReused,
    bool RoundedCornersPreserved,
    bool CheckAndUncheckWork,
    bool PersistenceFailureRollsBack,
    bool AccessibleNamesUpdate,
    bool TagSelectorSizeUnchanged,
    bool FloatingSizeUnchanged);

internal sealed record MainNoteCompletionGateResults(
    bool MainTargetSizeAndBehavior,
    bool OtherUserSizesUnchanged)
{
    public bool AllPassed => MainTargetSizeAndBehavior && OtherUserSizesUnchanged;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(MainTargetSizeAndBehavior)] = MainTargetSizeAndBehavior,
            [nameof(OtherUserSizesUnchanged)] = OtherUserSizesUnchanged
        };
}

internal sealed record ReminderCenterAcknowledgeAllExerciseResult(
    bool WritesAllDispositions,
    bool MatchesSingleSemantics,
    bool FailureRetainsAllAtomically,
    bool BackoffRetryCompletesBatch)
{
    public static ReminderCenterAcknowledgeAllExerciseResult Failed { get; } =
        new(false, false, false, false);
}

internal sealed class ValidationGuidSource(params Guid[] values) : IGuidSource
{
    private int _index;

    public Guid NewGuid()
    {
        if (_index >= values.Length)
        {
            throw new InvalidOperationException("验证 Guid 序列已耗尽。");
        }

        return values[_index++];
    }
}
