using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown;

// #235：可聚焦的便签卡。选中状态通过行名称与 ItemStatus 暴露，数量由独立直播区播报。
internal sealed class NoteSelectionCard : Border
{
    internal string SurfaceResource { get; init; } = "NoteBrush";
    internal string BoundaryResource { get; init; } = "NoteLineBrush";
    internal bool IsSelected { get; set; }
    protected override AutomationPeer OnCreateAutomationPeer() => new NoteSelectionPeer(this);
    private sealed class NoteSelectionPeer(NoteSelectionCard owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(NoteSelectionCard);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        protected override string GetItemStatusCore() => owner.IsSelected ? "已选择" : "未选择";
    }
}

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
    // LiveRegionChanged；相同文本（分钟级刷新、重复筛选摘要）不重复播报。
    // XAML 已写上初值时，补齐尚未设置的可访问名并只播报这一次。
    internal static event Action<TextBlock>? Announced;
    public static void Announce(TextBlock target, string text, Action<TextBlock>? afterText = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.Equals(target.Text, text, StringComparison.Ordinal))
        {
            if (!string.Equals(AutomationProperties.GetName(target), text, StringComparison.Ordinal))
            {
                AutomationProperties.SetName(target, text);
                afterText?.Invoke(target);
                RaiseLiveRegionChanged(target);
                Announced?.Invoke(target);
                return;
            }

            afterText?.Invoke(target);
            return;
        }

        target.Text = text;
        AutomationProperties.SetName(target, text);
        afterText?.Invoke(target);
        RaiseLiveRegionChanged(target);
        Announced?.Invoke(target);
    }

    private static void RaiseLiveRegionChanged(TextBlock target)
    {
        var peer = UIElementAutomationPeer.FromElement(target)
            ?? UIElementAutomationPeer.CreatePeerForElement(target);
        if (peer is null)
        {
            return;
        }

        if (!HasAutomationProvider(target, peer) && Window.GetWindow(target) is Window owner)
        {
            owner.UpdateLayout();
            var windowPeer = UIElementAutomationPeer.FromElement(owner)
                ?? UIElementAutomationPeer.CreatePeerForElement(owner);
            if (windowPeer is not null)
            {
                RealizeAutomationTree(windowPeer);
            }

            peer = UIElementAutomationPeer.FromElement(target) ?? peer;
        }

        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // #316：通过受保护的公开框架 API AutomationPeer.ProviderFromPeer 探测 provider，不再反射非公开成员。
    private static bool HasAutomationProvider(UIElement target, AutomationPeer peer) =>
        new AutomationProviderProbePeer(target).HasProvider(peer);

    private sealed class AutomationProviderProbePeer(UIElement owner) : UIElementAutomationPeer(owner)
    {
        internal bool HasProvider(AutomationPeer target) =>
            ProviderFromPeer(target.EventsSource ?? target) is not null;
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

internal enum InlineActionSurface
{
    MainRow,
    TagManager,
    ReminderRule
}

internal enum InlineActionIntent
{
    Edit,
    Copy,
    Delete
}

// #257/#308：三种既有表面共用一份行内动作契约。调用方只提供表面、动作和记录上下文；
// Content、Name/HelpText、样式、危险语义、焦点资格与 ToolTip 不再分别补齐。
internal static class InlineActionButton
{
    internal const double MainRowMinHeight = 33;

    public static Button Create(
        FrameworkElement scope,
        InlineActionSurface surface,
        InlineActionIntent intent,
        string id,
        string accessibleName,
        string? recordKind = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(accessibleName);
        var supported = surface switch
        {
            InlineActionSurface.MainRow => intent is InlineActionIntent.Edit or InlineActionIntent.Delete,
            InlineActionSurface.TagManager => intent == InlineActionIntent.Delete,
            InlineActionSurface.ReminderRule => intent is InlineActionIntent.Edit or InlineActionIntent.Copy or InlineActionIntent.Delete,
            _ => false
        };
        if (!supported)
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "该表面没有此行内动作。");
        }
        if (surface == InlineActionSurface.MainRow && intent == InlineActionIntent.Delete &&
            string.IsNullOrWhiteSpace(recordKind))
        {
            throw new ArgumentException("主行删除必须提供记录类型。", nameof(recordKind));
        }

        var content = intent switch
        {
            InlineActionIntent.Edit => "编辑",
            InlineActionIntent.Copy => "复制",
            InlineActionIntent.Delete => "删除",
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "未知行内动作。")
        };
        var danger = intent == InlineActionIntent.Delete;
        var button = new Button
        {
            Content = content,
            Tag = id,
            Focusable = true,
            Style = surface switch
            {
                InlineActionSurface.ReminderRule when !danger => (Style)scope.FindResource(typeof(Button)),
                _ when danger && surface != InlineActionSurface.MainRow => (Style)scope.FindResource("DangerButton"),
                _ => (Style)scope.FindResource("SecondaryButton")
            }
        };

        switch (surface)
        {
            case InlineActionSurface.MainRow:
                button.MinWidth = 53;
                button.MinHeight = MainRowMinHeight;
                button.Margin = new Thickness(4, 0, 0, 0);
                button.FontSize = (double)scope.FindResource("TypeCaptionFontSize");
                if (intent == InlineActionIntent.Edit)
                {
                    button.ContentTemplate = CreateGlyphButtonTemplate(
                        "M 2.5,13.2 L 3.2,10.4 L 11.6,2 C 12.2,1.4 13.2,1.4 13.8,2 C 14.4,2.6 14.4,3.6 13.8,4.2 L 5.4,12.6 L 2.5,13.2 Z",
                        "InkBrush");
                }
                else
                {
                    button.SetResourceReference(Control.ForegroundProperty, "DangerBrush");
                    button.BorderBrush = Brushes.Transparent;
                    button.ContentTemplate = CreateGlyphButtonTemplate(
                        "M 3.4,4.5 L 14.6,4.5 M 6.2,4.5 L 6.2,3 C 6.2,2.4 6.7,2 7.3,2 L 10.7,2 C 11.3,2 11.8,2.4 11.8,3 L 11.8,4.5 M 4.6,4.5 L 5.3,14.2 C 5.3,15.2 6.1,16 7.1,16 L 10.9,16 C 11.9,16 12.7,15.2 12.7,14.2 L 13.4,4.5",
                        "DangerBrush");
                }
                break;
            case InlineActionSurface.TagManager:
                button.MinWidth = 65;
                button.MinHeight = 39;
                button.Margin = new Thickness(7, 0, 0, 0);
                button.FontSize = (double)scope.FindResource("TypeCaptionFontSize");
                break;
            case InlineActionSurface.ReminderRule:
                button.MinWidth = 58;
                button.Margin = new Thickness(6, 0, 0, 0);
                break;
        }

        var foldedName = UiAccessibility.FoldAccessibleName(accessibleName);
        UiAccessibility.Name(button, foldedName);
        AutomationProperties.SetHelpText(button, foldedName);
        button.ToolTip = surface == InlineActionSurface.MainRow && danger
            ? $"删除{recordKind}（Delete）"
            : foldedName;
        KeyboardFocusPolicy.Apply(button);
        return button;
    }

    // 主行 Content 仍是可见字符串；铅笔/垃圾桶只经 ContentTemplate 注入，几何和笔画保持 #247 原值。
    private static DataTemplate CreateGlyphButtonTemplate(string glyphData, string glyphBrushKey)
    {
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var glyph = new FrameworkElementFactory(typeof(WpfPath));
        glyph.Name = "Glyph";
        glyph.SetValue(WpfPath.WidthProperty, 12.0);
        glyph.SetValue(WpfPath.HeightProperty, 12.0);
        glyph.SetValue(WpfPath.DataProperty, Geometry.Parse(glyphData));
        glyph.SetValue(WpfPath.StretchProperty, Stretch.Uniform);
        glyph.SetValue(WpfPath.StrokeThicknessProperty, 1.6);
        glyph.SetValue(WpfPath.StrokeStartLineCapProperty, PenLineCap.Round);
        glyph.SetValue(WpfPath.StrokeEndLineCapProperty, PenLineCap.Round);
        glyph.SetValue(WpfPath.StrokeLineJoinProperty, PenLineJoin.Round);
        glyph.SetValue(WpfPath.VerticalAlignmentProperty, VerticalAlignment.Center);
        glyph.SetResourceReference(WpfPath.StrokeProperty, glyphBrushKey);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(TextBlock.MarginProperty, new Thickness(5, 0, 0, 0));
        label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(TextBlock.TextProperty, new System.Windows.Data.Binding());
        row.AppendChild(glyph);
        row.AppendChild(label);
        return new DataTemplate { VisualTree = row };
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
/// #316：悬浮窗底部状态中可朗读、并在金额模式下可调用的文本元素。
/// 鼓励语由独立的 <see cref="SilentTextBlock"/> 互斥承载，不再反射或清除 WPF 内部 AutomationPeer 缓存。
/// </summary>
internal sealed class InteractiveStatusTextBlock : TextBlock
{
    private bool _isInvokable;
    private bool _pointerPressed;

    internal event EventHandler? Invoked;

    internal bool IsInvokable
    {
        get => _isInvokable;
        set
        {
            _isInvokable = value;
            if (!value)
            {
                _pointerPressed = false;
                KeyboardFocusPolicy.Clear(this);
            }
            Focusable = value;
            KeyboardNavigation.SetIsTabStop(this, value);
            Cursor = value ? Cursors.Hand : null;
            AutomationProperties.SetHelpText(this, value ? "按 Enter 或空格切换金额显示" : string.Empty);
        }
    }

    private void InvokeAction()
    {
        if (IsInvokable && IsEnabled) Invoked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInvokable || !IsEnabled) return;
        _pointerPressed = true;
        KeyboardFocusPolicy.FocusFromPointer(this);
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pointerPressed = false;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var invoke = _pointerPressed && IsInvokable && IsEnabled;
        _pointerPressed = false;
        if (!invoke) return;
        InvokeAction();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsInvokable || !IsEnabled || e.Key is not (Key.Enter or Key.Space)) return;
        KeyboardFocusPolicy.ShowForKeyboardInput(this);
        if (!e.IsRepeat) InvokeAction();
        e.Handled = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _pointerPressed = false;
        KeyboardFocusPolicy.Clear(this);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new InteractiveStatusTextPeer(this);

    private sealed class InteractiveStatusTextPeer(InteractiveStatusTextBlock owner)
        : TextBlockAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            owner.IsInvokable ? AutomationControlType.Button : AutomationControlType.Text;

        protected override bool IsControlElementCore() =>
            owner.IsVisible && base.IsControlElementCore();

        protected override bool IsContentElementCore() =>
            owner.IsVisible && base.IsContentElementCore();

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke && owner.IsInvokable && owner.IsVisible
                ? this
                : base.GetPattern(patternInterface);

        void IInvokeProvider.Invoke()
        {
            if (!owner.IsEnabled || !owner.IsInvokable || !owner.IsVisible)
            {
                throw new ElementNotEnabledException();
            }

            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(owner.InvokeAction));
        }
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
    // Dynamic note rows replace their menus on refresh. Keep lifecycle state on
    // the popup so registration cannot root discarded menus or their host tree.
    private static readonly DependencyProperty IsAttachedProperty = DependencyProperty.RegisterAttached(
        "IsAttached", typeof(bool), typeof(PopupFocusLifecycle), new PropertyMetadata(false));
    private static readonly DependencyProperty ClipTargetProperty = DependencyProperty.RegisterAttached(
        "ClipTarget", typeof(FrameworkElement), typeof(PopupFocusLifecycle), new PropertyMetadata(null));

    public static void Attach(
        Popup popup,
        UIElement trigger,
        Func<IInputElement?> initialFocus,
        Action? onEscapeBeforeClose = null)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(initialFocus);

        if (IsAttached(popup))
        {
            return;
        }

        if (popup.Child is not UIElement surface)
        {
            throw new InvalidOperationException("弹层必须先设置可聚焦的内容容器。");
        }

        popup.SetValue(IsAttachedProperty, true);

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

                if (popup.IsOpen)
                {
                    onEscapeBeforeClose?.Invoke();
                }

                // #242：Esc 是应用处理的键盘路径，立即关闭、不保留退出阶段。
                PopupMotionBehavior.Close(popup, immediate: true);
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

                onEscapeBeforeClose?.Invoke();
                PopupMotionBehavior.Close(popup, immediate: true);
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
        popup.SetValue(ClipTargetProperty, canvas);
    }

    private static void ClampPopupToCanvas(Popup popup)
    {
        if (popup.GetValue(ClipTargetProperty) is not FrameworkElement canvas ||
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

    internal static bool IsAttached(Popup popup) => (bool)popup.GetValue(IsAttachedProperty);

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
        var tagGlyph = new System.Windows.Shapes.Path
        {
            Width = 20,
            Height = 20,
            Data = Geometry.Parse(
                "M 2.5,3.5 L 2.5,9.2 C 2.5,10 2.8,10.8 3.4,11.4 L 11.6,19.6 C 12.7,20.7 14.4,20.7 15.5,19.6 L 19.6,15.5 C 20.7,14.4 20.7,12.7 19.6,11.6 L 11.4,3.4 C 10.8,2.8 10,2.5 9.2,2.5 L 3.5,2.5 C 2.9,2.5 2.5,2.9 2.5,3.5 Z M 7,7 L 7.02,7"),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center
        };
        tagGlyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedBrush");
        triggerContent.Children.Add(tagGlyph);
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
        var itemsScroller = new ScrollViewer
        {
            Content = _items,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        itemsScroller.SetResourceReference(FocusVisualStyleProperty, "MainKeyboardFocusVisual");
        panel.Children.Add(itemsScroller);
        panel.Children.Add(_create);

        _surface = new Border
        {
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 0),
            Child = panel,
            MinWidth = 230
        };
        // #250/#305：标签选择浮层与筛选、标签管理、显示设置浮层同用 RadiusOverlay 与 ControlBorderBrush（≥3:1）。
        _surface.SetResourceReference(Border.CornerRadiusProperty, "RadiusOverlay");
        _surface.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        _surface.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
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
        ReferenceEquals(_surface.BorderBrush, Application.Current.FindResource("ControlBorderBrush"));

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
                ? DateDisplay.FullDate(DateOnly.FromDateTime(date))
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
    private static ResourceDictionary? _sharedWpfUiResources;
    private Style? _dayCancelStyle;
    private Style? _dayConfirmStyle;
    private bool _systemContrastApplied;

    internal void ApplyTheme()
    {
        // 局部别名复制的是画刷实例；由共享字典随 PaletteChanged 统一重取，此处补齐按钮系统对比度样式。
        if (_sharedWpfUiResources is not null)
        {
            ApplyThemeAliases(_sharedWpfUiResources);
        }
        if (CancelButton is null || ConfirmButton is null || _systemContrastApplied == ApplicationTheme.HighContrastEnabled) return;
        if (ApplicationTheme.HighContrastEnabled)
        {
            _dayCancelStyle = CancelButton.Style;
            _dayConfirmStyle = ConfirmButton.Style;
            CancelButton.Style = (Style)Application.Current.FindResource("SecondaryButton");
            ConfirmButton.Style = (Style)Application.Current.FindResource("PrimaryButton");
        }
        else
        {
            CancelButton.Style = _dayCancelStyle;
            ConfirmButton.Style = _dayConfirmStyle;
        }
        _systemContrastApplied = ApplicationTheme.HighContrastEnabled;
    }
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
        // #205/#314：WPF-UI 资源字典进程内只构建一次并在确认框实例间共享；始终留在
        // 确认框窗口级 Resources，不触碰 Application.Current.Resources。
        Resources = GetSharedWpfUiResources();
        // Implicit styles are keyed by the exact control type; this subclass
        // therefore applies WPF-UI's MessageBox style explicitly while keeping
        // the style and all merged dictionaries local to this window.
        Style = (Style)FindResource(typeof(Wpf.Ui.Controls.MessageBox));
        Owner = owner;
        Title = title;
        Width = 390;
        Height = Math.Min(360, Math.Max(240, MonitorWorkAreaResolver.ResolveForWindow(owner).WorkAreaDip.Height - 48));
        MaxWidth = 390;
        MaxHeight = Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowWorkAreaGeometry.TrackChanges(this, () => WindowWorkAreaGeometry.Fit(this,
            MonitorWorkAreaResolver.ResolveForWindow(this).WorkAreaDip, 18));
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = false;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");
        SetResourceReference(ForegroundProperty, "InkBrush");

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
            BorderThickness = new Thickness(1)
        };
        surface.SetResourceReference(Border.CornerRadiusProperty, "RadiusCard");
        surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        Content = surface;
        ApplicationTheme.PaletteChanged += OnPaletteChanged;
        Closed += (_, _) => ApplicationTheme.PaletteChanged -= OnPaletteChanged;
    }

    private void OnPaletteChanged(object? sender, EventArgs args) => ApplyTheme();

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

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // #314：正文已保留标题（ConfirmationHeading），标题栏不再重复显示同一文字；
        // Window.Title 与窗口自动化名称保持不变。
        if (GetTemplateChild("Title") is UIElement templateTitle)
        {
            templateTitle.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();
        WindowWorkAreaGeometry.Fit(this,
            MonitorWorkAreaResolver.ResolveForWindow(Owner).WorkAreaDip, 18, centerOnOwner: true);

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
        ApplyTheme();
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

    private static ResourceDictionary GetSharedWpfUiResources()
    {
        if (_sharedWpfUiResources is null)
        {
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary
            {
                Theme = Wpf.Ui.Appearance.ApplicationTheme.Light
            });
            resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            ApplyThemeAliases(resources);
            _sharedWpfUiResources = resources;
            ApplicationTheme.PaletteChanged += OnSharedPaletteChanged;
        }

        return _sharedWpfUiResources;
    }

    private static void OnSharedPaletteChanged(object? sender, EventArgs args)
    {
        if (_sharedWpfUiResources is not null)
        {
            ApplyThemeAliases(_sharedWpfUiResources);
        }
    }

    // WPF-UI resource keys are local aliases of existing warm-pink semantic
    // tokens. These aliases are intentionally scoped to this dialog Window.
    private static readonly (string WpfUiKey, string ThemeKey)[] ThemeAliases =
    [
        ("MessageBoxBackground", "BackgroundBrush"),
        ("MessageBoxForeground", "InkBrush"),
        ("MessageBoxTopOverlay", "BackgroundBrush"),
        ("MessageBoxSeparatorBorderBrush", "PeachLineBrush"),
        ("ButtonBackground", "CardBrush"),
        ("ButtonBackgroundPointerOver", "CardAltBrush"),
        ("ButtonBackgroundPressed", "PeachTintBrush"),
        ("ButtonForeground", "InkBrush"),
        ("ButtonForegroundPointerOver", "InkBrush"),
        ("ButtonForegroundPressed", "InkBrush"),
        ("AccentButtonBackground", "MainActionBrush"),
        ("AccentButtonBackgroundPointerOver", "MainActionHoverBrush"),
        ("AccentButtonBackgroundPressed", "MainActionPressedBrush"),
        ("AccentButtonForeground", "MainActionTextBrush"),
        ("AccentButtonForegroundPointerOver", "MainActionTextBrush"),
        ("AccentButtonForegroundPressed", "MainActionTextBrush"),
        ("PaletteRedColor", "DangerColor")
    ];

    private static void ApplyThemeAliases(ResourceDictionary resources)
    {
        var updates = new List<(string Key, object Value)>(ThemeAliases.Length);
        foreach (var (wpfUiKey, themeKey) in ThemeAliases)
        {
            var value = ApplicationResource(themeKey);
            if (!Equals(resources[wpfUiKey], value))
            {
                updates.Add((wpfUiKey, value));
            }
        }
        if (updates.Count == 0)
        {
            return;
        }

        resources.BeginInit();
        try
        {
            foreach (var (wpfUiKey, value) in updates)
            {
                resources[wpfUiKey] = value;
            }
        }
        finally
        {
            resources.EndInit();
        }
    }

    private static object ApplicationResource(string key) =>
        Application.Current?.TryFindResource(key)
        ?? throw new InvalidOperationException($"缺少确认框主题资源：{key}");
}

// #310：全应用控件禁用态的唯一实现。普通主题将禁用目标降至 0.45 不透明度，
// 系统对比度下恢复不透明并映射到系统禁用文字与控件面画刷；只接管未写本地值的属性，恢复时仅清理自身写入项。
internal static class DisabledStateBehavior
{
    internal const double DisabledOpacity = 0.45;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DisabledStateBehavior),
            new PropertyMetadata(false, OnConfigurationChanged));

    public static readonly DependencyProperty OpacityTargetNamesProperty =
        DependencyProperty.RegisterAttached(
            "OpacityTargetNames",
            typeof(string),
            typeof(DisabledStateBehavior),
            new PropertyMetadata(null, OnConfigurationChanged));

    public static readonly DependencyProperty ContrastBorderTargetNamesProperty =
        DependencyProperty.RegisterAttached(
            "ContrastBorderTargetNames",
            typeof(string),
            typeof(DisabledStateBehavior),
            new PropertyMetadata(null, OnConfigurationChanged));

    public static readonly DependencyProperty ApplyContrastBorderBrushProperty =
        DependencyProperty.RegisterAttached(
            "ApplyContrastBorderBrush",
            typeof(bool),
            typeof(DisabledStateBehavior),
            new PropertyMetadata(true, OnConfigurationChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(DisabledStateController),
            typeof(DisabledStateBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty ControlControllerProperty =
        DependencyProperty.RegisterAttached(
            "ControlController",
            typeof(DisabledStateController),
            typeof(DisabledStateBehavior),
            new PropertyMetadata(null));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetOpacityTargetNames(DependencyObject element, string? value) =>
        element.SetValue(OpacityTargetNamesProperty, value);

    public static string? GetOpacityTargetNames(DependencyObject element) =>
        (string?)element.GetValue(OpacityTargetNamesProperty);

    public static void SetContrastBorderTargetNames(DependencyObject element, string? value) =>
        element.SetValue(ContrastBorderTargetNamesProperty, value);

    public static string? GetContrastBorderTargetNames(DependencyObject element) =>
        (string?)element.GetValue(ContrastBorderTargetNamesProperty);

    public static void SetApplyContrastBorderBrush(DependencyObject element, bool value) =>
        element.SetValue(ApplyContrastBorderBrushProperty, value);

    public static bool GetApplyContrastBorderBrush(DependencyObject element) =>
        (bool)element.GetValue(ApplyContrastBorderBrushProperty);

    internal static void OnSystemContrastChanged(DependencyObject target)
    {
        var hostController = target.ReadLocalValue(ControllerProperty) as DisabledStateController;
        hostController?.Apply();
        if (target.ReadLocalValue(ControlControllerProperty) is DisabledStateController controlController &&
            !ReferenceEquals(controlController, hostController))
        {
            controlController.Apply();
        }
    }

    private static void OnConfigurationChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement host)
        {
            return;
        }

        if (GetIsEnabled(host))
        {
            if (host.ReadLocalValue(ControllerProperty) is DisabledStateController existing)
            {
                existing.RefreshConfiguration();
            }
            else
            {
                var controller = new DisabledStateController(host);
                host.SetValue(ControllerProperty, controller);
                controller.Attach();
            }
        }
        else if (host.ReadLocalValue(ControllerProperty) is DisabledStateController existing)
        {
            existing.Detach();
            host.ClearValue(ControllerProperty);
        }
    }

    private sealed class DisabledStateController(FrameworkElement host)
    {
        private readonly FrameworkElement _host = host;
        private readonly List<FrameworkElement> _appliedOpacityTargets = [];
        private readonly List<(FrameworkElement Target, DependencyProperty Property, object Token)> _appliedContrastBrushes = [];
        private Control? _control;

        internal void Attach()
        {
            _host.Initialized += OnHostReady;
            _host.Loaded += OnHostReady;
            _host.Unloaded += OnHostUnloaded;
            if (_host.IsInitialized || _host.IsLoaded)
            {
                ResolveAndApply();
            }
        }

        internal void Detach()
        {
            _host.Initialized -= OnHostReady;
            _host.Loaded -= OnHostReady;
            _host.Unloaded -= OnHostUnloaded;
            RestoreAll();
            if (_control is not null)
            {
                _control.IsEnabledChanged -= OnControlEnabledChanged;
                if (ReferenceEquals(_control.ReadLocalValue(ControlControllerProperty), this))
                {
                    _control.ClearValue(ControlControllerProperty);
                }

                _control = null;
            }
        }

        internal void RefreshConfiguration()
        {
            if (_control is null)
            {
                if (_host.IsInitialized || _host.IsLoaded)
                {
                    ResolveAndApply();
                }

                return;
            }

            RestoreAll();
            Apply();
        }

        private void OnHostReady(object? sender, EventArgs args) => ResolveAndApply();

        private void OnHostUnloaded(object sender, RoutedEventArgs args) => RestoreAll();

        private void OnControlEnabledChanged(object sender, DependencyPropertyChangedEventArgs args) => Apply();

        private void ResolveAndApply()
        {
            var resolved = _host.TemplatedParent as Control ?? _host as Control;
            if (resolved is null)
            {
                return;
            }

            if (!ReferenceEquals(_control, resolved))
            {
                if (_control is not null)
                {
                    RestoreAll();
                    _control.IsEnabledChanged -= OnControlEnabledChanged;
                    if (ReferenceEquals(_control.ReadLocalValue(ControlControllerProperty), this))
                    {
                        _control.ClearValue(ControlControllerProperty);
                    }
                }

                _control = resolved;
                if (_control.ReadLocalValue(ControlControllerProperty) is DisabledStateController previous &&
                    !ReferenceEquals(previous, this))
                {
                    previous.RestoreAll();
                }

                _control.SetValue(ControlControllerProperty, this);
                _control.IsEnabledChanged += OnControlEnabledChanged;
            }

            Apply();
        }

        internal void Apply()
        {
            if (_control is null)
            {
                return;
            }

            if (_control.IsEnabled)
            {
                RestoreAll();
                return;
            }

            if (ApplicationTheme.GetIsSystemContrast(_control))
            {
                RestoreOpacity();
                ApplyContrastBrushes();
            }
            else
            {
                RestoreContrastBrushes();
                ApplyDisabledOpacity();
            }
        }

        private void ApplyDisabledOpacity()
        {
            foreach (var target in ResolveTargets(GetOpacityTargetNames(_host), fallbackToHost: true))
            {
                if (_appliedOpacityTargets.Contains(target))
                {
                    continue;
                }

                if (target.ReadLocalValue(UIElement.OpacityProperty) != DependencyProperty.UnsetValue)
                {
                    continue;
                }

                target.SetValue(UIElement.OpacityProperty, DisabledOpacity);
                _appliedOpacityTargets.Add(target);
            }
        }

        private void ApplyContrastBrushes()
        {
            if (_control is null)
            {
                return;
            }

            EnsureContrastBrush(_control, Control.ForegroundProperty, "SystemContrastDisabledTextBrush");
            EnsureContrastBrush(_control, Control.BackgroundProperty, "SystemContrastControlBrush");
            if (GetApplyContrastBorderBrush(_host))
            {
                EnsureContrastBrush(_control, Control.BorderBrushProperty, "SystemContrastDisabledTextBrush");
            }

            foreach (var target in ResolveTargets(GetContrastBorderTargetNames(_host), fallbackToHost: false))
            {
                var borderProperty = target switch
                {
                    Border => Border.BorderBrushProperty,
                    Control => Control.BorderBrushProperty,
                    _ => null
                };
                if (borderProperty is not null)
                {
                    EnsureContrastBrush(target, borderProperty, "SystemContrastDisabledTextBrush");
                }
            }
        }

        private void EnsureContrastBrush(
            FrameworkElement target,
            DependencyProperty property,
            string resourceKey)
        {
            foreach (var entry in _appliedContrastBrushes)
            {
                if (ReferenceEquals(entry.Target, target) && ReferenceEquals(entry.Property, property))
                {
                    return;
                }
            }

            if (target.ReadLocalValue(property) != DependencyProperty.UnsetValue)
            {
                return;
            }

            target.SetResourceReference(property, resourceKey);
            var token = target.ReadLocalValue(property);
            if (token != DependencyProperty.UnsetValue)
            {
                _appliedContrastBrushes.Add((target, property, token));
            }
        }

        private IEnumerable<FrameworkElement> ResolveTargets(string? names, bool fallbackToHost)
        {
            if (string.IsNullOrWhiteSpace(names))
            {
                if (fallbackToHost)
                {
                    yield return _host;
                }

                yield break;
            }

            if (_control?.Template is not { } template)
            {
                yield break;
            }

            foreach (var rawName in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (template.FindName(rawName, _control) is FrameworkElement element)
                {
                    yield return element;
                }
            }
        }

        private void RestoreAll()
        {
            RestoreOpacity();
            RestoreContrastBrushes();
        }

        private void RestoreOpacity()
        {
            for (var index = _appliedOpacityTargets.Count - 1; index >= 0; index--)
            {
                var target = _appliedOpacityTargets[index];
                if (target.ReadLocalValue(UIElement.OpacityProperty) is double opacity &&
                    Math.Abs(opacity - DisabledOpacity) < 0.0001)
                {
                    target.ClearValue(UIElement.OpacityProperty);
                }
            }

            _appliedOpacityTargets.Clear();
        }

        private void RestoreContrastBrushes()
        {
            for (var index = _appliedContrastBrushes.Count - 1; index >= 0; index--)
            {
                var (target, property, token) = _appliedContrastBrushes[index];
                if (ReferenceEquals(target.ReadLocalValue(property), token))
                {
                    target.ClearValue(property);
                }
            }

            _appliedContrastBrushes.Clear();
        }
    }
}
