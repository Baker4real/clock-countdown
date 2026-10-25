using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MinminCountdown;

internal static partial class ValidationReport
{
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

        var baselineBytes = File.ReadAllBytes(VisualReferenceBaseline.Main.BaselineFilePath(
            sourceRoot,
            "main-day-countdowns-empty.png"));
        var comparison = VisualReferenceBaseline.ComparePixels(baselineBytes, actualPng);
        var actualBitmap = DecodeBitmap(actualPng);
        var (pixels, stride) = GetBgra32Pixels(actualBitmap);
        int? firstX = null;
        int? firstY = null;
        byte? firstA = null;
        var holeCount = 0;
        var minA = byte.MaxValue;
        var maxA = byte.MinValue;
        for (var y = 0; y < actualBitmap.PixelHeight; y++)
        {
            for (var x = 0; x < actualBitmap.PixelWidth; x++)
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
        var withinRmse = comparison.RootMeanSquareError <= VisualReferenceBaseline.Main.MaximumRootMeanSquareError;
        var withinMae = comparison.MeanAbsoluteError <= VisualReferenceBaseline.Main.MaximumMeanAbsoluteError;
        var withinChanged = comparison.ChangedPixelCount <= VisualReferenceBaseline.Main.MaximumChangedPixelCount;
        var hashesMatch = string.Equals(actualSha, baselineSha, StringComparison.Ordinal);
        var reportPath = Path.Combine(Path.GetTempPath(), "minmin-empty-day-probe.json");
        var report = new Dictionary<string, object?>
        {
            ["emptyRender"] = emptyRender,
            ["captureDiagnostics"] = captureDiagnostics,
            ["width"] = actualBitmap.PixelWidth,
            ["height"] = actualBitmap.PixelHeight,
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
            ["rmseLimit"] = VisualReferenceBaseline.Main.MaximumRootMeanSquareError,
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
}
