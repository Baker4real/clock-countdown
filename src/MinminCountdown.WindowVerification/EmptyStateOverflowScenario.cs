using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #273：从产品程序集迁出的空态真实溢出场景。窗口只经窄探针切换工作区；
// 行、滚动条和操作按钮都从 WPF 公共视觉树读取，判定与本文件末尾记录一起属于窗口验证项目。
internal static class EmptyStateOverflowScenario
{
    private static readonly EmptyStateWindowCase[] WindowCases =
    [
        new("864x1000", new Rect(0, 0, 1920, 1032), 1, null),
        new("720x560", new Rect(0, 0, 1920, 1032), 1, new Size(720, 560))
    ];

    public static MainListOverflowProbe Run()
    {
        var probes = new List<MainListOverflowProbe>();
        foreach (var windowCase in WindowCases)
        {
            WithEmptyMainWindow(windowCase, (main, workspace) =>
                probes.Add(ProbeListOverflowScrolling(main, workspace, windowCase.Label)));
        }

        return MainListOverflowProbe.Combine(probes);
    }

    private static MainListOverflowProbe ProbeListOverflowScrolling(
        MainWindow main,
        DesktopWorkspace workspace,
        string sizeLabel)
    {
        RequireEmptyData(workspace);
        var originalFilter = workspace.Snapshot.Filter;
        var originalFocus = Keyboard.FocusedElement;
        var countdownIds = new List<string>();
        var noteIds = new List<string>();
        var details = new List<string>();
        IMainWindowEmptyStateProbe probe = new MainWindowEmptyStateProbe(main);
        try
        {
            for (var index = 1; index <= 12; index++)
            {
                countdownIds.Add(SaveCountdown(
                    workspace,
                    string.Create(CultureInfo.InvariantCulture, $"溢出探针倒计时 {index:00}"),
                    index,
                    main.Dispatcher));
            }

            for (var index = 1; index <= 14; index++)
            {
                noteIds.Add(SaveNote(
                    workspace,
                    string.Create(CultureInfo.InvariantCulture, $"溢出探针便签 {index:00}"),
                    main.Dispatcher));
            }

            main.Activate();
            probe.ShowWorkspace(WorkspaceKind.Countdowns);
            SettleLayout(main);
            var countdownRows = Required<Panel>(main, "CountdownRows")
                .Children
                .OfType<FrameworkElement>()
                .ToArray();
            RequireRowCount(countdownRows.Length, countdownIds.Count, sizeLabel, "倒计时");
            var countdowns = MeasureListOverflow(
                main,
                $"{sizeLabel}-countdowns",
                Required<ScrollViewer>(main, "CountdownListRegion"),
                countdownRows,
                details);

            probe.ShowWorkspace(WorkspaceKind.Notes);
            SettleLayout(main);
            var notesList = Required<ScrollViewer>(main, "NotesListRegion");
            var noteRows = VisualDescendants<Border>(notesList)
                .Where(row => row.Uid == "SelectableNoteRow")
                .ToArray();
            RequireRowCount(noteRows.Length, noteIds.Count, sizeLabel, "便签");
            var notes = MeasureListOverflow(
                main,
                $"{sizeLabel}-notes",
                notesList,
                noteRows,
                details);
            return MainListOverflowProbe.FromMeasurements([countdowns, notes], details);
        }
        finally
        {
            if (noteIds.Count > 0)
            {
                workspace.Apply(new DeleteNotesChange(noteIds.ToArray()));
            }

            foreach (var id in countdownIds)
            {
                workspace.Apply(new DeleteCountdownChange(id));
            }

            workspace.ApplyFilter(originalFilter);
            probe.ShowWorkspace(WorkspaceKind.Countdowns);
            SettleLayout(main);
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

    private static MainListOverflowMeasurement MeasureListOverflow(
        MainWindow main,
        string label,
        ScrollViewer list,
        IReadOnlyList<FrameworkElement> rows,
        ICollection<string> details)
    {
        var host = Required<ScrollViewer>(main, "MainWindowScrollHost");
        var listOffset = list.VerticalOffset;
        var hostHorizontalOffset = host.HorizontalOffset;
        var hostVerticalOffset = host.VerticalOffset;
        static bool Inside(Rect bounds, double width, double height) =>
            !bounds.IsEmpty &&
            bounds.Left >= -0.5 &&
            bounds.Top >= -0.5 &&
            bounds.Right <= width + 0.5 &&
            bounds.Bottom <= height + 0.5;
        bool InsideList(FrameworkElement element) =>
            Inside(Bounds(element, list), list.ViewportWidth, list.ViewportHeight);
        bool InsideWindow(FrameworkElement element) =>
            Inside(Bounds(element, host), host.ViewportWidth, host.ViewportHeight);
        static ButtonBase[] Actions(FrameworkElement row) =>
            VisualDescendants<ButtonBase>(row)
                .Where(action => action.IsVisible && action.IsEnabled)
                .ToArray();
        try
        {
            SettleLayout(main);
            var scrollBarShown =
                list.ScrollableHeight > 0.5 &&
                list.ComputedVerticalScrollBarVisibility == Visibility.Visible;

            list.ScrollToTop();
            SettleLayout(main);
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = list
            };
            list.RaiseEvent(wheel);
            SettleLayout(main);
            var wheelOffset = list.VerticalOffset;

            list.ScrollToTop();
            SettleLayout(main);
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
                SettleLayout(main);
                thumbOffset = list.VerticalOffset;
                thumb.RaiseEvent(new DragCompletedEventArgs(0, thumbOffset, canceled: false)
                {
                    RoutedEvent = Thumb.DragCompletedEvent,
                    Source = thumb
                });
                SettleLayout(main);
            }

            list.ScrollToTop();
            SettleLayout(main);
            list.Focus();
            WindowValidationInput.RaiseKey(list, Key.PageDown);
            SettleLayout(main);
            var pageDownOffset = list.VerticalOffset;
            var presses = 1;
            while (presses < 40 && list.VerticalOffset < list.ScrollableHeight - 0.5)
            {
                WindowValidationInput.RaiseKey(list, Key.PageDown);
                SettleLayout(main);
                presses++;
            }

            var pageDownEndOffset = list.VerticalOffset;
            var pageDownReachesEnd =
                list.ScrollableHeight > 0.5 &&
                Math.Abs(pageDownEndOffset - list.ScrollableHeight) <= 0.5;

            list.ScrollToBottom();
            SettleLayout(main);
            var lastRow = rows
                .Where(row => row.IsVisible)
                .OrderBy(row => Bounds(row, list).Bottom)
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
                    SettleLayout(main);
                    if (!InsideList(action) || !InsideWindow(action))
                    {
                        unreachable.Add(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{AutomationProperties.GetName(action)}@{Bounds(action, list)}"));
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
            host.ScrollToHorizontalOffset(hostHorizontalOffset);
            host.ScrollToVerticalOffset(hostVerticalOffset);
            SettleLayout(main);
        }
    }

    private static void WithEmptyMainWindow(
        EmptyStateWindowCase windowCase,
        Action<MainWindow, DesktopWorkspace> probe)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-window-verification-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new FixedClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(windowCase.WorkArea, windowCase.Dpi, windowCase.Dpi));
            var main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory);
            PrepareOffscreenWindow(main);
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                if (windowCase.UserSize is { } userSize)
                {
                    var handle = new WindowInteropHelper(main).Handle;
                    NativeWindowMessage.Send(
                        handle,
                        NativeWindowMessage.WmEnterSizeMove,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    main.Width = userSize.Width;
                    main.Height = userSize.Height;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    NativeWindowMessage.Send(
                        handle,
                        NativeWindowMessage.WmExitSizeMove,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                }

                probe(main, workspace);
            }
            finally
            {
                main.Close();
                WindowValidationInput.Pump(main.Dispatcher);
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static void RequireRowCount(int actual, int expected, string sizeLabel, string kind)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{sizeLabel} {kind}行数 {actual}，期望 {expected}；场景未覆盖全部新建记录。");
        }
    }

    private static T Required<T>(MainWindow main, string name) where T : DependencyObject =>
        main.FindName(name) as T ??
        throw new InvalidOperationException($"管理窗口缺少空态验证元素 {name}（{typeof(T).Name}）。");

    private static void PrepareOffscreenWindow(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;
        window.Top = -20_000;
        window.Opacity = 0;
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record EmptyStateWindowCase(
        string Label,
        Rect WorkArea,
        double Dpi,
        Size? UserSize);
}
