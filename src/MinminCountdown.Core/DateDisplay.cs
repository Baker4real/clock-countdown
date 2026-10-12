using System.Globalization;

namespace MinminCountdown.Core;

/// <summary>面向用户的北京时间显示；不用于设置文件中的日期序列化。</summary>
public static class DateDisplay
{
    public const string FullDateFormat = "yyyy 年 M 月 d 日";
    public const string FullDateBindingFormat = "{0:" + FullDateFormat + "}";

    public static string FullDate(DateOnly date) => date.ToString(FullDateFormat, CultureInfo.InvariantCulture);

    public static string Deadline(DateTimeOffset instant) =>
        ChinaStandardTime.ToBeijingTime(instant).ToString("M 月 d 日 HH:mm", CultureInfo.InvariantCulture);

    public static string NoteGroup(DateOnly? date, DateTimeOffset now)
    {
        if (date is null) return "未设置日期";
        var today = DateOnly.FromDateTime(ChinaStandardTime.ToBeijingTime(now).DateTime);
        var calendarDate = date.Value.Year == today.Year
            ? date.Value.ToString("M 月 d 日", CultureInfo.InvariantCulture)
            : FullDate(date.Value);
        if (date.Value == today) return $"今天 · {calendarDate}";
        if (date.Value.DayNumber == today.DayNumber - 1) return $"昨天 · {calendarDate}";
        var weekday = CultureInfo.GetCultureInfo("zh-CN").DateTimeFormat.GetAbbreviatedDayName(date.Value.DayOfWeek);
        return $"{calendarDate} · {weekday}";
    }

    public static string FullMoment(DateTimeOffset instant, bool includeSeconds = false)
    {
        var beijing = ChinaStandardTime.ToBeijingTime(instant);
        return FullDate(DateOnly.FromDateTime(beijing.DateTime)) + " " +
            beijing.ToString(includeSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
    }
}
