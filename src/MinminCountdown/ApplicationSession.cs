using System.Globalization;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class ApplicationSession
{
    private readonly SettingsStore _store;

    private ApplicationSession(
        string settingsPath,
        SettingsStore store,
        CountdownSettings settings,
        int sourceSchemaVersion,
        bool migrated)
    {
        SettingsPath = Path.GetFullPath(settingsPath);
        _store = store;
        Current = store.Copy(settings);
        SourceSchemaVersion = sourceSchemaVersion;
        SettingsWereMigrated = migrated;
        Reminders.Initialize(Current, store.Clock.UtcNow);
    }

    public event EventHandler? StateChanged;

    public string SettingsPath { get; }

    public CountdownSettings Current { get; private set; }

    public int SourceSchemaVersion { get; }

    public bool SettingsWereMigrated { get; private set; }

    public SettingsRecoveryOutcome Recovery { get; private init; }

    public WorkspaceFilter Filter { get; } = new();

    public ReminderScheduler Reminders { get; } = new();

    public DateTimeOffset BeijingNow => _store.GetCurrentBeijingTime();

    public DateTimeOffset UtcNow => _store.Clock.UtcNow;

    internal IClock Clock => _store.Clock;

    public static ApplicationSession Load(
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
        return new ApplicationSession(
            settingsPath,
            actualStore,
            result.Settings,
            result.SourceSchemaVersion,
            result.Migrated)
        {
            Recovery = result.Recovery
        };
    }

    public void PersistMigrationIfNeeded()
    {
        if (!SettingsWereMigrated)
        {
            return;
        }

        _store.Save(SettingsPath, Current);
        SettingsWereMigrated = false;
    }

    public IReadOnlyList<CountdownRecord> GetSortedCountdowns(bool applyFilter = true) =>
        GetSortedCountdowns(UtcNow, applyFilter);

    internal IReadOnlyList<CountdownRecord> GetSortedCountdowns(
        DateTimeOffset nowUtc,
        bool applyFilter = true)
    {
        return Current.Countdowns
            .Where(record => !applyFilter || Filter.Matches(record, nowUtc))
            .OrderBy(record => record.TargetUtc > nowUtc ? 0 : 1)
            .ThenBy(record => record.TargetUtc)
            .ToArray();
    }

    public IReadOnlyList<NoteRecord> GetSortedNotes(bool applyFilter = true)
    {
        return NoteOrdering.Sort(Current.Notes)
            .Where(note => !applyFilter || Filter.Matches(note))
            .ToArray();
    }

    public bool TrySaveCountdown(
        string? existingId,
        string name,
        DateTimeOffset targetUtc,
        IReadOnlyList<string> tagIds,
        out string error) =>
        TrySaveCountdownCore(
            existingId,
            name,
            targetUtc,
            tagIds,
            reminderRules: null,
            out error,
            out _);

    public bool TrySaveCountdown(
        string? existingId,
        string name,
        DateTimeOffset targetUtc,
        IReadOnlyList<string> tagIds,
        IReadOnlyList<ReminderRule> reminderRules,
        out string error,
        out ReminderRuleEditingErrorTarget errorTarget) =>
        TrySaveCountdownCore(
            existingId,
            name,
            targetUtc,
            tagIds,
            reminderRules,
            out error,
            out errorTarget);

    private bool TrySaveCountdownCore(
        string? existingId,
        string name,
        DateTimeOffset targetUtc,
        IReadOnlyList<string> tagIds,
        IReadOnlyList<ReminderRule>? reminderRules,
        out string error,
        out ReminderRuleEditingErrorTarget errorTarget)
    {
        var records = Current.Countdowns.ToList();
        var normalizedId = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        var index = records.FindIndex(item => string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        var existing = index >= 0 ? records[index] : null;
        var candidateRules = reminderRules?.ToArray() ??
            existing?.ReminderRules.ToArray() ??
            Array.Empty<ReminderRule>();
        var commitNow = UtcNow;
        var scheduleValidation = ReminderRuleEditing.ValidateCountdownRules(
            existing?.ReminderRules ?? Array.Empty<ReminderRule>(),
            candidateRules,
            targetUtc,
            commitNow);
        if (!scheduleValidation.Succeeded)
        {
            error = scheduleValidation.Error;
            errorTarget = scheduleValidation.ErrorTarget;
            return false;
        }

        var record = new CountdownRecord(
            normalizedId,
            name,
            targetUtc,
            tagIds.ToArray(),
            candidateRules);
        if (index >= 0)
        {
            records[index] = record;
        }
        else
        {
            records.Add(record);
        }

        var states = RebuildStatesAfterRuleEdit(
            Current.ReminderStates,
            existing?.ReminderRules,
            candidateRules,
            commitNow);
        errorTarget = ReminderRuleEditingErrorTarget.None;
        return TryCommit(
            Current with { Countdowns = records, ReminderStates = states },
            commitNow,
            out error);
    }

    public bool TryDeleteCountdown(string id, out string error)
    {
        var records = Current.Countdowns
            .Where(record => !string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == Current.Countdowns.Count)
        {
            error = "未找到倒计时。";
            return false;
        }

        var removedRuleIds = Current.Countdowns
            .Where(record => string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static record => record.ReminderRules)
            .Select(static rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var states = Current.ReminderStates
            .Where(pair => !removedRuleIds.Contains(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return TryCommit(Current with { Countdowns = records, ReminderStates = states }, out error);
    }

    public bool TrySaveNote(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        int? reminderIntervalMinutes,
        DateTimeOffset? firstReminderUtc,
        out string error)
    {
        var existing = string.IsNullOrWhiteSpace(existingId)
            ? null
            : Current.Notes.FirstOrDefault(
                note => string.Equals(note.Id, existingId, StringComparison.OrdinalIgnoreCase));
        if (HasSharedOnlyReminderRule(existing))
        {
            error = "包含日历规则的便签不能通过兼容提醒编辑器修改提醒。";
            return false;
        }

        NoteReminder reminder = reminderIntervalMinutes is null
            ? NoReminder.Instance
            : firstReminderUtc is not null
                ? ReminderSchedule.CreateRepeating(firstReminderUtc.Value, reminderIntervalMinutes.Value)
            : existing is not null &&
              ReminderRuleCompatibility.ToLegacy(Current, existing) is RepeatingReminder repeating &&
              repeating.Interval.Minutes == reminderIntervalMinutes.Value
                ? repeating
                : ReminderSchedule.CreateRepeating(
                    UtcNow.AddMinutes(reminderIntervalMinutes.Value),
                    reminderIntervalMinutes.Value);
        if (existing?.IsCompleted == true && !isCompleted && reminderIntervalMinutes is not null)
        {
            reminder = ReminderSchedule.CreateRepeating(
                UtcNow.AddMinutes(reminderIntervalMinutes.Value),
                reminderIntervalMinutes.Value);
        }

        return TrySaveNote(existingId, text, date, isCompleted, tagIds, reminder, out error);
    }

    public bool TrySaveNote(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        NoteReminder reminder,
        out string error)
    {
        var normalizedId = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        var existing = Current.Notes.FirstOrDefault(item =>
            string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (HasSharedOnlyReminderRule(existing))
        {
            error = "包含日历规则的便签不能通过兼容提醒编辑器修改提醒。";
            return false;
        }

        IReadOnlyList<ReminderRule> reminderRules;
        if (existing is { ReminderRules.Count: > 1 })
        {
            if (!Equals(ReminderRuleCompatibility.ToLegacySchedule(existing), reminder))
            {
                error = "包含多条提醒规则的便签不能通过兼容提醒编辑器修改提醒。";
                return false;
            }

            reminderRules = existing.ReminderRules;
        }
        else
        {
            reminderRules = BuildCompatibleReminderRules(normalizedId, existing, reminder);
        }

        return TrySaveNote(
            normalizedId,
            text,
            date,
            isCompleted,
            tagIds,
            reminderRules,
            out error);
    }

    private static bool HasSharedOnlyReminderRule(NoteRecord? note) =>
        note?.ReminderRules.Any(static rule =>
            rule is not (AbsoluteOnceReminderRule or FixedIntervalReminderRule)) == true;

    public bool TrySaveNote(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        IReadOnlyList<ReminderRule> reminderRules,
        out string error) =>
        TrySaveNote(
            existingId,
            text,
            date,
            isCompleted,
            tagIds,
            reminderRules,
            out error,
            out _);

    public bool TrySaveNote(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        IReadOnlyList<ReminderRule> reminderRules,
        out string error,
        out ReminderRuleEditingErrorTarget errorTarget)
    {
        ArgumentNullException.ThrowIfNull(reminderRules);
        var records = Current.Notes.ToList();
        var normalizedId = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        var index = records.FindIndex(item =>
            string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        var existing = index >= 0 ? records[index] : null;
        var candidateRules = reminderRules.ToArray();
        var commitNow = UtcNow;
        var scheduleValidation = ReminderRuleEditing.ValidateChangedSchedules(
            existing?.ReminderRules ?? Array.Empty<ReminderRule>(),
            candidateRules,
            commitNow);
        if (!scheduleValidation.Succeeded)
        {
            error = scheduleValidation.Error;
            errorTarget = scheduleValidation.ErrorTarget;
            return false;
        }

        var record = new NoteRecord(
            normalizedId,
            text,
            date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            isCompleted,
            tagIds.ToArray(),
            candidateRules);
        if (index >= 0)
        {
            records[index] = record;
        }
        else
        {
            records.Add(record);
        }

        var states = RebuildStatesAfterRuleEdit(
            Current.ReminderStates,
            existing?.ReminderRules,
            candidateRules,
            commitNow);
        errorTarget = ReminderRuleEditingErrorTarget.None;
        return TryCommit(Current with { Notes = records, ReminderStates = states }, commitNow, out error);
    }

    public bool TrySaveNote(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        int? reminderIntervalMinutes,
        out string error) =>
        TrySaveNote(
            existingId,
            text,
            date,
            isCompleted,
            tagIds,
            reminderIntervalMinutes,
            (DateTimeOffset?)null,
            out error);

    public bool TrySetNoteCompletion(string id, bool completed, out string error)
    {
        var note = Current.Notes.FirstOrDefault(
            item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            error = "未找到便签。";
            return false;
        }

        var records = Current.Notes
            .Select(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)
                ? item with { IsCompleted = completed }
                : item)
            .ToArray();
        var states = Current.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        if (note.IsCompleted != completed)
        {
            AdvanceNoteRulesThroughTransition(note, _store.Clock.UtcNow, states);
        }

        return TryCommit(Current with { Notes = records, ReminderStates = states }, out error);
    }

    private static void AdvanceNoteRulesThroughTransition(
        NoteRecord note,
        DateTimeOffset transitionUtc,
        IDictionary<string, ReminderRuleState> states)
    {
        var normalizedTransitionUtc = transitionUtc.ToUniversalTime();
        foreach (var rule in note.ReminderRules)
        {
            states.TryGetValue(rule.Id, out var existing);
            var latestScheduledUtc = GetLatestScheduledUtc(rule, normalizedTransitionUtc);
            var handledThroughUtc = existing?.HandledThroughUtc;
            if (latestScheduledUtc is not null &&
                (handledThroughUtc is null || latestScheduledUtc > handledThroughUtc))
            {
                handledThroughUtc = latestScheduledUtc;
            }

            var dispositionKey = existing?.LastDispositionOccurrenceKey;
            if (MinminCountdown.Core.ReminderOccurrenceKey.TryParse(
                existing?.LastDeliveredOccurrenceKey,
                    out var delivered) &&
                delivered.ScheduledUtc <= normalizedTransitionUtc &&
                (!MinminCountdown.Core.ReminderOccurrenceKey.TryParse(dispositionKey, out var disposition) ||
                 disposition.ScheduledUtc < delivered.ScheduledUtc))
            {
                dispositionKey = existing!.LastDeliveredOccurrenceKey;
            }

            if (handledThroughUtc is null &&
                existing?.LastDeliveredOccurrenceKey is null &&
                dispositionKey is null)
            {
                states.Remove(rule.Id);
                continue;
            }

            states[rule.Id] = new ReminderRuleState(
                rule.Id,
                handledThroughUtc,
                existing?.LastDeliveredOccurrenceKey,
                dispositionKey,
                ActiveSnooze: null);
        }
    }

    private static DateTimeOffset? GetLatestScheduledUtc(
        ReminderRule rule,
        DateTimeOffset transitionUtc) => rule switch
        {
            AbsoluteOnceReminderRule once when once.ScheduledUtc <= transitionUtc => once.ScheduledUtc,
            AbsoluteOnceReminderRule => null,
            FixedIntervalReminderRule repeating when repeating.FirstDueUtc <= transitionUtc =>
                ReminderSchedule.GetScheduledUtc(repeating, transitionUtc),
            FixedIntervalReminderRule => null,
            CalendarReminderRule calendar =>
                CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(calendar, null, transitionUtc),
            _ => throw new SettingsValidationException("当前调度器不支持该提醒规则。")
        };

    public bool TryMarkOnceReminderDelivered(
        string id,
        DateTimeOffset expectedFirstDueUtc,
        out string error)
    {
        var note = Current.Notes.FirstOrDefault(
            item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (note is null || ReminderRuleCompatibility.ToLegacy(Current, note) is not OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            } once || once.FirstDueUtc != expectedFirstDueUtc.ToUniversalTime())
        {
            error = "一次性提醒已变化或已送达。";
            return false;
        }

        var rule = note.ReminderRules.Single() as AbsoluteOnceReminderRule;
        if (rule is null)
        {
            error = "一次性提醒规则无效。";
            return false;
        }

        return TryMarkReminderDelivered(id, rule.Id, rule.ScheduledUtc, out error);
    }

    public bool TryMarkReminderDelivered(
        string noteId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        out string error) =>
        TryMarkReminderDelivered(
            ReminderSourceKind.Note,
            noteId,
            ruleId,
            scheduledUtc,
            out error);

    public bool TryMarkReminderDelivered(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        out string error)
    {
        var rule = FindReminderRule(sourceKind, sourceId, ruleId, out var countdownTargetUtc);
        Current.ReminderStates.TryGetValue(ruleId, out var existingState);
        if (rule is null ||
            (!ReminderSchedule.IsOccurrence(rule, scheduledUtc, countdownTargetUtc) &&
             !IsHistoricalRelativeDelivery(rule, scheduledUtc, existingState)))
        {
            error = "提醒规则或 occurrence 已变化。";
            return false;
        }

        var normalizedScheduledUtc = scheduledUtc.ToUniversalTime();
        var occurrence = MinminCountdown.Core.ReminderOccurrenceKey.Create(
            rule,
            normalizedScheduledUtc);
        if (existingState?.HandledThroughUtc is { } handledThroughUtc)
        {
            if (handledThroughUtc == normalizedScheduledUtc &&
                string.Equals(
                    existingState.LastDeliveredOccurrenceKey,
                    occurrence.ToString(),
                    StringComparison.Ordinal))
            {
                error = string.Empty;
                return true;
            }

            if (handledThroughUtc >= normalizedScheduledUtc)
            {
                error = "提醒 occurrence 已经变化。";
                return false;
            }
        }

        var states = Current.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        states[rule.Id] = new ReminderRuleState(
            rule.Id,
            normalizedScheduledUtc,
            occurrence.ToString(),
            existingState?.LastDispositionOccurrenceKey);
        return TryCommit(Current with { ReminderStates = states }, out error);
    }

    public bool TryAcknowledgeReminder(
        string noteId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        out string error) =>
        TryAcknowledgeReminder(
            ReminderSourceKind.Note,
            noteId,
            ruleId,
            scheduledUtc,
            out error);

    public bool TryAcknowledgeReminder(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        out string error)
    {
        var rule = FindReminderRule(sourceKind, sourceId, ruleId, out var countdownTargetUtc);
        Current.ReminderStates.TryGetValue(ruleId, out var knownState);
        if (rule is null ||
            (!ReminderSchedule.IsOccurrence(rule, scheduledUtc, countdownTargetUtc) &&
             !IsHistoricalRelativeDelivery(rule, scheduledUtc, knownState)))
        {
            error = "提醒规则或 occurrence 已变化。";
            return false;
        }

        var occurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
            rule,
            scheduledUtc).ToString();
        var normalizedScheduledUtc = scheduledUtc.ToUniversalTime();
        if (!Current.ReminderStates.TryGetValue(rule.Id, out var existingState) ||
            existingState.HandledThroughUtc is not { } handledThroughUtc ||
            handledThroughUtc < normalizedScheduledUtc ||
            existingState.LastDeliveredOccurrenceKey is null)
        {
            error = "提醒 occurrence 尚未确认送达或已经变化。";
            return false;
        }

        if (MinminCountdown.Core.ReminderOccurrenceKey.TryParse(
                existingState.LastDispositionOccurrenceKey,
                out var lastDispositionOccurrence) &&
            string.Equals(
                lastDispositionOccurrence.RuleId,
                rule.Id,
                StringComparison.OrdinalIgnoreCase) &&
            lastDispositionOccurrence.ScheduledUtc >= normalizedScheduledUtc)
        {
            error = string.Empty;
            return true;
        }

        var states = Current.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        states[rule.Id] = existingState with
        {
            LastDispositionOccurrenceKey = occurrenceKey,
            ActiveSnooze = null
        };
        return TryCommit(Current with { ReminderStates = states }, out error);
    }

    public bool TryAcknowledgeReminders(
        IReadOnlyList<ReminderAcknowledgementRequest> requests,
        out string error)
    {
        if (requests.Count == 0)
        {
            error = "没有可批量处置的提醒。";
            return false;
        }

        var states = Current.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var request in requests)
        {
            var rule = FindReminderRule(
                request.SourceKind,
                request.SourceId,
                request.RuleId,
                out var countdownTargetUtc);
            Current.ReminderStates.TryGetValue(request.RuleId, out var knownState);
            if (rule is null ||
                (!ReminderSchedule.IsOccurrence(rule, request.ScheduledUtc, countdownTargetUtc) &&
                 !IsHistoricalRelativeDelivery(rule, request.ScheduledUtc, knownState)))
            {
                error = "提醒规则或 occurrence 已变化。";
                return false;
            }

            var occurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
                rule,
                request.ScheduledUtc).ToString();
            var normalizedScheduledUtc = request.ScheduledUtc.ToUniversalTime();
            if (!Current.ReminderStates.TryGetValue(rule.Id, out var existingState) ||
                existingState.HandledThroughUtc is not { } handledThroughUtc ||
                handledThroughUtc < normalizedScheduledUtc ||
                existingState.LastDeliveredOccurrenceKey is null)
            {
                error = "提醒 occurrence 尚未确认送达或已经变化。";
                return false;
            }

            if (MinminCountdown.Core.ReminderOccurrenceKey.TryParse(
                    existingState.LastDispositionOccurrenceKey,
                    out var lastDispositionOccurrence) &&
                string.Equals(
                    lastDispositionOccurrence.RuleId,
                    rule.Id,
                    StringComparison.OrdinalIgnoreCase) &&
                lastDispositionOccurrence.ScheduledUtc >= normalizedScheduledUtc)
            {
                continue;
            }

            states[rule.Id] = existingState with
            {
                LastDispositionOccurrenceKey = occurrenceKey,
                ActiveSnooze = null
            };
            changed = true;
        }

        if (!changed)
        {
            error = string.Empty;
            return true;
        }

        return TryCommit(Current with { ReminderStates = states }, out error);
    }

    public bool TrySnoozeReminder(
        string noteId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        ReminderSnoozeOption option,
        DateTimeOffset? customBeijing,
        out string error,
        out ReminderSnoozeErrorCode errorCode) =>
        TrySnoozeReminder(
            ReminderSourceKind.Note,
            noteId,
            ruleId,
            scheduledUtc,
            option,
            customBeijing,
            out error,
            out errorCode);

    public bool TrySnoozeReminder(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        ReminderSnoozeOption option,
        DateTimeOffset? customBeijing,
        out string error,
        out ReminderSnoozeErrorCode errorCode)
    {
        errorCode = ReminderSnoozeErrorCode.None;
        var rule = FindReminderRule(sourceKind, sourceId, ruleId, out var countdownTargetUtc);
        Current.ReminderStates.TryGetValue(ruleId, out var knownState);
        if (rule is null ||
            (!ReminderSchedule.IsOccurrence(rule, scheduledUtc, countdownTargetUtc) &&
             !IsHistoricalRelativeDelivery(rule, scheduledUtc, knownState)))
        {
            error = "提醒规则或 occurrence 已变化。";
            return false;
        }

        var normalizedScheduledUtc = scheduledUtc.ToUniversalTime();
        var occurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
            rule,
            normalizedScheduledUtc).ToString();
        if (!Current.ReminderStates.TryGetValue(rule.Id, out var existingState) ||
            !string.Equals(existingState.LastDeliveredOccurrenceKey, occurrenceKey, StringComparison.Ordinal) ||
            (MinminCountdown.Core.ReminderOccurrenceKey.TryParse(
                 existingState.LastDispositionOccurrenceKey,
                 out var disposition) &&
             disposition.ScheduledUtc >= normalizedScheduledUtc))
        {
            error = "提醒 occurrence 尚未确认送达或已经处置。";
            return false;
        }

        var actionUtc = _store.Clock.UtcNow.ToUniversalTime();
        var snoozeUntilUtc = option switch
        {
            ReminderSnoozeOption.TenMinutes => actionUtc.AddMinutes(10),
            ReminderSnoozeOption.ThirtyMinutes => actionUtc.AddMinutes(30),
            ReminderSnoozeOption.SixtyMinutes => actionUtc.AddHours(1),
            ReminderSnoozeOption.TomorrowAtNine => GetTomorrowAtNineUtc(actionUtc),
            ReminderSnoozeOption.Custom when customBeijing is not null =>
                customBeijing.Value.ToUniversalTime(),
            ReminderSnoozeOption.Custom => DateTimeOffset.MinValue,
            _ => throw new ArgumentOutOfRangeException(nameof(option))
        };
        if (snoozeUntilUtc <= actionUtc)
        {
            error = "自定义稍后时间必须晚于当前北京时间。";
            errorCode = ReminderSnoozeErrorCode.CustomSnoozeNotInFuture;
            return false;
        }

        var states = Current.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        states[rule.Id] = existingState with
        {
            ActiveSnooze = new ReminderSnoozeState(
                occurrenceKey,
                normalizedScheduledUtc,
                snoozeUntilUtc)
        };
        return TryCommit(Current with { ReminderStates = states }, out error);
    }

    private static DateTimeOffset GetTomorrowAtNineUtc(DateTimeOffset actionUtc)
    {
        var tomorrow = ChinaStandardTime.ToBeijingTime(actionUtc).Date.AddDays(1);
        return CountdownFormatting.ToUtc(tomorrow, 9, 0, 0);
    }

    public bool TryDeleteNote(string id, out string error)
    {
        var records = Current.Notes
            .Where(note => !string.Equals(note.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == Current.Notes.Count)
        {
            error = "未找到便签。";
            return false;
        }

        var removedRuleIds = Current.Notes
            .Where(note => string.Equals(note.Id, id, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static note => note.ReminderRules)
            .Select(static rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var states = Current.ReminderStates
            .Where(pair => !removedRuleIds.Contains(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return TryCommit(Current with { Notes = records, ReminderStates = states }, out error);
    }

    // #155：撤销删除——记录与规则提醒状态在同一事务恢复；仅当同 ID 记录不存在时执行，
    // 已被占用的规则状态不覆盖，避免回写比删除后更新的提醒状态。
    public bool TryRestoreCountdown(
        CountdownRecord record,
        IReadOnlyDictionary<string, ReminderRuleState> reminderStates,
        out string error)
    {
        if (Current.Countdowns.Any(item =>
                string.Equals(item.Id, record.Id, StringComparison.OrdinalIgnoreCase)))
        {
            error = "倒计时已存在，无法撤销删除。";
            return false;
        }

        var states = MergeRestoredReminderStates(Current.ReminderStates, reminderStates);
        return TryCommit(
            Current with { Countdowns = Current.Countdowns.Append(record).ToArray(), ReminderStates = states },
            out error);
    }

    public bool TryRestoreNote(
        NoteRecord record,
        IReadOnlyDictionary<string, ReminderRuleState> reminderStates,
        out string error)
    {
        if (Current.Notes.Any(item =>
                string.Equals(item.Id, record.Id, StringComparison.OrdinalIgnoreCase)))
        {
            error = "便签已存在，无法撤销删除。";
            return false;
        }

        var states = MergeRestoredReminderStates(Current.ReminderStates, reminderStates);
        return TryCommit(
            Current with { Notes = Current.Notes.Append(record).ToArray(), ReminderStates = states },
            out error);
    }

    private static IReadOnlyDictionary<string, ReminderRuleState> MergeRestoredReminderStates(
        IReadOnlyDictionary<string, ReminderRuleState> current,
        IReadOnlyDictionary<string, ReminderRuleState> restored)
    {
        var states = new Dictionary<string, ReminderRuleState>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in restored)
        {
            if (!states.ContainsKey(pair.Key))
            {
                states[pair.Key] = pair.Value with { };
            }
        }

        return states;
    }

    public bool TryCreateTag(string name, string? color, out TagRecord? tag, out string error)
    {
        tag = new TagRecord(Guid.NewGuid().ToString("N"), name, color);
        var createdId = tag.Id;
        if (TryCommit(Current with { Tags = Current.Tags.Append(tag).ToArray() }, out error))
        {
            tag = Current.Tags.First(item => string.Equals(item.Id, createdId, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        tag = null;
        return false;
    }

    public bool TryRenameTag(string id, string name, string? color, out string error)
    {
        var found = false;
        var tags = Current.Tags.Select(tag =>
        {
            if (!string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return tag;
            }

            found = true;
            return tag with { Name = name, Color = color };
        }).ToArray();
        if (!found)
        {
            error = "未找到标签。";
            return false;
        }

        return TryCommit(Current with { Tags = tags }, out error);
    }

    public bool TryDeleteTag(string id, out string error)
    {
        var tags = Current.Tags
            .Where(tag => !string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (tags.Length == Current.Tags.Count)
        {
            error = "未找到标签。";
            return false;
        }

        var countdowns = Current.Countdowns
            .Select(record => record with
            {
                TagIds = record.TagIds.Where(
                    tagId => !string.Equals(tagId, id, StringComparison.OrdinalIgnoreCase)).ToArray()
            })
            .ToArray();
        var notes = Current.Notes
            .Select(note => note with
            {
                TagIds = note.TagIds.Where(
                    tagId => !string.Equals(tagId, id, StringComparison.OrdinalIgnoreCase)).ToArray()
            })
            .ToArray();
        var candidate = Current with { Tags = tags, Countdowns = countdowns, Notes = notes };
        if (!TryCommit(candidate, out error))
        {
            return false;
        }

        Filter.SelectedTagIds.Remove(id);
        return true;
    }

    public bool TrySavePreferences(
        string displayPrecision,
        decimal monthlySalary,
        TimeSpan workStart,
        TimeSpan workEnd,
        out string error)
    {
        var preferences = new CountdownPreferences(
            displayPrecision,
            monthlySalary,
            workStart,
            workEnd,
            Current.Preferences.ThemePreference);
        return TryCommit(Current with { Preferences = preferences }, out error);
    }

    private static bool StateSurvivesRuleEdit(
        IReadOnlyList<ReminderRule>? existingRules,
        IReadOnlyList<ReminderRule> candidateRules,
        string stateRuleId)
    {
        var oldRule = existingRules?.FirstOrDefault(rule =>
            string.Equals(rule.Id, stateRuleId, StringComparison.OrdinalIgnoreCase));
        if (oldRule is null)
        {
            return true;
        }

        var candidateRule = candidateRules.FirstOrDefault(rule =>
            string.Equals(rule.Id, stateRuleId, StringComparison.OrdinalIgnoreCase));
        return ReminderRuleSchedule.AreEquivalent(oldRule, candidateRule);
    }

    private static IReadOnlyDictionary<string, ReminderRuleState> RebuildStatesAfterRuleEdit(
        IReadOnlyDictionary<string, ReminderRuleState> existingStates,
        IReadOnlyList<ReminderRule>? existingRules,
        IReadOnlyList<ReminderRule> candidateRules,
        DateTimeOffset commitNow)
    {
        var states = existingStates
            .Where(pair => StateSurvivesRuleEdit(existingRules, candidateRules, pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var calendar in candidateRules.OfType<CalendarReminderRule>())
        {
            var existingRule = existingRules?.FirstOrDefault(rule =>
                string.Equals(rule.Id, calendar.Id, StringComparison.OrdinalIgnoreCase));
            if (ReminderRuleSchedule.AreEquivalent(existingRule, calendar))
            {
                continue;
            }

            var handledThroughUtc = CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                calendar,
                null,
                commitNow);
            if (handledThroughUtc is not null)
            {
                states[calendar.Id] = new ReminderRuleState(
                    calendar.Id,
                    handledThroughUtc,
                    null);
            }
        }

        return states;
    }

    private ReminderRule? FindReminderRule(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        out DateTimeOffset? countdownTargetUtc)
    {
        countdownTargetUtc = null;
        IReadOnlyList<ReminderRule>? rules;
        if (sourceKind == ReminderSourceKind.Countdown)
        {
            var countdown = Current.Countdowns.FirstOrDefault(item =>
                string.Equals(item.Id, sourceId, StringComparison.OrdinalIgnoreCase));
            countdownTargetUtc = countdown?.TargetUtc;
            rules = countdown?.ReminderRules;
        }
        else
        {
            rules = Current.Notes.FirstOrDefault(item =>
                string.Equals(item.Id, sourceId, StringComparison.OrdinalIgnoreCase))?.ReminderRules;
        }

        return rules?.FirstOrDefault(item =>
            string.Equals(item.Id, ruleId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHistoricalRelativeDelivery(
        ReminderRule rule,
        DateTimeOffset scheduledUtc,
        ReminderRuleState? state) =>
        rule is CountdownRelativeReminderRule &&
        state?.LastDeliveredOccurrenceKey is { } deliveredKey &&
        string.Equals(
            deliveredKey,
            ReminderOccurrenceKey.Create(rule, scheduledUtc).ToString(),
            StringComparison.Ordinal);

    private static IReadOnlyList<ReminderRule> BuildCompatibleReminderRules(
        string noteId,
        NoteRecord? existing,
        NoteReminder reminder)
    {
        if (reminder is NoReminder)
        {
            return Array.Empty<ReminderRule>();
        }

        var ruleId = existing?.ReminderRules.FirstOrDefault()?.Id ?? Guid.NewGuid().ToString("N");
        return reminder switch
        {
            OnceReminder once =>
            [
                new AbsoluteOnceReminderRule(ruleId, once.FirstDueUtc.ToUniversalTime())
            ],
            RepeatingReminder repeating =>
            [
                new FixedIntervalReminderRule(
                    ruleId,
                    repeating.FirstDueUtc.ToUniversalTime(),
                    repeating.Interval)
            ],
            _ => throw new SettingsValidationException($"便签 {noteId} 包含无效的兼容提醒。")
        };
    }

    public bool TryCommit(CountdownSettings candidate, out string error) =>
        TryCommit(candidate, null, out error);

    private bool TryCommit(
        CountdownSettings candidate,
        DateTimeOffset? commitNow,
        out string error)
    {
        CountdownSettings previous;
        CountdownSettings canonical;
        try
        {
            previous = Current;
            canonical = _store.Copy(candidate);
            _store.Save(SettingsPath, canonical);
        }
        catch (Exception exception) when (
            exception is SettingsValidationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            error = exception.Message;
            return false;
        }

        Current = canonical;
        Reminders.Synchronize(previous, canonical, commitNow ?? _store.Clock.UtcNow);
        error = string.Empty;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public string SerializeCurrent() => _store.Serialize(Current);
}

internal sealed record ReminderAcknowledgementRequest(
    ReminderSourceKind SourceKind,
    string SourceId,
    string RuleId,
    DateTimeOffset ScheduledUtc);
