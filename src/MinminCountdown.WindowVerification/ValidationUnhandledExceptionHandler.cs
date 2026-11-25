using System.Windows;
using System.Windows.Threading;

namespace MinminCountdown;

internal sealed class ValidationUnhandledExceptionHandler : IDisposable
{
    private readonly Application _app;
    private readonly DiagnosticLogger _logger;

    internal ValidationUnhandledExceptionHandler(Application app, DiagnosticLogger logger)
    {
        _app = app;
        _logger = logger;
        _app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    internal bool FatalExitRequested { get; private set; }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        FatalExitRequested = true;
        _logger.Error("FatalUnhandledException", eventArgs.Exception);
        CliOutput.WriteError($"{eventArgs.Exception.GetType().FullName}: {eventArgs.Exception.Message}");
        eventArgs.Handled = true;
        // ValidationEntryPoint owns the non-zero return and resource cleanup. Unlike
        // the product host, this runner has no App.Run message loop to shut down.
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        _logger.Error("FatalUnhandledException", eventArgs.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        _logger.Warning("UnobservedTaskException", eventArgs.Exception);
        eventArgs.SetObserved();
    }

    public void Dispose()
    {
        _app.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }
}
