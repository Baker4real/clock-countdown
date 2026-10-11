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
        File.WriteAllText(path, content, Utf8NoBom);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void ReplaceFile(
        string sourcePath,
        string destinationPath,
        string? backupPath,
        bool ignoreMetadataErrors) =>
        SettingsFileReplace.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);

    public void DeleteFile(string path) => File.Delete(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteNewFileAndFlush(string path, ReadOnlySpan<byte> content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }
}

public static class SettingsFileReplace
{
    // 本机 File.Replace 在回滚或杀毒扫描时会偶发失败，留下已轮换的 backup。
    // 短暂重试让失败替换仍能把原字节放回去。
    public static void Replace(
        string sourcePath,
        string destinationPath,
        string? backupPath,
        bool ignoreMetadataErrors)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);
                return;
            }
            catch (Exception exception) when (
                attempt < attempts &&
                exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }
}
