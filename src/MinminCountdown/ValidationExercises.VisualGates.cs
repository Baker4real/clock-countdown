using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    public static FloatingReferenceManifestGateResults EvaluateFloatingReferenceManifestGates(
        FloatingReferenceManifestGateInputs input) =>
        new(
            input.ManifestStructureValid,
            input.ExpectedCaptureFilesPresent,
            input.CapturePathsUnique,
            input.NoOrphanPngFiles,
            input.CanvasDimensionsMatch,
            input.ScenarioThemePairsUnique,
            input.SourceHashesMatch,
            input.ExecutableHashMatches,
            input.PngHashesMatch,
            input.AlphaMetricsMatch,
            input.ContentRoiMatches,
            input.PixelMetricsWithinThresholds);

    public static bool RunFloatingReferenceManifestGateIsolationBehavior()
    {
        var passing = new FloatingReferenceManifestGateInputs(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        Must(
            EvaluateFloatingReferenceManifestGates(passing).AllPassed,
            "悬浮窗参考图 manifest 全通过夹具被错误拒绝。");

        var fixtures = new (string Name, FloatingReferenceManifestGateInputs Input, string Gate)[]
        {
            ("malformed-manifest", passing with { ManifestStructureValid = false },
                nameof(FloatingReferenceManifestGateResults.ManifestStructureValid)),
            ("missing-png", passing with { ExpectedCaptureFilesPresent = false },
                nameof(FloatingReferenceManifestGateResults.ExpectedCaptureFilesPresent)),
            ("duplicate-path", passing with { CapturePathsUnique = false },
                nameof(FloatingReferenceManifestGateResults.CapturePathsUnique)),
            ("orphan-png", passing with { NoOrphanPngFiles = false },
                nameof(FloatingReferenceManifestGateResults.NoOrphanPngFiles)),
            ("wrong-dimensions", passing with { CanvasDimensionsMatch = false },
                nameof(FloatingReferenceManifestGateResults.CanvasDimensionsMatch)),
            ("duplicate-scenario-theme", passing with { ScenarioThemePairsUnique = false },
                nameof(FloatingReferenceManifestGateResults.ScenarioThemePairsUnique)),
            ("source-hash-mismatch", passing with { SourceHashesMatch = false },
                nameof(FloatingReferenceManifestGateResults.SourceHashesMatch)),
            ("executable-hash-mismatch", passing with { ExecutableHashMatches = false },
                nameof(FloatingReferenceManifestGateResults.ExecutableHashMatches)),
            ("png-hash-mismatch", passing with { PngHashesMatch = false },
                nameof(FloatingReferenceManifestGateResults.PngHashesMatch)),
            ("alpha-metrics-mismatch", passing with { AlphaMetricsMatch = false },
                nameof(FloatingReferenceManifestGateResults.AlphaMetricsMatch)),
            ("content-roi-mismatch", passing with { ContentRoiMatches = false },
                nameof(FloatingReferenceManifestGateResults.ContentRoiMatches)),
            ("pixel-metrics-mismatch", passing with { PixelMetricsWithinThresholds = false },
                nameof(FloatingReferenceManifestGateResults.PixelMetricsWithinThresholds))
        };
        foreach (var fixture in fixtures)
        {
            VerifyOnlyFloatingReferenceManifestGateFails(
                fixture.Name,
                fixture.Input,
                fixture.Gate);
        }

        return true;
    }

    public static FloatingReferenceFreshnessGateResults EvaluateFloatingReferenceFreshnessGates(
        FloatingReferenceFreshnessGateInputs input) =>
        new(
            input.ExpectedStateBindingsMatch,
            input.CaptureIdsUnique,
            input.StateFingerprintsMatch,
            input.StateFingerprintsUnique,
            input.PngHashesUnique,
            input.TopBottomPixelsDiffer,
            input.LongNoteTypographyMatches,
            input.LongNoteLayoutValid);

    public static bool RunFloatingReferenceFreshnessGateIsolationBehavior()
    {
        var passing = new FloatingReferenceFreshnessGateInputs(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        Must(
            EvaluateFloatingReferenceFreshnessGates(passing).AllPassed,
            "悬浮窗长便签帧新鲜度全通过夹具被错误拒绝。");

        var fixtures = new (string Name, FloatingReferenceFreshnessGateInputs Input, string Gate)[]
        {
            ("swapped-top-bottom", passing with { ExpectedStateBindingsMatch = false },
                nameof(FloatingReferenceFreshnessGateResults.ExpectedStateBindingsMatch)),
            ("duplicate-capture-id", passing with { CaptureIdsUnique = false },
                nameof(FloatingReferenceFreshnessGateResults.CaptureIdsUnique)),
            ("state-fingerprint-mismatch", passing with { StateFingerprintsMatch = false },
                nameof(FloatingReferenceFreshnessGateResults.StateFingerprintsMatch)),
            ("duplicate-state-fingerprint", passing with { StateFingerprintsUnique = false },
                nameof(FloatingReferenceFreshnessGateResults.StateFingerprintsUnique)),
            ("reused-frame", passing with { PngHashesUnique = false },
                nameof(FloatingReferenceFreshnessGateResults.PngHashesUnique)),
            ("top-bottom-pixel-difference-too-small", passing with { TopBottomPixelsDiffer = false },
                nameof(FloatingReferenceFreshnessGateResults.TopBottomPixelsDiffer)),
            ("long-note-typography-mismatch", passing with { LongNoteTypographyMatches = false },
                nameof(FloatingReferenceFreshnessGateResults.LongNoteTypographyMatches)),
            ("long-note-layout-unreachable", passing with { LongNoteLayoutValid = false },
                nameof(FloatingReferenceFreshnessGateResults.LongNoteLayoutValid))
        };
        foreach (var fixture in fixtures)
        {
            var results = EvaluateFloatingReferenceFreshnessGates(fixture.Input).AsDictionary();
            Must(
                results.TryGetValue(fixture.Gate, out var failed) && !failed,
                $"悬浮窗长便签负向夹具 {fixture.Name} 未让 {fixture.Gate} 变红。");
            Must(
                results
                    .Where(pair => !string.Equals(pair.Key, fixture.Gate, StringComparison.Ordinal))
                    .All(pair => pair.Value),
                $"悬浮窗长便签负向夹具 {fixture.Name} 误伤无关契约。");
        }

        return true;
    }

    public static MainReferenceManifestGateResults EvaluateMainReferenceManifestGates(
        MainReferenceManifestGateInputs input) =>
        new(
            input.ManifestStructureValid,
            input.ExpectedCaptureFilesPresent,
            input.NoOrphanPngFiles,
            input.CaptureIdsUnique,
            input.CapturePathsUnique,
            input.ScenarioBindingsUnique,
            input.CanvasDimensionsMatch,
            input.SourceHashesMatch,
            input.ExecutableHashMatches,
            input.PngHashesMatch,
            input.StateFingerprintsMatch,
            input.AlphaMetricsMatch,
            input.ContentRoiMatches,
            input.PixelMetricsWithinThresholds,
            input.StateFingerprintsUnique,
            input.PngHashesUnique);

    public static bool RunMainReferenceManifestGateIsolationBehavior()
    {
        var passing = new MainReferenceManifestGateInputs(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        Must(
            EvaluateMainReferenceManifestGates(passing).AllPassed,
            "主窗口参考图 manifest 全通过夹具被错误拒绝。");

        var fixtures = new (string Name, MainReferenceManifestGateInputs Input, string Gate)[]
        {
            ("malformed-manifest", passing with { ManifestStructureValid = false },
                nameof(MainReferenceManifestGateResults.ManifestStructureValid)),
            ("missing-png", passing with { ExpectedCaptureFilesPresent = false },
                nameof(MainReferenceManifestGateResults.ExpectedCaptureFilesPresent)),
            ("orphan-png", passing with { NoOrphanPngFiles = false },
                nameof(MainReferenceManifestGateResults.NoOrphanPngFiles)),
            ("duplicate-capture-id", passing with { CaptureIdsUnique = false },
                nameof(MainReferenceManifestGateResults.CaptureIdsUnique)),
            ("duplicate-path", passing with { CapturePathsUnique = false },
                nameof(MainReferenceManifestGateResults.CapturePathsUnique)),
            ("duplicate-scenario", passing with { ScenarioBindingsUnique = false },
                nameof(MainReferenceManifestGateResults.ScenarioBindingsUnique)),
            ("wrong-dimensions", passing with { CanvasDimensionsMatch = false },
                nameof(MainReferenceManifestGateResults.CanvasDimensionsMatch)),
            ("source-hash-mismatch", passing with { SourceHashesMatch = false },
                nameof(MainReferenceManifestGateResults.SourceHashesMatch)),
            ("executable-hash-mismatch", passing with { ExecutableHashMatches = false },
                nameof(MainReferenceManifestGateResults.ExecutableHashMatches)),
            ("png-hash-mismatch", passing with { PngHashesMatch = false },
                nameof(MainReferenceManifestGateResults.PngHashesMatch)),
            ("state-fingerprint-mismatch", passing with { StateFingerprintsMatch = false },
                nameof(MainReferenceManifestGateResults.StateFingerprintsMatch)),
            ("alpha-metrics-mismatch", passing with { AlphaMetricsMatch = false },
                nameof(MainReferenceManifestGateResults.AlphaMetricsMatch)),
            ("content-roi-mismatch", passing with { ContentRoiMatches = false },
                nameof(MainReferenceManifestGateResults.ContentRoiMatches)),
            ("pixel-metrics-mismatch", passing with { PixelMetricsWithinThresholds = false },
                nameof(MainReferenceManifestGateResults.PixelMetricsWithinThresholds)),
            ("duplicate-state-fingerprint", passing with { StateFingerprintsUnique = false },
                nameof(MainReferenceManifestGateResults.StateFingerprintsUnique)),
            ("reused-frame", passing with { PngHashesUnique = false },
                nameof(MainReferenceManifestGateResults.PngHashesUnique))
        };
        foreach (var fixture in fixtures)
        {
            var results = EvaluateMainReferenceManifestGates(fixture.Input).AsDictionary();
            Must(
                results.TryGetValue(fixture.Gate, out var failed) && !failed,
                $"主窗口参考图负向夹具 {fixture.Name} 未让 {fixture.Gate} 变红。");
            Must(
                results
                    .Where(pair => !string.Equals(pair.Key, fixture.Gate, StringComparison.Ordinal))
                    .All(pair => pair.Value),
                $"主窗口参考图负向夹具 {fixture.Name} 误伤无关契约。");
        }

        return true;
    }

    public static bool RunVisualReferenceBaselineInterfaceBehavior()
    {
        // 走调用方同一交界面：临时源码树 + 合成帧先生成再校验，再逐项篡改，看对应门禁变红。
        var sourceRoot = Path.Combine(
            Path.GetTempPath(),
            "MinminCountdown.VisualReferenceBaseline",
            Guid.NewGuid().ToString("N"));
        try
        {
            var interactionSources = new[]
            {
                "src/MinminCountdown/MotionBehavior.cs",
                "src/MinminCountdown.Core/DateDisplay.cs",
                "src/MinminCountdown.Core/NoteOrdering.cs",
                "src/MinminCountdown.Core/DesktopWorkspace.cs",
                "src/MinminCountdown.Core/DesktopWorkspace.Transactions.cs",
                "src/MinminCountdown/NoteCompletionExit.cs",
                "src/MinminCountdown/DeleteUndoOffer.cs"
            };
            Must(
                interactionSources
                    .Append("src/MinminCountdown/SegmentIndicator.cs")
                    .All(path => VisualReferenceBaseline.Main.CanonicalSourcePaths.Contains(path, StringComparer.Ordinal)) &&
                interactionSources
                    .Append("src/MinminCountdown/FloatingBottomStatus.cs")
                    .Append("src/MinminCountdown/HolidayCalendar.cs")
                    .Append("src/MinminCountdown.Core/FloatingCountdownProjection.cs")
                    .All(path => VisualReferenceBaseline.Floating.CanonicalSourcePaths.Contains(path, StringComparer.Ordinal)),
                "视觉基线源码绑定漏掉了交互行为源码。");
            foreach (var relativePath in VisualReferenceBaseline.Main.CanonicalSourcePaths
                         .Concat(VisualReferenceBaseline.Floating.CanonicalSourcePaths)
                         .Distinct(StringComparer.Ordinal))
            {
                WriteVisualReferenceFixtureSource(sourceRoot, relativePath, string.Empty);
            }

            var generate = new VisualReferenceMode(sourceRoot, Generate: true);
            var validate = new VisualReferenceMode(sourceRoot, Generate: false);

            var beijingTime = new DateTimeOffset(2032, 5, 18, 10, 15, 30, TimeSpan.FromHours(8));
            var mainFrames = VisualReferenceBaseline.Main.CaptureSpecs
                .Select((spec, index) => VisualReferenceBaseline.Main.ActualCapture(
                    spec,
                    beijingTime,
                    CreateSolidVisualReferenceFixturePng(864, 1000, (byte)(40 + (index * 20))),
                    CountdownSettings.Empty))
                .ToArray();
            var mainResult = VisualReferenceBaseline.Main.Run(generate, mainFrames);
            Must(
                mainResult.AllPassed && mainResult.Diagnostics.Count == 0,
                "主窗口视觉基线生成后未通过同一交界面的校验。");
            Must(
                ThrowsInvalidOperation(() => VisualReferenceBaseline.Main.Run(generate, mainFrames)),
                "主窗口视觉基线未拒绝覆盖已有版本目录。");
            var nonOpaqueFrame = CreateSolidVisualReferenceFixturePng(864, 1000, 40, firstPixelAlpha: 0);
            Must(
                ThrowsInvalidOperation(() => VisualReferenceBaseline.Main.ActualCapture(
                    VisualReferenceBaseline.Main.CaptureSpecs[0],
                    beijingTime,
                    nonOpaqueFrame,
                    CountdownSettings.Empty)),
                "主窗口视觉基线未拒绝含非不透明像素的实拍帧。");
            WriteVisualReferenceFixtureSource(sourceRoot, "src/MinminCountdown/MotionBehavior.cs", "// drift");
            Must(
                VisualReferenceBaseline.Main.Run(validate, mainFrames).Diagnostics
                    .SequenceEqual(new[] { "SourceHashesMatch" }, StringComparer.Ordinal),
                "主窗口视觉基线未只以源码哈希门禁拒绝改动过的绑定源码。");
            WriteVisualReferenceFixtureSource(sourceRoot, "src/MinminCountdown/MotionBehavior.cs", string.Empty);

            // ROI 帧只有 (100,50) 起 200x100 的不透明矩形；左上角 alpha=32 的像素不算内容。
            var floatingFrames = new[]
            {
                VisualReferenceBaseline.Floating.ActualCapture(
                    "floating-countdowns-day",
                    CreateContentRoiVisualReferenceFixturePng(),
                    "Countdowns",
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    "距离下班",
                    "七月倒计时"),
                VisualReferenceBaseline.Floating.ActualCapture(
                    "floating-workday-complete-day",
                    CreateSolidVisualReferenceFixturePng(520, 390, 60),
                    "Countdowns",
                    string.Empty,
                    "下班啦",
                    0,
                    0,
                    "下班啦",
                    "七月倒计时"),
                FloatingLongNoteFixtureFrame("long-notes-day-top", 100, 0),
                FloatingLongNoteFixtureFrame("long-notes-day-bottom", 140, 400),
                VisualReferenceBaseline.Floating.ActualCapture(
                    "notes-search-expanded-day",
                    CreateSolidVisualReferenceFixturePng(520, 390, 180),
                    "Notes",
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    "search-expanded",
                    "search-expanded")
            };
            FloatingReferenceBaselineValidation ValidateFloating(
                IReadOnlyList<FloatingReferenceActualCapture> frames) =>
                VisualReferenceBaseline.Floating.Run(validate, frames, true, true);
            var floatingResult = VisualReferenceBaseline.Floating.Run(generate, floatingFrames, true, true);
            Must(
                floatingResult.AllPassed && floatingResult.Diagnostics.Count == 0,
                "悬浮窗视觉基线生成后未通过同一交界面的校验。");
            Must(
                floatingResult.Captures.Single(capture => capture.CaptureId == "floating-countdowns-day").ContentRoi ==
                    new VisualReferenceContentRoi(32, 100, 50, 200, 100, 20_000),
                "悬浮窗视觉基线未按 alpha > 32 收缩内容 ROI。");

            var searchPngPath = VisualReferenceBaseline.Floating.BaselineFilePath(sourceRoot, "notes-search-expanded-day.png");
            var searchPng = File.ReadAllBytes(searchPngPath);
            File.WriteAllBytes(searchPngPath, CreateSolidVisualReferenceFixturePng(520, 390, 200));
            Must(!ValidateFloating(floatingFrames).Gates.PngHashesMatch, "悬浮窗视觉基线未让合法但不同的 PNG 触发哈希失败。");
            File.Delete(searchPngPath);
            Must(!ValidateFloating(floatingFrames).Gates.ExpectedCaptureFilesPresent, "悬浮窗视觉基线未让缺失 PNG 的文件门禁失败。");
            File.WriteAllBytes(searchPngPath, searchPng);

            var orphanPath = VisualReferenceBaseline.Floating.BaselineFilePath(sourceRoot, "orphan.png");
            File.WriteAllBytes(orphanPath, searchPng);
            Must(
                ValidateFloating(floatingFrames).Diagnostics.SequenceEqual(new[] { "NoOrphanPngFiles" }, StringComparer.Ordinal),
                "悬浮窗视觉基线未只以 inventory 门禁拒绝孤儿 PNG。");
            File.Delete(orphanPath);

            var manifestPath = VisualReferenceBaseline.Floating.BaselineFilePath(sourceRoot, "manifest.json");
            var manifest = File.ReadAllBytes(manifestPath);
            var escaping = JsonNode.Parse(manifest)!;
            escaping["captures"]![0]!["path"] = "../escape.png";
            File.WriteAllText(manifestPath, escaping.ToJsonString());
            var escapingResult = ValidateFloating(floatingFrames);
            Must(
                !escapingResult.Gates.ManifestStructureValid &&
                escapingResult.Diagnostics.Any(diagnostic => diagnostic.Contains("逃出源码根目录", StringComparison.Ordinal)),
                "悬浮窗视觉基线未拒绝逃出源码根目录的路径。");
            var unbound = JsonNode.Parse(manifest)!;
            unbound["sourceBinding"]!["files"]!.AsArray().RemoveAt(0);
            File.WriteAllText(manifestPath, unbound.ToJsonString());
            Must(!ValidateFloating(floatingFrames).Gates.SourceHashesMatch, "悬浮窗视觉基线未拒绝少绑一个源码文件的 manifest。");
            File.WriteAllBytes(manifestPath, manifest);

            WriteVisualReferenceFixtureSource(sourceRoot, "src/MinminCountdown/HolidayCalendar.cs", "// drift");
            Must(
                ValidateFloating(floatingFrames).Diagnostics.SequenceEqual(new[] { "SourceHashesMatch" }, StringComparer.Ordinal),
                "悬浮窗视觉基线未只以源码哈希门禁拒绝改动过的绑定源码。");
            WriteVisualReferenceFixtureSource(sourceRoot, "src/MinminCountdown/HolidayCalendar.cs", string.Empty);

            var scrolledFrames = floatingFrames
                .Select(frame => frame.Spec.CaptureId == "long-notes-day-top"
                    ? FloatingLongNoteFixtureFrame("long-notes-day-top", 100, 1)
                    : frame)
                .ToArray();
            Must(
                !ValidateFloating(scrolledFrames).FreshnessGates.StateFingerprintsMatch,
                "悬浮窗视觉基线未拒绝滚动状态与 manifest 不一致的实拍帧。");

            var bottomStatusPath = Path.Combine(sourceRoot, "src", "MinminCountdown", "FloatingBottomStatus.cs");
            File.Delete(bottomStatusPath);
            Must(
                ValidateFloating(floatingFrames).Diagnostics.Single()
                    .Contains("悬浮窗参考图绑定的视觉源码不存在", StringComparison.Ordinal),
                "悬浮窗视觉基线未报出缺失的绑定源码。");
            return true;
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }
        }
    }

    private static FloatingReferenceActualCapture FloatingLongNoteFixtureFrame(
        string captureId,
        byte blue,
        double verticalOffset) =>
        VisualReferenceBaseline.Floating.ActualCapture(
            captureId,
            CreateSolidVisualReferenceFixturePng(520, 390, blue),
            "Notes",
            "fixture-note",
            "长便签夹具",
            verticalOffset,
            400,
            "长便签夹具",
            "长便签夹具");

    private static void WriteVisualReferenceFixtureSource(string sourceRoot, string relativePath, string suffix)
    {
        var path = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath + suffix);
    }

    private static bool ThrowsInvalidOperation(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static byte[] CreateSolidVisualReferenceFixturePng(
        int width,
        int height,
        byte blue,
        byte firstPixelAlpha = byte.MaxValue)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = 112;
            pixels[offset + 2] = 176;
            pixels[offset + 3] = byte.MaxValue;
        }
        pixels[3] = firstPixelAlpha;
        return EncodeVisualReferenceFixturePng(width, height, pixels);
    }

    private static byte[] CreateContentRoiVisualReferenceFixturePng()
    {
        const int width = 520;
        const int height = 390;
        var pixels = new byte[width * height * 4];
        pixels[3] = 32;
        for (var y = 50; y < 150; y++)
        {
            for (var x = 100; x < 300; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = 48;
                pixels[offset + 1] = 112;
                pixels[offset + 2] = 176;
                pixels[offset + 3] = byte.MaxValue;
            }
        }
        return EncodeVisualReferenceFixturePng(width, height, pixels);
    }

    private static byte[] EncodeVisualReferenceFixturePng(int width, int height, byte[] pixels)
    {
        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static MainStressCountdownTitleGateResults EvaluateMainStressCountdownTitleGates(
        MainStressCountdownTitleGateInputs input) =>
        new(
            input.ExactTitleRendered,
            input.TextIsSingleLineAndUntrimmed &&
            input.TextFits &&
            input.FullyVisibleInViewport &&
            input.SeparatedFromRemaining &&
            input.SeparatedFromActions);

    public static bool RunMainStressCountdownTitleGateIsolationBehavior()
    {
        var passing = new MainStressCountdownTitleGateInputs(
            true,
            true,
            true,
            true,
            true,
            true);
        Must(
            EvaluateMainStressCountdownTitleGates(passing).AllPassed,
            "主窗口 Stress 标题全通过夹具被错误拒绝。");

        var titleMissing = EvaluateMainStressCountdownTitleGates(
            passing with { ExactTitleRendered = false });
        Must(
            !titleMissing.ExactTitleRendered && titleMissing.LayoutSafe,
            "主窗口 Stress 标题缺失负夹具未单独让精确文本门禁变红。");

        var truncatedOrOverlapping = EvaluateMainStressCountdownTitleGates(
            passing with
            {
                TextIsSingleLineAndUntrimmed = false,
                SeparatedFromActions = false
            });
        Must(
            truncatedOrOverlapping.ExactTitleRendered && !truncatedOrOverlapping.LayoutSafe,
            "主窗口 Stress 标题截断或重叠负夹具未单独让布局门禁变红。");

        var remainingOverlap = EvaluateMainStressCountdownTitleGates(
            passing with { SeparatedFromRemaining = false });
        Must(
            remainingOverlap.ExactTitleRendered && !remainingOverlap.LayoutSafe,
            "主窗口 Stress 标题与剩余时间重叠负夹具未单独让布局门禁变红。");

        return true;
    }

    private static void VerifyOnlyFloatingReferenceManifestGateFails(
        string fixtureName,
        FloatingReferenceManifestGateInputs input,
        string expectedFailure)
    {
        var results = EvaluateFloatingReferenceManifestGates(input).AsDictionary();
        Must(
            results.TryGetValue(expectedFailure, out var failed) && !failed,
            $"悬浮窗参考图负向夹具 {fixtureName} 未让 {expectedFailure} 变红。");
        Must(
            results
                .Where(pair => !string.Equals(pair.Key, expectedFailure, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"悬浮窗参考图负向夹具 {fixtureName} 误伤无关契约。");
    }

    public static bool RunFloatingQuickAddThemeResourceIsolationBehavior()
    {
        var passingChecks = FloatingWindow.RequiredQuickAddThemeChecks.ToDictionary(
            name => name,
            _ => true,
            StringComparer.Ordinal);
        Must(
            new FloatingQuickAddThemeProbeResult(
                passingChecks,
                Array.Empty<FloatingQuickAddThemeProbeDiagnostic>()).Passed,
            "悬浮窗快速新增局部主题全通过夹具被错误拒绝。");

        foreach (var target in FloatingWindow.RequiredQuickAddThemeChecks)
        {
            var failingChecks = new Dictionary<string, bool>(passingChecks, StringComparer.Ordinal)
            {
                [target] = false
            };
            var result = new FloatingQuickAddThemeProbeResult(
                failingChecks,
                [new FloatingQuickAddThemeProbeDiagnostic(
                    target,
                    "SyntheticControl",
                    "CanvasBrush",
                    "#FFFFFDFC",
                    "#FF473323",
                    false,
                    false,
                    false,
                    true,
                    true,
                    string.Empty,
                    string.Empty)]);
            Must(
                !result.Passed &&
                result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal) &&
                result.Diagnostics.Single().Check == target,
                $"悬浮窗快速新增局部主题负向夹具未让 {target} 单独失败。");
        }

        return true;
    }

    public static bool RunFloatingQuickAddPaletteBindingIsolationBehavior()
    {
        var day = FloatingQuickAddPaletteBindings.Expected();
        Must(
            FloatingQuickAddPaletteBindings.Evaluate(day).Passed,
            "悬浮窗快速新增日间 palette 固定映射未通过。");

        var historicalMiswirings = new[]
        {
            (
                Mapping: day with { InputSurfaceResourceKey = "FloatingDayErrorBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.InputSurfaceMatches)),
            (
                Mapping: day with { InputTextResourceKey = "FloatingDayNoteInputBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.InputTextMatches)),
            (
                Mapping: day with { ErrorTextResourceKey = "FloatingDayNoteInputTextBrush" },
                FailedCheck: nameof(FloatingQuickAddPaletteBindingResult.ErrorTextMatches))
        };
        foreach (var fixture in historicalMiswirings)
        {
            var failing = FloatingQuickAddPaletteBindings.Evaluate(fixture.Mapping);
            Must(
                !failing.Passed &&
                failing.FailedChecks.SequenceEqual([fixture.FailedCheck], StringComparer.Ordinal),
                $"悬浮窗快速新增历史串线未只击穿 {fixture.FailedCheck}。");
        }

        return true;
    }

    public static FloatingPrimaryCountdownContrastGateResults EvaluateFloatingPrimaryCountdownContrastGates(
        FloatingPrimaryCountdownContrastGateInputs input) =>
        new(
            input.ContrastRatios.Count == 3 && input.ContrastRatios.All(ratio => ratio >= 3),
            input.AccentSeparatedFromPlushGlow);

    public static bool RunFloatingPrimaryCountdownContrastGateIsolationBehavior()
    {
        var passing = new FloatingPrimaryCountdownContrastGateInputs(
            new[] { 3.96, 3.96, 3.96 },
            true);
        var passingResult = EvaluateFloatingPrimaryCountdownContrastGates(passing);
        Must(
            passingResult.TextContrast && passingResult.AccentSeparatedFromPlushGlow,
            "悬浮窗主倒计时对比度与语义分离全通过夹具被错误拒绝。");

        var lowContrast = EvaluateFloatingPrimaryCountdownContrastGates(
            passing with { ContrastRatios = new[] { 3.96, 2.9, 3.96 } });
        Must(
            !lowContrast.TextContrast && lowContrast.AccentSeparatedFromPlushGlow,
            "主倒计时低对比度负向夹具未单独让对比度门禁变红。");

        var sharedAccentToken = EvaluateFloatingPrimaryCountdownContrastGates(
            passing with { AccentSeparatedFromPlushGlow = false });
        Must(
            sharedAccentToken.TextContrast && !sharedAccentToken.AccentSeparatedFromPlushGlow,
            "主倒计时与玩偶共享 token 的负向夹具未单独让语义分离门禁变红。");

        return true;
    }

    public static AuditContrastGateResults EvaluateAuditContrastGates(AuditContrastGateInputs input) =>
        new(
            input.SourceLabelRatios.Count == 2 && input.SourceLabelRatios.All(ratio => ratio >= 4.5),
            input.SourceUsesInkBrush,
            input.SourceEmphasisPreserved,
            input.DialogRatios.Count == 2 && input.DialogRatios.All(ratio => ratio >= 4.5),
            input.FocusRatios.Count == 3 && input.FocusRatios.All(ratio => ratio >= 3));

    public static bool RunAuditContrastGateIsolationBehavior()
    {
        var passing = new AuditContrastGateInputs(
            new[] { 11.49, 10.0 },
            true,
            true,
            new[] { 6.05, 6.05 },
            new[] { 8.1, 8.4, 8.2 });
        var passingResult = EvaluateAuditContrastGates(passing);
        Must(passingResult.AllPassed, "审计对比度全通过夹具被错误拒绝。");

        var sourceLow = EvaluateAuditContrastGates(
            passing with { SourceLabelRatios = new[] { 1.90, 10.0 } });
        Must(
            !sourceLow.SourceLabelContrast &&
            sourceLow.DialogContrast &&
            sourceLow.FocusContrast,
            "来源标签不达标时未单独触发对比度负向门。");

        var dialogLow = EvaluateAuditContrastGates(
            passing with { DialogRatios = new[] { 3.85, 6.05 } });
        Must(
            dialogLow.SourceLabelContrast &&
            !dialogLow.DialogContrast &&
            dialogLow.FocusContrast,
            "系统对话框主按钮不达标时未单独触发对比度负向门。");

        var focusLow = EvaluateAuditContrastGates(
            passing with { FocusRatios = new[] { 1.90, 8.4, 8.2 } });
        Must(
            focusLow.SourceLabelContrast &&
            focusLow.DialogContrast &&
            !focusLow.FocusContrast,
            "键盘焦点环不达标时未单独触发对比度负向门。");

        return true;
    }

    public static FloatingSecondaryRemainingGateResults EvaluateFloatingSecondaryRemainingGates(
        FloatingSecondaryRemainingGateInputs input) =>
        new(
            input.ExpectedTexts.Count == 5 &&
                input.FormattedTexts.SequenceEqual(input.ExpectedTexts, StringComparer.Ordinal) &&
                input.RenderedTexts.SequenceEqual(input.ExpectedTexts, StringComparer.Ordinal),
            input.HasExpectedSampleCount &&
                input.ColumnsAre194Pixels &&
                input.TextIsRightAligned &&
                input.TextIsSingleLineAndUntrimmed &&
                input.TextIsFullyVisible &&
                input.UsesTabularNumerals &&
                input.FontSizingMatches);

    public static bool RunFloatingSecondaryRemainingGateIsolationBehavior()
    {
        var expected = new[]
        {
            "4 天",
            "4 天 13 小时",
            "4 天 13 小时 18 分钟",
            "4 天 13 小时 18 分钟 59 秒",
            "2000000 天 00 小时 00 分钟 00 秒"
        };
        var passing = new FloatingSecondaryRemainingGateInputs(
            expected,
            expected,
            expected,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        var passingResult = EvaluateFloatingSecondaryRemainingGates(passing);
        Must(
            passingResult.UnitSpacingMatches && passingResult.FixedColumnLayoutMatches,
            "悬浮窗次级倒计时全通过夹具被错误拒绝。");

        var spacingRemoved = EvaluateFloatingSecondaryRemainingGates(
            passing with
            {
                RenderedTexts = expected
                    .Select(text => text.Replace(" ", string.Empty, StringComparison.Ordinal))
                    .ToArray()
            });
        Must(
            !spacingRemoved.UnitSpacingMatches && spacingRemoved.FixedColumnLayoutMatches,
            "悬浮窗次级倒计时去空格负向夹具未单独变红。");

        var brokenLayout = EvaluateFloatingSecondaryRemainingGates(
            passing with { ColumnsAre194Pixels = false });
        Must(
            brokenLayout.UnitSpacingMatches && !brokenLayout.FixedColumnLayoutMatches,
            "悬浮窗次级倒计时固定列负向夹具未单独变红。");

        return true;
    }

    // #157 修复轮 1（reviewer CONCERN）：确定性视口右缘算术的唯一实现——现役捕获
    // （ValidationReport.CaptureFloatingViewportState）与反例门禁共同消费它；
    // 删除「− 设计命中轨」会使可见态负向夹具（越界 386.3 必须红）变红。
    public static double FloatingViewportDesignedRight(
        double scrollerSlotRight,
        double designedScrollBarRail,
        bool scrollBarVisible) =>
        scrollerSlotRight - (scrollBarVisible ? designedScrollBarRail : 0d);

    // #157：悬浮视口几何门禁——「卡右缘 ≤ 视口右缘 + 0.25」的比较基准必须是确定性口径：
    // 滚动内容槽右缘（只由 520 外壳固定边距决定）扣除 §10 设计命中轨（滚动条可见时），
    // 不消费随系统滚动条度量漂移的滚动条列宽（旧 ViewportWidth 口径在 100%/125% 显示
    // 缩放下分别 0.05 过 / 0.55 红，§11 漂移族）。用会失败的反例保住门禁语义。
    public static bool EvaluateFloatingViewportGeometryGate(FloatingViewportGeometryGateInputs input)
    {
        var rightLimit = FloatingViewportDesignedRight(
            input.ScrollerSlotRight,
            input.DesignedScrollBarRail,
            input.ScrollBarVisible) + 0.25;
        return SurfaceRightFits(input.MaximumCardRight, rightLimit) &&
               SurfaceRightFits(input.MaximumDateStampRight, rightLimit) &&
               SurfaceRightFits(input.NoteEntryRight, rightLimit);
    }

    private static bool SurfaceRightFits(double? right, double limit) =>
        right is null || right <= limit;

    public static bool RunFloatingViewportGeometryGateIsolationBehavior()
    {
        // 现役真实几何（#157 插桩实测，任意显示缩放下稳定）：槽右 396 = 51 + 345（520 外壳
        // 固定边距），设计命中轨 10，便签卡右 379.2、印记右 342.4、快速新增右 51。
        var passing = new FloatingViewportGeometryGateInputs(396, 10, true, 379.2, 342.4, 51);
        Must(EvaluateFloatingViewportGeometryGate(passing), "悬浮视口几何全通过夹具被错误拒绝。");
        var visibleBoundary = 396 - 10 + 0.25;
        Must(
            EvaluateFloatingViewportGeometryGate(passing with { MaximumCardRight = visibleBoundary }),
            "设计视口边界容差内的现役几何被错误拒绝。");
        Must(
            !EvaluateFloatingViewportGeometryGate(
                passing with { MaximumCardRight = visibleBoundary + 0.01 }),
            "卡片越过设计视口边界未被拒绝（真几何回归必须红）。");
        Must(
            !EvaluateFloatingViewportGeometryGate(
                passing with { MaximumDateStampRight = visibleBoundary + 0.01 }),
            "日期印记越过设计视口边界未被拒绝。");
        Must(
            !EvaluateFloatingViewportGeometryGate(
                passing with { NoteEntryRight = visibleBoundary + 0.01 }),
            "快速新增越过设计视口边界未被拒绝。");
        // 滚动条隐藏（零扣除）时设计边界放大到滚动内容槽右：越进命中轨区不算回归，
        // 越出槽右仍是回归——边界语义与扣除规则绑定，不随系统度量变化。
        Must(
            EvaluateFloatingViewportGeometryGate(
                passing with { ScrollBarVisible = false, MaximumCardRight = visibleBoundary + 0.01 }),
            "滚动条隐藏时设计边界内的几何被错误拒绝。");
        Must(
            !EvaluateFloatingViewportGeometryGate(
                passing with { ScrollBarVisible = false, MaximumCardRight = 396.3 }),
            "滚动条隐藏时卡片越过滚动内容槽未被拒绝。");
        return true;
    }

    public static FloatingScrollBarGateResults EvaluateFloatingScrollBarGates(
        FloatingScrollBarGateInputs input) =>
        new(
            input.LocalStylePresent && input.MainWindowStyleUnaffected,
            input.HitTargetWidth >= 10,
            input.NormalThumbWidth is >= 3.5 and <= 4.5 &&
                input.HoverThumbWidth is >= 3.5 and <= 4.5 &&
                input.DragThumbWidth is >= 3.5 and <= 4.5 &&
                input.NormalThumbOpacity is > 0 and <= 0.6 &&
                input.HoverThumbOpacity > input.NormalThumbOpacity &&
                input.DragThumbOpacity > input.HoverThumbOpacity &&
                input.DragThumbOpacity <= 1,
            input.ThemeTokensMatch,
            input.NormalDoesNotObscureContent &&
                input.HoverDoesNotObscureContent &&
                input.DragDoesNotObscureContent &&
                input.ContentSafeAreaUnchanged,
            input.WheelChangesOffset && input.DragChangesOffset);

    public static bool RunFloatingScrollBarGateIsolationBehavior()
    {
        var passing = new FloatingScrollBarGateInputs(
            true,
            true,
            10,
            4,
            4,
            4,
            0.52,
            0.78,
            0.96,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        var passingResult = EvaluateFloatingScrollBarGates(passing);
        Must(
            passingResult.AllPassed,
            "悬浮窗滚动条全通过夹具被错误拒绝。");

        VerifyOnlyFloatingScrollBarGateFails(
            passing with { LocalStylePresent = false },
            nameof(FloatingScrollBarGateResults.LocalStyleScoped));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { HitTargetWidth = 8 },
            nameof(FloatingScrollBarGateResults.HitTargetUsable));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { NormalThumbWidth = 7 },
            nameof(FloatingScrollBarGateResults.VisualWeightProgression));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { NormalDoesNotObscureContent = false },
            nameof(FloatingScrollBarGateResults.ContentRemainsSafe));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { ContentSafeAreaUnchanged = false },
            nameof(FloatingScrollBarGateResults.ContentRemainsSafe));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { ThemeTokensMatch = false },
            nameof(FloatingScrollBarGateResults.UsesWarmThemeTokens));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { WheelChangesOffset = false },
            nameof(FloatingScrollBarGateResults.InputPathsWork));
        VerifyOnlyFloatingScrollBarGateFails(
            passing with { DragChangesOffset = false },
            nameof(FloatingScrollBarGateResults.InputPathsWork));
        return true;
    }

    private static void VerifyOnlyFloatingScrollBarGateFails(
        FloatingScrollBarGateInputs input,
        string expectedFailure)
    {
        var results = EvaluateFloatingScrollBarGates(input).AsDictionary();
        Must(
            results.TryGetValue(expectedFailure, out var failed) && !failed,
            $"悬浮窗滚动条负向夹具未让 {expectedFailure} 变红。");
        Must(
            results
                .Where(pair => !string.Equals(pair.Key, expectedFailure, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"悬浮窗滚动条负向夹具 {expectedFailure} 误伤无关契约。");
    }

    private static void VerifyOnlyTargetFails(
        UiQualityGateInputs input,
        string target)
    {
        var results = EvaluateUiQualityGates(input).AsDictionary();
        Must(
            results.TryGetValue(target, out var targetValue) && !targetValue,
            $"UI 质量门禁负向夹具未让 {target} 单独失败。");
        Must(
            results
                .Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"UI 质量门禁负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyCountdownEmptyStateGateFails(
        CountdownEmptyStateGateInputs input,
        string target)
    {
        var results = EvaluateCountdownEmptyStateGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"倒计时空态负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"倒计时空态负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyNoteVisualTokenGateFails(
        NoteVisualTokenGateInputs input,
        string target)
    {
        var results = EvaluateNoteVisualTokenGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"主窗口便签视觉合同负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"主窗口便签视觉合同负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyMainNoteCompletionGateFails(
        MainNoteCompletionGateInputs input,
        string target)
    {
        var results = EvaluateMainNoteCompletionGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"主窗口便签完成复选框负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"主窗口便签完成复选框负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyMainCountdownRemainingGateFails(
        MainCountdownRemainingGateInputs input,
        string target)
    {
        var results = EvaluateMainCountdownRemainingGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"主窗口倒计时剩余时间负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"主窗口倒计时剩余时间负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyCountdownIncrementalGateFails(
        CountdownIncrementalRefreshGateResults results,
        string target)
    {
        Must(!results.AllPassed && results.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"倒计时增量刷新负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlyCountdownReadabilityGateFails(
        CountdownReadabilityGateInputs input,
        string target)
    {
        var results = EvaluateCountdownReadabilityGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"倒计时可读性负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"倒计时可读性负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyCountdownEditingGateFails(
        CountdownEditingGateInputs input,
        string target)
    {
        var results = EvaluateCountdownEditingGates(input).AsDictionary();
        Must(results.TryGetValue(target, out var value) && !value,
            $"倒计时编辑负向夹具未让 {target} 单独失败。");
        Must(results.Where(pair => !string.Equals(pair.Key, target, StringComparison.Ordinal))
                .All(pair => pair.Value),
            $"倒计时编辑负向夹具 {target} 误伤无关契约。");
    }

    private static void VerifyOnlyConfirmationGateFails(
        ConfirmationDialogGateInputs input,
        string target)
    {
        var result = EvaluateConfirmationDialogGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"删除确认对话框负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlySecondaryWindowTypographyGateFails(
        SecondaryWindowTypographyGateInputs input,
        string target)
    {
        var result = EvaluateSecondaryWindowTypographyGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"次级窗口字号负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlyReminderCenterKeyboardGateFails(
        ReminderCenterKeyboardGateInputs input,
        string target)
    {
        var result = EvaluateReminderCenterKeyboardGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"提醒中心键盘负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlyShortcutGateFails(MainShortcutGateInputs input, string target) =>
        Must(!EvaluateMainShortcutGates(input).AllPassed &&
             EvaluateMainShortcutGates(input).FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"主窗口快捷键负向夹具未只击穿 {target}。");

    private static void VerifyOnlyMainKeyboardNavigationGateFails(
        MainKeyboardNavigationGateInputs input,
        string target)
    {
        var result = EvaluateMainKeyboardNavigationGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"主窗口键盘导航负向夹具未只击穿 {target}。");
    }

    private static void VerifyOnlyMainStructuralAnnouncementGateFails(
        MainStructuralAnnouncementGateInputs input,
        string target)
    {
        var result = EvaluateMainStructuralAnnouncementGates(input);
        Must(!result.AllPassed && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"主窗口结构变化通知负向夹具未只击穿 {target}。");
    }
}
