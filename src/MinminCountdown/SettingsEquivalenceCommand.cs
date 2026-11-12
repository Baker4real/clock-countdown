using System.Globalization;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static class SettingsEquivalenceCommand
{
    private const string Mode = "--settings-equivalence";

    public static bool IsRequested(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && string.Equals(arguments[0], Mode, StringComparison.Ordinal);

    public static int Run(IReadOnlyList<string> arguments)
    {
        try
        {
            var invocation = Parse(arguments);
            var before = ReadInput(invocation.BeforePath, "部署前设置");
            var after = ReadInput(invocation.AfterPath, "部署后设置");
            var result = SettingsDeploymentEquivalence.Evaluate(
                before,
                after,
                invocation.MigrationUtc);
            var message = result.ReasonCategory switch
            {
                SettingsDeploymentEquivalence.UnchangedReason => "设置字节未变化。",
                SettingsDeploymentEquivalence.MigratedReason => "部署后设置是部署前设置的严格等价迁移。",
                SettingsDeploymentEquivalence.UnsupportedSchemaReason => "只允许 schema v3-v9 到 v10 的部署迁移。",
                SettingsDeploymentEquivalence.InvalidSettingsReason => "输入设置损坏或不满足设置契约。",
                SettingsDeploymentEquivalence.DataMismatchReason => "部署后设置超出允许的数据等价范围。",
                _ => "设置等价判定返回未知原因。"
            };
            Write(result.Equivalent, result.ReasonCategory, message);
            return result.Equivalent ? 0 : 1;
        }
        catch (SettingsEquivalenceUsageException exception)
        {
            Write(false, "usage", exception.Message);
            return 2;
        }
        catch (SettingsEquivalenceInputException exception)
        {
            Write(false, "invalid-input", exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Write(false, "invalid-input", $"无法读取设置输入：{exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Write(false, "runtime", $"设置等价命令执行失败：{exception.Message}");
            return 3;
        }
    }

    private static SettingsEquivalenceInvocation Parse(IReadOnlyList<string> arguments)
    {
        if (!IsRequested(arguments))
        {
            throw new SettingsEquivalenceUsageException($"设置等价命令必须以 {Mode} 开始。");
        }

        string? beforePath = null;
        string? afterPath = null;
        string? migrationUtcText = null;
        for (var index = 1; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (option is not "--before-settings" and not "--after-settings" and not "--migration-utc")
            {
                throw new SettingsEquivalenceUsageException($"不支持的设置等价参数：{option}");
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                throw new SettingsEquivalenceUsageException($"{option} 缺少值。");
            }

            var value = arguments[index];
            switch (option)
            {
                case "--before-settings" when beforePath is null:
                    beforePath = value;
                    break;
                case "--after-settings" when afterPath is null:
                    afterPath = value;
                    break;
                case "--migration-utc" when migrationUtcText is null:
                    migrationUtcText = value;
                    break;
                default:
                    throw new SettingsEquivalenceUsageException($"设置等价参数不能重复：{option}");
            }
        }

        if (beforePath is null || afterPath is null || migrationUtcText is null)
        {
            throw new SettingsEquivalenceUsageException(
                "必须提供 --before-settings、--after-settings 和 --migration-utc。");
        }

        if (!DateTimeOffset.TryParse(
                migrationUtcText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var migrationUtc))
        {
            throw new SettingsEquivalenceUsageException("--migration-utc 必须是带时区的有效时间。");
        }

        try
        {
            return new SettingsEquivalenceInvocation(
                Path.GetFullPath(beforePath),
                Path.GetFullPath(afterPath),
                migrationUtc.ToUniversalTime());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SettingsEquivalenceUsageException($"设置路径无效：{exception.Message}");
        }
    }

    private static byte[] ReadInput(string path, string subject)
    {
        if (!File.Exists(path))
        {
            throw new SettingsEquivalenceInputException($"{subject}文件不存在：{path}");
        }

        return File.ReadAllBytes(path);
    }

    private static void Write(bool equivalent, string reasonCategory, string message)
    {
        CliOutput.WriteJson(new
        {
            schemaVersion = 1,
            equivalent,
            reasonCategory,
            message
        });
    }

    private sealed record SettingsEquivalenceInvocation(
        string BeforePath,
        string AfterPath,
        DateTimeOffset MigrationUtc);

    private sealed class SettingsEquivalenceUsageException(string message) : Exception(message);

    private sealed class SettingsEquivalenceInputException(string message) : Exception(message);
}
