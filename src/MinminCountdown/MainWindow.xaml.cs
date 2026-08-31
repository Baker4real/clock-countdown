using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown;

public sealed partial class MainWindow : Window, IManagerWorkspaceHost
{
    private const double CountdownRemainingColumnWidth = 300;
    private const double CountdownRemainingLeftMargin = 16;
    private const double CountdownRemainingBaseFontSize = 20;
    private const double CountdownRemainingMinimumFontSize = 14;
    internal const string NoteCardReminderSummaryAutomationId = "MainNoteCardReminderSummary";
    private const double NoteCardReminderSummaryFontSize = 18.5;

    private static readonly string[] TagColors =
    {
        "#F4B6A6",
        "#F4D58D",
        "#F1D5C5",
        "#F9DFDB",
        "#FFF0E4"
    };

    internal static IReadOnlyList<string> DefaultTagColors => TagColors;

    private readonly DesktopWorkspace _desktopWorkspace;
    private readonly HolidayCalendar _calendar;
    private readonly DiagnosticLogger _logger;
    private readonly Dictionary<string, CheckBox> _noteCompletionBoxes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _countdownSourceRows =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _countdownRemainingTexts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _countdownActiveStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _countdownSourceFocusTargets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _noteSourceRows =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _noteSourceFocusTargets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(bool IsCompleted, string? Date)> _collapsedNoteDateSections = new();
    private WorkspaceKind _workspace = WorkspaceKind.Countdowns;
    private ApplicationThemeMode _themeMode = ApplicationThemeMode.Day;
    private string? _editingCountdownId;
    private string? _editingNoteId;
    private bool _activeNotesExpanded = true;
    private bool _completedNotesExpanded;
    private bool _settingDefaultDate;
    private bool _noteDateDirty;
    private bool _settingsDraftDirty;
    private bool _loadingPreferences;
    private bool _syncingNoteSearch;
    private IReadOnlyList<ReminderRule> _countdownReminderRules = Array.Empty<ReminderRule>();
    private IReadOnlyList<ReminderRule> _noteReminderRules = Array.Empty<ReminderRule>();
    private long _lastRenderedNoteMinuteUtc = -1;
    private string? _reminderSourceCountdownId;
    private string? _reminderSourceNoteId;
    private string? _countdownRenderSignature;

    internal bool CountdownRowsWereRebuiltForValidation { get; private set; }
    internal long CountdownRowRebuildGenerationForValidation { get; private set; }

    internal static (string Message, bool ShowReset) CountdownEmptyStatePresentation(bool hasSavedCountdowns) =>
        hasSavedCountdowns
            ? ("没有符合当前筛选条件的倒计时。", true)
            : ("还没有倒计时，先添加一个目标。", false);

    internal bool CountdownRemainingLayoutMatches
    {
        get
        {
            if (_countdownRemainingTexts.Count == 0)
            {
                return true;
            }

            return _countdownRemainingTexts.All(pair =>
                pair.Value.TextWrapping == TextWrapping.NoWrap &&
                pair.Value.TextTrimming == TextTrimming.None &&
                pair.Value.TextAlignment == TextAlignment.Right &&
                pair.Value.HorizontalAlignment == HorizontalAlignment.Stretch &&
                pair.Value.Typography.NumeralAlignment == FontNumeralAlignment.Tabular &&
                pair.Value.Parent is Grid grid &&
                grid.ColumnDefinitions.Count >= 2 &&
                Math.Abs(grid.ColumnDefinitions[1].Width.Value - 300) <= 0.01);
        }
    }

    internal IReadOnlyDictionary<string, TextBlock> CountdownRemainingTextsForValidation =>
        _countdownRemainingTexts;

    internal bool MainNoteCompletionStyleIsScoped
    {
        get
        {
            var mainBox = _noteCompletionBoxes.Values.FirstOrDefault();
            var global = Application.Current.TryFindResource(typeof(CheckBox)) as Style;
            return mainBox is null ||
                (mainBox.Style is not null &&
                 !ReferenceEquals(mainBox.Style, global) &&
                 Math.Abs(mainBox.ActualWidth - 30) <= 1);
        }
    }

    internal MainWindow(
        DesktopWorkspace desktopWorkspace,
        HolidayCalendar calendar,
        DiagnosticLogger logger,
        string assetRoot)
    {
        _desktopWorkspace = desktopWorkspace;
        _calendar = calendar;
        _logger = logger;
        InitializeComponent();
        PopupFocusLifecycle.Attach(FilterPopup, FilterButton, () => AnyTagMode);
        PopupFocusLifecycle.Attach(TagManagerPopup, TagManagerButton, () => NewTagInput);
        LoadCharacterAssets(assetRoot);
        ConfigureControls();
        WireEvents();
        PreviewKeyDown += OnPreviewKeyDown;
        _desktopWorkspace.StateChanged += OnWorkspaceStateChanged;
        ResetCountdownEditor();
        ResetNoteEditor();
        LoadPreferences();
        ShowWorkspace(WorkspaceKind.Countdowns);
        RefreshAll();
        Loaded += (_, _) =>
        {
            CountdownTab.Focus();
        };
    }

    public event EventHandler? CloseRequested;

    public event EventHandler? MinimizeToTrayRequested;

    internal event EventHandler? ThemePreferenceChanged;

    internal WorkspaceKind CurrentWorkspace => _workspace;

    internal string? EditingCountdownId => _editingCountdownId;

    internal ApplicationThemeMode CurrentThemeMode => _themeMode;

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

    internal bool MainShortcutScopeIsSafe =>
        IsEnabled &&
        !FilterPopup.IsOpen &&
        !TagManagerPopup.IsOpen &&
        !CountdownTags.IsPopupOpen &&
        !NoteTags.IsPopupOpen &&
        !IsTextEntryFocus(Keyboard.FocusedElement);

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (TryExecuteShortcut(MainShortcutInput.ResolveModifiers(args), args.Key))
        {
            args.Handled = true;
        }
    }

    internal bool TryExecuteShortcut(ModifierKeys modifiers, Key key)
    {
        if (!MainShortcutScopeIsSafe || modifiers == ModifierKeys.None)
        {
            return false;
        }

        switch (modifiers, key)
        {
            case (ModifierKeys.Control, Key.D1):
                ShowWorkspace(WorkspaceKind.Countdowns);
                return true;
            case (ModifierKeys.Control, Key.D2):
                ShowWorkspace(WorkspaceKind.Notes);
                return true;
            case (ModifierKeys.Control, Key.F):
                FilterPopup.IsOpen = true;
                return true;
            case (ModifierKeys.Control, Key.T):
                RenderTagManager();
                TagManagerPopup.IsOpen = true;
                return true;
            case (ModifierKeys.Control | ModifierKeys.Shift, Key.T):
                CycleThemePreference();
                return true;
            case (ModifierKeys.Control, Key.M):
                MinimizeToTray();
                return true;
            case (ModifierKeys.Control, Key.S) when _workspace == WorkspaceKind.Countdowns:
                SaveCountdown();
                return true;
            case (ModifierKeys.Control | ModifierKeys.Shift, Key.S):
                SavePreferences();
                return true;
            default:
                return false;
        }
    }

    internal void PrepareInstanceActivationStateForTest()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        SetNoteFilter(NoteStatusFilter.Completed);
        UpdateFilter(filter => filter with
        {
            SelectedTagIds = new[] { "instance-activation-tag-a", "instance-activation-tag-b" }
        });
        NoteSearch.Text = "instance-activation-search";
        NoteDateFilter.SelectedDate = new DateTime(2031, 4, 5);
        CountdownName.Text = "instance-activation-countdown-draft";
        NoteText.Text = "instance-activation-note-draft";
    }

    internal MainWindowActivationState CaptureInstanceActivationState() => new(
        _workspace,
        _desktopWorkspace.Snapshot.Filter,
        CountdownName.Text,
        NoteText.Text,
        NoteSearch.Text,
        NoteDateFilter.SelectedDate,
        _editingCountdownId,
        _editingNoteId);

    private NoteExerciseResult RunNoteAddExercise()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        var initial = _desktopWorkspace.Snapshot;
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
        NoteText.Text = "验证点击添加便签";
        _noteReminderRules = Array.Empty<ReminderRule>();
        _settingDefaultDate = true;
        NoteDate.SelectedDate = null;
        _settingDefaultDate = false;
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dateRequired =
            _desktopWorkspace.Snapshot.Settings.Notes.Count == countBefore &&
            string.Equals(NoteText.Text, "验证点击添加便签", StringComparison.Ordinal) &&
            NoteMessage.Text.Contains("日期", StringComparison.Ordinal);

        NoteDate.SelectedDate = noteDate;
        _noteReminderRules = new ReminderRule[]
        {
            new FixedIntervalReminderRule(
                Guid.NewGuid().ToString("N"),
                firstDue.ToUniversalTime(),
                new ReminderInterval(30))
        };
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var afterAdd = _desktopWorkspace.Snapshot.Settings;
        var added = afterAdd.Notes.LastOrDefault(
            note => string.Equals(note.Text, "验证点击添加便签", StringComparison.Ordinal));
        var addViaClick = added is not null && afterAdd.Notes.Count == countBefore + 1;
        var dateViaPicker = added?.Date == noteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var reminderViaSelector =
            added?.ReminderIntervalMinutes == 30 &&
            added.ReminderStartUtc == firstDue.ToUniversalTime().Subtract(TimeSpan.FromMinutes(30));

        var completionViaCheckbox = false;
        if (added is not null && _noteCompletionBoxes.TryGetValue(added.Id, out var checkBox))
        {
            checkBox.IsChecked = true;
            var completed = _desktopWorkspace.Snapshot.Settings.Notes
                .First(note => note.Id == added.Id).IsCompleted;
            if (_noteCompletionBoxes.TryGetValue(added.Id, out var refreshedCheckBox))
            {
                refreshedCheckBox.IsChecked = false;
            }

            var activeAgain = !_desktopWorkspace.Snapshot.Settings.Notes
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
        var initial = _desktopWorkspace.Snapshot;
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

        NoteText.Text = "验证主窗不提醒";
        NoteDate.SelectedDate = noteDate;
        _noteReminderRules = Array.Empty<ReminderRule>();
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var noReminderWorks = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证主窗不提醒")?.Reminder is NoReminder;

        NoteText.Text = "验证主窗仅一次";
        NoteDate.SelectedDate = noteDate;
        _noteReminderRules = new ReminderRule[]
        {
            new AbsoluteOnceReminderRule(
                Guid.NewGuid().ToString("N"),
                onceDue.ToUniversalTime())
        };
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var onceWorks = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证主窗仅一次")?.Reminder is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            } once &&
            once.FirstDueUtc == onceDue.ToUniversalTime();

        NoteText.Text = "验证主窗重复提醒";
        NoteDate.SelectedDate = noteDate;
        _noteReminderRules = new ReminderRule[]
        {
            new FixedIntervalReminderRule(
                Guid.NewGuid().ToString("N"),
                repeatDue.ToUniversalTime(),
                new ReminderInterval(30))
        };
        SaveNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var repeatWorks = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证主窗重复提醒")?.Reminder is RepeatingReminder repeating &&
            repeating.FirstDueUtc == repeatDue.ToUniversalTime() &&
            repeating.Interval.Minutes == 30;

        return new ReminderModeClickResult(noReminderWorks, onceWorks, repeatWorks);
    }

    private (bool TracksBeijingDay, bool PreservesChosenDate) ProbeNoteDateBehavior()
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

    private bool ProbeWorkspaceDraftRetention()
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
                before.Contains("下次 4 月 6 日 09:30", StringComparison.Ordinal) &&
                after.Contains("下次 4 月 6 日 10:00", StringComparison.Ordinal) &&
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
        UpdateReferenceDecorationVisibility();
        RenderVisibleWorkspace();
        SetMessage(StatusText, workspace == WorkspaceKind.Countdowns ? "当前工作区：倒计时" : "当前工作区：便签", false);
    }

    void IManagerWorkspaceHost.Reveal(WorkspaceKind workspace) => Reveal(workspace);

    internal void Reveal(WorkspaceKind workspace)
    {
        ShowWorkspace(workspace);
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
    }

    internal bool OpenReminderSource(string noteId) =>
        OpenReminderSource(ReminderSourceKind.Note, noteId);

    internal bool OpenReminderSource(ReminderSourceKind sourceKind, string sourceId) =>
        sourceKind == ReminderSourceKind.Countdown
            ? OpenCountdownReminderSource(sourceId)
            : OpenNoteReminderSource(sourceId);

    private bool OpenNoteReminderSource(string noteId)
    {
        var note = _desktopWorkspace.Snapshot.AllNotes.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            return false;
        }

        _reminderSourceNoteId = note.Id;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            if (_noteSourceRows.TryGetValue(note.Id, out var row))
            {
                row.BringIntoView();
            }

            if (_noteSourceFocusTargets.TryGetValue(note.Id, out var focusTarget))
            {
                Keyboard.Focus(focusTarget);
            }
            return true;
        }
        finally
        {
            _reminderSourceNoteId = null;
        }
    }

    private bool OpenCountdownReminderSource(string countdownId)
    {
        var countdown = _desktopWorkspace.Snapshot.Settings.Countdowns.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, countdownId, StringComparison.OrdinalIgnoreCase));
        if (countdown is null)
        {
            return false;
        }

        _reminderSourceCountdownId = countdown.Id;
        try
        {
            ShowWorkspace(WorkspaceKind.Countdowns);
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            if (_countdownSourceRows.TryGetValue(countdown.Id, out var row))
            {
                row.BringIntoView();
            }

            if (_countdownSourceFocusTargets.TryGetValue(countdown.Id, out var focusTarget))
            {
                Keyboard.Focus(focusTarget);
            }
            return true;
        }
        finally
        {
            _reminderSourceCountdownId = null;
        }
    }

    internal void UpdateTime(DateTimeOffset beijingNow)
    {
        NowText.Text = $"当前北京时间：{beijingNow:yyyy年MM月dd日 HH:mm:ss}";
        RefreshDefaultNoteDate(beijingNow);
        var nowUtc = beijingNow.ToUniversalTime();
        if (_workspace == WorkspaceKind.Countdowns)
        {
            RenderCountdowns(nowUtc);
            return;
        }

        var currentMinuteUtc = nowUtc.UtcDateTime.Ticks / TimeSpan.TicksPerMinute;
        if (_lastRenderedNoteMinuteUtc != currentMinuteUtc)
        {
            RenderNotes(nowUtc);
        }
    }

    internal void ApplyTheme(ApplicationThemeMode mode)
    {
        _themeMode = mode;
        CharacterBackgroundLayer.Opacity = mode == ApplicationThemeMode.Night ? 0.16 : 0.075;
        UpdateReferenceDecorationVisibility();
        SetMessage(StatusText, mode == ApplicationThemeMode.Day ? "当前主题：日间" : "当前主题：夜间", false);
    }

    internal bool CharacterBackgroundRemainsSubtle =>
        CharacterBackgroundLayer.Visibility == Visibility.Collapsed
            ? MainReferenceDecorationsLoaded
            : CharacterBackgroundLayer.Opacity is > 0 and <= 0.16;

    internal double CharacterBackgroundOpacity => CharacterBackgroundLayer.Opacity;

    internal bool MainReferenceDecorationsLoaded =>
        CountdownReferenceDecoration.Source is not null &&
        NotesReferenceDecoration.Source is not null &&
        NightCountdownTopDecoration.Source is not null &&
        NightNotesTopDecoration.Source is not null;

    internal bool MainNightReferenceDecorationsLoaded =>
        NightCountdownTopDecoration.Source is not null &&
        NightNotesTopDecoration.Source is not null;

    internal bool MainReferenceDecorationsAreNonInteractive =>
        !MainReferenceDecorationLayer.IsHitTestVisible &&
        !CountdownReferenceDecoration.IsHitTestVisible &&
        !NotesReferenceDecoration.IsHitTestVisible &&
        !NightCountdownTopDecoration.IsHitTestVisible &&
        !NightNotesTopDecoration.IsHitTestVisible;

    internal bool MainReferenceDecorationsFollowActiveTheme
    {
        get
        {
            var referencesVisible = MainReferenceDecorationsLoaded;
            var countdownVisible = _workspace == WorkspaceKind.Countdowns
                ? Visibility.Visible
                : Visibility.Collapsed;
            var notesVisible = _workspace == WorkspaceKind.Notes
                ? Visibility.Visible
                : Visibility.Collapsed;
            return referencesVisible &&
                CharacterBackgroundLayer.Visibility == Visibility.Collapsed &&
                CharacterPanelLayer.Visibility == Visibility.Collapsed &&
                CountdownReferenceDecoration.Visibility ==
                    (_themeMode == ApplicationThemeMode.Day ? countdownVisible : Visibility.Collapsed) &&
                NotesReferenceDecoration.Visibility ==
                    (_themeMode == ApplicationThemeMode.Day ? notesVisible : Visibility.Collapsed) &&
                NightCountdownTopDecoration.Visibility ==
                    (_themeMode == ApplicationThemeMode.Night ? countdownVisible : Visibility.Collapsed) &&
                NightNotesTopDecoration.Visibility ==
                    (_themeMode == ApplicationThemeMode.Night ? notesVisible : Visibility.Collapsed);
        }
    }

    internal bool CharacterPanelDocksFollowActiveTheme
    {
        get
        {
            var showingNotes = _workspace == WorkspaceKind.Notes;
            var panelBrush = showingNotes ? "NoteBrush" : "CharacterPanelBrush";
            var lineBrush = showingNotes ? "NoteLineBrush" : "CharacterLineBrush";
            return
                ReferenceEquals(CharacterPanelSurface.Background, FindResource(panelBrush)) &&
                ReferenceEquals(CharacterPanelSurface.BorderBrush, FindResource(lineBrush)) &&
        MyMelodyCharacterDock.Background is null &&
        CinnamorollCharacterDock.Background is null &&
        KuromiCharacterDock.Background is null &&
                ReferenceEquals(MyMelodyCharacterDock.BorderBrush, FindResource("CharacterLineBrush")) &&
                ReferenceEquals(CinnamorollCharacterDock.BorderBrush, FindResource("CharacterLineBrush")) &&
                ReferenceEquals(KuromiCharacterDock.BorderBrush, FindResource("CharacterLineBrush"));
        }
    }

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
            if (FindResource("TagChipTextBrush") is not SolidColorBrush foreground ||
                FindResource("TagBrush") is not SolidColorBrush defaultBackground ||
                !ApplicationTheme.MeetsContrast(defaultBackground, foreground, 4.5))
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
        var keyboardFocusVisual = (Style)FindResource("MainKeyboardFocusVisual");
        for (var hour = 0; hour < 24; hour++)
        {
            var text = hour.ToString("D2", CultureInfo.InvariantCulture);
            CountdownHour.Items.Add(text);
        }

        for (var value = 0; value < 60; value++)
        {
            var text = value.ToString("D2", CultureInfo.InvariantCulture);
            CountdownMinute.Items.Add(text);
            CountdownSecond.Items.Add(text);
        }

        DisplayPrecision.Items.Add("天");
        DisplayPrecision.Items.Add("小时");
        DisplayPrecision.Items.Add("分钟");
        DisplayPrecision.Items.Add("秒");
        AnyTagMode.IsChecked = true;
        CountdownAllFilter.IsChecked = true;
        NoteAllFilter.IsChecked = true;

        NameControl(CountdownName, "倒计时名称");
        NameControl(CountdownDate, "倒计时截止日期");
        NameControl(CountdownHour, "倒计时截止小时");
        NameControl(CountdownMinute, "倒计时截止分钟");
        NameControl(CountdownSecond, "倒计时截止秒");
        NameControl(SetNowButton, "设为当前北京时间");
        NameControl(ManageCountdownReminderRulesButton, "管理倒计时提醒规则");
        NameControl(SaveCountdownButton, "保存倒计时");
        NameControl(DisplayPrecision, "悬浮窗显示精度");
        NameControl(MonthlySalary, "月薪");
        NameControl(WorkStart, "上班时间");
        NameControl(WorkEnd, "下班时间");
        NameControl(SavePreferencesButton, "保存偏好");
        NameControl(ThemeModeButton, "主题模式：自动；点击切换为日间");
        NameControl(MinimizeButton, "最小化窗口");
        NameControl(CloseButton, "关闭窗口");
        NameControl(NoteText, "便签内容");
        NameControl(NoteDate, "便签日期");
        NameControl(ManageNoteReminderRulesButton, "管理便签提醒规则");
        NameControl(NoteSearch, "搜索便签");
        NameControl(SaveNoteButton, "保存便签");
        NameControl(ResetEmptyNotesFiltersButton, "重置便签筛选");
        NameControl(ResetEmptyCountdownFiltersButton, "重置倒计时筛选");
        NameControl(CountdownTab, "倒计时工作区");
        NameControl(NotesTab, "便签工作区");
        NameControl(FilterButton, "打开筛选");
        NameControl(TagManagerButton, "管理标签");
        AutomationProperties.SetAutomationId(CountdownTab, "CountdownTab");
        AutomationProperties.SetAutomationId(NotesTab, "NotesTab");
        AutomationProperties.SetAutomationId(FilterButton, "FilterButton");
        AutomationProperties.SetAutomationId(TagManagerButton, "TagManagerButton");
        var keyboardPathControls = new FrameworkElement[]
        {
            ThemeModeButton, TopmostCheck, MinimizeButton, CloseButton,
            CountdownTab, NotesTab, FilterButton, TagManagerButton,
            CountdownName, CountdownDate, CountdownHour, CountdownMinute, CountdownSecond,
            SetNowButton, CountdownTags.PopupTrigger, ManageCountdownReminderRulesButton,
            CancelCountdownEdit, SaveCountdownButton, DisplayPrecision, MonthlySalary,
            WorkStart, WorkEnd, SavePreferencesButton, CountdownListRegion,
            NoteText, NoteDate, SaveNoteButton, CancelNoteEdit,
            ManageNoteReminderRulesButton,
            NoteTags.PopupTrigger, NoteSearch, NotesListRegion, StatusText
        };
        foreach (var element in keyboardPathControls)
        {
            if (element is Control control)
            {
                control.FocusVisualStyle = keyboardFocusVisual;
            }
        }

        SetAutomationIds(
            (ThemeModeButton, "ThemeModeButton"),
            (TopmostCheck, "TopmostCheck"),
            (MinimizeButton, "MinimizeButton"),
            (CloseButton, "CloseButton"),
            (CountdownName, "CountdownName"),
            (CountdownDate, "CountdownDate"),
            (CountdownHour, "CountdownHour"),
            (CountdownMinute, "CountdownMinute"),
            (CountdownSecond, "CountdownSecond"),
            (SetNowButton, "SetNowButton"),
            (CountdownTags.PopupTrigger, "CountdownTags"),
            (ManageCountdownReminderRulesButton, "ManageCountdownReminderRulesButton"),
            (CancelCountdownEdit, "CancelCountdownEdit"),
            (SaveCountdownButton, "SaveCountdownButton"),
            (DisplayPrecision, "DisplayPrecision"),
            (MonthlySalary, "MonthlySalary"),
            (WorkStart, "WorkStart"),
            (WorkEnd, "WorkEnd"),
            (SavePreferencesButton, "SavePreferencesButton"),
            (CountdownListRegion, "CountdownListRegion"),
            (NoteText, "NoteText"),
            (NoteDate, "NoteDate"),
            (SaveNoteButton, "SaveNoteButton"),
            (CancelNoteEdit, "CancelNoteEdit"),
            (ManageNoteReminderRulesButton, "ManageNoteReminderRulesButton"),
            (NoteTags.PopupTrigger, "NoteTags"),
            (NoteSearch, "NoteSearch"),
            (NotesListRegion, "NotesListRegion"),
            (StatusText, "StatusText"));
        NameControl(NewTagInput, "新标签名称");
        NameControl(CreateTagButton, "新建标签");
        NameControl(NoteDateFilter, "按便签日期筛选");
        ThemedDatePickerBehavior.Attach(CountdownDate);
        ThemedDatePickerBehavior.Attach(NoteDate);
        ThemedDatePickerBehavior.Attach(NoteDateFilter);
        UseShortDateText(NoteDate);
        UiAccessibility.MarkLive(StatusText);
        UiAccessibility.MarkLive(CountdownMessage);
        UiAccessibility.MarkLive(NoteMessage);
        UiAccessibility.MarkLive(TagManagerMessage);
    }

    private void MinimizeToTray() => MinimizeToTrayRequested?.Invoke(this, EventArgs.Empty);

    private static bool IsTextEntryFocus(IInputElement? focused)
    {
        if (focused is ComboBoxItem or DatePickerTextBox or System.Windows.Controls.Calendar or CalendarDayButton)
        {
            return true;
        }

        for (var node = focused as DependencyObject; node is not null;)
        {
            if (node is TextBoxBase or DatePicker or ComboBox)
            {
                return true;
            }

            var visualParent = node is Visual ? VisualTreeHelper.GetParent(node) : null;
            node = visualParent ?? LogicalTreeHelper.GetParent(node);
        }

        return false;
    }

    private static void SetAutomationIds(params (DependencyObject Control, string Id)[] controls)
    {
        foreach (var (control, id) in controls)
        {
            AutomationProperties.SetAutomationId(control, id);
        }
    }

    private void WireEvents()
    {
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        MinimizeButton.Click += (_, _) => MinimizeToTray();
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
        SetNowButton.Click += (_, _) => SetCountdownTarget(_desktopWorkspace.Snapshot.BeijingNow);
        ManageCountdownReminderRulesButton.Click += (_, _) => OpenCountdownReminderRulesEditor();
        SaveCountdownButton.Click += (_, _) => SaveCountdown();
        CancelCountdownEdit.Click += (_, _) => ResetCountdownEditor();
        SavePreferencesButton.Click += (_, _) => SavePreferences();
        DisplayPrecision.SelectionChanged += (_, _) => MarkSettingsDraftDirty();
        MonthlySalary.TextChanged += (_, _) => MarkSettingsDraftDirty();
        WorkStart.TextChanged += (_, _) => MarkSettingsDraftDirty();
        WorkEnd.TextChanged += (_, _) => MarkSettingsDraftDirty();
        ThemeModeButton.Click += (_, _) => CycleThemePreference();
        SaveNoteButton.Click += (_, _) => SaveNote();
        CancelNoteEdit.Click += (_, _) => ResetNoteEditor();
        ManageNoteReminderRulesButton.Click += (_, _) => OpenNoteReminderRulesEditor();
        NoteSearch.TextChanged += (_, _) =>
        {
            if (_syncingNoteSearch)
            {
                return;
            }

            UpdateFilter(filter => filter with { NoteSearch = NoteSearch.Text });
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
            SetNoteStatusSectionState(
                ToggleActiveNotes,
                ActiveNoteRows,
                ActiveNotesChevron,
                "未完成",
                ActiveNotesCount.Text,
                _activeNotesExpanded);
        };
        ToggleCompletedNotes.Click += (_, _) =>
        {
            _completedNotesExpanded = !_completedNotesExpanded;
            SetNoteStatusSectionState(
                ToggleCompletedNotes,
                CompletedNoteRows,
                CompletedNotesChevron,
                "已完成",
                CompletedNotesCount.Text,
                _completedNotesExpanded);
        };
        CountdownTags.CreateRequested += CreateTagFromSelector;
        NoteTags.CreateRequested += CreateTagFromSelector;
        CreateTagButton.Click += (_, _) => CreateTag(NewTagInput.Text, null);

        AnyTagMode.Click += (_, _) =>
        {
            UpdateFilter(filter => filter with { MatchMode = TagMatchMode.Any });
            AnyTagMode.IsChecked = true;
            AllTagMode.IsChecked = false;
            RenderVisibleWorkspace();
        };
        AllTagMode.Click += (_, _) =>
        {
            UpdateFilter(filter => filter with { MatchMode = TagMatchMode.All });
            AnyTagMode.IsChecked = false;
            AllTagMode.IsChecked = true;
            RenderVisibleWorkspace();
        };
        UntaggedOnly.Click += (_, _) =>
        {
            UpdateFilter(filter => filter with { UntaggedOnly = UntaggedOnly.IsChecked == true });
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
            var undatedOnly = NoteUndatedFilter.IsChecked == true;
            UpdateFilter(filter => filter with
            {
                UndatedNotesOnly = undatedOnly,
                NoteDate = undatedOnly ? null : filter.NoteDate
            });
            if (_desktopWorkspace.Snapshot.Filter.UndatedNotesOnly)
            {
                NoteDateFilter.SelectedDate = null;
            }

            RenderNotes();
            UpdateFilterSummary();
        };
        NoteDateFilter.SelectedDateChanged += (_, _) =>
        {
            var noteDate = NoteDateFilter.SelectedDate?.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            UpdateFilter(filter => filter with
            {
                NoteDate = noteDate,
                UndatedNotesOnly = noteDate is null && filter.UndatedNotesOnly
            });
            if (_desktopWorkspace.Snapshot.Filter.NoteDate is not null)
            {
                NoteUndatedFilter.IsChecked = false;
            }

            RenderNotes();
            UpdateFilterSummary();
        };
        ClearNoteDateFilter.Click += (_, _) =>
        {
            NoteDateFilter.SelectedDate = null;
            UpdateFilter(filter => filter with { NoteDate = null, UndatedNotesOnly = false });
            NoteUndatedFilter.IsChecked = false;
            RenderNotes();
            UpdateFilterSummary();
        };
        ClearFilterButton.Click += (_, _) => ClearFilters();
        ResetEmptyNotesFiltersButton.Click += (_, _) => ClearFilters();
        ResetEmptyCountdownFiltersButton.Click += (_, _) => ClearFilters();
    }

    private void OnWorkspaceStateChanged(object? sender, EventArgs eventArgs)
    {
        Dispatcher.Invoke(RefreshAll);
    }

    private void RefreshAll()
    {
        var snapshot = _desktopWorkspace.Snapshot;
        CountdownTags.SetTags(snapshot.Settings.Tags);
        NoteTags.SetTags(snapshot.Settings.Tags);
        SyncNoteSearchFromFilter();
        RenderFilterTags();
        RenderTagManager();
        RenderVisibleWorkspace();
        LoadPreferences();
    }

    private void SyncNoteSearchFromFilter()
    {
        var term = _desktopWorkspace.Snapshot.Filter.NoteSearch ?? string.Empty;
        if (string.Equals(NoteSearch.Text, term, StringComparison.Ordinal))
        {
            return;
        }

        _syncingNoteSearch = true;
        try
        {
            NoteSearch.Text = term;
        }
        finally
        {
            _syncingNoteSearch = false;
        }
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

    private void RenderCountdowns(DateTimeOffset? nowUtc = null)
    {
        var snapshot = _desktopWorkspace.Snapshot;
        var effectiveNowUtc = (nowUtc ?? snapshot.UtcNow).ToUniversalTime();
        var visibleIds = snapshot.VisibleCountdowns
            .Select(static countdown => countdown.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var records = _reminderSourceCountdownId is null
            ? snapshot.VisibleCountdowns
            : snapshot.Settings.Countdowns
                .Where(countdown =>
                    visibleIds.Contains(countdown.Id) ||
                    string.Equals(
                        countdown.Id,
                        _reminderSourceCountdownId,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        RecordCountText.Text = $"{records.Count} 个倒计时";
        var signature = string.Join(
            "|",
            records.Select(record =>
                $"{record.Id}:{record.Name}:{record.TargetUtc.UtcTicks}:{string.Join(',', record.TagIds)}:{record.TargetUtc > effectiveNowUtc}:{snapshot.Settings.Preferences.DisplayPrecision}:{_themeMode}"));
        var shouldRebuild = !string.Equals(
            signature,
            _countdownRenderSignature,
            StringComparison.Ordinal);
        if (records.Count == 0)
        {
            if (shouldRebuild)
            {
                CountdownRowsWereRebuiltForValidation = true;
                CountdownRowRebuildGenerationForValidation++;
                _countdownRenderSignature = signature;
                CountdownRows.Children.Clear();
                _countdownSourceRows.Clear();
                _countdownSourceFocusTargets.Clear();
                _countdownRemainingTexts.Clear();
                _countdownActiveStates.Clear();
            }

            var hasSavedCountdowns = snapshot.Settings.Countdowns.Count > 0;
            var emptyState = CountdownEmptyStatePresentation(hasSavedCountdowns);
            EmptyCountdownMessage.Text = emptyState.Message;
            AutomationProperties.SetLiveSetting(EmptyCountdownMessage, AutomationLiveSetting.Polite);
            EmptyCountdownMessage.Visibility = Visibility.Visible;
            ResetEmptyCountdownFiltersButton.Visibility = emptyState.ShowReset
                ? Visibility.Visible
                : Visibility.Collapsed;
            AutomationProperties.SetName(EmptyCountdownMessage, EmptyCountdownMessage.Text);
            return;
        }
        if (!shouldRebuild)
        {
            foreach (var record in records)
            {
                if (_countdownRemainingTexts.TryGetValue(record.Id, out var remaining))
                {
                    SetCountdownRemainingText(remaining, CountdownFormatting.FormatRemaining(
                        record.TargetUtc,
                        effectiveNowUtc,
                        snapshot.Settings.Preferences.DisplayPrecision));
                }
            }

            EmptyCountdownMessage.Visibility = Visibility.Collapsed;
            ResetEmptyCountdownFiltersButton.Visibility = Visibility.Collapsed;
            return;
        }

        CountdownRowsWereRebuiltForValidation = true;
        CountdownRowRebuildGenerationForValidation++;
        _countdownRenderSignature = signature;
        CountdownRows.Children.Clear();
        _countdownSourceRows.Clear();
        _countdownSourceFocusTargets.Clear();
        _countdownRemainingTexts.Clear();
        _countdownActiveStates.Clear();
        EmptyCountdownMessage.Visibility = Visibility.Collapsed;
        ResetEmptyCountdownFiltersButton.Visibility = Visibility.Collapsed;

        foreach (var record in records)
        {
            var active = record.TargetUtc > effectiveNowUtc;
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
            layout.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(CountdownRemainingColumnWidth),
                MinWidth = CountdownRemainingColumnWidth,
                MaxWidth = CountdownRemainingColumnWidth
            });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock
            {
                Text = record.Name,
                FontSize = 25,
                FontWeight = FontWeights.SemiBold
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            var remaining = new TextBlock
            {
                Margin = new Thickness(CountdownRemainingLeftMargin, 5, 0, 0),
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                FontSize = CountdownRemainingBaseFontSize,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = CountdownRemainingColumnWidth - CountdownRemainingLeftMargin
            };
            SetCountdownRemainingText(remaining, CountdownFormatting.FormatRemaining(
                record.TargetUtc,
                effectiveNowUtc,
                snapshot.Settings.Preferences.DisplayPrecision));
            remaining.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            remaining.SetResourceReference(
                TextBlock.ForegroundProperty,
                active ? "BodyBrush" : "DangerBrush");
            var tagChips = CreateTagChips(record.TagIds);
            Grid.SetColumnSpan(name, 2);
            Grid.SetRow(tagChips, 1);
            Grid.SetColumn(remaining, 1);
            Grid.SetRow(remaining, 1);
            Grid.SetColumn(actions, 2);
            Grid.SetRowSpan(actions, 2);
            layout.Children.Add(name);
            layout.Children.Add(tagChips);
            layout.Children.Add(remaining);
            layout.Children.Add(actions);
            var card = Card(layout, active ? "CardBrush" : "ExpiredBrush");
            _countdownSourceRows[record.Id] = card;
            _countdownSourceFocusTargets[record.Id] = edit;
            _countdownRemainingTexts[record.Id] = remaining;
            _countdownActiveStates[record.Id] = active;
            if (string.Equals(
                    record.Id,
                    _reminderSourceCountdownId,
                    StringComparison.OrdinalIgnoreCase))
            {
                card.BorderThickness = new Thickness(2);
                card.SetResourceReference(Border.BorderBrushProperty, "ActiveBrush");
                UiAccessibility.Name(card, $"当前提醒来源倒计时：{record.Name}");
            }

            CountdownRows.Children.Add(card);
        }
    }

    private static void SetCountdownRemainingText(TextBlock textBlock, string text)
    {
        textBlock.Text = text;
        textBlock.FontSize = CountdownRemainingBaseFontSize;
        var availableWidth = CountdownRemainingColumnWidth - CountdownRemainingLeftMargin;
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            textBlock.FlowDirection,
            new Typeface(
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch),
            CountdownRemainingBaseFontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        var requiredWidth = formatted.WidthIncludingTrailingWhitespace +
            Math.Max(0, formatted.OverhangTrailing) +
            2 / dpi.DpiScaleX;
        if (requiredWidth <= availableWidth)
        {
            return;
        }

        var fitted = Math.Floor(
            CountdownRemainingBaseFontSize * availableWidth / requiredWidth * 4) / 4;
        textBlock.FontSize = Math.Max(CountdownRemainingMinimumFontSize, fitted);
    }

    private void RenderNotes(DateTimeOffset? nowUtc = null)
    {
        var snapshot = _desktopWorkspace.Snapshot;
        var effectiveNowUtc = (nowUtc ?? snapshot.UtcNow).ToUniversalTime();
        _lastRenderedNoteMinuteUtc = effectiveNowUtc.UtcDateTime.Ticks / TimeSpan.TicksPerMinute;
        ActiveNoteRows.Children.Clear();
        CompletedNoteRows.Children.Clear();
        _noteCompletionBoxes.Clear();
        _noteSourceRows.Clear();
        _noteSourceFocusTargets.Clear();
        var visibleIds = snapshot.VisibleNotes
            .Select(static note => note.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var records = _reminderSourceNoteId is null
            ? snapshot.VisibleNotes
            : snapshot.AllNotes
                .Where(note =>
                    visibleIds.Contains(note.Id) ||
                    string.Equals(note.Id, _reminderSourceNoteId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var active = records.Where(note => !note.IsCompleted).ToArray();
        var completed = records.Where(note => note.IsCompleted).ToArray();
        var revealNoteId = _reminderSourceNoteId;
        var revealActive = revealNoteId is not null &&
            active.Any(note =>
                string.Equals(note.Id, revealNoteId, StringComparison.OrdinalIgnoreCase));
        var revealCompleted = revealNoteId is not null &&
            completed.Any(note =>
                string.Equals(note.Id, revealNoteId, StringComparison.OrdinalIgnoreCase));
        RecordCountText.Text = $"{active.Length} 个待办";
        AddNotesGroupedByDay(ActiveNoteRows, active, isCompleted: false, effectiveNowUtc, revealNoteId);
        AddNotesGroupedByDay(CompletedNoteRows, completed, isCompleted: true, effectiveNowUtc, revealNoteId);
        ActiveNotesCount.Text = active.Length.ToString(CultureInfo.InvariantCulture);
        CompletedNotesCount.Text = completed.Length.ToString(CultureInfo.InvariantCulture);
        ToggleActiveNotes.Visibility = active.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToggleCompletedNotes.Visibility = completed.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetNoteStatusSectionState(
            ToggleActiveNotes,
            ActiveNoteRows,
            ActiveNotesChevron,
            "未完成",
            ActiveNotesCount.Text,
            _activeNotesExpanded || revealActive);
        SetNoteStatusSectionState(
            ToggleCompletedNotes,
            CompletedNoteRows,
            CompletedNotesChevron,
            "已完成",
            CompletedNotesCount.Text,
            _completedNotesExpanded || revealCompleted);
        if (active.Length == 0)
        {
            ActiveNoteRows.Visibility = Visibility.Collapsed;
        }

        if (completed.Length == 0)
        {
            CompletedNoteRows.Visibility = Visibility.Collapsed;
        }

        var noVisibleNotes = records.Count == 0;
        var emptyState = NoteEmptyStatePresentation(snapshot.Settings.Notes.Count > 0);
        EmptyNotesMessage.Text = emptyState.Message;
        AutomationProperties.SetLiveSetting(EmptyNotesMessage, AutomationLiveSetting.Polite);
        EmptyNotesMessage.Visibility = noVisibleNotes
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetName(EmptyNotesMessage, EmptyNotesMessage.Text);
        ResetEmptyNotesFiltersButton.Visibility = noVisibleNotes && emptyState.ShowReset
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static (string Message, bool ShowReset) NoteEmptyStatePresentation(bool hasSavedNotes) =>
        hasSavedNotes
            ? ("没有符合当前筛选条件的便签。", true)
            : ("还没有便签，先写下一件想记住的事。", false);

    private static void SetNoteStatusSectionState(
        Button toggle,
        Panel rows,
        WpfPath chevron,
        string status,
        string count,
        bool isExpanded)
    {
        if (chevron.RenderTransform is RotateTransform rotation)
        {
            rotation.Angle = isExpanded ? 0 : -90;
        }

        var action = isExpanded ? "收起" : "展开";
        var expansion = isExpanded ? "当前展开" : "当前收起";
        toggle.ToolTip = $"{action}{status}便签";
        AutomationProperties.SetName(toggle, $"{action}{expansion}的{status}便签，共 {count} 条");
        AutomationProperties.SetHelpText(toggle, expansion);
        rows.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddNotesGroupedByDay(
        Panel target,
        IReadOnlyList<NoteRecord> notes,
        bool isCompleted,
        DateTimeOffset nowUtc,
        string? revealNoteId = null)
    {
        foreach (var group in notes.GroupBy(note => note.Date, StringComparer.Ordinal))
        {
            var isFirstSection = target.Children.Count == 0;
            var groupedNotes = group.ToArray();
            var dateLabel = FormatNoteDateLabel(group.Key, nowUtc);
            var sectionKey = (IsCompleted: isCompleted, Date: group.Key);
            var sectionRows = new StackPanel { Uid = "NoteDateRows" };
            foreach (var note in groupedNotes)
            {
                sectionRows.Children.Add(CreateNoteRow(note, nowUtc));
            }

            var isExpanded = !_collapsedNoteDateSections.Contains(sectionKey) ||
                (revealNoteId is not null &&
                 groupedNotes.Any(note =>
                     string.Equals(note.Id, revealNoteId, StringComparison.OrdinalIgnoreCase)));
            var chevronRotation = new RotateTransform(isExpanded ? 0 : -90);
            var sectionToggle = new Button
            {
                Uid = "NoteDateDisclosure",
                Tag = group.Key ?? string.Empty,
                Margin = new Thickness(0, 0, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)MainContentProjection.FindResource("MainNoteDateDisclosureButton"),
                Content = CreateNoteDateDisclosureContent(
                    dateLabel,
                    groupedNotes.Length,
                    isCompleted,
                    chevronRotation)
            };
            SetNoteDateSectionState(
                sectionToggle,
                sectionRows,
                chevronRotation,
                dateLabel,
                groupedNotes.Length,
                isCompleted,
                isExpanded);
            sectionToggle.Click += (_, _) =>
            {
                var expand = _collapsedNoteDateSections.Remove(sectionKey);
                if (!expand)
                {
                    _collapsedNoteDateSections.Add(sectionKey);
                }

                SetNoteDateSectionState(
                    sectionToggle,
                    sectionRows,
                    chevronRotation,
                    dateLabel,
                    groupedNotes.Length,
                    isCompleted,
                    expand);
            };

            var timeline = new Grid
            {
                Uid = "NoteDateTimeline",
                Width = 30
            };
            var timelineLine = new Border
            {
                Uid = "NoteTimelineLine",
                Width = 1,
                Margin = new Thickness(0, isFirstSection ? 22 : 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            timelineLine.SetResourceReference(Border.BackgroundProperty, "NoteTimelineBrush");
            var timelineNode = new WpfEllipse
            {
                Uid = "NoteTimelineNode",
                Width = 10,
                Height = 10,
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                StrokeThickness = 2
            };
            timelineNode.SetResourceReference(
                System.Windows.Shapes.Shape.FillProperty,
                "CanvasBrush");
            timelineNode.SetResourceReference(
                System.Windows.Shapes.Shape.StrokeProperty,
                isCompleted ? "NoteCompletedRailBrush" : "NoteActiveRailBrush");
            timeline.Children.Add(timelineLine);
            timeline.Children.Add(timelineNode);

            var content = new StackPanel();
            content.Children.Add(sectionToggle);
            content.Children.Add(sectionRows);

            var section = new Grid
            {
                Uid = "NoteDateTimelineSection",
                Margin = new Thickness(0, 0, 0, 12)
            };
            section.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            section.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(content, 1);
            section.Children.Add(timeline);
            section.Children.Add(content);
            target.Children.Add(section);
        }
    }

    private Grid CreateNoteDateDisclosureContent(
        string dateLabel,
        int noteCount,
        bool isCompleted,
        RotateTransform chevronRotation)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Uid = "NoteDateLabel",
            Text = dateLabel,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "BodyBrush");
        var count = new TextBlock
        {
            Uid = "NoteDateCount",
            Text = $"{noteCount} 条",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15,
            Typography =
            {
                NumeralAlignment = FontNumeralAlignment.Tabular
            }
        };
        count.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var chevron = new WpfPath
        {
            Uid = "NoteDateChevron",
            Width = 12,
            Height = 8,
            Margin = new Thickness(13, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse("M 1,1 L 6,6 L 11,1"),
            RenderTransform = chevronRotation,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Stretch = Stretch.Uniform,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        chevron.SetResourceReference(
            System.Windows.Shapes.Shape.StrokeProperty,
            isCompleted ? "MutedBrush" : "BodyBrush");

        Grid.SetColumn(count, 1);
        Grid.SetColumn(chevron, 2);
        content.Children.Add(label);
        content.Children.Add(count);
        content.Children.Add(chevron);
        return content;
    }

    private static string FormatNoteDateLabel(string? dateValue, DateTimeOffset nowUtc)
    {
        if (dateValue is null)
        {
            return "未设置日期";
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

        var today = ChinaStandardTime.ToBeijingTime(nowUtc).Date;
        var calendarDate = date.Year == today.Year
            ? $"{date.Month} 月 {date.Day} 日"
            : $"{date.Year} 年 {date.Month} 月 {date.Day} 日";
        if (date.Date == today)
        {
            return $"今天 · {calendarDate}";
        }

        if (date.Date == today.AddDays(-1))
        {
            return $"昨天 · {calendarDate}";
        }

        var weekday = CultureInfo.GetCultureInfo("zh-CN")
            .DateTimeFormat
            .GetAbbreviatedDayName(date.DayOfWeek);
        return $"{calendarDate} · {weekday}";
    }

    private static void SetNoteDateSectionState(
        Button toggle,
        Panel rows,
        RotateTransform chevronRotation,
        string dateLabel,
        int noteCount,
        bool isCompleted,
        bool isExpanded)
    {
        var action = isExpanded ? "收起" : "展开";
        var status = isCompleted ? "已完成" : "未完成";
        var expansion = isExpanded ? "当前展开" : "当前收起";
        chevronRotation.Angle = isExpanded ? 0 : -90;
        toggle.ToolTip = $"{action}{dateLabel}的{status}便签";
        AutomationProperties.SetName(
            toggle,
            $"{action}{expansion}的{dateLabel}的{status}便签，共 {noteCount} 条");
        AutomationProperties.SetHelpText(toggle, expansion);
        rows.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement CreateNoteRow(NoteRecord note, DateTimeOffset nowUtc)
    {
        var completion = UiAccessibility.Name(
            new CheckBox
            {
                IsChecked = note.IsCompleted,
                Tag = note.Id,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 10, 0),
                Style = (Style)MainContentProjection.FindResource("MainNoteCompletionCheck")
            },
            note.IsCompleted ? $"恢复便签 {note.Text}" : $"完成便签 {note.Text}");
        completion.Checked += OnNoteCompletionChanged;
        completion.Unchecked += OnNoteCompletionChanged;
        _noteCompletionBoxes[note.Id] = completion;

        var edit = NoteActionButton("编辑", note.Id, $"编辑便签 {note.Text}");
        edit.Click += OnEditNote;
        var delete = NoteActionButton("删除", note.Id, $"删除便签 {note.Text}");
        delete.Style = (Style)FindResource("DangerButton");
        delete.Click += OnDeleteNote;
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(edit);
        actions.Children.Add(delete);

        var body = new StackPanel();
        var noteText = new TextBlock
        {
            Text = note.Text,
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = note.IsCompleted ? TextDecorations.Strikethrough : null
        };
        noteText.SetResourceReference(
            TextBlock.ForegroundProperty,
            note.IsCompleted ? "MutedBrush" : "InkBrush");
        body.Children.Add(noteText);
        var details = FormatNoteDetails(_desktopWorkspace.Snapshot.Settings, note, nowUtc);
        if (!string.IsNullOrWhiteSpace(details))
        {
            var detailText = new TextBlock
            {
                Text = details,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = NoteCardReminderSummaryFontSize
            };
            detailText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            AutomationProperties.SetAutomationId(detailText, NoteCardReminderSummaryAutomationId);
            UiAccessibility.Name(detailText, $"便签卡提醒摘要：{details}");
            body.Children.Add(detailText);
        }
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
        var card = NoteCard(grid);
        _noteSourceRows[note.Id] = card;
        _noteSourceFocusTargets[note.Id] = edit;
        if (string.Equals(note.Id, _reminderSourceNoteId, StringComparison.OrdinalIgnoreCase))
        {
            card.BorderThickness = new Thickness(2);
            card.SetResourceReference(Border.BorderBrushProperty, "ActiveBrush");
            UiAccessibility.Name(card, $"当前提醒来源便签：{note.Text}");
        }

        return card;
    }

    private static string FormatNoteDetails(NoteRecord note, DateTimeOffset nowUtc)
        => FormatNoteDetails(
            CountdownSettings.Empty with { Notes = new[] { note } },
            note,
            nowUtc);

    private static string FormatNoteDetails(
        CountdownSettings settings,
        NoteRecord note,
        DateTimeOffset nowUtc)
    {
        if (note.ReminderRules.Count == 0)
        {
            return string.Empty;
        }

        if (note.ReminderRules.Count == 1 &&
            note.ReminderRules[0] is AbsoluteOnceReminderRule once)
        {
            if (settings.ReminderStates.TryGetValue(once.Id, out var state) &&
                state.HandledThroughUtc >= once.ScheduledUtc)
            {
                return "已提醒";
            }

            var due = ChinaStandardTime.ToBeijingTime(once.ScheduledUtc);
            var suffix = note.IsCompleted ? "（已暂停）" : string.Empty;
            return $"一次提醒 {due:M 月 d 日 HH:mm}{suffix}";
        }

        if (note.ReminderRules.Count == 1 &&
            note.ReminderRules[0] is FixedIntervalReminderRule repeating)
        {
            var fixedNext = ReminderSchedule.GetNextDueUtc(settings, note, nowUtc);
            if (note.IsCompleted)
            {
                return $"每 {repeating.Interval.Minutes} 分钟（已暂停）";
            }

            return $"下次 {ChinaStandardTime.ToBeijingTime(fixedNext!.Value):M 月 d 日 HH:mm} · 每 {repeating.Interval.Minutes} 分钟";
        }

        var summary = note.ReminderRules.Count == 1 ? "日历重复" : $"{note.ReminderRules.Count} 条提醒";
        var nextReminder = ReminderSchedule.GetNextDueUtc(
            settings,
            note,
            nowUtc);
        if (note.IsCompleted)
        {
            return $"{summary}（已暂停）";
        }

        return nextReminder is null
            ? summary
            : $"下次 {ChinaStandardTime.ToBeijingTime(nextReminder.Value):M 月 d 日 HH:mm} · {summary}";
    }

    private void SaveCountdown()
    {
        if (string.IsNullOrWhiteSpace(CountdownName.Text) || CountdownDate.SelectedDate is null)
        {
            SetMessage(CountdownMessage, "请填写名称并选择截止日期。", true);
            return;
        }

        var target = GetCountdownTargetUtc()!.Value;
        var result = _desktopWorkspace.Apply(new SaveCountdownChange(
            _editingCountdownId,
            CountdownName.Text,
            target,
            CountdownTags.SelectedTagIds.ToArray(),
            _countdownReminderRules));
        if (!result.Succeeded)
        {
            SetMessage(CountdownMessage, $"保存失败：{result.Error}", true);
            if (result.ReminderRuleErrorTarget != ReminderRuleEditingErrorTarget.None)
            {
                OpenCountdownReminderRulesEditor(
                    result.ReminderRuleErrorTarget,
                    result.Error);
            }
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

        var existing = _editingNoteId is null
            ? null
            : _desktopWorkspace.Snapshot.Settings.Notes
                .FirstOrDefault(note => note.Id == _editingNoteId);
        if (NoteDate.SelectedDate is null)
        {
            if (existing is null)
            {
                SetMessage(NoteMessage, "请选择便签日期，当前草稿已保留。", true);
                return;
            }

            if (existing.Date is not null)
            {
                SetMessage(NoteMessage, "该便签已设置日期，不能清空日期保存，当前草稿已保留。", true);
                return;
            }
        }

        var result = _desktopWorkspace.Apply(new SaveNoteChange(
            _editingNoteId,
            NoteText.Text,
            NoteDate.SelectedDate,
            existing?.IsCompleted ?? false,
            NoteTags.SelectedTagIds.ToArray(),
            _noteReminderRules));
        if (!result.Succeeded)
        {
            SetMessage(NoteMessage, $"保存失败：{result.Error}", true);
            if (result.ReminderRuleErrorTarget != ReminderRuleEditingErrorTarget.None)
            {
                OpenNoteReminderRulesEditor(
                    result.ReminderRuleErrorTarget,
                    result.Error);
            }
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

        if (salary < 0)
        {
            SetMessage(StatusText, "月薪不能小于 0。", true);
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
        var result = _desktopWorkspace.Apply(new SavePreferencesChange(
            precision,
            salary,
            workStart,
            workEnd));
        if (!result.Succeeded)
        {
            SetMessage(StatusText, $"设置保存失败：{result.Error}", true);
            return;
        }

        _settingsDraftDirty = false;
        LoadPreferences();
        SetMessage(StatusText, "偏好已保存。", false);
    }

    private void OnEditCountdown(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var record = _desktopWorkspace.Snapshot.Settings.Countdowns.First(item => item.Id == id);
        BeginEditCountdown(record);
    }

    private void BeginEditCountdown(CountdownRecord record)
    {
        var beijing = ChinaStandardTime.ToBeijingTime(record.TargetUtc);
        _editingCountdownId = record.Id;
        CountdownName.Text = record.Name;
        CountdownDate.SelectedDate = beijing.Date;
        CountdownHour.SelectedIndex = beijing.Hour;
        CountdownMinute.SelectedIndex = beijing.Minute;
        CountdownSecond.SelectedIndex = beijing.Second;
        CountdownTags.SetSelection(record.TagIds);
        _countdownReminderRules = Array.AsReadOnly(record.ReminderRules.ToArray());
        UpdateCountdownReminderRulesSummary();
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

        var record = _desktopWorkspace.Snapshot.Settings.Countdowns.First(item => item.Id == id);
        if (new ConfirmationDialog(this, "删除倒计时", $"确定删除“{record.Name}”吗？").ShowDialog() != true)
        {
            return;
        }

        var result = _desktopWorkspace.Apply(new DeleteCountdownChange(id));
        if (!result.Succeeded)
        {
            SetMessage(StatusText, $"删除失败：{result.Error}", true);
        }
    }

    private void OnEditNote(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var note = _desktopWorkspace.Snapshot.Settings.Notes.First(item => item.Id == id);
        LoadNoteIntoEditor(note);
        SaveNoteButton.Content = "保存";
        CancelNoteEdit.Visibility = Visibility.Visible;
    }

    internal void LoadNoteIntoEditor(NoteRecord note)
    {
        _editingNoteId = note.Id;
        NoteText.Text = note.Text;
        _settingDefaultDate = true;
        NoteDate.SelectedDate = note.Date is null
            ? null
            : DateTime.ParseExact(note.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        _settingDefaultDate = false;
        _noteDateDirty = true;
        NoteTags.SetSelection(note.TagIds);
        _noteReminderRules = Array.AsReadOnly(note.ReminderRules.ToArray());
        UpdateNoteReminderRulesSummary();
        SaveNoteButton.Content = "保存";
        CancelNoteEdit.Visibility = Visibility.Visible;
    }

    private void OnDeleteNote(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var note = _desktopWorkspace.Snapshot.Settings.Notes.First(item => item.Id == id);
        if (new ConfirmationDialog(this, "删除便签", $"确定删除“{note.Text}”吗？").ShowDialog() != true)
        {
            return;
        }

        var result = _desktopWorkspace.Apply(new DeleteNoteChange(id));
        if (!result.Succeeded)
        {
            SetMessage(StatusText, $"删除失败：{result.Error}", true);
        }
    }

    private void OnNoteCompletionChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not CheckBox { Tag: string id } checkBox)
        {
            return;
        }

        var desired = checkBox.IsChecked == true;
        var current = _desktopWorkspace.Snapshot.Settings.Notes.FirstOrDefault(note => note.Id == id);
        if (current is null || current.IsCompleted == desired)
        {
            return;
        }

        var result = _desktopWorkspace.Apply(new SetNoteCompletionChange(id, desired));
        if (!result.Succeeded)
        {
            SetMessage(StatusText, $"便签状态保存失败：{result.Error}", true);
            checkBox.IsChecked = current.IsCompleted;
        }
    }

    private void ResetCountdownEditor()
    {
        _editingCountdownId = null;
        CountdownName.Clear();
        SetCountdownTarget(_desktopWorkspace.Snapshot.BeijingNow.AddDays(1));
        CountdownTags.SetSelection(Array.Empty<string>());
        _countdownReminderRules = Array.Empty<ReminderRule>();
        UpdateCountdownReminderRulesSummary();
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
        var snapshot = _desktopWorkspace.Snapshot;
        NoteDate.SelectedDate = snapshot.BeijingNow.Date;
        _settingDefaultDate = false;
        _noteDateDirty = false;
        NoteTags.SetSelection(snapshot.Filter.SelectedTagIds);
        _noteReminderRules = Array.Empty<ReminderRule>();
        UpdateNoteReminderRulesSummary();
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

    private void MarkSettingsDraftDirty()
    {
        if (!_loadingPreferences)
        {
            _settingsDraftDirty = true;
        }
    }

    private bool HasUnsavedSettingsEdits(CountdownPreferences preferences)
    {
        if (!_settingsDraftDirty)
        {
            return false;
        }

        return
            DisplayPrecision.SelectedIndex != PrecisionIndex(preferences.DisplayPrecision) ||
            !string.Equals(
                MonthlySalary.Text,
                preferences.MonthlySalary.ToString(CultureInfo.CurrentCulture),
                StringComparison.Ordinal) ||
            !string.Equals(
                WorkStart.Text,
                CountdownFormatting.FormatWorkTime(preferences.WorkStart),
                StringComparison.Ordinal) ||
            !string.Equals(
                WorkEnd.Text,
                CountdownFormatting.FormatWorkTime(preferences.WorkEnd),
                StringComparison.Ordinal);
    }

    private static int PrecisionIndex(string displayPrecision) => displayPrecision switch
    {
        "Day" => 0,
        "Hour" => 1,
        "Minute" => 2,
        _ => 3
    };

    private void LoadPreferences()
    {
        var preferences = _desktopWorkspace.Snapshot.Settings.Preferences;
        if (HasUnsavedSettingsEdits(preferences))
        {
            UpdateThemePreferenceButton(preferences.ThemePreference);
            return;
        }

        _settingsDraftDirty = false;
        _loadingPreferences = true;
        try
        {
            DisplayPrecision.SelectedIndex = PrecisionIndex(preferences.DisplayPrecision);
            MonthlySalary.Text = preferences.MonthlySalary.ToString(CultureInfo.CurrentCulture);
            WorkStart.Text = CountdownFormatting.FormatWorkTime(preferences.WorkStart);
            WorkEnd.Text = CountdownFormatting.FormatWorkTime(preferences.WorkEnd);
        }
        finally
        {
            _loadingPreferences = false;
        }

        UpdateThemePreferenceButton(preferences.ThemePreference);
    }

    private void CycleThemePreference()
    {
        var nextPreference = _desktopWorkspace.Snapshot.Settings.Preferences.ThemePreference switch
        {
            ThemePreferenceMode.Auto => ThemePreferenceMode.Day,
            ThemePreferenceMode.Day => ThemePreferenceMode.Night,
            ThemePreferenceMode.Night => ThemePreferenceMode.Auto,
            _ => throw new InvalidOperationException("未知的主题偏好。")
        };
        var result = _desktopWorkspace.Apply(new SaveThemePreferenceChange(nextPreference));
        if (!result.Succeeded)
        {
            SetMessage(StatusText, $"主题设置保存失败：{result.Error}", true);
            return;
        }

        UpdateThemePreferenceButton(nextPreference);
        ThemePreferenceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateThemePreferenceButton(ThemePreferenceMode preference)
    {
        var (label, nextLabel) = preference switch
        {
            ThemePreferenceMode.Auto => ("◐ 自动", "日间"),
            ThemePreferenceMode.Day => ("☀ 日间", "夜间"),
            ThemePreferenceMode.Night => ("☾ 夜间", "自动"),
            _ => throw new InvalidOperationException("未知的主题偏好。")
        };
        var description = $"主题模式：{label[2..]}；点击切换为{nextLabel}";
        ThemeModeButton.Content = label;
        ThemeModeButton.ToolTip = description;
        AutomationProperties.SetName(ThemeModeButton, description);
    }

    private void OpenNoteReminderRulesEditor(
        ReminderRuleEditingErrorTarget initialErrorTarget = ReminderRuleEditingErrorTarget.None,
        string initialError = "")
    {
        var editor = new ReminderRulesEditorWindow(
            _noteReminderRules,
            _desktopWorkspace.Clock,
            initialErrorTarget: initialErrorTarget,
            initialError: initialError)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _noteReminderRules = Array.AsReadOnly(editor.ResultRules.ToArray());
        UpdateNoteReminderRulesSummary();
        NoteMessage.Text = string.Empty;
    }

    private void OpenCountdownReminderRulesEditor(
        ReminderRuleEditingErrorTarget initialErrorTarget = ReminderRuleEditingErrorTarget.None,
        string initialError = "")
    {
        var targetUtc = GetCountdownTargetUtc();
        if (targetUtc is null)
        {
            SetMessage(CountdownMessage, "请先选择完整的倒计时截止时间。", true);
            return;
        }

        var editor = new ReminderRulesEditorWindow(
            _countdownReminderRules,
            _desktopWorkspace.Clock,
            initialErrorTarget: initialErrorTarget,
            initialError: initialError,
            countdownTargetUtc: targetUtc)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _countdownReminderRules = Array.AsReadOnly(editor.ResultRules.ToArray());
        UpdateCountdownReminderRulesSummary();
        CountdownMessage.Text = string.Empty;
    }

    private DateTimeOffset? GetCountdownTargetUtc()
    {
        if (CountdownDate.SelectedDate is null ||
            CountdownHour.SelectedIndex is < 0 or > 23 ||
            CountdownMinute.SelectedIndex is < 0 or > 59 ||
            CountdownSecond.SelectedIndex is < 0 or > 59)
        {
            return null;
        }

        return CountdownFormatting.ToUtc(
            CountdownDate.SelectedDate.Value,
            CountdownHour.SelectedIndex,
            CountdownMinute.SelectedIndex,
            CountdownSecond.SelectedIndex);
    }

    private void UpdateCountdownReminderRulesSummary()
    {
        CountdownReminderRulesSummary.Text = _countdownReminderRules.Count == 0
            ? "不提醒"
            : $"已配置 {_countdownReminderRules.Count} 条";
        AutomationProperties.SetName(
            CountdownReminderRulesSummary,
            $"倒计时提醒规则摘要：{CountdownReminderRulesSummary.Text}");
    }

    private void UpdateNoteReminderRulesSummary()
    {
        NoteReminderRulesSummary.Text = _noteReminderRules.Count == 0
            ? "不提醒"
            : $"已配置 {_noteReminderRules.Count} 条提醒规则";
        AutomationProperties.SetName(
            NoteReminderRulesSummary,
            $"便签提醒规则摘要：{NoteReminderRulesSummary.Text}");
    }

    private void RenderFilterTags()
    {
        FilterTagOptions.Children.Clear();
        var snapshot = _desktopWorkspace.Snapshot;
        foreach (var tag in snapshot.Settings.Tags)
        {
            var option = new ToggleButton
            {
                Content = tag.Name,
                IsChecked = snapshot.Filter.SelectedTagIds.Contains(tag.Id),
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
                    UpdateFilter(filter => filter with
                    {
                        SelectedTagIds = filter.SelectedTagIds
                            .Append(tag.Id)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                    });
                }
                else
                {
                    UpdateFilter(filter => filter with
                    {
                        SelectedTagIds = filter.SelectedTagIds
                            .Where(id => !string.Equals(id, tag.Id, StringComparison.OrdinalIgnoreCase))
                            .ToArray()
                    });
                }

                RenderVisibleWorkspace();
            };
            FilterTagOptions.Children.Add(option);
        }
    }

    private void RenderTagManager()
    {
        TagManagerRows.Children.Clear();
        foreach (var tag in _desktopWorkspace.Snapshot.Settings.Tags)
        {
            var name = UiAccessibility.Name(
                new TextBox { Text = tag.Name, MaxLength = 30 },
                $"标签名称 {tag.Name}");
            var save = ActionButton("保存", tag.Id, $"保存标签 {tag.Name}");
            var delete = ActionButton("删除", tag.Id, $"删除标签 {tag.Name}");
            delete.Style = (Style)FindResource("DangerButton");
            save.Click += (_, _) =>
            {
                var result = _desktopWorkspace.Apply(new RenameTagChange(
                    tag.Id,
                    name.Text,
                    tag.Color));
                if (!result.Succeeded)
                {
                    SetMessage(TagManagerMessage, result.Error, true);
                }
            };
            delete.Click += (_, _) =>
            {
                if (new ConfirmationDialog(this, "删除标签", "标签会从记录中解除，但记录本身不会删除。").ShowDialog() != true)
                {
                    return;
                }

                var result = _desktopWorkspace.Apply(new DeleteTagChange(tag.Id));
                if (!result.Succeeded)
                {
                    SetMessage(TagManagerMessage, result.Error, true);
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

        var color = TagColors[_desktopWorkspace.Snapshot.Settings.Tags.Count % TagColors.Length];
        var result = _desktopWorkspace.Apply(new CreateTagChange(normalized, color));
        if (!result.Succeeded)
        {
            SetMessage(TagManagerMessage, result.Error, true);
            return null;
        }

        NewTagInput.Clear();
        SetMessage(TagManagerMessage, "标签已创建。", false);
        return result.CreatedTag;
    }

    private void UpdateFilter(Func<DesktopWorkspaceFilter, DesktopWorkspaceFilter> update)
    {
        _desktopWorkspace.ApplyFilter(update(_desktopWorkspace.Snapshot.Filter));
    }

    private void SetCountdownFilter(CountdownStatusFilter filter)
    {
        UpdateFilter(current => current with { CountdownStatus = filter });
        CountdownAllFilter.IsChecked = filter == CountdownStatusFilter.All;
        CountdownActiveFilter.IsChecked = filter == CountdownStatusFilter.Active;
        CountdownExpiredFilter.IsChecked = filter == CountdownStatusFilter.Expired;
        RenderCountdowns();
        UpdateFilterSummary();
    }

    private void SetNoteFilter(NoteStatusFilter filter)
    {
        UpdateFilter(current => current with { NoteStatus = filter });
        NoteAllFilter.IsChecked = filter == NoteStatusFilter.All;
        NoteActiveFilter.IsChecked = filter == NoteStatusFilter.Active;
        NoteCompletedFilter.IsChecked = filter == NoteStatusFilter.Completed;
        RenderNotes();
        UpdateFilterSummary();
    }

    private void ClearFilters()
    {
        _desktopWorkspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
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
        var count = _desktopWorkspace.Snapshot.AppliedFilterCount;
        FilterButton.Content = count == 0 ? "筛选" : $"筛选 · {count}";
        AutomationProperties.SetName(FilterButton, count == 0 ? "打开筛选" : $"打开筛选，已应用 {count} 项");
        SetMessage(StatusText, $"筛选结果：{RecordCountText.Text}", false);
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
        foreach (var tag in _desktopWorkspace.Snapshot.Settings.Tags.Where(tag => tagIds.Contains(
                     tag.Id,
                     StringComparer.OrdinalIgnoreCase)))
        {
            var chipText = new TextBlock
            {
                Text = tag.Name,
                FontSize = 16
            };
            chipText.SetResourceReference(TextBlock.ForegroundProperty, "TagChipTextBrush");
            var chip = new Border
            {
                Uid = tag.Id,
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(7, 3, 7, 3),
                CornerRadius = new CornerRadius(10),
                Child = chipText
            };
            var savedBackground = ParseBrush(tag.Color);
            if (savedBackground is null)
            {
                chip.SetResourceReference(Border.BackgroundProperty, "TagBrush");
            }
            else
            {
                chip.Background = savedBackground;
            }
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
        var card = new Border
        {
            Child = content,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(21),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BackgroundProperty, backgroundResource);
        card.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        return card;
    }

    private Border NoteCard(UIElement content)
    {
        var card = new Border
        {
            Child = content,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BackgroundProperty, "NoteBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "NoteLineBrush");
        return card;
    }

    private Button NoteActionButton(string content, string id, string accessibleName)
    {
        return UiAccessibility.Name(
            new Button
            {
                Content = content,
                Tag = id,
                MinWidth = 72,
                MinHeight = 44,
                FontSize = 16,
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("SecondaryButton")
            },
            accessibleName);
    }

    private Button ActionButton(string content, string id, string accessibleName)
    {
        return UiAccessibility.Name(
            new Button
            {
                Content = content,
                Tag = id,
                MinWidth = 88,
                MinHeight = 52,
                FontSize = 18,
                Margin = new Thickness(9, 0, 0, 0),
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
        yield return ThemeModeButton;
        yield return MinimizeButton;
        yield return CloseButton;
        yield return NoteText;
        yield return NoteDate;
        yield return SaveNoteButton;
        yield return NoteSearch;
        yield return FilterButton;
        yield return TagManagerButton;
    }

    private static void NameControl(FrameworkElement control, string name) =>
        UiAccessibility.Name(control, name);

    private static void UseShortDateText(DatePicker datePicker)
    {
        datePicker.ApplyTemplate();
        if (datePicker.Template.FindName("PART_TextBox", datePicker) is not DatePickerTextBox textBox)
        {
            throw new InvalidOperationException("便签日期模板缺少 PART_TextBox。");
        }

        BindingOperations.SetBinding(
            textBox,
            TextBox.TextProperty,
            new Binding(nameof(DatePicker.SelectedDate))
            {
                Source = datePicker,
                StringFormat = "{0:yyyy/M/d}"
            });
        ThemedDatePickerBehavior.LockReadOnlyTextBox(datePicker);
    }

    private void SetMessage(TextBlock target, string text, bool isError)
    {
        target.Text = text;
        AutomationProperties.SetName(target, text);
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
        CountdownReferenceDecoration.Source = LoadImage(Path.Combine(
            characterRoot,
            "main-decorations",
            "main-countdown-scene-day.png"));
        NotesReferenceDecoration.Source = LoadImage(Path.Combine(
            characterRoot,
            "main-decorations",
            "main-notes-scene-day.png"));
        NightCountdownTopDecoration.Source = LoadImage(Path.Combine(
            characterRoot,
            "main-decorations",
            "main-countdown-top-night.png"));
        NightNotesTopDecoration.Source = LoadImage(Path.Combine(
            characterRoot,
            "main-decorations",
            "main-notes-top-night.png"));
    }

    private void UpdateReferenceDecorationVisibility()
    {
        var useDayReferences = _themeMode == ApplicationThemeMode.Day &&
            MainReferenceDecorationsLoaded;
        var useNightReferences = _themeMode == ApplicationThemeMode.Night &&
            MainNightReferenceDecorationsLoaded;
        var useReferenceDecorations = useDayReferences || useNightReferences;
        CharacterBackgroundLayer.Visibility = useReferenceDecorations
            ? Visibility.Collapsed
            : Visibility.Visible;
        CharacterPanelLayer.Visibility = useReferenceDecorations
            ? Visibility.Collapsed
            : Visibility.Visible;
        CountdownReferenceDecoration.Visibility = useDayReferences &&
            _workspace == WorkspaceKind.Countdowns
            ? Visibility.Visible
            : Visibility.Collapsed;
        NotesReferenceDecoration.Visibility = useDayReferences &&
            _workspace == WorkspaceKind.Notes
            ? Visibility.Visible
            : Visibility.Collapsed;
        NightCountdownTopDecoration.Visibility = useNightReferences &&
            _workspace == WorkspaceKind.Countdowns
            ? Visibility.Visible
            : Visibility.Collapsed;
        NightNotesTopDecoration.Visibility = useNightReferences &&
            _workspace == WorkspaceKind.Notes
            ? Visibility.Visible
            : Visibility.Collapsed;
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

internal static class MainShortcutInput
{
    private static readonly ConditionalWeakTable<KeyEventArgs, ModifierKeysBox> InjectedModifiers = new();

    public static ModifierKeys ResolveModifiers(KeyEventArgs args) =>
        InjectedModifiers.TryGetValue(args, out var value)
            ? value.Modifiers
            : Keyboard.Modifiers;

    public static IDisposable InjectModifiers(KeyEventArgs args, ModifierKeys modifiers)
    {
        InjectedModifiers.Add(args, new ModifierKeysBox(modifiers));
        return new ModifierInjection(args);
    }

    private sealed record ModifierKeysBox(ModifierKeys Modifiers);

    private sealed class ModifierInjection(KeyEventArgs args) : IDisposable
    {
        public void Dispose() => InjectedModifiers.Remove(args);
    }
}

internal sealed record MainWindowActivationState(
    WorkspaceKind Workspace,
    DesktopWorkspaceFilter Filter,
    string CountdownDraft,
    string NoteDraft,
    string NoteSearch,
    DateTime? NoteDateFilter,
    string? EditingCountdownId,
    string? EditingNoteId);

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
