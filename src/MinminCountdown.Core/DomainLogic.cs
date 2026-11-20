using System.Globalization;

namespace MinminCountdown.Core;

public enum WorkspaceKind
{
    Countdowns,
    Notes
}

public enum TagMatchMode
{
    Any,
    All
}

public enum CountdownStatusFilter
{
    All,
    Active,
    Expired
}

public enum NoteStatusFilter
{
    All,
    Active,
    Completed
}

public static class CountdownFormatting
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

public static class ReminderSchedule
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

    public static DateTimeOffset? GetNextDueUtc(
        CountdownSettings settings,
        NoteRecord note,
        DateTimeOffset nowUtc)
    {
        if (note.IsCompleted || note.ReminderRules.Count == 0)
        {
            return null;
        }

        var candidates = note.ReminderRules
            .Select(rule => GetNextDueUtc(settings, rule, nowUtc))
            .Where(static due => due is not null)
            .Select(static due => due!.Value)
            .ToArray();
        return candidates.Length == 0 ? null : candidates.Min();
    }

    private static DateTimeOffset? GetNextDueUtc(
        CountdownSettings settings,
        ReminderRule rule,
        DateTimeOffset nowUtc)
    {
        if (rule is AbsoluteOnceReminderRule once)
        {
            return settings.ReminderStates.TryGetValue(once.Id, out var onceState) &&
                   onceState.HandledThroughUtc >= once.ScheduledUtc
                ? null
                : once.ScheduledUtc;
        }

        if (rule is FixedIntervalReminderRule repeating)
        {
            if (nowUtc < repeating.FirstDueUtc)
            {
                return repeating.FirstDueUtc;
            }

            var completedCycles = Math.Floor((nowUtc - repeating.FirstDueUtc).TotalMinutes /
                repeating.Interval.Minutes) + 1;
            return repeating.FirstDueUtc.AddMinutes(completedCycles * repeating.Interval.Minutes);
        }

        if (rule is CalendarReminderRule calendar)
        {
            settings.ReminderStates.TryGetValue(calendar.Id, out var calendarState);
            return CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                       calendar,
                       calendarState?.HandledThroughUtc,
                       nowUtc) ??
                   CalendarOccurrenceCalculator.GetNextOccurrenceUtc(calendar, nowUtc);
        }

        throw new SettingsValidationException("当前调度器不支持该提醒规则。");
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

    public static DateTimeOffset GetScheduledUtc(ReminderRule rule, DateTimeOffset nowUtc)
        => GetScheduledUtc(rule, nowUtc, countdownTargetUtc: null);

    public static DateTimeOffset GetScheduledUtc(
        ReminderRule rule,
        DateTimeOffset nowUtc,
        DateTimeOffset? countdownTargetUtc)
    {
        return rule switch
        {
            AbsoluteOnceReminderRule once => once.ScheduledUtc,
            FixedIntervalReminderRule repeating => GetFixedIntervalScheduledUtc(repeating, nowUtc),
            CountdownRelativeReminderRule relative when countdownTargetUtc is { } targetUtc &&
                                                        TryGetCountdownRelativeScheduledUtc(
                                                            targetUtc,
                                                            relative.OffsetMinutesBefore,
                                                            out var scheduledUtc) => scheduledUtc,
            CalendarReminderRule calendar =>
                CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(calendar, null, nowUtc) ??
                throw new SettingsValidationException("当前时间之前没有可用的日历提醒 occurrence。"),
            _ => throw new SettingsValidationException("当前调度器不支持该提醒规则。")
        };
    }

    public static bool IsOccurrence(ReminderRule rule, DateTimeOffset scheduledUtc)
        => IsOccurrence(rule, scheduledUtc, countdownTargetUtc: null);

    public static bool IsOccurrence(
        ReminderRule rule,
        DateTimeOffset scheduledUtc,
        DateTimeOffset? countdownTargetUtc)
    {
        var normalized = scheduledUtc.ToUniversalTime();
        return rule switch
        {
            AbsoluteOnceReminderRule once => normalized == once.ScheduledUtc,
            FixedIntervalReminderRule repeating when normalized >= repeating.FirstDueUtc =>
                (normalized.Ticks - repeating.FirstDueUtc.Ticks) %
                TimeSpan.FromMinutes(repeating.Interval.Minutes).Ticks == 0,
            CountdownRelativeReminderRule relative when countdownTargetUtc is { } targetUtc &&
                                                        TryGetCountdownRelativeScheduledUtc(
                                                            targetUtc,
                                                            relative.OffsetMinutesBefore,
                                                            out var relativeScheduledUtc) =>
                normalized == relativeScheduledUtc,
            CalendarReminderRule calendar =>
                CalendarOccurrenceCalculator.IsOccurrence(calendar, normalized),
            _ => false
        };
    }

    public static bool TryGetCountdownRelativeScheduledUtc(
        DateTimeOffset targetUtc,
        int offsetMinutesBefore,
        out DateTimeOffset scheduledUtc)
    {
        scheduledUtc = default;
        if (offsetMinutesBefore is < 0 or > 5_256_000)
        {
            return false;
        }

        var normalizedTargetUtc = targetUtc.ToUniversalTime();
        var offsetTicks = TimeSpan.FromMinutes(offsetMinutesBefore).Ticks;
        if (normalizedTargetUtc.Ticks < DateTimeOffset.MinValue.UtcTicks + offsetTicks)
        {
            return false;
        }

        scheduledUtc = normalizedTargetUtc.AddTicks(-offsetTicks);
        return true;
    }

    private static DateTimeOffset GetFixedIntervalScheduledUtc(
        FixedIntervalReminderRule reminder,
        DateTimeOffset nowUtc)
    {
        if (nowUtc <= reminder.FirstDueUtc)
        {
            return reminder.FirstDueUtc;
        }

        var intervalTicks = TimeSpan.FromMinutes(reminder.Interval.Minutes).Ticks;
        var completedIntervals = (nowUtc.ToUniversalTime().Ticks - reminder.FirstDueUtc.Ticks) / intervalTicks;
        return reminder.FirstDueUtc.AddTicks(completedIntervals * intervalTicks);
    }
}

public sealed class ReminderScheduler
{
    private static readonly IReadOnlyList<DueReminderOccurrence> EmptyDueOccurrences =
        Array.AsReadOnly(Array.Empty<DueReminderOccurrence>());

    private readonly Dictionary<string, long> _lastRepeatCycles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<CalendarReminderRule, DateTimeOffset?, DateTimeOffset, DateTimeOffset?>
        _getLatestCalendarOccurrenceUtc;
    private readonly Func<CalendarReminderRule, DateTimeOffset, DateTimeOffset?>?
        _getNextCalendarOccurrenceUtc;
    private long _mutationVersion;
    private ScanGate? _scanGate;

    public ReminderScheduler()
        : this(
            CalendarOccurrenceCalculator.GetLatestOccurrenceUtc,
            CalendarOccurrenceCalculator.GetNextOccurrenceUtc)
    {
    }

    public ReminderScheduler(
        Func<CalendarReminderRule, DateTimeOffset?, DateTimeOffset, DateTimeOffset?>
            getLatestCalendarOccurrenceUtc)
        : this(getLatestCalendarOccurrenceUtc, getNextCalendarOccurrenceUtc: null)
    {
    }

    public ReminderScheduler(
        Func<CalendarReminderRule, DateTimeOffset?, DateTimeOffset, DateTimeOffset?>
            getLatestCalendarOccurrenceUtc,
        Func<CalendarReminderRule, DateTimeOffset, DateTimeOffset?>?
            getNextCalendarOccurrenceUtc)
    {
        _getLatestCalendarOccurrenceUtc = getLatestCalendarOccurrenceUtc ??
            throw new ArgumentNullException(nameof(getLatestCalendarOccurrenceUtc));
        _getNextCalendarOccurrenceUtc = getNextCalendarOccurrenceUtc;
    }

    public void Initialize(CountdownSettings settings, DateTimeOffset nowUtc)
    {
        _lastRepeatCycles.Clear();
        foreach (var source in GetActiveSources(settings))
        {
            foreach (var repeating in source.ReminderRules.OfType<FixedIntervalReminderRule>())
            {
                _lastRepeatCycles[repeating.Id] = TryGetHandledRepeatCycle(settings, repeating);
            }
        }

        InvalidateScanGate();
    }

    public void Synchronize(CountdownSettings previous, CountdownSettings current, DateTimeOffset nowUtc)
    {
        var activeRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in GetActiveSources(current))
        {
            var old = GetActiveSources(previous).FirstOrDefault(item =>
                item.Kind == source.Kind &&
                string.Equals(item.Id, source.Id, StringComparison.OrdinalIgnoreCase));
            foreach (var repeating in source.ReminderRules.OfType<FixedIntervalReminderRule>())
            {
                activeRuleIds.Add(repeating.Id);
                var oldRule = old?.ReminderRules.FirstOrDefault(rule =>
                    string.Equals(rule.Id, repeating.Id, StringComparison.OrdinalIgnoreCase));
                var ruleUnchanged = old is not null && Equals(oldRule, repeating);
                if (!ruleUnchanged || !_lastRepeatCycles.ContainsKey(repeating.Id))
                {
                    _lastRepeatCycles[repeating.Id] = TryGetHandledRepeatCycle(current, repeating);
                }
            }
        }

        foreach (var staleId in _lastRepeatCycles.Keys
                     .Where(id => !activeRuleIds.Contains(id)).ToArray())
        {
            _lastRepeatCycles.Remove(staleId);
        }

        InvalidateScanGate();
    }

    public IReadOnlyList<NoteRecord> GetDue(CountdownSettings settings, DateTimeOffset nowUtc)
    {
        var due = GetDueOccurrences(settings, nowUtc);
        if (due.Count == 0)
        {
            return Array.Empty<NoteRecord>();
        }

        return due
            .Where(static occurrence => occurrence.Source is NoteReminderSource)
            .Select(static occurrence => ((NoteReminderSource)occurrence.Source).Note)
            .ToArray();
    }

    public IReadOnlyList<DueReminderOccurrence> GetDueOccurrences(
        CountdownSettings settings,
        DateTimeOffset nowUtc)
    {
        var normalizedNowUtc = nowUtc.ToUniversalTime();
        if (_scanGate is { } gate &&
            ReferenceEquals(gate.Settings, settings) &&
            gate.MutationVersion == _mutationVersion &&
            normalizedNowUtc >= gate.ValidFromInclusiveUtc &&
            (gate.NextCheckAtUtc is null || normalizedNowUtc < gate.NextCheckAtUtc.Value))
        {
            return gate.DueOccurrences;
        }

        var due = new List<DueReminderOccurrence>();
        var liveRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canGate = true;
        DateTimeOffset? nextCheckAtUtc = null;

        void TrackNextCheck(DateTimeOffset? candidateUtc)
        {
            if (candidateUtc is not { } candidate)
            {
                return;
            }

            var normalizedCandidate = candidate.ToUniversalTime();
            if (nextCheckAtUtc is null || normalizedCandidate < nextCheckAtUtc.Value)
            {
                nextCheckAtUtc = normalizedCandidate;
            }
        }

        foreach (var source in GetActiveSources(settings))
        {
            foreach (var rule in source.ReminderRules)
            {
                if (TryGetUnresolvedOccurrence(settings, rule, normalizedNowUtc, out var unresolvedScheduledUtc))
                {
                    due.Add(new DueReminderOccurrence(source, rule, unresolvedScheduledUtc));
                    continue;
                }

                if (TryGetActiveSnoozeUntilUtc(settings, rule, normalizedNowUtc, out var snoozeUntilUtc))
                {
                    TrackNextCheck(snoozeUntilUtc);
                    continue;
                }

                if (TryGetFutureUnresolvedOccurrenceUtc(settings, rule, normalizedNowUtc, out var futureUnresolvedUtc))
                {
                    TrackNextCheck(futureUnresolvedUtc);
                }

                if (rule is AbsoluteOnceReminderRule once)
                {
                    if (!WasHandled(settings, once))
                    {
                        if (normalizedNowUtc >= once.ScheduledUtc)
                        {
                            due.Add(new DueReminderOccurrence(source, once, once.ScheduledUtc));
                        }
                        else
                        {
                            TrackNextCheck(once.ScheduledUtc);
                        }
                    }

                    continue;
                }

                if (rule is CountdownRelativeReminderRule relative)
                {
                    var scheduledUtc = ReminderSchedule.GetScheduledUtc(
                        relative,
                        normalizedNowUtc,
                        source.CountdownTargetUtc);
                    if (!WasHandled(settings, relative, scheduledUtc))
                    {
                        if (normalizedNowUtc >= scheduledUtc)
                        {
                            due.Add(new DueReminderOccurrence(source, relative, scheduledUtc));
                        }
                        else
                        {
                            TrackNextCheck(scheduledUtc);
                        }
                    }

                    continue;
                }

                if (rule is CalendarReminderRule calendar)
                {
                    settings.ReminderStates.TryGetValue(calendar.Id, out var state);
                    DateTimeOffset? scheduledUtc;
                    try
                    {
                        scheduledUtc = _getLatestCalendarOccurrenceUtc(
                            calendar,
                            state?.HandledThroughUtc,
                            normalizedNowUtc);
                    }
                    catch (SettingsValidationException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        throw new SettingsValidationException(
                            "Ical.Net 无法计算日历提醒 occurrence。",
                            exception);
                    }

                    if (scheduledUtc is not null)
                    {
                        due.Add(new DueReminderOccurrence(source, calendar, scheduledUtc.Value));
                    }

                    if (_getNextCalendarOccurrenceUtc is null)
                    {
                        canGate = false;
                    }
                    else
                    {
                        var afterExclusiveUtc = scheduledUtc is null &&
                                                state?.HandledThroughUtc is { } handledThroughUtc &&
                                                handledThroughUtc > normalizedNowUtc
                            ? handledThroughUtc
                            : normalizedNowUtc;
                        DateTimeOffset? nextCalendarUtc;
                        try
                        {
                            nextCalendarUtc = _getNextCalendarOccurrenceUtc(calendar, afterExclusiveUtc);
                        }
                        catch (SettingsValidationException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            throw new SettingsValidationException(
                                "Ical.Net 无法计算日历提醒 occurrence。",
                                exception);
                        }

                        TrackNextCheck(nextCalendarUtc);
                    }

                    continue;
                }

                if (rule is not FixedIntervalReminderRule repeating)
                {
                    continue;
                }

                liveRuleIds.Add(repeating.Id);
                if (!TryGetRepeatCycle(repeating, normalizedNowUtc, out var cycle))
                {
                    var handledOrTrackedCycle = _lastRepeatCycles.TryGetValue(repeating.Id, out var trackedCycle)
                        ? trackedCycle
                        : TryGetHandledRepeatCycle(settings, repeating);
                    TrackNextCheck(TryAddIntervals(
                        repeating.FirstDueUtc,
                        handledOrTrackedCycle,
                        repeating.Interval.Minutes));
                    continue;
                }

                if (!_lastRepeatCycles.TryGetValue(repeating.Id, out var lastCycle))
                {
                    lastCycle = TryGetHandledRepeatCycle(settings, repeating);
                    _lastRepeatCycles[repeating.Id] = lastCycle;
                }

                if (cycle > lastCycle)
                {
                    due.Add(new DueReminderOccurrence(
                        source,
                        repeating,
                        ReminderSchedule.GetScheduledUtc(repeating, normalizedNowUtc)));
                    TrackNextCheck(TryAddIntervals(
                        repeating.FirstDueUtc,
                        cycle,
                        repeating.Interval.Minutes));
                }
                else
                {
                    TrackNextCheck(TryAddIntervals(
                        repeating.FirstDueUtc,
                        lastCycle,
                        repeating.Interval.Minutes));
                }
            }
        }

        if (_lastRepeatCycles.Count > 0)
        {
            foreach (var staleId in _lastRepeatCycles.Keys
                         .Where(id => !liveRuleIds.Contains(id)).ToArray())
            {
                _lastRepeatCycles.Remove(staleId);
            }
        }

        var result = due.Count == 0
            ? EmptyDueOccurrences
            : Array.AsReadOnly(due.ToArray());
        _scanGate = canGate
            ? new ScanGate(settings, _mutationVersion, normalizedNowUtc, nextCheckAtUtc, result)
            : null;
        return result;
    }

    private void InvalidateScanGate()
    {
        _mutationVersion++;
        _scanGate = null;
    }

    private static bool TryGetUnresolvedOccurrence(
        CountdownSettings settings,
        ReminderRule rule,
        DateTimeOffset nowUtc,
        out DateTimeOffset scheduledUtc)
    {
        scheduledUtc = default;
        if (!TryGetUndisposedDeliveryUtc(settings, rule, out var state, out var deliveredUtc))
        {
            return false;
        }

        if (nowUtc < deliveredUtc)
        {
            return false;
        }

        if (state.ActiveSnooze is { } snooze && nowUtc < snooze.SnoozeUntilUtc)
        {
            return false;
        }

        scheduledUtc = deliveredUtc;
        return true;
    }

    private static bool TryGetFutureUnresolvedOccurrenceUtc(
        CountdownSettings settings,
        ReminderRule rule,
        DateTimeOffset nowUtc,
        out DateTimeOffset scheduledUtc)
    {
        scheduledUtc = default;
        if (!TryGetUndisposedDeliveryUtc(settings, rule, out _, out var deliveredUtc) ||
            deliveredUtc <= nowUtc)
        {
            return false;
        }

        scheduledUtc = deliveredUtc;
        return true;
    }

    private static bool TryGetUndisposedDeliveryUtc(
        CountdownSettings settings,
        ReminderRule rule,
        out ReminderRuleState state,
        out DateTimeOffset deliveredUtc)
    {
        deliveredUtc = default;
        if (!settings.ReminderStates.TryGetValue(rule.Id, out state!) ||
            !ReminderOccurrenceKey.TryParse(state.LastDeliveredOccurrenceKey, out var delivered) ||
            !string.Equals(delivered.RuleId, rule.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ReminderOccurrenceKey.TryParse(state.LastDispositionOccurrenceKey, out var disposition) &&
            string.Equals(disposition.RuleId, rule.Id, StringComparison.OrdinalIgnoreCase) &&
            disposition.ScheduledUtc >= delivered.ScheduledUtc)
        {
            return false;
        }

        deliveredUtc = delivered.ScheduledUtc;
        return true;
    }

    private static bool TryGetActiveSnoozeUntilUtc(
        CountdownSettings settings,
        ReminderRule rule,
        DateTimeOffset nowUtc,
        out DateTimeOffset snoozeUntilUtc)
    {
        if (settings.ReminderStates.TryGetValue(rule.Id, out var state) &&
            state.ActiveSnooze is { } snooze &&
            nowUtc < snooze.SnoozeUntilUtc)
        {
            snoozeUntilUtc = snooze.SnoozeUntilUtc;
            return true;
        }

        snoozeUntilUtc = default;
        return false;
    }

    public void ConfirmDisplayed(NoteRecord note, DateTimeOffset displayedAtUtc)
    {
        var rule = note.ReminderRules.FirstOrDefault();
        if (rule is null)
        {
            return;
        }

        ConfirmDisplayed(
            note,
            rule.Id,
            ReminderSchedule.GetScheduledUtc(rule, displayedAtUtc));
    }

    public void ConfirmDisplayed(
        ReminderSource source,
        string ruleId,
        DateTimeOffset scheduledUtc)
    {
        if (source is NoteReminderSource { Note.IsCompleted: true })
        {
            return;
        }

        var rule = source.ReminderRules.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.OrdinalIgnoreCase));
        if (rule is FixedIntervalReminderRule repeating &&
            TryGetRepeatCycle(repeating, scheduledUtc, out var cycle))
        {
            if (!_lastRepeatCycles.TryGetValue(repeating.Id, out var lastCycle) || cycle > lastCycle)
            {
                _lastRepeatCycles[repeating.Id] = cycle;
                InvalidateScanGate();
            }
        }
    }

    public void ConfirmDisplayed(
        NoteRecord note,
        string ruleId,
        DateTimeOffset scheduledUtc)
    {
        if (note.IsCompleted)
        {
            return;
        }

        var rule = note.ReminderRules.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.OrdinalIgnoreCase));
        if (rule is FixedIntervalReminderRule repeating &&
            TryGetRepeatCycle(repeating, scheduledUtc, out var cycle))
        {
            if (!_lastRepeatCycles.TryGetValue(repeating.Id, out var lastCycle) || cycle > lastCycle)
            {
                _lastRepeatCycles[repeating.Id] = cycle;
                InvalidateScanGate();
            }
        }
    }

    private static DateTimeOffset? TryAddIntervals(
        DateTimeOffset firstDueUtc,
        long intervalCount,
        int intervalMinutes)
    {
        if (intervalCount <= 0)
        {
            return firstDueUtc.ToUniversalTime();
        }

        var normalizedFirstDueUtc = firstDueUtc.ToUniversalTime();
        var intervalTicks = TimeSpan.FromMinutes(intervalMinutes).Ticks;
        var maxIntervals = (DateTimeOffset.MaxValue.UtcTicks - normalizedFirstDueUtc.UtcTicks) / intervalTicks;
        if (intervalCount > maxIntervals)
        {
            return null;
        }

        return normalizedFirstDueUtc.AddTicks(intervalCount * intervalTicks);
    }

    private sealed record ScanGate(
        CountdownSettings Settings,
        long MutationVersion,
        DateTimeOffset ValidFromInclusiveUtc,
        DateTimeOffset? NextCheckAtUtc,
        IReadOnlyList<DueReminderOccurrence> DueOccurrences);

    private static bool TryGetRepeatCycle(
        FixedIntervalReminderRule repeating,
        DateTimeOffset nowUtc,
        out long cycle)
    {
        cycle = 0;
        if (nowUtc < repeating.FirstDueUtc)
        {
            return false;
        }

        cycle = (long)Math.Floor((nowUtc - repeating.FirstDueUtc).TotalMinutes /
            repeating.Interval.Minutes) + 1;
        return true;
    }

    private static long TryGetHandledRepeatCycle(
        CountdownSettings settings,
        FixedIntervalReminderRule rule)
    {
        if (!settings.ReminderStates.TryGetValue(rule.Id, out var state) ||
            state.HandledThroughUtc is not { } handledThroughUtc ||
            handledThroughUtc < rule.FirstDueUtc)
        {
            return 0;
        }

        return (handledThroughUtc.Ticks - rule.FirstDueUtc.Ticks) /
            TimeSpan.FromMinutes(rule.Interval.Minutes).Ticks + 1;
    }

    private static bool WasHandled(
        CountdownSettings settings,
        AbsoluteOnceReminderRule rule) =>
        settings.ReminderStates.TryGetValue(rule.Id, out var state) &&
        state.HandledThroughUtc is { } handledThroughUtc &&
        handledThroughUtc >= rule.ScheduledUtc;

    private static bool WasHandled(
        CountdownSettings settings,
        CountdownRelativeReminderRule rule,
        DateTimeOffset scheduledUtc) =>
        settings.ReminderStates.TryGetValue(rule.Id, out var state) &&
        state.HandledThroughUtc == scheduledUtc.ToUniversalTime();

    private static IEnumerable<ReminderSource> GetActiveSources(CountdownSettings settings)
    {
        foreach (var countdown in settings.Countdowns)
        {
            yield return new CountdownReminderSource(countdown);
        }

        foreach (var note in settings.Notes.Where(static note => !note.IsCompleted))
        {
            yield return new NoteReminderSource(note);
        }
    }
}

public enum ReminderSourceKind
{
    Countdown,
    Note
}

public abstract record ReminderSource
{
    public abstract ReminderSourceKind Kind { get; }
    public abstract string Id { get; }
    public abstract string DisplayText { get; }
    public abstract IReadOnlyList<ReminderRule> ReminderRules { get; }
    public virtual DateTimeOffset? CountdownTargetUtc => null;
}

public sealed record CountdownReminderSource(CountdownRecord Countdown) : ReminderSource
{
    public override ReminderSourceKind Kind => ReminderSourceKind.Countdown;
    public override string Id => Countdown.Id;
    public override string DisplayText => Countdown.Name;
    public override IReadOnlyList<ReminderRule> ReminderRules => Countdown.ReminderRules;
    public override DateTimeOffset? CountdownTargetUtc => Countdown.TargetUtc;
}

public sealed record NoteReminderSource(NoteRecord Note) : ReminderSource
{
    public override ReminderSourceKind Kind => ReminderSourceKind.Note;
    public override string Id => Note.Id;
    public override string DisplayText => Note.Text;
    public override IReadOnlyList<ReminderRule> ReminderRules => Note.ReminderRules;
}

public sealed record DueReminderOccurrence(
    ReminderSource Source,
    ReminderRule Rule,
    DateTimeOffset ScheduledUtc)
{
    public DueReminderOccurrence(NoteRecord note, ReminderRule rule, DateTimeOffset scheduledUtc)
        : this(new NoteReminderSource(note), rule, scheduledUtc)
    {
    }

    public ReminderSourceKind SourceKind => Source.Kind;
    public string SourceId => Source.Id;
    public NoteRecord Note => ((NoteReminderSource)Source).Note;
}

public enum ReminderSnoozeOption
{
    TenMinutes,
    ThirtyMinutes,
    SixtyMinutes,
    TomorrowAtNine,
    Custom
}

public enum ReminderSnoozeErrorCode
{
    None,
    CustomSnoozeNotInFuture
}
