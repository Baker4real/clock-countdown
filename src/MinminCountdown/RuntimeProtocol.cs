using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MinminCountdown;

internal static class ApplicationPaths
{
    // #275：安装布局里程序目录（应用自身与随包资源所在目录）是应用根下的 app\，应用根总是它的上一级，
    // 与工作目录无关。源码构建输出等其他布局由调用方经工作目录指定应用根；开发和测试运行另用命令行开关
    // 显式给出设置与日志位置。
    public static string ResolveApplicationRoot()
    {
        var programDirectory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var programParent = Directory.GetParent(programDirectory)?.FullName;
        if (programParent is not null &&
            string.Equals(Path.GetFileName(programDirectory), "app", StringComparison.OrdinalIgnoreCase))
        {
            return programParent;
        }

        return Path.GetFullPath(Environment.CurrentDirectory);
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

    public static RuntimeReceipt WriteWindowReady(string path, string calendarState)
    {
        if (calendarState is not ("Pending" or "Ready" or "Unavailable"))
        {
            throw new ArgumentOutOfRangeException(nameof(calendarState));
        }

        // schema v5 不再携带启动令牌或 adapter 路径。安装器经 processId 找到存活的已安装 EXE，
        // 再独立核对其交互会话与载荷清单哈希。
        var receipt = new RuntimeReceipt(
            5,
            "window-ready",
            Environment.ProcessId,
            Process.GetCurrentProcess().SessionId,
            DateTimeOffset.UtcNow,
            calendarState,
            "China Standard Time");
        WriteJsonAtomically(path, receipt);
        return receipt;
    }

    private static void WriteJsonAtomically<T>(string path, T value)
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

    // 安装器轮询运行状态回执时用 File.ReadAllText 打开的读句柄不共享删除，此刻覆盖目标报 Access denied；
    // 临时文件被扫描占住时报共享冲突。这类占用是瞬时的：按设置文件替换的同一预算退避约 2 秒，仍失败才把
    // 路径写进消息抛出，不让一次轮询撞车变成未处理异常。
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

internal sealed record RuntimeReceipt(
    int SchemaVersion,
    string Stage,
    int ProcessId,
    int SessionId,
    DateTimeOffset ReadyAtUtc,
    string CalendarState,
    string TimeZoneId);
