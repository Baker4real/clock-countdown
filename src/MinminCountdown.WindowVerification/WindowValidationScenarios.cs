using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;
internal sealed partial class MainWindowValidationScenariosRunner
{
  // Expected layout values come from DESIGN.md, not from product-private constants.
  private const double CountdownRemainingColumnWidth = 228;
  private const double CountdownRemainingUnitFontSize = 13;
  private const double NoteCardBodyLineHeight = 18;
  private const double CalendarIndexNoteCardWidth = 319;
  private readonly MainWindow window;
  private readonly DesktopWorkspace? desktopWorkspace;
  private DesktopWorkspace Workspace => desktopWorkspace ?? throw new InvalidOperationException("此验证场景需要显式 DesktopWorkspace。");

  internal MainWindowValidationScenariosRunner(MainWindow window, DesktopWorkspace? desktopWorkspace = null)
  {
      this.window = window;
      this.desktopWorkspace = desktopWorkspace;
  }

  private void ShowWorkspace(WorkspaceKind workspace) => window.ShowWorkspace(workspace);
    internal NoteExerciseResult RunNoteAddFixture() => RunNoteAddExercise();

    internal ReminderModeClickResult RunNoteReminderModeFixture() => RunReminderModeClickExercises();

    internal UndatedNoteEditProbe RunUndatedNoteEditFixture() => ProbeUndatedNoteEditRetention();

    internal MainWindowValidationScenarios RunValidationScenarios(bool exerciseNoteAdd, ApplicationTheme theme)
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
            ProbeUiPolishScenarios(),
            ProbeSettingsDraftRetention(),
            ProbeUndatedNoteEditRetention(),
            ProbeCountdownReminderModeRadioMapping(),
            ProbeCountdownReadabilityScenarios(),
            ProbeCountdownEditingScenarios(),
            ProbeNoteValidationScenarios(),
            ProbeMainWindowInteractionStates(),
            ProbeStructureScenarios(),
            ProbeKeyboardImmediateMotionScenarios(theme));
    }

    // 静态结构事实：控件名、类型与已退役控件的缺席都在窗口内判定，报告只读结论。
    private MainStructureScenarios ProbeStructureScenarios() =>
        new(
            window.FindName("NoteGroupingSelector") is null,
            window.FindName("CountdownReminderNoRadio") is RadioButton &&
                window.FindName("CountdownReminderTimeRadio") is RadioButton &&
                window.FindName("CountdownReminderCustomRadio") is RadioButton,
            window.FindName("CountdownName") is TextBox,
            window.FindName("ManageNoteReminderRulesButton") is Button &&
                window.FindName("NoteReminderRulesSummary") is TextBlock,
            window.ManageNoteReminderRulesButton.Visibility == Visibility.Visible &&
                window.NoteReminderRulesSummary.Visibility == Visibility.Visible,
            window.FindName("NoteReminder") is null &&
                window.FindName("NoteReminderSchedule") is null &&
                window.FindName("FloatingReminderIntervalPanel") is null,
            window.FindName("CountdownTags") is TagSelector &&
                window.FindName("NoteTags") is TagSelector &&
                window.FindName("FilterTagOptions") is WrapPanel,
            window.FindName("CountdownWorkspace") is Canvas &&
                window.FindName("DisplayPrecision") is ComboBox,
            window.FindName("CountdownTab") is ToggleButton,
            window.FindName("NotesTab") is ToggleButton,
            window.FindName("AnyTagMode") is ToggleButton &&
                window.FindName("AllTagMode") is ToggleButton,
            window.FindName("NoteDateFilter") is DatePicker &&
                window.FindName("NoteUndatedFilter") is ToggleButton &&
                window.FindName("ClearNoteDateFilter") is Button,
            window.FindName("NoteText") is TextBox &&
                window.FindName("SaveNoteButton") is Button,
            window.FindName("NoteDate") is DatePicker,
            window.FindName("DisplayPrecision") is ComboBox &&
                window.FindName("MonthlySalary") is TextBox &&
                window.FindName("WorkStart") is TextBox &&
                window.FindName("WorkEnd") is TextBox &&
                window.FindName("SavePreferencesButton") is Button,
            window.FindName("CountdownWorkspace") is Canvas);

    // #185/#186/#188：便签字段错误和倒计时提醒日期前置条件必须回到来源控件，
    // 并复用同一字段错误/礼貌直播语义；无效提交不应清掉草稿或打开规则编辑器。
    internal NoteValidationScenarios ProbeNoteValidationScenarios()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalText = window.NoteText.Text;
        var originalDate = window.NoteDate.SelectedDate;
        var originalEditingId = window.CaptureInstanceActivationState().EditingNoteId;
        var originalTagIds = window.NoteTags.SelectedTagIds.ToArray();
        var originalReminderRules = WindowScenarioAccess.ReadMainNoteReminderRules(window);
        var originalSaveContent = window.SaveNoteButton.Content;
        var originalCancelVisibility = window.CancelNoteEdit.Visibility;
        string? temporaryNoteId = null;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            window.Activate();
            WindowValidationInput.Pump(window.Dispatcher);
            window.ResetNoteEditor();
            window.NoteDate.SelectedDate = null;
            window.NoteText.Text = string.Empty;
            window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var bothFields =
                window.NoteTextError.Visibility == Visibility.Visible &&
                window.NoteDateError.Visibility == Visibility.Visible &&
                EditingValidationErrorIsAccessible(window.NoteTextError, window.NoteText) &&
                EditingValidationErrorIsAccessible(window.NoteDateError, window.NoteDate);
            var firstInvalidFieldFocus = window.NoteText.IsKeyboardFocusWithin;

            window.NoteText.Text = "便签字段探针";
            WindowValidationInput.Pump(window.Dispatcher);
            var dateError =
                window.NoteTextError.Visibility == Visibility.Collapsed &&
                window.NoteDateError.Visibility == Visibility.Visible;
            window.NoteDate.SelectedDate = Workspace.Snapshot.BeijingNow.Date;
            WindowValidationInput.Pump(window.Dispatcher);
            var correctionClears =
                dateError &&
                window.NoteTextError.Visibility == Visibility.Collapsed &&
                window.NoteDateError.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(window.NoteMessage.Text);

            // The runner cleanup adapter must restore the same collapsed state and
            // geometry as the product correction path, not merely erase its text.
            var cleanEditorHeight = window.NoteEditorSurface.ActualHeight;
            var cleanDateTop = window.NoteDate.TranslatePoint(new Point(0, 0), window).Y;
            var cleanSaveTop = window.SaveNoteButton.TranslatePoint(new Point(0, 0), window).Y;
            window.NoteText.Text = string.Empty;
            window.NoteDate.SelectedDate = null;
            window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.ClearNoteFieldErrors();
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var adapterRowsCollapsed =
                window.NoteTextError.Visibility == Visibility.Collapsed &&
                window.NoteDateError.Visibility == Visibility.Collapsed;
            var adapterHelpCleared =
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(window.NoteTextError)) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(window.NoteDateError)) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(window.NoteText)) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(window.NoteDate));
            var adapterHeightDelta = Math.Abs(window.NoteEditorSurface.ActualHeight - cleanEditorHeight);
            var adapterDateDelta = Math.Abs(window.NoteDate.TranslatePoint(new Point(0, 0), window).Y - cleanDateTop);
            var adapterSaveDelta = Math.Abs(window.SaveNoteButton.TranslatePoint(new Point(0, 0), window).Y - cleanSaveTop);
            correctionClears &=
                adapterRowsCollapsed && adapterHelpCleared &&
                adapterHeightDelta <= 0.01 && adapterDateDelta <= 0.01 && adapterSaveDelta <= 0.01;

            window.NoteText.Text = "便签日期探针";
            window.NoteDate.SelectedDate = Workspace.Snapshot.BeijingNow.Date;
            var reminderDue = Workspace.Snapshot.BeijingNow.AddDays(1).ToUniversalTime();
            WindowScenarioAccess.SetMainNoteReminderRules(window, new ReminderRule[]
            {
                new AbsoluteOnceReminderRule(Guid.NewGuid().ToString("N"), reminderDue)
            });
            window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var saved = Workspace.Snapshot.Settings.Notes.LastOrDefault(
                note => note.Text == "便签日期探针");
            temporaryNoteId = saved?.Id;
            var datedEditRejectsClear = false;
            if (saved is not null)
            {
                window.LoadNoteIntoEditor(saved);
                var rulesBeforeReject = WindowScenarioAccess.ReminderRuleCount(window.NoteReminderRulesSummary);
                window.NoteDate.SelectedDate = null;
                window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(window.Dispatcher);
                datedEditRejectsClear =
                    window.NoteDateError.Visibility == Visibility.Visible &&
                    window.NoteDate.IsKeyboardFocusWithin &&
                    window.NoteText.Text == "便签日期探针" &&
                    WindowScenarioAccess.ReminderRuleCount(window.NoteReminderRulesSummary) == rulesBeforeReject;
            }

            ShowWorkspace(WorkspaceKind.Countdowns);
            window.Activate();
            WindowValidationInput.Pump(window.Dispatcher);
            window.ResetCountdownEditor();
            window.CountdownName.Text = "提醒日期草稿";
            window.CountdownDate.SelectedDate = null;
            var editorsBeforeMissingDate = Application.Current.Windows
                .OfType<ReminderRulesEditorWindow>()
                .Count();
            window.CountdownReminderTimeRadio.IsChecked = true;
            window.ApplyCountdownReminderModeForValidation(CountdownReminderMode.AtDeadline);
            WindowValidationInput.Pump(window.Dispatcher);
            var timeFocusesDate = window.CountdownDate.IsKeyboardFocusWithin;
            window.CountdownReminderCustomRadio.IsChecked = true;
            window.ApplyCountdownReminderModeForValidation(CountdownReminderMode.Custom);
            WindowValidationInput.Pump(window.Dispatcher);
            var customFocusesDate =
                window.CountdownDate.IsKeyboardFocusWithin &&
                window.CountdownName.Text == "提醒日期草稿" &&
                window.CountdownReminderNoRadio.IsChecked == true &&
                WindowScenarioAccess.ReminderRuleCount(window.CountdownReminderRulesSummary) == 0 &&
                Application.Current.Windows.OfType<ReminderRulesEditorWindow>().Count() ==
                    editorsBeforeMissingDate;

            return new NoteValidationScenarios(
                bothFields,
                firstInvalidFieldFocus,
                correctionClears,
                datedEditRejectsClear,
                timeFocusesDate && customFocusesDate);
        }
        finally
        {
            if (temporaryNoteId is not null)
            {
                Workspace.Apply(new DeleteNoteChange(temporaryNoteId));
            }

            WindowScenarioAccess.RestoreMainNoteEditor(
                window, Workspace, originalEditingId, originalText, originalDate);
            window.NoteTags.SetSelection(originalTagIds);
            WindowScenarioAccess.SetMainNoteReminderRules(window, originalReminderRules);
            window.SaveNoteButton.Content = originalSaveContent;
            window.CancelNoteEdit.Visibility = originalCancelVisibility;
            window.ClearNoteFieldErrors();
            UiAccessibility.Announce(window.NoteMessage, string.Empty);
            window.ResetCountdownEditor();
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private bool ProbeCountdownReminderModeRadioMapping()
    {
        var createdCountdownIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            window.ResetCountdownEditor();
            WindowValidationInput.Pump(window.Dispatcher);

            var addModeDefaultsToNone =
                window.CountdownReminderNoRadio.IsChecked == true &&
                window.CountdownReminderTimeRadio.IsChecked != true &&
                window.CountdownReminderCustomRadio.IsChecked != true &&
                WindowScenarioAccess.ReminderRuleCount(window.CountdownReminderRulesSummary) == 0;

            window.CountdownReminderTimeRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var atDeadlineTarget = WindowScenarioAccess.GetCountdownTargetUtc(window);
            var timeModeCreatesAtDeadlineOnce =
                atDeadlineTarget is not null &&
                window.CountdownReminderTimeRadio.IsChecked == true &&
                window.CountdownReminderNoRadio.IsChecked != true &&
                MainWindow.DeriveCountdownReminderMode(WindowScenarioAccess.ReadMainCountdownReminderRules(window), atDeadlineTarget) ==
                    CountdownReminderMode.AtDeadline;

            var currentTargetTime = TimeInputBehavior.TryParseStrict(
                window.CountdownTime.Text,
                TimeInputFormat.HourMinuteSecond,
                out var parsedTargetTime)
                ? parsedTargetTime
                : TimeSpan.Zero;
            TimeInputBehavior.SetValue(
                window.CountdownTime,
                TimeInputBehavior.ComputeStepped(
                    currentTargetTime,
                    TimeInputSegment.Second,
                    7,
                    TimeInputFormat.HourMinuteSecond));
            WindowValidationInput.Pump(window.Dispatcher);
            var shiftedTarget = WindowScenarioAccess.GetCountdownTargetUtc(window);
            var targetShiftKeepsAtDeadlineOnce =
                shiftedTarget is not null &&
                atDeadlineTarget is not null &&
                shiftedTarget != atDeadlineTarget &&
                MainWindow.DeriveCountdownReminderMode(WindowScenarioAccess.ReadMainCountdownReminderRules(window), shiftedTarget) ==
                    CountdownReminderMode.AtDeadline;

            window.CountdownReminderNoRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var noneModeClearsRules =
                window.CountdownReminderNoRadio.IsChecked == true &&
                window.CountdownReminderTimeRadio.IsChecked != true &&
                WindowScenarioAccess.ReminderRuleCount(window.CountdownReminderRulesSummary) == 0;

            var noReminderName = UniqueProbeName("单选探针-不提醒");
            window.CountdownName.Text = noReminderName;
            window.SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            TrackProbeCountdown(createdCountdownIds, noReminderName);
            var noReminderPersists = SnapshotCountdown(noReminderName)?.ReminderRules.Count == 0;

            window.ResetCountdownEditor();
            WindowValidationInput.Pump(window.Dispatcher);
            window.CountdownReminderTimeRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var savedAtDeadlineTarget = WindowScenarioAccess.GetCountdownTargetUtc(window);
            var atDeadlineName = UniqueProbeName("单选探针-到点");
            window.CountdownName.Text = atDeadlineName;
            window.SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            TrackProbeCountdown(createdCountdownIds, atDeadlineName);
            var atDeadlineRecord = SnapshotCountdown(atDeadlineName);
            var atDeadlinePersists =
                savedAtDeadlineTarget is not null &&
                atDeadlineRecord?.ReminderRules is [AbsoluteOnceReminderRule persistedOnce] &&
                persistedOnce.ScheduledUtc == savedAtDeadlineTarget.Value;

            window.ResetCountdownEditor();
            WindowValidationInput.Pump(window.Dispatcher);
            var customEditorOpenedAndCancelled = false;
            var customEditorTimedOut = false;
            var customEditorTimeout = new DispatcherTimer(DispatcherPriority.ApplicationIdle, window.Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            customEditorTimeout.Tick += (_, _) =>
            {
                customEditorTimeout.Stop();
                customEditorTimedOut = true;
                foreach (var editor in window.OwnedWindows.OfType<ReminderRulesEditorWindow>().ToArray())
                {
                    editor.Close();
                }
            };
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    var editors = Application.Current.Windows
                        .OfType<ReminderRulesEditorWindow>()
                        .Where(candidate => ReferenceEquals(candidate.Owner, window))
                        .ToArray();
                    customEditorOpenedAndCancelled = editors.Length == 1 && editors[0].IsVisible;
                    if (editors.Length == 1)
                    {
                        var cancel = ProbeVisualDescendants<Button>(editors[0])
                            .Single(button => string.Equals(
                                AutomationProperties.GetAutomationId(button),
                                "CancelReminderRulesButton",
                                StringComparison.Ordinal));
                        customEditorOpenedAndCancelled &= WindowValidationInput.InvokeButton(cancel);
                    }
                }));
            customEditorTimeout.Start();
            try
            {
                window.CountdownReminderCustomRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            finally
            {
                customEditorTimeout.Stop();
            }
            WindowValidationInput.Pump(window.Dispatcher);
            var customCancelRestoresDerivedMode =
                customEditorOpenedAndCancelled &&
                !customEditorTimedOut &&
                window.CountdownReminderCustomRadio.IsChecked != true &&
                window.CountdownReminderNoRadio.IsChecked == true &&
                WindowScenarioAccess.ReminderRuleCount(window.CountdownReminderRulesSummary) == 0;

            var beijingNow = Workspace.Snapshot.BeijingNow;
            // 反显探针记录携带提醒规则，规则首鸣时间不允许落在当前北京时间之前；
            // 固定取明天 15:40，避免本日 15:10 之后运行验证时保存被拒。
            var editReflectionDate = beijingNow.Date.AddDays(1);
            var editTargetUtc = new DateTimeOffset(
                editReflectionDate.Year,
                editReflectionDate.Month,
                editReflectionDate.Day,
                15,
                40,
                0,
                beijingNow.Offset).ToUniversalTime();
            var noneRecordName = UniqueProbeName("反显探针-无规则");
            var atTimeRecordName = UniqueProbeName("反显探针-到点");
            var customRecordName = UniqueProbeName("反显探针-自定义");
            var noneRecordSaved = Workspace.Apply(new SaveCountdownChange(
                null,
                noneRecordName,
                editTargetUtc,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var atTimeRecordSaved = Workspace.Apply(new SaveCountdownChange(
                null,
                atTimeRecordName,
                editTargetUtc,
                Array.Empty<string>(),
                new ReminderRule[]
                {
                    new AbsoluteOnceReminderRule(Guid.NewGuid().ToString("N"), editTargetUtc)
                }));
            var customRecordSaved = Workspace.Apply(new SaveCountdownChange(
                null,
                customRecordName,
                editTargetUtc,
                Array.Empty<string>(),
                new ReminderRule[]
                {
                    new FixedIntervalReminderRule(
                        Guid.NewGuid().ToString("N"),
                        editTargetUtc.AddMinutes(-30),
                        new ReminderInterval(30))
                }));
            TrackProbeCountdown(createdCountdownIds, noneRecordName);
            TrackProbeCountdown(createdCountdownIds, atTimeRecordName);
            TrackProbeCountdown(createdCountdownIds, customRecordName);
            var editReflectionRecordsExist =
                noneRecordSaved.Succeeded &&
                atTimeRecordSaved.Succeeded &&
                customRecordSaved.Succeeded;

            var noneRecord = SnapshotCountdown(noneRecordName);
            var atTimeRecord = SnapshotCountdown(atTimeRecordName);
            var customRecord = SnapshotCountdown(customRecordName);
            if (noneRecord is null || atTimeRecord is null || customRecord is null)
            {
                return false;
            }

            window.BeginEditCountdown(noneRecord);
            WindowValidationInput.Pump(window.Dispatcher);
            var editNoRulesRestoresNone =
                window.CountdownReminderNoRadio.IsChecked == true &&
                window.CountdownReminderTimeRadio.IsChecked != true &&
                window.CountdownReminderCustomRadio.IsChecked != true &&
                WindowScenarioAccess.ReminderRuleCount(window.CountdownReminderRulesSummary) == 0;

            window.BeginEditCountdown(atTimeRecord);
            WindowValidationInput.Pump(window.Dispatcher);
            var editAtDeadlineOnceRestoresAtTime =
                window.CountdownReminderTimeRadio.IsChecked == true &&
                window.CountdownReminderNoRadio.IsChecked != true &&
                window.CountdownReminderCustomRadio.IsChecked != true &&
                MainWindow.DeriveCountdownReminderMode(WindowScenarioAccess.ReadMainCountdownReminderRules(window), WindowScenarioAccess.GetCountdownTargetUtc(window)) ==
                    CountdownReminderMode.AtDeadline;

            window.BeginEditCountdown(customRecord);
            WindowValidationInput.Pump(window.Dispatcher);
            var editOtherRulesRestoreCustom =
                window.CountdownReminderCustomRadio.IsChecked == true &&
                window.CountdownReminderNoRadio.IsChecked != true &&
                window.CountdownReminderTimeRadio.IsChecked != true;

            window.ResetCountdownEditor();
            WindowValidationInput.Pump(window.Dispatcher);

            var result = addModeDefaultsToNone &&
                timeModeCreatesAtDeadlineOnce &&
                targetShiftKeepsAtDeadlineOnce &&
                noneModeClearsRules &&
                noReminderPersists &&
                atDeadlinePersists &&
                customCancelRestoresDerivedMode &&
                editReflectionRecordsExist &&
                editNoRulesRestoresNone &&
                editAtDeadlineOnceRestoresAtTime &&
                editOtherRulesRestoreCustom;
            return result;
        }
        finally
        {
            foreach (var id in createdCountdownIds)
            {
                Workspace.Apply(new DeleteCountdownChange(id));
            }
        }
    }

    private static string UniqueProbeName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..30];

    private static IEnumerable<T> ProbeVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in ProbeVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private CountdownRecord? SnapshotCountdown(string name) =>
        Workspace.Snapshot.Settings.Countdowns.LastOrDefault(record =>
            string.Equals(record.Name, name, StringComparison.Ordinal));

    private void TrackProbeCountdown(ICollection<string> createdIds, string name)
    {
        var record = SnapshotCountdown(name);
        if (record is not null)
        {
            createdIds.Add(record.Id);
        }
    }

    private UndatedNoteEditProbe ProbeUndatedNoteEditRetention()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var temporaryNoteIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);

            // 新建路径：空日期仍被拒绝且草稿保留
            window.ResetNoteEditor();
            WindowValidationInput.Pump(window.Dispatcher);
            window.NoteText.Text = "无日期新建拒绝探针";
            window.NoteDate.SelectedDate = null;
            var noteCountBeforeNewReject = Workspace.Snapshot.Settings.Notes.Count;
            window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var newPathMessage = window.NoteMessage.Text;
            var newNoteRejectsEmptyDate =
                Workspace.Snapshot.Settings.Notes.Count == noteCountBeforeNewReject &&
                newPathMessage.Contains("请选择便签日期", StringComparison.Ordinal) &&
                string.Equals(window.NoteText.Text, "无日期新建拒绝探针", StringComparison.Ordinal);

            // 编辑既存无日期便签：日期留空可保存，保存后仍保持未设置日期
            var undatedText = $"无日期编辑探针-{Guid.NewGuid():N}"[..24];
            var addUndated = Workspace.Apply(new SaveNoteChange(
                null,
                undatedText,
                null,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var undatedNote = Workspace.Snapshot.Settings.Notes
                .FirstOrDefault(note => string.Equals(note.Text, undatedText, StringComparison.Ordinal));
            if (undatedNote is not null)
            {
                temporaryNoteIds.Add(undatedNote.Id);
            }

            var undatedEditSavesWithoutDate = false;
            if (addUndated.Succeeded && undatedNote is not null)
            {
                window.LoadNoteIntoEditor(undatedNote);
                WindowValidationInput.Pump(window.Dispatcher);
                window.NoteText.Text = $"{undatedText}-已编辑";
                window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(window.Dispatcher);
                var savedUndated = Workspace.Snapshot.Settings.Notes
                    .FirstOrDefault(note => note.Id == undatedNote.Id);
                undatedEditSavesWithoutDate =
                    savedUndated is not null &&
                    savedUndated.Date is null &&
                    string.Equals(savedUndated.Text, $"{undatedText}-已编辑", StringComparison.Ordinal) &&
                    string.Equals(window.StatusText.Text, "便签已保存。", StringComparison.Ordinal);
            }

            // 编辑既有日期便签：清空日期被拒绝，草稿保留，文案与新建路径可区分
            var datedText = $"有日期编辑探针-{Guid.NewGuid():N}"[..24];
            var addDated = Workspace.Apply(new SaveNoteChange(
                null,
                datedText,
                Workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var datedNote = Workspace.Snapshot.Settings.Notes
                .FirstOrDefault(note => string.Equals(note.Text, datedText, StringComparison.Ordinal));
            if (datedNote is not null)
            {
                temporaryNoteIds.Add(datedNote.Id);
            }

            var datedEditRejectsClearedDate = false;
            var editPathMessage = string.Empty;
            if (addDated.Succeeded && datedNote is not null)
            {
                window.LoadNoteIntoEditor(datedNote);
                WindowValidationInput.Pump(window.Dispatcher);
                window.NoteText.Text = $"{datedText}-已编辑";
                    window.NoteDate.SelectedDate = null;
                        window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(window.Dispatcher);
                editPathMessage = window.NoteMessage.Text;
                var unchangedDated = Workspace.Snapshot.Settings.Notes
                    .FirstOrDefault(note => note.Id == datedNote.Id);
                datedEditRejectsClearedDate =
                    unchangedDated is not null &&
                    string.Equals(unchangedDated.Text, datedText, StringComparison.Ordinal) &&
                    string.Equals(unchangedDated.Date, datedNote.Date, StringComparison.Ordinal) &&
                    editPathMessage.Contains("不能清空日期", StringComparison.Ordinal) &&
                    string.Equals(window.NoteText.Text, $"{datedText}-已编辑", StringComparison.Ordinal) &&
                    string.Equals(window.CaptureInstanceActivationState().EditingNoteId, datedNote.Id, StringComparison.Ordinal);
            }

            var messagesDistinguished =
                !string.IsNullOrEmpty(newPathMessage) &&
                !string.IsNullOrEmpty(editPathMessage) &&
                !string.Equals(newPathMessage, editPathMessage, StringComparison.Ordinal);

            return new UndatedNoteEditProbe(
                newNoteRejectsEmptyDate,
                undatedEditSavesWithoutDate,
                datedEditRejectsClearedDate,
                messagesDistinguished);
        }
        finally
        {
            foreach (var temporaryNoteId in temporaryNoteIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Workspace.Apply(new DeleteNoteChange(temporaryNoteId));
            }

            window.ResetNoteEditor();
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private SettingsDraftRetentionProbe ProbeSettingsDraftRetention()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalPreferences = Workspace.Snapshot.Settings.Preferences;
        var temporaryNoteIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);

            var draftPrecisionIndex = string.Equals(
                originalPreferences.DisplayPrecision,
                "Second",
                StringComparison.Ordinal)
                ? 2
                : 3;
            const string draftSalary = "98765";
            const string draftWorkStart = "07:30";
            const string draftWorkEnd = "19:45";
            window.DisplayPrecision.SelectedIndex = draftPrecisionIndex;
            window.MonthlySalary.Text = draftSalary;
            window.WorkStart.Text = draftWorkStart;
            window.WorkEnd.Text = draftWorkEnd;

            // 后台刷新来源一：筛选切换
            var filter = Workspace.Snapshot.Filter;
            Workspace.ApplyFilter(filter with { NoteSearch = "settings-draft-probe" });
            WindowValidationInput.Pump(window.Dispatcher);
            Workspace.ApplyFilter(filter);
            WindowValidationInput.Pump(window.Dispatcher);

            // 后台刷新来源二：快速新增便签写盘（与悬浮窗快速新增共用同一工作区变更）
            var probeNoteText = $"设置草稿探针便签-{Guid.NewGuid():N}"[..24];
            var addResult = Workspace.Apply(new SaveNoteChange(
                null,
                probeNoteText,
                Workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            WindowValidationInput.Pump(window.Dispatcher);
            var probeNote = Workspace.Snapshot.Settings.Notes.FirstOrDefault(note =>
                string.Equals(note.Text, probeNoteText, StringComparison.Ordinal));
            if (probeNote is not null)
            {
                temporaryNoteIds.Add(probeNote.Id);
            }

            // 后台刷新来源三：便签勾选完成写盘
            if (probeNote is not null)
            {
                Workspace.Apply(new SetNoteCompletionChange(probeNote.Id, true));
                WindowValidationInput.Pump(window.Dispatcher);
            }

            var inputsRetainedOnRefresh =
                window.DisplayPrecision.SelectedIndex == draftPrecisionIndex &&
                string.Equals(window.MonthlySalary.Text, draftSalary, StringComparison.Ordinal) &&
                string.Equals(window.WorkStart.Text, draftWorkStart, StringComparison.Ordinal) &&
                string.Equals(window.WorkEnd.Text, draftWorkEnd, StringComparison.Ordinal);

            window.SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var savedPreferences = Workspace.Snapshot.Settings.Preferences;
            var savedPrecisionIndex = savedPreferences.DisplayPrecision switch
            {
                "Day" => 0,
                "Hour" => 1,
                "Minute" => 2,
                _ => 3
            };
            var savedValuesReflectedAfterSave =
                addResult.Succeeded &&
                savedPreferences.MonthlySalary == 98765m &&
                savedPreferences.WorkStart == new TimeSpan(7, 30, 0) &&
                savedPreferences.WorkEnd == new TimeSpan(19, 45, 0) &&
                window.DisplayPrecision.SelectedIndex == savedPrecisionIndex &&
                // #148：月薪输入显示与「今日已赚」同一套本地数字格式（N2）。
                string.Equals(
                    window.MonthlySalary.Text,
                    savedPreferences.MonthlySalary.ToString("N2", CultureInfo.CurrentCulture),
                    StringComparison.Ordinal) &&
                string.Equals(
                    window.WorkStart.Text,
                    CountdownFormatting.FormatWorkTime(savedPreferences.WorkStart),
                    StringComparison.Ordinal) &&
                string.Equals(
                    window.WorkEnd.Text,
                    CountdownFormatting.FormatWorkTime(savedPreferences.WorkEnd),
                    StringComparison.Ordinal);

            window.MonthlySalary.Text = "-500";
            window.SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var negativeSalaryRejectedKeepsDraft =
                Workspace.Snapshot.Settings.Preferences.MonthlySalary == 98765m &&
                string.Equals(window.MonthlySalary.Text, "-500", StringComparison.Ordinal) &&
                string.Equals(window.StatusText.Text, "月薪不能小于 0。", StringComparison.Ordinal);

            window.MonthlySalary.Text = "0";
            window.SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var zeroSalaryStillSavesAsUnset =
                Workspace.Snapshot.Settings.Preferences.MonthlySalary == 0m &&
                string.Equals(window.StatusText.Text, "设置已保存", StringComparison.Ordinal);

            return new SettingsDraftRetentionProbe(
                inputsRetainedOnRefresh,
                savedValuesReflectedAfterSave,
                negativeSalaryRejectedKeepsDraft,
                zeroSalaryStillSavesAsUnset);
        }
        finally
        {
            foreach (var temporaryNoteId in temporaryNoteIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Workspace.Apply(new DeleteNoteChange(temporaryNoteId));
            }

            var currentPreferences = Workspace.Snapshot.Settings.Preferences;
            if (currentPreferences != originalPreferences)
            {
                Workspace.Apply(new SavePreferencesChange(
                    originalPreferences.DisplayPrecision,
                    originalPreferences.MonthlySalary,
                    originalPreferences.WorkStart,
                    originalPreferences.WorkEnd));
            }

            window.LoadPreferences(Workspace);
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private (bool PointerResetRestoresResults, bool KeyboardResetRestoresResults)
        ProbeCountdownEmptyResetBehavior(string noResultsMessage)
    {
        var pointerResetRestoresResults = true;
        var keyboardResetRestoresResults = true;
        if (Workspace.Snapshot.Settings.Countdowns.Count > 0)
        {
            var savedSearch = window.NoteSearch.Text;
            window.SetCountdownFilter(CountdownStatusFilter.Expired);
            WindowValidationInput.Pump(window.Dispatcher);
            pointerResetRestoresResults =
                window.EmptyCountdownMessage.Visibility == Visibility.Visible &&
                window.ResetEmptyCountdownFiltersButton.Visibility == Visibility.Visible &&
                string.Equals(window.EmptyCountdownMessage.Text, noResultsMessage, StringComparison.Ordinal);
            window.ResetEmptyCountdownFiltersButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            pointerResetRestoresResults &= CountdownEmptyResetRestoredResults();

            window.SetCountdownFilter(CountdownStatusFilter.Expired);
            WindowValidationInput.Pump(window.Dispatcher);
            window.ResetEmptyCountdownFiltersButton.Focus();
            WindowValidationInput.Pump(window.Dispatcher);
            keyboardResetRestoresResults =
                ReferenceEquals(Keyboard.FocusedElement, window.ResetEmptyCountdownFiltersButton) &&
                WindowValidationInput.RaiseKey(window.ResetEmptyCountdownFiltersButton, Key.Space);
            WindowValidationInput.Pump(window.Dispatcher);
            keyboardResetRestoresResults &= CountdownEmptyResetRestoredResults();
            window.NoteSearch.Text = savedSearch;
        }

        return (pointerResetRestoresResults, keyboardResetRestoresResults);
    }

    internal (bool PointerResetRestoresResults, bool KeyboardResetRestoresResults)
        RunCountdownEmptyResetFixture()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalSearch = window.NoteSearch.Text;
        var originalFilter = Workspace.Snapshot.Filter;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            return ProbeCountdownEmptyResetBehavior(MainWindow.CountdownEmptyStatePresentation(true).Message);
        }
        finally
        {
            window.NoteSearch.Text = originalSearch;
            Workspace.ApplyFilter(originalFilter);
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private MainUiPolishScenarios ProbeUiPolishScenarios()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalSearch = window.NoteSearch.Text;
        var originalFilter = Workspace.Snapshot.Filter;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);

            var manageBounds = ValidationBounds(window.ManageNoteReminderRulesButton);
            var summaryBounds = ValidationBounds(window.NoteReminderRulesSummary);
            var messageBounds = ValidationBounds(window.NoteMessage);
            var editorFieldsDoNotOverlap =
                window.ManageNoteReminderRulesButton.IsVisible &&
                window.NoteReminderRulesSummary.IsVisible &&
                !manageBounds.IntersectsWith(summaryBounds) &&
                !manageBounds.IntersectsWith(messageBounds) &&
                !summaryBounds.IntersectsWith(messageBounds);
            var legacyInlineReminderControlsStayHidden =
                ProbeLegacyInlineReminderControlsStayHidden();
            var editorLabelsVisible = new[]
            {
                window.NoteTextLabel,
                window.NoteDateLabel,
                window.NoteReminderModeLabel,
                window.NoteTagsLabel,
                window.NoteSearchLabel
            }.All(label => label.IsVisible && !string.IsNullOrWhiteSpace(label.Text));

            var filterPopup = WindowValidationInput.ExercisePopup(
                window.FilterPopup,
                window.FilterButton,
                WindowScenarioAccess.FilterInitialFocusTarget(window),
                window.NoteSearch);
            var tagManagerPopup = WindowValidationInput.ExercisePopup(
                window.TagManagerPopup,
                window.TagManagerButton,
                window.NewTagInput,
                window.NoteSearch);
            var tagSelectorPopup = WindowValidationInput.ExercisePopup(
                window.NoteTags.Popup,
                window.NoteTags.PopupTrigger,
                window.NoteTags.PopupInitialFocus,
                window.NoteSearch);

            var firstUse = MainWindow.NoteEmptyStatePresentation(false, false, false);
            var noResults = MainWindow.NoteEmptyStatePresentation(true, true, false);
            var emptyStatesDistinguished =
                !string.Equals(firstUse.Message, noResults.Message, StringComparison.Ordinal) &&
                !firstUse.ShowReset &&
                noResults.ShowReset;
            var noResultsResetWorks = true;
            if (Workspace.Snapshot.Settings.Notes.Count > 0)
            {
                window.NoteSearch.Text = $"__validation_no_results_{Guid.NewGuid():N}";
                WindowValidationInput.RaisePreviewKey(window.NoteSearch, Key.Enter);
                WindowValidationInput.Pump(window.Dispatcher);
                noResultsResetWorks =
                    window.EmptyNotesMessage.Visibility == Visibility.Visible &&
                    string.Equals(window.EmptyNotesMessage.Text, noResults.Message, StringComparison.Ordinal) &&
                    window.ResetEmptyNotesFiltersButton.Visibility == Visibility.Visible;
                window.ResetEmptyNotesFiltersButton.RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(window.Dispatcher);
                noResultsResetWorks &=
                    string.IsNullOrEmpty(window.NoteSearch.Text) &&
                    window.ResetEmptyNotesFiltersButton.Visibility == Visibility.Collapsed;
            }

            ShowWorkspace(WorkspaceKind.Countdowns);
            window.UpdateLayout();
            var countdownEmptyStates =
                MainWindow.CountdownEmptyStatePresentation(false);
            var countdownNoResults =
                MainWindow.CountdownEmptyStatePresentation(true);
            var countdownEmptyStatesDistinguished =
                !string.Equals(countdownEmptyStates.Message, countdownNoResults.Message, StringComparison.Ordinal) &&
                !countdownEmptyStates.ShowReset &&
                countdownNoResults.ShowReset;
            var (countdownEmptyPointerResetWorks, countdownEmptyKeyboardResetWorks) =
                ProbeCountdownEmptyResetBehavior(countdownNoResults.Message);
            var countdownLayoutMatches = window.CountdownRemainingLayoutMatches;
            var countdownRemainingSamples = ProbeCountdownRemainingSamples();
            var countdownIncrementalRefresh = ProbeCountdownIncrementalRefresh();
            var noteListPolish = ProbeNoteListPolishScenarios();
            var noteCompletionStyleScoped = window.MainNoteCompletionStyleIsScoped;
            var originalSearchForSync = window.NoteSearch.Text;
            Workspace.ApplyFilter(originalFilter with { NoteSearch = "sync-from-outside" });
            WindowValidationInput.Pump(window.Dispatcher);
            var searchFilledFromFilter = string.Equals(
                window.NoteSearch.Text,
                "sync-from-outside",
                StringComparison.Ordinal);
            Workspace.ApplyFilter(originalFilter with { NoteSearch = string.Empty });
            WindowValidationInput.Pump(window.Dispatcher);
            var searchClearedFromFilter = string.IsNullOrEmpty(window.NoteSearch.Text);
            window.NoteSearch.Text = originalSearchForSync;
            Workspace.ApplyFilter(originalFilter);
            var noteSearchSyncsFromSharedFilter = searchFilledFromFilter && searchClearedFromFilter;
            // #154：视觉铬探针（漏斗/图标描边/矢量窗铬/胶囊圆角/装饰层/方角铬/页签次序）。
            var visualChrome = ProbeVisualChromeScenarios();

            return new MainUiPolishScenarios(
                editorFieldsDoNotOverlap,
                legacyInlineReminderControlsStayHidden,
                editorLabelsVisible,
                // #211：首选尺寸（MainWindow.PreferredWidth/Height，现为 864×1000）而非钉死尺寸，
                // 最小尺寸放宽为 720×560，窗口随工作区收缩时业务内容滚动而非缩放。
                // Native pixel rounding can report Width=863.2 at 125% DPI
                // while WPF still lays out the required DIP surface.
                window.ActualWidth >= window.MinWidth &&
                window.ActualHeight >= window.MinHeight &&
                Math.Abs(window.MinWidth - 720) <= 0.1 &&
                Math.Abs(window.MinHeight - 560) <= 0.1 &&
                Math.Abs(MainWindow.PreferredWidth - 864) <= 0.1 &&
                Math.Abs(MainWindow.PreferredHeight - 1000) <= 0.1,
                filterPopup.EscapeReturnsFocus &&
                tagManagerPopup.EscapeReturnsFocus &&
                tagSelectorPopup.EscapeReturnsFocus,
                filterPopup.OutsideFocusPreserved &&
                tagManagerPopup.OutsideFocusPreserved &&
                tagSelectorPopup.OutsideFocusPreserved,
                filterPopup.TriggerSecondClickCloses &&
                tagManagerPopup.TriggerSecondClickCloses &&
                tagSelectorPopup.TriggerSecondClickCloses,
                filterPopup.EscapeFromTriggerClosesAndKeepsFocus &&
                tagManagerPopup.EscapeFromTriggerClosesAndKeepsFocus &&
                tagSelectorPopup.EscapeFromTriggerClosesAndKeepsFocus,
                emptyStatesDistinguished,
                noResultsResetWorks,
                countdownEmptyStatesDistinguished,
                countdownEmptyPointerResetWorks,
                countdownEmptyKeyboardResetWorks,
                countdownLayoutMatches,
                countdownRemainingSamples,
                countdownIncrementalRefresh,
                noteCompletionStyleScoped,
                noteSearchSyncsFromSharedFilter,
                noteListPolish,
                visualChrome);
        }
        finally
        {
            window.FilterPopup.IsOpen = false;
            window.TagManagerPopup.IsOpen = false;
            window.NoteTags.IsPopupOpen = false;
            Workspace.ApplyFilter(originalFilter);
            window.AnyTagMode.IsChecked = originalFilter.MatchMode == TagMatchMode.Any;
            window.AllTagMode.IsChecked = originalFilter.MatchMode == TagMatchMode.All;
            window.UntaggedOnly.IsChecked = originalFilter.UntaggedOnly;
            window.CountdownAllFilter.IsChecked = originalFilter.CountdownStatus == CountdownStatusFilter.All;
            window.CountdownActiveFilter.IsChecked = originalFilter.CountdownStatus == CountdownStatusFilter.Active;
            window.CountdownExpiredFilter.IsChecked = originalFilter.CountdownStatus == CountdownStatusFilter.Expired;
            window.NoteAllFilter.IsChecked = originalFilter.NoteStatus == NoteStatusFilter.All;
            window.NoteActiveFilter.IsChecked = originalFilter.NoteStatus == NoteStatusFilter.Active;
            window.NoteCompletedFilter.IsChecked = originalFilter.NoteStatus == NoteStatusFilter.Completed;
            window.NoteUndatedFilter.IsChecked = originalFilter.UndatedNotesOnly;
            window.NoteDateFilter.SelectedDate = originalFilter.NoteDate is null
                ? null
                : DateTime.ParseExact(
                    originalFilter.NoteDate,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
            window.NoteSearch.Text = originalSearch;
            Workspace.ApplyFilter(originalFilter);
            window.RenderFilterTags(Workspace);
            window.RenderVisibleWorkspace(Workspace);
            window.UpdateFilterSummary(Workspace);
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    /// <summary>
    ///     #153：便签列表排版 / 芯片 / 空态 / 筛选日期标签 / 溢出菜单键盘 / 弹层 Tab 循环 / 间距 token 的
    ///     运行态探针。自建带长标签的探针便签（结束时删除便签与标签），不触碰帧捕获（帧捕获使用独立场景窗口）。
    /// </summary>
    private MainNoteListPolishScenarios ProbeNoteListPolishScenarios()
    {
        var entryWorkspace = window.CurrentWorkspace;
        var originalFilter = Workspace.Snapshot.Filter;
        var originalNoteDateFilter = window.NoteDateFilter.SelectedDate;
        var createdTagId = string.Empty;
        var createdNoteId = string.Empty;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            // 前置场景可能把已完成段留在选中态（ActiveNoteRows 折叠后可视树无行），先回到未完成段。
            window.SelectNotesSegment(false);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            // picker 文本用窗口内真实 NoteDateFilter（无父级控件解析不到 App 隐式模板）。
            // AC1：便签日期与倒计时截止同一中文日期格式——picker 模板选中日期文本与组头标签都留空格。
            var groupLabel = WindowScenarioAccess.FormatNoteDateLabel("2032-05-18", new DateTimeOffset(2032, 5, 18, 10, 15, 30, TimeSpan.FromHours(8)).ToUniversalTime());
            var groupLabelSpaced =
                groupLabel.Contains("5 月 18 日", StringComparison.Ordinal) &&
                !groupLabel.Contains("5月18日", StringComparison.Ordinal);

            // AC5：筛选弹层日期有可见标签，不是无名空下拉。
            window.FilterPopup.IsOpen = true;
            WindowValidationInput.Pump(window.Dispatcher);
            var filterDateHasVisibleLabel =
                window.NoteDateFilterLabel.IsVisible &&
                !string.IsNullOrWhiteSpace(window.NoteDateFilterLabel.Text);

            window.NoteDateFilter.SelectedDate = new DateTime(2032, 5, 18);
            window.NoteDateFilter.ApplyTemplate();
            WindowValidationInput.Pump(window.Dispatcher);
            var noteDateFilterTextBox = window.NoteDateFilter.Template.FindName(
                "PART_TextBox",
                window.NoteDateFilter) as System.Windows.Controls.Primitives.DatePickerTextBox;
            var pickerTextSpaced = string.Equals(
                noteDateFilterTextBox?.Text,
                "2032 年 5 月 18 日",
                StringComparison.Ordinal);
            // 立即还原：NoteDateFilter 驱动共享日期筛选，残留值会把后续行渲染全部过滤掉。
            window.NoteDateFilter.SelectedDate = originalNoteDateFilter;
            window.FilterPopup.IsOpen = false;
            WindowValidationInput.Pump(window.Dispatcher);
            var spacedChineseDate = pickerTextSpaced && groupLabelSpaced;

            // AC4：倒计时与便签空态文案都允许换行。
            var emptyStateTextAllowsWrap =
                window.EmptyNotesMessage.TextWrapping == TextWrapping.Wrap &&
                window.EmptyCountdownMessage.TextWrapping == TextWrapping.Wrap;

            // 自建长标签探针便签：AC2 正文行距、AC3 芯片契约、AC8 间距 token 都在真实卡上度量。
            // 标签名 26 字符（5 + 17 + 4 位随机后缀），超过芯片 MaxWidth 的可容纳字数以触发省略。
            var longTagName = $"长标签探针{new string('长', 17)}{Random.Shared.Next(1000, 9999)}";
            var createTagResult = Workspace.Apply(new CreateTagChange(longTagName, null));
            var probeTag = createTagResult.CreatedTag;
            if (probeTag is not null)
            {
                createdTagId = probeTag.Id;
                var saved = Workspace.Apply(new SaveNoteChange(
                    null,
                    $"便签排版探针-{Guid.NewGuid():N}",
                    Workspace.Snapshot.BeijingNow.Date,
                    false,
                    new[] { probeTag.Id },
                    NoReminder.Instance));
                if (saved.Succeeded)
                {
                    createdNoteId = Workspace.Snapshot.Settings.Notes.Single(note =>
                        note.TagIds.Contains(probeTag.Id, StringComparer.OrdinalIgnoreCase) &&
                        note.Text.StartsWith("便签排版探针-", StringComparison.Ordinal)).Id;
                }
            }

            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);

            var bodyLineHeightSeparatesSummary = false;
            var tagChipSingleLine = false;
            var cardSpacingConsumesTokens = false;
            if (createdNoteId is not null && probeTag is not null)
            {
                var probeRow = WindowScenarioAccess.FindMainNoteRow(window, createdNoteId);
                var bodyText = MainValidationDescendants<TextBlock>(probeRow)
                    .FirstOrDefault(text => string.Equals(text.Uid, "NoteRowBodyText", StringComparison.Ordinal));
                bodyLineHeightSeparatesSummary =
                    bodyText is not null &&
                    Math.Abs(bodyText.LineHeight - NoteCardBodyLineHeight) <= 0.01 &&
                    bodyText.LineStackingStrategy == LineStackingStrategy.BlockLineHeight &&
                    bodyText.TextWrapping == TextWrapping.Wrap;

                var chip = MainValidationDescendants<Border>(probeRow)
                    .FirstOrDefault(border => string.Equals(border.Uid, probeTag.Id, StringComparison.Ordinal));
                var chipText = chip?.Child as TextBlock;
                tagChipSingleLine =
                    chip is not null &&
                    chip.Parent is WrapPanel &&
                    Math.Abs(chip.MaxWidth - MainWindow.TagChipMaxWidth) <= 0.01 &&
                    chip.ActualWidth <= MainWindow.TagChipMaxWidth + 0.01 &&
                    chipText is not null &&
                    chipText.TextWrapping == TextWrapping.NoWrap &&
                    chipText.TextTrimming == TextTrimming.CharacterEllipsis &&
                    string.Equals(chip.ToolTip as string, probeTag.Name, StringComparison.Ordinal);

                var spacingCardPadding = (double)window.FindResource("SpacingCardPadding");
                var spacingUnit = (double)window.FindResource("SpacingUnit");
                var spacingBlockGap = (double)window.FindResource("SpacingBlockGap");
                var dateSection = MainValidationDescendants<Grid>(window.ActiveNoteRows)
                    .FirstOrDefault(grid =>
                        string.Equals(grid.Uid, "NoteDateTimelineSection", StringComparison.Ordinal));
                cardSpacingConsumesTokens =
                    Math.Abs(((Border)probeRow).Padding.Left - spacingCardPadding) <= 0.01 &&
                    Math.Abs(((Border)probeRow).Margin.Bottom - spacingUnit) <= 0.01 &&
                    dateSection is not null &&
                    Math.Abs(dateSection.Margin.Bottom - spacingBlockGap) <= 0.01;
            }

            // AC2：列表条目标题比区块标题低一档（管理窗整体降档后为 Body / H2），保持明确层级。
            // 空列表没有条目标题可度量，视为满足（空洞真）；有行而缺标题或字号不符才判失败。
            ShowWorkspace(WorkspaceKind.Countdowns);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            var rowTitleType = (double)window.FindResource("TypeBodyFontSize");
            var rowTitles = MainValidationDescendants<TextBlock>(window.CountdownRows)
                .Where(text => string.Equals(text.Uid, "CountdownRowTitleText", StringComparison.Ordinal))
                .ToArray();
            var rowTitleFallsToBlockTitleLevel =
                window.CountdownRows.Children.Count == 0 ||
                (rowTitles.Length > 0 &&
                 rowTitles.All(text => Math.Abs(text.FontSize - rowTitleType) <= 0.01));

            // #227：非空真实便签行的编辑/删除键盘动作与取消后的焦点归还。
            var rowActionsKeyboard = ProbeMainWindowNoteRowActionsKeyboard(createdNoteId!);

            // AC7：筛选 / 标签管理 / 标签选择器弹层 Tab 层内循环。
            ShowWorkspace(WorkspaceKind.Notes);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            var filterCycles = ProbePopupTabCyclesInsideLayer(window.FilterPopup, WindowScenarioAccess.FilterInitialFocusTarget(window));
            var tagManagerCycles = ProbePopupTabCyclesInsideLayer(window.TagManagerPopup, window.NewTagInput);
            var tagSelectorCycles = ProbePopupTabCyclesInsideLayer(
                window.NoteTags.Popup,
                window.NoteTags.PopupInitialFocus);

            return new MainNoteListPolishScenarios(
                spacedChineseDate,
                bodyLineHeightSeparatesSummary,
                rowTitleFallsToBlockTitleLevel,
                tagChipSingleLine,
                emptyStateTextAllowsWrap,
                filterDateHasVisibleLabel,
                rowActionsKeyboard,
                cardSpacingConsumesTokens,
                filterCycles,
                tagManagerCycles,
                tagSelectorCycles);
        }
        finally
        {
            if (!string.IsNullOrEmpty(createdNoteId))
            {
                Workspace.Apply(new DeleteNoteChange(createdNoteId));
            }

            if (!string.IsNullOrEmpty(createdTagId))
            {
                Workspace.Apply(new DeleteTagChange(createdTagId));
            }

            window.NoteDateFilter.SelectedDate = originalNoteDateFilter;
            Workspace.ApplyFilter(originalFilter);
            ShowWorkspace(entryWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    /// <summary>
    ///     #154：视觉铬探针——筛选漏斗实心在现役捕获口径可识别、控件图标描边统一且无重复搜索镜、
    ///     管理窗最小/关闭为矢量窗铬、页签与分段激活胶囊短边一半全圆角、倒计时页手写体落装饰层、
    ///     管理窗请求 DWM 原生圆角与系统阴影且不开整窗透明、页签在装饰带之上（#135 工作台骨架次序）。
    /// </summary>
    // 装饰 raster 原生 1165×1350，以 Fill 铺到 864×1000 管理窗画布；顶部装饰带下沿取
    // tools/MainDecorationSceneBands.psd1 的 TopBandEnd（倒计时 395、便签 415）按画布高换算。
    private const double DecorationRasterHeight = 1350;
    private const double CountdownDecorationBandBottom = 395 * 1000 / DecorationRasterHeight;
    private const double NotesDecorationBandBottom = 415 * 1000 / DecorationRasterHeight;

    internal MainVisualChromeScenarios ProbeVisualChromeScenarios()
    {
        var entryWorkspace = window.CurrentWorkspace;
        try
        {
            // 便签工作区可见态：搜索框图标 / 状态分段模板检查。
            ShowWorkspace(WorkspaceKind.Notes);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            var notesEditorTop = window.NoteEditorPanel.TranslatePoint(new Point(), window.MainLayoutSurface).Y;

            // AC1：筛选漏斗改实心填充（InkBrush）——1.8px 描边漏斗在 Aliased 捕获下只剩「筛选」文字
            // （v22 实证）；门禁钉「漏斗 Path 存在 + Fill/Stroke 同绑 InkBrush + 可见」。
            var funnel = MainValidationDescendants<System.Windows.Shapes.Path>(window.FilterButton).FirstOrDefault(path =>
                path.Data is not null &&
                Math.Abs(path.Data.Bounds.Width - 12.36) <= 0.5 &&
                Math.Abs(path.Data.Bounds.Height - 10.18) <= 0.5);
            var inkBrush = window.FindResource("InkBrush") as Brush;
            var filterFunnelIconSurvivesAliasedCapture =
                funnel is not null &&
                inkBrush is not null &&
                ReferenceEquals(funnel.Fill, inkBrush) &&
                ReferenceEquals(funnel.Stroke, inkBrush) &&
                funnel.IsVisible &&
                funnel.ActualWidth > 10;

            // AC2a：便签页标签旁重复搜索镜已移除；输入框内搜索镜统一描边语言
            // （Fill 空 + Stroke=MutedBrush 1.6 圆头，与铅笔/日历字段图标同规格）。
            var searchLabelRowClean =
                window.NoteSearchLabel.Parent is StackPanel labelRow &&
                !labelRow.Children.OfType<System.Windows.Shapes.Path>().Any();
            var mutedBrush = window.FindResource("MutedBrush") as Brush;
            var inputMagnifier =
                (window.NoteSearch.Parent as Grid)?.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
            var notesSearchDuplicateMagnifierRemoved =
                searchLabelRowClean &&
                inputMagnifier is not null &&
                mutedBrush is not null &&
                inputMagnifier.Fill is null &&
                ReferenceEquals(inputMagnifier.Stroke, mutedBrush) &&
                Math.Abs(inputMagnifier.StrokeThickness - 1.6) <= 0.01 &&
                inputMagnifier.StrokeStartLineCap == PenLineCap.Round &&
                inputMagnifier.StrokeEndLineCap == PenLineCap.Round &&
                inputMagnifier.IsVisible;

            // AC2b：最小/关闭为矢量路径（与悬浮窗头部同一 12x12 坐标几何、2px 圆头描边、
            // 描边绑定按钮 Foreground），不再是「−」「×」文本字形。
            var mainWindowChromeButtonsUseVectorPaths =
                ChromeButtonHasVectorGlyph(window.MinimizeButton, expectedWidth: 10, expectedHeight: 0) &&
                ChromeButtonHasVectorGlyph(window.CloseButton, expectedWidth: 10, expectedHeight: 10);

            // AC3：页签与分段激活胶囊短边一半全圆角。
            // 主窗页签：按钮高 44，Surface(3) 38 → 19，环 44 → 22。
            // 状态分段：按钮高 36，Surface(2) 32 → 16，环 36 → 18。
            // 悬浮工作区胶囊（App 级样式，模板宿主实例化检查）：高 36 → 18。
            var segmentActivePillsUseFullRoundedCorners =
                PillCornersAre(window.CountdownTab, 19, 22) &&
                PillCornersAre(window.NotesTab, 19, 22) &&
                PillCornersAre(window.ActiveNotesSegment, 16, 18) &&
                PillCornersAre(window.CompletedNotesSegment, 16, 18) &&
                FloatingSegmentStyleUsesPillCorner();

            // 倒计时工作区可见态：手写体装饰落点检查。
            ShowWorkspace(WorkspaceKind.Countdowns);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);

            // 装饰带玩偶不被业务内容盖住：两页不透明编辑卡的顶边都落在各自装饰带下沿之下
            // （#221 曾把倒计时编辑卡上移进装饰带，三只玩偶只剩头顶）。
            var countdownEditorTop = window.CountdownEditorSurface.TranslatePoint(new Point(), window.MainLayoutSurface).Y;
            var decorationBandCharactersUnobstructed =
                countdownEditorTop >= CountdownDecorationBandBottom &&
                notesEditorTop >= NotesDecorationBandBottom;

            // AC4：倒计时页手写体落装饰层——两处文案都是 CountdownWorkspace Canvas 直接子级
            // （绝对设计坐标落点），不在 CountdownWorkspaceBody 表单子树内参与测量。
            var decoFont = window.FindResource("HandwrittenDecoFontFamily") as FontFamily;
            var countdownDecoTexts = MainValidationDescendants<TextBlock>(window.CountdownWorkspace)
                .Where(textBlock => ReferenceEquals(textBlock.FontFamily, decoFont))
                .ToList();
            var countdownHandwrittenDecoOutOfFormMeasurement =
                decoFont is not null &&
                countdownDecoTexts.Count == 2 &&
                countdownDecoTexts.All(textBlock =>
                    textBlock.Visibility == Visibility.Visible &&
                    !textBlock.IsHitTestVisible &&
                    ReferenceEquals(textBlock.Parent, window.CountdownWorkspace) &&
                    Canvas.GetLeft(textBlock) > 0 &&
                    Canvas.GetTop(textBlock) > 0) &&
                !MainValidationDescendants<TextBlock>(window.CountdownWorkspaceBody)
                    .Any(textBlock =>
                        ReferenceEquals(textBlock.FontFamily, decoFont) &&
                        textBlock.Text.Length > 0);

            // AC6（#154/#200）：管理窗保留不透明 WPF 窗铬基线（CornerRadius 0、
            // GlassFrame 0），首个 HWND 创建后请求 Windows 11 DWM 原生圆角与系统阴影；
            // 不支持 DWM 时安全降级到既有窗铬并由诊断日志记录，装饰 raster 仍不可命中。
            var windowChrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
            var dwmWindowChromeConfigured =
                window.DwmWindowChromeResultForValidation is
                    DwmWindowChromeResult.Applied or DwmWindowChromeResult.Fallback;
            var mainWindowSquareChromeNoTransparency =
                !window.AllowsTransparency &&
                window.WindowStyle == WindowStyle.None &&
                windowChrome is not null &&
                windowChrome.CornerRadius == new CornerRadius(0) &&
                windowChrome.GlassFrameThickness == new Thickness(0) &&
                window.Background is not null &&
                dwmWindowChromeConfigured &&
                !window.CountdownReferenceDecoration.IsHitTestVisible &&
                !window.NotesReferenceDecoration.IsHitTestVisible;

            // AC7：#135 工作台骨架次序——页签（命令栏）在装饰带之上：装饰层 ZIndex 低于内容投影层、
            // 装饰层不可命中、页签在内容投影层子树内。
            var tabsRenderAboveDecorationBand =
                Panel.GetZIndex(window.MainReferenceDecorationLayer) < Panel.GetZIndex(window.MainContentProjection) &&
                !window.MainReferenceDecorationLayer.IsHitTestVisible &&
                window.MainContentProjection.IsAncestorOf(window.CountdownTab) &&
                window.MainContentProjection.IsAncestorOf(window.NotesTab);

            // 命令栏设置齿轮与折叠新增展开入口为矢量路径：填充/描边绑定按钮 Foreground、按钮内容区不把图标挤裁，
            // 展开入口收起态朝下、展开态转 180°；不再是 ⚙/⌄/⌃ 文本字形（v52–v57 帧里被内边距裁得只剩一截或一个点）。
            var gear = UnclippedVectorIcon(window.PreferencesButton);
            var chevron = UnclippedVectorIcon(window.CountdownEditorExpandButton);
            var editorWasExpanded = window.MainCountdownTimeTagRow.Visibility == Visibility.Visible;
            window.SetCountdownEditorExpanded(false);
            var collapsedChevronAngle = (chevron?.RenderTransform as RotateTransform)?.Angle;
            window.SetCountdownEditorExpanded(true);
            var expandedChevronAngle = (chevron?.RenderTransform as RotateTransform)?.Angle;
            window.SetCountdownEditorExpanded(editorWasExpanded);
            var mainCommandIconsUseVectorPaths =
                gear is { Data: Geometry gearGeometry } &&
                Math.Abs(gearGeometry.Bounds.Width - 15.32) <= 0.5 &&
                Math.Abs(gearGeometry.Bounds.Height - 15.9) <= 0.5 &&
                ReferenceEquals(gear.Fill, window.PreferencesButton.Foreground) &&
                chevron is { Data: Geometry chevronGeometry } &&
                Math.Abs(chevronGeometry.Bounds.Width - 10) <= 0.5 &&
                Math.Abs(chevronGeometry.Bounds.Height - 5) <= 0.5 &&
                Math.Abs(chevron.StrokeThickness - 2) <= 0.01 &&
                chevron.StrokeStartLineCap == PenLineCap.Round &&
                chevron.StrokeEndLineCap == PenLineCap.Round &&
                ReferenceEquals(chevron.Stroke, window.CountdownEditorExpandButton.Foreground) &&
                collapsedChevronAngle == 0 &&
                expandedChevronAngle == 180;

            return new MainVisualChromeScenarios(
                filterFunnelIconSurvivesAliasedCapture,
                notesSearchDuplicateMagnifierRemoved,
                mainWindowChromeButtonsUseVectorPaths,
                segmentActivePillsUseFullRoundedCorners,
                countdownHandwrittenDecoOutOfFormMeasurement,
                mainWindowSquareChromeNoTransparency,
                tabsRenderAboveDecorationBand,
                decorationBandCharactersUnobstructed,
                mainCommandIconsUseVectorPaths);
        }
        finally
        {
            ShowWorkspace(entryWorkspace);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    // #154：管理窗窗铬按钮（最小/关闭）内容为 Viewbox+Path 矢量字形——描边 2px 圆头、
    // 绑定按钮 Foreground、Data 包围盒与悬浮窗头部同几何（12x12 坐标系）。
    private static bool ChromeButtonHasVectorGlyph(Button button, double expectedWidth, double expectedHeight)
    {
        button.ApplyTemplate();
        if (button.Content is not Viewbox viewbox || viewbox.Child is not System.Windows.Shapes.Path glyph || glyph.Data is null)
        {
            return false;
        }

        var bounds = glyph.Data.Bounds;
        return Math.Abs(bounds.Width - expectedWidth) <= 0.5 &&
            Math.Abs(bounds.Height - expectedHeight) <= 0.5 &&
            Math.Abs(glyph.StrokeThickness - 2) <= 0.01 &&
            glyph.StrokeStartLineCap == PenLineCap.Round &&
            glyph.StrokeEndLineCap == PenLineCap.Round &&
            ReferenceEquals(glyph.Stroke, button.Foreground);
    }

    // 命令栏图标按钮：内容为矢量 Path，且按钮留给内容的布局槽（外框扣掉边框与内边距）不比图标小——
    // 共享按钮左右 13 DIP 的内边距会把 41/36 宽按钮的布局槽挤到 9/4 DIP，图标仍按自身 18/16 宽排版，超出部分被裁掉。
    private static System.Windows.Shapes.Path? UnclippedVectorIcon(Button button)
    {
        button.ApplyTemplate();
        if (button.Content is not System.Windows.Shapes.Path { Data: not null } icon ||
            icon.ActualWidth <= 0 ||
            VisualTreeHelper.GetParent(icon) is not ContentPresenter presenter)
        {
            return null;
        }

        var slot = LayoutInformation.GetLayoutSlot(presenter);
        return slot.Width + 0.01 >= icon.ActualWidth && slot.Height + 0.01 >= icon.ActualHeight ? icon : null;
    }

    // #154：页签/分段模板激活胶囊短边一半全圆角——Surface 与 FocusRing 的 CornerRadius 钉值。
    private static bool PillCornersAre(ToggleButton toggle, double surfaceRadius, double ringRadius)
    {
        toggle.ApplyTemplate();
        return toggle.Template?.FindName("Surface", toggle) is Border surface &&
            surface.CornerRadius.TopLeft == surfaceRadius &&
            toggle.Template?.FindName("FocusRing", toggle) is Border ring &&
            ring.CornerRadius.TopLeft == ringRadius;
    }

    // #154：内层表面和外层焦点环分别按各自短边的一半保持全圆角。
    // 实例化模式与 RegularFocusVisualThicknessIs 相同（模板无 FrameworkElementFactory）。
    private bool FloatingSegmentStyleUsesPillCorner()
    {
        if (window.FindResource("FloatingHeaderSegmentButton") is not Style style)
        {
            return false;
        }

        var host = new ToggleButton
        {
            Style = style,
            Width = 85,
            Height = 36,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false
        };
        window.MainContentProjection.Children.Add(host);
        try
        {
            host.ApplyTemplate();
            WindowValidationInput.Pump(window.Dispatcher);
            host.UpdateLayout();
            return host.Template?.FindName("Surface", host) is Border surface &&
                host.Template.FindName("FocusRing", host) is Border ring &&
                Math.Abs(surface.ActualHeight - 32) <= 0.01 &&
                Math.Abs(ring.ActualHeight - 36) <= 0.01 &&
                surface.CornerRadius == new CornerRadius(Math.Min(surface.ActualWidth, surface.ActualHeight) / 2) &&
                ring.CornerRadius == new CornerRadius(Math.Min(ring.ActualWidth, ring.ActualHeight) / 2);
        }
        finally
        {
            window.MainContentProjection.Children.Remove(host);
        }
    }

    // #227：自建记录必须存在；不以空列表作为键盘门通过证据。
    private bool ProbeMainWindowNoteRowActionsKeyboard(string noteId)
    {
        ShowWorkspace(WorkspaceKind.Notes);
        window.UpdateLayout();
        WindowValidationInput.Pump(window.Dispatcher);
        if (string.IsNullOrEmpty(noteId))
            return false;
        var row = WindowScenarioAccess.VisualDescendants<FrameworkElement>(window.NotesListCard)
            .FirstOrDefault(element => element is NoteSelectionCard &&
                string.Equals(element.Tag as string, noteId, StringComparison.Ordinal));
        if (row is null)
            return false;
        var buttons = MainValidationDescendants<Button>(row).ToArray();
        var edit = buttons.SingleOrDefault(button => Equals(button.Content, "编辑"));
        var delete = buttons.SingleOrDefault(button => Equals(button.Content, "删除"));
        if (edit is null || delete is null || buttons.Length != 2) return false;
        var note = Workspace.Snapshot.Settings.Notes.Single(item => item.Id == noteId);
        try
        {
            window.Activate();
            Keyboard.Focus(edit);
            edit.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(window.Dispatcher);
            if (!ReferenceEquals(Keyboard.FocusedElement, delete)) return false;
            delete.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            WindowValidationInput.Pump(window.Dispatcher);
            if (!ReferenceEquals(Keyboard.FocusedElement, edit)) return false;
            WindowValidationInput.RaiseKey(edit, Key.Enter);
            WindowValidationInput.Pump(window.Dispatcher);
            if (window.CaptureInstanceActivationState().EditingNoteId != noteId || window.NoteText.Text != note.Text || !window.NoteEditorTitle.IsVisible)
                return false;
            window.CancelNoteEdit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (window.CaptureInstanceActivationState().EditingNoteId is not null || window.NoteEditorTitle.IsVisible) return false;
            Keyboard.Focus(delete);
            var dialogCorrect = false;
            ValidationExercises.QueueConfirmationDialogResponse(window, false, dialog =>
            {
                dialogCorrect = dialog.BodyText.Text.Contains(note.Text, StringComparison.Ordinal) &&
                    dialog.CancelButton.IsDefault && !dialog.ConfirmButton.IsDefault;
            });
            WindowValidationInput.RaiseKey(delete, Key.Enter);
            WindowValidationInput.Pump(window.Dispatcher);
            return dialogCorrect && Workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteId) &&
                (delete.IsKeyboardFocusWithin || ReferenceEquals(FocusManager.GetFocusedElement(window), delete));
        }
        finally
        {
            window.ResetNoteEditor();
        }
    }

    /// <summary>
    ///     #153：弹层 Tab 层内循环——从首项连续 Tab 超过层内可聚焦项数量后焦点仍留在弹层子树内
    ///     且回到首项（Cycle 包环）；Tab 从层内逃到被挡住的宿主窗口即失败。
    /// </summary>
    private bool ProbePopupTabCyclesInsideLayer(Popup popup, IInputElement? expectedInitialFocus)
    {
        try
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
            window.Activate();
            WindowValidationInput.Pump(window.Dispatcher);
            popup.IsOpen = true;
            WindowValidationInput.Pump(window.Dispatcher);
            WindowValidationInput.Pump(window.Dispatcher);
            var surface = popup.Child as DependencyObject;
            if (surface is null || !popup.IsOpen)
            {
                ReportFailure("not-open", Keyboard.FocusedElement);
                return false;
            }

            var first = Keyboard.FocusedElement;
            if (expectedInitialFocus is not null && !ReferenceEquals(first, expectedInitialFocus))
            {
                ReportFailure("initial-focus", first);
                return false;
            }

            bool InsideSurface(IInputElement? focused) =>
                focused is not null &&
                (ReferenceEquals(focused, surface) ||
                 (focused is System.Windows.Media.Visual visual &&
                  surface is System.Windows.Media.Visual surfaceVisual &&
                  surfaceVisual.IsAncestorOf(visual)));
            var stayedInside = InsideSurface(first);
            var wrappedToFirst = false;
            var current = first;
            for (var step = 0; step < 32 && stayedInside && !wrappedToFirst; step++)
            {
                (current as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                WindowValidationInput.Pump(window.Dispatcher);
                current = Keyboard.FocusedElement;
                stayedInside = InsideSurface(current);
                wrappedToFirst = stayedInside && ReferenceEquals(current, first);
            }

            if (!stayedInside || !wrappedToFirst)
            {
                ReportFailure(stayedInside ? "did-not-wrap" : "escaped-layer", current);
            }

            return stayedInside && wrappedToFirst;
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
        }

        void ReportFailure(string phase, IInputElement? focused) =>
            Console.Error.WriteLine("POPUP_TAB_DIAGNOSTIC " +
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    Popup = popup.Name,
                    phase,
                    popup.IsOpen,
                    IsClosing = PopupMotionBehavior.IsClosing(popup),
                    Expected = (expectedInitialFocus as FrameworkElement)?.Name,
                    Actual = (focused as FrameworkElement)?.Name,
                    Type = focused?.GetType().Name
                }));
    }

    private bool ProbeLegacyInlineReminderControlsStayHidden()
    {
        var originalEditingId = window.CaptureInstanceActivationState().EditingNoteId;
        ShowWorkspace(WorkspaceKind.Notes);
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        try
        {
            var snapshot = Workspace.Snapshot;
            var noteDate = snapshot.BeijingNow.AddDays(2).Date;
            var onceDue = snapshot.BeijingNow.AddDays(3);
            onceDue = new DateTimeOffset(
                onceDue.Year,
                onceDue.Month,
                onceDue.Day,
                9,
                10,
                0,
                onceDue.Offset);
            var dateText = noteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var onceNote = new NoteRecord(
                Guid.NewGuid().ToString("N"),
                "验证主窗仅一次",
                dateText,
                false,
                Array.Empty<string>(),
                new ReminderRule[]
                {
                    new AbsoluteOnceReminderRule(
                        Guid.NewGuid().ToString("N"),
                        onceDue.ToUniversalTime())
                });
            var intervalNote = new NoteRecord(
                Guid.NewGuid().ToString("N"),
                "验证主窗重复提醒",
                dateText,
                false,
                Array.Empty<string>(),
                new ReminderRule[]
                {
                    new FixedIntervalReminderRule(
                        Guid.NewGuid().ToString("N"),
                        onceDue.AddDays(1).AddMinutes(20).ToUniversalTime(),
                        new ReminderInterval(30))
                });
            return LegacyInlineReminderEditorStaysCollapsed(onceNote) &&
                   LegacyInlineReminderEditorStaysCollapsed(intervalNote);
        }
        finally
        {
            window.NoteDate.IsDropDownOpen = false;
            window.ResetNoteEditor();
            if (originalEditingId is not null)
            {
                var originalNote = Workspace.Snapshot.AllNotes.Single(note => note.Id == originalEditingId);
                window.LoadNoteIntoEditor(originalNote);
            }
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private bool LegacyInlineReminderEditorStaysCollapsed(NoteRecord note)
    {
        window.LoadNoteIntoEditor(note);
        window.NoteDate.IsDropDownOpen = false;
        window.UpdateLayout();
        WindowValidationInput.Pump(window.Dispatcher);
        var retiredNames = new[]
        {
            "NoteReminder",
            "NoteReminderInterval",
            "CustomReminderMinutes",
            "NoteReminderSchedule",
            "NoteReminderDate",
            "NoteReminderHour",
            "NoteReminderMinute",
            "NoteReminderIntervalLabel",
            "NoteReminderLabel"
        };
        var retiredHidden = retiredNames.All(name =>
            window.FindName(name) is not FrameworkElement element || !element.IsVisible);
        var summaryBounds = ValidationBounds(window.NoteReminderRulesSummary);
        var obscured = retiredNames
            .Select(name => window.FindName(name) as FrameworkElement)
            .OfType<FrameworkElement>()
            .Where(element => element.IsVisible)
            .Select(element => ValidationBounds(element))
            .Any(bounds => bounds.IntersectsWith(summaryBounds));
        return
            retiredHidden &&
            !obscured &&
            window.ManageNoteReminderRulesButton.IsVisible &&
            window.NoteReminderRulesSummary.IsVisible &&
            window.NoteReminderRulesSummary.ActualHeight > 0 &&
            note.ReminderRules.Count > 0 &&
            window.NoteReminderRulesSummary.Text.Contains("条提醒规则", StringComparison.Ordinal);
    }

    private static IEnumerable<T> MainValidationDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in MainValidationDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private bool CountdownEmptyResetRestoredResults()
    {
        var filter = Workspace.Snapshot.Filter;
        return
            filter == DesktopWorkspaceFilter.Empty &&
            Workspace.Snapshot.VisibleCountdowns.Count > 0 &&
            window.CountdownRows.Children.Count > 0 &&
            window.EmptyCountdownMessage.Visibility == Visibility.Collapsed &&
            window.ResetEmptyCountdownFiltersButton.Visibility == Visibility.Collapsed &&
            // #154：筛选按钮内容改为「图标 + 具名文字」StackPanel，摘要断言读 FilterButtonText。
            string.Equals(window.FilterButtonText.Text, "筛选", StringComparison.Ordinal) &&
            string.Equals(
                AutomationProperties.GetName(window.FilterButton),
                "打开筛选",
                StringComparison.Ordinal) &&
            string.Equals(
                window.StatusText.Text,
                $"筛选结果：{window.RecordCountText.Text}",
                StringComparison.Ordinal);
    }

    private CountdownIncrementalRefreshProbe ProbeCountdownIncrementalRefresh()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalFilter = Workspace.Snapshot.Filter;
        var originalPreferences = Workspace.Snapshot.Settings.Preferences;
        var temporaryIds = new List<string>();
        double? originalOpacity = null;
        using var animationPreference = ApplicationTheme.OverrideAnimationPreferenceForValidation(true);
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            // #239：前序空态探针可能留下倒计时筛选（UI 夹具含已到期种子时会留下“已到期”），
            // 新建的未到期行会被筛掉；本探针从无筛选开始，finally 恢复原筛选。
            Workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            var nowUtc = Workspace.Snapshot.UtcNow;
            var secondsPrecisionSaved = Workspace.Apply(new SavePreferencesChange(
                "Second",
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd)).Succeeded;
            var dueUtc = nowUtc.AddSeconds(10);
            for (var index = 0; index < 10; index++)
            {
                var name = $"验证倒计时增量刷新-{index:D2}";
                var result = Workspace.Apply(new SaveCountdownChange(
                    null,
                    name,
                    index == 0 ? dueUtc : nowUtc.AddDays(index + 1),
                    Array.Empty<string>()));
                if (!result.Succeeded)
                {
                    return CountdownIncrementalRefreshProbe.Failed;
                }

                var added = Workspace.Snapshot.Settings.Countdowns
                    .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal));
                temporaryIds.Add(added.Id);
            }

            var record = Workspace.Snapshot.Settings.Countdowns
                .Single(item => item.Id == temporaryIds[0]);
            var pointerRecord = Workspace.Snapshot.Settings.Countdowns
                .Single(item => item.Id == temporaryIds[^1]);
            if (record is null)
            {
                return CountdownIncrementalRefreshProbe.Failed;
            }

            var before = ChinaStandardTime.ToBeijingTime(dueUtc.AddSeconds(-1));
            window.UpdateTime(before);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var rowBefore = WindowScenarioAccess.FindMainCountdownRow(window, record.Id);
            var textBefore = window.CountdownRemainingTextsForValidation.GetValueOrDefault(record.Id)
                ?? throw new InvalidOperationException("倒计时首行缺少剩余时间文本。");
            var pointerRow = WindowScenarioAccess.FindMainCountdownRow(window, pointerRecord.Id);
            var focusTarget = WindowScenarioAccess.FindMainCountdownFocusTarget(window, pointerRecord.Id);

            var scrollViewer = FindVisualParent<ScrollViewer>(rowBefore);
            // #148：程序化 Keyboard.Focus 会触发 WPF bring-into-view。若先 ScrollToEnd 再聚焦，
            // 聚焦行的编辑按钮在长分格下可能骑在视口顶裁切线上，此处最小滚入会被误记到 tick 头上。
            // 因此先聚焦、再滚到底取 offsetBefore，让断言只度量「tick 本身是否移动滚动位置」。
            Keyboard.Focus(focusTarget);
            WindowValidationInput.Pump(window.Dispatcher);
            scrollViewer?.ScrollToEnd();
            WindowValidationInput.Pump(window.Dispatcher);
            var offsetBefore = scrollViewer?.VerticalOffset ?? 0;
            var scrollableHeightBefore = scrollViewer?.ScrollableHeight ?? 0;
            originalOpacity = window.Opacity;
            window.Opacity = 1;
            window.Activate();
            WindowValidationInput.Pump(window.Dispatcher);
            var pointerCaptured = Mouse.Capture(pointerRow, CaptureMode.SubTree);
            var remainingTextBeforeTick = textBefore.Text;
            window.UpdateTime(before.AddMilliseconds(250));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var ordinaryIdentityPreserved =
                ReferenceEquals(rowBefore, WindowScenarioAccess.FindMainCountdownRow(window, record.Id)) &&
                ReferenceEquals(textBefore, window.CountdownRemainingTextsForValidation.GetValueOrDefault(record.Id));
            var secondsValues = WindowValidationInput.RemainingSecondsValues(window.CountdownRows);
            var countdownSecondsSwitchWithoutAnimation =
                secondsPrecisionSaved &&
                !string.Equals(remainingTextBeforeTick, textBefore.Text, StringComparison.Ordinal) &&
                secondsValues.Length >= 2 &&
                secondsValues.All(value => value.Opacity == 1 &&
                    !DependencyPropertyHelper.GetValueSource(value, UIElement.OpacityProperty).IsAnimated);
            var focusPreserved = ReferenceEquals(Keyboard.FocusedElement, focusTarget);
            var nonZeroScrollPreserved =
                offsetBefore > 0 &&
                Math.Abs((scrollViewer?.VerticalOffset ?? 0) - offsetBefore) <= 0.1;
            var scrollOffsetAfterTick = scrollViewer?.VerticalOffset ?? 0;
            // A session without an interactive mouse refuses Capture(); that is not a
            // tick-refresh regression. When capture is granted, the tick must keep it.
            var pointerStatePreserved = pointerCaptured
                ? ReferenceEquals(Mouse.Captured, pointerRow)
                : Mouse.Captured is null;
            if (originalOpacity is { } restoredOpacity)
            {
                window.Opacity = restoredOpacity;
                originalOpacity = null;
            }
            if (ReferenceEquals(Mouse.Captured, pointerRow))
            {
                Mouse.Capture(null);
            }

            var generation = window.CountdownRowRebuildGenerationForValidation;
            var collectionName = $"验证集合变化-{Guid.NewGuid():N}"[..27];
            var collectionResult = Workspace.Apply(new SaveCountdownChange(
                null,
                collectionName,
                nowUtc.AddDays(20),
                Array.Empty<string>()));
            WindowValidationInput.Pump(window.Dispatcher);
            var collectionRecord = Workspace.Snapshot.Settings.Countdowns
                .FirstOrDefault(item => string.Equals(item.Name, collectionName, StringComparison.Ordinal));
            if (collectionRecord is not null)
            {
                temporaryIds.Add(collectionRecord.Id);
            }
            var collectionChangeRebuilt =
                collectionResult.Succeeded &&
                collectionRecord is not null &&
                window.CountdownRowRebuildGenerationForValidation > generation &&
                WindowScenarioAccess.HasMainCountdownRow(window, collectionRecord.Id);

            generation = window.CountdownRowRebuildGenerationForValidation;
            Workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { CountdownStatus = CountdownStatusFilter.Expired });
            WindowValidationInput.Pump(window.Dispatcher);
            var filterChangeRebuilt =
                window.CountdownRowRebuildGenerationForValidation > generation &&
                !WindowScenarioAccess.HasMainCountdownRow(window, record.Id);
            Workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            WindowValidationInput.Pump(window.Dispatcher);

            generation = window.CountdownRowRebuildGenerationForValidation;
            // 探针开头已切到秒精度，这里换到分钟精度触发签名变化。
            var precisionSaved = Workspace.Apply(new SavePreferencesChange(
                "Minute",
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd)).Succeeded;
            WindowValidationInput.Pump(window.Dispatcher);
            var precisionChangeRebuilt =
                precisionSaved && window.CountdownRowRebuildGenerationForValidation > generation;

            generation = window.CountdownRowRebuildGenerationForValidation;
            window.UpdateTime(ChinaStandardTime.ToBeijingTime(dueUtc.AddSeconds(1)));
            WindowValidationInput.Pump(window.Dispatcher);
            var expiryChangeRebuilt = window.CountdownRowRebuildGenerationForValidation > generation;
            var expiryBackgroundFlipped =
                WindowScenarioAccess.HasMainCountdownRow(window, record.Id) &&
                WindowScenarioAccess.FindMainCountdownRow(window, record.Id) is { } rowAfter &&
                !ReferenceEquals(rowBefore, rowAfter) &&
                rowAfter is Border expiredCard &&
                ReferenceEquals(expiredCard.Background, window.FindResource("ExpiredBrush"));
            var expiryRemainingTextFlipped =
                window.CountdownRemainingTextsForValidation.TryGetValue(record.Id, out var textAfter) &&
                !ReferenceEquals(textBefore, textAfter) &&
                // #148：到期剩余文字不用删除红；该记录是首行主项，到期后回落主项数字色。
                ReferenceEquals(textAfter.Foreground, window.FindResource("PrimaryCountdownNumberBrush"));
            return new CountdownIncrementalRefreshProbe(
                ordinaryIdentityPreserved,
                countdownSecondsSwitchWithoutAnimation,
                focusPreserved,
                nonZeroScrollPreserved,
                pointerStatePreserved,
                collectionChangeRebuilt,
                filterChangeRebuilt,
                precisionChangeRebuilt,
                expiryChangeRebuilt,
                expiryBackgroundFlipped,
                expiryRemainingTextFlipped,
                Math.Round(offsetBefore, 2),
                Math.Round(scrollOffsetAfterTick, 2),
                Math.Round(scrollableHeightBefore, 2));
        }
        finally
        {
            if (originalOpacity is { } restoredOpacity)
            {
                window.Opacity = restoredOpacity;
            }

            Mouse.Capture(null);
            Workspace.ApplyFilter(originalFilter);
            Workspace.Apply(new SavePreferencesChange(
                originalPreferences.DisplayPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd));
            foreach (var temporaryId in temporaryIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Workspace.Apply(new DeleteCountdownChange(temporaryId));
            }

            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    // #148：倒计时列表可读性探针——「超期待！」排他、到期文字对比、分格几何与表格数字格式。
    // #151：倒计时编辑合同——字段级错误贴字段并聚焦首个无效控件、唯一错误行不裁切、
    // 偏好错误下移到字段下方、主次按钮角色（每页一枚实心主行动 + 珊瑚次级实心）、
    // 白纸输入面、独立选区高亮、状态条表面错误对比、提醒规则选中圆点非文本对比、排版/间距 token。
    internal CountdownEditingScenarios ProbeCountdownEditingScenarios()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalName = window.CountdownName.Text;
        var originalDate = window.CountdownDate.SelectedDate;
        var originalTime = window.CountdownTime.Text;
        var originalSalary = window.MonthlySalary.Text;
        var originalWorkStart = window.WorkStart.Text;
        var originalWorkEnd = window.WorkEnd.Text;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            // 焦点断言前置条件：与悬浮窗 DatePicker 探针同一口径，先激活本窗口再模拟提交。
            window.Activate();
            WindowValidationInput.Pump(window.Dispatcher);
            window.ResetCountdownEditor();
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();

            // —— 倒计时字段错误：名称与截止日期按字段拆开，聚焦第一个无效控件（名称优先）。
            window.CountdownName.Text = string.Empty;
            window.CountdownDate.SelectedDate = null;
            WindowValidationInput.Pump(window.Dispatcher);
            window.SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var fieldErrorsAtSource =
                window.CountdownNameError.Visibility == Visibility.Visible &&
                window.CountdownDateError.Visibility == Visibility.Visible &&
                string.Equals(window.CountdownNameError.Text, "请先填写倒计时名称。", StringComparison.Ordinal) &&
                string.Equals(window.CountdownDateError.Text, "请选择截止日期。", StringComparison.Ordinal) &&
                string.Equals(window.CountdownMessage.Text, window.CountdownNameError.Text, StringComparison.Ordinal) &&
                window.CountdownName.IsKeyboardFocusWithin &&
                EditingValidationErrorIsAccessible(window.CountdownNameError, window.CountdownName) &&
                EditingValidationErrorIsAccessible(window.CountdownDateError, window.CountdownDate);

            // 仅日期无效时再次提交：聚焦唯一无效控件（截止日期）。
            window.CountdownName.Text = "倒计时编辑探针";
            WindowValidationInput.Pump(window.Dispatcher);
            window.SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var dateOnlyFocusesDate =
                window.CountdownNameError.Visibility == Visibility.Collapsed &&
                window.CountdownDateError.Visibility == Visibility.Visible &&
                window.CountdownDate.IsKeyboardFocusWithin;

            // —— 唯一错误行不再固定高度裁切或省略（MaxHeight 默认为正无穷）。
            var singleErrorRowNotClipped =
                double.IsPositiveInfinity(window.CountdownMessage.MaxHeight) &&
                window.CountdownMessage.TextTrimming == TextTrimming.None &&
                window.CountdownMessage.TextWrapping == TextWrapping.Wrap;

            // —— 字段值修正后错误立即清除。
            window.CountdownDate.SelectedDate = Workspace.Snapshot.BeijingNow.Date;
            WindowValidationInput.Pump(window.Dispatcher);
            var countdownFieldErrorsClear =
                window.CountdownNameError.Visibility == Visibility.Collapsed &&
                window.CountdownDateError.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(window.CountdownMessage.Text);

            // —— 偏好错误出现在对应字段下方并聚焦；错误态错误字落在状态条表面上（见下）。
            // 前序场景可能留下已打开的设置弹层；直接点击 toggle 会启动退出动画，
            // 使字段探针偶然测到正在消失的表面。先完成 fixture 清理，再真实打开。
            WindowValidationInput.ClosePopupForCleanup(window.PreferencesPopup);
            window.Activate();
            Keyboard.Focus(window.PreferencesButton);
            window.PreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            if (!window.PreferencesPopup.IsOpen || PopupMotionBehavior.IsClosing(window.PreferencesPopup) ||
                !window.DisplayPrecision.IsKeyboardFocusWithin)
                throw new InvalidOperationException("偏好字段探针必须先真实打开设置弹层并聚焦首项。");
            window.MonthlySalary.Text = "not-a-number";
            window.SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var salaryErrorBelowField =
                window.PreferencesPopup.IsOpen && !PopupMotionBehavior.IsClosing(window.PreferencesPopup) && window.MonthlySalaryError.IsVisible &&
                window.MonthlySalaryError.Visibility == Visibility.Visible &&
                string.Equals(window.MonthlySalaryError.Text, "月薪格式无效。", StringComparison.Ordinal) &&
                window.MonthlySalary.IsKeyboardFocusWithin &&
                window.MonthlySalaryError.TranslatePoint(new Point(), window.CountdownPreferencesSurface).Y >=
                    window.MonthlySalary.TranslatePoint(new Point(0, window.MonthlySalary.ActualHeight), window.CountdownPreferencesSurface).Y - 0.5 &&
                EditingValidationErrorIsAccessible(window.MonthlySalaryError, window.MonthlySalary) &&
                string.Equals(window.StatusText.Text, "月薪格式无效。", StringComparison.Ordinal);
            if (!salaryErrorBelowField)
                Console.Error.WriteLine($"PREFERENCE_FIELD_DIAGNOSTIC salary: popup={window.PreferencesPopup.IsOpen}; visible={window.MonthlySalaryError.IsVisible}; text={window.MonthlySalaryError.Text}; focused={window.MonthlySalary.IsKeyboardFocusWithin}; current={Keyboard.FocusedElement}; errorY={window.MonthlySalaryError.TranslatePoint(new Point(), window.CountdownPreferencesSurface).Y}; fieldBottom={window.MonthlySalary.TranslatePoint(new Point(0, window.MonthlySalary.ActualHeight), window.CountdownPreferencesSurface).Y}; accessible={EditingValidationErrorIsAccessible(window.MonthlySalaryError, window.MonthlySalary)}; status={window.StatusText.Text}");

            // 月薪先恢复合法值，让校验推进到上班时间字段（镜像真实逐字段提交顺序）。
            window.MonthlySalary.Text = "8000";
            window.WorkStart.Text = "9点";
            window.SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var workStartErrorBelowField =
                window.PreferencesPopup.IsOpen && !PopupMotionBehavior.IsClosing(window.PreferencesPopup) && window.WorkStartError.IsVisible &&
                window.MonthlySalaryError.Visibility == Visibility.Collapsed &&
                window.WorkStartError.Visibility == Visibility.Visible &&
                string.Equals(window.WorkStartError.Text, "上班时间请使用 HH:mm。", StringComparison.Ordinal) &&
                window.WorkStart.IsKeyboardFocusWithin &&
                window.WorkStartError.TranslatePoint(new Point(), window.CountdownPreferencesSurface).Y >=
                    window.WorkStart.TranslatePoint(new Point(0, window.WorkStart.ActualHeight), window.CountdownPreferencesSurface).Y - 0.5;
            if (!workStartErrorBelowField)
                Console.Error.WriteLine($"PREFERENCE_FIELD_DIAGNOSTIC workStart: popup={window.PreferencesPopup.IsOpen}; salaryError={window.MonthlySalaryError.Visibility}; visible={window.WorkStartError.IsVisible}; text={window.WorkStartError.Text}; focused={window.WorkStart.IsKeyboardFocusWithin}; current={Keyboard.FocusedElement}; errorY={window.WorkStartError.TranslatePoint(new Point(), window.CountdownPreferencesSurface).Y}; fieldBottom={window.WorkStart.TranslatePoint(new Point(0, window.WorkStart.ActualHeight), window.CountdownPreferencesSurface).Y}");

            // —— 偏好字段修正后错误清除（不保存，避免写入偏好）。
            window.WorkStart.Text = "09:00";
            WindowValidationInput.Pump(window.Dispatcher);
            var preferenceErrorsClear =
                window.WorkStartError.Visibility == Visibility.Collapsed;

            // —— 主次按钮角色：倒计时页唯一实心主行动是「＋ 添加倒计时」；
            WindowValidationInput.ClosePopupForCleanup(window.PreferencesPopup);
            // #217「保存设置」为 CardAlt 次级表面 + 主文字色 + 可辨识的常态边界。
            var solidPrimaryButtons = ProbeVisualDescendants<Button>(window.CountdownWorkspaceBody)
                .Where(button => ReferenceEquals(button.Background, window.FindResource("MainActionBrush")))
                .ToArray();
            var primarySecondaryRolesDistinct =
                solidPrimaryButtons.Length == 1 &&
                ReferenceEquals(solidPrimaryButtons[0], window.SaveCountdownButton) &&
                ReferenceEquals(window.SavePreferencesButton.Background, window.FindResource("CardAltBrush")) &&
                ReferenceEquals(window.SavePreferencesButton.BorderBrush, window.FindResource("ControlBorderBrush")) &&
                ReferenceEquals(window.SavePreferencesButton.Foreground, window.FindResource("InkBrush")) &&
                ApplicationTheme.MeetsContrast(
                    (Color)window.FindResource("CardAltColor"),
                    (Color)window.FindResource("InkColor"),
                    4.5) &&
                ReferenceEquals(window.SaveNoteButton.Background, window.FindResource("MainActionBrush"));

            // —— 白纸输入面：主窗倒计时页输入控件表面为 PanelBrush，与画布色分开；
            // #312 单框日期控件由外层 Surface 承接 PanelBrush，内部文本框与箭头按钮保持透明不遮挡。
            window.CountdownDate.ApplyTemplate();
            var dateSurface = window.CountdownDate.Template.FindName("Surface", window.CountdownDate) as Border;
            var dateTextBox = window.CountdownDate.Template.FindName("PART_TextBox", window.CountdownDate)
                as System.Windows.Controls.Primitives.DatePickerTextBox;
            var dateButton = window.CountdownDate.Template.FindName("PART_Button", window.CountdownDate) as Button;
            var inputSurfacesWhitePaper =
                ReferenceEquals(window.CountdownName.Background, window.FindResource("PanelBrush")) &&
                ReferenceEquals(window.MonthlySalary.Background, window.FindResource("PanelBrush")) &&
                ReferenceEquals(window.CountdownTime.Background, window.FindResource("PanelBrush")) &&
                ReferenceEquals(window.DisplayPrecision.Background, window.FindResource("PanelBrush")) &&
                ReferenceEquals(window.CountdownDate.Background, window.FindResource("PanelBrush")) &&
                dateSurface is not null &&
                ReferenceEquals(dateSurface.Background, window.FindResource("PanelBrush")) &&
                dateTextBox is not null &&
                dateTextBox.Background is SolidColorBrush { Color.A: 0 } &&
                dateButton is not null &&
                dateButton.Background is SolidColorBrush { Color.A: 0 } &&
                !((Color)window.FindResource("PanelColor")).Equals((Color)window.FindResource("BackgroundColor"));

            // —— 文本选区高亮：独立语义色（不得复用 PeachLine 结构线色）且 0.4 合成肉眼可辨。
            var selectionBrush = window.CountdownName.SelectionBrush;
            var selectionIsIndependent =
                selectionBrush is SolidColorBrush selectionSolid &&
                ReferenceEquals(selectionBrush, window.FindResource("SelectionBrush")) &&
                selectionSolid.Color != (Color)window.FindResource("PeachLineColor");
            var selectionHighlightDistinct = false;
            if (selectionIsIndependent)
            {
                var panelColor = (Color)window.FindResource("PanelColor");
                var blended = selectionBrush is SolidColorBrush solid
                    ? Color.FromRgb(
                        (byte)Math.Round((0.4 * solid.Color.R) + (0.6 * panelColor.R)),
                        (byte)Math.Round((0.4 * solid.Color.G) + (0.6 * panelColor.G)),
                        (byte)Math.Round((0.4 * solid.Color.B) + (0.6 * panelColor.B)))
                    : panelColor;
                var maxChannelDelta = new[]
                {
                    Math.Abs(blended.R - panelColor.R),
                    Math.Abs(blended.G - panelColor.G),
                    Math.Abs(blended.B - panelColor.B)
                }.Max();
                selectionHighlightDistinct = maxChannelDelta >= 24;
            }

            // —— 状态条表面 + 错误字对比：状态栏消费 StatusBarBrush 表面；
            // 错误态 StatusText 前景为 DangerBrush 且对状态条表面 ≥ 4.5（不达标应改更深文字色）。
            var statusBarSurface = window.FindName("MainStatusBarSurface") as Border;
            var statusBarSurfaceErrorContrast =
                statusBarSurface is not null &&
                ReferenceEquals(statusBarSurface.Background, window.FindResource("StatusBarBrush")) &&
                ReferenceEquals(statusBarSurface.BorderBrush, window.FindResource("PeachLineBrush")) &&
                ReferenceEquals(window.StatusText.Foreground, window.FindResource("DangerBrush")) &&
                ApplicationTheme.MeetsContrast(
                    (Color)window.FindResource("StatusBarColor"),
                    (Color)window.FindResource("DangerColor"),
                    4.5);

            // —— 提醒规则选中圆点/描边：焦点·强调轨语义色 ActiveBrush，对编辑卡画布 ≥ 3:1。
            window.CountdownReminderNoRadio.ApplyTemplate();
            var radioDot = window.CountdownReminderNoRadio.Template.FindName("Dot", window.CountdownReminderNoRadio) as Ellipse;
            var radioRing = window.CountdownReminderNoRadio.Template.FindName("Ring", window.CountdownReminderNoRadio) as Ellipse;
            var editorSurfaceColor = window.CountdownEditorSurface.Background is SolidColorBrush surfaceBrush
                ? surfaceBrush.Color
                : (Color?)null;
            var reminderRadioSelectedContrast =
                window.CountdownReminderNoRadio.IsChecked == true &&
                radioDot is not null &&
                radioRing is not null &&
                radioDot.Visibility == Visibility.Visible &&
                ReferenceEquals(radioDot.Fill, window.FindResource("ActiveBrush")) &&
                ReferenceEquals(radioRing.Stroke, window.FindResource("ActiveBrush")) &&
                editorSurfaceColor is not null &&
                ApplicationTheme.MeetsContrast(editorSurfaceColor.Value, (Color)window.FindResource("ActiveColor"), 3.0);

            // —— 排版/间距 token：区块标题落 TypeH2（管理窗整体降一档）；字段行距/错误行距消费间距 token 值。
            var blockTitleType = (double)window.FindResource("TypeH2FontSize");
            var spacingUnit = (double)window.FindResource("SpacingUnit");
            var spacingFieldRow = (double)window.FindResource("SpacingFieldRow");
            var editorTypographyTokens =
                Math.Abs(window.CountdownEditorTitle.FontSize - blockTitleType) <= 0.01 &&
                Math.Abs(window.CountdownPreferencesTitle.FontSize - blockTitleType) <= 0.01 &&
                // #251：标题到首个字段行的 SpacingFieldRow 挂在标题下沿，折叠新增态随标题收起。
                Math.Abs(window.CountdownEditorHeading.Margin.Bottom - spacingFieldRow) <= 0.01 &&
                window.MainCountdownNameDateRow.Margin.Top <= 0.01 &&
                Math.Abs(window.MainCountdownTimeTagRow.Margin.Top - spacingFieldRow) <= 0.01 &&
                Math.Abs(window.MainCountdownActionRow.Margin.Top - spacingFieldRow) <= 0.01 &&
                Math.Abs(window.CountdownMessage.Margin.Top - spacingUnit) <= 0.01 &&
                Math.Abs(window.CountdownNameError.Margin.Top - spacingUnit) <= 0.01 &&
                Math.Abs(window.MonthlySalaryError.Margin.Top - spacingUnit) <= 0.01;

            return new CountdownEditingScenarios(
                fieldErrorsAtSource && dateOnlyFocusesDate,
                countdownFieldErrorsClear,
                singleErrorRowNotClipped,
                salaryErrorBelowField && workStartErrorBelowField,
                preferenceErrorsClear,
                primarySecondaryRolesDistinct,
                inputSurfacesWhitePaper,
                selectionHighlightDistinct,
                statusBarSurfaceErrorContrast,
                reminderRadioSelectedContrast,
                editorTypographyTokens);
        }
        finally
        {
            window.CountdownName.Text = originalName;
            window.CountdownDate.SelectedDate = originalDate;
            window.CountdownTime.Text = originalTime;
            window.MonthlySalary.Text = originalSalary;
            window.WorkStart.Text = originalWorkStart;
            window.WorkEnd.Text = originalWorkEnd;
            window.ClearCountdownFieldErrors();
            window.ClearPreferenceFieldErrors();
            UiAccessibility.Announce(window.CountdownMessage, string.Empty);
            window.ResetCountdownEditor();
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    // #151：字段错误可访问语义——错误行名称/帮助文本为完整消息，来源控件帮助文本指向错误。
    private static bool EditingValidationErrorIsAccessible(TextBlock field, Control source) =>
        !string.IsNullOrWhiteSpace(field.Text) &&
        string.Equals(AutomationProperties.GetName(field), field.Text, StringComparison.Ordinal) &&
        string.Equals(AutomationProperties.GetHelpText(field), field.Text, StringComparison.Ordinal) &&
        string.Equals(AutomationProperties.GetHelpText(source), field.Text, StringComparison.Ordinal) &&
        AutomationProperties.GetLiveSetting(field) == AutomationLiveSetting.Polite;

    // #152：主窗可操作性合同探针——常驻焦点环不挤布局、主按钮/置顶唯一焦点描边、
    // 未选中便签页签悬停不复用选中实心底、本地页签/帮助/单选/日期披露按压与禁用、
    // 帮助聚焦显示说明、默认缩放后命中 ≥24、筛选芯片与日历翻月紧凑几何。
    internal MainWindowInteractionStates ProbeMainWindowInteractionStates()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalExpanded = window.MainCountdownTimeTagRow.Visibility == Visibility.Visible;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            window.SetCountdownEditorExpanded(true);
            WindowValidationInput.Pump(window.Dispatcher);

            var focusRingsReserveSpace =
                FocusDoesNotChangeLayout(window.SaveCountdownButton, "FocusRing", "Surface") &&
                FocusDoesNotChangeLayout(window.TopmostCheck, "Box", "Box") &&
                FocusDoesNotChangeLayout(window.CountdownTab, "FocusRing", "Surface");

            // Check the same ownership/adorner contract used by production, then independently check geometry.
            var primaryAndPinSingleFocusStroke =
                WindowScenarioAccess.KeyboardPathControls(window).All(KeyboardFocusPolicy.HasValidAdornment) &&
                new FrameworkElement[] { window.SaveCountdownButton, window.SaveNoteButton, window.TopmostCheck,
                    window.CountdownTab, window.NotesTab, window.FilterButton, window.TagManagerButton, window.PreferencesButton }
                    .All(element => KeyboardFocusPolicy.UsesTemplateOrHostStroke(element) &&
                        KeyboardFocusPolicy.HasValidAdornment(element)) &&
                new DatePicker[] { window.CountdownDate, window.NoteDate }.All(picker =>
                {
                    picker.ApplyTemplate();
                    return picker.Template?.FindName("PART_Button", picker) is Control button &&
                        KeyboardFocusPolicy.HasValidAdornment(button);
                }) &&
                window.CountdownListRegion.FocusVisualStyle is Style regularStroke &&
                RegularFocusVisualThicknessIs(regularStroke, 2);

            var notesTabHoverDiffersFromSelected = NotesTabHoverDoesNotReuseSelectedFill();

            // #191：所有按钮的悬停反馈必须与常态有可感知的色差，并且沿暖粉
            // Card/ActiveHover 角色走统一状态语言；不能只依赖默认 WPF 蓝色。
            var hoverStateContrastIsPerceivable = HoverStateContrastIsPerceivable();

            // #191：按压只改变颜色，不改变边厚或控件外框。全局模板也必须
            // 预留 2px 透明环，避免从静止到按下时挤动同级元素。
            var pressedStateGeometryStable = PressedStateGeometryIsStable();

            // #191：焦点环由键盘焦点视觉样式提供，鼠标点击不应留下环；Tab
            // 路径仍须为可聚焦控件提供独立的键盘焦点样式。
            var mouseClickDoesNotLeaveFocusRing = MouseClickDoesNotLeaveFocusRing();
            var keyboardFocusRingShowsOnTab = KeyboardFocusRingShowsOnTab();

            // 选中页签保留 SemiBold；#337 的 PinkTint 底层胶囊与透明 Surface 由独立窗口事实验收。
            var selectedTabWeightDominates = SelectedTabWeightDominates();

            // 按压视觉无法在无鼠标隔离桌面驱动（ButtonBase 需真实 IsMouseOver 才置 IsPressed），
            // 改按模板触发器合同钉住：IsPressed → 状态环 ActiveBrush（与键盘焦点同语言）；禁用走运行态。
            var localControlsPressAndDisabled =
                TemplateHasPressRingTrigger(window.CountdownTab, "FocusRing") &&
                DisabledUses45Opacity(window.CountdownTab, "InteractionSurface") &&
                TemplateHasPressRingTrigger(window.CountdownReminderHelp, "FocusRing") &&
                DisabledUses45Opacity(window.CountdownReminderHelp, "Surface") &&
                TemplateHasPressRingTrigger(window.CountdownReminderNoRadio, "FocusRing") &&
                DisabledUses45Opacity(window.CountdownReminderNoRadio, "Surface") &&
                DateDisclosureTemplateHasPressAndDisabled();

            var segmentIndicators = ProbeSegmentIndicatorScenarios();

            // 聚焦帮助显示与悬停相同的说明（同一 ToolTip 即时打开），并提供帮助文本。
            window.SetCountdownEditorExpanded(true);
            WindowValidationInput.Pump(window.Dispatcher);
            window.Activate();
            var helpTooltip = window.CountdownReminderHelp.ToolTip as ToolTip;
            Keyboard.Focus(window.CountdownReminderHelp);
            WindowValidationInput.Pump(window.Dispatcher);
            var helpOpensOnFocus = helpTooltip is not null && helpTooltip.IsOpen;
            var helpTextProvided =
                helpTooltip?.Content is string tooltipText &&
                tooltipText.Length > 0 &&
                string.Equals(
                    AutomationProperties.GetHelpText(window.CountdownReminderHelp),
                    tooltipText,
                    StringComparison.Ordinal);
            Keyboard.Focus(window.CloseButton);
            WindowValidationInput.Pump(window.Dispatcher);
            var helpClosesOnBlur = helpTooltip is not null &&
                WindowValidationInput.WaitUntil(window.Dispatcher, () => !helpTooltip.IsOpen);
            var helpFocusShowsHelpText = helpOpensOnFocus && helpTextProvided && helpClosesOnBlur;
            if (!helpFocusShowsHelpText)
            {
                Console.Error.WriteLine("HELP_FOCUS_DIAGNOSTIC " +
                    System.Text.Json.JsonSerializer.Serialize(new { helpOpensOnFocus, helpTextProvided, helpClosesOnBlur }));
            }

            // #208: the 864 x 1000 canvas is laid out at 1:1 DIP, so the
            // compact controls keep their 28 DIP hit slots without a Viewbox.
            var scale = window.MainScaleViewport.ActualWidth > 0
                ? window.MainScaleViewport.ActualWidth / window.MainLayoutSurface.Width
                : 0;
            var compactHitTargetsMeetScaledFloor =
                Math.Abs(window.CountdownReminderHelp.ActualWidth - 28) <= 1 &&
                Math.Abs(window.CountdownReminderHelp.ActualHeight - 28) <= 1 &&
                Math.Abs(scale - 1) <= 0.02 &&
                28 * scale >= 24;

            // 筛选芯片与日历翻月按钮向同一紧凑下限靠拢（高度 36、相邻间距 8）。
            var chipsAndCalendarNavCompact =
                StyleHeightSetterIs("FilterOptionButton", 36) &&
                StyleSetterIs("CalendarNavigationButton", FrameworkElement.MinHeightProperty, 36) &&
                StyleSetterIs("CalendarNavigationButton", FrameworkElement.WidthProperty, 36) &&
                RequiredToggleButtonMargin("AllTagMode") &&
                RequiredToggleButtonMargin("NoteActiveFilter") &&
                FilterTagChipGapsMeetCompactSpacing();

            return new MainWindowInteractionStates(
                focusRingsReserveSpace,
                primaryAndPinSingleFocusStroke,
                notesTabHoverDiffersFromSelected,
                localControlsPressAndDisabled,
                helpFocusShowsHelpText,
                compactHitTargetsMeetScaledFloor,
                chipsAndCalendarNavCompact,
                hoverStateContrastIsPerceivable,
                pressedStateGeometryStable,
                mouseClickDoesNotLeaveFocusRing,
                keyboardFocusRingShowsOnTab,
                selectedTabWeightDominates,
                segmentIndicators.GeometryMatches,
                segmentIndicators.ReducedMotionIsImmediate,
                segmentIndicators.RapidSwitchStaysAligned);
        }
        finally
        {
            window.SetCountdownEditorExpanded(originalExpanded);
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    // 快速往返与关闭动画继续覆盖两个统一滑动胶囊实例的终态；几何、边框、
    // 动态计数和对比度由独立 WindowVerification 验收。
    private SegmentIndicatorScenarios ProbeSegmentIndicatorScenarios()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalCompleted = window.CompletedNotesSegment.IsChecked == true;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            window.Activate();
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var countdownTab = ObserveIndicator(
                window.CountdownTab, window.MainWorkspaceTabHost, window.MainWorkspaceTabIndicator);

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var notesTab = ObserveIndicator(
                window.NotesTab, window.MainWorkspaceTabHost, window.MainWorkspaceTabIndicator);
            var activeSegment = ObserveIndicator(
                window.ActiveNotesSegment, window.NotesSegmentHost, window.NotesSegmentIndicator);

            window.SelectNotesSegment(showCompleted: true);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var completedSegment = ObserveIndicator(
                window.CompletedNotesSegment, window.NotesSegmentHost, window.NotesSegmentIndicator);

            var rapidSwitchStaysAligned =
                window.SegmentIndicatorRapidSwitchIsStableForValidation;
            var reducedMotionIsImmediate =
                window.SegmentIndicatorReducedMotionIsImmediateForValidation;
            var observations = new[] { countdownTab, notesTab, activeSegment, completedSegment };

            return new SegmentIndicatorScenarios(
                observations.All(observation => observation),
                reducedMotionIsImmediate,
                rapidSwitchStaysAligned);
        }
        finally
        {
            window.SelectNotesSegment(originalCompleted);
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }

        bool ObserveIndicator(
            Control target,
            FrameworkElement root,
            Border border)
        {
            var aligned = WindowValidationInput.WaitUntil(
                window.Dispatcher,
                () => WindowScenarioAccess.SegmentIndicatorAligned(root, target, border));
            if (!aligned)
            {
                var targetBounds = target.TransformToAncestor(root).TransformBounds(new Rect(target.RenderSize));
                Console.Error.WriteLine("SEGMENT_GEOMETRY_DIAGNOSTIC " +
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        target.Name,
                        target.IsVisible,
                        Actual = border.TransformToAncestor(root)
                            .TransformBounds(new Rect(border.RenderSize))
                            .ToString(CultureInfo.InvariantCulture),
                        Target = targetBounds.ToString(CultureInfo.InvariantCulture)
                    }));
            }

            return aligned;
        }
    }

    // #242：应用自己处理的键盘路径（Ctrl+1/2、倒计时页 Ctrl+F、Ctrl+T、弹层焦点生命周期 Esc）
    // 显式要求即时：按键后的首个真实 Rendering 帧已是终态且没有动画时钟。鼠标点击页签与
    // 弹层仍以动画提交。两种客户区动画偏好各跑一遍；关闭动画时两条路径都直达终态。
    private MainKeyboardImmediateMotionScenarios ProbeKeyboardImmediateMotionScenarios(ApplicationTheme theme)
    {
        var originalWorkspace = window.CurrentWorkspace;
        var checks = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            window.Activate();
            foreach (var animationsEnabled in new[] { true, false })
            {
                var mode = animationsEnabled ? "Animated" : "Reduced";
                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(animationsEnabled))
                {
                    // 动效 token 只在 Apply 时按当前偏好写入资源。
                    theme.Apply();
                    checks[mode + "WorkspaceShortcutFirstFrame"] =
                        ProbeWorkspaceShortcutFirstFrame(WorkspaceKind.Countdowns, Key.D2) &&
                        ProbeWorkspaceShortcutFirstFrame(WorkspaceKind.Notes, Key.D1);
                    checks[mode + "WorkspaceClick"] = ProbeWorkspaceTabClick(animationsEnabled);
                    checks[mode + "FilterShortcutFirstFrame"] = ProbePopupShortcutFirstFrame(Key.F, window.FilterPopup);
                    checks[mode + "TagManagerShortcutFirstFrame"] =
                        ProbePopupShortcutFirstFrame(Key.T, window.TagManagerPopup);
                    checks[mode + "EscapeFromPopupFirstFrame"] = ProbeFilterEscapeFirstFrame(escapeFromTrigger: false);
                    checks[mode + "EscapeFromTriggerFirstFrame"] = ProbeFilterEscapeFirstFrame(escapeFromTrigger: true);
                    checks[mode + "FilterClick"] = ProbeFilterButtonClick(animationsEnabled);
                }
            }
        }
        finally
        {
            theme.Apply();
            WindowValidationInput.ClosePopupForCleanup(window.FilterPopup);
            WindowValidationInput.ClosePopupForCleanup(window.TagManagerPopup);
            ShowWorkspace(originalWorkspace);
            window.CountdownTab.Focus();
            WindowValidationInput.Pump(window.Dispatcher);
        }

        return new MainKeyboardImmediateMotionScenarios(checks);
    }

    private bool ProbeWorkspaceShortcutFirstFrame(WorkspaceKind startingWorkspace, Key key)
    {
        var expectedWorkspace = startingWorkspace == WorkspaceKind.Countdowns
            ? WorkspaceKind.Notes
            : WorkspaceKind.Countdowns;
        ShowWorkspace(startingWorkspace);
        if (!WaitForWorkspaceIndicatorAligned(WorkspaceTab(startingWorkspace)))
        {
            return false;
        }

        window.CountdownTab.Focus();
        WindowValidationInput.Pump(window.Dispatcher);
        var handled = WindowValidationInput.RaisePreviewKey(window.CountdownTab, key, ModifierKeys.Control);
        return handled && ObserveFirstRenderedFrame(() =>
            window.CurrentWorkspace == expectedWorkspace &&
            WorkspaceIndicatorImmediatelyAt(WorkspaceTab(expectedWorkspace)));
    }

    private bool ProbeWorkspaceTabClick(bool animationsEnabled)
    {
        ShowWorkspace(WorkspaceKind.Countdowns);
        if (!WaitForWorkspaceIndicatorAligned(window.CountdownTab))
        {
            return false;
        }

        window.NotesTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        // 鼠标路径保持既有提交时机：选中项完成排版后，在 Loaded 回调里开始移动。
        window.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
        var committedWithAnimation = WorkspaceIndicatorAnimating();
        var committedAtRest = WorkspaceIndicatorImmediatelyAt(window.NotesTab);
        var settled = window.CurrentWorkspace == WorkspaceKind.Notes && WaitForWorkspaceIndicatorAligned(window.NotesTab);
        return settled && (animationsEnabled ? committedWithAnimation : committedAtRest);
    }

    private bool ProbePopupShortcutFirstFrame(Key key, Popup popup)
    {
        ShowWorkspace(WorkspaceKind.Countdowns);
        WindowValidationInput.ClosePopupForCleanup(popup);
        window.CountdownTab.Focus();
        WindowValidationInput.Pump(window.Dispatcher);
        try
        {
            var handled = WindowValidationInput.RaisePreviewKey(window.CountdownTab, key, ModifierKeys.Control);
            return handled && ObserveFirstRenderedFrame(() =>
                popup.IsOpen &&
                !PopupMotionBehavior.IsClosing(popup) &&
                PopupSurfaceAtRest(popup, opacity: 1, offsetY: 0));
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
        }
    }

    private bool ProbeFilterEscapeFirstFrame(bool escapeFromTrigger)
    {
        ShowWorkspace(WorkspaceKind.Countdowns);
        WindowValidationInput.ClosePopupForCleanup(window.FilterPopup);
        window.CountdownTab.Focus();
        WindowValidationInput.Pump(window.Dispatcher);
        try
        {
            var filterInitialFocus = WindowScenarioAccess.FilterInitialFocusTarget(window);
            // 弹层打开后在 Input 优先级把焦点交给首项；等焦点进层再按 Esc。
            if (!WindowValidationInput.RaisePreviewKey(window.CountdownTab, Key.F, ModifierKeys.Control) ||
                !window.FilterPopup.IsOpen ||
                !WindowValidationInput.WaitUntil(window.Dispatcher, () => filterInitialFocus.IsKeyboardFocusWithin, 1000))
            {
                return false;
            }

            UIElement escapeSource = filterInitialFocus;
            if (escapeFromTrigger)
            {
                Keyboard.Focus(window.FilterButton);
                WindowValidationInput.Pump(window.Dispatcher);
                if (!window.FilterButton.IsKeyboardFocused || !window.FilterPopup.IsOpen)
                {
                    return false;
                }

                escapeSource = window.FilterButton;
            }

            // #244：关闭后的静止位在触发点一侧，按关闭前弹层与触发点的屏幕位置判定。
            var restOffsetY = PopupRestOffsetY(window.FilterPopup, window.FilterButton);
            var handled = WindowValidationInput.RaisePreviewKey(escapeSource, Key.Escape);
            var closedOnFirstFrame = restOffsetY is { } offsetY && ObserveFirstRenderedFrame(() =>
                !window.FilterPopup.IsOpen &&
                !PopupMotionBehavior.IsClosing(window.FilterPopup) &&
                PopupSurfaceAtRest(window.FilterPopup, opacity: 0, offsetY: offsetY));
            var focusReturned = WindowValidationInput.WaitUntil(window.Dispatcher, () =>
                window.FilterButton.IsKeyboardFocusWithin ||
                ReferenceEquals(FocusManager.GetFocusedElement(window), window.FilterButton), 1000);
            return handled && closedOnFirstFrame && focusReturned && !window.FilterPopup.IsOpen;
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(window.FilterPopup);
        }
    }

    private bool ProbeFilterButtonClick(bool animationsEnabled)
    {
        ShowWorkspace(WorkspaceKind.Countdowns);
        WindowValidationInput.ClosePopupForCleanup(window.FilterPopup);
        Keyboard.Focus(window.FilterButton);
        WindowValidationInput.Pump(window.Dispatcher);
        try
        {
            window.FilterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var committedWithAnimation = window.FilterPopup.IsOpen && PopupSurfaceAnimating(window.FilterPopup);
            var committedAtRest = window.FilterPopup.IsOpen && PopupSurfaceAtRest(window.FilterPopup, opacity: 1, offsetY: 0);
            var settled = WindowValidationInput.WaitUntil(window.Dispatcher, () =>
                window.FilterPopup.IsOpen && PopupSurfaceAtRest(window.FilterPopup, opacity: 1, offsetY: 0), 1000);
            return settled && (animationsEnabled ? committedWithAnimation : committedAtRest);
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(window.FilterPopup);
        }
    }

    // 只在请求后的第一次 CompositionTarget.Rendering 回调里观察一次：之前的同步状态与之后
    // 稳定下来的状态都不算，用来判定“按键后的首帧”。
    private bool ObserveFirstRenderedFrame(Func<bool> observation)
    {
        var observed = false;
        var firstFrame = WindowValidationInput.PumpRenderedFrames(window.Dispatcher, 1000, () =>
        {
            observed = observation();
            return true;
        });
        return firstFrame.Completed && observed;
    }

    private ToggleButton WorkspaceTab(WorkspaceKind workspace) =>
        workspace == WorkspaceKind.Countdowns ? window.CountdownTab : window.NotesTab;

    private bool WorkspaceIndicatorAnimating() =>
        DependencyPropertyHelper.GetValueSource(
            window.MainWorkspaceTabIndicator,
            FrameworkElement.WidthProperty).IsAnimated ||
        window.MainWorkspaceTabIndicator.RenderTransform is Transform { HasAnimatedProperties: true };

    // 动画结束后 HoldEnd 时钟仍可能挂在 Width/RenderTransform 上，等待就位只看几何；
    // “没有动画时钟”只用来判定即时路径（即时提交会清掉两类旧时钟）。
    private bool WaitForWorkspaceIndicatorAligned(FrameworkElement tab) =>
        WindowValidationInput.WaitUntil(
            window.Dispatcher,
            () => WindowScenarioAccess.SegmentIndicatorAligned(
                window.MainWorkspaceTabHost,
                (Control)tab,
                window.MainWorkspaceTabIndicator),
            1000);

    private bool WorkspaceIndicatorImmediatelyAt(FrameworkElement tab) =>
        WindowScenarioAccess.SegmentIndicatorAligned(
            window.MainWorkspaceTabHost,
            (Control)tab,
            window.MainWorkspaceTabIndicator) && !WorkspaceIndicatorAnimating();

    private static bool PopupSurfaceAnimating(Popup popup) =>
        popup.Child is FrameworkElement visual &&
        (DependencyPropertyHelper.GetValueSource(visual, UIElement.OpacityProperty).IsAnimated ||
         PopupSurfaceTranslation(visual)?.HasAnimatedProperties == true);

    private static bool PopupSurfaceAtRest(Popup popup, double opacity, double offsetY) =>
        popup.Child is FrameworkElement visual &&
        PopupSurfaceTranslation(visual) is { } translation &&
        Math.Abs(visual.Opacity - opacity) <= 0.000001 &&
        Math.Abs(translation.Y - offsetY) <= 0.000001 &&
        !PopupSurfaceAnimating(popup);

    private static TranslateTransform? PopupSurfaceTranslation(FrameworkElement visual) =>
        visual.RenderTransform switch
        {
            TranslateTransform direct => direct,
            TransformGroup group => group.Children.OfType<TranslateTransform>().LastOrDefault(),
            _ => null
        };

    // #244：弹层在触发点下方时静止位是 -4，在上方时是 +4。弹层与触发点分属不同 HWND，
    // 只比较屏幕坐标的纵向中心；任一方没有连上窗口时取不到。
    private static double? PopupRestOffsetY(Popup popup, FrameworkElement trigger)
    {
        if (popup.Child is not FrameworkElement visual ||
            PresentationSource.FromVisual(visual) is null ||
            PresentationSource.FromVisual(trigger) is null)
        {
            return null;
        }

        static double CenterY(FrameworkElement element) =>
            (element.PointToScreen(new Point(0, 0)).Y +
             element.PointToScreen(new Point(0, element.ActualHeight)).Y) / 2;

        return CenterY(visual) < CenterY(trigger) ? 4 : -4;
    }

    // 聚焦前后控件外尺寸与状态部件边厚都不变——焦点环是常驻透明环，只换色不加厚。
    private bool FocusDoesNotChangeLayout(Control control, string ringName, string surfaceName)
    {
        control.ApplyTemplate();
        var ring = control.Template?.FindName(ringName, control) as Border;
        var surface = control.Template?.FindName(surfaceName, control) as FrameworkElement;
        if (ring is null || surface is null)
        {
            return false;
        }

        window.Activate();
        var widthBefore = control.ActualWidth;
        var heightBefore = control.ActualHeight;
        var ringThicknessBefore = ring.BorderThickness;
        var focusVisualBefore = control.FocusVisualStyle;
        Keyboard.Focus(control);
        WindowValidationInput.Pump(window.Dispatcher);
        var layoutStable =
            Math.Abs(control.ActualWidth - widthBefore) <= 0.01 &&
            Math.Abs(control.ActualHeight - heightBefore) <= 0.01 &&
            ring.BorderThickness == ringThicknessBefore &&
            ReferenceEquals(control.FocusVisualStyle, focusVisualBefore);
        Keyboard.Focus(window.CloseButton);
        WindowValidationInput.Pump(window.Dispatcher);
        return layoutStable;
    }

    // XAML 模板没有 FrameworkElementFactory（VisualTree 为 null），把模板临时挂到窗口内
    // 实例化后读取可视子 Border 的边厚，验证常规 FocusVisualStyle 是 2px 暖边。
    private bool RegularFocusVisualThicknessIs(Style focusVisual, double expected)
    {
        var template = focusVisual.Setters
            .OfType<Setter>()
            .Where(setter => setter.Property == Control.TemplateProperty)
            .Select(setter => setter.Value)
            .OfType<ControlTemplate>()
            .FirstOrDefault();
        if (template is null)
        {
            return false;
        }

        var host = new Control
        {
            Template = template,
            Width = 10,
            Height = 10,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false
        };
        window.MainContentProjection.Children.Add(host);
        try
        {
            host.ApplyTemplate();
            WindowValidationInput.Pump(window.Dispatcher);
            if (System.Windows.Media.VisualTreeHelper.GetChild(host, 0) is not Border border)
            {
                return false;
            }

            return Math.Max(
                Math.Max(border.BorderThickness.Left, border.BorderThickness.Right),
                Math.Max(border.BorderThickness.Top, border.BorderThickness.Bottom)) == expected;
        }
        finally
        {
            window.MainContentProjection.Children.Remove(host);
        }
    }

    // 未选中「便签」页签 hover 使用独立暖底/暖边；选中态显式解析透明资源，
    // 避免 checked+hover 回落到 hover，PinkTint 只由底层滑动胶囊绘制。
    private bool NotesTabHoverDoesNotReuseSelectedFill()
    {
        if (window.NotesTab.Template is not { } template)
        {
            return false;
        }

        Trigger? hoverTrigger = null;
        Trigger? checkedTrigger = null;
        foreach (var trigger in template.Triggers.OfType<Trigger>())
        {
            if (trigger.Property == UIElement.IsMouseOverProperty)
            {
                hoverTrigger = trigger;
            }

            if (trigger.Property == ToggleButton.IsCheckedProperty)
            {
                checkedTrigger = trigger;
            }
        }

        var motionSurface = template.FindName("InteractionSurface", window.NotesTab) as FrameworkElement;
        var motionUsesDistinctTokens = motionSurface is not null &&
            MotionBehavior.GetIsEnabled(motionSurface) &&
            string.Equals(
                MotionBehavior.GetHoverBackgroundResourceKey(motionSurface),
                "CardAltBrush",
                StringComparison.Ordinal) &&
            string.Equals(
                MotionBehavior.GetHoverBorderBrushResourceKey(motionSurface),
                "ActiveHoverBrush",
                StringComparison.Ordinal) &&
            string.Equals(
                MotionBehavior.GetCheckedBackgroundResourceKey(motionSurface),
                "MainWorkspaceTabTransparentBrush",
                StringComparison.Ordinal);

        return motionUsesDistinctTokens ||
            (hoverTrigger is not null &&
             checkedTrigger is not null &&
             !SetsBackgroundTo(hoverTrigger, "Surface", "PeachTintBrush") &&
             SetsBackgroundTo(checkedTrigger, "Surface", "PeachTintBrush") &&
             SetsBorderBrushTo(hoverTrigger, "Surface", "ActiveBrush"));
    }

    // #191：悬停必须有可感知的暖色反馈。画布上的按钮使用 CardAltBrush（比常态
    // CanvasBrush 深一档），同时把桃色结构线换成更深的 ActiveHoverBrush；主按钮
    // 和页签可使用各自的同族语义色，但不得继续依赖系统默认蓝色。
    private bool HoverStateContrastIsPerceivable()
    {
        if (Application.Current.FindResource("CanvasBrush") is not Brush normal ||
            Application.Current.FindResource("CardAltBrush") is not Brush cardHover ||
            Application.Current.FindResource("PeachLineBrush") is not Brush peachLine ||
            Application.Current.FindResource("ActiveHoverBrush") is not Brush activeHover ||
            !ApplicationTheme.MeetsContrast(normal, cardHover, 1.04) ||
            !ApplicationTheme.MeetsContrast(peachLine, activeHover, 2.0))
        {
            return false;
        }

        var controls = new Control[]
        {
            window.SaveCountdownButton,
            window.SavePreferencesButton,
            window.SaveNoteButton,
            window.CloseButton,
            window.FilterButton,
            window.CountdownTab,
            window.NotesTab,
            window.CountdownReminderHelp,
            window.CountdownReminderNoRadio
        };

        var failures = controls.Where(control => !TemplateHasHoverFeedback(control))
            .Select(control => control.Name).ToArray();
        if (failures.Length > 0)
        {
            Console.Error.WriteLine("HOVER_FEEDBACK_DIAGNOSTIC " +
                System.Text.Json.JsonSerializer.Serialize(failures));
        }

        return failures.Length == 0;
    }

    private static bool TemplateHasHoverFeedback(Control control)
    {
        control.ApplyTemplate();
        if (control.Template is not { } template)
        {
            return false;
        }

        if (template.FindName("InteractionSurface", control) is FrameworkElement motionSurface &&
            MotionBehavior.GetIsEnabled(motionSurface))
        {
            return true;
        }

        if (template.Triggers is not { } triggers)
        {
            return false;
        }

        return triggers
            .OfType<Trigger>()
            .Where(trigger => trigger.Property == UIElement.IsMouseOverProperty)
            .SelectMany(trigger => trigger.Setters.OfType<Setter>())
            .Any(setter =>
                setter.TargetName is "Surface" or "FocusRing" or "Circle" or "Ring" or "Box" &&
                (setter.Property == Border.BackgroundProperty ||
                    setter.Property == Border.BorderBrushProperty ||
                    (setter.Property == Shape.StrokeProperty &&
                     template.FindName(setter.TargetName, control) is Shape &&
                     MarkupResourceKeyIs(setter.Value, "ActiveHoverBrush")) ||
                    setter.Property == UIElement.OpacityProperty));
    }

    // #191：模板的 IsPressed 触发器只能改变颜色、透明度或 RenderTransform；边厚
    // 与常驻状态环必须固定为 2px。检查真实主窗控件，覆盖全局隐式模板与窗口内模板。
    private bool PressedStateGeometryIsStable()
    {
        var controls = new Control[]
        {
            window.SaveCountdownButton,
            window.SavePreferencesButton,
            window.SaveNoteButton,
            window.CloseButton,
            window.FilterButton,
            window.CountdownTab,
            window.NotesTab,
            window.CountdownReminderHelp,
            window.CountdownReminderNoRadio,
            window.CountdownReminderTimeRadio,
            window.CountdownReminderCustomRadio,
            window.TopmostCheck
        };

        return controls.All(TemplateReservesPressRing);
    }

    private static bool TemplateReservesPressRing(Control control)
    {
        control.ApplyTemplate();
        if (control.Template is not { } template)
        {
            return false;
        }

        var ring = template.FindName("FocusRing", control) as Border ??
            template.FindName("Box", control) as Border;
        if (ring is null)
        {
            return false;
        }

        var constantRing = Application.Current.FindResource("FocusBorderThickness") is Thickness expectedThickness &&
            ring.BorderThickness == expectedThickness;
        var pressedDoesNotResize = template.Triggers
            .OfType<Trigger>()
            .Where(trigger => trigger.Property == ButtonBase.IsPressedProperty)
            .SelectMany(trigger => trigger.Setters.OfType<Setter>())
            .All(setter => setter.Property != Border.BorderThicknessProperty);
        return constantRing && pressedDoesNotResize;
    }

    // Start from a keyboard focus ring, then use the same native pointer seam
    // as the date/combobox probes. Only product input handlers change modality.
    private bool MouseClickDoesNotLeaveFocusRing()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalCompleted = window.CompletedNotesSegment.IsChecked == true;
        var originalName = window.CountdownName.Text;
        var originalMessage = window.CountdownMessage.Text;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            window.Activate();
            WindowValidationInput.Pump(window.Dispatcher);
            // A required empty name keeps the real save click inside the
            // validation fixture, without creating a countdown as a side effect.
            window.CountdownName.Text = string.Empty;
            var saveWorks = PointerRemovesKeyboardRing(window.SaveCountdownButton);
            var countdownTabWorks = PointerRemovesKeyboardRing(window.CountdownTab);
            var notesTabWorks = PointerRemovesKeyboardRing(window.NotesTab);
            var activeWorks = PointerRemovesKeyboardRing(window.ActiveNotesSegment);
            var completedWorks = PointerRemovesKeyboardRing(window.CompletedNotesSegment);
            if (!(saveWorks && countdownTabWorks && notesTabWorks && activeWorks && completedWorks))
            {
                Console.Error.WriteLine("POINTER_FOCUS_DIAGNOSTIC " +
                    System.Text.Json.JsonSerializer.Serialize(new { saveWorks, countdownTabWorks, notesTabWorks, activeWorks, completedWorks }));
            }

            return saveWorks && countdownTabWorks && notesTabWorks && activeWorks && completedWorks;
        }
        finally
        {
            window.CountdownName.Text = originalName;
            window.ClearCountdownFieldErrors();
            UiAccessibility.Announce(window.CountdownMessage, originalMessage);
            window.SelectNotesSegment(originalCompleted);
            ShowWorkspace(originalWorkspace);
            Keyboard.Focus(window.CloseButton);
            WindowValidationInput.Pump(window.Dispatcher);
        }

        bool PointerRemovesKeyboardRing(Control control)
        {
            control.BringIntoView();
            Keyboard.Focus(window.CloseButton);
            Keyboard.Focus(control);
            WindowValidationInput.Pump(window.Dispatcher);
            control.ApplyTemplate();
            var ring = control.Template?.FindName("FocusRing", control) as Border;
            var keyboardRingWasVisible = control.IsKeyboardFocused &&
                FocusVisualState.GetIsKeyboardFocusVisible(control) &&
                ring is not null && !IsTransparent(ring.BorderBrush);
            Keyboard.Focus(window.CloseButton);
            WindowValidationInput.Pump(window.Dispatcher);
            var pointerFocused = WindowValidationInput.ClickForFocusValidation(control);
            return keyboardRingWasVisible && pointerFocused &&
                !FocusVisualState.GetIsKeyboardFocusVisible(control) &&
                ring is not null && IsTransparent(ring.BorderBrush);
        }
    }

    private bool KeyboardFocusRingShowsOnTab()
    {
        var originalWorkspace = window.CurrentWorkspace;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            window.Activate();
            WindowValidationInput.Pump(window.Dispatcher);
            var saveButtonWorks = TabReachesVisibleRing(
                window.CountdownEditorExpandButton, window.SaveCountdownButton, FocusNavigationDirection.Previous, "MainActionFocusBrush");

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);
            var completedWorks = TabReachesVisibleRing(
                window.ActiveNotesSegment, window.CompletedNotesSegment, FocusNavigationDirection.Next, "ActiveBrush");
            var activeWorks = TabReachesVisibleRing(
                window.CompletedNotesSegment, window.ActiveNotesSegment, FocusNavigationDirection.Previous, "ActiveBrush");
            return saveButtonWorks && completedWorks && activeWorks;
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
            Keyboard.Focus(window.CloseButton);
            WindowValidationInput.Pump(window.Dispatcher);
        }

        bool TabReachesVisibleRing(UIElement from, Control target, FocusNavigationDirection direction, string resourceKey)
        {
            Keyboard.Focus(from);
            WindowValidationInput.Pump(window.Dispatcher);
            var moved = from.MoveFocus(new TraversalRequest(direction));
            WindowValidationInput.Pump(window.Dispatcher);
            target.ApplyTemplate();
            return moved && target.IsKeyboardFocused &&
                target.Template?.FindName("FocusRing", target) is Border ring &&
                ring.BorderThickness == new Thickness(2) &&
                UsesResourceBrush(ring.BorderBrush, resourceKey);
        }
    }

    // #337 后 PinkTint 由独立验证的底层滑动胶囊承载；这里仅保留既有 SemiBold 权重回归。
    private bool SelectedTabWeightDominates()
    {
        var originalWorkspace = window.CurrentWorkspace;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var countdownDominates = SelectedTabHasWeight(window.CountdownTab, window.NotesTab);
            ShowWorkspace(WorkspaceKind.Notes);
            return countdownDominates && SelectedTabHasWeight(window.NotesTab, window.CountdownTab);
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private bool SelectedTabHasWeight(ToggleButton selected, ToggleButton unselected)
    {
        return WindowValidationInput.WaitUntil(window.Dispatcher, () =>
            selected.IsChecked == true &&
            unselected.IsChecked == false &&
            selected.FontWeight == FontWeights.SemiBold &&
            unselected.FontWeight == FontWeights.Normal);
    }

    private static bool IsTransparent(Brush? brush) =>
        brush is SolidColorBrush solid && solid.Color.A == 0;

    private static bool UsesResourceBrush(Brush? brush, string resourceKey) =>
        brush is SolidColorBrush actual &&
        Application.Current.FindResource(resourceKey) is SolidColorBrush expected &&
        actual.Color == expected.Color;

    private static bool SetsBackgroundTo(Trigger trigger, string targetName, string resourceKey) =>
        trigger.Setters.OfType<Setter>().Any(setter =>
            setter.TargetName == targetName &&
            setter.Property == Border.BackgroundProperty &&
            MarkupResourceKeyIs(setter.Value, resourceKey));

    private static bool SetsBorderBrushTo(Trigger trigger, string targetName, string resourceKey) =>
        trigger.Setters.OfType<Setter>().Any(setter =>
            setter.TargetName == targetName &&
            setter.Property == Border.BorderBrushProperty &&
            MarkupResourceKeyIs(setter.Value, resourceKey));

    private static bool MarkupResourceKeyIs(object? value, string resourceKey) =>
        value is System.Windows.DynamicResourceExtension dynamic &&
        dynamic.ResourceKey as string == resourceKey;

    // 按压与键盘焦点同语言：模板存在 IsPressed → 状态环 ActiveBrush 的触发器。
    private static bool TemplateHasPressRingTrigger(Control control, string ringName)
    {
        control.ApplyTemplate();
        if (control.Template?.Triggers is not { } triggers)
        {
            return false;
        }

        return triggers
            .OfType<Trigger>()
            .Where(trigger => trigger.Property == ButtonBase.IsPressedProperty)
            .SelectMany(trigger => trigger.Setters.OfType<Setter>())
            .Any(setter =>
                setter.TargetName == ringName &&
                setter.Property == Border.BorderBrushProperty &&
                MarkupResourceKeyIs(setter.Value, "ActiveBrush"));
    }

    private bool DisabledUses45Opacity(Control control, string partName)
    {
        control.ApplyTemplate();
        if (control.Template?.FindName(partName, control) is not FrameworkElement part)
        {
            return false;
        }

        var original = control.IsEnabled;
        control.IsEnabled = false;
        WindowValidationInput.Pump(window.Dispatcher);
        var disabledOpacity = part.Opacity;
        control.IsEnabled = original;
        WindowValidationInput.Pump(window.Dispatcher);
        return Math.Abs(disabledOpacity - 0.45) <= 0.001;
    }

    // 日期披露按钮由代码构建，模板触发器检查通过窗口内样式资源进行（按压=环 Active、禁用=0.45）。
    private bool DateDisclosureTemplateHasPressAndDisabled()
    {
        if ((Style)window.MainContentProjection.FindResource("MainNoteDateDisclosureButton") is not { } style)
        {
            return false;
        }

        var template = style.Setters
            .OfType<Setter>()
            .Where(setter => setter.Property == Control.TemplateProperty)
            .Select(setter => setter.Value)
            .OfType<ControlTemplate>()
            .FirstOrDefault();
        if (template?.Triggers is not { } triggers)
        {
            return false;
        }

        var hasPressRing = triggers
            .OfType<Trigger>()
            .Where(trigger => trigger.Property == ButtonBase.IsPressedProperty)
            .SelectMany(trigger => trigger.Setters.OfType<Setter>())
            .Any(setter =>
                setter.TargetName == "FocusRing" &&
                setter.Property == Border.BorderBrushProperty &&
                MarkupResourceKeyIs(setter.Value, "ActiveBrush"));
        var probe = new Button { Style = style };
        var hasDisabledOpacity = DisabledUses45Opacity(probe, "InteractionSurface");
        return hasPressRing && hasDisabledOpacity;
    }

    private static bool StyleHeightSetterIs(string styleKey, double expected) =>
        StyleSetterIs(styleKey, FrameworkElement.HeightProperty, expected);

    private static bool StyleSetterIs(string styleKey, DependencyProperty property, double expected) =>
        Application.Current.TryFindResource(styleKey) is Style style &&
        style.Setters.OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == property)?.Value is double value &&
            Math.Abs(value - expected) <= 0.5;

    private bool FilterTagChipGapsMeetCompactSpacing()
    {
        var options = (Panel)window.FindName("FilterTagOptions")!;
        return options.Children
            .OfType<FrameworkElement>()
            .All(child => child.Margin.Right >= 8 && child.Margin.Bottom >= 8);
    }

    private bool RequiredToggleButtonMargin(string name) =>
        window.FindName(name) is FrameworkElement element && element.Margin.Left >= 8;

    private List<string>? AddCountdownReadabilityFixtureRecords(DateTimeOffset nowUtc)
    {
        var temporaryIds = new List<string>();
        foreach (var (name, targetUtc) in new[]
                 {
                     ("验证倒计时可读-主项", nowUtc.AddDays(1)),
                     ("验证倒计时可读-次项", nowUtc.AddDays(2)),
                     ("验证倒计时可读-到期主项", nowUtc.AddDays(-1)),
                     ("验证倒计时可读-到期次项", nowUtc.AddDays(-2))
                 })
        {
            var result = Workspace.Apply(new SaveCountdownChange(
                null,
                name,
                targetUtc,
                Array.Empty<string>()));
            if (!result.Succeeded)
            {
                foreach (var temporaryId in temporaryIds)
                {
                    Workspace.Apply(new DeleteCountdownChange(temporaryId));
                }

                return null;
            }

            temporaryIds.Add(Workspace.Snapshot.Settings.Countdowns
                .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal)).Id);
        }

        return temporaryIds;
    }

    private MainCountdownReadabilityScenarios ProbeCountdownReadabilityScenarios()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalFilter = Workspace.Snapshot.Filter;
        var temporaryIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var nowUtc = Workspace.Snapshot.UtcNow;
            // 主项/次项各一条进行中条目供徽章与分格几何检查；再各一条过去截止条目供到期对比检查。
            // 会话层允许保存 0 规则的过去截止倒计时（编辑器 UI 才限制未来），全程使用真实时钟。
            var addedRecordIds = AddCountdownReadabilityFixtureRecords(nowUtc);
            if (addedRecordIds is null)
            {
                return MainCountdownReadabilityScenarios.Failed;
            }

            temporaryIds.AddRange(addedRecordIds);

            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();

            // 「超期待！」只挂距离下班；管理窗口没有距离下班条目，任何列表行都不得携带徽章。
            // 列表必须确有行渲染，排除空列表下的空洞真。
            var rowsRendered = window.CountdownRows.Children.Count > 0;
            var encourageBadgeAbsent = rowsRendered && !ProbeVisualDescendants<TextBlock>(window.CountdownRows)
                .Any(text => string.Equals(text.Text, "超期待！", StringComparison.Ordinal));

            // 主项/次项分格与悬浮窗同一套竖长口径（约 2:3、单位在块外、单位字不小于辅助下限档）。
            var blocks = CountdownBlocksFollowWarmPaperContract();

            // 「已到期」筛选下：首行到期主项回落主项数字色，其余到期次项回落正文色，
            // 「已到期」文案保留，卡底为到期弱化底。
            Workspace.ApplyFilter(originalFilter with { CountdownStatus = CountdownStatusFilter.Expired });
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var expiredRows = window.CountdownRows.Children.OfType<Border>().ToArray();
            var expiredBrush = window.FindResource("ExpiredBrush");
            var numberBrush = window.FindResource("PrimaryCountdownNumberBrush");
            var bodyBrush = window.FindResource("BodyBrush");
            var expiredPairs = expiredRows
                .Select((card, index) => (card, isPrimary: index == 0))
                .SelectMany(pair => CountdownBlocksFallbackTexts(pair.card)
                    .Select(text => (text, pair.isPrimary)))
                .ToArray();
            var expiredCardBackgroundsMatch = expiredRows.Length >= 2 &&
                expiredRows.All(card => ReferenceEquals(card.Background, expiredBrush));
            var expiredFallbackTextsPresent = expiredRows.Length >= 2 &&
                expiredRows.All(card => CountdownBlocksFallbackTexts(card).Any());
            var expiredPrimaryUsesNumberColor = expiredPairs
                .Where(pair => pair.isPrimary)
                .All(pair =>
                    string.Equals(pair.text.Text, "已到期", StringComparison.Ordinal) &&
                    ReferenceEquals(pair.text.Foreground, numberBrush));
            var expiredSecondaryUsesBodyColor = expiredPairs
                .Where(pair => !pair.isPrimary)
                .All(pair =>
                    string.Equals(pair.text.Text, "已到期", StringComparison.Ordinal) &&
                    ReferenceEquals(pair.text.Foreground, bodyBrush));
            Workspace.ApplyFilter(originalFilter);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();

            // 北京时间时钟使用表格数字；月薪输入与「今日已赚」同一套本地数字格式。
            var numericTextsMatchContract =
                Typography.GetNumeralAlignment(window.NowText) == FontNumeralAlignment.Tabular &&
                decimal.TryParse(
                    window.MonthlySalary.Text,
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out var salaryValue) &&
                string.Equals(
                    window.MonthlySalary.Text,
                    salaryValue.ToString("N2", CultureInfo.CurrentCulture),
                    StringComparison.Ordinal);

            return new MainCountdownReadabilityScenarios(
                encourageBadgeAbsent,
                expiredCardBackgroundsMatch,
                expiredFallbackTextsPresent,
                expiredPrimaryUsesNumberColor,
                expiredSecondaryUsesBodyColor,
                blocks.PairCountValid,
                blocks.VerticalRatioMatches,
                blocks.GapMatches,
                blocks.UnitOutsideAndColored,
                blocks.ValueBrushAndFamily,
                numericTextsMatchContract);
        }
        finally
        {
            Workspace.ApplyFilter(originalFilter);
            foreach (var temporaryId in temporaryIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Workspace.Apply(new DeleteCountdownChange(temporaryId));
            }

            window.UpdateTime(ChinaStandardTime.ToBeijingTime(Workspace.Snapshot.UtcNow));
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    internal bool RunCountdownBlocksGeometryFixture()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var temporaryIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var addedRecordIds = AddCountdownReadabilityFixtureRecords(Workspace.Snapshot.UtcNow);
            if (addedRecordIds is null)
            {
                return false;
            }

            temporaryIds.AddRange(addedRecordIds);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var blocks = CountdownBlocksFollowWarmPaperContract();
            return blocks.PairCountValid &&
                blocks.VerticalRatioMatches &&
                blocks.GapMatches &&
                blocks.UnitOutsideAndColored &&
                blocks.ValueBrushAndFamily;
        }
        finally
        {
            foreach (var temporaryId in temporaryIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Workspace.Apply(new DeleteCountdownChange(temporaryId));
            }

            window.UpdateTime(ChinaStandardTime.ToBeijingTime(Workspace.Snapshot.UtcNow));
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private MainCountdownBlocksProbeResult CountdownBlocksFollowWarmPaperContract()
    {
        var primaryBlockBrush = window.FindResource("PinkBlockBrush");
        var secondaryBlockBrush = window.FindResource("SecondaryBlockBrush");
        var numberBrush = window.FindResource("PrimaryCountdownNumberBrush");
        var inkBrush = window.FindResource("InkBrush");
        var mutedBrush = window.FindResource("MutedBrush");
        var cards = window.CountdownRows.Children.OfType<Border>().ToArray();
        var result = new MainCountdownBlocksProbeResult();
        if (cards.Length == 0)
        {
            return result;
        }

        result.PairCountValid = true;
        foreach (var card in cards)
        {
            foreach (var surface in ProbeVisualDescendants<Grid>(card)
                         .Where(grid => Math.Abs(grid.Width - CountdownRemainingColumnWidth) <= 0.01))
            {
                var pairPanels = ProbeVisualDescendants<StackPanel>(surface)
                    .Where(panel => panel.Children.Count == 2 &&
                        panel.Children[0] is Border &&
                        panel.Children[1] is TextBlock)
                    .ToArray();
                if (pairPanels.Length == 0)
                {
                    continue;
                }

                // 显示精度决定对数（天=1 … 秒=4）；几何合同逐对成立。
                if (pairPanels.Length > 4)
                {
                    result.PairCountValid = false;
                    continue;
                }

                for (var index = 0; index < pairPanels.Length; index++)
                {
                    var block = (Border)pairPanels[index].Children[0];
                    var unit = (TextBlock)pairPanels[index].Children[1];
                    if (block.Child is not TextBlock value)
                    {
                        result.PairCountValid = false;
                        continue;
                    }

                    var isPrimary = ReferenceEquals(block.Background, primaryBlockBrush);
                    if (!isPrimary && !ReferenceEquals(block.Background, secondaryBlockBrush))
                    {
                        result.PairCountValid = false;
                        continue;
                    }

                    // 竖长 2:3：块高固定为基准块宽的 1.5 倍（主项 38x57、次项 34x51），
                    // 长数值只允许加宽色块，不允许压扁比例或缩单位字。
                    var expectedBaseWidth = isPrimary ? 38 : 34;
                    var expectedHeight = isPrimary ? 57 : 51;
                    var expectedRadius = expectedBaseWidth / 4.0;
                    if (Math.Abs(block.Height - expectedHeight) > 0.01 ||
                        block.MinWidth < expectedBaseWidth - 0.01 ||
                        Math.Abs(block.CornerRadius.TopLeft - expectedRadius) > 0.01)
                    {
                        result.VerticalRatioMatches = false;
                    }

                    // Four primary blocks keep their 38 DIP width and 20 DIP
                    // digits by taking the specified compact 4 DIP gap.
                    var expectedGap = isPrimary && pairPanels.Length == 4 ? 4 : 10;
                    var expectedMargin = index == 0
                        ? new Thickness(0)
                        : new Thickness(expectedGap, 0, 0, 0);
                    if (pairPanels[index].Margin != expectedMargin)
                    {
                        result.GapMatches = false;
                    }

                    if (!ReferenceEquals(unit.Foreground, mutedBrush) ||
                        unit.FontSize < CountdownRemainingUnitFontSize - 0.01 ||
                        unit.FontFamily.Source.Contains("Bahnschrift", StringComparison.Ordinal) ||
                        !ReferenceEquals(unit.Parent, pairPanels[index]) ||
                        ReferenceEquals(unit, block.Child))
                    {
                        result.UnitOutsideAndColored = false;
                    }

                    var expectedValueBrush = isPrimary ? numberBrush : inkBrush;
                    if (!ReferenceEquals(value.Foreground, expectedValueBrush) ||
                        Typography.GetNumeralAlignment(value) != FontNumeralAlignment.Tabular ||
                        !value.FontFamily.Source.Contains("Bahnschrift", StringComparison.Ordinal))
                    {
                        result.ValueBrushAndFamily = false;
                    }
                }
            }
        }

        return result;
    }

    private IEnumerable<TextBlock> CountdownBlocksFallbackTexts(Border card) =>
        ProbeVisualDescendants<Grid>(card)
            .Where(grid => Math.Abs(grid.Width - CountdownRemainingColumnWidth) <= 0.01)
            .SelectMany(surface => ProbeVisualDescendants<TextBlock>(surface))
            .Where(text => string.Equals(text.Text, "已到期", StringComparison.Ordinal));

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
        var originalPreferences = Workspace.Snapshot.Settings.Preferences;
        var originalWorkspace = window.CurrentWorkspace;
        var originalFilter = Workspace.Snapshot.Filter;
        var temporaryIds = new List<string>();
        string? sampleTagId = null;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            sampleTagId = Workspace.Apply(new CreateTagChange("倒计时字号验证", null)).CreatedTag?.Id;
            if (sampleTagId is null)
            {
                return Array.Empty<MainCountdownRemainingMeasurement>();
            }
            var sampleTags = new[] { sampleTagId };
            Workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = sampleTags });
            var snapshot = Workspace.Snapshot;
            var nowUtc = snapshot.UtcNow;
            var ordinaryName = "验证主窗口倒计时";
            var ordinaryResult = Workspace.Apply(new SaveCountdownChange(
                null,
                ordinaryName,
                nowUtc.AddDays(4).AddHours(13).AddMinutes(18).AddSeconds(59),
                sampleTags));
            if (ordinaryResult.Succeeded)
            {
                var added = Workspace.Snapshot.Settings.Countdowns
                    .FirstOrDefault(record => string.Equals(record.Name, ordinaryName, StringComparison.Ordinal));
                if (added is not null)
                {
                    temporaryIds.Add(added.Id);
                }
            }

            var longName = "验证主窗口超长天数倒计时";
            var longResult = Workspace.Apply(new SaveCountdownChange(
                null,
                longName,
                nowUtc.AddDays(2_000_000),
                sampleTags));
            if (longResult.Succeeded)
            {
                var added = Workspace.Snapshot.Settings.Countdowns
                    .FirstOrDefault(record => string.Equals(record.Name, longName, StringComparison.Ordinal));
                if (added is not null)
                {
                    temporaryIds.Add(added.Id);
                }
            }

            var samples = new List<MainCountdownRemainingMeasurement>();
            foreach (var precision in new[] { "Day", "Hour", "Minute", "Second" })
            {
                var saved = Workspace.Apply(new SavePreferencesChange(
                    precision,
                    originalPreferences.MonthlySalary,
                    originalPreferences.WorkStart,
                    originalPreferences.WorkEnd)).Succeeded;
                WindowValidationInput.Pump(window.Dispatcher);
                var sampleNowUtc = Workspace.Snapshot.UtcNow;
                window.UpdateTime(ChinaStandardTime.ToBeijingTime(sampleNowUtc));
                window.UpdateLayout();
                var current = Workspace.Snapshot;
                var record = current.Settings.Countdowns.FirstOrDefault(item =>
                    temporaryIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase) &&
                    string.Equals(item.Name, ordinaryName, StringComparison.Ordinal));
                if (record is null)
                {
                    continue;
                }
                if (!window.CountdownRemainingTextsForValidation.TryGetValue(record.Id, out var text))
                {
                    continue;
                }

                var expected = CountdownFormatting.FormatRemaining(record.TargetUtc, sampleNowUtc, precision);
                samples.Add(CaptureMainCountdownRemaining(precision, record, expected, text, saved));
            }

            var longRecord = Workspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
                string.Equals(item.Name, longName, StringComparison.Ordinal));
            if (longRecord is not null)
            {
                var longNowUtc = Workspace.Snapshot.UtcNow;
                window.UpdateTime(ChinaStandardTime.ToBeijingTime(longNowUtc));
                window.UpdateLayout();
                if (window.CountdownRemainingTextsForValidation.TryGetValue(longRecord.Id, out var longText))
                {
                    var expected = CountdownFormatting.FormatRemaining(longRecord.TargetUtc, longNowUtc, "Second");
                    samples.Add(CaptureMainCountdownRemaining("SecondLongDays", longRecord, expected, longText, longResult.Succeeded));
                }
            }

            var ordinaryRecord = Workspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
                temporaryIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase) && item.Name == ordinaryName);
            if (ordinaryRecord is not null)
            {
                var sampleNowUtc = Workspace.Snapshot.UtcNow;
                var targetUtc = sampleNowUtc.AddDays(999).AddHours(13).AddMinutes(18).AddSeconds(59);
                var saved = Workspace.Apply(new SaveCountdownChange(
                    ordinaryRecord.Id, ordinaryName, targetUtc, sampleTags)).Succeeded;
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateTime(ChinaStandardTime.ToBeijingTime(sampleNowUtc));
                window.UpdateLayout();
                if (window.CountdownRemainingTextsForValidation.TryGetValue(ordinaryRecord.Id, out var text))
                {
                    samples.Add(CaptureMainCountdownRemaining("Second999Days", ordinaryRecord,
                        "999 天 13 小时 18 分钟 59 秒", text, saved));
                }
            }

            return samples;
        }
        finally
        {
            foreach (var id in temporaryIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Workspace.Apply(new DeleteCountdownChange(id));
            }
            if (sampleTagId is not null)
            {
                Workspace.Apply(new DeleteTagChange(sampleTagId));
            }
            Workspace.Apply(new SavePreferencesChange(
                originalPreferences.DisplayPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd));
            Workspace.ApplyFilter(originalFilter);
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
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
        var host = text.Tag as MainCountdownBlocksHost;
        var pairs = host is null ? Array.Empty<StackPanel>() : ProbeVisualDescendants<StackPanel>(host.Surface)
            .Where(pair => pair.Children.Count == 2 && pair.Children[0] is Border { Child: TextBlock } &&
                pair.Children[1] is TextBlock).ToArray();
        var title = text.Parent is Grid layout ? ProbeVisualDescendants<TextBlock>(layout)
            .SingleOrDefault(candidate => candidate.Uid == "CountdownRowTitleText") : null;
        var values = pairs.Select(pair => (TextBlock)((Border)pair.Children[0]).Child).ToArray();
        var renderedBlocks = string.Join(" ", pairs.SelectMany(pair => new[]
            { ((TextBlock)((Border)pair.Children[0]).Child).Text, ((TextBlock)pair.Children[1]).Text }));
        var blocksFit = host is not null && pairs.Length > 0 && pairs.All(pair =>
        {
            var bounds = pair.TransformToAncestor(host.Surface).TransformBounds(new Rect(pair.RenderSize));
            return bounds.Left >= -0.5 && bounds.Right <= host.Surface.ActualWidth + 0.5;
        }) && values.All(value =>
        {
            var bounds = value.TransformToAncestor(host.Surface).TransformBounds(new Rect(value.RenderSize));
            var glyphs = new FormattedText(value.Text, CultureInfo.CurrentCulture, value.FlowDirection,
                new Typeface(value.FontFamily, value.FontStyle, value.FontWeight, value.FontStretch),
                value.FontSize, Brushes.Black, dpi.PixelsPerDip);
            return value.IsVisible && value.TextWrapping == TextWrapping.NoWrap && value.TextTrimming == TextTrimming.None &&
                glyphs.WidthIncludingTrailingWhitespace + Math.Max(0, glyphs.OverhangTrailing) <= value.ActualWidth + 0.5 &&
                glyphs.Height <= value.ActualHeight + 0.5 && bounds.Left >= -0.5 && bounds.Right <= host.Surface.ActualWidth + 0.5;
        });
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
            preferenceSaved,
            host?.IsPrimary == true,
            values.FirstOrDefault()?.FontSize ?? 0,
            title?.FontSize ?? 0,
            renderedBlocks,
            blocksFit);
    }

    private Rect ValidationBounds(FrameworkElement element) =>
        element.TransformToAncestor(window).TransformBounds(
            new Rect(new Point(0, 0), element.RenderSize));

    // #240：按现有排版与适配算法为同一文本新建一组分格，供就地更新后的几何对照。
    internal Grid BuildCountdownBlocksForValidation(string remaining, bool isPrimary)
    {
        var host = new MainCountdownBlocksHost
        {
            Surface = new Grid
            {
                Width = CountdownRemainingColumnWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            },
            IsPrimary = isPrimary
        };
        MainCountdownBlockRenderer.Render(window, host, remaining);
        return host.Surface;
    }

    // 焦点态证据要让时分秒下拉框与提醒单选也进 Tab 路径。
    internal void ExpandCountdownEditorForValidation() => window.SetCountdownEditorExpanded(true);

    // #253：箭头行为探针沿真实展开按钮接线取入口，不新增按名查找。
    internal Button CountdownEditorDisclosureForValidation => window.CountdownEditorExpandButton;

    // #247：倒计时行实拍要把列表滚到底部；按字段取滚动区，不经按名查找。
    internal ScrollViewer CountdownListRegionForValidation => window.CountdownListRegion;

    // #249：便签卡几何探针与实拍要动列表、搜索、分段与批量操作栏；同样按字段取。
    internal NoteListValidationParts NoteListPartsForValidation => new(
        window.NotesListRegion,
        window.ActiveNoteRows,
        window.CompletedNoteRows,
        window.NoteSearch,
        window.CompletedNotesSegment,
        window.NoteBatchActions,
        window.SelectedNotesCount);

    // #250：共享表面圆角探针按 DESIGN.md §13.3 的角色取样：区块表面与弹层（Section）、条目卡（Card）、
    // 按钮、输入与分段（Control）、芯片与复选框（Chip）。按字段与模板部件取，不经按名查找；
    // 派生圆角（胶囊、数字色块、指示条、强调轨、徽章与装饰）不取样。
    internal IReadOnlyList<SurfaceRadiusSample> SharedSurfaceRadiiForValidation()
    {
        var samples = new List<SurfaceRadiusSample>();
        void Surface(string role, string element, object? candidate) =>
            samples.Add(new SurfaceRadiusSample(role, element, (candidate as Border)?.CornerRadius));
        void Part(string role, string element, Control control, string part)
        {
            control.ApplyTemplate();
            Surface(role, $"{element}.{part}", control.Template?.FindName(part, control));
        }

        Surface("Section", nameof(window.CountdownWorkspaceBackdrop), window.CountdownWorkspaceBackdrop);
        Surface("Section", nameof(window.CountdownEditorSurface), window.CountdownEditorSurface);
        Surface("Section", nameof(window.NoteEditorPanel), window.NoteEditorPanel);
        Surface("Section", nameof(window.NoteEditorSurface), window.NoteEditorSurface);
        Surface("Section", nameof(window.NotesListSurface), window.NotesListSurface);
        Surface("Section", nameof(window.MainStatusBarSurface), window.MainStatusBarSurface);
        Surface("Section", nameof(window.CountdownPreferencesSurface), window.CountdownPreferencesSurface);
        Surface("Section", "FilterPopup.Child", window.FilterPopup.Child);
        Surface("Section", "TagManagerPopup.Child", window.TagManagerPopup.Child);
        Surface("Section", "CountdownTags.Popup.Child", window.CountdownTags.Popup.Child);
        Surface("Section", "NoteTags.Popup.Child", window.NoteTags.Popup.Child);

        foreach (var card in window.CountdownRows.Children.OfType<Border>())
        {
            Surface("Card", "CountdownRows.Card", card);
        }

        foreach (var card in MainValidationDescendants<NoteSelectionCard>(window.ActiveNoteRows))
        {
            Surface("Card", "ActiveNoteRows.Card", card);
        }

        var tagIds = Workspace.Snapshot.Settings.Tags
            .Select(tag => tag.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var chip in MainValidationDescendants<Border>(window.CountdownRows)
                     .Concat(MainValidationDescendants<Border>(window.ActiveNoteRows))
                     .Where(border => tagIds.Contains(border.Uid)))
        {
            Surface("Chip", "TagChip", chip);
        }

        Part("Control", nameof(window.SaveCountdownButton), window.SaveCountdownButton, "FocusRing");
        Part("Control", nameof(window.SaveCountdownButton), window.SaveCountdownButton, "Surface");
        Part("Control", nameof(window.SaveNoteButton), window.SaveNoteButton, "Surface");
        Part("Control", nameof(window.SavePreferencesButton), window.SavePreferencesButton, "FocusRing");
        Part("Control", nameof(window.SavePreferencesButton), window.SavePreferencesButton, "Surface");
        Part("Control", nameof(window.FilterButton), window.FilterButton, "Surface");
        Part("Control", nameof(window.CountdownName), window.CountdownName, "Surface");
        Part("Control", nameof(window.CountdownReminderNoRadio), window.CountdownReminderNoRadio, "FocusRing");
        Surface("Control", "MainWorkspaceTabHost.Parent", window.MainWorkspaceTabHost.Parent);
        Surface("Control", "NotesSegmentHost.Parent", window.NotesSegmentHost.Parent);
        var disclosureStyle = window.MainContentProjection.TryFindResource("MainNoteDateDisclosureButton") as Style;
        foreach (var disclosure in MainValidationDescendants<Button>(window.ActiveNoteRows)
                     .Where(button => disclosureStyle is not null && ReferenceEquals(button.Style, disclosureStyle)))
        {
            Part("Control", "NoteDateDisclosure", disclosure, "FocusRing");
            Part("Control", "NoteDateDisclosure", disclosure, "Surface");
        }

        Part("Chip", nameof(window.TopmostCheck), window.TopmostCheck, "Box");
        var completionStyle = window.MainContentProjection.TryFindResource("MainNoteCompletionCheck") as Style;
        foreach (var completion in MainValidationDescendants<CheckBox>(window.ActiveNoteRows)
                     .Where(check => completionStyle is not null && ReferenceEquals(check.Style, completionStyle)))
        {
            Part("Chip", "NoteCompletionCheck", completion, "Box");
        }

        return samples;
    }

    // #250：共享表面实拍要走到真实的字段错误与设置错误，并打开各个弹层；按字段取，不经按名查找。
    internal MainSurfaceEvidenceParts SurfaceEvidencePartsForValidation => new(
        window.SaveCountdownButton,
        window.PreferencesPopup,
        window.FilterPopup,
        window.TagManagerPopup,
        window.CountdownTags);

    internal void ShowCountdownFieldErrorForValidation()
    {
        window.SetCountdownEditorExpanded(true);
        window.CountdownName.Text = string.Empty;
        window.SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
    }

    // 与偏好字段探针同一入口：激活窗口后由设置按钮真实打开弹层，否则保存失败把焦点交回月薪框时
    // 非活动窗口上的 StaysOpen=False 弹层会丢失捕获并自行关闭。
    internal void ShowPreferencesSalaryErrorForValidation()
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

    private NoteExerciseResult RunNoteAddExercise()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        var initial = Workspace.Snapshot;
        var countBefore = initial.Settings.Notes.Count;
        var noteDate = initial.BeijingNow.AddDays(2).Date;
        var firstDueDay = initial.BeijingNow.AddDays(3);
        var firstDue = new DateTimeOffset(
            firstDueDay.Year,
            firstDueDay.Month,
            firstDueDay.Day,
            9,
            30,
            0,
            firstDueDay.Offset);
        window.NoteText.Text = "验证点击添加便签";
        WindowScenarioAccess.SetMainNoteReminderRules(window, Array.Empty<ReminderRule>());

        window.NoteDate.SelectedDate = null;

        window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dateRequired =
            Workspace.Snapshot.Settings.Notes.Count == countBefore &&
            string.Equals(window.NoteText.Text, "验证点击添加便签", StringComparison.Ordinal) &&
            window.NoteMessage.Text.Contains("日期", StringComparison.Ordinal);

        window.NoteDate.SelectedDate = noteDate;
        WindowValidationInput.Pump(window.Dispatcher);
        var cleanEditorHeight = window.NoteEditorSurface.ActualHeight;
        var cleanDateTop = window.NoteDate.TranslatePoint(new Point(0, 0), window).Y;
        var cleanSaveTop = window.SaveNoteButton.TranslatePoint(new Point(0, 0), window).Y;
        window.NoteDate.SelectedDate = null;
        window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        window.ClearNoteFieldErrors();
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        var adapterCleanupRestoresLayout =
            window.NoteDateError.Visibility == Visibility.Collapsed &&
            string.IsNullOrEmpty(AutomationProperties.GetHelpText(window.NoteDateError)) &&
            string.IsNullOrEmpty(AutomationProperties.GetHelpText(window.NoteDate)) &&
            Math.Abs(window.NoteEditorSurface.ActualHeight - cleanEditorHeight) <= 0.01 &&
            Math.Abs(window.NoteDate.TranslatePoint(new Point(0, 0), window).Y - cleanDateTop) <= 0.01 &&
            Math.Abs(window.SaveNoteButton.TranslatePoint(new Point(0, 0), window).Y - cleanSaveTop) <= 0.01;

        window.NoteDate.SelectedDate = noteDate;
        WindowScenarioAccess.SetMainNoteReminderRules(window, new ReminderRule[]
        {
            new FixedIntervalReminderRule(
                Guid.NewGuid().ToString("N"),
                firstDue.ToUniversalTime(),
                new ReminderInterval(30))
        });
        window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var afterAdd = Workspace.Snapshot.Settings;
        var added = afterAdd.Notes.LastOrDefault(
            note => string.Equals(note.Text, "验证点击添加便签", StringComparison.Ordinal));
        var addViaClick = added is not null && afterAdd.Notes.Count == countBefore + 1 && adapterCleanupRestoresLayout;
        var dateViaPicker = added?.Date == noteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var reminderViaSelector =
            added?.ReminderIntervalMinutes == 30 &&
            added.ReminderStartUtc == firstDue.ToUniversalTime().Subtract(TimeSpan.FromMinutes(30));

        var completionViaCheckbox = false;
        if (added is not null)
        {
            var checkBox = WindowScenarioAccess.FindMainNoteCompletion(window, added.Id);
            checkBox.IsChecked = true;
            var completed = Workspace.Snapshot.Settings.Notes
                .First(note => note.Id == added.Id).IsCompleted;
            var refreshedCheckBox = WindowScenarioAccess.FindMainNoteCompletion(window, added.Id);
            refreshedCheckBox.IsChecked = false;

            var activeAgain = !Workspace.Snapshot.Settings.Notes
                .First(note => note.Id == added.Id).IsCompleted;
            completionViaCheckbox = completed && activeAgain;
        }

        return new NoteExerciseResult(
            dateRequired,
            addViaClick,
            dateViaPicker,
            reminderViaSelector,
            completionViaCheckbox);
    }

    private ReminderModeClickResult RunReminderModeClickExercises()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        var initial = Workspace.Snapshot;
        var noteDate = initial.BeijingNow.AddDays(2).Date;
        var onceDue = initial.BeijingNow.AddDays(3);
        onceDue = new DateTimeOffset(
            onceDue.Year,
            onceDue.Month,
            onceDue.Day,
            9,
            10,
            0,
            onceDue.Offset);
        var repeatDue = onceDue.AddDays(1).AddMinutes(20);

        window.NoteText.Text = "验证主窗不提醒";
        window.NoteDate.SelectedDate = noteDate;
        WindowScenarioAccess.SetMainNoteReminderRules(window, Array.Empty<ReminderRule>());
        window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var noReminderWorks = Workspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证主窗不提醒")?.Reminder is NoReminder;

        window.NoteText.Text = "验证主窗仅一次";
        window.NoteDate.SelectedDate = noteDate;
        WindowScenarioAccess.SetMainNoteReminderRules(window, new ReminderRule[]
        {
            new AbsoluteOnceReminderRule(
                Guid.NewGuid().ToString("N"),
                onceDue.ToUniversalTime())
        });
        window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var onceWorks = Workspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证主窗仅一次")?.Reminder is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            } once &&
            once.FirstDueUtc == onceDue.ToUniversalTime();

        window.NoteText.Text = "验证主窗重复提醒";
        window.NoteDate.SelectedDate = noteDate;
        WindowScenarioAccess.SetMainNoteReminderRules(window, new ReminderRule[]
        {
            new FixedIntervalReminderRule(
                Guid.NewGuid().ToString("N"),
                repeatDue.ToUniversalTime(),
                new ReminderInterval(30))
        });
        window.SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var repeatWorks = Workspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证主窗重复提醒")?.Reminder is RepeatingReminder repeating &&
            repeating.FirstDueUtc == repeatDue.ToUniversalTime() &&
            repeating.Interval.Minutes == 30;

        return new ReminderModeClickResult(noReminderWorks, onceWorks, repeatWorks);
    }

    private (bool TracksBeijingDay, bool PreservesChosenDate) ProbeNoteDateBehavior()
    {
        var originalEditingId = window.CaptureInstanceActivationState().EditingNoteId;
        var originalDate = window.NoteDate.SelectedDate;
        var originalText = window.NoteText.Text;
        try
        {
            window.ResetNoteEditor();
            window.UpdateTime(new DateTimeOffset(2026, 7, 20, 23, 59, 59, TimeSpan.FromHours(8)));
            var firstDay = window.NoteDate.SelectedDate == new DateTime(2026, 7, 20);
            window.UpdateTime(new DateTimeOffset(2026, 7, 21, 0, 0, 1, TimeSpan.FromHours(8)));
            var nextDay = window.NoteDate.SelectedDate == new DateTime(2026, 7, 21);

            window.NoteText.Text = "跨日保留草稿";
            window.NoteDate.SelectedDate = new DateTime(2031, 4, 5);
            window.UpdateTime(new DateTimeOffset(2026, 7, 22, 0, 0, 1, TimeSpan.FromHours(8)));
            var preserved =
                window.NoteDate.SelectedDate == new DateTime(2031, 4, 5) &&
                window.NoteText.Text == "跨日保留草稿";
            return (firstDay && nextDay, preserved);
        }
        finally
        {
            WindowScenarioAccess.RestoreMainNoteEditor(
                window, Workspace, originalEditingId, originalText, originalDate);
        }
    }

    private bool ProbeWorkspaceDraftRetention()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var originalCountdownDraft = window.CountdownName.Text;
        var originalNoteDraft = window.NoteText.Text;
        try
        {
            window.CountdownName.Text = "未保存倒计时草稿";
            window.NoteText.Text = "未保存便签草稿";
            ShowWorkspace(WorkspaceKind.Countdowns);
            ShowWorkspace(WorkspaceKind.Notes);
            ShowWorkspace(WorkspaceKind.Countdowns);
            return
                window.CountdownName.Text == "未保存倒计时草稿" &&
                window.NoteText.Text == "未保存便签草稿";
        }
        finally
        {
            window.CountdownName.Text = originalCountdownDraft;
            window.NoteText.Text = originalNoteDraft;
            ShowWorkspace(originalWorkspace);
        }
    }

    private bool ProbeCrossDayReminderBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-cross-day-{Guid.NewGuid():N}.settings.json");
        try
        {
            var nowUtc = new DateTimeOffset(2031, 4, 5, 15, 59, 0, TimeSpan.Zero);
            var firstDueUtc = new DateTimeOffset(2031, 4, 6, 1, 30, 0, TimeSpan.Zero);

            var clock = new FixedClock(nowUtc);
            var store = new SettingsStore(clock: clock);
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            var savedProbe = workspace.Apply(new SaveNoteChange(
                null,
                "跨天提醒探针",
                new DateTime(2031, 4, 5),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateRepeating(firstDueUtc, 30)));
            var savedQuickProbe = workspace.Apply(new SaveNoteChange(
                null,
                "快速跨天提醒探针",
                new DateTime(2031, 4, 5),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateRepeating(firstDueUtc.AddDays(1), 60)));
            if (!savedProbe.Succeeded || !savedQuickProbe.Succeeded)
            {
                return false;
            }

            var saved = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "跨天提醒探针");
            var originalStart = saved.ReminderStartUtc;
            var liveEarly = workspace.GetDueReminders(firstDueUtc.AddSeconds(-1));
            var liveDue = workspace.GetDueReminders(firstDueUtc);
            var displayed = workspace.ConfirmReminderDisplayed(saved, firstDueUtc);
            var acknowledged = workspace.AcknowledgeReminder(
                saved,
                saved.ReminderRules.Single().Id,
                firstDueUtc);
            if (!displayed.Succeeded ||
                !acknowledged.Succeeded ||
                !workspace.Apply(new SaveNoteChange(
                    saved.Id,
                    "已编辑的跨天提醒探针",
                    new DateTime(2031, 4, 5),
                    false,
                    Array.Empty<string>(),
                    saved.Reminder)).Succeeded)
            {
                return false;
            }

            var reloaded = DesktopWorkspace.Load(settingsPath, store);
            var reloadedSettings = reloaded.Snapshot.Settings;
            var persistedNote = reloadedSettings.Notes.Single(note => note.Id == saved.Id);
            var quickNote = reloadedSettings.Notes.Single(note => note.Text == "快速跨天提醒探针");
            var early = reloaded.GetDueReminders(firstDueUtc.AddSeconds(-1));
            var due = reloaded.GetDueReminders(firstDueUtc);
            reloaded.ConfirmReminderDisplayed(persistedNote, firstDueUtc);
            var duplicate = reloaded.GetDueReminders(firstDueUtc);
            var nextCycle = reloaded.GetDueReminders(firstDueUtc.AddMinutes(30));
            var completionPreservedSchedule =
                reloaded.Apply(new SetNoteCompletionChange(persistedNote.Id, true)).Succeeded &&
                reloaded.Apply(new SetNoteCompletionChange(persistedNote.Id, false)).Succeeded &&
                reloaded.Snapshot.Settings.Notes
                    .Single(note => note.Id == persistedNote.Id).ReminderStartUtc == originalStart;
            return
                originalStart == new DateTimeOffset(2031, 4, 6, 1, 0, 0, TimeSpan.Zero) &&
                persistedNote.ReminderStartUtc == originalStart &&
                quickNote.ReminderStartUtc == new DateTimeOffset(2031, 4, 7, 0, 30, 0, TimeSpan.Zero) &&
                liveEarly.Count == 0 &&
                liveDue.Count == 1 &&
                early.Count == 0 &&
                due.Count == 0 &&
                duplicate.Count == 0 &&
                nextCycle.Count == 1 &&
                completionPreservedSchedule;
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
            File.Delete(settingsPath + ".backup1");
            File.Delete(settingsPath + ".backup2");
            File.Delete(settingsPath + ".backup2.tmp");
            File.Delete(settingsPath + ".backup2.rollback");
        }
    }

    private bool ProbeReminderSummaryAdvancesAfterFirstDue()
    {
        var originalWorkspace = window.CurrentWorkspace;
        var firstDueUtc = Workspace.Snapshot.UtcNow.AddDays(1);
        firstDueUtc = new DateTimeOffset(firstDueUtc.Year, firstDueUtc.Month,
            firstDueUtc.Day, 1, 30, 0, TimeSpan.Zero);
        string? noteId = null;
        try
        {
            var result = Workspace.Apply(new SaveNoteChange(
                null, "提醒摘要探针", null, false, Array.Empty<string>(),
                new ReminderRule[]
                {
                    new FixedIntervalReminderRule(Guid.NewGuid().ToString("N"), firstDueUtc, new ReminderInterval(30))
                }));
            if (!result.Succeeded)
            {
                return false;
            }
            noteId = Workspace.Snapshot.AllNotes.Single(note => note.Text == "提醒摘要探针").Id;
            ShowWorkspace(WorkspaceKind.Notes);
            window.RenderNotes(firstDueUtc.AddSeconds(-1));
            string Summary() => MainValidationDescendants<TextBlock>(WindowScenarioAccess.FindMainNoteRow(window, noteId))
                .Single(text => AutomationProperties.GetAutomationId(text) == MainWindow.NoteCardReminderSummaryAutomationId).Text;
            var before = Summary();
            window.UpdateTime(ChinaStandardTime.ToBeijingTime(firstDueUtc));
            var after = Summary();
            return
                before.Contains("09:30", StringComparison.Ordinal) &&
                after.Contains("10:00", StringComparison.Ordinal);
        }
        finally
        {
            if (noteId is not null)
            {
                Workspace.Apply(new DeleteNoteChange(noteId));
            }
            ShowWorkspace(originalWorkspace);
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
internal sealed partial class FloatingWindowValidationScenariosRunner
{
  private const double CalendarIndexNoteCardWidth = 319;
  private readonly FloatingWindow window;
  private readonly DesktopWorkspace? desktopWorkspace;
  private readonly IManagerWorkspaceHost? _manager;
  private readonly HolidayCalendar? calendar;
  private WorkspaceKind _workspace = WorkspaceKind.Countdowns;
  private DesktopWorkspace Workspace => desktopWorkspace ?? throw new InvalidOperationException("此验证场景需要显式 DesktopWorkspace。");
  private HolidayCalendar Calendar => calendar ?? throw new InvalidOperationException("此验证场景需要真实 HolidayCalendar。");
  private TextBlock ActiveBottomStatusElement => window.FloatingEncouragement.Visibility == Visibility.Visible
      ? window.FloatingEncouragement
      : window.FloatingIncome;

  internal FloatingWindowValidationScenariosRunner(FloatingWindow window, DesktopWorkspace? desktopWorkspace = null, IManagerWorkspaceHost? manager = null, HolidayCalendar? calendar = null)
  {
      this.window = window;
      this.desktopWorkspace = desktopWorkspace;
      _manager = manager;
      this.calendar = calendar;
      _workspace = window.FloatingNotesButton.IsChecked == true
          ? WorkspaceKind.Notes
          : WorkspaceKind.Countdowns;
  }

  private void ShowWorkspace(WorkspaceKind workspace)
  {
      _workspace = workspace;
      window.ShowWorkspace(workspace);
  }
    // #253：为完整验收里的底部锚定失败保留有界的真实几何诊断；只读，不推进布局或改变定位标志。
    internal void TraceAnchoringForValidation(string phase)
    {
        var area = MonitorWorkAreaResolver.ResolveForWindow(window).WorkAreaDip;
        var dpi = VisualTreeHelper.GetDpi(window);
        Console.Error.WriteLine(FormattableString.Invariant(
            $"FLOATING_ANCHOR_TRACE: phase={phase}; left={window.Left:0.###}; top={window.Top:0.###}; width={window.ActualWidth:0.###}; height={window.ActualHeight:0.###}; dpi={dpi.PixelsPerInchX:0.###}x{dpi.PixelsPerInchY:0.###}; work={area}; bottomMargin={area.Bottom - window.Top - window.ActualHeight:0.###}; visible={window.IsVisible}; state={window.WindowState}; restore={window.RestoreBounds}; workspace={_workspace}"));
    }

    internal IDisposable ObserveAnchoringForValidation() => new AnchoringObservation(this);

    private sealed class AnchoringObservation : IDisposable
    {
        private readonly FloatingWindow _window;
        private readonly FloatingWindowValidationScenariosRunner _owner;
        private int _reported;

        internal AnchoringObservation(FloatingWindowValidationScenariosRunner owner)
        {
            _owner = owner;
            _window = owner.window;
            _window.LocationChanged += OnLocationChanged;
        }

        private void OnLocationChanged(object? sender, EventArgs args)
        {
            if (_reported++ < 32)
            {
                _owner.TraceAnchoringForValidation("location-changed");
            }
        }

        public void Dispose() => _window.LocationChanged -= OnLocationChanged;
    }

    // #250：悬浮窗共享表面圆角取样——倒计时行底色与便签卡（Card）、「…」菜单弹层（Section）。
    // 悬浮外壳、快速新增、滚动条、强调轨、徽章与数字色块按各自章节派生，不在取样范围。
    internal IReadOnlyList<SurfaceRadiusSample> SharedSurfaceRadiiForValidation()
    {
        var originalWorkspace = _workspace;
        var samples = new List<SurfaceRadiusSample>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            foreach (var root in window.FloatingRows.Children.OfType<Border>().Select(row => row.Child).OfType<Grid>())
            {
                if (root.Children.OfType<Border>().FirstOrDefault() is { } tint)
                {
                    samples.Add(new SurfaceRadiusSample("Card", "FloatingCountdownRowTint", tint.CornerRadius));
                }
            }

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);
            foreach (var card in window.FloatingRows.Children.OfType<Border>())
            {
                samples.Add(new SurfaceRadiusSample("Card", "FloatingNoteCard", card.CornerRadius));
            }

            if (NoteOverflowTriggersForValidation().FirstOrDefault() is { } trigger)
            {
                try
                {
                    trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    WindowValidationInput.Pump(window.Dispatcher);
                    if (window.FindActiveNoteOverflowMenu() is { IsOpen: true } menu)
                    {
                        menu.ApplyTemplate();
                        menu.UpdateLayout();
                        var menuBorder = ValidationDescendants<Border>(menu).FirstOrDefault();
                        samples.Add(new SurfaceRadiusSample(
                            "Section",
                            "FloatingNoteOverflowMenu",
                            menuBorder?.CornerRadius));
                    }
                }
                finally
                {
                    window.CloseNoteOverflowMenu(immediate: true);
                    WindowValidationInput.Pump(window.Dispatcher);
                }
            }
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }

        return samples;
    }

    private IEnumerable<Button> NoteOverflowTriggersForValidation() =>
        ValidationDescendants<Button>(window.FloatingRows)
            .Where(button => Equals(button.GetValue(FloatingWindow.NoteControlRoleProperty), FloatingWindow.NoteControlRoleOverflow));

    // #250：共享表面实拍的悬浮窗状态——真实快速新增字段错误、首条便签「…」菜单与快速新增入口焦点。
    internal Button QuickAddToggleForValidation => window.FloatingAddNoteToggle;

    internal void ShowQuickAddTextErrorForValidation()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        window.SetQuickAddExpanded(true);
        window.UpdateLayout();
        WindowValidationInput.Pump(window.Dispatcher);
        window.FloatingNoteText.Text = string.Empty;
        window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal void CollapseQuickAddForValidation()
    {
        window.ClearNoteFieldErrors();
        window.SetNoteMessage(string.Empty);
        window.SetQuickAddExpanded(false);
        WindowValidationInput.Pump(window.Dispatcher);
    }

    internal ContextMenu? ActiveNoteOverflowMenuForValidation => window.FindActiveNoteOverflowMenu();

    internal ContextMenu? OpenFirstNoteOverflowMenuForValidation()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        WindowValidationInput.Pump(window.Dispatcher);
        if (NoteOverflowTriggersForValidation().FirstOrDefault() is not { } trigger)
        {
            return null;
        }

        trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        return window.FindActiveNoteOverflowMenu();
    }

    internal FloatingThemeExerciseResult RunThemeMigrationFixture() => RunDayThemeExercise();

    internal bool RunFloatingCrossDayReminderFixture() => RunCrossDayReminderAddExercise();

    internal ReminderModeClickResult RunFloatingReminderModeFixture() => RunReminderModeClickExercises();

    internal FloatingWindowValidationScenarios RunValidationScenarios(bool exerciseNoteAdd)
    {
        var originalWorkspace = _workspace;
        var initialStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Countdowns,
            Workspace.Snapshot);
        try
        {
            var crossDayReminderAdded =
                exerciseNoteAdd && RunCrossDayReminderAddExercise();
            var reminderModes = exerciseNoteAdd
                ? RunReminderModeClickExercises()
                : ReminderModeClickResult.NotRun;
            var dragInteraction = RunDragInteractionExercise();
            TraceAnchoringForValidation("after-drag-probe");
            var themeExercise = RunDayThemeExercise();
            TraceAnchoringForValidation("after-theme-probe");
            var manualPlacement = RunManualPlacementExercise();
            TraceAnchoringForValidation("after-manual-placement-probe");
            var uiPolish = ProbeUiPolishScenarios();
            TraceAnchoringForValidation("after-polish-probe");
            var workspace = ProbeWorkspaceScenarios(initialStateMatches);
            TraceAnchoringForValidation("after-workspace-probe");
            var noteSearch = ProbeNoteSearchScenarios();
            TraceAnchoringForValidation("after-note-search-probe");
            // #212：渲染签名稳定刷新探针（焦点/实例保留、隐藏与最小化门控）。
            var stableRefresh = ProbeStableRefreshScenarios();
            TraceAnchoringForValidation("after-stable-refresh-probe");
            var keyboardFocus = ProbeKeyboardFocusScenarios();
            TraceAnchoringForValidation("after-keyboard-focus-probe");
            // 放在最后：设置按钮与“…”编辑会揭示管理窗口，避免扰动前面的聚焦类探针。
            var bottomCommand = ProbeBottomCommandScenarios();
            TraceAnchoringForValidation("after-bottom-command-probe");
            var structure = ProbeStructureScenarios();

            return new FloatingWindowValidationScenarios(
                crossDayReminderAdded,
                reminderModes,
                dragInteraction,
                themeExercise,
                manualPlacement,
                uiPolish,
                workspace,
                noteSearch,
                stableRefresh,
                bottomCommand,
                structure,
                keyboardFocus);
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            if (_manager is Window managerWindow)
            {
                managerWindow.Activate();
                WindowValidationInput.Pump(window.Dispatcher);
            }
        }
    }

    // 静态结构事实：控件名、类型与已退役控件的缺席都在窗口内判定，报告只读结论。
    private FloatingStructureScenarios ProbeStructureScenarios() =>
        new(
            window.TaskCard.Effect is null && window.FloatingThemeBackdrop.Effect is null,
            Typography.GetNumeralAlignment(window.FloatingIncome) == FontNumeralAlignment.Tabular,
            window.FindName("FloatingManageReminderRules") is Button &&
                window.FindName("FloatingReminderRulesSummary") is TextBlock,
            window.FloatingManageReminderRules.Visibility == Visibility.Visible &&
                window.FloatingReminderRulesSummary.Visibility == Visibility.Visible,
            window.FloatingManageReminderRules.Width <= 116,
            window.FindName("FloatingReminder") is null &&
                window.FindName("FloatingReminderSchedule") is null &&
                window.FindName("FloatingReminderIntervalPanel") is null,
            window.FindName("OpenManagerButton") is Button,
            window.FindName("FloatingNotesButton") is ToggleButton,
            window.FindName("FloatingNoteText") is TextBox &&
                window.FindName("FloatingAddNote") is Button,
            window.FindName("FloatingNoteDate") is DatePicker);

    private FloatingWorkspaceValidationScenarios ProbeWorkspaceScenarios(
        bool initialStateMatches)
    {
        var snapshot = Workspace.Snapshot;

        window.FloatingNotesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
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
        var countdownButtonTextFit = MeasureValidationButtonText(window.FloatingCountdownsButton);
        var notesButtonTextFit = MeasureValidationButtonText(window.FloatingNotesButton);
        var titleTextFit = MeasureValidationTitleText(window.FloatingTitle, window.FloatingDragHandle);
        var titleTextHeightFit = MeasureValidationTitleTextHeight(window.FloatingTitle);

        window.FloatingNotesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        var notesReselectionStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Notes,
            snapshot);

        window.FloatingCountdownsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        var countdownStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Countdowns,
            snapshot);

        window.FloatingCountdownsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        var countdownReselectionStateMatches = FloatingWorkspacePresentationMatches(
            WorkspaceKind.Countdowns,
            snapshot);
        var countdownSelectionSurvivesRefresh =
            InitialCountdownSelectionSurvivesRefresh();

        var incomePreferences = Workspace.Snapshot.Settings.Preferences;
        var originalIncomeText = window.IncomeText;
        var originalIncomeVisibility = ActiveBottomStatusElement.Visibility;
        var incomeHiddenWithoutSalary = false;
        var incomeRestoredWithSalary = false;
        try
        {
            var zeroSalaryApplied = Workspace.Apply(new SavePreferencesChange(
                incomePreferences.DisplayPrecision,
                0m,
                incomePreferences.WorkStart,
                incomePreferences.WorkEnd)).Succeeded;
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            // 月薪未输入：不渲染金额；渲染结果必须等于底部状态解析对当前日历状态的决定——
            // 就绪路径是当前工作区的手写体鼓励文案，未就绪路径是 #218 的统一说明文案，
            // 加载中路径保持日历状态文本。
            var expectedEncouragement = _workspace == WorkspaceKind.Notes
                ? FloatingBottomStatus.NotesEncouragementText
                : FloatingBottomStatus.CountdownEncouragementText;
            var expectedBottomStatusText = FloatingBottomStatus.Resolve(
                null,
                0m,
                Calendar.State,
                Calendar.StatusText,
                expectedEncouragement).Text;
            incomeHiddenWithoutSalary =
                zeroSalaryApplied &&
                !(window.IncomeText ?? string.Empty).StartsWith("今日已赚", StringComparison.Ordinal) &&
                ActiveBottomStatusElement.Visibility == Visibility.Visible &&
                string.Equals(window.IncomeText, expectedBottomStatusText, StringComparison.Ordinal);
        }
        finally
        {
            var currentPreferences = Workspace.Snapshot.Settings.Preferences;
            if (currentPreferences.MonthlySalary != incomePreferences.MonthlySalary)
            {
                Workspace.Apply(new SavePreferencesChange(
                    incomePreferences.DisplayPrecision,
                    incomePreferences.MonthlySalary,
                    incomePreferences.WorkStart,
                    incomePreferences.WorkEnd));
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
            }

            incomeRestoredWithSalary =
                Workspace.Snapshot.Settings.Preferences.MonthlySalary ==
                    incomePreferences.MonthlySalary &&
                ActiveBottomStatusElement.Visibility == originalIncomeVisibility &&
                string.Equals(window.IncomeText, originalIncomeText, StringComparison.Ordinal);
        }

        return new FloatingWorkspaceValidationScenarios(
            initialStateMatches,
            notesStateMatches &&
            notesReselectionStateMatches &&
            countdownStateMatches &&
            countdownReselectionStateMatches,
            countdownSelectionSurvivesRefresh,
            notesKeepExistingSortOrder,
            countdownButtonTextFit,
            notesButtonTextFit,
            titleTextFit,
            titleTextHeightFit,
            window.FloatingCountdownCount,
            window.FloatingCountdownTexts,
            window.IncomeText,
            renderedNoteIds,
            incomeHiddenWithoutSalary,
            incomeRestoredWithSalary);
    }

    private bool FloatingWorkspacePresentationMatches(
        WorkspaceKind workspace,
        DesktopWorkspaceSnapshot snapshot)
    {
        var selectedButtonCount = new[]
        {
            window.FloatingCountdownsButton.IsChecked == true,
            window.FloatingNotesButton.IsChecked == true
        }.Count(selected => selected);
        var chromeMatches =
            selectedButtonCount == 1 &&
            window.FloatingCountdownsButton.IsChecked == (workspace == WorkspaceKind.Countdowns) &&
            window.FloatingNotesButton.IsChecked == (workspace == WorkspaceKind.Notes) &&
            string.Equals(
                window.FloatingTitle.Text,
                workspace == WorkspaceKind.Countdowns ? "倒计时" : "待办便签",
                StringComparison.Ordinal) &&
            window.FloatingNotesCommandArea.Visibility == (workspace == WorkspaceKind.Notes
                ? Visibility.Visible
                : Visibility.Collapsed) &&
            window.FloatingNoteEntry.Visibility == Visibility.Collapsed &&
            window.FloatingRows.Children.Count > 0;
        if (!chromeMatches)
        {
            return false;
        }

        if (workspace == WorkspaceKind.Countdowns)
        {
            var expectedCountdownCount = snapshot.VisibleCountdowns.Count(record =>
                record.TargetUtc > snapshot.UtcNow);
            var emptyMessages = window.FloatingRows.Children
                .OfType<TextBlock>()
                .Select(text => text.Text)
                .ToArray();
            return
                window.FloatingCountdownCount == expectedCountdownCount &&
                !ValidationDescendants<CheckBox>(window.FloatingRows).Any() &&
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
        var emptyNoteMessages = window.FloatingRows.Children
            .OfType<TextBlock>()
            .Select(text => text.Text)
            .ToArray();
        return
            noteIdsMatch &&
            (expectedNoteIds.Length > 0
                ? emptyNoteMessages.Length == 0
                : emptyNoteMessages.Length == 1 && string.Equals(
                    emptyNoteMessages[0],
                    "还没有待办便签。",
                    StringComparison.Ordinal));
    }

    private FloatingNoteSearchValidationScenarios ProbeNoteSearchScenarios()
    {
        var originalWorkspace = _workspace;
        var originalFilter = Workspace.Snapshot.Filter;
        var originalExpanded = window.FloatingNoteSearch.IsVisible;
        var originalNoteText = window.FloatingNoteText.Text;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var hiddenInCountdowns =
                window.FloatingSearchNotes.Visibility == Visibility.Collapsed &&
                window.FloatingNoteSearchSlot.Visibility == Visibility.Collapsed;

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var visibleInNotes =
                window.FloatingSearchNotes.Visibility == Visibility.Visible &&
                string.Equals(
                    AutomationProperties.GetName(window.FloatingSearchNotes),
                    "搜索便签",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(window.FloatingNoteSearch),
                    "搜索便签",
                    StringComparison.Ordinal);
            var searchButtonShowsClosedMagnifier = ProbeSearchButtonShowsClosedMagnifier();

            Keyboard.Focus(window.FloatingAddNoteToggle);
            WindowValidationInput.Pump(window.Dispatcher);
            window.FloatingAddNoteToggle.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(window.Dispatcher);
            var addThenSearchTab =
                ReferenceEquals(Keyboard.FocusedElement, window.FloatingSearchNotes);

            window.FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var expandsAndFocuses =
                window.FloatingNoteSearchInputRow.Visibility == Visibility.Visible &&
                ReferenceEquals(Keyboard.FocusedElement, window.FloatingNoteSearch);

            var sampleNote = Workspace.Snapshot.VisibleActiveNotes.FirstOrDefault();
            var matchTerm = sampleNote is null
                ? "不会命中"
                : sampleNote.Text.Length <= 2
                    ? sampleNote.Text
                    : sampleNote.Text[..Math.Min(4, sampleNote.Text.Length)];
            var placementLeft = window.Left;
            var placementTop = window.Top;
            var placementWidth = window.ActualWidth;
            var placementHeight = window.ActualHeight;
            window.FloatingNoteSearch.Text = matchTerm;
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            var searchSizeUnchanged =
                Math.Abs(window.ActualWidth - placementWidth) <= 0.5 &&
                Math.Abs(window.ActualHeight - placementHeight) <= 0.5;
            var searchRefreshLeavesPlacementUnchanged =
                !searchSizeUnchanged ||
                (Math.Abs(window.Left - placementLeft) <= 0.5 &&
                 Math.Abs(window.Top - placementTop) <= 0.5);
            var expectedMatchIds = Workspace.Snapshot.VisibleActiveNotes
                .Select(note => note.Id)
                .ToArray();
            var filtersRenderedIds = GetFloatingRenderedNoteIds().SequenceEqual(
                expectedMatchIds,
                StringComparer.Ordinal);

            window.FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var hintText = window.FloatingNoteSearchHintTerm.Content as string;
            var collapseKeepsTerm =
                window.FloatingNoteSearchHint.Visibility == Visibility.Visible &&
                string.Equals(hintText, $"搜索:{matchTerm}", StringComparison.Ordinal) &&
                GetFloatingRenderedNoteIds().SequenceEqual(expectedMatchIds, StringComparer.Ordinal);
            var hintNamesDescribeAction =
                string.Equals(
                    AutomationProperties.GetName(window.FloatingNoteSearchHintTerm),
                    $"展开搜索：{matchTerm}",
                    StringComparison.Ordinal) &&
                string.Equals(hintText, $"搜索:{matchTerm}", StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(window.FloatingNoteSearchHintClear),
                    "清除搜索",
                    StringComparison.Ordinal);

            window.FloatingNoteSearchHintTerm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var hintReexpands =
                window.FloatingNoteSearchInputRow.Visibility == Visibility.Visible &&
                ReferenceEquals(Keyboard.FocusedElement, window.FloatingNoteSearch);

            WindowValidationInput.RaisePreviewKey(window, Key.Escape);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var escapeClears =
                window.FloatingNoteSearchSlot.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(window.FloatingNoteSearch.Text) &&
                string.IsNullOrWhiteSpace(Workspace.Snapshot.Filter.NoteSearch);

            window.FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var longTerm = new string('长', 140);
            window.FloatingNoteSearch.Text = longTerm;
            WindowValidationInput.Pump(window.Dispatcher);
            window.FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var hintTermTextBlock = ValidationDescendants<TextBlock>(window.FloatingNoteSearchHintTerm)
                .FirstOrDefault();
            var longHintBounds = ValidationBounds(window.FloatingNoteSearchHintTerm);
            var longHintSlotBounds = ValidationBounds(window.FloatingNoteSearchHint);
            var longHintClearBounds = ValidationBounds(window.FloatingNoteSearchHintClear);
            var longHintTermTrimsWithinSlot =
                window.FloatingNoteSearchHint.Visibility == Visibility.Visible &&
                hintTermTextBlock is not null &&
                hintTermTextBlock.TextTrimming == TextTrimming.CharacterEllipsis &&
                longHintBounds.Right <= longHintSlotBounds.Right + 0.5 &&
                !longHintBounds.IntersectsWith(longHintClearBounds) &&
                string.Equals(
                    window.FloatingNoteSearchHintTerm.Content as string,
                    $"搜索:{longTerm}",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(window.FloatingNoteSearchHintTerm),
                    $"展开搜索：{longTerm}",
                    StringComparison.Ordinal);
            WindowValidationInput.RaisePreviewKey(window, Key.Escape);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();

            Keyboard.Focus(window.FloatingAddNoteToggle);
            WindowValidationInput.Pump(window.Dispatcher);
            window.FloatingAddNoteToggle.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(window.Dispatcher);
            var collapsedOmitsSearchInput =
                !window.FloatingNoteSearch.IsTabStop &&
                ReferenceEquals(Keyboard.FocusedElement, window.FloatingSearchNotes);

            window.FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var missTerm = $"__floating_search_miss_{Guid.NewGuid():N}";
            window.FloatingNoteSearch.Text = missTerm;
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            TextBlock? FindEmptyMessage() =>
                ValidationDescendants<TextBlock>(window.FloatingRows).FirstOrDefault(text =>
                    string.Equals(text.Text, "没有匹配的便签。", StringComparison.Ordinal));
            Button? FindEmptyClear() =>
                ValidationDescendants<Button>(window.FloatingRows).FirstOrDefault(button =>
                    string.Equals(button.Content as string, "清除搜索", StringComparison.Ordinal));
            var emptyMessage = FindEmptyMessage();
            var emptyClear = FindEmptyClear();
            var emptyDoesNotOverlap =
                emptyClear is not null &&
                emptyMessage is not null &&
                !ValidationBounds(emptyMessage).IntersectsWith(ValidationBounds(emptyClear));
            // #149：搜索无匹配空态只提供「清除搜索」，不与「清除筛选」同现（门禁反例方向）。
            var searchEmptyHasNoFilterClear = !ValidationDescendants<Button>(window.FloatingRows).Any(button =>
                string.Equals(button.Content as string, "清除筛选", StringComparison.Ordinal));

            window.ApplyTheme();
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var themedMessage = FindEmptyMessage();
            var themedClear = FindEmptyClear();
            var emptyStateThemed =
                themedMessage is not null &&
                themedClear is not null &&
                themedMessage.ActualWidth > 0 &&
                themedMessage.ActualHeight > 0 &&
                themedClear.ActualWidth > 0 &&
                themedClear.ActualHeight > 0 &&
                !ValidationBounds(themedMessage).IntersectsWith(ValidationBounds(themedClear));

            window.SetQuickAddExpanded(true);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            emptyMessage = FindEmptyMessage();
            emptyClear = FindEmptyClear();
            var emptyState =
                GetFloatingRenderedNoteIds().Count == 0 &&
                emptyMessage is not null &&
                emptyClear is not null &&
                window.FloatingNoteEntry.IsVisible &&
                window.FloatingAddNote.IsEnabled;

            var addText = $"空态可新增-{Guid.NewGuid():N}";
            window.FloatingNoteText.Text = addText;
            window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var addedNote = Workspace.Snapshot.Settings.Notes.FirstOrDefault(note =>
                string.Equals(note.Text, addText, StringComparison.Ordinal));
            // #117 起：快速新增提交成功后表单自动收起，以收起态与新增成功共同证明空态可新增。
            var emptyStateAddWorks =
                window.FloatingNoteEntry.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(window.FloatingNoteTextError.Text) &&
                string.IsNullOrEmpty(window.FloatingNoteDateError.Text) &&
                (addedNote is not null ||
                 (window.FloatingNoteMessage.Text ?? string.Empty).StartsWith("保存失败：", StringComparison.Ordinal));
            if (addedNote is not null)
            {
                Workspace.Apply(new DeleteNoteChange(addedNote.Id));
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
            }

            window.FloatingNoteText.Text = originalNoteText;
            window.SetQuickAddExpanded(false);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();

            emptyClear = FindEmptyClear();
            emptyClear?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var clearRestores =
                string.IsNullOrEmpty(window.FloatingNoteSearch.Text) &&
                window.FloatingNoteSearchSlot.Visibility == Visibility.Collapsed &&
                GetFloatingRenderedNoteIds().SequenceEqual(
                    Workspace.Snapshot.VisibleActiveNotes.Select(note => note.Id),
                    StringComparer.Ordinal);

            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            WindowValidationInput.RaisePreviewKey(window, Key.F, ModifierKeys.Control);
            WindowValidationInput.Pump(window.Dispatcher);
            var ctrlFFromCountdowns =
                WindowScenarioAccess.CurrentWorkspace(window) == WorkspaceKind.Notes &&
                window.FloatingNoteSearchInputRow.Visibility == Visibility.Visible &&
                ReferenceEquals(Keyboard.FocusedElement, window.FloatingNoteSearch);

            WindowValidationInput.RaisePreviewKey(window, Key.F, ModifierKeys.Control);
            WindowValidationInput.Pump(window.Dispatcher);
            var ctrlFKeepsFocusWhenExpanded =
                ReferenceEquals(Keyboard.FocusedElement, window.FloatingNoteSearch) &&
                window.FloatingNoteSearchInputRow.Visibility == Visibility.Visible;

            window.ClearNoteSearch();
            WindowValidationInput.Pump(window.Dispatcher);
            Keyboard.Focus(window.FloatingNotesButton);
            WindowValidationInput.Pump(window.Dispatcher);
            WindowValidationInput.RaisePreviewKey(window, Key.F, ModifierKeys.Control);
            WindowValidationInput.Pump(window.Dispatcher);
            var ctrlFFromNotes =
                WindowScenarioAccess.CurrentWorkspace(window) == WorkspaceKind.Notes &&
                window.FloatingNoteSearchInputRow.Visibility == Visibility.Visible &&
                ReferenceEquals(Keyboard.FocusedElement, window.FloatingNoteSearch);

            Keyboard.Focus(window.FloatingNoteSearch);
            WindowValidationInput.Pump(window.Dispatcher);
            window.FloatingNoteSearch.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(window.Dispatcher);
            var firstCheckbox = ValidationDescendants<CheckBox>(window.FloatingRows).FirstOrDefault();
            var searchBeforeFirstCard =
                firstCheckbox is null ||
                ReferenceEquals(Keyboard.FocusedElement, firstCheckbox);

            window.ClearNoteSearch();
            WindowValidationInput.Pump(window.Dispatcher);
            Workspace.ApplyFilter(originalFilter with
            {
                SelectedTagIds = Array.AsReadOnly(new[] { "__floating_filter_miss_tag__" })
            });
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var noteFilterEmptyMessages = window.FloatingRows.Children
                .OfType<TextBlock>()
                .Select(text => text.Text)
                .ToArray();
            // #149：筛选约束空态提供「清除筛选」且不出现「清除搜索」。
            var filterEmptyClear = ValidationDescendants<Button>(window.FloatingRows).FirstOrDefault(button =>
                string.Equals(button.Content as string, "清除筛选", StringComparison.Ordinal));
            var filterEmptyHasNoSearchClear = !ValidationDescendants<Button>(window.FloatingRows).Any(button =>
                string.Equals(button.Content as string, "清除搜索", StringComparison.Ordinal));
            var noteFilterEmptyStateDistinguished =
                noteFilterEmptyMessages.SequenceEqual(new[] { "没有匹配的便签。" }) &&
                filterEmptyClear is not null &&
                filterEmptyHasNoSearchClear &&
                window.FloatingNoteSearchSlot.Visibility == Visibility.Collapsed;

            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var countdownFilterEmptyMessages = window.FloatingRows.Children
                .OfType<TextBlock>()
                .Select(text => text.Text)
                .ToArray();
            var countdownFilterEmptyStateDistinguished =
                countdownFilterEmptyMessages.SequenceEqual(new[] { "没有匹配的倒计时。" });

            // #149：点击「清除筛选」还原到无约束便签列表（对已脱树按钮触发 Click 走同一处理）。
            filterEmptyClear?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var constrainedEmptyClearRestoresList =
                filterEmptyClear is not null &&
                GetFloatingRenderedNoteIds().SequenceEqual(
                    Workspace.Snapshot.VisibleActiveNotes.Select(note => note.Id),
                    StringComparer.Ordinal) &&
                Workspace.Snapshot.Filter.NoteStatus == NoteStatusFilter.All &&
                !Workspace.Snapshot.Filter.UntaggedOnly &&
                Workspace.Snapshot.Filter.NoteDate is null;
            var constrainedEmptyProvidesAction =
                searchEmptyHasNoFilterClear &&
                noteFilterEmptyStateDistinguished &&
                constrainedEmptyClearRestoresList;
            Workspace.ApplyFilter(originalFilter);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();

            window.ApplyTheme();
            WindowValidationInput.Pump(window.Dispatcher);
            var usesQuickAddTheme = TextBoxUsesQuickAddTheme(window.FloatingNoteSearch);

            var scenarios = new FloatingNoteSearchValidationScenarios(
                hiddenInCountdowns,
                visibleInNotes,
                expandsAndFocuses,
                filtersRenderedIds,
                collapseKeepsTerm,
                hintReexpands,
                escapeClears,
                emptyState && emptyStateThemed && emptyStateAddWorks,
                emptyDoesNotOverlap,
                clearRestores,
                addThenSearchTab && collapsedOmitsSearchInput,
                searchBeforeFirstCard,
                ctrlFFromCountdowns && ctrlFFromNotes && ctrlFKeepsFocusWhenExpanded,
                usesQuickAddTheme,
                hintNamesDescribeAction,
                longHintTermTrimsWithinSlot,
                noteFilterEmptyStateDistinguished,
                countdownFilterEmptyStateDistinguished,
                searchButtonShowsClosedMagnifier,
                searchRefreshLeavesPlacementUnchanged,
                constrainedEmptyProvidesAction);
            Console.Error.WriteLine("FLOATING_NOTE_SEARCH_SCENARIOS " +
                System.Text.Json.JsonSerializer.Serialize(scenarios));
            return scenarios;
        }
        finally
        {
            if (originalExpanded)
            {
                window.ExpandNoteSearch(false);
            }
            else if (window.FloatingNoteSearch.IsVisible)
            {
                window.ClearNoteSearch();
            }
            window.FloatingNoteText.Text = originalNoteText;
            Workspace.ApplyFilter(originalFilter);
            window.ApplyTheme();
            ShowWorkspace(originalWorkspace);
            if (window.FloatingNoteSearch.IsKeyboardFocusWithin)
            {
                Keyboard.Focus(window.FloatingNotesButton);
            }

            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private bool ProbeSearchButtonShowsClosedMagnifier()
    {
        if (window.FloatingSearchNotes.Visibility != Visibility.Visible)
        {
            return false;
        }

        window.FloatingSearchNotes.ApplyTemplate();
        window.FloatingSearchNotes.UpdateLayout();

        Ellipse? lens = null;
        Line? handle = null;
        if (window.FloatingSearchNotes.Content is Grid icon)
        {
            lens = icon.Children.OfType<Ellipse>().FirstOrDefault();
            handle = icon.Children.OfType<Line>().FirstOrDefault();
        }

        lens ??= ValidationDescendants<Ellipse>(window.FloatingSearchNotes).FirstOrDefault();
        handle ??= ValidationDescendants<Line>(window.FloatingSearchNotes).FirstOrDefault();
        if (lens is null || handle is null)
        {
            return false;
        }

        var contentWidth = window.FloatingSearchNotes.ActualWidth -
            window.FloatingSearchNotes.Padding.Left -
            window.FloatingSearchNotes.Padding.Right -
            window.FloatingSearchNotes.BorderThickness.Left -
            window.FloatingSearchNotes.BorderThickness.Right;
        var contentHeight = window.FloatingSearchNotes.ActualHeight -
            window.FloatingSearchNotes.Padding.Top -
            window.FloatingSearchNotes.Padding.Bottom -
            window.FloatingSearchNotes.BorderThickness.Top -
            window.FloatingSearchNotes.BorderThickness.Bottom;
        if (contentWidth + 0.5 < 16 || contentHeight + 0.5 < 16)
        {
            return false;
        }

        var lensWidth = lens.ActualWidth > 0 ? lens.ActualWidth : lens.Width;
        var lensHeight = lens.ActualHeight > 0 ? lens.ActualHeight : lens.Height;
        return lensWidth >= 8 &&
            lensHeight >= 8 &&
            Math.Abs(lensWidth - lensHeight) <= 1 &&
            handle.X2 > handle.X1 &&
            handle.Y2 > handle.Y1;
    }

    /// <summary>
    ///     #212：渲染签名稳定刷新探针——tick 签名不变时行/卡实例保留、剩余时间就地更新、
    ///     焦点不因 tick 迁移；数据变化重建后焦点按「记录 ID + 控件角色」恢复并还原滚动；
    ///     隐藏与最小化跳过渲染，恢复可见/正常态立即渲染一次。
    /// </summary>
    private FloatingStableRefreshScenarios ProbeStableRefreshScenarios()
    {
        var originalWorkspace = _workspace;
        var originalFilter = Workspace.Snapshot.Filter;
        var originalPreferences = Workspace.Snapshot.Settings.Preferences;
        var temporaryCountdownIds = new List<string>();
        var temporaryNoteIds = new List<string>();
        using var animationPreference = ApplicationTheme.OverrideAnimationPreferenceForValidation(true);
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var nowUtc = Workspace.Snapshot.UtcNow;
            var precisionSaved = Workspace.Apply(new SavePreferencesChange(
                "Second",
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd)).Succeeded;
            for (var index = 0; index < 2; index++)
            {
                var name = $"验证悬浮稳定刷新-{index}";
                var result = Workspace.Apply(new SaveCountdownChange(
                    null,
                    name,
                    nowUtc.AddHours(2 + index),
                    Array.Empty<string>()));
                var added = Workspace.Snapshot.Settings.Countdowns
                    .LastOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
                if (!result.Succeeded || added is null)
                {
                    return FloatingStableRefreshScenarios.Failed;
                }

                temporaryCountdownIds.Add(added.Id);
            }

            window.UpdateTime(nowUtc);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var countdownRowsBefore = window.FloatingRows.Children.Cast<FrameworkElement>().ToArray();
            var countdownTextsBefore = window.FloatingCountdownTexts.ToArray();
            var clusterNamesBefore = FloatingCountdownClusterNames();
            var rebuildBeforeTick = window.FloatingRowsRebuildGenerationForValidation;
            window.UpdateTime(nowUtc.AddSeconds(1));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var countdownRowsPreservedAndTextUpdatesInPlace =
                precisionSaved &&
                countdownRowsBefore.Length > 0 &&
                countdownRowsBefore.SequenceEqual(window.FloatingRows.Children.Cast<FrameworkElement>()) &&
                window.FloatingRowsRebuildGenerationForValidation == rebuildBeforeTick &&
                window.FloatingLastRenderWasInPlaceForValidation &&
                !countdownTextsBefore.SequenceEqual(window.FloatingCountdownTexts) &&
                !clusterNamesBefore.SequenceEqual(FloatingCountdownClusterNames());
            var secondsValues = WindowValidationInput.RemainingSecondsValues(window.FloatingRows);
            var countdownSecondsSwitchWithoutAnimation =
                countdownRowsPreservedAndTextUpdatesInPlace &&
                secondsValues.Length >= 2 &&
                secondsValues.All(value => value.Opacity == 1 &&
                    !DependencyPropertyHelper.GetValueSource(value, UIElement.OpacityProperty).IsAnimated);

            ShowWorkspace(WorkspaceKind.Notes);
            var today = Workspace.Snapshot.BeijingNow.Date;
            string? noteB = null;
            foreach (var (suffix, date) in new[]
                     {
                         ("A", today.AddDays(1)),
                         ("B", today.AddDays(2)),
                         ("C", today.AddDays(3))
                     })
            {
                var text = $"验证悬浮稳定刷新便签-{suffix}-{Guid.NewGuid():N}"[..30];
                var result = Workspace.Apply(new SaveNoteChange(
                    null,
                    text,
                    date,
                    false,
                    Array.Empty<string>(),
                    NoReminder.Instance));
                var added = Workspace.Snapshot.Settings.Notes
                    .LastOrDefault(item => string.Equals(item.Text, text, StringComparison.Ordinal));
                if (!result.Succeeded || added is null)
                {
                    return FloatingStableRefreshScenarios.Failed;
                }

                temporaryNoteIds.Add(added.Id);
                if (suffix == "B")
                {
                    noteB = added.Id;
                }
            }

            window.UpdateTime(nowUtc);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var noteCardsBefore = window.FloatingRows.Children.Cast<FrameworkElement>().ToArray();
            var completionB = ValidationDescendants<CheckBox>(window.FloatingRows).FirstOrDefault(checkBox =>
                string.Equals(checkBox.Tag as string, noteB, StringComparison.Ordinal));
            var triggerB = ValidationDescendants<Button>(window.FloatingRows).FirstOrDefault(button =>
                string.Equals(button.Tag as string, noteB, StringComparison.Ordinal));
            if (completionB is null || triggerB is null)
            {
                return FloatingStableRefreshScenarios.Failed;
            }

            Keyboard.Focus(triggerB);
            WindowValidationInput.Pump(window.Dispatcher);
            var focusLandedOnTrigger = ReferenceEquals(Keyboard.FocusedElement, triggerB);
            window.FloatingBodyScroller.ScrollToVerticalOffset(window.FloatingBodyScroller.ScrollableHeight);
            WindowValidationInput.Pump(window.Dispatcher);
            var offsetBeforeTicks = window.FloatingBodyScroller.VerticalOffset;
            for (var tick = 1; tick <= 3; tick++)
            {
                window.UpdateTime(nowUtc.AddMilliseconds(250 * tick));
                WindowValidationInput.Pump(window.Dispatcher);
            }

            window.UpdateLayout();
            var noteBUncompleted = Workspace.Snapshot.Settings.Notes
                .FirstOrDefault(note => string.Equals(note.Id, noteB, StringComparison.Ordinal))
                is { IsCompleted: false };
            var focusStaysOnOverflowTriggerAcrossTicks =
                focusLandedOnTrigger &&
                ReferenceEquals(Keyboard.FocusedElement, triggerB) &&
                noteBUncompleted &&
                Math.Abs(window.FloatingBodyScroller.VerticalOffset - offsetBeforeTicks) <= 0.1;
            var noteCardInstancesPreservedAcrossTicks =
                noteCardsBefore.Length > 0 &&
                noteCardsBefore.SequenceEqual(window.FloatingRows.Children.Cast<FrameworkElement>()) &&
                ValidationDescendants<CheckBox>(window.FloatingRows).Any(checkBox =>
                    ReferenceEquals(checkBox, completionB));

            var offsetBeforeRebuild = window.FloatingBodyScroller.VerticalOffset;
            var rebuildGenerationBeforeAdd = window.FloatingRowsRebuildGenerationForValidation;
            var earlierText = $"验证悬浮稳定刷新便签-Z-{Guid.NewGuid():N}"[..30];
            var earlierResult = Workspace.Apply(new SaveNoteChange(
                null,
                earlierText,
                today,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
            var earlierNote = Workspace.Snapshot.Settings.Notes
                .LastOrDefault(item => string.Equals(item.Text, earlierText, StringComparison.Ordinal));
            if (earlierNote is not null)
            {
                temporaryNoteIds.Add(earlierNote.Id);
            }

            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var focusedAfterRebuild = Keyboard.FocusedElement;
            var rebuildRestoresFocusAndScrollByRole =
                earlierResult.Succeeded &&
                earlierNote is not null &&
                window.FloatingRowsRebuildGenerationForValidation > rebuildGenerationBeforeAdd &&
                focusedAfterRebuild is Button focusedButton &&
                string.Equals(focusedButton.Tag as string, noteB, StringComparison.Ordinal) &&
                string.Equals(
                    focusedButton.GetValue(FloatingWindow.NoteControlRoleProperty) as string,
                    FloatingWindow.NoteControlRoleOverflow,
                    StringComparison.Ordinal) &&
                !ReferenceEquals(focusedAfterRebuild, completionB) &&
                (offsetBeforeRebuild <= 0 ||
                 Math.Abs(window.FloatingBodyScroller.VerticalOffset - offsetBeforeRebuild) <= 1);

            var passBeforeHide = window.FloatingRowsRenderPassCountForValidation;
            var skippedBeforeHide = window.FloatingRowsSkippedRenderCountForValidation;
            var rebuildBeforeHide = window.FloatingRowsRebuildGenerationForValidation;
            TraceAnchoringForValidation("stable-refresh-before-hide");
            window.Hide();
            WindowValidationInput.Pump(window.Dispatcher);
            TraceAnchoringForValidation("stable-refresh-after-hide");
            window.UpdateTime(nowUtc.AddSeconds(3));
            window.UpdateTime(nowUtc.AddSeconds(4));
            WindowValidationInput.Pump(window.Dispatcher);
            var hiddenText = $"验证悬浮稳定刷新便签-H-{Guid.NewGuid():N}"[..30];
            var hiddenResult = Workspace.Apply(new SaveNoteChange(
                null,
                hiddenText,
                today,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
            var hiddenNote = Workspace.Snapshot.Settings.Notes
                .LastOrDefault(item => string.Equals(item.Text, hiddenText, StringComparison.Ordinal));
            if (hiddenNote is not null)
            {
                temporaryNoteIds.Add(hiddenNote.Id);
            }

            WindowValidationInput.Pump(window.Dispatcher);
            var hiddenSkipsRender =
                window.FloatingRowsSkippedRenderCountForValidation > skippedBeforeHide &&
                window.FloatingRowsRenderPassCountForValidation == passBeforeHide &&
                window.FloatingRowsRebuildGenerationForValidation == rebuildBeforeHide &&
                hiddenNote is not null &&
                !GetFloatingRenderedNoteIds().Contains(hiddenNote.Id, StringComparer.Ordinal);
            window.Show();
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            TraceAnchoringForValidation("stable-refresh-after-show");
            var hiddenSkipsRenderUntilVisible =
                hiddenResult.Succeeded &&
                hiddenSkipsRender &&
                window.FloatingRowsRenderPassCountForValidation > passBeforeHide &&
                window.FloatingRowsRebuildGenerationForValidation > rebuildBeforeHide &&
                hiddenNote is not null &&
                GetFloatingRenderedNoteIds().Contains(hiddenNote.Id, StringComparer.Ordinal);

            var passBeforeMinimize = window.FloatingRowsRenderPassCountForValidation;
            var skippedBeforeMinimize = window.FloatingRowsSkippedRenderCountForValidation;
            TraceAnchoringForValidation("stable-refresh-before-minimize");
            window.WindowState = System.Windows.WindowState.Minimized;
            WindowValidationInput.Pump(window.Dispatcher);
            TraceAnchoringForValidation("stable-refresh-after-minimize");
            window.UpdateTime(nowUtc.AddSeconds(5));
            window.UpdateTime(nowUtc.AddSeconds(6));
            WindowValidationInput.Pump(window.Dispatcher);
            var minimizedSkipsRender =
                window.FloatingRowsSkippedRenderCountForValidation > skippedBeforeMinimize &&
                window.FloatingRowsRenderPassCountForValidation == passBeforeMinimize;
            window.WindowState = System.Windows.WindowState.Normal;
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            TraceAnchoringForValidation("stable-refresh-after-restore");
            var minimizedSkipsRenderUntilRestored =
                minimizedSkipsRender &&
                window.FloatingRowsRenderPassCountForValidation > passBeforeMinimize;

            return new FloatingStableRefreshScenarios(
                focusStaysOnOverflowTriggerAcrossTicks,
                countdownRowsPreservedAndTextUpdatesInPlace,
                countdownSecondsSwitchWithoutAnimation,
                noteCardInstancesPreservedAcrossTicks,
                rebuildRestoresFocusAndScrollByRole,
                hiddenSkipsRenderUntilVisible,
                minimizedSkipsRenderUntilRestored);
        }
        finally
        {
            if (window.WindowState != System.Windows.WindowState.Normal)
            {
                window.WindowState = System.Windows.WindowState.Normal;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            Workspace.ApplyFilter(originalFilter);
            Workspace.Apply(new SavePreferencesChange(
                originalPreferences.DisplayPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd));
            foreach (var id in temporaryCountdownIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Workspace.Apply(new DeleteCountdownChange(id));
            }

            foreach (var id in temporaryNoteIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Workspace.Apply(new DeleteNoteChange(id));
            }

            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private string[] FloatingCountdownClusterNames() =>
        ValidationDescendants<Grid>(window.FloatingRows)
            .Where(grid => grid.Tag is string tag &&
                string.Equals(tag, FloatingWindow.RemainingBlocksTag, StringComparison.Ordinal))
            .Select(grid => AutomationProperties.GetName(grid) ?? string.Empty)
            .ToArray();

    /// <summary>
    ///     #117 悬浮窗便签底部命令区与卡片新视觉探针：默认收起的“＋ 添加便签”、“设置”打开主窗口、
    ///     “…”菜单编辑/删除、日期印记 pastel 轮换、卡底交替与手写体装饰。
    /// </summary>
    private FloatingBottomCommandScenarios ProbeBottomCommandScenarios()
    {
        var originalWorkspace = _workspace;
        var originalFilter = Workspace.Snapshot.Filter;
        var createdNoteIds = new List<string>();
        var createdTagIds = new List<string>();
        var managerWindow = _manager as Window;
        var managerWasVisible = managerWindow is { IsVisible: true };
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var countdownsHideCommandArea =
                window.FloatingNotesCommandArea.Visibility == Visibility.Collapsed &&
                !window.FloatingAddNoteToggle.IsVisible &&
                !window.FloatingSearchNotes.IsVisible &&
                !window.FloatingOpenSettings.IsVisible &&
                !window.FloatingNotesDecoText.IsVisible;

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var defaultCollapsedInNotes =
                window.FloatingNotesCommandArea.Visibility == Visibility.Visible &&
                window.FloatingAddNoteToggle.IsVisible &&
                window.FloatingOpenSettings.IsVisible &&
                window.FloatingSearchNotes.IsVisible &&
                string.Equals(
                    AutomationProperties.GetName(window.FloatingSearchNotes),
                    "搜索便签",
                    StringComparison.Ordinal) &&
                window.FloatingNoteEntry.Visibility == Visibility.Collapsed;
            var settingsGearConcentric = SettingsGearIsConcentric();

            var deco = window.FloatingNotesDecoText;
            var decoLeft = window.FloatingNotesDecoTextLeft;
            // #149：520 外壳上的手写体保持 13 DIP（相对 17 设计坐标的外壳例外，DESIGN §4）。
            var handwrittenDecorationLands =
                deco.IsVisible &&
                decoLeft.IsVisible &&
                !deco.IsHitTestVisible &&
                !decoLeft.IsHitTestVisible &&
                !deco.Focusable &&
                !decoLeft.Focusable &&
                ReferenceEquals(deco.FontFamily, window.FindResource("HandwrittenDecoFontFamily")) &&
                ReferenceEquals(deco.Foreground, window.FindResource("DecoTextSoftBrush")) &&
                ReferenceEquals(decoLeft.FontFamily, window.FindResource("HandwrittenDecoFontFamily")) &&
                ReferenceEquals(decoLeft.Foreground, window.FindResource("DecoTextSoftBrush")) &&
                Math.Abs(deco.FontSize - 13) <= 0.01 &&
                Math.Abs(decoLeft.FontSize - 13) <= 0.01;

            EnsureProbeNotes(createdNoteIds, 3);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var cards = window.FloatingRows.Children.OfType<Border>()
                .Where(card => Math.Abs(card.Width - CalendarIndexNoteCardWidth) <= 0.5)
                .ToList();
            var stampPaletteKeys = new[] { "StampPinkBrush", "StampBlueBrush", "StampGreenBrush" };
            var pastelStampsRotate =
                cards.Count >= 3 &&
                cards.Select((card, index) => (card, index)).All(pair =>
                    BrushMatchesResource(
                        FindNoteCardDateStamp(pair.card)?.Background,
                        window.TryFindResource(stampPaletteKeys[pair.index % stampPaletteKeys.Length])));
            var cardSurfacesAlternate =
                cards.Count >= 3 &&
                cards.Select((card, index) => (card, index)).All(pair =>
                    ReferenceEquals(
                        pair.card.Background,
                        window.TryFindResource(pair.index % 2 == 0 ? FloatingThemePalette.Daylight.NoteCardBrushKey : "NoteWhiteBrush")));

            var tagResult = Workspace.Apply(new CreateTagChange(
                $"悬浮探针-{Guid.NewGuid():N}"[..17],
                "#F4B6A6"));
            var toggleExpandsWithFilterTags = false;
            var toggleCollapses = false;
            if (tagResult.Succeeded && tagResult.CreatedTag is { } createdTag)
            {
                createdTagIds.Add(createdTag.Id);
                Workspace.ApplyFilter(originalFilter with
                {
                    SelectedTagIds = Array.AsReadOnly(new[] { createdTag.Id })
                });
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
                window.FloatingAddNoteToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
                toggleExpandsWithFilterTags =
                    window.FloatingNoteEntry.IsVisible &&
                    window.FloatingNotesCommandBar.Visibility == Visibility.Visible &&
                    window.FloatingSearchNotes.IsVisible &&
                    window.FloatingSearchNotes.IsTabStop &&
                    window.FloatingOpenSettings.IsVisible &&
                    window.FloatingOpenSettings.IsTabStop &&
                    ValidationBounds(window.FloatingNotesCommandScroller).Bottom <= window.ActualHeight + 0.5 &&
                    ValidationBounds(window.FloatingNoteEntry).Top >=
                        ValidationBounds(window.FloatingNotesCommandScroller).Top - 0.5 &&
                    ValidationBounds(window.FloatingNoteEntry).Top <=
                        ValidationBounds(window.FloatingNotesCommandScroller).Bottom + 0.5 &&
                    window.FloatingTags.SelectedTagIds.SequenceEqual(
                        new[] { createdTag.Id },
                        StringComparer.Ordinal) &&
                    window.FloatingNoteText.IsEnabled &&
                    window.FloatingNoteDate.IsEnabled;

                window.FloatingAddNoteToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
                toggleCollapses =
                    window.FloatingNoteEntry.Visibility == Visibility.Collapsed &&
                    window.FloatingNotesCommandBar.Visibility == Visibility.Visible;
            }

            // #149：快速新增正文占位文与管理窗口一致；空白时可见、输入后隐藏。
            var quickAddPlaceholder = window.FindName("FloatingNoteTextPlaceholder") as TextBlock;
            var quickAddPlaceholderMirrorsManager =
                quickAddPlaceholder is not null &&
                string.Equals(
                    quickAddPlaceholder.Text,
                    "写下要记住的事情吧…",
                    StringComparison.Ordinal) &&
                quickAddPlaceholder.Visibility == Visibility.Visible;
            window.SetQuickAddExpanded(true);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            window.FloatingNoteText.Text = "占位隐藏探针";
            WindowValidationInput.Pump(window.Dispatcher);
            quickAddPlaceholderMirrorsManager =
                quickAddPlaceholderMirrorsManager &&
                quickAddPlaceholder is not null &&
                quickAddPlaceholder.Visibility == Visibility.Collapsed;
            window.FloatingNoteText.Text = string.Empty;
            WindowValidationInput.Pump(window.Dispatcher);
            // #149：展开态底部切换钮为次级「收起」，表单「添加」是唯一实心粉钮。
            var quickAddToggleSecondaryWhenExpanded =
                window.FloatingNoteEntry.IsVisible &&
                ReferenceEquals(window.FloatingAddNoteToggle.Style, window.FindResource("SecondaryButton")) &&
                string.Equals(window.FloatingAddNoteToggle.Content as string, "收起", StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(window.FloatingAddNoteToggle),
                    "收起快速新增便签表单",
                    StringComparison.Ordinal) &&
                ReferenceEquals(window.FloatingAddNote.Style, window.FindResource("PrimaryButton"));
            window.SetQuickAddExpanded(false);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            quickAddToggleSecondaryWhenExpanded =
                quickAddToggleSecondaryWhenExpanded &&
                ReferenceEquals(window.FloatingAddNoteToggle.Style, window.FindResource("FloatingAccentButton")) &&
                window.FloatingAddNoteToggle.Content is StackPanel &&
                string.Equals(
                    AutomationProperties.GetName(window.FloatingAddNoteToggle),
                    "添加便签",
                    StringComparison.Ordinal);

            window.SetQuickAddExpanded(true);
            WindowValidationInput.Pump(window.Dispatcher);
            var addText = $"悬浮底部命令探针便签-{Guid.NewGuid():N}";
            window.FloatingNoteText.Text = addText;
            window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var addedNote = Workspace.Snapshot.Settings.Notes.FirstOrDefault(note =>
                string.Equals(note.Text, addText, StringComparison.Ordinal));
            if (addedNote is not null)
            {
                createdNoteIds.Add(addedNote.Id);
            }

            var submitCollapsesAfterSuccess =
                addedNote is not null &&
                window.FloatingNoteEntry.Visibility == Visibility.Collapsed;

            var noteMenuProvidesEdit = false;
            var noteMenuDeleteConfirms = false;
            var noteMenuKeyboardLifecycle = false;
            if (addedNote is not null)
            {
                var trigger = ValidationDescendants<Button>(window.FloatingRows).FirstOrDefault(button =>
                    string.Equals(button.Tag as string, addedNote.Id, StringComparison.Ordinal));
                if (trigger is not null)
                {
                    // #149/#319：先走键盘路径——打开聚焦「编辑」，方向键进入「删除」，Esc 关闭并回到触发器。
                    noteMenuKeyboardLifecycle = ProbeNoteOverflowMenuKeyboard(trigger);

                    trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    WindowValidationInput.Pump(window.Dispatcher);
                    var menu = window.FindActiveNoteOverflowMenu();
                    var menuEdit = menu?.Items.OfType<MenuItem>().FirstOrDefault(item =>
                        string.Equals(item.Header as string, "编辑", StringComparison.Ordinal));
                    if (menu is { IsOpen: true } && menuEdit is not null)
                    {
                        menuEdit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        WindowValidationInput.Pump(window.Dispatcher);
                        window.UpdateLayout();
                        if (_manager is MainWindow revealedMain)
                        {
                            var editorText = (TextBox)revealedMain.FindName("NoteText");
                            var cancelEdit = (Button)revealedMain.FindName("CancelNoteEdit");
                            noteMenuProvidesEdit =
                                revealedMain.IsVisible &&
                                string.Equals(
                                    editorText.Text,
                                    addedNote.Text,
                                    StringComparison.Ordinal) &&
                                cancelEdit.Visibility == Visibility.Visible;
                            cancelEdit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                            WindowValidationInput.Pump(window.Dispatcher);
                        }

                        noteMenuProvidesEdit = noteMenuProvidesEdit && !menu.IsOpen;
                    }

                    noteMenuDeleteConfirms = ProbeNoteOverflowMenuDelete(
                        trigger,
                        addedNote.Id);
                }
            }

            var settingsOpensManagerPreservingWorkspace = false;
            if (managerWindow is not null)
            {
                if (managerWasVisible)
                {
                    managerWindow.Hide();
                    WindowValidationInput.Pump(window.Dispatcher);
                }

                window.FloatingOpenSettings.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(window.Dispatcher);
                settingsOpensManagerPreservingWorkspace =
                    managerWindow.IsVisible &&
                    managerWindow.WindowState == System.Windows.WindowState.Normal &&
                    _workspace == WorkspaceKind.Notes;
                managerWindow.Hide();
                WindowValidationInput.Pump(window.Dispatcher);
            }

            var collapsedCommandBarBounds = ValidationBounds(window.FloatingNotesCommandBar);
            var collapsedListOffset = window.FloatingBodyScroller.VerticalOffset;
            var expandedQuickAddScrollable = false;
            window.SetQuickAddExpanded(true);
            WindowValidationInput.Pump(window.Dispatcher);
            window.FloatingNoteText.Text = new string('敏', 60);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var expandedScrollerBounds = ValidationBounds(window.FloatingNotesCommandScroller);
            if (expandedScrollerBounds.Bottom <= window.ActualHeight + 0.5 &&
                window.FloatingNotesCommandScroller.ScrollableHeight > 0.5)
            {
                window.FloatingNotesCommandScroller.ScrollToBottom();
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
                var commandAreaBottom = ValidationBounds(window.FloatingNotesCommandArea).Bottom;
                var scrolledBarBounds = ValidationBounds(window.FloatingNotesCommandBar);
                var commandBarReachableAtBottom =
                    window.FloatingNotesCommandScroller.VerticalOffset >=
                        window.FloatingNotesCommandScroller.ScrollableHeight - 0.5 &&
                    commandAreaBottom <= window.ActualHeight + 0.5 &&
                    scrolledBarBounds.Bottom <= commandAreaBottom + 0.5 &&
                    scrolledBarBounds.Bottom >= commandAreaBottom - 8 &&
                    ValidationBounds(window.FloatingAddNote).Bottom <= commandAreaBottom + 0.5;

                window.FloatingNotesCommandScroller.ScrollToHome();
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
                var noteTextTop = ValidationBounds(window.FloatingNoteText).Top;
                var entryTopAfterHome = ValidationBounds(window.FloatingNoteEntry).Top;
                var entryRestoredToTop =
                    window.FloatingNotesCommandScroller.VerticalOffset <= 0.5 &&
                    Math.Abs(entryTopAfterHome - expandedScrollerBounds.Top) <= 0.5 &&
                    noteTextTop >= expandedScrollerBounds.Top - 0.5 &&
                    noteTextTop <= expandedScrollerBounds.Top + 24;

                window.SetQuickAddExpanded(false);
                window.FloatingNoteText.Text = string.Empty;
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
                var restoredBarBounds = ValidationBounds(window.FloatingNotesCommandBar);
                expandedQuickAddScrollable =
                    commandBarReachableAtBottom &&
                    entryRestoredToTop &&
                    Math.Abs(restoredBarBounds.X - collapsedCommandBarBounds.X) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Y - collapsedCommandBarBounds.Y) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Width - collapsedCommandBarBounds.Width) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Height - collapsedCommandBarBounds.Height) <= 0.5 &&
                    Math.Abs(window.FloatingBodyScroller.VerticalOffset - collapsedListOffset) <= 0.5 &&
                    window.FloatingNotesCommandScroller.VerticalOffset <= 0.5;
            }
            else
            {
                window.SetQuickAddExpanded(false);
                window.FloatingNoteText.Text = string.Empty;
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
            }

            var quickAddReopensAtTop = ProbeQuickAddReopensAtTop(createdNoteIds);

            return new FloatingBottomCommandScenarios(
                defaultCollapsedInNotes,
                countdownsHideCommandArea,
                toggleExpandsWithFilterTags,
                toggleCollapses,
                submitCollapsesAfterSuccess,
                expandedQuickAddScrollable,
                settingsOpensManagerPreservingWorkspace,
                noteMenuProvidesEdit,
                noteMenuDeleteConfirms,
                pastelStampsRotate,
                cardSurfacesAlternate,
                handwrittenDecorationLands,
                quickAddPlaceholderMirrorsManager,
                quickAddToggleSecondaryWhenExpanded,
                noteMenuKeyboardLifecycle,
                settingsGearConcentric,
                quickAddReopensAtTop);
        }
        finally
        {
            foreach (var id in createdNoteIds)
            {
                Workspace.Apply(new DeleteNoteChange(id));
            }

            Workspace.ApplyFilter(originalFilter);
            foreach (var id in createdTagIds)
            {
                Workspace.Apply(new DeleteTagChange(id));
            }

            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            if (managerWindow is not null && managerWasVisible)
            {
                managerWindow.Show();
                managerWindow.Activate();
                WindowValidationInput.Pump(window.Dispatcher);
            }
        }
    }

    // 用户在展开的快速新增表单里滚动后收起（点「收起」或提交成功），再点「＋ 添加便签」，表单顶部要贴着命令区顶部。
    // 命令区 FloatingNotesCommandScroller 收起后显示的偏移归零，请求偏移却还记着，不复位就回到旧位置。两条收起路径
    // 各走一遍；再展开后填入长正文，内容一定超出限高，偏移没复位时看得出来。
    private bool ProbeQuickAddReopensAtTop(List<string> createdNoteIds)
    {
        var longText = new string('敏', 60);
        try
        {
            var reopensAfterToggle = QuickAddReopensAtTopAfter(
                longText,
                longText,
                () => window.FloatingAddNoteToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
            var submitText = $"快速新增复位探针-{Guid.NewGuid():N}{longText}";
            var reopensAfterSubmit = QuickAddReopensAtTopAfter(
                submitText,
                longText,
                () => window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
            var submitted = Workspace.Snapshot.Settings.Notes.FirstOrDefault(note =>
                string.Equals(note.Text, submitText, StringComparison.Ordinal));
            if (submitted is not null)
            {
                createdNoteIds.Add(submitted.Id);
            }

            return reopensAfterToggle && reopensAfterSubmit && submitted is not null;
        }
        finally
        {
            window.FloatingNotesCommandScroller.ScrollToHome();
            window.SetQuickAddExpanded(false);
            window.FloatingNoteText.Text = string.Empty;
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    // 从收起态点开表单、填入正文并把命令区滚到底，执行收起动作，再点开并填入长正文，最后点「收起」回到收起态。
    private bool QuickAddReopensAtTopAfter(string textBeforeCollapse, string textAfterReopen, Action collapse)
    {
        window.FloatingAddNoteToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        window.FloatingNoteText.Text = textBeforeCollapse;
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        window.FloatingNotesCommandScroller.ScrollToBottom();
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        var scrolledDown =
            window.FloatingNoteEntry.IsVisible &&
            window.FloatingNotesCommandScroller.VerticalOffset > 0.5;

        collapse();
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        var collapsed = window.FloatingNoteEntry.Visibility == Visibility.Collapsed;

        window.FloatingAddNoteToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(window.Dispatcher);
        window.FloatingNoteText.Text = textAfterReopen;
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        var reopenedAtTop =
            window.FloatingNoteEntry.IsVisible &&
            window.FloatingNotesCommandScroller.ScrollableHeight > 0.5 &&
            window.FloatingNotesCommandScroller.VerticalOffset <= 0.5 &&
            Math.Abs(ValidationBounds(window.FloatingNoteEntry).Top - ValidationBounds(window.FloatingNotesCommandScroller).Top) <= 0.5;

        window.FloatingAddNoteToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        window.FloatingNoteText.Text = string.Empty;
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        return scrolledDown && collapsed && reopenedAtTop;
    }

    // 「设置」按钮图标：15x15 框内齿轮外轮廓的几何中心、孔的中心与框中心三者重合。容差 0.5 DIP：
    // 容得下设备像素取整，抓得住外轮廓曾比孔高 1.5 DIP 的偏移。
    private bool SettingsGearIsConcentric()
    {
        if (window.FloatingOpenSettings.Content is not StackPanel { Children.Count: > 0 } content ||
            content.Children[0] is not Grid { ActualWidth: > 0 } icon ||
            icon.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault() is not { Data: { } outline } outlinePath ||
            icon.Children.OfType<Ellipse>().FirstOrDefault() is not { } hole)
        {
            return false;
        }

        var bounds = outline.Bounds;
        var outlineCenter = outlinePath.TranslatePoint(
            new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), icon);
        var holeCenter = hole.TranslatePoint(new Point(hole.ActualWidth / 2, hole.ActualHeight / 2), icon);
        var boxCenter = new Point(icon.ActualWidth / 2, icon.ActualHeight / 2);
        return (outlineCenter - holeCenter).Length <= 0.5 && (outlineCenter - boxCenter).Length <= 0.5;
    }

    // 键盘焦点只画一套（DESIGN.md §4 常驻焦点环）：倒计时工作区沿 Tab 走完悬浮窗一圈；便签工作区展开快速新增后
    // 再走一圈，然后从键盘打开一条便签的「…」菜单和快速新增的标签选择器，各在弹层里走一圈。代码新建的行内控件
    // 与弹层按钮都在内。
    private FloatingKeyboardFocusScenarios ProbeKeyboardFocusScenarios()
    {
        var originalWorkspace = _workspace;
        var createdNoteIds = new List<string>();
        string? createdTagId = null;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var countdownsFailures = WindowValidationInput.KeyboardFocusFailuresInWindow(window);

            ShowWorkspace(WorkspaceKind.Notes);
            EnsureProbeNotes(createdNoteIds, 1);
            // 标签选择器弹层要有标签才有勾选框。
            if (Workspace.Snapshot.Settings.Tags.Count == 0 &&
                Workspace.Apply(new CreateTagChange($"焦点探针-{Guid.NewGuid():N}"[..17], "#F4B6A6")) is
                    { Succeeded: true, CreatedTag: { } createdTag })
            {
                createdTagId = createdTag.Id;
            }

            window.SetQuickAddExpanded(true);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var notesFailures = WindowValidationInput.KeyboardFocusFailuresInWindow(window);

            IReadOnlyList<string> overflowMenuFailures = ["没有便签「…」触发按钮"];
            var trigger = ValidationDescendants<Button>(window.FloatingRows).FirstOrDefault(button =>
                Equals(button.GetValue(FloatingWindow.NoteControlRoleProperty), FloatingWindow.NoteControlRoleOverflow));
            if (trigger is not null)
            {
                overflowMenuFailures = ProbeOverflowContextMenuKeyboardFocus(trigger);
            }

            var tagSelectorFailures = OpenFromKeyboardAndTraverse(
                window.FloatingTags.PopupTrigger,
                window.FloatingTags.Popup,
                "标签选择器");
            return new FloatingKeyboardFocusScenarios(
                countdownsFailures, notesFailures, overflowMenuFailures, tagSelectorFailures);
        }
        finally
        {
            // Tab 走过展开的表单会把命令区滚下去；ScrollViewer 收起后仍记着这个请求偏移，下次展开回到原处、
            // 表单顶部卷出视口，所以先滚回顶部再收起，不把滚动状态留给后面的探针。
            window.FloatingNotesCommandScroller.ScrollToHome();
            window.SetQuickAddExpanded(false);
            foreach (var id in createdNoteIds)
            {
                Workspace.Apply(new DeleteNoteChange(id));
            }

            if (createdTagId is not null)
            {
                Workspace.Apply(new DeleteTagChange(createdTagId));
            }

            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }

        IReadOnlyList<string> ProbeOverflowContextMenuKeyboardFocus(Button opener)
        {
            var failures = new List<string>();
            IDisposable? menuInput = null;
            try
            {
                window.CloseNoteOverflowMenu(immediate: true);
                window.Activate();
                Keyboard.Focus(opener);
                WindowValidationInput.Pump(window.Dispatcher);
                WindowValidationInput.RaiseKey(opener, Key.Enter);
                WindowValidationInput.Pump(window.Dispatcher);
                if (window.FindActiveNoteOverflowMenu() is not { IsOpen: true } menu)
                {
                    return ["「…」菜单没有从键盘打开"];
                }

                var items = menu.Items.OfType<MenuItem>().ToArray();
                if (items.Length != 2)
                {
                    return ["「…」菜单项数量不是 2"];
                }

                menuInput = WindowValidationInput.AcquireContextMenuInputForValidation(menu);
                if (!items[0].IsKeyboardFocused)
                {
                    Keyboard.Focus(menu);
                    WindowValidationInput.Pump(window.Dispatcher);
                    WindowValidationInput.RaiseKey(menu, Key.Down);
                    WindowValidationInput.Pump(window.Dispatcher);
                }

                var expectedHighlight = (Application.Current.FindResource("MainActionBrush") as SolidColorBrush)?.Color;
                var expectedText = (Application.Current.FindResource("MainActionTextBrush") as SolidColorBrush)?.Color;
                for (var index = 0; index < items.Length; index++)
                {
                    var item = items[index];
                    if (index > 0)
                    {
                        WindowValidationInput.RaiseKey(items[index - 1], Key.Down);
                        WindowValidationInput.Pump(window.Dispatcher);
                    }

                    if (!item.IsKeyboardFocused || !item.IsHighlighted)
                    {
                        failures.Add($"「{item.Header}」未获得原生菜单键盘高亮");
                    }

                    if (item.FocusVisualStyle is not null)
                    {
                        failures.Add($"「{item.Header}」挂了默认系统 FocusVisualStyle");
                    }

                    if ((item.Background as SolidColorBrush)?.Color != expectedHighlight ||
                        (item.Foreground as SolidColorBrush)?.Color != expectedText)
                    {
                        failures.Add($"「{item.Header}」高亮色未使用共享主行动色");
                    }
                }

                WindowValidationInput.RaiseKey(items[^1], Key.Escape);
                WindowValidationInput.Pump(window.Dispatcher);
                if (menu.IsOpen || !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(opener))
                {
                    failures.Add("Esc 关闭「…」菜单后未恢复触发器单层焦点环");
                }

                return failures;
            }
            finally
            {
                window.CloseNoteOverflowMenu(immediate: true);
                menuInput?.Dispose();
                WindowValidationInput.Pump(window.Dispatcher);
            }
        }

        IReadOnlyList<string> OpenFromKeyboardAndTraverse(Button opener, Popup popup, string name)
        {
            try
            {
                Keyboard.Focus(opener);
                WindowValidationInput.Pump(window.Dispatcher);
                opener.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var focusEntered = WindowValidationInput.WaitUntil(window.Dispatcher, () =>
                    popup is { IsOpen: true, Child: { } root } &&
                    Keyboard.FocusedElement is Visual focused &&
                    root.IsAncestorOf(focused));
                return focusEntered && Keyboard.FocusedElement is UIElement first
                    ? WindowValidationInput.KeyboardFocusFailuresAlongTabCycle(first)
                    : [$"{name}没有从键盘打开并接住焦点"];
            }
            finally
            {
                WindowValidationInput.ClosePopupForCleanup(popup);
            }
        }
    }

    /// <summary>“…”菜单删除：确认弹层取消则保留便签，确认则删除；弹层经 Dispatcher 队列驱动。</summary>
    private bool ProbeNoteOverflowMenuDelete(Button trigger, string noteId)
    {
        bool OpenMenuAndDriveDialog(Func<ConfirmationDialog?, bool> drive)
        {
            if (window.FindActiveNoteOverflowMenu() is not { IsOpen: true })
            {
                trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(window.Dispatcher);
            }

            if (window.FindActiveNoteOverflowMenu() is not { IsOpen: true } menu)
            {
                return false;
            }

            ConfirmationDialog? dialog = null;
            ValidationExercises.QueueConfirmationDialogAction(window, visibleDialog =>
            {
                dialog = visibleDialog;
                return drive(dialog);
            });
            var menuDelete = menu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                string.Equals(item.Header as string, "删除", StringComparison.Ordinal));
            menuDelete?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            return dialog is not null;
        }

        var cancelled = OpenMenuAndDriveDialog(dialog =>
        {
            return WindowValidationInput.InvokeButton(dialog!.CancelButton);
        });
        var noteStillExistsAfterCancel = Workspace.Snapshot.Settings.Notes.Any(note =>
            string.Equals(note.Id, noteId, StringComparison.Ordinal));
        var confirmed = OpenMenuAndDriveDialog(dialog =>
        {
            return WindowValidationInput.InvokeButton(dialog!.ConfirmButton);
        });
        var deleted = cancelled &&
            noteStillExistsAfterCancel &&
            confirmed &&
            !Workspace.Snapshot.Settings.Notes.Any(note =>
                string.Equals(note.Id, noteId, StringComparison.Ordinal));
        // #214：就地撤销是瞬时状态，布局探针继续测量收起后的命令区。
        window.DismissDeleteUndoForValidation();
        return deleted;
    }

    /// <summary>
    ///     #149/#319：悬浮便签 `…` 原生菜单键盘生命周期——打开聚焦首项「编辑」，方向键进入「删除」，
    ///     Esc 立即关闭并把焦点还给触发按钮。
    /// </summary>
    private bool ProbeNoteOverflowMenuKeyboard(Button trigger)
    {
        IDisposable? menuInput = null;
        try
        {
            window.CloseNoteOverflowMenu(immediate: true);
            Window.GetWindow(trigger)?.Activate();
            Keyboard.Focus(trigger);
            WindowValidationInput.Pump(window.Dispatcher);
            WindowValidationInput.RaiseKey(trigger, Key.Enter);
            WindowValidationInput.Pump(window.Dispatcher);
            WindowValidationInput.Pump(window.Dispatcher);
            if (window.FindActiveNoteOverflowMenu() is not { IsOpen: true } menu)
            {
                return false;
            }

            var items = menu.Items.OfType<MenuItem>().ToArray();
            if (items.Length != 2)
            {
                return false;
            }

            var menuEdit = items[0];
            var menuDelete = items[1];
            menuInput = WindowValidationInput.AcquireContextMenuInputForValidation(menu);
            if (!menuEdit.IsKeyboardFocused)
            {
                Keyboard.Focus(menu);
                WindowValidationInput.Pump(window.Dispatcher);
                WindowValidationInput.RaiseKey(menu, Key.Down);
                WindowValidationInput.Pump(window.Dispatcher);
            }

            var openedWithFocusOnEdit =
                menu.IsOpen &&
                ReferenceEquals(Keyboard.FocusedElement, menuEdit);
            WindowValidationInput.RaiseKey(menuEdit, Key.Down);
            WindowValidationInput.Pump(window.Dispatcher);
            var arrowReachesDelete = ReferenceEquals(Keyboard.FocusedElement, menuDelete);
            WindowValidationInput.RaiseKey(menuDelete, Key.Escape);
            WindowValidationInput.Pump(window.Dispatcher);
            WindowValidationInput.Pump(window.Dispatcher);
            var closedBackToTrigger =
                !menu.IsOpen &&
                (trigger.IsKeyboardFocusWithin ||
                 ReferenceEquals(FocusManager.GetFocusedElement(window), trigger));
            return openedWithFocusOnEdit && arrowReachesDelete && closedBackToTrigger;
        }
        finally
        {
            window.CloseNoteOverflowMenu(immediate: true);
            menuInput?.Dispose();
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private void EnsureProbeNotes(List<string> createdIds, int minimumVisible)
    {
        while (Workspace.Snapshot.VisibleActiveNotes.Count < minimumVisible)
        {
            var result = Workspace.Apply(new SaveNoteChange(
                null,
                $"悬浮底部命令探针-{Guid.NewGuid():N}",
                Workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
            if (!result.Succeeded)
            {
                return;
            }

            var note = Workspace.Snapshot.Settings.Notes.LastOrDefault(candidate =>
                candidate.Text.StartsWith("悬浮底部命令探针-", StringComparison.Ordinal));
            if (note is null)
            {
                return;
            }

            createdIds.Add(note.Id);
        }
    }

    internal static Border? FindNoteCardDateStamp(Border card) =>
        ValidationDescendants<Border>(card).FirstOrDefault(FloatingWindow.IsNoteCardDateStamp);

    private static bool BrushMatchesResource(Brush? actual, object? expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush actualBrush &&
         expected is SolidColorBrush expectedBrush &&
         actualBrush.Color == expectedBrush.Color);

    private Rect ValidationBounds(FrameworkElement element) =>
        element.TransformToAncestor(window).TransformBounds(
            new Rect(new Point(0, 0), element.RenderSize));

    private IReadOnlyList<string> GetFloatingRenderedNoteIds() =>
        ValidationDescendants<CheckBox>(window.FloatingRows)
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
        var originalText = window.FloatingNoteText.Text;
        var originalDate = window.FloatingNoteDate.SelectedDate;
        double originalNotesScrollOffset;
        if (originalWorkspace == WorkspaceKind.Notes)
        {
            originalNotesScrollOffset = window.FloatingBodyScroller.VerticalOffset;
        }
        else
        {
            ShowWorkspace(WorkspaceKind.Notes);
            originalNotesScrollOffset = window.FloatingBodyScroller.VerticalOffset;
            ShowWorkspace(originalWorkspace);
        }
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            // 字段错误需在真实可见表单中聚焦来源控件，展开快速新增表单后验证。
            window.SetQuickAddExpanded(true);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);
            var datePickerKeyboardLifecycleWorks =
                WindowValidationInput.ExerciseDatePicker(window.FloatingNoteDate);

            window.FloatingNoteText.Text = string.Empty;
            window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var textErrorAtSource =
                window.FloatingNoteTextError.Visibility == Visibility.Visible &&
                window.FloatingNoteText.IsKeyboardFocusWithin &&
                string.Equals(
                    window.FloatingNoteMessage.Text,
                    window.FloatingNoteTextError.Text,
                    StringComparison.Ordinal) &&
                ValidationErrorIsAccessible(window.FloatingNoteTextError, window.FloatingNoteText);
            window.FloatingNoteText.Text = "验证字段错误恢复";
            WindowValidationInput.Pump(window.Dispatcher);
            var textErrorCleared =
                window.FloatingNoteTextError.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(window.FloatingNoteMessage.Text) &&
                ValidationErrorIsCleared(window.FloatingNoteTextError, window.FloatingNoteText);

            window.FloatingNoteDate.SelectedDate = null;
            window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var dateErrorAtSource =
                window.FloatingNoteDateError.Visibility == Visibility.Visible &&
                window.FloatingNoteDate.IsKeyboardFocusWithin &&
                string.Equals(
                    window.FloatingNoteMessage.Text,
                    window.FloatingNoteDateError.Text,
                    StringComparison.Ordinal) &&
                ValidationErrorIsAccessible(window.FloatingNoteDateError, window.FloatingNoteDate);
            window.FloatingNoteDate.SelectedDate = Workspace.Snapshot.BeijingNow.Date;
            WindowValidationInput.Pump(window.Dispatcher);
            var dateErrorCleared =
                window.FloatingNoteDateError.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(window.FloatingNoteMessage.Text) &&
                ValidationErrorIsCleared(window.FloatingNoteDateError, window.FloatingNoteDate);

            return new FloatingUiPolishScenarios(
                datePickerKeyboardLifecycleWorks,
                textErrorAtSource && dateErrorAtSource,
                textErrorCleared && dateErrorCleared);
        }
        finally
        {
            window.FloatingNoteText.Text = originalText;
            window.FloatingBodyScroller.ScrollToVerticalOffset(originalNotesScrollOffset);
            WindowValidationInput.Pump(window.Dispatcher);
            ShowWorkspace(originalWorkspace);
            window.FloatingNoteDate.SelectedDate = originalDate;
            window.ClearNoteFieldErrors();
            window.SetNoteMessage(string.Empty);
            WindowValidationInput.Pump(window.Dispatcher);
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
    private static readonly string[] QuickAddThemeCheckNames =
    [
        "TextBox",
        "DatePicker",
        "DatePickerPopup",
        "ComboBox",
        "ComboBoxPopup",
        "TagSelector",
        "TagSelectorPopup",
        "CheckBox",
        "ScrollViewer",
        "ScrollBar"
    ];

    internal static IReadOnlyList<string> RequiredQuickAddThemeChecks => QuickAddThemeCheckNames;

    internal FloatingQuickAddPaletteBindingValidation ProbeQuickAddPaletteBindings()
    {
        var actual = new FloatingQuickAddPaletteMapping(
            ResolveQuickAddPaletteResourceKey(window.FloatingNoteEntry.Background),
            ResolveQuickAddPaletteResourceKey(window.FloatingNoteText.Foreground),
            ResolveQuickAddPaletteResourceKey(window.FloatingNoteTextError.Foreground),
            ResolveQuickAddPaletteResourceKey(window.FloatingReminderRulesSummary.Foreground),
            ResolveQuickAddPaletteResourceKey(window.FloatingReminderMessage.Foreground));
        return new FloatingQuickAddPaletteBindingValidation(
            "Day",
            FloatingQuickAddPaletteBindings.Expected(),
            actual,
            FloatingQuickAddPaletteBindings.Evaluate(actual));
    }

    private string ResolveQuickAddPaletteResourceKey(Brush? brush)
    {
        foreach (var resourceKey in FloatingQuickAddPaletteBindings.KnownResourceKeys)
        {
            if (ReferenceEquals(brush, window.FindResource(resourceKey)))
            {
                return resourceKey;
            }
        }

        return string.Empty;
    }

    private FloatingDragInteractionExerciseResult RunDragInteractionExercise()
    {
        var originalWorkspace = _workspace;
        var originalLeft = window.Left;
        var originalTop = window.Top;
        var originalDragState = window.DragProbe.Capture();
        var nativeDragRequestsBefore = originalDragState.NativeWindowDragRequestCount;
        var nativeDragCompletionsBefore = originalDragState.NativeWindowDragCompletionCount;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            window.UpdateLayout();

            var workArea = MonitorWorkAreaResolver.ResolveForWindow(window).WorkAreaDip;
            var minimumLeft = workArea.Left + 12;
            var maximumLeft = Math.Max(minimumLeft, workArea.Right - window.ActualWidth - 12);
            var minimumTop = workArea.Top + 12;
            var maximumTop = Math.Max(minimumTop, workArea.Bottom - window.ActualHeight - 12);
            window.DragProbe.MarkManualPlacement();
            window.Left = workArea.Right + 24;
            window.Top = workArea.Bottom + 24;

            var backgroundGesture = WindowScenarioAccess.CreateMouseLeftButtonDownGesture();
            window.TaskWrap.RaiseEvent(backgroundGesture);
            var backgroundRequestsNativeWindowDrag =
                window.DragProbe.Capture().NativeWindowDragRequestCount == nativeDragRequestsBefore + 1;
            var pointerGesture = WindowScenarioAccess.CreateMouseLeftButtonDownGesture();
            window.FloatingDragHandle.RaiseEvent(pointerGesture);
            var titleRequestsNativeWindowDrag =
                window.DragProbe.Capture().NativeWindowDragRequestCount == nativeDragRequestsBefore + 2;
            var syntheticGestureDoesNotClaimPhysicalCompletion =
                window.DragProbe.Capture().NativeWindowDragCompletionCount == nativeDragCompletionsBefore;
            var dragAttemptClampsToWorkArea =
                window.Left >= minimumLeft - 0.5 &&
                window.Left <= maximumLeft + 0.5 &&
                window.Top >= minimumTop - 0.5 &&
                window.Top <= maximumTop + 0.5;

            var interactiveControls = new UIElement[]
            {
                window.FloatingCountdownsButton,
                window.FloatingNotesButton,
                window.MinimizeToTrayButton,
                window.OpenManagerButton,
                window.CloseFloatingButton,
                window.FloatingBodyScroller,
                window.FloatingAddNoteToggle,
                window.FloatingSearchNotes,
                window.FloatingOpenSettings,
                window.FloatingNoteText,
                window.FloatingNoteDate,
                window.FloatingManageReminderRules,
                window.FloatingTags,
                window.FloatingAddNote
            };
            foreach (var control in interactiveControls)
            {
                control.RaiseEvent(WindowScenarioAccess.CreateMouseLeftButtonDownGesture());
            }

            return new FloatingDragInteractionExerciseResult(
                window.TaskWrap.Background is not null &&
                ReferenceEquals(window.TaskWrap.InputHitTest(new Point(260, 20)), window.TaskWrap) &&
                backgroundGesture.Handled &&
                backgroundRequestsNativeWindowDrag,
                window.FloatingDragHandle.IsHitTestVisible &&
                window.FloatingDragHandle.Background is not null &&
                window.FloatingDragHandle.Cursor == Cursors.SizeAll &&
                pointerGesture.Handled,
                titleRequestsNativeWindowDrag,
                syntheticGestureDoesNotClaimPhysicalCompletion,
                dragAttemptClampsToWorkArea,
                window.DragProbe.Capture().NativeWindowDragRequestCount == nativeDragRequestsBefore + 2,
                interactiveControls.All(control => !WindowScenarioAccess.IsVisualDescendantOf(control, window.FloatingDragHandle)));
        }
        finally
        {
            window.DragProbe.Restore(originalDragState, originalLeft, originalTop);

            ShowWorkspace(originalWorkspace);
        }
    }

    private FloatingManualPlacementExerciseResult RunManualPlacementExercise()
    {
        var originalDragState = window.DragProbe.Capture();
        var originalLeft = window.Left;
        var originalTop = window.Top;
        var workArea = MonitorWorkAreaResolver.ResolveForWindow(window).WorkAreaDip;
        var minimumLeft = workArea.Left + 12;
        var maximumLeft = Math.Max(minimumLeft, workArea.Right - window.ActualWidth - 12);
        var minimumTop = workArea.Top + 12;
        var maximumTop = Math.Max(minimumTop, workArea.Bottom - window.ActualHeight - 12);
        var manualLeft = maximumLeft > minimumLeft
            ? minimumLeft + Math.Min(24, maximumLeft - minimumLeft)
            : minimumLeft;
        var manualTop = maximumTop > minimumTop
            ? minimumTop + Math.Min(24, maximumTop - minimumTop)
            : minimumTop;

        try
        {
            window.Left = manualLeft;
            window.Top = manualTop;
            window.UpdateTime(Workspace.Snapshot.UtcNow);

            var manualPositionPersists =
                window.DragProbe.Capture().HasManualPlacement &&
                Math.Abs(window.Left - manualLeft) <= 0.5 &&
                Math.Abs(window.Top - manualTop) <= 0.5;

            window.Left = workArea.Right + 24;
            window.Top = workArea.Bottom + 24;
            var displacedLeft = window.Left;
            var displacedTop = window.Top;
            window.UpdateTime(Workspace.Snapshot.UtcNow);
            var refreshLeavesManualDisplacement =
                Math.Abs(window.Left - displacedLeft) <= 0.5 &&
                Math.Abs(window.Top - displacedTop) <= 0.5;

            window.Reanchor();

            var remainsWithinWorkArea =
                window.Left >= minimumLeft - 0.5 &&
                window.Left <= maximumLeft + 0.5 &&
                window.Top >= minimumTop - 0.5 &&
                window.Top <= maximumTop + 0.5;

            return new FloatingManualPlacementExerciseResult(
                manualPositionPersists && refreshLeavesManualDisplacement && remainsWithinWorkArea);
        }
        finally
        {
            window.DragProbe.Restore(originalDragState, originalLeft, originalTop);
        }
    }

    private bool RunCrossDayReminderAddExercise()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        window.SetQuickAddExpanded(true);
        var initial = Workspace.Snapshot;
        var countBefore = initial.Settings.Notes.Count;
        var noteDate = initial.BeijingNow.AddDays(2).Date;
        var firstDueDay = initial.BeijingNow.AddDays(3);
        var firstDue = new DateTimeOffset(
            firstDueDay.Year,
            firstDueDay.Month,
            firstDueDay.Day,
            10,
            15,
            0,
            firstDueDay.Offset);
        window.FloatingNoteText.Text = "验证快速跨天提醒";
        window.FloatingNoteDate.SelectedDate = noteDate;
        WindowScenarioAccess.SetFloatingReminderRules(window, new ReminderRule[]
        {
            new FixedIntervalReminderRule(
                Guid.NewGuid().ToString("N"),
                firstDue.ToUniversalTime(),
                new ReminderInterval(30))
        });
        window.SetQuickAddExpanded(true);
        window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var afterAdd = Workspace.Snapshot.Settings;
        var added = afterAdd.Notes.LastOrDefault(note => note.Text == "验证快速跨天提醒");
        return
            added is not null &&
            afterAdd.Notes.Count == countBefore + 1 &&
            added.Date == noteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
            added.ReminderIntervalMinutes == 30 &&
            added.ReminderStartUtc == firstDue.ToUniversalTime().Subtract(TimeSpan.FromMinutes(30));
    }

    private ReminderModeClickResult RunReminderModeClickExercises()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        window.SetQuickAddExpanded(true);
        var initial = Workspace.Snapshot;
        var noteDate = initial.BeijingNow.AddDays(2).Date;
        var onceDue = initial.BeijingNow.AddDays(3);
        onceDue = new DateTimeOffset(
            onceDue.Year,
            onceDue.Month,
            onceDue.Day,
            10,
            10,
            0,
            onceDue.Offset);
        var repeatDue = onceDue.AddDays(1).AddMinutes(20);

        window.FloatingNoteText.Text = "验证悬浮窗不提醒";
        window.FloatingNoteDate.SelectedDate = noteDate;
        WindowScenarioAccess.SetFloatingReminderRules(window, Array.Empty<ReminderRule>());
        window.SetQuickAddExpanded(true);
        window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var noReminderWorks = Workspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗不提醒")?.Reminder is NoReminder;

        window.FloatingNoteText.Text = "验证悬浮窗仅一次";
        window.FloatingNoteDate.SelectedDate = noteDate;
        WindowScenarioAccess.SetFloatingReminderRules(window, new ReminderRule[]
        {
            new AbsoluteOnceReminderRule(
                Guid.NewGuid().ToString("N"),
                onceDue.ToUniversalTime())
        });
        window.SetQuickAddExpanded(true);
        window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var onceWorks = Workspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗仅一次")?.Reminder is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            } once &&
            once.FirstDueUtc == onceDue.ToUniversalTime();

        window.FloatingNoteText.Text = "验证悬浮窗重复提醒";
        window.FloatingNoteDate.SelectedDate = noteDate;
        WindowScenarioAccess.SetFloatingReminderRules(window, new ReminderRule[]
        {
            new FixedIntervalReminderRule(
                Guid.NewGuid().ToString("N"),
                repeatDue.ToUniversalTime(),
                new ReminderInterval(30))
        });
        window.SetQuickAddExpanded(true);
        window.FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var repeatWorks = Workspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗重复提醒")?.Reminder is RepeatingReminder repeating &&
            repeating.FirstDueUtc == repeatDue.ToUniversalTime() &&
            repeating.Interval.Minutes == 30;

        return new ReminderModeClickResult(noReminderWorks, onceWorks, repeatWorks);
    }

    internal bool InitialCountdownSelectionSurvivesRefresh()
    {
        window.ApplyTheme();
        window.UpdateLayout();
        var survives = window.FloatingCountdownsButton.IsChecked == true &&
            window.FloatingNotesButton.IsChecked == false &&
            window.HeaderControlHasAccessibleContrast(window.FloatingCountdownsButton) &&
            window.HeaderControlHasAccessibleContrast(window.FloatingNotesButton);
        if (!survives)
        {
            var before = new[] { ReadHeaderState(window.FloatingCountdownsButton), ReadHeaderState(window.FloatingNotesButton) };
            var reachedSelectedColor = WindowValidationInput.WaitUntil(window.Dispatcher, () =>
                window.FloatingCountdownsButton.Template?.FindName("Surface", window.FloatingCountdownsButton) is Border surface &&
                surface.Background is SolidColorBrush background &&
                window.FindResource("PinkAccentStrongBrush") is SolidColorBrush target &&
                background.Color == target.Color);
            var after = new[] { ReadHeaderState(window.FloatingCountdownsButton), ReadHeaderState(window.FloatingNotesButton) };
            Console.Error.WriteLine("FLOATING_SELECTION_DIAGNOSTIC " +
                System.Text.Json.JsonSerializer.Serialize(new { before, reachedSelectedColor, after }));
        }

        return survives;

        object ReadHeaderState(ToggleButton button)
        {
            var surface = button.Template?.FindName("Surface", button) as Border;
            return new
            {
                button.Name,
                button.IsChecked,
                Background = surface?.Background?.ToString(),
                Foreground = button.Foreground?.ToString(),
                ContrastPasses = window.HeaderControlHasAccessibleContrast(button)
            };
        }
    }


    private FloatingThemeExerciseResult RunDayThemeExercise()
    {
        var snapshot = Workspace.Snapshot;
        window.ApplyTheme();
        window.UpdateTime(snapshot.UtcNow);
        window.UpdateLayout();
        var activeTheme = FloatingThemePalette.Daylight;
        var panelColor = window.ThemeColor(activeTheme.PanelBrushKey).ToString();
        var plushGlowColor = window.PlushGlowColor();
        var mascotPresentationMode = window.MascotPresentationMode();
        var textContrast = window.HasAccessibleThemeTextContrast;
        var surfacesFollowActiveTheme = window.SurfacesFollowActiveTheme();
        var plushSceneFollowsActiveTheme = window.PlushSceneFollowsActiveTheme();
        return new FloatingThemeExerciseResult(
            new[] { "Day" },
            new[] { panelColor },
            new[] { plushGlowColor },
            new[] { mascotPresentationMode },
            string.Equals(mascotPresentationMode, "DayRaster", StringComparison.Ordinal),
            textContrast,
            surfacesFollowActiveTheme,
            plushSceneFollowsActiveTheme);
    }

    internal FloatingQuickAddThemeProbeResult ProbeQuickAddThemeResources()
    {
        var originalWorkspace = _workspace;
        var originalTags = Workspace.Snapshot.Settings.Tags;
        var originalSelection = window.FloatingTags.SelectedTagIds.ToArray();
        var originalOffset = window.FloatingBodyScroller.VerticalOffset;
        var originalExpanded = window.FloatingNoteSearch.IsVisible;
        var probeTag = new TagRecord(
            $"floating-theme-probe-{Guid.NewGuid():N}",
            "主题探针",
            "#FFD36B");
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            window.SetQuickAddExpanded(true);
            window.ExpandNoteSearch(focus: false);
            window.FloatingTags.SetTags(originalTags.Concat([probeTag]).ToArray());
            window.FloatingTags.SetSelection([probeTag.Id]);
            window.UpdateLayout();
            WindowValidationInput.Pump(window.Dispatcher);

            // #232/#303: validate the rendered focus boundary against the application
            // non-red input focus role; matching the local (possibly misbound) alias is insufficient.
            window.Activate();
            Keyboard.Focus(window.FloatingNoteText);
            WindowValidationInput.Pump(window.Dispatcher);
            var focusSurface = window.FloatingNoteText.Template.FindName("Surface", window.FloatingNoteText) as Border;
            if (!window.FloatingNoteText.IsKeyboardFocused ||
                !ReferenceEquals(focusSurface?.BorderBrush, Application.Current.FindResource("MainActionFocusBrush")))
            {
                throw new InvalidOperationException("#303 快速新增实际输入焦点必须使用非红色的 MainActionFocusBrush，而非 Active 或 Danger。");
            }

            var textBoxes = new[] { window.FloatingNoteText, window.FloatingNoteSearch };
            var datePickers = new[] { window.FloatingNoteDate };
            var comboBoxes = Array.Empty<ComboBox>();

            var checks = QuickAddThemeCheckNames.ToDictionary(
                name => name,
                _ => false,
                StringComparer.Ordinal);
            var diagnostics = new List<FloatingQuickAddThemeProbeDiagnostic>();
            checks["TextBox"] = textBoxes.All(TextBoxUsesQuickAddTheme);
            checks["DatePicker"] = datePickers.All(ControlUsesQuickAddSurface);
            checks["DatePickerPopup"] = datePickers
                .Select(picker => DatePickerPopupUsesQuickAddTheme(picker, diagnostics))
                .ToArray()
                .All(matches => matches);
            checks["ComboBox"] = comboBoxes.All(ControlUsesQuickAddSurface);
            checks["ComboBoxPopup"] = comboBoxes
                .Select(comboBox => ComboBoxPopupUsesQuickAddTheme(comboBox, diagnostics))
                .ToArray()
                .All(matches => matches);
            checks["TagSelector"] =
                UsesQuickAddResources(window.FloatingTags, "CanvasBrush", "InkBrush", "ControlBorderBrush") &&
                ControlUsesQuickAddSurface(window.FloatingTags.PopupTrigger);

            var tagPopupOpened = false;
            UIElement? popupRoot = null;
            TextBox? popupSearch = null;
            CheckBox? popupCheckBox = null;
            ScrollViewer? popupScroller = null;
            EventHandler tagPopupOpenedHandler = (_, _) =>
            {
                tagPopupOpened = true;
                popupRoot = window.FloatingTags.Popup.Child;
                if (popupRoot is FrameworkElement popupElement)
                {
                    popupElement.UpdateLayout();
                }
                popupSearch = popupRoot is null
                    ? null
                    : QuickAddVisualDescendants<TextBox>(popupRoot).FirstOrDefault();
                popupCheckBox = popupRoot is null
                    ? null
                    : QuickAddVisualDescendants<CheckBox>(popupRoot).FirstOrDefault();
                popupScroller = popupRoot is null
                    ? null
                    : QuickAddVisualDescendants<ScrollViewer>(popupRoot).FirstOrDefault();
            };
            window.FloatingTags.Popup.Opened += tagPopupOpenedHandler;
            try
            {
                window.FloatingTags.IsPopupOpen = true;
                WindowValidationInput.Pump(window.Dispatcher);
            }
            finally
            {
                window.FloatingTags.Popup.Opened -= tagPopupOpenedHandler;
            }
            checks["TagSelectorPopup"] =
                tagPopupOpened &&
                popupRoot is Border popupSurface &&
                BorderUsesQuickAddSurface(popupSurface, "ControlBorderBrush") &&
                popupSearch is not null &&
                TextBoxUsesQuickAddTheme(popupSearch);
            checks["CheckBox"] = popupCheckBox is not null && CheckBoxUsesQuickAddTheme(popupCheckBox);
            checks["ScrollViewer"] =
                ScrollViewerUsesQuickAddTheme(window.FloatingBodyScroller) &&
                popupScroller is not null &&
                ScrollViewerUsesQuickAddTheme(popupScroller);
            checks["ScrollBar"] =
                ScrollViewerScrollBarsUseQuickAddTheme(window.FloatingBodyScroller) &&
                popupScroller is not null &&
                ScrollViewerScrollBarsUseQuickAddTheme(popupScroller);

            AddFailedQuickAddThemeDiagnostics(
                checks,
                diagnostics,
                textBoxes,
                datePickers,
                comboBoxes,
                popupRoot,
                popupSearch,
                popupCheckBox,
                popupScroller);

            return new FloatingQuickAddThemeProbeResult(checks, diagnostics);
        }
        finally
        {
            if (originalExpanded)
            {
                window.ExpandNoteSearch(false);
            }
            else if (window.FloatingNoteSearch.IsVisible)
            {
                window.ClearNoteSearch();
            }
            window.FloatingTags.IsPopupOpen = false;
            window.FloatingNoteDate.IsDropDownOpen = false;
            window.FloatingTags.SetTags(originalTags);
            window.FloatingTags.SetSelection(originalSelection);
            ShowWorkspace(originalWorkspace);
            window.FloatingBodyScroller.ScrollToVerticalOffset(originalOffset);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private bool DatePickerPopupUsesQuickAddTheme(
        DatePicker picker,
        ICollection<FloatingQuickAddThemeProbeDiagnostic> diagnostics)
    {
        picker.BringIntoView();
        WindowValidationInput.Pump(window.Dispatcher);
        picker.IsDropDownOpen = false;
        picker.ApplyTemplate();
        var trigger = picker.Template.FindName("PART_Button", picker) as Button;
        var owner = Window.GetWindow(picker);
        owner?.Activate();
        owner?.Focus();
        trigger?.Focus();
        WindowValidationInput.Pump(window.Dispatcher);
        var popup = picker.Template.FindName("PART_Popup", picker) as Popup;
        var popupOpened = false;
        var popupWasOpenDuringEvent = false;
        var surfaceMatchedDuringEvent = false;
        var resourcesMatchedDuringEvent = false;
        System.Windows.Controls.Calendar? calendarDuringEvent = null;
        RoutedEventHandler openedHandler = (_, _) =>
        {
            popupOpened = true;
            popupWasOpenDuringEvent = popup?.IsOpen == true;
            calendarDuringEvent = PickerTemplateParts.GetCalendar(picker);
            calendarDuringEvent?.ApplyTemplate();
            calendarDuringEvent?.UpdateLayout();
            surfaceMatchedDuringEvent =
                calendarDuringEvent is not null &&
                ControlUsesQuickAddSurface(calendarDuringEvent);
            resourcesMatchedDuringEvent =
                calendarDuringEvent is not null &&
                UsesQuickAddResources(
                    calendarDuringEvent,
                    "CardBrush",
                    "ActiveBrush",
                    "MutedBrush",
                    "SegmentSelectedBrush");
        };
        picker.CalendarOpened += openedHandler;
        try
        {
            trigger?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(window.Dispatcher);
            var matches =
                popupOpened &&
                popupWasOpenDuringEvent &&
                surfaceMatchedDuringEvent &&
                resourcesMatchedDuringEvent;
            if (!matches)
            {
                AddQuickAddThemeDiagnostics(
                    diagnostics,
                    "DatePickerPopup",
                    (FrameworkElement?)calendarDuringEvent ?? picker,
                    popupWasOpenDuringEvent,
                    $"Picker={picker.Name};" +
                    $"CalendarOpened={popupOpened};" +
                    $"TriggerFocused={trigger?.IsKeyboardFocusWithin == true};" +
                    $"PART_Popup={(popup is null ? "missing" : "present")};" +
                    $"ProductionStaysOpen={popup?.StaysOpen.ToString() ?? "unknown"};" +
                    $"Calendar={(calendarDuringEvent is null ? "missing" : "present")}",
                    "CardBrush",
                    "ActiveBrush",
                    "MutedBrush",
                    "SegmentSelectedBrush");
            }

            return matches;
        }
        finally
        {
            picker.CalendarOpened -= openedHandler;
            picker.IsDropDownOpen = false;
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private bool ComboBoxPopupUsesQuickAddTheme(
        ComboBox comboBox,
        ICollection<FloatingQuickAddThemeProbeDiagnostic> diagnostics)
    {
        comboBox.BringIntoView();
        WindowValidationInput.Pump(window.Dispatcher);
        comboBox.IsDropDownOpen = false;
        comboBox.ApplyTemplate();
        var trigger = QuickAddVisualDescendants<ToggleButton>(comboBox).FirstOrDefault();
        var toggleProvider = trigger is null
            ? null
            : new ToggleButtonAutomationPeer(trigger).GetPattern(PatternInterface.Toggle) as IToggleProvider;
        var owner = Window.GetWindow(comboBox);
        owner?.Activate();
        owner?.Focus();
        if (owner is not null && trigger is not null)
        {
            FocusManager.SetFocusedElement(owner, trigger);
        }

        Keyboard.Focus((IInputElement?)trigger ?? comboBox);
        WindowValidationInput.Pump(window.Dispatcher);
        var popup = comboBox.Template.FindName("PART_Popup", comboBox) as Popup;
        var popupOpened = false;
        var popupSurfaceMatchedDuringEvent = false;
        var itemResourcesMatchedDuringEvent = false;
        Border? popupSurfaceDuringEvent = null;
        ComboBoxItem? itemDuringEvent = null;
        EventHandler openedHandler = (_, _) =>
        {
            popupOpened = true;
            popupSurfaceDuringEvent = popup?.Child as Border;
            popupSurfaceDuringEvent?.UpdateLayout();
            itemDuringEvent = popupSurfaceDuringEvent is null
                ? null
                : QuickAddVisualDescendants<ComboBoxItem>(popupSurfaceDuringEvent).FirstOrDefault();
            itemDuringEvent ??= comboBox.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem;
            itemDuringEvent?.ApplyTemplate();
            itemDuringEvent?.UpdateLayout();
            popupSurfaceMatchedDuringEvent =
                popupSurfaceDuringEvent is not null &&
                BorderUsesQuickAddSurface(popupSurfaceDuringEvent);
            itemResourcesMatchedDuringEvent =
                itemDuringEvent is not null &&
                UsesQuickAddResources(
                    itemDuringEvent,
                    "InkBrush",
                    "CardBrush",
                    "SegmentSelectedBrush",
                    "SegmentSelectedForegroundBrush");
        };
        if (popup is not null)
        {
            popup.Opened += openedHandler;
        }

        try
        {
            toggleProvider?.Toggle();
            WindowValidationInput.Pump(window.Dispatcher);
            var matches =
                popupOpened &&
                popupSurfaceMatchedDuringEvent &&
                itemResourcesMatchedDuringEvent;
            if (!matches)
            {
                AddQuickAddThemeDiagnostics(
                    diagnostics,
                    "ComboBoxPopup",
                    (FrameworkElement?)itemDuringEvent ?? (FrameworkElement?)popupSurfaceDuringEvent ?? comboBox,
                    popupOpened,
                    $"ComboBox={comboBox.Name};" +
                    $"PopupOpened={popupOpened};" +
                    $"DropDownOpenAfterPump={comboBox.IsDropDownOpen};" +
                    $"Trigger={(trigger is null ? "missing" : "present")};" +
                    $"ToggleProvider={(toggleProvider is null ? "missing" : "present")};" +
                    $"TriggerCheckedAfterPump={trigger?.IsChecked?.ToString() ?? "unknown"};" +
                    $"TriggerFocused={trigger?.IsKeyboardFocusWithin == true};" +
                    $"PART_Popup={(popup is null ? "missing" : "present")};" +
                    $"ProductionStaysOpen={popup?.StaysOpen.ToString() ?? "unknown"};" +
                    $"PopupChild={popup?.Child?.GetType().Name ?? "missing"};" +
                    $"Item0={(itemDuringEvent is null ? "missing" : "present")}",
                    "InkBrush",
                    "CardBrush",
                    "SegmentSelectedBrush",
                    "SegmentSelectedForegroundBrush");
            }

            return matches;
        }
        finally
        {
            if (popup is not null)
            {
                popup.Opened -= openedHandler;
            }

            comboBox.IsDropDownOpen = false;
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }

    private void AddFailedQuickAddThemeDiagnostics(
        IReadOnlyDictionary<string, bool> checks,
        ICollection<FloatingQuickAddThemeProbeDiagnostic> diagnostics,
        IReadOnlyList<TextBox> textBoxes,
        IReadOnlyList<DatePicker> datePickers,
        IReadOnlyList<ComboBox> comboBoxes,
        UIElement? popupRoot,
        TextBox? popupSearch,
        CheckBox? popupCheckBox,
        ScrollViewer? popupScroller)
    {
        void AddForEach(
            string check,
            IEnumerable<FrameworkElement> elements,
            params string[] keys)
        {
            if (checks.TryGetValue(check, out var passed) && passed)
            {
                return;
            }

            foreach (var element in elements)
            {
                AddQuickAddThemeDiagnostics(
                    diagnostics,
                    check,
                    element,
                    window.FloatingTags.Popup.IsOpen,
                    string.Empty,
                    keys);
            }
        }

        AddForEach("TextBox", textBoxes, "CanvasBrush", "InkBrush", "ControlBorderBrush", "ActiveBrush", "CardBrush");
        AddForEach("DatePicker", datePickers, "CanvasBrush", "InkBrush", "ControlBorderBrush");
        AddForEach("ComboBox", comboBoxes, "CanvasBrush", "InkBrush", "ControlBorderBrush");
        AddForEach("TagSelector", [window.FloatingTags, window.FloatingTags.PopupTrigger], "CardAltBrush", "InkBrush", "ControlBorderBrush");

        if (!checks["TagSelectorPopup"])
        {
            AddQuickAddThemeDiagnostics(
                diagnostics,
                "TagSelectorPopup",
                popupRoot as FrameworkElement ?? window.FloatingTags,
                window.FloatingTags.Popup.IsOpen,
                $"PopupChild={popupRoot?.GetType().Name ?? "missing"};" +
                $"Search={(popupSearch is null ? "missing" : "present")}",
                "CanvasBrush",
                "InkBrush",
                "PeachLineBrush",
                "ControlBorderBrush",
                "ActiveBrush",
                "CardBrush");
        }

        if (!checks["CheckBox"])
        {
            AddQuickAddThemeDiagnostics(
                diagnostics,
                "CheckBox",
                (FrameworkElement?)popupCheckBox ?? window.FloatingTags,
                window.FloatingTags.Popup.IsOpen,
                $"CheckBox={(popupCheckBox is null ? "missing" : "present")};" +
                $"Checked={popupCheckBox?.IsChecked?.ToString() ?? "unknown"}",
                "CanvasBrush",
                "BodyBrush",
                "ControlBorderBrush",
                "ActiveBrush");
        }

        AddForEach(
            "ScrollViewer",
            new FrameworkElement?[] { window.FloatingBodyScroller, popupScroller }.OfType<FrameworkElement>(),
            "ControlBorderBrush",
            "ActiveBrush");
        if (!checks["ScrollBar"])
        {
            foreach (var scroller in new[] { window.FloatingBodyScroller, popupScroller }.Where(scroller => scroller is not null))
            {
                scroller!.ApplyTemplate();
                var vertical = scroller.Template.FindName("PART_VerticalScrollBar", scroller) as ScrollBar;
                var horizontal = scroller.Template.FindName("PART_HorizontalScrollBar", scroller) as ScrollBar;
                AddQuickAddThemeDiagnostics(
                    diagnostics,
                    "ScrollBar",
                    (FrameworkElement?)vertical ?? (FrameworkElement?)horizontal ?? scroller,
                    window.FloatingTags.Popup.IsOpen,
                    $"Vertical={(vertical is null ? "missing" : "present")};" +
                    $"Horizontal={(horizontal is null ? "missing" : "present")};" +
                    $"Scroller={scroller.Name}",
                    "ControlBorderBrush",
                    "ActiveBrush",
                    "FloatingMutedBrush");
            }
        }
    }

    private void AddQuickAddThemeDiagnostics(
        ICollection<FloatingQuickAddThemeProbeDiagnostic> diagnostics,
        string check,
        FrameworkElement element,
        bool popupIsOpen,
        string templateParts,
        params string[] resourceKeys)
    {
        var elementName = string.IsNullOrWhiteSpace(element.Name)
            ? element.GetType().Name
            : element.Name;
        var templatePresent = element is not Control control || control.Template is not null;
        var inBodyScrollerScope = QuickAddVisualDescendants<DependencyObject>(window.FloatingBodyScroller)
            .Any(candidate => ReferenceEquals(candidate, element));
        foreach (var resourceKey in resourceKeys)
        {
            var expected = window.Resources.Contains(resourceKey) ? window.Resources[resourceKey] : null;
            var actual = element.TryFindResource(resourceKey);
            diagnostics.Add(new FloatingQuickAddThemeProbeDiagnostic(
                check,
                elementName,
                resourceKey,
                DescribeQuickAddResource(expected),
                DescribeQuickAddResource(actual),
                ReferenceEquals(actual, expected),
                element.Resources.Contains(resourceKey),
                inBodyScrollerScope && window.FloatingBodyScroller.Resources.Contains(resourceKey),
                popupIsOpen,
                templatePresent,
                templateParts,
                DescribeQuickAddElementState(element)));
        }
    }

    private static string DescribeQuickAddResource(object? resource) => resource switch
    {
        SolidColorBrush brush => brush.Color.ToString(),
        null => "missing",
        _ => resource.GetType().Name
    };

    private static string DescribeQuickAddElementState(FrameworkElement element) => element switch
    {
        Control control =>
            $"Style={control.Style?.GetType().Name ?? "missing"};" +
            $"Template={control.Template?.GetType().Name ?? "missing"};" +
            $"Background={DescribeQuickAddResource(control.Background)};" +
            $"Foreground={DescribeQuickAddResource(control.Foreground)};" +
            $"Border={DescribeQuickAddResource(control.BorderBrush)}",
        Border border =>
            $"Background={DescribeQuickAddResource(border.Background)};" +
            $"Border={DescribeQuickAddResource(border.BorderBrush)}",
        _ => element.GetType().Name
    };

    private bool TextBoxUsesQuickAddTheme(TextBox textBox)
    {
        textBox.ApplyTemplate();
        var surfaceMatches = textBox.Style is not null &&
            textBox.Template is not null &&
            Equals(textBox.Background, textBox.TryFindResource("CanvasBrush")) &&
            Equals(
                textBox.Foreground,
                textBox.TryFindResource(
                    ReferenceEquals(textBox, window.FloatingNoteText) ||
                    ReferenceEquals(textBox, window.FloatingNoteSearch)
                        ? "FloatingNoteInputTextBrush"
                        : "InkBrush")) &&
            Equals(textBox.BorderBrush, textBox.TryFindResource("ControlBorderBrush")) &&
            UsesQuickAddResources(textBox, "CanvasBrush", "InkBrush", "ControlBorderBrush");
        return surfaceMatches &&
            UsesQuickAddResources(textBox, "ActiveBrush", "CardBrush");
    }

    private bool CheckBoxUsesQuickAddTheme(CheckBox checkBox)
    {
        checkBox.ApplyTemplate();
        checkBox.UpdateLayout();
        var box = QuickAddVisualDescendants<Border>(checkBox)
            .FirstOrDefault(border =>
                Math.Abs(border.Width - 18) <= 0.1 &&
                Math.Abs(border.Height - 18) <= 0.1);
        var boxBrushKey = checkBox.IsChecked == true ? "ActiveBrush" : "CanvasBrush";
        var boxBorderBrushKey = checkBox.IsChecked == true ? "ActiveBrush" : "ControlBorderBrush";
        return
            Equals(checkBox.Foreground, checkBox.TryFindResource("BodyBrush")) &&
            UsesQuickAddResources(checkBox, "CanvasBrush", "ControlBorderBrush", "ActiveBrush") &&
            box is not null &&
            Equals(box.Background, checkBox.TryFindResource(boxBrushKey)) &&
            Equals(box.BorderBrush, checkBox.TryFindResource(boxBorderBrushKey));
    }

    private bool ScrollViewerUsesQuickAddTheme(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        return scrollViewer.Template is not null &&
            UsesQuickAddResources(scrollViewer, "ControlBorderBrush", "ActiveBrush");
    }

    private bool ScrollViewerScrollBarsUseQuickAddTheme(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        var scrollBars = new[] { "PART_VerticalScrollBar", "PART_HorizontalScrollBar" }
            .Select(name => scrollViewer.Template.FindName(name, scrollViewer) as ScrollBar)
            .Where(scrollBar => scrollBar is not null)
            .Cast<ScrollBar>()
            .ToArray();
        foreach (var scrollBar in scrollBars)
        {
            scrollBar.ApplyTemplate();
        }

        return scrollBars.Length == 2 && scrollBars.All(scrollBar =>
            scrollBar.Template is not null &&
            Equals(scrollBar.Foreground, scrollBar.TryFindResource("ControlBorderBrush")) &&
            UsesQuickAddResources(scrollBar, "ActiveBrush"));
    }

    private bool ControlUsesQuickAddSurface(Control control)
    {
        var surfaceKey = control is Button ? "CardAltBrush" : "CanvasBrush";
        var borderKey = control is System.Windows.Controls.Calendar ? "PeachLineBrush" : "ControlBorderBrush";
        return control.Style is not null &&
            control.Template is not null &&
            Equals(control.Background, control.TryFindResource(surfaceKey)) &&
            Equals(control.Foreground, control.TryFindResource("InkBrush")) &&
            Equals(control.BorderBrush, control.TryFindResource(borderKey)) &&
            UsesQuickAddResources(control, surfaceKey, "InkBrush", borderKey);
    }

    private bool BorderUsesQuickAddSurface(Border border, string borderResourceKey = "PeachLineBrush") =>
        Equals(border.Background, border.TryFindResource("CanvasBrush")) &&
        Equals(border.BorderBrush, border.TryFindResource(borderResourceKey)) &&
        UsesQuickAddResources(border, "CanvasBrush", borderResourceKey);

    private bool UsesQuickAddResources(FrameworkElement element, params string[] keys) =>
        keys.All(key =>
            window.Resources.Contains(key) &&
            ReferenceEquals(element.TryFindResource(key), window.Resources[key]));

    private static IEnumerable<T> QuickAddVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var descendant in QuickAddVisualDescendants<T>(
                         VisualTreeHelper.GetChild(root, index)))
            {
                yield return descendant;
            }
        }
    }
}

internal sealed record MainWindowValidationScenarios(
    NoteExerciseResult NoteExercise,
    ReminderModeClickResult ReminderModes,
    bool WorkspaceDraftsRetained,
    (bool TracksBeijingDay, bool PreservesChosenDate) NoteDateBehavior,
    bool CrossDayReminderBehavior,
    bool ReminderSummaryAdvances,
    MainUiPolishScenarios UiPolish,
    SettingsDraftRetentionProbe SettingsDrafts,
    UndatedNoteEditProbe UndatedNoteEdit,
    bool CountdownReminderModeMappingWorks,
    MainCountdownReadabilityScenarios CountdownReadability,
    CountdownEditingScenarios CountdownEditing,
    NoteValidationScenarios NoteValidation,
    MainWindowInteractionStates InteractionStates,
    MainStructureScenarios Structure,
    MainKeyboardImmediateMotionScenarios KeyboardImmediateMotion);

internal sealed record MainStructureScenarios(
    bool NoteGroupingSelectorAbsent,
    bool CountdownReminderModeRadiosPresent,
    bool NameEditorPresent,
    bool NoteReminderRulesEntryPresent,
    bool NoteReminderRulesEntryVisible,
    bool LegacyNoteReminderControlsAbsent,
    bool TagControlsPresent,
    bool StaticWorkspaceLayout,
    bool CountdownTabPresent,
    bool NotesTabPresent,
    bool TagMatchModeTogglesPresent,
    bool NoteDateFilterControlsPresent,
    bool NoteEntryControlsPresent,
    bool NoteDatePresent,
    bool PreferenceControlsPresent,
    bool CountdownWorkspaceIsCanvas);

internal sealed record NoteValidationScenarios(
    bool BothFieldsShowErrors,
    bool FirstInvalidFieldFocus,
    bool ErrorsClearOnCorrection,
    bool DatedEditRejectsClearedDate,
    bool ReminderDateFocusForTimeAndCustom)
{
    public static NoteValidationScenarios Failed { get; } = new(false, false, false, false, false);
}

// #151：倒计时编辑合同探针结果（字段错误、主次按钮、输入面、选区、状态条、单选圆点、排版 token）。
internal sealed record CountdownEditingScenarios(
    bool FieldErrorsAtSourceWithFocus,
    bool FieldErrorsClearOnCorrection,
    bool SingleErrorRowNotClipped,
    bool PreferenceErrorsBelowFields,
    bool PreferenceErrorsClearOnCorrection,
    bool PrimarySecondaryRolesDistinct,
    bool InputSurfacesWhitePaper,
    bool SelectionHighlightDistinct,
    bool StatusBarSurfaceErrorContrast,
    bool ReminderRadioSelectedContrast,
    bool EditorTypographyTokenized)
{
    public static CountdownEditingScenarios Failed { get; } = new(
        false, false, false, false, false, false, false, false, false, false, false);
}

// #152：主窗可操作性合同探针结果——常驻焦点环、唯一焦点描边、悬停区分、按压/禁用、
// 帮助聚焦说明、默认缩放后紧凑命中、筛选芯片与日历翻月紧凑几何。
internal sealed record MainWindowInteractionStates(
    bool FocusRingsReserveSpace,
    bool PrimaryAndPinSingleFocusStroke,
    bool NotesTabHoverDiffersFromSelected,
    bool LocalControlsPressAndDisabled,
    bool HelpFocusShowsHelpText,
    bool CompactHitTargetsMeetScaledFloor,
    bool ChipsAndCalendarNavCompact,
    bool HoverStateContrastIsPerceivable,
    bool PressedStateGeometryStable,
    bool MouseClickDoesNotLeaveFocusRing,
    bool KeyboardFocusRingShowsOnTab,
    bool SelectedTabWeightDominates,
    bool SegmentIndicatorGeometryMatches,
    bool SegmentIndicatorReducedMotionIsImmediate,
    bool SegmentIndicatorRapidSwitchStaysAligned)
{
    public static MainWindowInteractionStates Failed { get; } = new(
        false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
}

internal sealed record SegmentIndicatorScenarios(
    bool GeometryMatches,
    bool ReducedMotionIsImmediate,
    bool RapidSwitchStaysAligned);

// #242：七个子检查各在开启与关闭客户区动画两种模式下运行一次。14 个检查名钉在 RequiredChecks 里：
// 缺少的、未通过的和未登记的检查名都进 FailedChecks。
internal sealed record MainKeyboardImmediateMotionScenarios(IReadOnlyDictionary<string, bool> Checks)
{
    internal static readonly IReadOnlyList<string> RequiredChecks =
    [
        "AnimatedWorkspaceShortcutFirstFrame",
        "AnimatedWorkspaceClick",
        "AnimatedFilterShortcutFirstFrame",
        "AnimatedTagManagerShortcutFirstFrame",
        "AnimatedEscapeFromPopupFirstFrame",
        "AnimatedEscapeFromTriggerFirstFrame",
        "AnimatedFilterClick",
        "ReducedWorkspaceShortcutFirstFrame",
        "ReducedWorkspaceClick",
        "ReducedFilterShortcutFirstFrame",
        "ReducedTagManagerShortcutFirstFrame",
        "ReducedEscapeFromPopupFirstFrame",
        "ReducedEscapeFromTriggerFirstFrame",
        "ReducedFilterClick"
    ];

    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks =>
        RequiredChecks.Where(name => !Checks.TryGetValue(name, out var passed) || !passed)
            .Concat(Checks.Keys.Where(name => !RequiredChecks.Contains(name)))
            .ToArray();
}

// #148：倒计时列表可读性——徽章排他、到期文字对比、分格几何、表格数字格式（颗粒化子旗标便于定位）。
internal sealed record MainCountdownReadabilityScenarios(
    bool EncourageBadgeAbsent,
    bool ExpiredCardBackgroundsMatch,
    bool ExpiredFallbackTextsPresent,
    bool ExpiredPrimaryUsesNumberColor,
    bool ExpiredSecondaryUsesBodyColor,
    bool BlocksPairCountValid,
    bool BlocksVerticalRatioMatches,
    bool BlocksGapMatches,
    bool BlocksUnitOutsideAndColored,
    bool BlocksValueBrushAndFamily,
    bool NumericTextsMatchContract)
{
    public bool ExpiredRemainingFollowsContrast =>
        ExpiredCardBackgroundsMatch &&
        ExpiredFallbackTextsPresent &&
        ExpiredPrimaryUsesNumberColor &&
        ExpiredSecondaryUsesBodyColor;

    public bool BlocksGeometryMatches =>
        BlocksPairCountValid &&
        BlocksVerticalRatioMatches &&
        BlocksGapMatches &&
        BlocksUnitOutsideAndColored &&
        BlocksValueBrushAndFamily;

    public static MainCountdownReadabilityScenarios Failed { get; } = new(
        false, false, false, false, false, false, false, false, false, false, false);
}

internal sealed record MainCountdownBlocksProbeResult
{
    public bool PairCountValid { get; set; }
    public bool VerticalRatioMatches { get; set; } = true;
    public bool GapMatches { get; set; } = true;
    public bool UnitOutsideAndColored { get; set; } = true;
    public bool ValueBrushAndFamily { get; set; } = true;
}

internal sealed record SettingsDraftRetentionProbe(
    bool InputsRetainedOnRefresh,
    bool SavedValuesReflectedAfterSave,
    bool NegativeSalaryRejectedKeepsDraft,
    bool ZeroSalaryStillSavesAsUnset);

internal sealed record UndatedNoteEditProbe(
    bool NewNoteRejectsEmptyDate,
    bool UndatedEditSavesWithoutDate,
    bool DatedEditRejectsClearedDate,
    bool ValidationMessagesDistinguished);

internal sealed record NoteListValidationParts(
    ScrollViewer ListRegion,
    StackPanel ActiveRows,
    StackPanel CompletedRows,
    TextBox Search,
    ToggleButton CompletedSegment,
    FrameworkElement BatchActions,
    TextBlock SelectedCount);

// #250：共享表面圆角取样——Role 是 DESIGN.md §13.3 的角色（Section/Card/WindowShell/Control/Chip），
// Element 指明取样点；取不到 Border 时 Radius 为空，探针按违反项报出。
internal sealed record SurfaceRadiusSample(string Role, string Element, CornerRadius? Radius);

internal sealed record MainSurfaceEvidenceParts(
    Button SaveCountdown,
    Popup Preferences,
    Popup Filter,
    Popup TagManager,
    TagSelector CountdownTags);

internal sealed record FloatingWindowValidationScenarios(
    bool CrossDayReminderAdded,
    ReminderModeClickResult ReminderModes,
    FloatingDragInteractionExerciseResult DragInteraction,
    FloatingThemeExerciseResult ThemeExercise,
    FloatingManualPlacementExerciseResult ManualPlacement,
    FloatingUiPolishScenarios UiPolish,
    FloatingWorkspaceValidationScenarios Workspace,
    FloatingNoteSearchValidationScenarios NoteSearch,
    FloatingStableRefreshScenarios StableRefresh,
    FloatingBottomCommandScenarios BottomCommand,
    FloatingStructureScenarios Structure,
    FloatingKeyboardFocusScenarios KeyboardFocus);

internal sealed record FloatingKeyboardFocusScenarios(
    IReadOnlyList<string> CountdownsWorkspaceFailures,
    IReadOnlyList<string> NotesWorkspaceFailures,
    IReadOnlyList<string> OverflowMenuFailures,
    IReadOnlyList<string> TagSelectorFailures)
{
    public IReadOnlyList<string> Failures =>
    [
        .. CountdownsWorkspaceFailures.Select(failure => $"悬浮窗倒计时：{failure}"),
        .. NotesWorkspaceFailures.Select(failure => $"悬浮窗便签：{failure}"),
        .. OverflowMenuFailures.Select(failure => $"「…」菜单：{failure}"),
        .. TagSelectorFailures.Select(failure => $"标签选择器：{failure}")
    ];

    public bool AllPassed => Failures.Count == 0;
}

internal sealed record FloatingStructureScenarios(
    bool ShellLargeShadowRemoved,
    bool IncomeUsesTabularNumerals,
    bool ReminderRulesEntryPresent,
    bool ReminderRulesEntryVisible,
    bool ReminderRulesEntryCompact,
    bool LegacyReminderControlsAbsent,
    bool ManagerButtonPresent,
    bool NotesButtonPresent,
    bool NoteEntryControlsPresent,
    bool NoteDatePresent);

/// <summary>#212：渲染签名稳定刷新探针结果，语义见 DESIGN §10「渲染签名稳定刷新」。</summary>
internal sealed record FloatingStableRefreshScenarios(
    bool FocusStaysOnOverflowTriggerAcrossTicks,
    bool CountdownRowsPreservedAndTextUpdatesInPlace,
    bool CountdownSecondsSwitchWithoutAnimation,
    bool NoteCardInstancesPreservedAcrossTicks,
    bool RebuildRestoresFocusAndScrollByRole,
    bool HiddenSkipsRenderUntilVisible,
    bool MinimizedSkipsRenderUntilRestored)
{
    public bool AllPassed =>
        FocusStaysOnOverflowTriggerAcrossTicks &&
        CountdownRowsPreservedAndTextUpdatesInPlace &&
        CountdownSecondsSwitchWithoutAnimation &&
        NoteCardInstancesPreservedAcrossTicks &&
        RebuildRestoresFocusAndScrollByRole &&
        HiddenSkipsRenderUntilVisible &&
        MinimizedSkipsRenderUntilRestored;

    public static FloatingStableRefreshScenarios Failed { get; } = new(
        false, false, false, false, false, false, false);
}

internal sealed record FloatingBottomCommandScenarios(
    bool DefaultCollapsedInNotes,
    bool CountdownsHideCommandArea,
    bool ToggleExpandsWithFilterTags,
    bool ToggleCollapses,
    bool SubmitCollapsesAfterSuccess,
    bool ExpandedQuickAddScrollable,
    bool SettingsOpensManagerPreservingWorkspace,
    bool NoteMenuProvidesEdit,
    bool NoteMenuDeleteConfirms,
    bool PastelStampsRotate,
    bool CardSurfacesAlternate,
    bool HandwrittenDecorationLands,
    bool QuickAddPlaceholderMirrorsManager,
    bool QuickAddToggleSecondaryWhenExpanded,
    bool NoteMenuKeyboardLifecycle,
    bool SettingsGearConcentric,
    bool QuickAddReopensAtTop)
{
    public bool QuickAddTogglePass =>
        DefaultCollapsedInNotes &&
        ToggleExpandsWithFilterTags &&
        ToggleCollapses &&
        SubmitCollapsesAfterSuccess &&
        ExpandedQuickAddScrollable &&
        QuickAddToggleSecondaryWhenExpanded &&
        QuickAddReopensAtTop;

    public bool OverflowMenuPass =>
        NoteMenuProvidesEdit &&
        NoteMenuDeleteConfirms &&
        NoteMenuKeyboardLifecycle;

    public bool PastelIndexPass =>
        PastelStampsRotate &&
        CardSurfacesAlternate;

    public bool AllPassed =>
        DefaultCollapsedInNotes &&
        CountdownsHideCommandArea &&
        QuickAddTogglePass &&
        SettingsOpensManagerPreservingWorkspace &&
        OverflowMenuPass &&
        PastelIndexPass &&
        HandwrittenDecorationLands &&
        QuickAddPlaceholderMirrorsManager &&
        SettingsGearConcentric;
}

internal sealed record FloatingWorkspaceValidationScenarios(
    bool InitialStateMatches,
    bool SwitchingStaysSynchronized,
    bool CountdownSelectionSurvivesRefresh,
    bool NotesKeepExistingSortOrder,
    WindowValidationTextFit CountdownButtonTextFit,
    WindowValidationTextFit NotesButtonTextFit,
    WindowValidationTextFit TitleTextFit,
    WindowValidationTextFit TitleTextHeightFit,
    int CountdownCount,
    IReadOnlyList<string> CountdownTexts,
    string IncomeText,
    IReadOnlyList<string> RenderedNoteIds,
    bool IncomeHiddenWithoutSalary,
    bool IncomeRestoredWithSalary);

internal sealed record FloatingNoteSearchValidationScenarios(
    bool HiddenInCountdowns,
    bool VisibleInNotes,
    bool ExpandsAndFocuses,
    bool FiltersRenderedIds,
    bool CollapseKeepsTerm,
    bool HintReexpands,
    bool EscapeClears,
    bool EmptyState,
    bool EmptyDoesNotOverlap,
    bool ClearRestores,
    bool AddThenSearchTab,
    bool SearchBeforeFirstCard,
    bool CtrlFFromCountdowns,
    bool UsesQuickAddTheme,
    bool HintNamesDescribeAction,
    bool LongHintTermTrimsWithinSlot,
    bool NoteFilterEmptyStateDistinguished,
    bool CountdownFilterEmptyStateDistinguished,
    bool SearchButtonShowsClosedMagnifier,
    bool SearchRefreshLeavesPlacementUnchanged,
    bool ConstrainedEmptyProvidesAction)
{
    public bool AllPassed =>
        HiddenInCountdowns &&
        VisibleInNotes &&
        ExpandsAndFocuses &&
        FiltersRenderedIds &&
        CollapseKeepsTerm &&
        HintReexpands &&
        EscapeClears &&
        EmptyState &&
        EmptyDoesNotOverlap &&
        ClearRestores &&
        AddThenSearchTab &&
        SearchBeforeFirstCard &&
        CtrlFFromCountdowns &&
        UsesQuickAddTheme &&
        HintNamesDescribeAction &&
        LongHintTermTrimsWithinSlot &&
        NoteFilterEmptyStateDistinguished &&
        CountdownFilterEmptyStateDistinguished &&
        SearchButtonShowsClosedMagnifier &&
        SearchRefreshLeavesPlacementUnchanged &&
        ConstrainedEmptyProvidesAction;
}

internal sealed record WindowValidationTextFit(
    bool Fits,
    double Available,
    double Required);

internal sealed record MainUiPolishScenarios(
    bool NoteEditorFieldsDoNotOverlap,
    bool LegacyInlineReminderControlsStayHidden,
    bool NoteEditorLabelsVisible,
    bool MinimumSizeMatchesDefault,
    bool PopupKeyboardLifecycleWorks,
    bool PopupOutsideFocusPreserved,
    bool PopupTriggerSecondClickCloses,
    bool PopupEscapeFromTriggerClosesAndKeepsFocus,
    bool EmptyStatesDistinguished,
    bool NoResultsResetWorks,
    bool CountdownEmptyStatesDistinguished,
    bool CountdownEmptyPointerResetRestoresResults,
    bool CountdownEmptyKeyboardResetRestoresResults,
    bool CountdownRemainingLayoutMatches,
    IReadOnlyList<MainCountdownRemainingMeasurement> CountdownRemainingSamples,
    CountdownIncrementalRefreshProbe CountdownIncrementalRefresh,
    bool MainNoteCompletionStyleScoped,
    bool NoteSearchSyncsFromSharedFilter,
    MainNoteListPolishScenarios NoteListPolish,
    MainVisualChromeScenarios VisualChrome);

/// <summary>#153：便签列表排版 / 芯片 / 空态 / 筛选标签 / 键盘与间距 token 探针结果。</summary>
internal sealed record MainNoteListPolishScenarios(
    bool SpacedChineseDateEverywhere,
    bool BodyLineHeightSeparatesSummary,
    bool RowTitleFallsToBlockTitleLevel,
    bool TagChipSingleLineEllipsisWithFullText,
    bool EmptyStateTextAllowsWrap,
    bool FilterDateHasVisibleLabel,
    bool MainNoteRowActionsKeyboardWorks,
    bool CardSpacingConsumesTokens,
    bool FilterPopupTabCyclesInsideLayer,
    bool TagManagerPopupTabCyclesInsideLayer,
    bool TagSelectorPopupTabCyclesInsideLayer);

/// <summary>
///     #154：视觉铬探针结果——漏斗实心可识别、图标描边统一无重复搜索镜、矢量窗铬、
///     激活胶囊全圆角、倒计时页手写体装饰层化、DWM 原生圆角与系统阴影、页签在装饰带之上。
/// </summary>
internal sealed record MainVisualChromeScenarios(
    bool FilterFunnelIconSurvivesAliasedCapture,
    bool NotesSearchDuplicateMagnifierRemoved,
    bool MainWindowChromeButtonsUseVectorPaths,
    bool SegmentActivePillsUseFullRoundedCorners,
    bool CountdownHandwrittenDecoOutOfFormMeasurement,
    bool MainWindowSquareChromeNoTransparency,
    bool TabsRenderAboveDecorationBand,
    bool DecorationBandCharactersUnobstructed,
    bool MainCommandIconsUseVectorPaths);

internal sealed record CountdownIncrementalRefreshProbe(
    bool IdentityPreserved,
    bool SecondsSwitchWithoutAnimation,
    bool FocusPreserved,
    bool NonZeroScrollPreserved,
    bool PointerStatePreserved,
    bool CollectionChangeRebuilt,
    bool FilterChangeRebuilt,
    bool PrecisionChangeRebuilt,
    bool ExpiryChangeRebuilt,
    bool ExpiryBackgroundFlipped,
    bool ExpiryRemainingTextFlipped,
    double ScrollOffsetBefore = 0,
    double ScrollOffsetAfterTick = 0,
    double ScrollableHeightBefore = 0)
{
    public bool ExpiryVisualsFlipTogether =>
        ExpiryBackgroundFlipped && ExpiryRemainingTextFlipped;

    public bool TriggerMatrixRebuilt =>
        CollectionChangeRebuilt &&
        FilterChangeRebuilt &&
        PrecisionChangeRebuilt &&
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
    bool PreferenceSaved,
    bool IsPrimary,
    double ValueFontSize,
    double TitleFontSize,
    string RenderedBlockText,
    bool BlocksFullyVisible);

internal sealed record FloatingUiPolishScenarios(
    bool DatePickerKeyboardLifecycleWorks,
    bool FieldErrorsAppearAtSource,
    bool CorrectedFieldErrorsClearLiveMessage);

internal sealed record PopupFocusExerciseResult(
    bool EscapeReturnsFocus,
    bool OutsideFocusPreserved,
    bool TriggerSecondClickCloses,
    bool EscapeFromTriggerClosesAndKeepsFocus);

internal sealed record DatePickerPointerExerciseResult(
    bool TextBoxPresent,
    bool PressInjected,
    bool PressDidNotOpen,
    bool ReleaseInjected,
    bool ReleaseOpened,
    bool ReleaseHandled,
    bool ThemedPopupOpened);

internal sealed record ComboBoxPointerExerciseResult(
    bool TextSurfacePresent,
    bool PressInjected,
    bool PressDidNotOpen,
    bool ReleaseInjected,
    bool ReleaseOpened,
    bool ReleaseHandled,
    bool ArrowPressOpens,
    bool KeyboardF4Opens,
    bool KeyboardAltDownOpens,
    bool KeyboardArrowsKeepOpen,
    bool DisabledClickDoesNotOpen,
    bool PopupItemClickSelects,
    bool Attached,
    bool ArrowColumnUnchanged,
    bool ArrowIsNotTabStop,
    bool KeyboardEscapeCloses = false);

internal sealed record NoteExerciseResult(
    bool DateRequired,
    bool AddedViaClick,
    bool DateViaPicker,
    bool ReminderViaSelector,
    bool CompletionViaCheckbox);

internal sealed record ReminderModeClickResult(
    bool NoReminder,
    bool OnceReminder,
    bool RepeatingReminder)
{
    public static ReminderModeClickResult NotRun { get; } = new(false, false, false);
}

internal sealed record FloatingDragInteractionExerciseResult(
    bool CoversWindowBackground,
    bool ReceivesPointerGesture,
    bool RequestsNativeWindowDrag,
    bool SyntheticGestureDoesNotClaimPhysicalCompletion,
    bool DragAttemptClampsToWorkArea,
    bool InteractiveControlsDoNotRequestWindowDrag,
    bool ExcludesInteractiveControls);

internal sealed record FloatingManualPlacementExerciseResult(
    bool PersistsAndRemainsVisibleAfterRefresh);

internal sealed record FloatingThemeExerciseResult(
    IReadOnlyList<string> BoundaryModes,
    IReadOnlyList<string> PanelColors,
    IReadOnlyList<string> PlushGlowColors,
    IReadOnlyList<string> MascotPresentationModes,
    bool RasterMascotPresentationMatches,
    bool TextContrast,
    bool SurfacesFollowActiveTheme,
    bool PlushSceneFollowsActiveTheme)
{
    public bool PlushSceneFollowsDayTheme => PlushSceneFollowsActiveTheme;
}

internal sealed record FloatingQuickAddThemeProbeResult(
    IReadOnlyDictionary<string, bool> Checks,
    IReadOnlyList<FloatingQuickAddThemeProbeDiagnostic> Diagnostics)
{
    public IReadOnlyList<string> FailedChecks => FloatingWindowValidationScenariosRunner.RequiredQuickAddThemeChecks
        .Where(name => !Checks.TryGetValue(name, out var passed) || !passed)
        .ToArray();

    public bool Passed => FailedChecks.Count == 0;
}

internal sealed record FloatingQuickAddThemeProbeDiagnostic(
    string Check,
    string Control,
    string ResourceKey,
    string ExpectedResource,
    string ActualResource,
    bool ResourceReferenceMatches,
    bool ElementDeclaresResource,
    bool UsesFloatingBodyScrollerScope,
    bool PopupIsOpen,
    bool TemplatePresent,
    string TemplateParts,
    string ElementState);

internal sealed record FloatingQuickAddPaletteBindingResult(
    bool InputSurfaceMatches,
    bool InputTextMatches,
    bool ErrorTextMatches)
{
    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(InputSurfaceMatches), InputSurfaceMatches),
        (nameof(InputTextMatches), InputTextMatches),
        (nameof(ErrorTextMatches), ErrorTextMatches)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();

    public bool Passed => FailedChecks.Count == 0;
}

internal sealed record FloatingQuickAddPaletteMapping(
    string InputSurfaceResourceKey,
    string InputTextResourceKey,
    string ErrorTextResourceKey,
    string ReminderTextResourceKey,
    string ReminderErrorResourceKey);

internal sealed record FloatingQuickAddPaletteBindingValidation(
    string Theme,
    FloatingQuickAddPaletteMapping Expected,
    FloatingQuickAddPaletteMapping Actual,
    FloatingQuickAddPaletteBindingResult Gates);

internal static class FloatingQuickAddPaletteBindings
{
    private const string DayInputSurface = "FloatingDayNoteInputBrush";
    private const string DayInputText = "FloatingDayNoteInputTextBrush";
    private const string DayError = "FloatingDayErrorBrush";
    internal static IReadOnlyList<string> KnownResourceKeys { get; } =
    [
        DayInputSurface,
        DayInputText,
        DayError
    ];

    internal static FloatingQuickAddPaletteMapping Expected() =>
        new(DayInputSurface, DayInputText, DayError, DayInputText, DayError);

    internal static FloatingQuickAddPaletteBindingResult Evaluate(
        FloatingQuickAddPaletteMapping actual)
    {
        var expected = Expected();
        return new FloatingQuickAddPaletteBindingResult(
            string.Equals(
                actual.InputSurfaceResourceKey,
                expected.InputSurfaceResourceKey,
                StringComparison.Ordinal),
            string.Equals(
                actual.InputTextResourceKey,
                expected.InputTextResourceKey,
                StringComparison.Ordinal) &&
            string.Equals(
                actual.ReminderTextResourceKey,
                expected.ReminderTextResourceKey,
                StringComparison.Ordinal),
            string.Equals(
                actual.ErrorTextResourceKey,
                expected.ErrorTextResourceKey,
                StringComparison.Ordinal) &&
            string.Equals(
                actual.ReminderErrorResourceKey,
                expected.ReminderErrorResourceKey,
                StringComparison.Ordinal));
    }
}
