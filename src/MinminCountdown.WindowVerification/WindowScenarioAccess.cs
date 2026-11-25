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

// Runner-only adapters over public WPF input, the visual tree and existing IVT
// observables. No member here is compiled into the product assembly.
internal static class WindowScenarioAccess
{
    internal static void ResetNoteEditor(this MainWindow window) =>
        window.CancelNoteEdit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    internal static void ResetCountdownEditor(this MainWindow window) =>
        window.CancelCountdownEdit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    internal static void SetCountdownEditorExpanded(this MainWindow window, bool expanded)
    {
        if (window.CountdownTime.IsVisible != expanded)
        {
            window.CountdownEditorExpandButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static void SetCountdownFilter(this MainWindow window, CountdownStatusFilter filter)
    {
        var target = filter switch
        {
            CountdownStatusFilter.All => window.CountdownAllFilter,
            CountdownStatusFilter.Active => window.CountdownActiveFilter,
            CountdownStatusFilter.Expired => window.CountdownExpiredFilter,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };
        target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    internal static void SelectNotesSegment(this MainWindow window, bool showCompleted)
    {
        var target = showCompleted ? window.CompletedNotesSegment : window.ActiveNotesSegment;
        target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static void ClearFilters(this MainWindow window) =>
        window.ClearFilterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    internal static void BeginEditCountdown(this MainWindow window, CountdownRecord record)
    {
        var edit = VisualDescendants<Button>(window.CountdownRows).Single(button =>
            string.Equals(button.Tag as string, record.Id, StringComparison.Ordinal) &&
            string.Equals(button.Content as string, "编辑", StringComparison.Ordinal));
        edit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static void SelectNoteRow(this MainWindow window, Border row, ModifierKeys modifiers)
    {
        row.Focus();
        WindowValidationInput.RaisePreviewKey(row, Key.Space, modifiers);
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static void OnClearSelectedNotes(this MainWindow window, object sender, RoutedEventArgs args) =>
        window.ClearSelectedNotesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    internal static Border[] VisibleNoteSelectionRows(this MainWindow window) =>
        VisualDescendants<Border>(window.NotesListCard)
            .Where(row => row.Uid == "SelectableNoteRow" && row.IsVisible)
            .ToArray();

    internal static UIElement FilterInitialFocusTarget(this MainWindow window) =>
        window.FilterTagOptions.Children
            .OfType<UIElement>()
            .FirstOrDefault(child => child.Visibility == Visibility.Visible && child.IsEnabled && child.Focusable)
        ?? window.UntaggedOnly;

    internal static FrameworkElement FindMainNoteRow(MainWindow window, string noteId) =>
        VisualDescendants<FrameworkElement>(window.NotesListCard)
            .FirstOrDefault(element => element is NoteSelectionCard &&
                string.Equals(element.Tag as string, noteId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"找不到便签 {noteId} 的真实行。");

    internal static CheckBox FindMainNoteCompletion(MainWindow window, string noteId) =>
        VisualDescendants<CheckBox>(window.NotesListCard)
            .FirstOrDefault(box => string.Equals(box.Tag as string, noteId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"找不到便签 {noteId} 的完成复选框。");

    internal static FrameworkElement FindMainCountdownRow(MainWindow window, string countdownId) =>
        window.CountdownRows.Children.OfType<FrameworkElement>().FirstOrDefault(row =>
            VisualDescendants<Button>(row).Any(button =>
                string.Equals(button.Tag as string, countdownId, StringComparison.Ordinal)))
        ?? throw new InvalidOperationException($"找不到倒计时 {countdownId} 的真实行。");

    internal static bool HasMainCountdownRow(MainWindow window, string countdownId) =>
        window.CountdownRows.Children.OfType<FrameworkElement>().Any(row =>
            VisualDescendants<Button>(row).Any(button =>
                string.Equals(button.Tag as string, countdownId, StringComparison.Ordinal)));

    internal static Button FindMainCountdownFocusTarget(MainWindow window, string countdownId) =>
        VisualDescendants<Button>(window.CountdownRows).FirstOrDefault(button =>
            string.Equals(button.Tag as string, countdownId, StringComparison.Ordinal) &&
            string.Equals(button.Content as string, "编辑", StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"找不到倒计时 {countdownId} 的编辑焦点目标。");

    internal static void RenderNotes(this MainWindow window, DateTimeOffset nowUtc) =>
        window.UpdateTime(ChinaStandardTime.ToBeijingTime(nowUtc));

    internal static void RenderVisibleWorkspace(this MainWindow window, DesktopWorkspace workspace) =>
        window.UpdateTime(workspace.Snapshot.BeijingNow);

    internal static void RenderFilterTags(this MainWindow window, DesktopWorkspace workspace)
    {
        window.RefreshBeforeShow();
        window.UpdateTime(workspace.Snapshot.BeijingNow);
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static void UpdateFilterSummary(this MainWindow window, DesktopWorkspace workspace)
    {
        var count = workspace.Snapshot.AppliedFilterCount;
        window.FilterButtonText.Text = count == 0 ? "筛选" : $"筛选 · {count}";
        AutomationProperties.SetName(
            window.FilterButton,
            count == 0 ? "打开筛选" : $"打开筛选，已应用 {count} 项");
        UiAccessibility.Announce(window.StatusText, $"筛选结果：{window.RecordCountText.Text}");
    }

    internal static void LoadPreferences(this MainWindow window, DesktopWorkspace workspace)
    {
        var preferences = workspace.Snapshot.Settings.Preferences;
        window.DisplayPrecision.SelectedIndex = preferences.DisplayPrecision switch
        {
            "Day" => 0,
            "Hour" => 1,
            "Minute" => 2,
            _ => 3
        };
        window.MonthlySalary.Text = preferences.MonthlySalary.ToString("N2", CultureInfo.CurrentCulture);
        TimeInputBehavior.SetValue(window.WorkStart, preferences.WorkStart);
        TimeInputBehavior.SetValue(window.WorkEnd, preferences.WorkEnd);
        // Commit the unchanged, restored values through the real command so the
        // product's own draft-dirty state is cleared instead of mirrored here.
        window.SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static void ClearNoteFieldErrors(this MainWindow window)
    {
        ClearFieldError(window.NoteTextError, window.NoteMessage, window.NoteText);
        ClearFieldError(window.NoteDateError, window.NoteMessage, window.NoteDate);
    }

    internal static void ClearCountdownFieldErrors(this MainWindow window)
    {
        ClearFieldError(window.CountdownNameError, window.CountdownMessage, window.CountdownName);
        ClearFieldError(window.CountdownDateError, window.CountdownMessage, window.CountdownDate);
        ClearFieldError(window.CountdownTimeError, window.CountdownMessage, window.CountdownTime);
    }

    internal static void ClearPreferenceFieldErrors(this MainWindow window)
    {
        ClearFieldError(window.MonthlySalaryError, window.StatusText, window.MonthlySalary);
        ClearFieldError(window.WorkStartError, window.StatusText, window.WorkStart);
        ClearFieldError(window.WorkEndError, window.StatusText, window.WorkEnd);
    }

    internal static void ClearNoteFieldErrors(this FloatingWindow window)
    {
        ClearFieldError(window.FloatingNoteTextError, window.FloatingNoteMessage, window.FloatingNoteText);
        ClearFieldError(window.FloatingNoteDateError, window.FloatingNoteMessage, window.FloatingNoteDate);
        ClearFieldError(window.FloatingReminderMessage, window.FloatingNoteMessage, window.FloatingManageReminderRules);
    }

    internal static void SetNoteMessage(this FloatingWindow window, string text) =>
        UiAccessibility.Announce(window.FloatingNoteMessage, text, block =>
            block.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible);

    internal static void ExpandNoteSearch(this FloatingWindow window, bool focus)
    {
        if (!window.FloatingNoteSearch.IsVisible)
        {
            var trigger = window.FloatingNoteSearchHintTerm.IsVisible
                ? window.FloatingNoteSearchHintTerm
                : window.FloatingSearchNotes;
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
        }
        if (!focus)
        {
            Keyboard.Focus(null);
        }
    }

    internal static void ClearNoteSearch(this FloatingWindow window)
    {
        if (window.FloatingNoteSearch.IsVisible)
        {
            WindowValidationInput.RaisePreviewKey(window.FloatingNoteSearch, Key.Escape);
        }
        else
        {
            window.FloatingNoteSearchHintClear.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static ContextMenu? FindActiveNoteOverflowMenu(this FloatingWindow window) =>
        WindowVerification.FloatingNoteOverflowMenuScenario.FindOpenContextMenu();

    internal static void CloseNoteOverflowMenu(this FloatingWindow window, bool immediate)
    {
        var menu = window.FindActiveNoteOverflowMenu();
        if (menu is not null)
        {
            menu.IsOpen = false;
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    internal static Color ThemeColor(this FloatingWindow window, string resourceKey) =>
        window.FindResource(resourceKey) is SolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"主题资源不是不透明色刷：{resourceKey}");

    internal static string PlushGlowColor(this FloatingWindow window) =>
        window.FindResource(FloatingThemePalette.Daylight.PlushGlowBrushKey)?.ToString() ?? string.Empty;

    internal static string MascotPresentationMode(this FloatingWindow window) =>
        window.DayMascotsImage.Visibility == Visibility.Visible &&
        window.FloatingPlushScene.IsVisible &&
        window.DayMascotsImage.IsVisible &&
        window.DayMascotsImage.Source is not null
            ? "DayRaster"
            : "Text";

    internal static bool SurfacesFollowActiveTheme(this FloatingWindow window)
    {
        static bool IsTransparent(Brush brush) =>
            brush is SolidColorBrush solid && solid.Color.A == 0;

        return
            window.FloatingDayRasterScene.Visibility == Visibility.Visible &&
            IsTransparent(window.TaskCard.Background) &&
            IsTransparent(window.TaskCard.BorderBrush) &&
            IsTransparent(window.FloatingHeaderSurface.Background) &&
            IsTransparent(window.FloatingHeaderSurface.BorderBrush) &&
            IsTransparent(window.FloatingThemeBackdrop.Background) &&
            IsTransparent(window.FloatingThemeBackdrop.BorderBrush) &&
            window.FloatingRows.Margin.Equals(new Thickness(9, 13, 10, 0));
    }

    internal static bool PlushSceneFollowsActiveTheme(this FloatingWindow window) =>
        string.Equals(window.MascotPresentationMode(), "DayRaster", StringComparison.Ordinal) &&
        window.DayMascotsImage.Source is not null;

    internal static bool HeaderControlHasAccessibleContrast(this FloatingWindow window, Control control)
    {
        control.ApplyTemplate();
        var surface = control.Template?.FindName("Surface", control) as Border;
        var background = surface?.Background is SolidColorBrush solid && solid.Color.A > 0
            ? solid
            : window.FloatingHeaderSurface.Background;
        return ApplicationTheme.MeetsContrast(background, control.Foreground, 4.5);
    }

    internal static void SetMainNoteReminderRules(MainWindow window, IReadOnlyList<ReminderRule> rules) =>
        SetReminderRulesThroughEditor(
            window,
            () => window.ManageNoteReminderRulesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)),
            rules);

    internal static void SetFloatingReminderRules(FloatingWindow window, IReadOnlyList<ReminderRule> rules) =>
        SetReminderRulesThroughEditor(
            window,
            () => window.FloatingManageReminderRules.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)),
            rules);

    private static void SetReminderRulesThroughEditor(
        Window owner,
        Action openEditor,
        IReadOnlyList<ReminderRule> rules)
    {
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var editor = owner.OwnedWindows.OfType<ReminderRulesEditorWindow>().Single();
            foreach (var delete in VisualDescendants<Button>(editor)
                         .Where(button => AutomationProperties.GetName(button)
                             .StartsWith("删除规则 ", StringComparison.Ordinal))
                         .ToArray())
            {
                WindowValidationInput.InvokeButton(delete);
            }

            foreach (var rule in rules)
            {
                switch (rule)
                {
                    case AbsoluteOnceReminderRule once:
                        SetEditorSchedule(editor, once.ScheduledUtc, null);
                        break;
                    case FixedIntervalReminderRule repeating:
                        SetEditorSchedule(editor, repeating.FirstDueUtc, repeating.Interval.Minutes);
                        break;
                    default:
                        throw new NotSupportedException($"该 UI 验证适配器尚不支持 {rule.GetType().Name}。");
                }
                WindowValidationInput.InvokeButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
            }
            WindowValidationInput.InvokeButton(FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
        }));
        openEditor();
        WindowValidationInput.Pump(owner.Dispatcher);
    }

    private static void SetEditorSchedule(
        ReminderRulesEditorWindow editor,
        DateTimeOffset firstDueUtc,
        int? intervalMinutes)
    {
        var due = ChinaStandardTime.ToBeijingTime(firstDueUtc);
        FindByAutomationId<ComboBox>(editor, "ReminderRuleMode").SelectedIndex = intervalMinutes is null ? 0 : 1;
        FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate").SelectedDate = due.Date;
        FindByAutomationId<TextBox>(editor, "ReminderRuleFirstDueTime").Text = due.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (intervalMinutes is { } minutes)
        {
            FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval").SelectedIndex = minutes switch
            {
                15 => 0,
                30 => 1,
                60 => 2,
                _ => 3
            };
            FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval").Text =
                minutes is 15 or 30 or 60 ? string.Empty : minutes.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static T FindByAutomationId<T>(DependencyObject root, string id) where T : DependencyObject =>
        VisualDescendants<T>(root).Single(element => AutomationProperties.GetAutomationId(element) == id);

    internal static int ReminderRuleCount(TextBlock summary)
    {
        if (string.Equals(summary.Text, "不提醒", StringComparison.Ordinal))
        {
            return 0;
        }
        var digits = new string(summary.Text.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : throw new InvalidOperationException($"无法从真实提醒摘要读取规则数量：{summary.Text}");
    }

    internal static IReadOnlyList<ReminderRule> ReadMainNoteReminderRules(MainWindow window) =>
        ReadReminderRulesThroughEditor(
            window,
            () => window.ManageNoteReminderRulesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));

    internal static IReadOnlyList<ReminderRule> ReadMainCountdownReminderRules(MainWindow window) =>
        ReadReminderRulesThroughEditor(
            window,
            () => window.ApplyCountdownReminderModeForValidation(CountdownReminderMode.Custom));

    internal static DateTimeOffset? GetCountdownTargetUtc(MainWindow window)
    {
        if (window.CountdownDate.SelectedDate is null ||
            !TimeInputBehavior.TryParseStrict(
                window.CountdownTime.Text,
                TimeInputFormat.HourMinuteSecond,
                out var timeOfDay))
        {
            return null;
        }
        return CountdownFormatting.ToUtc(
            window.CountdownDate.SelectedDate.Value,
            timeOfDay.Hours,
            timeOfDay.Minutes,
            timeOfDay.Seconds);
    }

    internal static FrameworkElement[] KeyboardPathControls(MainWindow window) =>
    [
        window.MinimizeButton, window.CloseButton,
        window.CountdownTab, window.NotesTab, window.PreferencesButton, window.FilterButton, window.TagManagerButton,
        window.CountdownName, window.CountdownDate, window.CountdownTime,
        window.CountdownTags.PopupTrigger, window.CountdownReminderHelp,
        window.CountdownReminderNoRadio, window.CountdownReminderTimeRadio, window.CountdownReminderCustomRadio,
        window.CancelCountdownEdit, window.CountdownEditorExpandButton, window.DisplayPrecision, window.MonthlySalary,
        window.AddEmptyCountdownButton, window.WorkStart, window.WorkEnd, window.SavePreferencesButton,
        window.CountdownListRegion, window.NoteText, window.NoteDate, window.CancelNoteEdit, window.AddEmptyNoteButton,
        window.ManageNoteReminderRulesButton, window.NoteTags.PopupTrigger, window.NoteSearch,
        window.NotesListRegion, window.StatusText
    ];

    internal static void RestoreMainNoteEditor(
        MainWindow window,
        DesktopWorkspace workspace,
        string? editingId,
        string text,
        DateTime? date)
    {
        window.ResetNoteEditor();
        if (editingId is not null)
        {
            var note = workspace.Snapshot.AllNotes.SingleOrDefault(note => note.Id == editingId)
                ?? throw new InvalidOperationException($"恢复原便签编辑器时找不到 {editingId}。");
            window.LoadNoteIntoEditor(note);
        }
        window.NoteText.Text = text;
        window.NoteDate.SelectedDate = date;
        WindowValidationInput.Pump(window.Dispatcher);
    }

    private static IReadOnlyList<ReminderRule> ReadReminderRulesThroughEditor(Window owner, Action openEditor)
    {
        IReadOnlyList<ReminderRule>? rules = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var editor = owner.OwnedWindows.OfType<ReminderRulesEditorWindow>().Single();
            rules = editor.ResultRules.ToArray();
            WindowValidationInput.InvokeButton(FindByAutomationId<Button>(editor, "CancelReminderRulesButton"));
        }));
        openEditor();
        WindowValidationInput.Pump(owner.Dispatcher);
        return rules ?? throw new InvalidOperationException("未能从真实提醒编辑器读取草稿规则。");
    }

    private static void ClearFieldError(TextBlock field, TextBlock message, params Control[] sources)
    {
        var previous = field.Text;
        UiAccessibility.Announce(field, string.Empty);
        AutomationProperties.SetName(field, string.Empty);
        AutomationProperties.SetHelpText(field, string.Empty);
        foreach (var source in sources)
        {
            if (string.Equals(AutomationProperties.GetHelpText(source), previous, StringComparison.Ordinal))
            {
                AutomationProperties.SetHelpText(source, string.Empty);
            }
            InputErrorState.SetHasError(source, false);
        }
        if (!string.IsNullOrWhiteSpace(previous) && string.Equals(message.Text, previous, StringComparison.Ordinal))
        {
            UiAccessibility.Announce(message, string.Empty);
        }
    }

    internal static bool SegmentIndicatorAligned(
        FrameworkElement root,
        Control target,
        Border indicator)
    {
        if (!root.IsLoaded || !root.IsVisible || !target.IsLoaded || !target.IsVisible ||
            target.ActualWidth <= 0 || target.ActualHeight <= 0 ||
            target.Template?.FindName("Surface", target) is not Border surface)
        {
            return false;
        }
        var targetBounds = target.TransformToAncestor(root)
            .TransformBounds(new Rect(new Point(0, 0), target.RenderSize));
        var margin = surface.Margin;
        var expected = new Rect(
            targetBounds.Left + margin.Left,
            targetBounds.Top + margin.Top,
            Math.Max(0, targetBounds.Width - margin.Left - margin.Right),
            Math.Max(0, targetBounds.Height - margin.Top - margin.Bottom));
        var translation = indicator.RenderTransform switch
        {
            TranslateTransform direct => direct,
            TransformGroup group => group.Children.OfType<TranslateTransform>().LastOrDefault(),
            _ => null
        };
        var actual = new Rect(
            translation?.X ?? 0,
            translation?.Y ?? 0,
            Math.Abs(indicator.Width),
            Math.Abs(indicator.Height));
        return SegmentIndicator.GeometryMatches(actual, expected);
    }

    internal static WorkspaceKind CurrentWorkspace(FloatingWindow window) =>
        window.FloatingNotesButton.IsChecked == true ? WorkspaceKind.Notes : WorkspaceKind.Countdowns;

    internal static TextBlock ActiveBottomStatus(FloatingWindow window) =>
        window.FloatingEncouragement.Visibility == Visibility.Visible
            ? window.FloatingEncouragement
            : window.FloatingIncome;

    internal static NoteListValidationParts NoteListParts(MainWindow window) => new(
        window.NotesListRegion,
        window.ActiveNoteRows,
        window.CompletedNoteRows,
        window.NoteSearch,
        window.CompletedNotesSegment,
        window.NoteBatchActions,
        window.SelectedNotesCount);

    internal static MainSurfaceEvidenceParts SurfaceEvidenceParts(MainWindow window) => new(
        window.SaveCountdownButton,
        window.PreferencesPopup,
        window.FilterPopup,
        window.TagManagerPopup,
        window.CountdownTags);

    internal static void ExpandCountdownEditorForValidation(this MainWindow window) =>
        window.SetCountdownEditorExpanded(true);

    internal static Grid BuildCountdownBlocksForValidation(this MainWindow window, string remaining, bool isPrimary) =>
        new MainWindowValidationScenariosRunner(window).BuildCountdownBlocksForValidation(remaining, isPrimary);

    internal static void ShowCountdownFieldErrorForValidation(this MainWindow window)
    {
        window.SetCountdownEditorExpanded(true);
        window.CountdownName.Text = string.Empty;
        window.SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static void ShowPreferencesSalaryErrorForValidation(this MainWindow window)
    {
        WindowValidationInput.ClosePopupForCleanup(window.PreferencesPopup);
        window.Activate();
        Keyboard.Focus(window.PreferencesButton);
        window.PreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        window.MonthlySalary.Text = "-500";
        window.SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static IReadOnlyList<SurfaceRadiusSample> SharedSurfaceRadiiForValidation(this FloatingWindow window) =>
        new FloatingWindowValidationScenariosRunner(window).SharedSurfaceRadiiForValidation();

    internal static FloatingScrollContextState ScrollContextStateForValidation(this FloatingWindow window) =>
        new FloatingWindowValidationScenariosRunner(window).ScrollContextStateForValidation();

    internal static void SettleScrollContextForValidation(this FloatingWindow window) =>
        new FloatingWindowValidationScenariosRunner(window).SettleScrollContextForValidation();

    internal static void ScrollBodyForValidation(this FloatingWindow window, double offset) =>
        new FloatingWindowValidationScenariosRunner(window).ScrollBodyForValidation(offset);

    internal static void SetNoteSearchExpandedForValidation(this FloatingWindow window, bool expanded) =>
        new FloatingWindowValidationScenariosRunner(window).SetNoteSearchExpandedForValidation(expanded);

    internal static ContextMenu? OpenVisibleNoteOverflowMenuForValidation(this FloatingWindow window) =>
        new FloatingWindowValidationScenariosRunner(window).OpenVisibleNoteOverflowMenuForValidation();

    internal static ContextMenu? OpenFirstNoteOverflowMenuForValidation(this FloatingWindow window) =>
        new FloatingWindowValidationScenariosRunner(window).OpenFirstNoteOverflowMenuForValidation();

    internal static FloatingQuickAddPaletteBindingValidation ProbeQuickAddPaletteBindings(this FloatingWindow window) =>
        new FloatingWindowValidationScenariosRunner(window).ProbeQuickAddPaletteBindings();

    internal static void ShowQuickAddTextErrorForValidation(this FloatingWindow window)
    {
        window.SetQuickAddExpanded(true);
        window.FloatingNoteText.Text = string.Empty;
        window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal static void CollapseQuickAddForValidation(this FloatingWindow window)
    {
        window.ClearNoteFieldErrors();
        window.SetNoteMessage(string.Empty);
        window.SetQuickAddExpanded(false);
    }

    internal static MouseButtonEventArgs CreateMouseLeftButtonDownGesture() =>
        new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        };

    internal static bool IsVisualDescendantOf(DependencyObject control, DependencyObject ancestor)
    {
        for (DependencyObject? current = control; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }
        return false;
    }

    internal static string FormatNoteDateLabel(string? dateValue, DateTimeOffset nowUtc)
    {
        if (dateValue is null)
        {
            return DateDisplay.NoteGroup(null, nowUtc);
        }
        if (!DateTime.TryParseExact(
                dateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return dateValue;
        }
        return DateDisplay.NoteGroup(DateOnly.FromDateTime(date), nowUtc);
    }

    internal static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var descendant in VisualDescendants<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return descendant;
            }
        }
    }
}
