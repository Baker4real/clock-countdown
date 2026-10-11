using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using H.NotifyIcon;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace MinminCountdown;

internal interface ITrayIconHost : IDisposable
{
    bool IsVisible { get; }

    void Show();

    void Hide();

    void InvokeRestoreForValidation();

    void InvokeResetPositionForValidation();

    void InvokeExitForValidation();

    void InvokeOpenManagerForValidation();

    void InvokeIconClickForValidation();
}

internal sealed class MemoryTrayIconHost : ITrayIconHost
{
    private readonly Action _restore;
    private readonly Action _exit;
    private readonly Action? _resetPosition;
    private readonly Action? _openManager;
    private bool _visible;
    private bool _disposed;

    public MemoryTrayIconHost(Action restore, Action exit, Action? resetPosition = null, Action? openManager = null)
    {
        _restore = restore;
        _exit = exit;
        _resetPosition = resetPosition;
        _openManager = openManager;
    }

    public bool IsVisible => !_disposed && _visible;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _visible = true;
    }

    public void Hide()
    {
        if (!_disposed)
        {
            _visible = false;
        }
    }

    public void InvokeRestoreForValidation() => _restore();

    public void InvokeResetPositionForValidation() => _resetPosition?.Invoke();

    public void InvokeExitForValidation() => _exit();

    public void InvokeOpenManagerForValidation() => _openManager?.Invoke();

    public void InvokeIconClickForValidation() => _restore();

    public void Dispose()
    {
        _visible = false;
        _disposed = true;
    }
}

internal sealed class TrayIconHost : ITrayIconHost
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _restore;
    private readonly Action _exit;
    private readonly Action? _resetPosition;
    private readonly Action? _openManager;
    private readonly Icon _icon;
    private readonly MenuItem _openManagerItem;
    private readonly MenuItem _restoreItem;
    private readonly MenuItem _resetPositionItem;
    private readonly MenuItem _exitItem;
    private readonly ContextMenu _menu;
    private readonly TaskbarIcon _taskbarIcon;
    private bool _visible;
    private bool _disposed;

    public TrayIconHost(
        string iconPath,
        Dispatcher dispatcher,
        Action restore,
        Action exit,
        Action? resetPosition = null,
        Action? openManager = null)
    {
        _dispatcher = dispatcher;
        _restore = restore;
        _exit = exit;
        _resetPosition = resetPosition;
        _openManager = openManager;
        _icon = LoadIcon(iconPath);
        (_menu, _openManagerItem, _restoreItem, _resetPositionItem, _exitItem) = TrayMenuFactory.Create(
            () => Invoke(OpenManager),
            () => Invoke(_restore),
            () => Invoke(ResetPosition),
            () => Invoke(_exit));
        _taskbarIcon = new TaskbarIcon
        {
            ContextMenu = _menu,
            Icon = _icon,
            ToolTipText = "敏敏子倒计时 - 单击打开",
            Visibility = Visibility.Collapsed
        };
        // #213：单击恢复悬浮窗。双击会先产生单击，恢复动作本身可重复。
        _taskbarIcon.TrayLeftMouseUp += (_, _) => Invoke(_restore);
    }

    public bool IsVisible => !_disposed && _visible;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);
        _taskbarIcon.TrayIcon.Show();
        _visible = true;
    }

    public void Hide()
    {
        if (!_disposed)
        {
            _taskbarIcon.TrayIcon.Hide();
            _visible = false;
        }
    }

    public void InvokeRestoreForValidation() => _restoreItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    public void InvokeOpenManagerForValidation() => _openManagerItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    public void InvokeIconClickForValidation() => _restore();

    public void InvokeResetPositionForValidation() => _resetPositionItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    public void InvokeExitForValidation() => _exitItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _taskbarIcon.TrayIcon.Hide();
        _taskbarIcon.Dispose();
        _menu.IsOpen = false;
        _icon.Dispose();
        _visible = false;
        _disposed = true;
    }

    private void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    private void ResetPosition() => _resetPosition?.Invoke();

    private void OpenManager() => _openManager?.Invoke();

    private static Icon LoadIcon(string iconPath)
    {
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        using var associated = string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? null
            : Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        return associated is null
            ? (Icon)SystemIcons.Application.Clone()
            : (Icon)associated.Clone();
    }
}

// The production menu is independent of Shell registration so the isolated
// validation harness can render and exercise these same WPF controls.
internal static class TrayMenuFactory
{
    internal static (ContextMenu Menu, MenuItem OpenManager, MenuItem Restore, MenuItem ResetPosition, MenuItem Exit) Create(
        Action openManager,
        Action restore,
        Action resetPosition,
        Action exit)
    {
        var openManagerItem = new MenuItem { Header = "打开管理窗口" };
        var restoreItem = new MenuItem { Header = "打开悬浮窗" };
        // #156：非拖动的窗口复位/贴边入口——悬浮窗被拖出屏幕时，托盘仍可达并把窗夹回工作区。
        var resetPositionItem = new MenuItem { Header = "复位窗口位置" };
        var exitItem = new MenuItem { Header = "退出" };
        var menu = new ContextMenu
        {
            Background = Brush("#FFFDFC"),
            BorderBrush = Brush("#F6DFD3"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HasDropShadow = true
        };
        menu.Resources.Add(typeof(MenuItem), CreateMenuItemStyle());
        menu.Items.Add(openManagerItem);
        menu.Items.Add(restoreItem);
        menu.Items.Add(resetPositionItem);
        menu.Items.Add(exitItem);
        openManagerItem.Click += (_, _) => openManager();
        restoreItem.Click += (_, _) => restore();
        resetPositionItem.Click += (_, _) => resetPosition();
        exitItem.Click += (_, _) => exit();
        return (menu, openManagerItem, restoreItem, resetPositionItem, exitItem);
    }

    private static Style CreateMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#3B190D")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#FFFDFC")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#D4526A")));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 9, 16, 9)));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new MediaFontFamily("Microsoft YaHei UI")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 14d));
        style.Setters.Add(new Setter(Control.TemplateProperty, CreateMenuItemTemplate()));

        var highlighted = new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true
        };
        highlighted.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#F6DFD3")));
        style.Triggers.Add(highlighted);
        return style;
    }

    private static ControlTemplate CreateMenuItemTemplate()
    {
        // These leaf commands have no icon, checkmark, or submenu. Bind
        // the rendered surface so the native theme cannot paint a blue highlight.
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        surface.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        surface.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        header.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        header.SetValue(FrameworkElement.HorizontalAlignmentProperty,
            new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        header.SetValue(FrameworkElement.VerticalAlignmentProperty,
            new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        surface.AppendChild(header);
        return new ControlTemplate(typeof(MenuItem)) { VisualTree = surface };
    }

    private static SolidColorBrush Brush(string hex)
    {
        var color = (MediaColor)MediaColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}
