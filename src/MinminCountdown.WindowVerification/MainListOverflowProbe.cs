namespace MinminCountdown.WindowVerification;

// #248：单个列表在真实溢出时的滚动可达性。
internal sealed record MainListOverflowMeasurement(
    bool ScrollBarShown,
    bool WheelScrolls,
    bool ThumbDragScrolls,
    bool PageDownScrolls,
    bool PageDownReachesEnd,
    bool LastRowActionsVisibleAtEnd,
    bool EveryRowActionReachable);

// #248：两个列表（可跨多个窗口尺寸）真实溢出时的滚动可达性汇总。
internal sealed record MainListOverflowProbe(
    bool ScrollBarsShown,
    bool WheelScrolls,
    bool ThumbDragScrolls,
    bool PageDownScrolls,
    bool PageDownReachesEnd,
    bool LastRowActionsVisibleAtEnd,
    bool EveryRowActionReachable,
    IReadOnlyList<string> Details)
{
    public bool AllPassed =>
        ScrollBarsShown &&
        WheelScrolls &&
        ThumbDragScrolls &&
        PageDownScrolls &&
        PageDownReachesEnd &&
        LastRowActionsVisibleAtEnd &&
        EveryRowActionReachable;

    public static MainListOverflowProbe FromMeasurements(
        IReadOnlyList<MainListOverflowMeasurement> lists,
        IReadOnlyList<string> details) =>
        new(
            lists.Count > 0 && lists.All(list => list.ScrollBarShown),
            lists.Count > 0 && lists.All(list => list.WheelScrolls),
            lists.Count > 0 && lists.All(list => list.ThumbDragScrolls),
            lists.Count > 0 && lists.All(list => list.PageDownScrolls),
            lists.Count > 0 && lists.All(list => list.PageDownReachesEnd),
            lists.Count > 0 && lists.All(list => list.LastRowActionsVisibleAtEnd),
            lists.Count > 0 && lists.All(list => list.EveryRowActionReachable),
            details);

    public static MainListOverflowProbe Combine(IReadOnlyList<MainListOverflowProbe> probes) =>
        new(
            probes.Count > 0 && probes.All(probe => probe.ScrollBarsShown),
            probes.Count > 0 && probes.All(probe => probe.WheelScrolls),
            probes.Count > 0 && probes.All(probe => probe.ThumbDragScrolls),
            probes.Count > 0 && probes.All(probe => probe.PageDownScrolls),
            probes.Count > 0 && probes.All(probe => probe.PageDownReachesEnd),
            probes.Count > 0 && probes.All(probe => probe.LastRowActionsVisibleAtEnd),
            probes.Count > 0 && probes.All(probe => probe.EveryRowActionReachable),
            probes.SelectMany(probe => probe.Details).ToArray());

    public static MainListOverflowProbe Failure(string detail) =>
        new(false, false, false, false, false, false, false, [detail]);

    public string Explain(string meaning)
    {
        if (AllPassed)
        {
            return meaning;
        }

        var failed = new (string Name, bool Passed)[]
            {
                (nameof(ScrollBarsShown), ScrollBarsShown),
                (nameof(WheelScrolls), WheelScrolls),
                (nameof(ThumbDragScrolls), ThumbDragScrolls),
                (nameof(PageDownScrolls), PageDownScrolls),
                (nameof(PageDownReachesEnd), PageDownReachesEnd),
                (nameof(LastRowActionsVisibleAtEnd), LastRowActionsVisibleAtEnd),
                (nameof(EveryRowActionReachable), EveryRowActionReachable)
            }
            .Where(item => !item.Passed)
            .Select(item => item.Name);
        return $"{meaning}；未通过：{string.Join('/', failed)}；实测：{string.Join("；", Details)}";
    }
}
