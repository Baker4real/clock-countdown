using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using WpfCalendar = System.Windows.Controls.Calendar;

namespace MinminCountdown;

internal static partial class ValidationReport
{
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
                    5086.31m,
                    new TimeSpan(9, 0, 0),
                    new TimeSpan(18, 0, 0)));
            File.WriteAllText(settingsPath, SettingsContract.SerializeCanonical(settings));
            var days = Enumerable.Range(0, 365)
                .Select(offset => new
                {
                    date = new DateTime(2030, 1, 1).AddDays(offset)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    is_workday = new DateTime(2030, 1, 1).AddDays(offset) != new DateTime(2030, 7, 16)
                })
                .ToArray();
            File.WriteAllText(holidayPath, JsonSerializer.Serialize(new { days }));

            var referenceClock = new MutableValidationClock(referenceUtc);
            var referenceWorkspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: referenceClock));
            using var referenceCalendar = new HolidayCalendar(temporaryRoot);
            referenceCalendar.RefreshAsync(referenceUtc, CancellationToken.None).GetAwaiter().GetResult();
            referenceWindow = new FloatingWindow(
                referenceWorkspace,
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
                referenceWindow, referenceWorkspace, referenceCalendar, manager, assetRoot, settingsPath, referenceClock);
            var completionResult = VerifyWorkdayCompletion(referenceWindow, referenceWorkspace, referenceClock, settingsPath);
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
            ClearCaptureKeyboardFocus(root);
            var actualCapture = CaptureVisual(root);
            var framedCapture = ScaleToFrame(actualCapture, captureWidth, captureHeight);
            var captureBytes = EncodePng(framedCapture);
            var captureWritten = captureBytes.Length > 0;
            var baselineBodyScroller = Required<ScrollViewer>(referenceWindow, "FloatingBodyScroller");
            var dayCountdownCapture = VisualReferenceBaseline.Floating.ActualCapture(
                "floating-countdowns-day",
                captureBytes,
                "Countdowns",
                string.Empty,
                string.Empty,
                baselineBodyScroller.VerticalOffset,
                baselineBodyScroller.ScrollableHeight,
                "距离下班",
                "七月倒计时");
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
            var referencePreferences = referenceWorkspace.Snapshot.Settings.Preferences;
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
                var preferenceSaved = referenceWorkspace.Apply(new SavePreferencesChange(
                    precisionCase.Precision,
                    referencePreferences.MonthlySalary,
                    referencePreferences.WorkStart,
                    referencePreferences.WorkEnd)).Succeeded;
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

            var longSecondaryAdded = referenceWorkspace.Apply(new SaveCountdownChange(
                "reference-long-secondary",
                "长周期次级倒计时",
                referenceUtc.AddDays(2_000_000),
                Array.Empty<string>())).Succeeded;
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
            var longSecondaryDeleted = referenceWorkspace.Apply(new DeleteCountdownChange(
                "reference-long-secondary")).Succeeded;
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            secondaryRemainingTextFullyVisible =
                longSecondaryAdded &&
                longSecondaryDeleted &&
                longSecondaryMeasurement.IsFullyVisible;
            // 月薪 0/非 0 底部文案切换：参考会话日历已就绪，月薪清零后必须显示手写体鼓励文案。
            var referenceSalary = referencePreferences.MonthlySalary;
            var zeroSalaryApplied = referenceWorkspace.Apply(new SavePreferencesChange(
                "Second",
                0m,
                referencePreferences.WorkStart,
                referencePreferences.WorkEnd)).Succeeded;
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
            var salaryRestored = referenceWorkspace.Apply(new SavePreferencesChange(
                "Second",
                referenceSalary,
                referencePreferences.WorkStart,
                referencePreferences.WorkEnd)).Succeeded;
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

            var userCountdownDeleted = referenceWorkspace.Apply(new DeleteCountdownChange(
                "reference-july")).Succeeded;
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            viewportStates.Add(
                CaptureFloatingViewportState("one-countdown", referenceWindow, root, rows));

            // #236: use an explicit calendar non-workday; after-work now has a completion card.
            var nonWorkdayUtc = new DateTimeOffset(2030, 7, 16, 19, 11, 0, TimeSpan.FromHours(8)).ToUniversalTime();
            referenceWindow.UpdateTime(nonWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 0);
            viewportStates.Add(
                CaptureFloatingViewportState("zero-countdowns", referenceWindow, root, rows));

            var ordinaryPrimaryTargetUtc = referenceUtc
                .AddDays(4)
                .AddHours(13)
                .AddMinutes(18)
                .AddSeconds(59);
            var ordinaryPrimaryAdded = referenceWorkspace.Apply(new SaveCountdownChange(
                "reference-ordinary-primary",
                "普通主项单行倒计时",
                ordinaryPrimaryTargetUtc,
                Array.Empty<string>())).Succeeded;
            referenceWindow.UpdateTime(nonWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            var ordinaryPrimaryRemainingTextFullyVisible =
                PrimaryRemainingTextFitsOnOneLine(
                    rows,
                    ordinaryPrimaryTargetUtc,
                    nonWorkdayUtc);
            primaryCountdownContrastMeasurements.Add(CaptureFloatingPrimaryCountdownContrast(
                referenceWindow, rows, panel, "Ordinary"));
            // 徽章排他性：主项由普通倒计时承担时不得出现「超期待！」徽章。
            var ordinaryPrimaryBadgeAbsent = !Descendants<TextBlock>(rows)
                .Any(text => string.Equals(text.Text, "超期待！", StringComparison.Ordinal));
            var ordinaryPrimaryDeleted = referenceWorkspace.Apply(new DeleteCountdownChange(
                "reference-ordinary-primary")).Succeeded;
            referenceWindow.UpdateTime(nonWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 0);

            var tripleDigitAdded = referenceWorkspace.Apply(new SaveCountdownChange(
                "reference-999-primary", "999 天主项", nonWorkdayUtc.AddDays(999).AddHours(13).AddMinutes(18).AddSeconds(59),
                Array.Empty<string>())).Succeeded;
            referenceWindow.UpdateTime(nonWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            var tripleDigitMeasurement = CaptureFloatingPrimaryCountdownContrast(referenceWindow, rows, panel, "999Days");
            primaryCountdownContrastMeasurements.Add(tripleDigitMeasurement);
            var tripleDigitDeleted = referenceWorkspace.Apply(new DeleteCountdownChange("reference-999-primary")).Succeeded;
            if (!tripleDigitAdded || !tripleDigitDeleted)
            {
                throw new InvalidOperationException("999 天悬浮主项夹具未能完整保存和恢复。");
            }
            referenceWindow.UpdateTime(nonWorkdayUtc);
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
            var extremePrimaryAdded = referenceWorkspace.Apply(new SaveCountdownChange(
                "reference-extreme-primary",
                "极端长剩余时间",
                extremePrimaryUtc,
                Array.Empty<string>())).Succeeded;
            referenceWindow.UpdateTime(nonWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 1);
            var extremePrimaryCluster = FindFloatingRemainingCluster(rows, "极端长剩余时间");
            var extremePrimaryExpected = CountdownFormatting.FormatRemaining(
                extremePrimaryUtc,
                nonWorkdayUtc,
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
            var extremePrimaryDeleted = referenceWorkspace.Apply(new DeleteCountdownChange(
                "reference-extreme-primary")).Succeeded;
            referenceWindow.UpdateTime(nonWorkdayUtc);
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
            var primaryDeadlineAdded = referenceWorkspace.Apply(new SaveCountdownChange(
                "reference-primary-deadline",
                "最长日期倒计时",
                primaryDeadlineUtc,
                Array.Empty<string>())).Succeeded;
            referenceWindow.UpdateTime(nonWorkdayUtc);
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
                primaryDeadlineOverflowAdded &= referenceWorkspace.Apply(new SaveCountdownChange(
                    primaryDeadlineOverflowIds[index],
                    $"最长日期滚动倒计时 {index + 1}",
                    primaryDeadlineUtc.AddDays(index + 1),
                    Array.Empty<string>())).Succeeded;
            }

            referenceWindow.UpdateTime(nonWorkdayUtc);
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
                primaryDeadlineOverflowDeleted &= referenceWorkspace.Apply(new DeleteCountdownChange(id)).Succeeded;
            }

            var primaryDeadlineDeleted = referenceWorkspace.Apply(new DeleteCountdownChange(
                "reference-primary-deadline")).Succeeded;
            referenceWindow.UpdateTime(nonWorkdayUtc);
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
                overflowCountdownsAdded &= referenceWorkspace.Apply(new SaveCountdownChange(
                    overflowCountdownIds[index],
                    $"滚动倒计时 {index + 1}",
                    referenceUtc.AddDays(index + 1),
                    Array.Empty<string>())).Succeeded;
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
            ClearCaptureKeyboardFocus(root);
            scrolledCountdownsCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
            viewportStates.Add(
                CaptureFloatingViewportState("nine-countdowns-scrolled", referenceWindow, root, rows));

            var overflowCountdownsDeleted = true;
            foreach (var id in overflowCountdownIds)
            {
                overflowCountdownsDeleted &= referenceWorkspace.Apply(new DeleteCountdownChange(id)).Succeeded;
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
            floatingReferenceBaseline = VisualReferenceBaseline.Floating.Run(
                VisualReferenceBaseline.Floating.ModeFromEnvironment(),
                new[] { dayCountdownCapture, completionResult.Capture }
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
            ClearCaptureKeyboardFocus(root);
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
            var undatedNoteSaved = referenceWorkspace.Apply(new SaveNoteChange(
                "reference-undated",
                "无日期历史便签",
                null,
                false,
                Array.Empty<string>(),
                NoReminder.Instance)).Succeeded;
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
            var undatedNoteDeleted = referenceWorkspace.Apply(new DeleteNoteChange("reference-undated")).Succeeded;
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
                    referenceWorkspace.Snapshot.Settings.Notes.All(note => !note.IsCompleted);
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
                var noteCompleted = referenceWorkspace.Apply(new SetNoteCompletionChange(
                    failedNoteId,
                    true)).Succeeded;
                WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 5);
                var noteRestored = referenceWorkspace.Apply(new SetNoteCompletionChange(
                    failedNoteId,
                    false)).Succeeded;
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
                completionResult.Passed,
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
                // 状态指纹取清焦点与编码之前的滚动位置。
                var fingerprintVerticalOffset = bodyScroller.VerticalOffset;
                var fingerprintScrollableHeight = bodyScroller.ScrollableHeight;
                ClearCaptureKeyboardFocus(root);
                var pngBytes = EncodePng(ScaleToFrame(
                    CaptureVisual(root),
                    captureWidth,
                    captureHeight));
                var longNoteCapture = VisualReferenceBaseline.Floating.ActualCapture(
                    captureId,
                    pngBytes,
                    "Notes",
                    primaryBinding?.Note.Id ?? string.Empty,
                    primaryBinding?.Note.Text ?? string.Empty,
                    fingerprintVerticalOffset,
                    fingerprintScrollableHeight,
                    firstVisibleAnchor,
                    lastVisibleAnchor);
                captures.Add(longNoteCapture);
                measurements.Add(new FloatingLongNoteFrameMeasurement(
                    captureId,
                    "Day",
                    state,
                    Math.Round(bodyScroller.VerticalOffset, 2),
                    Math.Round(bodyScroller.ScrollableHeight, 2),
                    primaryBinding?.Note.Id ?? string.Empty,
                    longNoteCapture.StateFingerprint.NoteTextSha256,
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
            ClearCaptureKeyboardFocus(root);
            captures.Add(VisualReferenceBaseline.Floating.ActualCapture(
                "notes-search-expanded-day",
                EncodePng(ScaleToFrame(CaptureVisual(root), captureWidth, captureHeight)),
                "Notes",
                string.Empty,
                string.Empty,
                scroller.VerticalOffset,
                scroller.ScrollableHeight,
                "search-expanded",
                "search-expanded"));
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

    private static Rect BoundsIn(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor)
            .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));

    // 打开的月视图必须是 7 个有字的星期标题 + 6 行 × 7 列互不重叠的日期格。CalendarItem 只给格子写
    // Grid.Row/Column，行列定义与星期标题模板都由模板提供；缺了它们，42 个日期会叠进同一格。
    private static bool CalendarMonthGridLaidOut(WpfCalendar? calendar)
    {
        if (calendar?.Template?.FindName("PART_CalendarItem", calendar) is not CalendarItem item ||
            item.Template?.FindName("PART_MonthView", item) is not Grid monthView)
        {
            return false;
        }

        var days = monthView.Children.OfType<CalendarDayButton>().ToArray();
        var bounds = days.Select(day => BoundsIn(day, monthView)).ToArray();
        var titles = monthView.Children.OfType<TextBlock>()
            .Where(title => Grid.GetRow(title) == 0 && !string.IsNullOrWhiteSpace(title.Text))
            .ToArray();
        var overlapping = bounds.Where((rect, index) => bounds.Skip(index + 1).Any(other =>
        {
            var shared = Rect.Intersect(rect, other);
            return !shared.IsEmpty && shared.Width > 0.5 && shared.Height > 0.5;
        })).Any();
        return days.Length == 42 &&
            bounds.All(rect => rect.Width >= 24 && rect.Height >= 24) &&
            bounds.Select(rect => Math.Round(rect.Left)).Distinct().Count() == 7 &&
            bounds.Select(rect => Math.Round(rect.Top)).Distinct().Count() == 6 &&
            !overlapping &&
            titles.Length == 7;
    }

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
                ClearCaptureKeyboardFocus(root);
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
}
