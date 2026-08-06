using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MinminCountdown;

internal static class VisualReferenceBaseline
{
    internal static class Main
    {
        public static MainVisualReferenceEvaluation Validate(MainVisualReferenceRequest request)
        {
            var evaluation = EvaluateFiles(
                request.Files,
                request.ExpectedWidth,
                request.ExpectedHeight,
                alphaThreshold: 0);
            var filesReady =
                evaluation.FilesReady &&
                evaluation.Measurements.Count == request.Files.ManifestCaptures.Count;
            var alphaMetricsMatch = filesReady;
            var contentRoiMatches = filesReady;
            foreach (var measurement in evaluation.Measurements)
            {
                var expected = request.Files.ManifestCaptures.Single(capture =>
                    string.Equals(capture.CaptureId, measurement.CaptureId, StringComparison.Ordinal));
                var fullCanvas = new VisualReferenceContentRoi(
                    0,
                    0,
                    0,
                    request.ExpectedWidth,
                    request.ExpectedHeight,
                    request.ExpectedWidth * request.ExpectedHeight);
                alphaMetricsMatch &=
                    MainAlphaMatches(expected.Alpha, measurement.BaselineAlpha) &&
                    MainAlphaMatches(expected.Alpha, measurement.ActualAlpha) &&
                    measurement.ActualAlpha.Minimum == byte.MaxValue &&
                    measurement.ActualAlpha.Maximum == byte.MaxValue &&
                    measurement.ActualAlpha.NonOpaquePixelCount == 0 &&
                    measurement.ActualAlpha.OpaquePixelCount ==
                        request.ExpectedWidth * request.ExpectedHeight;
                contentRoiMatches &=
                    expected.ContentRoi == fullCanvas &&
                    measurement.BaselineContentRoi == fullCanvas &&
                    measurement.ActualContentRoi == fullCanvas;
            }

            var gates = new MainReferenceManifestGateResults(
                request.ManifestStructureValid && evaluation.PathsSafe,
                evaluation.ExpectedCaptureFilesPresent,
                evaluation.NoOrphanPngFiles,
                request.CaptureIdsUnique,
                request.CapturePathsUnique,
                request.ScenarioBindingsUnique,
                evaluation.CanvasDimensionsMatch,
                request.SourceHashesMatch,
                request.ExecutableHashMatches,
                evaluation.PngHashesMatch,
                request.StateFingerprintsMatch && filesReady,
                alphaMetricsMatch,
                contentRoiMatches,
                evaluation.PixelMetricsWithinThresholds,
                request.StateFingerprintsUnique,
                evaluation.PngHashesUnique);
            return new MainVisualReferenceEvaluation(
                gates,
                evaluation.Measurements,
                Diagnostics(gates.AsDictionary(), evaluation.Diagnostics));
        }
    }

    internal static class Floating
    {
        public static FloatingVisualReferenceEvaluation Validate(FloatingVisualReferenceRequest request)
        {
            var evaluation = EvaluateFiles(
                request.Files,
                request.ExpectedWidth,
                request.ExpectedHeight,
                alphaThreshold: 32);
            var filesReady =
                evaluation.FilesReady &&
                evaluation.Measurements.Count == request.Files.ManifestCaptures.Count;
            var alphaMetricsMatch = filesReady;
            var contentRoiMatches = filesReady;
            foreach (var measurement in evaluation.Measurements)
            {
                var expected = request.Files.ManifestCaptures.Single(capture =>
                    string.Equals(capture.CaptureId, measurement.CaptureId, StringComparison.Ordinal));
                alphaMetricsMatch &=
                    expected.Alpha == measurement.BaselineAlpha &&
                    expected.Alpha == measurement.ActualAlpha;
                contentRoiMatches &=
                    expected.ContentRoi == measurement.BaselineContentRoi &&
                    expected.ContentRoi == measurement.ActualContentRoi;
            }

            var gates = new FloatingReferenceManifestGateResults(
                request.ManifestStructureValid && evaluation.PathsSafe,
                evaluation.ExpectedCaptureFilesPresent,
                request.CapturePathsUnique,
                evaluation.NoOrphanPngFiles,
                evaluation.CanvasDimensionsMatch,
                request.ScenarioThemePairsUnique,
                request.SourceHashesMatch,
                request.ExecutableHashMatches,
                evaluation.PngHashesMatch,
                alphaMetricsMatch,
                contentRoiMatches,
                evaluation.PixelMetricsWithinThresholds);
            var freshnessGates = new FloatingReferenceFreshnessGateResults(
                request.ExpectedStateBindingsMatch,
                request.CaptureIdsUnique,
                request.StateFingerprintsMatch && filesReady,
                request.StateFingerprintsUnique,
                evaluation.PngHashesUnique,
                request.TopBottomPixelsDiffer,
                request.LongNoteTypographyMatches,
                request.LongNoteLayoutValid);
            return new FloatingVisualReferenceEvaluation(
                gates,
                freshnessGates,
                evaluation.Measurements,
                Diagnostics(
                    gates.AsDictionary().Concat(freshnessGates.AsDictionary()),
                    evaluation.Diagnostics));
        }
    }

    private static VisualReferenceFileEvaluation EvaluateFiles(
        VisualReferenceFileRequest request,
        int expectedWidth,
        int expectedHeight,
        int alphaThreshold)
    {
        var diagnostics = new List<string>();
        var pathsSafe = true;
        var resolvedCapturePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var capture in request.ManifestCaptures)
        {
            try
            {
                resolvedCapturePaths[capture.CaptureId] = ResolveRepositoryRelativePath(
                    request.RepositoryRoot,
                    capture.Path);
            }
            catch (InvalidOperationException exception)
            {
                pathsSafe = false;
                diagnostics.Add(exception.Message);
            }
        }

        var expectedCaptureFilesPresent =
            pathsSafe &&
            request.ExpectedCapturePaths.All(relativePath =>
            {
                try
                {
                    return File.Exists(ResolveRepositoryRelativePath(request.RepositoryRoot, relativePath));
                }
                catch (InvalidOperationException exception)
                {
                    pathsSafe = false;
                    diagnostics.Add(exception.Message);
                    return false;
                }
            });
        var pngInventory = Directory.Exists(request.BaselineDirectory)
            ? Directory.GetFiles(request.BaselineDirectory, "*.png", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();
        var expectedInventory = request.ExpectedCapturePaths
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var noOrphanPngFiles = pngInventory.SequenceEqual(expectedInventory, StringComparer.Ordinal);

        var actualByCaptureId = request.ActualCaptures.ToDictionary(
            capture => capture.CaptureId,
            StringComparer.Ordinal);
        var canvasDimensionsMatch =
            request.ManifestCaptures.Count == request.ExpectedCapturePaths.Count;
        var pngHashesMatch = expectedCaptureFilesPresent && pathsSafe;
        var pixelMetricsWithinThresholds = expectedCaptureFilesPresent && pathsSafe;
        var measurements = new List<VisualReferenceCaptureMeasurement>();
        if (pathsSafe)
        {
            foreach (var capture in request.ManifestCaptures)
            {
                if (!resolvedCapturePaths.TryGetValue(capture.CaptureId, out var baselinePath) ||
                    !request.ExpectedCapturePaths.Contains(capture.Path, StringComparer.Ordinal) ||
                    !actualByCaptureId.TryGetValue(capture.CaptureId, out var actualCapture) ||
                    !File.Exists(baselinePath))
                {
                    canvasDimensionsMatch = false;
                    pngHashesMatch = false;
                    pixelMetricsWithinThresholds = false;
                    continue;
                }

                try
                {
                    var baselineBytes = File.ReadAllBytes(baselinePath);
                    var baselineMetrics = MeasureCapture(baselineBytes, alphaThreshold);
                    var actualMetrics = MeasureCapture(actualCapture.PngBytes, alphaThreshold);
                    var comparison = ComparePixels(baselineBytes, actualCapture.PngBytes);
                    var baselineSha256 = Sha256Bytes(baselineBytes);
                    var actualSha256 = Sha256Bytes(actualCapture.PngBytes);
                    canvasDimensionsMatch &=
                        capture.Width == expectedWidth &&
                        capture.Height == expectedHeight &&
                        baselineMetrics.Width == expectedWidth &&
                        baselineMetrics.Height == expectedHeight &&
                        actualMetrics.Width == expectedWidth &&
                        actualMetrics.Height == expectedHeight;
                    pngHashesMatch &=
                        IsUpperSha256(capture.PngSha256) &&
                        string.Equals(capture.PngSha256, baselineSha256, StringComparison.Ordinal) &&
                        string.Equals(capture.PngSha256, actualSha256, StringComparison.Ordinal);
                    pixelMetricsWithinThresholds &=
                        capture.Comparison == VisualReferencePixelMetrics.Zero &&
                        comparison.MaxAbsoluteChannelDelta <= request.MaximumAbsoluteChannelDelta &&
                        comparison.MeanAbsoluteError <= request.MaximumMeanAbsoluteError &&
                        comparison.RootMeanSquareError <= request.MaximumRootMeanSquareError &&
                        comparison.ChangedPixelCount <= request.MaximumChangedPixelCount;
                    measurements.Add(new VisualReferenceCaptureMeasurement(
                        capture.CaptureId,
                        capture.Path,
                        baselineSha256,
                        actualSha256,
                        baselineMetrics.Alpha,
                        actualMetrics.Alpha,
                        baselineMetrics.ContentRoi,
                        actualMetrics.ContentRoi,
                        comparison));
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or
                       NotSupportedException or FileFormatException)
                {
                    canvasDimensionsMatch = false;
                    pngHashesMatch = false;
                    pixelMetricsWithinThresholds = false;
                    diagnostics.Add($"参考 PNG {capture.Path} 无法验证：{exception.Message}");
                }
            }
        }

        var pngHashesUnique =
            request.ManifestCaptures.Select(capture => capture.PngSha256)
                .Distinct(StringComparer.Ordinal)
                .Count() == request.ManifestCaptures.Count;
        return new VisualReferenceFileEvaluation(
            pathsSafe,
            expectedCaptureFilesPresent,
            noOrphanPngFiles,
            canvasDimensionsMatch,
            pngHashesMatch,
            pngHashesUnique,
            pixelMetricsWithinThresholds,
            measurements,
            diagnostics);
    }

    private static VisualReferenceCaptureMetrics MeasureCapture(byte[] bytes, int alphaThreshold)
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
                if (alpha <= alphaThreshold)
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
            ? new VisualReferenceContentRoi(alphaThreshold, 0, 0, 0, 0, 0)
            : new VisualReferenceContentRoi(
                alphaThreshold,
                minX,
                minY,
                maxX - minX + 1,
                maxY - minY + 1,
                contentPixels);
        return new VisualReferenceCaptureMetrics(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            new VisualReferenceAlphaMetrics(minimum, maximum, transparent, nonOpaque, opaque),
            roi);
    }

    private static VisualReferencePixelMetrics ComparePixels(byte[] baselineBytes, byte[] actualBytes)
    {
        var baseline = DecodeBitmap(baselineBytes);
        var actual = DecodeBitmap(actualBytes);
        if (baseline.PixelWidth != actual.PixelWidth || baseline.PixelHeight != actual.PixelHeight)
        {
            return VisualReferencePixelMetrics.Invalid;
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

        return new VisualReferencePixelMetrics(
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

    private static string ResolveRepositoryRelativePath(string repositoryRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Contains('\\') ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"参考图路径不是可移植仓库相对路径：{relativePath}");
        }

        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) +
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

    private static string Sha256Bytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static bool MainAlphaMatches(
        VisualReferenceAlphaMetrics expected,
        VisualReferenceAlphaMetrics actual) =>
        expected.Minimum == actual.Minimum &&
        expected.Maximum == actual.Maximum &&
        expected.NonOpaquePixelCount == actual.NonOpaquePixelCount &&
        expected.OpaquePixelCount == actual.OpaquePixelCount;

    private static bool IsUpperSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static IReadOnlyList<string> Diagnostics(
        IEnumerable<KeyValuePair<string, bool>> gates,
        IReadOnlyList<string> fileDiagnostics) =>
        gates.Where(pair => !pair.Value)
            .Select(pair => pair.Key)
            .Concat(fileDiagnostics)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

internal sealed record VisualReferenceFileRequest(
    string RepositoryRoot,
    string BaselineDirectory,
    IReadOnlyList<string> ExpectedCapturePaths,
    IReadOnlyList<VisualReferenceManifestCapture> ManifestCaptures,
    IReadOnlyList<VisualReferenceActualCapture> ActualCaptures,
    int MaximumAbsoluteChannelDelta,
    double MaximumMeanAbsoluteError,
    double MaximumRootMeanSquareError,
    int MaximumChangedPixelCount);

internal sealed record MainVisualReferenceRequest(
    VisualReferenceFileRequest Files,
    int ExpectedWidth,
    int ExpectedHeight,
    bool ManifestStructureValid,
    bool CaptureIdsUnique,
    bool CapturePathsUnique,
    bool ScenarioBindingsUnique,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool StateFingerprintsMatch,
    bool StateFingerprintsUnique);

internal sealed record FloatingVisualReferenceRequest(
    VisualReferenceFileRequest Files,
    int ExpectedWidth,
    int ExpectedHeight,
    bool ManifestStructureValid,
    bool CapturePathsUnique,
    bool ScenarioThemePairsUnique,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool ExpectedStateBindingsMatch,
    bool CaptureIdsUnique,
    bool StateFingerprintsMatch,
    bool StateFingerprintsUnique,
    bool TopBottomPixelsDiffer,
    bool LongNoteTypographyMatches,
    bool LongNoteLayoutValid);

internal sealed record VisualReferenceManifestCapture(
    string CaptureId,
    string Path,
    int Width,
    int Height,
    string PngSha256,
    VisualReferenceAlphaMetrics Alpha,
    VisualReferenceContentRoi ContentRoi,
    VisualReferencePixelMetrics Comparison);

internal sealed record VisualReferenceActualCapture(string CaptureId, byte[] PngBytes);

internal sealed record VisualReferenceAlphaMetrics(
    int Minimum,
    int Maximum,
    int TransparentPixelCount,
    int NonOpaquePixelCount,
    int OpaquePixelCount);

internal sealed record VisualReferenceContentRoi(
    int AlphaThreshold,
    int X,
    int Y,
    int Width,
    int Height,
    int PixelCount);

internal sealed record VisualReferencePixelMetrics(
    int MaxAbsoluteChannelDelta,
    double MeanAbsoluteError,
    double RootMeanSquareError,
    int ChangedPixelCount)
{
    public static VisualReferencePixelMetrics Zero { get; } = new(0, 0, 0, 0);
    public static VisualReferencePixelMetrics Invalid { get; } =
        new(int.MaxValue, double.MaxValue, double.MaxValue, int.MaxValue);
}

internal sealed record VisualReferenceCaptureMeasurement(
    string CaptureId,
    string Path,
    string BaselineSha256,
    string ActualSha256,
    VisualReferenceAlphaMetrics BaselineAlpha,
    VisualReferenceAlphaMetrics ActualAlpha,
    VisualReferenceContentRoi BaselineContentRoi,
    VisualReferenceContentRoi ActualContentRoi,
    VisualReferencePixelMetrics Comparison);

internal sealed record MainVisualReferenceEvaluation(
    MainReferenceManifestGateResults Gates,
    IReadOnlyList<VisualReferenceCaptureMeasurement> Measurements,
    IReadOnlyList<string> Diagnostics)
{
    public bool AllPassed => Gates.AllPassed;
}

internal sealed record FloatingVisualReferenceEvaluation(
    FloatingReferenceManifestGateResults Gates,
    FloatingReferenceFreshnessGateResults FreshnessGates,
    IReadOnlyList<VisualReferenceCaptureMeasurement> Measurements,
    IReadOnlyList<string> Diagnostics)
{
    public bool AllPassed => Gates.AllPassed && FreshnessGates.AllPassed;
}

internal sealed record VisualReferenceCaptureMetrics(
    int Width,
    int Height,
    VisualReferenceAlphaMetrics Alpha,
    VisualReferenceContentRoi ContentRoi);

internal sealed record VisualReferenceFileEvaluation(
    bool PathsSafe,
    bool ExpectedCaptureFilesPresent,
    bool NoOrphanPngFiles,
    bool CanvasDimensionsMatch,
    bool PngHashesMatch,
    bool PngHashesUnique,
    bool PixelMetricsWithinThresholds,
    IReadOnlyList<VisualReferenceCaptureMeasurement> Measurements,
    IReadOnlyList<string> Diagnostics)
{
    public bool FilesReady =>
        PathsSafe &&
        ExpectedCaptureFilesPresent &&
        Measurements.Count > 0;
}
