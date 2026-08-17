using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

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

            ShowWorkspace(WorkspaceKind.Countdowns);
            UpdateLayout();
            var countdownEmptyStates =
                MainWindow.CountdownEmptyStatePresentation(false);
            var countdownNoResults =
                MainWindow.CountdownEmptyStatePresentation(true);
            var countdownEmptyStatesDistinguished =
                !string.Equals(countdownEmptyStates.Message, countdownNoResults.Message, StringComparison.Ordinal) &&
                !countdownEmptyStates.ShowReset &&
                countdownNoResults.ShowReset;
            var countdownEmptyPointerResetWorks = true;
            var countdownEmptyKeyboardResetWorks = true;
            if (_desktopWorkspace.Snapshot.Settings.Countdowns.Count > 0)
            {
                var savedSearch = NoteSearch.Text;
                SetCountdownFilter(CountdownStatusFilter.Expired);
                WindowValidationInput.Pump(Dispatcher);
                countdownEmptyPointerResetWorks =
                    EmptyCountdownMessage.Visibility == Visibility.Visible &&
                    ResetEmptyCountdownFiltersButton.Visibility == Visibility.Visible &&
                    string.Equals(EmptyCountdownMessage.Text, countdownNoResults.Message, StringComparison.Ordinal);
                ResetEmptyCountdownFiltersButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
                countdownEmptyPointerResetWorks &= CountdownEmptyResetRestoredResults();

                SetCountdownFilter(CountdownStatusFilter.Expired);
                WindowValidationInput.Pump(Dispatcher);
                ResetEmptyCountdownFiltersButton.Focus();
                WindowValidationInput.Pump(Dispatcher);
                countdownEmptyKeyboardResetWorks =
                    ReferenceEquals(Keyboard.FocusedElement, ResetEmptyCountdownFiltersButton) &&
                    WindowValidationInput.RaiseKey(ResetEmptyCountdownFiltersButton, Key.Space);
                WindowValidationInput.Pump(Dispatcher);
                countdownEmptyKeyboardResetWorks &= CountdownEmptyResetRestoredResults();
                NoteSearch.Text = savedSearch;
            }
            var countdownLayoutMatches = CountdownRemainingLayoutMatches;
            var countdownRemainingSamples = ProbeCountdownRemainingSamples();
            var countdownIncrementalRefresh = ProbeCountdownIncrementalRefresh();
            var noteCompletionStyleScoped = MainNoteCompletionStyleIsScoped;

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
                noResultsResetWorks,
                countdownEmptyStatesDistinguished,
                countdownEmptyPointerResetWorks,
                countdownEmptyKeyboardResetWorks,
                countdownLayoutMatches,
                countdownRemainingSamples,
                countdownIncrementalRefresh,
                noteCompletionStyleScoped);
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

    private bool CountdownEmptyResetRestoredResults()
    {
        var filter = _desktopWorkspace.Snapshot.Filter;
        return
            filter == DesktopWorkspaceFilter.Empty &&
            _desktopWorkspace.Snapshot.VisibleCountdowns.Count > 0 &&
            CountdownRows.Children.Count > 0 &&
            EmptyCountdownMessage.Visibility == Visibility.Collapsed &&
            ResetEmptyCountdownFiltersButton.Visibility == Visibility.Collapsed &&
            string.Equals(FilterButton.Content?.ToString(), "筛选", StringComparison.Ordinal) &&
            string.Equals(
                AutomationProperties.GetName(FilterButton),
                "打开筛选",
                StringComparison.Ordinal) &&
            string.Equals(
                StatusText.Text,
                $"筛选结果：{RecordCountText.Text}",
                StringComparison.Ordinal);
    }

    private CountdownIncrementalRefreshProbe ProbeCountdownIncrementalRefresh()
    {
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var originalPreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
        var originalTheme = _themeMode;
        var temporaryIds = new List<string>();
        double? originalOpacity = null;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var nowUtc = _desktopWorkspace.Snapshot.UtcNow;
            var dueUtc = nowUtc.AddSeconds(10);
            for (var index = 0; index < 10; index++)
            {
                var name = $"验证倒计时增量刷新-{index:D2}";
                var result = _desktopWorkspace.Apply(new SaveCountdownChange(
                    null,
                    name,
                    index == 0 ? dueUtc : nowUtc.AddDays(index + 1),
                    Array.Empty<string>()));
                if (!result.Succeeded)
                {
                    return CountdownIncrementalRefreshProbe.Failed;
                }

                var added = _desktopWorkspace.Snapshot.Settings.Countdowns
                    .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal));
                temporaryIds.Add(added.Id);
            }

            var record = _desktopWorkspace.Snapshot.Settings.Countdowns
                .Single(item => item.Id == temporaryIds[0]);
            var pointerRecord = _desktopWorkspace.Snapshot.Settings.Countdowns
                .Single(item => item.Id == temporaryIds[^1]);
            if (record is null)
            {
                return CountdownIncrementalRefreshProbe.Failed;
            }

            var before = ChinaStandardTime.ToBeijingTime(dueUtc.AddSeconds(-1));
            UpdateTime(before);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            if (!_countdownSourceRows.TryGetValue(record.Id, out var rowBefore) ||
                !_countdownRemainingTexts.TryGetValue(record.Id, out var textBefore) ||
                !_countdownSourceRows.TryGetValue(pointerRecord.Id, out var pointerRow) ||
                !_countdownSourceFocusTargets.TryGetValue(pointerRecord.Id, out var focusTarget))
            {
                return CountdownIncrementalRefreshProbe.Failed;
            }

            var scrollViewer = FindVisualParent<ScrollViewer>(rowBefore);
            scrollViewer?.ScrollToEnd();
            WindowValidationInput.Pump(Dispatcher);
            var offsetBefore = scrollViewer?.VerticalOffset ?? 0;
            Keyboard.Focus(focusTarget);
            WindowValidationInput.Pump(Dispatcher);
            originalOpacity = Opacity;
            Opacity = 1;
            Activate();
            WindowValidationInput.Pump(Dispatcher);
            var pointerCaptured = Mouse.Capture(pointerRow, CaptureMode.SubTree);
            UpdateTime(before.AddMilliseconds(250));
            WindowValidationInput.Pump(Dispatcher);
            var ordinaryIdentityPreserved =
                ReferenceEquals(rowBefore, _countdownSourceRows.GetValueOrDefault(record.Id)) &&
                ReferenceEquals(textBefore, _countdownRemainingTexts.GetValueOrDefault(record.Id));
            var focusPreserved = ReferenceEquals(Keyboard.FocusedElement, focusTarget);
            var nonZeroScrollPreserved =
                offsetBefore > 0 &&
                Math.Abs((scrollViewer?.VerticalOffset ?? 0) - offsetBefore) <= 0.1;
            // A session without an interactive mouse refuses Capture(); that is not a
            // tick-refresh regression. When capture is granted, the tick must keep it.
            var pointerStatePreserved = pointerCaptured
                ? ReferenceEquals(Mouse.Captured, pointerRow)
                : Mouse.Captured is null;
            if (originalOpacity is { } restoredOpacity)
            {
                Opacity = restoredOpacity;
                originalOpacity = null;
            }
            if (ReferenceEquals(Mouse.Captured, pointerRow))
            {
                Mouse.Capture(null);
            }

            var generation = CountdownRowRebuildGenerationForValidation;
            var collectionName = $"验证集合变化-{Guid.NewGuid():N}"[..27];
            var collectionResult = _desktopWorkspace.Apply(new SaveCountdownChange(
                null,
                collectionName,
                nowUtc.AddDays(20),
                Array.Empty<string>()));
            WindowValidationInput.Pump(Dispatcher);
            var collectionRecord = _desktopWorkspace.Snapshot.Settings.Countdowns
                .FirstOrDefault(item => string.Equals(item.Name, collectionName, StringComparison.Ordinal));
            if (collectionRecord is not null)
            {
                temporaryIds.Add(collectionRecord.Id);
            }
            var collectionChangeRebuilt =
                collectionResult.Succeeded &&
                collectionRecord is not null &&
                CountdownRowRebuildGenerationForValidation > generation &&
                _countdownSourceRows.ContainsKey(collectionRecord.Id);

            generation = CountdownRowRebuildGenerationForValidation;
            _desktopWorkspace.ApplyFilter(originalFilter with { CountdownStatus = CountdownStatusFilter.Expired });
            WindowValidationInput.Pump(Dispatcher);
            var filterChangeRebuilt =
                CountdownRowRebuildGenerationForValidation > generation &&
                !_countdownSourceRows.ContainsKey(record.Id);
            _desktopWorkspace.ApplyFilter(originalFilter);
            WindowValidationInput.Pump(Dispatcher);

            generation = CountdownRowRebuildGenerationForValidation;
            var nextPrecision = string.Equals(originalPreferences.DisplayPrecision, "Second", StringComparison.Ordinal)
                ? "Minute"
                : "Second";
            var precisionSaved = _desktopWorkspace.Apply(new SavePreferencesChange(
                nextPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd)).Succeeded;
            WindowValidationInput.Pump(Dispatcher);
            var precisionChangeRebuilt =
                precisionSaved && CountdownRowRebuildGenerationForValidation > generation;

            generation = CountdownRowRebuildGenerationForValidation;
            var nextTheme = originalTheme == ApplicationThemeMode.Day
                ? ApplicationThemeMode.Night
                : ApplicationThemeMode.Day;
            ApplyTheme(nextTheme);
            UpdateTime(before.AddMilliseconds(500));
            WindowValidationInput.Pump(Dispatcher);
            var themeChangeRebuilt =
                CountdownRowRebuildGenerationForValidation > generation &&
                _countdownSourceRows.ContainsKey(record.Id);

            generation = CountdownRowRebuildGenerationForValidation;
            UpdateTime(ChinaStandardTime.ToBeijingTime(dueUtc.AddSeconds(1)));
            WindowValidationInput.Pump(Dispatcher);
            var expiryChangeRebuilt = CountdownRowRebuildGenerationForValidation > generation;
            var expiryBackgroundFlipped =
                _countdownSourceRows.TryGetValue(record.Id, out var rowAfter) &&
                !ReferenceEquals(rowBefore, rowAfter) &&
                rowAfter is Border expiredCard &&
                ReferenceEquals(expiredCard.Background, FindResource("ExpiredBrush"));
            var expiryRemainingTextFlipped =
                _countdownRemainingTexts.TryGetValue(record.Id, out var textAfter) &&
                !ReferenceEquals(textBefore, textAfter) &&
                ReferenceEquals(textAfter.Foreground, FindResource("DangerBrush"));
            return new CountdownIncrementalRefreshProbe(
                ordinaryIdentityPreserved,
                focusPreserved,
                nonZeroScrollPreserved,
                pointerStatePreserved,
                collectionChangeRebuilt,
                filterChangeRebuilt,
                precisionChangeRebuilt,
                themeChangeRebuilt,
                expiryChangeRebuilt,
                expiryBackgroundFlipped,
                expiryRemainingTextFlipped);
        }
        finally
        {
            if (originalOpacity is { } restoredOpacity)
            {
                Opacity = restoredOpacity;
            }

            Mouse.Capture(null);
            ApplyTheme(originalTheme);
            _desktopWorkspace.ApplyFilter(originalFilter);
            _desktopWorkspace.Apply(new SavePreferencesChange(
                originalPreferences.DisplayPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd));
            foreach (var temporaryId in temporaryIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _desktopWorkspace.Apply(new DeleteCountdownChange(temporaryId));
            }

            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            child = VisualTreeHelper.GetParent(child);
            if (child is T match)
            {
                return match;
            }
        }

        return null;
    }

    private IReadOnlyList<MainCountdownRemainingMeasurement> ProbeCountdownRemainingSamples()
    {
        var originalPreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var temporaryIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var snapshot = _desktopWorkspace.Snapshot;
            var nowUtc = snapshot.UtcNow;
            var ordinaryName = "验证主窗口倒计时";
            var ordinaryResult = _desktopWorkspace.Apply(new SaveCountdownChange(
                null,
                ordinaryName,
                nowUtc.AddDays(4).AddHours(13).AddMinutes(18).AddSeconds(59),
                Array.Empty<string>()));
            if (ordinaryResult.Succeeded)
            {
                var added = _desktopWorkspace.Snapshot.Settings.Countdowns
                    .FirstOrDefault(record => string.Equals(record.Name, ordinaryName, StringComparison.Ordinal));
                if (added is not null)
                {
                    temporaryIds.Add(added.Id);
                }
            }

            var longName = "验证主窗口超长天数倒计时";
            var longResult = _desktopWorkspace.Apply(new SaveCountdownChange(
                null,
                longName,
                nowUtc.AddDays(2_000_000),
                Array.Empty<string>()));
            if (longResult.Succeeded)
            {
                var added = _desktopWorkspace.Snapshot.Settings.Countdowns
                    .FirstOrDefault(record => string.Equals(record.Name, longName, StringComparison.Ordinal));
                if (added is not null)
                {
                    temporaryIds.Add(added.Id);
                }
            }

            var samples = new List<MainCountdownRemainingMeasurement>();
            foreach (var precision in new[] { "Day", "Hour", "Minute", "Second" })
            {
                var saved = _desktopWorkspace.Apply(new SavePreferencesChange(
                    precision,
                    originalPreferences.MonthlySalary,
                    originalPreferences.WorkStart,
                    originalPreferences.WorkEnd)).Succeeded;
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
                var current = _desktopWorkspace.Snapshot;
                var record = current.Settings.Countdowns.FirstOrDefault(item =>
                    temporaryIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase) &&
                    string.Equals(item.Name, ordinaryName, StringComparison.Ordinal));
                if (record is null)
                {
                    continue;
                }
                if (!CountdownRemainingTextsForValidation.TryGetValue(record.Id, out var text))
                {
                    continue;
                }

                var expected = CountdownFormatting.FormatRemaining(record.TargetUtc, current.UtcNow, precision);
                samples.Add(CaptureMainCountdownRemaining(precision, record, expected, text, saved));
            }

            var longRecord = _desktopWorkspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
                string.Equals(item.Name, longName, StringComparison.Ordinal));
            if (longRecord is not null && CountdownRemainingTextsForValidation.TryGetValue(longRecord.Id, out var longText))
            {
                var expected = CountdownFormatting.FormatRemaining(longRecord.TargetUtc, _desktopWorkspace.Snapshot.UtcNow, "Second");
                samples.Add(CaptureMainCountdownRemaining("SecondLongDays", longRecord, expected, longText, longResult.Succeeded));
            }

            return samples;
        }
        finally
        {
            foreach (var id in temporaryIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _desktopWorkspace.Apply(new DeleteCountdownChange(id));
            }
            _desktopWorkspace.Apply(new SavePreferencesChange(
                originalPreferences.DisplayPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd));
            _desktopWorkspace.ApplyFilter(originalFilter);
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private static MainCountdownRemainingMeasurement CaptureMainCountdownRemaining(
        string precision,
        CountdownRecord record,
        string expected,
        TextBlock text,
        bool preferenceSaved)
    {
        var dpi = VisualTreeHelper.GetDpi(text);
        var formatted = new FormattedText(
            expected,
            CultureInfo.CurrentCulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        var requiredWidth = formatted.WidthIncludingTrailingWhitespace +
            Math.Max(0, formatted.OverhangTrailing) + 2 / dpi.DpiScaleX;
        var columnWidth = (text.Parent as Grid)?.ColumnDefinitions.ElementAtOrDefault(1)?.Width.Value ?? 0;
        var actualColumnWidth = (text.Parent as Grid)?.ColumnDefinitions.ElementAtOrDefault(1)?.ActualWidth ?? 0;
        return new MainCountdownRemainingMeasurement(
            precision,
            record.Name,
            expected,
            text.Text,
            Math.Round(columnWidth, 2),
            Math.Round(actualColumnWidth, 2),
            Math.Round(text.ActualWidth, 2),
            Math.Round(requiredWidth, 2),
            Math.Round(text.FontSize, 2),
            text.TextAlignment == TextAlignment.Right && text.HorizontalAlignment == HorizontalAlignment.Stretch,
            text.TextWrapping == TextWrapping.NoWrap && text.TextTrimming == TextTrimming.None,
            requiredWidth <= text.ActualWidth + 0.5 && !text.Text.Contains('\n') && !text.Text.Contains('\r') && !text.Text.Contains('…'),
            text.Typography.NumeralAlignment == FontNumeralAlignment.Tabular,
            preferenceSaved);
    }

    private Rect ValidationBounds(FrameworkElement element) =>
        element.TransformToAncestor(this).TransformBounds(
            new Rect(new Point(0, 0), element.RenderSize));
}

public sealed partial class FloatingWindow
{
    internal WorkspaceKind CurrentWorkspaceForValidation => _workspace;

    internal FloatingWindowValidationScenarios RunValidationScenarios(bool exerciseNoteAdd)
    {
        var originalWorkspace = _workspace;
        var initialStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Countdowns,
            _desktopWorkspace.Snapshot);
        try
        {
            var crossDayReminderAdded =
                exerciseNoteAdd && RunCrossDayReminderAddExercise();
            var reminderModes = exerciseNoteAdd
                ? RunReminderModeClickExercises()
                : ReminderModeClickResult.NotRun;
            var dragInteraction = RunDragInteractionExercise();
            var themeExercise = RunThemeBoundaryExercise();
            var manualPlacement = RunManualPlacementExercise();
            var uiPolish = ProbeUiPolishScenarios();
            var workspace = ProbeWorkspaceScenarios(initialStateMatches);

            return new FloatingWindowValidationScenarios(
                crossDayReminderAdded,
                reminderModes,
                dragInteraction,
                themeExercise,
                manualPlacement,
                uiPolish,
                workspace);
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
        }
    }

    private FloatingWorkspaceValidationScenarios ProbeWorkspaceScenarios(
        bool initialStateMatches)
    {
        var snapshot = _desktopWorkspace.Snapshot;

        FloatingNotesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(Dispatcher);
        UpdateLayout();
        var notesStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Notes,
            snapshot);
        var renderedNoteIds = GetFloatingRenderedNoteIds();
        var expectedNoteIds = snapshot.VisibleActiveNotes
            .Select(note => note.Id)
            .ToArray();
        var notesKeepExistingSortOrder = expectedNoteIds.SequenceEqual(
            renderedNoteIds,
            StringComparer.Ordinal);
        var countdownButtonTextFit = MeasureValidationButtonText(FloatingCountdownsButton);
        var notesButtonTextFit = MeasureValidationButtonText(FloatingNotesButton);
        var titleTextFit = MeasureValidationTitleText(FloatingTitle, FloatingDragHandle);
        var titleTextHeightFit = MeasureValidationTitleTextHeight(FloatingTitle);

        FloatingNotesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(Dispatcher);
        UpdateLayout();
        var notesReselectionStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Notes,
            snapshot);

        FloatingCountdownsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(Dispatcher);
        UpdateLayout();
        var countdownStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Countdowns,
            snapshot);

        FloatingCountdownsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(Dispatcher);
        UpdateLayout();
        var countdownReselectionStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Countdowns,
            snapshot);
        var countdownSelectionSurvivesDayNightThemes =
            InitialCountdownSelectionSurvivesDayNightThemes();

        return new FloatingWorkspaceValidationScenarios(
            initialStateMatches,
            notesStateMatches &&
            notesReselectionStateMatches &&
            countdownStateMatches &&
            countdownReselectionStateMatches,
            countdownSelectionSurvivesDayNightThemes,
            notesKeepExistingSortOrder,
            countdownButtonTextFit,
            notesButtonTextFit,
            titleTextFit,
            titleTextHeightFit,
            FloatingCountdownCount,
            FloatingCountdownTexts,
            IncomeText,
            renderedNoteIds);
    }

    private bool FloatingWorkspacePresentationMatches(
        WorkspaceKind workspace,
        DesktopWorkspaceSnapshot snapshot)
    {
        var selectedButtonCount = new[]
        {
            FloatingCountdownsButton.IsChecked == true,
            FloatingNotesButton.IsChecked == true
        }.Count(selected => selected);
        var chromeMatches =
            selectedButtonCount == 1 &&
            FloatingCountdownsButton.IsChecked == (workspace == WorkspaceKind.Countdowns) &&
            FloatingNotesButton.IsChecked == (workspace == WorkspaceKind.Notes) &&
            string.Equals(
                FloatingTitle.Text,
                workspace == WorkspaceKind.Countdowns ? "倒计时" : "今日便签",
                StringComparison.Ordinal) &&
            FloatingNoteEntry.Visibility == (workspace == WorkspaceKind.Notes
                ? Visibility.Visible
                : Visibility.Collapsed) &&
            FloatingRows.Children.Count > 0;
        if (!chromeMatches)
        {
            return false;
        }

        if (workspace == WorkspaceKind.Countdowns)
        {
            var expectedCountdownCount = snapshot.VisibleCountdowns.Count(record =>
                record.TargetUtc > snapshot.UtcNow);
            var emptyMessages = FloatingRows.Children
                .OfType<TextBlock>()
                .Select(text => text.Text)
                .ToArray();
            return
                FloatingCountdownCount == expectedCountdownCount &&
                !ValidationDescendants<CheckBox>(FloatingRows).Any() &&
                emptyMessages.All(message => string.Equals(
                    message,
                    "没有未到期的倒计时。",
                    StringComparison.Ordinal));
        }

        var expectedNoteIds = snapshot.VisibleActiveNotes
            .Select(note => note.Id)
            .ToArray();
        var noteIdsMatch = GetFloatingRenderedNoteIds().SequenceEqual(
            expectedNoteIds,
            StringComparer.Ordinal);
        var emptyNoteMessages = FloatingRows.Children
            .OfType<TextBlock>()
            .Select(text => text.Text)
            .ToArray();
        return
            noteIdsMatch &&
            (expectedNoteIds.Length > 0
                ? emptyNoteMessages.Length == 0
                : emptyNoteMessages.Length == 1 && string.Equals(
                    emptyNoteMessages[0],
                    "今天还没有待办便签。",
                    StringComparison.Ordinal));
    }

    private IReadOnlyList<string> GetFloatingRenderedNoteIds() =>
        ValidationDescendants<CheckBox>(FloatingRows)
            .Where(checkBox => checkBox.Tag is string)
            .Select(checkBox => (string)checkBox.Tag)
            .ToArray();

    private static WindowValidationTextFit MeasureValidationButtonText(ContentControl button)
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
        return new WindowValidationTextFit(available >= formatted.Width, available, formatted.Width);
    }

    private static WindowValidationTextFit MeasureValidationTitleText(
        TextBlock textBlock,
        FrameworkElement visibleAncestor)
    {
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var formatted = FormatValidationText(textBlock, dpi);
        var origin = textBlock.TranslatePoint(new Point(0, 0), visibleAncestor);
        var visibleBounds = Rect.Intersect(
            new Rect(origin, textBlock.RenderSize),
            new Rect(new Point(0, 0), visibleAncestor.RenderSize));
        var available = visibleBounds.IsEmpty ? 0 : visibleBounds.Width;
        var required =
            formatted.WidthIncludingTrailingWhitespace +
            Math.Max(0, formatted.OverhangTrailing) +
            (2 / dpi.DpiScaleX);
        return new WindowValidationTextFit(available >= required, available, required);
    }

    private static WindowValidationTextFit MeasureValidationTitleTextHeight(TextBlock textBlock)
    {
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var formatted = FormatValidationText(textBlock, dpi);
        var available = Math.Max(0, textBlock.ActualHeight);
        var required =
            formatted.Height +
            Math.Max(0, formatted.OverhangAfter) +
            (4 / dpi.DpiScaleY);
        return new WindowValidationTextFit(available >= required, available, required);
    }

    private static FormattedText FormatValidationText(TextBlock textBlock, DpiScale dpi) =>
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

    private static IEnumerable<T> ValidationDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in ValidationDescendants<T>(child))
            {
                yield return descendant;
            }
        }
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

            return new FloatingUiPolishScenarios(
                datePickerKeyboardLifecycleWorks,
                textErrorAtSource && dateErrorAtSource,
                textErrorCleared && dateErrorCleared);
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
    FloatingUiPolishScenarios UiPolish,
    FloatingWorkspaceValidationScenarios Workspace);

internal sealed record FloatingWorkspaceValidationScenarios(
    bool InitialStateMatches,
    bool SwitchingStaysSynchronized,
    bool CountdownSelectionSurvivesDayNightThemes,
    bool NotesKeepExistingSortOrder,
    WindowValidationTextFit CountdownButtonTextFit,
    WindowValidationTextFit NotesButtonTextFit,
    WindowValidationTextFit TitleTextFit,
    WindowValidationTextFit TitleTextHeightFit,
    int CountdownCount,
    IReadOnlyList<string> CountdownTexts,
    string IncomeText,
    IReadOnlyList<string> RenderedNoteIds);

internal sealed record WindowValidationTextFit(
    bool Fits,
    double Available,
    double Required);

internal sealed record MainUiPolishScenarios(
    bool NoteEditorFieldsDoNotOverlap,
    bool NoteEditorLabelsVisible,
    bool MinimumSizeMatchesDefault,
    bool PopupKeyboardLifecycleWorks,
    bool PopupOutsideFocusPreserved,
    bool EmptyStatesDistinguished,
    bool NoResultsResetWorks,
    bool CountdownEmptyStatesDistinguished,
    bool CountdownEmptyPointerResetRestoresResults,
    bool CountdownEmptyKeyboardResetRestoresResults,
    bool CountdownRemainingLayoutMatches,
    IReadOnlyList<MainCountdownRemainingMeasurement> CountdownRemainingSamples,
    CountdownIncrementalRefreshProbe CountdownIncrementalRefresh,
    bool MainNoteCompletionStyleScoped);

internal sealed record CountdownIncrementalRefreshProbe(
    bool IdentityPreserved,
    bool FocusPreserved,
    bool NonZeroScrollPreserved,
    bool PointerStatePreserved,
    bool CollectionChangeRebuilt,
    bool FilterChangeRebuilt,
    bool PrecisionChangeRebuilt,
    bool ThemeChangeRebuilt,
    bool ExpiryChangeRebuilt,
    bool ExpiryBackgroundFlipped,
    bool ExpiryRemainingTextFlipped)
{
    public bool ExpiryVisualsFlipTogether =>
        ExpiryBackgroundFlipped && ExpiryRemainingTextFlipped;

    public bool TriggerMatrixRebuilt =>
        CollectionChangeRebuilt &&
        FilterChangeRebuilt &&
        PrecisionChangeRebuilt &&
        ThemeChangeRebuilt &&
        ExpiryChangeRebuilt;

    public static CountdownIncrementalRefreshProbe Failed { get; } = new(
        false, false, false, false, false, false, false, false, false, false, false);
}

internal sealed record MainCountdownRemainingMeasurement(
    string Precision,
    string RecordName,
    string ExpectedText,
    string RenderedText,
    double ColumnWidth,
    double ActualColumnWidth,
    double ActualWidth,
    double RequiredWidth,
    double FontSize,
    bool IsRightAligned,
    bool IsSingleLineAndUntrimmed,
    bool IsFullyVisible,
    bool UsesTabularNumerals,
    bool PreferenceSaved);

internal sealed record FloatingUiPolishScenarios(
    bool DatePickerKeyboardLifecycleWorks,
    bool FieldErrorsAppearAtSource,
    bool CorrectedFieldErrorsClearLiveMessage);

internal sealed record PopupFocusExerciseResult(
    bool EscapeReturnsFocus,
    bool OutsideFocusPreserved);

internal sealed record DatePickerPointerExerciseResult(
    bool TextBoxPresent,
    bool PressInjected,
    bool PressDidNotOpen,
    bool ReleaseInjected,
    bool ReleaseOpened,
    bool ReleaseHandled,
    bool ThemedPopupOpened);

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

    public static DatePickerPointerExerciseResult ExerciseDatePickerTextPointer(DatePicker picker)
    {
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            picker.IsDropDownOpen = false;
            picker.ApplyTemplate();
            var textBox = picker.Template.FindName("PART_TextBox", picker) as DatePickerTextBox;
            if (textBox is null || PresentationSource.FromVisual(textBox) is null)
            {
                return new DatePickerPointerExerciseResult(
                    false, false, false, false, false, false, false);
            }

            RaiseDateTextPointer(
                picker,
                textBox,
                UIElement.PreviewMouseLeftButtonDownEvent);
            Pump(picker.Dispatcher);
            var pressDidNotOpen = !picker.IsDropDownOpen;
            var releaseHandled = RaiseDateTextPointer(
                picker,
                textBox,
                UIElement.PreviewMouseLeftButtonUpEvent);
            Pump(picker.Dispatcher);
            var calendar = ThemedDatePickerBehavior.GetCalendar(picker);
            var releaseOpened = picker.IsDropDownOpen;
            var themedPopupOpened =
                releaseOpened &&
                calendar?.Template is not null &&
                ReferenceEquals(calendar.Style, picker.CalendarStyle);
            return new DatePickerPointerExerciseResult(
                true,
                true,
                pressDidNotOpen,
                true,
                releaseOpened,
                releaseHandled,
                themedPopupOpened);
        }
        finally
        {
            picker.IsDropDownOpen = false;
            Pump(picker.Dispatcher);
        }
    }

    public static bool ExerciseDatePickerButton(DatePicker picker)
    {
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            picker.IsDropDownOpen = false;
            picker.ApplyTemplate();
            if (picker.Template.FindName("PART_Button", picker) is not Button button ||
                PresentationSource.FromVisual(button) is null)
            {
                return false;
            }

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(picker.Dispatcher);
            return picker.IsDropDownOpen;
        }
        finally
        {
            picker.IsDropDownOpen = false;
            Pump(picker.Dispatcher);
        }
    }

    public static bool RaisePreviewKey(
        UIElement target,
        Key key,
        ModifierKeys modifiers = ModifierKeys.None)
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
        using (MainShortcutInput.InjectModifiers(eventArgs, modifiers))
        {
            target.RaiseEvent(eventArgs);
        }
        return eventArgs.Handled;
    }

    public static bool RaiseKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target);
        if (source is null)
        {
            return false;
        }

        var keyDown = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        target.RaiseEvent(keyDown);

        var keyUp = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.KeyUpEvent
        };
        target.RaiseEvent(keyUp);
        return keyDown.Handled && keyUp.Handled;
    }

    private static bool RaiseDateTextPointer(
        DatePicker picker,
        DatePickerTextBox textBox,
        RoutedEvent routedEvent)
    {
        var eventArgs = new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = routedEvent,
            Source = textBox
        };
        picker.RaiseEvent(eventArgs);
        return eventArgs.Handled;
    }

    private static bool OwnsKeyboardFocus(IInputElement target) =>
        target is UIElement element
            ? element.IsKeyboardFocusWithin
            : ReferenceEquals(Keyboard.FocusedElement, target);
}
