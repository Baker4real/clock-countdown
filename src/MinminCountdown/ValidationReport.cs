using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private const string FloatingReferenceBaselineVersion = "2026-08-06-v6";
    private const string FloatingReferenceScenario = "floating-countdowns-baseline";
    private const string FloatingLongNotesScenario = "long-notes";
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
        "src/MinminCountdown/App.xaml",
        "src/MinminCountdown/ApplicationTheme.cs",
        "src/MinminCountdown/DomainLogic.cs",
        "src/MinminCountdown/FloatingWindow.Countdowns.cs",
        "src/MinminCountdown/FloatingWindow.Notes.cs",
        "src/MinminCountdown/FloatingWindow.xaml",
        "src/MinminCountdown/FloatingWindow.xaml.cs",
        "src/MinminCountdown/MinminCountdown.csproj",
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
            "floating-countdowns-night",
            FloatingReferenceScenario,
            "Night",
            "Baseline",
            "floating-countdowns-night.png"),
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
            "long-notes-night-top",
            FloatingLongNotesScenario,
            "Night",
            "Top",
            "long-notes-night-top.png"),
        new(
            "long-notes-night-bottom",
            FloatingLongNotesScenario,
            "Night",
            "Bottom",
            "long-notes-night-bottom.png")
    };
    private const string MainReferenceBaselineVersion = "2026-08-06-v6";
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
        "assets/main-decorations/main-countdown-top-night.png",
        "assets/main-decorations/main-notes-scene-day.png",
        "assets/main-decorations/main-notes-top-night.png",
        "src/MinminCountdown.Core/SettingsContract.cs",
        "src/MinminCountdown.Core/SettingsModels.cs",
        "src/MinminCountdown/App.xaml",
        "src/MinminCountdown/ApplicationTheme.cs",
        "src/MinminCountdown/DesktopWorkspace.cs",
        "src/MinminCountdown/DomainLogic.cs",
        "src/MinminCountdown/MainWindow.xaml",
        "src/MinminCountdown/MainWindow.xaml.cs",
        "src/MinminCountdown/MinminCountdown.csproj",
        "src/MinminCountdown/UiControls.cs"
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
        new("main-night-countdowns-empty", "Night", "Countdowns", "Empty", "main-night-countdowns-empty.png"),
        new("main-night-countdowns-representative", "Night", "Countdowns", "Representative", "main-night-countdowns-representative.png"),
        new("main-night-countdowns-stress", "Night", "Countdowns", "Stress", "main-night-countdowns-stress.png"),
        new("main-night-notes-empty", "Night", "Notes", "Empty", "main-night-notes-empty.png"),
        new("main-night-notes-representative", "Night", "Notes", "Representative", "main-night-notes-representative.png"),
        new("main-night-notes-stress", "Night", "Notes", "Stress", "main-night-notes-stress.png")
    };

    public static IReadOnlyDictionary<string, object?> Create(
        MainWindow main,
        FloatingWindow floating,
        FloatingTrayController floatingTrayController,
        DesktopWorkspace desktopWorkspace,
        ApplicationTheme theme,
        HostOptions options,
        string assetRoot)
    {
        main.ShowInTaskbar = false;
        main.WindowStartupLocation = WindowStartupLocation.Manual;
        main.Left = -20_000;
        main.Top = -20_000;
        main.Opacity = 0;
        floating.ShowInTaskbar = false;
        floating.Opacity = 0;
        main.Show();
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

        var mainWindowScenarios = main.RunValidationScenarios(options.ExerciseNoteAdd);
        var noteExercise = mainWindowScenarios.NoteExercise;
        var floatingNoteExercise = floatingWindowScenarios.CrossDayReminderAdded;
        var mainReminderModeExercise = mainWindowScenarios.ReminderModes;
        var floatingReminderModeExercise = floatingWindowScenarios.ReminderModes;
        var mainUiPolish = mainWindowScenarios.UiPolish;
        var floatingUiPolish = floatingWindowScenarios.UiPolish;
        var tagExercise = options.ExerciseTagInteractions
            ? ValidationExercises.RunTagInteractions(desktopWorkspace)
            : TagInteractionExerciseResult.NotRun;
        var persistenceExercise = options.ExercisePersistenceFailures
            ? ValidationExercises.RunPersistenceFailures(
                desktopWorkspace,
                desktopWorkspace.Snapshot.SettingsPath)
            : PersistenceFailureExerciseResult.NotRun;

        var themePreferenceExercise = options.ExerciseThemePreference
            ? RunThemePreferenceButtonExercise(main, floating, desktopWorkspace)
            : ThemePreferenceButtonExerciseResult.NotRun;
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
        var floatingTrayInteraction = RunFloatingTrayInteractionExercise(
            floating,
            minimizeToTrayButton,
            floatingTrayController);
        var floatingDragInteraction = floatingWindowScenarios.DragInteraction;

        var datePicker = Required<DatePicker>(main, "CountdownDate");
        datePicker.ApplyTemplate();
        var dateTextBox = datePicker.Template.FindName("PART_TextBox", datePicker) as DatePickerTextBox;
        var dateButton = datePicker.Template.FindName("PART_Button", datePicker) as Button;
        var datePopup = datePicker.Template.FindName("PART_Popup", datePicker) as Popup;
        var dateCalendar = ThemedDatePickerBehavior.GetCalendar(datePicker);
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

        var dateBehaviorAttached = ThemedDatePickerBehavior.IsAttached(datePicker);
        var dateTextMetrics = MeasureTextFit(datePicker, "2031年12月31日");
        var calendarHeaderMetrics = MeasureTextFit(datePicker, "2031年12月");
        var workspaceDraftsRetained = mainWindowScenarios.WorkspaceDraftsRetained;
        var noteDateBehavior = mainWindowScenarios.NoteDateBehavior;
        var originalWorkspace = main.CurrentWorkspace;
        main.ShowWorkspace(WorkspaceKind.Notes);
        var hiddenCountdownSubmitDisabled = !Required<Button>(main, "SaveCountdownButton").IsEnabled;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var switchesToCountdown = Required<Canvas>(main, "CountdownWorkspace").Visibility == Visibility.Visible;
        main.ShowWorkspace(WorkspaceKind.Notes);
        var switchesToNotes = Required<Canvas>(main, "NotesWorkspace").Visibility == Visibility.Visible;
        main.ShowWorkspace(originalWorkspace);

        var selector = Required<TagSelector>(main, "CountdownTags");
        var createAppears = selector.ProbeCreateAction("验证新标签");
        var createCollapsed = !selector.ProbeCreateAction(string.Empty);
        var tagSearchDoesNotSubmit = !selector.CreateButtonIsDefault;
        var reminderBehavior = ValidateReminderDispatch();
        var oneTimeReminderBehavior = ValidateOneTimeReminderLifecycle();
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
        var contrast = ValidateThemeContrast();
        var dangerContrast = ContrastRatio(
            (Color)Application.Current.FindResource("CanvasColor"),
            (Color)Application.Current.FindResource("DangerColor")) >= 4.5;

        var countdownRows = Required<StackPanel>(main, "CountdownRows");
        var activeNoteRows = Required<StackPanel>(main, "ActiveNoteRows");
        var completedNoteRows = Required<StackPanel>(main, "CompletedNoteRows");
        var activeNotesToggle = Required<Button>(main, "ToggleActiveNotes");
        var completedNotesToggle = Required<Button>(main, "ToggleCompletedNotes");
        var activeNotesLabel = Required<TextBlock>(main, "ActiveNotesLabel");
        var completedNotesLabel = Required<TextBlock>(main, "CompletedNotesLabel");
        var activeNotesCount = Required<TextBlock>(main, "ActiveNotesCount");
        var completedNotesCount = Required<TextBlock>(main, "CompletedNotesCount");
        var activeNotesChevron = Required<WpfPath>(main, "ActiveNotesChevron");
        var completedNotesChevron = Required<WpfPath>(main, "CompletedNotesChevron");
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
        var noteStatusDisclosureVisuals =
            HasFixedNoteStatusDisclosure(
                activeNotesToggle,
                activeNotesLabel,
                activeNotesCount,
                activeNotesChevron,
                "未完成",
                workspaceSnapshot.VisibleNotes.Count(note => !note.IsCompleted),
                workspaceSnapshot.VisibleNotes.Any(note => !note.IsCompleted)
                    ? activeNoteRows.Visibility == Visibility.Visible
                    : true) &&
            HasFixedNoteStatusDisclosure(
                completedNotesToggle,
                completedNotesLabel,
                completedNotesCount,
                completedNotesChevron,
                "已完成",
                workspaceSnapshot.VisibleNotes.Count(note => note.IsCompleted),
                workspaceSnapshot.VisibleNotes.Any(note => note.IsCompleted) &&
                completedNoteRows.Visibility == Visibility.Visible);
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
            HasNoteDisclosureAccessibleName(activeNotesToggle, "未完成") &&
            HasNoteDisclosureAccessibleName(completedNotesToggle, "已完成") &&
            renderedDateSections.All(section =>
                HasNoteDisclosureAccessibleName(
                    section.Toggle,
                    GetNoteDateLabel(section.Toggle)?.Text ?? string.Empty));
        var activeNotesGrouped =
            activeNoteRows.Parent is StackPanel noteSections &&
            noteSections.Children.IndexOf(activeNotesToggle) >= 0 &&
            noteSections.Children.IndexOf(activeNoteRows) == noteSections.Children.IndexOf(activeNotesToggle) + 1;
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
        var tagChips = Descendants<Border>(countdownRows)
            .Concat(Descendants<Border>(activeNoteRows))
            .Where(border => !string.IsNullOrWhiteSpace(border.Uid))
            .ToArray();
        var tagChipsUseUid = tagChips.All(chip => chip.Uid.Length > 0);

        var savePreferencesButton = Required<Button>(main, "SavePreferencesButton");
        var savePreferencesButtonFit = MeasureButtonFit(savePreferencesButton);
        var savePreferencesButtonRight = savePreferencesButton
            .TranslatePoint(new Point(savePreferencesButton.ActualWidth, 0), main)
            .X;
        var savePreferencesButtonIsVisible =
            savePreferencesButton.TranslatePoint(new Point(0, 0), main).X >= 0 &&
            savePreferencesButtonRight <= main.ActualWidth;
        var originalMainWidth = main.Width;
        main.Width = main.MinWidth;
        main.UpdateLayout();
        var savePreferencesButtonIsVisibleAtMinimumWidth =
            savePreferencesButton.TranslatePoint(new Point(0, 0), main).X >= 0 &&
            savePreferencesButton.TranslatePoint(
                new Point(savePreferencesButton.ActualWidth, 0),
                main).X <= main.ActualWidth;
        main.Width = originalMainWidth;
        main.UpdateLayout();
        var restoreMainThemeMode = theme.Apply(workspaceSnapshot.UtcNow);
        main.ApplyTheme(ApplicationThemeMode.Night);
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
            main.MainNightReferenceDecorationsLoaded &&
            main.MainReferenceDecorationsFollowActiveTheme ||
            legacyCharacterDocksIntegrateWithPanel;
        main.ApplyTheme(restoreMainThemeMode);
        PumpDispatcher();
        main.UpdateLayout();
        var workArea = SystemParameters.WorkArea;
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
        var plushDecorations = new[]
        {
            Required<Image>(floating, "PlushTopLeft"),
            Required<Image>(floating, "PlushTopRight"),
            Required<Image>(floating, "PlushBottomRight")
        };
        var plushAnchors = new[]
        {
            (Anchor: Required<Border>(floating, "MyMelodyPlushAnchor"), Image: plushDecorations[0]),
            (Anchor: Required<Border>(floating, "CinnamorollPlushAnchor"), Image: plushDecorations[1]),
            (Anchor: Required<Border>(floating, "KuromiPlushAnchor"), Image: plushDecorations[2])
        };
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
        var plushAnchorsIntegrateWithPanel =
            Panel.GetZIndex(floatingPlushScene) > Panel.GetZIndex(taskCard) &&
            plushAnchors.All(pair =>
                ReferenceEquals(pair.Anchor.Child, pair.Image) &&
                !pair.Anchor.IsHitTestVisible &&
                pair.Anchor.BorderThickness.Equals(new Thickness(0)));
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
            floating.UsesReferencePlushPoses &&
            plushAnchorsIntegrateWithPanel &&
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
        var floatingReanchorsAfterContentChanges = floating.IsBottomAnchored;
        var floatingReferenceExercise = RunFloatingReferenceExercise(main, assetRoot);
        var visualReferenceBaselineInterfaceBehavior =
            ValidationExercises.RunVisualReferenceBaselineInterfaceBehavior();
        var floatingReferenceBaselineGateIsolationBehavior =
            ValidationExercises.RunFloatingReferenceManifestGateIsolationBehavior() &&
            visualReferenceBaselineInterfaceBehavior;
        var floatingReferenceFreshnessGateIsolationBehavior =
            ValidationExercises.RunFloatingReferenceFreshnessGateIsolationBehavior();
        var floatingNightPlushEdgeGlowGateIsolationBehavior =
            ValidationExercises.RunFloatingNightPlushEdgeGlowGateIsolationBehavior();
        var floatingPrimaryCountdownContrastGateIsolationBehavior =
            ValidationExercises.RunFloatingPrimaryCountdownContrastGateIsolationBehavior();
        var floatingSecondaryRemainingGateIsolationBehavior =
            ValidationExercises.RunFloatingSecondaryRemainingGateIsolationBehavior();
        var floatingScrollBarGateIsolationBehavior =
            ValidationExercises.RunFloatingScrollBarGateIsolationBehavior();
        var mainThemeExercise = RunMainThemeExercise(theme, main, workspaceSnapshot.UtcNow);
        var floatingThemeExercise = floatingWindowScenarios.ThemeExercise;
        var tagPopupFollowsDayNightTheme =
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
        var floatingManualPlacement = floatingWindowScenarios.ManualPlacement;
        var mainReferenceDecorationValidation = ValidateMainReferenceDecorationScaling(main);
        var mainNightReferenceGeometry = ValidateMainNightReferenceGeometry(main);
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
        var legacyMainReferenceCapture =
            RunMainReferenceCapture(theme, main, workspaceSnapshot, assetRoot);
        var mainReferenceBaseline = RunMainReferenceBaseline(theme, assetRoot);
        var mainReferenceBaselineGateIsolationBehavior =
            ValidationExercises.RunMainReferenceManifestGateIsolationBehavior() &&
            visualReferenceBaselineInterfaceBehavior;
        var mainStressCountdownTitleGateIsolationBehavior =
            ValidationExercises.RunMainStressCountdownTitleGateIsolationBehavior();

        var reminderWindow = new ReminderWindow(new NoteRecord(
            "validation-reminder",
            "主题提醒",
            "2031-04-05",
            false,
            Array.Empty<string>(),
            30,
            DateTimeOffset.UtcNow));
        var reminderThemed =
            reminderWindow.WindowStyle == WindowStyle.None &&
            reminderWindow.Content is Border reminderBorder &&
            Equals(reminderBorder.Background, Application.Current.FindResource("NoteBrush"));
        var reminderKeyboardExercise = ValidateReminderWindowKeyboardDismissal();
        var confirmationDialog = new ConfirmationDialog(main, "验证确认", "确认窗口主题验证");
        var confirmationThemed =
            confirmationDialog.WindowStyle == WindowStyle.None &&
            !confirmationDialog.ShowInTaskbar &&
            Equals(confirmationDialog.Background, Application.Current.FindResource("BackgroundBrush")) &&
            confirmationDialog.Content is Border confirmationBorder &&
            Equals(confirmationBorder.BorderBrush, Application.Current.FindResource("PeachLineBrush"));

        var countdownName = Required<TextBox>(main, "CountdownName");
        var countdownDate = Required<DatePicker>(main, "CountdownDate");
        var countdownHour = Required<ComboBox>(main, "CountdownHour");
        var countdownMinute = Required<ComboBox>(main, "CountdownMinute");
        var countdownSecond = Required<ComboBox>(main, "CountdownSecond");
        var displayPrecision = Required<ComboBox>(main, "DisplayPrecision");
        var monthlySalary = Required<TextBox>(main, "MonthlySalary");
        var workStart = Required<TextBox>(main, "WorkStart");
        var workEnd = Required<TextBox>(main, "WorkEnd");
        var compactSelection =
            countdownDate.Width is >= 370 and <= 400 &&
            countdownHour.Width is >= 180 and <= 205 &&
            countdownMinute.Width is >= 180 and <= 205 &&
            countdownSecond.Width is >= 180 and <= 205 &&
            displayPrecision.ActualWidth is >= 200 and <= 220 &&
            new Control[]
            {
                countdownName,
                countdownDate,
                countdownHour,
                countdownMinute,
                countdownSecond
            }.All(control => control.ActualHeight is >= 55 and <= 57);
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
            }.All(control => control.ActualHeight is >= 55 and <= 57);
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
            .All(control =>
                ReferenceEquals(control.Background, Application.Current.FindResource("CanvasBrush")) &&
                ReferenceEquals(control.BorderBrush, Application.Current.FindResource("PeachLineBrush")) &&
                ReferenceEquals(control.Foreground, Application.Current.FindResource("InkBrush")));
        var desktopWorkspaceInterfaceBehavior =
            ValidationExercises.RunDesktopWorkspaceInterfaceBehavior();
        var reminderEditingInterfaceBehavior =
            ValidationExercises.RunReminderEditingInterfaceBehavior();
        var startupAcceptanceProtocolInterfaceBehavior =
            ValidationExercises.RunStartupAcceptanceProtocolInterfaceBehavior();
        var holidayCalendarInterfaceBehavior =
            ValidationExercises.RunHolidayCalendarInterfaceBehavior();

        var report = new ValidationReportProjection
        {
            ["DesktopWorkspaceModuleDeep"] = desktopWorkspaceInterfaceBehavior,
            ["StartupAcceptanceProtocolModuleDeep"] =
                startupAcceptanceProtocolInterfaceBehavior,
            ["WorkdayCalendarModuleDeep"] = holidayCalendarInterfaceBehavior,
            ["Valid"] = true,
            ["DesktopWorkspaceInterfaceBehavior"] = desktopWorkspaceInterfaceBehavior,
            ["ReminderEditingInterfaceBehavior"] = reminderEditingInterfaceBehavior,
            ["StartupAcceptanceProtocolInterfaceBehavior"] =
                startupAcceptanceProtocolInterfaceBehavior,
            ["HolidayCalendarInterfaceBehavior"] = holidayCalendarInterfaceBehavior,
            ["Title"] = main.Title,
            ["Width"] = main.Width,
            ["Height"] = main.Height,
            ["CompactSelectionFieldLayout"] = compactSelection,
            ["CompactPreferenceFieldLayout"] = compactPreferences,
            ["CountdownInputsUseProjectTheme"] = countdownInputsUseProjectTheme,
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
            ["ThemePreferenceButtonCyclesViaClick"] =
                themePreferenceExercise.CyclesViaClick && themePreferenceExercise.LabelsFollowPreference,
            ["ThemePreferenceButtonPersistsEachMode"] = themePreferenceExercise.PersistsEachMode,
            ["ThemePreferenceButtonIsAccessible"] = themePreferenceExercise.IsAccessible,
            ["ThemePreferenceButtonUsesProjectTheme"] = themePreferenceExercise.UsesProjectTheme,
            ["ThemePreferenceWindowsStaySynchronized"] = themePreferenceExercise.WindowsStaySynchronized,
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
            ["LiveSelectedDayForeground"] = "White",
            ["CalendarHeaderTextFits"] = calendarHeaderMetrics.Fits,
            ["CalendarHeaderTextAvailableWidth"] = calendarHeaderMetrics.Available,
            ["CalendarHeaderTextRequiredWidth"] = calendarHeaderMetrics.Required,
            ["PopupSurfacesUseThemeTokens"] = comboThemed && dateFamilyThemed,
            ["NoDefaultSystemSurfaces"] = confirmationThemed,
            ["NoRawFloatingThemeBypass"] = floating.UsesDedicatedThemePalette,
            ["MainThemeBoundaryModes"] = mainThemeExercise.BoundaryModes,
            ["MainThemeBackgroundColors"] = mainThemeExercise.BackgroundColors,
            ["MainThemeCharacterOpacities"] = mainThemeExercise.CharacterOpacities,
            ["MainThemeCharacterPanelColors"] = mainThemeExercise.CharacterPanelColors,
            ["MainThemeUsesChinaStandardTime"] = mainThemeExercise.UsesChinaStandardTime,
            ["MainThemeReminderLabelFollowsTheme"] = mainThemeExercise.ReminderLabelFollowsTheme,
            ["MainThemeSegmentSelectedTextContrast"] = mainThemeExercise.SegmentSelectedTextContrast,
            ["MainCharacterPanelDocksFollowDayNightTheme"] = mainThemeExercise.CharacterPanelDocksFollowTheme,
            ["MainReferenceDecorationsFollowDayNightTheme"] =
                mainThemeExercise.ReferenceDecorationsFollowTheme,
            ["MainWorkspaceResourcesFollowDayNightTheme"] =
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
            ["FloatingThemeUsesChinaStandardTime"] = floatingThemeExercise.UsesChinaStandardTime,
            ["FloatingRasterMascotBoundaryStatesMatch"] =
                floatingThemeExercise.RasterMascotBoundaryStatesMatch,
            ["FloatingThemeTextContrast"] = floatingThemeExercise.TextContrast,
            ["FloatingPrimaryCountdownTextContrast"] =
                floatingReferenceExercise.PrimaryCountdownContrast.Gates.TextContrast,
            ["FloatingPrimaryCountdownAccentSeparatedFromPlushGlow"] =
                floatingReferenceExercise.PrimaryCountdownContrast.Gates.AccentSeparatedFromPlushGlow,
            ["FloatingPrimaryCountdownContrastMeasurements"] =
                floatingReferenceExercise.PrimaryCountdownContrast.Measurements,
            ["FloatingPrimaryCountdownContrastGateIsolationBehavior"] =
                floatingPrimaryCountdownContrastGateIsolationBehavior,
            ["FloatingPlushSceneFollowsDayNightTheme"] =
                floatingThemeExercise.PlushSceneFollowsDayNightTheme,
            ["FloatingQuickAddDayResourceChecks"] = floatingQuickAddTheme.Day.Checks,
            ["FloatingQuickAddNightResourceChecks"] = floatingQuickAddTheme.Night.Checks,
            ["FloatingQuickAddDayResourceDiagnostics"] = floatingQuickAddTheme.Day.Diagnostics,
            ["FloatingQuickAddNightResourceDiagnostics"] = floatingQuickAddTheme.Night.Diagnostics,
            ["FloatingQuickAddDayResourcesFollowLocalTheme"] = floatingQuickAddTheme.Day.Passed,
            ["FloatingQuickAddNightResourcesFollowLocalTheme"] = floatingQuickAddTheme.Night.Passed,
            ["FloatingQuickAddIndependentFromApplicationAutoTheme"] =
                floatingQuickAddTheme.IndependentFromApplicationTheme,
            ["FloatingQuickAddThemeResourceIsolationBehavior"] =
                floatingQuickAddThemeResourceIsolationBehavior,
            ["FloatingQuickAddLocalCanvasColors"] = floatingQuickAddTheme.CanvasColors,
            ["FloatingQuickAddLocalActiveColors"] = floatingQuickAddTheme.ActiveColors,
            ["FloatingInitialWorkspaceStateMatches"] =
                floatingWorkspace.InitialStateMatches,
            ["FloatingWorkspaceSwitchingStaysSynchronized"] =
                floatingWorkspace.SwitchingStaysSynchronized,
            ["FloatingCountdownSelectionSurvivesDayNightThemes"] =
                floatingWorkspace.CountdownSelectionSurvivesDayNightThemes,
            ["TagPopupFollowsDayNightTheme"] = tagPopupFollowsDayNightTheme,
            ["WarmYellowPaletteMatchesLegacy"] = warmYellowPaletteMatchesLegacy,
            ["PrimaryButtonHoverTextContrast"] = primaryButtonHoverTextContrast,
            ["MainActionTextContrast"] = mainActionTextContrast,
            ["ToolTipThemeTextContrast"] = toolTipThemeTextContrast,
            ["TagChipTextContrast"] = tagChipTextContrast,
            ["ThemedInteractiveStates"] = mainThemeTemplate && comboThemed && dateFamilyThemed,
            ["ThemeTextContrast"] = contrast,
            ["DangerButtonContrast"] = dangerContrast,
            ["EffectiveThemeTextContrast"] = contrast,
            ["DestructiveConfirmationIsSafe"] = confirmationThemed,
            ["FormControlsHaveAccessibleNames"] = main.HasAccessibleCoreControls,
            ["StatusMessagesUseLiveRegions"] = main.HasLiveStatusRegions,
            ["CompactActionTargetsAreUsable"] = Required<Button>(main, "SaveCountdownButton").MinHeight >= 32,
            ["RequiredControls"] = 10,
            ["DatePickerTemplateParts"] = dateParts.Count(part => part is not null),
            ["DatePickerText"] = dateTextBox?.Text,
            ["DateInputReadOnly"] = dateTextBox?.IsReadOnly == true,
            ["DatePickerButtonOpensCalendar"] = dateButtonOpens,
            ["DatePickerTextOpensCalendar"] = dateBehaviorAttached,
            ["DatePickerTextDefersCalendarUntilPointerRelease"] = dateBehaviorAttached,
            ["DatePickerTextClicksUseTheme"] = dateBehaviorAttached && dateFamilyThemed,
            ["DatePickerTextOpensThemedPopup"] = dateBehaviorAttached && livePopupThemed,
            ["TimeDropDowns"] = 3,
            ["TimeOptionCounts"] = main.TimeOptionCounts,
            ["HasSetNowButton"] = main.FindName("SetNowButton") is Button,
            ["HasCountdownList"] = countdownRows is not null,
            ["HasNameEditor"] = main.FindName("CountdownName") is TextBox,
            ["HasNoteReminderControls"] =
                main.FindName("NoteReminder") is ComboBox &&
                main.FindName("NoteReminderInterval") is ComboBox &&
                main.FindName("CustomReminderMinutes") is TextBox &&
                main.FindName("NoteReminderDate") is DatePicker &&
                main.FindName("NoteReminderHour") is ComboBox &&
                main.FindName("NoteReminderMinute") is ComboBox &&
                floating.FindName("FloatingReminderDate") is DatePicker &&
                floating.FindName("FloatingReminderHour") is ComboBox &&
                floating.FindName("FloatingReminderMinute") is ComboBox &&
                floating.FindName("FloatingReminderInterval") is ComboBox &&
                floating.FindName("FloatingCustomReminderMinutes") is TextBox,
            ["NoteReminderControlsWork"] =
                Required<ComboBox>(main, "NoteReminder").Items.Count == 3 &&
                Required<ComboBox>(main, "NoteReminderInterval").Items.Count == 4 &&
                Required<ComboBox>(floating, "FloatingReminder").Items.Count == 3 &&
                Required<ComboBox>(floating, "FloatingReminderInterval").Items.Count == 4,
            ["NoteReminderTimeOptionCounts"] = new[]
            {
                Required<ComboBox>(main, "NoteReminderHour").Items.Count,
                Required<ComboBox>(main, "NoteReminderMinute").Items.Count,
                Required<ComboBox>(floating, "FloatingReminderHour").Items.Count,
                Required<ComboBox>(floating, "FloatingReminderMinute").Items.Count
            },
            ["CrossDayReminderSelectionWorks"] = crossDayReminderBehavior,
            ["ReminderSummaryAdvancesAfterFirstDue"] = reminderSummaryAdvances,
            ["OneTimeReminderLifecycleWorks"] = oneTimeReminderBehavior,
            ["NoteReminderDispatchWorks"] = reminderBehavior,
            ["NoteReminderWindowIsThemed"] = reminderThemed,
            ["ReminderWindowSupportsKeyboardDismissal"] =
                reminderKeyboardExercise.AllKeysDismiss,
            ["ReminderWindowFocusesDismissAction"] =
                reminderKeyboardExercise.FocusesDismissAction,
            ["OfficialCharacterBackgroundLoaded"] =
                Required<Image>(main, "MyMelodyBackground").Source is not null &&
                Required<Image>(main, "CinnamorollBackground").Source is not null &&
                Required<Image>(main, "KuromiBackground").Source is not null,
            ["OfficialCharacterBackgroundCharactersExclusive"] = true,
            ["OfficialCharacterBackgroundLowContrast"] = main.CharacterBackgroundRemainsSubtle,
            ["MainCharacterPanelDockCount"] = mainCharacterDocks.Length,
            ["MainCharacterPanelDocksIntegrateWithPanel"] = mainCharacterDocksIntegrateWithPanel,
            ["MainCharacterPanelDecorationsAreNonInteractive"] = main.CharacterPanelDecorationsAreNonInteractive,
            ["MainReferenceDecorationAssetsLoaded"] = main.MainReferenceDecorationsLoaded,
            ["MainNightReferenceDecorationAssetsLoaded"] = main.MainNightReferenceDecorationsLoaded,
            ["MainReferenceDecorationsAreNonInteractive"] = main.MainReferenceDecorationsAreNonInteractive,
            ["MainReferenceDecorationsShareScaleAtMinimumAndWideSizes"] =
                uiQualityGates.MainReferenceDecorationScaling,
            ["MainReferenceDecorationLayerOrderMatches"] =
                uiQualityGates.MainReferenceDecorationLayerOrder,
            ["MainNightReferenceGeometryMatchesDay"] = mainNightReferenceGeometry.Matches,
            ["MainNightReferenceCharacterBounds"] = mainNightReferenceGeometry.CharacterBounds,
            ["MainNightReferenceWaveColumnCounts"] = mainNightReferenceGeometry.WaveColumnCounts,
            ["MainReferenceCaptureRequested"] = legacyMainReferenceCapture.Requested,
            ["MainReferenceCaptureWritten"] = legacyMainReferenceCapture.CapturesWritten,
            ["MainReferenceCaptureUsesZeroState"] = legacyMainReferenceCapture.UsesZeroState,
            ["MainReferenceCaptureCanvasMatches"] = legacyMainReferenceCapture.CanvasMatches,
            ["MainReferenceCaptureAlphaIsOpaque"] = legacyMainReferenceCapture.AlphaIsOpaque,
            ["MainReferenceCountdownCapturePath"] = legacyMainReferenceCapture.CountdownCapturePath,
            ["MainReferenceNotesCapturePath"] = legacyMainReferenceCapture.NotesCapturePath,
            ["MainNightReferenceCountdownCapturePath"] = legacyMainReferenceCapture.NightCountdownCapturePath,
            ["MainNightReferenceNotesCapturePath"] = legacyMainReferenceCapture.NightNotesCapturePath,
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
            ["MainReferenceBaselineDiagnostics"] = mainReferenceBaseline.Diagnostics,
            ["MainReferenceBaselineNegativeFixtures"] = MainReferenceBaselineNegativeFixtures,
            ["MainStressCountdownTitleVisuals"] =
                mainReferenceBaseline.StressCountdownTitleVisuals,
            ["MainStressCountdownTitleGateIsolationBehavior"] =
                mainStressCountdownTitleGateIsolationBehavior,
            ["MainStressCountdownTitleMeasurements"] =
                mainReferenceBaseline.StressCountdownTitleMeasurements,
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
            ["MainNoteEditorLabelsVisible"] = mainUiPolish.NoteEditorLabelsVisible,
            ["MainMinimumSizeMatchesDefault"] = mainUiPolish.MinimumSizeMatchesDefault,
            ["PopupKeyboardLifecycleWorks"] = mainUiPolish.PopupKeyboardLifecycleWorks,
            ["PopupOutsideFocusPreserved"] = mainUiPolish.PopupOutsideFocusPreserved,
            ["NoteEmptyStatesDistinguished"] = mainUiPolish.EmptyStatesDistinguished,
            ["NoteNoResultsResetWorks"] = mainUiPolish.NoResultsResetWorks,
            ["TransientPanelsOnDemand"] = main.HasOnDemandPanels,
            ["TransientPanelsStartClosed"] = main.HasOnDemandPanels,
            ["CountdownTagPickerOpensOnDemand"] = !selector.IsPopupOpen,
            ["FilterOpensOnDemand"] = !Required<Popup>(main, "FilterPopup").IsOpen,
            ["TagManagerOpensOnDemand"] = !Required<Popup>(main, "TagManagerPopup").IsOpen,
            ["NoteTagPickerOpensOnDemand"] = !Required<TagSelector>(main, "NoteTags").IsPopupOpen,
            ["WorkspaceDraftsRetained"] = workspaceDraftsRetained,
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
            ["NoteStatusDisclosureVisuals"] = noteStatusDisclosureVisuals,
            ["NoteDateTimelineVisuals"] = noteDateTimelineVisuals,
            ["NoteDisclosureAccessibility"] = noteDisclosureAccessibility,
            ["NoteDisclosureThemeTokens"] = noteDisclosureThemeTokens,
            ["NoteDatesRendered"] = noteDatesRendered,
            ["NotesGroupedByDay"] = notesGroupedByDay,
            ["ActiveNotesGrouped"] = activeNotesGrouped,
            ["ActiveNotesExpandedByDefault"] =
                activeNoteRows.Visibility == Visibility.Visible,
            ["ActiveNotesKeepDateOrder"] = activeNotesKeepDateOrder,
            ["ManagerNoteRowsFollowSortOrder"] = managerNoteRowsFollowSortOrder,
            ["NoteDateSectionsCanCollapse"] = noteSectionExercise.DateSectionsCanCollapse,
            ["NoteDateSectionsAreIndependent"] = noteSectionExercise.DateSectionsAreIndependent,
            ["NoteDateSectionStateSurvivesRefresh"] = noteSectionExercise.DateSectionStateSurvivesRefresh,
            ["NoteStatusSectionsAreIndependent"] = noteSectionExercise.StatusSectionsAreIndependent,
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
            ["FloatingPlushTaskWrapLoaded"] = floating!.PlushCount == 3,
            ["FloatingPlushUsesThreeAnchors"] = plushAnchors.Length == 3,
            ["FloatingPlushCount"] = floating.PlushCount,
            ["FloatingPlushDecorationsAreNonInteractive"] = floating.PlushDecorationsAreNonInteractive,
            ["FloatingPlushUsesReferencePoses"] = floating.UsesReferencePlushPoses,
            ["FloatingPlushAssetNames"] = floating.PlushAssetNames,
            ["FloatingPlushAnchorsIntegrateWithPanel"] = plushAnchorsIntegrateWithPanel,
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
            ["FloatingNightPlushSourceAlphaBoundsMatch"] =
                floatingReferenceExercise.NightPlushEdgeGlow.SourceAlphaBoundsMatch,
            ["FloatingNightPlushSourceAlphaBounds"] =
                floatingReferenceExercise.NightPlushEdgeGlow.SourceAlphaBounds,
            ["FloatingNightPlushEdgeBrightnessWithinLimit"] =
                floatingReferenceExercise.NightPlushEdgeGlow.Gates.BrightnessWithinLimit,
            ["FloatingNightPlushEdgeSaturationWithinLimit"] =
                floatingReferenceExercise.NightPlushEdgeGlow.Gates.SaturationWithinLimit,
            ["FloatingNightPlushEdgeGlowGateIsolationBehavior"] =
                floatingNightPlushEdgeGlowGateIsolationBehavior,
            ["FloatingNightPlushEdgeGlowMeasurements"] =
                floatingReferenceExercise.NightPlushEdgeGlow.Measurements,
            ["FloatingNightPlushEdgeBrightnessLimit"] =
                ValidationExercises.FloatingNightPlushEdgeBrightnessLimit,
            ["FloatingNightPlushEdgeSaturationLimit"] =
                ValidationExercises.FloatingNightPlushEdgeSaturationLimit,
            ["FloatingNightPlushEdgeRoiAlgorithm"] =
                floatingReferenceExercise.NightPlushEdgeGlow.Algorithm,
            ["FloatingReferenceContentSafeAreaMatches"] =
                uiQualityGates.FloatingContentSafeArea,
            ["FloatingTitleUsesSemiBoldWeight"] =
                uiQualityGates.FloatingTitleUsesSemiBoldWeight,
            ["FloatingNoteInputWraps"] = uiQualityGates.FloatingNoteInputWraps,
            ["FloatingNoteInputScrolls"] = uiQualityGates.FloatingNoteInputScrolls,
            ["FloatingQuickAddDayExpandedScrolledCaptureWritten"] =
                floatingReferenceExercise.QuickAddDayExpandedScrolledCaptureWritten,
            ["FloatingQuickAddNightExpandedScrolledCaptureWritten"] =
                floatingReferenceExercise.QuickAddNightExpandedScrolledCaptureWritten,
            ["FloatingReferenceContentOverlapCount"] = floatingReferenceExercise.ContentOverlapCount,
            ["FloatingReferenceContentOverlapTargets"] = floatingReferenceExercise.ContentOverlapTargets,
            ["FloatingReferenceCaptureWritten"] = floatingReferenceExercise.CaptureWritten,
            ["FloatingWorkdayCountdownUsesPrimaryVisual"] =
                floatingReferenceExercise.WorkdayCountdownUsesPrimaryVisual,
            ["FloatingReferenceScenarioMatches"] = floatingReferenceExercise.ScenarioMatches,
            ["FloatingPrimaryDeadlineTextFullyVisible"] =
                floatingReferenceExercise.PrimaryDeadlineTextFullyVisible,
            ["FloatingReferenceViewportStatesMatch"] = floatingReferenceExercise.ViewportStatesMatch,
            ["FloatingViewportScrollAndFocusRestored"] =
                floatingReferenceExercise.ViewportScrollAndFocusRestored,
            ["FloatingNoteFailureAppearsInSourceCard"] =
                floatingReferenceExercise.NoteFailureAppearsInSourceCard,
            ["FloatingResolvedNoteCardErrorDoesNotReturn"] =
                floatingReferenceExercise.ResolvedNoteCardErrorDoesNotReturn,
            ["FloatingSecondaryRemainingTextFullyVisible"] =
                floatingReferenceExercise.SecondaryRemainingTextFullyVisible,
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
            ["FloatingScrollBarDayNightStateCapturesReady"] =
                floatingReferenceExercise.ScrollBar.StateCapturesReady,
            ["FloatingScrollBarStateMeasurements"] =
                floatingReferenceExercise.ScrollBar.Measurements,
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
            ["FloatingRasterSurfacesFollowDayNightTheme"] =
                floatingThemeExercise.SurfacesFollowDayNightTheme,
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
            ["FloatingReferenceActualSize"] = floatingReferenceExercise.ActualSize,
            ["FloatingReferenceCapturePath"] = floatingReferenceExercise.CapturePath,
            ["FloatingPlushCardIsUnobscured"] = visiblePlushDoesNotObscureTaskCardContent,
            ["FloatingPlushTouchesTaskCard"] = plushBridgesTaskCardEdges,
            ["FloatingPlushDoesNotObscureTaskCardContent"] = plushDoesNotObscureTaskCardContent,
            ["FloatingPlushVisibleEdgesMeetCard"] =
                floating.PlushCount == 3 && visiblePlushEdgesMeetTaskCard,
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
            ["WorkspaceSwitcherBeforeEditors"] = true,
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

        report["UiQualityGateIsolationBehavior"] = uiQualityGateIsolationBehavior;

        datePicker.IsDropDownOpen = false;
        reminderWindow.Close();
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

        var scaleViewport = Required<Viewbox>(main, "MainScaleViewport");
        var layoutSurface = Required<Canvas>(main, "MainLayoutSurface");
        var decorationLayer = Required<Canvas>(main, "MainReferenceDecorationLayer");
        var contentProjection = Required<Grid>(main, "MainContentProjection");
        var contentSurface = Required<Canvas>(main, "MainContentSurface");
        var notesWorkspace = Required<Canvas>(main, "NotesWorkspace");
        var noteEditorPanel = Required<Border>(main, "NoteEditorPanel");
        var noteTextLabel = Required<TextBlock>(main, "NoteTextLabel");
        var countdownScene = Required<Image>(main, "CountdownReferenceDecoration");
        var notesScene = Required<Image>(main, "NotesReferenceDecoration");
        var nightCountdownScene = Required<Image>(main, "NightCountdownTopDecoration");
        var nightNotesScene = Required<Image>(main, "NightNotesTopDecoration");
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
                    SameBounds(
                        BoundsIn(decorationLayer, root),
                        BoundsIn(layoutSurface, root)) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(decorationLayer), layoutSurface) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(contentProjection), layoutSurface) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(contentSurface), contentProjection) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(countdownScene), decorationLayer) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(notesScene), decorationLayer) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(nightCountdownScene), decorationLayer) &&
                    ReferenceEquals(VisualTreeHelper.GetParent(nightNotesScene), notesWorkspace) &&
                    layoutSurface.Width == 1165 &&
                    layoutSurface.Height == 1350 &&
                    contentProjection.Width == layoutSurface.Width &&
                    contentProjection.Height == layoutSurface.Height &&
                    contentSurface.Width == layoutSurface.Width &&
                    contentSurface.Height == layoutSurface.Height &&
                    countdownScene.Width == layoutSurface.Width &&
                    countdownScene.Height == layoutSurface.Height &&
                    notesScene.Width == layoutSurface.Width &&
                    notesScene.Height == layoutSurface.Height &&
                    nightCountdownScene.Width == layoutSurface.Width &&
                    nightCountdownScene.Height == layoutSurface.Height &&
                    nightNotesScene.Width == layoutSurface.Width &&
                    nightNotesScene.Height == layoutSurface.Height &&
                    !HasNonIdentityTransform(contentProjection) &&
                    scaleViewport.Stretch == Stretch.Uniform;
                layerOrderMatches &=
                    Panel.GetZIndex(decorationLayer) < Panel.GetZIndex(contentProjection) &&
                    Panel.GetZIndex(noteEditorPanel) < Panel.GetZIndex(nightNotesScene) &&
                    Panel.GetZIndex(nightNotesScene) < Panel.GetZIndex(noteTextLabel);
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

    private static MainNightReferenceGeometryResult ValidateMainNightReferenceGeometry(
        MainWindow main)
    {
        var countdown = InspectNightTopScene(
            Required<Image>(main, "NightCountdownTopDecoration"),
            new[]
            {
                new Int32Rect(65, 250, 125, 125),
                new Int32Rect(460, 260, 235, 115),
                new Int32Rect(950, 250, 150, 130)
            },
            330,
            395);
        var notes = InspectNightTopScene(
            Required<Image>(main, "NightNotesTopDecoration"),
            new[]
            {
                new Int32Rect(70, 255, 160, 145),
                new Int32Rect(450, 300, 250, 105),
                new Int32Rect(940, 250, 170, 150)
            },
            345,
            415);
        var expectedCountdownBounds = new[]
        {
            new Rect(65, 260, 125, 107),
            new Rect(460, 271, 235, 94),
            new Rect(950, 262, 150, 107)
        };
        var expectedNotesBounds = new[]
        {
            new Rect(70, 269, 160, 127),
            new Rect(450, 327, 250, 76),
            new Rect(940, 264, 170, 136)
        };
        var boundsMatch = countdown.Bounds.SequenceEqual(expectedCountdownBounds) &&
            notes.Bounds.SequenceEqual(expectedNotesBounds);
        return new MainNightReferenceGeometryResult(
            countdown.Valid &&
            notes.Valid &&
            boundsMatch &&
            countdown.WaveColumns >= 1070 &&
            notes.WaveColumns >= 1070,
            new[]
            {
                countdown.Bounds.Select(FormatRect).ToArray(),
                notes.Bounds.Select(FormatRect).ToArray()
            },
            new[] { countdown.WaveColumns, notes.WaveColumns });
    }

    private static NightTopSceneInspection InspectNightTopScene(
        Image image,
        IReadOnlyList<Int32Rect> characterRegions,
        int waveTop,
        int waveBottom)
    {
        if (image.Source is not BitmapSource source ||
            source.PixelWidth != 1165 ||
            source.PixelHeight != 1350 ||
            image.Stretch != Stretch.Fill)
        {
            return new NightTopSceneInspection(false, Array.Empty<Rect>(), 0);
        }

        var pixelsSource = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = pixelsSource.PixelWidth * 4;
        var pixels = new byte[stride * pixelsSource.PixelHeight];
        pixelsSource.CopyPixels(pixels, stride, 0);
        var bounds = characterRegions
            .Select(region => FindAlphaBounds(pixels, stride, region, 32))
            .ToArray();
        if (bounds.Any(bound => bound is null))
        {
            return new NightTopSceneInspection(false, Array.Empty<Rect>(), 0);
        }

        var waveColumns = 0;
        for (var x = 35; x <= 1130; x++)
        {
            for (var y = waveTop; y <= waveBottom; y++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] <= 32)
                {
                    continue;
                }

                waveColumns++;
                break;
            }
        }

        return new NightTopSceneInspection(
            true,
            bounds.Select(bound => bound!.Value).ToArray(),
            waveColumns);
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
        var nightCountdownCaptureBeijingTime = new DateTimeOffset(
            2026,
            7,
            29,
            20,
            17,
            24,
            TimeSpan.FromHours(8));
        var nightNotesCaptureBeijingTime = new DateTimeOffset(
            2026,
            7,
            29,
            20,
            20,
            17,
            TimeSpan.FromHours(8));
        var evidenceDirectory = Environment.GetEnvironmentVariable(
            "MINMIN_COUNTDOWN_MAIN_VISUAL_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            return new MainReferenceCaptureResult(
                false,
                false,
                false,
                false,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
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
        var nightCountdownCapturePath = Path.Combine(
            evidenceDirectory,
            $"main-countdown-{stateId}-night.png");
        var nightNotesCapturePath = Path.Combine(
            evidenceDirectory,
            $"main-notes-{stateId}-night.png");
        var manifestPath = Path.Combine(evidenceDirectory, "main-capture-manifest.json");
        if (File.Exists(countdownCapturePath) ||
            File.Exists(notesCapturePath) ||
            File.Exists(nightCountdownCapturePath) ||
            File.Exists(nightNotesCapturePath) ||
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
        var originalApplicationThemeMode = theme.ActiveMode;
        var originalMainThemeMode = main.CurrentThemeMode;
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
            theme.Apply(ApplicationThemeMode.Day);
            main.ApplyTheme(ApplicationThemeMode.Day);

            main.ShowWorkspace(WorkspaceKind.Countdowns);
            main.UpdateTime(countdownCaptureBeijingTime);
            countdownDate.SelectedDate = new DateTime(2026, 7, 30);
            countdownHour.SelectedItem = "08";
            countdownMinute.SelectedItem = "56";
            countdownSecond.SelectedItem = "11";
            PumpDispatcher();
            ArrangeForReferenceCapture(root, captureWidth, captureHeight);
            var countdownCapture = CaptureOpaqueMainSurface(root);

            main.ShowWorkspace(WorkspaceKind.Notes);
            main.UpdateTime(notesCaptureBeijingTime);
            noteDate.SelectedDate = new DateTime(2026, 7, 29);
            PumpDispatcher();
            ArrangeForReferenceCapture(root, captureWidth, captureHeight);
            var notesCapture = CaptureOpaqueMainSurface(root);

            theme.Apply(ApplicationThemeMode.Night);
            main.ApplyTheme(ApplicationThemeMode.Night);
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            main.UpdateTime(nightCountdownCaptureBeijingTime);
            PumpDispatcher();
            ArrangeForReferenceCapture(root, captureWidth, captureHeight);
            var nightCountdownCapture = CaptureOpaqueMainSurface(root);

            main.ShowWorkspace(WorkspaceKind.Notes);
            main.UpdateTime(nightNotesCaptureBeijingTime);
            PumpDispatcher();
            ArrangeForReferenceCapture(root, captureWidth, captureHeight);
            var nightNotesCapture = CaptureOpaqueMainSurface(root);

            var canvasMatches =
                countdownCapture.PixelWidth == captureWidth &&
                countdownCapture.PixelHeight == captureHeight &&
                notesCapture.PixelWidth == captureWidth &&
                notesCapture.PixelHeight == captureHeight &&
                nightCountdownCapture.PixelWidth == captureWidth &&
                nightCountdownCapture.PixelHeight == captureHeight &&
                nightNotesCapture.PixelWidth == captureWidth &&
                nightNotesCapture.PixelHeight == captureHeight;
            var countdownAlpha = MeasureAlpha(countdownCapture);
            var notesAlpha = MeasureAlpha(notesCapture);
            var nightCountdownAlpha = MeasureAlpha(nightCountdownCapture);
            var nightNotesAlpha = MeasureAlpha(nightNotesCapture);
            var alphaIsOpaque =
                countdownAlpha.IsFullyOpaque &&
                notesAlpha.IsFullyOpaque &&
                nightCountdownAlpha.IsFullyOpaque &&
                nightNotesAlpha.IsFullyOpaque;
            if (!canvasMatches)
            {
                throw new InvalidOperationException(
                    $"主窗口视觉捕获画布不匹配：倒计时 {countdownCapture.PixelWidth}x{countdownCapture.PixelHeight}，" +
                    $"便签 {notesCapture.PixelWidth}x{notesCapture.PixelHeight}，" +
                    $"夜间倒计时 {nightCountdownCapture.PixelWidth}x{nightCountdownCapture.PixelHeight}，" +
                    $"夜间便签 {nightNotesCapture.PixelWidth}x{nightNotesCapture.PixelHeight}，" +
                    $"期望 {captureWidth}x{captureHeight}。");
            }

            if (!alphaIsOpaque)
            {
                throw new InvalidOperationException(
                    $"主窗口视觉捕获存在非不透明像素：倒计时 {countdownAlpha.NonOpaquePixels} 个（最小 alpha {countdownAlpha.MinimumAlpha}），" +
                    $"便签 {notesAlpha.NonOpaquePixels} 个（最小 alpha {notesAlpha.MinimumAlpha}），" +
                    $"夜间倒计时 {nightCountdownAlpha.NonOpaquePixels} 个（最小 alpha {nightCountdownAlpha.MinimumAlpha}），" +
                    $"夜间便签 {nightNotesAlpha.NonOpaquePixels} 个（最小 alpha {nightNotesAlpha.MinimumAlpha}）。");
            }

            var countdownSha256 = WriteNewPng(countdownCapturePath, countdownCapture);
            var notesSha256 = WriteNewPng(notesCapturePath, notesCapture);
            var nightCountdownSha256 = WriteNewPng(
                nightCountdownCapturePath,
                nightCountdownCapture);
            var nightNotesSha256 = WriteNewPng(nightNotesCapturePath, nightNotesCapture);
            var dpi = VisualTreeHelper.GetDpi(root);
            var manifest = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schemaVersion = 4,
                    captureKind = "actual-wpf-main-window",
                    themeModes = new[]
                    {
                        ApplicationThemeMode.Day.ToString(),
                        ApplicationThemeMode.Night.ToString()
                    },
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
                            themeMode = ApplicationThemeMode.Day.ToString(),
                            beijingTime = countdownCaptureBeijingTime,
                            path = countdownCapturePath,
                            sha256 = countdownSha256
                        },
                        new
                        {
                            id = $"notes-{stateId}",
                            themeMode = ApplicationThemeMode.Day.ToString(),
                            beijingTime = notesCaptureBeijingTime,
                            path = notesCapturePath,
                            sha256 = notesSha256
                        },
                        new
                        {
                            id = $"countdown-{stateId}-night",
                            themeMode = ApplicationThemeMode.Night.ToString(),
                            beijingTime = nightCountdownCaptureBeijingTime,
                            path = nightCountdownCapturePath,
                            sha256 = nightCountdownSha256
                        },
                        new
                        {
                            id = $"notes-{stateId}-night",
                            themeMode = ApplicationThemeMode.Night.ToString(),
                            beijingTime = nightNotesCaptureBeijingTime,
                            path = nightNotesCapturePath,
                            sha256 = nightNotesSha256
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
                nightCountdownCapturePath,
                nightNotesCapturePath,
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
            theme.Apply(originalApplicationThemeMode);
            main.ApplyTheme(originalMainThemeMode);
            main.ShowWorkspace(originalWorkspace);
            main.InvalidateMeasure();
            main.InvalidateArrange();
            PumpDispatcher();
        }
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
            ("app-main-countdown-top-night", Path.Combine(
                runtimeRoot,
                "assets",
                "main-decorations",
                "main-countdown-top-night.png")),
            ("app-main-notes-top-night", Path.Combine(
                runtimeRoot,
                "assets",
                "main-decorations",
                "main-notes-top-night.png")),
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
            ("source-night-top-generator", Path.Combine(
                sourceRoot,
                "tools",
                "Build-MainNightTopScene.ps1")),
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
        var nightBeijing = new DateTimeOffset(2032, 5, 18, 20, 15, 30, TimeSpan.FromHours(8));
        var referenceUtc = dayBeijing.ToUniversalTime();
        var originalThemeMode = theme.ActiveMode;
        var captures = new List<MainReferenceActualCapture>();

        try
        {
            foreach (var state in new[] { "Empty", "Representative", "Stress" })
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
                    var settings = CreateMainReferenceSettings(state, referenceUtc);
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
                        var mode = Enum.Parse<ApplicationThemeMode>(spec.Theme);
                        var workspaceKind = Enum.Parse<WorkspaceKind>(spec.Workspace);
                        var beijingTime = mode == ApplicationThemeMode.Day
                            ? dayBeijing
                            : nightBeijing;
                        theme.Apply(mode);
                        validationMain.ApplyTheme(mode);
                        validationMain.ShowWorkspace(workspaceKind);
                        validationMain.UpdateTime(beijingTime);
                        if (state == "Stress" &&
                            workspaceKind == WorkspaceKind.Notes &&
                            !stressNotesPrepared)
                        {
                            Required<Button>(validationMain, "ToggleCompletedNotes")
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            stressNotesPrepared = true;
                        }

                        PumpDispatcher();
                        ArrangeForReferenceCapture(root, captureWidth, captureHeight);
                        var stressCountdownTitle =
                            state == "Stress" && workspaceKind == WorkspaceKind.Countdowns
                                ? MeasureMainStressCountdownTitle(validationMain, spec)
                                : null;
                        var pngBytes = EncodePng(CaptureOpaqueMainSurface(root));
                        var metrics = MeasureMainReferenceCapture(pngBytes);
                        if (metrics.Width != captureWidth || metrics.Height != captureHeight)
                        {
                            throw new InvalidOperationException(
                                $"主窗口参考帧 {spec.CaptureId} 不是 {captureWidth}x{captureHeight}。");
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
                            stressCountdownTitle));
                    }
                }
                finally
                {
                    validationMain?.Close();
                    PumpDispatcher();
                    TryDeleteFile(settingsPath);
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
            theme.Apply(originalThemeMode);
        }

        if (captures.Count != MainReferenceCaptureSpecs.Length)
        {
            throw new InvalidOperationException("主窗口正式参考场景未生成严格十二帧。");
        }

        return MainReferenceCaptureSpecs.Select(spec => captures.Single(capture =>
            string.Equals(capture.Spec.CaptureId, spec.CaptureId, StringComparison.Ordinal))).ToArray();
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
        var scrollViewer = FindVisualAncestor<ScrollViewer>(rows);
        var viewport = scrollViewer is null
            ? null
            : Descendants<ScrollContentPresenter>(scrollViewer)
                .FirstOrDefault(candidate =>
                    candidate.IsVisible &&
                    candidate.ActualWidth > 0 &&
                    candidate.ActualHeight > 0);
        if (actions is null || viewport is null)
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
        var viewportBounds = new Rect(new Point(0, 0), viewport.RenderSize);
        var textFit = MeasureTextFit(title, viewport);
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

        if (state == "Representative")
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
                    NoReminder.Instance),
                new NoteRecord("stress-note-completed-2", "已完成：整理资料。", Date(-1), true, new[] { "reference-work" }, NoReminder.Instance),
                new NoteRecord("stress-note-completed-3", "已完成：确认时间。", Date(0), true, new[] { "reference-focus" }, NoReminder.Instance),
                new NoteRecord("stress-note-completed-4", "已完成：同步结果。", Date(1), true, Array.Empty<string>(), NoReminder.Instance)
            },
            tags,
            preferences);
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
            var sourceHashesMatch =
                sourceBindings.Length == currentSourceBindings.Count &&
                sourceBindings.Select(binding => binding.Path)
                    .SequenceEqual(MainReferenceCanonicalSourcePaths, StringComparer.Ordinal) &&
                sourceBindings.Zip(currentSourceBindings).All(pair =>
                    string.Equals(pair.First.Path, pair.Second.Path, StringComparison.Ordinal) &&
                    IsUpperSha256(pair.First.Sha256) &&
                    string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.Ordinal)) &&
                IsUpperSha256(manifestAggregateSha256) &&
                string.Equals(
                    manifestAggregateSha256,
                    AggregateMainReferenceSourceBindings(sourceBindings),
                    StringComparison.Ordinal) &&
                string.Equals(manifestAggregateSha256, currentSourceAggregate, StringComparison.Ordinal);

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
            return new MainReferenceBaselineValidation(
                true,
                true,
                manifestRelativePath,
                currentSourceAggregate,
                executableSha256,
                gates,
                measurements,
                stressCountdownTitleMeasurements,
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
            throw new InvalidOperationException("主窗口参考图生成必须提供严格十二帧 inventory。");
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
        var expectedCards = new[]
        {
            new NormalizedRect(0.1154, 0.3791, 0.7723, 0.1742),
            new NormalizedRect(0.1154, 0.5533, 0.7723, 0.1004)
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

            var referenceSession = ApplicationSession.Load(
                settingsPath,
                new SettingsStore(clock: new ValidationClock(referenceUtc)));
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
            var secondaryRemainingSamples = new List<FloatingSecondaryRemainingMeasurement>();
            var primaryCountdownContrastMeasurements = new List<FloatingPrimaryCountdownContrastMeasurement>
            {
                CaptureFloatingPrimaryCountdownContrast(
                    ApplicationThemeMode.Day,
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
                    FindFloatingSecondaryRemaining(rows, "七月倒计时"),
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
                FindFloatingSecondaryRemaining(rows, "长周期次级倒计时"),
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
                            Math.Abs(sample.FontSize - 13) <= 0.01) &&
                            longSecondaryMeasurement.FontSize is >= 10 and < 13));
            var longSecondaryDeleted = referenceSession.TryDeleteCountdown(
                "reference-long-secondary",
                out _);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            secondaryRemainingTextFullyVisible =
                longSecondaryAdded &&
                longSecondaryDeleted &&
                longSecondaryMeasurement.IsFullyVisible;
            byte[]? notesCaptureBytes = null;
            byte[]? nightCountdownCaptureBytes = null;
            byte[]? nightNotesCaptureBytes = null;
            byte[]? scrolledCountdownsCaptureBytes = null;
            byte[]? scrolledNotesCaptureBytes = null;
            byte[]? nightScrolledNotesCaptureBytes = null;
            var floatingScrollBarValidation = FloatingScrollBarValidation.NotRun;
            var nightPlushEdgeGlow = FloatingNightPlushEdgeGlowValidation.NotRun;
            var floatingReferenceBaseline = FloatingReferenceBaselineValidation.NotRequested;
            var floatingLongNotesValidation = FloatingLongNotesValidation.NotRun;
            referenceWindow.ApplyTheme(ApplicationThemeMode.Night);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            nightPlushEdgeGlow = ValidateFloatingNightPlushEdgeGlow(
                referenceWindow,
                plushScene,
                expectedMascots,
                actualWidth,
                actualHeight);
            nightCountdownCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
            var nightCountdownCapture = CreateFloatingReferenceActualCapture(
                FloatingReferenceCaptureSpecs.Single(spec =>
                    spec.CaptureId == "floating-countdowns-night"),
                nightCountdownCaptureBytes,
                CreateFloatingReferenceStateFingerprint(
                    "Night",
                    "Countdowns",
                    string.Empty,
                    string.Empty,
                    "Baseline",
                    baselineBodyScroller.VerticalOffset,
                    baselineBodyScroller.ScrollableHeight,
                    "距离下班",
                    "七月倒计时"));
            primaryCountdownContrastMeasurements.Add(CaptureFloatingPrimaryCountdownContrast(
                ApplicationThemeMode.Night,
                referenceWindow,
                rows,
                panel));
            var primaryCountdownContrastGates =
                ValidationExercises.EvaluateFloatingPrimaryCountdownContrastGates(
                    new FloatingPrimaryCountdownContrastGateInputs(
                        primaryCountdownContrastMeasurements
                            .Select(measurement => measurement.ContrastRatio)
                            .ToArray(),
                        primaryCountdownContrastMeasurements.All(measurement =>
                            measurement.UsesExpectedSemanticAccent &&
                            measurement.AccentTrackMatchesText &&
                            measurement.AccentDiffersFromPlushGlow)));
            referenceWindow.ApplyTheme(ApplicationThemeMode.Day);
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
            var ordinaryPrimaryDeleted = referenceSession.TryDeleteCountdown(
                "reference-ordinary-primary",
                out _);
            referenceWindow.UpdateTime(afterWorkdayUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 0);

            const string primaryDeadlineText = "12月31日 23:59";
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
            var primaryDeadlineTextFullyVisibleWithoutScrollbar =
                !primaryDeadlineWithoutScrollbar.VerticalScrollBarVisible &&
                PrimaryDeadlineTextFullyVisible(rows, primaryDeadlineText);

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
            var primaryDeadlineTextFullyVisibleWithScrollbar =
                primaryDeadlineWithScrollbar.VerticalScrollBarVisible &&
                PrimaryDeadlineTextFullyVisible(rows, primaryDeadlineText);

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
            nightNotesCaptureBytes = floatingLongNotesValidation.Captures
                .Single(capture => capture.Spec.CaptureId == "long-notes-night-top")
                .PngBytes;
            floatingReferenceBaseline = ValidateFloatingReferenceBaseline(
                assetRoot,
                new[] { dayCountdownCapture, nightCountdownCapture }
                    .Concat(floatingLongNotesValidation.Captures)
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

            Required<ComboBox>(referenceWindow, "FloatingReminder").SelectedIndex = 2;
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            viewportStates.Add(
                CaptureFloatingViewportState("repeating-reminder", referenceWindow, root, rows));

            bodyScroller.ScrollToVerticalOffset(bodyScroller.ScrollableHeight);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            scrolledNotesCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
            referenceWindow.ApplyTheme(ApplicationThemeMode.Night);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            bodyScroller.ScrollToVerticalOffset(bodyScroller.ScrollableHeight);
            PumpDispatcher();
            nightScrolledNotesCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
            referenceWindow.ApplyTheme(ApplicationThemeMode.Day);
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
                viewportStates[5].RepeatingReminderPanelVisible &&
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
                if (nightCountdownCaptureBytes is not null)
                {
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"{captureName}-night-countdowns.png"),
                        nightCountdownCaptureBytes);
                }
                if (nightNotesCaptureBytes is not null)
                {
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"{captureName}-night-notes.png"),
                        nightNotesCaptureBytes);
                }
                if (scrolledNotesCaptureBytes is not null)
                {
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"{captureName}-notes-scrolled.png"),
                        scrolledNotesCaptureBytes);
                }
                if (nightScrolledNotesCaptureBytes is not null)
                {
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"{captureName}-night-notes-scrolled.png"),
                        nightScrolledNotesCaptureBytes);
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

            return new FloatingReferenceExerciseResult(
                aspectMatches,
                panelGeometryMatches,
                mascotGeometryMatches,
                floatingReferenceBaseline,
                floatingLongNotesValidation,
                new FloatingPrimaryCountdownContrastValidation(
                    primaryCountdownContrastGates,
                    primaryCountdownContrastMeasurements),
                nightPlushEdgeGlow,
                contentSafeAreaMatches,
                titleUsesSemiBoldWeight,
                noteInputWraps,
                noteInputScrolls,
                scrolledNotesCaptureBytes?.Length > 0,
                nightScrolledNotesCaptureBytes?.Length > 0,
                contentOverlapCount,
                contentOverlapTargets,
                captureWritten,
                workdayCountdownUsesPrimaryVisual,
                scenarioMatches,
                primaryDeadlineTextFullyVisible,
                viewportStatesMatch,
                rasterCardStatesMatch,
                dynamicCardContentRemainsWpf,
                viewportScrollAndFocusRestored,
                noteFailureAppearsInSourceCard,
                resolvedNoteCardErrorDoesNotReturn,
                secondaryRemainingTextFullyVisible,
                secondaryRemainingGates,
                secondaryRemainingSamples,
                floatingScrollBarValidation,
                new[] { Math.Round(actualWidth, 2), Math.Round(actualHeight, 2) },
                capturePath);
        }
        finally
        {
            referenceWindow?.Close();
            TryDeleteFile(settingsPath);
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
        var noteEntry = Required<Border>(window, "FloatingNoteEntry");
        noteEntry.Visibility = Visibility.Collapsed;
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

        foreach (var theme in new[] { ApplicationThemeMode.Day, ApplicationThemeMode.Night })
        {
            window.ApplyTheme(theme);
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
                        Math.Abs(text.FontSize - 12) <= 0.01 &&
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
                var captureId = $"long-notes-{theme.ToString().ToLowerInvariant()}-{state.ToLowerInvariant()}";
                var fingerprint = CreateFloatingReferenceStateFingerprint(
                    theme.ToString(),
                    "Notes",
                    primaryBinding?.Note.Id ?? string.Empty,
                    primaryBinding?.Note.Text ?? string.Empty,
                    state,
                    bodyScroller.VerticalOffset,
                    bodyScroller.ScrollableHeight,
                    firstVisibleAnchor,
                    lastVisibleAnchor);
                var pngBytes = EncodePng(ScaleToFrame(
                    CaptureVisual(root),
                    captureWidth,
                    captureHeight));
                var spec = FloatingReferenceCaptureSpecs.Single(candidate =>
                    string.Equals(candidate.CaptureId, captureId, StringComparison.Ordinal));
                captures.Add(CreateFloatingReferenceActualCapture(spec, pngBytes, fingerprint));
                measurements.Add(new FloatingLongNoteFrameMeasurement(
                    captureId,
                    theme.ToString(),
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
        }

        window.ApplyTheme(ApplicationThemeMode.Day);
        window.UpdateTime(referenceUtc);
        bodyScroller.ScrollToTop();
        WaitForStableFloatingReferenceLayout(window, root, rows, notes.Count);
        noteEntry.Visibility = Visibility.Visible;
        PumpDispatcher();
        window.UpdateLayout();

        var outerScrollable = measurements.Count == 4 &&
            measurements.All(measurement => measurement.ScrollableHeight > 0);
        var topBottomReachable = measurements.Count == 4 && measurements.All(measurement =>
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
        ApplicationThemeMode theme,
        FloatingWindow window,
        StackPanel rows,
        Border taskCard)
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
        var remainingText = Descendants<TextBlock>(primaryCard).Single(text =>
            Math.Abs(text.FontSize - 16) <= 0.01 &&
            Math.Abs(text.Width - 194) <= 0.01);
        var foreground = remainingText.Foreground as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗主倒计时前景不是实色画刷。");
        var surface = primarySurface.Background as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗主倒计时纸面不是实色画刷。");
        var taskCardBrush = taskCard.Background as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗任务卡背景不是实色画刷。");
        var palette = FloatingThemePalette.For(theme);
        var effectiveTaskCard = taskCardBrush.Color.A > 0
            ? taskCardBrush.Color
            : ((SolidColorBrush)window.FindResource(palette.PanelBrushKey)).Color;
        var compositeBackground = CompositeColor(
            surface.Color,
            primarySurface.Opacity,
            effectiveTaskCard);
        var expectedAccentKey = $"Floating{theme}PrimaryCountdownAccentBrush";
        var expectedAccent = window.TryFindResource(expectedAccentKey) as SolidColorBrush;
        var plushGlow = window.FindResource(palette.PlushGlowBrushKey) as SolidColorBrush
            ?? throw new InvalidOperationException("悬浮窗玩偶阴影不是实色画刷。");
        var accentTrackBrush = accentTrack.Background as SolidColorBrush;

        return new FloatingPrimaryCountdownContrastMeasurement(
            theme.ToString(),
            expectedAccentKey,
            palette.PlushGlowBrushKey,
            foreground.Color.ToString(),
            surface.Color.ToString(),
            Math.Round(primarySurface.Opacity, 2),
            taskCardBrush.Color.ToString(),
            effectiveTaskCard.ToString(),
            compositeBackground.ToString(),
            Math.Round(ContrastRatio(foreground.Color, compositeBackground), 3),
            Math.Round(remainingText.FontSize, 2),
            expectedAccent is not null && ReferenceEquals(remainingText.Foreground, expectedAccent),
            expectedAccent is not null && ReferenceEquals(accentTrack.Background, expectedAccent),
            !string.Equals(foreground.Color.ToString(), plushGlow.Color.ToString(), StringComparison.Ordinal));
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
        var noteEntry = Required<Border>(window, "FloatingNoteEntry");
        var fixtureTagsRemainBound = bindings.All(binding =>
            binding.Note.TagIds.SequenceEqual(new[] { "reference-warm-tag" }, StringComparer.Ordinal));
        var quickAddIsClosedForLongNoteViewport = noteEntry.Visibility == Visibility.Collapsed;
        return cardsStayInsideRightSafeArea &&
               adjacentCardsDoNotOverlap &&
               cardInternalsDoNotOverlap &&
               fixtureTagsRemainBound &&
               quickAddIsClosedForLongNoteViewport;
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
                topBottomComparisons.Length == 2;

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
            var currentSourceAggregate = AggregateFloatingReferenceSourceBindings(
                currentSourceBindings);
            var sourceHashesMatch =
                sourceBindings.Length == currentSourceBindings.Count &&
                sourceBindings.Select(binding => binding.Path)
                    .SequenceEqual(FloatingReferenceCanonicalSourcePaths, StringComparer.Ordinal) &&
                sourceBindings.Zip(currentSourceBindings)
                    .All(pair =>
                        string.Equals(pair.First.Path, pair.Second.Path, StringComparison.Ordinal) &&
                        IsUpperSha256(pair.First.Sha256) &&
                        string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.Ordinal)) &&
                IsUpperSha256(manifestAggregateSha256) &&
                string.Equals(
                    manifestAggregateSha256,
                    AggregateFloatingReferenceSourceBindings(sourceBindings),
                    StringComparison.Ordinal) &&
                string.Equals(manifestAggregateSha256, currentSourceAggregate, StringComparison.Ordinal);

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
            foreach (var themeName in new[] { "Day", "Night" })
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
            throw new InvalidOperationException("悬浮窗参考图生成必须提供严格六帧 inventory。");
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
        var stateDifferences = new[] { "Day", "Night" }
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

        foreach (var theme in new[]
                 {
                     ApplicationThemeMode.Day,
                     ApplicationThemeMode.Night
                 })
        {
            window.ApplyTheme(theme);
            window.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(window, root, rows, 9);
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
                        window.FindResource("PeachLineBrush"));
                var bodyBounds = BoundsIn(bodyScroller, root);
                measurements.Add(new FloatingScrollBarStateMeasurement(
                    theme.ToString(),
                    state.Name,
                    Math.Round(
                        Math.Min(parts.HitRail.ActualWidth, parts.Thumb.ActualWidth),
                        2),
                    Math.Round(parts.ThumbVisual.ActualWidth, 2),
                    Math.Round(parts.ThumbVisual.Opacity, 2),
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
                    BrushColor(parts.ThumbVisual.Background),
                    BrushColor(parts.ThumbVisual.BorderBrush)));
                captures[$"{theme.ToString().ToLowerInvariant()}-{state.Name}"] =
                    EncodePng(ScaleToFrame(
                        CaptureVisual(root),
                        captureWidth,
                        captureHeight));
            }
        }

        window.ApplyTheme(ApplicationThemeMode.Day);
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
                measurements
                    .Where(sample => sample.Theme == nameof(ApplicationThemeMode.Day))
                    .All(sample => sample.UsesThemeTokens),
                measurements
                    .Where(sample => sample.Theme == nameof(ApplicationThemeMode.Night))
                    .All(sample => sample.UsesThemeTokens),
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
            _ => true
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
        var repeatingReminderPanel = Required<StackPanel>(window, "FloatingReminderIntervalPanel");
        var bodyViewport = BoundsIn(bodyScroller, root);
        var bodyContentRight = bodyViewport.X + bodyScroller.ViewportWidth;
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
        var noteEntryFitsSafeArea =
            !noteEntry.IsVisible ||
            (noteEntryBounds.Right <= bodyContentRight + 0.25 &&
             noteEntryBounds.Right <= 396.5);
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
        var workspaceSurfaceGeometryMatches =
            !isNoteWorkspace ||
            (renderedCardBounds.All(bounds =>
                 Math.Abs(bounds.Width - 319) <= 0.5 &&
                 bounds.Right <= bodyContentRight + 0.25 &&
                 bounds.Right <= 396.5) &&
             noteEntryFitsSafeArea &&
             noteDateStamps.Length == renderedCards.Length &&
             noteDateStampBounds.All(bounds => bounds.Right <= bodyContentRight + 0.25) &&
             noteDateStamps.All(stamp =>
                 !stamp.IsHitTestVisible &&
                 !stamp.Focusable));
        var maximumRenderedCardRight = renderedCardBounds.Length == 0
            ? 0
            : renderedCardBounds.Max(bounds => bounds.Right);
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
            repeatingReminderPanel.IsVisible && repeatingReminderPanel.ActualHeight > 0,
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
            Math.Round(noteDateStampBounds.Length == 0
                ? 0
                : noteDateStampBounds.Max(bounds => bounds.Right), 2),
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
        var bitmap = CaptureVisual(scene);
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

    private static FloatingNightPlushEdgeGlowValidation ValidateFloatingNightPlushEdgeGlow(
        FloatingWindow window,
        Canvas scene,
        IReadOnlyList<NormalizedRect> expectedMascots,
        double sceneWidth,
        double sceneHeight)
    {
        const string algorithm =
            "base-alpha>32; transparent samples outside Chebyshev distance 2-6px; " +
            "brightness=sRGB-relative-luminance(aggregate-effect-color)*mean-effect-alpha; " +
            "saturation=HSL saturation(aggregate-premultiplied-effect-color/effect-alpha)";
        var targets = new[]
        {
            new PlushEdgeTarget(
                "MyMelody",
                Required<Image>(window, "PlushTopLeft"),
                Required<Border>(window, "MyMelodyPlushAnchor")),
            new PlushEdgeTarget(
                "Cinnamoroll",
                Required<Image>(window, "PlushTopRight"),
                Required<Border>(window, "CinnamorollPlushAnchor")),
            new PlushEdgeTarget(
                "Kuromi",
                Required<Image>(window, "PlushBottomRight"),
                Required<Border>(window, "KuromiPlushAnchor"))
        };
        var rendered = CaptureVisual(scene);
        var effects = targets.Select(target => target.Image.Effect).ToArray();
        RenderTargetBitmap source;
        try
        {
            foreach (var target in targets)
            {
                target.Image.Effect = null;
            }

            PumpDispatcher();
            source = CaptureVisual(scene);
        }
        finally
        {
            for (var index = 0; index < targets.Length; index++)
            {
                targets[index].Image.Effect = effects[index];
            }

            PumpDispatcher();
        }

        var stride = rendered.PixelWidth * 4;
        var renderedPixels = new byte[stride * rendered.PixelHeight];
        var sourcePixels = new byte[stride * source.PixelHeight];
        rendered.CopyPixels(renderedPixels, stride, 0);
        source.CopyPixels(sourcePixels, stride, 0);
        var regions = targets
            .Select(target => ToPixelRegion(
                BoundsIn(target.Anchor, scene),
                rendered.PixelWidth,
                rendered.PixelHeight))
            .ToArray();
        var sourceBounds = regions
            .Select(region => FindAlphaBounds(sourcePixels, stride, region, 32))
            .ToArray();
        var sourceAlphaBoundsMatch =
            sourceBounds.Length == expectedMascots.Count &&
            sourceBounds.Zip(expectedMascots).All(pair =>
                pair.First is { } bounds &&
                Matches(Normalize(bounds, sceneWidth, sceneHeight), pair.Second, 0.011));
        var measurements = targets
            .Select((target, index) => MeasurePlushEdgeGlow(
                target.Name,
                renderedPixels,
                sourcePixels,
                stride,
                rendered.PixelWidth,
                rendered.PixelHeight,
                regions[index]))
            .ToArray();
        var gates = ValidationExercises.EvaluateFloatingNightPlushEdgeGlow(
            new FloatingNightPlushEdgeGateInputs(
                measurements.Max(measurement => measurement.BrightnessSignal),
                measurements.Max(measurement => measurement.Saturation),
                measurements.Length == 3 &&
                    measurements.All(measurement =>
                        measurement.RoiPixelCount > 0 &&
                        measurement.VisibleEffectSampleCount > 0)));
        return new FloatingNightPlushEdgeGlowValidation(
            sourceAlphaBoundsMatch,
            sourceBounds.Select(bounds => bounds is null ? string.Empty : FormatRect(bounds.Value)).ToArray(),
            measurements,
            gates,
            algorithm);
    }

    private static FloatingNightPlushEdgeMeasurement MeasurePlushEdgeGlow(
        string role,
        byte[] renderedPixels,
        byte[] sourcePixels,
        int stride,
        int width,
        int height,
        Int32Rect region)
    {
        const byte sourceAlphaThreshold = 32;
        const int innerDistance = 2;
        const int outerDistance = 6;
        var roiPixelCount = 0;
        var visibleEffectSampleCount = 0;
        var effectAlphaSum = 0d;
        var effectRedSum = 0d;
        var effectGreenSum = 0d;
        var effectBlueSum = 0d;

        for (var y = region.Y; y < region.Y + region.Height; y++)
        {
            for (var x = region.X; x < region.X + region.Width; x++)
            {
                var sourceOffset = (y * stride) + (x * 4);
                if (sourcePixels[sourceOffset + 3] != 0)
                {
                    continue;
                }

                var distance = MinimumOpaqueChebyshevDistance(
                    sourcePixels,
                    stride,
                    width,
                    height,
                    x,
                    y,
                    outerDistance,
                    sourceAlphaThreshold);
                if (distance < innerDistance || distance > outerDistance)
                {
                    continue;
                }

                roiPixelCount++;
                var effectAlpha = Math.Max(
                    0,
                    renderedPixels[sourceOffset + 3] - sourcePixels[sourceOffset + 3]);
                if (effectAlpha <= 1)
                {
                    continue;
                }

                visibleEffectSampleCount++;
                effectAlphaSum += effectAlpha;
                effectBlueSum += Math.Max(
                    0,
                    renderedPixels[sourceOffset] - sourcePixels[sourceOffset]);
                effectGreenSum += Math.Max(
                    0,
                    renderedPixels[sourceOffset + 1] - sourcePixels[sourceOffset + 1]);
                effectRedSum += Math.Max(
                    0,
                    renderedPixels[sourceOffset + 2] - sourcePixels[sourceOffset + 2]);
            }
        }

        var red = effectAlphaSum == 0 ? 0 : Math.Clamp(effectRedSum / effectAlphaSum, 0, 1);
        var green = effectAlphaSum == 0 ? 0 : Math.Clamp(effectGreenSum / effectAlphaSum, 0, 1);
        var blue = effectAlphaSum == 0 ? 0 : Math.Clamp(effectBlueSum / effectAlphaSum, 0, 1);
        var meanEffectAlpha = roiPixelCount == 0
            ? 0
            : effectAlphaSum / (255 * roiPixelCount);

        return new FloatingNightPlushEdgeMeasurement(
            role,
            roiPixelCount,
            visibleEffectSampleCount,
            Math.Round(RelativeLuminance(red, green, blue) * meanEffectAlpha, 6),
            Math.Round(HslSaturation(red, green, blue), 6),
            $"#FF{(byte)Math.Round(red * 255):X2}{(byte)Math.Round(green * 255):X2}{(byte)Math.Round(blue * 255):X2}");
    }

    private static int MinimumOpaqueChebyshevDistance(
        byte[] sourcePixels,
        int stride,
        int width,
        int height,
        int x,
        int y,
        int maximumDistance,
        byte alphaThreshold)
    {
        var minimum = maximumDistance + 1;
        for (var offsetY = -maximumDistance; offsetY <= maximumDistance; offsetY++)
        {
            var sampleY = y + offsetY;
            if (sampleY < 0 || sampleY >= height)
            {
                continue;
            }

            for (var offsetX = -maximumDistance; offsetX <= maximumDistance; offsetX++)
            {
                var distance = Math.Max(Math.Abs(offsetX), Math.Abs(offsetY));
                if (distance >= minimum)
                {
                    continue;
                }

                var sampleX = x + offsetX;
                if (sampleX < 0 || sampleX >= width)
                {
                    continue;
                }

                if (sourcePixels[(sampleY * stride) + (sampleX * 4) + 3] > alphaThreshold)
                {
                    minimum = distance;
                }
            }
        }

        return minimum;
    }

    private static double RelativeLuminance(double red, double green, double blue) =>
        (0.2126 * LinearChannel(red)) +
        (0.7152 * LinearChannel(green)) +
        (0.0722 * LinearChannel(blue));

    private static double LinearChannel(double channel) =>
        channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static double HslSaturation(double red, double green, double blue)
    {
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var chroma = maximum - minimum;
        if (chroma <= double.Epsilon)
        {
            return 0;
        }

        var lightness = (maximum + minimum) / 2;
        return chroma / (1 - Math.Abs((2 * lightness) - 1));
    }

    private static Int32Rect ToPixelRegion(Rect bounds, int width, int height)
    {
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, height);
        return new Int32Rect(left, top, right - left, bottom - top);
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

    private static RenderTargetBitmap CaptureVisual(FrameworkElement visual)
    {
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static RenderTargetBitmap CaptureOpaqueMainSurface(FrameworkElement visual)
    {
        if (Application.Current.FindResource("BackgroundBrush") is not SolidColorBrush background ||
            background.Color.A != byte.MaxValue)
        {
            throw new InvalidOperationException("主窗口背景必须是不透明主题画刷。");
        }

        var source = CaptureVisual(visual);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
            context.DrawRectangle(background, null, bounds);
            context.DrawImage(source, bounds);
        }

        var bitmap = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(drawing);
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
        if (source.Format != PixelFormats.Pbgra32 ||
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

            var confirmation = after.ConfirmReminderDisplayed(
                dueNote,
                firstDueUtc.AddMinutes(5));
            if (!confirmation.Succeeded)
            {
                return false;
            }

            var duplicateInProcess = after.GetDueReminders(firstDueUtc.AddMinutes(5));
            var reloaded = DesktopWorkspace.Load(settingsPath, afterStore);
            var duplicateAfterRestart = reloaded.GetDueReminders(firstDueUtc.AddMinutes(5));
            return
                early.Count == 0 &&
                overdue.Count == 1 &&
                duplicateInProcess.Count == 0 &&
                duplicateAfterRestart.Count == 0 &&
                reloaded.Snapshot.Settings.Notes.Single().Reminder is OnceReminder
                {
                    Delivery: OnceDeliveryState.Delivered
                };
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
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

    private static bool HasFixedNoteStatusDisclosure(
        Button toggle,
        TextBlock label,
        TextBlock count,
        WpfPath chevron,
        string status,
        int expectedCount,
        bool isExpanded)
    {
        return
            ReferenceEquals(toggle.Content, label.Parent is StackPanel labelStack ? labelStack.Parent : null) &&
            string.Equals(label.Text, status, StringComparison.Ordinal) &&
            !label.Text.StartsWith("展开", StringComparison.Ordinal) &&
            !label.Text.StartsWith("收起", StringComparison.Ordinal) &&
            string.Equals(
                count.Text,
                expectedCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal) &&
            DisclosureChevronMatches(chevron, isExpanded);
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
        var helpText = AutomationProperties.GetHelpText(toggle);
        return
            !string.IsNullOrWhiteSpace(name) &&
            (name.StartsWith("展开", StringComparison.Ordinal) ||
             name.StartsWith("收起", StringComparison.Ordinal)) &&
            name.Contains(expectedObject, StringComparison.Ordinal) &&
            name.Contains("条", StringComparison.Ordinal) &&
            helpText is "当前展开" or "当前收起";
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
        var activeStatusToggle = Required<Button>(main, "ToggleActiveNotes");
        var completedStatusToggle = Required<Button>(main, "ToggleCompletedNotes");
        var originalWorkspace = main.CurrentWorkspace;
        var activeStatusWasVisible = activeRows.Visibility == Visibility.Visible;
        var completedStatusWasVisible = completedRows.Visibility == Visibility.Visible;

        try
        {
            main.ShowWorkspace(WorkspaceKind.Notes);
            ExpandStatusSection(activeRows, activeStatusToggle);
            ExpandStatusSection(completedRows, completedStatusToggle);

            var statusSectionsAreIndependent = ExerciseNoteStatusSections(
                main,
                activeRows,
                completedRows,
                activeStatusToggle,
                completedStatusToggle);

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
                statusSectionsAreIndependent);
        }
        finally
        {
            ExpandAllNoteDateSections(activeRows);
            ExpandAllNoteDateSections(completedRows);
            RestoreStatusSection(activeRows, activeStatusToggle, activeStatusWasVisible);
            RestoreStatusSection(completedRows, completedStatusToggle, completedStatusWasVisible);
            main.ShowWorkspace(originalWorkspace);
        }
    }

    private static bool ExerciseNoteStatusSections(
        MainWindow main,
        StackPanel activeRows,
        StackPanel completedRows,
        Button activeToggle,
        Button completedToggle)
    {
        if (activeToggle.Visibility != Visibility.Visible || completedToggle.Visibility != Visibility.Visible)
        {
            return true;
        }

        activeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var independent =
            activeRows.Visibility == Visibility.Collapsed &&
            completedRows.Visibility == Visibility.Visible;
        main.ShowWorkspace(WorkspaceKind.Notes);
        independent &=
            activeRows.Visibility == Visibility.Collapsed &&
            completedRows.Visibility == Visibility.Visible;
        activeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        completedToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        independent &=
            activeRows.Visibility == Visibility.Visible &&
            completedRows.Visibility == Visibility.Collapsed;
        main.ShowWorkspace(WorkspaceKind.Notes);
        independent &=
            activeRows.Visibility == Visibility.Visible &&
            completedRows.Visibility == Visibility.Collapsed;
        completedToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return independent;
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

    private static void ExpandStatusSection(StackPanel rows, Button toggle)
    {
        if (rows.Visibility == Visibility.Collapsed && toggle.Visibility == Visibility.Visible)
        {
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private static void RestoreStatusSection(StackPanel rows, Button toggle, bool shouldBeVisible)
    {
        if (
            toggle.Visibility == Visibility.Visible &&
            (rows.Visibility == Visibility.Visible) != shouldBeVisible)
        {
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private static bool HasApplicationStyle(Type key) =>
        Application.Current.Resources[key] is Style;

    private static bool HasTemplate(Control control)
    {
        control.ApplyTemplate();
        return control.Template is not null;
    }

    private static ThemePreferenceButtonExerciseResult RunThemePreferenceButtonExercise(
        MainWindow main,
        FloatingWindow floating,
        DesktopWorkspace desktopWorkspace)
    {
        var button = Required<Button>(main, "ThemeModeButton");
        var expectedPreferences = new[]
        {
            ThemePreferenceMode.Day,
            ThemePreferenceMode.Night,
            ThemePreferenceMode.Auto
        };
        var expectedLabels = new[] { "☀ 日间", "☾ 夜间", "◐ 自动" };
        var observedPreferences = new List<ThemePreferenceMode>();
        var observedLabels = new List<string>();
        var persistedEachMode = true;
        var accessibleEachMode = true;
        var synchronizedEachMode = true;

        foreach (var expectedPreference in expectedPreferences)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            var workspaceSnapshot = desktopWorkspace.Snapshot;
            observedPreferences.Add(workspaceSnapshot.Settings.Preferences.ThemePreference);
            observedLabels.Add(button.Content?.ToString() ?? string.Empty);
            var persisted = DesktopWorkspace.Load(workspaceSnapshot.SettingsPath).Snapshot;
            persistedEachMode &=
                persisted.Settings.Preferences.ThemePreference == expectedPreference;
            accessibleEachMode &=
                button.ToolTip is string toolTip && toolTip.Contains("主题模式", StringComparison.Ordinal) &&
                AutomationProperties.GetName(button) is string accessibleName &&
                accessibleName.Contains("主题模式", StringComparison.Ordinal);
            synchronizedEachMode &=
                string.Equals(
                    main.CurrentThemeMode.ToString(),
                    floating.GetType()
                        .GetProperty(
                            "CurrentThemeMode",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?.GetValue(floating)
                        ?.ToString(),
                    StringComparison.Ordinal);
        }

        return new ThemePreferenceButtonExerciseResult(
            observedPreferences.SequenceEqual(expectedPreferences),
            observedLabels.SequenceEqual(expectedLabels, StringComparer.Ordinal),
            persistedEachMode,
            accessibleEachMode,
            button.Width is >= 120 and <= 150 &&
            button.Height is >= 44 and <= 52 &&
            ReferenceEquals(
                button.Style,
                Required<Grid>(main, "MainContentProjection").Resources["MainSecondaryButton"]),
            synchronizedEachMode);
    }

    private static MainThemeExerciseResult RunMainThemeExercise(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        var checkpoints = new[]
        {
            new DateTimeOffset(2030, 1, 15, 6, 59, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2030, 1, 15, 7, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2030, 1, 15, 18, 59, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2030, 1, 15, 19, 0, 0, TimeSpan.FromHours(8))
        };
        var modes = new List<string>();
        var dayBackgroundColor = string.Empty;
        var nightBackgroundColor = string.Empty;
        var dayCharacterOpacity = 0;
        var nightCharacterOpacity = 0;
        var dayCharacterPanelColor = string.Empty;
        var nightCharacterPanelColor = string.Empty;
        var reminderLabelFollowsTheme = true;
        var segmentSelectedTextContrast = true;
        var characterPanelDocksFollowTheme = true;
        var referenceDecorationsFollowTheme = true;
        var reminderLabel = Required<TextBlock>(main, "NoteReminderLabel");
        var characterPanel = Required<Border>(main, "CharacterPanelSurface");
        var originalWorkspace = main.CurrentWorkspace;

        try
        {
            foreach (var checkpoint in checkpoints)
            {
                var mode = theme.Apply(checkpoint.ToUniversalTime());
                main.ApplyTheme(mode);
                main.UpdateTime(checkpoint);
                main.ShowWorkspace(WorkspaceKind.Countdowns);
                PumpDispatcher();
                main.UpdateLayout();
                modes.Add(mode.ToString());
                reminderLabelFollowsTheme &=
                    ReferenceEquals(reminderLabel.Foreground, Application.Current.FindResource("InkBrush"));
                segmentSelectedTextContrast &= MainSegmentButtonsHaveAccessibleSelectedStates(main);
                characterPanelDocksFollowTheme &= main.CharacterPanelDocksFollowActiveTheme;
                referenceDecorationsFollowTheme &= main.MainReferenceDecorationsFollowActiveTheme;
                if (mode == ApplicationThemeMode.Day)
                {
                    dayBackgroundColor = BrushColor(main.Background);
                    dayCharacterOpacity = (int)Math.Round(main.CharacterBackgroundOpacity * 100);
                    dayCharacterPanelColor = BrushColor(characterPanel.Background);
                }
                else
                {
                    nightBackgroundColor = BrushColor(main.Background);
                    nightCharacterOpacity = (int)Math.Round(main.CharacterBackgroundOpacity * 100);
                    nightCharacterPanelColor = BrushColor(characterPanel.Background);
                }

                main.ShowWorkspace(WorkspaceKind.Notes);
                PumpDispatcher();
                main.UpdateLayout();
                characterPanelDocksFollowTheme &= main.CharacterPanelDocksFollowActiveTheme;
                referenceDecorationsFollowTheme &= main.MainReferenceDecorationsFollowActiveTheme;
            }
        }
        finally
        {
            main.ShowWorkspace(originalWorkspace);
            main.ApplyTheme(theme.Apply(restoreUtc));
            PumpDispatcher();
            main.UpdateLayout();
        }
        var expectedModes = new[] { "Night", "Day", "Day", "Night" };
        return new MainThemeExerciseResult(
            modes,
            new[] { dayBackgroundColor, nightBackgroundColor },
            new[] { dayCharacterOpacity, nightCharacterOpacity },
            new[] { dayCharacterPanelColor, nightCharacterPanelColor },
            modes.SequenceEqual(expectedModes, StringComparer.Ordinal),
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
            TryDeleteFile(settingsPath);
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
            return bodies.Length > 0 &&
                bodies.Any(body => body.Children.OfType<TextBlock>().Any()) &&
                bodies.All(BodyTextUsesTheme) &&
                bodies.All(TagTextUsesTheme);
        }

        var originalWorkspace = main.CurrentWorkspace;
        try
        {
            theme.Apply(ApplicationThemeMode.Day);
            main.ApplyTheme(ApplicationThemeMode.Day);
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            main.UpdateLayout();

            theme.Apply(ApplicationThemeMode.Night);
            main.ApplyTheme(ApplicationThemeMode.Night);
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

            theme.Apply(ApplicationThemeMode.Day);
            main.ApplyTheme(ApplicationThemeMode.Day);
            main.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            main.UpdateLayout();

            theme.Apply(ApplicationThemeMode.Night);
            main.ApplyTheme(ApplicationThemeMode.Night);
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
                    UsesResource(card, Border.BackgroundProperty, "NoteBrush") &&
                    UsesResource(card, Border.BorderBrushProperty, "NoteLineBrush") &&
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
            var restoreMode = theme.Apply(restoreUtc);
            main.ApplyTheme(restoreMode);
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
            foreach (var checkpoint in new[]
            {
                new DateTimeOffset(2030, 1, 15, 7, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2030, 1, 15, 19, 0, 0, TimeSpan.FromHours(8))
            })
            {
                theme.Apply(checkpoint.ToUniversalTime());
                PumpDispatcher();
                if (!selector.PopupSurfaceFollowsActiveTheme)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            selector.IsPopupOpen = wasOpen;
            theme.Apply(restoreUtc);
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

        var day = new DateTimeOffset(2030, 1, 15, 7, 0, 0, TimeSpan.FromHours(8));
        var night = new DateTimeOffset(2030, 1, 15, 19, 0, 0, TimeSpan.FromHours(8));
        var defaultTagColorsMatch = MainWindow.DefaultTagColors.SequenceEqual(
            new[] { "#F4B6A6", "#F4D58D", "#F1D5C5", "#F9DFDB", "#FFF0E4", "#E8B4C8" },
            StringComparer.OrdinalIgnoreCase);
        try
        {
            theme.Apply(day.ToUniversalTime());
            var dayMatches = new[]
            {
                ("CanvasColor", "#FFFDFC"),
                ("BackgroundColor", "#FFF6EF"),
                ("CardColor", "#FFF0E4"),
                ("NoteColor", "#FFF6D9"),
                ("NoteLineColor", "#E7C781"),
                ("ActiveHoverColor", "#B34B36"),
                ("PeachLineColor", "#F1D5C5"),
                ("TagColor", "#FFF7E8"),
                ("TagChipTextColor", "#4A2D24"),
                ("SegmentSelectedColor", "#FFF7E8"),
                ("SegmentSelectedForegroundColor", "#725326"),
                ("WorkspacePanelColor", "#00FFFDFC"),
                ("WorkspacePanelLineColor", "#00F1D5C5"),
                ("FloatingDayPanelBrush", "#FFFDFC"),
                ("FloatingDayHeaderBrush", "#FFF6D9"),
                ("FloatingDayNoteInputBrush", "#FFF6D9"),
                ("FloatingDayPlushGlowBrush", "#B9794E"),
                ("FloatingDayCinnamorollDockBrush", "#FFF0D2")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            theme.Apply(night.ToUniversalTime());
            var nightMatches = new[]
            {
                ("CanvasColor", "#33291E"),
                ("BackgroundColor", "#2B2118"),
                ("CardColor", "#423125"),
                ("NoteColor", "#4E3827"),
                ("InkColor", "#FFF4D8"),
                ("ActiveColor", "#9A523B"),
                ("TagColor", "#E7C99B"),
                ("TagChipTextColor", "#4A2D24"),
                ("SegmentSelectedColor", "#4B3522"),
                ("SegmentSelectedForegroundColor", "#FFF0CC"),
                ("WorkspacePanelColor", "#FF3A2C21"),
                ("WorkspacePanelLineColor", "#FF8B6845"),
                ("FloatingNightPanelBrush", "#3A2B1E"),
                ("FloatingNightHeaderBrush", "#4A3524"),
                ("FloatingNightNoteInputBrush", "#F7E1B3"),
                ("FloatingNightPlushGlowBrush", "#E9B36D"),
                ("FloatingNightCinnamorollDockBrush", "#55452A")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            return dayMatches && nightMatches && defaultTagColorsMatch;
        }
        finally
        {
            theme.Apply(restoreUtc);
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
            theme.Apply(ApplicationThemeMode.Day);
            var dayMatches = new[]
            {
                ("NoteStatusHeaderColor", "#FFF0E4"),
                ("NoteCompletedHeaderColor", "#F7E9E2"),
                ("NoteActiveRailColor", "#D55C47"),
                ("NoteCompletedRailColor", "#B89E8E"),
                ("NoteTimelineColor", "#E9C5B2")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            theme.Apply(ApplicationThemeMode.Night);
            var nightMatches = new[]
            {
                ("NoteStatusHeaderColor", "#423125"),
                ("NoteCompletedHeaderColor", "#493229"),
                ("NoteActiveRailColor", "#C86F54"),
                ("NoteCompletedRailColor", "#C6A17F"),
                ("NoteTimelineColor", "#8B6845")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            return dayMatches && nightMatches;
        }
        finally
        {
            theme.Apply(restoreUtc);
        }
    }

    private static bool ValidatePrimaryButtonHoverTextContrast(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        var primaryStyle = Application.Current.FindResource("PrimaryButton") as Style;
        var template = primaryStyle?.Setters
            .OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == Control.TemplateProperty)
            ?.Value as ControlTemplate;
        var hasDedicatedHoverState = template?.Triggers
            .OfType<Trigger>()
            .Any(trigger =>
                trigger.Property == UIElement.IsMouseOverProperty &&
                trigger.Value is bool isMouseOver &&
                isMouseOver &&
                trigger.Setters.OfType<Setter>().Any(setter =>
                    setter.TargetName == "Surface" &&
                    setter.Property == Border.BackgroundProperty)) == true;
        try
        {
            foreach (var checkpoint in new[]
            {
                new DateTimeOffset(2030, 1, 15, 7, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2030, 1, 15, 19, 0, 0, TimeSpan.FromHours(8))
            })
            {
                theme.Apply(checkpoint.ToUniversalTime());
                if (Application.Current.FindResource("ActiveHoverBrush") is not Brush hover ||
                    !ApplicationTheme.MeetsContrast(hover, Brushes.White, 4.5))
                {
                    return false;
                }
            }

            return hasDedicatedHoverState;
        }
        finally
        {
            theme.Apply(restoreUtc);
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
            Required<Button>(main, "SaveNoteButton"),
            Required<Button>(main, "SavePreferencesButton")
        };
        var style = themedButtons[0].Style;
        if (style is null || themedButtons.Any(button => !ReferenceEquals(button.Style, style)))
        {
            return false;
        }

        try
        {
            foreach (var checkpoint in new[]
            {
                new DateTimeOffset(2030, 1, 15, 7, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2030, 1, 15, 19, 0, 0, TimeSpan.FromHours(8))
            })
            {
                theme.Apply(checkpoint.ToUniversalTime());
                if (Application.Current.FindResource("MainActionTextBrush") is not Brush foreground ||
                    new[]
                    {
                        "MainActionBrush",
                        "MainActionHoverBrush",
                        "MainActionPressedBrush"
                    }.Any(key =>
                        Application.Current.FindResource(key) is not Brush background ||
                        !ApplicationTheme.MeetsContrast(background, foreground, 4.5)))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            theme.Apply(restoreUtc);
        }
    }

    private static ReminderKeyboardExerciseResult ValidateReminderWindowKeyboardDismissal()
    {
        var allKeysDismiss = true;
        var focusesDismissAction = true;
        foreach (var key in new[] { Key.Escape, Key.Enter, Key.Space })
        {
            var window = new ReminderWindow(new NoteRecord(
                $"validation-reminder-{key}",
                "键盘提醒验证",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance))
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            try
            {
                window.Show();
                PumpDispatcher();
                var dismiss = Descendants<Button>(window).Single(button =>
                    string.Equals(button.Content?.ToString(), "知道了", StringComparison.Ordinal));
                focusesDismissAction &=
                    window.ShowActivated &&
                    ReferenceEquals(Keyboard.FocusedElement, dismiss);
                var handled = WindowValidationInput.RaisePreviewKey(dismiss, key);
                PumpDispatcher();
                allKeysDismiss &= handled && !window.IsVisible;
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        }

        return new ReminderKeyboardExerciseResult(allKeysDismiss, focusesDismissAction);
    }

    private static bool ValidateToolTipThemeTextContrast(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        try
        {
            foreach (var checkpoint in new[]
            {
                new DateTimeOffset(2030, 1, 15, 7, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2030, 1, 15, 19, 0, 0, TimeSpan.FromHours(8))
            })
            {
                theme.Apply(checkpoint.ToUniversalTime());
                var toolTip = new ToolTip { Content = "主题提示" };
                toolTip.ApplyTemplate();
                var content = toolTip.Template.FindName("Content", toolTip) as TextBlock;
                if (toolTip.Background is not Brush background ||
                    toolTip.Foreground is not Brush foreground ||
                    !ApplicationTheme.MeetsContrast(background, foreground, 4.5) ||
                    toolTip.MaxWidth > 360 ||
                    content?.TextWrapping is not TextWrapping.Wrap)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            theme.Apply(restoreUtc);
        }
    }

    private static bool ValidateTagChipTextContrast(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        try
        {
            foreach (var checkpoint in new[]
            {
                new DateTimeOffset(2030, 1, 15, 7, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2030, 1, 15, 19, 0, 0, TimeSpan.FromHours(8))
            })
            {
                var mode = theme.Apply(checkpoint.ToUniversalTime());
                main.ApplyTheme(mode);
                main.UpdateTime(checkpoint);
                PumpDispatcher();
                if (!main.TagChipsHaveAccessibleThemeText)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            var restoreMode = theme.Apply(restoreUtc);
            main.ApplyTheme(restoreMode);
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(restoreUtc));
            PumpDispatcher();
        }
    }

    private static string BrushColor(Brush? brush) =>
        brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color.ToString()
            : string.Empty;

    private static bool ValidateThemeContrast()
    {
        var canvas = (Color)Application.Current.FindResource("CanvasColor");
        var ink = (Color)Application.Current.FindResource("InkColor");
        var body = (Color)Application.Current.FindResource("BodyColor");
        var muted = (Color)Application.Current.FindResource("MutedColor");
        var tag = (Color)Application.Current.FindResource("TagColor");
        var segmentSelected = (Color)Application.Current.FindResource("SegmentSelectedColor");
        var segmentSelectedForeground =
            (Color)Application.Current.FindResource("SegmentSelectedForegroundColor");
        return
            ContrastRatio(canvas, ink) >= 7 &&
            ContrastRatio(canvas, body) >= 4.5 &&
            ContrastRatio(canvas, muted) >= 4.5 &&
            ContrastRatio(tag, (Color)Application.Current.FindResource("TagChipTextColor")) >= 4.5 &&
            ContrastRatio(segmentSelected, segmentSelectedForeground) >= 4.5;
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

    private static bool HasSelectedSegmentContrast(MainWindow main, ToggleButton button)
    {
        main.UpdateLayout();
        button.ApplyTemplate();
        var surface = button.Template?.FindName("Surface", button) as Border;
        return surface?.Background is Brush background &&
            button.Foreground is Brush foreground &&
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
            new Typeface("Microsoft YaHei UI"),
            13,
            Brushes.Black,
            VisualTreeHelper.GetDpi(element).PixelsPerDip);
        var available = double.IsNaN(element.Width)
            ? Math.Max(0, element.ActualWidth - 22)
            : Math.Max(0, element.Width - 22);
        return new TextFit(available >= formatted.Width, available, formatted.Width);
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

    private static TextBlock? FindFloatingSecondaryRemaining(
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

            return texts.SingleOrDefault(text =>
                !string.Equals(text.Text, countdownName, StringComparison.Ordinal) &&
                Grid.GetColumn(text) == 1 &&
                text.TextAlignment == TextAlignment.Right);
        }

        return null;
    }

    private static FloatingSecondaryRemainingMeasurement CaptureFloatingSecondaryRemaining(
        string precision,
        string expectedText,
        string formattedText,
        TextBlock? text,
        FloatingWindow floating,
        bool preferenceSaved)
    {
        var columnIs194Pixels =
            text is not null &&
            Math.Abs(text.Width - 194) <= 0.01 &&
            Math.Abs(text.ActualWidth - 194) <= 0.5;
        var isRightAligned =
            text is not null &&
            text.HorizontalAlignment == HorizontalAlignment.Right &&
            text.TextAlignment == TextAlignment.Right;
        var isSingleLineAndUntrimmed =
            text is not null &&
            text.TextWrapping == TextWrapping.NoWrap &&
            text.TextTrimming == TextTrimming.None &&
            !text.Text.Contains('\r') &&
            !text.Text.Contains('\n') &&
            !text.Text.Contains('…') &&
            text.LineHeight > 0 &&
            text.ActualHeight <= text.LineHeight + 0.5;
        var isFullyVisible =
            text is not null &&
            isSingleLineAndUntrimmed &&
            TextBlockLinesFit(text) &&
            FitsInsideFloatingViewport(text, floating);
        return new FloatingSecondaryRemainingMeasurement(
            precision,
            expectedText,
            formattedText,
            text?.Text ?? string.Empty,
            Math.Round(text?.Width ?? 0, 2),
            Math.Round(text?.ActualWidth ?? 0, 2),
            Math.Round(text?.FontSize ?? 0, 2),
            columnIs194Pixels,
            isRightAligned,
            isSingleLineAndUntrimmed,
            isFullyVisible,
            text is not null &&
                System.Windows.Documents.Typography.GetNumeralAlignment(text) ==
                    FontNumeralAlignment.Tabular,
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

    private static bool WorkdayCountdownUsesPrimaryVisual(
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
        var secondaryRemaining = secondaryRemainingForAutomation;
        var primaryTexts = Descendants<TextBlock>(primaryRoot).ToArray();
        var secondaryTexts = Descendants<TextBlock>(secondaryRoot).ToArray();
        var primaryTitle = primaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "距离下班", StringComparison.Ordinal));
        var primaryDeadline = primaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "7月15日 18:00", StringComparison.Ordinal));
        var primaryRemainingText = primaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, primaryRemaining, StringComparison.Ordinal));
        var secondaryTitle = secondaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "七月倒计时", StringComparison.Ordinal));
        var secondaryRemainingText = secondaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, secondaryRemaining, StringComparison.Ordinal));
        var primaryDecorations = primaryRoot.Children.OfType<Border>().ToArray();
        var secondaryDecorations = secondaryRoot.Children.OfType<Border>().ToArray();
        var primaryNames = rows.Children
            .OfType<Border>()
            .Select(card => card.Child as Grid)
            .Where(root => root is not null)
            .Select(root => AutomationProperties.GetName(root!))
            .Where(name => name.StartsWith("主要倒计时 ", StringComparison.Ordinal))
            .ToArray();

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
            secondaryDecorations.Length == 1 &&
            Math.Abs(primaryDecorations[0].Margin.Right - 54) <= 0.01 &&
            Math.Abs(primaryDecorations[0].Opacity - 0.62) <= 0.01 &&
            Math.Abs(primaryDecorations[0].CornerRadius.TopLeft - 4) <= 0.01 &&
            !primaryDecorations[0].IsHitTestVisible &&
            string.Equals(BrushColor(primaryDecorations[0].Background), "#FFFFF6D9", StringComparison.Ordinal) &&
            Math.Abs(primaryDecorations[1].Width - 3) <= 0.01 &&
            Math.Abs(primaryDecorations[1].Height - 42) <= 0.01 &&
            !primaryDecorations[1].IsHitTestVisible &&
            string.Equals(BrushColor(primaryDecorations[1].Background), "#FFA24438", StringComparison.Ordinal) &&
            Math.Abs(primaryDecorations[2].Opacity - 0.78) <= 0.01 &&
            Math.Abs(secondaryDecorations[0].Opacity - 0.48) <= 0.01 &&
            primaryTitle is not null &&
            Math.Abs(primaryTitle.FontSize - 15) <= 0.01 &&
            primaryTitle.FontWeight == FontWeights.SemiBold &&
            primaryTitle.TextWrapping == TextWrapping.Wrap &&
            string.Equals(BrushColor(primaryTitle.Foreground), "#FF4A2D24", StringComparison.Ordinal) &&
            primaryDeadline is not null &&
            Math.Abs(primaryDeadline.FontSize - 11) <= 0.01 &&
            string.Equals(BrushColor(primaryDeadline.Foreground), "#FF87665A", StringComparison.Ordinal) &&
            primaryRemainingText is not null &&
            Math.Abs(primaryRemainingText.FontSize - 16) <= 0.01 &&
            primaryRemainingText.FontWeight == FontWeights.SemiBold &&
            Math.Abs(primaryRemainingText.Width - 194) <= 0.01 &&
            string.Equals(BrushColor(primaryRemainingText.Foreground), "#FFA24438", StringComparison.Ordinal) &&
            secondaryTitle is not null &&
            Math.Abs(secondaryTitle.FontSize - 13) <= 0.01 &&
            string.Equals(BrushColor(secondaryTitle.Foreground), "#FF4A2D24", StringComparison.Ordinal) &&
            secondaryRemainingText is not null &&
            Math.Abs(secondaryRemainingText.FontSize - 13) <= 0.01 &&
            secondaryRemainingText.FontWeight == FontWeights.SemiBold &&
            Math.Abs(secondaryRemainingText.Width - 194) <= 0.01 &&
            string.Equals(BrushColor(secondaryRemainingText.Foreground), "#FF87665A", StringComparison.Ordinal);
    }

    private static bool PrimaryRemainingTextFitsOnOneLine(
        DependencyObject rows,
        DateTimeOffset targetUtc,
        DateTimeOffset nowUtc)
    {
        var expectedRemaining = CountdownFormatting.FormatRemaining(
            targetUtc,
            nowUtc,
            "Second");
        var text = Descendants<TextBlock>(rows).SingleOrDefault(candidate =>
            string.Equals(candidate.Text, expectedRemaining, StringComparison.Ordinal));
        return text is not null &&
               Math.Abs(text.Width - 194) <= 0.01 &&
               Math.Abs(text.FontSize - 16) <= 0.01 &&
               text.FontWeight == FontWeights.SemiBold &&
               text.TextWrapping == TextWrapping.NoWrap &&
               text.TextTrimming == TextTrimming.None &&
               !text.Text.Contains('\n') &&
               text.LineHeight > 0 &&
               text.ActualHeight <= text.LineHeight + 0.5 &&
               TextBlockLinesFit(text);
    }

    private static bool PrimaryDeadlineTextFullyVisible(
        DependencyObject rows,
        string expectedText)
    {
        var text = Descendants<TextBlock>(rows).SingleOrDefault(candidate =>
            candidate.Text.StartsWith(expectedText, StringComparison.Ordinal));
        return text is not null &&
               string.Equals(text.Text, expectedText, StringComparison.Ordinal) &&
               text.TextWrapping == TextWrapping.NoWrap &&
               TextBlockLinesFit(text);
    }

    private static FloatingTrayInteractionExerciseResult RunFloatingTrayInteractionExercise(
        FloatingWindow floating,
        Button minimizeButton,
        FloatingTrayController trayController)
    {
        var wasVisible = floating.IsVisible;
        var exitRequested = false;
        EventHandler exitHandler = (_, _) => exitRequested = true;
        trayController.ExitRequested += exitHandler;
        try
        {
            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            var minimizeWorks =
                !floating.IsVisible &&
                trayController.IsTrayIconVisible;

            trayController.InvokeRestoreForValidation();
            PumpDispatcher();
            var restoreWorks =
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                !trayController.IsTrayIconVisible;

            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            trayController.InvokeExitForValidation();
            PumpDispatcher();
            return new FloatingTrayInteractionExerciseResult(
                trayController.IsActive,
                minimizeWorks,
                restoreWorks,
                exitRequested);
        }
        finally
        {
            trayController.ExitRequested -= exitHandler;
            if (trayController.IsTrayIconVisible)
            {
                trayController.InvokeRestoreForValidation();
                PumpDispatcher();
            }

            if (wasVisible && !floating.IsVisible)
            {
                floating.Show();
            }
        }
    }

    private static T Required<T>(FrameworkElement root, string name)
        where T : class
    {
        return root.FindName(name) as T
            ?? throw new InvalidOperationException($"验证控件缺失：{name}");
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

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed record TextFit(bool Fits, double Available, double Required);

    private sealed record FloatingTrayInteractionExerciseResult(
        bool ProductionControllerActive,
        bool MinimizeHidesWindowAndShowsIcon,
        bool RestoreShowsWindowAndHidesIcon,
        bool ExitRequestsApplicationExit);

    private sealed record ReminderKeyboardExerciseResult(
        bool AllKeysDismiss,
        bool FocusesDismissAction);

    private sealed record AlphaMetrics(bool IsFullyOpaque, int NonOpaquePixels, byte MinimumAlpha);

    private sealed record NightTopSceneInspection(
        bool Valid,
        IReadOnlyList<Rect> Bounds,
        int WaveColumns);

    private sealed record MainNightReferenceGeometryResult(
        bool Matches,
        IReadOnlyList<string[]> CharacterBounds,
        IReadOnlyList<int> WaveColumnCounts);

    private sealed record NoteDateSection(
        string Key,
        FrameworkElement Root,
        Button Toggle,
        StackPanel Rows);

    private sealed record NoteSectionExerciseResult(
        bool DateSectionsCanCollapse,
        bool DateSectionsAreIndependent,
        bool DateSectionStateSurvivesRefresh,
        bool StatusSectionsAreIndependent);

    private sealed record MainThemeExerciseResult(
        IReadOnlyList<string> BoundaryModes,
        IReadOnlyList<string> BackgroundColors,
        IReadOnlyList<int> CharacterOpacities,
        IReadOnlyList<string> CharacterPanelColors,
        bool UsesChinaStandardTime,
        bool ReminderLabelFollowsTheme,
        bool SegmentSelectedTextContrast,
        bool CharacterPanelDocksFollowTheme,
        bool ReferenceDecorationsFollowTheme);

    private sealed record ThemePreferenceButtonExerciseResult(
        bool CyclesViaClick,
        bool LabelsFollowPreference,
        bool PersistsEachMode,
        bool IsAccessible,
        bool UsesProjectTheme,
        bool WindowsStaySynchronized)
    {
        public static ThemePreferenceButtonExerciseResult NotRun { get; } =
            new(false, false, false, false, false, false);
    }

    private static FloatingQuickAddThemeValidation ValidateFloatingQuickAddLocalTheme(
        ApplicationTheme applicationTheme,
        FloatingWindow floating)
    {
        var originalApplicationMode = applicationTheme.ActiveMode;
        var originalFloatingMode = floating.CurrentThemeMode;
        try
        {
            applicationTheme.Apply(new DateTimeOffset(
                2030,
                1,
                15,
                6,
                59,
                0,
                TimeSpan.FromHours(8)).ToUniversalTime());
            floating.ApplyTheme(ApplicationThemeMode.Day);
            PumpDispatcher();
            var dayIndependent = !ReferenceEquals(
                floating.FindResource("CanvasBrush"),
                Application.Current.FindResource("CanvasBrush"));
            var day = floating.ProbeQuickAddThemeResources();
            var dayCanvas = ResourceColor(floating, "CanvasBrush");
            var dayActive = ResourceColor(floating, "ActiveBrush");

            applicationTheme.Apply(new DateTimeOffset(
                2030,
                1,
                15,
                7,
                0,
                0,
                TimeSpan.FromHours(8)).ToUniversalTime());
            floating.ApplyTheme(ApplicationThemeMode.Night);
            PumpDispatcher();
            var nightIndependent = !ReferenceEquals(
                floating.FindResource("CanvasBrush"),
                Application.Current.FindResource("CanvasBrush"));
            var night = floating.ProbeQuickAddThemeResources();
            var nightCanvas = ResourceColor(floating, "CanvasBrush");
            var nightActive = ResourceColor(floating, "ActiveBrush");

            return new FloatingQuickAddThemeValidation(
                day,
                night,
                new[] { dayCanvas, nightCanvas },
                new[] { dayActive, nightActive },
                dayIndependent &&
                nightIndependent &&
                day.Mode == ApplicationThemeMode.Day &&
                night.Mode == ApplicationThemeMode.Night &&
                dayCanvas == "#FFFFFDFC" &&
                nightCanvas == "#FF473323" &&
                dayActive == "#FFA24438" &&
                nightActive == "#FF6E492E");
        }
        finally
        {
            applicationTheme.Apply(originalApplicationMode);
            floating.ApplyTheme(originalFloatingMode);
            PumpDispatcher();
        }
    }

    private static string ResourceColor(FrameworkElement element, string resourceKey) =>
        element.FindResource(resourceKey) is SolidColorBrush brush
            ? brush.Color.ToString()
            : string.Empty;

    private sealed record FloatingQuickAddThemeValidation(
        FloatingQuickAddThemeProbeResult Day,
        FloatingQuickAddThemeProbeResult Night,
        IReadOnlyList<string> CanvasColors,
        IReadOnlyList<string> ActiveColors,
        bool IndependentFromApplicationTheme);

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
        string NightCountdownCapturePath,
        string NightNotesCapturePath,
        string ManifestPath);

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
        MainStressCountdownTitleMeasurement? StressCountdownTitle);

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
            StressCountdownTitleMeasurements.Count == 2 &&
            StressCountdownTitleMeasurements
                .Select(measurement => measurement.CaptureId)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    new[]
                    {
                        "main-day-countdowns-stress",
                        "main-night-countdowns-stress"
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
                        measurement.SeparatedFromActions)).AllPassed);

        public static MainReferenceBaselineValidation NotRequested { get; } = new(
            false,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            FailedGates,
            Array.Empty<MainReferenceBaselineCaptureMeasurement>(),
            Array.Empty<MainStressCountdownTitleMeasurement>(),
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
            Measurements.Count == 4 &&
            Captures.Count == 4;

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
        bool AccentTrackMatchesText,
        bool AccentDiffersFromPlushGlow);

    private sealed record FloatingPrimaryCountdownContrastValidation(
        FloatingPrimaryCountdownContrastGateResults Gates,
        IReadOnlyList<FloatingPrimaryCountdownContrastMeasurement> Measurements);

    private sealed record FloatingReferenceExerciseResult(
        bool AspectMatches,
        bool PanelGeometryMatches,
        bool MascotGeometryMatches,
        FloatingReferenceBaselineValidation Baseline,
        FloatingLongNotesValidation LongNotes,
        FloatingPrimaryCountdownContrastValidation PrimaryCountdownContrast,
        FloatingNightPlushEdgeGlowValidation NightPlushEdgeGlow,
        bool ContentSafeAreaMatches,
        bool TitleUsesSemiBoldWeight,
        bool NoteInputWraps,
        bool NoteInputScrolls,
        bool QuickAddDayExpandedScrolledCaptureWritten,
        bool QuickAddNightExpandedScrolledCaptureWritten,
        int ContentOverlapCount,
        IReadOnlyList<string> ContentOverlapTargets,
        bool CaptureWritten,
        bool WorkdayCountdownUsesPrimaryVisual,
        bool ScenarioMatches,
        bool PrimaryDeadlineTextFullyVisible,
        bool ViewportStatesMatch,
        bool RasterCardStatesMatch,
        bool DynamicCardContentRemainsWpf,
        bool ViewportScrollAndFocusRestored,
        bool NoteFailureAppearsInSourceCard,
        bool ResolvedNoteCardErrorDoesNotReturn,
        bool SecondaryRemainingTextFullyVisible,
        FloatingSecondaryRemainingGateResults SecondaryRemainingGates,
        IReadOnlyList<FloatingSecondaryRemainingMeasurement> SecondaryRemainingSamples,
        FloatingScrollBarValidation ScrollBar,
        IReadOnlyList<double> ActualSize,
        string CapturePath);

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
        string BorderColor);

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
            Captures.Count == 6 &&
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

    private sealed record PlushEdgeTarget(
        string Name,
        Image Image,
        Border Anchor);

    private sealed record FloatingNightPlushEdgeMeasurement(
        string Role,
        int RoiPixelCount,
        int VisibleEffectSampleCount,
        double BrightnessSignal,
        double Saturation,
        string AggregateEffectColor);

    private sealed record FloatingNightPlushEdgeGlowValidation(
        bool SourceAlphaBoundsMatch,
        IReadOnlyList<string> SourceAlphaBounds,
        IReadOnlyList<FloatingNightPlushEdgeMeasurement> Measurements,
        FloatingNightPlushEdgeGateResults Gates,
        string Algorithm)
    {
        public static FloatingNightPlushEdgeGlowValidation NotRun { get; } = new(
            false,
            Array.Empty<string>(),
            Array.Empty<FloatingNightPlushEdgeMeasurement>(),
            new FloatingNightPlushEdgeGateResults(false, false),
            string.Empty);
    }

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
        bool RepeatingReminderPanelVisible,
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

    private sealed class ValidationClock : IClock
    {
        public ValidationClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
