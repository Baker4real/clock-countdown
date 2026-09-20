using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MinminCountdown.Core;
using Path = System.Windows.Shapes.Path;

namespace MinminCountdown;

internal sealed class ReminderCenterWindow : Window
{
    private readonly StackPanel _itemsHost;
    private readonly TextBlock _countText;
    private readonly Button _acknowledgeAll;
    private readonly Dictionary<ActiveReminderOccurrenceKey, ReminderCenterEntry> _entries = new();
    private readonly Border _dragHeader;
    private bool _contentRendered;
    private bool _coordinatorCloseRequested;
    private bool _initialFocusAssigned;
    private bool _hasManualWindowPlacement;
    private int _nativeWindowDragRequestCount;
    private int _nativeWindowDragCompletionCount;

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
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowTitleFontSize");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _countText = new TextBlock
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _countText.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
        _countText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(heading);
        titleRow.Children.Add(_countText);
        _acknowledgeAll = UiAccessibility.Name(
            new Button
            {
                Content = CreateIconTextContent(CreateBellIcon("InkBrush"), "全部已读"),
                IsEnabled = false,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            },
            "全部已读：当前全部提醒");
        AutomationProperties.SetAutomationId(_acknowledgeAll, "ReminderCenterAcknowledgeAll");
        _acknowledgeAll.Click += (_, _) => AcknowledgeAllRequested?.Invoke(this, EventArgs.Empty);
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_acknowledgeAll, 1);
        headerGrid.Children.Add(titleRow);
        headerGrid.Children.Add(_acknowledgeAll);
        _dragHeader = new Border
        {
            Child = headerGrid,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(0, 4, 0, 4)
        };
        AutomationProperties.SetAutomationId(_dragHeader, "ReminderCenterDragHeader");
        _dragHeader.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;

        _itemsHost = new StackPanel();
        var scroller = new ScrollViewer
        {
            Content = _itemsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        AutomationProperties.SetName(scroller, "提醒条目列表");

        var layout = new Grid { Margin = new Thickness(14) };
        layout.SetResourceReference(TextElement.FontSizeProperty, "SecondaryWindowBodyFontSize");
        AutomationProperties.SetAutomationId(layout, "ReminderCenterContentRoot");
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(scroller, 1);
        layout.Children.Add(_dragHeader);
        layout.Children.Add(scroller);

        var surface = new Border
        {
            Child = layout,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        surface.SetResourceReference(Border.BackgroundProperty, "ReminderPageBrush");
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
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter ||
                Keyboard.FocusedElement is not Button { IsEnabled: true } focused ||
                !_itemsHost.IsAncestorOf(focused))
            {
                return;
            }

            args.Handled = true;
            focused.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, focused));
        };
        // #150：有条目时 Esc 不关闭提醒中心，只把焦点移到首条目的「知道了」；
        // 「知道了」被失败态禁用时落到始终可用的「打开来源」。
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || _entries.Count == 0)
            {
                return;
            }

            args.Handled = true;
            FocusEscapeTarget();
        };
        Loaded += (_, _) => ApplyAutomaticPlacementIfNeeded();
        UpdateCount();
    }

    internal bool HasManualWindowPlacement => _hasManualWindowPlacement;

    internal int NativeWindowDragRequestCount => _nativeWindowDragRequestCount;

    internal int NativeWindowDragCompletionCount => _nativeWindowDragCompletionCount;

    internal Border DragHeader => _dragHeader;

    internal void ReapplyPlacement() => ApplyAutomaticPlacementIfNeeded();

    private void ApplyAutomaticPlacementIfNeeded()
    {
        if (_hasManualWindowPlacement)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 18;
        Top = workArea.Top + 18;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        SecondaryWindowChrome.DragFromHeader(
            this,
            eventArgs,
            _dragHeader,
            () => _hasManualWindowPlacement = true,
            ref _nativeWindowDragRequestCount,
            ref _nativeWindowDragCompletionCount);
    }

    internal event EventHandler<ReminderCenterEntryEventArgs>? EntryRendered;

    internal event EventHandler<ReminderCenterEntryEventArgs>? AcknowledgeRequested;

    internal event EventHandler<EventArgs>? AcknowledgeAllRequested;

    internal event EventHandler<ReminderCenterSnoozeEventArgs>? SnoozeRequested;

    internal event EventHandler<ReminderCenterEntryEventArgs>? CompleteRequested;

    internal event EventHandler<ReminderCenterEntryEventArgs>? OpenSourceRequested;

    internal bool PersistenceFailureVisible =>
        _entries.Values.Any(entry => entry.Error.Visibility == Visibility.Visible);

    internal int EntryCount => _entries.Count;

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

    internal bool IsAcknowledgeAllEnabled => _acknowledgeAll.IsEnabled;

    internal bool RemoveEntryThrowsForValidation { get; set; }

    internal void AddEntry(ReminderCenterItem item)
    {
        if (_entries.ContainsKey(item.Key))
        {
            return;
        }

        var isCountdown = item.Source.Kind == ReminderSourceKind.Countdown;
        var sourceLabel = isCountdown ? "倒计时" : "便签";
        var sourceIcon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = isCountdown
                ? CreateClockIcon("PinkAccentStrongBrush", 17)
                : CreateNoteIcon("WarningAccentBrush")
        };
        if (isCountdown)
        {
            sourceIcon.BorderThickness = new Thickness(1);
            sourceIcon.SetResourceReference(Border.BackgroundProperty, "PinkTintSoftBrush");
            sourceIcon.SetResourceReference(Border.BorderBrushProperty, "PinkAccentStrongBrush");
        }
        else
        {
            sourceIcon.SetResourceReference(Border.BackgroundProperty, "SourceNoteIconBrush");
        }
        AutomationProperties.SetAutomationId(sourceIcon, "ReminderEntrySourceIcon");
        // #150：来源图标是纯装饰，不携带可访问名（不作为独立控件朗读）；
        // 来源名称由整行可访问名与行内「便签 / 倒计时」标签承担。
        var source = new TextBlock
        {
            Text = sourceLabel,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        source.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowSectionFontSize");
        source.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        var pendingBadge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2, 8, 2),
            Child = CreateIconTextContent(CreateWarningIcon(), "待处理", 5)
        };
        pendingBadge.SetResourceReference(Border.BackgroundProperty, "WarningTintBrush");
        pendingBadge.SetResourceReference(TextElement.ForegroundProperty, "WarningAccentBrush");
        pendingBadge.SetResourceReference(TextElement.FontSizeProperty, "SecondaryWindowMinorFontSize");
        AutomationProperties.SetAutomationId(pendingBadge, "ReminderEntryPendingBadge");
        UiAccessibility.Name(pendingBadge, "待处理");
        var sourceRow = new Grid();
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition());
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pendingBadge, 1);
        sourceRow.Children.Add(source);
        sourceRow.Children.Add(pendingBadge);
        var body = new TextBlock
        {
            Text = item.Source.DisplayText,
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        var scheduled = new TextBlock
        {
            Text = $"计划：{ChinaStandardTime.ToBeijingTime(item.Key.ScheduledUtc):yyyy年M月d日 HH:mm}（北京时间）",
            VerticalAlignment = VerticalAlignment.Center
        };
        scheduled.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var scheduledClock = CreateClockIcon("MutedBrush", 13);
        scheduledClock.Margin = new Thickness(0, 0, 5, 0);
        scheduledClock.VerticalAlignment = VerticalAlignment.Center;
        var scheduledRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0)
        };
        scheduledRow.Children.Add(scheduledClock);
        scheduledRow.Children.Add(scheduled);
        var error = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed
        };
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 12, 0, 0)
        };
        divider.SetResourceReference(Border.BackgroundProperty, "NoteLineBrush");

        var acknowledge = UiAccessibility.Name(
            new Button
            {
                Content = CreateIconTextContent(CreateCheckCircleIcon("MainActionTextBrush"), "知道了"),
                MinWidth = 82,
                Margin = new Thickness(0, 0, 7, 0),
                Padding = new Thickness(8, 0, 8, 0),
                IsEnabled = false,
                Style = (Style)FindResource("PrimaryButton")
            },
            $"知道了：{item.Source.DisplayText}");
        var snoozeChoice = UiAccessibility.Name(
            new ComboBox
            {
                MinWidth = 132,
                Padding = new Thickness(28, 0, 0, 0)
            },
            $"稍后提醒时间：{item.Source.DisplayText}");
        snoozeChoice.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
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
        ThemedComboBoxBehavior.Attach(snoozeChoice);
        var snoozeFrame = new Grid
        {
            Margin = new Thickness(0, 0, 7, 0)
        };
        AutomationProperties.SetAutomationId(snoozeFrame, "ReminderEntrySnoozeChoiceFrame");
        var snoozeClock = CreateClockIcon("InkBrush", 15);
        snoozeClock.IsHitTestVisible = false;
        snoozeClock.HorizontalAlignment = HorizontalAlignment.Left;
        snoozeClock.VerticalAlignment = VerticalAlignment.Center;
        snoozeClock.Margin = new Thickness(10, 0, 0, 0);
        snoozeFrame.Children.Add(snoozeChoice);
        snoozeFrame.Children.Add(snoozeClock);
        var customDate = UiAccessibility.Name(
            new DatePicker
            {
                Width = 150,
                Margin = new Thickness(0, 8, 8, 0),
                Visibility = Visibility.Collapsed
            },
            $"自定义稍后日期：{item.Source.DisplayText}");
        ThemedDatePickerBehavior.Attach(customDate);
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
        ThemedComboBoxBehavior.Attach(customHour);
        ThemedComboBoxBehavior.Attach(customMinute);
        var customFields = new WrapPanel { Visibility = Visibility.Collapsed };
        customFields.Children.Add(customDate);
        customFields.Children.Add(customHour);
        customFields.Children.Add(customMinute);
        var customError = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed
        };
        customError.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(customError, AutomationLiveSetting.Assertive);
        var snooze = UiAccessibility.Name(
            new Button
            {
                Content = CreateIconTextContent(CreateBellIcon("InkBrush"), "稍后提醒"),
                MinWidth = 96,
                Padding = new Thickness(8, 0, 8, 0),
                IsEnabled = false
            },
            $"稍后提醒：{item.Source.DisplayText}");
        var complete = UiAccessibility.Name(
            new Button
            {
                Content = CreateIconTextContent(CreateCheckCircleIcon("InkBrush"), "完成便签"),
                MinWidth = 96,
                Padding = new Thickness(11, 0, 11, 0),
                IsEnabled = false
            },
            $"完成便签：{item.Source.DisplayText}");
        var openSource = UiAccessibility.Name(
            new Button
            {
                Content = CreateIconTextContent(CreateExternalLinkIcon("InkBrush"), "打开来源"),
                MinWidth = 96,
                Padding = new Thickness(11, 0, 11, 0)
            },
            $"打开{(isCountdown ? "倒计时" : "便签")}来源：{item.Source.DisplayText}");
        var primaryActions = new WrapPanel
        {
            Margin = new Thickness(0, 10, 0, 0)
        };
        AutomationProperties.SetAutomationId(primaryActions, "ReminderEntryActionsPrimaryRow");
        primaryActions.Children.Add(acknowledge);
        primaryActions.Children.Add(snoozeFrame);
        primaryActions.Children.Add(snooze);
        var secondaryActions = new WrapPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        AutomationProperties.SetAutomationId(secondaryActions, "ReminderEntryActionsSecondaryRow");
        if (item.CanComplete)
        {
            complete.Margin = new Thickness(0, 0, 8, 0);
            secondaryActions.Children.Add(complete);
        }
        secondaryActions.Children.Add(openSource);

        var content = new StackPanel();
        Grid.SetColumn(content, 1);
        content.Children.Add(sourceRow);
        content.Children.Add(body);
        content.Children.Add(scheduledRow);
        content.Children.Add(error);
        content.Children.Add(divider);
        content.Children.Add(primaryActions);
        content.Children.Add(secondaryActions);
        content.Children.Add(customFields);
        content.Children.Add(customError);
        var cardGrid = new Grid();
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition());
        cardGrid.Children.Add(sourceIcon);
        cardGrid.Children.Add(content);
        var row = new Border
        {
            Tag = item.Key,
            Child = cardGrid,
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        row.SetResourceReference(Border.BackgroundProperty, "ReminderCardBrush");
        row.SetResourceReference(Border.BorderBrushProperty, "NoteLineBrush");
        UiAccessibility.Name(
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
            var customVisibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            customFields.Visibility = customVisibility;
            customDate.Visibility = customVisibility;
            customHour.Visibility = customVisibility;
            customMinute.Visibility = customVisibility;
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
        UpdateAcknowledgeAllEnabled();
        if (_contentRendered)
        {
            ScheduleRenderedProbe(entry);
        }
    }

    internal void RemoveEntry(ActiveReminderOccurrenceKey key)
    {
        if (RemoveEntryThrowsForValidation)
        {
            throw new InvalidOperationException("注入的条目回滚失败。");
        }

        if (!_entries.Remove(key, out var entry))
        {
            return;
        }

        _itemsHost.Children.Remove(entry.Row);
        UpdateCount();
        UpdateAcknowledgeAllEnabled();
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
        UpdateAcknowledgeAllEnabled();
        EnsureInitialFocus();
    }

    internal void ShowDeliveryFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            ShowFailure(entry, "送达状态保存失败，正在自动重试。打开来源仍然可用。");
            UpdateAcknowledgeAllEnabled();
        }
    }

    internal void ShowAcknowledgementFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            ShowFailure(entry, "“知道了”状态保存失败，正在自动重试。条目仍保留在提醒中心。");
            UpdateAcknowledgeAllEnabled();
        }
    }

    internal void ShowSnoozeFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            ShowFailure(entry, "稍后提醒状态保存失败，正在自动重试。条目仍保留在提醒中心。");
            UpdateAcknowledgeAllEnabled();
        }
    }

    internal void ShowCompletionFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            ShowFailure(entry, "完成便签状态保存失败，正在自动重试。便签与提醒条目均保持不变。");
            UpdateAcknowledgeAllEnabled();
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
            UpdateAcknowledgeAllEnabled();
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

    internal bool RequestAcknowledgeAll()
    {
        if (!_acknowledgeAll.IsEnabled)
        {
            return false;
        }

        _acknowledgeAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

    private void FocusEscapeTarget()
    {
        var entry = _itemsHost.Children
            .OfType<Border>()
            .Select(row => _entries[(ActiveReminderOccurrenceKey)row.Tag])
            .FirstOrDefault();
        if (entry is null)
        {
            return;
        }

        Keyboard.Focus(entry.Acknowledge.IsEnabled ? entry.Acknowledge : entry.OpenSource);
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

    private void UpdateAcknowledgeAllEnabled() =>
        _acknowledgeAll.IsEnabled = _entries.Values.Any(entry => entry.Acknowledge.IsEnabled);

    private static StackPanel CreateIconTextContent(
        FrameworkElement icon,
        string text,
        double spacing = 8)
    {
        icon.VerticalAlignment = VerticalAlignment.Center;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(spacing, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static FrameworkElement CreateCheckCircleIcon(string strokeKey)
    {
        var circle = new Ellipse
        {
            Width = 15,
            Height = 15,
            StrokeThickness = 1.7
        };
        circle.SetResourceReference(Shape.StrokeProperty, strokeKey);
        var tick = new Path
        {
            Data = Geometry.Parse("M 4.4,7.9 L 6.9,10.4 L 10.9,5.2"),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        tick.SetResourceReference(Shape.StrokeProperty, strokeKey);
        return new Grid
        {
            Width = 15,
            Height = 15,
            Children = { circle, tick }
        };
    }

    private static FrameworkElement CreateBellIcon(string fillKey)
    {
        var path = new Path
        {
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Data = Geometry.Parse(
                "M 12,22 C 13.1,22 14,21.1 14,20 L 10,20 C 10,21.1 10.9,22 12,22 Z " +
                "M 18,16 L 18,11 C 18,7.93 16.37,5.36 13.5,4.68 L 13.5,4 " +
                "C 13.5,3.17 12.83,2.5 12,2.5 C 11.17,2.5 10.5,3.17 10.5,4 L 10.5,4.68 " +
                "C 7.64,5.36 6,7.92 6,11 L 6,16 L 4,18 L 4,19 L 20,19 L 20,18 Z")
        };
        path.SetResourceReference(Shape.FillProperty, fillKey);
        return path;
    }

    private static FrameworkElement CreateClockIcon(string strokeKey, double size)
    {
        var circle = new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 1.6
        };
        circle.SetResourceReference(Shape.StrokeProperty, strokeKey);
        var center = size / 2;
        var hands = new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {center},{center - size * 0.22} L {center},{center} L {center + size * 0.18},{center + size * 0.12}")),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        hands.SetResourceReference(Shape.StrokeProperty, strokeKey);
        return new Grid
        {
            Width = size,
            Height = size,
            Children = { circle, hands }
        };
    }

    private static FrameworkElement CreateExternalLinkIcon(string strokeKey)
    {
        var path = new Path
        {
            Data = Geometry.Parse(
                "M 6.5,3.5 L 3.5,3.5 L 3.5,12.5 L 12.5,12.5 L 12.5,9.5 " +
                "M 8.2,7.8 L 13.5,2.5 M 9.5,2.5 L 13.5,2.5 L 13.5,6.5"),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        path.SetResourceReference(Shape.StrokeProperty, strokeKey);
        return path;
    }

    private static FrameworkElement CreateNoteIcon(string strokeKey)
    {
        var path = new Path
        {
            Data = Geometry.Parse(
                "M 4,2.2 L 9.2,2.2 L 12,5 L 12,13.8 L 4,13.8 Z " +
                "M 9,2.4 L 9,5.2 L 11.8,5.2 M 6.2,8 L 9.8,8 M 6.2,10.4 L 9.8,10.4"),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        path.SetResourceReference(Shape.StrokeProperty, strokeKey);
        return path;
    }

    private static FrameworkElement CreateWarningIcon()
    {
        // #150：感叹号圆图标是矢量图形（描边竖条 + 圆点），不引入第三种字重（不使用 Bold 文本）。
        var circle = new Ellipse
        {
            Width = 13,
            Height = 13
        };
        circle.SetResourceReference(Shape.FillProperty, "WarningAccentBrush");
        var bar = new Path
        {
            Data = Geometry.Parse("M 6.5,3.3 L 6.5,7.3"),
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        bar.SetResourceReference(Shape.StrokeProperty, "MainActionTextBrush");
        var dot = new Ellipse
        {
            Width = 1.9,
            Height = 1.9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1.7)
        };
        dot.SetResourceReference(Shape.FillProperty, "MainActionTextBrush");
        return new Grid
        {
            Width = 13,
            Height = 13,
            Children = { circle, bar, dot }
        };
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
