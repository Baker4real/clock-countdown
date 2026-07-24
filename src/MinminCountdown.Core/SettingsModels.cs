namespace MinminCountdown.Core;

public static class SettingsSchema
{
    public const int CurrentVersion = 7;
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

public sealed record NoteRecord(
    string Id,
    string Text,
    string? Date,
    bool IsCompleted,
    IReadOnlyList<string> TagIds,
    int? ReminderIntervalMinutes,
    DateTimeOffset? ReminderStartUtc);

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
