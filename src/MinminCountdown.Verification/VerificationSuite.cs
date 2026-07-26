using System.Text.Json;
using System.Text;
using MinminCountdown.Core;

namespace MinminCountdown.Verification;

internal static class VerificationSuite
{
    public static IReadOnlyList<VerificationResult> RunAll(string? externalSettingsPath)
    {
        var results = new List<VerificationResult>();
        foreach (var fixture in SettingsFixtures.AllVersions)
        {
            Run(
                results,
                $"schema-v{fixture.Version}-migrates-to-canonical-v8",
                () => VerifySchemaMigration(fixture.Version, fixture.Json));
        }

        Run(results, "canonical-v8-is-stable", VerifyCanonicalVersionSevenIsStable);
        Run(results, "schema-v7-reminder-migrates-to-v8-repeat", VerifyVersionSevenReminderMigration);
        Run(results, "schema-v8-reminder-branches-reject-foreign-fields", VerifyVersionEightReminderBranchesAreStrict);
        Run(results, "invalid-once-delivery-state-is-rejected", VerifyInvalidOnceDeliveryStateIsRejected);
        Run(results, "legacy-case-insensitive-values-are-canonicalized", VerifyLegacyCaseInsensitiveValuesAreCanonicalized);
        Run(results, "invalid-settings-are-rejected", VerifyInvalidSettingsAreRejected);
        Run(results, "save-writes-canonical-v8-as-utf8-no-bom", VerifySaveWritesUtf8NoBom);
        Run(results, "failed-write-preserves-existing-settings", VerifyFailedWritePreservesExistingSettings);
        Run(results, "replacement-failure-preserves-existing-settings", VerifyReplacementFailurePreservesExistingSettings);
        Run(results, "settings-mutex-name-matches-powershell-contract", VerifySettingsMutexName);
        if (externalSettingsPath is not null)
        {
            Run(
                results,
                "external-settings-fixture-round-trips-without-mutation",
                () => VerifyExternalSettingsFixture(externalSettingsPath));
        }

        return results;
    }

    private static void VerifySchemaMigration(int version, string json)
    {
        var guidSource = new FixedGuidSource(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var result = SettingsContract.Deserialize(json, guidSource);

        Equal(version, result.SourceSchemaVersion, "来源 schema 版本");
        Equal(version != SettingsSchema.CurrentVersion, result.Migrated, "迁移标志");
        var canonicalJson = SettingsContract.SerializeCanonical(result.Settings);
        using var canonical = JsonDocument.Parse(canonicalJson);
        Equal(8, canonical.RootElement.GetProperty("schemaVersion").GetInt32(), "规范 schema 版本");
        Equal(
            SettingsSchema.ChinaStandardTimeZoneId,
            canonical.RootElement.GetProperty("timeZoneId").GetString(),
            "规范时区");

        var expectedCountdown = version switch
        {
            1 => new ExpectedCountdown(
                "11111111222233334444555555555555",
                "我的倒计时",
                "2030-01-02T03:04:05.0000000+00:00",
                []),
            2 => new ExpectedCountdown(
                "countdown-v2",
                "版本二倒计时",
                "2030-02-03T04:05:06.0000000+00:00",
                []),
            3 => new ExpectedCountdown(
                "countdown-v3",
                "版本三倒计时",
                "2030-03-04T05:06:07.0000000+00:00",
                []),
            4 => new ExpectedCountdown(
                "countdown-v4",
                "版本四倒计时",
                "2030-04-05T06:07:08.0000000+00:00",
                []),
            5 => new ExpectedCountdown(
                "countdown-v5",
                "版本五倒计时",
                "2030-05-06T07:08:09.0000000+00:00",
                ["tag-work"]),
            6 => new ExpectedCountdown(
                "countdown-v6",
                "版本六倒计时",
                "2030-06-07T08:09:10.0000000+00:00",
                []),
            7 => new ExpectedCountdown(
                "countdown-v7",
                "版本七倒计时",
                "2030-07-08T09:10:11.0000000+00:00",
                ["tag-focus"]),
            _ => throw new VerificationException($"测试不支持 schema v{version}。")
        };
        VerifyCountdown(result.Settings.Countdowns.Single(), expectedCountdown, version);
        VerifyPreferences(result.Settings.Preferences, version);
        VerifyNotesAndTags(result.Settings, version);

        var canonicalReload = SettingsContract.Deserialize(canonicalJson);
        Equal(8, canonicalReload.SourceSchemaVersion, $"v{version} canonical reload schema");
        Equal(false, canonicalReload.Migrated, $"v{version} canonical reload 迁移标志");
        VerifyCountdown(canonicalReload.Settings.Countdowns.Single(), expectedCountdown, version);
        VerifyPreferences(canonicalReload.Settings.Preferences, version);
        VerifyNotesAndTags(canonicalReload.Settings, version);
    }

    private static void VerifyCountdown(
        CountdownRecord actual,
        ExpectedCountdown expected,
        int version)
    {
        Equal(expected.Id, actual.Id, $"v{version} 倒计时 ID");
        Equal(expected.Name, actual.Name, $"v{version} 倒计时名称");
        Equal(
            DateTimeOffset.Parse(expected.TargetUtc).ToUniversalTime(),
            actual.TargetUtc,
            $"v{version} 倒计时 UTC 截止时间");
        SequenceEqual(expected.TagIds, actual.TagIds, $"v{version} 倒计时标签关联");
    }

    private static void VerifyPreferences(CountdownPreferences actual, int version)
    {
        var expected = version switch
        {
            1 or 2 => new ExpectedPreferences("Second", 0m, "09:00", "18:00"),
            3 => new ExpectedPreferences("Minute", 12345.67m, "08:30", "17:45"),
            4 => new ExpectedPreferences("Hour", 8800m, "09:00", "18:00"),
            5 => new ExpectedPreferences("Second", 10000m, "09:15", "18:15"),
            6 => new ExpectedPreferences("Day", 15000m, "08:00", "17:00"),
            7 => new ExpectedPreferences("Second", 20000.50m, "09:00", "18:30"),
            _ => throw new VerificationException($"测试不支持 schema v{version}。")
        };
        Equal(expected.DisplayPrecision, actual.DisplayPrecision, $"v{version} 显示精度");
        Equal(expected.MonthlySalary, actual.MonthlySalary, $"v{version} 月薪");
        Equal(TimeSpan.Parse(expected.WorkStart), actual.WorkStart, $"v{version} 上班时间");
        Equal(TimeSpan.Parse(expected.WorkEnd), actual.WorkEnd, $"v{version} 下班时间");
    }

    private static void VerifyNotesAndTags(CountdownSettings actual, int version)
    {
        if (version < 4)
        {
            Equal(0, actual.Notes.Count, $"v{version} 便签数量");
        }
        else
        {
            var note = actual.Notes.Single();
            Equal($"note-v{version}", note.Id, $"v{version} 便签 ID");
            Equal($"版本{ChineseVersionName(version)}便签", note.Text, $"v{version} 便签正文");
            Equal(version == 4, note.IsCompleted, $"v{version} 便签完成状态");
            Equal(version switch
            {
                6 => "2030-06-07",
                7 => "2030-07-08",
                _ => null
            }, note.Date, $"v{version} 便签日期");
            SequenceEqual(version switch
            {
                5 => ["tag-work"],
                6 => ["tag-life"],
                7 => ["tag-focus"],
                _ => []
            }, note.TagIds, $"v{version} 便签标签关联");
            Equal(version == 7 ? 30 : null, note.ReminderIntervalMinutes, $"v{version} 提醒间隔");
            Equal(
                version == 7
                    ? DateTimeOffset.Parse("2030-07-08T01:00:00.0000000+00:00")
                    : null,
                note.ReminderStartUtc,
                $"v{version} 提醒起点");
        }

        if (version < 5)
        {
            Equal(0, actual.Tags.Count, $"v{version} 标签数量");
            return;
        }

        var tag = actual.Tags.Single();
        var expectedTag = version switch
        {
            5 => new TagRecord("tag-work", "工作", "#FF8A70"),
            6 => new TagRecord("tag-life", "生活", null),
            7 => new TagRecord("tag-focus", "专注", "#FFD36B"),
            _ => throw new VerificationException($"测试不支持 schema v{version}。")
        };
        Equal(expectedTag.Id, tag.Id, $"v{version} 标签 ID");
        Equal(expectedTag.Name, tag.Name, $"v{version} 标签名称");
        Equal(expectedTag.Color, tag.Color, $"v{version} 标签颜色");
    }

    private static string ChineseVersionName(int version)
    {
        return version switch
        {
            4 => "四",
            5 => "五",
            6 => "六",
            7 => "七",
            _ => throw new VerificationException($"没有 schema v{version} 的中文名称。")
        };
    }

    private static void VerifyCanonicalVersionSevenIsStable()
    {
        var loaded = SettingsContract.Deserialize(SettingsFixtures.VersionSeven);
        var first = SettingsContract.SerializeCanonical(loaded.Settings);
        var second = SettingsContract.SerializeCanonical(SettingsContract.Deserialize(first).Settings);
        Equal(first, second, "连续规范化结果");

        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Equal(8, root.GetProperty("schemaVersion").GetInt32(), "schemaVersion");
        Equal(1, root.GetProperty("tags").GetArrayLength(), "标签数量");
        Equal(1, root.GetProperty("countdowns").GetArrayLength(), "倒计时数量");
        Equal(1, root.GetProperty("notes").GetArrayLength(), "便签数量");
    }

    private static void VerifyVersionSevenReminderMigration()
    {
        var loaded = SettingsContract.Deserialize(SettingsFixtures.VersionSeven);
        using var canonical = JsonDocument.Parse(SettingsContract.SerializeCanonical(loaded.Settings));
        var root = canonical.RootElement;
        Equal(8, root.GetProperty("schemaVersion").GetInt32(), "v7 提醒迁移后的 schemaVersion");
        var note = root.GetProperty("notes")[0];
        var reminder = note.GetProperty("reminder");
        Equal("repeat", reminder.GetProperty("type").GetString(), "v7 提醒迁移后的模式");
        Equal(
            "2030-07-08T01:30:00.0000000+00:00",
            reminder.GetProperty("firstDueUtc").GetString(),
            "v7 提醒迁移后的首次时间");
        Equal(30, reminder.GetProperty("intervalMinutes").GetInt32(), "v7 提醒迁移后的间隔");
        Equal("版本七便签", note.GetProperty("text").GetString(), "v7 提醒迁移后的正文");
        Equal("2030-07-08", note.GetProperty("date").GetString(), "v7 提醒迁移后的日期");
        Equal("tag-focus", note.GetProperty("tagIds")[0].GetString(), "v7 提醒迁移后的标签");
    }

    private static void VerifyVersionEightReminderBranchesAreStrict()
    {
        const string invalid =
            """
            {
              "schemaVersion": 8,
              "timeZoneId": "China Standard Time",
              "displayPrecision": "Second",
              "monthlySalary": 0,
              "workStart": "09:00",
              "workEnd": "18:00",
              "tags": [],
              "countdowns": [],
              "notes": [
                {
                  "id": "invalid-once",
                  "text": "不应携带重复间隔",
                  "date": "2031-04-06",
                  "isCompleted": false,
                  "tagIds": [],
                  "reminder": {
                    "type": "once",
                    "firstDueUtc": "2031-04-06T01:30:00.0000000+00:00",
                    "delivery": "pending",
                    "intervalMinutes": 30
                  }
                }
              ]
            }
            """;

        Throws<SettingsValidationException>(
            () => SettingsContract.Deserialize(invalid),
            "一次性提醒不能携带重复间隔");
    }

    private static void VerifyLegacyCaseInsensitiveValuesAreCanonicalized()
    {
        var lowercase = SettingsFixtures.VersionSeven
            .Replace("\"timeZoneId\": \"China Standard Time\"", "\"timeZoneId\": \"china standard time\"", StringComparison.Ordinal)
            .Replace("\"displayPrecision\": \"Second\"", "\"displayPrecision\": \"second\"", StringComparison.Ordinal);

        var loaded = SettingsContract.Deserialize(lowercase);
        Equal("Second", loaded.Settings.Preferences.DisplayPrecision, "大小写兼容的显示精度");

        using var canonical = JsonDocument.Parse(SettingsContract.SerializeCanonical(loaded.Settings));
        Equal(
            SettingsSchema.ChinaStandardTimeZoneId,
            canonical.RootElement.GetProperty("timeZoneId").GetString(),
            "规范时区拼写");
        Equal(
            "Second",
            canonical.RootElement.GetProperty("displayPrecision").GetString(),
            "规范显示精度拼写");
    }

    private static void VerifyInvalidSettingsAreRejected()
    {
        var invalidCases = new[]
        {
            ("malformed-json", "{"),
            ("unsupported-schema", """{"schemaVersion":9,"timeZoneId":"China Standard Time"}"""),
            ("wrong-time-zone", """{"schemaVersion":1,"timeZoneId":"UTC","targetUtc":"2030-01-02T03:04:05.0000000+00:00"}"""),
            (
                "duplicate-countdown-id",
                """
                {
                  "schemaVersion": 2,
                  "timeZoneId": "China Standard Time",
                  "countdowns": [
                    {"id":"same","name":"一","targetUtc":"2030-01-02T03:04:05.0000000+00:00"},
                    {"id":"same","name":"二","targetUtc":"2030-01-03T03:04:05.0000000+00:00"}
                  ]
                }
                """),
            (
                "invalid-date",
                SettingsFixtures.VersionSix.Replace("\"2030-06-07\"", "\"2030-02-30\"", StringComparison.Ordinal)),
            (
                "half-reminder",
                SettingsFixtures.VersionSeven.Replace(
                    "\"reminderStartUtc\": \"2030-07-08T01:00:00.0000000+00:00\"",
                    "\"reminderStartUtc\": null",
                    StringComparison.Ordinal))
        };

        foreach (var invalidCase in invalidCases)
        {
            Throws<SettingsValidationException>(
                () => SettingsContract.Deserialize(invalidCase.Item2),
                invalidCase.Item1);
        }
    }

    private static void VerifySaveWritesUtf8NoBom()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var settings = SettingsContract.Deserialize(SettingsFixtures.VersionSeven).Settings;
            new SettingsStore().Save(settingsPath, settings);

            var content = File.ReadAllBytes(settingsPath);
            Equal(false, HasUtf8Bom(content), "UTF-8 BOM");
            Equal(false, File.Exists(settingsPath + ".tmp"), "临时文件残留");
            using var document = JsonDocument.Parse(content);
            Equal(8, document.RootElement.GetProperty("schemaVersion").GetInt32(), "保存后的 schemaVersion");
            Equal("版本七便签", document.RootElement.GetProperty("notes")[0].GetProperty("text").GetString(), "便签正文");
        });
    }

    private static void VerifyInvalidOnceDeliveryStateIsRejected()
    {
        var invalidReminder = new OnceReminder(
            new DateTimeOffset(2030, 1, 2, 3, 4, 0, TimeSpan.Zero),
            (OnceDeliveryState)999);
        var settings = CountdownSettings.Empty with
        {
            Notes =
            [
                new NoteRecord(
                    "invalid-once-delivery",
                    "无效一次性提醒",
                    "2030-01-02",
                    false,
                    Array.Empty<string>(),
                    invalidReminder)
            ]
        };

        Throws<SettingsValidationException>(
            () => SettingsContract.SerializeCanonical(settings),
            "未知一次性提醒送达状态");
    }

    private static void VerifyFailedWritePreservesExistingSettings()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var original = Encoding.UTF8.GetBytes(SettingsContract.SerializeCanonical(
                SettingsContract.Deserialize(SettingsFixtures.VersionSeven).Settings));
            File.WriteAllBytes(settingsPath, original);
            Directory.CreateDirectory(settingsPath + ".tmp");

            ThrowsAny<IOException, UnauthorizedAccessException>(
                () => new SettingsStore().Save(settingsPath, CountdownSettings.Empty),
                "注入的设置写入失败");

            var afterFailure = File.ReadAllBytes(settingsPath);
            Equal(true, original.AsSpan().SequenceEqual(afterFailure), "写入失败后的旧设置字节");
        });
    }

    private static void VerifyReplacementFailurePreservesExistingSettings()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var original = Encoding.UTF8.GetBytes(SettingsContract.SerializeCanonical(
                SettingsContract.Deserialize(SettingsFixtures.VersionSeven).Settings));
            File.WriteAllBytes(settingsPath, original);
            File.SetAttributes(settingsPath, FileAttributes.ReadOnly);
            try
            {
                ThrowsAny<IOException, UnauthorizedAccessException>(
                    () => new SettingsStore().Save(settingsPath, CountdownSettings.Empty),
                    "注入的设置替换失败");

                var afterFailure = File.ReadAllBytes(settingsPath);
                Equal(true, original.AsSpan().SequenceEqual(afterFailure), "替换失败后的旧设置字节");
            }
            finally
            {
                File.SetAttributes(settingsPath, FileAttributes.Normal);
                var temporaryPath = settingsPath + ".tmp";
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        });
    }

    private static void VerifySettingsMutexName()
    {
        const string settingsPath = @"C:\Users\Test User\AppData\Local\MinminCountdown\settings.json";
        const string expected =
            @"Global\MinminCountdownSettings-3F01BA10B97E42E0489BF1AEE48064906541B7A4371C2A3BD324ADEE7D70A538";
        Equal(expected, SettingsMutex.GetWriteMutexName(settingsPath), "设置写入互斥锁名称");
    }

    private static void VerifyExternalSettingsFixture(string settingsPath)
    {
        var fullPath = Path.GetFullPath(settingsPath);
        var original = File.ReadAllBytes(fullPath);
        var store = new SettingsStore(new FixedGuidSource(Guid.Parse("11111111-2222-3333-4444-555555555555")));
        var loaded = store.Load(fullPath);
        var canonical = store.Serialize(loaded.Settings);
        var reloaded = SettingsContract.Deserialize(canonical);
        Equal(canonical, store.Serialize(reloaded.Settings), "外部 fixture canonical round-trip");
        Equal(true, original.AsSpan().SequenceEqual(File.ReadAllBytes(fullPath)), "外部 fixture 字节");
    }

    private static bool HasUtf8Bom(ReadOnlySpan<byte> content)
    {
        return content.Length >= 3
            && content[0] == 0xEF
            && content[1] == 0xBB
            && content[2] == 0xBF;
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "verification-temp",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void Run(
        ICollection<VerificationResult> results,
        string name,
        Action verification)
    {
        try
        {
            verification();
            results.Add(new VerificationResult(name, true, null));
        }
        catch (Exception exception)
        {
            results.Add(new VerificationResult(name, false, exception.Message));
        }
    }

    private static void Equal<T>(T expected, T actual, string subject)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new VerificationException(
                $"{subject}不匹配；期望 {Format(expected)}，实际 {Format(actual)}。");
        }
    }

    private static void SequenceEqual<T>(
        IReadOnlyList<T> expected,
        IReadOnlyList<T> actual,
        string subject)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new VerificationException(
                $"{subject}不匹配；期望 [{string.Join(",", expected)}]，实际 [{string.Join(",", actual)}]。");
        }
    }

    private static void Throws<TException>(Action action, string subject)
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
        catch (Exception exception)
        {
            throw new VerificationException(
                $"{subject} 抛出了 {exception.GetType().Name}，期望 {typeof(TException).Name}。");
        }

        throw new VerificationException($"{subject} 未拒绝非法设置。");
    }

    private static void ThrowsAny<TFirst, TSecond>(Action action, string subject)
        where TFirst : Exception
        where TSecond : Exception
    {
        try
        {
            action();
        }
        catch (TFirst)
        {
            return;
        }
        catch (TSecond)
        {
            return;
        }
        catch (Exception exception)
        {
            throw new VerificationException(
                $"{subject} 抛出了 {exception.GetType().Name}，期望 {typeof(TFirst).Name} 或 {typeof(TSecond).Name}。");
        }

        throw new VerificationException($"{subject} 未触发预期失败。");
    }

    private static string Format<T>(T value)
    {
        return value?.ToString() ?? "<null>";
    }
}

internal sealed record VerificationResult(string Name, bool Success, string? Error);

internal sealed record ExpectedCountdown(
    string Id,
    string Name,
    string TargetUtc,
    IReadOnlyList<string> TagIds);

internal sealed record ExpectedPreferences(
    string DisplayPrecision,
    decimal MonthlySalary,
    string WorkStart,
    string WorkEnd);

internal sealed class FixedGuidSource : IGuidSource
{
    private readonly Guid _value;

    public FixedGuidSource(Guid value)
    {
        _value = value;
    }

    public Guid NewGuid() => _value;
}

internal sealed class VerificationException : Exception
{
    public VerificationException(string message)
        : base(message)
    {
    }
}
