using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static class ValidationExercises
{
    internal const double FloatingNightPlushEdgeBrightnessLimit = 0.012;
    internal const double FloatingNightPlushEdgeSaturationLimit = 0.35;

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
            var body = window.Content as StackPanel;
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

            return defaultDecisionIsExit &&
                   closeKeepsExit &&
                   buttons.Length == 2 &&
                   recover is { IsDefault: true, IsEnabled: true } &&
                   exit is { IsCancel: true, IsEnabled: true } &&
                   !declinedPresented &&
                   failurePresented &&
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
                    !string.Equals(button.Content?.ToString(), "完成便签", StringComparison.Ordinal)),
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
            Directory.CreateDirectory(settingsPath + ".tmp");
            Must(center.RequestComplete(firstKey), "无法点击真实完成便签按钮。");
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
                    FindByAutomationId<Button>(editors[0], "CancelReminderRulesButton")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(ids));
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
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
                FindByAutomationId<Button>(editor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var originalEdit = VisualDescendants<Button>(editor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                var originalId = originalEdit.Tag as string;
                originalEdit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(1), 30);
                FindByAutomationId<Button>(editor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content?.ToString(), "复制", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, originalId, StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(2), 30);
                FindByAutomationId<Button>(editor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var copiedId = VisualDescendants<Button>(editor)
                    .Where(button => string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal))
                    .Select(button => button.Tag as string)
                    .Single(id => !string.Equals(id, originalId, StringComparison.Ordinal));
                VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content?.ToString(), "删除", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, originalId, StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(3), 30);
                FindByAutomationId<Button>(editor, "AcceptReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                actionsWorked = editor.ResultRules.Count == 2 &&
                    editor.ResultRules.Any(rule => string.Equals(rule.Id, copiedId, StringComparison.Ordinal)) &&
                    editor.ResultRules.All(rule => !string.Equals(rule.Id, originalId, StringComparison.Ordinal));
            }));
            var accepted = editor.ShowDialog() == true;
            Must(accepted, "共享提醒规则编辑器未接受有效的新增、编辑、复制与删除草稿。");
            Must(actionsWorked, "共享提醒规则编辑器未保留复制项、删除原项或提交 dirty 草稿。");
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
                FindByAutomationId<Button>(delayedEditor, "AcceptReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                FindByAutomationId<Button>(delayedEditor, "AcceptReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                VisualDescendants<Button>(cancelledEditor).Single(button =>
                        string.Equals(button.Content?.ToString(), "删除", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindByAutomationId<Button>(cancelledEditor, "CancelReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
                mode.SelectedIndex = 2;
                FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes").Text = "0";
                FindByAutomationId<Button>(editor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var edits = VisualDescendants<Button>(editor).Where(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal)).ToArray();
                if (edits.Length != 1)
                {
                    FindByAutomationId<Button>(editor, "CancelReminderRulesButton")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                }

                var edit = edits[0];
                var createdId = edit.Tag as string;
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes").Text =
                    "5256000";
                FindByAutomationId<Button>(editor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindByAutomationId<Button>(editor, "AcceptReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                SetEditorSchedule(transitionEditor, ReminderEditingMode.Once, dueBeijing, 30);
                FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                edit = VisualDescendants<Button>(transitionEditor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindByAutomationId<ComboBox>(transitionEditor, "ReminderRuleMode").SelectedIndex = 2;
                FindByAutomationId<TextBox>(transitionEditor, "ReminderRuleRelativeOffsetMinutes").Text = "30";
                FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                edit = VisualDescendants<Button>(transitionEditor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                SetEditorSchedule(transitionEditor, ReminderEditingMode.Repeating, dueBeijing, 30);
                FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindByAutomationId<Button>(transitionEditor, "AcceptReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                FindByAutomationId<Button>(rejectedEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var error = FindByAutomationId<TextBlock>(
                    rejectedEditor,
                    "ReminderRuleRelativeOffsetError");
                pastRejectedAtOffset =
                    rejectedEditor.ResultRules.Count == 0 &&
                    error.Visibility == Visibility.Visible &&
                    error.Text.Contains("派生时间晚于当前北京时间", StringComparison.Ordinal);
                FindByAutomationId<Button>(rejectedEditor, "CancelReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

                FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var monthlyLocalized = error.Visibility == Visibility.Visible &&
                    error.Text.Contains("按月重复", StringComparison.Ordinal) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(lastDay),
                        error.Text,
                        StringComparison.Ordinal) &&
                    lastDay.IsKeyboardFocusWithin;

                frequency.SelectedIndex = 3;
                FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleCalendarMonth").SelectedIndex = -1;
                FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindByAutomationId<Button>(noteEditor, "AcceptReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                FindByAutomationId<Button>(countdownEditor, "SaveReminderRuleButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FindByAutomationId<Button>(countdownEditor, "AcceptReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var complete = VisualDescendants<Button>(editor).Single(button =>
                    string.Equals(
                        AutomationProperties.GetName(button),
                        "接受提醒规则草稿",
                        StringComparison.Ordinal));
                complete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                FindByAutomationId<Button>(editor, "AcceptReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                FindByAutomationId<Button>(editor, "CancelReminderRulesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

            using (var calendar = new HolidayCalendar(unavailableRoot))
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
                    stateChangedCount == 1 &&
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
                    stateChangedCount == 2 &&
                    calendar.GetWorkdayEndCountdown(nowUtc, preferences) is not null &&
                    calendar.GetEarnings(nowUtc, preferences) == 1550m,
                    "工作日历 interface 未在退避后恢复同年失败的刷新。");
            }

            Must(
                !HolidayCalendar.TryParse(payload, nextYear, out _),
                "工作日历 interface 接受了与请求年份不一致的缓存。");

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
            input.DayNightTextContrast);

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
            passing with { DayNightTextContrast = false },
            nameof(CountdownEmptyStateGateResults.DayNightTextContrast));
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

    public static MainCountdownRemainingGateResults EvaluateMainCountdownRemainingGates(
        MainCountdownRemainingGateInputs input) =>
        new(
            input.ExpectedTexts.Count == 5 &&
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
            "2000000 天 00 小时 00 分钟 00 秒"
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

    public static bool RunCountdownIncrementalRefreshGateIsolationBehavior()
    {
        var passing = new CountdownIncrementalRefreshGateResults(
            true, true, true, true, true, true, true, true, true, true, true);
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
            passing with { ThemeChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.ThemeChangeRebuilt));
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
            input.InitialFocusAndAccessibleNameWork);

    public static bool RunReminderCenterKeyboardGateIsolationBehavior()
    {
        var passing = new ReminderCenterKeyboardGateInputs(true, true, true);
        Must(EvaluateReminderCenterKeyboardGates(passing).AllPassed,
            "提醒中心键盘全通过夹具被错误拒绝。");
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { TabTraversalWorks = false },
            nameof(ReminderCenterKeyboardGateResults.TabTraversalWorks));
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { CloseGuardWorks = false },
            nameof(ReminderCenterKeyboardGateResults.CloseGuardWorks));
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { InitialFocusAndAccessibleNameWork = false },
            nameof(ReminderCenterKeyboardGateResults.InitialFocusAndAccessibleNameWork));
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
            "CountdownsWorkspace", "Filter", "Minimize", "NotesWorkspace",
            "SaveCountdown", "SavePreferences", "TagManager", "Theme"
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
            input.EmptyStateAnnouncements,
            input.ThemeAnnouncements);

    public static bool RunMainStructuralAnnouncementGateIsolationBehavior()
    {
        var passing = new MainStructuralAnnouncementGateInputs(true, true, true, true);
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
        VerifyOnlyMainStructuralAnnouncementGateFails(
            passing with { ThemeAnnouncements = false },
            nameof(MainStructuralAnnouncementGateResults.ThemeAnnouncements));
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
                ApplicationThemeMode.Day,
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
                ApplicationThemeMode.Night,
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
        var day = FloatingQuickAddPaletteBindings.Expected(ApplicationThemeMode.Day);
        Must(
            FloatingQuickAddPaletteBindings.Evaluate(day).Passed,
            "悬浮窗快速新增日间 palette 固定映射未通过。");

        var night = FloatingQuickAddPaletteBindings.Expected(ApplicationThemeMode.Night);
        Must(
            FloatingQuickAddPaletteBindings.Evaluate(night).Passed,
            "悬浮窗快速新增夜间 palette 固定映射未通过。");

        var historicalNightMiswirings = new[]
        {
            (
                Mapping: night with { InputSurfaceResourceKey = "FloatingNightNoteCardBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.InputSurfaceMatches)),
            (
                Mapping: night with { InputTextResourceKey = "FloatingNightTextBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.InputTextMatches)),
            (
                Mapping: night with { ErrorTextResourceKey = "FloatingNightHeaderDangerBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.ErrorTextMatches))
        };
        foreach (var fixture in historicalNightMiswirings)
        {
            var failing = FloatingQuickAddPaletteBindings.Evaluate(fixture.Mapping);
            Must(
                !failing.Passed &&
                failing.FailedChecks.SequenceEqual([fixture.FailedCheck], StringComparer.Ordinal),
                $"悬浮窗快速新增历史串线未只击穿 {fixture.FailedCheck}。");
        }

        return true;
    }

    public static FloatingNightPlushEdgeGateResults EvaluateFloatingNightPlushEdgeGlow(
        FloatingNightPlushEdgeGateInputs input) =>
        new(
            input.HasThreeRoleSamples &&
                input.MaximumBrightnessSignal <= FloatingNightPlushEdgeBrightnessLimit,
            input.HasThreeRoleSamples &&
                input.MaximumSaturation <= FloatingNightPlushEdgeSaturationLimit);

    public static bool RunFloatingNightPlushEdgeGlowGateIsolationBehavior()
    {
        var passing = new FloatingNightPlushEdgeGateInputs(0.006, 0.20, true);
        var passingResult = EvaluateFloatingNightPlushEdgeGlow(passing);
        Must(
            passingResult.BrightnessWithinLimit && passingResult.SaturationWithinLimit,
            "夜间悬浮窗角色外缘暖光全通过夹具被错误拒绝。");

        var brightResult = EvaluateFloatingNightPlushEdgeGlow(
            passing with
            {
                MaximumBrightnessSignal = FloatingNightPlushEdgeBrightnessLimit + 0.01
            });
        Must(
            !brightResult.BrightnessWithinLimit && brightResult.SaturationWithinLimit,
            "夜间悬浮窗角色外缘亮度负向夹具未单独变红。");

        var saturatedResult = EvaluateFloatingNightPlushEdgeGlow(
            passing with
            {
                MaximumSaturation = FloatingNightPlushEdgeSaturationLimit + 0.01
            });
        Must(
            saturatedResult.BrightnessWithinLimit && !saturatedResult.SaturationWithinLimit,
            "夜间悬浮窗角色外缘饱和度负向夹具未单独变红。");

        return true;
    }

    public static FloatingPrimaryCountdownContrastGateResults EvaluateFloatingPrimaryCountdownContrastGates(
        FloatingPrimaryCountdownContrastGateInputs input) =>
        new(
            input.ContrastRatios.Count == 2 && input.ContrastRatios.All(ratio => ratio >= 4.5),
            input.AccentSeparatedFromPlushGlow);

    public static bool RunFloatingPrimaryCountdownContrastGateIsolationBehavior()
    {
        static double ContrastRatio(
            (byte Red, byte Green, byte Blue) first,
            (byte Red, byte Green, byte Blue) second)
        {
            static double Luminance((byte Red, byte Green, byte Blue) color)
            {
                static double Channel(byte value)
                {
                    var normalized = value / 255d;
                    return normalized <= 0.04045
                        ? normalized / 12.92
                        : Math.Pow((normalized + 0.055) / 1.055, 2.4);
                }

                return (0.2126 * Channel(color.Red)) +
                    (0.7152 * Channel(color.Green)) +
                    (0.0722 * Channel(color.Blue));
            }

            var firstLuminance = Luminance(first);
            var secondLuminance = Luminance(second);
            return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
                (Math.Min(firstLuminance, secondLuminance) + 0.05);
        }

        var passing = new FloatingPrimaryCountdownContrastGateInputs(
            new[] { 5.83, 6.12 },
            true);
        var passingResult = EvaluateFloatingPrimaryCountdownContrastGates(passing);
        Must(
            passingResult.TextContrast && passingResult.AccentSeparatedFromPlushGlow,
            "悬浮窗主倒计时对比度与语义分离全通过夹具被错误拒绝。");

        var legacyNightAccent = EvaluateFloatingPrimaryCountdownContrastGates(
            passing with
            {
                ContrastRatios = new[]
                {
                    5.83,
                    ContrastRatio((0x75, 0x6A, 0x62), (0x49, 0x35, 0x25))
                }
            });
        Must(
            !legacyNightAccent.TextContrast && legacyNightAccent.AccentSeparatedFromPlushGlow,
            "旧夜间主倒计时强调色负向夹具未单独让对比度门禁变红。");

        var sharedAccentToken = EvaluateFloatingPrimaryCountdownContrastGates(
            passing with { AccentSeparatedFromPlushGlow = false });
        Must(
            sharedAccentToken.TextContrast && !sharedAccentToken.AccentSeparatedFromPlushGlow,
            "主倒计时与玩偶共享 token 的负向夹具未单独让语义分离门禁变红。");

        return true;
    }

    public static NoteCompletedAuxiliaryContrastGateResults EvaluateNoteCompletedAuxiliaryContrastGates(
        NoteCompletedAuxiliaryContrastGateInputs input) =>
        new(
            input.ContrastRatios.Count == 2 && input.ContrastRatios.All(ratio => ratio >= 4.5),
            input.UsesDedicatedToken,
            input.ActualBackgroundMatchesHeader,
            input.MutedBrushUnchanged,
            input.CompletedHeaderColorUnchanged);

    public static bool RunNoteCompletedAuxiliaryContrastGateIsolationBehavior()
    {
        var passing = new NoteCompletedAuxiliaryContrastGateInputs(
            new[] { 5.772, 7.466 },
            true,
            true,
            true,
            true);
        var passingResult = EvaluateNoteCompletedAuxiliaryContrastGates(passing);
        Must(
            passingResult.AllPassed,
            "已完成便签辅助说明日夜实际背景对比度全通过夹具被错误拒绝。");

        var lowContrastResult = EvaluateNoteCompletedAuxiliaryContrastGates(
            passing with { ContrastRatios = new[] { 4.34, 7.466 } });
        Must(
            !lowContrastResult.TextContrast &&
            lowContrastResult.UsesDedicatedToken &&
            lowContrastResult.ActualBackgroundMatchesHeader &&
            lowContrastResult.MutedBrushUnchanged &&
            lowContrastResult.CompletedHeaderColorUnchanged,
            "已完成便签辅助说明 token 改回不达标值时未单独触发对比度负向门。");

        var sharedMutedResult = EvaluateNoteCompletedAuxiliaryContrastGates(
            passing with { UsesDedicatedToken = false });
        Must(
            sharedMutedResult.TextContrast &&
            !sharedMutedResult.UsesDedicatedToken &&
            sharedMutedResult.ActualBackgroundMatchesHeader &&
            sharedMutedResult.MutedBrushUnchanged &&
            sharedMutedResult.CompletedHeaderColorUnchanged,
            "已完成便签辅助说明与 MutedBrush 串线时未单独触发语义 token 负向门。");

        var backgroundResult = EvaluateNoteCompletedAuxiliaryContrastGates(
            passing with { ActualBackgroundMatchesHeader = false });
        Must(
            backgroundResult.TextContrast &&
            backgroundResult.UsesDedicatedToken &&
            !backgroundResult.ActualBackgroundMatchesHeader &&
            backgroundResult.MutedBrushUnchanged &&
            backgroundResult.CompletedHeaderColorUnchanged,
            "已完成便签辅助说明未对其实际标题背景校验时未单独触发负向门。");

        var mutedChangedResult = EvaluateNoteCompletedAuxiliaryContrastGates(
            passing with { MutedBrushUnchanged = false });
        Must(
            mutedChangedResult.TextContrast &&
            mutedChangedResult.UsesDedicatedToken &&
            mutedChangedResult.ActualBackgroundMatchesHeader &&
            !mutedChangedResult.MutedBrushUnchanged &&
            mutedChangedResult.CompletedHeaderColorUnchanged,
            "MutedBrush 改动未单独触发已完成便签辅助说明隔离门。");

        var headerChangedResult = EvaluateNoteCompletedAuxiliaryContrastGates(
            passing with { CompletedHeaderColorUnchanged = false });
        Must(
            headerChangedResult.TextContrast &&
            headerChangedResult.UsesDedicatedToken &&
            headerChangedResult.ActualBackgroundMatchesHeader &&
            headerChangedResult.MutedBrushUnchanged &&
            !headerChangedResult.CompletedHeaderColorUnchanged,
            "NoteCompletedHeaderColor 改动未单独触发已完成便签辅助说明隔离门。");

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
            input.DayThemeTokensMatch && input.NightThemeTokensMatch,
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
            passing with { NightThemeTokensMatch = false },
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

    private static string CreateHolidayCalendarPayload(int year)
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
                    is_workday = date == new DateTime(year, 5, 6) ||
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

internal sealed record FloatingNightPlushEdgeGateInputs(
    double MaximumBrightnessSignal,
    double MaximumSaturation,
    bool HasThreeRoleSamples);

internal sealed record FloatingNightPlushEdgeGateResults(
    bool BrightnessWithinLimit,
    bool SaturationWithinLimit);

internal sealed record FloatingPrimaryCountdownContrastGateInputs(
    IReadOnlyList<double> ContrastRatios,
    bool AccentSeparatedFromPlushGlow);

internal sealed record FloatingPrimaryCountdownContrastGateResults(
    bool TextContrast,
    bool AccentSeparatedFromPlushGlow);

internal sealed record NoteCompletedAuxiliaryContrastGateInputs(
    IReadOnlyList<double> ContrastRatios,
    bool UsesDedicatedToken,
    bool ActualBackgroundMatchesHeader,
    bool MutedBrushUnchanged,
    bool CompletedHeaderColorUnchanged);

internal sealed record NoteCompletedAuxiliaryContrastGateResults(
    bool TextContrast,
    bool UsesDedicatedToken,
    bool ActualBackgroundMatchesHeader,
    bool MutedBrushUnchanged,
    bool CompletedHeaderColorUnchanged)
{
    public bool AllPassed =>
        TextContrast &&
        UsesDedicatedToken &&
        ActualBackgroundMatchesHeader &&
        MutedBrushUnchanged &&
        CompletedHeaderColorUnchanged;
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

internal sealed record CountdownIncrementalRefreshGateResults(
    bool IdentityPreserved,
    bool FocusPreserved,
    bool NonZeroScrollPreserved,
    bool PointerStatePreserved,
    bool CollectionChangeRebuilt,
    bool FilterChangeRebuilt,
    bool PrecisionChangeRebuilt,
    bool ThemeChangeRebuilt,
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
        (nameof(ThemeChangeRebuilt), ThemeChangeRebuilt),
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
    bool InitialFocusAndAccessibleNameWork);

internal sealed record ReminderCenterKeyboardGateResults(
    bool TabTraversalWorks,
    bool CloseGuardWorks,
    bool InitialFocusAndAccessibleNameWork)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(TabTraversalWorks), TabTraversalWorks),
        (nameof(CloseGuardWorks), CloseGuardWorks),
        (nameof(InitialFocusAndAccessibleNameWork), InitialFocusAndAccessibleNameWork)
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
    bool EmptyStateAnnouncements,
    bool ThemeAnnouncements);

internal sealed record MainStructuralAnnouncementGateResults(
    bool WorkspaceAnnouncements,
    bool FilterResultAnnouncement,
    bool EmptyStateAnnouncements,
    bool ThemeAnnouncements)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(WorkspaceAnnouncements), WorkspaceAnnouncements),
        (nameof(FilterResultAnnouncement), FilterResultAnnouncement),
        (nameof(EmptyStateAnnouncements), EmptyStateAnnouncements),
        (nameof(ThemeAnnouncements), ThemeAnnouncements)
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
    bool DayThemeTokensMatch,
    bool NightThemeTokensMatch,
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
    bool DayNightTextContrast);

internal sealed record CountdownEmptyStateGateResults(
    bool EmptyStatesDistinguished,
    bool PointerResetRestoresResults,
    bool KeyboardResetRestoresResults,
    bool DayNightTextContrast)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(EmptyStatesDistinguished)] = EmptyStatesDistinguished,
            [nameof(PointerResetRestoresResults)] = PointerResetRestoresResults,
            [nameof(KeyboardResetRestoresResults)] = KeyboardResetRestoresResults,
            [nameof(DayNightTextContrast)] = DayNightTextContrast
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
