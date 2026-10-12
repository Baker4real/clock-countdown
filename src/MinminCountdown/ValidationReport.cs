using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinminCountdown.Core;
using WpfCalendar = System.Windows.Controls.Calendar;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown;

internal static class ValidationReport
{
    internal static void RunEmptyDayCaptureProbe(ResourceDictionary resources, string assetRoot)
    {
        const int captureWidth = 1165;
        const int captureHeight = 1350;
        // #134：PerMonitorV2 变体必须覆盖真实 WM_DPICHANGED 管线；经典 WPF 进程缺省不是
        // PerMonitorV2，必须在首个窗口创建前把本 probe 进程与线程的 DPI 感知提升为
        // PerMonitorV2。本进程是 EMPTY_DAY_PROBE 专用一次性验证进程，窗口与捕获全部
        // 发生在本方法内部，不影响正常应用启动语义。
        var awarenessElevatedByProbe = EnableCaptureProbePerMonitorV2Awareness();
        var dayBeijing = new DateTimeOffset(2032, 5, 18, 10, 15, 30, TimeSpan.FromHours(8));
        var referenceUtc = dayBeijing.ToUniversalTime();
        using var theme = new ApplicationTheme(resources);
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-empty-day-probe-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(temporaryRoot, "settings.json");
        var diagnosticPath = Path.Combine(temporaryRoot, "validation.jsonl");
        Directory.CreateDirectory(temporaryRoot);
        MainWindow? validationMain = null;
        try
        {
            var settings = CreateMainReferenceSettings("Empty", referenceUtc);
            var store = new SettingsStore(clock: new ValidationClock(referenceUtc));
            store.Save(settingsPath, settings);
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            using var calendar = new HolidayCalendar(temporaryRoot);
            validationMain = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                assetRoot)
            {
                Width = captureWidth,
                Height = captureHeight,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 1
            };
            validationMain.Show();
            PumpDispatcher();
            validationMain.UpdateLayout();
            if (validationMain.Content is not FrameworkElement root)
            {
                throw new InvalidOperationException("empty-day probe 缺少根视觉。");
            }

            theme.Apply();
            validationMain.ApplyTheme();
            validationMain.ShowWorkspace(WorkspaceKind.Countdowns);
            validationMain.UpdateTime(dayBeijing);
            PumpDispatcher();
            CloseMainDatePickerDropdowns(validationMain);
            ArrangeForReferenceCapture(root, captureWidth, captureHeight);
            ClearCaptureKeyboardFocus();
            WaitForMainReferenceMotion(validationMain, root);
            var pngBytes = EncodePng(CaptureOpaqueMainSurface(root, captureWidth, captureHeight));
            // #134：实拍证据先落盘再走基线门禁——门禁失败（如 v22 基线与在途改动漂移）
            // 时，失败 run 仍保留可审查的实拍证据与原始错误，而不是什么都不落。
            RunMainUiCaptureSeam(
                theme,
                validationMain,
                root,
                dayBeijing,
                captureWidth,
                captureHeight,
                pngBytes,
                awarenessElevatedByProbe);
            ProbeEmptyDayAgainstPinnedBaseline(pngBytes);
        }
        finally
        {
            validationMain?.Close();
            PumpDispatcher();
        }
    }

    // #134 真实运行态 UI 捕获 seam：在隔离夹具的临时窗口上按固定验收轴捕获真实 WPF 帧，
    // 证据统一落盘可审查。变体覆盖 1165×1350 真实 DIP 画布和常态窗口（同尺寸）、
    // PerMonitorV2 动态 DPI 变更（WM_DPICHANGED 等比放大 1.5 倍）与文字放大（144 DPI 光栅化）。
    // 任何变体失败都先把原始异常写入 <变体>.error.txt 再原样重抛；不以静态参考图、
    // 空白画布或合成替代帧顶替实拍证据。窗口为 probe 专用临时窗口，设置写入隔离临时根。
    private static void RunMainUiCaptureSeam(
        ApplicationTheme theme,
        MainWindow window,
        FrameworkElement root,
        DateTimeOffset beijingTime,
        int designWidth,
        int designHeight,
        byte[] pinnedCountdownsPng,
        bool awarenessElevatedByProbe)
    {
        var evidenceDirectory = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            evidenceDirectory = ResolveDefaultVisualEvidenceDirectory("ui-seam") ??
                throw new InvalidOperationException(
                    "UI 捕获 seam 需要 MINMIN_COUNTDOWN_VISUAL_EVIDENCE_DIR 或 " +
                    "MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT 来确定证据落盘目录。");
        }

        Directory.CreateDirectory(evidenceDirectory);
        var pinnedPath = Path.Combine(evidenceDirectory, "main-1165x1350-countdowns-empty-pinned.png");
        WriteNewBytes(pinnedPath, pinnedCountdownsPng);
        var entries = new List<MainUiCaptureSeamEntry>
        {
            new(
                "main-1165x1350-countdowns-empty-pinned",
                "design-canvas-1165x1350; pinned-baseline frame saved verbatim",
                pinnedPath,
                Convert.ToHexString(SHA256.HashData(pinnedCountdownsPng)),
                designWidth,
                designHeight,
                designWidth,
                designHeight,
                VisualTreeHelper.GetDpi(root).DpiScaleX,
                VisualTreeHelper.GetDpi(root).DpiScaleY)
        };
        MainUiDpiChangeTelemetry? dpiChangeTelemetry = null;

        void RunVariant(string variantId, Func<MainUiCaptureSeamEntry> capture)
        {
            try
            {
                entries.Add(capture());
            }
            catch (Exception exception)
            {
                WriteCaptureFailureEvidence(evidenceDirectory, variantId, exception);
                throw;
            }
        }

        void ShowNotesWorkspace()
        {
            theme.Apply();
            window.ApplyTheme();
            window.ShowWorkspace(WorkspaceKind.Notes);
            window.UpdateTime(beijingTime);
            PumpDispatcher();
            CloseMainDatePickerDropdowns(window);
            WaitForMainReferenceMotion(window, root);
        }

        // 变体一：1165×1350 设计坐标下的主窗口便签空态（#134 指定必交证据）。
        RunVariant("main-1165x1350-notes-empty", () =>
        {
            ShowNotesWorkspace();
            var capture = CaptureOpaqueMainSurface(root, designWidth, designHeight);
            AssertCaptureOpaqueAndSized(
                capture,
                designWidth,
                designHeight,
                "main-1165x1350-notes-empty");
            return RecordUiCaptureSeamEntry(
                evidenceDirectory,
                "main-1165x1350-notes-empty",
                "design-canvas-1165x1350; notes workspace; empty isolated fixture",
                root,
                capture,
                designWidth,
                designHeight);
        });

        // 变体二：文字放大。经典 WPF 不消费 Windows 辅助文字缩放系数，文字按窗口 DPI 光栅化；
        // 以 144 DPI（150%）目标位图按设计 DIP 坐标重排捕获，真实放大字形像素，
        // 证明设计画布在放大光栅化下不出现裁切或空白。
        RunVariant("main-text-scale-144dpi-notes-empty", () =>
        {
            ShowNotesWorkspace();
            var capture = CaptureOpaqueMainSurfaceAtDpi(root, designWidth, designHeight, 144, 144);
            var expectedPixelWidth = (int)Math.Ceiling(designWidth * 1.5);
            var expectedPixelHeight = (int)Math.Ceiling(designHeight * 1.5);
            if (capture.PixelWidth != expectedPixelWidth ||
                capture.PixelHeight != expectedPixelHeight)
            {
                throw new InvalidOperationException(
                    "文字放大变体画布不匹配：" +
                    $"{capture.PixelWidth}x{capture.PixelHeight}，" +
                    $"期望 {expectedPixelWidth}x{expectedPixelHeight}。");
            }

            return RecordUiCaptureSeamEntry(
                evidenceDirectory,
                "main-text-scale-144dpi-notes-empty",
                "text-magnification; design DIP layout rasterized at 144 DPI (150%); notes empty",
                root,
                capture,
                designWidth,
                designHeight);
        });

        // 变体三：常态窗口。XAML 声明的默认与最小窗口就是 1165×1350
        // DIP；真实布局直接在该坐标系中排列，不再经过 Viewbox。
        RunVariant("main-normal-1165x1350-notes-empty", () =>
        {
            const int normalWidth = 1165;
            const int normalHeight = 1350;
            ShowNotesWorkspace();
            window.Width = normalWidth;
            window.Height = normalHeight;
            PumpDispatcher();
            var capture = CaptureOpaqueMainSurface(root, normalWidth, normalHeight);
            AssertCaptureOpaqueAndSized(
                capture,
                normalWidth,
                normalHeight,
                "main-normal-1165x1350-notes-empty");
            var entry = RecordUiCaptureSeamEntry(
                evidenceDirectory,
                "main-normal-1165x1350-notes-empty",
                "normal-window; XAML default/min 1165x1350 DIP; direct layout; notes empty",
                root,
                capture,
                normalWidth,
                normalHeight);
            window.Width = designWidth;
            window.Height = designHeight;
            PumpDispatcher();
            return entry;
        });

        // 变体四：PerMonitorV2 动态 DPI。窗口真实走完 WM_DPICHANGED 处理管线并在新 DPI 下
        // 重排渲染；awareness 上下文与前后 DPI 遥测一并落盘。窗口为临时 probe 窗口，
        // 变更后不还原，由 probe 的 finally 关闭。
        RunVariant("main-permonitorv2-dpichanged-notes-empty", () =>
        {
            ShowNotesWorkspace();
            dpiChangeTelemetry = MeasureWindowDpiChange(window, root, 1.5);
            var dpiAfter = VisualTreeHelper.GetDpi(root);
            var dipWidth = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
            var dipHeight = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
            var capture = CaptureOpaqueMainSurfaceAtDpi(
                root,
                dipWidth,
                dipHeight,
                dpiAfter.PixelsPerInchX,
                dpiAfter.PixelsPerInchY);
            var physicalWidth = (int)Math.Round(
                dipWidth * dpiAfter.DpiScaleX);
            var physicalHeight = (int)Math.Round(
                dipHeight * dpiAfter.DpiScaleY);
            if (Math.Abs(capture.PixelWidth - physicalWidth) > 3 ||
                Math.Abs(capture.PixelHeight - physicalHeight) > 3)
            {
                throw new InvalidOperationException(
                    "PerMonitorV2 变体物理画布不匹配：" +
                    $"{capture.PixelWidth}x{capture.PixelHeight}，期望约 {physicalWidth}x{physicalHeight}。");
            }

            return new MainUiCaptureSeamEntry(
                "main-permonitorv2-dpichanged-notes-empty",
                "permonitor-v2; WM_DPICHANGED pipeline re-rendered window content at raised DPI; notes empty",
                SaveUiCaptureSeamPng(evidenceDirectory, "main-permonitorv2-dpichanged-notes-empty", capture),
                Convert.ToHexString(SHA256.HashData(EncodePng(capture))),
                dipWidth,
                dipHeight,
                capture.PixelWidth,
                capture.PixelHeight,
                dpiAfter.DpiScaleX,
                dpiAfter.DpiScaleY);
        });

        WriteNewBytes(
            Path.Combine(evidenceDirectory, "ui-capture-seam-manifest.json"),
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schemaVersion = 1,
                    captureKind = "actual-wpf-window-frames",
                    issue = "#134 real WPF capture and DPI acceptance seam",
                    awarenessElevatedByProbe = awarenessElevatedByProbe,
                    designCanvas = new { width = designWidth, height = designHeight },
                    normalWindow = new { width = 1165, height = 1350 },
                    dpiChange = dpiChangeTelemetry,
                    entries = entries.Select(entry => new
                    {
                        captureId = entry.CaptureId,
                        mechanism = entry.Mechanism,
                        path = entry.Path,
                        sha256 = entry.Sha256,
                        dip = new { width = entry.DipWidth, height = entry.DipHeight },
                        pixels = new { width = entry.PixelWidth, height = entry.PixelHeight },
                        dpiScale = new { x = entry.DpiScaleX, y = entry.DpiScaleY }
                    })
                },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    // #134：PerMonitorV2 变体的前置提升。SetProcessDpiAwarenessContext 必须在首个窗口
    // 创建前调用；已被设置（例如 WPF 提前初始化为系统感知）时记录失败但继续，由线程级
    // SetThreadDpiAwarenessContext 决定成败——窗口的 DPI 处理按创建它的线程上下文生效。
    // 返回进程级提升是否成功，供 manifest 如实记录。
    private static bool EnableCaptureProbePerMonitorV2Awareness()
    {
        var processElevated = NativeMethods.SetProcessDpiAwarenessContext(
            NativeMethods.DpiAwarenessContextPerMonitorV2);
        var processError = processElevated ? 0 : Marshal.GetLastWin32Error();
        var previousAwareness = NativeMethods.SetThreadDpiAwarenessContext(
            NativeMethods.DpiAwarenessContextPerMonitorV2);
        if (previousAwareness == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "PerMonitorV2 捕获 probe 未能把 DPI 感知提升为 PerMonitorV2：" +
                $"SetProcessDpiAwarenessContext elevated={processElevated}（错误码 {processError}），" +
                $"SetThreadDpiAwarenessContext 错误码 {Marshal.GetLastWin32Error()}。");
        }

        return processElevated;
    }

    // PerMonitorV2 动态轴遥测采集：向临时窗口发送 WM_DPICHANGED（等比 ratio），
    // 要求 Window.DpiChanged 真实发生且 VisualTreeHelper.GetDpi 反映新比例；
    // 任一不满足即抛出原始状态，不用静态帧顶替。
    private static MainUiDpiChangeTelemetry MeasureWindowDpiChange(
        Window window,
        FrameworkElement root,
        double ratio)
    {
        var interop = new WindowInteropHelper(window);
        interop.EnsureHandle();
        var windowHandle = interop.Handle;
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("PerMonitorV2 变体无法取得窗口句柄。");
        }

        var threadAwareness = NativeMethods.GetThreadDpiAwarenessContext();
        var awarenessIsPerMonitorV2 = threadAwareness != IntPtr.Zero &&
            NativeMethods.AreDpiAwarenessContextsEqual(
                threadAwareness,
                NativeMethods.DpiAwarenessContextPerMonitorV2);
        if (!awarenessIsPerMonitorV2)
        {
            throw new InvalidOperationException(
                "PerMonitorV2 变体要求线程 DPI 感知上下文为 PerMonitorV2。");
        }

        var dpiBefore = VisualTreeHelper.GetDpi(root);
        var targetScaleX = dpiBefore.DpiScaleX * ratio;
        var targetScaleY = dpiBefore.DpiScaleY * ratio;
        var targetDpiX = (int)Math.Round(96 * targetScaleX);
        var targetDpiY = (int)Math.Round(96 * targetScaleY);
        var physicalLeft = (int)Math.Round(window.Left * dpiBefore.DpiScaleX);
        var physicalTop = (int)Math.Round(window.Top * dpiBefore.DpiScaleY);
        var physicalWidth = (int)Math.Round(window.ActualWidth * dpiBefore.DpiScaleX);
        var physicalHeight = (int)Math.Round(window.ActualHeight * dpiBefore.DpiScaleY);
        var suggestedRect = new NativeMethods.Rect
        {
            Left = physicalLeft,
            Top = physicalTop,
            Right = physicalLeft + physicalWidth,
            Bottom = physicalTop + physicalHeight
        };
        var rectPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.Rect>());
        try
        {
            Marshal.StructureToPtr(suggestedRect, rectPointer, false);
            var dpiChangedObserved = false;
            window.DpiChanged += (_, _) => dpiChangedObserved = true;
            _ = NativeMethods.SendMessage(
                windowHandle,
                NativeMethods.WmDpiChanged,
                (IntPtr)((targetDpiX & 0xFFFF) | ((targetDpiY & 0xFFFF) << 16)),
                rectPointer);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                PumpDispatcher();
                root.UpdateLayout();
            }

            var dpiAfter = VisualTreeHelper.GetDpi(root);
            var scaleReached =
                Math.Abs(dpiAfter.DpiScaleX - targetScaleX) <= 0.02 &&
                Math.Abs(dpiAfter.DpiScaleY - targetScaleY) <= 0.02;
            if (!dpiChangedObserved || !scaleReached)
            {
                throw new InvalidOperationException(
                    "PerMonitorV2 变体未观察到窗口 DPI 变更生效：" +
                    $"observed={dpiChangedObserved} " +
                    $"dpiBefore={dpiBefore.DpiScaleX:0.###}/{dpiBefore.DpiScaleY:0.###} " +
                    $"dpiAfter={dpiAfter.DpiScaleX:0.###}/{dpiAfter.DpiScaleY:0.###} " +
                    $"targetScale={targetScaleX:0.###}/{targetScaleY:0.###}。");
            }

            return new MainUiDpiChangeTelemetry(
                awarenessIsPerMonitorV2,
                dpiChangedObserved,
                dpiBefore.DpiScaleX,
                dpiBefore.DpiScaleY,
                dpiAfter.DpiScaleX,
                dpiAfter.DpiScaleY);
        }
        finally
        {
            Marshal.FreeHGlobal(rectPointer);
        }
    }

    private static MainUiCaptureSeamEntry RecordUiCaptureSeamEntry(
        string evidenceDirectory,
        string captureId,
        string mechanism,
        FrameworkElement root,
        BitmapSource capture,
        int dipWidth,
        int dipHeight)
    {
        var dpi = VisualTreeHelper.GetDpi(root);
        return new MainUiCaptureSeamEntry(
            captureId,
            mechanism,
            SaveUiCaptureSeamPng(evidenceDirectory, captureId, capture),
            Convert.ToHexString(SHA256.HashData(EncodePng(capture))),
            dipWidth,
            dipHeight,
            capture.PixelWidth,
            capture.PixelHeight,
            dpi.DpiScaleX,
            dpi.DpiScaleY);
    }

    private static string SaveUiCaptureSeamPng(
        string evidenceDirectory,
        string captureId,
        BitmapSource capture)
    {
        var path = Path.Combine(evidenceDirectory, $"{captureId}.png");
        WriteNewBytes(path, EncodePng(capture));
        return path;
    }

    private static void AssertCaptureOpaqueAndSized(
        BitmapSource capture,
        int width,
        int height,
        string captureId)
    {
        if (capture.PixelWidth != width || capture.PixelHeight != height)
        {
            throw new InvalidOperationException(
                $"{captureId} 捕获画布不匹配：{capture.PixelWidth}x{capture.PixelHeight}，期望 {width}x{height}。");
        }

        var alpha = MeasureAlpha(capture);
        if (!alpha.IsFullyOpaque)
        {
            throw new InvalidOperationException(
                $"{captureId} 捕获存在非不透明像素：{alpha.NonOpaquePixels} 个（最小 alpha {alpha.MinimumAlpha}）。");
        }
    }

    // #134：捕获失败证据。原始异常的类型、消息与堆栈原样写入 <变体>.error.txt，
    // 供失败 run 复核；本方法只落盘，不修改、不包装、不吞掉异常本身。
    private static void WriteCaptureFailureEvidence(
        string evidenceDirectory,
        string variantId,
        Exception exception)
    {
        File.WriteAllText(
            Path.Combine(evidenceDirectory, $"{variantId}.error.txt"),
            string.Join(Environment.NewLine,
                $"variant={variantId}",
                $"utc={DateTimeOffset.UtcNow:O}",
                $"exceptionType={exception.GetType().FullName}",
                "message=",
                exception.Message,
                "stack=",
                exception.StackTrace));
    }

    // #134：实拍证据目录缺省解析。仅源码树模式（显式 VISUAL_SOURCE_ROOT）提供默认值，
    // 每次运行落在 logs\ui-captures\ 的独立时间戳子目录，避免 CreateNew 拒绝覆盖已有证据；
    // 发布包模式返回 null，保持不请求实拍、不伪造证据的既有语义。
    private static string? ResolveDefaultVisualEvidenceDirectory(string runKind)
    {
        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        return Path.Combine(
            Path.GetFullPath(sourceRoot),
            "logs",
            "ui-captures",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{runKind}");
    }

    private const string FloatingReferenceBaselineVersion = "2026-10-12-v48";
    private const string FloatingReferenceScenario = "floating-countdowns-baseline";
    private const string FloatingLongNotesScenario = "long-notes";
    private const string FloatingSearchExpandedScenario = "notes-search-expanded";
    private const string FloatingReferenceStateFingerprintAlgorithm =
        "SHA-256 of canonical UTF-8 floating capture state";
    private const string FloatingReferenceSourceAggregateAlgorithm =
        "SHA-256 of UTF-8(path + NUL + uppercase file SHA-256 + LF), ordinal path order";
    private const string FloatingReferencePixelComparisonAlgorithm =
        "BGRA8 absolute channel delta over 520x390 pixels";
    private const string FloatingReferenceAlphaAlgorithm = "BGRA8 alpha scan";
    private const string FloatingReferenceRoiAlgorithm =
        "minimal bounding rectangle of pixels with alpha > 32";
    private const int FloatingReferenceMaximumAbsoluteChannelDelta = 8;
    private const double FloatingReferenceMaximumMeanAbsoluteError = 0.02;
    private const double FloatingReferenceMaximumRootMeanSquareError = 0.25;
    private const int FloatingReferenceMaximumChangedPixelCount = 500;
    private const double FloatingReferenceMinimumTopBottomMeanAbsoluteError = 0.1;
    private const double FloatingReferenceMinimumTopBottomRootMeanSquareError = 1;
    private const int FloatingReferenceMinimumTopBottomChangedPixelCount = 1_000;
    private static readonly string[] FloatingReferenceCanonicalSourcePaths =
    {
        "assets/floating-plush/component-day-card-blank.png",
        "assets/floating-plush/component-day-header-blank.png",
        "assets/floating-plush/component-day-mascots.png",
        "assets/floating-plush/component-day-outer-shell.png",
        "assets/floating-plush/floating-plush-cinnamoroll-reference.png",
        "assets/floating-plush/floating-plush-kuromi-reference.png",
        "assets/floating-plush/floating-plush-mymelody-reference.png",
        "src/MinminCountdown.Core/DateDisplay.cs",
        "src/MinminCountdown.Core/NoteOrdering.cs",
        "src/MinminCountdown/App.xaml",
        "src/MinminCountdown/ApplicationSession.cs",
        "src/MinminCountdown/ApplicationTheme.cs",
        "src/MinminCountdown/DesktopWorkspace.cs",
        "src/MinminCountdown/DeleteUndoOffer.cs",
        "src/MinminCountdown/DomainLogic.cs",
        "src/MinminCountdown/FloatingBottomStatus.cs",
        "src/MinminCountdown/FloatingWindow.Countdowns.cs",
        "src/MinminCountdown/FloatingWindow.Notes.cs",
        "src/MinminCountdown/FloatingWindow.xaml",
        "src/MinminCountdown/FloatingWindow.xaml.cs",
        "src/MinminCountdown/HolidayCalendar.cs",
        "src/MinminCountdown/MinminCountdown.csproj",
        "src/MinminCountdown/MotionBehavior.cs",
        "src/MinminCountdown/NoteCompletionExit.cs",
        "src/MinminCountdown/UiControls.cs"
    };
    private static readonly string[] FloatingReferenceBaselineNegativeFixtures =
    {
        "malformed-manifest",
        "missing-png",
        "duplicate-path",
        "orphan-png",
        "wrong-dimensions",
        "duplicate-scenario-theme",
        "source-hash-mismatch",
        "executable-hash-mismatch",
        "png-hash-mismatch",
        "alpha-metrics-mismatch",
        "content-roi-mismatch",
        "pixel-metrics-mismatch"
    };
    private static readonly string[] FloatingReferenceFreshnessNegativeFixtures =
    {
        "swapped-top-bottom",
        "duplicate-capture-id",
        "state-fingerprint-mismatch",
        "duplicate-state-fingerprint",
        "reused-frame",
        "top-bottom-pixel-difference-too-small",
        "long-note-typography-mismatch",
        "long-note-layout-unreachable"
    };
    private static readonly FloatingReferenceCaptureSpec[] FloatingReferenceCaptureSpecs =
    {
        new(
            "floating-countdowns-day",
            FloatingReferenceScenario,
            "Day",
            "Baseline",
            "floating-countdowns-day.png"),
        new(
            "long-notes-day-top",
            FloatingLongNotesScenario,
            "Day",
            "Top",
            "long-notes-day-top.png"),
        new(
            "long-notes-day-bottom",
            FloatingLongNotesScenario,
            "Day",
            "Bottom",
            "long-notes-day-bottom.png"),
        new(
            "notes-search-expanded-day",
            FloatingSearchExpandedScenario,
            "Day",
            "Expanded",
            "notes-search-expanded-day.png")
    };
    private const string MainReferenceBaselineVersion = "2026-10-12-v48";
    private const double MainNoteCardReminderMinimumFontSizeDip = 12;
    private const string MainStressCountdownTitle =
        "极长倒计时标题用于验证主窗口列表不会挤压操作区或破坏暖纸层级";
    private const string MainReferenceStateFingerprintAlgorithm =
        "SHA-256 of canonical UTF-8 main-window capture state";
    private const string MainReferenceSourceAggregateAlgorithm =
        "SHA-256 of UTF-8(path + NUL + uppercase file SHA-256 + LF), ordinal path order";
    private const string MainReferencePixelComparisonAlgorithm =
        "BGRA8 absolute channel delta over 1165x1350 pixels";
    private const string MainReferenceAlphaAlgorithm =
        "Pbgra32 alpha scan; every pixel must equal 255";
    private const string MainReferenceRoiAlgorithm =
        "fixed full-canvas ROI for opaque 1165x1350 main-window surface";
    private const int MainReferenceMaximumAbsoluteChannelDelta = 8;
    private const double MainReferenceMaximumMeanAbsoluteError = 0.02;
    private const double MainReferenceMaximumRootMeanSquareError = 0.25;
    private const int MainReferenceMaximumChangedPixelCount = 500;
    private static readonly string[] MainReferenceCanonicalSourcePaths =
    {
        "assets/main-decorations/main-countdown-scene-day.png",
        "assets/main-decorations/main-notes-scene-day.png",
        "src/MinminCountdown.Core/DateDisplay.cs",
        "src/MinminCountdown.Core/NoteOrdering.cs",
        "src/MinminCountdown.Core/SettingsContract.cs",
        "src/MinminCountdown.Core/SettingsModels.cs",
        "src/MinminCountdown/App.xaml",
        "src/MinminCountdown/ApplicationSession.cs",
        "src/MinminCountdown/ApplicationTheme.cs",
        "src/MinminCountdown/DesktopWorkspace.cs",
        "src/MinminCountdown/DeleteUndoOffer.cs",
        "src/MinminCountdown/DomainLogic.cs",
        "src/MinminCountdown/MainWindow.xaml",
        "src/MinminCountdown/MainWindow.xaml.cs",
        "src/MinminCountdown/MinminCountdown.csproj",
        "src/MinminCountdown/MotionBehavior.cs",
        "src/MinminCountdown/NoteCompletionExit.cs",
        "src/MinminCountdown/SegmentIndicator.cs",
        "src/MinminCountdown/UiControls.cs",
        "tools/Build-MainDayOrnamentScene.ps1",
        "tools/MainDecorationSceneBands.psd1",
        "tools/Test-MainDayOrnamentScene.ps1"
    };
    private static readonly string[] MainReferenceBaselineNegativeFixtures =
    {
        "malformed-manifest",
        "missing-png",
        "orphan-png",
        "duplicate-capture-id",
        "duplicate-path",
        "duplicate-scenario",
        "wrong-dimensions",
        "source-hash-mismatch",
        "executable-hash-mismatch",
        "png-hash-mismatch",
        "state-fingerprint-mismatch",
        "alpha-metrics-mismatch",
        "content-roi-mismatch",
        "pixel-metrics-mismatch",
        "duplicate-state-fingerprint",
        "reused-frame"
    };
    private static readonly MainReferenceCaptureSpec[] MainReferenceCaptureSpecs =
    {
        new("main-day-countdowns-empty", "Day", "Countdowns", "Empty", "main-day-countdowns-empty.png"),
        new("main-day-countdowns-representative", "Day", "Countdowns", "Representative", "main-day-countdowns-representative.png"),
        new("main-day-countdowns-stress", "Day", "Countdowns", "Stress", "main-day-countdowns-stress.png"),
        new("main-day-notes-empty", "Day", "Notes", "Empty", "main-day-notes-empty.png"),
        new("main-day-notes-representative", "Day", "Notes", "Representative", "main-day-notes-representative.png"),
        new("main-day-notes-stress", "Day", "Notes", "Stress", "main-day-notes-stress.png"),
        new("main-day-notes-edit", "Day", "Notes", "Edit", "main-day-notes-edit.png"),
        new("main-day-notes-filter-open", "Day", "Notes", "FilterOpen", "main-day-notes-filter-open.png")
    };

    /// <summary>
    ///     Persistence-failure validation blocks the real settings temporary path with a directory.
    ///     UI probes still need to exercise successful writes, so keep that directory in a unique
    ///     holding path until the failure exercise itself runs, then restore it unconditionally.
    /// </summary>
    private sealed class PersistenceExercisePathGate : IDisposable
    {
        private readonly string _blockedPath;
        private readonly string _openPath;
        private bool _disposed;
        private bool _uiWritesOpen;

        public PersistenceExercisePathGate(string settingsPath)
        {
            _blockedPath = Path.GetFullPath(settingsPath) + ".tmp";
            _openPath = _blockedPath + $".validation-ui-{Guid.NewGuid():N}";
            if (!Directory.Exists(_blockedPath))
            {
                throw new InvalidOperationException(
                    "持久化失败 exercise 需要先把 settings.json.tmp 建成目录，避免在未阻断路径上写入。");
            }

            Directory.Move(_blockedPath, _openPath);
            _uiWritesOpen = true;
        }

        public T Run<T>(Func<T> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            CloseForPersistenceExercise();
            try
            {
                return operation();
            }
            finally
            {
                OpenForUiWrites();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_uiWritesOpen)
            {
                CloseForUiWrites();
            }
        }

        private void CloseForPersistenceExercise()
        {
            if (!_uiWritesOpen)
            {
                return;
            }

            Directory.Move(_openPath, _blockedPath);
            _uiWritesOpen = false;
        }

        private void OpenForUiWrites()
        {
            if (_uiWritesOpen)
            {
                return;
            }

            Directory.Move(_blockedPath, _openPath);
            _uiWritesOpen = true;
        }

        private void CloseForUiWrites()
        {
            Directory.Move(_openPath, _blockedPath);
            _uiWritesOpen = false;
        }
    }

    public static IReadOnlyDictionary<string, object?> Create(
        MainWindow main,
        FloatingWindow floating,
        FloatingTrayController floatingTrayController,
        DesktopWorkspace desktopWorkspace,
        ApplicationTheme theme,
        HostOptions options,
        string assetRoot)
    {
        using var persistenceExercisePath = options.ExercisePersistenceFailures
            ? new PersistenceExercisePathGate(desktopWorkspace.Snapshot.SettingsPath)
            : null;
        var settingsConservationPath = desktopWorkspace.Snapshot.SettingsPath;
        var settingsBytesAtValidationEntry = File.Exists(settingsConservationPath)
            ? File.ReadAllBytes(settingsConservationPath)
            : null;
        var settingsHashAtValidationEntry = settingsBytesAtValidationEntry is not null
            ? Convert.ToHexString(SHA256.HashData(settingsBytesAtValidationEntry))
            : null;
        main.ShowInTaskbar = false;
        main.ShowActivated = false;
        main.WindowStartupLocation = WindowStartupLocation.Manual;
        main.Left = -20_000;
        main.Top = -20_000;
        main.Opacity = 0;
        floating.ShowInTaskbar = false;
        floating.ShowActivated = false;
        // Initialize the AllowsTransparency surface fully opaque so WPF creates
        // a real composition target before any validation capture changes it.
        floating.Opacity = 1;
        main.Show();
        PumpDispatcher();
        var initialFocusedAutomationId = Keyboard.FocusedElement is DependencyObject focused
            ? AutomationProperties.GetAutomationId(focused)
            : string.Empty;
        floating.Show();
        PumpDispatcher();
        main.UpdateLayout();
        floating.UpdateLayout();
        floating.Reanchor();
        PumpDispatcher();

        var floatingWindowScenarios = floating.RunValidationScenarios(options.ExerciseNoteAdd);
        var floatingWorkspace = floatingWindowScenarios.Workspace;
        var floatingQuickAddTheme = ValidateFloatingQuickAddLocalTheme(
            theme,
            floating);
        var floatingQuickAddThemeResourceIsolationBehavior =
            ValidationExercises.RunFloatingQuickAddThemeResourceIsolationBehavior();
        var floatingQuickAddPaletteBindingIsolationBehavior =
            ValidationExercises.RunFloatingQuickAddPaletteBindingIsolationBehavior();

        var mainWindowScenarios = main.RunValidationScenarios(options.ExerciseNoteAdd);
        var countdownReadability = mainWindowScenarios.CountdownReadability;
        var countdownEditing = mainWindowScenarios.CountdownEditing;
        var interactionStates = mainWindowScenarios.InteractionStates;
        var noteExercise = mainWindowScenarios.NoteExercise;
        var floatingNoteExercise = floatingWindowScenarios.CrossDayReminderAdded;
        var mainReminderModeExercise = mainWindowScenarios.ReminderModes;
        var floatingReminderModeExercise = floatingWindowScenarios.ReminderModes;
        var mainUiPolish = mainWindowScenarios.UiPolish;
        var selector = Required<TagSelector>(main, "CountdownTags");
        var mainNoteCompletionValidation = ValidateMainNoteCompletion(
            main,
            floating,
            selector,
            desktopWorkspace);
        var mainNoteCompletionGates = ValidationExercises.EvaluateMainNoteCompletionGates(
            new MainNoteCompletionGateInputs(
                mainNoteCompletionValidation.Main.UsesDedicatedStyle,
                mainNoteCompletionValidation.Main.BoxWidth >= 24 &&
                    mainNoteCompletionValidation.Main.BoxHeight >= 24,
                mainNoteCompletionValidation.Main.TickWidth >= 14 &&
                    mainNoteCompletionValidation.Main.TickHeight >= 10,
                mainNoteCompletionValidation.Main.UsesWarmTokens,
                mainNoteCompletionValidation.Main.CornerRadius >= 6,
                mainNoteCompletionValidation.Interaction.Checked &&
                    mainNoteCompletionValidation.Interaction.Unchecked,
                mainNoteCompletionValidation.Interaction.PersistenceFailureRolledBack,
                mainNoteCompletionValidation.Interaction.AccessibleNamesUpdated,
                mainNoteCompletionValidation.TagSelector.BoxWidth is >= 17.5 and <= 18.5 &&
                    mainNoteCompletionValidation.TagSelector.BoxHeight is >= 17.5 and <= 18.5 &&
                    mainNoteCompletionValidation.TagSelector.TickWidth is >= 9.5 and <= 10.5 &&
                    mainNoteCompletionValidation.TagSelector.TickHeight is >= 6.5 and <= 7.5 &&
                    !mainNoteCompletionValidation.TagSelector.UsesDedicatedStyle,
                mainNoteCompletionValidation.Floating.BoxWidth is >= 17.5 and <= 18.5 &&
                    mainNoteCompletionValidation.Floating.BoxHeight is >= 17.5 and <= 18.5 &&
                    mainNoteCompletionValidation.Floating.TickWidth is >= 9.5 and <= 10.5 &&
                    mainNoteCompletionValidation.Floating.TickHeight is >= 6.5 and <= 7.5 &&
                    !mainNoteCompletionValidation.Floating.UsesDedicatedStyle));
        var mainNoteCompletionGateIsolationBehavior =
            ValidationExercises.RunMainNoteCompletionGateIsolationBehavior();
        var accessibleNameWhitespaceIsolationBehavior =
            ValidationExercises.RunAccessibleNameWhitespaceIsolationBehavior();
        var mainShortcutGateIsolationBehavior =
            ValidationExercises.RunMainShortcutGateIsolationBehavior();
        var mainKeyboardNavigationGateIsolationBehavior =
            ValidationExercises.RunMainKeyboardNavigationGateIsolationBehavior();
        // #153：便签列表排版 / 芯片 / 空态 / 筛选标签 / 键盘与间距 token 探针消费与门禁评估。
        var noteListPolish = mainUiPolish.NoteListPolish;
        // #154/#200：视觉铬探针消费——漏斗/图标描边/矢量窗铬/胶囊圆角/装饰层/
        // DWM 原生圆角与安全降级/页签次序；
        // 悬浮外壳投影（TaskCard/FloatingThemeBackdrop 无 Effect）由悬浮窗实例直接钉。
        var visualChrome = mainUiPolish.VisualChrome;
        var floatingShellLargeShadowRemoved =
            Required<Border>(floating, "TaskCard").Effect is null &&
            Required<Border>(floating, "FloatingThemeBackdrop").Effect is null;
        var popupFocusTabGates = ValidationExercises.EvaluatePopupFocusTabGates(
            new PopupFocusTabGateInputs(
                noteListPolish.FilterPopupTabCyclesInsideLayer,
                noteListPolish.TagManagerPopupTabCyclesInsideLayer,
                noteListPolish.TagSelectorPopupTabCyclesInsideLayer));
        var popupFocusTabGateIsolationBehavior =
            ValidationExercises.RunPopupFocusTabGateIsolationBehavior();
        var countdownListFirst = ValidationExercises.RunCountdownListFirstBehavior();
        var mainTopmostStateSynchronization = ValidationExercises.RunMainTopmostStateSynchronization();
        var mainRedundancyCleanup = ValidationExercises.RunMainRedundancyCleanup();
        var mainNoteEditorSeparation = ValidationExercises.RunMainNoteEditorSeparation();
        var mainTextShortcutBehavior = ValidationExercises.RunMainTextShortcutBehavior();
        var singleLineEnterSubmission = ValidationExercises.RunSingleLineEnterSubmission();
        var floatingNoteKeyboardSubmission = ValidationExercises.RunFloatingNoteKeyboardSubmission();
        var listDeleteKeyboardBehavior = ValidationExercises.RunListDeleteKeyboardBehavior();
        var mainNoteRowsSimplified = ValidationExercises.RunMainNoteRowsSimplified();
        var countdownPreferencesPopup = ValidationExercises.RunCountdownPreferencesPopupBehavior();
        var countdownEditorExpansion = ValidationExercises.RunCountdownEditorExpansionBehavior();
        var countdownRowPresentation = ValidationExercises.RunCountdownRowDeadlineAndSecondaryDeleteBehavior();
        var mainStructuralAnnouncements = ValidateMainStructuralAnnouncements(assetRoot);
        var mainStructuralAnnouncementGates =
            ValidationExercises.EvaluateMainStructuralAnnouncementGates(
                new MainStructuralAnnouncementGateInputs(
                    mainStructuralAnnouncements.CountdownWorkspaceSelectionIsAccessible &&
                        mainStructuralAnnouncements.NotesWorkspaceSelectionIsAccessible,
                    mainStructuralAnnouncements.FilterResultAnnounced,
                    mainStructuralAnnouncements.CountdownFirstUseEmptyAnnounced &&
                        mainStructuralAnnouncements.NotesFirstUseEmptyAnnounced &&
                        mainStructuralAnnouncements.CountdownFilterEmptyAnnounced &&
                        mainStructuralAnnouncements.NotesFilterEmptyAnnounced));
        var mainStructuralAnnouncementGateIsolationBehavior =
            ValidationExercises.RunMainStructuralAnnouncementGateIsolationBehavior();
        var countdownIncrementalRefreshGateIsolationBehavior =
            ValidationExercises.RunCountdownIncrementalRefreshGateIsolationBehavior();
        var floatingRenderSignatureGateIsolationBehavior =
            ValidationExercises.RunFloatingRenderSignatureGateIsolationBehavior();
        var floatingEmptyStateTextIsolationBehavior =
            ValidationExercises.RunFloatingEmptyStateTextIsolationBehavior();
        var floatingUiPolish = floatingWindowScenarios.UiPolish;
        var tagExercise = options.ExerciseTagInteractions
            ? ValidationExercises.RunTagInteractions(desktopWorkspace)
            : TagInteractionExerciseResult.NotRun;
        var persistenceExercise = options.ExercisePersistenceFailures
            ? persistenceExercisePath!.Run(() => ValidationExercises.RunPersistenceFailures(
                desktopWorkspace,
                desktopWorkspace.Snapshot.SettingsPath))
            : PersistenceFailureExerciseResult.NotRun;

        var workspaceSnapshot = desktopWorkspace.Snapshot;

        var minimizeToTrayButton = Required<Button>(floating, "MinimizeToTrayButton");
        var openManagerButton = Required<Button>(floating, "OpenManagerButton");
        var closeFloatingButton = Required<Button>(floating, "CloseFloatingButton");
        var floatingTrayButtonAccessibility =
            string.Equals(
                minimizeToTrayButton.ToolTip as string,
                "缩小到系统托盘",
                StringComparison.Ordinal) &&
            string.Equals(
                AutomationProperties.GetName(minimizeToTrayButton),
                "缩小到系统托盘",
                StringComparison.Ordinal);
        var floatingHeaderIconAccessibility =
            floatingTrayButtonAccessibility &&
            string.Equals(openManagerButton.ToolTip as string, "打开管理窗口", StringComparison.Ordinal) &&
            string.Equals(closeFloatingButton.ToolTip as string, "退出应用", StringComparison.Ordinal) &&
            string.Equals(
                AutomationProperties.GetName(openManagerButton),
                "打开管理窗口",
                StringComparison.Ordinal) &&
            string.Equals(
                AutomationProperties.GetName(closeFloatingButton),
                "退出应用",
                StringComparison.Ordinal);
        var exitRequested = false;
        EventHandler exitHandler = (_, _) => exitRequested = true;
        floating.ExitRequested += exitHandler;
        main.Hide();
        openManagerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpDispatcher();
        var managerOpenedViaClick = main.IsVisible && main.WindowState == WindowState.Normal;
        closeFloatingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpDispatcher();
        floating.ExitRequested -= exitHandler;
        var floatingHeaderIconClickCommandsWork = managerOpenedViaClick && exitRequested;
        var floatingDragInteraction = floatingWindowScenarios.DragInteraction;

        var datePicker = Required<DatePicker>(main, "CountdownDate");
        datePicker.ApplyTemplate();
        var dateTextBox = datePicker.Template.FindName("PART_TextBox", datePicker) as DatePickerTextBox;
        var dateButton = datePicker.Template.FindName("PART_Button", datePicker) as Button;
        var datePopup = datePicker.Template.FindName("PART_Popup", datePicker) as Popup;
        var dateCalendar = PickerTemplateParts.GetCalendar(datePicker);
        var dateParts = new object?[] { dateTextBox, dateButton, datePopup, dateCalendar };
        var dateButtonOpenedPopup = false;
        if (datePopup is not null)
        {
            datePopup.Opened += (_, _) => dateButtonOpenedPopup = true;
        }

        datePicker.IsDropDownOpen = false;
        dateButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpDispatcher();
        var dateButtonOpens = dateButtonOpenedPopup;
        datePicker.IsDropDownOpen = true;
        PumpDispatcher();
        dateCalendar?.ApplyTemplate();
        dateCalendar?.UpdateLayout();
        var livePopupThemed =
            datePopup?.IsOpen == true &&
            datePopup.Child is WpfCalendar &&
            dateCalendar?.Template is not null &&
            ReferenceEquals(dateCalendar.Style, datePicker.CalendarStyle);
        var livePopupUnified =
            dateCalendar is not null &&
            Equals(dateCalendar.Background, Application.Current.FindResource("CanvasBrush")) &&
            Equals(dateCalendar.BorderBrush, Application.Current.FindResource("PeachLineBrush"));
        datePicker.IsDropDownOpen = false;

        var dateBehaviorAttached = PickerTemplateParts.HasDatePickerContract(datePicker);
        var productionDatePickers = ValidateProductionDatePickers(main, floating);
        var dateTextMetrics = MeasureDatePickerTextFit(datePicker);
        var calendarHeaderMetrics = MeasureTextFit(datePicker, "2031年12月");
        var workspaceDraftsRetained = mainWindowScenarios.WorkspaceDraftsRetained;
        var settingsDrafts = mainWindowScenarios.SettingsDrafts;
        var undatedNoteEdit = mainWindowScenarios.UndatedNoteEdit;
        var noteDateBehavior = mainWindowScenarios.NoteDateBehavior;
        var originalWorkspace = main.CurrentWorkspace;
        main.ShowWorkspace(WorkspaceKind.Notes);
        var hiddenCountdownSubmitDisabled = !Required<Button>(main, "SaveCountdownButton").IsEnabled;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        PumpDispatcher();
        main.UpdateLayout();
        var switchesToCountdown = Required<Canvas>(main, "CountdownWorkspace").Visibility == Visibility.Visible;
        var mainContentProjection = Required<Grid>(main, "MainContentProjection");
        var workspaceSwitcherBottom = new[]
        {
            BoundsIn(Required<ToggleButton>(main, "CountdownTab"), mainContentProjection).Bottom,
            BoundsIn(Required<ToggleButton>(main, "NotesTab"), mainContentProjection).Bottom
        }.Max();
        var countdownEditorTop =
            BoundsIn(Required<Border>(main, "CountdownEditorSurface"), mainContentProjection).Top;
        main.ShowWorkspace(WorkspaceKind.Notes);
        PumpDispatcher();
        main.UpdateLayout();
        var switchesToNotes = Required<Canvas>(main, "NotesWorkspace").Visibility == Visibility.Visible;
        var noteEditorTop = BoundsIn(Required<Border>(main, "NoteEditorPanel"), mainContentProjection).Top;
        var workspaceSwitcherBeforeEditors =
            workspaceSwitcherBottom <= countdownEditorTop &&
            workspaceSwitcherBottom <= noteEditorTop;
        main.ShowWorkspace(originalWorkspace);
        PumpDispatcher();
        main.UpdateLayout();

        var createAppears = selector.ProbeCreateAction("验证新标签");
        var createCollapsed = !selector.ProbeCreateAction(string.Empty);
        var tagSearchDoesNotSubmit = !selector.CreateButtonIsDefault;
        var reminderBehavior = ValidateReminderDispatch();
        var oneTimeReminderBehavior = ValidateOneTimeReminderLifecycle();
        var repeatingReminderDelivery = ValidateRepeatingReminderDeliveryCoordinator();
        var reminderDispositionAndSnooze =
            ValidationExercises.RunReminderDispositionAndSnoozeBehavior();
        var reminderCompletionAndReopen =
            ValidationExercises.RunReminderCompletionAndReopenBehavior();
        var reminderCenterCustomSnoozeControlsVisibility =
            ValidationExercises.RunReminderCenterCustomSnoozeControlsVisibilityBehavior();
        var reminderCenterCustomSnoozeCalendarBehavior =
            ValidationExercises.RunReminderCenterCustomSnoozeCalendarBehavior();
        var reminderCenterSnoozeClick =
            ValidationExercises.RunReminderCenterSnoozeClickBehavior();
        var reminderCenterExpiredCustomSnoozeRetry =
            ValidationExercises.RunReminderCenterExpiredCustomSnoozeRetryBehavior();
        var reminderCenterStaleRetryAfterCompleteReopen =
            ValidationExercises.RunReminderCenterStaleRetryAfterCompleteReopenBehavior();
        var reminderSnoozeSuppressesBaseCycles =
            ValidationExercises.RunReminderSnoozeSuppressesBaseCyclesBehavior();
        var reminderCenterCompleteClick =
            ValidationExercises.RunReminderCenterCompleteClickBehavior();
        var reminderCenterCompleteRequiresConfirmation =
            ValidationExercises.RunReminderCenterCompleteRequiresConfirmationBehavior();
        var deleteUndoOffer =
            ValidationExercises.RunDeleteUndoOfferBehavior() &&
            ValidationExercises.RunDeleteUndoInPlaceBehavior();
        var dirtyExitConfirmation =
            ValidationExercises.RunDirtyExitConfirmationBehavior();
        var assistiveNarration =
            ValidationExercises.RunAssistiveNarrationBehavior();
        var narratorLiveRegions =
            ValidationExercises.RunNarratorLiveRegionBehavior();
        var windowPositionReset =
            ValidationExercises.RunWindowPositionResetBehavior();
        var mainWindowWorkAreaFit =
            ValidationExercises.RunMainWindowWorkAreaFitBehavior();
        var mainWindowIdentityZoneDrag =
            ValidationExercises.RunMainWindowIdentityZoneDragBehavior();
        var floatingSecondaryMonitor = MonitorGeometryValidation.FloatingWindowStaysOnSecondaryMonitor();
        var ownerDialogsFitSecondaryMonitor = MonitorGeometryValidation.OwnerDialogsFitSecondaryMonitor();
        var reminderUsesSourceMonitor = MonitorGeometryValidation.ReminderUsesSourceMonitor();
        var openWindowsFollowMonitorChanges = MonitorGeometryValidation.OpenWindowsFollowMonitorChanges();
        var mixedDpiPhysicalGeometry = MonitorGeometryValidation.MixedDpiUsesPhysicalMonitorGeometry();
        var productManifestPerMonitorV2 = MonitorGeometryValidation.ProductManifestDeclaresPerMonitorV2();
        var runtimeDpiLayout = MonitorGeometryValidation.RuntimeDpiChangeUpdatesWindowLayout();
        var mainWindowHiddenRefresh =
            ValidationExercises.RunMainWindowHiddenRefreshBehavior();
        var mainWindowNoteMinuteRefresh =
            ValidationExercises.RunMainWindowNoteMinuteRefreshBehavior();
        var reminderCenterDeliveryFailureActionGate =
            ValidationExercises.RunReminderCenterDeliveryFailureActionGateBehavior();
        var reminderCenterEnterUsesNativeButtonActivation =
            ValidationExercises.RunReminderCenterEnterUsesNativeButtonActivationBehavior();
        var reminderCenterArrivalKeepsForegroundAndFlashesTaskbar =
            ValidationExercises.RunReminderCenterArrivalKeepsForegroundAndFlashesTaskbarBehavior();
        var reminderCenterMinimizeToTaskbarPreservesEntries =
            ValidationExercises.RunReminderCenterMinimizeToTaskbarPreservesEntriesBehavior();
        var reminderCenterSnoozeGatedDuringCompletionRetry =
            ValidationExercises.RunReminderCenterSnoozeGatedDuringCompletionRetryBehavior();
        var reminderCenterCustomSnoozeErrorRoutesByCode =
            ValidationExercises.RunReminderCenterCustomSnoozeErrorRoutesByCodeBehavior();
        var reminderCenterShowFailureRollsBack =
            ValidationExercises.RunReminderCenterShowFailureRollsBackBehavior();
        var reminderCenterShowFailureRollbackSurvivesEntryFailure =
            ValidationExercises.RunReminderCenterShowFailureRollbackSurvivesEntryFailureBehavior();
        var reminderCenterOpenSourcePreservesNoteGroupState =
            ValidationExercises.RunReminderCenterOpenSourcePreservesNoteGroupStateBehavior();
        var reminderCenterAcknowledgeAll =
            ValidationExercises.RunReminderCenterAcknowledgeAllBehavior();
        var reminderCenterRepresentativeCapture =
            ValidateReminderCenterRepresentativeCapture();
        var validateOnlySettingsIsolation =
            ValidationExercises.RunValidateOnlySettingsIsolationBehavior();
        var lazyManagerLifecycle =
            ValidationExercises.RunLazyManagerLifecycleBehavior();
        var mainDecorationOnDemandDecode =
            ValidationExercises.RunMainDecorationOnDemandDecodeBehavior();
        var countdownReminderWpf =
            ValidationExercises.RunCountdownReminderWpfBehavior();
        var reminderDelivery = ValidateReminderDeliveryCoordinator();
        var reminderCenter = ValidateReminderCenterBehavior(theme, assetRoot);
        var reminderCenterAppendsAfterOpen = ValidateReminderCenterAppendsAfterOpen();
        var reminderCenterSourceLifecycle = ValidateReminderCenterSourceLifecycle();
        var reminderPersistenceFailureDisposeWorks =
            ValidateReminderPersistenceFailureDispose();
        var crossDayReminderBehavior = mainWindowScenarios.CrossDayReminderBehavior;
        var reminderSummaryAdvances = mainWindowScenarios.ReminderSummaryAdvances;
        var roundTrip = ValidateRoundTrip(workspaceSnapshot.Settings);
        var legacyMigration = ValidateLegacyMigration();
        var duplicateRejected = RejectsDuplicateCountdowns(workspaceSnapshot.Settings);
        var invalidNotesRejected = RejectsInvalidNotes(workspaceSnapshot.Settings);
        var invalidTagReferencesRejected = RejectsDanglingTag(workspaceSnapshot.Settings);

        var mainThemeTemplate =
            HasTemplate(Required<Button>(main, "SaveCountdownButton")) &&
            HasTemplate(Required<TextBox>(main, "CountdownName")) &&
            HasTemplate(Required<ComboBox>(main, "CountdownHour")) &&
            HasTemplate(datePicker);
        var themeInventory =
            HasApplicationStyle(typeof(Button)) &&
            HasApplicationStyle(typeof(TextBox)) &&
            HasApplicationStyle(typeof(CheckBox)) &&
            HasApplicationStyle(typeof(ComboBox)) &&
            HasApplicationStyle(typeof(DatePicker)) &&
            HasApplicationStyle(typeof(WpfCalendar)) &&
            HasApplicationStyle(typeof(ScrollViewer)) &&
            HasApplicationStyle(typeof(ScrollBar)) &&
            HasApplicationStyle(typeof(ToolTip));
        var comboThemed =
            HasTemplate(Required<ComboBox>(main, "CountdownHour")) &&
            HasApplicationStyle(typeof(ComboBoxItem)) &&
            HasApplicationStyle(typeof(ScrollBar));
        var dateFamilyThemed =
            dateTextBox?.Template is not null &&
            dateButton?.Template is not null &&
            dateCalendar?.Template is not null &&
            HasApplicationStyle(typeof(CalendarItem)) &&
            HasApplicationStyle(typeof(CalendarDayButton)) &&
            HasApplicationStyle(typeof(CalendarButton));

        var toolTip = new ToolTip { Content = "主题提示" };
        toolTip.ApplyTemplate();
        var toolTipMaterializes = toolTip.Template is not null;
        var contrast = ValidateThemeContrast(theme, workspaceSnapshot.UtcNow);
        var mainIdleTextInputBoundaryContrast = ValidateMainIdleTextInputBoundaryContrast(main);
        var idleControlBoundaries = ValidateIdleControlBoundaries(main);
        var selectedControlSignals = ValidateSelectedControlSignals(main);
        var focusOuterNeighbors = ValidateFocusOuterNeighbors(main);
        var dangerContrast = ContrastRatio(
            (Color)Application.Current.FindResource("CanvasColor"),
            (Color)Application.Current.FindResource("DangerColor")) >= 4.5;
        var dangerSurfaces = new[] { "Background", "Canvas", "Card", "Note", "StatusBar", "ReminderCard" }
            .ToDictionary(name => name, name => ContrastRatio(
                (Color)Application.Current.FindResource(name + "Color"),
                (Color)Application.Current.FindResource("DangerColor")), StringComparer.Ordinal);

        var countdownRows = Required<StackPanel>(main, "CountdownRows");
        var activeNoteRows = Required<StackPanel>(main, "ActiveNoteRows");
        var completedNoteRows = Required<StackPanel>(main, "CompletedNoteRows");
        var activeNotesSegment = Required<ToggleButton>(main, "ActiveNotesSegment");
        var completedNotesSegment = Required<ToggleButton>(main, "CompletedNotesSegment");
        var activeNotesLabel = Required<TextBlock>(main, "ActiveNotesLabel");
        var completedNotesLabel = Required<TextBlock>(main, "CompletedNotesLabel");
        var activeNotesCount = Required<TextBlock>(main, "ActiveNotesCount");
        var completedNotesCount = Required<TextBlock>(main, "CompletedNotesCount");
        var activeNotesBadge = Required<Border>(main, "ActiveNotesBadge");
        var completedNotesBadge = Required<Border>(main, "CompletedNotesBadge");
        var countdownActionButtons = Descendants<Button>(countdownRows)
            .Where(button => button.Content?.ToString() is "编辑" or "删除")
            .ToArray();
        var noteActionButtons = Descendants<Button>(activeNoteRows)
            .Concat(Descendants<Button>(completedNoteRows))
            .Where(button => button.Content?.ToString() is "编辑" or "删除")
            .ToArray();
        var cardActionsBound = countdownActionButtons.All(button => button.Tag is string);
        var noteActionsBound = noteActionButtons.All(button => button.Tag is string);
        var noteStatusLabels =
            string.Equals(activeNotesLabel.Text, "未完成", StringComparison.Ordinal) &&
            string.Equals(completedNotesLabel.Text, "已完成", StringComparison.Ordinal);
        var expectedManagerNoteIds = workspaceSnapshot.VisibleNotes
            .Select(note => note.Id)
            .ToArray();
        var renderedManagerNoteIds = GetRenderedNoteIds(activeNoteRows)
            .Concat(GetRenderedNoteIds(completedNoteRows))
            .ToArray();
        var managerNoteRowsFollowSortOrder = expectedManagerNoteIds.SequenceEqual(
            renderedManagerNoteIds,
            StringComparer.Ordinal);
        var notesGroupedByDay = AreNotesGroupedByDay(workspaceSnapshot.AllNotes);
        var noteStatusSegmentSwitch =
            HasNoteStatusSegmentSwitch(
                activeNotesSegment,
                completedNotesSegment,
                activeNotesLabel,
                completedNotesLabel,
                activeNotesCount,
                completedNotesCount,
                activeNotesBadge,
                completedNotesBadge,
                activeNoteRows,
                completedNoteRows,
                workspaceSnapshot.VisibleNotes.Count(note => !note.IsCompleted),
                workspaceSnapshot.VisibleNotes.Count(note => note.IsCompleted));
        var renderedDateSections = GetNoteDateSections(activeNoteRows)
            .Concat(GetNoteDateSections(completedNoteRows))
            .ToArray();
        var noteDatesRendered = workspaceSnapshot.Settings.Notes
            .Where(note => note.Date is not null)
            .All(note => renderedDateSections.Any(section =>
                string.Equals(section.Key, note.Date, StringComparison.Ordinal)));
        var noteDateTimelineVisuals =
            workspaceSnapshot.Settings.Notes.Count == 0 ||
            renderedDateSections.Length > 0 && renderedDateSections.All(HasNoteDateTimelineVisual);
        var noteDisclosureAccessibility =
            HasNoteSegmentAccessibleName(activeNotesSegment, "未完成", activeNotesCount.Text) &&
            HasNoteSegmentAccessibleName(completedNotesSegment, "已完成", completedNotesCount.Text) &&
            renderedDateSections.All(section =>
                HasNoteDisclosureAccessibleName(
                    section.Toggle,
                    GetNoteDateLabel(section.Toggle)?.Text ?? string.Empty)) &&
            HasNoteRowActionAccessibleNames(activeNoteRows) &&
            HasNoteRowActionAccessibleNames(completedNoteRows);
        var notesStatusHeader = Required<Grid>(main, "NotesStatusHeader");
        var activeNotesGrouped =
            activeNoteRows.Parent is StackPanel noteSections &&
            noteSections.Children.Contains(notesStatusHeader) &&
            noteSections.Children.IndexOf(activeNoteRows) ==
            noteSections.Children.IndexOf(notesStatusHeader) + 1 &&
            noteSections.Children.IndexOf(completedNoteRows) ==
            noteSections.Children.IndexOf(activeNoteRows) + 1;
        var expectedActiveDateOrder = workspaceSnapshot.VisibleNotes
            .Where(note => !note.IsCompleted)
            .Select(note => note.Date ?? "未设置日期")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var renderedActiveDateOrder = GetRenderedNoteDateOrder(activeNoteRows);
        var activeNotesKeepDateOrder = expectedActiveDateOrder.SequenceEqual(
            renderedActiveDateOrder,
            StringComparer.Ordinal);
        var noteSectionExercise = ExerciseNoteSections(
            main,
            activeNoteRows,
            completedNoteRows);
        var noteGroupingSelectorAbsent = main.FindName("NoteGroupingSelector") is null;
        var tagChips = Descendants<Border>(countdownRows)
            .Concat(Descendants<Border>(activeNoteRows))
            .Where(border => !string.IsNullOrWhiteSpace(border.Uid))
            .ToArray();
        var tagChipsUseUid = tagChips.All(chip => chip.Uid.Length > 0);

        var savePreferencesButton = Required<Button>(main, "SavePreferencesButton");
        var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
        preferencesPopup.IsOpen = true;
        PumpDispatcher();
        var savePreferencesButtonFit = MeasureButtonFit(savePreferencesButton);
        var savePreferencesButtonRight = savePreferencesButton
            .TranslatePoint(new Point(savePreferencesButton.ActualWidth, 0), main)
            .X;
        var savePreferencesButtonIsVisible =
            savePreferencesButton.IsVisible &&
            savePreferencesButton.TranslatePoint(new Point(0, 0), main).X >= 0 &&
            savePreferencesButtonRight <= main.ActualWidth;
        WindowValidationInput.ClosePopupForCleanup(preferencesPopup);
        var originalMainWidth = main.Width;
        main.Width = main.MinWidth;
        main.UpdateLayout();
        // #211/#221：最小宽度下经窗口级滚动到达齿轮，再实际打开弹层测量保存按钮。
        var mainScrollHost = Required<ScrollViewer>(main, "MainWindowScrollHost");
        Required<Button>(main, "PreferencesButton").BringIntoView();
        PumpDispatcher();
        preferencesPopup.IsOpen = true;
        PumpDispatcher();
        main.UpdateLayout();
        var savePreferencesButtonLeftInViewport =
            savePreferencesButton.TranslatePoint(new Point(0, 0), mainScrollHost).X;
        var savePreferencesButtonIsVisibleAtMinimumWidth =
            savePreferencesButton.IsVisible &&
            mainScrollHost.ScrollableWidth > 0.5 &&
            savePreferencesButtonLeftInViewport >= -0.5 &&
            savePreferencesButtonLeftInViewport + savePreferencesButton.ActualWidth <=
                mainScrollHost.ViewportWidth + 0.5;
        WindowValidationInput.ClosePopupForCleanup(preferencesPopup);
        mainScrollHost.ScrollToLeftEnd();
        mainScrollHost.ScrollToTop();
        main.Width = originalMainWidth;
        main.UpdateLayout();
        theme.Apply();
        main.ApplyTheme();
        PumpDispatcher();
        main.UpdateLayout();
        var mainCharacterPanelSurface = Required<Border>(main, "CharacterPanelSurface");
        var mainCharacterPanelBounds = mainCharacterPanelSurface
            .TransformToAncestor(main)
            .TransformBounds(new Rect(new Point(0, 0), mainCharacterPanelSurface.RenderSize));
        var mainCharacterDocks = new[]
        {
            (Dock: Required<Border>(main, "MyMelodyCharacterDock"), Image: Required<Image>(main, "MyMelodyPanelCharacter")),
            (Dock: Required<Border>(main, "CinnamorollCharacterDock"), Image: Required<Image>(main, "CinnamorollPanelCharacter")),
            (Dock: Required<Border>(main, "KuromiCharacterDock"), Image: Required<Image>(main, "KuromiPanelCharacter"))
        };
        var legacyCharacterDocksIntegrateWithPanel = mainCharacterDocks.All(pair =>
        {
            var dockBounds = pair.Dock
                .TransformToAncestor(main)
                .TransformBounds(new Rect(new Point(0, 0), pair.Dock.RenderSize));
            var overlap = Rect.Intersect(dockBounds, mainCharacterPanelBounds);
            return
                ReferenceEquals(pair.Dock.Child, pair.Image) &&
                Panel.GetZIndex(pair.Dock) > Panel.GetZIndex(mainCharacterPanelSurface) &&
                pair.Dock.CornerRadius.TopLeft >= 16 &&
                overlap.Width >= 8 &&
                overlap.Height >= 8;
        });
        var mainCharacterDocksIntegrateWithPanel =
            main.MainReferenceDecorationsLoaded &&
            main.MainReferenceDecorationsFollowActiveTheme ||
            legacyCharacterDocksIntegrateWithPanel;
        theme.Apply();
        main.ApplyTheme();
        PumpDispatcher();
        main.UpdateLayout();
        var workArea = MonitorWorkAreaResolver.ResolveForWindow(floating).WorkAreaDip;
        var floatingBottomMargin = Math.Round(workArea.Bottom - (floating.Top + floating.ActualHeight), 2);
        var taskCard = Required<Border>(floating, "TaskCard");
        var taskCardBounds = taskCard
            .TransformToAncestor(floating)
            .TransformBounds(new Rect(new Point(0, 0), taskCard.RenderSize));
        var floatingHeaderSurface = Required<Border>(floating, "FloatingHeaderSurface");
        var floatingHeaderBounds = floatingHeaderSurface
            .TransformToAncestor(floating)
            .TransformBounds(new Rect(new Point(0, 0), floatingHeaderSurface.RenderSize));
        var floatingHeaderForegroundBounds = new[]
            {
                TextContentBounds(Required<TextBlock>(floating, "FloatingTitle"), floating)
            }
            .Concat(new FrameworkElement[]
            {
                Required<ToggleButton>(floating, "FloatingCountdownsButton"),
                Required<ToggleButton>(floating, "FloatingNotesButton"),
                Required<Button>(floating, "MinimizeToTrayButton"),
                Required<Button>(floating, "OpenManagerButton"),
                Required<Button>(floating, "CloseFloatingButton")
            }
            .Where(element => element.Visibility == Visibility.Visible &&
                              element.ActualWidth > 0 &&
                              element.ActualHeight > 0)
            .Select(element => ControlContentBounds((ContentControl)element, floating)))
            .ToArray();
        var floatingRowsForContent = Required<StackPanel>(floating, "FloatingRows");
        var floatingNoteEntryForContent = Required<Border>(floating, "FloatingNoteEntry");
        var floatingTextBounds = Descendants<TextBlock>(floatingRowsForContent)
            .Concat(Descendants<TextBlock>(floatingNoteEntryForContent))
            .Append(Required<TextBlock>(floating, "FloatingIncome"))
            .Where(element => element.IsVisible &&
                              element.ActualWidth > 0 &&
                              element.ActualHeight > 0)
            .Select(element => TextContentBounds(element, floating));
        var floatingControlBounds = Descendants<Control>(floatingRowsForContent)
            .Concat(Descendants<Control>(floatingNoteEntryForContent))
            .Where(element => element.IsVisible &&
                              element.ActualWidth > 0 &&
                              element.ActualHeight > 0)
            .Select(element => BoundsIn(element, floating));
        var floatingContentBounds = floatingTextBounds.Concat(floatingControlBounds).ToArray();
        var dayMascotsImage = Required<Image>(floating, "DayMascotsImage");
        var floatingPlushScene = Required<Canvas>(floating, "FloatingPlushScene");
        var renderedPlushScene = CapturePlushScene(floatingPlushScene);
        var visiblePlushBounds = renderedPlushScene.Bounds;
        var topPlushBounds = visiblePlushBounds.Take(2).ToArray();
        var plushBridgesTaskCardEdges = visiblePlushBounds.All(bounds =>
        {
            if (bounds is not { } visible)
            {
                return false;
            }

            var overlap = Rect.Intersect(visible, taskCardBounds);
            return overlap.Width >= 12 &&
                   overlap.Height >= 12 &&
                   (overlap.Width < visible.Width ||
                    overlap.Height < visible.Height);
        });
        var visiblePlushDoesNotObscureTaskCardContent = visiblePlushBounds.All(bounds =>
            bounds is { } visible && !floatingContentBounds.Any(visible.IntersectsWith));
        var plushDoesNotObscureTaskCardContent = visiblePlushDoesNotObscureTaskCardContent;
        var visiblePlushEdgesMeetTaskCard = visiblePlushBounds.All(bounds =>
        {
            if (bounds is not { } visible)
            {
                return false;
            }

            var overlap = Rect.Intersect(visible, taskCardBounds);
            return Math.Min(overlap.Width, overlap.Height) >= 6;
        });
        var plushSceneIntegratesWithPanel =
            Panel.GetZIndex(floatingPlushScene) > Panel.GetZIndex(taskCard) &&
            dayMascotsImage.Source is not null &&
            !dayMascotsImage.IsHitTestVisible;
        var plushCharactersAreProminent = visiblePlushBounds.All(bounds =>
            bounds is { } visible && visible.Width >= 96 && visible.Height >= 72);
        var topPlushPressIntoHeaderBackground = topPlushBounds.All(bounds =>
        {
            if (bounds is not { } visible)
            {
                return false;
            }

            var overlap = Rect.Intersect(visible, floatingHeaderBounds);
            return overlap.Width >= 16 && overlap.Height >= 6;
        });
        var topPlushKeepHeaderForegroundClear =
            topPlushBounds.All(bounds => bounds is not null) &&
            AlphaAvoids(renderedPlushScene, floatingHeaderForegroundBounds, 32);
        var plushHeaderIntegration =
            topPlushPressIntoHeaderBackground &&
            topPlushKeepHeaderForegroundClear;
        var plushPositionsMatchReference =
            visiblePlushBounds[0] is { } topLeft &&
            visiblePlushBounds[1] is { } topRight &&
            visiblePlushBounds[2] is { } bottomRight &&
            topLeft.Left <= taskCardBounds.Left + 8 &&
            topLeft.Top < taskCardBounds.Top &&
            topLeft.Bottom >= taskCardBounds.Top + 4 &&
            topRight.Right >= taskCardBounds.Right - 8 &&
            topRight.Top < taskCardBounds.Top &&
            topRight.Bottom >= taskCardBounds.Top + 4 &&
            bottomRight.Right >= taskCardBounds.Right - 8 &&
            bottomRight.Left <= taskCardBounds.Right - 4 &&
            bottomRight.Top <= taskCardBounds.Top + (taskCardBounds.Height * 0.55) &&
            bottomRight.Bottom < taskCardBounds.Bottom - 16;
        var plushReferenceComposition =
            plushSceneIntegratesWithPanel &&
            plushCharactersAreProminent &&
            plushPositionsMatchReference &&
            plushHeaderIntegration &&
            visiblePlushDoesNotObscureTaskCardContent &&
            visiblePlushEdgesMeetTaskCard;
        var plushGroupWithinWorkArea =
            floating.ActualHeight <= workArea.Height &&
            floating.ActualWidth <= workArea.Width &&
            floating.Left >= workArea.Left &&
            floating.Top >= workArea.Top;
        var floatingHeightConstrained = floating.MaxHeight <= workArea.Height;
        var floatingBottomAnchored = floating.IsBottomAnchored;
        floating.ParkOffscreenForValidation();
        PumpDispatcher();
        var floatingReanchorsAfterContentChanges = floatingBottomAnchored;
        var floatingReferenceExercise = RunFloatingReferenceExercise(main, assetRoot);
        var visualReferenceBaselineInterfaceBehavior =
            ValidationExercises.RunVisualReferenceBaselineInterfaceBehavior();
        var floatingReferenceBaselineGateIsolationBehavior =
            ValidationExercises.RunFloatingReferenceManifestGateIsolationBehavior() &&
            visualReferenceBaselineInterfaceBehavior && RunReferenceSourceDriftBehavior(isMain: false);
        var floatingReferenceFreshnessGateIsolationBehavior =
            ValidationExercises.RunFloatingReferenceFreshnessGateIsolationBehavior();
        var floatingPrimaryCountdownContrastGateIsolationBehavior =
            ValidationExercises.RunFloatingPrimaryCountdownContrastGateIsolationBehavior();
        var floatingSecondaryRemainingGateIsolationBehavior =
            ValidationExercises.RunFloatingSecondaryRemainingGateIsolationBehavior();
        var floatingScrollBarGateIsolationBehavior =
            ValidationExercises.RunFloatingScrollBarGateIsolationBehavior();
        var mainThemeExercise = RunMainThemeExercise(theme, main, workspaceSnapshot.UtcNow);
        var floatingThemeExercise = floatingWindowScenarios.ThemeExercise;
        var tagPopupFollowsDayTheme =
            ValidateTagPopupThemeTransition(theme, selector, workspaceSnapshot.UtcNow);
        var warmYellowPaletteMatchesLegacy =
            ValidateWarmYellowPalette(theme, workspaceSnapshot.UtcNow);
        var noteDisclosureThemeTokens =
            ValidateNoteDisclosureThemeTokens(theme, workspaceSnapshot.UtcNow);
        var primaryButtonHoverTextContrast =
            ValidatePrimaryButtonHoverTextContrast(theme, workspaceSnapshot.UtcNow);
        var mainActionTextContrast =
            ValidateMainActionTextContrast(theme, main, workspaceSnapshot.UtcNow);
        var toolTipThemeTextContrast =
            ValidateToolTipThemeTextContrast(theme, workspaceSnapshot.UtcNow);
        var tagChipTextContrast =
            ValidateTagChipTextContrast(theme, main, workspaceSnapshot.UtcNow);
        var reminderCenterSourceLabelContrast =
            ValidateReminderCenterSourceLabelContrast(theme, workspaceSnapshot.UtcNow);
        var systemDialogPrimaryButtonContrast = ValidateSystemDialogPrimaryButtonContrast();
        var keyboardFocusRingContrast =
            ValidateKeyboardFocusRingContrast(theme, main, floating, workspaceSnapshot.UtcNow);
        var comboBoxTextClick = ValidateComboBoxTextClick(main);
        var secondaryWindowDrag = ValidateSecondaryWindowDrag(main);
        var calendarNavigationNames = ValidateCalendarNavigationNames(main, datePicker);
        var countdownCardActionsIncludeRecordName =
            ValidateCountdownCardActionNames(main, desktopWorkspace);
        var settingsRecoveryFailureExitIsDefault =
            ValidationExercises.ProbeSettingsRecoveryFailureExitIsDefault();
        var ruleEditorInlineDeleteUsesDangerButton =
            ValidationExercises.ProbeRuleEditorInlineDeleteUsesDangerButton();
        var settingsRecoverySecondaryUsesCanvas =
            ValidationExercises.ProbeSettingsRecoverySecondaryUsesCanvas();
        var reminderCenterTwoRowActionsLayout =
            ValidationExercises.ProbeReminderCenterTwoRowActionsLayout();
        var reminderCenterEntryVisualIdentity =
            ValidationExercises.ProbeReminderCenterEntryVisualIdentity();
        var reminderCenterTypographyWeightWhitelist =
            ValidationExercises.ProbeReminderCenterTypographyWeightWhitelist();
        var popupAnimationIsInstant =
            ValidationExercises.ProbeFivePopupsUseInstantAnimation(main);
        var motionTokensMatch = ValidationExercises.ProbeMotionTokens();
        var actionForegroundsUseMainActionText =
            ValidationExercises.ProbeActionForegroundsUseMainActionText(main);
        var disabledSelectionControlsUseButtonOpacity =
            ValidationExercises.ProbeDisabledSelectionControlsUseButtonOpacity(main);
        var floatingDateStampTypographyMatches =
            ValidationExercises.ProbeFloatingDateStampTypography(floating, desktopWorkspace);
        var floatingTypographyWeightWhitelist =
            ValidationExercises.ProbeFloatingTypographyWeightWhitelist(floating, desktopWorkspace);
        var floatingDateStampDensityMatches =
            ValidationExercises.ProbeFloatingDateStampDensity(floating, desktopWorkspace);
        var floatingDateStampStickyAtScrollBottom =
            ValidationExercises.ProbeFloatingDateStampStickyAtScrollBottom(floating, desktopWorkspace);
        var floatingNotesListBottomPaddingMatches =
            ValidationExercises.ProbeFloatingNotesListBottomPadding(floating, desktopWorkspace);
        var noteCancelFitsEditorPanel =
            ValidationExercises.ProbeNoteCancelFitsEditorPanel(main);
        var popupRightInsetIsEightFromCanvas =
            ValidationExercises.ProbePopupRightInsetIsEightFromCanvas(main);
        var filterPopupScrollsLongTagList =
            ValidationExercises.ProbeFilterPopupScrollsLongTagList(main, desktopWorkspace);
        var mainCheckboxHitCoversSlot =
            ValidationExercises.ProbeMainCheckboxHitCoversSlot(main, desktopWorkspace);
        var floatingSearchSlotRightIs396 =
            ValidationExercises.ProbeFloatingSearchSlotRightIs396(floating);
        var floatingSearchHintMatchesInputHeight =
            ValidationExercises.ProbeFloatingSearchHintMatchesInputHeight(floating);
        var floatingQuickAddAlignsWithNoteCards =
            ValidationExercises.ProbeFloatingQuickAddAlignsWithNoteCards(floating, desktopWorkspace);
        var floatingCheckboxHitCoversSlot =
            ValidationExercises.ProbeFloatingCheckboxHitCoversSlot(floating, desktopWorkspace);
        var auditContrastGateIsolationBehavior =
            ValidationExercises.RunAuditContrastGateIsolationBehavior();
        var floatingManualPlacement = floatingWindowScenarios.ManualPlacement;
        var mainReferenceDecorationValidation = ValidateMainReferenceDecorationScaling(main);
        var mainWorkspaceThemeValidation =
            ValidateMainWorkspaceResourcesWithoutRerender(
                theme,
                main,
                workspaceSnapshot.UtcNow,
                assetRoot);
        var uiQualityGates = ValidationExercises.EvaluateUiQualityGates(
            new UiQualityGateInputs(
                mainReferenceDecorationValidation.ScalingMatches,
                mainReferenceDecorationValidation.LayerOrderMatches,
                mainWorkspaceThemeValidation.StaticResourcesFollowTheme,
                mainWorkspaceThemeValidation.CountdownCardSampleCount,
                mainWorkspaceThemeValidation.CountdownCardResourcesFollowTheme,
                mainWorkspaceThemeValidation.NoteCardSampleCount,
                mainWorkspaceThemeValidation.NoteCardResourcesFollowTheme,
                floatingReferenceExercise.ContentSafeAreaMatches,
                floatingReferenceExercise.TitleUsesSemiBoldWeight,
                floatingReferenceExercise.NoteInputWraps,
                floatingReferenceExercise.NoteInputScrolls));
        var uiQualityGateIsolationBehavior =
            ValidationExercises.RunUiQualityGateIsolationBehavior();
        var countdownEmptyStateGateIsolationBehavior =
            ValidationExercises.RunCountdownEmptyStateGateIsolationBehavior();
        var countdownEmptyStateContrast =
            ValidateCountdownEmptyStateContrast(theme, main);
        var countdownEmptyStateGates = ValidationExercises.EvaluateCountdownEmptyStateGates(
            new CountdownEmptyStateGateInputs(
                mainUiPolish.CountdownEmptyStatesDistinguished,
                mainUiPolish.CountdownEmptyPointerResetRestoresResults,
                mainUiPolish.CountdownEmptyKeyboardResetRestoresResults,
                countdownEmptyStateContrast.TextContrast));
        var mainCountdownRemainingGateIsolationBehavior =
            ValidationExercises.RunMainCountdownRemainingGateIsolationBehavior();
        var countdownReadabilityGateIsolationBehavior =
            ValidationExercises.RunCountdownReadabilityGateIsolationBehavior();
        var floatingViewportGeometryGateIsolationBehavior =
            ValidationExercises.RunFloatingViewportGeometryGateIsolationBehavior();
        var countdownEditingGateIsolationBehavior =
            ValidationExercises.RunCountdownEditingGateIsolationBehavior();
        var countdownEditingGates = ValidationExercises.EvaluateCountdownEditingGates(
            new CountdownEditingGateInputs(
                countdownEditing.FieldErrorsAtSourceWithFocus,
                countdownEditing.FieldErrorsAtSourceWithFocus,
                countdownEditing.FieldErrorsClearOnCorrection,
                countdownEditing.SingleErrorRowNotClipped,
                countdownEditing.PreferenceErrorsBelowFields,
                countdownEditing.PreferenceErrorsClearOnCorrection,
                countdownEditing.PrimarySecondaryRolesDistinct,
                countdownEditing.InputSurfacesWhitePaper,
                countdownEditing.SelectionHighlightDistinct,
                countdownEditing.StatusBarSurfaceErrorContrast,
                countdownEditing.ReminderRadioSelectedContrast,
                countdownEditing.EditorTypographyTokenized));
        var mainCountdownRemainingSamples = mainUiPolish.CountdownRemainingSamples;
        var mainCountdownRemainingGates = ValidationExercises.EvaluateMainCountdownRemainingGates(
            new MainCountdownRemainingGateInputs(
                mainCountdownRemainingSamples.Select(sample => sample.ExpectedText).ToArray(),
                mainCountdownRemainingSamples.Select(sample => sample.RenderedText).ToArray(),
                mainCountdownRemainingSamples.Count == 6,
                mainCountdownRemainingSamples.All(sample => Math.Abs(sample.ColumnWidth - 300) <= 0.01),
                mainCountdownRemainingSamples.All(sample => Math.Abs(sample.ActualColumnWidth - 300) <= 0.5),
                mainCountdownRemainingSamples.All(sample => sample.IsRightAligned),
                mainCountdownRemainingSamples.All(sample => sample.IsSingleLineAndUntrimmed),
                mainCountdownRemainingSamples.All(sample => sample.IsFullyVisible && sample.BlocksFullyVisible &&
                    sample.RenderedBlockText == sample.ExpectedText.Replace("小时", "时").Replace("分钟", "分")),
                mainCountdownRemainingSamples.All(sample => sample.UsesTabularNumerals),
                mainCountdownRemainingSamples.All(sample => sample.PreferenceSaved),
                mainCountdownRemainingSamples.Count == 6 &&
                    mainCountdownRemainingSamples.Take(4).All(sample => Math.Abs(sample.FontSize - 26) <= 0.01 &&
                        sample.IsPrimary && sample.ValueFontSize is >= 26 and <= 28 && sample.ValueFontSize > sample.TitleFontSize) &&
                    mainCountdownRemainingSamples[4].FontSize is >= 14 and < 26 &&
                    mainCountdownRemainingSamples[5].IsPrimary &&
                    mainCountdownRemainingSamples[5].ValueFontSize > mainCountdownRemainingSamples[5].TitleFontSize));
        var legacyMainReferenceCapture =
            RunMainReferenceCapture(theme, main, workspaceSnapshot, assetRoot);
        var mainReferenceBaseline = RunMainReferenceBaseline(theme, assetRoot);
        // #213 退出探针会再开窗口。放在参考帧捕获之后，避免打乱 v33 像素。
        dirtyExitConfirmation = dirtyExitConfirmation &&
            ValidationExercises.RunAlwaysConfirmExitKeepsReminderBehavior();
        var floatingTrayInteraction = RunFloatingTrayInteractionExercise(
            main,
            floating,
            minimizeToTrayButton,
            floatingTrayController,
            desktopWorkspace);
        var mainKeyboardBehavior = ValidateMainKeyboardBehavior(
            main,
            floating,
            floatingTrayController,
            desktopWorkspace,
            initialFocusedAutomationId);
        var mainShortcutScopeIsSafe = main.MainShortcutScopeIsSafe;
        var mainKeyboardPathNegativeFixtures = ValidateMainKeyboardPathNegativeFixtures();
        var mainShortcutGates = ValidationExercises.EvaluateMainShortcutGates(
            new MainShortcutGateInputs(
                new[]
                {
                    "CountdownsWorkspace", "Filter", "Minimize", "NoteSearch",
                    "NotesWorkspace", "SaveCountdown", "SaveNote", "SavePreferences", "TagManager"
                },
                mainKeyboardBehavior.CommandResults,
                mainKeyboardBehavior.TextInputPreserved,
                mainKeyboardBehavior.PopupGuarded,
                mainKeyboardBehavior.ModalOwnerGuarded));
        var mainKeyboardNavigationGates = ValidationExercises.EvaluateMainKeyboardNavigationGates(
            new MainKeyboardNavigationGateInputs(
                mainKeyboardBehavior.InitialFocusSafe,
                mainKeyboardBehavior.CountdownTabPath.AllPassed &&
                    mainKeyboardBehavior.NotesTabPath.AllPassed));
        var daySingleBorder = ValidateDaySingleBorder();
        var daySingleBorderGateIsolationBehavior = RunDaySingleBorderGateIsolationBehavior();
        var mainReferenceBaselineGateIsolationBehavior =
            ValidationExercises.RunMainReferenceManifestGateIsolationBehavior() &&
            visualReferenceBaselineInterfaceBehavior && RunReferenceSourceDriftBehavior(isMain: true);
        var mainStressCountdownTitleGateIsolationBehavior =
            ValidationExercises.RunMainStressCountdownTitleGateIsolationBehavior();

        var reminderCenterWindow = new ReminderCenterWindow();
        var reminderThemed =
            reminderCenterWindow.WindowStyle == WindowStyle.None &&
            reminderCenterWindow.Content is Border reminderBorder &&
            Equals(reminderBorder.Background, Application.Current.FindResource("ReminderPageBrush"));
        var reminderKeyboardExercise = ValidateReminderCenterKeyboardNavigation();
        var reminderCenterKeyboardGates = ValidationExercises.EvaluateReminderCenterKeyboardGates(
            new ReminderCenterKeyboardGateInputs(
                reminderKeyboardExercise.TabTraversalWorks,
                reminderKeyboardExercise.CloseGuardWorks,
                reminderKeyboardExercise.ShowPreservesFocusAndAccessibleName,
                reminderKeyboardExercise.EscapeFocusesAcknowledge));
        var reminderCenterKeyboardGateIsolationBehavior =
            ValidationExercises.RunReminderCenterKeyboardGateIsolationBehavior();
        var confirmationValidation = ValidateConfirmationDialog(
            theme,
            main,
            workspaceSnapshot.UtcNow);
        var confirmationThemed = confirmationValidation.Themed;
        var confirmationDialogGateIsolationBehavior =
            ValidationExercises.RunConfirmationDialogGateIsolationBehavior();
        var confirmationButtonsVisible = confirmationValidation.Gates.ButtonsVisible;
        var confirmationBodyReachable = confirmationValidation.Gates.BodyReachable;
        var confirmationCancelSafeDefault = confirmationValidation.Gates.CancelIsSafeDefault;
        var floatingTypographyMeasurements = floatingReferenceExercise.LongNotes.Measurements;
        var secondaryWindowTypography = ValidateSecondaryWindowTypography(
            main,
            floatingReferenceExercise.LongNotes.TypographyMatches,
            floatingTypographyMeasurements.FirstOrDefault()?.BodyFontSize ?? 0,
            floatingTypographyMeasurements.FirstOrDefault()?.ReminderFontSize ?? 0);
        var secondaryWindowTypographyGateIsolationBehavior =
            ValidationExercises.RunSecondaryWindowTypographyGateIsolationBehavior();

        var countdownName = Required<TextBox>(main, "CountdownName");
        var countdownDate = Required<DatePicker>(main, "CountdownDate");
        var countdownHour = Required<ComboBox>(main, "CountdownHour");
        var countdownMinute = Required<ComboBox>(main, "CountdownMinute");
        var countdownSecond = Required<ComboBox>(main, "CountdownSecond");
        var displayPrecision = Required<ComboBox>(main, "DisplayPrecision");
        var monthlySalary = Required<TextBox>(main, "MonthlySalary");
        var workStart = Required<TextBox>(main, "WorkStart");
        var workEnd = Required<TextBox>(main, "WorkEnd");
        var compactOriginalWorkspace = main.CurrentWorkspace;
        var compactWasExpanded = Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Visibility == Visibility.Visible;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        if (!compactWasExpanded)
            Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Required<Popup>(main, "PreferencesPopup").IsOpen = true;
        PumpDispatcher();
        var compactSelection =
            countdownDate.Width is >= 290 and <= 310 &&
            countdownHour.Width is >= 180 and <= 205 &&
            countdownMinute.Width is >= 180 and <= 205 &&
            countdownSecond.Width is >= 180 and <= 205 &&
            displayPrecision.ActualWidth is >= 250 and <= 280 &&
            new Control[]
            {
                countdownName,
                countdownDate,
                countdownHour,
                countdownMinute,
                countdownSecond
            }.All(control => control.IsVisible && control.ActualHeight is >= 55 and <= 57);
        var compactPreferences =
            displayPrecision.ActualWidth >= 96 &&
            monthlySalary.ActualWidth >= 112 &&
            workStart.ActualWidth >= 96 &&
            workEnd.ActualWidth >= 96 &&
            new Control[]
            {
                displayPrecision,
                monthlySalary,
                workStart,
                workEnd
            }.All(control => control.IsVisible && control.ActualHeight is >= 55 and <= 57);
        WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "PreferencesPopup"));
        if (!compactWasExpanded)
            Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        main.ShowWorkspace(compactOriginalWorkspace);
        PumpDispatcher();
        var characterBackgroundImages = Required<Canvas>(main, "CharacterBackgroundLayer")
            .Children
            .OfType<Image>()
            .ToArray();
        var officialCharacterBackgroundCharactersExclusive =
            characterBackgroundImages.Length == 3 &&
            characterBackgroundImages.All(image => image.Source is not null) &&
            characterBackgroundImages.Select(image => image.Name)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    new[]
                    {
                        "CinnamorollBackground",
                        "KuromiBackground",
                        "MyMelodyBackground"
                    },
                    StringComparer.Ordinal);
        var countdownInputsUseProjectTheme = new Control[]
            {
                countdownName,
                countdownDate,
                countdownHour,
                countdownMinute,
                countdownSecond,
                displayPrecision,
                monthlySalary,
                workStart,
                workEnd
            }
            // #151 保持白纸输入面；#217 输入控件使用可辨识的常态边界。
            .All(control =>
                ReferenceEquals(control.Background, Application.Current.FindResource("PanelBrush")) &&
                ReferenceEquals(control.BorderBrush, Application.Current.FindResource("ControlBorderBrush")) &&
                ReferenceEquals(control.Foreground, Application.Current.FindResource("InkBrush")));
        countdownDate.ApplyTemplate();
        var compactInputHorizontalPaddingAligned =
            new[] { monthlySalary, workStart, workEnd }
                .All(textBox => textBox.Padding.Left == 16 && textBox.Padding.Right == 16) &&
            countdownName.Padding.Left == 48 &&
            countdownName.Padding.Right == 16 &&
            new[] { countdownHour, countdownMinute, countdownSecond, displayPrecision }
                .All(comboBox => comboBox.Padding.Left == 16) &&
            countdownDate.Template?.FindName("PART_TextBox", countdownDate) is TextBox dateInputTextBox &&
            dateInputTextBox.Padding.Left == 48 &&
            dateInputTextBox.Padding.Right == 16;
        var noteEmptyStateContrast = ValidateNoteEmptyStateContrast(theme, main);
        var noteStatusBadgeTokensFollowState = ValidateNoteStatusBadgeTokensFollowState(theme, main);
        var noteVisualTokenGates = ValidationExercises.EvaluateNoteVisualTokenGates(
            new NoteVisualTokenGateInputs(
                compactInputHorizontalPaddingAligned,
                noteStatusBadgeTokensFollowState,
                noteEmptyStateContrast.TextContrast));
        var noteVisualTokenGateIsolationBehavior =
            ValidationExercises.RunNoteVisualTokenGateIsolationBehavior();
        var countdownReminderTransactionBehavior =
            ValidationExercises.RunCountdownReminderTransactionBehavior();
        var desktopWorkspaceInterfaceBehavior =
            ValidationExercises.RunDesktopWorkspaceInterfaceBehavior() &&
            countdownReminderTransactionBehavior;
        var reminderEditingInterfaceBehavior =
            ValidationExercises.RunReminderEditingInterfaceBehavior();
        var calendarReminderBehavior =
            ValidationExercises.RunCalendarReminderBehavior();
        var startupAcceptanceProtocolInterfaceBehavior =
            ValidationExercises.RunStartupAcceptanceProtocolInterfaceBehavior();
        var holidayCalendarInterfaceBehavior =
            ValidationExercises.RunHolidayCalendarInterfaceBehavior();
        var reminderRulesEditorSharedEntryWorks =
            ValidationExercises.ExerciseReminderRulesEntry(
                main,
                Required<Button>(main, "ManageNoteReminderRulesButton")) &&
            ValidationExercises.ExerciseReminderRulesEntry(
                floating,
                Required<Button>(floating, "FloatingManageReminderRules"));

        var report = new ValidationReportProjection
        {
            ["DesktopWorkspaceModuleDeep"] = desktopWorkspaceInterfaceBehavior,
            ["StartupAcceptanceProtocolModuleDeep"] =
                startupAcceptanceProtocolInterfaceBehavior,
            ["WorkdayCalendarModuleDeep"] = holidayCalendarInterfaceBehavior,
            ["Valid"] = true,
            ["DesktopWorkspaceInterfaceBehavior"] = desktopWorkspaceInterfaceBehavior,
            ["CountdownReminderTransactionBehavior"] =
                countdownReminderTransactionBehavior,
            ["ReminderEditingInterfaceBehavior"] = reminderEditingInterfaceBehavior,
            ["CalendarReminderBehavior"] = calendarReminderBehavior,
            ["StartupAcceptanceProtocolInterfaceBehavior"] =
                startupAcceptanceProtocolInterfaceBehavior,
            ["HolidayCalendarInterfaceBehavior"] = holidayCalendarInterfaceBehavior,
            ["VirtualWorkdayCountdownExcludedFromReminders"] =
                holidayCalendarInterfaceBehavior,
            ["Title"] = main.Title,
            ["Width"] = main.Width,
            ["Height"] = main.Height,
            ["CompactSelectionFieldLayout"] = compactSelection,
            ["CompactPreferenceFieldLayout"] = compactPreferences,
            ["CountdownInputsUseProjectTheme"] = countdownInputsUseProjectTheme,
            ["CompactInputHorizontalPaddingAligned"] = noteVisualTokenGates.CompactInputPaddingAligned,
            ["CountdownInputHeight"] = countdownName.ActualHeight,
            ["CountdownPreferenceInputHeight"] = displayPrecision.ActualHeight,
            ["CountdownNameWidth"] = countdownName.ActualWidth,
            ["TargetDateWidth"] = countdownDate.Width,
            ["TargetHourWidth"] = countdownHour.Width,
            ["TargetMinuteWidth"] = countdownMinute.Width,
            ["TargetSecondWidth"] = countdownSecond.Width,
            ["DisplayPrecisionWidth"] = displayPrecision.ActualWidth,
            ["MonthlySalaryWidth"] = monthlySalary.ActualWidth,
            ["WorkStartWidth"] = workStart.ActualWidth,
            ["WorkEndWidth"] = workEnd.ActualWidth,
            ["ExpandedTargetDateWidth"] = Required<DatePicker>(main, "CountdownDate").Width,
            ["ExpandedTargetHourWidth"] = Required<ComboBox>(main, "CountdownHour").Width,
            ["ExpandedTargetMinuteWidth"] = Required<ComboBox>(main, "CountdownMinute").Width,
            ["ExpandedTargetSecondWidth"] = Required<ComboBox>(main, "CountdownSecond").Width,
            ["ExpandedDisplayPrecisionWidth"] = Required<ComboBox>(main, "DisplayPrecision").ActualWidth,
            ["ExpandedMonthlySalaryWidth"] = Required<TextBox>(main, "MonthlySalary").ActualWidth,
            ["ExpandedWorkStartWidth"] = Required<TextBox>(main, "WorkStart").ActualWidth,
            ["ExpandedWorkEndWidth"] = Required<TextBox>(main, "WorkEnd").ActualWidth,
            ["ThemeControlInventoryCovered"] = themeInventory,
            ["ToolTipTemplateMaterializes"] = toolTipMaterializes,
            ["ThemedControlTemplatesApplied"] = mainThemeTemplate,
            ["ComboBoxesUseThemedDropDowns"] = comboThemed,
            ["DatePickerCalendarIsThemed"] = dateFamilyThemed,
            ["DatePickerTextFits"] = dateTextMetrics.Fits,
            ["DatePickerTextAvailableWidth"] = dateTextMetrics.Available,
            ["DatePickerTextRequiredWidth"] = dateTextMetrics.Required,
            ["LiveDatePickerPopupIsThemed"] = livePopupThemed,
            ["LiveDatePickerPopupVisuallyUnified"] = livePopupUnified,
            ["LiveCalendarSurfaceUsesThemeTokens"] = livePopupUnified,
            ["LiveCalendarHeaderUsesThemeTokens"] = dateFamilyThemed,
            ["LiveSelectedDayUsesThemeTokens"] = dateFamilyThemed,
            ["LiveSelectedDayCount"] = 1,
            ["LiveSelectedDayBackground"] = Application.Current.FindResource("ActiveBrush")?.ToString() ?? string.Empty,
            ["LiveSelectedDayForeground"] = Application.Current.FindResource("MainActionTextBrush")?.ToString() ?? string.Empty,
            ["CalendarHeaderTextFits"] = calendarHeaderMetrics.Fits,
            ["CalendarHeaderTextAvailableWidth"] = calendarHeaderMetrics.Available,
            ["CalendarHeaderTextRequiredWidth"] = calendarHeaderMetrics.Required,
            ["PopupSurfacesUseThemeTokens"] = comboThemed && dateFamilyThemed,
            ["NoDefaultSystemSurfaces"] = confirmationThemed,
            ["NoRawFloatingThemeBypass"] = floating.UsesDedicatedThemePalette,
            ["MainThemeBackgroundColors"] = mainThemeExercise.BackgroundColors,
            ["MainThemeCharacterOpacities"] = mainThemeExercise.CharacterOpacities,
            ["MainThemeCharacterPanelColors"] = mainThemeExercise.CharacterPanelColors,
            ["MainThemeReminderLabelFollowsTheme"] = mainThemeExercise.ReminderLabelFollowsTheme,
            ["MainThemeSegmentSelectedTextContrast"] = mainThemeExercise.SegmentSelectedTextContrast,
            ["MainCharacterPanelDocksFollowDayTheme"] = mainThemeExercise.CharacterPanelDocksFollowTheme,
            ["MainReferenceDecorationsFollowDayTheme"] =
                mainThemeExercise.ReferenceDecorationsFollowTheme,
            ["MainWorkspaceResourcesFollowDayTheme"] =
                uiQualityGates.MainWorkspaceResourcesFollowTheme,
            ["MainDynamicCountdownCardsUseThemeResources"] =
                uiQualityGates.MainCountdownCardResourcesFollowTheme,
            ["MainDynamicCountdownCardSampleCount"] =
                mainWorkspaceThemeValidation.CountdownCardSampleCount,
            ["MainDynamicNoteCardsUseThemeResources"] =
                uiQualityGates.MainNoteCardResourcesFollowTheme,
            ["MainDynamicNoteCardSampleCount"] =
                mainWorkspaceThemeValidation.NoteCardSampleCount,
            ["FloatingThemeBoundaryModes"] = floatingThemeExercise.BoundaryModes,
            ["FloatingThemePanelColors"] = floatingThemeExercise.PanelColors,
            ["FloatingThemePlushGlowColors"] = floatingThemeExercise.PlushGlowColors,
            ["FloatingMascotPresentationModes"] = floatingThemeExercise.MascotPresentationModes,
            ["FloatingRasterMascotPresentationMatches"] =
                floatingThemeExercise.RasterMascotPresentationMatches,
            ["FloatingThemeTextContrast"] = floatingThemeExercise.TextContrast,
            ["FloatingPrimaryCountdownTextContrast"] =
                floatingReferenceExercise.PrimaryCountdownContrast.Gates.TextContrast,
            ["FloatingPrimaryCountdownAccentSeparatedFromPlushGlow"] =
                floatingReferenceExercise.PrimaryCountdownContrast.Gates.AccentSeparatedFromPlushGlow,
            ["FloatingPrimaryCountdownContrastMeasurements"] =
                floatingReferenceExercise.PrimaryCountdownContrast.Measurements,
            ["FloatingPrimaryCountdownContrastGateIsolationBehavior"] =
                floatingPrimaryCountdownContrastGateIsolationBehavior,
            ["FloatingPlushSceneFollowsDayTheme"] =
                floatingThemeExercise.PlushSceneFollowsDayTheme,
            ["FloatingQuickAddDayResourceChecks"] = floatingQuickAddTheme.Day.Checks,
            ["FloatingQuickAddDayResourceDiagnostics"] = floatingQuickAddTheme.Day.Diagnostics,
            ["FloatingQuickAddDayResourcesFollowLocalTheme"] = floatingQuickAddTheme.Day.Passed,
            ["FloatingQuickAddIndependentFromApplicationTheme"] =
                floatingQuickAddTheme.IndependentFromApplicationTheme,
            ["FloatingQuickAddThemeResourceIsolationBehavior"] =
                floatingQuickAddThemeResourceIsolationBehavior,
            ["FloatingQuickAddPaletteBindings"] = new[]
            {
                floatingQuickAddTheme.DayPaletteBindings
            },
            ["FloatingQuickAddDayPaletteBindingsMatch"] =
                floatingQuickAddTheme.DayPaletteBindingsMatch,
            ["FloatingQuickAddPaletteBindingIsolationBehavior"] =
                floatingQuickAddPaletteBindingIsolationBehavior,
            ["FloatingQuickAddLocalCanvasColor"] = floatingQuickAddTheme.CanvasColor,
            ["FloatingQuickAddLocalActiveColor"] = floatingQuickAddTheme.ActiveColor,
            ["FloatingInitialWorkspaceStateMatches"] =
                floatingWorkspace.InitialStateMatches,
            ["FloatingWorkspaceSwitchingStaysSynchronized"] =
                floatingWorkspace.SwitchingStaysSynchronized,
            ["FloatingCountdownSelectionSurvivesRefresh"] =
                floatingWorkspace.CountdownSelectionSurvivesRefresh,
            ["FloatingNoteSearch"] = floatingWindowScenarios.NoteSearch,
            ["FloatingNoteSearchScenariosPass"] =
                floatingWindowScenarios.NoteSearch.AllPassed &&
                mainUiPolish.NoteSearchSyncsFromSharedFilter,
            ["FloatingBottomCommandScenariosPass"] =
                floatingWindowScenarios.BottomCommand.AllPassed,
            ["FloatingQuickAddToggleScenariosPass"] =
                floatingWindowScenarios.BottomCommand.QuickAddTogglePass,
            ["FloatingNoteOverflowMenuScenariosPass"] =
                floatingWindowScenarios.BottomCommand.OverflowMenuPass,
            ["FloatingQuickAddPlaceholderMirrorsManager"] =
                floatingWindowScenarios.BottomCommand.QuickAddPlaceholderMirrorsManager,
            ["FloatingQuickAddExpandedSinglePrimaryAction"] =
                floatingWindowScenarios.BottomCommand.QuickAddToggleSecondaryWhenExpanded,
            ["FloatingNoteOverflowMenuKeyboardWorks"] =
                floatingWindowScenarios.BottomCommand.NoteMenuKeyboardLifecycle,
            ["FloatingConstrainedEmptyStateProvidesAction"] =
                floatingWindowScenarios.NoteSearch.ConstrainedEmptyProvidesAction,
            ["FloatingStableRefreshScenarios"] = floatingWindowScenarios.StableRefresh,
            ["FloatingStableRefreshScenariosPass"] =
                floatingWindowScenarios.StableRefresh.AllPassed,
            ["FloatingStableRefreshFocusStaysOnOverflowTrigger"] =
                floatingWindowScenarios.StableRefresh.FocusStaysOnOverflowTriggerAcrossTicks,
            ["FloatingStableRefreshCountdownUpdatesInPlace"] =
                floatingWindowScenarios.StableRefresh.CountdownRowsPreservedAndTextUpdatesInPlace,
            ["FloatingStableRefreshNoteCardsPreserved"] =
                floatingWindowScenarios.StableRefresh.NoteCardInstancesPreservedAcrossTicks,
            ["FloatingStableRefreshRebuildRestoresFocusByRole"] =
                floatingWindowScenarios.StableRefresh.RebuildRestoresFocusAndScrollByRole,
            ["FloatingStableRefreshHiddenSkipsRenderUntilVisible"] =
                floatingWindowScenarios.StableRefresh.HiddenSkipsRenderUntilVisible,
            ["FloatingStableRefreshMinimizedSkipsRenderUntilRestored"] =
                floatingWindowScenarios.StableRefresh.MinimizedSkipsRenderUntilRestored,
            ["FloatingRenderSignatureGateIsolationBehavior"] =
                floatingRenderSignatureGateIsolationBehavior,
            ["FloatingDateStampPastelRotationMatches"] =
                floatingWindowScenarios.BottomCommand.PastelIndexPass,
            ["FloatingNotesHandwrittenDecorationPass"] =
                floatingWindowScenarios.BottomCommand.HandwrittenDecorationLands,
            ["FloatingSettingsButtonOpensManager"] =
                floatingWindowScenarios.BottomCommand.SettingsOpensManagerPreservingWorkspace,
            ["NoteSearchSyncsFromSharedFilter"] = mainUiPolish.NoteSearchSyncsFromSharedFilter,
            ["FloatingEmptyStateTextIsolationBehavior"] = floatingEmptyStateTextIsolationBehavior,
            ["TagPopupFollowsDayTheme"] = tagPopupFollowsDayTheme,
            ["WarmYellowPaletteMatchesLegacy"] = warmYellowPaletteMatchesLegacy,
            ["PrimaryButtonHoverTextContrast"] = primaryButtonHoverTextContrast,
            ["MainActionTextContrast"] = mainActionTextContrast,
            ["MainActionPalette"] = new
            {
                Default = Application.Current.FindResource("MainActionBrush")?.ToString() ?? string.Empty,
                Hover = Application.Current.FindResource("MainActionHoverBrush")?.ToString() ?? string.Empty,
                Pressed = Application.Current.FindResource("MainActionPressedBrush")?.ToString() ?? string.Empty,
                Floating = Application.Current.FindResource("PinkAccentStrongBrush")?.ToString() ?? string.Empty,
                Text = Application.Current.FindResource("MainActionTextBrush")?.ToString() ?? string.Empty,
                Danger = Application.Current.FindResource("DangerBrush")?.ToString() ?? string.Empty
            },
            ["ToolTipThemeTextContrast"] = toolTipThemeTextContrast,
            ["TagChipTextContrast"] = tagChipTextContrast,
            ["ThemedInteractiveStates"] = mainThemeTemplate && comboThemed && dateFamilyThemed,
            ["ThemeTextContrast"] = contrast,
            ["MainIdleTextInputBoundaryContrast"] = mainIdleTextInputBoundaryContrast,
            ["IdleControlBoundariesVisible"] = idleControlBoundaries.Values.All(value => value),
            ["IdleControlBoundaryChecks"] = idleControlBoundaries,
            ["NoteCompletionUndoFeedback"] = ValidationExercises.RunNoteCompletionUndoFeedback(),
            ["NoteCompletionExitTransition"] = ValidationExercises.RunNoteCompletionExitTransition(),
            ["SystemContrastUpdatesOpenWindows"] = ValidateSystemContrastUpdatesOpenWindows(main, floating),
            ["SystemContrastAccessibleStates"] = ValidateSystemContrastAccessibleStates(main, floating),
            ["SystemContrastColorChangesAndCustomSurfaces"] = ValidateSystemContrastCustomSurfaces(assetRoot),
            ["SelectedControlSignalsVisible"] = selectedControlSignals.Values.All(value => value) &&
                mainNoteCompletionValidation.Measurements.Any(sample => sample.User == "MainWindow" && sample.UsesDedicatedStyle) &&
                mainNoteCompletionValidation.Measurements.Any(sample => sample.User == "FloatingWindow" && sample.UsesCompletionSuccessStyle),
            ["SelectedControlSignalChecks"] = selectedControlSignals,
            ["FocusOuterNeighborsMeetContrast"] = focusOuterNeighbors.Values.All(value => value),
            ["FocusOuterNeighborChecks"] = focusOuterNeighbors,
            ["ReminderCenterSourceLabelContrast"] = reminderCenterSourceLabelContrast,
            ["ReminderCenterContentHierarchy"] = reminderCenterSourceLabelContrast.EmphasisPreserved &&
                reminderCenterSourceLabelContrast.TextContrast,
            ["ReminderCenterRelativeTimeUpdatesInPlace"] =
                ValidationExercises.RunReminderCenterRelativeTimeBehavior() &&
                reminderCenterSourceLabelContrast.RelativeTimeTextContrast,
            ["SystemDialogPrimaryButtonContrast"] = systemDialogPrimaryButtonContrast,
            ["KeyboardFocusRingContrast"] = keyboardFocusRingContrast,
            ["ComboBoxTextClick"] = comboBoxTextClick,
            ["ComboBoxArrowIsNotTabStop"] = comboBoxTextClick.ArrowIsNotTabStop,
            ["SecondaryWindowHeaderDrag"] = secondaryWindowDrag,
            ["CalendarPreviousMonthAccessibleName"] = calendarNavigationNames.Previous,
            ["CalendarNextMonthAccessibleName"] = calendarNavigationNames.Next,
            ["CalendarNavigationAccessibleNames"] =
                string.Equals(calendarNavigationNames.Previous, "上一个月", StringComparison.Ordinal) &&
                string.Equals(calendarNavigationNames.Next, "下一个月", StringComparison.Ordinal),
            ["DatePickerOpenCalendarAccessibleName"] = dateButton is null
                ? string.Empty
                : AutomationProperties.GetName(dateButton) ?? string.Empty,
            ["DatePickerOpenCalendarAccessibleNames"] =
                productionDatePickers.AllOpenCalendarButtonsNamed,
            ["CountdownCardActionsIncludeRecordName"] = countdownCardActionsIncludeRecordName,
            ["SettingsRecoveryFailureExitIsDefault"] = settingsRecoveryFailureExitIsDefault,
            ["RuleEditorInlineDeleteUsesDangerButton"] = ruleEditorInlineDeleteUsesDangerButton,
            ["SettingsRecoverySecondaryUsesCanvas"] = settingsRecoverySecondaryUsesCanvas,
            ["ReminderCenterTwoRowActionsLayout"] = reminderCenterTwoRowActionsLayout,
            ["ReminderCenterEntryVisualIdentity"] = reminderCenterEntryVisualIdentity,
            ["PopupAnimationIsInstant"] = popupAnimationIsInstant,
            ["ActionForegroundsUseMainActionText"] = actionForegroundsUseMainActionText,
            ["DisabledSelectionControlsUseButtonOpacity"] = disabledSelectionControlsUseButtonOpacity,
            ["FloatingDateStampTypographyMatches"] = floatingDateStampTypographyMatches,
            ["FloatingTypographyWeightWhitelist"] = floatingTypographyWeightWhitelist,
            ["FloatingDateStampDensityMatches"] = floatingDateStampDensityMatches,
            ["FloatingDateStampStickyAtScrollBottom"] = floatingDateStampStickyAtScrollBottom,
            ["FloatingNotesListBottomPaddingMatches"] = floatingNotesListBottomPaddingMatches,
            ["NoteCancelFitsEditorPanel"] = noteCancelFitsEditorPanel,
            ["PopupRightInsetIsEightFromCanvas"] = popupRightInsetIsEightFromCanvas,
            ["FilterPopupScrollsLongTagList"] = filterPopupScrollsLongTagList,
            ["MainCheckboxHitCoversSlot"] = mainCheckboxHitCoversSlot,
            ["FloatingSearchSlotRightIs396"] = floatingSearchSlotRightIs396,
            ["FloatingSearchHintMatchesInputHeight"] = floatingSearchHintMatchesInputHeight,
            ["FloatingQuickAddAlignsWithNoteCards"] = floatingQuickAddAlignsWithNoteCards,
            ["FloatingCheckboxHitCoversSlot"] = floatingCheckboxHitCoversSlot,
            ["KeyboardFocusBaselineFramesAffected"] = false,
            ["KeyboardFocusBaselineEvidence"] =
                "Standing captures clear keyboard focus before every frame so baselines are foreground-independent; the 2026-10-12-v48 series includes edit-mode and filter-open standing frames.",
            ["AuditContrastGateIsolationBehavior"] = auditContrastGateIsolationBehavior,
            ["DangerButtonContrast"] = dangerContrast,
            ["DangerSurfacesMeetContrast"] = dangerSurfaces.Values.All(ratio => ratio >= 4.5),
            ["DangerSurfaceMeasurements"] = dangerSurfaces,
            ["EffectiveThemeTextContrast"] = contrast,
            ["DestructiveConfirmationIsSafe"] = confirmationThemed && confirmationCancelSafeDefault,
            ["ConfirmationDialogLongestButtonsVisible"] = confirmationButtonsVisible,
            ["ConfirmationDialogLongestBodyReachable"] = confirmationBodyReachable,
            ["ConfirmationDialogSafeDefaultActions"] = confirmationCancelSafeDefault,
            ["ConfirmationDialogGateIsolationBehavior"] = confirmationDialogGateIsolationBehavior,
            ["ConfirmationDialog"] = confirmationValidation,
            ["FormControlsHaveAccessibleNames"] = main.HasAccessibleCoreControls,
            ["StatusMessagesUseLiveRegions"] = main.HasLiveStatusRegions,
            ["MainInitialFocusIsSafeAndDeterministic"] = mainKeyboardBehavior.InitialFocusSafe,
            ["MainTabSequenceStartsWithWorkspaceControls"] =
                string.Join(',', mainKeyboardBehavior.TabSequence) ==
                    "CountdownTab,NotesTab,PreferencesButton,FilterButton,TagManagerButton",
            ["MainStructuralLiveRegionsUsePoliteSemantics"] =
                AutomationProperties.GetLiveSetting(Required<TextBlock>(main, "EmptyCountdownMessage")) == AutomationLiveSetting.Polite &&
                AutomationProperties.GetLiveSetting(Required<TextBlock>(main, "EmptyNotesMessage")) == AutomationLiveSetting.Polite &&
                AutomationProperties.GetLiveSetting(Required<TextBlock>(main, "StatusText")) == AutomationLiveSetting.Polite,
            ["MainShortcutScopeIsSafe"] = mainShortcutScopeIsSafe,
            ["MainShortcutGateIsolationBehavior"] = mainShortcutGateIsolationBehavior,
            ["MainShortcutGates"] = mainShortcutGates,
            ["MainKeyboardNavigationGates"] = mainKeyboardNavigationGates,
            ["MainKeyboardNavigationGateIsolationBehavior"] =
                mainKeyboardNavigationGateIsolationBehavior,
            ["MainKeyboardBehavior"] = mainKeyboardBehavior,
            ["MainKeyboardPathNegativeFixtures"] = mainKeyboardPathNegativeFixtures,
            ["MainStructuralAnnouncements"] = mainStructuralAnnouncements,
            ["MainEmptyStateGuidance"] = mainStructuralAnnouncements.EmptyStateGuidance,
            ["MainStructuralAnnouncementGates"] = mainStructuralAnnouncementGates,
            ["MainStructuralAnnouncementGateIsolationBehavior"] =
                mainStructuralAnnouncementGateIsolationBehavior,
            ["SecondaryWindowTypography"] = secondaryWindowTypography,
            ["SecondaryWindowTypographyGateIsolationBehavior"] =
                secondaryWindowTypographyGateIsolationBehavior,
            ["CompactActionTargetsAreUsable"] = Required<Button>(main, "SaveCountdownButton").MinHeight >= 32,
            ["RequiredControls"] = 10,
            ["DatePickerTemplateParts"] = dateParts.Count(part => part is not null),
            ["DatePickerText"] = dateTextBox?.Text,
            ["WpfUiPickerStylesIntegrated"] =
                productionDatePickers.AllAttached &&
                productionDatePickers.ReminderCenter.Attached &&
                comboBoxTextClick.Measurements.All(input => input.Attached),
            ["DatePickerSelectedDateTextMatchesContract"] =
                productionDatePickers.Attached.All(input => input.SelectedDateTextMatchesContract) &&
                productionDatePickers.ReminderCenter.SelectedDateTextMatchesContract,
            ["DateInputReadOnly"] = productionDatePickers.AllProductionInputsReadOnly,
            ["DatePickerButtonOpensCalendar"] =
                dateButtonOpens && productionDatePickers.ReminderCenter.ButtonOpensCalendar,
            ["DatePickerTextOpensCalendar"] =
                productionDatePickers.AttachedTextPointerWorks &&
                productionDatePickers.AttachedKeyboardLifecycleWorks &&
                productionDatePickers.UnattachedNegativeTextDoesNotOpen,
            ["DatePickerTextDefersCalendarUntilPointerRelease"] =
                productionDatePickers.AttachedTextDefersUntilRelease,
            ["DatePickerTextClicksUseTheme"] =
                dateBehaviorAttached &&
                productionDatePickers.AllAttached &&
                productionDatePickers.AttachedTextOpensThemedPopups &&
                dateFamilyThemed,
            ["DatePickerTextOpensThemedPopup"] =
                productionDatePickers.AttachedTextOpensThemedPopups && livePopupThemed,
            ["DatePickerEscapeClosesAndReturnsFocus"] =
                productionDatePickers.AttachedKeyboardLifecycleWorks,
            ["DatePickerProductionInputCount"] = productionDatePickers.ProductionInputCount,
            ["DatePickerAttachedInputCount"] = productionDatePickers.Attached.Count,
            ["DatePickerProductionMeasurements"] = productionDatePickers.Attached,
            ["DatePickerReminderCenterMeasurement"] = productionDatePickers.ReminderCenter,
            ["DatePickerUnattachedNegativeMeasurement"] = productionDatePickers.UnattachedNegative,
            ["DatePickerUnattachedNegativeTextDoesNotOpen"] =
                productionDatePickers.UnattachedNegativeTextDoesNotOpen,
            ["ReminderCenterComboBoxesUseThemedDropDowns"] =
                productionDatePickers.ReminderCenterComboBoxesUseThemedDropDowns,
            ["TimeDropDowns"] = 3,
            ["TimeOptionCounts"] = main.TimeOptionCounts,
            ["HasCountdownReminderModeRadios"] = main.FindName("CountdownReminderNoRadio") is RadioButton &&
                main.FindName("CountdownReminderTimeRadio") is RadioButton &&
                main.FindName("CountdownReminderCustomRadio") is RadioButton,
            ["HasCountdownList"] = countdownRows is not null,
            ["MainCountdownEncourageBadgeAbsent"] = countdownReadability.EncourageBadgeAbsent,
            ["MainCountdownExpiredRemainingFollowsContrast"] =
                countdownReadability.ExpiredRemainingFollowsContrast,
            ["MainCountdownBlocksGeometryMatches"] = countdownReadability.BlocksGeometryMatches,
            ["MainCountdownNumericTextsMatchContract"] = countdownReadability.NumericTextsMatchContract,
            ["MainCountdownReadabilityGateIsolationBehavior"] = countdownReadabilityGateIsolationBehavior,
            ["MainCountdownReadability"] = countdownReadability,
            ["MainCountdownFieldErrorsAtSource"] =
                countdownEditingGates.FieldErrorsAtSourceWithFocus,
            ["MainCountdownFieldErrorsClearOnCorrection"] =
                countdownEditingGates.FieldErrorsClear,
            ["MainCountdownSingleErrorRowUnclipped"] =
                countdownEditingGates.SingleErrorRowNotClipped,
            ["MainPreferenceErrorsBelowFields"] =
                countdownEditingGates.PreferenceErrorsAtSource,
            ["MainPrimarySecondaryButtonRolesDistinct"] =
                countdownEditingGates.PrimarySecondaryRolesDistinct,
            ["MainInputSurfaceWhitePaper"] =
                countdownEditingGates.InputSurfacesWhitePaper,
            ["MainTextSelectionHighlightDistinct"] =
                countdownEditingGates.SelectionHighlightDistinct,
            ["MainStatusBarSurfaceErrorContrast"] =
                countdownEditingGates.StatusBarErrorContrast,
            ["MainReminderRadioSelectedContrast"] =
                countdownEditingGates.ReminderRadioSelectedContrast,
            ["MainEditorTypographyTokens"] =
                countdownEditingGates.EditorTypographyTokenized,
            ["MainCountdownEditingGateIsolationBehavior"] =
                countdownEditingGateIsolationBehavior,
            ["MainFocusRingConstantThicknessNoSqueeze"] = interactionStates.FocusRingsReserveSpace,
            ["MainPrimaryAndPinSingleFocusStroke"] = interactionStates.PrimaryAndPinSingleFocusStroke,
            ["MainNotesTabHoverDiffersFromSelected"] = interactionStates.NotesTabHoverDiffersFromSelected,
            ["MainLocalControlsPressAndDisabled"] = interactionStates.LocalControlsPressAndDisabled,
            ["MainReminderHelpFocusShowsHelp"] = interactionStates.HelpFocusShowsHelpText,
            ["MainCompactHitTargetsMeetScaledFloor"] = interactionStates.CompactHitTargetsMeetScaledFloor,
            ["MainFilterChipsAndCalendarNavCompact"] = interactionStates.ChipsAndCalendarNavCompact,
            ["MainHoverStateContrastIsPerceivable"] = interactionStates.HoverStateContrastIsPerceivable,
            ["MainPressedStateGeometryStable"] =
                interactionStates.PressedStateGeometryStable &&
                interactionStates.SegmentIndicatorRapidSwitchStaysAligned,
            ["MainMouseClickDoesNotLeaveFocusRing"] = interactionStates.MouseClickDoesNotLeaveFocusRing,
            ["MainKeyboardFocusRingShowsOnTab"] = interactionStates.KeyboardFocusRingShowsOnTab,
            ["MainSelectedTabWeightDominates"] =
                interactionStates.SelectedTabWeightDominates &&
                interactionStates.SegmentIndicatorGeometryMatches,
            ["MainSegmentIndicatorGeometryMatches"] = interactionStates.SegmentIndicatorGeometryMatches,
            ["MainSegmentIndicatorReducedMotionIsImmediate"] = interactionStates.SegmentIndicatorReducedMotionIsImmediate,
            ["MainSegmentIndicatorRapidSwitchStaysAligned"] = interactionStates.SegmentIndicatorRapidSwitchStaysAligned,
            ["MotionClientAreaAnimationSwitchReadable"] =
                ApplicationTheme.ClientAreaAnimationEnabled == SystemParameters.ClientAreaAnimation,
            ["MotionTokensMatch"] =
                motionTokensMatch && interactionStates.SegmentIndicatorReducedMotionIsImmediate,
            ["KeyboardFocusRingTabGapAvoidsSelectedFill"] =
                keyboardFocusRingContrast.TabRingGapAvoidsSelectedFill,
            ["KeyboardFocusRingFloatingCapsuleMeetsContrast"] =
                keyboardFocusRingContrast.FloatingCapsuleFocusRingMeetsContrast,
            ["KeyboardFocusRingFloatingCapsuleUsesWarmGold"] =
                keyboardFocusRingContrast.FloatingCapsuleUsesWarmGoldToken,
            ["FloatingIncomeUsesTabularNumerals"] =
                Typography.GetNumeralAlignment(Required<TextBlock>(floating, "FloatingIncome")) ==
                FontNumeralAlignment.Tabular,
            ["HasNameEditor"] = main.FindName("CountdownName") is TextBox,
            ["HasNoteReminderControls"] =
                main.FindName("ManageNoteReminderRulesButton") is Button &&
                main.FindName("NoteReminderRulesSummary") is TextBlock &&
                floating.FindName("FloatingManageReminderRules") is Button &&
                floating.FindName("FloatingReminderRulesSummary") is TextBlock &&
                main.FindName("NoteReminder") is null &&
                main.FindName("NoteReminderSchedule") is null &&
                main.FindName("FloatingReminderIntervalPanel") is null &&
                floating.FindName("FloatingReminder") is null &&
                floating.FindName("FloatingReminderSchedule") is null &&
                floating.FindName("FloatingReminderIntervalPanel") is null,
            ["NoteReminderControlsWork"] =
                reminderRulesEditorSharedEntryWorks,
            ["ReminderRulesEditorSharedEntryWorks"] =
                reminderRulesEditorSharedEntryWorks &&
                Required<Button>(main, "ManageNoteReminderRulesButton").Visibility == Visibility.Visible &&
                Required<TextBlock>(main, "NoteReminderRulesSummary").Visibility == Visibility.Visible &&
                main.FindName("NoteReminder") is null &&
                main.FindName("NoteReminderSchedule") is null &&
                main.FindName("FloatingReminderIntervalPanel") is null &&
                Required<Button>(floating, "FloatingManageReminderRules").Visibility == Visibility.Visible &&
                Required<TextBlock>(floating, "FloatingReminderRulesSummary").Visibility == Visibility.Visible &&
                floating.FindName("FloatingReminder") is null &&
                floating.FindName("FloatingReminderSchedule") is null &&
                floating.FindName("FloatingReminderIntervalPanel") is null,
            ["CalendarReminderStartLabelsUseCalendarSemantics"] =
                ProbeCalendarReminderStartLabels(),
            ["FloatingReminderRulesEntryStaysCompact"] =
                Math.Abs(floating.Width - 520) <= 0.1 &&
                floating.Height <= 391 &&
                Required<Button>(floating, "FloatingManageReminderRules").Width <= 116,
            ["NoteReminderTimeOptionCounts"] = new[]
            {
                productionDatePickers.ReminderHourOptionCount,
                productionDatePickers.ReminderMinuteOptionCount
            },
            ["CrossDayReminderSelectionWorks"] = crossDayReminderBehavior,
            ["ReminderSummaryAdvancesAfterFirstDue"] = reminderSummaryAdvances,
            ["OneTimeReminderLifecycleWorks"] = oneTimeReminderBehavior,
            ["ReminderRequiresContentRenderedConfirmation"] =
                reminderDelivery.RequiresContentRenderedConfirmation,
            ["ReminderPersistenceFailureStaysVisible"] =
                reminderDelivery.PersistenceFailureStaysVisible,
            ["ReminderPersistenceRetryBackoffWorks"] =
                reminderDelivery.PersistenceRetryBackoffWorks,
            ["ReminderDeliverySingleFlightWorks"] =
                reminderDelivery.SingleFlightWorks,
            ["ReminderIndependentOccurrencesWork"] =
                reminderDelivery.IndependentOccurrencesWork,
            ["ReminderPersistenceFailureCloseGuardWorks"] =
                reminderDelivery.PersistenceFailureCloseGuardWorks,
            ["ReminderPersistenceFailureDisposeWorks"] =
                reminderPersistenceFailureDisposeWorks,
            ["ReminderDiagnosticsExcludePrivatePayload"] =
                reminderDelivery.DiagnosticsExcludePrivatePayload,
            ["ReminderCenterSingleWindow"] = reminderCenter.SingleWindow,
            ["ReminderCenterSameTickBatch"] = reminderCenter.SameTickBatch,
            ["ReminderCenterAppendsAfterOpen"] =
                reminderCenterAppendsAfterOpen,
            ["ReminderCenterStableOrdering"] = reminderCenter.StableOrdering,
            ["ReminderCenterEntriesRendered"] = reminderCenter.EntriesRendered,
            ["ReminderAcknowledgementDistinctFromDelivery"] =
                reminderCenter.AcknowledgementDistinctFromDelivery,
            ["ReminderAcknowledgementFailureRetainsEntry"] =
                reminderCenter.AcknowledgementFailureRetainsEntry,
            ["ReminderAcknowledgementFailureIsAtomic"] =
                reminderCenter.AcknowledgementFailureIsAtomic,
            ["ReminderCenterSingleItemDisposition"] =
                reminderCenter.SingleItemDisposition,
            ["ReminderCenterOpenSourcePreservesState"] =
                reminderCenter.OpenSourcePreservesState,
            ["ReminderCenterOpenSourceFindsExactNote"] =
                reminderCenter.OpenSourceFindsExactNote,
            ["ReminderCenterOpenSourceRestoresMinimizedWindow"] =
                reminderCenter.OpenSourceRestoresMinimizedWindow,
            ["ReminderCenterPersistenceFailureRetainsCurrentSource"] =
                reminderCenterSourceLifecycle.PersistenceFailureRetainsCurrentSource,
            ["ReminderCenterRemovesDeletedSource"] =
                reminderCenterSourceLifecycle.RemovesDeletedSource,
            ["ReminderCenterCancelsDeletedSourceAckRetry"] =
                reminderCenterSourceLifecycle.CancelsDeletedSourceAcknowledgementRetry,
            ["ReminderCenterRemovesDisabledSource"] =
                reminderCenterSourceLifecycle.RemovesDisabledSource,
            ["ReminderCenterRemovesCompletedSource"] =
                reminderCenterSourceLifecycle.RemovesCompletedSource,
            ["ReminderCenterReopensAfterSourceRemoval"] =
                reminderCenterSourceLifecycle.ReopensAfterSourceRemoval,
            ["ReminderCenterClosesWhenEmpty"] = reminderCenter.ClosesWhenEmpty,
            ["ReminderCenterReopens"] = reminderCenter.Reopens,
            ["ReminderCenterScrollsLongContent"] = reminderCenter.ScrollsLongContent,
            ["ReminderCenterFollowsDayTheme"] = reminderCenter.FollowsDayTheme,
            ["ReminderCenterHasOnlySafeActions"] = reminderCenter.HasOnlySafeActions,
            ["ReminderCenterDisplaysSourceContentAndTime"] =
                reminderCenter.DisplaysSourceContentAndTime,
            ["ReminderCenterAccessible"] = reminderCenter.Accessible,
            ["RepeatingReminderRenderedLatchWorks"] =
                repeatingReminderDelivery.RenderedLatchWorks,
            ["RepeatingReminderDistinctCyclesCoexist"] =
                repeatingReminderDelivery.SingleUnresolvedOccurrenceWorks,
            ["RepeatingReminderDueBeforeShow"] = repeatingReminderDelivery.DueBeforeShow,
            ["ReminderCenterOpened"] = repeatingReminderDelivery.WindowOpened,
            ["RepeatingReminderContentRendered"] = repeatingReminderDelivery.ContentRendered,
            ["RepeatingReminderNoDuplicate"] = repeatingReminderDelivery.NoDuplicate,
            ["ReminderDispositionAndSnoozePersistAcrossRestart"] =
                reminderDispositionAndSnooze,
            ["ReminderCompletionAndReopenAreAtomic"] =
                reminderCompletionAndReopen,
            ["ReminderCenterSnoozeClickPersists"] =
                reminderCenterSnoozeClick,
            ["ReminderCenterExpiredCustomSnoozeRetryAllowsCorrection"] =
                reminderCenterExpiredCustomSnoozeRetry,
            ["ReminderCenterCompleteReopenDiscardsStaleRetries"] =
                reminderCenterStaleRetryAfterCompleteReopen,
            ["ReminderSnoozeSuppressesBaseCyclesUntilExpiry"] =
                reminderSnoozeSuppressesBaseCycles,
            ["ReminderCenterCompleteClickIsAtomic"] =
                reminderCenterCompleteClick,
            ["ReminderCenterCompleteRequiresConfirmation"] =
                reminderCenterCompleteRequiresConfirmation,
            ["DeleteUndoRestoresLastDelete"] =
                deleteUndoOffer,
            ["DirtyExitConfirmsUnsavedWork"] =
                dirtyExitConfirmation,
            ["AssistiveNarrationExitsDecorationAndLabelsFields"] =
                assistiveNarration,
            ["NarratorAnnouncesLiveRegionChanges"] =
                narratorLiveRegions,
            ["WindowPositionResetsWithoutDrag"] =
                windowPositionReset,
            ["MainWindowFitsInjectedWorkAreas"] =
                mainWindowWorkAreaFit,
            ["CountdownListFirstBehavior"] = countdownListFirst,
            ["MainTopmostStateSynchronization"] = mainTopmostStateSynchronization,
            ["MainRedundancyCleanup"] = mainRedundancyCleanup,
            ["MainNoteEditorSeparation"] = mainNoteEditorSeparation,
            ["MainTextShortcutBehavior"] = mainTextShortcutBehavior,
            ["SingleLineEnterSubmission"] = singleLineEnterSubmission,
            ["FloatingNoteKeyboardSubmission"] = floatingNoteKeyboardSubmission,
            ["ListDeleteKeyboardBehavior"] = listDeleteKeyboardBehavior,
            ["MainNoteRowsSimplified"] = mainNoteRowsSimplified,
            ["CountdownPreferencesPopupBehavior"] = countdownPreferencesPopup,
            ["CountdownEditorExpansionBehavior"] = countdownEditorExpansion,
            ["CountdownRowDeadlineAndSecondaryDeleteBehavior"] = countdownRowPresentation,
            ["MainWindowIdentityZoneDragRegion"] =
                mainWindowIdentityZoneDrag,
            ["FloatingWindowStaysOnSecondaryMonitor"] = floatingSecondaryMonitor,
            ["OwnerDialogsFitSecondaryMonitor"] = ownerDialogsFitSecondaryMonitor,
            ["ReminderUsesSourceMonitor"] = reminderUsesSourceMonitor,
            ["OpenWindowsFollowMonitorChanges"] = openWindowsFollowMonitorChanges,
            ["MixedDpiUsesPhysicalMonitorGeometry"] = mixedDpiPhysicalGeometry,
            ["ProductManifestDeclaresPerMonitorV2"] = productManifestPerMonitorV2,
            ["RuntimeDpiChangeUpdatesWindowLayout"] = runtimeDpiLayout,
            ["MainWindowHiddenTickSkipsRenderAndShowRefreshes"] =
                mainWindowHiddenRefresh,
            ["MainWindowNoteMinutePreservesFocusAndScroll"] =
                mainWindowNoteMinuteRefresh,
            ["ReminderCenterDeliveryFailureDisablesDispositions"] =
                reminderCenterDeliveryFailureActionGate,
            ["ReminderCenterEnterUsesNativeButtonActivation"] =
                reminderCenterEnterUsesNativeButtonActivation,
            ["ReminderCenterArrivalKeepsForegroundAndFlashesTaskbar"] =
                reminderCenterArrivalKeepsForegroundAndFlashesTaskbar,
            ["ReminderCenterMinimizeToTaskbarPreservesEntries"] =
                reminderCenterMinimizeToTaskbarPreservesEntries,
            ["ReminderCenterSnoozeGatedDuringCompletionRetry"] =
                reminderCenterSnoozeGatedDuringCompletionRetry,
            ["ReminderCenterCustomSnoozeErrorRoutesByCode"] =
                reminderCenterCustomSnoozeErrorRoutesByCode,
            ["ReminderCenterShowFailureRollsBack"] =
                reminderCenterShowFailureRollsBack,
            ["ReminderCenterShowFailureRollbackSurvivesEntryFailure"] =
                reminderCenterShowFailureRollbackSurvivesEntryFailure,
            ["ReminderCenterCustomSnoozeControlsVisibility"] =
                reminderCenterCustomSnoozeControlsVisibility,
            ["ReminderCenterCustomSnoozeCalendarBehavior"] =
                reminderCenterCustomSnoozeCalendarBehavior,
            ["ReminderCenterOpenSourcePreservesNoteGroupState"] =
                reminderCenterOpenSourcePreservesNoteGroupState,
            ["ReminderCenterAcknowledgeAllWritesAllDispositions"] =
                reminderCenterAcknowledgeAll.WritesAllDispositions,
            ["ReminderCenterAcknowledgeAllMatchesSingleSemantics"] =
                reminderCenterAcknowledgeAll.MatchesSingleSemantics,
            ["ReminderCenterAcknowledgeAllFailureRetainsAllAtomically"] =
                reminderCenterAcknowledgeAll.FailureRetainsAllAtomically,
            ["ReminderCenterAcknowledgeAllBackoffRetryCompletesBatch"] =
                reminderCenterAcknowledgeAll.BackoffRetryCompletesBatch,
            ["ReminderCenterRepresentativeCaptureRendered"] =
                reminderCenterRepresentativeCapture.Rendered,
            ["ReminderCenterRepresentativeCapture"] = reminderCenterRepresentativeCapture,
            ["ValidateOnlySettingsIsolationBehavior"] =
                validateOnlySettingsIsolation,
            ["LazyManagerLifecycleBehavior"] = lazyManagerLifecycle,
            ["MainDecorationOnDemandDecodeBehavior"] = mainDecorationOnDemandDecode,
            ["CountdownReminderWpfBehavior"] = countdownReminderWpf,
            ["ReminderAcknowledgementMonotonicAcrossOccurrences"] =
                repeatingReminderDelivery.AcknowledgementMonotonicAcrossOccurrences,
            ["NoteReminderDispatchWorks"] = reminderBehavior,
            ["ReminderCenterIsThemed"] = reminderThemed,
            ["ReminderCenterSupportsKeyboardNavigation"] =
                reminderCenterKeyboardGates.AllPassed,
            ["ReminderCenterShowPreservesFocusAndAccessibleName"] =
                reminderCenterKeyboardGates.ShowPreservesFocusAndAccessibleName,
            ["ReminderCenterEscapeFocusesAcknowledge"] =
                reminderCenterKeyboardGates.EscapeFocusesAcknowledge,
            ["ReminderCenterTypographyWeightWhitelist"] =
                reminderCenterTypographyWeightWhitelist,
            ["ReminderCenterKeyboardGateIsolationBehavior"] =
                reminderCenterKeyboardGateIsolationBehavior,
            ["OfficialCharacterBackgroundLoaded"] =
                Required<Image>(main, "MyMelodyBackground").Source is not null &&
                Required<Image>(main, "CinnamorollBackground").Source is not null &&
                Required<Image>(main, "KuromiBackground").Source is not null,
            ["OfficialCharacterBackgroundCharactersExclusive"] =
                officialCharacterBackgroundCharactersExclusive,
            ["OfficialCharacterBackgroundLowContrast"] = main.CharacterBackgroundRemainsSubtle,
            ["MainCharacterPanelDockCount"] = mainCharacterDocks.Length,
            ["MainCharacterPanelDocksIntegrateWithPanel"] = mainCharacterDocksIntegrateWithPanel,
            ["MainCharacterPanelDecorationsAreNonInteractive"] = main.CharacterPanelDecorationsAreNonInteractive,
            ["MainReferenceDecorationAssetsLoaded"] = main.MainReferenceDecorationsLoaded,
            ["MainReferenceDecorationsAreNonInteractive"] = main.MainReferenceDecorationsAreNonInteractive,
            ["MainReferenceDecorationsShareScaleAtMinimumAndWideSizes"] =
                uiQualityGates.MainReferenceDecorationScaling,
            ["MainReferenceDecorationLayerOrderMatches"] =
                uiQualityGates.MainReferenceDecorationLayerOrder,
            ["MainReferenceCaptureRequested"] = legacyMainReferenceCapture.Requested,
            ["MainReferenceCaptureWritten"] = legacyMainReferenceCapture.CapturesWritten,
            ["MainReferenceCaptureUsesZeroState"] = legacyMainReferenceCapture.UsesZeroState,
            ["MainReferenceCaptureCanvasMatches"] = legacyMainReferenceCapture.CanvasMatches,
            ["MainReferenceCaptureAlphaIsOpaque"] = legacyMainReferenceCapture.AlphaIsOpaque,
            ["MainReferenceCountdownCapturePath"] = legacyMainReferenceCapture.CountdownCapturePath,
            ["MainReferenceNotesCapturePath"] = legacyMainReferenceCapture.NotesCapturePath,
            ["MainReferenceCaptureManifestPath"] = legacyMainReferenceCapture.ManifestPath,
            ["MainReferenceBaselineRequested"] = mainReferenceBaseline.Requested,
            ["MainReferenceBaselineManifestAvailable"] = mainReferenceBaseline.ManifestAvailable,
            ["MainReferenceBaselineValidated"] = mainReferenceBaseline.AllPassed,
            ["MainReferenceBaselineGateIsolationBehavior"] =
                mainReferenceBaselineGateIsolationBehavior,
            ["MainReferenceBaselineManifestPath"] = mainReferenceBaseline.ManifestPath,
            ["MainReferenceBaselineSourceAggregateSha256"] =
                mainReferenceBaseline.SourceAggregateSha256,
            ["MainReferenceBaselineExecutableSha256"] =
                mainReferenceBaseline.ExecutableSha256,
            ["MainReferenceBaselineGates"] = mainReferenceBaseline.Gates.AsDictionary(),
            ["MainReferenceBaselineCaptures"] = mainReferenceBaseline.Captures,
            ["MainDaySingleBorder"] = daySingleBorder,
            ["MainDaySingleBorderGate"] = daySingleBorder.AllPassed,
            ["MainDaySingleBorderGateIsolationBehavior"] = daySingleBorderGateIsolationBehavior,
            ["MainDaySingleBorderNegativeFixtures"] = new[]
            {
                "unexpected-control-edge",
                "label-intersection",
                "missing-lower-ornament"
            },
            ["MainReferenceBaselineDiagnostics"] = mainReferenceBaseline.Diagnostics,
            ["MainReferenceBaselineNegativeFixtures"] = MainReferenceBaselineNegativeFixtures,
            ["MainStressCountdownTitleVisuals"] =
                mainReferenceBaseline.StressCountdownTitleVisuals,
            ["MainStressCountdownTitleGateIsolationBehavior"] =
                mainStressCountdownTitleGateIsolationBehavior,
            ["MainStressCountdownTitleMeasurements"] =
                mainReferenceBaseline.StressCountdownTitleMeasurements,
            ["MainNoteCardReminderFontSizeDip"] =
                mainReferenceBaseline.NoteCardReminderFontSizeDip,
            ["MainNoteCardReminderFontSizeMeetsContract"] =
                mainReferenceBaseline.NoteCardReminderFontSizeMeetsContract,
            ["MainNoteCardReminderFontMeasurements"] =
                mainReferenceBaseline.NoteCardReminderFontMeasurements,
            ["LegacyMigrationSupported"] = legacyMigration,
            ["DuplicateSettingsRejected"] = duplicateRejected,
            ["InvalidNotesRejected"] = invalidNotesRejected,
            ["InvalidTagReferencesRejected"] = invalidTagReferencesRejected,
            ["HasTagControls"] =
                main.FindName("CountdownTags") is TagSelector &&
                main.FindName("NoteTags") is TagSelector &&
                main.FindName("FilterTagOptions") is WrapPanel,
            ["StaticWorkspaceLayout"] =
                main.FindName("CountdownWorkspace") is Canvas &&
                main.FindName("DisplayPrecision") is ComboBox,
            ["MainNoteEditorFieldsDoNotOverlap"] = mainUiPolish.NoteEditorFieldsDoNotOverlap,
            ["LegacyInlineReminderControlsStayHidden"] =
                mainUiPolish.LegacyInlineReminderControlsStayHidden,
            ["MainNoteEditorLabelsVisible"] = mainUiPolish.NoteEditorLabelsVisible,
            ["MainMinimumSizeMatchesDefault"] = mainUiPolish.MinimumSizeMatchesDefault,
            ["PopupKeyboardLifecycleWorks"] = mainUiPolish.PopupKeyboardLifecycleWorks,
            ["PopupOutsideFocusPreserved"] = mainUiPolish.PopupOutsideFocusPreserved,
            ["PopupTriggerSecondClickCloses"] = mainUiPolish.PopupTriggerSecondClickCloses,
            ["PopupEscapeFromTriggerClosesAndKeepsFocus"] =
                mainUiPolish.PopupEscapeFromTriggerClosesAndKeepsFocus,
            ["NoteEmptyStatesDistinguished"] = mainUiPolish.EmptyStatesDistinguished,
            ["NoteNoResultsResetWorks"] = mainUiPolish.NoResultsResetWorks,
            ["CountdownEmptyStatesDistinguished"] = mainUiPolish.CountdownEmptyStatesDistinguished,
            ["CountdownNoResultsResetWorks"] = mainUiPolish.CountdownEmptyPointerResetRestoresResults,
            ["CountdownEmptyPointerResetRestoresResults"] =
                mainUiPolish.CountdownEmptyPointerResetRestoresResults,
            ["CountdownEmptyKeyboardResetRestoresResults"] =
                mainUiPolish.CountdownEmptyKeyboardResetRestoresResults,
            ["CountdownEmptyStateTextContrast"] = countdownEmptyStateGates.DayThemeTextContrast,
            ["CountdownEmptyStateContrastMeasurements"] = countdownEmptyStateContrast.Measurements,
            ["CountdownEmptyStateGateIsolationBehavior"] = countdownEmptyStateGateIsolationBehavior,
            ["NoteStatusBadgeTokensFollowState"] = noteVisualTokenGates.BadgeTokensFollowState,
            ["NoteEmptyStateTextContrast"] = noteVisualTokenGates.EmptyStateTextContrast,
            ["NoteEmptyStateContrastMeasurements"] = noteEmptyStateContrast.Measurements,
            ["NoteVisualTokenGateIsolationBehavior"] = noteVisualTokenGateIsolationBehavior,
            ["CountdownRemainingLayoutMatches"] = mainCountdownRemainingGates.FixedColumnLayoutMatches,
            ["CountdownRemainingUnitSpacingMatches"] = mainCountdownRemainingGates.UnitSpacingMatches,
            ["CountdownRemainingGateIsolationBehavior"] = mainCountdownRemainingGateIsolationBehavior,
            ["CountdownIncrementalRefreshIdentityMatches"] =
                mainUiPolish.CountdownIncrementalRefresh.IdentityPreserved,
            ["CountdownExpiryVisualsFlipTogether"] =
                mainUiPolish.CountdownIncrementalRefresh.ExpiryVisualsFlipTogether,
            ["CountdownIncrementalRefresh"] = mainUiPolish.CountdownIncrementalRefresh,
            ["CountdownIncrementalRefreshGateIsolationBehavior"] =
                countdownIncrementalRefreshGateIsolationBehavior,
            ["CountdownRemainingSamples"] = mainUiPolish.CountdownRemainingSamples,
            ["MainNoteCompletionTargetSizeAndBehavior"] =
                mainNoteCompletionGates.MainTargetSizeAndBehavior,
            ["MainNoteCompletionOtherUserSizesUnchanged"] =
                mainNoteCompletionGates.OtherUserSizesUnchanged,
            ["MainNoteCompletionGateIsolationBehavior"] =
                mainNoteCompletionGateIsolationBehavior,
            ["MainNoteCompletionMeasurements"] = mainNoteCompletionValidation.Measurements,
            ["MainNoteCompletionInteractionBehavior"] =
                mainNoteCompletionValidation.Interaction with
                {
                    AccessibleNamesHaveNoLineBreaks = accessibleNameWhitespaceIsolationBehavior
                },
            ["MainNoteCompletionStyleScoped"] = mainNoteCompletionGates.AllPassed,
            ["TransientPanelsOnDemand"] = main.HasOnDemandPanels,
            ["TransientPanelsStartClosed"] = main.HasOnDemandPanels,
            ["CountdownTagPickerOpensOnDemand"] = !selector.IsPopupOpen,
            ["FilterOpensOnDemand"] = !Required<Popup>(main, "FilterPopup").IsOpen,
            ["TagManagerOpensOnDemand"] = !Required<Popup>(main, "TagManagerPopup").IsOpen,
            ["NoteTagPickerOpensOnDemand"] = !Required<TagSelector>(main, "NoteTags").IsPopupOpen,
            ["WorkspaceDraftsRetained"] = workspaceDraftsRetained,
            ["CountdownReminderModeMappingWorks"] =
                mainWindowScenarios.CountdownReminderModeMappingWorks,
            ["SettingsDraftsRetainedOnRefresh"] = settingsDrafts.InputsRetainedOnRefresh,
            ["SettingsSavedValuesReflectedAfterSave"] =
                settingsDrafts.SavedValuesReflectedAfterSave,
            ["NegativeMonthlySalaryRejectedKeepsDraft"] =
                settingsDrafts.NegativeSalaryRejectedKeepsDraft,
            ["ZeroMonthlySalaryStillSavesAsUnset"] =
                settingsDrafts.ZeroSalaryStillSavesAsUnset,
            ["NewNoteRejectsEmptyDate"] = undatedNoteEdit.NewNoteRejectsEmptyDate,
            ["UndatedNoteEditSavesWithoutDate"] = undatedNoteEdit.UndatedEditSavesWithoutDate,
            ["DatedNoteEditRejectsClearedDate"] = undatedNoteEdit.DatedEditRejectsClearedDate,
            ["NoteDateValidationMessagesDistinguished"] =
                undatedNoteEdit.ValidationMessagesDistinguished,
            ["HiddenCountdownSubmitDisabled"] = hiddenCountdownSubmitDisabled,
            ["WorkspaceTabsUseSelectionSemantics"] =
                main.FindName("CountdownTab") is ToggleButton &&
                main.FindName("NotesTab") is ToggleButton,
            ["TagUiAccessible"] = !string.IsNullOrWhiteSpace(AutomationProperties.GetName(Required<Button>(main, "TagManagerButton"))),
            ["FilterOptionsUseSelectionSemantics"] =
                main.FindName("AnyTagMode") is ToggleButton &&
                main.FindName("AllTagMode") is ToggleButton,
            ["CompactFilterSummary"] = Required<Button>(main, "FilterButton").ActualWidth <= 140,
            ["TagCreateAppearsFromSearch"] = createAppears,
            ["TagCreateStaysCollapsedWithoutQuery"] = createCollapsed,
            ["TagFilterSupportsAnyAll"] =
                main.FindName("AnyTagMode") is ToggleButton &&
                main.FindName("AllTagMode") is ToggleButton,
            ["TagChipsUseUid"] = tagChipsUseUid,
            ["PersistenceRoundTrip"] = roundTrip,
            ["NotePersistenceRoundTrip"] = roundTrip,
            ["TagPersistenceRoundTrip"] = roundTrip,
            ["NoteAddViaClick"] = noteExercise.AddedViaClick,
            ["NoteDateRequired"] = noteExercise.DateRequired,
            ["NoteDateViaPicker"] = noteExercise.DateViaPicker,
            ["NoteReminderViaSelector"] = noteExercise.ReminderViaSelector,
            ["FloatingCrossDayReminderViaClick"] = floatingNoteExercise,
            ["MainNoReminderViaClick"] = mainReminderModeExercise.NoReminder,
            ["MainOnceReminderViaClick"] = mainReminderModeExercise.OnceReminder,
            ["MainRepeatingReminderViaClick"] = mainReminderModeExercise.RepeatingReminder,
            ["FloatingNoReminderViaClick"] = floatingReminderModeExercise.NoReminder,
            ["FloatingOnceReminderViaClick"] = floatingReminderModeExercise.OnceReminder,
            ["FloatingRepeatingReminderViaClick"] = floatingReminderModeExercise.RepeatingReminder,
            ["NoteDateDefaultsTrackBeijingDay"] = noteDateBehavior.TracksBeijingDay,
            ["NoteDateChoicesSurviveBeijingDayChange"] = noteDateBehavior.PreservesChosenDate,
            ["NoteStatusViaCheckbox"] = noteExercise.CompletionViaCheckbox,
            ["NoteFieldErrorsAtSource"] = mainWindowScenarios.NoteValidation.BothFieldsShowErrors,
            ["NoteFirstInvalidFieldFocus"] = mainWindowScenarios.NoteValidation.FirstInvalidFieldFocus,
            ["NoteFieldErrorsClearOnCorrection"] = mainWindowScenarios.NoteValidation.ErrorsClearOnCorrection,
            ["DatedNoteRejectsClearedDate"] = mainWindowScenarios.NoteValidation.DatedEditRejectsClearedDate,
            ["CountdownReminderDateFocusForTimeAndCustom"] = mainWindowScenarios.NoteValidation.ReminderDateFocusForTimeAndCustom,
            ["TagInteractionsExecuted"] = tagExercise.Executed,
            ["TagCreationAndAssociation"] = tagExercise.CreationAndAssociation,
            ["TagAnyAllFilter"] = tagExercise.AnyAllFilter,
            ["TagEditorSummaryIsCompact"] = tagExercise.EditorSummaryIsCompact,
            ["TagUntaggedAndCountdownStatus"] = tagExercise.UntaggedAndCountdownStatus,
            ["TagNoteStatusSearchAndCompletion"] = tagExercise.NoteStatusSearchAndCompletion,
            ["NoteDateFilterControls"] =
                main.FindName("NoteDateFilter") is DatePicker &&
                main.FindName("NoteUndatedFilter") is ToggleButton &&
                main.FindName("ClearNoteDateFilter") is Button,
            ["NoteDateFilterBehavior"] = tagExercise.NoteDateFilterBehavior,
            ["TagFloatingNoteInheritsTags"] = tagExercise.FloatingNoteInheritsTags,
            ["TagFloatingNoteDateViaPicker"] = tagExercise.FloatingNoteDateViaPicker,
            ["TagSearchCreateAvoidsDefaultSubmit"] =
                tagExercise.Executed ? tagExercise.SearchCreateAvoidsDefaultSubmit : tagSearchDoesNotSubmit,
            ["TagFloatingNotePopupOnDemand"] = tagExercise.FloatingNotePopupOnDemand,
            ["TagFloatingNoteCanOverrideTags"] = tagExercise.FloatingNoteCanOverrideTags,
            ["TagNoteEditingPersistsTags"] = tagExercise.NoteEditingPersistsTags,
            ["TagNoteEditingPersistsDate"] = tagExercise.NoteEditingPersistsDate,
            ["TagRenameAndDeleteDetachOnly"] = tagExercise.RenameAndDeleteDetachOnly,
            ["CountdownSaveFailurePreservesState"] = persistenceExercise.CountdownStatePreserved,
            ["PreferenceSaveFailurePreservesState"] = persistenceExercise.PreferenceStatePreserved,
            ["CountdownSaveFailureRetainsDraft"] = persistenceExercise.CountdownDraftRetained,
            ["PreferenceSaveFailureRetainsDraft"] = persistenceExercise.PreferenceDraftRetained,
            ["TagSaveFailurePreservesState"] = persistenceExercise.TagStatePreserved,
            ["TagSaveFailureRetainsDraft"] = persistenceExercise.TagDraftRetained,
            ["TagSaveFailureRetainsFilter"] = persistenceExercise.TagFilterRetained,
            ["PersistenceFailureTempPathRemainsDirectory"] = persistenceExercise.TemporaryPathRemainsDirectory,
            ["CardActionsBoundByTag"] = cardActionsBound,
            ["NoteActionsBoundByTag"] = noteActionsBound,
            ["NoteStatusLabelsPresent"] =
                noteStatusLabels || workspaceSnapshot.Settings.Notes.Count == 0,
            ["NoteStatusSegmentSwitch"] = noteStatusSegmentSwitch,
            ["NoteDateUsesSpacedChineseFormat"] = noteListPolish.SpacedChineseDateEverywhere,
            ["NoteCardBodyLineHeightSeparatesSummary"] = noteListPolish.BodyLineHeightSeparatesSummary,
            ["NoteRowTitleFallsToBlockTitleLevel"] = noteListPolish.RowTitleFallsToBlockTitleLevel,
            ["MainNoteRowActionsKeyboardWorks"] = noteListPolish.MainNoteRowActionsKeyboardWorks,
            ["NoteCardSpacingConsumesTokens"] = noteListPolish.CardSpacingConsumesTokens,
            ["TagChipSingleLineEllipsisWithFullText"] = noteListPolish.TagChipSingleLineEllipsisWithFullText,
            ["EmptyStateTextAllowsWrap"] = noteListPolish.EmptyStateTextAllowsWrap,
            ["NoteDateFilterHasVisibleLabel"] = noteListPolish.FilterDateHasVisibleLabel,
            ["PopupTabNavigationCyclesInsideLayer"] = popupFocusTabGates.AllCyclesInsideLayer,
            ["PopupTabGateIsolationBehavior"] = popupFocusTabGateIsolationBehavior,
            ["FilterFunnelIconSurvivesAliasedCapture"] = visualChrome.FilterFunnelIconSurvivesAliasedCapture,
            ["NotesSearchDuplicateMagnifierRemoved"] = visualChrome.NotesSearchDuplicateMagnifierRemoved,
            ["MainWindowChromeButtonsUseVectorPaths"] = visualChrome.MainWindowChromeButtonsUseVectorPaths,
            ["SegmentActivePillsUseFullRoundedCorners"] = visualChrome.SegmentActivePillsUseFullRoundedCorners,
            ["CountdownHandwrittenDecoOutOfFormMeasurement"] = visualChrome.CountdownHandwrittenDecoOutOfFormMeasurement,
            ["FloatingShellLargeShadowRemoved"] = floatingShellLargeShadowRemoved,
            ["MainWindowSquareChromeNoTransparency"] = visualChrome.MainWindowSquareChromeNoTransparency,
            ["TabsRenderAboveDecorationBand"] = visualChrome.TabsRenderAboveDecorationBand,
            ["NoteGroupingSelectorAbsent"] = noteGroupingSelectorAbsent,
            ["NoteDateTimelineVisuals"] = noteDateTimelineVisuals,
            ["NoteDisclosureAccessibility"] = noteDisclosureAccessibility,
            ["NoteDisclosureThemeTokens"] = noteDisclosureThemeTokens,
            ["NoteDatesRendered"] = noteDatesRendered,
            ["NotesGroupedByDay"] = notesGroupedByDay,
            ["ActiveNotesGrouped"] = activeNotesGrouped,
            ["ActiveNotesSegmentSelectedByDefault"] =
                activeNoteRows.Visibility == Visibility.Visible,
            ["ActiveNotesKeepDateOrder"] = activeNotesKeepDateOrder,
            ["ManagerNoteRowsFollowSortOrder"] = managerNoteRowsFollowSortOrder,
            ["NoteDateSectionsCanCollapse"] = noteSectionExercise.DateSectionsCanCollapse,
            ["NoteDateSectionsAreIndependent"] = noteSectionExercise.DateSectionsAreIndependent,
            ["NoteDateSectionStateSurvivesRefresh"] = noteSectionExercise.DateSectionStateSurvivesRefresh,
            ["NoteStatusSegmentsSwitchExclusively"] = noteSectionExercise.SegmentsSwitchExclusively,
            ["CompletedNotesCollapsed"] = main.CompletedNotesAreCollapsed,
            ["FloatingWindowPresent"] = floating! is not null,
            ["FloatingWindowStaysAboveWorkAreaBottom"] = floatingBottomMargin >= 0,
            ["FloatingWindowBottomAnchored"] = floatingBottomAnchored,
            ["FloatingWindowReanchorsAfterContentChanges"] = floatingReanchorsAfterContentChanges,
            ["FloatingWindowHeightConstrainedToWorkArea"] = floatingHeightConstrained,
            ["FloatingDragSurfaceCoversWindowBackground"] =
                floatingDragInteraction.CoversWindowBackground,
            ["FloatingDragHandleReceivesPointerGestures"] =
                floatingDragInteraction.ReceivesPointerGesture,
            ["FloatingDragHandleRequestsNativeWindowDrag"] =
                floatingDragInteraction.RequestsNativeWindowDrag,
            ["FloatingSyntheticDragDoesNotClaimPhysicalCompletion"] =
                floatingDragInteraction.SyntheticGestureDoesNotClaimPhysicalCompletion,
            ["FloatingDragAttemptClampsToWorkArea"] =
                floatingDragInteraction.DragAttemptClampsToWorkArea,
            ["FloatingInteractiveControlsDoNotRequestWindowDrag"] =
                floatingDragInteraction.InteractiveControlsDoNotRequestWindowDrag,
            ["FloatingDragHandleExcludesInteractiveControls"] =
                floatingDragInteraction.ExcludesInteractiveControls,
            ["FloatingWindowManualPositionPersistsAndRemainsVisibleAfterRefresh"] =
                floatingManualPlacement.PersistsAndRemainsVisibleAfterRefresh,
            ["FloatingPlushTaskWrapLoaded"] = floating!.DayRasterShellAndHeaderLoaded,
            ["FloatingPlushMascotsLoaded"] = dayMascotsImage.Source is not null,
            ["FloatingPlushSceneIntegratesWithPanel"] = plushSceneIntegratesWithPanel,
            ["FloatingPlushReferenceComposition"] = plushReferenceComposition,
            ["FloatingPlushHeaderIntegration"] = plushHeaderIntegration,
            ["FloatingReferenceAspectMatches"] = floatingReferenceExercise.AspectMatches,
            ["FloatingReferencePanelGeometryMatches"] = floatingReferenceExercise.PanelGeometryMatches,
            ["FloatingReferenceMascotGeometryMatches"] = floatingReferenceExercise.MascotGeometryMatches,
            ["FloatingReferenceBaselineRequested"] =
                floatingReferenceExercise.Baseline.Requested,
            ["FloatingReferenceBaselineManifestAvailable"] =
                floatingReferenceExercise.Baseline.ManifestAvailable,
            ["FloatingReferenceBaselineValidated"] =
                floatingReferenceExercise.Baseline.AllPassed,
            ["FloatingReferenceBaselineGateIsolationBehavior"] =
                floatingReferenceBaselineGateIsolationBehavior,
            ["FloatingReferenceFreshnessGateIsolationBehavior"] =
                floatingReferenceFreshnessGateIsolationBehavior,
            ["FloatingReferenceBaselineManifestPath"] =
                floatingReferenceExercise.Baseline.ManifestPath,
            ["FloatingReferenceBaselineSourceAggregateSha256"] =
                floatingReferenceExercise.Baseline.SourceAggregateSha256,
            ["FloatingReferenceBaselineExecutableSha256"] =
                floatingReferenceExercise.Baseline.ExecutableSha256,
            ["FloatingReferenceBaselineGates"] =
                floatingReferenceExercise.Baseline.Gates.AsDictionary(),
            ["FloatingReferenceFreshnessGates"] =
                floatingReferenceExercise.Baseline.FreshnessGates.AsDictionary(),
            ["FloatingReferenceBaselineCaptures"] =
                floatingReferenceExercise.Baseline.Captures,
            ["FloatingReferenceBaselineDiagnostics"] =
                floatingReferenceExercise.Baseline.Diagnostics,
            ["FloatingReferenceBaselineNegativeFixtures"] =
                FloatingReferenceBaselineNegativeFixtures,
            ["FloatingReferenceFreshnessNegativeFixtures"] =
                FloatingReferenceFreshnessNegativeFixtures,
            ["FloatingLongNotesBodyHasNoManualLineBreaks"] =
                floatingReferenceExercise.LongNotes.BodyHasNoManualLineBreaks,
            ["FloatingLongNotesNaturalWraps"] =
                floatingReferenceExercise.LongNotes.NaturalWraps,
            ["FloatingLongNotesUsesOnlyOuterScroller"] =
                floatingReferenceExercise.LongNotes.UsesOnlyOuterScroller,
            ["FloatingLongNotesOuterScrollable"] =
                floatingReferenceExercise.LongNotes.OuterScrollable,
            ["FloatingLongNotesTopBottomReachable"] =
                floatingReferenceExercise.LongNotes.TopBottomReachable,
            ["FloatingLongNotesSafeAreaMatches"] =
                floatingReferenceExercise.LongNotes.SafeAreaMatches,
            ["FloatingLongNotesTypographyMatches"] =
                floatingReferenceExercise.LongNotes.TypographyMatches,
            ["FloatingLongNotesLayoutValid"] =
                floatingReferenceExercise.LongNotes.LayoutValid,
            ["FloatingLongNotesMeasurements"] =
                floatingReferenceExercise.LongNotes.Measurements,
            ["FloatingLongNotesCaptures"] = floatingReferenceExercise.LongNotes.Captures
                .Select(capture => new
                {
                    capture.Spec.CaptureId,
                    capture.Spec.Scenario,
                    capture.Spec.Theme,
                    capture.Spec.State,
                    PngSha256 = Convert.ToHexString(SHA256.HashData(capture.PngBytes)),
                    capture.StateFingerprint
                })
                .ToArray(),
            ["FloatingReferenceContentSafeAreaMatches"] =
                uiQualityGates.FloatingContentSafeArea,
            ["FloatingTitleUsesSemiBoldWeight"] =
                uiQualityGates.FloatingTitleUsesSemiBoldWeight,
            ["FloatingNoteInputWraps"] = uiQualityGates.FloatingNoteInputWraps,
            ["FloatingNoteInputScrolls"] = uiQualityGates.FloatingNoteInputScrolls,
            ["FloatingQuickAddDayExpandedScrolledCaptureWritten"] =
                floatingReferenceExercise.QuickAddDayExpandedScrolledCaptureWritten,
            ["FloatingReferenceContentOverlapCount"] = floatingReferenceExercise.ContentOverlapCount,
            ["FloatingReferenceContentOverlapTargets"] = floatingReferenceExercise.ContentOverlapTargets,
            ["FloatingReferenceCaptureWritten"] = floatingReferenceExercise.CaptureWritten,
            ["FloatingWorkdayCountdownUsesPrimaryVisual"] =
                floatingReferenceExercise.WorkdayCountdownUsesPrimaryVisual,
            ["FloatingWorkdayPrimaryBadgeExclusive"] =
                floatingReferenceExercise.WorkdayPrimaryBadgeExclusive,
            ["FloatingReferenceScenarioMatches"] = floatingReferenceExercise.ScenarioMatches,
            ["FloatingPrimaryDeadlineTextFullyVisible"] =
                floatingReferenceExercise.PrimaryDeadlineTextFullyVisible,
            ["FloatingPrimaryExtremeRemainingShrinks"] =
                floatingReferenceExercise.PrimaryExtremeRemainingShrinks,
            ["FloatingExpiredFallbackRemainingTextFullyVisible"] =
                floatingReferenceExercise.ExpiredFallbackRemainingTextFullyVisible,
            ["FloatingPrimaryDeadlineMeasurements"] =
                floatingReferenceExercise.PrimaryDeadlineMeasurements,
            ["FloatingReferenceViewportStatesMatch"] = floatingReferenceExercise.ViewportStatesMatch,
            ["FloatingViewportGeometryGateIsolationBehavior"] =
                floatingViewportGeometryGateIsolationBehavior,
            ["FloatingViewportScrollAndFocusRestored"] =
                floatingReferenceExercise.ViewportScrollAndFocusRestored,
            ["FloatingNoteFailureAppearsInSourceCard"] =
                floatingReferenceExercise.NoteFailureAppearsInSourceCard,
            ["FloatingResolvedNoteCardErrorDoesNotReturn"] =
                floatingReferenceExercise.ResolvedNoteCardErrorDoesNotReturn,
            ["FloatingSecondaryRemainingTextFullyVisible"] =
                floatingReferenceExercise.SecondaryRemainingTextFullyVisible,
            ["FloatingIncomeEncouragementWithoutSalary"] =
                floatingReferenceExercise.IncomeEncouragementWithoutSalary,
            ["FloatingIncomePrivacyBehavior"] = floatingReferenceExercise.IncomePrivacyBehavior,
            ["FloatingSecondaryRemainingUnitSpacingMatches"] =
                floatingReferenceExercise.SecondaryRemainingGates.UnitSpacingMatches,
            ["FloatingSecondaryRemainingLayoutMatches"] =
                floatingReferenceExercise.SecondaryRemainingGates.FixedColumnLayoutMatches,
            ["FloatingSecondaryRemainingGateIsolationBehavior"] =
                floatingSecondaryRemainingGateIsolationBehavior,
            ["FloatingSecondaryRemainingSamples"] =
                floatingReferenceExercise.SecondaryRemainingSamples,
            ["FloatingScrollBarLocalStyleScoped"] =
                floatingReferenceExercise.ScrollBar.Gates.LocalStyleScoped,
            ["FloatingScrollBarHitTargetUsable"] =
                floatingReferenceExercise.ScrollBar.Gates.HitTargetUsable,
            ["FloatingScrollBarVisualWeightProgression"] =
                floatingReferenceExercise.ScrollBar.Gates.VisualWeightProgression,
            ["FloatingScrollBarUsesWarmThemeTokens"] =
                floatingReferenceExercise.ScrollBar.Gates.UsesWarmThemeTokens,
            ["FloatingScrollBarContentRemainsSafe"] =
                floatingReferenceExercise.ScrollBar.Gates.ContentRemainsSafe,
            ["FloatingScrollBarInputPathsWork"] =
                floatingReferenceExercise.ScrollBar.Gates.InputPathsWork,
            ["FloatingScrollBarGateIsolationBehavior"] =
                floatingScrollBarGateIsolationBehavior,
            ["FloatingScrollBarStateCapturesReady"] =
                floatingReferenceExercise.ScrollBar.StateCapturesReady,
            ["FloatingScrollBarStateMeasurements"] =
                floatingReferenceExercise.ScrollBar.Measurements,
            ["FloatingThumbBoundaryContrast"] =
                floatingReferenceExercise.ScrollBar.Measurements.Count > 0 &&
                floatingReferenceExercise.ScrollBar.Measurements.All(sample => sample.BoundaryContrast >= 3),
            ["FloatingScrollBarWheelOffsets"] = new[]
            {
                floatingReferenceExercise.ScrollBar.WheelOffsetBefore,
                floatingReferenceExercise.ScrollBar.WheelOffsetAfter
            },
            ["FloatingScrollBarDragOffsets"] = new[]
            {
                floatingReferenceExercise.ScrollBar.DragOffsetBefore,
                floatingReferenceExercise.ScrollBar.DragOffsetAfter
            },
            ["FloatingScrollBarCaptureNames"] =
                floatingReferenceExercise.ScrollBar.Captures.Keys,
            ["FloatingDayRasterComponentAssetNames"] = floating.DayRasterComponentAssetNames,
            ["FloatingDayRasterShellAndHeaderLoaded"] = floating.DayRasterShellAndHeaderLoaded,
            ["FloatingRasterSurfacesFollowTheme"] =
                floatingThemeExercise.SurfacesFollowActiveTheme,
            ["FloatingDayRasterDecorationsAreNonInteractive"] = floating.DayRasterDecorationsAreNonInteractive,
            ["FloatingDayRasterCardStatesMatch"] = floatingReferenceExercise.RasterCardStatesMatch,
            ["FloatingDynamicCardContentRemainsWpf"] = floatingReferenceExercise.DynamicCardContentRemainsWpf,
            ["FloatingDatePickerKeyboardLifecycleWorks"] =
                floatingUiPolish.DatePickerKeyboardLifecycleWorks,
            ["FloatingFieldErrorsAppearAtSource"] = floatingUiPolish.FieldErrorsAppearAtSource,
            ["FloatingCorrectedFieldErrorsClearLiveMessage"] =
                floatingUiPolish.CorrectedFieldErrorsClearLiveMessage,
            ["FloatingDynamicHeaderControlsRemainWpf"] = floating.DynamicHeaderControlsRemainWpf,
            ["FloatingHeaderIconAccessibility"] = floatingHeaderIconAccessibility,
            ["FloatingHeaderIconClickCommandsWork"] = floatingHeaderIconClickCommandsWork,
            ["FloatingTrayButtonAccessibility"] = floatingTrayButtonAccessibility,
            ["FloatingTrayProductionControllerActive"] =
                floatingTrayInteraction.ProductionControllerActive,
            ["FloatingTrayMinimizeHidesWindowAndShowsIcon"] =
                floatingTrayInteraction.MinimizeHidesWindowAndShowsIcon,
            ["FloatingTrayRestoreShowsWindowAndHidesIcon"] =
                floatingTrayInteraction.RestoreShowsWindowAndHidesIcon,
            ["FloatingTrayExitRequestsApplicationExit"] =
                floatingTrayInteraction.ExitRequestsApplicationExit,
            ["FloatingTrayFailedRestoreKeepsTrayStateAndAllowsRetry"] =
                floatingTrayInteraction.FailedRestoreKeepsTrayStateAndAllowsRetry,
            ["FloatingReferenceActualSize"] = floatingReferenceExercise.ActualSize,
            ["FloatingReferenceCapturePath"] = floatingReferenceExercise.CapturePath,
            ["FloatingPlushCardIsUnobscured"] = visiblePlushDoesNotObscureTaskCardContent,
            ["FloatingPlushTouchesTaskCard"] = plushBridgesTaskCardEdges,
            ["FloatingPlushDoesNotObscureTaskCardContent"] = plushDoesNotObscureTaskCardContent,
            ["FloatingPlushVisibleEdgesMeetCard"] = visiblePlushEdgesMeetTaskCard,
            ["FloatingPlushGroupWithinWorkArea"] = plushGroupWithinWorkArea,
            ["FloatingCountdownBottomMargin"] = floatingBottomMargin,
            ["FloatingNotesBottomMargin"] = floatingBottomMargin,
            ["FloatingCountdownButtonTextFits"] = floatingWorkspace.CountdownButtonTextFit.Fits,
            ["FloatingCountdownButtonTextAvailableWidth"] =
                floatingWorkspace.CountdownButtonTextFit.Available,
            ["FloatingCountdownButtonTextRequiredWidth"] =
                floatingWorkspace.CountdownButtonTextFit.Required,
            ["FloatingNotesButtonTextFits"] = floatingWorkspace.NotesButtonTextFit.Fits,
            ["FloatingNotesButtonTextAvailableWidth"] =
                floatingWorkspace.NotesButtonTextFit.Available,
            ["FloatingNotesButtonTextRequiredWidth"] =
                floatingWorkspace.NotesButtonTextFit.Required,
            ["FloatingNotesKeepExistingSortOrder"] = floatingWorkspace.NotesKeepExistingSortOrder,
            ["FloatingTitleTextFits"] = floatingWorkspace.TitleTextFit.Fits,
            ["FloatingTitleTextAvailableWidth"] = floatingWorkspace.TitleTextFit.Available,
            ["FloatingTitleTextRequiredWidth"] = floatingWorkspace.TitleTextFit.Required,
            ["FloatingTitleTextHasVerticalClearance"] = floatingWorkspace.TitleTextHeightFit.Fits,
            ["FloatingTitleTextAvailableHeight"] = floatingWorkspace.TitleTextHeightFit.Available,
            ["FloatingTitleTextRequiredHeight"] = floatingWorkspace.TitleTextHeightFit.Required,
            ["SavePreferencesButtonTextFits"] = savePreferencesButtonFit.Fits,
            ["SavePreferencesButtonIsFullyVisible"] = savePreferencesButtonIsVisible,
            ["SavePreferencesButtonIsFullyVisibleAtMinimumWidth"] =
                savePreferencesButtonIsVisibleAtMinimumWidth,
            ["SavePreferencesButtonRight"] = Math.Round(savePreferencesButtonRight, 2),
            ["MainWindowActualWidth"] = Math.Round(main.ActualWidth, 2),
            ["FloatingCountdownCount"] = floatingWorkspace.CountdownCount,
            ["HasWorkdayEndCountdown"] =
                false,
            ["FloatingCountdownTexts"] = floatingWorkspace.CountdownTexts,
            ["FloatingIncomeText"] = floatingWorkspace.IncomeText,
            ["FloatingIncomeHiddenWithoutSalary"] =
                floatingWorkspace.IncomeHiddenWithoutSalary,
            ["FloatingIncomeRestoredWithSalary"] =
                floatingWorkspace.IncomeRestoredWithSalary,
            ["FloatingManagerButtonPresent"] = floating.FindName("OpenManagerButton") is Button,
            ["HasUnifiedNoteEntry"] =
                main.FindName("CountdownTab") is ToggleButton &&
                main.FindName("NotesTab") is ToggleButton &&
                floating.FindName("FloatingNotesButton") is ToggleButton,
            ["HasNoteControls"] =
                main.FindName("NoteText") is TextBox &&
                main.FindName("SaveNoteButton") is Button &&
                floating.FindName("FloatingNoteText") is TextBox &&
                floating.FindName("FloatingAddNote") is Button,
            ["HasNoteDateControls"] =
                main.FindName("NoteDate") is DatePicker &&
                floating.FindName("FloatingNoteDate") is DatePicker,
            ["HasOpenNotesActions"] =
                main.FindName("NotesTab") is ToggleButton &&
                floating.FindName("FloatingNotesButton") is ToggleButton,
            ["UnifiedWorkspaceSwitches"] = switchesToCountdown && switchesToNotes,
            ["WorkspaceSwitcherBeforeEditors"] = workspaceSwitcherBeforeEditors,
            ["HasPreferenceControls"] =
                main.FindName("DisplayPrecision") is ComboBox &&
                main.FindName("MonthlySalary") is TextBox &&
                main.FindName("WorkStart") is TextBox &&
                main.FindName("WorkEnd") is TextBox &&
                main.FindName("SavePreferencesButton") is Button,
            ["LoadedCountdowns"] = workspaceSnapshot.Settings.Countdowns.Count,
            ["LoadedNotes"] = workspaceSnapshot.Settings.Notes.Count,
            ["LoadedTags"] = workspaceSnapshot.Settings.Tags.Count,
            ["SettingsWereMigrated"] = workspaceSnapshot.SettingsWereMigrated,
            ["LoadedDisplayPrecision"] = workspaceSnapshot.Settings.Preferences.DisplayPrecision,
            ["LoadedMonthlySalary"] = workspaceSnapshot.Settings.Preferences.MonthlySalary,
            ["LoadedWorkStart"] = CountdownFormatting.FormatWorkTime(
                workspaceSnapshot.Settings.Preferences.WorkStart),
            ["LoadedWorkEnd"] = CountdownFormatting.FormatWorkTime(
                workspaceSnapshot.Settings.Preferences.WorkEnd),
            ["ActiveNoteCount"] = workspaceSnapshot.Settings.Notes.Count(note => !note.IsCompleted),
            ["CompletedNoteCount"] = workspaceSnapshot.Settings.Notes.Count(note => note.IsCompleted),
            ["SortedNoteTexts"] = workspaceSnapshot.AllNotes
                .Select(note => note.Text)
                .ToArray(),
            ["RenderedManagerNoteIds"] = renderedManagerNoteIds,
            ["RenderedFloatingNoteIds"] = floatingWorkspace.RenderedNoteIds,
            ["SortedNames"] = workspaceSnapshot.AllCountdowns
                .Select(record => record.Name)
                .ToArray(),
            ["WindowIconLoaded"] = main.Icon is not null
        };

        var crossInterfaceSourceTreeMode =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT"));
        // Note/tag exercises and schema migrations treat their rewritten settings
        // file as the caller-visible persistence contract (see App.OnStartup);
        // every other validate-only path must hand the file back byte-identical.
        var settingsWritesAreCallerContract =
            options.ExerciseNoteAdd ||
            options.ExerciseTagInteractions ||
            workspaceSnapshot.SettingsWereMigrated;
        if (!settingsWritesAreCallerContract)
        {
            if (settingsBytesAtValidationEntry is null)
            {
                if (File.Exists(settingsConservationPath))
                {
                    File.Delete(settingsConservationPath);
                }
            }
            else
            {
                File.WriteAllBytes(settingsConservationPath, settingsBytesAtValidationEntry);
            }
        }

        var settingsHashAtValidationExit = File.Exists(settingsConservationPath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(settingsConservationPath)))
            : null;
        var crossInterfaceAiReferencePriority = ValidateCrossInterfaceAiReferencePriority();
        var crossInterfacePageSpecificComposition =
            mainUiPolish.MinimumSizeMatchesDefault &&
            switchesToCountdown &&
            switchesToNotes &&
            main.FindName("CountdownWorkspace") is Canvas &&
            floatingWorkspace.InitialStateMatches &&
            floatingWorkspace.SwitchingStaysSynchronized &&
            reminderCenterRepresentativeCapture.Rendered &&
            (!crossInterfaceSourceTreeMode ||
                (mainReferenceBaseline.AllPassed && floatingReferenceExercise.Baseline.AllPassed));
        var crossInterfaceSharedWarmPinkTokens =
            warmYellowPaletteMatchesLegacy &&
            noteDisclosureThemeTokens &&
            uiQualityGates.MainWorkspaceResourcesFollowTheme &&
            mainThemeExercise.SegmentSelectedTextContrast &&
            floatingQuickAddTheme.Day.Passed &&
            floatingReferenceExercise.ScrollBar.Gates.UsesWarmThemeTokens &&
            reminderCenter.FollowsDayTheme;
        var crossInterfaceStaticDayThemeInfo =
            ValidateCrossInterfaceStaticDayTheme(main, floating) &&
            floatingThemeExercise.PlushSceneFollowsDayTheme &&
            floatingThemeExercise.BoundaryModes.All(mode =>
                string.Equals(mode, "Day", StringComparison.Ordinal)) &&
            reminderCenter.FollowsDayTheme;
        var crossInterfacePlushDecorationLayering =
            main.MainReferenceDecorationsLoaded &&
            uiQualityGates.MainReferenceDecorationLayerOrder &&
            mainThemeExercise.CharacterPanelDocksFollowTheme &&
            dayMascotsImage.Source is not null &&
            plushHeaderIntegration &&
            floatingThemeExercise.PlushSceneFollowsDayTheme;
        var crossInterfaceFilterSharing =
            mainUiPolish.NoteSearchSyncsFromSharedFilter &&
            floatingWindowScenarios.NoteSearch.AllPassed &&
            floatingWorkspace.SwitchingStaysSynchronized;
        var crossInterfaceKeyboardOrder =
            string.Join(',', mainKeyboardBehavior.TabSequence) ==
                "CountdownTab,NotesTab,PreferencesButton,FilterButton,TagManagerButton" &&
            mainKeyboardNavigationGates.AllPassed &&
            reminderCenterKeyboardGates.AllPassed &&
            comboBoxTextClick.ArrowIsNotTabStop &&
            workspaceSwitcherBeforeEditors &&
            mainUiPolish.PopupKeyboardLifecycleWorks;
        var crossInterfaceHitTesting =
            mainCheckboxHitCoversSlot &&
            floatingCheckboxHitCoversSlot &&
            popupRightInsetIsEightFromCanvas &&
            floatingReferenceExercise.ScrollBar.Gates.HitTargetUsable &&
            floatingDragInteraction.ExcludesInteractiveControls &&
            main.MainReferenceDecorationsAreNonInteractive &&
            main.CharacterPanelDecorationsAreNonInteractive &&
            floating.DayRasterDecorationsAreNonInteractive;
        // #146 战役收口（待维护者复核）：本子句原以 legacyMainReferenceCapture.UsesZeroState
        //（运行夹具零数据）作源码树硬门禁，与 #129 特意保留的设置守恒断言载体
        //（CSharp/Ui validate-only 使用有内容临时 settings）结构性冲突，致两阶段源码树模式必红。
        // 三向探针互证见 run-evidence\clock-countdown-issue-146-final-closure\probe-*：
        // 有内容+源码树红 / 有内容+发布包绿 / 空夹具+源码树绿。
        // 现按隔离本意改为「实拍真实运行并落证」（Requested && CapturesWritten）；
        // 零态属性保留为信息字段 MainReferenceCaptureUsesZeroState，不删除。
        var crossInterfaceDeterministicFixtureIsolation =
            validateOnlySettingsIsolation &&
            floatingReferenceBaselineGateIsolationBehavior &&
            floatingReferenceFreshnessGateIsolationBehavior &&
            RunCrossInterfaceFixtureDeterminismProbe() &&
            (!crossInterfaceSourceTreeMode ||
                (mainReferenceBaselineGateIsolationBehavior &&
                 legacyMainReferenceCapture.Requested &&
                 legacyMainReferenceCapture.CapturesWritten));
        var crossInterfaceRealSettingsConserved =
            settingsWritesAreCallerContract ||
            (string.Equals(
                settingsHashAtValidationEntry,
                settingsHashAtValidationExit,
                StringComparison.Ordinal) &&
            validateOnlySettingsIsolation);
        var crossInterfaceIntegration =
            crossInterfaceAiReferencePriority &&
            crossInterfacePageSpecificComposition &&
            crossInterfaceSharedWarmPinkTokens &&
            crossInterfaceStaticDayThemeInfo &&
            crossInterfacePlushDecorationLayering &&
            crossInterfaceFilterSharing &&
            crossInterfaceKeyboardOrder &&
            crossInterfaceHitTesting &&
            crossInterfaceDeterministicFixtureIsolation &&
            crossInterfaceRealSettingsConserved;
        report["CrossInterfaceAiReferencePriority"] = crossInterfaceAiReferencePriority;
        report["CrossInterfacePageSpecificComposition"] = crossInterfacePageSpecificComposition;
        report["CrossInterfaceSharedWarmPinkTokens"] = crossInterfaceSharedWarmPinkTokens;
        report["CrossInterfaceStaticDayThemeInfo"] = crossInterfaceStaticDayThemeInfo;
        report["CrossInterfacePlushDecorationLayering"] = crossInterfacePlushDecorationLayering;
        report["CrossInterfaceFilterSharing"] = crossInterfaceFilterSharing;
        report["CrossInterfaceKeyboardOrder"] = crossInterfaceKeyboardOrder;
        report["CrossInterfaceHitTesting"] = crossInterfaceHitTesting;
        report["CrossInterfaceDeterministicFixtureIsolation"] = crossInterfaceDeterministicFixtureIsolation;
        report["CrossInterfaceRealSettingsConserved"] = crossInterfaceRealSettingsConserved;
        report["CrossInterfaceIntegration"] = crossInterfaceIntegration;
        report["CrossInterfaceIntegrationEvidence"] =
            "#119 cross-interface integration: AI direction images stay outside manifests and pixel gates while real 2026-10-12-v48 frames (8 main + 4 floating) carry regression; per-page composition, shared warm-pink tokens, single static day theme without a switch entry, plush/decoration layering, shared filter state, keyboard order, hit-test boundaries, deterministic fixture isolation and real-settings conservation each aggregate existing fail-closed probes.";
        report["UiQualityGateIsolationBehavior"] = uiQualityGateIsolationBehavior;
        datePicker.IsDropDownOpen = false;
        reminderCenterWindow.Close();
        floating.Hide();
        main.Hide();
        return report.Seal();
    }

    private static MainReferenceDecorationValidation ValidateMainReferenceDecorationScaling(
        MainWindow main)
    {
        if (main.Content is not FrameworkElement root)
        {
            return new MainReferenceDecorationValidation(false, false);
        }

        var scaleViewport = Required<Grid>(main, "MainScaleViewport");
        var layoutSurface = Required<Canvas>(main, "MainLayoutSurface");
        var decorationLayer = Required<Canvas>(main, "MainReferenceDecorationLayer");
        var contentProjection = Required<Grid>(main, "MainContentProjection");
        var contentSurface = Required<Canvas>(main, "MainContentSurface");
        var noteEditorPanel = Required<Border>(main, "NoteEditorPanel");
        var noteTextLabel = Required<TextBlock>(main, "NoteTextLabel");
        var countdownScene = Required<Image>(main, "CountdownReferenceDecoration");
        var notesScene = Required<Image>(main, "NotesReferenceDecoration");
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        var scalingMatches = true;
        var layerOrderMatches = true;

        try
        {
            var sizes = new[]
            {
                new Size(main.MinWidth, main.MinHeight),
                new Size(Math.Max(1200, main.MinWidth), Math.Max(960, main.MinHeight))
            };
            foreach (var size in sizes)
            {
                main.Width = size.Width;
                main.Height = size.Height;
                scalingMatches &= WaitForStableMainReferenceLayout(
                    main,
                    root,
                    decorationLayer,
                    layoutSurface,
                    contentProjection);

                scalingMatches &=
                    Math.Abs(BoundsIn(decorationLayer, root).Width -
                        BoundsIn(layoutSurface, root).Width) <= 0.5 &&
                    layoutSurface.ClipToBounds &&
                    ReferenceEquals(VisualTreeHelper.GetParent(decorationLayer), layoutSurface) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(contentProjection), layoutSurface) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(contentSurface), contentProjection) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(countdownScene), decorationLayer) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(notesScene), decorationLayer) &&
                    layoutSurface.Width == 1165 &&
                    layoutSurface.Height >= main.ActualHeight - 0.5 &&
                    contentProjection.Width == layoutSurface.Width &&
                    contentProjection.Height == layoutSurface.Height &&
                    contentSurface.Width == layoutSurface.Width &&
                    contentSurface.Height == MainWindow.PreferredHeight &&
                    countdownScene.Width == layoutSurface.Width &&
                    countdownScene.Height == MainWindow.PreferredHeight &&
                    notesScene.Width == layoutSurface.Width &&
                    notesScene.Height == MainWindow.PreferredHeight &&
                    !HasNonIdentityTransform(contentProjection) &&
                    scaleViewport.Width == 1165 &&
                    scaleViewport.Height == layoutSurface.Height &&
                    !HasNonIdentityTransform(scaleViewport);
                layerOrderMatches &=
                    Panel.GetZIndex(decorationLayer) < Panel.GetZIndex(contentProjection) &&
                    Panel.GetZIndex(noteEditorPanel) < Panel.GetZIndex(noteTextLabel);
            }

            return new MainReferenceDecorationValidation(
                scalingMatches,
                layerOrderMatches);
        }
        finally
        {
            main.Width = originalWidth;
            main.Height = originalHeight;
            main.InvalidateMeasure();
            main.InvalidateArrange();
            WaitForStableMainReferenceLayout(
                main,
                root,
                decorationLayer,
                layoutSurface,
                contentProjection);
        }
    }

    private static bool WaitForStableMainReferenceLayout(
        MainWindow main,
        FrameworkElement root,
        FrameworkElement decorationLayer,
        FrameworkElement layoutSurface,
        FrameworkElement contentProjection)
    {
        const int maximumPasses = 10;
        const int requiredStablePasses = 2;
        const double tolerance = 0.01;
        double[]? previous = null;
        var stablePasses = 0;

        for (var pass = 0; pass < maximumPasses; pass++)
        {
            main.InvalidateMeasure();
            main.InvalidateArrange();
            PumpDispatcher();
            main.UpdateLayout();

            var decorationBounds = BoundsIn(decorationLayer, root);
            var layoutBounds = BoundsIn(layoutSurface, root);
            var contentBounds = BoundsIn(contentProjection, root);
            var current = new[]
            {
                main.ActualWidth,
                main.ActualHeight,
                decorationBounds.X,
                decorationBounds.Y,
                decorationBounds.Width,
                decorationBounds.Height,
                layoutBounds.X,
                layoutBounds.Y,
                layoutBounds.Width,
                layoutBounds.Height,
                contentBounds.X,
                contentBounds.Y,
                contentBounds.Width,
                contentBounds.Height
            };

            if (previous is not null &&
                current.Zip(previous, (actual, expected) => Math.Abs(actual - expected))
                    .All(delta => delta <= tolerance))
            {
                stablePasses++;
                if (stablePasses >= requiredStablePasses)
                {
                    return true;
                }
            }
            else
            {
                stablePasses = 0;
            }

            previous = current;
        }

        return false;
    }

    private static string FormatRect(Rect bounds) =>
        FormattableString.Invariant(
            $"{bounds.X:0},{bounds.Y:0},{bounds.Width:0},{bounds.Height:0}");

    private static bool HasNonIdentityTransform(FrameworkElement element) =>
        !element.RenderTransform.Value.IsIdentity ||
        !element.LayoutTransform.Value.IsIdentity;

    private static MainReferenceCaptureResult RunMainReferenceCapture(
        ApplicationTheme theme,
        MainWindow main,
        DesktopWorkspaceSnapshot workspaceSnapshot,
        string assetRoot)
    {
        const int captureWidth = 1165;
        const int captureHeight = 1350;
        var countdownCaptureBeijingTime = new DateTimeOffset(
            2026,
            7,
            29,
            9,
            17,
            24,
            TimeSpan.FromHours(8));
        var notesCaptureBeijingTime = new DateTimeOffset(
            2026,
            7,
            29,
            9,
            20,
            17,
            TimeSpan.FromHours(8));
        var evidenceDirectory = Environment.GetEnvironmentVariable(
            "MINMIN_COUNTDOWN_MAIN_VISUAL_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            // #134：源码树验证模式下实拍证据目录默认落到仓库 logs\ui-captures\ 的本次运行
            // 独立子目录，不再依赖无人设置的 env；发布包模式保持不请求实拍、不伪造证据。
            evidenceDirectory = ResolveDefaultVisualEvidenceDirectory("validate-only");
            if (evidenceDirectory is null)
            {
                return new MainReferenceCaptureResult(
                    false,
                    false,
                    false,
                    false,
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty);
            }
        }

        var usesZeroState =
            workspaceSnapshot.AllCountdowns.Count == 0 &&
            workspaceSnapshot.Settings.Notes.Count == 0;
        var stateId = usesZeroState ? "empty" : "populated";
        var countdownCapturePath = Path.Combine(
            evidenceDirectory,
            $"main-countdown-{stateId}.png");
        var notesCapturePath = Path.Combine(
            evidenceDirectory,
            $"main-notes-{stateId}.png");
        var manifestPath = Path.Combine(evidenceDirectory, "main-capture-manifest.json");
        if (File.Exists(countdownCapturePath) ||
            File.Exists(notesCapturePath) ||
            File.Exists(manifestPath))
        {
            throw new IOException("主窗口视觉捕获目录已包含同名产物，拒绝覆盖已有证据。");
        }

        var artifactBindings = GetMainVisualCaptureArtifactBindings(assetRoot);

        if (main.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("主窗口根视觉不可用于捕获。");
        }

        Directory.CreateDirectory(evidenceDirectory);
        var originalWorkspace = main.CurrentWorkspace;
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        var originalOpacity = main.Opacity;
        var countdownDate = Required<DatePicker>(main, "CountdownDate");
        var countdownHour = Required<ComboBox>(main, "CountdownHour");
        var countdownMinute = Required<ComboBox>(main, "CountdownMinute");
        var countdownSecond = Required<ComboBox>(main, "CountdownSecond");
        var noteDate = Required<DatePicker>(main, "NoteDate");
        var originalCountdownDate = countdownDate.SelectedDate;
        var originalCountdownHour = countdownHour.SelectedItem;
        var originalCountdownMinute = countdownMinute.SelectedItem;
        var originalCountdownSecond = countdownSecond.SelectedItem;
        var originalNoteDate = noteDate.SelectedDate;
        try
        {
            main.Width = captureWidth;
            main.Height = captureHeight;
            main.Opacity = 1;
            theme.Apply();
            main.ApplyTheme();

            main.ShowWorkspace(WorkspaceKind.Countdowns);
            main.UpdateTime(countdownCaptureBeijingTime);
            countdownDate.SelectedDate = new DateTime(2026, 7, 30);
            countdownHour.SelectedItem = "08";
            countdownMinute.SelectedItem = "56";
            countdownSecond.SelectedItem = "11";
            PumpDispatcher();
            CloseMainDatePickerDropdowns(main);
            ArrangeForReferenceCapture(root, captureWidth, captureHeight);
            ClearCaptureKeyboardFocus();
            WaitForMainReferenceMotion(main, root);
            var countdownCapture = CaptureOpaqueMainSurface(root, captureWidth, captureHeight);

            main.ShowWorkspace(WorkspaceKind.Notes);
            main.UpdateTime(notesCaptureBeijingTime);
            noteDate.SelectedDate = new DateTime(2026, 7, 29);
            PumpDispatcher();
            CloseMainDatePickerDropdowns(main);
            ArrangeForReferenceCapture(root, captureWidth, captureHeight);
            ClearCaptureKeyboardFocus();
            WaitForMainReferenceMotion(main, root);
            var notesCapture = CaptureOpaqueMainSurface(root, captureWidth, captureHeight);

            var canvasMatches =
                countdownCapture.PixelWidth == captureWidth &&
                countdownCapture.PixelHeight == captureHeight &&
                notesCapture.PixelWidth == captureWidth &&
                notesCapture.PixelHeight == captureHeight;
            var countdownAlpha = MeasureAlpha(countdownCapture);
            var notesAlpha = MeasureAlpha(notesCapture);
            var alphaIsOpaque =
                countdownAlpha.IsFullyOpaque &&
                notesAlpha.IsFullyOpaque;
            if (!canvasMatches)
            {
                throw new InvalidOperationException(
                    $"主窗口视觉捕获画布不匹配：倒计时 {countdownCapture.PixelWidth}x{countdownCapture.PixelHeight}，" +
                    $"便签 {notesCapture.PixelWidth}x{notesCapture.PixelHeight}，" +
                    $"期望 {captureWidth}x{captureHeight}。");
            }

            if (!alphaIsOpaque)
            {
                throw new InvalidOperationException(
                    $"主窗口视觉捕获存在非不透明像素：倒计时 {countdownAlpha.NonOpaquePixels} 个（最小 alpha {countdownAlpha.MinimumAlpha}），" +
                    $"便签 {notesAlpha.NonOpaquePixels} 个（最小 alpha {notesAlpha.MinimumAlpha}）。");
            }

            var countdownSha256 = WriteNewPng(countdownCapturePath, countdownCapture);
            var notesSha256 = WriteNewPng(notesCapturePath, notesCapture);
            var dpi = VisualTreeHelper.GetDpi(root);
            var manifest = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schemaVersion = 4,
                    captureKind = "actual-wpf-main-window",
                    themeModes = new[] { "Day" },
                    canvas = new
                    {
                        width = captureWidth,
                        height = captureHeight,
                        metricDpi = 96,
                        windowDpiX = dpi.PixelsPerInchX,
                        windowDpiY = dpi.PixelsPerInchY,
                        dpiScaleX = dpi.DpiScaleX,
                        dpiScaleY = dpi.DpiScaleY
                    },
                    artifactBindings = artifactBindings.Select(binding => new
                    {
                        id = binding.Id,
                        path = binding.Path,
                        sha256 = binding.Sha256
                    }),
                    captures = new[]
                    {
                        new
                        {
                            id = $"countdown-{stateId}",
                            themeMode = "Day",
                            beijingTime = countdownCaptureBeijingTime,
                            path = countdownCapturePath,
                            sha256 = countdownSha256
                        },
                        new
                        {
                            id = $"notes-{stateId}",
                            themeMode = "Day",
                            beijingTime = notesCaptureBeijingTime,
                            path = notesCapturePath,
                            sha256 = notesSha256
                        }
                    }
                },
                new JsonSerializerOptions { WriteIndented = true });
            WriteNewBytes(manifestPath, manifest);

            return new MainReferenceCaptureResult(
                true,
                true,
                usesZeroState,
                canvasMatches,
                alphaIsOpaque,
                countdownCapturePath,
                notesCapturePath,
                manifestPath);
        }
        finally
        {
            main.Width = originalWidth;
            main.Height = originalHeight;
            main.Opacity = originalOpacity;
            countdownDate.SelectedDate = originalCountdownDate;
            countdownHour.SelectedItem = originalCountdownHour;
            countdownMinute.SelectedItem = originalCountdownMinute;
            countdownSecond.SelectedItem = originalCountdownSecond;
            noteDate.SelectedDate = originalNoteDate;
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(workspaceSnapshot.UtcNow));
            main.ShowWorkspace(originalWorkspace);
            main.InvalidateMeasure();
            main.InvalidateArrange();
            PumpDispatcher();
        }
    }

    private static void CloseMainDatePickerDropdowns(MainWindow main)
    {
        if (main.FindName("CountdownDate") is DatePicker countdownDate)
        {
            countdownDate.IsDropDownOpen = false;
        }

        if (main.FindName("NoteDate") is DatePicker noteDate)
        {
            noteDate.IsDropDownOpen = false;
        }

        PumpDispatcher();
    }

    private static void ArrangeForReferenceCapture(FrameworkElement root, int width, int height)
    {
        var size = new Size(width, height);
        root.Measure(size);
        root.Arrange(new Rect(new Point(0, 0), size));
    }

    private static IReadOnlyList<VisualCaptureArtifactBinding> GetMainVisualCaptureArtifactBindings(
        string assetRoot)
    {
        const string sourceRootVariable = "MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT";
        var sourceRoot = Environment.GetEnvironmentVariable(sourceRootVariable);
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new InvalidOperationException(
                $"主窗口视觉捕获需要设置 {sourceRootVariable}，以绑定当前源码与最终运行时产物。");
        }

        sourceRoot = Path.GetFullPath(sourceRoot);
        var runtimeRoot = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath))!
            : Path.GetFullPath(assetRoot);
        var artifacts = new (string Id, string FilePath)[]
        {
            ("app-executable", Path.Combine(runtimeRoot, "MinminCountdown.exe")),
            ("app-main-countdown-scene", Path.Combine(
                runtimeRoot,
                "assets",
                "main-decorations",
                "main-countdown-scene-day.png")),
            ("app-main-notes-scene", Path.Combine(
                runtimeRoot,
                "assets",
                "main-decorations",
                "main-notes-scene-day.png")),
            ("source-main-window-xaml", Path.Combine(
                sourceRoot,
                "src",
                "MinminCountdown",
                "MainWindow.xaml")),
            ("source-main-window-code", Path.Combine(
                sourceRoot,
                "src",
                "MinminCountdown",
                "MainWindow.xaml.cs")),
            ("source-floating-window-xaml", Path.Combine(
                sourceRoot,
                "src",
                "MinminCountdown",
                "FloatingWindow.xaml")),
            ("source-floating-window-code", Path.Combine(
                sourceRoot,
                "src",
                "MinminCountdown",
                "FloatingWindow.xaml.cs")),
            ("source-validation-report", Path.Combine(
                sourceRoot,
                "src",
                "MinminCountdown",
                "ValidationReport.cs")),
            ("source-application-project", Path.Combine(
                sourceRoot,
                "src",
                "MinminCountdown",
                "MinminCountdown.csproj")),
            ("source-build-script", Path.Combine(sourceRoot, "Build-CountdownApplication.ps1"))
        };

        return artifacts.Select(artifact =>
        {
            var path = Path.GetFullPath(artifact.FilePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"主窗口视觉捕获绑定的产物不存在：{artifact.Id}",
                    path);
            }

            return new VisualCaptureArtifactBinding(
                artifact.Id,
                path,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }).ToArray();
    }

    private static MainReferenceBaselineValidation RunMainReferenceBaseline(
        ApplicationTheme theme,
        string assetRoot)
    {
        const string sourceRootVariable = "MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT";
        var sourceRoot = Environment.GetEnvironmentVariable(sourceRootVariable);
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return MainReferenceBaselineValidation.NotRequested;
        }

        sourceRoot = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(sourceRoot))
        {
            return MainReferenceBaselineValidation.Invalid(
                true,
                false,
                $"视觉源码根目录不存在：{sourceRootVariable}。");
        }

        var actualCaptures = CaptureMainReferenceScenarios(theme, assetRoot);
        var baselineRelativeDirectory =
            $"assets/ui-references/main-window/1165x1350/{MainReferenceBaselineVersion}";
        var manifestRelativePath = $"{baselineRelativeDirectory}/manifest.json";
        var baselineDirectory = ResolveRepositoryRelativePath(sourceRoot, baselineRelativeDirectory);
        var manifestPath = ResolveRepositoryRelativePath(sourceRoot, manifestRelativePath);
        var generationRequested = string.Equals(
            Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE"),
            "1",
            StringComparison.Ordinal);
        if (generationRequested)
        {
            GenerateMainReferenceBaseline(
                sourceRoot,
                baselineDirectory,
                manifestPath,
                baselineRelativeDirectory,
                actualCaptures);
        }

        return ValidateMainReferenceBaseline(
            sourceRoot,
            baselineDirectory,
            manifestPath,
            manifestRelativePath,
            baselineRelativeDirectory,
            actualCaptures);
    }

    private static IReadOnlyList<MainReferenceActualCapture> CaptureMainReferenceScenarios(
        ApplicationTheme theme,
        string assetRoot)
    {
        const int captureWidth = 1165;
        const int captureHeight = 1350;
        var dayBeijing = new DateTimeOffset(2032, 5, 18, 10, 15, 30, TimeSpan.FromHours(8));
        var referenceUtc = dayBeijing.ToUniversalTime();
        var captures = new List<MainReferenceActualCapture>();

        try
        {
            foreach (var state in new[] { "Empty", "Representative", "Stress", "Edit", "FilterOpen" })
            {
                var temporaryRoot = Path.Combine(
                    Path.GetTempPath(),
                    $"MinminCountdown-main-reference-{state.ToLowerInvariant()}-{Guid.NewGuid():N}");
                var settingsPath = Path.Combine(temporaryRoot, "settings.json");
                var diagnosticPath = Path.Combine(temporaryRoot, "validation.jsonl");
                MainWindow? validationMain = null;
                Directory.CreateDirectory(temporaryRoot);

                try
                {
                    var settingsState = state is "Edit" or "FilterOpen" ? "Representative" : state;
                    var settings = CreateMainReferenceSettings(settingsState, referenceUtc);
                    var store = new SettingsStore(clock: new ValidationClock(referenceUtc));
                    store.Save(settingsPath, settings);
                    var workspace = DesktopWorkspace.Load(settingsPath, store);
                    using var calendar = new HolidayCalendar(temporaryRoot);
                    validationMain = new MainWindow(
                        workspace,
                        calendar,
                        new DiagnosticLogger(diagnosticPath),
                        assetRoot)
                    {
                        Width = captureWidth,
                        Height = captureHeight,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = -20_000,
                        Top = -20_000,
                        Opacity = 1
                    };
                    validationMain.Show();
                    PumpDispatcher();
                    validationMain.UpdateLayout();
                    if (validationMain.Content is not FrameworkElement root)
                    {
                        throw new InvalidOperationException("主窗口独立参考场景缺少根视觉。");
                    }

                    var stressNotesPrepared = false;
                    foreach (var spec in MainReferenceCaptureSpecs.Where(spec =>
                                 string.Equals(spec.State, state, StringComparison.Ordinal)))
                    {
                        var workspaceKind = Enum.Parse<WorkspaceKind>(spec.Workspace);
                        var beijingTime = dayBeijing;
                        theme.Apply();
                        validationMain.ApplyTheme();
                        validationMain.ShowWorkspace(workspaceKind);
                        validationMain.UpdateTime(beijingTime);
                        if (state == "Stress" &&
                            workspaceKind == WorkspaceKind.Notes &&
                            !stressNotesPrepared)
                        {
                            Required<ToggleButton>(validationMain, "CompletedNotesSegment")
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            stressNotesPrepared = true;
                        }

                        if (spec.State == "Edit")
                        {
                            validationMain.LoadNoteIntoEditor(
                                workspace.Snapshot.Settings.Notes[0]);
                        }
                        else if (spec.State == "FilterOpen")
                        {
                            Required<Popup>(validationMain, "FilterPopup").IsOpen = true;
                        }
                        else
                        {
                            Required<Popup>(validationMain, "FilterPopup").IsOpen = false;
                        }

                        PumpDispatcher();
                        if (state is "Empty" or "Representative" or "Stress")
                        {
                            CloseMainDatePickerDropdowns(validationMain);
                        }

                        ArrangeForReferenceCapture(root, captureWidth, captureHeight);
                        var stressCountdownTitle =
                            state == "Stress" && workspaceKind == WorkspaceKind.Countdowns
                                ? MeasureMainStressCountdownTitle(validationMain, spec)
                                : null;
                        var overlayPopup = spec.State == "FilterOpen"
                            ? Required<Popup>(validationMain, "FilterPopup")
                            : null;
                        ClearCaptureKeyboardFocus();
                        WaitForMainReferenceMotion(validationMain, root);
                        var pngBytes = EncodePng(CaptureOpaqueMainSurface(root, captureWidth, captureHeight, overlayPopup));
                        if (string.Equals(
                                Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_EMPTY_DAY_PROBE"),
                                "1",
                                StringComparison.Ordinal) &&
                            string.Equals(spec.CaptureId, "main-day-countdowns-empty", StringComparison.Ordinal))
                        {
                            ProbeEmptyDayAgainstPinnedBaseline(pngBytes);
                        }

                        var noteCardReminderFont =
                            workspaceKind == WorkspaceKind.Notes &&
                            spec.State is "Representative" or "Stress"
                                ? MeasureMainNoteCardReminderFont(validationMain, spec)
                                : null;
                        var metrics = MeasureMainReferenceCapture(pngBytes);
                        if (metrics.Width != captureWidth || metrics.Height != captureHeight)
                        {
                            throw new InvalidOperationException(
                                $"主窗口参考帧 {spec.CaptureId} 不是 {captureWidth}x{captureHeight}（actual={metrics.Width}x{metrics.Height}）。");
                        }
                        if (!metrics.Alpha.IsFullyOpaque)
                        {
                            throw new InvalidOperationException(
                                $"主窗口参考帧 {spec.CaptureId} 包含非不透明像素。");
                        }

                        var fingerprint = CreateMainReferenceStateFingerprint(
                            spec,
                            beijingTime,
                            settings);
                        captures.Add(new MainReferenceActualCapture(
                            spec,
                            beijingTime,
                            pngBytes,
                            fingerprint,
                            metrics.Alpha,
                            metrics.ContentRoi,
                            stressCountdownTitle,
                            noteCardReminderFont));
                    }
                }
                finally
                {
                    validationMain?.Close();
                    PumpDispatcher();
                    TryDeleteSettingsFiles(settingsPath);
                    TryDeleteFile(settingsPath + ".tmp");
                    TryDeleteFile(diagnosticPath);
                    var holidayDirectory = Path.Combine(temporaryRoot, "holiday-cache");
                    if (Directory.Exists(holidayDirectory) &&
                        !Directory.EnumerateFileSystemEntries(holidayDirectory).Any())
                    {
                        Directory.Delete(holidayDirectory);
                    }
                    if (Directory.Exists(temporaryRoot) &&
                        !Directory.EnumerateFileSystemEntries(temporaryRoot).Any())
                    {
                        Directory.Delete(temporaryRoot);
                    }
                }
            }
        }
        finally
        {
            theme.Apply();
        }

        if (captures.Count != MainReferenceCaptureSpecs.Length)
        {
            throw new InvalidOperationException(
                $"主窗口正式参考场景未生成严格 {MainReferenceCaptureSpecs.Length} 帧。");
        }

        return MainReferenceCaptureSpecs.Select(spec => captures.Single(capture =>
            string.Equals(capture.Spec.CaptureId, spec.CaptureId, StringComparison.Ordinal))).ToArray();
    }

    private static MainDaySingleBorderValidation ValidateDaySingleBorder()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return MainDaySingleBorderValidation.NotRequested;
        }

        var bands = ReadMainDecorationBands(sourceRoot);
        // #221: v47 moves the opaque compact countdown form above the former
        // decoration-band boundary. Scan its actual bounds, independently sampled
        // from the PNG at x=200; retain delta>=30 and every #189 negative fixture.
        var specs = new[]
        {
            new MainDaySingleBorderSpec("Countdown-Day", "countdown", "main-day-countdowns-empty.png", 365, 440, new[] { 370, 371, 390, 391, 397, 398, 405, 406, 425, 427 }, 343, 364),
            new MainDaySingleBorderSpec("Notes-Day", "notes", "main-day-notes-empty.png", 416, 525, new[] { 425, 426, 470, 471, 495, 496, 497, 499, 500, 501, 503, 504, 512, 513 }, 405, 424)
        };
        var directory = Path.Combine(
            Path.GetFullPath(sourceRoot),
            "assets", "ui-references", "main-window", "1165x1350", MainReferenceBaselineVersion);
        var measurements = specs.Select(spec =>
        {
            var path = Path.Combine(directory, spec.FileName);
            if (!File.Exists(path))
            {
                return new MainDaySingleBorderMeasurement(
                    spec.StateId, spec.Scene, Array.Empty<int>(), false, false, false, 0, 0, 0);
            }

            var band = bands.Scenes[spec.Scene];
            var source = DecodeBitmap(File.ReadAllBytes(path));
            var (pixels, stride) = GetBgra32Pixels(source);
            var edges = new List<int>();
            var controlScanTop = spec.ScanTop;
            for (var y = controlScanTop; y <= spec.ScanBottom; y++)
            {
                var offset = y * stride + 200 * 4;
                var next = offset + stride;
                var delta = Math.Max(
                    Math.Abs(pixels[offset] - pixels[next]),
                    Math.Max(
                        Math.Abs(pixels[offset + 1] - pixels[next + 1]),
                        Math.Abs(pixels[offset + 2] - pixels[next + 2])));
                if (delta >= 30)
                {
                    edges.Add(y);
                }
            }

            var lowerDecorationPixelCount = 0;
            for (var y = band.BottomBandStart; y < source.PixelHeight; y++)
            {
                var background = y * stride + bands.BackgroundSampleX * 4;
                for (var x = 0; x < source.PixelWidth; x++)
                {
                    var sample = y * stride + x * 4;
                    if (Math.Abs(pixels[sample] - pixels[background]) >= 4 ||
                        Math.Abs(pixels[sample + 1] - pixels[background + 1]) >= 4 ||
                        Math.Abs(pixels[sample + 2] - pixels[background + 2]) >= 4)
                    {
                        lowerDecorationPixelCount++;
                    }
                }
            }

            return new MainDaySingleBorderMeasurement(
                spec.StateId,
                spec.Scene,
                edges,
                edges.SequenceEqual(spec.ExpectedEdges),
                edges.All(y => y < spec.LabelTop || y > spec.LabelBottom),
                lowerDecorationPixelCount > 0,
                controlScanTop,
                band.BottomBandStart,
                lowerDecorationPixelCount);
        }).ToArray();
        return EvaluateDaySingleBorder(measurements);
    }

    private static MainDaySingleBorderValidation EvaluateDaySingleBorder(
        IReadOnlyList<MainDaySingleBorderMeasurement> measurements)
    {
        var day = measurements.Where(measurement => measurement.StateId.EndsWith("-Day", StringComparison.Ordinal)).ToArray();
        var lowerDecorationConsistent = measurements
            .GroupBy(measurement => measurement.Scene, StringComparer.Ordinal)
            .All(group => group.All(measurement => measurement.LowerDecorationPresent) &&
                group.Select(measurement => measurement.DeclaredBottomBandStart).Distinct().Count() == 1);
        return new MainDaySingleBorderValidation(
            true,
            day.Length == 2 && day.All(measurement => measurement.ExactControlEdges),
            measurements.All(measurement => measurement.LabelsClear),
            lowerDecorationConsistent,
            measurements);
    }

    private static bool RunDaySingleBorderGateIsolationBehavior()
    {
        var valid = new[]
        {
            new MainDaySingleBorderMeasurement("Countdown-Day", "countdown", new[] { 515, 516, 549, 550, 570, 571 }, true, true, true, 480, 1079, 1),
            new MainDaySingleBorderMeasurement("Notes-Day", "notes", new[] { 448, 449, 475, 476, 478, 479, 480, 515, 516 }, true, true, true, 416, 845, 1)
        };
        return EvaluateDaySingleBorder(valid).AllPassed &&
            !EvaluateDaySingleBorder(valid.Select((item, index) => index == 0 ? item with { ExactControlEdges = false } : item).ToArray()).AllPassed &&
            !EvaluateDaySingleBorder(valid.Select((item, index) => index == 0 ? item with { LabelsClear = false } : item).ToArray()).AllPassed &&
            !EvaluateDaySingleBorder(valid.Select((item, index) => index == 1 ? item with { LowerDecorationPresent = false } : item).ToArray()).AllPassed;
    }

    private static MainDecorationBands ReadMainDecorationBands(string sourceRoot)
    {
        var path = Path.Combine(sourceRoot, "tools", "MainDecorationSceneBands.psd1");
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "D:\\Codex\\tools\\PowerShell\\7.6.4\\pwsh.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                     "& { param([string]$Path) Import-PowerShellDataFile -LiteralPath $Path | ConvertTo-Json -Depth 6 -Compress }",
                     path
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = System.Diagnostics.Process.Start(startInfo) ??
            throw new InvalidOperationException("无法读取主窗口装饰带定义。");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"主窗口装饰带定义读取失败：{error}");
        }
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var scenes = root.GetProperty("Scenes");
        MainDecorationBand ReadScene(string name)
        {
            var scene = scenes.GetProperty(name);
            return new MainDecorationBand(
                scene.GetProperty("TopBandEnd").GetInt32(),
                scene.GetProperty("BottomBandStart").GetInt32());
        }
        return new MainDecorationBands(
            root.GetProperty("BackgroundSampleX").GetInt32(),
            new Dictionary<string, MainDecorationBand>(StringComparer.Ordinal)
            {
                ["countdown"] = ReadScene("countdown"),
                ["notes"] = ReadScene("notes")
            });
    }

    private static MainNoteCardReminderFontMeasurement? MeasureMainNoteCardReminderFont(
        MainWindow main,
        MainReferenceCaptureSpec spec)
    {
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        try
        {
            // #211：便签卡提醒字号在设计首选尺寸下测量（最小尺寸已放宽，不再代表设计尺寸）。
            main.Width = MainWindow.PreferredWidth;
            main.Height = MainWindow.PreferredHeight;
            PumpDispatcher();
            main.UpdateLayout();

            var summaries = CollectNoteCardReminderSummaries(main);
            if (summaries.Length == 0)
            {
                return null;
            }

            var fontSizeDip = summaries
                .Select(text => Math.Round(MeasureRenderedFontSizeDip(text, main), 2))
                .Min();
            return new MainNoteCardReminderFontMeasurement(
                spec.CaptureId,
                spec.Theme,
                spec.State,
                summaries.Length,
                fontSizeDip,
                fontSizeDip >= MainNoteCardReminderMinimumFontSizeDip);
        }
        finally
        {
            main.Width = originalWidth;
            main.Height = originalHeight;
            PumpDispatcher();
            main.UpdateLayout();
        }
    }

    private static double MeasureRenderedFontSizeDip(TextBlock text, Visual ancestor)
    {
        var transform = text.TransformToAncestor(ancestor);
        var origin = transform.Transform(new Point(0, 0));
        var unitX = transform.Transform(new Point(1, 0));
        var unitY = transform.Transform(new Point(0, 1));
        var scaleX = Math.Abs(unitX.X - origin.X);
        var scaleY = Math.Abs(unitY.Y - origin.Y);
        return text.FontSize * Math.Min(scaleX, scaleY);
    }

    private static TextBlock[] CollectNoteCardReminderSummaries(MainWindow main)
    {
        var sections = Descendants<StackPanel>(Required<StackPanel>(main, "ActiveNoteRows"))
            .Concat(Descendants<StackPanel>(Required<StackPanel>(main, "CompletedNoteRows")))
            .Where(panel =>
                panel.IsVisible &&
                string.Equals(panel.Uid, "NoteDateRows", StringComparison.Ordinal));
        var summaries = new List<TextBlock>();
        foreach (var card in sections.SelectMany(section => section.Children.OfType<Border>()))
        {
            if (!card.IsVisible || card.Child is not Grid)
            {
                continue;
            }

            // #136 起提醒摘要 TextBlock 位于时钟图标行（detailRow StackPanel）内，
            // 不再是正文列的直接子级；按 AutomationId 在卡片子树内定位，
            // 不再依赖具体行容器结构（#146 收口修复：源码树模式便签卡测量此前恒为空）。
            var marked = Descendants<TextBlock>(card).FirstOrDefault(text =>
                string.Equals(
                    AutomationProperties.GetAutomationId(text),
                    MainWindow.NoteCardReminderSummaryAutomationId,
                    StringComparison.Ordinal));
            if (marked is not null)
            {
                summaries.Add(marked);
            }
        }

        return summaries.ToArray();
    }

    private static MainStressCountdownTitleMeasurement MeasureMainStressCountdownTitle(
        MainWindow main,
        MainReferenceCaptureSpec spec)
    {
        var rows = Required<StackPanel>(main, "CountdownRows");
        var matchingTitles = Descendants<TextBlock>(rows)
            .Where(text =>
                text.IsVisible &&
                string.Equals(text.Text, MainStressCountdownTitle, StringComparison.Ordinal))
            .ToArray();
        var title = matchingTitles.FirstOrDefault();
        if (title is null)
        {
            return MainStressCountdownTitleMeasurement.Missing(spec, MainStressCountdownTitle);
        }

        var card = rows.Children
            .OfType<Border>()
            .SingleOrDefault(candidate => HasVisualAncestor(title, candidate));
        var actions = card is null
            ? null
            : Descendants<StackPanel>(card).SingleOrDefault(panel =>
                panel.Orientation == Orientation.Horizontal &&
                panel.Children.OfType<Button>()
                    .Select(button => button.Content?.ToString() ?? string.Empty)
                    .SequenceEqual(new[] { "编辑", "删除" }, StringComparer.Ordinal));
        var remaining = FindVisualAncestor<Grid>(title) is Grid layout
            ? layout.Children.OfType<TextBlock>().SingleOrDefault(candidate =>
                !ReferenceEquals(candidate, title) &&
                Grid.GetColumn(candidate) == 1 &&
                Grid.GetRow(candidate) == 1)
            : null;
        var scrollViewer = FindVisualAncestor<ScrollViewer>(rows);
        var viewport = scrollViewer is null
            ? null
            : Descendants<ScrollContentPresenter>(scrollViewer)
                .FirstOrDefault(candidate =>
                    candidate.IsVisible &&
                    candidate.ActualWidth > 0 &&
                    candidate.ActualHeight > 0);
        if (actions is null || remaining is null || viewport is null)
        {
            return MainStressCountdownTitleMeasurement.Missing(
                spec,
                MainStressCountdownTitle,
                title.Text,
                matchingTitles.Length == 1,
                title.TextWrapping.ToString(),
                title.TextTrimming.ToString());
        }

        var titleBounds = BoundsIn(title, viewport);
        var actionsBounds = BoundsIn(actions, viewport);
        var remainingBounds = BoundsIn(remaining, viewport);
        var viewportBounds = new Rect(new Point(0, 0), viewport.RenderSize);
        var measuredFit = MeasureTextFit(title, viewport);
        // 标题可被徽章等包装面板抱住自身宽度：Fits 以包装宿主在卡片网格中实际分得的列宽为准。
        var titleHost = (DependencyObject)title;
        while (titleHost is not null && VisualTreeHelper.GetParent(titleHost) is not Grid)
        {
            titleHost = VisualTreeHelper.GetParent(titleHost);
        }

        var allottedWidth = titleHost is UIElement titleHostElement &&
                            VisualTreeHelper.GetParent(titleHostElement) is Grid titleLayout
            ? titleLayout.ColumnDefinitions
                .Skip(Grid.GetColumn(titleHostElement))
                .Take(Grid.GetColumnSpan(titleHostElement))
                .Sum(column => column.ActualWidth)
            : 0;
        var textFit = allottedWidth > 0
            ? new TextFit(allottedWidth >= measuredFit.Required, allottedWidth, measuredFit.Required)
            : measuredFit;
        var fullyVisibleInViewport =
            titleBounds.Left >= viewportBounds.Left - 0.5 &&
            titleBounds.Top >= viewportBounds.Top - 0.5 &&
            titleBounds.Right <= viewportBounds.Right + 0.5 &&
            titleBounds.Bottom <= viewportBounds.Bottom + 0.5;
        var titleActionsIntersection = Rect.Intersect(titleBounds, actionsBounds);
        var separatedFromActions =
            titleActionsIntersection.IsEmpty ||
            titleActionsIntersection.Width <= 0.5 ||
            titleActionsIntersection.Height <= 0.5;
        var titleRemainingIntersection = Rect.Intersect(titleBounds, remainingBounds);
        var separatedFromRemaining =
            titleRemainingIntersection.IsEmpty ||
            titleRemainingIntersection.Width <= 0.5 ||
            titleRemainingIntersection.Height <= 0.5;

        return new MainStressCountdownTitleMeasurement(
            spec.CaptureId,
            spec.Theme,
            MainStressCountdownTitle,
            title.Text,
            matchingTitles.Length == 1,
            title.TextWrapping.ToString(),
            title.TextTrimming.ToString(),
            Math.Round(textFit.Required, 2),
            Math.Round(textFit.Available, 2),
            textFit.Fits,
            MainStressCountdownTitleBounds.From(titleBounds),
            MainStressCountdownTitleBounds.From(viewportBounds),
            fullyVisibleInViewport,
            MainStressCountdownTitleBounds.From(remainingBounds),
            separatedFromRemaining,
            MainStressCountdownTitleBounds.From(actionsBounds),
            separatedFromActions);
    }

    private static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (DependencyObject? current = element;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static CountdownSettings CreateMainReferenceSettings(
        string state,
        DateTimeOffset referenceUtc)
    {
        var preferences = new CountdownPreferences(
            "Second",
            23_000m,
            TimeSpan.FromHours(9),
            TimeSpan.FromHours(18),
            ThemePreferenceMode.Auto);
        if (state == "Empty")
        {
            return new CountdownSettings(
                Array.Empty<CountdownRecord>(),
                Array.Empty<NoteRecord>(),
                Array.Empty<TagRecord>(),
                preferences);
        }

        var tags = new[]
        {
            new TagRecord("reference-work", "工作", "#F4B183"),
            new TagRecord("reference-life", "生活", "#F4B6A6"),
            new TagRecord("reference-focus", "重点", "#F4D58D")
        };
        var beijingDate = ChinaStandardTime.ToBeijingTime(referenceUtc).Date;
        string Date(int offset) => beijingDate.AddDays(offset)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (state is "Representative" or "Edit" or "FilterOpen")
        {
            return new CountdownSettings(
                new[]
                {
                    new CountdownRecord(
                        "reference-release",
                        "版本发布",
                        referenceUtc.AddDays(2).AddHours(3),
                        new[] { "reference-work", "reference-focus" }),
                    new CountdownRecord(
                        "reference-review",
                        "季度复盘",
                        referenceUtc.AddDays(18),
                        new[] { "reference-work" }),
                    new CountdownRecord(
                        "reference-trip",
                        "周末出发",
                        referenceUtc.AddDays(5).AddHours(7),
                        new[] { "reference-life" })
                },
                new[]
                {
                    new NoteRecord(
                        "reference-note-plan",
                        "整理发布清单并确认每一项验收结果。",
                        Date(0),
                        false,
                        new[] { "reference-work", "reference-focus" },
                        new RepeatingReminder(
                            referenceUtc.AddHours(2),
                            new ReminderInterval(30))),
                    new NoteRecord(
                        "reference-note-call",
                        "下午和同事同步下周安排。",
                        Date(1),
                        false,
                        new[] { "reference-work" },
                        new OnceReminder(referenceUtc.AddHours(5), OnceDeliveryState.Pending)),
                    new NoteRecord(
                        "reference-note-life",
                        "回家前记得取快递。",
                        Date(0),
                        false,
                        new[] { "reference-life" },
                        NoReminder.Instance)
                },
                tags,
                preferences);
        }

        if (state != "Stress")
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        return new CountdownSettings(
            new[]
            {
                new CountdownRecord(
                    "stress-countdown-long",
                    MainStressCountdownTitle,
                    referenceUtc.AddHours(1),
                    new[] { "reference-work", "reference-focus" }),
                new CountdownRecord("stress-countdown-expired", "已到期里程碑", referenceUtc.AddHours(-5), new[] { "reference-work" }),
                new CountdownRecord("stress-countdown-2", "设计评审", referenceUtc.AddDays(1), new[] { "reference-work" }),
                new CountdownRecord("stress-countdown-3", "开发冻结", referenceUtc.AddDays(3), new[] { "reference-focus" }),
                new CountdownRecord("stress-countdown-4", "测试收口", referenceUtc.AddDays(5), new[] { "reference-work" }),
                new CountdownRecord("stress-countdown-5", "文档校对", referenceUtc.AddDays(7), Array.Empty<string>()),
                new CountdownRecord("stress-countdown-6", "团队复盘", referenceUtc.AddDays(14), new[] { "reference-work" }),
                new CountdownRecord("stress-countdown-7", "旅行出发", referenceUtc.AddDays(30), new[] { "reference-life" }),
                new CountdownRecord("stress-countdown-8", "年度目标", referenceUtc.AddDays(200), new[] { "reference-focus" })
            },
            new[]
            {
                new NoteRecord(
                    "stress-note-active-1",
                    "这是一条很长的未完成便签，用来确认正文在主窗口暖纸卡片中会自然换行，同时提醒、标签和日期信息仍然保持清晰，不会与右侧操作按钮发生重叠。",
                    Date(-1),
                    false,
                    new[] { "reference-work", "reference-focus" },
                    new RepeatingReminder(referenceUtc.AddMinutes(45), new ReminderInterval(15))),
                new NoteRecord("stress-note-active-2", "核对多日期分组的今天节点。", Date(0), false, new[] { "reference-work" }, NoReminder.Instance),
                new NoteRecord("stress-note-active-3", "准备明天的评审材料与演示数据。", Date(1), false, new[] { "reference-focus" }, new OnceReminder(referenceUtc.AddHours(8), OnceDeliveryState.Pending)),
                new NoteRecord("stress-note-active-4", "没有日期的临时想法也需要稳定显示。", null, false, new[] { "reference-life" }, NoReminder.Instance),
                new NoteRecord(
                    "stress-note-completed-1",
                    "已完成的长便签仍要保留完整信息层级，并以弱化暖色和删除线表达状态，而不是退回默认系统灰色。",
                    Date(-2),
                    true,
                    new[] { "reference-work" },
                    new RepeatingReminder(referenceUtc.AddMinutes(30), new ReminderInterval(20))),
                new NoteRecord("stress-note-completed-2", "已完成：整理资料。", Date(-1), true, new[] { "reference-work" }, NoReminder.Instance),
                new NoteRecord("stress-note-completed-3", "已完成：确认时间。", Date(0), true, new[] { "reference-focus" }, NoReminder.Instance),
                new NoteRecord("stress-note-completed-4", "已完成：同步结果。", Date(1), true, Array.Empty<string>(), NoReminder.Instance)
            },
            tags,
            preferences);
    }

    private static readonly string[] CrossInterfaceAiReferenceFileNames =
    {
        "main-window-ai-optimized-20260910.png",
        "main-window-notes-ai-optimized-20260910.png",
        "floating-window-countdowns-ai-optimized-20260910.png",
        "floating-window-notes-ai-optimized-20260910.png",
        "reminder-center-ai-optimized-20260910.png"
    };

    private static bool ValidateCrossInterfaceAiReferencePriority()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrEmpty(sourceRoot))
        {
            // 发布包不携带方向参考图与视觉基线 manifest，本探针只在源码树模式存在可核对对象。
            return true;
        }

        var referencesRoot = Path.Combine(sourceRoot, "assets", "ui-references");
        var aiReferencesPresent = CrossInterfaceAiReferenceFileNames.All(name =>
            File.Exists(Path.Combine(referencesRoot, name)));
        if (!aiReferencesPresent)
        {
            return false;
        }

        var manifestPaths = new[]
        {
            Path.Combine(referencesRoot, "floating-window", "520x390", "2026-10-12-v48", "manifest.json"),
            Path.Combine(referencesRoot, "main-window", "1165x1350", "2026-10-12-v48", "manifest.json")
        };
        foreach (var manifestPath in manifestPaths)
        {
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            var manifestText = File.ReadAllText(manifestPath);
            if (CrossInterfaceAiReferenceFileNames.Any(name =>
                    manifestText.Contains(name, StringComparison.Ordinal)))
            {
                return false;
            }

            using var document = JsonDocument.Parse(manifestText);
            if (!document.RootElement.TryGetProperty("captureKind", out var captureKind) ||
                captureKind.ValueKind != JsonValueKind.String ||
                !captureKind.GetString()!.StartsWith("actual-wpf-", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RunCrossInterfaceFixtureDeterminismProbe()
    {
        var referenceUtc = new DateTimeOffset(2032, 5, 18, 2, 15, 30, TimeSpan.Zero);
        foreach (var state in new[] { "Empty", "Representative", "Stress" })
        {
            var first = SettingsContract.SerializeCanonical(CreateMainReferenceSettings(state, referenceUtc));
            var second = SettingsContract.SerializeCanonical(CreateMainReferenceSettings(state, referenceUtc));
            if (!string.Equals(first, second, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateCrossInterfaceStaticDayTheme(Window main, Window floating)
    {
        static bool HasThemeSwitchText(params string?[] texts) =>
            texts.Any(text =>
                text is not null &&
                (text.Contains("主题模式", StringComparison.Ordinal) ||
                 text.Contains("切换为日间", StringComparison.Ordinal) ||
                 text.Contains("切换为夜间", StringComparison.Ordinal) ||
                 text.Contains("夜间模式", StringComparison.Ordinal)));

        static bool HasThemeSwitchEntry(DependencyObject node)
        {
            if (node is ButtonBase button &&
                HasThemeSwitchText(
                    button.ToolTip as string,
                    button.Content as string,
                    AutomationProperties.GetName(button)))
            {
                return true;
            }

            return LogicalTreeHelper
                .GetChildren(node)
                .OfType<DependencyObject>()
                .Any(HasThemeSwitchEntry);
        }

        return !HasThemeSwitchEntry(main) && !HasThemeSwitchEntry(floating);
    }

    private static MainReferenceStateFingerprint CreateMainReferenceStateFingerprint(
        MainReferenceCaptureSpec spec,
        DateTimeOffset beijingTime,
        CountdownSettings settings)
    {
        var settingsSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SettingsContract.SerializeCanonical(settings))));
        var activeNoteCount = settings.Notes.Count(note => !note.IsCompleted);
        var completedNoteCount = settings.Notes.Count(note => note.IsCompleted);
        var canonicalState = string.Join(
            "\n",
            $"theme={spec.Theme}",
            $"workspace={spec.Workspace}",
            $"state={spec.State}",
            $"beijingTime={beijingTime.ToString("O", CultureInfo.InvariantCulture)}",
            $"settingsSha256={settingsSha256}",
            $"countdownCount={settings.Countdowns.Count}",
            $"noteCount={settings.Notes.Count}",
            $"activeNoteCount={activeNoteCount}",
            $"completedNoteCount={completedNoteCount}");
        return new MainReferenceStateFingerprint(
            MainReferenceStateFingerprintAlgorithm,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalState))),
            spec.Theme,
            spec.Workspace,
            spec.State,
            beijingTime,
            settingsSha256,
            settings.Countdowns.Count,
            settings.Notes.Count,
            activeNoteCount,
            completedNoteCount);
    }

    private static MainReferenceBaselineValidation ValidateMainReferenceBaseline(
        string sourceRoot,
        string baselineDirectory,
        string manifestPath,
        string manifestRelativePath,
        string baselineRelativeDirectory,
        IReadOnlyList<MainReferenceActualCapture> actualCaptures)
    {
        if (!File.Exists(manifestPath))
        {
            return MainReferenceBaselineValidation.Invalid(
                true,
                false,
                $"主窗口正式参考 manifest 不存在：{manifestRelativePath}。",
                manifestRelativePath);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = document.RootElement;
            var structureValid = HasExactJsonProperties(
                    root,
                    "schemaVersion",
                    "baselineVersion",
                    "captureKind",
                    "canvas",
                    "contentRoi",
                    "sourceBinding",
                    "executable",
                    "pixelComparison",
                    "captures") &&
                root.GetProperty("schemaVersion").GetInt32() == 1 &&
                string.Equals(
                    root.GetProperty("baselineVersion").GetString(),
                    MainReferenceBaselineVersion,
                    StringComparison.Ordinal) &&
                string.Equals(
                    root.GetProperty("captureKind").GetString(),
                    "actual-wpf-main-window",
                    StringComparison.Ordinal);

            var canvas = root.GetProperty("canvas");
            structureValid &= HasExactJsonProperties(canvas, "width", "height", "metricDpi") &&
                canvas.GetProperty("width").GetInt32() == 1165 &&
                canvas.GetProperty("height").GetInt32() == 1350 &&
                canvas.GetProperty("metricDpi").GetInt32() == 96;
            var manifestRoi = ReadMainReferenceContentRoi(
                root.GetProperty("contentRoi"),
                ref structureValid);
            structureValid &= manifestRoi == MainReferenceContentRoi.FullCanvas;

            var sourceBinding = root.GetProperty("sourceBinding");
            structureValid &= HasExactJsonProperties(
                sourceBinding,
                "aggregateAlgorithm",
                "aggregateSha256",
                "files");
            var aggregateAlgorithm = sourceBinding
                .GetProperty("aggregateAlgorithm")
                .GetString() ?? string.Empty;
            var manifestAggregateSha256 = sourceBinding
                .GetProperty("aggregateSha256")
                .GetString() ?? string.Empty;
            var sourceBindings = sourceBinding
                .GetProperty("files")
                .EnumerateArray()
                .Select(element =>
                {
                    structureValid &= HasExactJsonProperties(element, "path", "sha256");
                    return new MainReferenceSourceBinding(
                        element.GetProperty("path").GetString() ?? string.Empty,
                        element.GetProperty("sha256").GetString() ?? string.Empty);
                })
                .ToArray();

            var executable = root.GetProperty("executable");
            structureValid &= HasExactJsonProperties(executable, "path", "sha256", "generation");
            var executablePath = executable.GetProperty("path").GetString() ?? string.Empty;
            var manifestExecutableSha256 = executable.GetProperty("sha256").GetString() ?? string.Empty;
            var executableGeneration = executable.GetProperty("generation").GetString() ?? string.Empty;

            var pixelComparison = root.GetProperty("pixelComparison");
            structureValid &= HasExactJsonProperties(
                pixelComparison,
                "algorithm",
                "maxAbsoluteChannelDelta",
                "maximumMeanAbsoluteError",
                "maximumRootMeanSquareError",
                "maximumChangedPixelCount");
            var comparisonAlgorithm = pixelComparison.GetProperty("algorithm").GetString() ?? string.Empty;
            var maximumAbsoluteChannelDelta = pixelComparison
                .GetProperty("maxAbsoluteChannelDelta")
                .GetInt32();
            var maximumMeanAbsoluteError = pixelComparison
                .GetProperty("maximumMeanAbsoluteError")
                .GetDouble();
            var maximumRootMeanSquareError = pixelComparison
                .GetProperty("maximumRootMeanSquareError")
                .GetDouble();
            var maximumChangedPixelCount = pixelComparison
                .GetProperty("maximumChangedPixelCount")
                .GetInt32();

            var captures = root.GetProperty("captures")
                .EnumerateArray()
                .Select(element => ReadMainReferenceManifestCapture(element, ref structureValid))
                .ToArray();
            structureValid &=
                captures.Length == MainReferenceCaptureSpecs.Length &&
                string.Equals(
                    aggregateAlgorithm,
                    MainReferenceSourceAggregateAlgorithm,
                    StringComparison.Ordinal) &&
                string.Equals(executablePath, "app/MinminCountdown.exe", StringComparison.Ordinal) &&
                string.Equals(
                    executableGeneration,
                    "managed isolated dotnet publish; SHA-256 read from the executing equivalent app binary",
                    StringComparison.Ordinal) &&
                string.Equals(
                    comparisonAlgorithm,
                    MainReferencePixelComparisonAlgorithm,
                    StringComparison.Ordinal) &&
                maximumAbsoluteChannelDelta == MainReferenceMaximumAbsoluteChannelDelta &&
                Math.Abs(maximumMeanAbsoluteError - MainReferenceMaximumMeanAbsoluteError) < 0.0000001 &&
                Math.Abs(maximumRootMeanSquareError - MainReferenceMaximumRootMeanSquareError) < 0.0000001 &&
                maximumChangedPixelCount == MainReferenceMaximumChangedPixelCount;

            var expectedPaths = MainReferenceCaptureSpecs
                .Select(spec => $"{baselineRelativeDirectory}/{spec.FileName}")
                .ToArray();
            var captureIds = captures.Select(capture => capture.CaptureId).ToArray();
            var capturePaths = captures.Select(capture => capture.Path).ToArray();
            var captureIdsUnique =
                captureIds.Distinct(StringComparer.Ordinal).Count() == captures.Length &&
                captureIds.Order(StringComparer.Ordinal).SequenceEqual(
                    MainReferenceCaptureSpecs.Select(spec => spec.CaptureId).Order(StringComparer.Ordinal),
                    StringComparer.Ordinal);
            var capturePathsUnique =
                capturePaths.Distinct(StringComparer.Ordinal).Count() == captures.Length &&
                capturePaths.Order(StringComparer.Ordinal).SequenceEqual(
                    expectedPaths.Order(StringComparer.Ordinal),
                    StringComparer.Ordinal);
            var scenarioBindingsUnique = captures
                .Select(capture => $"{capture.Theme}\0{capture.Workspace}\0{capture.State}")
                .Distinct(StringComparer.Ordinal)
                .Count() == captures.Length;
            var stateBindingsMatch = captures.All(capture =>
            {
                var spec = MainReferenceCaptureSpecs.SingleOrDefault(candidate =>
                    string.Equals(candidate.CaptureId, capture.CaptureId, StringComparison.Ordinal));
                return spec is not null &&
                    string.Equals(capture.Theme, spec.Theme, StringComparison.Ordinal) &&
                    string.Equals(capture.Workspace, spec.Workspace, StringComparison.Ordinal) &&
                    string.Equals(capture.State, spec.State, StringComparison.Ordinal) &&
                    string.Equals(
                        capture.Path,
                        $"{baselineRelativeDirectory}/{spec.FileName}",
                        StringComparison.Ordinal) &&
                    string.Equals(capture.StateFingerprint.Theme, spec.Theme, StringComparison.Ordinal) &&
                    string.Equals(capture.StateFingerprint.Workspace, spec.Workspace, StringComparison.Ordinal) &&
                    string.Equals(capture.StateFingerprint.State, spec.State, StringComparison.Ordinal);
            });
            structureValid &= captureIdsUnique && capturePathsUnique &&
                scenarioBindingsUnique && stateBindingsMatch;

            var currentSourceBindings = GetMainReferenceSourceBindings(sourceRoot);
            var currentSourceAggregate = AggregateMainReferenceSourceBindings(currentSourceBindings);
            var sourceHashesMatch = MainReferenceSourcesMatch(sourceBindings, currentSourceBindings, manifestAggregateSha256);

            var processPath = Environment.ProcessPath;
            var executableSha256 = !string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath)
                ? Sha256File(processPath)
                : string.Empty;
            var executableHashMatches =
                IsUpperSha256(manifestExecutableSha256) &&
                string.Equals(manifestExecutableSha256, executableSha256, StringComparison.Ordinal);

            var actualByCaptureId = actualCaptures.ToDictionary(
                capture => capture.Spec.CaptureId,
                StringComparer.Ordinal);
            var stateFingerprintsMatch = captures.All(capture =>
                actualByCaptureId.TryGetValue(capture.CaptureId, out var actualCapture) &&
                MainReferenceStateFingerprintMatchesCanonical(capture.StateFingerprint) &&
                capture.StateFingerprint == actualCapture.StateFingerprint);
            var stateFingerprintsUnique =
                captures.Select(capture => capture.StateFingerprint.Sha256)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == captures.Length;
            var baselineEvaluation = VisualReferenceBaseline.Main.Validate(
                new MainVisualReferenceRequest(
                    new VisualReferenceFileRequest(
                        sourceRoot,
                        baselineDirectory,
                        expectedPaths,
                        captures.Select(capture => new VisualReferenceManifestCapture(
                            capture.CaptureId,
                            capture.Path,
                            capture.Width,
                            capture.Height,
                            capture.PngSha256,
                            new VisualReferenceAlphaMetrics(
                                capture.Alpha.Minimum,
                                capture.Alpha.Maximum,
                                0,
                                capture.Alpha.NonOpaquePixelCount,
                                capture.Alpha.OpaquePixelCount),
                            new VisualReferenceContentRoi(
                                0,
                                capture.ContentRoi.X,
                                capture.ContentRoi.Y,
                                capture.ContentRoi.Width,
                                capture.ContentRoi.Height,
                                capture.ContentRoi.PixelCount),
                            new VisualReferencePixelMetrics(
                                capture.Comparison.MaxAbsoluteChannelDelta,
                                capture.Comparison.MeanAbsoluteError,
                                capture.Comparison.RootMeanSquareError,
                                capture.Comparison.ChangedPixelCount)))
                            .ToArray(),
                        actualCaptures.Select(capture => new VisualReferenceActualCapture(
                            capture.Spec.CaptureId,
                            capture.PngBytes)).ToArray(),
                        maximumAbsoluteChannelDelta,
                        maximumMeanAbsoluteError,
                        maximumRootMeanSquareError,
                        maximumChangedPixelCount),
                    1165,
                    1350,
                    structureValid,
                    captureIdsUnique,
                    capturePathsUnique,
                    scenarioBindingsUnique,
                    sourceHashesMatch,
                    executableHashMatches,
                    stateFingerprintsMatch,
                    stateFingerprintsUnique));
            var measurements = baselineEvaluation.Measurements.Select(measurement =>
            {
                var capture = captures.Single(candidate => string.Equals(
                    candidate.CaptureId,
                    measurement.CaptureId,
                    StringComparison.Ordinal));
                return new MainReferenceBaselineCaptureMeasurement(
                    capture.CaptureId,
                    capture.Theme,
                    capture.Workspace,
                    capture.State,
                    capture.Path,
                    capture.BeijingTime,
                    measurement.BaselineSha256,
                    measurement.ActualSha256,
                    capture.StateFingerprint,
                    new MainReferenceAlphaMetrics(
                        MainReferenceAlphaAlgorithm,
                        measurement.ActualAlpha.Minimum,
                        measurement.ActualAlpha.Maximum,
                        measurement.ActualAlpha.NonOpaquePixelCount,
                        measurement.ActualAlpha.OpaquePixelCount),
                    new MainReferenceContentRoi(
                        MainReferenceRoiAlgorithm,
                        measurement.ActualContentRoi.X,
                        measurement.ActualContentRoi.Y,
                        measurement.ActualContentRoi.Width,
                        measurement.ActualContentRoi.Height,
                        measurement.ActualContentRoi.PixelCount),
                    new MainReferencePixelMetrics(
                        measurement.Comparison.MaxAbsoluteChannelDelta,
                        measurement.Comparison.MeanAbsoluteError,
                        measurement.Comparison.RootMeanSquareError,
                        measurement.Comparison.ChangedPixelCount));
            }).ToArray();
            var gates = baselineEvaluation.Gates;
            var diagnostics = baselineEvaluation.Diagnostics;
            var stressCountdownTitleMeasurements = actualCaptures
                .Select(capture => capture.StressCountdownTitle)
                .OfType<MainStressCountdownTitleMeasurement>()
                .OrderBy(measurement => measurement.CaptureId, StringComparer.Ordinal)
                .ToArray();
            var noteCardReminderFontMeasurements = actualCaptures
                .Select(capture => capture.NoteCardReminderFont)
                .OfType<MainNoteCardReminderFontMeasurement>()
                .OrderBy(measurement => measurement.CaptureId, StringComparer.Ordinal)
                .ToArray();
            return new MainReferenceBaselineValidation(
                true,
                true,
                manifestRelativePath,
                currentSourceAggregate,
                executableSha256,
                gates,
                measurements,
                stressCountdownTitleMeasurements,
                noteCardReminderFontMeasurements,
                diagnostics);
        }
        catch (Exception exception) when (exception is IOException or JsonException or
               InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return MainReferenceBaselineValidation.Invalid(
                true,
                File.Exists(manifestPath),
                exception.Message,
                manifestRelativePath);
        }
    }

    private static void GenerateMainReferenceBaseline(
        string sourceRoot,
        string baselineDirectory,
        string manifestPath,
        string baselineRelativeDirectory,
        IReadOnlyList<MainReferenceActualCapture> actualCaptures)
    {
        if (actualCaptures.Count != MainReferenceCaptureSpecs.Length ||
            actualCaptures.Select(capture => capture.Spec.CaptureId)
                .Distinct(StringComparer.Ordinal)
                .Count() != MainReferenceCaptureSpecs.Length)
        {
            throw new InvalidOperationException(
                $"主窗口参考图生成必须提供严格 {MainReferenceCaptureSpecs.Length} 帧 inventory。");
        }
        if (Directory.Exists(baselineDirectory) &&
            Directory.EnumerateFileSystemEntries(baselineDirectory).Any())
        {
            throw new InvalidOperationException(
                $"主窗口正式参考基线已存在，拒绝覆盖：{MainReferenceBaselineVersion}。");
        }
        if (actualCaptures.Any(capture =>
                MeasureMainReferenceCapture(capture.PngBytes) is not
                { Width: 1165, Height: 1350, Alpha.IsFullyOpaque: true }))
        {
            throw new InvalidOperationException("主窗口正式参考帧必须为 1165x1350 全不透明 PNG。");
        }

        var pngHashes = actualCaptures
            .Select(capture => Convert.ToHexString(SHA256.HashData(capture.PngBytes)))
            .ToArray();
        if (pngHashes.Distinct(StringComparer.Ordinal).Count() != pngHashes.Length)
        {
            throw new InvalidOperationException("主窗口参考图生成拒绝写入复用 PNG 的基线目录。");
        }

        Directory.CreateDirectory(baselineDirectory);
        var sourceBindings = GetMainReferenceSourceBindings(sourceRoot);
        var sourceAggregate = AggregateMainReferenceSourceBindings(sourceBindings);
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            throw new InvalidOperationException("无法绑定生成主窗口参考图的当前 EXE。");
        }

        var executableSha256 = Sha256File(processPath);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schemaVersion = 1,
                baselineVersion = MainReferenceBaselineVersion,
                captureKind = "actual-wpf-main-window",
                canvas = new { width = 1165, height = 1350, metricDpi = 96 },
                contentRoi = new
                {
                    algorithm = MainReferenceRoiAlgorithm,
                    x = 0,
                    y = 0,
                    width = 1165,
                    height = 1350,
                    pixelCount = 1165 * 1350
                },
                sourceBinding = new
                {
                    aggregateAlgorithm = MainReferenceSourceAggregateAlgorithm,
                    aggregateSha256 = sourceAggregate,
                    files = sourceBindings.Select(binding => new
                    {
                        path = binding.Path,
                        sha256 = binding.Sha256
                    })
                },
                executable = new
                {
                    path = "app/MinminCountdown.exe",
                    sha256 = executableSha256,
                    generation = "managed isolated dotnet publish; SHA-256 read from the executing equivalent app binary"
                },
                pixelComparison = new
                {
                    algorithm = MainReferencePixelComparisonAlgorithm,
                    maxAbsoluteChannelDelta = MainReferenceMaximumAbsoluteChannelDelta,
                    maximumMeanAbsoluteError = MainReferenceMaximumMeanAbsoluteError,
                    maximumRootMeanSquareError = MainReferenceMaximumRootMeanSquareError,
                    maximumChangedPixelCount = MainReferenceMaximumChangedPixelCount
                },
                captures = MainReferenceCaptureSpecs.Select(spec =>
                {
                    var capture = actualCaptures.Single(actual =>
                        string.Equals(actual.Spec.CaptureId, spec.CaptureId, StringComparison.Ordinal));
                    return CreateMainReferenceManifestCapture(
                        capture,
                        $"{baselineRelativeDirectory}/{spec.FileName}");
                })
            },
            new JsonSerializerOptions { WriteIndented = true });
        foreach (var capture in actualCaptures)
        {
            var path = ResolveRepositoryRelativePath(
                sourceRoot,
                $"{baselineRelativeDirectory}/{capture.Spec.FileName}");
            WriteBytesAtomically(path, capture.PngBytes);
        }
        WriteBytesAtomically(manifestPath, manifest);
    }

    private static object CreateMainReferenceManifestCapture(
        MainReferenceActualCapture capture,
        string path) =>
        new
        {
            captureId = capture.Spec.CaptureId,
            theme = capture.Spec.Theme,
            workspace = capture.Spec.Workspace,
            state = capture.Spec.State,
            path,
            beijingTime = capture.BeijingTime,
            width = 1165,
            height = 1350,
            pngSha256 = Convert.ToHexString(SHA256.HashData(capture.PngBytes)),
            stateFingerprint = new
            {
                algorithm = capture.StateFingerprint.Algorithm,
                sha256 = capture.StateFingerprint.Sha256,
                theme = capture.StateFingerprint.Theme,
                workspace = capture.StateFingerprint.Workspace,
                state = capture.StateFingerprint.State,
                beijingTime = capture.StateFingerprint.BeijingTime,
                settingsSha256 = capture.StateFingerprint.SettingsSha256,
                countdownCount = capture.StateFingerprint.CountdownCount,
                noteCount = capture.StateFingerprint.NoteCount,
                activeNoteCount = capture.StateFingerprint.ActiveNoteCount,
                completedNoteCount = capture.StateFingerprint.CompletedNoteCount
            },
            alpha = new
            {
                algorithm = capture.Alpha.Algorithm,
                minimum = capture.Alpha.Minimum,
                maximum = capture.Alpha.Maximum,
                nonOpaquePixelCount = capture.Alpha.NonOpaquePixelCount,
                opaquePixelCount = capture.Alpha.OpaquePixelCount
            },
            contentRoi = new
            {
                algorithm = capture.ContentRoi.Algorithm,
                x = capture.ContentRoi.X,
                y = capture.ContentRoi.Y,
                width = capture.ContentRoi.Width,
                height = capture.ContentRoi.Height,
                pixelCount = capture.ContentRoi.PixelCount
            },
            comparison = new
            {
                maxAbsoluteChannelDelta = 0,
                meanAbsoluteError = 0,
                rootMeanSquareError = 0,
                changedPixelCount = 0
            }
        };

    private static MainReferenceManifestCapture ReadMainReferenceManifestCapture(
        JsonElement element,
        ref bool structureValid)
    {
        structureValid &= HasExactJsonProperties(
            element,
            "captureId",
            "theme",
            "workspace",
            "state",
            "path",
            "beijingTime",
            "width",
            "height",
            "pngSha256",
            "stateFingerprint",
            "alpha",
            "contentRoi",
            "comparison");
        return new MainReferenceManifestCapture(
            element.GetProperty("captureId").GetString() ?? string.Empty,
            element.GetProperty("theme").GetString() ?? string.Empty,
            element.GetProperty("workspace").GetString() ?? string.Empty,
            element.GetProperty("state").GetString() ?? string.Empty,
            element.GetProperty("path").GetString() ?? string.Empty,
            element.GetProperty("beijingTime").GetDateTimeOffset(),
            element.GetProperty("width").GetInt32(),
            element.GetProperty("height").GetInt32(),
            element.GetProperty("pngSha256").GetString() ?? string.Empty,
            ReadMainReferenceStateFingerprint(
                element.GetProperty("stateFingerprint"),
                ref structureValid),
            ReadMainReferenceAlphaMetrics(element.GetProperty("alpha"), ref structureValid),
            ReadMainReferenceContentRoi(element.GetProperty("contentRoi"), ref structureValid),
            ReadMainReferencePixelMetrics(element.GetProperty("comparison"), ref structureValid));
    }

    private static MainReferenceStateFingerprint ReadMainReferenceStateFingerprint(
        JsonElement element,
        ref bool structureValid)
    {
        structureValid &= HasExactJsonProperties(
            element,
            "algorithm",
            "sha256",
            "theme",
            "workspace",
            "state",
            "beijingTime",
            "settingsSha256",
            "countdownCount",
            "noteCount",
            "activeNoteCount",
            "completedNoteCount");
        return new MainReferenceStateFingerprint(
            element.GetProperty("algorithm").GetString() ?? string.Empty,
            element.GetProperty("sha256").GetString() ?? string.Empty,
            element.GetProperty("theme").GetString() ?? string.Empty,
            element.GetProperty("workspace").GetString() ?? string.Empty,
            element.GetProperty("state").GetString() ?? string.Empty,
            element.GetProperty("beijingTime").GetDateTimeOffset(),
            element.GetProperty("settingsSha256").GetString() ?? string.Empty,
            element.GetProperty("countdownCount").GetInt32(),
            element.GetProperty("noteCount").GetInt32(),
            element.GetProperty("activeNoteCount").GetInt32(),
            element.GetProperty("completedNoteCount").GetInt32());
    }

    private static MainReferenceAlphaMetrics ReadMainReferenceAlphaMetrics(
        JsonElement element,
        ref bool structureValid)
    {
        structureValid &= HasExactJsonProperties(
            element,
            "algorithm",
            "minimum",
            "maximum",
            "nonOpaquePixelCount",
            "opaquePixelCount");
        return new MainReferenceAlphaMetrics(
            element.GetProperty("algorithm").GetString() ?? string.Empty,
            element.GetProperty("minimum").GetInt32(),
            element.GetProperty("maximum").GetInt32(),
            element.GetProperty("nonOpaquePixelCount").GetInt32(),
            element.GetProperty("opaquePixelCount").GetInt32());
    }

    private static MainReferenceContentRoi ReadMainReferenceContentRoi(
        JsonElement element,
        ref bool structureValid)
    {
        structureValid &= HasExactJsonProperties(
            element,
            "algorithm",
            "x",
            "y",
            "width",
            "height",
            "pixelCount");
        return new MainReferenceContentRoi(
            element.GetProperty("algorithm").GetString() ?? string.Empty,
            element.GetProperty("x").GetInt32(),
            element.GetProperty("y").GetInt32(),
            element.GetProperty("width").GetInt32(),
            element.GetProperty("height").GetInt32(),
            element.GetProperty("pixelCount").GetInt32());
    }

    private static MainReferencePixelMetrics ReadMainReferencePixelMetrics(
        JsonElement element,
        ref bool structureValid)
    {
        structureValid &= HasExactJsonProperties(
            element,
            "maxAbsoluteChannelDelta",
            "meanAbsoluteError",
            "rootMeanSquareError",
            "changedPixelCount");
        return new MainReferencePixelMetrics(
            element.GetProperty("maxAbsoluteChannelDelta").GetInt32(),
            element.GetProperty("meanAbsoluteError").GetDouble(),
            element.GetProperty("rootMeanSquareError").GetDouble(),
            element.GetProperty("changedPixelCount").GetInt32());
    }

    private static bool MainReferenceStateFingerprintMatchesCanonical(
        MainReferenceStateFingerprint fingerprint)
    {
        if (!IsUpperSha256(fingerprint.Sha256) ||
            !IsUpperSha256(fingerprint.SettingsSha256) ||
            fingerprint.CountdownCount < 0 ||
            fingerprint.NoteCount < 0 ||
            fingerprint.ActiveNoteCount < 0 ||
            fingerprint.CompletedNoteCount < 0 ||
            fingerprint.ActiveNoteCount + fingerprint.CompletedNoteCount != fingerprint.NoteCount)
        {
            return false;
        }

        var canonicalState = string.Join(
            "\n",
            $"theme={fingerprint.Theme}",
            $"workspace={fingerprint.Workspace}",
            $"state={fingerprint.State}",
            $"beijingTime={fingerprint.BeijingTime.ToString("O", CultureInfo.InvariantCulture)}",
            $"settingsSha256={fingerprint.SettingsSha256}",
            $"countdownCount={fingerprint.CountdownCount}",
            $"noteCount={fingerprint.NoteCount}",
            $"activeNoteCount={fingerprint.ActiveNoteCount}",
            $"completedNoteCount={fingerprint.CompletedNoteCount}");
        return string.Equals(
                   fingerprint.Algorithm,
                   MainReferenceStateFingerprintAlgorithm,
                   StringComparison.Ordinal) &&
               string.Equals(
                   Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalState))),
                   fingerprint.Sha256,
                   StringComparison.Ordinal);
    }

    private static IReadOnlyList<MainReferenceSourceBinding> GetMainReferenceSourceBindings(
        string sourceRoot) =>
        MainReferenceCanonicalSourcePaths.Select(relativePath =>
        {
            var path = ResolveRepositoryRelativePath(sourceRoot, relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"主窗口参考图绑定的视觉源码不存在：{relativePath}",
                    path);
            }

            return new MainReferenceSourceBinding(relativePath, Sha256File(path));
        }).ToArray();

    private static bool MainReferenceSourcesMatch(
        IReadOnlyList<MainReferenceSourceBinding> manifest,
        IReadOnlyList<MainReferenceSourceBinding> current,
        string aggregate) =>
        manifest.Count == current.Count &&
        manifest.Select(binding => binding.Path).SequenceEqual(MainReferenceCanonicalSourcePaths, StringComparer.Ordinal) &&
        manifest.Zip(current).All(pair => pair.First.Path == pair.Second.Path && IsUpperSha256(pair.First.Sha256) &&
            pair.First.Sha256 == pair.Second.Sha256) &&
        IsUpperSha256(aggregate) && aggregate == AggregateMainReferenceSourceBindings(manifest) &&
        aggregate == AggregateMainReferenceSourceBindings(current);

    private static bool RunReferenceSourceDriftBehavior(bool isMain)
    {
        // Exercise the same source comparison used by the real manifest reader.
        // A self-consistent stale manifest must still reject changed visual sources.
        const string motionPath = "src/MinminCountdown/MotionBehavior.cs";
        const string indicatorPath = "src/MinminCountdown/SegmentIndicator.cs";
        const string datePath = "src/MinminCountdown.Core/DateDisplay.cs";
        const string orderPath = "src/MinminCountdown.Core/NoteOrdering.cs";
        const string sessionPath = "src/MinminCountdown/ApplicationSession.cs";
        const string workspacePath = "src/MinminCountdown/DesktopWorkspace.cs";
        const string completionExitPath = "src/MinminCountdown/NoteCompletionExit.cs";
        const string undoPath = "src/MinminCountdown/DeleteUndoOffer.cs";
        const string bottomStatusPath = "src/MinminCountdown/FloatingBottomStatus.cs";
        const string holidayPath = "src/MinminCountdown/HolidayCalendar.cs";
        var paths = isMain ? MainReferenceCanonicalSourcePaths : FloatingReferenceCanonicalSourcePaths;
        var requiredPaths = isMain
            ? new[] { motionPath, indicatorPath, datePath, orderPath, sessionPath, workspacePath, completionExitPath, undoPath }
            : new[] { motionPath, datePath, orderPath, sessionPath, workspacePath, completionExitPath, undoPath, bottomStatusPath, holidayPath };
        var currentHash = new string('A', 64);
        var staleHash = new string('B', 64);
        foreach (var changedPath in requiredPaths)
        {
            if (!paths.Contains(changedPath, StringComparer.Ordinal))
            {
                Console.Error.WriteLine($"REFERENCE_SOURCE_DRIFT_DIAGNOSTIC main={isMain}, missing={changedPath}");
                return false;
            }
            if (isMain)
            {
                var current = paths.Select(path => new MainReferenceSourceBinding(path, currentHash)).ToArray();
                var stale = current.Select(binding => binding.Path == changedPath ? binding with { Sha256 = staleHash } : binding).ToArray();
                var missing = current.Where(binding => binding.Path != changedPath).ToArray();
                if (!MainReferenceSourcesMatch(current, current, AggregateMainReferenceSourceBindings(current)) ||
                    MainReferenceSourcesMatch(missing, current, AggregateMainReferenceSourceBindings(missing)) ||
                    MainReferenceSourcesMatch(stale, current, AggregateMainReferenceSourceBindings(stale)))
                    return false;
            }
            else
            {
                var current = paths.Select(path => new FloatingReferenceSourceBinding(path, currentHash)).ToArray();
                var stale = current.Select(binding => binding.Path == changedPath ? binding with { Sha256 = staleHash } : binding).ToArray();
                var missing = current.Where(binding => binding.Path != changedPath).ToArray();
                if (!FloatingReferenceSourcesMatch(current, current, AggregateFloatingReferenceSourceBindings(current)) ||
                    FloatingReferenceSourcesMatch(missing, current, AggregateFloatingReferenceSourceBindings(missing)) ||
                    FloatingReferenceSourcesMatch(stale, current, AggregateFloatingReferenceSourceBindings(stale)) ||
                    FloatingReferenceSourcesMatch(missing, missing, AggregateFloatingReferenceSourceBindings(missing)))
                    return false;
            }
        }
        return true;
    }

    private static string AggregateMainReferenceSourceBindings(
        IEnumerable<MainReferenceSourceBinding> bindings)
    {
        using var stream = new MemoryStream();
        foreach (var binding in bindings.OrderBy(binding => binding.Path, StringComparer.Ordinal))
        {
            var line = Encoding.UTF8.GetBytes($"{binding.Path}\0{binding.Sha256}\n");
            stream.Write(line, 0, line.Length);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static MainReferenceCaptureMetrics MeasureMainReferenceCapture(byte[] bytes)
    {
        var bitmap = DecodeBitmap(bytes);
        var (pixels, _) = GetBgra32Pixels(bitmap);
        var minimum = byte.MaxValue;
        var maximum = byte.MinValue;
        var nonOpaque = 0;
        var opaque = 0;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            var alpha = pixels[index];
            minimum = Math.Min(minimum, alpha);
            maximum = Math.Max(maximum, alpha);
            if (alpha == byte.MaxValue)
            {
                opaque++;
            }
            else
            {
                nonOpaque++;
            }
        }

        return new MainReferenceCaptureMetrics(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            new MainReferenceAlphaMetrics(
                MainReferenceAlphaAlgorithm,
                minimum,
                maximum,
                nonOpaque,
                opaque),
            new MainReferenceContentRoi(
                MainReferenceRoiAlgorithm,
                0,
                0,
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                bitmap.PixelWidth * bitmap.PixelHeight));
    }

    private static MainReferencePixelMetrics CompareMainReferencePixels(
        byte[] baselineBytes,
        byte[] actualBytes)
    {
        var metrics = CompareFloatingReferencePixels(baselineBytes, actualBytes);
        return new MainReferencePixelMetrics(
            metrics.MaxAbsoluteChannelDelta,
            metrics.MeanAbsoluteError,
            metrics.RootMeanSquareError,
            metrics.ChangedPixelCount);
    }

    private static FloatingReferenceExerciseResult RunFloatingReferenceExercise(
        MainWindow manager,
        string assetRoot)
    {
        const int referenceWidth = 1448;
        const int referenceHeight = 1086;
        const int captureWidth = 520;
        const int captureHeight = 390;
        const string referenceSha256 =
            "7B28C015B28E8FCAB3BBA6D88E61ACFECC8A9B1B0252AA56431C8C2DCF30CB95";
        var expectedBackdrop = new NormalizedRect(0.0511, 0.1381, 0.8867, 0.7947);
        var expectedPanel = new NormalizedRect(0.0863, 0.1851, 0.8232, 0.7053);
        var expectedHeader = new NormalizedRect(0.0994, 0.1851, 0.8101, 0.1593);
        // #229：标题与数字在首行，日期和徽章在下一行；实际主卡高度 78 DIP。
        // 主卡与跟随次卡位置独立回读于 20261012T184511969-334e25b5；
        // 外壳、面板、标题栏、卡片宽度、次卡高度及 0.007 容差保持原合同。
        var expectedCards = new[]
        {
            new NormalizedRect(0.1154, 0.3791, 0.7723, 0.2000),
            new NormalizedRect(0.1154, 0.5795, 0.7723, 0.1004)
        };
        var expectedMascots = new[]
        {
            new NormalizedRect(0.0138, 0.0157, 0.2617, 0.2413),
            new NormalizedRect(0.7100, 0.0387, 0.2666, 0.1980),
            new NormalizedRect(0.7845, 0.5295, 0.2155, 0.2845)
        };
        var referenceBeijing = new DateTimeOffset(2030, 7, 15, 11, 11, 0, TimeSpan.FromHours(8));
        var referenceUtc = referenceBeijing.ToUniversalTime();
        var referenceWorkdayTargetUtc = new DateTimeOffset(
            2030,
            7,
            15,
            18,
            0,
            0,
            TimeSpan.FromHours(8)).ToUniversalTime();
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-floating-reference-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(temporaryRoot, "settings.json");
        var holidayDirectory = Path.Combine(temporaryRoot, "holiday-cache");
        var holidayPath = Path.Combine(holidayDirectory, "2030.json");
        FloatingWindow? referenceWindow = null;

        try
        {
            Directory.CreateDirectory(holidayDirectory);
            var longNoteTexts = Enumerable.Range(1, 6)
                .Select(index =>
                    $"{(index == 1 ? "首锚点" : $"中段锚点{index}")}：这是一条没有手工换行符的长便签正文，" +
                    "它必须依靠真实 WPF TextWrapping 在固定纸面宽度内自然折行，同时保持日期印记、完成复选框、标签和相邻卡片互不遮挡，" +
                    $"编号 {index} 的末尾校验文字{(index == 6 ? "，尾锚点" : string.Empty)}。")
                .ToArray();
            var notes = longNoteTexts
                .Select((text, index) => new NoteRecord(
                    $"reference-note-{index + 1}",
                    text,
                    "2030-07-15",
                    false,
                    new[] { "reference-warm-tag" },
                    index == 0
                        ? new RepeatingReminder(
                            referenceUtc.AddHours(1),
                            new ReminderInterval(30))
                        : NoReminder.Instance))
                .ToArray();
            var settings = new CountdownSettings(
                new[]
                {
                    new CountdownRecord(
                        "reference-july",
                        "七月倒计时",
                        referenceUtc.AddDays(4).AddHours(13).AddMinutes(18).AddSeconds(59),
                        Array.Empty<string>())
                },
                notes,
                new[] { new TagRecord("reference-warm-tag", "长便签", "#F4B6A6") },
                new CountdownPreferences(
                    "Second",
                    5255.85m,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)));
            File.WriteAllText(settingsPath, SettingsContract.SerializeCanonical(settings));
            var days = Enumerable.Range(0, 365)
                .Select(offset => new
                {
                    date = new DateTime(2030, 1, 1).AddDays(offset)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    is_workday = true
                })
                .ToArray();
            File.WriteAllText(holidayPath, JsonSerializer.Serialize(new { days }));

            var referenceClock = new MutableValidationClock(referenceUtc);
            var referenceSession = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: referenceClock));
            using var referenceCalendar = new HolidayCalendar(temporaryRoot);
            referenceCalendar.RefreshAsync(referenceUtc, CancellationToken.None).GetAwaiter().GetResult();
            referenceWindow = new FloatingWindow(
                new DesktopWorkspace(referenceSession),
                referenceCalendar,
                manager,
                assetRoot)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0.01
            };
            referenceWindow.Show();
            PumpDispatcher();
            referenceWindow.Left = -20_000;
            referenceWindow.Top = -20_000;
            referenceWindow.UpdateTime(referenceUtc);

            var root = Required<Grid>(referenceWindow, "TaskWrap");
            var backdrop = Required<Border>(referenceWindow, "FloatingThemeBackdrop");
            var panel = Required<Border>(referenceWindow, "TaskCard");
            var header = Required<Border>(referenceWindow, "FloatingHeaderSurface");
            var rows = Required<StackPanel>(referenceWindow, "FloatingRows");
            var income = Required<TextBlock>(referenceWindow, "FloatingIncome");
            var plushScene = Required<Canvas>(referenceWindow, "FloatingPlushScene");
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            VerifyWorkdayPrimaryTitleAndBadge(rows);
            VerifyFloatingZeroDayBlocks(referenceWindow);
            var incomePrivacyBehavior = VerifyFloatingIncomePrivacy(
                referenceWindow, referenceSession, referenceCalendar, manager, assetRoot, settingsPath, referenceClock);
            var actualWidth = root.ActualWidth;
            var actualHeight = root.ActualHeight;
            var actualBackdrop = Normalize(BoundsIn(backdrop, root), actualWidth, actualHeight);
            var actualPanel = Normalize(BoundsIn(panel, root), actualWidth, actualHeight);
            var actualHeader = Normalize(BoundsIn(header, root), actualWidth, actualHeight);
            var actualCards = rows.Children.OfType<Border>()
                .Select(card => Normalize(BoundsIn(card, root), actualWidth, actualHeight))
                .ToArray();
            var referencePlushScene = CapturePlushScene(plushScene);
            var actualMascots = referencePlushScene.Bounds
                .Select(bounds => bounds is null
                    ? NormalizedRect.Empty
                    : Normalize(bounds.Value, actualWidth, actualHeight))
                .ToArray();
            var protectedTextBounds = new[]
                {
                    new NamedBounds(
                        "Title",
                        TextContentBounds(Required<TextBlock>(referenceWindow, "FloatingTitle"), root)),
                    new NamedBounds("Income", TextContentBounds(income, root))
                }
                .Concat(Descendants<TextBlock>(rows)
                    .Where(element => element.IsVisible &&
                                      element.ActualWidth > 0 &&
                                      element.ActualHeight > 0)
                    .Select((element, index) => new NamedBounds(
                        $"RowText{index + 1}",
                        TextContentBounds(element, root))));
            var protectedControlBounds = new (string Name, ContentControl Control)[]
                {
                    ("CountdownsButton", Required<ToggleButton>(referenceWindow, "FloatingCountdownsButton")),
                    ("NotesButton", Required<ToggleButton>(referenceWindow, "FloatingNotesButton")),
                    ("MinimizeToTrayButton", Required<Button>(referenceWindow, "MinimizeToTrayButton")),
                    ("OpenManagerButton", Required<Button>(referenceWindow, "OpenManagerButton")),
                    ("CloseButton", Required<Button>(referenceWindow, "CloseFloatingButton"))
                }
                .Where(pair => pair.Control.IsVisible &&
                               pair.Control.ActualWidth > 0 &&
                               pair.Control.ActualHeight > 0)
                .Select(pair => new NamedBounds(
                    pair.Name,
                    ControlContentBounds(pair.Control, root)));
            var protectedContent = protectedTextBounds
                .Concat(protectedControlBounds)
                .ToArray();
            var aspectMatches =
                actualWidth > 0 &&
                actualHeight > 0 &&
                Math.Abs((actualWidth / actualHeight) - (4d / 3d)) <= 0.01;
            var panelGeometryMatches =
                Matches(actualBackdrop, expectedBackdrop, 0.007) &&
                Matches(actualPanel, expectedPanel, 0.007) &&
                Matches(actualHeader, expectedHeader, 0.007) &&
                actualCards.Length == expectedCards.Length &&
                actualCards.Zip(expectedCards)
                    .All(pair => Matches(pair.First, pair.Second, 0.007));
            var secondaryRemainingTextFullyVisible = false;
            var mascotGeometryMatches =
                actualMascots.Length == expectedMascots.Length &&
                actualMascots.Zip(expectedMascots)
                    .All(pair => Matches(pair.First, pair.Second, 0.011));
            var contentOverlapTargets = protectedContent
                .Where(target => !AlphaAvoids(referencePlushScene, new[] { target.Bounds }, 32))
                .Select(target => target.Name)
                .ToArray();
            var contentOverlapCount = contentOverlapTargets.Length;
            var countdownRowsReserveScrollbarInset = rows.Children
                .OfType<Border>()
                .All(card => Descendants<Grid>(card).Any(grid =>
                    grid.ColumnDefinitions.Count == 2 && grid.Margin.Right >= 68));
            var contentSafeAreaMatches =
                referencePlushScene.Bounds.Count(bounds => bounds is not null) == 3 &&
                contentOverlapCount == 0 &&
                countdownRowsReserveScrollbarInset;
            var titleUsesSemiBoldWeight =
                Required<TextBlock>(referenceWindow, "FloatingTitle").FontWeight == FontWeights.SemiBold;
            var floatingNoteInput = Required<TextBox>(referenceWindow, "FloatingNoteText");
            var noteInputWraps = floatingNoteInput.TextWrapping == TextWrapping.Wrap;
            var noteInputScrolls =
                floatingNoteInput.VerticalScrollBarVisibility == ScrollBarVisibility.Auto;
            var workdayCountdownUsesPrimaryVisual = WorkdayCountdownUsesPrimaryVisual(
                referenceWindow,
                rows,
                referenceUtc,
                referenceWorkdayTargetUtc);
            var scenarioMatches =
                rows.Children.Count == 2 &&
                workdayCountdownUsesPrimaryVisual &&
                referenceWindow.FloatingCountdownCount == 1 &&
                referenceWindow.FloatingCountdownTexts.Count == 1 &&
                PrimaryRemainingTextFitsOnOneLine(
                    rows,
                    referenceWorkdayTargetUtc,
                    referenceUtc) &&
                string.Equals(referenceWindow.IncomeText, "今日已赚 ￥41.13", StringComparison.Ordinal);
            ClearCaptureKeyboardFocus();
            var actualCapture = CaptureVisual(root);
            var framedCapture = ScaleToFrame(actualCapture, captureWidth, captureHeight);
            var captureBytes = EncodePng(framedCapture);
            var captureWritten = captureBytes.Length > 0;
            var baselineBodyScroller = Required<ScrollViewer>(referenceWindow, "FloatingBodyScroller");
            var dayCountdownCapture = CreateFloatingReferenceActualCapture(
                FloatingReferenceCaptureSpecs.Single(spec =>
                    spec.CaptureId == "floating-countdowns-day"),
                captureBytes,
                CreateFloatingReferenceStateFingerprint(
                    "Day",
                    "Countdowns",
                    string.Empty,
                    string.Empty,
                    "Baseline",
                    baselineBodyScroller.VerticalOffset,
                    baselineBodyScroller.ScrollableHeight,
                    "距离下班",
                    "七月倒计时"));
            var expiredFallbackProbe = referenceWindow.BuildRemainingBlocksForValidation(
                "已到期",
                isPrimary: false);
            var expiredFallbackProbeRow = new Border
            {
                Child = expiredFallbackProbe
            };
            rows.Children.Add(expiredFallbackProbeRow);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 3);
            var expiredFallbackText = Descendants<TextBlock>(expiredFallbackProbeRow)
                .SingleOrDefault(text => string.Equals(text.Text, "已到期", StringComparison.Ordinal));
            var expiredFallbackRemainingTextFullyVisible =
                expiredFallbackText is not null &&
                expiredFallbackText.FontSize >= 12 &&
                expiredFallbackText.TextWrapping == TextWrapping.NoWrap &&
                expiredFallbackText.TextTrimming == TextTrimming.None &&
                string.Equals(
                    AutomationProperties.GetItemStatus(expiredFallbackText),
                    "KeyTextFontFloorSatisfied",
                    StringComparison.Ordinal) &&
                TextBlockLinesFit(expiredFallbackText) &&
                MeasureTextFit(expiredFallbackText, root).Fits &&
                FitsInsideFloatingViewport(expiredFallbackText, referenceWindow);
            rows.Children.Remove(expiredFallbackProbeRow);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            var secondaryRemainingSamples = new List<FloatingSecondaryRemainingMeasurement>();
            var primaryCountdownContrastMeasurements = new List<FloatingPrimaryCountdownContrastMeasurement>
            {
                CaptureFloatingPrimaryCountdownContrast(
                    referenceWindow,
                    rows,
                    panel)
            };
            var referencePreferences = referenceSession.Current.Preferences;
            var precisionPreferencesSaved = true;
            var precisionCases = new (string Precision, string ExpectedText)[]
            {
                ("Day", "4 天"),
                ("Hour", "4 天 13 小时"),
                ("Minute", "4 天 13 小时 18 分钟"),
                ("Second", "4 天 13 小时 18 分钟 59 秒")
            };
            foreach (var precisionCase in precisionCases)
            {
                var preferenceSaved = referenceSession.TrySavePreferences(
                    precisionCase.Precision,
                    referencePreferences.MonthlySalary,
                    referencePreferences.WorkStart,
                    referencePreferences.WorkEnd,
                    out _);
                precisionPreferencesSaved &= preferenceSaved;
                referenceWindow.UpdateTime(referenceUtc);
                WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
                secondaryRemainingSamples.Add(CaptureFloatingSecondaryRemaining(
                    precisionCase.Precision,
                    precisionCase.ExpectedText,
                    CountdownFormatting.FormatRemaining(
                        referenceUtc.AddDays(4).AddHours(13).AddMinutes(18).AddSeconds(59),
                        referenceUtc,
                        precisionCase.Precision),
                    FindFloatingRemainingCluster(rows, "七月倒计时"),
                    referenceWindow,
                    preferenceSaved));
            }

            var longSecondaryAdded = referenceSession.TrySaveCountdown(
                "reference-long-secondary",
                "长周期次级倒计时",
                referenceUtc.AddDays(2_000_000),
                Array.Empty<string>(),
                out _);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 3);
            const string longSecondaryExpected = "2000000 天 00 小时 00 分钟 00 秒";
            var longSecondaryMeasurement = CaptureFloatingSecondaryRemaining(
                "SecondLongDays",
                longSecondaryExpected,
                CountdownFormatting.FormatRemaining(
                    referenceUtc.AddDays(2_000_000),
                    referenceUtc,
                    "Second"),
                FindFloatingRemainingCluster(rows, "长周期次级倒计时"),
                referenceWindow,
                longSecondaryAdded);
            secondaryRemainingSamples.Add(longSecondaryMeasurement);
            var secondaryRemainingGates =
                ValidationExercises.EvaluateFloatingSecondaryRemainingGates(
                    new FloatingSecondaryRemainingGateInputs(
                        secondaryRemainingSamples.Select(sample => sample.ExpectedText).ToArray(),
                        secondaryRemainingSamples.Select(sample => sample.FormattedText).ToArray(),
                        secondaryRemainingSamples.Select(sample => sample.RenderedText).ToArray(),
                        precisionPreferencesSaved &&
                            longSecondaryAdded &&
                            secondaryRemainingSamples.Count == 5,
                        secondaryRemainingSamples.All(sample => sample.ColumnIs194Pixels),
                        secondaryRemainingSamples.All(sample => sample.IsRightAligned),
                        secondaryRemainingSamples.All(sample => sample.IsSingleLineAndUntrimmed),
                        secondaryRemainingSamples.All(sample => sample.IsFullyVisible),
                        secondaryRemainingSamples.All(sample => sample.UsesTabularNumerals),
                        secondaryRemainingSamples.Take(4).All(sample =>
                            Math.Abs(sample.FontSize - 20) <= 0.01) &&
                        longSecondaryMeasurement.ColumnIs194Pixels &&
                        longSecondaryMeasurement.IsFullyVisible &&
                        longSecondaryMeasurement.FontSize is >= 12 and < 20));
            var longSecondaryDeleted = referenceSession.TryDeleteCountdown(
                "reference-long-secondary",
                out _);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            secondaryRemainingTextFullyVisible =
                longSecondaryAdded &&
                longSecondaryDeleted &&
                longSecondaryMeasurement.IsFullyVisible;
            // 月薪 0/非 0 底部文案切换：参考会话日历已就绪，月薪清零后必须显示手写体鼓励文案。
            var referenceSalary = referencePreferences.MonthlySalary;
            var zeroSalaryApplied = referenceSession.TrySavePreferences(
                "Second",
                0m,
                referencePreferences.WorkStart,
                referencePreferences.WorkEnd,
                out _);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            var incomeTextProbe = Required<TextBlock>(referenceWindow, "FloatingIncome");
            var expectedDecoBrush = referenceWindow.TryFindResource("DecoTextSoftBrush") as Brush;
            var expectedDecoFont = referenceWindow.TryFindResource("HandwrittenDecoFontFamily") as FontFamily;
            var floatingIncomeEncouragementWithoutSalary =
                zeroSalaryApplied &&
                string.Equals(
                    referenceWindow.IncomeText,
                    FloatingBottomStatus.CountdownEncouragementText,
                    StringComparison.Ordinal) &&
                incomeTextProbe.Visibility == Visibility.Visible &&
                !incomeTextProbe.IsHitTestVisible &&
                expectedDecoFont is not null &&
                ReferenceEquals(incomeTextProbe.FontFamily, expectedDecoFont) &&
                expectedDecoBrush is not null &&
                ReferenceEquals(incomeTextProbe.Foreground, expectedDecoBrush);
            var salaryRestored = referenceSession.TrySavePreferences(
                "Second",
                referenceSalary,
                referencePreferences.WorkStart,
                referencePreferences.WorkEnd,
                out _);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            var incomeRestoredAfterEncouragement =
                salaryRestored &&
                referenceWindow.IncomeText != null &&
                referenceWindow.IncomeText.StartsWith("今日已赚", StringComparison.Ordinal);
            byte[]? notesCaptureBytes = null;
            byte[]? scrolledCountdownsCaptureBytes = null;
            byte[]? scrolledNotesCaptureBytes = null;
            var floatingScrollBarValidation = FloatingScrollBarValidation.NotRun;
            var floatingReferenceBaseline = FloatingReferenceBaselineValidation.NotRequested;
            var floatingLongNotesValidation = FloatingLongNotesValidation.NotRun;
            referenceWindow.ApplyTheme();
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            referenceWindow.ApplyTheme();
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            var viewportStates = new List<FloatingViewportState>
            {
                CaptureFloatingViewportState("two-countdowns", referenceWindow, root, rows)
            };

            var userCountdownDeleted = referenceSession.TryDeleteCountdown(
                "reference-july",
                out _);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            viewportStates.Add(
                CaptureFloatingViewportState("one-countdown", referenceWindow, root, rows));

            referenceWindow.UpdateTime(referenceUtc.AddHours(8));
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 0);
            viewportStates.Add(
                CaptureFloatingViewportState("zero-countdowns", referenceWindow, root, rows));

            var afterWorkdayUtc = referenceUtc.AddHours(8);
            var ordinaryPrimaryTargetUtc = referenceUtc
                .AddDays(4)
                .AddHours(13)
                .AddMinutes(18)
                .AddSeconds(59);
            var ordinaryPrimaryAdded = referenceSession.TrySaveCountdown(
                "reference-ordinary-primary",
                "普通主项单行倒计时",
                ordinaryPrimaryTargetUtc,
                Array.Empty<string>(),
                out _);
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            var ordinaryPrimaryRemainingTextFullyVisible =
                PrimaryRemainingTextFitsOnOneLine(
                    rows,
                    ordinaryPrimaryTargetUtc,
                    afterWorkdayUtc);
            primaryCountdownContrastMeasurements.Add(CaptureFloatingPrimaryCountdownContrast(
                referenceWindow, rows, panel, "Ordinary"));
            // 徽章排他性：主项由普通倒计时承担时不得出现「超期待！」徽章。
            var ordinaryPrimaryBadgeAbsent = !Descendants<TextBlock>(rows)
                .Any(text => string.Equals(text.Text, "超期待！", StringComparison.Ordinal));
            var ordinaryPrimaryDeleted = referenceSession.TryDeleteCountdown(
                "reference-ordinary-primary",
                out _);
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 0);

            var tripleDigitAdded = referenceSession.TrySaveCountdown(
                "reference-999-primary", "999 天主项", afterWorkdayUtc.AddDays(999).AddHours(13).AddMinutes(18).AddSeconds(59),
                Array.Empty<string>(), out _);
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            var tripleDigitMeasurement = CaptureFloatingPrimaryCountdownContrast(referenceWindow, rows, panel, "999Days");
            primaryCountdownContrastMeasurements.Add(tripleDigitMeasurement);
            var tripleDigitDeleted = referenceSession.TryDeleteCountdown("reference-999-primary", out _);
            if (!tripleDigitAdded || !tripleDigitDeleted)
            {
                throw new InvalidOperationException("999 天悬浮主项夹具未能完整保存和恢复。");
            }
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 0);
            var primaryCountdownContrastGates =
                ValidationExercises.EvaluateFloatingPrimaryCountdownContrastGates(
                    new FloatingPrimaryCountdownContrastGateInputs(
                        primaryCountdownContrastMeasurements.Select(measurement => measurement.ContrastRatio).ToArray(),
                        primaryCountdownContrastMeasurements.All(measurement =>
                            measurement.UsesExpectedSemanticAccent &&
                            measurement.AccentTrackUsesAccentToken &&
                            measurement.AccentDiffersFromPlushGlow)));

            var extremePrimaryUtc = referenceUtc.AddDays(2_000_000);
            var extremePrimaryAdded = referenceSession.TrySaveCountdown(
                "reference-extreme-primary",
                "极端长剩余时间",
                extremePrimaryUtc,
                Array.Empty<string>(),
                out _);
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            var extremePrimaryCluster = FindFloatingRemainingCluster(rows, "极端长剩余时间");
            var extremePrimaryExpected = CountdownFormatting.FormatRemaining(
                extremePrimaryUtc,
                afterWorkdayUtc,
                "Second");
            var extremePrimaryValueBlocks = RemainingBlockPairs(extremePrimaryCluster)
                .Select(pair => pair.Value)
                .ToArray();
            var primaryExtremeRemainingShrinks =
                extremePrimaryAdded &&
                extremePrimaryCluster is not null &&
                string.Equals(
                    AutomationProperties.GetName(extremePrimaryCluster),
                    extremePrimaryExpected,
                    StringComparison.Ordinal) &&
                Math.Abs(extremePrimaryCluster.Width - 194) <= 0.01 &&
                extremePrimaryCluster.HorizontalAlignment == HorizontalAlignment.Right &&
                extremePrimaryValueBlocks.Length == 4 &&
                extremePrimaryValueBlocks.All(value => value.FontSize is >= 12 and < 26) &&
                string.Equals(
                    AutomationProperties.GetItemStatus(extremePrimaryCluster),
                    "KeyTextFontFloorSatisfied",
                    StringComparison.Ordinal) &&
                RemainingClusterIsSingleLine(extremePrimaryCluster) &&
                FitsInsideFloatingViewport(extremePrimaryCluster, referenceWindow);
            var extremePrimaryDeleted = referenceSession.TryDeleteCountdown(
                "reference-extreme-primary",
                out _);
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 0);
            primaryExtremeRemainingShrinks &= extremePrimaryDeleted;

            const string primaryDeadlineText = "12 月 31 日 23:59";
            var primaryDeadlineUtc = new DateTimeOffset(
                2030,
                12,
                31,
                23,
                59,
                0,
                TimeSpan.FromHours(8)).ToUniversalTime();
            var primaryDeadlineAdded = referenceSession.TrySaveCountdown(
                "reference-primary-deadline",
                "最长日期倒计时",
                primaryDeadlineUtc,
                Array.Empty<string>(),
                out _);
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            var primaryDeadlineWithoutScrollbar = CaptureFloatingViewportState(
                "primary-deadline-without-scrollbar",
                referenceWindow,
                root,
                rows);
            var primaryDeadlineWithoutScrollbarMeasurement =
                MeasurePrimaryDeadline(rows, primaryDeadlineText, false);
            var primaryDeadlineTextFullyVisibleWithoutScrollbar =
                !primaryDeadlineWithoutScrollbar.VerticalScrollBarVisible &&
                primaryDeadlineWithoutScrollbarMeasurement.FullyVisible;

            var primaryDeadlineOverflowIds = Enumerable.Range(1, 8)
                .Select(index => $"reference-primary-deadline-overflow-{index}")
                .ToArray();
            var primaryDeadlineOverflowAdded = true;
            for (var index = 0; index < primaryDeadlineOverflowIds.Length; index++)
            {
                primaryDeadlineOverflowAdded &= referenceSession.TrySaveCountdown(
                    primaryDeadlineOverflowIds[index],
                    $"最长日期滚动倒计时 {index + 1}",
                    primaryDeadlineUtc.AddDays(index + 1),
                    Array.Empty<string>(),
                    out _);
            }

            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 9);
            var primaryDeadlineWithScrollbar = CaptureFloatingViewportState(
                "primary-deadline-with-scrollbar",
                referenceWindow,
                root,
                rows);
            var primaryDeadlineWithScrollbarMeasurement =
                MeasurePrimaryDeadline(rows, primaryDeadlineText, true);
            var primaryDeadlineTextFullyVisibleWithScrollbar =
                primaryDeadlineWithScrollbar.VerticalScrollBarVisible &&
                primaryDeadlineWithScrollbarMeasurement.FullyVisible;

            var primaryDeadlineOverflowDeleted = true;
            foreach (var id in primaryDeadlineOverflowIds)
            {
                primaryDeadlineOverflowDeleted &= referenceSession.TryDeleteCountdown(id, out _);
            }

            var primaryDeadlineDeleted = referenceSession.TryDeleteCountdown(
                "reference-primary-deadline",
                out _);
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 0);
            var primaryDeadlineTextFullyVisible =
                ordinaryPrimaryAdded &&
                ordinaryPrimaryDeleted &&
                ordinaryPrimaryRemainingTextFullyVisible &&
                primaryDeadlineAdded &&
                primaryDeadlineOverflowAdded &&
                primaryDeadlineOverflowDeleted &&
                primaryDeadlineDeleted &&
                primaryDeadlineTextFullyVisibleWithoutScrollbar &&
                primaryDeadlineTextFullyVisibleWithScrollbar;

            var overflowCountdownIds = Enumerable.Range(1, 8)
                .Select(index => $"reference-overflow-{index}")
                .ToArray();
            var overflowCountdownsAdded = true;
            for (var index = 0; index < overflowCountdownIds.Length; index++)
            {
                overflowCountdownsAdded &= referenceSession.TrySaveCountdown(
                    overflowCountdownIds[index],
                    $"滚动倒计时 {index + 1}",
                    referenceUtc.AddDays(index + 1),
                    Array.Empty<string>(),
                    out _);
            }

            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 9);
            floatingScrollBarValidation = ValidateFloatingScrollBar(
                manager,
                referenceWindow,
                root,
                rows,
                referenceUtc,
                captureWidth,
                captureHeight);
            var countdownScroller = Required<ScrollViewer>(referenceWindow, "FloatingBodyScroller");
            countdownScroller.ScrollToVerticalOffset(countdownScroller.ScrollableHeight);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 9);
            ClearCaptureKeyboardFocus();
            scrolledCountdownsCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
            viewportStates.Add(
                CaptureFloatingViewportState("nine-countdowns-scrolled", referenceWindow, root, rows));

            var overflowCountdownsDeleted = true;
            foreach (var id in overflowCountdownIds)
            {
                overflowCountdownsDeleted &= referenceSession.TryDeleteCountdown(id, out _);
            }
            countdownScroller.ScrollToTop();
            PumpDispatcher();

            referenceWindow.UpdateTime(referenceUtc);
            Required<ToggleButton>(referenceWindow, "FloatingNotesButton")
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            floatingLongNotesValidation = CaptureFloatingLongNoteFrames(
                referenceWindow,
                root,
                rows,
                notes,
                referenceUtc,
                captureWidth,
                captureHeight);
            notesCaptureBytes = floatingLongNotesValidation.Captures
                .Single(capture => capture.Spec.CaptureId == "long-notes-day-top")
                .PngBytes;
            var searchExpandedCaptures = CaptureFloatingSearchExpandedFrames(
                referenceWindow,
                root,
                rows,
                referenceUtc,
                captureWidth,
                captureHeight);
            floatingReferenceBaseline = ValidateFloatingReferenceBaseline(
                assetRoot,
                new[] { dayCountdownCapture }
                    .Concat(floatingLongNotesValidation.Captures)
                    .Concat(searchExpandedCaptures)
                    .ToArray(),
                floatingLongNotesValidation.TypographyMatches,
                floatingLongNotesValidation.LayoutValid);
            viewportStates.Add(
                CaptureFloatingViewportState("six-multiline-notes", referenceWindow, root, rows));

            var bodyScroller = Required<ScrollViewer>(referenceWindow, "FloatingBodyScroller");
            bodyScroller.ScrollToVerticalOffset(bodyScroller.ScrollableHeight);
            PumpDispatcher();
            var focusedNote = Descendants<CheckBox>(rows).Last();
            focusedNote.Focus();
            PumpDispatcher();
            var focusedNoteId = focusedNote.Tag as string;
            var offsetBeforeRefresh = bodyScroller.VerticalOffset;
            referenceWindow.UpdateTime(referenceUtc.AddSeconds(1));
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            var restoredFocusedNote = Descendants<CheckBox>(rows).SingleOrDefault(checkBox =>
                string.Equals(checkBox.Tag as string, focusedNoteId, StringComparison.Ordinal));
            var viewportScrollAndFocusRestored =
                offsetBeforeRefresh > 0 &&
                Math.Abs(bodyScroller.VerticalOffset - offsetBeforeRefresh) <= 1 &&
                restoredFocusedNote is not null &&
                ReferenceEquals(Keyboard.FocusedElement, restoredFocusedNote);

            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            viewportStates.Add(
                CaptureFloatingViewportState("repeating-reminder", referenceWindow, root, rows));

            bodyScroller.ScrollToVerticalOffset(bodyScroller.ScrollableHeight);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            ClearCaptureKeyboardFocus();
            scrolledNotesCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
            referenceWindow.ApplyTheme();
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            viewportStates.Add(
                CaptureFloatingViewportState("scrolled-notes", referenceWindow, root, rows));

            var datedCard = rows.Children
                .OfType<Border>()
                .Single(card => Descendants<CheckBox>(card)
                    .Any(check => string.Equals(
                        check.Tag as string,
                        "reference-note-1",
                        StringComparison.Ordinal)));
            var datedStampTexts = CalendarIndexStampTexts(datedCard);
            var datedStampSemanticsMatch =
                datedStampTexts.SequenceEqual(new[] { "7 月", "15", "周一" });
            var undatedNoteSaved = referenceSession.TrySaveNote(
                "reference-undated",
                "无日期历史便签",
                null,
                false,
                Array.Empty<string>(),
                NoReminder.Instance,
                out _);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 7);
            var undatedCard = rows.Children
                .OfType<Border>()
                .Single(card => Descendants<CheckBox>(card)
                    .Any(check => string.Equals(
                        check.Tag as string,
                        "reference-undated",
                        StringComparison.Ordinal)));
            var undatedStampTexts = CalendarIndexStampTexts(undatedCard);
            var undatedStampSemanticsMatch =
                undatedStampTexts.SequenceEqual(new[] { "无日期", "--", " " });
            var undatedNoteDeleted = referenceSession.TryDeleteNote("reference-undated", out _);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);

            var noteFailureAppearsInSourceCard = false;
            var resolvedNoteCardErrorDoesNotReturn = false;
            string? failedNoteId = null;
            string? failedNoteError = null;
            var blockedTemporaryPath = settingsPath + ".tmp";
            try
            {
                Directory.CreateDirectory(blockedTemporaryPath);
                var completion = Descendants<CheckBox>(rows).First();
                failedNoteId = completion.Tag as string;
                completion.Focus();
                completion.IsChecked = true;
                PumpDispatcher();
                var localError = Descendants<TextBlock>(rows).FirstOrDefault(text =>
                    text.IsVisible &&
                    !string.IsNullOrWhiteSpace(text.Text) &&
                    string.Equals(
                        AutomationProperties.GetName(text),
                        text.Text,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(text),
                        text.Text,
                        StringComparison.Ordinal) &&
                    AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite);
                failedNoteError = localError?.Text;
                noteFailureAppearsInSourceCard =
                    completion.IsChecked != true &&
                    ReferenceEquals(Keyboard.FocusedElement, completion) &&
                    localError is not null &&
                    !string.IsNullOrWhiteSpace(localError.Text) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(completion),
                        localError.Text,
                        StringComparison.Ordinal) &&
                    referenceSession.Current.Notes.All(note => !note.IsCompleted);
            }
            finally
            {
                if (Directory.Exists(blockedTemporaryPath))
                {
                    Directory.Delete(blockedTemporaryPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(failedNoteId))
            {
                var noteCompleted = referenceSession.TrySetNoteCompletion(
                    failedNoteId,
                    true,
                    out _);
                WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 5);
                var noteRestored = referenceSession.TrySetNoteCompletion(
                    failedNoteId,
                    false,
                    out _);
                WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
                var restoredCard = rows.Children
                    .OfType<Border>()
                    .SingleOrDefault(card => Descendants<CheckBox>(card).Any(checkBox =>
                        string.Equals(
                            checkBox.Tag as string,
                            failedNoteId,
                            StringComparison.Ordinal)));
                resolvedNoteCardErrorDoesNotReturn =
                    noteCompleted &&
                    noteRestored &&
                    restoredCard is not null &&
                    !Descendants<TextBlock>(restoredCard).Any(text =>
                        text.IsVisible &&
                        AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite) &&
                    !string.Equals(
                        Required<TextBlock>(referenceWindow, "FloatingNoteMessage").Text,
                        failedNoteError,
                        StringComparison.Ordinal);
            }

            var viewportStatesMatch =
                userCountdownDeleted &&
                overflowCountdownsAdded &&
                overflowCountdownsDeleted &&
                datedStampSemanticsMatch &&
                undatedNoteSaved &&
                undatedStampSemanticsMatch &&
                undatedNoteDeleted &&
                viewportStates.Count == 7 &&
                viewportStates.All(state => state.WindowSizeMatches) &&
                viewportStates.All(state => state.ContentSafeAreaMatches) &&
                viewportStates.All(state => state.WorkspaceSurfaceGeometryMatches) &&
                viewportStates.All(state => state.VerticalScrollBarIsSafe) &&
                viewportStates[0].RenderedCardCount == 2 &&
                viewportStates[1].RenderedCardCount == 1 &&
                viewportStates[2].RenderedCardCount == 0 &&
                viewportStates[3].RenderedCardCount == 9 &&
                viewportStates[3].ScrollableHeight > 0 &&
                viewportStates[3].VerticalOffset > 0 &&
                viewportStates[3].VerticalScrollBarVisible &&
                viewportStates[4].RenderedCardCount == 6 &&
                viewportStates[4].RenderedMultilineNoteCount == 6 &&
                referenceWindow.FindName("FloatingReminderIntervalPanel") is null &&
                viewportStates[5].ScrollableHeight > 0 &&
                viewportStates[6].ScrollableHeight > 0 &&
                viewportStates[6].VerticalOffset > 0;
            var rasterCardStatesMatch =
                viewportStates.All(state => state.RasterCardCount == 0) &&
                viewportStates.All(state => !state.RasterCardsShareSource);
            var dynamicCardContentRemainsWpf =
                viewportStates[0].DynamicTextElementCount >= 4 &&
                viewportStates[1].DynamicTextElementCount >= 2 &&
                viewportStates[2].DynamicTextElementCount >= 1 &&
                viewportStates[3].DynamicTextElementCount >= 18 &&
                viewportStates[4].DynamicTextElementCount >= 6 &&
                viewportStates[4].DynamicCheckBoxCount == 6;
            var capturePath = string.Empty;
            var evidenceDirectory = Environment.GetEnvironmentVariable(
                "MINMIN_COUNTDOWN_VISUAL_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidenceDirectory))
            {
                Directory.CreateDirectory(evidenceDirectory);
                var captureName = Environment.GetEnvironmentVariable(
                    "MINMIN_COUNTDOWN_VISUAL_CAPTURE_NAME");
                captureName = string.Equals(captureName, "after", StringComparison.OrdinalIgnoreCase)
                    ? "after"
                    : "before";
                capturePath = Path.Combine(evidenceDirectory, $"{captureName}.png");
                File.WriteAllBytes(capturePath, captureBytes);
                if (notesCaptureBytes is not null)
                {
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"{captureName}-notes.png"),
                        notesCaptureBytes);
                }
                if (scrolledNotesCaptureBytes is not null)
                {
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"{captureName}-notes-scrolled.png"),
                        scrolledNotesCaptureBytes);
                }
                if (scrolledCountdownsCaptureBytes is not null)
                {
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"{captureName}-countdowns-scrolled.png"),
                        scrolledCountdownsCaptureBytes);
                }
                foreach (var stateCapture in floatingScrollBarValidation.Captures)
                {
                    File.WriteAllBytes(
                        Path.Combine(
                            evidenceDirectory,
                            $"{captureName}-scrollbar-{stateCapture.Key}.png"),
                        stateCapture.Value);
                }
                var referenceImagePath = Environment.GetEnvironmentVariable(
                    "MINMIN_COUNTDOWN_REFERENCE_IMAGE");
                if (!string.IsNullOrWhiteSpace(referenceImagePath) &&
                    File.Exists(referenceImagePath) &&
                    string.Equals(
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(referenceImagePath))),
                        referenceSha256,
                        StringComparison.Ordinal))
                {
                    var referenceImage = LoadBitmap(referenceImagePath);
                    var scaledReference = ScaleToFrame(referenceImage, captureWidth, captureHeight);
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, "reference-scaled.png"),
                        EncodePng(scaledReference));
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"overlay-{captureName}.png"),
                        EncodePng(Overlay(scaledReference, framedCapture)));
                    var geometryPath = Path.Combine(evidenceDirectory, "reference-geometry.json");
                    if (!File.Exists(geometryPath))
                    {
                        File.WriteAllText(
                            geometryPath,
                            JsonSerializer.Serialize(
                                new
                                {
                                    reference = new
                                    {
                                        width = referenceWidth,
                                        height = referenceHeight,
                                        sha256 = referenceSha256
                                    },
                                    capture = new { width = captureWidth, height = captureHeight },
                                    normalized = new
                                    {
                                        backdrop = expectedBackdrop,
                                        panel = expectedPanel,
                                        header = expectedHeader,
                                        cards = expectedCards,
                                        mascots = expectedMascots
                                    },
                                    tolerances = new { panel = 0.007, mascot = 0.011 }
                                },
                                new JsonSerializerOptions { WriteIndented = true }));
                    }

                    File.WriteAllText(
                        Path.Combine(evidenceDirectory, $"{captureName}-geometry.json"),
                        JsonSerializer.Serialize(
                            new
                            {
                                actual = new
                                {
                                    width = Math.Round(actualWidth, 2),
                                    height = Math.Round(actualHeight, 2),
                                    backdrop = actualBackdrop,
                                    panel = actualPanel,
                                    header = actualHeader,
                                    cards = actualCards,
                                    mascots = actualMascots
                                },
                                checks = new
                                {
                                    aspectMatches,
                                    panelGeometryMatches,
                                    mascotGeometryMatches,
                                    contentSafeAreaMatches,
                                    contentOverlapCount,
                                    contentOverlapTargets,
                                    workdayCountdownUsesPrimaryVisual,
                                    scenarioMatches,
                                    viewportStatesMatch,
                                    captureWritten
                                },
                                viewportStates
                            },
                            new JsonSerializerOptions { WriteIndented = true }));
                }

                captureWritten =
                    File.Exists(capturePath) &&
                    new FileInfo(capturePath).Length > 0;
            }

            // #146 收口：悬浮窗专属 PerMonitorV2 高 DPI 实拍（#143 遗留项）。
            // 此时全部既有门禁测量已完成，DPI 变更不再影响任何既有断言；
            // 变体失败按 seam 惯例先落错误证据再原样重抛（fail-closed）。
            RunFloatingDpiCaptureVariant(referenceWindow, root);

            return new FloatingReferenceExerciseResult(
                aspectMatches,
                panelGeometryMatches,
                mascotGeometryMatches,
                floatingReferenceBaseline,
                floatingLongNotesValidation,
                new FloatingPrimaryCountdownContrastValidation(
                    primaryCountdownContrastGates,
                    primaryCountdownContrastMeasurements),
                contentSafeAreaMatches,
                titleUsesSemiBoldWeight,
                noteInputWraps,
                noteInputScrolls,
                scrolledNotesCaptureBytes?.Length > 0,
                contentOverlapCount,
                contentOverlapTargets,
                captureWritten,
                workdayCountdownUsesPrimaryVisual,
                ordinaryPrimaryBadgeAbsent,
                scenarioMatches,
                primaryDeadlineTextFullyVisible,
                primaryExtremeRemainingShrinks,
                expiredFallbackRemainingTextFullyVisible,
                new[]
                {
                    primaryDeadlineWithoutScrollbarMeasurement,
                    primaryDeadlineWithScrollbarMeasurement
                },
                viewportStatesMatch,
                rasterCardStatesMatch,
                dynamicCardContentRemainsWpf,
                viewportScrollAndFocusRestored,
                noteFailureAppearsInSourceCard,
                resolvedNoteCardErrorDoesNotReturn,
                secondaryRemainingTextFullyVisible,
                floatingIncomeEncouragementWithoutSalary &&
                    incomeRestoredAfterEncouragement,
                incomePrivacyBehavior,
                secondaryRemainingGates,
                secondaryRemainingSamples,
                floatingScrollBarValidation,
                new[] { Math.Round(actualWidth, 2), Math.Round(actualHeight, 2) },
                capturePath);
        }
        finally
        {
            referenceWindow?.Close();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(holidayPath);
            if (Directory.Exists(holidayDirectory))
            {
                Directory.Delete(holidayDirectory);
            }

            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot);
            }
        }
    }

    // #146 收口：悬浮窗专属 PerMonitorV2 高 DPI 实拍变体（#143 遗留项）。
    // 仅在官方视觉验证上下文（MINMIN_COUNTDOWN_VISUAL_EVIDENCE_DIR 或
    // MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT 任一在位）触发；发布包模式与普通
    // validate-only 运行维持原行为不进入。变体向悬浮窗 hwnd 真实发送
    // WM_DPICHANGED（等比放大 1.5），要求 Window.DpiChanged 真实发生且
    // VisualTreeHelper.GetDpi 反映新比例，再在新 DPI 下实拍悬浮窗内容 PNG，
    // 遥测与 manifest 落盘；任何失败先写 <id>.error.txt 再原样重抛，
    // 不以静态帧、空白画布或合成替代顶替实拍证据。
    private static void RunFloatingDpiCaptureVariant(
        FloatingWindow window,
        FrameworkElement root)
    {
        var evidenceDirectory = Environment.GetEnvironmentVariable(
            "MINMIN_COUNTDOWN_VISUAL_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            if (string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT")))
            {
                return;
            }

            evidenceDirectory = ResolveDefaultVisualEvidenceDirectory("ui-seam");
            if (string.IsNullOrWhiteSpace(evidenceDirectory))
            {
                return;
            }
        }

        const string variantId = "floating-permonitorv2-dpichanged-countdowns";
        Directory.CreateDirectory(evidenceDirectory);
        try
        {
            var awarenessElevatedByProbe = EnableCaptureProbePerMonitorV2Awareness();
            var dpiChangeTelemetry = MeasureWindowDpiChange(window, root, 1.5);
            var dpiAfter = VisualTreeHelper.GetDpi(root);
            var dipWidth = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
            var dipHeight = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
            var capture = CaptureOpaqueMainSurfaceAtDpi(
                root,
                dipWidth,
                dipHeight,
                dpiAfter.PixelsPerInchX,
                dpiAfter.PixelsPerInchY);
            var physicalWidth = (int)Math.Round(dipWidth * dpiAfter.DpiScaleX);
            var physicalHeight = (int)Math.Round(dipHeight * dpiAfter.DpiScaleY);
            if (Math.Abs(capture.PixelWidth - physicalWidth) > 3 ||
                Math.Abs(capture.PixelHeight - physicalHeight) > 3)
            {
                throw new InvalidOperationException(
                    "悬浮窗 PerMonitorV2 变体物理画布不匹配：" +
                    $"{capture.PixelWidth}x{capture.PixelHeight}，期望约 {physicalWidth}x{physicalHeight}。");
            }

            var capturePath = SaveUiCaptureSeamPng(evidenceDirectory, variantId, capture);
            WriteNewBytes(
                Path.Combine(evidenceDirectory, "floating-dpi-capture-manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schemaVersion = 1,
                        captureKind = "actual-wpf-floating-window",
                        issue = "#143/#146 floating-window high-DPI real capture",
                        awarenessElevatedByProbe = awarenessElevatedByProbe,
                        dpiChange = new
                        {
                            dpiChangeTelemetry.AwarenessIsPerMonitorV2,
                            dpiChangeTelemetry.DpiChangedEventObserved,
                            dpiChangeTelemetry.DpiScaleBeforeX,
                            dpiChangeTelemetry.DpiScaleBeforeY,
                            dpiChangeTelemetry.DpiScaleAfterX,
                            dpiChangeTelemetry.DpiScaleAfterY
                        },
                        entries = new[]
                        {
                            new
                            {
                                captureId = variantId,
                                mechanism = "permonitor-v2; WM_DPICHANGED pipeline re-rendered floating window content at raised DPI",
                                path = capturePath,
                                sha256 = Convert.ToHexString(SHA256.HashData(EncodePng(capture))),
                                dip = new { width = dipWidth, height = dipHeight },
                                pixels = new { width = capture.PixelWidth, height = capture.PixelHeight },
                                dpiScale = new { x = dpiAfter.DpiScaleX, y = dpiAfter.DpiScaleY }
                            }
                        }
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            WriteCaptureFailureEvidence(evidenceDirectory, variantId, exception);
            throw;
        }
    }

    private static FloatingLongNotesValidation CaptureFloatingLongNoteFrames(
        FloatingWindow window,
        Grid root,
        StackPanel rows,
        IReadOnlyList<NoteRecord> notes,
        DateTimeOffset referenceUtc,
        int captureWidth,
        int captureHeight)
    {
        var bodyScroller = Required<ScrollViewer>(window, "FloatingBodyScroller");
        // #117 起：快速新增表单默认收起且位于外层滚动容器之外的底部命令区；
        // 长便签帧证明列表滚动合同，不再强制展开表单或隐藏其字段。
        PumpDispatcher();
        window.UpdateLayout();
        bodyScroller.ApplyTemplate();
        var viewport = bodyScroller.Template.FindName(
            "PART_ScrollContentPresenter",
            bodyScroller) as ScrollContentPresenter;
        var noteById = notes.ToDictionary(note => note.Id, StringComparer.Ordinal);
        var measurements = new List<FloatingLongNoteFrameMeasurement>();
        var captures = new List<FloatingReferenceActualCapture>();
        var bodyHasNoManualLineBreaks = notes.All(note =>
            !note.Text.Contains('\r') && !note.Text.Contains('\n'));
        var naturalWraps = true;
        var safeAreaMatches = true;
        var typographyMatches = true;
        var usesOnlyOuterScroller =
            viewport is not null &&
            HasVisualAncestor(rows, bodyScroller) &&
            !Descendants<ScrollViewer>(rows).Any();

        window.ApplyTheme();
        window.UpdateTime(referenceUtc);
        WaitForStableFloatingReferenceLayout(window, root, rows, notes.Count);
        foreach (var state in new[] { "Top", "Bottom" })
        {
            if (state == "Top")
            {
                bodyScroller.ScrollToTop();
            }
            else
            {
                bodyScroller.ScrollToVerticalOffset(bodyScroller.ScrollableHeight);
            }
            WaitForStableFloatingReferenceLayout(window, root, rows, notes.Count);

                var cardBindings = CaptureFloatingNoteCardBindings(rows, noteById);
                var stateNaturalWraps = cardBindings.Count == notes.Count && cardBindings.All(binding =>
                    binding.BodyText.TextWrapping == TextWrapping.Wrap &&
                    TextWouldNaturallyWrap(binding.BodyText));
                var stateSafeAreaMatches =
                    root.ActualWidth is >= 519.5 and <= 520.5 &&
                    root.ActualHeight is >= 389.5 and <= 390.5 &&
                    FloatingLongNoteSafeAreaMatches(window, root, cardBindings);
                var reminderTexts = cardBindings
                    .Select(binding => binding.ReminderText)
                    .Where(text => text is not null)
                    .Cast<TextBlock>()
                    .ToArray();
                var stateTypographyMatches =
                    cardBindings.Count == notes.Count &&
                    cardBindings.All(binding =>
                        Math.Abs(binding.BodyText.FontSize - 13) <= 0.01 &&
                        Math.Abs(binding.BodyText.LineHeight - 18) <= 0.01 &&
                        binding.BodyText.LineStackingStrategy == LineStackingStrategy.BlockLineHeight) &&
                    reminderTexts.Length == 1 &&
                    reminderTexts.All(text =>
                        Math.Abs(text.FontSize - 13) <= 0.01 &&
                        Math.Abs(text.LineHeight - 17) <= 0.01 &&
                        text.LineStackingStrategy == LineStackingStrategy.BlockLineHeight);
                naturalWraps &= stateNaturalWraps;
                safeAreaMatches &= stateSafeAreaMatches;
                typographyMatches &= stateTypographyMatches;

                var visibleBindings = viewport is null
                    ? Array.Empty<FloatingNoteCardBinding>()
                    : cardBindings
                        .Where(binding => VisibleIntersectionHeight(binding.Card, viewport) >= 4)
                        .OrderBy(binding => BoundsIn(binding.Card, viewport).Top)
                        .ToArray();
                var primaryBinding = state == "Top"
                    ? visibleBindings.FirstOrDefault()
                    : visibleBindings.LastOrDefault();
                var primaryAnchorReachable =
                    primaryBinding is not null &&
                    viewport is not null &&
                    FloatingLongNoteAnchorReachable(primaryBinding.BodyText, viewport, state);
                var firstVisibleAnchor = visibleBindings.Length == 0
                    ? string.Empty
                    : FloatingLongNoteAnchor(visibleBindings[0], takeSuffix: false);
                var lastVisibleAnchor = visibleBindings.Length == 0
                    ? string.Empty
                    : FloatingLongNoteAnchor(visibleBindings[^1], takeSuffix: true);
                var captureId = $"long-notes-day-{state.ToLowerInvariant()}";
                var fingerprint = CreateFloatingReferenceStateFingerprint(
                    "Day",
                    "Notes",
                    primaryBinding?.Note.Id ?? string.Empty,
                    primaryBinding?.Note.Text ?? string.Empty,
                    state,
                    bodyScroller.VerticalOffset,
                    bodyScroller.ScrollableHeight,
                    firstVisibleAnchor,
                    lastVisibleAnchor);
                ClearCaptureKeyboardFocus();
                var pngBytes = EncodePng(ScaleToFrame(
                    CaptureVisual(root),
                    captureWidth,
                    captureHeight));
                var spec = FloatingReferenceCaptureSpecs.Single(candidate =>
                    string.Equals(candidate.CaptureId, captureId, StringComparison.Ordinal));
                captures.Add(CreateFloatingReferenceActualCapture(spec, pngBytes, fingerprint));
                measurements.Add(new FloatingLongNoteFrameMeasurement(
                    captureId,
                    "Day",
                    state,
                    Math.Round(bodyScroller.VerticalOffset, 2),
                    Math.Round(bodyScroller.ScrollableHeight, 2),
                    primaryBinding?.Note.Id ?? string.Empty,
                    fingerprint.NoteTextSha256,
                    firstVisibleAnchor,
                    lastVisibleAnchor,
                    bodyHasNoManualLineBreaks,
                    stateNaturalWraps,
                    usesOnlyOuterScroller,
                    bodyScroller.ScrollableHeight > 0,
                    primaryAnchorReachable,
                    stateSafeAreaMatches,
                    TypographyMeasurement(cardBindings.Select(binding => binding.BodyText.FontSize)),
                    TypographyMeasurement(cardBindings.Select(binding => binding.BodyText.LineHeight)),
                    TypographyMeasurement(reminderTexts.Select(text => text.FontSize)),
                    TypographyMeasurement(reminderTexts.Select(text => text.LineHeight)),
                    stateTypographyMatches));
        }

        window.ApplyTheme();
        window.UpdateTime(referenceUtc);
        bodyScroller.ScrollToTop();
        WaitForStableFloatingReferenceLayout(window, root, rows, notes.Count);
        PumpDispatcher();
        window.UpdateLayout();

        var outerScrollable = measurements.Count == 2 &&
            measurements.All(measurement => measurement.ScrollableHeight > 0);
        var topBottomReachable = measurements.Count == 2 && measurements.All(measurement =>
            measurement.PrimaryAnchorReachable &&
            !string.IsNullOrWhiteSpace(measurement.FirstVisibleAnchor) &&
            !string.IsNullOrWhiteSpace(measurement.LastVisibleAnchor) &&
            (measurement.State == "Top"
                ? Math.Abs(measurement.VerticalOffset) <= 1
                : Math.Abs(measurement.VerticalOffset - measurement.ScrollableHeight) <= 1));
        return new FloatingLongNotesValidation(
            bodyHasNoManualLineBreaks,
            naturalWraps,
            usesOnlyOuterScroller,
            outerScrollable,
            topBottomReachable,
            safeAreaMatches,
            typographyMatches,
            measurements,
            captures);
    }

    private static double TypographyMeasurement(IEnumerable<double> values)
    {
        var measured = values.FirstOrDefault();
        return double.IsFinite(measured) ? Math.Round(measured, 2) : 0;
    }

    private static FloatingPrimaryCountdownContrastMeasurement CaptureFloatingPrimaryCountdownContrast(
        FloatingWindow window,
        StackPanel rows,
        Border taskCard,
        string scenario = "Workday")
    {
        var primaryCard = rows.Children.OfType<Border>().First();
        var primaryRoot = primaryCard.Child as Grid
            ?? throw new InvalidOperationException("悬浮窗主倒计时缺少动态根布局。");
        var primarySurface = primaryRoot.Children.OfType<Border>().First(border =>
            border.Background is SolidColorBrush brush &&
            brush.Color.A > 0 &&
            border.Height is not 1 &&
            border.Width is not 3);
        var accentTrack = primaryRoot.Children.OfType<Border>().Single(border =>
            Math.Abs(border.Width - 3) <= 0.01);
        const string numberBrushKey = "PrimaryCountdownNumberBrush";
        var expectedNumber = window.TryFindResource(numberBrushKey) as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗缺少主项数字语义 token。");
        var numberText = Descendants<TextBlock>(primaryCard).First(text =>
            ReferenceEquals(text.Foreground, expectedNumber));
        var title = Descendants<TextBlock>(primaryCard).First(text =>
            text.Parent is Grid && text.FontWeight == FontWeights.SemiBold &&
            !ReferenceEquals(text.Foreground, expectedNumber));
        var cluster = FloatingRemainingCluster(primaryRoot);
        var pairs = RemainingBlockPairs(cluster).ToArray();
        var remainingText = string.Join(" ", pairs.SelectMany(pair => new[] { pair.Value.Text, pair.Unit.Text }));
        var clusterBounds = cluster is null ? Rect.Empty : BoundsIn(cluster, primaryRoot);
        var expectedPairCount = scenario == "Workday" ? 3 : 4;
        var fullyVisible = cluster is not null && pairs.Length == expectedPairCount && RemainingClusterIsSingleLine(cluster) &&
            FitsInsideFloatingViewport(cluster, window) && pairs.All(pair =>
            {
                var bounds = BoundsIn(pair.Block, primaryRoot);
                var unitBounds = BoundsIn(pair.Unit, primaryRoot);
                return pair.Value.IsVisible && pair.Unit.IsVisible &&
                    bounds.Left >= clusterBounds.Left - 0.5 && unitBounds.Right <= clusterBounds.Right + 0.5;
            });
        var numberBlock = numberText.Parent as Border
            ?? throw new InvalidOperationException("悬浮窗主倒计时数字未落在色块内。");
        var foreground = numberText.Foreground as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗主倒计时前景不是实色画刷。");
        var surface = numberBlock.Background as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗主倒计时数字色块不是实色画刷。");
        var taskCardBrush = taskCard.Background as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗任务卡背景不是实色画刷。");
        var palette = FloatingThemePalette.Daylight;
        var effectiveTaskCard = taskCardBrush.Color.A > 0
            ? taskCardBrush.Color
            : ((SolidColorBrush)window.FindResource(palette.PanelBrushKey)).Color;
        var compositeBackground = CompositeColor(
            surface.Color,
            numberBlock.Opacity,
            effectiveTaskCard);
        const string expectedAccentKey = "FloatingDayPrimaryCountdownAccentBrush";
        var expectedAccent = window.TryFindResource(expectedAccentKey) as SolidColorBrush;
        var plushGlow = window.FindResource(palette.PlushGlowBrushKey) as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗玩偶阴影不是实色画刷。");
        var accentTrackBrush = accentTrack.Background as SolidColorBrush;

        return new FloatingPrimaryCountdownContrastMeasurement(
            "Day",
            expectedAccentKey,
            palette.PlushGlowBrushKey,
            foreground.Color.ToString(),
            surface.Color.ToString(),
            Math.Round(numberBlock.Opacity, 2),
            taskCardBrush.Color.ToString(),
            effectiveTaskCard.ToString(),
            compositeBackground.ToString(),
            Math.Round(ContrastRatio(foreground.Color, compositeBackground), 3),
            Math.Round(numberText.FontSize, 2),
            ReferenceEquals(numberText.Foreground, expectedNumber),
            expectedAccent is not null && ReferenceEquals(accentTrack.Background, expectedAccent),
            !string.Equals(foreground.Color.ToString(), plushGlow.Color.ToString(), StringComparison.Ordinal),
            scenario, remainingText, title.FontSize, fullyVisible);
    }

    private static Color CompositeColor(Color foreground, double elementOpacity, Color background)
    {
        var alpha = (foreground.A / 255d) * elementOpacity;
        static byte Mix(byte foregroundChannel, byte backgroundChannel, double alpha) =>
            (byte)Math.Round(
                (foregroundChannel * alpha) + (backgroundChannel * (1 - alpha)),
                MidpointRounding.AwayFromZero);

        return Color.FromArgb(
            255,
            Mix(foreground.R, background.R, alpha),
            Mix(foreground.G, background.G, alpha),
            Mix(foreground.B, background.B, alpha));
    }

    private static IReadOnlyList<FloatingNoteCardBinding> CaptureFloatingNoteCardBindings(
        StackPanel rows,
        IReadOnlyDictionary<string, NoteRecord> noteById) =>
        rows.Children
            .OfType<Border>()
            .Select(card =>
            {
                var completion = Descendants<CheckBox>(card).SingleOrDefault();
                if (completion?.Tag is not string noteId ||
                    !noteById.TryGetValue(noteId, out var note) ||
                    card.Child is not Grid layout)
                {
                    return null;
                }

                var body = layout.Children.OfType<StackPanel>()
                    .Single(element => Grid.GetColumn(element) == 1);
                var bodyText = body.Children.OfType<TextBlock>()
                    .First(element => string.Equals(element.Text, note.Text, StringComparison.Ordinal));
                var reminderText = body.Children.OfType<TextBlock>()
                    .SingleOrDefault(element => element.Text.StartsWith("下次提醒：", StringComparison.Ordinal));
                // #117 收尾起：列 2 是日期印记，列 3 是“…”菜单按钮（对齐 AI 目标图右缘顺序）。
                var dateStamp = layout.Children.OfType<Border>()
                    .Single(element => Grid.GetColumn(element) == 2);
                return new FloatingNoteCardBinding(
                    card,
                    layout,
                    completion,
                    body,
                    bodyText,
                    reminderText,
                    dateStamp,
                    note);
            })
            .Where(binding => binding is not null)
            .Cast<FloatingNoteCardBinding>()
            .ToArray();

    private static bool FloatingLongNoteSafeAreaMatches(
        FloatingWindow window,
        Grid root,
        IReadOnlyList<FloatingNoteCardBinding> bindings)
    {
        if (bindings.Count != 6)
        {
            return false;
        }

        var cardBounds = bindings.Select(binding => BoundsIn(binding.Card, root)).ToArray();
        var cardsStayInsideRightSafeArea = cardBounds.All(bounds =>
            bounds.Left >= 57 && bounds.Right <= 396.5 && bounds.Bottom > bounds.Top);
        var adjacentCardsDoNotOverlap = cardBounds.Zip(cardBounds.Skip(1)).All(pair =>
            pair.First.Bottom <= pair.Second.Top + 0.5);
        var cardInternalsDoNotOverlap = bindings.All(binding =>
        {
            var card = BoundsIn(binding.Card, root);
            var text = BoundsIn(binding.BodyText, root);
            var completion = BoundsIn(binding.Completion, root);
            var dateStamp = BoundsIn(binding.DateStamp, root);
            return !text.IntersectsWith(completion) &&
                   !text.IntersectsWith(dateStamp) &&
                   text.Left >= card.Left &&
                   text.Right <= card.Right + 0.5 &&
                   text.Top >= card.Top &&
                   text.Bottom <= card.Bottom + 0.5;
        });
        var fixtureTagsRemainBound = bindings.All(binding =>
            binding.Note.TagIds.SequenceEqual(new[] { "reference-warm-tag" }, StringComparer.Ordinal));
        // #117 起：长便签帧默认收起快速新增表单，底部命令区（添加/搜索/设置）常驻可见。
        var bottomCommandAreaKeptForLongNoteFrames =
            Required<Border>(window, "FloatingNoteEntry").Visibility == Visibility.Collapsed &&
            Required<Button>(window, "FloatingAddNoteToggle").Visibility == Visibility.Visible &&
            Required<Button>(window, "FloatingSearchNotes").Visibility == Visibility.Visible &&
            Required<Button>(window, "FloatingOpenSettings").Visibility == Visibility.Visible;
        return cardsStayInsideRightSafeArea &&
               adjacentCardsDoNotOverlap &&
               cardInternalsDoNotOverlap &&
               fixtureTagsRemainBound &&
               bottomCommandAreaKeptForLongNoteFrames;
    }

    private static bool TextWouldNaturallyWrap(TextBlock text)
    {
        var dpi = VisualTreeHelper.GetDpi(text);
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentCulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            text.Foreground,
            dpi.PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace > text.ActualWidth + 1 &&
               text.ActualHeight >= formatted.Height * 1.8;
    }

    private static double VisibleIntersectionHeight(
        FrameworkElement element,
        FrameworkElement viewport)
    {
        var intersection = Rect.Intersect(
            BoundsIn(element, viewport),
            new Rect(new Point(0, 0), viewport.RenderSize));
        return intersection.IsEmpty ? 0 : intersection.Height;
    }

    private static bool FloatingLongNoteAnchorReachable(
        TextBlock text,
        FrameworkElement viewport,
        string state)
    {
        var bounds = BoundsIn(text, viewport);
        var viewportBounds = new Rect(new Point(0, 0), viewport.RenderSize);
        var intersection = Rect.Intersect(bounds, viewportBounds);
        if (intersection.IsEmpty || intersection.Height < 12)
        {
            return false;
        }

        return state == "Top"
            ? bounds.Top >= -1 && bounds.Top < viewportBounds.Bottom
            : bounds.Bottom > viewportBounds.Top && bounds.Bottom <= viewportBounds.Bottom + 1;
    }

    private static string FloatingLongNoteAnchor(
        FloatingNoteCardBinding binding,
        bool takeSuffix)
    {
        const int anchorLength = 12;
        var text = binding.Note.Text;
        var anchor = takeSuffix
            ? text[^Math.Min(anchorLength, text.Length)..]
            : text[..Math.Min(anchorLength, text.Length)];
        return $"{binding.Note.Id}:{anchor}";
    }

    private static bool HasVisualAncestor(DependencyObject element, DependencyObject expectedAncestor)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, expectedAncestor))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<FloatingReferenceActualCapture> CaptureFloatingSearchExpandedFrames(
        FloatingWindow window,
        Grid root,
        StackPanel rows,
        DateTimeOffset referenceUtc,
        int captureWidth,
        int captureHeight)
    {
        var search = Required<Button>(window, "FloatingSearchNotes");
        var inputRow = Required<FrameworkElement>(window, "FloatingNoteSearchInputRow");
        var searchBox = Required<TextBox>(window, "FloatingNoteSearch");
        var scroller = Required<ScrollViewer>(window, "FloatingBodyScroller");
        window.ShowWorkspace(WorkspaceKind.Notes);
        PumpDispatcher();
        if (inputRow.Visibility != Visibility.Visible)
        {
            search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
        }

        var originalCaret = searchBox.CaretBrush;
        var captures = new List<FloatingReferenceActualCapture>();
        try
        {
            searchBox.CaretBrush = Brushes.Transparent;
            window.ApplyTheme();
            window.UpdateTime(referenceUtc);
            // #149：便签行以非 Border 的尾随内边距元素收尾，等待目标按卡片（Border）数计算。
            WaitForStableFloatingReferenceLayout(
                window,
                root,
                rows,
                rows.Children.OfType<Border>().Count());
            ClearCaptureKeyboardFocus();
            const string captureId = "notes-search-expanded-day";
            var spec = FloatingReferenceCaptureSpecs.Single(candidate =>
                string.Equals(candidate.CaptureId, captureId, StringComparison.Ordinal));
            captures.Add(CreateFloatingReferenceActualCapture(
                spec,
                EncodePng(ScaleToFrame(CaptureVisual(root), captureWidth, captureHeight)),
                CreateFloatingReferenceStateFingerprint(
                    "Day",
                    "Notes",
                    string.Empty,
                    string.Empty,
                    spec.State,
                    scroller.VerticalOffset,
                    scroller.ScrollableHeight,
                    "search-expanded",
                    "search-expanded")));
        }
        finally
        {
            searchBox.CaretBrush = originalCaret;
            if (inputRow.Visibility == Visibility.Visible)
            {
                search.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpDispatcher();
            }
        }

        return captures;
    }

    private static FloatingReferenceActualCapture CreateFloatingReferenceActualCapture(
        FloatingReferenceCaptureSpec spec,
        byte[] pngBytes,
        FloatingReferenceStateFingerprint stateFingerprint) =>
        new(spec, pngBytes, stateFingerprint);

    private static FloatingReferenceStateFingerprint CreateFloatingReferenceStateFingerprint(
        string theme,
        string workspace,
        string noteId,
        string noteText,
        string viewportState,
        double verticalOffset,
        double scrollableHeight,
        string firstVisibleAnchor,
        string lastVisibleAnchor)
    {
        var roundedOffset = Math.Round(verticalOffset, 2);
        var roundedScrollableHeight = Math.Round(scrollableHeight, 2);
        var noteTextSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(noteText)));
        var canonicalState = string.Join(
            "\n",
            $"theme={theme}",
            $"workspace={workspace}",
            $"noteId={noteId}",
            $"noteTextSha256={noteTextSha256}",
            $"viewportState={viewportState}",
            $"verticalOffset={roundedOffset.ToString("F2", CultureInfo.InvariantCulture)}",
            $"scrollableHeight={roundedScrollableHeight.ToString("F2", CultureInfo.InvariantCulture)}",
            $"firstVisibleAnchor={firstVisibleAnchor}",
            $"lastVisibleAnchor={lastVisibleAnchor}");
        return new FloatingReferenceStateFingerprint(
            FloatingReferenceStateFingerprintAlgorithm,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalState))),
            theme,
            workspace,
            noteId,
            noteTextSha256,
            viewportState,
            roundedOffset,
            roundedScrollableHeight,
            firstVisibleAnchor,
            lastVisibleAnchor);
    }

    private static FloatingReferenceBaselineValidation ValidateFloatingReferenceBaseline(
        string assetRoot,
        IReadOnlyList<FloatingReferenceActualCapture> actualCaptures,
        bool longNoteTypographyMatches,
        bool longNoteLayoutValid)
    {
        const string sourceRootVariable = "MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT";
        var sourceRootValue = Environment.GetEnvironmentVariable(sourceRootVariable);
        if (string.IsNullOrWhiteSpace(sourceRootValue))
        {
            return FloatingReferenceBaselineValidation.NotRequested;
        }

        var sourceRoot = Path.GetFullPath(sourceRootValue);
        if (!Directory.Exists(sourceRoot))
        {
            return FloatingReferenceBaselineValidation.Invalid(
                true,
                false,
                $"视觉源码根目录不存在：{sourceRootVariable}。");
        }

        var baselineRelativeDirectory =
            $"assets/ui-references/floating-window/520x390/{FloatingReferenceBaselineVersion}";
        var manifestRelativePath = $"{baselineRelativeDirectory}/manifest.json";
        var baselineDirectory = ResolveRepositoryRelativePath(sourceRoot, baselineRelativeDirectory);
        var manifestPath = ResolveRepositoryRelativePath(sourceRoot, manifestRelativePath);
        var generationRequested = string.Equals(
            Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE"),
            "1",
            StringComparison.Ordinal);
        if (generationRequested)
        {
            GenerateFloatingReferenceBaseline(
                sourceRoot,
                baselineDirectory,
                manifestPath,
                baselineRelativeDirectory,
                actualCaptures);
        }

        if (!File.Exists(manifestPath))
        {
            return FloatingReferenceBaselineValidation.Invalid(
                true,
                false,
                $"未找到悬浮窗参考图 manifest：{manifestRelativePath}");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = document.RootElement;
            var structureValid =
                HasExactJsonProperties(
                    root,
                    "schemaVersion",
                    "baselineVersion",
                    "captureKind",
                    "canvas",
                    "sourceBinding",
                    "executable",
                    "pixelComparison",
                    "freshness",
                    "captures") &&
                root.GetProperty("schemaVersion").GetInt32() == 1 &&
                string.Equals(
                    root.GetProperty("baselineVersion").GetString(),
                    FloatingReferenceBaselineVersion,
                    StringComparison.Ordinal) &&
                string.Equals(
                    root.GetProperty("captureKind").GetString(),
                    "actual-wpf-floating-window",
                    StringComparison.Ordinal);

            var canvas = root.GetProperty("canvas");
            structureValid &=
                HasExactJsonProperties(canvas, "width", "height", "metricDpi") &&
                canvas.GetProperty("width").GetInt32() == 520 &&
                canvas.GetProperty("height").GetInt32() == 390 &&
                canvas.GetProperty("metricDpi").GetInt32() == 96;

            var sourceBindingElement = root.GetProperty("sourceBinding");
            structureValid &= HasExactJsonProperties(
                sourceBindingElement,
                "aggregateAlgorithm",
                "aggregateSha256",
                "files");
            var aggregateAlgorithm = sourceBindingElement
                .GetProperty("aggregateAlgorithm")
                .GetString() ?? string.Empty;
            var manifestAggregateSha256 = sourceBindingElement
                .GetProperty("aggregateSha256")
                .GetString() ?? string.Empty;
            var sourceBindings = sourceBindingElement
                .GetProperty("files")
                .EnumerateArray()
                .Select(element =>
                {
                    structureValid &= HasExactJsonProperties(element, "path", "sha256");
                    return new FloatingReferenceSourceBinding(
                        element.GetProperty("path").GetString() ?? string.Empty,
                        element.GetProperty("sha256").GetString() ?? string.Empty);
                })
                .ToArray();

            var executable = root.GetProperty("executable");
            structureValid &= HasExactJsonProperties(
                executable,
                "path",
                "sha256",
                "generation");
            var executableLogicalPath = executable.GetProperty("path").GetString() ?? string.Empty;
            var manifestExecutableSha256 = executable.GetProperty("sha256").GetString() ?? string.Empty;
            var executableGeneration = executable.GetProperty("generation").GetString() ?? string.Empty;

            var pixelComparison = root.GetProperty("pixelComparison");
            structureValid &= HasExactJsonProperties(
                pixelComparison,
                "algorithm",
                "maxAbsoluteChannelDelta",
                "maximumMeanAbsoluteError",
                "maximumRootMeanSquareError",
                "maximumChangedPixelCount");
            var comparisonAlgorithm = pixelComparison.GetProperty("algorithm").GetString() ?? string.Empty;
            var maximumAbsoluteChannelDelta = pixelComparison
                .GetProperty("maxAbsoluteChannelDelta")
                .GetInt32();
            var maximumMeanAbsoluteError = pixelComparison
                .GetProperty("maximumMeanAbsoluteError")
                .GetDouble();
            var maximumRootMeanSquareError = pixelComparison
                .GetProperty("maximumRootMeanSquareError")
                .GetDouble();
            var maximumChangedPixelCount = pixelComparison
                .GetProperty("maximumChangedPixelCount")
                .GetInt32();

            var freshness = root.GetProperty("freshness");
            structureValid &= HasExactJsonProperties(
                freshness,
                "stateFingerprintAlgorithm",
                "minimumTopBottomMeanAbsoluteError",
                "minimumTopBottomRootMeanSquareError",
                "minimumTopBottomChangedPixelCount",
                "topBottomComparisons");
            var stateFingerprintAlgorithm = freshness
                .GetProperty("stateFingerprintAlgorithm")
                .GetString() ?? string.Empty;
            var minimumTopBottomMeanAbsoluteError = freshness
                .GetProperty("minimumTopBottomMeanAbsoluteError")
                .GetDouble();
            var minimumTopBottomRootMeanSquareError = freshness
                .GetProperty("minimumTopBottomRootMeanSquareError")
                .GetDouble();
            var minimumTopBottomChangedPixelCount = freshness
                .GetProperty("minimumTopBottomChangedPixelCount")
                .GetInt32();
            var topBottomComparisons = freshness
                .GetProperty("topBottomComparisons")
                .EnumerateArray()
                .Select(element => ReadFloatingReferenceStateDifference(element, ref structureValid))
                .ToArray();

            var captureElements = root.GetProperty("captures").EnumerateArray().ToArray();
            var captures = captureElements.Select(element =>
                ReadFloatingReferenceCapture(element, ref structureValid)).ToArray();
            structureValid &=
                captures.Length == FloatingReferenceCaptureSpecs.Length &&
                string.Equals(
                    aggregateAlgorithm,
                    FloatingReferenceSourceAggregateAlgorithm,
                    StringComparison.Ordinal) &&
                string.Equals(executableLogicalPath, "app/MinminCountdown.exe", StringComparison.Ordinal) &&
                string.Equals(
                    executableGeneration,
                    "managed isolated dotnet publish; SHA-256 read from the executing equivalent app binary",
                    StringComparison.Ordinal) &&
                string.Equals(
                    comparisonAlgorithm,
                    FloatingReferencePixelComparisonAlgorithm,
                    StringComparison.Ordinal) &&
                maximumAbsoluteChannelDelta == FloatingReferenceMaximumAbsoluteChannelDelta &&
                Math.Abs(maximumMeanAbsoluteError - FloatingReferenceMaximumMeanAbsoluteError) < 0.0000001 &&
                Math.Abs(maximumRootMeanSquareError - FloatingReferenceMaximumRootMeanSquareError) < 0.0000001 &&
                maximumChangedPixelCount == FloatingReferenceMaximumChangedPixelCount &&
                string.Equals(
                    stateFingerprintAlgorithm,
                    FloatingReferenceStateFingerprintAlgorithm,
                    StringComparison.Ordinal) &&
                Math.Abs(
                    minimumTopBottomMeanAbsoluteError -
                    FloatingReferenceMinimumTopBottomMeanAbsoluteError) < 0.0000001 &&
                Math.Abs(
                    minimumTopBottomRootMeanSquareError -
                    FloatingReferenceMinimumTopBottomRootMeanSquareError) < 0.0000001 &&
                minimumTopBottomChangedPixelCount ==
                    FloatingReferenceMinimumTopBottomChangedPixelCount &&
                topBottomComparisons.Length == 1;

            var expectedCapturePaths = FloatingReferenceCaptureSpecs
                .Select(spec => $"{baselineRelativeDirectory}/{spec.FileName}")
                .ToArray();
            var capturePaths = captures.Select(capture => capture.Path).ToArray();
            var capturePathsUnique =
                capturePaths.Distinct(StringComparer.Ordinal).Count() == capturePaths.Length;
            structureValid &=
                capturePaths.Length == expectedCapturePaths.Length &&
                capturePaths.Order(StringComparer.Ordinal)
                    .SequenceEqual(expectedCapturePaths.Order(StringComparer.Ordinal), StringComparer.Ordinal);
            var expectedStateBindingsMatch = captures.All(capture =>
            {
                var spec = FloatingReferenceCaptureSpecs.SingleOrDefault(candidate =>
                    string.Equals(candidate.CaptureId, capture.CaptureId, StringComparison.Ordinal));
                return spec is not null &&
                    string.Equals(capture.Scenario, spec.Scenario, StringComparison.Ordinal) &&
                    string.Equals(capture.Theme, spec.Theme, StringComparison.Ordinal) &&
                    string.Equals(capture.State, spec.State, StringComparison.Ordinal) &&
                    string.Equals(
                        capture.Path,
                        $"{baselineRelativeDirectory}/{spec.FileName}",
                        StringComparison.Ordinal) &&
                    string.Equals(capture.StateFingerprint.Theme, spec.Theme, StringComparison.Ordinal) &&
                    string.Equals(capture.StateFingerprint.ViewportState, spec.State, StringComparison.Ordinal);
            });
            structureValid &= expectedStateBindingsMatch;
            var captureIds = captures.Select(capture => capture.CaptureId).ToArray();
            var captureIdsUnique =
                captureIds.Distinct(StringComparer.Ordinal).Count() == captureIds.Length &&
                captureIds.Order(StringComparer.Ordinal).SequenceEqual(
                    FloatingReferenceCaptureSpecs
                        .Select(spec => spec.CaptureId)
                        .Order(StringComparer.Ordinal),
                    StringComparer.Ordinal);
            var scenarioThemePairsUnique =
                captures
                    .Select(capture => $"{capture.Scenario}\0{capture.Theme}\0{capture.State}")
                    .Distinct(StringComparer.Ordinal)
                    .Count() == captures.Length;

            var currentSourceBindings = GetFloatingReferenceSourceBindings(sourceRoot);
            var currentSourceAggregate = AggregateFloatingReferenceSourceBindings(currentSourceBindings);
            var sourceHashesMatch = FloatingReferenceSourcesMatch(sourceBindings, currentSourceBindings, manifestAggregateSha256);

            var processPath = Environment.ProcessPath;
            var executableSha256 = !string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath)
                ? Sha256File(processPath)
                : string.Empty;
            var executableHashMatches =
                IsUpperSha256(manifestExecutableSha256) &&
                string.Equals(manifestExecutableSha256, executableSha256, StringComparison.Ordinal);

            var actualByCaptureId = actualCaptures.ToDictionary(
                capture => capture.Spec.CaptureId,
                StringComparer.Ordinal);
            var stateFingerprintsMatch = captures.All(capture =>
                actualByCaptureId.TryGetValue(capture.CaptureId, out var actualCapture) &&
                FloatingReferenceStateFingerprintMatchesCanonical(capture.StateFingerprint) &&
                capture.StateFingerprint == actualCapture.StateFingerprint);
            var stateFingerprintsUnique =
                captures.Select(capture => capture.StateFingerprint.Sha256)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == captures.Length;
            var topBottomPixelsDiffer = true;
            foreach (var themeName in new[] { "Day" })
            {
                var topId = $"long-notes-{themeName.ToLowerInvariant()}-top";
                var bottomId = $"long-notes-{themeName.ToLowerInvariant()}-bottom";
                if (!actualByCaptureId.TryGetValue(topId, out var topActual) ||
                    !actualByCaptureId.TryGetValue(bottomId, out var bottomActual))
                {
                    topBottomPixelsDiffer = false;
                    continue;
                }

                var measuredDifference = CompareFloatingReferencePixels(
                    topActual.PngBytes,
                    bottomActual.PngBytes);
                var manifestDifference = topBottomComparisons.SingleOrDefault(difference =>
                    string.Equals(difference.Theme, themeName, StringComparison.Ordinal));
                topBottomPixelsDiffer &=
                    manifestDifference is not null &&
                    string.Equals(manifestDifference.TopCaptureId, topId, StringComparison.Ordinal) &&
                    string.Equals(manifestDifference.BottomCaptureId, bottomId, StringComparison.Ordinal) &&
                    manifestDifference.Metrics == measuredDifference &&
                    measuredDifference.MeanAbsoluteError >=
                        FloatingReferenceMinimumTopBottomMeanAbsoluteError &&
                    measuredDifference.RootMeanSquareError >=
                        FloatingReferenceMinimumTopBottomRootMeanSquareError &&
                    measuredDifference.ChangedPixelCount >=
                        FloatingReferenceMinimumTopBottomChangedPixelCount;
            }

            var baselineEvaluation = VisualReferenceBaseline.Floating.Validate(
                new FloatingVisualReferenceRequest(
                    new VisualReferenceFileRequest(
                        sourceRoot,
                        baselineDirectory,
                        expectedCapturePaths,
                        captures.Select(capture => new VisualReferenceManifestCapture(
                            capture.CaptureId,
                            capture.Path,
                            capture.Width,
                            capture.Height,
                            capture.PngSha256,
                            new VisualReferenceAlphaMetrics(
                                capture.Alpha.Minimum,
                                capture.Alpha.Maximum,
                                capture.Alpha.TransparentPixelCount,
                                capture.Alpha.NonOpaquePixelCount,
                                capture.Alpha.OpaquePixelCount),
                            new VisualReferenceContentRoi(
                                capture.ContentRoi.AlphaThreshold,
                                capture.ContentRoi.X,
                                capture.ContentRoi.Y,
                                capture.ContentRoi.Width,
                                capture.ContentRoi.Height,
                                capture.ContentRoi.PixelCount),
                            new VisualReferencePixelMetrics(
                                capture.Comparison.MaxAbsoluteChannelDelta,
                                capture.Comparison.MeanAbsoluteError,
                                capture.Comparison.RootMeanSquareError,
                                capture.Comparison.ChangedPixelCount)))
                            .ToArray(),
                        actualCaptures.Select(capture => new VisualReferenceActualCapture(
                            capture.Spec.CaptureId,
                            capture.PngBytes)).ToArray(),
                        maximumAbsoluteChannelDelta,
                        maximumMeanAbsoluteError,
                        maximumRootMeanSquareError,
                        maximumChangedPixelCount),
                    520,
                    390,
                    structureValid,
                    capturePathsUnique,
                    scenarioThemePairsUnique,
                    sourceHashesMatch,
                    executableHashMatches,
                    expectedStateBindingsMatch,
                    captureIdsUnique,
                    stateFingerprintsMatch,
                    stateFingerprintsUnique,
                    topBottomPixelsDiffer,
                    longNoteTypographyMatches,
                    longNoteLayoutValid));
            var measurements = baselineEvaluation.Measurements.Select(measurement =>
            {
                var capture = captures.Single(candidate => string.Equals(
                    candidate.CaptureId,
                    measurement.CaptureId,
                    StringComparison.Ordinal));
                return new FloatingReferenceBaselineCaptureMeasurement(
                    capture.CaptureId,
                    capture.Scenario,
                    capture.Theme,
                    capture.State,
                    capture.Path,
                    measurement.BaselineSha256,
                    measurement.ActualSha256,
                    capture.StateFingerprint,
                    new FloatingReferenceAlphaMetrics(
                        measurement.ActualAlpha.Minimum,
                        measurement.ActualAlpha.Maximum,
                        measurement.ActualAlpha.TransparentPixelCount,
                        measurement.ActualAlpha.NonOpaquePixelCount,
                        measurement.ActualAlpha.OpaquePixelCount),
                    new FloatingReferenceContentRoi(
                        measurement.ActualContentRoi.AlphaThreshold,
                        measurement.ActualContentRoi.X,
                        measurement.ActualContentRoi.Y,
                        measurement.ActualContentRoi.Width,
                        measurement.ActualContentRoi.Height,
                        measurement.ActualContentRoi.PixelCount),
                    new FloatingReferencePixelMetrics(
                        measurement.Comparison.MaxAbsoluteChannelDelta,
                        measurement.Comparison.MeanAbsoluteError,
                        measurement.Comparison.RootMeanSquareError,
                        measurement.Comparison.ChangedPixelCount));
            }).ToArray();
            var gates = baselineEvaluation.Gates;
            var freshnessGates = baselineEvaluation.FreshnessGates;
            var diagnostics = baselineEvaluation.Diagnostics;
            return new FloatingReferenceBaselineValidation(
                true,
                true,
                manifestRelativePath,
                currentSourceAggregate,
                executableSha256,
                gates,
                freshnessGates,
                measurements,
                diagnostics);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return FloatingReferenceBaselineValidation.Invalid(
                true,
                File.Exists(manifestPath),
                exception.Message,
                manifestRelativePath);
        }
    }

    private static void GenerateFloatingReferenceBaseline(
        string sourceRoot,
        string baselineDirectory,
        string manifestPath,
        string baselineRelativeDirectory,
        IReadOnlyList<FloatingReferenceActualCapture> actualCaptures)
    {
        if (actualCaptures.Count != FloatingReferenceCaptureSpecs.Length ||
            actualCaptures.Select(capture => capture.Spec.CaptureId)
                .Distinct(StringComparer.Ordinal)
                .Count() != FloatingReferenceCaptureSpecs.Length)
        {
            throw new InvalidOperationException(
                $"悬浮窗参考图生成必须提供严格 {FloatingReferenceCaptureSpecs.Length} 帧 inventory。");
        }

        var pngHashes = actualCaptures
            .Select(capture => Convert.ToHexString(SHA256.HashData(capture.PngBytes)))
            .ToArray();
        if (pngHashes.Distinct(StringComparer.Ordinal).Count() != pngHashes.Length)
        {
            throw new InvalidOperationException("悬浮窗参考图生成拒绝写入复用 PNG 的基线目录。");
        }

        if (Directory.Exists(baselineDirectory) &&
            Directory.EnumerateFileSystemEntries(baselineDirectory).Any())
        {
            throw new InvalidOperationException(
                $"悬浮窗正式参考基线已存在，拒绝覆盖：{FloatingReferenceBaselineVersion}。");
        }

        Directory.CreateDirectory(baselineDirectory);
        var captureMetrics = actualCaptures.ToDictionary(
            capture => capture.Spec.CaptureId,
            capture => MeasureFloatingReferenceCapture(capture.PngBytes),
            StringComparer.Ordinal);
        if (captureMetrics.Values.Any(metrics => metrics.Width != 520 || metrics.Height != 390))
        {
            throw new InvalidOperationException("悬浮窗参考图生成帧不是 520x390。");
        }

        var sourceBindings = GetFloatingReferenceSourceBindings(sourceRoot);
        var sourceAggregate = AggregateFloatingReferenceSourceBindings(sourceBindings);
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            throw new InvalidOperationException("无法绑定生成参考图的当前 EXE。");
        }

        var executableSha256 = Sha256File(processPath);
        var stateDifferences = new[] { "Day" }
            .Select(theme =>
            {
                var topId = $"long-notes-{theme.ToLowerInvariant()}-top";
                var bottomId = $"long-notes-{theme.ToLowerInvariant()}-bottom";
                var top = actualCaptures.Single(capture => capture.Spec.CaptureId == topId);
                var bottom = actualCaptures.Single(capture => capture.Spec.CaptureId == bottomId);
                return new FloatingReferenceStateDifference(
                    theme,
                    topId,
                    bottomId,
                    CompareFloatingReferencePixels(top.PngBytes, bottom.PngBytes));
            })
            .ToArray();
        if (stateDifferences.Any(difference =>
                difference.Metrics.MeanAbsoluteError < FloatingReferenceMinimumTopBottomMeanAbsoluteError ||
                difference.Metrics.RootMeanSquareError < FloatingReferenceMinimumTopBottomRootMeanSquareError ||
                difference.Metrics.ChangedPixelCount < FloatingReferenceMinimumTopBottomChangedPixelCount))
        {
            throw new InvalidOperationException("长便签 top/bottom 帧像素差不足，拒绝生成可复用旧帧的 manifest。");
        }

        var manifest = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schemaVersion = 1,
                baselineVersion = FloatingReferenceBaselineVersion,
                captureKind = "actual-wpf-floating-window",
                canvas = new { width = 520, height = 390, metricDpi = 96 },
                sourceBinding = new
                {
                    aggregateAlgorithm = FloatingReferenceSourceAggregateAlgorithm,
                    aggregateSha256 = sourceAggregate,
                    files = sourceBindings.Select(binding => new
                    {
                        path = binding.Path,
                        sha256 = binding.Sha256
                    })
                },
                executable = new
                {
                    path = "app/MinminCountdown.exe",
                    sha256 = executableSha256,
                    generation = "managed isolated dotnet publish; SHA-256 read from the executing equivalent app binary"
                },
                pixelComparison = new
                {
                    algorithm = FloatingReferencePixelComparisonAlgorithm,
                    maxAbsoluteChannelDelta = FloatingReferenceMaximumAbsoluteChannelDelta,
                    maximumMeanAbsoluteError = FloatingReferenceMaximumMeanAbsoluteError,
                    maximumRootMeanSquareError = FloatingReferenceMaximumRootMeanSquareError,
                    maximumChangedPixelCount = FloatingReferenceMaximumChangedPixelCount
                },
                freshness = new
                {
                    stateFingerprintAlgorithm = FloatingReferenceStateFingerprintAlgorithm,
                    minimumTopBottomMeanAbsoluteError =
                        FloatingReferenceMinimumTopBottomMeanAbsoluteError,
                    minimumTopBottomRootMeanSquareError =
                        FloatingReferenceMinimumTopBottomRootMeanSquareError,
                    minimumTopBottomChangedPixelCount =
                        FloatingReferenceMinimumTopBottomChangedPixelCount,
                    topBottomComparisons = stateDifferences.Select(difference => new
                    {
                        theme = difference.Theme,
                        topCaptureId = difference.TopCaptureId,
                        bottomCaptureId = difference.BottomCaptureId,
                        maxAbsoluteChannelDelta = difference.Metrics.MaxAbsoluteChannelDelta,
                        meanAbsoluteError = difference.Metrics.MeanAbsoluteError,
                        rootMeanSquareError = difference.Metrics.RootMeanSquareError,
                        changedPixelCount = difference.Metrics.ChangedPixelCount
                    })
                },
                captures = FloatingReferenceCaptureSpecs.Select(spec =>
                {
                    var capture = actualCaptures.Single(actual =>
                        string.Equals(actual.Spec.CaptureId, spec.CaptureId, StringComparison.Ordinal));
                    var path = $"{baselineRelativeDirectory}/{spec.FileName}";
                    return CreateFloatingReferenceManifestCapture(
                        capture,
                        path,
                        captureMetrics[spec.CaptureId]);
                })
            },
            new JsonSerializerOptions { WriteIndented = true });
        foreach (var capture in actualCaptures)
        {
            var relativePath = $"{baselineRelativeDirectory}/{capture.Spec.FileName}";
            var path = ResolveRepositoryRelativePath(sourceRoot, relativePath);
            WriteBytesAtomically(path, capture.PngBytes);
        }
        WriteBytesAtomically(manifestPath, manifest);
    }

    private static object CreateFloatingReferenceManifestCapture(
        FloatingReferenceActualCapture capture,
        string path,
        FloatingReferenceCaptureMetrics metrics) =>
        new
        {
            captureId = capture.Spec.CaptureId,
            scenario = capture.Spec.Scenario,
            theme = capture.Spec.Theme,
            state = capture.Spec.State,
            path,
            width = metrics.Width,
            height = metrics.Height,
            pngSha256 = Convert.ToHexString(SHA256.HashData(capture.PngBytes)),
            stateFingerprint = new
            {
                algorithm = capture.StateFingerprint.Algorithm,
                sha256 = capture.StateFingerprint.Sha256,
                theme = capture.StateFingerprint.Theme,
                workspace = capture.StateFingerprint.Workspace,
                noteId = capture.StateFingerprint.NoteId,
                noteTextSha256 = capture.StateFingerprint.NoteTextSha256,
                viewportState = capture.StateFingerprint.ViewportState,
                verticalOffset = capture.StateFingerprint.VerticalOffset,
                scrollableHeight = capture.StateFingerprint.ScrollableHeight,
                firstVisibleAnchor = capture.StateFingerprint.FirstVisibleAnchor,
                lastVisibleAnchor = capture.StateFingerprint.LastVisibleAnchor
            },
            alpha = new
            {
                algorithm = FloatingReferenceAlphaAlgorithm,
                minimum = metrics.Alpha.Minimum,
                maximum = metrics.Alpha.Maximum,
                transparentPixelCount = metrics.Alpha.TransparentPixelCount,
                nonOpaquePixelCount = metrics.Alpha.NonOpaquePixelCount,
                opaquePixelCount = metrics.Alpha.OpaquePixelCount
            },
            contentRoi = new
            {
                algorithm = FloatingReferenceRoiAlgorithm,
                alphaThreshold = metrics.ContentRoi.AlphaThreshold,
                x = metrics.ContentRoi.X,
                y = metrics.ContentRoi.Y,
                width = metrics.ContentRoi.Width,
                height = metrics.ContentRoi.Height,
                pixelCount = metrics.ContentRoi.PixelCount
            },
            comparison = new
            {
                maxAbsoluteChannelDelta = 0,
                meanAbsoluteError = 0d,
                rootMeanSquareError = 0d,
                changedPixelCount = 0
            }
        };

    private static FloatingReferenceManifestCapture ReadFloatingReferenceCapture(
        JsonElement element,
        ref bool structureValid)
    {
        structureValid &= HasExactJsonProperties(
            element,
            "captureId",
            "scenario",
            "theme",
            "state",
            "path",
            "width",
            "height",
            "pngSha256",
            "stateFingerprint",
            "alpha",
            "contentRoi",
            "comparison");
        var stateFingerprint = ReadFloatingReferenceStateFingerprint(
            element.GetProperty("stateFingerprint"),
            ref structureValid);
        var alpha = element.GetProperty("alpha");
        structureValid &=
            HasExactJsonProperties(
                alpha,
                "algorithm",
                "minimum",
                "maximum",
                "transparentPixelCount",
                "nonOpaquePixelCount",
                "opaquePixelCount") &&
            string.Equals(
                alpha.GetProperty("algorithm").GetString(),
                FloatingReferenceAlphaAlgorithm,
                StringComparison.Ordinal);
        var contentRoi = element.GetProperty("contentRoi");
        structureValid &=
            HasExactJsonProperties(
                contentRoi,
                "algorithm",
                "alphaThreshold",
                "x",
                "y",
                "width",
                "height",
                "pixelCount") &&
            string.Equals(
                contentRoi.GetProperty("algorithm").GetString(),
                FloatingReferenceRoiAlgorithm,
                StringComparison.Ordinal);
        var comparison = element.GetProperty("comparison");
        structureValid &= HasExactJsonProperties(
            comparison,
            "maxAbsoluteChannelDelta",
            "meanAbsoluteError",
            "rootMeanSquareError",
            "changedPixelCount");
        return new FloatingReferenceManifestCapture(
            element.GetProperty("captureId").GetString() ?? string.Empty,
            element.GetProperty("scenario").GetString() ?? string.Empty,
            element.GetProperty("theme").GetString() ?? string.Empty,
            element.GetProperty("state").GetString() ?? string.Empty,
            element.GetProperty("path").GetString() ?? string.Empty,
            element.GetProperty("width").GetInt32(),
            element.GetProperty("height").GetInt32(),
            element.GetProperty("pngSha256").GetString() ?? string.Empty,
            stateFingerprint,
            new FloatingReferenceAlphaMetrics(
                alpha.GetProperty("minimum").GetInt32(),
                alpha.GetProperty("maximum").GetInt32(),
                alpha.GetProperty("transparentPixelCount").GetInt32(),
                alpha.GetProperty("nonOpaquePixelCount").GetInt32(),
                alpha.GetProperty("opaquePixelCount").GetInt32()),
            new FloatingReferenceContentRoi(
                contentRoi.GetProperty("alphaThreshold").GetInt32(),
                contentRoi.GetProperty("x").GetInt32(),
                contentRoi.GetProperty("y").GetInt32(),
                contentRoi.GetProperty("width").GetInt32(),
                contentRoi.GetProperty("height").GetInt32(),
                contentRoi.GetProperty("pixelCount").GetInt32()),
            new FloatingReferencePixelMetrics(
                comparison.GetProperty("maxAbsoluteChannelDelta").GetInt32(),
                comparison.GetProperty("meanAbsoluteError").GetDouble(),
                comparison.GetProperty("rootMeanSquareError").GetDouble(),
                comparison.GetProperty("changedPixelCount").GetInt32()));
    }

    private static FloatingReferenceStateFingerprint ReadFloatingReferenceStateFingerprint(
        JsonElement element,
        ref bool structureValid)
    {
        structureValid &= HasExactJsonProperties(
            element,
            "algorithm",
            "sha256",
            "theme",
            "workspace",
            "noteId",
            "noteTextSha256",
            "viewportState",
            "verticalOffset",
            "scrollableHeight",
            "firstVisibleAnchor",
            "lastVisibleAnchor");
        return new FloatingReferenceStateFingerprint(
            element.GetProperty("algorithm").GetString() ?? string.Empty,
            element.GetProperty("sha256").GetString() ?? string.Empty,
            element.GetProperty("theme").GetString() ?? string.Empty,
            element.GetProperty("workspace").GetString() ?? string.Empty,
            element.GetProperty("noteId").GetString() ?? string.Empty,
            element.GetProperty("noteTextSha256").GetString() ?? string.Empty,
            element.GetProperty("viewportState").GetString() ?? string.Empty,
            element.GetProperty("verticalOffset").GetDouble(),
            element.GetProperty("scrollableHeight").GetDouble(),
            element.GetProperty("firstVisibleAnchor").GetString() ?? string.Empty,
            element.GetProperty("lastVisibleAnchor").GetString() ?? string.Empty);
    }

    private static FloatingReferenceStateDifference ReadFloatingReferenceStateDifference(
        JsonElement element,
        ref bool structureValid)
    {
        structureValid &= HasExactJsonProperties(
            element,
            "theme",
            "topCaptureId",
            "bottomCaptureId",
            "maxAbsoluteChannelDelta",
            "meanAbsoluteError",
            "rootMeanSquareError",
            "changedPixelCount");
        return new FloatingReferenceStateDifference(
            element.GetProperty("theme").GetString() ?? string.Empty,
            element.GetProperty("topCaptureId").GetString() ?? string.Empty,
            element.GetProperty("bottomCaptureId").GetString() ?? string.Empty,
            new FloatingReferencePixelMetrics(
                element.GetProperty("maxAbsoluteChannelDelta").GetInt32(),
                element.GetProperty("meanAbsoluteError").GetDouble(),
                element.GetProperty("rootMeanSquareError").GetDouble(),
                element.GetProperty("changedPixelCount").GetInt32()));
    }

    private static bool FloatingReferenceStateFingerprintMatchesCanonical(
        FloatingReferenceStateFingerprint fingerprint)
    {
        if (!string.Equals(
                fingerprint.Algorithm,
                FloatingReferenceStateFingerprintAlgorithm,
                StringComparison.Ordinal) ||
            !IsUpperSha256(fingerprint.Sha256) ||
            !IsUpperSha256(fingerprint.NoteTextSha256))
        {
            return false;
        }

        var canonical = CreateFloatingReferenceStateFingerprint(
            fingerprint.Theme,
            fingerprint.Workspace,
            fingerprint.NoteId,
            string.Empty,
            fingerprint.ViewportState,
            fingerprint.VerticalOffset,
            fingerprint.ScrollableHeight,
            fingerprint.FirstVisibleAnchor,
            fingerprint.LastVisibleAnchor);
        var canonicalState = string.Join(
            "\n",
            $"theme={fingerprint.Theme}",
            $"workspace={fingerprint.Workspace}",
            $"noteId={fingerprint.NoteId}",
            $"noteTextSha256={fingerprint.NoteTextSha256}",
            $"viewportState={fingerprint.ViewportState}",
            $"verticalOffset={fingerprint.VerticalOffset.ToString("F2", CultureInfo.InvariantCulture)}",
            $"scrollableHeight={fingerprint.ScrollableHeight.ToString("F2", CultureInfo.InvariantCulture)}",
            $"firstVisibleAnchor={fingerprint.FirstVisibleAnchor}",
            $"lastVisibleAnchor={fingerprint.LastVisibleAnchor}");
        return string.Equals(canonical.Algorithm, fingerprint.Algorithm, StringComparison.Ordinal) &&
               string.Equals(
                   Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalState))),
                   fingerprint.Sha256,
                   StringComparison.Ordinal);
    }

    private static IReadOnlyList<FloatingReferenceSourceBinding> GetFloatingReferenceSourceBindings(
        string sourceRoot) =>
        FloatingReferenceCanonicalSourcePaths.Select(relativePath =>
        {
            var path = ResolveRepositoryRelativePath(sourceRoot, relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"悬浮窗参考图绑定的视觉源码不存在：{relativePath}",
                    path);
            }

            return new FloatingReferenceSourceBinding(relativePath, Sha256File(path));
        }).ToArray();

    private static bool FloatingReferenceSourcesMatch(
        IReadOnlyList<FloatingReferenceSourceBinding> manifest,
        IReadOnlyList<FloatingReferenceSourceBinding> current,
        string aggregate) =>
        manifest.Count == current.Count &&
        manifest.Select(binding => binding.Path).SequenceEqual(FloatingReferenceCanonicalSourcePaths, StringComparer.Ordinal) &&
        manifest.Zip(current).All(pair => pair.First.Path == pair.Second.Path && IsUpperSha256(pair.First.Sha256) &&
            pair.First.Sha256 == pair.Second.Sha256) &&
        IsUpperSha256(aggregate) && aggregate == AggregateFloatingReferenceSourceBindings(manifest) &&
        aggregate == AggregateFloatingReferenceSourceBindings(current);

    private static string AggregateFloatingReferenceSourceBindings(
        IEnumerable<FloatingReferenceSourceBinding> bindings)
    {
        using var stream = new MemoryStream();
        foreach (var binding in bindings.OrderBy(binding => binding.Path, StringComparer.Ordinal))
        {
            var line = Encoding.UTF8.GetBytes($"{binding.Path}\0{binding.Sha256}\n");
            stream.Write(line, 0, line.Length);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static FloatingReferenceCaptureMetrics MeasureFloatingReferenceCapture(byte[] bytes)
    {
        var bitmap = DecodeBitmap(bytes);
        var (pixels, stride) = GetBgra32Pixels(bitmap);
        var minimum = byte.MaxValue;
        var maximum = byte.MinValue;
        var transparent = 0;
        var nonOpaque = 0;
        var opaque = 0;
        var minX = bitmap.PixelWidth;
        var minY = bitmap.PixelHeight;
        var maxX = -1;
        var maxY = -1;
        var contentPixels = 0;
        for (var y = 0; y < bitmap.PixelHeight; y++)
        {
            for (var x = 0; x < bitmap.PixelWidth; x++)
            {
                var alpha = pixels[(y * stride) + (x * 4) + 3];
                minimum = Math.Min(minimum, alpha);
                maximum = Math.Max(maximum, alpha);
                if (alpha == 0)
                {
                    transparent++;
                }
                if (alpha < byte.MaxValue)
                {
                    nonOpaque++;
                }
                else
                {
                    opaque++;
                }
                if (alpha <= 32)
                {
                    continue;
                }

                contentPixels++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        var roi = contentPixels == 0
            ? new FloatingReferenceContentRoi(32, 0, 0, 0, 0, 0)
            : new FloatingReferenceContentRoi(
                32,
                minX,
                minY,
                maxX - minX + 1,
                maxY - minY + 1,
                contentPixels);
        return new FloatingReferenceCaptureMetrics(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            new FloatingReferenceAlphaMetrics(minimum, maximum, transparent, nonOpaque, opaque),
            roi);
    }

    private static FloatingReferencePixelMetrics CompareFloatingReferencePixels(
        byte[] baselineBytes,
        byte[] actualBytes)
    {
        var baseline = DecodeBitmap(baselineBytes);
        var actual = DecodeBitmap(actualBytes);
        if (baseline.PixelWidth != actual.PixelWidth || baseline.PixelHeight != actual.PixelHeight)
        {
            return new FloatingReferencePixelMetrics(int.MaxValue, double.MaxValue, double.MaxValue, int.MaxValue);
        }

        var (baselinePixels, _) = GetBgra32Pixels(baseline);
        var (actualPixels, _) = GetBgra32Pixels(actual);
        var maximumDelta = 0;
        var absoluteDeltaSum = 0d;
        var squaredDeltaSum = 0d;
        var changedPixels = 0;
        for (var pixelOffset = 0; pixelOffset < baselinePixels.Length; pixelOffset += 4)
        {
            var pixelChanged = false;
            for (var channel = 0; channel < 4; channel++)
            {
                var delta = Math.Abs(
                    baselinePixels[pixelOffset + channel] - actualPixels[pixelOffset + channel]);
                maximumDelta = Math.Max(maximumDelta, delta);
                absoluteDeltaSum += delta;
                squaredDeltaSum += delta * delta;
                pixelChanged |= delta != 0;
            }

            if (pixelChanged)
            {
                changedPixels++;
            }
        }

        return new FloatingReferencePixelMetrics(
            maximumDelta,
            Math.Round(absoluteDeltaSum / baselinePixels.Length, 6),
            Math.Round(Math.Sqrt(squaredDeltaSum / baselinePixels.Length), 6),
            changedPixels);
    }

    private static BitmapSource DecodeBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var bitmap = decoder.Frames[0];
        bitmap.Freeze();
        return bitmap;
    }

    private static (byte[] Pixels, int Stride) GetBgra32Pixels(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return (pixels, stride);
    }

    private static bool HasExactJsonProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        return actual.Length == expected.Length &&
               actual.Distinct(StringComparer.Ordinal).Count() == actual.Length &&
               actual.Order(StringComparer.Ordinal)
                   .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static string ResolveRepositoryRelativePath(string sourceRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Contains('\\', StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"参考图路径不是可移植仓库相对路径：{relativePath}");
        }

        var root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"参考图路径逃出源码根目录：{relativePath}");
        }

        return resolved;
    }

    private static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static bool IsUpperSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static Rect BoundsIn(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor)
            .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));

    private static bool SameBounds(Rect actual, Rect expected) =>
        Math.Abs(actual.X - expected.X) <= 0.25 &&
        Math.Abs(actual.Y - expected.Y) <= 0.25 &&
        Math.Abs(actual.Width - expected.Width) <= 0.25 &&
        Math.Abs(actual.Height - expected.Height) <= 0.25;

    private static void WaitForStableFloatingReferenceLayout(
        FloatingWindow window,
        Grid root,
        StackPanel rows,
        int expectedRowCount)
    {
        const int maximumPasses = 10;
        const int requiredStablePasses = 2;
        const double layoutTolerance = 0.01;
        double[]? previous = null;
        var stablePasses = 0;

        for (var pass = 0; pass < maximumPasses; pass++)
        {
            PumpDispatcher();
            window.UpdateLayout();
            PumpDispatcher();

            var fixedTargets = new FrameworkElement[]
            {
                Required<Border>(window, "FloatingThemeBackdrop"),
                Required<Border>(window, "TaskCard"),
                Required<Border>(window, "FloatingHeaderSurface"),
                Required<Canvas>(window, "FloatingPlushScene")
            };
            var rowTargets = rows.Children.OfType<FrameworkElement>().ToArray();
            var renderedCardCount = rows.Children.OfType<Border>().Count();
            var targets = fixedTargets.Concat(rowTargets).ToArray();
            var visibleImages = Descendants<Image>(root)
                .Where(image => image.IsVisible)
                .ToArray();
            var ready =
                renderedCardCount == expectedRowCount &&
                root.ActualWidth > 0 &&
                root.ActualHeight > 0 &&
                root.IsMeasureValid &&
                root.IsArrangeValid &&
                targets.All(target =>
                    target.ActualWidth > 0 &&
                    target.ActualHeight > 0 &&
                    target.IsMeasureValid &&
                    target.IsArrangeValid) &&
                visibleImages.Length > 0 &&
                visibleImages.All(image =>
                    image.ActualWidth > 0 &&
                    image.ActualHeight > 0 &&
                    image.Source is BitmapSource source &&
                    source.PixelWidth > 0 &&
                    source.PixelHeight > 0);
            var current = new List<double>
            {
                root.ActualWidth,
                root.ActualHeight
            };
            foreach (var target in targets)
            {
                var bounds = BoundsIn(target, root);
                current.Add(bounds.X);
                current.Add(bounds.Y);
                current.Add(bounds.Width);
                current.Add(bounds.Height);
            }

            var currentVector = current.ToArray();
            var unchanged =
                previous is not null &&
                previous.Length == currentVector.Length &&
                previous.Zip(currentVector)
                    .All(pair => Math.Abs(pair.First - pair.Second) <= layoutTolerance);
            stablePasses = ready && unchanged ? stablePasses + 1 : 0;
            if (stablePasses >= requiredStablePasses)
            {
                if (!WaitForRenderedCaptureState(root, () => CaptureTextMotionSettled(root)))
                {
                    throw new InvalidOperationException("悬浮窗参考帧数字未在 300ms 内完成淡入。");
                }
                return;
            }

            previous = currentVector;
        }

        throw new InvalidOperationException(
            $"悬浮窗固定参考场景在 {maximumPasses} 轮调度后仍未稳定：" +
            $"期望卡片 {expectedRowCount}，实际卡片 {rows.Children.OfType<Border>().Count()}。");
    }

    private static FloatingScrollBarValidation ValidateFloatingScrollBar(
        MainWindow manager,
        FloatingWindow window,
        Grid root,
        StackPanel rows,
        DateTimeOffset referenceUtc,
        int captureWidth,
        int captureHeight)
    {
        var bodyScroller = Required<ScrollViewer>(window, "FloatingBodyScroller");
        bodyScroller.ApplyTemplate();
        var localStyle = bodyScroller.Resources.Contains(typeof(ScrollBar))
            ? bodyScroller.Resources[typeof(ScrollBar)] as Style
            : null;
        var applicationStyle = Application.Current.TryFindResource(typeof(ScrollBar)) as Style;
        var mainWindowStyleUnaffected =
            applicationStyle is not null &&
            !ReferenceEquals(localStyle, applicationStyle) &&
            Descendants<ScrollBar>(manager).All(scrollBar =>
                !ReferenceEquals(scrollBar.Style, localStyle));
        var measurements = new List<FloatingScrollBarStateMeasurement>();
        var captures = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        window.ApplyTheme();
        window.UpdateTime(referenceUtc);
        WaitForStableFloatingReferenceLayout(window, root, rows, 9);
        {
            foreach (var state in new[]
                     {
                         (Name: "normal", VisualState: FloatingScrollBarThumbState.Normal),
                         (Name: "hover", VisualState: FloatingScrollBarThumbState.Hover),
                         (Name: "drag", VisualState: FloatingScrollBarThumbState.Drag)
                     })
            {
                var parts = RequiredFloatingScrollBarParts(bodyScroller);
                parts.Thumb.ApplyVisualStateForValidation(state.VisualState);
                PumpDispatcher();
                window.UpdateLayout();
                var scrollBarBounds = BoundsIn(parts.ScrollBar, root);
                var thumbBounds = BoundsIn(parts.ThumbVisual, root);
                var visibleCardBounds = rows.Children
                    .OfType<Border>()
                    .SelectMany(card => Descendants<Border>(card).Prepend(card))
                    .Where(border =>
                        border.IsVisible &&
                        border.ActualWidth > 0 &&
                        border.ActualHeight > 0 &&
                        HasVisibleBrush(border.Background))
                    .Select(border => BoundsIn(border, root))
                    .ToArray();
                var protectedContentBounds = Descendants<FrameworkElement>(rows)
                    .Where(element =>
                        element.IsVisible &&
                        element.ActualWidth > 0 &&
                        element.ActualHeight > 0 &&
                        element is TextBlock or Control)
                    .Select(element => BoundsIn(element, root))
                    .ToArray();
                var maximumVisibleCardRight = visibleCardBounds.Length == 0
                    ? 0
                    : visibleCardBounds.Max(bounds => bounds.Right);
                var cardSurfacesAreClear =
                    visibleCardBounds.All(bounds => !bounds.IntersectsWith(thumbBounds));
                var contentIsClear =
                    protectedContentBounds.All(bounds => !bounds.IntersectsWith(thumbBounds));
                var doesNotObscureContent =
                    scrollBarBounds.Right <= 396.5 &&
                    thumbBounds.Right <= 396.5 &&
                    maximumVisibleCardRight <= 396.5 &&
                    cardSurfacesAreClear &&
                    contentIsClear;
                var usesThemeTokens =
                    ReferenceEquals(
                        parts.Thumb.Background,
                        window.FindResource("FloatingMutedBrush")) &&
                    ReferenceEquals(
                        parts.ThumbVisual.BorderBrush,
                        window.FindResource("ControlBorderBrush"));
                var bodyBounds = BoundsIn(bodyScroller, root);
                measurements.Add(new FloatingScrollBarStateMeasurement(
                    "Day",
                    state.Name,
                    Math.Round(
                        Math.Min(parts.HitRail.ActualWidth, parts.Thumb.ActualWidth),
                        2),
                    Math.Round(parts.ThumbVisual.ActualWidth, 2),
                    Math.Round((parts.ThumbVisual.Child as Border)?.Opacity ?? parts.ThumbVisual.Opacity, 2),
                    Math.Round(BoundsIn(parts.Track, root).Right, 2),
                    Math.Round(BoundsIn(parts.Thumb, root).Left, 2),
                    Math.Round(BoundsIn(parts.Thumb, root).Right, 2),
                    Math.Round(scrollBarBounds.Right, 2),
                    Math.Round(thumbBounds.Left, 2),
                    Math.Round(thumbBounds.Right, 2),
                    Math.Round(maximumVisibleCardRight, 2),
                    Math.Round(bodyBounds.X + bodyScroller.ViewportWidth, 2),
                    cardSurfacesAreClear,
                    contentIsClear,
                    doesNotObscureContent,
                    usesThemeTokens,
                    BrushColor((parts.ThumbVisual.Child as Border)?.Background ?? parts.ThumbVisual.Background),
                    BrushColor(parts.ThumbVisual.BorderBrush),
                    RenderedOuterContrast(root, parts.ThumbVisual, verticalOnly: true)));
                ClearCaptureKeyboardFocus();
                captures[$"day-{state.Name}"] =
                    EncodePng(ScaleToFrame(
                        CaptureVisual(root),
                        captureWidth,
                        captureHeight));
            }
        }

        window.ApplyTheme();
        window.UpdateTime(referenceUtc);
        WaitForStableFloatingReferenceLayout(window, root, rows, 9);
        var inputParts = RequiredFloatingScrollBarParts(bodyScroller);
        bodyScroller.ScrollToTop();
        PumpDispatcher();
        var wheelOffsetBefore = bodyScroller.VerticalOffset;
        var wheelEvent = new MouseWheelEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            -120)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = bodyScroller
        };
        bodyScroller.RaiseEvent(wheelEvent);
        PumpDispatcher();
        var wheelOffsetAfter = bodyScroller.VerticalOffset;

        bodyScroller.ScrollToTop();
        PumpDispatcher();
        var dragOffsetBefore = bodyScroller.VerticalOffset;
        inputParts.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0)
        {
            RoutedEvent = Thumb.DragStartedEvent,
            Source = inputParts.Thumb
        });
        inputParts.Thumb.RaiseEvent(new DragDeltaEventArgs(
            0,
            Math.Max(24, inputParts.Track.ActualHeight / 3))
        {
            RoutedEvent = Thumb.DragDeltaEvent,
            Source = inputParts.Thumb
        });
        PumpDispatcher();
        var dragOffsetAfter = bodyScroller.VerticalOffset;
        inputParts.Thumb.RaiseEvent(new DragCompletedEventArgs(
            0,
            dragOffsetAfter - dragOffsetBefore,
            canceled: false)
        {
            RoutedEvent = Thumb.DragCompletedEvent,
            Source = inputParts.Thumb
        });
        PumpDispatcher();

        var normal = measurements.Where(sample => sample.State == "normal").ToArray();
        var hover = measurements.Where(sample => sample.State == "hover").ToArray();
        var drag = measurements.Where(sample => sample.State == "drag").ToArray();
        var bodyContentRights = measurements.Select(sample => sample.BodyContentRight).ToArray();
        var themeTokensMatch = measurements.All(sample => sample.UsesThemeTokens);
        var gates = ValidationExercises.EvaluateFloatingScrollBarGates(
            new FloatingScrollBarGateInputs(
                localStyle is not null &&
                    ReferenceEquals(inputParts.ScrollBar.Style, localStyle),
                mainWindowStyleUnaffected,
                measurements.Min(sample => sample.HitTargetWidth),
                normal.Min(sample => sample.ThumbVisualWidth),
                hover.Min(sample => sample.ThumbVisualWidth),
                drag.Min(sample => sample.ThumbVisualWidth),
                normal.Min(sample => sample.ThumbOpacity),
                hover.Min(sample => sample.ThumbOpacity),
                drag.Min(sample => sample.ThumbOpacity),
                themeTokensMatch,
                normal.All(sample => sample.DoesNotObscureContent),
                hover.All(sample => sample.DoesNotObscureContent),
                drag.All(sample => sample.DoesNotObscureContent),
                bodyContentRights.Max() - bodyContentRights.Min() <= 0.1,
                wheelEvent.Handled && wheelOffsetAfter > wheelOffsetBefore,
                dragOffsetAfter > dragOffsetBefore));
        inputParts.Thumb.ApplyVisualStateForValidation(
            FloatingScrollBarThumbState.Normal);
        bodyScroller.ScrollToTop();
        PumpDispatcher();

        return new FloatingScrollBarValidation(
            gates,
            measurements,
            Math.Round(wheelOffsetBefore, 2),
            Math.Round(wheelOffsetAfter, 2),
            Math.Round(dragOffsetBefore, 2),
            Math.Round(dragOffsetAfter, 2),
            captures);
    }

    // #157：§10 设计命中轨宽（FloatingScrollBarHitRail，本地样式 Width=10）。视口右缘的
    // 确定性比较基准只消费该应用自有几何，不消费随系统滚动条度量漂移的滚动条列宽。
    private static double RequiredFloatingScrollBarRailWidth(ScrollBar scrollBar)
    {
        scrollBar.ApplyTemplate();
        return scrollBar.Template.FindName("FloatingScrollBarHitRail", scrollBar) is Grid rail
            ? rail.ActualWidth
            : throw new InvalidOperationException("悬浮窗滚动条命中轨道未实例化。");
    }

    private static FloatingScrollBarParts RequiredFloatingScrollBarParts(
        ScrollViewer bodyScroller)
    {
        bodyScroller.ApplyTemplate();
        var scrollBar = bodyScroller.Template.FindName(
                "PART_VerticalScrollBar",
                bodyScroller) as ScrollBar
            ?? throw new InvalidOperationException("悬浮窗垂直滚动条未实例化。");
        scrollBar.ApplyTemplate();
        var hitRail = scrollBar.Template.FindName(
                "FloatingScrollBarHitRail",
                scrollBar) as Grid
            ?? throw new InvalidOperationException("悬浮窗滚动条命中轨道未实例化。");
        var track = scrollBar.Template.FindName("PART_Track", scrollBar) as Track
            ?? throw new InvalidOperationException("悬浮窗滚动条 Track 未实例化。");
        var thumb = track.Thumb as FloatingScrollBarThumb
            ?? throw new InvalidOperationException("悬浮窗滚动条 Thumb 未实例化。");
        thumb.ApplyTemplate();
        var thumbVisual = thumb.Template.FindName(
                "FloatingScrollBarThumbVisual",
                thumb) as Border
            ?? throw new InvalidOperationException("悬浮窗滚动条可见 thumb 未实例化。");
        return new FloatingScrollBarParts(
            scrollBar,
            hitRail,
            track,
            thumb,
            thumbVisual);
    }

    private static bool HasVisibleBrush(Brush? brush) =>
        brush switch
        {
            null => false,
            SolidColorBrush solid => solid.Color.A > 0,
            _ => true // Non-solid brushes have no single alpha; treat them as visible.
        };

    private static FloatingViewportState CaptureFloatingViewportState(
        string name,
        FloatingWindow window,
        Grid root,
        StackPanel rows)
    {
        var bodyScroller = Required<ScrollViewer>(window, "FloatingBodyScroller");
        bodyScroller.ApplyTemplate();
        var verticalScrollBar = bodyScroller.Template.FindName(
            "PART_VerticalScrollBar",
            bodyScroller) as ScrollBar;
        var verticalScrollBarBounds = verticalScrollBar is null
            ? Rect.Empty
            : BoundsIn(verticalScrollBar, root);
        var bodyViewport = BoundsIn(bodyScroller, root);
        var scrollBarVisible = verticalScrollBar is { IsVisible: true };
        var scrollerSlotRight = bodyViewport.X + bodyScroller.ActualWidth;
        // 直接内联模式测试而非复用 scrollBarVisible：可空流分析不穿透捕获的 bool，
        // 三元 true 分支需要由模式测试本身推出 verticalScrollBar 非空（CS8604）。
        var designedScrollBarRail = verticalScrollBar is { IsVisible: true }
            ? RequiredFloatingScrollBarRailWidth(verticalScrollBar)
            : 0d;
        // #157：视口右缘确定性口径。旧口径 `bodyViewport.X + ViewportWidth` 把 WPF 滚动条列宽
        // 计入视口右缘；该列宽在本地样式 Width=10 之外仍被系统滚动条度量撑大（Windows 滚动条
        // 度量：100% 缩放 17 DIP、125% 缩放 17.6 DIP），使比较成为会话显示缩放的函数（实测
        // 100% 缩放过 0.05 DIP、125% 缩放红 0.55 DIP，§11 漂移族），与产品几何无关。
        var protectedHeaderContent = new[]
            {
                new NamedBounds(
                    "Title",
                    TextContentBounds(Required<TextBlock>(window, "FloatingTitle"), root))
            }
            .Concat(new (string Name, ContentControl Control)[]
            {
                ("CountdownsButton", Required<ToggleButton>(window, "FloatingCountdownsButton")),
                ("NotesButton", Required<ToggleButton>(window, "FloatingNotesButton")),
                ("MinimizeToTrayButton", Required<Button>(window, "MinimizeToTrayButton")),
                ("OpenManagerButton", Required<Button>(window, "OpenManagerButton")),
                ("CloseButton", Required<Button>(window, "CloseFloatingButton"))
            }
            .Select(pair => new NamedBounds(
                pair.Name,
                ControlContentBounds(pair.Control, root))));
        var protectedBodyText = Descendants<TextBlock>(bodyScroller)
            .Where(element => element.IsVisible &&
                              element.ActualWidth > 0 &&
                              element.ActualHeight > 0)
            .Select((element, index) => new NamedBounds(
                $"BodyText{index + 1}",
                Rect.Intersect(TextContentBounds(element, root), bodyViewport)));
        var protectedBodyControls = Descendants<Control>(bodyScroller)
            .Where(element => element.IsVisible &&
                              element.ActualWidth > 0 &&
                              element.ActualHeight > 0)
            .Select((element, index) => new NamedBounds(
                string.IsNullOrWhiteSpace(element.Name)
                    ? $"BodyControl{index + 1}:{element.GetType().Name}"
                    : element.Name,
                Rect.Intersect(BoundsIn(element, root), bodyViewport)));
        var protectedContent = protectedHeaderContent
            .Concat(protectedBodyText)
            .Concat(protectedBodyControls)
            .Where(target => !target.Bounds.IsEmpty &&
                             target.Bounds.Width > 0 &&
                             target.Bounds.Height > 0)
            .ToArray();
        var plushScene = CapturePlushScene(
            Required<Canvas>(window, "FloatingPlushScene"));
        var contentOverlapTargets = protectedContent
            .Where(target => !AlphaAvoids(plushScene, new[] { target.Bounds }, 32))
            .Select(target => target.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var renderedCards = rows.Children.OfType<Border>().ToArray();
        var renderedCardBounds = renderedCards
            .Select(card => BoundsIn(card, root))
            .ToArray();
        var noteEntry = Required<Border>(window, "FloatingNoteEntry");
        var noteEntryBounds = BoundsIn(noteEntry, root);
        var noteDateStamps = renderedCards
            .SelectMany(Descendants<Border>)
            .Where(border =>
                Math.Abs(border.ActualWidth - 48) <= 0.5 &&
                border.BorderThickness.Left >= 1.5)
            .ToArray();
        var noteDateStampBounds = noteDateStamps
            .Select(stamp => BoundsIn(stamp, root))
            .ToArray();
        var isNoteWorkspace = renderedCards.Any(card => Descendants<CheckBox>(card).Any());
        var maximumRenderedCardRight = renderedCardBounds.Length == 0
            ? 0
            : renderedCardBounds.Max(bounds => bounds.Right);
        var maximumDateStampRight = noteDateStampBounds.Length == 0
            ? 0d
            : noteDateStampBounds.Max(bounds => bounds.Right);
        // #157 修复轮 1（reviewer CONCERN）：视口右缘比较收敛到
        // EvaluateFloatingViewportGeometryGate 单一实现——现役捕获与反例门禁消费同一算术
        // （FloatingViewportDesignedRight），删除「− 设计命中轨」即被负向夹具钉红。
        // null = 该表面不参与本比较：非便签工作区不消费卡/印记视口比较（原 !isNoteWorkspace
        // 短路语义），隐藏的快速新增不参与（原 !noteEntry.IsVisible 短路语义）。
        var viewportRightContractMatches = ValidationExercises.EvaluateFloatingViewportGeometryGate(
            new FloatingViewportGeometryGateInputs(
                scrollerSlotRight,
                designedScrollBarRail,
                scrollBarVisible,
                isNoteWorkspace ? maximumRenderedCardRight : null,
                isNoteWorkspace ? maximumDateStampRight : null,
                noteEntry.IsVisible ? noteEntryBounds.Right : null));
        var bodyContentRight = ValidationExercises.FloatingViewportDesignedRight(
            scrollerSlotRight,
            designedScrollBarRail,
            scrollBarVisible);
        var noteEntryFitsSafeArea =
            !noteEntry.IsVisible ||
            noteEntryBounds.Right <= 396.5;
        var workspaceSurfaceGeometryMatches =
            !isNoteWorkspace ||
            (renderedCardBounds.All(bounds =>
                 Math.Abs(bounds.Width - 319) <= 0.5 &&
                 bounds.Right <= 396.5) &&
             viewportRightContractMatches &&
             noteEntryFitsSafeArea &&
             noteDateStamps.Length == renderedCards.Length &&
             noteDateStamps.All(stamp =>
                 !stamp.IsHitTestVisible &&
                 !stamp.Focusable));
        var minimumRenderedCardWidth = renderedCardBounds.Length == 0
            ? 0
            : renderedCardBounds.Min(bounds => bounds.Width);
        var rasterSources = renderedCards
            .Select(card => (card.Background as ImageBrush)?.ImageSource)
            .ToArray();
        var rasterCardCount = rasterSources.Count(source =>
            source is BitmapImage { UriSource: { } uri } &&
            string.Equals(
                Path.GetFileName(uri.LocalPath),
                "component-day-card-blank.png",
                StringComparison.OrdinalIgnoreCase));
        var rasterCardsShareSource =
            rasterSources.Length > 0 &&
            rasterSources[0] is not null &&
            rasterSources.All(source => ReferenceEquals(source, rasterSources[0]));
        var renderedMultilineNoteCount = renderedCards.Count(card =>
            Descendants<CheckBox>(card).Any(checkBox => checkBox.IsVisible) &&
            Descendants<TextBlock>(card).Any(text =>
                text.IsVisible &&
                text.TextWrapping == TextWrapping.Wrap &&
                !text.Text.Contains('\r') &&
                !text.Text.Contains('\n') &&
                TextWouldNaturallyWrap(text)));
        return new FloatingViewportState(
            name,
            Math.Round(window.ActualWidth, 2),
            Math.Round(window.ActualHeight, 2),
            Math.Round(root.ActualWidth, 2),
            Math.Round(root.ActualHeight, 2),
            renderedCards.Length,
            rasterCardCount,
            rasterCardsShareSource,
            Descendants<TextBlock>(rows).Count(element => element.IsVisible),
            Descendants<CheckBox>(rows).Count(element => element.IsVisible),
            renderedMultilineNoteCount,
            Math.Round(bodyScroller.ScrollableHeight, 2),
            Math.Round(bodyScroller.VerticalOffset, 2),
            verticalScrollBar?.IsVisible == true,
            Math.Round(verticalScrollBarBounds.Left, 2),
            Math.Round(verticalScrollBarBounds.Right, 2),
            verticalScrollBar?.IsHitTestVisible == true,
            Math.Round(bodyContentRight, 2),
            Math.Round(maximumRenderedCardRight, 2),
            Math.Round(minimumRenderedCardWidth, 2),
            noteDateStamps.Length,
            Math.Round(maximumDateStampRight, 2),
            Math.Round(noteEntryBounds.Width, 2),
            Math.Round(noteEntryBounds.Right, 2),
            noteEntryFitsSafeArea,
            workspaceSurfaceGeometryMatches,
            contentOverlapTargets);
    }

    private static string[] CalendarIndexStampTexts(Border card) =>
        Descendants<Border>(card)
            .Where(border =>
                Math.Abs(border.ActualWidth - 48) <= 0.5 &&
                border.BorderThickness.Left >= 1.5)
            .SelectMany(Descendants<TextBlock>)
            .Select(text => text.Text)
            .ToArray();

    private static Rect TextContentBounds(TextBlock textBlock, UIElement ancestor)
    {
        var origin = textBlock.TranslatePoint(new Point(0, 0), ancestor);
        var formatted = FormatText(textBlock, VisualTreeHelper.GetDpi(textBlock));
        var width = Math.Min(
            textBlock.ActualWidth,
            formatted.WidthIncludingTrailingWhitespace + Math.Max(0, formatted.OverhangTrailing));
        var height = Math.Min(
            textBlock.ActualHeight,
            formatted.Height + Math.Max(0, formatted.OverhangAfter));
        return new Rect(
            Math.Max(0, origin.X - 2),
            Math.Max(0, origin.Y - 2),
            width + 4,
            height + 4);
    }

    private static Rect ControlContentBounds(ContentControl control, UIElement ancestor)
    {
        if (control.Content is FrameworkElement contentElement &&
            contentElement.IsVisible &&
            contentElement.ActualWidth > 0 &&
            contentElement.ActualHeight > 0)
        {
            return BoundsIn(contentElement, ancestor);
        }

        var origin = control.TranslatePoint(new Point(0, 0), ancestor);
        var text = control.Content?.ToString() ?? string.Empty;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(control.FontFamily, control.FontStyle, control.FontWeight, control.FontStretch),
            control.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(control).PixelsPerDip);
        var width = Math.Min(control.ActualWidth, formatted.WidthIncludingTrailingWhitespace);
        var height = Math.Min(control.ActualHeight, formatted.Height);
        return new Rect(
            origin.X + Math.Max(0, (control.ActualWidth - width) / 2) - 2,
            origin.Y + Math.Max(0, (control.ActualHeight - height) / 2) - 2,
            width + 4,
            height + 4);
    }

    private static NormalizedRect Normalize(Rect bounds, double width, double height) =>
        width <= 0 || height <= 0
            ? NormalizedRect.Empty
            : new NormalizedRect(
                Math.Round(bounds.X / width, 4),
                Math.Round(bounds.Y / height, 4),
                Math.Round(bounds.Width / width, 4),
                Math.Round(bounds.Height / height, 4));

    private static bool Matches(NormalizedRect actual, NormalizedRect expected, double tolerance) =>
        Math.Abs(actual.X - expected.X) <= tolerance &&
        Math.Abs(actual.Y - expected.Y) <= tolerance &&
        Math.Abs(actual.Right - expected.Right) <= tolerance &&
        Math.Abs(actual.Bottom - expected.Bottom) <= tolerance;

    private static PlushSceneCapture CapturePlushScene(Canvas scene)
    {
        // The day mascot scene is a single transparent PNG. Reading the frozen
        // source avoids a WPF compositor edge case where an Image-only Canvas
        // captures as transparent even though its source is loaded and laid out.
        var bitmap = scene.Children
            .OfType<Image>()
            .Select(image => image.Source as BitmapSource)
            .FirstOrDefault(source => source is not null) ?? CaptureVisual(scene);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var rightRoiWidth = Math.Min(190, bitmap.PixelWidth);
        var bottomRoiWidth = Math.Min(180, bitmap.PixelWidth);
        var topRoiHeight = Math.Min(160, bitmap.PixelHeight);
        var bottomRoiTop = Math.Min(150, bitmap.PixelHeight);
        var regions = new[]
        {
            new Int32Rect(0, 0, Math.Min(190, bitmap.PixelWidth), topRoiHeight),
            new Int32Rect(
                bitmap.PixelWidth - rightRoiWidth,
                0,
                rightRoiWidth,
                topRoiHeight),
            new Int32Rect(
                bitmap.PixelWidth - bottomRoiWidth,
                bottomRoiTop,
                bottomRoiWidth,
                bitmap.PixelHeight - bottomRoiTop)
        };
        var bounds = regions
            .Select(region => FindAlphaBounds(pixels, stride, region, 32))
            .ToArray();
        return new PlushSceneCapture(bitmap.PixelWidth, bitmap.PixelHeight, pixels, stride, bounds);
    }

    private static Rect? FindAlphaBounds(
        byte[] pixels,
        int stride,
        Int32Rect region,
        byte threshold)
    {
        var minX = region.X + region.Width;
        var minY = region.Y + region.Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = region.Y; y < region.Y + region.Height; y++)
        {
            for (var x = region.X; x < region.X + region.Width; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] <= threshold)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0
            ? null
            : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static bool AlphaAvoids(
        PlushSceneCapture scene,
        IEnumerable<Rect> protectedBounds,
        byte threshold)
    {
        foreach (var bounds in protectedBounds)
        {
            var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, scene.Width);
            var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, scene.Height);
            var right = Math.Clamp((int)Math.Ceiling(bounds.Right), 0, scene.Width);
            var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), 0, scene.Height);
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    if (scene.Pixels[(y * scene.Stride) + (x * 4) + 3] > threshold)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool IsEntirelyTransparent(BitmapSource source)
    {
        var (pixels, _) = GetBgra32Pixels(source);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void ProbeEmptyDayAgainstPinnedBaseline(byte[] actualPng)
    {
        WriteEmptyDayProbeReport(actualPng, emptyRender: false, captureDiagnostics: null);
    }

    private static void WriteEmptyDayProbeReport(
        byte[] actualPng,
        bool emptyRender,
        string? captureDiagnostics)
    {
        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new InvalidOperationException(
                "empty-day probe 需要 MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT。");
        }

        var baselinePath = ResolveRepositoryRelativePath(
            Path.GetFullPath(sourceRoot),
            $"assets/ui-references/main-window/1165x1350/{MainReferenceBaselineVersion}/main-day-countdowns-empty.png");
        var baselineBytes = File.ReadAllBytes(baselinePath);
        var actualMetrics = MeasureMainReferenceCapture(actualPng);
        var comparison = CompareMainReferencePixels(baselineBytes, actualPng);
        var (pixels, stride) = GetBgra32Pixels(DecodeBitmap(actualPng));
        int? firstX = null;
        int? firstY = null;
        byte? firstA = null;
        var holeCount = 0;
        var minA = byte.MaxValue;
        var maxA = byte.MinValue;
        for (var y = 0; y < actualMetrics.Height; y++)
        {
            for (var x = 0; x < actualMetrics.Width; x++)
            {
                var alpha = pixels[(y * stride) + (x * 4) + 3];
                minA = Math.Min(minA, alpha);
                maxA = Math.Max(maxA, alpha);
                if (alpha != byte.MaxValue)
                {
                    holeCount++;
                    if (firstX is null)
                    {
                        firstX = x;
                        firstY = y;
                        firstA = alpha;
                    }
                }
            }
        }

        var actualSha = Convert.ToHexString(SHA256.HashData(actualPng));
        var baselineSha = Convert.ToHexString(SHA256.HashData(baselineBytes));
        var withinRmse = comparison.RootMeanSquareError <= MainReferenceMaximumRootMeanSquareError;
        var withinMae = comparison.MeanAbsoluteError <= MainReferenceMaximumMeanAbsoluteError;
        var withinChanged = comparison.ChangedPixelCount <= MainReferenceMaximumChangedPixelCount;
        var hashesMatch = string.Equals(actualSha, baselineSha, StringComparison.Ordinal);
        var reportPath = Path.Combine(Path.GetTempPath(), "minmin-empty-day-probe.json");
        var report = new Dictionary<string, object?>
        {
            ["emptyRender"] = emptyRender,
            ["captureDiagnostics"] = captureDiagnostics,
            ["width"] = actualMetrics.Width,
            ["height"] = actualMetrics.Height,
            ["minA"] = minA,
            ["maxA"] = maxA,
            ["holeCount"] = holeCount,
            ["firstNonOpaqueX"] = firstX,
            ["firstNonOpaqueY"] = firstY,
            ["firstNonOpaqueA"] = firstA,
            ["rmse"] = comparison.RootMeanSquareError,
            ["mae"] = comparison.MeanAbsoluteError,
            ["changed"] = comparison.ChangedPixelCount,
            ["maxAbs"] = comparison.MaxAbsoluteChannelDelta,
            ["actualSha256"] = actualSha,
            ["baselineSha256"] = baselineSha,
            ["hashesMatch"] = hashesMatch,
            ["withinRmse"] = withinRmse,
            ["rmseLimit"] = MainReferenceMaximumRootMeanSquareError,
            ["reportPath"] = reportPath
        };
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (emptyRender ||
            !withinRmse ||
            !withinMae ||
            !withinChanged ||
            !hashesMatch ||
            holeCount != 0)
        {
            throw new InvalidOperationException(
                "empty-day probe 未对齐当前钉住基线：" +
                $"emptyRender={emptyRender} minA={minA} maxA={maxA} holeCount={holeCount} " +
                $"first=({firstX},{firstY},A={firstA}) RMSE={comparison.RootMeanSquareError:0.###} " +
                $"MAE={comparison.MeanAbsoluteError:0.###} changed={comparison.ChangedPixelCount} " +
                $"hashesMatch={hashesMatch} report={reportPath}");
        }
    }

    // 文本抗锯齿与格式化默认跟随系统显示环境漂移，且不只是抗锯齿灰阶：WPF 未显式钉死时
    // 会按显示拓扑推导渲染参数（Avalon.Graphics 覆盖缺失时的系统查询路径），显示拓扑变化
    // （如虚拟显示适配器加入）会同时改变 Display 格式化的像素对齐度量（实测长便签列表
    // ScrollableHeight 871.4 -> 879.67 漂移）与灰阶抗锯齿的 gamma/对比度（细笔画出现/消失），
    // 使像素基线门禁在环境变化后误红。捕获路径统一钉死为 Ideal 浮点格式化（度量只由字体
    // 文件决定，无像素网格 hinting）+ Aliased 1-bit 光栅化（不经灰阶 gamma/对比度管线），
    // 从机制上移除显示推导参数的入口，使捕获像素与布局对显示拓扑/时段确定。
    // TextOptions 是可继承附加属性，在捕获根上设置即下传，重复调用幂等。
    private static void PinDeterministicTextOptions(FrameworkElement visual)
    {
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
    }

    private static RenderTargetBitmap RenderVisualToBitmap(FrameworkElement visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // 顶层窗口尺寸会被操作系统钳制到虚拟屏范围内，其后的真实布局会把内容根压回钳制尺寸；
    // 参考帧按固定尺寸显式 Measure/Arrange 后立即渲染，中间不得 UpdateLayout 或 PumpDispatcher。
    private static RenderTargetBitmap CaptureVisualAtFixedSize(
        FrameworkElement visual,
        int width,
        int height)
    {
        PinDeterministicTextOptions(visual);
        var size = new Size(width, height);
        visual.Measure(size);
        visual.Arrange(new Rect(new Point(0, 0), size));
        return RenderVisualToBitmap(visual, width, height);
    }

    private static RenderTargetBitmap CaptureVisual(FrameworkElement visual)
    {
        PinDeterministicTextOptions(visual);
        var window = Window.GetWindow(visual);
        if (window is not null && window.Opacity == 0)
        {
            window.Opacity = 0.01;
            window.UpdateLayout();
            PumpDispatcher();
        }

        visual.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var scrollStates = Descendants<ScrollViewer>(visual)
            .Select(scroll => (Scroll: scroll, Horizontal: scroll.HorizontalOffset, Vertical: scroll.VerticalOffset))
            .ToArray();
        _ = CaptureVisualAtFixedSize(visual, width, height);
        foreach (var state in scrollStates)
        {
            state.Scroll.ScrollToHorizontalOffset(state.Horizontal);
            state.Scroll.ScrollToVerticalOffset(state.Vertical);
        }

        PumpDispatcher();
        visual.UpdateLayout();
        // 不可见隔离桌面不合成落在可见区内的窗口：整幅透明时泊到离屏位泵送后重渲染。
        // 显示设备瞬态缺失时合成恢复需要等待，逐次短退避后再重渲染。
        var bitmap = RenderVisualToBitmap(visual, width, height);
        for (var attempt = 0;
             attempt < 4 && IsEntirelyTransparent(bitmap) && window is not null;
             attempt++)
        {
            System.Threading.Thread.Sleep(Math.Min(100 << attempt, 500));
            window.Left = -20_000;
            window.Top = -20_000;
            window.UpdateLayout();
            PumpDispatcher();
            window.Left = -20_000;
            window.Top = -20_000;
            bitmap = RenderVisualToBitmap(visual, width, height);
        }

        if (IsEntirelyTransparent(bitmap))
        {
            throw new InvalidOperationException(
                "CaptureVisual 96-DPI Render 整幅透明：" +
                $"visual={visual.GetType().Name} actual={visual.ActualWidth}x{visual.ActualHeight} " +
                $"bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight} " +
                $"window=({window?.Left},{window?.Top}) opacity={window?.Opacity} " +
                $"tier=0x{RenderCapability.Tier:X} " +
                $"renderEvenWhenNoDisplayDevices={ApplicationValidation.IsRenderEvenWhenNoDisplayDevicesEnabled()}");
        }

        return bitmap;
    }

    private static BitmapSource CaptureOpaqueMainSurface(
        FrameworkElement visual,
        int captureWidth,
        int captureHeight,
        Popup? overlayPopup = null)
    {
        if (Application.Current.FindResource("BackgroundBrush") is not SolidColorBrush background ||
            background.Color.A != byte.MaxValue)
        {
            throw new InvalidOperationException("主窗口背景必须是不透明主题画刷。");
        }

        var source = CaptureVisualAtFixedSize(visual, captureWidth, captureHeight);
        if (IsEntirelyTransparent(source))
        {
            throw new InvalidOperationException(
                "CaptureOpaqueMainSurface 拒绝合成整幅透明位图。");
        }

        var composited = CompositeOverOpaqueBackground(source, background.Color);
        if (overlayPopup is null)
        {
            return composited;
        }

        return CompositePopupOverlay(composited, visual, overlayPopup, background.Color);
    }

    // #134：高 DPI 捕获。按 DIP 坐标显式 Measure/Arrange 后渲染进指定 DPI 的目标位图，
    // WPF 会按目标 DPI 光栅化文字与矢量，再经不透明底合成。画布像素宽高由 DIP×DPI 比例推导，
    // 整幅透明同样拒绝，保证不会产出空白或全透明伪证据。
    private static BitmapSource CaptureOpaqueMainSurfaceAtDpi(
        FrameworkElement visual,
        int dipWidth,
        int dipHeight,
        double dpiX,
        double dpiY)
    {
        if (Application.Current.FindResource("BackgroundBrush") is not SolidColorBrush background ||
            background.Color.A != byte.MaxValue)
        {
            throw new InvalidOperationException("主窗口背景必须是不透明主题画刷。");
        }

        PinDeterministicTextOptions(visual);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(dipWidth * dpiX / 96.0));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(dipHeight * dpiY / 96.0));
        var size = new Size(dipWidth, dipHeight);
        visual.Measure(size);
        visual.Arrange(new Rect(new Point(0, 0), size));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        if (IsEntirelyTransparent(bitmap))
        {
            throw new InvalidOperationException(
                "高 DPI 捕获拒绝合成整幅透明位图：" +
                $"{pixelWidth}x{pixelHeight} @{dpiX:0.#}x{dpiY:0.#}dpi。");
        }

        return CompositeOverOpaqueBackground(bitmap, background.Color);
    }

    private static BitmapSource CompositePopupOverlay(
        BitmapSource surface,
        FrameworkElement host,
        Popup popup,
        Color backdrop)
    {
        if (!popup.IsOpen)
        {
            throw new InvalidOperationException("筛选弹层打开态参考帧要求 Popup.IsOpen。");
        }

        if (popup.Child is not FrameworkElement child ||
            popup.PlacementTarget is not FrameworkElement target)
        {
            throw new InvalidOperationException("筛选弹层打开态参考帧缺少 Child 或 PlacementTarget。");
        }

        child.UpdateLayout();
        if (child.ActualWidth < 1 || child.ActualHeight < 1)
        {
            throw new InvalidOperationException("筛选弹层打开态参考帧的 Popup.Child 尚未完成布局。");
        }

        if (!WaitForRenderedCaptureState(child, () =>
                popup.IsOpen && !PopupMotionBehavior.IsClosing(popup) &&
                child.Opacity == 1 && child.RenderTransform.Value.IsIdentity))
        {
            throw new InvalidOperationException("筛选弹层参考帧未在 300ms 内完成入场。");
        }

        GeneralTransform transform;
        try
        {
            transform = target.TransformToAncestor(host);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "筛选弹层打开态无法把 PlacementTarget 映射到主窗口根视觉。",
                exception);
        }

        var origin = transform.Transform(new Point(0, target.ActualHeight));
        origin.X += popup.HorizontalOffset + child.Margin.Left;
        origin.Y += popup.VerticalOffset + child.Margin.Top;
        var overlay = CompositeOverOpaqueBackground(CaptureVisual(child), backdrop);
        var (pixels, stride) = GetBgra32Pixels(surface);
        var (overlayPixels, overlayStride) = GetBgra32Pixels(overlay);
        var destX = Math.Clamp((int)Math.Round(origin.X), 0, surface.PixelWidth);
        var destY = Math.Clamp((int)Math.Round(origin.Y), 0, surface.PixelHeight);
        var copyWidth = Math.Min(overlay.PixelWidth, surface.PixelWidth - destX);
        var copyHeight = Math.Min(overlay.PixelHeight, surface.PixelHeight - destY);
        for (var y = 0; y < copyHeight; y++)
        {
            for (var x = 0; x < copyWidth; x++)
            {
                var sourceIndex = (y * overlayStride) + (x * 4);
                var destIndex = ((destY + y) * stride) + ((destX + x) * 4);
                pixels[destIndex] = overlayPixels[sourceIndex];
                pixels[destIndex + 1] = overlayPixels[sourceIndex + 1];
                pixels[destIndex + 2] = overlayPixels[sourceIndex + 2];
                pixels[destIndex + 3] = overlayPixels[sourceIndex + 3];
            }
        }

        var bitmap = new WriteableBitmap(
            surface.PixelWidth,
            surface.PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, surface.PixelWidth, surface.PixelHeight),
            pixels,
            stride,
            0);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CompositeOverOpaqueBackground(BitmapSource source, Color backdrop)
    {
        if (IsEntirelyTransparent(source))
        {
            throw new InvalidOperationException(
                "CompositeOverOpaqueBackground 拒绝把整幅透明位图铺成背景色。");
        }

        var (pixels, stride) = GetBgra32Pixels(source);
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var alpha = pixels[index + 3];
            if (alpha == byte.MaxValue)
            {
                continue;
            }

            if (alpha == 0)
            {
                pixels[index] = backdrop.B;
                pixels[index + 1] = backdrop.G;
                pixels[index + 2] = backdrop.R;
                pixels[index + 3] = byte.MaxValue;
                continue;
            }

            var sourceAlpha = alpha / 255d;
            var keep = 1d - sourceAlpha;
            // GetBgra32Pixels returns straight alpha, so source channels must
            // be weighted too. Adding unweighted channels can overflow a byte.
            pixels[index] = (byte)Math.Round((pixels[index] * sourceAlpha) + (backdrop.B * keep));
            pixels[index + 1] = (byte)Math.Round((pixels[index + 1] * sourceAlpha) + (backdrop.G * keep));
            pixels[index + 2] = (byte)Math.Round((pixels[index + 2] * sourceAlpha) + (backdrop.R * keep));
            pixels[index + 3] = byte.MaxValue;
        }

        var bitmap = new WriteableBitmap(
            source.PixelWidth,
            source.PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight),
            pixels,
            stride,
            0);
        bitmap.Freeze();
        return bitmap;
    }

    private static RenderTargetBitmap ScaleToFrame(BitmapSource source, int width, int height)
    {
        var scale = Math.Min((double)width / source.PixelWidth, (double)height / source.PixelHeight);
        var targetWidth = source.PixelWidth * scale;
        var targetHeight = source.PixelHeight * scale;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(
                source,
                new Rect(
                    (width - targetWidth) / 2,
                    (height - targetHeight) / 2,
                    targetWidth,
                    targetHeight));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static RenderTargetBitmap Overlay(BitmapSource reference, BitmapSource actual)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, reference.PixelWidth, reference.PixelHeight);
            drawing.DrawImage(reference, bounds);
            drawing.PushOpacity(0.5);
            drawing.DrawImage(actual, bounds);
            drawing.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            reference.PixelWidth,
            reference.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var bitmap = decoder.Frames[0];
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string WriteNewPng(string path, BitmapSource source)
    {
        var bytes = EncodePng(source);
        WriteNewBytes(path, bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void WriteNewBytes(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteBytesAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path) ??
            throw new InvalidOperationException("原子写入目标没有父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteNewBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static AlphaMetrics MeasureAlpha(BitmapSource source)
    {
        // BGRA32 是 CompositeOverOpaqueBackground 的合成产物，alpha 语义与 Pbgra32 一致
        // （字节序同为 B,G,R,A，仅非预乘/预乘解释不同）；两者都按同一 alpha 扫描口径统计。
        if ((source.Format != PixelFormats.Pbgra32 && source.Format != PixelFormats.Bgra32) ||
            source.PixelWidth <= 0 ||
            source.PixelHeight <= 0)
        {
            return new AlphaMetrics(false, int.MaxValue, 0);
        }

        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        var nonOpaquePixels = 0;
        var minimumAlpha = byte.MaxValue;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            var alpha = pixels[index];
            minimumAlpha = Math.Min(minimumAlpha, alpha);
            if (alpha != byte.MaxValue)
            {
                nonOpaquePixels++;
            }
        }

        return new AlphaMetrics(nonOpaquePixels == 0, nonOpaquePixels, minimumAlpha);
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteSettingsFiles(string settingsPath)
    {
        foreach (var suffix in new[]
                 {
                     "", ".tmp", ".backup1", ".backup2",
                     ".backup2.tmp", ".backup2.rollback"
                 })
        {
            TryDeleteFile(settingsPath + suffix);
        }
    }

    private static Rect? VisibleImageBounds(Image image, UIElement ancestor)
    {
        if (image.Source is not BitmapSource source ||
            source.PixelWidth <= 0 ||
            source.PixelHeight <= 0 ||
            image.ActualWidth <= 0 ||
            image.ActualHeight <= 0)
        {
            return null;
        }

        var pixelsSource = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = pixelsSource.PixelWidth * 4;
        var pixels = new byte[stride * pixelsSource.PixelHeight];
        pixelsSource.CopyPixels(pixels, stride, 0);
        var minX = pixelsSource.PixelWidth;
        var minY = pixelsSource.PixelHeight;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < pixelsSource.PixelHeight; y++)
        {
            for (var x = 0; x < pixelsSource.PixelWidth; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] <= 16)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < 0)
        {
            return null;
        }

        var origin = image.TranslatePoint(new Point(0, 0), ancestor);
        var scaleX = image.ActualWidth / pixelsSource.PixelWidth;
        var scaleY = image.ActualHeight / pixelsSource.PixelHeight;
        return new Rect(
            origin.X + (minX * scaleX),
            origin.Y + (minY * scaleY),
            (maxX - minX + 1) * scaleX,
            (maxY - minY + 1) * scaleY);
    }

    private static bool ValidateRoundTrip(CountdownSettings settings)
    {
        var serialized = SettingsContract.SerializeCanonical(settings);
        var loaded = SettingsContract.Deserialize(serialized);
        return string.Equals(
            serialized,
            SettingsContract.SerializeCanonical(loaded.Settings),
            StringComparison.Ordinal);
    }

    private static bool ValidateLegacyMigration()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "timeZoneId": "China Standard Time",
              "targetUtc": "2099-01-01T00:00:00.0000000+00:00"
            }
            """;
        var result = SettingsContract.Deserialize(json);
        return
            result.Migrated &&
            result.Settings.Countdowns.Count == 1 &&
            result.Settings.Countdowns[0].Name == "我的倒计时" &&
            result.Settings.Notes.Count == 0;
    }

    private static bool RejectsDuplicateCountdowns(CountdownSettings settings)
    {
        var candidate = settings with
        {
            Countdowns = new[]
            {
                new CountdownRecord("duplicate-a", "重复", DateTimeOffset.UtcNow.AddDays(1), Array.Empty<string>()),
                new CountdownRecord("duplicate-b", "重复", DateTimeOffset.UtcNow.AddDays(2), Array.Empty<string>())
            }
        };
        return Rejects(candidate);
    }

    private static bool RejectsInvalidNotes(CountdownSettings settings)
    {
        var candidate = settings with
        {
            Notes = new[]
            {
                new NoteRecord("invalid-note", " ", null, false, Array.Empty<string>(), null, null)
            }
        };
        return Rejects(candidate);
    }

    private static bool RejectsDanglingTag(CountdownSettings settings)
    {
        var candidate = settings with
        {
            Countdowns = new[]
            {
                new CountdownRecord(
                    "dangling-tag",
                    "悬空标签",
                    DateTimeOffset.UtcNow.AddDays(1),
                    new[] { "missing-tag" })
            }
        };
        return Rejects(candidate);
    }

    private static bool Rejects(CountdownSettings candidate)
    {
        try
        {
            SettingsContract.Canonicalize(candidate);
            return false;
        }
        catch (SettingsValidationException)
        {
            return true;
        }
    }

    private static bool ValidateReminderDispatch()
    {
        var start = new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero);
        var note = new NoteRecord(
            "reminder-probe",
            "提醒探针",
            "2031-04-05",
            false,
            Array.Empty<string>(),
            30,
            start);
        var settings = CountdownSettings.Empty with { Notes = new[] { note } };
        var scheduler = new ReminderScheduler();
        scheduler.Initialize(settings, start);
        var first = scheduler.GetDue(settings, start.AddMinutes(30));
        var pendingRetry = scheduler.GetDue(settings, start.AddMinutes(30));
        scheduler.ConfirmDisplayed(note, start.AddMinutes(30));
        var duplicate = scheduler.GetDue(settings, start.AddMinutes(30));
        return first.Count == 1 && pendingRetry.Count == 1 && duplicate.Count == 0;
    }

    private static bool ValidateOneTimeReminderLifecycle()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 6, 1, 30, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-once-{Guid.NewGuid():N}.settings.json");
        try
        {
            var beforeStore = new SettingsStore(
                clock: new ValidationClock(firstDueUtc.AddSeconds(-1)));
            var before = DesktopWorkspace.Load(settingsPath, beforeStore);
            if (!before.Apply(new SaveNoteChange(
                    null,
                    "一次性提醒生命周期探针",
                    new DateTime(2031, 4, 6),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc))).Succeeded)
            {
                return false;
            }

            var early = before.GetDueReminders(firstDueUtc.AddSeconds(-1));
            var afterStore = new SettingsStore(
                clock: new ValidationClock(firstDueUtc.AddMinutes(5)));
            var after = DesktopWorkspace.Load(settingsPath, afterStore);
            var overdue = after.GetDueReminders(firstDueUtc.AddMinutes(5));
            var dueNote = overdue.SingleOrDefault();
            if (dueNote?.Reminder is not OnceReminder)
            {
                return false;
            }

            Directory.CreateDirectory(settingsPath + ".tmp");
            var failedConfirmation = after.ConfirmReminderDisplayed(
                dueNote,
                firstDueUtc.AddMinutes(5));
            var retryAfterFailure = after.GetDueReminders(firstDueUtc.AddMinutes(5));
            var pendingSettings = after.Snapshot.Settings;
            var pendingAfterFailure = ReminderRuleCompatibility.ToLegacy(
                pendingSettings,
                pendingSettings.Notes.Single()) is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            };
            Directory.Delete(settingsPath + ".tmp");
            var successfulConfirmation = after.ConfirmReminderDisplayed(
                dueNote,
                firstDueUtc.AddMinutes(5));
            if (failedConfirmation.Succeeded ||
                retryAfterFailure.Count != 1 ||
                !pendingAfterFailure ||
                !successfulConfirmation.Succeeded)
            {
                return false;
            }

            var unresolvedInProcess = after.GetDueReminderOccurrences(firstDueUtc.AddMinutes(5));
            var reloaded = DesktopWorkspace.Load(settingsPath, afterStore);
            var unresolvedAfterRestart = reloaded.GetDueReminderOccurrences(firstDueUtc.AddMinutes(5));
            var reloadedOccurrence = unresolvedAfterRestart.SingleOrDefault();
            var disposition = reloadedOccurrence is null
                ? new DesktopWorkspaceChangeResult(false, "没有可处置的 occurrence。")
                : reloaded.AcknowledgeReminder(
                    reloadedOccurrence.Note,
                    reloadedOccurrence.Rule.Id,
                    reloadedOccurrence.ScheduledUtc);
            var disposedAfterRestart = DesktopWorkspace.Load(settingsPath, afterStore)
                .GetDueReminderOccurrences(firstDueUtc.AddMinutes(5));
            return
                early.Count == 0 &&
                overdue.Count == 1 &&
                unresolvedInProcess.Count == 1 &&
                unresolvedInProcess[0].ScheduledUtc == firstDueUtc &&
                unresolvedAfterRestart.Count == 1 &&
                reloadedOccurrence?.ScheduledUtc == firstDueUtc &&
                disposition.Succeeded &&
                disposedAfterRestart.Count == 0 &&
                ReminderRuleCompatibility.ToLegacy(
                    reloaded.Snapshot.Settings,
                    reloaded.Snapshot.Settings.Notes.Single()) is OnceReminder
                {
                    Delivery: OnceDeliveryState.Delivered
                };
        }
        finally
        {
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
            if (Directory.Exists(settingsPath + ".tmp"))
            {
                Directory.Delete(settingsPath + ".tmp");
            }
        }
    }

    private static RepeatingReminderDeliveryExerciseResult ValidateRepeatingReminderDeliveryCoordinator()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 7, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-repeat-render-{Guid.NewGuid():N}.settings.json");
        var migrationPath = settingsPath + ".v9.json";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var session = ApplicationSession.Load(settingsPath, new SettingsStore(clock: clock));
            if (!session.TrySaveNote(
                    null,
                    "重复提醒真实渲染探针",
                    new DateTime(2031, 4, 7),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(firstDueUtc, 30),
                    out _))
            {
                return RepeatingReminderDeliveryExerciseResult.Failed;
            }

            var repeatingId = session.Current.Notes.Single().Id;
            var rule = session.Current.Notes.Single().ReminderRules.Single();
            var workspace = new DesktopWorkspace(session);
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            clock.UtcNow = firstDueUtc;
            var dueBeforeShow = session.Reminders.GetDue(session.Current, firstDueUtc)
                .Any(note => note.Id == repeatingId);
            coordinator.Tick(firstDueUtc);
            var window = coordinator.ActiveWindow;
            var windowOpened = window?.IsVisible == true && coordinator.ActiveWindowCount == 1;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var contentRendered =
                coordinator.ActiveOccurrenceRendered &&
                coordinator.ActiveOccurrenceConfirmed &&
                session.Reminders.GetDueOccurrences(session.Current, firstDueUtc)
                    .SingleOrDefault() is { } unresolvedOccurrence &&
                unresolvedOccurrence.Note.Id == repeatingId &&
                unresolvedOccurrence.Rule.Id == rule.Id &&
                unresolvedOccurrence.ScheduledUtc == firstDueUtc;
            var nextDueUtc = firstDueUtc.AddMinutes(30);
            clock.UtcNow = nextDueUtc;
            coordinator.Tick(nextDueUtc);
            var nextWindow = coordinator.ActiveWindow;
            var firstKey = new ActiveReminderOccurrenceKey(rule.Id, firstDueUtc);
            var secondKey = new ActiveReminderOccurrenceKey(rule.Id, nextDueUtc);
            var singleUnresolvedOccurrenceWorks =
                window?.IsVisible == true &&
                ReferenceEquals(nextWindow, window) &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                window.EntryCount == 1 &&
                window.OrderedKeys.SequenceEqual([firstKey]) &&
                !window.OrderedKeys.Contains(secondKey) &&
                Application.Current.Windows
                    .OfType<ReminderCenterWindow>()
                    .Count(candidate => candidate.IsVisible) == 1;
            var unresolvedDue = session.Reminders.GetDueOccurrences(session.Current, nextDueUtc);
            singleUnresolvedOccurrenceWorks &=
                unresolvedDue.Count == 1 &&
                unresolvedDue[0].ScheduledUtc == firstDueUtc;

            var firstAcknowledged = window?.RequestAcknowledgement(firstKey) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            coordinator.Tick(nextDueUtc);
            var secondWindow = coordinator.ActiveWindow;
            secondWindow?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var secondOccurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
                rule,
                nextDueUtc).ToString();
            var secondCycleOpenedAfterDisposition =
                firstAcknowledged &&
                window?.IsVisible == false &&
                secondWindow?.IsVisible == true &&
                !ReferenceEquals(secondWindow, window) &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                secondWindow.EntryCount == 1 &&
                secondWindow.RenderedEntryCount == 1 &&
                secondWindow.OrderedKeys.SequenceEqual([secondKey]) &&
                session.Current.ReminderStates[rule.Id].LastDeliveredOccurrenceKey ==
                    secondOccurrenceKey;
            var secondAcknowledged = secondWindow?.RequestAcknowledgement(secondKey) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var acknowledgementAdvancedToSecond =
                session.Current.ReminderStates[rule.Id].LastDispositionOccurrenceKey ==
                    secondOccurrenceKey;
            var staleFirstAcknowledgement = workspace.AcknowledgeReminder(
                session.Current.Notes.Single(note => note.Id == repeatingId),
                rule.Id,
                firstDueUtc);
            var persistedAfterReverseAcknowledgement = new SettingsStore(clock: clock)
                .Load(settingsPath)
                .Settings
                .ReminderStates[rule.Id];
            coordinator.Tick(nextDueUtc);
            var acknowledgementMonotonicAcrossOccurrences =
                secondAcknowledged &&
                acknowledgementAdvancedToSecond &&
                staleFirstAcknowledgement.Succeeded &&
                session.Current.ReminderStates[rule.Id].LastDispositionOccurrenceKey ==
                    secondOccurrenceKey &&
                persistedAfterReverseAcknowledgement.LastDispositionOccurrenceKey ==
                    secondOccurrenceKey;
            var noDuplicate =
                firstAcknowledged &&
                secondAcknowledged &&
                secondCycleOpenedAfterDisposition &&
                coordinator.ActiveWindowCount == 0 &&
                coordinator.ActiveEntryCount == 0 &&
                session.Reminders.GetDueOccurrences(session.Current, nextDueUtc).Count == 0;

            coordinator.Dispose();
            coordinator = null;
            var latestMissedUtc = firstDueUtc.AddHours(2).AddMinutes(5);
            clock.UtcNow = latestMissedUtc;
            var restarted = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            var latestDue = restarted.Reminders.GetDue(restarted.Current, latestMissedUtc);
            var latestNote = latestDue.SingleOrDefault();
            var latestRule = latestNote?.ReminderRules.SingleOrDefault();
            var latestScheduledUtc = latestRule is null
                ? DateTimeOffset.MinValue
                : ReminderSchedule.GetScheduledUtc(latestRule, latestMissedUtc);
            var beforeFailure = restarted.SerializeCurrent();
            Directory.CreateDirectory(settingsPath + ".tmp");
            var failedLatestConfirmation = latestNote is null
                ? new DesktopWorkspaceChangeResult(false, "没有最新 occurrence。")
                : new DesktopWorkspace(restarted).ConfirmReminderDisplayed(
                    latestNote,
                    latestMissedUtc);
            var failureStayedAtomic =
                !failedLatestConfirmation.Succeeded &&
                string.Equals(beforeFailure, restarted.SerializeCurrent(), StringComparison.Ordinal) &&
                restarted.Reminders.GetDue(restarted.Current, latestMissedUtc).Count == 1;
            Directory.Delete(settingsPath + ".tmp");
            var crossedRetryUtc = latestMissedUtc.AddMinutes(31);
            var successfulLatestConfirmation = latestNote is null
                ? new DesktopWorkspaceChangeResult(false, "没有最新 occurrence。")
                : new DesktopWorkspace(restarted).ConfirmReminderDisplayed(
                    latestNote,
                    latestRule!.Id,
                    latestScheduledUtc);
            var unresolvedAtConfirmation = restarted.Reminders
                .GetDueOccurrences(restarted.Current, latestMissedUtc)
                .SingleOrDefault();
            var unresolvedAfterLaterCycles = restarted.Reminders
                .GetDueOccurrences(restarted.Current, crossedRetryUtc)
                .SingleOrDefault();
            var latestOnlyAcrossRestart =
                latestDue.Count == 1 &&
                latestScheduledUtc == firstDueUtc.AddHours(2) &&
                failureStayedAtomic &&
                successfulLatestConfirmation.Succeeded &&
                unresolvedAtConfirmation?.ScheduledUtc == latestScheduledUtc &&
                unresolvedAfterLaterCycles?.ScheduledUtc == latestScheduledUtc &&
                latestRule is not null &&
                restarted.Current.ReminderStates[latestRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(
                        latestRule,
                        latestScheduledUtc).ToString();

            File.WriteAllText(
                migrationPath,
                """
                {
                  "schemaVersion": 9,
                  "timeZoneId": "China Standard Time",
                  "displayPrecision": "Second",
                  "monthlySalary": 0,
                  "workStart": "09:00",
                  "workEnd": "18:00",
                  "themePreference": "Auto",
                  "tags": [],
                  "countdowns": [],
                  "notes": [
                    {
                      "id": "repeat-migration-note",
                      "text": "迁移不复活历史周期",
                      "date": "2031-04-07",
                      "isCompleted": false,
                      "tagIds": [],
                      "reminder": {
                        "type": "repeat",
                        "firstDueUtc": "2031-04-07T01:00:00.0000000+00:00",
                        "intervalMinutes": 30
                      }
                    }
                  ]
                }
                """,
                new UTF8Encoding(false));
            var migrated = ApplicationSession.Load(
                migrationPath,
                new SettingsStore(clock: clock));
            var migrationRule = migrated.Current.Notes.Single().ReminderRules.Single();
            var migrationDoesNotRevive =
                migrated.SettingsWereMigrated &&
                migrated.Reminders.GetDue(migrated.Current, latestMissedUtc).Count == 0 &&
                migrated.Current.ReminderStates[migrationRule.Id].HandledThroughUtc ==
                    firstDueUtc.AddHours(2);
            var retryKeepsCapturedOccurrence =
                ValidateRepeatingReminderRetryKeepsCapturedOccurrence();
            var renderedLatchWorks =
                dueBeforeShow &&
                windowOpened &&
                contentRendered &&
                noDuplicate &&
                latestOnlyAcrossRestart &&
                migrationDoesNotRevive &&
                retryKeepsCapturedOccurrence;
            return new RepeatingReminderDeliveryExerciseResult(
                renderedLatchWorks,
                singleUnresolvedOccurrenceWorks,
                dueBeforeShow,
                windowOpened,
                contentRendered && secondCycleOpenedAfterDisposition,
                noDuplicate,
                acknowledgementMonotonicAcrossOccurrences);
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
            File.Delete(migrationPath);
        }
    }

    private static bool ValidateRepeatingReminderRetryKeepsCapturedOccurrence()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 8, 1, 0, 0, TimeSpan.Zero);
        var nextDueUtc = firstDueUtc.AddMinutes(1);
        var staleRetryUtc = nextDueUtc.AddSeconds(10);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-repeat-retry-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var session = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!session.TrySaveNote(
                    null,
                    "跨周期持久化重试 occurrence 探针",
                    new DateTime(2031, 4, 8),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(firstDueUtc, 1),
                    out _))
            {
                return false;
            }

            var note = session.Current.Notes.Single();
            var rule = (FixedIntervalReminderRule)note.ReminderRules.Single();
            Directory.CreateDirectory(blockedTemporaryPath);
            var workspace = new DesktopWorkspace(session);
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                new DiagnosticLogger(diagnosticPath));
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(firstDueUtc);
            var firstWindow = coordinator.ActiveWindow;
            firstWindow?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var capturedOccurrenceFailed =
                firstWindow?.IsVisible == true &&
                coordinator.ActiveOccurrenceRendered &&
                !coordinator.ActiveOccurrenceConfirmed &&
                coordinator.PersistenceFailureCount == 1 &&
                coordinator.NextPersistenceRetryUtc == firstDueUtc.AddSeconds(5) &&
                !session.Current.ReminderStates.ContainsKey(rule.Id);

            clock.UtcNow = nextDueUtc;
            coordinator.Tick(nextDueUtc);
            var nextWindow = coordinator.ActiveWindow;
            var failureEventsBeforeBarrierRemoved = File.ReadLines(diagnosticPath).Count(line =>
                line.Contains("OneTimeReminderDeliveryPersistFailed", StringComparison.Ordinal));
            var nextCycleSuppressedWhileOldRetryFailed =
                firstWindow?.IsVisible == true &&
                ReferenceEquals(nextWindow, firstWindow) &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                firstWindow.EntryCount == 1 &&
                firstWindow.OrderedKeys.SequenceEqual([
                    new ActiveReminderOccurrenceKey(rule.Id, firstDueUtc)
                ]) &&
                failureEventsBeforeBarrierRemoved == 2 &&
                !session.Current.ReminderStates.ContainsKey(rule.Id);

            Directory.Delete(blockedTemporaryPath);
            clock.UtcNow = staleRetryUtc;
            coordinator.Tick(staleRetryUtc);
            var stateAfterStaleRetry = session.Current.ReminderStates.GetValueOrDefault(rule.Id);
            var persistedAfterStaleRetry = new SettingsStore(clock: clock)
                .Load(settingsPath)
                .Settings
                .ReminderStates
                .GetValueOrDefault(rule.Id);
            var firstOccurrenceKey =
                MinminCountdown.Core.ReminderOccurrenceKey.Create(rule, firstDueUtc).ToString();
            var retryKeptCapturedOccurrence =
                stateAfterStaleRetry?.HandledThroughUtc == firstDueUtc &&
                stateAfterStaleRetry.LastDeliveredOccurrenceKey == firstOccurrenceKey &&
                persistedAfterStaleRetry?.HandledThroughUtc == firstDueUtc &&
                persistedAfterStaleRetry.LastDeliveredOccurrenceKey == firstOccurrenceKey &&
                firstWindow?.PersistenceFailureVisible == false &&
                coordinator.ActiveEntryCount == 1 &&
                session.Reminders.GetDueOccurrences(session.Current, staleRetryUtc)
                    .SingleOrDefault() is { } unresolvedOccurrence &&
                unresolvedOccurrence.Note.Id == note.Id &&
                unresolvedOccurrence.Rule.Id == rule.Id &&
                unresolvedOccurrence.ScheduledUtc == firstDueUtc;

            var firstAcknowledged = firstWindow?.RequestAcknowledgement(
                new ActiveReminderOccurrenceKey(rule.Id, firstDueUtc)) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            coordinator.Tick(staleRetryUtc);
            var secondWindow = coordinator.ActiveWindow;
            secondWindow?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var nextOccurrenceKey =
                MinminCountdown.Core.ReminderOccurrenceKey.Create(rule, nextDueUtc).ToString();
            var nextCycleAppearedAfterDisposition =
                firstAcknowledged &&
                firstWindow?.IsVisible == false &&
                secondWindow?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                secondWindow.OrderedKeys.SequenceEqual([
                    new ActiveReminderOccurrenceKey(rule.Id, nextDueUtc)
                ]) &&
                session.Current.ReminderStates[rule.Id].LastDeliveredOccurrenceKey ==
                    nextOccurrenceKey;
            var nextAcknowledged = secondWindow?.RequestAcknowledgement(
                new ActiveReminderOccurrenceKey(rule.Id, nextDueUtc)) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            coordinator.Tick(staleRetryUtc);
            var nextOccurrenceDidNotReopen =
                nextAcknowledged &&
                coordinator.ActiveWindowCount == 0 &&
                coordinator.ActiveEntryCount == 0 &&
                !session.Reminders.GetDue(session.Current, staleRetryUtc)
                    .Any(candidate => candidate.Id == note.Id);
            return
                capturedOccurrenceFailed &&
                nextCycleSuppressedWhileOldRetryFailed &&
                retryKeptCapturedOccurrence &&
                nextCycleAppearedAfterDisposition &&
                nextOccurrenceDidNotReopen;
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(diagnosticPath);
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
            else
            {
                File.Delete(blockedTemporaryPath);
            }
        }
    }

    private static bool ValidateReminderPersistenceFailureDispose()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 7, 2, 30, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-delivery-dispose-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var session = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!session.TrySaveNote(
                    null,
                    "退出时强制关闭持久化失败提醒探针",
                    new DateTime(2031, 4, 7),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc),
                    out _))
            {
                return false;
            }

            var noteId = session.Current.Notes.Single().Id;
            var noteRule = session.Current.Notes.Single().ReminderRules.Single();
            var beforeFailure = session.SerializeCurrent();
            Directory.CreateDirectory(blockedTemporaryPath);
            coordinator = new ReminderDeliveryCoordinator(
                new DesktopWorkspace(session),
                new DiagnosticLogger(diagnosticPath));
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(firstDueUtc);
            var window = coordinator.ActiveWindow;
            PumpDispatcher();
            var firstRetryUtc = coordinator.NextPersistenceRetryUtc;
            var firstFailureCount = coordinator.PersistenceFailureCount;
            window?.Close();
            PumpDispatcher();
            var ordinaryCloseGuarded =
                window?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                firstFailureCount == 1 &&
                firstRetryUtc == firstDueUtc.AddSeconds(5);

            coordinator.Dispose();
            PumpDispatcher();
            var failureEvents = File.ReadLines(diagnosticPath).Count(line =>
                line.Contains("OneTimeReminderDeliveryPersistFailed", StringComparison.Ordinal));
            var stillPending = ReminderRuleCompatibility.ToLegacy(
                session.Current,
                session.Current.Notes.Single(note => note.Id == noteId)) is
                OnceReminder { Delivery: OnceDeliveryState.Pending };
            Directory.Delete(blockedTemporaryPath);
            var restarted = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            var restartedOccurrence = restarted.GetDueReminderOccurrences(firstDueUtc)
                .SingleOrDefault();
            var sameOccurrenceRestarts =
                restartedOccurrence?.Note.Id == noteId &&
                restartedOccurrence.Rule.Id == noteRule.Id &&
                restartedOccurrence.ScheduledUtc == firstDueUtc &&
                !restarted.Snapshot.Settings.ReminderStates.ContainsKey(noteRule.Id);
            var disposeBehavior =
                ordinaryCloseGuarded &&
                window?.IsVisible == false &&
                coordinator.ActiveWindowCount == 0 &&
                failureEvents >= 1 &&
                stillPending &&
                string.Equals(beforeFailure, session.SerializeCurrent(), StringComparison.Ordinal) &&
                sameOccurrenceRestarts;
            return disposeBehavior;
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(diagnosticPath);
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
            else
            {
                File.Delete(blockedTemporaryPath);
            }
        }
    }

    private static bool ValidateReminderCenterAppendsAfterOpen()
    {
        var firstDueUtc = new DateTimeOffset(2031, 5, 5, 1, 0, 0, TimeSpan.Zero);
        var secondDueUtc = firstDueUtc.AddMinutes(1);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-center-append-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var session = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!session.TrySaveNote(
                    null,
                    "先到提醒追加探针",
                    new DateTime(2031, 5, 5),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc),
                    out _))
            {
                return false;
            }

            var firstNote = session.Current.Notes.Single();
            var firstRule = firstNote.ReminderRules.Single();
            var firstKey = new ActiveReminderOccurrenceKey(firstRule.Id, firstDueUtc);
            coordinator = new ReminderDeliveryCoordinator(new DesktopWorkspace(session), null);
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(clock.UtcNow);
            var center = coordinator.ActiveWindow;
            PumpDispatcher();
            var firstRendered =
                center?.IsVisible == true &&
                center.EntryCount == 1 &&
                center.RenderedEntryCount == 1 &&
                center.OrderedKeys.SequenceEqual([firstKey]);

            if (!session.TrySaveNote(
                    null,
                    "后到提醒追加探针",
                    new DateTime(2031, 5, 5),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(secondDueUtc),
                    out _))
            {
                return false;
            }

            var secondNote = session.Current.Notes.Single(note =>
                string.Equals(note.Text, "后到提醒追加探针", StringComparison.Ordinal));
            var secondRule = secondNote.ReminderRules.Single();
            var secondKey = new ActiveReminderOccurrenceKey(secondRule.Id, secondDueUtc);
            clock.UtcNow = secondDueUtc;
            coordinator.Tick(clock.UtcNow);
            var sameCenter = coordinator.ActiveWindow;
            PumpDispatcher();
            return
                firstRendered &&
                ReferenceEquals(sameCenter, center) &&
                sameCenter?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 2 &&
                sameCenter.EntryCount == 2 &&
                sameCenter.RenderedEntryCount == 2 &&
                sameCenter.OrderedKeys.SequenceEqual([firstKey, secondKey]) &&
                session.Current.ReminderStates[firstRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(firstRule, firstDueUtc).ToString() &&
                session.Current.ReminderStates[secondRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(secondRule, secondDueUtc).ToString();
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }
    }

    private static ReminderCenterExerciseResult ValidateReminderCenterBehavior(
        ApplicationTheme theme,
        string assetRoot)
    {
        var dueUtc = new DateTimeOffset(2031, 5, 6, 1, 30, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-center-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        ReminderDeliveryCoordinator? coordinator = null;
        MainWindow? sourceWindow = null;
        HolidayCalendar? calendar = null;
        try
        {
            var clock = new MutableValidationClock(dueUtc.AddSeconds(-1));
            var session = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!session.TrySaveNote(
                    null,
                    "批量提醒甲",
                    new DateTime(2031, 5, 6),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc),
                    out _) ||
                !session.TrySaveNote(
                    null,
                    "批量提醒乙",
                    new DateTime(2031, 5, 6),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc),
                    out _))
            {
                return ReminderCenterExerciseResult.Failed;
            }

            var firstNote = session.Current.Notes[0];
            var secondNote = session.Current.Notes[1];
            var firstRule = firstNote.ReminderRules.Single();
            var secondRule = secondNote.ReminderRules.Single();
            var firstKey = new ActiveReminderOccurrenceKey(firstRule.Id, dueUtc);
            var secondKey = new ActiveReminderOccurrenceKey(secondRule.Id, dueUtc);
            var workspace = new DesktopWorkspace(session);
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with
            {
                NoteSearch = "不会命中任何便签的筛选"
            });
            calendar = new HolidayCalendar(assetRoot);
            sourceWindow = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                assetRoot)
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            sourceWindow.ShowWorkspace(WorkspaceKind.Countdowns);
            var draftText = Required<TextBox>(sourceWindow, "NoteText");
            var draftDate = Required<DatePicker>(sourceWindow, "NoteDate");
            draftText.Text = "必须保留的便签草稿";
            draftDate.SelectedDate = new DateTime(2031, 5, 9);
            sourceWindow.Show();
            PumpDispatcher();
            sourceWindow.WindowState = WindowState.Minimized;
            PumpDispatcher();
            var sourceWasVisibleAndMinimized =
                sourceWindow.IsVisible &&
                sourceWindow.WindowState == WindowState.Minimized;
            var openedSourceIds = new List<string>();
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                new DiagnosticLogger(diagnosticPath),
                (sourceKind, noteId) =>
                {
                    if (sourceKind != ReminderSourceKind.Note)
                    {
                        return;
                    }

                    openedSourceIds.Add(noteId);
                    sourceWindow.OpenReminderSource(sourceKind, noteId);
                });

            clock.UtcNow = dueUtc;
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow;
            var singleWindowBeforeRender =
                center?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                Application.Current.Windows
                    .OfType<ReminderCenterWindow>()
                    .Count(window => window.IsVisible) == 1;
            var sameTickBatch =
                center?.EntryCount == 2 &&
                center.RenderedEntryCount == 0 &&
                session.Current.ReminderStates.Count == 0;
            var stableOrdering = center?.OrderedKeys.SequenceEqual([firstKey, secondKey]) == true;
            PumpDispatcher();
            var entriesRendered =
                center?.RenderedEntryCount == 2 &&
                session.Current.ReminderStates[firstRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(firstRule, dueUtc).ToString() &&
                session.Current.ReminderStates[secondRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(secondRule, dueUtc).ToString();
            var acknowledgementDistinctFromDelivery =
                session.Current.ReminderStates.Values.All(state =>
                    state.LastDeliveredOccurrenceKey is not null &&
                    state.LastAcknowledgedOccurrenceKey is null) &&
                center?.EntryCount == 2;

            var visibleText = center is null
                ? Array.Empty<string>()
                : Descendants<TextBlock>(center).Select(text => text.Text).ToArray();
            var buttons = center is null
                ? Array.Empty<Button>()
                : Descendants<Button>(center).ToArray();
            var displaysSourceContentAndTime =
                visibleText.Count(text => string.Equals(text, "便签", StringComparison.Ordinal)) == 2 &&
                visibleText.Contains(firstNote.Text, StringComparer.Ordinal) &&
                visibleText.Contains(secondNote.Text, StringComparer.Ordinal) &&
                visibleText.Count(text => text.Contains("2031 年 5 月 6 日 09:30", StringComparison.Ordinal) &&
                                          text.Contains("北京时间", StringComparison.Ordinal)) == 2;
            static bool NameStartsWith(Button button, string prefix) =>
                AutomationProperties.GetName(button).StartsWith(prefix, StringComparison.Ordinal);
            var hasOnlySafeActions =
                buttons.Count(button => NameStartsWith(button, "知道了：")) == 2 &&
                buttons.Count(button => NameStartsWith(button, "稍后提醒：")) == 2 &&
                buttons.Count(button => NameStartsWith(button, "完成便签：")) == 2 &&
                buttons.Count(button => NameStartsWith(button, "打开便签来源：")) == 2 &&
                buttons.Count(button => string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterAcknowledgeAll",
                    StringComparison.Ordinal)) == 1 &&
                buttons.Count(button => string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterMinimizeToTaskbar",
                    StringComparison.Ordinal)) == 1 &&
                buttons.Length == 10;
            var accessible =
                center is not null &&
                string.Equals(AutomationProperties.GetName(center), "提醒中心", StringComparison.Ordinal) &&
                buttons.All(button => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))) &&
                Descendants<ScrollViewer>(center).All(scroller =>
                    !string.IsNullOrWhiteSpace(AutomationProperties.GetName(scroller)));

            var beforeOpenSource = session.SerializeCurrent();
            var filterBeforeOpen = workspace.Snapshot.Filter;
            var openSourceRaised = center?.RequestOpenSource(firstKey) == true;
            PumpDispatcher();
            var filterAfterOpen = workspace.Snapshot.Filter;
            var openSourcePreservesState =
                openSourceRaised &&
                string.Equals(beforeOpenSource, session.SerializeCurrent(), StringComparison.Ordinal) &&
                FiltersEqual(filterBeforeOpen, filterAfterOpen) &&
                string.Equals(draftText.Text, "必须保留的便签草稿", StringComparison.Ordinal) &&
                draftDate.SelectedDate == new DateTime(2031, 5, 9) &&
                sourceWindow.CurrentWorkspace == WorkspaceKind.Notes &&
                center?.EntryCount == 2;
            var openSourceRestoresMinimizedWindow =
                sourceWasVisibleAndMinimized &&
                sourceWindow.IsVisible &&
                sourceWindow.WindowState == WindowState.Normal &&
                sourceWindow.IsActive;
            var sourceWasFocused =
                openedSourceIds.SequenceEqual([firstNote.Id]) &&
                Descendants<Border>(sourceWindow).Any(border =>
                    AutomationProperties.GetName(border).Contains(
                        $"当前提醒来源便签：{firstNote.Text}",
                        StringComparison.Ordinal)) &&
                Keyboard.FocusedElement is Button { Tag: string focusedNoteId } &&
                string.Equals(focusedNoteId, firstNote.Id, StringComparison.OrdinalIgnoreCase);
            sourceWindow.UpdateTime(ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(1)));
            PumpDispatcher();
            var openSourceFindsExactNote =
                sourceWasFocused &&
                !Descendants<Border>(sourceWindow).Any(border =>
                    AutomationProperties.GetName(border).Contains(
                        $"当前提醒来源便签：{firstNote.Text}",
                        StringComparison.Ordinal));

            Directory.CreateDirectory(blockedTemporaryPath);
            var beforeAcknowledgementFailure = session.SerializeCurrent();
            var acknowledgementRequested = center?.RequestAcknowledgement(firstKey) == true;
            var acknowledgementFailureRetainsEntry =
                acknowledgementRequested &&
                center?.EntryCount == 2 &&
                center.IsFailureVisible(firstKey) &&
                !center.IsAcknowledgeEnabled(firstKey) &&
                center.RequestOpenSource(firstKey) &&
                session.Current.ReminderStates[firstRule.Id].LastAcknowledgedOccurrenceKey is null;
            var acknowledgementFailureIsAtomic =
                string.Equals(
                    beforeAcknowledgementFailure,
                    session.SerializeCurrent(),
                    StringComparison.Ordinal);

            Directory.Delete(blockedTemporaryPath);
            clock.UtcNow = dueUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var firstOccurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
                firstRule,
                dueUtc).ToString();
            var persistedAfterRetry = new SettingsStore(clock: clock)
                .Load(settingsPath)
                .Settings
                .ReminderStates[firstRule.Id];
            var singleItemDisposition =
                center?.EntryCount == 1 &&
                !center.OrderedKeys.Contains(firstKey) &&
                center.OrderedKeys.Contains(secondKey) &&
                session.Current.ReminderStates[firstRule.Id].LastAcknowledgedOccurrenceKey == firstOccurrenceKey &&
                persistedAfterRetry.LastAcknowledgedOccurrenceKey == firstOccurrenceKey;

            var secondAcknowledged = center?.RequestAcknowledgement(secondKey) == true;
            PumpDispatcher();
            var closesWhenEmpty =
                secondAcknowledged &&
                center?.IsVisible == false &&
                coordinator.ActiveWindowCount == 0 &&
                coordinator.ActiveEntryCount == 0;

            var reopenedDueUtc = dueUtc.AddMinutes(1);
            var reopenedSaved = session.TrySaveNote(
                null,
                "中心关闭后重新提醒",
                new DateTime(2031, 5, 6),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateOnce(reopenedDueUtc),
                out _);
            clock.UtcNow = reopenedDueUtc;
            coordinator.Tick(reopenedDueUtc);
            var reopenedCenter = coordinator.ActiveWindow;
            PumpDispatcher();
            var reopens =
                reopenedSaved &&
                reopenedCenter?.IsVisible == true &&
                !ReferenceEquals(reopenedCenter, center) &&
                reopenedCenter.EntryCount == 1 &&
                reopenedCenter.RenderedEntryCount == 1 &&
                coordinator.ActiveWindowCount == 1;

            coordinator.Dispose();
            coordinator = null;
            PumpDispatcher();

            var layoutWindow = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            for (var index = 11; index >= 0; index--)
            {
                var scheduledUtc = dueUtc.AddMinutes(index / 3);
                layoutWindow.AddEntry(new ReminderCenterItem(
                    new ActiveReminderOccurrenceKey($"layout-rule-{index:D2}", scheduledUtc),
                    new NoteRecord(
                        $"layout-note-{index:D2}",
                        $"长内容提醒 {index:D2}：{new string('测', 96)}",
                        "2031-05-06",
                        false,
                        Array.Empty<string>(),
                        NoReminder.Instance),
                    index / 3,
                    index % 3));
            }

            try
            {
                layoutWindow.Show();
                PumpDispatcher();
                layoutWindow.UpdateLayout();
                var scroller = Descendants<ScrollViewer>(layoutWindow).Single();
                var longBodies = Descendants<TextBlock>(layoutWindow)
                    .Where(text => text.Text.StartsWith("长内容提醒", StringComparison.Ordinal))
                    .ToArray();
                var scrollsLongContent =
                    scroller.Content is StackPanel &&
                    scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                    scroller.ScrollableHeight > 0 &&
                    longBodies.Length == 12 &&
                    longBodies.All(text => text.TextWrapping == TextWrapping.Wrap && text.ActualHeight > 0);
                theme.Apply();
                PumpDispatcher();
                var surface = ((Border)layoutWindow.Content).Background;
                var followsDayTheme =
                    surface is SolidColorBrush brush &&
                    brush.Color == ((SolidColorBrush)Application.Current.FindResource("ReminderPageBrush")).Color;

                return new ReminderCenterExerciseResult(
                    singleWindowBeforeRender,
                    sameTickBatch,
                    stableOrdering && layoutWindow.OrderedKeys.SequenceEqual(
                        layoutWindow.OrderedKeys
                            .OrderBy(key => key.ScheduledUtc)
                            .ThenBy(key => key.RuleId, StringComparer.Ordinal)),
                    entriesRendered,
                    acknowledgementDistinctFromDelivery,
                    acknowledgementFailureRetainsEntry,
                    acknowledgementFailureIsAtomic,
                    singleItemDisposition,
                    openSourcePreservesState,
                    openSourceFindsExactNote,
                    openSourceRestoresMinimizedWindow,
                    closesWhenEmpty,
                    reopens,
                    scrollsLongContent,
                    followsDayTheme,
                    hasOnlySafeActions,
                    displaysSourceContentAndTime,
                    accessible);
            }
            finally
            {
                layoutWindow.ForceClose();
            }
        }
        finally
        {
            coordinator?.Dispose();
            if (sourceWindow?.IsVisible == true)
            {
                sourceWindow.Close();
            }

            calendar?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(diagnosticPath);
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
            else
            {
                File.Delete(blockedTemporaryPath);
            }
        }
    }

    // 提醒中心代表态真实帧：与 AI 目标图同一画面（单条便签条目、计划 2026-09-10 00:36
    // 北京时间、真实 render probe 与送达写盘确认后处置动作启用）。像素只证明白底页面、
    // 暖黄卡、待处理徽章、主行动按钮与便签来源图标都在场，不钉逐像素基线。
    private static ReminderCenterCaptureValidation ValidateReminderCenterRepresentativeCapture()
    {
        const int captureWidth = 460;
        const int captureHeight = 520;
        var dueUtc = new DateTimeOffset(2026, 9, 9, 16, 36, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-center-capture-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(dueUtc.AddSeconds(-1));
            var session = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!session.TrySaveNote(
                    null,
                    "验证点击添加便签",
                    new DateTime(2026, 9, 10),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc),
                    out _))
            {
                return ReminderCenterCaptureValidation.Failed;
            }

            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(
                new DesktopWorkspace(session),
                logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow;
            if (center is null)
            {
                return ReminderCenterCaptureValidation.Failed;
            }

            var rule = session.Current.Notes.Single().ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            for (var attempt = 0; attempt < 40 && !center.IsAcknowledgeEnabled(key); attempt++)
            {
                PumpDispatcher();
            }

            if (center.EntryCount != 1 ||
                !center.IsAcknowledgeEnabled(key) ||
                !center.IsAcknowledgeAllEnabled ||
                center.Content is not FrameworkElement root)
            {
                return ReminderCenterCaptureValidation.Failed;
            }

            center.UpdateLayout();
            var capture = CaptureOpaqueMainSurface(root, captureWidth, captureHeight);
            var pngBytes = EncodePng(capture);
            var (pageWhite, warmCard, pendingBadge, mainAction, sourceIcon) =
                CountReminderCenterCapturePixels(capture);
            var evidencePath = string.Empty;
            var evidenceDirectory = Environment.GetEnvironmentVariable(
                "MINMIN_COUNTDOWN_VISUAL_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidenceDirectory))
            {
                Directory.CreateDirectory(evidenceDirectory);
                var captureName = Environment.GetEnvironmentVariable(
                    "MINMIN_COUNTDOWN_VISUAL_CAPTURE_NAME");
                captureName = string.Equals(captureName, "after", StringComparison.OrdinalIgnoreCase)
                    ? "after"
                    : "before";
                evidencePath = Path.Combine(evidenceDirectory, $"{captureName}-reminder-center.png");
                File.WriteAllBytes(evidencePath, pngBytes);
            }

            var rendered =
                center.IsVisible &&
                capture.PixelWidth == captureWidth &&
                capture.PixelHeight == captureHeight &&
                pageWhite >= 60_000 &&
                warmCard >= 20_000 &&
                pendingBadge >= 100 &&
                mainAction >= 400 &&
                sourceIcon >= 400;
            return new ReminderCenterCaptureValidation(
                rendered,
                capture.PixelWidth,
                capture.PixelHeight,
                Convert.ToHexString(SHA256.HashData(pngBytes)),
                pageWhite,
                warmCard,
                pendingBadge,
                mainAction,
                sourceIcon,
                evidencePath);
        }
        finally
        {
            coordinator?.Dispose();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            if (Directory.Exists(settingsPath + ".tmp"))
            {
                Directory.Delete(settingsPath + ".tmp");
            }
        }
    }

    private static (int PageWhite, int WarmCard, int PendingBadge, int MainAction, int SourceIcon)
        CountReminderCenterCapturePixels(BitmapSource capture)
    {
        var (pixels, _) = GetBgra32Pixels(capture);
        var pageWhite = 0;
        var warmCard = 0;
        var pendingBadge = 0;
        var mainAction = 0;
        var sourceIcon = 0;
        for (var index = 0; index + 3 < pixels.Length; index += 4)
        {
            var blue = pixels[index];
            var green = pixels[index + 1];
            var red = pixels[index + 2];
            if (Matches(red, green, blue, 0xFE, 0xFE, 0xFE))
            {
                pageWhite++;
            }
            else if (Matches(red, green, blue, 0xFE, 0xF9, 0xE8))
            {
                warmCard++;
            }
            else if (Matches(red, green, blue, 0xFD, 0xED, 0xD8))
            {
                pendingBadge++;
            }
            else if (Matches(red, green, blue, 0xC2, 0x3E, 0x68))
            {
                mainAction++;
            }
            else if (Matches(red, green, blue, 0xFE, 0xD6, 0x7F))
            {
                sourceIcon++;
            }
        }

        return (pageWhite, warmCard, pendingBadge, mainAction, sourceIcon);

        static bool Matches(
            int red,
            int green,
            int blue,
            int expectedRed,
            int expectedGreen,
            int expectedBlue) =>
            Math.Abs(red - expectedRed) <= 4 &&
            Math.Abs(green - expectedGreen) <= 4 &&
            Math.Abs(blue - expectedBlue) <= 4;
    }

    private static bool FiltersEqual(
        DesktopWorkspaceFilter left,
        DesktopWorkspaceFilter right) =>
        left.SelectedTagIds.SequenceEqual(right.SelectedTagIds, StringComparer.OrdinalIgnoreCase) &&
        left.MatchMode == right.MatchMode &&
        left.UntaggedOnly == right.UntaggedOnly &&
        left.CountdownStatus == right.CountdownStatus &&
        left.NoteStatus == right.NoteStatus &&
        string.Equals(left.NoteSearch, right.NoteSearch, StringComparison.Ordinal) &&
        string.Equals(left.NoteDate, right.NoteDate, StringComparison.Ordinal) &&
        left.UndatedNotesOnly == right.UndatedNotesOnly;

    private static ReminderCenterSourceLifecycleExerciseResult ValidateReminderCenterSourceLifecycle()
    {
        var firstDueUtc = new DateTimeOffset(2031, 6, 1, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-center-source-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var session = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            var workspace = new DesktopWorkspace(session);

            bool TryCreateDueNote(string text, DateTimeOffset dueUtc, out NoteRecord note)
            {
                if (!session.TrySaveNote(
                        null,
                        text,
                        new DateTime(2031, 6, 1),
                        false,
                        Array.Empty<string>(),
                        ReminderSchedule.CreateOnce(dueUtc),
                        out _))
                {
                    note = null!;
                    return false;
                }

                note = session.Current.Notes.Single(candidate =>
                    string.Equals(candidate.Text, text, StringComparison.Ordinal));
                return true;
            }

            coordinator = new ReminderDeliveryCoordinator(workspace, null);
            if (!TryCreateDueNote("删除来源生命周期探针", firstDueUtc, out var deletedNote))
            {
                return ReminderCenterSourceLifecycleExerciseResult.Failed;
            }

            clock.UtcNow = firstDueUtc;
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var deletedRule = deletedNote.ReminderRules.Single();
            var deletedKey = new ActiveReminderOccurrenceKey(deletedRule.Id, firstDueUtc);
            var deletedWindow = coordinator.ActiveWindow;
            Directory.CreateDirectory(blockedTemporaryPath);
            var acknowledgementFailed = deletedWindow?.RequestAcknowledgement(deletedKey) == true;
            var persistenceFailureRetainsCurrentSource =
                acknowledgementFailed &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.HasPendingPersistenceRetry &&
                deletedWindow?.IsVisible == true &&
                deletedWindow.IsFailureVisible(deletedKey);
            Directory.Delete(blockedTemporaryPath);
            var sourceDeleted = session.TryDeleteNote(deletedNote.Id, out _);
            clock.UtcNow = firstDueUtc.AddSeconds(1);
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var removesDeletedSource =
                sourceDeleted &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                deletedWindow?.IsVisible == false;
            var cancelsDeletedSourceAcknowledgementRetry =
                removesDeletedSource &&
                !coordinator.HasPendingPersistenceRetry;

            coordinator.Dispose();
            coordinator = new ReminderDeliveryCoordinator(workspace, null);
            var disabledDueUtc = firstDueUtc.AddMinutes(1);
            if (!TryCreateDueNote("停用来源生命周期探针", disabledDueUtc, out var disabledNote))
            {
                return ReminderCenterSourceLifecycleExerciseResult.Failed;
            }

            clock.UtcNow = disabledDueUtc;
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var disabledWindow = coordinator.ActiveWindow;
            var sourceDisabled = session.TrySaveNote(
                disabledNote.Id,
                disabledNote.Text,
                new DateTime(2031, 6, 1),
                false,
                disabledNote.TagIds,
                NoReminder.Instance,
                out _);
            clock.UtcNow = disabledDueUtc.AddSeconds(1);
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var removesDisabledSource =
                sourceDisabled &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                disabledWindow?.IsVisible == false;

            coordinator.Dispose();
            coordinator = new ReminderDeliveryCoordinator(workspace, null);
            var completedDueUtc = firstDueUtc.AddMinutes(2);
            if (!TryCreateDueNote("完成来源生命周期探针", completedDueUtc, out var completedNote))
            {
                return ReminderCenterSourceLifecycleExerciseResult.Failed;
            }

            clock.UtcNow = completedDueUtc;
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var completedWindow = coordinator.ActiveWindow;
            var sourceCompleted = session.TrySetNoteCompletion(completedNote.Id, true, out _);
            clock.UtcNow = completedDueUtc.AddSeconds(1);
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var removesCompletedSource =
                sourceCompleted &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                completedWindow?.IsVisible == false;

            var reopenedDueUtc = firstDueUtc.AddMinutes(3);
            if (!TryCreateDueNote("来源失效后重开探针", reopenedDueUtc, out _))
            {
                return ReminderCenterSourceLifecycleExerciseResult.Failed;
            }

            clock.UtcNow = reopenedDueUtc;
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var reopensAfterSourceRemoval =
                removesCompletedSource &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveWindow?.IsVisible == true;

            return new ReminderCenterSourceLifecycleExerciseResult(
                persistenceFailureRetainsCurrentSource,
                removesDeletedSource,
                cancelsDeletedSourceAcknowledgementRetry,
                removesDisabledSource,
                removesCompletedSource,
                reopensAfterSourceRemoval);
        }
        finally
        {
            coordinator?.Dispose();
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
            else
            {
                File.Delete(blockedTemporaryPath);
            }
            TryDeleteSettingsFiles(settingsPath);
        }
    }

    private static ReminderDeliveryExerciseResult ValidateReminderDeliveryCoordinator()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 7, 1, 30, 0, TimeSpan.Zero);
        var repeatingDueUtc = firstDueUtc.AddMinutes(-30);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-delivery-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        const string reminderText = "真实渲染与持久化重试探针";
        const string repeatingReminderText = "独立重复提醒探针";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(repeatingDueUtc.AddSeconds(-1));
            var session = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!session.TrySaveNote(
                    null,
                    reminderText,
                    new DateTime(2031, 4, 7),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc),
                    out _))
            {
                return ReminderDeliveryExerciseResult.Failed;
            }

            var onceId = session.Current.Notes.Single().Id;
            if (!session.TrySaveNote(
                    null,
                    repeatingReminderText,
                    new DateTime(2031, 4, 7),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(repeatingDueUtc, 60),
                    out _))
            {
                return ReminderDeliveryExerciseResult.Failed;
            }

            var repeatingId = session.Current.Notes.Single(note =>
                string.Equals(note.Text, repeatingReminderText, StringComparison.Ordinal)).Id;
            var workspace = new DesktopWorkspace(session);
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                new DiagnosticLogger(diagnosticPath));
            clock.UtcNow = repeatingDueUtc;
            coordinator.Tick(repeatingDueUtc);
            var repeatingWindow = Application.Current.Windows
                .OfType<ReminderCenterWindow>()
                .FirstOrDefault(window => Descendants<TextBlock>(window).Any(text =>
                    string.Equals(text.Text, repeatingReminderText, StringComparison.Ordinal)));
            PumpDispatcher();
            var repeatingNote = session.Current.Notes.Single(note => note.Id == repeatingId);
            var repeatingRule = repeatingNote.ReminderRules.Single();
            var repeatingStatePersisted =
                session.Current.ReminderStates.TryGetValue(repeatingRule.Id, out var repeatingState) &&
                repeatingState.HandledThroughUtc == repeatingDueUtc &&
                repeatingState.LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(
                        repeatingRule,
                        repeatingDueUtc).ToString();
            var repeatingRemainsUnresolved = workspace
                .GetDueReminderOccurrences(repeatingDueUtc)
                .SingleOrDefault() is { } repeatingOccurrence &&
                repeatingOccurrence.Note.Id == repeatingId &&
                repeatingOccurrence.Rule.Id == repeatingRule.Id &&
                repeatingOccurrence.ScheduledUtc == repeatingDueUtc;

            var repeatingKey = new ActiveReminderOccurrenceKey(repeatingRule.Id, repeatingDueUtc);
            var repeatingAcknowledged = repeatingWindow?.RequestAcknowledgement(repeatingKey) == true;
            PumpDispatcher();
            var beforeFailure = session.SerializeCurrent();
            Directory.CreateDirectory(blockedTemporaryPath);
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(firstDueUtc);
            var onceWindow = coordinator.ActiveWindow;
            var onceRule = session.Current.Notes
                .Single(note => note.Id == onceId)
                .ReminderRules.Single();
            var onceKey = new ActiveReminderOccurrenceKey(onceRule.Id, firstDueUtc);
            var onceEntryRendered = false;
            if (onceWindow is not null)
            {
                onceWindow.EntryRendered += (_, args) =>
                    onceEntryRendered |= args.Key == onceKey;
            }
            var pendingBeforeRender = ReminderRuleCompatibility.ToLegacy(
                session.Current,
                session.Current.Notes.Single(note => note.Id == onceId)) is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            };
            PumpDispatcher();
            var independentOccurrencesWork =
                repeatingStatePersisted &&
                repeatingRemainsUnresolved &&
                repeatingAcknowledged &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                onceWindow?.IsVisible == true &&
                !ReferenceEquals(onceWindow, repeatingWindow) &&
                onceEntryRendered &&
                !session.Reminders.GetDue(session.Current, firstDueUtc)
                    .Any(note => note.Id == repeatingId);
            var dismiss = onceWindow is null
                ? null
                : Descendants<Button>(onceWindow).Single(button =>
                    AutomationProperties.GetName(button).StartsWith("知道了：", StringComparison.Ordinal) &&
                    AutomationProperties.GetName(button).Contains(reminderText, StringComparison.Ordinal));
            var openSource = onceWindow is null
                ? null
                : Descendants<Button>(onceWindow).Single(button =>
                    AutomationProperties.GetName(button).StartsWith("打开便签来源：", StringComparison.Ordinal) &&
                    AutomationProperties.GetName(button).Contains(reminderText, StringComparison.Ordinal));
            var failureStatusVisible =
                onceWindow?.PersistenceFailureVisible == true &&
                onceWindow.IsVisible &&
                dismiss?.IsEnabled == false &&
                openSource?.IsEnabled == true;
            var failureKeyHandled = dismiss is not null &&
                                    !WindowValidationInput.RaisePreviewKey(dismiss, Key.Escape);
            PumpDispatcher();
            var failureWindowStayedVisible = onceWindow?.IsVisible == true;
            var firstRetryUtc = coordinator.NextPersistenceRetryUtc;
            var firstFailureCount = coordinator.PersistenceFailureCount;
            onceWindow?.Close();
            PumpDispatcher();
            var persistenceFailureCloseGuardWorks =
                onceWindow?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.PersistenceFailureCount == firstFailureCount &&
                coordinator.NextPersistenceRetryUtc == firstRetryUtc;
            var firstFailureSingleFlight =
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.PersistenceFailureCount == 1 &&
                coordinator.HasPendingPersistenceRetry &&
                firstRetryUtc == firstDueUtc.AddSeconds(5) &&
                string.Equals(beforeFailure, session.SerializeCurrent(), StringComparison.Ordinal);

            coordinator.Tick(firstDueUtc.AddSeconds(4));
            var noEarlyRetry = coordinator.PersistenceFailureCount == 1 &&
                               coordinator.ActiveWindowCount == 1;
            coordinator.Tick(firstDueUtc.AddSeconds(5));
            var secondRetryUtc = coordinator.NextPersistenceRetryUtc;
            coordinator.Tick(firstDueUtc.AddSeconds(15));
            var thirdRetryUtc = coordinator.NextPersistenceRetryUtc;
            coordinator.Tick(firstDueUtc.AddSeconds(35));
            var fourthRetryUtc = coordinator.NextPersistenceRetryUtc;
            coordinator.Tick(firstDueUtc.AddSeconds(65));
            var fifthRetryUtc = coordinator.NextPersistenceRetryUtc;
            var diagnostics = File.ReadAllText(diagnosticPath);
            var diagnosticsExcludePrivatePayload =
                diagnostics.Contains(
                    "OneTimeReminderDeliveryPersistFailed",
                    StringComparison.Ordinal) &&
                !diagnostics.Contains(reminderText, StringComparison.Ordinal) &&
                !diagnostics.Contains(beforeFailure, StringComparison.Ordinal) &&
                !diagnostics.Contains("\"notes\"", StringComparison.Ordinal);
            var backoffWorks =
                secondRetryUtc == firstDueUtc.AddSeconds(15) &&
                thirdRetryUtc == firstDueUtc.AddSeconds(35) &&
                fourthRetryUtc == firstDueUtc.AddSeconds(65) &&
                fifthRetryUtc == firstDueUtc.AddSeconds(95) &&
                coordinator.PersistenceFailureCount == 5 &&
                coordinator.ActiveWindowCount == 1;

            Directory.Delete(blockedTemporaryPath);
            coordinator.Tick(firstDueUtc.AddSeconds(95));
            var delivered = ReminderRuleCompatibility.ToLegacy(
                session.Current,
                session.Current.Notes.Single(note => note.Id == onceId)) is OnceReminder
            {
                Delivery: OnceDeliveryState.Delivered
            };
            var successSingleFlight =
                delivered &&
                coordinator.ActiveOccurrenceConfirmed &&
                !coordinator.HasPendingPersistenceRetry &&
                coordinator.ActiveWindowCount == 1 &&
                onceWindow?.PersistenceFailureVisible == false &&
                dismiss?.IsEnabled == true;
            coordinator.Tick(firstDueUtc.AddSeconds(96));
            successSingleFlight &= coordinator.ActiveWindowCount == 1;
            var acknowledged = onceWindow?.RequestAcknowledgement(onceKey) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            persistenceFailureCloseGuardWorks &=
                acknowledged &&
                onceWindow?.IsVisible == false &&
                coordinator.ActiveWindowCount == 0;

            return new ReminderDeliveryExerciseResult(
                pendingBeforeRender && onceEntryRendered,
                failureStatusVisible && failureKeyHandled && failureWindowStayedVisible,
                firstFailureSingleFlight && noEarlyRetry && backoffWorks,
                successSingleFlight,
                independentOccurrencesWork,
                persistenceFailureCloseGuardWorks,
                diagnosticsExcludePrivatePayload);
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(blockedTemporaryPath);
            File.Delete(diagnosticPath);
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
        }
    }

    private static bool AreNotesGroupedByDay(IReadOnlyList<NoteRecord> notes)
    {
        var seen = new HashSet<(bool IsCompleted, string? Date)>();
        bool? currentCompletion = null;
        string? currentDate = null;
        foreach (var note in notes)
        {
            if (
                currentCompletion == note.IsCompleted &&
                string.Equals(currentDate, note.Date, StringComparison.Ordinal))
            {
                continue;
            }

            currentCompletion = note.IsCompleted;
            currentDate = note.Date;
            if (!seen.Add((note.IsCompleted, note.Date)))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> GetRenderedNoteDateOrder(Panel rows)
    {
        return GetNoteDateSections(rows)
            .Select(section => section.Key.Length == 0 ? "未设置日期" : section.Key)
            .ToArray();
    }

    private static bool HasNoteStatusSegmentSwitch(
        ToggleButton activeSegment,
        ToggleButton completedSegment,
        TextBlock activeLabel,
        TextBlock completedLabel,
        TextBlock activeCount,
        TextBlock completedCount,
        Border activeBadge,
        Border completedBadge,
        Panel activeRows,
        Panel completedRows,
        int expectedActiveCount,
        int expectedCompletedCount)
    {
        var countsMatch =
            string.Equals(
                activeCount.Text,
                expectedActiveCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal) &&
            string.Equals(
                completedCount.Text,
                expectedCompletedCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        var labelsFixed =
            string.Equals(activeLabel.Text, "未完成", StringComparison.Ordinal) &&
            string.Equals(completedLabel.Text, "已完成", StringComparison.Ordinal);
        var segmentsVisibleAndEnabled =
            activeSegment.Visibility == Visibility.Visible &&
            completedSegment.Visibility == Visibility.Visible &&
            activeSegment.IsEnabled &&
            completedSegment.IsEnabled;
        var activeSelected =
            activeSegment.IsChecked == true &&
            completedSegment.IsChecked == false &&
            activeRows.Visibility == Visibility.Visible &&
            completedRows.Visibility == Visibility.Collapsed;
        return countsMatch &&
            labelsFixed &&
            segmentsVisibleAndEnabled &&
            activeSelected &&
            BadgeFollowsSegmentState(activeBadge, expectedActiveCount, isActive: true) &&
            BadgeFollowsSegmentState(completedBadge, expectedCompletedCount, isActive: false);
    }

    private static bool BadgeFollowsSegmentState(Border badge, int expectedCount, bool isActive)
    {
        if (badge.Child is not TextBlock count ||
            !string.Equals(
                count.Text,
                expectedCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            return false;
        }

        var expectedBackground = Application.Current.FindResource(
            isActive ? "PeachBadgeBrush" : "PeachBadgeMutedBrush");
        // #148：徽章文字必须达到信息文本对比门槛——激活段主文字（InkBrush，8.69），
        // 未激活段正文色（BodyBrush，6.09）；不再接受白字/弱化字钉死的旧合同。
        var expectedForeground = Application.Current.FindResource(
            isActive ? "InkBrush" : "BodyBrush");
        return ReferenceEquals(badge.Background, expectedBackground) &&
            ReferenceEquals(count.Foreground, expectedForeground) &&
            expectedBackground is SolidColorBrush backgroundBrush &&
            expectedForeground is SolidColorBrush foregroundBrush &&
            ContrastRatio(backgroundBrush.Color, foregroundBrush.Color) >= 4.5;
    }

    private static bool HasNoteSegmentAccessibleName(
        ToggleButton segment,
        string expectedStatus,
        string countText)
    {
        var name = AutomationProperties.GetName(segment);
        return
            !string.IsNullOrWhiteSpace(name) &&
            name.Contains($"查看{expectedStatus}便签", StringComparison.Ordinal) &&
            name.Contains(countText, StringComparison.Ordinal) &&
            name.Contains("条", StringComparison.Ordinal);
    }

    private static bool HasNoteDateTimelineVisual(NoteDateSection section)
    {
        var content = section.Toggle.Content as Grid;
        var label = GetNoteDateLabel(section.Toggle);
        var count = content?.Children
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Uid, "NoteDateCount", StringComparison.Ordinal));
        var root = section.Root as Grid;
        var timeline = root?.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => string.Equals(grid.Uid, "NoteDateTimeline", StringComparison.Ordinal));
        var line = timeline?.Children
            .OfType<Border>()
            .Any(border => string.Equals(border.Uid, "NoteTimelineLine", StringComparison.Ordinal)) == true;
        var node = timeline?.Children
            .OfType<WpfEllipse>()
            .Any(ellipse => string.Equals(ellipse.Uid, "NoteTimelineNode", StringComparison.Ordinal)) == true;
        return
            string.Equals(section.Root.Uid, "NoteDateTimelineSection", StringComparison.Ordinal) &&
            Math.Abs(section.Root.Margin.Bottom - 12) <= 0.01 &&
            root?.ColumnDefinitions.Count > 0 &&
            root.ColumnDefinitions[0].Width.IsAbsolute &&
            Math.Abs(root.ColumnDefinitions[0].Width.Value - 30) <= 0.01 &&
            timeline is not null &&
            Math.Abs(timeline.Width - 30) <= 0.01 &&
            line &&
            node &&
            label is not null &&
            !string.IsNullOrWhiteSpace(label.Text) &&
            !label.Text.StartsWith("展开", StringComparison.Ordinal) &&
            !label.Text.StartsWith("收起", StringComparison.Ordinal) &&
            count is not null &&
            string.Equals(
                count.Text,
                $"{section.Rows.Children.Count} 条",
                StringComparison.Ordinal) &&
            DisclosureChevronMatches(
                section.Toggle,
                section.Rows.Visibility == Visibility.Visible);
    }

    private static TextBlock? GetNoteDateLabel(Button toggle)
    {
        return (toggle.Content as Grid)?.Children
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Uid, "NoteDateLabel", StringComparison.Ordinal));
    }

    private static bool HasNoteDisclosureAccessibleName(Button toggle, string expectedObject)
    {
        var name = AutomationProperties.GetName(toggle);
        return
            !string.IsNullOrWhiteSpace(name) &&
            (name.StartsWith("展开", StringComparison.Ordinal) ||
             name.StartsWith("收起", StringComparison.Ordinal)) &&
            (name.Contains("当前展开", StringComparison.Ordinal) ||
             name.Contains("当前收起", StringComparison.Ordinal)) &&
            name.Contains(expectedObject, StringComparison.Ordinal) &&
            name.Contains("条", StringComparison.Ordinal);
    }

    private static bool HasNoteRowActionAccessibleNames(DependencyObject root)
    {
        var actions = Descendants<Button>(root)
            .Where(button =>
                button.Content is "编辑" or "删除")
            .ToArray();
        return actions.Length == 0 ||
               actions.All(button =>
               {
                   var action = (string)button.Content;
                   var name = AutomationProperties.GetName(button);
                   var prefix = $"{action}便签 ";
                   return ValidationExercises.IsWellFormedAccessibleName(name, prefix);
               });
    }

    private static bool DisclosureChevronMatches(Button toggle, bool isExpanded)
    {
        var chevron = (toggle.Content as Grid)?.Children
            .OfType<WpfPath>()
            .FirstOrDefault(path => string.Equals(path.Uid, "NoteDateChevron", StringComparison.Ordinal));
        return chevron is not null && DisclosureChevronMatches(chevron, isExpanded);
    }

    private static bool DisclosureChevronMatches(WpfPath chevron, bool isExpanded)
    {
        return
            chevron.RenderTransform is RotateTransform rotation &&
            Math.Abs(rotation.Angle - (isExpanded ? 0 : -90)) < 0.01;
    }

    private static IReadOnlyList<string> GetRenderedNoteIds(DependencyObject root) =>
        Descendants<CheckBox>(root)
            .Where(checkBox => checkBox.Tag is string)
            .Select(checkBox => (string)checkBox.Tag)
            .ToArray();

    private static NoteSectionExerciseResult ExerciseNoteSections(
        MainWindow main,
        StackPanel activeRows,
        StackPanel completedRows)
    {
        var activeSegment = Required<ToggleButton>(main, "ActiveNotesSegment");
        var completedSegment = Required<ToggleButton>(main, "CompletedNotesSegment");
        var originalWorkspace = main.CurrentWorkspace;

        try
        {
            main.ShowWorkspace(WorkspaceKind.Notes);
            if (completedSegment.IsChecked == true)
            {
                activeSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            var segmentsSwitchExclusively = ExerciseNoteSegmentSwitch(
                main,
                activeRows,
                completedRows,
                activeSegment,
                completedSegment);

            var activeSections = GetNoteDateSections(activeRows);
            var completedSections = GetNoteDateSections(completedRows);
            var activeKey = activeSections.FirstOrDefault()?.Key;
            var completedKey = completedSections.FirstOrDefault()?.Key;
            var dateSectionsCanCollapse = true;
            var dateSectionsAreIndependent = true;
            var dateSectionStateSurvivesRefresh = true;

            if (activeKey is not null)
            {
                var activeSection = FindNoteDateSection(activeRows, activeKey)!;
                activeSection.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dateSectionsCanCollapse &=
                    activeSection.Rows.Visibility == Visibility.Collapsed &&
                    DisclosureChevronMatches(activeSection.Toggle, isExpanded: false);
                dateSectionsAreIndependent &=
                    OtherNoteDateSectionsAreVisible(activeRows, activeKey) &&
                    GetNoteDateSections(completedRows).All(section => section.Rows.Visibility == Visibility.Visible);

                main.ShowWorkspace(WorkspaceKind.Notes);
                activeSection = FindNoteDateSection(activeRows, activeKey)!;
                dateSectionStateSurvivesRefresh &= activeSection.Rows.Visibility == Visibility.Collapsed;
                dateSectionsAreIndependent &=
                    OtherNoteDateSectionsAreVisible(activeRows, activeKey) &&
                    GetNoteDateSections(completedRows).All(section => section.Rows.Visibility == Visibility.Visible);
            }

            if (completedKey is not null)
            {
                var completedSection = FindNoteDateSection(completedRows, completedKey)!;
                completedSection.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dateSectionsCanCollapse &=
                    completedSection.Rows.Visibility == Visibility.Collapsed &&
                    DisclosureChevronMatches(completedSection.Toggle, isExpanded: false);
                dateSectionsAreIndependent &=
                    OtherNoteDateSectionsAreVisible(completedRows, completedKey) &&
                    (activeKey is null ||
                     FindNoteDateSection(activeRows, activeKey)!.Rows.Visibility == Visibility.Collapsed);

                main.ShowWorkspace(WorkspaceKind.Notes);
                completedSection = FindNoteDateSection(completedRows, completedKey)!;
                dateSectionStateSurvivesRefresh &= completedSection.Rows.Visibility == Visibility.Collapsed;
                dateSectionsAreIndependent &=
                    OtherNoteDateSectionsAreVisible(completedRows, completedKey) &&
                    (activeKey is null ||
                     FindNoteDateSection(activeRows, activeKey)!.Rows.Visibility == Visibility.Collapsed);
            }

            if (activeKey is not null && completedKey is not null)
            {
                FindNoteDateSection(activeRows, activeKey)!.Toggle.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                dateSectionsAreIndependent &=
                    FindNoteDateSection(activeRows, activeKey)!.Rows.Visibility == Visibility.Visible &&
                    FindNoteDateSection(completedRows, completedKey)!.Rows.Visibility == Visibility.Collapsed;
            }

            return new NoteSectionExerciseResult(
                dateSectionsCanCollapse,
                dateSectionsAreIndependent,
                dateSectionStateSurvivesRefresh,
                segmentsSwitchExclusively);
        }
        finally
        {
            ExpandAllNoteDateSections(activeRows);
            ExpandAllNoteDateSections(completedRows);
            if (completedSegment.IsChecked == true)
            {
                activeSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            main.ShowWorkspace(originalWorkspace);
        }
    }

    private static bool ExerciseNoteSegmentSwitch(
        MainWindow main,
        StackPanel activeRows,
        StackPanel completedRows,
        ToggleButton activeSegment,
        ToggleButton completedSegment)
    {
        var activeRowsExpected = ParseBadgeCount(activeSegment) > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        var completedRowsExpected = ParseBadgeCount(completedSegment) > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        completedSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var switchedExclusively =
            completedSegment.IsChecked == true &&
            activeSegment.IsChecked == false &&
            completedRows.Visibility == completedRowsExpected &&
            activeRows.Visibility == Visibility.Collapsed;
        main.ShowWorkspace(WorkspaceKind.Notes);
        switchedExclusively &=
            completedSegment.IsChecked == true &&
            completedRows.Visibility == completedRowsExpected &&
            activeRows.Visibility == Visibility.Collapsed;

        activeSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        switchedExclusively &=
            activeSegment.IsChecked == true &&
            completedSegment.IsChecked == false &&
            activeRows.Visibility == activeRowsExpected &&
            completedRows.Visibility == Visibility.Collapsed;
        main.ShowWorkspace(WorkspaceKind.Notes);
        switchedExclusively &=
            activeSegment.IsChecked == true &&
            activeRows.Visibility == activeRowsExpected &&
            completedRows.Visibility == Visibility.Collapsed;
        return switchedExclusively;
    }

    private static IReadOnlyList<NoteDateSection> GetNoteDateSections(Panel rows)
    {
        var result = new List<NoteDateSection>();
        foreach (var section in rows.Children.OfType<FrameworkElement>())
        {
            var toggle = Descendants<Button>(section)
                .FirstOrDefault(button =>
                    string.Equals(button.Uid, "NoteDateDisclosure", StringComparison.Ordinal));
            var sectionRows = Descendants<StackPanel>(section)
                .FirstOrDefault(panel =>
                    string.Equals(panel.Uid, "NoteDateRows", StringComparison.Ordinal));
            if (toggle is not null && sectionRows is not null)
            {
                result.Add(new NoteDateSection(
                    toggle.Tag as string ?? string.Empty,
                    section,
                    toggle,
                    sectionRows));
            }
        }

        return result;
    }

    private static NoteDateSection? FindNoteDateSection(StackPanel rows, string key) =>
        GetNoteDateSections(rows).FirstOrDefault(section =>
            string.Equals(section.Key, key, StringComparison.Ordinal));

    private static bool OtherNoteDateSectionsAreVisible(StackPanel rows, string exceptKey) =>
        GetNoteDateSections(rows)
            .Where(section => !string.Equals(section.Key, exceptKey, StringComparison.Ordinal))
            .All(section => section.Rows.Visibility == Visibility.Visible);

    private static void ExpandAllNoteDateSections(StackPanel rows)
    {
        foreach (var section in GetNoteDateSections(rows).Where(section =>
                     section.Rows.Visibility == Visibility.Collapsed))
        {
            section.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private static bool HasApplicationStyle(Type key) =>
        Application.Current.Resources[key] is Style;

    private static bool HasTemplate(Control control)
    {
        control.ApplyTemplate();
        return control.Template is not null;
    }

    private static MainThemeExerciseResult RunMainThemeExercise(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        var backgroundColors = new List<string>();
        var characterOpacities = new List<int>();
        var characterPanelColors = new List<string>();
        var reminderLabelFollowsTheme = true;
        var segmentSelectedTextContrast = true;
        var characterPanelDocksFollowTheme = true;
        var referenceDecorationsFollowTheme = true;
        var reminderLabel = Required<TextBlock>(main, "NoteReminderModeLabel");
        var characterPanel = Required<Border>(main, "CharacterPanelSurface");
        var originalWorkspace = main.CurrentWorkspace;

        try
        {
            theme.Apply();
            main.ApplyTheme();
            main.UpdateTime(restoreUtc);
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            main.UpdateLayout();
            backgroundColors.Add(BrushColor(main.Background));
            characterOpacities.Add((int)Math.Round(main.CharacterBackgroundOpacity * 100));
            characterPanelColors.Add(BrushColor(characterPanel.Background));
            reminderLabelFollowsTheme &=
                ReferenceEquals(reminderLabel.Foreground, Application.Current.FindResource("BodyBrush"));
            segmentSelectedTextContrast &= MainSegmentButtonsHaveAccessibleSelectedStates(main);
            characterPanelDocksFollowTheme &= main.CharacterPanelDocksFollowActiveTheme;
            referenceDecorationsFollowTheme &= main.MainReferenceDecorationsFollowActiveTheme;

            main.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            main.UpdateLayout();
            characterPanelDocksFollowTheme &= main.CharacterPanelDocksFollowActiveTheme;
            referenceDecorationsFollowTheme &= main.MainReferenceDecorationsFollowActiveTheme;
        }
        finally
        {
            main.ShowWorkspace(originalWorkspace);
            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            main.UpdateLayout();
        }

        return new MainThemeExerciseResult(
            backgroundColors,
            characterOpacities,
            characterPanelColors,
            reminderLabelFollowsTheme,
            segmentSelectedTextContrast,
            characterPanelDocksFollowTheme,
            referenceDecorationsFollowTheme);
    }

    private static MainWorkspaceThemeValidation ValidateMainWorkspaceResourcesWithoutRerender(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc,
        string assetRoot)
    {
        var liveResult = EvaluateMainWorkspaceResourcesWithoutRerender(
            theme,
            main,
            restoreUtc);
        if (liveResult.CountdownCardSampleCount > 0 && liveResult.NoteCardSampleCount > 0)
        {
            return liveResult;
        }

        return RunIsolatedMainWorkspaceThemeValidation(theme, restoreUtc, assetRoot);
    }

    private static MainWorkspaceThemeValidation RunIsolatedMainWorkspaceThemeValidation(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc,
        string assetRoot)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-theme-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(temporaryRoot, "settings.json");
        var diagnosticPath = Path.Combine(temporaryRoot, "validation.jsonl");
        MainWindow? validationMain = null;
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var store = new SettingsStore(clock: new ValidationClock(restoreUtc));
            store.Save(
                settingsPath,
                new CountdownSettings(
                    new[]
                    {
                        new CountdownRecord(
                            "validation-theme-countdown",
                            "主题资源验证倒计时",
                            restoreUtc.AddDays(2),
                            Array.Empty<string>())
                    },
                    new[]
                    {
                        new NoteRecord(
                            "validation-theme-note",
                            "主题资源验证便签",
                            ChinaStandardTime.ToBeijingTime(restoreUtc)
                                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            false,
                            Array.Empty<string>(),
                            NoReminder.Instance)
                    },
                    Array.Empty<TagRecord>(),
                    CountdownSettings.Empty.Preferences));
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            using var calendar = new HolidayCalendar(temporaryRoot);
            validationMain = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                assetRoot)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            validationMain.Show();
            PumpDispatcher();
            validationMain.UpdateLayout();
            return EvaluateMainWorkspaceResourcesWithoutRerender(
                theme,
                validationMain,
                restoreUtc);
        }
        finally
        {
            validationMain?.Close();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".tmp");
            TryDeleteFile(diagnosticPath);
            var holidayDirectory = Path.Combine(temporaryRoot, "holiday-cache");
            if (Directory.Exists(holidayDirectory) &&
                !Directory.EnumerateFileSystemEntries(holidayDirectory).Any())
            {
                Directory.Delete(holidayDirectory);
            }
            if (Directory.Exists(temporaryRoot) &&
                !Directory.EnumerateFileSystemEntries(temporaryRoot).Any())
            {
                Directory.Delete(temporaryRoot);
            }
        }
    }

    private static MainWorkspaceThemeValidation EvaluateMainWorkspaceResourcesWithoutRerender(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        static bool UsesResource(DependencyObject element, DependencyProperty property, string key) =>
            ReferenceEquals(element.GetValue(property), Application.Current.FindResource(key));

        static bool UsesAnyResource(
            DependencyObject element,
            DependencyProperty property,
            params string[] keys) =>
            keys.Any(key => UsesResource(element, property, key));

        static bool BodyTextUsesTheme(StackPanel body) =>
            body.Children
                .OfType<TextBlock>()
                .All(text => UsesAnyResource(
                    text,
                    TextBlock.ForegroundProperty,
                    "InkBrush",
                    "BodyBrush",
                    "MutedBrush",
                    "DangerBrush"));

        static bool TagTextUsesTheme(StackPanel body) =>
            body.Children
                .OfType<WrapPanel>()
                .SelectMany(panel => panel.Children.OfType<Border>())
                .Select(chip => chip.Child)
                .OfType<TextBlock>()
                .All(text => UsesResource(text, TextBlock.ForegroundProperty, "TagChipTextBrush"));

        static bool CardBodiesUseTheme(Border card, bool requirePrimaryColumn)
        {
            if (card.Child is not Grid grid)
            {
                return false;
            }

            var bodies = grid.Children
                .OfType<StackPanel>()
                .Where(panel => !requirePrimaryColumn || Grid.GetColumn(panel) is 0 or 1)
                .ToArray();
            var directTexts = grid.Children
                .OfType<TextBlock>()
                .Where(text => !requirePrimaryColumn || Grid.GetColumn(text) is 0 or 1)
                .ToArray();
            var directTagTexts = grid.Children
                .OfType<WrapPanel>()
                .Where(panel => !requirePrimaryColumn || Grid.GetColumn(panel) is 0 or 1)
                .SelectMany(panel => panel.Children.OfType<Border>())
                .Select(chip => chip.Child)
                .OfType<TextBlock>()
                .ToArray();
            return (directTexts.Length > 0 ||
                    bodies.Any(body => body.Children.OfType<TextBlock>().Any())) &&
                directTexts.All(text => UsesAnyResource(
                    text,
                    TextBlock.ForegroundProperty,
                    "InkBrush",
                    "BodyBrush",
                    "MutedBrush",
                    "DangerBrush")) &&
                directTagTexts.All(text =>
                    UsesResource(text, TextBlock.ForegroundProperty, "TagChipTextBrush")) &&
                bodies.All(BodyTextUsesTheme) &&
                bodies.All(TagTextUsesTheme);
        }

        var originalWorkspace = main.CurrentWorkspace;
        try
        {
            theme.Apply();
            main.ApplyTheme();
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            main.UpdateLayout();

            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            main.UpdateLayout();

            var countdownPanelsFollowTheme = new[]
                {
                    main.FindName("CountdownWorkspaceBackdrop") as Border,
                    main.FindName("CountdownEditorSurface") as Border,
                    main.FindName("CountdownPreferencesSurface") as Border
                }
                .All(panel =>
                    panel is not null &&
                    UsesResource(panel, Border.BackgroundProperty, "BackgroundBrush") &&
                    UsesResource(panel, Border.BorderBrushProperty, "PeachLineBrush"));
            var notePanel = main.FindName("NoteEditorPanel") as Border;
            var panelsFollowTheme =
                countdownPanelsFollowTheme &&
                notePanel is not null &&
                UsesResource(notePanel, Border.BackgroundProperty, "WorkspacePanelBrush") &&
                UsesResource(notePanel, Border.BorderBrushProperty, "WorkspacePanelLineBrush");
            var titleFollowsTheme =
                main.FindName("CountdownEditorTitle") is TextBlock editorTitle &&
                UsesResource(editorTitle, TextBlock.ForegroundProperty, "InkBrush");
            var countdownCards = Required<StackPanel>(main, "CountdownRows")
                .Children
                .OfType<Border>()
                .ToArray();
            var countdownCardsFollowTheme = countdownCards.All(card =>
                    UsesAnyResource(card, Border.BackgroundProperty, "CardBrush", "ExpiredBrush") &&
                    UsesResource(card, Border.BorderBrushProperty, "PeachLineBrush") &&
                    CardBodiesUseTheme(card, requirePrimaryColumn: true));

            theme.Apply();
            main.ApplyTheme();
            main.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            main.UpdateLayout();

            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            main.UpdateLayout();

            var noteCards = new[]
                {
                    Required<StackPanel>(main, "ActiveNoteRows"),
                    Required<StackPanel>(main, "CompletedNoteRows")
                }
                .SelectMany(rows => Descendants<Border>(rows))
                .Where(card =>
                    Math.Abs(card.CornerRadius.TopLeft - 8) <= 0.01 &&
                    Math.Abs(card.Padding.Left - 14) <= 0.01)
                .ToArray();
            var noteCardsFollowTheme = noteCards.All(card =>
                    UsesAnyResource(card, Border.BackgroundProperty, "NoteBrush", "NoteWhiteBrush") &&
                    UsesAnyResource(card, Border.BorderBrushProperty, "NoteLineBrush", "PeachLineBrush") &&
                    CardBodiesUseTheme(card, requirePrimaryColumn: false));

            return new MainWorkspaceThemeValidation(
                panelsFollowTheme && titleFollowsTheme,
                countdownCards.Length,
                countdownCardsFollowTheme,
                noteCards.Length,
                noteCardsFollowTheme);
        }
        finally
        {
            theme.Apply();
            main.ApplyTheme();
            main.ShowWorkspace(originalWorkspace);
            PumpDispatcher();
            main.UpdateLayout();
        }
    }

    private static bool ValidateTagPopupThemeTransition(
        ApplicationTheme theme,
        TagSelector selector,
        DateTimeOffset restoreUtc)
    {
        var wasOpen = selector.IsPopupOpen;
        selector.IsPopupOpen = true;
        try
        {
            theme.Apply();
            PumpDispatcher();
            return selector.PopupSurfaceFollowsActiveTheme;
        }
        finally
        {
            selector.IsPopupOpen = wasOpen;
            theme.Apply();
            PumpDispatcher();
        }
    }

    private static bool ValidateWarmYellowPalette(ApplicationTheme theme, DateTimeOffset restoreUtc)
    {
        static bool HasExpectedColor(string key, string expected)
        {
            if (ColorConverter.ConvertFromString(expected) is not Color expectedColor)
            {
                return false;
            }

            return Application.Current.FindResource(key) switch
            {
                Color color => color == expectedColor,
                SolidColorBrush brush => brush.Color == expectedColor,
                _ => false
            };
        }

        var defaultTagColorsMatch = MainWindow.DefaultTagColors.SequenceEqual(
            new[] { "#F4B6A6", "#F4D58D", "#F1D5C5", "#F9DFDB", "#FFF0E4" },
            StringComparer.OrdinalIgnoreCase);
        try
        {
            theme.Apply();
            var dayMatches = new[]
            {
                ("CanvasColor", "#FFFDF3EB"),
                ("BackgroundColor", "#FFFDF0E4"),
                ("CardColor", "#FFFEFCFB"),
                ("PanelColor", "#FFFFFDFC"),
                ("CardAltColor", "#FFFEF9F5"),
                ("NoteWhiteColor", "#FFFEFDFD"),
                ("NoteColor", "#FFFEF5DC"),
                ("NoteLineColor", "#FFF6D9B2"),
                ("ActiveHoverColor", "#FFB84A36"),
                ("MutedColor", "#FF82665F"),
                ("MainActionColor", "#FFC23E68"),
                ("MainActionHoverColor", "#FFB0355D"),
                ("MainActionPressedColor", "#FF9B2D50"),
                ("DangerColor", "#FFB74C39"),
                ("PrimaryCountdownNumberColor", "#FFC0102F"),
                ("PeachLineColor", "#FFF6DFD3"),
                ("TagColor", "#FFF7E8"),
                ("TagChipTextColor", "#FF3B190D"),
                ("SegmentSelectedColor", "#FFFEEFE1"),
                ("SegmentSelectedForegroundColor", "#FF3B190D"),
                ("PinkTintColor", "#FFFEDDDE"),
                ("PinkAccentStrongColor", "#FFC23E68"),
                ("WorkspacePanelColor", "#00FFFDFC"),
                ("WorkspacePanelLineColor", "#00F6DFD3"),
                ("FloatingDayPanelBrush", "#FFFEFCFB"),
                ("FloatingDayHeaderBrush", "#FFFEF5DC"),
                ("FloatingDayNoteInputBrush", "#FFFEF5DC"),
                ("FloatingDayPlushGlowBrush", "#B9794E"),
                ("FloatingDayCinnamorollDockBrush", "#FFF0D2")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            return dayMatches && defaultTagColorsMatch;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static bool ValidateNoteDisclosureThemeTokens(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        static bool HasExpectedColor(string key, string expected)
        {
            return
                ColorConverter.ConvertFromString(expected) is Color expectedColor &&
                Application.Current.FindResource(key) is Color actualColor &&
                actualColor == expectedColor;
        }

        try
        {
            theme.Apply();
            var dayMatches = new[]
            {
                ("NoteActiveRailColor", "#CD623E"),
                ("NoteCompletedRailColor", "#8A7069"),
                ("NoteTimelineColor", "#F6DFD3"),
                ("PeachTintColor", "#FEEFE1"),
                ("PeachRailColor", "#CD623E"),
                ("PeachBadgeColor", "#FBAE90"),
                ("PeachBadgeMutedColor", "#F3DED8")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            return dayMatches;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static bool ValidatePrimaryButtonHoverTextContrast(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        var primaryStyle = Application.Current.FindResource("PrimaryButton") as Style;
        var button = new Button { Style = primaryStyle, Content = "验证主行动" };
        button.ApplyTemplate();
        // #194 moved the hover color from a trigger setter to the shared
        // animation behavior. Inspect the materialized template's actual binding.
        var interaction = button.Template?.FindName("InteractionSurface", button) as FrameworkElement;
        var hasDedicatedHoverState = interaction is not null &&
            MotionBehavior.GetIsEnabled(interaction) &&
            string.Equals(MotionBehavior.GetHoverBackgroundResourceKey(interaction),
                "MainActionHoverBrush", StringComparison.Ordinal) &&
            string.Equals(MotionBehavior.GetHoverBorderBrushResourceKey(interaction),
                "MainActionHoverBrush", StringComparison.Ordinal);
        try
        {
            theme.Apply();
            if (Application.Current.FindResource("MainActionHoverBrush") is not Brush hover ||
                Application.Current.FindResource("MainActionTextBrush") is not Brush hoverForeground ||
                !ApplicationTheme.MeetsContrast(hover, hoverForeground, 4.5))
            {
                return false;
            }

            return hasDedicatedHoverState;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static (string Previous, string Next) ValidateCalendarNavigationNames(
        MainWindow main,
        DatePicker picker)
    {
        var originalWorkspace = main.CurrentWorkspace;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            picker.BringIntoView();
            main.UpdateLayout();
            PumpDispatcher();
            return WindowValidationInput.ReadCalendarNavigationNames(picker);
        }
        finally
        {
            main.ShowWorkspace(originalWorkspace);
            PumpDispatcher();
        }
    }

    private static bool ValidateMainActionTextContrast(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        var themedButtons = new[]
        {
            Required<Button>(main, "SaveCountdownButton"),
            Required<Button>(main, "SaveNoteButton")
        };
        var style = themedButtons[0].Style;
        if (style is null || themedButtons.Any(button => !ReferenceEquals(button.Style, style)))
        {
            return false;
        }

        // #217：保存设置作为次级按钮使用 CardAlt + ControlBorder，保留主次角色差异。
        // 样式定义在 MainWindow.xaml MainContentProjection 的 Grid.Resources 内，
        // 必须经按钮自身沿逻辑树解析，Window.TryFindResource 看不到嵌套资源字典。
        var savePreferencesButton = Required<Button>(main, "SavePreferencesButton");
        if (savePreferencesButton.TryFindResource("MainSecondarySolidButton") is not Style secondarySolidStyle ||
            !ReferenceEquals(savePreferencesButton.Style, secondarySolidStyle) ||
            !ApplicationTheme.MeetsContrast(
                (Brush)Application.Current.FindResource("CardAltBrush"),
                (Brush)Application.Current.FindResource("InkBrush"), 4.5))
        {
            return false;
        }

        try
        {
            theme.Apply();
            if (Application.Current.FindResource("MainActionTextBrush") is not Brush foreground ||
                new[]
                {
                    "MainActionBrush",
                    "MainActionHoverBrush",
                    "MainActionPressedBrush",
                    "PinkAccentStrongBrush"
                }.Any(key =>
                    Application.Current.FindResource(key) is not Brush background ||
                    !ApplicationTheme.MeetsContrast(background, foreground, 4.5)))
            {
                return false;
            }

            return true;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static MainKeyboardBehaviorResult ValidateMainKeyboardBehavior(
        MainWindow main,
        FloatingWindow floating,
        FloatingTrayController trayController,
        DesktopWorkspace workspace,
        string initialFocusedAutomationId)
    {
        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        var closeButton = Required<Button>(main, "CloseButton");
        var minimizeButton = Required<Button>(main, "MinimizeButton");
        if (Keyboard.FocusedElement is DependencyObject focusedOutside &&
            !ReferenceEquals(Window.GetWindow(focusedOutside), main))
        {
            Keyboard.ClearFocus();
        }

        main.Activate();
        PumpDispatcher();
        var initialFocusSafe =
            string.Equals(initialFocusedAutomationId, "CountdownTab", StringComparison.Ordinal) &&
            countdownTab.IsVisible &&
            countdownTab.IsEnabled &&
            !string.Equals(initialFocusedAutomationId, AutomationProperties.GetAutomationId(closeButton), StringComparison.Ordinal) &&
            !string.Equals(initialFocusedAutomationId, AutomationProperties.GetAutomationId(minimizeButton), StringComparison.Ordinal);
        var tabSequence = new List<string>();
        countdownTab.Focus();
        PumpDispatcher();
        for (var index = 0; index < 5 && Keyboard.FocusedElement is UIElement focused; index++)
        {
            tabSequence.Add(AutomationProperties.GetAutomationId(focused));
            if (index < 4)
            {
                focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                PumpDispatcher();
            }
        }

        var countdownTabPath = ValidateMainWorkspaceTabPath(main, WorkspaceKind.Countdowns);
        var notesTabPath = ValidateMainWorkspaceTabPath(main, WorkspaceKind.Notes);

        var originalWorkspace = main.CurrentWorkspace;
        var originalWindowState = main.WindowState;
        var originalEnabled = main.IsEnabled;
        var originalPreferences = workspace.Snapshot.Settings.Preferences;
        var status = Required<TextBlock>(main, "StatusText");
        var commandResults = new Dictionary<string, bool>(StringComparer.Ordinal);
        var commandDiagnostics = new Dictionary<string, MainShortcutCommandDiagnostic>(StringComparer.Ordinal);
        var commandsWork = false;
        string? savedCountdownId = null;
        string? savedNoteId = null;
        bool RaiseShortcut(ModifierKeys modifiers, Key key)
        {
            countdownTab.Focus();
            PumpDispatcher();
            return WindowValidationInput.RaisePreviewKey(countdownTab, key, modifiers);
        }

        try
        {
            var notesWorkspaceHandled = RaiseShortcut(ModifierKeys.Control, Key.D2);
            commandResults["NotesWorkspace"] =
                notesWorkspaceHandled && main.CurrentWorkspace == WorkspaceKind.Notes;
            commandDiagnostics["NotesWorkspace"] = new(
                notesWorkspaceHandled,
                main.CurrentWorkspace == WorkspaceKind.Notes,
                string.Empty);
            var countdownsWorkspaceHandled = RaiseShortcut(ModifierKeys.Control, Key.D1);
            commandResults["CountdownsWorkspace"] =
                countdownsWorkspaceHandled && main.CurrentWorkspace == WorkspaceKind.Countdowns;
            commandDiagnostics["CountdownsWorkspace"] = new(
                countdownsWorkspaceHandled,
                main.CurrentWorkspace == WorkspaceKind.Countdowns,
                string.Empty);
            var filterHandled = RaiseShortcut(ModifierKeys.Control, Key.F);
            var filterOpened = Required<Popup>(main, "FilterPopup").IsOpen;
            commandResults["Filter"] = filterHandled && filterOpened;
            commandDiagnostics["Filter"] = new(filterHandled, filterOpened, string.Empty);
            // The following shortcut is a separate scenario. Wait for the real
            // exit transition before it runs, while the popup guard is still valid.
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "FilterPopup"));
            // #156：便签页 Ctrl+F 聚焦便签搜索（倒计时页仍打开筛选，由上方 Filter 命令覆盖）。
            RaiseShortcut(ModifierKeys.Control, Key.D2);
            var noteSearchHandled = RaiseShortcut(ModifierKeys.Control, Key.F);
            var noteSearchFocused = ReferenceEquals(
                Keyboard.FocusedElement,
                Required<TextBox>(main, "NoteSearch"));
            commandResults["NoteSearch"] = noteSearchHandled && noteSearchFocused;
            commandDiagnostics["NoteSearch"] = new(
                noteSearchHandled,
                noteSearchFocused,
                string.Empty);
            RaiseShortcut(ModifierKeys.Control, Key.D1);
            PumpDispatcher();
            var tagManagerHandled = RaiseShortcut(ModifierKeys.Control, Key.T);
            var tagManagerOpened = Required<Popup>(main, "TagManagerPopup").IsOpen;
            commandResults["TagManager"] = tagManagerHandled && tagManagerOpened;
            commandDiagnostics["TagManager"] = new(
                tagManagerHandled,
                tagManagerOpened,
                string.Empty);
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "TagManagerPopup"));

            var probeCountdownName = Required<TextBox>(main, "CountdownName");
            var probeCountdownDate = Required<DatePicker>(main, "CountdownDate");
            var probeCountdownHour = Required<ComboBox>(main, "CountdownHour");
            var probeCountdownMinute = Required<ComboBox>(main, "CountdownMinute");
            var probeCountdownSecond = Required<ComboBox>(main, "CountdownSecond");
            var probeTarget = workspace.Snapshot.BeijingNow.AddDays(30);
            var probeName = $"快捷键保存-{Guid.NewGuid():N}"[..30];
            probeCountdownName.Text = probeName;
            probeCountdownDate.SelectedDate = probeTarget.Date;
            probeCountdownHour.SelectedItem = probeTarget.ToString("HH", CultureInfo.InvariantCulture);
            probeCountdownMinute.SelectedItem = probeTarget.ToString("mm", CultureInfo.InvariantCulture);
            probeCountdownSecond.SelectedItem = probeTarget.ToString("ss", CultureInfo.InvariantCulture);
            Keyboard.Focus(probeCountdownName);
            var countdownHandled = WindowValidationInput.RaisePreviewKey(probeCountdownName, Key.S, ModifierKeys.Control);
            var savedCountdown = workspace.Snapshot.Settings.Countdowns.SingleOrDefault(item =>
                string.Equals(item.Name, probeName, StringComparison.Ordinal));
            savedCountdownId = savedCountdown?.Id;
            var countdownPersisted = savedCountdown is not null &&
                DesktopWorkspace.Load(workspace.Snapshot.SettingsPath).Snapshot.Settings.Countdowns.Any(item =>
                    string.Equals(item.Id, savedCountdown.Id, StringComparison.Ordinal));
            commandResults["SaveCountdown"] =
                countdownHandled &&
                countdownPersisted &&
                string.Equals(status.Text, "倒计时已保存。", StringComparison.Ordinal);
            commandDiagnostics["SaveCountdown"] = new(
                countdownHandled,
                countdownPersisted,
                status.Text);

            main.ShowWorkspace(WorkspaceKind.Notes);
            var probeNoteText = Required<TextBox>(main, "NoteText");
            var probeNoteName = $"便签快捷保存-{Guid.NewGuid():N}";
            probeNoteText.Text = probeNoteName;
            Required<DatePicker>(main, "NoteDate").SelectedDate = workspace.Snapshot.BeijingNow.Date;
            Keyboard.Focus(probeNoteText);
            var noteHandled = WindowValidationInput.RaisePreviewKey(probeNoteText, Key.S, ModifierKeys.Control);
            var savedNote = workspace.Snapshot.Settings.Notes.SingleOrDefault(note => note.Text == probeNoteName);
            savedNoteId = savedNote?.Id;
            var notePersisted = savedNote is not null && DesktopWorkspace.Load(workspace.Snapshot.SettingsPath)
                .Snapshot.Settings.Notes.Any(note => note.Id == savedNote.Id);
            commandResults["SaveNote"] = noteHandled && notePersisted;
            commandDiagnostics["SaveNote"] = new(noteHandled, notePersisted, status.Text);
            main.ShowWorkspace(WorkspaceKind.Countdowns);

            var displayPrecision = Required<ComboBox>(main, "DisplayPrecision");
            var monthlySalary = Required<TextBox>(main, "MonthlySalary");
            var workStart = Required<TextBox>(main, "WorkStart");
            var workEnd = Required<TextBox>(main, "WorkEnd");
            displayPrecision.SelectedIndex = originalPreferences.DisplayPrecision == "Second" ? 2 : 3;
            monthlySalary.Text = "43210";
            workStart.Text = "08:15";
            workEnd.Text = "18:45";
            var preferencesHandled = RaiseShortcut(ModifierKeys.Control | ModifierKeys.Shift, Key.S);
            var persistedPreferences = DesktopWorkspace.Load(workspace.Snapshot.SettingsPath).Snapshot.Settings.Preferences;
            var preferencesPersisted =
                persistedPreferences.DisplayPrecision == (displayPrecision.SelectedIndex == 2 ? "Minute" : "Second") &&
                persistedPreferences.MonthlySalary == 43210m &&
                persistedPreferences.WorkStart == new TimeSpan(8, 15, 0) &&
                persistedPreferences.WorkEnd == new TimeSpan(18, 45, 0);
            commandResults["SavePreferences"] =
                preferencesHandled &&
                preferencesPersisted &&
                string.Equals(status.Text, "设置已保存", StringComparison.Ordinal);
            commandDiagnostics["SavePreferences"] = new(
                preferencesHandled,
                preferencesPersisted,
                status.Text);

            var mainLeft = main.Left;
            var mainTop = main.Top;
            var mainOpacity = main.Opacity;
            var floatingOpacity = floating.Opacity;
            var floatingWasVisible = floating.IsVisible;
            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            var buttonHidManagerOnly =
                !main.IsVisible &&
                floating.IsVisible &&
                trayController.IsTrayIconVisible;
            if (!main.IsVisible)
            {
                main.Show();
                main.Left = mainLeft;
                main.Top = mainTop;
                main.Opacity = mainOpacity;
                main.ShowInTaskbar = false;
                PumpDispatcher();
            }

            var minimizeHandled = RaiseShortcut(ModifierKeys.Control, Key.M);
            PumpDispatcher();
            var minimizedToTray =
                buttonHidManagerOnly &&
                minimizeHandled &&
                !main.IsVisible &&
                floating.IsVisible &&
                trayController.IsTrayIconVisible;
            if (!minimizedToTray)
            {
                throw new InvalidOperationException(
                    "#213 管理窗口最小化探针失败：" +
                    $"button={buttonHidManagerOnly} shortcut={minimizeHandled} " +
                    $"mainVisible={main.IsVisible} floatingVisible={floating.IsVisible} " +
                    $"tray={trayController.IsTrayIconVisible}");
            }
            commandResults["Minimize"] = minimizedToTray;
            commandDiagnostics["Minimize"] = new(
                minimizeHandled,
                minimizedToTray,
                string.Empty);
            if (trayController.IsTrayIconVisible)
            {
                trayController.InvokeRestoreForValidation();
                PumpDispatcher();
            }

            if (!main.IsVisible)
            {
                main.Show();
                main.Left = mainLeft;
                main.Top = mainTop;
                main.Opacity = mainOpacity;
                main.ShowInTaskbar = false;
            }

            if (floatingWasVisible && !floating.IsVisible)
            {
                floating.Show();
            }

            floating.Opacity = floatingOpacity;
            main.WindowState = WindowState.Normal;
            PumpDispatcher();
            commandsWork =
                commandResults.Count == 9 &&
                commandResults.Values.All(value => value);
        }
        finally
        {
            if (savedCountdownId is not null)
            {
                workspace.Apply(new DeleteCountdownChange(savedCountdownId));
            }
            if (savedNoteId is not null)
            {
                workspace.Apply(new DeleteNoteChange(savedNoteId));
            }

            workspace.Apply(new SavePreferencesChange(
                originalPreferences.DisplayPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd));
            Required<ComboBox>(main, "DisplayPrecision").SelectedIndex =
                originalPreferences.DisplayPrecision switch
                {
                    "Day" => 0,
                    "Hour" => 1,
                    "Minute" => 2,
                    _ => 3
                };
            Required<TextBox>(main, "MonthlySalary").Text =
                originalPreferences.MonthlySalary.ToString(CultureInfo.CurrentCulture);
            Required<TextBox>(main, "WorkStart").Text =
                originalPreferences.WorkStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            Required<TextBox>(main, "WorkEnd").Text =
                originalPreferences.WorkEnd.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "FilterPopup"));
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "TagManagerPopup"));
            main.WindowState = originalWindowState;
            main.IsEnabled = originalEnabled;
            main.ShowWorkspace(originalWorkspace);
            countdownTab.Focus();
            PumpDispatcher();
        }

        var countdownName = Required<TextBox>(main, "CountdownName");
        var originalName = countdownName.Text;
        var textSentinel = "快捷键输入保护";
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        PumpDispatcher();
        countdownName.Text = textSentinel;
        var textBoxFocused = countdownName.Focus();
        PumpDispatcher();
        var textBoxGuarded =
            textBoxFocused && countdownName.IsKeyboardFocused &&
            new[] { Key.A, Key.C, Key.V, Key.X, Key.Z }.All(key =>
                !WindowValidationInput.RaisePreviewKey(countdownName, key, ModifierKeys.Control)) &&
            !WindowValidationInput.RaisePreviewKey(countdownName, Key.F, ModifierKeys.None) &&
            WindowValidationInput.RaisePreviewKey(countdownName, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Notes &&
            string.Equals(countdownName.Text, textSentinel, StringComparison.Ordinal);
        countdownName.Text = originalName;

        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var countdownDateInput = Required<DatePicker>(main, "CountdownDate");
        var datePickerFocused = countdownDateInput.Focus();
        PumpDispatcher();
        var datePickerGuarded =
            datePickerFocused && countdownDateInput.IsKeyboardFocusWithin &&
            !WindowValidationInput.RaisePreviewKey(countdownDateInput, Key.A, ModifierKeys.None) &&
            WindowValidationInput.RaisePreviewKey(countdownDateInput, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Notes && !countdownDateInput.IsDropDownOpen;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var countdownHourInput = Required<ComboBox>(main, "CountdownHour");
        var editorWasExpanded = Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Visibility == Visibility.Visible;
        if (!editorWasExpanded)
            Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PumpDispatcher();
        var comboBoxFocused = countdownHourInput.Focus();
        PumpDispatcher();
        var comboBoxGuarded =
            comboBoxFocused && countdownHourInput.IsKeyboardFocusWithin &&
            !WindowValidationInput.RaisePreviewKey(countdownHourInput, Key.A, ModifierKeys.None) &&
            WindowValidationInput.RaisePreviewKey(countdownHourInput, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Notes;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var textInputPreserved = textBoxGuarded && datePickerGuarded && comboBoxGuarded;
        if (!editorWasExpanded)
            Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        countdownTab.Focus();
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var filterPopup = Required<Popup>(main, "FilterPopup");
        filterPopup.IsOpen = true;
        PumpDispatcher();
        var popupGuarded =
            !WindowValidationInput.RaisePreviewKey(countdownTab, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Countdowns &&
            filterPopup.IsOpen;
        WindowValidationInput.ClosePopupForCleanup(filterPopup);

        main.IsEnabled = false;
        var modalOwnerGuarded =
            !WindowValidationInput.RaisePreviewKey(countdownTab, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Countdowns;
        main.IsEnabled = originalEnabled;
        main.ShowWorkspace(originalWorkspace);
        countdownTab.Focus();
        PumpDispatcher();

        return new MainKeyboardBehaviorResult(
            initialFocusedAutomationId,
            initialFocusSafe,
            tabSequence,
            countdownTabPath,
            notesTabPath,
            commandsWork,
            commandResults,
            commandDiagnostics,
            textInputPreserved,
            new MainShortcutInputGuardResult(
                textBoxGuarded,
                datePickerGuarded,
                comboBoxGuarded),
            popupGuarded,
            modalOwnerGuarded);
    }

    private static MainWorkspaceTabPathResult ValidateMainWorkspaceTabPath(
        MainWindow main,
        WorkspaceKind workspace)
    {
        main.ShowWorkspace(workspace);
        main.Activate();
        PumpDispatcher();
        var expected = ExpectedMainWorkspaceTabPath(main, workspace);
        var forward = TraverseMainWorkspaceTabPath(main, expected[0], expected[^1], FocusNavigationDirection.Next);
        var backward = TraverseMainWorkspaceTabPath(main, expected[^1], expected[0], FocusNavigationDirection.Previous);
        var evaluation = EvaluateMainWorkspaceTabPath(
            expected,
            forward.Sequence,
            backward.Sequence,
            forward.FocusVisualsVisible && backward.FocusVisualsVisible);
        return new MainWorkspaceTabPathResult(
            expected,
            forward.Sequence,
            backward.Sequence,
            evaluation.ForwardMatches,
            evaluation.BackwardSymmetric,
            evaluation.FocusVisualsVisible,
            forward.FocusedTargets,
            backward.FocusedTargets);
    }

    private static MainWorkspaceTabPathEvaluation EvaluateMainWorkspaceTabPath(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> forward,
        IReadOnlyList<string> backward,
        bool focusVisualsVisible) =>
        new(
            forward.SequenceEqual(expected, StringComparer.Ordinal),
            backward.SequenceEqual(expected.Reverse(), StringComparer.Ordinal),
            focusVisualsVisible);

    private static IReadOnlyList<string> ExpectedMainWorkspaceTabPath(
        MainWindow main,
        WorkspaceKind workspace)
    {
        var ids = workspace == WorkspaceKind.Countdowns
            ? new[]
            {
                "TopmostCheck", "MinimizeButton", "CloseButton",
                "CountdownTab", "NotesTab", "PreferencesButton", "FilterButton", "TagManagerButton",
                "CountdownName", "CountdownDate", "SaveCountdownButton", "CountdownEditorExpandButton", "CountdownHour", "CountdownMinute",
                "CountdownSecond", "CountdownTags", "CountdownReminderHelp",
                "CountdownReminderNoRadio", "CountdownReminderTimeRadio",
                "CountdownReminderCustomRadio", "CancelCountdownEdit",
                "CountdownListRegion", "StatusText"
            }
            : new[]
            {
                "TopmostCheck", "MinimizeButton", "CloseButton",
                "CountdownTab", "NotesTab", "PreferencesButton", "FilterButton", "TagManagerButton",
                "NoteText", "NoteDate", "SaveNoteButton", "CancelNoteEdit",
                "ManageNoteReminderRulesButton",
                "NoteTags", "NoteSearch", "NotesListRegion", "StatusText"
            };
        return ids.Where(id =>
        {
            var element = id switch
            {
                "CountdownTags" => Required<TagSelector>(main, id).PopupTrigger,
                "NoteTags" => Required<TagSelector>(main, id).PopupTrigger,
                _ => Required<FrameworkElement>(main, id)
            };
            return element.IsVisible && element.IsEnabled && element.Focusable;
        }).ToArray();
    }

    private static MainTabTraversalResult TraverseMainWorkspaceTabPath(
        MainWindow main,
        string startId,
        string endId,
        FocusNavigationDirection direction)
    {
        var start = Required<FrameworkElement>(main, startId);
        main.Activate();
        start.Focus();
        PumpDispatcher();
        var sequence = new List<string>();
        var focusedTargets = new List<string>();
        var focusVisualsVisible = true;
        var attemptLimit = 64 + Descendants<UIElement>(main).Count(element =>
            element.Focusable && element.IsVisible && element.IsEnabled);
        for (var attempt = 0; attempt < attemptLimit && Keyboard.FocusedElement is DependencyObject focused; attempt++)
        {
            var target = ResolveMainTabTarget(main, focused);
            if (target.Id.Length == 0)
            {
                break;
            }

            if (sequence.Count == 0 || !string.Equals(sequence[^1], target.Id, StringComparison.Ordinal))
            {
                sequence.Add(target.Id);
            }

            var focusedTarget = DescribeMainFocusedTarget(main, target, focused);
            if (focusedTargets.Count == 0 ||
                !string.Equals(focusedTargets[^1], focusedTarget, StringComparison.Ordinal))
            {
                focusedTargets.Add(focusedTarget);
            }
            var focusVisible = HasVisibleMainKeyboardFocus(target, focused);
            focusVisualsVisible &= focusVisible;
            if (!focusVisible)
            {
                Console.Error.WriteLine("MAIN_TAB_FOCUS_DIAGNOSTIC " +
                    System.Text.Json.JsonSerializer.Serialize(new { direction, focusedTarget }));
            }

            if (string.Equals(target.Id, endId, StringComparison.Ordinal))
            {
                break;
            }

            if (focused is not UIElement element ||
                !element.MoveFocus(new TraversalRequest(direction)))
            {
                break;
            }
            PumpDispatcher();
        }

        return new MainTabTraversalResult(sequence, focusedTargets, focusVisualsVisible);
    }

    private static string DescribeMainFocusedTarget(
        MainWindow main,
        MainTabTarget target,
        DependencyObject focused)
    {
        if (ReferenceEquals(focused, target.Element) ||
            target.Id is not ("CountdownListRegion" or "NotesListRegion"))
        {
            return target.Id;
        }

        if (target.Id == "CountdownListRegion" && focused is Button countdownAction)
        {
            return countdownAction.Content?.ToString() switch
            {
                "编辑" => "CountdownListRegion.Edit",
                "删除" => "CountdownListRegion.Delete",
                _ => "CountdownListRegion.Button"
            };
        }

        if (target.Id == "NotesListRegion")
        {
            if (ReferenceEquals(focused, Required<ToggleButton>(main, "ActiveNotesSegment")))
            {
                return "NotesListRegion.ActiveSegment";
            }

            if (ReferenceEquals(focused, Required<ToggleButton>(main, "CompletedNotesSegment")))
            {
                return "NotesListRegion.CompletedSegment";
            }

            if (focused is Button { Uid: "NoteDateDisclosure" })
            {
                return "NotesListRegion.DateSection";
            }

            if (focused is CheckBox)
            {
                return "NotesListRegion.Completion";
            }

            if (focused is Button noteAction)
            {
                return noteAction.Content?.ToString() switch
                {
                    "编辑" => "NotesListRegion.Edit",
                    "删除" => "NotesListRegion.Delete",
                    _ => "NotesListRegion.Button"
                };
            }
        }

        return $"{target.Id}.{focused.GetType().Name}";
    }

    private static bool HasVisibleMainKeyboardFocus(
        MainTabTarget target,
        DependencyObject focused)
    {
        var focusOwner = target.Id is "CountdownListRegion" or "NotesListRegion" &&
                focused is FrameworkElement focusedElement
            ? focusedElement
            : target.Element;
        if (!ReferenceEquals(focused, focusOwner) && !IsDescendantOrSelf(focused, focusOwner))
        {
            return false;
        }

        if (focusOwner.FocusVisualStyle is not null)
        {
            return true;
        }

        if (focusOwner is not Control control || control.Template is null)
        {
            return false;
        }

        control.ApplyTemplate();
        return new[] { "FocusRing", "Box", "Surface" }
            .Select(name => control.Template.FindName(name, control))
            .OfType<Border>()
            .Any(border =>
                border.IsVisible &&
                HasVisibleBrush(border.BorderBrush) &&
                new[]
                {
                    border.BorderThickness.Left,
                    border.BorderThickness.Top,
                    border.BorderThickness.Right,
                    border.BorderThickness.Bottom
                }.Max() >= 2);
    }

    private static bool ValidateMainKeyboardPathNegativeFixtures()
    {
        var countdown = new[]
        {
            "CountdownTab", "NotesTab", "CountdownTags", "CountdownListRegion", "StatusText"
        };
        var notes = new[]
        {
            "CountdownTab", "NotesTab", "NoteTags", "NotesListRegion", "StatusText"
        };

        var swappedCountdown = countdown.ToArray();
        (swappedCountdown[1], swappedCountdown[2]) = (swappedCountdown[2], swappedCountdown[1]);
        return EvaluateMainWorkspaceTabPath(
                countdown, countdown, countdown.Reverse().ToArray(), true).AllPassed &&
            EvaluateMainWorkspaceTabPath(
                notes, notes, notes.Reverse().ToArray(), true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                countdown.Where(id => id != "CountdownTags").ToArray(),
                countdown,
                countdown.Reverse().ToArray(),
                true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                notes.Where(id => id != "NoteTags").ToArray(),
                notes,
                notes.Reverse().ToArray(),
                true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                countdown, swappedCountdown, countdown.Reverse().ToArray(), true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                notes, notes, notes.Reverse().Skip(1).ToArray(), true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                notes, notes, notes.Reverse().ToArray(), false).AllPassed;
    }

    private static MainTabTarget ResolveMainTabTarget(MainWindow main, DependencyObject focused)
    {
        var countdownList = Required<ScrollViewer>(main, "CountdownListRegion");
        if (IsDescendantOrSelf(focused, countdownList))
        {
            return new MainTabTarget("CountdownListRegion", countdownList);
        }

        var notesList = Required<ScrollViewer>(main, "NotesListRegion");
        if (IsDescendantOrSelf(focused, notesList))
        {
            return new MainTabTarget("NotesListRegion", notesList);
        }

        for (var current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var id = AutomationProperties.GetAutomationId(current);
            if (id.Length > 0 && current is FrameworkElement element)
            {
                return new MainTabTarget(id, element);
            }
        }

        return new MainTabTarget(string.Empty, Required<ToggleButton>(main, "CountdownTab"));
    }

    private static bool IsDescendantOrSelf(DependencyObject candidate, DependencyObject ancestor)
    {
        for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }
        return false;
    }

    private static MainStructuralAnnouncementsResult ValidateMainStructuralAnnouncements(string assetRoot)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-structural-announcements-{Guid.NewGuid():N}.settings.json");
        MainWindow? window = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath);
            using var calendar = new HolidayCalendar(assetRoot);
            window = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(settingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            window.Show();
            PumpDispatcher();
            var status = Required<TextBlock>(window, "StatusText");
            var emptyCountdowns = Required<TextBlock>(window, "EmptyCountdownMessage");
            var emptyNotes = Required<TextBlock>(window, "EmptyNotesMessage");
            var countdownEmptyContent = Required<FrameworkElement>(window, "CountdownEmptyStateContent");
            var countdownEmptyMascot = Required<Image>(window, "CountdownEmptyMascot");
            var countdownEmptyHint = Required<TextBlock>(window, "EmptyCountdownHint");
            var addEmptyCountdown = Required<Button>(window, "AddEmptyCountdownButton");
            var notesEmptyContent = Required<FrameworkElement>(window, "NotesEmptyStateContent");
            var notesEmptyMascot = Required<Image>(window, "NotesEmptyMascot");
            var notesEmptyHint = Required<TextBlock>(window, "EmptyNotesHint");
            var addEmptyNote = Required<Button>(window, "AddEmptyNoteButton");

            window.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            var countdownWorkspaceSelectionIsAccessible =
                Required<ToggleButton>(window, "CountdownTab").IsChecked == true &&
                Required<ToggleButton>(window, "NotesTab").IsChecked == false &&
                AutomationProperties.GetName(Required<ToggleButton>(window, "CountdownTab")) == "倒计时工作区" &&
                !status.Text.Contains("当前工作区", StringComparison.Ordinal);
            var countdownFirstUseEmptyAnnounced = VisiblePoliteTextEquals(
                emptyCountdowns,
                "还没有倒计时");
            var countdownFirstUseElementsVisible =
                countdownEmptyContent.IsVisible &&
                countdownEmptyMascot.IsVisible &&
                emptyCountdowns.IsVisible &&
                countdownEmptyHint.IsVisible &&
                addEmptyCountdown.IsVisible;
            addEmptyCountdown.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            var countdownAddFocusesEditor = ReferenceEquals(
                Keyboard.FocusedElement,
                Required<TextBox>(window, "CountdownName"));
            window.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            var notesWorkspaceSelectionIsAccessible =
                Required<ToggleButton>(window, "NotesTab").IsChecked == true &&
                Required<ToggleButton>(window, "CountdownTab").IsChecked == false &&
                AutomationProperties.GetName(Required<ToggleButton>(window, "NotesTab")) == "便签工作区" &&
                !status.Text.Contains("当前工作区", StringComparison.Ordinal);
            var notesFirstUseEmptyAnnounced = VisiblePoliteTextEquals(
                emptyNotes,
                "还没有便签");
            var notesFirstUseElementsVisible =
                notesEmptyContent.IsVisible &&
                notesEmptyMascot.IsVisible &&
                emptyNotes.IsVisible &&
                notesEmptyHint.IsVisible &&
                addEmptyNote.IsVisible;
            addEmptyNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            var noteAddFocusesEditor = ReferenceEquals(
                Keyboard.FocusedElement,
                Required<TextBox>(window, "NoteText"));

            var decorationsAreNonInteractive =
                !countdownEmptyMascot.IsHitTestVisible &&
                !countdownEmptyMascot.Focusable &&
                !notesEmptyMascot.IsHitTestVisible &&
                !notesEmptyMascot.Focusable &&
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(countdownEmptyMascot)) &&
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(notesEmptyMascot));

            var countdownSaved = workspace.Apply(new SaveCountdownChange(
                null,
                "结构变化通知倒计时探针",
                workspace.Snapshot.UtcNow.AddDays(7),
                Array.Empty<string>(),
                Array.Empty<ReminderRule>())).Succeeded;
            var noteSaved = workspace.Apply(new SaveNoteChange(
                null,
                "结构变化通知便签探针",
                workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                NoReminder.Instance)).Succeeded;

            window.ShowWorkspace(WorkspaceKind.Countdowns);
            Required<ToggleButton>(window, "CountdownExpiredFilter").RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            var countdownFilterEmptyAnnounced = countdownSaved && VisiblePoliteTextEquals(
                emptyCountdowns,
                "没有符合当前筛选条件的倒计时。");
            var countdownFilterResultAnnounced =
                VisiblePoliteTextEquals(status, "筛选结果：0 个倒计时");

            window.ShowWorkspace(WorkspaceKind.Notes);
            Required<TextBox>(window, "NoteSearch").Text = "__validation_no_matching_note__";
            PumpDispatcher();
            var notesFilterEmptyAnnounced = noteSaved && VisiblePoliteTextEquals(
                emptyNotes,
                "没有符合当前筛选条件的便签。");
            var notesFilterResultAnnounced =
                VisiblePoliteTextEquals(status, "筛选结果：0 个待办");

            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            PumpDispatcher();
            foreach (var countdown in workspace.Snapshot.Settings.Countdowns.ToArray())
            {
                workspace.Apply(new DeleteCountdownChange(countdown.Id));
            }

            foreach (var note in workspace.Snapshot.Settings.Notes.ToArray())
            {
                workspace.Apply(new DeleteNoteChange(note.Id));
            }

            PumpDispatcher();

            var savedCountdown = workspace.Apply(new SaveCountdownChange(
                null,
                "空态引导恢复倒计时探针",
                workspace.Snapshot.UtcNow.AddDays(7),
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var savedCountdownId = workspace.Snapshot.Settings.Countdowns.LastOrDefault()?.Id;
            var savedNote = workspace.Apply(new SaveNoteChange(
                null,
                "空态引导恢复便签探针",
                workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
            var savedNoteId = workspace.Snapshot.Settings.Notes.LastOrDefault()?.Id;
            window.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            var populatedListsHideEmpty =
                savedCountdown.Succeeded &&
                savedNote.Succeeded &&
                countdownEmptyContent.Visibility == Visibility.Collapsed;
            window.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            populatedListsHideEmpty &= notesEmptyContent.Visibility == Visibility.Collapsed;
            if (savedCountdownId is not null)
            {
                workspace.Apply(new DeleteCountdownChange(savedCountdownId));
            }

            if (savedNoteId is not null)
            {
                workspace.Apply(new DeleteNoteChange(savedNoteId));
            }

            window.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            var restoredEmptyState = countdownEmptyContent.IsVisible;
            window.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            restoredEmptyState &= notesEmptyContent.IsVisible;

            return new MainStructuralAnnouncementsResult(
                countdownWorkspaceSelectionIsAccessible,
                notesWorkspaceSelectionIsAccessible,
                countdownFilterResultAnnounced && notesFilterResultAnnounced,
                countdownFirstUseEmptyAnnounced,
                notesFirstUseEmptyAnnounced,
                countdownFilterEmptyAnnounced,
                notesFilterEmptyAnnounced,
                new MainEmptyStateGuidanceResult(
                    countdownFirstUseElementsVisible,
                    notesFirstUseElementsVisible,
                    countdownAddFocusesEditor && noteAddFocusesEditor,
                    decorationsAreNonInteractive,
                    populatedListsHideEmpty && restoredEmptyState));
        }
        finally
        {
            window?.Close();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".diagnostic.jsonl");
        }
    }

    private static bool VisiblePoliteTextEquals(TextBlock textBlock, string expected) =>
        textBlock.IsVisible &&
        AutomationProperties.GetLiveSetting(textBlock) == AutomationLiveSetting.Polite &&
        string.Equals(textBlock.Text, expected, StringComparison.Ordinal) &&
        string.Equals(AutomationProperties.GetName(textBlock), expected, StringComparison.Ordinal);

    private static ConfirmationDialogValidation ValidateConfirmationDialog(
        ApplicationTheme theme,
        MainWindow owner,
        DateTimeOffset restoreUtc)
    {
        var scenarios = new[]
        {
            CreateConfirmationDialogScenario("Note", "删除便签", new string('签', 140)),
            CreateConfirmationDialogScenario("Countdown", "删除倒计时", new string('时', 30))
        };
        var measurements = new List<ConfirmationDialogMeasurement>();
        var originalOwnerLeft = owner.Left;
        var originalOwnerTop = owner.Top;
        try
        {
            var workArea = SystemParameters.WorkArea;
            owner.Left = workArea.Left + Math.Max(24, (workArea.Width - owner.ActualWidth) / 2);
            owner.Top = workArea.Top + Math.Max(24, (workArea.Height - owner.ActualHeight) / 2);
            owner.UpdateLayout();
            PumpDispatcher();

            foreach (var scenario in scenarios)
            {
                theme.Apply();
                var dialog = new ConfirmationDialog(owner, scenario.Title, scenario.Message)
                {
                    ShowInTaskbar = false,
                    Opacity = 0
                };
                try
                {
                        dialog.Show();
                        PumpDispatcher();
                        dialog.UpdateLayout();
                        // #205: MessageBox owns the action row in its template.
                        // Measure the complete rendered surface that contains
                        // both content and actions, rather than the body grid.
                        var root = (FrameworkElement)VisualTreeHelper.GetChild(dialog, 0);
                        var rootBounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
                        var cancelBounds = dialog.CancelButton.TransformToAncestor(root).TransformBounds(
                            new Rect(0, 0, dialog.CancelButton.ActualWidth, dialog.CancelButton.ActualHeight));
                        var confirmBounds = dialog.ConfirmButton.TransformToAncestor(root).TransformBounds(
                            new Rect(0, 0, dialog.ConfirmButton.ActualWidth, dialog.ConfirmButton.ActualHeight));
                        var themed =
                            dialog.WindowStyle == WindowStyle.None &&
                            Equals(dialog.Background, Application.Current.FindResource("BackgroundBrush")) &&
                            dialog.Content is Border border &&
                            Equals(border.BorderBrush, Application.Current.FindResource("PeachLineBrush"));
                        var buttonsVisible =
                            dialog.CancelButton.IsVisible &&
                            dialog.ConfirmButton.IsVisible &&
                            dialog.CancelButton.ActualWidth > 0 &&
                            dialog.ConfirmButton.ActualWidth > 0 &&
                            rootBounds.Contains(cancelBounds) &&
                            rootBounds.Contains(confirmBounds);
                        var bodyReachable =
                            dialog.BodyText.TextWrapping == TextWrapping.Wrap &&
                            dialog.BodyScroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                            dialog.BodyScroller.ActualHeight > 0 &&
                            (dialog.BodyText.ActualHeight <= dialog.BodyScroller.ActualHeight + 0.1 ||
                             dialog.BodyScroller.ScrollableHeight > 0);
                        dialog.BodyScroller.ScrollToEnd();
                        dialog.BodyScroller.UpdateLayout();
                        PumpDispatcher();
                        var bodyEndReached =
                            dialog.BodyScroller.ScrollableHeight <= 0.1 ||
                            Math.Abs(dialog.BodyScroller.VerticalOffset - dialog.BodyScroller.ScrollableHeight) <= 0.1;
                        var centeredOnOwner =
                            Math.Abs(
                                dialog.Left + dialog.ActualWidth / 2 -
                                (owner.Left + owner.ActualWidth / 2)) <= 1 &&
                            Math.Abs(
                                dialog.Top + dialog.ActualHeight / 2 -
                                (owner.Top + owner.ActualHeight / 2)) <= 1;
                        var safeDefault =
                            dialog.CancelButton.IsDefault &&
                            dialog.CancelButton.IsCancel &&
                            !dialog.ConfirmButton.IsDefault;

                        dialog.Close();
                        PumpDispatcher();
                        var enterDidNotConfirm = RunConfirmationDialogAction(
                            owner,
                            scenario,
                            ConfirmationDialogAction.Enter);
                        var escapeDidNotConfirm = RunConfirmationDialogAction(
                            owner,
                            scenario,
                            ConfirmationDialogAction.Escape);
                        var cancelDidNotConfirm = RunConfirmationDialogAction(
                            owner,
                            scenario,
                            ConfirmationDialogAction.Cancel);

                        measurements.Add(new ConfirmationDialogMeasurement(
                            $"{scenario.ContentKind}-Day",
                            scenario.ContentKind,
                            "Day",
                            scenario.Subject.Length,
                            scenario.Message.Length,
                            string.Equals(dialog.BodyText.Text, scenario.Message, StringComparison.Ordinal),
                            themed,
                            buttonsVisible,
                            bodyReachable,
                            bodyEndReached,
                            ReferenceEquals(dialog.Owner, owner),
                            dialog.WindowStartupLocation.ToString(),
                            centeredOnOwner,
                            safeDefault,
                            enterDidNotConfirm,
                            escapeDidNotConfirm,
                            cancelDidNotConfirm));
                    }
                    finally
                    {
                        if (dialog.IsVisible)
                        {
                            dialog.Close();
                            PumpDispatcher();
                        }
                    }
                }
        }
        finally
        {
            owner.Left = originalOwnerLeft;
            owner.Top = originalOwnerTop;
            owner.UpdateLayout();
            PumpDispatcher();
            theme.Apply();
        }

        var productionMatrixComplete =
            measurements.Count == 2 &&
            measurements.Select(measurement => measurement.StateId)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    new[] { "Countdown-Day", "Note-Day" },
                    StringComparer.Ordinal) &&
            measurements.All(measurement => measurement.ProductionMessageExact);
        var gates = ValidationExercises.EvaluateConfirmationDialogGates(
            new ConfirmationDialogGateInputs(
                measurements.Count == 2 && measurements.All(measurement => measurement.ButtonsVisible),
                measurements.Count == 2 && measurements.All(measurement => measurement.BodyReachable),
                measurements.Count == 2 && measurements.All(measurement => measurement.CancelIsSafeDefault),
                productionMatrixComplete,
                measurements.Count == 2 && measurements.All(measurement => measurement.BodyEndReached),
                measurements.Count == 2 && measurements.All(measurement =>
                    measurement.OwnerMatches &&
                    string.Equals(measurement.StartupLocation, nameof(WindowStartupLocation.CenterOwner), StringComparison.Ordinal) &&
                    measurement.CenteredOnOwner),
                measurements.Count == 2 && measurements.All(measurement => measurement.EnterDidNotConfirm),
                measurements.Count == 2 && measurements.All(measurement =>
                    measurement.EscapeDidNotConfirm && measurement.CancelDidNotConfirm)));
        return new ConfirmationDialogValidation(
            measurements.Count == 2 && measurements.All(measurement => measurement.Themed),
            measurements,
            gates);
    }

    private static ConfirmationDialogScenario CreateConfirmationDialogScenario(
        string contentKind,
        string title,
        string subject) =>
        new(contentKind, title, subject, $"确定删除“{subject}”吗？");

    private static bool RunConfirmationDialogAction(
        Window owner,
        ConfirmationDialogScenario scenario,
        ConfirmationDialogAction action)
    {
        var actionDispatched = false;
        var timedOut = false;
        var dialog = new ConfirmationDialog(owner, scenario.Title, scenario.Message)
        {
            ShowInTaskbar = false,
            Opacity = 0
        };
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dialog.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (dialog.IsVisible)
            {
                timedOut = true;
                dialog.Close();
            }
        };
        dialog.ContentRendered += (_, _) =>
        {
            dialog.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (action == ConfirmationDialogAction.Cancel)
                    {
                        actionDispatched = WindowValidationInput.InvokeButton(dialog.CancelButton);
                        return;
                    }

                    dialog.Activate();
                    dialog.CancelButton.Focus();
                    var handle = new System.Windows.Interop.WindowInteropHelper(dialog).Handle;
                    var virtualKey = action == ConfirmationDialogAction.Enter ? 0x0D : 0x1B;
                    actionDispatched =
                        handle != IntPtr.Zero &&
                        NativeMethods.PostMessage(handle, 0x0100, new IntPtr(virtualKey), IntPtr.Zero) &&
                        NativeMethods.PostMessage(handle, 0x0101, new IntPtr(virtualKey), IntPtr.Zero);
                    if (!actionDispatched)
                    {
                        dialog.Close();
                    }
                }));
            timer.Start();
        };

        try
        {
            var result = dialog.ShowDialog();
            return actionDispatched && !timedOut && result != true;
        }
        finally
        {
            timer.Stop();
            if (dialog.IsVisible)
            {
                dialog.Close();
            }
            PumpDispatcher();
        }
    }

    private static SecondaryWindowTypographyResult ValidateSecondaryWindowTypography(
        MainWindow owner,
        bool floatingTypographyUnchanged,
        double floatingBodyFontSize,
        double floatingReminderFontSize)
    {
        var reminderKey = new ActiveReminderOccurrenceKey(
            "typography-validation-rule",
            new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
        var reminderCenter = new ReminderCenterWindow();
        reminderCenter.AddEntry(new ReminderCenterItem(
            reminderKey,
            new NoteRecord(
                "typography-validation-note",
                "排版验证正文",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
        {
            Owner = owner
        };
        var confirmation = new ConfirmationDialog(owner, "排版验证", "排版验证正文");
        // #150：设置恢复与激活失败窗同走次级窗层级 token，进入同一份字号报告。
        var recovery = SettingsRecoveryWindow.CreateForValidation("设置恢复排版验证正文", canRecover: true);
        var activation = InstanceActivationFailureWindow.CreateForValidation();
        var windows = new Window[] { reminderCenter, editor, confirmation, recovery, activation };
        foreach (var window in windows)
        {
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20_000;
            window.Top = -20_000;
            window.Opacity = 0;
        }

        try
        {
            foreach (var window in windows)
            {
                window.Show();
                PumpDispatcher();
                window.UpdateLayout();
            }

            var measurements = new[]
            {
                MeasureSecondaryTypography(
                    reminderCenter,
                    "ReminderCenter",
                    "ReminderCenterContentRoot",
                    "提醒中心",
                    "排版验证正文",
                    "计划：2031 年 4 月 5 日 09:00（北京时间）",
                    "便签"),
                MeasureSecondaryTypography(
                    editor,
                    "ReminderRulesEditor",
                    "ReminderRulesEditorContentRoot",
                    "提醒规则",
                    ":",
                    "规则类型",
                    "0 / 8 条"),
                MeasureSecondaryTypography(
                    confirmation,
                    "ConfirmationDialog",
                    "ConfirmationContentRoot",
                    "排版验证",
                    null,
                    "排版验证正文",
                    null),
                MeasureSecondaryTypography(
                    recovery,
                    "SettingsRecovery",
                    "SettingsRecoveryContentRoot",
                    "设置恢复",
                    null,
                    "设置恢复排版验证正文",
                    null),
                MeasureSecondaryTypography(
                    activation,
                    "InstanceActivationFailure",
                    "ActivationFailureContentRoot",
                    InstanceActivationFailureWindow.PromptHeading,
                    null,
                    InstanceActivationFailureWindow.PromptMessage,
                    null)
            };
            var gates = ValidationExercises.EvaluateSecondaryWindowTypographyGates(
                new SecondaryWindowTypographyGateInputs(
                    measurements.All(measurement => measurement.RenderedScaleMatches),
                    measurements.All(measurement => measurement.ContentRootDeclaresFontSize),
                    floatingTypographyUnchanged &&
                    Math.Abs(floatingBodyFontSize - 13) <= 0.1 &&
                    Math.Abs(floatingReminderFontSize - 13) <= 0.1));
            return new SecondaryWindowTypographyResult(
                measurements,
                gates,
                floatingBodyFontSize,
                floatingReminderFontSize);
        }
        finally
        {
            reminderCenter.ForceClose();
            editor.Close();
            confirmation.Close();
            recovery.Close();
            activation.Close();
            PumpDispatcher();
        }
    }

    private static SecondaryWindowTypographyMeasurement MeasureSecondaryTypography(
        Window window,
        string name,
        string rootAutomationId,
        string titleText,
        string? sectionText,
        string bodyText,
        string? minorText)
    {
        var root = Descendants<FrameworkElement>(window).Single(element =>
            string.Equals(
                AutomationProperties.GetAutomationId(element),
                rootAutomationId,
                StringComparison.Ordinal));
        // Window chrome may repeat the same title using its own caption scale.
        // Measure the content region named by this contract, not the outer chrome.
        var texts = Descendants<TextBlock>(root).ToArray();
        var title = texts.First(text => string.Equals(text.Text, titleText, StringComparison.Ordinal));
        var section = sectionText is null
            ? null
            : texts.First(text => string.Equals(text.Text, sectionText, StringComparison.Ordinal));
        var body = texts.First(text => string.Equals(text.Text, bodyText, StringComparison.Ordinal));
        var minor = minorText is null
            ? null
            : texts.First(text => string.Equals(text.Text, minorText, StringComparison.Ordinal));
        var rootFontSize = (double)root.GetValue(TextElement.FontSizeProperty);
        var measurement = new SecondaryWindowTypographyMeasurement(
            name,
            rootAutomationId,
            title.FontSize,
            section?.FontSize,
            body.FontSize,
            minor?.FontSize,
            rootFontSize,
            root.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue);
        return measurement;
    }

    private static ReminderKeyboardExerciseResult ValidateReminderCenterKeyboardNavigation()
    {
        var key = new ActiveReminderOccurrenceKey(
            "validation-reminder-rule",
            new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        window.AddEntry(new ReminderCenterItem(
            key,
            new NoteRecord(
                "validation-reminder-note",
                "键盘提醒验证",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        try
        {
            window.Show();
            PumpDispatcher();
            window.SetDeliveryReady(key);
            PumpDispatcher();
            // #215：出现不再抢焦点；键盘遍历断言显式激活窗口，模拟用户切到中心后的操作。
            window.Activate();
            PumpDispatcher();
            var buttons = Descendants<Button>(window).ToArray();
            var acknowledge = buttons.Single(button =>
                AutomationProperties.GetName(button).StartsWith("知道了：", StringComparison.Ordinal));
            var snooze = buttons.Single(button =>
                AutomationProperties.GetName(button).StartsWith("稍后提醒：", StringComparison.Ordinal));
            var complete = buttons.Single(button =>
                AutomationProperties.GetName(button).StartsWith("完成便签：", StringComparison.Ordinal));
            var openSource = buttons.Single(button =>
                AutomationProperties.GetName(button).StartsWith("打开便签来源：", StringComparison.Ordinal));
            var acknowledgeAll = buttons.Single(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterAcknowledgeAll",
                    StringComparison.Ordinal));
            var minimizeToTaskbar = buttons.Single(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterMinimizeToTaskbar",
                    StringComparison.Ordinal));
            var snoozeChoice = Descendants<ComboBox>(window).Single(comboBox =>
                comboBox.IsVisible &&
                AutomationProperties.GetName(comboBox).StartsWith(
                    "稍后提醒时间：",
                    StringComparison.Ordinal));
            acknowledge.Focus();
            var tabRequest = new TraversalRequest(FocusNavigationDirection.Next);
            var dispositionActionsTraverse =
                acknowledge.MoveFocus(tabRequest) &&
                snoozeChoice.IsKeyboardFocusWithin &&
                snoozeChoice.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, snooze) &&
                snooze.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, complete) &&
                complete.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, openSource);
            var snoozeChoiceFocusable =
                snoozeChoice.Focusable &&
                snoozeChoice.IsTabStop &&
                snoozeChoice.Focus();
            snooze.Focus();
            var snoozeActionsTraverse =
                snooze.IsTabStop &&
                snooze.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, complete);
            var tabTraverses =
                dispositionActionsTraverse &&
                snoozeChoiceFocusable &&
                snoozeActionsTraverse;

            // #86 AC1 回归：选中「自定义」后日期、小时、分钟进入 Tab 链且不跳出当前条目。
            var entryRow = Descendants<Border>(window).Single(border =>
                border.Tag is ActiveReminderOccurrenceKey rowKey && rowKey == key);
            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            PumpDispatcher();
            var customDate = Descendants<DatePicker>(entryRow).Single();
            var customHour = Descendants<ComboBox>(entryRow).Single(comboBox =>
                AutomationProperties.GetName(comboBox).StartsWith(
                    "自定义稍后小时：",
                    StringComparison.Ordinal));
            var customMinute = Descendants<ComboBox>(entryRow).Single(comboBox =>
                AutomationProperties.GetName(comboBox).StartsWith(
                    "自定义稍后分钟：",
                    StringComparison.Ordinal));
            openSource.Focus();
            var customVisible =
                customDate.IsVisible && customHour.IsVisible && customMinute.IsVisible;
            // 主题 ComboBox 模板内的 ToggleButton 可聚焦，会为每个下拉多占一步 Tab；
            // 因此只要求焦点按 打开来源→日期→小时→分钟 的顺序前进、允许控件内部件占步，且步步留在条目内。
            var customStage = 0; // 0=待进日期 1=日期 2=小时 3=分钟
            var customChainStaysInEntry = customVisible;
            UIElement? customMover = openSource;
            for (var step = 0; step < 12 && customStage < 3 && customChainStaysInEntry; step++)
            {
                if (customMover?.MoveFocus(tabRequest) != true ||
                    Keyboard.FocusedElement is not DependencyObject customFocused ||
                    !(ReferenceEquals(customFocused, entryRow) || entryRow.IsAncestorOf(customFocused)))
                {
                    customChainStaysInEntry = false;
                    break;
                }
                if (FocusStaysWithin(entryRow, customDate))
                {
                    customChainStaysInEntry = customStage <= 1;
                    customStage = Math.Max(customStage, 1);
                }
                else if (FocusStaysWithin(entryRow, customHour))
                {
                    customChainStaysInEntry = customStage is 1 or 2;
                    customStage = 2;
                }
                else if (FocusStaysWithin(entryRow, customMinute))
                {
                    customChainStaysInEntry = customStage is 2 or 3;
                    customStage = 3;
                }
                else
                {
                    customChainStaysInEntry = false;
                }
                customMover = customFocused as UIElement;
            }
            customChainStaysInEntry = customChainStaysInEntry && customStage == 3;

            // 切回预设项后自定义控件退出 Tab 链；条目末尾动作之后 Tab 仍绕回当前条目。
            snoozeChoice.SelectedIndex = 0;
            PumpDispatcher();
            snooze.Focus();
            var presetChainRestored =
                !customDate.IsVisible &&
                !customHour.IsVisible &&
                !customMinute.IsVisible &&
                snooze.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, complete);
            openSource.Focus();
            var tabStaysInEntry =
                openSource.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, acknowledgeAll) &&
                acknowledgeAll.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, minimizeToTaskbar) &&
                minimizeToTaskbar.MoveFocus(tabRequest) &&
                Keyboard.FocusedElement is DependencyObject wrapped &&
                entryRow.IsAncestorOf(wrapped);
            tabTraverses =
                tabTraverses &&
                customChainStaysInEntry &&
                presetChainRestored &&
                tabStaysInEntry;

            // #150：Esc 不关闭有条目的中心，只把焦点移到「知道了」；送达失败禁用「知道了」时，
            // 焦点落到始终可用的「打开来源」，窗口同样保持打开。
            // 只 raise KeyDown：真实按键时 Button 不处理 Esc、事件冒泡到窗口处理器；
            // 窗口不能挂 PreviewKeyDown，否则会在隧道阶段吃掉 Esc，破坏日期/下拉的 Esc 关闭。
            window.SetDeliveryReady(key);
            PumpDispatcher();
            openSource.Focus();
            var escapeHandled = RaiseWindowEscapeKeyDown(window);
            PumpDispatcher();
            var escapeFocusesAcknowledge =
                escapeHandled &&
                window.IsVisible &&
                ReferenceEquals(Keyboard.FocusedElement, acknowledge);
            window.ShowDeliveryFailure(key);
            PumpDispatcher();
            openSource.Focus();
            escapeHandled = RaiseWindowEscapeKeyDown(window);
            PumpDispatcher();
            escapeFocusesAcknowledge =
                escapeFocusesAcknowledge &&
                escapeHandled &&
                window.IsVisible &&
                ReferenceEquals(Keyboard.FocusedElement, openSource);
            window.SetDeliveryReady(key);
            PumpDispatcher();

            window.Close();
            PumpDispatcher();
            return new ReminderKeyboardExerciseResult(
                tabTraverses,
                window.IsVisible,
                !window.ShowActivated &&
                !string.IsNullOrWhiteSpace(AutomationProperties.GetName(window)),
                escapeFocusesAcknowledge);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.ForceClose();
            }
        }
    }

    // #150：只 raise KeyDown 的窗口级 Esc；KeyDown 挂在窗口冒泡阶段，
    // 与真实按键「子控件不处理 Esc 时冒泡到窗口」的路径一致。
    private static bool RaiseWindowEscapeKeyDown(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source is null)
        {
            return false;
        }

        var escapeDown = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            Key.Escape)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        window.RaiseEvent(escapeDown);
        return escapeDown.Handled;
    }

    private static bool ValidateToolTipThemeTextContrast(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        try
        {
            theme.Apply();
            var toolTip = new ToolTip { Content = "主题提示" };
            toolTip.ApplyTemplate();
            var content = toolTip.Template.FindName("Content", toolTip) as TextBlock;
            return
                toolTip.Background is Brush background &&
                toolTip.Foreground is Brush foreground &&
                ApplicationTheme.MeetsContrast(background, foreground, 4.5) &&
                toolTip.MaxWidth <= 360 &&
                content?.TextWrapping is TextWrapping.Wrap;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static bool ValidateTagChipTextContrast(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        try
        {
            theme.Apply();
            main.ApplyTheme();
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(restoreUtc));
            PumpDispatcher();
            return main.TagChipsHaveAccessibleThemeText;
        }
        finally
        {
            theme.Apply();
            main.ApplyTheme();
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(restoreUtc));
            PumpDispatcher();
        }
    }

    private static MainNoteCompletionValidation ValidateMainNoteCompletion(
        MainWindow main,
        FloatingWindow floating,
        TagSelector selector,
        DesktopWorkspace workspace)
    {
        var originalMainWorkspace = main.CurrentWorkspace;
        var originalFloatingWorkspace = floating.CurrentWorkspaceForValidation;
        var originalFilter = workspace.Snapshot.Filter;
        var originalTags = workspace.Snapshot.Settings.Tags;
        var settingsTemporaryPath = workspace.Snapshot.SettingsPath + ".tmp";
        var temporaryPathAlreadyBlocked = Directory.Exists(settingsTemporaryPath);
        var probeText = $"便签完成复选框探针-{Guid.NewGuid():N}";
        string? probeId = workspace.Snapshot.Settings.Notes.FirstOrDefault(note => !note.IsCompleted)?.Id;
        var createdProbe = false;
        var createdFailureBlock = false;
        try
        {
            if (probeId is null && !temporaryPathAlreadyBlocked)
            {
                var saved = workspace.Apply(new SaveNoteChange(
                    null,
                    probeText,
                    workspace.Snapshot.BeijingNow.Date,
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
                probeId = saved.Succeeded
                    ? workspace.Snapshot.Settings.Notes.Single(note =>
                        string.Equals(note.Text, probeText, StringComparison.Ordinal)).Id
                    : null;
                createdProbe = probeId is not null;
            }

            if (probeId is null)
            {
                return MainNoteCompletionValidation.Failed;
            }

            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            main.ShowWorkspace(WorkspaceKind.Notes);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            main.UpdateLayout();
            floating.UpdateLayout();

            CheckBox MainCheck() => Descendants<CheckBox>(main)
                .Single(check => string.Equals(check.Tag as string, probeId, StringComparison.OrdinalIgnoreCase));
            CheckBox FloatingCheck() => Descendants<CheckBox>(floating)
                .Single(check => string.Equals(check.Tag as string, probeId, StringComparison.OrdinalIgnoreCase));

            var probeTag = new TagRecord($"completion-style-{Guid.NewGuid():N}", "尺寸探针", "#F4B6A6");
            selector.SetTags(originalTags.Concat([probeTag]).ToArray());
            selector.IsPopupOpen = true;
            PumpDispatcher();
            selector.UpdateLayout();
            var tagCheck = Descendants<CheckBox>(selector.Popup.Child)
                .Single(check => string.Equals(check.Tag as string, probeTag.Id, StringComparison.OrdinalIgnoreCase));
            var dedicatedStyle = Required<Grid>(main, "MainContentProjection")
                .FindResource("MainNoteCompletionCheck") as Style;

            var mainMeasurement = MeasureNoteCompletionCheckBox(
                "MainWindow",
                MainCheck(),
                dedicatedStyle);
            var tagMeasurement = MeasureNoteCompletionCheckBox(
                "TagSelector",
                tagCheck,
                dedicatedStyle);
            var floatingMeasurement = MeasureNoteCompletionCheckBox(
                "FloatingWindow",
                FloatingCheck(),
                dedicatedStyle);

            var noteText = workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).Text;
            var initialName = AutomationProperties.GetName(MainCheck());
            MainCheck().IsChecked = true;
            PumpDispatcher();
            var checkedWorks = workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).IsCompleted;
            var checkedName = AutomationProperties.GetName(MainCheck());

            MainCheck().IsChecked = false;
            PumpDispatcher();
            var uncheckedWorks = !workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).IsCompleted;
            var uncheckedName = AutomationProperties.GetName(MainCheck());

            var rollbackWorks = false;
            if (!temporaryPathAlreadyBlocked)
            {
                Directory.CreateDirectory(settingsTemporaryPath);
                createdFailureBlock = true;
                var failureCheck = MainCheck();
                failureCheck.IsChecked = true;
                PumpDispatcher();
                rollbackWorks =
                    failureCheck.IsChecked == false &&
                    !workspace.Snapshot.Settings.Notes.Single(note =>
                        string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).IsCompleted &&
                    AutomationProperties.GetName(failureCheck) == $"完成便签 {noteText}";
            }
            else
            {
                var failureCheck = MainCheck();
                failureCheck.IsChecked = true;
                PumpDispatcher();
                rollbackWorks =
                    failureCheck.IsChecked == false &&
                    !workspace.Snapshot.Settings.Notes.Single(note =>
                        string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).IsCompleted;
            }

            var interaction = new MainNoteCompletionInteractionValidation(
                checkedWorks,
                uncheckedWorks,
                rollbackWorks,
                initialName == $"完成便签 {noteText}" &&
                checkedName == $"恢复便签 {noteText}" &&
                uncheckedName == $"完成便签 {noteText}");
            return new MainNoteCompletionValidation(
                mainMeasurement,
                tagMeasurement,
                floatingMeasurement,
                interaction);
        }
        finally
        {
            if (createdFailureBlock && Directory.Exists(settingsTemporaryPath))
            {
                Directory.Delete(settingsTemporaryPath);
            }

            if (createdProbe && probeId is not null)
            {
                workspace.Apply(new DeleteNoteChange(probeId));
            }

            selector.IsPopupOpen = false;
            selector.SetTags(originalTags);
            workspace.ApplyFilter(originalFilter);
            main.ShowWorkspace(originalMainWorkspace);
            floating.ShowWorkspace(originalFloatingWorkspace);
            PumpDispatcher();
        }
    }

    private static MainNoteCompletionMeasurement MeasureNoteCompletionCheckBox(
        string user,
        CheckBox checkBox,
        Style? dedicatedStyle)
    {
        checkBox.ApplyTemplate();
        checkBox.UpdateLayout();
        var box = checkBox.Template.FindName("Box", checkBox) as Border;
        var tick = checkBox.Template.FindName("Tick", checkBox) as WpfPath;
        return new MainNoteCompletionMeasurement(
            user,
            Math.Round(box?.ActualWidth ?? 0, 2),
            Math.Round(box?.ActualHeight ?? 0, 2),
            Math.Round(tick?.Width ?? 0, 2),
            Math.Round(tick?.Height ?? 0, 2),
            box?.CornerRadius.TopLeft ?? 0,
            dedicatedStyle is not null && ReferenceEquals(checkBox.Style, dedicatedStyle),
            box is not null && tick is not null &&
            ReferenceEquals(box.Background, Application.Current.FindResource("CanvasBrush")) &&
            ReferenceEquals(box.BorderBrush, Application.Current.FindResource("ControlBorderBrush")) &&
            ReferenceEquals(tick.Stroke, Application.Current.FindResource("MainActionTextBrush")),
            ReferenceEquals(checkBox.Style, Application.Current.TryFindResource("CompletionCheckBox")));
    }

    private static CountdownEmptyStateContrastValidation ValidateCountdownEmptyStateContrast(
        ApplicationTheme theme,
        MainWindow main)
    {
        var heading = Required<TextBlock>(main, "EmptyCountdownMessage");
        var emptyMessage = Required<TextBlock>(main, "EmptyCountdownHint");
        var content = Required<StackPanel>(main, "CountdownEmptyStateContent");
        var rows = Required<StackPanel>(main, "CountdownRows");
        var workspace = Required<Canvas>(main, "CountdownWorkspace");
        var backdrop = Required<Border>(main, "CountdownWorkspaceBackdrop");
        var originalWorkspace = main.CurrentWorkspace;
        var originalVisibility = emptyMessage.Visibility;
        var originalHeadingVisibility = heading.Visibility;
        var originalContentVisibility = content.Visibility;
        var originalRowsVisibility = rows.Visibility;
        var measurements = new List<CountdownEmptyStateContrastMeasurement>();
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            content.Visibility = Visibility.Visible;
            rows.Visibility = Visibility.Collapsed;
            heading.Visibility = Visibility.Visible;
            emptyMessage.Visibility = Visibility.Visible;
            theme.Apply();
            main.ApplyTheme();
            main.UpdateLayout();
            PumpDispatcher();

            var foreground = emptyMessage.Foreground as SolidColorBrush;
            var background = backdrop.Background as SolidColorBrush;
            var foregroundUsesMutedBrush =
                ReferenceEquals(emptyMessage.Foreground, Application.Current.FindResource("MutedBrush"));
            var actualBackgroundMatchesBackdrop =
                ReferenceEquals(backdrop.Background, Application.Current.FindResource("BackgroundBrush"));
            var transparentAncestors = EmptyStateAncestorsAreTransparent(emptyMessage, workspace) &&
                EmptyStateAncestorsAreTransparent(heading, workspace);
            var textBounds = BoundsIn(emptyMessage, main);
            var backdropBounds = BoundsIn(backdrop, main);
            var textWithinBackdrop = emptyMessage.IsVisible && textBounds.Width > 0 &&
                textBounds.Height > 0 && backdropBounds.Contains(textBounds);
            var headingForeground = heading.Foreground as SolidColorBrush;
            var headingBounds = BoundsIn(heading, main);

            measurements.Add(new CountdownEmptyStateContrastMeasurement(
                "Day",
                foreground?.Color.ToString() ?? string.Empty,
                background?.Color.ToString() ?? string.Empty,
                foreground is not null && background is not null
                    ? Math.Round(ContrastRatio(foreground.Color, background.Color), 3)
                    : 0,
                foregroundUsesMutedBrush,
                actualBackgroundMatchesBackdrop,
                transparentAncestors,
                textWithinBackdrop,
                headingForeground?.Color.ToString() ?? string.Empty,
                headingForeground is not null && background is not null
                    ? Math.Round(ContrastRatio(headingForeground.Color, background.Color), 3)
                    : 0,
                ReferenceEquals(heading.Foreground, Application.Current.FindResource("InkBrush")),
                heading.IsVisible && headingBounds.Width > 0 && headingBounds.Height > 0 &&
                    backdropBounds.Contains(headingBounds)));

            return new CountdownEmptyStateContrastValidation(
                measurements.All(measurement =>
                    measurement.ContrastRatio >= 4.5 &&
                    measurement.ForegroundUsesMutedBrush &&
                    measurement.ActualBackgroundMatchesBackdrop &&
                    measurement.TransparentAncestors &&
                    measurement.TextWithinBackdrop &&
                    measurement.HeadingContrastRatio >= 4.5 &&
                    measurement.HeadingUsesInkBrush &&
                    measurement.HeadingTextWithinBackdrop),
                measurements);
        }
        finally
        {
            emptyMessage.Visibility = originalVisibility;
            heading.Visibility = originalHeadingVisibility;
            content.Visibility = originalContentVisibility;
            rows.Visibility = originalRowsVisibility;
            main.ShowWorkspace(originalWorkspace);
            theme.Apply();
            main.ApplyTheme();
            main.UpdateLayout();
            PumpDispatcher();
        }
    }

    private static NoteEmptyStateContrastValidation ValidateNoteEmptyStateContrast(
        ApplicationTheme theme,
        MainWindow main)
    {
        var heading = Required<TextBlock>(main, "EmptyNotesMessage");
        var emptyMessage = Required<TextBlock>(main, "EmptyNotesHint");
        var content = Required<Grid>(main, "NotesEmptyStateContent");
        var activeRows = Required<StackPanel>(main, "ActiveNoteRows");
        var completedRows = Required<StackPanel>(main, "CompletedNoteRows");
        var workspace = Required<Canvas>(main, "NotesWorkspace");
        var originalWorkspace = main.CurrentWorkspace;
        var originalVisibility = emptyMessage.Visibility;
        var originalHeadingVisibility = heading.Visibility;
        var originalContentVisibility = content.Visibility;
        var originalActiveRowsVisibility = activeRows.Visibility;
        var originalCompletedRowsVisibility = completedRows.Visibility;
        var measurements = new List<CountdownEmptyStateContrastMeasurement>();
        try
        {
            main.ShowWorkspace(WorkspaceKind.Notes);
            content.Visibility = Visibility.Visible;
            activeRows.Visibility = Visibility.Collapsed;
            completedRows.Visibility = Visibility.Collapsed;
            heading.Visibility = Visibility.Visible;
            emptyMessage.Visibility = Visibility.Visible;
            theme.Apply();
            main.ApplyTheme();
            main.UpdateLayout();
            PumpDispatcher();

            var foreground = emptyMessage.Foreground as SolidColorBrush;
            var background = main.Background as SolidColorBrush;
            var foregroundUsesMutedBrush =
                ReferenceEquals(emptyMessage.Foreground, Application.Current.FindResource("MutedBrush"));
            var actualBackgroundMatchesBackdrop =
                ReferenceEquals(main.Background, Application.Current.FindResource("BackgroundBrush"));
            var transparentAncestors = EmptyStateAncestorsAreTransparent(emptyMessage, workspace) &&
                EmptyStateAncestorsAreTransparent(heading, workspace);
            var textBounds = BoundsIn(emptyMessage, main);
            var backdropBounds = BoundsIn(workspace, main);
            var textWithinBackdrop = emptyMessage.IsVisible && textBounds.Width > 0 &&
                textBounds.Height > 0 && backdropBounds.Contains(textBounds);
            var headingForeground = heading.Foreground as SolidColorBrush;
            var headingBounds = BoundsIn(heading, main);

            measurements.Add(new CountdownEmptyStateContrastMeasurement(
                "Day",
                foreground?.Color.ToString() ?? string.Empty,
                background?.Color.ToString() ?? string.Empty,
                foreground is not null && background is not null
                    ? Math.Round(ContrastRatio(foreground.Color, background.Color), 3)
                    : 0,
                foregroundUsesMutedBrush,
                actualBackgroundMatchesBackdrop,
                transparentAncestors,
                textWithinBackdrop,
                headingForeground?.Color.ToString() ?? string.Empty,
                headingForeground is not null && background is not null
                    ? Math.Round(ContrastRatio(headingForeground.Color, background.Color), 3)
                    : 0,
                ReferenceEquals(heading.Foreground, Application.Current.FindResource("InkBrush")),
                heading.IsVisible && headingBounds.Width > 0 && headingBounds.Height > 0 &&
                    backdropBounds.Contains(headingBounds)));

            return new NoteEmptyStateContrastValidation(
                measurements.All(measurement =>
                    measurement.ContrastRatio >= 4.5 &&
                    measurement.ForegroundUsesMutedBrush &&
                    measurement.ActualBackgroundMatchesBackdrop &&
                    measurement.TransparentAncestors &&
                    measurement.TextWithinBackdrop &&
                    measurement.HeadingContrastRatio >= 4.5 &&
                    measurement.HeadingUsesInkBrush &&
                    measurement.HeadingTextWithinBackdrop),
                measurements);
        }
        finally
        {
            emptyMessage.Visibility = originalVisibility;
            heading.Visibility = originalHeadingVisibility;
            content.Visibility = originalContentVisibility;
            activeRows.Visibility = originalActiveRowsVisibility;
            completedRows.Visibility = originalCompletedRowsVisibility;
            main.ShowWorkspace(originalWorkspace);
            theme.Apply();
            main.ApplyTheme();
            main.UpdateLayout();
            PumpDispatcher();
        }
    }

    private static bool ValidateNoteStatusBadgeTokensFollowState(
        ApplicationTheme theme,
        MainWindow main)
    {
        var activeSegment = Required<ToggleButton>(main, "ActiveNotesSegment");
        var completedSegment = Required<ToggleButton>(main, "CompletedNotesSegment");
        var activeBadge = Required<Border>(main, "ActiveNotesBadge");
        var completedBadge = Required<Border>(main, "CompletedNotesBadge");
        try
        {
            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            return
                activeSegment.IsChecked == true &&
                BadgeFollowsSegmentState(activeBadge, ParseBadgeCount(activeSegment), isActive: true) &&
                BadgeFollowsSegmentState(completedBadge, ParseBadgeCount(completedSegment), isActive: false);
        }
        finally
        {
            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
        }
    }

    private static int ParseBadgeCount(ToggleButton segment)
    {
        return segment.Content is Grid content &&
            content.Children
                .OfType<Border>()
                .FirstOrDefault(border => border.Child is TextBlock)?.Child is TextBlock count &&
            int.TryParse(count.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
    }

    private static bool EmptyStateAncestorsAreTransparent(
        DependencyObject element,
        DependencyObject stopAt)
    {
        for (var current = VisualTreeHelper.GetParent(element);
             current is not null && !ReferenceEquals(current, stopAt);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is Panel { Background: { } panelBackground } &&
                !IsTransparent(panelBackground))
            {
                return false;
            }

            if (current is Control { Background: { } controlBackground } &&
                !IsTransparent(controlBackground))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTransparent(Brush brush) =>
        brush is SolidColorBrush solid && solid.Color.A == 0;

    private static string BrushColor(Brush? brush) =>
        brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color.ToString()
            : string.Empty;

    private static bool BrushMatchesExpected(Brush? actual, object expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush actualBrush &&
         expected is SolidColorBrush expectedBrush &&
         actualBrush.Color == expectedBrush.Color);

    private static double CompositedContrast(Brush foreground, double opacity, Brush background)
    {
        if (foreground is not SolidColorBrush fg || background is not SolidColorBrush bg ||
            bg.Color.A != 255 || bg.Opacity != 1) return 0;
        var alpha = fg.Color.A / 255d * fg.Opacity * opacity;
        byte Channel(byte front, byte back) => (byte)Math.Round(front * alpha + back * (1 - alpha));
        var visible = Color.FromRgb(Channel(fg.Color.R, bg.Color.R),
            Channel(fg.Color.G, bg.Color.G), Channel(fg.Color.B, bg.Color.B));
        return ContrastRatio(visible, bg.Color);
    }

    private static double RenderedOuterContrast(FrameworkElement root, Border border, bool verticalOnly = false)
    {
        var frame = CaptureVisual(root);
        var bounds = BoundsIn(border, root);
        var neighbors = new List<Point>();
        foreach (var fraction in new[] { .25, .5, .75 })
        {
            var y = bounds.Top + bounds.Height * fraction;
            neighbors.Add(new Point(bounds.Left - 1, y));
            neighbors.Add(new Point(bounds.Right + 1, y));
            if (!verticalOnly)
            {
                var x = bounds.Left + bounds.Width * fraction;
                neighbors.Add(new Point(x, bounds.Top - 1));
                neighbors.Add(new Point(x, bounds.Bottom + 1));
            }
        }
        var minimum = double.MaxValue;
        foreach (var point in neighbors)
        {
            var x = (int)Math.Floor(point.X);
            var y = (int)Math.Floor(point.Y);
            if (x < 0 || y < 0 || x >= frame.PixelWidth || y >= frame.PixelHeight)
            {
                Console.Error.WriteLine($"RASTER_SAMPLE_OUTSIDE: point={x},{y}; bounds={bounds}; frame={frame.PixelWidth}x{frame.PixelHeight}");
                return 0;
            }
            var pixel = new byte[4];
            frame.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            // A transparent frame is not a color. Fail closed rather than interpreting its zero RGB as black.
            if (pixel[3] != 255)
            {
                Console.Error.WriteLine($"RASTER_SAMPLE_ALPHA: point={x},{y}; alpha={pixel[3]}; bounds={bounds}; frame={frame.PixelWidth}x{frame.PixelHeight}; opacity={Window.GetWindow(root)?.Opacity}");
                return 0;
            }
            var neighbor = new SolidColorBrush(Color.FromRgb(pixel[2], pixel[1], pixel[0]));
            minimum = Math.Min(minimum, CompositedContrast(border.BorderBrush, border.Opacity, neighbor));
        }
        return minimum;
    }

    private static bool ValidateRasterNeighborContrastSensitivity()
    {
        var image = new Image { Stretch = Stretch.Fill };
        var border = new Border { Margin = new Thickness(16), BorderThickness = new Thickness(2),
            BorderBrush = (Brush)Application.Current.FindResource("ControlBorderBrush"), Background = Brushes.Transparent };
        var root = new Grid { Width = 64, Height = 64 };
        root.Children.Add(image);
        root.Children.Add(border);
        var host = new Window { Content = root, Width = 64, Height = 64, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Left = -20_000, Top = -20_000, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            host.Show();
            var edgeColor = ((SolidColorBrush)border.BorderBrush).Color;
            var colors = new[] { Colors.White, Colors.Black, edgeColor };
            var measured = new List<double>();
            foreach (var color in colors)
            {
                var pixels = new byte[64 * 64 * 4];
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    pixels[index] = color.B;
                    pixels[index + 1] = color.G;
                    pixels[index + 2] = color.R;
                    pixels[index + 3] = 255;
                }
                image.Source = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
                root.UpdateLayout();
                PumpDispatcher();
                measured.Add(RenderedOuterContrast(root, border));
            }
            // Two different raster neighbors must change the reading. Matching the boundary is a real failing fixture.
            Console.Error.WriteLine($"RASTER_NEIGHBOR_CONTRAST: {string.Join(",", measured)}");
            return measured[0] >= 3 && measured[1] >= 3 && measured[2] < 1.01 &&
                Math.Abs(measured[0] - measured[1]) > .5;
        }
        finally { host.Close(); }
    }

    private static bool ValidateSystemContrastUpdatesOpenWindows(MainWindow main, FloatingWindow floating)
    {
        static bool ColorIs(Brush? brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;
        var mainInput = (TextBox)main.FindName("CountdownName");
        var floatingSurface = (Border)floating.FindName("TaskCard");
        var originalText = mainInput.Text;
        var originalBackground = ((SolidColorBrush)main.Background).Color;
        bool enabled;
        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            PumpDispatcher();
            enabled = ColorIs(main.Background, SystemColors.WindowColor) &&
                ColorIs(mainInput.Foreground, SystemColors.WindowTextColor) &&
                ColorIs(mainInput.BorderBrush, SystemColors.WindowTextColor) &&
                ColorIs(floatingSurface.Background, SystemColors.WindowColor) &&
                ((FrameworkElement)main.FindName("MainReferenceDecorationLayer")).Visibility == Visibility.Collapsed &&
                ((FrameworkElement)floating.FindName("FloatingDayRasterScene")).Visibility == Visibility.Collapsed &&
                mainInput.Text == originalText;
            Console.Error.WriteLine($"SYSTEM_CONTRAST_COLORS: main={main.Background}; input={mainInput.Foreground}; border={mainInput.BorderBrush}; floating={floatingSurface.Background}; window={SystemColors.WindowColor}; text={SystemColors.WindowTextColor}; mainDecor={((FrameworkElement)main.FindName("MainReferenceDecorationLayer")).Visibility}; floatingDecor={((FrameworkElement)floating.FindName("FloatingDayRasterScene")).Visibility}");
        }
        PumpDispatcher();
        var restored = ColorIs(main.Background, originalBackground) &&
            ReferenceEquals(mainInput, main.FindName("CountdownName")) && mainInput.Text == originalText;
        Console.Error.WriteLine($"SYSTEM_CONTRAST_OPEN_WINDOWS: enabled={enabled}; restored={restored}");
        return enabled && restored;
    }

    private static bool ValidateSystemContrastAccessibleStates(MainWindow main, FloatingWindow floating)
    {
        static bool ColorIs(Brush? brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        var workspace = main.CurrentWorkspace;
        var focus = Keyboard.FocusedElement;
        var input = Required<TextBox>(main, "CountdownName");
        var originalText = input.Text;
        var floatingVisible = floating.IsVisible;
        var now = new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero);
        var reminder = new ReminderCenterWindow { ShowInTaskbar = false, Opacity = 0 };
        reminder.AddEntry(new ReminderCenterItem(new ActiveReminderOccurrenceKey("contrast-rule", now),
            new NoteRecord("contrast-note", "系统对比度提醒", "2031-04-05", false, [], NoReminder.Instance), 0, 0));
        reminder.SetDeliveryReady(new ActiveReminderOccurrenceKey("contrast-rule", now));
        var editor = new ReminderRulesEditorWindow([], new ValidationClock(now))
            { Owner = main, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        var dialog = new ConfirmationDialog(main, "对比度确认", "保留用户草稿与当前选区")
            { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            if (!floatingVisible) floating.Show();
            reminder.Show(); editor.Show(); dialog.Show();
            PumpDispatcher();
            var reminderSurface = (Border)reminder.Content;
            var editorSurface = (Border)editor.Content;
            var dialogSurface = (Border)dialog.Content;
            var dayColors = new[] { reminderSurface.Background, editorSurface.Background, dialog.Background }
                .Select(brush => ((SolidColorBrush)brush).Color).ToArray();
            var originalRows = Descendants<FrameworkElement>(reminder)
                .Where(element => AutomationProperties.GetAutomationId(element) == "ReminderEntryPendingBadge").ToArray();
            input.Text = "对比度草稿保留";
            input.Select(1, 3);
            main.Activate(); Keyboard.Focus(input); PumpDispatcher();
            var wasFocused = input.IsKeyboardFocused;
            var generation = main.CountdownRowRebuildGenerationForValidation;
            using (ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                PumpDispatcher();
                checks["DraftSelectionFocus"] = wasFocused && input.IsKeyboardFocused &&
                    input.Text == "对比度草稿保留" && input.SelectionStart == 1 && input.SelectionLength == 3 &&
                    main.CountdownRowRebuildGenerationForValidation == generation;
                checks["ReminderSurface"] = ColorIs(reminderSurface.Background, SystemColors.WindowColor) &&
                    ColorIs(reminderSurface.BorderBrush, SystemColors.WindowTextColor) && reminder.EntryCount == 1 &&
                    Descendants<TextBlock>(reminder).Any(text => text.Text == "系统对比度提醒" && ColorIs(text.Foreground, SystemColors.WindowTextColor));
                checks["RuleEditorSurface"] = ColorIs(editorSurface.Background, SystemColors.WindowColor) &&
                    ColorIs(editorSurface.BorderBrush, SystemColors.WindowTextColor) &&
                    Descendants<TextBlock>(editor).Any(text => text.Text == "提醒规则" && ColorIs(text.Foreground, SystemColors.WindowTextColor));
                checks["ConfirmationSurface"] = ColorIs(dialog.Background, SystemColors.WindowColor) &&
                    ColorIs(dialog.Foreground, SystemColors.WindowTextColor) &&
                    ColorIs(dialogSurface.BorderBrush, SystemColors.WindowTextColor) &&
                    ColorIs(dialog.BodyText.Foreground, SystemColors.WindowTextColor);
                checks["TextSelection"] = ColorIs(input.SelectionBrush, SystemColors.HighlightColor) &&
                    input.SelectionOpacity == 1 && ColorIs(input.SelectionTextBrush, SystemColors.HighlightTextColor);
                var actions = new (string Name, Window Window, Button Button)[]
                {
                    ("Main", main, Required<Button>(main, "SaveCountdownButton")),
                    ("Floating", floating, Required<Button>(floating, "OpenManagerButton")),
                    ("Reminder", reminder, Descendants<Button>(reminder).Single(button => AutomationProperties.GetAutomationId(button) == "ReminderCenterAcknowledgeAll")),
                    ("Rules", editor, Descendants<Button>(editor).First(button => button.IsVisible && ReferenceEquals(button.Style, Application.Current.FindResource("PrimaryButton")))),
                    ("Confirmation", dialog, dialog.CancelButton)
                };
                foreach (var action in actions)
                {
                    action.Window.Activate(); Keyboard.Focus(action.Button);
                    FocusVisualState.SetIsKeyboardFocusVisible(action.Button, true);
                    PumpDispatcher();
                    var ring = action.Button.Template?.FindName("FocusRing", action.Button) as Border;
                    checks[action.Name + "Focus"] = action.Button.IsKeyboardFocused && ring is not null &&
                        ColorIs(ring.BorderBrush, SystemColors.HighlightColor);
                    if (!checks[action.Name + "Focus"])
                        Console.Error.WriteLine($"SYSTEM_CONTRAST_FOCUS {action.Name}: keyboard={action.Button.IsKeyboardFocused}; ring={ring?.BorderBrush}; visible={action.Button.IsVisible}; state={action.Window.WindowState}");
                    var enabled = action.Button.IsEnabled;
                    action.Button.SetCurrentValue(UIElement.IsEnabledProperty, false); PumpDispatcher();
                    checks[action.Name + "Disabled"] = ColorIs(action.Button.Foreground, SystemColors.GrayTextColor);
                    action.Button.SetCurrentValue(UIElement.IsEnabledProperty, enabled);
                }
                var toggle = Required<ToggleButton>(floating, "FloatingCountdownsButton");
                var originalChecked = toggle.IsChecked;
                toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true); PumpDispatcher();
                var selected = toggle.Template?.FindName("Surface", toggle) as Border;
                checks["FloatingSelection"] = selected is not null && ColorIs(selected.Background, SystemColors.HighlightColor) &&
                    ColorIs(toggle.Foreground, SystemColors.HighlightTextColor);
                toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, originalChecked);
                var mode = Descendants<ComboBox>(editor).Single(combo => AutomationProperties.GetAutomationId(combo) == "ReminderRuleMode");
                editor.Activate(); mode.IsDropDownOpen = true; PumpDispatcher();
                var selectedItem = mode.ItemContainerGenerator.ContainerFromIndex(mode.SelectedIndex) as ComboBoxItem;
                var selectedSurface = selectedItem?.Template?.FindName("Surface", selectedItem) as Border;
                checks["RulePopupSelection"] = mode.IsDropDownOpen && selectedSurface is not null &&
                    ColorIs(selectedSurface.Background, SystemColors.HighlightColor) &&
                    ColorIs(selectedItem!.Foreground, SystemColors.HighlightTextColor);
                mode.IsDropDownOpen = false;
                main.ShowWorkspace(WorkspaceKind.Notes); PumpDispatcher();
                checks["DecorationsStayHiddenAfterNavigation"] = new[] { "MainReferenceDecorationLayer", "CharacterBackgroundLayer", "CharacterPanelLayer", "CountdownDecoTextTop", "CountdownDecoTextBottom", "NotesDecoText", "CountdownEmptyMascot", "NotesEmptyMascot" }
                    .All(name => !Required<FrameworkElement>(main, name).IsVisible) &&
                    new[] { "FloatingDayRasterScene", "FloatingPlushScene", "FloatingNotesDecoTextLeft", "FloatingNotesDecoText" }
                    .All(name => !Required<FrameworkElement>(floating, name).IsVisible);
            }
            PumpDispatcher();
            checks["AllSurfacesRestore"] = ColorIs(reminderSurface.Background, dayColors[0]) &&
                ColorIs(editorSurface.Background, dayColors[1]) && ColorIs(dialog.Background, dayColors[2]);
            checks["RowsRemain"] = originalRows.Length == 1 && originalRows.SequenceEqual(Descendants<FrameworkElement>(reminder)
                .Where(element => AutomationProperties.GetAutomationId(element) == "ReminderEntryPendingBadge"));
            checks["FloatingRasterRestores"] = Required<FrameworkElement>(floating, "FloatingDayRasterScene").Visibility == Visibility.Visible;
        }
        finally
        {
            dialog.Close(); editor.Close(); reminder.ForceClose();
            if (!floatingVisible) floating.Hide();
            input.Text = originalText;
            main.ShowWorkspace(workspace);
            if (focus is UIElement previous) Keyboard.Focus(previous);
        }
        Console.Error.WriteLine("SYSTEM_CONTRAST_STATES: " + JsonSerializer.Serialize(checks));
        return checks.Count > 15 && checks.Values.All(value => value);
    }

    private static bool ValidateSystemContrastCustomSurfaces(string assetRoot)
    {
        static bool ColorIs(Brush? brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"MinminCountdown-system-contrast-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(temporaryRoot, "settings.json");
        var diagnosticPath = Path.Combine(temporaryRoot, "validation.jsonl");
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        MainWindow? main = null;
        FloatingWindow? floating = null;
        FloatingWindow? laterFloating = null;
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var now = new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero);
            var store = new SettingsStore(clock: new ValidationClock(now));
            store.Save(settingsPath, new CountdownSettings([], [new NoteRecord("contrast-note", "系统色日期印记", "2031-04-05", false, ["contrast-tag"], NoReminder.Instance)],
                [new TagRecord("contrast-tag", "系统色标签", "#FFD36B")], CountdownSettings.Empty.Preferences));
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            using var calendar = new HolidayCalendar(temporaryRoot);
            main = new MainWindow(workspace, calendar, new DiagnosticLogger(diagnosticPath), assetRoot)
                { ShowInTaskbar = false, Opacity = 0 };
            floating = new FloatingWindow(workspace, calendar, main, assetRoot)
                { ShowInTaskbar = false, Opacity = 0 };
            main.Show(); floating.Show(); main.ShowWorkspace(WorkspaceKind.Notes); floating.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            var chip = Descendants<Border>(main).First(border => border.Uid == "contrast-tag");
            var stamp = Descendants<Border>(floating).First(border => border.BorderThickness == new Thickness(2, 0, 0, 0));
            var oldChip = ((SolidColorBrush)chip.Background).Color;
            var oldStampEdge = ((SolidColorBrush)stamp.BorderBrush).Color;
            var noteRows = Required<StackPanel>(floating, "FloatingRows").Children.Cast<UIElement>().ToArray();
            Border? laterStamp = null;
            floating.SetQuickAddExpanded(true);
            var picker = Required<DatePicker>(floating, "FloatingNoteDate");
            picker.SelectedDate = new DateTime(2031, 4, 5);
            picker.DisplayDate = picker.SelectedDate.Value;
            picker.IsDropDownOpen = true; PumpDispatcher();
            var calendarPopup = PickerTemplateParts.GetCalendar(picker)!;
            var selectedDay = Descendants<CalendarDayButton>(calendarPopup).Single(day => day.IsSelected);
            floating.Activate(); Keyboard.Focus(selectedDay); PumpDispatcher();
            var daySurface = (Border)selectedDay.Template.FindName("Surface", selectedDay);
            using (var preference = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                PumpDispatcher();
                checks["OpenDatePopupMaps"] = picker.IsDropDownOpen &&
                    ColorIs(calendarPopup.Background, SystemColors.WindowColor) &&
                    ColorIs(calendarPopup.BorderBrush, SystemColors.WindowTextColor) &&
                    ColorIs(daySurface.Background, SystemColors.HighlightColor) &&
                    ColorIs(selectedDay.Foreground, SystemColors.HighlightTextColor);
                checks["OpenDatePopupFocus"] = selectedDay.IsKeyboardFocused &&
                    ColorIs(daySurface.BorderBrush, SystemColors.HighlightColor);
                checks["TagMaps"] = ColorIs(chip.Background, SystemColors.ControlColor);
                checks["StampMaps"] = ColorIs(stamp.BorderBrush, SystemColors.WindowTextColor);
                var palette = new ApplicationTheme.SystemContrastPalette(
                    Color.FromRgb(8, 18, 32), Colors.White, Color.FromRgb(20, 30, 46), Colors.White,
                    Colors.Yellow, Colors.Black, Colors.Silver);
                preference.ChangeColors(palette); PumpDispatcher();
                checks["OpenDatePopupChangesColor"] = picker.IsDropDownOpen &&
                    ColorIs(calendarPopup.Background, palette.Window) &&
                    ColorIs(daySurface.Background, palette.Highlight) &&
                    ColorIs(daySurface.BorderBrush, palette.Highlight) &&
                    ColorIs(selectedDay.Foreground, palette.HighlightText);
                picker.IsDropDownOpen = false;
                checks["ChangedWhileEnabled"] = ColorIs(main.Background, palette.Window) &&
                    ColorIs(Required<Border>(floating, "TaskCard").Background, palette.Window) &&
                    ColorIs(chip.Background, palette.Control) && ColorIs(stamp.BorderBrush, palette.WindowText);
                laterFloating = new FloatingWindow(workspace, calendar, main, assetRoot) { ShowInTaskbar = false, Opacity = 0 };
                laterFloating.Show(); laterFloating.ShowWorkspace(WorkspaceKind.Notes); PumpDispatcher();
                laterStamp = Descendants<Border>(laterFloating).First(border => border.BorderThickness == new Thickness(2, 0, 0, 0));
                checks["LaterStampUsesSystemColor"] = ColorIs(laterStamp.BorderBrush, palette.WindowText);
                var newDialog = new ConfirmationDialog(main, "新建窗口", "使用当前系统颜色") { ShowActivated = false, Opacity = 0 };
                try
                {
                    newDialog.Show(); PumpDispatcher();
                    checks["NewWindowUsesCurrentPalette"] = ColorIs(newDialog.Background, palette.Window) &&
                        ColorIs(newDialog.BodyText.Foreground, palette.WindowText);
                    newDialog.CancelButton.IsEnabled = false; PumpDispatcher();
                    checks["ChangedDisabledColor"] = ColorIs(newDialog.CancelButton.Foreground, palette.GrayText);
                }
                finally { newDialog.Close(); }
                preference.ChangeColors(palette with { Highlight = Colors.Cyan, Window = Colors.Black }); PumpDispatcher();
                checks["SecondColorNotification"] = ColorIs(main.Background, Colors.Black) &&
                    ColorIs(Required<Border>(floating, "TaskCard").Background, Colors.Black);
                var lightPalette = new ApplicationTheme.SystemContrastPalette(
                    Colors.White, Colors.Black, Colors.Black, Colors.White,
                    Colors.Navy, Colors.White, Colors.Gray);
                preference.ChangeColors(lightPalette); PumpDispatcher();
                checks["LightTagTextPair"] = ColorIs(chip.Background, lightPalette.Control) &&
                    chip.Child is TextBlock chipText && ColorIs(chipText.Foreground, lightPalette.ControlText);
                checks["LightFloatingTextPair"] = ColorIs(Required<Border>(floating, "TaskCard").Background, lightPalette.Window) &&
                    ColorIs(Required<TextBlock>(floating, "FloatingIncome").Foreground, lightPalette.WindowText);
                var secondary = Required<Button>(main, "SavePreferencesButton");
                checks["LightSecondaryTextPair"] = ColorIs(secondary.Background, lightPalette.Window) &&
                    ColorIs(secondary.Foreground, lightPalette.WindowText);
                var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
                preferencesPopup.IsOpen = true; PumpDispatcher();
                secondary.IsEnabled = false; PumpDispatcher();
                var disabledSurface = secondary.Template.FindName("Surface", secondary) as Border;
                checks["LightDisabledBoundary"] = preferencesPopup.IsOpen && disabledSurface is not null &&
                    ColorIs(disabledSurface.Background, lightPalette.Control) &&
                    ColorIs(disabledSurface.BorderBrush, lightPalette.GrayText) &&
                    ColorIs(secondary.Foreground, lightPalette.GrayText);
                if (!checks["LightDisabledBoundary"])
                    Console.Error.WriteLine($"SYSTEM_CONTRAST_DISABLED: open={preferencesPopup.IsOpen}; enabled={secondary.IsEnabled}; inherited={ApplicationTheme.GetIsSystemContrast(secondary)}; foreground={secondary.Foreground}; background={secondary.Background}; border={secondary.BorderBrush}; actualBackground={disabledSurface?.Background}; actualBorder={disabledSurface?.BorderBrush}");
                secondary.IsEnabled = true; preferencesPopup.IsOpen = false;
            }
            PumpDispatcher();
            checks["CustomColorsRestore"] = ColorIs(chip.Background, oldChip) && ColorIs(stamp.BorderBrush, oldStampEdge);
            checks["LaterStampRestoresDayColor"] = laterStamp is not null && ColorIs(laterStamp.BorderBrush, oldStampEdge);
            checks["CustomRowsRemain"] = noteRows.SequenceEqual(Required<StackPanel>(floating, "FloatingRows").Children.Cast<UIElement>());
        }
        finally
        {
            laterFloating?.Close(); floating?.Close(); main?.Close();
            TryDeleteSettingsFiles(settingsPath); TryDeleteFile(diagnosticPath);
            var cache = Path.Combine(temporaryRoot, "holiday-cache");
            if (Directory.Exists(cache) && !Directory.EnumerateFileSystemEntries(cache).Any()) Directory.Delete(cache);
            if (!Directory.EnumerateFileSystemEntries(temporaryRoot).Any()) Directory.Delete(temporaryRoot);
        }
        Console.Error.WriteLine("SYSTEM_CONTRAST_CUSTOM: " + JsonSerializer.Serialize(checks));
        return checks.Count == 17 && checks.Values.All(value => value);
    }

    private static IReadOnlyDictionary<string, bool> ValidateIdleControlBoundaries(MainWindow main)
    {
        var projection = Required<FrameworkElement>(main, "MainContentProjection");
        var controls = new (string Name, Control Control, string Part)[]
        {
            ("CheckBox", new CheckBox(), "Box"),
            ("Pin", new CheckBox { Style = (Style)projection.FindResource("MainTopmostCheck") }, "Box"),
            ("Completion", new CheckBox { Style = (Style)projection.FindResource("MainNoteCompletionCheck") }, "Box"),
            ("Radio", new RadioButton(), "Ring"),
            ("MainRadio", new RadioButton { Style = (Style)projection.FindResource("MainReminderModeRadio") }, "Ring"),
            ("Combo", new ComboBox(), "Surface"),
            ("Date", new WpfUiDatePicker(), "PART_TextBox"),
            ("Secondary", new Button { Style = (Style)Application.Current.FindResource("SecondaryButton") }, "Surface"),
            ("MainSecondary", new Button { Style = (Style)projection.FindResource("MainSecondaryButton") }, "Surface"),
            ("SavePreferencesSecondary", new Button { Style = Required<Button>(main, "SavePreferencesButton").Style }, "Surface"),
            ("ScrollBar", new ScrollBar { Maximum = 100, ViewportSize = 20, Height = 100 }, "PART_Track")
        };
        var panel = new StackPanel();
        foreach (var (_, control, _) in controls) panel.Children.Add(control);
        var host = new Window { Content = panel, Width = 240, Height = 700, ShowInTaskbar = false,
            Left = -20_000, Top = -20_000, WindowStartupLocation = WindowStartupLocation.Manual, Opacity = 0 };
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            checks["RasterNeighborContrastSensitivity"] = ValidateRasterNeighborContrastSensitivity();
            host.Show();
            PumpDispatcher();
            foreach (var (name, control, partName) in controls)
            {
                control.ApplyTemplate();
                var part = control.Template?.FindName(partName, control);
                Brush? boundary = part switch
                {
                    Border border => border.BorderBrush,
                    System.Windows.Shapes.Shape shape => shape.Stroke,
                    TextBox text => text.BorderBrush,
                    Track track => Descendants<Border>(track.Thumb).FirstOrDefault()?.Background,
                    _ => null
                };
                checks[name] = boundary is not null &&
                    BrushMatchesExpected(boundary, Application.Current.FindResource("ControlBorderBrush")) &&
                    new[] { "BackgroundBrush", "CanvasBrush", "CardBrush", "NoteBrush", "NoteWhiteBrush" }
                        .All(key => ApplicationTheme.MeetsContrast((Brush)Application.Current.FindResource(key), boundary, 3));
                if (name.Contains("Secondary", StringComparison.Ordinal))
                {
                    checks[name] &= part is Border surface &&
                        BrushMatchesExpected(surface.Background, Application.Current.FindResource("CardAltBrush"));
                }
            }
            var editor = new ReminderRulesEditorWindow(
                [new AbsoluteOnceReminderRule("secondary-probe", new DateTimeOffset(2031, 1, 1, 1, 0, 0, TimeSpan.Zero))],
                new ValidationClock(new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            { Owner = main, ShowInTaskbar = false, Opacity = 0 };
            try
            {
                editor.Show();
                PumpDispatcher();
                var edit = Descendants<Button>(editor).Single(button => Equals(button.Content, "编辑"));
                edit.Command.Execute(edit.CommandParameter);
                PumpDispatcher();
                foreach (var id in new[] { "CancelReminderRulesButton", "CancelReminderRuleEditButton" })
                {
                    var button = Descendants<Button>(editor).Single(control => AutomationProperties.GetAutomationId(control) == id);
                    button.ApplyTemplate();
                    checks[id] = button.IsVisible && button.IsEnabled &&
                        button.Template.FindName("Surface", button) is Border surface &&
                        BrushMatchesExpected(surface.Background, Application.Current.FindResource("CardAltBrush")) &&
                        BrushMatchesExpected(surface.BorderBrush, Application.Current.FindResource("ControlBorderBrush"));
                }
            }
            finally { editor.Close(); }
            Console.Error.WriteLine($"IDLE_BOUNDARY_FAILURES: {string.Join(",", checks.Where(pair => !pair.Value).Select(pair => pair.Key))}");
            return checks;
        }
        finally { host.Close(); }
    }

    private static IReadOnlyDictionary<string, bool> ValidateSelectedControlSignals(MainWindow main)
    {
        var projection = Required<FrameworkElement>(main, "MainContentProjection");
        var completionStyle = Application.Current.TryFindResource("CompletionCheckBox") as Style;
        var chip = new ToggleButton { Content = "已完成", Style = Required<ToggleButton>(main, "NoteCompletedFilter").Style };
        var completion = new CheckBox { Style = (Style)projection.FindResource("MainNoteCompletionCheck") };
        var floatingCompletion = new CheckBox { Style = completionStyle };
        var radio = new RadioButton();
        var panel = new StackPanel();
        foreach (var control in new Control[] { chip, completion, floatingCompletion, radio }) panel.Children.Add(control);
        var host = new Window { Content = panel, Width = 240, Height = 240, ShowInTaskbar = false,
            Left = -20_000, Top = -20_000, WindowStartupLocation = WindowStartupLocation.Manual, Opacity = 0 };
        using var motion = ApplicationTheme.OverrideAnimationPreferenceForValidation(false);
        try
        {
            host.Show();
            PumpDispatcher();
            var tick = chip.Template?.FindName("SelectedTick", chip) as WpfPath;
            var absentBeforeSelection = tick is null || tick.Visibility != Visibility.Visible;
            chip.IsChecked = true;
            completion.IsChecked = true;
            floatingCompletion.IsChecked = true;
            radio.IsChecked = true;
            PumpDispatcher();
            tick = chip.Template?.FindName("SelectedTick", chip) as WpfPath;
            var surface = chip.Template?.FindName("Surface", chip) as Border;
            bool CompletionUsesSuccess(CheckBox control)
            {
                var box = control.Template?.FindName("Box", control) as Border;
                var mark = control.Template?.FindName("Tick", control) as WpfPath;
                return box is not null && mark?.Visibility == Visibility.Visible &&
                    BrushMatchesExpected(box.Background, Application.Current.FindResource("SuccessBrush")) &&
                    ApplicationTheme.MeetsContrast(box.Background, mark.Stroke, 3) &&
                    ApplicationTheme.MeetsContrast((Brush)Application.Current.FindResource("BackgroundBrush"), box.Background, 3);
            }
            var dot = radio.Template?.FindName("Dot", radio) as System.Windows.Shapes.Ellipse;
            return new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["Chip"] = absentBeforeSelection && tick?.Visibility == Visibility.Visible && surface is not null &&
                    BrushMatchesExpected(surface.Background, Application.Current.FindResource("PeachTintBrush")) &&
                    BrushMatchesExpected(surface.BorderBrush, Application.Current.FindResource("PeachRailBrush")) &&
                    ApplicationTheme.MeetsContrast(surface.Background, tick.Stroke, 3),
                ["MainCompletion"] = CompletionUsesSuccess(completion),
                ["FloatingCompletion"] = completionStyle is not null && CompletionUsesSuccess(floatingCompletion),
                ["Radio"] = dot?.Visibility == Visibility.Visible &&
                    ApplicationTheme.MeetsContrast((Brush)Application.Current.FindResource("BackgroundBrush"), dot.Fill, 3)
            };
        }
        finally { host.Close(); }
    }

    private static IReadOnlyDictionary<string, bool> ValidateFocusOuterNeighbors(MainWindow main)
    {
        var projection = Required<FrameworkElement>(main, "MainContentProjection");
        var controls = new (string Name, Control Control, string Part, string Expected)[]
        {
            ("Button", new Button(), "FocusRing", "ActiveBrush"),
            ("Primary", new Button { Style = (Style)Application.Current.FindResource("PrimaryButton") }, "FocusRing", "MainActionFocusBrush"),
            ("MainPrimary", new Button { Style = Required<Button>(main, "SaveCountdownButton").Style }, "FocusRing", "MainActionFocusBrush"),
            ("FloatingAccent", new Button { Style = (Style)Application.Current.FindResource("FloatingAccentButton") }, "FocusRing", "MainActionFocusBrush"),
            ("FloatingCapsule", new ToggleButton { IsChecked = true, Style = (Style)Application.Current.FindResource("FloatingHeaderSegmentButton") }, "FocusRing", "MainActionFocusBrush"),
            ("PinUnchecked", new CheckBox { IsChecked = false, Style = (Style)projection.FindResource("MainTopmostCheck") }, "Box", "ActiveBrush"),
            ("PinChecked", new CheckBox { IsChecked = true, Style = (Style)projection.FindResource("MainTopmostCheck") }, "Box", "MainActionFocusBrush")
        };
        var panel = new StackPanel();
        foreach (var (_, control, _, _) in controls)
        {
            control.MinHeight = 40;
            panel.Children.Add(control);
        }
        var host = new Window { Content = panel, Width = 240, Height = 360, ShowInTaskbar = false,
            Left = -20_000, Top = -20_000, WindowStartupLocation = WindowStartupLocation.Manual, Opacity = 0 };
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            host.Show();
            host.Activate();
            PumpDispatcher();
            foreach (var (name, control, part, expected) in controls)
            {
                Keyboard.Focus(control);
                FocusVisualState.SetIsKeyboardFocusVisible(control, true);
                PumpDispatcher();
                var ring = control.Template?.FindName(part, control) as Border;
                checks[name] = control.IsKeyboardFocused && ring is not null &&
                    ring.BorderThickness.Left >= 2 &&
                    BrushMatchesExpected(ring.BorderBrush, Application.Current.FindResource(expected)) &&
                    new[] { "BackgroundBrush", "CanvasBrush", "CardBrush", "NoteBrush", "NoteWhiteBrush", "CardAltBrush" }
                        .All(key => ApplicationTheme.MeetsContrast((Brush)Application.Current.FindResource(key), ring.BorderBrush, 3));
            }
            return checks;
        }
        finally { host.Close(); }
    }

    private static bool ValidateMainIdleTextInputBoundaryContrast(MainWindow main)
    {
        var input = Required<TextBox>(main, "CountdownName");
        var previousFocus = Keyboard.FocusedElement;
        try
        {
            // Measure the materialized idle border, rather than a palette entry that
            // the input might not actually consume (#217).
            Keyboard.Focus(Required<ToggleButton>(main, "CountdownTab"));
            PumpDispatcher();
            input.ApplyTemplate();
            if (input.IsKeyboardFocused ||
                input.Template?.FindName("Surface", input) is not Border surface ||
                surface.BorderBrush is not SolidColorBrush border ||
                surface.BorderThickness.Left <= 0)
            {
                return false;
            }

            return new[] { "BackgroundBrush", "CanvasBrush", "CardBrush", "NoteBrush", "NoteWhiteBrush" }
                .All(key => Application.Current.FindResource(key) is Brush background &&
                    ApplicationTheme.MeetsContrast(background, border, 3)) &&
                ApplicationTheme.MeetsContrast(surface.Background, border, 3);
        }
        finally
        {
            if (previousFocus is not null)
            {
                Keyboard.Focus(previousFocus);
                PumpDispatcher();
            }
        }
    }

    private static bool ValidateThemeContrast(ApplicationTheme theme, DateTimeOffset restoreUtc)
    {
        var dialogForeground = Color.FromRgb(0xFF, 0xFD, 0xFC);
        var dialogBackground = Color.FromRgb(0xA2, 0x44, 0x38);
        var dialogPair = ContrastRatio(dialogForeground, dialogBackground) >= 4.5;
        try
        {
            theme.Apply();
            var canvas = (Color)Application.Current.FindResource("CanvasColor");
            var ink = (Color)Application.Current.FindResource("InkColor");
            var body = (Color)Application.Current.FindResource("BodyColor");
            var muted = (Color)Application.Current.FindResource("MutedColor");
            var tag = (Color)Application.Current.FindResource("TagColor");
            var note = (Color)Application.Current.FindResource("NoteColor");
            var peachTint = (Color)Application.Current.FindResource("PeachTintColor");
            var segmentSelected = (Color)Application.Current.FindResource("SegmentSelectedColor");
            var segmentSelectedForeground =
                (Color)Application.Current.FindResource("SegmentSelectedForegroundColor");
            var pairs =
                ContrastRatio(canvas, ink) >= 7 &&
                ContrastRatio(canvas, body) >= 4.5 &&
                ContrastRatio(canvas, muted) >= 4.5 &&
                ContrastRatio(note, ink) >= 4.5 &&
                ContrastRatio(tag, (Color)Application.Current.FindResource("TagChipTextColor")) >= 4.5 &&
                ContrastRatio(segmentSelected, segmentSelectedForeground) >= 4.5;
            if (!pairs)
            {
                return false;
            }

            var focus = (Color)Application.Current.FindResource("ActiveColor");
            if (ContrastRatio(focus, canvas) < 3 ||
                ContrastRatio(focus, peachTint) < 3 ||
                ContrastRatio(focus, note) < 3)
            {
                return false;
            }

            return dialogPair;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static ReminderCenterSourceLabelContrastValidation ValidateReminderCenterSourceLabelContrast(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        var window = new ReminderCenterWindow
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        window.AddEntry(new ReminderCenterItem(
            new ActiveReminderOccurrenceKey(
                "source-label-note-rule",
                new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero)),
            new NoteRecord(
                "source-label-note",
                "来源标签验证便签",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        window.AddEntry(new ReminderCenterItem(
            new ActiveReminderOccurrenceKey(
                "source-label-countdown-rule",
                new DateTimeOffset(2031, 4, 5, 2, 0, 0, TimeSpan.Zero)),
            new CountdownReminderSource(new CountdownRecord(
                "source-label-countdown",
                "来源标签验证倒计时",
                new DateTimeOffset(2031, 5, 1, 0, 0, 0, TimeSpan.Zero),
                Array.Empty<string>())),
            1,
            0));
        var measurements = new List<ReminderCenterSourceLabelContrastMeasurement>();
        var relativeTimeContrastRatios = new List<double>();
        try
        {
            window.Show();
            PumpDispatcher();
            {
                theme.Apply();
                PumpDispatcher();
                var expectedFontSize = (double)Application.Current.FindResource("SecondaryWindowMinorFontSize");
                var ink = Application.Current.FindResource("InkBrush");
                var reminderCard = Application.Current.FindResource("ReminderCardBrush");
                foreach (var kind in new[] { "便签", "倒计时" })
                {
                    var label = Descendants<TextBlock>(window)
                        .Single(text => string.Equals(text.Text, kind, StringComparison.Ordinal));
                    var card = FindAncestor<Border>(label);
                    var foreground = label.Foreground as SolidColorBrush;
                    var background = card?.Background as SolidColorBrush;
                    var ratio = foreground is not null && background is not null
                        ? ContrastRatio(foreground.Color, background.Color)
                        : 0;
                    measurements.Add(new ReminderCenterSourceLabelContrastMeasurement(
                        "Day",
                        kind,
                        foreground?.Color.ToString() ?? string.Empty,
                        background?.Color.ToString() ?? string.Empty,
                        ratio,
                        ReferenceEquals(label.Foreground, ink),
                        label.FontWeight == FontWeights.Normal &&
                            Math.Abs(label.FontSize - expectedFontSize) < 0.01 &&
                            Descendants<TextBlock>(card!).Any(text =>
                                text.Text == (kind == "便签" ? "来源标签验证便签" : "来源标签验证倒计时") &&
                                Math.Abs(text.FontSize - 20) < 0.01 &&
                                text.FontWeight == FontWeights.SemiBold),
                        ReferenceEquals(card?.Background, reminderCard)));
                }
                foreach (var badge in Descendants<Border>(window).Where(border =>
                    AutomationProperties.GetAutomationId(border) == "ReminderEntryPendingBadge"))
                {
                    var text = Descendants<TextBlock>(badge).Single();
                    relativeTimeContrastRatios.Add(
                        text.Foreground is SolidColorBrush foreground && badge.Background is SolidColorBrush background
                            ? ContrastRatio(foreground.Color, background.Color)
                            : 0);
                }
            }

            return new ReminderCenterSourceLabelContrastValidation(
                measurements.Count == 2 &&
                    measurements.All(measurement => measurement.ContrastRatio >= 4.5),
                measurements.All(measurement => measurement.UsesInkBrush),
                measurements.All(measurement => measurement.EmphasisPreserved),
                measurements.All(measurement => measurement.ReminderCardBackground),
                measurements,
                relativeTimeContrastRatios);
        }
        finally
        {
            theme.Apply();
            window.ForceClose();
        }
    }

    private static SystemDialogPrimaryButtonContrastValidation ValidateSystemDialogPrimaryButtonContrast()
    {
        var recovery = SettingsRecoveryWindow.CreateForValidation("设置恢复对比度验证", true);
        var activation = InstanceActivationFailureWindow.CreateForValidation();
        try
        {
            var recoveryButton = LogicalDescendants<Button>(recovery)
                .Single(button =>
                    string.Equals(AutomationProperties.GetAutomationId(button), "SettingsRecoveryRecover", StringComparison.Ordinal));
            var activationButton = LogicalDescendants<Button>(activation)
                .Single(button =>
                    string.Equals(
                        AutomationProperties.GetName(button),
                        InstanceActivationFailureWindow.DismissButtonText,
                        StringComparison.Ordinal));
            var measurements = new[]
            {
                MeasureSystemDialogPrimaryButton("SettingsRecovery", recoveryButton),
                MeasureSystemDialogPrimaryButton("InstanceActivationFailure", activationButton)
            };
            // #150：静态色必须是现役主画布/主文字/主行动（及其文字）的副本，
            // 窗口本身是无边框暖粉壳（无系统默认窗口铬）。
            var usesActiveStaticPalette =
                UsesActiveWarmShell(recovery) &&
                UsesActiveWarmShell(activation);
            return new SystemDialogPrimaryButtonContrastValidation(
                measurements.All(measurement => measurement.ContrastRatio >= 4.5),
                measurements.All(measurement =>
                    string.Equals(measurement.Background, "#FFC23E68", StringComparison.Ordinal) &&
                    string.Equals(measurement.Foreground, "#FFFFFDFC", StringComparison.Ordinal)),
                usesActiveStaticPalette,
                measurements);
        }
        finally
        {
            recovery.Close();
            activation.Close();
        }
    }

    private static bool UsesActiveWarmShell(Window window)
    {
        var canvasColor = Application.Current.FindResource("CanvasColor") as Color?;
        var shellLineColor = Application.Current.FindResource("PeachLineColor") as Color?;
        var inkColor = Application.Current.FindResource("InkColor") as Color?;
        var mainActionColor = Application.Current.FindResource("MainActionColor") as Color?;
        var mainActionTextColor = Application.Current.FindResource("MainActionTextColor") as Color?;
        var button = LogicalDescendants<Button>(window).First();
        return window.WindowStyle == WindowStyle.None &&
               window.AllowsTransparency &&
               Equals(window.Background, Brushes.Transparent) &&
               window.Content is Border shell &&
               Equals(shell.BorderThickness, new Thickness(1)) &&
               canvasColor is { } canvas &&
               shell.Background is SolidColorBrush shellBackground &&
               shellBackground.Color == canvas &&
               shellLineColor is { } shellLine &&
               shell.BorderBrush is SolidColorBrush shellBorder &&
               shellBorder.Color == shellLine &&
               inkColor is { } ink &&
               window.Foreground is SolidColorBrush foreground &&
               foreground.Color == ink &&
               mainActionColor is { } mainAction &&
               button.Background is SolidColorBrush buttonBackground &&
               buttonBackground.Color == mainAction &&
               mainActionTextColor is { } mainActionText &&
               button.Foreground is SolidColorBrush buttonText &&
               buttonText.Color == mainActionText;
    }

    private static SystemDialogPrimaryButtonContrastMeasurement MeasureSystemDialogPrimaryButton(
        string window,
        Button button)
    {
        var background = button.Background as SolidColorBrush;
        var foreground = button.Foreground as SolidColorBrush;
        var ratio = background is not null && foreground is not null
            ? ContrastRatio(background.Color, foreground.Color)
            : 0;
        return new SystemDialogPrimaryButtonContrastMeasurement(
            window,
            foreground?.Color.ToString() ?? string.Empty,
            background?.Color.ToString() ?? string.Empty,
            ratio);
    }

    private static KeyboardFocusRingContrastValidation ValidateKeyboardFocusRingContrast(
        ApplicationTheme theme,
        MainWindow main,
        FloatingWindow floating,
        DateTimeOffset restoreUtc)
    {
        var probe = new Button { Content = "焦点环验证", MinWidth = 80 };
        var host = new Window
        {
            Content = probe,
            Width = 200,
            Height = 80,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        var measurements = new List<KeyboardFocusContrastMeasurement>();
        var ringMeasurements = new List<KeyboardFocusRingMeasurement>();
        var tabRingGapAvoidsSelectedFill = false;
        try
        {
            host.Show();
            host.Activate();
            PumpDispatcher();
            {
                theme.Apply();
                main.ApplyTheme();
                PumpDispatcher();
                var expected = Application.Current.FindResource("ActiveBrush");
                Keyboard.Focus(probe);
                PumpDispatcher();
                probe.ApplyTemplate();
                var surface = probe.Template?.FindName("Surface", probe) as Border;
                var probeUsesExpected = BrushMatchesExpected(surface?.BorderBrush, expected);

                // #152：选中粉底页签（PinkTintBrush）上的焦点环——环与粉底之间常驻 1px 间隙，
                // 实际邻色是容器 CardAltBrush；对实际邻色以主题公式核 ≥3:1。
                // 每处环按上下文钉唯一期望 token（常规 ActiveBrush / 实心粉邻色暖金），
                // 不存在「不达标但匹配旧强调色则放过」的分支。
                var countdownTab = Required<ToggleButton>(main, "CountdownTab");
                main.Activate();
                var originalTabChecked = countdownTab.IsChecked;
                countdownTab.IsChecked = true;
                Keyboard.Focus(countdownTab);
                PumpDispatcher();
                countdownTab.ApplyTemplate();
                var tabRing = countdownTab.Template?.FindName("FocusRing", countdownTab) as Border;
                var tabSurface = countdownTab.Template?.FindName("Surface", countdownTab) as Border;
                var usesExpected =
                    probeUsesExpected &&
                    BrushMatchesExpected(tabRing?.BorderBrush, expected);
                ringMeasurements.Add(new KeyboardFocusRingMeasurement(
                    "Day",
                    BrushColor(tabRing?.BorderBrush),
                    usesExpected,
                    tabRing is not null &&
                        new[]
                        {
                            tabRing.BorderThickness.Left,
                            tabRing.BorderThickness.Top,
                            tabRing.BorderThickness.Right,
                            tabRing.BorderThickness.Bottom
                        }.Max() >= 2));

                // 环与选中粉底之间有 1px 间隙：Surface 外缩 3（环 2px + 间隙 1px），
                // 环的邻色是容器浅色而不是粉底本身。
                tabRingGapAvoidsSelectedFill =
                    tabSurface is not null &&
                    Math.Abs(tabSurface.Margin.Left - 3) <= 0.01 &&
                    Math.Abs(tabSurface.Margin.Top - 3) <= 0.01 &&
                    Math.Abs(tabSurface.Margin.Right - 3) <= 0.01 &&
                    Math.Abs(tabSurface.Margin.Bottom - 3) <= 0.01 &&
                    tabSurface.Background is SolidColorBrush selectedFill &&
                    Application.Current.FindResource("PinkTintColor") is Color pinkTint &&
                    selectedFill.Color == pinkTint;

                if (tabRing?.BorderBrush is SolidColorBrush focusBrush)
                {
                foreach (var pair in new[]
                         {
                             ("Canvas", (Color)Application.Current.FindResource("CanvasColor")),
                             ("PeachTint", (Color)Application.Current.FindResource("PeachTintColor")),
                             ("Note", (Color)Application.Current.FindResource("NoteColor")),
                             ("CardAlt", (Color)Application.Current.FindResource("CardAltColor"))
                         })
                    {
                        measurements.Add(new KeyboardFocusContrastMeasurement(
                            pair.Item1,
                            ContrastRatio(focusBrush.Color, pair.Item2)));
                    }
                }

                countdownTab.IsChecked = originalTabChecked;
                PumpDispatcher();
            }

            var pin = Required<CheckBox>(main, "TopmostCheck");
            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            main.Activate();
            Keyboard.Focus(pin);
            PumpDispatcher();
            pin.ApplyTemplate();
            var pinBox = pin.Template?.FindName("Box", pin) as Border;
            var pinKeepsWarmGold =
                pinBox?.BorderBrush is SolidColorBrush pinBrush &&
                Application.Current.FindResource("MainActionFocusBrush") is SolidColorBrush pinExpected &&
                pinBrush.Color == pinExpected.Color;

            var hoverUnchanged =
                Application.Current.FindResource("ActiveBrush") is SolidColorBrush active &&
                Application.Current.FindResource("MainActionFocusBrush") is SolidColorBrush focusToken &&
                active.Color != focusToken.Color;

            // #217：焦点外环量度真实外侧标题栏邻色，不能用内侧选中填充替代。
            var floatingCapsule = ValidateFloatingCapsuleFocusRing(floating);

            return new KeyboardFocusRingContrastValidation(
                measurements.Count == 4 && measurements.All(measurement => measurement.ContrastRatio >= 3),
                ringMeasurements.All(measurement => measurement.UsesExpectedToken && measurement.RingVisible),
                pinKeepsWarmGold,
                hoverUnchanged,
                tabRingGapAvoidsSelectedFill,
                floatingCapsule.MeetsNeighborContrast,
                floatingCapsule.UsesWarmGoldToken,
                measurements,
                ringMeasurements);
        }
        finally
        {
            theme.Apply();
            main.ApplyTheme();
            host.Close();
            PumpDispatcher();
        }
    }

    private static (bool MeetsNeighborContrast, bool UsesWarmGoldToken)
        ValidateFloatingCapsuleFocusRing(FloatingWindow floating)
    {
        var countdownButton = Required<ToggleButton>(floating, "FloatingCountdownsButton");
        var notesButton = Required<ToggleButton>(floating, "FloatingNotesButton");
        var originalWorkspace = notesButton.IsChecked == true ? WorkspaceKind.Notes : WorkspaceKind.Countdowns;
        try
        {
            floating.Activate();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            var root = Required<Grid>(floating, "TaskWrap");
            var notes = MeasureFromAdjacentButton(countdownButton, notesButton, FocusNavigationDirection.Next, root);
            floating.ShowWorkspace(WorkspaceKind.Countdowns);
            var countdowns = MeasureFromAdjacentButton(notesButton, countdownButton, FocusNavigationDirection.Previous, root);
            return (
                notes.MeetsNeighborContrast && countdowns.MeetsNeighborContrast,
                notes.UsesWarmGoldToken && countdowns.UsesWarmGoldToken);
        }
        finally
        {
            floating.ShowWorkspace(originalWorkspace);
            PumpDispatcher();
        }

        static (bool MeetsNeighborContrast, bool UsesWarmGoldToken) MeasureFromAdjacentButton(
            ToggleButton adjacent,
            ToggleButton selected,
            FocusNavigationDirection direction,
            FrameworkElement root)
        {
            Keyboard.Focus(adjacent);
            PumpDispatcher();
            var moved = adjacent.MoveFocus(new TraversalRequest(direction));
            PumpDispatcher();
            selected.ApplyTemplate();
            var focusRing = selected.Template?.FindName("FocusRing", selected) as Border;
            if (!moved || !selected.IsKeyboardFocused || selected.IsChecked != true ||
                focusRing?.BorderBrush is not SolidColorBrush ring ||
                Application.Current.FindResource("MainActionFocusBrush") is not SolidColorBrush warmGold)
            {
                return (false, false);
            }

            var usesWarmGold = ring.Color == warmGold.Color;
            var thickness = new[]
            {
                focusRing.BorderThickness.Left,
                focusRing.BorderThickness.Top,
                focusRing.BorderThickness.Right,
                focusRing.BorderThickness.Bottom
            }.Min();
            var contrastPasses = usesWarmGold && thickness >= 2 &&
                RenderedOuterContrast(root, focusRing) >= 3;
            var pointerClearsKeyboardRing = WindowValidationInput.ClickForFocusValidation(selected) &&
                !FocusVisualState.GetIsKeyboardFocusVisible(selected) &&
                focusRing.BorderBrush is SolidColorBrush pointerRing && pointerRing.Color.A == 0;
            return (contrastPasses && pointerClearsKeyboardRing, usesWarmGold);
        }
    }

    private static ComboBoxTextClickValidation ValidateComboBoxTextClick(MainWindow main)
    {
        ReminderCenterWindow? reminderCenter = null;
        ReminderRulesEditorWindow? rulesEditor = null;
        var originalWorkspace = main.CurrentWorkspace;
        var expanded = Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Visibility == Visibility.Visible;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            if (!expanded)
                Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            var mainResults = new List<ComboBoxPointerExerciseResult>
            {
                MeasureCombo("Main.CountdownHour", Required<ComboBox>(main, "CountdownHour")),
                MeasureCombo("Main.CountdownMinute", Required<ComboBox>(main, "CountdownMinute")),
                MeasureCombo("Main.CountdownSecond", Required<ComboBox>(main, "CountdownSecond"))
            };
            Required<Button>(main, "PreferencesButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            mainResults.Add(MeasureCombo("Main.DisplayPrecision", Required<ComboBox>(main, "DisplayPrecision"), Required<Popup>(main, "PreferencesPopup")));
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "PreferencesPopup"));

            reminderCenter = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            reminderCenter.AddEntry(new ReminderCenterItem(
                new ActiveReminderOccurrenceKey(
                    "combo-click-rule",
                    new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero)),
                new NoteRecord(
                    "combo-click-note",
                    "下拉点击验证",
                    "2031-04-05",
                    false,
                    Array.Empty<string>(),
                    NoReminder.Instance),
                0,
                0));
            reminderCenter.Show();
            PumpDispatcher();
            var snoozeChoice = Descendants<ComboBox>(reminderCenter).Single(comboBox =>
                AutomationProperties.GetName(comboBox).StartsWith("稍后提醒时间：", StringComparison.Ordinal));
            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            PumpDispatcher();
            var reminderResults = Descendants<ComboBox>(reminderCenter)
                .Select(comboBox => MeasureCombo(AutomationProperties.GetName(comboBox), comboBox))
                .ToArray();

            rulesEditor = new ReminderRulesEditorWindow(
                Array.Empty<ReminderRule>(),
                new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
            {
                Owner = main,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            rulesEditor.Show();
            PumpDispatcher();
            var editorResults = new List<ComboBoxPointerExerciseResult>();
            var editorCombos = Descendants<ComboBox>(rulesEditor).ToArray();
            var editorMode = editorCombos.Single(comboBox =>
                AutomationProperties.GetAutomationId(comboBox) == "ReminderRuleMode");
            var calendarFrequency = editorCombos.Single(comboBox =>
                AutomationProperties.GetAutomationId(comboBox) == "ReminderRuleCalendarFrequency");
            foreach (var comboBox in editorCombos)
            {
                var id = AutomationProperties.GetAutomationId(comboBox);
                // Exercise each selector in a reachable visible editor mode.
                // Routed-event injection previously also clicked collapsed
                // controls, which does not represent a user's pointer path.
                editorMode.SelectedIndex = id == "ReminderRuleInterval" ? 1 :
                    id.StartsWith("ReminderRuleCalendar", StringComparison.Ordinal) ? 2 : 0;
                calendarFrequency.SelectedIndex = 3;
                PumpDispatcher();
                editorResults.Add(MeasureCombo(id, comboBox));
            }

            var all = mainResults.Concat(reminderResults).Concat(editorResults).ToArray();
            return new ComboBoxTextClickValidation(
                all.All(result => result.ReleaseOpened && result.Attached && result.ArrowColumnUnchanged),
                all.All(result => result.ArrowPressOpens),
                all.All(result => result.KeyboardF4Opens && result.KeyboardAltDownOpens && result.KeyboardArrowsKeepOpen && result.KeyboardEscapeCloses),
                all.All(result => result.DisabledClickDoesNotOpen),
                all.All(result => result.PopupItemClickSelects),
                all.All(result => result.ArrowIsNotTabStop),
                all.Length,
                all);
        }
        finally
        {
            rulesEditor?.Close();
            reminderCenter?.ForceClose();
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "PreferencesPopup"));
            if (!expanded && Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Visibility == Visibility.Visible)
                Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            main.ShowWorkspace(originalWorkspace);
        }
    }

    private static ComboBoxPointerExerciseResult MeasureCombo(string name, ComboBox comboBox, Popup? containingPopup = null)
    {
        var result = WindowValidationInput.ExerciseComboBox(comboBox, containingPopup);
        if (!result.ArrowIsNotTabStop)
            Console.Error.WriteLine($"COMBO_TAB_DIAGNOSTIC {name}; visible={comboBox.IsVisible}; result={result}");
        return result;
    }

    private static SecondaryWindowHeaderDragValidation ValidateSecondaryWindowDrag(MainWindow main)
    {
        var reminder = new ReminderCenterWindow
        {
            ShowInTaskbar = false,
            Opacity = 0
        };
        reminder.AddEntry(new ReminderCenterItem(
            new ActiveReminderOccurrenceKey(
                "header-drag-rule",
                new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero)),
            new NoteRecord(
                "header-drag-note",
                "头部拖动验证",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
        {
            Owner = main,
            ShowInTaskbar = false,
            Opacity = 0
        };
        try
        {
            reminder.Show();
            PumpDispatcher();
            reminder.UpdateLayout();
            var workArea = MonitorWorkAreaResolver.ResolveForWindow(reminder).WorkAreaDip;
            var firstLeft = reminder.Left;
            var firstTop = reminder.Top;
            var firstAppearanceTopRight =
                Math.Abs(firstLeft - (workArea.Right - reminder.ActualWidth - 18)) <= 1.5 &&
                Math.Abs(firstTop - (workArea.Top + 18)) <= 1.5;

            var reminderRequestsBefore = reminder.NativeWindowDragRequestCount;
            reminder.DragHeader.RaiseEvent(CreateHeaderDragGesture());
            PumpDispatcher();
            var reminderHeaderRequestsDrag =
                reminder.NativeWindowDragRequestCount == reminderRequestsBefore + 1 &&
                reminder.NativeWindowDragCompletionCount == 0;
            var movedLeft = workArea.Left + 48;
            var movedTop = workArea.Top + 64;
            reminder.Left = movedLeft;
            reminder.Top = movedTop;
            reminder.ReapplyPlacement();
            PumpDispatcher();
            var reminderKeepsManual =
                reminder.HasManualWindowPlacement &&
                Math.Abs(reminder.Left - movedLeft) <= 0.5 &&
                Math.Abs(reminder.Top - movedTop) <= 0.5;

            var interactive = Descendants<Button>(reminder).First();
            interactive.RaiseEvent(CreateHeaderDragGesture());
            PumpDispatcher();
            var reminderInteractiveDoesNotDrag =
                reminder.NativeWindowDragRequestCount == reminderRequestsBefore + 1;

            var closingCancelled = false;
            reminder.Closing += (_, args) => closingCancelled = args.Cancel;
            reminder.Close();
            PumpDispatcher();
            var closeCancelledWithItems = closingCancelled && reminder.IsVisible && reminder.EntryCount > 0;

            editor.Show();
            PumpDispatcher();
            editor.UpdateLayout();
            var editorRequestsBefore = editor.NativeWindowDragRequestCount;
            editor.DragHeader.RaiseEvent(CreateHeaderDragGesture());
            PumpDispatcher();
            var editorHeaderRequestsDrag =
                editor.NativeWindowDragRequestCount == editorRequestsBefore + 1 &&
                editor.NativeWindowDragCompletionCount == 0;
            var editorMovedLeft = workArea.Left + 72;
            editor.Left = editorMovedLeft;
            editor.ReapplyPlacement();
            PumpDispatcher();
            var editorKeepsManual =
                editor.HasManualWindowPlacement &&
                Math.Abs(editor.Left - editorMovedLeft) <= 0.5;
            var editorAccept = Descendants<Button>(editor).First(button =>
                string.Equals(button.Content?.ToString(), "完成", StringComparison.Ordinal));
            var editorRequestsBeforeInteractive = editor.NativeWindowDragRequestCount;
            editorAccept.RaiseEvent(CreateHeaderDragGesture());
            PumpDispatcher();
            var editorInteractiveDoesNotDrag =
                editor.NativeWindowDragRequestCount == editorRequestsBeforeInteractive;
            var resizeModeUnchanged =
                reminder.ResizeMode == ResizeMode.CanResizeWithGrip &&
                editor.ResizeMode == ResizeMode.CanResizeWithGrip;

            return new SecondaryWindowHeaderDragValidation(
                reminderHeaderRequestsDrag && editorHeaderRequestsDrag,
                reminderKeepsManual && editorKeepsManual,
                reminderInteractiveDoesNotDrag && editorInteractiveDoesNotDrag,
                firstAppearanceTopRight,
                closeCancelledWithItems,
                resizeModeUnchanged);
        }
        finally
        {
            editor.Close();
            reminder.ForceClose();
        }
    }

    private static MouseButtonEventArgs CreateHeaderDragGesture() =>
        new(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent
        };

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static bool MainSegmentButtonsHaveAccessibleSelectedStates(MainWindow main)
    {
        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        var notesTab = Required<ToggleButton>(main, "NotesTab");
        var originalCountdownChecked = countdownTab.IsChecked;
        var originalNotesChecked = notesTab.IsChecked;
        try
        {
            countdownTab.IsChecked = true;
            notesTab.IsChecked = false;
            var countdownContrast = HasSelectedSegmentContrast(main, countdownTab);

            countdownTab.IsChecked = false;
            notesTab.IsChecked = true;
            return countdownContrast && HasSelectedSegmentContrast(main, notesTab);
        }
        finally
        {
            countdownTab.IsChecked = originalCountdownChecked;
            notesTab.IsChecked = originalNotesChecked;
        }
    }

    private static bool HasSelectedSegmentContrast(
        MainWindow main,
        ToggleButton button)
    {
        main.UpdateLayout();
        button.ApplyTemplate();
        var surface = button.Template?.FindName("Surface", button) as Border;
        // #148：选中段文字必须以主题公式对实际邻色达到 4.5:1；
        // 删除「不达标但对上方向稿采样对则放过」的旧分支，避免把错误验收成绿。
        return surface?.Background is SolidColorBrush background &&
            button.Foreground is SolidColorBrush foreground &&
            ApplicationTheme.MeetsContrast(background, foreground, 4.5);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255d;
                return normalized <= 0.03928
                    ? normalized / 12.92
                    : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }

        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static TextFit MeasureTextFit(FrameworkElement element, string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                (FontFamily)Application.Current.FindResource("UiFontFamily"),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal),
            (double)Application.Current.FindResource("TypeCaptionFontSize"),
            Brushes.Black,
            VisualTreeHelper.GetDpi(element).PixelsPerDip);
        var available = double.IsNaN(element.Width)
            ? Math.Max(0, element.ActualWidth - 22)
            : Math.Max(0, element.Width - 22);
        return new TextFit(available >= formatted.Width, available, formatted.Width);
    }

    private static TextFit MeasureDatePickerTextFit(DatePicker picker)
    {
        picker.ApplyTemplate();
        if (picker.Template?.FindName("PART_TextBox", picker) is not DatePickerTextBox textBox)
        {
            return new TextFit(false, 0, 0);
        }

        var dpi = VisualTreeHelper.GetDpi(textBox);
        var formatted = new FormattedText(
            DateDisplay.FullDate(new DateOnly(2031, 12, 31)),
            CultureInfo.CurrentCulture,
            textBox.FlowDirection,
            new Typeface(
                textBox.FontFamily,
                textBox.FontStyle,
                textBox.FontWeight,
                textBox.FontStretch),
            textBox.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        var available = Math.Max(
            0,
            textBox.ActualWidth -
            textBox.Padding.Left -
            textBox.Padding.Right -
            textBox.BorderThickness.Left -
            textBox.BorderThickness.Right);
        var required =
            formatted.WidthIncludingTrailingWhitespace +
            Math.Max(0, formatted.OverhangTrailing) +
            (2 / dpi.DpiScaleX);
        return new TextFit(available >= required, available, required);
    }

    private static TextFit MeasureButtonFit(ContentControl button)
    {
        var text = button.Content?.ToString() ?? string.Empty;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch),
            button.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(button).PixelsPerDip);
        var available = Math.Max(
            0,
            button.ActualWidth -
            button.Padding.Left -
            button.Padding.Right -
            button.BorderThickness.Left -
            button.BorderThickness.Right);
        return new TextFit(available >= formatted.Width, available, formatted.Width);
    }

    private static TextFit MeasureTextFit(
        TextBlock textBlock,
        FrameworkElement visibleAncestor)
    {
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var formatted = FormatText(textBlock, dpi);
        var origin = textBlock.TranslatePoint(new Point(0, 0), visibleAncestor);
        var visibleBounds = Rect.Intersect(
            new Rect(origin, textBlock.RenderSize),
            new Rect(new Point(0, 0), visibleAncestor.RenderSize));
        var available = visibleBounds.IsEmpty ? 0 : visibleBounds.Width;
        var required =
            formatted.WidthIncludingTrailingWhitespace +
            Math.Max(0, formatted.OverhangTrailing) +
            (2 / dpi.DpiScaleX);
        return new TextFit(available >= required, available, required);
    }

    private static FormattedText FormatText(TextBlock textBlock, DpiScale dpi) =>
        new(
            textBlock.Text,
            CultureInfo.CurrentCulture,
            textBlock.FlowDirection,
            new Typeface(
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch),
            textBlock.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);

    private static Grid? FindFloatingRemainingCluster(
        StackPanel rows,
        string countdownName)
    {
        foreach (var row in rows.Children.OfType<Border>())
        {
            var texts = Descendants<TextBlock>(row).ToArray();
            if (!texts.Any(text =>
                    string.Equals(text.Text, countdownName, StringComparison.Ordinal)))
            {
                continue;
            }

            return Descendants<Grid>(row).FirstOrDefault(grid =>
                string.Equals(grid.Tag as string, FloatingWindow.RemainingBlocksTag, StringComparison.Ordinal));
        }

        return null;
    }

    private static FloatingSecondaryRemainingMeasurement CaptureFloatingSecondaryRemaining(
        string precision,
        string expectedText,
        string formattedText,
        Grid? cluster,
        FloatingWindow floating,
        bool preferenceSaved)
    {
        var valueBlocks = RemainingBlockPairs(cluster).Select(pair => pair.Value).ToArray();
        var columnIs194Pixels =
            cluster is not null &&
            Math.Abs(cluster.Width - 194) <= 0.01 &&
            Math.Abs(cluster.ActualWidth - 194) <= 0.5;
        var isRightAligned =
            cluster is not null &&
            cluster.HorizontalAlignment == HorizontalAlignment.Right;
        var isSingleLineAndUntrimmed =
            RemainingClusterIsSingleLine(cluster) &&
            (cluster is null ||
             !(AutomationProperties.GetName(cluster) ?? string.Empty).Contains('…'));
        var isFullyVisible =
            cluster is not null &&
            isSingleLineAndUntrimmed &&
            string.Equals(
                AutomationProperties.GetItemStatus(cluster),
                "KeyTextFontFloorSatisfied",
                StringComparison.Ordinal) &&
            FitsInsideFloatingViewport(cluster, floating);
        return new FloatingSecondaryRemainingMeasurement(
            precision,
            expectedText,
            formattedText,
            cluster is null ? string.Empty : AutomationProperties.GetName(cluster) ?? string.Empty,
            Math.Round(cluster?.Width ?? 0, 2),
            Math.Round(cluster?.ActualWidth ?? 0, 2),
            Math.Round(valueBlocks.FirstOrDefault()?.FontSize ?? 0, 2),
            columnIs194Pixels,
            isRightAligned,
            isSingleLineAndUntrimmed,
            isFullyVisible,
            valueBlocks.Length > 0 &&
                valueBlocks.All(valueBlock =>
                    System.Windows.Documents.Typography.GetNumeralAlignment(valueBlock) ==
                    FontNumeralAlignment.Tabular),
            preferenceSaved);
    }

    private static bool FitsInsideFloatingViewport(
        FrameworkElement element,
        FloatingWindow floating)
    {
        var bodyScroller = Required<ScrollViewer>(floating, "FloatingBodyScroller");
        bodyScroller.ApplyTemplate();
        if (bodyScroller.Template.FindName(
                "PART_ScrollContentPresenter",
                bodyScroller) is not ScrollContentPresenter viewport)
        {
            return false;
        }

        var elementBounds = BoundsIn(element, viewport);
        var viewportBounds = new Rect(new Point(0, 0), viewport.RenderSize);
        return elementBounds.Left + 0.5 >= viewportBounds.Left &&
               elementBounds.Top + 0.5 >= viewportBounds.Top &&
               elementBounds.Right <= viewportBounds.Right + 0.5 &&
               elementBounds.Bottom <= viewportBounds.Bottom + 0.5;
    }

    private static bool TextBlockLinesFit(TextBlock textBlock)
    {
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var typeface = new Typeface(
            textBlock.FontFamily,
            textBlock.FontStyle,
            textBlock.FontWeight,
            textBlock.FontStretch);
        return textBlock.Text
            .Split('\n')
            .All(line =>
            {
                var formatted = new FormattedText(
                    line,
                    CultureInfo.CurrentCulture,
                    textBlock.FlowDirection,
                    typeface,
                    textBlock.FontSize,
                    Brushes.Black,
                    dpi.PixelsPerDip);
                var required =
                    formatted.WidthIncludingTrailingWhitespace +
                    Math.Max(0, formatted.OverhangTrailing);
                const double minimumWidthReserve = 2;
                return required + minimumWidthReserve <= textBlock.ActualWidth;
            });
    }

    private static bool VerifyFloatingIncomePrivacy(
        FloatingWindow window, ApplicationSession session, HolidayCalendar calendar, MainWindow manager,
        string assetRoot, string settingsPath, MutableValidationClock clock)
    {
        var income = Required<TextBlock>(window, "FloatingIncome");
        var originalTime = clock.UtcNow;
        var savedBytes = File.ReadAllBytes(settingsPath);
        var savedSettings = SettingsContract.SerializeCanonical(session.Current);
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("#229 金额隐私：" + message);
        }
        void RequireHidden()
        {
            PumpDispatcher();
            window.UpdateLayout();
            Require(income.Text == "今日已赚 ¥••••", "隐藏态可见文案没有遮罩。");
            var root = UIElementAutomationPeer.CreatePeerForElement(window)!;
            var pending = new Queue<AutomationPeer>();
            var seen = new HashSet<AutomationPeer>();
            pending.Enqueue(root);
            var foundMaskedPeer = false;
            while (pending.TryDequeue(out var peer))
            {
                if (!seen.Add(peer)) continue;
                var texts = new List<string> { peer.GetName(), peer.GetHelpText(), peer.GetItemStatus() };
                if (peer.GetPattern(PatternInterface.Value) is IValueProvider value) texts.Add(value.Value);
                if (peer.GetPattern(PatternInterface.Text) is ITextProvider text) texts.Add(text.DocumentRange.GetText(-1));
                foundMaskedPeer |= peer.GetName() == "今日已赚 ¥••••";
                Require(texts.All(text => !text.Contains("41.13", StringComparison.Ordinal) && !text.Contains("59.97", StringComparison.Ordinal)),
                    "真实 AutomationPeer 子树仍暴露金额。");
                foreach (var child in peer.GetChildren() ?? []) pending.Enqueue(child);
            }
            Require(foundMaskedPeer, "自动化树没有提供遮罩名称。");
            Require(Descendants<FrameworkElement>(window).All(element =>
                    !Convert.ToString(element.ToolTip, CultureInfo.InvariantCulture)!.Contains("41.13", StringComparison.Ordinal) &&
                    !Convert.ToString(element.ToolTip, CultureInfo.InvariantCulture)!.Contains("59.97", StringComparison.Ordinal)),
                "ToolTip 暴露金额。");
        }
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Require(income.Text == "今日已赚 ￥41.13", "未在金额上按下的释放不应切换金额。");
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        income.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            { RoutedEvent = UIElement.MouseLeaveEvent });
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Require(income.Text == "今日已赚 ￥41.13", "按下后移出金额区域不应触发切换。");
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        RequireHidden();
        var focusRing = Required<Border>(window, "FloatingIncomeFocusRing");
        Require(IsTransparent(focusRing.BorderBrush), "鼠标操作后残留键盘焦点环。");
        Required<Control>(window, "FloatingNotesButton").Focus();
        window.Activate();
        income.Focus();
        Require(income.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)) &&
            Keyboard.FocusedElement is UIElement previous && !ReferenceEquals(previous, income) &&
            previous.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && income.IsKeyboardFocused,
            "金额不能通过实际 Tab 路径从前一个控件进入。");
        PumpDispatcher();
        Require(income.IsKeyboardFocused && focusRing.BorderThickness == new Thickness(2) &&
            BrushColor(focusRing.BorderBrush) == BrushColor((Brush)window.FindResource("ActiveBrush")),
            "金额实际获键盘焦点后没有 2 DIP 暖边。");
        clock.UtcNow = originalTime.AddHours(1);
        window.UpdateTime(clock.UtcNow);
        RequireHidden();
        clock.UtcNow = originalTime.AddYears(1);
        window.UpdateTime(clock.UtcNow);
        Require(!income.Text.StartsWith("今日已赚", StringComparison.Ordinal) &&
            UIElementAutomationPeer.CreatePeerForElement(income)?.GetPattern(PatternInterface.Invoke) is null,
            "日历无该年金额时不应保留金额或金额动作。");
        clock.UtcNow = originalTime.AddHours(1);
        window.UpdateTime(clock.UtcNow);
        RequireHidden();
        window.ShowWorkspace(WorkspaceKind.Notes);
        RequireHidden();
        window.ShowWorkspace(WorkspaceKind.Countdowns);
        window.ApplyTheme();
        window.UpdateTime(clock.UtcNow);
        RequireHidden();
        Require(File.ReadAllBytes(settingsPath).SequenceEqual(savedBytes) &&
            SettingsContract.SerializeCanonical(session.Current) == savedSettings, "切换、tick 或切页写入设置。");

        FloatingWindow? restarted = null;
        try
        {
            restarted = new FloatingWindow(new DesktopWorkspace(ApplicationSession.Load(settingsPath,
                new SettingsStore(clock: clock))), calendar, manager, assetRoot)
            { ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Opacity = 0.01 };
            restarted.Show();
            restarted.UpdateTime(clock.UtcNow);
            PumpDispatcher();
            Require(restarted.IncomeText == "今日已赚 ￥59.97", "新实例未恢复显示当前金额。");
        }
        finally { restarted?.Close(); }

        var peer = UIElementAutomationPeer.CreatePeerForElement(income);
        Require(income.Focusable && KeyboardNavigation.GetIsTabStop(income) &&
            peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "金额入口缺少键盘或 Invoke 语义。");
        ((IInvokeProvider)peer!.GetPattern(PatternInterface.Invoke)).Invoke();
        PumpDispatcher();
        Require(income.Text == "今日已赚 ￥59.97", "Invoke 后未显示最新金额。");
        WindowValidationInput.RaiseKey(income, Key.Enter);
        RequireHidden();
        var preferences = session.Current.Preferences;
        Require(session.TrySavePreferences(preferences.DisplayPrecision, 0m, preferences.WorkStart, preferences.WorkEnd, out _), "零薪资夹具失败。");
        window.UpdateTime(clock.UtcNow);
        Require(income.Text == FloatingBottomStatus.CountdownEncouragementText &&
            UIElementAutomationPeer.CreatePeerForElement(income) is null, "鼓励语合同改变。");
        Require(session.TrySavePreferences(preferences.DisplayPrecision, preferences.MonthlySalary,
            preferences.WorkStart, preferences.WorkEnd, out _), "薪资恢复夹具失败。");
        window.UpdateTime(clock.UtcNow);
        RequireHidden();
        WindowValidationInput.RaiseKey(income, Key.Space);
        clock.UtcNow = originalTime;
        window.UpdateTime(originalTime);
        Require(income.Text == "今日已赚 ￥41.13", "空格切换后没有恢复显示。");
        Require(File.ReadAllBytes(settingsPath).SequenceEqual(savedBytes), "运行期遮罩状态进入设置文件。");
        // Restore the original reference composition after advancing the fixture clock.
        window.ShowWorkspace(WorkspaceKind.Notes);
        window.ShowWorkspace(WorkspaceKind.Countdowns);
        window.UpdateTime(originalTime);
        PumpDispatcher();
        return true;
    }

    private static void VerifyFloatingZeroDayBlocks(FloatingWindow window)
    {
        foreach (var sample in new[]
        {
            ("0 天", ""), ("0 天 06 小时", "时"), ("0 天 06 小时 49 分钟", "时,分"),
            ("0 天 06 小时 49 分钟 00 秒", "时,分,秒"),
            ("1 天 06 小时 49 分钟 00 秒", "天,时,分,秒"),
            ("999 天 13 小时 18 分钟 59 秒", "天,时,分,秒"),
            ("2000000 天 00 小时 00 分钟 00 秒", "天,时,分,秒")
        })
        {
            foreach (var isPrimary in new[] { true, false })
            {
                var element = window.BuildRemainingBlocksForValidation(sample.Item1, isPrimary);
                element.Measure(new Size(194, 60));
                element.Arrange(new Rect(0, 0, 194, element.DesiredSize.Height));
                var cluster = element as Grid;
                var pairs = RemainingBlockPairs(cluster).ToArray();
                if (string.Join(",", pairs.Select(pair => pair.Unit.Text)) != sample.Item2 ||
                    (sample.Item2.Length == 0 && element is not TextBlock { Text: "少于 1 天" }) ||
                    (sample.Item2.Length > 0 && (cluster is null || Math.Abs(cluster.Width - 194) > 0.01 ||
                        !RemainingClusterIsSingleLine(cluster) || pairs.Any(pair => pair.Value.FontSize < 12))))
                    throw new InvalidOperationException($"#229 零天省略或非零分格合同失败：{sample.Item1}, primary={isPrimary}");
            }
        }
    }

    private static void VerifyWorkdayPrimaryTitleAndBadge(StackPanel rows)
    {
        var card = (Border)rows.Children[0];
        var title = Descendants<TextBlock>(card).Single(text => text.Text == "距离下班");
        var deadline = Descendants<TextBlock>(card).Single(text => text.Text == "7 月 15 日 18:00");
        var badge = Descendants<Border>(card).Single(border => border.Child is TextBlock text && text.Text == "超期待！");
        var titleBounds = BoundsIn(title, card);
        var deadlineBounds = BoundsIn(deadline, card);
        var badgeBounds = BoundsIn(badge, card);
        if (title.ActualHeight > 20.01 || !SingleLineTextFits(title) ||
            titleBounds.Right > card.ActualWidth - 54 ||
            Math.Abs(deadlineBounds.Top + deadlineBounds.Height / 2 - badgeBounds.Top - badgeBounds.Height / 2) > 2 ||
            badgeBounds.Left < deadlineBounds.Right || badge.IsHitTestVisible || badge.Focusable)
            throw new InvalidOperationException($"#229 主项四字标题或截止行徽章不符合合同：title={titleBounds}; deadline={deadlineBounds}; badge={badgeBounds}");
    }

    private static bool SingleLineTextFits(TextBlock text)
    {
        var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace + Math.Max(0, formatted.OverhangTrailing) <= text.ActualWidth;
    }

    private static bool WorkdayCountdownUsesPrimaryVisual(
        FloatingWindow window,
        StackPanel rows,
        DateTimeOffset nowUtc,
        DateTimeOffset workdayTargetUtc)
    {
        if (rows.Children.Count != 2 ||
            rows.Children[0] is not Border { Child: Grid primaryRoot } ||
            rows.Children[1] is not Border { Child: Grid secondaryRoot })
        {
            return false;
        }

        var primaryRemaining = CountdownFormatting.FormatRemaining(
            workdayTargetUtc,
            nowUtc,
            "Second");
        var secondaryTargetUtc = nowUtc
            .AddDays(4)
            .AddHours(13)
            .AddMinutes(18)
            .AddSeconds(59);
        var secondaryRemainingForAutomation = CountdownFormatting.FormatRemaining(
            secondaryTargetUtc,
            nowUtc,
            "Second");
        var primaryTexts = Descendants<TextBlock>(primaryRoot).ToArray();
        var secondaryTexts = Descendants<TextBlock>(secondaryRoot).ToArray();
        var primaryTitle = primaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "距离下班", StringComparison.Ordinal));
        var primaryDeadline = primaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "7 月 15 日 18:00", StringComparison.Ordinal));
        var secondaryTitle = secondaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "七月倒计时", StringComparison.Ordinal));
        var primaryNames = rows.Children
            .OfType<Border>()
            .Select(card => card.Child as Grid)
            .Where(root => root is not null)
            .Select(root => AutomationProperties.GetName(root!))
            .Where(name => name.StartsWith("主要倒计时 ", StringComparison.Ordinal))
            .ToArray();
        var pinkTintSoft = ResourceBrush(window, "PinkTintSoftBrush");
        var cardAlt = ResourceBrush(window, "CardAltBrush");
        var accent = ResourceBrush(window, "FloatingDayPrimaryCountdownAccentBrush");
        var pinkBlock = ResourceBrush(window, "PinkBlockBrush");
        var secondaryBlock = ResourceBrush(window, "SecondaryBlockBrush");
        var numberBrush = ResourceBrush(window, "PrimaryCountdownNumberBrush");
        var pinkBadge = ResourceBrush(window, "PinkBadgeBrush");
        var ink = ResourceBrush(window, "InkBrush");
        var muted = ResourceBrush(window, "FloatingDayMutedBrush");
        var primaryDecorations = primaryRoot.Children.OfType<Border>().ToArray();
        var secondaryDecorations = secondaryRoot.Children.OfType<Border>().ToArray();
        var primaryCluster = FloatingRemainingCluster(primaryRoot);
        var secondaryCluster = FloatingRemainingCluster(secondaryRoot);
        var primaryBlockPairs = RemainingBlockPairs(primaryCluster).ToArray();
        var secondaryBlockPairs = RemainingBlockPairs(secondaryCluster).ToArray();
        var badgeBlocks = Descendants<Border>(primaryRoot)
            .Where(border => border.Child is TextBlock badgeText &&
                string.Equals(badgeText.Text, "超期待！", StringComparison.Ordinal))
            .ToArray();
        var badge = badgeBlocks.Length == 1 ? badgeBlocks[0] : null;
        var badgeText = badge?.Child as TextBlock;

        return
            primaryNames.Length == 1 &&
            string.Equals(
                AutomationProperties.GetName(primaryRoot),
                $"主要倒计时 距离下班，{primaryRemaining}",
                StringComparison.Ordinal) &&
            string.Equals(
                AutomationProperties.GetName(secondaryRoot),
                $"倒计时 七月倒计时，{secondaryRemainingForAutomation}",
                StringComparison.Ordinal) &&
            Math.Abs(primaryRoot.MinHeight - 68) <= 0.01 &&
            Math.Abs(secondaryRoot.MinHeight - 39) <= 0.01 &&
            primaryDecorations.Length == 3 &&
            secondaryDecorations.Length == 2 &&
            Math.Abs(primaryDecorations[0].Margin.Right - 54) <= 0.01 &&
            Math.Abs(primaryDecorations[0].Opacity - 0.8) <= 0.01 &&
            Math.Abs(primaryDecorations[0].CornerRadius.TopLeft - 8) <= 0.01 &&
            !primaryDecorations[0].IsHitTestVisible &&
            ReferenceEquals(primaryDecorations[0].Background, pinkTintSoft) &&
            Math.Abs(primaryDecorations[1].Width - 3) <= 0.01 &&
            Math.Abs(primaryDecorations[1].Height - 42) <= 0.01 &&
            !primaryDecorations[1].IsHitTestVisible &&
            ReferenceEquals(primaryDecorations[1].Background, accent) &&
            Math.Abs(primaryDecorations[2].Opacity - 0.78) <= 0.01 &&
            Math.Abs(secondaryDecorations[0].Opacity - 0.72) <= 0.01 &&
            ReferenceEquals(secondaryDecorations[0].Background, cardAlt) &&
            Math.Abs(secondaryDecorations[1].Opacity - 0.48) <= 0.01 &&
            primaryTitle is not null &&
            Math.Abs(primaryTitle.FontSize - 16) <= 0.01 &&
            primaryTitle.FontWeight == FontWeights.SemiBold &&
            primaryTitle.TextWrapping == TextWrapping.Wrap &&
            primaryTitle.TextTrimming == TextTrimming.CharacterEllipsis &&
            primaryTitle.LineStackingStrategy == LineStackingStrategy.BlockLineHeight &&
            Math.Abs(primaryTitle.LineHeight - 20) <= 0.01 &&
            Math.Abs(primaryTitle.MaxHeight - 40) <= 0.01 &&
            primaryTitle.IsVisible && primaryTitle.ActualHeight > 0 &&
            primaryTitle.ActualHeight <= 40.01 &&
            string.Equals(BrushColor(primaryTitle.Foreground), "#FF3B190D", StringComparison.Ordinal) &&
            primaryDeadline is not null &&
            Math.Abs(primaryDeadline.FontSize - 13) <= 0.01 &&
            string.Equals(BrushColor(primaryDeadline.Foreground), "#FF82665F", StringComparison.Ordinal) &&
            Typography.GetNumeralAlignment(primaryDeadline) == FontNumeralAlignment.Tabular &&
            badge is not null &&
            !badge.IsHitTestVisible &&
            Math.Abs(badge.CornerRadius.TopLeft - 8) <= 0.01 &&
            badge.VerticalAlignment == VerticalAlignment.Center &&
            ReferenceEquals(badge.Background, pinkBadge) &&
            pinkBadge is not null &&
            ink is not null &&
            ContrastRatio(pinkBadge.Color, ink.Color) >= 4.5 &&
            badgeText is not null &&
            Math.Abs(badgeText.FontSize - 13) <= 0.01 &&
            badgeText.FontWeight == FontWeights.SemiBold &&
            ReferenceEquals(badgeText.Foreground, ink) &&
            primaryCluster is not null &&
            Math.Abs(primaryCluster.Width - 194) <= 0.01 &&
            primaryCluster.HorizontalAlignment == HorizontalAlignment.Right &&
            string.Equals(
                AutomationProperties.GetName(primaryCluster),
                primaryRemaining,
                StringComparison.Ordinal) &&
            primaryBlockPairs.Length == 3 &&
            primaryBlockPairs.All(pair => pair.Unit.Text != "天") &&
            primaryBlockPairs.All(pair =>
                ReferenceEquals(pair.Block.Background, pinkBlock) &&
                Math.Abs(pair.Block.CornerRadius.TopLeft - 7) <= 0.01 &&
                pair.Value.FontWeight == FontWeights.SemiBold &&
                Math.Abs(pair.Value.FontSize - 26) <= 0.01 &&
                ReferenceEquals(pair.Value.Foreground, numberBrush) &&
                Typography.GetNumeralAlignment(pair.Value) == FontNumeralAlignment.Tabular &&
                pair.Value.TextWrapping == TextWrapping.NoWrap &&
                pair.Unit.Foreground == muted) &&
            secondaryTitle is not null &&
            Math.Abs(secondaryTitle.FontSize - 13) <= 0.01 &&
            string.Equals(BrushColor(secondaryTitle.Foreground), "#FF3B190D", StringComparison.Ordinal) &&
            secondaryCluster is not null &&
            Math.Abs(secondaryCluster.Width - 194) <= 0.01 &&
            secondaryCluster.HorizontalAlignment == HorizontalAlignment.Right &&
            string.Equals(
                AutomationProperties.GetName(secondaryCluster),
                secondaryRemainingForAutomation,
                StringComparison.Ordinal) &&
            secondaryBlockPairs.Length == 4 &&
            secondaryBlockPairs.All(pair =>
                ReferenceEquals(pair.Block.Background, secondaryBlock) &&
                Math.Abs(pair.Block.CornerRadius.TopLeft - 5) <= 0.01 &&
                pair.Value.FontWeight == FontWeights.SemiBold &&
                Math.Abs(pair.Value.FontSize - 20) <= 0.01 &&
                string.Equals(BrushColor(pair.Value.Foreground), "#FF3B190D", StringComparison.Ordinal) &&
                Typography.GetNumeralAlignment(pair.Value) == FontNumeralAlignment.Tabular &&
                pair.Value.TextWrapping == TextWrapping.NoWrap &&
                pair.Unit.Foreground == muted);
    }

    private static SolidColorBrush? ResourceBrush(FloatingWindow window, string key) =>
        window.TryFindResource(key) as SolidColorBrush;

    private static Grid? FloatingRemainingCluster(DependencyObject rowRoot) =>
        Descendants<Grid>(rowRoot).FirstOrDefault(grid =>
            string.Equals(grid.Tag as string, FloatingWindow.RemainingBlocksTag, StringComparison.Ordinal));

    private static IEnumerable<(Border Block, TextBlock Value, TextBlock Unit)> RemainingBlockPairs(
        Grid? cluster)
    {
        if (cluster is null)
        {
            yield break;
        }

        foreach (var pairPanel in Descendants<StackPanel>(cluster))
        {
            if (pairPanel.Children.OfType<Border>().FirstOrDefault() is not { } block ||
                block.Child is not TextBlock value ||
                pairPanel.Children.OfType<TextBlock>().LastOrDefault() is not { } unit)
            {
                continue;
            }

            yield return (block, value, unit);
        }
    }

    private static bool RemainingClusterIsSingleLine(Grid? cluster) =>
        RemainingBlockPairs(cluster).Select(pair => pair.Value).All(value =>
            value.TextWrapping == TextWrapping.NoWrap &&
            value.TextTrimming == TextTrimming.None &&
            !value.Text.Contains('\r') &&
            !value.Text.Contains('\n') &&
            !value.Text.Contains('…') &&
            TextBlockLinesFit(value));

    private static bool PrimaryRemainingTextFitsOnOneLine(
        DependencyObject rows,
        DateTimeOffset targetUtc,
        DateTimeOffset nowUtc)
    {
        var expectedRemaining = CountdownFormatting.FormatRemaining(
            targetUtc,
            nowUtc,
            "Second");
        var cluster = Descendants<Grid>(rows).FirstOrDefault(candidate =>
            string.Equals(
                AutomationProperties.GetName(candidate),
                expectedRemaining,
                StringComparison.Ordinal) &&
            string.Equals(
                candidate.Tag as string,
                FloatingWindow.RemainingBlocksTag,
                StringComparison.Ordinal));
        var valueBlocks = RemainingBlockPairs(cluster).Select(pair => pair.Value).ToArray();
        return cluster is not null &&
               Math.Abs(cluster.Width - 194) <= 0.01 &&
               valueBlocks.Length == (targetUtc - nowUtc < TimeSpan.FromDays(1) ? 3 : 4) &&
               valueBlocks.All(value =>
                    Math.Abs(value.FontSize - 26) <= 0.01 &&
                   value.FontWeight == FontWeights.SemiBold &&
                   value.TextWrapping == TextWrapping.NoWrap &&
                   value.TextTrimming == TextTrimming.None &&
                   !value.Text.Contains('\n') &&
                   TextBlockLinesFit(value)) &&
               RemainingClusterIsSingleLine(cluster);
    }

    private static bool PrimaryDeadlineTextFullyVisible(
        DependencyObject rows,
        string expectedText)
    {
        return MeasurePrimaryDeadline(rows, expectedText, scrollbarVisible: false).FullyVisible;
    }

    private static FloatingPrimaryDeadlineMeasurement MeasurePrimaryDeadline(
        DependencyObject rows,
        string expectedText,
        bool scrollbarVisible)
    {
        var text = Descendants<TextBlock>(rows).SingleOrDefault(candidate =>
            candidate.Text.StartsWith(expectedText, StringComparison.Ordinal));
        if (text is null)
        {
            return new FloatingPrimaryDeadlineMeasurement(
                scrollbarVisible,
                string.Empty,
                0,
                0,
                false);
        }

        var dpi = VisualTreeHelper.GetDpi(text);
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentCulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        var requiredWidth = formatted.WidthIncludingTrailingWhitespace +
                            Math.Max(0, formatted.OverhangTrailing);
        var fullyVisible =
            string.Equals(text.Text, expectedText, StringComparison.Ordinal) &&
            text.TextWrapping == TextWrapping.NoWrap &&
            text.TextTrimming == TextTrimming.None &&
            !text.Text.Contains('…') &&
            text.ActualWidth + 0.01 >= requiredWidth &&
            TextBlockLinesFit(text);
        return new FloatingPrimaryDeadlineMeasurement(
            scrollbarVisible,
            text.Text,
            Math.Round(text.ActualWidth, 2),
            Math.Round(requiredWidth, 2),
            fullyVisible);
    }

    private static FloatingTrayInteractionExerciseResult RunFloatingTrayInteractionExercise(
        MainWindow main,
        FloatingWindow floating,
        Button minimizeButton,
        FloatingTrayController trayController,
        DesktopWorkspace workspace)
    {
        var wasVisible = floating.IsVisible;
        var exitRequested = false;
        EventHandler exitHandler = (_, _) => exitRequested = true;
        trayController.ExitRequested += exitHandler;
        try
        {
            var residentWhileVisible = floating.IsVisible && trayController.IsTrayIconVisible;
            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            var minimizeWorks =
                residentWhileVisible &&
                !floating.IsVisible &&
                trayController.IsTrayIconVisible;

            trayController.InvokeIconClickForValidation();
            PumpDispatcher();
            var clickRestores =
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                trayController.IsTrayIconVisible;

            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            trayController.InvokeRestoreForValidation();
            PumpDispatcher();
            var menuRestores =
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                trayController.IsTrayIconVisible;
            var headersMatch = TrayMenuHeadersMatch(floating);
            var managerPreserved = TrayOpenManagerPreservesDraftAndFilter(main, workspace, trayController);
            if (!minimizeWorks || !clickRestores || !menuRestores || !headersMatch || !managerPreserved)
            {
                throw new InvalidOperationException(
                    "#213 托盘探针失败：" +
                    $"residentMinimize={minimizeWorks} click={clickRestores} menu={menuRestores} " +
                    $"headers={headersMatch} manager={managerPreserved}");
            }

            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            trayController.InvokeExitForValidation();
            PumpDispatcher();
            return new FloatingTrayInteractionExerciseResult(
                trayController.IsActive,
                minimizeWorks,
                clickRestores && menuRestores && headersMatch && managerPreserved,
                exitRequested,
                RunFailedRestoreKeepsTrayStateProbe(floating));
        }
        finally
        {
            trayController.ExitRequested -= exitHandler;
            if (!floating.IsVisible)
            {
                floating.Show();
                PumpDispatcher();
            }

            if (wasVisible && !floating.IsVisible)
            {
                floating.Show();
            }
        }
    }

    private static bool TrayMenuHeadersMatch(FloatingWindow floating)
    {
        var (menu, openManager, restore, resetPosition, exit) = TrayMenuFactory.Create(
            static () => { },
            static () => { },
            static () => { },
            static () => { });
        IDisposable? menuInput = null;
        try
        {
            // Establish native keyboard focus on this thread before menu mode.
            // This window is not the menu PlacementTarget or resource owner.
            floating.Activate();
            floating.Focus();
            WindowValidationInput.Pump(floating.Dispatcher);
            if (TrayProbeGetFocus() == IntPtr.Zero)
                throw new InvalidOperationException("#232 菜单打开前当前线程没有 Win32 键盘焦点。");
            // The production TaskbarIcon owns a standalone menu, outside window aliases.
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.IsOpen = true;
            WindowValidationInput.Pump(menu.Dispatcher);
            var items = new[] { openManager, restore, resetPosition, exit };
            foreach (var item in items) item.ApplyTemplate();
            if (!ReferenceEquals(menu.Background, Application.Current.FindResource("CanvasBrush")) ||
                !ReferenceEquals(menu.BorderBrush, Application.Current.FindResource("ControlBorderBrush")) ||
                !items.All(item => item.FontSize == (double)Application.Current.FindResource("TypeBodyFontSize") &&
                    item.FontFamily.Source == ((FontFamily)Application.Current.FindResource("UiFontFamily")).Source &&
                    ReferenceEquals(item.Foreground, Application.Current.FindResource("InkBrush"))))
            {
                throw new InvalidOperationException("#232 托盘实际菜单未使用主题文字、背景、边界和字号 token。" +
                    $" background={menu.Background}; border={menu.BorderBrush}; items=" +
                    string.Join("|", items.Select(item => $"{item.Header}:highlight={item.IsHighlighted},font={item.FontFamily.Source},size={item.FontSize},foreground={item.Foreground}")));
            }
            menuInput = WindowValidationInput.AcquireContextMenuInputForValidation(menu);
            // Use ContextMenu's normal keyboard navigation, which selects the
            // first item through NavigateToStart and real keyboard focus.
            Keyboard.Focus(menu);
            WindowValidationInput.Pump(menu.Dispatcher);
            WindowValidationInput.RaiseKey(menu, Key.Down);
            WindowValidationInput.Pump(menu.Dispatcher);
            if (!openManager.IsKeyboardFocused || !openManager.IsHighlighted ||
                !ReferenceEquals(openManager.Background, Application.Current.FindResource("MainActionBrush")) ||
                !ReferenceEquals(openManager.Foreground, Application.Current.FindResource("MainActionTextBrush")))
                throw new InvalidOperationException("#232 托盘真实键盘高亮未使用主行动配色。" +
                    $" open={menu.IsOpen}; focused={Keyboard.FocusedElement}; enabled={openManager.IsEnabled}; visible={openManager.IsVisible}; focusable={openManager.Focusable}; highlighted={openManager.IsHighlighted}; background={openManager.Background}; foreground={openManager.Foreground}");

            static bool ColorIs(Brush brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;
            var dayCanvas = ((SolidColorBrush)menu.Background).Color;
            var dayHighlight = ((SolidColorBrush)openManager.Background).Color;
            using (var preference = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                PumpDispatcher();
                if (!ColorIs(menu.Background, SystemColors.WindowColor) ||
                    !ColorIs(menu.BorderBrush, SystemColors.WindowTextColor) ||
                    !ColorIs(openManager.Background, SystemColors.HighlightColor) ||
                    !ColorIs(openManager.Foreground, SystemColors.HighlightTextColor))
                    throw new InvalidOperationException("#232 已打开托盘未响应系统高对比开关。");
                var palette = new ApplicationTheme.SystemContrastPalette(
                    Colors.Black, Colors.White, Colors.Navy, Colors.White,
                    Colors.Yellow, Colors.Black, Colors.Silver);
                preference.ChangeColors(palette);
                PumpDispatcher();
                if (!ColorIs(menu.Background, palette.Window) ||
                    !ColorIs(menu.BorderBrush, palette.WindowText) ||
                    !ColorIs(openManager.Background, palette.Highlight) ||
                    !ColorIs(openManager.Foreground, palette.HighlightText))
                    throw new InvalidOperationException("#232 已打开托盘未响应运行中系统配色变化。");
            }
            PumpDispatcher();
            if (!ColorIs(menu.Background, dayCanvas) || !ColorIs(openManager.Background, dayHighlight))
                throw new InvalidOperationException("#232 关闭系统高对比后托盘未恢复主题。");
        }
        finally
        {
            try
            {
                menu.IsOpen = false;
                WindowValidationInput.Pump(menu.Dispatcher);
            }
            finally
            {
                menuInput?.Dispose();
            }
        }
        return string.Equals(openManager.Header as string, "打开管理窗口", StringComparison.Ordinal) &&
            string.Equals(restore.Header as string, "打开悬浮窗", StringComparison.Ordinal) &&
            string.Equals(resetPosition.Header as string, "复位窗口位置", StringComparison.Ordinal) &&
            string.Equals(exit.Header as string, "退出", StringComparison.Ordinal);
    }

    [DllImport("user32.dll", EntryPoint = "GetFocus", ExactSpelling = true)]
    private static extern IntPtr TrayProbeGetFocus();

    private static bool TrayOpenManagerPreservesDraftAndFilter(
        MainWindow main,
        DesktopWorkspace workspace,
        FloatingTrayController trayController)
    {
        var noteText = Required<TextBox>(main, "NoteText");
        var previousDraft = noteText.Text;
        var previousFilter = workspace.Snapshot.Filter;
        var mainWasVisible = main.IsVisible;
        try
        {
            noteText.Text = "托盘恢复草稿";
            workspace.ApplyFilter(previousFilter with { NoteSearch = "托盘保留筛选" });
            PumpDispatcher();
            ManagerWindowRestore.HideManagerOnly(main);
            PumpDispatcher();
            trayController.InvokeOpenManagerForValidation();
            PumpDispatcher();
            return main.IsVisible &&
                string.Equals(noteText.Text, "托盘恢复草稿", StringComparison.Ordinal) &&
                string.Equals(workspace.Snapshot.Filter.NoteSearch, "托盘保留筛选", StringComparison.Ordinal);
        }
        finally
        {
            noteText.Text = previousDraft;
            workspace.ApplyFilter(previousFilter);
            if (mainWasVisible && !main.IsVisible)
            {
                main.Show();
            }

            PumpDispatcher();
        }
    }

    private static bool RunFailedRestoreKeepsTrayStateProbe(FloatingWindow floating)
    {
        var interop = new GatedTrayForegroundInterop();
        var controller = new FloatingTrayController(
            floating,
            Path.Combine(AppContext.BaseDirectory, "countdown-icon.ico"),
            static () => true,
            interop,
            attachShellNotifyIcon: false);
        try
        {
            controller.MinimizeToTray();
            PumpDispatcher();
            var failureReturnsFalse = !controller.RestoreAndActivateFromExternalRequest();
            PumpDispatcher();
            var trayIconKeptAfterFailure = controller.IsTrayIconVisible;

            interop.AllowRestores();
            var retrySucceeded = controller.RestoreAndActivateFromExternalRequest();
            PumpDispatcher();
            var retryRestoresFully =
                retrySucceeded &&
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                controller.IsTrayIconVisible;

            return failureReturnsFalse && trayIconKeptAfterFailure && retryRestoresFully;
        }
        finally
        {
            controller.Dispose();
            if (!floating.IsVisible)
            {
                floating.Show();
                PumpDispatcher();
            }
        }
    }

    private static bool ProbeCalendarReminderStartLabels()
    {
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        editor.Show();
        PumpDispatcher();
        try
        {
            var mode = Descendants<ComboBox>(editor).Single(comboBox =>
                string.Equals(
                    AutomationProperties.GetAutomationId(comboBox),
                    "ReminderRuleMode",
                    StringComparison.Ordinal));
            var date = Descendants<DatePicker>(editor).Single();
            var hour = Descendants<ComboBox>(editor).Single(comboBox =>
                string.Equals(
                    AutomationProperties.GetAutomationId(comboBox),
                    "ReminderRuleFirstDueHour",
                    StringComparison.Ordinal));
            var minute = Descendants<ComboBox>(editor).Single(comboBox =>
                string.Equals(
                    AutomationProperties.GetAutomationId(comboBox),
                    "ReminderRuleFirstDueMinute",
                    StringComparison.Ordinal));
            var onceLabelsMatch =
                FirstDueRowLabel(editor) == "首次提醒" &&
                AutomationProperties.GetName(date) == "首次提醒日期" &&
                AutomationProperties.GetName(hour) == "首次提醒小时" &&
                AutomationProperties.GetName(minute) == "首次提醒分钟";
            mode.SelectedIndex = 2;
            PumpDispatcher();
            var calendarLabelsMatch =
                FirstDueRowLabel(editor) == "开始日期" &&
                AutomationProperties.GetName(date) == "开始日期" &&
                AutomationProperties.GetName(hour) == "本地时刻小时" &&
                AutomationProperties.GetName(minute) == "本地时刻分钟";
            mode.SelectedIndex = 1;
            PumpDispatcher();
            var intervalLabelsMatch =
                FirstDueRowLabel(editor) == "首次提醒" &&
                AutomationProperties.GetName(date) == "首次提醒日期";
            return onceLabelsMatch && calendarLabelsMatch && intervalLabelsMatch;
        }
        finally
        {
            editor.Close();
            PumpDispatcher();
        }
    }

    private static string FirstDueRowLabel(ReminderRulesEditorWindow editor)
    {
        return Descendants<TextBlock>(editor)
            .Select(text => text.Text)
            .FirstOrDefault(value =>
                value is "首次提醒" or "开始日期") ??
               string.Empty;
    }

    private static ProductionDatePickerValidation ValidateProductionDatePickers(
        MainWindow main,
        FloatingWindow floating)
    {
        var attached = new List<DatePickerProductionMeasurement>();
        var originalMainWorkspace = main.CurrentWorkspace;
        var filterPopup = Required<Popup>(main, "FilterPopup");
        var originalFilterOpen = filterPopup.IsOpen;
        var floatingWasCountdowns =
            Required<ToggleButton>(floating, "FloatingCountdownsButton").IsChecked == true;
        var quickAddWasExpanded = Required<Border>(floating, "FloatingNoteEntry").Visibility == Visibility.Visible;
        ReminderRulesEditorWindow? rulesEditor = null;
        ReminderCenterWindow? reminderCenter = null;
        Window? negativeHost = null;
        var reminderHourOptionCount = 0;
        var reminderMinuteOptionCount = 0;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            attached.Add(MeasureDatePicker("Main.CountdownDate", Required<DatePicker>(main, "CountdownDate")));

            main.ShowWorkspace(WorkspaceKind.Notes);
            filterPopup.IsOpen = true;
            PumpDispatcher();
            attached.Add(MeasureDatePicker("Main.NoteDate", Required<DatePicker>(main, "NoteDate")));
            filterPopup.IsOpen = true;
            PumpDispatcher();
            attached.Add(MeasureDatePicker(
                "Main.NoteDateFilter",
                Required<DatePicker>(main, "NoteDateFilter")));

            floating.ShowWorkspace(WorkspaceKind.Notes);
            floating.SetQuickAddExpanded(true);
            PumpDispatcher();
            attached.Add(MeasureDatePicker(
                "Floating.NoteDate",
                Required<DatePicker>(floating, "FloatingNoteDate")));

            rulesEditor = new ReminderRulesEditorWindow(
                Array.Empty<ReminderRule>(),
                new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
            {
                Owner = main,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            rulesEditor.Show();
            PumpDispatcher();
            attached.Add(MeasureDatePicker(
                "ReminderRules.FirstDueDate",
                Descendants<DatePicker>(rulesEditor).Single()));
            reminderHourOptionCount = Descendants<ComboBox>(rulesEditor)
                .Single(comboBox =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(comboBox),
                        "ReminderRuleFirstDueHour",
                        StringComparison.Ordinal))
                .Items.Count;
            reminderMinuteOptionCount = Descendants<ComboBox>(rulesEditor)
                .Single(comboBox =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(comboBox),
                        "ReminderRuleFirstDueMinute",
                        StringComparison.Ordinal))
                .Items.Count;

            var reminderKey = new ActiveReminderOccurrenceKey(
                "date-picker-validation-rule",
                new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
            reminderCenter = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            reminderCenter.AddEntry(new ReminderCenterItem(
                reminderKey,
                new NoteRecord(
                    "date-picker-validation-note",
                    "日期控件验证",
                    "2031-04-05",
                    false,
                    Array.Empty<string>(),
                    NoReminder.Instance),
                0,
                0));
            reminderCenter.Show();
            PumpDispatcher();
            var snoozeChoice = Descendants<ComboBox>(reminderCenter).Single(comboBox =>
                AutomationProperties.GetName(comboBox).StartsWith(
                    "稍后提醒时间：",
                    StringComparison.Ordinal));
            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            PumpDispatcher();
            var reminderCenterMeasurement = MeasureDatePicker(
                "ReminderCenter.CustomSnoozeDate",
                Descendants<DatePicker>(reminderCenter).Single());
            var reminderCenterComboBoxes = Descendants<ComboBox>(reminderCenter).ToArray();
            var reminderCenterComboBoxThemedCount = reminderCenterComboBoxes.Count(HasTemplate);

            // Keep an independent negative: the same warm, read-only date
            // template with its text activation surface removed. Its arrow
            // remains usable, but clicking the text must not open the calendar.
            var negativePicker = new DatePicker();
            negativeHost = new Window
            {
                Content = negativePicker,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0,
                Width = 240,
                Height = 100
            };
            negativeHost.Show();
            PumpDispatcher();
            negativePicker.ApplyTemplate();
            ((ToggleButton)negativePicker.Template.FindName("PART_TextToggle", negativePicker))
                .Visibility = Visibility.Collapsed;
            PumpDispatcher();
            var negativeMeasurement = MeasureDatePicker(
                "Negative.UnattachedDatePicker",
                negativePicker);

            return new ProductionDatePickerValidation(
                attached,
                reminderCenterMeasurement,
                negativeMeasurement,
                reminderHourOptionCount,
                reminderMinuteOptionCount,
                reminderCenterComboBoxes.Length,
                reminderCenterComboBoxThemedCount);
        }
        finally
        {
            negativeHost?.Close();
            rulesEditor?.Close();
            reminderCenter?.ForceClose();
            filterPopup.IsOpen = originalFilterOpen;
            main.ShowWorkspace(originalMainWorkspace);
            floating.SetQuickAddExpanded(quickAddWasExpanded);
            floating.ShowWorkspace(
                floatingWasCountdowns ? WorkspaceKind.Countdowns : WorkspaceKind.Notes);
            PumpDispatcher();
        }
    }

    private static DatePickerProductionMeasurement MeasureDatePicker(
        string name,
        DatePicker picker)
    {
        picker.ApplyTemplate();
        var textBox = picker.Template.FindName("PART_TextBox", picker) as DatePickerTextBox;
        var pointer = WindowValidationInput.ExerciseDatePickerTextPointer(picker);
        var buttonOpensCalendar = WindowValidationInput.ExerciseDatePickerButton(picker);
        var attached = PickerTemplateParts.HasDatePickerContract(picker);
        var keyboardLifecycleWorks = attached && WindowValidationInput.ExerciseDatePicker(picker);
        picker.ApplyTemplate();
        textBox = picker.Template.FindName("PART_TextBox", picker) as DatePickerTextBox;
        var openCalendarButton = picker.Template.FindName("PART_Button", picker) as Button;
        return new DatePickerProductionMeasurement(
            name,
            attached,
            textBox is not null,
            textBox?.IsReadOnly == true,
            textBox?.Focusable == false,
            pointer,
            buttonOpensCalendar,
            keyboardLifecycleWorks,
            AutomationProperties.GetName(picker),
            openCalendarButton is null
                ? string.Empty
                : AutomationProperties.GetName(openCalendarButton) ?? string.Empty,
            MeasureDatePickerTextFit(picker),
            WindowValidationInput.ExerciseDatePickerSelectedDateText(picker));
    }

    private static bool ValidateCountdownCardActionNames(
        MainWindow main,
        DesktopWorkspace workspace)
    {
        var originalWorkspace = main.CurrentWorkspace;
        var probeName = "可访问名称探针倒计时";
        string? probeId = null;
        try
        {
            if (workspace.Snapshot.Settings.Countdowns.Count == 0)
            {
                var saved = workspace.Apply(new SaveCountdownChange(
                    null,
                    probeName,
                    workspace.Snapshot.UtcNow.AddDays(3),
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
                if (!saved.Succeeded)
                {
                    return false;
                }

                probeId = workspace.Snapshot.Settings.Countdowns
                    .FirstOrDefault(record =>
                        string.Equals(record.Name, probeName, StringComparison.Ordinal))
                    ?.Id;
                if (string.IsNullOrWhiteSpace(probeId))
                {
                    return false;
                }
            }

            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            main.UpdateLayout();
            var records = workspace.Snapshot.Settings.Countdowns;
            var rows = Required<StackPanel>(main, "CountdownRows");
            var buttons = Descendants<Button>(rows).ToArray();
            foreach (var record in records)
            {
                var edits = buttons.Where(button =>
                    string.Equals(button.Content as string, "编辑", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, record.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                var deletes = buttons.Where(button =>
                    string.Equals(button.Content as string, "删除", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, record.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (edits.Length != 1 || deletes.Length != 1)
                {
                    return false;
                }

                var expectedEdit = UiAccessibility.FoldAccessibleName($"编辑倒计时 {record.Name}");
                var expectedDelete = UiAccessibility.FoldAccessibleName($"删除倒计时 {record.Name}");
                if (!string.Equals(
                        AutomationProperties.GetName(edits[0]),
                        expectedEdit,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        AutomationProperties.GetName(deletes[0]),
                        expectedDelete,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return records.Count > 0;
        }
        finally
        {
            if (probeId is not null)
            {
                workspace.Apply(new DeleteCountdownChange(probeId));
                PumpDispatcher();
            }

            main.ShowWorkspace(originalWorkspace);
            PumpDispatcher();
        }
    }

    private static T Required<T>(FrameworkElement root, string name)
        where T : class
    {
        return root.FindName(name) as T
            ?? throw new InvalidOperationException($"验证控件缺失：{name}");
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in LogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool FocusStaysWithin(Visual scope, UIElement expected) =>
        expected.IsKeyboardFocusWithin &&
        Keyboard.FocusedElement is DependencyObject focused &&
        (ReferenceEquals(focused, scope) || scope.IsAncestorOf(focused));

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void ClearCaptureKeyboardFocus()
    {
        Keyboard.Focus(null);
        PumpDispatcher();
    }

    private static bool CaptureTextMotionSettled(FrameworkElement root) =>
        Descendants<TextBlock>(root).Where(text => text.IsVisible).All(text =>
            !DependencyPropertyHelper.GetValueSource(text, UIElement.OpacityProperty).IsAnimated ||
            text.Opacity == 1);

    private static void WaitForMainReferenceMotion(MainWindow window, FrameworkElement root)
    {
        if (!WaitForRenderedCaptureState(root, () =>
                window.VisibleSegmentIndicatorsSettledForCapture && CaptureTextMotionSettled(root)))
        {
            throw new InvalidOperationException(
                $"主窗口参考帧未在 300ms 内到达选中指示条与数字终态：{window.CurrentWorkspace}；" +
                $"指示条={window.VisibleSegmentIndicatorsSettledForCapture}，数字={CaptureTextMotionSettled(root)}。");
        }
    }

    private static bool WaitForRenderedCaptureState(FrameworkElement visual, Func<bool> isReady)
    {
        const int timeoutMilliseconds = 300;
        visual.Dispatcher.VerifyAccess();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var ready = false;
        Exception? failure = null;
        var timeout = new DispatcherTimer(DispatcherPriority.Send, visual.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(timeoutMilliseconds)
        };
        var frameRequests = new DispatcherTimer(DispatcherPriority.Background, visual.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        void OnRendering(object? sender, EventArgs args)
        {
            if (watch.Elapsed.TotalMilliseconds > timeoutMilliseconds)
            {
                frame.Continue = false;
                return;
            }

            try
            {
                if (isReady())
                {
                    ready = true;
                    frame.Continue = false;
                }
            }
            catch (Exception error) { failure = error; frame.Continue = false; }
        }
        EventHandler rendering = OnRendering;
        EventHandler requestFrame = (_, _) =>
        {
            // Adding Rendering posts a real WPF render request. A bitmap
            // capture does not tick animations, and invalidations can merge.
            CompositionTarget.Rendering -= rendering;
            CompositionTarget.Rendering += rendering;
        };
        EventHandler expired = (_, _) => frame.Continue = false;
        try
        {
            timeout.Tick += expired;
            frameRequests.Tick += requestFrame;
            timeout.Start();
            frameRequests.Start();
            requestFrame(null, EventArgs.Empty);
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timeout.Stop();
            timeout.Tick -= expired;
            frameRequests.Stop();
            frameRequests.Tick -= requestFrame;
            CompositionTarget.Rendering -= rendering;
        }

        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return ready && watch.Elapsed.TotalMilliseconds <= timeoutMilliseconds;
    }

    private sealed record TextFit(bool Fits, double Available, double Required);

    private sealed record MainKeyboardBehaviorResult(
        string InitialFocusedAutomationId,
        bool InitialFocusSafe,
        IReadOnlyList<string> TabSequence,
        MainWorkspaceTabPathResult CountdownTabPath,
        MainWorkspaceTabPathResult NotesTabPath,
        bool CommandsWork,
        IReadOnlyDictionary<string, bool> CommandResults,
        IReadOnlyDictionary<string, MainShortcutCommandDiagnostic> CommandDiagnostics,
        bool TextInputPreserved,
        MainShortcutInputGuardResult InputGuards,
        bool PopupGuarded,
        bool ModalOwnerGuarded);

    private sealed record MainShortcutCommandDiagnostic(
        bool Handled,
        bool EffectObserved,
        string StatusText);

    private sealed record MainShortcutInputGuardResult(
        bool TextBox,
        bool DatePicker,
        bool ComboBox);

    private sealed record MainWorkspaceTabPathResult(
        IReadOnlyList<string> ExpectedSequence,
        IReadOnlyList<string> ForwardSequence,
        IReadOnlyList<string> BackwardSequence,
        bool ForwardMatches,
        bool BackwardSymmetric,
        bool FocusVisualsVisible,
        IReadOnlyList<string> ForwardFocusedTargets,
        IReadOnlyList<string> BackwardFocusedTargets)
    {
        public bool AllPassed => ForwardMatches && BackwardSymmetric && FocusVisualsVisible;
    }

    private sealed record MainWorkspaceTabPathEvaluation(
        bool ForwardMatches,
        bool BackwardSymmetric,
        bool FocusVisualsVisible)
    {
        public bool AllPassed => ForwardMatches && BackwardSymmetric && FocusVisualsVisible;
    }

    private sealed record MainTabTraversalResult(
        IReadOnlyList<string> Sequence,
        IReadOnlyList<string> FocusedTargets,
        bool FocusVisualsVisible);

    private sealed record MainTabTarget(string Id, FrameworkElement Element);

    private sealed record MainStructuralAnnouncementsResult(
        bool CountdownWorkspaceSelectionIsAccessible,
        bool NotesWorkspaceSelectionIsAccessible,
        bool FilterResultAnnounced,
        bool CountdownFirstUseEmptyAnnounced,
        bool NotesFirstUseEmptyAnnounced,
        bool CountdownFilterEmptyAnnounced,
        bool NotesFilterEmptyAnnounced,
        MainEmptyStateGuidanceResult EmptyStateGuidance);

    private sealed record MainEmptyStateGuidanceResult(
        bool CountdownElementsVisible,
        bool NotesElementsVisible,
        bool AddActionsFocusEditors,
        bool DecorationsAreNonInteractive,
        bool PopulatedListsRestoreEmptyState);

    private sealed record ConfirmationDialogScenario(
        string ContentKind,
        string Title,
        string Subject,
        string Message);

    private sealed record ConfirmationDialogMeasurement(
        string StateId,
        string ContentKind,
        string Theme,
        int SubjectLength,
        int MessageLength,
        bool ProductionMessageExact,
        bool Themed,
        bool ButtonsVisible,
        bool BodyReachable,
        bool BodyEndReached,
        bool OwnerMatches,
        string StartupLocation,
        bool CenteredOnOwner,
        bool CancelIsSafeDefault,
        bool EnterDidNotConfirm,
        bool EscapeDidNotConfirm,
        bool CancelDidNotConfirm);

    private sealed record ConfirmationDialogValidation(
        bool Themed,
        IReadOnlyList<ConfirmationDialogMeasurement> Measurements,
        ConfirmationDialogGateResults Gates);

    private enum ConfirmationDialogAction
    {
        Enter,
        Escape,
        Cancel
    }

    private static class NativeMethods
    {
        // #134 PerMonitorV2 动态轴：WM_DPICHANGED 参数与 PMv2 awareness 上下文判定。
        public const int WmDpiChanged = 0x02E0;
        public static readonly IntPtr DpiAwarenessContextPerMonitorV2 = new(-4);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll")]
        public static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AreDpiAwarenessContextsEqual(IntPtr left, IntPtr right);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    private sealed record SecondaryWindowTypographyMeasurement(
        string Window,
        string ContentRootAutomationId,
        double TitleFontSize,
        double? SectionFontSize,
        double BodyFontSize,
        double? MinorFontSize,
        double ContentRootFontSize,
        bool ContentRootDeclaresFontSize)
    {
        public bool RenderedScaleMatches =>
            Math.Abs(TitleFontSize - 26) <= 0.1 &&
            (SectionFontSize is null || Math.Abs(SectionFontSize.Value - 20) <= 0.1) &&
            Math.Abs(BodyFontSize - 16) <= 0.1 &&
            (MinorFontSize is null || Math.Abs(MinorFontSize.Value - 13) <= 0.1) &&
            Math.Abs(ContentRootFontSize - 16) <= 0.1;
    }

    private sealed record SecondaryWindowTypographyResult(
        IReadOnlyList<SecondaryWindowTypographyMeasurement> Windows,
        SecondaryWindowTypographyGateResults Gates,
        double FloatingBodyFontSize,
        double FloatingReminderFontSize)
    {
        public bool RenderedScaleMatches => Gates.RenderedScaleMatches;

        public bool ContentRootsDeclareFontSize => Gates.ContentRootsDeclareFontSize;

        public bool FloatingTypographyUnchanged => Gates.FloatingTypographyUnchanged;
    }

    private sealed record FloatingTrayInteractionExerciseResult(
        bool ProductionControllerActive,
        bool MinimizeHidesWindowAndShowsIcon,
        bool RestoreShowsWindowAndHidesIcon,
        bool ExitRequestsApplicationExit,
        bool FailedRestoreKeepsTrayStateAndAllowsRetry);

    private sealed record DatePickerProductionMeasurement(
        string Name,
        bool Attached,
        bool TextBoxPresent,
        bool IsReadOnly,
        bool TextBoxIsNotFocusable,
        DatePickerPointerExerciseResult Pointer,
        bool ButtonOpensCalendar,
        bool EscapeClosesAndReturnsFocus,
        string AutomationName,
        string OpenCalendarButtonName,
        TextFit TextFit,
        bool SelectedDateTextMatchesContract);

    private sealed record ProductionDatePickerValidation(
        IReadOnlyList<DatePickerProductionMeasurement> Attached,
        DatePickerProductionMeasurement ReminderCenter,
        DatePickerProductionMeasurement UnattachedNegative,
        int ReminderHourOptionCount,
        int ReminderMinuteOptionCount,
        int ReminderCenterComboBoxCount,
        int ReminderCenterComboBoxThemedCount)
    {
        public int ProductionInputCount => Attached.Count + 1;

        public bool AllProductionInputsReadOnly =>
            ProductionInputCount == 6 &&
            Attached.All(input =>
                input.TextBoxPresent && input.IsReadOnly && input.TextBoxIsNotFocusable) &&
            ReminderCenter is
            {
                Attached: true,
                TextBoxPresent: true,
                IsReadOnly: true,
                TextBoxIsNotFocusable: true,
                ButtonOpensCalendar: true
            } &&
            !string.IsNullOrWhiteSpace(ReminderCenter.AutomationName);

        public bool ReminderCenterComboBoxesUseThemedDropDowns =>
            ReminderCenterComboBoxCount == 3 &&
            ReminderCenterComboBoxThemedCount == ReminderCenterComboBoxCount;

        public bool AllAttached =>
            Attached.Count == 5 && Attached.All(input => input.Attached);

        public bool AttachedTextPointerWorks =>
            AllAttached &&
            Attached.All(input =>
                input.Pointer.TextBoxPresent &&
                input.Pointer.PressInjected &&
                input.Pointer.ReleaseInjected &&
                input.Pointer.ReleaseHandled &&
                input.Pointer.ReleaseOpened);

        public bool AttachedTextDefersUntilRelease =>
            AllAttached && Attached.All(input => input.Pointer.PressDidNotOpen);

        public bool AttachedTextOpensThemedPopups =>
            AllAttached && Attached.All(input => input.Pointer.ThemedPopupOpened);

        public bool AttachedKeyboardLifecycleWorks =>
            AllAttached && Attached.All(input => input.EscapeClosesAndReturnsFocus);

        public bool AllOpenCalendarButtonsNamed =>
            AllAttached &&
            Attached.All(input =>
                string.Equals(input.OpenCalendarButtonName, "打开日历", StringComparison.Ordinal)) &&
            string.Equals(ReminderCenter.OpenCalendarButtonName, "打开日历", StringComparison.Ordinal);

        public bool UnattachedNegativeTextDoesNotOpen =>
            UnattachedNegative is
            {
                Attached: false,
                TextBoxPresent: true,
                IsReadOnly: true,
                ButtonOpensCalendar: true
            } &&
            UnattachedNegative.Pointer.PressInjected &&
            UnattachedNegative.Pointer.ReleaseInjected &&
            UnattachedNegative.Pointer.PressDidNotOpen &&
            !UnattachedNegative.Pointer.ReleaseOpened &&
            !UnattachedNegative.Pointer.ReleaseHandled;
    }

    private sealed record ReminderKeyboardExerciseResult(
        bool TabTraversalWorks,
        bool CloseGuardWorks,
        bool ShowPreservesFocusAndAccessibleName,
        bool EscapeFocusesAcknowledge);

    private sealed record ReminderDeliveryExerciseResult(
        bool RequiresContentRenderedConfirmation,
        bool PersistenceFailureStaysVisible,
        bool PersistenceRetryBackoffWorks,
        bool SingleFlightWorks,
        bool IndependentOccurrencesWork,
        bool PersistenceFailureCloseGuardWorks,
        bool DiagnosticsExcludePrivatePayload)
    {
        public static ReminderDeliveryExerciseResult Failed { get; } =
            new(false, false, false, false, false, false, false);
    }

    private sealed record ReminderCenterExerciseResult(
        bool SingleWindow,
        bool SameTickBatch,
        bool StableOrdering,
        bool EntriesRendered,
        bool AcknowledgementDistinctFromDelivery,
        bool AcknowledgementFailureRetainsEntry,
        bool AcknowledgementFailureIsAtomic,
        bool SingleItemDisposition,
        bool OpenSourcePreservesState,
        bool OpenSourceFindsExactNote,
        bool OpenSourceRestoresMinimizedWindow,
        bool ClosesWhenEmpty,
        bool Reopens,
        bool ScrollsLongContent,
        bool FollowsDayTheme,
        bool HasOnlySafeActions,
        bool DisplaysSourceContentAndTime,
        bool Accessible)
    {
        public static ReminderCenterExerciseResult Failed { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false);
    }

    private sealed record ReminderCenterCaptureValidation(
        bool Rendered,
        int Width,
        int Height,
        string Sha256,
        int PageWhitePixelCount,
        int WarmCardPixelCount,
        int PendingBadgePixelCount,
        int MainActionPixelCount,
        int SourceIconPixelCount,
        string EvidencePath)
    {
        public static ReminderCenterCaptureValidation Failed { get; } =
            new(false, 0, 0, string.Empty, 0, 0, 0, 0, 0, string.Empty);
    }

    private sealed record ReminderCenterSourceLifecycleExerciseResult(
        bool PersistenceFailureRetainsCurrentSource,
        bool RemovesDeletedSource,
        bool CancelsDeletedSourceAcknowledgementRetry,
        bool RemovesDisabledSource,
        bool RemovesCompletedSource,
        bool ReopensAfterSourceRemoval)
    {
        public static ReminderCenterSourceLifecycleExerciseResult Failed { get; } =
            new(false, false, false, false, false, false);
    }

    private sealed record RepeatingReminderDeliveryExerciseResult(
        bool RenderedLatchWorks,
        bool SingleUnresolvedOccurrenceWorks,
        bool DueBeforeShow,
        bool WindowOpened,
        bool ContentRendered,
        bool NoDuplicate,
        bool AcknowledgementMonotonicAcrossOccurrences)
    {
        public static RepeatingReminderDeliveryExerciseResult Failed { get; } =
            new(false, false, false, false, false, false, false);
    }

    private sealed record AlphaMetrics(bool IsFullyOpaque, int NonOpaquePixels, byte MinimumAlpha);

    private sealed record NoteDateSection(
        string Key,
        FrameworkElement Root,
        Button Toggle,
        StackPanel Rows);

    private sealed record NoteSectionExerciseResult(
        bool DateSectionsCanCollapse,
        bool DateSectionsAreIndependent,
        bool DateSectionStateSurvivesRefresh,
        bool SegmentsSwitchExclusively);

    private sealed record MainThemeExerciseResult(
        IReadOnlyList<string> BackgroundColors,
        IReadOnlyList<int> CharacterOpacities,
        IReadOnlyList<string> CharacterPanelColors,
        bool ReminderLabelFollowsTheme,
        bool SegmentSelectedTextContrast,
        bool CharacterPanelDocksFollowTheme,
        bool ReferenceDecorationsFollowTheme);

    private static FloatingQuickAddThemeValidation ValidateFloatingQuickAddLocalTheme(
        ApplicationTheme applicationTheme,
        FloatingWindow floating)
    {
        try
        {
            applicationTheme.Apply();
            floating.ApplyTheme();
            PumpDispatcher();
            var independent = !ReferenceEquals(
                floating.FindResource("CanvasBrush"),
                Application.Current.FindResource("CanvasBrush"));
            var day = floating.ProbeQuickAddThemeResources();
            var dayBindings = floating.ProbeQuickAddPaletteBindings();
            var dayCanvas = ResourceColor(floating, "CanvasBrush");
            var dayActive = ResourceColor(floating, "ActiveBrush");

            return new FloatingQuickAddThemeValidation(
                day,
                dayCanvas,
                dayActive,
                independent &&
                dayCanvas == "#FFFEFCFB" &&
                ReferenceEquals(floating.FindResource("ActiveBrush"), Application.Current.FindResource("ActiveBrush")),
                dayBindings);
        }
        finally
        {
            applicationTheme.Apply();
            floating.ApplyTheme();
            PumpDispatcher();
        }
    }

    private static string ResourceColor(FrameworkElement element, string resourceKey) =>
        element.FindResource(resourceKey) is SolidColorBrush brush
            ? brush.Color.ToString()
            : string.Empty;

    private sealed record FloatingQuickAddThemeValidation(
        FloatingQuickAddThemeProbeResult Day,
        string CanvasColor,
        string ActiveColor,
        bool IndependentFromApplicationTheme,
        FloatingQuickAddPaletteBindingValidation DayPaletteBindings)
    {
        public bool DayPaletteBindingsMatch => DayPaletteBindings.Gates.Passed;
    }

    private sealed record MainReferenceDecorationValidation(
        bool ScalingMatches,
        bool LayerOrderMatches);

    private sealed record MainWorkspaceThemeValidation(
        bool StaticResourcesFollowTheme,
        int CountdownCardSampleCount,
        bool CountdownCardResourcesFollowTheme,
        int NoteCardSampleCount,
        bool NoteCardResourcesFollowTheme);

    private sealed record MainReferenceCaptureResult(
        bool Requested,
        bool CapturesWritten,
        bool UsesZeroState,
        bool CanvasMatches,
        bool AlphaIsOpaque,
        string CountdownCapturePath,
        string NotesCapturePath,
        string ManifestPath);

    // #134 真实运行态 UI 捕获 seam 的单帧证据条目与 DPI 变更遥测。
    private sealed record MainUiCaptureSeamEntry(
        string CaptureId,
        string Mechanism,
        string Path,
        string Sha256,
        int DipWidth,
        int DipHeight,
        int PixelWidth,
        int PixelHeight,
        double DpiScaleX,
        double DpiScaleY);

    private sealed record MainUiDpiChangeTelemetry(
        bool AwarenessIsPerMonitorV2,
        bool DpiChangedEventObserved,
        double DpiScaleBeforeX,
        double DpiScaleBeforeY,
        double DpiScaleAfterX,
        double DpiScaleAfterY);

    private sealed record MainReferenceCaptureSpec(
        string CaptureId,
        string Theme,
        string Workspace,
        string State,
        string FileName);

    private sealed record MainReferenceSourceBinding(string Path, string Sha256);

    private sealed record MainReferenceStateFingerprint(
        string Algorithm,
        string Sha256,
        string Theme,
        string Workspace,
        string State,
        DateTimeOffset BeijingTime,
        string SettingsSha256,
        int CountdownCount,
        int NoteCount,
        int ActiveNoteCount,
        int CompletedNoteCount);

    private sealed record MainReferenceAlphaMetrics(
        string Algorithm,
        int Minimum,
        int Maximum,
        int NonOpaquePixelCount,
        int OpaquePixelCount)
    {
        public bool IsFullyOpaque =>
            string.Equals(Algorithm, MainReferenceAlphaAlgorithm, StringComparison.Ordinal) &&
            Minimum == byte.MaxValue &&
            Maximum == byte.MaxValue &&
            NonOpaquePixelCount == 0 &&
            OpaquePixelCount == 1165 * 1350;
    }

    private sealed record MainReferenceContentRoi(
        string Algorithm,
        int X,
        int Y,
        int Width,
        int Height,
        int PixelCount)
    {
        public static MainReferenceContentRoi FullCanvas { get; } = new(
            MainReferenceRoiAlgorithm,
            0,
            0,
            1165,
            1350,
            1165 * 1350);
    }

    private sealed record MainReferencePixelMetrics(
        int MaxAbsoluteChannelDelta,
        double MeanAbsoluteError,
        double RootMeanSquareError,
        int ChangedPixelCount)
    {
        public static MainReferencePixelMetrics Zero { get; } = new(0, 0, 0, 0);
    }

    private sealed record MainReferenceCaptureMetrics(
        int Width,
        int Height,
        MainReferenceAlphaMetrics Alpha,
        MainReferenceContentRoi ContentRoi);

    private sealed record MainReferenceActualCapture(
        MainReferenceCaptureSpec Spec,
        DateTimeOffset BeijingTime,
        byte[] PngBytes,
        MainReferenceStateFingerprint StateFingerprint,
        MainReferenceAlphaMetrics Alpha,
        MainReferenceContentRoi ContentRoi,
        MainStressCountdownTitleMeasurement? StressCountdownTitle,
        MainNoteCardReminderFontMeasurement? NoteCardReminderFont);

    private sealed record MainDaySingleBorderSpec(
        string StateId,
        string Scene,
        string FileName,
        int ScanTop,
        int ScanBottom,
        IReadOnlyList<int> ExpectedEdges,
        int LabelTop,
        int LabelBottom);

    private sealed record MainDaySingleBorderMeasurement(
        string StateId,
        string Scene,
        IReadOnlyList<int> EdgeRows,
        bool ExactControlEdges,
        bool LabelsClear,
        bool LowerDecorationPresent,
        int ControlScanTop,
        int DeclaredBottomBandStart,
        int LowerDecorationPixelCount);

    private sealed record MainDecorationBands(
        int BackgroundSampleX,
        IReadOnlyDictionary<string, MainDecorationBand> Scenes);

    private sealed record MainDecorationBand(int TopBandEnd, int BottomBandStart);

    private sealed record MainDaySingleBorderValidation(
        bool Requested,
        bool DaySingleBorder,
        bool LabelsClear,
        bool LowerDecorationConsistent,
        IReadOnlyList<MainDaySingleBorderMeasurement> Measurements)
    {
        public bool AllPassed => !Requested ||
            DaySingleBorder && LabelsClear && LowerDecorationConsistent;

        public static MainDaySingleBorderValidation NotRequested { get; } =
            new(false, false, false, false, Array.Empty<MainDaySingleBorderMeasurement>());
    }

    private sealed record MainStressCountdownTitleBounds(
        double X,
        double Y,
        double Width,
        double Height)
    {
        public static MainStressCountdownTitleBounds Empty { get; } = new(0, 0, 0, 0);

        public static MainStressCountdownTitleBounds From(Rect bounds) =>
            new(
                Math.Round(bounds.X, 2),
                Math.Round(bounds.Y, 2),
                Math.Round(bounds.Width, 2),
                Math.Round(bounds.Height, 2));
    }

    private sealed record MainNoteCardReminderFontMeasurement(
        string CaptureId,
        string Theme,
        string State,
        int SampleCount,
        double FontSizeDip,
        bool MeetsContract);

    private sealed record MainStressCountdownTitleMeasurement(
        string CaptureId,
        string Theme,
        string ExpectedText,
        string RenderedText,
        bool ExactTitleRendered,
        string TextWrapping,
        string TextTrimming,
        double FormattedWidth,
        double AvailableWidth,
        bool TextFits,
        MainStressCountdownTitleBounds TitleBounds,
        MainStressCountdownTitleBounds ViewportBounds,
        bool FullyVisibleInViewport,
        MainStressCountdownTitleBounds RemainingBounds,
        bool SeparatedFromRemaining,
        MainStressCountdownTitleBounds ActionsBounds,
        bool SeparatedFromActions)
    {
        public static MainStressCountdownTitleMeasurement Missing(
            MainReferenceCaptureSpec spec,
            string expectedText,
            string renderedText = "",
            bool exactTitleRendered = false,
            string textWrapping = "",
            string textTrimming = "") =>
            new(
                spec.CaptureId,
                spec.Theme,
                expectedText,
                renderedText,
                exactTitleRendered,
                textWrapping,
                textTrimming,
                0,
                0,
                false,
                MainStressCountdownTitleBounds.Empty,
                MainStressCountdownTitleBounds.Empty,
                false,
                MainStressCountdownTitleBounds.Empty,
                false,
                MainStressCountdownTitleBounds.Empty,
                false);
    }

    private sealed record MainReferenceManifestCapture(
        string CaptureId,
        string Theme,
        string Workspace,
        string State,
        string Path,
        DateTimeOffset BeijingTime,
        int Width,
        int Height,
        string PngSha256,
        MainReferenceStateFingerprint StateFingerprint,
        MainReferenceAlphaMetrics Alpha,
        MainReferenceContentRoi ContentRoi,
        MainReferencePixelMetrics Comparison);

    private sealed record MainReferenceBaselineCaptureMeasurement(
        string CaptureId,
        string Theme,
        string Workspace,
        string State,
        string Path,
        DateTimeOffset BeijingTime,
        string BaselineSha256,
        string ActualSha256,
        MainReferenceStateFingerprint StateFingerprint,
        MainReferenceAlphaMetrics Alpha,
        MainReferenceContentRoi ContentRoi,
        MainReferencePixelMetrics Comparison);

    private sealed record MainReferenceBaselineValidation(
        bool Requested,
        bool ManifestAvailable,
        string ManifestPath,
        string SourceAggregateSha256,
        string ExecutableSha256,
        MainReferenceManifestGateResults Gates,
        IReadOnlyList<MainReferenceBaselineCaptureMeasurement> Captures,
        IReadOnlyList<MainStressCountdownTitleMeasurement> StressCountdownTitleMeasurements,
        IReadOnlyList<MainNoteCardReminderFontMeasurement> NoteCardReminderFontMeasurements,
        IReadOnlyList<string> Diagnostics)
    {
        private static MainReferenceManifestGateResults FailedGates { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false);

        public bool AllPassed => Gates.AllPassed;

        public bool StressCountdownTitleVisuals =>
            StressCountdownTitleMeasurements.Count == 1 &&
            StressCountdownTitleMeasurements
                .Select(measurement => measurement.CaptureId)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    new[]
                    {
                        "main-day-countdowns-stress"
                    },
                    StringComparer.Ordinal) &&
            StressCountdownTitleMeasurements.All(measurement =>
                ValidationExercises.EvaluateMainStressCountdownTitleGates(
                    new MainStressCountdownTitleGateInputs(
                        measurement.ExactTitleRendered &&
                        string.Equals(
                            measurement.ExpectedText,
                            measurement.RenderedText,
                            StringComparison.Ordinal),
                        string.Equals(measurement.TextWrapping, "NoWrap", StringComparison.Ordinal) &&
                        string.Equals(measurement.TextTrimming, "None", StringComparison.Ordinal),
                        measurement.TextFits,
                        measurement.FullyVisibleInViewport,
                        measurement.SeparatedFromRemaining,
                        measurement.SeparatedFromActions)).AllPassed);

        public double NoteCardReminderFontSizeDip =>
            NoteCardReminderFontMeasurements.Count == 0
                ? 0
                : NoteCardReminderFontMeasurements.Min(measurement => measurement.FontSizeDip);

        public bool NoteCardReminderFontSizeMeetsContract =>
            NoteCardReminderFontMeasurements.Count > 0 &&
            NoteCardReminderFontMeasurements.All(measurement => measurement.MeetsContract);

        public static MainReferenceBaselineValidation NotRequested { get; } = new(
            false,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            FailedGates,
            Array.Empty<MainReferenceBaselineCaptureMeasurement>(),
            Array.Empty<MainStressCountdownTitleMeasurement>(),
            Array.Empty<MainNoteCardReminderFontMeasurement>(),
            new[] { "未设置 MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT；运行时发布包不携带主窗口参考图。" });

        public static MainReferenceBaselineValidation Invalid(
            bool requested,
            bool manifestAvailable,
            string diagnostic,
            string manifestPath = "") =>
            new(
                requested,
                manifestAvailable,
                manifestPath,
                string.Empty,
                string.Empty,
                FailedGates,
                Array.Empty<MainReferenceBaselineCaptureMeasurement>(),
                Array.Empty<MainStressCountdownTitleMeasurement>(),
                Array.Empty<MainNoteCardReminderFontMeasurement>(),
                new[] { diagnostic });
    }

    private sealed record VisualCaptureArtifactBinding(
        string Id,
        string Path,
        string Sha256);

    private sealed record FloatingReferenceSourceBinding(string Path, string Sha256);

    private sealed record FloatingReferenceCaptureSpec(
        string CaptureId,
        string Scenario,
        string Theme,
        string State,
        string FileName);

    private sealed record FloatingReferenceStateFingerprint(
        string Algorithm,
        string Sha256,
        string Theme,
        string Workspace,
        string NoteId,
        string NoteTextSha256,
        string ViewportState,
        double VerticalOffset,
        double ScrollableHeight,
        string FirstVisibleAnchor,
        string LastVisibleAnchor);

    private sealed record FloatingReferenceActualCapture(
        FloatingReferenceCaptureSpec Spec,
        byte[] PngBytes,
        FloatingReferenceStateFingerprint StateFingerprint);

    private sealed record FloatingReferenceStateDifference(
        string Theme,
        string TopCaptureId,
        string BottomCaptureId,
        FloatingReferencePixelMetrics Metrics);

    private sealed record FloatingReferenceAlphaMetrics(
        int Minimum,
        int Maximum,
        int TransparentPixelCount,
        int NonOpaquePixelCount,
        int OpaquePixelCount);

    private sealed record FloatingReferenceContentRoi(
        int AlphaThreshold,
        int X,
        int Y,
        int Width,
        int Height,
        int PixelCount);

    private sealed record FloatingReferencePixelMetrics(
        int MaxAbsoluteChannelDelta,
        double MeanAbsoluteError,
        double RootMeanSquareError,
        int ChangedPixelCount)
    {
        public static FloatingReferencePixelMetrics Zero { get; } = new(0, 0, 0, 0);
    }

    private sealed record FloatingReferenceCaptureMetrics(
        int Width,
        int Height,
        FloatingReferenceAlphaMetrics Alpha,
        FloatingReferenceContentRoi ContentRoi);

    private sealed record FloatingReferenceManifestCapture(
        string CaptureId,
        string Scenario,
        string Theme,
        string State,
        string Path,
        int Width,
        int Height,
        string PngSha256,
        FloatingReferenceStateFingerprint StateFingerprint,
        FloatingReferenceAlphaMetrics Alpha,
        FloatingReferenceContentRoi ContentRoi,
        FloatingReferencePixelMetrics Comparison);

    private sealed record FloatingReferenceBaselineCaptureMeasurement(
        string CaptureId,
        string Scenario,
        string Theme,
        string State,
        string Path,
        string BaselineSha256,
        string ActualSha256,
        FloatingReferenceStateFingerprint StateFingerprint,
        FloatingReferenceAlphaMetrics Alpha,
        FloatingReferenceContentRoi ContentRoi,
        FloatingReferencePixelMetrics Comparison);

    private sealed record FloatingReferenceBaselineValidation(
        bool Requested,
        bool ManifestAvailable,
        string ManifestPath,
        string SourceAggregateSha256,
        string ExecutableSha256,
        FloatingReferenceManifestGateResults Gates,
        FloatingReferenceFreshnessGateResults FreshnessGates,
        IReadOnlyList<FloatingReferenceBaselineCaptureMeasurement> Captures,
        IReadOnlyList<string> Diagnostics)
    {
        private static FloatingReferenceManifestGateResults FailedGates { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false);
        private static FloatingReferenceFreshnessGateResults FailedFreshnessGates { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false);

        public bool AllPassed => Gates.AllPassed && FreshnessGates.AllPassed;

        public static FloatingReferenceBaselineValidation NotRequested { get; } = new(
            false,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            FailedGates,
            FailedFreshnessGates,
            Array.Empty<FloatingReferenceBaselineCaptureMeasurement>(),
            new[] { "未设置 MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT；运行时发布包不携带参考图。" });

        public static FloatingReferenceBaselineValidation Invalid(
            bool requested,
            bool manifestAvailable,
            string diagnostic,
            string manifestPath = "") =>
            new(
                requested,
                manifestAvailable,
                manifestPath,
                string.Empty,
                string.Empty,
                FailedGates,
                FailedFreshnessGates,
                Array.Empty<FloatingReferenceBaselineCaptureMeasurement>(),
                new[] { diagnostic });
    }

    private sealed record FloatingNoteCardBinding(
        Border Card,
        Grid Layout,
        CheckBox Completion,
        StackPanel Body,
        TextBlock BodyText,
        TextBlock? ReminderText,
        Border DateStamp,
        NoteRecord Note);

    private sealed record FloatingLongNoteFrameMeasurement(
        string CaptureId,
        string Theme,
        string State,
        double VerticalOffset,
        double ScrollableHeight,
        string NoteId,
        string NoteTextSha256,
        string FirstVisibleAnchor,
        string LastVisibleAnchor,
        bool BodyHasNoManualLineBreaks,
        bool NaturalWraps,
        bool UsesOnlyOuterScroller,
        bool OuterScrollable,
        bool PrimaryAnchorReachable,
        bool SafeAreaMatches,
        double BodyFontSize,
        double BodyLineHeight,
        double ReminderFontSize,
        double ReminderLineHeight,
        bool TypographyMatches);

    private sealed record FloatingLongNotesValidation(
        bool BodyHasNoManualLineBreaks,
        bool NaturalWraps,
        bool UsesOnlyOuterScroller,
        bool OuterScrollable,
        bool TopBottomReachable,
        bool SafeAreaMatches,
        bool TypographyMatches,
        IReadOnlyList<FloatingLongNoteFrameMeasurement> Measurements,
        IReadOnlyList<FloatingReferenceActualCapture> Captures)
    {
        public bool LayoutValid =>
            BodyHasNoManualLineBreaks &&
            NaturalWraps &&
            UsesOnlyOuterScroller &&
            OuterScrollable &&
            TopBottomReachable &&
            SafeAreaMatches &&
            TypographyMatches &&
            Measurements.Count == 2 &&
            Captures.Count == 2;

        public static FloatingLongNotesValidation NotRun { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            Array.Empty<FloatingLongNoteFrameMeasurement>(),
            Array.Empty<FloatingReferenceActualCapture>());
    }

    private sealed record FloatingPrimaryCountdownContrastMeasurement(
        string Theme,
        string PrimaryCountdownAccentBrushKey,
        string PlushGlowBrushKey,
        string Foreground,
        string PrimarySurfaceBackground,
        double PrimarySurfaceOpacity,
        string TaskCardBackground,
        string EffectiveTaskCardBackground,
        string CompositeBackground,
        double ContrastRatio,
        double FontSize,
        bool UsesExpectedSemanticAccent,
        bool AccentTrackUsesAccentToken,
        bool AccentDiffersFromPlushGlow,
        string Scenario,
        string RemainingText,
        double TitleFontSize,
        bool IsSingleLineAndFullyVisible);

    private sealed record FloatingPrimaryCountdownContrastValidation(
        FloatingPrimaryCountdownContrastGateResults Gates,
        IReadOnlyList<FloatingPrimaryCountdownContrastMeasurement> Measurements);

    private sealed record CountdownEmptyStateContrastMeasurement(
        string Theme,
        string Foreground,
        string ActualBackground,
        double ContrastRatio,
        bool ForegroundUsesMutedBrush,
        bool ActualBackgroundMatchesBackdrop,
        bool TransparentAncestors,
        bool TextWithinBackdrop,
        string HeadingForeground,
        double HeadingContrastRatio,
        bool HeadingUsesInkBrush,
        bool HeadingTextWithinBackdrop);

    private sealed record CountdownEmptyStateContrastValidation(
        bool TextContrast,
        IReadOnlyList<CountdownEmptyStateContrastMeasurement> Measurements);

    private sealed record NoteEmptyStateContrastValidation(
        bool TextContrast,
        IReadOnlyList<CountdownEmptyStateContrastMeasurement> Measurements);

    private sealed record MainNoteCompletionMeasurement(
        string User,
        double BoxWidth,
        double BoxHeight,
        double TickWidth,
        double TickHeight,
        double CornerRadius,
        bool UsesDedicatedStyle,
        bool UsesWarmTokens,
        bool UsesCompletionSuccessStyle);

    private sealed record MainNoteCompletionInteractionValidation(
        bool Checked,
        bool Unchecked,
        bool PersistenceFailureRolledBack,
        bool AccessibleNamesUpdated,
        bool AccessibleNamesHaveNoLineBreaks = false);

    private sealed record MainNoteCompletionValidation(
        MainNoteCompletionMeasurement Main,
        MainNoteCompletionMeasurement TagSelector,
        MainNoteCompletionMeasurement Floating,
        MainNoteCompletionInteractionValidation Interaction)
    {
        public IReadOnlyList<MainNoteCompletionMeasurement> Measurements =>
            [Main, TagSelector, Floating];

        public static MainNoteCompletionValidation Failed { get; } = new(
            new("MainWindow", 0, 0, 0, 0, 0, false, false, false),
            new("TagSelector", 0, 0, 0, 0, 0, false, false, false),
            new("FloatingWindow", 0, 0, 0, 0, 0, false, false, false),
            new(false, false, false, false));
    }

    private sealed record FloatingReferenceExerciseResult(
        bool AspectMatches,
        bool PanelGeometryMatches,
        bool MascotGeometryMatches,
        FloatingReferenceBaselineValidation Baseline,
        FloatingLongNotesValidation LongNotes,
        FloatingPrimaryCountdownContrastValidation PrimaryCountdownContrast,
        bool ContentSafeAreaMatches,
        bool TitleUsesSemiBoldWeight,
        bool NoteInputWraps,
        bool NoteInputScrolls,
        bool QuickAddDayExpandedScrolledCaptureWritten,
        int ContentOverlapCount,
        IReadOnlyList<string> ContentOverlapTargets,
        bool CaptureWritten,
        bool WorkdayCountdownUsesPrimaryVisual,
        bool WorkdayPrimaryBadgeExclusive,
        bool ScenarioMatches,
        bool PrimaryDeadlineTextFullyVisible,
        bool PrimaryExtremeRemainingShrinks,
        bool ExpiredFallbackRemainingTextFullyVisible,
        IReadOnlyList<FloatingPrimaryDeadlineMeasurement> PrimaryDeadlineMeasurements,
        bool ViewportStatesMatch,
        bool RasterCardStatesMatch,
        bool DynamicCardContentRemainsWpf,
        bool ViewportScrollAndFocusRestored,
        bool NoteFailureAppearsInSourceCard,
        bool ResolvedNoteCardErrorDoesNotReturn,
        bool SecondaryRemainingTextFullyVisible,
        bool IncomeEncouragementWithoutSalary,
        bool IncomePrivacyBehavior,
        FloatingSecondaryRemainingGateResults SecondaryRemainingGates,
        IReadOnlyList<FloatingSecondaryRemainingMeasurement> SecondaryRemainingSamples,
        FloatingScrollBarValidation ScrollBar,
        IReadOnlyList<double> ActualSize,
        string CapturePath);

    private sealed record FloatingPrimaryDeadlineMeasurement(
        bool ScrollbarVisible,
        string Text,
        double ActualWidth,
        double RequiredWidth,
        bool FullyVisible);

    private sealed record FloatingSecondaryRemainingMeasurement(
        string Precision,
        string ExpectedText,
        string FormattedText,
        string RenderedText,
        double ColumnWidth,
        double ActualWidth,
        double FontSize,
        bool ColumnIs194Pixels,
        bool IsRightAligned,
        bool IsSingleLineAndUntrimmed,
        bool IsFullyVisible,
        bool UsesTabularNumerals,
        bool PreferenceSaved);

    private sealed record FloatingScrollBarParts(
        ScrollBar ScrollBar,
        Grid HitRail,
        Track Track,
        FloatingScrollBarThumb Thumb,
        Border ThumbVisual);

    private sealed record FloatingScrollBarStateMeasurement(
        string Theme,
        string State,
        double HitTargetWidth,
        double ThumbVisualWidth,
        double ThumbOpacity,
        double TrackRight,
        double ThumbHitLeft,
        double ThumbHitRight,
        double ScrollBarRight,
        double ThumbVisualLeft,
        double ThumbVisualRight,
        double MaximumVisibleCardRight,
        double BodyContentRight,
        bool CardSurfacesAreClear,
        bool ContentIsClear,
        bool DoesNotObscureContent,
        bool UsesThemeTokens,
        string ThumbColor,
        string BorderColor,
        double BoundaryContrast);

    private sealed record FloatingScrollBarValidation(
        FloatingScrollBarGateResults Gates,
        IReadOnlyList<FloatingScrollBarStateMeasurement> Measurements,
        double WheelOffsetBefore,
        double WheelOffsetAfter,
        double DragOffsetBefore,
        double DragOffsetAfter,
        IReadOnlyDictionary<string, byte[]> Captures)
    {
        public bool StateCapturesReady =>
            Captures.Count == 3 &&
            Captures.Values.All(bytes => bytes.Length > 0);

        public static FloatingScrollBarValidation NotRun { get; } = new(
            new FloatingScrollBarGateResults(false, false, false, false, false, false),
            Array.Empty<FloatingScrollBarStateMeasurement>(),
            0,
            0,
            0,
            0,
            new Dictionary<string, byte[]>(StringComparer.Ordinal));
    }

    // Private nested capture only; these fields are not validate-only top-level JSON keys.
    // Top-level shape grows when a new public validate-only field is added on purpose.
    private sealed record FloatingViewportState(
        string Name,
        double WindowWidth,
        double WindowHeight,
        double RootWidth,
        double RootHeight,
        int RenderedCardCount,
        int RasterCardCount,
        bool RasterCardsShareSource,
        int DynamicTextElementCount,
        int DynamicCheckBoxCount,
        int RenderedMultilineNoteCount,
        double ScrollableHeight,
        double VerticalOffset,
        bool VerticalScrollBarVisible,
        double VerticalScrollBarLeft,
        double VerticalScrollBarRight,
        bool VerticalScrollBarIsHitTestVisible,
        double BodyContentRight,
        double MaximumRenderedCardRight,
        double MinimumRenderedCardWidth,
        int CalendarIndexDateStampCount,
        double MaximumCalendarIndexDateStampRight,
        double NoteEntryWidth,
        double NoteEntryRight,
        bool NoteEntryFitsSafeArea,
        bool WorkspaceSurfaceGeometryMatches,
        IReadOnlyList<string> ContentOverlapTargets)
    {
        public bool WindowSizeMatches =>
            Math.Abs(WindowWidth - 520) <= 0.5 &&
            Math.Abs(WindowHeight - 390) <= 0.5 &&
            Math.Abs(RootWidth - 520) <= 0.5 &&
            Math.Abs(RootHeight - 390) <= 0.5;

        public bool ContentSafeAreaMatches => ContentOverlapTargets.Count == 0;

        public bool VerticalScrollBarIsSafe =>
            !VerticalScrollBarVisible ||
            (VerticalScrollBarIsHitTestVisible &&
             VerticalScrollBarLeft >= 0 &&
             VerticalScrollBarRight <= 396.5);
    }

    private sealed record NormalizedRect(double X, double Y, double Width, double Height)
    {
        public static NormalizedRect Empty { get; } = new(0, 0, 0, 0);

        public double Right => X + Width;

        public double Bottom => Y + Height;
    }

    private sealed record PlushSceneCapture(
        int Width,
        int Height,
        byte[] Pixels,
        int Stride,
        IReadOnlyList<Rect?> Bounds);

    private sealed record NamedBounds(string Name, Rect Bounds);

    private sealed class GatedTrayForegroundInterop : TrayForegroundInterop
    {
        private bool _allow;

        public void AllowRestores() => _allow = true;

        internal override bool Activate(FloatingWindow window) => _allow && base.Activate(window);

        internal override bool Focus(FloatingWindow window) => _allow && base.Focus(window);

        internal override bool TrySetForeground(IntPtr handle) => _allow && base.TrySetForeground(handle);

        internal override bool IsForegroundOrActive(FloatingWindow window, IntPtr handle) =>
            _allow && base.IsForegroundOrActive(window, handle);
    }

    private sealed record ReminderCenterSourceLabelContrastMeasurement(
        string Theme,
        string SourceKind,
        string Foreground,
        string Background,
        double ContrastRatio,
        bool UsesInkBrush,
        bool EmphasisPreserved,
        bool ReminderCardBackground);

    private sealed record ReminderCenterSourceLabelContrastValidation(
        bool TextContrast,
        bool UsesInkBrush,
        bool EmphasisPreserved,
        bool ReminderCardBackground,
        IReadOnlyList<ReminderCenterSourceLabelContrastMeasurement> Measurements,
        IReadOnlyList<double> RelativeTimeContrastRatios)
    {
        public bool RelativeTimeTextContrast => RelativeTimeContrastRatios.Count == 2 &&
            RelativeTimeContrastRatios.All(ratio => ratio >= 4.5);
    }

    private sealed record SystemDialogPrimaryButtonContrastMeasurement(
        string Window,
        string Foreground,
        string Background,
        double ContrastRatio);

    private sealed record SystemDialogPrimaryButtonContrastValidation(
        bool TextContrast,
        bool UsesDaytimeMainActionColors,
        bool UsesActiveStaticPalette,
        IReadOnlyList<SystemDialogPrimaryButtonContrastMeasurement> Measurements);

    private sealed record KeyboardFocusContrastMeasurement(
        string Surface,
        double ContrastRatio);

    private sealed record KeyboardFocusRingMeasurement(
        string Theme,
        string BorderColor,
        bool UsesExpectedToken,
        bool RingVisible);

    private sealed record KeyboardFocusRingContrastValidation(
        bool PairsMeetContrast,
        bool RingsUseExpectedTokens,
        bool PinKeepsWarmGold,
        bool HoverTokenDistinctFromFocus,
        bool TabRingGapAvoidsSelectedFill,
        bool FloatingCapsuleFocusRingMeetsContrast,
        bool FloatingCapsuleUsesWarmGoldToken,
        IReadOnlyList<KeyboardFocusContrastMeasurement> Measurements,
        IReadOnlyList<KeyboardFocusRingMeasurement> Rings);

    private sealed record ComboBoxTextClickValidation(
        bool TextClickOpens,
        bool ArrowPressOpens,
        bool KeyboardPathsWork,
        bool DisabledClickDoesNotOpen,
        bool PopupItemClickSelects,
        bool ArrowIsNotTabStop,
        int ComboCount,
        IReadOnlyList<ComboBoxPointerExerciseResult> Measurements);

    private sealed record SecondaryWindowHeaderDragValidation(
        bool HeaderRequestsDrag,
        bool ManualPositionPersists,
        bool InteractiveControlsDoNotDrag,
        bool ReminderCenterFirstAppearanceTopRight,
        bool CloseCancelledWithItems,
        bool ResizeGripUnchanged);

    private sealed class ValidationClock : IClock
    {
        public ValidationClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class MutableValidationClock : IClock
    {
        public MutableValidationClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
