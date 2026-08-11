using System.Globalization;
using MinminCountdown.Core;

namespace MinminCountdown;

internal enum ReminderEditingMode
{
    None,
    Once,
    Repeating,
    CountdownRelative,
    Calendar
}

internal enum ReminderIntervalSelection
{
    FifteenMinutes,
    ThirtyMinutes,
    SixtyMinutes,
    Custom
}

internal sealed record ReminderEditingRequest(
    ReminderEditingMode Mode,
    DateTime? FirstDueDate,
    int FirstDueHour,
    int FirstDueMinute,
    ReminderIntervalSelection IntervalSelection,
    string CustomIntervalMinutes,
    DateTimeOffset NowUtc,
    NoteReminder? ExistingReminder,
    bool ScheduleDirty);

internal sealed record ReminderEditingResult(
    bool Succeeded,
    NoteReminder Reminder,
    string Error);

internal sealed record ReminderEditingDefaultResult(
    bool Succeeded,
    DateTimeOffset? FirstDueBeijing,
    string Error);

internal sealed record ReminderRuleEditingRequest(
    string? ExistingRuleId,
    ReminderEditingMode Mode,
    DateTime? FirstDueDate,
    int FirstDueHour,
    int FirstDueMinute,
    ReminderIntervalSelection IntervalSelection,
    string CustomIntervalMinutes,
    DateTimeOffset NowUtc,
    DateTimeOffset? CountdownTargetUtc = null,
    string OffsetMinutesBefore = "",
    CalendarFrequency CalendarFrequency = CalendarFrequency.Daily,
    string CalendarInterval = "1",
    IReadOnlyList<DayOfWeek>? CalendarWeekdays = null,
    string CalendarDay = "1",
    string CalendarMonth = "1",
    bool CalendarLastDay = false);

internal enum ReminderRuleEditingErrorTarget
{
    None,
    Mode,
    FirstDue,
    Interval,
    RelativeOffset,
    CalendarInterval,
    CalendarSelector,
    Collection
}

internal sealed record ReminderRuleEditingResult(
    bool Succeeded,
    IReadOnlyList<ReminderRule> Rules,
    string Error,
    ReminderRuleEditingErrorTarget ErrorTarget);

internal sealed class ReminderEditing
{
    private const string InvalidIntervalError = "自定义提醒必须是 1 到 1440 分钟。";

    public ReminderEditingResult Build(ReminderEditingRequest request)
    {
        if (request.Mode == ReminderEditingMode.None)
        {
            return Success(NoReminder.Instance);
        }

        int? intervalMinutes = null;
        if (request.Mode == ReminderEditingMode.Repeating)
        {
            if (!TryGetIntervalMinutes(
                    request.IntervalSelection,
                    request.CustomIntervalMinutes,
                    out var parsedInterval))
            {
                return Failure(InvalidIntervalError);
            }

            intervalMinutes = parsedInterval;
        }
        else if (request.Mode != ReminderEditingMode.Once)
        {
            return Failure("请选择有效的提醒模式。");
        }

        if (!request.ScheduleDirty)
        {
            if (request.Mode == ReminderEditingMode.Once &&
                request.ExistingReminder is OnceReminder existingOnce)
            {
                return Success(existingOnce);
            }

            if (request.Mode == ReminderEditingMode.Repeating &&
                request.ExistingReminder is RepeatingReminder existingRepeating &&
                existingRepeating.Interval.Minutes == intervalMinutes)
            {
                return Success(existingRepeating);
            }
        }

        if (!ReminderSchedule.TryCreateFirstDueUtc(
                request.FirstDueDate,
                request.FirstDueHour,
                request.FirstDueMinute,
                request.NowUtc,
                out var firstDueUtc,
                out var error))
        {
            return Failure(error);
        }

        NoteReminder reminder = request.Mode == ReminderEditingMode.Once
            ? ReminderSchedule.CreateOnce(firstDueUtc!.Value)
            : ReminderSchedule.CreateRepeating(firstDueUtc!.Value, intervalMinutes!.Value);
        return Success(reminder);
    }

    public ReminderEditingDefaultResult GetDefaultFirstDueBeijing(
        ReminderEditingMode mode,
        ReminderIntervalSelection intervalSelection,
        string customIntervalMinutes,
        DateTimeOffset nowUtc)
    {
        if (mode == ReminderEditingMode.None)
        {
            return new ReminderEditingDefaultResult(true, null, string.Empty);
        }

        var intervalMinutes = 15;
        if (mode == ReminderEditingMode.Repeating)
        {
            if (!TryGetIntervalMinutes(intervalSelection, customIntervalMinutes, out intervalMinutes))
            {
                return new ReminderEditingDefaultResult(false, null, InvalidIntervalError);
            }
        }
        else if (mode != ReminderEditingMode.Once)
        {
            return new ReminderEditingDefaultResult(false, null, "请选择有效的提醒模式。");
        }

        var beijingNow = ChinaStandardTime.ToBeijingTime(nowUtc);
        return new ReminderEditingDefaultResult(
            true,
            ReminderSchedule.GetDefaultFirstDueBeijing(beijingNow, intervalMinutes),
            string.Empty);
    }

    private static bool TryGetIntervalMinutes(
        ReminderIntervalSelection selection,
        string customIntervalMinutes,
        out int intervalMinutes)
    {
        intervalMinutes = selection switch
        {
            ReminderIntervalSelection.FifteenMinutes => 15,
            ReminderIntervalSelection.ThirtyMinutes => 30,
            ReminderIntervalSelection.SixtyMinutes => 60,
            _ => -1
        };
        if (intervalMinutes != -1)
        {
            return true;
        }

        return selection == ReminderIntervalSelection.Custom &&
            int.TryParse(
                customIntervalMinutes,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out intervalMinutes) &&
            intervalMinutes is >= 1 and <= 1440;
    }

    private static ReminderEditingResult Success(NoteReminder reminder) =>
        new(true, reminder, string.Empty);

    private static ReminderEditingResult Failure(string error) =>
        new(false, NoReminder.Instance, error);
}

internal sealed class ReminderRuleEditing
{
    private const string PastFirstDueError = "首次提醒时间必须晚于当前北京时间。";
    private readonly IGuidSource _guidSource;
    private readonly ReminderEditing _scheduleEditor = new();

    public ReminderRuleEditing(IGuidSource? guidSource = null)
    {
        _guidSource = guidSource ?? SystemGuidSource.Instance;
    }

    public ReminderRuleEditingResult SaveRule(
        IReadOnlyList<ReminderRule> existingRules,
        ReminderRuleEditingRequest request)
    {
        ArgumentNullException.ThrowIfNull(existingRules);
        ArgumentNullException.ThrowIfNull(request);

        var existingIndex = -1;
        ReminderRule? existingRule = null;
        if (!string.IsNullOrWhiteSpace(request.ExistingRuleId))
        {
            for (var index = 0; index < existingRules.Count; index++)
            {
                if (string.Equals(
                        existingRules[index].Id,
                        request.ExistingRuleId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = index;
                    existingRule = existingRules[index];
                    break;
                }
            }

            if (existingRule is null)
            {
                return Failure(
                    existingRules,
                    "未找到要编辑的提醒规则。",
                    ReminderRuleEditingErrorTarget.Collection);
            }
        }
        else if (existingRules.Count >= 8)
        {
            return Failure(
                existingRules,
                "每条记录最多只能包含 8 条提醒规则。",
                ReminderRuleEditingErrorTarget.Collection);
        }

        if (request.Mode == ReminderEditingMode.CountdownRelative)
        {
            if (request.CountdownTargetUtc is null ||
                !int.TryParse(
                    request.OffsetMinutesBefore,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var offsetMinutesBefore) ||
                offsetMinutesBefore is < 0 or > 5_256_000 ||
                !ReminderSchedule.TryGetCountdownRelativeScheduledUtc(
                    request.CountdownTargetUtc.Value,
                    offsetMinutesBefore,
                    out var relativeScheduledUtc) ||
                relativeScheduledUtc <= request.NowUtc.ToUniversalTime())
            {
                return Failure(
                    existingRules,
                    "倒计时相对提醒必须提前 0 到 5256000 分钟，且派生时间晚于当前北京时间。",
                    ReminderRuleEditingErrorTarget.RelativeOffset);
            }

            var relativeRuleId = existingRule?.Id ?? _guidSource.NewGuid().ToString("N");
            var relativeRule = new CountdownRelativeReminderRule(relativeRuleId, offsetMinutesBefore);
            var relativeScheduleKey = ReminderRuleSchedule.CreateKey(relativeRule);
            if (existingRules.Where((_, index) => index != existingIndex)
                .Any(existing => ReminderRuleSchedule.CreateKey(existing) == relativeScheduleKey))
            {
                return Failure(
                    existingRules,
                    "不能添加重复的提醒日程。",
                    ReminderRuleEditingErrorTarget.RelativeOffset);
            }

            var relativeCandidate = existingRules.ToArray();
            if (existingIndex >= 0)
            {
                relativeCandidate[existingIndex] = relativeRule;
            }
            else
            {
                relativeCandidate = [.. relativeCandidate, relativeRule];
            }

            return new ReminderRuleEditingResult(
                true,
                Array.AsReadOnly(relativeCandidate),
                string.Empty,
                ReminderRuleEditingErrorTarget.None);
        }

        if (request.Mode == ReminderEditingMode.Calendar)
        {
            return SaveCalendarRule(existingRules, existingIndex, existingRule, request);
        }

        var schedule = _scheduleEditor.Build(new ReminderEditingRequest(
            request.Mode,
            request.FirstDueDate,
            request.FirstDueHour,
            request.FirstDueMinute,
            request.IntervalSelection,
            request.CustomIntervalMinutes,
            request.NowUtc,
            existingRule is null or CountdownRelativeReminderRule or CalendarReminderRule
                ? null
                : ReminderRuleCompatibility.ToLegacySchedule(
                new NoteRecord(
                    "rule-editor",
                    "规则编辑",
                    "2034-01-01",
                    false,
                    Array.Empty<string>(),
                    [existingRule])),
            true));
        if (!schedule.Succeeded)
        {
            var target = request.Mode == ReminderEditingMode.Repeating &&
                         string.Equals(
                             schedule.Error,
                             "自定义提醒必须是 1 到 1440 分钟。",
                             StringComparison.Ordinal)
                ? ReminderRuleEditingErrorTarget.Interval
                : request.Mode is ReminderEditingMode.Once or ReminderEditingMode.Repeating
                    ? ReminderRuleEditingErrorTarget.FirstDue
                    : ReminderRuleEditingErrorTarget.Mode;
            return Failure(existingRules, schedule.Error, target);
        }

        var id = existingRule?.Id ?? _guidSource.NewGuid().ToString("N");
        ReminderRule rule = schedule.Reminder switch
        {
            OnceReminder once => new AbsoluteOnceReminderRule(id, once.FirstDueUtc),
            RepeatingReminder repeating => new FixedIntervalReminderRule(
                id,
                repeating.FirstDueUtc,
                repeating.Interval),
            _ => throw new InvalidOperationException("规则编辑器必须生成有效的提醒规则。")
        };
        var scheduleKey = ReminderRuleSchedule.CreateKey(rule);
        if (existingRules.Where((_, index) => index != existingIndex)
            .Any(existing => ReminderRuleSchedule.CreateKey(existing) == scheduleKey))
        {
            return Failure(
                existingRules,
                "不能添加重复的提醒日程。",
                ReminderRuleEditingErrorTarget.FirstDue);
        }

        var candidate = existingRules.ToArray();
        if (existingIndex >= 0)
        {
            candidate[existingIndex] = rule;
        }
        else
        {
            candidate = [.. candidate, rule];
        }

        return new ReminderRuleEditingResult(
            true,
            Array.AsReadOnly(candidate),
            string.Empty,
            ReminderRuleEditingErrorTarget.None);
    }

    public ReminderRuleEditingResult DeleteRule(
        IReadOnlyList<ReminderRule> existingRules,
        string ruleId)
    {
        ArgumentNullException.ThrowIfNull(existingRules);
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return Failure(
                existingRules,
                "请选择要删除的提醒规则。",
                ReminderRuleEditingErrorTarget.Collection);
        }

        var candidate = existingRules
            .Where(rule => !string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidate.Length == existingRules.Count)
        {
            return Failure(
                existingRules,
                "未找到要删除的提醒规则。",
                ReminderRuleEditingErrorTarget.Collection);
        }

        return new ReminderRuleEditingResult(
            true,
            Array.AsReadOnly(candidate),
            string.Empty,
            ReminderRuleEditingErrorTarget.None);
    }

    public static ReminderRuleEditingResult ValidateChangedSchedules(
        IReadOnlyList<ReminderRule> existingRules,
        IReadOnlyList<ReminderRule> candidateRules,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(existingRules);
        ArgumentNullException.ThrowIfNull(candidateRules);

        foreach (var candidateRule in candidateRules)
        {
            if (candidateRule is CountdownRelativeReminderRule or CalendarReminderRule)
            {
                continue;
            }

            var existingRule = existingRules.FirstOrDefault(rule =>
                string.Equals(rule.Id, candidateRule.Id, StringComparison.OrdinalIgnoreCase));
            if (ReminderRuleSchedule.AreEquivalent(existingRule, candidateRule))
            {
                continue;
            }

            var firstDueUtc = candidateRule switch
            {
                AbsoluteOnceReminderRule once => once.ScheduledUtc,
                FixedIntervalReminderRule repeating => repeating.FirstDueUtc,
                _ => throw new ArgumentOutOfRangeException(nameof(candidateRules))
            };
            if (firstDueUtc <= nowUtc)
            {
                return Failure(
                    candidateRules,
                    PastFirstDueError,
                    ReminderRuleEditingErrorTarget.FirstDue);
            }
        }

        return new ReminderRuleEditingResult(
            true,
            Array.AsReadOnly(candidateRules.ToArray()),
            string.Empty,
            ReminderRuleEditingErrorTarget.None);
    }

    public static ReminderRuleEditingResult ValidateCountdownRules(
        IReadOnlyList<ReminderRule> existingRules,
        IReadOnlyList<ReminderRule> candidateRules,
        DateTimeOffset targetUtc,
        DateTimeOffset nowUtc)
    {
        var scheduleValidation = ValidateChangedSchedules(existingRules, candidateRules, nowUtc);
        if (!scheduleValidation.Succeeded)
        {
            return scheduleValidation;
        }

        foreach (var relative in candidateRules.OfType<CountdownRelativeReminderRule>())
        {
            if (!ReminderSchedule.TryGetCountdownRelativeScheduledUtc(
                    targetUtc,
                    relative.OffsetMinutesBefore,
                    out var scheduledUtc) ||
                scheduledUtc <= nowUtc.ToUniversalTime())
            {
                return Failure(
                    candidateRules,
                    "倒计时相对提醒时间必须晚于当前北京时间。",
                    ReminderRuleEditingErrorTarget.RelativeOffset);
            }
        }

        return new ReminderRuleEditingResult(
            true,
            Array.AsReadOnly(candidateRules.ToArray()),
            string.Empty,
            ReminderRuleEditingErrorTarget.None);
    }

    private static ReminderRuleEditingResult Failure(
        IReadOnlyList<ReminderRule> existingRules,
        string error,
        ReminderRuleEditingErrorTarget target) =>
        new(false, Array.AsReadOnly(existingRules.ToArray()), error, target);

    private ReminderRuleEditingResult SaveCalendarRule(
        IReadOnlyList<ReminderRule> existingRules,
        int existingIndex,
        ReminderRule? existingRule,
        ReminderRuleEditingRequest request)
    {
        if (!int.TryParse(
                request.CalendarInterval,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var interval) ||
            interval is < 1 or > 999)
        {
            return Failure(
                existingRules,
                "日历重复间隔必须是 1 到 999。",
                ReminderRuleEditingErrorTarget.CalendarInterval);
        }

        if (request.FirstDueDate is null ||
            request.FirstDueHour is < 0 or > 23 ||
            request.FirstDueMinute is < 0 or > 59)
        {
            return Failure(
                existingRules,
                "请选择完整的日历起始日期和本地时间。",
                ReminderRuleEditingErrorTarget.FirstDue);
        }

        CalendarSelector selector;
        switch (request.CalendarFrequency)
        {
            case CalendarFrequency.Daily:
                selector = new DailyCalendarSelector();
                break;
            case CalendarFrequency.Weekly:
                var weekdays = request.CalendarWeekdays?.Distinct().OrderBy(
                    static day => day == DayOfWeek.Sunday ? 7 : (int)day).ToArray() ?? [];
                if (weekdays.Length == 0)
                {
                    return Failure(
                        existingRules,
                        "按周重复必须至少选择一个星期。",
                        ReminderRuleEditingErrorTarget.CalendarSelector);
                }

                selector = new WeeklyCalendarSelector(weekdays);
                break;
            case CalendarFrequency.Monthly:
                if (!TryBuildMonthlySelector(request.CalendarDay, request.CalendarLastDay, out var monthly))
                {
                    return Failure(
                        existingRules,
                        "按月重复必须选择 1 到 31 日或显式最后一天。",
                        ReminderRuleEditingErrorTarget.CalendarSelector);
                }

                selector = monthly;
                break;
            case CalendarFrequency.Yearly:
                if (!int.TryParse(
                        request.CalendarMonth,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var month) ||
                    month is < 1 or > 12 ||
                    !TryBuildMonthlySelector(request.CalendarDay, request.CalendarLastDay, out var yearlyDay))
                {
                    return Failure(
                        existingRules,
                        "按年重复必须选择月份及 1 到 31 日或显式最后一天。",
                        ReminderRuleEditingErrorTarget.CalendarSelector);
                }

                selector = new YearlyCalendarSelector(month, yearlyDay.Day, yearlyDay.LastDay);
                break;
            default:
                return Failure(
                    existingRules,
                    "请选择支持的日历重复频率。",
                    ReminderRuleEditingErrorTarget.CalendarSelector);
        }

        var id = existingRule?.Id ?? _guidSource.NewGuid().ToString("N");
        var calendar = new CalendarReminderRule(
            id,
            request.CalendarFrequency,
            interval,
            DateOnly.FromDateTime(request.FirstDueDate.Value),
            new TimeOnly(request.FirstDueHour, request.FirstDueMinute),
            selector);
        var scheduleKey = ReminderRuleSchedule.CreateKey(calendar);
        if (existingRules.Where((_, index) => index != existingIndex)
            .Any(existing => ReminderRuleSchedule.CreateKey(existing) == scheduleKey))
        {
            return Failure(
                existingRules,
                "不能添加重复的提醒日程。",
                ReminderRuleEditingErrorTarget.CalendarSelector);
        }

        var candidate = existingRules.ToArray();
        if (existingIndex >= 0)
        {
            candidate[existingIndex] = calendar;
        }
        else
        {
            candidate = [.. candidate, calendar];
        }

        return new ReminderRuleEditingResult(
            true,
            Array.AsReadOnly(candidate),
            string.Empty,
            ReminderRuleEditingErrorTarget.None);
    }

    private static bool TryBuildMonthlySelector(
        string dayText,
        bool lastDay,
        out MonthlyCalendarSelector selector)
    {
        if (lastDay)
        {
            selector = new MonthlyCalendarSelector(null, true);
            return true;
        }

        if (int.TryParse(dayText, NumberStyles.None, CultureInfo.InvariantCulture, out var day) &&
            day is >= 1 and <= 31)
        {
            selector = new MonthlyCalendarSelector(day, false);
            return true;
        }

        selector = new MonthlyCalendarSelector(null, false);
        return false;
    }

}
