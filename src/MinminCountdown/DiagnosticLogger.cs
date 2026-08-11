using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinminCountdown;

internal sealed class DiagnosticLogger
{
    private const long MaxBytes = 1_048_576;
    private const int MaxArchives = 3;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly ConcurrentDictionary<string, object> LocalGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate;
    private readonly string _mutexName;

    public DiagnosticLogger(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _gate = LocalGates.GetOrAdd(Path, static _ => new object());
        var normalizedPath = Path.ToUpperInvariant();
        var pathHash = Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(normalizedPath)));
        _mutexName = $"Local\\MinminCountdown.Diagnostic-{pathHash[..32]}";
    }

    public string Path { get; }

    public void Info(string eventName) => Write("Info", eventName, null, null);

    public void Activation(string eventName, ActivationDiagnostic diagnostic) =>
        Write("Info", eventName, null, diagnostic);

    public void Warning(string eventName, Exception? exception = null) => Write("Warning", eventName, exception, null);

    public void Error(string eventName, Exception? exception = null) => Write("Error", eventName, exception, null);

    private void Write(
        string level,
        string eventName,
        Exception? exception,
        ActivationDiagnostic? activation)
    {
        try
        {
            var entry = new DiagnosticEntry(
                1,
                DateTimeOffset.UtcNow,
                level,
                Truncate(eventName, 256),
                Environment.ProcessId,
                Process.GetCurrentProcess().SessionId,
                Environment.Version.ToString(),
                exception?.GetType().FullName,
                exception is null ? null : Truncate(exception.Message, 2048),
                exception?.HResult,
                exception is null ? null : Truncate(exception.StackTrace, 4096),
                activation?.ScopeId,
                activation?.RequestId,
                activation?.ServerProcessId,
                activation?.ForegroundPermissionGranted,
                activation?.Result,
                activation?.StateBefore,
                activation?.StateAfter,
                activation?.InteractiveFloatingWindowCount,
                activation?.ForegroundWindowHandle,
                activation?.FloatingWindowIsActive,
                activation?.MainWindowVisibleBefore,
                activation?.MainWindowVisibleAfter,
                activation?.ManagerStatePreserved);
            var line = JsonSerializer.Serialize(entry, JsonOptions);

            lock (_gate)
            {
                using var mutex = new Mutex(false, _mutexName);
                var acquired = false;
                try
                {
                    try
                    {
                        mutex.WaitOne();
                        acquired = true;
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }

                    var directory = System.IO.Path.GetDirectoryName(Path)
                        ?? throw new InvalidOperationException("诊断日志路径缺少父目录。");
                    Directory.CreateDirectory(directory);
                    RotateIfNeeded(line);
                    File.AppendAllText(Path, line + Environment.NewLine, Utf8NoBom);
                }
                finally
                {
                    if (acquired)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }
        catch
        {
            // Diagnostics must never replace the original application failure.
        }
    }

    private void RotateIfNeeded(string nextLine)
    {
        if (!File.Exists(Path))
        {
            return;
        }

        var nextBytes = Utf8NoBom.GetByteCount(nextLine + Environment.NewLine);
        if (new FileInfo(Path).Length + nextBytes <= MaxBytes)
        {
            return;
        }

        for (var archive = MaxArchives; archive >= 1; archive--)
        {
            var source = archive == 1 ? Path : $"{Path}.{archive - 1}";
            if (!File.Exists(source))
            {
                continue;
            }

            var destination = $"{Path}.{archive}";
            File.Move(source, destination, overwrite: true);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private sealed record DiagnosticEntry(
        int SchemaVersion,
        DateTimeOffset TimestampUtc,
        string Level,
        string? Event,
        int ProcessId,
        int SessionId,
        string RuntimeVersion,
        string? ExceptionType,
        string? ExceptionMessage,
        int? HResult,
        string? StackTrace,
        string? ActivationScopeId,
        string? ActivationRequestId,
        int? ActivationServerProcessId,
        bool? ActivationForegroundPermissionGranted,
        string? ActivationResult,
        string? ActivationStateBefore,
        string? ActivationStateAfter,
        int? InteractiveFloatingWindowCount,
        long? ActivationForegroundWindowHandle,
        bool? ActivationFloatingWindowIsActive,
        bool? MainWindowVisibleBefore,
        bool? MainWindowVisibleAfter,
        bool? ManagerStatePreserved);
}

internal sealed record ActivationDiagnostic(
    string ScopeId,
    string? RequestId,
    string Result,
    int? ServerProcessId = null,
    bool? ForegroundPermissionGranted = null,
    string? StateBefore = null,
    string? StateAfter = null,
    int? InteractiveFloatingWindowCount = null,
    long? ForegroundWindowHandle = null,
    bool? FloatingWindowIsActive = null,
    bool? MainWindowVisibleBefore = null,
    bool? MainWindowVisibleAfter = null,
    bool? ManagerStatePreserved = null);
