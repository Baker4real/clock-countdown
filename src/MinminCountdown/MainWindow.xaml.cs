using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class MainWindow : Window
{
    private static readonly string[] TagColors =
    {
        "#F4B6A6",
        "#F4D58D",
        "#F1D5C5",
        "#F9DFDB",
        "#FFF0E4",
        "#E8B4C8"
    };

    internal static IReadOnlyList<string> DefaultTagColors => TagColors;

    private readonly ApplicationSession _session;
    private readonly DesktopWorkspace _workspaceModule;
    private readonly HolidayCalendar _calendar;
    private readonly DiagnosticLogger _logger;
    private readonly Dictionary<string, CheckBox> _noteCompletionBoxes =
        new(StringComparer.OrdinalIgnoreCase);
    private WorkspaceKind _workspace = WorkspaceKind.Countdowns;
    private string? _editingCountdownId;
    private string? _editingNoteId;
    private bool _activeNotesExpanded = true;
    private bool _completedNotesExpanded;
    private bool _settingDefaultDate;
    private bool _noteDateDirty;
    private bool _settingReminderSchedule;
    private bool _noteReminderScheduleDirty;
    private long _lastRenderedNoteMinuteUtc = -1;

    internal MainWindow(
        ApplicationSession session,
        HolidayCalendar calendar,
        DiagnosticLogger logger,
        string assetRoot)
    {
        _session = session;
        _workspaceModule = new DesktopWorkspace(session);
        _calendar = calendar;
        _logger = logger;
        InitializeComponent();
        LoadCharacterAssets(assetRoot);
        ConfigureControls();
        WireEvents();
        _session.StateChanged += OnSessionStateChanged;
        ResetCountdownEditor();
        ResetNoteEditor();
        LoadPreferences();
        ShowWorkspace(WorkspaceKind.Countdowns);
        RefreshAll();
    }

    public event EventHandler? CloseRequested;

    internal ApplicationSession Session => _session;

    internal WorkspaceKind CurrentWorkspace => _workspace;

    internal IReadOnlyList<int> TimeOptionCounts =>
        new[] { CountdownHour.Items.Count, CountdownMinute.Items.Count, CountdownSecond.Items.Count };

    internal bool CompletedNotesAreCollapsed => !_completedNotesExpanded;

    internal bool HasLiveStatusRegions =>
        AutomationProperties.GetLiveSetting(StatusText) == AutomationLiveSetting.Polite &&
        AutomationProperties.GetLiveSetting(CountdownMessage) == AutomationLiveSetting.Polite &&
        AutomationProperties.GetLiveSetting(NoteMessage) == AutomationLiveSetting.Polite;

    internal bool HasAccessibleCoreControls =>
        FindCoreControls().All(control => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));

    internal bool HasOnDemandPanels =>
        !FilterPopup.IsOpen &&
        !TagManagerPopup.IsOpen &&
        !CountdownTags.IsPopupOpen &&
        !NoteTags.IsPopupOpen;

    internal NoteExerciseResult RunNoteAddExercise()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        var countBefore = _session.Current.Notes.Count;
        var noteDate = _session.BeijingNow.AddDays(2).Date;
        var firstDueDay = _session.BeijingNow.AddDays(3);
        var firstDue = new DateTimeOffset(
            firstDueDay.Year,
            firstDueDay.Month,
            firstDueDay.Day,
            9,
            30,
            0,
            firstDueDay.Offset);
        NoteText.Text = "验证点击添加便签";
        _settingDefaultDate = true;
        NoteDate.SelectedDate = null;
        _settingDefaultDate = false;
        NoteReminder.SelectedIndex = 2;
        NoteReminderInterval.SelectedIndex = 1;
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dateRequired =
            _session.Current.Notes.Count == countBefore &&
            string.Equals(NoteText.Text, "验证点击添加便签", StringComparison.Ordinal) &&
            NoteMessage.Text.Contains("日期", StringComparison.Ordinal);

        NoteDate.SelectedDate = noteDate;
        NoteReminder.SelectedIndex = 2;
        NoteReminderInterval.SelectedIndex = 1;
        SetReminderFirstDue(firstDue);
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var added = _session.Current.Notes.LastOrDefault(
            note => string.Equals(note.Text, "验证点击添加便签", StringComparison.Ordinal));
        var addViaClick = added is not null && _session.Current.Notes.Count == countBefore + 1;
        var dateViaPicker = added?.Date == noteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var reminderViaSelector =
            added?.ReminderIntervalMinutes == 30 &&
            added.ReminderStartUtc == firstDue.ToUniversalTime().Subtract(TimeSpan.FromMinutes(30));

        var completionViaCheckbox = false;
        if (added is not null && _noteCompletionBoxes.TryGetValue(added.Id, out var checkBox))
        {
            checkBox.IsChecked = true;
            var completed = _session.Current.Notes.First(note => note.Id == added.Id).IsCompleted;
            if (_noteCompletionBoxes.TryGetValue(added.Id, out var refreshedCheckBox))
            {
                refreshedCheckBox.IsChecked = false;
            }

            var activeAgain = !_session.Current.Notes.First(note => note.Id == added.Id).IsCompleted;
            completionViaCheckbox = completed && activeAgain;
        }

        return new NoteExerciseResult(
            dateRequired,
            addViaClick,
            dateViaPicker,
            reminderViaSelector,
            completionViaCheckbox);
    }

    internal ReminderModeClickResult RunReminderModeClickExercises()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        var noteDate = _session.BeijingNow.AddDays(2).Date;
        var onceDue = _session.BeijingNow.AddDays(3);
        onceDue = new DateTimeOffset(
            onceDue.Year,
            onceDue.Month,
            onceDue.Day,
            9,
            10,
            0,
            onceDue.Offset);
        var repeatDue = onceDue.AddDays(1).AddMinutes(20);

        NoteText.Text = "验证主窗不提醒";
        NoteDate.SelectedDate = noteDate;
        NoteReminder.SelectedIndex = 0;
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var noReminderWorks = _session.Current.Notes.LastOrDefault(
            note => note.Text == "验证主窗不提醒")?.Reminder is NoReminder;

        NoteText.Text = "验证主窗仅一次";
        NoteDate.SelectedDate = noteDate;
        NoteReminder.SelectedIndex = 1;
        SetReminderFirstDue(onceDue);
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var onceWorks = _session.Current.Notes.LastOrDefault(
            note => note.Text == "验证主窗仅一次")?.Reminder is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            } once &&
            once.FirstDueUtc == onceDue.ToUniversalTime();

        NoteText.Text = "验证主窗重复提醒";
        NoteDate.SelectedDate = noteDate;
        NoteReminder.SelectedIndex = 2;
        NoteReminderInterval.SelectedIndex = 1;
        SetReminderFirstDue(repeatDue);
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var repeatWorks = _session.Current.Notes.LastOrDefault(
            note => note.Text == "验证主窗重复提醒")?.Reminder is RepeatingReminder repeating &&
            repeating.FirstDueUtc == repeatDue.ToUniversalTime() &&
            repeating.Interval.Minutes == 30;

        return new ReminderModeClickResult(noReminderWorks, onceWorks, repeatWorks);
    }

    internal (bool TracksBeijingDay, bool PreservesChosenDate) ProbeNoteDateBehavior()
    {
        var originalEditingId = _editingNoteId;
        var originalDirty = _noteDateDirty;
        var originalDate = NoteDate.SelectedDate;
        var originalText = NoteText.Text;
        try
        {
            _editingNoteId = null;
            _noteDateDirty = false;
            UpdateTime(new DateTimeOffset(2026, 7, 20, 23, 59, 59, TimeSpan.FromHours(8)));
            var firstDay = NoteDate.SelectedDate == new DateTime(2026, 7, 20);
            UpdateTime(new DateTimeOffset(2026, 7, 21, 0, 0, 1, TimeSpan.FromHours(8)));
            var nextDay = NoteDate.SelectedDate == new DateTime(2026, 7, 21);

            NoteText.Text = "跨日保留草稿";
            NoteDate.SelectedDate = new DateTime(2031, 4, 5);
            UpdateTime(new DateTimeOffset(2026, 7, 22, 0, 0, 1, TimeSpan.FromHours(8)));
            var preserved =
                NoteDate.SelectedDate == new DateTime(2031, 4, 5) &&
                NoteText.Text == "跨日保留草稿";
            return (firstDay && nextDay, preserved);
        }
        finally
        {
            _editingNoteId = originalEditingId;
            _noteDateDirty = originalDirty;
            _settingDefaultDate = true;
            NoteDate.SelectedDate = originalDate;
            _settingDefaultDate = false;
            NoteText.Text = originalText;
        }
    }

    internal bool ProbeWorkspaceDraftRetention()
    {
        var originalWorkspace = _workspace;
        var originalCountdownDraft = CountdownName.Text;
        var originalNoteDraft = NoteText.Text;
        try
        {
            CountdownName.Text = "未保存倒计时草稿";
            NoteText.Text = "未保存便签草稿";
            ShowWorkspace(WorkspaceKind.Countdowns);
            ShowWorkspace(WorkspaceKind.Notes);
            ShowWorkspace(WorkspaceKind.Countdowns);
            return
                CountdownName.Text == "未保存倒计时草稿" &&
                NoteText.Text == "未保存便签草稿";
        }
        finally
        {
            CountdownName.Text = originalCountdownDraft;
            NoteText.Text = originalNoteDraft;
            ShowWorkspace(originalWorkspace);
        }
    }

    internal bool ProbeCrossDayReminderBehavior()
    {
        var originalDate = NoteReminderDate.SelectedDate;
        var originalHour = NoteReminderHour.SelectedIndex;
        var originalMinute = NoteReminderMinute.SelectedIndex;
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-cross-day-{Guid.NewGuid():N}.settings.json");
        try
        {
            SetReminderFirstDue(new DateTimeOffset(2031, 4, 6, 9, 30, 0, TimeSpan.FromHours(8)));
            var nowUtc = new DateTimeOffset(2031, 4, 5, 15, 59, 0, TimeSpan.Zero);
            if (!ReminderSchedule.TryCreateFirstDueUtc(
                    NoteReminderDate.SelectedDate,
                    NoteReminderHour.SelectedIndex,
                    NoteReminderMinute.SelectedIndex,
                    nowUtc,
                    out var firstDueUtc,
                    out _) ||
                firstDueUtc != new DateTimeOffset(2031, 4, 6, 1, 30, 0, TimeSpan.Zero))
            {
                return false;
            }

            var clock = new FixedClock(nowUtc);
            var store = new SettingsStore(clock: clock);
            var session = ApplicationSession.Load(settingsPath, store);
            var workspace = new DesktopWorkspace(session);
            if (!workspace.TrySaveNote(
                    null,
                    "跨天提醒探针",
                    new DateTime(2031, 4, 5),
                    false,
                    Array.Empty<string>(),
                    30,
                    firstDueUtc,
                    out _) ||
                !workspace.TrySaveQuickNote(
                    "快速跨天提醒探针",
                    new DateTime(2031, 4, 5),
                    Array.Empty<string>(),
                    60,
                    firstDueUtc.Value.AddDays(1),
                    out _))
            {
                return false;
            }

            var saved = session.Current.Notes.Single(note => note.Text == "跨天提醒探针");
            var originalStart = saved.ReminderStartUtc;
            var liveEarly = session.Reminders.GetDue(session.Current, firstDueUtc.Value.AddSeconds(-1));
            var liveDue = session.Reminders.GetDue(session.Current, firstDueUtc.Value);
            session.Reminders.ConfirmDisplayed(saved, firstDueUtc.Value);
            if (!workspace.TrySaveNote(
                    saved.Id,
                    "已编辑的跨天提醒探针",
                    new DateTime(2031, 4, 5),
                    false,
                    Array.Empty<string>(),
                    30,
                    null,
                    out _))
            {
                return false;
            }

            var reloaded = ApplicationSession.Load(settingsPath, store);
            var persistedNote = reloaded.Current.Notes.Single(note => note.Id == saved.Id);
            var quickNote = reloaded.Current.Notes.Single(note => note.Text == "快速跨天提醒探针");
            var early = reloaded.Reminders.GetDue(reloaded.Current, firstDueUtc.Value.AddSeconds(-1));
            var due = reloaded.Reminders.GetDue(reloaded.Current, firstDueUtc.Value);
            reloaded.Reminders.ConfirmDisplayed(persistedNote, firstDueUtc.Value);
            var duplicate = reloaded.Reminders.GetDue(reloaded.Current, firstDueUtc.Value);
            var nextCycle = reloaded.Reminders.GetDue(reloaded.Current, firstDueUtc.Value.AddMinutes(30));
            var completionRestarted =
                reloaded.TrySetNoteCompletion(persistedNote.Id, true, out _) &&
                reloaded.TrySetNoteCompletion(persistedNote.Id, false, out _) &&
                reloaded.Current.Notes.Single(note => note.Id == persistedNote.Id).ReminderStartUtc == nowUtc;
            return
                originalStart == new DateTimeOffset(2031, 4, 6, 1, 0, 0, TimeSpan.Zero) &&
                persistedNote.ReminderStartUtc == originalStart &&
                quickNote.ReminderStartUtc == new DateTimeOffset(2031, 4, 7, 0, 30, 0, TimeSpan.Zero) &&
                liveEarly.Count == 0 &&
                liveDue.Count == 1 &&
                early.Count == 0 &&
                due.Count == 1 &&
                duplicate.Count == 0 &&
                nextCycle.Count == 1 &&
                completionRestarted;
        }
        finally
        {
            _settingReminderSchedule = true;
            NoteReminderDate.SelectedDate = originalDate;
            NoteReminderHour.SelectedIndex = originalHour;
            NoteReminderMinute.SelectedIndex = originalMinute;
            _settingReminderSchedule = false;
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }
    }

    internal bool ProbeReminderSummaryAdvancesAfterFirstDue()
    {
        var originalWorkspace = _workspace;
        var firstDueUtc = new DateTimeOffset(2031, 4, 6, 1, 30, 0, TimeSpan.Zero);
        var note = new NoteRecord(
            "reminder-summary-probe",
            "提醒摘要探针",
            "2031-04-06",
            false,
            Array.Empty<string>(),
            30,
            firstDueUtc.Subtract(TimeSpan.FromMinutes(30)));
        try
        {
            var before = FormatNoteDetails(note, firstDueUtc.AddSeconds(-1));
            var after = FormatNoteDetails(note, firstDueUtc);
            ShowWorkspace(WorkspaceKind.Notes);
            RenderNotes(firstDueUtc.AddSeconds(-1));
            var sentinel = new Border();
            ActiveNoteRows.Children.Add(sentinel);
            UpdateTime(ChinaStandardTime.ToBeijingTime(firstDueUtc));
            return
                before.Contains("下次 4月6日 09:30", StringComparison.Ordinal) &&
                after.Contains("下次 4月6日 10:00", StringComparison.Ordinal) &&
                !ActiveNoteRows.Children.Contains(sentinel);
        }
        finally
        {
            ShowWorkspace(originalWorkspace);
        }
    }

    internal void ShowWorkspace(WorkspaceKind workspace)
    {
        _workspace = workspace;
        var showCountdowns = workspace == WorkspaceKind.Countdowns;
        CountdownWorkspace.Visibility = showCountdowns ? Visibility.Visible : Visibility.Collapsed;
        NotesWorkspace.Visibility = showCountdowns ? Visibility.Collapsed : Visibility.Visible;
        CountdownTab.IsChecked = showCountdowns;
        NotesTab.IsChecked = !showCountdowns;
        CountdownFilterOptions.Visibility = showCountdowns ? Visibility.Visible : Visibility.Collapsed;
        NoteFilterOptions.Visibility = showCountdowns ? Visibility.Collapsed : Visibility.Visible;
        SaveCountdownButton.IsEnabled = showCountdowns;
        RenderVisibleWorkspace();
    }

    internal void UpdateTime(DateTimeOffset beijingNow)
    {
        NowText.Text = $"当前北京时间：{beijingNow:yyyy年MM月dd日 HH:mm:ss}";
        RefreshDefaultNoteDate(beijingNow);
        if (_workspace == WorkspaceKind.Countdowns)
        {
            RenderCountdowns();
            return;
        }

        var nowUtc = beijingNow.ToUniversalTime();
        var currentMinuteUtc = nowUtc.UtcDateTime.Ticks / TimeSpan.TicksPerMinute;
        if (_lastRenderedNoteMinuteUtc != currentMinuteUtc)
        {
            RenderNotes(nowUtc);
        }
    }

    internal void ApplyTheme(ApplicationThemeMode mode)
    {
        CharacterBackgroundLayer.Opacity = mode == ApplicationThemeMode.Night ? 0.16 : 0.10;
    }

    internal bool CharacterBackgroundRemainsSubtle =>
        CharacterBackgroundLayer.Opacity is > 0 and <= 0.16;

    internal double CharacterBackgroundOpacity => CharacterBackgroundLayer.Opacity;

    internal bool CharacterPanelDocksFollowActiveTheme =>
        ReferenceEquals(CharacterPanelSurface.Background, FindResource("CharacterPanelBrush")) &&
        ReferenceEquals(CharacterPanelSurface.BorderBrush, FindResource("CharacterLineBrush")) &&
        ReferenceEquals(MyMelodyCharacterDock.Background, FindResource("CharacterMyMelodyDockBrush")) &&
        ReferenceEquals(CinnamorollCharacterDock.Background, FindResource("CharacterCinnamorollDockBrush")) &&
        ReferenceEquals(KuromiCharacterDock.Background, FindResource("CharacterKuromiDockBrush")) &&
        ReferenceEquals(MyMelodyCharacterDock.BorderBrush, FindResource("CharacterLineBrush")) &&
        ReferenceEquals(CinnamorollCharacterDock.BorderBrush, FindResource("CharacterLineBrush")) &&
        ReferenceEquals(KuromiCharacterDock.BorderBrush, FindResource("CharacterLineBrush"));

    internal bool CharacterPanelDecorationsAreNonInteractive =>
        !CharacterPanelLayer.IsHitTestVisible &&
        !MyMelodyCharacterDock.IsHitTestVisible &&
        !CinnamorollCharacterDock.IsHitTestVisible &&
        !KuromiCharacterDock.IsHitTestVisible &&
        !MyMelodyPanelCharacter.IsHitTestVisible &&
        !CinnamorollPanelCharacter.IsHitTestVisible &&
        !KuromiPanelCharacter.IsHitTestVisible;

    internal bool TagChipsHaveAccessibleThemeText
    {
        get
        {
            if (FindResource("TagChipTextBrush") is not SolidColorBrush foreground)
            {
                return false;
            }

            return TagColors.All(color =>
                ParseBrush(color) is SolidColorBrush background &&
                ApplicationTheme.MeetsContrast(background, foreground, 4.5));
        }
    }

    private void ConfigureControls()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            var text = hour.ToString("D2", CultureInfo.InvariantCulture);
            CountdownHour.Items.Add(text);
            NoteReminderHour.Items.Add(text);
        }

        for (var value = 0; value < 60; value++)
        {
            var text = value.ToString("D2", CultureInfo.InvariantCulture);
            CountdownMinute.Items.Add(text);
            CountdownSecond.Items.Add(text);
            NoteReminderMinute.Items.Add(text);
        }

        DisplayPrecision.Items.Add("天");
        DisplayPrecision.Items.Add("小时");
        DisplayPrecision.Items.Add("分钟");
        DisplayPrecision.Items.Add("秒");
        NoteReminder.Items.Add("不提醒");
        NoteReminder.Items.Add("仅一次");
        NoteReminder.Items.Add("重复提醒");
        NoteReminderInterval.Items.Add("15 分钟");
        NoteReminderInterval.Items.Add("30 分钟");
        NoteReminderInterval.Items.Add("60 分钟");
        NoteReminderInterval.Items.Add("自定义");
        AnyTagMode.IsChecked = true;
        CountdownAllFilter.IsChecked = true;
        NoteAllFilter.IsChecked = true;

        NameControl(CountdownName, "倒计时名称");
        NameControl(CountdownDate, "倒计时截止日期");
        NameControl(CountdownHour, "倒计时截止小时");
        NameControl(CountdownMinute, "倒计时截止分钟");
        NameControl(CountdownSecond, "倒计时截止秒");
        NameControl(SetNowButton, "设为当前北京时间");
        NameControl(SaveCountdownButton, "保存倒计时");
        NameControl(DisplayPrecision, "悬浮窗显示精度");
        NameControl(MonthlySalary, "月薪");
        NameControl(WorkStart, "上班时间");
        NameControl(WorkEnd, "下班时间");
        NameControl(SavePreferencesButton, "保存偏好");
        NameControl(NoteText, "便签内容");
        NameControl(NoteDate, "便签日期");
        NameControl(NoteReminder, "便签提醒模式");
        NameControl(NoteReminderInterval, "便签重复提醒间隔");
        NameControl(CustomReminderMinutes, "自定义提醒分钟");
        NameControl(NoteReminderDate, "首次提醒日期");
        NameControl(NoteReminderHour, "首次提醒小时");
        NameControl(NoteReminderMinute, "首次提醒分钟");
        NameControl(NoteSearch, "搜索便签");
        NameControl(SaveNoteButton, "保存便签");
        NameControl(CountdownTab, "倒计时工作区");
        NameControl(NotesTab, "便签工作区");
        NameControl(FilterButton, "打开筛选");
        NameControl(TagManagerButton, "管理标签");
        NameControl(NewTagInput, "新标签名称");
        NameControl(CreateTagButton, "新建标签");
        NameControl(NoteDateFilter, "按便签日期筛选");
        ThemedDatePickerBehavior.Attach(CountdownDate);
        ThemedDatePickerBehavior.Attach(NoteDate);
        ThemedDatePickerBehavior.Attach(NoteReminderDate);
        ThemedDatePickerBehavior.Attach(NoteDateFilter);
        UiAccessibility.MarkLive(StatusText);
        UiAccessibility.MarkLive(CountdownMessage);
        UiAccessibility.MarkLive(NoteMessage);
        UiAccessibility.MarkLive(TagManagerMessage);
    }

    private void WireEvents()
    {
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        TopmostCheck.Checked += (_, _) => Topmost = true;
        TopmostCheck.Unchecked += (_, _) => Topmost = false;
        CountdownTab.Click += (_, _) => ShowWorkspace(WorkspaceKind.Countdowns);
        NotesTab.Click += (_, _) => ShowWorkspace(WorkspaceKind.Notes);
        FilterButton.Click += (_, _) => FilterPopup.IsOpen = !FilterPopup.IsOpen;
        TagManagerButton.Click += (_, _) =>
        {
            RenderTagManager();
            TagManagerPopup.IsOpen = !TagManagerPopup.IsOpen;
        };
        SetNowButton.Click += (_, _) => SetCountdownTarget(_session.BeijingNow);
        SaveCountdownButton.Click += (_, _) => SaveCountdown();
        CancelCountdownEdit.Click += (_, _) => ResetCountdownEditor();
        SavePreferencesButton.Click += (_, _) => SavePreferences();
        SaveNoteButton.Click += (_, _) => SaveNote();
        CancelNoteEdit.Click += (_, _) => ResetNoteEditor();
        NoteReminder.SelectionChanged += (_, _) => UpdateReminderScheduleControls();
        NoteReminderInterval.SelectionChanged += (_, _) => UpdateReminderIntervalControls();
        CustomReminderMinutes.TextChanged += (_, _) => SetDefaultReminderFirstDue();
        NoteReminderDate.SelectedDateChanged += (_, _) => MarkReminderScheduleDirty();
        NoteReminderHour.SelectionChanged += (_, _) => MarkReminderScheduleDirty();
        NoteReminderMinute.SelectionChanged += (_, _) => MarkReminderScheduleDirty();
        NoteSearch.TextChanged += (_, _) =>
        {
            _workspaceModule.SetNoteSearch(NoteSearch.Text);
            RenderNotes();
            UpdateFilterSummary();
        };
        NoteDate.SelectedDateChanged += (_, _) =>
        {
            if (!_settingDefaultDate)
            {
                _noteDateDirty = true;
            }
        };
        ToggleActiveNotes.Click += (_, _) =>
        {
            _activeNotesExpanded = !_activeNotesExpanded;
            ActiveNoteRows.Visibility =
                _activeNotesExpanded ? Visibility.Visible : Visibility.Collapsed;
            ToggleActiveNotes.Content = _activeNotesExpanded ? "收起未完成" : "未完成";
        };
        ToggleCompletedNotes.Click += (_, _) =>
        {
            _completedNotesExpanded = !_completedNotesExpanded;
            CompletedNoteRows.Visibility =
                _completedNotesExpanded ? Visibility.Visible : Visibility.Collapsed;
            ToggleCompletedNotes.Content = _completedNotesExpanded ? "收起已完成" : "已完成";
        };
        CountdownTags.CreateRequested += CreateTagFromSelector;
        NoteTags.CreateRequested += CreateTagFromSelector;
        CreateTagButton.Click += (_, _) => CreateTag(NewTagInput.Text, null);

        AnyTagMode.Click += (_, _) =>
        {
            _workspaceModule.SetTagMatchMode(TagMatchMode.Any);
            AnyTagMode.IsChecked = true;
            AllTagMode.IsChecked = false;
            RenderVisibleWorkspace();
        };
        AllTagMode.Click += (_, _) =>
        {
            _workspaceModule.SetTagMatchMode(TagMatchMode.All);
            AnyTagMode.IsChecked = false;
            AllTagMode.IsChecked = true;
            RenderVisibleWorkspace();
        };
        UntaggedOnly.Click += (_, _) =>
        {
            _workspaceModule.SetUntaggedOnly(UntaggedOnly.IsChecked == true);
            RenderVisibleWorkspace();
        };
        CountdownAllFilter.Click += (_, _) => SetCountdownFilter(CountdownStatusFilter.All);
        CountdownActiveFilter.Click += (_, _) => SetCountdownFilter(CountdownStatusFilter.Active);
        CountdownExpiredFilter.Click += (_, _) => SetCountdownFilter(CountdownStatusFilter.Expired);
        NoteAllFilter.Click += (_, _) => SetNoteFilter(NoteStatusFilter.All);
        NoteActiveFilter.Click += (_, _) => SetNoteFilter(NoteStatusFilter.Active);
        NoteCompletedFilter.Click += (_, _) => SetNoteFilter(NoteStatusFilter.Completed);
        NoteUndatedFilter.Click += (_, _) =>
        {
            _workspaceModule.SetUndatedNotesOnly(NoteUndatedFilter.IsChecked == true);
            if (_workspaceModule.Filter.UndatedNotesOnly)
            {
                NoteDateFilter.SelectedDate = null;
            }

            RenderNotes();
            UpdateFilterSummary();
        };
        NoteDateFilter.SelectedDateChanged += (_, _) =>
        {
            _workspaceModule.SetNoteDateFilter(NoteDateFilter.SelectedDate?.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture));
            if (_workspaceModule.Filter.NoteDate is not null)
            {
                NoteUndatedFilter.IsChecked = false;
            }

            RenderNotes();
            UpdateFilterSummary();
        };
        ClearNoteDateFilter.Click += (_, _) =>
        {
            NoteDateFilter.SelectedDate = null;
            _workspaceModule.SetNoteDateFilter(null);
            _workspaceModule.SetUndatedNotesOnly(false);
            NoteUndatedFilter.IsChecked = false;
            RenderNotes();
            UpdateFilterSummary();
        };
        ClearFilterButton.Click += (_, _) => ClearFilters();
    }

    private void OnSessionStateChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.Invoke(RefreshAll);
    }

    private void RefreshAll()
    {
        CountdownTags.SetTags(_session.Current.Tags);
        NoteTags.SetTags(_session.Current.Tags);
        RenderFilterTags();
        RenderTagManager();
        RenderVisibleWorkspace();
        LoadPreferences();
    }

    private void RenderVisibleWorkspace()
    {
        if (_workspace == WorkspaceKind.Countdowns)
        {
            RenderCountdowns();
        }
        else
        {
            RenderNotes();
        }

        UpdateFilterSummary();
    }

    private void RenderCountdowns()
    {
        CountdownRows.Children.Clear();
        var records = _workspaceModule.GetVisibleCountdowns();
        RecordCountText.Text = $"{records.Count} 个倒计时";
        if (records.Count == 0)
        {
            CountdownRows.Children.Add(new TextBlock
            {
                Text = "还没有符合条件的倒计时。",
                Margin = new Thickness(0, 22, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("MutedBrush")
            });
            return;
        }

        foreach (var record in records)
        {
            var active = record.TargetUtc > _session.UtcNow;
            var edit = ActionButton("编辑", record.Id, "编辑倒计时");
            edit.Click += OnEditCountdown;
            var delete = ActionButton("删除", record.Id, "删除倒计时");
            delete.Style = (Style)FindResource("DangerButton");
            delete.Click += OnDeleteCountdown;
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            actions.Children.Add(edit);
            actions.Children.Add(delete);

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = record.Name,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            });
            text.Children.Add(new TextBlock
            {
                Text = CountdownFormatting.FormatRemaining(
                    record.TargetUtc,
                    _session.UtcNow,
                    _session.Current.Preferences.DisplayPrecision),
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = active ? (Brush)FindResource("BodyBrush") : (Brush)FindResource("DangerBrush")
            });
            text.Children.Add(CreateTagChips(record.TagIds));
            Grid.SetColumn(actions, 1);
            layout.Children.Add(text);
            layout.Children.Add(actions);
            CountdownRows.Children.Add(Card(layout, active ? "CardBrush" : "ExpiredBrush"));
        }
    }

    private void RenderNotes(DateTimeOffset? nowUtc = null)
    {
        var effectiveNowUtc = (nowUtc ?? _session.UtcNow).ToUniversalTime();
        _lastRenderedNoteMinuteUtc = effectiveNowUtc.UtcDateTime.Ticks / TimeSpan.TicksPerMinute;
        ActiveNoteRows.Children.Clear();
        CompletedNoteRows.Children.Clear();
        _noteCompletionBoxes.Clear();
        var records = _workspaceModule.GetVisibleNotes();
        var active = records.Where(note => !note.IsCompleted).ToArray();
        var completed = records.Where(note => note.IsCompleted).ToArray();
        RecordCountText.Text = $"{active.Length} 个待办";
        AddNotesGroupedByDay(ActiveNoteRows, active, effectiveNowUtc);
        AddNotesGroupedByDay(CompletedNoteRows, completed, effectiveNowUtc);
        ToggleActiveNotes.Content = _activeNotesExpanded ? "收起未完成" : "未完成";
        ToggleActiveNotes.Visibility = active.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveNoteRows.Visibility =
            active.Length == 0 || _activeNotesExpanded ? Visibility.Visible : Visibility.Collapsed;
        ToggleCompletedNotes.Visibility = completed.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompletedNoteRows.Visibility =
            _completedNotesExpanded && completed.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (records.Count == 0)
        {
            ActiveNoteRows.Children.Add(new TextBlock
            {
                Text = "还没有符合条件的便签。",
                Margin = new Thickness(0, 22, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("MutedBrush")
            });
        }
    }

    private void AddNotesGroupedByDay(
        Panel target,
        IReadOnlyList<NoteRecord> notes,
        DateTimeOffset nowUtc)
    {
        string? previousDate = null;
        foreach (var note in notes)
        {
            if (!string.Equals(previousDate, note.Date, StringComparison.Ordinal))
            {
                target.Children.Add(new TextBlock
                {
                    Text = note.Date is null ? "未设置日期" : note.Date,
                    Margin = new Thickness(2, previousDate is null ? 0 : 10, 0, 6),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("MutedBrush")
                });
                previousDate = note.Date;
            }

            target.Children.Add(CreateNoteRow(note, nowUtc));
        }
    }

    private FrameworkElement CreateNoteRow(NoteRecord note, DateTimeOffset nowUtc)
    {
        var completion = UiAccessibility.Name(
            new CheckBox
            {
                IsChecked = note.IsCompleted,
                Tag = note.Id,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 10, 0)
            },
            note.IsCompleted ? $"恢复便签 {note.Text}" : $"完成便签 {note.Text}");
        completion.Checked += OnNoteCompletionChanged;
        completion.Unchecked += OnNoteCompletionChanged;
        _noteCompletionBoxes[note.Id] = completion;

        var edit = ActionButton("编辑", note.Id, "编辑便签");
        edit.Click += OnEditNote;
        var delete = ActionButton("删除", note.Id, "删除便签");
        delete.Style = (Style)FindResource("DangerButton");
        delete.Click += OnDeleteNote;
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(edit);
        actions.Children.Add(delete);

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = note.Text,
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = note.IsCompleted ? TextDecorations.Strikethrough : null,
            Foreground = note.IsCompleted ? (Brush)FindResource("MutedBrush") : (Brush)FindResource("InkBrush")
        });
        var details = FormatNoteDetails(note, nowUtc);
        body.Children.Add(new TextBlock
        {
            Text = details,
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 12,
            Foreground = (Brush)FindResource("MutedBrush")
        });
        body.Children.Add(CreateTagChips(note.TagIds));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(body, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(completion);
        grid.Children.Add(body);
        grid.Children.Add(actions);
        return Card(grid, "NoteBrush");
    }

    private static string FormatNoteDetails(NoteRecord note, DateTimeOffset nowUtc)
    {
        var status = note.IsCompleted ? "已完成" : "未完成";
        if (note.Reminder is NoReminder)
        {
            return $"{note.Date ?? "未设置日期"} · {status}";
        }

        if (note.Reminder is OnceReminder once)
        {
            if (once.Delivery == OnceDeliveryState.Delivered)
            {
                return $"{note.Date ?? "未设置日期"} · {status} · 已提醒";
            }

            var due = ChinaStandardTime.ToBeijingTime(once.FirstDueUtc);
            var suffix = note.IsCompleted ? "（已暂停）" : string.Empty;
            return $"{note.Date ?? "未设置日期"} · {status} · 一次提醒 {due:M月d日 HH:mm}{suffix}";
        }

        var repeating = (RepeatingReminder)note.Reminder;
        var nextReminder = ReminderSchedule.GetNextDueUtc(note, nowUtc);
        if (note.IsCompleted)
        {
            return $"{note.Date ?? "未设置日期"} · {status} · 每 {repeating.Interval.Minutes} 分钟（已暂停）";
        }

        return $"{note.Date ?? "未设置日期"} · {status} · 下次 {ChinaStandardTime.ToBeijingTime(nextReminder!.Value):M月d日 HH:mm} · 每 {repeating.Interval.Minutes} 分钟";
    }

    private void SaveCountdown()
    {
        if (string.IsNullOrWhiteSpace(CountdownName.Text) || CountdownDate.SelectedDate is null)
        {
            SetMessage(CountdownMessage, "请填写名称并选择截止日期。", true);
            return;
        }

        var target = CountdownFormatting.ToUtc(
            CountdownDate.SelectedDate.Value,
            CountdownHour.SelectedIndex,
            CountdownMinute.SelectedIndex,
            CountdownSecond.SelectedIndex);
        if (!_workspaceModule.TrySaveCountdown(
                _editingCountdownId,
                CountdownName.Text,
                target,
                CountdownTags.SelectedTagIds.ToArray(),
                out var error))
        {
            SetMessage(CountdownMessage, $"保存失败：{error}", true);
            return;
        }

        SetMessage(StatusText, "倒计时已保存。", false);
        ResetCountdownEditor();
    }

    private void SaveNote()
    {
        if (string.IsNullOrWhiteSpace(NoteText.Text))
        {
            SetMessage(NoteMessage, "请先写下便签内容。", true);
            return;
        }

        if (NoteDate.SelectedDate is null)
        {
            SetMessage(NoteMessage, "请选择便签日期，当前草稿已保留。", true);
            return;
        }

        var existing = _editingNoteId is null
            ? null
            : _session.Current.Notes.FirstOrDefault(note => note.Id == _editingNoteId);
        if (!TryBuildReminder(existing, out var reminder, out var reminderError))
        {
            SetMessage(NoteMessage, reminderError, true);
            return;
        }

        if (!_workspaceModule.TrySaveNote(
                _editingNoteId,
                NoteText.Text,
                NoteDate.SelectedDate,
                existing?.IsCompleted ?? false,
                NoteTags.SelectedTagIds.ToArray(),
                reminder,
                out var error))
        {
            SetMessage(NoteMessage, $"保存失败：{error}", true);
            return;
        }

        SetMessage(StatusText, "便签已保存。", false);
        ResetNoteEditor();
    }

    private void SavePreferences()
    {
        if (!decimal.TryParse(MonthlySalary.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var salary) &&
            !decimal.TryParse(MonthlySalary.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out salary))
        {
            SetMessage(StatusText, "月薪格式无效。", true);
            return;
        }

        if (!TimeSpan.TryParseExact(WorkStart.Text, @"hh\:mm", CultureInfo.InvariantCulture, out var workStart) ||
            !TimeSpan.TryParseExact(WorkEnd.Text, @"hh\:mm", CultureInfo.InvariantCulture, out var workEnd))
        {
            SetMessage(StatusText, "上下班时间请使用 HH:mm。", true);
            return;
        }

        var precision = DisplayPrecision.SelectedIndex switch
        {
            0 => "Day",
            1 => "Hour",
            2 => "Minute",
            _ => "Second"
        };
        if (!_workspaceModule.TrySavePreferences(precision, salary, workStart, workEnd, out var error))
        {
            SetMessage(StatusText, $"设置保存失败：{error}", true);
            return;
        }

        SetMessage(StatusText, "偏好已保存。", false);
    }

    private void OnEditCountdown(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var record = _session.Current.Countdowns.First(item => item.Id == id);
        var beijing = ChinaStandardTime.ToBeijingTime(record.TargetUtc);
        _editingCountdownId = record.Id;
        CountdownName.Text = record.Name;
        CountdownDate.SelectedDate = beijing.Date;
        CountdownHour.SelectedIndex = beijing.Hour;
        CountdownMinute.SelectedIndex = beijing.Minute;
        CountdownSecond.SelectedIndex = beijing.Second;
        CountdownTags.SetSelection(record.TagIds);
        CountdownEditorTitle.Text = "编辑倒计时";
        SaveCountdownButton.Content = "保存修改";
        CancelCountdownEdit.Visibility = Visibility.Visible;
    }

    private void OnDeleteCountdown(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var record = _session.Current.Countdowns.First(item => item.Id == id);
        if (new ConfirmationDialog(this, "删除倒计时", $"确定删除“{record.Name}”吗？").ShowDialog() != true)
        {
            return;
        }

        if (!_workspaceModule.TryDeleteCountdown(id, out var error))
        {
            SetMessage(StatusText, $"删除失败：{error}", true);
        }
    }

    private void OnEditNote(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var note = _session.Current.Notes.First(item => item.Id == id);
        _editingNoteId = id;
        NoteText.Text = note.Text;
        _settingDefaultDate = true;
        NoteDate.SelectedDate = note.Date is null
            ? null
            : DateTime.ParseExact(note.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        _settingDefaultDate = false;
        _noteDateDirty = true;
        NoteTags.SetSelection(note.TagIds);
        SetReminderSelection(note.Reminder);
        var displayedDueUtc = note.Reminder switch
        {
            OnceReminder once => once.FirstDueUtc,
            RepeatingReminder => ReminderSchedule.GetNextDueUtc(note, _session.UtcNow),
            _ => null
        };
        SetReminderFirstDue(displayedDueUtc is null
            ? null
            : ChinaStandardTime.ToBeijingTime(displayedDueUtc.Value));
        _noteReminderScheduleDirty = false;
        SaveNoteButton.Content = "保存";
        CancelNoteEdit.Visibility = Visibility.Visible;
    }

    private void OnDeleteNote(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var note = _session.Current.Notes.First(item => item.Id == id);
        if (new ConfirmationDialog(this, "删除便签", $"确定删除“{note.Text}”吗？").ShowDialog() != true)
        {
            return;
        }

        if (!_workspaceModule.TryDeleteNote(id, out var error))
        {
            SetMessage(StatusText, $"删除失败：{error}", true);
        }
    }

    private void OnNoteCompletionChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not CheckBox { Tag: string id } checkBox)
        {
            return;
        }

        var desired = checkBox.IsChecked == true;
        var current = _session.Current.Notes.FirstOrDefault(note => note.Id == id);
        if (current is null || current.IsCompleted == desired)
        {
            return;
        }

        if (!_workspaceModule.TrySetNoteCompletion(id, desired, out var error))
        {
            SetMessage(StatusText, $"便签状态保存失败：{error}", true);
            checkBox.IsChecked = current.IsCompleted;
        }
    }

    private void ResetCountdownEditor()
    {
        _editingCountdownId = null;
        CountdownName.Clear();
        SetCountdownTarget(_session.BeijingNow.AddDays(1));
        CountdownTags.SetSelection(Array.Empty<string>());
        CountdownEditorTitle.Text = "新增倒计时";
        SaveCountdownButton.Content = "添加倒计时";
        CancelCountdownEdit.Visibility = Visibility.Collapsed;
        CountdownMessage.Text = string.Empty;
    }

    private void ResetNoteEditor()
    {
        _editingNoteId = null;
        NoteText.Clear();
        _settingDefaultDate = true;
        NoteDate.SelectedDate = _session.BeijingNow.Date;
        _settingDefaultDate = false;
        _noteDateDirty = false;
        NoteTags.SetSelection(_workspaceModule.SelectedTagIds);
        NoteReminder.SelectedIndex = 0;
        NoteReminderInterval.SelectedIndex = 1;
        CustomReminderMinutes.Clear();
        NoteReminderInterval.Visibility = Visibility.Collapsed;
        CustomReminderMinutes.Visibility = Visibility.Collapsed;
        SetReminderFirstDue(null);
        _noteReminderScheduleDirty = false;
        SaveNoteButton.Content = "添加";
        CancelNoteEdit.Visibility = Visibility.Collapsed;
        NoteMessage.Text = string.Empty;
    }

    private void SetCountdownTarget(DateTimeOffset beijing)
    {
        CountdownDate.SelectedDate = beijing.Date;
        CountdownHour.SelectedIndex = beijing.Hour;
        CountdownMinute.SelectedIndex = beijing.Minute;
        CountdownSecond.SelectedIndex = beijing.Second;
    }

    private void LoadPreferences()
    {
        var preferences = _session.Current.Preferences;
        DisplayPrecision.SelectedIndex = preferences.DisplayPrecision switch
        {
            "Day" => 0,
            "Hour" => 1,
            "Minute" => 2,
            _ => 3
        };
        MonthlySalary.Text = preferences.MonthlySalary.ToString(CultureInfo.CurrentCulture);
        WorkStart.Text = CountdownFormatting.FormatWorkTime(preferences.WorkStart);
        WorkEnd.Text = CountdownFormatting.FormatWorkTime(preferences.WorkEnd);
    }

    private void SetReminderSelection(NoteReminder reminder)
    {
        NoteReminder.SelectedIndex = reminder switch
        {
            NoReminder => 0,
            OnceReminder => 1,
            RepeatingReminder => 2,
            _ => throw new InvalidOperationException("未知便签提醒模式。")
        };
        var interval = reminder is RepeatingReminder repeating
            ? repeating.Interval.Minutes
            : 30;
        NoteReminderInterval.SelectedIndex = interval switch
        {
            15 => 0,
            30 => 1,
            60 => 2,
            _ => 3
        };
        CustomReminderMinutes.Text = interval is 15 or 30 or 60
            ? string.Empty
            : interval.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateReminderScheduleControls()
    {
        var enabled = NoteReminder.SelectedIndex > 0;
        var repeating = NoteReminder.SelectedIndex == 2;
        NoteReminderInterval.Visibility = repeating ? Visibility.Visible : Visibility.Collapsed;
        CustomReminderMinutes.Visibility =
            repeating && NoteReminderInterval.SelectedIndex == 3
                ? Visibility.Visible
                : Visibility.Collapsed;
        NoteReminderSchedule.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled)
        {
            _noteReminderScheduleDirty = true;
            SetDefaultReminderFirstDue();
        }
        else
        {
            SetReminderFirstDue(null);
            _noteReminderScheduleDirty = false;
        }
    }

    private void UpdateReminderIntervalControls()
    {
        CustomReminderMinutes.Visibility =
            NoteReminder.SelectedIndex == 2 && NoteReminderInterval.SelectedIndex == 3
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (NoteReminder.SelectedIndex == 2)
        {
            _noteReminderScheduleDirty = true;
            SetDefaultReminderFirstDue();
        }
    }

    private void SetDefaultReminderFirstDue()
    {
        if (NoteReminder.SelectedIndex == 0)
        {
            return;
        }

        var offsetMinutes = 15;
        if (NoteReminder.SelectedIndex == 2)
        {
            if (!TryGetReminderInterval(out var interval, out _) || interval is null)
            {
                return;
            }

            offsetMinutes = interval.Value;
        }

        SetReminderFirstDue(
            ReminderSchedule.GetDefaultFirstDueBeijing(_session.BeijingNow, offsetMinutes));
    }

    private void SetReminderFirstDue(DateTimeOffset? beijing)
    {
        _settingReminderSchedule = true;
        NoteReminderDate.SelectedDate = beijing?.Date;
        NoteReminderHour.SelectedIndex = beijing?.Hour ?? -1;
        NoteReminderMinute.SelectedIndex = beijing?.Minute ?? -1;
        _settingReminderSchedule = false;
    }

    private void MarkReminderScheduleDirty()
    {
        if (!_settingReminderSchedule)
        {
            _noteReminderScheduleDirty = true;
        }
    }

    private bool TryGetReminderInterval(out int? interval, out string error)
    {
        interval = NoteReminderInterval.SelectedIndex switch
        {
            0 => 15,
            1 => 30,
            2 => 60,
            _ => -1
        };
        if (interval != -1)
        {
            error = string.Empty;
            return true;
        }

        if (!int.TryParse(CustomReminderMinutes.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var custom) ||
            custom is < 1 or > 1440)
        {
            error = "自定义提醒必须是 1 到 1440 分钟。";
            return false;
        }

        interval = custom;
        error = string.Empty;
        return true;
    }

    private bool TryBuildReminder(
        NoteRecord? existing,
        out NoteReminder reminder,
        out string error)
    {
        if (NoteReminder.SelectedIndex == 0)
        {
            reminder = NoReminder.Instance;
            error = string.Empty;
            return true;
        }

        if (NoteReminder.SelectedIndex == 1 &&
            existing?.Reminder is OnceReminder once &&
            !_noteReminderScheduleDirty)
        {
            reminder = once;
            error = string.Empty;
            return true;
        }

        int? interval = null;
        if (NoteReminder.SelectedIndex == 2 &&
            !TryGetReminderInterval(out interval, out error))
        {
            reminder = NoReminder.Instance;
            return false;
        }

        if (NoteReminder.SelectedIndex == 2 &&
            existing?.Reminder is RepeatingReminder repeating &&
            repeating.Interval.Minutes == interval &&
            !_noteReminderScheduleDirty)
        {
            reminder = repeating;
            error = string.Empty;
            return true;
        }

        if (NoteReminder.SelectedIndex is not 1 and not 2)
        {
            reminder = NoReminder.Instance;
            error = "请选择有效的提醒模式。";
            return false;
        }

        if (!ReminderSchedule.TryCreateFirstDueUtc(
                NoteReminderDate.SelectedDate,
                NoteReminderHour.SelectedIndex,
                NoteReminderMinute.SelectedIndex,
                _session.UtcNow,
                out var firstDueUtc,
                out error))
        {
            reminder = NoReminder.Instance;
            return false;
        }

        reminder = NoteReminder.SelectedIndex == 1
            ? ReminderSchedule.CreateOnce(firstDueUtc!.Value)
            : ReminderSchedule.CreateRepeating(firstDueUtc!.Value, interval!.Value);
        return true;
    }

    private void RenderFilterTags()
    {
        FilterTagOptions.Children.Clear();
        foreach (var tag in _session.Current.Tags)
        {
            var option = new ToggleButton
            {
                Content = tag.Name,
                IsChecked = _workspaceModule.SelectedTagIds.Contains(tag.Id),
                Tag = tag.Id,
                Uid = tag.Id,
                Margin = new Thickness(0, 0, 6, 6),
                Style = (Style)FindResource("TagToggleButton")
            };
            NameControl(option, $"筛选标签 {tag.Name}");
            option.Click += (_, _) =>
            {
                if (option.IsChecked == true)
                {
                    _workspaceModule.SelectTag(tag.Id, true);
                }
                else
                {
                    _workspaceModule.SelectTag(tag.Id, false);
                }

                RenderVisibleWorkspace();
            };
            FilterTagOptions.Children.Add(option);
        }
    }

    private void RenderTagManager()
    {
        TagManagerRows.Children.Clear();
        foreach (var tag in _session.Current.Tags)
        {
            var name = UiAccessibility.Name(
                new TextBox { Text = tag.Name, MaxLength = 30 },
                $"标签名称 {tag.Name}");
            var save = ActionButton("保存", tag.Id, $"保存标签 {tag.Name}");
            var delete = ActionButton("删除", tag.Id, $"删除标签 {tag.Name}");
            delete.Style = (Style)FindResource("DangerButton");
            save.Click += (_, _) =>
            {
                if (!_workspaceModule.TryRenameTag(tag.Id, name.Text, tag.Color, out var error))
                {
                    SetMessage(TagManagerMessage, error, true);
                }
            };
            delete.Click += (_, _) =>
            {
                if (new ConfirmationDialog(this, "删除标签", "标签会从记录中解除，但记录本身不会删除。").ShowDialog() != true)
                {
                    return;
                }

                if (!_workspaceModule.TryDeleteTag(tag.Id, out var error))
                {
                    SetMessage(TagManagerMessage, error, true);
                }
            };
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(save, 1);
            Grid.SetColumn(delete, 2);
            row.Children.Add(name);
            row.Children.Add(save);
            row.Children.Add(delete);
            TagManagerRows.Children.Add(row);
        }
    }

    private void CreateTagFromSelector(object? sender, string name)
    {
        if (CreateTag(name, sender as TagSelector) is { } tag && sender is TagSelector selector)
        {
            selector.SelectedTagIds.Add(tag.Id);
            selector.SetSelection(selector.SelectedTagIds);
        }
    }

    private TagRecord? CreateTag(string name, TagSelector? selector)
    {
        var normalized = name.Trim();
        if (normalized.Length == 0)
        {
            SetMessage(TagManagerMessage, "请输入标签名称。", true);
            return null;
        }

        var color = TagColors[_session.Current.Tags.Count % TagColors.Length];
        if (!_workspaceModule.TryCreateTag(normalized, color, out var tag, out var error))
        {
            SetMessage(TagManagerMessage, error, true);
            return null;
        }

        NewTagInput.Clear();
        SetMessage(TagManagerMessage, "标签已创建。", false);
        return tag;
    }

    private void SetCountdownFilter(CountdownStatusFilter filter)
    {
        _workspaceModule.SetCountdownStatus(filter);
        CountdownAllFilter.IsChecked = filter == CountdownStatusFilter.All;
        CountdownActiveFilter.IsChecked = filter == CountdownStatusFilter.Active;
        CountdownExpiredFilter.IsChecked = filter == CountdownStatusFilter.Expired;
        RenderCountdowns();
        UpdateFilterSummary();
    }

    private void SetNoteFilter(NoteStatusFilter filter)
    {
        _workspaceModule.SetNoteStatus(filter);
        NoteAllFilter.IsChecked = filter == NoteStatusFilter.All;
        NoteActiveFilter.IsChecked = filter == NoteStatusFilter.Active;
        NoteCompletedFilter.IsChecked = filter == NoteStatusFilter.Completed;
        RenderNotes();
        UpdateFilterSummary();
    }

    private void ClearFilters()
    {
        _workspaceModule.ClearFilters();
        AnyTagMode.IsChecked = true;
        AllTagMode.IsChecked = false;
        UntaggedOnly.IsChecked = false;
        CountdownAllFilter.IsChecked = true;
        CountdownActiveFilter.IsChecked = false;
        CountdownExpiredFilter.IsChecked = false;
        NoteAllFilter.IsChecked = true;
        NoteActiveFilter.IsChecked = false;
        NoteCompletedFilter.IsChecked = false;
        NoteUndatedFilter.IsChecked = false;
        NoteDateFilter.SelectedDate = null;
        NoteSearch.Clear();
        RenderFilterTags();
        RenderVisibleWorkspace();
    }

    private void UpdateFilterSummary()
    {
        var count = _workspaceModule.AppliedFilterCount;
        FilterButton.Content = count == 0 ? "筛选" : $"筛选 · {count}";
        AutomationProperties.SetName(FilterButton, count == 0 ? "打开筛选" : $"打开筛选，已应用 {count} 项");
    }

    private void RefreshDefaultNoteDate(DateTimeOffset beijingNow)
    {
        if (_editingNoteId is not null || _noteDateDirty)
        {
            return;
        }

        _settingDefaultDate = true;
        NoteDate.SelectedDate = beijingNow.Date;
        _settingDefaultDate = false;
    }

    private FrameworkElement CreateTagChips(IReadOnlyList<string> tagIds)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var tag in _session.Current.Tags.Where(tag => tagIds.Contains(
                     tag.Id,
                     StringComparer.OrdinalIgnoreCase)))
        {
            var chip = new Border
            {
                Uid = tag.Id,
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(7, 3, 7, 3),
                CornerRadius = new CornerRadius(10),
                Background = ParseBrush(tag.Color) ?? (Brush)FindResource("TagBrush"),
                Child = new TextBlock
                {
                    Text = tag.Name,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TagChipTextBrush")
                }
            };
            panel.Children.Add(chip);
        }

        return panel;
    }

    private static Brush? ParseBrush(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        try
        {
            return new BrushConverter().ConvertFromString(color) as Brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private Border Card(UIElement content, string backgroundResource)
    {
        return new Border
        {
            Child = content,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)FindResource(backgroundResource),
            BorderBrush = (Brush)FindResource("PeachLineBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private Button ActionButton(string content, string id, string accessibleName)
    {
        return UiAccessibility.Name(
            new Button
            {
                Content = content,
                Tag = id,
                MinWidth = 58,
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("SecondaryButton")
            },
            accessibleName);
    }

    private IEnumerable<FrameworkElement> FindCoreControls()
    {
        yield return CountdownName;
        yield return CountdownDate;
        yield return CountdownHour;
        yield return CountdownMinute;
        yield return CountdownSecond;
        yield return SetNowButton;
        yield return SaveCountdownButton;
        yield return DisplayPrecision;
        yield return MonthlySalary;
        yield return WorkStart;
        yield return WorkEnd;
        yield return SavePreferencesButton;
        yield return NoteText;
        yield return NoteDate;
        yield return NoteReminder;
        yield return NoteReminderInterval;
        yield return NoteReminderDate;
        yield return NoteReminderHour;
        yield return NoteReminderMinute;
        yield return SaveNoteButton;
        yield return NoteSearch;
        yield return FilterButton;
        yield return TagManagerButton;
    }

    private static void NameControl(FrameworkElement control, string name) =>
        AutomationProperties.SetName(control, name);

    private void SetMessage(TextBlock target, string text, bool isError)
    {
        target.Text = text;
        target.SetResourceReference(
            TextBlock.ForegroundProperty,
            isError ? "DangerBrush" : "SuccessBrush");
    }

    private void LoadCharacterAssets(string assetRoot)
    {
        var characterRoot = Path.Combine(assetRoot, "assets");
        var myMelody = LoadImage(Path.Combine(characterRoot, "sanrio-official-mymelody.png"));
        var cinnamoroll = LoadImage(Path.Combine(characterRoot, "sanrio-official-cinnamoroll.png"));
        var kuromi = LoadImage(Path.Combine(characterRoot, "sanrio-official-kuromi.png"));
        MyMelodyBackground.Source = myMelody;
        CinnamorollBackground.Source = cinnamoroll;
        KuromiBackground.Source = kuromi;
        MyMelodyPanelCharacter.Source = myMelody;
        CinnamorollPanelCharacter.Source = cinnamoroll;
        KuromiPanelCharacter.Source = kuromi;
    }

    internal static BitmapImage? LoadImage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
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
