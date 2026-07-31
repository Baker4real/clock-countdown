using System.Text;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class App : Application
{
    private readonly List<ReminderWindow> _reminderWindows = new();
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private HostOptions? _options;
    private DiagnosticLogger? _logger;
    private DesktopWorkspace? _desktopWorkspace;
    private ApplicationTheme? _theme;
    private HolidayCalendar? _calendar;
    private MainWindow? _mainWindow;
    private FloatingWindow? _floatingWindow;
    private FloatingTrayController? _floatingTrayController;
    private SingleInstanceMutexLease? _singleInstanceLease;
    private StartupAcceptanceProtocol? _startupProtocol;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _testExitTimer;
    private string? _applicationRoot;
    private bool _acceptanceReadOnly;
    private bool _runtimeReceiptWritten;
    private bool _shuttingDown;

    private void OnStartup(object sender, StartupEventArgs eventArgs)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var exitCode = 1;
        try
        {
            _options = HostOptions.Parse(eventArgs.Args);
            _applicationRoot = ApplicationPaths.ResolveApplicationRoot();
            _startupProtocol = StartupAcceptanceProtocol.Load(_applicationRoot, _options.RequireStartupRequest);
            _acceptanceReadOnly = _startupProtocol.AcceptanceReadOnly;
            var diagnosticPath = _options.DiagnosticLogPath ??
                                 Path.Combine(_applicationRoot, "logs", "application.jsonl");
            _logger = new DiagnosticLogger(diagnosticPath);
            _logger.Info("ProcessStarted");

            if (_options.ExerciseFatalDiagnostic)
            {
                throw new InvalidOperationException("Synthetic fatal diagnostic probe.");
            }

            if (_options.ValidateOnly)
            {
                var validationReport = ApplicationValidation.Run(
                    _options,
                    _applicationRoot,
                    _logger,
                    Resources);
                CliOutput.WriteJson(validationReport);
                _logger.Info("ValidationCompleted");
                Shutdown(0);
                return;
            }

            var settingsPath = _options.SettingsFile ??
                               Path.Combine(_applicationRoot, "settings.json");
            _desktopWorkspace = DesktopWorkspace.Load(settingsPath);
            var desktopWorkspace = _desktopWorkspace;
            var initialSnapshot = desktopWorkspace.Snapshot;
            _theme = new ApplicationTheme(Resources);
            _theme.Apply(initialSnapshot.UtcNow, initialSnapshot.Settings.Preferences.ThemePreference);
            _calendar = new HolidayCalendar(_applicationRoot);
            _mainWindow = new MainWindow(desktopWorkspace, _calendar, _logger, AppContext.BaseDirectory);
            _mainWindow.ApplyTheme(_theme.ActiveMode);
            _floatingWindow = new FloatingWindow(
                desktopWorkspace,
                _calendar,
                _mainWindow,
                AppContext.BaseDirectory);
            _floatingTrayController = new FloatingTrayController(
                _floatingWindow,
                Path.Combine(_applicationRoot, "countdown-icon.ico"),
                () => !_shuttingDown);
            _mainWindow.ThemePreferenceChanged +=
                (_, _) => ApplyCurrentTheme(desktopWorkspace.Snapshot.UtcNow);
            ApplyCurrentTheme(initialSnapshot.UtcNow);

            _singleInstanceLease = SingleInstanceMutex.TryAcquire(_applicationRoot, settingsPath);
            if (_singleInstanceLease is null)
            {
                _logger.Info("DuplicateInstanceRejected");
                Shutdown(0);
                return;
            }

            if (!_acceptanceReadOnly)
            {
                desktopWorkspace.PersistMigrationIfNeeded();
            }
            StartInteractiveHost();
            return;
        }
        catch (Exception exception)
        {
            _logger?.Error("FatalUnhandledException", exception);
            CliOutput.WriteError($"{exception.GetType().FullName}: {exception.Message}");
            exitCode = 1;
        }

        Shutdown(exitCode);
    }

    private void StartInteractiveHost()
    {
        if (_mainWindow is null ||
            _floatingWindow is null ||
            _desktopWorkspace is null ||
            _calendar is null ||
            _theme is null ||
            _startupProtocol is null ||
            _applicationRoot is null ||
            _floatingTrayController is null)
        {
            throw new InvalidOperationException("应用宿主尚未初始化。");
        }

        _mainWindow.CloseRequested += (_, _) => _mainWindow.Hide();
        _mainWindow.Closing += (_, eventArgs) =>
        {
            if (_shuttingDown)
            {
                return;
            }

            eventArgs.Cancel = true;
            _mainWindow.Hide();
        };
        _floatingTrayController.Minimized +=
            (_, _) => _logger?.Info("FloatingWindowMinimizedToTray");
        _floatingTrayController.Restored +=
            (_, _) => _logger?.Info("FloatingWindowRestoredFromTray");
        _floatingTrayController.ExitRequested += (_, _) => ShutdownInteractiveHost();
        _floatingWindow.ExitRequested += (_, _) => ShutdownInteractiveHost();
        _floatingWindow.Closed += (_, _) =>
        {
            if (!_shuttingDown)
            {
                ShutdownInteractiveHost();
            }
        };
        _floatingWindow.ContentRendered += OnFloatingContentRendered;
        if (_acceptanceReadOnly)
        {
            _mainWindow.IsEnabled = false;
            _floatingWindow.IsEnabled = false;
            _logger?.Info("AcceptanceReadOnlyEnabled");
        }

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _clockTimer.Tick += (_, _) => UpdateInteractiveState();
        _clockTimer.Start();
        UpdateInteractiveState();
        _floatingWindow.Show();
    }

    private void OnFloatingContentRendered(object? sender, EventArgs eventArgs)
    {
        if (_runtimeReceiptWritten ||
            _startupProtocol is null ||
            _options is null ||
            _desktopWorkspace is null)
        {
            return;
        }

        _startupProtocol.WriteWindowReady("Pending");
        _runtimeReceiptWritten = true;
        _logger?.Info("WindowReady");

        if (_options.ExitAfterReadyForTest)
        {
            _testExitTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _testExitTimer.Tick += (_, _) =>
            {
                _testExitTimer.Stop();
                ShutdownInteractiveHost();
            };
            _testExitTimer.Start();
        }

        _ = RefreshHolidayCalendarAsync(_desktopWorkspace.Snapshot.UtcNow);
    }

    private async Task RefreshHolidayCalendarAsync(DateTimeOffset nowUtc)
    {
        if (_calendar is null ||
            _startupProtocol is null)
        {
            return;
        }

        var refreshed = await _calendar.RefreshAsync(nowUtc, _shutdownCancellation.Token);
        if (!refreshed || _shutdownCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            _startupProtocol.WriteWindowReady(_calendar.State);
        }
        catch (Exception exception)
        {
            _logger?.Warning("RuntimeCalendarStateWriteFailed", exception);
        }

        if (_calendar.State == "Ready")
        {
            _logger?.Info("HolidayCalendarReady");
        }
        else
        {
            _logger?.Warning("HolidayCalendarUnavailable");
        }
    }

    private void UpdateInteractiveState()
    {
        if (_desktopWorkspace is null ||
            _mainWindow is null ||
            _floatingWindow is null ||
            _theme is null)
        {
            return;
        }

        AdvanceAcceptanceState();
        var nowUtc = _desktopWorkspace.Snapshot.UtcNow;
        if (_runtimeReceiptWritten)
        {
            _ = RefreshHolidayCalendarAsync(nowUtc);
        }
        ApplyCurrentTheme(nowUtc);
        _mainWindow.UpdateTime(ChinaStandardTime.ToBeijingTime(nowUtc));
        _floatingWindow.UpdateTime(nowUtc);
        if (_acceptanceReadOnly)
        {
            return;
        }

        try
        {
            foreach (var note in _desktopWorkspace.GetDueReminders(nowUtc))
            {
                var window = new ReminderWindow(note);
                window.Closed += (_, _) => _reminderWindows.Remove(window);
                window.Show();
                _reminderWindows.Add(window);
                var result = _desktopWorkspace.ConfirmReminderDisplayed(note, nowUtc);
                if (!result.Succeeded)
                {
                    _logger?.Error(
                        "OneTimeReminderDeliveryPersistFailed",
                        new IOException(result.Error));
                }
            }
        }
        catch (Exception exception)
        {
            _logger?.Error("NoteReminderFailed", exception);
        }
    }

    private void ApplyCurrentTheme(DateTimeOffset nowUtc)
    {
        if (_desktopWorkspace is null ||
            _mainWindow is null ||
            _floatingWindow is null ||
            _theme is null)
        {
            return;
        }

        var mode = _theme.Apply(
            nowUtc,
            _desktopWorkspace.Snapshot.Settings.Preferences.ThemePreference);
        _mainWindow.ApplyTheme(mode);
        _floatingWindow.ApplyTheme(mode);
    }

    private void AdvanceAcceptanceState()
    {
        if (!_acceptanceReadOnly ||
            _desktopWorkspace is null ||
            _mainWindow is null ||
            _floatingWindow is null ||
            _startupProtocol is null)
        {
            return;
        }

        var acceptanceState = _startupProtocol.ReadAcceptanceState();
        if (string.Equals(acceptanceState, "released", StringComparison.Ordinal))
        {
            _desktopWorkspace.PersistMigrationIfNeeded();
            if (!_startupProtocol.MarkAcceptanceReady())
            {
                throw new InvalidOperationException("验收迁移完成后无法确认 ready 状态。");
            }

            _logger?.Info("AcceptanceMigrationReady");
            return;
        }

        if (!string.Equals(acceptanceState, "finalized", StringComparison.Ordinal))
        {
            return;
        }

        _mainWindow.IsEnabled = true;
        _floatingWindow.IsEnabled = true;
        try
        {
            _startupProtocol.WriteAcceptanceReady();
        }
        catch
        {
            _mainWindow.IsEnabled = false;
            _floatingWindow.IsEnabled = false;
            throw;
        }

        _acceptanceReadOnly = false;
        _logger?.Info("AcceptanceReadOnlyReleased");
    }

    private void ShutdownInteractiveHost()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _shutdownCancellation.Cancel();
        _clockTimer?.Stop();
        _testExitTimer?.Stop();
        foreach (var reminder in _reminderWindows.ToArray())
        {
            reminder.Close();
        }

        _floatingTrayController?.Dispose();
        _floatingTrayController = null;
        _floatingWindow?.Close();
        _mainWindow?.Close();
        _calendar?.Dispose();
        _singleInstanceLease?.Dispose();
        _singleInstanceLease = null;
        _logger?.Info("ProcessExited");
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _floatingTrayController?.Dispose();
        _floatingTrayController = null;
        base.OnExit(eventArgs);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        _logger?.Error("FatalUnhandledException", eventArgs.Exception);
        CliOutput.WriteError($"{eventArgs.Exception.GetType().FullName}: {eventArgs.Exception.Message}");
        eventArgs.Handled = true;
        _shuttingDown = true;
        Shutdown(1);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        _logger?.Error("FatalUnhandledException", eventArgs.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        _logger?.Warning("UnobservedTaskException", eventArgs.Exception);
        eventArgs.SetObserved();
    }
}

internal static class CliOutput
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static void WriteJson<T>(T value)
    {
        Write(Console.OpenStandardOutput(), JsonSerializer.Serialize(value, JsonOptions));
    }

    public static void WriteError(string value)
    {
        Write(Console.OpenStandardError(), value);
    }

    private static void Write(Stream stream, string value)
    {
        if (!stream.CanWrite)
        {
            return;
        }

        using var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };
        writer.WriteLine(value);
    }
}
