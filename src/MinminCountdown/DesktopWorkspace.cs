using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class DesktopWorkspace
{
    private readonly ApplicationSession _session;

    public DesktopWorkspace(ApplicationSession session)
    {
        _session = session;
    }

    public static bool IsDeepModule => true;

    public CountdownSettings Current => _session.Current;

    public WorkspaceFilter Filter => _session.Filter;

    public DateTimeOffset BeijingNow => _session.BeijingNow;

    public DateTimeOffset UtcNow => _session.UtcNow;

    public bool HasFilterConstraints => _session.Filter.HasConstraints;

    public IReadOnlyCollection<string> SelectedTagIds => _session.Filter.SelectedTagIds;

    public IReadOnlyList<CountdownRecord> GetVisibleCountdowns() => _session.GetSortedCountdowns();

    public IReadOnlyList<CountdownRecord> GetAllCountdowns() => _session.GetSortedCountdowns(applyFilter: false);

    public IReadOnlyList<NoteRecord> GetVisibleNotes() => _session.GetSortedNotes();

    public IReadOnlyList<NoteRecord> GetAllNotes() => _session.GetSortedNotes(applyFilter: false);

    public IReadOnlyList<NoteRecord> GetVisibleActiveNotes() =>
        _session.GetSortedNotes().Where(note => !note.IsCompleted).ToArray();

    public bool TrySaveCountdown(
        string? existingId,
        string name,
        DateTimeOffset targetUtc,
        IReadOnlyList<string> tagIds,
        out string error) =>
        _session.TrySaveCountdown(existingId, name, targetUtc, tagIds, out error);

    public bool TryDeleteCountdown(string id, out string error) =>
        _session.TryDeleteCountdown(id, out error);

    public bool TrySaveNote(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        int? reminderIntervalMinutes,
        out string error) =>
        _session.TrySaveNote(existingId, text, date, isCompleted, tagIds, reminderIntervalMinutes, out error);

    public bool TrySaveQuickNote(
        string text,
        DateTime? date,
        IReadOnlyList<string> tagIds,
        int? reminderIntervalMinutes,
        out string error) =>
        _session.TrySaveNote(null, text, date, false, tagIds, reminderIntervalMinutes, out error);

    public bool TrySetNoteCompletion(string id, bool completed, out string error) =>
        _session.TrySetNoteCompletion(id, completed, out error);

    public bool TryDeleteNote(string id, out string error) =>
        _session.TryDeleteNote(id, out error);

    public bool TryCreateTag(string name, string? color, out TagRecord? tag, out string error) =>
        _session.TryCreateTag(name, color, out tag, out error);

    public bool TryRenameTag(string id, string name, string? color, out string error) =>
        _session.TryRenameTag(id, name, color, out error);

    public bool TryDeleteTag(string id, out string error) =>
        _session.TryDeleteTag(id, out error);

    public bool TrySavePreferences(
        string displayPrecision,
        decimal monthlySalary,
        TimeSpan workStart,
        TimeSpan workEnd,
        out string error) =>
        _session.TrySavePreferences(displayPrecision, monthlySalary, workStart, workEnd, out error);

    public void SelectTag(string tagId, bool selected)
    {
        if (selected)
        {
            _session.Filter.SelectedTagIds.Add(tagId);
        }
        else
        {
            _session.Filter.SelectedTagIds.Remove(tagId);
        }
    }

    public void SetTagMatchMode(TagMatchMode mode) => _session.Filter.MatchMode = mode;

    public void SetUntaggedOnly(bool value) => _session.Filter.UntaggedOnly = value;

    public void SetCountdownStatus(CountdownStatusFilter filter) => _session.Filter.CountdownStatus = filter;

    public void SetNoteStatus(NoteStatusFilter filter) => _session.Filter.NoteStatus = filter;

    public void SetNoteSearch(string value) => _session.Filter.NoteSearch = value;

    public void SetNoteDateFilter(string? date)
    {
        _session.Filter.NoteDate = date;
        if (date is not null)
        {
            _session.Filter.UndatedNotesOnly = false;
        }
    }

    public void SetUndatedNotesOnly(bool value)
    {
        _session.Filter.UndatedNotesOnly = value;
        if (value)
        {
            _session.Filter.NoteDate = null;
        }
    }

    public void ClearFilters()
    {
        _session.Filter.SelectedTagIds.Clear();
        _session.Filter.MatchMode = TagMatchMode.Any;
        _session.Filter.UntaggedOnly = false;
        _session.Filter.CountdownStatus = CountdownStatusFilter.All;
        _session.Filter.NoteStatus = NoteStatusFilter.All;
        _session.Filter.NoteSearch = string.Empty;
        _session.Filter.NoteDate = null;
        _session.Filter.UndatedNotesOnly = false;
    }

    public int AppliedFilterCount =>
        _session.Filter.SelectedTagIds.Count +
        (_session.Filter.UntaggedOnly ? 1 : 0) +
        (_session.Filter.CountdownStatus != CountdownStatusFilter.All ? 1 : 0) +
        (_session.Filter.NoteStatus != NoteStatusFilter.All ? 1 : 0) +
        (!string.IsNullOrWhiteSpace(_session.Filter.NoteSearch) ? 1 : 0) +
        (_session.Filter.NoteDate is not null || _session.Filter.UndatedNotesOnly ? 1 : 0);
}
