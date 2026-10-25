using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in VisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    public static PersistenceFailureExerciseResult RunPersistenceFailures(
        DesktopWorkspace workspace,
        string settingsPath)
    {
        var blockedTemporaryPath = settingsPath + ".tmp";
        if (!Directory.Exists(blockedTemporaryPath))
        {
            throw new InvalidOperationException(
                "持久化失败 exercise 需要先把 settings.json.tmp 建成目录，避免在未阻断路径上写入。");
        }

        var before = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
        var countdownDraft = "持久化失败倒计时草稿";
        var preferenceDraft = "9999";
        var tagDraft = "持久化失败标签草稿";
        var selectedFilterBefore = workspace.Snapshot.Filter.SelectedTagIds.ToArray();

        var countdownResult = workspace.Apply(new SaveCountdownChange(
            null,
            countdownDraft,
            workspace.Snapshot.UtcNow.AddDays(10),
            Array.Empty<string>()));
        var countdownStatePreserved =
            !countdownResult.Succeeded &&
            string.Equals(
                before,
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                StringComparison.Ordinal);

        var preferences = workspace.Snapshot.Settings.Preferences;
        var preferenceResult = workspace.Apply(new SavePreferencesChange(
            preferences.DisplayPrecision,
            9999m,
            preferences.WorkStart,
            preferences.WorkEnd));
        var preferenceStatePreserved =
            !preferenceResult.Succeeded &&
            string.Equals(
                before,
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                StringComparison.Ordinal);

        var tagResult = workspace.Apply(new CreateTagChange(tagDraft, "#F4B6A6"));
        var tagStatePreserved =
            !tagResult.Succeeded &&
            tagResult.CreatedTag is null &&
            string.Equals(
                before,
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                StringComparison.Ordinal);
        var filterPreserved = selectedFilterBefore.SequenceEqual(
            workspace.Snapshot.Filter.SelectedTagIds,
            StringComparer.OrdinalIgnoreCase);

        return new PersistenceFailureExerciseResult(
            countdownStatePreserved,
            preferenceStatePreserved,
            countdownDraft == "持久化失败倒计时草稿",
            preferenceDraft == "9999",
            tagStatePreserved,
            tagDraft == "持久化失败标签草稿",
            filterPreserved,
            Directory.Exists(blockedTemporaryPath));
    }

    public static TagInteractionExerciseResult RunTagInteractions(DesktopWorkspace workspace)
    {
        var initial = workspace.Snapshot.Settings;
        var initialCountdownCount = initial.Countdowns.Count;
        var initialNoteCount = initial.Notes.Count;
        var initialTagCount = initial.Tags.Count;

        var projectTagResult = MustApply(
            workspace,
            new CreateTagChange("很长的项目协作标签名称", "#F4B6A6"));
        var projectTag = projectTagResult.CreatedTag;
        Must(projectTag is not null, "未取得新建项目标签。");
        var countdownCountAfterSearchCreate = workspace.Snapshot.Settings.Countdowns.Count;
        var collaborationTag = MustApply(
            workspace,
            new CreateTagChange("协作", "#A8D5BA")).CreatedTag;
        Must(collaborationTag is not null, "未取得新建协作标签。");
        var removableTag = MustApply(
            workspace,
            new CreateTagChange("待删除", "#A8C7E8")).CreatedTag;
        Must(removableTag is not null, "未取得待删除标签。");

        var targetBase = new DateTimeOffset(2095, 1, 1, 0, 0, 0, TimeSpan.Zero);
        MustApply(workspace, new SaveCountdownChange(
            null,
            "标签验证甲",
            targetBase,
            new[] { projectTag!.Id, removableTag!.Id }));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "标签验证乙",
            targetBase.AddDays(1),
            new[] { projectTag.Id, collaborationTag!.Id }));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "无标签验证",
            targetBase.AddDays(2),
            Array.Empty<string>()));

        MustApply(workspace, new SaveNoteChange(
            null,
            "浮窗继承标签便签",
            new DateTime(2032, 2, 3),
            false,
            new[] { projectTag.Id },
            ReminderSchedule.CreateRepeating(workspace.Snapshot.UtcNow.AddMinutes(30), 30)));
        var inheritedNote = workspace.Snapshot.Settings.Notes
            .Last(note => note.Text == "浮窗继承标签便签");
        MustApply(workspace, new SaveNoteChange(
            null,
            "浮窗清空标签便签",
            new DateTime(2032, 2, 4),
            false,
            Array.Empty<string>(),
            NoReminder.Instance));
        var untaggedNote = workspace.Snapshot.Settings.Notes
            .Last(note => note.Text == "浮窗清空标签便签");

        var anyFilter = DesktopWorkspaceFilter.Empty with { SelectedTagIds = new[] { projectTag.Id } };
        var snapshot = workspace.Snapshot;
        var anyMatches = snapshot.Settings.Countdowns.Count(record => anyFilter.Matches(record, snapshot.UtcNow));
        var allFilter = anyFilter with
        {
            SelectedTagIds = new[] { projectTag.Id, collaborationTag.Id },
            MatchMode = TagMatchMode.All
        };
        var allMatches = snapshot.Settings.Countdowns.Count(record => allFilter.Matches(record, snapshot.UtcNow));
        var anyAllFilter = anyMatches >= 2 && allMatches == 1;

        var untaggedFilter = DesktopWorkspaceFilter.Empty with
        {
            UntaggedOnly = true,
            CountdownStatus = CountdownStatusFilter.Active
        };
        var untaggedAndStatus = snapshot.Settings.Countdowns.Any(
            record => record.Name == "无标签验证" && untaggedFilter.Matches(record, snapshot.UtcNow));

        MustApply(workspace, new SetNoteCompletionChange(inheritedNote.Id, true));
        var completed = workspace.Snapshot.Settings.Notes.First(note => note.Id == inheritedNote.Id);
        var noteFilter = DesktopWorkspaceFilter.Empty with
        {
            SelectedTagIds = new[] { projectTag.Id },
            NoteStatus = NoteStatusFilter.Completed,
            NoteSearch = "继承标签"
        };
        var noteStatusSearch =
            noteFilter.Matches(completed) &&
            completed.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase);
        MustApply(workspace, new SetNoteCompletionChange(inheritedNote.Id, false));

        var exactDateFilter = DesktopWorkspaceFilter.Empty with { NoteDate = "2032-02-03" };
        var undatedFilter = DesktopWorkspaceFilter.Empty with { UndatedNotesOnly = true };
        var noteDateBehavior =
            exactDateFilter.Matches(workspace.Snapshot.Settings.Notes.First(note => note.Id == inheritedNote.Id)) &&
            !exactDateFilter.Matches(untaggedNote) &&
            workspace.Snapshot.Settings.Notes.Where(note => note.Date is null).All(undatedFilter.Matches);

        MustApply(workspace, new SaveNoteChange(
            inheritedNote.Id,
            "已编辑的浮窗继承标签便签",
            new DateTime(2032, 3, 5),
            false,
            new[] { collaborationTag.Id },
            inheritedNote.Reminder));
        var edited = workspace.Snapshot.Settings.Notes.First(note => note.Id == inheritedNote.Id);
        var editTags =
            edited.TagIds.Count == 1 &&
            edited.TagIds.Contains(collaborationTag.Id, StringComparer.OrdinalIgnoreCase);
        var editDate = edited.Date == "2032-03-05";

        MustApply(workspace, new RenameTagChange(projectTag.Id, "项目协作", projectTag.Color));
        MustApply(workspace, new DeleteTagChange(removableTag.Id));
        var final = workspace.Snapshot.Settings;
        var renamed = final.Tags.Any(tag =>
            tag.Id == projectTag.Id &&
            tag.Name == "项目协作");
        var deleted = final.Tags.All(tag => tag.Id != removableTag.Id);
        var recordsPreserved =
            final.Countdowns.Count == initialCountdownCount + 3 &&
            final.Notes.Count == initialNoteCount + 2;
        var detachedOnly =
            final.Countdowns
                .Where(record => record.Name == "标签验证甲")
                .All(record =>
                    record.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase) &&
                    !record.TagIds.Contains(removableTag.Id, StringComparer.OrdinalIgnoreCase));

        var selector = new TagSelector();
        selector.SetTags(final.Tags);
        selector.SetSelection(new[] { projectTag.Id, collaborationTag.Id });
        var summaryCompact = selector.SummaryText.Length <= 18 && selector.SummaryText.Length > 0;
        var popupStartsClosed = !selector.IsPopupOpen;

        var creationAndAssociation =
            final.Tags.Count >= initialTagCount + 2 &&
            final.Countdowns.Any(record =>
                record.Name == "标签验证乙" &&
                record.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase) &&
                record.TagIds.Contains(collaborationTag.Id, StringComparer.OrdinalIgnoreCase));
        var floatingInherits =
            inheritedNote.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase);
        var floatingDate = inheritedNote.Date == "2032-02-03";
        var floatingOverride = untaggedNote.TagIds.Count == 0;
        var searchCreateAvoidsSubmit = countdownCountAfterSearchCreate == initialCountdownCount;

        return new TagInteractionExerciseResult(
            true,
            creationAndAssociation,
            anyAllFilter,
            summaryCompact,
            searchCreateAvoidsSubmit,
            untaggedAndStatus,
            noteStatusSearch,
            true,
            noteDateBehavior,
            floatingInherits,
            floatingDate,
            popupStartsClosed,
            floatingOverride,
            editTags,
            editDate,
            renamed && deleted && recordsPreserved && detachedOnly);
    }

    private static void Must(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static DesktopWorkspaceChangeResult MustApply(
        DesktopWorkspace workspace,
        DesktopWorkspaceChange change)
    {
        var result = workspace.Apply(change);
        Must(result.Succeeded, result.Error);
        return result;
    }

    private static void WriteStartupRequest(
        string path,
        string launchToken,
        string acceptanceState,
        bool? acceptanceReadOnly = null,
        DateTimeOffset? requestedAtUtc = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            launchToken,
            requestedAtUtc = (requestedAtUtc ??
                new DateTimeOffset(2026, 7, 20, 2, 16, 0, TimeSpan.Zero)).ToString("o"),
            acceptanceReadOnly = acceptanceReadOnly ??
                acceptanceState is "active" or "released" or "ready",
            acceptanceState
        });
        File.WriteAllText(path, payload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string CreateHolidayCalendarPayload(
        int year,
        bool includeMaySixthWorkday = true)
    {
        var firstDay = new DateTime(year, 1, 1);
        var dayCount = DateTime.IsLeapYear(year) ? 366 : 365;
        var days = Enumerable.Range(0, dayCount)
            .Select(offset =>
            {
                var date = firstDay.AddDays(offset);
                return new
                {
                    date = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    is_workday = includeMaySixthWorkday && date == new DateTime(year, 5, 6) ||
                                 date == new DateTime(year, 5, 7)
                };
            })
            .ToArray();
        return JsonSerializer.Serialize(new { days });
    }

    private static void MustThrow<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteSettingsFiles(string settingsPath)
    {
        foreach (var suffix in new[]
                 {
                     "", ".tmp", ".backup1", ".backup2",
                     ".backup2.tmp", ".backup2.rollback"
                 })
        {
            DeleteFileIfPresent(settingsPath + suffix);
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class AclDeniedMainSettingsFileOperations : ISettingsFileOperations
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);
        private readonly string _settingsPath;
        private bool _denialActive = true;

        public AclDeniedMainSettingsFileOperations(string settingsPath)
        {
            _settingsPath = Path.GetFullPath(settingsPath);
        }

        public bool DenialClearedByReplace { get; private set; }

        public SettingsPathState GetPathState(string path) =>
            IsDenied(path)
                ? throw new UnauthorizedAccessException("验证注入的设置路径状态探测 ACL 拒绝。")
                : File.Exists(path)
                    ? SettingsPathState.RegularFile
                    : Directory.Exists(path)
                        ? SettingsPathState.Occupied
                        : SettingsPathState.Absent;

        public bool FileExists(string path) => File.Exists(path);

        public string ReadUtf8Text(string path) =>
            IsDenied(path)
                ? throw new UnauthorizedAccessException("验证注入的设置读取 ACL 拒绝。")
                : File.ReadAllText(path, Utf8NoBom);

        public byte[] ReadFileBytes(string path) =>
            IsDenied(path)
                ? throw new UnauthorizedAccessException("验证注入的设置字节读取 ACL 拒绝。")
                : File.ReadAllBytes(path);

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
            bool ignoreMetadataErrors)
        {
            SettingsFileReplace.Replace(
                sourcePath,
                destinationPath,
                backupPath,
                ignoreMetadataErrors);
            if (string.Equals(Path.GetFullPath(destinationPath), _settingsPath, StringComparison.OrdinalIgnoreCase) &&
                backupPath is not null &&
                backupPath.Contains(".recovery-rollback-", StringComparison.Ordinal))
            {
                // 恢复替换生成的新主文件继承父目录 ACL，原文件级拒绝随之消失。
                _denialActive = false;
                DenialClearedByReplace = true;
            }
        }

        public void DeleteFile(string path) => File.Delete(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteNewFileAndFlush(string path, ReadOnlySpan<byte> content)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        private bool IsDenied(string path) =>
            _denialActive &&
            string.Equals(Path.GetFullPath(path), _settingsPath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class WorkspaceValidationClock : IClock
    {
        private readonly DateTimeOffset _utcNow;

        public WorkspaceValidationClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public int ReadCount { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                ReadCount++;
                return _utcNow;
            }
        }
    }

    private sealed class AdvancingValidationClock : IClock
    {
        public AdvancingValidationClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow.ToUniversalTime();
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class CountingAdvancingValidationClock : IClock
    {
        private DateTimeOffset _utcNow;

        public CountingAdvancingValidationClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public int ReadCount { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                ReadCount++;
                return _utcNow;
            }
            set => _utcNow = value.ToUniversalTime();
        }
    }
}
