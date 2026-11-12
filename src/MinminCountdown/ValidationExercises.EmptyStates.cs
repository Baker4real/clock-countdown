using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using MinminCountdown.Core;

namespace MinminCountdown;

// #248：管理窗口空态与列表滚动边界的隔离窗口编排。每个窗口从空数据起步、注入工作区与窗口尺寸，测量与判定在
// 窗口的场景 partial（WindowValidationScenarios.EmptyStates）里，这里只排窗口档并汇总。
internal static partial class ValidationExercises
{
    // 首选尺寸、工作区缩小（125%/150% 缩放下的 1080p 工作区）、最小尺寸 720×560、真实 WM_DPICHANGED 各一个
    // 窗口；DPI 档同时覆盖文字放大（经典 WPF 按窗口 DPI 光栅化文字，见 #134 捕获 seam）。
    private static readonly EmptyStateWindowCase[] EmptyStateWindowCases =
    [
        new("864x1000", new Rect(0, 0, 1920, 1032), 1, null, false, true, new Size(864, 1000)),
        new("864x802-125", new Rect(0, 0, 1536, 826), 1.25, null, false, false, new Size(864, 802)),
        new("864x664-150", new Rect(0, 0, 1280, 688), 1.5, null, false, false, new Size(864, 664)),
        new("720x560", new Rect(0, 0, 1920, 1032), 1, new Size(720, 560), false, true, new Size(720, 560)),
        new("dpi", new Rect(0, 0, 1920, 1032), 1, null, true, false, new Size(864, 1000))
    ];

    // 每个窗口量 6 种空态：倒计时首次与筛选无结果，便签首次、筛选无结果、已完成分段无内容、未完成分段无内容。
    private const int EmptyStateMeasurementsPerWindow = 6;

    // #248：空态在列表实际视口里居中且完整可见，没有虚假滚动范围；首选与最小尺寸窗口的空态逐张实拍。
    public static MainEmptyStateViewportProbe RunMainEmptyStateViewportBehavior(
        Action<MainWindow, string>? captureEvidence = null)
    {
        var failures = new List<string>();
        var measured = 0;
        foreach (var windowCase in EmptyStateWindowCases)
        {
            WithEmptyMainWindow(windowCase, (main, injection) =>
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
                var geometries = main.MeasureEmptyStateGeometriesForValidation(label, revealed);
                measured += geometries.Count;
                failures.AddRange(geometries.SelectMany(geometry => geometry.Failures()));
            });
        }

        return new MainEmptyStateViewportProbe(
            measured,
            EmptyStateWindowCases.Length * EmptyStateMeasurementsPerWindow,
            failures);
    }

    // #248：三种空态的文案与动作、轻量填写引导与唯一添加入口（首选尺寸窗口）。
    public static MainEmptyStateGuidanceProbe RunMainEmptyStateGuidanceBehavior()
    {
        MainEmptyStateGuidanceProbe? probe = null;
        WithEmptyMainWindow(EmptyStateWindowCases[0], (main, _) => probe = main.ProbeEmptyStateGuidanceForValidation());
        return probe ?? throw new InvalidOperationException("空态引导探针没有产出结果。");
    }

    // 空数据的隔离管理窗口：注入工作区、离屏不激活显示；需要时按用户拖拽路径调成指定尺寸，或注入一次真实
    // WM_DPICHANGED（窗口句柄须在 PerMonitorV2 线程上下文里创建）。收尾关窗、恢复线程 DPI 上下文并删临时设置。
    private static void WithEmptyMainWindow(
        EmptyStateWindowCase windowCase,
        Action<MainWindow, DpiChangeInjection?> probe)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-empty-states-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var previousDpiContext = windowCase.RaiseDpi
            ? MonitorGeometryValidation.EnterPerMonitorV2ThreadDpiContextForValidation()
            : IntPtr.Zero;
        try
        {
            var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(windowCase.WorkArea, windowCase.Dpi, windowCase.Dpi));
            var main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                if (windowCase.UserSize is { } userSize)
                {
                    // 与 #211 相同，经 WM_ENTERSIZEMOVE→几何变化→WM_EXITSIZEMOVE 标记为用户调整的尺寸。
                    var handle = new WindowInteropHelper(main).Handle;
                    NativeWorkAreaProbe.SendMessage(handle, NativeWorkAreaProbe.WmEnterSizeMove, IntPtr.Zero, IntPtr.Zero);
                    main.Width = userSize.Width;
                    main.Height = userSize.Height;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    NativeWorkAreaProbe.SendMessage(handle, NativeWorkAreaProbe.WmExitSizeMove, IntPtr.Zero, IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                }

                var injection = windowCase.RaiseDpi && previousDpiContext != IntPtr.Zero
                    ? MonitorGeometryValidation.InjectDpiChangeForValidation(main, 1.5)
                    : null;
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                probe(main, injection);
            }
            finally
            {
                main.Close();
                WindowValidationInput.Pump(main.Dispatcher);
            }
        }
        finally
        {
            MonitorGeometryValidation.RestoreThreadDpiContextForValidation(previousDpiContext);
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private sealed record EmptyStateWindowCase(
        string Label,
        Rect WorkArea,
        double Dpi,
        Size? UserSize,
        bool RaiseDpi,
        bool Capture,
        Size ExpectedWindow);
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
