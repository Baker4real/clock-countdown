using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    // #197：分格单位字收敛到字号阶梯的 caption 档，不随列宽缩小。
    private const double CountdownRemainingUnitFontSize = 13;
    internal const string NoteCardReminderSummaryAutomationId = "MainNoteCardReminderSummary";
    // #153/#197：便签正文 20px 按悬浮窗 §10「13/18」同一比例（18/13≈1.385）取整为 28 的显式行高，
    // 多行正文与「下次提醒」摘要分开，不再挤成一块（§3 便签正文行距合同）。
    private const double NoteCardBodyLineHeight = 28;
    // #153：标签芯片单行不折字，集合由 WrapPanel 先折行；超长芯片按此上限省略并以 ToolTip 提供全文。
    internal const double TagChipMaxWidth = 200;

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
    private string? _editingCountdownId;
    private string? _editingNoteId;
    private bool _completedSegmentSelected;
    private bool _settingDefaultDate;
    private bool _noteDateDirty;
    private bool _settingsDraftDirty;
    private bool _loadingPreferences;
    private bool _syncingNoteSearch;
    private bool _loadingCountdownEditor;
    private CountdownReminderMode _countdownReminderMode = CountdownReminderMode.None;
    private IReadOnlyList<ReminderRule> _countdownReminderRules = Array.Empty<ReminderRule>();
    private IReadOnlyList<ReminderRule> _noteReminderRules = Array.Empty<ReminderRule>();
    private long _lastRenderedNoteMinuteUtc = -1;
    private string? _reminderSourceCountdownId;
    private string? _reminderSourceNoteId;
    private string? _countdownRenderSignature;
    // #155：最近一次删除的撤销载荷；只保留最近一次，不引入通用撤销栈。
    private DeletedRecordUndo? _deleteUndo;
    private DispatcherTimer? _deleteUndoTimer;

    // #155：撤销入口的短时窗口。过期后自动隐藏，删除本身不受影响。
    private static readonly TimeSpan DeleteUndoOfferDuration = TimeSpan.FromSeconds(10);

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
        PopupFocusLifecycle.ClipToCanvas(FilterPopup, MainLayoutSurface);
        PopupFocusLifecycle.ClipToCanvas(TagManagerPopup, MainLayoutSurface);
        PopupFocusLifecycle.ClipToCanvas(NoteTags.Popup, MainLayoutSurface);
        PopupFocusLifecycle.ClipToCanvas(CountdownTags.Popup, MainLayoutSurface);
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

    internal WorkspaceKind CurrentWorkspace => _workspace;

    internal string? EditingCountdownId => _editingCountdownId;

    internal IReadOnlyList<int> TimeOptionCounts =>
        new[] { CountdownHour.Items.Count, CountdownMinute.Items.Count, CountdownSecond.Items.Count };

    internal bool CompletedNotesAreCollapsed => !_completedSegmentSelected;

    internal bool HasLiveStatusRegions =>
        AutomationProperties.GetLiveSetting(StatusText) == AutomationLiveSetting.Polite &&
        AutomationProperties.GetLiveSetting(CountdownMessage) == AutomationLiveSetting.Polite &&
        AutomationProperties.GetLiveSetting(NoteMessage) == AutomationLiveSetting.Polite;

    internal bool HasAccessibleCoreControls =>
        FindCoreControls().All(control =>
            !string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) ||
            // #156：可访问名允许经由标签关系（LabeledBy）从可见标签取得——此时输入不再
            // 携带重复显式名，避免同一字段被读两遍。
            AutomationProperties.GetLabeledBy(control) is not null);

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
                // #156：便签页有正文搜索，Ctrl+F 聚焦便签搜索（与悬浮窗记忆一致）；
                // 倒计时页没有正文搜索，Ctrl+F 仍打开筛选。
                if (_workspace == WorkspaceKind.Notes)
                {
                    NoteSearch.Focus();
                }
                else
                {
                    FilterPopup.IsOpen = true;
                }

                return true;
            case (ModifierKeys.Control, Key.T):
                RenderTagManager();
                TagManagerPopup.IsOpen = true;
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

    internal void ApplyTheme()
    {
        CharacterBackgroundLayer.Opacity = 0.075;
        UpdateReferenceDecorationVisibility();
    }

    internal bool CharacterBackgroundRemainsSubtle =>
        CharacterBackgroundLayer.Visibility == Visibility.Collapsed
            ? MainReferenceDecorationsLoaded
            : CharacterBackgroundLayer.Opacity is > 0 and <= 0.16;

    internal double CharacterBackgroundOpacity => CharacterBackgroundLayer.Opacity;

    internal bool MainReferenceDecorationsLoaded =>
        CountdownReferenceDecoration.Source is not null &&
        NotesReferenceDecoration.Source is not null;

    internal bool MainReferenceDecorationsAreNonInteractive =>
        !MainReferenceDecorationLayer.IsHitTestVisible &&
        !CountdownReferenceDecoration.IsHitTestVisible &&
        !NotesReferenceDecoration.IsHitTestVisible;

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
                CountdownReferenceDecoration.Visibility == countdownVisible &&
                NotesReferenceDecoration.Visibility == notesVisible;
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

        // #156：有可见文字的字段改经标签关系（LabeledBy）指向输入——输入不再携带与可见
        // 标签重复的显式自动化名，同一字段只被读一遍；原更丰富的语境（如「悬浮窗显示精度」
        // 「按便签日期筛选」）降级为该输入的帮助文本。映射为自动化名/帮助文本/标签关系，
        // 不把 Web aria 属性原样搬到 WPF。
        WireFieldLabelRelations();
        NameControl(CountdownReminderHelp, "提醒规则说明");
        NameControl(CountdownReminderNoRadio, "提醒规则：不提醒");
        NameControl(CountdownReminderTimeRadio, "提醒规则：按时间提醒");
        NameControl(CountdownReminderCustomRadio, "提醒规则：自定义提醒");
        NameControl(SaveCountdownButton, "保存倒计时");
        NameControl(SavePreferencesButton, "保存偏好");
        NameControl(MinimizeButton, "最小化窗口");
        NameControl(CloseButton, "关闭窗口");
        NameControl(ManageNoteReminderRulesButton, "管理便签提醒规则");
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
        // #152：主按钮（SaveCountdownButton/SaveNoteButton）与置顶开关不再套 FocusVisualStyle——
        // 它们的模板内常驻 2px 暖粉焦点环是全应用唯一焦点例外，外圈 3px（现 2px）描边属于被
        // 禁止的第二套焦点描边；其余键盘路径控件继续用 2px 暖边 FocusVisualStyle。
        var keyboardPathControls = new FrameworkElement[]
        {
            MinimizeButton, CloseButton,
            CountdownTab, NotesTab, FilterButton, TagManagerButton,
            CountdownName, CountdownDate, CountdownHour, CountdownMinute, CountdownSecond,
            CountdownTags.PopupTrigger, CountdownReminderHelp,
            CountdownReminderNoRadio, CountdownReminderTimeRadio, CountdownReminderCustomRadio,
            CancelCountdownEdit, DisplayPrecision, MonthlySalary,
            WorkStart, WorkEnd, SavePreferencesButton, CountdownListRegion,
            NoteText, NoteDate, CancelNoteEdit,
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

        ConfigureReminderHelpFocusHelpText();

        SetAutomationIds(
            (TopmostCheck, "TopmostCheck"),
            (MinimizeButton, "MinimizeButton"),
            (CloseButton, "CloseButton"),
            (CountdownName, "CountdownName"),
            (CountdownDate, "CountdownDate"),
            (CountdownHour, "CountdownHour"),
            (CountdownMinute, "CountdownMinute"),
            (CountdownSecond, "CountdownSecond"),
            (CountdownTags.PopupTrigger, "CountdownTags"),
            (CountdownReminderHelp, "CountdownReminderHelp"),
            (CountdownReminderNoRadio, "CountdownReminderNoRadio"),
            (CountdownReminderTimeRadio, "CountdownReminderTimeRadio"),
            (CountdownReminderCustomRadio, "CountdownReminderCustomRadio"),
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
        NameControl(CreateTagButton, "新建标签");
        // #156：筛选「清除」的可访问名说明清除的是便签日期筛选，不是只读一个「清除」。
        NameControl(ClearNoteDateFilter, "清除便签日期筛选");
        ThemedDatePickerBehavior.Attach(CountdownDate);
        ThemedDatePickerBehavior.Attach(NoteDate);
        ThemedDatePickerBehavior.Attach(NoteDateFilter);
        ThemedComboBoxBehavior.Attach(CountdownHour);
        ThemedComboBoxBehavior.Attach(CountdownMinute);
        ThemedComboBoxBehavior.Attach(CountdownSecond);
        ThemedComboBoxBehavior.Attach(DisplayPrecision);
        ThemedComboBoxBehavior.Attach(NoteGroupingSelector);
        UseShortDateText(NoteDate);
        UiAccessibility.MarkLive(StatusText);
        // #156：数量变化由一处状态区域说出完整句子——命令栏数量文字（N 个待办 / N 个倒计时）
        // 是唯一的数量直播区；分段徽章与分段名只承载视觉与可访问名，不再各自播报。
        UiAccessibility.MarkLive(RecordCountText);
        UiAccessibility.MarkLive(CountdownMessage);
        // #151：字段级错误行同样以礼貌直播区播报，镜像悬浮窗快速新增行为。
        UiAccessibility.MarkLive(CountdownNameError);
        UiAccessibility.MarkLive(CountdownDateError);
        UiAccessibility.MarkLive(NoteTextError);
        UiAccessibility.MarkLive(NoteDateError);
        UiAccessibility.MarkLive(MonthlySalaryError);
        UiAccessibility.MarkLive(WorkStartError);
        UiAccessibility.MarkLive(WorkEndError);
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

    internal const string ReminderRuleHelpText =
        "按时间提醒会在倒计时截止时刻提醒一次；自定义提醒可配置多条提醒规则。";

    // #152：提醒规则帮助在键盘聚焦时显示与悬停相同的说明（ToolTip 即时打开/关闭），
    // 并通过 AutomationProperties.HelpText 提供同一份帮助文本。
    private void ConfigureReminderHelpFocusHelpText()
    {
        var helpText = CountdownReminderHelp.ToolTip as string ?? ReminderRuleHelpText;
        var tooltip = new ToolTip
        {
            Content = helpText,
            PlacementTarget = CountdownReminderHelp,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };
        CountdownReminderHelp.ToolTip = tooltip;
        AutomationProperties.SetHelpText(CountdownReminderHelp, helpText);
        CountdownReminderHelp.GotKeyboardFocus += (_, _) => tooltip.IsOpen = true;
        CountdownReminderHelp.LostKeyboardFocus += (_, _) => tooltip.IsOpen = false;
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
        CountdownReminderNoRadio.Click += (_, _) => ApplyCountdownReminderMode(CountdownReminderMode.None);
        CountdownReminderTimeRadio.Click += (_, _) => ApplyCountdownReminderMode(CountdownReminderMode.AtDeadline);
        CountdownReminderCustomRadio.Click += (_, _) => ApplyCountdownReminderMode(CountdownReminderMode.Custom);
        CountdownDate.SelectedDateChanged += (_, _) => OnCountdownTargetFieldsChanged();
        CountdownHour.SelectionChanged += (_, _) => OnCountdownTargetFieldsChanged();
        CountdownMinute.SelectionChanged += (_, _) => OnCountdownTargetFieldsChanged();
        CountdownSecond.SelectionChanged += (_, _) => OnCountdownTargetFieldsChanged();
        SaveCountdownButton.Click += (_, _) => SaveCountdown();
        CancelCountdownEdit.Click += (_, _) => ResetCountdownEditor();
        SavePreferencesButton.Click += (_, _) => SavePreferences();
        // #151：字段值修正后立即清除对应字段错误（镜像悬浮窗快速新增）。
        CountdownName.TextChanged += (_, _) => ClearCountdownFieldError(CountdownNameError);
        CountdownDate.SelectedDateChanged += (_, _) => ClearCountdownFieldError(CountdownDateError);
        CountdownHour.SelectionChanged += (_, _) => ClearCountdownFieldError(CountdownDateError);
        CountdownMinute.SelectionChanged += (_, _) => ClearCountdownFieldError(CountdownDateError);
        CountdownSecond.SelectionChanged += (_, _) => ClearCountdownFieldError(CountdownDateError);
        DisplayPrecision.SelectionChanged += (_, _) => MarkSettingsDraftDirty();
        MonthlySalary.TextChanged += (_, _) =>
        {
            MarkSettingsDraftDirty();
            ClearPreferenceFieldError(MonthlySalaryError);
        };
        WorkStart.TextChanged += (_, _) =>
        {
            MarkSettingsDraftDirty();
            ClearPreferenceFieldError(WorkStartError);
        };
        WorkEnd.TextChanged += (_, _) =>
        {
            MarkSettingsDraftDirty();
            ClearPreferenceFieldError(WorkEndError);
        };
        SaveNoteButton.Click += (_, _) => SaveNote();
        CancelNoteEdit.Click += (_, _) => ResetNoteEditor();
        ManageNoteReminderRulesButton.Click += (_, _) => OpenNoteReminderRulesEditor();
        NoteText.TextChanged += (_, _) => ClearNoteFieldError(NoteTextError);
        MainStatusUndoButton.Click += OnUndoDeleteClick;
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

            ClearNoteFieldError(NoteDateError);
        };
        ActiveNotesSegment.Click += (_, _) => SelectNotesSegment(showCompleted: false);
        CompletedNotesSegment.Click += (_, _) => SelectNotesSegment(showCompleted: true);
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
        SetRecordCountText(RecordCountText, $"{records.Count} 个倒计时");
        var signature = string.Join(
            "|",
            records.Select(record =>
                $"{record.Id}:{record.Name}:{record.TargetUtc.UtcTicks}:{string.Join(',', record.TagIds)}:{record.TargetUtc > effectiveNowUtc}:{snapshot.Settings.Preferences.DisplayPrecision}"));
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

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            var record = records[recordIndex];
            var active = record.TargetUtc > effectiveNowUtc;
            var isPrimary = recordIndex == 0;
            var edit = ActionButton("编辑", record.Id, $"编辑倒计时 {record.Name}");
            edit.Click += OnEditCountdown;
            var delete = ActionButton("删除", record.Id, $"删除倒计时 {record.Name}");
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
                Uid = "CountdownRowTitleText",
                Text = record.Name,
                // #153/#197：列表条目标题落 H2 阶梯，去掉与区块标题混用的旧字号台阶。
                FontSize = (double)FindResource("TypeH2FontSize"),
                FontWeight = FontWeights.SemiBold
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            // #148：「超期待！」徽章只挂距离下班主项；管理窗口不渲染距离下班条目，
            // 普通倒计时即便承担视觉主项也不得携带该徽章，此处不再构造。
            var title = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            title.Children.Add(name);
            var remaining = new TextBlock
            {
                Margin = new Thickness(CountdownRemainingLeftMargin, 5, 0, 0),
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                FontSize = CountdownRemainingBaseFontSize,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = CountdownRemainingColumnWidth - CountdownRemainingLeftMargin,
                Opacity = 0,
                IsHitTestVisible = false
            };
            remaining.SetResourceReference(TextBlock.FontFamilyProperty, "NumeralFontFamily");
            var remainingValue = CountdownFormatting.FormatRemaining(
                record.TargetUtc,
                effectiveNowUtc,
                snapshot.Settings.Preferences.DisplayPrecision);
            var blocksHost = new MainCountdownBlocksHost
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
            remaining.Tag = blocksHost;
            SetCountdownRemainingText(remaining, remainingValue);
            remaining.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            // #148：到期剩余文字不得用删除红/强调轨色当字。
            // 主项到期回落主项数字色，次项到期回落正文色；「已到期」文案继续显示。
            remaining.SetResourceReference(
                TextBlock.ForegroundProperty,
                active ? "BodyBrush" : isPrimary ? "PrimaryCountdownNumberBrush" : "BodyBrush");
            var tagChips = CreateTagChips(record.TagIds);
            Grid.SetColumnSpan(title, 2);
            Grid.SetRow(tagChips, 1);
            Grid.SetColumn(remaining, 1);
            Grid.SetRow(remaining, 1);
            Grid.SetColumn(blocksHost.Surface, 1);
            Grid.SetRow(blocksHost.Surface, 1);
            Grid.SetColumn(actions, 2);
            Grid.SetRowSpan(actions, 2);
            layout.Children.Add(title);
            layout.Children.Add(tagChips);
            layout.Children.Add(remaining);
            layout.Children.Add(blocksHost.Surface);
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

    private void SetCountdownRemainingText(TextBlock textBlock, string text)
    {
        textBlock.Text = text;
        if (textBlock.Tag is MainCountdownBlocksHost blocksHost)
        {
            RenderMainCountdownBlocks(blocksHost, text);
        }
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

    private void RenderMainCountdownBlocks(MainCountdownBlocksHost host, string remaining)
    {
        host.Surface.Children.Clear();
        var pairs = ParseMainRemainingPairs(remaining);
        if (pairs is null || pairs.Count == 0)
        {
            var fallback = new TextBlock
            {
                Text = remaining,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = CountdownRemainingBaseFontSize,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None
            };
            fallback.SetResourceReference(TextBlock.FontFamilyProperty, "NumeralFontFamily");
            fallback.SetResourceReference(
                TextBlock.ForegroundProperty,
                host.IsPrimary ? "PrimaryCountdownNumberBrush" : "BodyBrush");
            Typography.SetNumeralAlignment(fallback, FontNumeralAlignment.Tabular);
            host.Surface.Children.Add(fallback);
            return;
        }

        // #148：主窗口分格与悬浮窗同一套竖长口径（约 2:3、单位在块外、间距约块宽三分之一），
        // 主窗走设计坐标：主项 42x63、次项 38x57，圆角约短边 1/4；数值优先压内边距与间距，
        // 最后才在辅助下限档之上收缩数值字号；单位字固定在下限档、不得缩小硬塞。
        const double valueFontPrimary = 19;
        const double valueFontSecondary = 17;
        const double blockWidthPrimary = 42;
        const double blockWidthSecondary = 38;
        const double blockHeightPrimary = 63;
        const double blockHeightSecondary = 57;
        const double minimumValueFontSize = 13;
        const double blockWidthReserve = 3;
        const double unitGap = 3;
        var valueFontSize = host.IsPrimary ? valueFontPrimary : valueFontSecondary;
        var blockPadding = 4;
        var pairGap = host.IsPrimary ? 14 : 13;

        var valueBlocks = pairs
            .Select(pair => new TextBlock
            {
                Text = pair.Value,
                MinWidth = host.IsPrimary ? 38 : 34,
                Padding = new Thickness(blockPadding, 0, blockPadding, 0),
                FontSize = valueFontSize,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None
            })
            .ToArray();
        foreach (var valueBlock in valueBlocks)
        {
            valueBlock.SetResourceReference(TextBlock.FontFamilyProperty, "NumeralFontFamily");
            Typography.SetNumeralAlignment(valueBlock, FontNumeralAlignment.Tabular);
        }

        var unitFace = new Typeface(
            (FontFamily)FindResource("UiFontFamily"),
            new FontStyle(),
            FontWeights.Regular,
            FontStretches.Normal);
        double ValueBlockWidth(int index, double font, double padding)
        {
            valueBlocks[index].FontSize = font;
            valueBlocks[index].Padding = new Thickness(padding, 0, padding, 0);
            valueBlocks[index].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return valueBlocks[index].DesiredSize.Width;
        }

        // 分格几何按设计坐标计量（主窗整体在 Viewbox 内缩放），文本测量固定 1.0 pixelsPerDip。
        double RequiredWidth(double font, double padding, double gap)
        {
            var total = 0d;
            for (var index = 0; index < pairs.Count; index++)
            {
                var valueWidth = ValueBlockWidth(index, font, padding);
                var unitWidth = MeasureMainText(pairs[index].Unit, unitFace, CountdownRemainingUnitFontSize);
                var blockWidth = Math.Max(
                    host.IsPrimary ? blockWidthPrimary : blockWidthSecondary,
                    Math.Ceiling(valueWidth) + blockWidthReserve);
                total += blockWidth + unitWidth + unitGap;
                if (index < pairs.Count - 1)
                {
                    total += gap;
                }
            }

            return total;
        }

        var availableWidth = CountdownRemainingColumnWidth - CountdownRemainingLeftMargin;
        if (RequiredWidth(valueFontSize, blockPadding, pairGap) > availableWidth)
        {
            blockPadding = 2;
        }

        if (RequiredWidth(valueFontSize, blockPadding, pairGap) > availableWidth)
        {
            pairGap = 6;
        }

        var required = RequiredWidth(valueFontSize, blockPadding, pairGap);
        while (required > availableWidth && valueFontSize > minimumValueFontSize)
        {
            var fittedFontSize = valueFontSize * (availableWidth / required);
            var nextFontSize = Math.Max(minimumValueFontSize, Math.Floor(fittedFontSize * 4) / 4);
            if (nextFontSize >= valueFontSize)
            {
                nextFontSize = Math.Max(minimumValueFontSize, valueFontSize - 0.25);
            }

            valueFontSize = nextFontSize;
            required = RequiredWidth(valueFontSize, blockPadding, pairGap);
        }

        var blockHeight = host.IsPrimary ? blockHeightPrimary : blockHeightSecondary;
        var blockBaseWidth = host.IsPrimary ? blockWidthPrimary : blockWidthSecondary;
        var blockRadius = Math.Max(4, blockBaseWidth / 4);
        var values = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        for (var index = 0; index < pairs.Count; index++)
        {
            var value = valueBlocks[index];
            value.FontSize = valueFontSize;
            value.Padding = new Thickness(blockPadding, 0, blockPadding, 0);
            value.SetResourceReference(
                TextBlock.ForegroundProperty,
                host.IsPrimary ? "PrimaryCountdownNumberBrush" : "InkBrush");
            value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var block = new Border
            {
                MinWidth = Math.Max(
                    blockBaseWidth,
                    Math.Ceiling(value.DesiredSize.Width) + blockWidthReserve),
                Height = blockHeight,
                CornerRadius = new CornerRadius(blockRadius)
            };
            block.SetResourceReference(
                Border.BackgroundProperty,
                host.IsPrimary ? "PinkBlockBrush" : "SecondaryBlockBrush");
            block.Child = value;
            var unit = new TextBlock
            {
                Text = pairs[index].Unit,
                Margin = new Thickness(unitGap, 0, 0, 0),
                FontSize = CountdownRemainingUnitFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None
            };
            unit.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var pairPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = index == 0 ? new Thickness(0) : new Thickness(pairGap, 0, 0, 0)
            };
            pairPanel.Children.Add(block);
            pairPanel.Children.Add(unit);
            values.Children.Add(pairPanel);
        }

        host.Surface.Children.Add(values);
    }

    private static double MeasureMainText(string text, Typeface typeface, double fontSize)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1);
        return formatted.WidthIncludingTrailingWhitespace + Math.Max(0, formatted.OverhangTrailing);
    }

    private static IReadOnlyList<RemainingPair>? ParseMainRemainingPairs(string remaining)
    {
        if (string.Equals(remaining, "已到期", StringComparison.Ordinal))
        {
            return null;
        }

        var tokens = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length % 2 != 0)
        {
            return null;
        }

        var pairs = new List<RemainingPair>(tokens.Length / 2);
        for (var index = 0; index < tokens.Length; index += 2)
        {
            if (!long.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return null;
            }

            var unit = tokens[index + 1] switch
            {
                "天" => "天",
                "小时" => "时",
                "分钟" => "分",
                "秒" => "秒",
                _ => null
            };
            if (unit is null)
            {
                return null;
            }

            pairs.Add(new RemainingPair(tokens[index], unit));
        }

        return pairs;
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
        var revealCompleted = revealNoteId is not null &&
            completed.Any(note =>
                string.Equals(note.Id, revealNoteId, StringComparison.OrdinalIgnoreCase));
        SetRecordCountText(RecordCountText, $"{active.Length} 个待办");
        AddNotesGroupedByDay(ActiveNoteRows, active, isCompleted: false, effectiveNowUtc, revealNoteId);
        AddNotesGroupedByDay(CompletedNoteRows, completed, isCompleted: true, effectiveNowUtc, revealNoteId);
        ActiveNotesCount.Text = active.Length.ToString(CultureInfo.InvariantCulture);
        CompletedNotesCount.Text = completed.Length.ToString(CultureInfo.InvariantCulture);
        var showCompleted = revealNoteId is not null ? revealCompleted : _completedSegmentSelected;
        ApplyNotesSegmentState(showCompleted, active.Length, completed.Length);

        var noVisibleNotes = records.Count == 0;
        var segmentHasNotes = showCompleted ? completed.Length > 0 : active.Length > 0;
        var emptyState = NoteEmptyStatePresentation(snapshot.Settings.Notes.Count > 0);
        EmptyNotesMessage.Text = emptyState.Message;
        AutomationProperties.SetLiveSetting(EmptyNotesMessage, AutomationLiveSetting.Polite);
        EmptyNotesMessage.Visibility = noVisibleNotes || !segmentHasNotes
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetName(EmptyNotesMessage, EmptyNotesMessage.Text);
        ResetEmptyNotesFiltersButton.Visibility = (noVisibleNotes || !segmentHasNotes) && emptyState.ShowReset
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static (string Message, bool ShowReset) NoteEmptyStatePresentation(bool hasSavedNotes) =>
        hasSavedNotes
            ? ("没有符合当前筛选条件的便签。", true)
            : ("还没有便签，先写下一件想记住的事。", false);

    private void SelectNotesSegment(bool showCompleted)
    {
        _completedSegmentSelected = showCompleted;
        RenderNotes();
    }

    private void ApplyNotesSegmentState(bool showCompleted, int activeCount, int completedCount)
    {
        ActiveNotesSegment.IsChecked = !showCompleted;
        CompletedNotesSegment.IsChecked = showCompleted;
        ActiveNoteRows.Visibility = !showCompleted && activeCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompletedNoteRows.Visibility = showCompleted && completedCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetNoteStatusSegmentState(ActiveNotesSegment, "未完成", activeCount);
        SetNoteStatusSegmentState(CompletedNotesSegment, "已完成", completedCount);
    }

    private static void SetNoteStatusSegmentState(
        ToggleButton segment,
        string status,
        int count)
    {
        AutomationProperties.SetName(segment, $"查看{status}便签，共 {count} 条");
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
                // #153：日期章节间距消费 §13.3 SpacingBlockGap（12，§5 日期章节 12px 节奏）。
                Margin = new Thickness(0, 0, 0, (double)FindResource("SpacingBlockGap"))
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
            FontSize = (double)FindResource("TypeH2FontSize"),
            FontWeight = FontWeights.SemiBold
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "BodyBrush");
        var count = new TextBlock
        {
            Uid = "NoteDateCount",
            Text = $"{noteCount} 条",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = (double)FindResource("TypeCaptionFontSize"),
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

        var edit = NoteActionButton("编辑", note.Id, $"编辑便签 {note.Text}", "pencil");
        edit.Click += OnEditNote;
        var delete = NoteActionButton("删除", note.Id, $"删除便签 {note.Text}", "trash");
        delete.Style = (Style)FindResource("DangerButton");
        delete.Click += OnDeleteNote;
        var more = NoteActionButton("…", note.Id, $"更多操作：编辑或删除便签 {note.Text}");
        more.Uid = "NoteRowOverflowMenu";
        more.MinWidth = 56;
        more.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
        AttachNoteRowOverflowMenu(more, note);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(edit);
        actions.Children.Add(delete);
        actions.Children.Add(more);

        var body = new StackPanel();
        var noteText = new TextBlock
        {
            Uid = "NoteRowBodyText",
            Text = note.Text,
            TextWrapping = TextWrapping.Wrap,
            // #153：正文行距足够分开正文与提醒摘要（NoteCardBodyLineHeight 注释推导）。
            LineHeight = NoteCardBodyLineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
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
                Text = details
            };
            // #197：摘要使用 H2 阶梯，Viewbox 后约 13 DIP，保持长便签提醒的可读下限。
            detailText.SetResourceReference(TextBlock.FontSizeProperty, "TypeH2FontSize");
            detailText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            AutomationProperties.SetAutomationId(detailText, NoteCardReminderSummaryAutomationId);
            UiAccessibility.Name(detailText, $"便签卡提醒摘要：{details}");
            var detailRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var clockGlyph = CreateNoteRowGlyph(
                "M 9,2.6 C 5.9,2.6 3.4,5.1 3.4,8.2 C 3.4,11.3 5.9,13.8 9,13.8 C 12.1,13.8 14.6,11.3 14.6,8.2 C 14.6,5.1 12.1,2.6 9,2.6 Z M 9,5.2 L 9,8.2 L 11.2,9.6",
                "MutedBrush");
            detailRow.Children.Add(clockGlyph);
            detailRow.Children.Add(detailText);
            body.Children.Add(detailRow);
        }

        // #136 便签卡空间关系对齐目标图：标签芯片是独立的中列并垂直居中，
        // 不再换行堆叠在正文下方。
        var tagChips = CreateTagChips(note.TagIds, hashPrefix: true);
        tagChips.VerticalAlignment = VerticalAlignment.Center;
        tagChips.Margin = new Thickness(8, 0, 8, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(body, 1);
        Grid.SetColumn(tagChips, 2);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(completion);
        grid.Children.Add(body);
        grid.Children.Add(tagChips);
        grid.Children.Add(actions);
        var card = NoteCard(grid, UseWhiteNoteCard(note));
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

    private WpfPath CreateNoteRowGlyph(string data, string brushKey)
    {
        var glyph = new WpfPath
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 1, 7, 0),
            Data = Geometry.Parse(data),
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center
        };
        glyph.SetResourceReference(WpfPath.StrokeProperty, brushKey);
        return glyph;
    }

    private void AttachNoteRowOverflowMenu(Button trigger, NoteRecord note)
    {
        var menuEdit = NoteActionButton("编辑", note.Id, $"编辑便签 {note.Text}");
        var menuDelete = NoteActionButton("删除", note.Id, $"删除便签 {note.Text}");
        menuDelete.Style = (Style)FindResource("DangerButton");
        foreach (var item in new[] { menuEdit, menuDelete })
        {
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
            item.HorizontalContentAlignment = HorizontalAlignment.Left;
            item.Margin = new Thickness(0, 2, 0, 2);
        }

        var items = new StackPanel { Margin = new Thickness(4) };
        items.Children.Add(menuEdit);
        items.Children.Add(menuDelete);
        var surface = new Border
        {
            MinWidth = 150,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = items
        };
        surface.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        var popup = new Popup
        {
            PlacementTarget = trigger,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.None,
            Child = surface
        };
        NoteRowOverflowMenuBehavior.SetPopup(trigger, popup);
        // #153：管理窗便签 `…` 菜单接入与筛选 / 标签管理 / 标签选择器 / 悬浮窗 `…` 菜单同一套
        // 弹层焦点生命周期——打开聚焦首项「编辑」、Tab 在层内循环走完「编辑 / 删除」、
        // Esc 关闭并把焦点归还触发按钮；不再保留 ad-hoc 的二次点击抑制与 Esc 处理。
        PopupFocusLifecycle.Attach(popup, trigger, () => menuEdit);
        trigger.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        menuEdit.Click += (_, _) => popup.IsOpen = false;
        menuEdit.Click += OnEditNote;
        menuDelete.Click += (_, _) => popup.IsOpen = false;
        menuDelete.Click += OnDeleteNote;
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
        // #151：校验按字段拆开；错误完整出现在对应字段旁并聚焦第一个无效控件；
        // 唯一错误行不再固定高度裁切或省略（XAML 侧已去除 MaxHeight/TextTrimming）。
        ClearCountdownFieldErrors();
        var validations = new List<(TextBlock Error, string Message, Control Source)>();
        if (string.IsNullOrWhiteSpace(CountdownName.Text))
        {
            validations.Add((CountdownNameError, "请先填写倒计时名称。", CountdownName));
        }

        if (CountdownDate.SelectedDate is null)
        {
            validations.Add((CountdownDateError, "请选择截止日期。", CountdownDate));
        }

        if (validations.Count > 0)
        {
            foreach (var (error, message, source) in validations)
            {
                ShowCountdownValidationError(error, message, source);
            }

            SetMessage(CountdownMessage, validations[0].Message, true);
            validations[0].Source.Focus();
            return;
        }

        if (GetCountdownTargetUtc() is not { } target)
        {
            ShowCountdownValidationError(
                CountdownDateError,
                "请先选择完整的倒计时截止时间。",
                CountdownDate);
            SetMessage(CountdownMessage, "请先选择完整的倒计时截止时间。", true);
            CountdownDate.Focus();
            return;
        }

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
        var existing = _editingNoteId is null
            ? null
            : _desktopWorkspace.Snapshot.Settings.Notes
                .FirstOrDefault(note => note.Id == _editingNoteId);
        ClearNoteFieldErrors();
        var validations = new List<(TextBlock Error, string Message, Control Source)>();
        if (string.IsNullOrWhiteSpace(NoteText.Text))
        {
            validations.Add((NoteTextError, "请先写下便签内容。", NoteText));
        }

        if (NoteDate.SelectedDate is null)
        {
            if (existing is null)
            {
                validations.Add((NoteDateError, "请选择便签日期，当前草稿已保留。", NoteDate));
            }
            else if (existing.Date is not null)
            {
                validations.Add((NoteDateError, "该便签已设置日期，不能清空日期保存，当前草稿已保留。", NoteDate));
            }
        }

        if (validations.Count > 0)
        {
            foreach (var (error, message, source) in validations)
            {
                ShowNoteValidationError(error, message, source);
            }

            SetMessage(NoteMessage, validations[0].Message, true);
            validations[0].Source.Focus();
            return;
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
        // #151：偏好格式错误出现在对应字段下方并同时镜像到底栏；不再只出现在窗口底栏。
        ClearPreferenceFieldErrors();
        if (!decimal.TryParse(MonthlySalary.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var salary) &&
            !decimal.TryParse(MonthlySalary.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out salary))
        {
            ShowPreferenceFieldError(MonthlySalaryError, "月薪格式无效。", MonthlySalary);
            SetMessage(StatusText, "月薪格式无效。", true);
            MonthlySalary.Focus();
            return;
        }

        if (salary < 0)
        {
            ShowPreferenceFieldError(MonthlySalaryError, "月薪不能小于 0。", MonthlySalary);
            SetMessage(StatusText, "月薪不能小于 0。", true);
            MonthlySalary.Focus();
            return;
        }

        if (!TimeSpan.TryParseExact(WorkStart.Text, @"hh\:mm", CultureInfo.InvariantCulture, out var workStart))
        {
            ShowPreferenceFieldError(WorkStartError, "上班时间请使用 HH:mm。", WorkStart);
            SetMessage(StatusText, "上班时间请使用 HH:mm。", true);
            WorkStart.Focus();
            return;
        }

        if (!TimeSpan.TryParseExact(WorkEnd.Text, @"hh\:mm", CultureInfo.InvariantCulture, out var workEnd))
        {
            ShowPreferenceFieldError(WorkEndError, "下班时间请使用 HH:mm。", WorkEnd);
            SetMessage(StatusText, "下班时间请使用 HH:mm。", true);
            WorkEnd.Focus();
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
        _loadingCountdownEditor = true;
        try
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
            SyncCountdownReminderModeFromRules();
            CountdownEditorTitle.Text = "编辑倒计时";
            SaveCountdownButton.Content = "保存修改";
            CancelCountdownEdit.Visibility = Visibility.Visible;
        }
        finally
        {
            _loadingCountdownEditor = false;
        }
    }

    private void OnDeleteCountdown(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var snapshot = _desktopWorkspace.Snapshot;
        var record = snapshot.Settings.Countdowns.First(item => item.Id == id);
        if (new ConfirmationDialog(this, "删除倒计时", $"确定删除“{record.Name}”吗？").ShowDialog() != true)
        {
            return;
        }

        var undo = DeletedRecordUndo.ForCountdown(
            record,
            DeletedRecordUndo.CaptureReminderStates(
                snapshot.Settings.ReminderStates,
                record.ReminderRules));
        var result = _desktopWorkspace.Apply(new DeleteCountdownChange(id));
        if (!result.Succeeded)
        {
            SetMessage(StatusText, $"删除失败：{result.Error}", true);
            return;
        }

        RegisterDeleteUndo(undo);
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

        var snapshot = _desktopWorkspace.Snapshot;
        var note = snapshot.Settings.Notes.First(item => item.Id == id);
        if (new ConfirmationDialog(this, "删除便签", $"确定删除“{note.Text}”吗？").ShowDialog() != true)
        {
            return;
        }

        var undo = DeletedRecordUndo.ForNote(
            note,
            DeletedRecordUndo.CaptureReminderStates(
                snapshot.Settings.ReminderStates,
                note.ReminderRules));
        var result = _desktopWorkspace.Apply(new DeleteNoteChange(id));
        if (!result.Succeeded)
        {
            SetMessage(StatusText, $"删除失败：{result.Error}", true);
            return;
        }

        RegisterDeleteUndo(undo);
    }

    // #155：最近一次删除的短时撤销——状态条入口以 #151 状态条表面合同呈现；
    // 主窗口与悬浮窗便签删除成功后都会经 RegisterDeleteUndo 提供同一入口。
    internal void RegisterDeleteUndo(DeletedRecordUndo undo)
    {
        _deleteUndo = undo;
        SetMessage(StatusText, $"已删除{undo.KindName}「{undo.Description}」。", false);
        AutomationProperties.SetName(
            MainStatusUndoButton,
            $"撤销删除{undo.KindName}「{undo.Description}」");
        MainStatusUndoButton.Visibility = Visibility.Visible;
        ResetDeleteUndoTimer();
    }

    private void OnUndoDeleteClick(object sender, RoutedEventArgs eventArgs)
    {
        var undo = _deleteUndo;
        if (undo is null)
        {
            return;
        }

        var result = _desktopWorkspace.Apply(undo.ToChange());
        if (!result.Succeeded)
        {
            SetMessage(StatusText, $"撤销失败：{result.Error}", true);
            return;
        }

        ExpireDeleteUndo();
        SetMessage(StatusText, $"已撤销删除{undo.KindName}「{undo.Description}」。", false);
    }

    private void ResetDeleteUndoTimer()
    {
        _deleteUndoTimer ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = DeleteUndoOfferDuration
        };
        _deleteUndoTimer.Stop();
        _deleteUndoTimer.Tick -= OnDeleteUndoTimerTick;
        _deleteUndoTimer.Tick += OnDeleteUndoTimerTick;
        _deleteUndoTimer.Start();
    }

    private void OnDeleteUndoTimerTick(object? sender, EventArgs eventArgs)
    {
        ExpireDeleteUndo();
    }

    private void ExpireDeleteUndo()
    {
        _deleteUndoTimer?.Stop();
        _deleteUndo = null;
        MainStatusUndoButton.Visibility = Visibility.Collapsed;
    }

    internal DeletedRecordUndo? DeleteUndoForValidation => _deleteUndo;

    internal void ExpireDeleteUndoForValidation() => ExpireDeleteUndo();

    // #155：退出确认的脏状态描述——显示设置脏沿用既有 HasUnsavedSettingsEdits 语义；
    // 编辑器脏指倒计时编辑、便签编辑与便签草稿正文，进程退出会丢弃这些未保存内容。
    internal string? DescribeUnsavedExitWork()
    {
        var parts = new List<string>();
        if (HasUnsavedSettingsEdits(_desktopWorkspace.Snapshot.Settings.Preferences))
        {
            parts.Add("显示设置有未保存的更改。");
        }

        if (_editingCountdownId is not null)
        {
            parts.Add("倒计时编辑器有未保存的更改。");
        }

        if (_editingNoteId is not null || _noteDateDirty || !string.IsNullOrEmpty(NoteText.Text))
        {
            parts.Add("便签编辑器有未保存的草稿。");
        }

        return parts.Count == 0 ? null : string.Join(string.Empty, parts);
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
        _loadingCountdownEditor = true;
        try
        {
            _editingCountdownId = null;
            CountdownName.Clear();
            SetCountdownTarget(_desktopWorkspace.Snapshot.BeijingNow.AddDays(1));
            CountdownTags.SetSelection(Array.Empty<string>());
            _countdownReminderRules = Array.Empty<ReminderRule>();
            SyncCountdownReminderModeFromRules();
            CountdownEditorTitle.Text = "新增倒计时";
            SaveCountdownButton.Content = "＋ 添加倒计时";
            CancelCountdownEdit.Visibility = Visibility.Collapsed;
            ClearCountdownFieldErrors();
            CountdownMessage.Text = string.Empty;
        }
        finally
        {
            _loadingCountdownEditor = false;
        }
    }

    private void ResetNoteEditor()
    {
        _editingNoteId = null;
        ClearNoteFieldErrors();
        NoteText.Clear();
        _settingDefaultDate = true;
        var snapshot = _desktopWorkspace.Snapshot;
        NoteDate.SelectedDate = snapshot.BeijingNow.Date;
        _settingDefaultDate = false;
        _noteDateDirty = false;
        NoteTags.SetSelection(snapshot.Filter.SelectedTagIds);
        _noteReminderRules = Array.Empty<ReminderRule>();
        UpdateNoteReminderRulesSummary();
        SaveNoteButton.Content = "＋ 添加";
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
                FormatMonthlySalary(preferences.MonthlySalary),
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

    // #148：月薪与「今日已赚」使用同一套本地数字格式（N2，当前区域千位分隔 + 两位小数），
    // 显示可用 NumberStyles.Number 原样解析回写。
    private static string FormatMonthlySalary(decimal salary) =>
        salary.ToString("N2", CultureInfo.CurrentCulture);

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
            return;
        }

        _settingsDraftDirty = false;
        _loadingPreferences = true;
        try
        {
            DisplayPrecision.SelectedIndex = PrecisionIndex(preferences.DisplayPrecision);
            MonthlySalary.Text = FormatMonthlySalary(preferences.MonthlySalary);
            WorkStart.Text = CountdownFormatting.FormatWorkTime(preferences.WorkStart);
            WorkEnd.Text = CountdownFormatting.FormatWorkTime(preferences.WorkEnd);
        }
        finally
        {
            _loadingPreferences = false;
        }
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
            FocusCountdownDatePrerequisite();
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
        SyncCountdownReminderModeFromRules();
        CountdownMessage.Text = string.Empty;
    }

    private void ApplyCountdownReminderMode(CountdownReminderMode mode)
    {
        if (_loadingCountdownEditor)
        {
            return;
        }

        switch (mode)
        {
            case CountdownReminderMode.None:
                _countdownReminderRules = Array.Empty<ReminderRule>();
                _countdownReminderMode = CountdownReminderMode.None;
                EnsureCountdownReminderModeRadio(mode);
                break;
            case CountdownReminderMode.AtDeadline:
                var targetUtc = GetCountdownTargetUtc();
                if (targetUtc is null)
                {
                    FocusCountdownDatePrerequisite();
                    EnsureCountdownReminderModeRadio(_countdownReminderMode);
                    return;
                }

                _countdownReminderRules = Array.AsReadOnly(new ReminderRule[]
                {
                    new AbsoluteOnceReminderRule(Guid.NewGuid().ToString("N"), targetUtc.Value)
                });
                _countdownReminderMode = CountdownReminderMode.AtDeadline;
                EnsureCountdownReminderModeRadio(mode);
                break;
            case CountdownReminderMode.Custom:
                if (GetCountdownTargetUtc() is null)
                {
                    FocusCountdownDatePrerequisite();
                    EnsureCountdownReminderModeRadio(_countdownReminderMode);
                    return;
                }

                OpenCountdownReminderRulesEditor();
                SyncCountdownReminderModeFromRules();
                return;
        }

        UpdateCountdownReminderRulesSummary();
    }

    internal void ApplyCountdownReminderModeForValidation(CountdownReminderMode mode) =>
        ApplyCountdownReminderMode(mode);

    private void FocusCountdownDatePrerequisite()
    {
        const string message = "请先选择完整的倒计时截止时间。";
        ShowCountdownValidationError(CountdownDateError, message, CountdownDate);
        SetMessage(CountdownMessage, message, true);
        // DatePickerTextBoxStyle deliberately disables the inner PART_TextBox;
        // focus the DatePicker owner so keyboard focus remains reachable.
        var target = (Control)CountdownDate;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => Keyboard.Focus(target)));
    }

    private void EnsureCountdownReminderModeRadio(CountdownReminderMode mode)
    {
        RadioButton modeRadio = mode switch
        {
            CountdownReminderMode.None => CountdownReminderNoRadio,
            CountdownReminderMode.AtDeadline => CountdownReminderTimeRadio,
            _ => CountdownReminderCustomRadio
        };
        if (modeRadio.IsChecked != true)
        {
            modeRadio.IsChecked = true;
        }
    }

    private void OnCountdownTargetFieldsChanged()
    {
        if (_loadingCountdownEditor || _countdownReminderMode != CountdownReminderMode.AtDeadline)
        {
            return;
        }

        var targetUtc = GetCountdownTargetUtc();
        if (targetUtc is null)
        {
            return;
        }

        _countdownReminderRules = Array.AsReadOnly(new ReminderRule[]
        {
            new AbsoluteOnceReminderRule(Guid.NewGuid().ToString("N"), targetUtc.Value)
        });
        UpdateCountdownReminderRulesSummary();
    }

    private void SyncCountdownReminderModeFromRules()
    {
        _countdownReminderMode = DeriveCountdownReminderMode(_countdownReminderRules, GetCountdownTargetUtc());
        RadioButton modeRadio = _countdownReminderMode switch
        {
            CountdownReminderMode.None => CountdownReminderNoRadio,
            CountdownReminderMode.AtDeadline => CountdownReminderTimeRadio,
            _ => CountdownReminderCustomRadio
        };
        if (modeRadio.IsChecked != true)
        {
            modeRadio.IsChecked = true;
        }

        UpdateCountdownReminderRulesSummary();
    }

    internal static CountdownReminderMode DeriveCountdownReminderMode(
        IReadOnlyList<ReminderRule> rules,
        DateTimeOffset? countdownTargetUtc)
    {
        if (rules.Count == 0)
        {
            return CountdownReminderMode.None;
        }

        return rules is [AbsoluteOnceReminderRule once] &&
            countdownTargetUtc is not null &&
            once.ScheduledUtc == countdownTargetUtc.Value
                ? CountdownReminderMode.AtDeadline
                : CountdownReminderMode.Custom;
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
                // #152：筛选标签芯片相邻间距向紧凑下限族靠拢（6 → 8，与 #143 相邻命令间距一致）。
                Margin = new Thickness(0, 0, 8, 8),
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
        // #154：只更新筛选文字，不再整体替换 Button.Content——原实现把 XAML 里的漏斗图标+
        // 文字 StackPanel 换成纯字符串，运行时第一次摘要刷新后漏斗即消失（v22 帧实证只剩「筛选」）。
        FilterButtonText.Text = count == 0 ? "筛选" : $"筛选 · {count}";
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

    private FrameworkElement CreateTagChips(IReadOnlyList<string> tagIds, bool hashPrefix = false)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var tag in _desktopWorkspace.Snapshot.Settings.Tags.Where(tag => tagIds.Contains(
                     tag.Id,
                     StringComparer.OrdinalIgnoreCase)))
        {
            // #136 便签卡目标图：芯片文字带 # 前缀（仅展示层，标签名与持久化不变）。
            // #153：芯片单行不折字（NoWrap），超长以省略号收尾并经 ToolTip 提供全文；
            // 芯片集合仍由 WrapPanel 先折行，单枚超长芯片不得把卡片撑破（父票裁决：先折行再省略）。
            var chipText = new TextBlock
            {
                Text = hashPrefix ? $"# {tag.Name}" : tag.Name,
                FontSize = (double)FindResource("TypeBodyFontSize"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            chipText.SetResourceReference(TextBlock.ForegroundProperty, "TagChipTextBrush");
            var chip = new Border
            {
                Uid = tag.Id,
                MaxWidth = TagChipMaxWidth,
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(7, 3, 7, 3),
                CornerRadius = new CornerRadius(10),
                Child = chipText,
                ToolTip = tag.Name
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

    private static bool UseWhiteNoteCard(NoteRecord note)
    {
        if (note.IsCompleted)
        {
            return true;
        }

        var stableHash = note.Id.Aggregate(17, (hash, character) => unchecked(hash * 31 + character));
        return (stableHash & 1) == 0;
    }

    private Border NoteCard(UIElement content, bool useWhiteSurface)
    {
        var card = new Border
        {
            Child = content,
            // #153：卡片内边距与卡间距消费间距 token（§13.3 SpacingCardPadding=14 / SpacingUnit=8），
            // 不再各写字面量。
            Margin = new Thickness(0, 0, 0, (double)FindResource("SpacingUnit")),
            Padding = new Thickness((double)FindResource("SpacingCardPadding")),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(
            Border.BackgroundProperty,
            useWhiteSurface ? "NoteWhiteBrush" : "NoteBrush");
        card.SetResourceReference(
            Border.BorderBrushProperty,
            useWhiteSurface ? "PeachLineBrush" : "NoteLineBrush");
        return card;
    }

    private Button NoteActionButton(string content, string id, string accessibleName, string? glyph = null)
    {
        var button = UiAccessibility.Name(
            new Button
            {
                Content = content,
                Tag = id,
                MinWidth = 72,
                MinHeight = 44,
                FontSize = (double)FindResource("TypeBodyFontSize"),
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("SecondaryButton")
            },
            accessibleName);
        if (glyph is not null)
        {
            // Content 保持可见字符串（编辑/删除渲染探针与自动化名称依赖它），
            // 图标经 ContentTemplate 注入，不改按钮语义内容。
            button.ContentTemplate = glyph == "pencil"
                ? CreateGlyphButtonTemplate(
                    "M 2.5,13.2 L 3.2,10.4 L 11.6,2 C 12.2,1.4 13.2,1.4 13.8,2 C 14.4,2.6 14.4,3.6 13.8,4.2 L 5.4,12.6 L 2.5,13.2 Z",
                    "InkBrush")
                : CreateGlyphButtonTemplate(
                    "M 3.4,4.5 L 14.6,4.5 M 6.2,4.5 L 6.2,3 C 6.2,2.4 6.7,2 7.3,2 L 10.7,2 C 11.3,2 11.8,2.4 11.8,3 L 11.8,4.5 M 4.6,4.5 L 5.3,14.2 C 5.3,15.2 6.1,16 7.1,16 L 10.9,16 C 11.9,16 12.7,15.2 12.7,14.2 L 13.4,4.5",
                    "DangerBrush");
        }

        return button;
    }

    private DataTemplate CreateGlyphButtonTemplate(string glyphData, string glyphBrushKey)
    {
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var glyph = new FrameworkElementFactory(typeof(WpfPath));
        glyph.Name = "Glyph";
        glyph.SetValue(WpfPath.WidthProperty, 16.0);
        glyph.SetValue(WpfPath.HeightProperty, 16.0);
        glyph.SetValue(WpfPath.DataProperty, Geometry.Parse(glyphData));
        glyph.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
        glyph.SetValue(WpfPath.StrokeThicknessProperty, 1.6);
        glyph.SetValue(WpfPath.StrokeStartLineCapProperty, PenLineCap.Round);
        glyph.SetValue(WpfPath.StrokeEndLineCapProperty, PenLineCap.Round);
        glyph.SetValue(WpfPath.StrokeLineJoinProperty, PenLineJoin.Round);
        glyph.SetValue(WpfPath.VerticalAlignmentProperty, VerticalAlignment.Center);
        glyph.SetResourceReference(WpfPath.StrokeProperty, glyphBrushKey);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(TextBlock.MarginProperty, new Thickness(7, 0, 0, 0));
        label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(TextBlock.TextProperty, new Binding());
        row.AppendChild(glyph);
        row.AppendChild(label);
        return new DataTemplate { VisualTree = row };
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
                FontSize = (double)FindResource("TypeBodyFontSize"),
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
        yield return CountdownReminderHelp;
        yield return CountdownReminderNoRadio;
        yield return CountdownReminderTimeRadio;
        yield return CountdownReminderCustomRadio;
        yield return SaveCountdownButton;
        yield return DisplayPrecision;
        yield return MonthlySalary;
        yield return WorkStart;
        yield return WorkEnd;
        yield return SavePreferencesButton;
        yield return MinimizeButton;
        yield return CloseButton;
        yield return NoteText;
        yield return NoteDate;
        yield return SaveNoteButton;
        yield return NoteSearch;
        yield return NoteGroupingSelector;
        yield return FilterButton;
        yield return TagManagerButton;
    }

    private static void NameControl(FrameworkElement control, string name) =>
        UiAccessibility.Name(control, name);

    // #156：数量文字只在数值真正变化时更新，避免分钟级刷新让唯一的数量直播区反复空播。
    private static void SetRecordCountText(TextBlock target, string text)
    {
        if (string.Equals(target.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        target.Text = text;
    }

    // #156：可见标签 → 输入的标签关系表（主窗全部带可见文字的字段）。输入一律不携带与
    // 标签重复的显式自动化名；仅在标签文本比原自动化名更短时，把原语境写入帮助文本。
    private void WireFieldLabelRelations()
    {
        LabelFor(CountdownNameLabel, CountdownName);
        LabelFor(CountdownDateLabel, CountdownDate);
        LabelFor(CountdownHourLabel, CountdownHour);
        LabelFor(CountdownMinuteLabel, CountdownMinute);
        LabelFor(CountdownSecondLabel, CountdownSecond);
        LabelFor(DisplayPrecisionLabel, DisplayPrecision, "悬浮窗显示精度");
        LabelFor(MonthlySalaryLabel, MonthlySalary);
        LabelFor(WorkStartLabel, WorkStart);
        LabelFor(WorkEndLabel, WorkEnd);
        LabelFor(NoteTextLabel, NoteText);
        LabelFor(NoteDateLabel, NoteDate);
        LabelFor(NoteSearchLabel, NoteSearch);
        LabelFor(NoteDateFilterLabel, NoteDateFilter, "按便签日期筛选");
        LabelFor(NewTagInputLabel, NewTagInput);
    }

    private static void LabelFor(FrameworkElement label, FrameworkElement input, string? helpText = null)
    {
        AutomationProperties.SetLabeledBy(input, label);
        if (helpText is not null)
        {
            AutomationProperties.SetHelpText(input, helpText);
        }
    }

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

    // #151：字段级错误反馈（镜像悬浮窗快速新增）——错误文本完整出现在字段旁、不裁切；
    // 名称/帮助文本同步到错误行与来源控件，修正字段值后由 WireEvents 的回调清除。
    private static void ShowCountdownValidationError(TextBlock field, string message, Control source)
    {
        SetFieldError(field, message);
        AutomationProperties.SetHelpText(source, message);
    }

    private void ShowPreferenceFieldError(TextBlock field, string message, Control source)
    {
        SetFieldError(field, message);
        AutomationProperties.SetHelpText(source, message);
    }

    private void ClearCountdownFieldErrors()
    {
        ClearCountdownFieldError(CountdownNameError);
        ClearCountdownFieldError(CountdownDateError);
    }

    private void ShowNoteValidationError(TextBlock field, string message, Control source)
    {
        SetFieldError(field, message);
        AutomationProperties.SetHelpText(source, message);
    }

    private void ClearNoteFieldErrors()
    {
        ClearNoteFieldError(NoteTextError);
        ClearNoteFieldError(NoteDateError);
    }

    private void ClearNoteFieldError(TextBlock field)
    {
        var previousMessage = field.Text;
        SetFieldError(field, string.Empty);
        foreach (var control in NoteValidationControls())
        {
            if (string.Equals(
                    AutomationProperties.GetHelpText(control),
                    previousMessage,
                    StringComparison.Ordinal))
            {
                AutomationProperties.SetHelpText(control, string.Empty);
            }
        }

        if (!string.IsNullOrWhiteSpace(previousMessage) &&
            string.Equals(NoteMessage.Text, previousMessage, StringComparison.Ordinal))
        {
            NoteMessage.Text = string.Empty;
            AutomationProperties.SetName(NoteMessage, string.Empty);
        }
    }

    private void ClearPreferenceFieldErrors()
    {
        ClearPreferenceFieldError(MonthlySalaryError);
        ClearPreferenceFieldError(WorkStartError);
        ClearPreferenceFieldError(WorkEndError);
    }

    private void ClearCountdownFieldError(TextBlock field)
    {
        var previousMessage = field.Text;
        SetFieldError(field, string.Empty);
        foreach (var control in CountdownValidationControls())
        {
            if (string.Equals(
                    AutomationProperties.GetHelpText(control),
                    previousMessage,
                    StringComparison.Ordinal))
            {
                AutomationProperties.SetHelpText(control, string.Empty);
            }
        }

        if (!string.IsNullOrWhiteSpace(previousMessage) &&
            string.Equals(CountdownMessage.Text, previousMessage, StringComparison.Ordinal))
        {
            CountdownMessage.Text = string.Empty;
            AutomationProperties.SetName(CountdownMessage, string.Empty);
        }
    }

    private void ClearPreferenceFieldError(TextBlock field) => SetFieldError(field, string.Empty);

    private static void SetFieldError(TextBlock field, string message)
    {
        field.Text = message;
        AutomationProperties.SetName(field, message);
        AutomationProperties.SetHelpText(field, message);
        field.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private IEnumerable<Control> CountdownValidationControls()
    {
        yield return CountdownName;
        yield return CountdownDate;
        yield return CountdownHour;
        yield return CountdownMinute;
        yield return CountdownSecond;
    }

    private IEnumerable<Control> NoteValidationControls()
    {
        yield return NoteText;
        yield return NoteDate;
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
    }

    private void UpdateReferenceDecorationVisibility()
    {
        var useReferenceDecorations = MainReferenceDecorationsLoaded;
        CharacterBackgroundLayer.Visibility = useReferenceDecorations
            ? Visibility.Collapsed
            : Visibility.Visible;
        CharacterPanelLayer.Visibility = useReferenceDecorations
            ? Visibility.Collapsed
            : Visibility.Visible;
        CountdownReferenceDecoration.Visibility =
            useReferenceDecorations && _workspace == WorkspaceKind.Countdowns
                ? Visibility.Visible
                : Visibility.Collapsed;
        NotesReferenceDecoration.Visibility =
            useReferenceDecorations && _workspace == WorkspaceKind.Notes
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

internal sealed class MainCountdownBlocksHost
{
    public required Grid Surface { get; init; }

    public bool IsPrimary { get; init; }
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

internal enum CountdownReminderMode
{
    None,
    AtDeadline,
    Custom
}

internal sealed record ReminderModeClickResult(
    bool NoReminder,
    bool OnceReminder,
    bool RepeatingReminder)
{
    public static ReminderModeClickResult NotRun { get; } = new(false, false, false);
}
