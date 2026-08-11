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

    internal IClock Clock => _session.Clock;

    public static DesktopWorkspace Load(
        string settingsPath,
        SettingsStore? store = null,
        Func<SettingsRecoveryPrompt, SettingsRecoveryDecision>? chooseRecovery = null,
        bool recoveryAllowed = true,
        DateTimeOffset? migrationUtc = null) =>
        new(ApplicationSession.Load(settingsPath, store, chooseRecovery, recoveryAllowed, migrationUtc));

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

    public IReadOnlyList<DueReminderOccurrence> GetDueReminderOccurrences(DateTimeOffset nowUtc) =>
        Array.AsReadOnly(_session.Reminders.GetDueOccurrences(_session.Current, nowUtc)
            .Select(occurrence => new DueReminderOccurrence(
                FreezeReminderSource(occurrence.Source),
                FreezeRule(occurrence.Rule),
                occurrence.ScheduledUtc))
            .ToArray());

    public DesktopWorkspaceChangeResult ConfirmReminderDisplayed(
        NoteRecord note,
        DateTimeOffset displayedUtc)
    {
        var rule = note.ReminderRules.FirstOrDefault();
        if (rule is null)
        {
            return Result(false, "提醒规则已变化。");
        }

        return ConfirmReminderDisplayed(
            note,
            rule.Id,
            ReminderSchedule.GetScheduledUtc(rule, displayedUtc));
    }

    public DesktopWorkspaceChangeResult ConfirmReminderDisplayed(
        NoteRecord note,
        string ruleId,
        DateTimeOffset scheduledUtc)
    {
        var succeeded = _session.TryMarkReminderDelivered(
            note.Id,
            ruleId,
            scheduledUtc,
            out var error);
        if (succeeded)
        {
            _session.Reminders.ConfirmDisplayed(note, ruleId, scheduledUtc);
        }

        return Result(succeeded, error);
    }

    public DesktopWorkspaceChangeResult ConfirmReminderDisplayed(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc)
    {
        var succeeded = _session.TryMarkReminderDelivered(
            sourceKind,
            sourceId,
            ruleId,
            scheduledUtc,
            out var error);
        if (succeeded)
        {
            var occurrence = _session.Reminders.GetDueOccurrences(_session.Current, scheduledUtc)
                .FirstOrDefault(candidate =>
                    candidate.Source.Kind == sourceKind &&
                    string.Equals(candidate.Source.Id, sourceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));
            if (occurrence is not null)
            {
                _session.Reminders.ConfirmDisplayed(occurrence.Source, ruleId, scheduledUtc);
            }
        }

        return Result(succeeded, error);
    }

    public DesktopWorkspaceChangeResult AcknowledgeReminder(
        NoteRecord note,
        string ruleId,
        DateTimeOffset scheduledUtc) =>
        AcknowledgeReminder(
            ReminderSourceKind.Note,
            note.Id,
            ruleId,
            scheduledUtc);

    public DesktopWorkspaceChangeResult AcknowledgeReminder(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc) =>
        Result(
            _session.TryAcknowledgeReminder(
                sourceKind,
                sourceId,
                ruleId,
                scheduledUtc,
                out var error),
            error);

    public DesktopWorkspaceChangeResult SnoozeReminder(
        NoteRecord note,
        string ruleId,
        DateTimeOffset scheduledUtc,
        ReminderSnoozeOption option,
        DateTimeOffset? customBeijing) =>
        SnoozeReminder(
            ReminderSourceKind.Note,
            note.Id,
            ruleId,
            scheduledUtc,
            option,
            customBeijing);

    public DesktopWorkspaceChangeResult SnoozeReminder(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        ReminderSnoozeOption option,
        DateTimeOffset? customBeijing) =>
        Result(
            _session.TrySnoozeReminder(
                sourceKind,
                sourceId,
                ruleId,
                scheduledUtc,
                option,
                customBeijing,
                out var error),
            error);

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

    private DesktopWorkspaceChangeResult ApplySaveCountdown(SaveCountdownChange change)
    {
        if (change.ReminderRules is null)
        {
            return Result(
                _session.TrySaveCountdown(
                    change.ExistingId,
                    change.Name,
                    change.TargetUtc,
                    change.TagIds,
                    out var compatibleError),
                compatibleError);
        }

        var succeeded = _session.TrySaveCountdown(
            change.ExistingId,
            change.Name,
            change.TargetUtc,
            change.TagIds,
            change.ReminderRules,
            out var error,
            out var errorTarget);
        return new DesktopWorkspaceChangeResult(
            succeeded,
            error,
            ReminderRuleErrorTarget: errorTarget);
    }

    private DesktopWorkspaceChangeResult ApplySaveNote(SaveNoteChange change)
    {
        if (change.ReminderRules is { } reminderRules)
        {
            var succeeded = _session.TrySaveNote(
                change.ExistingId,
                change.Text,
                change.Date,
                change.IsCompleted,
                change.TagIds,
                reminderRules,
                out var error,
                out var errorTarget);
            return new DesktopWorkspaceChangeResult(
                succeeded,
                error,
                ReminderRuleErrorTarget: errorTarget);
        }

        return Result(
            _session.TrySaveNote(
                change.ExistingId,
                change.Text,
                change.Date,
                change.IsCompleted,
                change.TagIds,
                change.Reminder ?? NoReminder.Instance,
                out var compatibleError),
            compatibleError);
    }

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
            settings.Preferences with { },
            settings.ReminderStates.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value with { },
                StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<CountdownRecord> FreezeCountdowns(
        IEnumerable<CountdownRecord> records) =>
        Array.AsReadOnly(records.Select(record => new CountdownRecord(
            record.Id,
            record.Name,
            record.TargetUtc,
            FreezeStrings(record.TagIds),
            FreezeRules(record.ReminderRules))).ToArray());

    private static IReadOnlyList<NoteRecord> FreezeNotes(IEnumerable<NoteRecord> notes) =>
        Array.AsReadOnly(notes.Select(FreezeNote).ToArray());

    private static NoteRecord FreezeNote(NoteRecord note) =>
        new(
            note.Id,
            note.Text,
            note.Date,
            note.IsCompleted,
            FreezeStrings(note.TagIds),
            FreezeRules(note.ReminderRules));

    private static IReadOnlyList<TagRecord> FreezeTags(IEnumerable<TagRecord> tags) =>
        Array.AsReadOnly(tags.Select(tag => tag with { }).ToArray());

    private static IReadOnlyList<string> FreezeStrings(IEnumerable<string> values) =>
        Array.AsReadOnly(values.ToArray());

    private static IReadOnlyList<ReminderRule> FreezeRules(IEnumerable<ReminderRule> rules) =>
        Array.AsReadOnly(rules.Select(FreezeRule).ToArray());

    private static ReminderRule FreezeRule(ReminderRule rule) => rule switch
        {
            AbsoluteOnceReminderRule once => once with { },
            FixedIntervalReminderRule repeating => repeating with { },
            CountdownRelativeReminderRule relative => relative with { },
            CalendarReminderRule calendar => calendar with
            {
                Selector = calendar.Selector switch
                {
                    DailyCalendarSelector => new DailyCalendarSelector(),
                    WeeklyCalendarSelector weekly =>
                        new WeeklyCalendarSelector(weekly.Weekdays.ToArray()),
                    MonthlyCalendarSelector monthly => monthly with { },
                    YearlyCalendarSelector yearly => yearly with { },
                    _ => throw new ArgumentOutOfRangeException(nameof(rule))
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };

    private static ReminderSource FreezeReminderSource(ReminderSource source) => source switch
    {
        CountdownReminderSource countdown => new CountdownReminderSource(
            new CountdownRecord(
                countdown.Countdown.Id,
                countdown.Countdown.Name,
                countdown.Countdown.TargetUtc,
                FreezeStrings(countdown.Countdown.TagIds),
                FreezeRules(countdown.Countdown.ReminderRules))),
        NoteReminderSource note => new NoteReminderSource(FreezeNote(note.Note)),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

}

internal abstract record DesktopWorkspaceChange;

internal sealed record SaveCountdownChange(
    string? ExistingId,
    string Name,
    DateTimeOffset TargetUtc,
    IReadOnlyList<string> TagIds,
    IReadOnlyList<ReminderRule>? ReminderRules = null) : DesktopWorkspaceChange;

internal sealed record DeleteCountdownChange(string Id) : DesktopWorkspaceChange;

internal sealed record SaveNoteChange : DesktopWorkspaceChange
{
    public SaveNoteChange(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        NoteReminder reminder)
    {
        ExistingId = existingId;
        Text = text;
        Date = date;
        IsCompleted = isCompleted;
        TagIds = tagIds;
        Reminder = reminder;
    }

    public SaveNoteChange(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        IReadOnlyList<ReminderRule> reminderRules)
    {
        ExistingId = existingId;
        Text = text;
        Date = date;
        IsCompleted = isCompleted;
        TagIds = tagIds;
        ReminderRules = reminderRules;
    }

    public string? ExistingId { get; }

    public string Text { get; }

    public DateTime? Date { get; }

    public bool IsCompleted { get; }

    public IReadOnlyList<string> TagIds { get; }

    public NoteReminder? Reminder { get; }

    public IReadOnlyList<ReminderRule>? ReminderRules { get; }
}

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
    TagRecord? CreatedTag = null,
    ReminderRuleEditingErrorTarget ReminderRuleErrorTarget = ReminderRuleEditingErrorTarget.None);

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
