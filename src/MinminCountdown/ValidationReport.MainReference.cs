using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationReport
{
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
                    layoutSurface.Width == 864 &&
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
                    scaleViewport.Width == 864 &&
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
        const int captureWidth = 864;
        const int captureHeight = 1000;
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
            ClearCaptureKeyboardFocus(root);
            WaitForMainReferenceMotion(main, root);
            var countdownCapture = CaptureOpaqueMainSurface(root, captureWidth, captureHeight);

            main.ShowWorkspace(WorkspaceKind.Notes);
            main.UpdateTime(notesCaptureBeijingTime);
            noteDate.SelectedDate = new DateTime(2026, 7, 29);
            PumpDispatcher();
            CloseMainDatePickerDropdowns(main);
            ArrangeForReferenceCapture(root, captureWidth, captureHeight);
            ClearCaptureKeyboardFocus(root);
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
        // ValidationReport 按区域拆成 partial 文件，主文件之外的每个分片也绑定进证据。
        var validationReportParts = Directory
            .EnumerateFiles(Path.Combine(sourceRoot, "src", "MinminCountdown"), "ValidationReport.*.cs")
            .Order(StringComparer.Ordinal)
            .Select(path => (
                Id: "source-validation-report-" +
                    Path.GetFileNameWithoutExtension(path)["ValidationReport.".Length..].ToLowerInvariant(),
                FilePath: path));
        (string Id, string FilePath)[] artifacts =
        [
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
            .. validationReportParts,
            ("source-application-project", Path.Combine(
                sourceRoot,
                "src",
                "MinminCountdown",
                "MinminCountdown.csproj")),
            ("source-build-script", Path.Combine(sourceRoot, "Build-CountdownApplication.ps1"))
        ];

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

    private static MainReferenceScenes CaptureMainReferenceScenarios(
        ApplicationTheme theme,
        string assetRoot)
    {
        const int captureWidth = 864;
        const int captureHeight = 1000;
        var dayBeijing = new DateTimeOffset(2032, 5, 18, 10, 15, 30, TimeSpan.FromHours(8));
        var referenceUtc = dayBeijing.ToUniversalTime();
        var captures = new List<MainReferenceActualCapture>();
        var stressCountdownTitles = new List<MainStressCountdownTitleMeasurement>();
        var noteCardReminderFonts = new List<MainNoteCardReminderFontMeasurement>();

        try
        {
            foreach (var state in new[] { "Empty", "Representative", "Stress", "Edit", "FilterOpen", "Selected" })
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
                    var settingsState = state is "Edit" or "FilterOpen" or "Selected" ? "Representative" : state;
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
                    foreach (var spec in VisualReferenceBaseline.Main.CaptureSpecs.Where(spec =>
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
                        if (state == "Selected")
                        {
                            var selectedRows = Descendants<NoteSelectionCard>(validationMain).Where(row => row.IsVisible).ToArray();
                            Keyboard.Focus(selectedRows[0]);
                            WindowValidationInput.RaisePreviewKey(selectedRows[0], Key.Space, ModifierKeys.Control);
                            WindowValidationInput.RaisePreviewKey(selectedRows[0], Key.Down, ModifierKeys.Shift);
                            PumpDispatcher();
                            if (Required<TextBlock>(validationMain, "SelectedNotesCount").Text != "已选择 2 条")
                                throw new InvalidOperationException("#235 selected参考帧必须真实选中两条并显示批量操作条。");
                        }
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
                        ClearCaptureKeyboardFocus(root);
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
                        captures.Add(VisualReferenceBaseline.Main.ActualCapture(
                            spec,
                            beijingTime,
                            pngBytes,
                            settings));
                        if (stressCountdownTitle is not null)
                        {
                            stressCountdownTitles.Add(stressCountdownTitle);
                        }
                        if (noteCardReminderFont is not null)
                        {
                            noteCardReminderFonts.Add(noteCardReminderFont);
                        }
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

        var specs = VisualReferenceBaseline.Main.CaptureSpecs;
        if (captures.Count != specs.Count)
        {
            throw new InvalidOperationException(
                $"主窗口正式参考场景未生成严格 {specs.Count} 帧。");
        }

        return new MainReferenceScenes(
            specs.Select(spec => captures.Single(capture =>
                string.Equals(capture.Spec.CaptureId, spec.CaptureId, StringComparison.Ordinal))).ToArray(),
            stressCountdownTitles.OrderBy(measurement => measurement.CaptureId, StringComparer.Ordinal).ToArray(),
            noteCardReminderFonts.OrderBy(measurement => measurement.CaptureId, StringComparer.Ordinal).ToArray());
    }

    private static MainDaySingleBorderValidation ValidateDaySingleBorder()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return MainDaySingleBorderValidation.NotRequested;
        }

        var bands = ReadMainDecorationBands(sourceRoot);
        // #221/#237: v54 shrinks the list-first management window to 864x1000. The opaque
        // compact countdown form now starts below its top decoration band (TopBandEnd 395 ->
        // 395 * 1000 / 1350 = 292.6, form top 300) so it no longer covers the plush characters;
        // scan its actual bounds, independently sampled from the PNG at x=200; retain delta>=30
        // and every #189 negative fixture. Notes still starts one row below its top decoration
        // band scaled to the frame height (TopBandEnd 415 -> ceil(415 * 1000 / 1350) = 308).
        var specs = new[]
        {
            new MainDaySingleBorderSpec("Countdown-Day", "countdown", "main-day-countdowns-empty.png", 338, 391, new[] { 342, 343, 344, 359, 360, 363, 364, 366, 367, 368, 369, 370, 382, 383, 384 }, 319, 337),
            new MainDaySingleBorderSpec("Notes-Day", "notes", "main-day-notes-empty.png", 309, 389, new[] { 314, 315, 348, 349, 367, 379, 380 }, 300, 313)
        };
        var measurements = specs.Select(spec =>
        {
            var path = VisualReferenceBaseline.Main.BaselineFilePath(sourceRoot, spec.FileName);
            if (!File.Exists(path))
            {
                return new MainDaySingleBorderMeasurement(
                    spec.StateId, spec.Scene, Array.Empty<int>(), false, false, false, 0, 0, 0);
            }

            var band = bands.Scenes[spec.Scene];
            var source = DecodeBitmap(File.ReadAllBytes(path));
            var (pixels, stride) = GetBgra32Pixels(source);
            // 装饰带定义以装饰图原生像素为坐标；参考帧把装饰图 Fill 到管理窗画布，
            // 先按帧/装饰图尺寸换算：底部带首行向下取整，缩放过渡行不漏计下沿装饰。
            var rowScale = (double)source.PixelHeight / bands.CanvasHeight;
            var bottomBandStart = (int)Math.Floor(band.BottomBandStart * rowScale);
            var backgroundSampleX = (int)Math.Round(
                bands.BackgroundSampleX * (double)source.PixelWidth / bands.CanvasWidth);
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
            for (var y = bottomBandStart; y < source.PixelHeight; y++)
            {
                var background = y * stride + backgroundSampleX * 4;
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
                bottomBandStart,
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
            // 本进程是无控制台的 WPF 应用：不设 CreateNoWindow 会给 pwsh 新建可见控制台，经默认终端委托弹到用户桌面。
            CreateNoWindow = true,
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
            root.GetProperty("CanvasWidth").GetInt32(),
            root.GetProperty("CanvasHeight").GetInt32(),
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
            VisualReferenceBaseline.Floating.BaselineFilePath(sourceRoot, "manifest.json"),
            VisualReferenceBaseline.Main.BaselineFilePath(sourceRoot, "manifest.json")
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
}
