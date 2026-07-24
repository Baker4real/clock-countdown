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
    private readonly HolidayCalendar _calendar;
    private readonly MainWindow _manager;
    private WorkspaceKind _workspace = WorkspaceKind.Countdowns;
    private bool _settingDefaultDate;
    private bool _dateDirty;

    internal FloatingWindow(
        ApplicationSession session,
        HolidayCalendar calendar,
        MainWindow manager,
        string assetRoot)
    {
        _session = session;
        _calendar = calendar;
        _manager = manager;
        InitializeComponent();
        LoadAssets(assetRoot);
        ConfigureControls();
        WireEvents();
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
            FloatingTags.SetSelection(_session.Filter.SelectedTagIds);
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
        if (!_dateDirty)
        {
            _settingDefaultDate = true;
            FloatingNoteDate.SelectedDate = ChinaStandardTime.ToBeijingTime(nowUtc).Date;
            _settingDefaultDate = false;
        }

        RenderRows(nowUtc);
    }

    internal void Reanchor()
    {
        var workArea = SystemParameters.WorkArea;
        MaxHeight = Math.Max(280, workArea.Height - 24);
        Left = Math.Max(workArea.Left + 12, workArea.Right - ActualWidth - 12);
        Top = Math.Max(workArea.Top + 12, workArea.Bottom - ActualHeight - 12);
    }

    private void ConfigureControls()
    {
        FloatingReminder.Items.Add("关闭提醒");
        FloatingReminder.Items.Add("15 分钟");
        FloatingReminder.Items.Add("30 分钟");
        FloatingReminder.Items.Add("60 分钟");
        FloatingReminder.SelectedIndex = 0;
        FloatingTags.SetTags(_session.Current.Tags);
        AutomationProperties.SetName(FloatingCountdownsButton, "显示悬浮倒计时");
        AutomationProperties.SetName(FloatingNotesButton, "显示悬浮便签");
        AutomationProperties.SetName(OpenManagerButton, "打开管理窗口");
        AutomationProperties.SetName(CloseFloatingButton, "退出应用");
        AutomationProperties.SetName(FloatingNoteText, "快速便签内容");
        AutomationProperties.SetName(FloatingNoteDate, "快速便签日期");
        AutomationProperties.SetName(FloatingReminder, "快速便签提醒");
        AutomationProperties.SetName(FloatingAddNote, "添加快速便签");
        ThemedDatePickerBehavior.Attach(FloatingNoteDate);
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
        FloatingAddNote.Click += (_, _) => AddNote();
        FloatingTags.CreateRequested += (_, name) =>
        {
            if (_session.TryCreateTag(
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
        FloatingTags.SetTags(_session.Current.Tags);
        RenderRows(_session.UtcNow);
        Reanchor();
    }

    private void RenderRows(DateTimeOffset nowUtc)
    {
        FloatingRows.Children.Clear();
        if (_workspace == WorkspaceKind.Countdowns)
        {
            var records = _session.GetSortedCountdowns()
                .Where(record => record.TargetUtc > nowUtc)
                .ToList();
            if (!_session.Filter.HasConstraints &&
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
            var notes = _session.GetSortedNotes().Where(note => !note.IsCompleted).ToArray();
            foreach (var note in notes)
            {
                FloatingRows.Children.Add(FloatingNoteCard(note));
            }

            if (notes.Length == 0)
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
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = CountdownFormatting.FormatRemaining(
                record.TargetUtc,
                nowUtc,
                _session.Current.Preferences.DisplayPrecision),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("BodyBrush")
        });
        return FloatingCard(stack, "CardBrush");
    }

    private FrameworkElement FloatingNoteCard(NoteRecord note)
    {
        var check = UiAccessibility.Name(
            new CheckBox
            {
                Tag = note.Id,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0)
            },
            $"完成便签 {note.Text}");
        check.Checked += (_, _) =>
        {
            if (!_session.TrySetNoteCompletion(note.Id, true, out var error))
            {
                check.IsChecked = false;
                SetNoteMessage(error);
            }
        };
        var text = new TextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(text, 1);
        grid.Children.Add(check);
        grid.Children.Add(text);
        return FloatingCard(grid, "NoteBrush");
    }

    private Border FloatingCard(UIElement child, string background)
    {
        return new Border
        {
            Child = child,
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource(background),
            BorderBrush = (Brush)FindResource("PeachLineBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private TextBlock EmptyText(string text) => new()
    {
        Text = text,
        Margin = new Thickness(2, 8, 2, 8),
        HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = (Brush)FindResource("MutedBrush")
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

        var interval = FloatingReminder.SelectedIndex switch
        {
            1 => 15,
            2 => 30,
            3 => 60,
            _ => (int?)null
        };
        if (!_session.TrySaveNote(
                null,
                FloatingNoteText.Text,
                FloatingNoteDate.SelectedDate,
                false,
                FloatingTags.SelectedTagIds.ToArray(),
                interval,
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
        SetNoteMessage(string.Empty);
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
