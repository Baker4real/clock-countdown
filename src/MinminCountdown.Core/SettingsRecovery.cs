namespace MinminCountdown.Core;

public enum SettingsRecoveryDecision
{
    RecoverBackup,
    Exit
}

public sealed record SettingsRecoveryPrompt(
    int BackupGeneration,
    DateTimeOffset BackupLastWriteTimeUtc);

public class SettingsRecoveryException : IOException
{
    public SettingsRecoveryException(string message)
        : base(message)
    {
    }

    public SettingsRecoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class SettingsRecoveryDeclinedException : SettingsRecoveryException
{
    public SettingsRecoveryDeclinedException()
        : base("设置恢复已取消，应用将退出且不会写入设置。")
    {
    }
}

public sealed class SettingsRecoveryUnavailableException : SettingsRecoveryException
{
    public SettingsRecoveryUnavailableException(Exception innerException)
        : base("主设置无法读取或未通过校验，并且没有可安全恢复的合法备份。应用将退出且不会写入设置。", innerException)
    {
    }
}
