namespace MinminCountdown.Core;

using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

public static class CalendarOccurrenceCalculator
{
    private static readonly TimeZoneInfo BeijingZone =
        TimeZoneInfo.FindSystemTimeZoneById(SettingsSchema.ChinaStandardTimeZoneId);

    public static DateTimeOffset? GetLatestOccurrenceUtc(
        CalendarReminderRule rule,
        DateTimeOffset? afterExclusiveUtc,
        DateTimeOffset atOrBeforeUtc)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ValidateRule(rule);

        var normalizedEndUtc = atOrBeforeUtc.ToUniversalTime();
        var normalizedAfterUtc = afterExclusiveUtc?.ToUniversalTime();
        if (normalizedAfterUtc is not null && normalizedAfterUtc >= normalizedEndUtc)
        {
            return null;
        }

        try
        {
            var calendarEvent = CreateCalendarEvent(rule);
            var startLocal = rule.StartDate.ToDateTime(rule.LocalTime, DateTimeKind.Unspecified);
            var searchLocal = normalizedAfterUtc is null
                ? startLocal
                : TimeZoneInfo.ConvertTime(normalizedAfterUtc.Value, BeijingZone).DateTime.AddSeconds(-1);
            if (searchLocal < startLocal)
            {
                searchLocal = startLocal;
            }

            var inclusiveEndLocal = TimeZoneInfo.ConvertTime(normalizedEndUtc, BeijingZone).DateTime;
            var exclusiveEnd = inclusiveEndLocal >= DateTime.MaxValue.AddSeconds(-1)
                ? DateTime.MaxValue
                : inclusiveEndLocal.AddSeconds(1);
            DateTimeOffset? latest = null;
            foreach (var occurrence in calendarEvent
                         .GetOccurrences(ToCalDateTime(searchLocal))
                         .TakeWhileBefore(ToCalDateTime(exclusiveEnd)))
            {
                var local = DateTime.SpecifyKind(occurrence.Period.StartTime.Value, DateTimeKind.Unspecified);
                if (local < startLocal || !MatchesTypedSelector(rule, DateOnly.FromDateTime(local)))
                {
                    continue;
                }

                var occurrenceUtc = new DateTimeOffset(
                    TimeZoneInfo.ConvertTimeToUtc(local, BeijingZone),
                    TimeSpan.Zero);
                if (occurrenceUtc > normalizedEndUtc ||
                    normalizedAfterUtc is not null && occurrenceUtc <= normalizedAfterUtc)
                {
                    continue;
                }

                if (latest is null || occurrenceUtc > latest)
                {
                    latest = occurrenceUtc;
                }
            }

            return latest;
        }
        catch (SettingsValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SettingsValidationException("Ical.Net 无法计算日历提醒 occurrence。", exception);
        }
    }

    public static bool IsOccurrence(CalendarReminderRule rule, DateTimeOffset scheduledUtc)
    {
        var normalized = scheduledUtc.ToUniversalTime();
        var previous = normalized == DateTimeOffset.MinValue
            ? (DateTimeOffset?)null
            : normalized.AddSeconds(-1);
        return GetLatestOccurrenceUtc(rule, previous, normalized) == normalized;
    }

    public static DateTimeOffset? GetNextOccurrenceUtc(
        CalendarReminderRule rule,
        DateTimeOffset afterExclusiveUtc)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ValidateRule(rule);

        try
        {
            var calendarEvent = CreateCalendarEvent(rule);
            var startLocal = rule.StartDate.ToDateTime(rule.LocalTime, DateTimeKind.Unspecified);
            var normalizedAfterUtc = afterExclusiveUtc.ToUniversalTime();
            var searchLocal = TimeZoneInfo.ConvertTime(normalizedAfterUtc, BeijingZone).DateTime;
            if (searchLocal < startLocal)
            {
                searchLocal = startLocal;
            }

            var exclusiveEnd = new CalDateTime(9999, 12, 31, 23, 59, 59);
            foreach (var occurrence in calendarEvent
                         .GetOccurrences(ToCalDateTime(searchLocal))
                         .TakeWhileBefore(exclusiveEnd))
            {
                var local = DateTime.SpecifyKind(occurrence.Period.StartTime.Value, DateTimeKind.Unspecified);
                if (local < startLocal || !MatchesTypedSelector(rule, DateOnly.FromDateTime(local)))
                {
                    continue;
                }

                var occurrenceUtc = new DateTimeOffset(
                    TimeZoneInfo.ConvertTimeToUtc(local, BeijingZone),
                    TimeSpan.Zero);
                if (occurrenceUtc > normalizedAfterUtc)
                {
                    return occurrenceUtc;
                }
            }

            return null;
        }
        catch (SettingsValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SettingsValidationException("Ical.Net 无法计算日历提醒 occurrence。", exception);
        }
    }

    private static CalendarEvent CreateCalendarEvent(CalendarReminderRule rule)
    {
        var pattern = new RecurrencePattern
        {
            Frequency = rule.Frequency switch
            {
                CalendarFrequency.Daily => FrequencyType.Daily,
                CalendarFrequency.Weekly => FrequencyType.Weekly,
                CalendarFrequency.Monthly => FrequencyType.Monthly,
                CalendarFrequency.Yearly => FrequencyType.Yearly,
                _ => throw new SettingsValidationException("设置中包含不支持的日历提醒频率。")
            },
            Interval = rule.Interval
        };

        switch (rule.Selector)
        {
            case DailyCalendarSelector:
                break;
            case WeeklyCalendarSelector weekly:
                pattern.ByDay = weekly.Weekdays.Select(static day => new WeekDay(day)).ToList();
                break;
            case MonthlyCalendarSelector monthly:
                pattern.ByMonthDay = [monthly.LastDay ? -1 : monthly.Day!.Value];
                break;
            case YearlyCalendarSelector yearly:
                pattern.ByMonth = [yearly.Month];
                pattern.ByMonthDay = [yearly.LastDay ? -1 : yearly.Day!.Value];
                break;
            default:
                throw new SettingsValidationException("日历提醒选择器与频率不匹配。");
        }

        var start = rule.StartDate.ToDateTime(rule.LocalTime, DateTimeKind.Unspecified);
        return new CalendarEvent
        {
            DtStart = ToCalDateTime(start),
            RecurrenceRule = pattern
        };
    }

    private static CalDateTime ToCalDateTime(DateTime value) => new(
        value.Year,
        value.Month,
        value.Day,
        value.Hour,
        value.Minute,
        value.Second);

    private static bool MatchesTypedSelector(CalendarReminderRule rule, DateOnly date) => rule.Selector switch
    {
        DailyCalendarSelector => true,
        WeeklyCalendarSelector weekly => weekly.Weekdays.Contains(date.DayOfWeek),
        MonthlyCalendarSelector { LastDay: true } => date.Day == DateTime.DaysInMonth(date.Year, date.Month),
        MonthlyCalendarSelector monthly => date.Day == monthly.Day,
        YearlyCalendarSelector { LastDay: true } yearly =>
            date.Month == yearly.Month && date.Day == DateTime.DaysInMonth(date.Year, date.Month),
        YearlyCalendarSelector yearly => date.Month == yearly.Month && date.Day == yearly.Day,
        _ => false
    };

    private static void ValidateRule(CalendarReminderRule rule)
    {
        if (rule.Interval is < 1 or > 999 ||
            rule.LocalTime.Second != 0 ||
            rule.LocalTime.Millisecond != 0)
        {
            throw new SettingsValidationException("日历提醒规则无效。");
        }

        var valid = (rule.Frequency, rule.Selector) switch
        {
            (CalendarFrequency.Daily, DailyCalendarSelector) => true,
            (CalendarFrequency.Weekly, WeeklyCalendarSelector weekly) =>
                weekly.Weekdays.Count > 0 && weekly.Weekdays.Distinct().Count() == weekly.Weekdays.Count,
            (CalendarFrequency.Monthly, MonthlyCalendarSelector monthly) =>
                monthly.LastDay && monthly.Day is null || !monthly.LastDay && monthly.Day is >= 1 and <= 31,
            (CalendarFrequency.Yearly, YearlyCalendarSelector yearly) =>
                yearly.Month is >= 1 and <= 12 &&
                (yearly.LastDay && yearly.Day is null || !yearly.LastDay && yearly.Day is >= 1 and <= 31),
            _ => false
        };
        if (!valid)
        {
            throw new SettingsValidationException("日历提醒选择器与频率不匹配。");
        }
    }
}
