using System.Text;

namespace MinminCountdown.Core;

public sealed class SettingsStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly IGuidSource _guidSource;
    private readonly ISettingsFileOperations _files;

    public SettingsStore(
        IGuidSource? guidSource = null,
        IClock? clock = null,
        ISettingsFileOperations? fileOperations = null)
    {
        _guidSource = guidSource ?? SystemGuidSource.Instance;
        Clock = clock ?? SystemClock.Instance;
        _files = fileOperations ?? SystemSettingsFileOperations.Instance;
    }

    public IClock Clock { get; }

    public DateTimeOffset GetCurrentBeijingTime()
    {
        return ChinaStandardTime.GetCurrentBeijingTime(Clock);
    }

    public SettingsLoadResult Load(string settingsPath, DateTimeOffset? migrationUtc = null)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("设置文件路径不能为空。", nameof(settingsPath));
        }

        return SettingsContract.Deserialize(_files.ReadUtf8Text(settingsPath), _guidSource, Clock, migrationUtc);
    }

    public SettingsLoadResult LoadForStartup(
        string settingsPath,
        Func<SettingsRecoveryPrompt, SettingsRecoveryDecision> chooseRecovery,
        bool recoveryAllowed = true,
        DateTimeOffset? migrationUtc = null)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("设置文件路径不能为空。", nameof(settingsPath));
        }

        ArgumentNullException.ThrowIfNull(chooseRecovery);
        SettingsPathState pathState;
        try
        {
            pathState = _files.GetPathState(settingsPath);
        }
        catch (UnauthorizedAccessException)
        {
            // ACL 拒绝（文件或父目录）按“主文件存在但不可读”处理，继续走恢复候选流程。
            pathState = SettingsPathState.Unreadable;
        }

        if (pathState == SettingsPathState.Absent)
        {
            return new SettingsLoadResult(
                CountdownSettings.Empty,
                SettingsSchema.CurrentVersion,
                Migrated: false);
        }

        if (pathState == SettingsPathState.Occupied)
        {
            throw new SettingsRecoveryException(
                "设置路径已被目录、reparse point 或其他非普通文件对象占用，应用将退出且不会以空设置继续。");
        }

        try
        {
            return Load(settingsPath, migrationUtc);
        }
        catch (Exception exception) when (
            exception is SettingsValidationException or IOException or UnauthorizedAccessException)
        {
            if (!recoveryAllowed)
            {
                throw new SettingsRecoveryException(
                    "当前验收阶段尚未授权恢复写入，应用将退出且不会提示或修改设置。",
                    exception);
            }

            var candidate = FindLatestLegalBackup(settingsPath, migrationUtc);
            if (candidate is null)
            {
                throw new SettingsRecoveryUnavailableException(exception);
            }

            var decision = chooseRecovery(new SettingsRecoveryPrompt(
                candidate.Generation,
                candidate.LastWriteTimeUtc));
            if (decision == SettingsRecoveryDecision.Exit)
            {
                throw new SettingsRecoveryDeclinedException();
            }

            if (decision != SettingsRecoveryDecision.RecoverBackup)
            {
                throw new SettingsRecoveryException("设置恢复选择无效，应用将退出且不会写入设置。");
            }

            try
            {
                return RestoreForStartup(settingsPath, candidate, migrationUtc);
            }
            catch (SettingsRecoveryException)
            {
                throw;
            }
            catch (Exception recoveryException) when (
                recoveryException is IOException or UnauthorizedAccessException or TimeoutException)
            {
                throw new SettingsRecoveryException(
                    "备份无法安全替换主设置，应用将退出且不会启动交互宿主。",
                    recoveryException);
            }
        }
    }

    public CountdownSettings Copy(CountdownSettings settings)
    {
        return SettingsContract.Canonicalize(settings);
    }

    public string Serialize(CountdownSettings settings)
    {
        return SettingsContract.SerializeCanonical(settings);
    }

    public void Save(string settingsPath, CountdownSettings settings, int timeoutSeconds = 5)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("设置文件路径不能为空。", nameof(settingsPath));
        }

        // Validate and serialize before acquiring the shared write mutex, matching the existing host.
        var payload = SettingsContract.SerializeCanonical(settings);
        var temporaryPath = settingsPath + ".tmp";
        var backupOnePath = settingsPath + ".backup1";
        var backupTwoPath = settingsPath + ".backup2";
        var backupTwoTemporaryPath = backupTwoPath + ".tmp";
        var backupTwoRollbackPath = backupTwoPath + ".rollback";
        using var mutex = SettingsMutex.EnterWriteMutex(settingsPath, timeoutSeconds);
        if (_files.FileExists(backupTwoRollbackPath))
        {
            throw new IOException(
                "检测到未决 backup 2 rollback artifact，保存已停止以避免覆盖唯一可恢复字节。");
        }

        var backupTwoRotated = false;
        var backupTwoExisted = false;
        var preserveBackupTwoRollback = false;
        try
        {
            if (!_files.FileExists(settingsPath))
            {
                _files.WriteUtf8NoBomText(temporaryPath, payload);
                _files.MoveFile(temporaryPath, settingsPath, overwrite: false);
                return;
            }

            // Preflight history without consuming the caller's domain clock or GUID source.
            ValidateSettingsFile(settingsPath);
            if (IsReadOnlySettingsFile(settingsPath))
            {
                throw new UnauthorizedAccessException("主设置是只读文件，无法替换。");
            }

            _files.WriteUtf8NoBomText(temporaryPath, payload);

            if (TryReadLegalSettings(backupOnePath))
            {
                _files.CopyFile(backupOnePath, backupTwoTemporaryPath, overwrite: true);
                backupTwoExisted = _files.FileExists(backupTwoPath);
                if (backupTwoExisted)
                {
                    _files.ReplaceFile(
                        backupTwoTemporaryPath,
                        backupTwoPath,
                        backupTwoRollbackPath,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    _files.MoveFile(backupTwoTemporaryPath, backupTwoPath, overwrite: false);
                }

                backupTwoRotated = true;
            }

            // The OS replaces the main file and captures its exact prior bytes as backup 1 atomically.
            try
            {
                _files.ReplaceFile(temporaryPath, settingsPath, backupOnePath, ignoreMetadataErrors: true);
            }
            catch (Exception commitException)
            {
                try
                {
                    RestoreBackupTwoAfterFailedCommit(
                        backupTwoPath,
                        backupTwoRollbackPath,
                        backupTwoRotated,
                        backupTwoExisted);
                }
                catch (Exception rollbackException) when (
                    rollbackException is IOException or UnauthorizedAccessException)
                {
                    preserveBackupTwoRollback = true;
                    // 调用方只保留 Message，两步各自的操作和路径都要写进外层消息。
                    throw new IOException(
                        $"主设置替换失败，且 backup 2 无法还原。主设置和应用候选均未提交。提交：{commitException.Message} 还原：{rollbackException.Message}",
                        new AggregateException(commitException, rollbackException));
                }

                throw;
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
            TryDeleteFile(backupTwoTemporaryPath);
            if (!preserveBackupTwoRollback)
            {
                TryDeleteFile(backupTwoRollbackPath);
            }
        }
    }

    private void RestoreBackupTwoAfterFailedCommit(
        string backupTwoPath,
        string backupTwoRollbackPath,
        bool backupTwoRotated,
        bool backupTwoExisted)
    {
        if (!backupTwoRotated)
        {
            return;
        }

        if (backupTwoExisted)
        {
            _files.ReplaceFile(
                backupTwoRollbackPath,
                backupTwoPath,
                null,
                ignoreMetadataErrors: true);
        }
        else if (_files.FileExists(backupTwoPath))
        {
            _files.DeleteFile(backupTwoPath);
        }
    }

    private static bool IsReadOnlySettingsFile(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool TryReadLegalSettings(string path)
    {
        if (!_files.FileExists(path))
        {
            return false;
        }

        try
        {
            ValidateSettingsFile(path);
            return true;
        }
        catch (Exception exception) when (
            exception is SettingsValidationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ValidateSettingsFile(string path)
    {
        SettingsContract.Deserialize(
            _files.ReadUtf8Text(path),
            SystemGuidSource.Instance,
            SystemClock.Instance);
    }

    private RecoveryCandidate? FindLatestLegalBackup(string settingsPath, DateTimeOffset? migrationUtc)
    {
        for (var generation = 1; generation <= 2; generation++)
        {
            var path = settingsPath + $".backup{generation}";
            try
            {
                var bytes = _files.ReadFileBytes(path);
                var loaded = SettingsContract.Deserialize(Utf8NoBom.GetString(bytes), _guidSource, Clock, migrationUtc);
                return new RecoveryCandidate(
                    generation,
                    path,
                    new DateTimeOffset(_files.GetLastWriteTimeUtc(path), TimeSpan.Zero),
                    bytes,
                    loaded);
            }
            catch (Exception exception) when (
                exception is SettingsValidationException or IOException or UnauthorizedAccessException)
            {
                // Each generation is independent; a bad backup 1 must not hide a legal backup 2.
            }
        }

        return null;
    }

    private SettingsLoadResult RestoreForStartup(
        string settingsPath,
        RecoveryCandidate offeredCandidate,
        DateTimeOffset? migrationUtc)
    {
        var temporaryPath = settingsPath + ".tmp";
        var rollbackPath = settingsPath + $".recovery-rollback-{Guid.NewGuid():N}";
        using var mutex = SettingsMutex.EnterWriteMutex(settingsPath, timeoutSeconds: 5);
        var preserveRollback = false;
        try
        {
            if (!_files.FileExists(settingsPath))
            {
                throw new SettingsRecoveryException(
                    "主设置在恢复确认后发生变化，应用将退出且不会以空设置继续。");
            }

            try
            {
                return Load(settingsPath, migrationUtc);
            }
            catch (Exception exception) when (
                exception is SettingsValidationException or IOException or UnauthorizedAccessException)
            {
                // The main file is still damaged; continue with the explicitly approved candidate.
            }

            var currentCandidate = FindLatestLegalBackup(settingsPath, migrationUtc);
            if (currentCandidate is null ||
                currentCandidate.Generation != offeredCandidate.Generation ||
                !currentCandidate.Bytes.AsSpan().SequenceEqual(offeredCandidate.Bytes))
            {
                throw new SettingsRecoveryException(
                    "恢复候选在用户确认后发生变化，应用将退出且不会写入设置。");
            }

            var originalPreserved = PreserveDamagedMainEvidence(settingsPath);
            var canonicalPayload = SettingsContract.SerializeCanonical(currentCandidate.Loaded.Settings);
            _files.WriteUtf8NoBomText(temporaryPath, canonicalPayload);
            _files.ReplaceFile(temporaryPath, settingsPath, rollbackPath, ignoreMetadataErrors: true);

            try
            {
                var reloaded = Load(settingsPath, migrationUtc);
                TryDeleteFile(rollbackPath);
                return reloaded with
                {
                    Recovery = originalPreserved
                        ? SettingsRecoveryOutcome.RestoredWithOriginalPreserved
                        : SettingsRecoveryOutcome.RestoredWithoutOriginal
                };
            }
            catch (Exception readbackException) when (
                readbackException is SettingsValidationException or IOException or UnauthorizedAccessException)
            {
                try
                {
                    _files.ReplaceFile(rollbackPath, settingsPath, null, ignoreMetadataErrors: true);
                }
                catch (Exception rollbackException) when (
                    rollbackException is IOException or UnauthorizedAccessException)
                {
                    preserveRollback = true;
                    throw new SettingsRecoveryException(
                        "恢复后的主设置未能通过回读校验，且原主文件无法安全还原；应用将退出。原始证据已保留。",
                        new AggregateException(readbackException, rollbackException));
                }

                throw new SettingsRecoveryException(
                    "恢复后的主设置未能通过回读校验，已还原原主文件；应用将退出。",
                    readbackException);
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
            if (!preserveRollback)
            {
                TryDeleteFile(rollbackPath);
            }
        }
    }

    private bool PreserveDamagedMainEvidence(string settingsPath)
    {
        byte[] bytes;
        try
        {
            bytes = _files.ReadFileBytes(settingsPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var parentDirectory = Path.GetDirectoryName(Path.GetFullPath(settingsPath))
            ?? throw new SettingsRecoveryException("设置路径缺少父目录，无法保留恢复证据。");
        var evidenceDirectory = Path.Combine(parentDirectory, "settings-recovery-evidence");
        _files.CreateDirectory(evidenceDirectory);
        var timestamp = Clock.UtcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfffffff'Z'");
        var evidencePath = Path.Combine(
            evidenceDirectory,
            $"settings.corrupt.{timestamp}.{Guid.NewGuid():N}.json");
        _files.WriteNewFileAndFlush(evidencePath, bytes);
        return true;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (_files.FileExists(path))
            {
                _files.DeleteFile(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A committed main file remains authoritative; a cleanup artifact is safer than rollback.
        }
    }

    private sealed record RecoveryCandidate(
        int Generation,
        string Path,
        DateTimeOffset LastWriteTimeUtc,
        byte[] Bytes,
        SettingsLoadResult Loaded);
}
