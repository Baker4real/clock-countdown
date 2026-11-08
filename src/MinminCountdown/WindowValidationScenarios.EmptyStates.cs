using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

// #248：空态真实居中、滚动边界与唯一添加入口的窗口探针。控件访问留在窗口的场景 partial 里；隔离窗口、
// 窗口尺寸与 DPI 档由 ValidationExercises.EmptyStates 编排，判定写在文件末尾的测量记录上。
public sealed partial class MainWindow
{
    private const string EmptyStateProbeNoMatchSearch = "__empty_state_probe_no_match__";

    // 空态几何：量当前工作区空态所在列表的视口、内容范围与滚动条，空态块在列表视口里的位置和应居中的区域
    // （倒计时是整个列表视口，便签是状态分段以下到视口底）；再把空态块滚进管理窗口可视区，量它是否完整可见。
    // 只测量、不判定；revealed 在空态块滚进可视区时调用（实拍用）。收尾先把列表与外层滚动偏移复原。
    internal MainEmptyStateGeometry MeasureEmptyStateGeometryForValidation(
        string label,
        Action<string>? revealed = null)
    {
        var countdowns = _workspace == WorkspaceKind.Countdowns;
        var list = countdowns ? CountdownListRegion : NotesListRegion;
        FrameworkElement content = countdowns ? CountdownEmptyStateContent : NotesEmptyStateContent;
        var block = countdowns
            ? content
            : NotesEmptyStateContent.Children.OfType<FrameworkElement>().Single();
        var listOffset = list.VerticalOffset;
        var hostHorizontalOffset = MainWindowScrollHost.HorizontalOffset;
        var hostVerticalOffset = MainWindowScrollHost.VerticalOffset;
        try
        {
            SettleEmptyStateProbeLayout();
            var visible = content.IsVisible && block.ActualWidth > 0 && block.ActualHeight > 0;
            var areaTop = countdowns
                ? 0
                : Math.Max(0, EmptyStateProbeBounds(NotesStatusHeader, list).Bottom);
            var geometry = new MainEmptyStateGeometry(
                label,
                visible,
                (countdowns ? EmptyCountdownMessage : EmptyNotesMessage).Text,
                list.ViewportWidth,
                list.ViewportHeight,
                list.ExtentHeight,
                list.ScrollableHeight,
                list.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                visible ? EmptyStateProbeBounds(block, list) : Rect.Empty,
                new Rect(0, areaTop, list.ViewportWidth, Math.Max(0, list.ViewportHeight - areaTop)),
                Rect.Empty,
                list.VerticalOffset,
                new Size(MainWindowScrollHost.ViewportWidth, MainWindowScrollHost.ViewportHeight));
            if (!visible)
            {
                return geometry;
            }

            block.BringIntoView();
            SettleEmptyStateProbeLayout();
            geometry = geometry with
            {
                BlockInWindow = EmptyStateProbeBounds(block, MainWindowScrollHost),
                ListOffset = list.VerticalOffset
            };
            revealed?.Invoke(label);
            return geometry;
        }
        finally
        {
            list.ScrollToVerticalOffset(listOffset);
            MainWindowScrollHost.ScrollToHorizontalOffset(hostHorizontalOffset);
            MainWindowScrollHost.ScrollToVerticalOffset(hostVerticalOffset);
            SettleEmptyStateProbeLayout();
        }
    }

    // 依次切到首次无数据、筛选无结果和当前分段无内容（两个分段），每种空态各量一次几何。要求开始时没有已保存的
    // 倒计时与便签；探针加的记录、筛选、分段和工作区在收尾复原。
    internal IReadOnlyList<MainEmptyStateGeometry> MeasureEmptyStateGeometriesForValidation(
        string sizeLabel,
        Action<string>? revealed = null)
    {
        RequireEmptyDataForEmptyStateProbe();
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var originalSegment = _completedSegmentSelected;
        var measurements = new List<MainEmptyStateGeometry>();
        var countdownIds = new List<string>();
        var noteIds = new List<string>();
        void Measure(WorkspaceKind workspace, string state)
        {
            ShowWorkspace(workspace);
            var workspaceName = workspace == WorkspaceKind.Countdowns ? "countdowns" : "notes";
            measurements.Add(MeasureEmptyStateGeometryForValidation(
                $"{sizeLabel}-{workspaceName}-{state}",
                revealed));
        }

        try
        {
            Measure(WorkspaceKind.Countdowns, "first-use");
            Measure(WorkspaceKind.Notes, "first-use");
            countdownIds.Add(SaveEmptyStateProbeCountdown("空态几何探针倒计时", 7));
            noteIds.Add(SaveEmptyStateProbeNote("空态几何探针便签"));
            _desktopWorkspace.ApplyFilter(
                DesktopWorkspaceFilter.Empty with { CountdownStatus = CountdownStatusFilter.Expired });
            Measure(WorkspaceKind.Countdowns, "filter-no-results");
            _desktopWorkspace.ApplyFilter(
                DesktopWorkspaceFilter.Empty with { NoteSearch = EmptyStateProbeNoMatchSearch });
            Measure(WorkspaceKind.Notes, "filter-no-results");
            ClearFilters();
            SelectNotesSegment(true);
            Measure(WorkspaceKind.Notes, "completed-segment-empty");
            ApplyEmptyStateProbeChange(new SetNoteCompletionChange(noteIds[0], true));
            SelectNotesSegment(false);
            Measure(WorkspaceKind.Notes, "active-segment-empty");
            return measurements;
        }
        finally
        {
            RestoreAfterEmptyStateProbe(originalWorkspace, originalFilter, originalSegment, countdownIds, noteIds);
        }
    }

    // 空态内容与动作：首次无数据给说明、角色与填写引导，引导只把焦点交给已有表单——保留草稿、不新增记录；
    // 筛选无结果只给重置，重置后恢复结果；当前分段无内容说明记录在另一分段，不给重置、不说成没有数据。
    // 每种空态里都没有写着「添加」的按钮，首要添加入口只有表单里的保存按钮。要求开始时没有已保存的记录；
    // 探针加的记录、草稿、筛选、分段、焦点和工作区在收尾复原。
    internal MainEmptyStateGuidanceProbe ProbeEmptyStateGuidanceForValidation()
    {
        RequireEmptyDataForEmptyStateProbe();
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var originalSegment = _completedSegmentSelected;
        var originalFocus = Keyboard.FocusedElement;
        var originalCountdownName = CountdownName.Text;
        var originalNoteText = NoteText.Text;
        var countdownIds = new List<string>();
        var noteIds = new List<string>();
        var details = new List<string>();
        MainEmptyStateView Read(WorkspaceKind workspace, string state)
        {
            ShowWorkspace(workspace);
            SettleEmptyStateProbeLayout();
            var view = ReadEmptyStateViewForValidation();
            details.Add($"{state}：{view.Describe()}");
            return view;
        }

        bool GuideKeepsDraft(string state, Button guide, TextBox editor, string draft, Func<int> savedCount)
        {
            editor.Text = draft;
            guide.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleEmptyStateProbeLayout();
            var focused = Keyboard.FocusedElement as FrameworkElement;
            details.Add(
                $"{state}：焦点 {focused?.Name ?? Keyboard.FocusedElement?.GetType().Name ?? "无"}，" +
                $"内容「{editor.Text}」，记录 {savedCount()} 条");
            return ReferenceEquals(Keyboard.FocusedElement, editor) &&
                string.Equals(editor.Text, draft, StringComparison.Ordinal) &&
                savedCount() == 0;
        }

        try
        {
            Activate();
            var countdownFirstUse = Read(WorkspaceKind.Countdowns, "倒计时首次");
            var countdownGuideKeepsDraft = GuideKeepsDraft(
                "倒计时引导",
                AddEmptyCountdownButton,
                CountdownName,
                "空态引导保留的倒计时草稿",
                () => _desktopWorkspace.Snapshot.Settings.Countdowns.Count);
            CountdownName.Text = originalCountdownName;
            var noteFirstUse = Read(WorkspaceKind.Notes, "便签首次");
            var noteGuideKeepsDraft = GuideKeepsDraft(
                "便签引导",
                AddEmptyNoteButton,
                NoteText,
                "空态引导保留的便签草稿",
                () => _desktopWorkspace.Snapshot.Settings.Notes.Count);
            NoteText.Text = originalNoteText;

            countdownIds.Add(SaveEmptyStateProbeCountdown("空态引导探针倒计时", 7));
            noteIds.Add(SaveEmptyStateProbeNote("空态引导探针便签"));

            ShowWorkspace(WorkspaceKind.Countdowns);
            SetCountdownFilter(CountdownStatusFilter.Expired);
            var countdownFiltered = Read(WorkspaceKind.Countdowns, "倒计时筛选无结果");
            ResetEmptyCountdownFiltersButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleEmptyStateProbeLayout();
            var countdownResetRestores =
                !_desktopWorkspace.Snapshot.Filter.HasConstraints &&
                CountdownRows.Children.Count > 0 &&
                !CountdownEmptyStateContent.IsVisible;

            ShowWorkspace(WorkspaceKind.Notes);
            NoteSearch.Text = EmptyStateProbeNoMatchSearch;
            var noteFiltered = Read(WorkspaceKind.Notes, "便签筛选无结果");
            ResetEmptyNotesFiltersButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleEmptyStateProbeLayout();
            var noteResetRestores =
                !_desktopWorkspace.Snapshot.Filter.HasConstraints &&
                string.IsNullOrEmpty(NoteSearch.Text) &&
                ActiveNoteRows.IsVisible &&
                ActiveNoteRows.Children.Count > 0 &&
                !NotesEmptyStateContent.IsVisible;
            details.Add($"重置：倒计时恢复结果 {countdownResetRestores}，便签恢复结果 {noteResetRestores}");

            SelectNotesSegment(true);
            var completedSegmentEmpty = Read(WorkspaceKind.Notes, "已完成分段无内容");
            ApplyEmptyStateProbeChange(new SetNoteCompletionChange(noteIds[0], true));
            SelectNotesSegment(false);
            var activeSegmentEmpty = Read(WorkspaceKind.Notes, "未完成分段无内容");

            var views = new[]
            {
                countdownFirstUse, noteFirstUse, countdownFiltered, noteFiltered, completedSegmentEmpty, activeSegmentEmpty
            };
            return new MainEmptyStateGuidanceProbe(
                countdownFirstUse.ShowsFirstUse("还没有倒计时", CountdownEmptyStateHint),
                noteFirstUse.ShowsFirstUse("还没有便签", NoteEmptyStateHint),
                countdownFirstUse.GuideIsLightweight && noteFirstUse.GuideIsLightweight,
                countdownGuideKeepsDraft && noteGuideKeepsDraft,
                views.All(view => view.AddLabeledButtons == 0 && view.FormAddEntryVisible),
                countdownFiltered.ShowsOnlyReset("没有符合当前筛选条件的倒计时。") &&
                    noteFiltered.ShowsOnlyReset("没有符合当前筛选条件的便签。"),
                countdownResetRestores && noteResetRestores,
                completedSegmentEmpty.ExplainsEmptySegment("已完成", otherSegment: null) &&
                    activeSegmentEmpty.ExplainsEmptySegment("未完成", otherSegment: "已完成"),
                details);
        }
        finally
        {
            CountdownName.Text = originalCountdownName;
            NoteText.Text = originalNoteText;
            RestoreAfterEmptyStateProbe(originalWorkspace, originalFilter, originalSegment, countdownIds, noteIds);
            if (originalFocus is not null)
            {
                Keyboard.Focus(originalFocus);
            }
            else
            {
                Keyboard.ClearFocus();
            }
        }
    }

    // 真实溢出：两个列表各加足以溢出的记录。滚动条照常出现；滚轮、拖动 thumb 与键盘 PageDown 都能滚动，PageDown
    // 一路能到底；滚到底时视觉上最后一行的操作完整落在列表视口里；每一行的每个操作滚进视口后都完整可见。
    // 要求开始时没有已保存的记录；收尾删掉探针记录，复原滚动偏移、焦点与工作区。
    internal MainListOverflowProbe ProbeListOverflowScrollingForValidation(string sizeLabel)
    {
        RequireEmptyDataForEmptyStateProbe();
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var originalSegment = _completedSegmentSelected;
        var originalFocus = Keyboard.FocusedElement;
        var countdownIds = new List<string>();
        var noteIds = new List<string>();
        var details = new List<string>();
        try
        {
            for (var index = 1; index <= 12; index++)
            {
                countdownIds.Add(SaveEmptyStateProbeCountdown(
                    string.Create(CultureInfo.InvariantCulture, $"溢出探针倒计时 {index:00}"),
                    index));
            }

            for (var index = 1; index <= 14; index++)
            {
                noteIds.Add(SaveEmptyStateProbeNote(
                    string.Create(CultureInfo.InvariantCulture, $"溢出探针便签 {index:00}")));
            }

            Activate();
            ShowWorkspace(WorkspaceKind.Countdowns);
            SettleEmptyStateProbeLayout();
            var countdowns = MeasureListOverflowForValidation(
                $"{sizeLabel}-countdowns",
                CountdownListRegion,
                countdownIds.Select(id => _countdownSourceRows[id]).ToArray(),
                details);
            ShowWorkspace(WorkspaceKind.Notes);
            SettleEmptyStateProbeLayout();
            var notes = MeasureListOverflowForValidation(
                $"{sizeLabel}-notes",
                NotesListRegion,
                noteIds.Select(id => _noteSourceRows[id]).ToArray(),
                details);
            return MainListOverflowProbe.FromMeasurements([countdowns, notes], details);
        }
        finally
        {
            RestoreAfterEmptyStateProbe(originalWorkspace, originalFilter, originalSegment, countdownIds, noteIds);
            if (originalFocus is not null)
            {
                Keyboard.Focus(originalFocus);
            }
            else
            {
                Keyboard.ClearFocus();
            }
        }
    }

    private MainListOverflowMeasurement MeasureListOverflowForValidation(
        string label,
        ScrollViewer list,
        IReadOnlyList<FrameworkElement> rows,
        ICollection<string> details)
    {
        var listOffset = list.VerticalOffset;
        var hostHorizontalOffset = MainWindowScrollHost.HorizontalOffset;
        var hostVerticalOffset = MainWindowScrollHost.VerticalOffset;
        static bool Inside(Rect bounds, double width, double height) =>
            !bounds.IsEmpty &&
            bounds.Left >= -0.5 &&
            bounds.Top >= -0.5 &&
            bounds.Right <= width + 0.5 &&
            bounds.Bottom <= height + 0.5;
        bool InsideList(FrameworkElement element) =>
            Inside(EmptyStateProbeBounds(element, list), list.ViewportWidth, list.ViewportHeight);
        bool InsideWindow(FrameworkElement element) =>
            Inside(
                EmptyStateProbeBounds(element, MainWindowScrollHost),
                MainWindowScrollHost.ViewportWidth,
                MainWindowScrollHost.ViewportHeight);
        static ButtonBase[] Actions(FrameworkElement row) =>
            MainValidationDescendants<ButtonBase>(row)
                .Where(action => action.IsVisible && action.IsEnabled)
                .ToArray();
        try
        {
            SettleEmptyStateProbeLayout();
            var scrollBarShown =
                list.ScrollableHeight > 0.5 &&
                list.ComputedVerticalScrollBarVisibility == Visibility.Visible;

            list.ScrollToTop();
            SettleEmptyStateProbeLayout();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = list
            };
            list.RaiseEvent(wheel);
            SettleEmptyStateProbeLayout();
            var wheelOffset = list.VerticalOffset;

            list.ScrollToTop();
            SettleEmptyStateProbeLayout();
            var track = (list.Template?.FindName("PART_VerticalScrollBar", list) as ScrollBar)?.Track;
            var thumbOffset = 0d;
            if (track?.Thumb is { } thumb)
            {
                thumb.RaiseEvent(new DragStartedEventArgs(0, 0)
                {
                    RoutedEvent = Thumb.DragStartedEvent,
                    Source = thumb
                });
                thumb.RaiseEvent(new DragDeltaEventArgs(0, Math.Max(24, track.ActualHeight / 3))
                {
                    RoutedEvent = Thumb.DragDeltaEvent,
                    Source = thumb
                });
                SettleEmptyStateProbeLayout();
                thumbOffset = list.VerticalOffset;
                thumb.RaiseEvent(new DragCompletedEventArgs(0, thumbOffset, canceled: false)
                {
                    RoutedEvent = Thumb.DragCompletedEvent,
                    Source = thumb
                });
                SettleEmptyStateProbeLayout();
            }

            list.ScrollToTop();
            SettleEmptyStateProbeLayout();
            list.Focus();
            WindowValidationInput.RaiseKey(list, Key.PageDown);
            SettleEmptyStateProbeLayout();
            var pageDownOffset = list.VerticalOffset;
            var presses = 1;
            while (presses < 40 && list.VerticalOffset < list.ScrollableHeight - 0.5)
            {
                WindowValidationInput.RaiseKey(list, Key.PageDown);
                SettleEmptyStateProbeLayout();
                presses++;
            }

            var pageDownEndOffset = list.VerticalOffset;
            var pageDownReachesEnd =
                list.ScrollableHeight > 0.5 &&
                Math.Abs(pageDownEndOffset - list.ScrollableHeight) <= 0.5;

            list.ScrollToBottom();
            SettleEmptyStateProbeLayout();
            var lastRow = rows
                .Where(row => row.IsVisible)
                .OrderBy(row => EmptyStateProbeBounds(row, list).Bottom)
                .LastOrDefault();
            var lastRowActions = lastRow is null ? Array.Empty<ButtonBase>() : Actions(lastRow);
            var lastRowActionsVisible = lastRowActions.Length > 0 && lastRowActions.All(InsideList);

            var actionCount = 0;
            var unreachable = new List<string>();
            foreach (var row in rows)
            {
                foreach (var action in Actions(row))
                {
                    actionCount++;
                    action.BringIntoView();
                    SettleEmptyStateProbeLayout();
                    if (!InsideList(action) || !InsideWindow(action))
                    {
                        unreachable.Add(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{AutomationProperties.GetName(action)}@{EmptyStateProbeBounds(action, list)}"));
                    }
                }
            }

            details.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{label}：视口 {list.ViewportHeight:0.#}，可滚 {list.ScrollableHeight:0.#}，滚动条{(scrollBarShown ? "显示" : "隐藏")}，" +
                $"滚轮→{wheelOffset:0.#}，thumb→{thumbOffset:0.#}，PageDown→{pageDownOffset:0.#}" +
                $"（{presses} 次到 {pageDownEndOffset:0.#}），末行操作 {lastRowActions.Length} 个，" +
                $"操作 {actionCount} 个、不可达 {unreachable.Count} 个" +
                $"{(unreachable.Count > 0 ? "：" + string.Join("，", unreachable.Take(4)) : string.Empty)}"));
            return new MainListOverflowMeasurement(
                scrollBarShown,
                wheel.Handled && wheelOffset > 0.5,
                thumbOffset > 0.5,
                pageDownOffset > 0.5,
                pageDownReachesEnd,
                lastRowActionsVisible,
                actionCount > 0 && unreachable.Count == 0);
        }
        finally
        {
            list.ScrollToVerticalOffset(listOffset);
            MainWindowScrollHost.ScrollToHorizontalOffset(hostHorizontalOffset);
            MainWindowScrollHost.ScrollToVerticalOffset(hostVerticalOffset);
            SettleEmptyStateProbeLayout();
        }
    }

    private MainEmptyStateView ReadEmptyStateViewForValidation()
    {
        var countdowns = _workspace == WorkspaceKind.Countdowns;
        Panel content = countdowns ? CountdownEmptyStateContent : NotesEmptyStateContent;
        var message = countdowns ? EmptyCountdownMessage : EmptyNotesMessage;
        var hint = countdowns ? EmptyCountdownHint : EmptyNotesHint;
        var mascot = countdowns ? CountdownEmptyMascot : NotesEmptyMascot;
        var guide = countdowns ? AddEmptyCountdownButton : AddEmptyNoteButton;
        var reset = countdowns ? ResetEmptyCountdownFiltersButton : ResetEmptyNotesFiltersButton;
        var save = countdowns ? SaveCountdownButton : SaveNoteButton;
        guide.ApplyTemplate();
        var surface = guide.Template?.FindName("Surface", guide) as Border;
        return new MainEmptyStateView(
            content.IsVisible,
            message.IsVisible ? message.Text : null,
            hint.IsVisible ? hint.Text : null,
            mascot.IsVisible,
            guide.IsVisible,
            guide.Content as string ?? string.Empty,
            AutomationProperties.GetName(guide),
            EmptyStateProbeBrushIsClear(guide.Background) &&
                EmptyStateProbeBrushIsClear(guide.BorderBrush) &&
                surface is not null &&
                EmptyStateProbeBrushIsClear(surface.Background) &&
                EmptyStateProbeBrushIsClear(surface.BorderBrush),
            reset.IsVisible,
            MainValidationDescendants<ButtonBase>(content)
                .Count(button => button.IsVisible && EmptyStateProbeSaysAdd(button)),
            save.IsVisible && save.IsEnabled && EmptyStateProbeSaysAdd(save));
    }

    private static bool EmptyStateProbeSaysAdd(ButtonBase button) =>
        (button.Content as string ?? string.Empty).Contains("添加", StringComparison.Ordinal) ||
        AutomationProperties.GetName(button).Contains("添加", StringComparison.Ordinal);

    private static bool EmptyStateProbeBrushIsClear(Brush? brush) =>
        brush is null || brush is SolidColorBrush { Color.A: 0 };

    private void RequireEmptyDataForEmptyStateProbe()
    {
        var settings = _desktopWorkspace.Snapshot.Settings;
        if (settings.Countdowns.Count > 0 || settings.Notes.Count > 0)
        {
            throw new InvalidOperationException("空态探针要求从没有任何倒计时与便签的数据开始。");
        }
    }

    private string SaveEmptyStateProbeCountdown(string name, int daysAhead)
    {
        ApplyEmptyStateProbeChange(new SaveCountdownChange(
            null,
            name,
            _desktopWorkspace.Snapshot.UtcNow.AddDays(daysAhead),
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        return _desktopWorkspace.Snapshot.Settings.Countdowns.Single(record => record.Name == name).Id;
    }

    private string SaveEmptyStateProbeNote(string text)
    {
        ApplyEmptyStateProbeChange(new SaveNoteChange(
            null,
            text,
            _desktopWorkspace.Snapshot.BeijingNow.Date,
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        return _desktopWorkspace.Snapshot.Settings.Notes.Single(note => note.Text == text).Id;
    }

    private void ApplyEmptyStateProbeChange(DesktopWorkspaceChange change)
    {
        var result = _desktopWorkspace.Apply(change);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"空态探针写入失败：{result.Error}");
        }

        WindowValidationInput.Pump(Dispatcher);
    }

    private void RestoreAfterEmptyStateProbe(
        WorkspaceKind workspace,
        DesktopWorkspaceFilter filter,
        bool completedSegment,
        IReadOnlyList<string> countdownIds,
        IReadOnlyList<string> noteIds)
    {
        if (noteIds.Count > 0)
        {
            _desktopWorkspace.Apply(new DeleteNotesChange(noteIds.ToArray()));
        }

        foreach (var id in countdownIds)
        {
            _desktopWorkspace.Apply(new DeleteCountdownChange(id));
        }

        if (filter.HasConstraints)
        {
            _desktopWorkspace.ApplyFilter(filter);
        }
        else
        {
            ClearFilters();
        }

        SelectNotesSegment(completedSegment);
        ShowWorkspace(workspace);
        SettleEmptyStateProbeLayout();
    }

    private void SettleEmptyStateProbeLayout()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
        }
    }

    private static Rect EmptyStateProbeBounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(0, 0), element.RenderSize));
}

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

// #248：空态当前显示的内容与动作。
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
    bool FormAddEntryVisible)
{
    public bool ShowsFirstUse(string message, string hint) =>
        Visible &&
        Message == message &&
        Hint == hint &&
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

    // 分段无内容：文案点明是哪个分段空着，不提筛选、不给重置和引导；需要时说明记录在哪个分段。
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
        $"{(Visible ? "显示" : "隐藏")}，文案「{Message ?? "无"}」，说明「{Hint ?? "无"}」，角色{(MascotVisible ? "显示" : "隐藏")}，" +
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
