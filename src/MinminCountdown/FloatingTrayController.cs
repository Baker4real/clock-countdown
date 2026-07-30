namespace MinminCountdown;

internal sealed class FloatingTrayController : IDisposable
{
    private readonly FloatingWindow _floatingWindow;
    private readonly TrayIconHost _trayIcon;
    private readonly Func<bool> _canTransition;
    private bool _disposed;

    public FloatingTrayController(
        FloatingWindow floatingWindow,
        string iconPath,
        Func<bool>? canTransition = null)
    {
        _floatingWindow = floatingWindow;
        _canTransition = canTransition ?? (() => true);
        _trayIcon = new TrayIconHost(
            iconPath,
            floatingWindow.Dispatcher,
            Restore,
            RequestExit);
        _floatingWindow.MinimizeToTrayRequested += OnMinimizeRequested;
    }

    internal bool IsActive => !_disposed;

    internal bool IsTrayIconVisible => !_disposed && _trayIcon.IsVisible;

    internal event EventHandler? Minimized;

    internal event EventHandler? Restored;

    internal event EventHandler? ExitRequested;

    internal void InvokeRestoreForValidation() => _trayIcon.InvokeRestoreForValidation();

    internal void InvokeExitForValidation() => _trayIcon.InvokeExitForValidation();

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
        if (!CanTransition())
        {
            return;
        }

        if (!_floatingWindow.IsVisible)
        {
            _floatingWindow.Show();
        }

        _floatingWindow.WindowState = System.Windows.WindowState.Normal;
        _floatingWindow.Activate();
        _trayIcon.Hide();
        Restored?.Invoke(this, EventArgs.Empty);
    }

    private void RequestExit()
    {
        if (CanTransition())
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanTransition() => !_disposed && _canTransition();
}
