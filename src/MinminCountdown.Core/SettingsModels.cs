namespace MinminCountdown.Core;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public static class SettingsSchema
{
    public const int CurrentVersion = 10;
    public const string ChinaStandardTimeZoneId = "China Standard Time";
}

/// <summary>
/// 主题偏好字段保留读取以兼容旧设置文件；应用固定为单一日间主题，任何值一律按日间解释。
/// </summary>
public enum ThemePreferenceMode
{
    Auto,
    Day,
    Night
}

public sealed record CountdownPreferences(
    string DisplayPrecision,
    decimal MonthlySalary,
    TimeSpan WorkStart,
    TimeSpan WorkEnd,
    ThemePreferenceMode ThemePreference)
{
    public CountdownPreferences(
        string displayPrecision,
        decimal monthlySalary,
        TimeSpan workStart,
        TimeSpan workEnd)
        : this(displayPrecision, monthlySalary, workStart, workEnd, ThemePreferenceMode.Auto)
    {
    }
}

public sealed record CountdownRecord(
    string Id,
    string Name,
    DateTimeOffset TargetUtc,
    IReadOnlyList<string> TagIds,
    IReadOnlyList<ReminderRule> ReminderRules)
{
    public CountdownRecord(
        string id,
        string name,
        DateTimeOffset targetUtc,
        IReadOnlyList<string> tagIds)
        : this(id, name, targetUtc, tagIds, Array.Empty<ReminderRule>())
    {
    }
}

public abstract record NoteReminder;

public sealed record NoReminder : NoteReminder
{
    public static NoReminder Instance { get; } = new();

    private NoReminder()
    {
    }
}

public enum OnceDeliveryState
{
    Pending,
    Delivered
}

public sealed record OnceReminder(
    DateTimeOffset FirstDueUtc,
    OnceDeliveryState Delivery) : NoteReminder;

public readonly record struct ReminderInterval
{
    public ReminderInterval(int minutes)
    {
        if (minutes is < 1 or > 1440)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes));
        }

        Minutes = minutes;
    }

    public int Minutes { get; }
}

public sealed record RepeatingReminder(
    DateTimeOffset FirstDueUtc,
    ReminderInterval Interval) : NoteReminder;

public abstract record ReminderRule(string Id);

public sealed record AbsoluteOnceReminderRule(
    string Id,
    DateTimeOffset ScheduledUtc) : ReminderRule(Id);

public sealed record FixedIntervalReminderRule(
    string Id,
    DateTimeOffset FirstDueUtc,
    ReminderInterval Interval) : ReminderRule(Id);

public sealed record CountdownRelativeReminderRule(
    string Id,
    int OffsetMinutesBefore) : ReminderRule(Id);

public enum CalendarFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}

public abstract record CalendarSelector;

public sealed record DailyCalendarSelector : CalendarSelector;

public sealed record WeeklyCalendarSelector(
    IReadOnlyList<DayOfWeek> Weekdays) : CalendarSelector;

public sealed record MonthlyCalendarSelector(
    int? Day,
    bool LastDay) : CalendarSelector;

public sealed record YearlyCalendarSelector(
    int Month,
    int? Day,
    bool LastDay) : CalendarSelector;

public sealed record CalendarReminderRule(
    string Id,
    CalendarFrequency Frequency,
    int Interval,
    DateOnly StartDate,
    TimeOnly LocalTime,
    CalendarSelector Selector) : ReminderRule(Id);

public abstract record ReminderRuleScheduleKey
{
    public sealed record AbsoluteOnce(DateTimeOffset ScheduledUtc) : ReminderRuleScheduleKey;

    public sealed record FixedInterval(
        DateTimeOffset FirstDueUtc,
        int IntervalMinutes) : ReminderRuleScheduleKey;

    public sealed record CountdownRelative(int OffsetMinutesBefore) : ReminderRuleScheduleKey;

    public sealed record Calendar(
        CalendarFrequency Frequency,
        int Interval,
        DateOnly StartDate,
        TimeOnly LocalTime,
        CalendarSelectorKey Selector) : ReminderRuleScheduleKey;

    public abstract record CalendarSelectorKey;

    public sealed record Daily : CalendarSelectorKey;

    public sealed record Weekly(int WeekdayMask) : CalendarSelectorKey;

    public sealed record Monthly(int? Day, bool LastDay) : CalendarSelectorKey;

    public sealed record Yearly(int Month, int? Day, bool LastDay) : CalendarSelectorKey;
}

public static class ReminderRuleSchedule
{
    public static bool AreEquivalent(ReminderRule? left, ReminderRule? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        CreateKey(left) == CreateKey(right);

    public static ReminderRuleScheduleKey CreateKey(ReminderRule rule) => rule switch
    {
        AbsoluteOnceReminderRule once =>
            new ReminderRuleScheduleKey.AbsoluteOnce(once.ScheduledUtc.ToUniversalTime()),
        FixedIntervalReminderRule repeating =>
            new ReminderRuleScheduleKey.FixedInterval(
                repeating.FirstDueUtc.ToUniversalTime(),
                repeating.Interval.Minutes),
        CountdownRelativeReminderRule relative =>
            new ReminderRuleScheduleKey.CountdownRelative(relative.OffsetMinutesBefore),
        CalendarReminderRule calendar =>
            new ReminderRuleScheduleKey.Calendar(
                calendar.Frequency,
                calendar.Interval,
                calendar.StartDate,
                calendar.LocalTime,
                CreateCalendarSelectorKey(calendar.Selector)),
        _ => throw new ArgumentOutOfRangeException(nameof(rule))
    };

    private static ReminderRuleScheduleKey.CalendarSelectorKey CreateCalendarSelectorKey(
        CalendarSelector selector) => selector switch
    {
        DailyCalendarSelector => new ReminderRuleScheduleKey.Daily(),
        WeeklyCalendarSelector weekly =>
            new ReminderRuleScheduleKey.Weekly(CreateWeekdayMask(weekly.Weekdays)),
        MonthlyCalendarSelector monthly =>
            new ReminderRuleScheduleKey.Monthly(monthly.Day, monthly.LastDay),
        YearlyCalendarSelector yearly =>
            new ReminderRuleScheduleKey.Yearly(yearly.Month, yearly.Day, yearly.LastDay),
        _ => throw new ArgumentOutOfRangeException(nameof(selector))
    };

    private static int CreateWeekdayMask(IReadOnlyList<DayOfWeek> weekdays)
    {
        var mask = 0;
        foreach (var weekday in weekdays)
        {
            if (!Enum.IsDefined(weekday))
            {
                throw new ArgumentOutOfRangeException(nameof(weekdays));
            }

            mask |= 1 << (int)weekday;
        }

        return mask;
    }
}

public sealed record ReminderSnoozeState(
    string OccurrenceKey,
    DateTimeOffset OriginalScheduledUtc,
    DateTimeOffset SnoozeUntilUtc);

public sealed record ReminderRuleState(
    string RuleId,
    DateTimeOffset? HandledThroughUtc,
    string? LastDeliveredOccurrenceKey,
    string? LastDispositionOccurrenceKey = null,
    ReminderSnoozeState? ActiveSnooze = null)
{
    public string? LastAcknowledgedOccurrenceKey => LastDispositionOccurrenceKey;
}

public readonly record struct ReminderOccurrenceKey(string RuleId, DateTimeOffset ScheduledUtc)
{
    public override string ToString() =>
        $"{RuleId}|{ScheduledUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}";

    public static ReminderOccurrenceKey Create(ReminderRule rule, DateTimeOffset scheduledUtc) =>
        new(rule.Id, scheduledUtc.ToUniversalTime());

    public static bool TryParse(string? value, out ReminderOccurrenceKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.LastIndexOf('|');
        if (separator <= 0 || separator == value.Length - 1 ||
            !DateTimeOffset.TryParseExact(
                value[(separator + 1)..],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var scheduledUtc) ||
            scheduledUtc.Offset != TimeSpan.Zero)
        {
            return false;
        }

        key = new ReminderOccurrenceKey(value[..separator], scheduledUtc.ToUniversalTime());
        return true;
    }
}

public static class ReminderRuleIdentity
{
    public static string CreateLegacy(string sourceType, string recordId, string legacyBranch)
    {
        var identity = $"{sourceType.Trim().ToLowerInvariant()}\n{recordId.Trim().ToUpperInvariant()}\n{legacyBranch.Trim().ToLowerInvariant()}";
        return $"legacy-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()}";
    }
}

public static class ReminderRuleCompatibility
{
    // Unique remaining use: settings schema migration and shared-editor/save compatibility
    // conversion. Inline first-reminder controls no longer call this path.
    public static IReadOnlyList<ReminderRule> FromLegacy(string noteId, NoteReminder reminder)
    {
        return reminder switch
        {
            NoReminder => Array.Empty<ReminderRule>(),
            OnceReminder once =>
            [
                new AbsoluteOnceReminderRule(
                    ReminderRuleIdentity.CreateLegacy("note", noteId, "once"),
                    once.FirstDueUtc.ToUniversalTime())
            ],
            RepeatingReminder repeating =>
            [
                new FixedIntervalReminderRule(
                    ReminderRuleIdentity.CreateLegacy("note", noteId, "repeat"),
                    repeating.FirstDueUtc.ToUniversalTime(),
                    repeating.Interval)
            ],
            _ => throw new SettingsValidationException("设置中包含无效的便签提醒。")
        };
    }

    public static NoteReminder ToLegacy(CountdownSettings settings, NoteRecord note)
    {
        if (note.ReminderRules.Count == 0)
        {
            return NoReminder.Instance;
        }

        var rule = note.ReminderRules[0];
        return rule switch
        {
            AbsoluteOnceReminderRule once => new OnceReminder(
                once.ScheduledUtc,
                IsDelivered(settings.ReminderStates, once)
                    ? OnceDeliveryState.Delivered
                    : OnceDeliveryState.Pending),
            FixedIntervalReminderRule repeating =>
                new RepeatingReminder(repeating.FirstDueUtc, repeating.Interval),
            _ => throw new SettingsValidationException("当前兼容编辑器不支持该提醒规则。")
        };
    }

    public static NoteReminder ToLegacySchedule(NoteRecord note)
    {
        if (note.ReminderRules.Count == 0)
        {
            return NoReminder.Instance;
        }

        return note.ReminderRules[0] switch
        {
            AbsoluteOnceReminderRule once =>
                new OnceReminder(once.ScheduledUtc, OnceDeliveryState.Pending),
            FixedIntervalReminderRule repeating =>
                new RepeatingReminder(repeating.FirstDueUtc, repeating.Interval),
            _ => throw new SettingsValidationException("当前兼容编辑器不支持该提醒规则。")
        };
    }

    private static bool IsDelivered(
        IReadOnlyDictionary<string, ReminderRuleState> states,
        AbsoluteOnceReminderRule rule)
    {
        if (!states.TryGetValue(rule.Id, out var state) ||
            state.HandledThroughUtc is not { } handledThroughUtc ||
            handledThroughUtc < rule.ScheduledUtc)
        {
            return false;
        }

        return state.LastDeliveredOccurrenceKey is { } key &&
               string.Equals(
                   key,
                   ReminderOccurrenceKey.Create(rule, rule.ScheduledUtc).ToString(),
                   StringComparison.Ordinal);
    }
}

public sealed record NoteRecord(
    string Id,
    string Text,
    string? Date,
    bool IsCompleted,
    IReadOnlyList<string> TagIds,
    IReadOnlyList<ReminderRule> ReminderRules)
{
    public NoteReminder Reminder => ReminderRuleCompatibility.ToLegacySchedule(this);

    public NoteRecord(
        string id,
        string text,
        string? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        NoteReminder reminder)
        : this(id, text, date, isCompleted, tagIds, ReminderRuleCompatibility.FromLegacy(id, reminder))
    {
    }

    public NoteRecord(
        string id,
        string text,
        string? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        int? reminderIntervalMinutes,
        DateTimeOffset? reminderStartUtc)
        : this(
            id,
            text,
            date,
            isCompleted,
            tagIds,
            ReminderRuleCompatibility.FromLegacy(
                id,
                FromVersionSeven(reminderIntervalMinutes, reminderStartUtc)))
    {
    }

    public int? ReminderIntervalMinutes =>
        ReminderRules.FirstOrDefault() is FixedIntervalReminderRule repeating
            ? repeating.Interval.Minutes
            : null;

    public DateTimeOffset? ReminderStartUtc =>
        ReminderRules.FirstOrDefault() is FixedIntervalReminderRule repeating
            ? repeating.FirstDueUtc.Subtract(TimeSpan.FromMinutes(repeating.Interval.Minutes))
            : null;

    private static NoteReminder FromVersionSeven(
        int? intervalMinutes,
        DateTimeOffset? startUtc)
    {
        if (intervalMinutes is null && startUtc is null)
        {
            return NoReminder.Instance;
        }

        if (intervalMinutes is null || startUtc is null)
        {
            throw new SettingsValidationException("便签提醒间隔和开始时间必须同时设置或同时关闭。");
        }

        var interval = new ReminderInterval(intervalMinutes.Value);
        return new RepeatingReminder(
            startUtc.Value.ToUniversalTime().AddMinutes(interval.Minutes),
            interval);
    }
}

public sealed record TagRecord(
    string Id,
    string Name,
    string? Color);

public sealed record CountdownSettings(
    IReadOnlyList<CountdownRecord> Countdowns,
    IReadOnlyList<NoteRecord> Notes,
    IReadOnlyList<TagRecord> Tags,
    CountdownPreferences Preferences,
    IReadOnlyDictionary<string, ReminderRuleState> ReminderStates)
{
    public CountdownSettings(
        IReadOnlyList<CountdownRecord> countdowns,
        IReadOnlyList<NoteRecord> notes,
        IReadOnlyList<TagRecord> tags,
        CountdownPreferences preferences)
        : this(
            countdowns,
            notes,
            tags,
            preferences,
            new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public static CountdownSettings Empty { get; } = new(
        Array.Empty<CountdownRecord>(),
        Array.Empty<NoteRecord>(),
        Array.Empty<TagRecord>(),
        new CountdownPreferences("Second", 0m, TimeSpan.FromHours(9), TimeSpan.FromHours(18)),
        new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase));
}

public enum SettingsRecoveryOutcome
{
    None,
    RestoredWithOriginalPreserved,
    RestoredWithoutOriginal
}

public sealed record SettingsLoadResult(
    CountdownSettings Settings,
    int SourceSchemaVersion,
    bool Migrated)
{
    public SettingsRecoveryOutcome Recovery { get; init; }
}

public interface IGuidSource
{
    Guid NewGuid();
}

public sealed class SystemGuidSource : IGuidSource
{
    public static SystemGuidSource Instance { get; } = new();

    private SystemGuidSource()
    {
    }

    public Guid NewGuid() => Guid.NewGuid();
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(string message)
        : base(message)
    {
    }

    public SettingsValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class ChinaStandardTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(SettingsSchema.ChinaStandardTimeZoneId);

    public static DateTimeOffset GetCurrentBeijingTime(IClock? clock = null)
    {
        return TimeZoneInfo.ConvertTime((clock ?? SystemClock.Instance).UtcNow, Zone);
    }

    public static DateTimeOffset ToBeijingTime(DateTimeOffset value)
    {
        return TimeZoneInfo.ConvertTime(value, Zone);
    }
}
