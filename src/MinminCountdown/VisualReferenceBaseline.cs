using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;

namespace MinminCountdown;

// 视觉基线：调用方交界面种类、模式和实拍帧，拿回门禁结论与证据。版本目录、manifest 读写、
// 源码绑定、状态指纹、像素 / alpha / ROI 度量和阈值都只在这里。
internal static class VisualReferenceBaseline
{
    // 主窗与悬浮窗共用一个版本号；编号跨分支全局分配，换号用 tools/Set-CountdownBaselineVersion.ps1。
    public const string Version = "2026-10-28-v64";

    private const string SourceRootVariable = "MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT";
    private const string SourceAggregateAlgorithm =
        "SHA-256 of UTF-8(path + NUL + uppercase file SHA-256 + LF), ordinal path order";
    private const string ExecutableLogicalPath = "app/MinminCountdown.exe";
    private const string ExecutableGeneration =
        "managed isolated dotnet publish; SHA-256 read from the executing equivalent app binary";

    internal static class Main
    {
        // empty-day 诊断探针按同一组阈值报告偏差。
        public const double MaximumMeanAbsoluteError = 0.02;
        public const double MaximumRootMeanSquareError = 0.25;
        public const int MaximumChangedPixelCount = 500;
        public const string AlphaAlgorithm = "Pbgra32 alpha scan; every pixel must equal 255";
        public const string RoiAlgorithm = "fixed full-canvas ROI for opaque 864x1000 main-window surface";
        private const int MaximumAbsoluteChannelDelta = 8;
        private const int Width = 864;
        private const int Height = 1000;
        private const string BaselineRelativeDirectory = $"assets/ui-references/main-window/864x1000/{Version}";
        private const string ManifestRelativePath = $"{BaselineRelativeDirectory}/manifest.json";
        private const string StateFingerprintAlgorithm = "SHA-256 of canonical UTF-8 main-window capture state";
        private const string PixelComparisonAlgorithm = "BGRA8 absolute channel delta over 864x1000 pixels";

        public static IReadOnlyList<MainReferenceCaptureSpec> CaptureSpecs { get; } = new MainReferenceCaptureSpec[]
        {
            new("main-day-countdowns-empty", "Day", "Countdowns", "Empty", "main-day-countdowns-empty.png"),
            new("main-day-countdowns-representative", "Day", "Countdowns", "Representative", "main-day-countdowns-representative.png"),
            new("main-day-countdowns-stress", "Day", "Countdowns", "Stress", "main-day-countdowns-stress.png"),
            new("main-day-notes-empty", "Day", "Notes", "Empty", "main-day-notes-empty.png"),
            new("main-day-notes-representative", "Day", "Notes", "Representative", "main-day-notes-representative.png"),
            new("main-day-notes-stress", "Day", "Notes", "Stress", "main-day-notes-stress.png"),
            new("main-day-notes-edit", "Day", "Notes", "Edit", "main-day-notes-edit.png"),
            new("main-day-notes-filter-open", "Day", "Notes", "FilterOpen", "main-day-notes-filter-open.png"),
            new("main-day-notes-selected", "Day", "Notes", "Selected", "main-day-notes-selected.png")
        };

        public static IReadOnlyList<string> CanonicalSourcePaths { get; } = new[]
        {
            "assets/main-decorations/main-countdown-scene-day.png",
            "assets/main-decorations/main-notes-scene-day.png",
            "src/MinminCountdown.Core/DateDisplay.cs",
            "src/MinminCountdown.Core/DesktopWorkspace.Transactions.cs",
            "src/MinminCountdown.Core/DesktopWorkspace.cs",
            "src/MinminCountdown.Core/DomainLogic.cs",
            "src/MinminCountdown.Core/NoteOrdering.cs",
            "src/MinminCountdown.Core/SettingsContract.cs",
            "src/MinminCountdown.Core/SettingsModels.cs",
            "src/MinminCountdown/App.xaml",
            "src/MinminCountdown/ApplicationTheme.cs",
            "src/MinminCountdown/DeleteUndoOffer.cs",
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

        public static VisualReferenceMode ModeFromEnvironment() =>
            VisualReferenceMode.FromEnvironment("MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE");

        public static string BaselineFilePath(string sourceRoot, string fileName) =>
            ResolveRepositoryRelativePath(sourceRoot, $"{BaselineRelativeDirectory}/{fileName}");

        public static MainReferenceActualCapture ActualCapture(
            MainReferenceCaptureSpec spec,
            DateTimeOffset beijingTime,
            byte[] pngBytes,
            CountdownSettings settings)
        {
            var metrics = Measure(pngBytes);
            if (metrics.Width != Width || metrics.Height != Height)
            {
                throw new InvalidOperationException(
                    $"主窗口参考帧 {spec.CaptureId} 不是 {Width}x{Height}（actual={metrics.Width}x{metrics.Height}）。");
            }
            if (!metrics.Alpha.IsFullyOpaque)
            {
                throw new InvalidOperationException(
                    $"主窗口参考帧 {spec.CaptureId} 包含非不透明像素。");
            }

            return new MainReferenceActualCapture(
                spec,
                beijingTime,
                pngBytes,
                CreateStateFingerprint(spec, beijingTime, settings));
        }

        public static MainReferenceBaselineValidation Run(
            VisualReferenceMode mode,
            IReadOnlyList<MainReferenceActualCapture> actualCaptures)
        {
            if (!mode.Requested)
            {
                return MainReferenceBaselineValidation.NotRequested;
            }

            var sourceRoot = Path.GetFullPath(mode.SourceRoot!);
            if (!Directory.Exists(sourceRoot))
            {
                return MainReferenceBaselineValidation.Invalid(
                    true,
                    false,
                    $"视觉源码根目录不存在：{SourceRootVariable}。");
            }

            var baselineDirectory = ResolveRepositoryRelativePath(sourceRoot, BaselineRelativeDirectory);
            var manifestPath = ResolveRepositoryRelativePath(sourceRoot, ManifestRelativePath);
            if (mode.Generate)
            {
                Generate(sourceRoot, baselineDirectory, manifestPath, actualCaptures);
            }

            return Validate(sourceRoot, baselineDirectory, manifestPath, actualCaptures);
        }

        private static MainReferenceStateFingerprint CreateStateFingerprint(
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
                StateFingerprintAlgorithm,
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

        private static MainReferenceBaselineValidation Validate(
            string sourceRoot,
            string baselineDirectory,
            string manifestPath,
            IReadOnlyList<MainReferenceActualCapture> actualCaptures)
        {
            if (!File.Exists(manifestPath))
            {
                return MainReferenceBaselineValidation.Invalid(
                    true,
                    false,
                    $"主窗口正式参考 manifest 不存在：{ManifestRelativePath}。",
                    ManifestRelativePath);
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
                        Version,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        root.GetProperty("captureKind").GetString(),
                        "actual-wpf-main-window",
                        StringComparison.Ordinal);

                var canvas = root.GetProperty("canvas");
                structureValid &= HasExactJsonProperties(canvas, "width", "height", "metricDpi") &&
                    canvas.GetProperty("width").GetInt32() == Width &&
                    canvas.GetProperty("height").GetInt32() == Height &&
                    canvas.GetProperty("metricDpi").GetInt32() == 96;
                var manifestRoi = ReadContentRoi(
                    root.GetProperty("contentRoi"),
                    ref structureValid);
                structureValid &= manifestRoi == MainReferenceContentRoi.FullCanvas;

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
                var sourceBindings = ReadSourceBindings(sourceBindingElement, ref structureValid);

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
                    .Select(element => ReadManifestCapture(element, ref structureValid))
                    .ToArray();
                structureValid &=
                    captures.Length == CaptureSpecs.Count &&
                    string.Equals(
                        aggregateAlgorithm,
                        SourceAggregateAlgorithm,
                        StringComparison.Ordinal) &&
                    string.Equals(executablePath, ExecutableLogicalPath, StringComparison.Ordinal) &&
                    string.Equals(
                        executableGeneration,
                        ExecutableGeneration,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        comparisonAlgorithm,
                        PixelComparisonAlgorithm,
                        StringComparison.Ordinal) &&
                    maximumAbsoluteChannelDelta == MaximumAbsoluteChannelDelta &&
                    Math.Abs(maximumMeanAbsoluteError - MaximumMeanAbsoluteError) < 0.0000001 &&
                    Math.Abs(maximumRootMeanSquareError - MaximumRootMeanSquareError) < 0.0000001 &&
                    maximumChangedPixelCount == MaximumChangedPixelCount;

                var expectedPaths = CaptureSpecs
                    .Select(spec => $"{BaselineRelativeDirectory}/{spec.FileName}")
                    .ToArray();
                var captureIds = captures.Select(capture => capture.CaptureId).ToArray();
                var capturePaths = captures.Select(capture => capture.Path).ToArray();
                var captureIdsUnique =
                    captureIds.Distinct(StringComparer.Ordinal).Count() == captures.Length &&
                    captureIds.Order(StringComparer.Ordinal).SequenceEqual(
                        CaptureSpecs.Select(spec => spec.CaptureId).Order(StringComparer.Ordinal),
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
                    var spec = CaptureSpecs.SingleOrDefault(candidate =>
                        string.Equals(candidate.CaptureId, capture.CaptureId, StringComparison.Ordinal));
                    return spec is not null &&
                        string.Equals(capture.Theme, spec.Theme, StringComparison.Ordinal) &&
                        string.Equals(capture.Workspace, spec.Workspace, StringComparison.Ordinal) &&
                        string.Equals(capture.State, spec.State, StringComparison.Ordinal) &&
                        string.Equals(
                            capture.Path,
                            $"{BaselineRelativeDirectory}/{spec.FileName}",
                            StringComparison.Ordinal) &&
                        string.Equals(capture.StateFingerprint.Theme, spec.Theme, StringComparison.Ordinal) &&
                        string.Equals(capture.StateFingerprint.Workspace, spec.Workspace, StringComparison.Ordinal) &&
                        string.Equals(capture.StateFingerprint.State, spec.State, StringComparison.Ordinal);
                });
                structureValid &= captureIdsUnique && capturePathsUnique &&
                    scenarioBindingsUnique && stateBindingsMatch;

                var currentSourceBindings = BindSources(sourceRoot, CanonicalSourcePaths, "主窗口");
                var currentSourceAggregate = AggregateSourceBindings(currentSourceBindings);
                var sourceHashesMatch = SourcesMatch(
                    sourceBindings,
                    currentSourceBindings,
                    manifestAggregateSha256,
                    CanonicalSourcePaths);

                var executableSha256 = CurrentExecutableSha256();
                var executableHashMatches =
                    IsUpperSha256(manifestExecutableSha256) &&
                    string.Equals(manifestExecutableSha256, executableSha256, StringComparison.Ordinal);

                var actualByCaptureId = actualCaptures.ToDictionary(
                    capture => capture.Spec.CaptureId,
                    StringComparer.Ordinal);
                var stateFingerprintsMatch = captures.All(capture =>
                    actualByCaptureId.TryGetValue(capture.CaptureId, out var actualCapture) &&
                    StateFingerprintMatchesCanonical(capture.StateFingerprint) &&
                    capture.StateFingerprint == actualCapture.StateFingerprint);
                var stateFingerprintsUnique =
                    captures.Select(capture => capture.StateFingerprint.Sha256)
                        .Distinct(StringComparer.Ordinal)
                        .Count() == captures.Length;

                var manifestFrames = captures.Select(capture => new ManifestFrame(
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
                        capture.Comparison))
                    .ToArray();
                var files = EvaluateFiles(
                    new FileRequest(
                        sourceRoot,
                        baselineDirectory,
                        expectedPaths,
                        manifestFrames,
                        actualCaptures.Select(capture => new ActualFrame(
                            capture.Spec.CaptureId,
                            capture.PngBytes)).ToArray(),
                        maximumAbsoluteChannelDelta,
                        maximumMeanAbsoluteError,
                        maximumRootMeanSquareError,
                        maximumChangedPixelCount),
                    Width,
                    Height,
                    alphaThreshold: 0);
                var filesReady =
                    files.FilesReady &&
                    files.Measurements.Count == manifestFrames.Length;
                var alphaMetricsMatch = filesReady;
                var contentRoiMatches = filesReady;
                var fullCanvas = new VisualReferenceContentRoi(0, 0, 0, Width, Height, Width * Height);
                foreach (var measurement in files.Measurements)
                {
                    var expected = manifestFrames.Single(frame =>
                        string.Equals(frame.CaptureId, measurement.CaptureId, StringComparison.Ordinal));
                    alphaMetricsMatch &=
                        AlphaMatches(expected.Alpha, measurement.BaselineAlpha) &&
                        AlphaMatches(expected.Alpha, measurement.ActualAlpha) &&
                        measurement.ActualAlpha.Minimum == byte.MaxValue &&
                        measurement.ActualAlpha.Maximum == byte.MaxValue &&
                        measurement.ActualAlpha.NonOpaquePixelCount == 0 &&
                        measurement.ActualAlpha.OpaquePixelCount == Width * Height;
                    contentRoiMatches &=
                        expected.ContentRoi == fullCanvas &&
                        measurement.BaselineContentRoi == fullCanvas &&
                        measurement.ActualContentRoi == fullCanvas;
                }

                var gates = new MainReferenceManifestGateResults(
                    structureValid && files.PathsSafe,
                    files.ExpectedCaptureFilesPresent,
                    files.NoOrphanPngFiles,
                    captureIdsUnique,
                    capturePathsUnique,
                    scenarioBindingsUnique,
                    files.CanvasDimensionsMatch,
                    sourceHashesMatch,
                    executableHashMatches,
                    files.PngHashesMatch,
                    stateFingerprintsMatch && filesReady,
                    alphaMetricsMatch,
                    contentRoiMatches,
                    files.PixelMetricsWithinThresholds,
                    stateFingerprintsUnique,
                    files.PngHashesUnique);
                var measurements = files.Measurements.Select(measurement =>
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
                            AlphaAlgorithm,
                            measurement.ActualAlpha.Minimum,
                            measurement.ActualAlpha.Maximum,
                            measurement.ActualAlpha.NonOpaquePixelCount,
                            measurement.ActualAlpha.OpaquePixelCount),
                        new MainReferenceContentRoi(
                            RoiAlgorithm,
                            measurement.ActualContentRoi.X,
                            measurement.ActualContentRoi.Y,
                            measurement.ActualContentRoi.Width,
                            measurement.ActualContentRoi.Height,
                            measurement.ActualContentRoi.PixelCount),
                        measurement.Comparison);
                }).ToArray();
                return new MainReferenceBaselineValidation(
                    true,
                    true,
                    ManifestRelativePath,
                    currentSourceAggregate,
                    executableSha256,
                    gates,
                    measurements,
                    Diagnostics(gates.AsDictionary(), files.Diagnostics));
            }
            catch (Exception exception) when (exception is IOException or JsonException or
                   InvalidOperationException or FormatException or KeyNotFoundException)
            {
                return MainReferenceBaselineValidation.Invalid(
                    true,
                    File.Exists(manifestPath),
                    exception.Message,
                    ManifestRelativePath);
            }
        }

        private static void Generate(
            string sourceRoot,
            string baselineDirectory,
            string manifestPath,
            IReadOnlyList<MainReferenceActualCapture> actualCaptures)
        {
            if (actualCaptures.Count != CaptureSpecs.Count ||
                actualCaptures.Select(capture => capture.Spec.CaptureId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != CaptureSpecs.Count)
            {
                throw new InvalidOperationException(
                    $"主窗口参考图生成必须提供严格 {CaptureSpecs.Count} 帧 inventory。");
            }
            if (Directory.Exists(baselineDirectory) &&
                Directory.EnumerateFileSystemEntries(baselineDirectory).Any())
            {
                throw new InvalidOperationException(
                    $"主窗口正式参考基线已存在，拒绝覆盖：{Version}。");
            }

            var captureMetrics = actualCaptures.ToDictionary(
                capture => capture.Spec.CaptureId,
                capture => Measure(capture.PngBytes),
                StringComparer.Ordinal);
            if (captureMetrics.Values.Any(metrics =>
                    metrics is not { Width: Width, Height: Height, Alpha.IsFullyOpaque: true }))
            {
                throw new InvalidOperationException("主窗口正式参考帧必须为 864x1000 全不透明 PNG。");
            }

            var pngHashes = actualCaptures
                .Select(capture => Sha256Bytes(capture.PngBytes))
                .ToArray();
            if (pngHashes.Distinct(StringComparer.Ordinal).Count() != pngHashes.Length)
            {
                throw new InvalidOperationException("主窗口参考图生成拒绝写入复用 PNG 的基线目录。");
            }

            Directory.CreateDirectory(baselineDirectory);
            var sourceBindings = BindSources(sourceRoot, CanonicalSourcePaths, "主窗口");
            var sourceAggregate = AggregateSourceBindings(sourceBindings);
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
                    baselineVersion = Version,
                    captureKind = "actual-wpf-main-window",
                    canvas = new { width = Width, height = Height, metricDpi = 96 },
                    contentRoi = new
                    {
                        algorithm = RoiAlgorithm,
                        x = 0,
                        y = 0,
                        width = Width,
                        height = Height,
                        pixelCount = Width * Height
                    },
                    sourceBinding = new
                    {
                        aggregateAlgorithm = SourceAggregateAlgorithm,
                        aggregateSha256 = sourceAggregate,
                        files = sourceBindings.Select(binding => new
                        {
                            path = binding.Path,
                            sha256 = binding.Sha256
                        })
                    },
                    executable = new
                    {
                        path = ExecutableLogicalPath,
                        sha256 = executableSha256,
                        generation = ExecutableGeneration
                    },
                    pixelComparison = new
                    {
                        algorithm = PixelComparisonAlgorithm,
                        maxAbsoluteChannelDelta = MaximumAbsoluteChannelDelta,
                        maximumMeanAbsoluteError = MaximumMeanAbsoluteError,
                        maximumRootMeanSquareError = MaximumRootMeanSquareError,
                        maximumChangedPixelCount = MaximumChangedPixelCount
                    },
                    captures = CaptureSpecs.Select(spec =>
                    {
                        var capture = actualCaptures.Single(actual =>
                            string.Equals(actual.Spec.CaptureId, spec.CaptureId, StringComparison.Ordinal));
                        return CreateManifestCapture(
                            capture,
                            $"{BaselineRelativeDirectory}/{spec.FileName}",
                            captureMetrics[spec.CaptureId]);
                    })
                },
                new JsonSerializerOptions { WriteIndented = true });
            foreach (var capture in actualCaptures)
            {
                var path = ResolveRepositoryRelativePath(
                    sourceRoot,
                    $"{BaselineRelativeDirectory}/{capture.Spec.FileName}");
                WriteBytesAtomically(path, capture.PngBytes);
            }
            WriteBytesAtomically(manifestPath, manifest);
        }

        private static object CreateManifestCapture(
            MainReferenceActualCapture capture,
            string path,
            MainReferenceCaptureMetrics metrics) =>
            new
            {
                captureId = capture.Spec.CaptureId,
                theme = capture.Spec.Theme,
                workspace = capture.Spec.Workspace,
                state = capture.Spec.State,
                path,
                beijingTime = capture.BeijingTime,
                width = Width,
                height = Height,
                pngSha256 = Sha256Bytes(capture.PngBytes),
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
                    algorithm = metrics.Alpha.Algorithm,
                    minimum = metrics.Alpha.Minimum,
                    maximum = metrics.Alpha.Maximum,
                    nonOpaquePixelCount = metrics.Alpha.NonOpaquePixelCount,
                    opaquePixelCount = metrics.Alpha.OpaquePixelCount
                },
                contentRoi = new
                {
                    algorithm = metrics.ContentRoi.Algorithm,
                    x = metrics.ContentRoi.X,
                    y = metrics.ContentRoi.Y,
                    width = metrics.ContentRoi.Width,
                    height = metrics.ContentRoi.Height,
                    pixelCount = metrics.ContentRoi.PixelCount
                },
                comparison = new
                {
                    maxAbsoluteChannelDelta = 0,
                    meanAbsoluteError = 0,
                    rootMeanSquareError = 0,
                    changedPixelCount = 0
                }
            };

        private static MainManifestCapture ReadManifestCapture(
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
            return new MainManifestCapture(
                element.GetProperty("captureId").GetString() ?? string.Empty,
                element.GetProperty("theme").GetString() ?? string.Empty,
                element.GetProperty("workspace").GetString() ?? string.Empty,
                element.GetProperty("state").GetString() ?? string.Empty,
                element.GetProperty("path").GetString() ?? string.Empty,
                element.GetProperty("beijingTime").GetDateTimeOffset(),
                element.GetProperty("width").GetInt32(),
                element.GetProperty("height").GetInt32(),
                element.GetProperty("pngSha256").GetString() ?? string.Empty,
                ReadStateFingerprint(
                    element.GetProperty("stateFingerprint"),
                    ref structureValid),
                ReadAlphaMetrics(element.GetProperty("alpha"), ref structureValid),
                ReadContentRoi(element.GetProperty("contentRoi"), ref structureValid),
                ReadPixelMetrics(element.GetProperty("comparison"), ref structureValid));
        }

        private static MainReferenceStateFingerprint ReadStateFingerprint(
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

        private static MainReferenceAlphaMetrics ReadAlphaMetrics(
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

        private static MainReferenceContentRoi ReadContentRoi(
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

        private static VisualReferencePixelMetrics ReadPixelMetrics(
            JsonElement element,
            ref bool structureValid)
        {
            structureValid &= HasExactJsonProperties(
                element,
                "maxAbsoluteChannelDelta",
                "meanAbsoluteError",
                "rootMeanSquareError",
                "changedPixelCount");
            return new VisualReferencePixelMetrics(
                element.GetProperty("maxAbsoluteChannelDelta").GetInt32(),
                element.GetProperty("meanAbsoluteError").GetDouble(),
                element.GetProperty("rootMeanSquareError").GetDouble(),
                element.GetProperty("changedPixelCount").GetInt32());
        }

        private static bool StateFingerprintMatchesCanonical(
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
                       StateFingerprintAlgorithm,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       Sha256Bytes(Encoding.UTF8.GetBytes(canonicalState)),
                       fingerprint.Sha256,
                       StringComparison.Ordinal);
        }

        private static MainReferenceCaptureMetrics Measure(byte[] bytes)
        {
            var metrics = MeasureCapture(bytes, alphaThreshold: 0);
            return new MainReferenceCaptureMetrics(
                metrics.Width,
                metrics.Height,
                new MainReferenceAlphaMetrics(
                    AlphaAlgorithm,
                    metrics.Alpha.Minimum,
                    metrics.Alpha.Maximum,
                    metrics.Alpha.NonOpaquePixelCount,
                    metrics.Alpha.OpaquePixelCount),
                new MainReferenceContentRoi(
                    RoiAlgorithm,
                    0,
                    0,
                    metrics.Width,
                    metrics.Height,
                    metrics.Width * metrics.Height));
        }

        private static bool AlphaMatches(
            VisualReferenceAlphaMetrics expected,
            VisualReferenceAlphaMetrics actual) =>
            expected.Minimum == actual.Minimum &&
            expected.Maximum == actual.Maximum &&
            expected.NonOpaquePixelCount == actual.NonOpaquePixelCount &&
            expected.OpaquePixelCount == actual.OpaquePixelCount;

        private sealed record MainManifestCapture(
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
            VisualReferencePixelMetrics Comparison);

        private sealed record MainReferenceCaptureMetrics(
            int Width,
            int Height,
            MainReferenceAlphaMetrics Alpha,
            MainReferenceContentRoi ContentRoi);
    }

    internal static class Floating
    {
        private const int Width = 520;
        private const int Height = 390;
        private const int ContentAlphaThreshold = 32;
        private const string BaselineRelativeDirectory = $"assets/ui-references/floating-window/520x390/{Version}";
        private const string ManifestRelativePath = $"{BaselineRelativeDirectory}/manifest.json";
        private const string StateFingerprintAlgorithm = "SHA-256 of canonical UTF-8 floating capture state";
        private const string PixelComparisonAlgorithm = "BGRA8 absolute channel delta over 520x390 pixels";
        private const string AlphaAlgorithm = "BGRA8 alpha scan";
        private const string RoiAlgorithm = "minimal bounding rectangle of pixels with alpha > 32";
        private const int MaximumAbsoluteChannelDelta = 8;
        private const double MaximumMeanAbsoluteError = 0.02;
        private const double MaximumRootMeanSquareError = 0.25;
        private const int MaximumChangedPixelCount = 500;
        private const double MinimumTopBottomMeanAbsoluteError = 0.1;
        private const double MinimumTopBottomRootMeanSquareError = 1;
        private const int MinimumTopBottomChangedPixelCount = 1_000;

        private static readonly FloatingReferenceCaptureSpec[] CaptureSpecs =
        {
            new(
                "floating-countdowns-day",
                "floating-countdowns-baseline",
                "Day",
                "Baseline",
                "floating-countdowns-day.png"),
            new(
                "floating-workday-complete-day",
                "floating-workday-complete",
                "Day",
                "Complete",
                "floating-workday-complete-day.png"),
            new(
                "long-notes-day-top",
                "long-notes",
                "Day",
                "Top",
                "long-notes-day-top.png"),
            new(
                "long-notes-day-bottom",
                "long-notes",
                "Day",
                "Bottom",
                "long-notes-day-bottom.png"),
            new(
                "notes-search-expanded-day",
                "notes-search-expanded",
                "Day",
                "Expanded",
                "notes-search-expanded-day.png")
        };

        public static IReadOnlyList<string> CanonicalSourcePaths { get; } = new[]
        {
            "assets/floating-plush/component-day-card-blank.png",
            "assets/floating-plush/component-day-header-blank.png",
            "assets/floating-plush/component-day-mascots.png",
            "assets/floating-plush/component-day-outer-shell.png",
            "assets/floating-plush/floating-plush-cinnamoroll-reference.png",
            "assets/floating-plush/floating-plush-kuromi-reference.png",
            "assets/floating-plush/floating-plush-mymelody-reference.png",
            "src/MinminCountdown.Core/DateDisplay.cs",
            "src/MinminCountdown.Core/DesktopWorkspace.Transactions.cs",
            "src/MinminCountdown.Core/DesktopWorkspace.cs",
            "src/MinminCountdown.Core/DomainLogic.cs",
            "src/MinminCountdown.Core/FloatingCountdownProjection.cs",
            "src/MinminCountdown.Core/NoteOrdering.cs",
            "src/MinminCountdown/App.xaml",
            "src/MinminCountdown/ApplicationTheme.cs",
            "src/MinminCountdown/DeleteUndoOffer.cs",
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

        public static VisualReferenceMode ModeFromEnvironment() =>
            VisualReferenceMode.FromEnvironment("MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE");

        public static string BaselineFilePath(string sourceRoot, string fileName) =>
            ResolveRepositoryRelativePath(sourceRoot, $"{BaselineRelativeDirectory}/{fileName}");

        // 主题与视口状态取自帧规格，调用方只交它在场景里实际量到的滚动与锚点。
        public static FloatingReferenceActualCapture ActualCapture(
            string captureId,
            byte[] pngBytes,
            string workspace,
            string noteId,
            string noteText,
            double verticalOffset,
            double scrollableHeight,
            string firstVisibleAnchor,
            string lastVisibleAnchor)
        {
            var spec = CaptureSpecs.Single(candidate =>
                string.Equals(candidate.CaptureId, captureId, StringComparison.Ordinal));
            return new FloatingReferenceActualCapture(
                spec,
                pngBytes,
                CreateStateFingerprint(
                    spec.Theme,
                    workspace,
                    noteId,
                    noteText,
                    spec.State,
                    verticalOffset,
                    scrollableHeight,
                    firstVisibleAnchor,
                    lastVisibleAnchor));
        }

        public static FloatingReferenceBaselineValidation Run(
            VisualReferenceMode mode,
            IReadOnlyList<FloatingReferenceActualCapture> actualCaptures,
            bool longNoteTypographyMatches,
            bool longNoteLayoutValid)
        {
            if (!mode.Requested)
            {
                return FloatingReferenceBaselineValidation.NotRequested;
            }

            var sourceRoot = Path.GetFullPath(mode.SourceRoot!);
            if (!Directory.Exists(sourceRoot))
            {
                return FloatingReferenceBaselineValidation.Invalid(
                    true,
                    false,
                    $"视觉源码根目录不存在：{SourceRootVariable}。");
            }

            var baselineDirectory = ResolveRepositoryRelativePath(sourceRoot, BaselineRelativeDirectory);
            var manifestPath = ResolveRepositoryRelativePath(sourceRoot, ManifestRelativePath);
            if (mode.Generate)
            {
                Generate(sourceRoot, baselineDirectory, manifestPath, actualCaptures);
            }

            return Validate(
                sourceRoot,
                baselineDirectory,
                manifestPath,
                actualCaptures,
                longNoteTypographyMatches,
                longNoteLayoutValid);
        }

        private static FloatingReferenceStateFingerprint CreateStateFingerprint(
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
            var noteTextSha256 = Sha256Bytes(Encoding.UTF8.GetBytes(noteText));
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
                StateFingerprintAlgorithm,
                Sha256Bytes(Encoding.UTF8.GetBytes(canonicalState)),
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

        private static FloatingReferenceBaselineValidation Validate(
            string sourceRoot,
            string baselineDirectory,
            string manifestPath,
            IReadOnlyList<FloatingReferenceActualCapture> actualCaptures,
            bool longNoteTypographyMatches,
            bool longNoteLayoutValid)
        {
            if (!File.Exists(manifestPath))
            {
                return FloatingReferenceBaselineValidation.Invalid(
                    true,
                    false,
                    $"未找到悬浮窗参考图 manifest：{ManifestRelativePath}");
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
                        Version,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        root.GetProperty("captureKind").GetString(),
                        "actual-wpf-floating-window",
                        StringComparison.Ordinal);

                var canvas = root.GetProperty("canvas");
                structureValid &=
                    HasExactJsonProperties(canvas, "width", "height", "metricDpi") &&
                    canvas.GetProperty("width").GetInt32() == Width &&
                    canvas.GetProperty("height").GetInt32() == Height &&
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
                var sourceBindings = ReadSourceBindings(sourceBindingElement, ref structureValid);

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
                    .Select(element => ReadStateDifference(element, ref structureValid))
                    .ToArray();

                var captureElements = root.GetProperty("captures").EnumerateArray().ToArray();
                var captures = captureElements.Select(element =>
                    ReadManifestCapture(element, ref structureValid)).ToArray();
                structureValid &=
                    captures.Length == CaptureSpecs.Length &&
                    string.Equals(
                        aggregateAlgorithm,
                        SourceAggregateAlgorithm,
                        StringComparison.Ordinal) &&
                    string.Equals(executableLogicalPath, ExecutableLogicalPath, StringComparison.Ordinal) &&
                    string.Equals(
                        executableGeneration,
                        ExecutableGeneration,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        comparisonAlgorithm,
                        PixelComparisonAlgorithm,
                        StringComparison.Ordinal) &&
                    maximumAbsoluteChannelDelta == MaximumAbsoluteChannelDelta &&
                    Math.Abs(maximumMeanAbsoluteError - MaximumMeanAbsoluteError) < 0.0000001 &&
                    Math.Abs(maximumRootMeanSquareError - MaximumRootMeanSquareError) < 0.0000001 &&
                    maximumChangedPixelCount == MaximumChangedPixelCount &&
                    string.Equals(
                        stateFingerprintAlgorithm,
                        StateFingerprintAlgorithm,
                        StringComparison.Ordinal) &&
                    Math.Abs(
                        minimumTopBottomMeanAbsoluteError -
                        MinimumTopBottomMeanAbsoluteError) < 0.0000001 &&
                    Math.Abs(
                        minimumTopBottomRootMeanSquareError -
                        MinimumTopBottomRootMeanSquareError) < 0.0000001 &&
                    minimumTopBottomChangedPixelCount ==
                        MinimumTopBottomChangedPixelCount &&
                    topBottomComparisons.Length == 1;

                var expectedCapturePaths = CaptureSpecs
                    .Select(spec => $"{BaselineRelativeDirectory}/{spec.FileName}")
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
                    var spec = CaptureSpecs.SingleOrDefault(candidate =>
                        string.Equals(candidate.CaptureId, capture.CaptureId, StringComparison.Ordinal));
                    return spec is not null &&
                        string.Equals(capture.Scenario, spec.Scenario, StringComparison.Ordinal) &&
                        string.Equals(capture.Theme, spec.Theme, StringComparison.Ordinal) &&
                        string.Equals(capture.State, spec.State, StringComparison.Ordinal) &&
                        string.Equals(
                            capture.Path,
                            $"{BaselineRelativeDirectory}/{spec.FileName}",
                            StringComparison.Ordinal) &&
                        string.Equals(capture.StateFingerprint.Theme, spec.Theme, StringComparison.Ordinal) &&
                        string.Equals(capture.StateFingerprint.ViewportState, spec.State, StringComparison.Ordinal);
                });
                structureValid &= expectedStateBindingsMatch;
                var captureIds = captures.Select(capture => capture.CaptureId).ToArray();
                var captureIdsUnique =
                    captureIds.Distinct(StringComparer.Ordinal).Count() == captureIds.Length &&
                    captureIds.Order(StringComparer.Ordinal).SequenceEqual(
                        CaptureSpecs
                            .Select(spec => spec.CaptureId)
                            .Order(StringComparer.Ordinal),
                        StringComparer.Ordinal);
                var scenarioThemePairsUnique =
                    captures
                        .Select(capture => $"{capture.Scenario}\0{capture.Theme}\0{capture.State}")
                        .Distinct(StringComparer.Ordinal)
                        .Count() == captures.Length;

                var currentSourceBindings = BindSources(sourceRoot, CanonicalSourcePaths, "悬浮窗");
                var currentSourceAggregate = AggregateSourceBindings(currentSourceBindings);
                var sourceHashesMatch = SourcesMatch(
                    sourceBindings,
                    currentSourceBindings,
                    manifestAggregateSha256,
                    CanonicalSourcePaths);

                var executableSha256 = CurrentExecutableSha256();
                var executableHashMatches =
                    IsUpperSha256(manifestExecutableSha256) &&
                    string.Equals(manifestExecutableSha256, executableSha256, StringComparison.Ordinal);

                var actualByCaptureId = actualCaptures.ToDictionary(
                    capture => capture.Spec.CaptureId,
                    StringComparer.Ordinal);
                var stateFingerprintsMatch = captures.All(capture =>
                    actualByCaptureId.TryGetValue(capture.CaptureId, out var actualCapture) &&
                    StateFingerprintMatchesCanonical(capture.StateFingerprint) &&
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

                    var measuredDifference = ComparePixels(
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
                            MinimumTopBottomMeanAbsoluteError &&
                        measuredDifference.RootMeanSquareError >=
                            MinimumTopBottomRootMeanSquareError &&
                        measuredDifference.ChangedPixelCount >=
                            MinimumTopBottomChangedPixelCount;
                }

                var manifestFrames = captures.Select(capture => new ManifestFrame(
                        capture.CaptureId,
                        capture.Path,
                        capture.Width,
                        capture.Height,
                        capture.PngSha256,
                        capture.Alpha,
                        capture.ContentRoi,
                        capture.Comparison))
                    .ToArray();
                var files = EvaluateFiles(
                    new FileRequest(
                        sourceRoot,
                        baselineDirectory,
                        expectedCapturePaths,
                        manifestFrames,
                        actualCaptures.Select(capture => new ActualFrame(
                            capture.Spec.CaptureId,
                            capture.PngBytes)).ToArray(),
                        maximumAbsoluteChannelDelta,
                        maximumMeanAbsoluteError,
                        maximumRootMeanSquareError,
                        maximumChangedPixelCount),
                    Width,
                    Height,
                    ContentAlphaThreshold);
                var filesReady =
                    files.FilesReady &&
                    files.Measurements.Count == manifestFrames.Length;
                var alphaMetricsMatch = filesReady;
                var contentRoiMatches = filesReady;
                foreach (var measurement in files.Measurements)
                {
                    var expected = manifestFrames.Single(frame =>
                        string.Equals(frame.CaptureId, measurement.CaptureId, StringComparison.Ordinal));
                    alphaMetricsMatch &=
                        expected.Alpha == measurement.BaselineAlpha &&
                        expected.Alpha == measurement.ActualAlpha;
                    contentRoiMatches &=
                        expected.ContentRoi == measurement.BaselineContentRoi &&
                        expected.ContentRoi == measurement.ActualContentRoi;
                }

                var gates = new FloatingReferenceManifestGateResults(
                    structureValid && files.PathsSafe,
                    files.ExpectedCaptureFilesPresent,
                    capturePathsUnique,
                    files.NoOrphanPngFiles,
                    files.CanvasDimensionsMatch,
                    scenarioThemePairsUnique,
                    sourceHashesMatch,
                    executableHashMatches,
                    files.PngHashesMatch,
                    alphaMetricsMatch,
                    contentRoiMatches,
                    files.PixelMetricsWithinThresholds);
                var freshnessGates = new FloatingReferenceFreshnessGateResults(
                    expectedStateBindingsMatch,
                    captureIdsUnique,
                    stateFingerprintsMatch && filesReady,
                    stateFingerprintsUnique,
                    files.PngHashesUnique,
                    topBottomPixelsDiffer,
                    longNoteTypographyMatches,
                    longNoteLayoutValid);
                var measurements = files.Measurements.Select(measurement =>
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
                        measurement.ActualAlpha,
                        measurement.ActualContentRoi,
                        measurement.Comparison);
                }).ToArray();
                return new FloatingReferenceBaselineValidation(
                    true,
                    true,
                    ManifestRelativePath,
                    currentSourceAggregate,
                    executableSha256,
                    gates,
                    freshnessGates,
                    measurements,
                    Diagnostics(
                        gates.AsDictionary().Concat(freshnessGates.AsDictionary()),
                        files.Diagnostics));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                return FloatingReferenceBaselineValidation.Invalid(
                    true,
                    File.Exists(manifestPath),
                    exception.Message,
                    ManifestRelativePath);
            }
        }

        private static void Generate(
            string sourceRoot,
            string baselineDirectory,
            string manifestPath,
            IReadOnlyList<FloatingReferenceActualCapture> actualCaptures)
        {
            if (actualCaptures.Count != CaptureSpecs.Length ||
                actualCaptures.Select(capture => capture.Spec.CaptureId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != CaptureSpecs.Length)
            {
                throw new InvalidOperationException(
                    $"悬浮窗参考图生成必须提供严格 {CaptureSpecs.Length} 帧 inventory。");
            }

            var pngHashes = actualCaptures
                .Select(capture => Sha256Bytes(capture.PngBytes))
                .ToArray();
            if (pngHashes.Distinct(StringComparer.Ordinal).Count() != pngHashes.Length)
            {
                throw new InvalidOperationException("悬浮窗参考图生成拒绝写入复用 PNG 的基线目录。");
            }

            if (Directory.Exists(baselineDirectory) &&
                Directory.EnumerateFileSystemEntries(baselineDirectory).Any())
            {
                throw new InvalidOperationException(
                    $"悬浮窗正式参考基线已存在，拒绝覆盖：{Version}。");
            }

            Directory.CreateDirectory(baselineDirectory);
            var captureMetrics = actualCaptures.ToDictionary(
                capture => capture.Spec.CaptureId,
                capture => MeasureCapture(capture.PngBytes, ContentAlphaThreshold),
                StringComparer.Ordinal);
            if (captureMetrics.Values.Any(metrics => metrics.Width != Width || metrics.Height != Height))
            {
                throw new InvalidOperationException("悬浮窗参考图生成帧不是 520x390。");
            }

            var sourceBindings = BindSources(sourceRoot, CanonicalSourcePaths, "悬浮窗");
            var sourceAggregate = AggregateSourceBindings(sourceBindings);
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
                    return new StateDifference(
                        theme,
                        topId,
                        bottomId,
                        ComparePixels(top.PngBytes, bottom.PngBytes));
                })
                .ToArray();
            if (stateDifferences.Any(difference =>
                    difference.Metrics.MeanAbsoluteError < MinimumTopBottomMeanAbsoluteError ||
                    difference.Metrics.RootMeanSquareError < MinimumTopBottomRootMeanSquareError ||
                    difference.Metrics.ChangedPixelCount < MinimumTopBottomChangedPixelCount))
            {
                throw new InvalidOperationException("长便签 top/bottom 帧像素差不足，拒绝生成可复用旧帧的 manifest。");
            }

            var manifest = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schemaVersion = 1,
                    baselineVersion = Version,
                    captureKind = "actual-wpf-floating-window",
                    canvas = new { width = Width, height = Height, metricDpi = 96 },
                    sourceBinding = new
                    {
                        aggregateAlgorithm = SourceAggregateAlgorithm,
                        aggregateSha256 = sourceAggregate,
                        files = sourceBindings.Select(binding => new
                        {
                            path = binding.Path,
                            sha256 = binding.Sha256
                        })
                    },
                    executable = new
                    {
                        path = ExecutableLogicalPath,
                        sha256 = executableSha256,
                        generation = ExecutableGeneration
                    },
                    pixelComparison = new
                    {
                        algorithm = PixelComparisonAlgorithm,
                        maxAbsoluteChannelDelta = MaximumAbsoluteChannelDelta,
                        maximumMeanAbsoluteError = MaximumMeanAbsoluteError,
                        maximumRootMeanSquareError = MaximumRootMeanSquareError,
                        maximumChangedPixelCount = MaximumChangedPixelCount
                    },
                    freshness = new
                    {
                        stateFingerprintAlgorithm = StateFingerprintAlgorithm,
                        minimumTopBottomMeanAbsoluteError = MinimumTopBottomMeanAbsoluteError,
                        minimumTopBottomRootMeanSquareError = MinimumTopBottomRootMeanSquareError,
                        minimumTopBottomChangedPixelCount = MinimumTopBottomChangedPixelCount,
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
                    captures = CaptureSpecs.Select(spec =>
                    {
                        var capture = actualCaptures.Single(actual =>
                            string.Equals(actual.Spec.CaptureId, spec.CaptureId, StringComparison.Ordinal));
                        return CreateManifestCapture(
                            capture,
                            $"{BaselineRelativeDirectory}/{spec.FileName}",
                            captureMetrics[spec.CaptureId]);
                    })
                },
                new JsonSerializerOptions { WriteIndented = true });
            foreach (var capture in actualCaptures)
            {
                var path = ResolveRepositoryRelativePath(
                    sourceRoot,
                    $"{BaselineRelativeDirectory}/{capture.Spec.FileName}");
                WriteBytesAtomically(path, capture.PngBytes);
            }
            WriteBytesAtomically(manifestPath, manifest);
        }

        private static object CreateManifestCapture(
            FloatingReferenceActualCapture capture,
            string path,
            CaptureMetrics metrics) =>
            new
            {
                captureId = capture.Spec.CaptureId,
                scenario = capture.Spec.Scenario,
                theme = capture.Spec.Theme,
                state = capture.Spec.State,
                path,
                width = metrics.Width,
                height = metrics.Height,
                pngSha256 = Sha256Bytes(capture.PngBytes),
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
                    algorithm = AlphaAlgorithm,
                    minimum = metrics.Alpha.Minimum,
                    maximum = metrics.Alpha.Maximum,
                    transparentPixelCount = metrics.Alpha.TransparentPixelCount,
                    nonOpaquePixelCount = metrics.Alpha.NonOpaquePixelCount,
                    opaquePixelCount = metrics.Alpha.OpaquePixelCount
                },
                contentRoi = new
                {
                    algorithm = RoiAlgorithm,
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

        private static FloatingManifestCapture ReadManifestCapture(
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
            var stateFingerprint = ReadStateFingerprint(
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
                    AlphaAlgorithm,
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
                    RoiAlgorithm,
                    StringComparison.Ordinal);
            var comparison = element.GetProperty("comparison");
            structureValid &= HasExactJsonProperties(
                comparison,
                "maxAbsoluteChannelDelta",
                "meanAbsoluteError",
                "rootMeanSquareError",
                "changedPixelCount");
            return new FloatingManifestCapture(
                element.GetProperty("captureId").GetString() ?? string.Empty,
                element.GetProperty("scenario").GetString() ?? string.Empty,
                element.GetProperty("theme").GetString() ?? string.Empty,
                element.GetProperty("state").GetString() ?? string.Empty,
                element.GetProperty("path").GetString() ?? string.Empty,
                element.GetProperty("width").GetInt32(),
                element.GetProperty("height").GetInt32(),
                element.GetProperty("pngSha256").GetString() ?? string.Empty,
                stateFingerprint,
                new VisualReferenceAlphaMetrics(
                    alpha.GetProperty("minimum").GetInt32(),
                    alpha.GetProperty("maximum").GetInt32(),
                    alpha.GetProperty("transparentPixelCount").GetInt32(),
                    alpha.GetProperty("nonOpaquePixelCount").GetInt32(),
                    alpha.GetProperty("opaquePixelCount").GetInt32()),
                new VisualReferenceContentRoi(
                    contentRoi.GetProperty("alphaThreshold").GetInt32(),
                    contentRoi.GetProperty("x").GetInt32(),
                    contentRoi.GetProperty("y").GetInt32(),
                    contentRoi.GetProperty("width").GetInt32(),
                    contentRoi.GetProperty("height").GetInt32(),
                    contentRoi.GetProperty("pixelCount").GetInt32()),
                new VisualReferencePixelMetrics(
                    comparison.GetProperty("maxAbsoluteChannelDelta").GetInt32(),
                    comparison.GetProperty("meanAbsoluteError").GetDouble(),
                    comparison.GetProperty("rootMeanSquareError").GetDouble(),
                    comparison.GetProperty("changedPixelCount").GetInt32()));
        }

        private static FloatingReferenceStateFingerprint ReadStateFingerprint(
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

        private static StateDifference ReadStateDifference(
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
            return new StateDifference(
                element.GetProperty("theme").GetString() ?? string.Empty,
                element.GetProperty("topCaptureId").GetString() ?? string.Empty,
                element.GetProperty("bottomCaptureId").GetString() ?? string.Empty,
                new VisualReferencePixelMetrics(
                    element.GetProperty("maxAbsoluteChannelDelta").GetInt32(),
                    element.GetProperty("meanAbsoluteError").GetDouble(),
                    element.GetProperty("rootMeanSquareError").GetDouble(),
                    element.GetProperty("changedPixelCount").GetInt32()));
        }

        private static bool StateFingerprintMatchesCanonical(
            FloatingReferenceStateFingerprint fingerprint)
        {
            if (!string.Equals(
                    fingerprint.Algorithm,
                    StateFingerprintAlgorithm,
                    StringComparison.Ordinal) ||
                !IsUpperSha256(fingerprint.Sha256) ||
                !IsUpperSha256(fingerprint.NoteTextSha256))
            {
                return false;
            }

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
            return string.Equals(
                Sha256Bytes(Encoding.UTF8.GetBytes(canonicalState)),
                fingerprint.Sha256,
                StringComparison.Ordinal);
        }

        private sealed record FloatingManifestCapture(
            string CaptureId,
            string Scenario,
            string Theme,
            string State,
            string Path,
            int Width,
            int Height,
            string PngSha256,
            FloatingReferenceStateFingerprint StateFingerprint,
            VisualReferenceAlphaMetrics Alpha,
            VisualReferenceContentRoi ContentRoi,
            VisualReferencePixelMetrics Comparison);

        private sealed record StateDifference(
            string Theme,
            string TopCaptureId,
            string BottomCaptureId,
            VisualReferencePixelMetrics Metrics);
    }

    // empty-day 诊断探针用同一像素比较口径对照钉住的参考帧。
    internal static VisualReferencePixelMetrics ComparePixels(byte[] baselineBytes, byte[] actualBytes)
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

    private static FileEvaluation EvaluateFiles(
        FileRequest request,
        int expectedWidth,
        int expectedHeight,
        int alphaThreshold)
    {
        var diagnostics = new List<string>();
        var pathsSafe = true;
        var resolvedCapturePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var capture in request.ManifestFrames)
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

        var actualByCaptureId = request.ActualFrames.ToDictionary(
            capture => capture.CaptureId,
            StringComparer.Ordinal);
        var canvasDimensionsMatch =
            request.ManifestFrames.Count == request.ExpectedCapturePaths.Count;
        var pngHashesMatch = expectedCaptureFilesPresent && pathsSafe;
        var pixelMetricsWithinThresholds = expectedCaptureFilesPresent && pathsSafe;
        var measurements = new List<CaptureMeasurement>();
        if (pathsSafe)
        {
            foreach (var capture in request.ManifestFrames)
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
                    measurements.Add(new CaptureMeasurement(
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
            request.ManifestFrames.Select(capture => capture.PngSha256)
                .Distinct(StringComparer.Ordinal)
                .Count() == request.ManifestFrames.Count;
        return new FileEvaluation(
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

    private static CaptureMetrics MeasureCapture(byte[] bytes, int alphaThreshold)
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
        return new CaptureMetrics(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            new VisualReferenceAlphaMetrics(minimum, maximum, transparent, nonOpaque, opaque),
            roi);
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

    private static IReadOnlyList<SourceBinding> ReadSourceBindings(
        JsonElement sourceBinding,
        ref bool structureValid)
    {
        var bindings = new List<SourceBinding>();
        foreach (var element in sourceBinding.GetProperty("files").EnumerateArray())
        {
            structureValid &= HasExactJsonProperties(element, "path", "sha256");
            bindings.Add(new SourceBinding(
                element.GetProperty("path").GetString() ?? string.Empty,
                element.GetProperty("sha256").GetString() ?? string.Empty));
        }

        return bindings;
    }

    private static IReadOnlyList<SourceBinding> BindSources(
        string sourceRoot,
        IReadOnlyList<string> canonicalPaths,
        string surface) =>
        canonicalPaths.Select(relativePath =>
        {
            var path = ResolveRepositoryRelativePath(sourceRoot, relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"{surface}参考图绑定的视觉源码不存在：{relativePath}",
                    path);
            }

            return new SourceBinding(relativePath, Sha256File(path));
        }).ToArray();

    private static bool SourcesMatch(
        IReadOnlyList<SourceBinding> manifest,
        IReadOnlyList<SourceBinding> current,
        string aggregate,
        IReadOnlyList<string> canonicalPaths) =>
        manifest.Count == current.Count &&
        manifest.Select(binding => binding.Path).SequenceEqual(canonicalPaths, StringComparer.Ordinal) &&
        manifest.Zip(current).All(pair => pair.First.Path == pair.Second.Path && IsUpperSha256(pair.First.Sha256) &&
            pair.First.Sha256 == pair.Second.Sha256) &&
        IsUpperSha256(aggregate) && aggregate == AggregateSourceBindings(manifest) &&
        aggregate == AggregateSourceBindings(current);

    private static string AggregateSourceBindings(IEnumerable<SourceBinding> bindings)
    {
        using var stream = new MemoryStream();
        foreach (var binding in bindings.OrderBy(binding => binding.Path, StringComparer.Ordinal))
        {
            var line = Encoding.UTF8.GetBytes($"{binding.Path}\0{binding.Sha256}\n");
            stream.Write(line, 0, line.Length);
        }

        return Sha256Bytes(stream.ToArray());
    }

    private static string CurrentExecutableSha256()
    {
        var processPath = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath)
            ? Sha256File(processPath)
            : string.Empty;
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

    private static void WriteBytesAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path) ??
            throw new InvalidOperationException("原子写入目标没有父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string Sha256Bytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static string Sha256File(string path) =>
        Sha256Bytes(File.ReadAllBytes(path));

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

    private sealed record SourceBinding(string Path, string Sha256);

    private sealed record FileRequest(
        string RepositoryRoot,
        string BaselineDirectory,
        IReadOnlyList<string> ExpectedCapturePaths,
        IReadOnlyList<ManifestFrame> ManifestFrames,
        IReadOnlyList<ActualFrame> ActualFrames,
        int MaximumAbsoluteChannelDelta,
        double MaximumMeanAbsoluteError,
        double MaximumRootMeanSquareError,
        int MaximumChangedPixelCount);

    private sealed record ManifestFrame(
        string CaptureId,
        string Path,
        int Width,
        int Height,
        string PngSha256,
        VisualReferenceAlphaMetrics Alpha,
        VisualReferenceContentRoi ContentRoi,
        VisualReferencePixelMetrics Comparison);

    private sealed record ActualFrame(string CaptureId, byte[] PngBytes);

    private sealed record CaptureMeasurement(
        string CaptureId,
        string Path,
        string BaselineSha256,
        string ActualSha256,
        VisualReferenceAlphaMetrics BaselineAlpha,
        VisualReferenceAlphaMetrics ActualAlpha,
        VisualReferenceContentRoi BaselineContentRoi,
        VisualReferenceContentRoi ActualContentRoi,
        VisualReferencePixelMetrics Comparison);

    private sealed record CaptureMetrics(
        int Width,
        int Height,
        VisualReferenceAlphaMetrics Alpha,
        VisualReferenceContentRoi ContentRoi);

    private sealed record FileEvaluation(
        bool PathsSafe,
        bool ExpectedCaptureFilesPresent,
        bool NoOrphanPngFiles,
        bool CanvasDimensionsMatch,
        bool PngHashesMatch,
        bool PngHashesUnique,
        bool PixelMetricsWithinThresholds,
        IReadOnlyList<CaptureMeasurement> Measurements,
        IReadOnlyList<string> Diagnostics)
    {
        public bool FilesReady =>
            PathsSafe &&
            ExpectedCaptureFilesPresent &&
            Measurements.Count > 0;
    }
}

// SourceRoot 为空表示发布包运行（不携带参考图），只报 NotRequested。
internal sealed record VisualReferenceMode(string? SourceRoot, bool Generate)
{
    public bool Requested => !string.IsNullOrWhiteSpace(SourceRoot);

    internal static VisualReferenceMode FromEnvironment(string generateVariable) => new(
        Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT"),
        string.Equals(Environment.GetEnvironmentVariable(generateVariable), "1", StringComparison.Ordinal));
}

internal sealed record MainReferenceCaptureSpec(
    string CaptureId,
    string Theme,
    string Workspace,
    string State,
    string FileName);

internal sealed record MainReferenceStateFingerprint(
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

internal sealed record MainReferenceActualCapture(
    MainReferenceCaptureSpec Spec,
    DateTimeOffset BeijingTime,
    byte[] PngBytes,
    MainReferenceStateFingerprint StateFingerprint);

internal sealed record MainReferenceAlphaMetrics(
    string Algorithm,
    int Minimum,
    int Maximum,
    int NonOpaquePixelCount,
    int OpaquePixelCount)
{
    public bool IsFullyOpaque =>
        string.Equals(Algorithm, VisualReferenceBaseline.Main.AlphaAlgorithm, StringComparison.Ordinal) &&
        Minimum == byte.MaxValue &&
        Maximum == byte.MaxValue &&
        NonOpaquePixelCount == 0 &&
        OpaquePixelCount == 864 * 1000;
}

internal sealed record MainReferenceContentRoi(
    string Algorithm,
    int X,
    int Y,
    int Width,
    int Height,
    int PixelCount)
{
    public static MainReferenceContentRoi FullCanvas { get; } = new(
        VisualReferenceBaseline.Main.RoiAlgorithm,
        0,
        0,
        864,
        1000,
        864 * 1000);
}

internal sealed record MainReferenceBaselineCaptureMeasurement(
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
    VisualReferencePixelMetrics Comparison);

internal sealed record MainReferenceBaselineValidation(
    bool Requested,
    bool ManifestAvailable,
    string ManifestPath,
    string SourceAggregateSha256,
    string ExecutableSha256,
    MainReferenceManifestGateResults Gates,
    IReadOnlyList<MainReferenceBaselineCaptureMeasurement> Captures,
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

    public static MainReferenceBaselineValidation NotRequested { get; } = new(
        false,
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        FailedGates,
        Array.Empty<MainReferenceBaselineCaptureMeasurement>(),
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
            new[] { diagnostic });
}

internal sealed record FloatingReferenceCaptureSpec(
    string CaptureId,
    string Scenario,
    string Theme,
    string State,
    string FileName);

internal sealed record FloatingReferenceStateFingerprint(
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

internal sealed record FloatingReferenceActualCapture(
    FloatingReferenceCaptureSpec Spec,
    byte[] PngBytes,
    FloatingReferenceStateFingerprint StateFingerprint);

internal sealed record FloatingReferenceBaselineCaptureMeasurement(
    string CaptureId,
    string Scenario,
    string Theme,
    string State,
    string Path,
    string BaselineSha256,
    string ActualSha256,
    FloatingReferenceStateFingerprint StateFingerprint,
    VisualReferenceAlphaMetrics Alpha,
    VisualReferenceContentRoi ContentRoi,
    VisualReferencePixelMetrics Comparison);

internal sealed record FloatingReferenceBaselineValidation(
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
