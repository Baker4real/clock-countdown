using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

// #251：管理窗口密度回收与状态/操作上下文的窗口探针。隔离窗口与窗口档由 ValidationExercises.Density 编排，
// 判定写在文件末尾的测量记录上。这里装入长内容，量两张编辑卡的内部留白、编辑卡到列表与列表到状态栏的区块间距、
// 两个列表首屏能给行用的视口，以及便签列表顶部的状态分段、搜索与批量操作条在滚动、搜索、筛选与多选下是否固定
// 可见、互不重叠且不被裁切。只测量；revealed 在每个状态摆好后调用（实拍用）。
public sealed partial class MainWindow
{
    private const string DensityProbeTagName = "密度探针";
    private const string DensityProbeSearch = "密度探针";

    internal MainWorkbenchDensityMeasurement MeasureWorkbenchDensityForValidation(
        string label,
        Action<string>? revealed = null)
    {
        RequireEmptyDataForEmptyStateProbe();
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var originalSegment = _completedSegmentSelected;
        var countdownIds = new List<string>();
        var noteIds = new List<string>();
        string? tagId = null;
        var context = new List<string>();
        var hostHorizontalOffset = MainWindowScrollHost.HorizontalOffset;
        var hostVerticalOffset = MainWindowScrollHost.VerticalOffset;
        try
        {
            var tag = _desktopWorkspace.Apply(new CreateTagChange(DensityProbeTagName, "#F4B6A6"));
            var probeTag = tag.CreatedTag?.Id ?? throw new InvalidOperationException($"密度探针未取得夹具标签：{tag.Error}");
            tagId = probeTag;
            WindowValidationInput.Pump(Dispatcher);
            for (var index = 1; index <= 12; index++)
            {
                var name = index == 1
                    ? "密度探针三十字名称用来确认长标题仍单行完整显示不被裁切"
                    : string.Create(CultureInfo.InvariantCulture, $"密度探针倒计时 {index:00}");
                ApplyEmptyStateProbeChange(new SaveCountdownChange(
                    null,
                    name,
                    _desktopWorkspace.Snapshot.UtcNow.AddDays(index),
                    index % 2 == 0 ? new[] { probeTag } : Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
                countdownIds.Add(_desktopWorkspace.Snapshot.Settings.Countdowns.Single(record => record.Name == name).Id);
            }

            for (var index = 1; index <= 14; index++)
            {
                var number = index.ToString("00", CultureInfo.InvariantCulture);
                var text = index is 2 or 7
                    ? $"{DensityProbeSearch}长正文 {number}：" + string.Concat(Enumerable.Repeat("确认长便签在列表里自然换行、动作仍可达。", 3))
                    : $"{DensityProbeSearch}便签 {number}";
                ApplyEmptyStateProbeChange(new SaveNoteChange(
                    null,
                    text,
                    _desktopWorkspace.Snapshot.BeijingNow.Date.AddDays(index % 3),
                    false,
                    index % 2 == 0 ? new[] { probeTag } : Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
                noteIds.Add(_desktopWorkspace.Snapshot.Settings.Notes.Single(note => note.Text == text).Id);
            }

            // —— 倒计时页：收起新增区，量编辑卡留白、到列表的间距、列表首屏视口与完整可见行数。
            ShowWorkspace(WorkspaceKind.Countdowns);
            SetCountdownEditorExpanded(false);
            SettleEmptyStateProbeLayout();
            CountdownListRegion.ScrollToTop();
            SettleEmptyStateProbeLayout();
            var countdownEditor = DensityProbeBounds(CountdownEditorSurface, MainLayoutSurface);
            var countdownContent = DensityProbeContentBounds(CountdownEditorCard, MainLayoutSurface);
            var countdownList = DensityProbeBounds(CountdownListRegion, MainLayoutSurface);
            var countdownViewport = CountdownListRegion.ViewportHeight;
            var countdownRowsVisible = DensityProbeFullyVisible(CountdownRows.Children.OfType<FrameworkElement>(), CountdownListRegion);
            var countdownScrollable = CountdownListRegion.ScrollableHeight;
            Reveal($"{label}-countdowns", CountdownListRegion);
            CountdownListRegion.ScrollToBottom();
            SettleEmptyStateProbeLayout();
            var countdownEditorStays = DensityProbeSameBounds(
                countdownEditor,
                DensityProbeBounds(CountdownEditorSurface, MainLayoutSurface));
            CountdownListRegion.ScrollToTop();
            SettleEmptyStateProbeLayout();

            // —— 便签页：量编辑卡留白、区块间距、列表卡到状态栏的间距与首屏能给行用的视口。
            ShowWorkspace(WorkspaceKind.Notes);
            SelectNotesSegment(false);
            SettleEmptyStateProbeLayout();
            NotesListRegion.ScrollToTop();
            SettleEmptyStateProbeLayout();
            var noteEditor = DensityProbeBounds(NoteEditorSurface, MainLayoutSurface);
            var noteContent = DensityProbeContentBounds(NoteEditorFields, MainLayoutSurface);
            var notesCard = DensityProbeBounds(NotesListSurface, MainLayoutSurface);
            var statusBar = DensityProbeBounds(MainStatusBarSurface, MainLayoutSurface);
            var noteRowsTop = DensityProbeBounds(ActiveNoteRows, NotesListRegion).Top;
            var noteRowsViewport = NotesListRegion.ViewportHeight - noteRowsTop;
            var noteCardsVisible = DensityProbeFullyVisible(
                _noteSourceRows.Values.OfType<FrameworkElement>().Where(row => row.IsVisible),
                NotesListRegion);
            var headerInsideScroller = DensityProbeIsDescendant(NotesStatusHeader, NotesListRegion);
            CheckNotesContext("顶部");
            Reveal($"{label}-notes", NotesListCard);
            var header = DensityProbeBounds(NotesStatusHeader, MainLayoutSurface);
            NotesListRegion.ScrollToBottom();
            SettleEmptyStateProbeLayout();
            var headerStays = DensityProbeSameBounds(header, DensityProbeBounds(NotesStatusHeader, MainLayoutSurface)) &&
                NotesListRegion.VerticalOffset > 0.5;
            CheckNotesContext("滚到底");
            NotesListRegion.ScrollToBottom();
            SettleEmptyStateProbeLayout();
            Reveal($"{label}-notes-scrolled", NotesListCard);

            // —— 搜索：命中全部探针便签，列表仍出行，分段与搜索固定可见。
            NotesListRegion.ScrollToTop();
            NoteSearch.Text = DensityProbeSearch;
            SettleEmptyStateProbeLayout();
            var searchShowsRows = DensityProbeFullyVisible(
                _noteSourceRows.Values.OfType<FrameworkElement>().Where(row => row.IsVisible),
                NotesListRegion) > 0;
            CheckNotesContext("搜索");
            Reveal($"{label}-notes-search", NotesListCard);
            NoteSearch.Text = string.Empty;
            SettleEmptyStateProbeLayout();

            // —— 标签筛选：只剩一半便签，分段与搜索固定可见。
            _desktopWorkspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = new[] { probeTag } });
            SettleEmptyStateProbeLayout();
            var filterShowsRows = DensityProbeFullyVisible(
                _noteSourceRows.Values.OfType<FrameworkElement>().Where(row => row.IsVisible),
                NotesListRegion) > 0;
            CheckNotesContext("标签筛选");
            ClearFilters();
            SettleEmptyStateProbeLayout();

            // —— 多选：两张便签被选中后批量操作条出现在搜索与列表之间，分段、搜索、操作条与列表互不重叠。
            var selectable = VisibleNoteSelectionRows();
            if (selectable.Length < 2)
            {
                throw new InvalidOperationException("密度探针需要至少两张可选便签。");
            }

            SelectNoteRow(selectable[0], ModifierKeys.None);
            SelectNoteRow(selectable[1], ModifierKeys.Control);
            SettleEmptyStateProbeLayout();
            var batchVisible = NoteBatchActions.IsVisible;
            CheckNotesContext("多选");
            var batchViewport = NotesListRegion.ViewportHeight;
            Reveal($"{label}-notes-selected", NotesListCard);
            OnClearSelectedNotes(this, new RoutedEventArgs());
            SettleEmptyStateProbeLayout();

            return new MainWorkbenchDensityMeasurement(
                label,
                new Size(ActualWidth, ActualHeight),
                DensityProbeHasLayoutScaling(),
                countdownContent.Top - countdownEditor.Top,
                countdownEditor.Bottom - countdownContent.Bottom,
                countdownList.Top - countdownEditor.Bottom,
                countdownViewport,
                countdownScrollable,
                countdownRowsVisible,
                countdownEditorStays,
                noteContent.Top - noteEditor.Top,
                noteEditor.Bottom - noteContent.Bottom,
                notesCard.Top - noteEditor.Bottom,
                statusBar.Top - notesCard.Bottom,
                noteRowsViewport,
                noteCardsVisible,
                !headerInsideScroller,
                headerStays,
                searchShowsRows,
                filterShowsRows,
                batchVisible,
                batchViewport,
                context);
        }
        finally
        {
            NoteSearch.Text = string.Empty;
            OnClearSelectedNotes(this, new RoutedEventArgs());
            RestoreAfterEmptyStateProbe(originalWorkspace, originalFilter, originalSegment, countdownIds, noteIds);
            if (tagId is not null)
            {
                _desktopWorkspace.Apply(new DeleteTagChange(tagId));
            }

            MainWindowScrollHost.ScrollToHorizontalOffset(hostHorizontalOffset);
            MainWindowScrollHost.ScrollToVerticalOffset(hostVerticalOffset);
            SettleEmptyStateProbeLayout();
        }

        // 实拍：先把要看的区块滚进管理窗口可视区（窄窗口靠窗口级滚动兜底），拍完复原外层滚动偏移。
        void Reveal(string state, FrameworkElement subject)
        {
            if (revealed is null)
            {
                return;
            }

            subject.BringIntoView();
            SettleEmptyStateProbeLayout();
            revealed(state);
            MainWindowScrollHost.ScrollToHorizontalOffset(hostHorizontalOffset);
            MainWindowScrollHost.ScrollToVerticalOffset(hostVerticalOffset);
            SettleEmptyStateProbeLayout();
        }

        // 列表卡顶部的上下文控件：完整落在列表卡里、不落进滚动视口、两两不重叠；滚进管理窗口可视区后完整可见；
        // 文字不低于 13 DIP，命中区不低于 28 DIP。
        void CheckNotesContext(string state)
        {
            var card = DensityProbeBounds(NotesListSurface, NotesListCard);
            var list = DensityProbeBounds(NotesListRegion, NotesListCard);
            var targets = new List<(string Name, FrameworkElement Element)>
            {
                ("未完成分段", ActiveNotesSegment),
                ("已完成分段", CompletedNotesSegment),
                ("搜索标签", NoteSearchLabel),
                ("搜索框", NoteSearch)
            };
            if (NoteBatchActions.IsVisible)
            {
                targets.Add(("已选数量", SelectedNotesCount));
                targets.AddRange(NoteBatchActions.Children.OfType<ButtonBase>()
                    .Where(button => button.IsVisible)
                    .Select(button => (button.Content?.ToString() ?? string.Empty, (FrameworkElement)button)));
            }

            var bounds = targets.Select(target => (target.Name, target.Element, Bounds: DensityProbeBounds(target.Element, NotesListCard))).ToArray();
            foreach (var (name, element, rect) in bounds)
            {
                if (!element.IsVisible || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                {
                    context.Add($"{state}：{name}不可见");
                    continue;
                }

                if (!DensityProbeInside(rect, card))
                {
                    context.Add($"{state}：{name}越出列表卡 {DensityProbeFormat(rect)}");
                }

                if (rect.IntersectsWith(list) && Rect.Intersect(rect, list) is { Width: > 0.5, Height: > 0.5 })
                {
                    context.Add($"{state}：{name}落进列表滚动视口 {DensityProbeFormat(rect)}");
                }

                if (element is Control && element.ActualHeight < 28 - 0.5)
                {
                    context.Add($"{state}：{name}命中高 {element.ActualHeight:0.##} 低于 28");
                }
            }

            for (var first = 0; first < bounds.Length; first++)
            {
                for (var second = first + 1; second < bounds.Length; second++)
                {
                    var overlap = Rect.Intersect(bounds[first].Bounds, bounds[second].Bounds);
                    if (!overlap.IsEmpty && overlap.Width > 0.5 && overlap.Height > 0.5 &&
                        !DensityProbeIsDescendant(bounds[first].Element, bounds[second].Element) &&
                        !DensityProbeIsDescendant(bounds[second].Element, bounds[first].Element))
                    {
                        context.Add($"{state}：{bounds[first].Name}与{bounds[second].Name}重叠");
                    }
                }
            }

            foreach (var text in MainValidationDescendants<TextBlock>(NotesListCard)
                         .Where(text => text.IsVisible && !string.IsNullOrEmpty(text.Text) &&
                                        !DensityProbeIsDescendant(text, NotesListRegion)))
            {
                if (text.FontSize < 13 - 0.01)
                {
                    context.Add($"{state}：「{text.Text}」字号 {text.FontSize:0.##} 低于 13");
                }
            }

            // 窄窗口（720 宽）靠窗口级滚动兜底，左侧分段与右侧搜索不必同屏；逐个滚进可视区后各自完整可见。
            var window = new Rect(0, 0, MainWindowScrollHost.ViewportWidth, MainWindowScrollHost.ViewportHeight);
            foreach (var (name, element) in new (string, FrameworkElement)[]
                     {
                         ("未完成分段", ActiveNotesSegment),
                         ("已完成分段", CompletedNotesSegment),
                         ("搜索框", NoteSearch)
                     })
            {
                element.BringIntoView();
                SettleEmptyStateProbeLayout();
                var inWindow = DensityProbeBounds(element, MainWindowScrollHost);
                if (!DensityProbeInside(inWindow, window))
                {
                    context.Add($"{state}：{name}滚进窗口可视区后仍被裁切 {DensityProbeFormat(inWindow)}");
                }
            }

            MainWindowScrollHost.ScrollToHorizontalOffset(hostHorizontalOffset);
            MainWindowScrollHost.ScrollToVerticalOffset(hostVerticalOffset);
            SettleEmptyStateProbeLayout();
        }
    }

    private bool DensityProbeHasLayoutScaling()
    {
        for (DependencyObject? current = MainLayoutSurface; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Viewbox ||
                current is FrameworkElement { LayoutTransform: { } transform } && !transform.Value.IsIdentity)
            {
                return true;
            }
        }

        return false;
    }

    // 卡内真实内容的范围：可见的直接子元素，空文本的 TextBlock（收起的错误行）不算内容。
    private static Rect DensityProbeContentBounds(Panel panel, Visual ancestor)
    {
        var content = Rect.Empty;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (!child.IsVisible || child.ActualHeight <= 0 || child is TextBlock { Text: null or "" })
            {
                continue;
            }

            content.Union(DensityProbeBounds(child, ancestor));
        }

        return content;
    }

    private static int DensityProbeFullyVisible(IEnumerable<FrameworkElement> rows, ScrollViewer list) =>
        rows.Count(row =>
        {
            if (!row.IsVisible || row.ActualHeight <= 0)
            {
                return false;
            }

            var bounds = DensityProbeBounds(row, list);
            return bounds.Top >= -0.5 && bounds.Bottom <= list.ViewportHeight + 0.5;
        });

    private static bool DensityProbeIsDescendant(DependencyObject candidate, DependencyObject ancestor)
    {
        for (var current = VisualTreeHelper.GetParent(candidate); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static Rect DensityProbeBounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(0, 0), element.RenderSize));

    private static bool DensityProbeSameBounds(Rect first, Rect second) =>
        Math.Abs(first.X - second.X) <= 0.5 &&
        Math.Abs(first.Y - second.Y) <= 0.5 &&
        Math.Abs(first.Width - second.Width) <= 0.5 &&
        Math.Abs(first.Height - second.Height) <= 0.5;

    private static bool DensityProbeInside(Rect inner, Rect outer) =>
        !inner.IsEmpty &&
        inner.Left >= outer.Left - 0.5 &&
        inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 &&
        inner.Bottom <= outer.Bottom + 0.5;

    private static string DensityProbeFormat(Rect bounds) => bounds.IsEmpty
        ? "无"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"({bounds.X:0.##},{bounds.Y:0.##},{bounds.Width:0.##}×{bounds.Height:0.##})");
}

// #251：悬浮窗滚动上下文提示的窗口探针。读内容呈现器上实际挂着的不透明遮罩，按渐变停靠点还原两侧是否渐隐、
// 渐隐带长度与边缘不透明度；同时记下其余表面（滚动条、命令区、玩偶层、标题栏、外壳）有没有被挂遮罩，以及滚动条
// 命中轨、thumb 与正文右界。场景切换（搜索展开、快速新增展开、「…」菜单）走窗口自己的入口。只测量、不判定。
public sealed partial class FloatingWindow
{
    internal ScrollViewer BodyScrollerForValidation => FloatingBodyScroller;

    internal FrameworkElement IncomeForValidation => FloatingIncome;

    // thumb 拖动输入路径用的真实滚动条 thumb。
    internal Thumb? BodyScrollThumbForValidation
    {
        get
        {
            FloatingBodyScroller.ApplyTemplate();
            return (FloatingBodyScroller.Template?.FindName("PART_VerticalScrollBar", FloatingBodyScroller) as ScrollBar)
                ?.Track?.Thumb;
        }
    }

    internal FloatingScrollContextState ScrollContextStateForValidation()
    {
        SettleScrollContextForValidation();
        FloatingBodyScroller.ApplyTemplate();
        var presenter = FloatingBodyScroller.Template.FindName("PART_ScrollContentPresenter", FloatingBodyScroller)
            as ScrollContentPresenter ?? throw new InvalidOperationException("悬浮窗列表内容呈现器未实例化。");
        var scrollBar = FloatingBodyScroller.Template.FindName("PART_VerticalScrollBar", FloatingBodyScroller)
            as ScrollBar ?? throw new InvalidOperationException("悬浮窗列表滚动条未实例化。");
        scrollBar.ApplyTemplate();
        var rail = scrollBar.Template.FindName("FloatingScrollBarHitRail", scrollBar) as FrameworkElement;
        var thumbVisual = scrollBar.Track?.Thumb is { } thumb
            ? ValidationDescendants<Border>(thumb).FirstOrDefault(border => border.Name == "FloatingScrollBarThumbVisual")
            : null;
        var root = Content as FrameworkElement ?? throw new InvalidOperationException("悬浮窗根视觉不可用。");
        var strayMasks = new (string Name, UIElement Element)[]
            {
                ("FloatingBodyScroller", FloatingBodyScroller),
                ("PART_VerticalScrollBar", scrollBar),
                ("FloatingNotesCommandScroller", FloatingNotesCommandScroller),
                ("FloatingPlushScene", FloatingPlushScene),
                ("FloatingHeaderSurface", FloatingHeaderSurface),
                ("TaskCard", TaskCard)
            }
            .Where(target => target.Element.OpacityMask is not null)
            .Select(target => target.Name)
            .Concat(ValidationDescendants<UIElement>(presenter)
                .Where(element => element.OpacityMask is not null)
                .Select(element => $"呈现器内 {element.GetType().Name}"))
            .ToArray();
        var viewportHeight = presenter.ActualHeight;
        var presenterBounds = presenter.TransformToAncestor(root)
            .TransformBounds(new Rect(new Point(0, 0), presenter.RenderSize));
        var scrollBarBounds = scrollBar.IsVisible
            ? scrollBar.TransformToAncestor(root).TransformBounds(new Rect(new Point(0, 0), scrollBar.RenderSize))
            : Rect.Empty;
        var state = new FloatingScrollContextState(
            _workspace.ToString(),
            FloatingBodyScroller.VerticalOffset,
            FloatingBodyScroller.ScrollableHeight,
            viewportHeight,
            presenter.OpacityMask is not null,
            false,
            false,
            0,
            0,
            1,
            presenter.OpacityMask is null,
            strayMasks,
            rail?.ActualWidth ?? 0,
            thumbVisual?.ActualWidth ?? 0,
            presenterBounds,
            scrollBarBounds,
            DensityProbeIsDescendantOf(scrollBar, presenter) ||
                DensityProbeIsDescendantOf(FloatingNotesCommandScroller, presenter) ||
                DensityProbeIsDescendantOf(FloatingPlushScene, presenter));
        if (presenter.OpacityMask is not LinearGradientBrush mask || mask.GradientStops.Count < 2)
        {
            return state;
        }

        var stops = mask.GradientStops.OrderBy(stop => stop.Offset).ToArray();
        var opaque = stops.Where(stop => stop.Color.A == byte.MaxValue).ToArray();
        var above = stops[0].Offset <= 0.0001 && stops[0].Color.A < byte.MaxValue;
        var below = stops[^1].Offset >= 0.9999 && stops[^1].Color.A < byte.MaxValue;
        var maskHeight = mask.EndPoint.Y - mask.StartPoint.Y;
        var wellFormed =
            mask.MappingMode == BrushMappingMode.Absolute &&
            Math.Abs(mask.StartPoint.X) <= 0.01 && Math.Abs(mask.EndPoint.X) <= 0.01 &&
            Math.Abs(mask.StartPoint.Y) <= 0.01 &&
            Math.Abs(maskHeight - viewportHeight) <= 0.5 &&
            opaque.Length > 0 &&
            mask.Opacity >= 0.999;
        return state with
        {
            Above = above,
            Below = below,
            AboveFade = above && opaque.Length > 0 ? opaque[0].Offset * maskHeight : 0,
            BelowFade = below && opaque.Length > 0 ? (1 - opaque[^1].Offset) * maskHeight : 0,
            EdgeOpacity = stops.Min(stop => stop.Color.A) / 255.0,
            BrushWellFormed = wellFormed
        };
    }

    internal void SettleScrollContextForValidation()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
        }
    }

    internal void ScrollBodyForValidation(double offset)
    {
        FloatingBodyScroller.ScrollToVerticalOffset(offset);
        SettleScrollContextForValidation();
    }

    internal void SetNoteSearchExpandedForValidation(bool expanded)
    {
        if (expanded)
        {
            ExpandNoteSearch(focus: false);
        }
        else
        {
            _searchExpanded = false;
            UpdateSearchPresentation();
        }

        SettleScrollContextForValidation();
    }

    // 「…」菜单挑一张露在视口里的便签卡打开，弹层贴着可见卡片出现。
    internal Popup? OpenVisibleNoteOverflowMenuForValidation()
    {
        SettleScrollContextForValidation();
        var viewport = new Rect(0, 0, FloatingBodyScroller.ViewportWidth, FloatingBodyScroller.ViewportHeight);
        var trigger = NoteOverflowTriggersForValidation().FirstOrDefault(candidate =>
            candidate.IsVisible &&
            viewport.Contains(candidate.TransformToAncestor(FloatingBodyScroller)
                .TransformBounds(new Rect(new Point(0, 0), candidate.RenderSize))));
        if (trigger is null || NoteRowOverflowMenuBehavior.GetPopup(trigger) is not { } popup)
        {
            return null;
        }

        popup.IsOpen = true;
        SettleScrollContextForValidation();
        return popup;
    }

    private static bool DensityProbeIsDescendantOf(DependencyObject candidate, DependencyObject ancestor)
    {
        for (var current = VisualTreeHelper.GetParent(candidate); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}

// #251：悬浮窗滚动上下文提示的一次读数。AboveFade/BelowFade 是渐隐带长度（DIP），EdgeOpacity 是边缘最低不透明度；
// Masked 为假时两侧都没有提示。StrayMasks 列出内容呈现器以外被挂上遮罩的表面；RailWidth/ThumbVisualWidth 是滚动条
// 命中轨与 thumb 可见宽；ProtectedInsidePresenter 为真表示滚动条、命令区或玩偶层落进了被遮罩的呈现器。
internal sealed record FloatingScrollContextState(
    string Workspace,
    double Offset,
    double Scrollable,
    double Viewport,
    bool Masked,
    bool Above,
    bool Below,
    double AboveFade,
    double BelowFade,
    double EdgeOpacity,
    bool BrushWellFormed,
    IReadOnlyList<string> StrayMasks,
    double RailWidth,
    double ThumbVisualWidth,
    Rect PresenterBounds,
    Rect ScrollBarBounds,
    bool ProtectedInsidePresenter)
{
    internal const double MaximumFade = 18;
    internal const double MinimumFade = 8;
    internal const double MinimumEdgeOpacity = 0.3;
    internal const double MaximumEdgeOpacity = 0.6;

    // 期望的提示方向之外，还要求：遮罩形状正确、渐隐带与边缘不透明度在克制范围内、其余表面不挂遮罩、
    // 滚动条 10 DIP 命中轨与 4 DIP thumb 不变。
    public IReadOnlyList<string> Problems(bool expectAbove, bool expectBelow)
    {
        var problems = new List<string>();
        if (Above != expectAbove || Below != expectBelow)
        {
            problems.Add($"提示方向 上{(Above ? "有" : "无")}/下{(Below ? "有" : "无")}，期望 上{(expectAbove ? "有" : "无")}/下{(expectBelow ? "有" : "无")}");
        }

        if (Masked != (expectAbove || expectBelow))
        {
            problems.Add(Masked ? "无提示时仍挂着遮罩" : "缺少遮罩");
        }

        if (Masked && !BrushWellFormed)
        {
            problems.Add("遮罩不是按视口高度的绝对坐标纵向渐变");
        }

        foreach (var (active, fade, side) in new[] { (Above, AboveFade, "上"), (Below, BelowFade, "下") })
        {
            if (active && (fade < MinimumFade - 0.01 || fade > MaximumFade + 0.01))
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{side}侧渐隐带 {fade:0.##} DIP 越出 {MinimumFade}–{MaximumFade}"));
            }
        }

        if (Masked && (EdgeOpacity < MinimumEdgeOpacity - 0.01 || EdgeOpacity > MaximumEdgeOpacity + 0.01))
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"边缘不透明度 {EdgeOpacity:0.##} 越出 {MinimumEdgeOpacity}–{MaximumEdgeOpacity}"));
        }

        if (StrayMasks.Count > 0)
        {
            problems.Add($"其余表面挂了遮罩：{string.Join("、", StrayMasks)}");
        }

        if (ProtectedInsidePresenter)
        {
            problems.Add("滚动条、命令区或玩偶层落进被遮罩的呈现器");
        }

        if (Scrollable > 0.5 && (Math.Abs(RailWidth - 10) > 0.01 || Math.Abs(ThumbVisualWidth - 4) > 0.01))
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"滚动条命中轨 {RailWidth:0.##} / thumb {ThumbVisualWidth:0.##}"));
        }

        return problems;
    }

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Workspace} 偏移 {Offset:0.##}/{Scrollable:0.##}，视口 {Viewport:0.##}，遮罩{(Masked ? "有" : "无")}，" +
        $"上 {(Above ? AboveFade.ToString("0.##", CultureInfo.InvariantCulture) : "无")}，" +
        $"下 {(Below ? BelowFade.ToString("0.##", CultureInfo.InvariantCulture) : "无")}，边缘 {EdgeOpacity:0.##}，" +
        $"呈现器 ({PresenterBounds.X:0.##},{PresenterBounds.Y:0.##},{PresenterBounds.Width:0.##}×{PresenterBounds.Height:0.##})");
}

// #251：密度与上下文测量。判定：每个窗口档窗口尺寸符合期望且不靠 Viewbox / LayoutTransform；两张编辑卡上下留白
// 只剩卡片内边距（倒计时 13、便签 10，各加 1 DIP 边框），编辑卡到列表、便签列表卡到状态栏都是 12 DIP 区块节奏；
// 首选尺寸下倒计时列表视口不低于 525 DIP、便签首屏给行的视口不低于 345 DIP（v71 约 490 / 276）；滚动列表时编辑卡与
// 便签状态分段原位不动；搜索、筛选、多选下仍有完整可见行，批量操作条出现时上下文控件不被裁切、不重叠。
internal sealed record MainWorkbenchDensityMeasurement(
    string Label,
    Size Window,
    bool LayoutScaled,
    double CountdownEditorTopInset,
    double CountdownEditorBottomInset,
    double CountdownEditorToList,
    double CountdownViewport,
    double CountdownScrollable,
    int CountdownRowsVisible,
    bool CountdownEditorStaysWhileScrolling,
    double NoteEditorTopInset,
    double NoteEditorBottomInset,
    double NoteEditorToList,
    double NoteListToStatusBar,
    double NoteRowsViewport,
    int NoteCardsVisible,
    bool NoteStatusHeaderOutsideScroller,
    bool NoteStatusHeaderStaysWhileScrolling,
    bool SearchShowsRows,
    bool FilterShowsRows,
    bool BatchActionsVisible,
    double BatchViewport,
    IReadOnlyList<string> ContextFailures)
{
    internal const double PreferredCountdownViewportFloor = 525;
    internal const double PreferredNoteRowsViewportFloor = 345;
    private const double Tolerance = 0.5;

    public IReadOnlyList<string> Failures(Size expectedWindow, bool preferredSize)
    {
        var failures = new List<string>();
        if (Math.Abs(Window.Width - expectedWindow.Width) > Tolerance ||
            Math.Abs(Window.Height - expectedWindow.Height) > Tolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"窗口 {Window.Width:0.##}×{Window.Height:0.##}，期望 {expectedWindow.Width:0.##}×{expectedWindow.Height:0.##}"));
        }

        if (LayoutScaled)
        {
            failures.Add("布局链上出现 Viewbox 或 LayoutTransform");
        }

        if (CountdownEditorTopInset > 14 + Tolerance || CountdownEditorBottomInset > 14 + Tolerance)
        {
            failures.Add("倒计时编辑卡留白超过卡片内边距");
        }

        if (Math.Abs(CountdownEditorToList - 12) > Tolerance)
        {
            failures.Add("倒计时编辑卡到列表不是 12 DIP");
        }

        if (NoteEditorTopInset > 11 + Tolerance || NoteEditorBottomInset > 11 + Tolerance)
        {
            failures.Add("便签编辑卡留白超过卡片内边距");
        }

        if (Math.Abs(NoteEditorToList - 12) > Tolerance || Math.Abs(NoteListToStatusBar - 12) > Tolerance)
        {
            failures.Add("便签编辑卡、列表卡与状态栏之间不是 12 DIP");
        }

        if (preferredSize &&
            (CountdownViewport < PreferredCountdownViewportFloor || NoteRowsViewport < PreferredNoteRowsViewportFloor))
        {
            failures.Add("首选尺寸列表首屏视口没有回收到位");
        }

        if (CountdownScrollable <= Tolerance)
        {
            failures.Add("倒计时长内容没有产生列表滚动");
        }

        if (!CountdownEditorStaysWhileScrolling)
        {
            failures.Add("滚动倒计时列表时编辑卡移位");
        }

        if (!NoteStatusHeaderOutsideScroller || !NoteStatusHeaderStaysWhileScrolling)
        {
            failures.Add("便签状态分段随列表滚动");
        }

        if (!SearchShowsRows || !FilterShowsRows || !BatchActionsVisible)
        {
            failures.Add("搜索、筛选或多选下没有完整行或批量操作条");
        }

        failures.AddRange(ContextFailures);
        return failures.Count == 0 ? [] : [$"{Label}：{string.Join("、", failures)}（{Describe()}）"];
    }

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"倒计时编辑留白 {CountdownEditorTopInset:0.##}/{CountdownEditorBottomInset:0.##}，到列表 {CountdownEditorToList:0.##}，" +
        $"视口 {CountdownViewport:0.##}，可滚 {CountdownScrollable:0.##}，完整行 {CountdownRowsVisible}；" +
        $"便签编辑留白 {NoteEditorTopInset:0.##}/{NoteEditorBottomInset:0.##}，到列表 {NoteEditorToList:0.##}，" +
        $"列表到状态栏 {NoteListToStatusBar:0.##}，首屏行视口 {NoteRowsViewport:0.##}，完整卡 {NoteCardsVisible}，" +
        $"多选视口 {BatchViewport:0.##}");
}
