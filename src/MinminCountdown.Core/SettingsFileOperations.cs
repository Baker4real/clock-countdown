using System.Diagnostics;
using System.Text;

namespace MinminCountdown.Core;

public enum SettingsPathState
{
    Absent,
    RegularFile,
    Occupied,
    Unreadable
}

public interface ISettingsFileOperations
{
    SettingsPathState GetPathState(string path);

    bool FileExists(string path);

    string ReadUtf8Text(string path);

    byte[] ReadFileBytes(string path);

    DateTime GetLastWriteTimeUtc(string path);

    void WriteUtf8NoBomText(string path, string content);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    void ReplaceFile(
        string sourcePath,
        string destinationPath,
        string? backupPath,
        bool ignoreMetadataErrors);

    void DeleteFile(string path);

    void CreateDirectory(string path);

    void WriteNewFileAndFlush(string path, ReadOnlySpan<byte> content);
}

internal sealed class SystemSettingsFileOperations : ISettingsFileOperations
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static SystemSettingsFileOperations Instance { get; } = new();

    private SystemSettingsFileOperations()
    {
    }

    public SettingsPathState GetPathState(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0
                ? SettingsPathState.Occupied
                : SettingsPathState.RegularFile;
        }
        catch (FileNotFoundException)
        {
            return SettingsPathState.Absent;
        }
        catch (DirectoryNotFoundException)
        {
            return SettingsPathState.Absent;
        }
        catch (UnauthorizedAccessException)
        {
            // ACL 拒绝（文件或父目录）按“主文件存在但不可读”处理，交由启动恢复流程。
            return SettingsPathState.Unreadable;
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public string ReadUtf8Text(string path) => File.ReadAllText(path, Utf8NoBom);

    public byte[] ReadFileBytes(string path) => File.ReadAllBytes(path);

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public void WriteUtf8NoBomText(string path, string content) =>
        SettingsFileRetry.Run("写入", path, () => File.WriteAllText(path, content, Utf8NoBom));

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        SettingsFileRetry.Run(
            "复制",
            $"{sourcePath} → {destinationPath}",
            () => File.Copy(sourcePath, destinationPath, overwrite));

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        SettingsFileReplace.Move(sourcePath, destinationPath, overwrite);

    public void ReplaceFile(
        string sourcePath,
        string destinationPath,
        string? backupPath,
        bool ignoreMetadataErrors) =>
        SettingsFileReplace.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);

    public void DeleteFile(string path) =>
        SettingsFileRetry.Run("删除", path, () => File.Delete(path));

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    // 证据文件用全新的唯一文件名独占创建，扫描无法提前持有它；CreateNew 重试反而会撞上自己留下的半截文件。
    public void WriteNewFileAndFlush(string path, ReadOnlySpan<byte> content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }
}

public static class SettingsFileReplace
{
    public static void Replace(
        string sourcePath,
        string destinationPath,
        string? backupPath,
        bool ignoreMetadataErrors) =>
        SettingsFileRetry.Run(
            "替换",
            backupPath is null
                ? $"{sourcePath} → {destinationPath}"
                : $"{sourcePath} → {destinationPath}，原文件转存为 {backupPath}",
            () => File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors));

    public static void Move(string sourcePath, string destinationPath, bool overwrite) =>
        SettingsFileRetry.Run(
            "改名",
            $"{sourcePath} → {destinationPath}",
            () => File.Move(sourcePath, destinationPath, overwrite));
}

internal static class SettingsFileRetry
{
    // 实时扫描会短暂持有刚写入的文件且不共享删除：改名、替换和删除报共享冲突；替换时被扫描的旧备份
    // 无法被覆盖，报 ERROR_UNABLE_TO_REMOVE_REPLACED。两种失败都不改动任何文件名，可以原样重试。
    // 构建后扫描排队时占用会超过 200 ms，所以退避预算约 2 秒；只读、目录占位等确定性失败不是占用，
    // 立即抛出，不在界面线程上空等。放弃时把操作和路径写进消息：系统给 File.Replace / File.Move 的
    // 共享冲突消息不带路径，调用方又只保留 Message。消息只含路径，不含设置内容。
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int UnableToRemoveReplaced = unchecked((int)0x80070497);
    private static readonly TimeSpan TransientLockBudget = TimeSpan.FromSeconds(2);

    public static void Run(string operation, string target, Action action)
    {
        var elapsed = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException exception) when (
                (exception.HResult is SharingViolation or LockViolation or UnableToRemoveReplaced) &&
                elapsed.Elapsed < TransientLockBudget)
            {
                Thread.Sleep(Math.Min(20 * attempt, 200));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var message =
                    $"设置文件{operation}失败：{target}（尝试 {attempt} 次，历时 {elapsed.ElapsedMilliseconds} ms）。{exception.Message}";
                Exception failure = exception is UnauthorizedAccessException
                    ? new UnauthorizedAccessException(message, exception)
                    : new IOException(message, exception);
                failure.HResult = exception.HResult;
                throw failure;
            }
        }
    }
}
