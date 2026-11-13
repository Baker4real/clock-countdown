using System.Globalization;
using System.Windows;

namespace MinminCountdown.WindowVerification;

// #248：空态几何测量（列表视口坐标）。判定：没有可滚动范围与滚动条；空态块完整落在列表视口内，并在应居中的区域里
// 水平、垂直居中（±2 DIP）；滚进管理窗口可视区后完整可见，列表自身不被带着滚动。
internal sealed record MainEmptyStateGeometry(
    string Label,
    bool Visible,
    string Message,
    double ViewportWidth,
    double ViewportHeight,
    double ExtentHeight,
    double ScrollableHeight,
    bool ScrollBarShown,
    Rect Block,
    Rect CenteringArea,
    Rect BlockInWindow,
    double ListOffset,
    Size WindowViewport)
{
    private const double Tolerance = 0.5;
    private const double CenteringTolerance = 2;

    public IReadOnlyList<string> Failures()
    {
        if (!Visible)
        {
            return [$"{Label}：空态未显示（{Message}）"];
        }

        var failures = new List<string>();
        if (ScrollableHeight > Tolerance || ScrollBarShown || ExtentHeight > ViewportHeight + Tolerance)
        {
            failures.Add("出现虚假滚动范围");
        }

        if (!Inside(Block, ViewportWidth, ViewportHeight))
        {
            failures.Add("空态块越出列表视口");
        }

        if (Math.Abs(CenterX(Block) - CenterX(CenteringArea)) > CenteringTolerance ||
            Math.Abs(CenterY(Block) - CenterY(CenteringArea)) > CenteringTolerance)
        {
            failures.Add("空态块未在可用区域居中");
        }

        if (!Inside(BlockInWindow, WindowViewport.Width, WindowViewport.Height) || ListOffset > Tolerance)
        {
            failures.Add("滚进管理窗口可视区后仍不完整可见");
        }

        return failures.Count == 0 ? [] : [$"{Label}：{string.Join("、", failures)}（{Describe()}）"];
    }

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"「{Message}」，视口 {ViewportWidth:0.##}×{ViewportHeight:0.##}，内容高 {ExtentHeight:0.##}，" +
        $"可滚 {ScrollableHeight:0.##}，滚动条{(ScrollBarShown ? "显示" : "隐藏")}，空态块 {Format(Block)}，" +
        $"居中区域 {Format(CenteringArea)}，窗口内 {Format(BlockInWindow)}，" +
        $"窗口视口 {WindowViewport.Width:0.##}×{WindowViewport.Height:0.##}，列表偏移 {ListOffset:0.##}");

    private static bool Inside(Rect bounds, double width, double height) =>
        !bounds.IsEmpty &&
        bounds.Left >= -Tolerance &&
        bounds.Top >= -Tolerance &&
        bounds.Right <= width + Tolerance &&
        bounds.Bottom <= height + Tolerance;

    private static double CenterX(Rect bounds) => bounds.IsEmpty ? double.NaN : bounds.X + bounds.Width / 2;

    private static double CenterY(Rect bounds) => bounds.IsEmpty ? double.NaN : bounds.Y + bounds.Height / 2;

    private static string Format(Rect bounds) => bounds.IsEmpty
        ? "无"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"({bounds.X:0.##},{bounds.Y:0.##},{bounds.Width:0.##}×{bounds.Height:0.##})");
}

// #248：空态当前显示的内容与动作。MessageWraps 承接原 CSharp 的 XAML 正则，改为真实控件行为。
internal sealed record MainEmptyStateView(
    bool Visible,
    string? Message,
    string? Hint,
    bool MascotVisible,
    bool GuideVisible,
    string GuideLabel,
    string GuideName,
    bool GuideRestsClear,
    bool ResetVisible,
    int AddLabeledButtons,
    bool FormAddEntryVisible,
    bool MessageWraps)
{
    public bool ShowsFirstUse(string message, string hint) =>
        Visible &&
        Message == message &&
        Hint == hint &&
        MessageWraps &&
        (MascotVisible || ApplicationTheme.HighContrastEnabled) &&
        GuideVisible &&
        !ResetVisible;

    // 轻量引导：常态无底无边，可见文字与可访问名都不写「添加」，可访问名包含可见文字。
    public bool GuideIsLightweight =>
        GuideRestsClear &&
        GuideLabel.Length > 0 &&
        !GuideLabel.Contains("添加", StringComparison.Ordinal) &&
        !GuideName.Contains("添加", StringComparison.Ordinal) &&
        GuideName.Contains(GuideLabel, StringComparison.Ordinal);

    public bool ShowsOnlyReset(string message) =>
        Visible && Message == message && Hint is null && ResetVisible && !GuideVisible;

    public bool ExplainsEmptySegment(string segment, string? otherSegment) =>
        Visible &&
        Message is { } message &&
        message.Contains(segment, StringComparison.Ordinal) &&
        !message.Contains("筛选", StringComparison.Ordinal) &&
        Hint is { } hint &&
        (otherSegment is null || hint.Contains(otherSegment, StringComparison.Ordinal)) &&
        !ResetVisible &&
        !GuideVisible;

    public string Describe() =>
        $"{(Visible ? "显示" : "隐藏")}，文案「{Message ?? "无"}」（换行 {MessageWraps}），说明「{Hint ?? "无"}」，角色{(MascotVisible ? "显示" : "隐藏")}，" +
        $"引导{(GuideVisible ? "显示" : "隐藏")}「{GuideLabel}」（名「{GuideName}」，常态{(GuideRestsClear ? "无底无边" : "有底或有边")}），" +
        $"重置{(ResetVisible ? "显示" : "隐藏")}，空态里写着「添加」的按钮 {AddLabeledButtons} 个，" +
        $"表单添加入口{(FormAddEntryVisible ? "可用" : "不可用")}";
}

// #248：三种空态的文案与动作是否各自正确。
internal sealed record MainEmptyStateGuidanceProbe(
    bool CountdownFirstUseGuides,
    bool NoteFirstUseGuides,
    bool GuidesAreLightweight,
    bool GuidesFocusEditorsAndKeepDrafts,
    bool SinglePrimaryAddEntry,
    bool FilterNoResultsOfferOnlyReset,
    bool ResetRestoresResults,
    bool SegmentEmptyExplainsOtherSegment,
    IReadOnlyList<string> Details)
{
    public bool AllPassed =>
        CountdownFirstUseGuides &&
        NoteFirstUseGuides &&
        GuidesAreLightweight &&
        GuidesFocusEditorsAndKeepDrafts &&
        SinglePrimaryAddEntry &&
        FilterNoResultsOfferOnlyReset &&
        ResetRestoresResults &&
        SegmentEmptyExplainsOtherSegment;

    public string Explain(string meaning)
    {
        if (AllPassed)
        {
            return meaning;
        }

        var failed = new (string Name, bool Passed)[]
            {
                (nameof(CountdownFirstUseGuides), CountdownFirstUseGuides),
                (nameof(NoteFirstUseGuides), NoteFirstUseGuides),
                (nameof(GuidesAreLightweight), GuidesAreLightweight),
                (nameof(GuidesFocusEditorsAndKeepDrafts), GuidesFocusEditorsAndKeepDrafts),
                (nameof(SinglePrimaryAddEntry), SinglePrimaryAddEntry),
                (nameof(FilterNoResultsOfferOnlyReset), FilterNoResultsOfferOnlyReset),
                (nameof(ResetRestoresResults), ResetRestoresResults),
                (nameof(SegmentEmptyExplainsOtherSegment), SegmentEmptyExplainsOtherSegment)
            }
            .Where(item => !item.Passed)
            .Select(item => item.Name);
        return $"{meaning}；未通过：{string.Join('/', failed)}；实测：{string.Join("；", Details)}";
    }
}

// #248：空态几何汇总。全部窗口档都量满且没有失败项才算通过；失败时列出前 8 项实测。
internal sealed record MainEmptyStateViewportProbe(int Measured, int Expected, IReadOnlyList<string> Failures)
{
    public bool Passed => Measured == Expected && Failures.Count == 0;

    public string Explain(string meaning)
    {
        if (Passed)
        {
            return meaning;
        }

        var shown = string.Join("；", Failures.Take(8));
        var remaining = Failures.Count > 8 ? $"；另有 {Failures.Count - 8} 项" : string.Empty;
        return $"{meaning}；测量 {Measured}/{Expected} 次，未通过 {Failures.Count} 项：{shown}{remaining}";
    }
}
