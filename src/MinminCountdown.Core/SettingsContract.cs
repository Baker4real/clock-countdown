using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MinminCountdown.Core;

public static class SettingsContract
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly StringComparer IdentifierComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> ValidPrecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Day",
        "Hour",
        "Minute",
        "Second"
    };
    private static readonly HashSet<string> ValidThemePreferences = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ThemePreferenceMode.Auto),
        nameof(ThemePreferenceMode.Day),
        nameof(ThemePreferenceMode.Night)
    };

    public static SettingsLoadResult Deserialize(
        string json,
        IGuidSource? guidSource = null,
        IClock? clock = null,
        DateTimeOffset? migrationUtc = null)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new SettingsValidationException("设置 JSON 根节点必须是对象。");
            }

            var root = new JsonObjectProperties(document.RootElement, "设置");
            var schemaVersion = ReadSchemaVersion(root);
            EnsureRootShape(root, schemaVersion);
            EnsureTimeZone(root);

            var defaults = DefaultPreferences();
            CountdownSettings settings;
            var migratedStates = new Dictionary<string, ReminderRuleState>(IdentifierComparer);
            var normalizedMigrationUtc = (migrationUtc ?? (clock ?? SystemClock.Instance).UtcNow).ToUniversalTime();
            switch (schemaVersion)
            {
                case 1:
                    settings = ReadVersionOne(root, guidSource ?? SystemGuidSource.Instance, defaults);
                    break;
                case 2:
                    settings = new CountdownSettings(
                        ReadCountdowns(root, 2, Array.Empty<string>()),
                        Array.Empty<NoteRecord>(),
                        Array.Empty<TagRecord>(),
                        defaults);
                    break;
                case 3:
                    settings = new CountdownSettings(
                        ReadCountdowns(root, 3, Array.Empty<string>()),
                        Array.Empty<NoteRecord>(),
                        Array.Empty<TagRecord>(),
                        ReadPreferences(root, schemaVersion));
                    break;
                case 4:
                    settings = new CountdownSettings(
                        ReadCountdowns(root, 4, Array.Empty<string>()),
                        ReadNotes(root, 4, Array.Empty<string>(), migratedStates, normalizedMigrationUtc),
                        Array.Empty<TagRecord>(),
                        ReadPreferences(root, schemaVersion));
                    break;
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                    var tags = ReadTags(root);
                    var knownTagIds = tags.Select(static tag => tag.Id).ToArray();
                    settings = new CountdownSettings(
                        ReadCountdowns(root, schemaVersion, knownTagIds),
                        ReadNotes(root, schemaVersion, knownTagIds, migratedStates, normalizedMigrationUtc),
                        tags,
                        ReadPreferences(root, schemaVersion),
                        migratedStates);
                    break;
                case 10:
                    var currentTags = ReadTags(root);
                    var currentKnownTagIds = currentTags.Select(static tag => tag.Id).ToArray();
                    var currentCountdowns = ReadCountdowns(root, schemaVersion, currentKnownTagIds);
                    var currentNotes = ReadNotes(
                        root,
                        schemaVersion,
                        currentKnownTagIds,
                        migratedStates,
                        normalizedMigrationUtc);
                    var currentRules = currentCountdowns.SelectMany(static record => record.ReminderRules)
                        .Concat(currentNotes.SelectMany(static note => note.ReminderRules))
                        .ToArray();
                    settings = new CountdownSettings(
                        currentCountdowns,
                        currentNotes,
                        currentTags,
                        ReadPreferences(root, schemaVersion),
                        ReadReminderStates(root, currentRules));
                    break;
                default:
                    throw new SettingsValidationException("设置文件版本无效。");
            }

            return new SettingsLoadResult(Canonicalize(settings), schemaVersion, schemaVersion != SettingsSchema.CurrentVersion);
        }
        catch (SettingsValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SettingsValidationException("设置 JSON 无效。", exception);
        }
    }

    public static CountdownSettings Canonicalize(CountdownSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var preferences = NormalizePreferences(settings.Preferences);
        var tags = NormalizeTags(settings.Tags);
        var knownTagIds = tags.Select(static tag => tag.Id).ToArray();
        var ruleIds = new HashSet<string>(IdentifierComparer);
        var countdowns = NormalizeCountdowns(settings.Countdowns, knownTagIds, ruleIds);
        var notes = NormalizeNotes(settings.Notes, knownTagIds, ruleIds);
        var rules = countdowns.SelectMany(static record => record.ReminderRules)
            .Concat(notes.SelectMany(static note => note.ReminderRules))
            .ToDictionary(static rule => rule.Id, IdentifierComparer);
        var reminderStates = NormalizeReminderStates(settings.ReminderStates, rules);
        return new CountdownSettings(countdowns, notes, tags, preferences, reminderStates);
    }

    public static string SerializeCanonical(CountdownSettings settings)
    {
        var canonical = Canonicalize(settings);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", SettingsSchema.CurrentVersion);
        writer.WriteString("timeZoneId", SettingsSchema.ChinaStandardTimeZoneId);
        writer.WriteString("displayPrecision", canonical.Preferences.DisplayPrecision);
        writer.WriteNumber("monthlySalary", canonical.Preferences.MonthlySalary);
        writer.WriteString("workStart", FormatWorkTime(canonical.Preferences.WorkStart));
        writer.WriteString("workEnd", FormatWorkTime(canonical.Preferences.WorkEnd));
        writer.WriteString("themePreference", canonical.Preferences.ThemePreference.ToString());

        writer.WritePropertyName("tags");
        writer.WriteStartArray();
        foreach (var tag in canonical.Tags)
        {
            writer.WriteStartObject();
            writer.WriteString("id", tag.Id);
            writer.WriteString("name", tag.Name);
            if (tag.Color is not null)
            {
                writer.WriteString("color", tag.Color);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("countdowns");
        writer.WriteStartArray();
        foreach (var countdown in canonical.Countdowns)
        {
            writer.WriteStartObject();
            writer.WriteString("id", countdown.Id);
            writer.WriteString("name", countdown.Name);
            writer.WriteString("targetUtc", FormatUtc(countdown.TargetUtc));
            WriteTagIds(writer, countdown.TagIds);
            WriteReminderRules(writer, countdown.ReminderRules);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("notes");
        writer.WriteStartArray();
        foreach (var note in canonical.Notes)
        {
            writer.WriteStartObject();
            writer.WriteString("id", note.Id);
            writer.WriteString("text", note.Text);
            if (note.Date is null)
            {
                writer.WriteNull("date");
            }
            else
            {
                writer.WriteString("date", note.Date);
            }

            writer.WriteBoolean("isCompleted", note.IsCompleted);
            WriteTagIds(writer, note.TagIds);
            WriteReminderRules(writer, note.ReminderRules);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("reminderStates");
        writer.WriteStartObject();
        foreach (var state in canonical.ReminderStates.Values)
        {
            writer.WritePropertyName(state.RuleId);
            writer.WriteStartObject();
            if (state.HandledThroughUtc is null)
            {
                writer.WriteNull("handledThroughUtc");
            }
            else
            {
                writer.WriteString("handledThroughUtc", FormatUtc(state.HandledThroughUtc.Value));
            }

            if (state.LastDeliveredOccurrenceKey is null)
            {
                writer.WriteNull("lastDeliveredOccurrenceKey");
            }
            else
            {
                writer.WriteString("lastDeliveredOccurrenceKey", state.LastDeliveredOccurrenceKey);
            }

            if (state.LastDispositionOccurrenceKey is null)
            {
                writer.WriteNull("lastDispositionOccurrenceKey");
            }
            else
            {
                writer.WriteString("lastDispositionOccurrenceKey", state.LastDispositionOccurrenceKey);
            }

            writer.WritePropertyName("activeSnooze");
            if (state.ActiveSnooze is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteString("occurrenceKey", state.ActiveSnooze.OccurrenceKey);
                writer.WriteString("originalScheduledUtc", FormatUtc(state.ActiveSnooze.OriginalScheduledUtc));
                writer.WriteString("snoozeUntilUtc", FormatUtc(state.ActiveSnooze.SnoozeUntilUtc));
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return Utf8NoBom.GetString(buffer.WrittenSpan);
    }

    private static CountdownSettings ReadVersionOne(
        JsonObjectProperties root,
        IGuidSource guidSource,
        CountdownPreferences defaults)
    {
        var targetUtc = ReadTargetUtc(root.Require("targetUtc"));
        var countdown = new CountdownRecord(
            guidSource.NewGuid().ToString("N"),
            "我的倒计时",
            targetUtc,
            Array.Empty<string>());

        return new CountdownSettings(
            new[] { countdown },
            Array.Empty<NoteRecord>(),
            Array.Empty<TagRecord>(),
            defaults);
    }

    private static int ReadSchemaVersion(JsonObjectProperties root)
    {
        var value = root.Require("schemaVersion");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var schemaVersion))
        {
            throw new SettingsValidationException("设置文件版本无效。");
        }

        return schemaVersion;
    }

    private static void EnsureRootShape(JsonObjectProperties root, int schemaVersion)
    {
        switch (schemaVersion)
        {
            case 1:
                root.EnsureOnly("schemaVersion", "timeZoneId", "targetUtc");
                break;
            case 2:
                root.EnsureOnly("schemaVersion", "timeZoneId", "countdowns");
                break;
            case 3:
                root.EnsureOnly(
                    "schemaVersion", "timeZoneId", "displayPrecision", "monthlySalary",
                    "workStart", "workEnd", "countdowns");
                break;
            case 4:
                root.EnsureOnly(
                    "schemaVersion", "timeZoneId", "displayPrecision", "monthlySalary",
                    "workStart", "workEnd", "countdowns", "notes");
                break;
            case >= 5 and <= 8:
                root.EnsureOnly(
                    "schemaVersion", "timeZoneId", "displayPrecision", "monthlySalary",
                    "workStart", "workEnd", "tags", "countdowns", "notes");
                break;
            case 9:
                root.EnsureOnly(
                    "schemaVersion", "timeZoneId", "displayPrecision", "monthlySalary",
                    "workStart", "workEnd", "themePreference", "tags", "countdowns", "notes");
                break;
            case 10:
                root.EnsureOnly(
                    "schemaVersion", "timeZoneId", "displayPrecision", "monthlySalary",
                    "workStart", "workEnd", "themePreference", "tags", "countdowns", "notes",
                    "reminderStates");
                break;
        }
    }

    private static void EnsureTimeZone(JsonObjectProperties root)
    {
        var timeZoneId = ReadRequiredString(root.Require("timeZoneId"), "设置文件时区");
        if (!string.Equals(timeZoneId, SettingsSchema.ChinaStandardTimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SettingsValidationException("设置文件时区无效。");
        }
    }

    private static CountdownPreferences DefaultPreferences()
    {
        return new CountdownPreferences("Second", 0m, TimeSpan.FromHours(9), TimeSpan.FromHours(18));
    }

    private static CountdownPreferences ReadPreferences(JsonObjectProperties root, int schemaVersion)
    {
        var precision = ReadRequiredString(root.Require("displayPrecision"), "悬浮窗显示精度");
        var salary = ReadDecimal(root.Require("monthlySalary"));
        var workStart = ParseWorkTime(ReadRequiredString(root.Require("workStart"), "上班时间"));
        var workEnd = ParseWorkTime(ReadRequiredString(root.Require("workEnd"), "下班时间"));
        var themePreference = schemaVersion >= 9
            ? ParseThemePreference(ReadRequiredString(root.Require("themePreference"), "主题偏好"))
            : ThemePreferenceMode.Auto;
        return NormalizePreferences(new CountdownPreferences(
            precision,
            salary,
            workStart,
            workEnd,
            themePreference));
    }

    private static ThemePreferenceMode ParseThemePreference(string value)
    {
        if (!ValidThemePreferences.TryGetValue(value, out var canonical) ||
            !Enum.TryParse<ThemePreferenceMode>(canonical, out var preference))
        {
            throw new SettingsValidationException("设置中包含无效的主题偏好。");
        }

        return preference;
    }

    private static IReadOnlyList<TagRecord> ReadTags(JsonObjectProperties root)
    {
        var tags = new List<TagRecord>();
        var ids = new HashSet<string>(IdentifierComparer);
        var names = new HashSet<string>(IdentifierComparer);
        foreach (var item in ReadArray(root.Require("tags"), "标签库"))
        {
            var properties = new JsonObjectProperties(item, "标签");
            properties.EnsureOnly("id", "name", "color");
            var color = properties.TryGet("color", out var colorValue)
                ? ReadOptionalString(colorValue, "标签颜色")
                : null;
            var tag = NormalizeTag(new TagRecord(
                ReadRequiredString(properties.Require("id"), "标签 ID"),
                ReadRequiredString(properties.Require("name"), "标签名称"),
                color));
            if (!ids.Add(tag.Id))
            {
                throw new SettingsValidationException("设置中包含重复的标签 ID。");
            }

            if (!names.Add(tag.Name))
            {
                throw new SettingsValidationException("设置中包含重复的标签名称。");
            }

            tags.Add(tag);
        }

        return tags;
    }

    private static IReadOnlyList<CountdownRecord> ReadCountdowns(
        JsonObjectProperties root,
        int schemaVersion,
        IReadOnlyList<string> knownTagIds)
    {
        var countdowns = new List<CountdownRecord>();
        var ids = new HashSet<string>(IdentifierComparer);
        var names = new HashSet<string>(IdentifierComparer);
        foreach (var item in ReadArray(root.Require("countdowns"), "倒计时列表"))
        {
            var properties = new JsonObjectProperties(item, "倒计时");
            if (schemaVersion >= 10)
            {
                properties.EnsureOnly("id", "name", "targetUtc", "tagIds", "reminderRules");
            }
            else if (schemaVersion >= 5)
            {
                properties.EnsureOnly("id", "name", "targetUtc", "tagIds");
            }
            else
            {
                properties.EnsureOnly("id", "name", "targetUtc");
            }
            var id = NormalizeRequired(properties.Require("id"), "倒计时 ID");
            var name = NormalizeRequired(properties.Require("name"), "倒计时名称");
            if (name.Length > 30)
            {
                throw new SettingsValidationException("设置中包含无效的倒计时名称。");
            }

            if (!ids.Add(id))
            {
                throw new SettingsValidationException("设置中包含重复的倒计时 ID。");
            }

            if (!names.Add(name))
            {
                throw new SettingsValidationException("设置中包含重复的倒计时名称。");
            }

            var tagIds = schemaVersion >= 5
                ? CanonicalizeTagIds(ReadTagIds(properties.Require("tagIds")), knownTagIds, "倒计时")
                : Array.Empty<string>();
            var reminderRules = schemaVersion >= 10
                ? ReadReminderRules(properties, "倒计时", allowCountdownRelative: true)
                : Array.Empty<ReminderRule>();
            countdowns.Add(new CountdownRecord(
                id,
                name,
                ReadTargetUtc(properties.Require("targetUtc")),
                tagIds,
                reminderRules));
        }

        return countdowns;
    }

    private static IReadOnlyList<NoteRecord> ReadNotes(
        JsonObjectProperties root,
        int schemaVersion,
        IReadOnlyList<string> knownTagIds,
        IDictionary<string, ReminderRuleState> migratedStates,
        DateTimeOffset migrationUtc)
    {
        var notes = new List<NoteRecord>();
        var ids = new HashSet<string>(IdentifierComparer);
        foreach (var item in ReadArray(root.Require("notes"), "便签列表"))
        {
            var properties = new JsonObjectProperties(item, "便签");
            switch (schemaVersion)
            {
                case 4:
                    properties.EnsureOnly("id", "text", "isCompleted");
                    break;
                case 5:
                    properties.EnsureOnly("id", "text", "isCompleted", "tagIds");
                    break;
                case 6:
                    properties.EnsureOnly("id", "text", "date", "isCompleted", "tagIds");
                    break;
                case 7:
                    properties.EnsureOnly(
                        "id", "text", "date", "isCompleted", "tagIds",
                        "reminderIntervalMinutes", "reminderStartUtc");
                    break;
                case 8 or 9:
                    properties.EnsureOnly("id", "text", "date", "isCompleted", "tagIds", "reminder");
                    break;
                case 10:
                    properties.EnsureOnly("id", "text", "date", "isCompleted", "tagIds", "reminderRules");
                    break;
            }
            var id = NormalizeRequired(properties.Require("id"), "便签 ID");
            if (!ids.Add(id))
            {
                throw new SettingsValidationException("设置中包含重复的便签 ID。");
            }

            var text = NormalizeNoteText(ReadRequiredString(properties.Require("text"), "便签内容"));
            var isCompleted = ReadBoolean(properties.Require("isCompleted"), "便签完成状态");
            var tagIds = schemaVersion >= 5
                ? CanonicalizeTagIds(ReadTagIds(properties.Require("tagIds")), knownTagIds, "便签")
                : Array.Empty<string>();
            var date = schemaVersion >= 6
                ? NormalizeDate(properties.Require("date"))
                : null;
            if (schemaVersion >= 10)
            {
                notes.Add(new NoteRecord(
                    id,
                    text,
                    date,
                    isCompleted,
                    tagIds,
                    ReadReminderRules(properties, "便签", allowCountdownRelative: false)));
                continue;
            }

            var reminder = schemaVersion switch
            {
                >= 8 => ReadReminder(properties),
                7 => ReadVersionSevenReminder(properties),
                _ => NoReminder.Instance
            };
            var rules = ReminderRuleCompatibility.FromLegacy(id, reminder);
            AddLegacyReminderState(migratedStates, rules, reminder, migrationUtc);
            notes.Add(new NoteRecord(id, text, date, isCompleted, tagIds, rules));
        }

        return notes;
    }

    private static void AddLegacyReminderState(
        IDictionary<string, ReminderRuleState> states,
        IReadOnlyList<ReminderRule> rules,
        NoteReminder reminder,
        DateTimeOffset migrationUtc)
    {
        if (rules.Count == 0)
        {
            return;
        }

        var rule = rules[0];
        DateTimeOffset? handledThroughUtc = reminder switch
        {
            OnceReminder { Delivery: OnceDeliveryState.Delivered } once =>
                once.FirstDueUtc.ToUniversalTime(),
            RepeatingReminder repeating when migrationUtc >= repeating.FirstDueUtc =>
                GetLatestFixedIntervalOccurrence(repeating.FirstDueUtc, repeating.Interval, migrationUtc),
            _ => null
        };
        if (handledThroughUtc is null)
        {
            return;
        }

        var deliveredKey = reminder is OnceReminder { Delivery: OnceDeliveryState.Delivered }
            ? ReminderOccurrenceKey.Create(rule, handledThroughUtc.Value).ToString()
            : null;
        states.Add(
            rule.Id,
            new ReminderRuleState(rule.Id, handledThroughUtc, deliveredKey));
    }

    private static DateTimeOffset GetLatestFixedIntervalOccurrence(
        DateTimeOffset firstDueUtc,
        ReminderInterval interval,
        DateTimeOffset nowUtc)
    {
        var normalizedFirstDueUtc = firstDueUtc.ToUniversalTime();
        var intervalTicks = TimeSpan.FromMinutes(interval.Minutes).Ticks;
        var completedIntervals = (nowUtc.ToUniversalTime().Ticks - normalizedFirstDueUtc.Ticks) / intervalTicks;
        return normalizedFirstDueUtc.AddTicks(completedIntervals * intervalTicks);
    }

    private static IReadOnlyList<ReminderRule> ReadReminderRules(
        JsonObjectProperties properties,
        string owner,
        bool allowCountdownRelative)
    {
        var rules = new List<ReminderRule>();
        foreach (var item in ReadArray(properties.Require("reminderRules"), $"{owner}提醒规则"))
        {
            var rule = new JsonObjectProperties(item, $"{owner}提醒规则");
            var id = NormalizeRequired(rule.Require("id"), "提醒规则 ID");
            var type = ReadRequiredString(rule.Require("type"), "提醒规则类型");
            if (string.Equals(type, "absoluteOnce", StringComparison.OrdinalIgnoreCase))
            {
                rule.EnsureOnly("id", "type", "scheduledUtc");
                rules.Add(new AbsoluteOnceReminderRule(
                    id,
                    ReadReminderUtc(rule.Require("scheduledUtc"), "绝对一次提醒时间")));
                continue;
            }

            if (string.Equals(type, "fixedInterval", StringComparison.OrdinalIgnoreCase))
            {
                rule.EnsureOnly("id", "type", "firstDueUtc", "intervalMinutes");
                var intervalElement = rule.Require("intervalMinutes");
                if (!IsJsonInteger(intervalElement, out var interval) || interval is < 1 or > 1440)
                {
                    throw new SettingsValidationException("设置中的固定间隔提醒必须在 1 到 1440 分钟之间。");
                }

                rules.Add(new FixedIntervalReminderRule(
                    id,
                    ReadReminderUtc(rule.Require("firstDueUtc"), "固定间隔首次提醒时间"),
                    new ReminderInterval(interval)));
                continue;
            }

            if (allowCountdownRelative &&
                string.Equals(type, "countdownRelative", StringComparison.OrdinalIgnoreCase))
            {
                rule.EnsureOnly("id", "type", "offsetMinutesBefore");
                var offsetElement = rule.Require("offsetMinutesBefore");
                if (!IsJsonInteger(offsetElement, out var offsetMinutesBefore) ||
                    offsetMinutesBefore is < 0 or > 5_256_000)
                {
                    throw new SettingsValidationException(
                        "设置中的倒计时相对提醒必须提前 0 到 5256000 分钟。");
                }

                rules.Add(new CountdownRelativeReminderRule(id, offsetMinutesBefore));
                continue;
            }

            if (string.Equals(type, "calendar", StringComparison.OrdinalIgnoreCase))
            {
                rules.Add(ReadCalendarRule(rule, id));
                continue;
            }

            throw new SettingsValidationException("设置中包含当前 schema 不支持的提醒规则类型。");
        }

        return rules;
    }

    private static IReadOnlyDictionary<string, ReminderRuleState> ReadReminderStates(
        JsonObjectProperties root,
        IReadOnlyList<ReminderRule> rules)
    {
        var element = root.Require("reminderStates");
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new SettingsValidationException("提醒状态必须是按规则 ID 索引的对象。");
        }

        var ruleIds = new HashSet<string>(rules.Select(static rule => rule.Id), IdentifierComparer);
        var states = new Dictionary<string, ReminderRuleState>(IdentifierComparer);
        foreach (var property in element.EnumerateObject())
        {
            var ruleId = NormalizeRequired(property.Name, "提醒状态规则 ID");
            if (!ruleIds.Contains(ruleId))
            {
                throw new SettingsValidationException("设置中包含悬空的提醒规则状态。");
            }

            var state = new JsonObjectProperties(property.Value, "提醒规则状态");
            state.EnsureOnly(
                "handledThroughUtc",
                "lastDeliveredOccurrenceKey",
                "lastDispositionOccurrenceKey",
                "lastAcknowledgedOccurrenceKey",
                "activeSnooze");
            var handledThroughElement = state.Require("handledThroughUtc");
            var handledThroughUtc = handledThroughElement.ValueKind == JsonValueKind.Null
                ? (DateTimeOffset?)null
                : ReadReminderUtc(handledThroughElement, "提醒规则游标");
            var lastDeliveredOccurrenceKey = ReadOptionalString(
                state.Require("lastDeliveredOccurrenceKey"),
                "最后送达 occurrence key");
            var hasDisposition = state.TryGet("lastDispositionOccurrenceKey", out var dispositionElement);
            var hasAcknowledgement = state.TryGet("lastAcknowledgedOccurrenceKey", out var acknowledgementElement);
            if (hasDisposition == hasAcknowledgement)
            {
                throw new SettingsValidationException("提醒规则状态必须且只能包含一种处置 occurrence key 字段。");
            }

            var lastDispositionOccurrenceKey = ReadOptionalString(
                hasDisposition ? dispositionElement : acknowledgementElement,
                "最后处置 occurrence key");
            ReminderSnoozeState? activeSnooze = null;
            if (state.TryGet("activeSnooze", out var snoozeElement) &&
                snoozeElement.ValueKind != JsonValueKind.Null)
            {
                var snooze = new JsonObjectProperties(snoozeElement, "活动稍后提醒");
                snooze.EnsureOnly("occurrenceKey", "originalScheduledUtc", "snoozeUntilUtc");
                activeSnooze = new ReminderSnoozeState(
                    ReadRequiredString(snooze.Require("occurrenceKey"), "稍后 occurrence key"),
                    ReadReminderUtc(snooze.Require("originalScheduledUtc"), "稍后原计划时刻"),
                    ReadReminderUtc(snooze.Require("snoozeUntilUtc"), "稍后覆盖时刻"));
            }
            if (!states.TryAdd(
                    ruleId,
                    new ReminderRuleState(
                        ruleId,
                        handledThroughUtc,
                        lastDeliveredOccurrenceKey,
                        lastDispositionOccurrenceKey,
                        activeSnooze)))
            {
                throw new SettingsValidationException("设置中包含重复的提醒规则状态。");
            }
        }

        return states;
    }

    private static NoteReminder ReadVersionSevenReminder(JsonObjectProperties properties)
    {
        var intervalElement = properties.Require("reminderIntervalMinutes");
        var startElement = properties.Require("reminderStartUtc");
        if (intervalElement.ValueKind == JsonValueKind.Null && startElement.ValueKind == JsonValueKind.Null)
        {
            return NoReminder.Instance;
        }

        if (intervalElement.ValueKind == JsonValueKind.Null || startElement.ValueKind == JsonValueKind.Null)
        {
            throw new SettingsValidationException("设置中的便签提醒间隔和开始时间必须同时设置或同时关闭。");
        }

        if (!IsJsonInteger(intervalElement, out var interval) || interval is < 1 or > 1440)
        {
            throw new SettingsValidationException("设置中的便签提醒间隔必须在 1 到 1440 分钟之间。");
        }

        if (startElement.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParseExact(
                startElement.GetString(),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var reminderStart))
        {
            throw new SettingsValidationException("设置中的便签提醒开始时间必须是带时区的有效时间。");
        }

        var normalizedInterval = new ReminderInterval(interval);
        return new RepeatingReminder(
            reminderStart.ToUniversalTime().AddMinutes(normalizedInterval.Minutes),
            normalizedInterval);
    }

    private static NoteReminder ReadReminder(JsonObjectProperties properties)
    {
        var reminder = new JsonObjectProperties(properties.Require("reminder"), "便签提醒");
        var type = ReadRequiredString(reminder.Require("type"), "便签提醒类型");
        if (string.Equals(type, "none", StringComparison.OrdinalIgnoreCase))
        {
            reminder.EnsureOnly("type");
            return NoReminder.Instance;
        }

        var firstDueUtc = ReadReminderUtc(reminder.Require("firstDueUtc"), "便签首次提醒时间");
        if (string.Equals(type, "once", StringComparison.OrdinalIgnoreCase))
        {
            reminder.EnsureOnly("type", "firstDueUtc", "delivery");
            var deliveryText = ReadRequiredString(reminder.Require("delivery"), "一次性提醒送达状态");
            var delivery = deliveryText.ToLowerInvariant() switch
            {
                "pending" => OnceDeliveryState.Pending,
                "delivered" => OnceDeliveryState.Delivered,
                _ => throw new SettingsValidationException("设置中包含无效的一次性提醒送达状态。")
            };
            return new OnceReminder(firstDueUtc, delivery);
        }

        if (string.Equals(type, "repeat", StringComparison.OrdinalIgnoreCase))
        {
            reminder.EnsureOnly("type", "firstDueUtc", "intervalMinutes");
            var intervalElement = reminder.Require("intervalMinutes");
            if (!IsJsonInteger(intervalElement, out var interval) || interval is < 1 or > 1440)
            {
                throw new SettingsValidationException("设置中的便签提醒间隔必须在 1 到 1440 分钟之间。");
            }

            return new RepeatingReminder(firstDueUtc, new ReminderInterval(interval));
        }

        throw new SettingsValidationException("设置中包含无效的便签提醒类型。");
    }

    private static DateTimeOffset ReadReminderUtc(JsonElement element, string subject)
    {
        if (element.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParseExact(
                element.GetString(),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var value))
        {
            throw new SettingsValidationException($"设置中的{subject}必须是带时区的有效时间。");
        }

        return value.ToUniversalTime();
    }

    private static void WriteReminder(Utf8JsonWriter writer, NoteReminder reminder)
    {
        writer.WritePropertyName("reminder");
        writer.WriteStartObject();
        switch (reminder)
        {
            case NoReminder:
                writer.WriteString("type", "none");
                break;
            case OnceReminder once:
                writer.WriteString("type", "once");
                writer.WriteString("firstDueUtc", FormatUtc(once.FirstDueUtc));
                writer.WriteString(
                    "delivery",
                    once.Delivery == OnceDeliveryState.Pending ? "pending" : "delivered");
                break;
            case RepeatingReminder repeating:
                writer.WriteString("type", "repeat");
                writer.WriteString("firstDueUtc", FormatUtc(repeating.FirstDueUtc));
                writer.WriteNumber("intervalMinutes", repeating.Interval.Minutes);
                break;
            default:
                throw new SettingsValidationException("设置中包含无效的便签提醒。");
        }

        writer.WriteEndObject();
    }

    private static void WriteReminderRules(
        Utf8JsonWriter writer,
        IReadOnlyList<ReminderRule> reminderRules)
    {
        writer.WritePropertyName("reminderRules");
        writer.WriteStartArray();
        foreach (var rule in reminderRules)
        {
            writer.WriteStartObject();
            writer.WriteString("id", rule.Id);
            switch (rule)
            {
                case AbsoluteOnceReminderRule once:
                    writer.WriteString("type", "absoluteOnce");
                    writer.WriteString("scheduledUtc", FormatUtc(once.ScheduledUtc));
                    break;
                case FixedIntervalReminderRule repeating:
                    writer.WriteString("type", "fixedInterval");
                    writer.WriteString("firstDueUtc", FormatUtc(repeating.FirstDueUtc));
                    writer.WriteNumber("intervalMinutes", repeating.Interval.Minutes);
                    break;
                case CountdownRelativeReminderRule relative:
                    writer.WriteString("type", "countdownRelative");
                    writer.WriteNumber("offsetMinutesBefore", relative.OffsetMinutesBefore);
                    break;
                case CalendarReminderRule calendar:
                    writer.WriteString("type", "calendar");
                    writer.WriteString("frequency", FormatCalendarFrequency(calendar.Frequency));
                    writer.WriteNumber("interval", calendar.Interval);
                    writer.WriteString("startDate", calendar.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    writer.WriteString("localTime", calendar.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture));
                    WriteCalendarSelector(writer, calendar);
                    break;
                default:
                    throw new SettingsValidationException("设置中包含当前 schema 不支持的提醒规则。");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static CountdownPreferences NormalizePreferences(CountdownPreferences? preferences)
    {
        if (preferences is null)
        {
            throw new SettingsValidationException("设置状态缺少偏好设置。");
        }

        if (!ValidPrecisions.TryGetValue(preferences.DisplayPrecision, out var displayPrecision))
        {
            throw new SettingsValidationException("设置中包含无效的悬浮窗显示精度。");
        }

        if (preferences.MonthlySalary < 0)
        {
            throw new SettingsValidationException("月薪不能小于 0。");
        }

        var workStart = ParseWorkTime(FormatWorkTime(preferences.WorkStart));
        var workEnd = ParseWorkTime(FormatWorkTime(preferences.WorkEnd));
        if (workEnd <= workStart)
        {
            throw new SettingsValidationException("下班时间必须晚于上班时间。");
        }

        if (!Enum.IsDefined(preferences.ThemePreference))
        {
            throw new SettingsValidationException("设置中包含无效的主题偏好。");
        }

        return new CountdownPreferences(
            displayPrecision,
            preferences.MonthlySalary,
            workStart,
            workEnd,
            preferences.ThemePreference);
    }

    private static IReadOnlyList<TagRecord> NormalizeTags(IReadOnlyList<TagRecord>? tags)
    {
        if (tags is null)
        {
            throw new SettingsValidationException("设置状态缺少标签库。");
        }

        var normalized = new List<TagRecord>(tags.Count);
        var ids = new HashSet<string>(IdentifierComparer);
        var names = new HashSet<string>(IdentifierComparer);
        foreach (var tag in tags)
        {
            if (tag is null)
            {
                throw new SettingsValidationException("设置中包含无效的标签。");
            }

            var value = NormalizeTag(tag);
            if (!ids.Add(value.Id))
            {
                throw new SettingsValidationException("设置中包含重复的标签 ID。");
            }

            if (!names.Add(value.Name))
            {
                throw new SettingsValidationException("设置中包含重复的标签名称。");
            }

            normalized.Add(value);
        }

        return normalized;
    }

    private static TagRecord NormalizeTag(TagRecord tag)
    {
        var id = NormalizeRequired(tag.Id, "标签 ID");
        var name = NormalizeRequired(tag.Name, "标签名称");
        if (name.Length > 30)
        {
            throw new SettingsValidationException("标签名称不能超过 30 个字符。");
        }

        var color = string.IsNullOrWhiteSpace(tag.Color) ? null : tag.Color.Trim();
        if (color is not null && !IsValidColor(color))
        {
            throw new SettingsValidationException("标签颜色必须是 #RRGGBB 或 #AARRGGBB。");
        }

        return new TagRecord(id, name, color);
    }

    private static IReadOnlyList<CountdownRecord> NormalizeCountdowns(
        IReadOnlyList<CountdownRecord>? countdowns,
        IReadOnlyList<string> knownTagIds,
        ISet<string> globalRuleIds)
    {
        if (countdowns is null)
        {
            throw new SettingsValidationException("设置状态缺少倒计时列表。");
        }

        var normalized = new List<CountdownRecord>(countdowns.Count);
        var ids = new HashSet<string>(IdentifierComparer);
        var names = new HashSet<string>(IdentifierComparer);
        foreach (var countdown in countdowns)
        {
            if (countdown is null)
            {
                throw new SettingsValidationException("设置中包含无效的倒计时记录。");
            }

            var id = NormalizeRequired(countdown.Id, "倒计时 ID");
            var name = NormalizeRequired(countdown.Name, "倒计时名称");
            if (name.Length > 30)
            {
                throw new SettingsValidationException("设置中包含无效的倒计时记录。");
            }

            if (!ids.Add(id) || !names.Add(name))
            {
                throw new SettingsValidationException("设置中包含重复的倒计时记录。");
            }

            normalized.Add(new CountdownRecord(
                id,
                name,
                countdown.TargetUtc.ToUniversalTime(),
                CanonicalizeTagIds(countdown.TagIds, knownTagIds, "倒计时"),
                NormalizeReminderRules(
                    countdown.ReminderRules,
                    globalRuleIds,
                    "倒计时",
                    allowCountdownRelative: true)));
        }

        return normalized;
    }

    private static IReadOnlyList<NoteRecord> NormalizeNotes(
        IReadOnlyList<NoteRecord>? notes,
        IReadOnlyList<string> knownTagIds,
        ISet<string> globalRuleIds)
    {
        if (notes is null)
        {
            throw new SettingsValidationException("设置状态缺少便签列表。");
        }

        var normalized = new List<NoteRecord>(notes.Count);
        var ids = new HashSet<string>(IdentifierComparer);
        foreach (var note in notes)
        {
            if (note is null)
            {
                throw new SettingsValidationException("设置中包含无效的便签。");
            }

            var id = NormalizeRequired(note.Id, "便签 ID");
            if (!ids.Add(id))
            {
                throw new SettingsValidationException("设置中包含重复的便签 ID。");
            }

            normalized.Add(new NoteRecord(
                id,
                NormalizeNoteText(note.Text),
                NormalizeDate(note.Date),
                note.IsCompleted,
                CanonicalizeTagIds(note.TagIds, knownTagIds, "便签"),
                NormalizeReminderRules(
                    note.ReminderRules,
                    globalRuleIds,
                    "便签",
                    allowCountdownRelative: false)));
        }

        return normalized;
    }

    private static IReadOnlyList<ReminderRule> NormalizeReminderRules(
        IReadOnlyList<ReminderRule>? rules,
        ISet<string> globalRuleIds,
        string owner,
        bool allowCountdownRelative)
    {
        if (rules is null)
        {
            throw new SettingsValidationException($"{owner}缺少提醒规则列表。");
        }

        if (rules.Count > 8)
        {
            throw new SettingsValidationException($"{owner}最多只能包含 8 条提醒规则。");
        }

        var normalized = new List<ReminderRule>(rules.Count);
        var schedules = new HashSet<ReminderRuleScheduleKey>();
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                throw new SettingsValidationException($"{owner}包含无效的提醒规则。");
            }

            var id = NormalizeRequired(rule.Id, "提醒规则 ID");
            if (!globalRuleIds.Add(id))
            {
                throw new SettingsValidationException("设置中包含重复的全局提醒规则 ID。");
            }

            ReminderRule value;
            switch (rule)
            {
                case AbsoluteOnceReminderRule once:
                    var scheduledUtc = once.ScheduledUtc.ToUniversalTime();
                    value = new AbsoluteOnceReminderRule(id, scheduledUtc);
                    break;
                case FixedIntervalReminderRule repeating:
                    var firstDueUtc = repeating.FirstDueUtc.ToUniversalTime();
                    var interval = new ReminderInterval(repeating.Interval.Minutes);
                    value = new FixedIntervalReminderRule(id, firstDueUtc, interval);
                    break;
                case CountdownRelativeReminderRule relative when allowCountdownRelative &&
                                                                  relative.OffsetMinutesBefore is >= 0 and <= 5_256_000:
                    value = new CountdownRelativeReminderRule(id, relative.OffsetMinutesBefore);
                    break;
                case CalendarReminderRule calendar:
                    var normalizedCalendar = NormalizeCalendarRule(calendar with { Id = id });
                    value = normalizedCalendar;
                    break;
                default:
                    throw new SettingsValidationException("设置中包含当前 schema 不支持的提醒规则。");
            }

            if (!schedules.Add(ReminderRuleSchedule.CreateKey(value)))
            {
                throw new SettingsValidationException($"{owner}包含重复的规范提醒日程。");
            }

            normalized.Add(value);
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, ReminderRuleState> NormalizeReminderStates(
        IReadOnlyDictionary<string, ReminderRuleState>? states,
        IReadOnlyDictionary<string, ReminderRule> rules)
    {
        if (states is null)
        {
            throw new SettingsValidationException("设置状态缺少提醒规则状态。");
        }

        var normalized = new Dictionary<string, ReminderRuleState>(IdentifierComparer);
        foreach (var pair in states)
        {
            var ruleId = NormalizeRequired(pair.Key, "提醒状态规则 ID");
            var state = pair.Value;
            if (state is null ||
                !string.Equals(ruleId, NormalizeRequired(state.RuleId, "提醒状态规则 ID"), StringComparison.OrdinalIgnoreCase) ||
                !rules.TryGetValue(ruleId, out var rule))
            {
                throw new SettingsValidationException("设置中包含悬空或不一致的提醒规则状态。");
            }

            var handledThroughUtc = state.HandledThroughUtc?.ToUniversalTime();
            var lastDeliveredOccurrenceKey = string.IsNullOrWhiteSpace(state.LastDeliveredOccurrenceKey)
                ? null
                : state.LastDeliveredOccurrenceKey;
            var lastDispositionOccurrenceKey = string.IsNullOrWhiteSpace(state.LastDispositionOccurrenceKey)
                ? null
                : state.LastDispositionOccurrenceKey;
            var firstScheduledUtc = rule switch
            {
                AbsoluteOnceReminderRule once => (DateTimeOffset?)once.ScheduledUtc,
                FixedIntervalReminderRule repeating => repeating.FirstDueUtc,
                CountdownRelativeReminderRule => null,
                CalendarReminderRule => null,
                _ => throw new SettingsValidationException("设置中包含当前 schema 不支持的提醒规则。")
            };
            if (handledThroughUtc is not null &&
                firstScheduledUtc is not null &&
                handledThroughUtc < firstScheduledUtc)
            {
                throw new SettingsValidationException("提醒规则游标不能早于首次计划时刻。");
            }

            if (handledThroughUtc is not null &&
                !OccurrenceBelongsToRule(rule, handledThroughUtc.Value))
            {
                throw new SettingsValidationException("提醒规则游标必须落在规则的计划 occurrence 上。");
            }

            DateTimeOffset? lastDeliveredScheduledUtc = null;
            if (lastDeliveredOccurrenceKey is not null)
            {
                if (handledThroughUtc is null ||
                    !ReminderOccurrenceKey.TryParse(lastDeliveredOccurrenceKey, out var occurrence) ||
                    !string.Equals(occurrence.RuleId, ruleId, StringComparison.OrdinalIgnoreCase) ||
                    occurrence.ScheduledUtc > handledThroughUtc ||
                    !OccurrenceBelongsToRule(rule, occurrence.ScheduledUtc))
                {
                    throw new SettingsValidationException("提醒 occurrence 与规则游标互相矛盾。");
                }

                lastDeliveredScheduledUtc = occurrence.ScheduledUtc;
                lastDeliveredOccurrenceKey = ReminderOccurrenceKey.Create(rule, occurrence.ScheduledUtc).ToString();
            }

            DateTimeOffset? lastDispositionScheduledUtc = null;
            if (lastDispositionOccurrenceKey is not null)
            {
                if (handledThroughUtc is null ||
                    lastDeliveredOccurrenceKey is null ||
                    !ReminderOccurrenceKey.TryParse(lastDispositionOccurrenceKey, out var occurrence) ||
                    !string.Equals(occurrence.RuleId, ruleId, StringComparison.OrdinalIgnoreCase) ||
                    occurrence.ScheduledUtc > handledThroughUtc ||
                    lastDeliveredScheduledUtc is null ||
                    occurrence.ScheduledUtc > lastDeliveredScheduledUtc.Value ||
                    !OccurrenceBelongsToRule(rule, occurrence.ScheduledUtc))
                {
                    throw new SettingsValidationException("提醒知道了 occurrence 与规则游标互相矛盾。");
                }

                lastDispositionScheduledUtc = occurrence.ScheduledUtc;
                lastDispositionOccurrenceKey = ReminderOccurrenceKey.Create(
                    rule,
                    occurrence.ScheduledUtc).ToString();
            }

            ReminderSnoozeState? activeSnooze = null;
            if (state.ActiveSnooze is { } candidateSnooze)
            {
                var originalScheduledUtc = candidateSnooze.OriginalScheduledUtc.ToUniversalTime();
                var snoozeUntilUtc = candidateSnooze.SnoozeUntilUtc.ToUniversalTime();
                if (handledThroughUtc is null ||
                    lastDeliveredOccurrenceKey is null ||
                    !ReminderOccurrenceKey.TryParse(candidateSnooze.OccurrenceKey, out var occurrence) ||
                    !string.Equals(occurrence.RuleId, ruleId, StringComparison.OrdinalIgnoreCase) ||
                    occurrence.ScheduledUtc != originalScheduledUtc ||
                    occurrence.ScheduledUtc > handledThroughUtc ||
                    !OccurrenceBelongsToRule(rule, occurrence.ScheduledUtc) ||
                    !string.Equals(lastDeliveredOccurrenceKey, ReminderOccurrenceKey.Create(rule, occurrence.ScheduledUtc).ToString(), StringComparison.Ordinal) ||
                    snoozeUntilUtc <= originalScheduledUtc ||
                    lastDispositionScheduledUtc >= originalScheduledUtc)
                {
                    throw new SettingsValidationException("活动稍后提醒与规则 occurrence 状态互相矛盾。");
                }

                activeSnooze = new ReminderSnoozeState(
                    ReminderOccurrenceKey.Create(rule, occurrence.ScheduledUtc).ToString(),
                    originalScheduledUtc,
                    snoozeUntilUtc);
            }

            if (handledThroughUtc is null &&
                lastDeliveredOccurrenceKey is null &&
                lastDispositionOccurrenceKey is null &&
                activeSnooze is null)
            {
                throw new SettingsValidationException("空提醒状态不得持久化。");
            }

            normalized.Add(
                ruleId,
                new ReminderRuleState(
                    rule.Id,
                    handledThroughUtc,
                    lastDeliveredOccurrenceKey,
                    lastDispositionOccurrenceKey,
                    activeSnooze));
        }

        return normalized;
    }

    private static bool OccurrenceBelongsToRule(ReminderRule rule, DateTimeOffset scheduledUtc)
    {
        var normalizedScheduledUtc = scheduledUtc.ToUniversalTime();
        return rule switch
        {
            AbsoluteOnceReminderRule once => normalizedScheduledUtc == once.ScheduledUtc,
            FixedIntervalReminderRule repeating when normalizedScheduledUtc >= repeating.FirstDueUtc =>
                (normalizedScheduledUtc.Ticks - repeating.FirstDueUtc.Ticks) %
                TimeSpan.FromMinutes(repeating.Interval.Minutes).Ticks == 0,
            CountdownRelativeReminderRule => true,
            CalendarReminderRule calendar =>
                CalendarOccurrenceCalculator.IsOccurrence(calendar, normalizedScheduledUtc),
            _ => false
        };
    }

    private static CalendarReminderRule ReadCalendarRule(JsonObjectProperties rule, string id)
    {
        var frequency = ParseCalendarFrequency(
            ReadRequiredString(rule.Require("frequency"), "日历提醒频率"));
        var intervalElement = rule.Require("interval");
        if (!IsJsonInteger(intervalElement, out var interval) || interval is < 1 or > 999)
        {
            throw new SettingsValidationException("设置中的日历提醒间隔必须在 1 到 999 之间。");
        }

        var startDate = ReadCalendarDate(rule.Require("startDate"));
        var localTime = ReadCalendarTime(rule.Require("localTime"));
        CalendarSelector selector;
        switch (frequency)
        {
            case CalendarFrequency.Daily:
                rule.EnsureOnly("id", "type", "frequency", "interval", "startDate", "localTime");
                selector = new DailyCalendarSelector();
                break;
            case CalendarFrequency.Weekly:
                rule.EnsureOnly("id", "type", "frequency", "interval", "startDate", "localTime", "weekdays");
                selector = new WeeklyCalendarSelector(ReadCalendarWeekdays(rule.Require("weekdays")));
                break;
            case CalendarFrequency.Monthly:
                rule.EnsureOnly("id", "type", "frequency", "interval", "startDate", "localTime", "day", "lastDay");
                selector = ReadMonthlySelector(rule);
                break;
            case CalendarFrequency.Yearly:
                rule.EnsureOnly("id", "type", "frequency", "interval", "startDate", "localTime", "month", "day", "lastDay");
                selector = ReadYearlySelector(rule);
                break;
            default:
                throw new SettingsValidationException("设置中包含不支持的日历提醒频率。");
        }

        return NormalizeCalendarRule(new CalendarReminderRule(
            id,
            frequency,
            interval,
            startDate,
            localTime,
            selector));
    }

    private static CalendarReminderRule NormalizeCalendarRule(CalendarReminderRule rule)
    {
        if (rule.Interval is < 1 or > 999)
        {
            throw new SettingsValidationException("设置中的日历提醒间隔必须在 1 到 999 之间。");
        }

        CalendarSelector selector = (rule.Frequency, rule.Selector) switch
        {
            (CalendarFrequency.Daily, DailyCalendarSelector) => new DailyCalendarSelector(),
            (CalendarFrequency.Weekly, WeeklyCalendarSelector weekly) =>
                new WeeklyCalendarSelector(NormalizeCalendarWeekdays(weekly.Weekdays)),
            (CalendarFrequency.Monthly, MonthlyCalendarSelector monthly) when
                monthly.LastDay && monthly.Day is null => new MonthlyCalendarSelector(null, true),
            (CalendarFrequency.Monthly, MonthlyCalendarSelector monthly) when
                !monthly.LastDay && monthly.Day is >= 1 and <= 31 =>
                new MonthlyCalendarSelector(monthly.Day, false),
            (CalendarFrequency.Yearly, YearlyCalendarSelector yearly) when
                yearly.Month is >= 1 and <= 12 && yearly.LastDay && yearly.Day is null =>
                new YearlyCalendarSelector(yearly.Month, null, true),
            (CalendarFrequency.Yearly, YearlyCalendarSelector yearly) when
                yearly.Month is >= 1 and <= 12 && !yearly.LastDay && yearly.Day is >= 1 and <= 31 =>
                new YearlyCalendarSelector(yearly.Month, yearly.Day, false),
            _ => throw new SettingsValidationException("日历提醒选择器与频率不匹配。")
        };

        return new CalendarReminderRule(
            NormalizeRequired(rule.Id, "提醒规则 ID"),
            rule.Frequency,
            rule.Interval,
            rule.StartDate,
            new TimeOnly(rule.LocalTime.Hour, rule.LocalTime.Minute),
            selector);
    }

    private static MonthlyCalendarSelector ReadMonthlySelector(JsonObjectProperties rule)
    {
        var hasDay = rule.TryGet("day", out var dayElement);
        var hasLastDay = rule.TryGet("lastDay", out var lastDayElement);
        if (hasDay == hasLastDay)
        {
            throw new SettingsValidationException("按月日历提醒必须且只能选择日期或最后一天。");
        }

        if (hasLastDay)
        {
            if (!ReadBoolean(lastDayElement, "按月最后一天") )
            {
                throw new SettingsValidationException("按月最后一天必须显式选择 true。");
            }

            return new MonthlyCalendarSelector(null, true);
        }

        if (!IsJsonInteger(dayElement, out var day) || day is < 1 or > 31)
        {
            throw new SettingsValidationException("按月日历提醒日期必须在 1 到 31 之间。");
        }

        return new MonthlyCalendarSelector(day, false);
    }

    private static YearlyCalendarSelector ReadYearlySelector(JsonObjectProperties rule)
    {
        var monthElement = rule.Require("month");
        if (!IsJsonInteger(monthElement, out var month) || month is < 1 or > 12)
        {
            throw new SettingsValidationException("按年日历提醒月份必须在 1 到 12 之间。");
        }

        var monthly = ReadMonthlySelector(rule);
        return new YearlyCalendarSelector(month, monthly.Day, monthly.LastDay);
    }

    private static IReadOnlyList<DayOfWeek> ReadCalendarWeekdays(JsonElement element)
    {
        var weekdays = new List<DayOfWeek>();
        foreach (var item in ReadArray(element, "按周日历提醒星期"))
        {
            weekdays.Add(ParseCalendarWeekday(ReadRequiredString(item, "按周日历提醒星期")));
        }

        return NormalizeCalendarWeekdays(weekdays);
    }

    private static IReadOnlyList<DayOfWeek> NormalizeCalendarWeekdays(IReadOnlyList<DayOfWeek>? weekdays)
    {
        if (weekdays is null || weekdays.Count == 0 ||
            weekdays.Any(day => day is < DayOfWeek.Sunday or > DayOfWeek.Saturday))
        {
            throw new SettingsValidationException("按周日历提醒必须至少选择一个有效星期。");
        }

        var unique = new HashSet<DayOfWeek>();
        foreach (var weekday in weekdays)
        {
            if (!unique.Add(weekday))
            {
                throw new SettingsValidationException("按周日历提醒不能包含重复星期。");
            }
        }

        return unique.OrderBy(static day => day == DayOfWeek.Sunday ? 7 : (int)day).ToArray();
    }

    private static CalendarFrequency ParseCalendarFrequency(string value) => value.Trim().ToLowerInvariant() switch
    {
        "daily" => CalendarFrequency.Daily,
        "weekly" => CalendarFrequency.Weekly,
        "monthly" => CalendarFrequency.Monthly,
        "yearly" => CalendarFrequency.Yearly,
        _ => throw new SettingsValidationException("设置中包含不支持的日历提醒频率。")
    };

    private static string FormatCalendarFrequency(CalendarFrequency frequency) => frequency switch
    {
        CalendarFrequency.Daily => "daily",
        CalendarFrequency.Weekly => "weekly",
        CalendarFrequency.Monthly => "monthly",
        CalendarFrequency.Yearly => "yearly",
        _ => throw new SettingsValidationException("设置中包含不支持的日历提醒频率。")
    };

    private static DayOfWeek ParseCalendarWeekday(string value) => value.Trim().ToLowerInvariant() switch
    {
        "monday" => DayOfWeek.Monday,
        "tuesday" => DayOfWeek.Tuesday,
        "wednesday" => DayOfWeek.Wednesday,
        "thursday" => DayOfWeek.Thursday,
        "friday" => DayOfWeek.Friday,
        "saturday" => DayOfWeek.Saturday,
        "sunday" => DayOfWeek.Sunday,
        _ => throw new SettingsValidationException("设置中包含无效的日历提醒星期。")
    };

    private static string FormatCalendarWeekday(DayOfWeek value) => value switch
    {
        DayOfWeek.Monday => "monday",
        DayOfWeek.Tuesday => "tuesday",
        DayOfWeek.Wednesday => "wednesday",
        DayOfWeek.Thursday => "thursday",
        DayOfWeek.Friday => "friday",
        DayOfWeek.Saturday => "saturday",
        DayOfWeek.Sunday => "sunday",
        _ => throw new SettingsValidationException("设置中包含无效的日历提醒星期。")
    };

    private static DateOnly ReadCalendarDate(JsonElement element)
    {
        var value = ReadRequiredString(element, "日历提醒起始日期");
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new SettingsValidationException("日历提醒起始日期必须使用 yyyy-MM-dd。");
        }

        return date;
    }

    private static TimeOnly ReadCalendarTime(JsonElement element)
    {
        var value = ReadRequiredString(element, "日历提醒本地时间");
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            throw new SettingsValidationException("日历提醒本地时间必须使用 HH:mm。");
        }

        return time;
    }

    private static void WriteCalendarSelector(Utf8JsonWriter writer, CalendarReminderRule rule)
    {
        switch (rule.Selector)
        {
            case DailyCalendarSelector:
                break;
            case WeeklyCalendarSelector weekly:
                writer.WritePropertyName("weekdays");
                writer.WriteStartArray();
                foreach (var weekday in weekly.Weekdays)
                {
                    writer.WriteStringValue(FormatCalendarWeekday(weekday));
                }
                writer.WriteEndArray();
                break;
            case MonthlyCalendarSelector { LastDay: true }:
                writer.WriteBoolean("lastDay", true);
                break;
            case MonthlyCalendarSelector monthly:
                writer.WriteNumber("day", monthly.Day!.Value);
                break;
            case YearlyCalendarSelector yearly:
                writer.WriteNumber("month", yearly.Month);
                if (yearly.LastDay)
                {
                    writer.WriteBoolean("lastDay", true);
                }
                else
                {
                    writer.WriteNumber("day", yearly.Day!.Value);
                }
                break;
            default:
                throw new SettingsValidationException("日历提醒选择器与频率不匹配。");
        }
    }

    private static NoteReminder NormalizeReminder(NoteReminder? reminder) => reminder switch
    {
        NoReminder => NoReminder.Instance,
        OnceReminder once => NormalizeOnceReminder(once),
        RepeatingReminder repeating => new RepeatingReminder(
            repeating.FirstDueUtc.ToUniversalTime(),
            new ReminderInterval(repeating.Interval.Minutes)),
        _ => throw new SettingsValidationException("设置中包含无效的便签提醒。")
    };

    private static OnceReminder NormalizeOnceReminder(OnceReminder reminder)
    {
        if (reminder.Delivery is not OnceDeliveryState.Pending and not OnceDeliveryState.Delivered)
        {
            throw new SettingsValidationException("设置中包含无效的一次性提醒送达状态。");
        }

        return new OnceReminder(reminder.FirstDueUtc.ToUniversalTime(), reminder.Delivery);
    }

    private static IReadOnlyList<string> CanonicalizeTagIds(
        IReadOnlyList<string>? tagIds,
        IReadOnlyList<string> knownTagIds,
        string recordKind)
    {
        if (tagIds is null)
        {
            throw new SettingsValidationException($"设置中的{recordKind}缺少标签 ID 列表。");
        }

        var knownById = new Dictionary<string, string>(IdentifierComparer);
        foreach (var knownTagId in knownTagIds)
        {
            var normalizedKnownTagId = NormalizeRequired(knownTagId, "标签库 ID");
            if (!knownById.TryAdd(normalizedKnownTagId, normalizedKnownTagId))
            {
                throw new SettingsValidationException("标签库包含重复 ID。");
            }
        }

        var result = new List<string>(tagIds.Count);
        var seen = new HashSet<string>(IdentifierComparer);
        foreach (var tagId in tagIds)
        {
            var normalizedTagId = NormalizeRequired(tagId, $"{recordKind}标签 ID");
            if (!knownById.TryGetValue(normalizedTagId, out var canonicalTagId))
            {
                throw new SettingsValidationException("记录包含不存在的标签 ID。");
            }

            if (!seen.Add(canonicalTagId))
            {
                throw new SettingsValidationException("记录包含重复的标签 ID。");
            }

            result.Add(canonicalTagId);
        }

        return result;
    }

    private static IReadOnlyList<string> ReadTagIds(JsonElement element)
    {
        var tagIds = new List<string>();
        foreach (var item in ReadArray(element, "标签 ID 列表"))
        {
            tagIds.Add(ReadRequiredString(item, "标签 ID"));
        }

        return tagIds;
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement element, string subject)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new SettingsValidationException($"{subject}必须是数组。");
        }

        return element.EnumerateArray().ToArray();
    }

    private static DateTimeOffset ReadTargetUtc(JsonElement element)
    {
        var text = ReadRequiredString(element, "倒计时截止时间");
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var targetUtc))
        {
            throw new SettingsValidationException("设置中包含无效的倒计时截止时间。");
        }

        return targetUtc.ToUniversalTime();
    }

    private static decimal ReadDecimal(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(
                element.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        throw new SettingsValidationException("设置中包含无效的月薪。");
    }

    private static bool ReadBoolean(JsonElement element, string subject)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new SettingsValidationException($"{subject}无效。")
        };
    }

    private static string? ReadOptionalString(JsonElement element, string subject)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw new SettingsValidationException($"{subject}无效。")
        };
    }

    private static string ReadRequiredString(JsonElement element, string subject)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value)
        {
            throw new SettingsValidationException($"{subject}必须是字符串。");
        }

        return value;
    }

    private static string NormalizeRequired(JsonElement element, string subject)
    {
        return NormalizeRequired(ReadRequiredString(element, subject), subject);
    }

    private static string NormalizeRequired(string? value, string subject)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new SettingsValidationException($"{subject}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeNoteText(string? value)
    {
        var normalized = NormalizeRequired(value, "便签内容");
        if (normalized.Length > 140)
        {
            throw new SettingsValidationException("便签内容不能超过 140 个字符。");
        }

        return normalized;
    }

    private static string? NormalizeDate(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => NormalizeDate(element.GetString()),
            _ => throw new SettingsValidationException("便签日期必须使用 yyyy-MM-dd 格式。")
        };
    }

    private static string? NormalizeDate(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length != 10 ||
            value[4] != '-' ||
            value[7] != '-' ||
            !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new SettingsValidationException("便签日期必须使用 yyyy-MM-dd 格式。");
        }

        return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static TimeSpan ParseWorkTime(string value)
    {
        if (!TimeSpan.TryParseExact(value.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var result))
        {
            throw new SettingsValidationException("上下班时间格式无效，请使用 09:00 这样的 24 小时制时间。");
        }

        return result;
    }

    private static string FormatWorkTime(TimeSpan value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value.Hours:D2}:{value.Minutes:D2}");
    }

    private static int? NormalizeReminderInterval(int? intervalMinutes)
    {
        if (intervalMinutes is null)
        {
            return null;
        }

        if (intervalMinutes is < 1 or > 1440)
        {
            throw new SettingsValidationException("便签提醒间隔必须是 1 到 1440 分钟之间的整数。");
        }

        return intervalMinutes;
    }

    private static bool IsJsonInteger(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = element.GetRawText();
        if (raw.Contains('.', StringComparison.Ordinal) ||
            raw.Contains('e', StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidColor(string color)
    {
        if (color.Length is not (7 or 9) || color[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < color.Length; index++)
        {
            if (!Uri.IsHexDigit(color[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static void WriteTagIds(Utf8JsonWriter writer, IReadOnlyList<string> tagIds)
    {
        writer.WritePropertyName("tagIds");
        writer.WriteStartArray();
        foreach (var tagId in tagIds)
        {
            writer.WriteStringValue(tagId);
        }

        writer.WriteEndArray();
    }

    private sealed class JsonObjectProperties
    {
        private readonly Dictionary<string, JsonElement> _properties = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _subject;

        public JsonObjectProperties(JsonElement element, string subject)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new SettingsValidationException($"{subject}必须是对象。");
            }

            _subject = subject;
            foreach (var property in element.EnumerateObject())
            {
                if (!_properties.TryAdd(property.Name, property.Value))
                {
                    throw new SettingsValidationException($"{subject}包含重复字段。");
                }
            }
        }

        public JsonElement Require(string name)
        {
            if (!_properties.TryGetValue(name, out var value))
            {
                throw new SettingsValidationException($"{_subject}中缺少 {name} 字段。");
            }

            return value;
        }

        public bool TryGet(string name, out JsonElement value)
        {
            return _properties.TryGetValue(name, out value);
        }

        public void EnsureOnly(params string[] names)
        {
            var allowed = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            if (_properties.Keys.Any(name => !allowed.Contains(name)))
            {
                throw new SettingsValidationException($"{_subject}包含不适用于当前类型的字段。");
            }
        }
    }
}
