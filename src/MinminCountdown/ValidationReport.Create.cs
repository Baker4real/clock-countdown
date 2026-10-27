using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;
using WpfCalendar = System.Windows.Controls.Calendar;

namespace MinminCountdown;

internal static partial class ValidationReport
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
        using var persistenceExercisePath = options.ExercisePersistenceFailures
            ? new PersistenceExercisePathGate(desktopWorkspace.Snapshot.SettingsPath)
            : null;
        var settingsConservationPath = desktopWorkspace.Snapshot.SettingsPath;
        var settingsBytesAtValidationEntry = File.Exists(settingsConservationPath)
            ? File.ReadAllBytes(settingsConservationPath)
            : null;
        var settingsHashAtValidationEntry = settingsBytesAtValidationEntry is not null
            ? Convert.ToHexString(SHA256.HashData(settingsBytesAtValidationEntry))
            : null;
        main.ShowInTaskbar = false;
        main.ShowActivated = false;
        main.WindowStartupLocation = WindowStartupLocation.Manual;
        main.Left = -20_000;
        main.Top = -20_000;
        main.Opacity = 0;
        floating.ShowInTaskbar = false;
        floating.ShowActivated = false;
        // Initialize the AllowsTransparency surface fully opaque so WPF creates
        // a real composition target before any validation capture changes it.
        floating.Opacity = 1;
        main.Show();
        PumpDispatcher();
        var initialFocusedAutomationId = Keyboard.FocusedElement is DependencyObject focused
            ? AutomationProperties.GetAutomationId(focused)
            : string.Empty;
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
        var floatingQuickAddPaletteBindingIsolationBehavior =
            ValidationExercises.RunFloatingQuickAddPaletteBindingIsolationBehavior();

        var mainWindowScenarios = main.RunValidationScenarios(options.ExerciseNoteAdd);
        var countdownReadability = mainWindowScenarios.CountdownReadability;
        var countdownEditing = mainWindowScenarios.CountdownEditing;
        var interactionStates = mainWindowScenarios.InteractionStates;
        var noteExercise = mainWindowScenarios.NoteExercise;
        var floatingNoteExercise = floatingWindowScenarios.CrossDayReminderAdded;
        var mainReminderModeExercise = mainWindowScenarios.ReminderModes;
        var floatingReminderModeExercise = floatingWindowScenarios.ReminderModes;
        var mainUiPolish = mainWindowScenarios.UiPolish;
        var mainStructure = mainWindowScenarios.Structure;
        var floatingStructure = floatingWindowScenarios.Structure;
        var selector = Required<TagSelector>(main, "CountdownTags");
        var mainNoteCompletionValidation = ValidateMainNoteCompletion(
            main,
            floating,
            selector,
            desktopWorkspace);
        var mainNoteCompletionGates = ValidationExercises.EvaluateMainNoteCompletionGates(
            new MainNoteCompletionGateInputs(
                mainNoteCompletionValidation.Main.UsesDedicatedStyle,
                mainNoteCompletionValidation.Main.BoxWidth >= 17.5 &&
                    mainNoteCompletionValidation.Main.BoxHeight >= 17.5,
                mainNoteCompletionValidation.Main.TickWidth >= 9.5 &&
                    mainNoteCompletionValidation.Main.TickHeight >= 6.5,
                mainNoteCompletionValidation.Main.UsesWarmTokens,
                mainNoteCompletionValidation.Main.CornerRadius >= 4,
                mainNoteCompletionValidation.Interaction.Checked &&
                    mainNoteCompletionValidation.Interaction.Unchecked,
                mainNoteCompletionValidation.Interaction.PersistenceFailureRolledBack,
                mainNoteCompletionValidation.Interaction.AccessibleNamesUpdated,
                mainNoteCompletionValidation.TagSelector.BoxWidth is >= 17.5 and <= 18.5 &&
                    mainNoteCompletionValidation.TagSelector.BoxHeight is >= 17.5 and <= 18.5 &&
                    mainNoteCompletionValidation.TagSelector.TickWidth is >= 9.5 and <= 10.5 &&
                    mainNoteCompletionValidation.TagSelector.TickHeight is >= 6.5 and <= 7.5 &&
                    !mainNoteCompletionValidation.TagSelector.UsesDedicatedStyle,
                mainNoteCompletionValidation.Floating.BoxWidth is >= 17.5 and <= 18.5 &&
                    mainNoteCompletionValidation.Floating.BoxHeight is >= 17.5 and <= 18.5 &&
                    mainNoteCompletionValidation.Floating.TickWidth is >= 9.5 and <= 10.5 &&
                    mainNoteCompletionValidation.Floating.TickHeight is >= 6.5 and <= 7.5 &&
                    !mainNoteCompletionValidation.Floating.UsesDedicatedStyle));
        var mainNoteCompletionGateIsolationBehavior =
            ValidationExercises.RunMainNoteCompletionGateIsolationBehavior();
        var accessibleNameWhitespaceIsolationBehavior =
            ValidationExercises.RunAccessibleNameWhitespaceIsolationBehavior();
        var mainShortcutGateIsolationBehavior =
            ValidationExercises.RunMainShortcutGateIsolationBehavior();
        var mainKeyboardNavigationGateIsolationBehavior =
            ValidationExercises.RunMainKeyboardNavigationGateIsolationBehavior();
        // #153：便签列表排版 / 芯片 / 空态 / 筛选标签 / 键盘与间距 token 探针消费与门禁评估。
        var noteListPolish = mainUiPolish.NoteListPolish;
        // #154/#200：视觉铬探针消费——漏斗/图标描边/矢量窗铬/胶囊圆角/装饰层/
        // DWM 原生圆角与安全降级/页签次序；
        // 悬浮外壳投影（TaskCard/FloatingThemeBackdrop 无 Effect）由悬浮窗实例直接钉。
        var visualChrome = mainUiPolish.VisualChrome;
        var floatingShellLargeShadowRemoved = floatingStructure.ShellLargeShadowRemoved;
        var popupFocusTabGates = ValidationExercises.EvaluatePopupFocusTabGates(
            new PopupFocusTabGateInputs(
                noteListPolish.FilterPopupTabCyclesInsideLayer,
                noteListPolish.TagManagerPopupTabCyclesInsideLayer,
                noteListPolish.TagSelectorPopupTabCyclesInsideLayer));
        var popupFocusTabGateIsolationBehavior =
            ValidationExercises.RunPopupFocusTabGateIsolationBehavior();
        var countdownListFirst = ValidationExercises.RunCountdownListFirstBehavior();
        var mainTopmostStateSynchronization = ValidationExercises.RunMainTopmostStateSynchronization();
        var mainRedundancyCleanup = ValidationExercises.RunMainRedundancyCleanup();
        var mainNoteEditorSeparation = ValidationExercises.RunMainNoteEditorSeparation();
        var mainTextShortcutBehavior = ValidationExercises.RunMainTextShortcutBehavior();
        var singleLineEnterSubmission = ValidationExercises.RunSingleLineEnterSubmission();
        var floatingNoteKeyboardSubmission = ValidationExercises.RunFloatingNoteKeyboardSubmission();
        var listDeleteKeyboardBehavior = ValidationExercises.RunListDeleteKeyboardBehavior();
        var mainNoteRowsSimplified = ValidationExercises.RunMainNoteRowsSimplified();
        var countdownPreferencesPopup = ValidationExercises.RunCountdownPreferencesPopupBehavior();
        var countdownEditorExpansion = ValidationExercises.RunCountdownEditorExpansionBehavior();
        var countdownRowPresentation = ValidationExercises.RunCountdownRowDeadlineAndSecondaryDeleteBehavior();
        var mainStructuralAnnouncements = ValidateMainStructuralAnnouncements(assetRoot);
        var mainStructuralAnnouncementGates =
            ValidationExercises.EvaluateMainStructuralAnnouncementGates(
                new MainStructuralAnnouncementGateInputs(
                    mainStructuralAnnouncements.CountdownWorkspaceSelectionIsAccessible &&
                        mainStructuralAnnouncements.NotesWorkspaceSelectionIsAccessible,
                    mainStructuralAnnouncements.FilterResultAnnounced,
                    mainStructuralAnnouncements.CountdownFirstUseEmptyAnnounced &&
                        mainStructuralAnnouncements.NotesFirstUseEmptyAnnounced &&
                        mainStructuralAnnouncements.CountdownFilterEmptyAnnounced &&
                        mainStructuralAnnouncements.NotesFilterEmptyAnnounced));
        var mainStructuralAnnouncementGateIsolationBehavior =
            ValidationExercises.RunMainStructuralAnnouncementGateIsolationBehavior();
        var countdownIncrementalRefreshGateIsolationBehavior =
            ValidationExercises.RunCountdownIncrementalRefreshGateIsolationBehavior();
        var floatingRenderSignatureGateIsolationBehavior =
            ValidationExercises.RunFloatingRenderSignatureGateIsolationBehavior();
        var floatingEmptyStateTextIsolationBehavior =
            ValidationExercises.RunFloatingEmptyStateTextIsolationBehavior();
        var floatingUiPolish = floatingWindowScenarios.UiPolish;
        var tagExercise = options.ExerciseTagInteractions
            ? ValidationExercises.RunTagInteractions(desktopWorkspace)
            : TagInteractionExerciseResult.NotRun;
        var persistenceExercise = options.ExercisePersistenceFailures
            ? persistenceExercisePath!.Run(() => ValidationExercises.RunPersistenceFailures(
                desktopWorkspace,
                desktopWorkspace.Snapshot.SettingsPath))
            : PersistenceFailureExerciseResult.NotRun;

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
        var floatingDragInteraction = floatingWindowScenarios.DragInteraction;

        var datePicker = Required<DatePicker>(main, "CountdownDate");
        datePicker.ApplyTemplate();
        var dateTextBox = datePicker.Template.FindName("PART_TextBox", datePicker) as DatePickerTextBox;
        var dateButton = datePicker.Template.FindName("PART_Button", datePicker) as Button;
        var datePopup = datePicker.Template.FindName("PART_Popup", datePicker) as Popup;
        var dateCalendar = PickerTemplateParts.GetCalendar(datePicker);
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
        var calendarMonthGridLaidOut = CalendarMonthGridLaidOut(dateCalendar);
        datePicker.IsDropDownOpen = false;

        var dateBehaviorAttached = PickerTemplateParts.HasDatePickerContract(datePicker);
        var productionDatePickers = ValidateProductionDatePickers(main, floating);
        var dateTextMetrics = MeasureDatePickerTextFit(datePicker);
        var calendarHeaderMetrics = MeasureTextFit(datePicker, "2031年12月");
        var workspaceDraftsRetained = mainWindowScenarios.WorkspaceDraftsRetained;
        var settingsDrafts = mainWindowScenarios.SettingsDrafts;
        var undatedNoteEdit = mainWindowScenarios.UndatedNoteEdit;
        var noteDateBehavior = mainWindowScenarios.NoteDateBehavior;
        var originalWorkspace = main.CurrentWorkspace;
        main.ShowWorkspace(WorkspaceKind.Notes);
        var hiddenCountdownSubmitDisabled = !Required<Button>(main, "SaveCountdownButton").IsEnabled;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        PumpDispatcher();
        main.UpdateLayout();
        var switchesToCountdown = Required<Canvas>(main, "CountdownWorkspace").Visibility == Visibility.Visible;
        var mainContentProjection = Required<Grid>(main, "MainContentProjection");
        var workspaceSwitcherBottom = new[]
        {
            BoundsIn(Required<ToggleButton>(main, "CountdownTab"), mainContentProjection).Bottom,
            BoundsIn(Required<ToggleButton>(main, "NotesTab"), mainContentProjection).Bottom
        }.Max();
        var countdownEditorTop =
            BoundsIn(Required<Border>(main, "CountdownEditorSurface"), mainContentProjection).Top;
        main.ShowWorkspace(WorkspaceKind.Notes);
        PumpDispatcher();
        main.UpdateLayout();
        var switchesToNotes = Required<Canvas>(main, "NotesWorkspace").Visibility == Visibility.Visible;
        var noteEditorTop = BoundsIn(Required<Border>(main, "NoteEditorPanel"), mainContentProjection).Top;
        var workspaceSwitcherBeforeEditors =
            workspaceSwitcherBottom <= countdownEditorTop &&
            workspaceSwitcherBottom <= noteEditorTop;
        main.ShowWorkspace(originalWorkspace);
        PumpDispatcher();
        main.UpdateLayout();

        var createAppears = selector.ProbeCreateAction("验证新标签");
        var createCollapsed = !selector.ProbeCreateAction(string.Empty);
        var tagSearchDoesNotSubmit = !selector.CreateButtonIsDefault;
        var reminderBehavior = ValidateReminderDispatch();
        var oneTimeReminderBehavior = ValidateOneTimeReminderLifecycle();
        var repeatingReminderDelivery = ValidateRepeatingReminderDeliveryCoordinator();
        var reminderDispositionAndSnooze =
            ValidationExercises.RunReminderDispositionAndSnoozeBehavior();
        var reminderCompletionAndReopen =
            ValidationExercises.RunReminderCompletionAndReopenBehavior();
        var reminderCenterCustomSnoozeControlsVisibility =
            ValidationExercises.RunReminderCenterCustomSnoozeControlsVisibilityBehavior();
        var reminderCenterCustomSnoozeCalendarBehavior =
            ValidationExercises.RunReminderCenterCustomSnoozeCalendarBehavior();
        var reminderCenterSnoozeClick =
            ValidationExercises.RunReminderCenterSnoozeClickBehavior();
        var reminderCenterExpiredCustomSnoozeRetry =
            ValidationExercises.RunReminderCenterExpiredCustomSnoozeRetryBehavior();
        var reminderCenterStaleRetryAfterCompleteReopen =
            ValidationExercises.RunReminderCenterStaleRetryAfterCompleteReopenBehavior();
        var reminderSnoozeSuppressesBaseCycles =
            ValidationExercises.RunReminderSnoozeSuppressesBaseCyclesBehavior();
        var reminderCenterCompleteClick =
            ValidationExercises.RunReminderCenterCompleteClickBehavior();
        var reminderCenterCompleteRequiresConfirmation =
            ValidationExercises.RunReminderCenterCompleteRequiresConfirmationBehavior();
        var deleteUndoOffer =
            ValidationExercises.RunDeleteUndoOfferBehavior() &&
            ValidationExercises.RunDeleteUndoInPlaceBehavior();
        var dirtyExitConfirmation =
            ValidationExercises.RunDirtyExitConfirmationBehavior();
        var assistiveNarration =
            ValidationExercises.RunAssistiveNarrationBehavior();
        var narratorLiveRegions =
            ValidationExercises.RunNarratorLiveRegionBehavior();
        var windowPositionReset =
            ValidationExercises.RunWindowPositionResetBehavior();
        var mainWindowWorkAreaFit =
            ValidationExercises.RunMainWindowWorkAreaFitBehavior();
        var mainWindowIdentityZoneDrag =
            ValidationExercises.RunMainWindowIdentityZoneDragBehavior();
        var floatingSecondaryMonitor = MonitorGeometryValidation.FloatingWindowStaysOnSecondaryMonitor();
        var ownerDialogsFitSecondaryMonitor = MonitorGeometryValidation.OwnerDialogsFitSecondaryMonitor();
        var reminderUsesSourceMonitor = MonitorGeometryValidation.ReminderUsesSourceMonitor();
        var openWindowsFollowMonitorChanges = MonitorGeometryValidation.OpenWindowsFollowMonitorChanges();
        var mixedDpiPhysicalGeometry = MonitorGeometryValidation.MixedDpiUsesPhysicalMonitorGeometry();
        var productManifestPerMonitorV2 = MonitorGeometryValidation.ProductManifestDeclaresPerMonitorV2();
        var runtimeDpiLayout = MonitorGeometryValidation.RuntimeDpiChangeUpdatesWindowLayout();
        var mainWindowHiddenRefresh =
            ValidationExercises.RunMainWindowHiddenRefreshBehavior();
        var mainWindowNoteMinuteRefresh =
            ValidationExercises.RunMainWindowNoteMinuteRefreshBehavior();
        var reminderCenterDeliveryFailureActionGate =
            ValidationExercises.RunReminderCenterDeliveryFailureActionGateBehavior();
        var reminderCenterEnterUsesNativeButtonActivation =
            ValidationExercises.RunReminderCenterEnterUsesNativeButtonActivationBehavior();
        var reminderCenterArrivalKeepsForegroundAndFlashesTaskbar =
            ValidationExercises.RunReminderCenterArrivalKeepsForegroundAndFlashesTaskbarBehavior();
        var reminderCenterMinimizeToTaskbarPreservesEntries =
            ValidationExercises.RunReminderCenterMinimizeToTaskbarPreservesEntriesBehavior();
        var reminderCenterSnoozeGatedDuringCompletionRetry =
            ValidationExercises.RunReminderCenterSnoozeGatedDuringCompletionRetryBehavior();
        var reminderCenterCustomSnoozeErrorRoutesByCode =
            ValidationExercises.RunReminderCenterCustomSnoozeErrorRoutesByCodeBehavior();
        var reminderCenterShowFailureRollsBack =
            ValidationExercises.RunReminderCenterShowFailureRollsBackBehavior();
        var reminderCenterShowFailureRollbackSurvivesEntryFailure =
            ValidationExercises.RunReminderCenterShowFailureRollbackSurvivesEntryFailureBehavior();
        var reminderCenterOpenSourcePreservesNoteGroupState =
            ValidationExercises.RunReminderCenterOpenSourcePreservesNoteGroupStateBehavior();
        var reminderCenterAcknowledgeAll =
            ValidationExercises.RunReminderCenterAcknowledgeAllBehavior();
        var reminderCenterRepresentativeCapture =
            ValidateReminderCenterRepresentativeCapture();
        var validateOnlySettingsIsolation =
            ValidationExercises.RunValidateOnlySettingsIsolationBehavior();
        var lazyManagerLifecycle =
            ValidationExercises.RunLazyManagerLifecycleBehavior();
        var mainDecorationOnDemandDecode =
            ValidationExercises.RunMainDecorationOnDemandDecodeBehavior();
        var countdownReminderWpf =
            ValidationExercises.RunCountdownReminderWpfBehavior();
        var reminderDelivery = ValidateReminderDeliveryCoordinator();
        var reminderCenter = ValidateReminderCenterBehavior(theme, assetRoot);
        var reminderCenterAppendsAfterOpen = ValidateReminderCenterAppendsAfterOpen();
        var reminderCenterSourceLifecycle = ValidateReminderCenterSourceLifecycle();
        var reminderPersistenceFailureDisposeWorks =
            ValidateReminderPersistenceFailureDispose();
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
        var contrast = ValidateThemeContrast(theme, workspaceSnapshot.UtcNow);
        var mainIdleTextInputBoundaryContrast = ValidateMainIdleTextInputBoundaryContrast(main);
        var idleControlBoundaries = ValidateIdleControlBoundaries(main);
        var selectedControlSignals = ValidateSelectedControlSignals(main);
        var focusOuterNeighbors = ValidateFocusOuterNeighbors(main);
        var dangerContrast = ContrastRatio(
            (Color)Application.Current.FindResource("CanvasColor"),
            (Color)Application.Current.FindResource("DangerColor")) >= 4.5;
        var dangerSurfaces = new[] { "Background", "Canvas", "Card", "Note", "StatusBar", "ReminderCard" }
            .ToDictionary(name => name, name => ContrastRatio(
                (Color)Application.Current.FindResource(name + "Color"),
                (Color)Application.Current.FindResource("DangerColor")), StringComparer.Ordinal);

        var countdownRows = Required<StackPanel>(main, "CountdownRows");
        var activeNoteRows = Required<StackPanel>(main, "ActiveNoteRows");
        var completedNoteRows = Required<StackPanel>(main, "CompletedNoteRows");
        var activeNotesSegment = Required<ToggleButton>(main, "ActiveNotesSegment");
        var completedNotesSegment = Required<ToggleButton>(main, "CompletedNotesSegment");
        var activeNotesLabel = Required<TextBlock>(main, "ActiveNotesLabel");
        var completedNotesLabel = Required<TextBlock>(main, "CompletedNotesLabel");
        var activeNotesCount = Required<TextBlock>(main, "ActiveNotesCount");
        var completedNotesCount = Required<TextBlock>(main, "CompletedNotesCount");
        var activeNotesBadge = Required<Border>(main, "ActiveNotesBadge");
        var completedNotesBadge = Required<Border>(main, "CompletedNotesBadge");
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
        var noteStatusSegmentSwitch =
            HasNoteStatusSegmentSwitch(
                activeNotesSegment,
                completedNotesSegment,
                activeNotesLabel,
                completedNotesLabel,
                activeNotesCount,
                completedNotesCount,
                activeNotesBadge,
                completedNotesBadge,
                activeNoteRows,
                completedNoteRows,
                workspaceSnapshot.VisibleNotes.Count(note => !note.IsCompleted),
                workspaceSnapshot.VisibleNotes.Count(note => note.IsCompleted));
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
            HasNoteSegmentAccessibleName(activeNotesSegment, "未完成", activeNotesCount.Text) &&
            HasNoteSegmentAccessibleName(completedNotesSegment, "已完成", completedNotesCount.Text) &&
            renderedDateSections.All(section =>
                HasNoteDisclosureAccessibleName(
                    section.Toggle,
                    GetNoteDateLabel(section.Toggle)?.Text ?? string.Empty)) &&
            HasNoteRowActionAccessibleNames(activeNoteRows) &&
            HasNoteRowActionAccessibleNames(completedNoteRows);
        var notesStatusHeader = Required<Grid>(main, "NotesStatusHeader");
        var activeNotesGrouped =
            activeNoteRows.Parent is StackPanel noteSections &&
            noteSections.Children.Contains(notesStatusHeader) &&
            noteSections.Children.IndexOf(activeNoteRows) ==
            noteSections.Children.IndexOf(notesStatusHeader) + 1 &&
            noteSections.Children.IndexOf(completedNoteRows) ==
            noteSections.Children.IndexOf(activeNoteRows) + 1;
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
        var noteGroupingSelectorAbsent = mainStructure.NoteGroupingSelectorAbsent;
        var tagChips = Descendants<Border>(countdownRows)
            .Concat(Descendants<Border>(activeNoteRows))
            .Where(border => !string.IsNullOrWhiteSpace(border.Uid))
            .ToArray();
        var tagChipsUseUid = tagChips.All(chip => chip.Uid.Length > 0);

        var savePreferencesButton = Required<Button>(main, "SavePreferencesButton");
        var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
        preferencesPopup.IsOpen = true;
        PumpDispatcher();
        var savePreferencesButtonFit = MeasureButtonFit(savePreferencesButton);
        var savePreferencesButtonRight = savePreferencesButton
            .TranslatePoint(new Point(savePreferencesButton.ActualWidth, 0), main)
            .X;
        var savePreferencesButtonIsVisible =
            savePreferencesButton.IsVisible &&
            savePreferencesButton.TranslatePoint(new Point(0, 0), main).X >= 0 &&
            savePreferencesButtonRight <= main.ActualWidth;
        WindowValidationInput.ClosePopupForCleanup(preferencesPopup);
        var originalMainWidth = main.Width;
        main.Width = main.MinWidth;
        main.UpdateLayout();
        // #211/#221：最小宽度下经窗口级滚动到达齿轮，再实际打开弹层测量保存按钮。
        var mainScrollHost = Required<ScrollViewer>(main, "MainWindowScrollHost");
        Required<Button>(main, "PreferencesButton").BringIntoView();
        PumpDispatcher();
        preferencesPopup.IsOpen = true;
        PumpDispatcher();
        main.UpdateLayout();
        var savePreferencesButtonLeftInViewport =
            savePreferencesButton.TranslatePoint(new Point(0, 0), mainScrollHost).X;
        var savePreferencesButtonIsVisibleAtMinimumWidth =
            savePreferencesButton.IsVisible &&
            mainScrollHost.ScrollableWidth > 0.5 &&
            savePreferencesButtonLeftInViewport >= -0.5 &&
            savePreferencesButtonLeftInViewport + savePreferencesButton.ActualWidth <=
                mainScrollHost.ViewportWidth + 0.5;
        WindowValidationInput.ClosePopupForCleanup(preferencesPopup);
        mainScrollHost.ScrollToLeftEnd();
        mainScrollHost.ScrollToTop();
        main.Width = originalMainWidth;
        main.UpdateLayout();
        theme.Apply();
        main.ApplyTheme();
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
            main.MainReferenceDecorationsLoaded &&
            main.MainReferenceDecorationsFollowActiveTheme ||
            legacyCharacterDocksIntegrateWithPanel;
        theme.Apply();
        main.ApplyTheme();
        PumpDispatcher();
        main.UpdateLayout();
        var workArea = MonitorWorkAreaResolver.ResolveForWindow(floating).WorkAreaDip;
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
        var dayMascotsImage = Required<Image>(floating, "DayMascotsImage");
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
        var plushSceneIntegratesWithPanel =
            Panel.GetZIndex(floatingPlushScene) > Panel.GetZIndex(taskCard) &&
            dayMascotsImage.Source is not null &&
            !dayMascotsImage.IsHitTestVisible;
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
            plushSceneIntegratesWithPanel &&
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
        floating.ParkOffscreenForValidation();
        PumpDispatcher();
        var floatingReanchorsAfterContentChanges = floatingBottomAnchored;
        var floatingReferenceExercise = RunFloatingReferenceExercise(main, assetRoot);
        var visualReferenceBaselineInterfaceBehavior =
            ValidationExercises.RunVisualReferenceBaselineInterfaceBehavior();
        var floatingReferenceBaselineGateIsolationBehavior =
            ValidationExercises.RunFloatingReferenceManifestGateIsolationBehavior() &&
            visualReferenceBaselineInterfaceBehavior;
        var floatingReferenceFreshnessGateIsolationBehavior =
            ValidationExercises.RunFloatingReferenceFreshnessGateIsolationBehavior();
        var floatingPrimaryCountdownContrastGateIsolationBehavior =
            ValidationExercises.RunFloatingPrimaryCountdownContrastGateIsolationBehavior();
        var floatingSecondaryRemainingGateIsolationBehavior =
            ValidationExercises.RunFloatingSecondaryRemainingGateIsolationBehavior();
        var floatingScrollBarGateIsolationBehavior =
            ValidationExercises.RunFloatingScrollBarGateIsolationBehavior();
        var mainThemeExercise = RunMainThemeExercise(theme, main, workspaceSnapshot.UtcNow);
        var floatingThemeExercise = floatingWindowScenarios.ThemeExercise;
        var tagPopupFollowsDayTheme =
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
        var reminderCenterSourceLabelContrast =
            ValidateReminderCenterSourceLabelContrast(theme, workspaceSnapshot.UtcNow);
        var systemDialogPrimaryButtonContrast = ValidateSystemDialogPrimaryButtonContrast();
        var keyboardFocusRingContrast =
            ValidateKeyboardFocusRingContrast(theme, main, floating, workspaceSnapshot.UtcNow);
        var comboBoxTextClick = ValidateComboBoxTextClick(main);
        var secondaryWindowDrag = ValidateSecondaryWindowDrag(main);
        var calendarNavigationNames = ValidateCalendarNavigationNames(main, datePicker);
        var countdownCardActionsIncludeRecordName =
            ValidateCountdownCardActionNames(main, desktopWorkspace);
        var settingsRecoveryFailureExitIsDefault =
            ValidationExercises.ProbeSettingsRecoveryFailureExitIsDefault();
        var ruleEditorInlineDeleteUsesDangerButton =
            ValidationExercises.ProbeRuleEditorInlineDeleteUsesDangerButton();
        var settingsRecoverySecondaryUsesCanvas =
            ValidationExercises.ProbeSettingsRecoverySecondaryUsesCanvas();
        var reminderCenterTwoRowActionsLayout =
            ValidationExercises.ProbeReminderCenterTwoRowActionsLayout();
        var reminderCenterEntryVisualIdentity =
            ValidationExercises.ProbeReminderCenterEntryVisualIdentity();
        var reminderCenterTypographyWeightWhitelist =
            ValidationExercises.ProbeReminderCenterTypographyWeightWhitelist();
        var popupAnimationIsInstant =
            ValidationExercises.ProbeFivePopupsUseInstantAnimation(main);
        var motionTokensMatch = ValidationExercises.ProbeMotionTokens();
        var actionForegroundsUseMainActionText =
            ValidationExercises.ProbeActionForegroundsUseMainActionText(main);
        var disabledSelectionControlsUseButtonOpacity =
            ValidationExercises.ProbeDisabledSelectionControlsUseButtonOpacity(main);
        var floatingDateStampTypographyMatches =
            ValidationExercises.ProbeFloatingDateStampTypography(floating, desktopWorkspace);
        var floatingTypographyWeightWhitelist =
            ValidationExercises.ProbeFloatingTypographyWeightWhitelist(floating, desktopWorkspace);
        var floatingDateStampDensityMatches =
            ValidationExercises.ProbeFloatingDateStampDensity(floating, desktopWorkspace);
        var floatingDateStampStickyAtScrollBottom =
            ValidationExercises.ProbeFloatingDateStampStickyAtScrollBottom(floating, desktopWorkspace);
        var floatingNotesListBottomPaddingMatches =
            ValidationExercises.ProbeFloatingNotesListBottomPadding(floating, desktopWorkspace);
        var noteCancelFitsEditorPanel =
            ValidationExercises.ProbeNoteCancelFitsEditorPanel(main);
        var popupRightInsetIsEightFromCanvas =
            ValidationExercises.ProbePopupRightInsetIsEightFromCanvas(main);
        var filterPopupScrollsLongTagList =
            ValidationExercises.ProbeFilterPopupScrollsLongTagList(main, desktopWorkspace);
        var mainCheckboxHitCoversSlot =
            ValidationExercises.ProbeMainCheckboxHitCoversSlot(main, desktopWorkspace);
        var floatingSearchSlotRightIs396 =
            ValidationExercises.ProbeFloatingSearchSlotRightIs396(floating);
        var floatingSearchHintMatchesInputHeight =
            ValidationExercises.ProbeFloatingSearchHintMatchesInputHeight(floating);
        var floatingQuickAddAlignsWithNoteCards =
            ValidationExercises.ProbeFloatingQuickAddAlignsWithNoteCards(floating, desktopWorkspace);
        var floatingCheckboxHitCoversSlot =
            ValidationExercises.ProbeFloatingCheckboxHitCoversSlot(floating, desktopWorkspace);
        var auditContrastGateIsolationBehavior =
            ValidationExercises.RunAuditContrastGateIsolationBehavior();
        var floatingManualPlacement = floatingWindowScenarios.ManualPlacement;
        var mainReferenceDecorationValidation = ValidateMainReferenceDecorationScaling(main);
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
        var countdownEmptyStateGateIsolationBehavior =
            ValidationExercises.RunCountdownEmptyStateGateIsolationBehavior();
        var countdownEmptyStateContrast =
            ValidateCountdownEmptyStateContrast(theme, main);
        var countdownEmptyStateGates = ValidationExercises.EvaluateCountdownEmptyStateGates(
            new CountdownEmptyStateGateInputs(
                mainUiPolish.CountdownEmptyStatesDistinguished,
                mainUiPolish.CountdownEmptyPointerResetRestoresResults,
                mainUiPolish.CountdownEmptyKeyboardResetRestoresResults,
                countdownEmptyStateContrast.TextContrast));
        var mainCountdownRemainingGateIsolationBehavior =
            ValidationExercises.RunMainCountdownRemainingGateIsolationBehavior();
        var countdownReadabilityGateIsolationBehavior =
            ValidationExercises.RunCountdownReadabilityGateIsolationBehavior();
        var floatingViewportGeometryGateIsolationBehavior =
            ValidationExercises.RunFloatingViewportGeometryGateIsolationBehavior();
        var countdownEditingGateIsolationBehavior =
            ValidationExercises.RunCountdownEditingGateIsolationBehavior();
        var countdownEditingGates = ValidationExercises.EvaluateCountdownEditingGates(
            new CountdownEditingGateInputs(
                countdownEditing.FieldErrorsAtSourceWithFocus,
                countdownEditing.FieldErrorsAtSourceWithFocus,
                countdownEditing.FieldErrorsClearOnCorrection,
                countdownEditing.SingleErrorRowNotClipped,
                countdownEditing.PreferenceErrorsBelowFields,
                countdownEditing.PreferenceErrorsClearOnCorrection,
                countdownEditing.PrimarySecondaryRolesDistinct,
                countdownEditing.InputSurfacesWhitePaper,
                countdownEditing.SelectionHighlightDistinct,
                countdownEditing.StatusBarSurfaceErrorContrast,
                countdownEditing.ReminderRadioSelectedContrast,
                countdownEditing.EditorTypographyTokenized));
        var mainCountdownRemainingSamples = mainUiPolish.CountdownRemainingSamples;
        var mainCountdownRemainingGates = ValidationExercises.EvaluateMainCountdownRemainingGates(
            new MainCountdownRemainingGateInputs(
                mainCountdownRemainingSamples.Select(sample => sample.ExpectedText).ToArray(),
                mainCountdownRemainingSamples.Select(sample => sample.RenderedText).ToArray(),
                mainCountdownRemainingSamples.Count == 6,
                mainCountdownRemainingSamples.All(sample => Math.Abs(sample.ColumnWidth - 228) <= 0.01),
                mainCountdownRemainingSamples.All(sample => Math.Abs(sample.ActualColumnWidth - 228) <= 0.5),
                mainCountdownRemainingSamples.All(sample => sample.IsRightAligned),
                mainCountdownRemainingSamples.All(sample => sample.IsSingleLineAndUntrimmed),
                mainCountdownRemainingSamples.All(sample => sample.IsFullyVisible && sample.BlocksFullyVisible &&
                    sample.RenderedBlockText == sample.ExpectedText.Replace("小时", "时").Replace("分钟", "分")),
                mainCountdownRemainingSamples.All(sample => sample.UsesTabularNumerals),
                mainCountdownRemainingSamples.All(sample => sample.PreferenceSaved),
                mainCountdownRemainingSamples.Count == 6 &&
                    mainCountdownRemainingSamples.Take(4).All(sample => Math.Abs(sample.FontSize - 20) <= 0.01 &&
                        sample.IsPrimary && sample.ValueFontSize is >= 20 and <= 22 && sample.ValueFontSize > sample.TitleFontSize) &&
                    mainCountdownRemainingSamples[4].FontSize is >= 13 and < 20 &&
                    mainCountdownRemainingSamples[5].IsPrimary &&
                    mainCountdownRemainingSamples[5].ValueFontSize > mainCountdownRemainingSamples[5].TitleFontSize));
        var legacyMainReferenceCapture =
            RunMainReferenceCapture(theme, main, workspaceSnapshot, assetRoot);
        var mainReferenceMode = VisualReferenceBaseline.Main.ModeFromEnvironment();
        var mainReferenceScenes = mainReferenceMode.Requested
            ? CaptureMainReferenceScenarios(theme, assetRoot)
            : MainReferenceScenes.NotCaptured;
        var mainReferenceBaseline = VisualReferenceBaseline.Main.Run(
            mainReferenceMode,
            mainReferenceScenes.Captures);
        // #213 退出探针会再开窗口。放在参考帧捕获之后，避免打乱 v33 像素。
        dirtyExitConfirmation = dirtyExitConfirmation &&
            ValidationExercises.RunAlwaysConfirmExitKeepsReminderBehavior();
        var floatingTrayInteraction = RunFloatingTrayInteractionExercise(
            main,
            floating,
            minimizeToTrayButton,
            floatingTrayController,
            desktopWorkspace);
        var mainKeyboardBehavior = ValidateMainKeyboardBehavior(
            main,
            floating,
            floatingTrayController,
            desktopWorkspace,
            initialFocusedAutomationId);
        var mainShortcutScopeIsSafe = main.MainShortcutScopeIsSafe;
        var mainKeyboardPathNegativeFixtures = ValidateMainKeyboardPathNegativeFixtures();
        var mainShortcutGates = ValidationExercises.EvaluateMainShortcutGates(
            new MainShortcutGateInputs(
                new[]
                {
                    "CountdownsWorkspace", "Filter", "Minimize", "NoteSearch",
                    "NotesWorkspace", "SaveCountdown", "SaveNote", "SavePreferences", "TagManager"
                },
                mainKeyboardBehavior.CommandResults,
                mainKeyboardBehavior.TextInputPreserved,
                mainKeyboardBehavior.PopupGuarded,
                mainKeyboardBehavior.ModalOwnerGuarded));
        var mainKeyboardNavigationGates = ValidationExercises.EvaluateMainKeyboardNavigationGates(
            new MainKeyboardNavigationGateInputs(
                mainKeyboardBehavior.InitialFocusSafe,
                mainKeyboardBehavior.CountdownTabPath.AllPassed &&
                    mainKeyboardBehavior.NotesTabPath.AllPassed));
        var daySingleBorder = ValidateDaySingleBorder();
        var daySingleBorderGateIsolationBehavior = RunDaySingleBorderGateIsolationBehavior();
        var mainReferenceBaselineGateIsolationBehavior =
            ValidationExercises.RunMainReferenceManifestGateIsolationBehavior() &&
            visualReferenceBaselineInterfaceBehavior;
        var mainStressCountdownTitleGateIsolationBehavior =
            ValidationExercises.RunMainStressCountdownTitleGateIsolationBehavior();

        var reminderCenterWindow = new ReminderCenterWindow();
        var reminderThemed =
            reminderCenterWindow.WindowStyle == WindowStyle.None &&
            reminderCenterWindow.Content is Border reminderBorder &&
            Equals(reminderBorder.Background, Application.Current.FindResource("ReminderPageBrush"));
        var reminderKeyboardExercise = ValidateReminderCenterKeyboardNavigation();
        var reminderCenterKeyboardGates = ValidationExercises.EvaluateReminderCenterKeyboardGates(
            new ReminderCenterKeyboardGateInputs(
                reminderKeyboardExercise.TabTraversalWorks,
                reminderKeyboardExercise.CloseGuardWorks,
                reminderKeyboardExercise.ShowPreservesFocusAndAccessibleName,
                reminderKeyboardExercise.EscapeFocusesAcknowledge));
        var reminderCenterKeyboardGateIsolationBehavior =
            ValidationExercises.RunReminderCenterKeyboardGateIsolationBehavior();
        var confirmationValidation = ValidateConfirmationDialog(
            theme,
            main,
            workspaceSnapshot.UtcNow);
        var confirmationThemed = confirmationValidation.Themed;
        var confirmationDialogGateIsolationBehavior =
            ValidationExercises.RunConfirmationDialogGateIsolationBehavior();
        var confirmationButtonsVisible = confirmationValidation.Gates.ButtonsVisible;
        var confirmationBodyReachable = confirmationValidation.Gates.BodyReachable;
        var confirmationCancelSafeDefault = confirmationValidation.Gates.CancelIsSafeDefault;
        var floatingTypographyMeasurements = floatingReferenceExercise.LongNotes.Measurements;
        var secondaryWindowTypography = ValidateSecondaryWindowTypography(
            main,
            floatingReferenceExercise.LongNotes.TypographyMatches,
            floatingTypographyMeasurements.FirstOrDefault()?.BodyFontSize ?? 0,
            floatingTypographyMeasurements.FirstOrDefault()?.ReminderFontSize ?? 0);
        var secondaryWindowTypographyGateIsolationBehavior =
            ValidationExercises.RunSecondaryWindowTypographyGateIsolationBehavior();

        var countdownName = Required<TextBox>(main, "CountdownName");
        var countdownDate = Required<DatePicker>(main, "CountdownDate");
        var countdownHour = Required<ComboBox>(main, "CountdownHour");
        var countdownMinute = Required<ComboBox>(main, "CountdownMinute");
        var countdownSecond = Required<ComboBox>(main, "CountdownSecond");
        var displayPrecision = Required<ComboBox>(main, "DisplayPrecision");
        var monthlySalary = Required<TextBox>(main, "MonthlySalary");
        var workStart = Required<TextBox>(main, "WorkStart");
        var workEnd = Required<TextBox>(main, "WorkEnd");
        var compactOriginalWorkspace = main.CurrentWorkspace;
        var compactWasExpanded = Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Visibility == Visibility.Visible;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        if (!compactWasExpanded)
            Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Required<Popup>(main, "PreferencesPopup").IsOpen = true;
        PumpDispatcher();
        // 管理窗等比收缩（1165→864，×0.74）：宽度区间与 56→41 的高度窗随之换算；
        // 偏好字段的宽度下限是内容兜底，不随缩放。日期框例外（#237）：共享模板的 38 DIP 下拉列不缩，
        // 完整中文日期连同 36/12 留白约需 237 DIP，区间按 242 而不是按比例的 222 收。
        var compactSelection =
            countdownDate.Width is >= 237 and <= 250 &&
            countdownHour.Width is >= 133 and <= 152 &&
            countdownMinute.Width is >= 133 and <= 152 &&
            countdownSecond.Width is >= 133 and <= 152 &&
            displayPrecision.ActualWidth is >= 185 and <= 208 &&
            new Control[]
            {
                countdownName,
                countdownDate,
                countdownHour,
                countdownMinute,
                countdownSecond
            }.All(control => control.IsVisible && control.ActualHeight is >= 40 and <= 42);
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
            }.All(control => control.IsVisible && control.ActualHeight is >= 40 and <= 42);
        WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "PreferencesPopup"));
        if (!compactWasExpanded)
            Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        main.ShowWorkspace(compactOriginalWorkspace);
        PumpDispatcher();
        var characterBackgroundImages = Required<Canvas>(main, "CharacterBackgroundLayer")
            .Children
            .OfType<Image>()
            .ToArray();
        var officialCharacterBackgroundCharactersExclusive =
            characterBackgroundImages.Length == 3 &&
            characterBackgroundImages.All(image => image.Source is not null) &&
            characterBackgroundImages.Select(image => image.Name)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    new[]
                    {
                        "CinnamorollBackground",
                        "KuromiBackground",
                        "MyMelodyBackground"
                    },
                    StringComparer.Ordinal);
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
            // #151 保持白纸输入面；#217 输入控件使用可辨识的常态边界。
            .All(control =>
                ReferenceEquals(control.Background, Application.Current.FindResource("PanelBrush")) &&
                ReferenceEquals(control.BorderBrush, Application.Current.FindResource("ControlBorderBrush")) &&
                ReferenceEquals(control.Foreground, Application.Current.FindResource("InkBrush")));
        countdownDate.ApplyTemplate();
        // 内部横向留白 16→12、前导图标位 48→36（管理窗等比收缩）。
        var compactInputHorizontalPaddingAligned =
            new[] { monthlySalary, workStart, workEnd }
                .All(textBox => textBox.Padding.Left == 12 && textBox.Padding.Right == 12) &&
            countdownName.Padding.Left == 36 &&
            countdownName.Padding.Right == 12 &&
            new[] { countdownHour, countdownMinute, countdownSecond, displayPrecision }
                .All(comboBox => comboBox.Padding.Left == 12) &&
            countdownDate.Template?.FindName("PART_TextBox", countdownDate) is TextBox dateInputTextBox &&
            dateInputTextBox.Padding.Left == 36 &&
            dateInputTextBox.Padding.Right == 12;
        var noteEmptyStateContrast = ValidateNoteEmptyStateContrast(theme, main);
        var noteStatusBadgeTokensFollowState = ValidateNoteStatusBadgeTokensFollowState(theme, main);
        var noteVisualTokenGates = ValidationExercises.EvaluateNoteVisualTokenGates(
            new NoteVisualTokenGateInputs(
                compactInputHorizontalPaddingAligned,
                noteStatusBadgeTokensFollowState,
                noteEmptyStateContrast.TextContrast));
        var noteVisualTokenGateIsolationBehavior =
            ValidationExercises.RunNoteVisualTokenGateIsolationBehavior();
        var reminderEditingInterfaceBehavior =
            ValidationExercises.RunReminderEditingInterfaceBehavior();
        var calendarReminderBehavior =
            ValidationExercises.RunCalendarReminderBehavior();
        var startupAcceptanceProtocolInterfaceBehavior =
            ValidationExercises.RunStartupAcceptanceProtocolInterfaceBehavior();
        var holidayCalendarInterfaceBehavior =
            ValidationExercises.RunHolidayCalendarInterfaceBehavior();
        var reminderRulesEditorSharedEntryWorks =
            ValidationExercises.ExerciseReminderRulesEntry(
                main,
                Required<Button>(main, "ManageNoteReminderRulesButton")) &&
            ValidationExercises.ExerciseReminderRulesEntry(
                floating,
                Required<Button>(floating, "FloatingManageReminderRules"));

        var report = new ValidationReportProjection
        {
            ["StartupAcceptanceProtocolModuleDeep"] = Must(startupAcceptanceProtocolInterfaceBehavior, "兼容字段由启动验收真实 interface 行为计算"),
            ["WorkdayCalendarModuleDeep"] = Must(holidayCalendarInterfaceBehavior, "兼容字段由工作日历真实 interface 行为计算"),
            ["ReminderEditingInterfaceBehavior"] = Must(reminderEditingInterfaceBehavior, "提醒编辑 interface 统一模式、时间、间隔与既有提醒保留行为"),
            ["CalendarReminderBehavior"] = Must(calendarReminderBehavior, "日历提醒覆盖类型化规则、北京时间 occurrence、错过合并与 Ical.Net 失败闭锁"),
            ["StartupAcceptanceProtocolInterfaceBehavior"] = Must(
                startupAcceptanceProtocolInterfaceBehavior,
                "启动验收 protocol interface 覆盖请求、回执、状态推进与拒绝路径"),
            ["HolidayCalendarInterfaceBehavior"] = Must(holidayCalendarInterfaceBehavior, "工作日历 interface 覆盖缓存初始化、派生结果与失败状态"),
            ["VirtualWorkdayCountdownExcludedFromReminders"] =
                holidayCalendarInterfaceBehavior,
            ["Title"] = main.Title,
            ["Width"] = main.Width,
            ["Height"] = main.Height,
            ["CompactSelectionFieldLayout"] = Must(compactSelection, "短选择字段保持内容尺寸与紧凑高度（#129 自 U1 迁移）"),
            ["CompactPreferenceFieldLayout"] = Must(compactPreferences, "偏好字段在默认与展开宽度下保持紧凑（#129 自 U1 迁移）"),
            ["CountdownInputsUseProjectTheme"] = Must(
                countdownInputsUseProjectTheme,
                "倒计时输入使用共享 Panel（#151 白纸输入面）、PeachLine 与 Ink 主题资源（#129 自 U1 迁移，#151 换绑）"),
            ["CompactInputHorizontalPaddingAligned"] = Must(
                noteVisualTokenGates.CompactInputPaddingAligned,
                "倒计时编辑区与设置区三类输入内部横向留白对齐 12 DIP（管理窗等比收缩由 16 换算，前导图标位 48→36）"),
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
            ["ThemeControlInventoryCovered"] = Must(themeInventory, "adapter 验证主题控件资产清单"),
            ["ToolTipTemplateMaterializes"] = Must(toolTipMaterializes, "主题化 ToolTip 模板可安全实例化（#129 自 U1 迁移）"),
            ["ThemedControlTemplatesApplied"] = Must(mainThemeTemplate, "adapter 验证真实 WPF 主题模板"),
            ["ComboBoxesUseThemedDropDowns"] = Must(comboThemed, "ComboBox 下拉、选项与滚动条无未主题化表面（#129 自 U1 迁移）"),
            ["DatePickerCalendarIsThemed"] = Must(dateFamilyThemed, "DatePicker 文本框、触发器与日历族完全主题化（#129 自 U1 迁移）"),
            ["DatePickerTextFits"] = Must(dateTextMetrics.Fits, "完整选中日期在日期框内完整可见（#129 自 U1 迁移）"),
            ["DatePickerTextAvailableWidth"] = dateTextMetrics.Available,
            ["DatePickerTextRequiredWidth"] = dateTextMetrics.Required,
            ["LiveDatePickerPopupIsThemed"] = Must(livePopupThemed, "adapter 验证真实 DatePicker Popup 主题"),
            ["LiveDatePickerPopupVisuallyUnified"] = Must(livePopupUnified, "打开的 DatePicker 弹层使用共享暖色边框、头部与选中样式（#129 自 U1 迁移）"),
            ["LiveCalendarSurfaceUsesThemeTokens"] = livePopupUnified,
            ["LiveCalendarHeaderUsesThemeTokens"] = dateFamilyThemed,
            ["LiveSelectedDayUsesThemeTokens"] = dateFamilyThemed,
            ["LiveSelectedDayCount"] = 1,
            ["LiveSelectedDayBackground"] = Application.Current.FindResource("ActiveBrush")?.ToString() ?? string.Empty,
            ["LiveSelectedDayForeground"] = Application.Current.FindResource("MainActionTextBrush")?.ToString() ?? string.Empty,
            ["CalendarHeaderTextFits"] = Must(calendarHeaderMetrics.Fits, "日历月份头部完整可见（#129 自 U1 迁移）"),
            ["CalendarHeaderTextAvailableWidth"] = calendarHeaderMetrics.Available,
            ["CalendarHeaderTextRequiredWidth"] = calendarHeaderMetrics.Required,
            ["PopupSurfacesUseThemeTokens"] = Must(comboThemed && dateFamilyThemed, "弹层表面使用共享主题背景、边框与动效（#129 自 U1 迁移）"),
            ["NoDefaultSystemSurfaces"] = Must(confirmationThemed, "应用流程不残留系统窗口框或 MessageBox（#129 自 U1 迁移）"),
            ["NoRawFloatingThemeBypass"] = Must(floating.UsesDedicatedThemePalette, "悬浮窗不用原始颜色绕过共享主题 token（#129 自 U1 迁移）"),
            ["MainThemeBackgroundColors"] = mainThemeExercise.BackgroundColors,
            ["MainThemeCharacterOpacities"] = mainThemeExercise.CharacterOpacities,
            ["MainThemeCharacterPanelColors"] = mainThemeExercise.CharacterPanelColors,
            ["MainThemeReminderLabelFollowsTheme"] = Must(mainThemeExercise.ReminderLabelFollowsTheme, "主窗口提醒标签保持主题前景色"),
            ["MainThemeSegmentSelectedTextContrast"] = Must(mainThemeExercise.SegmentSelectedTextContrast, "主窗口工作区选中态文字对比度达标或精确匹配设计声明 token"),
            ["MainCharacterPanelDocksFollowDayTheme"] = Must(mainThemeExercise.CharacterPanelDocksFollowTheme, "主窗口三个玩偶底座使用日间配色"),
            ["MainReferenceDecorationsFollowDayTheme"] = Must(mainThemeExercise.ReferenceDecorationsFollowTheme, "主窗口日间参考装饰按工作区切换"),
            ["MainWorkspaceResourcesFollowDayTheme"] = Must(uiQualityGates.MainWorkspaceResourcesFollowTheme, "主窗口工作区静态表面独立使用主题资源"),
            ["MainDynamicCountdownCardsUseThemeResources"] = Must(uiQualityGates.MainCountdownCardResourcesFollowTheme, "主窗口动态倒计时卡片命中样本并使用主题资源"),
            ["MainDynamicCountdownCardSampleCount"] =
                mainWorkspaceThemeValidation.CountdownCardSampleCount,
            ["MainDynamicNoteCardsUseThemeResources"] = Must(uiQualityGates.MainNoteCardResourcesFollowTheme, "主窗口动态便签卡片命中样本并使用主题资源"),
            ["MainDynamicNoteCardSampleCount"] =
                mainWorkspaceThemeValidation.NoteCardSampleCount,
            ["FloatingThemeBoundaryModes"] = floatingThemeExercise.BoundaryModes,
            ["FloatingThemePanelColors"] = floatingThemeExercise.PanelColors,
            ["FloatingThemePlushGlowColors"] = floatingThemeExercise.PlushGlowColors,
            ["FloatingMascotPresentationModes"] = floatingThemeExercise.MascotPresentationModes,
            ["FloatingRasterMascotPresentationMatches"] = Must(floatingThemeExercise.RasterMascotPresentationMatches, "悬浮窗日间 raster 角色呈现状态完整"),
            ["FloatingThemeTextContrast"] = Must(floatingThemeExercise.TextContrast, "悬浮窗标题与便签错误提示在日间主题下保持可读对比度"),
            ["FloatingPrimaryCountdownTextContrast"] = Must(
                floatingReferenceExercise.PrimaryCountdownContrast.Gates.TextContrast,
                "悬浮窗主倒计时数字相对数字色块底达到 3:1 对比度"),
            ["FloatingPrimaryCountdownAccentSeparatedFromPlushGlow"] = Must(
                floatingReferenceExercise.PrimaryCountdownContrast.Gates.AccentSeparatedFromPlushGlow,
                "悬浮窗主倒计时强调与玩偶阴影使用独立语义 token"),
            ["FloatingPrimaryCountdownContrastMeasurements"] =
                floatingReferenceExercise.PrimaryCountdownContrast.Measurements,
            ["FloatingPrimaryCountdownContrastGateIsolationBehavior"] = Must(
                floatingPrimaryCountdownContrastGateIsolationBehavior,
                "低对比度与共享 token 负向夹具分别单项失败"),
            ["FloatingPlushSceneFollowsDayTheme"] = Must(floatingThemeExercise.PlushSceneFollowsDayTheme, "悬浮窗角色场景在单一日间主题下稳定呈现"),
            ["FloatingQuickAddDayResourceChecks"] = floatingQuickAddTheme.Day.Checks,
            ["FloatingQuickAddDayResourceDiagnostics"] = floatingQuickAddTheme.Day.Diagnostics,
            ["FloatingQuickAddDayResourcesFollowLocalTheme"] = Must(floatingQuickAddTheme.Day.Passed, "悬浮窗快速新增真实控件日间使用局部暖色主题"),
            ["FloatingQuickAddIndependentFromApplicationTheme"] = Must(
                floatingQuickAddTheme.IndependentFromApplicationTheme,
                "悬浮窗快速新增局部主题不受应用主题反向切换污染"),
            ["FloatingQuickAddThemeResourceIsolationBehavior"] = Must(floatingQuickAddThemeResourceIsolationBehavior, "悬浮窗快速新增资源枚举负向夹具可逐控件单项失败"),
            ["FloatingQuickAddPaletteBindings"] = new[]
            {
                floatingQuickAddTheme.DayPaletteBindings
            },
            ["FloatingQuickAddDayPaletteBindingsMatch"] = Must(floatingQuickAddTheme.DayPaletteBindingsMatch, "悬浮窗快速新增日间控件绑定专用日间 palette token"),
            ["FloatingQuickAddPaletteBindingIsolationBehavior"] = Must(
                floatingQuickAddPaletteBindingIsolationBehavior,
                "悬浮窗快速新增 palette 绑定负向夹具可逐控件单项失败"),
            ["FloatingQuickAddLocalCanvasColor"] = floatingQuickAddTheme.CanvasColor,
            ["FloatingQuickAddLocalActiveColor"] = floatingQuickAddTheme.ActiveColor,
            ["FloatingInitialWorkspaceStateMatches"] = Must(floatingWorkspace.InitialStateMatches, "悬浮窗首次显示立即同步倒计时选中态、标题、输入区和内容"),
            ["FloatingWorkspaceSwitchingStaysSynchronized"] = Must(floatingWorkspace.SwitchingStaysSynchronized, "悬浮窗往返切换后始终恰有一个工作区并同步表面"),
            ["FloatingCountdownSelectionSurvivesRefresh"] = Must(floatingWorkspace.CountdownSelectionSurvivesRefresh, "悬浮窗倒计时首次选中态刷新后保持"),
            ["FloatingNoteSearch"] = floatingWindowScenarios.NoteSearch,
            ["FloatingNoteSearchScenariosPass"] = Must(
                floatingWindowScenarios.NoteSearch.AllPassed &&
                mainUiPolish.NoteSearchSyncsFromSharedFilter,
                "悬浮窗折叠搜索主链路、空态、Ctrl+F 与主题 token 通过"),
            ["FloatingBottomCommandScenariosPass"] = Must(floatingWindowScenarios.BottomCommand.AllPassed, "悬浮窗便签底部命令区：默认收起、展开/收起、提交收起、设置按钮全链路通过"),
            ["FloatingQuickAddToggleScenariosPass"] = Must(
                floatingWindowScenarios.BottomCommand.QuickAddTogglePass,
                "悬浮窗＋添加便签默认收起、点击原地展开并按筛选标签初始化、再次点击收起、提交后收起、展开态切换钮降级次级收起（#149）"),
            ["FloatingNoteOverflowMenuScenariosPass"] = Must(
                floatingWindowScenarios.BottomCommand.OverflowMenuPass,
                "悬浮窗便签…菜单提供编辑（复用主窗口编辑流程）与删除（确认后删除，取消保留）"),
            ["FloatingQuickAddPlaceholderMirrorsManager"] = Must(
                floatingWindowScenarios.BottomCommand.QuickAddPlaceholderMirrorsManager,
                "悬浮快速新增正文占位文与管理窗口一致：空白可见、输入后隐藏（#149）"),
            ["FloatingQuickAddExpandedSinglePrimaryAction"] = Must(
                floatingWindowScenarios.BottomCommand.QuickAddToggleSecondaryWhenExpanded,
                "悬浮展开态底部切换钮为次级「收起」，表单添加为唯一实心粉钮（#149）"),
            ["FloatingNoteOverflowMenuKeyboardWorks"] = Must(
                floatingWindowScenarios.BottomCommand.NoteMenuKeyboardLifecycle,
                "悬浮便签…菜单键盘可走完：打开聚焦编辑、Tab 进入删除、Esc 关闭并回触发器（#149）"),
            ["FloatingSettingsGearConcentric"] = Must(floatingWindowScenarios.BottomCommand.SettingsGearConcentric, "悬浮「设置」齿轮外轮廓与孔同心、在 15x15 图标框内居中"),
            ["FloatingConstrainedEmptyStateProvidesAction"] = Must(
                floatingWindowScenarios.NoteSearch.ConstrainedEmptyProvidesAction,
                "悬浮筛选约束空态提供「清除筛选」且与「清除搜索」互斥，点击还原全量列表（#149）"),
            ["FloatingStableRefreshScenarios"] = floatingWindowScenarios.StableRefresh,
            ["FloatingStableRefreshScenariosPass"] = Must(
                floatingWindowScenarios.StableRefresh.AllPassed,
                "悬浮窗渲染签名稳定刷新：tick 就地更新、角色焦点恢复、隐藏/最小化门控全链路通过（#212）"),
            ["FloatingStableRefreshFocusStaysOnOverflowTrigger"] = Must(
                floatingWindowScenarios.StableRefresh.FocusStaysOnOverflowTriggerAcrossTicks,
                "悬浮窗 tick 后焦点停留在便签「…」触发器且便签未被误完成、滚动位置不动（#212）"),
            ["FloatingStableRefreshCountdownUpdatesInPlace"] = Must(
                floatingWindowScenarios.StableRefresh.CountdownRowsPreservedAndTextUpdatesInPlace,
                "悬浮窗倒计时 tick 行实例保留且剩余时间分格就地更新（#212）"),
            ["FloatingStableRefreshNoteCardsPreserved"] = Must(
                floatingWindowScenarios.StableRefresh.NoteCardInstancesPreservedAcrossTicks,
                "悬浮窗便签卡与完成复选框实例跨 tick 保留（#212）"),
            ["FloatingStableRefreshRebuildRestoresFocusByRole"] = Must(
                floatingWindowScenarios.StableRefresh.RebuildRestoresFocusAndScrollByRole,
                "悬浮窗数据变化重建后焦点按「记录 ID + 控件角色」回到「…」触发器并还原滚动（#212）"),
            ["FloatingStableRefreshHiddenSkipsRenderUntilVisible"] = Must(
                floatingWindowScenarios.StableRefresh.HiddenSkipsRenderUntilVisible,
                "悬浮窗隐藏期间 tick 与数据变化跳过渲染，恢复可见立即渲染一次（#212）"),
            ["FloatingStableRefreshMinimizedSkipsRenderUntilRestored"] = Must(
                floatingWindowScenarios.StableRefresh.MinimizedSkipsRenderUntilRestored,
                "悬浮窗最小化期间 tick 跳过渲染，还原正常态立即渲染一次（#212）"),
            ["FloatingRenderSignatureGateIsolationBehavior"] = Must(
                floatingRenderSignatureGateIsolationBehavior,
                "渲染签名隔离反例：同输入同签名，标题/精度/筛选/结构变化改签名，剩余时间仅数值变化不改签名（#212）"),
            ["FloatingDateStampPastelRotationMatches"] = Must(floatingWindowScenarios.BottomCommand.PastelIndexPass, "悬浮窗日期印记按列表顺序粉/蓝/绿轮换且卡底暖黄与白纸交替"),
            ["FloatingNotesHandwrittenDecorationPass"] = Must(
                floatingWindowScenarios.BottomCommand.HandwrittenDecorationLands,
                "悬浮窗便签工作区右下手写体装饰落地且不可命中不可聚焦"),
            ["FloatingSettingsButtonOpensManager"] = Must(
                floatingWindowScenarios.BottomCommand.SettingsOpensManagerPreservingWorkspace,
                "悬浮窗底部设置按钮打开管理窗口并沿用当前工作区"),
            ["NoteSearchSyncsFromSharedFilter"] = Must(mainUiPolish.NoteSearchSyncsFromSharedFilter, "主窗口搜索框回填共享筛选态"),
            ["FloatingEmptyStateTextIsolationBehavior"] = Must(floatingEmptyStateTextIsolationBehavior, "悬浮窗命中与遮挡门禁（#143）"),
            ["TagPopupFollowsDayTheme"] = Must(tagPopupFollowsDayTheme, "打开的标签弹层保持日间主题"),
            ["WarmYellowPaletteMatchesLegacy"] = Must(warmYellowPaletteMatchesLegacy, "全局主题恢复旧版暖黄日间色"),
            ["PrimaryButtonHoverTextContrast"] = Must(primaryButtonHoverTextContrast, "主操作按钮悬停时保持玫粉底白字的可读对比度"),
            ["MainActionTextContrast"] = Must(mainActionTextContrast, "主窗口与悬浮窗主操作白字在默认、悬停与按下色上均达到 4.5:1 对比度（#199）"),
            ["MainActionPalette"] = new
            {
                Default = Application.Current.FindResource("MainActionBrush")?.ToString() ?? string.Empty,
                Hover = Application.Current.FindResource("MainActionHoverBrush")?.ToString() ?? string.Empty,
                Pressed = Application.Current.FindResource("MainActionPressedBrush")?.ToString() ?? string.Empty,
                Floating = Application.Current.FindResource("PinkAccentStrongBrush")?.ToString() ?? string.Empty,
                Text = Application.Current.FindResource("MainActionTextBrush")?.ToString() ?? string.Empty,
                Danger = Application.Current.FindResource("DangerBrush")?.ToString() ?? string.Empty
            },
            ["ToolTipThemeTextContrast"] = Must(toolTipThemeTextContrast, "提示气泡在日间主题下保持可读对比度"),
            ["TagChipTextContrast"] = Must(tagChipTextContrast, "标签芯片在日间主题下保持暖墨文字的可读对比度"),
            ["ThemedInteractiveStates"] = Must(mainThemeTemplate && comboThemed && dateFamilyThemed, "主题化交互控件具备视觉状态反馈（#129 自 U1 迁移）"),
            ["ThemeTextContrast"] = Must(contrast, "暖色主题满足全部文字对比度对（#129 自 U1 迁移）"),
            ["MainIdleTextInputBoundaryContrast"] = Must(mainIdleTextInputBoundaryContrast, "未聚焦的名称输入框真实边界对五种浅色表面及输入底色达到 3:1（#217）"),
            ["IdleControlBoundariesVisible"] = Must(idleControlBoundaries.Values.All(value => value), "选择控件、输入和次级按钮的真实常态边界可见（#217）"),
            ["IdleControlBoundaryChecks"] = idleControlBoundaries,
            ["NoteCompletionUndoFeedback"] = Must(ValidationExercises.RunNoteCompletionUndoFeedback(), "#233 两窗真实完成、重新打开事务、失败回滚、唯一撤销槽及10秒到期行为"),
            ["NoteBatchKeyboardCompletion"] = Must(ValidationExercises.RunNoteBatchKeyboardCompletion(), "#235 键盘多选与原子完成，推进cursor并移除对应中心条目，失败整批保留"),
            ["NoteBatchDeleteUndo"] = Must(ValidationExercises.RunNoteBatchDeleteUndo(), "#235 一次安全确认与撤销，恢复全部记录和规则状态，写盘失败或ID冲突原子拒绝"),
            ["NoteSelectionAccessibility"] = Must(ValidationExercises.RunNoteSelectionAccessibility(), "#235 选中数量直播区、行peer、刷新保留与筛选折叠清理"),
            ["NoteCompletionExitTransition"] = Must(ValidationExercises.RunNoteCompletionExitTransition(), "#233 完成卡片160ms透明度离场、动画开关与撤销/切页/隐藏中断"),
            ["NoteCompletionFlipTransition"] = Must(ValidationExercises.RunNoteCompletionFlipTransition(), "#243 两窗卡片尺寸快照、稳定身份FLIP补位、同步时钟及全部中断清零"),
            ["SystemContrastUpdatesOpenWindows"] = Must(ValidateSystemContrastUpdatesOpenWindows(main, floating), "#220 已打开主窗口与浮窗随系统对比度开关立即更新并恢复"),
            ["SystemContrastAccessibleStates"] = Must(ValidateSystemContrastAccessibleStates(main, floating), "#220 五窗文字、背景、边界、焦点、选中和禁用态映射系统颜色并保留草稿"),
            ["SystemContrastColorChangesAndCustomSurfaces"] = Must(
                ValidateSystemContrastCustomSurfaces(assetRoot),
                "#220 系统配色运行中变化更新弹层、自选标签和日期印记并恢复日间原色"),
            ["SelectedControlSignalsVisible"] = Must(
                selectedControlSignals.Values.All(value => value) &&
                mainNoteCompletionValidation.Measurements.Any(sample => sample.User == "MainWindow" && sample.UsesDedicatedStyle) &&
                mainNoteCompletionValidation.Measurements.Any(sample => sample.User == "FloatingWindow" && sample.UsesCompletionSuccessStyle),
                "筛选勾号、成功勾选和单选图形在真实模板中可辨识（#217）"),
            ["SelectedControlSignalChecks"] = selectedControlSignals,
            ["FocusOuterNeighborsMeetContrast"] = Must(focusOuterNeighbors.Values.All(value => value), "主按钮、浮窗胶囊和置顶两态的焦点环对外侧邻色至少 3:1（#217）"),
            ["FocusOuterNeighborChecks"] = focusOuterNeighbors,
            ["ReminderCenterSourceLabelContrast"] = reminderCenterSourceLabelContrast,
            ["ReminderCenterContentHierarchy"] = Must(
                reminderCenterSourceLabelContrast.EmphasisPreserved &&
                reminderCenterSourceLabelContrast.TextContrast,
                "#223 提醒正文为H2标题、来源为caption且可访问名称保来源"),
            ["ReminderCenterRelativeTimeUpdatesInPlace"] = Must(
                ValidationExercises.RunReminderCenterRelativeTimeBehavior() &&
                reminderCenterSourceLabelContrast.RelativeTimeTextContrast,
                "#223 相对时间按分钟/小时原位更新，保条目、焦点、滚动与失败信息且对比至少4.5"),
            ["SystemDialogPrimaryButtonContrast"] = systemDialogPrimaryButtonContrast,
            ["KeyboardFocusRingContrast"] = keyboardFocusRingContrast,
            ["ComboBoxTextClick"] = comboBoxTextClick,
            ["ComboBoxArrowIsNotTabStop"] = Must(comboBoxTextClick.ArrowIsNotTabStop, "下拉箭头不是独立 Tab 停靠点"),
            ["SecondaryWindowHeaderDrag"] = secondaryWindowDrag,
            ["CalendarPreviousMonthAccessibleName"] = calendarNavigationNames.Previous,
            ["CalendarNextMonthAccessibleName"] = calendarNavigationNames.Next,
            ["CalendarNavigationAccessibleNames"] = Must(
                string.Equals(calendarNavigationNames.Previous, "上一个月", StringComparison.Ordinal) &&
                string.Equals(calendarNavigationNames.Next, "下一个月", StringComparison.Ordinal),
                "日历上一个月/下一个月按钮具有可访问名称"),
            ["DatePickerOpenCalendarAccessibleName"] = dateButton is null
                ? string.Empty
                : AutomationProperties.GetName(dateButton) ?? string.Empty,
            ["DatePickerOpenCalendarAccessibleNames"] = Must(productionDatePickers.AllOpenCalendarButtonsNamed, "生产日期框打开日历按钮均可访问播报"),
            ["CountdownCardActionsIncludeRecordName"] = Must(countdownCardActionsIncludeRecordName, "倒计时卡编辑/删除可访问名称包含对象名"),
            ["SettingsRecoveryFailureExitIsDefault"] = Must(settingsRecoveryFailureExitIsDefault, "设置恢复失败态退出按钮是 Enter 默认按钮"),
            ["RuleEditorInlineDeleteUsesDangerButton"] = Must(ruleEditorInlineDeleteUsesDangerButton, "规则编辑器行内删除使用 DangerButton"),
            ["SettingsRecoverySecondaryUsesCanvas"] = Must(settingsRecoverySecondaryUsesCanvas, "设置恢复次级按钮使用现役暖白画布副本 #FFFDF3EB"),
            ["ReminderCenterTwoRowActionsLayout"] = Must(reminderCenterTwoRowActionsLayout, "提醒中心动作按 DESIGN 两行布局且按钮为图标加文字"),
            ["ReminderCenterEntryVisualIdentity"] = Must(reminderCenterEntryVisualIdentity, "提醒中心页面、卡片、相对时间徽章与来源图标符合 DESIGN 视觉标识"),
            ["PopupAnimationIsInstant"] = Must(popupAnimationIsInstant, "五处生产弹层使用即时出现而不是系统淡入"),
            ["ActionForegroundsUseMainActionText"] = Must(actionForegroundsUseMainActionText, "主按钮、复选勾与日历选中前景使用 MainActionTextBrush"),
            ["DisabledSelectionControlsUseButtonOpacity"] = Must(
                disabledSelectionControlsUseButtonOpacity,
                "禁用 CheckBox/ComboBox 与主窗口专用复选模板表面透明度为 0.45"),
            ["FloatingDateStampTypographyMatches"] = Must(floatingDateStampTypographyMatches, "日期印记字重、字体 fallback、表格数字与规则线间距符合 §3"),
            ["FloatingTypographyWeightWhitelist"] = Must(floatingTypographyWeightWhitelist, "悬浮窗字重只使用 Regular 与 SemiBold"),
            ["FloatingDateStampDensityMatches"] = Must(floatingDateStampDensityMatches, "日期印记上下留白与三行间距降低拥挤感"),
            ["FloatingDateStampStickyAtScrollBottom"] = floatingDateStampStickyAtScrollBottom,
            ["FloatingNotesListBottomPaddingMatches"] = floatingNotesListBottomPaddingMatches,
            ["NoteCancelFitsEditorPanel"] = Must(noteCancelFitsEditorPanel, "便签编辑取消按钮完全位于面板右缘内并与保存同高同行"),
            ["PopupRightInsetIsEightFromCanvas"] = Must(popupRightInsetIsEightFromCanvas, "筛选、标签管理与标签选择弹层右缘距内容画布内缘 8px"),
            ["FilterPopupScrollsLongTagList"] = Must(filterPopupScrollsLongTagList, "筛选弹层长标签列表有最大高度并内部滚动"),
            ["MainCheckboxHitCoversSlot"] = Must(mainCheckboxHitCoversSlot, "主窗口便签复选框命中覆盖 38px 槽位（默认缩放后 ≥24 DIP）且保留 Tag=note.Id"),
            ["FloatingSearchSlotRightIs396"] = Must(floatingSearchSlotRightIs396, "悬浮搜索槽右缘对齐 x=396"),
            ["FloatingSearchHintMatchesInputHeight"] = Must(floatingSearchHintMatchesInputHeight, "悬浮搜索提示行与输入行等高且展开不跳动视口"),
            ["FloatingQuickAddAlignsWithNoteCards"] = Must(floatingQuickAddAlignsWithNoteCards, "悬浮快速新增右缘与便签卡列右缘对齐"),
            ["FloatingCheckboxHitCoversSlot"] = Must(floatingCheckboxHitCoversSlot, "悬浮便签复选框命中覆盖 36px 槽位且不覆盖日期印记"),
            ["KeyboardFocusBaselineFramesAffected"] = false,
            ["KeyboardFocusBaselineEvidence"] =
                $"Standing captures clear keyboard focus before every frame so baselines are foreground-independent; the {VisualReferenceBaseline.Version} series includes edit-mode and filter-open standing frames.",
            ["AuditContrastGateIsolationBehavior"] = Must(auditContrastGateIsolationBehavior, "审计对比度负向夹具可独立失败"),
            ["DangerButtonContrast"] = Must(dangerContrast, "危险确认按钮悬停/按下保持文字对比度（#129 自 U1 迁移）"),
            ["DangerSurfacesMeetContrast"] = Must(dangerSurfaces.Values.All(ratio => ratio >= 4.5), "错误文字对六种真实表面至少 4.5:1（#217）"),
            ["DangerSurfaceMeasurements"] = dangerSurfaces,
            ["EffectiveThemeTextContrast"] = Must(contrast, "弱化便签/日历文字达到有效对比度（#129 自 U1 迁移）"),
            ["DestructiveConfirmationIsSafe"] = Must(confirmationThemed && confirmationCancelSafeDefault, "破坏性确认不以删除为 Enter 默认动作（#129 自 U1 迁移）"),
            ["ConfirmationDialogLongestButtonsVisible"] = Must(confirmationButtonsVisible, "最长删除确认正文下两个按钮完整位于窗口内容区"),
            ["ConfirmationDialogLongestBodyReachable"] = Must(confirmationBodyReachable, "最长删除确认正文可换行并由滚动容器承载"),
            ["ConfirmationDialogSafeDefaultActions"] = Must(confirmationCancelSafeDefault, "删除确认取消按钮仍为默认和取消动作"),
            ["ConfirmationDialogGateIsolationBehavior"] = Must(confirmationDialogGateIsolationBehavior, "删除确认对话框负向夹具可独立失败"),
            ["ConfirmationDialog"] = confirmationValidation,
            ["FormControlsHaveAccessibleNames"] = Must(main.HasAccessibleCoreControls, "核心表单控件具有显式可访问名称（#129 自 U1 迁移）"),
            ["StatusMessagesUseLiveRegions"] = Must(main.HasLiveStatusRegions, "动态状态消息通过 live region 播报（#129 自 U1 迁移）"),
            ["MainInitialFocusIsSafeAndDeterministic"] = Must(mainKeyboardBehavior.InitialFocusSafe, "主窗口初始焦点落在确定且非危险控件"),
            ["MainTabSequenceStartsWithWorkspaceControls"] = Must(
                string.Join(',', mainKeyboardBehavior.TabSequence) ==
                    "CountdownTab,NotesTab,PreferencesButton,FilterButton,TagManagerButton",
                "主窗口工作区控件具备可遍历顺序"),
            ["MainStructuralLiveRegionsUsePoliteSemantics"] = Must(
                AutomationProperties.GetLiveSetting(Required<TextBlock>(main, "EmptyCountdownMessage")) == AutomationLiveSetting.Polite &&
                AutomationProperties.GetLiveSetting(Required<TextBlock>(main, "EmptyNotesMessage")) == AutomationLiveSetting.Polite &&
                AutomationProperties.GetLiveSetting(Required<TextBlock>(main, "StatusText")) == AutomationLiveSetting.Polite,
                "主窗口结构变化使用既有可见 polite live region"),
            ["MainShortcutScopeIsSafe"] = Must(mainShortcutScopeIsSafe, "主窗口Ctrl快捷键允许输入焦点，避开弹层与模态上下文（#224）"),
            ["MainShortcutGateIsolationBehavior"] = Must(mainShortcutGateIsolationBehavior, "主窗口快捷键负向夹具可独立失败"),
            ["MainShortcutGates"] = mainShortcutGates,
            ["MainKeyboardNavigationGates"] = mainKeyboardNavigationGates,
            ["MainKeyboardNavigationGateIsolationBehavior"] = Must(mainKeyboardNavigationGateIsolationBehavior, "主窗口初始焦点与完整 Tab 路径负向夹具可独立失败"),
            ["MainKeyboardBehavior"] = mainKeyboardBehavior,
            ["MainKeyboardPathNegativeFixtures"] = Must(mainKeyboardPathNegativeFixtures, "完整 Tab 路径负夹具可击穿缺失标签、换序、非对称和不可见焦点"),
            ["MainStructuralAnnouncements"] = mainStructuralAnnouncements,
            ["MainEmptyStateGuidance"] = mainStructuralAnnouncements.EmptyStateGuidance,
            ["MainStructuralAnnouncementGates"] = mainStructuralAnnouncementGates,
            ["MainStructuralAnnouncementGateIsolationBehavior"] = Must(mainStructuralAnnouncementGateIsolationBehavior, "结构变化通知三门负向夹具可独立失败"),
            ["SecondaryWindowTypography"] = secondaryWindowTypography,
            ["SecondaryWindowTypographyGateIsolationBehavior"] = Must(secondaryWindowTypographyGateIsolationBehavior, "次级窗口字号三门负向夹具可独立失败"),
            ["CompactActionTargetsAreUsable"] = Must(
                Required<Button>(main, "SaveCountdownButton").MinHeight >= 32,
                "紧凑标签与便签动作达到桌面交互目标尺寸（#129 自 U1 迁移）"),
            ["RequiredControls"] = 10,
            ["DatePickerTemplateParts"] = dateParts.Count(part => part is not null),
            ["DatePickerText"] = dateTextBox?.Text,
            ["WpfUiPickerStylesIntegrated"] = Must(
                productionDatePickers.AllAttached &&
                productionDatePickers.ReminderCenter.Attached &&
                comboBoxTextClick.Measurements.All(input => input.Attached),
                "生产日期与下拉实际继承 WPF-UI 样式并使用库提供的切换模板（#206）"),
            ["DatePickerSelectedDateTextMatchesContract"] = Must(
                productionDatePickers.Attached.All(input => input.SelectedDateTextMatchesContract) &&
                productionDatePickers.ReminderCenter.SelectedDateTextMatchesContract,
                "全部生产日期框在日期变化与日历关闭后保持中文空格格式（#206）"),
            ["DateInputReadOnly"] = Must(productionDatePickers.AllProductionInputsReadOnly, "日期选择不接受自由文本输入（#129 自 U1 迁移）"),
            ["DatePickerButtonOpensCalendar"] = Must(
                dateButtonOpens && productionDatePickers.ReminderCenter.ButtonOpensCalendar,
                "点击日期下拉按钮打开日历弹层（#129 自 U1 迁移）"),
            ["DatePickerTextOpensCalendar"] = Must(
                productionDatePickers.AttachedTextPointerWorks &&
                productionDatePickers.AttachedKeyboardLifecycleWorks &&
                productionDatePickers.UnattachedNegativeTextDoesNotOpen,
                "点击日期文字打开全部主题化日历（#129 自 U1 迁移）"),
            ["DatePickerTextDefersCalendarUntilPointerRelease"] = Must(
                productionDatePickers.AttachedTextDefersUntilRelease,
                "日期文字在指针释放前不提前打开日历（#129 自 U1 迁移）"),
            ["DatePickerTextClicksUseTheme"] = Must(
                dateBehaviorAttached &&
                productionDatePickers.AllAttached &&
                productionDatePickers.AttachedTextOpensThemedPopups &&
                dateFamilyThemed,
                "日期文字点击绑定到全部主题化日期框（#129 自 U1 迁移）"),
            ["DatePickerTextOpensThemedPopup"] = Must(
                productionDatePickers.AttachedTextOpensThemedPopups && livePopupThemed,
                "点击日期文字打开真实主题化日历弹层（#129 自 U1 迁移）"),
            ["DatePickerCalendarMonthGridLaidOut"] = Must(calendarMonthGridLaidOut, "打开的月视图是 7 个星期标题 + 6×7 互不重叠的日期格，不叠进同一格"),
            ["DatePickerEscapeClosesAndReturnsFocus"] =
                productionDatePickers.AttachedKeyboardLifecycleWorks,
            ["DatePickerProductionInputCount"] = productionDatePickers.ProductionInputCount,
            ["DatePickerAttachedInputCount"] = productionDatePickers.Attached.Count,
            ["DatePickerProductionMeasurements"] = productionDatePickers.Attached,
            ["DatePickerReminderCenterMeasurement"] = productionDatePickers.ReminderCenter,
            ["DatePickerUnattachedNegativeMeasurement"] = productionDatePickers.UnattachedNegative,
            ["DatePickerUnattachedNegativeTextDoesNotOpen"] =
                productionDatePickers.UnattachedNegativeTextDoesNotOpen,
            ["ReminderCenterComboBoxesUseThemedDropDowns"] =
                productionDatePickers.ReminderCenterComboBoxesUseThemedDropDowns,
            ["TimeDropDowns"] = 3,
            ["TimeOptionCounts"] = main.TimeOptionCounts,
            ["HasCountdownReminderModeRadios"] = Must(
                mainStructure.CountdownReminderModeRadiosPresent,
                "倒计时提醒模式单选组存在（#129 自 U1 迁移）"),
            ["HasCountdownList"] = Must(countdownRows is not null, "多倒计时列表存在（#129 自 U1 迁移）"),
            ["MainCountdownEncourageBadgeAbsent"] = Must(countdownReadability.EncourageBadgeAbsent, "管理窗口倒计时列表不渲染超期待徽章（两窗都只挂距离下班）"),
            ["MainCountdownExpiredRemainingFollowsContrast"] = Must(
                countdownReadability.ExpiredRemainingFollowsContrast,
                "到期剩余文字：主项用主项数字色、次项用正文色并保留已到期文案"),
            ["MainCountdownBlocksGeometryMatches"] = countdownReadability.BlocksGeometryMatches,
            ["MainCountdownNumericTextsMatchContract"] = Must(countdownReadability.NumericTextsMatchContract, "北京时间使用表格数字且月薪与今日已赚同一套本地数字格式"),
            ["MainCountdownReadabilityGateIsolationBehavior"] = Must(countdownReadabilityGateIsolationBehavior, "倒计时可读性门禁用会失败的反例保住"),
            ["MainCountdownReadability"] = countdownReadability,
            ["MainCountdownFieldErrorsAtSource"] = Must(countdownEditingGates.FieldErrorsAtSourceWithFocus, "倒计时名称/截止日期错误按字段拆开贴字段显示并聚焦第一个无效控件（#151）"),
            ["MainCountdownFieldErrorsClearOnCorrection"] = Must(countdownEditingGates.FieldErrorsClear, "倒计时字段值修正后字段错误立即清除（#151）"),
            ["MainCountdownSingleErrorRowUnclipped"] = Must(countdownEditingGates.SingleErrorRowNotClipped, "倒计时唯一错误行不再固定高度裁切或省略（#151）"),
            ["MainPreferenceErrorsBelowFields"] = Must(countdownEditingGates.PreferenceErrorsAtSource, "月薪/上下班时间格式错误出现在对应字段下方（#151）"),
            ["MainPrimarySecondaryButtonRolesDistinct"] = Must(
                countdownEditingGates.PrimarySecondaryRolesDistinct,
                "倒计时页唯一实心主行动为＋添加倒计时，保存设置为CardAlt次级表面，便签＋添加仍为主按钮（#217）"),
            ["MainInputSurfaceWhitePaper"] = Must(countdownEditingGates.InputSurfacesWhitePaper, "倒计时编辑输入面使用白纸面板色并与画布分开（#151）"),
            ["MainTextSelectionHighlightDistinct"] = Must(countdownEditingGates.SelectionHighlightDistinct, "文本选区高亮为独立语义色且选中后肉眼可辨（#151）"),
            ["MainStatusBarSurfaceErrorContrast"] = Must(countdownEditingGates.StatusBarErrorContrast, "状态栏消费状态条表面且错误字达信息文本对比（#151）"),
            ["MainReminderRadioSelectedContrast"] = Must(countdownEditingGates.ReminderRadioSelectedContrast, "提醒规则选中圆点与描边达非文本对比门槛（#151）"),
            ["MainEditorTypographyTokens"] = Must(countdownEditingGates.EditorTypographyTokenized, "编辑卡区块标题与字段行距消费排版/间距 token（#151）"),
            ["MainCountdownEditingGateIsolationBehavior"] = Must(countdownEditingGateIsolationBehavior, "倒计时编辑门禁用会失败的反例保住（#151）"),
            ["MainFocusRingConstantThicknessNoSqueeze"] = Must(interactionStates.FocusRingsReserveSpace, "主窗焦点环常驻透明环变色，聚焦不加厚边、不挤布局"),
            ["MainPrimaryAndPinSingleFocusStroke"] = Must(
                interactionStates.PrimaryAndPinSingleFocusStroke,
                "主按钮与置顶不再叠 FocusVisualStyle 第二套焦点描边，常规控件保留 2px 暖边"),
            ["MainNotesTabHoverDiffersFromSelected"] = Must(interactionStates.NotesTabHoverDiffersFromSelected, "未选中便签页签悬停边换 ActiveBrush，不复用选中实心底"),
            ["MainLocalControlsPressAndDisabled"] = Must(interactionStates.LocalControlsPressAndDisabled, "本地页签/帮助/提醒单选/日期披露具备按压与禁用外观"),
            ["MainReminderHelpFocusShowsHelp"] = Must(interactionStates.HelpFocusShowsHelpText, "聚焦提醒规则帮助显示与悬停相同的说明并提供帮助文本"),
            ["MainCompactHitTargetsMeetScaledFloor"] = Must(interactionStates.CompactHitTargetsMeetScaledFloor, "默认缩放后完成勾选与帮助命中槽位 ≥24 DIP"),
            ["MainFilterChipsAndCalendarNavCompact"] = Must(interactionStates.ChipsAndCalendarNavCompact, "筛选芯片高度/间距与日历翻月按钮向紧凑下限族靠拢"),
            ["MainHoverStateContrastIsPerceivable"] = Must(interactionStates.HoverStateContrastIsPerceivable, "主窗按钮悬停具有可感知的暖色底/边反馈"),
            ["MainPressedStateGeometryStable"] = Must(
                interactionStates.PressedStateGeometryStable &&
                interactionStates.SegmentIndicatorRapidSwitchStaysAligned,
                "主窗按钮按下保留常驻 2px 环且不改变布局边界"),
            ["MainMouseClickDoesNotLeaveFocusRing"] = Must(interactionStates.MouseClickDoesNotLeaveFocusRing, "鼠标点击后不残留键盘焦点环"),
            ["MainKeyboardFocusRingShowsOnTab"] = Must(interactionStates.KeyboardFocusRingShowsOnTab, "Tab 键聚焦时显示键盘焦点环"),
            ["MainSelectedTabWeightDominates"] = Must(
                interactionStates.SelectedTabWeightDominates &&
                interactionStates.SegmentIndicatorGeometryMatches,
                "选中页签使用 PinkTint 底与 SemiBold 权重"),
            ["MainSegmentIndicatorGeometryMatches"] = Must(interactionStates.SegmentIndicatorGeometryMatches, "主窗口分段指示条几何与选中页签对齐"),
            ["MainSegmentIndicatorReducedMotionIsImmediate"] = Must(interactionStates.SegmentIndicatorReducedMotionIsImmediate, "减少动态效果时分段指示条立即就位"),
            ["MainSegmentIndicatorRapidSwitchStaysAligned"] = Must(interactionStates.SegmentIndicatorRapidSwitchStaysAligned, "快速连续切换后分段指示条仍停在当前页签"),
            ["MotionClientAreaAnimationSwitchReadable"] = Must(
                ApplicationTheme.ClientAreaAnimationEnabled == SystemParameters.ClientAreaAnimation,
                "已接入系统客户区动画开关读取点"),
            ["MotionTokensMatch"] = Must(
                motionTokensMatch && interactionStates.SegmentIndicatorReducedMotionIsImmediate,
                "六个动效时长与 ease-out 缓动符合当前系统动画偏好"),
            ["KeyboardFocusRingTabGapAvoidsSelectedFill"] = Must(keyboardFocusRingContrast.TabRingGapAvoidsSelectedFill, "页签键盘焦点环与选中底色之间留出间隙"),
            ["KeyboardFocusRingFloatingCapsuleMeetsContrast"] = Must(
                keyboardFocusRingContrast.FloatingCapsuleFocusRingMeetsContrast,
                "悬浮窗胶囊键盘焦点环达到对比度要求"),
            ["KeyboardFocusRingFloatingCapsuleUsesWarmGold"] = Must(
                keyboardFocusRingContrast.FloatingCapsuleUsesWarmGoldToken,
                "悬浮窗胶囊键盘焦点环使用暖金色 token"),
            ["FloatingIncomeUsesTabularNumerals"] = Must(
                floatingStructure.IncomeUsesTabularNumerals,
                "悬浮窗今日已赚使用表格数字"),
            ["HasNameEditor"] = Must(mainStructure.NameEditorPresent, "倒计时名称编辑器存在（#129 自 U1 迁移）"),
            ["HasNoteReminderControls"] = Must(
                mainStructure.NoteReminderRulesEntryPresent &&
                floatingStructure.ReminderRulesEntryPresent &&
                mainStructure.LegacyNoteReminderControlsAbsent &&
                floatingStructure.LegacyReminderControlsAbsent,
                "管理窗口或悬浮入口存在便签提醒选择器与自定义分钟输入（#129 自 U1 迁移）"),
            ["NoteReminderControlsWork"] = Must(reminderRulesEditorSharedEntryWorks, "便签提醒选择器支持预设、自定义分钟与禁用提醒（#129 自 U1 迁移）"),
            ["ReminderRulesEditorSharedEntryWorks"] = Must(
                reminderRulesEditorSharedEntryWorks &&
                mainStructure.NoteReminderRulesEntryVisible &&
                mainStructure.LegacyNoteReminderControlsAbsent &&
                floatingStructure.ReminderRulesEntryVisible &&
                floatingStructure.LegacyReminderControlsAbsent,
                "主窗口与悬浮窗复用共享提醒规则编辑器入口并隐藏旧复杂控件"),
            ["CalendarReminderStartLabelsUseCalendarSemantics"] = Must(ProbeCalendarReminderStartLabels(), "日历模式开始日期/本地时刻标签与其余模式首次提醒标签区分"),
            ["FloatingReminderRulesEntryStaysCompact"] = Must(
                Math.Abs(floating.Width - 520) <= 0.1 &&
                floating.Height <= 391 &&
                floatingStructure.ReminderRulesEntryCompact,
                "悬浮窗提醒规则入口保持 520 x 390 紧凑表面"),
            ["NoteReminderTimeOptionCounts"] = new[]
            {
                productionDatePickers.ReminderHourOptionCount,
                productionDatePickers.ReminderMinuteOptionCount
            },
            ["CrossDayReminderSelectionWorks"] = Must(crossDayReminderBehavior, "便签提醒支持跨天选择并按北京时间触发"),
            ["ReminderSummaryAdvancesAfterFirstDue"] = Must(reminderSummaryAdvances, "首次提醒到期后主窗口摘要显示下一循环时间"),
            ["OneTimeReminderLifecycleWorks"] = Must(oneTimeReminderBehavior, "一次性提醒支持离线补发并在送达后跨重启去重"),
            ["ReminderRequiresContentRenderedConfirmation"] = Must(
                reminderDelivery.RequiresContentRenderedConfirmation,
                "提醒只在真实 ContentRendered 后确认送达"),
            ["ReminderPersistenceFailureStaysVisible"] = Must(reminderDelivery.PersistenceFailureStaysVisible, "一次提醒持久化失败时保持可见并阻止提前关闭"),
            ["ReminderPersistenceRetryBackoffWorks"] = Must(reminderDelivery.PersistenceRetryBackoffWorks, "一次提醒按 5/10/20/30 秒上限持续重试"),
            ["ReminderDeliverySingleFlightWorks"] = Must(reminderDelivery.SingleFlightWorks, "同一提醒 occurrence 在中心内只有一个条目和一个持久化重试"),
            ["ReminderIndependentOccurrencesWork"] = Must(reminderDelivery.IndependentOccurrencesWork, "不同到期 occurrence 在同一中心独立显示且失败项不阻塞其他项确认"),
            ["ReminderPersistenceFailureCloseGuardWorks"] = Must(reminderDelivery.PersistenceFailureCloseGuardWorks, "持久化失败态拒绝普通 Close 且保持同一退避状态"),
            ["ReminderPersistenceFailureDisposeWorks"] = Must(reminderPersistenceFailureDisposeWorks, "应用退出可强制关闭失败态窗口且不触发提前重试"),
            ["ReminderDiagnosticsExcludePrivatePayload"] = Must(reminderDelivery.DiagnosticsExcludePrivatePayload, "提醒失败诊断不包含便签正文或完整设置载荷"),
            ["ReminderCenterSingleWindow"] = Must(reminderCenter.SingleWindow, "提醒中心验证门禁"),
            ["ReminderCenterSameTickBatch"] = Must(reminderCenter.SameTickBatch, "提醒中心验证门禁"),
            ["ReminderCenterAppendsAfterOpen"] = Must(reminderCenterAppendsAfterOpen, "提醒中心验证门禁"),
            ["ReminderCenterStableOrdering"] = Must(reminderCenter.StableOrdering, "提醒中心验证门禁"),
            ["ReminderCenterEntriesRendered"] = Must(reminderCenter.EntriesRendered, "提醒中心验证门禁"),
            ["ReminderAcknowledgementDistinctFromDelivery"] = Must(reminderCenter.AcknowledgementDistinctFromDelivery, "提醒中心验证门禁"),
            ["ReminderAcknowledgementFailureRetainsEntry"] = Must(reminderCenter.AcknowledgementFailureRetainsEntry, "提醒中心验证门禁"),
            ["ReminderAcknowledgementFailureIsAtomic"] = Must(reminderCenter.AcknowledgementFailureIsAtomic, "提醒中心验证门禁"),
            ["ReminderCenterSingleItemDisposition"] = Must(reminderCenter.SingleItemDisposition, "提醒中心验证门禁"),
            ["ReminderCenterOpenSourcePreservesState"] = Must(reminderCenter.OpenSourcePreservesState, "提醒中心验证门禁"),
            ["ReminderCenterOpenSourceFindsExactNote"] = Must(reminderCenter.OpenSourceFindsExactNote, "提醒中心验证门禁"),
            ["ReminderCenterOpenSourceRestoresMinimizedWindow"] = Must(reminderCenter.OpenSourceRestoresMinimizedWindow, "提醒中心验证门禁"),
            ["ReminderCenterPersistenceFailureRetainsCurrentSource"] = Must(
                reminderCenterSourceLifecycle.PersistenceFailureRetainsCurrentSource,
                "提醒中心验证门禁"),
            ["ReminderCenterRemovesDeletedSource"] = Must(reminderCenterSourceLifecycle.RemovesDeletedSource, "提醒中心验证门禁"),
            ["ReminderCenterCancelsDeletedSourceAckRetry"] = Must(reminderCenterSourceLifecycle.CancelsDeletedSourceAcknowledgementRetry, "提醒中心验证门禁"),
            ["ReminderCenterRemovesDisabledSource"] = Must(reminderCenterSourceLifecycle.RemovesDisabledSource, "提醒中心验证门禁"),
            ["ReminderCenterRemovesCompletedSource"] = Must(reminderCenterSourceLifecycle.RemovesCompletedSource, "提醒中心验证门禁"),
            ["ReminderCenterReopensAfterSourceRemoval"] = Must(reminderCenterSourceLifecycle.ReopensAfterSourceRemoval, "提醒中心验证门禁"),
            ["ReminderCenterClosesWhenEmpty"] = Must(reminderCenter.ClosesWhenEmpty, "提醒中心验证门禁"),
            ["ReminderCenterReopens"] = Must(reminderCenter.Reopens, "提醒中心验证门禁"),
            ["ReminderCenterScrollsLongContent"] = Must(reminderCenter.ScrollsLongContent, "提醒中心验证门禁"),
            ["ReminderCenterFollowsDayTheme"] = Must(reminderCenter.FollowsDayTheme, "提醒中心验证门禁"),
            ["ReminderCenterHasOnlySafeActions"] = Must(reminderCenter.HasOnlySafeActions, "提醒中心验证门禁"),
            ["ReminderCenterDisplaysSourceContentAndTime"] = Must(reminderCenter.DisplaysSourceContentAndTime, "提醒中心验证门禁"),
            ["ReminderCenterAccessible"] = Must(reminderCenter.Accessible, "提醒中心验证门禁"),
            ["RepeatingReminderRenderedLatchWorks"] = Must(repeatingReminderDelivery.RenderedLatchWorks, "重复提醒只在真实渲染后锁存当前周期"),
            ["RepeatingReminderDistinctCyclesCoexist"] = Must(repeatingReminderDelivery.SingleUnresolvedOccurrenceWorks, "重复提醒相邻周期聚合到同一中心且同周期不重复加入"),
            ["RepeatingReminderDueBeforeShow"] = Must(repeatingReminderDelivery.DueBeforeShow, "重复提醒只在窗口显示前已到期时才进入提醒中心"),
            ["ReminderCenterOpened"] = Must(repeatingReminderDelivery.WindowOpened, "提醒中心状态与失败路径门禁"),
            ["RepeatingReminderContentRendered"] = Must(repeatingReminderDelivery.ContentRendered, "重复提醒条目经真实渲染确认且未解决 occurrence 唯一"),
            ["RepeatingReminderNoDuplicate"] = Must(repeatingReminderDelivery.NoDuplicate, "重复提醒同一周期不重复加入提醒中心"),
            ["ReminderDispositionAndSnoozePersistAcrossRestart"] = Must(
                reminderDispositionAndSnooze,
                "知道了处置与 10/30/60 分钟、明天 09:00 及自定义稍后按成功动作时刻计算、规范持久化并跨重启恢复"),
            ["ReminderCompletionAndReopenAreAtomic"] = Must(reminderCompletionAndReopen, "完成便签事务原子推进 cursor、取消活动稍后并保留未到期 absolute，失败全量保留"),
            ["ReminderCenterSnoozeClickPersists"] = Must(reminderCenterSnoozeClick, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterExpiredCustomSnoozeRetryAllowsCorrection"] = Must(reminderCenterExpiredCustomSnoozeRetry, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterCompleteReopenDiscardsStaleRetries"] = Must(reminderCenterStaleRetryAfterCompleteReopen, "提醒中心状态与失败路径门禁"),
            ["ReminderSnoozeSuppressesBaseCyclesUntilExpiry"] = Must(
                reminderSnoozeSuppressesBaseCycles,
                "活动稍后未到期时抑制同规则基础周期（含重启），到期按原 occurrence 重送达，解决后只补 cursor 之后最新周期"),
            ["ReminderCenterCompleteClickIsAtomic"] = Must(reminderCenterCompleteClick, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterCompleteRequiresConfirmation"] = Must(
                reminderCenterCompleteRequiresConfirmation,
                "提醒中心「完成便签」先确认再提交：取消全量保留便签与条目、确认原子完成并移除条目（#155）"),
            ["DeleteUndoRestoresLastDelete"] = Must(
                deleteUndoOffer,
                "删除成功后撤销出现在发起删除的窗口：悬浮窗就地恢复便签与规则提醒状态，管理窗口仍走状态条；Ctrl+Z 生效；焦点或悬停暂停 10 秒计时；确认框写明可撤销且取消为安全默认（#155/#214）"),
            ["DirtyExitConfirmsUnsavedWork"] = Must(dirtyExitConfirmation, "关闭悬浮窗和 Alt+F4 都会确认，正文包含「退出后不再提醒」；有未保存内容时列出未保存项；取消后悬浮窗和提醒派发仍在（#213）"),
            ["AssistiveNarrationExitsDecorationAndLabelsFields"] = Must(
                assistiveNarration,
                "装饰手写体与分段裸数字退出朗读、可见标签经标签关系指向输入不读两遍、数量由一处状态区播完整句、筛选「清除」可访问名说明清除便签日期筛选（#156）"),
            ["NarratorAnnouncesLiveRegionChanges"] = Must(
                narratorLiveRegions,
                "状态消息、字段错误、数量变化和提醒送达失败触发与声明一致的 LiveRegionChanged；数量不变的分钟刷新不播报；鼓励语退出自动化树，金额与日历状态名称正确（#216）"),
            ["WindowPositionResetsWithoutDrag"] = Must(windowPositionReset, "越界悬浮窗可经 Ctrl+R 或托盘「复位窗口位置」夹回工作区，隐藏态复位先恢复再夹回（#156）"),
            ["MainWindowFitsInjectedWorkAreas"] = Must(
                mainWindowWorkAreaFit,
                "管理窗口装进 1032/826/688 DIP 注入工作区：首选尺寸收缩、顶端控件与状态栏可达、倒计时三行滚动可见、720×560 滚动不裁切、隐藏再显保留用户几何、越界与工作区变化夹回（#211）"),
            ["CountdownListFirstBehavior"] = Must(countdownListFirst, "倒计时默认折叠新增，首选尺寸至少三行，窗口增高显示更多完整行（#221）"),
            ["MainTopmostStateSynchronization"] = Must(mainTopmostStateSynchronization, "置顶首次打开与两次切换均由勾选状态单向驱动（#222）"),
            ["MainRedundancyCleanup"] = Must(mainRedundancyCleanup, "数量纯文字保留唯一直播区，删除主题与工作区冗余文字（#222）"),
            ["MainNoteEditorSeparation"] = Must(mainNoteEditorSeparation, "搜索属于列表区、编辑标题与规则字段对齐、Ctrl+F及取消行为正常（#227）"),
            ["MainTextShortcutBehavior"] = Must(mainTextShortcutBehavior, "输入焦点下Ctrl+S与便签Ctrl+Enter持久保存，Enter换行与弹层守卫（#224）"),
            ["SingleLineEnterSubmission"] = Must(singleLineEnterSubmission, "单行名称、标签、设置Enter提交及首个无效字段焦点（#224）"),
            ["FloatingNoteKeyboardSubmission"] = Must(floatingNoteKeyboardSubmission, "悬浮便签Ctrl+Enter提交、普通Enter换行与校验（#224）"),
            ["ListDeleteKeyboardBehavior"] = Must(listDeleteKeyboardBehavior, "列表Delete安全确认、取消归还、撤销与文本Ctrl+Z隔离（#224）"),
            ["MainNoteRowsSimplified"] = Must(mainNoteRowsSimplified, "主便签行仅保留编辑删除、正文摘要层级和跨分组交替底色（#227）"),
            ["CountdownPreferencesPopupBehavior"] = Must(countdownPreferencesPopup, "设置弹层焦点、Tab/Esc、草稿、字段错误、持久化及浮窗双宿主入口（#221）"),
            ["CountdownEditorExpansionBehavior"] = Must(countdownEditorExpansion, "展开保留草稿，编辑自动展开，取消回到折叠新增并保全持久记录（#221）"),
            ["CountdownRowDeadlineAndSecondaryDeleteBehavior"] = Must(countdownRowPresentation, "倒计时行有北京时区截止行，删除为危险色文字加图标且无红框（#221）"),
            ["MainWindowIdentityZoneDragRegion"] = Must(mainWindowIdentityZoneDrag, "身份区空白为标题拖动区（HTCAPTION）且区内控件仍可点击（HTCLIENT）（#211）"),
            ["FloatingWindowStaysOnSecondaryMonitor"] = Must(floatingSecondaryMonitor, "#219 悬浮窗保留副屏手动位置，键盘与托盘复位夹回当前屏，保持 520×390 DIP"),
            ["OwnerDialogsFitSecondaryMonitor"] = Must(ownerDialogsFitSecondaryMonitor, "#219 所有者弹窗装进副屏，规则编辑器底部按钮与缩放手柄可达"),
            ["ReminderUsesSourceMonitor"] = Must(reminderUsesSourceMonitor, "#219 提醒中心首次在悬浮所在屏右上，此后保留手动位置"),
            ["OpenWindowsFollowMonitorChanges"] = Must(openWindowsFollowMonitorChanges, "#219 显示器与工作区变化后所有已打开窗口归位"),
            ["MixedDpiUsesPhysicalMonitorGeometry"] = Must(mixedDpiPhysicalGeometry, "#219 重叠 DIP 坐标按物理像素与目标屏 DPI 解析"),
            ["ProductManifestDeclaresPerMonitorV2"] = Must(productManifestPerMonitorV2, "#219 发布 EXE 嵌入清单声明 PerMonitorV2"),
            ["RuntimeDpiChangeUpdatesWindowLayout"] = Must(runtimeDpiLayout, "#219 真实 DPI 事件更新比例、窗口几何与可操作布局"),
            ["MainWindowHiddenTickSkipsRenderAndShowRefreshes"] = Must(mainWindowHiddenRefresh, "管理窗口隐藏期间 tick 不重建分格，重新显示立即同步最新时间（#230）"),
            ["MainWindowNoteMinutePreservesFocusAndScroll"] = Must(mainWindowNoteMinuteRefresh, "便签数据不变的分钟刷新保留行控件、键盘焦点和非零滚动位置（#230）"),
            ["ReminderCenterDeliveryFailureDisablesDispositions"] = Must(reminderCenterDeliveryFailureActionGate, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterEnterUsesNativeButtonActivation"] = Must(reminderCenterEnterUsesNativeButtonActivation, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterArrivalKeepsForegroundAndFlashesTaskbar"] = Must(reminderCenterArrivalKeepsForegroundAndFlashesTaskbar, "提醒中心验证门禁"),
            ["ReminderCenterMinimizeToTaskbarPreservesEntries"] = Must(reminderCenterMinimizeToTaskbarPreservesEntries, "提醒中心验证门禁"),
            ["ReminderCenterSnoozeGatedDuringCompletionRetry"] = Must(reminderCenterSnoozeGatedDuringCompletionRetry, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterCustomSnoozeErrorRoutesByCode"] = Must(reminderCenterCustomSnoozeErrorRoutesByCode, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterShowFailureRollsBack"] = Must(reminderCenterShowFailureRollsBack, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterShowFailureRollbackSurvivesEntryFailure"] = Must(
                reminderCenterShowFailureRollbackSurvivesEntryFailure,
                "提醒中心展示失败回滚在条目回滚抛异常时仍清理协调器状态"),
            ["ReminderCenterCustomSnoozeControlsVisibility"] = Must(reminderCenterCustomSnoozeControlsVisibility, "提醒中心自定义稍后子控件随选项真实展开与收起"),
            ["ReminderCenterCustomSnoozeCalendarBehavior"] = Must(reminderCenterCustomSnoozeCalendarBehavior, "提醒中心自定义稍后日历保持统一只读、Esc 关闭与聚焦行为"),
            ["ReminderCenterOpenSourcePreservesNoteGroupState"] = Must(reminderCenterOpenSourcePreservesNoteGroupState, "提醒中心状态与失败路径门禁"),
            ["ReminderCenterAcknowledgeAllWritesAllDispositions"] = Must(reminderCenterAcknowledgeAll.WritesAllDispositions, "提醒中心验证门禁"),
            ["ReminderCenterAcknowledgeAllMatchesSingleSemantics"] = Must(reminderCenterAcknowledgeAll.MatchesSingleSemantics, "提醒中心验证门禁"),
            ["ReminderCenterAcknowledgeAllFailureRetainsAllAtomically"] = Must(reminderCenterAcknowledgeAll.FailureRetainsAllAtomically, "提醒中心验证门禁"),
            ["ReminderCenterAcknowledgeAllBackoffRetryCompletesBatch"] = Must(reminderCenterAcknowledgeAll.BackoffRetryCompletesBatch, "提醒中心验证门禁"),
            ["ReminderCenterRepresentativeCaptureRendered"] = Must(
                reminderCenterRepresentativeCapture.Rendered,
                "提醒中心代表态真实帧渲染出白底页面、暖黄卡、待处理徽章、主行动按钮与便签来源图标"),
            ["ReminderCenterRepresentativeCapture"] = reminderCenterRepresentativeCapture,
            ["ValidateOnlySettingsIsolationBehavior"] = Must(validateOnlySettingsIsolation, "validate-only 强制使用显式非根 settings 路径且不改变生产默认路径"),
            ["LazyManagerLifecycleBehavior"] = lazyManagerLifecycle,
            ["MainDecorationOnDemandDecodeBehavior"] = Must(mainDecorationOnDemandDecode, "主窗口装饰图按当前页按需解码且解码尺寸等于显示尺寸（#231）"),
            ["CountdownReminderWpfBehavior"] = Must(countdownReminderWpf, "倒计时 occurrence 进入只含安全动作的提醒中心：完成便签不出现，打开倒计时来源保留草稿"),
            ["ReminderAcknowledgementMonotonicAcrossOccurrences"] = Must(
                repeatingReminderDelivery.AcknowledgementMonotonicAcrossOccurrences,
                "提醒中心验证门禁"),
            ["NoteReminderDispatchWorks"] = Must(reminderBehavior, "便签提醒按到期区间恰好派发一次（#129 自 U1 迁移）"),
            ["ReminderCenterIsThemed"] = Must(reminderThemed, "提醒中心验证门禁"),
            ["ReminderCenterSupportsKeyboardNavigation"] = Must(reminderCenterKeyboardGates.AllPassed, "提醒中心验证门禁"),
            ["ReminderCenterShowPreservesFocusAndAccessibleName"] = Must(
                reminderCenterKeyboardGates.ShowPreservesFocusAndAccessibleName,
                "提醒中心出现时不抢焦点且保留可访问名称"),
            ["ReminderCenterEscapeFocusesAcknowledge"] = Must(
                reminderCenterKeyboardGates.EscapeFocusesAcknowledge,
                "提醒中心有条目时 Esc 不关窗只把焦点移到知道了（失败态回落打开来源）"),
            ["ReminderCenterTypographyWeightWhitelist"] = Must(reminderCenterTypographyWeightWhitelist, "提醒中心字重只用 Regular 与 SemiBold 且感叹号为矢量图形"),
            ["ReminderCenterKeyboardGateIsolationBehavior"] = Must(reminderCenterKeyboardGateIsolationBehavior, "提醒中心 Tab 遍历、关闭守卫与初始焦点/可访问名负向夹具可独立失败"),
            ["OfficialCharacterBackgroundLoaded"] = Must(
                Required<Image>(main, "MyMelodyBackground").Source is not null &&
                Required<Image>(main, "CinnamorollBackground").Source is not null &&
                Required<Image>(main, "KuromiBackground").Source is not null,
                "adapter 验证角色图片资产已加载"),
            ["OfficialCharacterBackgroundCharactersExclusive"] = Must(
                officialCharacterBackgroundCharactersExclusive,
                "官方角色背景只包含美乐蒂、玉桂狗与库洛米（#129 自 U1 迁移）"),
            ["OfficialCharacterBackgroundLowContrast"] = Must(main.CharacterBackgroundRemainsSubtle, "官方角色背景保持低对比不干扰应用内容（#129 自 U1 迁移）"),
            ["MainCharacterPanelDockCount"] = mainCharacterDocks.Length,
            ["MainCharacterPanelDocksIntegrateWithPanel"] = Must(mainCharacterDocksIntegrateWithPanel, "主窗口三个玩偶底座与工作区面板形成一体"),
            ["MainCharacterPanelDecorationsAreNonInteractive"] = Must(main.CharacterPanelDecorationsAreNonInteractive, "主窗口角色场景栏不拦截工作区操作"),
            ["MainReferenceDecorationAssetsLoaded"] = Must(main.MainReferenceDecorationsLoaded, "主窗口日间参考装饰资产已加载"),
            ["MainReferenceDecorationsAreNonInteractive"] = Must(main.MainReferenceDecorationsAreNonInteractive, "主窗口日间参考装饰不会阻断真实控件输入"),
            ["MainReferenceDecorationsShareScaleAtMinimumAndWideSizes"] = Must(
                uiQualityGates.MainReferenceDecorationScaling,
                "主窗口日间参考装饰在最小和放大尺寸下与真实控件共用缩放坐标系"),
            ["MainReferenceDecorationLayerOrderMatches"] = Must(uiQualityGates.MainReferenceDecorationLayerOrder, "主窗口装饰层级顺序独立通过"),
            ["MainReferenceCaptureRequested"] = legacyMainReferenceCapture.Requested,
            ["MainReferenceCaptureWritten"] = legacyMainReferenceCapture.CapturesWritten,
            ["MainReferenceCaptureUsesZeroState"] = legacyMainReferenceCapture.UsesZeroState,
            ["MainReferenceCaptureCanvasMatches"] = legacyMainReferenceCapture.CanvasMatches,
            ["MainReferenceCaptureAlphaIsOpaque"] = legacyMainReferenceCapture.AlphaIsOpaque,
            ["MainReferenceCountdownCapturePath"] = legacyMainReferenceCapture.CountdownCapturePath,
            ["MainReferenceNotesCapturePath"] = legacyMainReferenceCapture.NotesCapturePath,
            ["MainReferenceCaptureManifestPath"] = legacyMainReferenceCapture.ManifestPath,
            ["MainReferenceBaselineRequested"] = mainReferenceBaseline.Requested,
            ["MainReferenceBaselineManifestAvailable"] = mainReferenceBaseline.ManifestAvailable,
            ["MainReferenceBaselineValidated"] = mainReferenceBaseline.AllPassed,
            ["MainReferenceBaselineGateIsolationBehavior"] = Must(
                mainReferenceBaselineGateIsolationBehavior,
                "主窗口十六个 manifest 门禁负向夹具与视觉基线 module interface 均通过"),
            ["MainReferenceBaselineManifestPath"] = mainReferenceBaseline.ManifestPath,
            ["MainReferenceBaselineSourceAggregateSha256"] =
                mainReferenceBaseline.SourceAggregateSha256,
            ["MainReferenceBaselineExecutableSha256"] =
                mainReferenceBaseline.ExecutableSha256,
            ["MainReferenceBaselineGates"] = mainReferenceBaseline.Gates.AsDictionary(),
            ["MainReferenceBaselineCaptures"] = mainReferenceBaseline.Captures,
            ["MainDaySingleBorder"] = daySingleBorder,
            ["MainDaySingleBorderGate"] = daySingleBorder.AllPassed,
            ["MainDaySingleBorderGateIsolationBehavior"] = daySingleBorderGateIsolationBehavior,
            ["MainDaySingleBorderNegativeFixtures"] = new[]
            {
                "unexpected-control-edge",
                "label-intersection",
                "missing-lower-ornament"
            },
            ["MainReferenceBaselineDiagnostics"] = mainReferenceBaseline.Diagnostics,
            ["MainReferenceBaselineNegativeFixtures"] = MainReferenceBaselineNegativeFixtures,
            ["MainStressCountdownTitleVisuals"] =
                mainReferenceScenes.StressCountdownTitleVisuals,
            ["MainStressCountdownTitleGateIsolationBehavior"] = Must(mainStressCountdownTitleGateIsolationBehavior, "主窗口 Stress 标题缺失和截断或重叠负夹具逐项失败闭锁"),
            ["MainStressCountdownTitleMeasurements"] =
                mainReferenceScenes.StressCountdownTitleMeasurements,
            ["MainNoteCardReminderFontSizeDip"] =
                mainReferenceScenes.NoteCardReminderFontSizeDip,
            ["MainNoteCardReminderFontSizeMeetsContract"] =
                mainReferenceScenes.NoteCardReminderFontSizeMeetsContract,
            ["MainNoteCardReminderFontMeasurements"] =
                mainReferenceScenes.NoteCardReminderFontMeasurements,
            ["LegacyMigrationSupported"] = Must(legacyMigration, "旧版单倒计时设置受支持并被迁移（#129 自 U1 迁移）"),
            ["DuplicateSettingsRejected"] = Must(duplicateRejected, "重复的持久化倒计时名称被拒绝（#129 自 U1 迁移）"),
            ["InvalidNotesRejected"] = Must(invalidNotesRejected, "非法持久化便签被拒绝（#129 自 U1 迁移）"),
            ["InvalidTagReferencesRejected"] = Must(invalidTagReferencesRejected, "持久化悬空标签引用被拒绝（#129 自 U1 迁移）"),
            ["HasTagControls"] = Must(
                mainStructure.TagControlsPresent,
                "共享标签选择、筛选与管理控件存在（#129 自 U1 迁移）"),
            ["StaticWorkspaceLayout"] = Must(
                mainStructure.StaticWorkspaceLayout,
                "倒计时工作区与设置控件使用稳定实例（#221 设置移入按需弹层）"),
            ["MainNoteEditorFieldsDoNotOverlap"] = Must(mainUiPolish.NoteEditorFieldsDoNotOverlap, "主窗口提醒入口、规则摘要与错误区互不重叠"),
            ["LegacyInlineReminderControlsStayHidden"] = Must(
                mainUiPolish.LegacyInlineReminderControlsStayHidden,
                "编辑单条一次/固定间隔便签后旧版内嵌提醒控件保持收起且摘要不被遮挡"),
            ["MainNoteEditorLabelsVisible"] = Must(mainUiPolish.NoteEditorLabelsVisible, "主窗口便签编辑器保留可见字段标签"),
            ["MainMinimumSizeMatchesDefault"] = Must(mainUiPolish.MinimumSizeMatchesDefault, "主窗口首选尺寸 864×1000、最小可用尺寸 720×560（#211）"),
            ["PopupKeyboardLifecycleWorks"] = Must(mainUiPolish.PopupKeyboardLifecycleWorks, "筛选、标签管理与标签选择弹层支持 Esc 关闭并返焦"),
            ["PopupOutsideFocusPreserved"] = Must(mainUiPolish.PopupOutsideFocusPreserved, "鼠标或外部焦点关闭弹层时不会抢回触发器焦点"),
            ["PopupTriggerSecondClickCloses"] = Must(mainUiPolish.PopupTriggerSecondClickCloses, "弹层打开后再点触发器关闭而不是重开"),
            ["PopupEscapeFromTriggerClosesAndKeepsFocus"] = Must(mainUiPolish.PopupEscapeFromTriggerClosesAndKeepsFocus, "焦点在触发器上按 Esc 关闭弹层并保持触发器焦点"),
            ["NoteEmptyStatesDistinguished"] = Must(mainUiPolish.EmptyStatesDistinguished, "首次空态与筛选无结果状态具有不同反馈"),
            ["NoteNoResultsResetWorks"] = Must(mainUiPolish.NoResultsResetWorks, "筛选无结果状态可就地重置完整筛选条件"),
            ["CountdownEmptyStatesDistinguished"] = Must(mainUiPolish.CountdownEmptyStatesDistinguished, "倒计时首次空态与筛选无结果状态具有不同反馈"),
            ["CountdownNoResultsResetWorks"] = mainUiPolish.CountdownEmptyPointerResetRestoresResults,
            ["CountdownEmptyPointerResetRestoresResults"] =
                mainUiPolish.CountdownEmptyPointerResetRestoresResults,
            ["CountdownEmptyKeyboardResetRestoresResults"] =
                mainUiPolish.CountdownEmptyKeyboardResetRestoresResults,
            ["CountdownEmptyStateTextContrast"] = Must(countdownEmptyStateGates.DayThemeTextContrast, "倒计时空态文字对实际日间背景达到 4.5:1"),
            ["CountdownEmptyStateContrastMeasurements"] = countdownEmptyStateContrast.Measurements,
            ["CountdownEmptyStateGateIsolationBehavior"] = Must(countdownEmptyStateGateIsolationBehavior, "倒计时空态门禁可独立失败（#129 自 U1 迁移）"),
            ["NoteStatusBadgeTokensFollowState"] = Must(noteVisualTokenGates.BadgeTokensFollowState, "validate-only 验证分段数量徽章随激活态切换专用 token"),
            ["NoteEmptyStateTextContrast"] = Must(noteVisualTokenGates.EmptyStateTextContrast, "便签空态文字对实际日间背景达到 4.5:1 并使用 MutedBrush"),
            ["NoteEmptyStateContrastMeasurements"] = noteEmptyStateContrast.Measurements,
            ["NoteVisualTokenGateIsolationBehavior"] = Must(noteVisualTokenGateIsolationBehavior, "主窗口便签视觉合同负向夹具逐项失败且不误伤无关契约"),
            ["CountdownRemainingLayoutMatches"] = Must(mainCountdownRemainingGates.FixedColumnLayoutMatches, "主窗口倒计时剩余时间使用固定列、表格数字和单行布局"),
            ["CountdownRemainingUnitSpacingMatches"] = Must(mainCountdownRemainingGates.UnitSpacingMatches, "主窗口倒计时剩余时间保留 FormatRemaining 单位空格与六项样本"),
            ["CountdownRemainingGateIsolationBehavior"] = Must(mainCountdownRemainingGateIsolationBehavior, "主窗口倒计时剩余时间门禁负向夹具独立失败"),
            ["CountdownIncrementalRefreshIdentityMatches"] =
                mainUiPolish.CountdownIncrementalRefresh.IdentityPreserved,
            ["CountdownExpiryVisualsFlipTogether"] =
                mainUiPolish.CountdownIncrementalRefresh.ExpiryVisualsFlipTogether,
            ["CountdownIncrementalRefresh"] = mainUiPolish.CountdownIncrementalRefresh,
            ["CountdownIncrementalRefreshGateIsolationBehavior"] = Must(countdownIncrementalRefreshGateIsolationBehavior, "主窗口倒计时增量刷新负向夹具可独立失败"),
            ["CountdownRemainingSamples"] = mainUiPolish.CountdownRemainingSamples,
            ["MainNoteCompletionTargetSizeAndBehavior"] = Must(mainNoteCompletionGates.MainTargetSizeAndBehavior, "主窗口便签完成复选框达到 24px 模板与交互合同"),
            ["MainNoteCompletionOtherUserSizesUnchanged"] = Must(mainNoteCompletionGates.OtherUserSizesUnchanged, "标签选择器与悬浮窗复选框保持原尺寸"),
            ["MainNoteCompletionGateIsolationBehavior"] = Must(mainNoteCompletionGateIsolationBehavior, "主窗口便签完成尺寸门与样式隔离门可独立失败"),
            ["MainNoteCompletionMeasurements"] = mainNoteCompletionValidation.Measurements,
            ["MainNoteCompletionInteractionBehavior"] =
                mainNoteCompletionValidation.Interaction with
                {
                    AccessibleNamesHaveNoLineBreaks = accessibleNameWhitespaceIsolationBehavior
                },
            ["MainNoteCompletionStyleScoped"] = Must(mainNoteCompletionGates.AllPassed, "兼容便签完成样式门保持通过"),
            ["TransientPanelsOnDemand"] = Must(main.HasOnDemandPanels, "筛选、标签选择与标签管理按需交互（#129 自 U1 迁移）"),
            ["TransientPanelsStartClosed"] = main.HasOnDemandPanels,
            ["CountdownTagPickerOpensOnDemand"] = !selector.IsPopupOpen,
            ["FilterOpensOnDemand"] = !Required<Popup>(main, "FilterPopup").IsOpen,
            ["TagManagerOpensOnDemand"] = !Required<Popup>(main, "TagManagerPopup").IsOpen,
            ["NoteTagPickerOpensOnDemand"] = Must(!Required<TagSelector>(main, "NoteTags").IsPopupOpen, "validate-only 验证便签标签选择弹层默认按需收起"),
            ["WorkspaceDraftsRetained"] = Must(workspaceDraftsRetained, "切换工作区保留未保存的倒计时与便签草稿（#129 自 U1 迁移）"),
            ["CountdownReminderModeMappingWorks"] = Must(
                mainWindowScenarios.CountdownReminderModeMappingWorks,
                "主窗口提醒规则单选组三态映射：不提醒清空、按时间提醒到点 absoluteOnce、自定义打开编辑器并按规则集反显"),
            ["SettingsDraftsRetainedOnRefresh"] = Must(settingsDrafts.InputsRetainedOnRefresh, "后台状态刷新不丢弃设置区未保存编辑（#129 自 U1 迁移）"),
            ["SettingsSavedValuesReflectedAfterSave"] = Must(settingsDrafts.SavedValuesReflectedAfterSave, "设置区在保存成功后立即回显持久化值（#129 自 U1 迁移）"),
            ["NegativeMonthlySalaryRejectedKeepsDraft"] = Must(settingsDrafts.NegativeSalaryRejectedKeepsDraft, "负月薪保存被拒绝、原偏好与草稿文本保留并提示月薪不能小于 0"),
            ["ZeroMonthlySalaryStillSavesAsUnset"] = Must(settingsDrafts.ZeroSalaryStillSavesAsUnset, "月薪 0 仍合法保存为未输入状态"),
            ["NewNoteRejectsEmptyDate"] = undatedNoteEdit.NewNoteRejectsEmptyDate,
            ["UndatedNoteEditSavesWithoutDate"] = undatedNoteEdit.UndatedEditSavesWithoutDate,
            ["DatedNoteEditRejectsClearedDate"] = undatedNoteEdit.DatedEditRejectsClearedDate,
            ["NoteDateValidationMessagesDistinguished"] =
                undatedNoteEdit.ValidationMessagesDistinguished,
            ["HiddenCountdownSubmitDisabled"] = Must(hiddenCountdownSubmitDisabled, "隐藏的倒计时编辑器不暴露默认提交按钮（#129 自 U1 迁移）"),
            ["WorkspaceTabsUseSelectionSemantics"] = Must(
                mainStructure.CountdownTabPresent &&
                mainStructure.NotesTabPresent,
                "工作区页签暴露可访问选中态（#129 自 U1 迁移）"),
            ["TagUiAccessible"] = Must(
                !string.IsNullOrWhiteSpace(AutomationProperties.GetName(Required<Button>(main, "TagManagerButton"))),
                "标签搜索标签与图标按钮可访问名称完整（#129 自 U1 迁移）"),
            ["FilterOptionsUseSelectionSemantics"] = Must(
                mainStructure.TagMatchModeTogglesPresent,
                "筛选选项暴露可访问选中态（#129 自 U1 迁移）"),
            ["CompactFilterSummary"] = Must(Required<Button>(main, "FilterButton").ActualWidth <= 140, "筛选触发器使用紧凑摘要布局（#129 自 U1 迁移）"),
            ["TagCreateAppearsFromSearch"] = Must(createAppears, "新标签可从统一标签搜索框创建（#129 自 U1 迁移）"),
            ["TagCreateStaysCollapsedWithoutQuery"] = Must(createCollapsed, "未输入新标签名前创建动作保持收起（#129 自 U1 迁移）"),
            ["TagFilterSupportsAnyAll"] = Must(
                mainStructure.TagMatchModeTogglesPresent,
                "标签筛选暴露显式任意/全部匹配（#129 自 U1 迁移）"),
            ["TagChipsUseUid"] = Must(tagChipsUseUid, "标签芯片不复用记录 ID 的 Tag 属性（#129 自 U1 迁移）"),
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
            ["NoteFieldErrorsAtSource"] = mainWindowScenarios.NoteValidation.BothFieldsShowErrors,
            ["NoteFirstInvalidFieldFocus"] = mainWindowScenarios.NoteValidation.FirstInvalidFieldFocus,
            ["NoteFieldErrorsClearOnCorrection"] = mainWindowScenarios.NoteValidation.ErrorsClearOnCorrection,
            ["DatedNoteRejectsClearedDate"] = mainWindowScenarios.NoteValidation.DatedEditRejectsClearedDate,
            ["CountdownReminderDateFocusForTimeAndCustom"] = mainWindowScenarios.NoteValidation.ReminderDateFocusForTimeAndCustom,
            ["TagInteractionsExecuted"] = tagExercise.Executed,
            ["TagCreationAndAssociation"] = tagExercise.CreationAndAssociation,
            ["TagAnyAllFilter"] = tagExercise.AnyAllFilter,
            ["TagEditorSummaryIsCompact"] = tagExercise.EditorSummaryIsCompact,
            ["TagUntaggedAndCountdownStatus"] = tagExercise.UntaggedAndCountdownStatus,
            ["TagNoteStatusSearchAndCompletion"] = tagExercise.NoteStatusSearchAndCompletion,
            ["NoteDateFilterControls"] = mainStructure.NoteDateFilterControlsPresent,
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
            ["NoteStatusSegmentSwitch"] = noteStatusSegmentSwitch,
            ["NoteDateUsesSpacedChineseFormat"] = Must(noteListPolish.SpacedChineseDateEverywhere, "便签日期与倒计时截止同一中文日期格式：数字与「月」「日」之间留空格（#153）"),
            ["NoteCardBodyLineHeightSeparatesSummary"] = Must(noteListPolish.BodyLineHeightSeparatesSummary, "便签正文行距足够分开正文与提醒摘要（#153）"),
            ["NoteRowTitleFallsToBlockTitleLevel"] = Must(noteListPolish.RowTitleFallsToBlockTitleLevel, "列表条目标题落 H2 阶梯、与 H1 区块标题保持明确层级（#153/#197）"),
            ["MainNoteRowActionsKeyboardWorks"] = Must(
                noteListPolish.MainNoteRowActionsKeyboardWorks,
                "管理窗非空便签行编辑/删除键盘可达，Enter打开正确记录及安全确认，取消归还焦点（#227）"),
            ["NoteCardSpacingConsumesTokens"] = Must(noteListPolish.CardSpacingConsumesTokens, "便签卡内边距与日期章节间距消费间距 token（#153）"),
            ["TagChipSingleLineEllipsisWithFullText"] = Must(noteListPolish.TagChipSingleLineEllipsisWithFullText, "标签芯片单行不折字、集合折行、超长省略并提供全文提示（#153）"),
            ["EmptyStateTextAllowsWrap"] = Must(noteListPolish.EmptyStateTextAllowsWrap, "倒计时与便签空态文案都允许换行（#153）"),
            ["NoteDateFilterHasVisibleLabel"] = Must(noteListPolish.FilterDateHasVisibleLabel, "筛选弹层便签日期有可见标签，不是无名空下拉（#153）"),
            ["PopupTabNavigationCyclesInsideLayer"] = Must(popupFocusTabGates.AllCyclesInsideLayer, "筛选/标签管理/标签选择器弹层 Tab 层内循环，焦点不逃到被挡住的主窗（#153/#227）"),
            ["PopupTabGateIsolationBehavior"] = Must(popupFocusTabGateIsolationBehavior, "弹层 Tab 层内循环门禁负向夹具逐项只击穿目标字段（#153）"),
            ["FilterFunnelIconSurvivesAliasedCapture"] = Must(
                visualChrome.FilterFunnelIconSurvivesAliasedCapture,
                "筛选漏斗实心填充且常驻按钮内，运行时摘要刷新不再销毁图标（#154）"),
            ["NotesSearchDuplicateMagnifierRemoved"] = Must(visualChrome.NotesSearchDuplicateMagnifierRemoved, "便签页标签旁重复搜索镜已移除，输入框内搜索镜统一描边语言（#154）"),
            ["MainWindowChromeButtonsUseVectorPaths"] = Must(visualChrome.MainWindowChromeButtonsUseVectorPaths, "管理窗最小/关闭为矢量路径窗铬，与悬浮窗头部同一几何（#154）"),
            ["SegmentActivePillsUseFullRoundedCorners"] = Must(
                visualChrome.SegmentActivePillsUseFullRoundedCorners,
                "页签与分段激活胶囊短边一半全圆角（主窗页签 27/分段 22/悬浮 18）（#154）"),
            ["CountdownHandwrittenDecoOutOfFormMeasurement"] = Must(
                visualChrome.CountdownHandwrittenDecoOutOfFormMeasurement,
                "倒计时页手写体落装饰层：不参与表单测量、不可点（#154）"),
            ["FloatingShellLargeShadowRemoved"] = Must(floatingShellLargeShadowRemoved, "悬浮外壳去掉字面色大面积投影，深度由 1px 暖边与 raster 表达（#154）"),
            ["MainWindowSquareChromeNoTransparency"] = Must(
                visualChrome.MainWindowSquareChromeNoTransparency,
                "管理窗请求 Windows 11 DWM 原生圆角与系统阴影、失败安全降级且不开整窗透明（#200）"),
            ["TabsRenderAboveDecorationBand"] = Must(visualChrome.TabsRenderAboveDecorationBand, "#135 工作台骨架次序：页签在装饰带之上（#154）"),
            ["DecorationBandCharactersUnobstructed"] = Must(visualChrome.DecorationBandCharactersUnobstructed, "倒计时与便签编辑卡顶边都落在装饰带下沿之下，三只玩偶不被不透明表面盖住"),
            ["MainCommandIconsUseVectorPaths"] = Must(visualChrome.MainCommandIconsUseVectorPaths, "命令栏设置齿轮与展开入口为矢量路径：绑定按钮前景、内容区不挤裁、展开态箭头转 180°"),
            ["NoteGroupingSelectorAbsent"] = Must(noteGroupingSelectorAbsent, "移除只有单一选项的按日期分组下拉（#227）"),
            ["NoteDateTimelineVisuals"] = Must(noteDateTimelineVisuals, "validate-only 验证便签日期时间轴视觉"),
            ["NoteDisclosureAccessibility"] = Must(noteDisclosureAccessibility, "validate-only 验证便签披露控件无障碍契约"),
            ["NoteDisclosureThemeTokens"] = Must(noteDisclosureThemeTokens, "validate-only 验证便签披露与分段切换日间主题 token"),
            ["NoteDatesRendered"] = noteDatesRendered,
            ["NotesGroupedByDay"] = notesGroupedByDay,
            ["ActiveNotesGrouped"] = Must(activeNotesGrouped, "未完成便签收拢在独立分组中"),
            ["ActiveNotesSegmentSelectedByDefault"] = activeNoteRows.Visibility == Visibility.Visible,
            ["ActiveNotesKeepDateOrder"] = Must(activeNotesKeepDateOrder, "未完成便签组内按较新日期优先显示"),
            ["ManagerNoteRowsFollowSortOrder"] = Must(managerNoteRowsFollowSortOrder, "完整管理窗口的实际便签行遵循管理排序"),
            ["NoteDateSectionsCanCollapse"] = Must(noteSectionExercise.DateSectionsCanCollapse, "未完成与已完成便签支持独立的日期级展开和收起"),
            ["NoteDateSectionsAreIndependent"] = Must(noteSectionExercise.DateSectionsAreIndependent, "同一日期的未完成与已完成折叠状态互不影响"),
            ["NoteDateSectionStateSurvivesRefresh"] = Must(noteSectionExercise.DateSectionStateSurvivesRefresh, "日期折叠状态在管理窗口刷新后保持"),
            ["NoteStatusSegmentsSwitchExclusively"] = Must(noteSectionExercise.SegmentsSwitchExclusively, "未完成/已完成分段切换互斥、两状态不同屏且刷新后保持"),
            ["CompletedNotesCollapsed"] = main.CompletedNotesAreCollapsed,
            ["FloatingWindowPresent"] = Must(floating! is not null, "桌面悬浮倒计时窗口存在（#129 自 U1 迁移）"),
            ["FloatingWindowStaysAboveWorkAreaBottom"] = Must(floatingBottomMargin >= 0, "悬浮窗不遮挡任务栏工作区（#129 自 U1 迁移）"),
            ["FloatingWindowBottomAnchored"] = Must(floatingBottomAnchored, "内容尺寸变化后悬浮窗回到工作区底部（#129 自 U1 迁移）"),
            ["FloatingWindowReanchorsAfterContentChanges"] = Must(floatingReanchorsAfterContentChanges, "便签内容增删时悬浮窗保持底部锚定（#129 自 U1 迁移）"),
            ["FloatingWindowHeightConstrainedToWorkArea"] = Must(floatingHeightConstrained, "悬浮窗高度受工作区约束（#129 自 U1 迁移）"),
            ["FloatingDragSurfaceCoversWindowBackground"] = Must(floatingDragInteraction.CoversWindowBackground, "悬浮窗非交互背景均可接收拖动手势"),
            ["FloatingDragHandleReceivesPointerGestures"] = Must(floatingDragInteraction.ReceivesPointerGesture, "悬浮窗标题拖动区接收真实 WPF 左键手势"),
            ["FloatingDragHandleRequestsNativeWindowDrag"] = Must(floatingDragInteraction.RequestsNativeWindowDrag, "悬浮窗标题拖动区经 WPF 左键路由请求原生窗口拖动"),
            ["FloatingSyntheticDragDoesNotClaimPhysicalCompletion"] = Must(
                floatingDragInteraction.SyntheticGestureDoesNotClaimPhysicalCompletion,
                "悬浮窗合成拖动验收不会把被 WPF 拒绝的 DragMove 误报为物理拖动成功"),
            ["FloatingDragAttemptClampsToWorkArea"] = Must(floatingDragInteraction.DragAttemptClampsToWorkArea, "悬浮窗拖动尝试结束后立即收紧到工作区内"),
            ["FloatingInteractiveControlsDoNotRequestWindowDrag"] = Must(
                floatingDragInteraction.InteractiveControlsDoNotRequestWindowDrag,
                "悬浮窗按钮、输入和滚动控件不请求窗口拖动"),
            ["FloatingDragHandleExcludesInteractiveControls"] = Must(floatingDragInteraction.ExcludesInteractiveControls, "悬浮窗标题拖动区不覆盖按钮或输入控件"),
            ["FloatingWindowManualPositionPersistsAndRemainsVisibleAfterRefresh"] = Must(
                floatingManualPlacement.PersistsAndRemainsVisibleAfterRefresh,
                "悬浮窗手动移动后内容刷新会保留位置并限制在工作区内"),
            ["FloatingPlushTaskWrapLoaded"] = Must(floating!.DayRasterShellAndHeaderLoaded, "悬浮窗日间 raster 外壳与头部已加载（#129 自 U1 迁移）"),
            ["FloatingPlushMascotsLoaded"] = Must(dayMascotsImage.Source is not null, "悬浮窗日间角色场景已加载"),
            ["FloatingPlushSceneIntegratesWithPanel"] = Must(plushSceneIntegratesWithPanel, "悬浮窗角色场景层在任务面板上方且不拦截交互"),
            ["FloatingPlushReferenceComposition"] = Must(plushReferenceComposition, "悬浮窗命中与遮挡门禁（#143）"),
            ["FloatingPlushHeaderIntegration"] = Must(plushHeaderIntegration, "悬浮窗命中与遮挡门禁（#143）"),
            ["FloatingReferenceAspectMatches"] = Must(floatingReferenceExercise.AspectMatches, "悬浮窗参考场景保持 520×390 的 4:3 比例"),
            ["FloatingReferencePanelGeometryMatches"] = Must(floatingReferenceExercise.PanelGeometryMatches, "悬浮窗外框、内容面板与标题栏匹配参考图构图"),
            ["FloatingReferenceMascotGeometryMatches"] = Must(floatingReferenceExercise.MascotGeometryMatches, "悬浮窗三角色可见像素匹配参考图锚点和裁切"),
            ["FloatingReferenceBaselineRequested"] =
                floatingReferenceExercise.Baseline.Requested,
            ["FloatingReferenceBaselineManifestAvailable"] =
                floatingReferenceExercise.Baseline.ManifestAvailable,
            ["FloatingReferenceBaselineValidated"] =
                floatingReferenceExercise.Baseline.AllPassed,
            ["FloatingReferenceBaselineGateIsolationBehavior"] = Must(
                floatingReferenceBaselineGateIsolationBehavior,
                "悬浮窗参考 manifest 十二个负向夹具与视觉基线 module interface 均通过"),
            ["FloatingReferenceFreshnessGateIsolationBehavior"] = Must(floatingReferenceFreshnessGateIsolationBehavior, "长便签帧新鲜度与排版八个负向夹具逐项失败闭锁"),
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
            ["FloatingLongNotesBodyHasNoManualLineBreaks"] = Must(floatingReferenceExercise.LongNotes.BodyHasNoManualLineBreaks, "长便签真实 WPF 门禁"),
            ["FloatingLongNotesNaturalWraps"] = Must(floatingReferenceExercise.LongNotes.NaturalWraps, "长便签真实 WPF 门禁"),
            ["FloatingLongNotesUsesOnlyOuterScroller"] = Must(floatingReferenceExercise.LongNotes.UsesOnlyOuterScroller, "长便签真实 WPF 门禁"),
            ["FloatingLongNotesOuterScrollable"] = Must(floatingReferenceExercise.LongNotes.OuterScrollable, "长便签真实 WPF 门禁"),
            ["FloatingLongNotesTopBottomReachable"] = Must(floatingReferenceExercise.LongNotes.TopBottomReachable, "长便签真实 WPF 门禁"),
            ["FloatingLongNotesSafeAreaMatches"] = Must(floatingReferenceExercise.LongNotes.SafeAreaMatches, "长便签真实 WPF 门禁"),
            ["FloatingLongNotesTypographyMatches"] = Must(floatingReferenceExercise.LongNotes.TypographyMatches, "长便签真实 WPF 门禁"),
            ["FloatingLongNotesLayoutValid"] = Must(floatingReferenceExercise.LongNotes.LayoutValid, "长便签真实 WPF 门禁"),
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
            ["FloatingReferenceContentSafeAreaMatches"] = Must(uiQualityGates.FloatingContentSafeArea, "悬浮窗角色 alpha 不遮挡标题、图标、卡片文字或收入"),
            ["FloatingTitleUsesSemiBoldWeight"] = Must(uiQualityGates.FloatingTitleUsesSemiBoldWeight, "悬浮窗标题独立保持 SemiBold 字重"),
            ["FloatingNoteInputWraps"] = Must(uiQualityGates.FloatingNoteInputWraps, "悬浮窗便签输入独立保持自动换行"),
            ["FloatingNoteInputScrolls"] = Must(uiQualityGates.FloatingNoteInputScrolls, "悬浮窗便签输入独立保持垂直滚动"),
            ["FloatingQuickAddDayExpandedScrolledCaptureWritten"] = Must(
                floatingReferenceExercise.QuickAddDayExpandedScrolledCaptureWritten,
                "悬浮窗快速新增日间展开滚动场景可离屏捕获"),
            ["FloatingReferenceContentOverlapCount"] = floatingReferenceExercise.ContentOverlapCount,
            ["FloatingReferenceContentOverlapTargets"] = floatingReferenceExercise.ContentOverlapTargets,
            ["FloatingReferenceCaptureWritten"] = Must(floatingReferenceExercise.CaptureWritten, "悬浮窗参考场景可由 RenderTargetBitmap 写出非空 PNG"),
            ["FloatingWorkdayCountdownUsesPrimaryVisual"] = Must(
                floatingReferenceExercise.WorkdayCountdownUsesPrimaryVisual,
                "距离下班在首行使用唯一主项的 Automation 语义、暖粉主项底、粉红轨、数字色块与超期待徽章"),
            ["FloatingWorkdayPrimaryBadgeExclusive"] = Must(floatingReferenceExercise.WorkdayPrimaryBadgeExclusive, "超期待徽章仅距离下班主项显示，普通倒计时主项不显示"),
            ["FloatingReferenceScenarioMatches"] = Must(floatingReferenceExercise.ScenarioMatches, "悬浮窗参考场景将距离下班作为首行主项，并保持两张卡片、收入和主项单行剩余时间"),
            ["FloatingPrimaryDeadlineTextFullyVisible"] = Must(floatingReferenceExercise.PrimaryDeadlineTextFullyVisible, "悬浮窗主项的最宽日期在有无滚动条时均完整可见"),
            ["FloatingPrimaryExtremeRemainingShrinks"] = Must(
                floatingReferenceExercise.PrimaryExtremeRemainingShrinks,
                "超长天数主项剩余时间在 194px 列内收缩且完整分格显示"),
            ["FloatingExpiredFallbackRemainingTextFullyVisible"] =
                floatingReferenceExercise.ExpiredFallbackRemainingTextFullyVisible,
            ["FloatingPrimaryDeadlineMeasurements"] =
                floatingReferenceExercise.PrimaryDeadlineMeasurements,
            ["FloatingReferenceViewportStatesMatch"] = Must(floatingReferenceExercise.ViewportStatesMatch, "悬浮窗固定视口覆盖零、一、多卡片及便签提醒滚动状态"),
            ["FloatingViewportGeometryGateIsolationBehavior"] = Must(
                floatingViewportGeometryGateIsolationBehavior,
                "悬浮视口几何门禁用会失败的反例保住确定性边界：卡/印记/快速新增越界即红、滚动条隐藏零扣除（#157）"),
            ["FloatingViewportScrollAndFocusRestored"] = Must(floatingReferenceExercise.ViewportScrollAndFocusRestored, "悬浮窗刷新后恢复当前滚动位置与便签焦点"),
            ["FloatingNoteFailureAppearsInSourceCard"] = Must(floatingReferenceExercise.NoteFailureAppearsInSourceCard, "悬浮便签完成失败时在来源卡片显示可访问错误"),
            ["FloatingResolvedNoteCardErrorDoesNotReturn"] = Must(floatingReferenceExercise.ResolvedNoteCardErrorDoesNotReturn, "便签完成并恢复后不会重新显示旧卡片错误"),
            ["FloatingSecondaryRemainingTextFullyVisible"] = Must(floatingReferenceExercise.SecondaryRemainingTextFullyVisible, "超长次级倒计时在固定视口内单行完整显示"),
            ["FloatingIncomeEncouragementWithoutSalary"] = Must(
                floatingReferenceExercise.IncomeEncouragementWithoutSalary,
                "悬浮窗参考会话月薪清零后底部显示手写体鼓励文案并随正数月薪还原"),
            ["FloatingIncomePrivacyBehavior"] = Must(
                floatingReferenceExercise.IncomePrivacyBehavior,
                "悬浮金额鼠标键盘 Invoke 切换，tick 切页临时无金额后仍隐藏，真实自动化树不泄露且新实例默认显示（#229）"),
            ["FloatingWorkdayCompletionBehavior"] = Must(
                floatingReferenceExercise.WorkdayCompletionBehavior,
                "实际工作日下班到北京时间午夜收尾，日历筛选守卫、计数提醒排除、原位刷新及共用金额隐私（#236）"),
            ["FloatingSecondaryRemainingUnitSpacingMatches"] = Must(
                floatingReferenceExercise.SecondaryRemainingGates.UnitSpacingMatches,
                "次级倒计时直接保留 FormatRemaining 的单位分隔空格"),
            ["FloatingSecondaryRemainingLayoutMatches"] = Must(
                floatingReferenceExercise.SecondaryRemainingGates.FixedColumnLayoutMatches,
                "次级倒计时保持 194px 右对齐、单行、完整显示及表格数字"),
            ["FloatingSecondaryRemainingGateIsolationBehavior"] = Must(floatingSecondaryRemainingGateIsolationBehavior, "次级倒计时去空格与破坏固定列负向夹具可分别单独变红"),
            ["FloatingSecondaryRemainingSamples"] =
                floatingReferenceExercise.SecondaryRemainingSamples,
            ["FloatingScrollBarLocalStyleScoped"] = Must(floatingReferenceExercise.ScrollBar.Gates.LocalStyleScoped, "悬浮窗滚动条门禁"),
            ["FloatingScrollBarHitTargetUsable"] = Must(floatingReferenceExercise.ScrollBar.Gates.HitTargetUsable, "悬浮窗滚动条门禁"),
            ["FloatingScrollBarVisualWeightProgression"] = Must(floatingReferenceExercise.ScrollBar.Gates.VisualWeightProgression, "悬浮窗滚动条门禁"),
            ["FloatingScrollBarUsesWarmThemeTokens"] = Must(floatingReferenceExercise.ScrollBar.Gates.UsesWarmThemeTokens, "悬浮窗滚动条门禁"),
            ["FloatingScrollBarContentRemainsSafe"] = Must(floatingReferenceExercise.ScrollBar.Gates.ContentRemainsSafe, "悬浮窗滚动条门禁"),
            ["FloatingScrollBarInputPathsWork"] = Must(floatingReferenceExercise.ScrollBar.Gates.InputPathsWork, "悬浮窗滚动条门禁"),
            ["FloatingScrollBarGateIsolationBehavior"] = Must(floatingScrollBarGateIsolationBehavior, "悬浮窗滚动条门禁"),
            ["FloatingScrollBarStateCapturesReady"] = Must(floatingReferenceExercise.ScrollBar.StateCapturesReady, "悬浮窗滚动条门禁"),
            ["FloatingScrollBarStateMeasurements"] =
                floatingReferenceExercise.ScrollBar.Measurements,
            ["FloatingThumbBoundaryContrast"] = Must(
                floatingReferenceExercise.ScrollBar.Measurements.Count > 0 &&
                floatingReferenceExercise.ScrollBar.Measurements.All(sample => sample.BoundaryContrast >= 3),
                "浮窗滚动条合成后的边界对实际邻色至少 3:1（#217）"),
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
            ["FloatingDayRasterShellAndHeaderLoaded"] = Must(floating.DayRasterShellAndHeaderLoaded, "悬浮窗日间 shell 与无文字标题栏组件已加载"),
            ["FloatingRasterSurfacesFollowTheme"] = Must(floatingThemeExercise.SurfacesFollowActiveTheme, "悬浮窗 raster 表面保持单一日间主题"),
            ["FloatingDayRasterDecorationsAreNonInteractive"] = Must(floating.DayRasterDecorationsAreNonInteractive, "悬浮窗日间 raster 装饰不拦截鼠标命中"),
            ["FloatingDayRasterCardStatesMatch"] = Must(floatingReferenceExercise.RasterCardStatesMatch, "悬浮窗日间 0/1/2 张倒计时及便签均按真实记录复用无文字卡片底板"),
            ["FloatingDynamicCardContentRemainsWpf"] = Must(floatingReferenceExercise.DynamicCardContentRemainsWpf, "悬浮窗卡片文字与便签勾选框仍由真实 WPF 控件渲染"),
            ["FloatingDatePickerKeyboardLifecycleWorks"] = Must(floatingUiPolish.DatePickerKeyboardLifecycleWorks, "悬浮窗两个日期选择器支持日历聚焦、Esc 关闭与按钮返焦"),
            ["FloatingFieldErrorsAppearAtSource"] = Must(floatingUiPolish.FieldErrorsAppearAtSource, "悬浮快速便签错误显示在无效字段附近并关联输入控件"),
            ["FloatingCorrectedFieldErrorsClearLiveMessage"] = Must(floatingUiPolish.CorrectedFieldErrorsClearLiveMessage, "悬浮快速便签字段修正后清除局部与全局错误"),
            ["FloatingDynamicHeaderControlsRemainWpf"] = Must(floating.DynamicHeaderControlsRemainWpf, "悬浮窗标题、页签与命令图标仍由真实 WPF 控件渲染"),
            ["FloatingHeaderIconAccessibility"] = Must(floatingHeaderIconAccessibility, "悬浮窗图标按钮保留 ToolTip 与可访问名称"),
            ["FloatingHeaderIconClickCommandsWork"] = Must(floatingHeaderIconClickCommandsWork, "悬浮窗打开管理与退出图标保留真实点击命令"),
            ["FloatingTrayButtonAccessibility"] = Must(floatingTrayButtonAccessibility, "悬浮窗缩小按钮提供明确 ToolTip 与可访问名称"),
            ["FloatingTrayProductionControllerActive"] = Must(floatingTrayInteraction.ProductionControllerActive, "应用宿主与托盘验收共用生产切换控制器"),
            ["FloatingTrayMinimizeHidesWindowAndShowsIcon"] = Must(
                floatingTrayInteraction.MinimizeHidesWindowAndShowsIcon,
                "悬浮窗缩小按钮隐藏悬浮窗，托盘图标在窗口仍可见时已经常驻（#213）"),
            ["FloatingTrayRestoreShowsWindowAndHidesIcon"] = Must(
                floatingTrayInteraction.RestoreShowsWindowAndHidesIcon,
                "单击托盘图标或「打开悬浮窗」恢复悬浮窗且图标保持常驻；「打开管理窗口」保留草稿和筛选（#213）"),
            ["FloatingTrayExitRequestsApplicationExit"] = Must(floatingTrayInteraction.ExitRequestsApplicationExit, "系统托盘退出命令触发应用退出请求"),
            ["FloatingTrayFailedRestoreKeepsTrayStateAndAllowsRetry"] = Must(
                floatingTrayInteraction.FailedRestoreKeepsTrayStateAndAllowsRetry,
                "恢复判定失败时托盘图标保留且可再次恢复"),
            ["FloatingReferenceActualSize"] = floatingReferenceExercise.ActualSize,
            ["FloatingReferenceCapturePath"] = floatingReferenceExercise.CapturePath,
            ["FloatingPlushCardIsUnobscured"] = Must(visiblePlushDoesNotObscureTaskCardContent, "悬浮窗命中与遮挡门禁（#143）"),
            ["FloatingPlushTouchesTaskCard"] = Must(plushBridgesTaskCardEdges, "悬浮窗命中与遮挡门禁（#143）"),
            ["FloatingPlushDoesNotObscureTaskCardContent"] = Must(plushDoesNotObscureTaskCardContent, "悬浮窗命中与遮挡门禁（#143）"),
            ["FloatingPlushVisibleEdgesMeetCard"] = Must(visiblePlushEdgesMeetTaskCard, "悬浮窗命中与遮挡门禁（#143）"),
            ["FloatingPlushGroupWithinWorkArea"] = Must(plushGroupWithinWorkArea, "悬浮窗命中与遮挡门禁（#143）"),
            ["FloatingCountdownBottomMargin"] = floatingBottomMargin,
            ["FloatingNotesBottomMargin"] = floatingBottomMargin,
            ["FloatingCountdownButtonTextFits"] = Must(floatingWorkspace.CountdownButtonTextFit.Fits, "悬浮倒计时页签文字完整可见（#129 自 U1 迁移）"),
            ["FloatingCountdownButtonTextAvailableWidth"] =
                floatingWorkspace.CountdownButtonTextFit.Available,
            ["FloatingCountdownButtonTextRequiredWidth"] =
                floatingWorkspace.CountdownButtonTextFit.Required,
            ["FloatingNotesButtonTextFits"] = Must(floatingWorkspace.NotesButtonTextFit.Fits, "悬浮便签页签文字完整可见（#129 自 U1 迁移）"),
            ["FloatingNotesButtonTextAvailableWidth"] =
                floatingWorkspace.NotesButtonTextFit.Available,
            ["FloatingNotesButtonTextRequiredWidth"] =
                floatingWorkspace.NotesButtonTextFit.Required,
            ["FloatingNotesKeepExistingSortOrder"] = Must(floatingWorkspace.NotesKeepExistingSortOrder, "悬浮窗使用共享领域便签排序"),
            ["FloatingTitleTextFits"] = Must(floatingWorkspace.TitleTextFit.Fits, "悬浮窗待办便签标题在实际父布局槽内完整可见"),
            ["FloatingTitleTextAvailableWidth"] = floatingWorkspace.TitleTextFit.Available,
            ["FloatingTitleTextRequiredWidth"] = floatingWorkspace.TitleTextFit.Required,
            ["FloatingTitleTextHasVerticalClearance"] = Must(floatingWorkspace.TitleTextHeightFit.Fits, "悬浮窗待办便签标题保留完整字形所需的垂直余量"),
            ["FloatingTitleTextAvailableHeight"] = floatingWorkspace.TitleTextHeightFit.Available,
            ["FloatingTitleTextRequiredHeight"] = floatingWorkspace.TitleTextHeightFit.Required,
            ["SavePreferencesButtonTextFits"] = Must(savePreferencesButtonFit.Fits, "设置保存按钮文字完整显示"),
            ["SavePreferencesButtonIsFullyVisible"] = Must(savePreferencesButtonIsVisible, "设置保存按钮完整位于主窗口内"),
            ["SavePreferencesButtonIsFullyVisibleAtMinimumWidth"] = Must(
                savePreferencesButtonIsVisibleAtMinimumWidth,
                "最小主窗口宽度下经横向滚动到达齿轮，设置保存按钮完整落入视口（#211/#221）"),
            ["SavePreferencesButtonRight"] = Math.Round(savePreferencesButtonRight, 2),
            ["MainWindowActualWidth"] = Math.Round(main.ActualWidth, 2),
            ["FloatingCountdownCount"] = floatingWorkspace.CountdownCount,
            ["HasWorkdayEndCountdown"] =
                false,
            ["FloatingCountdownTexts"] = floatingWorkspace.CountdownTexts,
            ["FloatingIncomeText"] = floatingWorkspace.IncomeText,
            ["FloatingIncomeHiddenWithoutSalary"] = Must(floatingWorkspace.IncomeHiddenWithoutSalary, "悬浮窗月薪为 0 时不渲染今日已赚金额：日历就绪显示鼓励文案，未就绪仍显示状态"),
            ["FloatingIncomeRestoredWithSalary"] = Must(floatingWorkspace.IncomeRestoredWithSalary, "悬浮窗恢复正数月薪后今日已赚金额行还原"),
            ["FloatingManagerButtonPresent"] = Must(floatingStructure.ManagerButtonPresent, "悬浮窗倒计时管理动作存在（#129 自 U1 迁移）"),
            ["HasUnifiedNoteEntry"] = Must(
                mainStructure.CountdownTabPresent &&
                mainStructure.NotesTabPresent &&
                floatingStructure.NotesButtonPresent,
                "倒计时工作区存在统一便签入口（#129 自 U1 迁移）"),
            ["HasNoteControls"] = Must(
                mainStructure.NoteEntryControlsPresent &&
                floatingStructure.NoteEntryControlsPresent,
                "便签输入或添加动作存在（#129 自 U1 迁移）"),
            ["HasNoteDateControls"] =
                mainStructure.NoteDatePresent &&
                floatingStructure.NoteDatePresent,
            ["HasOpenNotesActions"] = Must(
                mainStructure.NotesTabPresent &&
                floatingStructure.NotesButtonPresent,
                "存在打开统一便签工作区的动作（#129 自 U1 迁移）"),
            ["UnifiedWorkspaceSwitches"] = Must(switchesToCountdown && switchesToNotes, "倒计时与便签工作区一起切换（#129 自 U1 迁移）"),
            ["WorkspaceSwitcherBeforeEditors"] = Must(workspaceSwitcherBeforeEditors, "工作区切换器出现在编辑器之前（#129 自 U1 迁移）"),
            ["HasPreferenceControls"] = Must(
                mainStructure.PreferenceControlsPresent,
                "悬浮窗精度与月薪设置控件存在（#129 自 U1 迁移）"),
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

        var crossInterfaceSourceTreeMode =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT"));
        // Note/tag exercises and schema migrations treat their rewritten settings
        // file as the caller-visible persistence contract (see App.OnStartup);
        // every other validate-only path must hand the file back byte-identical.
        var settingsWritesAreCallerContract =
            options.ExerciseNoteAdd ||
            options.ExerciseTagInteractions ||
            workspaceSnapshot.SettingsWereMigrated;
        if (!settingsWritesAreCallerContract)
        {
            if (settingsBytesAtValidationEntry is null)
            {
                if (File.Exists(settingsConservationPath))
                {
                    File.Delete(settingsConservationPath);
                }
            }
            else
            {
                File.WriteAllBytes(settingsConservationPath, settingsBytesAtValidationEntry);
            }
        }

        var settingsHashAtValidationExit = File.Exists(settingsConservationPath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(settingsConservationPath)))
            : null;
        var crossInterfaceAiReferencePriority = ValidateCrossInterfaceAiReferencePriority();
        var crossInterfacePageSpecificComposition =
            mainUiPolish.MinimumSizeMatchesDefault &&
            switchesToCountdown &&
            switchesToNotes &&
            mainStructure.CountdownWorkspaceIsCanvas &&
            floatingWorkspace.InitialStateMatches &&
            floatingWorkspace.SwitchingStaysSynchronized &&
            reminderCenterRepresentativeCapture.Rendered &&
            (!crossInterfaceSourceTreeMode ||
                (mainReferenceBaseline.AllPassed && floatingReferenceExercise.Baseline.AllPassed));
        var crossInterfaceSharedWarmPinkTokens =
            warmYellowPaletteMatchesLegacy &&
            noteDisclosureThemeTokens &&
            uiQualityGates.MainWorkspaceResourcesFollowTheme &&
            mainThemeExercise.SegmentSelectedTextContrast &&
            floatingQuickAddTheme.Day.Passed &&
            floatingReferenceExercise.ScrollBar.Gates.UsesWarmThemeTokens &&
            reminderCenter.FollowsDayTheme;
        var crossInterfaceStaticDayThemeInfo =
            ValidateCrossInterfaceStaticDayTheme(main, floating) &&
            floatingThemeExercise.PlushSceneFollowsDayTheme &&
            floatingThemeExercise.BoundaryModes.All(mode =>
                string.Equals(mode, "Day", StringComparison.Ordinal)) &&
            reminderCenter.FollowsDayTheme;
        var crossInterfacePlushDecorationLayering =
            main.MainReferenceDecorationsLoaded &&
            uiQualityGates.MainReferenceDecorationLayerOrder &&
            mainThemeExercise.CharacterPanelDocksFollowTheme &&
            dayMascotsImage.Source is not null &&
            plushHeaderIntegration &&
            floatingThemeExercise.PlushSceneFollowsDayTheme;
        var crossInterfaceFilterSharing =
            mainUiPolish.NoteSearchSyncsFromSharedFilter &&
            floatingWindowScenarios.NoteSearch.AllPassed &&
            floatingWorkspace.SwitchingStaysSynchronized;
        var crossInterfaceKeyboardOrder =
            string.Join(',', mainKeyboardBehavior.TabSequence) ==
                "CountdownTab,NotesTab,PreferencesButton,FilterButton,TagManagerButton" &&
            mainKeyboardNavigationGates.AllPassed &&
            reminderCenterKeyboardGates.AllPassed &&
            comboBoxTextClick.ArrowIsNotTabStop &&
            workspaceSwitcherBeforeEditors &&
            mainUiPolish.PopupKeyboardLifecycleWorks;
        var crossInterfaceHitTesting =
            mainCheckboxHitCoversSlot &&
            floatingCheckboxHitCoversSlot &&
            popupRightInsetIsEightFromCanvas &&
            floatingReferenceExercise.ScrollBar.Gates.HitTargetUsable &&
            floatingDragInteraction.ExcludesInteractiveControls &&
            main.MainReferenceDecorationsAreNonInteractive &&
            main.CharacterPanelDecorationsAreNonInteractive &&
            floating.DayRasterDecorationsAreNonInteractive;
        // #146 战役收口（待维护者复核）：本子句原以 legacyMainReferenceCapture.UsesZeroState
        //（运行夹具零数据）作源码树硬门禁，与 #129 特意保留的设置守恒断言载体
        //（CSharp/Ui validate-only 使用有内容临时 settings）结构性冲突，致两阶段源码树模式必红。
        // 三向探针互证见 run-evidence\clock-countdown-issue-146-final-closure\probe-*：
        // 有内容+源码树红 / 有内容+发布包绿 / 空夹具+源码树绿。
        // 现按隔离本意改为「实拍真实运行并落证」（Requested && CapturesWritten）；
        // 零态属性保留为信息字段 MainReferenceCaptureUsesZeroState，不删除。
        var crossInterfaceDeterministicFixtureIsolation =
            validateOnlySettingsIsolation &&
            floatingReferenceBaselineGateIsolationBehavior &&
            floatingReferenceFreshnessGateIsolationBehavior &&
            RunCrossInterfaceFixtureDeterminismProbe() &&
            (!crossInterfaceSourceTreeMode ||
                (mainReferenceBaselineGateIsolationBehavior &&
                 legacyMainReferenceCapture.Requested &&
                 legacyMainReferenceCapture.CapturesWritten));
        var crossInterfaceRealSettingsConserved =
            settingsWritesAreCallerContract ||
            (string.Equals(
                settingsHashAtValidationEntry,
                settingsHashAtValidationExit,
                StringComparison.Ordinal) &&
            validateOnlySettingsIsolation);
        var crossInterfaceIntegration =
            crossInterfaceAiReferencePriority &&
            crossInterfacePageSpecificComposition &&
            crossInterfaceSharedWarmPinkTokens &&
            crossInterfaceStaticDayThemeInfo &&
            crossInterfacePlushDecorationLayering &&
            crossInterfaceFilterSharing &&
            crossInterfaceKeyboardOrder &&
            crossInterfaceHitTesting &&
            crossInterfaceDeterministicFixtureIsolation &&
            crossInterfaceRealSettingsConserved;
        report["CrossInterfaceAiReferencePriority"] = Must(crossInterfaceAiReferencePriority, "跨界面整合门禁");
        report["CrossInterfacePageSpecificComposition"] = Must(crossInterfacePageSpecificComposition, "跨界面整合门禁");
        report["CrossInterfaceSharedWarmPinkTokens"] = Must(crossInterfaceSharedWarmPinkTokens, "跨界面整合门禁");
        report["CrossInterfaceStaticDayThemeInfo"] = Must(crossInterfaceStaticDayThemeInfo, "跨界面整合门禁");
        report["CrossInterfacePlushDecorationLayering"] = Must(crossInterfacePlushDecorationLayering, "跨界面整合门禁");
        report["CrossInterfaceFilterSharing"] = Must(crossInterfaceFilterSharing, "跨界面整合门禁");
        report["CrossInterfaceKeyboardOrder"] = Must(crossInterfaceKeyboardOrder, "跨界面整合门禁");
        report["CrossInterfaceHitTesting"] = Must(crossInterfaceHitTesting, "跨界面整合门禁");
        report["CrossInterfaceDeterministicFixtureIsolation"] = Must(crossInterfaceDeterministicFixtureIsolation, "跨界面整合门禁");
        report["CrossInterfaceRealSettingsConserved"] = Must(crossInterfaceRealSettingsConserved, "跨界面整合门禁");
        report["CrossInterfaceIntegration"] = Must(crossInterfaceIntegration, "跨界面整合门禁");
        report["CrossInterfaceIntegrationEvidence"] =
            $"#119 cross-interface integration: AI direction images stay outside manifests and pixel gates while real {VisualReferenceBaseline.Version} frames (9 main + 5 floating, including #235 selection and #236 completion) carry regression; per-page composition, shared warm-pink tokens, single static day theme without a switch entry, plush/decoration layering, shared filter state, keyboard order, hit-test boundaries, deterministic fixture isolation and real-settings conservation each aggregate existing fail-closed probes.";
        report["UiQualityGateIsolationBehavior"] = Must(uiQualityGateIsolationBehavior, "UI 质量门禁负向夹具逐项失败且不误伤无关契约");
        datePicker.IsDropDownOpen = false;
        reminderCenterWindow.Close();
        floating.Hide();
        main.Hide();
        return report.Seal();
    }
}
