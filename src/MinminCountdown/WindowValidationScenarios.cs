using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace MinminCountdown;

public sealed partial class MainWindow
{
    internal MainWindowValidationScenarios RunValidationScenarios(bool exerciseNoteAdd)
    {
        var noteExercise = exerciseNoteAdd
            ? RunNoteAddExercise()
            : new NoteExerciseResult(false, false, false, false, false);
        var reminderModes = exerciseNoteAdd
            ? RunReminderModeClickExercises()
            : ReminderModeClickResult.NotRun;

        return new MainWindowValidationScenarios(
            noteExercise,
            reminderModes,
            ProbeWorkspaceDraftRetention(),
            ProbeNoteDateBehavior(),
            ProbeCrossDayReminderBehavior(),
            ProbeReminderSummaryAdvancesAfterFirstDue(),
            ProbeUiPolishScenarios());
    }

    private MainUiPolishScenarios ProbeUiPolishScenarios()
    {
        var originalWorkspace = _workspace;
        var originalReminderMode = NoteReminder.SelectedIndex;
        var originalReminderInterval = NoteReminderInterval.SelectedIndex;
        var originalCustomMinutes = CustomReminderMinutes.Text;
        var originalSearch = NoteSearch.Text;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            NoteReminder.SelectedIndex = 2;
            NoteReminderInterval.SelectedIndex = 3;
            CustomReminderMinutes.Text = "45";
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);

            var intervalBounds = ValidationBounds(NoteReminderInterval);
            var customBounds = ValidationBounds(CustomReminderMinutes);
            var scheduleBounds = ValidationBounds(NoteReminderSchedule);
            var messageBounds = ValidationBounds(NoteMessage);
            var editorFieldsDoNotOverlap =
                NoteReminderInterval.IsVisible &&
                CustomReminderMinutes.IsVisible &&
                NoteReminderSchedule.IsVisible &&
                !intervalBounds.IntersectsWith(customBounds) &&
                !intervalBounds.IntersectsWith(scheduleBounds) &&
                !customBounds.IntersectsWith(scheduleBounds) &&
                !intervalBounds.IntersectsWith(messageBounds) &&
                !customBounds.IntersectsWith(messageBounds) &&
                !scheduleBounds.IntersectsWith(messageBounds);
            var editorLabelsVisible = new[]
            {
                NoteTextLabel,
                NoteDateLabel,
                NoteReminderModeLabel,
                NoteTagsLabel,
                NoteSearchLabel
            }.All(label => label.IsVisible && !string.IsNullOrWhiteSpace(label.Text));

            var filterPopup = WindowValidationInput.ExercisePopup(
                FilterPopup,
                FilterButton,
                AnyTagMode,
                NoteSearch);
            var tagManagerPopup = WindowValidationInput.ExercisePopup(
                TagManagerPopup,
                TagManagerButton,
                NewTagInput,
                NoteSearch);
            var tagSelectorPopup = WindowValidationInput.ExercisePopup(
                NoteTags.Popup,
                NoteTags.PopupTrigger,
                NoteTags.PopupInitialFocus,
                NoteSearch);

            var firstUse = NoteEmptyStatePresentation(false);
            var noResults = NoteEmptyStatePresentation(true);
            var emptyStatesDistinguished =
                !string.Equals(firstUse.Message, noResults.Message, StringComparison.Ordinal) &&
                !firstUse.ShowReset &&
                noResults.ShowReset;
            var noResultsResetWorks = true;
            if (_desktopWorkspace.Snapshot.Settings.Notes.Count > 0)
            {
                NoteSearch.Text = $"__validation_no_results_{Guid.NewGuid():N}";
                WindowValidationInput.Pump(Dispatcher);
                noResultsResetWorks =
                    EmptyNotesMessage.Visibility == Visibility.Visible &&
                    string.Equals(EmptyNotesMessage.Text, noResults.Message, StringComparison.Ordinal) &&
                    ResetEmptyNotesFiltersButton.Visibility == Visibility.Visible;
                ResetEmptyNotesFiltersButton.RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
                noResultsResetWorks &=
                    string.IsNullOrEmpty(NoteSearch.Text) &&
                    ResetEmptyNotesFiltersButton.Visibility == Visibility.Collapsed;
            }

            return new MainUiPolishScenarios(
                editorFieldsDoNotOverlap,
                editorLabelsVisible,
                Width >= MinWidth &&
                Height >= MinHeight &&
                Math.Abs(MinWidth - 760) <= 0.1 &&
                Math.Abs(MinHeight - 880) <= 0.1,
                filterPopup.EscapeReturnsFocus &&
                tagManagerPopup.EscapeReturnsFocus &&
                tagSelectorPopup.EscapeReturnsFocus,
                filterPopup.OutsideFocusPreserved &&
                tagManagerPopup.OutsideFocusPreserved &&
                tagSelectorPopup.OutsideFocusPreserved,
                emptyStatesDistinguished,
                noResultsResetWorks);
        }
        finally
        {
            FilterPopup.IsOpen = false;
            TagManagerPopup.IsOpen = false;
            NoteTags.IsPopupOpen = false;
            NoteReminder.SelectedIndex = originalReminderMode;
            NoteReminderInterval.SelectedIndex = originalReminderInterval;
            CustomReminderMinutes.Text = originalCustomMinutes;
            _desktopWorkspace.ApplyFilter(originalFilter);
            AnyTagMode.IsChecked = originalFilter.MatchMode == TagMatchMode.Any;
            AllTagMode.IsChecked = originalFilter.MatchMode == TagMatchMode.All;
            UntaggedOnly.IsChecked = originalFilter.UntaggedOnly;
            CountdownAllFilter.IsChecked = originalFilter.CountdownStatus == CountdownStatusFilter.All;
            CountdownActiveFilter.IsChecked = originalFilter.CountdownStatus == CountdownStatusFilter.Active;
            CountdownExpiredFilter.IsChecked = originalFilter.CountdownStatus == CountdownStatusFilter.Expired;
            NoteAllFilter.IsChecked = originalFilter.NoteStatus == NoteStatusFilter.All;
            NoteActiveFilter.IsChecked = originalFilter.NoteStatus == NoteStatusFilter.Active;
            NoteCompletedFilter.IsChecked = originalFilter.NoteStatus == NoteStatusFilter.Completed;
            NoteUndatedFilter.IsChecked = originalFilter.UndatedNotesOnly;
            NoteDateFilter.SelectedDate = originalFilter.NoteDate is null
                ? null
                : DateTime.ParseExact(
                    originalFilter.NoteDate,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
            NoteSearch.Text = originalSearch;
            _desktopWorkspace.ApplyFilter(originalFilter);
            RenderFilterTags();
            RenderVisibleWorkspace();
            UpdateFilterSummary();
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private Rect ValidationBounds(FrameworkElement element) =>
        element.TransformToAncestor(this).TransformBounds(
            new Rect(new Point(0, 0), element.RenderSize));
}

public sealed partial class FloatingWindow
{
    internal FloatingWindowValidationScenarios RunValidationScenarios(bool exerciseNoteAdd)
    {
        var crossDayReminderAdded =
            exerciseNoteAdd && RunCrossDayReminderAddExercise();
        var reminderModes = exerciseNoteAdd
            ? RunReminderModeClickExercises()
            : ReminderModeClickResult.NotRun;

        return new FloatingWindowValidationScenarios(
            crossDayReminderAdded,
            reminderModes,
            RunDragInteractionExercise(),
            RunThemeBoundaryExercise(),
            RunManualPlacementExercise(),
            ProbeUiPolishScenarios());
    }

    private FloatingUiPolishScenarios ProbeUiPolishScenarios()
    {
        var originalWorkspace = _workspace;
        var originalText = FloatingNoteText.Text;
        var originalDate = FloatingNoteDate.SelectedDate;
        var originalReminderMode = FloatingReminder.SelectedIndex;
        var originalReminderInterval = FloatingReminderInterval.SelectedIndex;
        var originalCustomMinutes = FloatingCustomReminderMinutes.Text;
        var originalReminderDate = FloatingReminderDate.SelectedDate;
        var originalReminderHour = FloatingReminderHour.SelectedIndex;
        var originalReminderMinute = FloatingReminderMinute.SelectedIndex;
        var originalSettingDefaultDate = _settingDefaultDate;
        var originalDateDirty = _dateDirty;
        var originalNotesScrollOffset = _workspaceScrollOffsets.TryGetValue(
            WorkspaceKind.Notes,
            out var savedNotesScrollOffset)
                ? savedNotesScrollOffset
                : 0;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            FloatingReminder.SelectedIndex = 1;
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);
            var datePickerKeyboardLifecycleWorks =
                WindowValidationInput.ExerciseDatePicker(FloatingNoteDate) &&
                WindowValidationInput.ExerciseDatePicker(FloatingReminderDate);

            FloatingReminder.SelectedIndex = 0;
            FloatingNoteText.Text = string.Empty;
            FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var textErrorAtSource =
                FloatingNoteTextError.Visibility == Visibility.Visible &&
                FloatingNoteText.IsKeyboardFocusWithin &&
                string.Equals(
                    FloatingNoteMessage.Text,
                    FloatingNoteTextError.Text,
                    StringComparison.Ordinal) &&
                ValidationErrorIsAccessible(FloatingNoteTextError, FloatingNoteText);
            FloatingNoteText.Text = "验证字段错误恢复";
            WindowValidationInput.Pump(Dispatcher);
            var textErrorCleared =
                FloatingNoteTextError.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(FloatingNoteMessage.Text) &&
                ValidationErrorIsCleared(FloatingNoteTextError, FloatingNoteText);

            FloatingNoteDate.SelectedDate = null;
            FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var dateErrorAtSource =
                FloatingNoteDateError.Visibility == Visibility.Visible &&
                FloatingNoteDate.IsKeyboardFocusWithin &&
                string.Equals(
                    FloatingNoteMessage.Text,
                    FloatingNoteDateError.Text,
                    StringComparison.Ordinal) &&
                ValidationErrorIsAccessible(FloatingNoteDateError, FloatingNoteDate);
            FloatingNoteDate.SelectedDate = _desktopWorkspace.Snapshot.BeijingNow.Date;
            WindowValidationInput.Pump(Dispatcher);
            var dateErrorCleared =
                FloatingNoteDateError.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(FloatingNoteMessage.Text) &&
                ValidationErrorIsCleared(FloatingNoteDateError, FloatingNoteDate);

            FloatingReminder.SelectedIndex = 2;
            FloatingReminderInterval.SelectedIndex = 3;
            FloatingCustomReminderMinutes.Text = "0";
            FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var reminderErrorAtSource =
                FloatingReminderMessage.Visibility == Visibility.Visible &&
                FloatingCustomReminderMinutes.IsKeyboardFocusWithin &&
                string.Equals(
                    FloatingNoteMessage.Text,
                    FloatingReminderMessage.Text,
                    StringComparison.Ordinal) &&
                ValidationErrorIsAccessible(
                    FloatingReminderMessage,
                    FloatingCustomReminderMinutes);
            FloatingCustomReminderMinutes.Text = "45";
            WindowValidationInput.Pump(Dispatcher);
            var reminderErrorCleared =
                FloatingReminderMessage.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(FloatingNoteMessage.Text) &&
                ValidationErrorIsCleared(
                    FloatingReminderMessage,
                    FloatingCustomReminderMinutes);

            return new FloatingUiPolishScenarios(
                datePickerKeyboardLifecycleWorks,
                textErrorAtSource && dateErrorAtSource && reminderErrorAtSource,
                textErrorCleared && dateErrorCleared && reminderErrorCleared);
        }
        finally
        {
            FloatingNoteText.Text = originalText;
            FloatingReminder.SelectedIndex = originalReminderMode;
            FloatingReminderInterval.SelectedIndex = originalReminderInterval;
            FloatingCustomReminderMinutes.Text = originalCustomMinutes;
            FloatingBodyScroller.ScrollToVerticalOffset(originalNotesScrollOffset);
            WindowValidationInput.Pump(Dispatcher);
            ShowWorkspace(originalWorkspace);
            FloatingReminderDate.SelectedDate = originalReminderDate;
            FloatingReminderHour.SelectedIndex = originalReminderHour;
            FloatingReminderMinute.SelectedIndex = originalReminderMinute;
            _settingDefaultDate = true;
            FloatingNoteDate.SelectedDate = originalDate;
            _settingDefaultDate = originalSettingDefaultDate;
            _dateDirty = originalDateDirty;
            ClearNoteFieldErrors();
            SetNoteMessage(string.Empty);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private static bool ValidationErrorIsAccessible(TextBlock field, Control source) =>
        !string.IsNullOrWhiteSpace(field.Text) &&
        string.Equals(AutomationProperties.GetName(field), field.Text, StringComparison.Ordinal) &&
        string.Equals(AutomationProperties.GetHelpText(field), field.Text, StringComparison.Ordinal) &&
        string.Equals(AutomationProperties.GetHelpText(source), field.Text, StringComparison.Ordinal) &&
        AutomationProperties.GetLiveSetting(field) == AutomationLiveSetting.Polite;

    private static bool ValidationErrorIsCleared(TextBlock field, Control source) =>
        string.IsNullOrEmpty(AutomationProperties.GetName(field)) &&
        string.IsNullOrEmpty(AutomationProperties.GetHelpText(field)) &&
        string.IsNullOrEmpty(AutomationProperties.GetHelpText(source));
}

internal sealed record MainWindowValidationScenarios(
    NoteExerciseResult NoteExercise,
    ReminderModeClickResult ReminderModes,
    bool WorkspaceDraftsRetained,
    (bool TracksBeijingDay, bool PreservesChosenDate) NoteDateBehavior,
    bool CrossDayReminderBehavior,
    bool ReminderSummaryAdvances,
    MainUiPolishScenarios UiPolish);

internal sealed record FloatingWindowValidationScenarios(
    bool CrossDayReminderAdded,
    ReminderModeClickResult ReminderModes,
    FloatingDragInteractionExerciseResult DragInteraction,
    FloatingThemeExerciseResult ThemeExercise,
    FloatingManualPlacementExerciseResult ManualPlacement,
    FloatingUiPolishScenarios UiPolish);

internal sealed record MainUiPolishScenarios(
    bool NoteEditorFieldsDoNotOverlap,
    bool NoteEditorLabelsVisible,
    bool MinimumSizeMatchesDefault,
    bool PopupKeyboardLifecycleWorks,
    bool PopupOutsideFocusPreserved,
    bool EmptyStatesDistinguished,
    bool NoResultsResetWorks);

internal sealed record FloatingUiPolishScenarios(
    bool DatePickerKeyboardLifecycleWorks,
    bool FieldErrorsAppearAtSource,
    bool CorrectedFieldErrorsClearLiveMessage);

internal sealed record PopupFocusExerciseResult(
    bool EscapeReturnsFocus,
    bool OutsideFocusPreserved);

internal static class WindowValidationInput
{
    public static void Pump(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(
            DispatcherPriority.SystemIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public static PopupFocusExerciseResult ExercisePopup(
        Popup popup,
        ButtonBase trigger,
        IInputElement initialFocus,
        UIElement outsideFocus)
    {
        var escapeReturnsFocus = false;
        var outsideFocusPreserved = false;
        var popupOpened = false;
        var focusEntered = false;
        var escapeHandled = false;
        var popupClosedAfterEscape = false;
        var triggerFocused = false;
        try
        {
            popup.IsOpen = false;
            Window.GetWindow(trigger)?.Activate();
            Keyboard.Focus(trigger);
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            popupOpened = popup.IsOpen;
            focusEntered = OwnsKeyboardFocus(initialFocus);
            escapeHandled = initialFocus is UIElement input &&
                RaisePreviewKey(input, Key.Escape);
            Pump(popup.Dispatcher);
            popupClosedAfterEscape = !popup.IsOpen;
            triggerFocused = trigger.IsKeyboardFocusWithin;
            escapeReturnsFocus =
                popupOpened &&
                focusEntered &&
                escapeHandled &&
                popupClosedAfterEscape &&
                triggerFocused;

            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            Keyboard.Focus(outsideFocus);
            popup.IsOpen = false;
            Pump(popup.Dispatcher);
            outsideFocusPreserved =
                outsideFocus.IsKeyboardFocusWithin;
        }
        finally
        {
            popup.IsOpen = false;
            Pump(popup.Dispatcher);
        }

        return new PopupFocusExerciseResult(escapeReturnsFocus, outsideFocusPreserved);
    }

    public static bool ExerciseDatePicker(DatePicker picker)
    {
        var owner = Window.GetWindow(picker);
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            picker.IsDropDownOpen = false;
            picker.ApplyTemplate();
            var triggerButton = picker.Template.FindName("PART_Button", picker) as Button;
            owner?.Activate();
            owner?.Focus();
            triggerButton?.Focus();
            Pump(picker.Dispatcher);
            picker.IsDropDownOpen = true;
            Pump(picker.Dispatcher);
            picker.ApplyTemplate();
            var calendar = ThemedDatePickerBehavior.GetCalendar(picker);
            var focusedVisual = Keyboard.FocusedElement as DependencyObject;
            var focusedWithinCalendarVisual =
                calendar is not null &&
                focusedVisual is not null &&
                (ReferenceEquals(focusedVisual, calendar) || calendar.IsAncestorOf(focusedVisual));
            var focusEntered = calendar?.IsKeyboardFocusWithin == true || focusedWithinCalendarVisual;
            var escapeHandled = calendar is not null &&
                RaisePreviewKey(calendar, Key.Escape);
            Pump(picker.Dispatcher);
            picker.ApplyTemplate();
            triggerButton = picker.Template.FindName("PART_Button", picker) as Button;
            var focusReturned = triggerButton?.IsKeyboardFocusWithin == true;
            var works =
                focusEntered &&
                escapeHandled &&
                !picker.IsDropDownOpen &&
                focusReturned &&
                picker.CalendarStyle is not null &&
                ReferenceEquals(calendar?.Style, picker.CalendarStyle);
            return works;
        }
        finally
        {
            picker.IsDropDownOpen = false;
            Pump(picker.Dispatcher);
        }
    }

    public static bool RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target);
        if (source is null)
        {
            return false;
        }

        var eventArgs = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(eventArgs);
        return eventArgs.Handled;
    }

    private static bool OwnsKeyboardFocus(IInputElement target) =>
        target is UIElement element
            ? element.IsKeyboardFocusWithin
            : ReferenceEquals(Keyboard.FocusedElement, target);
}
