using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed record InstanceActivationScope(string ScopeId, string PipeName)
{
    public static InstanceActivationScope Create(string applicationRoot, string settingsPath)
    {
        var mutexName = SingleInstanceMutex.GetCurrentUserMutexName(applicationRoot, settingsPath);
        var scopeId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mutexName)));
        return new InstanceActivationScope(
            scopeId,
            $"MinminCountdown.Activation-{scopeId[..32]}");
    }
}

internal enum InstanceActivationClientOutcome
{
    Succeeded,
    TimedOut,
    ProtocolMismatch,
    ListenerFailed,
    Rejected
}

internal enum InstanceActivationListenerBehavior
{
    Normal,
    Timeout,
    ProtocolMismatch,
    Disconnect,
    CallbackFailureOnce
}

internal sealed record InstanceActivationClientResult(
    InstanceActivationClientOutcome Outcome,
    string ScopeId,
    string RequestId,
    int? ServerProcessId,
    bool? ForegroundPermissionGranted);

internal sealed record InstanceActivationHandlingResult(
    bool Activated,
    string StateBefore,
    string StateAfter,
    int InteractiveFloatingWindowCount,
    long ForegroundWindowHandle,
    bool FloatingWindowIsActive,
    bool MainWindowVisibleBefore,
    bool MainWindowVisibleAfter,
    bool ManagerStatePreserved);

internal static class InstanceActivationClient
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static InstanceActivationClientResult SendOnce(
        InstanceActivationScope scope,
        TimeSpan timeout)
    {
        var requestId = Guid.NewGuid().ToString("N");
        int? serverProcessId = null;
        bool? foregroundPermissionGranted = null;
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                scope.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.ConnectAsync(cancellation.Token).GetAwaiter().GetResult();
            if (!ForegroundActivationPermission.TryAllowServerProcess(
                    pipe.SafePipeHandle,
                    out serverProcessId,
                    out foregroundPermissionGranted))
            {
                return new InstanceActivationClientResult(
                    InstanceActivationClientOutcome.Rejected,
                    scope.ScopeId,
                    requestId,
                    serverProcessId,
                    foregroundPermissionGranted);
            }

            using var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            writer.WriteLine(JsonSerializer.Serialize(new ActivationRequest(
                1,
                "activate-floating-window",
                scope.ScopeId,
                requestId)));
            var responseLine = ActivationWireProtocol.ReadLineAsync(reader, cancellation.Token)
                .GetAwaiter()
                .GetResult();
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return new InstanceActivationClientResult(
                    InstanceActivationClientOutcome.ListenerFailed,
                    scope.ScopeId,
                    requestId,
                    serverProcessId,
                    foregroundPermissionGranted);
            }

            ActivationAcknowledgement? acknowledgement;
            try
            {
                acknowledgement = JsonSerializer.Deserialize<ActivationAcknowledgement>(responseLine);
            }
            catch (JsonException)
            {
                acknowledgement = null;
            }

            if (acknowledgement is null ||
                acknowledgement.SchemaVersion != 1 ||
                !string.Equals(acknowledgement.Kind, "activation-ack", StringComparison.Ordinal) ||
                !string.Equals(acknowledgement.ScopeId, scope.ScopeId, StringComparison.Ordinal) ||
                !string.Equals(acknowledgement.RequestId, requestId, StringComparison.Ordinal))
            {
                return new InstanceActivationClientResult(
                    InstanceActivationClientOutcome.ProtocolMismatch,
                    scope.ScopeId,
                    requestId,
                    serverProcessId,
                    foregroundPermissionGranted);
            }

            return new InstanceActivationClientResult(
                string.Equals(acknowledgement.Result, "activated", StringComparison.Ordinal)
                    ? InstanceActivationClientOutcome.Succeeded
                    : InstanceActivationClientOutcome.Rejected,
                scope.ScopeId,
                requestId,
                serverProcessId,
                foregroundPermissionGranted);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            return new InstanceActivationClientResult(
                InstanceActivationClientOutcome.TimedOut,
                scope.ScopeId,
                requestId,
                serverProcessId,
                foregroundPermissionGranted);
        }
        catch (InvalidDataException)
        {
            return new InstanceActivationClientResult(
                InstanceActivationClientOutcome.ProtocolMismatch,
                scope.ScopeId,
                requestId,
                serverProcessId,
                foregroundPermissionGranted);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return new InstanceActivationClientResult(
                InstanceActivationClientOutcome.ListenerFailed,
                scope.ScopeId,
                requestId,
                serverProcessId,
                foregroundPermissionGranted);
        }
    }
}

internal sealed class InstanceActivationServer : IDisposable
{
    private static readonly TimeSpan ConnectionReadTimeout = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly InstanceActivationScope _scope;
    private readonly Func<string, CancellationToken, Task<InstanceActivationHandlingResult>> _activate;
    private readonly InstanceActivationListenerBehavior _behavior;
    private readonly Action<string>? _requestReceived;
    private readonly Action? _listenerFailed;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _pipeGate = new();
    private readonly Task _listenerTask;
    private NamedPipeServerStream? _listeningPipe;
    private int _disposed;

    public InstanceActivationServer(
        InstanceActivationScope scope,
        Func<string, CancellationToken, Task<InstanceActivationHandlingResult>> activate,
        InstanceActivationListenerBehavior behavior = InstanceActivationListenerBehavior.Normal,
        Action<string>? requestReceived = null,
        Action? listenerFailed = null)
    {
        _scope = scope;
        _activate = activate;
        _behavior = behavior;
        _requestReceived = requestReceived;
        _listenerFailed = listenerFailed;
        _listeningPipe = CreatePipe();
        _listenerTask = Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellation.Cancel();
        NamedPipeServerStream? listeningPipe;
        lock (_pipeGate)
        {
            listeningPipe = _listeningPipe;
            _listeningPipe = null;
        }

        listeningPipe?.Dispose();
        try
        {
            _listenerTask.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
        catch
        {
            NotifyListenerFailed();
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe;
            lock (_pipeGate)
            {
                pipe = _listeningPipe;
                _listeningPipe = null;
            }

            if (pipe is null)
            {
                return;
            }

            var replacementPublished = false;
            using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);
                    if (_cancellation.IsCancellationRequested)
                    {
                        return;
                    }

                    replacementPublished = TryPublishReplacementPipe();
                    if (!replacementPublished)
                    {
                        return;
                    }

                    await HandleConnectionAsync(pipe, _cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    _cancellation.IsCancellationRequested &&
                    exception is OperationCanceledException or IOException or ObjectDisposedException)
                {
                    return;
                }
                catch (Exception)
                {
                    NotifyListenerFailed();
                    if (!replacementPublished && !TryPublishReplacementPipe())
                    {
                        return;
                    }
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCancellation.CancelAfter(ConnectionReadTimeout);
        using var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        string? requestLine;
        try
        {
            requestLine = await ActivationWireProtocol.ReadLineAsync(reader, readCancellation.Token).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            requestLine = null;
        }
        ActivationRequest? request;
        try
        {
            request = string.IsNullOrWhiteSpace(requestLine)
                ? null
                : JsonSerializer.Deserialize<ActivationRequest>(requestLine);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null ||
            request.SchemaVersion != 1 ||
            !string.Equals(request.Kind, "activate-floating-window", StringComparison.Ordinal) ||
            !string.Equals(request.ScopeId, _scope.ScopeId, StringComparison.Ordinal) ||
            !Guid.TryParseExact(request.RequestId, "N", out _))
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new ActivationAcknowledgement(
                1,
                "activation-ack",
                _scope.ScopeId,
                request?.RequestId ?? string.Empty,
                "rejected"))).ConfigureAwait(false);
            return;
        }

        _requestReceived?.Invoke(request.RequestId);

        if (_behavior == InstanceActivationListenerBehavior.Timeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_behavior == InstanceActivationListenerBehavior.ProtocolMismatch)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new ActivationAcknowledgement(
                1,
                "activation-ack",
                _scope.ScopeId,
                Guid.NewGuid().ToString("N"),
                "activated"))).ConfigureAwait(false);
            return;
        }

        if (_behavior == InstanceActivationListenerBehavior.Disconnect)
        {
            return;
        }

        var result = await _activate(request.RequestId, cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new ActivationAcknowledgement(
            1,
            "activation-ack",
            _scope.ScopeId,
            request.RequestId,
            result.Activated ? "activated" : "rejected"))).ConfigureAwait(false);
    }

    private NamedPipeServerStream CreatePipe() => new(
        _scope.PipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private bool TryPublishReplacementPipe()
    {
        NamedPipeServerStream replacement;
        try
        {
            replacement = CreatePipe();
        }
        catch
        {
            NotifyListenerFailed();
            return false;
        }

        lock (_pipeGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || _cancellation.IsCancellationRequested)
            {
                replacement.Dispose();
                return false;
            }

            if (_listeningPipe is not null)
            {
                replacement.Dispose();
                throw new InvalidOperationException("实例激活 listener 已有待监听管道。");
            }

            _listeningPipe = replacement;
            return true;
        }
    }

    private void NotifyListenerFailed()
    {
        try
        {
            _listenerFailed?.Invoke();
        }
        catch
        {
        }
    }

}

internal sealed record ActivationRequest(
    int SchemaVersion,
    string Kind,
    string ScopeId,
    string RequestId);

internal sealed record ActivationAcknowledgement(
    int SchemaVersion,
    string Kind,
    string ScopeId,
    string RequestId,
    string Result);

internal static class ActivationWireProtocol
{
    private const int MaxMessageCharacters = 4096;

    public static async Task<string?> ReadLineAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[1];
        while (result.Length <= MaxMessageCharacters)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return result.Length == 0 ? null : result.ToString();
            }

            if (buffer[0] == '\n')
            {
                if (result.Length > 0 && result[^1] == '\r')
                {
                    result.Length--;
                }

                return result.ToString();
            }

            result.Append(buffer[0]);
        }

        throw new InvalidDataException("实例激活协议消息超过允许长度。");
    }
}

internal static class ForegroundActivationPermission
{
    public static bool TryAllowServerProcess(
        SafePipeHandle pipeHandle,
        out int? serverProcessId,
        out bool? permissionGranted)
    {
        serverProcessId = null;
        permissionGranted = null;
        try
        {
            if (!GetNamedPipeServerProcessId(pipeHandle, out var processId) ||
                processId == 0 ||
                processId > int.MaxValue)
            {
                return false;
            }

            serverProcessId = checked((int)processId);
            permissionGranted = AllowSetForegroundWindow(serverProcessId.Value);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipeHandle,
        out uint serverProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
