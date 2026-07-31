using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static class ValidationExercises
{
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

        return true;
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
            var onceDueUtc = nowUtc.AddMinutes(-1);
            var onceSave = reminderWorkspace.Apply(new SaveNoteChange(
                null,
                "工作区一次性提醒事务",
                new DateTime(2034, 5, 6),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateOnce(onceDueUtc)));
            var dueReminder = reminderWorkspace.GetDueReminders(nowUtc).Single(note =>
                note.Text == "工作区一次性提醒事务");
            var reminderConfirmation = reminderWorkspace.ConfirmReminderDisplayed(
                dueReminder,
                nowUtc);
            var afterReminderConfirmation = DesktopWorkspace.Load(settingsPath, store);
            var deliveredReminder = afterReminderConfirmation.Snapshot.Settings.Notes
                .Single(note => note.Text == "工作区一次性提醒事务")
                .Reminder as OnceReminder;

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
                reminderConfirmation.Succeeded &&
                deliveredReminder?.Delivery == OnceDeliveryState.Delivered &&
                afterReminderConfirmation.GetDueReminders(nowUtc).Count == 0 &&
                reloaded.UtcNow == nowUtc &&
                reloaded.BeijingNow.Offset == TimeSpan.FromHours(8);
        }
        finally
        {
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            File.Delete(settingsPath);
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
            WriteStartupRequest(startupRequestPath, launchToken, "active");
            var active = StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true);
            Must(
                active.LaunchToken == launchToken &&
                active.AcceptanceReadOnly &&
                active.Request.AcceptanceState == "active" &&
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
            WriteStartupRequest(startupRequestPath, string.Empty, "active");
            MustThrow<InvalidDataException>(
                () => StartupAcceptanceProtocol.Load(temporaryRoot, requireStartupRequest: true),
                "启动验收 interface 未拒绝空 launch token。");
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

    public static bool RunValidationReportContractBehavior(
        IReadOnlyDictionary<string, object?> report)
    {
        var json = JsonSerializer.Serialize(report);
        var repeatedJson = JsonSerializer.Serialize(report);
        using var document = JsonDocument.Parse(json);
        Must(
            document.RootElement.ValueKind == JsonValueKind.Object,
            "validate-only report 未序列化为 JSON object。");

        var propertyNames = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Must(
            propertyNames.Length == report.Count &&
            propertyNames.Distinct(StringComparer.Ordinal).Count() == propertyNames.Length,
            "validate-only report 的顶层字段不唯一。");
        Must(
            string.Equals(json, repeatedJson, StringComparison.Ordinal),
            "同一 validate-only report 的 JSON 序列化结果不稳定。");

        foreach (var requiredBehavior in new[]
                 {
                     "Valid",
                     "DesktopWorkspaceInterfaceBehavior",
                     "ReminderEditingInterfaceBehavior",
                     "StartupAcceptanceProtocolInterfaceBehavior",
                     "HolidayCalendarInterfaceBehavior"
                 })
        {
            Must(
                report.TryGetValue(requiredBehavior, out var value) && value is true,
                $"validate-only report 的真实行为字段 {requiredBehavior} 未通过。");
        }

        if (report.TryGetValue("ValidationReportContractBehavior", out var contractBehavior))
        {
            Must(
                contractBehavior is true &&
                report.TryGetValue("ValidationReportModuleDeep", out var compatibilityValue) &&
                compatibilityValue is true,
                "validate-only report 的契约结果与兼容字段不一致。");
        }

        return true;
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

    private static void WriteStartupRequest(string path, string launchToken, string acceptanceState)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            launchToken,
            acceptanceReadOnly = true,
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
