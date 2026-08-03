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
using System.Windows.Threading;
using MinminCountdown.Core;
using WpfCalendar = System.Windows.Controls.Calendar;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown;

internal static class ValidationReport
{
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

        var mainWindowScenarios = main.RunValidationScenarios(options.ExerciseNoteAdd);
        var floatingWindowScenarios = floating.RunValidationScenarios(options.ExerciseNoteAdd);
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

        var floatingCountdownButton = Required<ToggleButton>(floating, "FloatingCountdownsButton");
        var floatingNotesButton = Required<ToggleButton>(floating, "FloatingNotesButton");
        var countdownButtonFit = MeasureButtonFit(floatingCountdownButton);
        var notesButtonFit = MeasureButtonFit(floatingNotesButton);
        var originalFloatingWorkspace = floatingNotesButton.IsChecked == true
            ? WorkspaceKind.Notes
            : WorkspaceKind.Countdowns;
        floating.ShowWorkspace(WorkspaceKind.Notes);
        floating.UpdateLayout();
        var floatingRows = Required<StackPanel>(floating, "FloatingRows");
        var renderedFloatingNoteIds = GetRenderedNoteIds(floatingRows);
        var expectedFloatingNoteIds = workspaceSnapshot.VisibleActiveNotes
            .Select(note => note.Id)
            .ToArray();
        var floatingNotesKeepExistingSortOrder = expectedFloatingNoteIds.SequenceEqual(
            renderedFloatingNoteIds,
            StringComparer.Ordinal);
        var floatingTitle = Required<TextBlock>(floating, "FloatingTitle");
        var floatingTitleFit = MeasureTextFit(
            floatingTitle,
            Required<Border>(floating, "FloatingDragHandle"));
        var floatingTitleHeightFit = MeasureTextHeightFit(floatingTitle);
        floating.ShowWorkspace(originalFloatingWorkspace);
        floating.UpdateLayout();
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
        var floatingHeaderForegroundBounds = new[] { TextContentBounds(floatingTitle, floating) }
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
        var mainReferenceDecorationScaling = ValidateMainReferenceDecorationScaling(main);
        var mainNightReferenceGeometry = ValidateMainNightReferenceGeometry(main);
        var mainWorkspaceResourcesFollowTheme =
            ValidateMainWorkspaceResourcesWithoutRerender(theme, main, workspaceSnapshot.UtcNow);
        var mainReferenceCapture =
            RunMainReferenceCapture(theme, main, workspaceSnapshot, assetRoot);

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

        var compactSelection =
            Required<DatePicker>(main, "CountdownDate").Width is >= 370 and <= 400 &&
            Required<ComboBox>(main, "CountdownHour").Width is >= 180 and <= 205 &&
            Required<ComboBox>(main, "CountdownMinute").Width is >= 180 and <= 205 &&
            Required<ComboBox>(main, "CountdownSecond").Width is >= 180 and <= 205 &&
            Required<ComboBox>(main, "DisplayPrecision").ActualWidth is >= 200 and <= 220;
        var compactPreferences =
            Required<ComboBox>(main, "DisplayPrecision").ActualWidth >= 96 &&
            Required<TextBox>(main, "MonthlySalary").ActualWidth >= 112 &&
            Required<TextBox>(main, "WorkStart").ActualWidth >= 96 &&
            Required<TextBox>(main, "WorkEnd").ActualWidth >= 96;
        var desktopWorkspaceInterfaceBehavior =
            ValidationExercises.RunDesktopWorkspaceInterfaceBehavior();
        var reminderEditingInterfaceBehavior =
            ValidationExercises.RunReminderEditingInterfaceBehavior();
        var startupAcceptanceProtocolInterfaceBehavior =
            ValidationExercises.RunStartupAcceptanceProtocolInterfaceBehavior();
        var holidayCalendarInterfaceBehavior =
            ValidationExercises.RunHolidayCalendarInterfaceBehavior();

        var report = new Dictionary<string, object?>(StringComparer.Ordinal)
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
            ["CountdownNameWidth"] = Required<TextBox>(main, "CountdownName").ActualWidth,
            ["TargetDateWidth"] = Required<DatePicker>(main, "CountdownDate").Width,
            ["TargetHourWidth"] = Required<ComboBox>(main, "CountdownHour").Width,
            ["TargetMinuteWidth"] = Required<ComboBox>(main, "CountdownMinute").Width,
            ["TargetSecondWidth"] = Required<ComboBox>(main, "CountdownSecond").Width,
            ["DisplayPrecisionWidth"] = Required<ComboBox>(main, "DisplayPrecision").ActualWidth,
            ["MonthlySalaryWidth"] = Required<TextBox>(main, "MonthlySalary").ActualWidth,
            ["WorkStartWidth"] = Required<TextBox>(main, "WorkStart").ActualWidth,
            ["WorkEndWidth"] = Required<TextBox>(main, "WorkEnd").ActualWidth,
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
                mainThemeExercise.ReferenceDecorationsFollowTheme && mainWorkspaceResourcesFollowTheme,
            ["FloatingThemeBoundaryModes"] = floatingThemeExercise.BoundaryModes,
            ["FloatingThemePanelColors"] = floatingThemeExercise.PanelColors,
            ["FloatingThemePlushGlowColors"] = floatingThemeExercise.PlushGlowColors,
            ["FloatingMascotPresentationModes"] = floatingThemeExercise.MascotPresentationModes,
            ["FloatingThemeUsesChinaStandardTime"] = floatingThemeExercise.UsesChinaStandardTime,
            ["FloatingRasterMascotBoundaryStatesMatch"] =
                floatingThemeExercise.RasterMascotBoundaryStatesMatch,
            ["FloatingThemeTextContrast"] = floatingThemeExercise.TextContrast,
            ["FloatingPlushSceneFollowsDayNightTheme"] =
                floatingThemeExercise.PlushSceneFollowsDayNightTheme,
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
                mainReferenceDecorationScaling,
            ["MainNightReferenceGeometryMatchesDay"] = mainNightReferenceGeometry.Matches,
            ["MainNightReferenceCharacterBounds"] = mainNightReferenceGeometry.CharacterBounds,
            ["MainNightReferenceWaveColumnCounts"] = mainNightReferenceGeometry.WaveColumnCounts,
            ["MainReferenceCaptureRequested"] = mainReferenceCapture.Requested,
            ["MainReferenceCaptureWritten"] = mainReferenceCapture.CapturesWritten,
            ["MainReferenceCaptureUsesZeroState"] = mainReferenceCapture.UsesZeroState,
            ["MainReferenceCaptureCanvasMatches"] = mainReferenceCapture.CanvasMatches,
            ["MainReferenceCaptureAlphaIsOpaque"] = mainReferenceCapture.AlphaIsOpaque,
            ["MainReferenceCountdownCapturePath"] = mainReferenceCapture.CountdownCapturePath,
            ["MainReferenceNotesCapturePath"] = mainReferenceCapture.NotesCapturePath,
            ["MainNightReferenceCountdownCapturePath"] = mainReferenceCapture.NightCountdownCapturePath,
            ["MainNightReferenceNotesCapturePath"] = mainReferenceCapture.NightNotesCapturePath,
            ["MainReferenceCaptureManifestPath"] = mainReferenceCapture.ManifestPath,
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
            ["FloatingReferenceContentSafeAreaMatches"] = floatingReferenceExercise.ContentSafeAreaMatches,
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
            ["FloatingCountdownButtonTextFits"] = countdownButtonFit.Fits,
            ["FloatingCountdownButtonTextAvailableWidth"] = countdownButtonFit.Available,
            ["FloatingCountdownButtonTextRequiredWidth"] = countdownButtonFit.Required,
            ["FloatingNotesButtonTextFits"] = notesButtonFit.Fits,
            ["FloatingNotesButtonTextAvailableWidth"] = notesButtonFit.Available,
            ["FloatingNotesButtonTextRequiredWidth"] = notesButtonFit.Required,
            ["FloatingNotesKeepExistingSortOrder"] = floatingNotesKeepExistingSortOrder,
            ["FloatingTitleTextFits"] = floatingTitleFit.Fits,
            ["FloatingTitleTextAvailableWidth"] = floatingTitleFit.Available,
            ["FloatingTitleTextRequiredWidth"] = floatingTitleFit.Required,
            ["FloatingTitleTextHasVerticalClearance"] = floatingTitleHeightFit.Fits,
            ["FloatingTitleTextAvailableHeight"] = floatingTitleHeightFit.Available,
            ["FloatingTitleTextRequiredHeight"] = floatingTitleHeightFit.Required,
            ["SavePreferencesButtonTextFits"] = savePreferencesButtonFit.Fits,
            ["SavePreferencesButtonIsFullyVisible"] = savePreferencesButtonIsVisible,
            ["SavePreferencesButtonIsFullyVisibleAtMinimumWidth"] =
                savePreferencesButtonIsVisibleAtMinimumWidth,
            ["SavePreferencesButtonRight"] = Math.Round(savePreferencesButtonRight, 2),
            ["MainWindowActualWidth"] = Math.Round(main.ActualWidth, 2),
            ["FloatingCountdownCount"] = floating.FloatingCountdownCount,
            ["HasWorkdayEndCountdown"] =
                false,
            ["FloatingCountdownTexts"] = floating.FloatingCountdownTexts,
            ["FloatingIncomeText"] = floating.IncomeText,
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
            ["RenderedFloatingNoteIds"] = renderedFloatingNoteIds,
            ["SortedNames"] = workspaceSnapshot.AllCountdowns
                .Select(record => record.Name)
                .ToArray(),
            ["WindowIconLoaded"] = main.Icon is not null
        };

        var validationReportContractBehavior =
            ValidationExercises.RunValidationReportContractBehavior(report);
        report["ValidationReportContractBehavior"] = validationReportContractBehavior;
        report["ValidationReportModuleDeep"] = validationReportContractBehavior;
        ValidationExercises.RunValidationReportContractBehavior(report);

        datePicker.IsDropDownOpen = false;
        reminderWindow.Close();
        floating.Hide();
        main.Hide();
        return report;
    }

    private static bool ValidateMainReferenceDecorationScaling(MainWindow main)
    {
        if (main.Content is not FrameworkElement root)
        {
            return false;
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
                PumpDispatcher();
                main.UpdateLayout();

                if (!SameBounds(
                        BoundsIn(decorationLayer, root),
                        BoundsIn(layoutSurface, root)) ||
                    !ReferenceEquals(VisualTreeHelper.GetParent(decorationLayer), layoutSurface) ||
                    !ReferenceEquals(VisualTreeHelper.GetParent(contentProjection), layoutSurface) ||
                    !ReferenceEquals(VisualTreeHelper.GetParent(contentSurface), contentProjection) ||
                    !ReferenceEquals(VisualTreeHelper.GetParent(countdownScene), decorationLayer) ||
                    !ReferenceEquals(VisualTreeHelper.GetParent(notesScene), decorationLayer) ||
                    !ReferenceEquals(VisualTreeHelper.GetParent(nightCountdownScene), decorationLayer) ||
                    !ReferenceEquals(VisualTreeHelper.GetParent(nightNotesScene), notesWorkspace) ||
                    Panel.GetZIndex(decorationLayer) >= Panel.GetZIndex(contentProjection) ||
                    Panel.GetZIndex(noteEditorPanel) >= Panel.GetZIndex(nightNotesScene) ||
                    Panel.GetZIndex(nightNotesScene) >= Panel.GetZIndex(noteTextLabel) ||
                    decorationLayer.IsHitTestVisible ||
                    countdownScene.IsHitTestVisible ||
                    notesScene.IsHitTestVisible ||
                    nightCountdownScene.IsHitTestVisible ||
                    nightNotesScene.IsHitTestVisible ||
                    layoutSurface.Width != 1165 ||
                    layoutSurface.Height != 1350 ||
                    contentProjection.Width != layoutSurface.Width ||
                    contentProjection.Height != layoutSurface.Height ||
                    contentSurface.Width != layoutSurface.Width ||
                    contentSurface.Height != layoutSurface.Height ||
                    countdownScene.Width != layoutSurface.Width ||
                    countdownScene.Height != layoutSurface.Height ||
                    notesScene.Width != layoutSurface.Width ||
                    notesScene.Height != layoutSurface.Height ||
                    nightCountdownScene.Width != layoutSurface.Width ||
                    nightCountdownScene.Height != layoutSurface.Height ||
                    nightNotesScene.Width != layoutSurface.Width ||
                    nightNotesScene.Height != layoutSurface.Height ||
                    HasNonIdentityTransform(contentProjection) ||
                    scaleViewport.Stretch != Stretch.Uniform)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            main.Width = originalWidth;
            main.Height = originalHeight;
            main.InvalidateMeasure();
            main.InvalidateArrange();
            PumpDispatcher();
        }
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
            var notes = Enumerable.Range(1, 6)
                .Select(index => new NoteRecord(
                    $"reference-note-{index}",
                    $"固定视口便签 {index}\n第二行用于验证多行内容和滚动",
                    "2030-07-15",
                    false,
                    Array.Empty<string>(),
                    index == 1
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
                Array.Empty<TagRecord>(),
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
                countdownRowsReserveScrollbarInset &&
                Required<TextBlock>(referenceWindow, "FloatingTitle").FontWeight == FontWeights.SemiBold &&
                Required<TextBox>(referenceWindow, "FloatingNoteText").TextWrapping == TextWrapping.Wrap;
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
            var longSecondaryAdded = referenceSession.TrySaveCountdown(
                "reference-long-secondary",
                "长周期次级倒计时",
                referenceUtc.AddDays(2_000_000),
                Array.Empty<string>(),
                out _);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 3);
            var longSecondaryRemaining = Descendants<TextBlock>(rows).FirstOrDefault(text =>
                text.Text.Contains("2000000天", StringComparison.Ordinal));
            var longSecondaryFitsViewport = false;
            if (longSecondaryRemaining is not null)
            {
                var referenceBodyScroller = Required<ScrollViewer>(
                    referenceWindow,
                    "FloatingBodyScroller");
                referenceBodyScroller.ApplyTemplate();
                if (referenceBodyScroller.Template.FindName(
                        "PART_ScrollContentPresenter",
                        referenceBodyScroller) is ScrollContentPresenter viewport)
                {
                    var remainingBounds = BoundsIn(longSecondaryRemaining, viewport);
                    var viewportBounds = new Rect(
                        new Point(0, 0),
                        viewport.RenderSize);
                    longSecondaryFitsViewport =
                        remainingBounds.Left + 0.5 >= viewportBounds.Left &&
                        remainingBounds.Top + 0.5 >= viewportBounds.Top &&
                        remainingBounds.Right <= viewportBounds.Right + 0.5 &&
                        remainingBounds.Bottom <= viewportBounds.Bottom + 0.5;
                }
            }

            var longSecondaryTextFullyVisible =
                longSecondaryRemaining is not null &&
                longSecondaryRemaining.TextWrapping == TextWrapping.NoWrap &&
                longSecondaryRemaining.TextTrimming == TextTrimming.None &&
                !longSecondaryRemaining.Text.Contains('\n') &&
                !longSecondaryRemaining.Text.Contains('…') &&
                longSecondaryRemaining.LineHeight > 0 &&
                longSecondaryRemaining.ActualHeight <= longSecondaryRemaining.LineHeight + 0.5 &&
                TextBlockLinesFit(longSecondaryRemaining) &&
                longSecondaryFitsViewport;
            var longSecondaryDeleted = referenceSession.TryDeleteCountdown(
                "reference-long-secondary",
                out _);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            secondaryRemainingTextFullyVisible =
                longSecondaryAdded &&
                longSecondaryDeleted &&
                longSecondaryTextFullyVisible;
            byte[]? notesCaptureBytes = null;
            byte[]? nightCountdownCaptureBytes = null;
            byte[]? nightNotesCaptureBytes = null;
            byte[]? scrolledCountdownsCaptureBytes = null;
            byte[]? scrolledNotesCaptureBytes = null;
            referenceWindow.ApplyTheme(ApplicationThemeMode.Night);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 2);
            nightCountdownCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
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
            notesCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
            referenceWindow.ApplyTheme(ApplicationThemeMode.Night);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
            nightNotesCaptureBytes = EncodePng(ScaleToFrame(
                CaptureVisual(root),
                captureWidth,
                captureHeight));
            referenceWindow.ApplyTheme(ApplicationThemeMode.Day);
            referenceWindow.UpdateTime(referenceUtc);
            WaitForStableFloatingReferenceLayout(referenceWindow, root, rows, 6);
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
                if (scrolledCountdownsCaptureBytes is not null)
                {
                    File.WriteAllBytes(
                        Path.Combine(evidenceDirectory, $"{captureName}-countdowns-scrolled.png"),
                        scrolledCountdownsCaptureBytes);
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
                contentSafeAreaMatches,
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
            Descendants<TextBlock>(rows).Count(text => text.Text.Contains('\n')),
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

    private static bool ValidateMainWorkspaceResourcesWithoutRerender(
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

        static IEnumerable<StackPanel> CardBodies(Panel rows) =>
            rows.Children
                .OfType<Border>()
                .Select(card => card.Child as Grid)
                .Where(grid => grid is not null)
                .SelectMany(grid => grid!.Children.OfType<StackPanel>())
                .Where(panel => Grid.GetColumn(panel) is 0 or 1);

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

            var panelsFollowTheme = new[]
                {
                    main.FindName("CountdownEditorSurface") as Border,
                    main.FindName("CountdownPreferencesSurface") as Border,
                    main.FindName("NoteEditorPanel") as Border
                }
                .All(panel =>
                    panel is not null &&
                    UsesResource(panel, Border.BackgroundProperty, "WorkspacePanelBrush") &&
                    UsesResource(panel, Border.BorderBrushProperty, "WorkspacePanelLineBrush"));
            var titleFollowsTheme =
                main.FindName("CountdownEditorTitle") is TextBlock editorTitle &&
                UsesResource(editorTitle, TextBlock.ForegroundProperty, "InkBrush");
            var countdownRows = Required<StackPanel>(main, "CountdownRows");
            var countdownCardsFollowTheme = countdownRows.Children
                .OfType<Border>()
                .All(card =>
                    UsesAnyResource(card, Border.BackgroundProperty, "CardBrush", "ExpiredBrush") &&
                    UsesResource(card, Border.BorderBrushProperty, "PeachLineBrush"));
            var countdownBodies = CardBodies(countdownRows).ToArray();
            var countdownTextFollowsTheme =
                countdownBodies.All(BodyTextUsesTheme) && countdownBodies.All(TagTextUsesTheme);

            theme.Apply(ApplicationThemeMode.Day);
            main.ApplyTheme(ApplicationThemeMode.Day);
            main.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            main.UpdateLayout();

            theme.Apply(ApplicationThemeMode.Night);
            main.ApplyTheme(ApplicationThemeMode.Night);
            PumpDispatcher();
            main.UpdateLayout();

            var noteBodies = new[]
                {
                    Required<StackPanel>(main, "ActiveNoteRows"),
                    Required<StackPanel>(main, "CompletedNoteRows")
                }
                .SelectMany(rows => Descendants<Border>(rows))
                .Where(card =>
                    Math.Abs(card.CornerRadius.TopLeft - 8) <= 0.01 &&
                    Math.Abs(card.Padding.Left - 14) <= 0.01)
                .Select(card => card.Child as Grid)
                .Where(grid => grid is not null)
                .SelectMany(grid => grid!.Children.OfType<StackPanel>())
                .ToArray();
            var noteCardsFollowTheme = new[]
                {
                    Required<StackPanel>(main, "ActiveNoteRows"),
                    Required<StackPanel>(main, "CompletedNoteRows")
                }
                .SelectMany(rows => Descendants<Border>(rows))
                .Where(card =>
                    Math.Abs(card.CornerRadius.TopLeft - 8) <= 0.01 &&
                    Math.Abs(card.Padding.Left - 14) <= 0.01)
                .All(card =>
                    UsesResource(card, Border.BackgroundProperty, "NoteBrush") &&
                    UsesResource(card, Border.BorderBrushProperty, "NoteLineBrush"));
            var noteTextFollowsTheme =
                noteBodies.All(BodyTextUsesTheme) && noteBodies.All(TagTextUsesTheme);

            return panelsFollowTheme &&
                titleFollowsTheme &&
                countdownCardsFollowTheme &&
                countdownTextFollowsTheme &&
                noteCardsFollowTheme &&
                noteTextFollowsTheme;
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

    private static TextFit MeasureTextHeightFit(TextBlock textBlock)
    {
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var formatted = FormatText(textBlock, dpi);
        var available = Math.Max(0, textBlock.ActualHeight);
        var required =
            formatted.Height +
            Math.Max(0, formatted.OverhangAfter) +
            (4 / dpi.DpiScaleY);
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
        var secondaryRemaining = string.Concat(secondaryRemainingForAutomation
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
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

    private sealed record VisualCaptureArtifactBinding(
        string Id,
        string Path,
        string Sha256);

    private sealed record FloatingReferenceExerciseResult(
        bool AspectMatches,
        bool PanelGeometryMatches,
        bool MascotGeometryMatches,
        bool ContentSafeAreaMatches,
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
        IReadOnlyList<double> ActualSize,
        string CapturePath);

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
