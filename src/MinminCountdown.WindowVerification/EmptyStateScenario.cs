using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #248/#274：管理窗口空态的隔离编排。窗口工作区只经 #273 的窄探针切换；
// 筛选、分段、重置、引导及全部观察都走公开 WPF 控件行为与视觉树。
internal static class EmptyStateScenario
{
    private const string NoMatchSearch = "__empty_state_probe_no_match__";
    private const int MeasurementsPerWindow = 6;

    private static readonly EmptyStateWindowCase[] WindowCases =
    [
        new("864x1000", new Rect(0, 0, 1920, 1032), 1, null, false, true, new Size(864, 1000)),
        new("864x802-125", new Rect(0, 0, 1536, 826), 1.25, null, false, false, new Size(864, 802)),
        new("864x664-150", new Rect(0, 0, 1280, 688), 1.5, null, false, false, new Size(864, 664)),
        new("720x560", new Rect(0, 0, 1920, 1032), 1, new Size(720, 560), false, true, new Size(720, 560)),
        new("dpi", new Rect(0, 0, 1920, 1032), 1, null, true, false, new Size(864, 1000))
    ];

    public static MainEmptyStateViewportProbe RunViewport()
    {
        var failures = new List<string>();
        var measured = 0;
        var captureEvidence = CreateEvidenceWriter();
        foreach (var windowCase in WindowCases)
        {
            WithEmptyMainWindow(windowCase, (main, workspace, injection) =>
            {
                var label = windowCase.Label;
                if (windowCase.RaiseDpi)
                {
                    Console.Error.WriteLine(
                        $"EMPTY_STATE_DPI_PROBE: before={injection?.BeforePixelsPerInch}; after={injection?.AfterPixelsPerInch}; target={injection?.Target}");
                    if (injection is not { Reached: true })
                    {
                        failures.Add($"{label}：WM_DPICHANGED 注入未到达目标（{injection?.ToString() ?? "未注入"}）");
                    }

                    label = string.Create(CultureInfo.InvariantCulture, $"dpi-{injection?.Target ?? 0}");
                }

                if (Math.Abs(main.ActualWidth - windowCase.ExpectedWindow.Width) > 0.5 ||
                    Math.Abs(main.ActualHeight - windowCase.ExpectedWindow.Height) > 0.5)
                {
                    failures.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{label}：窗口实际 {main.ActualWidth:0.##}×{main.ActualHeight:0.##}，" +
                        $"期望 {windowCase.ExpectedWindow.Width:0.##}×{windowCase.ExpectedWindow.Height:0.##}"));
                }

                Action<string>? revealed = windowCase.Capture && captureEvidence is not null
                    ? state => captureEvidence(main, state)
                    : null;
                var geometries = MeasureEmptyStateGeometries(main, workspace, label, revealed);
                measured += geometries.Count;
                failures.AddRange(geometries.SelectMany(geometry => geometry.Failures()));
            });
        }

        return new MainEmptyStateViewportProbe(measured, WindowCases.Length * MeasurementsPerWindow, failures);
    }

    public static MainEmptyStateGuidanceProbe RunGuidance()
    {
        MainEmptyStateGuidanceProbe? probe = null;
        WithEmptyMainWindow(WindowCases[0], (main, workspace, _) => probe = ProbeGuidance(main, workspace));
        return probe ?? throw new InvalidOperationException("空态引导探针没有产出结果。");
    }

    private static IReadOnlyList<MainEmptyStateGeometry> MeasureEmptyStateGeometries(
        MainWindow main,
        DesktopWorkspace workspace,
        string sizeLabel,
        Action<string>? revealed)
    {
        RequireEmptyData(workspace);
        IMainWindowEmptyStateProbe probe = new MainWindowEmptyStateProbe(main);
        var originalWorkspace = CurrentWorkspace(main);
        var originalFilter = workspace.Snapshot.Filter;
        var originalSegment = Required<ToggleButton>(main, "CompletedNotesSegment").IsChecked == true;
        var measurements = new List<MainEmptyStateGeometry>();
        var countdownIds = new List<string>();
        var noteIds = new List<string>();

        void Measure(WorkspaceKind workspaceKind, string state)
        {
            probe.ShowWorkspace(workspaceKind);
            var workspaceName = workspaceKind == WorkspaceKind.Countdowns ? "countdowns" : "notes";
            measurements.Add(MeasureEmptyStateGeometry(
                main,
                $"{sizeLabel}-{workspaceName}-{state}",
                revealed));
        }

        try
        {
            Measure(WorkspaceKind.Countdowns, "first-use");
            Measure(WorkspaceKind.Notes, "first-use");
            countdownIds.Add(SaveCountdown(workspace, "空态几何探针倒计时", 7, Application.Current.Dispatcher));
            noteIds.Add(SaveNote(workspace, "空态几何探针便签", Application.Current.Dispatcher));
            workspace.ApplyFilter(
                DesktopWorkspaceFilter.Empty with { CountdownStatus = CountdownStatusFilter.Expired });
            Measure(WorkspaceKind.Countdowns, "filter-no-results");
            workspace.ApplyFilter(
                DesktopWorkspaceFilter.Empty with { NoteSearch = NoMatchSearch });
            Measure(WorkspaceKind.Notes, "filter-no-results");
            Click(main, "ResetEmptyNotesFiltersButton");
            SelectNotesSegment(main, completed: true);
            Measure(WorkspaceKind.Notes, "completed-segment-empty");
            ApplyChange(workspace, new SetNoteCompletionChange(noteIds[0], true));
            SelectNotesSegment(main, completed: false);
            Measure(WorkspaceKind.Notes, "active-segment-empty");
            return measurements;
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
            Required<TextBox>(main, "NoteSearch").Text = originalFilter.NoteSearch ?? string.Empty;
            SelectNotesSegment(main, originalSegment);
            probe.ShowWorkspace(originalWorkspace);
            SettleLayout(main);
        }
    }

    private static MainEmptyStateGeometry MeasureEmptyStateGeometry(
        MainWindow main,
        string label,
        Action<string>? revealed)
    {
        var countdowns = CurrentWorkspace(main) == WorkspaceKind.Countdowns;
        var list = Required<ScrollViewer>(main, countdowns ? "CountdownListRegion" : "NotesListRegion");
        var content = Required<Panel>(main, countdowns ? "CountdownEmptyStateContent" : "NotesEmptyStateContent");
        var block = countdowns
            ? content
            : content.Children.OfType<FrameworkElement>().Single();
        var host = Required<ScrollViewer>(main, "MainWindowScrollHost");
        var listOffset = list.VerticalOffset;
        var hostHorizontalOffset = host.HorizontalOffset;
        var hostVerticalOffset = host.VerticalOffset;
        try
        {
            SettleLayout(main);
            var visible = content.IsVisible && block.ActualWidth > 0 && block.ActualHeight > 0;
            const double areaTop = 0;
            var geometry = new MainEmptyStateGeometry(
                label,
                visible,
                Required<TextBlock>(main, countdowns ? "EmptyCountdownMessage" : "EmptyNotesMessage").Text,
                list.ViewportWidth,
                list.ViewportHeight,
                list.ExtentHeight,
                list.ScrollableHeight,
                list.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                visible ? Bounds(block, list) : Rect.Empty,
                new Rect(0, areaTop, list.ViewportWidth, Math.Max(0, list.ViewportHeight - areaTop)),
                Rect.Empty,
                list.VerticalOffset,
                new Size(host.ViewportWidth, host.ViewportHeight));
            if (!visible)
            {
                return geometry;
            }

            block.BringIntoView();
            SettleLayout(main);
            geometry = geometry with
            {
                BlockInWindow = Bounds(block, host),
                ListOffset = list.VerticalOffset
            };
            revealed?.Invoke(label);
            return geometry;
        }
        finally
        {
            list.ScrollToVerticalOffset(listOffset);
            host.ScrollToHorizontalOffset(hostHorizontalOffset);
            host.ScrollToVerticalOffset(hostVerticalOffset);
            SettleLayout(main);
        }
    }

    private static MainEmptyStateGuidanceProbe ProbeGuidance(MainWindow main, DesktopWorkspace workspace)
    {
        RequireEmptyData(workspace);
        IMainWindowEmptyStateProbe probe = new MainWindowEmptyStateProbe(main);
        var originalWorkspace = CurrentWorkspace(main);
        var originalFilter = workspace.Snapshot.Filter;
        var originalSegment = Required<ToggleButton>(main, "CompletedNotesSegment").IsChecked == true;
        var originalFocus = Keyboard.FocusedElement;
        var originalCountdownName = Required<TextBox>(main, "CountdownName").Text;
        var originalNoteText = Required<TextBox>(main, "NoteText").Text;
        var countdownIds = new List<string>();
        var noteIds = new List<string>();
        var details = new List<string>();

        MainEmptyStateView Read(WorkspaceKind workspaceKind, string state)
        {
            probe.ShowWorkspace(workspaceKind);
            SettleLayout(main);
            var view = ReadEmptyStateView(main);
            details.Add($"{state}：{view.Describe()}");
            return view;
        }

        bool GuideKeepsDraft(string state, string guideName, string editorName, string draft, Func<int> savedCount)
        {
            var editor = Required<TextBox>(main, editorName);
            editor.Text = draft;
            Click(main, guideName);
            SettleLayout(main);
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
            main.Activate();
            var countdownFirstUse = Read(WorkspaceKind.Countdowns, "倒计时首次");
            var countdownGuideKeepsDraft = GuideKeepsDraft(
                "倒计时引导",
                "AddEmptyCountdownButton",
                "CountdownName",
                "空态引导保留的倒计时草稿",
                () => workspace.Snapshot.Settings.Countdowns.Count);
            Required<TextBox>(main, "CountdownName").Text = originalCountdownName;
            var noteFirstUse = Read(WorkspaceKind.Notes, "便签首次");
            var noteGuideKeepsDraft = GuideKeepsDraft(
                "便签引导",
                "AddEmptyNoteButton",
                "NoteText",
                "空态引导保留的便签草稿",
                () => workspace.Snapshot.Settings.Notes.Count);
            Required<TextBox>(main, "NoteText").Text = originalNoteText;

            countdownIds.Add(SaveCountdown(workspace, "空态引导探针倒计时", 7, Application.Current.Dispatcher));
            noteIds.Add(SaveNote(workspace, "空态引导探针便签", Application.Current.Dispatcher));

            probe.ShowWorkspace(WorkspaceKind.Countdowns);
            Click(main, "CountdownExpiredFilter");
            var countdownFiltered = Read(WorkspaceKind.Countdowns, "倒计时筛选无结果");
            Click(main, "ResetEmptyCountdownFiltersButton");
            SettleLayout(main);
            var countdownResetRestores =
                !workspace.Snapshot.Filter.HasConstraints &&
                Required<Panel>(main, "CountdownRows").Children.Count > 0 &&
                !Required<Panel>(main, "CountdownEmptyStateContent").IsVisible;

            probe.ShowWorkspace(WorkspaceKind.Notes);
            Required<TextBox>(main, "NoteSearch").Text = NoMatchSearch;
            var noteFiltered = Read(WorkspaceKind.Notes, "便签筛选无结果");
            Click(main, "ResetEmptyNotesFiltersButton");
            SettleLayout(main);
            var noteResetRestores =
                !workspace.Snapshot.Filter.HasConstraints &&
                string.IsNullOrEmpty(Required<TextBox>(main, "NoteSearch").Text) &&
                Required<Panel>(main, "ActiveNoteRows").IsVisible &&
                Required<Panel>(main, "ActiveNoteRows").Children.Count > 0 &&
                !Required<Panel>(main, "NotesEmptyStateContent").IsVisible;
            details.Add($"重置：倒计时恢复结果 {countdownResetRestores}，便签恢复结果 {noteResetRestores}");

            SelectNotesSegment(main, completed: true);
            var completedSegmentEmpty = Read(WorkspaceKind.Notes, "已完成分段无内容");
            ApplyChange(workspace, new SetNoteCompletionChange(noteIds[0], true));
            SelectNotesSegment(main, completed: false);
            var activeSegmentEmpty = Read(WorkspaceKind.Notes, "未完成分段无内容");

            var views = new[]
            {
                countdownFirstUse, noteFirstUse, countdownFiltered, noteFiltered, completedSegmentEmpty, activeSegmentEmpty
            };
            return new MainEmptyStateGuidanceProbe(
                countdownFirstUse.ShowsFirstUse("还没有倒计时", "把重要的时刻记下来，从一个目标开始吧。"),
                noteFirstUse.ShowsFirstUse("还没有便签", "先写下一件想记住的事。"),
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
            DeleteAllNotes(workspace);
            DeleteAllCountdowns(workspace);
            workspace.ApplyFilter(originalFilter);
            Required<TextBox>(main, "NoteSearch").Text = originalFilter.NoteSearch ?? string.Empty;
            SelectNotesSegment(main, originalSegment);
            Required<TextBox>(main, "CountdownName").Text = originalCountdownName;
            Required<TextBox>(main, "NoteText").Text = originalNoteText;
            probe.ShowWorkspace(originalWorkspace);
            if (originalFocus is not null)
            {
                Keyboard.Focus(originalFocus);
            }
            else
            {
                Keyboard.ClearFocus();
            }
            SettleLayout(main);
        }
    }

    private static MainEmptyStateView ReadEmptyStateView(MainWindow main)
    {
        var countdowns = CurrentWorkspace(main) == WorkspaceKind.Countdowns;
        var content = Required<Panel>(main, countdowns ? "CountdownEmptyStateContent" : "NotesEmptyStateContent");
        var message = Required<TextBlock>(main, countdowns ? "EmptyCountdownMessage" : "EmptyNotesMessage");
        var hint = Required<TextBlock>(main, countdowns ? "EmptyCountdownHint" : "EmptyNotesHint");
        var mascot = Required<Image>(main, countdowns ? "CountdownEmptyMascot" : "NotesEmptyMascot");
        var guide = Required<Button>(main, countdowns ? "AddEmptyCountdownButton" : "AddEmptyNoteButton");
        var reset = Required<Button>(main, countdowns ? "ResetEmptyCountdownFiltersButton" : "ResetEmptyNotesFiltersButton");
        var save = Required<Button>(main, countdowns ? "SaveCountdownButton" : "SaveNoteButton");
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
            BrushIsClear(guide.Background) &&
                BrushIsClear(guide.BorderBrush) &&
                surface is not null &&
                BrushIsClear(surface.Background) &&
                BrushIsClear(surface.BorderBrush),
            reset.IsVisible,
            VisualDescendants<ButtonBase>(content)
                .Count(button => button.IsVisible && SaysAdd(button)),
            save.IsVisible && save.IsEnabled && SaysAdd(save),
            message.TextWrapping == TextWrapping.Wrap);
    }

    private static bool SaysAdd(ButtonBase button) =>
        (button.Content as string ?? string.Empty).Contains("添加", StringComparison.Ordinal) ||
        AutomationProperties.GetName(button).Contains("添加", StringComparison.Ordinal);

    private static bool BrushIsClear(Brush? brush) =>
        brush is null || brush is SolidColorBrush { Color.A: 0 };

    private static void WithEmptyMainWindow(
        EmptyStateWindowCase windowCase,
        Action<MainWindow, DesktopWorkspace, DpiChangeInjection?> probe)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-empty-state-window-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var previousDpiContext = windowCase.RaiseDpi
            ? MonitorGeometryValidation.EnterPerMonitorV2ThreadDpiContextForValidation()
            : IntPtr.Zero;
        MainWindow? main = null;
        try
        {
            var clock = new FixedClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(windowCase.WorkArea, windowCase.Dpi, windowCase.Dpi));
            main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            main.Show();
            SettleLayout(main);
            if (windowCase.UserSize is { } userSize)
            {
                // 与 #211 相同，经 WM_ENTERSIZEMOVE→几何变化→WM_EXITSIZEMOVE 标记为用户调整的尺寸。
                var handle = new WindowInteropHelper(main).Handle;
                NativeWindowMessage.Send(handle, NativeWindowMessage.WmEnterSizeMove, IntPtr.Zero, IntPtr.Zero);
                main.Width = userSize.Width;
                main.Height = userSize.Height;
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                NativeWindowMessage.Send(handle, NativeWindowMessage.WmExitSizeMove, IntPtr.Zero, IntPtr.Zero);
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
            }

            var injection = windowCase.RaiseDpi && previousDpiContext != IntPtr.Zero
                ? MonitorGeometryValidation.InjectDpiChangeForValidation(main, 1.5)
                : null;
            WindowValidationInput.Pump(main.Dispatcher);
            main.UpdateLayout();
            probe(main, workspace, injection);
        }
        finally
        {
            if (main is not null)
            {
                main.Close();
                WindowValidationInput.Pump(main.Dispatcher);
            }
            MonitorGeometryValidation.RestoreThreadDpiContextForValidation(previousDpiContext);
            DeleteSettingsFiles(settingsPath);
            if (File.Exists(diagnosticPath))
            {
                File.Delete(diagnosticPath);
            }
        }
    }

    private static Action<MainWindow, string>? CreateEvidenceWriter()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        var directory = Path.Combine(
            Path.GetFullPath(sourceRoot),
            "logs",
            "ui-captures",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-empty-states");
        Directory.CreateDirectory(directory);
        return (window, label) =>
        {
            if (window.Content is not FrameworkElement root)
            {
                throw new InvalidOperationException("空态实拍缺少管理窗口根视觉。");
            }

            _ = WindowValidationInput.PumpRenderedFrames(
                root.Dispatcher,
                1000,
                () => window.VisibleSegmentIndicatorsSettledForCapture);
            WriteNewBytes(Path.Combine(directory, label + ".png"), EncodePng(CaptureVisual(root)));
        };
    }

    private static RenderTargetBitmap CaptureVisual(FrameworkElement visual)
    {
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        var window = Window.GetWindow(visual);
        if (window is not null && window.Opacity == 0)
        {
            window.Opacity = 0.01;
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
        }

        visual.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var scrollStates = VisualDescendants<ScrollViewer>(visual)
            .Select(scroll => (Scroll: scroll, Horizontal: scroll.HorizontalOffset, Vertical: scroll.VerticalOffset))
            .ToArray();
        _ = CaptureVisualAtFixedSize(visual, width, height);
        foreach (var state in scrollStates)
        {
            state.Scroll.ScrollToHorizontalOffset(state.Horizontal);
            state.Scroll.ScrollToVerticalOffset(state.Vertical);
        }

        WindowValidationInput.Pump(visual.Dispatcher);
        visual.UpdateLayout();
        var bitmap = RenderVisualToBitmap(visual, width, height);
        for (var attempt = 0; attempt < 4 && IsEntirelyTransparent(bitmap) && window is not null; attempt++)
        {
            System.Threading.Thread.Sleep(Math.Min(100 << attempt, 500));
            window.Left = -20_000;
            window.Top = -20_000;
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            bitmap = RenderVisualToBitmap(visual, width, height);
        }

        if (IsEntirelyTransparent(bitmap))
        {
            throw new InvalidOperationException(
                "空态实拍整幅透明：" +
                $"visual={visual.GetType().Name} actual={visual.ActualWidth}x{visual.ActualHeight} " +
                $"window={window?.GetType().Name ?? "none"}");
        }

        return bitmap;
    }

    private static RenderTargetBitmap CaptureVisualAtFixedSize(FrameworkElement visual, int width, int height)
    {
        var size = new Size(width, height);
        visual.Measure(size);
        visual.Arrange(new Rect(new Point(0, 0), size));
        return RenderVisualToBitmap(visual, width, height);
    }

    private static RenderTargetBitmap RenderVisualToBitmap(FrameworkElement visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsEntirelyTransparent(BitmapSource source)
    {
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteNewBytes(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static WorkspaceKind CurrentWorkspace(MainWindow main) =>
        Required<ToggleButton>(main, "NotesTab").IsChecked == true ? WorkspaceKind.Notes : WorkspaceKind.Countdowns;

    private static void SelectNotesSegment(MainWindow main, bool completed)
    {
        var name = completed ? "CompletedNotesSegment" : "ActiveNotesSegment";
        if (Required<ToggleButton>(main, name).IsChecked != true)
        {
            Click(main, name);
        }
        SettleLayout(main);
    }

    private static void Click(MainWindow main, string name) =>
        Required<ButtonBase>(main, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static T Required<T>(MainWindow main, string name) where T : class =>
        main.FindName(name) as T ?? throw new InvalidOperationException($"空态窗口缺少控件 {name}（{typeof(T).Name}）");

    private static void DeleteAllCountdowns(DesktopWorkspace workspace)
    {
        foreach (var countdown in workspace.Snapshot.Settings.Countdowns)
        {
            ApplyChange(workspace, new DeleteCountdownChange(countdown.Id));
        }
    }

    private static void DeleteAllNotes(DesktopWorkspace workspace)
    {
        var notes = workspace.Snapshot.Settings.Notes.Select(note => note.Id).ToArray();
        if (notes.Length > 0)
        {
            ApplyChange(workspace, new DeleteNotesChange(notes));
        }
    }

    private static void ApplyChange(DesktopWorkspace workspace, DesktopWorkspaceChange change) =>
        EmptyStateScenarioSupport.Apply(workspace, change, Application.Current.Dispatcher);

    private sealed record EmptyStateWindowCase(
        string Label,
        Rect WorkArea,
        double Dpi,
        Size? UserSize,
        bool RaiseDpi,
        bool Capture,
        Size ExpectedWindow);
}
