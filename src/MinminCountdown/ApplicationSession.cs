using System.Globalization;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class ApplicationSession
{
    private readonly SettingsStore _store;

    private ApplicationSession(
        string settingsPath,
        SettingsStore store,
        CountdownSettings settings,
        int sourceSchemaVersion,
        bool migrated)
    {
        SettingsPath = Path.GetFullPath(settingsPath);
        _store = store;
        Current = store.Copy(settings);
        SourceSchemaVersion = sourceSchemaVersion;
        SettingsWereMigrated = migrated;
        Reminders.Initialize(Current, store.Clock.UtcNow);
    }

    public event EventHandler? StateChanged;

    public string SettingsPath { get; }

    public CountdownSettings Current { get; private set; }

    public int SourceSchemaVersion { get; }

    public bool SettingsWereMigrated { get; private set; }

    public WorkspaceFilter Filter { get; } = new();

    public ReminderScheduler Reminders { get; } = new();

    public DateTimeOffset BeijingNow => _store.GetCurrentBeijingTime();

    public DateTimeOffset UtcNow => _store.Clock.UtcNow;

    public static ApplicationSession Load(string settingsPath, SettingsStore? store = null)
    {
        var actualStore = store ?? new SettingsStore();
        if (!File.Exists(settingsPath))
        {
            return new ApplicationSession(
                settingsPath,
                actualStore,
                CountdownSettings.Empty,
                SettingsSchema.CurrentVersion,
                migrated: false);
        }

        var result = actualStore.Load(settingsPath);
        return new ApplicationSession(
            settingsPath,
            actualStore,
            result.Settings,
            result.SourceSchemaVersion,
            result.Migrated);
    }

    public void PersistMigrationIfNeeded()
    {
        if (!SettingsWereMigrated)
        {
            return;
        }

        _store.Save(SettingsPath, Current);
        SettingsWereMigrated = false;
    }

    public IReadOnlyList<CountdownRecord> GetSortedCountdowns(bool applyFilter = true)
    {
        var now = UtcNow;
        return Current.Countdowns
            .Where(record => !applyFilter || Filter.Matches(record, now))
            .OrderBy(record => record.TargetUtc > now ? 0 : 1)
            .ThenBy(record => record.TargetUtc)
            .ToArray();
    }

    public IReadOnlyList<NoteRecord> GetSortedNotes(bool applyFilter = true)
    {
        return Current.Notes
            .Where(note => !applyFilter || Filter.Matches(note))
            .OrderBy(note => note.IsCompleted ? 1 : 0)
            .ThenBy(note => note.Date is null ? 1 : 0)
            .ThenBy(note => note.Date, StringComparer.Ordinal)
            .ThenBy(note => note.Text, StringComparer.CurrentCulture)
            .ToArray();
    }

    public bool TrySaveCountdown(
        string? existingId,
        string name,
        DateTimeOffset targetUtc,
        IReadOnlyList<string> tagIds,
        out string error)
    {
        var records = Current.Countdowns.ToList();
        var normalizedId = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        var record = new CountdownRecord(normalizedId, name, targetUtc, tagIds.ToArray());
        var index = records.FindIndex(item => string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            records[index] = record;
        }
        else
        {
            records.Add(record);
        }

        return TryCommit(Current with { Countdowns = records }, out error);
    }

    public bool TryDeleteCountdown(string id, out string error)
    {
        var records = Current.Countdowns
            .Where(record => !string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == Current.Countdowns.Count)
        {
            error = "未找到倒计时。";
            return false;
        }

        return TryCommit(Current with { Countdowns = records }, out error);
    }

    public bool TrySaveNote(
        string? existingId,
        string text,
        DateTime? date,
        bool isCompleted,
        IReadOnlyList<string> tagIds,
        int? reminderIntervalMinutes,
        out string error)
    {
        var records = Current.Notes.ToList();
        var normalizedId = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("N") : existingId;
        var existing = records.FirstOrDefault(
            note => string.Equals(note.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        var reminderStart = reminderIntervalMinutes is null
            ? null
            : existing?.ReminderIntervalMinutes == reminderIntervalMinutes && existing.ReminderStartUtc is not null
                ? existing.ReminderStartUtc
                : UtcNow;
        if (existing?.IsCompleted == true && !isCompleted && reminderIntervalMinutes is not null)
        {
            reminderStart = UtcNow;
        }

        var record = new NoteRecord(
            normalizedId,
            text,
            date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            isCompleted,
            tagIds.ToArray(),
            reminderIntervalMinutes,
            reminderStart);
        var index = records.FindIndex(item => string.Equals(item.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            records[index] = record;
        }
        else
        {
            records.Add(record);
        }

        return TryCommit(Current with { Notes = records }, out error);
    }

    public bool TrySetNoteCompletion(string id, bool completed, out string error)
    {
        var note = Current.Notes.FirstOrDefault(
            item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            error = "未找到便签。";
            return false;
        }

        var start = note.ReminderStartUtc;
        if (note.IsCompleted && !completed && note.ReminderIntervalMinutes is not null)
        {
            start = UtcNow;
        }

        var records = Current.Notes
            .Select(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)
                ? item with { IsCompleted = completed, ReminderStartUtc = start }
                : item)
            .ToArray();
        return TryCommit(Current with { Notes = records }, out error);
    }

    public bool TryDeleteNote(string id, out string error)
    {
        var records = Current.Notes
            .Where(note => !string.Equals(note.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == Current.Notes.Count)
        {
            error = "未找到便签。";
            return false;
        }

        return TryCommit(Current with { Notes = records }, out error);
    }

    public bool TryCreateTag(string name, string? color, out TagRecord? tag, out string error)
    {
        tag = new TagRecord(Guid.NewGuid().ToString("N"), name, color);
        var createdId = tag.Id;
        if (TryCommit(Current with { Tags = Current.Tags.Append(tag).ToArray() }, out error))
        {
            tag = Current.Tags.First(item => string.Equals(item.Id, createdId, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        tag = null;
        return false;
    }

    public bool TryRenameTag(string id, string name, string? color, out string error)
    {
        var found = false;
        var tags = Current.Tags.Select(tag =>
        {
            if (!string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return tag;
            }

            found = true;
            return tag with { Name = name, Color = color };
        }).ToArray();
        if (!found)
        {
            error = "未找到标签。";
            return false;
        }

        return TryCommit(Current with { Tags = tags }, out error);
    }

    public bool TryDeleteTag(string id, out string error)
    {
        var tags = Current.Tags
            .Where(tag => !string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (tags.Length == Current.Tags.Count)
        {
            error = "未找到标签。";
            return false;
        }

        var countdowns = Current.Countdowns
            .Select(record => record with
            {
                TagIds = record.TagIds.Where(
                    tagId => !string.Equals(tagId, id, StringComparison.OrdinalIgnoreCase)).ToArray()
            })
            .ToArray();
        var notes = Current.Notes
            .Select(note => note with
            {
                TagIds = note.TagIds.Where(
                    tagId => !string.Equals(tagId, id, StringComparison.OrdinalIgnoreCase)).ToArray()
            })
            .ToArray();
        var candidate = Current with { Tags = tags, Countdowns = countdowns, Notes = notes };
        if (!TryCommit(candidate, out error))
        {
            return false;
        }

        Filter.SelectedTagIds.Remove(id);
        return true;
    }

    public bool TrySavePreferences(
        string displayPrecision,
        decimal monthlySalary,
        TimeSpan workStart,
        TimeSpan workEnd,
        out string error)
    {
        var preferences = new CountdownPreferences(displayPrecision, monthlySalary, workStart, workEnd);
        return TryCommit(Current with { Preferences = preferences }, out error);
    }

    public bool TryCommit(CountdownSettings candidate, out string error)
    {
        try
        {
            var canonical = _store.Copy(candidate);
            _store.Save(SettingsPath, canonical);
            Current = canonical;
            StateChanged?.Invoke(this, EventArgs.Empty);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is SettingsValidationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            error = exception.Message;
            return false;
        }
    }

    public string SerializeCurrent() => _store.Serialize(Current);
}
