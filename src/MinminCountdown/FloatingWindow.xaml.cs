using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class FloatingWindow : Window
{
    private const double CountdownScrollBarSafeOffset = 76;
    private readonly DesktopWorkspace _desktopWorkspace;
    private readonly HolidayCalendar _calendar;
    private readonly MainWindow _manager;
    private readonly ReminderEditing _reminderEditing = new();
    private WorkspaceKind _workspace = WorkspaceKind.Countdowns;
    private bool _settingDefaultDate;
    private bool _dateDirty;
    private FloatingThemePalette? _activeTheme;
    private Brush _floatingPrimaryBrush = Brushes.Black;
    private Brush _floatingMutedBrush = Brushes.Gray;
    private Brush _floatingSurfaceBrush = Brushes.White;
    private Brush _floatingNoteCardBrush = Brushes.White;
    private Brush _floatingBorderBrush = Brushes.Transparent;
    private BitmapImage? _dayCardRaster;
    private readonly Effect? _vectorTaskCardEffect;
    private bool _isApplyingBottomAnchor;
    private bool _trackManualWindowPlacement;
    private bool _hasManualWindowPlacement;
    private Point? _bottomAnchorPosition;
    private int _nativeWindowDragRequestCount;
    private int _nativeWindowDragCompletionCount;

    internal FloatingWindow(
        DesktopWorkspace desktopWorkspace,
        HolidayCalendar calendar,
        MainWindow manager,
        string assetRoot)
    {
        _desktopWorkspace = desktopWorkspace;
        _calendar = calendar;
        _manager = manager;
        InitializeComponent();
        _vectorTaskCardEffect = TaskCard.Effect;
        LoadAssets(assetRoot);
        ConfigureControls();
        WireEvents();
        var snapshot = _desktopWorkspace.Snapshot;
        ApplyTheme(ApplicationTheme.Resolve(
            snapshot.UtcNow,
            snapshot.Settings.Preferences.ThemePreference));
        _desktopWorkspace.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        _calendar.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        Refresh();
    }

    internal int PlushCount => new[] { PlushTopLeft.Source, PlushTopRight.Source, PlushBottomRight.Source }
        .Count(source => source is not null);

    internal ApplicationThemeMode CurrentThemeMode =>
        (_activeTheme ?? throw new InvalidOperationException("悬浮窗主题尚未初始化。")).Mode;

    internal bool PlushDecorationsAreNonInteractive =>
        !PlushTopLeft.IsHitTestVisible &&
        !PlushTopRight.IsHitTestVisible &&
        !PlushBottomRight.IsHitTestVisible;

    internal bool UsesDedicatedThemePalette =>
        _activeTheme is not null &&
        SurfacesFollowActiveTheme &&
        ReferenceEquals(FloatingTitle.Foreground, FindResource(_activeTheme.HeaderTextBrushKey)) &&
        ReferenceEquals(FloatingReminderLabel.Foreground, FindResource(_activeTheme.NoteInputTextBrushKey)) &&
        ReferenceEquals(FloatingReminderIntervalLabel.Foreground, FindResource(_activeTheme.NoteInputTextBrushKey)) &&
        ReferenceEquals(FloatingReminderSeparator.Foreground, FindResource(_activeTheme.NoteInputTextBrushKey)) &&
        ReferenceEquals(FloatingNoteMessage.Foreground, FindResource(_activeTheme.ErrorBrushKey)) &&
        ReferenceEquals(FloatingCountdownsButton.Style, FindResource("FloatingHeaderSegmentButton")) &&
        ReferenceEquals(FloatingNotesButton.Style, FindResource("FloatingHeaderSegmentButton")) &&
        ReferenceEquals(MinimizeToTrayButton.Style, FindResource("FloatingHeaderIconButton")) &&
        ReferenceEquals(OpenManagerButton.Style, FindResource("FloatingHeaderIconButton")) &&
        ReferenceEquals(CloseFloatingButton.Style, FindResource("FloatingHeaderIconButton")) &&
        PlushSceneFollowsActiveTheme;

    private bool SurfacesFollowActiveTheme => _activeTheme?.Mode == ApplicationThemeMode.Day
        ? FloatingDayRasterScene.Visibility == Visibility.Visible &&
          IsTransparent(TaskCard.Background) &&
          IsTransparent(TaskCard.BorderBrush) &&
          IsTransparent(FloatingHeaderSurface.Background) &&
          IsTransparent(FloatingHeaderSurface.BorderBrush) &&
          IsTransparent(FloatingThemeBackdrop.Background) &&
          IsTransparent(FloatingThemeBackdrop.BorderBrush) &&
          FloatingRows.Margin.Equals(new Thickness(9, 13, 10, 0))
        : _activeTheme is not null &&
          ReferenceEquals(TaskCard.Background, FindResource(_activeTheme.PanelBrushKey)) &&
          ReferenceEquals(FloatingHeaderSurface.Background, FindResource(_activeTheme.HeaderBrushKey)) &&
          ReferenceEquals(FloatingThemeBackdrop.Background, FindResource(_activeTheme.BackdropBrushKey)) &&
          FloatingRows.Margin.Equals(new Thickness(8, 13, 11, 0));

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

    internal bool UsesReferencePlushPoses =>
        UsesAsset(PlushTopLeft, "floating-plush-mymelody-reference.png") &&
        UsesAsset(PlushTopRight, "floating-plush-cinnamoroll-reference.png") &&
        UsesAsset(PlushBottomRight, "floating-plush-kuromi-reference.png");

    private IEnumerable<Image> PlushImages => new[]
    {
        PlushTopLeft,
        PlushTopRight,
        PlushBottomRight
    };

    private IEnumerable<Border> PlushAnchors => new[]
    {
        MyMelodyPlushAnchor,
        CinnamorollPlushAnchor,
        KuromiPlushAnchor
    };

    internal IReadOnlyList<string> PlushAssetNames => PlushImages
        .Select(image => image.Source is BitmapImage { UriSource: { } source }
            ? Path.GetFileName(source.LocalPath)
            : string.Empty)
        .ToArray();

    private bool PlushSceneFollowsActiveTheme
    {
        get
        {
            if (_activeTheme is null)
            {
                return false;
            }

            var glowColor = ThemeColor(_activeTheme.PlushGlowBrushKey);
            var expectedPresentation = _activeTheme.Mode == ApplicationThemeMode.Day
                ? "DayRaster"
                : "NightAnchors";
            return string.Equals(MascotPresentationMode(), expectedPresentation, StringComparison.Ordinal) &&
                   DayMascotsImage.Source is not null &&
                   PlushImages.All(image =>
                image.Effect is DropShadowEffect glow &&
                glow.Color.Equals(glowColor) &&
                Math.Abs(glow.BlurRadius - _activeTheme.PlushGlowBlurRadius) < 0.01 &&
                Math.Abs(glow.ShadowDepth - _activeTheme.PlushGlowDepth) < 0.01 &&
                Math.Abs(glow.Opacity - _activeTheme.PlushGlowOpacity) < 0.01);
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
            var workArea = SystemParameters.WorkArea;
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

            var workArea = SystemParameters.WorkArea;
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
                FloatingNoteText,
                FloatingNoteDate,
                FloatingReminder,
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
        var workArea = SystemParameters.WorkArea;
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
            Refresh();

            var remainsWithinWorkArea =
                Left >= minimumLeft - 0.5 &&
                Left <= maximumLeft + 0.5 &&
                Top >= minimumTop - 0.5 &&
                Top <= maximumTop + 0.5;

            return new FloatingManualPlacementExerciseResult(
                manualPositionPersists && remainsWithinWorkArea);
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

    internal string IncomeText => FloatingIncome.Text;

    internal event EventHandler? ExitRequested;

    internal event EventHandler? MinimizeToTrayRequested;

    private bool RunCrossDayReminderAddExercise()
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
            10,
            15,
            0,
            firstDueDay.Offset);
        FloatingNoteText.Text = "验证快速跨天提醒";
        FloatingNoteDate.SelectedDate = noteDate;
        FloatingReminder.SelectedIndex = 2;
        FloatingReminderInterval.SelectedIndex = 1;
        SetReminderSchedule(firstDue);
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
        FloatingReminder.SelectedIndex = 0;
        FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var noReminderWorks = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗不提醒")?.Reminder is NoReminder;

        FloatingNoteText.Text = "验证悬浮窗仅一次";
        FloatingNoteDate.SelectedDate = noteDate;
        FloatingReminder.SelectedIndex = 1;
        SetReminderSchedule(onceDue);
        FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var onceWorks = _desktopWorkspace.Snapshot.Settings.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗仅一次")?.Reminder is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            } once &&
            once.FirstDueUtc == onceDue.ToUniversalTime();

        FloatingNoteText.Text = "验证悬浮窗重复提醒";
        FloatingNoteDate.SelectedDate = noteDate;
        FloatingReminder.SelectedIndex = 2;
        FloatingReminderInterval.SelectedIndex = 1;
        SetReminderSchedule(repeatDue);
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
        FloatingTitle.Text = workspace == WorkspaceKind.Countdowns ? "倒计时" : "今日便签";
        FloatingBodyScroller.Margin = workspace == WorkspaceKind.Notes
            ? new Thickness(0, 0, 76, 0)
            : new Thickness(0);
        UpdateFloatingBodyScrollBarPlacement();
        FloatingNoteEntry.Visibility =
            workspace == WorkspaceKind.Notes ? Visibility.Visible : Visibility.Collapsed;
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

    private FloatingThemeExerciseResult RunThemeBoundaryExercise()
    {
        var checkpoints = new[]
        {
            new DateTimeOffset(2030, 1, 15, 6, 59, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2030, 1, 15, 7, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2030, 1, 15, 18, 59, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2030, 1, 15, 19, 0, 0, TimeSpan.FromHours(8))
        };
        var modes = new List<string>();
        var dayPanelColor = string.Empty;
        var nightPanelColor = string.Empty;
        var dayPlushGlowColor = string.Empty;
        var nightPlushGlowColor = string.Empty;
        var mascotPresentationModes = new List<string>();
        var textContrast = true;
        var surfacesFollowDayNightTheme = true;
        var plushSceneFollowsDayNightTheme = true;

        foreach (var checkpoint in checkpoints)
        {
            ApplyTheme(ApplicationTheme.Resolve(checkpoint.ToUniversalTime()));
            UpdateTime(checkpoint.ToUniversalTime());
            var activeTheme = _activeTheme ?? throw new InvalidOperationException("悬浮窗主题尚未初始化。");
            modes.Add(activeTheme.Mode.ToString());
            mascotPresentationModes.Add(MascotPresentationMode());
            if (activeTheme.Mode == ApplicationThemeMode.Day)
            {
                dayPanelColor = ThemeColor(activeTheme.PanelBrushKey).ToString();
                dayPlushGlowColor = PlushGlowColor();
            }
            else
            {
                nightPanelColor = ThemeColor(activeTheme.PanelBrushKey).ToString();
                nightPlushGlowColor = PlushGlowColor();
            }

            textContrast &= HasAccessibleThemeTextContrast;
            surfacesFollowDayNightTheme &= SurfacesFollowActiveTheme;
            plushSceneFollowsDayNightTheme &= PlushSceneFollowsActiveTheme;
        }

        var snapshot = _desktopWorkspace.Snapshot;
        ApplyTheme(ApplicationTheme.Resolve(
            snapshot.UtcNow,
            snapshot.Settings.Preferences.ThemePreference));
        UpdateTime(snapshot.UtcNow);
        var expectedModes = new[] { "Night", "Day", "Day", "Night" };
        var expectedMascotPresentationModes = new[]
        {
            "NightAnchors",
            "DayRaster",
            "DayRaster",
            "NightAnchors"
        };
        return new FloatingThemeExerciseResult(
            modes,
            new[] { dayPanelColor, nightPanelColor },
            new[] { dayPlushGlowColor, nightPlushGlowColor },
            mascotPresentationModes,
            modes.SequenceEqual(expectedModes, StringComparer.Ordinal),
            mascotPresentationModes.SequenceEqual(
                expectedMascotPresentationModes,
                StringComparer.Ordinal),
            textContrast,
            surfacesFollowDayNightTheme,
            plushSceneFollowsDayNightTheme);
    }

    internal void Reanchor()
    {
        var workArea = SystemParameters.WorkArea;
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

    internal void ApplyTheme(ApplicationThemeMode mode)
    {
        var nextTheme = FloatingThemePalette.For(mode);
        if (ReferenceEquals(_activeTheme, nextTheme))
        {
            return;
        }

        _activeTheme = nextTheme;
        SetActiveThemeBrush("FloatingHeaderTextBrush", nextTheme.HeaderTextBrushKey);
        SetActiveThemeBrush("FloatingHeaderDangerBrush", nextTheme.HeaderDangerBrushKey);
        SetActiveThemeBrush("FloatingHeaderHoverBrush", nextTheme.HeaderHoverBrushKey);
        SetActiveThemeBrush("FloatingHeaderSelectedBrush", nextTheme.HeaderSelectedBrushKey);
        SetActiveThemeBrush("FloatingHeaderSelectedTextBrush", nextTheme.HeaderSelectedTextBrushKey);
        SetActiveThemeBrush("FloatingNoteInputTextBrush", nextTheme.NoteInputTextBrushKey);
        SetActiveThemeBrush("FloatingErrorBrush", nextTheme.ErrorBrushKey);
        SetActiveThemeBrush("FloatingMutedBrush", nextTheme.MutedBrushKey);
        var useDayRaster = nextTheme.Mode == ApplicationThemeMode.Day;
        FloatingDayRasterScene.Visibility = useDayRaster ? Visibility.Visible : Visibility.Collapsed;
        TaskCard.Background = useDayRaster ? Brushes.Transparent : ThemeBrush(nextTheme.PanelBrushKey);
        TaskCard.BorderBrush = useDayRaster ? Brushes.Transparent : ThemeBrush(nextTheme.BorderBrushKey);
        TaskCard.Effect = useDayRaster ? null : _vectorTaskCardEffect;
        FloatingThemeBackdrop.Background = useDayRaster ? Brushes.Transparent : ThemeBrush(nextTheme.BackdropBrushKey);
        FloatingThemeBackdrop.BorderBrush = useDayRaster ? Brushes.Transparent : ThemeBrush(nextTheme.BorderBrushKey);
        FloatingHeaderSurface.Background = useDayRaster ? Brushes.Transparent : ThemeBrush(nextTheme.HeaderBrushKey);
        FloatingHeaderSurface.BorderBrush = useDayRaster ? Brushes.Transparent : ThemeBrush(nextTheme.BorderBrushKey);
        FloatingRows.Margin = useDayRaster
            ? new Thickness(9, 13, 10, 0)
            : new Thickness(8, 13, 11, 0);
        SetRasterTranslation(FloatingTitle, useDayRaster, -1, -1);
        SetRasterTranslation(FloatingCountdownsButton, useDayRaster, 0, 1);
        SetRasterTranslation(FloatingNotesButton, useDayRaster, 0, 1);
        SetRasterTranslation(OpenManagerButton, useDayRaster, -1, -1);
        SetRasterTranslation(CloseFloatingButton, useDayRaster, 0, 0);
        SetRasterTranslation(FloatingIncome, useDayRaster, 2, 3);
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

    private static void SetRasterTranslation(
        UIElement element,
        bool useDayRaster,
        double offsetX,
        double offsetY) =>
        element.RenderTransform = useDayRaster
            ? new TranslateTransform(offsetX, offsetY)
            : Transform.Identity;

    private Color ThemeColor(string resourceKey) =>
        ThemeBrush(resourceKey) is SolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"悬浮窗主题资源不是纯色画刷：{resourceKey}");

    private void ApplyPlushSceneTheme(FloatingThemePalette theme)
    {
        var useDayRaster = theme.Mode == ApplicationThemeMode.Day;
        DayMascotsImage.Visibility = useDayRaster ? Visibility.Visible : Visibility.Collapsed;
        foreach (var anchor in PlushAnchors)
        {
            anchor.Visibility = useDayRaster ? Visibility.Collapsed : Visibility.Visible;
        }

        var glowColor = ThemeColor(theme.PlushGlowBrushKey);
        foreach (var image in PlushImages)
        {
            image.Effect = new DropShadowEffect
            {
                Color = glowColor,
                BlurRadius = theme.PlushGlowBlurRadius,
                ShadowDepth = theme.PlushGlowDepth,
                Direction = 90,
                Opacity = theme.PlushGlowOpacity,
                RenderingBias = RenderingBias.Quality
            };
        }
    }

    private string PlushGlowColor() =>
        PlushTopLeft.Effect is DropShadowEffect glow
            ? glow.Color.ToString()
            : string.Empty;

    private string MascotPresentationMode()
    {
        var anchors = PlushAnchors.ToArray();
        if (DayMascotsImage.Visibility == Visibility.Visible &&
            anchors.All(anchor => anchor.Visibility == Visibility.Collapsed))
        {
            return "DayRaster";
        }

        if (DayMascotsImage.Visibility == Visibility.Collapsed &&
            anchors.All(anchor => anchor.Visibility == Visibility.Visible))
        {
            return "NightAnchors";
        }

        return "Mixed";
    }

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
        for (var hour = 0; hour < 24; hour++)
        {
            FloatingReminderHour.Items.Add(hour.ToString("D2", CultureInfo.InvariantCulture));
        }

        for (var minute = 0; minute < 60; minute++)
        {
            FloatingReminderMinute.Items.Add(minute.ToString("D2", CultureInfo.InvariantCulture));
        }

        FloatingReminder.Items.Add("不提醒");
        FloatingReminder.Items.Add("仅一次");
        FloatingReminder.Items.Add("重复提醒");
        FloatingReminderInterval.Items.Add("15 分钟");
        FloatingReminderInterval.Items.Add("30 分钟");
        FloatingReminderInterval.Items.Add("60 分钟");
        FloatingReminderInterval.Items.Add("自定义");
        FloatingReminder.SelectedIndex = 0;
        FloatingReminderInterval.SelectedIndex = 1;
        FloatingTags.SetTags(_desktopWorkspace.Snapshot.Settings.Tags);
        AutomationProperties.SetName(FloatingCountdownsButton, "显示悬浮倒计时");
        AutomationProperties.SetName(FloatingNotesButton, "显示悬浮便签");
        AutomationProperties.SetName(MinimizeToTrayButton, "缩小到系统托盘");
        AutomationProperties.SetName(OpenManagerButton, "打开管理窗口");
        AutomationProperties.SetName(CloseFloatingButton, "退出应用");
        AutomationProperties.SetName(FloatingNoteText, "快速便签内容");
        AutomationProperties.SetName(FloatingNoteDate, "快速便签日期");
        AutomationProperties.SetName(FloatingReminder, "快速便签提醒模式");
        AutomationProperties.SetName(FloatingReminderInterval, "快速便签重复提醒间隔");
        AutomationProperties.SetName(FloatingCustomReminderMinutes, "快速便签自定义提醒分钟");
        AutomationProperties.SetName(FloatingReminderDate, "快速便签首次提醒日期");
        AutomationProperties.SetName(FloatingReminderHour, "快速便签首次提醒小时");
        AutomationProperties.SetName(FloatingReminderMinute, "快速便签首次提醒分钟");
        AutomationProperties.SetName(FloatingAddNote, "添加快速便签");
        ThemedDatePickerBehavior.Attach(FloatingNoteDate);
        ThemedDatePickerBehavior.Attach(FloatingReminderDate);
        UiAccessibility.MarkLive(FloatingNoteMessage);
    }

    private void WireEvents()
    {
        FloatingCountdownsButton.Click += (_, _) => ShowWorkspace(WorkspaceKind.Countdowns);
        FloatingNotesButton.Click += (_, _) => ShowWorkspace(WorkspaceKind.Notes);
        MinimizeToTrayButton.Click += (_, _) => MinimizeToTrayRequested?.Invoke(this, EventArgs.Empty);
        OpenManagerButton.Click += (_, _) =>
        {
            _manager.ShowWorkspace(_workspace);
            if (!_manager.IsVisible)
            {
                _manager.Show();
            }

            _manager.WindowState = WindowState.Normal;
            _manager.Activate();
        };
        CloseFloatingButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        FloatingNoteDate.SelectedDateChanged += (_, _) =>
        {
            if (!_settingDefaultDate)
            {
                _dateDirty = true;
            }
        };
        FloatingReminder.SelectionChanged += (_, _) => UpdateReminderSchedule();
        FloatingReminderInterval.SelectionChanged += (_, _) => UpdateReminderInterval();
        FloatingCustomReminderMinutes.TextChanged += (_, _) => SetDefaultReminderSchedule();
        FloatingAddNote.Click += (_, _) => AddNote();
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
        Loaded += (_, _) =>
        {
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
        RenderRows(snapshot.UtcNow);
        Reanchor();
    }

    private void RenderRows(DateTimeOffset nowUtc)
    {
        FloatingRows.Children.Clear();
        var snapshot = _desktopWorkspace.Snapshot;
        if (_workspace == WorkspaceKind.Countdowns)
        {
            var records = snapshot.VisibleCountdowns
                .Where(record => record.TargetUtc > nowUtc)
                .ToList();
            if (!snapshot.HasFilterConstraints &&
                _calendar.GetWorkdayEndCountdown(nowUtc, snapshot.Settings.Preferences) is { } workdayEnd)
            {
                records.Add(workdayEnd);
            }

            FloatingCountdownCount = records.Count(record => record.Id != "__workday_end__");
            FloatingCountdownTexts = records
                .Where(record => record.Id != "__workday_end__")
                .Select(record => CountdownFormatting.FormatRemaining(
                    record.TargetUtc,
                    nowUtc,
                    snapshot.Settings.Preferences.DisplayPrecision))
                .ToArray();
            foreach (var row in BuildWarmPaperCountdownRows(
                         records,
                         nowUtc,
                         snapshot.Settings.Preferences.DisplayPrecision))
            {
                FloatingRows.Children.Add(row);
            }

            if (records.Count == 0)
            {
                FloatingRows.Children.Add(EmptyText("没有未到期的倒计时。"));
            }
        }
        else
        {
            var notes = snapshot.VisibleActiveNotes;
            foreach (var note in notes)
            {
                FloatingRows.Children.Add(BuildCalendarIndexNoteCard(note, nowUtc));
            }

            if (notes.Count == 0)
            {
                FloatingRows.Children.Add(EmptyText("今天还没有待办便签。"));
            }
        }

        var earnings = _calendar.GetEarnings(nowUtc, snapshot.Settings.Preferences);
        FloatingIncome.Text = earnings is null
            ? _calendar.StatusText
            : $"今日已赚 ￥{earnings.Value:N2}";
        Dispatcher.BeginInvoke(Reanchor);
    }

    private TextBlock EmptyText(string text) => new()
    {
        Text = text,
        Margin = new Thickness(2, 8, 2, 8),
        HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = _floatingMutedBrush
    };

    private void AddNote()
    {
        if (string.IsNullOrWhiteSpace(FloatingNoteText.Text))
        {
            SetNoteMessage("请先写下便签内容。");
            return;
        }

        if (FloatingNoteDate.SelectedDate is null)
        {
            SetNoteMessage("请选择便签日期。");
            return;
        }

        var reminderResult = _reminderEditing.Build(new ReminderEditingRequest(
            SelectedReminderMode(),
            FloatingReminderDate.SelectedDate,
            FloatingReminderHour.SelectedIndex,
            FloatingReminderMinute.SelectedIndex,
            SelectedReminderInterval(),
            FloatingCustomReminderMinutes.Text,
            _desktopWorkspace.Snapshot.UtcNow,
            null,
            true));
        if (!reminderResult.Succeeded)
        {
            SetNoteMessage(reminderResult.Error);
            return;
        }

        var result = _desktopWorkspace.Apply(new SaveNoteChange(
            null,
            FloatingNoteText.Text,
            FloatingNoteDate.SelectedDate,
            false,
            FloatingTags.SelectedTagIds.ToArray(),
            reminderResult.Reminder));
        if (!result.Succeeded)
        {
            SetNoteMessage($"保存失败：{result.Error}");
            return;
        }

        FloatingNoteText.Clear();
        _dateDirty = false;
        _settingDefaultDate = true;
        FloatingNoteDate.SelectedDate = _desktopWorkspace.Snapshot.BeijingNow.Date;
        _settingDefaultDate = false;
        SetDefaultReminderSchedule();
        SetNoteMessage(string.Empty);
    }

    private void UpdateReminderSchedule()
    {
        var enabled = FloatingReminder.SelectedIndex > 0;
        var repeating = FloatingReminder.SelectedIndex == 2;
        FloatingReminderSchedule.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        FloatingReminderIntervalPanel.Visibility =
            repeating ? Visibility.Visible : Visibility.Collapsed;
        FloatingCustomReminderMinutes.Visibility =
            repeating && FloatingReminderInterval.SelectedIndex == 3
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (enabled)
        {
            SetDefaultReminderSchedule();
        }
        else
        {
            SetReminderSchedule(null);
        }
    }

    private void UpdateReminderInterval()
    {
        FloatingCustomReminderMinutes.Visibility =
            FloatingReminder.SelectedIndex == 2 && FloatingReminderInterval.SelectedIndex == 3
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (FloatingReminder.SelectedIndex == 2)
        {
            SetDefaultReminderSchedule();
        }
    }

    private void SetDefaultReminderSchedule()
    {
        if (FloatingReminder.SelectedIndex == 0)
        {
            return;
        }

        var result = _reminderEditing.GetDefaultFirstDueBeijing(
            SelectedReminderMode(),
            SelectedReminderInterval(),
            FloatingCustomReminderMinutes.Text,
            _desktopWorkspace.Snapshot.UtcNow);
        if (result.Succeeded)
        {
            SetReminderSchedule(result.FirstDueBeijing);
        }
    }

    private void SetReminderSchedule(DateTimeOffset? beijing)
    {
        FloatingReminderDate.SelectedDate = beijing?.Date;
        FloatingReminderHour.SelectedIndex = beijing?.Hour ?? -1;
        FloatingReminderMinute.SelectedIndex = beijing?.Minute ?? -1;
    }

    private ReminderEditingMode SelectedReminderMode() => FloatingReminder.SelectedIndex switch
    {
        0 => ReminderEditingMode.None,
        1 => ReminderEditingMode.Once,
        2 => ReminderEditingMode.Repeating,
        _ => (ReminderEditingMode)(-1)
    };

    private ReminderIntervalSelection SelectedReminderInterval() =>
        FloatingReminderInterval.SelectedIndex switch
        {
            0 => ReminderIntervalSelection.FifteenMinutes,
            1 => ReminderIntervalSelection.ThirtyMinutes,
            2 => ReminderIntervalSelection.SixtyMinutes,
            _ => ReminderIntervalSelection.Custom
        };

    private void SetNoteMessage(string text)
    {
        FloatingNoteMessage.Text = text;
    }

    private void LoadAssets(string assetRoot)
    {
        var plushRoot = Path.Combine(assetRoot, "assets", "floating-plush");
        DayOuterShellImage.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "component-day-outer-shell.png"));
        DayHeaderImage.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "component-day-header-blank.png"));
        _dayCardRaster = MainWindow.LoadImage(
            Path.Combine(plushRoot, "component-day-card-blank.png"));
        DayMascotsImage.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "component-day-mascots.png"));
        PlushTopLeft.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "floating-plush-mymelody-reference.png"));
        PlushTopRight.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "floating-plush-cinnamoroll-reference.png"));
        PlushBottomRight.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "floating-plush-kuromi-reference.png"));
    }

    private static bool UsesAsset(Image image, string expectedFileName) =>
        image.Source is BitmapImage { UriSource: { } source } &&
        string.Equals(Path.GetFileName(source.LocalPath), expectedFileName, StringComparison.OrdinalIgnoreCase);

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
            if (current is Control)
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
    bool UsesChinaStandardTime,
    bool RasterMascotBoundaryStatesMatch,
    bool TextContrast,
    bool SurfacesFollowDayNightTheme,
    bool PlushSceneFollowsDayNightTheme);

internal sealed record FloatingThemePalette(
    ApplicationThemeMode Mode,
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
    string PlushGlowBrushKey,
    double PlushGlowBlurRadius,
    double PlushGlowOpacity,
    double PlushGlowDepth)
{
    private static readonly FloatingThemePalette Daylight = new(
        ApplicationThemeMode.Day,
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
        "FloatingDayPlushGlowBrush",
        8,
        0.08,
        0);

    private static readonly FloatingThemePalette Nighttime = new(
        ApplicationThemeMode.Night,
        "FloatingNightPanelBrush",
        "FloatingNightBackdropBrush",
        "FloatingNightBorderBrush",
        "FloatingNightHeaderBrush",
        "FloatingNightHeaderTextBrush",
        "FloatingNightHeaderDangerBrush",
        "FloatingNightHeaderHoverBrush",
        "FloatingNightHeaderSelectedBrush",
        "FloatingNightHeaderSelectedTextBrush",
        "FloatingNightSurfaceBrush",
        "FloatingNightNoteCardBrush",
        "FloatingNightNoteInputBrush",
        "FloatingNightNoteInputTextBrush",
        "FloatingNightErrorBrush",
        "FloatingNightTextBrush",
        "FloatingNightMutedBrush",
        "FloatingNightPlushGlowBrush",
        16,
        0.34,
        0);

    public static FloatingThemePalette For(DateTimeOffset nowUtc)
    {
        return For(ApplicationTheme.Resolve(nowUtc));
    }

    public static FloatingThemePalette For(ApplicationThemeMode mode) =>
        mode == ApplicationThemeMode.Day ? Daylight : Nighttime;
}
