namespace MinminCountdown.Core;

// 桌面工作区：管理窗口与悬浮窗共用的一个 module。调用方只读不可变快照、提交变更、换筛选值、处置提醒；
// 提交事务、规则提醒状态与重复提醒 cursor 是它的 implementation（DesktopWorkspace.Transactions.cs）。
public sealed partial class DesktopWorkspace
{
    private readonly SettingsStore _store;
    private readonly string _settingsPath;
    private readonly ReminderScheduler _reminders = new();
    private CountdownSettings _current;
    private DesktopWorkspaceFilter _filter = DesktopWorkspaceFilter.Empty;
    private bool _settingsWereMigrated;
    private readonly HashSet<WorkspaceUndoSurface> _undoHovered = new();
    private readonly HashSet<WorkspaceUndoSurface> _undoFocused = new();
    private WorkspaceUndoOffer? _undoOffer;
    private TimeSpan _undoRemaining;
    private DateTimeOffset? _undoRunningSinceUtc;

    private DesktopWorkspace(
        string settingsPath,
        SettingsStore store,
        CountdownSettings settings,
        bool migrated,
        SettingsRecoveryOutcome recovery)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
        _store = store;
        _current = store.Copy(settings);
        _settingsWereMigrated = migrated;
        Recovery = recovery;
        _reminders.Initialize(_current, store.Clock.UtcNow);
    }

    public event EventHandler? StateChanged;

    public event EventHandler<WorkspaceUndoChangedEventArgs>? UndoChanged;

    public static TimeSpan UndoWindow { get; } = TimeSpan.FromSeconds(10);

    public IClock Clock => _store.Clock;

    // #214/#233：唯一最近操作槽。删除与完成共用，只由发起的表面呈现。
    public WorkspaceUndoOffer? UndoOffer => _undoOffer;

    public SettingsRecoveryOutcome Recovery { get; }

    public static DesktopWorkspace Load(
        string settingsPath,
        SettingsStore? store = null,
        Func<SettingsRecoveryPrompt, SettingsRecoveryDecision>? chooseRecovery = null,
        bool recoveryAllowed = true,
        DateTimeOffset? migrationUtc = null)
    {
        var actualStore = store ?? new SettingsStore();
        var result = actualStore.LoadForStartup(
            settingsPath,
            chooseRecovery ?? (_ => SettingsRecoveryDecision.Exit),
            recoveryAllowed,
            migrationUtc);
        return new DesktopWorkspace(
            settingsPath,
            actualStore,
            result.Settings,
            result.Migrated,
            result.Recovery);
    }

    public DesktopWorkspaceSnapshot Snapshot
    {
        get
        {
            var nowUtc = _store.Clock.UtcNow;
            return new DesktopWorkspaceSnapshot(
                FreezeSettings(_current),
                _settingsPath,
                _settingsWereMigrated,
                nowUtc,
                ChinaStandardTime.ToBeijingTime(nowUtc),
                _filter,
                FreezeCountdowns(GetSortedCountdowns(nowUtc, applyFilter: true)),
                FreezeCountdowns(GetSortedCountdowns(nowUtc, applyFilter: false)),
                FreezeNotes(GetSortedNotes(applyFilter: true)),
                FreezeNotes(GetSortedNotes(applyFilter: false)),
                FreezeNotes(GetSortedNotes(applyFilter: true).Where(note => !note.IsCompleted)));
        }
    }

    public DesktopWorkspaceChangeResult Apply(DesktopWorkspaceChange change) => change switch
    {
        SaveCountdownChange saveCountdown => ApplySaveCountdown(saveCountdown),
        DeleteCountdownChange deleteCountdown => Result(
            TryDeleteCountdown(deleteCountdown.Id, out var error),
            error),
        RestoreDeletedCountdownChange restoreCountdown => Result(
            TryRestoreCountdown(
                restoreCountdown.Record,
                restoreCountdown.ReminderStates,
                out var restoreCountdownError),
            restoreCountdownError),
        RestoreDeletedNoteChange restoreNote => Result(
            TryRestoreNote(
                restoreNote.Record,
                restoreNote.ReminderStates,
                out var restoreNoteError),
            restoreNoteError),
        SaveNoteChange saveNote => ApplySaveNote(saveNote),
        SetNoteCompletionChange completion => Result(
            TrySetNoteCompletion(completion.Id, completion.Completed, out var error),
            error),
        SetNotesCompletionChange completion => Result(
            TrySetNotesCompletion(completion.Ids, completion.Completed, out var error), error),
        DeleteNoteChange deleteNote => Result(
            TryDeleteNote(deleteNote.Id, out var error),
            error),
        DeleteNotesChange deleteNotes => Result(TryDeleteNotes(deleteNotes.Ids, out var error), error),
        RestoreDeletedNotesChange restoreNotes => Result(
            TryRestoreNotes(restoreNotes.Records, restoreNotes.ReminderStates, out var error), error),
        CreateTagChange createTag => ApplyCreateTag(createTag),
        RenameTagChange renameTag => Result(
            TryRenameTag(renameTag.Id, renameTag.Name, renameTag.Color, out var error),
            error),
        DeleteTagChange deleteTag => Result(
            TryDeleteTag(deleteTag.Id, out var error),
            error),
        SavePreferencesChange savePreferences => ApplySavePreferences(savePreferences),
        _ => throw new ArgumentOutOfRangeException(nameof(change))
    };

    public IReadOnlyList<NoteRecord> GetDueReminders(DateTimeOffset nowUtc) =>
        FreezeNotes(_reminders.GetDue(_current, nowUtc));

    public IReadOnlyList<DueReminderOccurrence> GetDueReminderOccurrences(DateTimeOffset nowUtc) =>
        Array.AsReadOnly(_reminders.GetDueOccurrences(_current, nowUtc)
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
        var succeeded = TryMarkReminderDelivered(
            ReminderSourceKind.Note,
            note.Id,
            ruleId,
            scheduledUtc,
            out var error);
        if (succeeded)
        {
            _reminders.ConfirmDisplayed(note, ruleId, scheduledUtc);
        }

        return Result(succeeded, error);
    }

    public DesktopWorkspaceChangeResult ConfirmReminderDisplayed(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc)
    {
        var succeeded = TryMarkReminderDelivered(
            sourceKind,
            sourceId,
            ruleId,
            scheduledUtc,
            out var error);
        if (succeeded)
        {
            var occurrence = _reminders.GetDueOccurrences(_current, scheduledUtc)
                .FirstOrDefault(candidate =>
                    candidate.Source.Kind == sourceKind &&
                    string.Equals(candidate.Source.Id, sourceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));
            if (occurrence is not null)
            {
                _reminders.ConfirmDisplayed(occurrence.Source, ruleId, scheduledUtc);
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
            TryAcknowledgeReminder(
                sourceKind,
                sourceId,
                ruleId,
                scheduledUtc,
                out var error),
            error);

    public DesktopWorkspaceChangeResult AcknowledgeReminders(
        IReadOnlyList<ReminderAcknowledgementRequest> requests) =>
        Result(
            TryAcknowledgeReminders(requests, out var error),
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
            TrySnoozeReminder(
                sourceKind,
                sourceId,
                ruleId,
                scheduledUtc,
                option,
                customBeijing,
                out var error,
                out var errorCode),
            error,
            errorCode);

    public void PersistMigrationIfNeeded()
    {
        if (!_settingsWereMigrated)
        {
            return;
        }

        _store.Save(_settingsPath, _current);
        _settingsWereMigrated = false;
    }

    // 筛选只存一个不可变值：标签 ID 按序号忽略大小写去重并保留首次出现的顺序，只看无日期时清掉具体日期。
    public void ApplyFilter(DesktopWorkspaceFilter filter)
    {
        _filter = filter with
        {
            SelectedTagIds = Array.AsReadOnly(filter.SelectedTagIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()),
            NoteDate = filter.UndatedNotesOnly ? null : filter.NoteDate
        };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // 撤销载荷在提交这一刻从当前设置取（确认框关闭之后），提交成功才替换槽并重新计满 10 秒；
    // 提交失败时槽原样保留。只支持删除与完成，其余变更直接拒绝且不提交。
    public DesktopWorkspaceChangeResult ApplyWithUndo(
        DesktopWorkspaceChange change,
        WorkspaceUndoSurface surface)
    {
        var undo = CaptureUndo(change);
        var result = Apply(change);
        if (result.Succeeded && undo is not null)
        {
            OfferUndo(new WorkspaceUndoOffer(surface, undo));
        }

        return result;
    }

    // 只撤销发起表面上的那一份载荷；恢复失败时载荷与计时原样保留，供用户重试。
    public DesktopWorkspaceChangeResult Undo(WorkspaceUndoSurface surface)
    {
        if (_undoOffer is not { } offer || offer.Surface != surface)
        {
            return Result(false, "没有可撤销的操作。");
        }

        var result = Apply(offer.Payload.ToChange());
        if (result.Succeeded)
        {
            EndUndo(WorkspaceUndoEnd.Restored);
        }

        return result;
    }

    public void DismissUndo(WorkspaceUndoSurface surface)
    {
        if (_undoOffer?.Surface == surface)
        {
            EndUndo(WorkspaceUndoEnd.Dismissed);
        }
    }

    // 悬停或焦点停在发起表面的撤销入口上时暂停计时，移开后按剩余时间继续，而不是重新计满。
    public void SetUndoHovered(WorkspaceUndoSurface surface, bool hovered) =>
        SetUndoHold(_undoHovered, surface, hovered);

    public void SetUndoFocused(WorkspaceUndoSurface surface, bool focused) =>
        SetUndoHold(_undoFocused, surface, focused);

    // 计时只读注入的时钟；界面计时器按节拍调用这里，走满 10 秒（含）即收起载荷。
    public void ExpireUndoIfDue()
    {
        if (_undoRunningSinceUtc is { } runningSinceUtc &&
            _store.Clock.UtcNow - runningSinceUtc >= _undoRemaining)
        {
            EndUndo(WorkspaceUndoEnd.Expired);
        }
    }

    private WorkspaceUndo? CaptureUndo(DesktopWorkspaceChange change) => change switch
    {
        DeleteCountdownChange delete => _current.Countdowns.FirstOrDefault(record =>
                string.Equals(record.Id, delete.Id, StringComparison.OrdinalIgnoreCase)) is { } countdown
            ? DeletedRecordUndo.ForCountdown(
                FreezeCountdown(countdown),
                DeletedRecordUndo.CaptureReminderStates(_current.ReminderStates, countdown.ReminderRules))
            : null,
        DeleteNoteChange delete => FindNote(delete.Id) is { } note
            ? DeletedRecordUndo.ForNote(
                FreezeNote(note),
                DeletedRecordUndo.CaptureReminderStates(_current.ReminderStates, note.ReminderRules))
            : null,
        DeleteNotesChange delete => CaptureDeletedNotes(delete.Ids),
        SetNoteCompletionChange { Completed: true } completion => FindNote(completion.Id) is { } note
            ? new CompletedNoteUndo(note.Id, note.Text)
            : null,
        SetNotesCompletionChange { Completed: true } completion =>
            new CompletedNotesUndo(FreezeStrings(completion.Ids)),
        _ => throw new ArgumentException("该变更不提供撤销。", nameof(change))
    };

    private NoteRecord? FindNote(string id) =>
        _current.Notes.FirstOrDefault(note => string.Equals(note.Id, id, StringComparison.OrdinalIgnoreCase));

    // 批量删除按设置里的原位置记下每条便签，撤销时插回原处。
    private DeletedNotesUndo CaptureDeletedNotes(IReadOnlyList<string> ids)
    {
        var records = _current.Notes
            .Select((note, index) => new DeletedNoteEntry(FreezeNote(note), index))
            .Where(entry => ids.Contains(entry.Record.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return new DeletedNotesUndo(
            Array.AsReadOnly(records),
            DeletedRecordUndo.CaptureReminderStates(
                _current.ReminderStates,
                records.SelectMany(entry => entry.Record.ReminderRules).ToArray()));
    }

    private void OfferUndo(WorkspaceUndoOffer offer)
    {
        var superseded = _undoOffer;
        _undoOffer = offer;
        _undoRemaining = UndoWindow;
        _undoRunningSinceUtc = UndoHeld() ? null : _store.Clock.UtcNow;
        UndoChanged?.Invoke(this, new WorkspaceUndoChangedEventArgs(
            offer,
            superseded,
            superseded is null ? null : WorkspaceUndoEnd.Superseded));
    }

    private void EndUndo(WorkspaceUndoEnd reason)
    {
        if (_undoOffer is not { } ended)
        {
            return;
        }

        _undoOffer = null;
        _undoRunningSinceUtc = null;
        UndoChanged?.Invoke(this, new WorkspaceUndoChangedEventArgs(null, ended, reason));
    }

    private void SetUndoHold(HashSet<WorkspaceUndoSurface> holds, WorkspaceUndoSurface surface, bool active)
    {
        var wasHeld = UndoHeld();
        var changed = active ? holds.Add(surface) : holds.Remove(surface);
        if (!changed || _undoOffer is null || UndoHeld() == wasHeld)
        {
            return;
        }

        var nowUtc = _store.Clock.UtcNow;
        if (wasHeld)
        {
            _undoRunningSinceUtc = nowUtc;
            return;
        }

        var elapsed = nowUtc - _undoRunningSinceUtc!.Value;
        if (elapsed >= _undoRemaining)
        {
            EndUndo(WorkspaceUndoEnd.Expired);
            return;
        }

        if (elapsed > TimeSpan.Zero)
        {
            _undoRemaining -= elapsed;
        }

        _undoRunningSinceUtc = null;
    }

    private bool UndoHeld() =>
        _undoOffer is { } offer &&
        (_undoHovered.Contains(offer.Surface) || _undoFocused.Contains(offer.Surface));

    private DesktopWorkspaceChangeResult ApplySaveCountdown(SaveCountdownChange change)
    {
        var succeeded = TrySaveCountdown(
            change.ExistingId,
            change.Name,
            change.TargetUtc,
            change.TagIds,
            change.ReminderRules,
            out var error,
            out var errorTarget);
        return change.ReminderRules is null
            ? Result(succeeded, error)
            : new DesktopWorkspaceChangeResult(
                succeeded,
                error,
                ReminderRuleErrorTarget: errorTarget);
    }

    private DesktopWorkspaceChangeResult ApplySaveNote(SaveNoteChange change)
    {
        if (change.ReminderRules is { } reminderRules)
        {
            var succeeded = TrySaveNote(
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
            TrySaveNote(
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
        var succeeded = TryCreateTag(change.Name, change.Color, out var tag, out var error);
        return new DesktopWorkspaceChangeResult(succeeded, error, tag);
    }

    private DesktopWorkspaceChangeResult ApplySavePreferences(SavePreferencesChange change) => Result(
        TrySavePreferences(
            change.DisplayPrecision,
            change.MonthlySalary,
            change.WorkStart,
            change.WorkEnd,
            out var error),
        error);

    private static DesktopWorkspaceChangeResult Result(bool succeeded, string error) =>
        new(succeeded, error);

    private static DesktopWorkspaceChangeResult Result(
        bool succeeded,
        string error,
        ReminderSnoozeErrorCode snoozeErrorCode) =>
        new(succeeded, error, SnoozeErrorCode: snoozeErrorCode);

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
        Array.AsReadOnly(records.Select(FreezeCountdown).ToArray());

    private static CountdownRecord FreezeCountdown(CountdownRecord record) =>
        new(
            record.Id,
            record.Name,
            record.TargetUtc,
            FreezeStrings(record.TagIds),
            FreezeRules(record.ReminderRules));

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
        CountdownReminderSource countdown => new CountdownReminderSource(FreezeCountdown(countdown.Countdown)),
        NoteReminderSource note => new NoteReminderSource(FreezeNote(note.Note)),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}

public abstract record DesktopWorkspaceChange;

public sealed record SaveCountdownChange(
    string? ExistingId,
    string Name,
    DateTimeOffset TargetUtc,
    IReadOnlyList<string> TagIds,
    IReadOnlyList<ReminderRule>? ReminderRules = null) : DesktopWorkspaceChange;

public sealed record DeleteCountdownChange(string Id) : DesktopWorkspaceChange;

public sealed record SaveNoteChange : DesktopWorkspaceChange
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

public sealed record SetNoteCompletionChange(string Id, bool Completed) : DesktopWorkspaceChange;
public sealed record SetNotesCompletionChange(IReadOnlyList<string> Ids, bool Completed) : DesktopWorkspaceChange;

public sealed record DeleteNoteChange(string Id) : DesktopWorkspaceChange;
public sealed record DeleteNotesChange(IReadOnlyList<string> Ids) : DesktopWorkspaceChange;
public sealed record DeletedNoteEntry(NoteRecord Record, int Index);
public sealed record RestoreDeletedNotesChange(IReadOnlyList<DeletedNoteEntry> Records,
    IReadOnlyDictionary<string, ReminderRuleState> ReminderStates) : DesktopWorkspaceChange;

public sealed record DeletedNotesUndo(IReadOnlyList<DeletedNoteEntry> Records,
    IReadOnlyDictionary<string, ReminderRuleState> ReminderStates) : WorkspaceUndo($"{Records.Count} 条便签")
{
    public override DesktopWorkspaceChange ToChange() => new RestoreDeletedNotesChange(Records, ReminderStates);
    public override string OfferedMessage => $"已删除 {Records.Count} 条便签。";
    public override string AccessibleName => $"撤销删除 {Records.Count} 条便签";
    public override string RestoredMessage => $"已撤销删除 {Records.Count} 条便签。";
}

// #155/#214：撤销删除的恢复事务——被删记录与其规则提醒状态在同一事务原样恢复；
// 不引入通用撤销栈。撤销载荷只保留最近一次删除，由发起删除的窗口呈现。
public sealed record RestoreDeletedCountdownChange(
    CountdownRecord Record,
    IReadOnlyDictionary<string, ReminderRuleState> ReminderStates) : DesktopWorkspaceChange;

public sealed record RestoreDeletedNoteChange(
    NoteRecord Record,
    IReadOnlyDictionary<string, ReminderRuleState> ReminderStates) : DesktopWorkspaceChange;

// #214/#233：唯一最近操作槽的载荷与反馈。Description 为来源名称或便签正文。
public abstract record WorkspaceUndo(string Description)
{
    public abstract DesktopWorkspaceChange ToChange();
    public abstract string OfferedMessage { get; }
    public abstract string AccessibleName { get; }
    public abstract string RestoredMessage { get; }
}

public enum WorkspaceUndoSurface
{
    Main,
    Floating
}

public enum WorkspaceUndoEnd
{
    Superseded,
    Restored,
    Expired,
    Dismissed
}

public sealed record WorkspaceUndoOffer(WorkspaceUndoSurface Surface, WorkspaceUndo Payload);

// Current 是槽里现在的载荷；Ended 是这次被收起的载荷及原因（新载荷替换旧载荷时为 Superseded）。
public sealed record WorkspaceUndoChangedEventArgs(
    WorkspaceUndoOffer? Current,
    WorkspaceUndoOffer? Ended,
    WorkspaceUndoEnd? EndReason);

// #233：完成撤销只重新打开同一便签，不回写旧cursor或已处置的提醒状态。
public sealed record CompletedNoteUndo(string Id, string Description) : WorkspaceUndo(Description)
{
    public override DesktopWorkspaceChange ToChange() => new SetNoteCompletionChange(Id, false);
    public override string OfferedMessage => $"已完成「{Description}」·";
    public override string AccessibleName => $"撤销完成便签「{Description}」";
    public override string RestoredMessage => $"已撤销完成便签「{Description}」。";
}

public sealed record CompletedNotesUndo(IReadOnlyList<string> Ids) : WorkspaceUndo($"{Ids.Count} 条便签")
{
    public override DesktopWorkspaceChange ToChange() => new SetNotesCompletionChange(Ids, false);
    public override string OfferedMessage => $"已完成 {Ids.Count} 条便签 ·";
    public override string AccessibleName => $"撤销完成 {Ids.Count} 条便签";
    public override string RestoredMessage => $"已撤销完成 {Ids.Count} 条便签。";
}

// #155/#214：删除前捕获记录与规则状态，撤销删除时原样恢复。
public sealed record DeletedRecordUndo(
    CountdownRecord? Countdown,
    NoteRecord? Note,
    IReadOnlyDictionary<string, ReminderRuleState> ReminderStates,
    string Description) : WorkspaceUndo(Description)
{
    public string KindName => Countdown is not null ? "倒计时" : "便签";

    public override string OfferedMessage => $"已删除{KindName}「{Description}」。";
    public override string AccessibleName => $"撤销删除{KindName}「{Description}」";
    public override string RestoredMessage => $"已撤销删除{KindName}「{Description}」。";

    public override DesktopWorkspaceChange ToChange() => Countdown is { } countdown
        ? new RestoreDeletedCountdownChange(countdown, ReminderStates)
        : new RestoreDeletedNoteChange(Note!, ReminderStates);

    public static DeletedRecordUndo ForCountdown(
        CountdownRecord record,
        IReadOnlyDictionary<string, ReminderRuleState> reminderStates) =>
        new(record, null, reminderStates, record.Name);

    public static DeletedRecordUndo ForNote(
        NoteRecord note,
        IReadOnlyDictionary<string, ReminderRuleState> reminderStates) =>
        new(null, note, reminderStates, note.Text);

    // 删除前从当前设置快照捕获该记录各规则已有的提醒状态；没有状态的规则跳过。
    public static IReadOnlyDictionary<string, ReminderRuleState> CaptureReminderStates(
        IReadOnlyDictionary<string, ReminderRuleState> states,
        IReadOnlyList<ReminderRule> rules) =>
        rules
            .Select(rule => rule.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ruleId => (RuleId: ruleId, Key: states.Keys
                .FirstOrDefault(key => string.Equals(key, ruleId, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.Key is not null)
            .ToDictionary(
                pair => pair.RuleId,
                pair => states[pair.Key!] with { },
                StringComparer.OrdinalIgnoreCase);
}

public sealed record CreateTagChange(string Name, string? Color) : DesktopWorkspaceChange;

public sealed record RenameTagChange(string Id, string Name, string? Color) : DesktopWorkspaceChange;

public sealed record DeleteTagChange(string Id) : DesktopWorkspaceChange;

public sealed record SavePreferencesChange(
    string DisplayPrecision,
    decimal MonthlySalary,
    TimeSpan WorkStart,
    TimeSpan WorkEnd) : DesktopWorkspaceChange;

public sealed record DesktopWorkspaceChangeResult(
    bool Succeeded,
    string Error,
    TagRecord? CreatedTag = null,
    ReminderRuleEditingErrorTarget ReminderRuleErrorTarget = ReminderRuleEditingErrorTarget.None,
    ReminderSnoozeErrorCode SnoozeErrorCode = ReminderSnoozeErrorCode.None);

public sealed record DesktopWorkspaceFilter(
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

    // record 默认相等对 IReadOnlyList 退化为引用相等；激活状态前后两次快照的标签序列
    // 是不同实例，必须按序号逐元素比较（与应用内标签 ID 语义一致的序号忽略大小写）。
    public bool Equals(DesktopWorkspaceFilter? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return SelectedTagIds.SequenceEqual(other.SelectedTagIds, StringComparer.OrdinalIgnoreCase) &&
               MatchMode == other.MatchMode &&
               UntaggedOnly == other.UntaggedOnly &&
               CountdownStatus == other.CountdownStatus &&
               NoteStatus == other.NoteStatus &&
               string.Equals(NoteSearch, other.NoteSearch, StringComparison.Ordinal) &&
               string.Equals(NoteDate, other.NoteDate, StringComparison.Ordinal) &&
               UndatedNotesOnly == other.UndatedNotesOnly;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var tagId in SelectedTagIds)
        {
            hash.Add(tagId, StringComparer.OrdinalIgnoreCase);
        }

        hash.Add(MatchMode);
        hash.Add(UntaggedOnly);
        hash.Add(CountdownStatus);
        hash.Add(NoteStatus);
        hash.Add(NoteSearch, StringComparer.Ordinal);
        hash.Add(NoteDate, StringComparer.Ordinal);
        hash.Add(UndatedNotesOnly);
        return hash.ToHashCode();
    }
}

public sealed record DesktopWorkspaceSnapshot(
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

public sealed record ReminderAcknowledgementRequest(
    ReminderSourceKind SourceKind,
    string SourceId,
    string RuleId,
    DateTimeOffset ScheduledUtc);
