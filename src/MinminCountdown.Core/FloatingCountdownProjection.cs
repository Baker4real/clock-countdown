using System.Globalization;
using System.Text;

namespace MinminCountdown.Core;

// 悬浮窗倒计时页投影（#148/#212/#236）：哪些行出现、谁是唯一主项、用户倒计时计数、空态与渲染签名，
// 都按一份快照和工作日历事实在这里一次算出；窗口只把行画出来。
// 两种“筛选激活”口径是现行行为，集中在这里：任意筛选约束（含便签条件）都让距离下班与下班收尾退出；
// 空态文案与「清除筛选」只看倒计时自己的约束（标签、无标签、倒计时状态）。
public static class FloatingCountdownProjection
{
    public const string WorkdayEndName = "距离下班";

    public static FloatingCountdownView Project(
        DesktopWorkspaceSnapshot snapshot,
        DateTimeOffset nowUtc,
        DateTimeOffset? workdayEndUtc,
        bool workdayComplete)
    {
        var showsWorkday = !snapshot.HasFilterConstraints;
        var showsCompletion = showsWorkday && workdayComplete;
        var sources = new List<(FloatingCountdownRowKind Kind, string Key, string Name, DateTimeOffset TargetUtc)>();
        if (showsWorkday && workdayEndUtc is { } endUtc)
        {
            sources.Add((FloatingCountdownRowKind.WorkdayEnd, "workday-end", WorkdayEndName, endUtc));
        }

        sources.AddRange(snapshot.VisibleCountdowns
            .Where(record => record.TargetUtc > nowUtc)
            .Select(record => (FloatingCountdownRowKind.Countdown, "countdown:" + record.Id, record.Name, record.TargetUtc)));
        var precision = snapshot.Settings.Preferences.DisplayPrecision;
        var rows = sources
            .Select((source, index) => new FloatingCountdownRow(
                source.Kind,
                source.Key,
                source.Name,
                source.TargetUtc,
                CountdownFormatting.FormatRemaining(source.TargetUtc, nowUtc, precision),
                IsPrimary: index == 0 && !showsCompletion))
            .ToArray();
        var empty = rows.Length == 0 && !showsCompletion;
        return new FloatingCountdownView(
            Array.AsReadOnly(rows),
            showsCompletion,
            empty ? CountdownEmptyStateText(snapshot.Filter) : null,
            empty && CountdownConstraintsActive(snapshot.Filter),
            SignatureEntries(rows, showsCompletion, nowUtc));
    }

    public static bool CountdownConstraintsActive(DesktopWorkspaceFilter filter) =>
        filter.SelectedTagIds.Count > 0 ||
        filter.UntaggedOnly ||
        filter.CountdownStatus != CountdownStatusFilter.All;

    public static string CountdownEmptyStateText(DesktopWorkspaceFilter filter) =>
        CountdownConstraintsActive(filter) ? "没有匹配的倒计时。" : "没有未到期的倒计时。";

    // 剩余时间签名只保留结构与单位：数字串折叠为单个 0，位数变化由行内分格重建吸收。
    public static string NormalizeRemainingStructure(string remaining)
    {
        var builder = new StringBuilder(remaining.Length);
        var inDigits = false;
        foreach (var character in remaining)
        {
            if (char.IsDigit(character))
            {
                if (!inDigits)
                {
                    builder.Append('0');
                    inDigits = true;
                }

                continue;
            }

            inDigits = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    // #212：签名只比较语义输入——行身份、标题、目标时刻、剩余时间结构与收尾卡日期；
    // 剩余时间的数值变化不改签名，由就地路径更新数字。
    private static IReadOnlyList<string> SignatureEntries(
        IReadOnlyList<FloatingCountdownRow> rows,
        bool showsCompletion,
        DateTimeOffset nowUtc)
    {
        var entries = new List<string>(rows.Count + 1);
        if (showsCompletion)
        {
            entries.Add("workday-complete:" + ChinaStandardTime.ToBeijingTime(nowUtc)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        entries.AddRange(rows.Select(row => string.Join("\u001e", new[]
        {
            row.Key,
            row.Name,
            row.TargetUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            NormalizeRemainingStructure(row.Remaining)
        })));
        return entries.AsReadOnly();
    }
}

public enum FloatingCountdownRowKind
{
    WorkdayEnd,
    Countdown
}

// Key 在一次投影内唯一，且不会与用户记录 ID 相撞；窗口用它对应行实例与秒位淡入簿记。
public sealed record FloatingCountdownRow(
    FloatingCountdownRowKind Kind,
    string Key,
    string Name,
    DateTimeOffset TargetUtc,
    string Remaining,
    bool IsPrimary);

public sealed record FloatingCountdownView(
    IReadOnlyList<FloatingCountdownRow> Rows,
    bool ShowsWorkdayCompletion,
    string? EmptyStateText,
    bool OffersClearFilter,
    IReadOnlyList<string> SignatureEntries)
{
    // 只含用户倒计时；距离下班与下班收尾都不计入用户数量。
    public IReadOnlyList<string> UserRemainingTexts =>
        Array.AsReadOnly(Rows
            .Where(row => row.Kind == FloatingCountdownRowKind.Countdown)
            .Select(row => row.Remaining)
            .ToArray());
}
