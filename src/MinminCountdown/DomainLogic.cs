using System.Globalization;
using MinminCountdown.Core;

namespace MinminCountdown;

internal enum WorkspaceKind
{
    Countdowns,
    Notes
}

internal enum TagMatchMode
{
    Any,
    All
}

internal enum CountdownStatusFilter
{
    All,
    Active,
    Expired
}

internal enum NoteStatusFilter
{
    All,
    Active,
    Completed
}

internal sealed class WorkspaceFilter
{
    public HashSet<string> SelectedTagIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TagMatchMode MatchMode { get; set; } = TagMatchMode.Any;

    public bool UntaggedOnly { get; set; }

    public CountdownStatusFilter CountdownStatus { get; set; } = CountdownStatusFilter.All;

    public NoteStatusFilter NoteStatus { get; set; } = NoteStatusFilter.All;

    public string NoteSearch { get; set; } = string.Empty;

    public string? NoteDate { get; set; }

    public bool UndatedNotesOnly { get; set; }

    public bool HasConstraints =>
        SelectedTagIds.Count > 0 ||
        UntaggedOnly ||
        CountdownStatus != CountdownStatusFilter.All ||
        NoteStatus != NoteStatusFilter.All ||
        !string.IsNullOrWhiteSpace(NoteSearch) ||
        NoteDate is not null ||
        UndatedNotesOnly;

    public bool Matches(CountdownRecord record, DateTimeOffset nowUtc)
    {
        var active = record.TargetUtc > nowUtc;
        if (CountdownStatus == CountdownStatusFilter.Active && !active ||
            CountdownStatus == CountdownStatusFilter.Expired && active)
        {
            return false;
        }

        return MatchesTags(record.TagIds);
    }

    public bool Matches(NoteRecord record)
    {
        if (NoteStatus == NoteStatusFilter.Active && record.IsCompleted ||
            NoteStatus == NoteStatusFilter.Completed && !record.IsCompleted)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(NoteSearch) &&
            !record.Text.Contains(NoteSearch.Trim(), StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (UndatedNotesOnly && record.Date is not null)
        {
            return false;
        }

        if (NoteDate is not null && !string.Equals(NoteDate, record.Date, StringComparison.Ordinal))
        {
            return false;
        }

        return MatchesTags(record.TagIds);
    }

    private bool MatchesTags(IReadOnlyList<string> recordTagIds)
    {
        if (UntaggedOnly)
        {
            return recordTagIds.Count == 0;
        }

        if (SelectedTagIds.Count == 0)
        {
            return true;
        }

        return MatchMode == TagMatchMode.All
            ? SelectedTagIds.All(selected => recordTagIds.Contains(selected, StringComparer.OrdinalIgnoreCase))
            : SelectedTagIds.Any(selected => recordTagIds.Contains(selected, StringComparer.OrdinalIgnoreCase));
    }
}

internal static class CountdownFormatting
{
    private static readonly TimeZoneInfo BeijingZone =
        TimeZoneInfo.FindSystemTimeZoneById(SettingsSchema.ChinaStandardTimeZoneId);

    public static DateTimeOffset ToUtc(DateTime date, int hour, int minute, int second)
    {
        var local = DateTime.SpecifyKind(
            new DateTime(date.Year, date.Month, date.Day, hour, minute, second),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, BeijingZone), TimeSpan.Zero);
    }

    public static string FormatRemaining(DateTimeOffset targetUtc, DateTimeOffset nowUtc, string precision)
    {
        var remaining = targetUtc - nowUtc;
        if (remaining.TotalSeconds <= 0)
        {
            return "已到期";
        }

        var days = (int)Math.Floor(remaining.TotalDays);
        return precision switch
        {
            "Day" => $"{days} 天",
            "Hour" => $"{days} 天 {remaining.Hours:D2} 小时",
            "Minute" => $"{days} 天 {remaining.Hours:D2} 小时 {remaining.Minutes:D2} 分钟",
            _ => $"{days} 天 {remaining.Hours:D2} 小时 {remaining.Minutes:D2} 分钟 {remaining.Seconds:D2} 秒"
        };
    }

    public static string FormatWorkTime(TimeSpan value) =>
        string.Create(CultureInfo.InvariantCulture, $"{value.Hours:D2}:{value.Minutes:D2}");
}

internal sealed class ReminderScheduler
{
    private readonly Dictionary<string, long> _lastCycles = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(CountdownSettings settings, DateTimeOffset nowUtc)
    {
        _lastCycles.Clear();
        foreach (var note in settings.Notes)
        {
            if (!note.IsCompleted && TryGetCycle(note, nowUtc, out var cycle))
            {
                _lastCycles[note.Id] = cycle;
            }
        }
    }

    public IReadOnlyList<NoteRecord> GetDue(CountdownSettings settings, DateTimeOffset nowUtc)
    {
        var due = new List<NoteRecord>();
        var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in settings.Notes)
        {
            liveIds.Add(note.Id);
            if (note.IsCompleted || !TryGetCycle(note, nowUtc, out var cycle))
            {
                continue;
            }

            if (!_lastCycles.TryGetValue(note.Id, out var lastCycle))
            {
                _lastCycles[note.Id] = cycle;
                continue;
            }

            if (cycle > lastCycle)
            {
                due.Add(note);
            }
        }

        foreach (var staleId in _lastCycles.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            _lastCycles.Remove(staleId);
        }

        return due;
    }

    public void ConfirmDisplayed(NoteRecord note, DateTimeOffset displayedAtUtc)
    {
        if (!note.IsCompleted && TryGetCycle(note, displayedAtUtc, out var cycle))
        {
            _lastCycles[note.Id] = cycle;
        }
    }

    private static bool TryGetCycle(NoteRecord note, DateTimeOffset nowUtc, out long cycle)
    {
        cycle = 0;
        if (note.ReminderIntervalMinutes is null || note.ReminderStartUtc is null)
        {
            return false;
        }

        var elapsed = nowUtc - note.ReminderStartUtc.Value;
        if (elapsed < TimeSpan.Zero)
        {
            return false;
        }

        cycle = (long)Math.Floor(elapsed.TotalMinutes / note.ReminderIntervalMinutes.Value);
        return true;
    }
}
