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

internal static class ReminderSchedule
{
    public static bool TryCreateFirstDueUtc(
        DateTime? date,
        int hour,
        int minute,
        DateTimeOffset nowUtc,
        out DateTimeOffset? firstDueUtc,
        out string error)
    {
        firstDueUtc = null;
        if (date is null || hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            error = "请选择完整的首次提醒日期和时间。";
            return false;
        }

        var candidate = CountdownFormatting.ToUtc(date.Value, hour, minute, 0);
        if (candidate <= nowUtc)
        {
            error = "首次提醒时间必须晚于当前北京时间。";
            return false;
        }

        firstDueUtc = candidate;
        error = string.Empty;
        return true;
    }

    public static OnceReminder CreateOnce(DateTimeOffset firstDueUtc) =>
        new(firstDueUtc.ToUniversalTime(), OnceDeliveryState.Pending);

    public static RepeatingReminder CreateRepeating(DateTimeOffset firstDueUtc, int intervalMinutes) =>
        new(firstDueUtc.ToUniversalTime(), new ReminderInterval(intervalMinutes));

    public static DateTimeOffset? GetNextDueUtc(NoteRecord note, DateTimeOffset nowUtc)
    {
        if (note.Reminder is NoReminder ||
            note.Reminder is OnceReminder { Delivery: OnceDeliveryState.Delivered })
        {
            return null;
        }

        if (note.Reminder is OnceReminder once)
        {
            return once.FirstDueUtc;
        }

        var repeating = (RepeatingReminder)note.Reminder;
        if (nowUtc < repeating.FirstDueUtc)
        {
            return repeating.FirstDueUtc;
        }

        var completedCycles = Math.Floor((nowUtc - repeating.FirstDueUtc).TotalMinutes /
            repeating.Interval.Minutes) + 1;
        return repeating.FirstDueUtc.AddMinutes(completedCycles * repeating.Interval.Minutes);
    }

    public static DateTimeOffset GetDefaultFirstDueBeijing(DateTimeOffset beijingNow, int intervalMinutes)
    {
        var candidate = beijingNow.AddMinutes(intervalMinutes);
        var rounded = new DateTimeOffset(
            candidate.Year,
            candidate.Month,
            candidate.Day,
            candidate.Hour,
            candidate.Minute,
            0,
            candidate.Offset);
        return rounded > beijingNow ? rounded : rounded.AddMinutes(1);
    }
}

internal sealed class ReminderScheduler
{
    private readonly Dictionary<string, long> _lastRepeatCycles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _shownOnce = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(CountdownSettings settings, DateTimeOffset nowUtc)
    {
        _lastRepeatCycles.Clear();
        _shownOnce.Clear();
        foreach (var note in settings.Notes)
        {
            if (!note.IsCompleted && note.Reminder is RepeatingReminder)
            {
                _lastRepeatCycles[note.Id] = TryGetRepeatCycle(note, nowUtc, out var cycle) ? cycle : 0;
            }
        }
    }

    public void Synchronize(CountdownSettings previous, CountdownSettings current, DateTimeOffset nowUtc)
    {
        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in current.Notes)
        {
            if (note.IsCompleted ||
                note.Reminder is NoReminder or OnceReminder { Delivery: OnceDeliveryState.Delivered })
            {
                continue;
            }

            activeIds.Add(note.Id);
            var old = previous.Notes.FirstOrDefault(item =>
                string.Equals(item.Id, note.Id, StringComparison.OrdinalIgnoreCase));
            var reminderUnchanged =
                old is not null &&
                !old.IsCompleted &&
                Equals(old.Reminder, note.Reminder);
            if (note.Reminder is RepeatingReminder &&
                (!reminderUnchanged || !_lastRepeatCycles.ContainsKey(note.Id)))
            {
                _lastRepeatCycles[note.Id] =
                    TryGetRepeatCycle(note, nowUtc, out var cycle) ? cycle : 0;
            }

            if (note.Reminder is OnceReminder && !reminderUnchanged)
            {
                _shownOnce.Remove(note.Id);
            }
        }

        foreach (var staleId in _lastRepeatCycles.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _lastRepeatCycles.Remove(staleId);
        }

        foreach (var staleId in _shownOnce.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _shownOnce.Remove(staleId);
        }
    }

    public IReadOnlyList<NoteRecord> GetDue(CountdownSettings settings, DateTimeOffset nowUtc)
    {
        var due = new List<NoteRecord>();
        var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in settings.Notes)
        {
            if (note.IsCompleted || note.Reminder is NoReminder)
            {
                continue;
            }

            liveIds.Add(note.Id);
            if (note.Reminder is OnceReminder once)
            {
                if (once.Delivery == OnceDeliveryState.Pending &&
                    nowUtc >= once.FirstDueUtc &&
                    !_shownOnce.Contains(note.Id))
                {
                    due.Add(note);
                }

                continue;
            }

            if (!TryGetRepeatCycle(note, nowUtc, out var cycle))
            {
                continue;
            }

            if (!_lastRepeatCycles.TryGetValue(note.Id, out var lastCycle))
            {
                _lastRepeatCycles[note.Id] = cycle;
                continue;
            }

            if (cycle > lastCycle)
            {
                due.Add(note);
            }
        }

        foreach (var staleId in _lastRepeatCycles.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            _lastRepeatCycles.Remove(staleId);
        }

        foreach (var staleId in _shownOnce.Where(id => !liveIds.Contains(id)).ToArray())
        {
            _shownOnce.Remove(staleId);
        }

        return due;
    }

    public void ConfirmDisplayed(NoteRecord note, DateTimeOffset displayedAtUtc)
    {
        if (note.IsCompleted)
        {
            return;
        }

        if (note.Reminder is OnceReminder { Delivery: OnceDeliveryState.Pending })
        {
            _shownOnce.Add(note.Id);
            return;
        }

        if (TryGetRepeatCycle(note, displayedAtUtc, out var cycle))
        {
            _lastRepeatCycles[note.Id] = cycle;
        }
    }

    private static bool TryGetRepeatCycle(NoteRecord note, DateTimeOffset nowUtc, out long cycle)
    {
        cycle = 0;
        if (note.Reminder is not RepeatingReminder repeating)
        {
            return false;
        }

        if (nowUtc < repeating.FirstDueUtc)
        {
            return false;
        }

        cycle = (long)Math.Floor((nowUtc - repeating.FirstDueUtc).TotalMinutes /
            repeating.Interval.Minutes) + 1;
        return true;
    }
}
