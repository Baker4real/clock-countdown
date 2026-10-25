using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    public static bool RunSettingsRecoveryWindowBehavior()
    {
        ProbeRecoveryPromptCopyThroughChoose();
        ProbeRecoveryCompletionMessage();
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
                    AutomationProperties.GetAutomationId(button),
                    "SettingsRecoveryRecover",
                    StringComparison.Ordinal));
            var exit = buttons.SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "SettingsRecoveryExit",
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

    private static void ProbeRecoveryPromptCopyThroughChoose()
    {
        var expectedBackupTime = new DateTimeOffset(2031, 1, 2, 3, 4, 0, TimeSpan.Zero);
        var inspected = false;
        var correct = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var window = Application.Current.Windows.OfType<SettingsRecoveryWindow>().Single();
            var body = ((Border)window.Content).Child as StackPanel;
            var message = body!.Children.OfType<TextBlock>().Last();
            var buttons = VisualDescendants<Button>(window).ToArray();
            correct = message.Text.Contains("2031 年 1 月 2 日 11:04", StringComparison.Ordinal) &&
                message.Text.Contains("北京时间", StringComparison.Ordinal) &&
                message.Text.Contains("逐字节另存", StringComparison.Ordinal) &&
                message.Text.Contains("若原文件可读", StringComparison.Ordinal) &&
                !message.Text.Contains("原文件已另存", StringComparison.Ordinal) &&
                message.Foreground is SolidColorBrush foreground &&
                foreground.Color == Color.FromRgb(0x6B, 0x4A, 0x3C) &&
                ((Border)window.Content).Background is SolidColorBrush background &&
                ApplicationTheme.MeetsContrast(foreground, background, 4.5) &&
                buttons.Any(button => Equals(button.Content, "恢复备份并继续")) &&
                buttons.Any(button => Equals(button.Content, "不改设置，退出"));
            inspected = true;
            window.Close();
        }));
        var decision = SettingsRecoveryWindow.Choose(new SettingsRecoveryPrompt(2, expectedBackupTime));
        Must(inspected && correct && decision == SettingsRecoveryDecision.Exit,
            "#228 Choose真实恢复窗缺少备份时间、阶段准确文案、结果按钮或正文对比度。");
    }

    private static void ProbeRecoveryCompletionMessage()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"MinminCountdown-recovery-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var damaged = Encoding.UTF8.GetBytes("{unreadable-settings");
            var payload = SettingsContract.SerializeCanonical(CountdownSettings.Empty);
            foreach (var mode in new[] { "readable", "acl", "normal", "cancel" })
            {
                var branch = Path.Combine(directory, mode);
                Directory.CreateDirectory(branch);
                var path = Path.Combine(branch, "settings.json");
                File.WriteAllBytes(path, mode == "normal" ? Encoding.UTF8.GetBytes(payload) : damaged);
                File.WriteAllText(path + ".backup1", payload, new UTF8Encoding(false));
                var evidence = Path.Combine(branch, "settings-recovery-evidence");
                var store = mode == "acl"
                    ? new SettingsStore(fileOperations: new AclDeniedMainSettingsFileOperations(path))
                    : new SettingsStore();
                DesktopWorkspace? workspace = null;
                try
                {
                    workspace = DesktopWorkspace.Load(path, store, _ =>
                    {
                        Must(!Directory.Exists(evidence), "#228 用户确认前不得写恢复证据。");
                        return mode == "cancel" ? SettingsRecoveryDecision.Exit : SettingsRecoveryDecision.RecoverBackup;
                    });
                }
                catch (SettingsRecoveryDeclinedException) when (mode == "cancel")
                {
                    Must(File.ReadAllBytes(path).SequenceEqual(damaged) && !Directory.Exists(evidence) &&
                        File.ReadAllText(path + ".backup1") == payload,
                        "#228 取消恢复必须保留主文件和备份且不写证据。");
                    continue;
                }
                Must(workspace is not null && mode != "cancel", "#228 取消恢复不可启动工作区。");
                using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
                var manager = new MainWindow(workspace!, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
                var floating = new FloatingWindow(workspace!, calendar, manager, AppContext.BaseDirectory);
                PrepareOffscreenValidationWindow(floating);
                var announcements = new List<string>();
                void Observe(TextBlock text)
                {
                    if (AutomationProperties.GetAutomationId(text) == "SettingsRecoveryCompletedStatus")
                        announcements.Add(text.Text);
                }
                UiAccessibility.Announced += Observe;
                try
                {
                    floating.Show();
                    WindowValidationInput.Pump(floating.Dispatcher);
                    var status = VisualDescendants<TextBlock>(floating).SingleOrDefault(text =>
                        AutomationProperties.GetAutomationId(text) == "SettingsRecoveryCompletedStatus");
                    if (mode == "normal")
                    {
                        Must(status is null || !status.IsVisible, "#228 普通启动不能出现恢复成功提示。");
                        Must(announcements.Count == 0, "#228 普通启动不播报恢复提示。");
                        continue;
                    }
                    var expected = mode == "readable"
                        ? "已恢复备份；原文件已另存。"
                        : "已恢复备份；原文件无法读取，未能另存。";
                    Must(status is { IsVisible: true } && status.Text == expected &&
                        AutomationProperties.GetName(status) == expected &&
                        AutomationProperties.GetLiveSetting(status) == AutomationLiveSetting.Polite &&
                        announcements.SequenceEqual(new[] { expected }),
                        "#228 恢复成功后缺少按真实副本结果呈现的一次可见播报。");
                    var scroller = (ScrollViewer)floating.FindName("FloatingBodyScroller");
                    var bounds = BoundsOn(status!, scroller);
                    Must(bounds.Top >= 0 && bounds.Bottom <= scroller.ViewportHeight &&
                        bounds.Left >= 0 && bounds.Right <= scroller.ViewportWidth + 1,
                        "#228 恢复成功提示必须在列表首屏完整可见。");
                    if (mode == "readable")
                        Must(Directory.GetFiles(evidence).Length == 1 &&
                            File.ReadAllBytes(Directory.GetFiles(evidence).Single()).SequenceEqual(damaged),
                            "#228 已另存提示必须对应逐字节落盘副本。");
                    else Must(!Directory.Exists(evidence), "#228 ACL不可读时不可虚构副本。");
                    Must(new SettingsStore().Load(path).Settings.Countdowns.Count == 0,
                        "#228 成功提示之前恢复主设置必须能严格回读。");
                    floating.UpdateTime(DateTimeOffset.UtcNow);
                    floating.ShowWorkspace(WorkspaceKind.Notes);
                    WindowValidationInput.Pump(floating.Dispatcher);
                    Must(status!.Text == expected && announcements.Count == 1,
                        "#228 刷新和切工作区不得覆盖或重复播报恢复结果。");
                    floating.Hide();
                    floating.Show();
                    WindowValidationInput.Pump(floating.Dispatcher);
                    Must(status.IsVisible && status.Text == expected && announcements.Count == 1,
                        "#228 隐藏再显示浮窗不得重复播报恢复结果。");
                }
                finally { UiAccessibility.Announced -= Observe; floating.Close(); manager.Close(); }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
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
                    AutomationProperties.GetAutomationId(button),
                    "SettingsRecoveryRecover",
                    StringComparison.Ordinal));
            var exit = buttons.SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "SettingsRecoveryExit",
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
                string.Equals(AutomationProperties.GetAutomationId(button), "SettingsRecoveryRecover", StringComparison.Ordinal));
            var exit = buttons.SingleOrDefault(button =>
                string.Equals(AutomationProperties.GetAutomationId(button), "SettingsRecoveryExit", StringComparison.Ordinal));
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
                !ButtonHasIconAndText(acknowledgeAll, "全部知道了") ||
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
                        string.Equals(text.Text, "刚刚", StringComparison.Ordinal));
                if (badge is null ||
                    badge.Focusable ||
                    !ReferenceEquals(badge.Background, warningTint) ||
                    badgeText is null ||
                    !ReferenceEquals(badgeText.Foreground, warningAccent))
                {
                    return false;
                }

                // #223：相对时间徽章使用矢量时钟，不靠文字字重构造图标。
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
    // 且相对时间徽章的时钟是纯矢量图形而不是文本（#223）。
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
            // #152/#208：真实 DIP 槽位 28（管理窗等比收缩后），仍高于桌面紧凑下限 24。
            return Math.Abs(check.ActualWidth - 28) <= 1 &&
                   Math.Abs(check.ActualHeight - 28) <= 1 &&
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
}
