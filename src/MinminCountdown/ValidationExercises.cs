using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static class ValidationExercises
{
    internal const double FloatingNightPlushEdgeBrightnessLimit = 0.012;
    internal const double FloatingNightPlushEdgeSaturationLimit = 0.35;

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
                     "HolidayCalendarInterfaceBehavior",
                     "MainReferenceDecorationsShareScaleAtMinimumAndWideSizes",
                     "MainReferenceDecorationLayerOrderMatches",
                     "MainWorkspaceResourcesFollowDayNightTheme",
                     "MainDynamicCountdownCardsUseThemeResources",
                     "MainDynamicNoteCardsUseThemeResources",
                     "FloatingReferenceContentSafeAreaMatches",
                     "FloatingReferenceBaselineGateIsolationBehavior",
                     "FloatingReferenceFreshnessGateIsolationBehavior",
                     "FloatingLongNotesBodyHasNoManualLineBreaks",
                     "FloatingLongNotesNaturalWraps",
                     "FloatingLongNotesUsesOnlyOuterScroller",
                     "FloatingLongNotesOuterScrollable",
                     "FloatingLongNotesTopBottomReachable",
                     "FloatingLongNotesSafeAreaMatches",
                     "FloatingLongNotesLayoutValid",
                     "FloatingTitleUsesSemiBoldWeight",
                     "FloatingNoteInputWraps",
                     "FloatingNoteInputScrolls",
                     "FloatingInitialWorkspaceStateMatches",
                     "FloatingWorkspaceSwitchingStaysSynchronized",
                     "FloatingCountdownSelectionSurvivesDayNightThemes",
                     "FloatingQuickAddDayResourcesFollowLocalTheme",
                     "FloatingQuickAddNightResourcesFollowLocalTheme",
                     "FloatingQuickAddIndependentFromApplicationAutoTheme",
                     "FloatingQuickAddThemeResourceIsolationBehavior",
                     "FloatingQuickAddDayExpandedScrolledCaptureWritten",
                     "FloatingQuickAddNightExpandedScrolledCaptureWritten",
                     "FloatingNightPlushSourceAlphaBoundsMatch",
                     "FloatingNightPlushEdgeBrightnessWithinLimit",
                     "FloatingNightPlushEdgeSaturationWithinLimit",
                     "FloatingNightPlushEdgeGlowGateIsolationBehavior",
                     "FloatingSecondaryRemainingUnitSpacingMatches",
                     "FloatingSecondaryRemainingLayoutMatches",
                     "FloatingSecondaryRemainingGateIsolationBehavior",
                     "FloatingScrollBarLocalStyleScoped",
                     "FloatingScrollBarHitTargetUsable",
                     "FloatingScrollBarVisualWeightProgression",
                     "FloatingScrollBarUsesWarmThemeTokens",
                     "FloatingScrollBarContentRemainsSafe",
                     "FloatingScrollBarInputPathsWork",
                     "FloatingScrollBarGateIsolationBehavior",
                     "FloatingScrollBarDayNightStateCapturesReady",
                     "UiQualityGateIsolationBehavior"
                 })
        {
            var quickAddThemePrefix = requiredBehavior switch
            {
                "FloatingQuickAddDayResourcesFollowLocalTheme" => "FloatingQuickAddDayResource",
                "FloatingQuickAddNightResourcesFollowLocalTheme" => "FloatingQuickAddNightResource",
                _ => null
            };
            var quickAddFailureDetails = quickAddThemePrefix is null
                ? string.Empty
                : $" Checks={JsonSerializer.Serialize(report[$"{quickAddThemePrefix}Checks"])};" +
                  $" Diagnostics={JsonSerializer.Serialize(report[$"{quickAddThemePrefix}Diagnostics"])}";
            Must(
                report.TryGetValue(requiredBehavior, out var value) && value is true,
                $"validate-only report 的真实行为字段 {requiredBehavior} 未通过。" +
                quickAddFailureDetails);
        }

        foreach (var sampleCountField in new[]
                 {
                     "MainDynamicCountdownCardSampleCount",
                     "MainDynamicNoteCardSampleCount"
                 })
        {
            Must(
                report.TryGetValue(sampleCountField, out var value) &&
                value is int sampleCount &&
                sampleCount > 0,
                $"validate-only report 的动态卡片样本字段 {sampleCountField} 未命中合格样本。");
        }

        Must(
            report.TryGetValue("FloatingReferenceBaselineRequested", out var baselineRequestedValue) &&
            baselineRequestedValue is bool baselineRequested &&
            report.TryGetValue("FloatingReferenceBaselineManifestAvailable", out var baselineAvailableValue) &&
            baselineAvailableValue is bool baselineAvailable &&
            report.TryGetValue("FloatingReferenceBaselineValidated", out var baselineValidatedValue) &&
            baselineValidatedValue is bool baselineValidated &&
            (baselineRequested
                ? baselineAvailable && baselineValidated
                : !baselineAvailable && !baselineValidated),
            "悬浮窗参考基线报告没有区分源码树实测与不携带参考资产的运行时发布包。");

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
    bool LongNoteLayoutValid);

internal sealed record FloatingReferenceFreshnessGateResults(
    bool ExpectedStateBindingsMatch,
    bool CaptureIdsUnique,
    bool StateFingerprintsMatch,
    bool StateFingerprintsUnique,
    bool PngHashesUnique,
    bool TopBottomPixelsDiffer,
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
            [nameof(LongNoteLayoutValid)] = LongNoteLayoutValid
        };
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
