using System.Globalization;

namespace MinminCountdown.Core;

// 工作区的提交事务：候选设置先经规范化与原子落盘，成功后才替换内存状态并同步提醒 cursor。
public sealed partial class DesktopWorkspace
{
    private IReadOnlyList<CountdownRecord> GetSortedCountdowns(DateTimeOffset nowUtc, bool applyFilter) =>
        _current.Countdowns
            .Where(record => !applyFilter || _filter.Matches(record, nowUtc))
            .OrderBy(record => record.TargetUtc > nowUtc ? 0 : 1)
            .ThenBy(record => record.TargetUtc)
            .ToArray();

    private IReadOnlyList<NoteRecord> GetSortedNotes(bool applyFilter) =>
        NoteOrdering.Sort(_current.Notes)
            .Where(note => !applyFilter || _filter.Matches(note))
            .ToArray();

    private bool TrySaveCountdown(
        string? existingId,
        string name,
        DateTimeOffset targetUtc,
        IReadOnlyList<string> tagIds,
        IReadOnlyList<ReminderRule>? reminderRules,
        out string error,
        out ReminderRuleEditingErrorTarget errorTarget)
    {
        var records = _current.Countdowns.ToList();
        var normalizedId = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        var index = records.FindIndex(item => string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        var existing = index >= 0 ? records[index] : null;
        var candidateRules = reminderRules?.ToArray() ??
            existing?.ReminderRules.ToArray() ??
            Array.Empty<ReminderRule>();
        var commitNow = _store.Clock.UtcNow;
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
            _current.ReminderStates,
            existing?.ReminderRules,
            candidateRules,
            commitNow);
        errorTarget = ReminderRuleEditingErrorTarget.None;
        return TryCommit(
            _current with { Countdowns = records, ReminderStates = states },
            commitNow,
            out error);
    }

    private bool TryDeleteCountdown(string id, out string error)
    {
        var records = _current.Countdowns
            .Where(record => !string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == _current.Countdowns.Count)
        {
            error = "未找到倒计时。";
            return false;
        }

        var removedRuleIds = _current.Countdowns
            .Where(record => string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static record => record.ReminderRules)
            .Select(static rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var states = _current.ReminderStates
            .Where(pair => !removedRuleIds.Contains(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return TryCommit(_current with { Countdowns = records, ReminderStates = states }, out error);
    }

    private bool TrySaveNote(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        NoteReminder reminder,
        out string error)
    {
        var normalizedId = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        var existing = _current.Notes.FirstOrDefault(item =>
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
            out error,
            out _);
    }

    private static bool HasSharedOnlyReminderRule(NoteRecord? note) =>
        note?.ReminderRules.Any(static rule =>
            rule is not (AbsoluteOnceReminderRule or FixedIntervalReminderRule)) == true;

    private bool TrySaveNote(
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
        var records = _current.Notes.ToList();
        var normalizedId = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        var index = records.FindIndex(item =>
            string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        var existing = index >= 0 ? records[index] : null;
        var candidateRules = reminderRules.ToArray();
        var commitNow = _store.Clock.UtcNow;
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
            _current.ReminderStates,
            existing?.ReminderRules,
            candidateRules,
            commitNow);
        errorTarget = ReminderRuleEditingErrorTarget.None;
        return TryCommit(_current with { Notes = records, ReminderStates = states }, commitNow, out error);
    }

    private bool TrySetNoteCompletion(string id, bool completed, out string error)
        => TrySetNotesCompletion(new[] { id }, completed, out error);

    private bool TrySetNotesCompletion(IReadOnlyList<string> ids, bool completed, out string error)
    {
        if (!TryResolveNotes(ids, out var selected, out error)) return false;
        var transitionUtc = _store.Clock.UtcNow;
        var records = _current.Notes
            .Select(item => selected.Contains(item.Id)
                ? item with { IsCompleted = completed }
                : item)
            .ToArray();
        var states = _current.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        foreach (var note in _current.Notes.Where(note => selected.Contains(note.Id) && note.IsCompleted != completed))
        {
            AdvanceNoteRulesThroughTransition(note, transitionUtc, states);
        }

        return TryCommit(_current with { Notes = records, ReminderStates = states }, transitionUtc, out error);
    }

    private bool TryResolveNotes(IReadOnlyList<string> ids, out HashSet<string> selected, out string error)
    {
        selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = _current.Notes.Select(note => note.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        error = selected.Count == 0 || !selected.IsSubsetOf(available) ? "未找到所选便签，请重新选择。" : string.Empty;
        return error.Length == 0;
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
            if (ReminderOccurrenceKey.TryParse(
                existing?.LastDeliveredOccurrenceKey,
                    out var delivered) &&
                delivered.ScheduledUtc <= normalizedTransitionUtc &&
                (!ReminderOccurrenceKey.TryParse(dispositionKey, out var disposition) ||
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

    private bool TryMarkReminderDelivered(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        out string error)
    {
        var rule = FindReminderRule(sourceKind, sourceId, ruleId, out var countdownTargetUtc);
        _current.ReminderStates.TryGetValue(ruleId, out var existingState);
        if (rule is null ||
            (!ReminderSchedule.IsOccurrence(rule, scheduledUtc, countdownTargetUtc) &&
             !IsHistoricalRelativeDelivery(rule, scheduledUtc, existingState)))
        {
            error = "提醒规则或 occurrence 已变化。";
            return false;
        }

        var normalizedScheduledUtc = scheduledUtc.ToUniversalTime();
        var occurrence = ReminderOccurrenceKey.Create(
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

        var states = _current.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        states[rule.Id] = new ReminderRuleState(
            rule.Id,
            normalizedScheduledUtc,
            occurrence.ToString(),
            existingState?.LastDispositionOccurrenceKey);
        return TryCommit(_current with { ReminderStates = states }, out error);
    }

    private bool TryAcknowledgeReminder(
        ReminderSourceKind sourceKind,
        string sourceId,
        string ruleId,
        DateTimeOffset scheduledUtc,
        out string error)
    {
        var rule = FindReminderRule(sourceKind, sourceId, ruleId, out var countdownTargetUtc);
        _current.ReminderStates.TryGetValue(ruleId, out var knownState);
        if (rule is null ||
            (!ReminderSchedule.IsOccurrence(rule, scheduledUtc, countdownTargetUtc) &&
             !IsHistoricalRelativeDelivery(rule, scheduledUtc, knownState)))
        {
            error = "提醒规则或 occurrence 已变化。";
            return false;
        }

        var occurrenceKey = ReminderOccurrenceKey.Create(
            rule,
            scheduledUtc).ToString();
        var normalizedScheduledUtc = scheduledUtc.ToUniversalTime();
        if (!_current.ReminderStates.TryGetValue(rule.Id, out var existingState) ||
            existingState.HandledThroughUtc is not { } handledThroughUtc ||
            handledThroughUtc < normalizedScheduledUtc ||
            existingState.LastDeliveredOccurrenceKey is null)
        {
            error = "提醒 occurrence 尚未确认送达或已经变化。";
            return false;
        }

        if (ReminderOccurrenceKey.TryParse(
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

        var states = _current.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        states[rule.Id] = existingState with
        {
            LastDispositionOccurrenceKey = occurrenceKey,
            ActiveSnooze = null
        };
        return TryCommit(_current with { ReminderStates = states }, out error);
    }

    private bool TryAcknowledgeReminders(
        IReadOnlyList<ReminderAcknowledgementRequest> requests,
        out string error)
    {
        if (requests.Count == 0)
        {
            error = "没有可批量处置的提醒。";
            return false;
        }

        var states = _current.ReminderStates.ToDictionary(
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
            _current.ReminderStates.TryGetValue(request.RuleId, out var knownState);
            if (rule is null ||
                (!ReminderSchedule.IsOccurrence(rule, request.ScheduledUtc, countdownTargetUtc) &&
                 !IsHistoricalRelativeDelivery(rule, request.ScheduledUtc, knownState)))
            {
                error = "提醒规则或 occurrence 已变化。";
                return false;
            }

            var occurrenceKey = ReminderOccurrenceKey.Create(
                rule,
                request.ScheduledUtc).ToString();
            var normalizedScheduledUtc = request.ScheduledUtc.ToUniversalTime();
            if (!_current.ReminderStates.TryGetValue(rule.Id, out var existingState) ||
                existingState.HandledThroughUtc is not { } handledThroughUtc ||
                handledThroughUtc < normalizedScheduledUtc ||
                existingState.LastDeliveredOccurrenceKey is null)
            {
                error = "提醒 occurrence 尚未确认送达或已经变化。";
                return false;
            }

            if (ReminderOccurrenceKey.TryParse(
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

        return TryCommit(_current with { ReminderStates = states }, out error);
    }

    private bool TrySnoozeReminder(
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
        _current.ReminderStates.TryGetValue(ruleId, out var knownState);
        if (rule is null ||
            (!ReminderSchedule.IsOccurrence(rule, scheduledUtc, countdownTargetUtc) &&
             !IsHistoricalRelativeDelivery(rule, scheduledUtc, knownState)))
        {
            error = "提醒规则或 occurrence 已变化。";
            return false;
        }

        var normalizedScheduledUtc = scheduledUtc.ToUniversalTime();
        var occurrenceKey = ReminderOccurrenceKey.Create(
            rule,
            normalizedScheduledUtc).ToString();
        if (!_current.ReminderStates.TryGetValue(rule.Id, out var existingState) ||
            !string.Equals(existingState.LastDeliveredOccurrenceKey, occurrenceKey, StringComparison.Ordinal) ||
            (ReminderOccurrenceKey.TryParse(
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

        var states = _current.ReminderStates.ToDictionary(
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
        return TryCommit(_current with { ReminderStates = states }, out error);
    }

    private static DateTimeOffset GetTomorrowAtNineUtc(DateTimeOffset actionUtc)
    {
        var tomorrow = ChinaStandardTime.ToBeijingTime(actionUtc).Date.AddDays(1);
        return CountdownFormatting.ToUtc(tomorrow, 9, 0, 0);
    }

    private bool TryDeleteNote(string id, out string error)
        => TryDeleteNotes(new[] { id }, out error);

    private bool TryDeleteNotes(IReadOnlyList<string> ids, out string error)
    {
        if (!TryResolveNotes(ids, out var selected, out error)) return false;
        var records = _current.Notes
            .Where(note => !selected.Contains(note.Id))
            .ToArray();

        var removedRuleIds = _current.Notes
            .Where(note => selected.Contains(note.Id))
            .SelectMany(static note => note.ReminderRules)
            .Select(static rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var states = _current.ReminderStates
            .Where(pair => !removedRuleIds.Contains(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return TryCommit(_current with { Notes = records, ReminderStates = states }, out error);
    }

    // #155：撤销删除——记录与规则提醒状态在同一事务恢复；仅当同 ID 记录不存在时执行，
    // 已被占用的规则状态不覆盖，避免回写比删除后更新的提醒状态。
    private bool TryRestoreCountdown(
        CountdownRecord record,
        IReadOnlyDictionary<string, ReminderRuleState> reminderStates,
        out string error)
    {
        if (_current.Countdowns.Any(item =>
                string.Equals(item.Id, record.Id, StringComparison.OrdinalIgnoreCase)))
        {
            error = "倒计时已存在，无法撤销删除。";
            return false;
        }

        var states = MergeRestoredReminderStates(_current.ReminderStates, reminderStates);
        return TryCommit(
            _current with { Countdowns = _current.Countdowns.Append(record).ToArray(), ReminderStates = states },
            out error);
    }

    private bool TryRestoreNote(
        NoteRecord record,
        IReadOnlyDictionary<string, ReminderRuleState> reminderStates,
        out string error)
    {
        if (_current.Notes.Any(item =>
                string.Equals(item.Id, record.Id, StringComparison.OrdinalIgnoreCase)))
        {
            error = "便签已存在，无法撤销删除。";
            return false;
        }

        var states = MergeRestoredReminderStates(_current.ReminderStates, reminderStates);
        return TryCommit(
            _current with { Notes = _current.Notes.Append(record).ToArray(), ReminderStates = states },
            out error);
    }

    private bool TryRestoreNotes(IReadOnlyList<DeletedNoteEntry> records,
        IReadOnlyDictionary<string, ReminderRuleState> reminderStates, out string error)
    {
        var ids = records.Select(entry => entry.Record.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (records.Count == 0 || ids.Count != records.Count || _current.Notes.Any(note => ids.Contains(note.Id)))
        {
            error = "便签已存在或撤销载荷无效，无法撤销删除。";
            return false;
        }
        var restored = _current.Notes.ToList();
        foreach (var entry in records.OrderBy(entry => entry.Index))
            restored.Insert(Math.Clamp(entry.Index, 0, restored.Count), entry.Record);
        return TryCommit(_current with { Notes = restored.ToArray(),
            ReminderStates = MergeRestoredReminderStates(_current.ReminderStates, reminderStates) }, out error);
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

    private bool TryCreateTag(string name, string? color, out TagRecord? tag, out string error)
    {
        tag = new TagRecord(Guid.NewGuid().ToString("N"), name, color);
        var createdId = tag.Id;
        if (TryCommit(_current with { Tags = _current.Tags.Append(tag).ToArray() }, out error))
        {
            tag = _current.Tags.First(item => string.Equals(item.Id, createdId, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        tag = null;
        return false;
    }

    private bool TryRenameTag(string id, string name, string? color, out string error)
    {
        var found = false;
        var tags = _current.Tags.Select(tag =>
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

        return TryCommit(_current with { Tags = tags }, out error);
    }

    private bool TryDeleteTag(string id, out string error)
    {
        var tags = _current.Tags
            .Where(tag => !string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (tags.Length == _current.Tags.Count)
        {
            error = "未找到标签。";
            return false;
        }

        var countdowns = _current.Countdowns
            .Select(record => record with
            {
                TagIds = record.TagIds.Where(
                    tagId => !string.Equals(tagId, id, StringComparison.OrdinalIgnoreCase)).ToArray()
            })
            .ToArray();
        var notes = _current.Notes
            .Select(note => note with
            {
                TagIds = note.TagIds.Where(
                    tagId => !string.Equals(tagId, id, StringComparison.OrdinalIgnoreCase)).ToArray()
            })
            .ToArray();
        var candidate = _current with { Tags = tags, Countdowns = countdowns, Notes = notes };
        if (!TryCommit(candidate, out error))
        {
            return false;
        }

        _filter = _filter with
        {
            SelectedTagIds = Array.AsReadOnly(_filter.SelectedTagIds
                .Where(tagId => !string.Equals(tagId, id, StringComparison.OrdinalIgnoreCase))
                .ToArray())
        };
        return true;
    }

    private bool TrySavePreferences(
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
            _current.Preferences.ThemePreference);
        return TryCommit(_current with { Preferences = preferences }, out error);
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
            var countdown = _current.Countdowns.FirstOrDefault(item =>
                string.Equals(item.Id, sourceId, StringComparison.OrdinalIgnoreCase));
            countdownTargetUtc = countdown?.TargetUtc;
            rules = countdown?.ReminderRules;
        }
        else
        {
            rules = _current.Notes.FirstOrDefault(item =>
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

    private bool TryCommit(CountdownSettings candidate, out string error) =>
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
            previous = _current;
            canonical = _store.Copy(candidate);
            _store.Save(_settingsPath, canonical);
        }
        catch (Exception exception) when (
            exception is SettingsValidationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            error = exception.Message;
            return false;
        }

        _current = canonical;
        _reminders.Synchronize(previous, canonical, commitNow ?? _store.Clock.UtcNow);
        error = string.Empty;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
