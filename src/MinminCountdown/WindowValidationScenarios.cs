using System.Diagnostics;
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
            ProbeUiPolishScenarios(),
            ProbeSettingsDraftRetention(),
            ProbeUndatedNoteEditRetention(),
            ProbeCountdownReminderModeRadioMapping(),
            ProbeCountdownReadabilityScenarios(),
            ProbeCountdownEditingScenarios(),
            ProbeNoteValidationScenarios(),
            ProbeMainWindowInteractionStates());
    }

    // #185/#186/#188：便签字段错误和倒计时提醒日期前置条件必须回到来源控件，
    // 并复用同一字段错误/礼貌直播语义；无效提交不应清掉草稿或打开规则编辑器。
    internal NoteValidationScenarios ProbeNoteValidationScenarios()
    {
        var originalWorkspace = _workspace;
        var originalText = NoteText.Text;
        var originalDate = NoteDate.SelectedDate;
        var originalEditingId = _editingNoteId;
        var originalDateDirty = _noteDateDirty;
        var originalTagIds = NoteTags.SelectedTagIds.ToArray();
        var originalReminderRules = _noteReminderRules;
        var originalSaveContent = SaveNoteButton.Content;
        var originalCancelVisibility = CancelNoteEdit.Visibility;
        string? temporaryNoteId = null;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            Activate();
            WindowValidationInput.Pump(Dispatcher);
            ResetNoteEditor();
            NoteDate.SelectedDate = null;
            NoteText.Text = string.Empty;
            SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var bothFields =
                NoteTextError.Visibility == Visibility.Visible &&
                NoteDateError.Visibility == Visibility.Visible &&
                EditingValidationErrorIsAccessible(NoteTextError, NoteText) &&
                EditingValidationErrorIsAccessible(NoteDateError, NoteDate);
            var firstInvalidFieldFocus = NoteText.IsKeyboardFocusWithin;

            NoteText.Text = "便签字段探针";
            WindowValidationInput.Pump(Dispatcher);
            var dateError =
                NoteTextError.Visibility == Visibility.Collapsed &&
                NoteDateError.Visibility == Visibility.Visible;
            NoteDate.SelectedDate = _desktopWorkspace.Snapshot.BeijingNow.Date;
            WindowValidationInput.Pump(Dispatcher);
            var correctionClears =
                dateError &&
                NoteTextError.Visibility == Visibility.Collapsed &&
                NoteDateError.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(NoteMessage.Text);

            NoteText.Text = "便签日期探针";
            NoteDate.SelectedDate = _desktopWorkspace.Snapshot.BeijingNow.Date;
            var reminderDue = _desktopWorkspace.Snapshot.BeijingNow.AddDays(1).ToUniversalTime();
            _noteReminderRules = Array.AsReadOnly(new ReminderRule[]
            {
                new AbsoluteOnceReminderRule(Guid.NewGuid().ToString("N"), reminderDue)
            });
            UpdateNoteReminderRulesSummary();
            SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var saved = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
                note => note.Text == "便签日期探针");
            temporaryNoteId = saved?.Id;
            var datedEditRejectsClear = false;
            if (saved is not null)
            {
                LoadNoteIntoEditor(saved);
                var rulesBeforeReject = _noteReminderRules.Count;
                NoteDate.SelectedDate = null;
                SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
                datedEditRejectsClear =
                    NoteDateError.Visibility == Visibility.Visible &&
                    NoteDate.IsKeyboardFocusWithin &&
                    NoteText.Text == "便签日期探针" &&
                    _noteReminderRules.Count == rulesBeforeReject;
            }

            ShowWorkspace(WorkspaceKind.Countdowns);
            Activate();
            WindowValidationInput.Pump(Dispatcher);
            ResetCountdownEditor();
            CountdownName.Text = "提醒日期草稿";
            CountdownDate.SelectedDate = null;
            var editorsBeforeMissingDate = Application.Current.Windows
                .OfType<ReminderRulesEditorWindow>()
                .Count();
            CountdownReminderTimeRadio.IsChecked = true;
            ApplyCountdownReminderModeForValidation(CountdownReminderMode.AtDeadline);
            WindowValidationInput.Pump(Dispatcher);
            var timeFocusesDate = CountdownDate.IsKeyboardFocusWithin;
            CountdownReminderCustomRadio.IsChecked = true;
            ApplyCountdownReminderModeForValidation(CountdownReminderMode.Custom);
            WindowValidationInput.Pump(Dispatcher);
            var customFocusesDate =
                CountdownDate.IsKeyboardFocusWithin &&
                CountdownName.Text == "提醒日期草稿" &&
                CountdownReminderNoRadio.IsChecked == true &&
                _countdownReminderRules.Count == 0 &&
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
                _desktopWorkspace.Apply(new DeleteNoteChange(temporaryNoteId));
            }

            _editingNoteId = originalEditingId;
            NoteText.Text = originalText;
            _settingDefaultDate = true;
            NoteDate.SelectedDate = originalDate;
            _settingDefaultDate = false;
            _noteDateDirty = originalDateDirty;
            NoteTags.SetSelection(originalTagIds);
            _noteReminderRules = originalReminderRules;
            UpdateNoteReminderRulesSummary();
            SaveNoteButton.Content = originalSaveContent;
            CancelNoteEdit.Visibility = originalCancelVisibility;
            ClearNoteFieldErrors();
            UiAccessibility.Announce(NoteMessage, string.Empty);
            ResetCountdownEditor();
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private bool ProbeCountdownReminderModeRadioMapping()
    {
        var createdCountdownIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(Dispatcher);
            ResetCountdownEditor();
            WindowValidationInput.Pump(Dispatcher);

            var addModeDefaultsToNone =
                CountdownReminderNoRadio.IsChecked == true &&
                CountdownReminderTimeRadio.IsChecked != true &&
                CountdownReminderCustomRadio.IsChecked != true &&
                _countdownReminderRules.Count == 0;

            CountdownReminderTimeRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var atDeadlineTarget = GetCountdownTargetUtc();
            var timeModeCreatesAtDeadlineOnce =
                atDeadlineTarget is not null &&
                CountdownReminderTimeRadio.IsChecked == true &&
                CountdownReminderNoRadio.IsChecked != true &&
                DeriveCountdownReminderMode(_countdownReminderRules, atDeadlineTarget) ==
                    CountdownReminderMode.AtDeadline;

            CountdownSecond.SelectedIndex = (CountdownSecond.SelectedIndex + 7) % 60;
            WindowValidationInput.Pump(Dispatcher);
            var shiftedTarget = GetCountdownTargetUtc();
            var targetShiftKeepsAtDeadlineOnce =
                shiftedTarget is not null &&
                atDeadlineTarget is not null &&
                shiftedTarget != atDeadlineTarget &&
                DeriveCountdownReminderMode(_countdownReminderRules, shiftedTarget) ==
                    CountdownReminderMode.AtDeadline;

            CountdownReminderNoRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var noneModeClearsRules =
                CountdownReminderNoRadio.IsChecked == true &&
                CountdownReminderTimeRadio.IsChecked != true &&
                _countdownReminderRules.Count == 0;

            var noReminderName = UniqueProbeName("单选探针-不提醒");
            CountdownName.Text = noReminderName;
            SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            TrackProbeCountdown(createdCountdownIds, noReminderName);
            var noReminderPersists = SnapshotCountdown(noReminderName)?.ReminderRules.Count == 0;

            ResetCountdownEditor();
            WindowValidationInput.Pump(Dispatcher);
            CountdownReminderTimeRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var savedAtDeadlineTarget = GetCountdownTargetUtc();
            var atDeadlineName = UniqueProbeName("单选探针-到点");
            CountdownName.Text = atDeadlineName;
            SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            TrackProbeCountdown(createdCountdownIds, atDeadlineName);
            var atDeadlineRecord = SnapshotCountdown(atDeadlineName);
            var atDeadlinePersists =
                savedAtDeadlineTarget is not null &&
                atDeadlineRecord?.ReminderRules is [AbsoluteOnceReminderRule persistedOnce] &&
                persistedOnce.ScheduledUtc == savedAtDeadlineTarget.Value;

            ResetCountdownEditor();
            WindowValidationInput.Pump(Dispatcher);
            var customEditorOpenedAndCancelled = false;
            var customEditorTimedOut = false;
            var customEditorTimeout = new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            customEditorTimeout.Tick += (_, _) =>
            {
                customEditorTimeout.Stop();
                customEditorTimedOut = true;
                foreach (var editor in OwnedWindows.OfType<ReminderRulesEditorWindow>().ToArray())
                {
                    editor.Close();
                }
            };
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    var editors = Application.Current.Windows
                        .OfType<ReminderRulesEditorWindow>()
                        .Where(window => ReferenceEquals(window.Owner, this))
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
                CountdownReminderCustomRadio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            finally
            {
                customEditorTimeout.Stop();
            }
            WindowValidationInput.Pump(Dispatcher);
            var customCancelRestoresDerivedMode =
                customEditorOpenedAndCancelled &&
                !customEditorTimedOut &&
                CountdownReminderCustomRadio.IsChecked != true &&
                CountdownReminderNoRadio.IsChecked == true &&
                _countdownReminderRules.Count == 0;

            var beijingNow = _desktopWorkspace.Snapshot.BeijingNow;
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
            var noneRecordSaved = _desktopWorkspace.Apply(new SaveCountdownChange(
                null,
                noneRecordName,
                editTargetUtc,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var atTimeRecordSaved = _desktopWorkspace.Apply(new SaveCountdownChange(
                null,
                atTimeRecordName,
                editTargetUtc,
                Array.Empty<string>(),
                new ReminderRule[]
                {
                    new AbsoluteOnceReminderRule(Guid.NewGuid().ToString("N"), editTargetUtc)
                }));
            var customRecordSaved = _desktopWorkspace.Apply(new SaveCountdownChange(
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

            BeginEditCountdown(noneRecord);
            WindowValidationInput.Pump(Dispatcher);
            var editNoRulesRestoresNone =
                CountdownReminderNoRadio.IsChecked == true &&
                CountdownReminderTimeRadio.IsChecked != true &&
                CountdownReminderCustomRadio.IsChecked != true &&
                _countdownReminderRules.Count == 0;

            BeginEditCountdown(atTimeRecord);
            WindowValidationInput.Pump(Dispatcher);
            var editAtDeadlineOnceRestoresAtTime =
                CountdownReminderTimeRadio.IsChecked == true &&
                CountdownReminderNoRadio.IsChecked != true &&
                CountdownReminderCustomRadio.IsChecked != true &&
                DeriveCountdownReminderMode(_countdownReminderRules, GetCountdownTargetUtc()) ==
                    CountdownReminderMode.AtDeadline;

            BeginEditCountdown(customRecord);
            WindowValidationInput.Pump(Dispatcher);
            var editOtherRulesRestoreCustom =
                CountdownReminderCustomRadio.IsChecked == true &&
                CountdownReminderNoRadio.IsChecked != true &&
                CountdownReminderTimeRadio.IsChecked != true;

            ResetCountdownEditor();
            WindowValidationInput.Pump(Dispatcher);

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
                _desktopWorkspace.Apply(new DeleteCountdownChange(id));
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
        _desktopWorkspace.Snapshot.Settings.Countdowns.LastOrDefault(record =>
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
        var originalWorkspace = _workspace;
        var temporaryNoteIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher);

            // 新建路径：空日期仍被拒绝且草稿保留
            ResetNoteEditor();
            WindowValidationInput.Pump(Dispatcher);
            NoteText.Text = "无日期新建拒绝探针";
            _settingDefaultDate = true;
            NoteDate.SelectedDate = null;
            _settingDefaultDate = false;
            _noteDateDirty = true;
            var noteCountBeforeNewReject = _desktopWorkspace.Snapshot.Settings.Notes.Count;
            SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var newPathMessage = NoteMessage.Text;
            var newNoteRejectsEmptyDate =
                _desktopWorkspace.Snapshot.Settings.Notes.Count == noteCountBeforeNewReject &&
                newPathMessage.Contains("请选择便签日期", StringComparison.Ordinal) &&
                string.Equals(NoteText.Text, "无日期新建拒绝探针", StringComparison.Ordinal);

            // 编辑既存无日期便签：日期留空可保存，保存后仍保持未设置日期
            var undatedText = $"无日期编辑探针-{Guid.NewGuid():N}"[..24];
            var addUndated = _desktopWorkspace.Apply(new SaveNoteChange(
                null,
                undatedText,
                null,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var undatedNote = _desktopWorkspace.Snapshot.Settings.Notes
                .FirstOrDefault(note => string.Equals(note.Text, undatedText, StringComparison.Ordinal));
            if (undatedNote is not null)
            {
                temporaryNoteIds.Add(undatedNote.Id);
            }

            var undatedEditSavesWithoutDate = false;
            if (addUndated.Succeeded && undatedNote is not null)
            {
                LoadNoteIntoEditor(undatedNote);
                WindowValidationInput.Pump(Dispatcher);
                NoteText.Text = $"{undatedText}-已编辑";
                SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
                var savedUndated = _desktopWorkspace.Snapshot.Settings.Notes
                    .FirstOrDefault(note => note.Id == undatedNote.Id);
                undatedEditSavesWithoutDate =
                    savedUndated is not null &&
                    savedUndated.Date is null &&
                    string.Equals(savedUndated.Text, $"{undatedText}-已编辑", StringComparison.Ordinal) &&
                    string.Equals(StatusText.Text, "便签已保存。", StringComparison.Ordinal);
            }

            // 编辑既有日期便签：清空日期被拒绝，草稿保留，文案与新建路径可区分
            var datedText = $"有日期编辑探针-{Guid.NewGuid():N}"[..24];
            var addDated = _desktopWorkspace.Apply(new SaveNoteChange(
                null,
                datedText,
                _desktopWorkspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var datedNote = _desktopWorkspace.Snapshot.Settings.Notes
                .FirstOrDefault(note => string.Equals(note.Text, datedText, StringComparison.Ordinal));
            if (datedNote is not null)
            {
                temporaryNoteIds.Add(datedNote.Id);
            }

            var datedEditRejectsClearedDate = false;
            var editPathMessage = string.Empty;
            if (addDated.Succeeded && datedNote is not null)
            {
                LoadNoteIntoEditor(datedNote);
                WindowValidationInput.Pump(Dispatcher);
                NoteText.Text = $"{datedText}-已编辑";
                _settingDefaultDate = true;
                NoteDate.SelectedDate = null;
                _settingDefaultDate = false;
                _noteDateDirty = true;
                SaveNoteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
                editPathMessage = NoteMessage.Text;
                var unchangedDated = _desktopWorkspace.Snapshot.Settings.Notes
                    .FirstOrDefault(note => note.Id == datedNote.Id);
                datedEditRejectsClearedDate =
                    unchangedDated is not null &&
                    string.Equals(unchangedDated.Text, datedText, StringComparison.Ordinal) &&
                    string.Equals(unchangedDated.Date, datedNote.Date, StringComparison.Ordinal) &&
                    editPathMessage.Contains("不能清空日期", StringComparison.Ordinal) &&
                    string.Equals(NoteText.Text, $"{datedText}-已编辑", StringComparison.Ordinal) &&
                    string.Equals(_editingNoteId, datedNote.Id, StringComparison.Ordinal);
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
                _desktopWorkspace.Apply(new DeleteNoteChange(temporaryNoteId));
            }

            ResetNoteEditor();
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private SettingsDraftRetentionProbe ProbeSettingsDraftRetention()
    {
        var originalWorkspace = _workspace;
        var originalPreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
        var temporaryNoteIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(Dispatcher);

            var draftPrecisionIndex = string.Equals(
                originalPreferences.DisplayPrecision,
                "Second",
                StringComparison.Ordinal)
                ? 2
                : 3;
            const string draftSalary = "98765";
            const string draftWorkStart = "07:30";
            const string draftWorkEnd = "19:45";
            DisplayPrecision.SelectedIndex = draftPrecisionIndex;
            MonthlySalary.Text = draftSalary;
            WorkStart.Text = draftWorkStart;
            WorkEnd.Text = draftWorkEnd;

            // 后台刷新来源一：筛选切换
            var filter = _desktopWorkspace.Snapshot.Filter;
            _desktopWorkspace.ApplyFilter(filter with { NoteSearch = "settings-draft-probe" });
            WindowValidationInput.Pump(Dispatcher);
            _desktopWorkspace.ApplyFilter(filter);
            WindowValidationInput.Pump(Dispatcher);

            // 后台刷新来源二：快速新增便签写盘（与悬浮窗快速新增共用同一工作区变更）
            var probeNoteText = $"设置草稿探针便签-{Guid.NewGuid():N}"[..24];
            var addResult = _desktopWorkspace.Apply(new SaveNoteChange(
                null,
                probeNoteText,
                _desktopWorkspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            WindowValidationInput.Pump(Dispatcher);
            var probeNote = _desktopWorkspace.Snapshot.Settings.Notes.FirstOrDefault(note =>
                string.Equals(note.Text, probeNoteText, StringComparison.Ordinal));
            if (probeNote is not null)
            {
                temporaryNoteIds.Add(probeNote.Id);
            }

            // 后台刷新来源三：便签勾选完成写盘
            if (probeNote is not null)
            {
                _desktopWorkspace.Apply(new SetNoteCompletionChange(probeNote.Id, true));
                WindowValidationInput.Pump(Dispatcher);
            }

            var inputsRetainedOnRefresh =
                DisplayPrecision.SelectedIndex == draftPrecisionIndex &&
                string.Equals(MonthlySalary.Text, draftSalary, StringComparison.Ordinal) &&
                string.Equals(WorkStart.Text, draftWorkStart, StringComparison.Ordinal) &&
                string.Equals(WorkEnd.Text, draftWorkEnd, StringComparison.Ordinal);

            SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var savedPreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
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
                DisplayPrecision.SelectedIndex == savedPrecisionIndex &&
                // #148：月薪输入显示与「今日已赚」同一套本地数字格式（N2）。
                string.Equals(
                    MonthlySalary.Text,
                    savedPreferences.MonthlySalary.ToString("N2", CultureInfo.CurrentCulture),
                    StringComparison.Ordinal) &&
                string.Equals(
                    WorkStart.Text,
                    CountdownFormatting.FormatWorkTime(savedPreferences.WorkStart),
                    StringComparison.Ordinal) &&
                string.Equals(
                    WorkEnd.Text,
                    CountdownFormatting.FormatWorkTime(savedPreferences.WorkEnd),
                    StringComparison.Ordinal);

            MonthlySalary.Text = "-500";
            SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var negativeSalaryRejectedKeepsDraft =
                _desktopWorkspace.Snapshot.Settings.Preferences.MonthlySalary == 98765m &&
                string.Equals(MonthlySalary.Text, "-500", StringComparison.Ordinal) &&
                string.Equals(StatusText.Text, "月薪不能小于 0。", StringComparison.Ordinal);

            MonthlySalary.Text = "0";
            SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var zeroSalaryStillSavesAsUnset =
                _desktopWorkspace.Snapshot.Settings.Preferences.MonthlySalary == 0m &&
                string.Equals(StatusText.Text, "偏好已保存。", StringComparison.Ordinal);

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
                _desktopWorkspace.Apply(new DeleteNoteChange(temporaryNoteId));
            }

            var currentPreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
            if (currentPreferences != originalPreferences)
            {
                _desktopWorkspace.Apply(new SavePreferencesChange(
                    originalPreferences.DisplayPrecision,
                    originalPreferences.MonthlySalary,
                    originalPreferences.WorkStart,
                    originalPreferences.WorkEnd));
            }

            _settingsDraftDirty = false;
            LoadPreferences();
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private MainUiPolishScenarios ProbeUiPolishScenarios()
    {
        var originalWorkspace = _workspace;
        var originalSearch = NoteSearch.Text;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);

            var manageBounds = ValidationBounds(ManageNoteReminderRulesButton);
            var summaryBounds = ValidationBounds(NoteReminderRulesSummary);
            var messageBounds = ValidationBounds(NoteMessage);
            var editorFieldsDoNotOverlap =
                ManageNoteReminderRulesButton.IsVisible &&
                NoteReminderRulesSummary.IsVisible &&
                !manageBounds.IntersectsWith(summaryBounds) &&
                !manageBounds.IntersectsWith(messageBounds) &&
                !summaryBounds.IntersectsWith(messageBounds);
            var legacyInlineReminderControlsStayHidden =
                ProbeLegacyInlineReminderControlsStayHidden();
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
            var noteListPolish = ProbeNoteListPolishScenarios();
            var noteCompletionStyleScoped = MainNoteCompletionStyleIsScoped;
            var originalSearchForSync = NoteSearch.Text;
            _desktopWorkspace.ApplyFilter(originalFilter with { NoteSearch = "sync-from-outside" });
            WindowValidationInput.Pump(Dispatcher);
            var searchFilledFromFilter = string.Equals(
                NoteSearch.Text,
                "sync-from-outside",
                StringComparison.Ordinal);
            _desktopWorkspace.ApplyFilter(originalFilter with { NoteSearch = string.Empty });
            WindowValidationInput.Pump(Dispatcher);
            var searchClearedFromFilter = string.IsNullOrEmpty(NoteSearch.Text);
            NoteSearch.Text = originalSearchForSync;
            _desktopWorkspace.ApplyFilter(originalFilter);
            var noteSearchSyncsFromSharedFilter = searchFilledFromFilter && searchClearedFromFilter;
            // #154：视觉铬探针（漏斗/图标描边/矢量窗铬/胶囊圆角/装饰层/方角铬/页签次序）。
            var visualChrome = ProbeVisualChromeScenarios();

            return new MainUiPolishScenarios(
                editorFieldsDoNotOverlap,
                legacyInlineReminderControlsStayHidden,
                editorLabelsVisible,
                // #211：1165×1350 改为首选尺寸（MainWindow.PreferredWidth/Height），
                // 最小尺寸放宽为 720×560，窗口随工作区收缩时业务内容滚动而非缩放。
                // Native pixel rounding can report Width=1164.8 at 125% DPI
                // while WPF still lays out the required DIP surface.
                ActualWidth >= MinWidth &&
                ActualHeight >= MinHeight &&
                Math.Abs(MinWidth - 720) <= 0.1 &&
                Math.Abs(MinHeight - 560) <= 0.1 &&
                Math.Abs(MainWindow.PreferredWidth - 1165) <= 0.1 &&
                Math.Abs(MainWindow.PreferredHeight - 1350) <= 0.1,
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
            FilterPopup.IsOpen = false;
            TagManagerPopup.IsOpen = false;
            NoteTags.IsPopupOpen = false;
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

    /// <summary>
    ///     #153：便签列表排版 / 芯片 / 空态 / 筛选日期标签 / 溢出菜单键盘 / 弹层 Tab 循环 / 间距 token 的
    ///     运行态探针。自建带长标签的探针便签（结束时删除便签与标签），不触碰帧捕获（帧捕获使用独立场景窗口）。
    /// </summary>
    private MainNoteListPolishScenarios ProbeNoteListPolishScenarios()
    {
        var entryWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var originalNoteDateFilter = NoteDateFilter.SelectedDate;
        var createdTagId = string.Empty;
        var createdNoteId = string.Empty;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);
            // 前置场景可能把已完成段留在选中态（ActiveNoteRows 折叠后可视树无行），先回到未完成段。
            SelectNotesSegment(false);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);
            // picker 文本用窗口内真实 NoteDateFilter（无父级控件解析不到 App 隐式模板）。
            // AC1：便签日期与倒计时截止同一中文日期格式——picker 模板选中日期文本与组头标签都留空格。
            var groupLabel = FormatNoteDateLabel("2032-05-18", new DateTimeOffset(2032, 5, 18, 10, 15, 30, TimeSpan.FromHours(8)).ToUniversalTime());
            var groupLabelSpaced =
                groupLabel.Contains("5 月 18 日", StringComparison.Ordinal) &&
                !groupLabel.Contains("5月18日", StringComparison.Ordinal);

            // AC5：筛选弹层日期有可见标签，不是无名空下拉。
            FilterPopup.IsOpen = true;
            WindowValidationInput.Pump(Dispatcher);
            var filterDateHasVisibleLabel =
                NoteDateFilterLabel.IsVisible &&
                !string.IsNullOrWhiteSpace(NoteDateFilterLabel.Text);

            NoteDateFilter.SelectedDate = new DateTime(2032, 5, 18);
            NoteDateFilter.ApplyTemplate();
            WindowValidationInput.Pump(Dispatcher);
            var noteDateFilterTextBox = NoteDateFilter.Template.FindName(
                "PART_TextBox",
                NoteDateFilter) as System.Windows.Controls.Primitives.DatePickerTextBox;
            var pickerTextSpaced = string.Equals(
                noteDateFilterTextBox?.Text,
                "2032 年 5 月 18 日",
                StringComparison.Ordinal);
            // 立即还原：NoteDateFilter 驱动共享日期筛选，残留值会把后续行渲染全部过滤掉。
            NoteDateFilter.SelectedDate = originalNoteDateFilter;
            FilterPopup.IsOpen = false;
            WindowValidationInput.Pump(Dispatcher);
            var spacedChineseDate = pickerTextSpaced && groupLabelSpaced;

            // AC4：倒计时与便签空态文案都允许换行。
            var emptyStateTextAllowsWrap =
                EmptyNotesMessage.TextWrapping == TextWrapping.Wrap &&
                EmptyCountdownMessage.TextWrapping == TextWrapping.Wrap;

            // 自建长标签探针便签：AC2 正文行距、AC3 芯片契约、AC8 间距 token 都在真实卡上度量。
            // 标签名 26 字符（5 + 17 + 4 位随机后缀），超过芯片 MaxWidth 的可容纳字数以触发省略。
            var longTagName = $"长标签探针{new string('长', 17)}{Random.Shared.Next(1000, 9999)}";
            var createTagResult = _desktopWorkspace.Apply(new CreateTagChange(longTagName, null));
            var probeTag = createTagResult.CreatedTag;
            if (probeTag is not null)
            {
                createdTagId = probeTag.Id;
                var saved = _desktopWorkspace.Apply(new SaveNoteChange(
                    null,
                    $"便签排版探针-{Guid.NewGuid():N}",
                    _desktopWorkspace.Snapshot.BeijingNow.Date,
                    false,
                    new[] { probeTag.Id },
                    NoReminder.Instance));
                if (saved.Succeeded)
                {
                    createdNoteId = _desktopWorkspace.Snapshot.Settings.Notes.Single(note =>
                        note.TagIds.Contains(probeTag.Id, StringComparer.OrdinalIgnoreCase) &&
                        note.Text.StartsWith("便签排版探针-", StringComparison.Ordinal)).Id;
                }
            }

            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);

            var bodyLineHeightSeparatesSummary = false;
            var tagChipSingleLine = false;
            var cardSpacingConsumesTokens = false;
            if (_noteSourceRows.TryGetValue(createdNoteId, out var probeRow) && probeTag is not null)
            {
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
                    Math.Abs(chip.MaxWidth - TagChipMaxWidth) <= 0.01 &&
                    chip.ActualWidth <= TagChipMaxWidth + 0.01 &&
                    chipText is not null &&
                    chipText.TextWrapping == TextWrapping.NoWrap &&
                    chipText.TextTrimming == TextTrimming.CharacterEllipsis &&
                    string.Equals(chip.ToolTip as string, probeTag.Name, StringComparison.Ordinal);

                var spacingCardPadding = (double)FindResource("SpacingCardPadding");
                var spacingUnit = (double)FindResource("SpacingUnit");
                var spacingBlockGap = (double)FindResource("SpacingBlockGap");
                var dateSection = MainValidationDescendants<Grid>(ActiveNoteRows)
                    .FirstOrDefault(grid =>
                        string.Equals(grid.Uid, "NoteDateTimelineSection", StringComparison.Ordinal));
                cardSpacingConsumesTokens =
                    Math.Abs(((Border)probeRow).Padding.Left - spacingCardPadding) <= 0.01 &&
                    Math.Abs(((Border)probeRow).Margin.Bottom - spacingUnit) <= 0.01 &&
                    dateSection is not null &&
                    Math.Abs(dateSection.Margin.Bottom - spacingBlockGap) <= 0.01;
            }

            // AC2：列表条目标题落 H2 阶梯，与 H1 区块标题保持明确层级。
            // 空列表没有条目标题可度量，视为满足（空洞真）；有行而缺标题或字号不符才判失败。
            ShowWorkspace(WorkspaceKind.Countdowns);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);
            var rowTitleTypeH2 = (double)FindResource("TypeH2FontSize");
            var rowTitles = MainValidationDescendants<TextBlock>(CountdownRows)
                .Where(text => string.Equals(text.Uid, "CountdownRowTitleText", StringComparison.Ordinal))
                .ToArray();
            var rowTitleFallsToBlockTitleLevel =
                CountdownRows.Children.Count == 0 ||
                (rowTitles.Length > 0 &&
                 rowTitles.All(text => Math.Abs(text.FontSize - rowTitleTypeH2) <= 0.01));

            // AC6：管理窗便签 `…` 打开后键盘进入「编辑 / 删除」、层内 Tab 循环、Esc 关闭归还触发器。
            var overflowMenuKeyboard = ProbeMainWindowNoteRowOverflowMenuKeyboard();

            // AC7：筛选 / 标签管理 / 标签选择器弹层 Tab 层内循环。
            ShowWorkspace(WorkspaceKind.Notes);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);
            var filterCycles = ProbePopupTabCyclesInsideLayer(FilterPopup, AnyTagMode);
            var tagManagerCycles = ProbePopupTabCyclesInsideLayer(TagManagerPopup, NewTagInput);
            var tagSelectorCycles = ProbePopupTabCyclesInsideLayer(
                NoteTags.Popup,
                NoteTags.PopupInitialFocus);

            return new MainNoteListPolishScenarios(
                spacedChineseDate,
                bodyLineHeightSeparatesSummary,
                rowTitleFallsToBlockTitleLevel,
                tagChipSingleLine,
                emptyStateTextAllowsWrap,
                filterDateHasVisibleLabel,
                overflowMenuKeyboard,
                cardSpacingConsumesTokens,
                filterCycles,
                tagManagerCycles,
                tagSelectorCycles,
                overflowMenuKeyboard);
        }
        finally
        {
            if (!string.IsNullOrEmpty(createdNoteId))
            {
                _desktopWorkspace.Apply(new DeleteNoteChange(createdNoteId));
            }

            if (!string.IsNullOrEmpty(createdTagId))
            {
                _desktopWorkspace.Apply(new DeleteTagChange(createdTagId));
            }

            NoteDateFilter.SelectedDate = originalNoteDateFilter;
            _desktopWorkspace.ApplyFilter(originalFilter);
            ShowWorkspace(entryWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    /// <summary>
    ///     #154：视觉铬探针——筛选漏斗实心在现役捕获口径可识别、控件图标描边统一且无重复搜索镜、
    ///     管理窗最小/关闭为矢量窗铬、页签与分段激活胶囊短边一半全圆角、倒计时页手写体落装饰层、
    ///     管理窗请求 DWM 原生圆角与系统阴影且不开整窗透明、页签在装饰带之上（#135 工作台骨架次序）。
    /// </summary>
    internal MainVisualChromeScenarios ProbeVisualChromeScenarios()
    {
        var entryWorkspace = _workspace;
        try
        {
            // 便签工作区可见态：搜索框图标 / 状态分段模板检查。
            ShowWorkspace(WorkspaceKind.Notes);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);

            // AC1：筛选漏斗改实心填充（InkBrush）——1.8px 描边漏斗在 Aliased 捕获下只剩「筛选」文字
            // （v22 实证）；门禁钉「漏斗 Path 存在 + Fill/Stroke 同绑 InkBrush + 可见」。
            var funnel = MainValidationDescendants<System.Windows.Shapes.Path>(FilterButton).FirstOrDefault(path =>
                path.Data is not null &&
                Math.Abs(path.Data.Bounds.Width - 17) <= 0.5 &&
                Math.Abs(path.Data.Bounds.Height - 14) <= 0.5);
            var inkBrush = FindResource("InkBrush") as Brush;
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
                NoteSearchLabel.Parent is StackPanel labelRow &&
                !labelRow.Children.OfType<System.Windows.Shapes.Path>().Any();
            var mutedBrush = FindResource("MutedBrush") as Brush;
            var inputMagnifier =
                (NoteSearch.Parent as Grid)?.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
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
                ChromeButtonHasVectorGlyph(MinimizeButton, expectedWidth: 10, expectedHeight: 0) &&
                ChromeButtonHasVectorGlyph(CloseButton, expectedWidth: 10, expectedHeight: 10);

            // AC3：页签与分段激活胶囊短边一半全圆角。
            // 主窗页签：按钮高 60，Surface(3) 54 → 27，环 60 → 30。
            // 状态分段：按钮高 48，Surface(2) 44 → 22，环 48 → 24。
            // 悬浮工作区胶囊（App 级样式，模板宿主实例化检查）：高 36 → 18。
            var segmentActivePillsUseFullRoundedCorners =
                PillCornersAre(CountdownTab, 27, 30) &&
                PillCornersAre(NotesTab, 27, 30) &&
                PillCornersAre(ActiveNotesSegment, 22, 24) &&
                PillCornersAre(CompletedNotesSegment, 22, 24) &&
                FloatingSegmentStyleUsesPillCorner();

            // 倒计时工作区可见态：手写体装饰落点检查。
            ShowWorkspace(WorkspaceKind.Countdowns);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);

            // AC4：倒计时页手写体落装饰层——两处文案都是 CountdownWorkspace Canvas 直接子级
            // （绝对设计坐标落点），不在 CountdownWorkspaceBody 表单子树内参与测量。
            var decoFont = FindResource("HandwrittenDecoFontFamily") as FontFamily;
            var countdownDecoTexts = MainValidationDescendants<TextBlock>(CountdownWorkspace)
                .Where(textBlock => ReferenceEquals(textBlock.FontFamily, decoFont))
                .ToList();
            var countdownHandwrittenDecoOutOfFormMeasurement =
                decoFont is not null &&
                countdownDecoTexts.Count == 2 &&
                countdownDecoTexts.All(textBlock =>
                    textBlock.Visibility == Visibility.Visible &&
                    !textBlock.IsHitTestVisible &&
                    ReferenceEquals(textBlock.Parent, CountdownWorkspace) &&
                    Canvas.GetLeft(textBlock) > 0 &&
                    Canvas.GetTop(textBlock) > 0) &&
                !MainValidationDescendants<TextBlock>(CountdownWorkspaceBody)
                    .Any(textBlock =>
                        ReferenceEquals(textBlock.FontFamily, decoFont) &&
                        textBlock.Text.Length > 0);

            // AC6（#154/#200）：管理窗保留不透明 WPF 窗铬基线（CornerRadius 0、
            // GlassFrame 0），首个 HWND 创建后请求 Windows 11 DWM 原生圆角与系统阴影；
            // 不支持 DWM 时安全降级到既有窗铬并由诊断日志记录，装饰 raster 仍不可命中。
            var windowChrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            var dwmWindowChromeConfigured =
                DwmWindowChromeResultForValidation is
                    DwmWindowChromeResult.Applied or DwmWindowChromeResult.Fallback;
            var mainWindowSquareChromeNoTransparency =
                !AllowsTransparency &&
                WindowStyle == WindowStyle.None &&
                windowChrome is not null &&
                windowChrome.CornerRadius == new CornerRadius(0) &&
                windowChrome.GlassFrameThickness == new Thickness(0) &&
                Background is not null &&
                dwmWindowChromeConfigured &&
                !CountdownReferenceDecoration.IsHitTestVisible &&
                !NotesReferenceDecoration.IsHitTestVisible;

            // AC7：#135 工作台骨架次序——页签（命令栏）在装饰带之上：装饰层 ZIndex 低于内容投影层、
            // 装饰层不可命中、页签在内容投影层子树内。
            var tabsRenderAboveDecorationBand =
                Panel.GetZIndex(MainReferenceDecorationLayer) < Panel.GetZIndex(MainContentProjection) &&
                !MainReferenceDecorationLayer.IsHitTestVisible &&
                MainContentProjection.IsAncestorOf(CountdownTab) &&
                MainContentProjection.IsAncestorOf(NotesTab);

            return new MainVisualChromeScenarios(
                filterFunnelIconSurvivesAliasedCapture,
                notesSearchDuplicateMagnifierRemoved,
                mainWindowChromeButtonsUseVectorPaths,
                segmentActivePillsUseFullRoundedCorners,
                countdownHandwrittenDecoOutOfFormMeasurement,
                mainWindowSquareChromeNoTransparency,
                tabsRenderAboveDecorationBand);
        }
        finally
        {
            ShowWorkspace(entryWorkspace);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);
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
        if (FindResource("FloatingHeaderSegmentButton") is not Style style)
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
        MainContentProjection.Children.Add(host);
        try
        {
            host.ApplyTemplate();
            WindowValidationInput.Pump(Dispatcher);
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
            MainContentProjection.Children.Remove(host);
        }
    }

    /// <summary>#153：管理窗便签 `…` 菜单键盘生命周期——打开聚焦「编辑」、Tab 到「删除」、再 Tab 回「编辑」（层内循环）、Esc 关闭归还触发器。</summary>
    private bool ProbeMainWindowNoteRowOverflowMenuKeyboard()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        UpdateLayout();
        WindowValidationInput.Pump(Dispatcher);
        var trigger = MainValidationDescendants<Button>(ActiveNoteRows)
            .FirstOrDefault(button =>
                string.Equals(button.Uid, "NoteRowOverflowMenu", StringComparison.Ordinal));
        if (trigger is null)
        {
            // 空便签列表没有可打开的 `…` 菜单，视为满足（空洞真，与 NoteRowOverflowMenuWorks 同语义）；
            // 有便签行而找不到触发器才是失败。
            return _noteSourceRows.Count == 0;
        }

        var popup = NoteRowOverflowMenuBehavior.GetPopup(trigger);
        if (popup is null)
        {
            return false;
        }

        try
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
            Activate();
            Keyboard.Focus(trigger);
            WindowValidationInput.Pump(Dispatcher);
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            WindowValidationInput.Pump(Dispatcher);
            var menuEdit = MainValidationDescendants<Button>(popup.Child)
                .FirstOrDefault(button =>
                    string.Equals(button.Content as string, "编辑", StringComparison.Ordinal));
            var openedWithFocusOnEdit =
                popup.IsOpen &&
                menuEdit is not null &&
                ReferenceEquals(Keyboard.FocusedElement, menuEdit);
            if (menuEdit is null)
            {
                return false;
            }

            menuEdit.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(Dispatcher);
            var menuDelete = MainValidationDescendants<Button>(popup.Child)
                .FirstOrDefault(button =>
                    string.Equals(button.Content as string, "删除", StringComparison.Ordinal));
            var tabReachesDelete =
                menuDelete is not null &&
                ReferenceEquals(Keyboard.FocusedElement, menuDelete);
            var tabWrapsBackToEdit = false;
            if (menuDelete is not null)
            {
                menuDelete.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                WindowValidationInput.Pump(Dispatcher);
                tabWrapsBackToEdit = ReferenceEquals(Keyboard.FocusedElement, menuEdit);
            }

            var escapeHandled = WindowValidationInput.RaisePreviewKey(menuDelete ?? menuEdit, Key.Escape);
            WindowValidationInput.Pump(Dispatcher);
            var popupClosedAfterEscape = WindowValidationInput.WaitForPopupClosed(popup);
            var closedBackToTrigger =
                popupClosedAfterEscape &&
                (trigger.IsKeyboardFocusWithin ||
                 ReferenceEquals(FocusManager.GetFocusedElement(this), trigger));
            return
                openedWithFocusOnEdit &&
                tabReachesDelete &&
                tabWrapsBackToEdit &&
                escapeHandled &&
                closedBackToTrigger;
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
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
            Activate();
            WindowValidationInput.Pump(Dispatcher);
            popup.IsOpen = true;
            WindowValidationInput.Pump(Dispatcher);
            WindowValidationInput.Pump(Dispatcher);
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
                WindowValidationInput.Pump(Dispatcher);
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
        var originalEditingId = _editingNoteId;
        ShowWorkspace(WorkspaceKind.Notes);
        WindowValidationInput.Pump(Dispatcher);
        UpdateLayout();
        try
        {
            var snapshot = _desktopWorkspace.Snapshot;
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
            NoteDate.IsDropDownOpen = false;
            ResetNoteEditor();
            _editingNoteId = originalEditingId;
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private bool LegacyInlineReminderEditorStaysCollapsed(NoteRecord note)
    {
        LoadNoteIntoEditor(note);
        NoteDate.IsDropDownOpen = false;
        UpdateLayout();
        WindowValidationInput.Pump(Dispatcher);
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
            FindName(name) is not FrameworkElement element || !element.IsVisible);
        var summaryBounds = ValidationBounds(NoteReminderRulesSummary);
        var obscured = retiredNames
            .Select(name => FindName(name) as FrameworkElement)
            .OfType<FrameworkElement>()
            .Where(element => element.IsVisible)
            .Select(element => ValidationBounds(element))
            .Any(bounds => bounds.IntersectsWith(summaryBounds));
        return
            retiredHidden &&
            !obscured &&
            ManageNoteReminderRulesButton.IsVisible &&
            NoteReminderRulesSummary.IsVisible &&
            NoteReminderRulesSummary.ActualHeight > 0 &&
            note.ReminderRules.Count > 0 &&
            NoteReminderRulesSummary.Text.Contains("条提醒规则", StringComparison.Ordinal);
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
        var filter = _desktopWorkspace.Snapshot.Filter;
        return
            filter == DesktopWorkspaceFilter.Empty &&
            _desktopWorkspace.Snapshot.VisibleCountdowns.Count > 0 &&
            CountdownRows.Children.Count > 0 &&
            EmptyCountdownMessage.Visibility == Visibility.Collapsed &&
            ResetEmptyCountdownFiltersButton.Visibility == Visibility.Collapsed &&
            // #154：筛选按钮内容改为「图标 + 具名文字」StackPanel，摘要断言读 FilterButtonText。
            string.Equals(FilterButtonText.Text, "筛选", StringComparison.Ordinal) &&
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
            // #148：程序化 Keyboard.Focus 会触发 WPF bring-into-view。若先 ScrollToEnd 再聚焦，
            // 聚焦行的编辑按钮在长分格下可能骑在视口顶裁切线上，此处最小滚入会被误记到 tick 头上。
            // 因此先聚焦、再滚到底取 offsetBefore，让断言只度量「tick 本身是否移动滚动位置」。
            Keyboard.Focus(focusTarget);
            WindowValidationInput.Pump(Dispatcher);
            scrollViewer?.ScrollToEnd();
            WindowValidationInput.Pump(Dispatcher);
            var offsetBefore = scrollViewer?.VerticalOffset ?? 0;
            var scrollableHeightBefore = scrollViewer?.ScrollableHeight ?? 0;
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
            var scrollOffsetAfterTick = scrollViewer?.VerticalOffset ?? 0;
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
                // #148：到期剩余文字不用删除红；该记录是首行主项，到期后回落主项数字色。
                ReferenceEquals(textAfter.Foreground, FindResource("PrimaryCountdownNumberBrush"));
            return new CountdownIncrementalRefreshProbe(
                ordinaryIdentityPreserved,
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
                Opacity = restoredOpacity;
            }

            Mouse.Capture(null);
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

    // #148：倒计时列表可读性探针——「超期待！」排他、到期文字对比、分格几何与表格数字格式。
    // #151：倒计时编辑合同——字段级错误贴字段并聚焦首个无效控件、唯一错误行不裁切、
    // 偏好错误下移到字段下方、主次按钮角色（每页一枚实心主行动 + 珊瑚次级实心）、
    // 白纸输入面、独立选区高亮、状态条表面错误对比、提醒规则选中圆点非文本对比、排版/间距 token。
    internal CountdownEditingScenarios ProbeCountdownEditingScenarios()
    {
        var originalWorkspace = _workspace;
        var originalName = CountdownName.Text;
        var originalDate = CountdownDate.SelectedDate;
        var originalHour = CountdownHour.SelectedIndex;
        var originalMinute = CountdownMinute.SelectedIndex;
        var originalSecond = CountdownSecond.SelectedIndex;
        var originalSalary = MonthlySalary.Text;
        var originalWorkStart = WorkStart.Text;
        var originalWorkEnd = WorkEnd.Text;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(Dispatcher);
            // 焦点断言前置条件：与悬浮窗 DatePicker 探针同一口径，先激活本窗口再模拟提交。
            Activate();
            WindowValidationInput.Pump(Dispatcher);
            ResetCountdownEditor();
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();

            // —— 倒计时字段错误：名称与截止日期按字段拆开，聚焦第一个无效控件（名称优先）。
            CountdownName.Text = string.Empty;
            CountdownDate.SelectedDate = null;
            WindowValidationInput.Pump(Dispatcher);
            SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var fieldErrorsAtSource =
                CountdownNameError.Visibility == Visibility.Visible &&
                CountdownDateError.Visibility == Visibility.Visible &&
                string.Equals(CountdownNameError.Text, "请先填写倒计时名称。", StringComparison.Ordinal) &&
                string.Equals(CountdownDateError.Text, "请选择截止日期。", StringComparison.Ordinal) &&
                string.Equals(CountdownMessage.Text, CountdownNameError.Text, StringComparison.Ordinal) &&
                CountdownName.IsKeyboardFocusWithin &&
                EditingValidationErrorIsAccessible(CountdownNameError, CountdownName) &&
                EditingValidationErrorIsAccessible(CountdownDateError, CountdownDate);

            // 仅日期无效时再次提交：聚焦唯一无效控件（截止日期）。
            CountdownName.Text = "倒计时编辑探针";
            WindowValidationInput.Pump(Dispatcher);
            SaveCountdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var dateOnlyFocusesDate =
                CountdownNameError.Visibility == Visibility.Collapsed &&
                CountdownDateError.Visibility == Visibility.Visible &&
                CountdownDate.IsKeyboardFocusWithin;

            // —— 唯一错误行不再固定高度裁切或省略（MaxHeight 默认为正无穷）。
            var singleErrorRowNotClipped =
                double.IsPositiveInfinity(CountdownMessage.MaxHeight) &&
                CountdownMessage.TextTrimming == TextTrimming.None &&
                CountdownMessage.TextWrapping == TextWrapping.Wrap;

            // —— 字段值修正后错误立即清除。
            CountdownDate.SelectedDate = _desktopWorkspace.Snapshot.BeijingNow.Date;
            WindowValidationInput.Pump(Dispatcher);
            var countdownFieldErrorsClear =
                CountdownNameError.Visibility == Visibility.Collapsed &&
                CountdownDateError.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(CountdownMessage.Text);

            // —— 偏好错误出现在对应字段下方并聚焦；错误态错误字落在状态条表面上（见下）。
            MonthlySalary.Text = "not-a-number";
            SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var salaryErrorBelowField =
                MonthlySalaryError.Visibility == Visibility.Visible &&
                string.Equals(MonthlySalaryError.Text, "月薪格式无效。", StringComparison.Ordinal) &&
                MonthlySalary.IsKeyboardFocusWithin &&
                ValidationBounds(MonthlySalaryError).Top >= ValidationBounds(MonthlySalary).Bottom - 0.5 &&
                EditingValidationErrorIsAccessible(MonthlySalaryError, MonthlySalary) &&
                string.Equals(StatusText.Text, "月薪格式无效。", StringComparison.Ordinal);

            // 月薪先恢复合法值，让校验推进到上班时间字段（镜像真实逐字段提交顺序）。
            MonthlySalary.Text = "8000";
            WorkStart.Text = "9点";
            SavePreferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var workStartErrorBelowField =
                MonthlySalaryError.Visibility == Visibility.Collapsed &&
                WorkStartError.Visibility == Visibility.Visible &&
                string.Equals(WorkStartError.Text, "上班时间请使用 HH:mm。", StringComparison.Ordinal) &&
                WorkStart.IsKeyboardFocusWithin &&
                ValidationBounds(WorkStartError).Top >= ValidationBounds(WorkStart).Bottom - 0.5;

            // —— 偏好字段修正后错误清除（不保存，避免写入偏好）。
            WorkStart.Text = "09:00";
            WindowValidationInput.Pump(Dispatcher);
            var preferenceErrorsClear =
                WorkStartError.Visibility == Visibility.Collapsed;

            // —— 主次按钮角色：倒计时页唯一实心主行动是「＋ 添加倒计时」；
            // 「保存设置」为珊瑚语义次级实心 + 主文字色（珊瑚底白字实测 2.77 不达标）。
            var solidPrimaryButtons = ProbeVisualDescendants<Button>(CountdownWorkspaceBody)
                .Where(button => ReferenceEquals(button.Background, FindResource("MainActionBrush")))
                .ToArray();
            var primarySecondaryRolesDistinct =
                solidPrimaryButtons.Length == 1 &&
                ReferenceEquals(solidPrimaryButtons[0], SaveCountdownButton) &&
                ReferenceEquals(SavePreferencesButton.Background, FindResource("CoralActionBrush")) &&
                ReferenceEquals(SavePreferencesButton.Foreground, FindResource("InkBrush")) &&
                ApplicationTheme.MeetsContrast(
                    (Color)FindResource("CoralActionColor"),
                    (Color)FindResource("InkColor"),
                    4.5) &&
                ReferenceEquals(SaveNoteButton.Background, FindResource("MainActionBrush"));

            // —— 白纸输入面：主窗倒计时页输入控件表面为 PanelBrush，与画布色分开。
            CountdownDate.ApplyTemplate();
            var dateTextBox = CountdownDate.Template.FindName("PART_TextBox", CountdownDate)
                as System.Windows.Controls.Primitives.DatePickerTextBox;
            var dateButton = CountdownDate.Template.FindName("PART_Button", CountdownDate) as Button;
            var inputSurfacesWhitePaper =
                ReferenceEquals(CountdownName.Background, FindResource("PanelBrush")) &&
                ReferenceEquals(MonthlySalary.Background, FindResource("PanelBrush")) &&
                ReferenceEquals(CountdownHour.Background, FindResource("PanelBrush")) &&
                ReferenceEquals(DisplayPrecision.Background, FindResource("PanelBrush")) &&
                ReferenceEquals(CountdownDate.Background, FindResource("PanelBrush")) &&
                dateTextBox is not null &&
                ReferenceEquals(dateTextBox.Background, FindResource("PanelBrush")) &&
                dateButton is not null &&
                ReferenceEquals(dateButton.Background, CountdownDate.Background) &&
                !((Color)FindResource("PanelColor")).Equals((Color)FindResource("BackgroundColor"));

            // —— 文本选区高亮：独立语义色（不得复用 PeachLine 结构线色）且 0.4 合成肉眼可辨。
            var selectionBrush = CountdownName.SelectionBrush;
            var selectionIsIndependent =
                selectionBrush is SolidColorBrush selectionSolid &&
                ReferenceEquals(selectionBrush, FindResource("SelectionBrush")) &&
                selectionSolid.Color != (Color)FindResource("PeachLineColor");
            var selectionHighlightDistinct = false;
            if (selectionIsIndependent)
            {
                var panelColor = (Color)FindResource("PanelColor");
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
            var statusBarSurface = FindName("MainStatusBarSurface") as Border;
            var statusBarSurfaceErrorContrast =
                statusBarSurface is not null &&
                ReferenceEquals(statusBarSurface.Background, FindResource("StatusBarBrush")) &&
                ReferenceEquals(statusBarSurface.BorderBrush, FindResource("PeachLineBrush")) &&
                ReferenceEquals(StatusText.Foreground, FindResource("DangerBrush")) &&
                ApplicationTheme.MeetsContrast(
                    (Color)FindResource("StatusBarColor"),
                    (Color)FindResource("DangerColor"),
                    4.5);

            // —— 提醒规则选中圆点/描边：焦点·强调轨语义色 ActiveBrush，对编辑卡画布 ≥ 3:1。
            CountdownReminderNoRadio.ApplyTemplate();
            var radioDot = CountdownReminderNoRadio.Template.FindName("Dot", CountdownReminderNoRadio) as Ellipse;
            var radioRing = CountdownReminderNoRadio.Template.FindName("Ring", CountdownReminderNoRadio) as Ellipse;
            var editorSurfaceColor = CountdownEditorSurface.Background is SolidColorBrush surfaceBrush
                ? surfaceBrush.Color
                : (Color?)null;
            var reminderRadioSelectedContrast =
                CountdownReminderNoRadio.IsChecked == true &&
                radioDot is not null &&
                radioRing is not null &&
                radioDot.Visibility == Visibility.Visible &&
                ReferenceEquals(radioDot.Fill, FindResource("ActiveBrush")) &&
                ReferenceEquals(radioRing.Stroke, FindResource("ActiveBrush")) &&
                editorSurfaceColor is not null &&
                ApplicationTheme.MeetsContrast(editorSurfaceColor.Value, (Color)FindResource("ActiveColor"), 3.0);

            // —— 排版/间距 token：区块标题落 TypeH1；字段行距/错误行距消费间距 token 值。
            var typeH1 = (double)FindResource("TypeH1FontSize");
            var spacingUnit = (double)FindResource("SpacingUnit");
            var spacingFieldRow = (double)FindResource("SpacingFieldRow");
            var editorTypographyTokens =
                Math.Abs(CountdownEditorTitle.FontSize - typeH1) <= 0.01 &&
                Math.Abs(CountdownPreferencesTitle.FontSize - typeH1) <= 0.01 &&
                Math.Abs(MainCountdownNameDateRow.Margin.Top - spacingFieldRow) <= 0.01 &&
                Math.Abs(MainCountdownTimeTagRow.Margin.Top - spacingFieldRow) <= 0.01 &&
                Math.Abs(MainCountdownActionRow.Margin.Top - spacingFieldRow) <= 0.01 &&
                Math.Abs(CountdownMessage.Margin.Top - spacingUnit) <= 0.01 &&
                Math.Abs(CountdownNameError.Margin.Top - spacingUnit) <= 0.01 &&
                Math.Abs(MonthlySalaryError.Margin.Top - spacingUnit) <= 0.01;

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
            CountdownName.Text = originalName;
            CountdownDate.SelectedDate = originalDate;
            CountdownHour.SelectedIndex = originalHour;
            CountdownMinute.SelectedIndex = originalMinute;
            CountdownSecond.SelectedIndex = originalSecond;
            MonthlySalary.Text = originalSalary;
            WorkStart.Text = originalWorkStart;
            WorkEnd.Text = originalWorkEnd;
            ClearCountdownFieldErrors();
            ClearPreferenceFieldErrors();
            UiAccessibility.Announce(CountdownMessage, string.Empty);
            ResetCountdownEditor();
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
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
        var originalWorkspace = CurrentWorkspace;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(Dispatcher);

            var focusRingsReserveSpace =
                FocusDoesNotChangeLayout(SaveCountdownButton, "FocusRing", "Surface") &&
                FocusDoesNotChangeLayout(TopmostCheck, "Box", "Box") &&
                FocusDoesNotChangeLayout(CountdownTab, "FocusRing", "Surface");

            // 主按钮/置顶只有模板内暖粉焦点环（FocusVisualStyle 为空）；常规控件保留 2px 暖边。
            var primaryAndPinSingleFocusStroke =
                SaveCountdownButton.FocusVisualStyle is null &&
                SaveNoteButton.FocusVisualStyle is null &&
                TopmostCheck.FocusVisualStyle is null &&
                FilterButton.FocusVisualStyle is Style regularStroke &&
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

            // #191：选中页签必须以 PinkTint 底 + SemiBold 建立高于未选中页签
            // 的权重，悬停只补充反馈，不能反转层级。
            var selectedTabWeightDominates = SelectedTabWeightDominates();

            // 按压视觉无法在无鼠标隔离桌面驱动（ButtonBase 需真实 IsMouseOver 才置 IsPressed），
            // 改按模板触发器合同钉住：IsPressed → 状态环 ActiveBrush（与键盘焦点同语言）；禁用走运行态。
            var localControlsPressAndDisabled =
                TemplateHasPressRingTrigger(CountdownTab, "FocusRing") &&
                DisabledUses45Opacity(CountdownTab, "InteractionSurface") &&
                TemplateHasPressRingTrigger(CountdownReminderHelp, "FocusRing") &&
                DisabledUses45Opacity(CountdownReminderHelp, "Surface") &&
                TemplateHasPressRingTrigger(CountdownReminderNoRadio, "FocusRing") &&
                DisabledUses45Opacity(CountdownReminderNoRadio, "Surface") &&
                DateDisclosureTemplateHasPressAndDisabled();

            var segmentIndicators = ProbeSegmentIndicatorScenarios();

            // 聚焦帮助显示与悬停相同的说明（同一 ToolTip 即时打开），并提供帮助文本。
            Activate();
            var helpTooltip = CountdownReminderHelp.ToolTip as ToolTip;
            Keyboard.Focus(CountdownReminderHelp);
            WindowValidationInput.Pump(Dispatcher);
            var helpOpensOnFocus = helpTooltip is not null && helpTooltip.IsOpen;
            var helpTextProvided =
                helpTooltip?.Content is string tooltipText &&
                tooltipText.Length > 0 &&
                string.Equals(
                    AutomationProperties.GetHelpText(CountdownReminderHelp),
                    tooltipText,
                    StringComparison.Ordinal);
            Keyboard.Focus(CloseButton);
            WindowValidationInput.Pump(Dispatcher);
            var helpClosesOnBlur = helpTooltip is not null &&
                WindowValidationInput.WaitUntil(Dispatcher, () => !helpTooltip.IsOpen);
            var helpFocusShowsHelpText = helpOpensOnFocus && helpTextProvided && helpClosesOnBlur;
            if (!helpFocusShowsHelpText)
            {
                Console.Error.WriteLine("HELP_FOCUS_DIAGNOSTIC " +
                    System.Text.Json.JsonSerializer.Serialize(new { helpOpensOnFocus, helpTextProvided, helpClosesOnBlur }));
            }

            // #208: the 1165 x 1350 canvas is laid out at 1:1 DIP, so the
            // compact controls keep their 38 DIP hit slots without a Viewbox.
            var scale = MainScaleViewport.ActualWidth > 0
                ? MainScaleViewport.ActualWidth / MainLayoutSurface.Width
                : 0;
            var compactHitTargetsMeetScaledFloor =
                Math.Abs(CountdownReminderHelp.ActualWidth - 38) <= 1 &&
                Math.Abs(CountdownReminderHelp.ActualHeight - 38) <= 1 &&
                Math.Abs(scale - 1) <= 0.02 &&
                38 * scale >= 24;

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
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    // #196：页签与便签状态分段共享一个可中断指示条。这里以真实控件的
    // TransformToAncestor 几何作断言，并快速往返切换，确保过期回调不会把
    // 指示条留在旧段；关闭系统动画时只保留同一目标几何的即时等价。
    private SegmentIndicatorScenarios ProbeSegmentIndicatorScenarios()
    {
        var originalWorkspace = _workspace;
        var originalCompleted = _completedSegmentSelected;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            Activate();
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var countdownTabAligned = ObserveIndicator(_workspaceTabIndicator, CountdownTab, MainWorkspaceTabHost);

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var notesTabAligned = ObserveIndicator(_workspaceTabIndicator, NotesTab, MainWorkspaceTabHost);
            var activeSegmentAligned = ObserveIndicator(_notesSegmentIndicator, ActiveNotesSegment, NotesSegmentHost);

            SelectNotesSegment(showCompleted: true);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var completedSegmentAligned = ObserveIndicator(_notesSegmentIndicator, CompletedNotesSegment, NotesSegmentHost);

            var rapidSwitchStaysAligned =
                SegmentIndicatorRapidSwitchIsStableForValidation;
            var reducedMotionIsImmediate =
                SegmentIndicatorReducedMotionIsImmediateForValidation;

            return new SegmentIndicatorScenarios(
                countdownTabAligned &&
                notesTabAligned &&
                activeSegmentAligned &&
                completedSegmentAligned,
                reducedMotionIsImmediate,
                rapidSwitchStaysAligned);
        }
        finally
        {
            _completedSegmentSelected = originalCompleted;
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
        }

        bool ObserveIndicator(SegmentIndicator indicator, FrameworkElement target, FrameworkElement root)
        {
            var aligned = WindowValidationInput.WaitUntil(Dispatcher, () => indicator.IsAlignedTo(target));
            if (!aligned)
            {
                var targetBounds = target.TransformToAncestor(root).TransformBounds(new Rect(target.RenderSize));
                Console.Error.WriteLine("SEGMENT_GEOMETRY_DIAGNOSTIC " +
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        target.Name,
                        target.IsVisible,
                        Actual = indicator.CurrentVisualBounds.ToString(CultureInfo.InvariantCulture),
                        Target = targetBounds.ToString(CultureInfo.InvariantCulture),
                        LastTarget = indicator.LastTargetBounds.ToString(CultureInfo.InvariantCulture)
                    }));
            }

            return aligned;
        }
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

        Activate();
        var widthBefore = control.ActualWidth;
        var heightBefore = control.ActualHeight;
        var ringThicknessBefore = ring.BorderThickness;
        var focusVisualBefore = control.FocusVisualStyle;
        Keyboard.Focus(control);
        WindowValidationInput.Pump(Dispatcher);
        var layoutStable =
            Math.Abs(control.ActualWidth - widthBefore) <= 0.01 &&
            Math.Abs(control.ActualHeight - heightBefore) <= 0.01 &&
            ring.BorderThickness == ringThicknessBefore &&
            ReferenceEquals(control.FocusVisualStyle, focusVisualBefore);
        Keyboard.Focus(CloseButton);
        WindowValidationInput.Pump(Dispatcher);
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
        MainContentProjection.Children.Add(host);
        try
        {
            host.ApplyTemplate();
            WindowValidationInput.Pump(Dispatcher);
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
            MainContentProjection.Children.Remove(host);
        }
    }

    // 未选中「便签」页签的 IsMouseOver 触发器不得把 Surface 底换成选中实心底 PeachTintBrush
    // （#152 换成「边换 ActiveBrush」）；IsChecked 触发器才允许设置选中底。
    private bool NotesTabHoverDoesNotReuseSelectedFill()
    {
        if (NotesTab.Template is not { } template)
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

        var motionSurface = template.FindName("InteractionSurface", NotesTab) as FrameworkElement;
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
                "PinkTintBrush",
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
            SaveCountdownButton,
            SavePreferencesButton,
            SaveNoteButton,
            CloseButton,
            FilterButton,
            CountdownTab,
            NotesTab,
            CountdownReminderHelp,
            CountdownReminderNoRadio
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
            SaveCountdownButton,
            SavePreferencesButton,
            SaveNoteButton,
            CloseButton,
            FilterButton,
            CountdownTab,
            NotesTab,
            CountdownReminderHelp,
            CountdownReminderNoRadio,
            CountdownReminderTimeRadio,
            CountdownReminderCustomRadio,
            TopmostCheck
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
        var originalWorkspace = _workspace;
        var originalCompleted = _completedSegmentSelected;
        var originalName = CountdownName.Text;
        var originalMessage = CountdownMessage.Text;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            Activate();
            WindowValidationInput.Pump(Dispatcher);
            // A required empty name keeps the real save click inside the
            // validation fixture, without creating a countdown as a side effect.
            CountdownName.Text = string.Empty;
            var saveWorks = PointerRemovesKeyboardRing(SaveCountdownButton);
            var countdownTabWorks = PointerRemovesKeyboardRing(CountdownTab);
            var notesTabWorks = PointerRemovesKeyboardRing(NotesTab);
            var activeWorks = PointerRemovesKeyboardRing(ActiveNotesSegment);
            var completedWorks = PointerRemovesKeyboardRing(CompletedNotesSegment);
            if (!(saveWorks && countdownTabWorks && notesTabWorks && activeWorks && completedWorks))
            {
                Console.Error.WriteLine("POINTER_FOCUS_DIAGNOSTIC " +
                    System.Text.Json.JsonSerializer.Serialize(new { saveWorks, countdownTabWorks, notesTabWorks, activeWorks, completedWorks }));
            }

            return saveWorks && countdownTabWorks && notesTabWorks && activeWorks && completedWorks;
        }
        finally
        {
            CountdownName.Text = originalName;
            ClearCountdownFieldErrors();
            UiAccessibility.Announce(CountdownMessage, originalMessage);
            _completedSegmentSelected = originalCompleted;
            ShowWorkspace(originalWorkspace);
            Keyboard.Focus(CloseButton);
            WindowValidationInput.Pump(Dispatcher);
        }

        bool PointerRemovesKeyboardRing(Control control)
        {
            control.BringIntoView();
            Keyboard.Focus(CloseButton);
            Keyboard.Focus(control);
            WindowValidationInput.Pump(Dispatcher);
            control.ApplyTemplate();
            var ring = control.Template?.FindName("FocusRing", control) as Border;
            var keyboardRingWasVisible = control.IsKeyboardFocused &&
                FocusVisualState.GetIsKeyboardFocusVisible(control) &&
                ring is not null && !IsTransparent(ring.BorderBrush);
            Keyboard.Focus(CloseButton);
            WindowValidationInput.Pump(Dispatcher);
            var pointerFocused = WindowValidationInput.ClickForFocusValidation(control);
            return keyboardRingWasVisible && pointerFocused &&
                !FocusVisualState.GetIsKeyboardFocusVisible(control) &&
                ring is not null && IsTransparent(ring.BorderBrush);
        }
    }

    private bool KeyboardFocusRingShowsOnTab()
    {
        var originalWorkspace = _workspace;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            Activate();
            WindowValidationInput.Pump(Dispatcher);
            var previous = CancelCountdownEdit.IsVisible
                ? (UIElement)CancelCountdownEdit
                : CountdownReminderCustomRadio;
            var saveButtonWorks = TabReachesVisibleRing(
                previous, SaveCountdownButton, FocusNavigationDirection.Next, "MainActionFocusBrush");

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher);
            var completedWorks = TabReachesVisibleRing(
                ActiveNotesSegment, CompletedNotesSegment, FocusNavigationDirection.Next, "ActiveBrush");
            var activeWorks = TabReachesVisibleRing(
                CompletedNotesSegment, ActiveNotesSegment, FocusNavigationDirection.Previous, "ActiveBrush");
            return saveButtonWorks && completedWorks && activeWorks;
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
            Keyboard.Focus(CloseButton);
            WindowValidationInput.Pump(Dispatcher);
        }

        bool TabReachesVisibleRing(UIElement from, Control target, FocusNavigationDirection direction, string resourceKey)
        {
            Keyboard.Focus(from);
            WindowValidationInput.Pump(Dispatcher);
            var moved = from.MoveFocus(new TraversalRequest(direction));
            WindowValidationInput.Pump(Dispatcher);
            target.ApplyTemplate();
            return moved && target.IsKeyboardFocused &&
                target.Template?.FindName("FocusRing", target) is Border ring &&
                ring.BorderThickness == new Thickness(2) &&
                UsesResourceBrush(ring.BorderBrush, resourceKey);
        }
    }

    // #191：两枚主窗页签的选中态都使用 PinkTintBrush + SemiBold；未选中态不
    // 预先加粗，确保视觉权重不会被悬停红环反转。
    private bool SelectedTabWeightDominates()
    {
        var originalWorkspace = _workspace;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var countdownDominates = SelectedTabHasWeight(CountdownTab, NotesTab);
            ShowWorkspace(WorkspaceKind.Notes);
            return countdownDominates && SelectedTabHasWeight(NotesTab, CountdownTab);
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private bool SelectedTabHasWeight(ToggleButton selected, ToggleButton unselected)
    {
        selected.ApplyTemplate();
        unselected.ApplyTemplate();
        if (selected.Template?.FindName("Surface", selected) is not Border selectedSurface ||
            unselected.Template?.FindName("Surface", unselected) is not Border unselectedSurface)
        {
            return false;
        }

        // Observe the actual color after the bounded transition; a declared token
        // or a trigger setter alone does not prove that the selected fill rendered.
        return WindowValidationInput.WaitUntil(Dispatcher, () =>
            selected.IsChecked == true &&
            unselected.IsChecked == false &&
            UsesResourceBrush(selectedSurface.Background, "PinkTintBrush") &&
            !UsesResourceBrush(unselectedSurface.Background, "PinkTintBrush") &&
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
        WindowValidationInput.Pump(Dispatcher);
        var disabledOpacity = part.Opacity;
        control.IsEnabled = original;
        WindowValidationInput.Pump(Dispatcher);
        return Math.Abs(disabledOpacity - 0.45) <= 0.001;
    }

    // 日期披露按钮由代码构建，模板触发器检查通过窗口内样式资源进行（按压=环 Active、禁用=0.45）。
    private bool DateDisclosureTemplateHasPressAndDisabled()
    {
        if ((Style)MainContentProjection.FindResource("MainNoteDateDisclosureButton") is not { } style)
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
        var hasDisabledOpacity = triggers
            .OfType<Trigger>()
            .Where(trigger => trigger.Property == UIElement.IsEnabledProperty)
            .SelectMany(trigger => trigger.Setters.OfType<Setter>())
            .Any(setter =>
                setter.Property == UIElement.OpacityProperty &&
                setter.Value is double opacity &&
                Math.Abs(opacity - 0.45) <= 0.001);
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
        var options = (Panel)FindName("FilterTagOptions")!;
        return options.Children
            .OfType<FrameworkElement>()
            .All(child => child.Margin.Right >= 8 && child.Margin.Bottom >= 8);
    }

    private bool RequiredToggleButtonMargin(string name) =>
        FindName(name) is FrameworkElement element && element.Margin.Left >= 8;

    private MainCountdownReadabilityScenarios ProbeCountdownReadabilityScenarios()
    {
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var temporaryIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var nowUtc = _desktopWorkspace.Snapshot.UtcNow;
            // 主项/次项各一条进行中条目供徽章与分格几何检查；再各一条过去截止条目供到期对比检查。
            // 会话层允许保存 0 规则的过去截止倒计时（编辑器 UI 才限制未来），全程使用真实时钟。
            foreach (var (name, targetUtc) in new[]
                     {
                         ("验证倒计时可读-主项", nowUtc.AddDays(1)),
                         ("验证倒计时可读-次项", nowUtc.AddDays(2)),
                         ("验证倒计时可读-到期主项", nowUtc.AddDays(-1)),
                         ("验证倒计时可读-到期次项", nowUtc.AddDays(-2))
                     })
            {
                var result = _desktopWorkspace.Apply(new SaveCountdownChange(
                    null,
                    name,
                    targetUtc,
                    Array.Empty<string>()));
                if (!result.Succeeded)
                {
                    return MainCountdownReadabilityScenarios.Failed;
                }

                temporaryIds.Add(_desktopWorkspace.Snapshot.Settings.Countdowns
                    .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal)).Id);
            }

            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();

            // 「超期待！」只挂距离下班；管理窗口没有距离下班条目，任何列表行都不得携带徽章。
            // 列表必须确有行渲染，排除空列表下的空洞真。
            var rowsRendered = CountdownRows.Children.Count > 0;
            var encourageBadgeAbsent = rowsRendered && !ProbeVisualDescendants<TextBlock>(CountdownRows)
                .Any(text => string.Equals(text.Text, "超期待！", StringComparison.Ordinal));

            // 主项/次项分格与悬浮窗同一套竖长口径（约 2:3、单位在块外、单位字不小于辅助下限档）。
            var blocks = CountdownBlocksFollowWarmPaperContract();

            // 「已到期」筛选下：首行到期主项回落主项数字色，其余到期次项回落正文色，
            // 「已到期」文案保留，卡底为到期弱化底。
            _desktopWorkspace.ApplyFilter(originalFilter with { CountdownStatus = CountdownStatusFilter.Expired });
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var expiredRows = CountdownRows.Children.OfType<Border>().ToArray();
            var expiredBrush = FindResource("ExpiredBrush");
            var numberBrush = FindResource("PrimaryCountdownNumberBrush");
            var bodyBrush = FindResource("BodyBrush");
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
            _desktopWorkspace.ApplyFilter(originalFilter);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();

            // 北京时间时钟使用表格数字；月薪输入与「今日已赚」同一套本地数字格式。
            var numericTextsMatchContract =
                Typography.GetNumeralAlignment(NowText) == FontNumeralAlignment.Tabular &&
                decimal.TryParse(
                    MonthlySalary.Text,
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out var salaryValue) &&
                string.Equals(
                    MonthlySalary.Text,
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
            _desktopWorkspace.ApplyFilter(originalFilter);
            foreach (var temporaryId in temporaryIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _desktopWorkspace.Apply(new DeleteCountdownChange(temporaryId));
            }

            UpdateTime(ChinaStandardTime.ToBeijingTime(_desktopWorkspace.Snapshot.UtcNow));
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private MainCountdownBlocksProbeResult CountdownBlocksFollowWarmPaperContract()
    {
        var primaryBlockBrush = FindResource("PinkBlockBrush");
        var secondaryBlockBrush = FindResource("SecondaryBlockBrush");
        var numberBrush = FindResource("PrimaryCountdownNumberBrush");
        var inkBrush = FindResource("InkBrush");
        var mutedBrush = FindResource("MutedBrush");
        var cards = CountdownRows.Children.OfType<Border>().ToArray();
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

                    // 竖长 2:3：块高固定为基准块宽的 1.5 倍（主项 52x78、次项 46x69），
                    // 长数值只允许加宽色块，不允许压扁比例或缩单位字。
                    var expectedBaseWidth = isPrimary ? 52 : 46;
                    var expectedHeight = isPrimary ? 78 : 69;
                    var expectedRadius = expectedBaseWidth / 4.0;
                    if (Math.Abs(block.Height - expectedHeight) > 0.01 ||
                        block.MinWidth < expectedBaseWidth - 0.01 ||
                        Math.Abs(block.CornerRadius.TopLeft - expectedRadius) > 0.01)
                    {
                        result.VerticalRatioMatches = false;
                    }

                    // Four primary blocks keep their 52 DIP width and 26 DIP
                    // digits by taking the specified compact 6 DIP gap.
                    var expectedGap = isPrimary ? pairPanels.Length == 4 ? 6 : 14 : 13;
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
        var originalPreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var temporaryIds = new List<string>();
        string? sampleTagId = null;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            sampleTagId = _desktopWorkspace.Apply(new CreateTagChange("倒计时字号验证", null)).CreatedTag?.Id;
            if (sampleTagId is null)
            {
                return Array.Empty<MainCountdownRemainingMeasurement>();
            }
            var sampleTags = new[] { sampleTagId };
            _desktopWorkspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = sampleTags });
            var snapshot = _desktopWorkspace.Snapshot;
            var nowUtc = snapshot.UtcNow;
            var ordinaryName = "验证主窗口倒计时";
            var ordinaryResult = _desktopWorkspace.Apply(new SaveCountdownChange(
                null,
                ordinaryName,
                nowUtc.AddDays(4).AddHours(13).AddMinutes(18).AddSeconds(59),
                sampleTags));
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
                sampleTags));
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
                var sampleNowUtc = _desktopWorkspace.Snapshot.UtcNow;
                UpdateTime(ChinaStandardTime.ToBeijingTime(sampleNowUtc));
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

                var expected = CountdownFormatting.FormatRemaining(record.TargetUtc, sampleNowUtc, precision);
                samples.Add(CaptureMainCountdownRemaining(precision, record, expected, text, saved));
            }

            var longRecord = _desktopWorkspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
                string.Equals(item.Name, longName, StringComparison.Ordinal));
            if (longRecord is not null)
            {
                var longNowUtc = _desktopWorkspace.Snapshot.UtcNow;
                UpdateTime(ChinaStandardTime.ToBeijingTime(longNowUtc));
                UpdateLayout();
                if (CountdownRemainingTextsForValidation.TryGetValue(longRecord.Id, out var longText))
                {
                    var expected = CountdownFormatting.FormatRemaining(longRecord.TargetUtc, longNowUtc, "Second");
                    samples.Add(CaptureMainCountdownRemaining("SecondLongDays", longRecord, expected, longText, longResult.Succeeded));
                }
            }

            var ordinaryRecord = _desktopWorkspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
                temporaryIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase) && item.Name == ordinaryName);
            if (ordinaryRecord is not null)
            {
                var sampleNowUtc = _desktopWorkspace.Snapshot.UtcNow;
                var targetUtc = sampleNowUtc.AddDays(999).AddHours(13).AddMinutes(18).AddSeconds(59);
                var saved = _desktopWorkspace.Apply(new SaveCountdownChange(
                    ordinaryRecord.Id, ordinaryName, targetUtc, sampleTags)).Succeeded;
                WindowValidationInput.Pump(Dispatcher);
                UpdateTime(ChinaStandardTime.ToBeijingTime(sampleNowUtc));
                UpdateLayout();
                if (CountdownRemainingTextsForValidation.TryGetValue(ordinaryRecord.Id, out var text))
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
                _desktopWorkspace.Apply(new DeleteCountdownChange(id));
            }
            if (sampleTagId is not null)
            {
                _desktopWorkspace.Apply(new DeleteTagChange(sampleTagId));
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
            var themeExercise = RunDayThemeExercise();
            var manualPlacement = RunManualPlacementExercise();
            var uiPolish = ProbeUiPolishScenarios();
            var workspace = ProbeWorkspaceScenarios(initialStateMatches);
            var noteSearch = ProbeNoteSearchScenarios();
            // #212：渲染签名稳定刷新探针（焦点/实例保留、隐藏与最小化门控）。
            var stableRefresh = ProbeStableRefreshScenarios();
            // 放在最后：设置按钮与“…”编辑会揭示管理窗口，避免扰动前面的聚焦类探针。
            var bottomCommand = ProbeBottomCommandScenarios();

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
                bottomCommand);
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            if (_manager is Window managerWindow)
            {
                managerWindow.Activate();
                WindowValidationInput.Pump(Dispatcher);
            }
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
        var countdownSelectionSurvivesRefresh =
            InitialCountdownSelectionSurvivesRefresh();

        var incomePreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
        var originalIncomeText = IncomeText;
        var originalIncomeVisibility = FloatingIncome.Visibility;
        var incomeHiddenWithoutSalary = false;
        var incomeRestoredWithSalary = false;
        try
        {
            var zeroSalaryApplied = _desktopWorkspace.Apply(new SavePreferencesChange(
                incomePreferences.DisplayPrecision,
                0m,
                incomePreferences.WorkStart,
                incomePreferences.WorkEnd)).Succeeded;
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            // 月薪未输入：不渲染金额；渲染结果必须等于底部状态解析对当前日历状态的决定——
            // 就绪路径是当前工作区的手写体鼓励文案，未就绪路径是 #218 的统一说明文案，
            // 加载中路径保持日历状态文本。
            var expectedEncouragement = _workspace == WorkspaceKind.Notes
                ? FloatingBottomStatus.NotesEncouragementText
                : FloatingBottomStatus.CountdownEncouragementText;
            var expectedBottomStatusText = FloatingBottomStatus.Resolve(
                null,
                0m,
                _calendar.State,
                _calendar.StatusText,
                expectedEncouragement).Text;
            incomeHiddenWithoutSalary =
                zeroSalaryApplied &&
                !(IncomeText ?? string.Empty).StartsWith("今日已赚", StringComparison.Ordinal) &&
                FloatingIncome.Visibility == Visibility.Visible &&
                string.Equals(IncomeText, expectedBottomStatusText, StringComparison.Ordinal);
        }
        finally
        {
            var currentPreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
            if (currentPreferences.MonthlySalary != incomePreferences.MonthlySalary)
            {
                _desktopWorkspace.Apply(new SavePreferencesChange(
                    incomePreferences.DisplayPrecision,
                    incomePreferences.MonthlySalary,
                    incomePreferences.WorkStart,
                    incomePreferences.WorkEnd));
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
            }

            incomeRestoredWithSalary =
                _desktopWorkspace.Snapshot.Settings.Preferences.MonthlySalary ==
                    incomePreferences.MonthlySalary &&
                FloatingIncome.Visibility == originalIncomeVisibility &&
                string.Equals(IncomeText, originalIncomeText, StringComparison.Ordinal);
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
            FloatingCountdownCount,
            FloatingCountdownTexts,
            IncomeText,
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
            FloatingNotesCommandArea.Visibility == (workspace == WorkspaceKind.Notes
                ? Visibility.Visible
                : Visibility.Collapsed) &&
            FloatingNoteEntry.Visibility == Visibility.Collapsed &&
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

    private FloatingNoteSearchValidationScenarios ProbeNoteSearchScenarios()
    {
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var originalExpanded = _searchExpanded;
        var originalNoteText = FloatingNoteText.Text;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var hiddenInCountdowns =
                FloatingSearchNotes.Visibility == Visibility.Collapsed &&
                FloatingNoteSearchSlot.Visibility == Visibility.Collapsed;

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var visibleInNotes =
                FloatingSearchNotes.Visibility == Visibility.Visible &&
                string.Equals(
                    AutomationProperties.GetName(FloatingSearchNotes),
                    "搜索便签",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(FloatingNoteSearch),
                    "搜索便签",
                    StringComparison.Ordinal);
            var searchButtonShowsClosedMagnifier = ProbeSearchButtonShowsClosedMagnifier();

            Keyboard.Focus(FloatingAddNoteToggle);
            WindowValidationInput.Pump(Dispatcher);
            FloatingAddNoteToggle.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(Dispatcher);
            var addThenSearchTab =
                ReferenceEquals(Keyboard.FocusedElement, FloatingSearchNotes);

            FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var expandsAndFocuses =
                FloatingNoteSearchInputRow.Visibility == Visibility.Visible &&
                ReferenceEquals(Keyboard.FocusedElement, FloatingNoteSearch);

            var sampleNote = _desktopWorkspace.Snapshot.VisibleActiveNotes.FirstOrDefault();
            var matchTerm = sampleNote is null
                ? "不会命中"
                : sampleNote.Text.Length <= 2
                    ? sampleNote.Text
                    : sampleNote.Text[..Math.Min(4, sampleNote.Text.Length)];
            var placementLeft = Left;
            var placementTop = Top;
            var placementWidth = ActualWidth;
            var placementHeight = ActualHeight;
            FloatingNoteSearch.Text = matchTerm;
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);
            var searchSizeUnchanged =
                Math.Abs(ActualWidth - placementWidth) <= 0.5 &&
                Math.Abs(ActualHeight - placementHeight) <= 0.5;
            var searchRefreshLeavesPlacementUnchanged =
                !searchSizeUnchanged ||
                (Math.Abs(Left - placementLeft) <= 0.5 &&
                 Math.Abs(Top - placementTop) <= 0.5);
            var expectedMatchIds = _desktopWorkspace.Snapshot.VisibleActiveNotes
                .Select(note => note.Id)
                .ToArray();
            var filtersRenderedIds = GetFloatingRenderedNoteIds().SequenceEqual(
                expectedMatchIds,
                StringComparer.Ordinal);

            FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var hintText = FloatingNoteSearchHintTerm.Content as string;
            var collapseKeepsTerm =
                FloatingNoteSearchHint.Visibility == Visibility.Visible &&
                string.Equals(hintText, $"搜索:{matchTerm}", StringComparison.Ordinal) &&
                GetFloatingRenderedNoteIds().SequenceEqual(expectedMatchIds, StringComparer.Ordinal);
            var hintNamesDescribeAction =
                string.Equals(
                    AutomationProperties.GetName(FloatingNoteSearchHintTerm),
                    $"展开搜索：{matchTerm}",
                    StringComparison.Ordinal) &&
                string.Equals(hintText, $"搜索:{matchTerm}", StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(FloatingNoteSearchHintClear),
                    "清除搜索",
                    StringComparison.Ordinal);

            FloatingNoteSearchHintTerm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var hintReexpands =
                FloatingNoteSearchInputRow.Visibility == Visibility.Visible &&
                ReferenceEquals(Keyboard.FocusedElement, FloatingNoteSearch);

            WindowValidationInput.RaisePreviewKey(this, Key.Escape);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var escapeClears =
                FloatingNoteSearchSlot.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(FloatingNoteSearch.Text) &&
                string.IsNullOrWhiteSpace(_desktopWorkspace.Snapshot.Filter.NoteSearch);

            FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var longTerm = new string('长', 140);
            FloatingNoteSearch.Text = longTerm;
            WindowValidationInput.Pump(Dispatcher);
            FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var hintTermTextBlock = ValidationDescendants<TextBlock>(FloatingNoteSearchHintTerm)
                .FirstOrDefault();
            var longHintBounds = ValidationBounds(FloatingNoteSearchHintTerm);
            var longHintSlotBounds = ValidationBounds(FloatingNoteSearchHint);
            var longHintClearBounds = ValidationBounds(FloatingNoteSearchHintClear);
            var longHintTermTrimsWithinSlot =
                FloatingNoteSearchHint.Visibility == Visibility.Visible &&
                hintTermTextBlock is not null &&
                hintTermTextBlock.TextTrimming == TextTrimming.CharacterEllipsis &&
                longHintBounds.Right <= longHintSlotBounds.Right + 0.5 &&
                !longHintBounds.IntersectsWith(longHintClearBounds) &&
                string.Equals(
                    FloatingNoteSearchHintTerm.Content as string,
                    $"搜索:{longTerm}",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(FloatingNoteSearchHintTerm),
                    $"展开搜索：{longTerm}",
                    StringComparison.Ordinal);
            WindowValidationInput.RaisePreviewKey(this, Key.Escape);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();

            Keyboard.Focus(FloatingAddNoteToggle);
            WindowValidationInput.Pump(Dispatcher);
            FloatingAddNoteToggle.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(Dispatcher);
            var collapsedOmitsSearchInput =
                !FloatingNoteSearch.IsTabStop &&
                ReferenceEquals(Keyboard.FocusedElement, FloatingSearchNotes);

            FloatingSearchNotes.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            var missTerm = $"__floating_search_miss_{Guid.NewGuid():N}";
            FloatingNoteSearch.Text = missTerm;
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            TextBlock? FindEmptyMessage() =>
                ValidationDescendants<TextBlock>(FloatingRows).FirstOrDefault(text =>
                    string.Equals(text.Text, "没有匹配的便签。", StringComparison.Ordinal));
            Button? FindEmptyClear() =>
                ValidationDescendants<Button>(FloatingRows).FirstOrDefault(button =>
                    string.Equals(button.Content as string, "清除搜索", StringComparison.Ordinal));
            var emptyMessage = FindEmptyMessage();
            var emptyClear = FindEmptyClear();
            var emptyDoesNotOverlap =
                emptyClear is not null &&
                emptyMessage is not null &&
                !ValidationBounds(emptyMessage).IntersectsWith(ValidationBounds(emptyClear));
            // #149：搜索无匹配空态只提供「清除搜索」，不与「清除筛选」同现（门禁反例方向）。
            var searchEmptyHasNoFilterClear = !ValidationDescendants<Button>(FloatingRows).Any(button =>
                string.Equals(button.Content as string, "清除筛选", StringComparison.Ordinal));

            ApplyTheme();
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
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

            SetQuickAddExpanded(true);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            emptyMessage = FindEmptyMessage();
            emptyClear = FindEmptyClear();
            var emptyState =
                GetFloatingRenderedNoteIds().Count == 0 &&
                emptyMessage is not null &&
                emptyClear is not null &&
                FloatingNoteEntry.IsVisible &&
                FloatingAddNote.IsEnabled;

            var addText = $"空态可新增-{Guid.NewGuid():N}";
            FloatingNoteText.Text = addText;
            FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var addedNote = _desktopWorkspace.Snapshot.Settings.Notes.FirstOrDefault(note =>
                string.Equals(note.Text, addText, StringComparison.Ordinal));
            // #117 起：快速新增提交成功后表单自动收起，以收起态与新增成功共同证明空态可新增。
            var emptyStateAddWorks =
                FloatingNoteEntry.Visibility == Visibility.Collapsed &&
                string.IsNullOrEmpty(FloatingNoteTextError.Text) &&
                string.IsNullOrEmpty(FloatingNoteDateError.Text) &&
                (addedNote is not null ||
                 (FloatingNoteMessage.Text ?? string.Empty).StartsWith("保存失败：", StringComparison.Ordinal));
            if (addedNote is not null)
            {
                _desktopWorkspace.Apply(new DeleteNoteChange(addedNote.Id));
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
            }

            FloatingNoteText.Text = originalNoteText;
            SetQuickAddExpanded(false);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();

            emptyClear = FindEmptyClear();
            emptyClear?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var clearRestores =
                string.IsNullOrEmpty(FloatingNoteSearch.Text) &&
                FloatingNoteSearchSlot.Visibility == Visibility.Collapsed &&
                GetFloatingRenderedNoteIds().SequenceEqual(
                    _desktopWorkspace.Snapshot.VisibleActiveNotes.Select(note => note.Id),
                    StringComparer.Ordinal);

            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(Dispatcher);
            WindowValidationInput.RaisePreviewKey(this, Key.F, ModifierKeys.Control);
            WindowValidationInput.Pump(Dispatcher);
            var ctrlFFromCountdowns =
                _workspace == WorkspaceKind.Notes &&
                FloatingNoteSearchInputRow.Visibility == Visibility.Visible &&
                ReferenceEquals(Keyboard.FocusedElement, FloatingNoteSearch);

            WindowValidationInput.RaisePreviewKey(this, Key.F, ModifierKeys.Control);
            WindowValidationInput.Pump(Dispatcher);
            var ctrlFKeepsFocusWhenExpanded =
                ReferenceEquals(Keyboard.FocusedElement, FloatingNoteSearch) &&
                FloatingNoteSearchInputRow.Visibility == Visibility.Visible;

            ClearNoteSearch();
            WindowValidationInput.Pump(Dispatcher);
            Keyboard.Focus(FloatingNotesButton);
            WindowValidationInput.Pump(Dispatcher);
            WindowValidationInput.RaisePreviewKey(this, Key.F, ModifierKeys.Control);
            WindowValidationInput.Pump(Dispatcher);
            var ctrlFFromNotes =
                _workspace == WorkspaceKind.Notes &&
                FloatingNoteSearchInputRow.Visibility == Visibility.Visible &&
                ReferenceEquals(Keyboard.FocusedElement, FloatingNoteSearch);

            Keyboard.Focus(FloatingNoteSearch);
            WindowValidationInput.Pump(Dispatcher);
            FloatingNoteSearch.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(Dispatcher);
            var firstCheckbox = ValidationDescendants<CheckBox>(FloatingRows).FirstOrDefault();
            var searchBeforeFirstCard =
                firstCheckbox is null ||
                ReferenceEquals(Keyboard.FocusedElement, firstCheckbox);

            ClearNoteSearch();
            WindowValidationInput.Pump(Dispatcher);
            _desktopWorkspace.ApplyFilter(originalFilter with
            {
                SelectedTagIds = Array.AsReadOnly(new[] { "__floating_filter_miss_tag__" })
            });
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var noteFilterEmptyMessages = FloatingRows.Children
                .OfType<TextBlock>()
                .Select(text => text.Text)
                .ToArray();
            // #149：筛选约束空态提供「清除筛选」且不出现「清除搜索」。
            var filterEmptyClear = ValidationDescendants<Button>(FloatingRows).FirstOrDefault(button =>
                string.Equals(button.Content as string, "清除筛选", StringComparison.Ordinal));
            var filterEmptyHasNoSearchClear = !ValidationDescendants<Button>(FloatingRows).Any(button =>
                string.Equals(button.Content as string, "清除搜索", StringComparison.Ordinal));
            var noteFilterEmptyStateDistinguished =
                noteFilterEmptyMessages.SequenceEqual(new[] { "没有匹配的便签。" }) &&
                filterEmptyClear is not null &&
                filterEmptyHasNoSearchClear &&
                FloatingNoteSearchSlot.Visibility == Visibility.Collapsed;

            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var countdownFilterEmptyMessages = FloatingRows.Children
                .OfType<TextBlock>()
                .Select(text => text.Text)
                .ToArray();
            var countdownFilterEmptyStateDistinguished =
                countdownFilterEmptyMessages.SequenceEqual(new[] { "没有匹配的倒计时。" });

            // #149：点击「清除筛选」还原到无约束便签列表（对已脱树按钮触发 Click 走同一处理）。
            filterEmptyClear?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var constrainedEmptyClearRestoresList =
                filterEmptyClear is not null &&
                GetFloatingRenderedNoteIds().SequenceEqual(
                    _desktopWorkspace.Snapshot.VisibleActiveNotes.Select(note => note.Id),
                    StringComparer.Ordinal) &&
                _desktopWorkspace.Snapshot.Filter.NoteStatus == NoteStatusFilter.All &&
                !_desktopWorkspace.Snapshot.Filter.UntaggedOnly &&
                _desktopWorkspace.Snapshot.Filter.NoteDate is null;
            var constrainedEmptyProvidesAction =
                searchEmptyHasNoFilterClear &&
                noteFilterEmptyStateDistinguished &&
                constrainedEmptyClearRestoresList;
            _desktopWorkspace.ApplyFilter(originalFilter);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();

            ApplyTheme();
            WindowValidationInput.Pump(Dispatcher);
            var usesQuickAddTheme = TextBoxUsesQuickAddTheme(FloatingNoteSearch);

            return new FloatingNoteSearchValidationScenarios(
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
        }
        finally
        {
            _searchExpanded = originalExpanded;
            FloatingNoteText.Text = originalNoteText;
            _desktopWorkspace.ApplyFilter(originalFilter);
            ApplyTheme();
            ShowWorkspace(originalWorkspace);
            if (FloatingNoteSearch.IsKeyboardFocusWithin)
            {
                Keyboard.Focus(FloatingNotesButton);
            }

            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
        }
    }

    private bool ProbeSearchButtonShowsClosedMagnifier()
    {
        if (FloatingSearchNotes.Visibility != Visibility.Visible)
        {
            return false;
        }

        FloatingSearchNotes.ApplyTemplate();
        FloatingSearchNotes.UpdateLayout();

        Ellipse? lens = null;
        Line? handle = null;
        if (FloatingSearchNotes.Content is Grid icon)
        {
            lens = icon.Children.OfType<Ellipse>().FirstOrDefault();
            handle = icon.Children.OfType<Line>().FirstOrDefault();
        }

        lens ??= ValidationDescendants<Ellipse>(FloatingSearchNotes).FirstOrDefault();
        handle ??= ValidationDescendants<Line>(FloatingSearchNotes).FirstOrDefault();
        if (lens is null || handle is null)
        {
            return false;
        }

        var contentWidth = FloatingSearchNotes.ActualWidth -
            FloatingSearchNotes.Padding.Left -
            FloatingSearchNotes.Padding.Right -
            FloatingSearchNotes.BorderThickness.Left -
            FloatingSearchNotes.BorderThickness.Right;
        var contentHeight = FloatingSearchNotes.ActualHeight -
            FloatingSearchNotes.Padding.Top -
            FloatingSearchNotes.Padding.Bottom -
            FloatingSearchNotes.BorderThickness.Top -
            FloatingSearchNotes.BorderThickness.Bottom;
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
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var originalPreferences = _desktopWorkspace.Snapshot.Settings.Preferences;
        var temporaryCountdownIds = new List<string>();
        var temporaryNoteIds = new List<string>();
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            var nowUtc = _desktopWorkspace.Snapshot.UtcNow;
            var precisionSaved = _desktopWorkspace.Apply(new SavePreferencesChange(
                "Second",
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd)).Succeeded;
            for (var index = 0; index < 2; index++)
            {
                var name = $"验证悬浮稳定刷新-{index}";
                var result = _desktopWorkspace.Apply(new SaveCountdownChange(
                    null,
                    name,
                    nowUtc.AddHours(2 + index),
                    Array.Empty<string>()));
                var added = _desktopWorkspace.Snapshot.Settings.Countdowns
                    .LastOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
                if (!result.Succeeded || added is null)
                {
                    return FloatingStableRefreshScenarios.Failed;
                }

                temporaryCountdownIds.Add(added.Id);
            }

            UpdateTime(nowUtc);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var countdownRowsBefore = FloatingRows.Children.Cast<FrameworkElement>().ToArray();
            var countdownTextsBefore = FloatingCountdownTexts.ToArray();
            var clusterNamesBefore = FloatingCountdownClusterNames();
            var rebuildBeforeTick = FloatingRowsRebuildGenerationForValidation;
            UpdateTime(nowUtc.AddSeconds(1));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var countdownRowsPreservedAndTextUpdatesInPlace =
                precisionSaved &&
                countdownRowsBefore.Length > 0 &&
                countdownRowsBefore.SequenceEqual(FloatingRows.Children.Cast<FrameworkElement>()) &&
                FloatingRowsRebuildGenerationForValidation == rebuildBeforeTick &&
                FloatingLastRenderWasInPlaceForValidation &&
                !countdownTextsBefore.SequenceEqual(FloatingCountdownTexts) &&
                !clusterNamesBefore.SequenceEqual(FloatingCountdownClusterNames());

            ShowWorkspace(WorkspaceKind.Notes);
            var today = _desktopWorkspace.Snapshot.BeijingNow.Date;
            string? noteB = null;
            foreach (var (suffix, date) in new[]
                     {
                         ("A", today.AddDays(1)),
                         ("B", today.AddDays(2)),
                         ("C", today.AddDays(3))
                     })
            {
                var text = $"验证悬浮稳定刷新便签-{suffix}-{Guid.NewGuid():N}"[..30];
                var result = _desktopWorkspace.Apply(new SaveNoteChange(
                    null,
                    text,
                    date,
                    false,
                    Array.Empty<string>(),
                    NoReminder.Instance));
                var added = _desktopWorkspace.Snapshot.Settings.Notes
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

            UpdateTime(nowUtc);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var noteCardsBefore = FloatingRows.Children.Cast<FrameworkElement>().ToArray();
            var completionB = ValidationDescendants<CheckBox>(FloatingRows).FirstOrDefault(checkBox =>
                string.Equals(checkBox.Tag as string, noteB, StringComparison.Ordinal));
            var triggerB = ValidationDescendants<Button>(FloatingRows).FirstOrDefault(button =>
                string.Equals(button.Tag as string, noteB, StringComparison.Ordinal));
            if (completionB is null || triggerB is null)
            {
                return FloatingStableRefreshScenarios.Failed;
            }

            Keyboard.Focus(triggerB);
            WindowValidationInput.Pump(Dispatcher);
            var focusLandedOnTrigger = ReferenceEquals(Keyboard.FocusedElement, triggerB);
            FloatingBodyScroller.ScrollToVerticalOffset(FloatingBodyScroller.ScrollableHeight);
            WindowValidationInput.Pump(Dispatcher);
            var offsetBeforeTicks = FloatingBodyScroller.VerticalOffset;
            for (var tick = 1; tick <= 3; tick++)
            {
                UpdateTime(nowUtc.AddMilliseconds(250 * tick));
                WindowValidationInput.Pump(Dispatcher);
            }

            UpdateLayout();
            var noteBUncompleted = _desktopWorkspace.Snapshot.Settings.Notes
                .FirstOrDefault(note => string.Equals(note.Id, noteB, StringComparison.Ordinal))
                is { IsCompleted: false };
            var focusStaysOnOverflowTriggerAcrossTicks =
                focusLandedOnTrigger &&
                ReferenceEquals(Keyboard.FocusedElement, triggerB) &&
                noteBUncompleted &&
                Math.Abs(FloatingBodyScroller.VerticalOffset - offsetBeforeTicks) <= 0.1;
            var noteCardInstancesPreservedAcrossTicks =
                noteCardsBefore.Length > 0 &&
                noteCardsBefore.SequenceEqual(FloatingRows.Children.Cast<FrameworkElement>()) &&
                ValidationDescendants<CheckBox>(FloatingRows).Any(checkBox =>
                    ReferenceEquals(checkBox, completionB));

            var offsetBeforeRebuild = FloatingBodyScroller.VerticalOffset;
            var rebuildGenerationBeforeAdd = FloatingRowsRebuildGenerationForValidation;
            var earlierText = $"验证悬浮稳定刷新便签-Z-{Guid.NewGuid():N}"[..30];
            var earlierResult = _desktopWorkspace.Apply(new SaveNoteChange(
                null,
                earlierText,
                today,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
            var earlierNote = _desktopWorkspace.Snapshot.Settings.Notes
                .LastOrDefault(item => string.Equals(item.Text, earlierText, StringComparison.Ordinal));
            if (earlierNote is not null)
            {
                temporaryNoteIds.Add(earlierNote.Id);
            }

            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var focusedAfterRebuild = Keyboard.FocusedElement;
            var rebuildRestoresFocusAndScrollByRole =
                earlierResult.Succeeded &&
                earlierNote is not null &&
                FloatingRowsRebuildGenerationForValidation > rebuildGenerationBeforeAdd &&
                focusedAfterRebuild is Button focusedButton &&
                string.Equals(focusedButton.Tag as string, noteB, StringComparison.Ordinal) &&
                string.Equals(
                    focusedButton.GetValue(NoteControlRoleProperty) as string,
                    NoteControlRoleOverflow,
                    StringComparison.Ordinal) &&
                !ReferenceEquals(focusedAfterRebuild, completionB) &&
                (offsetBeforeRebuild <= 0 ||
                 Math.Abs(FloatingBodyScroller.VerticalOffset - offsetBeforeRebuild) <= 1);

            var passBeforeHide = FloatingRowsRenderPassCountForValidation;
            var skippedBeforeHide = FloatingRowsSkippedRenderCountForValidation;
            var rebuildBeforeHide = FloatingRowsRebuildGenerationForValidation;
            Hide();
            WindowValidationInput.Pump(Dispatcher);
            UpdateTime(nowUtc.AddSeconds(3));
            UpdateTime(nowUtc.AddSeconds(4));
            WindowValidationInput.Pump(Dispatcher);
            var hiddenText = $"验证悬浮稳定刷新便签-H-{Guid.NewGuid():N}"[..30];
            var hiddenResult = _desktopWorkspace.Apply(new SaveNoteChange(
                null,
                hiddenText,
                today,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
            var hiddenNote = _desktopWorkspace.Snapshot.Settings.Notes
                .LastOrDefault(item => string.Equals(item.Text, hiddenText, StringComparison.Ordinal));
            if (hiddenNote is not null)
            {
                temporaryNoteIds.Add(hiddenNote.Id);
            }

            WindowValidationInput.Pump(Dispatcher);
            var hiddenSkipsRender =
                FloatingRowsSkippedRenderCountForValidation > skippedBeforeHide &&
                FloatingRowsRenderPassCountForValidation == passBeforeHide &&
                FloatingRowsRebuildGenerationForValidation == rebuildBeforeHide &&
                hiddenNote is not null &&
                !GetFloatingRenderedNoteIds().Contains(hiddenNote.Id, StringComparer.Ordinal);
            Show();
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var hiddenSkipsRenderUntilVisible =
                hiddenResult.Succeeded &&
                hiddenSkipsRender &&
                FloatingRowsRenderPassCountForValidation > passBeforeHide &&
                FloatingRowsRebuildGenerationForValidation > rebuildBeforeHide &&
                hiddenNote is not null &&
                GetFloatingRenderedNoteIds().Contains(hiddenNote.Id, StringComparer.Ordinal);

            var passBeforeMinimize = FloatingRowsRenderPassCountForValidation;
            var skippedBeforeMinimize = FloatingRowsSkippedRenderCountForValidation;
            WindowState = WindowState.Minimized;
            WindowValidationInput.Pump(Dispatcher);
            UpdateTime(nowUtc.AddSeconds(5));
            UpdateTime(nowUtc.AddSeconds(6));
            WindowValidationInput.Pump(Dispatcher);
            var minimizedSkipsRender =
                FloatingRowsSkippedRenderCountForValidation > skippedBeforeMinimize &&
                FloatingRowsRenderPassCountForValidation == passBeforeMinimize;
            WindowState = WindowState.Normal;
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var minimizedSkipsRenderUntilRestored =
                minimizedSkipsRender &&
                FloatingRowsRenderPassCountForValidation > passBeforeMinimize;

            return new FloatingStableRefreshScenarios(
                focusStaysOnOverflowTriggerAcrossTicks,
                countdownRowsPreservedAndTextUpdatesInPlace,
                noteCardInstancesPreservedAcrossTicks,
                rebuildRestoresFocusAndScrollByRole,
                hiddenSkipsRenderUntilVisible,
                minimizedSkipsRenderUntilRestored);
        }
        finally
        {
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }

            if (!IsVisible)
            {
                Show();
            }

            _desktopWorkspace.ApplyFilter(originalFilter);
            _desktopWorkspace.Apply(new SavePreferencesChange(
                originalPreferences.DisplayPrecision,
                originalPreferences.MonthlySalary,
                originalPreferences.WorkStart,
                originalPreferences.WorkEnd));
            foreach (var id in temporaryCountdownIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _desktopWorkspace.Apply(new DeleteCountdownChange(id));
            }

            foreach (var id in temporaryNoteIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _desktopWorkspace.Apply(new DeleteNoteChange(id));
            }

            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private string[] FloatingCountdownClusterNames() =>
        ValidationDescendants<Grid>(FloatingRows)
            .Where(grid => grid.Tag is string tag &&
                string.Equals(tag, RemainingBlocksTag, StringComparison.Ordinal))
            .Select(grid => AutomationProperties.GetName(grid) ?? string.Empty)
            .ToArray();

    /// <summary>
    ///     #117 悬浮窗便签底部命令区与卡片新视觉探针：默认收起的“＋ 添加便签”、“设置”打开主窗口、
    ///     “…”菜单编辑/删除、日期印记 pastel 轮换、卡底交替与手写体装饰。
    /// </summary>
    private FloatingBottomCommandScenarios ProbeBottomCommandScenarios()
    {
        var originalWorkspace = _workspace;
        var originalFilter = _desktopWorkspace.Snapshot.Filter;
        var createdNoteIds = new List<string>();
        var createdTagIds = new List<string>();
        var managerWindow = _manager as Window;
        var managerWasVisible = managerWindow is { IsVisible: true };
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var countdownsHideCommandArea =
                FloatingNotesCommandArea.Visibility == Visibility.Collapsed &&
                !FloatingAddNoteToggle.IsVisible &&
                !FloatingSearchNotes.IsVisible &&
                !FloatingOpenSettings.IsVisible &&
                !FloatingNotesDecoText.IsVisible;

            ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var defaultCollapsedInNotes =
                FloatingNotesCommandArea.Visibility == Visibility.Visible &&
                FloatingAddNoteToggle.IsVisible &&
                FloatingOpenSettings.IsVisible &&
                FloatingSearchNotes.IsVisible &&
                string.Equals(
                    AutomationProperties.GetName(FloatingSearchNotes),
                    "搜索便签",
                    StringComparison.Ordinal) &&
                FloatingNoteEntry.Visibility == Visibility.Collapsed;

            var deco = FloatingNotesDecoText;
            var decoLeft = FloatingNotesDecoTextLeft;
            // #149：520 外壳上的手写体保持 13 DIP（相对 17 设计坐标的外壳例外，DESIGN §4）。
            var handwrittenDecorationLands =
                deco.IsVisible &&
                decoLeft.IsVisible &&
                !deco.IsHitTestVisible &&
                !decoLeft.IsHitTestVisible &&
                !deco.Focusable &&
                !decoLeft.Focusable &&
                ReferenceEquals(deco.FontFamily, FindResource("HandwrittenDecoFontFamily")) &&
                ReferenceEquals(deco.Foreground, FindResource("DecoTextSoftBrush")) &&
                ReferenceEquals(decoLeft.FontFamily, FindResource("HandwrittenDecoFontFamily")) &&
                ReferenceEquals(decoLeft.Foreground, FindResource("DecoTextSoftBrush")) &&
                Math.Abs(deco.FontSize - 13) <= 0.01 &&
                Math.Abs(decoLeft.FontSize - 13) <= 0.01;

            EnsureProbeNotes(createdNoteIds, 3);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var cards = FloatingRows.Children.OfType<Border>()
                .Where(card => Math.Abs(card.Width - CalendarIndexNoteCardWidth) <= 0.5)
                .ToList();
            var stampPaletteKeys = new[] { "StampPinkBrush", "StampBlueBrush", "StampGreenBrush" };
            var pastelStampsRotate =
                cards.Count >= 3 &&
                cards.Select((card, index) => (card, index)).All(pair =>
                    BrushMatchesResource(
                        FindNoteCardDateStamp(pair.card)?.Background,
                        TryFindResource(stampPaletteKeys[pair.index % stampPaletteKeys.Length])));
            var cardSurfacesAlternate =
                cards.Count >= 3 &&
                cards.Select((card, index) => (card, index)).All(pair =>
                    ReferenceEquals(
                        pair.card.Background,
                        pair.index % 2 == 0
                            ? _floatingNoteCardBrush
                            : TryFindResource("NoteWhiteBrush")));

            var tagResult = _desktopWorkspace.Apply(new CreateTagChange(
                $"悬浮探针-{Guid.NewGuid():N}"[..17],
                "#F4B6A6"));
            var toggleExpandsWithFilterTags = false;
            var toggleCollapses = false;
            if (tagResult.Succeeded && tagResult.CreatedTag is { } createdTag)
            {
                createdTagIds.Add(createdTag.Id);
                _desktopWorkspace.ApplyFilter(originalFilter with
                {
                    SelectedTagIds = Array.AsReadOnly(new[] { createdTag.Id })
                });
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
                FloatingAddNoteToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
                toggleExpandsWithFilterTags =
                    FloatingNoteEntry.IsVisible &&
                    FloatingNotesCommandBar.Visibility == Visibility.Visible &&
                    FloatingSearchNotes.IsVisible &&
                    FloatingSearchNotes.IsTabStop &&
                    FloatingOpenSettings.IsVisible &&
                    FloatingOpenSettings.IsTabStop &&
                    ValidationBounds(FloatingNotesCommandScroller).Bottom <= ActualHeight + 0.5 &&
                    ValidationBounds(FloatingNoteEntry).Top >=
                        ValidationBounds(FloatingNotesCommandScroller).Top - 0.5 &&
                    ValidationBounds(FloatingNoteEntry).Top <=
                        ValidationBounds(FloatingNotesCommandScroller).Bottom + 0.5 &&
                    FloatingTags.SelectedTagIds.SequenceEqual(
                        new[] { createdTag.Id },
                        StringComparer.Ordinal) &&
                    FloatingNoteText.IsEnabled &&
                    FloatingNoteDate.IsEnabled;

                FloatingAddNoteToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
                toggleCollapses =
                    FloatingNoteEntry.Visibility == Visibility.Collapsed &&
                    FloatingNotesCommandBar.Visibility == Visibility.Visible;
            }

            // #149：快速新增正文占位文与管理窗口一致；空白时可见、输入后隐藏。
            var quickAddPlaceholder = FindName("FloatingNoteTextPlaceholder") as TextBlock;
            var quickAddPlaceholderMirrorsManager =
                quickAddPlaceholder is not null &&
                string.Equals(
                    quickAddPlaceholder.Text,
                    "写下要记住的事情吧…",
                    StringComparison.Ordinal) &&
                quickAddPlaceholder.Visibility == Visibility.Visible;
            SetQuickAddExpanded(true);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            FloatingNoteText.Text = "占位隐藏探针";
            WindowValidationInput.Pump(Dispatcher);
            quickAddPlaceholderMirrorsManager =
                quickAddPlaceholderMirrorsManager &&
                quickAddPlaceholder is not null &&
                quickAddPlaceholder.Visibility == Visibility.Collapsed;
            FloatingNoteText.Text = string.Empty;
            WindowValidationInput.Pump(Dispatcher);
            // #149：展开态底部切换钮为次级「收起」，表单「添加」是唯一实心粉钮。
            var quickAddToggleSecondaryWhenExpanded =
                FloatingNoteEntry.IsVisible &&
                ReferenceEquals(FloatingAddNoteToggle.Style, FindResource("SecondaryButton")) &&
                string.Equals(FloatingAddNoteToggle.Content as string, "收起", StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(FloatingAddNoteToggle),
                    "收起快速新增便签表单",
                    StringComparison.Ordinal) &&
                ReferenceEquals(FloatingAddNote.Style, FindResource("PrimaryButton"));
            SetQuickAddExpanded(false);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            quickAddToggleSecondaryWhenExpanded =
                quickAddToggleSecondaryWhenExpanded &&
                ReferenceEquals(FloatingAddNoteToggle.Style, FindResource("FloatingAccentButton")) &&
                FloatingAddNoteToggle.Content is StackPanel &&
                string.Equals(
                    AutomationProperties.GetName(FloatingAddNoteToggle),
                    "添加便签",
                    StringComparison.Ordinal);

            SetQuickAddExpanded(true);
            WindowValidationInput.Pump(Dispatcher);
            var addText = $"悬浮底部命令探针便签-{Guid.NewGuid():N}";
            FloatingNoteText.Text = addText;
            FloatingAddNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var addedNote = _desktopWorkspace.Snapshot.Settings.Notes.FirstOrDefault(note =>
                string.Equals(note.Text, addText, StringComparison.Ordinal));
            if (addedNote is not null)
            {
                createdNoteIds.Add(addedNote.Id);
            }

            var submitCollapsesAfterSuccess =
                addedNote is not null &&
                FloatingNoteEntry.Visibility == Visibility.Collapsed;

            var noteMenuProvidesEdit = false;
            var noteMenuDeleteConfirms = false;
            var noteMenuKeyboardLifecycle = false;
            if (addedNote is not null)
            {
                var trigger = ValidationDescendants<Button>(FloatingRows).FirstOrDefault(button =>
                    string.Equals(button.Tag as string, addedNote.Id, StringComparison.Ordinal));
                if (trigger is not null)
                {
                    var popup = NoteRowOverflowMenuBehavior.GetPopup(trigger);
                    var menuEdit = popup?.Child is { } keyboardRoot
                        ? ValidationDescendants<Button>(keyboardRoot).FirstOrDefault(button =>
                            string.Equals(button.Content as string, "编辑", StringComparison.Ordinal))
                        : null;
                    // #149：先走键盘路径——打开聚焦「编辑」，Tab 进入「删除」，Esc 关闭并回到触发器。
                    if (popup is not null && menuEdit is not null)
                    {
                        noteMenuKeyboardLifecycle = ProbeNoteOverflowMenuKeyboard(
                            trigger,
                            popup,
                            menuEdit);
                    }

                    trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    WindowValidationInput.Pump(Dispatcher);
                    if (popup is { IsOpen: true } && menuEdit is not null)
                    {
                        menuEdit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        WindowValidationInput.Pump(Dispatcher);
                        UpdateLayout();
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
                            WindowValidationInput.Pump(Dispatcher);
                        }

                        noteMenuProvidesEdit = noteMenuProvidesEdit && !popup.IsOpen;
                    }

                    if (popup is not null)
                    {
                        noteMenuDeleteConfirms = ProbeNoteOverflowMenuDelete(
                            trigger,
                            popup,
                            addedNote.Id);
                    }
                }
            }

            var settingsOpensManagerPreservingWorkspace = false;
            if (managerWindow is not null)
            {
                if (managerWasVisible)
                {
                    managerWindow.Hide();
                    WindowValidationInput.Pump(Dispatcher);
                }

                FloatingOpenSettings.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
                settingsOpensManagerPreservingWorkspace =
                    managerWindow.IsVisible &&
                    managerWindow.WindowState == WindowState.Normal &&
                    _workspace == WorkspaceKind.Notes;
                managerWindow.Hide();
                WindowValidationInput.Pump(Dispatcher);
            }

            var collapsedCommandBarBounds = ValidationBounds(FloatingNotesCommandBar);
            var collapsedListOffset = FloatingBodyScroller.VerticalOffset;
            var expandedQuickAddScrollable = false;
            SetQuickAddExpanded(true);
            WindowValidationInput.Pump(Dispatcher);
            FloatingNoteText.Text = new string('敏', 60);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            var expandedScrollerBounds = ValidationBounds(FloatingNotesCommandScroller);
            if (expandedScrollerBounds.Bottom <= ActualHeight + 0.5 &&
                FloatingNotesCommandScroller.ScrollableHeight > 0.5)
            {
                FloatingNotesCommandScroller.ScrollToBottom();
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
                var scrollerViewportBottom = ValidationBounds(FloatingNotesCommandScroller).Bottom;
                var scrolledBarBounds = ValidationBounds(FloatingNotesCommandBar);
                var commandBarReachableAtBottom =
                    FloatingNotesCommandScroller.VerticalOffset >=
                        FloatingNotesCommandScroller.ScrollableHeight - 0.5 &&
                    scrolledBarBounds.Bottom <= scrollerViewportBottom + 0.5 &&
                    scrolledBarBounds.Bottom >= scrollerViewportBottom - 8 &&
                    ValidationBounds(FloatingAddNote).Bottom <= scrollerViewportBottom + 0.5;

                FloatingNotesCommandScroller.ScrollToHome();
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
                var noteTextTop = ValidationBounds(FloatingNoteText).Top;
                var entryTopAfterHome = ValidationBounds(FloatingNoteEntry).Top;
                var entryRestoredToTop =
                    FloatingNotesCommandScroller.VerticalOffset <= 0.5 &&
                    Math.Abs(entryTopAfterHome - expandedScrollerBounds.Top) <= 0.5 &&
                    noteTextTop >= expandedScrollerBounds.Top - 0.5 &&
                    noteTextTop <= expandedScrollerBounds.Top + 24;

                SetQuickAddExpanded(false);
                FloatingNoteText.Text = string.Empty;
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
                var restoredBarBounds = ValidationBounds(FloatingNotesCommandBar);
                expandedQuickAddScrollable =
                    commandBarReachableAtBottom &&
                    entryRestoredToTop &&
                    Math.Abs(restoredBarBounds.X - collapsedCommandBarBounds.X) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Y - collapsedCommandBarBounds.Y) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Width - collapsedCommandBarBounds.Width) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Height - collapsedCommandBarBounds.Height) <= 0.5 &&
                    Math.Abs(FloatingBodyScroller.VerticalOffset - collapsedListOffset) <= 0.5 &&
                    FloatingNotesCommandScroller.VerticalOffset <= 0.5;
            }
            else
            {
                SetQuickAddExpanded(false);
                FloatingNoteText.Text = string.Empty;
                WindowValidationInput.Pump(Dispatcher);
                UpdateLayout();
            }

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
                noteMenuKeyboardLifecycle);
        }
        finally
        {
            foreach (var id in createdNoteIds)
            {
                _desktopWorkspace.Apply(new DeleteNoteChange(id));
            }

            _desktopWorkspace.ApplyFilter(originalFilter);
            foreach (var id in createdTagIds)
            {
                _desktopWorkspace.Apply(new DeleteTagChange(id));
            }

            ShowWorkspace(originalWorkspace);
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            if (managerWindow is not null && managerWasVisible)
            {
                managerWindow.Show();
                managerWindow.Activate();
                WindowValidationInput.Pump(Dispatcher);
            }
        }
    }

    /// <summary>“…”菜单删除：确认弹层取消则保留便签，确认则删除；弹层经 Dispatcher 队列驱动。</summary>
    private bool ProbeNoteOverflowMenuDelete(Button trigger, Popup popup, string noteId)
    {
        bool OpenMenuAndDriveDialog(Func<ConfirmationDialog?, bool> drive)
        {
            if (!popup.IsOpen)
            {
                trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(Dispatcher);
            }

            if (!popup.IsOpen)
            {
                return false;
            }

            ConfirmationDialog? dialog = null;
            ValidationExercises.QueueConfirmationDialogAction(this, visibleDialog =>
            {
                dialog = visibleDialog;
                return drive(dialog);
            });
            var menuDelete = ValidationDescendants<Button>(popup.Child).FirstOrDefault(button =>
                string.Equals(button.Content as string, "删除", StringComparison.Ordinal));
            menuDelete?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            UpdateLayout();
            return dialog is not null;
        }

        var cancelled = OpenMenuAndDriveDialog(dialog =>
        {
            return WindowValidationInput.InvokeButton(dialog!.CancelButton);
        });
        var noteStillExistsAfterCancel = _desktopWorkspace.Snapshot.Settings.Notes.Any(note =>
            string.Equals(note.Id, noteId, StringComparison.Ordinal));
        var confirmed = OpenMenuAndDriveDialog(dialog =>
        {
            return WindowValidationInput.InvokeButton(dialog!.ConfirmButton);
        });
        var deleted = cancelled &&
            noteStillExistsAfterCancel &&
            confirmed &&
            !_desktopWorkspace.Snapshot.Settings.Notes.Any(note =>
                string.Equals(note.Id, noteId, StringComparison.Ordinal));
        // #214：就地撤销是瞬时状态，布局探针继续测量收起后的命令区。
        DismissDeleteUndoForValidation();
        return deleted;
    }

    /// <summary>
    ///     #149：悬浮便签 `…` 菜单键盘生命周期——打开聚焦首项「编辑」，Tab 进入「删除」，
    ///     Esc 关闭并把焦点还给触发按钮（与筛选 / 标签选择器同一套弹层焦点生命周期）。
    /// </summary>
    private bool ProbeNoteOverflowMenuKeyboard(Button trigger, Popup popup, Button menuEdit)
    {
        try
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
            Window.GetWindow(trigger)?.Activate();
            Keyboard.Focus(trigger);
            WindowValidationInput.Pump(Dispatcher);
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(Dispatcher);
            WindowValidationInput.Pump(Dispatcher);
            var openedWithFocusOnEdit =
                popup.IsOpen &&
                ReferenceEquals(Keyboard.FocusedElement, menuEdit);
            menuEdit.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(Dispatcher);
            var deleteButton = popup.Child is { } root
                ? ValidationDescendants<Button>(root).FirstOrDefault(button =>
                    string.Equals(button.Content as string, "删除", StringComparison.Ordinal))
                : null;
            var tabReachesDelete =
                deleteButton is not null &&
                ReferenceEquals(Keyboard.FocusedElement, deleteButton);
            var escapeHandled = deleteButton is not null &&
                WindowValidationInput.RaisePreviewKey(deleteButton, Key.Escape);
            WindowValidationInput.Pump(Dispatcher);
            var closedBackToTrigger =
                escapeHandled &&
                WindowValidationInput.WaitForPopupClosed(popup) &&
                (trigger.IsKeyboardFocusWithin ||
                 ReferenceEquals(FocusManager.GetFocusedElement(this), trigger));
            return openedWithFocusOnEdit && tabReachesDelete && closedBackToTrigger;
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
        }
    }

    private void EnsureProbeNotes(List<string> createdIds, int minimumVisible)
    {
        while (_desktopWorkspace.Snapshot.VisibleActiveNotes.Count < minimumVisible)
        {
            var result = _desktopWorkspace.Apply(new SaveNoteChange(
                null,
                $"悬浮底部命令探针-{Guid.NewGuid():N}",
                _desktopWorkspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                NoReminder.Instance));
            if (!result.Succeeded)
            {
                return;
            }

            var note = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(candidate =>
                candidate.Text.StartsWith("悬浮底部命令探针-", StringComparison.Ordinal));
            if (note is null)
            {
                return;
            }

            createdIds.Add(note.Id);
        }
    }

    internal static Border? FindNoteCardDateStamp(Border card) =>
        ValidationDescendants<Border>(card).FirstOrDefault(border =>
            Math.Abs(border.ActualWidth - 48) <= 0.5);

    private static bool BrushMatchesResource(Brush? actual, object? expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush actualBrush &&
         expected is SolidColorBrush expectedBrush &&
         actualBrush.Color == expectedBrush.Color);

    private Rect ValidationBounds(FrameworkElement element) =>
        element.TransformToAncestor(this).TransformBounds(
            new Rect(new Point(0, 0), element.RenderSize));

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
            // 字段错误需在真实可见表单中聚焦来源控件，展开快速新增表单后验证。
            SetQuickAddExpanded(true);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);
            var datePickerKeyboardLifecycleWorks =
                WindowValidationInput.ExerciseDatePicker(FloatingNoteDate);

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
            FloatingBodyScroller.ScrollToVerticalOffset(originalNotesScrollOffset);
            WindowValidationInput.Pump(Dispatcher);
            ShowWorkspace(originalWorkspace);
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
    MainUiPolishScenarios UiPolish,
    SettingsDraftRetentionProbe SettingsDrafts,
    UndatedNoteEditProbe UndatedNoteEdit,
    bool CountdownReminderModeMappingWorks,
    MainCountdownReadabilityScenarios CountdownReadability,
    CountdownEditingScenarios CountdownEditing,
    NoteValidationScenarios NoteValidation,
    MainWindowInteractionStates InteractionStates);

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
    FloatingBottomCommandScenarios BottomCommand);

/// <summary>#212：渲染签名稳定刷新探针结果，语义见 DESIGN §10「渲染签名稳定刷新」。</summary>
internal sealed record FloatingStableRefreshScenarios(
    bool FocusStaysOnOverflowTriggerAcrossTicks,
    bool CountdownRowsPreservedAndTextUpdatesInPlace,
    bool NoteCardInstancesPreservedAcrossTicks,
    bool RebuildRestoresFocusAndScrollByRole,
    bool HiddenSkipsRenderUntilVisible,
    bool MinimizedSkipsRenderUntilRestored)
{
    public bool AllPassed =>
        FocusStaysOnOverflowTriggerAcrossTicks &&
        CountdownRowsPreservedAndTextUpdatesInPlace &&
        NoteCardInstancesPreservedAcrossTicks &&
        RebuildRestoresFocusAndScrollByRole &&
        HiddenSkipsRenderUntilVisible &&
        MinimizedSkipsRenderUntilRestored;

    public static FloatingStableRefreshScenarios Failed { get; } = new(
        false, false, false, false, false, false);
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
    bool NoteMenuKeyboardLifecycle)
{
    public bool QuickAddTogglePass =>
        DefaultCollapsedInNotes &&
        ToggleExpandsWithFilterTags &&
        ToggleCollapses &&
        SubmitCollapsesAfterSuccess &&
        ExpandedQuickAddScrollable &&
        QuickAddToggleSecondaryWhenExpanded;

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
        QuickAddPlaceholderMirrorsManager;
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
    bool OverflowMenuKeyboardLifecycleWorks,
    bool CardSpacingConsumesTokens,
    bool FilterPopupTabCyclesInsideLayer,
    bool TagManagerPopupTabCyclesInsideLayer,
    bool TagSelectorPopupTabCyclesInsideLayer,
    bool OverflowMenuPopupTabCyclesInsideLayer);

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
    bool TabsRenderAboveDecorationBand);

internal sealed record CountdownIncrementalRefreshProbe(
    bool IdentityPreserved,
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
        false, false, false, false, false, false, false, false, false, false);
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

internal static class WindowValidationInput
{
    public static bool ClickForFocusValidation(Control target)
    {
        target.BringIntoView();
        Pump(target.Dispatcher);
        using var pointer = new PickerPointerState();
        var pressed = RaisePickerPointer(target, UIElement.PreviewMouseLeftButtonDownEvent);
        Pump(target.Dispatcher);
        var focusedOnPress = target.IsKeyboardFocusWithin;
        var released = RaisePickerPointer(target, UIElement.PreviewMouseLeftButtonUpEvent);
        Pump(target.Dispatcher);
        return pressed && released && focusedOnPress;
    }

    // Hygiene bound for native popup HWND teardown only. The logical close keeps
    // the interactive 300 ms budget; the compositor-synchronized hide that trails
    // it is environment-dominated and must not race that budget.
    private const int TeardownTimeoutMilliseconds = 1000;

    public static bool WaitUntil(Dispatcher dispatcher, Func<bool> condition, int timeoutMilliseconds = 300)
    {
        dispatcher.VerifyAccess();
        if (timeoutMilliseconds <= 0)
        {
            return condition();
        }

        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var observed = false;
        Exception? failure = null;
        var frameRequestCount = 0;
        var renderingCount = 0;
        var observedMilliseconds = -1d;
        var operationStarts = new Dictionary<DispatcherOperation, double>();
        var longestOperationMilliseconds = 0d;
        var longestOperationStartedMilliseconds = 0d;
        var longestOperationPriority = DispatcherPriority.Inactive;
        void OnOperationStarted(object? sender, DispatcherHookEventArgs args) =>
            operationStarts[args.Operation] = watch.Elapsed.TotalMilliseconds;
        void OnOperationCompleted(object? sender, DispatcherHookEventArgs args)
        {
            if (!operationStarts.Remove(args.Operation, out var started)) return;
            var elapsed = watch.Elapsed.TotalMilliseconds - started;
            if (elapsed <= longestOperationMilliseconds) return;
            longestOperationMilliseconds = elapsed;
            longestOperationStartedMilliseconds = started;
            longestOperationPriority = args.Operation.Priority;
        }
        var timeout = new DispatcherTimer(DispatcherPriority.Send, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(timeoutMilliseconds)
        };
        // Observe at the same Send priority as the deadline: Background ticks
        // can starve behind a long Normal-priority popup teardown and never
        // witness a condition that became true inside the window.
        var frameRequests = new DispatcherTimer(DispatcherPriority.Send, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        void Observe(bool final = false)
        {
            // A tick executing at or past the deadline is the last observation
            // opportunity, so check the condition instead of stopping blind;
            // a genuinely late condition still fails that final check.
            if (!final && watch.Elapsed.TotalMilliseconds > timeoutMilliseconds)
            {
                Observe(final: true);
                frame.Continue = false;
                return;
            }

            try
            {
                if (condition())
                {
                    observed = true;
                    observedMilliseconds = watch.Elapsed.TotalMilliseconds;
                    frame.Continue = false;
                }
            }
            catch (Exception error)
            {
                failure = error;
                frame.Continue = false;
            }
        }

        void OnRendering(object? sender, EventArgs args)
        {
            // Keep the observer for this bounded wait. Removing it inside the
            // last animation tick makes MediaContext leave presentation and
            // synchronously wait before queued native popup cleanup can run.
            renderingCount++;
            Observe();
        }
        EventHandler rendering = OnRendering;
        EventHandler requestFrame = (_, _) =>
        {
            // Also observe between frames: WPF defers popup HWND destruction
            // until after IsOpen=false, and it need not produce another frame.
            Observe();
            if (!frame.Continue) return;

            // Adding Rendering posts a real WPF render request. Invalidations
            // can merge on the isolated desktop; a bitmap does not tick clocks.
            frameRequestCount++;
            CompositionTarget.Rendering -= rendering;
            CompositionTarget.Rendering += rendering;
        };
        EventHandler expired = (_, _) =>
        {
            Observe(final: true);
            frame.Continue = false;
        };
        try
        {
            dispatcher.Hooks.OperationStarted += OnOperationStarted;
            dispatcher.Hooks.OperationCompleted += OnOperationCompleted;
            dispatcher.Hooks.OperationAborted += OnOperationCompleted;
            timeout.Tick += expired;
            frameRequests.Tick += requestFrame;
            timeout.Start();
            frameRequests.Start();
            requestFrame(null, EventArgs.Empty);
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            dispatcher.Hooks.OperationStarted -= OnOperationStarted;
            dispatcher.Hooks.OperationCompleted -= OnOperationCompleted;
            dispatcher.Hooks.OperationAborted -= OnOperationCompleted;
            timeout.Stop();
            timeout.Tick -= expired;
            frameRequests.Stop();
            frameRequests.Tick -= requestFrame;
            CompositionTarget.Rendering -= rendering;
        }

        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        // The Send-priority deadline tick is the operative deadline: when
        // dispatcher starvation delays observers, the final check at that tick
        // decides, and observedMilliseconds keeps the honest witness time.
        var passed = observed;
        if (!passed)
        {
            Console.Error.WriteLine("VALIDATION_RENDER_WAIT_DIAGNOSTIC " + System.Text.Json.JsonSerializer.Serialize(new
            {
                timeoutMilliseconds, observed, observedMilliseconds,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                frameRequestCount, renderingCount,
                longestOperationMilliseconds, longestOperationStartedMilliseconds,
                longestOperationPriority = longestOperationPriority.ToString()
            }));
        }
        return passed;
    }

    // #205 regression: raising Click alone bypasses ButtonBase.OnClick and its
    // command source. UIA Invoke follows the same command path as a real click.
    public static bool InvokeButton(Button button)
    {
        if (UIElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke)
            is not IInvokeProvider provider)
        {
            return false;
        }

        provider.Invoke();
        return true;
    }

    /// <summary>
    /// Waits for the popup's own close path to complete without changing its state.
    /// Callers use this only as an observation before an assertion; fixture cleanup
    /// belongs in <c>ClosePopupForCleanup</c> from setup/finally blocks.
    /// </summary>
    public static bool WaitForPopupClosed(Popup popup, int timeoutMilliseconds = 300)
    {
        // The interactive contract is the logical close. Native HWND teardown
        // trails it: Popup's hide synchronizes with the compositor, and on the
        // isolated BelowNormal desktop that sync alone can approach the
        // interactive budget. Resource reclamation therefore gets its own
        // hygiene bound, which still fails a genuinely stuck teardown (the
        // close-after-deadline fixture never reaches it).
        var logicallyClosed = WaitUntil(popup.Dispatcher, () => !popup.IsOpen, timeoutMilliseconds);
        var detached = logicallyClosed && WaitUntil(popup.Dispatcher, IsSourceDetached, TeardownTimeoutMilliseconds);
        var closed = logicallyClosed && detached;
        if (!closed)
        {
            Console.Error.WriteLine("POPUP_CLOSE_WAIT_DIAGNOSTIC " + System.Text.Json.JsonSerializer.Serialize(new
            {
                popup.IsOpen,
                sourceAttached = popup.Child is Visual child && PresentationSource.FromVisual(child) is not null,
                childType = popup.Child?.GetType().Name,
                transition = PopupMotionBehavior.DescribeTransitionForValidation(popup)
            }));
        }
        return closed;

        bool IsSourceDetached() =>
            popup.Child is not Visual visual || PresentationSource.FromVisual(visual) is null;
    }

    /// <summary>
    /// Restores a popup fixture after a scenario. This is deliberately separate
    /// from <see cref="WaitForPopupClosed"/> so cleanup cannot turn a failed
    /// close assertion into a passing result.
    /// </summary>
    public static void ClosePopupForCleanup(Popup popup)
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
        }

        if (!WaitForPopupClosed(popup))
        {
            throw new InvalidOperationException(
                "Popup fixture did not finish closing within its close budget (300 ms interactive + teardown).");
        }
    }

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
        var triggerSecondClickCloses = false;
        var escapeFromTriggerClosesAndKeepsFocus = false;
        var popupOpened = false;
        var focusEntered = false;
        var escapeHandled = false;
        var popupClosedAfterEscape = false;
        var triggerFocused = false;
        try
        {
            ClosePopupForCleanup(popup);
            Window.GetWindow(trigger)?.Activate();
            Keyboard.Focus(trigger);
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            popupOpened = popup.IsOpen;
            focusEntered = OwnsKeyboardFocus(initialFocus);
            escapeHandled = initialFocus is UIElement input &&
                RaisePreviewKey(input, Key.Escape);
            Pump(popup.Dispatcher);
            popupClosedAfterEscape = WaitForPopupClosed(popup);
            triggerFocused = trigger.IsKeyboardFocusWithin ||
                (Window.GetWindow(trigger) is { } triggerWindow &&
                 ReferenceEquals(FocusManager.GetFocusedElement(triggerWindow), trigger));
            escapeReturnsFocus =
                popupOpened &&
                focusEntered &&
                escapeHandled &&
                popupClosedAfterEscape &&
                triggerFocused;

            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            var openedForSecondClick = popup.IsOpen;
            Keyboard.Focus(trigger);
            Pump(popup.Dispatcher);
            trigger.RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                Environment.TickCount,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
            });
            Pump(popup.Dispatcher);
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            var closeBefore = PopupMotionBehavior.DescribeTransitionForValidation(popup);
            triggerSecondClickCloses = openedForSecondClick && WaitForPopupClosed(popup);
            if (!triggerSecondClickCloses)
            {
                Console.Error.WriteLine($"POPUP_SECOND_CLICK_DIAGNOSTIC trigger={trigger.Name}, opened={openedForSecondClick}, before={closeBefore}, after={PopupMotionBehavior.DescribeTransitionForValidation(popup)}");
            }

            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            var openedForTriggerEscape = popup.IsOpen;
            Keyboard.Focus(trigger);
            Pump(popup.Dispatcher);
            var triggerEscapeHandled = RaisePreviewKey(trigger, Key.Escape);
            Pump(popup.Dispatcher);
            var triggerEscapeClosed = WaitForPopupClosed(popup);
            escapeFromTriggerClosesAndKeepsFocus =
                openedForTriggerEscape &&
                triggerEscapeHandled &&
                triggerEscapeClosed &&
                (trigger.IsKeyboardFocusWithin ||
                 (Window.GetWindow(trigger) is { } triggerWindowAfterEscape &&
                  ReferenceEquals(FocusManager.GetFocusedElement(triggerWindowAfterEscape), trigger)));

            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            Keyboard.Focus(outsideFocus);
            Pump(popup.Dispatcher);
            outsideFocusPreserved =
                popup.IsOpen && outsideFocus.IsKeyboardFocusWithin;
        }
        finally
        {
            ClosePopupForCleanup(popup);
        }

        return new PopupFocusExerciseResult(
            escapeReturnsFocus,
            outsideFocusPreserved,
            triggerSecondClickCloses,
            escapeFromTriggerClosesAndKeepsFocus);
    }

    public static bool ExerciseDatePicker(DatePicker picker) => ExerciseDatePicker(picker, out _);

    public static bool ExerciseDatePicker(DatePicker picker, out string diagnostic)
    {
        diagnostic = "not-exercised";
        var owner = Window.GetWindow(picker);
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            ClosePickerForFixture(picker);
            picker.ApplyTemplate();
            var triggerButton = picker.Template.FindName("PART_Button", picker) as Button;
            owner?.Activate();
            owner?.Focus();
            triggerButton?.Focus();
            Pump(picker.Dispatcher);
            picker.IsDropDownOpen = true;
            Pump(picker.Dispatcher);
            picker.ApplyTemplate();
            var calendar = PickerTemplateParts.GetCalendar(picker);
            var focusedVisual = Keyboard.FocusedElement as DependencyObject;
            var focusedWithinCalendarVisual =
                calendar is not null &&
                focusedVisual is not null &&
                (ReferenceEquals(focusedVisual, calendar) || calendar.IsAncestorOf(focusedVisual));
            var focusEntered = calendar?.IsKeyboardFocusWithin == true || focusedWithinCalendarVisual;
            var escapeHandled = calendar is not null &&
                RaisePreviewKey(calendar, Key.Escape);
            var popupClosed = picker.Template.FindName("PART_Popup", picker) is Popup popup &&
                WaitForPopupClosed(popup);
            Pump(picker.Dispatcher);
            picker.ApplyTemplate();
            triggerButton = picker.Template.FindName("PART_Button", picker) as Button;
            var focusReturned = triggerButton?.IsKeyboardFocusWithin == true ||
                ReferenceEquals(Keyboard.FocusedElement, triggerButton) ||
                (owner is not null &&
                 ReferenceEquals(FocusManager.GetFocusedElement(owner), triggerButton));
            var works =
                focusEntered &&
                escapeHandled &&
                popupClosed &&
                !picker.IsDropDownOpen &&
                focusReturned &&
                picker.CalendarStyle is not null &&
                ReferenceEquals(calendar?.Style, picker.CalendarStyle);
            diagnostic = $"FocusEntered={focusEntered}; EscapeHandled={escapeHandled}; " +
                $"Closed={!picker.IsDropDownOpen}; PopupClosed={popupClosed}; FocusReturned={focusReturned}; " +
                $"CalendarStyleMatches={ReferenceEquals(calendar?.Style, picker.CalendarStyle)}; " +
                $"TriggerFocusable={triggerButton?.Focusable}; FocusedType={Keyboard.FocusedElement?.GetType().Name}";
            return works;
        }
        finally
        {
            ClosePickerForFixture(picker);
            Pump(picker.Dispatcher);
        }
    }

    public static DatePickerPointerExerciseResult ExerciseDatePickerTextPointer(DatePicker picker)
    {
        using var pointerState = new PickerPointerState();
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            ClosePickerForFixture(picker);
            picker.ApplyTemplate();
            var textBox = picker.Template.FindName("PART_TextBox", picker) as DatePickerTextBox;
            var textToggle = PickerTemplateParts.GetTextToggle(picker);
            var textSurface = textToggle?.IsVisible == true ? (UIElement)textToggle : textBox;
            if (textBox is null ||
                textSurface is null ||
                PresentationSource.FromVisual(textSurface) is null)
            {
                return new DatePickerPointerExerciseResult(
                    false, false, false, false, false, false, false);
            }

            RaiseDateTextPointer(
                picker,
                textSurface,
                UIElement.PreviewMouseLeftButtonDownEvent);
            Pump(picker.Dispatcher);
            var pressDidNotOpen = !picker.IsDropDownOpen;
            var releaseHandled = RaiseDateTextPointer(
                picker,
                textSurface,
                UIElement.PreviewMouseLeftButtonUpEvent);
            Pump(picker.Dispatcher);
            var calendar = PickerTemplateParts.GetCalendar(picker);
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
            ClosePickerForFixture(picker);
            Pump(picker.Dispatcher);
        }
    }

    public static bool ExerciseDatePickerButton(DatePicker picker)
    {
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            ClosePickerForFixture(picker);
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
            ClosePickerForFixture(picker);
            Pump(picker.Dispatcher);
        }
    }

    public static bool ExerciseDatePickerSelectedDateText(DatePicker picker)
    {
        var original = picker.SelectedDate;
        try
        {
            // Independent literals include the widest date and a leap day.
            foreach (var (date, expected) in new (DateTime? Date, string Text)[]
            {
                (new DateTime(2031, 12, 31), "2031 年 12 月 31 日"),
                (new DateTime(2032, 2, 29), "2032 年 2 月 29 日"),
                (null, string.Empty)
            })
            {
                picker.SetCurrentValue(DatePicker.SelectedDateProperty, date);
                Pump(picker.Dispatcher);
                picker.ApplyTemplate();
                if (picker.Template.FindName("PART_TextBox", picker) is not DatePickerTextBox text ||
                    text.Text != expected || !text.IsReadOnly || text.Focusable)
                {
                    return false;
                }

                picker.IsDropDownOpen = true;
                Pump(picker.Dispatcher);
                ClosePickerForFixture(picker);
                Pump(picker.Dispatcher);
                if (text.Text != expected)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            ClosePickerForFixture(picker);
            picker.SetCurrentValue(DatePicker.SelectedDateProperty, original);
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

    public static bool RaiseKey(UIElement target, Key key) =>
        RaiseKey(target, key, ModifierKeys.None);

    public static bool RaiseKey(UIElement target, Key key, ModifierKeys modifiers)
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
        var keyUp = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.KeyUpEvent
        };
        if (modifiers == ModifierKeys.None)
        {
            target.RaiseEvent(keyDown);
            target.RaiseEvent(keyUp);
        }
        else
        {
            using (MainShortcutInput.InjectModifiers(keyDown, modifiers))
            {
                target.RaiseEvent(keyDown);
            }

            using (MainShortcutInput.InjectModifiers(keyUp, modifiers))
            {
                target.RaiseEvent(keyUp);
            }
        }

        return keyDown.Handled && keyUp.Handled;
    }

    public static ComboBoxPointerExerciseResult ExerciseComboBox(ComboBox comboBox)
    {
        using var pointerState = new PickerPointerState();
        var attached = PickerTemplateParts.HasComboBoxContract(comboBox);
        var arrowColumnUnchanged = PickerTemplateParts.HasUnchangedArrowColumn(comboBox);
        var presenter = PickerTemplateParts.GetSelectionPresenter(comboBox);
        var textToggle = PickerTemplateParts.GetTextToggle(comboBox);
        var arrow = PickerTemplateParts.GetArrowToggle(comboBox);
        if (presenter is null || textToggle is null || PresentationSource.FromVisual(comboBox) is null)
        {
            return new ComboBoxPointerExerciseResult(
                false, false, false, false, false, false, false, false, false, false, false, false,
                attached,
                arrowColumnUnchanged,
                false);
        }

        comboBox.BringIntoView();
        Pump(comboBox.Dispatcher);
        var originalEnabled = comboBox.IsEnabled;
        var originalIndex = comboBox.SelectedIndex;
        try
        {
            comboBox.IsEnabled = true;
            ClosePickerForFixture(comboBox);
            Pump(comboBox.Dispatcher);
            RaiseComboTextPointer(comboBox, textToggle, UIElement.PreviewMouseLeftButtonDownEvent);
            Pump(comboBox.Dispatcher);
            var pressDidNotOpen = !comboBox.IsDropDownOpen;
            var releaseHandled = RaiseComboTextPointer(
                comboBox,
                textToggle,
                UIElement.PreviewMouseLeftButtonUpEvent);
            Pump(comboBox.Dispatcher);
            var releaseOpened = comboBox.IsDropDownOpen;

            ClosePickerForFixture(comboBox);
            Pump(comboBox.Dispatcher);
            var arrowPressOpens = false;
            if (arrow is not null && PresentationSource.FromVisual(arrow) is not null)
            {
                RaisePickerPointer(arrow, UIElement.PreviewMouseLeftButtonDownEvent);
                Pump(comboBox.Dispatcher);
                arrowPressOpens = comboBox.IsDropDownOpen;
                RaisePickerPointer(arrow, UIElement.PreviewMouseLeftButtonUpEvent);
                Pump(comboBox.Dispatcher);
            }

            ClosePickerForFixture(comboBox);
            Pump(comboBox.Dispatcher);
            Keyboard.Focus(comboBox);
            Pump(comboBox.Dispatcher);
            RaiseKey(comboBox, Key.F4);
            Pump(comboBox.Dispatcher);
            var f4Opened = comboBox.IsDropDownOpen;

            ClosePickerForFixture(comboBox);
            Pump(comboBox.Dispatcher);
            Keyboard.Focus(comboBox);
            Pump(comboBox.Dispatcher);
            RaiseComboBoxAltDown(comboBox);
            Pump(comboBox.Dispatcher);
            var altDownOpened = comboBox.IsDropDownOpen;

            comboBox.IsDropDownOpen = true;
            Pump(comboBox.Dispatcher);
            RaiseKey(comboBox, Key.Down);
            Pump(comboBox.Dispatcher);
            var arrowsKeepOpen = comboBox.IsDropDownOpen;
            RaiseKey(comboBox, Key.Escape);
            var escapePopupClosed = comboBox.Template.FindName("PART_Popup", comboBox) is Popup escapePopup &&
                WaitForPopupClosed(escapePopup);
            var escapeCloses = escapePopupClosed && !comboBox.IsDropDownOpen;

            ClosePickerForFixture(comboBox);
            Pump(comboBox.Dispatcher);
            comboBox.IsEnabled = false;
            RaiseComboTextPointer(comboBox, textToggle, UIElement.PreviewMouseLeftButtonDownEvent);
            Pump(comboBox.Dispatcher);
            var disabledHandled = RaiseComboTextPointer(
                comboBox,
                textToggle,
                UIElement.PreviewMouseLeftButtonUpEvent);
            Pump(comboBox.Dispatcher);
            var disabledClickDoesNotOpen = !comboBox.IsDropDownOpen && !disabledHandled;
            comboBox.IsEnabled = true;

            comboBox.IsDropDownOpen = true;
            Pump(comboBox.Dispatcher);
            comboBox.ApplyTemplate();
            comboBox.UpdateLayout();
            Pump(comboBox.Dispatcher);
            var itemIndex = comboBox.Items.Count > 1 ? 1 : 0;
            var item = comboBox.ItemContainerGenerator.ContainerFromIndex(itemIndex) as ComboBoxItem;
            var popupItemClickSelects = false;
            if (item is not null)
            {
                item.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent
                });
                item.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent
                });
                Pump(comboBox.Dispatcher);
                popupItemClickSelects =
                    comboBox.SelectedIndex == itemIndex ||
                    Equals(comboBox.SelectedItem, comboBox.Items[itemIndex]);
            }

            ClosePickerForFixture(comboBox);
            comboBox.IsEnabled = true;
            Pump(comboBox.Dispatcher);
            var arrowIsNotTabStop = MeasureComboBoxArrowIsNotTabStop(comboBox, arrow);

            return new ComboBoxPointerExerciseResult(
                true,
                true,
                pressDidNotOpen,
                true,
                releaseOpened,
                releaseHandled,
                arrowPressOpens,
                f4Opened,
                altDownOpened,
                arrowsKeepOpen,
                disabledClickDoesNotOpen,
                popupItemClickSelects,
                attached,
                arrowColumnUnchanged,
                arrowIsNotTabStop,
                escapeCloses);
        }
        finally
        {
            comboBox.IsEnabled = originalEnabled;
            ClosePickerForFixture(comboBox);
            if (originalIndex >= 0 && originalIndex < comboBox.Items.Count)
            {
                comboBox.SelectedIndex = originalIndex;
            }

            Pump(comboBox.Dispatcher);
        }
    }

    private static bool MeasureComboBoxArrowIsNotTabStop(ComboBox comboBox, ToggleButton? arrow)
    {
        if (arrow is null || PresentationSource.FromVisual(arrow) is null)
        {
            return false;
        }

        var owner = Window.GetWindow(comboBox);
        owner?.Activate();
        Keyboard.Focus(comboBox);
        Pump(comboBox.Dispatcher);
        comboBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Pump(comboBox.Dispatcher);
        var nextFocused = Keyboard.FocusedElement as DependencyObject;
        var nextIsArrow =
            nextFocused is not null &&
            (ReferenceEquals(nextFocused, arrow) || arrow.IsAncestorOf(nextFocused));
        Keyboard.Focus(comboBox);
        Pump(comboBox.Dispatcher);

        return !arrow.Focusable &&
               !arrow.IsTabStop &&
               !nextIsArrow;
    }

    public static (string Previous, string Next) ReadCalendarNavigationNames(DatePicker picker)
    {
        picker.ApplyTemplate();
        var originalOpen = picker.IsDropDownOpen;
        try
        {
            ClosePickerForFixture(picker);
            picker.BringIntoView();
            Window.GetWindow(picker)?.Activate();
            Pump(picker.Dispatcher);
            picker.IsDropDownOpen = true;
            Pump(picker.Dispatcher);
            picker.ApplyTemplate();
            picker.UpdateLayout();
            Pump(picker.Dispatcher);
            if (!picker.IsDropDownOpen &&
                picker.Template.FindName("PART_Button", picker) is Button button)
            {
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(picker.Dispatcher);
            }

            var calendar = PickerTemplateParts.GetCalendar(picker);
            calendar?.ApplyTemplate();
            calendar?.UpdateLayout();
            Pump(picker.Dispatcher);
            Pump(picker.Dispatcher);
            var item = calendar?.Template.FindName("PART_CalendarItem", calendar) as CalendarItem;
            item ??= calendar is null ? null : FindVisualDescendant<CalendarItem>(calendar);
            item?.ApplyTemplate();
            item?.UpdateLayout();
            var previous = item?.Template.FindName("PART_PreviousButton", item) as Button
                ?? FindCalendarNavigationButton(calendar, "上一个月");
            var next = item?.Template.FindName("PART_NextButton", item) as Button
                ?? FindCalendarNavigationButton(calendar, "下一个月");
            var names = (
                previous is null ? string.Empty : AutomationProperties.GetName(previous),
                next is null ? string.Empty : AutomationProperties.GetName(next));
            if (string.IsNullOrEmpty(names.Item1) || string.IsNullOrEmpty(names.Item2))
            {
                var popup = picker.Template.FindName("PART_Popup", picker) as Popup;
                Console.Error.WriteLine(
                    $"[calendar-navigation] pickerVisible={picker.IsVisible};pickerOpen={picker.IsDropDownOpen};" +
                    $"popupOpen={popup?.IsOpen};child={popup?.Child?.GetType().Name};" +
                    $"calendarVisible={calendar?.IsVisible};item={item?.GetType().Name};" +
                    $"calendarItemStyleIsProject={ReferenceEquals(calendar?.CalendarItemStyle, Application.Current.FindResource(typeof(CalendarItem)))};" +
                    $"itemStyleIsProject={ReferenceEquals(item?.Style, Application.Current.FindResource(typeof(CalendarItem)))};" +
                    $"previous={previous?.Name}/{previous?.IsVisible};next={next?.Name}/{next?.IsVisible}");
            }

            return names;
        }
        finally
        {
            ClosePickerForFixture(picker);
            if (originalOpen)
            {
                picker.IsDropDownOpen = true;
                Pump(picker.Dispatcher);
            }
        }
    }

    private static Button? FindCalendarNavigationButton(DependencyObject? root, string name)
    {
        if (root is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                (string.Equals(AutomationProperties.GetName(button), name, StringComparison.Ordinal) ||
                 string.Equals(button.ToolTip as string, name, StringComparison.Ordinal)))
            {
                return button;
            }

            var nested = FindCalendarNavigationButton(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void ClosePickerForFixture(Control picker)
    {
        picker.ApplyTemplate();
        var popup = picker.Template?.FindName("PART_Popup", picker) as Popup;
        var motionEnabled = popup is not null && PopupMotionBehavior.GetIsEnabled(popup);
        // Only setup/finally calls this helper. User-close assertions observe
        // WaitForPopupClosed before cleanup, with animation still enabled.
        // Stop fixture animation clocks so their two-way binding cannot reopen
        // a popup in the middle of the next independent gesture.
        if (motionEnabled)
        {
            PopupMotionBehavior.SetIsEnabled(popup!, false);
        }

        try
        {
            if (picker is DatePicker date)
            {
                date.IsDropDownOpen = false;
            }
            else if (picker is ComboBox combo)
            {
                combo.IsDropDownOpen = false;
            }

            if (popup is not null)
            {
                popup.SetCurrentValue(Popup.IsOpenProperty, false);
            }

            Pump(picker.Dispatcher);
        }
        finally
        {
            if (motionEnabled)
            {
                PopupMotionBehavior.SetIsEnabled(popup!, true);
            }
        }
    }

    private static bool RaiseComboTextPointer(
        ComboBox comboBox,
        UIElement target,
        RoutedEvent routedEvent) => RaisePickerPointer(target, routedEvent);

    private static bool RaiseDateTextPointer(
        DatePicker picker,
        UIElement target,
        RoutedEvent routedEvent) => RaisePickerPointer(target, routedEvent);

    private static bool RaisePickerPointer(UIElement target, RoutedEvent routedEvent)
    {
        // Native ButtonBase needs both routed phases and pressed-device state.
        // PickerPointerState keeps the press down across dispatcher turns and
        // restores the isolated test thread's original state after the gesture.
        if (!target.IsEnabled || !target.IsVisible ||
            PresentationSource.FromVisual(target) is not System.Windows.Interop.HwndSource source ||
            source.RootVisual is not Visual root)
        {
            return false;
        }

        var pressed = routedEvent == UIElement.PreviewMouseLeftButtonDownEvent;
        var keys = new byte[256];
        if (!PickerNativeInput.GetKeyboardState(keys))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        keys[1] = pressed ? (byte)0x80 : (byte)0;
        if (!PickerNativeInput.SetKeyboardState(keys))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        // Off-screen HWNDs cannot pass HwndMouseInputProvider's cursor-over-
        // window check. Activate its input source through Win32 capture before
        // routing the existing test seam's preview and bubbling events.
        // ButtonBase still owns its element capture and pressed/click state.
        // No physical cursor movement or global input is sent.
        Window.GetWindow(target)?.Activate();
        var point = target.TransformToAncestor(root).Transform(
            new Point(target.RenderSize.Width / 2, target.RenderSize.Height / 2));
        point = source.CompositionTarget.TransformToDevice.Transform(point);
        var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
        if (pressed)
        {
            PickerNativeInput.SetCapture(source.Handle);
            if (PickerNativeInput.GetCapture() != source.Handle)
            {
                throw new InvalidOperationException("The isolated picker HWND could not capture pointer input.");
            }
        }

        PickerNativeInput.SendMessage(source.Handle, 0x0200, pressed ? new IntPtr(1) : IntPtr.Zero, coordinates);
        // TextEditor listens to the generic mouse-button events. Raising only
        // the button-specific events bypasses TextBox focus and selection.
        var textInput = target is TextBoxBase;
        var previewEvent = textInput
            ? (pressed ? Mouse.PreviewMouseDownEvent : Mouse.PreviewMouseUpEvent)
            : routedEvent;
        var eventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = previewEvent,
            Source = target
        };
        try
        {
            target.RaiseEvent(eventArgs);
            if (!eventArgs.Handled)
            {
                eventArgs.RoutedEvent = textInput
                    ? (pressed ? Mouse.MouseDownEvent : Mouse.MouseUpEvent)
                    : (pressed ? UIElement.MouseLeftButtonDownEvent : UIElement.MouseLeftButtonUpEvent);
                target.RaiseEvent(eventArgs);
            }
        }
        finally
        {
            if (!pressed && Mouse.Captured is null && PickerNativeInput.GetCapture() == source.Handle)
            {
                PickerNativeInput.ReleaseCapture();
            }
        }

        return eventArgs.Handled;
    }

    private static void RaiseComboBoxAltDown(ComboBox comboBox)
    {
        var source = PresentationSource.FromVisual(comboBox);
        if (source is null)
        {
            return;
        }

        // ComboBox consumes the actual keyboard device's modifiers. Keep this
        // state local to the isolated validation thread and restore it before
        // yielding; creating another KeyboardDevice registers another IME.
        var originalKeys = new byte[256];
        if (!PickerNativeInput.GetKeyboardState(originalKeys))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        var keys = (byte[])originalKeys.Clone();
        keys[0x12] = 0x80;
        keys[0xA4] = 0x80;
        try
        {
            if (!PickerNativeInput.SetKeyboardState(keys))
            {
                throw new System.ComponentModel.Win32Exception();
            }

            var keyDown = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Down)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            comboBox.RaiseEvent(keyDown);
            if (!keyDown.Handled)
            {
                keyDown.RoutedEvent = Keyboard.KeyDownEvent;
                comboBox.RaiseEvent(keyDown);
            }
        }
        finally
        {
            if (!PickerNativeInput.SetKeyboardState(originalKeys))
            {
                throw new System.ComponentModel.Win32Exception();
            }
        }
    }

    private sealed class PickerPointerState : IDisposable
    {
        private readonly byte[] _original = new byte[256];

        public PickerPointerState()
        {
            if (!PickerNativeInput.GetKeyboardState(_original))
            {
                throw new System.ComponentModel.Win32Exception();
            }
        }

        public void Dispose()
        {
            Mouse.Capture(null);
            if (PickerNativeInput.GetCapture() != IntPtr.Zero)
            {
                PickerNativeInput.ReleaseCapture();
            }
            if (!PickerNativeInput.SetKeyboardState(_original))
            {
                throw new System.ComponentModel.Win32Exception();
            }
        }
    }

    private static class PickerNativeInput
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool GetKeyboardState(byte[] state);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool SetKeyboardState(byte[] state);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr SetCapture(IntPtr window);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();
    }

    private static bool OwnsKeyboardFocus(IInputElement target) =>
        target is UIElement element
            ? element.IsKeyboardFocusWithin
            : ReferenceEquals(Keyboard.FocusedElement, target);
}
