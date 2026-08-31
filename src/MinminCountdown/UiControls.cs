using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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

internal static class PopupFocusLifecycle
{
    private static readonly HashSet<Popup> Attached = new();

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

        popup.Opened += (_, _) =>
        {
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
                if (eventArgs.Key == Key.Escape && popup.IsOpen)
                {
                    popup.IsOpen = false;
                    eventArgs.Handled = true;
                    trigger.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Input,
                        new Action(() =>
                        {
                            if (!trigger.IsVisible || !trigger.IsEnabled)
                            {
                                return;
                            }

                            if (Window.GetWindow(trigger) is { } window)
                            {
                                window.Activate();
                                FocusManager.SetFocusedElement(window, trigger);
                            }

                            Keyboard.Focus(trigger);
                        }));
                }
            }),
            true);
    }

    internal static bool IsAttached(Popup popup) => Attached.Contains(popup);
}

internal sealed class TagSelector : Grid
{
    private readonly Button _trigger;
    private readonly Popup _popup;
    private readonly Border _surface;
    private readonly TextBox _search;
    private readonly Button _create;
    private readonly StackPanel _items;
    private IReadOnlyList<TagRecord> _tags = Array.Empty<TagRecord>();
    private bool _rendering;

    public TagSelector()
    {
        _trigger = UiAccessibility.Name(
            new Button
            {
                Content = "选择标签",
                MinWidth = 116,
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
            PopupAnimation = PopupAnimation.Fade,
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

    public string SummaryText => _trigger.Content?.ToString() ?? string.Empty;

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
        _trigger.Content = text;
        _trigger.ToolTip = names.Length == 0 ? "选择标签" : $"已选标签：{fullSelection}";
        UiAccessibility.Name(_trigger, names.Length == 0 ? "选择标签" : $"已选标签：{fullSelection}");
    }
}

internal static class ThemedDatePickerBehavior
{
    private static readonly HashSet<DatePicker> Attached = new();
    private static readonly HashSet<DatePicker> PointerPressedOnText = new();

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
                picker.IsDropDownOpen = false;
                eventArgs.Handled = true;
                picker.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        picker.ApplyTemplate();
                        var focusTarget =
                            picker.Template.FindName("PART_Button", picker) as IInputElement ?? picker;
                        if (Window.GetWindow(picker) is { } window)
                        {
                            window.Activate();
                            FocusManager.SetFocusedElement(window, focusTarget);
                        }

                        Keyboard.Focus(focusTarget);
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
                    if (picker.IsDropDownOpen)
                    {
                        Keyboard.Focus(calendar);
                    }
                }));
        };

        picker.PreviewMouseLeftButtonDown += (_, eventArgs) =>
        {
            if (IsDateTextSource(eventArgs.OriginalSource as DependencyObject))
            {
                PointerPressedOnText.Add(picker);
            }
        };
        picker.PreviewMouseLeftButtonUp += (_, eventArgs) =>
        {
            if (PointerPressedOnText.Remove(picker) &&
                IsDateTextSource(eventArgs.OriginalSource as DependencyObject))
            {
                picker.IsDropDownOpen = true;
                eventArgs.Handled = true;
            }
        };
        picker.LostMouseCapture += (_, _) => PointerPressedOnText.Remove(picker);
        picker.Loaded += (_, _) => LockReadOnlyTextBox(picker);
        picker.GotKeyboardFocus += (_, _) => LockReadOnlyTextBox(picker);
        picker.LostKeyboardFocus += (_, _) => LockReadOnlyTextBox(picker);
        picker.CalendarClosed += (_, _) => LockReadOnlyTextBox(picker);
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

        textBox.IsReadOnly = true;
        textBox.Focusable = false;
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
