using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class ReminderCenterWindow : Window
{
    private readonly StackPanel _itemsHost;
    private readonly TextBlock _countText;
    private readonly Dictionary<ActiveReminderOccurrenceKey, ReminderCenterEntry> _entries = new();
    private bool _contentRendered;
    private bool _coordinatorCloseRequested;
    private bool _initialFocusAssigned;

    public ReminderCenterWindow()
    {
        Title = "提醒中心";
        Width = 460;
        Height = 520;
        MinWidth = 400;
        MinHeight = 360;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        AutomationProperties.SetName(this, "提醒中心");

        var heading = new TextBlock
        {
            Text = "提醒中心",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _countText = new TextBlock
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };
        _countText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };
        header.Children.Add(heading);
        header.Children.Add(_countText);

        _itemsHost = new StackPanel();
        var scroller = new ScrollViewer
        {
            Content = _itemsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        AutomationProperties.SetName(scroller, "提醒条目列表");

        var layout = new Grid { Margin = new Thickness(18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(scroller, 1);
        layout.Children.Add(header);
        layout.Children.Add(scroller);

        var surface = new Border
        {
            Child = layout,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        surface.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        Content = surface;

        ContentRendered += (_, _) =>
        {
            _contentRendered = true;
            foreach (var entry in _entries.Values)
            {
                ScheduleRenderedProbe(entry);
            }
        };
        Closing += (_, args) =>
        {
            if (_entries.Count > 0 && !_coordinatorCloseRequested)
            {
                args.Cancel = true;
            }
        };
        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 18;
            Top = workArea.Top + 18;
        };
        UpdateCount();
    }

    internal event EventHandler<ReminderCenterEntryEventArgs>? EntryRendered;

    internal event EventHandler<ReminderCenterEntryEventArgs>? AcknowledgeRequested;

    internal event EventHandler<ReminderCenterSnoozeEventArgs>? SnoozeRequested;

    internal event EventHandler<ReminderCenterEntryEventArgs>? CompleteRequested;

    internal event EventHandler<ReminderCenterEntryEventArgs>? OpenSourceRequested;

    internal bool SupportsKeyboardNavigation => true;

    internal bool PersistenceFailureVisible =>
        _entries.Values.Any(entry => entry.Error.Visibility == Visibility.Visible);

    internal int EntryCount => _entries.Count;

    internal bool UsesScrollableList => true;

    internal int RenderedEntryCount => _entries.Values.Count(entry => entry.RenderedRaised);

    internal IReadOnlyList<ActiveReminderOccurrenceKey> OrderedKeys =>
        _itemsHost.Children
            .OfType<FrameworkElement>()
            .Select(element => (ActiveReminderOccurrenceKey)element.Tag)
            .ToArray();

    internal bool IsFailureVisible(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) &&
        entry.Error.Visibility == Visibility.Visible;

    internal bool IsAcknowledgeEnabled(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) && entry.Acknowledge.IsEnabled;

    internal bool IsSnoozeEnabled(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) && entry.Snooze.IsEnabled;

    internal bool IsCompleteEnabled(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) && entry.Complete.IsEnabled;

    internal bool IsOpenSourceEnabled(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) && entry.OpenSource.IsEnabled;

    internal bool IsCustomSnoozeErrorVisible(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) &&
        entry.CustomError.Visibility == Visibility.Visible;

    internal void AddEntry(ReminderCenterItem item)
    {
        if (_entries.ContainsKey(item.Key))
        {
            return;
        }

        var source = new TextBlock
        {
            Text = item.Source.Kind == ReminderSourceKind.Countdown ? "倒计时" : "便签",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };
        source.SetResourceReference(TextBlock.ForegroundProperty, "ActiveBrush");
        var body = new TextBlock
        {
            Text = item.Source.DisplayText,
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        var scheduled = new TextBlock
        {
            Text = $"计划：{ChinaStandardTime.ToBeijingTime(item.Key.ScheduledUtc):yyyy年M月d日 HH:mm}（北京时间）",
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13
        };
        scheduled.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var error = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed
        };
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);

        var acknowledge = UiAccessibility.Name(
            new Button
            {
                Content = "知道了",
                IsDefault = true,
                MinWidth = 82,
                IsEnabled = false,
                Style = (Style)FindResource("PrimaryButton")
            },
            $"知道了：{item.Source.DisplayText}");
        var snoozeChoice = UiAccessibility.Name(
            new ComboBox
            {
                MinWidth = 132,
                Margin = new Thickness(0, 0, 8, 0)
            },
            $"稍后提醒时间：{item.Source.DisplayText}");
        foreach (var choice in new[]
                 {
                     (Text: "10 分钟后", Option: ReminderSnoozeOption.TenMinutes),
                     (Text: "30 分钟后", Option: ReminderSnoozeOption.ThirtyMinutes),
                     (Text: "1 小时后", Option: ReminderSnoozeOption.SixtyMinutes),
                     (Text: "明天 09:00", Option: ReminderSnoozeOption.TomorrowAtNine),
                     (Text: "自定义", Option: ReminderSnoozeOption.Custom)
                 })
        {
            snoozeChoice.Items.Add(new ComboBoxItem
            {
                Content = choice.Text,
                Tag = choice.Option
            });
        }

        snoozeChoice.SelectedIndex = 0;
        var customDate = UiAccessibility.Name(
            new DatePicker
            {
                Width = 150,
                Margin = new Thickness(0, 8, 8, 0),
                Visibility = Visibility.Collapsed
            },
            $"自定义稍后日期：{item.Source.DisplayText}");
        var customHour = UiAccessibility.Name(
            new ComboBox
            {
                Width = 68,
                Margin = new Thickness(0, 8, 6, 0),
                ItemsSource = Enumerable.Range(0, 24).Select(static value => value.ToString("00")).ToArray(),
                Visibility = Visibility.Collapsed
            },
            $"自定义稍后小时：{item.Source.DisplayText}");
        var customMinute = UiAccessibility.Name(
            new ComboBox
            {
                Width = 68,
                Margin = new Thickness(0, 8, 0, 0),
                ItemsSource = Enumerable.Range(0, 60).Select(static value => value.ToString("00")).ToArray(),
                Visibility = Visibility.Collapsed
            },
            $"自定义稍后分钟：{item.Source.DisplayText}");
        var customFields = new WrapPanel { Visibility = Visibility.Collapsed };
        customFields.Children.Add(customDate);
        customFields.Children.Add(customHour);
        customFields.Children.Add(customMinute);
        var customError = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed
        };
        customError.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(customError, AutomationLiveSetting.Assertive);
        var snooze = UiAccessibility.Name(
            new Button
            {
                Content = "稍后提醒",
                MinWidth = 96,
                IsEnabled = false
            },
            $"稍后提醒：{item.Source.DisplayText}");
        var complete = UiAccessibility.Name(
            new Button
            {
                Content = "完成便签",
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 96,
                IsEnabled = false
            },
            $"完成便签：{item.Source.DisplayText}");
        var snoozeActions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        snoozeActions.Children.Add(snoozeChoice);
        snoozeActions.Children.Add(snooze);
        var openSource = UiAccessibility.Name(
            new Button
            {
                Content = "打开来源",
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 96
            },
            $"打开{(item.Source.Kind == ReminderSourceKind.Countdown ? "倒计时" : "便签")}来源：{item.Source.DisplayText}");
        var actions = new WrapPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(acknowledge);
        if (item.CanComplete)
        {
            actions.Children.Add(complete);
        }
        actions.Children.Add(openSource);

        var content = new StackPanel();
        content.Children.Add(source);
        content.Children.Add(body);
        content.Children.Add(scheduled);
        content.Children.Add(error);
        content.Children.Add(snoozeActions);
        content.Children.Add(customFields);
        content.Children.Add(customError);
        content.Children.Add(actions);
        var row = new Border
        {
            Tag = item.Key,
            Child = content,
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        row.SetResourceReference(Border.BackgroundProperty, "NoteBrush");
        row.SetResourceReference(Border.BorderBrushProperty, "NoteLineBrush");
        AutomationProperties.SetName(
            row,
            $"{(item.Source.Kind == ReminderSourceKind.Countdown ? "倒计时" : "便签")}提醒：{item.Source.DisplayText}；计划 {ChinaStandardTime.ToBeijingTime(item.Key.ScheduledUtc):yyyy年M月d日 HH:mm} 北京时间");

        var entry = new ReminderCenterEntry(
            item,
            row,
            acknowledge,
            snooze,
            complete,
            snoozeChoice,
            customFields,
            customDate,
            customHour,
            customMinute,
            customError,
            openSource,
            error);
        acknowledge.Click += (_, _) =>
            AcknowledgeRequested?.Invoke(this, new ReminderCenterEntryEventArgs(item.Key));
        snoozeChoice.SelectionChanged += (_, _) =>
        {
            var isCustom = SelectedSnoozeOption(entry) == ReminderSnoozeOption.Custom;
            customFields.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            if (!isCustom)
            {
                ClearCustomError(entry);
            }
        };
        snooze.Click += (_, _) => RequestSnooze(entry);
        if (item.CanComplete)
        {
            complete.Click += (_, _) =>
                CompleteRequested?.Invoke(this, new ReminderCenterEntryEventArgs(item.Key));
        }
        openSource.Click += (_, _) =>
            OpenSourceRequested?.Invoke(this, new ReminderCenterEntryEventArgs(item.Key));
        row.Loaded += (_, _) => ScheduleRenderedProbe(entry);
        _entries.Add(item.Key, entry);

        var insertionIndex = _itemsHost.Children
            .OfType<Border>()
            .Select((candidate, index) => (candidate, index))
            .FirstOrDefault(pair =>
                CompareItems(item, _entries[(ActiveReminderOccurrenceKey)pair.candidate.Tag].Item) < 0)
            .index;
        if (insertionIndex == 0 && _itemsHost.Children.Count > 0 &&
            CompareItems(
                item,
                _entries[(ActiveReminderOccurrenceKey)((Border)_itemsHost.Children[0]).Tag].Item) >= 0)
        {
            insertionIndex = _itemsHost.Children.Count;
        }

        _itemsHost.Children.Insert(insertionIndex, row);
        UpdateCount();
        if (_contentRendered)
        {
            ScheduleRenderedProbe(entry);
        }
    }

    internal void RemoveEntry(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.Remove(key, out var entry))
        {
            return;
        }

        _itemsHost.Children.Remove(entry.Row);
        UpdateCount();
    }

    internal void SetDeliveryReady(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return;
        }

        entry.Error.Visibility = Visibility.Collapsed;
        entry.Error.Text = string.Empty;
        entry.Acknowledge.IsEnabled = true;
        entry.Snooze.IsEnabled = true;
        entry.Complete.IsEnabled = entry.Item.CanComplete;
        EnsureInitialFocus();
    }

    internal void ShowDeliveryFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            ShowFailure(entry, "送达状态保存失败，正在自动重试。打开来源仍然可用。");
        }
    }

    internal void ShowAcknowledgementFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            ShowFailure(entry, "“知道了”状态保存失败，正在自动重试。条目仍保留在提醒中心。");
        }
    }

    internal void ShowSnoozeFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            ShowFailure(entry, "稍后提醒状态保存失败，正在自动重试。条目仍保留在提醒中心。");
        }
    }

    internal void ShowCompletionFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            ShowFailure(entry, "完成便签状态保存失败，正在自动重试。便签与提醒条目均保持不变。");
        }
    }

    internal void ShowCustomSnoozeError(ActiveReminderOccurrenceKey key, string message)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.Error.Text = string.Empty;
            entry.Error.Visibility = Visibility.Collapsed;
            entry.CustomError.Text = message;
            entry.CustomError.Visibility = Visibility.Visible;
            AutomationProperties.SetName(entry.CustomError, message);
            entry.Acknowledge.IsEnabled = true;
            entry.Snooze.IsEnabled = true;
            entry.Complete.IsEnabled = entry.Item.CanComplete;
        }
    }

    internal bool RequestFirstAcknowledgement()
    {
        var entry = _itemsHost.Children
            .OfType<Border>()
            .Select(row => _entries[(ActiveReminderOccurrenceKey)row.Tag])
            .FirstOrDefault(candidate => candidate.Acknowledge.IsEnabled);
        if (entry is null)
        {
            return false;
        }

        entry.Acknowledge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal bool RequestAcknowledgement(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.TryGetValue(key, out var entry) || !entry.Acknowledge.IsEnabled)
        {
            return false;
        }

        entry.Acknowledge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal bool RequestSnooze(
        ActiveReminderOccurrenceKey key,
        ReminderSnoozeOption option,
        DateTimeOffset? customBeijing = null)
    {
        if (!_entries.TryGetValue(key, out var entry) || !entry.Snooze.IsEnabled)
        {
            return false;
        }

        entry.SnoozeChoice.SelectedItem = entry.SnoozeChoice.Items
            .OfType<ComboBoxItem>()
            .Single(item => Equals(item.Tag, option));
        if (option == ReminderSnoozeOption.Custom && customBeijing is { } custom)
        {
            var beijing = ChinaStandardTime.ToBeijingTime(custom);
            entry.CustomDate.SelectedDate = beijing.Date;
            entry.CustomHour.SelectedIndex = beijing.Hour;
            entry.CustomMinute.SelectedIndex = beijing.Minute;
        }

        entry.Snooze.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal bool RequestComplete(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.TryGetValue(key, out var entry) || !entry.Complete.IsEnabled)
        {
            return false;
        }

        entry.Complete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal bool RequestOpenSource(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        entry.OpenSource.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    internal void CloseWhenEmpty()
    {
        if (_entries.Count != 0)
        {
            throw new InvalidOperationException("提醒中心仍有条目时不能自动关闭。");
        }

        ForceClose();
    }

    internal void ForceClose()
    {
        _coordinatorCloseRequested = true;
        try
        {
            Close();
        }
        finally
        {
            _coordinatorCloseRequested = false;
        }
    }

    private void ScheduleRenderedProbe(ReminderCenterEntry entry)
    {
        if (!_contentRendered || entry.RenderedRaised || entry.RenderProbeScheduled)
        {
            return;
        }

        entry.RenderProbeScheduled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                entry.RenderProbeScheduled = false;
                if (entry.RenderedRaised ||
                    !_entries.ContainsKey(entry.Item.Key) ||
                    PresentationSource.FromVisual(entry.Row) is null)
                {
                    return;
                }

                entry.Row.UpdateLayout();
                if (entry.Row.ActualWidth <= 0 || entry.Row.ActualHeight <= 0)
                {
                    ScheduleRenderedProbe(entry);
                    return;
                }

                entry.RenderedRaised = true;
                EntryRendered?.Invoke(
                    this,
                    new ReminderCenterEntryEventArgs(entry.Item.Key));
                EnsureInitialFocus();
            }));
    }

    private void EnsureInitialFocus()
    {
        if (_initialFocusAssigned || !IsVisible)
        {
            return;
        }

        var first = _itemsHost.Children
            .OfType<Border>()
            .Select(row => _entries[(ActiveReminderOccurrenceKey)row.Tag])
            .FirstOrDefault();
        var target = first?.Acknowledge.IsEnabled == true
            ? first.Acknowledge
            : first?.OpenSource;
        if (target is null)
        {
            return;
        }

        Activate();
        Keyboard.Focus(target);
        _initialFocusAssigned = true;
    }

    private static void ShowFailure(ReminderCenterEntry entry, string message)
    {
        entry.Error.Text = message;
        entry.Error.Visibility = Visibility.Visible;
        AutomationProperties.SetName(entry.Error, message);
        entry.Acknowledge.IsEnabled = false;
        entry.Snooze.IsEnabled = false;
        entry.Complete.IsEnabled = false;
    }

    private void RequestSnooze(ReminderCenterEntry entry)
    {
        var option = SelectedSnoozeOption(entry);
        DateTimeOffset? customBeijing = null;
        if (option == ReminderSnoozeOption.Custom)
        {
            if (entry.CustomDate.SelectedDate is not { } date ||
                entry.CustomHour.SelectedIndex < 0 ||
                entry.CustomMinute.SelectedIndex < 0)
            {
                ShowCustomSnoozeError(entry.Item.Key, "请选择完整的自定义稍后日期和时间。");
                return;
            }

            customBeijing = ChinaStandardTime.ToBeijingTime(
                CountdownFormatting.ToUtc(
                    date,
                    entry.CustomHour.SelectedIndex,
                    entry.CustomMinute.SelectedIndex,
                    0));
        }

        ClearCustomError(entry);
        SnoozeRequested?.Invoke(
            this,
            new ReminderCenterSnoozeEventArgs(entry.Item.Key, option, customBeijing));
    }

    private static ReminderSnoozeOption SelectedSnoozeOption(ReminderCenterEntry entry) =>
        entry.SnoozeChoice.SelectedItem is ComboBoxItem { Tag: ReminderSnoozeOption option }
            ? option
            : ReminderSnoozeOption.TenMinutes;

    private static void ClearCustomError(ReminderCenterEntry entry)
    {
        entry.CustomError.Text = string.Empty;
        entry.CustomError.Visibility = Visibility.Collapsed;
    }

    private void UpdateCount()
    {
        _countText.Text = $"{_entries.Count} 条";
        AutomationProperties.SetName(_countText, $"共 {_entries.Count} 条提醒");
    }

    private static int CompareItems(ReminderCenterItem left, ReminderCenterItem right)
    {
        var result = left.Key.ScheduledUtc.CompareTo(right.Key.ScheduledUtc);
        if (result != 0)
        {
            return result;
        }

        result = left.RecordOrder.CompareTo(right.RecordOrder);
        if (result != 0)
        {
            return result;
        }

        result = left.RuleOrder.CompareTo(right.RuleOrder);
        return result != 0
            ? result
            : string.Compare(left.Key.RuleId, right.Key.RuleId, StringComparison.Ordinal);
    }

    private sealed class ReminderCenterEntry(
        ReminderCenterItem item,
        Border row,
        Button acknowledge,
        Button snooze,
        Button complete,
        ComboBox snoozeChoice,
        WrapPanel customFields,
        DatePicker customDate,
        ComboBox customHour,
        ComboBox customMinute,
        TextBlock customError,
        Button openSource,
        TextBlock error)
    {
        public ReminderCenterItem Item { get; } = item;

        public Border Row { get; } = row;

        public Button Acknowledge { get; } = acknowledge;

        public Button Snooze { get; } = snooze;

        public Button Complete { get; } = complete;

        public ComboBox SnoozeChoice { get; } = snoozeChoice;

        public WrapPanel CustomFields { get; } = customFields;

        public DatePicker CustomDate { get; } = customDate;

        public ComboBox CustomHour { get; } = customHour;

        public ComboBox CustomMinute { get; } = customMinute;

        public TextBlock CustomError { get; } = customError;

        public Button OpenSource { get; } = openSource;

        public TextBlock Error { get; } = error;

        public bool RenderProbeScheduled { get; set; }

        public bool RenderedRaised { get; set; }
    }
}

internal sealed class ReminderCenterEntryEventArgs(ActiveReminderOccurrenceKey key) : EventArgs
{
    public ActiveReminderOccurrenceKey Key { get; } = key;
}

internal sealed class ReminderCenterSnoozeEventArgs(
    ActiveReminderOccurrenceKey key,
    ReminderSnoozeOption option,
    DateTimeOffset? customBeijing) : EventArgs
{
    public ActiveReminderOccurrenceKey Key { get; } = key;

    public ReminderSnoozeOption Option { get; } = option;

    public DateTimeOffset? CustomBeijing { get; } = customBeijing;
}

internal sealed record ReminderCenterItem(
    ActiveReminderOccurrenceKey Key,
    ReminderSource Source,
    int RecordOrder,
    int RuleOrder)
{
    public ReminderCenterItem(
        ActiveReminderOccurrenceKey key,
        NoteRecord note,
        int recordOrder,
        int ruleOrder)
        : this(key, new NoteReminderSource(note), recordOrder, ruleOrder)
    {
    }

    public bool CanComplete => Source.Kind == ReminderSourceKind.Note;
}
