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
    private ApplicationSession? _session;
    private ApplicationTheme? _theme;
    private HolidayCalendar? _calendar;
    private MainWindow? _mainWindow;
    private FloatingWindow? _floatingWindow;
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
            var settingsPath = _options.SettingsFile ??
                               Path.Combine(_applicationRoot, "settings.json");
            var diagnosticPath = _options.DiagnosticLogPath ??
                                 Path.Combine(_applicationRoot, "logs", "application.jsonl");
            _logger = new DiagnosticLogger(diagnosticPath);
            _logger.Info("ProcessStarted");

            if (_options.ExerciseFatalDiagnostic)
            {
                throw new InvalidOperationException("Synthetic fatal diagnostic probe.");
            }

            _session = ApplicationSession.Load(settingsPath);
            _theme = new ApplicationTheme(Resources);
            _theme.Apply(_session.UtcNow, _session.Current.Preferences.ThemePreference);
            _calendar = new HolidayCalendar(_applicationRoot);
            _mainWindow = new MainWindow(_session, _calendar, _logger, AppContext.BaseDirectory);
            _mainWindow.ApplyTheme(_theme.ActiveMode);
            _floatingWindow = new FloatingWindow(
                _session,
                _calendar,
                _mainWindow,
                AppContext.BaseDirectory);
            _mainWindow.ThemePreferenceChanged += (_, _) => ApplyCurrentTheme(_session.UtcNow);
            ApplyCurrentTheme(_session.UtcNow);

            if (_options.ValidateOnly)
            {
                var report = ValidationReport.Create(
                    _mainWindow,
                    _floatingWindow,
                    _session,
                    _theme,
                    _options,
                    AppContext.BaseDirectory);
                CliOutput.WriteJson(report);
                _logger.Info("ValidationCompleted");
                _floatingWindow.Close();
                _mainWindow.Close();
                _calendar.Dispose();
                Shutdown(0);
                return;
            }

            _singleInstanceLease = SingleInstanceMutex.TryAcquire(_applicationRoot, settingsPath);
            if (_singleInstanceLease is null)
            {
                _logger.Info("DuplicateInstanceRejected");
                Shutdown(0);
                return;
            }

            if (!_acceptanceReadOnly)
            {
                _session.PersistMigrationIfNeeded();
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
            _session is null ||
            _calendar is null ||
            _theme is null ||
            _startupProtocol is null)
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
            _session is null)
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

        _ = InitializeHolidayCalendarAsync(_session.BeijingNow.Year);
    }

    private async Task InitializeHolidayCalendarAsync(int year)
    {
        if (_calendar is null ||
            _startupProtocol is null)
        {
            return;
        }

        await _calendar.InitializeAsync(year, _shutdownCancellation.Token);
        if (_shutdownCancellation.IsCancellationRequested)
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
        if (_session is null || _mainWindow is null || _floatingWindow is null || _theme is null)
        {
            return;
        }

        AdvanceAcceptanceState();
        var nowUtc = _session.UtcNow;
        ApplyCurrentTheme(nowUtc);
        _mainWindow.UpdateTime(ChinaStandardTime.ToBeijingTime(nowUtc));
        _floatingWindow.UpdateTime(nowUtc);
        if (_acceptanceReadOnly)
        {
            return;
        }

        try
        {
            foreach (var note in _session.Reminders.GetDue(_session.Current, nowUtc))
            {
                var window = new ReminderWindow(note);
                window.Closed += (_, _) => _reminderWindows.Remove(window);
                window.Show();
                _reminderWindows.Add(window);
                _session.Reminders.ConfirmDisplayed(note, nowUtc);
                if (note.Reminder is OnceReminder once &&
                    !_session.TryMarkOnceReminderDelivered(note.Id, once.FirstDueUtc, out var error))
                {
                    _logger?.Error(
                        "OneTimeReminderDeliveryPersistFailed",
                        new IOException(error));
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
        if (_session is null || _mainWindow is null || _floatingWindow is null || _theme is null)
        {
            return;
        }

        var mode = _theme.Apply(nowUtc, _session.Current.Preferences.ThemePreference);
        _mainWindow.ApplyTheme(mode);
        _floatingWindow.ApplyTheme(mode);
    }

    private void AdvanceAcceptanceState()
    {
        if (!_acceptanceReadOnly ||
            _session is null ||
            _mainWindow is null ||
            _floatingWindow is null ||
            _startupProtocol is null)
        {
            return;
        }

        var acceptanceState = _startupProtocol.ReadAcceptanceState();
        if (string.Equals(acceptanceState, "released", StringComparison.Ordinal))
        {
            _session.PersistMigrationIfNeeded();
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

        _floatingWindow?.Close();
        _mainWindow?.Close();
        _calendar?.Dispose();
        _singleInstanceLease?.Dispose();
        _singleInstanceLease = null;
        _logger?.Info("ProcessExited");
        Shutdown(0);
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
