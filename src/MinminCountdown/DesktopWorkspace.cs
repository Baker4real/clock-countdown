using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class DesktopWorkspace
{
    private readonly ApplicationSession _session;

    public DesktopWorkspace(ApplicationSession session)
    {
        _session = session;
        _session.StateChanged += OnSessionStateChanged;
    }

    public event EventHandler? StateChanged;

    public static DesktopWorkspace Load(string settingsPath, SettingsStore? store = null) =>
        new(ApplicationSession.Load(settingsPath, store));

    public DesktopWorkspaceSnapshot Snapshot
    {
        get
        {
            var nowUtc = _session.UtcNow;
            var filter = new DesktopWorkspaceFilter(
                FreezeStrings(_session.Filter.SelectedTagIds),
                _session.Filter.MatchMode,
                _session.Filter.UntaggedOnly,
                _session.Filter.CountdownStatus,
                _session.Filter.NoteStatus,
                _session.Filter.NoteSearch,
                _session.Filter.NoteDate,
                _session.Filter.UndatedNotesOnly);
            return new DesktopWorkspaceSnapshot(
                FreezeSettings(_session.Current),
                _session.SettingsPath,
                _session.SettingsWereMigrated,
                nowUtc,
                ChinaStandardTime.ToBeijingTime(nowUtc),
                filter,
                FreezeCountdowns(_session.GetSortedCountdowns(nowUtc)),
                FreezeCountdowns(_session.GetSortedCountdowns(nowUtc, applyFilter: false)),
                FreezeNotes(_session.GetSortedManagerNotes()),
                FreezeNotes(_session.GetSortedManagerNotes(applyFilter: false)),
                FreezeNotes(_session.GetSortedNotes().Where(note => !note.IsCompleted)));
        }
    }

    public DesktopWorkspaceChangeResult Apply(DesktopWorkspaceChange change) => change switch
    {
        SaveCountdownChange saveCountdown => ApplySaveCountdown(saveCountdown),
        DeleteCountdownChange deleteCountdown => Result(
            _session.TryDeleteCountdown(deleteCountdown.Id, out var error),
            error),
        SaveNoteChange saveNote => ApplySaveNote(saveNote),
        SetNoteCompletionChange completion => Result(
            _session.TrySetNoteCompletion(completion.Id, completion.Completed, out var error),
            error),
        DeleteNoteChange deleteNote => Result(
            _session.TryDeleteNote(deleteNote.Id, out var error),
            error),
        CreateTagChange createTag => ApplyCreateTag(createTag),
        RenameTagChange renameTag => Result(
            _session.TryRenameTag(renameTag.Id, renameTag.Name, renameTag.Color, out var error),
            error),
        DeleteTagChange deleteTag => Result(
            _session.TryDeleteTag(deleteTag.Id, out var error),
            error),
        SavePreferencesChange savePreferences => ApplySavePreferences(savePreferences),
        SaveThemePreferenceChange saveTheme => Result(
            _session.TrySaveThemePreference(saveTheme.Preference, out var error),
            error),
        _ => throw new ArgumentOutOfRangeException(nameof(change))
    };

    public IReadOnlyList<NoteRecord> GetDueReminders(DateTimeOffset nowUtc) =>
        FreezeNotes(_session.Reminders.GetDue(_session.Current, nowUtc));

    public DesktopWorkspaceChangeResult ConfirmReminderDisplayed(
        NoteRecord note,
        DateTimeOffset displayedUtc)
    {
        _session.Reminders.ConfirmDisplayed(note, displayedUtc);
        if (note.Reminder is not OnceReminder once)
        {
            return Result(true, string.Empty);
        }

        return Result(
            _session.TryMarkOnceReminderDelivered(note.Id, once.FirstDueUtc, out var error),
            error);
    }

    public void PersistMigrationIfNeeded() => _session.PersistMigrationIfNeeded();

    public void ApplyFilter(DesktopWorkspaceFilter filter)
    {
        _session.Filter.SelectedTagIds.Clear();
        foreach (var tagId in filter.SelectedTagIds)
        {
            _session.Filter.SelectedTagIds.Add(tagId);
        }

        _session.Filter.MatchMode = filter.MatchMode;
        _session.Filter.UntaggedOnly = filter.UntaggedOnly;
        _session.Filter.CountdownStatus = filter.CountdownStatus;
        _session.Filter.NoteStatus = filter.NoteStatus;
        _session.Filter.NoteSearch = filter.NoteSearch;
        _session.Filter.NoteDate = filter.UndatedNotesOnly ? null : filter.NoteDate;
        _session.Filter.UndatedNotesOnly = filter.UndatedNotesOnly;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private DesktopWorkspaceChangeResult ApplySaveCountdown(SaveCountdownChange change) => Result(
        _session.TrySaveCountdown(
            change.ExistingId,
            change.Name,
            change.TargetUtc,
            change.TagIds,
            out var error),
        error);

    private DesktopWorkspaceChangeResult ApplySaveNote(SaveNoteChange change) => Result(
        _session.TrySaveNote(
            change.ExistingId,
            change.Text,
            change.Date,
            change.IsCompleted,
            change.TagIds,
            change.Reminder,
            out var error),
        error);

    private DesktopWorkspaceChangeResult ApplyCreateTag(CreateTagChange change)
    {
        var succeeded = _session.TryCreateTag(change.Name, change.Color, out var tag, out var error);
        return new DesktopWorkspaceChangeResult(succeeded, error, tag);
    }

    private DesktopWorkspaceChangeResult ApplySavePreferences(SavePreferencesChange change) => Result(
        _session.TrySavePreferences(
            change.DisplayPrecision,
            change.MonthlySalary,
            change.WorkStart,
            change.WorkEnd,
            out var error),
        error);

    private static DesktopWorkspaceChangeResult Result(bool succeeded, string error) =>
        new(succeeded, error);

    private void OnSessionStateChanged(object? sender, EventArgs eventArgs) =>
        StateChanged?.Invoke(this, EventArgs.Empty);

    private static CountdownSettings FreezeSettings(CountdownSettings settings) =>
        new(
            FreezeCountdowns(settings.Countdowns),
            FreezeNotes(settings.Notes),
            FreezeTags(settings.Tags),
            settings.Preferences with { });

    private static IReadOnlyList<CountdownRecord> FreezeCountdowns(
        IEnumerable<CountdownRecord> records) =>
        Array.AsReadOnly(records.Select(record => new CountdownRecord(
            record.Id,
            record.Name,
            record.TargetUtc,
            FreezeStrings(record.TagIds))).ToArray());

    private static IReadOnlyList<NoteRecord> FreezeNotes(IEnumerable<NoteRecord> notes) =>
        Array.AsReadOnly(notes.Select(note => new NoteRecord(
            note.Id,
            note.Text,
            note.Date,
            note.IsCompleted,
            FreezeStrings(note.TagIds),
            FreezeReminder(note.Reminder))).ToArray());

    private static IReadOnlyList<TagRecord> FreezeTags(IEnumerable<TagRecord> tags) =>
        Array.AsReadOnly(tags.Select(tag => tag with { }).ToArray());

    private static IReadOnlyList<string> FreezeStrings(IEnumerable<string> values) =>
        Array.AsReadOnly(values.ToArray());

    private static NoteReminder FreezeReminder(NoteReminder reminder) => reminder switch
    {
        NoReminder => NoReminder.Instance,
        OnceReminder once => once with { },
        RepeatingReminder repeating => repeating with { },
        _ => throw new ArgumentOutOfRangeException(nameof(reminder))
    };

}

internal abstract record DesktopWorkspaceChange;

internal sealed record SaveCountdownChange(
    string? ExistingId,
    string Name,
    DateTimeOffset TargetUtc,
    IReadOnlyList<string> TagIds) : DesktopWorkspaceChange;

internal sealed record DeleteCountdownChange(string Id) : DesktopWorkspaceChange;

internal sealed record SaveNoteChange(
    string? ExistingId,
    string Text,
    DateTime? Date,
    bool IsCompleted,
    IReadOnlyList<string> TagIds,
    NoteReminder Reminder) : DesktopWorkspaceChange;

internal sealed record SetNoteCompletionChange(string Id, bool Completed) : DesktopWorkspaceChange;

internal sealed record DeleteNoteChange(string Id) : DesktopWorkspaceChange;

internal sealed record CreateTagChange(string Name, string? Color) : DesktopWorkspaceChange;

internal sealed record RenameTagChange(string Id, string Name, string? Color) : DesktopWorkspaceChange;

internal sealed record DeleteTagChange(string Id) : DesktopWorkspaceChange;

internal sealed record SavePreferencesChange(
    string DisplayPrecision,
    decimal MonthlySalary,
    TimeSpan WorkStart,
    TimeSpan WorkEnd) : DesktopWorkspaceChange;

internal sealed record SaveThemePreferenceChange(ThemePreferenceMode Preference) : DesktopWorkspaceChange;

internal sealed record DesktopWorkspaceChangeResult(
    bool Succeeded,
    string Error,
    TagRecord? CreatedTag = null);

internal sealed record DesktopWorkspaceFilter(
    IReadOnlyList<string> SelectedTagIds,
    TagMatchMode MatchMode,
    bool UntaggedOnly,
    CountdownStatusFilter CountdownStatus,
    NoteStatusFilter NoteStatus,
    string NoteSearch,
    string? NoteDate,
    bool UndatedNotesOnly)
{
    public static DesktopWorkspaceFilter Empty { get; } = new(
        Array.AsReadOnly(Array.Empty<string>()),
        TagMatchMode.Any,
        false,
        CountdownStatusFilter.All,
        NoteStatusFilter.All,
        string.Empty,
        null,
        false);

    public bool HasConstraints =>
        SelectedTagIds.Count > 0 ||
        UntaggedOnly ||
        CountdownStatus != CountdownStatusFilter.All ||
        NoteStatus != NoteStatusFilter.All ||
        !string.IsNullOrWhiteSpace(NoteSearch) ||
        NoteDate is not null ||
        UndatedNotesOnly;

    public int AppliedCount =>
        SelectedTagIds.Count +
        (UntaggedOnly ? 1 : 0) +
        (CountdownStatus != CountdownStatusFilter.All ? 1 : 0) +
        (NoteStatus != NoteStatusFilter.All ? 1 : 0) +
        (!string.IsNullOrWhiteSpace(NoteSearch) ? 1 : 0) +
        (NoteDate is not null || UndatedNotesOnly ? 1 : 0);
}

internal sealed record DesktopWorkspaceSnapshot(
    CountdownSettings Settings,
    string SettingsPath,
    bool SettingsWereMigrated,
    DateTimeOffset UtcNow,
    DateTimeOffset BeijingNow,
    DesktopWorkspaceFilter Filter,
    IReadOnlyList<CountdownRecord> VisibleCountdowns,
    IReadOnlyList<CountdownRecord> AllCountdowns,
    IReadOnlyList<NoteRecord> VisibleNotes,
    IReadOnlyList<NoteRecord> AllNotes,
    IReadOnlyList<NoteRecord> VisibleActiveNotes)
{
    public bool HasFilterConstraints => Filter.HasConstraints;

    public int AppliedFilterCount => Filter.AppliedCount;
}
