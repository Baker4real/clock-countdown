using System.Text;
using System.Windows;
using System.Windows.Automation;
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
                popup.IsOpen = false;
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

                popup.IsOpen = false;
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
                    if (popup.IsOpen && initialFocus() is { } target)
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

                popup.IsOpen = false;
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
        PopupFocusLifecycle.Attach(_popup, _trigger, () => _search);
        _trigger.Click += (_, _) => _popup.IsOpen = !_popup.IsOpen;
        Children.Add(_popup);
    }

    public event EventHandler? SelectionChanged;

    public event Action<object?, string>? CreateRequested;

    public HashSet<string> SelectedTagIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPopupOpen
    {
        get => _popup.IsOpen;
        set => _popup.IsOpen = value;
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

internal static class ThemedDatePickerBehavior
{
    private static readonly HashSet<DatePicker> Attached = new();
    private static readonly HashSet<DatePicker> PointerPressedOnText = new();
    private static readonly HashSet<DatePickerTextBox> TextPointerAttached = new();
    private static readonly HashSet<DatePicker> RefocusTriggerAfterClose = new();

    public static void Attach(DatePicker picker)
    {
        if (!Attached.Add(picker))
        {
            return;
        }

        KeyEventHandler closeOnEscape = (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Escape && picker.IsDropDownOpen)
            {
                RefocusTriggerAfterClose.Add(picker);
                picker.IsDropDownOpen = false;
                eventArgs.Handled = true;
                picker.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        picker.ApplyTemplate();
                        FocusDatePickerTrigger(picker);
                    }));
            }
        };

        picker.CalendarOpened += (_, _) =>
        {
            if (GetCalendar(picker) is not { } calendar)
            {
                return;
            }

            calendar.RemoveHandler(Keyboard.PreviewKeyDownEvent, closeOnEscape);
            calendar.AddHandler(Keyboard.PreviewKeyDownEvent, closeOnEscape, true);
            picker.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    ApplyCalendarNavigationAccessibleNames(picker);
                    if (picker.IsDropDownOpen)
                    {
                        Keyboard.Focus(calendar);
                    }
                }));
        };

        picker.Loaded += (_, _) => LockReadOnlyTextBox(picker);
        picker.IsVisibleChanged += (_, eventArgs) =>
        {
            if (eventArgs.NewValue is true)
            {
                LockReadOnlyTextBox(picker);
            }
        };
        picker.GotKeyboardFocus += (_, _) => LockReadOnlyTextBox(picker);
        picker.LostKeyboardFocus += (_, _) => LockReadOnlyTextBox(picker);
        picker.CalendarClosed += (_, _) =>
        {
            LockReadOnlyTextBox(picker);
            if (RefocusTriggerAfterClose.Remove(picker))
            {
                picker.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() => FocusDatePickerTrigger(picker)));
            }
        };
        if (picker.IsLoaded)
        {
            LockReadOnlyTextBox(picker);
        }
    }

    internal static void LockReadOnlyTextBox(DatePicker picker)
    {
        // 折叠控件不提前套模板：模板样式已保证只读与不可聚焦，
        // 控件真实可见后由 GotKeyboardFocus/CalendarClosed 再补本地锁定（如提醒中心自定义稍后日期）。
        if (picker.Visibility != Visibility.Visible)
        {
            return;
        }

        if (!picker.IsLoaded && picker.Template is null)
        {
            return;
        }

        picker.ApplyTemplate();
        if (picker.Template?.FindName("PART_TextBox", picker) is not DatePickerTextBox textBox)
        {
            return;
        }

        if (TextPointerAttached.Add(textBox))
        {
            textBox.PreviewMouseLeftButtonDown += (_, _) =>
                PointerPressedOnText.Add(picker);
            textBox.PreviewMouseLeftButtonUp += (_, eventArgs) =>
            {
                if (!PointerPressedOnText.Remove(picker))
                {
                    return;
                }

                picker.IsDropDownOpen = true;
                eventArgs.Handled = true;
            };
            textBox.LostMouseCapture += (_, _) => PointerPressedOnText.Remove(picker);
        }

        textBox.IsReadOnly = true;
        textBox.Focusable = false;
        AlignReadOnlyContentHostPadding(picker, textBox);
        ApplyOpenCalendarAccessibleName(picker);
    }

    /// <summary>
    /// 右缘裁切修复（issue #139）：App 的 DatePicker 模板把 DatePicker.Padding 经 TemplateBinding
    /// 映射为 PART_ContentHost 的 Margin，而 TextBoxBase 又会把 DatePickerTextBox.Padding 动态同步成
    /// 同一 ScrollViewer 的 Padding，两者叠加后窄日期框（便签日期 244 宽）内 Short 日期放不下，
    /// 文本被右缘裁切，文本起点也偏离 §4「图标与文字间距约 12 DIP」的单一留白意图。
    /// 这里把内容宿主的 Padding 归零让 Margin 单份生效；DatePickerTextBox.Padding 保持模板值
    /// 48/16 不变，CompactInputHorizontalPaddingAligned 合同继续按模板值通过。
    /// 仅作用于主窗口工作区的主题化日期框，不触碰悬浮窗与提醒中心的日期框基线。
    /// </summary>
    private static void AlignReadOnlyContentHostPadding(DatePicker picker, DatePickerTextBox textBox)
    {
        if (Window.GetWindow(picker) is not MainWindow)
        {
            return;
        }

        if (textBox.Template?.FindName("PART_ContentHost", textBox) is ScrollViewer contentHost &&
            contentHost.Padding != default)
        {
            contentHost.Padding = default;
        }
    }

    internal static void ApplyOpenCalendarAccessibleName(DatePicker picker)
    {
        picker.ApplyTemplate();
        if (picker.Template.FindName("PART_Button", picker) is Button button)
        {
            AutomationProperties.SetName(button, "打开日历");
        }
    }

    private static void FocusDatePickerTrigger(DatePicker picker)
    {
        picker.ApplyTemplate();
        var focusTarget = picker.Template.FindName("PART_Button", picker) as IInputElement ?? picker;
        if (Window.GetWindow(picker) is { } window)
        {
            window.Activate();
            FocusManager.SetFocusedElement(window, focusTarget);
        }

        if (focusTarget is UIElement element)
        {
            element.Focusable = true;
            element.Focus();
        }

        Keyboard.Focus(focusTarget);
    }

    public static bool IsAttached(DatePicker picker) => Attached.Contains(picker);

    internal static Calendar? GetCalendar(DatePicker picker)
    {
        picker.ApplyTemplate();
        if (picker.Template.FindName("PART_Popup", picker) is Popup popup &&
            popup.Child is Calendar popupCalendar)
        {
            return popupCalendar;
        }

        return picker.Template.FindName("PART_Calendar", picker) as Calendar;
    }

    internal static void ApplyCalendarNavigationAccessibleNames(DatePicker picker)
    {
        var calendar = GetCalendar(picker);
        if (calendar is null)
        {
            return;
        }

        calendar.ApplyTemplate();
        var item = calendar.Template.FindName("PART_CalendarItem", calendar) as CalendarItem;
        item?.ApplyTemplate();
        var previous = item?.Template.FindName("PART_PreviousButton", item) as Button;
        var next = item?.Template.FindName("PART_NextButton", item) as Button;
        previous ??= FindNamedCalendarButton(calendar, "上一个月");
        next ??= FindNamedCalendarButton(calendar, "下一个月");
        if (previous is not null)
        {
            AutomationProperties.SetName(previous, "上一个月");
        }

        if (next is not null)
        {
            AutomationProperties.SetName(next, "下一个月");
        }
    }

    private static Button? FindNamedCalendarButton(DependencyObject root, string name)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                (string.Equals(button.ToolTip as string, name, StringComparison.Ordinal) ||
                 string.Equals(button.Name, name == "上一个月" ? "PART_PreviousButton" : "PART_NextButton", StringComparison.Ordinal)))
            {
                return button;
            }

            var nested = FindNamedCalendarButton(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool IsDateTextSource(DependencyObject? source)
    {
        var cursor = source;
        while (cursor is not null)
        {
            if (cursor is DatePickerTextBox)
            {
                return true;
            }

            cursor = VisualTreeHelper.GetParent(cursor);
        }

        return false;
    }
}

internal static class ThemedComboBoxBehavior
{
    private static readonly HashSet<ComboBox> Attached = new();
    private static readonly HashSet<ComboBox> PointerPressedOnText = new();

    public static void Attach(ComboBox comboBox)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        if (!Attached.Add(comboBox))
        {
            return;
        }

        comboBox.PreviewMouseLeftButtonDown += (_, eventArgs) =>
        {
            if (IsComboTextSource(comboBox, eventArgs.OriginalSource as DependencyObject))
            {
                PointerPressedOnText.Add(comboBox);
            }
        };
        comboBox.PreviewMouseLeftButtonUp += (_, eventArgs) =>
        {
            if (PointerPressedOnText.Remove(comboBox) &&
                comboBox.IsEnabled &&
                IsComboTextSource(comboBox, eventArgs.OriginalSource as DependencyObject))
            {
                eventArgs.Handled = true;
                comboBox.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    () =>
                    {
                        if (comboBox.IsEnabled)
                        {
                            comboBox.IsDropDownOpen = true;
                        }
                    });
            }
        };
        comboBox.LostMouseCapture += (_, _) => PointerPressedOnText.Remove(comboBox);
        comboBox.PreviewKeyDown += (_, eventArgs) =>
        {
            if (!comboBox.IsEnabled)
            {
                return;
            }

            var isDown = eventArgs.Key == Key.Down || eventArgs.SystemKey == Key.Down;
            if (!isDown ||
                (MainShortcutInput.ResolveModifiers(eventArgs) & ModifierKeys.Alt) != ModifierKeys.Alt)
            {
                return;
            }

            comboBox.IsDropDownOpen = !comboBox.IsDropDownOpen;
            eventArgs.Handled = true;
        };
    }

    public static bool IsAttached(ComboBox comboBox) => Attached.Contains(comboBox);

    internal static ToggleButton? GetArrowToggle(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return FindDescendant<ToggleButton>(comboBox, static candidate =>
            candidate.ClickMode == ClickMode.Press &&
            !IsInsidePopup(candidate));
    }

    internal static ContentPresenter? GetSelectionPresenter(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return FindDescendant<ContentPresenter>(comboBox, static candidate =>
            !IsInsidePopup(candidate));
    }

    internal static bool HasUnchangedArrowColumn(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        var columns = FindDescendant<Grid>(comboBox, static candidate =>
            candidate.ColumnDefinitions.Count == 2 &&
            !IsInsidePopup(candidate))?.ColumnDefinitions;
        return columns is { Count: 2 } &&
            columns[1].Width.IsAbsolute &&
            Math.Abs(columns[1].Width.Value - 36) < 0.01;
    }

    private static bool IsComboTextSource(ComboBox comboBox, DependencyObject? source)
    {
        if (source is null || IsInsidePopup(source) || !comboBox.IsEnabled)
        {
            return false;
        }

        var cursor = source;
        while (cursor is not null && !ReferenceEquals(cursor, comboBox))
        {
            if (cursor is ToggleButton)
            {
                return false;
            }

            cursor = VisualTreeHelper.GetParent(cursor);
        }

        return ReferenceEquals(cursor, comboBox);
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

internal sealed class ConfirmationDialog : Window
{
    internal Button CancelButton { get; private set; } = null!;
    internal Button ConfirmButton { get; private set; } = null!;
    internal TextBlock BodyText { get; private set; } = null!;
    internal ScrollViewer BodyScroller { get; private set; } = null!;

    public ConfirmationDialog(Window owner, string title, string message)
    {
        Owner = owner;
        Title = title;
        Width = 390;
        Height = Math.Min(360, Math.Max(240, SystemParameters.WorkArea.Height - 48));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");

        var cancel = UiAccessibility.Name(
            new Button
            {
                Content = "取消",
                IsDefault = true,
                IsCancel = true,
                MinWidth = 82,
                Style = (Style)FindResource("SecondaryButton")
            },
            "取消");
        CancelButton = cancel;
        AutomationProperties.SetAutomationId(cancel, "ConfirmationCancelButton");
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var confirm = UiAccessibility.Name(
            new Button
            {
                Content = "确认删除",
                IsDefault = false,
                MinWidth = 96,
                Margin = new Thickness(8, 0, 0, 0),
                Style = (Style)FindResource("DangerButton")
            },
            "确认删除");
        ConfirmButton = confirm;
        AutomationProperties.SetAutomationId(confirm, "ConfirmationConfirmButton");
        confirm.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);

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
        Grid.SetRow(actions, 2);
        layout.Children.Add(heading);
        layout.Children.Add(bodyScroller);
        layout.Children.Add(actions);
        var surface = new Border
        {
            Child = layout,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        Content = surface;
    }
}
