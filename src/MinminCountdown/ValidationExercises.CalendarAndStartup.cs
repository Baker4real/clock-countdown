using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
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

            // 安装器轮询启动请求时持有不共享删除的读句柄：released -> ready 要等句柄关闭后完成替换；
            // 句柄超过约 2 秒退避预算仍不关闭时带路径失败，启动请求保持 released，不留临时文件。
            WriteStartupRequest(startupRequestPath, launchToken, "released");
            var installerPoll = new FileStream(startupRequestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var installerPollClosed = Task.Delay(500).ContinueWith(_ => installerPoll.Dispose(), TaskScheduler.Default);
            bool readyAfterPollClosed;
            try
            {
                readyAfterPollClosed = released.MarkAcceptanceReady();
            }
            finally
            {
                installerPollClosed.Wait();
            }

            Must(
                readyAfterPollClosed && released.ReadAcceptanceState() == "ready",
                "启动验收 interface 未等安装器读句柄关闭后完成 released -> ready。");

            WriteStartupRequest(startupRequestPath, launchToken, "released");
            Exception? stuckPollFailure = null;
            using (new FileStream(startupRequestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    released.MarkAcceptanceReady();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    stuckPollFailure = exception;
                }
            }

            Must(
                stuckPollFailure?.Message.Contains(startupRequestPath, StringComparison.Ordinal) == true &&
                released.ReadAcceptanceState() == "released" &&
                Directory.GetFiles(temporaryRoot, "*.tmp").Length == 0,
                "启动验收 interface 未在读句柄超过退避预算时带路径失败并保持 released。");

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
                var workdayEndUtc = calendar.GetWorkdayEndUtc(workdayNowUtc, preferences);

                Must(
                    calendar.State == "Ready" &&
                    calendar.StatusText == "已同步 2034 年法定节假日与调休" &&
                    initialRefresh &&
                    stateChangedCount == 1,
                    "工作日历 interface 未从本地缓存进入 Ready 状态。");
                Must(
                    workdayEndUtc == new DateTimeOffset(2034, 5, 6, 10, 0, 0, TimeSpan.Zero) &&
                    calendar.GetEarnings(workdayNowUtc, preferences) == 1550m,
                    "工作日历 interface 未按北京时间和当月工作日派生下班时刻与今日收入。");
                Must(
                    calendar.GetWorkdayEndUtc(nonWorkdayNowUtc, preferences) is null &&
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
                var nextYearWorkdayEndUtc = calendar.GetWorkdayEndUtc(nextYearNowUtc, preferences);
                Must(
                    !sameYearRefresh &&
                    nextYearRefresh &&
                    calendar.State == "Ready" &&
                    calendar.StatusText == "已同步 2035 年法定节假日与调休" &&
                    stateChangedCount == 2 &&
                    nextYearWorkdayEndUtc == new DateTimeOffset(2035, 5, 6, 10, 0, 0, TimeSpan.Zero) &&
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
                    calendar.GetWorkdayEndUtc(nowUtc, preferences) is null &&
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
                    calendar.GetWorkdayEndUtc(nowUtc, preferences) is not null &&
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
                    packagedCalendar.GetWorkdayEndUtc(nowUtc, preferences) is not null &&
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
                    conflictCalendar.GetWorkdayEndUtc(nowUtc, preferences) is null &&
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
                    missingCalendar.GetWorkdayEndUtc(unsupportedNowUtc, preferences) is null &&
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

    // #241：交互时钟每拍后只按工作区快照时钟的当前时刻重算下一次间隔，
    // 期望值按“下一个四分之一秒边界之后 10ms”逐例手算。
    public static bool RunInteractiveClockCadenceBehavior()
    {
        var boundary = new DateTimeOffset(2026, 10, 28, 2, 0, 0, TimeSpan.Zero);
        void Expect(string scenario, DateTimeOffset nowUtc, int expectedMilliseconds)
        {
            var actual = InteractiveClockCadence.NextInterval(nowUtc);
            Must(
                actual == TimeSpan.FromMilliseconds(expectedMilliseconds),
                $"#241：{scenario}时下一拍应在 {expectedMilliseconds}ms 后，实际 {actual.TotalMilliseconds}ms。");
        }

        Expect("正好在四分之一秒边界", boundary, 260);
        Expect("边界前 1ms", boundary.AddMilliseconds(-1), 11);
        Expect("边界后 249ms", boundary.AddMilliseconds(249), 11);
        Expect("边界后 1ms", boundary.AddMilliseconds(1), 259);
        Expect("已落在边界后 10ms", boundary.AddMilliseconds(10), 250);
        Expect("北京时间偏移", new DateTimeOffset(2026, 10, 28, 10, 0, 0, 249, TimeSpan.FromHours(8)), 11);

        // 上一拍落在 02:00:00.010 之后系统时间被改或机器睡眠：下一次间隔只看新时刻。
        var previousTick = boundary.AddMilliseconds(10);
        Expect("时钟向前跳变 1h02m03.456s", previousTick + new TimeSpan(0, 1, 2, 3, 456), 44);
        Expect("时钟向后跳变 3m07.123s", previousTick - new TimeSpan(0, 0, 3, 7, 123), 123);
        Expect("睡眠 8h17m33.333s 后恢复", previousTick + new TimeSpan(0, 8, 17, 33, 333), 167);

        var minimum = TimeSpan.FromMilliseconds(10);
        var maximum = TimeSpan.FromMilliseconds(260);
        foreach (var extreme in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue })
        {
            var interval = InteractiveClockCadence.NextInterval(extreme);
            Must(
                interval >= minimum && interval <= maximum,
                $"#241：{extreme:O} 的下一拍间隔 {interval.TotalMilliseconds}ms 越出 10–260ms。");
        }

        var quarterTicks = TimeSpan.FromMilliseconds(250).Ticks;
        var landingTicks = TimeSpan.FromMilliseconds(10).Ticks;
        for (var millisecond = 0; millisecond < 1000; millisecond++)
        {
            foreach (var extraTicks in new long[] { 0, 1, 5_000, 9_999 })
            {
                var nowUtc = boundary.AddTicks(millisecond * TimeSpan.TicksPerMillisecond + extraTicks);
                var interval = InteractiveClockCadence.NextInterval(nowUtc);
                if (interval < minimum || interval > maximum ||
                    (nowUtc + interval).UtcTicks % quarterTicks != landingTicks)
                {
                    throw new InvalidOperationException(
                        $"#241：{nowUtc:O} 之后的下一拍间隔 {interval.TotalMilliseconds}ms 越出 10–260ms 或没有落在四分之一秒边界后 10ms。");
                }
            }
        }

        return true;
    }
}
