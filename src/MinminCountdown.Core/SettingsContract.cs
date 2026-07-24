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

    public static SettingsLoadResult Deserialize(string json, IGuidSource? guidSource = null)
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
            EnsureTimeZone(root);

            var defaults = DefaultPreferences();
            CountdownSettings settings;
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
                        ReadPreferences(root));
                    break;
                case 4:
                    settings = new CountdownSettings(
                        ReadCountdowns(root, 4, Array.Empty<string>()),
                        ReadNotes(root, 4, Array.Empty<string>()),
                        Array.Empty<TagRecord>(),
                        ReadPreferences(root));
                    break;
                case 5:
                case 6:
                case 7:
                    var tags = ReadTags(root);
                    var knownTagIds = tags.Select(static tag => tag.Id).ToArray();
                    settings = new CountdownSettings(
                        ReadCountdowns(root, schemaVersion, knownTagIds),
                        ReadNotes(root, schemaVersion, knownTagIds),
                        tags,
                        ReadPreferences(root));
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
        var countdowns = NormalizeCountdowns(settings.Countdowns, knownTagIds);
        var notes = NormalizeNotes(settings.Notes, knownTagIds);
        return new CountdownSettings(countdowns, notes, tags, preferences);
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
            if (note.ReminderIntervalMinutes is null)
            {
                writer.WriteNull("reminderIntervalMinutes");
            }
            else
            {
                writer.WriteNumber("reminderIntervalMinutes", note.ReminderIntervalMinutes.Value);
            }

            if (note.ReminderStartUtc is null)
            {
                writer.WriteNull("reminderStartUtc");
            }
            else
            {
                writer.WriteString("reminderStartUtc", FormatUtc(note.ReminderStartUtc.Value));
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
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

    private static CountdownPreferences ReadPreferences(JsonObjectProperties root)
    {
        var precision = ReadRequiredString(root.Require("displayPrecision"), "悬浮窗显示精度");
        var salary = ReadDecimal(root.Require("monthlySalary"));
        var workStart = ParseWorkTime(ReadRequiredString(root.Require("workStart"), "上班时间"));
        var workEnd = ParseWorkTime(ReadRequiredString(root.Require("workEnd"), "下班时间"));
        return NormalizePreferences(new CountdownPreferences(precision, salary, workStart, workEnd));
    }

    private static IReadOnlyList<TagRecord> ReadTags(JsonObjectProperties root)
    {
        var tags = new List<TagRecord>();
        var ids = new HashSet<string>(IdentifierComparer);
        var names = new HashSet<string>(IdentifierComparer);
        foreach (var item in ReadArray(root.Require("tags"), "标签库"))
        {
            var properties = new JsonObjectProperties(item, "标签");
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
            countdowns.Add(new CountdownRecord(
                id,
                name,
                ReadTargetUtc(properties.Require("targetUtc")),
                tagIds));
        }

        return countdowns;
    }

    private static IReadOnlyList<NoteRecord> ReadNotes(
        JsonObjectProperties root,
        int schemaVersion,
        IReadOnlyList<string> knownTagIds)
    {
        var notes = new List<NoteRecord>();
        var ids = new HashSet<string>(IdentifierComparer);
        foreach (var item in ReadArray(root.Require("notes"), "便签列表"))
        {
            var properties = new JsonObjectProperties(item, "便签");
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
            var (intervalMinutes, reminderStartUtc) = schemaVersion >= 7
                ? ReadReminder(properties)
                : (null, null);
            notes.Add(new NoteRecord(id, text, date, isCompleted, tagIds, intervalMinutes, reminderStartUtc));
        }

        return notes;
    }

    private static (int? IntervalMinutes, DateTimeOffset? ReminderStartUtc) ReadReminder(JsonObjectProperties properties)
    {
        var intervalElement = properties.Require("reminderIntervalMinutes");
        var startElement = properties.Require("reminderStartUtc");
        if (intervalElement.ValueKind == JsonValueKind.Null && startElement.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
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

        return (interval, reminderStart.ToUniversalTime());
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

        return new CountdownPreferences(displayPrecision, preferences.MonthlySalary, workStart, workEnd);
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
        IReadOnlyList<string> knownTagIds)
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
                CanonicalizeTagIds(countdown.TagIds, knownTagIds, "倒计时")));
        }

        return normalized;
    }

    private static IReadOnlyList<NoteRecord> NormalizeNotes(
        IReadOnlyList<NoteRecord>? notes,
        IReadOnlyList<string> knownTagIds)
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

            var intervalMinutes = NormalizeReminderInterval(note.ReminderIntervalMinutes);
            var reminderStartUtc = note.ReminderStartUtc?.ToUniversalTime();
            if ((intervalMinutes is null) != (reminderStartUtc is null))
            {
                throw new SettingsValidationException("便签提醒间隔和开始时间必须同时设置或同时关闭。");
            }

            normalized.Add(new NoteRecord(
                id,
                NormalizeNoteText(note.Text),
                NormalizeDate(note.Date),
                note.IsCompleted,
                CanonicalizeTagIds(note.TagIds, knownTagIds, "便签"),
                intervalMinutes,
                reminderStartUtc));
        }

        return normalized;
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
    }
}
