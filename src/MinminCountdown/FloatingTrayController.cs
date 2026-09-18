using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MinminCountdown;

internal sealed class FloatingTrayController : IDisposable
{
    private readonly FloatingWindow _floatingWindow;
    private readonly ITrayIconHost _trayIcon;
    private readonly Func<bool> _canTransition;
    private readonly TrayForegroundInterop _foregroundInterop;
    private bool _disposed;

    public FloatingTrayController(
        FloatingWindow floatingWindow,
        string iconPath,
        Func<bool>? canTransition = null)
        : this(floatingWindow, iconPath, canTransition, foregroundInterop: null, attachShellNotifyIcon: true)
    {
    }

    internal FloatingTrayController(
        FloatingWindow floatingWindow,
        string iconPath,
        Func<bool>? canTransition,
        TrayForegroundInterop? foregroundInterop)
        : this(floatingWindow, iconPath, canTransition, foregroundInterop, attachShellNotifyIcon: true)
    {
    }

    internal FloatingTrayController(
        FloatingWindow floatingWindow,
        string iconPath,
        Func<bool>? canTransition,
        TrayForegroundInterop? foregroundInterop,
        bool attachShellNotifyIcon)
    {
        _floatingWindow = floatingWindow;
        _canTransition = canTransition ?? (() => true); // Production default: allow tray transitions unless a host supplies a guard.
        _foregroundInterop = foregroundInterop ?? TrayForegroundInterop.System;
        _trayIcon = attachShellNotifyIcon
            ? new TrayIconHost(
                iconPath,
                floatingWindow.Dispatcher,
                Restore,
                RequestExit)
            : new MemoryTrayIconHost(Restore, RequestExit);
        _floatingWindow.MinimizeToTrayRequested += OnMinimizeRequested;
    }

    internal bool IsActive => !_disposed;

    internal bool IsTrayIconVisible => !_disposed && _trayIcon.IsVisible;

    internal event EventHandler? Minimized;

    internal event EventHandler? Restored;

    internal event EventHandler? ExitRequested;

    internal void InvokeRestoreForValidation() => _trayIcon.InvokeRestoreForValidation();

    internal void InvokeExitForValidation() => _trayIcon.InvokeExitForValidation();

    internal string ActivationState => !_floatingWindow.IsVisible
        ? IsTrayIconVisible ? "Tray" : "Hidden"
        : _floatingWindow.WindowState == System.Windows.WindowState.Minimized
            ? "Minimized"
            : "VisibleNormal";

    internal int InteractiveFloatingWindowCount => System.Windows.Application.Current?.Windows
        .OfType<FloatingWindow>()
        .Count() ?? 0;

    internal long ForegroundWindowHandle => GetForegroundWindow().ToInt64();

    internal bool FloatingWindowIsActive => _floatingWindow.IsActive;

    internal bool RestoreAndActivateFromExternalRequest()
    {
        if (!CanTransition())
        {
            return false;
        }

        return RestoreCore();
    }

    internal void MinimizeToTray() => OnMinimizeRequested(this, EventArgs.Empty);

    internal void MinimizeToTrayForActivationTest() => MinimizeToTray();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _floatingWindow.MinimizeToTrayRequested -= OnMinimizeRequested;
        _trayIcon.Dispose();
        _disposed = true;
    }

    private void OnMinimizeRequested(object? sender, EventArgs eventArgs)
    {
        if (!CanTransition())
        {
            return;
        }

        _trayIcon.Show();
        _floatingWindow.Hide();
        Minimized?.Invoke(this, EventArgs.Empty);
    }

    private void Restore()
    {
        if (!RestoreAndActivateFromExternalRequest())
        {
            return;
        }

        Restored?.Invoke(this, EventArgs.Empty);
    }

    private bool RestoreCore()
    {
        if (!_floatingWindow.IsVisible)
        {
            _floatingWindow.Show();
        }

        _floatingWindow.WindowState = System.Windows.WindowState.Normal;
        var activateAccepted = _foregroundInterop.Activate(_floatingWindow);
        var focusAccepted = _foregroundInterop.Focus(_floatingWindow);
        var handle = new WindowInteropHelper(_floatingWindow).Handle;
        var foregroundAccepted = false;
        if (handle != IntPtr.Zero)
        {
            foregroundAccepted = _foregroundInterop.TrySetForeground(handle);
        }

        var isForeground = _foregroundInterop.IsForegroundOrActive(_floatingWindow, handle);
        var restored = _floatingWindow.IsVisible &&
               _floatingWindow.WindowState == System.Windows.WindowState.Normal &&
               isForeground &&
               (activateAccepted || focusAccepted || foregroundAccepted || isForeground);
        if (restored)
        {
            // 判定恢复成功才隐藏托盘图标；失败时保留图标让外部请求可再次重试。
            _trayIcon.Hide();
        }

        return restored;
    }

    private void RequestExit()
    {
        if (CanTransition())
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanTransition() => !_disposed && _canTransition();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

// RestoreCore 判定成功所依赖的 shell 交互 seam；验证替身可注入确定性失败。
internal class TrayForegroundInterop
{
    internal static TrayForegroundInterop System { get; } = new();

    internal virtual bool Activate(FloatingWindow window) => window.Activate();

    internal virtual bool Focus(FloatingWindow window) => window.Focus();

    internal virtual bool TrySetForeground(IntPtr handle) => SetForegroundWindow(handle);

    internal virtual bool IsForegroundOrActive(FloatingWindow window, IntPtr handle) =>
        handle != IntPtr.Zero && (GetForegroundWindow() == handle || window.IsActive);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
