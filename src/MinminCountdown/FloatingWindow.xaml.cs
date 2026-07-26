using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class FloatingWindow : Window
{
    private readonly ApplicationSession _session;
    private readonly DesktopWorkspace _workspaceModule;
    private readonly HolidayCalendar _calendar;
    private readonly MainWindow _manager;
    private WorkspaceKind _workspace = WorkspaceKind.Countdowns;
    private bool _settingDefaultDate;
    private bool _dateDirty;
    private FloatingThemePalette? _activeTheme;
    private Brush _floatingPrimaryBrush = Brushes.Black;
    private Brush _floatingMutedBrush = Brushes.Gray;
    private Brush _floatingSurfaceBrush = Brushes.White;
    private Brush _floatingNoteCardBrush = Brushes.White;
    private Brush _floatingBorderBrush = Brushes.Transparent;

    internal FloatingWindow(
        ApplicationSession session,
        HolidayCalendar calendar,
        MainWindow manager,
        string assetRoot)
    {
        _session = session;
        _workspaceModule = new DesktopWorkspace(session);
        _calendar = calendar;
        _manager = manager;
        InitializeComponent();
        LoadAssets(assetRoot);
        ConfigureControls();
        WireEvents();
        ApplyTheme(_session.UtcNow);
        _session.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        _calendar.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        Refresh();
    }

    internal int PlushCount => new[] { PlushTopLeft.Source, PlushTopRight.Source, PlushBottomRight.Source }
        .Count(source => source is not null);

    internal bool PlushDecorationsAreNonInteractive =>
        !PlushTopLeft.IsHitTestVisible &&
        !PlushTopRight.IsHitTestVisible &&
        !PlushBottomRight.IsHitTestVisible;

    internal bool UsesDedicatedThemePalette =>
        _activeTheme is not null &&
        ReferenceEquals(TaskCard.Background, FindResource(_activeTheme.PanelBrushKey)) &&
        ReferenceEquals(FloatingHeaderSurface.Background, FindResource(_activeTheme.HeaderBrushKey)) &&
        ReferenceEquals(FloatingThemeBackdrop.Background, FindResource(_activeTheme.BackdropBrushKey)) &&
        ReferenceEquals(FloatingTitle.Foreground, FindResource(_activeTheme.HeaderTextBrushKey)) &&
        ReferenceEquals(FloatingReminderLabel.Foreground, FindResource(_activeTheme.NoteInputTextBrushKey)) &&
        ReferenceEquals(FloatingReminderIntervalLabel.Foreground, FindResource(_activeTheme.NoteInputTextBrushKey)) &&
        ReferenceEquals(FloatingReminderSeparator.Foreground, FindResource(_activeTheme.NoteInputTextBrushKey)) &&
        ReferenceEquals(FloatingNoteMessage.Foreground, FindResource(_activeTheme.ErrorBrushKey)) &&
        ReferenceEquals(FloatingCountdownsButton.Style, FindResource("FloatingHeaderSegmentButton")) &&
        ReferenceEquals(FloatingNotesButton.Style, FindResource("FloatingHeaderSegmentButton")) &&
        ReferenceEquals(OpenManagerButton.Style, FindResource("FloatingHeaderIconButton")) &&
        ReferenceEquals(CloseFloatingButton.Style, FindResource("FloatingHeaderIconButton"));

    internal bool PlushDocksUseActiveTheme =>
        _activeTheme is not null &&
        ReferenceEquals(MyMelodyPlushDock.Background, FindResource(_activeTheme.MyMelodyDockBrushKey)) &&
        ReferenceEquals(CinnamorollPlushDock.Background, FindResource(_activeTheme.CinnamorollDockBrushKey)) &&
        ReferenceEquals(KuromiPlushDock.Background, FindResource(_activeTheme.KuromiDockBrushKey));

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

    internal IReadOnlyList<string> FloatingCountdownTexts { get; private set; } = Array.Empty<string>();

    internal int FloatingCountdownCount { get; private set; }

    internal string IncomeText => FloatingIncome.Text;

    internal event EventHandler? ExitRequested;

    internal bool RunCrossDayReminderAddExercise()
    {
        ShowWorkspace(WorkspaceKind.Notes);
        var countBefore = _session.Current.Notes.Count;
        var noteDate = _session.BeijingNow.AddDays(2).Date;
        var firstDueDay = _session.BeijingNow.AddDays(3);
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
        var added = _session.Current.Notes.LastOrDefault(note => note.Text == "验证快速跨天提醒");
        return
            added is not null &&
            _session.Current.Notes.Count == countBefore + 1 &&
            added.Date == noteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
            added.ReminderIntervalMinutes == 30 &&
            added.ReminderStartUtc == firstDue.ToUniversalTime().Subtract(TimeSpan.FromMinutes(30));
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
            10,
            10,
            0,
            onceDue.Offset);
        var repeatDue = onceDue.AddDays(1).AddMinutes(20);

        FloatingNoteText.Text = "验证悬浮窗不提醒";
        FloatingNoteDate.SelectedDate = noteDate;
        FloatingReminder.SelectedIndex = 0;
        FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var noReminderWorks = _session.Current.Notes.LastOrDefault(
            note => note.Text == "验证悬浮窗不提醒")?.Reminder is NoReminder;

        FloatingNoteText.Text = "验证悬浮窗仅一次";
        FloatingNoteDate.SelectedDate = noteDate;
        FloatingReminder.SelectedIndex = 1;
        SetReminderSchedule(onceDue);
        FloatingAddNote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var onceWorks = _session.Current.Notes.LastOrDefault(
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
        var repeatWorks = _session.Current.Notes.LastOrDefault(
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
        FloatingNoteEntry.Visibility =
            workspace == WorkspaceKind.Notes ? Visibility.Visible : Visibility.Collapsed;
        if (workspace == WorkspaceKind.Notes)
        {
            FloatingTags.SetSelection(_workspaceModule.SelectedTagIds);
            if (!_dateDirty)
            {
                _settingDefaultDate = true;
                FloatingNoteDate.SelectedDate = _session.BeijingNow.Date;
                _settingDefaultDate = false;
            }
        }

        Refresh();
    }

    internal void UpdateTime(DateTimeOffset nowUtc)
    {
        ApplyTheme(nowUtc);
        if (!_dateDirty)
        {
            _settingDefaultDate = true;
            FloatingNoteDate.SelectedDate = ChinaStandardTime.ToBeijingTime(nowUtc).Date;
            _settingDefaultDate = false;
        }

        RenderRows(nowUtc);
    }

    internal FloatingThemeExerciseResult RunThemeBoundaryExercise()
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
        var docksFollowTheme = true;
        var textContrast = true;

        foreach (var checkpoint in checkpoints)
        {
            UpdateTime(checkpoint.ToUniversalTime());
            modes.Add(_activeTheme?.Mode.ToString() ?? string.Empty);
            if (_activeTheme?.Mode == ApplicationThemeMode.Day)
            {
                dayPanelColor = BrushColor(TaskCard.Background);
            }
            else
            {
                nightPanelColor = BrushColor(TaskCard.Background);
            }

            docksFollowTheme &= PlushDocksUseActiveTheme;
            textContrast &= HasAccessibleThemeTextContrast;
        }

        UpdateTime(_session.UtcNow);
        var expectedModes = new[] { "Night", "Day", "Day", "Night" };
        return new FloatingThemeExerciseResult(
            modes,
            new[] { dayPanelColor, nightPanelColor },
            modes.SequenceEqual(expectedModes, StringComparer.Ordinal),
            docksFollowTheme,
            textContrast);
    }

    internal void Reanchor()
    {
        var workArea = SystemParameters.WorkArea;
        MaxHeight = Math.Max(280, workArea.Height - 24);
        Left = Math.Max(workArea.Left + 12, workArea.Right - ActualWidth - 12);
        Top = Math.Max(workArea.Top + 12, workArea.Bottom - ActualHeight - 12);
    }

    private void ApplyTheme(DateTimeOffset nowUtc)
    {
        var nextTheme = FloatingThemePalette.For(nowUtc);
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
        TaskCard.Background = ThemeBrush(nextTheme.PanelBrushKey);
        TaskCard.BorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        FloatingThemeBackdrop.Background = ThemeBrush(nextTheme.BackdropBrushKey);
        FloatingThemeBackdrop.BorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        FloatingHeaderSurface.Background = ThemeBrush(nextTheme.HeaderBrushKey);
        FloatingHeaderSurface.BorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        FloatingNoteEntry.Background = ThemeBrush(nextTheme.NoteInputBrushKey);
        FloatingNoteEntry.BorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        MyMelodyPlushDock.Background = ThemeBrush(nextTheme.MyMelodyDockBrushKey);
        CinnamorollPlushDock.Background = ThemeBrush(nextTheme.CinnamorollDockBrushKey);
        KuromiPlushDock.Background = ThemeBrush(nextTheme.KuromiDockBrushKey);
        MyMelodyPlushDock.BorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        CinnamorollPlushDock.BorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        KuromiPlushDock.BorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
        _floatingPrimaryBrush = ThemeBrush(nextTheme.TextBrushKey);
        _floatingMutedBrush = ThemeBrush(nextTheme.MutedBrushKey);
        _floatingSurfaceBrush = ThemeBrush(nextTheme.SurfaceBrushKey);
        _floatingNoteCardBrush = ThemeBrush(nextTheme.NoteCardBrushKey);
        _floatingBorderBrush = ThemeBrush(nextTheme.BorderBrushKey);
    }

    private Brush ThemeBrush(string resourceKey) =>
        FindResource(resourceKey) as Brush ??
        throw new InvalidOperationException($"找不到悬浮窗主题资源：{resourceKey}");

    private void SetActiveThemeBrush(string activeResourceKey, string sourceResourceKey) =>
        Application.Current.Resources[activeResourceKey] = ThemeBrush(sourceResourceKey);

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
        FloatingTags.SetTags(_session.Current.Tags);
        AutomationProperties.SetName(FloatingCountdownsButton, "显示悬浮倒计时");
        AutomationProperties.SetName(FloatingNotesButton, "显示悬浮便签");
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
            if (_workspaceModule.TryCreateTag(
                    name,
                    "#F4B6A6",
                    out var tag,
                    out var error) &&
                tag is not null)
            {
                FloatingTags.SelectedTagIds.Add(tag.Id);
                FloatingTags.SetSelection(FloatingTags.SelectedTagIds);
            }
            else
            {
                SetNoteMessage(error);
            }
        };
        SizeChanged += (_, _) => Reanchor();
        Loaded += (_, _) => Reanchor();
    }

    private void Refresh()
    {
        ApplyTheme(_session.UtcNow);
        FloatingTags.SetTags(_session.Current.Tags);
        RenderRows(_session.UtcNow);
        Reanchor();
    }

    private void RenderRows(DateTimeOffset nowUtc)
    {
        FloatingRows.Children.Clear();
        if (_workspace == WorkspaceKind.Countdowns)
        {
            var records = _workspaceModule.GetVisibleCountdowns()
                .Where(record => record.TargetUtc > nowUtc)
                .ToList();
            if (!_workspaceModule.HasFilterConstraints &&
                _calendar.GetWorkdayEndCountdown(nowUtc, _session.Current.Preferences) is { } workdayEnd)
            {
                records.Add(workdayEnd);
            }

            FloatingCountdownCount = records.Count(record => record.Id != "__workday_end__");
            FloatingCountdownTexts = records
                .Where(record => record.Id != "__workday_end__")
                .Select(record => CountdownFormatting.FormatRemaining(
                    record.TargetUtc,
                    nowUtc,
                    _session.Current.Preferences.DisplayPrecision))
                .ToArray();
            foreach (var record in records)
            {
                FloatingRows.Children.Add(FloatingCountdownCard(record, nowUtc));
            }

            if (records.Count == 0)
            {
                FloatingRows.Children.Add(EmptyText("没有未到期的倒计时。"));
            }
        }
        else
        {
            var notes = _workspaceModule.GetVisibleActiveNotes();
            foreach (var note in notes)
            {
                FloatingRows.Children.Add(FloatingNoteCard(note));
            }

            if (notes.Count == 0)
            {
                FloatingRows.Children.Add(EmptyText("今天还没有待办便签。"));
            }
        }

        var earnings = _calendar.GetEarnings(nowUtc, _session.Current.Preferences);
        FloatingIncome.Text = earnings is null
            ? _calendar.StatusText
            : $"今日已赚 ￥{earnings.Value:N2}";
        Dispatcher.BeginInvoke(Reanchor);
    }

    private FrameworkElement FloatingCountdownCard(CountdownRecord record, DateTimeOffset nowUtc)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = record.Name,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = _floatingPrimaryBrush
        });
        stack.Children.Add(new TextBlock
        {
            Text = CountdownFormatting.FormatRemaining(
                record.TargetUtc,
                nowUtc,
                _session.Current.Preferences.DisplayPrecision),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = _floatingMutedBrush
        });
        return FloatingCard(stack, _floatingSurfaceBrush);
    }

    private FrameworkElement FloatingNoteCard(NoteRecord note)
    {
        var check = UiAccessibility.Name(
            new CheckBox
            {
                Tag = note.Id,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0),
                Foreground = _floatingPrimaryBrush
            },
            $"完成便签 {note.Text}");
        check.Checked += (_, _) =>
        {
            if (!_workspaceModule.TrySetNoteCompletion(note.Id, true, out var error))
            {
                check.IsChecked = false;
                SetNoteMessage(error);
            }
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = note.Text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = _floatingPrimaryBrush
        });
        var nextReminder = ReminderSchedule.GetNextDueUtc(note, _session.UtcNow);
        if (nextReminder is not null)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"下次提醒：{ChinaStandardTime.ToBeijingTime(nextReminder.Value):M月d日 HH:mm}",
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 11,
                Foreground = _floatingMutedBrush
            });
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(body, 1);
        grid.Children.Add(check);
        grid.Children.Add(body);
        return FloatingCard(grid, _floatingNoteCardBrush);
    }

    private Border FloatingCard(UIElement child, Brush background)
    {
        return new Border
        {
            Child = child,
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Background = background,
            BorderBrush = _floatingBorderBrush,
            BorderThickness = new Thickness(1)
        };
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

        DateTimeOffset? firstReminderUtc = null;
        if (FloatingReminder.SelectedIndex > 0 &&
            !ReminderSchedule.TryCreateFirstDueUtc(
                FloatingReminderDate.SelectedDate,
                FloatingReminderHour.SelectedIndex,
                FloatingReminderMinute.SelectedIndex,
                _session.UtcNow,
                out firstReminderUtc,
                out var reminderError))
        {
            SetNoteMessage(reminderError);
            return;
        }

        NoteReminder reminder;
        if (FloatingReminder.SelectedIndex == 0)
        {
            reminder = NoReminder.Instance;
        }
        else if (FloatingReminder.SelectedIndex == 1)
        {
            reminder = ReminderSchedule.CreateOnce(firstReminderUtc!.Value);
        }
        else if (FloatingReminder.SelectedIndex == 2)
        {
            if (!TryGetReminderInterval(out var interval, out var intervalError))
            {
                SetNoteMessage(intervalError);
                return;
            }

            reminder = ReminderSchedule.CreateRepeating(firstReminderUtc!.Value, interval);
        }
        else
        {
            SetNoteMessage("请选择有效的提醒模式。");
            return;
        }

        if (!_workspaceModule.TrySaveQuickNote(
                FloatingNoteText.Text,
                FloatingNoteDate.SelectedDate,
                FloatingTags.SelectedTagIds.ToArray(),
                reminder,
                out var error))
        {
            SetNoteMessage($"保存失败：{error}");
            return;
        }

        FloatingNoteText.Clear();
        _dateDirty = false;
        _settingDefaultDate = true;
        FloatingNoteDate.SelectedDate = _session.BeijingNow.Date;
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

        var offsetMinutes = 15;
        if (FloatingReminder.SelectedIndex == 2)
        {
            if (!TryGetReminderInterval(out var interval, out _))
            {
                return;
            }

            offsetMinutes = interval;
        }

        SetReminderSchedule(
            ReminderSchedule.GetDefaultFirstDueBeijing(_session.BeijingNow, offsetMinutes));
    }

    private void SetReminderSchedule(DateTimeOffset? beijing)
    {
        FloatingReminderDate.SelectedDate = beijing?.Date;
        FloatingReminderHour.SelectedIndex = beijing?.Hour ?? -1;
        FloatingReminderMinute.SelectedIndex = beijing?.Minute ?? -1;
    }

    private bool TryGetReminderInterval(out int interval, out string error)
    {
        interval = FloatingReminderInterval.SelectedIndex switch
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

        if (!int.TryParse(
                FloatingCustomReminderMinutes.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out interval) ||
            interval is < 1 or > 1440)
        {
            error = "自定义提醒必须是 1 到 1440 分钟。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void SetNoteMessage(string text)
    {
        FloatingNoteMessage.Text = text;
    }

    private void LoadAssets(string assetRoot)
    {
        var plushRoot = Path.Combine(assetRoot, "assets", "floating-plush");
        PlushTopLeft.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "floating-plush-mymelody-front.png"));
        PlushTopRight.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "floating-plush-cinnamoroll-front.png"));
        PlushBottomRight.Source = MainWindow.LoadImage(
            Path.Combine(plushRoot, "floating-plush-kuromi-pose.png"));
    }
}

internal sealed record FloatingThemeExerciseResult(
    IReadOnlyList<string> BoundaryModes,
    IReadOnlyList<string> PanelColors,
    bool UsesChinaStandardTime,
    bool PlushDocksFollowTheme,
    bool TextContrast);

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
    string MyMelodyDockBrushKey,
    string CinnamorollDockBrushKey,
    string KuromiDockBrushKey)
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
        "FloatingDayMyMelodyDockBrush",
        "FloatingDayCinnamorollDockBrush",
        "FloatingDayKuromiDockBrush");

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
        "FloatingNightMyMelodyDockBrush",
        "FloatingNightCinnamorollDockBrush",
        "FloatingNightKuromiDockBrush");

    public static FloatingThemePalette For(DateTimeOffset nowUtc)
    {
        return ApplicationTheme.Resolve(nowUtc) == ApplicationThemeMode.Day ? Daylight : Nighttime;
    }
}
