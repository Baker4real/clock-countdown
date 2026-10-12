using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class FloatingWindow : Window
{
    private const double CountdownScrollBarSafeOffset = 76;

    // #149：列表视口底部稳定内边距（12 DIP，§5 区块节奏），末卡不视觉顶住底部命令区。
    private const double FloatingNotesListBottomPadding = 12;
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
    private readonly DesktopWorkspace _desktopWorkspace;
    private readonly HolidayCalendar _calendar;
    private readonly IManagerWorkspaceHost _manager;
    private WorkspaceKind _workspace = WorkspaceKind.Countdowns;
    private WorkspaceKind _renderedWorkspace = WorkspaceKind.Countdowns;
    private readonly Dictionary<WorkspaceKind, double> _workspaceScrollOffsets = new();
    private readonly Dictionary<string, string> _noteCardErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastCountdownSeconds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _countdownRowElements = new(StringComparer.Ordinal);
    private int _floatingRowsRenderVersion;
    private string? _floatingRenderSignature;
    private bool _hasBeenVisible;
    private bool _settingDefaultDate;
    private bool _dateDirty;
    private bool _searchExpanded;
    private bool _syncingSearch;
    private bool _quickAddExpanded;
    private bool _earningsHidden;
    private IReadOnlyList<ReminderRule> _floatingReminderRules = Array.Empty<ReminderRule>();
    private FloatingThemePalette? _activeTheme;
    private Brush _floatingPrimaryBrush = Brushes.Black;
    private Brush _floatingMutedBrush = Brushes.Gray;
    private Brush _floatingSurfaceBrush = Brushes.White;
    private Brush _floatingNoteCardBrush = Brushes.White;
    private Brush _floatingBorderBrush = Brushes.Transparent;
    private BitmapImage? _dayCardRaster;
    private bool _isApplyingBottomAnchor;
    private bool _trackManualWindowPlacement;
    private bool _hasManualWindowPlacement;
    private Point? _bottomAnchorPosition;
    private int _nativeWindowDragRequestCount;
    private int _nativeWindowDragCompletionCount;
    private readonly string _assetRoot;
    private bool _assetsLoaded;
    private readonly DeleteUndoCountdown _deleteUndoCountdown;
    private bool _retainFloatingUndoMessage;

    internal FloatingWindow(
        DesktopWorkspace desktopWorkspace,
        HolidayCalendar calendar,
        IManagerWorkspaceHost manager,
        string assetRoot,
        bool deferAssets = false)
    {
        _desktopWorkspace = desktopWorkspace;
        _calendar = calendar;
        _manager = manager;
        _assetRoot = assetRoot;
        InitializeComponent();
        if (!deferAssets)
        {
            LoadAssets(assetRoot);
        }

        _deleteUndoCountdown = new DeleteUndoCountdown(Dispatcher, ExpireOwnedFloatingDeleteUndo);
        ConfigureControls();
        WireEvents();
        WindowWorkAreaGeometry.TrackChanges(this, Reanchor);
        PreviewKeyDown += OnPreviewKeyDown;
        ApplyTheme();
        _desktopWorkspace.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        _calendar.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        ShowWorkspace(WorkspaceKind.Countdowns);
    }

    internal void LoadDeferredAssets()
    {
        if (_assetsLoaded)
        {
            return;
        }

        LoadAssets(_assetRoot);
        ApplyTheme();
        Refresh();
    }

    internal static IReadOnlyList<string> RequiredQuickAddThemeChecks => QuickAddThemeCheckNames;

    internal bool UsesDedicatedThemePalette =>
        _activeTheme is not null &&
        SurfacesFollowActiveTheme &&
        ReferenceEquals(FloatingTitle.Foreground, FindResource(_activeTheme.HeaderTextBrushKey)) &&
        ReferenceEquals(FloatingReminderRulesSummary.Foreground, FindResource(_activeTheme.NoteInputTextBrushKey)) &&
        ReferenceEquals(FloatingNoteTextError.Foreground, FindResource(_activeTheme.ErrorBrushKey)) &&
        ReferenceEquals(FloatingNoteDateError.Foreground, FindResource(_activeTheme.ErrorBrushKey)) &&
        ReferenceEquals(FloatingReminderMessage.Foreground, FindResource(_activeTheme.ErrorBrushKey)) &&
        ReferenceEquals(FloatingNoteMessage.Foreground, FindResource(_activeTheme.ErrorBrushKey)) &&
        ReferenceEquals(FloatingCountdownsButton.Style, FindResource("FloatingHeaderSegmentButton")) &&
        ReferenceEquals(FloatingNotesButton.Style, FindResource("FloatingHeaderSegmentButton")) &&
        ReferenceEquals(MinimizeToTrayButton.Style, FindResource("FloatingHeaderIconButton")) &&
        ReferenceEquals(OpenManagerButton.Style, FindResource("FloatingHeaderIconButton")) &&
        ReferenceEquals(CloseFloatingButton.Style, FindResource("FloatingHeaderIconButton")) &&
        PlushSceneFollowsActiveTheme;

    internal FloatingQuickAddPaletteBindingValidation ProbeQuickAddPaletteBindings()
    {
        var actual = new FloatingQuickAddPaletteMapping(
            ResolveQuickAddPaletteResourceKey(FloatingNoteEntry.Background),
            ResolveQuickAddPaletteResourceKey(FloatingNoteText.Foreground),
            ResolveQuickAddPaletteResourceKey(FloatingNoteTextError.Foreground),
            ResolveQuickAddPaletteResourceKey(FloatingReminderRulesSummary.Foreground),
            ResolveQuickAddPaletteResourceKey(FloatingReminderMessage.Foreground));
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
            if (ReferenceEquals(brush, FindResource(resourceKey)))
            {
                return resourceKey;
            }
        }

        return string.Empty;
    }

    private bool SurfacesFollowActiveTheme =>
        _activeTheme is not null &&
        FloatingDayRasterScene.Visibility == Visibility.Visible &&
        IsTransparent(TaskCard.Background) &&
        IsTransparent(TaskCard.BorderBrush) &&
        IsTransparent(FloatingHeaderSurface.Background) &&
        IsTransparent(FloatingHeaderSurface.BorderBrush) &&
        IsTransparent(FloatingThemeBackdrop.Background) &&
        IsTransparent(FloatingThemeBackdrop.BorderBrush) &&
        FloatingRows.Margin.Equals(new Thickness(9, 13, 10, 0));

    internal IReadOnlyList<string> DayRasterComponentAssetNames =>
    [
        AssetName(DayOuterShellImage.Source),
        AssetName(DayHeaderImage.Source),
        AssetName(_dayCardRaster),
        AssetName(DayMascotsImage.Source)
    ];

    internal bool DayRasterShellAndHeaderLoaded =>
        DayOuterShellImage.Source is not null &&
        DayHeaderImage.Source is not null;

    internal bool DayRasterDecorationsAreNonInteractive =>
        !FloatingDayRasterScene.IsHitTestVisible &&
        !DayOuterShellImage.IsHitTestVisible &&
        !DayHeaderImage.IsHitTestVisible &&
        !DayMascotsImage.IsHitTestVisible;

    internal bool DynamicHeaderControlsRemainWpf =>
        VisualTreeHelper.GetParent(FloatingTitle) is Border dragHandle &&
        ReferenceEquals(dragHandle, FloatingDragHandle) &&
        VisualTreeHelper.GetParent(FloatingDragHandle) is Grid &&
        FloatingDragHandle.IsHitTestVisible &&
        VisualTreeHelper.GetParent(FloatingCountdownsButton) is Grid &&
        VisualTreeHelper.GetParent(FloatingNotesButton) is Grid &&
        VisualTreeHelper.GetParent(MinimizeToTrayButton) is Grid &&
        VisualTreeHelper.GetParent(OpenManagerButton) is Grid &&
        VisualTreeHelper.GetParent(CloseFloatingButton) is Grid &&
        FloatingCountdownsButton.IsHitTestVisible &&
        FloatingNotesButton.IsHitTestVisible &&
        MinimizeToTrayButton.IsHitTestVisible &&
        OpenManagerButton.IsHitTestVisible &&
        CloseFloatingButton.IsHitTestVisible;

    private bool PlushSceneFollowsActiveTheme
    {
        get
        {
            if (_activeTheme is null)
            {
                return false;
            }

            return string.Equals(MascotPresentationMode(), "DayRaster", StringComparison.Ordinal) &&
                   DayMascotsImage.Source is not null;
        }
    }

    internal bool HasAccessibleThemeTextContrast
    {
        get
        {
            if (_activeTheme is null)
            {
                return false;
            }

            var header = ThemeBrush(_activeTheme.HeaderBrushKey);
            var headerText = ThemeBrush(_activeTheme.HeaderTextBrushKey);
            var headerDanger = ThemeBrush(_activeTheme.HeaderDangerBrushKey);
            var headerHover = ThemeBrush(_activeTheme.HeaderHoverBrushKey);
            var headerSelected = ThemeBrush(_activeTheme.HeaderSelectedBrushKey);
            var headerSelectedText = ThemeBrush(_activeTheme.HeaderSelectedTextBrushKey);
            return
                ApplicationTheme.MeetsContrast(header, headerText, 4.5) &&
                ApplicationTheme.MeetsContrast(headerHover, headerText, 4.5) &&
                ApplicationTheme.MeetsContrast(headerSelected, headerSelectedText, 4.5) &&
                ApplicationTheme.MeetsContrast(header, headerDanger, 4.5) &&
                ApplicationTheme.MeetsContrast(headerHover, headerDanger, 4.5) &&
                ApplicationTheme.MeetsContrast(
                    ThemeBrush(_activeTheme.NoteInputBrushKey),
                    ThemeBrush(_activeTheme.NoteInputTextBrushKey),
                    4.5) &&
                ApplicationTheme.MeetsContrast(
                    ThemeBrush(_activeTheme.NoteInputBrushKey),
                    ThemeBrush(_activeTheme.ErrorBrushKey),
                    4.5) &&
                HeaderControlsHaveAccessibleStates();
        }
    }

    internal bool IsBottomAnchored
    {
        get
        {
            var workArea = MonitorWorkAreaResolver.ResolveForWindow(this).WorkAreaDip;
            return Math.Abs((Top + ActualHeight) - (workArea.Bottom - 12)) <= 2.5;
        }
    }

    private FloatingDragInteractionExerciseResult RunDragInteractionExercise()
    {
        var originalWorkspace = _workspace;
        var originalLeft = Left;
        var originalTop = Top;
        var originallyManual = _hasManualWindowPlacement;
        var originalBottomAnchorPosition = _bottomAnchorPosition;
        var nativeDragRequestsBefore = _nativeWindowDragRequestCount;
        var nativeDragCompletionsBefore = _nativeWindowDragCompletionCount;
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            UpdateLayout();

            var workArea = MonitorWorkAreaResolver.ResolveForWindow(this).WorkAreaDip;
            var minimumLeft = workArea.Left + 12;
            var maximumLeft = Math.Max(minimumLeft, workArea.Right - ActualWidth - 12);
            var minimumTop = workArea.Top + 12;
            var maximumTop = Math.Max(minimumTop, workArea.Bottom - ActualHeight - 12);
            _hasManualWindowPlacement = true;
            Left = workArea.Right + 24;
            Top = workArea.Bottom + 24;

            var backgroundGesture = CreateMouseLeftButtonDownGesture();
            TaskWrap.RaiseEvent(backgroundGesture);
            var backgroundRequestsNativeWindowDrag =
                _nativeWindowDragRequestCount == nativeDragRequestsBefore + 1;
            var pointerGesture = CreateMouseLeftButtonDownGesture();
            FloatingDragHandle.RaiseEvent(pointerGesture);
            var titleRequestsNativeWindowDrag =
                _nativeWindowDragRequestCount == nativeDragRequestsBefore + 2;
            var syntheticGestureDoesNotClaimPhysicalCompletion =
                _nativeWindowDragCompletionCount == nativeDragCompletionsBefore;
            var dragAttemptClampsToWorkArea =
                Left >= minimumLeft - 0.5 &&
                Left <= maximumLeft + 0.5 &&
                Top >= minimumTop - 0.5 &&
                Top <= maximumTop + 0.5;

            var interactiveControls = new UIElement[]
            {
                FloatingCountdownsButton,
                FloatingNotesButton,
                MinimizeToTrayButton,
                OpenManagerButton,
                CloseFloatingButton,
                FloatingBodyScroller,
                FloatingAddNoteToggle,
                FloatingSearchNotes,
                FloatingOpenSettings,
                FloatingNoteText,
                FloatingNoteDate,
                FloatingManageReminderRules,
                FloatingTags,
                FloatingAddNote
            };
            foreach (var control in interactiveControls)
            {
                control.RaiseEvent(CreateMouseLeftButtonDownGesture());
            }

            return new FloatingDragInteractionExerciseResult(
                TaskWrap.Background is not null &&
                ReferenceEquals(TaskWrap.InputHitTest(new Point(260, 20)), TaskWrap) &&
                backgroundGesture.Handled &&
                backgroundRequestsNativeWindowDrag,
                FloatingDragHandle.IsHitTestVisible &&
                FloatingDragHandle.Background is not null &&
                FloatingDragHandle.Cursor == Cursors.SizeAll &&
                pointerGesture.Handled,
                titleRequestsNativeWindowDrag,
                syntheticGestureDoesNotClaimPhysicalCompletion,
                dragAttemptClampsToWorkArea,
                _nativeWindowDragRequestCount == nativeDragRequestsBefore + 2,
                interactiveControls.All(control => !IsVisualDescendantOf(control, FloatingDragHandle)));
        }
        finally
        {
            _isApplyingBottomAnchor = true;
            try
            {
                _hasManualWindowPlacement = originallyManual;
                _bottomAnchorPosition = originalBottomAnchorPosition;
                Left = originalLeft;
                Top = originalTop;
            }
            finally
            {
                _isApplyingBottomAnchor = false;
            }

            ShowWorkspace(originalWorkspace);
        }
    }

    private FloatingManualPlacementExerciseResult RunManualPlacementExercise()
    {
        var originallyManual = _hasManualWindowPlacement;
        var originalBottomAnchorPosition = _bottomAnchorPosition;
        var originalLeft = Left;
        var originalTop = Top;
        var workArea = MonitorWorkAreaResolver.ResolveForWindow(this).WorkAreaDip;
        var minimumLeft = workArea.Left + 12;
        var maximumLeft = Math.Max(minimumLeft, workArea.Right - ActualWidth - 12);
        var minimumTop = workArea.Top + 12;
        var maximumTop = Math.Max(minimumTop, workArea.Bottom - ActualHeight - 12);
        var manualLeft = maximumLeft > minimumLeft
            ? minimumLeft + Math.Min(24, maximumLeft - minimumLeft)
            : minimumLeft;
        var manualTop = maximumTop > minimumTop
            ? minimumTop + Math.Min(24, maximumTop - minimumTop)
            : minimumTop;

        try
        {
            Left = manualLeft;
            Top = manualTop;
            Refresh();

            var manualPositionPersists =
                _hasManualWindowPlacement &&
                Math.Abs(Left - manualLeft) <= 0.5 &&
                Math.Abs(Top - manualTop) <= 0.5;

            Left = workArea.Right + 24;
            Top = workArea.Bottom + 24;
            var displacedLeft = Left;
            var displacedTop = Top;
            Refresh();
            var refreshLeavesManualDisplacement =
                Math.Abs(Left - displacedLeft) <= 0.5 &&
                Math.Abs(Top - displacedTop) <= 0.5;

            Reanchor();

            var remainsWithinWorkArea =
                Left >= minimumLeft - 0.5 &&
                Left <= maximumLeft + 0.5 &&
                Top >= minimumTop - 0.5 &&
                Top <= maximumTop + 0.5;

            return new FloatingManualPlacementExerciseResult(
                manualPositionPersists && refreshLeavesManualDisplacement && remainsWithinWorkArea);
        }
        finally
        {
            _isApplyingBottomAnchor = true;
            try
            {
                _hasManualWindowPlacement = originallyManual;
                _bottomAnchorPosition = originalBottomAnchorPosition;
                Left = originalLeft;
                Top = originalTop;
            }
            finally
            {
                _isApplyingBottomAnchor = false;
            }
        }
    }

    internal IReadOnlyList<string> FloatingCountdownTexts { get; private set; } = Array.Empty<string>();

    internal int FloatingCountdownCount { get; private set; }

    // #212：渲染签名稳定刷新的验证可观察计数——渲染趟数（就地+重建）、重建代数、隐藏/最小化跳过次数。
    internal long FloatingRowsRenderPassCountForValidation { get; private set; }

    internal long FloatingRowsRebuildGenerationForValidation { get; private set; }

    internal long FloatingRowsSkippedRenderCountForValidation { get; private set; }

    internal bool FloatingLastRenderWasInPlaceForValidation { get; private set; }

    // #212：便签卡内可聚焦控件的角色标记；焦点恢复按「记录 ID + 角色」精确匹配。
    internal static readonly DependencyProperty NoteControlRoleProperty =
        DependencyProperty.RegisterAttached(
            "NoteControlRole",
            typeof(string),
            typeof(FloatingWindow));

    internal const string NoteControlRoleCompletion = "completion";
    internal const string NoteControlRoleOverflow = "overflow";

    internal string IncomeText => FloatingIncome.Text;

    internal event EventHandler? ExitRequested;

    internal event EventHandler? MinimizeToTrayRequested;

    // #155：退出确认的脏状态描述——快速新增正文非空即视为未保存草稿（保存成功后正文被清空）。
    internal string? DescribeUnsavedExitWork() =>
        string.IsNullOrEmpty(FloatingNoteText.Text)
            ? null
            : "悬浮窗便签草稿未保存。";

    private bool RunCrossDayReminderAddExercise()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        SetQuickAddExpanded(true);
        var initial = _desktopWorkspace.Snapshot;
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
        FloatingNoteText.Text = "验证快速跨天提醒";
        FloatingNoteDate.SelectedDate = noteDate;
        _floatingReminderRules = new ReminderRule[]
        {
            new FixedIntervalReminderRule(
                Guid.NewGuid().ToString("N"),
                firstDue.ToUniversalTime(),
                new ReminderInterval(30))
        };
        SetQuickAddExpanded(true);
        FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var afterAdd = _desktopWorkspace.Snapshot.Settings;
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
        SetQuickAddExpanded(true);
        var initial = _desktopWorkspace.Snapshot;
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

        FloatingNoteText.Text = "验证悬浮窗不提醒";
        FloatingNoteDate.SelectedDate = noteDate;
        _floatingReminderRules = Array.Empty<ReminderRule>();
        SetQuickAddExpanded(true);
        FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var noReminderWorks = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗不提醒")?.Reminder is NoReminder;

        FloatingNoteText.Text = "验证悬浮窗仅一次";
        FloatingNoteDate.SelectedDate = noteDate;
        _floatingReminderRules = new ReminderRule[]
        {
            new AbsoluteOnceReminderRule(
                Guid.NewGuid().ToString("N"),
                onceDue.ToUniversalTime())
        };
        SetQuickAddExpanded(true);
        FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var onceWorks = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗仅一次")?.Reminder is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            } once &&
            once.FirstDueUtc == onceDue.ToUniversalTime();

        FloatingNoteText.Text = "验证悬浮窗重复提醒";
        FloatingNoteDate.SelectedDate = noteDate;
        _floatingReminderRules = new ReminderRule[]
        {
            new FixedIntervalReminderRule(
                Guid.NewGuid().ToString("N"),
                repeatDue.ToUniversalTime(),
                new ReminderInterval(30))
        };
        SetQuickAddExpanded(true);
        FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var repeatWorks = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗重复提醒")?.Reminder is RepeatingReminder repeating &&
            repeating.FirstDueUtc == repeatDue.ToUniversalTime() &&
            repeating.Interval.Minutes == 30;

        return new ReminderModeClickResult(noReminderWorks, onceWorks, repeatWorks);
    }

    internal void ShowWorkspace(WorkspaceKind workspace)
    {
        _workspace = workspace;
        FloatingCountdownsButton.IsChecked = workspace == WorkspaceKind.Countdowns;
        FloatingNotesButton.IsChecked = workspace == WorkspaceKind.Notes;
        FloatingTitle.Text = workspace == WorkspaceKind.Countdowns ? "倒计时" : "待办便签";
        FloatingBodyScroller.Margin = workspace == WorkspaceKind.Notes
            ? new Thickness(0, 0, 76, 0)
            : new Thickness(0);
        UpdateFloatingBodyScrollBarPlacement();
        FloatingNotesCommandArea.Visibility =
            workspace == WorkspaceKind.Notes ? Visibility.Visible : Visibility.Collapsed;
        SetQuickAddExpanded(false);
        UpdateSearchPresentation();
        if (workspace == WorkspaceKind.Notes)
        {
            var snapshot = _desktopWorkspace.Snapshot;
            FloatingTags.SetSelection(snapshot.Filter.SelectedTagIds);
            if (!_dateDirty)
            {
                _settingDefaultDate = true;
                FloatingNoteDate.SelectedDate = snapshot.BeijingNow.Date;
                _settingDefaultDate = false;
            }
        }

        Refresh();
    }

    /// <summary>
    ///     底部“＋ 添加便签”主按钮的展开/收起：默认收起；展开时按当前筛选标签初始化并保持日期默认值，
    ///     列表滚动偏移不受影响（表单位于外层滚动容器之外）。
    /// </summary>
    internal void SetQuickAddExpanded(bool expanded)
    {
        _quickAddExpanded = expanded;
        FloatingNoteEntry.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        // The command bar remains reachable while the in-place editor is open;
        // the search action is part of the notes workspace contract.
        FloatingNotesCommandBar.Visibility = Visibility.Visible;
        // #149：展开后切换钮降级为次级「收起」，表单「添加」是该状态唯一实心粉钮。
        ApplyQuickAddTogglePresentation(expanded);
        if (!expanded)
        {
            return;
        }

        var snapshot = _desktopWorkspace.Snapshot;
        FloatingTags.SetSelection(snapshot.Filter.SelectedTagIds);
        if (!_dateDirty)
        {
            _settingDefaultDate = true;
            FloatingNoteDate.SelectedDate = snapshot.BeijingNow.Date;
            _settingDefaultDate = false;
        }
    }

    /// <summary>
    ///     #149：底部切换钮双态呈现——收起态为实心粉胶囊「＋ 添加便签」（FloatingAccentButton），
    ///     展开态换次级样式并显示「收起」；可访问名随动作与对象同步。
    /// </summary>
    private void ApplyQuickAddTogglePresentation(bool expanded)
    {
        if (expanded)
        {
            FloatingAddNoteToggle.Style = (Style)FindResource("SecondaryButton");
            FloatingAddNoteToggle.Content = "收起";
            UiAccessibility.Name(FloatingAddNoteToggle, "收起快速新增便签表单");
            return;
        }

        FloatingAddNoteToggle.Style = (Style)FindResource("FloatingAccentButton");
        FloatingAddNoteToggle.Content = CreateAddNoteToggleContent();
        UiAccessibility.Name(FloatingAddNoteToggle, "添加便签");
    }

    private StackPanel CreateAddNoteToggleContent()
    {
        var circle = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16 };
        circle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MainActionTextBrush");
        var plus = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 4.6,8 L 11.4,8 M 8,4.6 L 8,11.4"),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        plus.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "PinkAccentStrongBrush");
        var icon = new Grid
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.Children.Add(circle);
        icon.Children.Add(plus);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                icon,
                new TextBlock
                {
                    Text = "添加便签",
                    FontSize = (double)FindResource("TypeCaptionFontSize"),
                    Margin = new Thickness(7, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    internal void UpdateTime(DateTimeOffset nowUtc)
    {
        if (!_dateDirty)
        {
            _settingDefaultDate = true;
            FloatingNoteDate.SelectedDate = ChinaStandardTime.ToBeijingTime(nowUtc).Date;
            _settingDefaultDate = false;
        }

        RenderRows(nowUtc);
    }

    internal bool InitialCountdownSelectionSurvivesRefresh()
    {
        ApplyTheme();
        UpdateLayout();
        var survives = FloatingCountdownsButton.IsChecked == true &&
            FloatingNotesButton.IsChecked == false &&
            HeaderControlHasAccessibleContrast(FloatingCountdownsButton) &&
            HeaderControlHasAccessibleContrast(FloatingNotesButton);
        if (!survives)
        {
            var before = new[] { ReadHeaderState(FloatingCountdownsButton), ReadHeaderState(FloatingNotesButton) };
            var reachedSelectedColor = WindowValidationInput.WaitUntil(Dispatcher, () =>
                FloatingCountdownsButton.Template?.FindName("Surface", FloatingCountdownsButton) is Border surface &&
                surface.Background is SolidColorBrush background &&
                FindResource("PinkAccentStrongBrush") is SolidColorBrush target &&
                background.Color == target.Color);
            var after = new[] { ReadHeaderState(FloatingCountdownsButton), ReadHeaderState(FloatingNotesButton) };
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
                ContrastPasses = HeaderControlHasAccessibleContrast(button)
            };
        }
    }


    private FloatingThemeExerciseResult RunDayThemeExercise()
    {
        var snapshot = _desktopWorkspace.Snapshot;
        ApplyTheme();
        UpdateTime(snapshot.UtcNow);
        UpdateLayout();
        var activeTheme = _activeTheme ?? throw new InvalidOperationException("悬浮窗主题尚未初始化。");
        var panelColor = ThemeColor(activeTheme.PanelBrushKey).ToString();
        var plushGlowColor = PlushGlowColor();
        var mascotPresentationMode = MascotPresentationMode();
        var textContrast = HasAccessibleThemeTextContrast;
        var surfacesFollowActiveTheme = SurfacesFollowActiveTheme;
        var plushSceneFollowsActiveTheme = PlushSceneFollowsActiveTheme;
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

    internal void Reanchor()
    {
        var workArea = MonitorWorkAreaResolver.ResolveForWindow(this).WorkAreaDip;
        var minimumLeft = workArea.Left + 12;
        var maximumLeft = Math.Max(minimumLeft, workArea.Right - ActualWidth - 12);
        var minimumTop = workArea.Top + 12;
        var maximumTop = Math.Max(minimumTop, workArea.Bottom - ActualHeight - 12);

        _isApplyingBottomAnchor = true;
        try
        {
            MaxHeight = Math.Max(280, workArea.Height - 24);
            if (_hasManualWindowPlacement)
            {
                Left = Math.Clamp(Left, minimumLeft, maximumLeft);
                Top = Math.Clamp(Top, minimumTop, maximumTop);
                return;
            }

            var left = maximumLeft;
            var top = maximumTop;
            _bottomAnchorPosition = new Point(left, top);
            Left = left;
            Top = top;
        }
        finally
        {
            _isApplyingBottomAnchor = false;
        }
    }

    // #156：非拖动的复位/贴边入口——托盘菜单「复位窗口位置」与悬浮窗 Ctrl+R 共用：
    // 隐藏或最小化时先恢复为可见正常态，再把窗口夹回工作区（Reanchor 只在越界时移动，
    // 未越界的手动位置保持不变；完成任务的窗口收纳不依赖拖无边框窗）。
    internal void ResetPositionIntoWorkArea()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = System.Windows.WindowState.Normal;
        UpdateLayout();
        Reanchor();
    }

    internal void ParkOffscreenForValidation()
    {
        _isApplyingBottomAnchor = true;
        try
        {
            Left = -20_000;
            Top = -20_000;
        }
        finally
        {
            _isApplyingBottomAnchor = false;
        }
    }

    internal void ApplyTheme()
    {
        var nextTheme = FloatingThemePalette.Daylight;
        _activeTheme = nextTheme;
        ApplyQuickAddThemeResources(nextTheme);
        SetActiveThemeBrush("FloatingHeaderTextBrush", nextTheme.HeaderTextBrushKey);
        SetActiveThemeBrush("FloatingHeaderDangerBrush", nextTheme.HeaderDangerBrushKey);
        SetActiveThemeBrush("FloatingHeaderHoverBrush", nextTheme.HeaderHoverBrushKey);
        SetActiveThemeBrush("FloatingHeaderSelectedBrush", nextTheme.HeaderSelectedBrushKey);
        SetActiveThemeBrush("FloatingHeaderSelectedTextBrush", nextTheme.HeaderSelectedTextBrushKey);
        SetActiveThemeBrush("FloatingNoteInputTextBrush", nextTheme.NoteInputTextBrushKey);
        SetActiveThemeBrush("FloatingErrorBrush", nextTheme.ErrorBrushKey);
        SetActiveThemeBrush("FloatingMutedBrush", nextTheme.MutedBrushKey);
        var contrast = ApplicationTheme.HighContrastEnabled;
        var decorationVisibility = contrast ? Visibility.Collapsed : Visibility.Visible;
        FloatingDayRasterScene.Visibility = decorationVisibility;
        FloatingPlushScene.Visibility = decorationVisibility;
        FloatingNotesDecoTextLeft.Visibility = decorationVisibility;
        FloatingNotesDecoText.Visibility = decorationVisibility;
        TaskCard.Background = contrast ? ThemeBrush("CanvasBrush") : Brushes.Transparent;
        TaskCard.BorderBrush = contrast ? ThemeBrush("ControlBorderBrush") : Brushes.Transparent;
        TaskCard.Effect = null;
        FloatingThemeBackdrop.Background = contrast ? ThemeBrush("CanvasBrush") : Brushes.Transparent;
        FloatingThemeBackdrop.BorderBrush = contrast ? ThemeBrush("ControlBorderBrush") : Brushes.Transparent;
        FloatingHeaderSurface.Background = contrast ? ThemeBrush("CanvasBrush") : Brushes.Transparent;
        FloatingHeaderSurface.BorderBrush = contrast ? ThemeBrush("ControlBorderBrush") : Brushes.Transparent;
        FloatingRows.Margin = new Thickness(9, 13, 10, 0);
        SetRasterTranslation(FloatingTitle, -1, -1);
        SetRasterTranslation(FloatingCountdownsButton, 0, 1);
        SetRasterTranslation(FloatingNotesButton, 0, 1);
        SetRasterTranslation(OpenManagerButton, -1, -1);
        SetRasterTranslation(CloseFloatingButton, 0, 0);
        SetRasterTranslation(FloatingIncome, 2, 3);
        FloatingNoteEntry.Background = ThemeBrush(nextTheme.NoteInputBrushKey);
        FloatingNoteEntry.BorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        _floatingPrimaryBrush = ThemeBrush(nextTheme.TextBrushKey);
        _floatingMutedBrush = ThemeBrush(nextTheme.MutedBrushKey);
        _floatingSurfaceBrush = ThemeBrush(nextTheme.SurfaceBrushKey);
        _floatingNoteCardBrush = ThemeBrush(nextTheme.NoteCardBrushKey);
        _floatingBorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        ApplyPlushSceneTheme(nextTheme);
    }

    private Brush ThemeBrush(string resourceKey) =>
        FindResource(resourceKey) as Brush ??
        throw new InvalidOperationException($"找不到悬浮窗主题资源：{resourceKey}");

    private void ApplyQuickAddThemeResources(FloatingThemePalette theme)
    {
        foreach (var scope in QuickAddThemeResourceScopes())
        {
            ApplyQuickAddThemeResources(scope, theme);
        }
    }

    private void ApplyQuickAddThemeResources(
        ResourceDictionary scope,
        FloatingThemePalette theme)
    {
        var bindings = new (string LocalKey, string ThemeKey)[]
        {
            ("CanvasBrush", theme.SurfaceBrushKey),
            ("CardBrush", theme.NoteCardBrushKey),
            ("CardAltBrush", "CardAltBrush"),
            ("NoteBrush", theme.NoteCardBrushKey),
            ("InkBrush", theme.TextBrushKey),
            ("BodyBrush", theme.MutedBrushKey),
            ("MutedBrush", theme.MutedBrushKey),
            ("PeachLineBrush", theme.BorderBrushKey),
            ("ControlBorderBrush", "ControlBorderBrush"),
            ("ActiveBrush", "ActiveBrush"),
            ("ActiveHoverBrush", "ActiveHoverBrush"),
            ("DangerBrush", theme.HeaderDangerBrushKey),
            ("SegmentSelectedBrush", theme.HeaderSelectedBrushKey),
            ("SegmentSelectedForegroundBrush", theme.HeaderSelectedTextBrushKey)
        };

        foreach (var binding in bindings)
        {
            scope[binding.LocalKey] = binding.LocalKey is "ActiveBrush" or "ActiveHoverBrush"
                ? Application.Current.FindResource(binding.LocalKey)
                : ThemeBrush(binding.ThemeKey);
        }
    }

    private void ApplyQuickAddThemeToDatePickerPopup(DatePicker picker)
    {
        if (_activeTheme is not { } theme ||
            PickerTemplateParts.GetCalendar(picker) is not { } calendar)
        {
            return;
        }

        ApplyQuickAddThemeResources(calendar.Resources, theme);
    }

    private IEnumerable<ResourceDictionary> QuickAddThemeResourceScopes()
    {
        yield return Resources;
        yield return FloatingBodyScroller.Resources;
        yield return FloatingNoteSearchSlot.Resources;
        yield return FloatingNoteEntry.Resources;
        yield return FloatingNoteDate.Resources;
        yield return FloatingTags.Resources;
        if (PickerTemplateParts.GetCalendar(FloatingNoteDate) is { } calendar)
            yield return calendar.Resources;
    }

    internal FloatingQuickAddThemeProbeResult ProbeQuickAddThemeResources()
    {
        var originalWorkspace = _workspace;
        var originalTags = _desktopWorkspace.Snapshot.Settings.Tags;
        var originalSelection = FloatingTags.SelectedTagIds.ToArray();
        var originalOffset = FloatingBodyScroller.VerticalOffset;
        var originalExpanded = _searchExpanded;
        var probeTag = new TagRecord(
            $"floating-theme-probe-{Guid.NewGuid():N}",
            "主题探针",
            "#FFD36B");
        try
        {
            ShowWorkspace(WorkspaceKind.Notes);
            SetQuickAddExpanded(true);
            ExpandNoteSearch(focus: false);
            FloatingTags.SetTags(originalTags.Concat([probeTag]).ToArray());
            FloatingTags.SetSelection([probeTag.Id]);
            UpdateLayout();
            WindowValidationInput.Pump(Dispatcher);

            // #232: validate the rendered focus boundary against the application
            // interaction role; matching the local (possibly misbound) alias is insufficient.
            Activate();
            Keyboard.Focus(FloatingNoteText);
            WindowValidationInput.Pump(Dispatcher);
            var focusSurface = FloatingNoteText.Template.FindName("Surface", FloatingNoteText) as Border;
            if (!FloatingNoteText.IsKeyboardFocused ||
                !ReferenceEquals(focusSurface?.BorderBrush, Application.Current.FindResource("ActiveBrush")))
            {
                throw new InvalidOperationException("#232 快速新增实际输入焦点必须使用 Active，而非 Danger。");
            }

            var textBoxes = new[] { FloatingNoteText, FloatingNoteSearch };
            var datePickers = new[] { FloatingNoteDate };
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
                UsesQuickAddResources(FloatingTags, "CanvasBrush", "InkBrush", "PeachLineBrush") &&
                ControlUsesQuickAddSurface(FloatingTags.PopupTrigger);

            var tagPopupOpened = false;
            UIElement? popupRoot = null;
            TextBox? popupSearch = null;
            CheckBox? popupCheckBox = null;
            ScrollViewer? popupScroller = null;
            EventHandler tagPopupOpenedHandler = (_, _) =>
            {
                tagPopupOpened = true;
                popupRoot = FloatingTags.Popup.Child;
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
            FloatingTags.Popup.Opened += tagPopupOpenedHandler;
            try
            {
                FloatingTags.IsPopupOpen = true;
                WindowValidationInput.Pump(Dispatcher);
            }
            finally
            {
                FloatingTags.Popup.Opened -= tagPopupOpenedHandler;
            }
            checks["TagSelectorPopup"] =
                tagPopupOpened &&
                popupRoot is Border popupSurface &&
                BorderUsesQuickAddSurface(popupSurface) &&
                popupSearch is not null &&
                TextBoxUsesQuickAddTheme(popupSearch);
            checks["CheckBox"] = popupCheckBox is not null && CheckBoxUsesQuickAddTheme(popupCheckBox);
            checks["ScrollViewer"] =
                ScrollViewerUsesQuickAddTheme(FloatingBodyScroller) &&
                popupScroller is not null &&
                ScrollViewerUsesQuickAddTheme(popupScroller);
            checks["ScrollBar"] =
                ScrollViewerScrollBarsUseQuickAddTheme(FloatingBodyScroller) &&
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
            _searchExpanded = originalExpanded;
            FloatingTags.IsPopupOpen = false;
            FloatingNoteDate.IsDropDownOpen = false;
            FloatingTags.SetTags(originalTags);
            FloatingTags.SetSelection(originalSelection);
            ShowWorkspace(originalWorkspace);
            FloatingBodyScroller.ScrollToVerticalOffset(originalOffset);
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private bool DatePickerPopupUsesQuickAddTheme(
        DatePicker picker,
        ICollection<FloatingQuickAddThemeProbeDiagnostic> diagnostics)
    {
        picker.BringIntoView();
        WindowValidationInput.Pump(Dispatcher);
        picker.IsDropDownOpen = false;
        picker.ApplyTemplate();
        var trigger = picker.Template.FindName("PART_Button", picker) as Button;
        var owner = Window.GetWindow(picker);
        owner?.Activate();
        owner?.Focus();
        trigger?.Focus();
        WindowValidationInput.Pump(Dispatcher);
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
            WindowValidationInput.Pump(Dispatcher);
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
            WindowValidationInput.Pump(Dispatcher);
        }
    }

    private bool ComboBoxPopupUsesQuickAddTheme(
        ComboBox comboBox,
        ICollection<FloatingQuickAddThemeProbeDiagnostic> diagnostics)
    {
        comboBox.BringIntoView();
        WindowValidationInput.Pump(Dispatcher);
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
        WindowValidationInput.Pump(Dispatcher);
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
            WindowValidationInput.Pump(Dispatcher);
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
            WindowValidationInput.Pump(Dispatcher);
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
                    FloatingTags.Popup.IsOpen,
                    string.Empty,
                    keys);
            }
        }

        AddForEach("TextBox", textBoxes, "CanvasBrush", "InkBrush", "ControlBorderBrush", "ActiveBrush", "CardBrush");
        AddForEach("DatePicker", datePickers, "CanvasBrush", "InkBrush", "ControlBorderBrush");
        AddForEach("ComboBox", comboBoxes, "CanvasBrush", "InkBrush", "ControlBorderBrush");
        AddForEach("TagSelector", [FloatingTags, FloatingTags.PopupTrigger], "CardAltBrush", "InkBrush", "ControlBorderBrush");

        if (!checks["TagSelectorPopup"])
        {
            AddQuickAddThemeDiagnostics(
                diagnostics,
                "TagSelectorPopup",
                popupRoot as FrameworkElement ?? FloatingTags,
                FloatingTags.Popup.IsOpen,
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
                (FrameworkElement?)popupCheckBox ?? FloatingTags,
                FloatingTags.Popup.IsOpen,
                $"CheckBox={(popupCheckBox is null ? "missing" : "present")};" +
                $"Checked={popupCheckBox?.IsChecked?.ToString() ?? "unknown"}",
                "CanvasBrush",
                "BodyBrush",
                "ControlBorderBrush",
                "ActiveBrush");
        }

        AddForEach(
            "ScrollViewer",
            new FrameworkElement?[] { FloatingBodyScroller, popupScroller }.OfType<FrameworkElement>(),
            "ControlBorderBrush",
            "ActiveBrush");
        if (!checks["ScrollBar"])
        {
            foreach (var scroller in new[] { FloatingBodyScroller, popupScroller }.Where(scroller => scroller is not null))
            {
                scroller!.ApplyTemplate();
                var vertical = scroller.Template.FindName("PART_VerticalScrollBar", scroller) as ScrollBar;
                var horizontal = scroller.Template.FindName("PART_HorizontalScrollBar", scroller) as ScrollBar;
                AddQuickAddThemeDiagnostics(
                    diagnostics,
                    "ScrollBar",
                    (FrameworkElement?)vertical ?? (FrameworkElement?)horizontal ?? scroller,
                    FloatingTags.Popup.IsOpen,
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
        var inBodyScrollerScope = QuickAddVisualDescendants<DependencyObject>(FloatingBodyScroller)
            .Any(candidate => ReferenceEquals(candidate, element));
        foreach (var resourceKey in resourceKeys)
        {
            var expected = Resources.Contains(resourceKey) ? Resources[resourceKey] : null;
            var actual = element.TryFindResource(resourceKey);
            diagnostics.Add(new FloatingQuickAddThemeProbeDiagnostic(
                check,
                elementName,
                resourceKey,
                DescribeQuickAddResource(expected),
                DescribeQuickAddResource(actual),
                ReferenceEquals(actual, expected),
                element.Resources.Contains(resourceKey),
                inBodyScrollerScope && FloatingBodyScroller.Resources.Contains(resourceKey),
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
                    ReferenceEquals(textBox, FloatingNoteText) ||
                    ReferenceEquals(textBox, FloatingNoteSearch)
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

    private bool BorderUsesQuickAddSurface(Border border) =>
        Equals(border.Background, border.TryFindResource("CanvasBrush")) &&
        Equals(border.BorderBrush, border.TryFindResource("PeachLineBrush")) &&
        UsesQuickAddResources(border, "CanvasBrush", "PeachLineBrush");

    private bool UsesQuickAddResources(FrameworkElement element, params string[] keys) =>
        keys.All(key =>
            Resources.Contains(key) &&
            ReferenceEquals(element.TryFindResource(key), Resources[key]));

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

    private static void SetRasterTranslation(
        UIElement element,
        double offsetX,
        double offsetY) =>
        element.RenderTransform = new TranslateTransform(offsetX, offsetY);

    private Color ThemeColor(string resourceKey) =>
        ThemeBrush(resourceKey) is SolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"悬浮窗主题资源不是纯色画刷：{resourceKey}");

    private void ApplyPlushSceneTheme(FloatingThemePalette theme)
    {
        DayMascotsImage.Visibility = ApplicationTheme.HighContrastEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private string PlushGlowColor() =>
        ThemeBrush(FloatingThemePalette.Daylight.PlushGlowBrushKey).ToString() ?? string.Empty;

    private string MascotPresentationMode() =>
        DayMascotsImage.Visibility == Visibility.Visible ? "DayRaster" : "Mixed";

    private void SetActiveThemeBrush(string activeResourceKey, string sourceResourceKey) =>
        Resources[activeResourceKey] = ThemeBrush(sourceResourceKey);

    private bool HeaderControlsHaveAccessibleStates()
    {
        var originalCountdownsChecked = FloatingCountdownsButton.IsChecked;
        var originalNotesChecked = FloatingNotesButton.IsChecked;
        try
        {
            // Avoid Refresh here: it reapplies the live clock theme and would hide a boundary regression.
            FloatingCountdownsButton.IsChecked = true;
            FloatingNotesButton.IsChecked = false;
            var countdownState = HeaderControlHasAccessibleContrast(FloatingCountdownsButton);
            var noteState = HeaderControlHasAccessibleContrast(FloatingNotesButton);
            var openManagerState = HeaderControlHasAccessibleContrast(OpenManagerButton);
            var closeState = HeaderControlHasAccessibleContrast(CloseFloatingButton);

            FloatingCountdownsButton.IsChecked = false;
            FloatingNotesButton.IsChecked = true;
            return
                countdownState &&
                noteState &&
                openManagerState &&
                closeState &&
                HeaderControlHasAccessibleContrast(FloatingCountdownsButton) &&
                HeaderControlHasAccessibleContrast(FloatingNotesButton);
        }
        finally
        {
            FloatingCountdownsButton.IsChecked = originalCountdownsChecked;
            FloatingNotesButton.IsChecked = originalNotesChecked;
        }
    }

    private bool HeaderControlHasAccessibleContrast(Control control)
    {
        control.ApplyTemplate();
        var surface = control.Template?.FindName("Surface", control) as Border;
        var background = surface?.Background is SolidColorBrush solidColorBrush && solidColorBrush.Color.A > 0
            ? solidColorBrush
            : FloatingHeaderSurface.Background;
        return ApplicationTheme.MeetsContrast(background, control.Foreground, 4.5);
    }

    private static string BrushColor(Brush brush) =>
        brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color.ToString()
            : string.Empty;

    private static bool IsTransparent(Brush brush) =>
        brush is SolidColorBrush solidColorBrush && solidColorBrush.Color.A == 0;

    private void ConfigureControls()
    {
        FloatingTags.SetTags(_desktopWorkspace.Snapshot.Settings.Tags);
        AutomationProperties.SetName(FloatingCountdownsButton, "显示悬浮倒计时");
        AutomationProperties.SetName(FloatingNotesButton, "显示悬浮便签");
        FocusVisualState.Attach(FloatingCountdownsButton);
        FocusVisualState.Attach(FloatingNotesButton);
        AutomationProperties.SetName(MinimizeToTrayButton, "缩小到系统托盘");
        AutomationProperties.SetName(OpenManagerButton, "打开管理窗口");
        AutomationProperties.SetName(CloseFloatingButton, "退出应用");
        AutomationProperties.SetName(FloatingNoteText, "快速便签内容");
        AutomationProperties.SetName(FloatingNoteDate, "快速便签日期");
        AutomationProperties.SetName(FloatingManageReminderRules, "管理快速便签提醒规则");
        AutomationProperties.SetName(FloatingAddNote, "添加快速便签");
        UiAccessibility.Name(FloatingSearchNotes, "搜索便签");
        UiAccessibility.Name(FloatingAddNoteToggle, "添加便签");
        UiAccessibility.Name(FloatingOpenSettings, "设置");
        UiAccessibility.Name(FloatingNoteSearch, "搜索便签");
        UiAccessibility.Name(FloatingNoteSearchHintClear, "清除搜索");
        FloatingNoteSearch.IsTabStop = false;
        UiAccessibility.MarkLive(FloatingNoteTextError);
        UiAccessibility.MarkLive(FloatingNoteDateError);
        UiAccessibility.MarkLive(FloatingReminderMessage);
        UiAccessibility.MarkLive(FloatingNoteMessage);
    }

    private void WireEvents()
    {
        FloatingIncome.Invoked += (_, _) =>
        {
            _earningsHidden = !_earningsHidden;
            Refresh();
        };
        FloatingCountdownsButton.Click += (_, _) => ShowWorkspace(WorkspaceKind.Countdowns);
        FloatingNotesButton.Click += (_, _) => ShowWorkspace(WorkspaceKind.Notes);
        MinimizeToTrayButton.Click += (_, _) => MinimizeToTrayRequested?.Invoke(this, EventArgs.Empty);
        OpenManagerButton.Click += (_, _) => _manager.Reveal(_workspace);
        CloseFloatingButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        FloatingNoteDate.SelectedDateChanged += (_, _) =>
        {
            if (!_settingDefaultDate)
            {
                _dateDirty = true;
            }

            ClearNoteFieldError(FloatingNoteDateError);
        };
        FloatingNoteDate.CalendarOpened += (_, _) =>
            ApplyQuickAddThemeToDatePickerPopup(FloatingNoteDate);
        FloatingNoteText.TextChanged += (_, _) => ClearNoteFieldError(FloatingNoteTextError);
        FloatingManageReminderRules.Click += (_, _) => OpenFloatingReminderRulesEditor();
        FloatingAddNote.Click += (_, _) => AddNote();
        FloatingAddNoteToggle.Click += (_, _) =>
        {
            SetQuickAddExpanded(!_quickAddExpanded);
        };
        FloatingOpenSettings.Click += (_, _) => _manager.RevealPreferences(_workspace);
        FloatingSearchNotes.Click += (_, _) => ToggleNoteSearch();
        FloatingDeleteUndoButton.Click += (_, _) => CommitFloatingDeleteUndo();
        FloatingDeleteUndoButton.MouseEnter += (_, _) => _deleteUndoCountdown.SetHovering(true);
        FloatingDeleteUndoButton.MouseLeave += (_, _) => _deleteUndoCountdown.SetHovering(false);
        FloatingDeleteUndoButton.GotKeyboardFocus += (_, _) => _deleteUndoCountdown.SetFocused(true);
        FloatingDeleteUndoButton.LostKeyboardFocus += (_, _) => _deleteUndoCountdown.SetFocused(false);
        var floatingUndo = DeleteUndoOffers.For(_desktopWorkspace);
        floatingUndo.Changed += OnFloatingDeleteUndoChanged;
        Closed += (_, _) =>
        {
            _deleteUndoCountdown.Stop();
            floatingUndo.Changed -= OnFloatingDeleteUndoChanged;
        };
        // #149：滚动时把被裁切长卡的日期印记粘在卡片可见区域内（三行始终可见）。
        FloatingBodyScroller.ScrollChanged += (_, _) => UpdateNoteDateStampStickyOffsets();
        FloatingNoteSearch.TextChanged += (_, _) => OnNoteSearchTextChanged();
        FloatingNoteSearchHintTerm.Click += (_, _) => ExpandNoteSearch(focus: true);
        FloatingNoteSearchHintClear.Click += (_, _) => ClearNoteSearch();
        FloatingTags.CreateRequested += (_, name) =>
        {
            var result = _desktopWorkspace.Apply(new CreateTagChange(name, "#F4B6A6"));
            if (result is { Succeeded: true, CreatedTag: { } tag })
            {
                FloatingTags.SelectedTagIds.Add(tag.Id);
                FloatingTags.SetSelection(FloatingTags.SelectedTagIds);
            }
            else
            {
                SetNoteMessage(result.Error);
            }
        };
        SizeChanged += (_, _) => Reanchor();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                // #212：恢复可见时立即渲染一次（隐藏/托盘期间 tick 跳过渲染）。
                _hasBeenVisible = true;
                Reanchor();
                Refresh();
            }
        };
        StateChanged += (_, _) =>
        {
            // #212：从最小化还原到正常态且窗口可见时立即渲染一次。
            if (WindowState == WindowState.Normal && IsVisible)
            {
                Refresh();
            }
        };
        Loaded += (_, _) =>
        {
            var recoveryMessage = _desktopWorkspace.Recovery switch
            {
                SettingsRecoveryOutcome.RestoredWithOriginalPreserved => "已恢复备份；原文件已另存。",
                SettingsRecoveryOutcome.RestoredWithoutOriginal => "已恢复备份；原文件无法读取，未能另存。",
                _ => null
            };
            if (recoveryMessage is not null)
            {
                SettingsRecoveryCompletedStatus.Foreground = (Brush)Application.Current.FindResource("BodyBrush");
                SettingsRecoveryCompletedStatus.Visibility = Visibility.Visible;
                UiAccessibility.MarkLive(SettingsRecoveryCompletedStatus);
                UiAccessibility.Announce(SettingsRecoveryCompletedStatus, recoveryMessage);
            }
            UpdateFloatingBodyScrollBarPlacement();
            Reanchor();
            _trackManualWindowPlacement = true;
        };
        LocationChanged += (_, _) =>
        {
            if (!_trackManualWindowPlacement || _isApplyingBottomAnchor ||
                _bottomAnchorPosition is not { } bottomAnchor)
            {
                return;
            }

            if (Math.Abs(Left - bottomAnchor.X) > 0.5 || Math.Abs(Top - bottomAnchor.Y) > 0.5)
            {
                _hasManualWindowPlacement = true;
            }
        };
    }

    private void UpdateFloatingBodyScrollBarPlacement()
    {
        FloatingBodyScroller.ApplyTemplate();
        if (FloatingBodyScroller.Template.FindName(
                "PART_VerticalScrollBar",
                FloatingBodyScroller) is not ScrollBar verticalScrollBar)
        {
            return;
        }

        verticalScrollBar.RenderTransform = _workspace == WorkspaceKind.Countdowns
            ? new TranslateTransform(-CountdownScrollBarSafeOffset, 0)
            : Transform.Identity;
    }

    private void OnFloatingDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (eventArgs.OriginalSource is DependencyObject source && IsInteractiveDragSource(source))
        {
            return;
        }

        eventArgs.Handled = true;
        var leftBeforeDrag = Left;
        var topBeforeDrag = Top;
        try
        {
            _nativeWindowDragRequestCount++;
            DragMove();
            _nativeWindowDragCompletionCount++;
        }
        catch (InvalidOperationException)
        {
            // Synthetic or canceled gestures cannot establish a completed physical drag.
        }
        finally
        {
            if (Math.Abs(Left - leftBeforeDrag) > 0.5 || Math.Abs(Top - topBeforeDrag) > 0.5)
            {
                _hasManualWindowPlacement = true;
            }

            Reanchor();
        }
    }

    private void Refresh()
    {
        var snapshot = _desktopWorkspace.Snapshot;
        FloatingTags.SetTags(snapshot.Settings.Tags);
        SyncNoteSearchFromFilter();
        RenderRows(snapshot.UtcNow);
        UpdateSearchPresentation();
    }

    private void RenderRows(DateTimeOffset nowUtc)
    {
        // #212：隐藏（托盘）或最小化时 tick 跳过渲染；恢复可见/正常态时立即渲染一次。
        // 首次显示前允许渲染，保证构造期初始内容与未 Show 的验证路径不变。
        if (WindowState == WindowState.Minimized || (_hasBeenVisible && !IsVisible))
        {
            FloatingRowsSkippedRenderCountForValidation++;
            return;
        }

        FloatingRowsRenderPassCountForValidation++;
        var previouslyRenderedWorkspace = _renderedWorkspace;
        _workspaceScrollOffsets[previouslyRenderedWorkspace] = FloatingBodyScroller.VerticalOffset;
        var workspace = _workspace;
        var snapshot = _desktopWorkspace.Snapshot;
        var signature = ComputeRenderSignature(snapshot, workspace, nowUtc);
        if (string.Equals(signature, _floatingRenderSignature, StringComparison.Ordinal) &&
            workspace == _renderedWorkspace)
        {
            // #212：签名不变只就地更新剩余时间数字与底部状态（今日已赚）；
            // 行与便签卡实例、滚动位置与键盘焦点原样保留（DESIGN §10 渲染签名稳定刷新）。
            // 不 bump _floatingRowsRenderVersion：上一次重建排队的视口恢复回调仍然有效，
            // 避免同周期内的就地 tick 把尚未执行的滚动/焦点恢复取消掉。
            FloatingLastRenderWasInPlaceForValidation = true;
            if (workspace == WorkspaceKind.Countdowns)
            {
                UpdateCountdownRowsInPlace(snapshot, nowUtc);
            }

            ApplyFloatingBottomStatus(snapshot, workspace, nowUtc);
            return;
        }

        FloatingLastRenderWasInPlaceForValidation = false;
        var targetScrollOffset = _workspaceScrollOffsets.TryGetValue(workspace, out var savedScrollOffset)
            ? savedScrollOffset
            : 0;
        var focusedNoteTag = previouslyRenderedWorkspace == workspace
            ? FocusedFloatingNoteTag()
            : null;
        var renderVersion = ++_floatingRowsRenderVersion;

        FloatingRows.Children.Clear();
        ClearNoteDateStampStickyEntries();
        if (workspace == WorkspaceKind.Countdowns)
        {
            var records = ResolveFloatingCountdownRecords(snapshot, nowUtc);
            var visibleCountdownIds = records
                .Select(static record => record.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var staleId in _lastCountdownSeconds.Keys
                         .Where(id => !visibleCountdownIds.Contains(id))
                         .ToArray())
            {
                _lastCountdownSeconds.Remove(staleId);
            }

            FloatingCountdownCount = records.Count(record => record.Id != "__workday_end__");
            FloatingCountdownTexts = records
                .Where(record => record.Id != "__workday_end__")
                .Select(record => CountdownFormatting.FormatRemaining(
                    record.TargetUtc,
                    nowUtc,
                    snapshot.Settings.Preferences.DisplayPrecision))
                .ToArray();
            _countdownRowElements.Clear();
            var countdownRows = BuildWarmPaperCountdownRows(
                records,
                nowUtc,
                snapshot.Settings.Preferences.DisplayPrecision);
            for (var index = 0; index < countdownRows.Count; index++)
            {
                _countdownRowElements[records[index].Id] = countdownRows[index];
                FloatingRows.Children.Add(countdownRows[index]);
            }

            if (records.Count == 0)
            {
                FloatingRows.Children.Add(EmptyText(CountdownEmptyStateText(snapshot.Filter)));
                if (CountdownConstraintsActive(snapshot.Filter))
                {
                    FloatingRows.Children.Add(BuildCountdownConstrainedEmptyStateAction());
                }
            }
        }
        else
        {
            var notes = snapshot.VisibleActiveNotes;
            PruneNoteCardErrors(notes);
            for (var index = 0; index < notes.Count; index++)
            {
                FloatingRows.Children.Add(BuildCalendarIndexNoteCard(notes[index], nowUtc, index));
            }

            if (notes.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.Filter.NoteSearch))
                {
                    FloatingRows.Children.Add(BuildNoteSearchEmptyState());
                }
                else if (NoteConstraintsActive(snapshot.Filter))
                {
                    // #149：筛选约束空态提供可执行下一步「清除筛选」；搜索无匹配仍走「清除搜索」。
                    // 消息与按钮都是 FloatingRows 直接子级，保持空态消息直读语义。
                    FloatingRows.Children.Add(EmptyText(NoteEmptyStateText(snapshot.Filter)));
                    FloatingRows.Children.Add(BuildNoteConstrainedEmptyStateAction());
                }
                else
                {
                    FloatingRows.Children.Add(EmptyText(NoteEmptyStateText(snapshot.Filter)));
                }
            }
            else
            {
                // #149：列表视口底部稳定内边距，末卡不视觉顶住底部命令区（DESIGN §10）。
                // 用普通 FrameworkElement 而非 Border：基线管线以 rows 内 Border 数计卡。
                FloatingRows.Children.Add(new FrameworkElement
                {
                    Height = FloatingNotesListBottomPadding,
                    IsHitTestVisible = false,
                    Focusable = false
                });
            }
        }

        ApplyFloatingBottomStatus(snapshot, workspace, nowUtc);
        _renderedWorkspace = workspace;
        RestoreFloatingViewportAfterRender(
            workspace,
            targetScrollOffset,
            focusedNoteTag,
            renderVersion);
        _floatingRenderSignature = signature;
        FloatingRowsRebuildGenerationForValidation++;
    }

    private List<CountdownRecord> ResolveFloatingCountdownRecords(
        DesktopWorkspaceSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        var records = snapshot.VisibleCountdowns
            .Where(record => record.TargetUtc > nowUtc)
            .ToList();
        if (!snapshot.HasFilterConstraints &&
            _calendar.GetWorkdayEndCountdown(nowUtc, snapshot.Settings.Preferences) is { } workdayEnd)
        {
            records.Insert(0, workdayEnd);
        }

        return records;
    }

    // #212：签名不变的就地更新——逐条重建行内剩余时间分格（在行容器内原位替换，
    // 行实例保留），秒位变化仍按 #198 执行 0.6→1 fade；底部状态由调用方统一 Apply。
    private void UpdateCountdownRowsInPlace(DesktopWorkspaceSnapshot snapshot, DateTimeOffset nowUtc)
    {
        var precision = snapshot.Settings.Preferences.DisplayPrecision;
        var records = ResolveFloatingCountdownRecords(snapshot, nowUtc);
        FloatingCountdownCount = records.Count(record => record.Id != "__workday_end__");
        FloatingCountdownTexts = records
            .Where(record => record.Id != "__workday_end__")
            .Select(record => CountdownFormatting.FormatRemaining(record.TargetUtc, nowUtc, precision))
            .ToArray();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (!_countdownRowElements.TryGetValue(record.Id, out var row) ||
                FindRemainingBlocksCluster(row) is not { } cluster ||
                VisualTreeHelper.GetParent(cluster) is not Grid host)
            {
                // 签名一致时行必然在册；防御性跳过，下一次签名变化会走整表重建。
                continue;
            }

            var remaining = CountdownFormatting.FormatRemaining(record.TargetUtc, nowUtc, precision);
            var fadeSeconds = TrackSecondsChange(record.Id, remaining);
            var fresh = BuildRemainingBlocks(remaining, isPrimary: index == 0, fadeSeconds: fadeSeconds);
            var childIndex = host.Children.IndexOf(cluster);
            host.Children.RemoveAt(childIndex);
            Grid.SetColumn(fresh, 1);
            host.Children.Insert(childIndex, fresh);
        }
    }

    private static Grid? FindRemainingBlocksCluster(DependencyObject element)
    {
        if (element is Grid grid &&
            grid.Tag is string tag &&
            string.Equals(tag, RemainingBlocksTag, StringComparison.Ordinal))
        {
            return grid;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            if (FindRemainingBlocksCluster(VisualTreeHelper.GetChild(element, index)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // #212：渲染签名只比较语义输入；剩余时间的数值变化（同一结构、同一精度下的下一秒）
    // 不改变签名，由就地路径更新数字。
    private string ComputeRenderSignature(
        DesktopWorkspaceSnapshot snapshot,
        WorkspaceKind workspace,
        DateTimeOffset nowUtc)
    {
        var filter = snapshot.Filter;
        var filterFingerprint = string.Join("\u001e", new[]
        {
            string.Join(",", filter.SelectedTagIds),
            filter.MatchMode.ToString(),
            filter.UntaggedOnly.ToString(),
            filter.CountdownStatus.ToString(),
            filter.NoteStatus.ToString(),
            filter.NoteSearch,
            filter.NoteDate ?? string.Empty,
            filter.UndatedNotesOnly.ToString()
        });
        var entries = new List<string>();
        if (workspace == WorkspaceKind.Countdowns)
        {
            var precision = snapshot.Settings.Preferences.DisplayPrecision;
            foreach (var record in ResolveFloatingCountdownRecords(snapshot, nowUtc))
            {
                entries.Add(string.Join("\u001e", new[]
                {
                    record.Id,
                    record.Name,
                    record.TargetUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
                    NormalizeFloatingRemainingStructure(CountdownFormatting.FormatRemaining(
                        record.TargetUtc,
                        nowUtc,
                        precision))
                }));
            }
        }
        else
        {
            foreach (var note in snapshot.VisibleActiveNotes)
            {
                var nextReminder = ReminderSchedule.GetNextDueUtc(snapshot.Settings, note, nowUtc);
                entries.Add(string.Join("\u001e", new[]
                {
                    note.Id,
                    note.Text,
                    note.Date ?? string.Empty,
                    note.IsCompleted.ToString(),
                    string.Join(",", note.TagIds),
                    nextReminder?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "-",
                    _noteCardErrors.TryGetValue(note.Id, out var error) ? error : string.Empty
                }));
            }
        }

        return BuildFloatingRenderSignature(
            workspace,
            snapshot.Settings.Preferences.DisplayPrecision,
            filterFingerprint,
            entries);
    }

    internal static string BuildFloatingRenderSignature(
        WorkspaceKind workspace,
        string displayPrecision,
        string filterFingerprint,
        IEnumerable<string> entryFingerprints) =>
        string.Concat(
            workspace.ToString(),
            "\u001f",
            displayPrecision,
            "\u001f",
            filterFingerprint,
            "\u001f",
            string.Join("\u001f", entryFingerprints));

    // 剩余时间签名只保留结构与单位：数字串折叠为单个 0，位数变化由行内分格重建吸收。
    internal static string NormalizeFloatingRemainingStructure(string remaining)
    {
        var builder = new StringBuilder(remaining.Length);
        var inDigits = false;
        foreach (var character in remaining)
        {
            if (char.IsDigit(character))
            {
                if (!inDigits)
                {
                    builder.Append('0');
                    inDigits = true;
                }

                continue;
            }

            inDigits = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    private void ApplyFloatingBottomStatus(
        DesktopWorkspaceSnapshot snapshot,
        WorkspaceKind workspace,
        DateTimeOffset nowUtc)
    {
        var preferences = snapshot.Settings.Preferences;
        var encouragementText = workspace == WorkspaceKind.Notes
            ? FloatingBottomStatus.NotesEncouragementText
            : FloatingBottomStatus.CountdownEncouragementText;
        FloatingBottomStatus.Apply(
            FloatingIncome,
            FloatingBottomStatus.Resolve(
                _calendar.GetEarnings(nowUtc, preferences),
                preferences.MonthlySalary,
                _calendar.State,
                _calendar.StatusText,
                encouragementText,
                _earningsHidden),
            _floatingMutedBrush,
            ThemeBrush("DecoTextSoftBrush"),
            (FontFamily)FindResource("HandwrittenDecoFontFamily"));
    }

    private TextBlock EmptyText(string text) => new()
    {
        Text = text,
        FontSize = (double)FindResource("TypeCaptionFontSize"),
        Margin = new Thickness(2, 8, 2, 8),
        HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = _floatingMutedBrush
    };

    internal static bool CountdownConstraintsActive(DesktopWorkspaceFilter filter) =>
        filter.SelectedTagIds.Count > 0 ||
        filter.UntaggedOnly ||
        filter.CountdownStatus != CountdownStatusFilter.All;

    internal static bool NoteConstraintsActive(DesktopWorkspaceFilter filter) =>
        filter.SelectedTagIds.Count > 0 ||
        filter.UntaggedOnly ||
        filter.NoteStatus != NoteStatusFilter.All ||
        filter.NoteDate is not null ||
        filter.UndatedNotesOnly;

    internal static string CountdownEmptyStateText(DesktopWorkspaceFilter filter) =>
        CountdownConstraintsActive(filter) ? "没有匹配的倒计时。" : "没有未到期的倒计时。";

    internal static string NoteEmptyStateText(DesktopWorkspaceFilter filter) =>
        NoteConstraintsActive(filter) ? "没有匹配的便签。" : "还没有待办便签。";

    private FrameworkElement BuildNoteSearchEmptyState()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(2, 8, 2, 8)
        };
        panel.Children.Add(new TextBlock
        {
            FontSize = (double)FindResource("TypeCaptionFontSize"),
            Text = "没有匹配的便签。",
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = _floatingMutedBrush
        });
        var clear = UiAccessibility.Name(
            new Button
            {
                Content = "清除搜索",
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = TryFindResource("SecondaryButton") as Style
            },
            "清除搜索");
        clear.Click += (_, _) => ClearNoteSearch();
        panel.Children.Add(clear);
        return panel;
    }

    /// <summary>
    ///     #149：筛选约束（标签 / 无标签 / 状态 / 日期）导致的空态提供可执行下一步「清除筛选」，
    ///     一键还原到无约束视图；搜索词不受影响，语义与「清除搜索」互斥出现。
    /// </summary>
    private FrameworkElement BuildNoteConstrainedEmptyStateAction()
    {
        var clear = UiAccessibility.Name(
            new Button
            {
                Content = "清除筛选",
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = TryFindResource("SecondaryButton") as Style
            },
            "清除便签筛选");
        clear.Click += (_, _) => ClearNoteFilterConstraints();
        return clear;
    }

    private FrameworkElement BuildCountdownConstrainedEmptyStateAction()
    {
        var clear = UiAccessibility.Name(
            new Button
            {
                Content = "清除筛选",
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = TryFindResource("SecondaryButton") as Style
            },
            "清除倒计时筛选");
        clear.Click += (_, _) =>
        {
            var filter = _desktopWorkspace.Snapshot.Filter;
            _desktopWorkspace.ApplyFilter(filter with
            {
                SelectedTagIds = Array.Empty<string>(),
                UntaggedOnly = false,
                CountdownStatus = CountdownStatusFilter.All
            });
        };
        return clear;
    }

    private void ClearNoteFilterConstraints()
    {
        var filter = _desktopWorkspace.Snapshot.Filter;
        _desktopWorkspace.ApplyFilter(filter with
        {
            SelectedTagIds = Array.Empty<string>(),
            UntaggedOnly = false,
            NoteStatus = NoteStatusFilter.All,
            NoteDate = null,
            UndatedNotesOnly = false
        });
    }

    private void OnFloatingDeleteUndoChanged(object? sender, EventArgs eventArgs)
    {
        var slot = DeleteUndoOffers.For(_desktopWorkspace);
        if (slot.Surface == DeleteUndoSurface.Floating && slot.Payload is { } undo)
        {
            _retainFloatingUndoMessage = false;
            PresentFloatingDeleteUndo(undo);
            return;
        }

        _deleteUndoCountdown.Stop();
        FloatingDeleteUndoButton.Visibility = Visibility.Collapsed;
        if (!_retainFloatingUndoMessage)
        {
            FloatingDeleteUndoLine.Visibility = Visibility.Collapsed;
            FloatingDeleteUndoText.Text = string.Empty;
        }

        _retainFloatingUndoMessage = false;
    }

    private void PresentFloatingDeleteUndo(DeletedRecordUndo undo)
    {
        FloatingDeleteUndoText.Text = $"已删除{undo.KindName}「{undo.Description}」。";
        AutomationProperties.SetName(
            FloatingDeleteUndoButton,
            $"撤销删除{undo.KindName}「{undo.Description}」");
        FloatingDeleteUndoButton.Visibility = Visibility.Visible;
        FloatingDeleteUndoLine.Visibility = Visibility.Visible;
        _deleteUndoCountdown.Restart();
    }

    private void CommitFloatingDeleteUndo()
    {
        var slot = DeleteUndoOffers.For(_desktopWorkspace);
        if (slot.Surface != DeleteUndoSurface.Floating || slot.Payload is not { } undo)
        {
            return;
        }

        var result = _desktopWorkspace.Apply(undo.ToChange());
        if (!result.Succeeded)
        {
            FloatingDeleteUndoText.Text = $"撤销失败：{result.Error}";
            FloatingDeleteUndoLine.Visibility = Visibility.Visible;
            return;
        }

        FloatingDeleteUndoText.Text = $"已撤销删除{undo.KindName}「{undo.Description}」。";
        _retainFloatingUndoMessage = true;
        slot.Clear();
    }

    private void ExpireOwnedFloatingDeleteUndo()
    {
        var slot = DeleteUndoOffers.For(_desktopWorkspace);
        if (slot.Surface != DeleteUndoSurface.Floating)
        {
            return;
        }

        _retainFloatingUndoMessage = true;
        slot.Clear();
    }

    internal void AdvanceDeleteUndoForValidation(TimeSpan elapsed) =>
        _deleteUndoCountdown.Advance(elapsed);

    internal DeletedRecordUndo? DeleteUndoForValidation
    {
        get
        {
            var slot = DeleteUndoOffers.For(_desktopWorkspace);
            return slot.Surface == DeleteUndoSurface.Floating ? slot.Payload : null;
        }
    }

    internal void ExpireDeleteUndoForValidation() => ExpireOwnedFloatingDeleteUndo();

    internal void DismissDeleteUndoForValidation()
    {
        _retainFloatingUndoMessage = false;
        var slot = DeleteUndoOffers.For(_desktopWorkspace);
        if (slot.Surface == DeleteUndoSurface.Floating)
        {
            slot.Clear();
            return;
        }

        FloatingDeleteUndoButton.Visibility = Visibility.Collapsed;
        FloatingDeleteUndoLine.Visibility = Visibility.Collapsed;
        FloatingDeleteUndoText.Text = string.Empty;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        var modifiers = MainShortcutInput.ResolveModifiers(args);
        if (modifiers == ModifierKeys.Control &&
            args.Key == Key.Z &&
            DeleteUndoOffers.For(_desktopWorkspace).Surface == DeleteUndoSurface.Floating)
        {
            CommitFloatingDeleteUndo();
            args.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && args.Key == Key.R)
        {
            // #156：Ctrl+R 是非拖动的窗口复位入口（与托盘「复位窗口位置」共用实现）；
            // 弹层或日期下拉打开时不抢动作。
            if (FloatingTags.IsPopupOpen || FloatingNoteDate.IsDropDownOpen)
            {
                return;
            }

            ResetPositionIntoWorkArea();
            args.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && args.Key == Key.F)
        {
            if (FloatingTags.IsPopupOpen || FloatingNoteDate.IsDropDownOpen)
            {
                return;
            }

            ExpandNoteSearchFromShortcut();
            args.Handled = true;
            return;
        }

        if (args.Key != Key.Escape ||
            modifiers != ModifierKeys.None ||
            !_searchExpanded && string.IsNullOrWhiteSpace(_desktopWorkspace.Snapshot.Filter.NoteSearch) ||
            FloatingTags.IsPopupOpen ||
            FloatingNoteDate.IsDropDownOpen)
        {
            return;
        }

        ClearNoteSearch();
        args.Handled = true;
    }

    private void ToggleNoteSearch()
    {
        if (_searchExpanded)
        {
            _searchExpanded = false;
            UpdateSearchPresentation();
            return;
        }

        ExpandNoteSearch(focus: true);
    }

    private void ExpandNoteSearchFromShortcut()
    {
        if (_workspace != WorkspaceKind.Notes)
        {
            ShowWorkspace(WorkspaceKind.Notes);
        }

        ExpandNoteSearch(focus: true);
    }

    private void ExpandNoteSearch(bool focus)
    {
        _searchExpanded = true;
        UpdateSearchPresentation();
        if (!focus)
        {
            return;
        }

        WindowValidationInput.Pump(Dispatcher);
        Keyboard.Focus(FloatingNoteSearch);
    }

    private void ClearNoteSearch()
    {
        _searchExpanded = false;
        if (FloatingNoteSearch.Text.Length > 0)
        {
            FloatingNoteSearch.Clear();
        }
        else
        {
            ApplyNoteSearchTerm(string.Empty);
        }

        UpdateSearchPresentation();
    }

    private void OnNoteSearchTextChanged()
    {
        ApplyNoteSearchTerm(FloatingNoteSearch.Text);
        UpdateSearchPresentation();
    }

    private void ApplyNoteSearchTerm(string term)
    {
        if (_syncingSearch)
        {
            return;
        }

        var current = _desktopWorkspace.Snapshot.Filter;
        if (string.Equals(current.NoteSearch, term, StringComparison.Ordinal))
        {
            return;
        }

        _desktopWorkspace.ApplyFilter(current with { NoteSearch = term });
    }

    private void SyncNoteSearchFromFilter()
    {
        var term = _desktopWorkspace.Snapshot.Filter.NoteSearch ?? string.Empty;
        if (string.Equals(FloatingNoteSearch.Text, term, StringComparison.Ordinal))
        {
            return;
        }

        _syncingSearch = true;
        try
        {
            FloatingNoteSearch.Text = term;
        }
        finally
        {
            _syncingSearch = false;
        }

        if (string.IsNullOrWhiteSpace(term) && !_searchExpanded)
        {
            _searchExpanded = false;
        }
    }

    private void UpdateSearchPresentation()
    {
        var searchSlotWasVisible = FloatingNoteSearchSlot.Visibility == Visibility.Visible;
        var keepBottomAnchored = _workspace == WorkspaceKind.Notes &&
            (FloatingBodyScroller.ScrollableHeight <= 0.5 ||
             FloatingBodyScroller.VerticalOffset >= FloatingBodyScroller.ScrollableHeight - 0.5);
        UpdateSearchPresentationCore();
        var searchSlotVisibilityChanged =
            (FloatingNoteSearchSlot.Visibility == Visibility.Visible) != searchSlotWasVisible;
        if (!searchSlotVisibilityChanged || !keepBottomAnchored)
        {
            return;
        }

        // 搜索槽展开/收起会改变列表视口高度；原本吸底时保持吸底，底部“今日已赚/鼓励”状态行不被裁切。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_workspace != WorkspaceKind.Notes)
            {
                return;
            }

            FloatingBodyScroller.UpdateLayout();
            FloatingBodyScroller.ScrollToBottom();
        }), DispatcherPriority.Loaded);
    }

    private void UpdateSearchPresentationCore()
    {
        var notesWorkspace = _workspace == WorkspaceKind.Notes;
        FloatingSearchNotes.Visibility = notesWorkspace ? Visibility.Visible : Visibility.Collapsed;
        FloatingSearchNotes.IsTabStop = notesWorkspace;
        var term = FloatingNoteSearch.Text;
        if (!notesWorkspace)
        {
            FloatingNoteSearchSlot.Visibility = Visibility.Collapsed;
            FloatingNoteSearchInputRow.Visibility = Visibility.Collapsed;
            FloatingNoteSearchHint.Visibility = Visibility.Collapsed;
            FloatingNoteSearchPlaceholder.Visibility = Visibility.Collapsed;
            FloatingNoteSearch.IsTabStop = false;
            ReleaseCollapsedSearchFocus();
            return;
        }

        if (_searchExpanded)
        {
            FloatingNoteSearchSlot.Visibility = Visibility.Visible;
            FloatingNoteSearchInputRow.Visibility = Visibility.Visible;
            FloatingNoteSearchHint.Visibility = Visibility.Collapsed;
            FloatingNoteSearchPlaceholder.Visibility =
                string.IsNullOrEmpty(term) ? Visibility.Visible : Visibility.Collapsed;
            FloatingNoteSearch.IsTabStop = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            FloatingNoteSearchSlot.Visibility = Visibility.Visible;
            FloatingNoteSearchInputRow.Visibility = Visibility.Collapsed;
            FloatingNoteSearchHint.Visibility = Visibility.Visible;
            FloatingNoteSearchPlaceholder.Visibility = Visibility.Collapsed;
            FloatingNoteSearchHintTerm.Content = $"搜索:{term}";
            UiAccessibility.Name(FloatingNoteSearchHintTerm, $"展开搜索：{term}");
            FloatingNoteSearch.IsTabStop = false;
            ReleaseCollapsedSearchFocus();
            return;
        }

        FloatingNoteSearchSlot.Visibility = Visibility.Collapsed;
        FloatingNoteSearchInputRow.Visibility = Visibility.Collapsed;
        FloatingNoteSearchHint.Visibility = Visibility.Collapsed;
        FloatingNoteSearchPlaceholder.Visibility = Visibility.Collapsed;
        FloatingNoteSearch.IsTabStop = false;
        ReleaseCollapsedSearchFocus();
    }

    private void ReleaseCollapsedSearchFocus()
    {
        if (!FloatingNoteSearch.IsKeyboardFocusWithin)
        {
            return;
        }

        var fallback = FloatingSearchNotes.IsVisible
            ? (IInputElement)FloatingSearchNotes
            : FloatingNotesButton;
        Keyboard.Focus(fallback);
    }

    private void AddNote()
    {
        ClearNoteFieldErrors();
        if (string.IsNullOrWhiteSpace(FloatingNoteText.Text))
        {
            ShowNoteValidationError(
                FloatingNoteTextError,
                "请先写下便签内容。",
                FloatingNoteText);
            return;
        }

        if (FloatingNoteDate.SelectedDate is null)
        {
            ShowNoteValidationError(
                FloatingNoteDateError,
                "请选择便签日期。",
                FloatingNoteDate);
            return;
        }

        var result = _desktopWorkspace.Apply(new SaveNoteChange(
            null,
            FloatingNoteText.Text,
            FloatingNoteDate.SelectedDate,
            false,
            FloatingTags.SelectedTagIds.ToArray(),
            _floatingReminderRules));
        if (!result.Succeeded)
        {
            SetNoteMessage($"保存失败：{result.Error}");
            if (result.ReminderRuleErrorTarget != ReminderRuleEditingErrorTarget.None)
            {
                OpenFloatingReminderRulesEditor(
                    result.ReminderRuleErrorTarget,
                    result.Error);
            }
            return;
        }

        FloatingNoteText.Clear();
        _dateDirty = false;
        _settingDefaultDate = true;
        FloatingNoteDate.SelectedDate = _desktopWorkspace.Snapshot.BeijingNow.Date;
        _settingDefaultDate = false;
        _floatingReminderRules = Array.Empty<ReminderRule>();
        UpdateFloatingReminderRulesSummary();
        ClearNoteFieldErrors();
        SetNoteMessage(string.Empty);
        SetQuickAddExpanded(false);
    }

    private void OpenFloatingReminderRulesEditor(
        ReminderRuleEditingErrorTarget initialErrorTarget = ReminderRuleEditingErrorTarget.None,
        string initialError = "")
    {
        var editor = new ReminderRulesEditorWindow(
            _floatingReminderRules,
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

        _floatingReminderRules = Array.AsReadOnly(editor.ResultRules.ToArray());
        UpdateFloatingReminderRulesSummary();
        ClearNoteFieldError(FloatingReminderMessage);
    }

    private void UpdateFloatingReminderRulesSummary()
    {
        FloatingReminderRulesSummary.Text = _floatingReminderRules.Count == 0
            ? "不提醒"
            : $"已配置 {_floatingReminderRules.Count} 条提醒规则";
        AutomationProperties.SetName(
            FloatingReminderRulesSummary,
            $"快速便签提醒规则摘要：{FloatingReminderRulesSummary.Text}");
    }

    private void SetNoteMessage(string text) =>
        UiAccessibility.Announce(FloatingNoteMessage, text);

    private void ClearNoteFieldErrors()
    {
        ClearNoteFieldError(FloatingNoteTextError);
        ClearNoteFieldError(FloatingNoteDateError);
        ClearNoteFieldError(FloatingReminderMessage);
    }

    private void ClearNoteFieldError(TextBlock field)
    {
        var previousMessage = field.Text;
        SetNoteFieldError(field, string.Empty);
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
            string.Equals(FloatingNoteMessage.Text, previousMessage, StringComparison.Ordinal))
        {
            SetNoteMessage(string.Empty);
        }
    }

    private void ShowNoteValidationError(TextBlock field, string message, Control focusTarget)
    {
        SetNoteFieldError(field, message);
        SetNoteMessage(message);
        AutomationProperties.SetHelpText(focusTarget, message);
        focusTarget.Focus();
    }

    private static void SetNoteFieldError(TextBlock field, string message)
    {
        UiAccessibility.Announce(field, message, block =>
        {
            AutomationProperties.SetHelpText(block, message);
            block.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        });
    }

    private IEnumerable<Control> NoteValidationControls()
    {
        yield return FloatingNoteText;
        yield return FloatingNoteDate;
        yield return FloatingManageReminderRules;
    }

    private (string NoteId, string? Role)? FocusedFloatingNoteTag()
    {
        if (Keyboard.FocusedElement is not FrameworkElement focused ||
            focused.Tag is not string noteId ||
            string.IsNullOrWhiteSpace(noteId) ||
            !IsVisualDescendantOf(focused, FloatingRows))
        {
            return null;
        }

        return (noteId, focused.GetValue(NoteControlRoleProperty) as string);
    }

    private void RestoreFloatingViewportAfterRender(
        WorkspaceKind workspace,
        double targetScrollOffset,
        (string NoteId, string? Role)? focusedNoteTag,
        int renderVersion)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (renderVersion != _floatingRowsRenderVersion ||
                workspace != _workspace ||
                workspace != _renderedWorkspace)
            {
                return;
            }

            FloatingBodyScroller.UpdateLayout();
            if (focusedNoteTag is { } focusTag &&
                FindFocusableFloatingNoteControl(focusTag.NoteId, focusTag.Role) is { } focusTarget)
            {
                focusTarget.Focus();
            }

            var restoredOffset = ClampScrollOffset(targetScrollOffset);
            FloatingBodyScroller.ScrollToVerticalOffset(restoredOffset);
            // #149：滚动位置恢复后同步日期印记粘性位移（ScrollToVerticalOffset 幅度相同时不触发 ScrollChanged）。
            UpdateNoteDateStampStickyOffsets();
            _workspaceScrollOffsets[workspace] = FloatingBodyScroller.VerticalOffset;
        }), DispatcherPriority.Loaded);
    }

    private double ClampScrollOffset(double offset)
    {
        if (double.IsNaN(offset) || double.IsInfinity(offset) || offset <= 0 ||
            double.IsNaN(FloatingBodyScroller.ScrollableHeight) ||
            double.IsInfinity(FloatingBodyScroller.ScrollableHeight) ||
            FloatingBodyScroller.ScrollableHeight <= 0)
        {
            return 0;
        }

        return Math.Min(offset, FloatingBodyScroller.ScrollableHeight);
    }

    // #212：重建后焦点按「记录 ID + 控件角色」恢复；角色控件缺失时回退到同 ID 首个可聚焦控件。
    private FrameworkElement? FindFocusableFloatingNoteControl(string noteId, string? role)
    {
        foreach (UIElement row in FloatingRows.Children)
        {
            if (FindFocusableFloatingNoteControl(row, noteId, role) is { } control)
            {
                return control;
            }
        }

        if (role is null)
        {
            return null;
        }

        foreach (UIElement row in FloatingRows.Children)
        {
            if (FindFocusableFloatingNoteControl(row, noteId, null) is { } fallback)
            {
                return fallback;
            }
        }

        return null;
    }

    private static FrameworkElement? FindFocusableFloatingNoteControl(
        DependencyObject element,
        string noteId,
        string? role)
    {
        if (element is FrameworkElement candidate &&
            candidate.Tag is string candidateTag &&
            string.Equals(candidateTag, noteId, StringComparison.Ordinal) &&
            candidate.Focusable &&
            candidate.IsEnabled &&
            candidate.IsVisible &&
            (role is null ||
             string.Equals(
                 candidate.GetValue(NoteControlRoleProperty) as string,
                 role,
                 StringComparison.Ordinal)))
        {
            return candidate;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            if (FindFocusableFloatingNoteControl(
                    VisualTreeHelper.GetChild(element, index),
                    noteId,
                    role) is { } child)
            {
                return child;
            }
        }

        return null;
    }

    private void PruneNoteCardErrors(IEnumerable<NoteRecord> notes)
    {
        if (_noteCardErrors.Count == 0)
        {
            return;
        }

        var noteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var note in notes)
        {
            noteIds.Add(note.Id);
        }

        foreach (var noteId in _noteCardErrors.Keys.ToArray())
        {
            if (!noteIds.Contains(noteId))
            {
                var removedError = _noteCardErrors[noteId];
                _noteCardErrors.Remove(noteId);
                if (string.Equals(FloatingNoteMessage.Text, removedError, StringComparison.Ordinal))
                {
                    SetNoteMessage(string.Empty);
                }
            }
        }
    }

    private void LoadAssets(string assetRoot)
    {
        _assetsLoaded = true;
        var plushRoot = Path.Combine(assetRoot, "assets", "floating-plush");
        DayOuterShellImage.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "component-day-outer-shell.png"));
        DayHeaderImage.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "component-day-header-blank.png"));
        _dayCardRaster = MainWindow.LoadImage(
            Path.Combine(plushRoot, "component-day-card-blank.png"));
        DayMascotsImage.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "component-day-mascots.png"));
    }

    private static string AssetName(ImageSource? source) =>
        source is BitmapImage { UriSource: { } uri }
            ? Path.GetFileName(uri.LocalPath)
            : string.Empty;

    private static bool IsVisualDescendantOf(DependencyObject control, DependencyObject ancestor)
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

    private bool IsInteractiveDragSource(DependencyObject source)
    {
        for (DependencyObject? current = source;
             current is not null && !ReferenceEquals(current, TaskWrap);
             current = InputParent(current))
        {
            if (current is Control || current is TagSelector)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? InputParent(DependencyObject current) => current switch
    {
        Visual => VisualTreeHelper.GetParent(current),
        FrameworkContentElement content => content.Parent,
        _ => LogicalTreeHelper.GetParent(current)
    };

    private static MouseButtonEventArgs CreateMouseLeftButtonDownGesture() =>
        new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        };
}

internal enum FloatingScrollBarThumbState
{
    Normal,
    Hover,
    Drag
}

internal sealed class FloatingScrollBarThumb : Thumb
{
    private static readonly DependencyPropertyKey VisualWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(VisualWidth),
            typeof(double),
            typeof(FloatingScrollBarThumb),
            new FrameworkPropertyMetadata(4d));

    private static readonly DependencyPropertyKey ThumbOpacityPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ThumbOpacity),
            typeof(double),
            typeof(FloatingScrollBarThumb),
            new FrameworkPropertyMetadata(0.52d));

    public static readonly DependencyProperty VisualWidthProperty =
        VisualWidthPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ThumbOpacityProperty =
        ThumbOpacityPropertyKey.DependencyProperty;

    public double VisualWidth => (double)GetValue(VisualWidthProperty);

    public double ThumbOpacity => (double)GetValue(ThumbOpacityProperty);

    protected override void OnMouseEnter(MouseEventArgs eventArgs)
    {
        base.OnMouseEnter(eventArgs);
        if (!IsDragging)
        {
            ApplyVisualState(FloatingScrollBarThumbState.Hover);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        if (!IsDragging)
        {
            ApplyVisualState(FloatingScrollBarThumbState.Normal);
        }
    }

    protected override void OnDraggingChanged(DependencyPropertyChangedEventArgs eventArgs)
    {
        base.OnDraggingChanged(eventArgs);
        ApplyVisualState(IsDragging
            ? FloatingScrollBarThumbState.Drag
            : IsMouseOver
                ? FloatingScrollBarThumbState.Hover
                : FloatingScrollBarThumbState.Normal);
    }

    internal void ApplyVisualStateForValidation(FloatingScrollBarThumbState state) =>
        ApplyVisualState(state);

    private void ApplyVisualState(FloatingScrollBarThumbState state)
    {
        var (width, opacity) = state switch
        {
            FloatingScrollBarThumbState.Normal => (4d, 0.52d),
            FloatingScrollBarThumbState.Hover => (4d, 0.78d),
            FloatingScrollBarThumbState.Drag => (4d, 0.96d),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
        SetValue(VisualWidthPropertyKey, width);
        SetValue(ThumbOpacityPropertyKey, opacity);
    }
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
    public IReadOnlyList<string> FailedChecks => FloatingWindow.RequiredQuickAddThemeChecks
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

internal sealed record FloatingThemePalette(
    string PanelBrushKey,
    string BackdropBrushKey,
    string BorderBrushKey,
    string HeaderBrushKey,
    string HeaderTextBrushKey,
    string HeaderDangerBrushKey,
    string HeaderHoverBrushKey,
    string HeaderSelectedBrushKey,
    string HeaderSelectedTextBrushKey,
    string SurfaceBrushKey,
    string NoteCardBrushKey,
    string NoteInputBrushKey,
    string NoteInputTextBrushKey,
    string ErrorBrushKey,
    string TextBrushKey,
    string MutedBrushKey,
    string PrimaryCountdownAccentBrushKey,
    string PlushGlowBrushKey,
    double PlushGlowBlurRadius,
    double PlushGlowOpacity,
    double PlushGlowDepth)
{
    internal static FloatingThemePalette Daylight { get; } = new(
        "FloatingDayPanelBrush",
        "FloatingDayBackdropBrush",
        "FloatingDayBorderBrush",
        "FloatingDayHeaderBrush",
        "FloatingDayHeaderTextBrush",
        "FloatingDayHeaderDangerBrush",
        "FloatingDayHeaderHoverBrush",
        "FloatingDayHeaderSelectedBrush",
        "FloatingDayHeaderSelectedTextBrush",
        "FloatingDaySurfaceBrush",
        "FloatingDayNoteCardBrush",
        "FloatingDayNoteInputBrush",
        "FloatingDayNoteInputTextBrush",
        "FloatingDayErrorBrush",
        "FloatingDayTextBrush",
        "FloatingDayMutedBrush",
        "FloatingDayPrimaryCountdownAccentBrush",
        "FloatingDayPlushGlowBrush",
        8,
        0.08,
        0);

}
