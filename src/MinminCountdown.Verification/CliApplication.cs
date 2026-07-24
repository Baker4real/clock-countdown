using System.Text.Encodings.Web;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using MinminCountdown.Core;

namespace MinminCountdown.Verification;

internal static class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        try
        {
            var invocation = CliInvocation.Parse(args);
            return invocation.Command switch
            {
                "verify" => RunVerification(invocation),
                "round-trip" => RunRoundTrip(invocation),
                "mutex-name" => RunMutexName(invocation),
                "mutex-probe" => RunMutexProbe(invocation),
                "hold-mutex" => RunHoldMutex(invocation),
                _ => throw new CliUsageException($"未知命令：{invocation.Command}")
            };
        }
        catch (CliUsageException exception)
        {
            WriteSummary(
                args.Contains("--json", StringComparer.Ordinal),
                new CommandSummary(1, "usage", false, 0, 1, new[] { exception.Message }));
            return ExitCodes.UsageError;
        }
        catch (SettingsValidationException exception)
        {
            WriteSummary(
                args.Contains("--json", StringComparer.Ordinal),
                new CommandSummary(1, InferCommand(args), false, 0, 1, new[] { exception.Message }));
            return ExitCodes.VerificationFailed;
        }
        catch (IOException exception)
        {
            WriteSummary(
                args.Contains("--json", StringComparer.Ordinal),
                new CommandSummary(1, InferCommand(args), false, 0, 1, new[] { exception.Message }));
            return ExitCodes.VerificationFailed;
        }
        catch (Exception exception)
        {
            WriteSummary(
                args.Contains("--json", StringComparer.Ordinal),
                new CommandSummary(1, "runtime", false, 0, 1, new[] { exception.Message }));
            return ExitCodes.RuntimeError;
        }
    }

    private static int RunVerification(CliInvocation invocation)
    {
        invocation.EnsureOnlyOptions("--json", "--settings-file");
        var settingsFile = invocation.GetOptionalValue("--settings-file");
        var results = VerificationSuite.RunAll(settingsFile);
        var failures = results
            .Where(static result => !result.Success)
            .Select(static result => $"{result.Name}: {result.Error}")
            .ToArray();
        var summary = new CommandSummary(
            1,
            "verify",
            failures.Length == 0,
            results.Count - failures.Length,
            failures.Length,
            failures);

        WriteSummary(invocation.HasFlag("--json"), summary);
        return summary.Success ? ExitCodes.Success : ExitCodes.VerificationFailed;
    }

    private static int RunRoundTrip(CliInvocation invocation)
    {
        invocation.EnsureOnlyOptions("--json", "--input", "--output");
        var inputPath = Path.GetFullPath(invocation.GetRequiredValue("--input"));
        var outputPath = Path.GetFullPath(invocation.GetRequiredValue("--output"));
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("--input 与 --output 必须是不同文件，避免覆盖来源 fixture。");
        }

        var originalInput = File.ReadAllBytes(inputPath);
        var store = new SettingsStore();
        var input = store.Load(inputPath);
        store.Save(outputPath, input.Settings);
        var output = store.Load(outputPath);
        var canonical = store.Serialize(output.Settings);
        var written = File.ReadAllBytes(outputPath);

        if (!originalInput.AsSpan().SequenceEqual(File.ReadAllBytes(inputPath)))
        {
            throw new VerificationException("round-trip 修改了输入 fixture。");
        }

        if (HasUtf8Bom(written))
        {
            throw new VerificationException("round-trip 输出包含 UTF-8 BOM。");
        }

        if (!string.Equals(canonical, Encoding.UTF8.GetString(written), StringComparison.Ordinal))
        {
            throw new VerificationException("round-trip 输出不是稳定的 canonical v7 JSON。");
        }

        var summary = new RoundTripSummary(
            1,
            "round-trip",
            true,
            input.SourceSchemaVersion,
            input.Migrated,
            outputPath,
            Convert.ToHexString(SHA256.HashData(written)),
            output.Settings.Countdowns.Count,
            output.Settings.Notes.Count,
            output.Settings.Tags.Count);
        WriteResult(invocation.HasFlag("--json"), summary, $"round-trip 通过：{outputPath}");
        return ExitCodes.Success;
    }

    private static int RunMutexName(CliInvocation invocation)
    {
        invocation.EnsureOnlyOptions("--json", "--settings-path");
        var settingsPath = Path.GetFullPath(invocation.GetRequiredValue("--settings-path"));
        var mutexName = SettingsMutex.GetWriteMutexName(settingsPath);
        var summary = new MutexNameSummary(1, "mutex-name", true, settingsPath, mutexName);
        WriteResult(invocation.HasFlag("--json"), summary, mutexName);
        return ExitCodes.Success;
    }

    private static int RunMutexProbe(CliInvocation invocation)
    {
        invocation.EnsureOnlyOptions("--json", "--settings-path", "--timeout-seconds");
        var settingsPath = Path.GetFullPath(invocation.GetRequiredValue("--settings-path"));
        var timeoutSeconds = invocation.GetRequiredInt32("--timeout-seconds", 1, 60);
        var mutexName = SettingsMutex.GetWriteMutexName(settingsPath);
        try
        {
            using var lease = SettingsMutex.EnterWriteMutex(settingsPath, timeoutSeconds);
            var success = new MutexProbeSummary(
                1,
                "mutex-probe",
                true,
                settingsPath,
                mutexName,
                timeoutSeconds,
                true,
                null);
            WriteResult(invocation.HasFlag("--json"), success, $"互斥锁可获取：{mutexName}");
            return ExitCodes.Success;
        }
        catch (TimeoutException exception)
        {
            var failure = new MutexProbeSummary(
                1,
                "mutex-probe",
                false,
                settingsPath,
                mutexName,
                timeoutSeconds,
                false,
                exception.Message);
            WriteResult(invocation.HasFlag("--json"), failure, exception.Message);
            return ExitCodes.VerificationFailed;
        }
    }

    private static int RunHoldMutex(CliInvocation invocation)
    {
        invocation.EnsureOnlyOptions(
            "--json",
            "--settings-path",
            "--ready-file",
            "--milliseconds");
        var settingsPath = Path.GetFullPath(invocation.GetRequiredValue("--settings-path"));
        var readyFile = Path.GetFullPath(invocation.GetRequiredValue("--ready-file"));
        var milliseconds = invocation.GetRequiredInt32("--milliseconds", 1, 10000);
        if (string.Equals(settingsPath, readyFile, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("--ready-file 不能与设置文件相同。");
        }

        var mutexName = SettingsMutex.GetWriteMutexName(settingsPath);
        try
        {
            using (SettingsMutex.EnterWriteMutex(settingsPath))
            {
                WriteReadyMarker(readyFile, settingsPath, mutexName);
                Thread.Sleep(milliseconds);
            }

            var success = new MutexHoldSummary(
                1,
                "hold-mutex",
                true,
                settingsPath,
                mutexName,
                readyFile,
                milliseconds,
                null);
            WriteResult(invocation.HasFlag("--json"), success, $"互斥锁已持有并释放：{mutexName}");
            return ExitCodes.Success;
        }
        catch (TimeoutException exception)
        {
            var failure = new MutexHoldSummary(
                1,
                "hold-mutex",
                false,
                settingsPath,
                mutexName,
                readyFile,
                milliseconds,
                exception.Message);
            WriteResult(invocation.HasFlag("--json"), failure, exception.Message);
            return ExitCodes.VerificationFailed;
        }
    }

    private static void WriteReadyMarker(
        string readyFile,
        string settingsPath,
        string mutexName)
    {
        var marker = new MutexReadyMarker(
            1,
            "ready",
            Environment.ProcessId,
            settingsPath,
            mutexName);
        using var stream = new FileStream(
            readyFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
        JsonSerializer.Serialize(stream, marker, JsonOptions);
        stream.Flush(flushToDisk: true);
    }

    private static bool HasUtf8Bom(ReadOnlySpan<byte> content)
    {
        return content.Length >= 3
            && content[0] == 0xEF
            && content[1] == 0xBB
            && content[2] == 0xBF;
    }

    private static string InferCommand(string[] args)
    {
        return args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : "verify";
    }

    private static void WriteSummary(bool json, CommandSummary summary)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(summary, JsonOptions));
            return;
        }

        Console.WriteLine(
            summary.Success
                ? $"验证通过：{summary.Passed} 项。"
                : $"验证失败：通过 {summary.Passed} 项，失败 {summary.Failed} 项。");
        foreach (var failure in summary.Failures)
        {
            Console.WriteLine($"- {failure}");
        }
    }

    private static void WriteResult<T>(bool json, T value, string humanReadable)
    {
        Console.WriteLine(json ? JsonSerializer.Serialize(value, JsonOptions) : humanReadable);
    }
}

internal sealed record CommandSummary(
    int ProtocolVersion,
    string Command,
    bool Success,
    int Passed,
    int Failed,
    IReadOnlyList<string> Failures);

internal sealed record RoundTripSummary(
    int ProtocolVersion,
    string Command,
    bool Success,
    int SourceSchemaVersion,
    bool Migrated,
    string OutputPath,
    string Sha256,
    int CountdownCount,
    int NoteCount,
    int TagCount);

internal sealed record MutexNameSummary(
    int ProtocolVersion,
    string Command,
    bool Success,
    string SettingsPath,
    string MutexName);

internal sealed record MutexProbeSummary(
    int ProtocolVersion,
    string Command,
    bool Success,
    string SettingsPath,
    string MutexName,
    int TimeoutSeconds,
    bool Acquired,
    string? Error);

internal sealed record MutexHoldSummary(
    int ProtocolVersion,
    string Command,
    bool Success,
    string SettingsPath,
    string MutexName,
    string ReadyFile,
    int Milliseconds,
    string? Error);

internal sealed record MutexReadyMarker(
    int ProtocolVersion,
    string State,
    int ProcessId,
    string SettingsPath,
    string MutexName);

internal sealed class CliInvocation
{
    private readonly Dictionary<string, string?> _options;

    private CliInvocation(string command, Dictionary<string, string?> options)
    {
        Command = command;
        _options = options;
    }

    public string Command { get; }

    public bool HasFlag(string name)
    {
        return _options.TryGetValue(name, out var value) && value is null;
    }

    public string? GetOptionalValue(string name)
    {
        return _options.TryGetValue(name, out var value) ? value : null;
    }

    public string GetRequiredValue(string name)
    {
        if (!_options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException($"命令 {Command} 缺少必需选项 {name}。");
        }

        return value;
    }

    public int GetRequiredInt32(string name, int minimum, int maximum)
    {
        var raw = GetRequiredValue(name);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < minimum
            || value > maximum)
        {
            throw new CliUsageException(
                $"选项 {name} 必须是 {minimum} 到 {maximum} 之间的整数。");
        }

        return value;
    }

    public void EnsureOnlyOptions(params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        var unknown = _options.Keys.FirstOrDefault(option => !allowedSet.Contains(option));
        if (unknown is not null)
        {
            throw new CliUsageException($"命令 {Command} 不支持选项 {unknown}。");
        }
    }

    public static CliInvocation Parse(string[] args)
    {
        var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : "verify";
        var startIndex = args.Length > 0 && string.Equals(args[0], command, StringComparison.Ordinal)
            ? 1
            : 0;
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (var index = startIndex; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliUsageException($"无法识别参数：{argument}");
            }

            string? value = null;
            if (!string.Equals(argument, "--json", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new CliUsageException($"选项 {argument} 缺少值。");
                }

                value = args[++index];
            }

            if (!options.TryAdd(argument, value))
            {
                throw new CliUsageException($"选项不能重复：{argument}");
            }
        }

        return new CliInvocation(command, options);
    }
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int VerificationFailed = 1;
    public const int UsageError = 2;
    public const int RuntimeError = 3;
}

internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }
}
