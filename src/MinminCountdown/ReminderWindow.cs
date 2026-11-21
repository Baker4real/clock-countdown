using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Data;
using System.Runtime.InteropServices;
using MinminCountdown.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Path = System.Windows.Shapes.Path;

namespace MinminCountdown;

internal sealed class ReminderCenterWindow : Window
{
    private const double CollapsedTitleLineHeight = 28;
    private const int CollapsedTitleMaxLines = 2;
    private const double CollapsedTitleMaxHeight = CollapsedTitleLineHeight * CollapsedTitleMaxLines;
    private readonly StackPanel _itemsHost;
    private readonly TextBlock _countText;
    private readonly Button _acknowledgeAll;
    private readonly Button _minimizeToTaskbar;
    private readonly Dictionary<ActiveReminderOccurrenceKey, ReminderCenterEntry> _entries = new();
    private readonly ReminderCenterViewModel _viewModel;
    private readonly Border _dragHeader;
    private readonly BooleanToVisibilityConverter _visibilityConverter = new();
    private bool _contentRendered;
    private bool _coordinatorCloseRequested;
    private bool _initialFocusAssigned;
    private bool _hasManualWindowPlacement;
    private readonly Window? _placementSource;
    private bool _placementInitialized;
    private int _nativeWindowDragRequestCount;
    private int _nativeWindowDragCompletionCount;
    private int _taskbarFlashRequestCount;

    public ReminderCenterWindow(
        Window? placementSource = null,
        DiagnosticLogger? logger = null,
        IDwmWindowChromeHost? dwmChromeHost = null)
    {
        _placementSource = placementSource;
        _viewModel = new ReminderCenterViewModel();
        _viewModel.AcknowledgeAllRequested += (_, args) =>
            AcknowledgeAllRequested?.Invoke(this, args);
        Title = "提醒中心";
        Width = 460;
        Height = 520;
        MinWidth = 400;
        MinHeight = 360;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        // #215：出现时不激活、不抢焦点、不置顶；显示在任务栏且标题带条目数。
        ShowInTaskbar = true;
        ShowActivated = false;
        Topmost = false;
        // #324：改为不透明无边框窗口，向 DWM 请求系统圆角、投影与 1 DIP 暖边。
        SetResourceReference(BackgroundProperty, "ReminderPageBrush");
        DwmWindowChrome.ConfigureOpaqueBorderlessChrome(this);
        DataContext = _viewModel;
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
        _countText.SetBinding(TextBlock.TextProperty, new Binding(nameof(ReminderCenterViewModel.CountText)));
        _countText.SetBinding(
            AutomationProperties.NameProperty,
            new Binding(nameof(ReminderCenterViewModel.AccessibleCountText)));
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
                Content = CreateIconTextContent(CreateBellIcon("InkBrush"), "全部知道了"),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            },
            "全部知道了：当前全部提醒");
        AutomationProperties.SetAutomationId(_acknowledgeAll, "ReminderCenterAcknowledgeAll");
        _acknowledgeAll.SetBinding(
            Button.IsEnabledProperty,
            new Binding(nameof(ReminderCenterViewModel.CanAcknowledgeAll)));
        _acknowledgeAll.SetBinding(
            Button.CommandProperty,
            new Binding(nameof(ReminderCenterViewModel.AcknowledgeAllCommand)));
        // #215：头部右端「收起」把窗口最小化到任务栏；有条目时关闭守卫不变。
        var unitSpacing = SpacingTokens.Resolve(this, SpacingTokens.Unit);
        _minimizeToTaskbar = UiAccessibility.Name(
            new Button
            {
                Content = CreateIconTextContent(CreateMinimizeIcon("InkBrush"), "收起"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(unitSpacing, 0, 0, 0),
                Cursor = Cursors.Hand
            },
            "收起提醒中心到任务栏");
        AutomationProperties.SetAutomationId(_minimizeToTaskbar, "ReminderCenterMinimizeToTaskbar");
        _minimizeToTaskbar.Click += (_, _) => WindowState = WindowState.Minimized;
        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerActions.Children.Add(_acknowledgeAll);
        headerActions.Children.Add(_minimizeToTaskbar);
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerActions, 1);
        headerGrid.Children.Add(titleRow);
        headerGrid.Children.Add(headerActions);
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
        scroller.SetResourceReference(FocusVisualStyleProperty, "MainKeyboardFocusVisual");

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
            Child = layout
        };
        surface.SetResourceReference(Border.BackgroundProperty, "ReminderPageBrush");
        // #324：由 DWM 承担系统圆角/投影与 1 DIP 暖色边框；不支持时自动回退为直角并由内容边界承担 1 DIP 暖边。
        DwmWindowChrome.AttachSecondaryWindow(
            this,
            surface,
            "PeachLineColor",
            "PeachLineBrush",
            DwmWindowChrome.ReminderCenterEvents,
            logger,
            dwmChromeHost);
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
        WindowWorkAreaGeometry.TrackChanges(this, ApplyAutomaticPlacementIfNeeded,
            () => _hasManualWindowPlacement = true);
        // #215：初始焦点只在用户真正激活窗口后落下，出现时不得抢焦点。
        Activated += (_, _) => EnsureInitialFocus();
        DpiChanged += (_, _) =>
        {
            foreach (var entry in _entries.Values)
            {
                entry.RefreshTitleOverflow();
            }
        };
        Closed += (_, _) =>
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.SnoozeMenu.IsOpen)
                {
                    entry.SnoozeMenu.IsOpen = false;
                }
            }
        };
    }

    internal bool HasManualWindowPlacement => _hasManualWindowPlacement;

    internal int NativeWindowDragRequestCount => _nativeWindowDragRequestCount;

    internal int NativeWindowDragCompletionCount => _nativeWindowDragCompletionCount;

    internal Border DragHeader => _dragHeader;

    internal void ReapplyPlacement() => ApplyAutomaticPlacementIfNeeded();

    private void ApplyAutomaticPlacementIfNeeded()
    {
        var monitor = MonitorWorkAreaResolver.ResolveForWindow(
            !_placementInitialized && _placementSource is { } source ? source : this);
        // 首次物理迁移可能同步改变 HWND DPI；用更新后的窗口 DPI 再对齐一次。
        for (var pass = 0; pass < 2; pass++)
        {
            var workArea = MonitorWorkAreaResolver.ConvertWorkAreaToWindowDpi(monitor, this);
            if (!_hasManualWindowPlacement)
            {
                Left = workArea.Right - ActualWidth - 18;
                Top = workArea.Top + 18;
            }
            WindowWorkAreaGeometry.Fit(this, workArea, 18);
        }
        _placementInitialized = MonitorWorkAreaResolver.ResolveForWindow(this) == monitor;
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
        _entries.Values.Any(entry => entry.ViewModel.IsFailureVisible);

    internal int EntryCount => _entries.Count;

    internal int RenderedEntryCount => _entries.Values.Count(entry => entry.RenderedRaised);

    internal IReadOnlyList<ActiveReminderOccurrenceKey> OrderedKeys =>
        _itemsHost.Children
            .OfType<FrameworkElement>()
            .Select(element => (ActiveReminderOccurrenceKey)element.Tag)
            .ToArray();

    internal bool IsFailureVisible(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) &&
        entry.ViewModel.IsFailureVisible;

    internal bool IsAcknowledgeEnabled(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) && entry.ViewModel.IsAcknowledgeEnabled;

    internal bool IsSnoozeEnabled(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) && entry.ViewModel.IsSnoozeEnabled;

    internal bool IsCompleteEnabled(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) && entry.ViewModel.IsCompleteEnabled;

    internal bool IsOpenSourceEnabled(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) && entry.OpenSource.IsEnabled;

    internal bool IsCustomSnoozeErrorVisible(ActiveReminderOccurrenceKey key) =>
        _entries.TryGetValue(key, out var entry) &&
        entry.ViewModel.IsCustomErrorVisible;

    internal bool IsAcknowledgeAllEnabled => _viewModel.CanAcknowledgeAll;

    internal bool RemoveEntryThrowsForValidation { get; set; }

    internal void AddEntry(ReminderCenterItem item)
    {
        if (_entries.ContainsKey(item.Key))
        {
            return;
        }

        var viewModel = new ReminderCenterEntryViewModel(item);
        viewModel.AcknowledgeRequested += OnViewModelAcknowledgeRequested;
        viewModel.SnoozeRequested += OnViewModelSnoozeRequested;
        viewModel.CompleteRequested += OnViewModelCompleteRequested;
        viewModel.OpenSourceRequested += OnViewModelOpenSourceRequested;

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
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        source.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
        source.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        var relativeTimeContent = CreateIconTextContent(CreateClockIcon("WarningAccentBrush", 13), string.Empty, 5);
        ((TextBlock)relativeTimeContent.Children[1]).SetBinding(
            TextBlock.TextProperty, new Binding(nameof(ReminderCenterEntryViewModel.RelativeTimeText)));
        var pendingBadge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2, 8, 2),
            Child = relativeTimeContent
        };
        pendingBadge.SetResourceReference(Border.BackgroundProperty, "WarningTintBrush");
        pendingBadge.SetResourceReference(TextElement.ForegroundProperty, "WarningAccentBrush");
        pendingBadge.SetResourceReference(TextElement.FontSizeProperty, "SecondaryWindowMinorFontSize");
        AutomationProperties.SetAutomationId(pendingBadge, "ReminderEntryPendingBadge");
        pendingBadge.SetBinding(AutomationProperties.NameProperty,
            new Binding(nameof(ReminderCenterEntryViewModel.RelativeTimeText)));
        var sourceRow = new Grid();
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition());
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pendingBadge, 1);
        sourceRow.Children.Add(source);
        sourceRow.Children.Add(pendingBadge);
        var body = new TextBlock
        {
            Text = item.Source.DisplayText,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 0),
            LineHeight = CollapsedTitleLineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            MaxHeight = CollapsedTitleMaxHeight,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        body.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowSectionFontSize");
        body.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        // #321：超过两行的标题以两行省略号收起，并仅在真实溢出时挂载可键盘到达的展开/收起按钮。
        var titleDisclosure = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
            MinWidth = 0,
            MinHeight = 30,
            Padding = new Thickness(10, 0, 10, 0),
            Style = (Style)FindResource("SecondaryButton")
        };
        titleDisclosure.SetResourceReference(Control.FontSizeProperty, "SecondaryWindowMinorFontSize");
        AutomationProperties.SetAutomationId(titleDisclosure, "ReminderEntryTitleDisclosure");
        BindEntry(
            titleDisclosure,
            ContentControl.ContentProperty,
            nameof(ReminderCenterEntryViewModel.TitleDisclosureText),
            viewModel);
        BindEntry(
            titleDisclosure,
            AutomationProperties.NameProperty,
            nameof(ReminderCenterEntryViewModel.TitleDisclosureAccessibleName),
            viewModel);
        BindEntry(
            titleDisclosure,
            AutomationProperties.HelpTextProperty,
            nameof(ReminderCenterEntryViewModel.TitleDisclosureStateText),
            viewModel);
        BindEntry(
            titleDisclosure,
            ButtonBase.CommandProperty,
            nameof(ReminderCenterEntryViewModel.ToggleTitleExpandedCommand),
            viewModel);
        KeyboardFocusPolicy.Apply(titleDisclosure);
        var titleDisclosureSlot = new Border();
        void ApplyTitlePresentation()
        {
            var clamped = viewModel.IsTitleOverflowing && !viewModel.IsTitleExpanded;
            body.MaxHeight = clamped ? CollapsedTitleMaxHeight : double.PositiveInfinity;
            body.TextTrimming = clamped ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            if (viewModel.IsTitleOverflowing)
            {
                if (!ReferenceEquals(titleDisclosureSlot.Child, titleDisclosure))
                {
                    titleDisclosureSlot.Child = titleDisclosure;
                }
            }
            else if (titleDisclosureSlot.Child is not null)
            {
                titleDisclosureSlot.Child = null;
            }
        }

        var lastMeasuredTitleWidth = -1d;
        void RefreshTitleOverflow(bool force)
        {
            var width = body.ActualWidth;
            if (width <= 0)
            {
                return;
            }

            if (!force && Math.Abs(width - lastMeasuredTitleWidth) <= 0.5)
            {
                return;
            }

            lastMeasuredTitleWidth = width;
            var overflows = MeasureUnwrappedTitleHeight(body, width) > CollapsedTitleMaxHeight + 0.5;
            viewModel.SetTitleOverflowing(overflows);
            ApplyTitlePresentation();
        }

        PropertyChangedEventHandler titlePropertyChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(ReminderCenterEntryViewModel.IsTitleExpanded) or
                nameof(ReminderCenterEntryViewModel.IsTitleOverflowing))
            {
                ApplyTitlePresentation();
            }
        };
        viewModel.PropertyChanged += titlePropertyChanged;
        body.Loaded += (_, _) => RefreshTitleOverflow(force: true);
        body.SizeChanged += (_, args) =>
        {
            if (args.WidthChanged || lastMeasuredTitleWidth <= 0)
            {
                RefreshTitleOverflow(force: false);
            }
        };
        // #253：完整时区标记整体换到下一行，保留原文案，避免在卡片右缘把「（北京时间）」逐字拆开。
        // 仍受卡片内容宽度约束；时钟对齐首行：16px 正文行高约 21，13px 图标顶部留 4。
        var scheduled = new TextBlock
        {
            Text = $"计划：{DateDisplay.FullMoment(item.Key.ScheduledUtc)}\n（北京时间）",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        scheduled.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var scheduledClock = CreateClockIcon("MutedBrush", 13);
        scheduledClock.Margin = new Thickness(0, 4, 5, 0);
        scheduledClock.VerticalAlignment = VerticalAlignment.Top;
        var scheduledRow = new Grid
        {
            Margin = new Thickness(0, 6, 0, 0)
        };
        scheduledRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        scheduledRow.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(scheduled, 1);
        scheduledRow.Children.Add(scheduledClock);
        scheduledRow.Children.Add(scheduled);
        var unitSpacing = SpacingTokens.Resolve(this, SpacingTokens.Unit);
        var blockGap = SpacingTokens.Resolve(this, SpacingTokens.BlockGap);
        var cardPadding = SpacingTokens.Resolve(this, SpacingTokens.CardPadding);
        var error = new TextBlock
        {
            Margin = new Thickness(0, unitSpacing, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold
        };
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        AutomationProperties.SetAutomationId(error, "ReminderEntryDeliveryError");
        UiAccessibility.BindAnnouncedText(
            error,
            viewModel,
            nameof(ReminderCenterEntryViewModel.ErrorMessage),
            () => viewModel.ErrorMessage);
        BindEntry(
            error,
            TextBlock.VisibilityProperty,
            nameof(ReminderCenterEntryViewModel.IsFailureVisible),
            viewModel,
            _visibilityConverter);
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, blockGap, 0, 0)
        };
        divider.SetResourceReference(Border.BackgroundProperty, "NoteLineBrush");

        var acknowledge = UiAccessibility.Name(
            new Button
            {
                Content = CreateIconTextContent(CreateCheckCircleIcon("MainActionTextBrush"), "知道了"),
                MinWidth = 82,
                Margin = new Thickness(0, 0, unitSpacing, 0),
                Padding = new Thickness(8, 0, 8, 0),
                Style = (Style)FindResource("PrimaryButton")
            },
            $"知道了：{item.Source.DisplayText}");
        BindEntry(
            acknowledge,
            Button.IsEnabledProperty,
            nameof(ReminderCenterEntryViewModel.IsAcknowledgeEnabled),
            viewModel);
        BindEntry(
            acknowledge,
            Button.CommandProperty,
            nameof(ReminderCenterEntryViewModel.AcknowledgeCommand),
            viewModel);
        // #320：时长选择与「稍后提醒」合并为单一菜单按钮，套用共享暖粉菜单样式，
        // 预设档位一次选择即稍后；自定义输入在首次选择「自定义」时才懒创建。
        var snooze = UiAccessibility.Name(
            new Button
            {
                Content = CreateMenuButtonContent(CreateBellIcon("InkBrush"), "稍后提醒"),
                MinWidth = 108,
                Padding = new Thickness(10, 0, 10, 0)
            },
            $"稍后提醒：{item.Source.DisplayText}");
        AutomationProperties.SetAutomationId(snooze, "ReminderEntrySnoozeButton");
        BindEntry(
            snooze,
            Button.IsEnabledProperty,
            nameof(ReminderCenterEntryViewModel.IsSnoozeEnabled),
            viewModel);
        var customSnoozeSlot = new Border();
        var complete = UiAccessibility.Name(
            new Button
            {
                Content = CreateIconTextContent(CreateCheckCircleIcon("InkBrush"), "完成便签"),
                MinWidth = 96,
                Padding = new Thickness(11, 0, 11, 0)
            },
            $"完成便签：{item.Source.DisplayText}");
        BindEntry(
            complete,
            Button.IsEnabledProperty,
            nameof(ReminderCenterEntryViewModel.IsCompleteEnabled),
            viewModel);
        BindEntry(
            complete,
            Button.CommandProperty,
            nameof(ReminderCenterEntryViewModel.CompleteCommand),
            viewModel);
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
        primaryActions.Children.Add(snooze);
        var secondaryActions = new WrapPanel
        {
            Margin = new Thickness(0, unitSpacing, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        AutomationProperties.SetAutomationId(secondaryActions, "ReminderEntryActionsSecondaryRow");
        if (item.CanComplete)
        {
            complete.Margin = new Thickness(0, 0, unitSpacing, 0);
            secondaryActions.Children.Add(complete);
        }
        secondaryActions.Children.Add(openSource);

        var content = new StackPanel();
        Grid.SetColumn(content, 1);
        content.Children.Add(sourceRow);
        content.Children.Add(body);
        content.Children.Add(titleDisclosureSlot);
        content.Children.Add(scheduledRow);
        content.Children.Add(error);
        content.Children.Add(divider);
        content.Children.Add(primaryActions);
        content.Children.Add(secondaryActions);
        content.Children.Add(customSnoozeSlot);
        var cardGrid = new Grid();
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition());
        cardGrid.Children.Add(sourceIcon);
        cardGrid.Children.Add(content);
        var row = new Border
        {
            Tag = item.Key,
            Child = cardGrid,
            Padding = new Thickness(cardPadding),
            Margin = new Thickness(0, 0, 0, unitSpacing),
            BorderThickness = new Thickness(1),
            DataContext = viewModel
        };
        row.SetResourceReference(Border.CornerRadiusProperty, "RadiusCard");
        row.SetResourceReference(Border.BackgroundProperty, "ReminderCardBrush");
        row.SetResourceReference(Border.BorderBrushProperty, "NoteLineBrush");
        UiAccessibility.Name(
            row,
            $"{(item.Source.Kind == ReminderSourceKind.Countdown ? "倒计时" : "便签")}提醒：{item.Source.DisplayText}；计划 {DateDisplay.FullMoment(item.Key.ScheduledUtc)} 北京时间");

        ReminderCenterEntry? entry = null;
        var snoozeMenu = CreateSnoozeContextMenu(item, viewModel, snooze, () => entry);
        snooze.ContextMenu = snoozeMenu;

        PropertyChangedEventHandler customSnoozePropertyChanged = (_, args) =>
        {
            if (entry is null)
            {
                return;
            }

            if (args.PropertyName == nameof(ReminderCenterEntryViewModel.IsCustomSnoozeVisible))
            {
                ApplyCustomSnoozePresentation(entry);
            }
            else if (args.PropertyName == nameof(ReminderCenterEntryViewModel.IsSnoozeEnabled) &&
                     !viewModel.IsSnoozeEnabled &&
                     entry.SnoozeMenu.IsOpen)
            {
                entry.SnoozeMenu.IsOpen = false;
            }
        };
        viewModel.PropertyChanged += customSnoozePropertyChanged;

        entry = new ReminderCenterEntry(
            item,
            viewModel,
            row,
            acknowledge,
            snooze,
            snoozeMenu,
            complete,
            customSnoozeSlot,
            openSource,
            error,
            titlePropertyChanged,
            customSnoozePropertyChanged,
            () => RefreshTitleOverflow(force: true));
        BindEntry(
            openSource,
            Button.CommandProperty,
            nameof(ReminderCenterEntryViewModel.OpenSourceCommand),
            viewModel);
        row.Loaded += (_, _) => ScheduleRenderedProbe(entry);
        _entries.Add(item.Key, entry);
        _viewModel.Add(viewModel);

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
        UpdateTitle();
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

        if (entry.SnoozeMenu.IsOpen)
        {
            entry.SnoozeMenu.IsOpen = false;
        }
        entry.ViewModel.AcknowledgeRequested -= OnViewModelAcknowledgeRequested;
        entry.ViewModel.SnoozeRequested -= OnViewModelSnoozeRequested;
        entry.ViewModel.CompleteRequested -= OnViewModelCompleteRequested;
        entry.ViewModel.OpenSourceRequested -= OnViewModelOpenSourceRequested;
        entry.ViewModel.PropertyChanged -= entry.TitlePropertyChanged;
        entry.ViewModel.PropertyChanged -= entry.CustomSnoozePropertyChanged;
        _viewModel.Remove(key);
        _itemsHost.Children.Remove(entry.Row);
        UpdateTitle();
    }

    internal void UpdateTime(DateTimeOffset nowUtc) => _viewModel.UpdateTime(nowUtc);

    private void UpdateTitle()
    {
        Title = _entries.Count == 0
            ? "提醒中心"
            : $"提醒中心（{_entries.Count} 条）";
    }

    internal void SetDeliveryReady(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return;
        }

        entry.ViewModel.SetDeliveryReady();
        EnsureInitialFocus();
    }

    internal void ShowDeliveryFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.ViewModel.ShowFailure("送达状态保存失败，正在自动重试。打开来源仍然可用。");
        }
    }

    internal void ShowAcknowledgementFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.ViewModel.ShowFailure("“知道了”状态保存失败，正在自动重试。条目仍保留在提醒中心。");
        }
    }

    internal void ShowSnoozeFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.ViewModel.ShowFailure("稍后提醒状态保存失败，正在自动重试。条目仍保留在提醒中心。");
        }
    }

    internal void ShowCompletionFailure(ActiveReminderOccurrenceKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.ViewModel.ShowFailure("完成便签状态保存失败，正在自动重试。便签与提醒条目均保持不变。");
        }
    }

    internal void ShowCustomSnoozeError(ActiveReminderOccurrenceKey key, string message)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.ViewModel.ShowCustomSnoozeError(message);
        }
    }

    internal bool RequestFirstAcknowledgement()
    {
        var entry = _itemsHost.Children
            .OfType<Border>()
            .Select(row => _entries[(ActiveReminderOccurrenceKey)row.Tag])
            .FirstOrDefault(candidate => candidate.ViewModel.IsAcknowledgeEnabled);
        if (entry is null)
        {
            return false;
        }

        entry.ViewModel.AcknowledgeCommand.Execute(null);
        return true;
    }

    internal bool RequestAcknowledgeAll()
    {
        if (!_viewModel.CanAcknowledgeAll)
        {
            return false;
        }

        _viewModel.AcknowledgeAllCommand.Execute(null);
        return true;
    }

    internal bool RequestAcknowledgement(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.TryGetValue(key, out var entry) || !entry.ViewModel.IsAcknowledgeEnabled)
        {
            return false;
        }

        entry.ViewModel.AcknowledgeCommand.Execute(null);
        return true;
    }

    internal bool RequestSnooze(
        ActiveReminderOccurrenceKey key,
        ReminderSnoozeOption option,
        DateTimeOffset? customBeijing = null)
    {
        if (!_entries.TryGetValue(key, out var entry) || !entry.ViewModel.IsSnoozeEnabled)
        {
            return false;
        }

        if (option == ReminderSnoozeOption.Custom)
        {
            entry.ViewModel.BeginCustomSnooze();
            if (customBeijing is { } custom)
            {
                var beijing = ChinaStandardTime.ToBeijingTime(custom);
                entry.ViewModel.CustomDate = beijing.Date;
                entry.ViewModel.CustomTime = new TimeSpan(beijing.Hour, beijing.Minute, 0);
                entry.ViewModel.HasInvalidCustomTimeInput = false;
            }

            entry.ViewModel.ConfirmCustomSnoozeCommand.Execute(null);
            return true;
        }

        entry.ViewModel.SelectSnoozePresetCommand.Execute(option);
        return true;
    }

    internal bool RequestComplete(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.TryGetValue(key, out var entry) || !entry.ViewModel.IsCompleteEnabled)
        {
            return false;
        }

        entry.ViewModel.CompleteCommand.Execute(null);
        return true;
    }

    internal bool RequestOpenSource(ActiveReminderOccurrenceKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        entry.ViewModel.OpenSourceCommand.Execute(null);
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
                entry.ViewModel.MarkRendered();
                EntryRendered?.Invoke(
                    this,
                    new ReminderCenterEntryEventArgs(entry.Item.Key));
                EnsureInitialFocus();
            }));
    }

    private void EnsureInitialFocus()
    {
        // #215：不激活、不抢焦点——只有窗口已被用户激活时才落初始焦点。
        if (_initialFocusAssigned || !IsVisible || !IsActive)
        {
            return;
        }

        var first = _itemsHost.Children
            .OfType<Border>()
            .Select(row => _entries[(ActiveReminderOccurrenceKey)row.Tag])
            .FirstOrDefault();
        var target = first?.ViewModel.IsAcknowledgeEnabled == true
            ? first.Acknowledge
            : first?.OpenSource;
        if (target is null)
        {
            return;
        }

        Keyboard.Focus(target);
        _initialFocusAssigned = true;
    }

    internal int TaskbarFlashRequestCount => _taskbarFlashRequestCount;

    // #215：窗口可见但未激活时新提醒到达，闪烁任务栏按钮直到用户切到前台。
    internal void NotifyReminderArrived()
    {
        if (!IsVisible || IsActive)
        {
            return;
        }

        _taskbarFlashRequestCount++;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = new HWND(handle),
            dwFlags = FLASHWINFO_FLAGS.FLASHW_TRAY | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG,
            uCount = 0,
            dwTimeout = 0
        };
        PInvoke.FlashWindowEx(in info);
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

        Keyboard.Focus(entry.ViewModel.IsAcknowledgeEnabled ? entry.Acknowledge : entry.OpenSource);
    }

    private void OnViewModelAcknowledgeRequested(
        object? sender,
        ReminderCenterEntryEventArgs args) =>
        AcknowledgeRequested?.Invoke(this, args);

    private void OnViewModelSnoozeRequested(
        object? sender,
        ReminderCenterSnoozeEventArgs args) =>
        SnoozeRequested?.Invoke(this, args);

    private void OnViewModelCompleteRequested(
        object? sender,
        ReminderCenterEntryEventArgs args) =>
        CompleteRequested?.Invoke(this, args);

    private void OnViewModelOpenSourceRequested(
        object? sender,
        ReminderCenterEntryEventArgs args) =>
        OpenSourceRequested?.Invoke(this, args);

    private static void BindEntry(
        DependencyObject target,
        DependencyProperty property,
        string path,
        ReminderCenterEntryViewModel viewModel,
        IValueConverter? converter = null,
        BindingMode mode = BindingMode.Default) =>
        BindingOperations.SetBinding(
            target,
            property,
            new Binding(path)
            {
                Source = viewModel,
                Mode = mode,
                Converter = converter
            });

    private ContextMenu CreateSnoozeContextMenu(
        ReminderCenterItem item,
        ReminderCenterEntryViewModel viewModel,
        Button snooze,
        Func<ReminderCenterEntry?> getEntry)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = snooze,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4
        };
        SharedMenuStyles.Apply(menu);
        AutomationProperties.SetAutomationId(menu, "ReminderEntrySnoozeMenu");
        AutomationProperties.SetName(menu, $"稍后提醒选项：{item.Source.DisplayText}");

        var pointerOpeningMenu = false;
        var suppressNextClickReopen = false;
        var escapeClosePending = false;
        var focusRestoredForCurrentOpen = false;

        void OpenMenu()
        {
            if (!IsActive)
            {
                Activate();
            }

            menu.PlacementTarget = snooze;
            menu.Placement = PlacementMode.Bottom;
            menu.VerticalOffset = 4;
            menu.IsOpen = true;
        }

        void RestoreFocusToSnoozeAfterEscape()
        {
            if (focusRestoredForCurrentOpen)
            {
                return;
            }

            focusRestoredForCurrentOpen = true;
            escapeClosePending = false;
            if (snooze.IsEnabled && snooze.IsVisible && IsVisible)
            {
                Keyboard.Focus(snooze);
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (!menu.IsOpen &&
                        IsActive &&
                        snooze.IsEnabled &&
                        snooze.IsVisible &&
                        Keyboard.FocusedElement is null)
                    {
                        Keyboard.Focus(snooze);
                    }
                }));
            }
        }

        foreach (var (text, option, automationId) in new[]
                 {
                     ("10 分钟后", ReminderSnoozeOption.TenMinutes, "ReminderEntrySnoozeTenMinutes"),
                     ("30 分钟后", ReminderSnoozeOption.ThirtyMinutes, "ReminderEntrySnoozeThirtyMinutes"),
                     ("1 小时后", ReminderSnoozeOption.SixtyMinutes, "ReminderEntrySnoozeSixtyMinutes"),
                     ("明天 09:00", ReminderSnoozeOption.TomorrowAtNine, "ReminderEntrySnoozeTomorrowAtNine"),
                     ("自定义", ReminderSnoozeOption.Custom, "ReminderEntrySnoozeCustom")
                 })
        {
            var menuItem = new MenuItem
            {
                Header = text,
                Tag = option
            };
            AutomationProperties.SetAutomationId(menuItem, automationId);
            if (option == ReminderSnoozeOption.Custom)
            {
                menuItem.Click += (_, _) =>
                {
                    if (menu.IsOpen)
                    {
                        menu.IsOpen = false;
                    }

                    viewModel.BeginCustomSnooze();
                    if (IsActive && getEntry()?.CustomDate is { IsVisible: true, IsEnabled: true } customDate)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                        {
                            if (IsActive && viewModel.IsCustomSnoozeVisible && customDate.IsVisible && customDate.IsEnabled)
                            {
                                Keyboard.Focus(customDate);
                            }
                        }));
                    }
                };
            }
            else
            {
                var presetOption = option;
                menuItem.Click += (_, _) =>
                {
                    if (menu.IsOpen)
                    {
                        menu.IsOpen = false;
                    }

                    viewModel.SelectSnoozePreset(presetOption);
                };
            }

            menu.Items.Add(menuItem);
        }

        snooze.PreviewMouseLeftButtonDown += (_, _) =>
        {
            pointerOpeningMenu = true;
            suppressNextClickReopen = menu.IsOpen;
        };
        snooze.PreviewKeyDown += (_, _) =>
        {
            pointerOpeningMenu = false;
            suppressNextClickReopen = false;
        };
        snooze.LostKeyboardFocus += (_, _) => pointerOpeningMenu = false;
        snooze.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Down && viewModel.IsSnoozeEnabled && !menu.IsOpen)
            {
                pointerOpeningMenu = false;
                suppressNextClickReopen = false;
                args.Handled = true;
                OpenMenu();
            }
        };
        snooze.Click += (_, _) =>
        {
            if (suppressNextClickReopen)
            {
                suppressNextClickReopen = false;
                return;
            }

            if (!viewModel.IsSnoozeEnabled)
            {
                return;
            }

            if (menu.IsOpen)
            {
                menu.IsOpen = false;
                return;
            }

            OpenMenu();
        };
        menu.Opened += (_, _) =>
        {
            escapeClosePending = false;
            focusRestoredForCurrentOpen = false;
            var openedByPointer = pointerOpeningMenu;
            pointerOpeningMenu = false;
            if (!openedByPointer && menu.Items.Count > 0 && menu.Items[0] is MenuItem firstItem)
            {
                Keyboard.Focus(firstItem);
            }
            else
            {
                Keyboard.Focus(menu);
            }
        };
        menu.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                escapeClosePending = true;
            }
        };
        menu.AddHandler(
            Keyboard.KeyDownEvent,
            new KeyEventHandler((_, args) =>
            {
                if (args.Key != Key.Escape)
                {
                    return;
                }

                if (menu.IsOpen)
                {
                    escapeClosePending = true;
                    menu.IsOpen = false;
                    args.Handled = true;
                }
                else
                {
                    RestoreFocusToSnoozeAfterEscape();
                }
            }),
            handledEventsToo: true);
        menu.Closed += (_, _) =>
        {
            if (escapeClosePending)
            {
                RestoreFocusToSnoozeAfterEscape();
            }
        };
        return menu;
    }

    private void ApplyCustomSnoozePresentation(ReminderCenterEntry entry)
    {
        if (entry.ViewModel.IsCustomSnoozeVisible)
        {
            EnsureCustomSnoozeControls(entry);
            entry.CustomPanel!.Visibility = Visibility.Visible;
        }
        else if (entry.CustomPanel is not null)
        {
            entry.CustomPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void EnsureCustomSnoozeControls(ReminderCenterEntry entry)
    {
        if (entry.CustomPanel is not null)
        {
            return;
        }

        var item = entry.Item;
        var viewModel = entry.ViewModel;
        var unitSpacing = SpacingTokens.Resolve(this, SpacingTokens.Unit);
        var customDate = UiAccessibility.Name(
            new WpfUiDatePicker
            {
                Width = 216,
                HorizontalAlignment = HorizontalAlignment.Left
            },
            $"自定义稍后日期：{item.Source.DisplayText}");
        BindEntry(
            customDate,
            DatePicker.SelectedDateProperty,
            nameof(ReminderCenterEntryViewModel.CustomDate),
            viewModel,
            mode: BindingMode.TwoWay);
        BindEntry(
            customDate,
            InputErrorState.HasErrorProperty,
            nameof(ReminderCenterEntryViewModel.IsCustomDateErrorVisible),
            viewModel);
        BindEntry(
            customDate,
            AutomationProperties.HelpTextProperty,
            nameof(ReminderCenterEntryViewModel.CustomErrorMessage),
            viewModel);
        var customTimeLabel = new TextBlock
        {
            Text = "时间",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, unitSpacing, 0)
        };
        customTimeLabel.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
        customTimeLabel.SetResourceReference(TextBlock.ForegroundProperty, "BodyBrush");
        var customTime = UiAccessibility.Name(
            new TextBox(),
            $"自定义稍后时间：{item.Source.DisplayText}");
        AutomationProperties.SetAutomationId(customTime, "ReminderEntryCustomSnoozeTime");
        AutomationProperties.SetLabeledBy(customTime, customTimeLabel);
        TimeInputBehavior.SetFormat(customTime, TimeInputFormat.HourMinute);
        TimeInputBehavior.SetIsEnabled(customTime, true);
        BindEntry(
            customTime,
            TimeInputBehavior.ValueProperty,
            nameof(ReminderCenterEntryViewModel.CustomTime),
            viewModel,
            mode: BindingMode.TwoWay);
        BindEntry(
            customTime,
            TimeInputBehavior.HasInvalidTextProperty,
            nameof(ReminderCenterEntryViewModel.HasInvalidCustomTimeInput),
            viewModel,
            mode: BindingMode.TwoWay);
        BindEntry(
            customTime,
            InputErrorState.HasErrorProperty,
            nameof(ReminderCenterEntryViewModel.IsCustomTimeErrorVisible),
            viewModel);
        BindEntry(
            customTime,
            AutomationProperties.HelpTextProperty,
            nameof(ReminderCenterEntryViewModel.CustomErrorMessage),
            viewModel);
        customTime.LostKeyboardFocus += (_, _) => viewModel.ValidateCustomTimeOnBlur();
        customTime.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter &&
                Keyboard.Modifiers == ModifierKeys.None &&
                viewModel.ConfirmCustomSnoozeCommand.CanExecute(null))
            {
                viewModel.ConfirmCustomSnoozeCommand.Execute(null);
                args.Handled = true;
            }
        };
        var customTimePlaceholder = new SilentTextBlock
        {
            Text = "HH:mm",
            Margin = new Thickness(12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = string.IsNullOrEmpty(customTime.Text) ? Visibility.Visible : Visibility.Collapsed
        };
        customTimePlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Typography.SetNumeralAlignment(customTimePlaceholder, FontNumeralAlignment.Tabular);
        customTime.TextChanged += (_, _) =>
            customTimePlaceholder.Visibility = string.IsNullOrEmpty(customTime.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        var customTimeSlot = new Grid
        {
            Width = 108,
            Margin = new Thickness(0, 0, unitSpacing, 0)
        };
        customTimeSlot.Children.Add(customTime);
        customTimeSlot.Children.Add(customTimePlaceholder);
        var customConfirm = UiAccessibility.Name(
            new Button
            {
                Content = "确认稍后",
                MinWidth = 88,
                Padding = new Thickness(10, 0, 10, 0)
            },
            $"确认自定义稍后：{item.Source.DisplayText}");
        AutomationProperties.SetAutomationId(customConfirm, "ReminderEntryCustomSnoozeConfirm");
        BindEntry(
            customConfirm,
            Button.IsEnabledProperty,
            nameof(ReminderCenterEntryViewModel.IsSnoozeEnabled),
            viewModel);
        BindEntry(
            customConfirm,
            Button.CommandProperty,
            nameof(ReminderCenterEntryViewModel.ConfirmCustomSnoozeCommand),
            viewModel);
        var customTimeRow = new WrapPanel
        {
            Margin = new Thickness(0, unitSpacing, 0, 0)
        };
        AutomationProperties.SetAutomationId(customTimeRow, "ReminderEntryCustomSnoozeTimeRow");
        customTimeRow.Children.Add(customTimeLabel);
        customTimeRow.Children.Add(customTimeSlot);
        customTimeRow.Children.Add(customConfirm);
        var customError = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold
        };
        customError.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(customError, AutomationLiveSetting.Assertive);
        AutomationProperties.SetAutomationId(customError, "ReminderEntryCustomError");
        UiAccessibility.BindAnnouncedText(
            customError,
            viewModel,
            nameof(ReminderCenterEntryViewModel.CustomErrorMessage),
            () => viewModel.CustomErrorMessage);
        BindEntry(
            customError,
            TextBlock.VisibilityProperty,
            nameof(ReminderCenterEntryViewModel.IsCustomErrorVisible),
            viewModel,
            _visibilityConverter);
        var customPanel = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0)
        };
        AutomationProperties.SetAutomationId(customPanel, "ReminderEntryCustomSnoozePanel");
        customPanel.Children.Add(customDate);
        customPanel.Children.Add(customTimeRow);
        customPanel.Children.Add(customError);
        entry.AttachCustomControls(customPanel, customDate, customTime, customConfirm, customError);
        entry.CustomSnoozeSlot.Child = customPanel;
    }

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

    private static StackPanel CreateMenuButtonContent(FrameworkElement icon, string text)
    {
        var panel = CreateIconTextContent(icon, text);
        panel.Children.Add(CreateDropdownChevronIcon("InkBrush"));
        return panel;
    }

    private static FrameworkElement CreateDropdownChevronIcon(string strokeKey)
    {
        var path = new Path
        {
            Width = 9,
            Height = 6,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(6, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Data = IconGeometries.Resolve(IconGeometries.ChevronDown),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        path.SetResourceReference(Shape.StrokeProperty, strokeKey);
        return path;
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
            Width = 8.2,
            Height = 6.9,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = IconGeometries.Resolve(IconGeometries.Checkmark),
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
            Data = IconGeometries.Resolve(IconGeometries.BellFilled)
        };
        path.SetResourceReference(Shape.FillProperty, fillKey);
        return path;
    }

    private static FrameworkElement CreateMinimizeIcon(string strokeKey)
    {
        var path = new Path
        {
            Width = 13,
            Height = 13,
            Stretch = Stretch.Uniform,
            Data = IconGeometries.Resolve(IconGeometries.Minimize),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        path.SetResourceReference(Shape.StrokeProperty, strokeKey);
        return path;
    }

    private static FrameworkElement CreateClockIcon(string strokeKey, double size)
    {
        var clock = new Path
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Data = IconGeometries.Resolve(IconGeometries.Clock),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        clock.SetResourceReference(Shape.StrokeProperty, strokeKey);
        return clock;
    }

    private static FrameworkElement CreateExternalLinkIcon(string strokeKey)
    {
        var path = new Path
        {
            Data = IconGeometries.Resolve(IconGeometries.ExternalLink),
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
            Data = IconGeometries.Resolve(IconGeometries.ReminderNoteDocument),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        path.SetResourceReference(Shape.StrokeProperty, strokeKey);
        return path;
    }

    private static double MeasureUnwrappedTitleHeight(TextBlock body, double width)
    {
        var probe = new TextBlock
        {
            Text = body.Text,
            FontFamily = body.FontFamily,
            FontSize = body.FontSize,
            FontWeight = body.FontWeight,
            FontStyle = body.FontStyle,
            FontStretch = body.FontStretch,
            LineHeight = body.LineHeight,
            LineStackingStrategy = body.LineStackingStrategy,
            TextWrapping = body.TextWrapping,
            TextTrimming = TextTrimming.None,
            FlowDirection = body.FlowDirection
        };
        TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(body));
        TextOptions.SetTextRenderingMode(probe, TextOptions.GetTextRenderingMode(body));
        probe.Measure(new Size(width, double.PositiveInfinity));
        return probe.DesiredSize.Height;
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
        ReminderCenterEntryViewModel viewModel,
        Border row,
        Button acknowledge,
        Button snooze,
        ContextMenu snoozeMenu,
        Button complete,
        Border customSnoozeSlot,
        Button openSource,
        TextBlock error,
        PropertyChangedEventHandler titlePropertyChanged,
        PropertyChangedEventHandler customSnoozePropertyChanged,
        Action refreshTitleOverflow)
    {
        public ReminderCenterItem Item { get; } = item;

        public ReminderCenterEntryViewModel ViewModel { get; } = viewModel;

        public Border Row { get; } = row;

        public Button Acknowledge { get; } = acknowledge;

        public Button Snooze { get; } = snooze;

        public ContextMenu SnoozeMenu { get; } = snoozeMenu;

        public Button Complete { get; } = complete;

        public Border CustomSnoozeSlot { get; } = customSnoozeSlot;

        public StackPanel? CustomPanel { get; private set; }

        public DatePicker? CustomDate { get; private set; }

        public TextBox? CustomTime { get; private set; }

        public Button? CustomConfirm { get; private set; }

        public TextBlock? CustomError { get; private set; }

        public Button OpenSource { get; } = openSource;

        public TextBlock Error { get; } = error;

        public PropertyChangedEventHandler TitlePropertyChanged { get; } = titlePropertyChanged;

        public PropertyChangedEventHandler CustomSnoozePropertyChanged { get; } = customSnoozePropertyChanged;

        public Action RefreshTitleOverflow { get; } = refreshTitleOverflow;

        public bool RenderProbeScheduled { get; set; }

        public bool RenderedRaised { get; set; }

        public void AttachCustomControls(
            StackPanel customPanel,
            DatePicker customDate,
            TextBox customTime,
            Button customConfirm,
            TextBlock customError)
        {
            CustomPanel = customPanel;
            CustomDate = customDate;
            CustomTime = customTime;
            CustomConfirm = customConfirm;
            CustomError = customError;
        }
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
