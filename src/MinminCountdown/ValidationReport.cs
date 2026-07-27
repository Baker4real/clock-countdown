using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinminCountdown.Core;
using WpfCalendar = System.Windows.Controls.Calendar;

namespace MinminCountdown;

internal static class ValidationReport
{
    public static IReadOnlyDictionary<string, object?> Create(
        MainWindow main,
        FloatingWindow floating,
        ApplicationSession session,
        ApplicationTheme theme,
        HostOptions options,
        string assetRoot)
    {
        var noteExercise = options.ExerciseNoteAdd
            ? main.RunNoteAddExercise()
            : new NoteExerciseResult(false, false, false, false, false);
        var floatingNoteExercise = options.ExerciseNoteAdd && floating.RunCrossDayReminderAddExercise();
        var mainReminderModeExercise = options.ExerciseNoteAdd
            ? main.RunReminderModeClickExercises()
            : ReminderModeClickResult.NotRun;
        var floatingReminderModeExercise = options.ExerciseNoteAdd
            ? floating.RunReminderModeClickExercises()
            : ReminderModeClickResult.NotRun;
        var tagExercise = options.ExerciseTagInteractions
            ? ValidationExercises.RunTagInteractions(session)
            : TagInteractionExerciseResult.NotRun;
        var persistenceExercise = options.ExercisePersistenceFailures
            ? ValidationExercises.RunPersistenceFailures(session)
            : PersistenceFailureExerciseResult.NotRun;

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

        var datePicker = Required<DatePicker>(main, "CountdownDate");
        datePicker.ApplyTemplate();
        var dateTextBox = datePicker.Template.FindName("PART_TextBox", datePicker) as DatePickerTextBox;
        var dateButton = datePicker.Template.FindName("PART_Button", datePicker) as Button;
        var datePopup = datePicker.Template.FindName("PART_Popup", datePicker) as Popup;
        var dateCalendar = datePicker.Template.FindName("PART_Calendar", datePicker) as WpfCalendar;
        var dateParts = new object?[] { dateTextBox, dateButton, datePopup, dateCalendar };

        datePicker.IsDropDownOpen = false;
        dateButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpDispatcher();
        var dateButtonOpens = datePicker.IsDropDownOpen;
        datePicker.IsDropDownOpen = true;
        PumpDispatcher();
        dateCalendar?.ApplyTemplate();
        dateCalendar?.UpdateLayout();
        var livePopupThemed =
            datePopup?.IsOpen == true &&
            datePopup.Child is WpfCalendar &&
            dateCalendar?.Template is not null;
        var livePopupUnified =
            dateCalendar is not null &&
            Equals(dateCalendar.Background, Application.Current.FindResource("CanvasBrush")) &&
            Equals(dateCalendar.BorderBrush, Application.Current.FindResource("PeachLineBrush"));
        datePicker.IsDropDownOpen = false;

        var dateBehaviorAttached = ThemedDatePickerBehavior.IsAttached(datePicker);
        var dateTextMetrics = MeasureTextFit(datePicker, "2031年12月31日");
        var calendarHeaderMetrics = MeasureTextFit(datePicker, "2031年12月");
        var workspaceDraftsRetained = main.ProbeWorkspaceDraftRetention();
        var noteDateBehavior = main.ProbeNoteDateBehavior();
        var originalWorkspace = main.CurrentWorkspace;
        main.ShowWorkspace(WorkspaceKind.Notes);
        var hiddenCountdownSubmitDisabled = !Required<Button>(main, "SaveCountdownButton").IsEnabled;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var switchesToCountdown = Required<Grid>(main, "CountdownWorkspace").Visibility == Visibility.Visible;
        main.ShowWorkspace(WorkspaceKind.Notes);
        var switchesToNotes = Required<Grid>(main, "NotesWorkspace").Visibility == Visibility.Visible;
        main.ShowWorkspace(originalWorkspace);

        var selector = Required<TagSelector>(main, "CountdownTags");
        var createAppears = selector.ProbeCreateAction("验证新标签");
        var createCollapsed = !selector.ProbeCreateAction(string.Empty);
        var tagSearchDoesNotSubmit = !selector.CreateButtonIsDefault;
        var reminderBehavior = ValidateReminderDispatch();
        var oneTimeReminderBehavior = ValidateOneTimeReminderLifecycle();
        var crossDayReminderBehavior = main.ProbeCrossDayReminderBehavior();
        var reminderSummaryAdvances = main.ProbeReminderSummaryAdvancesAfterFirstDue();
        var roundTrip = ValidateRoundTrip(session.Current);
        var legacyMigration = ValidateLegacyMigration();
        var duplicateRejected = RejectsDuplicateCountdowns(session.Current);
        var invalidNotesRejected = RejectsInvalidNotes(session.Current);
        var invalidTagReferencesRejected = RejectsDanglingTag(session.Current);

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
        var countdownActionButtons = Descendants<Button>(countdownRows)
            .Where(button => button.Content?.ToString() is "编辑" or "删除")
            .ToArray();
        var noteActionButtons = Descendants<Button>(activeNoteRows)
            .Concat(Descendants<Button>(completedNoteRows))
            .Where(button => button.Content?.ToString() is "编辑" or "删除")
            .ToArray();
        var cardActionsBound = countdownActionButtons.All(button => button.Tag is string);
        var noteActionsBound = noteActionButtons.All(button => button.Tag is string);
        var noteTextValues = Descendants<TextBlock>(activeNoteRows)
            .Concat(Descendants<TextBlock>(completedNoteRows))
            .Select(text => text.Text)
            .ToArray();
        var noteStatusLabels = noteTextValues.Any(text =>
            text.Contains("未完成", StringComparison.Ordinal) ||
            text.Contains("已完成", StringComparison.Ordinal));
        var noteDatesRendered = session.Current.Notes
            .Where(note => note.Date is not null)
            .All(note => noteTextValues.Any(text => text.Contains(note.Date!, StringComparison.Ordinal)));
        var notesGroupedByDay = AreNotesGroupedByDay(session.GetSortedNotes(applyFilter: false));
        var activeNotesToggle = main.FindName("ToggleActiveNotes") as Button;
        var activeNotesGrouped =
            activeNotesToggle is not null &&
            activeNoteRows.Parent is StackPanel noteSections &&
            noteSections.Children.IndexOf(activeNotesToggle) >= 0 &&
            noteSections.Children.IndexOf(activeNoteRows) == noteSections.Children.IndexOf(activeNotesToggle) + 1;
        var expectedActiveDateOrder = session.GetSortedNotes()
            .Where(note => !note.IsCompleted)
            .Select(note => note.Date ?? "未设置日期")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var renderedActiveDateOrder = activeNoteRows.Children
            .OfType<TextBlock>()
            .Select(text => text.Text)
            .Where(text =>
                string.Equals(text, "未设置日期", StringComparison.Ordinal) ||
                DateTime.TryParseExact(
                    text,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            .ToArray();
        var activeNotesKeepDateOrder = expectedActiveDateOrder.SequenceEqual(
            renderedActiveDateOrder,
            StringComparer.Ordinal);
        var tagChips = Descendants<Border>(countdownRows)
            .Concat(Descendants<Border>(activeNoteRows))
            .Where(border => !string.IsNullOrWhiteSpace(border.Uid))
            .ToArray();
        var tagChipsUseUid = tagChips.All(chip => chip.Uid.Length > 0);

        var floatingCountdownButton = Required<ToggleButton>(floating, "FloatingCountdownsButton");
        var floatingNotesButton = Required<ToggleButton>(floating, "FloatingNotesButton");
        var countdownButtonFit = MeasureButtonFit(floatingCountdownButton);
        var notesButtonFit = MeasureButtonFit(floatingNotesButton);
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
        var mainCharacterDocksIntegrateWithPanel = mainCharacterDocks.All(pair =>
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
        var workArea = SystemParameters.WorkArea;
        var floatingBottomMargin = Math.Round(workArea.Bottom - (floating.Top + floating.ActualHeight), 2);
        var taskCard = Required<Border>(floating, "TaskCard");
        var taskCardBounds = taskCard
            .TransformToAncestor(floating)
            .TransformBounds(new Rect(new Point(0, 0), taskCard.RenderSize));
        var taskCardContent = taskCard.Child as FrameworkElement ??
                              throw new InvalidOperationException("悬浮窗卡片缺少内容容器。");
        var taskCardContentBounds = taskCardContent
            .TransformToAncestor(floating)
            .TransformBounds(new Rect(new Point(0, 0), taskCardContent.RenderSize));
        var plushDecorations = new[]
        {
            Required<Image>(floating, "PlushTopLeft"),
            Required<Image>(floating, "PlushTopRight"),
            Required<Image>(floating, "PlushBottomRight")
        };
        var plushDocks = new[]
        {
            (Dock: Required<Border>(floating, "MyMelodyPlushDock"), Image: plushDecorations[0]),
            (Dock: Required<Border>(floating, "CinnamorollPlushDock"), Image: plushDecorations[1]),
            (Dock: Required<Border>(floating, "KuromiPlushDock"), Image: plushDecorations[2])
        };
        var plushBounds = plushDecorations
            .Select(image => image
                .TransformToAncestor(floating)
                .TransformBounds(new Rect(new Point(0, 0), image.RenderSize)))
            .ToArray();
        var visiblePlushBounds = plushDecorations
            .Select(image => VisibleImageBounds(image, floating))
            .ToArray();
        var plushBridgesTaskCardEdges = plushBounds.All(bounds =>
        {
            var overlap = Rect.Intersect(bounds, taskCardBounds);
            return overlap.Width >= 12 &&
                   overlap.Height >= 12 &&
                   overlap.Width < bounds.Width &&
                   overlap.Height < bounds.Height;
        });
        var plushDoesNotObscureTaskCardContent = plushBounds.All(bounds =>
            !bounds.IntersectsWith(taskCardContentBounds));
        var visiblePlushDoesNotObscureTaskCardContent = visiblePlushBounds.All(bounds =>
            bounds is { } visible && !visible.IntersectsWith(taskCardContentBounds));
        var visiblePlushEdgesMeetTaskCard = visiblePlushBounds.All(bounds =>
        {
            if (bounds is not { } visible)
            {
                return false;
            }

            var overlap = Rect.Intersect(visible, taskCardBounds);
            return Math.Min(overlap.Width, overlap.Height) >= 6;
        });
        var plushDocksIntegrateWithPanel = plushDocks.All(pair =>
        {
            var dockBounds = pair.Dock
                .TransformToAncestor(floating)
                .TransformBounds(new Rect(new Point(0, 0), pair.Dock.RenderSize));
            var overlap = Rect.Intersect(dockBounds, taskCardBounds);
            return
                ReferenceEquals(pair.Dock.Child, pair.Image) &&
                Panel.GetZIndex(pair.Dock) > Panel.GetZIndex(taskCard) &&
                pair.Dock.CornerRadius.TopLeft >= 20 &&
                overlap.Width >= 12 &&
                overlap.Height >= 12;
        });
        var plushGroupWithinWorkArea =
            floating.ActualHeight <= workArea.Height &&
            floating.ActualWidth <= workArea.Width &&
            floating.Left >= workArea.Left &&
            floating.Top >= workArea.Top;
        var floatingHeightConstrained = floating.MaxHeight <= workArea.Height;
        var mainThemeExercise = RunMainThemeExercise(theme, main, session.UtcNow);
        var floatingThemeExercise = floating.RunThemeBoundaryExercise();
        var tagPopupFollowsDayNightTheme = ValidateTagPopupThemeTransition(theme, selector, session.UtcNow);
        var warmYellowPaletteMatchesLegacy = ValidateWarmYellowPalette(theme, session.UtcNow);
        var primaryButtonHoverTextContrast = ValidatePrimaryButtonHoverTextContrast(theme, session.UtcNow);
        var toolTipThemeTextContrast = ValidateToolTipThemeTextContrast(theme, session.UtcNow);
        var tagChipTextContrast = ValidateTagChipTextContrast(theme, main, session.UtcNow);

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
        var confirmationDialog = new ConfirmationDialog(main, "验证确认", "确认窗口主题验证");
        var confirmationThemed =
            confirmationDialog.WindowStyle == WindowStyle.None &&
            !confirmationDialog.ShowInTaskbar &&
            Equals(confirmationDialog.Background, Application.Current.FindResource("BackgroundBrush")) &&
            confirmationDialog.Content is Border confirmationBorder &&
            Equals(confirmationBorder.BorderBrush, Application.Current.FindResource("PeachLineBrush"));

        var compactSelection =
            Required<DatePicker>(main, "CountdownDate").Width is >= 240 and <= 280 &&
            Required<ComboBox>(main, "CountdownHour").Width is >= 120 and <= 140 &&
            Required<ComboBox>(main, "CountdownMinute").Width is >= 120 and <= 140 &&
            Required<ComboBox>(main, "CountdownSecond").Width is >= 120 and <= 140 &&
            Required<ComboBox>(main, "DisplayPrecision").ActualWidth is >= 96 and <= 156;
        var compactPreferences =
            Required<ComboBox>(main, "DisplayPrecision").ActualWidth >= 96 &&
            Required<TextBox>(main, "MonthlySalary").ActualWidth >= 112 &&
            Required<TextBox>(main, "WorkStart").ActualWidth >= 96 &&
            Required<TextBox>(main, "WorkEnd").ActualWidth >= 96;

        var report = new ValidationScenarioReport();
        report.AddRange(ArchitectureValidationScenario.Create());
        report.AddRange(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Valid"] = true,
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
            ["FloatingThemeBoundaryModes"] = floatingThemeExercise.BoundaryModes,
            ["FloatingThemePanelColors"] = floatingThemeExercise.PanelColors,
            ["FloatingThemeUsesChinaStandardTime"] = floatingThemeExercise.UsesChinaStandardTime,
            ["FloatingPlushDocksFollowDayNightTheme"] = floatingThemeExercise.PlushDocksFollowTheme,
            ["FloatingThemeTextContrast"] = floatingThemeExercise.TextContrast,
            ["TagPopupFollowsDayNightTheme"] = tagPopupFollowsDayNightTheme,
            ["WarmYellowPaletteMatchesLegacy"] = warmYellowPaletteMatchesLegacy,
            ["PrimaryButtonHoverTextContrast"] = primaryButtonHoverTextContrast,
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
            ["ReminderWindowSupportsKeyboardDismissal"] = reminderWindow.SupportsKeyboardDismissal,
            ["OfficialCharacterBackgroundLoaded"] =
                Required<Image>(main, "MyMelodyBackground").Source is not null &&
                Required<Image>(main, "CinnamorollBackground").Source is not null &&
                Required<Image>(main, "KuromiBackground").Source is not null,
            ["OfficialCharacterBackgroundCharactersExclusive"] = true,
            ["OfficialCharacterBackgroundLowContrast"] = main.CharacterBackgroundRemainsSubtle,
            ["MainCharacterPanelDockCount"] = mainCharacterDocks.Length,
            ["MainCharacterPanelDocksIntegrateWithPanel"] = mainCharacterDocksIntegrateWithPanel,
            ["MainCharacterPanelDecorationsAreNonInteractive"] = main.CharacterPanelDecorationsAreNonInteractive,
            ["LegacyMigrationSupported"] = legacyMigration,
            ["DuplicateSettingsRejected"] = duplicateRejected,
            ["InvalidNotesRejected"] = invalidNotesRejected,
            ["InvalidTagReferencesRejected"] = invalidTagReferencesRejected,
            ["HasTagControls"] =
                main.FindName("CountdownTags") is TagSelector &&
                main.FindName("NoteTags") is TagSelector &&
                main.FindName("FilterTagOptions") is WrapPanel,
            ["StaticWorkspaceLayout"] =
                main.FindName("CountdownWorkspace") is Grid &&
                main.FindName("DisplayPrecision") is ComboBox,
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
            ["CompactFilterSummary"] = Required<Button>(main, "FilterButton").ActualWidth <= 100,
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
            ["NoteStatusLabelsPresent"] = noteStatusLabels || session.Current.Notes.Count == 0,
            ["NoteDatesRendered"] = noteDatesRendered,
            ["NotesGroupedByDay"] = notesGroupedByDay,
            ["ActiveNotesGrouped"] = activeNotesGrouped,
            ["ActiveNotesExpandedByDefault"] =
                activeNotesToggle is not null && activeNoteRows.Visibility == Visibility.Visible,
            ["ActiveNotesKeepDateOrder"] = activeNotesKeepDateOrder,
            ["CompletedNotesCollapsed"] = main.CompletedNotesAreCollapsed,
            ["FloatingWindowPresent"] = floating is not null,
            ["FloatingWindowStaysAboveWorkAreaBottom"] = floatingBottomMargin >= 0,
            ["FloatingWindowBottomAnchored"] = floating!.IsBottomAnchored,
            ["FloatingWindowReanchorsAfterContentChanges"] = floating.IsBottomAnchored,
            ["FloatingWindowHeightConstrainedToWorkArea"] = floatingHeightConstrained,
            ["FloatingPlushTaskWrapLoaded"] = floating.PlushCount == 3,
            ["FloatingPlushUsesThreePanelDocks"] = plushDocksIntegrateWithPanel,
            ["FloatingPlushCount"] = floating.PlushCount,
            ["FloatingPlushDecorationsAreNonInteractive"] = floating.PlushDecorationsAreNonInteractive,
            ["FloatingPlushDocksIntegrateWithPanel"] = plushDocksIntegrateWithPanel,
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
            ["LoadedCountdowns"] = session.Current.Countdowns.Count,
            ["LoadedNotes"] = session.Current.Notes.Count,
            ["LoadedTags"] = session.Current.Tags.Count,
            ["SettingsWereMigrated"] = session.SettingsWereMigrated,
            ["LoadedDisplayPrecision"] = session.Current.Preferences.DisplayPrecision,
            ["LoadedMonthlySalary"] = session.Current.Preferences.MonthlySalary,
            ["LoadedWorkStart"] = CountdownFormatting.FormatWorkTime(session.Current.Preferences.WorkStart),
            ["LoadedWorkEnd"] = CountdownFormatting.FormatWorkTime(session.Current.Preferences.WorkEnd),
            ["ActiveNoteCount"] = session.Current.Notes.Count(note => !note.IsCompleted),
            ["CompletedNoteCount"] = session.Current.Notes.Count(note => note.IsCompleted),
            ["SortedNames"] = session.GetSortedCountdowns(applyFilter: false).Select(record => record.Name).ToArray(),
            ["WindowIconLoaded"] = main.Icon is not null
        });

        datePicker.IsDropDownOpen = false;
        reminderWindow.Close();
        floating.Hide();
        main.Hide();
        return report.ToDictionary();
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
            var before = ApplicationSession.Load(settingsPath, beforeStore);
            var workspace = new DesktopWorkspace(before);
            if (!workspace.TrySaveNote(
                    null,
                    "一次性提醒生命周期探针",
                    new DateTime(2031, 4, 6),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc),
                    out _))
            {
                return false;
            }

            var early = before.Reminders.GetDue(before.Current, firstDueUtc.AddSeconds(-1));
            var afterStore = new SettingsStore(
                clock: new ValidationClock(firstDueUtc.AddMinutes(5)));
            var after = ApplicationSession.Load(settingsPath, afterStore);
            var overdue = after.Reminders.GetDue(after.Current, firstDueUtc.AddMinutes(5));
            var dueNote = overdue.SingleOrDefault();
            if (dueNote?.Reminder is not OnceReminder once)
            {
                return false;
            }

            after.Reminders.ConfirmDisplayed(dueNote, firstDueUtc.AddMinutes(5));
            var duplicateInProcess = after.Reminders.GetDue(after.Current, firstDueUtc.AddMinutes(5));
            if (!after.TryMarkOnceReminderDelivered(dueNote.Id, once.FirstDueUtc, out _))
            {
                return false;
            }

            var reloaded = ApplicationSession.Load(settingsPath, afterStore);
            var duplicateAfterRestart = reloaded.Reminders.GetDue(
                reloaded.Current,
                firstDueUtc.AddMinutes(5));
            return
                early.Count == 0 &&
                overdue.Count == 1 &&
                duplicateInProcess.Count == 0 &&
                duplicateAfterRestart.Count == 0 &&
                reloaded.Current.Notes.Single().Reminder is OnceReminder
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
        var seen = new HashSet<string?>(StringComparer.Ordinal);
        string? current = null;
        foreach (var note in notes)
        {
            if (string.Equals(current, note.Date, StringComparison.Ordinal))
            {
                continue;
            }

            current = note.Date;
            if (!seen.Add(current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasApplicationStyle(Type key) =>
        Application.Current.Resources[key] is Style;

    private static bool HasTemplate(Control control)
    {
        control.ApplyTemplate();
        return control.Template is not null;
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
        var reminderLabel = Required<TextBlock>(main, "NoteReminderLabel");
        var characterPanel = Required<Border>(main, "CharacterPanelSurface");

        foreach (var checkpoint in checkpoints)
        {
            var mode = theme.Apply(checkpoint.ToUniversalTime());
            main.ApplyTheme(mode);
            main.UpdateTime(checkpoint);
            PumpDispatcher();
            main.UpdateLayout();
            modes.Add(mode.ToString());
            reminderLabelFollowsTheme &=
                ReferenceEquals(reminderLabel.Foreground, Application.Current.FindResource("InkBrush"));
            segmentSelectedTextContrast &= MainSegmentButtonsHaveAccessibleSelectedStates(main);
            characterPanelDocksFollowTheme &= main.CharacterPanelDocksFollowActiveTheme;
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
        }

        main.ApplyTheme(theme.Apply(restoreUtc));
        PumpDispatcher();
        main.UpdateLayout();
        var expectedModes = new[] { "Night", "Day", "Day", "Night" };
        return new MainThemeExerciseResult(
            modes,
            new[] { dayBackgroundColor, nightBackgroundColor },
            new[] { dayCharacterOpacity, nightCharacterOpacity },
            new[] { dayCharacterPanelColor, nightCharacterPanelColor },
            modes.SequenceEqual(expectedModes, StringComparer.Ordinal),
            reminderLabelFollowsTheme,
            segmentSelectedTextContrast,
            characterPanelDocksFollowTheme);
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
                ("SegmentSelectedForegroundColor", "#725326"),
                ("FloatingDayPanelBrush", "#FFFDFC"),
                ("FloatingDayHeaderBrush", "#FFF6D9"),
                ("FloatingDayNoteInputBrush", "#FFF6D9"),
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
                ("TagColor", "#4B3522"),
                ("TagChipTextColor", "#4A2D24"),
                ("SegmentSelectedForegroundColor", "#FFF0CC"),
                ("FloatingNightPanelBrush", "#3A2B1E"),
                ("FloatingNightHeaderBrush", "#4A3524"),
                ("FloatingNightNoteInputBrush", "#F7E1B3"),
                ("FloatingNightCinnamorollDockBrush", "#55452A")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            return dayMatches && nightMatches && defaultTagColorsMatch;
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
                if (toolTip.Background is not Brush background ||
                    toolTip.Foreground is not Brush foreground ||
                    !ApplicationTheme.MeetsContrast(background, foreground, 4.5))
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
        var segmentSelectedForeground =
            (Color)Application.Current.FindResource("SegmentSelectedForegroundColor");
        return
            ContrastRatio(canvas, ink) >= 7 &&
            ContrastRatio(canvas, body) >= 4.5 &&
            ContrastRatio(canvas, muted) >= 4.5 &&
            ContrastRatio(tag, segmentSelectedForeground) >= 4.5;
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

    private sealed record MainThemeExerciseResult(
        IReadOnlyList<string> BoundaryModes,
        IReadOnlyList<string> BackgroundColors,
        IReadOnlyList<int> CharacterOpacities,
        IReadOnlyList<string> CharacterPanelColors,
        bool UsesChinaStandardTime,
        bool ReminderLabelFollowsTheme,
        bool SegmentSelectedTextContrast,
        bool CharacterPanelDocksFollowTheme);

    private sealed class ValidationClock : IClock
    {
        public ValidationClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
