using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MinminCountdown;

internal static class ApplicationPaths
{
    public const string AdapterFileName = "敏敏子倒计时.ps1";

    public static string ResolveApplicationRoot()
    {
        var executableDirectory = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath))!
            : Path.GetFullPath(AppContext.BaseDirectory);
        var executableParent = Directory.GetParent(executableDirectory)?.FullName;
        if (executableParent is not null &&
            string.Equals(Path.GetFileName(executableDirectory.TrimEnd(Path.DirectorySeparatorChar)), "app", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(executableParent, AdapterFileName)))
        {
            return executableParent;
        }

        var currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        if (File.Exists(Path.Combine(currentDirectory, AdapterFileName)))
        {
            return currentDirectory;
        }

        var cursor = new DirectoryInfo(executableDirectory);
        while (cursor is not null)
        {
            if (File.Exists(Path.Combine(cursor.FullName, AdapterFileName)))
            {
                return cursor.FullName;
            }

            cursor = cursor.Parent;
        }

        return executableParent ?? executableDirectory;
    }
}

internal static class RuntimeProtocol
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private static readonly TimeSpan TransientLockBudget = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static StartupRequest ReadStartupRequest(string startupRequestPath, bool required = false)
    {
        if (!required)
        {
            return new StartupRequest(Guid.NewGuid().ToString("N"), false, "none", null);
        }

        try
        {
            return ParseStartupRequest(startupRequestPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new InvalidDataException("启动请求存在但无法验证，拒绝以可编辑模式启动。", exception);
        }
    }

    public static string ReadLaunchToken(string startupRequestPath) =>
        ReadStartupRequest(startupRequestPath).LaunchToken;

    public static string? ReadAcceptanceState(string startupRequestPath, string launchToken)
    {
        var request = ParseStartupRequest(startupRequestPath);
        return string.Equals(request.LaunchToken, launchToken, StringComparison.Ordinal)
            ? request.AcceptanceState
            : null;
    }

    public static bool MarkAcceptanceReady(string startupRequestPath, string launchToken) =>
        TransitionAcceptanceState(
            startupRequestPath,
            launchToken,
            "released",
            "ready",
            "readyAtUtc",
            acceptanceReadOnly: true);

    private static StartupRequest ParseStartupRequest(string startupRequestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(startupRequestPath, Utf8NoBom));
        if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaElement) ||
            schemaElement.ValueKind != JsonValueKind.Number ||
            !schemaElement.TryGetInt32(out var schemaVersion) ||
            schemaVersion != 1)
        {
            throw new InvalidDataException("启动请求 schemaVersion 必须为 1。");
        }

        if (!document.RootElement.TryGetProperty("launchToken", out var tokenElement))
        {
            throw new InvalidDataException("启动请求缺少 launchToken。");
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidDataException("启动请求的 launchToken 无效。");
        }

        if (!document.RootElement.TryGetProperty("acceptanceReadOnly", out var readOnlyElement) ||
            readOnlyElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("启动请求的 acceptanceReadOnly 必须是布尔值。");
        }

        var acceptanceReadOnly = readOnlyElement.GetBoolean();
        var acceptanceState = document.RootElement.TryGetProperty("acceptanceState", out var stateElement)
            ? stateElement.GetString()
            : null;
        if (acceptanceState is not ("active" or "released" or "ready" or "finalized" or "none"))
        {
            throw new InvalidDataException("启动请求的 acceptanceState 无效。");
        }

        var expectedReadOnly = acceptanceState is "active" or "released" or "ready";
        if (acceptanceReadOnly != expectedReadOnly)
        {
            throw new InvalidDataException("启动请求的 acceptanceReadOnly 与 acceptanceState 不一致。");
        }

        if (!document.RootElement.TryGetProperty("requestedAtUtc", out var requestedAtElement) ||
            requestedAtElement.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParseExact(
                requestedAtElement.GetString(),
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var requestedAtUtc))
        {
            throw new InvalidDataException("启动请求的 requestedAtUtc 无效。");
        }

        return new StartupRequest(
            token,
            acceptanceReadOnly,
            acceptanceState,
            requestedAtUtc.ToUniversalTime());
    }

    private static bool TransitionAcceptanceState(
        string startupRequestPath,
        string launchToken,
        string expectedState,
        string nextState,
        string timestampProperty,
        bool acceptanceReadOnly)
    {
        var request = JsonNode.Parse(File.ReadAllText(startupRequestPath, Utf8NoBom)) as JsonObject
            ?? throw new InvalidDataException("启动请求必须是 JSON object。");
        var requestToken = request["launchToken"]?.GetValue<string>();
        var currentState = request["acceptanceState"]?.GetValue<string>();
        if (!string.Equals(requestToken, launchToken, StringComparison.Ordinal) ||
            !string.Equals(currentState, expectedState, StringComparison.Ordinal))
        {
            return false;
        }

        request["acceptanceReadOnly"] = acceptanceReadOnly;
        request["acceptanceState"] = nextState;
        request[timestampProperty] = DateTimeOffset.UtcNow.ToString("O");
        WriteJsonAtomically(startupRequestPath, request);
        return true;
    }

    public static RuntimeReceipt WriteWindowReady(
        string path,
        string launchToken,
        string applicationPath,
        string calendarState)
    {
        if (string.IsNullOrWhiteSpace(launchToken))
        {
            throw new InvalidOperationException("启动回执缺少 launchToken。");
        }

        if (calendarState is not ("Pending" or "Ready" or "Unavailable"))
        {
            throw new ArgumentOutOfRangeException(nameof(calendarState));
        }

        var receipt = new RuntimeReceipt(
            4,
            "window-ready",
            launchToken,
            Environment.ProcessId,
            Process.GetCurrentProcess().SessionId,
            Path.GetFullPath(applicationPath),
            DateTimeOffset.UtcNow,
            calendarState,
            "China Standard Time");
        WriteJsonAtomically(path, receipt);
        return receipt;
    }

    public static AcceptanceReadyReceipt WriteAcceptanceReady(
        string path,
        string launchToken,
        string applicationPath)
    {
        if (string.IsNullOrWhiteSpace(launchToken))
        {
            throw new InvalidOperationException("验收确认回执缺少 launchToken。");
        }

        var receipt = new AcceptanceReadyReceipt(
            1,
            "finalized-ready",
            launchToken,
            Environment.ProcessId,
            Process.GetCurrentProcess().SessionId,
            Path.GetFullPath(applicationPath),
            DateTimeOffset.UtcNow);
        WriteJsonAtomically(path, receipt);
        return receipt;
    }

    public static void WriteJsonAtomically<T>(string path, T value)
    {
        var resolvedPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(resolvedPath)
            ?? throw new InvalidOperationException("运行状态路径缺少父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{resolvedPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions), Utf8NoBom);
            ReplaceWithTransientRetry(temporaryPath, resolvedPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    // 安装器轮询启动请求和回执时用 File.ReadAllText 打开的读句柄不共享删除，此刻覆盖目标报 Access denied；
    // 临时文件被扫描占住时报共享冲突。这类占用是瞬时的：按设置文件替换的同一预算退避约 2 秒，仍失败才把
    // 路径写进消息抛出，不让一次轮询撞车变成验收中的未处理异常。
    private static void ReplaceWithTransientRetry(string temporaryPath, string resolvedPath)
    {
        var elapsed = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, resolvedPath, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                (exception is UnauthorizedAccessException ||
                 exception is IOException { HResult: SharingViolation or LockViolation }) &&
                elapsed.Elapsed < TransientLockBudget)
            {
                Thread.Sleep(Math.Min(20 * attempt, 200));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var message =
                    $"运行状态文件替换失败：{resolvedPath}（尝试 {attempt} 次，历时 {elapsed.ElapsedMilliseconds} ms）。{exception.Message}";
                Exception failure = exception is UnauthorizedAccessException
                    ? new UnauthorizedAccessException(message, exception)
                    : new IOException(message, exception);
                failure.HResult = exception.HResult;
                throw failure;
            }
        }
    }
}

internal sealed class StartupAcceptanceProtocol
{
    private readonly string _applicationRoot;
    private readonly string _startupRequestPath;
    private readonly string _runtimeStatusPath;
    private readonly string _acceptanceStatusPath;

    private StartupAcceptanceProtocol(
        string applicationRoot,
        string startupRequestPath,
        string runtimeStatusPath,
        string acceptanceStatusPath,
        StartupRequest request)
    {
        _applicationRoot = applicationRoot;
        _startupRequestPath = startupRequestPath;
        _runtimeStatusPath = runtimeStatusPath;
        _acceptanceStatusPath = acceptanceStatusPath;
        Request = request;
    }

    public StartupRequest Request { get; }

    public string LaunchToken => Request.LaunchToken;

    public bool AcceptanceReadOnly => Request.AcceptanceReadOnly;

    public bool RecoveryWritesAllowed =>
        !string.Equals(Request.AcceptanceState, "active", StringComparison.Ordinal);

    public string RuntimeStatusPath => _runtimeStatusPath;

    public string AcceptanceStatusPath => _acceptanceStatusPath;

    public static StartupAcceptanceProtocol Load(string applicationRoot, bool requireStartupRequest)
    {
        var startupRequestPath = Path.Combine(applicationRoot, "startup-request.json");
        return new StartupAcceptanceProtocol(
            applicationRoot,
            startupRequestPath,
            Path.Combine(applicationRoot, "runtime-status.json"),
            Path.Combine(applicationRoot, "acceptance-status.json"),
            RuntimeProtocol.ReadStartupRequest(startupRequestPath, requireStartupRequest));
    }

    public RuntimeReceipt WriteWindowReady(string calendarState) =>
        RuntimeProtocol.WriteWindowReady(
            _runtimeStatusPath,
            LaunchToken,
            AdapterPath,
            calendarState);

    public string? ReadAcceptanceState() =>
        RuntimeProtocol.ReadAcceptanceState(_startupRequestPath, LaunchToken);

    public bool MarkAcceptanceReady() =>
        RuntimeProtocol.MarkAcceptanceReady(_startupRequestPath, LaunchToken);

    public AcceptanceReadyReceipt WriteAcceptanceReady() =>
        RuntimeProtocol.WriteAcceptanceReady(
            _acceptanceStatusPath,
            LaunchToken,
            AdapterPath);

    private string AdapterPath => Path.Combine(_applicationRoot, ApplicationPaths.AdapterFileName);
}

internal sealed record StartupRequest(
    string LaunchToken,
    bool AcceptanceReadOnly,
    string AcceptanceState,
    DateTimeOffset? MigrationUtc);

internal sealed record RuntimeReceipt(
    int SchemaVersion,
    string Stage,
    string LaunchToken,
    int ProcessId,
    int SessionId,
    string ApplicationPath,
    DateTimeOffset ReadyAtUtc,
    string CalendarState,
    string TimeZoneId);

internal sealed record AcceptanceReadyReceipt(
    int SchemaVersion,
    string Stage,
    string LaunchToken,
    int ProcessId,
    int SessionId,
    string ApplicationPath,
    DateTimeOffset ReadyAtUtc);
