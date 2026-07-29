using System.Drawing;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace MinminCountdown;

internal sealed class TrayIconHost : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _restore;
    private readonly Action _exit;
    private readonly Icon _icon;
    private readonly Forms.ToolStripMenuItem _restoreItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _disposed;

    public TrayIconHost(
        string iconPath,
        Dispatcher dispatcher,
        Action restore,
        Action exit)
    {
        _dispatcher = dispatcher;
        _restore = restore;
        _exit = exit;
        _icon = LoadIcon(iconPath);
        _restoreItem = new Forms.ToolStripMenuItem("打开悬浮窗");
        _exitItem = new Forms.ToolStripMenuItem("退出");
        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false
        };
        _menu.Items.AddRange([_restoreItem, _exitItem]);
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _icon,
            Text = "敏敏子倒计时 - 双击打开",
            Visible = false
        };
        _restoreItem.Click += (_, _) => Invoke(_restore);
        _exitItem.Click += (_, _) => Invoke(_exit);
        _notifyIcon.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                Invoke(_restore);
            }
        };
    }

    internal bool IsVisible => !_disposed && _notifyIcon.Visible;

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

    internal void InvokeRestoreForValidation() => _restoreItem.PerformClick();

    internal void InvokeExitForValidation() => _exitItem.PerformClick();

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
