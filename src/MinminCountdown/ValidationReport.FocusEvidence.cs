using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    // 焦点态实拍证据，不进门禁。参考帧捕获前会清掉键盘焦点，焦点描边从不入画，焦点相关的修改只能靠状态断言核对。
    // 这里在一次性窗口里沿两个工作区的 Tab 路径停在每个停靠点，把该处的放大裁图写进本次运行实拍证据目录的 focus\，
    // 供人工核对单层描边，不做像素断言。WPF 只在键盘是最近输入设备时创建 FocusVisualStyle 装饰层
    // （KeyboardNavigation.ShowFocusVisual），程序移动焦点不满足这一条，所以这一趟临时把最近输入设备设为键盘；
    // 装饰层画在 AdornerLayer 上，裁图因此取自整个窗口视觉，而不是窗口内容。失败只写 focus-evidence.error.txt，
    // 并在报告里记下原因，不影响其余断言。
    private static MainFocusEvidenceResult CaptureMainFocusEvidence(string assetRoot, string referenceCapturePath)
    {
        // 发布包模式不写实拍证据，参考帧路径为空，这里同样不请求。
        if (string.IsNullOrEmpty(referenceCapturePath))
        {
            return new MainFocusEvidenceResult(string.Empty, string.Empty);
        }

        var evidenceDirectory = Path.Combine(Path.GetDirectoryName(referenceCapturePath)!, "focus");
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-focus-evidence-{Guid.NewGuid():N}.settings.json");
        var inputManager = InputManager.Current;
        var previousDevice = inputManager.MostRecentInputDevice;
        var setMostRecentDevice = typeof(InputManager)
            .GetProperty(nameof(InputManager.MostRecentInputDevice))?
            .GetSetMethod(nonPublic: true);
        MainWindow? window = null;
        try
        {
            if (setMostRecentDevice is null)
            {
                throw new InvalidOperationException(
                    "WPF 未公开 InputManager.MostRecentInputDevice 的 setter，无法按键盘输入截取焦点态。");
            }

            Directory.CreateDirectory(evidenceDirectory);
            var referenceUtc = new DateTimeOffset(2026, 7, 29, 9, 20, 17, TimeSpan.FromHours(8)).ToUniversalTime();
            var store = new SettingsStore(clock: new ValidationClock(referenceUtc));
            store.Save(settingsPath, CreateMainReferenceSettings("Representative", referenceUtc));
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            using var calendar = new HolidayCalendar(assetRoot);
            var probe = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(settingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                Width = 864,
                Height = 1000,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000
            };
            window = probe;
            probe.Show();
            PumpDispatcher();
            setMostRecentDevice.Invoke(inputManager, new object?[] { Keyboard.PrimaryDevice });

            var entries = new List<object>();
            foreach (var workspaceKind in new[] { WorkspaceKind.Countdowns, WorkspaceKind.Notes })
            {
                probe.ShowWorkspace(workspaceKind);
                if (workspaceKind == WorkspaceKind.Countdowns)
                {
                    probe.ExpandCountdownEditorForValidation();
                }

                probe.Activate();
                PumpDispatcher();
                var expected = ExpectedMainWorkspaceTabPath(probe, workspaceKind);
                var captured = new HashSet<string>(StringComparer.Ordinal);
                TraverseMainWorkspaceTabPath(
                    probe,
                    expected[0],
                    expected[^1],
                    FocusNavigationDirection.Next,
                    (target, focused) =>
                    {
                        var stop = DescribeMainFocusedTarget(probe, target, focused);
                        if (captured.Add(stop))
                        {
                            entries.Add(SaveMainFocusCrop(probe, evidenceDirectory, $"{workspaceKind}-{stop}", target, focused));
                        }
                    });
            }

            var manifestPath = Path.Combine(evidenceDirectory, "focus-evidence.json");
            WriteNewBytes(
                manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schemaVersion = 1,
                        captureKind = "main-window keyboard focus crops along the Tab path; not gated",
                        entries
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            return new MainFocusEvidenceResult(manifestPath, string.Empty);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(evidenceDirectory);
            WriteCaptureFailureEvidence(evidenceDirectory, "focus-evidence", exception);
            return new MainFocusEvidenceResult(string.Empty, $"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            setMostRecentDevice?.Invoke(inputManager, new object?[] { previousDevice });
            window?.Close();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".diagnostic.jsonl");
        }
    }

    // #247：倒计时行关键状态实拍，不进门禁。一次性主窗口载入行几何探针的同一份夹具，按首选 864×1000 截取倒计时页
    // 四种排布（七行时滚动条在顶部与底部、百万天主项、到期主项）和便签页（次级动作对照），写进本次运行实拍证据目录的
    // countdown-rows\，供人工核对主阅读轴、辅助带与动作。失败只写 countdown-rows.error.txt，并在报告里记下原因，
    // 不影响其余断言。
    private static CountdownRowEvidenceResult CaptureCountdownRowStateEvidence(string assetRoot, string referenceCapturePath)
    {
        // 发布包模式不写实拍证据，参考帧路径为空，这里同样不请求。
        if (string.IsNullOrEmpty(referenceCapturePath))
        {
            return new CountdownRowEvidenceResult(string.Empty, string.Empty);
        }

        const int captureWidth = 864;
        const int captureHeight = 1000;
        var evidenceDirectory = Path.Combine(Path.GetDirectoryName(referenceCapturePath)!, "countdown-rows");
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-countdown-row-evidence-{Guid.NewGuid():N}.settings.json");
        MainWindow? window = null;
        try
        {
            Directory.CreateDirectory(evidenceDirectory);
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new ValidationClock(ValidationExercises.CountdownRowFixtureUtc)));
            var fixture = ValidationExercises.SeedCountdownRowStates(workspace);
            using var calendar = new HolidayCalendar(assetRoot);
            var probe = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(settingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                Width = captureWidth,
                Height = captureHeight,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000
            };
            window = probe;
            probe.Show();
            PumpDispatcher();
            if (probe.Content is not FrameworkElement root)
            {
                throw new InvalidOperationException("主窗口根视觉不可用于捕获。");
            }

            var entries = new List<object>();
            void Capture(string captureId, string state)
            {
                PumpDispatcher();
                ArrangeForReferenceCapture(root, captureWidth, captureHeight);
                ClearCaptureKeyboardFocus(root);
                WaitForMainReferenceMotion(probe, root);
                var path = Path.Combine(evidenceDirectory, $"{captureId}.png");
                var sha256 = WriteNewPng(path, CaptureOpaqueMainSurface(root, captureWidth, captureHeight));
                entries.Add(new { captureId, state, path, sha256 });
            }

            probe.ShowWorkspace(WorkspaceKind.Countdowns);
            Capture("countdown-rows-top", "七行，纵向滚动条在顶部：普通主项、30 字名称、五枚标签、999 天");
            probe.CountdownListRegionForValidation.ScrollToEnd();
            Capture("countdown-rows-bottom", "七行滚到底部：北京时区截止行、百万天、已到期次项");
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = [fixture.LongestTagId] });
            Capture("countdown-rows-longest-primary", "「长期」筛选：百万天主项与已到期次项，无滚动条");
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = [fixture.ExpiredTagId] });
            Capture("countdown-rows-expired-primary", "「收尾」筛选：已到期主项");
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            probe.ShowWorkspace(WorkspaceKind.Notes);
            Capture("note-row-actions", "便签行：与倒计时行同一套编辑/删除");

            var manifestPath = Path.Combine(evidenceDirectory, "countdown-rows.json");
            WriteNewBytes(
                manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schemaVersion = 1,
                        captureKind = "main-window countdown row states at 864x1000 for #247; not gated",
                        referenceUtc = ValidationExercises.CountdownRowFixtureUtc,
                        entries
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            return new CountdownRowEvidenceResult(manifestPath, string.Empty);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(evidenceDirectory);
            WriteCaptureFailureEvidence(evidenceDirectory, "countdown-rows", exception);
            return new CountdownRowEvidenceResult(string.Empty, $"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            window?.Close();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".diagnostic.jsonl");
        }
    }

    private static object SaveMainFocusCrop(
        MainWindow window,
        string evidenceDirectory,
        string captureId,
        MainTabTarget target,
        DependencyObject focused)
    {
        // 列表区的焦点落在行内控件上，裁那个控件；其余停靠点裁整个控件（日期框的焦点在打开日历按钮上，裁整个日期框）。
        var owner = target.Id is "CountdownListRegion" or "NotesListRegion" && focused is FrameworkElement focusedElement
            ? focusedElement
            : target.Element;
        var dpi = VisualTreeHelper.GetDpi(window);
        var frame = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        frame.Render(window);
        var bounds = owner.TransformToAncestor(window).TransformBounds(new Rect(owner.RenderSize));
        bounds.Inflate(8, 8);
        var left = Math.Clamp((int)Math.Floor(bounds.Left * dpi.DpiScaleX), 0, frame.PixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(bounds.Top * dpi.DpiScaleY), 0, frame.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right * dpi.DpiScaleX), left + 1, frame.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * dpi.DpiScaleY), top + 1, frame.PixelHeight);
        var crop = new CroppedBitmap(frame, new Int32Rect(left, top, right - left, bottom - top));
        // 整数倍最近邻放大，1 px 描边保持原样像素；长边约束在 1600 px 内。
        var magnification = Math.Clamp(1600 / Math.Max(crop.PixelWidth, crop.PixelHeight), 1, 4);
        var path = Path.Combine(evidenceDirectory, $"{captureId}.png");
        var sha256 = WriteNewPng(path, MagnifyNearestNeighbor(crop, magnification));
        return new
        {
            captureId,
            focusedType = focused.GetType().Name,
            keyboardIsMostRecentInputDevice = InputManager.Current.MostRecentInputDevice is KeyboardDevice,
            // 两项同时为真：按约定自己画焦点的控件又挂了装饰层，可能叠成两层描边，也可能是装饰层顶替了没点亮的模板焦点。
            templateDrawsFocus = focused is Control focusedControl && FocusVisualState.DrawsOwnKeyboardFocus(focusedControl),
            focusVisualAdorner = focused is UIElement focusedVisual &&
                AdornerLayer.GetAdornerLayer(focusedVisual)?.GetAdorners(focusedVisual)?
                    .Any(adorner => adorner.GetType().Name == "FocusVisualAdorner") == true,
            cropDip = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height },
            dpiScale = new { x = dpi.DpiScaleX, y = dpi.DpiScaleY },
            magnification,
            path,
            sha256
        };
    }

    private static BitmapSource MagnifyNearestNeighbor(BitmapSource source, int factor)
    {
        var (pixels, stride) = GetBgra32Pixels(source);
        var width = source.PixelWidth * factor;
        var height = source.PixelHeight * factor;
        var magnified = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Buffer.BlockCopy(pixels, (y / factor * stride) + (x / factor * 4), magnified, ((y * width) + x) * 4, 4);
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, magnified, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
