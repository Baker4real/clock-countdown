using System.Globalization;
using MinminCountdown.Core;

namespace MinminCountdown;

internal enum ReminderEditingMode
{
    None,
    Once,
    Repeating
}

internal enum ReminderIntervalSelection
{
    FifteenMinutes,
    ThirtyMinutes,
    SixtyMinutes,
    Custom
}

internal sealed record ReminderEditingRequest(
    ReminderEditingMode Mode,
    DateTime? FirstDueDate,
    int FirstDueHour,
    int FirstDueMinute,
    ReminderIntervalSelection IntervalSelection,
    string CustomIntervalMinutes,
    DateTimeOffset NowUtc,
    NoteReminder? ExistingReminder,
    bool ScheduleDirty);

internal sealed record ReminderEditingResult(
    bool Succeeded,
    NoteReminder Reminder,
    string Error);

internal sealed record ReminderEditingDefaultResult(
    bool Succeeded,
    DateTimeOffset? FirstDueBeijing,
    string Error);

internal sealed class ReminderEditing
{
    private const string InvalidIntervalError = "自定义提醒必须是 1 到 1440 分钟。";

    public ReminderEditingResult Build(ReminderEditingRequest request)
    {
        if (request.Mode == ReminderEditingMode.None)
        {
            return Success(NoReminder.Instance);
        }

        int? intervalMinutes = null;
        if (request.Mode == ReminderEditingMode.Repeating)
        {
            if (!TryGetIntervalMinutes(
                    request.IntervalSelection,
                    request.CustomIntervalMinutes,
                    out var parsedInterval))
            {
                return Failure(InvalidIntervalError);
            }

            intervalMinutes = parsedInterval;
        }
        else if (request.Mode != ReminderEditingMode.Once)
        {
            return Failure("请选择有效的提醒模式。");
        }

        if (!request.ScheduleDirty)
        {
            if (request.Mode == ReminderEditingMode.Once &&
                request.ExistingReminder is OnceReminder existingOnce)
            {
                return Success(existingOnce);
            }

            if (request.Mode == ReminderEditingMode.Repeating &&
                request.ExistingReminder is RepeatingReminder existingRepeating &&
                existingRepeating.Interval.Minutes == intervalMinutes)
            {
                return Success(existingRepeating);
            }
        }

        if (!ReminderSchedule.TryCreateFirstDueUtc(
                request.FirstDueDate,
                request.FirstDueHour,
                request.FirstDueMinute,
                request.NowUtc,
                out var firstDueUtc,
                out var error))
        {
            return Failure(error);
        }

        NoteReminder reminder = request.Mode == ReminderEditingMode.Once
            ? ReminderSchedule.CreateOnce(firstDueUtc!.Value)
            : ReminderSchedule.CreateRepeating(firstDueUtc!.Value, intervalMinutes!.Value);
        return Success(reminder);
    }

    public ReminderEditingDefaultResult GetDefaultFirstDueBeijing(
        ReminderEditingMode mode,
        ReminderIntervalSelection intervalSelection,
        string customIntervalMinutes,
        DateTimeOffset nowUtc)
    {
        if (mode == ReminderEditingMode.None)
        {
            return new ReminderEditingDefaultResult(true, null, string.Empty);
        }

        var intervalMinutes = 15;
        if (mode == ReminderEditingMode.Repeating)
        {
            if (!TryGetIntervalMinutes(intervalSelection, customIntervalMinutes, out intervalMinutes))
            {
                return new ReminderEditingDefaultResult(false, null, InvalidIntervalError);
            }
        }
        else if (mode != ReminderEditingMode.Once)
        {
            return new ReminderEditingDefaultResult(false, null, "请选择有效的提醒模式。");
        }

        var beijingNow = ChinaStandardTime.ToBeijingTime(nowUtc);
        return new ReminderEditingDefaultResult(
            true,
            ReminderSchedule.GetDefaultFirstDueBeijing(beijingNow, intervalMinutes),
            string.Empty);
    }

    private static bool TryGetIntervalMinutes(
        ReminderIntervalSelection selection,
        string customIntervalMinutes,
        out int intervalMinutes)
    {
        intervalMinutes = selection switch
        {
            ReminderIntervalSelection.FifteenMinutes => 15,
            ReminderIntervalSelection.ThirtyMinutes => 30,
            ReminderIntervalSelection.SixtyMinutes => 60,
            _ => -1
        };
        if (intervalMinutes != -1)
        {
            return true;
        }

        return selection == ReminderIntervalSelection.Custom &&
            int.TryParse(
                customIntervalMinutes,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out intervalMinutes) &&
            intervalMinutes is >= 1 and <= 1440;
    }

    private static ReminderEditingResult Success(NoteReminder reminder) =>
        new(true, reminder, string.Empty);

    private static ReminderEditingResult Failure(string error) =>
        new(false, NoReminder.Instance, error);
}
