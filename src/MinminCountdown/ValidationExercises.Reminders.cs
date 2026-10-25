using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
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
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(workspace.Apply(new SaveNoteChange(
                    null,
                    "送达失败抑制新基础周期探针",
                    new DateTime(2036, 6, 11),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立送达失败抑制探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            Directory.CreateDirectory(settingsPath + ".tmp");
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
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
                workspace.Snapshot.Settings.ReminderStates[rule.Id].LastDeliveredOccurrenceKey ==
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
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心稍后点击探针",
                    new DateTime(2036, 5, 11),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立提醒中心稍后点击探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(
                center.IsSnoozeEnabled(key),
                "提醒中心真实稍后控件未在送达后启用。");
            var beforeInvalidCustom = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
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
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeInvalidCustom,
                "无效自定义稍后没有在来源控件显示错误或改变了状态。");
            Must(
                FindReminderEntryControl<DatePicker>(center, key, "自定义稍后日期：").IsVisible &&
                FindReminderEntryControl<ComboBox>(center, key, "自定义稍后小时：").IsVisible &&
                FindReminderEntryControl<ComboBox>(center, key, "自定义稍后分钟：").IsVisible,
                "无效自定义稍后的来源控件未真实展开。");
            Must(center.RequestSnooze(key, ReminderSnoozeOption.TenMinutes),
                "更正后无法点击 10 分钟稍后控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var state = workspace.Snapshot.Settings.ReminderStates[rule.Id];
            var expectedOccurrenceKey = ReminderOccurrenceKey.Create(rule, dueUtc).ToString();
            var expectedSnoozeUntilUtc = dueUtc.AddMinutes(10);
            var persisted = new SettingsStore(clock: clock).Load(settingsPath).Settings;
            persisted.ReminderStates.TryGetValue(rule.Id, out var persistedState);
            var visibleErrors = string.Join(" | ", VisualDescendants<TextBlock>(center)
                .Where(text => text.IsVisible &&
                    (text.Text.Contains("失败", StringComparison.Ordinal) ||
                     text.Text.Contains("必须", StringComparison.Ordinal) ||
                     text.Text.Contains("请选择", StringComparison.Ordinal)))
                .Select(text => text.Text));
            Must(
                state.ActiveSnooze?.OccurrenceKey ==
                    expectedOccurrenceKey &&
                state.ActiveSnooze.SnoozeUntilUtc == expectedSnoozeUntilUtc &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                persistedState == state,
                "提醒中心稍后点击没有持久化覆盖并关闭空中心。" +
                $"actualKey={state.ActiveSnooze?.OccurrenceKey ?? "<null>"}; " +
                $"expectedKey={expectedOccurrenceKey}; " +
                $"actualUntil={state.ActiveSnooze?.SnoozeUntilUtc:O}; " +
                $"expectedUntil={expectedSnoozeUntilUtc:O}; " +
                $"entries={coordinator.ActiveEntryCount}; windows={coordinator.ActiveWindowCount}; " +
                $"persistedMatches={persistedState == state}; " +
                $"persistedKey={persistedState?.ActiveSnooze?.OccurrenceKey ?? "<null>"}; " +
                $"persistedUntil={persistedState?.ActiveSnooze?.SnoozeUntilUtc:O}; " +
                $"failureVisible={center.IsFailureVisible(key)}; " +
                $"customErrorVisible={center.IsCustomSnoozeErrorVisible(key)}; " +
                $"pendingRetry={coordinator.HasPendingPersistenceRetry}; errors={visibleErrors}");
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

    public static bool RunReminderCenterRelativeTimeBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 8, 20, 1, 30, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"MinminCountdown-relative-time-{Guid.NewGuid():N}.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            for (var index = 0; index < 6; index++)
            {
                var saved = workspace.Apply(new SaveNoteChange(null, $"相对时间便签 {index}", new DateTime(2036, 8, 20), false,
                    Array.Empty<string>(), ReminderSchedule.CreateOnce(dueUtc)));
                Must(saved.Succeeded, $"无法建立相对时间发生项：{saved.Error}");
            }
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ?? throw new InvalidOperationException("相对时间提醒中心未打开。");
            WindowValidationInput.Pump(center.Dispatcher);
            center.UpdateLayout();
            var rows = VisualDescendants<Border>(center).Where(row => row.Tag is ActiveReminderOccurrenceKey).ToArray();
            var models = rows.Select(row => row.DataContext).ToArray();
            var badges = rows.Select(row => VisualDescendants<Border>(row).Single(border =>
                AutomationProperties.GetAutomationId(border) == "ReminderEntryPendingBadge")).ToArray();
            var labels = badges.Select(badge => VisualDescendants<TextBlock>(badge).Single()).ToArray();
            var scroller = VisualDescendants<ScrollViewer>(center).Single(scroll => scroll.Content is StackPanel);
            var focus = FindEntryButton(rows.Last(), "打开便签来源：")!;
            center.ShowDeliveryFailure((ActiveReminderOccurrenceKey)rows[0].Tag);
            var error = VisualDescendants<TextBlock>(rows[0]).Single(text =>
                AutomationProperties.GetAutomationId(text) == "ReminderEntryDeliveryError");
            var errorText = error.Text;
            center.Activate();
            WindowValidationInput.Pump(center.Dispatcher);
            Keyboard.Focus(focus);
            scroller.ScrollToEnd();
            WindowValidationInput.Pump(center.Dispatcher);
            var offset = scroller.VerticalOffset;
            Must(offset > 0 && ReferenceEquals(Keyboard.FocusedElement, focus), "时间刷新前须有非零滚动及真实按钮焦点。");
            var commits = 0;
            workspace.StateChanged += (_, _) => commits++;
            foreach (var (seconds, expected) in new[] { (0, "刚刚"), (59, "刚刚"), (60, "已过 1 分钟"), (3599, "已过 59 分钟"), (3600, "已过 1 小时"), (7200, "已过 2 小时") })
            {
                clock.UtcNow = dueUtc.AddSeconds(seconds);
                coordinator.Tick(clock.UtcNow);
                WindowValidationInput.Pump(center.Dispatcher);
                center.UpdateLayout();
                if (labels.Any(label => label.Text != expected) ||
                    badges.Any(badge => AutomationProperties.GetName(badge) != expected) ||
                    !rows.SequenceEqual(VisualDescendants<Border>(center).Where(row => row.Tag is ActiveReminderOccurrenceKey)) ||
                    !models.SequenceEqual(rows.Select(row => row.DataContext)) ||
                    !labels.SequenceEqual(badges.Select(badge => VisualDescendants<TextBlock>(badge).Single())) ||
                    !ReferenceEquals(Keyboard.FocusedElement, focus) || Math.Abs(scroller.VerticalOffset - offset) > 0.1 ||
                    commits != 0 || error.Text != errorText || error.Visibility != Visibility.Visible)
                {
                    Console.Error.WriteLine($"#223 relative time at {seconds}s: expected={expected}, actual={labels[0].Text}, commits={commits}");
                    return false;
                }
            }
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteSettingsFiles(settingsPath);
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
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var saved = true;
            foreach (var text in new[] { "批量已读探针甲", "批量已读探针乙", "批量已读探针丙" })
            {
                saved &= workspace.Apply(new SaveNoteChange(
                    null,
                    text,
                    new DateTime(2036, 8, 20),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc))).Succeeded;
            }

            if (!saved || workspace.Snapshot.Settings.Notes.Count != 3)
            {
                return ReminderCenterAcknowledgeAllExerciseResult.Failed;
            }

            var notes = workspace.Snapshot.Settings.Notes.ToArray();
            var rules = notes.Select(note => note.ReminderRules.Single()).ToArray();
            var keys = rules
                .Select(rule => new ActiveReminderOccurrenceKey(rule.Id, dueUtc))
                .ToArray();
            var occurrenceKeys = rules
                .Select(rule => ReminderOccurrenceKey.Create(rule, dueUtc).ToString())
                .ToArray();
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
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

            // 第一条走逐条「知道了」作为语义对照，其余两条走头部「全部知道了」。
            var singleAcknowledged = center.RequestAcknowledgement(keys[0]);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var commitCount = 0;
            EventHandler countCommit = (_, _) => commitCount++;
            workspace.StateChanged += countCommit;
            var batchRaised = center.RequestAcknowledgeAll();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            workspace.StateChanged -= countCommit;
            var persisted = new SettingsStore(clock: clock).Load(settingsPath).Settings;
            var dispositionKeysMatch = true;
            for (var index = 0; index < rules.Length; index++)
            {
                dispositionKeysMatch &=
                    string.Equals(
                        workspace.Snapshot.Settings.ReminderStates[rules[index].Id].LastDispositionOccurrenceKey,
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
                workspace.GetDueReminderOccurrences(dueUtc).Count == 0;

            var failDueUtc = dueUtc.AddMinutes(30);
            var failureSaved =
                workspace.Apply(new SaveNoteChange(
                    null,
                    "批量失败探针丁",
                    new DateTime(2036, 8, 20),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(failDueUtc))).Succeeded &&
                workspace.Apply(new SaveNoteChange(
                    null,
                    "批量失败探针戊",
                    new DateTime(2036, 8, 20),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(failDueUtc))).Succeeded &&
                workspace.Apply(new SaveNoteChange(
                    null,
                    "批量失败后删除探针己",
                    new DateTime(2036, 8, 20),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(failDueUtc))).Succeeded;
            clock.UtcNow = failDueUtc;
            coordinator.Tick(failDueUtc);
            var failureCenter = coordinator.ActiveWindow;
            failureCenter?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var failureRules = workspace.Snapshot.Settings.Notes
                .Skip(notes.Length)
                .Select(note => note.ReminderRules.Single())
                .ToArray();
            var failureKeys = failureRules
                .Select(rule => new ActiveReminderOccurrenceKey(rule.Id, failDueUtc))
                .ToArray();
            if (!failureSaved ||
                failureCenter is null ||
                coordinator.ActiveEntryCount != 3 ||
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
            var beforeFailure = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var failureRaised = failureCenter.RequestAcknowledgeAll();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var diskDuringFailure = new SettingsStore(clock: clock).Load(settingsPath).Settings;
            var failureRetainsAllAtomically =
                failureRaised &&
                coordinator.ActiveEntryCount == 3 &&
                failureKeys.All(key => failureCenter.IsFailureVisible(key)) &&
                failureKeys.All(key => !failureCenter.IsAcknowledgeEnabled(key)) &&
                failureKeys.All(key => failureCenter.IsOpenSourceEnabled(key)) &&
                !failureCenter.IsAcknowledgeAllEnabled &&
                string.Equals(beforeFailure, SettingsContract.SerializeCanonical(workspace.Snapshot.Settings), StringComparison.Ordinal) &&
                failureRules.All(rule =>
                    workspace.Snapshot.Settings.ReminderStates[rule.Id].LastDispositionOccurrenceKey is null &&
                    diskDuringFailure.ReminderStates[rule.Id].LastDispositionOccurrenceKey is null);

            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            clock.UtcNow = failDueUtc.AddSeconds(3);
            var deletedNote = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "批量失败后删除探针己");
            Must(workspace.Apply(new DeleteNoteChange(deletedNote.Id)).Succeeded, "批量失败后通过来源删除条目失败。");
            Must(workspace.Apply(new SaveNoteChange(null, "批量失败期间新到达", new DateTime(2036, 8, 20), false,
                Array.Empty<string>(), ReminderSchedule.CreateOnce(failDueUtc.AddSeconds(4)))).Succeeded,
                "无法建立原批次之外的新发生项。");
            clock.UtcNow = failDueUtc.AddSeconds(4);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var newRule = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "批量失败期间新到达").ReminderRules.Single();
            var newKey = new ActiveReminderOccurrenceKey(newRule.Id, failDueUtc.AddSeconds(4));
            var retainedFailureKeys = failureKeys.Take(2).ToArray();
            bool HasRow(ActiveReminderOccurrenceKey key) => VisualDescendants<Border>(failureCenter)
                .Any(row => row.Tag is ActiveReminderOccurrenceKey rowKey && rowKey == key);
            var noEarlyRetry =
                coordinator.ActiveEntryCount == 3 &&
                retainedFailureKeys.All(key => failureCenter.IsFailureVisible(key)) &&
                !HasRow(failureKeys[2]) && failureCenter.IsAcknowledgeEnabled(newKey);
            clock.UtcNow = failDueUtc.AddSeconds(5);
            commitCount = 0;
            workspace.StateChanged += countCommit;
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            workspace.StateChanged -= countCommit;
            Console.Error.WriteLine($"REMINDER_BATCH_RETRY_COMMITS: {commitCount}");
            var failureOccurrenceKeys = failureRules
                .Select(rule => ReminderOccurrenceKey.Create(rule, failDueUtc).ToString())
                .ToArray();
            var persistedAfterRetry = new SettingsStore(clock: clock).Load(settingsPath).Settings;
            var backoffRetryCompletesBatch =
                noEarlyRetry &&
                commitCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                failureCenter.IsVisible && HasRow(newKey) &&
                !HasRow(failureKeys[2]) &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(failureRules[2].Id) &&
                workspace.Snapshot.Settings.ReminderStates[newRule.Id].LastDispositionOccurrenceKey is null &&
                persistedAfterRetry.ReminderStates[newRule.Id].LastDispositionOccurrenceKey is null &&
                failureRules.Take(2)
                    .Select((rule, index) => (rule, index))
                    .All(pair =>
                        string.Equals(
                            workspace.Snapshot.Settings.ReminderStates[pair.rule.Id].LastDispositionOccurrenceKey,
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
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(workspace.Apply(new SaveNoteChange(
                    null,
                    "过期自定义稍后重试探针",
                    new DateTime(2036, 5, 11),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立过期自定义稍后重试探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(clock.UtcNow);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(center.IsSnoozeEnabled(key), "送达后稍后提醒动作未启用。");

            var beforeFailure = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
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
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeFailure &&
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
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeFailure,
                "过期自定义稍后重试没有释放重试锁存并把错误归位到来源控件。");

            Must(
                center.RequestSnooze(key, ReminderSnoozeOption.TenMinutes),
                "过期错误后无法点击 10 分钟稍后控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var state = workspace.Snapshot.Settings.ReminderStates[rule.Id];
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
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var repeating = new FixedIntervalReminderRule(
                "stale-delivery-repeat",
                dueUtc,
                new ReminderInterval(15));
            var futureOnce = new AbsoluteOnceReminderRule(
                "stale-delivery-future",
                dueUtc.AddHours(1));
            Must(workspace.Apply(new SaveNoteChange(
                    null,
                    "完成重开丢弃旧送达重试探针",
                    new DateTime(2036, 5, 14),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { repeating, futureOnce })).Succeeded,
                "无法建立旧送达重试探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
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
            var afterReopen = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var diskAfterReopen = File.ReadAllText(settingsPath);
            var staleDelivery = workspace.ConfirmReminderDisplayed(
                note,
                repeating.Id,
                dueUtc);
            Must(
                !staleDelivery.Succeeded &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == afterReopen &&
                File.ReadAllText(settingsPath) == diskAfterReopen,
                "cursor 已越过旧 occurrence 时仍被错误视为送达幂等成功。");

            clock.UtcNow = dueUtc.AddMinutes(16).AddSeconds(2);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var state = workspace.Snapshot.Settings.ReminderStates[repeating.Id];
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                !coordinator.HasPendingPersistenceRetry &&
                state.HandledThroughUtc == dueUtc.AddMinutes(15) &&
                state.LastDeliveredOccurrenceKey is null &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == afterReopen &&
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
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var repeating = new FixedIntervalReminderRule(
                "stale-snooze-repeat",
                dueUtc,
                new ReminderInterval(15));
            var futureOnce = new AbsoluteOnceReminderRule(
                "stale-snooze-future",
                dueUtc.AddHours(1));
            Must(workspace.Apply(new SaveNoteChange(
                    null,
                    "完成重开丢弃旧稍后重试探针",
                    new DateTime(2036, 5, 15),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { repeating, futureOnce })).Succeeded,
                "无法建立旧稍后重试探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
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
            var afterReopen = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var diskAfterReopen = File.ReadAllText(settingsPath);

            clock.UtcNow = dueUtc.AddMinutes(16).AddSeconds(2);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var state = workspace.Snapshot.Settings.ReminderStates[repeating.Id];
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                !coordinator.HasPendingPersistenceRetry &&
                state.HandledThroughUtc == dueUtc.AddMinutes(15) &&
                state.LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(repeating, dueUtc).ToString() &&
                state.ActiveSnooze is null &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == afterReopen &&
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
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var firstRule = new AbsoluteOnceReminderRule("complete-click-first", dueUtc);
            var secondRule = new AbsoluteOnceReminderRule(
                "complete-click-second",
                dueUtc.AddMinutes(1));
            Must(workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心完成点击探针",
                    new DateTime(2036, 5, 12),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { firstRule, secondRule })).Succeeded,
                "无法建立提醒中心完成点击探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var firstKey = new ActiveReminderOccurrenceKey(firstRule.Id, dueUtc);
            clock.UtcNow = secondRule.ScheduledUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(clock.UtcNow);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(
                center.IsCompleteEnabled(firstKey) &&
                coordinator.ActiveEntryCount == 2,
                "完成动作在所有 occurrence 送达后仍不可用。");

            var beforeFailure = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var diskBeforeFailure = File.ReadAllText(settingsPath);
            // #155：完成确认以取消为安全默认——先取消确认，便签、状态与中心条目全量保留。
            QueueConfirmationDialogResponse(center, confirm: false);
            Must(center.RequestComplete(firstKey), "无法点击真实完成便签按钮。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                !workspace.Snapshot.Settings.Notes.Single().IsCompleted &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeFailure &&
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
                !workspace.Snapshot.Settings.Notes.Single().IsCompleted &&
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == beforeFailure &&
                File.ReadAllText(settingsPath) == diskBeforeFailure &&
                coordinator.ActiveEntryCount == 2 &&
                center.IsFailureVisible(firstKey),
                "完成保存失败没有全量保留便签、状态与中心条目。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            clock.UtcNow = secondRule.ScheduledUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                workspace.Snapshot.Settings.Notes.Single().IsCompleted &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                workspace.Snapshot.Settings.ReminderStates[firstRule.Id].LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(firstRule, dueUtc).ToString() &&
                workspace.Snapshot.Settings.ReminderStates[secondRule.Id].LastDispositionOccurrenceKey ==
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
}
