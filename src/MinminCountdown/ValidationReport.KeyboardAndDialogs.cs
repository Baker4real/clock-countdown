using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    private static MainKeyboardBehaviorResult ValidateMainKeyboardBehavior(
        MainWindow main,
        FloatingWindow floating,
        FloatingTrayController trayController,
        DesktopWorkspace workspace,
        string initialFocusedAutomationId)
    {
        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        var closeButton = Required<Button>(main, "CloseButton");
        var minimizeButton = Required<Button>(main, "MinimizeButton");
        if (Keyboard.FocusedElement is DependencyObject focusedOutside &&
            !ReferenceEquals(Window.GetWindow(focusedOutside), main))
        {
            Keyboard.ClearFocus();
        }

        main.Activate();
        PumpDispatcher();
        var initialFocusSafe =
            string.Equals(initialFocusedAutomationId, "CountdownTab", StringComparison.Ordinal) &&
            countdownTab.IsVisible &&
            countdownTab.IsEnabled &&
            !string.Equals(initialFocusedAutomationId, AutomationProperties.GetAutomationId(closeButton), StringComparison.Ordinal) &&
            !string.Equals(initialFocusedAutomationId, AutomationProperties.GetAutomationId(minimizeButton), StringComparison.Ordinal);
        var tabSequence = new List<string>();
        countdownTab.Focus();
        PumpDispatcher();
        for (var index = 0; index < 5 && Keyboard.FocusedElement is UIElement focused; index++)
        {
            tabSequence.Add(AutomationProperties.GetAutomationId(focused));
            if (index < 4)
            {
                focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                PumpDispatcher();
            }
        }

        var countdownTabPath = ValidateMainWorkspaceTabPath(main, WorkspaceKind.Countdowns);
        var notesTabPath = ValidateMainWorkspaceTabPath(main, WorkspaceKind.Notes);

        var originalWorkspace = main.CurrentWorkspace;
        var originalWindowState = main.WindowState;
        var originalEnabled = main.IsEnabled;
        var originalPreferences = workspace.Snapshot.Settings.Preferences;
        var status = Required<TextBlock>(main, "StatusText");
        var commandResults = new Dictionary<string, bool>(StringComparer.Ordinal);
        var commandDiagnostics = new Dictionary<string, MainShortcutCommandDiagnostic>(StringComparer.Ordinal);
        var commandsWork = false;
        string? savedCountdownId = null;
        string? savedNoteId = null;
        bool RaiseShortcut(ModifierKeys modifiers, Key key)
        {
            countdownTab.Focus();
            PumpDispatcher();
            return WindowValidationInput.RaisePreviewKey(countdownTab, key, modifiers);
        }

        try
        {
            var notesWorkspaceHandled = RaiseShortcut(ModifierKeys.Control, Key.D2);
            commandResults["NotesWorkspace"] =
                notesWorkspaceHandled && main.CurrentWorkspace == WorkspaceKind.Notes;
            commandDiagnostics["NotesWorkspace"] = new(
                notesWorkspaceHandled,
                main.CurrentWorkspace == WorkspaceKind.Notes,
                string.Empty);
            var countdownsWorkspaceHandled = RaiseShortcut(ModifierKeys.Control, Key.D1);
            commandResults["CountdownsWorkspace"] =
                countdownsWorkspaceHandled && main.CurrentWorkspace == WorkspaceKind.Countdowns;
            commandDiagnostics["CountdownsWorkspace"] = new(
                countdownsWorkspaceHandled,
                main.CurrentWorkspace == WorkspaceKind.Countdowns,
                string.Empty);
            var filterHandled = RaiseShortcut(ModifierKeys.Control, Key.F);
            var filterOpened = Required<Popup>(main, "FilterPopup").IsOpen;
            commandResults["Filter"] = filterHandled && filterOpened;
            commandDiagnostics["Filter"] = new(filterHandled, filterOpened, string.Empty);
            // The following shortcut is a separate scenario. Wait for the real
            // exit transition before it runs, while the popup guard is still valid.
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "FilterPopup"));
            // #156：便签页 Ctrl+F 聚焦便签搜索（倒计时页仍打开筛选，由上方 Filter 命令覆盖）。
            RaiseShortcut(ModifierKeys.Control, Key.D2);
            var noteSearchHandled = RaiseShortcut(ModifierKeys.Control, Key.F);
            var noteSearchFocused = ReferenceEquals(
                Keyboard.FocusedElement,
                Required<TextBox>(main, "NoteSearch"));
            commandResults["NoteSearch"] = noteSearchHandled && noteSearchFocused;
            commandDiagnostics["NoteSearch"] = new(
                noteSearchHandled,
                noteSearchFocused,
                string.Empty);
            RaiseShortcut(ModifierKeys.Control, Key.D1);
            PumpDispatcher();
            var tagManagerHandled = RaiseShortcut(ModifierKeys.Control, Key.T);
            var tagManagerOpened = Required<Popup>(main, "TagManagerPopup").IsOpen;
            commandResults["TagManager"] = tagManagerHandled && tagManagerOpened;
            commandDiagnostics["TagManager"] = new(
                tagManagerHandled,
                tagManagerOpened,
                string.Empty);
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "TagManagerPopup"));

            var probeCountdownName = Required<TextBox>(main, "CountdownName");
            var probeCountdownDate = Required<DatePicker>(main, "CountdownDate");
            var probeCountdownHour = Required<ComboBox>(main, "CountdownHour");
            var probeCountdownMinute = Required<ComboBox>(main, "CountdownMinute");
            var probeCountdownSecond = Required<ComboBox>(main, "CountdownSecond");
            var probeTarget = workspace.Snapshot.BeijingNow.AddDays(30);
            var probeName = $"快捷键保存-{Guid.NewGuid():N}"[..30];
            probeCountdownName.Text = probeName;
            probeCountdownDate.SelectedDate = probeTarget.Date;
            probeCountdownHour.SelectedItem = probeTarget.ToString("HH", CultureInfo.InvariantCulture);
            probeCountdownMinute.SelectedItem = probeTarget.ToString("mm", CultureInfo.InvariantCulture);
            probeCountdownSecond.SelectedItem = probeTarget.ToString("ss", CultureInfo.InvariantCulture);
            Keyboard.Focus(probeCountdownName);
            var countdownHandled = WindowValidationInput.RaisePreviewKey(probeCountdownName, Key.S, ModifierKeys.Control);
            var savedCountdown = workspace.Snapshot.Settings.Countdowns.SingleOrDefault(item =>
                string.Equals(item.Name, probeName, StringComparison.Ordinal));
            savedCountdownId = savedCountdown?.Id;
            var countdownPersisted = savedCountdown is not null &&
                DesktopWorkspace.Load(workspace.Snapshot.SettingsPath).Snapshot.Settings.Countdowns.Any(item =>
                    string.Equals(item.Id, savedCountdown.Id, StringComparison.Ordinal));
            commandResults["SaveCountdown"] =
                countdownHandled &&
                countdownPersisted &&
                string.Equals(status.Text, "倒计时已保存。", StringComparison.Ordinal);
            commandDiagnostics["SaveCountdown"] = new(
                countdownHandled,
                countdownPersisted,
                status.Text);

            main.ShowWorkspace(WorkspaceKind.Notes);
            var probeNoteText = Required<TextBox>(main, "NoteText");
            var probeNoteName = $"便签快捷保存-{Guid.NewGuid():N}";
            probeNoteText.Text = probeNoteName;
            Required<DatePicker>(main, "NoteDate").SelectedDate = workspace.Snapshot.BeijingNow.Date;
            Keyboard.Focus(probeNoteText);
            var noteHandled = WindowValidationInput.RaisePreviewKey(probeNoteText, Key.S, ModifierKeys.Control);
            var savedNote = workspace.Snapshot.Settings.Notes.SingleOrDefault(note => note.Text == probeNoteName);
            savedNoteId = savedNote?.Id;
            var notePersisted = savedNote is not null && DesktopWorkspace.Load(workspace.Snapshot.SettingsPath)
                .Snapshot.Settings.Notes.Any(note => note.Id == savedNote.Id);
            commandResults["SaveNote"] = noteHandled && notePersisted;
            commandDiagnostics["SaveNote"] = new(noteHandled, notePersisted, status.Text);
            main.ShowWorkspace(WorkspaceKind.Countdowns);

            var displayPrecision = Required<ComboBox>(main, "DisplayPrecision");
            var monthlySalary = Required<TextBox>(main, "MonthlySalary");
            var workStart = Required<TextBox>(main, "WorkStart");
            var workEnd = Required<TextBox>(main, "WorkEnd");
            displayPrecision.SelectedIndex = originalPreferences.DisplayPrecision == "Second" ? 2 : 3;
            monthlySalary.Text = "43210";
            workStart.Text = "08:15";
            workEnd.Text = "18:45";
            var preferencesHandled = RaiseShortcut(ModifierKeys.Control | ModifierKeys.Shift, Key.S);
            var persistedPreferences = DesktopWorkspace.Load(workspace.Snapshot.SettingsPath).Snapshot.Settings.Preferences;
            var preferencesPersisted =
                persistedPreferences.DisplayPrecision == (displayPrecision.SelectedIndex == 2 ? "Minute" : "Second") &&
                persistedPreferences.MonthlySalary == 43210m &&
                persistedPreferences.WorkStart == new TimeSpan(8, 15, 0) &&
                persistedPreferences.WorkEnd == new TimeSpan(18, 45, 0);
            commandResults["SavePreferences"] =
                preferencesHandled &&
                preferencesPersisted &&
                string.Equals(status.Text, "设置已保存", StringComparison.Ordinal);
            commandDiagnostics["SavePreferences"] = new(
                preferencesHandled,
                preferencesPersisted,
                status.Text);

            var mainLeft = main.Left;
            var mainTop = main.Top;
            var mainOpacity = main.Opacity;
            var floatingOpacity = floating.Opacity;
            var floatingWasVisible = floating.IsVisible;
            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            var buttonHidManagerOnly =
                !main.IsVisible &&
                floating.IsVisible &&
                trayController.IsTrayIconVisible;
            if (!main.IsVisible)
            {
                main.Show();
                main.Left = mainLeft;
                main.Top = mainTop;
                main.Opacity = mainOpacity;
                main.ShowInTaskbar = false;
                PumpDispatcher();
            }

            var minimizeHandled = RaiseShortcut(ModifierKeys.Control, Key.M);
            PumpDispatcher();
            var minimizedToTray =
                buttonHidManagerOnly &&
                minimizeHandled &&
                !main.IsVisible &&
                floating.IsVisible &&
                trayController.IsTrayIconVisible;
            if (!minimizedToTray)
            {
                throw new InvalidOperationException(
                    "#213 管理窗口最小化探针失败：" +
                    $"button={buttonHidManagerOnly} shortcut={minimizeHandled} " +
                    $"mainVisible={main.IsVisible} floatingVisible={floating.IsVisible} " +
                    $"tray={trayController.IsTrayIconVisible}");
            }
            commandResults["Minimize"] = minimizedToTray;
            commandDiagnostics["Minimize"] = new(
                minimizeHandled,
                minimizedToTray,
                string.Empty);
            if (trayController.IsTrayIconVisible)
            {
                trayController.InvokeRestoreForValidation();
                PumpDispatcher();
            }

            if (!main.IsVisible)
            {
                main.Show();
                main.Left = mainLeft;
                main.Top = mainTop;
                main.Opacity = mainOpacity;
                main.ShowInTaskbar = false;
            }

            if (floatingWasVisible && !floating.IsVisible)
            {
                floating.Show();
            }

            floating.Opacity = floatingOpacity;
            main.WindowState = WindowState.Normal;
            PumpDispatcher();
            commandsWork =
                commandResults.Count == 9 &&
                commandResults.Values.All(value => value);
        }
        finally
        {
            if (savedCountdownId is not null)
            {
                workspace.Apply(new DeleteCountdownChange(savedCountdownId));
            }
            if (savedNoteId is not null)
            {
                workspace.Apply(new DeleteNoteChange(savedNoteId));
            }

            workspace.Apply(new SavePreferencesChange(
                originalPreferences.DisplayPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd));
            Required<ComboBox>(main, "DisplayPrecision").SelectedIndex =
                originalPreferences.DisplayPrecision switch
                {
                    "Day" => 0,
                    "Hour" => 1,
                    "Minute" => 2,
                    _ => 3
                };
            Required<TextBox>(main, "MonthlySalary").Text =
                originalPreferences.MonthlySalary.ToString(CultureInfo.CurrentCulture);
            Required<TextBox>(main, "WorkStart").Text =
                originalPreferences.WorkStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            Required<TextBox>(main, "WorkEnd").Text =
                originalPreferences.WorkEnd.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "FilterPopup"));
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "TagManagerPopup"));
            main.WindowState = originalWindowState;
            main.IsEnabled = originalEnabled;
            main.ShowWorkspace(originalWorkspace);
            countdownTab.Focus();
            PumpDispatcher();
        }

        var countdownName = Required<TextBox>(main, "CountdownName");
        var originalName = countdownName.Text;
        var textSentinel = "快捷键输入保护";
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        PumpDispatcher();
        countdownName.Text = textSentinel;
        var textBoxFocused = countdownName.Focus();
        PumpDispatcher();
        var textBoxGuarded =
            textBoxFocused && countdownName.IsKeyboardFocused &&
            new[] { Key.A, Key.C, Key.V, Key.X, Key.Z }.All(key =>
                !WindowValidationInput.RaisePreviewKey(countdownName, key, ModifierKeys.Control)) &&
            !WindowValidationInput.RaisePreviewKey(countdownName, Key.F, ModifierKeys.None) &&
            WindowValidationInput.RaisePreviewKey(countdownName, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Notes &&
            string.Equals(countdownName.Text, textSentinel, StringComparison.Ordinal);
        countdownName.Text = originalName;

        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var countdownDateInput = Required<DatePicker>(main, "CountdownDate");
        var datePickerFocused = countdownDateInput.Focus();
        PumpDispatcher();
        var datePickerGuarded =
            datePickerFocused && countdownDateInput.IsKeyboardFocusWithin &&
            !WindowValidationInput.RaisePreviewKey(countdownDateInput, Key.A, ModifierKeys.None) &&
            WindowValidationInput.RaisePreviewKey(countdownDateInput, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Notes && !countdownDateInput.IsDropDownOpen;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var countdownHourInput = Required<ComboBox>(main, "CountdownHour");
        var editorWasExpanded = Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Visibility == Visibility.Visible;
        if (!editorWasExpanded)
            Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PumpDispatcher();
        var comboBoxFocused = countdownHourInput.Focus();
        PumpDispatcher();
        var comboBoxGuarded =
            comboBoxFocused && countdownHourInput.IsKeyboardFocusWithin &&
            !WindowValidationInput.RaisePreviewKey(countdownHourInput, Key.A, ModifierKeys.None) &&
            WindowValidationInput.RaisePreviewKey(countdownHourInput, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Notes;
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var textInputPreserved = textBoxGuarded && datePickerGuarded && comboBoxGuarded;
        if (!editorWasExpanded)
            Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        countdownTab.Focus();
        main.ShowWorkspace(WorkspaceKind.Countdowns);
        var filterPopup = Required<Popup>(main, "FilterPopup");
        filterPopup.IsOpen = true;
        PumpDispatcher();
        var popupGuarded =
            !WindowValidationInput.RaisePreviewKey(countdownTab, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Countdowns &&
            filterPopup.IsOpen;
        WindowValidationInput.ClosePopupForCleanup(filterPopup);

        main.IsEnabled = false;
        var modalOwnerGuarded =
            !WindowValidationInput.RaisePreviewKey(countdownTab, Key.D2, ModifierKeys.Control) &&
            main.CurrentWorkspace == WorkspaceKind.Countdowns;
        main.IsEnabled = originalEnabled;
        main.ShowWorkspace(originalWorkspace);
        countdownTab.Focus();
        PumpDispatcher();

        return new MainKeyboardBehaviorResult(
            initialFocusedAutomationId,
            initialFocusSafe,
            tabSequence,
            countdownTabPath,
            notesTabPath,
            commandsWork,
            commandResults,
            commandDiagnostics,
            textInputPreserved,
            new MainShortcutInputGuardResult(
                textBoxGuarded,
                datePickerGuarded,
                comboBoxGuarded),
            popupGuarded,
            modalOwnerGuarded);
    }

    private static MainWorkspaceTabPathResult ValidateMainWorkspaceTabPath(
        MainWindow main,
        WorkspaceKind workspace)
    {
        main.ShowWorkspace(workspace);
        main.Activate();
        PumpDispatcher();
        var expected = ExpectedMainWorkspaceTabPath(main, workspace);
        var forward = TraverseMainWorkspaceTabPath(main, expected[0], expected[^1], FocusNavigationDirection.Next);
        var backward = TraverseMainWorkspaceTabPath(main, expected[^1], expected[0], FocusNavigationDirection.Previous);
        var evaluation = EvaluateMainWorkspaceTabPath(
            expected,
            forward.Sequence,
            backward.Sequence,
            forward.FocusVisualsVisible && backward.FocusVisualsVisible);
        return new MainWorkspaceTabPathResult(
            expected,
            forward.Sequence,
            backward.Sequence,
            evaluation.ForwardMatches,
            evaluation.BackwardSymmetric,
            evaluation.FocusVisualsVisible,
            forward.FocusedTargets,
            backward.FocusedTargets);
    }

    private static MainWorkspaceTabPathEvaluation EvaluateMainWorkspaceTabPath(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> forward,
        IReadOnlyList<string> backward,
        bool focusVisualsVisible) =>
        new(
            forward.SequenceEqual(expected, StringComparer.Ordinal),
            backward.SequenceEqual(expected.Reverse(), StringComparer.Ordinal),
            focusVisualsVisible);

    private static IReadOnlyList<string> ExpectedMainWorkspaceTabPath(
        MainWindow main,
        WorkspaceKind workspace)
    {
        var ids = workspace == WorkspaceKind.Countdowns
            ? new[]
            {
                "TopmostCheck", "MinimizeButton", "CloseButton",
                "CountdownTab", "NotesTab", "PreferencesButton", "FilterButton", "TagManagerButton",
                "CountdownName", "CountdownDate", "SaveCountdownButton", "CountdownEditorExpandButton", "CountdownHour", "CountdownMinute",
                "CountdownSecond", "CountdownTags", "CountdownReminderHelp",
                "CountdownReminderNoRadio", "CountdownReminderTimeRadio",
                "CountdownReminderCustomRadio", "CancelCountdownEdit",
                "CountdownListRegion", "StatusText"
            }
            : new[]
            {
                "TopmostCheck", "MinimizeButton", "CloseButton",
                "CountdownTab", "NotesTab", "PreferencesButton", "FilterButton", "TagManagerButton",
                "NoteText", "NoteDate", "SaveNoteButton", "CancelNoteEdit",
                "ManageNoteReminderRulesButton",
                "NoteTags", "NoteSearch", "NotesListRegion", "StatusText"
            };
        return ids.Where(id =>
        {
            var element = id switch
            {
                "CountdownTags" => Required<TagSelector>(main, id).PopupTrigger,
                "NoteTags" => Required<TagSelector>(main, id).PopupTrigger,
                _ => Required<FrameworkElement>(main, id)
            };
            return element.IsVisible && element.IsEnabled && element.Focusable;
        }).ToArray();
    }

    private static MainTabTraversalResult TraverseMainWorkspaceTabPath(
        MainWindow main,
        string startId,
        string endId,
        FocusNavigationDirection direction)
    {
        var start = Required<FrameworkElement>(main, startId);
        main.Activate();
        start.Focus();
        PumpDispatcher();
        var sequence = new List<string>();
        var focusedTargets = new List<string>();
        var focusVisualsVisible = true;
        var attemptLimit = 64 + Descendants<UIElement>(main).Count(element =>
            element.Focusable && element.IsVisible && element.IsEnabled);
        for (var attempt = 0; attempt < attemptLimit && Keyboard.FocusedElement is DependencyObject focused; attempt++)
        {
            var target = ResolveMainTabTarget(main, focused);
            if (target.Id.Length == 0)
            {
                break;
            }

            if (sequence.Count == 0 || !string.Equals(sequence[^1], target.Id, StringComparison.Ordinal))
            {
                sequence.Add(target.Id);
            }

            var focusedTarget = DescribeMainFocusedTarget(main, target, focused);
            if (focusedTargets.Count == 0 ||
                !string.Equals(focusedTargets[^1], focusedTarget, StringComparison.Ordinal))
            {
                focusedTargets.Add(focusedTarget);
            }
            var focusVisible = HasVisibleMainKeyboardFocus(target, focused);
            focusVisualsVisible &= focusVisible;
            if (!focusVisible)
            {
                Console.Error.WriteLine("MAIN_TAB_FOCUS_DIAGNOSTIC " +
                    System.Text.Json.JsonSerializer.Serialize(new { direction, focusedTarget }));
            }

            if (string.Equals(target.Id, endId, StringComparison.Ordinal))
            {
                break;
            }

            if (focused is not UIElement element ||
                !element.MoveFocus(new TraversalRequest(direction)))
            {
                break;
            }
            PumpDispatcher();
        }

        return new MainTabTraversalResult(sequence, focusedTargets, focusVisualsVisible);
    }

    private static string DescribeMainFocusedTarget(
        MainWindow main,
        MainTabTarget target,
        DependencyObject focused)
    {
        if (ReferenceEquals(focused, target.Element) ||
            target.Id is not ("CountdownListRegion" or "NotesListRegion"))
        {
            return target.Id;
        }

        if (target.Id == "CountdownListRegion" && focused is Button countdownAction)
        {
            return countdownAction.Content?.ToString() switch
            {
                "编辑" => "CountdownListRegion.Edit",
                "删除" => "CountdownListRegion.Delete",
                _ => "CountdownListRegion.Button"
            };
        }

        if (target.Id == "NotesListRegion")
        {
            if (ReferenceEquals(focused, Required<ToggleButton>(main, "ActiveNotesSegment")))
            {
                return "NotesListRegion.ActiveSegment";
            }

            if (ReferenceEquals(focused, Required<ToggleButton>(main, "CompletedNotesSegment")))
            {
                return "NotesListRegion.CompletedSegment";
            }

            if (focused is Button { Uid: "NoteDateDisclosure" })
            {
                return "NotesListRegion.DateSection";
            }

            if (focused is CheckBox)
            {
                return "NotesListRegion.Completion";
            }

            if (focused is Button noteAction)
            {
                return noteAction.Content?.ToString() switch
                {
                    "编辑" => "NotesListRegion.Edit",
                    "删除" => "NotesListRegion.Delete",
                    _ => "NotesListRegion.Button"
                };
            }
        }

        return $"{target.Id}.{focused.GetType().Name}";
    }

    private static bool HasVisibleMainKeyboardFocus(
        MainTabTarget target,
        DependencyObject focused)
    {
        var focusOwner = target.Id is "CountdownListRegion" or "NotesListRegion" &&
                focused is FrameworkElement focusedElement
            ? focusedElement
            : target.Element;
        if (!ReferenceEquals(focused, focusOwner) && !IsDescendantOrSelf(focused, focusOwner))
        {
            return false;
        }

        if (focusOwner.FocusVisualStyle is not null)
        {
            return true;
        }

        if (focusOwner is not Control control || control.Template is null)
        {
            return false;
        }

        // 日期框的焦点边界画在 PART_TextBox（DatePickerTextBox）的边框属性上，不在具名 Border 上。
        control.ApplyTemplate();
        return new[] { "FocusRing", "Box", "Surface", "PART_TextBox" }
            .Select(name => control.Template.FindName(name, control))
            .Any(part => part switch
            {
                Border border => IsFocusStroke(border.IsVisible, border.BorderBrush, border.BorderThickness),
                Control inner => IsFocusStroke(inner.IsVisible, inner.BorderBrush, inner.BorderThickness),
                _ => false
            });

        static bool IsFocusStroke(bool visible, Brush? brush, Thickness thickness) =>
            visible &&
            HasVisibleBrush(brush) &&
            new[] { thickness.Left, thickness.Top, thickness.Right, thickness.Bottom }.Max() >= 2;
    }

    private static bool ValidateMainKeyboardPathNegativeFixtures()
    {
        var countdown = new[]
        {
            "CountdownTab", "NotesTab", "CountdownTags", "CountdownListRegion", "StatusText"
        };
        var notes = new[]
        {
            "CountdownTab", "NotesTab", "NoteTags", "NotesListRegion", "StatusText"
        };

        var swappedCountdown = countdown.ToArray();
        (swappedCountdown[1], swappedCountdown[2]) = (swappedCountdown[2], swappedCountdown[1]);
        return EvaluateMainWorkspaceTabPath(
                countdown, countdown, countdown.Reverse().ToArray(), true).AllPassed &&
            EvaluateMainWorkspaceTabPath(
                notes, notes, notes.Reverse().ToArray(), true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                countdown.Where(id => id != "CountdownTags").ToArray(),
                countdown,
                countdown.Reverse().ToArray(),
                true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                notes.Where(id => id != "NoteTags").ToArray(),
                notes,
                notes.Reverse().ToArray(),
                true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                countdown, swappedCountdown, countdown.Reverse().ToArray(), true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                notes, notes, notes.Reverse().Skip(1).ToArray(), true).AllPassed &&
            !EvaluateMainWorkspaceTabPath(
                notes, notes, notes.Reverse().ToArray(), false).AllPassed;
    }

    private static MainTabTarget ResolveMainTabTarget(MainWindow main, DependencyObject focused)
    {
        var countdownList = Required<ScrollViewer>(main, "CountdownListRegion");
        if (IsDescendantOrSelf(focused, countdownList))
        {
            return new MainTabTarget("CountdownListRegion", countdownList);
        }

        var notesList = Required<ScrollViewer>(main, "NotesListRegion");
        if (IsDescendantOrSelf(focused, notesList))
        {
            return new MainTabTarget("NotesListRegion", notesList);
        }

        for (var current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var id = AutomationProperties.GetAutomationId(current);
            if (id.Length > 0 && current is FrameworkElement element)
            {
                return new MainTabTarget(id, element);
            }
        }

        return new MainTabTarget(string.Empty, Required<ToggleButton>(main, "CountdownTab"));
    }

    private static bool IsDescendantOrSelf(DependencyObject candidate, DependencyObject ancestor)
    {
        for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }
        return false;
    }

    private static MainStructuralAnnouncementsResult ValidateMainStructuralAnnouncements(string assetRoot)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-structural-announcements-{Guid.NewGuid():N}.settings.json");
        MainWindow? window = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath);
            using var calendar = new HolidayCalendar(assetRoot);
            window = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(settingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            window.Show();
            PumpDispatcher();
            var status = Required<TextBlock>(window, "StatusText");
            var emptyCountdowns = Required<TextBlock>(window, "EmptyCountdownMessage");
            var emptyNotes = Required<TextBlock>(window, "EmptyNotesMessage");
            var countdownEmptyContent = Required<FrameworkElement>(window, "CountdownEmptyStateContent");
            var countdownEmptyMascot = Required<Image>(window, "CountdownEmptyMascot");
            var countdownEmptyHint = Required<TextBlock>(window, "EmptyCountdownHint");
            var addEmptyCountdown = Required<Button>(window, "AddEmptyCountdownButton");
            var notesEmptyContent = Required<FrameworkElement>(window, "NotesEmptyStateContent");
            var notesEmptyMascot = Required<Image>(window, "NotesEmptyMascot");
            var notesEmptyHint = Required<TextBlock>(window, "EmptyNotesHint");
            var addEmptyNote = Required<Button>(window, "AddEmptyNoteButton");

            window.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            var countdownWorkspaceSelectionIsAccessible =
                Required<ToggleButton>(window, "CountdownTab").IsChecked == true &&
                Required<ToggleButton>(window, "NotesTab").IsChecked == false &&
                AutomationProperties.GetName(Required<ToggleButton>(window, "CountdownTab")) == "倒计时工作区" &&
                !status.Text.Contains("当前工作区", StringComparison.Ordinal);
            var countdownFirstUseEmptyAnnounced = VisiblePoliteTextEquals(
                emptyCountdowns,
                "还没有倒计时");
            var countdownFirstUseElementsVisible =
                countdownEmptyContent.IsVisible &&
                countdownEmptyMascot.IsVisible &&
                emptyCountdowns.IsVisible &&
                countdownEmptyHint.IsVisible &&
                addEmptyCountdown.IsVisible;
            addEmptyCountdown.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            var countdownAddFocusesEditor = ReferenceEquals(
                Keyboard.FocusedElement,
                Required<TextBox>(window, "CountdownName"));
            window.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            var notesWorkspaceSelectionIsAccessible =
                Required<ToggleButton>(window, "NotesTab").IsChecked == true &&
                Required<ToggleButton>(window, "CountdownTab").IsChecked == false &&
                AutomationProperties.GetName(Required<ToggleButton>(window, "NotesTab")) == "便签工作区" &&
                !status.Text.Contains("当前工作区", StringComparison.Ordinal);
            var notesFirstUseEmptyAnnounced = VisiblePoliteTextEquals(
                emptyNotes,
                "还没有便签");
            var notesFirstUseElementsVisible =
                notesEmptyContent.IsVisible &&
                notesEmptyMascot.IsVisible &&
                emptyNotes.IsVisible &&
                notesEmptyHint.IsVisible &&
                addEmptyNote.IsVisible;
            addEmptyNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            var noteAddFocusesEditor = ReferenceEquals(
                Keyboard.FocusedElement,
                Required<TextBox>(window, "NoteText"));

            var decorationsAreNonInteractive =
                !countdownEmptyMascot.IsHitTestVisible &&
                !countdownEmptyMascot.Focusable &&
                !notesEmptyMascot.IsHitTestVisible &&
                !notesEmptyMascot.Focusable &&
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(countdownEmptyMascot)) &&
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(notesEmptyMascot));

            var countdownSaved = workspace.Apply(new SaveCountdownChange(
                null,
                "结构变化通知倒计时探针",
                workspace.Snapshot.UtcNow.AddDays(7),
                Array.Empty<string>(),
                Array.Empty<ReminderRule>())).Succeeded;
            var noteSaved = workspace.Apply(new SaveNoteChange(
                null,
                "结构变化通知便签探针",
                workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                NoReminder.Instance)).Succeeded;

            window.ShowWorkspace(WorkspaceKind.Countdowns);
            Required<ToggleButton>(window, "CountdownExpiredFilter").RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            var countdownFilterEmptyAnnounced = countdownSaved && VisiblePoliteTextEquals(
                emptyCountdowns,
                "没有符合当前筛选条件的倒计时。");
            var countdownFilterResultAnnounced =
                VisiblePoliteTextEquals(status, "筛选结果：0 个倒计时");

            window.ShowWorkspace(WorkspaceKind.Notes);
            Required<TextBox>(window, "NoteSearch").Text = "__validation_no_matching_note__";
            PumpDispatcher();
            var notesFilterEmptyAnnounced = noteSaved && VisiblePoliteTextEquals(
                emptyNotes,
                "没有符合当前筛选条件的便签。");
            var notesFilterResultAnnounced =
                VisiblePoliteTextEquals(status, "筛选结果：0 个待办");

            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            PumpDispatcher();
            foreach (var countdown in workspace.Snapshot.Settings.Countdowns.ToArray())
            {
                workspace.Apply(new DeleteCountdownChange(countdown.Id));
            }

            foreach (var note in workspace.Snapshot.Settings.Notes.ToArray())
            {
                workspace.Apply(new DeleteNoteChange(note.Id));
            }

            PumpDispatcher();

            var savedCountdown = workspace.Apply(new SaveCountdownChange(
                null,
                "空态引导恢复倒计时探针",
                workspace.Snapshot.UtcNow.AddDays(7),
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var savedCountdownId = workspace.Snapshot.Settings.Countdowns.LastOrDefault()?.Id;
            var savedNote = workspace.Apply(new SaveNoteChange(
                null,
                "空态引导恢复便签探针",
                workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
            var savedNoteId = workspace.Snapshot.Settings.Notes.LastOrDefault()?.Id;
            window.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            var populatedListsHideEmpty =
                savedCountdown.Succeeded &&
                savedNote.Succeeded &&
                countdownEmptyContent.Visibility == Visibility.Collapsed;
            window.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            populatedListsHideEmpty &= notesEmptyContent.Visibility == Visibility.Collapsed;
            if (savedCountdownId is not null)
            {
                workspace.Apply(new DeleteCountdownChange(savedCountdownId));
            }

            if (savedNoteId is not null)
            {
                workspace.Apply(new DeleteNoteChange(savedNoteId));
            }

            window.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            var restoredEmptyState = countdownEmptyContent.IsVisible;
            window.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            restoredEmptyState &= notesEmptyContent.IsVisible;

            return new MainStructuralAnnouncementsResult(
                countdownWorkspaceSelectionIsAccessible,
                notesWorkspaceSelectionIsAccessible,
                countdownFilterResultAnnounced && notesFilterResultAnnounced,
                countdownFirstUseEmptyAnnounced,
                notesFirstUseEmptyAnnounced,
                countdownFilterEmptyAnnounced,
                notesFilterEmptyAnnounced,
                new MainEmptyStateGuidanceResult(
                    countdownFirstUseElementsVisible,
                    notesFirstUseElementsVisible,
                    countdownAddFocusesEditor && noteAddFocusesEditor,
                    decorationsAreNonInteractive,
                    populatedListsHideEmpty && restoredEmptyState));
        }
        finally
        {
            window?.Close();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".diagnostic.jsonl");
        }
    }

    private static bool VisiblePoliteTextEquals(TextBlock textBlock, string expected) =>
        textBlock.IsVisible &&
        AutomationProperties.GetLiveSetting(textBlock) == AutomationLiveSetting.Polite &&
        string.Equals(textBlock.Text, expected, StringComparison.Ordinal) &&
        string.Equals(AutomationProperties.GetName(textBlock), expected, StringComparison.Ordinal);

    private static ConfirmationDialogValidation ValidateConfirmationDialog(
        ApplicationTheme theme,
        MainWindow owner,
        DateTimeOffset restoreUtc)
    {
        var scenarios = new[]
        {
            CreateConfirmationDialogScenario("Note", "删除便签", new string('签', 140)),
            CreateConfirmationDialogScenario("Countdown", "删除倒计时", new string('时', 30))
        };
        var measurements = new List<ConfirmationDialogMeasurement>();
        var originalOwnerLeft = owner.Left;
        var originalOwnerTop = owner.Top;
        var originalOwnerWidth = owner.Width;
        var originalOwnerHeight = owner.Height;
        try
        {
            var workArea = SystemParameters.WorkArea;
            // The suite injects a large logical work area for reference captures.
            // After hide/show, SSH desktops can retain that requested size while
            // the native HWND is smaller. WPF-UI centers using Width/Height, so
            // prepare this owner to fit the real desktop before measuring it.
            owner.Width = Math.Min(owner.ActualWidth, Math.Max(owner.MinWidth, workArea.Width - 48));
            owner.Height = Math.Min(owner.ActualHeight, Math.Max(owner.MinHeight, workArea.Height - 48));
            owner.UpdateLayout();
            PumpDispatcher();
            owner.Left = workArea.Left + Math.Max(24, (workArea.Width - owner.ActualWidth) / 2);
            owner.Top = workArea.Top + Math.Max(24, (workArea.Height - owner.ActualHeight) / 2);
            owner.UpdateLayout();
            PumpDispatcher();

            foreach (var scenario in scenarios)
            {
                theme.Apply();
                var dialog = new ConfirmationDialog(owner, scenario.Title, scenario.Message)
                {
                    ShowInTaskbar = false,
                    Opacity = 0
                };
                try
                {
                        dialog.Show();
                        PumpDispatcher();
                        dialog.UpdateLayout();
                        // #205: MessageBox owns the action row in its template.
                        // Measure the complete rendered surface that contains
                        // both content and actions, rather than the body grid.
                        var root = (FrameworkElement)VisualTreeHelper.GetChild(dialog, 0);
                        var rootBounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
                        var cancelBounds = dialog.CancelButton.TransformToAncestor(root).TransformBounds(
                            new Rect(0, 0, dialog.CancelButton.ActualWidth, dialog.CancelButton.ActualHeight));
                        var confirmBounds = dialog.ConfirmButton.TransformToAncestor(root).TransformBounds(
                            new Rect(0, 0, dialog.ConfirmButton.ActualWidth, dialog.ConfirmButton.ActualHeight));
                        var themed =
                            dialog.WindowStyle == WindowStyle.None &&
                            Equals(dialog.Background, Application.Current.FindResource("BackgroundBrush")) &&
                            dialog.Content is Border border &&
                            Equals(border.BorderBrush, Application.Current.FindResource("PeachLineBrush"));
                        var buttonsVisible =
                            dialog.CancelButton.IsVisible &&
                            dialog.ConfirmButton.IsVisible &&
                            dialog.CancelButton.ActualWidth > 0 &&
                            dialog.ConfirmButton.ActualWidth > 0 &&
                            rootBounds.Contains(cancelBounds) &&
                            rootBounds.Contains(confirmBounds);
                        var bodyReachable =
                            dialog.BodyText.TextWrapping == TextWrapping.Wrap &&
                            dialog.BodyScroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                            dialog.BodyScroller.ActualHeight > 0 &&
                            (dialog.BodyText.ActualHeight <= dialog.BodyScroller.ActualHeight + 0.1 ||
                             dialog.BodyScroller.ScrollableHeight > 0);
                        dialog.BodyScroller.ScrollToEnd();
                        dialog.BodyScroller.UpdateLayout();
                        PumpDispatcher();
                        var bodyEndReached =
                            dialog.BodyScroller.ScrollableHeight <= 0.1 ||
                            Math.Abs(dialog.BodyScroller.VerticalOffset - dialog.BodyScroller.ScrollableHeight) <= 0.1;
                        var centeredOnOwner =
                            Math.Abs(
                                dialog.Left + dialog.ActualWidth / 2 -
                                (owner.Left + owner.ActualWidth / 2)) <= 1 &&
                            Math.Abs(
                                dialog.Top + dialog.ActualHeight / 2 -
                                (owner.Top + owner.ActualHeight / 2)) <= 1;
                        var safeDefault =
                            dialog.CancelButton.IsDefault &&
                            dialog.CancelButton.IsCancel &&
                            !dialog.ConfirmButton.IsDefault;

                        dialog.Close();
                        PumpDispatcher();
                        var enterDidNotConfirm = RunConfirmationDialogAction(
                            owner,
                            scenario,
                            ConfirmationDialogAction.Enter);
                        var escapeDidNotConfirm = RunConfirmationDialogAction(
                            owner,
                            scenario,
                            ConfirmationDialogAction.Escape);
                        var cancelDidNotConfirm = RunConfirmationDialogAction(
                            owner,
                            scenario,
                            ConfirmationDialogAction.Cancel);

                        measurements.Add(new ConfirmationDialogMeasurement(
                            $"{scenario.ContentKind}-Day",
                            scenario.ContentKind,
                            "Day",
                            scenario.Subject.Length,
                            scenario.Message.Length,
                            string.Equals(dialog.BodyText.Text, scenario.Message, StringComparison.Ordinal),
                            themed,
                            buttonsVisible,
                            bodyReachable,
                            bodyEndReached,
                            ReferenceEquals(dialog.Owner, owner),
                            dialog.WindowStartupLocation.ToString(),
                            centeredOnOwner,
                            safeDefault,
                            enterDidNotConfirm,
                            escapeDidNotConfirm,
                            cancelDidNotConfirm));
                    }
                    finally
                    {
                        if (dialog.IsVisible)
                        {
                            dialog.Close();
                            PumpDispatcher();
                        }
                    }
                }
        }
        finally
        {
            owner.Width = originalOwnerWidth;
            owner.Height = originalOwnerHeight;
            owner.Left = originalOwnerLeft;
            owner.Top = originalOwnerTop;
            owner.UpdateLayout();
            PumpDispatcher();
            theme.Apply();
        }

        var productionMatrixComplete =
            measurements.Count == 2 &&
            measurements.Select(measurement => measurement.StateId)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    new[] { "Countdown-Day", "Note-Day" },
                    StringComparer.Ordinal) &&
            measurements.All(measurement => measurement.ProductionMessageExact);
        var gates = ValidationExercises.EvaluateConfirmationDialogGates(
            new ConfirmationDialogGateInputs(
                measurements.Count == 2 && measurements.All(measurement => measurement.ButtonsVisible),
                measurements.Count == 2 && measurements.All(measurement => measurement.BodyReachable),
                measurements.Count == 2 && measurements.All(measurement => measurement.CancelIsSafeDefault),
                productionMatrixComplete,
                measurements.Count == 2 && measurements.All(measurement => measurement.BodyEndReached),
                measurements.Count == 2 && measurements.All(measurement =>
                    measurement.OwnerMatches &&
                    string.Equals(measurement.StartupLocation, nameof(WindowStartupLocation.CenterOwner), StringComparison.Ordinal) &&
                    measurement.CenteredOnOwner),
                measurements.Count == 2 && measurements.All(measurement => measurement.EnterDidNotConfirm),
                measurements.Count == 2 && measurements.All(measurement =>
                    measurement.EscapeDidNotConfirm && measurement.CancelDidNotConfirm)));
        return new ConfirmationDialogValidation(
            measurements.Count == 2 && measurements.All(measurement => measurement.Themed),
            measurements,
            gates);
    }

    private static ConfirmationDialogScenario CreateConfirmationDialogScenario(
        string contentKind,
        string title,
        string subject) =>
        new(contentKind, title, subject, $"确定删除“{subject}”吗？");

    private static bool RunConfirmationDialogAction(
        Window owner,
        ConfirmationDialogScenario scenario,
        ConfirmationDialogAction action)
    {
        var actionDispatched = false;
        var timedOut = false;
        var dialog = new ConfirmationDialog(owner, scenario.Title, scenario.Message)
        {
            ShowInTaskbar = false,
            Opacity = 0
        };
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dialog.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (dialog.IsVisible)
            {
                timedOut = true;
                dialog.Close();
            }
        };
        dialog.ContentRendered += (_, _) =>
        {
            dialog.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (action == ConfirmationDialogAction.Cancel)
                    {
                        actionDispatched = WindowValidationInput.InvokeButton(dialog.CancelButton);
                        return;
                    }

                    dialog.Activate();
                    dialog.CancelButton.Focus();
                    var handle = new System.Windows.Interop.WindowInteropHelper(dialog).Handle;
                    var virtualKey = action == ConfirmationDialogAction.Enter ? 0x0D : 0x1B;
                    actionDispatched =
                        handle != IntPtr.Zero &&
                        NativeMethods.PostMessage(handle, 0x0100, new IntPtr(virtualKey), IntPtr.Zero) &&
                        NativeMethods.PostMessage(handle, 0x0101, new IntPtr(virtualKey), IntPtr.Zero);
                    if (!actionDispatched)
                    {
                        dialog.Close();
                    }
                }));
            timer.Start();
        };

        try
        {
            var result = dialog.ShowDialog();
            return actionDispatched && !timedOut && result != true;
        }
        finally
        {
            timer.Stop();
            if (dialog.IsVisible)
            {
                dialog.Close();
            }
            PumpDispatcher();
        }
    }

    private static SecondaryWindowTypographyResult ValidateSecondaryWindowTypography(
        MainWindow owner,
        bool floatingTypographyUnchanged,
        double floatingBodyFontSize,
        double floatingReminderFontSize)
    {
        var reminderKey = new ActiveReminderOccurrenceKey(
            "typography-validation-rule",
            new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
        var reminderCenter = new ReminderCenterWindow();
        reminderCenter.AddEntry(new ReminderCenterItem(
            reminderKey,
            new NoteRecord(
                "typography-validation-note",
                "排版验证正文",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
        {
            Owner = owner
        };
        var confirmation = new ConfirmationDialog(owner, "排版验证", "排版验证正文");
        // #150：设置恢复与激活失败窗同走次级窗层级 token，进入同一份字号报告。
        var recovery = SettingsRecoveryWindow.CreateForValidation("设置恢复排版验证正文", canRecover: true);
        var activation = InstanceActivationFailureWindow.CreateForValidation();
        var windows = new Window[] { reminderCenter, editor, confirmation, recovery, activation };
        foreach (var window in windows)
        {
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20_000;
            window.Top = -20_000;
            window.Opacity = 0;
        }

        try
        {
            foreach (var window in windows)
            {
                window.Show();
                PumpDispatcher();
                window.UpdateLayout();
            }

            var measurements = new[]
            {
                MeasureSecondaryTypography(
                    reminderCenter,
                    "ReminderCenter",
                    "ReminderCenterContentRoot",
                    "提醒中心",
                    "排版验证正文",
                    "计划：2031 年 4 月 5 日 09:00（北京时间）",
                    "便签"),
                MeasureSecondaryTypography(
                    editor,
                    "ReminderRulesEditor",
                    "ReminderRulesEditorContentRoot",
                    "提醒规则",
                    ":",
                    "规则类型",
                    "0 / 8 条"),
                MeasureSecondaryTypography(
                    confirmation,
                    "ConfirmationDialog",
                    "ConfirmationContentRoot",
                    "排版验证",
                    null,
                    "排版验证正文",
                    null),
                MeasureSecondaryTypography(
                    recovery,
                    "SettingsRecovery",
                    "SettingsRecoveryContentRoot",
                    "设置恢复",
                    null,
                    "设置恢复排版验证正文",
                    null),
                MeasureSecondaryTypography(
                    activation,
                    "InstanceActivationFailure",
                    "ActivationFailureContentRoot",
                    InstanceActivationFailureWindow.PromptHeading,
                    null,
                    InstanceActivationFailureWindow.PromptMessage,
                    null)
            };
            var gates = ValidationExercises.EvaluateSecondaryWindowTypographyGates(
                new SecondaryWindowTypographyGateInputs(
                    measurements.All(measurement => measurement.RenderedScaleMatches),
                    measurements.All(measurement => measurement.ContentRootDeclaresFontSize),
                    floatingTypographyUnchanged &&
                    Math.Abs(floatingBodyFontSize - 13) <= 0.1 &&
                    Math.Abs(floatingReminderFontSize - 13) <= 0.1));
            return new SecondaryWindowTypographyResult(
                measurements,
                gates,
                floatingBodyFontSize,
                floatingReminderFontSize);
        }
        finally
        {
            reminderCenter.ForceClose();
            editor.Close();
            confirmation.Close();
            recovery.Close();
            activation.Close();
            PumpDispatcher();
        }
    }

    private static SecondaryWindowTypographyMeasurement MeasureSecondaryTypography(
        Window window,
        string name,
        string rootAutomationId,
        string titleText,
        string? sectionText,
        string bodyText,
        string? minorText)
    {
        var root = Descendants<FrameworkElement>(window).Single(element =>
            string.Equals(
                AutomationProperties.GetAutomationId(element),
                rootAutomationId,
                StringComparison.Ordinal));
        // Window chrome may repeat the same title using its own caption scale.
        // Measure the content region named by this contract, not the outer chrome.
        var texts = Descendants<TextBlock>(root).ToArray();
        var title = texts.First(text => string.Equals(text.Text, titleText, StringComparison.Ordinal));
        var section = sectionText is null
            ? null
            : texts.First(text => string.Equals(text.Text, sectionText, StringComparison.Ordinal));
        var body = texts.First(text => string.Equals(text.Text, bodyText, StringComparison.Ordinal));
        var minor = minorText is null
            ? null
            : texts.First(text => string.Equals(text.Text, minorText, StringComparison.Ordinal));
        var rootFontSize = (double)root.GetValue(TextElement.FontSizeProperty);
        var measurement = new SecondaryWindowTypographyMeasurement(
            name,
            rootAutomationId,
            title.FontSize,
            section?.FontSize,
            body.FontSize,
            minor?.FontSize,
            rootFontSize,
            root.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue);
        return measurement;
    }

    private static ReminderKeyboardExerciseResult ValidateReminderCenterKeyboardNavigation()
    {
        var key = new ActiveReminderOccurrenceKey(
            "validation-reminder-rule",
            new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        window.AddEntry(new ReminderCenterItem(
            key,
            new NoteRecord(
                "validation-reminder-note",
                "键盘提醒验证",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        try
        {
            window.Show();
            PumpDispatcher();
            window.SetDeliveryReady(key);
            PumpDispatcher();
            // #215：出现不再抢焦点；键盘遍历断言显式激活窗口，模拟用户切到中心后的操作。
            window.Activate();
            PumpDispatcher();
            var buttons = Descendants<Button>(window).ToArray();
            var acknowledge = buttons.Single(button =>
                AutomationProperties.GetName(button).StartsWith("知道了：", StringComparison.Ordinal));
            var snooze = buttons.Single(button =>
                AutomationProperties.GetName(button).StartsWith("稍后提醒：", StringComparison.Ordinal));
            var complete = buttons.Single(button =>
                AutomationProperties.GetName(button).StartsWith("完成便签：", StringComparison.Ordinal));
            var openSource = buttons.Single(button =>
                AutomationProperties.GetName(button).StartsWith("打开便签来源：", StringComparison.Ordinal));
            var acknowledgeAll = buttons.Single(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterAcknowledgeAll",
                    StringComparison.Ordinal));
            var minimizeToTaskbar = buttons.Single(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterMinimizeToTaskbar",
                    StringComparison.Ordinal));
            var snoozeChoice = Descendants<ComboBox>(window).Single(comboBox =>
                comboBox.IsVisible &&
                AutomationProperties.GetName(comboBox).StartsWith(
                    "稍后提醒时间：",
                    StringComparison.Ordinal));
            acknowledge.Focus();
            var tabRequest = new TraversalRequest(FocusNavigationDirection.Next);
            var dispositionActionsTraverse =
                acknowledge.MoveFocus(tabRequest) &&
                snoozeChoice.IsKeyboardFocusWithin &&
                snoozeChoice.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, snooze) &&
                snooze.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, complete) &&
                complete.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, openSource);
            var snoozeChoiceFocusable =
                snoozeChoice.Focusable &&
                snoozeChoice.IsTabStop &&
                snoozeChoice.Focus();
            snooze.Focus();
            var snoozeActionsTraverse =
                snooze.IsTabStop &&
                snooze.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, complete);
            var tabTraverses =
                dispositionActionsTraverse &&
                snoozeChoiceFocusable &&
                snoozeActionsTraverse;

            // #86 AC1 回归：选中「自定义」后日期、小时、分钟进入 Tab 链且不跳出当前条目。
            var entryRow = Descendants<Border>(window).Single(border =>
                border.Tag is ActiveReminderOccurrenceKey rowKey && rowKey == key);
            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            PumpDispatcher();
            var customDate = Descendants<DatePicker>(entryRow).Single();
            var customHour = Descendants<ComboBox>(entryRow).Single(comboBox =>
                AutomationProperties.GetName(comboBox).StartsWith(
                    "自定义稍后小时：",
                    StringComparison.Ordinal));
            var customMinute = Descendants<ComboBox>(entryRow).Single(comboBox =>
                AutomationProperties.GetName(comboBox).StartsWith(
                    "自定义稍后分钟：",
                    StringComparison.Ordinal));
            openSource.Focus();
            var customVisible =
                customDate.IsVisible && customHour.IsVisible && customMinute.IsVisible;
            // 主题 ComboBox 模板内的 ToggleButton 可聚焦，会为每个下拉多占一步 Tab；
            // 因此只要求焦点按 打开来源→日期→小时→分钟 的顺序前进、允许控件内部件占步，且步步留在条目内。
            var customStage = 0; // 0=待进日期 1=日期 2=小时 3=分钟
            var customChainStaysInEntry = customVisible;
            UIElement? customMover = openSource;
            for (var step = 0; step < 12 && customStage < 3 && customChainStaysInEntry; step++)
            {
                if (customMover?.MoveFocus(tabRequest) != true ||
                    Keyboard.FocusedElement is not DependencyObject customFocused ||
                    !(ReferenceEquals(customFocused, entryRow) || entryRow.IsAncestorOf(customFocused)))
                {
                    customChainStaysInEntry = false;
                    break;
                }
                if (FocusStaysWithin(entryRow, customDate))
                {
                    customChainStaysInEntry = customStage <= 1;
                    customStage = Math.Max(customStage, 1);
                }
                else if (FocusStaysWithin(entryRow, customHour))
                {
                    customChainStaysInEntry = customStage is 1 or 2;
                    customStage = 2;
                }
                else if (FocusStaysWithin(entryRow, customMinute))
                {
                    customChainStaysInEntry = customStage is 2 or 3;
                    customStage = 3;
                }
                else
                {
                    customChainStaysInEntry = false;
                }
                customMover = customFocused as UIElement;
            }
            customChainStaysInEntry = customChainStaysInEntry && customStage == 3;

            // 切回预设项后自定义控件退出 Tab 链；条目末尾动作之后 Tab 仍绕回当前条目。
            snoozeChoice.SelectedIndex = 0;
            PumpDispatcher();
            snooze.Focus();
            var presetChainRestored =
                !customDate.IsVisible &&
                !customHour.IsVisible &&
                !customMinute.IsVisible &&
                snooze.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, complete);
            openSource.Focus();
            var tabStaysInEntry =
                openSource.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, acknowledgeAll) &&
                acknowledgeAll.MoveFocus(tabRequest) &&
                ReferenceEquals(Keyboard.FocusedElement, minimizeToTaskbar) &&
                minimizeToTaskbar.MoveFocus(tabRequest) &&
                Keyboard.FocusedElement is DependencyObject wrapped &&
                entryRow.IsAncestorOf(wrapped);
            tabTraverses =
                tabTraverses &&
                customChainStaysInEntry &&
                presetChainRestored &&
                tabStaysInEntry;

            // #150：Esc 不关闭有条目的中心，只把焦点移到「知道了」；送达失败禁用「知道了」时，
            // 焦点落到始终可用的「打开来源」，窗口同样保持打开。
            // 只 raise KeyDown：真实按键时 Button 不处理 Esc、事件冒泡到窗口处理器；
            // 窗口不能挂 PreviewKeyDown，否则会在隧道阶段吃掉 Esc，破坏日期/下拉的 Esc 关闭。
            window.SetDeliveryReady(key);
            PumpDispatcher();
            openSource.Focus();
            var escapeHandled = RaiseWindowEscapeKeyDown(window);
            PumpDispatcher();
            var escapeFocusesAcknowledge =
                escapeHandled &&
                window.IsVisible &&
                ReferenceEquals(Keyboard.FocusedElement, acknowledge);
            window.ShowDeliveryFailure(key);
            PumpDispatcher();
            openSource.Focus();
            escapeHandled = RaiseWindowEscapeKeyDown(window);
            PumpDispatcher();
            escapeFocusesAcknowledge =
                escapeFocusesAcknowledge &&
                escapeHandled &&
                window.IsVisible &&
                ReferenceEquals(Keyboard.FocusedElement, openSource);
            window.SetDeliveryReady(key);
            PumpDispatcher();

            window.Close();
            PumpDispatcher();
            return new ReminderKeyboardExerciseResult(
                tabTraverses,
                window.IsVisible,
                !window.ShowActivated &&
                !string.IsNullOrWhiteSpace(AutomationProperties.GetName(window)),
                escapeFocusesAcknowledge);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.ForceClose();
            }
        }
    }

    // #150：只 raise KeyDown 的窗口级 Esc；KeyDown 挂在窗口冒泡阶段，
    // 与真实按键「子控件不处理 Esc 时冒泡到窗口」的路径一致。
    private static bool RaiseWindowEscapeKeyDown(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source is null)
        {
            return false;
        }

        var escapeDown = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            Key.Escape)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        window.RaiseEvent(escapeDown);
        return escapeDown.Handled;
    }
}
