using System.Text;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class App : Application
{
    static App()
    {
        TextBoxBase.IsReadOnlyProperty.OverrideMetadata(
            typeof(DatePickerTextBox),
            new FrameworkPropertyMetadata(true));
    }

    private readonly CancellationTokenSource _shutdownCancellation = new();
    private HostOptions? _options;
    private DiagnosticLogger? _logger;
    private DesktopWorkspace? _desktopWorkspace;
    private ApplicationTheme? _theme;
    private HolidayCalendar? _calendar;
    private ManagerWindowLifecycle? _managerLifecycle;
    private MainWindow? _mainWindow;
    private FloatingWindow? _floatingWindow;
    private FloatingTrayController? _floatingTrayController;
    private ReminderDeliveryCoordinator? _reminderDeliveryCoordinator;
    private SingleInstanceMutexLease? _singleInstanceLease;
    private InstanceActivationServer? _instanceActivationServer;
    private InstanceActivationScope? _instanceActivationScope;
    private StartupAcceptanceProtocol? _startupProtocol;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _testExitTimer;
    private string? _applicationRoot;
    private bool _acceptanceReadOnly;
    private bool _activationTestStateApplied;
    private int _activationCallbackFailureInjected;
    private bool _runtimeReceiptWritten;
    private bool _firstFrameWorkStarted;
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
            _instanceActivationScope = InstanceActivationScope.Create(_applicationRoot, settingsPath);
            _singleInstanceLease = SingleInstanceMutex.TryAcquire(_applicationRoot, settingsPath);
            if (_singleInstanceLease is null)
            {
                if (_acceptanceReadOnly)
                {
                    _logger.Activation(
                        "InstanceActivationSuppressedAcceptanceReadOnly",
                        new ActivationDiagnostic(
                            _instanceActivationScope.ScopeId,
                            Guid.NewGuid().ToString("N"),
                            "Suppressed"));
                    Shutdown(1);
                    return;
                }

                var activation = InstanceActivationClient.SendOnce(
                    _instanceActivationScope,
                    TimeSpan.FromSeconds(3));
                _logger.Activation(
                    activation.Outcome switch
                    {
                        InstanceActivationClientOutcome.Succeeded => "InstanceActivationSucceeded",
                        InstanceActivationClientOutcome.TimedOut => "InstanceActivationTimedOut",
                        InstanceActivationClientOutcome.ProtocolMismatch => "InstanceActivationProtocolMismatch",
                        InstanceActivationClientOutcome.ListenerFailed => "InstanceActivationListenerFailed",
                        _ => "InstanceActivationRejected"
                    },
                    new ActivationDiagnostic(
                        activation.ScopeId,
                        activation.RequestId,
                        activation.Outcome.ToString(),
                        activation.ServerProcessId,
                        activation.ForegroundPermissionGranted));
                Shutdown(activation.Outcome == InstanceActivationClientOutcome.Succeeded ? 0 : 1);
                return;
            }

            if (!_acceptanceReadOnly)
            {
                _instanceActivationServer = new InstanceActivationServer(
                    _instanceActivationScope,
                    HandleInstanceActivation,
                    _options.ActivationListenerBehavior,
                    LogInstanceActivationRequestReceived,
                    LogInstanceActivationListenerFailure);
            }

            _desktopWorkspace = DesktopWorkspace.Load(
                settingsPath,
                chooseRecovery: ShowSettingsRecoveryPrompt,
                recoveryAllowed: _startupProtocol.RecoveryWritesAllowed,
                migrationUtc: _startupProtocol.Request.MigrationUtc);
            var desktopWorkspace = _desktopWorkspace;
            var initialSnapshot = desktopWorkspace.Snapshot;
            _theme = new ApplicationTheme(Resources);
            _theme.Apply(initialSnapshot.UtcNow, initialSnapshot.Settings.Preferences.ThemePreference);
            var calendar = new HolidayCalendar(_applicationRoot);
            _calendar = calendar;
            var logger = _logger ??
                         throw new InvalidOperationException("诊断日志尚未初始化。");
            var managerLifecycle = new ManagerWindowLifecycle(
                () => new MainWindow(desktopWorkspace, calendar, logger, AppContext.BaseDirectory),
                AttachManagerWindow);
            _managerLifecycle = managerLifecycle;
            _reminderDeliveryCoordinator = new ReminderDeliveryCoordinator(
                desktopWorkspace,
                _logger,
                (sourceKind, sourceId) => managerLifecycle.OpenReminderSource(sourceKind, sourceId));
            _floatingWindow = new FloatingWindow(
                desktopWorkspace,
                _calendar,
                _managerLifecycle,
                AppContext.BaseDirectory,
                deferAssets: true);
            ApplyCurrentTheme(initialSnapshot.UtcNow);

            if (!_acceptanceReadOnly)
            {
                desktopWorkspace.PersistMigrationIfNeeded();
            }
            StartInteractiveHost();
            return;
        }
        catch (SettingsRecoveryDeclinedException exception)
        {
            _logger?.Info("SettingsRecoveryDeclinedExit");
            CliOutput.WriteError($"{exception.GetType().FullName}: {exception.Message}");
            exitCode = 1;
        }
        catch (SettingsRecoveryException exception)
        {
            _logger?.Warning("SettingsRecoveryAborted");
            SettingsRecoveryFailurePresentation.TryPresent(
                exception,
                SettingsRecoveryWindow.ShowFailure);
            CliOutput.WriteError($"{exception.GetType().FullName}: {exception.Message}");
            exitCode = 1;
        }
        catch (Exception exception)
        {
            _logger?.Error("FatalUnhandledException", exception);
            CliOutput.WriteError($"{exception.GetType().FullName}: {exception.Message}");
            exitCode = 1;
        }

        Shutdown(exitCode);
    }

    private SettingsRecoveryDecision ShowSettingsRecoveryPrompt(SettingsRecoveryPrompt prompt)
    {
        _logger?.Info("SettingsRecoveryOffered");
        var decision = SettingsRecoveryWindow.Choose(prompt);
        _logger?.Info(decision == SettingsRecoveryDecision.RecoverBackup
            ? "SettingsRecoveryApproved"
            : "SettingsRecoveryDeclined");
        return decision;
    }

    private Task<InstanceActivationHandlingResult> HandleInstanceActivation(
        string requestId,
        CancellationToken cancellationToken)
    {
        if (_options?.ActivationListenerBehavior == InstanceActivationListenerBehavior.CallbackFailureOnce &&
            Interlocked.Exchange(ref _activationCallbackFailureInjected, 1) == 0)
        {
            return Task.FromException<InstanceActivationHandlingResult>(
                new InvalidOperationException("Synthetic activation callback failure."));
        }

        if (Dispatcher.CheckAccess())
        {
            return Task.FromResult(HandleInstanceActivationCore(requestId));
        }

        return Dispatcher.InvokeAsync(
            () => HandleInstanceActivationCore(requestId),
            DispatcherPriority.Send,
            cancellationToken).Task;
    }

    private InstanceActivationHandlingResult HandleInstanceActivationCore(string requestId)
    {
        if (_floatingTrayController is null ||
            _instanceActivationScope is null ||
            _shuttingDown)
        {
            return new InstanceActivationHandlingResult(
                false,
                "Unavailable",
                "Unavailable",
                0,
                0,
                false,
                false,
                false,
                false);
        }

        var stateBefore = _floatingTrayController.ActivationState;
        var mainWindowVisibleBefore = _mainWindow?.IsVisible == true;
        var managerStateBefore = _mainWindow?.CaptureInstanceActivationState();
        var activated = _floatingTrayController.RestoreAndActivateFromExternalRequest();
        var managerStatePreserved = _mainWindow is null ||
            managerStateBefore == _mainWindow.CaptureInstanceActivationState();
        var result = new InstanceActivationHandlingResult(
            activated,
            stateBefore,
            _floatingTrayController.ActivationState,
            _floatingTrayController.InteractiveFloatingWindowCount,
            _floatingTrayController.ForegroundWindowHandle,
            _floatingTrayController.FloatingWindowIsActive,
            mainWindowVisibleBefore,
            _mainWindow?.IsVisible == true,
            managerStatePreserved);
        _logger?.Activation(
            "InstanceActivationHandled",
            new ActivationDiagnostic(
                _instanceActivationScope.ScopeId,
                requestId,
                activated ? "Activated" : "Rejected",
                StateBefore: result.StateBefore,
                StateAfter: result.StateAfter,
                InteractiveFloatingWindowCount: result.InteractiveFloatingWindowCount,
                ForegroundWindowHandle: result.ForegroundWindowHandle,
                FloatingWindowIsActive: result.FloatingWindowIsActive,
                MainWindowVisibleBefore: result.MainWindowVisibleBefore,
                MainWindowVisibleAfter: result.MainWindowVisibleAfter,
                ManagerStatePreserved: result.ManagerStatePreserved));
        return result;
    }

    private void LogInstanceActivationRequestReceived(string requestId)
    {
        if (_instanceActivationScope is null)
        {
            return;
        }

        _logger?.Activation(
            "InstanceActivationRequestReceived",
            new ActivationDiagnostic(
                _instanceActivationScope.ScopeId,
                requestId,
                "Received"));
    }

    private void LogInstanceActivationListenerFailure()
    {
        if (_instanceActivationScope is null)
        {
            return;
        }

        _logger?.Activation(
            "InstanceActivationListenerFailed",
            new ActivationDiagnostic(
                _instanceActivationScope.ScopeId,
                null,
                "ListenerFailed"));
    }

    private void AttachManagerWindow(MainWindow window)
    {
        if (_theme is not null)
        {
            window.ApplyTheme(_theme.ActiveMode);
        }

        window.CloseRequested += (_, _) => window.Hide();
        window.MinimizeToTrayRequested += (_, _) =>
        {
            window.Hide();
            _floatingTrayController?.MinimizeToTray();
        };
        window.Closing += (_, eventArgs) =>
        {
            if (_shuttingDown)
            {
                return;
            }

            eventArgs.Cancel = true;
            window.Hide();
        };
        window.ThemePreferenceChanged += (_, _) =>
        {
            if (_desktopWorkspace is not null)
            {
                ApplyCurrentTheme(_desktopWorkspace.Snapshot.UtcNow);
            }
        };
        if (_acceptanceReadOnly)
        {
            window.IsEnabled = false;
        }

        _mainWindow = window;
    }

    private void StartInteractiveHost()
    {
        if (_managerLifecycle is null ||
            _floatingWindow is null ||
            _desktopWorkspace is null ||
            _calendar is null ||
            _theme is null ||
            _startupProtocol is null ||
            _applicationRoot is null)
        {
            throw new InvalidOperationException("应用宿主尚未初始化。");
        }

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
            _floatingWindow.IsEnabled = false;
            _logger?.Info("AcceptanceReadOnlyEnabled");
        }

        _floatingWindow.Show();
    }

    private void WriteFirstFloatingFrameReady()
    {
        if (_runtimeReceiptWritten ||
            _startupProtocol is null ||
            _options is null)
        {
            return;
        }

        if (_options.ActivationTestState is InstanceActivationTestState.Tray)
        {
            EnsureFloatingTrayController();
        }

        ApplyInstanceActivationTestState();
        _startupProtocol.WriteWindowReady("Pending");
        _runtimeReceiptWritten = true;
        _logger?.Info("WindowReady");
    }

    private void EnsureFloatingTrayController()
    {
        if (_floatingTrayController is not null ||
            _floatingWindow is null ||
            _applicationRoot is null)
        {
            return;
        }

        _floatingTrayController = new FloatingTrayController(
            _floatingWindow,
            Path.Combine(_applicationRoot, "countdown-icon.ico"),
            () => !_shuttingDown);
        _floatingTrayController.Minimized +=
            (_, _) => _logger?.Info("FloatingWindowMinimizedToTray");
        _floatingTrayController.Restored +=
            (_, _) => _logger?.Info("FloatingWindowRestoredFromTray");
        _floatingTrayController.ExitRequested += (_, _) => ShutdownInteractiveHost();
    }

    private void StartInteractiveClock()
    {
        if (_clockTimer is not null)
        {
            return;
        }

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _clockTimer.Tick += (_, _) => UpdateInteractiveState();
        _clockTimer.Start();
        UpdateInteractiveState();
    }

    private void OnFloatingContentRendered(object? sender, EventArgs eventArgs)
    {
        if (_firstFrameWorkStarted ||
            _options is null ||
            _desktopWorkspace is null ||
            !ReferenceEquals(sender, _floatingWindow))
        {
            return;
        }

        _firstFrameWorkStarted = true;

        WriteFirstFloatingFrameReady();
        _floatingWindow?.LoadDeferredAssets();
        EnsureFloatingTrayController();
        StartInteractiveClock();

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

    private void ApplyInstanceActivationTestState()
    {
        if (_activationTestStateApplied ||
            _options?.ActivationTestState is null)
        {
            return;
        }

        if (_options.ActivationTestState is InstanceActivationTestState.Tray &&
            _floatingTrayController is null)
        {
            return;
        }

        _activationTestStateApplied = true;
        switch (_options.ActivationTestState)
        {
            case InstanceActivationTestState.FloatingVisible:
                break;
            case InstanceActivationTestState.Tray:
                _floatingTrayController!.MinimizeToTrayForActivationTest();
                break;
            case InstanceActivationTestState.Hidden:
                _floatingWindow?.Hide();
                break;
            case InstanceActivationTestState.Minimized:
                if (_floatingWindow is not null)
                {
                    _floatingWindow.WindowState = WindowState.Minimized;
                }
                break;
            case InstanceActivationTestState.MainVisible:
                _managerLifecycle?.EnsureCreated();
                _mainWindow?.PrepareInstanceActivationStateForTest();
                _mainWindow?.Show();
                break;
            default:
                throw new InvalidOperationException(
                    $"实例激活测试状态尚未实现：{_options.ActivationTestState}");
        }
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
            _floatingWindow is null ||
            _theme is null ||
            _reminderDeliveryCoordinator is null)
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
        _mainWindow?.UpdateTime(ChinaStandardTime.ToBeijingTime(nowUtc));
        _floatingWindow.UpdateTime(nowUtc);
        if (_acceptanceReadOnly)
        {
            return;
        }

        try
        {
            _reminderDeliveryCoordinator.Tick(nowUtc);
        }
        catch (Exception exception)
        {
            _logger?.Error("NoteReminderFailed", exception);
        }
    }

    private void ApplyCurrentTheme(DateTimeOffset nowUtc)
    {
        if (_desktopWorkspace is null ||
            _floatingWindow is null ||
            _theme is null)
        {
            return;
        }

        var mode = _theme.Apply(
            nowUtc,
            _desktopWorkspace.Snapshot.Settings.Preferences.ThemePreference);
        _mainWindow?.ApplyTheme(mode);
        _floatingWindow.ApplyTheme(mode);
    }

    private void AdvanceAcceptanceState()
    {
        if (!_acceptanceReadOnly ||
            _desktopWorkspace is null ||
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

        if (_mainWindow is not null)
        {
            _mainWindow.IsEnabled = true;
        }

        _floatingWindow.IsEnabled = true;
        try
        {
            _startupProtocol.WriteAcceptanceReady();
        }
        catch
        {
            if (_mainWindow is not null)
            {
                _mainWindow.IsEnabled = false;
            }

            _floatingWindow.IsEnabled = false;
            throw;
        }

        _acceptanceReadOnly = false;
        if (_instanceActivationServer is null &&
            _instanceActivationScope is not null &&
            _options is not null)
        {
            _instanceActivationServer = new InstanceActivationServer(
                _instanceActivationScope,
                HandleInstanceActivation,
                _options.ActivationListenerBehavior,
                LogInstanceActivationRequestReceived,
                LogInstanceActivationListenerFailure);
        }
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
        _reminderDeliveryCoordinator?.Dispose();
        _reminderDeliveryCoordinator = null;

        _instanceActivationServer?.Dispose();
        _instanceActivationServer = null;
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
        _reminderDeliveryCoordinator?.Dispose();
        _reminderDeliveryCoordinator = null;
        _instanceActivationServer?.Dispose();
        _instanceActivationServer = null;
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

internal interface IManagerWorkspaceHost
{
    void Reveal(WorkspaceKind workspace);
}

internal sealed class ManagerWindowLifecycle : IManagerWorkspaceHost
{
    private readonly Func<MainWindow> _create;
    private readonly Action<MainWindow> _attach;
    private MainWindow? _window;

    public ManagerWindowLifecycle(Func<MainWindow> create, Action<MainWindow> attach)
    {
        _create = create;
        _attach = attach;
    }

    public MainWindow? Window => _window;

    public MainWindow EnsureCreated()
    {
        if (_window is not null)
        {
            return _window;
        }

        MainWindow? window = null;
        try
        {
            window = _create();
            _attach(window);
            _window = window;
            return window;
        }
        catch
        {
            window?.Close();
            throw;
        }
    }

    public void Reveal(WorkspaceKind workspace) => EnsureCreated().Reveal(workspace);

    public bool OpenReminderSource(ReminderSourceKind sourceKind, string sourceId) =>
        EnsureCreated().OpenReminderSource(sourceKind, sourceId);
}

internal sealed class ReminderDeliveryCoordinator : IDisposable
{
    private readonly DesktopWorkspace _workspace;
    private readonly DiagnosticLogger? _logger;
    private readonly Action<ReminderSourceKind, string>? _openSource;
    private readonly Dictionary<ActiveReminderOccurrenceKey, ActiveReminder> _active = new();
    private ReminderCenterWindow? _window;
    private bool _persistenceWriteInFlight;
    private bool _disposed;

    public ReminderDeliveryCoordinator(
        DesktopWorkspace workspace,
        DiagnosticLogger? logger,
        Action<ReminderSourceKind, string>? openSource = null)
    {
        _workspace = workspace;
        _logger = logger;
        _openSource = openSource;
        _workspace.StateChanged += OnWorkspaceStateChanged;
    }

    internal ReminderCenterWindow? ActiveWindow => _window;

    internal int ActiveWindowCount => _window?.IsVisible == true ? 1 : 0;

    internal int ActiveEntryCount => _active.Count;

    internal bool ActiveOccurrenceConfirmed =>
        _active.Count == 1 && _active.Values.Single().Confirmed;

    internal bool ActiveOccurrenceRendered =>
        _active.Count == 1 && _active.Values.Single().Rendered;

    internal int PersistenceFailureCount =>
        _active.Count == 1
            ? _active.Values.Single().PersistenceFailureCount +
              _active.Values.Single().AcknowledgementFailureCount +
              _active.Values.Single().SnoozeFailureCount +
              _active.Values.Single().CompletionFailureCount
            : 0;

    internal bool HasPendingPersistenceRetry =>
        _active.Count == 1 &&
        (_active.Values.Single().NextPersistenceRetryUtc is not null ||
         _active.Values.Single().NextAcknowledgementRetryUtc is not null ||
         _active.Values.Single().NextSnoozeRetryUtc is not null ||
         _active.Values.Single().NextCompletionRetryUtc is not null);

    internal DateTimeOffset? NextPersistenceRetryUtc =>
        _active.Count == 1
            ? _active.Values.Single().NextPersistenceRetryUtc ??
              _active.Values.Single().NextAcknowledgementRetryUtc ??
              _active.Values.Single().NextSnoozeRetryUtc ??
              _active.Values.Single().NextCompletionRetryUtc
            : null;

    public void Tick(DateTimeOffset nowUtc)
    {
        foreach (var active in _active.Values.ToArray())
        {
            if (!OccurrenceIsCurrent(active))
            {
                Remove(active);
                continue;
            }

            if (active.NextAcknowledgementRetryUtc is { } acknowledgementRetryUtc &&
                nowUtc >= acknowledgementRetryUtc)
            {
                Acknowledge(active, nowUtc);
                continue;
            }

            if (active.NextSnoozeRetryUtc is { } snoozeRetryUtc &&
                nowUtc >= snoozeRetryUtc)
            {
                Snooze(
                    active,
                    active.PendingSnoozeOption,
                    active.PendingCustomSnoozeBeijing,
                    nowUtc);
                continue;
            }

            if (active.NextCompletionRetryUtc is { } completionRetryUtc &&
                nowUtc >= completionRetryUtc)
            {
                CompleteNote(active, nowUtc);
                continue;
            }

            if (active.NextPersistenceRetryUtc is { } retryUtc && nowUtc >= retryUtc)
            {
                ConfirmRenderedOccurrence(active, nowUtc);
            }
        }

        var snapshot = _workspace.Snapshot;
        var countdownOrder = snapshot.Settings.Countdowns
            .Select((countdown, index) => (countdown.Id, index))
            .ToDictionary(static item => item.Id, static item => item.index, StringComparer.OrdinalIgnoreCase);
        var noteOrder = snapshot.Settings.Notes
            .Select((note, index) => (note.Id, index))
            .ToDictionary(static item => item.Id, static item => item.index, StringComparer.OrdinalIgnoreCase);
        var added = false;
        foreach (var occurrence in _workspace.GetDueReminderOccurrences(nowUtc))
        {
            var source = occurrence.Source;
            var key = ActiveReminderOccurrenceKey.Create(occurrence);
            if (_active.Values.Any(active =>
                    string.Equals(
                        active.Key.RuleId,
                        key.RuleId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!_active.ContainsKey(key))
            {
                Add(
                    key,
                    source,
                    source.Kind == ReminderSourceKind.Countdown
                        ? countdownOrder.GetValueOrDefault(source.Id, int.MaxValue)
                        : noteOrder.GetValueOrDefault(source.Id, int.MaxValue),
                    RuleOrder(source, key.RuleId));
                added = true;
            }
        }

        if (added && _window?.IsVisible != true)
        {
            ShowCenter();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspace.StateChanged -= OnWorkspaceStateChanged;
        _active.Clear();
        if (_window is { } window)
        {
            _window = null;
            if (window.IsVisible)
            {
                window.ForceClose();
            }
        }
    }

    private void OnWorkspaceStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed || _persistenceWriteInFlight)
        {
            return;
        }

        foreach (var active in _active.Values.ToArray())
        {
            if (!OccurrenceIsCurrent(active))
            {
                Remove(active);
            }
        }
    }

    private void Add(
        ActiveReminderOccurrenceKey key,
        ReminderSource source,
        int recordOrder,
        int ruleOrder)
    {
        var window = EnsureCenter();
        var active = new ActiveReminder(key, source);
        _active.Add(key, active);
        window.AddEntry(new ReminderCenterItem(key, source, recordOrder, ruleOrder));
    }

    private ReminderCenterWindow EnsureCenter()
    {
        if (_window is not null)
        {
            return _window;
        }

        var window = new ReminderCenterWindow();
        _window = window;
        window.EntryRendered += (_, args) => OnEntryRendered(args.Key);
        window.AcknowledgeRequested += (_, args) => OnAcknowledgeRequested(args.Key);
        window.SnoozeRequested += (_, args) => OnSnoozeRequested(args);
        window.CompleteRequested += (_, args) => OnCompleteRequested(args.Key);
        window.OpenSourceRequested += (_, args) => OnOpenSourceRequested(args.Key);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
        return window;
    }

    private void ShowCenter()
    {
        var window = _window ?? throw new InvalidOperationException("提醒中心尚未创建。");
        try
        {
            window.Show();
        }
        catch
        {
            _active.Clear();
            _window = null;
            throw;
        }
    }

    private void OnEntryRendered(ActiveReminderOccurrenceKey key)
    {
        if (!_active.TryGetValue(key, out var active) || active.Rendered)
        {
            return;
        }

        active.Rendered = true;
        ConfirmRenderedOccurrence(active, _workspace.Snapshot.UtcNow);
    }

    private void ConfirmRenderedOccurrence(ActiveReminder active, DateTimeOffset nowUtc)
    {
        if (!IsActive(active) || active.Confirmed || !active.Rendered)
        {
            return;
        }

        if (_persistenceWriteInFlight)
        {
            active.NextPersistenceRetryUtc ??= nowUtc.AddSeconds(5);
            return;
        }

        DesktopWorkspaceChangeResult result;
        _persistenceWriteInFlight = true;
        try
        {
            result = _workspace.ConfirmReminderDisplayed(
                active.Source.Kind,
                active.Source.Id,
                active.Key.RuleId,
                active.Key.ScheduledUtc);
        }
        finally
        {
            _persistenceWriteInFlight = false;
        }

        if (result.Succeeded)
        {
            active.Confirmed = true;
            active.NextPersistenceRetryUtc = null;
            _window?.SetDeliveryReady(active.Key);
            return;
        }

        active.PersistenceFailureCount++;
        active.NextPersistenceRetryUtc = nowUtc.Add(
            ReminderPersistenceRetrySchedule.GetDelay(active.PersistenceFailureCount));
        _window?.ShowDeliveryFailure(active.Key);
        _logger?.Error(
            "OneTimeReminderDeliveryPersistFailed",
            new IOException(result.Error));
    }

    private void OnAcknowledgeRequested(ActiveReminderOccurrenceKey key)
    {
        if (_active.TryGetValue(key, out var active) &&
            active.Confirmed &&
            active.NextAcknowledgementRetryUtc is null)
        {
            Acknowledge(active, _workspace.Snapshot.UtcNow);
        }
    }

    private void Acknowledge(ActiveReminder active, DateTimeOffset nowUtc)
    {
        if (!IsActive(active) || !active.Confirmed)
        {
            return;
        }

        if (_persistenceWriteInFlight)
        {
            active.NextAcknowledgementRetryUtc ??= nowUtc.AddSeconds(5);
            _window?.ShowAcknowledgementFailure(active.Key);
            return;
        }

        DesktopWorkspaceChangeResult result;
        _persistenceWriteInFlight = true;
        try
        {
            result = _workspace.AcknowledgeReminder(
                active.Source.Kind,
                active.Source.Id,
                active.Key.RuleId,
                active.Key.ScheduledUtc);
        }
        finally
        {
            _persistenceWriteInFlight = false;
        }

        if (result.Succeeded)
        {
            Remove(active);
            return;
        }

        active.AcknowledgementFailureCount++;
        active.NextAcknowledgementRetryUtc = nowUtc.Add(
            ReminderPersistenceRetrySchedule.GetDelay(active.AcknowledgementFailureCount));
        _window?.ShowAcknowledgementFailure(active.Key);
        _logger?.Error(
            "ReminderAcknowledgementPersistFailed",
            new IOException(result.Error));
    }

    private void OnSnoozeRequested(ReminderCenterSnoozeEventArgs args)
    {
        if (_active.TryGetValue(args.Key, out var active) &&
            active.Confirmed &&
            active.NextAcknowledgementRetryUtc is null &&
            active.NextSnoozeRetryUtc is null)
        {
            Snooze(active, args.Option, args.CustomBeijing, _workspace.Snapshot.UtcNow);
        }
    }

    private void Snooze(
        ActiveReminder active,
        ReminderSnoozeOption option,
        DateTimeOffset? customBeijing,
        DateTimeOffset nowUtc)
    {
        if (!IsActive(active) || !active.Confirmed)
        {
            return;
        }

        if (_persistenceWriteInFlight)
        {
            active.PendingSnoozeOption = option;
            active.PendingCustomSnoozeBeijing = customBeijing;
            active.NextSnoozeRetryUtc ??= nowUtc.AddSeconds(5);
            _window?.ShowSnoozeFailure(active.Key);
            return;
        }

        DesktopWorkspaceChangeResult result;
        _persistenceWriteInFlight = true;
        try
        {
            result = _workspace.SnoozeReminder(
                active.Source.Kind,
                active.Source.Id,
                active.Key.RuleId,
                active.Key.ScheduledUtc,
                option,
                customBeijing);
        }
        finally
        {
            _persistenceWriteInFlight = false;
        }

        if (result.Succeeded)
        {
            Remove(active);
            return;
        }

        if (option == ReminderSnoozeOption.Custom &&
            string.Equals(
                result.Error,
                "自定义稍后时间必须晚于当前北京时间。",
                StringComparison.Ordinal))
        {
            active.PendingSnoozeOption = ReminderSnoozeOption.TenMinutes;
            active.PendingCustomSnoozeBeijing = null;
            active.SnoozeFailureCount = 0;
            active.NextSnoozeRetryUtc = null;
            _window?.ShowCustomSnoozeError(active.Key, result.Error);
            return;
        }

        active.PendingSnoozeOption = option;
        active.PendingCustomSnoozeBeijing = customBeijing;
        active.SnoozeFailureCount++;
        active.NextSnoozeRetryUtc = nowUtc.Add(
            ReminderPersistenceRetrySchedule.GetDelay(active.SnoozeFailureCount));
        _window?.ShowSnoozeFailure(active.Key);
        _logger?.Error(
            "ReminderSnoozePersistFailed",
            new IOException(result.Error));
    }

    private void OnCompleteRequested(ActiveReminderOccurrenceKey key)
    {
        if (_active.TryGetValue(key, out var active) &&
            active.Confirmed &&
            active.NextAcknowledgementRetryUtc is null &&
            active.NextSnoozeRetryUtc is null &&
            active.NextCompletionRetryUtc is null)
        {
            CompleteNote(active, _workspace.Snapshot.UtcNow);
        }
    }

    private void CompleteNote(ActiveReminder active, DateTimeOffset nowUtc)
    {
        if (!IsActive(active) ||
            !active.Confirmed ||
            active.Source is not NoteReminderSource noteSource)
        {
            return;
        }

        if (_persistenceWriteInFlight)
        {
            active.NextCompletionRetryUtc ??= nowUtc.AddSeconds(5);
            _window?.ShowCompletionFailure(active.Key);
            return;
        }

        DesktopWorkspaceChangeResult result;
        _persistenceWriteInFlight = true;
        try
        {
            result = _workspace.Apply(new SetNoteCompletionChange(noteSource.Note.Id, true));
        }
        finally
        {
            _persistenceWriteInFlight = false;
        }

        if (result.Succeeded)
        {
            foreach (var noteOccurrence in _active.Values
                         .Where(candidate =>
                             candidate.Source is NoteReminderSource candidateNote &&
                             string.Equals(
                             candidateNote.Note.Id,
                             noteSource.Note.Id,
                             StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                Remove(noteOccurrence);
            }

            return;
        }

        active.CompletionFailureCount++;
        active.NextCompletionRetryUtc = nowUtc.Add(
            ReminderPersistenceRetrySchedule.GetDelay(active.CompletionFailureCount));
        _window?.ShowCompletionFailure(active.Key);
        _logger?.Error(
            "ReminderNoteCompletionPersistFailed",
            new IOException(result.Error));
    }

    private void OnOpenSourceRequested(ActiveReminderOccurrenceKey key)
    {
        if (_active.TryGetValue(key, out var active))
        {
            _openSource?.Invoke(active.Source.Kind, active.Source.Id);
        }
    }

    private void Remove(ActiveReminder active)
    {
        if (!IsActive(active))
        {
            return;
        }

        _active.Remove(active.Key);
        var window = _window;
        window?.RemoveEntry(active.Key);
        if (_active.Count == 0 && window?.IsVisible == true)
        {
            window.CloseWhenEmpty();
        }
    }

    private bool OccurrenceIsCurrent(ActiveReminder active)
    {
        var settings = _workspace.Snapshot.Settings;
        var current = FindSource(settings, active.Source.Kind, active.Source.Id);
        var currentRule = current?.ReminderRules.FirstOrDefault(rule =>
            string.Equals(rule.Id, active.Key.RuleId, StringComparison.OrdinalIgnoreCase));
        var expectedRule = active.Source.ReminderRules.FirstOrDefault(rule =>
            string.Equals(rule.Id, active.Key.RuleId, StringComparison.OrdinalIgnoreCase));
        if (current is null ||
            current is NoteReminderSource { Note.IsCompleted: true } ||
            currentRule is null ||
            !ReminderRuleSchedule.AreEquivalent(currentRule, expectedRule))
        {
            return false;
        }

        if (!ReminderSchedule.IsOccurrence(
                currentRule,
                active.Key.ScheduledUtc,
                current.CountdownTargetUtc) &&
            !(active.Confirmed && currentRule is CountdownRelativeReminderRule))
        {
            return false;
        }

        if (!settings.ReminderStates.TryGetValue(currentRule.Id, out var state) ||
            state.HandledThroughUtc is not { } handledThroughUtc ||
            handledThroughUtc < active.Key.ScheduledUtc)
        {
            return true;
        }

        var occurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
            currentRule,
            active.Key.ScheduledUtc).ToString();
        if (handledThroughUtc > active.Key.ScheduledUtc ||
            !string.Equals(
                state.LastDeliveredOccurrenceKey,
                occurrenceKey,
                StringComparison.Ordinal))
        {
            return false;
        }

        return !MinminCountdown.Core.ReminderOccurrenceKey.TryParse(
                   state.LastDispositionOccurrenceKey,
                   out var disposition) ||
               disposition.ScheduledUtc < active.Key.ScheduledUtc;
    }

    private bool IsActive(ActiveReminder expected) =>
        _active.TryGetValue(expected.Key, out var current) && ReferenceEquals(current, expected);

    private static ReminderSource? FindSource(
        CountdownSettings settings,
        ReminderSourceKind sourceKind,
        string sourceId) =>
        sourceKind == ReminderSourceKind.Countdown
            ? settings.Countdowns
                .Where(countdown => string.Equals(
                    countdown.Id,
                    sourceId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static countdown => (ReminderSource)new CountdownReminderSource(countdown))
                .FirstOrDefault()
            : settings.Notes
                .Where(note => string.Equals(
                    note.Id,
                    sourceId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static note => (ReminderSource)new NoteReminderSource(note))
                .FirstOrDefault();

    private static int RuleOrder(ReminderSource source, string ruleId)
    {
        for (var index = 0; index < source.ReminderRules.Count; index++)
        {
            if (string.Equals(source.ReminderRules[index].Id, ruleId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private sealed class ActiveReminder
    {
        public ActiveReminder(
            ActiveReminderOccurrenceKey key,
            ReminderSource source)
        {
            Key = key;
            Source = source;
        }

        public ActiveReminderOccurrenceKey Key { get; }

        public ReminderSource Source { get; }

        public bool Rendered { get; set; }

        public bool Confirmed { get; set; }

        public int PersistenceFailureCount { get; set; }

        public DateTimeOffset? NextPersistenceRetryUtc { get; set; }

        public int AcknowledgementFailureCount { get; set; }

        public DateTimeOffset? NextAcknowledgementRetryUtc { get; set; }

        public ReminderSnoozeOption PendingSnoozeOption { get; set; }

        public DateTimeOffset? PendingCustomSnoozeBeijing { get; set; }

        public int SnoozeFailureCount { get; set; }

        public DateTimeOffset? NextSnoozeRetryUtc { get; set; }

        public int CompletionFailureCount { get; set; }

        public DateTimeOffset? NextCompletionRetryUtc { get; set; }
    }
}

internal readonly record struct ActiveReminderOccurrenceKey(string RuleId, DateTimeOffset ScheduledUtc)
{
    public static ActiveReminderOccurrenceKey Create(DueReminderOccurrence occurrence) =>
        new(occurrence.Rule.Id, occurrence.ScheduledUtc.ToUniversalTime());

    public static ActiveReminderOccurrenceKey Create(NoteRecord note, DateTimeOffset nowUtc)
    {
        var rule = note.ReminderRules.FirstOrDefault();
        if (rule is null)
        {
            throw new InvalidOperationException("没有提醒规则的便签不存在 occurrence。");
        }

        return new ActiveReminderOccurrenceKey(
            rule.Id,
            ReminderSchedule.GetScheduledUtc(rule, nowUtc));
    }
}

internal static class ReminderPersistenceRetrySchedule
{
    public static TimeSpan GetDelay(int failureCount) => failureCount switch
    {
        <= 0 => throw new ArgumentOutOfRangeException(nameof(failureCount)),
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(10),
        3 => TimeSpan.FromSeconds(20),
        _ => TimeSpan.FromSeconds(30)
    };
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
