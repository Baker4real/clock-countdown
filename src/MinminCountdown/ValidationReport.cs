using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    // 必须为真的验证事实：报告字段仍输出 bool，Seal 按声明派生 Valid 与失败清单。
    private static ValidationFact Must(bool passed, string meaning) => new(passed, meaning);

    // 由场景记录汇总的事实：失败时在含义后列出记录里为假的 bool 子项（与手写的「；未通过：」同一格式），
    // 失败清单直接指出是哪一项；基线生成失败时不写报告，否则只能临时加诊断重拍。
    private static ValidationFact Must(object scenarios, bool passed, string meaning) =>
        Must(passed, passed
            ? meaning
            : meaning + "；未通过：" + string.Join('/', scenarios.GetType().GetProperties()
                .Where(property => property.PropertyType == typeof(bool) && property.GetValue(scenarios) is false)
                .Select(property => property.Name)));

    internal static object RunWindowPreflight(ResourceDictionary resources, string assetRoot)
    {
        var theme = new ApplicationTheme(resources);
        theme.Apply();
        var checks = new List<object>();
        var failures = new List<string>();
        var passed = 0;

        bool Check(string name, Func<bool> action)
        {
            try
            {
                if (!action())
                {
                    throw new InvalidOperationException($"{name} returned false.");
                }
                checks.Add(new { Name = name, Passed = true, Error = (string?)null });
                passed++;
                return true;
            }
            catch (Exception exception)
            {
                failures.Add($"{name}: {exception.Message}");
                checks.Add(new { Name = name, Passed = false, Error = exception.Message });
                return false;
            }
        }

        if (Check("MainWindowWorkAreaFit", () => ValidationExercises.RunMainWindowWorkAreaFitBehavior(assetRoot)))
        {
            Check("ConfirmationDialog", () => RunConfirmationWindowPreflight(theme, assetRoot));
        }
        return new { Passed = passed, Total = checks.Count, Failed = failures.Count, Checks = checks, Failures = failures };
    }

    private static bool RunConfirmationWindowPreflight(ApplicationTheme theme, string assetRoot)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"window-preflight-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore());
        using var calendar = new HolidayCalendar(assetRoot);
        var logger = new DiagnosticLogger(diagnosticPath);
        using var logicalWorkArea = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(new Rect(0, 0, 2560, 1600), 1, 1));
        var owner = new MainWindow(workspace, calendar, logger, assetRoot)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            Opacity = 0
        };
        // Reproduce the SSH desktop's constrained HWND on larger local desktops
        // too. The production confirmation validator still owns every verdict.
        try
        {
            using var capacity = ValidationExercises.OverrideWindowTrackingCapacityForValidation(owner, new Size(1044, 788));
            owner.Show();
            PumpDispatcher();
            owner.Hide();
            owner.Show();
            PumpDispatcher();
            owner.UpdateLayout();
            var validation = ValidateConfirmationDialog(theme, owner, DateTimeOffset.UtcNow);
            if (!validation.Themed || !validation.Gates.AllPassed)
            {
                throw new InvalidOperationException(
                    $"Confirmation dialog preflight failed: themed={validation.Themed}; " +
                    string.Join(", ", validation.Gates.FailedChecks));
            }
            if (File.Exists(settingsPath))
            {
                throw new InvalidOperationException("The confirmation fixture wrote its empty settings file.");
            }
            return true;
        }
        finally
        {
            owner.Close();
            PumpDispatcher();
            if (File.Exists(diagnosticPath)) { File.Delete(diagnosticPath); }
        }
    }

    internal static void RunEmptyDayCaptureProbe(ResourceDictionary resources, string assetRoot)
    {
        const int captureWidth = 864;
        const int captureHeight = 1000;
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
            ClearCaptureKeyboardFocus(root);
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
    // 证据统一落盘可审查。变体覆盖 864×1000 真实 DIP 画布和常态窗口（同尺寸）、
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
        var pinnedPath = Path.Combine(evidenceDirectory, "main-864x1000-countdowns-empty-pinned.png");
        WriteNewBytes(pinnedPath, pinnedCountdownsPng);
        var entries = new List<MainUiCaptureSeamEntry>
        {
            new(
                "main-864x1000-countdowns-empty-pinned",
                "design-canvas-864x1000; pinned-baseline frame saved verbatim",
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

        // 变体一：864×1000 设计坐标下的主窗口便签空态（#134 指定必交证据）。
        RunVariant("main-864x1000-notes-empty", () =>
        {
            ShowNotesWorkspace();
            var capture = CaptureOpaqueMainSurface(root, designWidth, designHeight);
            AssertCaptureOpaqueAndSized(
                capture,
                designWidth,
                designHeight,
                "main-864x1000-notes-empty");
            return RecordUiCaptureSeamEntry(
                evidenceDirectory,
                "main-864x1000-notes-empty",
                "design-canvas-864x1000; notes workspace; empty isolated fixture",
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

        // 变体三：常态窗口。XAML 声明的首选窗口就是 864×1000
        // DIP；真实布局直接在该坐标系中排列，不再经过 Viewbox。
        RunVariant("main-normal-864x1000-notes-empty", () =>
        {
            const int normalWidth = 864;
            const int normalHeight = 1000;
            ShowNotesWorkspace();
            window.Width = normalWidth;
            window.Height = normalHeight;
            PumpDispatcher();
            var capture = CaptureOpaqueMainSurface(root, normalWidth, normalHeight);
            AssertCaptureOpaqueAndSized(
                capture,
                normalWidth,
                normalHeight,
                "main-normal-864x1000-notes-empty");
            var entry = RecordUiCaptureSeamEntry(
                evidenceDirectory,
                "main-normal-864x1000-notes-empty",
                "normal-window; XAML preferred 864x1000 DIP; direct layout; notes empty",
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
                    normalWindow = new { width = 864, height = 1000 },
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
    private const double MainNoteCardReminderMinimumFontSizeDip = 12;
    private const string MainStressCountdownTitle =
        "极长倒计时标题用于验证主窗口列表不会挤压操作区或破坏暖纸层级";
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

    private static void ClearCaptureKeyboardFocus(FrameworkElement captureRoot)
    {
        // Focus(null) targets the active presentation source's root, which can
        // restore its focused child. ClearFocus keeps the target null and lets
        // normal LostKeyboardFocus/style triggers remove the actual focus ring.
        Keyboard.ClearFocus();
        PumpDispatcher();
        var focusedVisual = Descendants<UIElement>(captureRoot).Prepend(captureRoot)
            .FirstOrDefault(element => element.IsVisible &&
                (element.IsKeyboardFocused || element.IsKeyboardFocusWithin));
        if (Keyboard.FocusedElement is not null || focusedVisual is not null)
        {
            var focusedName = Keyboard.FocusedElement is FrameworkElement focused
                ? $"{focused.GetType().Name}/{focused.Name}"
                : Keyboard.FocusedElement?.GetType().Name ?? "none";
            throw new InvalidOperationException(
                $"参考帧捕获前仍有键盘焦点：root={captureRoot.Name}; focused={focusedName}; " +
                $"visibleFocus={focusedVisual?.GetType().Name ?? "none"}。");
        }
    }

    private static void WaitForMainReferenceMotion(MainWindow window, FrameworkElement root)
    {
        if (!WaitForRenderedCaptureState(root, () => window.VisibleSegmentIndicatorsSettledForCapture))
        {
            throw new InvalidOperationException(
                $"主窗口参考帧未在 300ms 内到达选中指示条终态：{window.CurrentWorkspace}。");
        }
    }

    // 逐个真实渲染帧轮询到就绪：300ms 内某一帧就绪、且泵结束时仍未超时才算到达。
    private static bool WaitForRenderedCaptureState(FrameworkElement visual, Func<bool> isReady)
    {
        const int timeoutMilliseconds = 300;
        var pumped = WindowValidationInput.PumpRenderedFrames(visual.Dispatcher, timeoutMilliseconds, isReady);
        return pumped.Completed && pumped.ElapsedMilliseconds <= timeoutMilliseconds;
    }
}
