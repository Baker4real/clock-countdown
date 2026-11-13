using System.Globalization;
using MinminCountdown.Core;

namespace MinminCountdown;

// #278：安装器换程序前用候选 EXE 在设置副本上演练迁移。命令只读写调用方给出的设置文件及其同目录备份，
// 加载、迁移与备份轮转走正常启动的同一份设置代码；损坏或缺失的输入只报告，不读备份、不恢复。
internal static class SettingsMigrationCommand
{
    private const string Mode = "--settings-migration";
    private const string SettingsFileOption = "--settings-file";

    public static bool IsRequested(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && string.Equals(arguments[0], Mode, StringComparison.Ordinal);

    public static int Run(IReadOnlyList<string> arguments)
    {
        try
        {
            var settingsPath = ParseSettingsPath(arguments);
            if (!File.Exists(settingsPath))
            {
                return Fail(1, "invalid-input", $"设置文件不存在：{settingsPath}");
            }

            // 报告的时刻就是迁移实际使用的时刻，安装器把它原样交给 --settings-equivalence。
            var migrationUtc = SystemClock.Instance.UtcNow.ToUniversalTime();
            var store = new SettingsStore();
            SettingsLoadResult loaded;
            try
            {
                loaded = store.Load(settingsPath, migrationUtc);
            }
            catch (SettingsValidationException)
            {
                return Fail(1, "invalid-settings", "设置文件损坏或不满足设置契约，未写入任何文件。");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Fail(1, "invalid-input", $"无法读取设置文件：{exception.Message}");
            }

            if (!loaded.Migrated)
            {
                return Succeed(
                    migrated: false,
                    migrationUtc,
                    "current-schema",
                    $"设置已是当前 schema v{SettingsSchema.CurrentVersion}，未写入任何文件。");
            }

            try
            {
                store.Save(settingsPath, loaded.Settings);
            }
            catch (Exception exception) when (
                exception is IOException or
                    UnauthorizedAccessException or
                    TimeoutException or
                    SettingsValidationException)
            {
                return Fail(1, "write-failed", $"迁移结果未能保存：{exception.Message}");
            }

            return Succeed(
                migrated: true,
                migrationUtc,
                "migrated",
                $"设置已从 schema v{loaded.SourceSchemaVersion} 迁移到 v{SettingsSchema.CurrentVersion} 并保存，迁移前的文件成为 backup 1。");
        }
        catch (SettingsMigrationUsageException exception)
        {
            return Fail(2, "usage", exception.Message);
        }
        catch (Exception exception)
        {
            return Fail(3, "runtime", $"设置迁移命令执行失败：{exception.Message}");
        }
    }

    private static string ParseSettingsPath(IReadOnlyList<string> arguments)
    {
        if (!IsRequested(arguments))
        {
            throw new SettingsMigrationUsageException($"设置迁移命令必须以 {Mode} 开始。");
        }

        string? settingsPath = null;
        for (var index = 1; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (!string.Equals(option, SettingsFileOption, StringComparison.Ordinal))
            {
                throw new SettingsMigrationUsageException($"不支持的设置迁移参数：{option}");
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                throw new SettingsMigrationUsageException($"{SettingsFileOption} 缺少值。");
            }

            if (settingsPath is not null)
            {
                throw new SettingsMigrationUsageException($"设置迁移参数不能重复：{SettingsFileOption}");
            }

            settingsPath = arguments[index];
        }

        if (settingsPath is null)
        {
            throw new SettingsMigrationUsageException($"必须提供 {SettingsFileOption}。");
        }

        try
        {
            return Path.GetFullPath(settingsPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SettingsMigrationUsageException($"设置路径无效：{exception.Message}");
        }
    }

    private static int Succeed(bool migrated, DateTimeOffset migrationUtc, string reasonCategory, string message)
    {
        Write(true, migrated, migrationUtc.ToString("o", CultureInfo.InvariantCulture), reasonCategory, message);
        return 0;
    }

    private static int Fail(int exitCode, string reasonCategory, string message)
    {
        Write(false, false, null, reasonCategory, message);
        return exitCode;
    }

    private static void Write(
        bool succeeded,
        bool migrated,
        string? migrationUtc,
        string reasonCategory,
        string message)
    {
        CliOutput.WriteJson(new
        {
            schemaVersion = 1,
            succeeded,
            migrated,
            migrationUtc,
            reasonCategory,
            message
        });
    }

    private sealed class SettingsMigrationUsageException(string message) : Exception(message);
}
