using System.Diagnostics;
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
    private readonly object _gate = new();

    public DiagnosticLogger(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public void Info(string eventName) => Write("Info", eventName, null);

    public void Warning(string eventName, Exception? exception = null) => Write("Warning", eventName, exception);

    public void Error(string eventName, Exception? exception = null) => Write("Error", eventName, exception);

    private void Write(string level, string eventName, Exception? exception)
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
                exception is null ? null : Truncate(exception.StackTrace, 4096));
            var line = JsonSerializer.Serialize(entry, JsonOptions);

            lock (_gate)
            {
                var directory = System.IO.Path.GetDirectoryName(Path)
                    ?? throw new InvalidOperationException("诊断日志路径缺少父目录。");
                Directory.CreateDirectory(directory);
                RotateIfNeeded(line);
                File.AppendAllText(Path, line + Environment.NewLine, Utf8NoBom);
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
        string? StackTrace);
}
