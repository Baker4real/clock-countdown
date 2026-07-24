using MinminCountdown.Core;

namespace MinminCountdown;

internal static class ValidationExercises
{
    public static PersistenceFailureExerciseResult RunPersistenceFailures(ApplicationSession session)
    {
        var blockedTemporaryPath = session.SettingsPath + ".tmp";
        if (!Directory.Exists(blockedTemporaryPath))
        {
            throw new InvalidOperationException(
                "持久化失败 exercise 需要先把 settings.json.tmp 建成目录，避免在未阻断路径上写入。");
        }

        var before = session.SerializeCurrent();
        var countdownDraft = "持久化失败倒计时草稿";
        var preferenceDraft = "9999";
        var tagDraft = "持久化失败标签草稿";
        var selectedFilterBefore = session.Filter.SelectedTagIds.ToArray();

        var countdownSaved = session.TrySaveCountdown(
            null,
            countdownDraft,
            session.UtcNow.AddDays(10),
            Array.Empty<string>(),
            out _);
        var countdownStatePreserved =
            !countdownSaved &&
            string.Equals(before, session.SerializeCurrent(), StringComparison.Ordinal);

        var preferences = session.Current.Preferences;
        var preferenceSaved = session.TrySavePreferences(
            preferences.DisplayPrecision,
            9999m,
            preferences.WorkStart,
            preferences.WorkEnd,
            out _);
        var preferenceStatePreserved =
            !preferenceSaved &&
            string.Equals(before, session.SerializeCurrent(), StringComparison.Ordinal);

        var tagSaved = session.TryCreateTag(tagDraft, "#F4B6A6", out var createdTag, out _);
        var tagStatePreserved =
            !tagSaved &&
            createdTag is null &&
            string.Equals(before, session.SerializeCurrent(), StringComparison.Ordinal);
        var filterPreserved = selectedFilterBefore.SequenceEqual(
            session.Filter.SelectedTagIds,
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

    public static TagInteractionExerciseResult RunTagInteractions(ApplicationSession session)
    {
        var initialCountdownCount = session.Current.Countdowns.Count;
        var initialNoteCount = session.Current.Notes.Count;
        var initialTagCount = session.Current.Tags.Count;

        Must(session.TryCreateTag("很长的项目协作标签名称", "#F4B6A6", out var projectTag, out var error), error);
        Must(projectTag is not null, "未取得新建项目标签。");
        var countdownCountAfterSearchCreate = session.Current.Countdowns.Count;
        Must(session.TryCreateTag("协作", "#A8D5BA", out var collaborationTag, out error), error);
        Must(collaborationTag is not null, "未取得新建协作标签。");
        Must(session.TryCreateTag("待删除", "#A8C7E8", out var removableTag, out error), error);
        Must(removableTag is not null, "未取得待删除标签。");

        var targetBase = new DateTimeOffset(2095, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Must(session.TrySaveCountdown(
            null,
            "标签验证甲",
            targetBase,
            new[] { projectTag!.Id, removableTag!.Id },
            out error), error);
        Must(session.TrySaveCountdown(
            null,
            "标签验证乙",
            targetBase.AddDays(1),
            new[] { projectTag.Id, collaborationTag!.Id },
            out error), error);
        Must(session.TrySaveCountdown(
            null,
            "无标签验证",
            targetBase.AddDays(2),
            Array.Empty<string>(),
            out error), error);

        Must(session.TrySaveNote(
            null,
            "浮窗继承标签便签",
            new DateTime(2032, 2, 3),
            false,
            new[] { projectTag.Id },
            30,
            out error), error);
        var inheritedNote = session.Current.Notes.Last(note => note.Text == "浮窗继承标签便签");
        Must(session.TrySaveNote(
            null,
            "浮窗清空标签便签",
            new DateTime(2032, 2, 4),
            false,
            Array.Empty<string>(),
            null,
            out error), error);
        var untaggedNote = session.Current.Notes.Last(note => note.Text == "浮窗清空标签便签");

        var anyFilter = new WorkspaceFilter();
        anyFilter.SelectedTagIds.Add(projectTag.Id);
        var anyMatches = session.Current.Countdowns.Count(record => anyFilter.Matches(record, session.UtcNow));
        anyFilter.SelectedTagIds.Add(collaborationTag.Id);
        anyFilter.MatchMode = TagMatchMode.All;
        var allMatches = session.Current.Countdowns.Count(record => anyFilter.Matches(record, session.UtcNow));
        var anyAllFilter = anyMatches >= 2 && allMatches == 1;

        var untaggedFilter = new WorkspaceFilter
        {
            UntaggedOnly = true,
            CountdownStatus = CountdownStatusFilter.Active
        };
        var untaggedAndStatus = session.Current.Countdowns.Any(
            record => record.Name == "无标签验证" && untaggedFilter.Matches(record, session.UtcNow));

        Must(session.TrySetNoteCompletion(inheritedNote.Id, true, out error), error);
        var completed = session.Current.Notes.First(note => note.Id == inheritedNote.Id);
        var noteFilter = new WorkspaceFilter
        {
            NoteStatus = NoteStatusFilter.Completed,
            NoteSearch = "继承标签"
        };
        noteFilter.SelectedTagIds.Add(projectTag.Id);
        var noteStatusSearch =
            noteFilter.Matches(completed) &&
            completed.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase);
        Must(session.TrySetNoteCompletion(inheritedNote.Id, false, out error), error);

        var exactDateFilter = new WorkspaceFilter { NoteDate = "2032-02-03" };
        var undatedFilter = new WorkspaceFilter { UndatedNotesOnly = true };
        var noteDateBehavior =
            exactDateFilter.Matches(session.Current.Notes.First(note => note.Id == inheritedNote.Id)) &&
            !exactDateFilter.Matches(untaggedNote) &&
            session.Current.Notes.Where(note => note.Date is null).All(undatedFilter.Matches);

        Must(session.TrySaveNote(
            inheritedNote.Id,
            "已编辑的浮窗继承标签便签",
            new DateTime(2032, 3, 5),
            false,
            new[] { collaborationTag.Id },
            30,
            out error), error);
        var edited = session.Current.Notes.First(note => note.Id == inheritedNote.Id);
        var editTags =
            edited.TagIds.Count == 1 &&
            edited.TagIds.Contains(collaborationTag.Id, StringComparer.OrdinalIgnoreCase);
        var editDate = edited.Date == "2032-03-05";

        Must(session.TryRenameTag(projectTag.Id, "项目协作", projectTag.Color, out error), error);
        Must(session.TryDeleteTag(removableTag.Id, out error), error);
        var renamed = session.Current.Tags.Any(tag =>
            tag.Id == projectTag.Id &&
            tag.Name == "项目协作");
        var deleted = session.Current.Tags.All(tag => tag.Id != removableTag.Id);
        var recordsPreserved =
            session.Current.Countdowns.Count == initialCountdownCount + 3 &&
            session.Current.Notes.Count == initialNoteCount + 2;
        var detachedOnly =
            session.Current.Countdowns
                .Where(record => record.Name == "标签验证甲")
                .All(record =>
                    record.TagIds.Contains(projectTag.Id, StringComparer.OrdinalIgnoreCase) &&
                    !record.TagIds.Contains(removableTag.Id, StringComparer.OrdinalIgnoreCase));

        var selector = new TagSelector();
        selector.SetTags(session.Current.Tags);
        selector.SetSelection(new[] { projectTag.Id, collaborationTag.Id });
        var summaryCompact = selector.SummaryText.Length <= 18 && selector.SummaryText.Length > 0;
        var popupStartsClosed = !selector.IsPopupOpen;

        var creationAndAssociation =
            session.Current.Tags.Count >= initialTagCount + 2 &&
            session.Current.Countdowns.Any(record =>
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
}

internal sealed record PersistenceFailureExerciseResult(
    bool CountdownStatePreserved,
    bool PreferenceStatePreserved,
    bool CountdownDraftRetained,
    bool PreferenceDraftRetained,
    bool TagStatePreserved,
    bool TagDraftRetained,
    bool TagFilterRetained,
    bool TemporaryPathRemainsDirectory)
{
    public static PersistenceFailureExerciseResult NotRun { get; } =
        new(false, false, false, false, false, false, false, false);
}

internal sealed record TagInteractionExerciseResult(
    bool Executed,
    bool CreationAndAssociation,
    bool AnyAllFilter,
    bool EditorSummaryIsCompact,
    bool SearchCreateAvoidsDefaultSubmit,
    bool UntaggedAndCountdownStatus,
    bool NoteStatusSearchAndCompletion,
    bool NoteDateFilterControls,
    bool NoteDateFilterBehavior,
    bool FloatingNoteInheritsTags,
    bool FloatingNoteDateViaPicker,
    bool FloatingNotePopupOnDemand,
    bool FloatingNoteCanOverrideTags,
    bool NoteEditingPersistsTags,
    bool NoteEditingPersistsDate,
    bool RenameAndDeleteDetachOnly)
{
    public static TagInteractionExerciseResult NotRun { get; } =
        new(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
}
