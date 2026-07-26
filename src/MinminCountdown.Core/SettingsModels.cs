namespace MinminCountdown.Core;

public static class SettingsSchema
{
    public const int CurrentVersion = 8;
    public const string ChinaStandardTimeZoneId = "China Standard Time";
}

public sealed record CountdownPreferences(
    string DisplayPrecision,
    decimal MonthlySalary,
    TimeSpan WorkStart,
    TimeSpan WorkEnd);

public sealed record CountdownRecord(
    string Id,
    string Name,
    DateTimeOffset TargetUtc,
    IReadOnlyList<string> TagIds);

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

public sealed record NoteRecord(
    string Id,
    string Text,
    string? Date,
    bool IsCompleted,
    IReadOnlyList<string> TagIds,
    NoteReminder Reminder)
{
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
            FromVersionSeven(reminderIntervalMinutes, reminderStartUtc))
    {
    }

    public int? ReminderIntervalMinutes =>
        Reminder is RepeatingReminder repeating ? repeating.Interval.Minutes : null;

    public DateTimeOffset? ReminderStartUtc =>
        Reminder is RepeatingReminder repeating
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
    CountdownPreferences Preferences)
{
    public static CountdownSettings Empty { get; } = new(
        Array.Empty<CountdownRecord>(),
        Array.Empty<NoteRecord>(),
        Array.Empty<TagRecord>(),
        new CountdownPreferences("Second", 0m, TimeSpan.FromHours(9), TimeSpan.FromHours(18)));
}

public sealed record SettingsLoadResult(
    CountdownSettings Settings,
    int SourceSchemaVersion,
    bool Migrated);

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
