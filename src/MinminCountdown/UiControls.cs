using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static class UiAccessibility
{
    public static T Name<T>(T element, string name)
        where T : FrameworkElement
    {
        AutomationProperties.SetName(element, FoldAccessibleName(name));
        return element;
    }

    internal static string FoldAccessibleName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name ?? string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var ch in name)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(ch);
            pendingSpace = false;
        }

        return builder.ToString();
    }

    public static void MarkLive(TextBlock textBlock)
    {
        AutomationProperties.SetLiveSetting(textBlock, AutomationLiveSetting.Polite);
    }

    // #216：全仓直播区文本的唯一更新入口。文本真正变化后才改可访问名并触发
    // LiveRegionChanged；相同文本（分钟级刷新、重复筛选摘要）不播报。
    public static void Announce(TextBlock target, string text, Action<TextBlock>? afterText = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.Equals(target.Text, text, StringComparison.Ordinal))
        {
            afterText?.Invoke(target);
            return;
        }

        target.Text = text;
        AutomationProperties.SetName(target, text);
        afterText?.Invoke(target);
        var announced = text;
        // 布局完成后再触发：折叠错误行刚变为可见时，同步 Raise 的 peer 还没有 UIA provider，事件会被丢掉。
        target.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (!string.Equals(target.Text, announced, StringComparison.Ordinal))
                {
                    return;
                }

                if (Window.GetWindow(target) is Window owner)
                {
                    var windowPeer = UIElementAutomationPeer.FromElement(owner)
                        ?? UIElementAutomationPeer.CreatePeerForElement(owner);
                    if (windowPeer is not null)
                    {
                        RealizeAutomationTree(windowPeer);
                    }
                }

                var peer = UIElementAutomationPeer.FromElement(target)
                    ?? UIElementAutomationPeer.CreatePeerForElement(target);
                object? provider = null;
                if (peer is not null)
                {
                    var providerMethod = typeof(AutomationPeer).GetMethod(
                        "ProviderFromPeer",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    provider = providerMethod?.GetParameters().Length == 0
                        ? providerMethod.Invoke(peer, null)
                        : providerMethod?.Invoke(peer, new object[] { peer });
                }
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "narrator-216-trace.txt"),
                    $"{AutomationProperties.GetAutomationId(target)} peer={peer is not null} provider={provider is not null}{Environment.NewLine}");
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }));
    }

    public static void BindAnnouncedText(
        TextBlock target,
        INotifyPropertyChanged source,
        string propertyName,
        Func<string?> readText)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(readText);

        void Push() => Announce(target, readText() ?? string.Empty);

        source.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null ||
                string.Equals(args.PropertyName, propertyName, StringComparison.Ordinal))
            {
                Push();
            }
        };
        Push();
    }

    private static void RealizeAutomationTree(AutomationPeer peer)
    {
        IReadOnlyList<AutomationPeer>? children;
        try
        {
            children = peer.GetChildren();
        }
        catch (ElementNotAvailableException)
        {
            return;
        }

        if (children is null)
        {
            return;
        }

        foreach (var child in children)
        {
            RealizeAutomationTree(child);
        }
    }
}

/// <summary>
/// #156：退出控件朗读的文本（Web aria-hidden 的 WPF 映射——不产生自动化 peer，元素整体
/// 不进 UIA 树，装饰手写体与分段裸数字因此不再被当作独立内容控件朗读）。
/// 不携带信息语义：信息一律由携带可访问名的宿主控件（如图标按钮、分段按钮名）承担。
/// 渲染与 <see cref="TextBlock"/> 完全一致，只影响自动化树。
/// </summary>
internal sealed class SilentTextBlock : TextBlock
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}

/// <summary>
/// #216：同一底部文案在鼓励语模式下退出自动化树，金额与日历状态模式保留可朗读的 peer。
/// WPF 会缓存已创建的 peer，且没有公开 API 能卸下它，因此模式切换时清掉内部缓存并让祖先重列子节点。
/// </summary>
internal sealed class AutomationOptionalTextBlock : TextBlock
{
    private static readonly FieldInfo AutomationPeerField = typeof(UIElement).GetField(
        "AutomationPeerField",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("WPF 未公开 AutomationPeerField，无法在鼓励语模式下退出自动化树。");

    private static readonly MethodInfo ClearAutomationPeer = AutomationPeerField.FieldType.GetMethod(
        "ClearValue",
        new[] { typeof(DependencyObject) })
        ?? throw new InvalidOperationException("WPF AutomationPeerField 缺少 ClearValue。");

    private bool _excludedFromAutomationTree;

    internal void SetExcludedFromAutomationTree(bool excluded)
    {
        if (_excludedFromAutomationTree == excluded)
        {
            return;
        }

        _excludedFromAutomationTree = excluded;
        var uncommonField = AutomationPeerField.GetValue(null)
            ?? throw new InvalidOperationException("AutomationPeerField 尚未初始化。");
        ClearAutomationPeer.Invoke(uncommonField, new object[] { this });
        var parent = VisualTreeHelper.GetParent(this) as UIElement;
        while (parent is not null)
        {
            UIElementAutomationPeer.FromElement(parent)?.ResetChildrenCache();
            parent = VisualTreeHelper.GetParent(parent) as UIElement;
        }
    }

    protected override AutomationPeer? OnCreateAutomationPeer() =>
        _excludedFromAutomationTree ? null : base.OnCreateAutomationPeer();
}
/// <summary>
/// 便签行「…」溢出菜单：把弹出层挂在其触发按钮上，供真实渲染验证读取。
/// </summary>
internal static class NoteRowOverflowMenuBehavior
{
    private static readonly DependencyProperty PopupProperty = DependencyProperty.RegisterAttached(
        "Popup",
        typeof(Popup),
        typeof(NoteRowOverflowMenuBehavior),
        new PropertyMetadata(null));

    public static Popup? GetPopup(Button trigger) =>
        (Popup?)trigger.GetValue(PopupProperty);

    public static void SetPopup(Button trigger, Popup popup) =>
        trigger.SetValue(PopupProperty, popup);
}

internal static class PopupFocusLifecycle
{
    private static readonly HashSet<Popup> Attached = new();
    private static readonly Dictionary<Popup, FrameworkElement> ClipTargets = new();

    public static void Attach(
        Popup popup,
        UIElement trigger,
        Func<IInputElement?> initialFocus)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(initialFocus);

        if (!Attached.Add(popup))
        {
            return;
        }

        if (popup.Child is not UIElement surface)
        {
            throw new InvalidOperationException("弹层必须先设置可聚焦的内容容器。");
        }

        // #153：弹层焦点生命周期补齐层内 Tab 循环——从最后一个可聚焦项继续 Tab 回到首项，
        // 焦点不得逃到被挡住的宿主窗口而弹层仍开着（父票键盘裁决：打开聚焦首项、Esc 关并归还、层内 Tab 循环）。
        KeyboardNavigation.SetTabNavigation(surface, KeyboardNavigationMode.Cycle);

        var suppressClickToggle = false;
        trigger.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, _) =>
            {
                suppressClickToggle = popup.IsOpen;
            }),
            true);
        trigger.AddHandler(
            ButtonBase.ClickEvent,
            new RoutedEventHandler((_, eventArgs) =>
            {
                if (!suppressClickToggle)
                {
                    return;
                }

                suppressClickToggle = false;
                PopupMotionBehavior.Close(popup);
                eventArgs.Handled = true;
            }),
            true);
        trigger.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((_, eventArgs) =>
            {
                if (eventArgs.Key != Key.Escape)
                {
                    return;
                }

                PopupMotionBehavior.Close(popup);
                eventArgs.Handled = true;
                RestoreTriggerFocus(trigger);
                trigger.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    new Action(() =>
                    {
                        RestoreTriggerFocus(trigger);
                    }));
            }),
            true);

        popup.Opened += (_, _) =>
        {
            ClampPopupToCanvas(popup);
            popup.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() =>
                {
                    if (popup.IsOpen &&
                        !PopupMotionBehavior.IsClosing(popup) &&
                        initialFocus() is { } target)
                    {
                        Keyboard.Focus(target);
                    }
                }));
        };
        surface.AddHandler(
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((_, eventArgs) =>
            {
                if (eventArgs.Key != Key.Escape || !popup.IsOpen)
                {
                    return;
                }

                if (HasOpenNestedDropDown(surface))
                {
                    return;
                }

                PopupMotionBehavior.Close(popup);
                eventArgs.Handled = true;
                trigger.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    new Action(() =>
                    {
                        RestoreTriggerFocus(trigger);
                    }));
            }),
            true);
    }

    private static void RestoreTriggerFocus(UIElement trigger)
    {
        if (!trigger.IsEnabled)
        {
            return;
        }

        if (Window.GetWindow(trigger) is { } window)
        {
            window.Activate();
            FocusManager.SetFocusedElement(window, trigger);
        }

        trigger.Focusable = true;
        trigger.Focus();
        Keyboard.Focus(trigger);
    }

    public static void ClipToCanvas(Popup popup, FrameworkElement canvas)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(canvas);
        ClipTargets[popup] = canvas;
    }

    private static void ClampPopupToCanvas(Popup popup)
    {
        if (!ClipTargets.TryGetValue(popup, out var canvas) ||
            popup.Child is not FrameworkElement child)
        {
            return;
        }

        void Apply()
        {
            if (!popup.IsOpen || popup.PlacementTarget is not FrameworkElement target)
            {
                return;
            }

            canvas.UpdateLayout();
            child.UpdateLayout();
            target.UpdateLayout();
            if (canvas.ActualWidth <= 0 || child.ActualWidth <= 0)
            {
                return;
            }

            var dip = VisualTreeHelper.GetDpi(child).PixelsPerDip;
            if (dip <= 0)
            {
                dip = 1;
            }

            popup.HorizontalOffset = 0;
            Point desired;
            Point targetLeft;
            try
            {
                desired = canvas.PointToScreen(new Point(canvas.ActualWidth - 8, 0));
                targetLeft = target.PointToScreen(new Point(0, 0));
            }
            catch (InvalidOperationException)
            {
                return;
            }

            var childRight = targetLeft.X + (child.ActualWidth * dip);
            var overflow = childRight - desired.X;
            popup.HorizontalOffset = -overflow / dip;
        }

        Apply();
        popup.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Apply);
        popup.Dispatcher.BeginInvoke(DispatcherPriority.Render, Apply);
    }

    internal static bool IsAttached(Popup popup) => Attached.Contains(popup);

    private static bool HasOpenNestedDropDown(DependencyObject root)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current is DatePicker { IsDropDownOpen: true } ||
                current is ComboBox { IsDropDownOpen: true })
            {
                return true;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < childCount; index++)
            {
                pending.Enqueue(VisualTreeHelper.GetChild(current, index));
            }
        }

        return false;
    }
}

internal sealed class TagSelector : Grid
{
    private readonly Button _trigger;
    private readonly Popup _popup;
    private readonly Border _surface;
    private readonly TextBox _search;
    private readonly Button _create;
    private readonly StackPanel _items;
    private readonly TextBlock _triggerText;
    private IReadOnlyList<TagRecord> _tags = Array.Empty<TagRecord>();
    private bool _rendering;

    public TagSelector()
    {
        _triggerText = new TextBlock
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = "选择标签"
        };
        var triggerContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        triggerContent.Children.Add(new System.Windows.Shapes.Path
        {
            Width = 20,
            Height = 20,
            Data = Geometry.Parse(
                "M 2.5,3.5 L 2.5,9.2 C 2.5,10 2.8,10.8 3.4,11.4 L 11.6,19.6 C 12.7,20.7 14.4,20.7 15.5,19.6 L 19.6,15.5 C 20.7,14.4 20.7,12.7 19.6,11.6 L 11.4,3.4 C 10.8,2.8 10,2.5 9.2,2.5 L 3.5,2.5 C 2.9,2.5 2.5,2.9 2.5,3.5 Z M 7,7 L 7.02,7"),
            Stroke = (Brush)FindResource("MutedBrush"),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center
        });
        triggerContent.Children.Add(_triggerText);
        _trigger = UiAccessibility.Name(
            new Button
            {
                Content = triggerContent,
                MinWidth = 116,
                Padding = new Thickness(16, 0, 8, 0),
                Style = (Style)FindResource("SecondaryButton")
            },
            "选择标签");
        Children.Add(_trigger);

        _search = UiAccessibility.Name(
            new TextBox { MaxLength = 30, Margin = new Thickness(0, 0, 0, 8) },
            "搜索或新建标签");
        _search.TextChanged += (_, _) => RenderItems();

        _create = UiAccessibility.Name(
            new Button
            {
                Content = "新建标签",
                IsDefault = false,
                Visibility = Visibility.Collapsed,
                Style = (Style)FindResource("PrimaryButton"),
                Margin = new Thickness(0, 8, 0, 0)
            },
            "新建当前搜索标签");
        _create.Click += (_, _) =>
        {
            var name = _search.Text.Trim();
            if (name.Length > 0)
            {
                CreateRequested?.Invoke(this, name);
            }
        };

        _items = new StackPanel();
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(_search);
        panel.Children.Add(new ScrollViewer
        {
            Content = _items,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        panel.Children.Add(_create);

        _surface = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 8, 0, 0),
            Child = panel,
            MinWidth = 230
        };
        _surface.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        _surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        _popup = new Popup
        {
            PlacementTarget = _trigger,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            Child = _surface
        };
        PopupMotionBehavior.Attach(_popup);
        PopupFocusLifecycle.Attach(_popup, _trigger, () => _search);
        _trigger.Click += (_, _) => PopupMotionBehavior.Toggle(_popup);
        Children.Add(_popup);
    }

    public event EventHandler? SelectionChanged;

    public event Action<object?, string>? CreateRequested;

    public HashSet<string> SelectedTagIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPopupOpen
    {
        get => _popup.IsOpen;
        set
        {
            if (value)
            {
                PopupMotionBehavior.Open(_popup);
            }
            else
            {
                PopupMotionBehavior.Close(_popup);
            }
        }
    }

    public bool CreateActionVisible => _create.Visibility == Visibility.Visible;

    public string SummaryText => _triggerText.Text;

    public bool CreateButtonIsDefault => _create.IsDefault;

    internal Popup Popup => _popup;

    internal Button PopupTrigger => _trigger;

    internal TextBox PopupInitialFocus => _search;

    internal bool PopupSurfaceFollowsActiveTheme =>
        ReferenceEquals(_surface.Background, Application.Current.FindResource("CanvasBrush")) &&
        ReferenceEquals(_surface.BorderBrush, Application.Current.FindResource("PeachLineBrush"));

    public bool ProbeCreateAction(string query)
    {
        _search.Text = query;
        return _create.Visibility == Visibility.Visible;
    }

    public void SetTags(IReadOnlyList<TagRecord> tags)
    {
        _tags = tags;
        SelectedTagIds.RemoveWhere(id => !_tags.Any(
            tag => string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase)));
        RenderItems();
        UpdateSummary();
    }

    public void SetSelection(IEnumerable<string> tagIds)
    {
        SelectedTagIds.Clear();
        foreach (var id in tagIds)
        {
            SelectedTagIds.Add(id);
        }

        RenderItems();
        UpdateSummary();
    }

    private void RenderItems()
    {
        if (_rendering)
        {
            return;
        }

        _rendering = true;
        try
        {
            _items.Children.Clear();
            var query = _search.Text.Trim();
            var visibleTags = _tags.Where(tag =>
                query.Length == 0 ||
                tag.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            foreach (var tag in visibleTags)
            {
                var checkBox = UiAccessibility.Name(
                    new CheckBox
                    {
                        Content = tag.Name,
                        IsChecked = SelectedTagIds.Contains(tag.Id),
                        Tag = tag.Id,
                        Uid = tag.Id,
                        Margin = new Thickness(2, 3, 2, 3)
                    },
                    $"标签 {tag.Name}");
                checkBox.Checked += OnTagChecked;
                checkBox.Unchecked += OnTagChecked;
                _items.Children.Add(checkBox);
            }

            _create.Visibility =
                query.Length > 0 &&
                !_tags.Any(tag => string.Equals(tag.Name, query, StringComparison.OrdinalIgnoreCase))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        finally
        {
            _rendering = false;
        }
    }

    private void OnTagChecked(object sender, RoutedEventArgs eventArgs)
    {
        if (_rendering || sender is not CheckBox { Tag: string id } checkBox)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            SelectedTagIds.Add(id);
        }
        else
        {
            SelectedTagIds.Remove(id);
        }

        UpdateSummary();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSummary()
    {
        var names = _tags
            .Where(tag => SelectedTagIds.Contains(tag.Id))
            .Select(tag => tag.Name)
            .ToArray();
        var text = names.Length switch
        {
            0 => "选择标签",
            1 => names[0],
            2 => $"{names[0]}、{names[1]}",
            _ => $"{names[0]} +{names.Length - 1}"
        };
        if (text.Length > 18)
        {
            text = text[..15] + "…";
        }

        var fullSelection = string.Join("、", names);
        _triggerText.Text = text;
        _trigger.ToolTip = names.Length == 0 ? "选择标签" : $"已选标签：{fullSelection}";
        UiAccessibility.Name(_trigger, names.Length == 0 ? "选择标签" : $"已选标签：{fullSelection}");
    }
}

internal static class WpfUiPickerStyles
{
    private static readonly ResourceDictionary Library = CreateLibrary();

    // WPF-UI supports the standard WPF DatePicker and ComboBox through styles.
    // Keep its implicit styles out of Application.Resources: only these explicit
    // base styles, and the tokens scoped to each style, enter the picker family.
    public static Style DatePicker { get; } = CreateScopedStyle("DefaultDatePickerStyle");
    public static Style ComboBox { get; } = CreateScopedStyle("DefaultComboBoxStyle");
    public static Style ComboBoxItem { get; } = CreateScopedStyle("DefaultComboBoxItemStyle");
    public static Style ToggleButton { get; } = CreateScopedStyle("DefaultComboBoxToggleButtonStyle");

    private static ResourceDictionary CreateLibrary()
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary
        {
            Theme = Wpf.Ui.Appearance.ApplicationTheme.Light
        });
        resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        return resources;
    }

    private static Style CreateScopedStyle(string name)
    {
        var libraryStyle = (Style)Library[name];
        var style = new Style(libraryStyle.TargetType, libraryStyle);
        style.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary
        {
            Theme = Wpf.Ui.Appearance.ApplicationTheme.Light
        });
        return style;
    }

    internal static bool IsBasedOn(Style? style, Style libraryStyle)
    {
        for (var current = style; current is not null; current = current.BasedOn)
        {
            if (ReferenceEquals(current, libraryStyle))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class WpfUiDatePicker : DatePicker
{
    internal const string SpacedChineseDateFormat = "yyyy 年 M 月 d 日";
    private static readonly CoerceValueCallback? BaseCoerceSelectedDate =
        SelectedDateProperty.GetMetadata(typeof(DatePicker)).CoerceValueCallback;
    private DatePickerTextBox? _textBox;
    private Calendar? _openCalendar;
    private bool _returnFocusAfterClose;
    private DateTime? _selectedDateAtOpen;
    private bool _handlingEscapeSelection;

    static WpfUiDatePicker()
    {
        IsDropDownOpenProperty.OverrideMetadata(typeof(WpfUiDatePicker),
            new FrameworkPropertyMetadata(false, (owner, args) =>
            {
                if (args.NewValue is true)
                {
                    // A retained popup does not raise CalendarOpened again.
                    // Reopening cancels the previous Escape focus request.
                    var picker = (WpfUiDatePicker)owner;
                    picker._returnFocusAfterClose = false;
                    picker._selectedDateAtOpen = picker.SelectedDate;
                }
            }));
        SelectedDateProperty.OverrideMetadata(typeof(WpfUiDatePicker),
            new FrameworkPropertyMetadata(null, null, CoerceSelectedDate));
    }

    private static object CoerceSelectedDate(DependencyObject owner, object value)
    {
        var picker = (WpfUiDatePicker)owner;
        // WPF's day-button handler runs even for handled Escape keys and uses
        // the last physical opening's snapshot. Replace that value before the
        // base coercion can update Calendar or raise selection notifications.
        var candidate = picker._handlingEscapeSelection ? picker._selectedDateAtOpen : value;
        return BaseCoerceSelectedDate is { } coerce ? coerce(owner, candidate!) : candidate!;
    }

    public override void OnApplyTemplate()
    {
        DetachOpenCalendar();
        base.OnApplyTemplate();
        _textBox = GetTemplateChild("PART_TextBox") as DatePickerTextBox;
        if (_textBox is not null)
        {
            _textBox.IsReadOnly = true;
            _textBox.Focusable = false;
            _textBox.ApplyTemplate();
            // #139: TextBoxBase copies Padding to the content host. The warm
            // template already applies it as Margin, so count it only once.
            if (_textBox.Template?.FindName("PART_ContentHost", _textBox) is ScrollViewer host)
            {
                host.Padding = default;
            }
        }

        WriteSelectedDateText();
    }

    protected override void OnSelectedDateChanged(SelectionChangedEventArgs eventArgs)
    {
        base.OnSelectedDateChanged(eventArgs);
        WriteSelectedDateText();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs eventArgs)
    {
        base.OnLostKeyboardFocus(eventArgs);
        WriteSelectedDateText();
    }

    protected override void OnCalendarOpened(RoutedEventArgs eventArgs)
    {
        base.OnCalendarOpened(eventArgs);
        DetachOpenCalendar();
        _openCalendar = PickerTemplateParts.GetCalendar(this);
        _openCalendar?.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnCalendarKeyDown), true);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (IsDropDownOpen && _openCalendar is not null)
            {
                Keyboard.Focus(_openCalendar);
            }
        }));
    }

    protected override void OnCalendarClosed(RoutedEventArgs eventArgs)
    {
        base.OnCalendarClosed(eventArgs);
        DetachOpenCalendar();
        WriteSelectedDateText();
        if (_returnFocusAfterClose)
        {
            _returnFocusAfterClose = false;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (!IsDropDownOpen && GetTemplateChild("PART_Button") is Button trigger)
                {
                    trigger.Focusable = true;
                    if (Window.GetWindow(this) is { } owner)
                    {
                        owner.Activate();
                        FocusManager.SetFocusedElement(owner, trigger);
                    }

                    trigger.Focus();
                    Keyboard.Focus(trigger);
                }
            }));
        }
    }

    private void OnCalendarKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && IsDropDownOpen)
        {
            var selection = _selectedDateAtOpen;
            _handlingEscapeSelection = true;
            _returnFocusAfterClose = true;
            try
            {
                SetCurrentValue(SelectedDateProperty, selection);
                IsDropDownOpen = false;
                eventArgs.Handled = true;
            }
            finally
            {
                // Complete this key route before restoring the uncoerced
                // value. Send priority clears the guard before later input;
                // the cancelled native value must not survive for recoercion.
                Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    try { SetCurrentValue(SelectedDateProperty, selection); }
                    finally
                    {
                        _handlingEscapeSelection = false;
                        CoerceValue(SelectedDateProperty);
                    }
                }));
            }
        }
    }

    private void DetachOpenCalendar()
    {
        _openCalendar?.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnCalendarKeyDown));
        _openCalendar = null;
    }

    private void WriteSelectedDateText()
    {
        if (_textBox is not null)
        {
            _textBox.Text = SelectedDate is { } date
                ? date.ToString(SpacedChineseDateFormat, System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
        }
    }
}

internal static class PickerTemplateParts
{

    internal static bool HasDatePickerContract(DatePicker picker)
    {
        picker.ApplyTemplate();
        return picker is WpfUiDatePicker &&
               WpfUiPickerStyles.IsBasedOn(picker.Style, WpfUiPickerStyles.DatePicker) &&
               picker.Template?.FindName("PART_TextBox", picker) is DatePickerTextBox &&
               picker.Template.FindName("PART_TextToggle", picker) is ToggleButton textToggle &&
               WpfUiPickerStyles.IsBasedOn(textToggle.Style, WpfUiPickerStyles.ToggleButton) &&
               picker.Template.FindName("PART_Button", picker) is Wpf.Ui.Controls.Button;
    }

    internal static ToggleButton? GetTextToggle(DatePicker picker)
    {
        picker.ApplyTemplate();
        return picker.Template?.FindName("PART_TextToggle", picker) as ToggleButton;
    }

    internal static Calendar? GetCalendar(DatePicker picker)
    {
        picker.ApplyTemplate();
        if (picker.Template?.FindName("PART_Popup", picker) is Popup popup &&
            popup.Child is Calendar popupCalendar)
        {
            return popupCalendar;
        }

        return picker.Template?.FindName("PART_Calendar", picker) as Calendar;
    }

    internal static bool HasComboBoxContract(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return WpfUiPickerStyles.IsBasedOn(comboBox.Style, WpfUiPickerStyles.ComboBox) &&
               GetTextToggle(comboBox) is { } textToggle &&
               WpfUiPickerStyles.IsBasedOn(textToggle.Style, WpfUiPickerStyles.ToggleButton) &&
               GetArrowToggle(comboBox) is not null &&
               GetSelectionPresenter(comboBox) is not null &&
               HasUnchangedArrowColumn(comboBox);
    }

    internal static ToggleButton? GetTextToggle(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return FindDescendant<ToggleButton>(
            comboBox,
            static candidate =>
                string.Equals(candidate.Name, "PART_TextToggle", StringComparison.Ordinal) &&
                !IsInsidePopup(candidate));
    }

    internal static ToggleButton? GetArrowToggle(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return FindDescendant<ToggleButton>(
            comboBox,
            static candidate =>
                candidate.ClickMode == ClickMode.Press &&
                !IsInsidePopup(candidate));
    }

    internal static ContentPresenter? GetSelectionPresenter(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return FindDescendant<ContentPresenter>(
            comboBox,
            static candidate => !IsInsidePopup(candidate));
    }

    internal static bool HasUnchangedArrowColumn(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        var columns = FindDescendant<Grid>(
            comboBox,
            static candidate =>
                candidate.ColumnDefinitions.Count == 2 &&
                !IsInsidePopup(candidate))?.ColumnDefinitions;
        return columns is { Count: 2 } &&
               columns[1].Width.IsAbsolute &&
               Math.Abs(columns[1].Width.Value - 36) < 0.01;
    }

    private static bool IsInsidePopup(DependencyObject source)
    {
        var cursor = source;
        while (cursor is not null)
        {
            if (cursor is Popup)
            {
                return true;
            }

            cursor = VisualTreeHelper.GetParent(cursor);
        }

        return false;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var nested = FindDescendant(child, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
internal static class SecondaryWindowChrome
{
    public static bool IsInteractiveDragSource(DependencyObject source, DependencyObject headerRoot)
    {
        for (DependencyObject? current = source;
             current is not null && !ReferenceEquals(current, headerRoot);
             current = InputParent(current))
        {
            if (current is Control)
            {
                return true;
            }
        }

        return false;
    }

    public static void DragFromHeader(
        Window window,
        MouseButtonEventArgs eventArgs,
        DependencyObject headerRoot,
        Action markManual,
        ref int nativeDragRequestCount,
        ref int nativeDragCompletionCount)
    {
        if (eventArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (eventArgs.OriginalSource is DependencyObject source &&
            IsInteractiveDragSource(source, headerRoot))
        {
            return;
        }

        eventArgs.Handled = true;
        nativeDragRequestCount++;
        try
        {
            window.DragMove();
            nativeDragCompletionCount++;
        }
        catch (InvalidOperationException)
        {
            // Synthetic or canceled gestures cannot establish a completed physical drag.
        }
        finally
        {
            markManual();
        }
    }

    private static DependencyObject? InputParent(DependencyObject current) => current switch
    {
        Visual => VisualTreeHelper.GetParent(current),
        FrameworkContentElement content => content.Parent,
        _ => LogicalTreeHelper.GetParent(current)
    };
}

internal sealed class ConfirmationDialog : Wpf.Ui.Controls.MessageBox
{
    internal Button CancelButton { get; private set; } = null!;
    internal Button ConfirmButton { get; private set; } = null!;
    internal TextBlock BodyText { get; private set; } = null!;
    internal ScrollViewer BodyScroller { get; private set; } = null!;

    public ConfirmationDialog(
        Window owner,
        string title,
        string message,
        string confirmText = "确认删除",
        string confirmStyleKey = "DangerButton")
    {
        // #205：WPF-UI 只在本确认框的 Window.Resources 中合并；不触碰
        // Application.Current.Resources，避免其隐式样式进入主窗、悬浮窗和其他次级窗口。
        Resources = CreateLocalWpfUiResources();
        // Implicit styles are keyed by the exact control type; this subclass
        // therefore applies WPF-UI's MessageBox style explicitly while keeping
        // the style and all merged dictionaries local to this window.
        Style = (Style)FindResource(typeof(Wpf.Ui.Controls.MessageBox));
        Owner = owner;
        Title = title;
        Width = 390;
        Height = Math.Min(360, Math.Max(240, SystemParameters.WorkArea.Height - 48));
        MaxWidth = 390;
        MaxHeight = Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = false;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");
        Foreground = (Brush)FindResource("InkBrush");

        // WPF-UI MessageBox 的 Primary 按钮默认响应 Enter；把它承载为「取消」后，
        // Enter 与 Esc 都落到安全路径，Secondary 才是唯一的确认路径。
        PrimaryButtonText = "取消";
        PrimaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        IsPrimaryButtonEnabled = true;
        SecondaryButtonText = confirmText;
        SecondaryButtonAppearance = string.Equals(confirmStyleKey, "DangerButton", StringComparison.Ordinal)
            ? Wpf.Ui.Controls.ControlAppearance.Danger
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
        IsSecondaryButtonEnabled = true;
        CloseButtonText = string.Empty;
        IsCloseButtonEnabled = false;

        var layout = new Grid { Margin = new Thickness(24) };
        layout.SetResourceReference(TextElement.FontSizeProperty, "SecondaryWindowBodyFontSize");
        AutomationProperties.SetAutomationId(layout, "ConfirmationContentRoot");
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold
        };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowTitleFontSize");
        AutomationProperties.SetAutomationId(heading, "ConfirmationHeading");
        var body = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        BodyText = body;
        AutomationProperties.SetAutomationId(body, "ConfirmationBody");
        var bodyScroller = new ScrollViewer
        {
            Content = body,
            Margin = new Thickness(0, 14, 0, 18),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        BodyScroller = bodyScroller;
        AutomationProperties.SetAutomationId(bodyScroller, "ConfirmationBodyScroller");
        Grid.SetRow(bodyScroller, 1);
        layout.Children.Add(heading);
        layout.Children.Add(bodyScroller);
        var surface = new Border
        {
            Child = layout,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        Content = surface;
    }

    // WPF-UI MessageBox intentionally hides Window.Show/ShowDialog/Close and asks
    // consumers to use ShowDialogAsync. The existing product seam is synchronous,
    // so keep that seam while delegating the actual modal loop to WPF-UI.
    public new void Show() => ((Window)this).Show();

    public new bool? ShowDialog()
    {
        var result = ShowDialogAsync().GetAwaiter().GetResult();
        return result switch
        {
            Wpf.Ui.Controls.MessageBoxResult.Secondary => true,
            Wpf.Ui.Controls.MessageBoxResult.Primary => false,
            _ => null
        };
    }

    public new void Close() => ((Window)this).Close();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        // MessageBox's Loaded handler runs before all TemplateBinding values are
        // observable on the generated button elements. Defer one dispatcher
        // turn, then select by the template's command parameter rather than by
        // translated button text or visual-tree position.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(ConfigureTemplateButtons));
    }

    private void ConfigureTemplateButtons()
    {
        ApplyTemplate();
        UpdateLayout();

        var buttons = Descendants<Button>(this).ToArray();
        CancelButton = buttons.SingleOrDefault(button =>
            button.CommandParameter is Wpf.Ui.Controls.MessageBoxButton command &&
            command == Wpf.Ui.Controls.MessageBoxButton.Primary)
            ?? throw new InvalidOperationException("WPF-UI 确认框缺少 Primary 取消按钮。");
        ConfirmButton = buttons.SingleOrDefault(button =>
            button.CommandParameter is Wpf.Ui.Controls.MessageBoxButton command &&
            command == Wpf.Ui.Controls.MessageBoxButton.Secondary)
            ?? throw new InvalidOperationException("WPF-UI 确认框缺少 Secondary 确认按钮。");

        // WPF-UI keeps its zero-width Close button registered as an Esc target.
        // Multiple IsCancel buttons make WPF cycle focus instead of clicking,
        // so only the safe Primary action may own the Enter and Esc access keys.
        foreach (var button in buttons.Where(button =>
                     button.CommandParameter is Wpf.Ui.Controls.MessageBoxButton))
        {
            button.IsDefault = ReferenceEquals(button, CancelButton);
            button.IsCancel = ReferenceEquals(button, CancelButton);
        }
        UiAccessibility.Name(CancelButton, "取消");
        UiAccessibility.Name(ConfirmButton, SecondaryButtonText);
        AutomationProperties.SetAutomationId(CancelButton, "ConfirmationCancelButton");
        AutomationProperties.SetAutomationId(ConfirmButton, "ConfirmationConfirmButton");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static ResourceDictionary CreateLocalWpfUiResources()
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary
        {
            Theme = Wpf.Ui.Appearance.ApplicationTheme.Light
        });
        resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());

        // WPF-UI resource keys are local aliases of existing warm-pink semantic
        // tokens. These aliases are intentionally scoped to this dialog Window.
        resources["MessageBoxBackground"] = ApplicationResource("BackgroundBrush");
        resources["MessageBoxForeground"] = ApplicationResource("InkBrush");
        resources["MessageBoxTopOverlay"] = ApplicationResource("BackgroundBrush");
        resources["MessageBoxSeparatorBorderBrush"] = ApplicationResource("PeachLineBrush");
        resources["ButtonBackground"] = ApplicationResource("CardBrush");
        resources["ButtonBackgroundPointerOver"] = ApplicationResource("CardAltBrush");
        resources["ButtonBackgroundPressed"] = ApplicationResource("PeachTintBrush");
        resources["ButtonForeground"] = ApplicationResource("InkBrush");
        resources["ButtonForegroundPointerOver"] = ApplicationResource("InkBrush");
        resources["ButtonForegroundPressed"] = ApplicationResource("InkBrush");
        resources["AccentButtonBackground"] = ApplicationResource("MainActionBrush");
        resources["AccentButtonBackgroundPointerOver"] = ApplicationResource("MainActionHoverBrush");
        resources["AccentButtonBackgroundPressed"] = ApplicationResource("MainActionPressedBrush");
        resources["AccentButtonForeground"] = ApplicationResource("MainActionTextBrush");
        resources["AccentButtonForegroundPointerOver"] = ApplicationResource("MainActionTextBrush");
        resources["AccentButtonForegroundPressed"] = ApplicationResource("MainActionTextBrush");
        resources["PaletteRedColor"] = ApplicationResource("DangerColor");
        return resources;
    }

    private static object ApplicationResource(string key) =>
        Application.Current?.TryFindResource(key)
        ?? throw new InvalidOperationException($"缺少确认框主题资源：{key}");
}

// #155：进程退出（悬浮退出、托盘退出）在显示设置或编辑器脏时的确认入口。
// reasons 为空表示没有未保存内容，直接放行；确认框以取消为安全默认，
// 确认按钮为危险色「退出」，管理窗口关闭/隐藏不经过本入口（继续只 Hide 并保留草稿）。
internal static class ExitConfirmation
{
    public static bool Confirm(Window? owner, IReadOnlyList<string> reasons)
    {
        if (reasons.Count == 0)
        {
            return true;
        }

        var message =
            $"{string.Join(string.Empty, reasons)}退出后这些未保存内容将丢失。确定退出吗？";
        return new ConfirmationDialog(
            owner!,
            "退出敏敏子倒计时",
            message,
            confirmText: "退出",
            confirmStyleKey: "DangerButton").ShowDialog() == true;
    }
}
