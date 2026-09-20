using System.Drawing;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace MinminCountdown;

internal interface ITrayIconHost : IDisposable
{
    bool IsVisible { get; }

    void Show();

    void Hide();

    void InvokeRestoreForValidation();

    void InvokeResetPositionForValidation();

    void InvokeExitForValidation();
}

internal sealed class MemoryTrayIconHost : ITrayIconHost
{
    private readonly Action _restore;
    private readonly Action _exit;
    private readonly Action? _resetPosition;
    private bool _visible;
    private bool _disposed;

    public MemoryTrayIconHost(Action restore, Action exit, Action? resetPosition = null)
    {
        _restore = restore;
        _exit = exit;
        _resetPosition = resetPosition;
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
    private readonly Icon _icon;
    private readonly Forms.ToolStripMenuItem _restoreItem;
    private readonly Forms.ToolStripMenuItem _resetPositionItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _disposed;

    public TrayIconHost(
        string iconPath,
        Dispatcher dispatcher,
        Action restore,
        Action exit,
        Action? resetPosition = null)
    {
        _dispatcher = dispatcher;
        _restore = restore;
        _exit = exit;
        _resetPosition = resetPosition;
        _icon = LoadIcon(iconPath);
        _restoreItem = new Forms.ToolStripMenuItem("打开悬浮窗");
        // #156：非拖动的窗口复位/贴边入口——悬浮窗被拖出屏幕时，托盘仍可达并把窗夹回工作区。
        _resetPositionItem = new Forms.ToolStripMenuItem("复位窗口位置");
        _exitItem = new Forms.ToolStripMenuItem("退出");
        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false
        };
        _menu.Items.AddRange([_restoreItem, _resetPositionItem, _exitItem]);
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _icon,
            Text = "敏敏子倒计时 - 双击打开",
            Visible = false
        };
        _restoreItem.Click += (_, _) => Invoke(_restore);
        _resetPositionItem.Click += (_, _) => Invoke(ResetPosition);
        _exitItem.Click += (_, _) => Invoke(_exit);
        _notifyIcon.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                Invoke(_restore);
            }
        };
    }

    public bool IsVisible => !_disposed && _notifyIcon.Visible;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _notifyIcon.Visible = true;
    }

    public void Hide()
    {
        if (!_disposed)
        {
            _notifyIcon.Visible = false;
        }
    }

    public void InvokeRestoreForValidation() => _restoreItem.PerformClick();

    public void InvokeResetPositionForValidation() => _resetPositionItem.PerformClick();

    public void InvokeExitForValidation() => _exitItem.PerformClick();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
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
