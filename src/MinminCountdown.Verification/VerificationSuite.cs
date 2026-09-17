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
                $"schema-v{fixture.Version}-migrates-to-canonical-v10",
                () => VerifySchemaMigration(fixture.Version, fixture.Json));
        }

        Run(results, "legacy-theme-preference-defaults-to-auto-and-v9-is-preserved", VerifyLegacyThemePreferenceDefaultsToAuto);
        Run(results, "any-theme-preference-value-loads-without-error", VerifyAnyThemePreferenceValueLoadsWithoutError);
        Run(results, "canonical-v10-is-stable", VerifyCanonicalVersionSevenIsStable);
        Run(results, "schema-v7-reminder-migrates-to-v10-fixed-interval", VerifyVersionSevenReminderMigration);
        Run(results, "schema-v8-reminder-branches-reject-foreign-fields", VerifyVersionEightReminderBranchesAreStrict);
        Run(results, "schema-v9-reminders-migrate-to-stable-v10-rules", VerifyVersionNineReminderMigration);
        Run(results, "schema-v10-multiple-rules-round-trip-stably", VerifyVersionTenRoundTrip);
        Run(results, "schema-v10-countdown-relative-rules-round-trip-at-bounds", VerifyCountdownRelativeRulesRoundTrip);
        Run(results, "schema-v10-calendar-rules-round-trip-as-typed-subset", VerifyCalendarRulesRoundTrip);
        Run(results, "ical-net-calendar-occurrences-use-beijing-time-and-collapse-missed-runs", VerifyCalendarOccurrences);
        Run(results, "calendar-rules-reject-unsupported-shapes-and-misaligned-cursors", VerifyCalendarRulesAreStrict);
        Run(results, "schema-v10-disposition-and-snooze-round-trip-stably", VerifyVersionTenDispositionAndSnoozeRoundTrip);
        Run(results, "schema-v9-repeat-migration-uses-injected-clock", VerifyVersionNineRepeatMigrationClock);
        Run(results, "v10-rule-and-state-negative-cases-are-rejected", VerifyVersionTenStrictNegativeCases);
        Run(results, "v10-cursors-align-with-rule-occurrences", VerifyVersionTenCursorsAlignWithOccurrences);
        Run(results, "v10-interval-json-numbers-are-strict", VerifyVersionTenIntervalJsonNumbersAreStrict);
        Run(results, "occurrence-key-round-trips-canonically", VerifyOccurrenceKeyRoundTrip);
        Run(results, "absolute-once-cursor-before-schedule-is-rejected", VerifyAbsoluteOnceCursorBeforeScheduleIsRejected);
        Run(results, "dangling-reminder-state-is-rejected", VerifyDanglingReminderStateIsRejected);
        Run(results, "legacy-case-insensitive-values-are-canonicalized", VerifyLegacyCaseInsensitiveValuesAreCanonicalized);
        Run(results, "invalid-settings-are-rejected", VerifyInvalidSettingsAreRejected);
        Run(results, "save-writes-canonical-v10-as-utf8-no-bom", VerifySaveWritesUtf8NoBom);
        Run(results, "save-keeps-two-prior-legal-settings-generations", VerifySaveKeepsTwoPriorLegalGenerations);
        Run(results, "save-history-preflight-does-not-consume-domain-clock", VerifySaveHistoryPreflightDoesNotConsumeDomainClock);
        Run(results, "startup-prefers-missing-or-strict-valid-main", VerifyStartupPrefersMissingOrStrictValidMain);
        Run(results, "startup-occupied-settings-path-never-starts-empty", VerifyStartupOccupiedSettingsPathNeverStartsEmpty);
        Run(results, "startup-recovery-writes-require-explicit-authorization", VerifyStartupRecoveryWritesRequireExplicitAuthorization);
        Run(results, "startup-restores-latest-legal-backup-after-confirmation", VerifyStartupRestoresLatestLegalBackupAfterConfirmation);
        Run(results, "startup-readback-failure-rolls-back-recovery", VerifyStartupReadbackFailureRollsBackRecovery);
        Run(results, "startup-failed-readback-and-rollback-preserves-original-main-artifact", VerifyStartupFailedReadbackAndRollbackPreservesOriginalMainArtifact);
        Run(results, "startup-main-disappearance-after-prompt-never-starts-empty", VerifyStartupMainDisappearanceAfterPromptNeverStartsEmpty);
        Run(results, "startup-refusal-or-no-backup-never-writes", VerifyStartupRefusalOrNoBackupNeverWrites);
        Run(results, "startup-readonly-or-unreadable-main-never-overwrites", VerifyStartupReadOnlyOrUnreadableMainNeverOverwrites);
        Run(results, "startup-acl-denied-main-enters-backup-recovery", VerifyStartupAclDeniedMainEntersBackupRecovery);
        Run(results, "startup-real-filesystem-acl-denial-enters-recovery", VerifyStartupRealFilesystemAclDenialEntersRecovery);
        Run(results, "invalid-main-never-enters-backup-history", VerifyInvalidMainNeverEntersBackupHistory);
        Run(results, "replacement-failure-preserves-main-and-backup-generations", VerifyReplacementFailurePreservesMainAndBackupGenerations);
        Run(results, "failed-backup-rollback-preserves-artifact-and-blocks-next-save", VerifyFailedBackupRollbackPreservesArtifactAndBlocksNextSave);
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
        var result = SettingsContract.Deserialize(
            json,
            guidSource,
            migrationUtc: new DateTimeOffset(2031, 9, 10, 2, 16, 0, TimeSpan.Zero));

        Equal(version, result.SourceSchemaVersion, "来源 schema 版本");
        Equal(version != SettingsSchema.CurrentVersion, result.Migrated, "迁移标志");
        var canonicalJson = SettingsContract.SerializeCanonical(result.Settings);
        using var canonical = JsonDocument.Parse(canonicalJson);
        Equal(10, canonical.RootElement.GetProperty("schemaVersion").GetInt32(), "规范 schema 版本");
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
            8 => new ExpectedCountdown(
                "countdown-v8",
                "版本八倒计时",
                "2030-08-09T10:11:12.0000000+00:00",
                ["tag-focus"]),
            9 => new ExpectedCountdown(
                "countdown-v9",
                "版本九倒计时",
                "2031-09-10T11:12:13.0000000+00:00",
                []),
            _ => throw new VerificationException($"测试不支持 schema v{version}。")
        };
        VerifyCountdown(result.Settings.Countdowns.Single(), expectedCountdown, version);
        VerifyPreferences(result.Settings.Preferences, version);
        VerifyNotesAndTags(result.Settings, version);

        var canonicalReload = SettingsContract.Deserialize(canonicalJson);
        Equal(10, canonicalReload.SourceSchemaVersion, $"v{version} canonical reload schema");
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
            8 or 9 => new ExpectedPreferences("Second", 20000.50m, "09:00", "18:30"),
            _ => throw new VerificationException($"测试不支持 schema v{version}。")
        };
        Equal(expected.DisplayPrecision, actual.DisplayPrecision, $"v{version} 显示精度");
        Equal(expected.MonthlySalary, actual.MonthlySalary, $"v{version} 月薪");
        Equal(TimeSpan.Parse(expected.WorkStart), actual.WorkStart, $"v{version} 上班时间");
        Equal(TimeSpan.Parse(expected.WorkEnd), actual.WorkEnd, $"v{version} 下班时间");
    }

    private static void VerifyNotesAndTags(CountdownSettings actual, int version)
    {
        if (version == 9)
        {
            Equal(4, actual.Notes.Count, "v9 便签数量");
            SequenceEqual(
                ["note-v9-none", "note-v9-pending", "note-v9-delivered", "note-v9-repeat"],
                actual.Notes.Select(static note => note.Id).ToArray(),
                "v9 便签 ID");
            Equal(true, actual.Notes.All(static note => note.TagIds.Count == 0), "v9 便签空标签关联");
            Equal(0, actual.Tags.Count, "v9 空标签库");
            return;
        }

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
                8 => "2030-08-09",
                _ => null
            }, note.Date, $"v{version} 便签日期");
            SequenceEqual(version switch
            {
                5 => ["tag-work"],
                6 => ["tag-life"],
                7 or 8 => ["tag-focus"],
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
            7 or 8 => new TagRecord("tag-focus", "专注", "#FFD36B"),
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
            8 => "八",
            _ => throw new VerificationException($"没有 schema v{version} 的中文名称。")
        };
    }

    private static void VerifyLegacyThemePreferenceDefaultsToAuto()
    {
        foreach (var fixture in SettingsFixtures.AllVersions)
        {
            var loaded = SettingsContract.Deserialize(fixture.Json);
            var expected = fixture.Version == 9 ? ThemePreferenceMode.Night : ThemePreferenceMode.Auto;
            Equal(expected, loaded.Settings.Preferences.ThemePreference, $"v{fixture.Version} 主题偏好");
            using var canonical = JsonDocument.Parse(SettingsContract.SerializeCanonical(loaded.Settings));
            Equal(10, canonical.RootElement.GetProperty("schemaVersion").GetInt32(), $"v{fixture.Version} 规范 schema");
            Equal(expected.ToString(), canonical.RootElement.GetProperty("themePreference").GetString(), $"v{fixture.Version} 规范主题偏好");
        }
    }

    private static void VerifyAnyThemePreferenceValueLoadsWithoutError()
    {
        // 主题迁移：Auto/Night 及未知值都按日间解释，启动不报错、不丢数据。
        var settings = SettingsFixtures.VersionTen;
        foreach (var preference in new[] { "Auto", "Day", "Night", "Nightfall", "" })
        {
            var json = SetThemePreference(settings, preference);
            var loaded = SettingsContract.Deserialize(json);
            var canonical = SettingsContract.SerializeCanonical(loaded.Settings);
            var reloaded = SettingsContract.Deserialize(canonical);
            Equal(
                SettingsContract.SerializeCanonical(reloaded.Settings),
                canonical,
                $"主题偏好 {preference} 规范化结果稳定");
        }
    }

    private static string SetThemePreference(string json, string preference)
    {
        using var document = JsonDocument.Parse(json);
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "themePreference", StringComparison.Ordinal))
                {
                    writer.WriteString("themePreference", preference);
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void VerifyCanonicalVersionSevenIsStable()
    {
        var loaded = SettingsContract.Deserialize(SettingsFixtures.VersionSeven);
        var first = SettingsContract.SerializeCanonical(loaded.Settings);
        var second = SettingsContract.SerializeCanonical(SettingsContract.Deserialize(first).Settings);
        Equal(first, second, "连续规范化结果");

        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Equal(10, root.GetProperty("schemaVersion").GetInt32(), "schemaVersion");
        Equal(1, root.GetProperty("tags").GetArrayLength(), "标签数量");
        Equal(1, root.GetProperty("countdowns").GetArrayLength(), "倒计时数量");
        Equal(1, root.GetProperty("notes").GetArrayLength(), "便签数量");
    }

    private static void VerifyVersionSevenReminderMigration()
    {
        var loaded = SettingsContract.Deserialize(SettingsFixtures.VersionSeven);
        using var canonical = JsonDocument.Parse(SettingsContract.SerializeCanonical(loaded.Settings));
        var root = canonical.RootElement;
        Equal(10, root.GetProperty("schemaVersion").GetInt32(), "v7 提醒迁移后的 schemaVersion");
        var note = root.GetProperty("notes")[0];
        var reminder = note.GetProperty("reminderRules")[0];
        Equal("fixedInterval", reminder.GetProperty("type").GetString(), "v7 提醒迁移后的模式");
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

    private static void VerifyVersionNineReminderMigration()
    {
        var first = SettingsContract.SerializeCanonical(
            SettingsContract.Deserialize(SettingsFixtures.VersionNine).Settings);
        var second = SettingsContract.SerializeCanonical(
            SettingsContract.Deserialize(SettingsFixtures.VersionNine).Settings);
        Equal(first, second, "v9 重试迁移规范结果");

        using var canonical = JsonDocument.Parse(first);
        var root = canonical.RootElement;
        Equal(10, root.GetProperty("schemaVersion").GetInt32(), "v9 迁移后的 schemaVersion");
        Equal(0, root.GetProperty("countdowns")[0].GetProperty("reminderRules").GetArrayLength(), "倒计时空规则");
        var notes = root.GetProperty("notes");
        Equal(0, notes[0].GetProperty("reminderRules").GetArrayLength(), "none 迁移为空规则");
        Equal("absoluteOnce", notes[1].GetProperty("reminderRules")[0].GetProperty("type").GetString(), "pending once 规则类型");
        Equal("absoluteOnce", notes[2].GetProperty("reminderRules")[0].GetProperty("type").GetString(), "delivered once 规则类型");
        Equal("fixedInterval", notes[3].GetProperty("reminderRules")[0].GetProperty("type").GetString(), "repeat 规则类型");

        var pendingRuleId = notes[1].GetProperty("reminderRules")[0].GetProperty("id").GetString()!;
        var deliveredRuleId = notes[2].GetProperty("reminderRules")[0].GetProperty("id").GetString()!;
        var states = root.GetProperty("reminderStates");
        Equal(false, states.TryGetProperty(pendingRuleId, out _), "pending once 不创建游标");
        var deliveredState = states.GetProperty(deliveredRuleId);
        Equal(
            "2031-09-10T02:30:00.0000000+00:00",
            deliveredState.GetProperty("handledThroughUtc").GetString(),
            "delivered once 游标");
        Equal(
            $"{deliveredRuleId}|2031-09-10T02:30:00.0000000+00:00",
            deliveredState.GetProperty("lastDeliveredOccurrenceKey").GetString(),
            "delivered once occurrence key");
        Equal(
            JsonValueKind.Null,
            deliveredState.GetProperty("lastDispositionOccurrenceKey").ValueKind,
            "v9 delivered once migration does not invent disposition");
        Equal(
            JsonValueKind.Null,
            deliveredState.GetProperty("activeSnooze").ValueKind,
            "v9 delivered once migration does not invent snooze");
    }

    private static void VerifyVersionTenRoundTrip()
    {
        var loaded = SettingsContract.Deserialize(SettingsFixtures.VersionTen);
        Equal(10, loaded.SourceSchemaVersion, "v10 来源 schema");
        Equal(false, loaded.Migrated, "v10 不迁移");
        Equal(2, loaded.Settings.Countdowns.Single().ReminderRules.Count, "倒计时规则数量");
        Equal(2, loaded.Settings.Notes.Single().ReminderRules.Count, "便签规则数量");
        Equal(2, loaded.Settings.ReminderStates.Count, "规则状态数量");
        Equal(
            "rule-note-once|2032-10-11T01:30:00.0000000+00:00",
            loaded.Settings.ReminderStates["rule-note-once"].LastDispositionOccurrenceKey,
            "v10 处置 occurrence 往返");

        var first = SettingsContract.SerializeCanonical(loaded.Settings);
        var second = SettingsContract.SerializeCanonical(SettingsContract.Deserialize(first).Settings);
        Equal(first, second, "v10 多规则规范往返");
    }

    private static void VerifyCountdownRelativeRulesRoundTrip()
    {
        const string json =
            """
            {
              "schemaVersion": 10,
              "timeZoneId": "China Standard Time",
              "displayPrecision": "Second",
              "monthlySalary": 0,
              "workStart": "09:00",
              "workEnd": "18:00",
              "themePreference": "Auto",
              "tags": [],
              "countdowns": [
                {
                  "id": "countdown-relative",
                  "name": "相对提醒边界",
                  "targetUtc": "2040-01-01T00:00:00.0000000+00:00",
                  "tagIds": [],
                  "reminderRules": [
                    {
                      "id": "relative-at-target",
                      "type": "countdownRelative",
                      "offsetMinutesBefore": 0
                    },
                    {
                      "id": "relative-at-maximum",
                      "type": "countdownRelative",
                      "offsetMinutesBefore": 5256000
                    }
                  ]
                }
              ],
              "notes": [],
              "reminderStates": {}
            }
            """;

        var loaded = SettingsContract.Deserialize(json).Settings;
        var rules = loaded.Countdowns.Single().ReminderRules;
        Equal(0, ((CountdownRelativeReminderRule)rules[0]).OffsetMinutesBefore, "相对规则到点边界");
        Equal(5_256_000, ((CountdownRelativeReminderRule)rules[1]).OffsetMinutesBefore, "相对规则最大提前边界");

        var first = SettingsContract.SerializeCanonical(loaded);
        var second = SettingsContract.SerializeCanonical(SettingsContract.Deserialize(first).Settings);
        Equal(first, second, "倒计时相对规则规范往返");
        using var canonical = JsonDocument.Parse(first);
        var firstRule = canonical.RootElement.GetProperty("countdowns")[0].GetProperty("reminderRules")[0];
        Equal("countdownRelative", firstRule.GetProperty("type").GetString(), "相对规则规范类型");
        Equal(false, firstRule.TryGetProperty("scheduledUtc", out _), "相对规则不缓存派生 UTC");
    }

    private static void VerifyCalendarRulesRoundTrip()
    {
        const string json =
            """
            {
              "schemaVersion": 10,
              "timeZoneId": "China Standard Time",
              "displayPrecision": "Second",
              "monthlySalary": 0,
              "workStart": "09:00",
              "workEnd": "18:00",
              "themePreference": "Auto",
              "tags": [],
              "countdowns": [],
              "notes": [
                {
                  "id": "calendar-note",
                  "text": "日历规则",
                  "date": "2032-01-01",
                  "isCompleted": false,
                  "tagIds": [],
                  "reminderRules": [
                    {
                      "id": "calendar-daily",
                      "type": "calendar",
                      "frequency": "daily",
                      "interval": 999,
                      "startDate": "2032-01-01",
                      "localTime": "09:05"
                    },
                    {
                      "id": "calendar-weekly",
                      "type": "calendar",
                      "frequency": "weekly",
                      "interval": 2,
                      "startDate": "2032-01-01",
                      "localTime": "10:10",
                      "weekdays": ["monday", "friday"]
                    },
                    {
                      "id": "calendar-monthly-last",
                      "type": "calendar",
                      "frequency": "monthly",
                      "interval": 1,
                      "startDate": "2032-01-01",
                      "localTime": "11:15",
                      "lastDay": true
                    },
                    {
                      "id": "calendar-yearly-leap",
                      "type": "calendar",
                      "frequency": "yearly",
                      "interval": 4,
                      "startDate": "2032-01-01",
                      "localTime": "12:20",
                      "month": 2,
                      "day": 29
                    }
                  ]
                }
              ],
              "reminderStates": {}
            }
            """;

        var loaded = SettingsContract.Deserialize(json).Settings;
        var rules = loaded.Notes.Single().ReminderRules.Cast<CalendarReminderRule>().ToArray();
        Equal(CalendarFrequency.Daily, rules[0].Frequency, "日规则频率");
        Equal(999, rules[0].Interval, "日规则最大间隔");
        Equal(new DateOnly(2032, 1, 1), rules[0].StartDate, "日规则北京时间起始日期");
        Equal(new TimeOnly(9, 5), rules[0].LocalTime, "日规则本地时间");
        SequenceEqual(
            new[] { DayOfWeek.Monday, DayOfWeek.Friday },
            ((WeeklyCalendarSelector)rules[1].Selector).Weekdays,
            "周规则星期选择器");
        Equal(true, ((MonthlyCalendarSelector)rules[2].Selector).LastDay, "月规则显式最后一天");
        Equal(2, ((YearlyCalendarSelector)rules[3].Selector).Month, "年规则月份选择器");
        Equal(29, ((YearlyCalendarSelector)rules[3].Selector).Day, "年规则日期选择器");

        var canonical = SettingsContract.SerializeCanonical(loaded);
        Equal(canonical, SettingsContract.SerializeCanonical(SettingsContract.Deserialize(canonical).Settings),
            "日历规则规范往返");
        using var document = JsonDocument.Parse(canonical);
        var serialized = document.RootElement.GetProperty("notes")[0].GetProperty("reminderRules");
        Equal("daily", serialized[0].GetProperty("frequency").GetString(), "规范日频率");
        Equal(false, serialized[0].TryGetProperty("weekdays", out _), "日规则不写其他选择器");
        Equal(true, serialized[2].GetProperty("lastDay").GetBoolean(), "规范显式最后一天");
        Equal(false, serialized[2].TryGetProperty("day", out _), "月末规则不写日期");
    }

    private static void VerifyCalendarOccurrences()
    {
        var daily = new CalendarReminderRule(
            "daily",
            CalendarFrequency.Daily,
            2,
            new DateOnly(2032, 1, 1),
            new TimeOnly(9, 5),
            new DailyCalendarSelector());
        Equal(
            new DateTimeOffset(2032, 1, 7, 1, 5, 0, TimeSpan.Zero),
            CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                daily,
                afterExclusiveUtc: null,
                new DateTimeOffset(2032, 1, 8, 2, 0, 0, TimeSpan.Zero)),
            "两天间隔按北京时间计算并合并历史 occurrence");
        Equal(
            new DateTimeOffset(2032, 1, 7, 1, 5, 0, TimeSpan.Zero),
            CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                daily,
                new DateTimeOffset(2032, 1, 5, 1, 5, 0, TimeSpan.Zero),
                new DateTimeOffset(2032, 1, 8, 2, 0, 0, TimeSpan.Zero)),
            "游标之后只返回最新 occurrence");

        var weekly = new CalendarReminderRule(
            "weekly",
            CalendarFrequency.Weekly,
            2,
            new DateOnly(2032, 1, 5),
            new TimeOnly(10, 10),
            new WeeklyCalendarSelector([DayOfWeek.Monday, DayOfWeek.Friday]));
        Equal(
            new DateTimeOffset(2032, 1, 23, 2, 10, 0, TimeSpan.Zero),
            CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                weekly,
                afterExclusiveUtc: null,
                new DateTimeOffset(2032, 1, 31, 0, 0, 0, TimeSpan.Zero)),
            "双周多星期选择器");

        var monthlyThirtyFirst = new CalendarReminderRule(
            "monthly-31",
            CalendarFrequency.Monthly,
            1,
            new DateOnly(2031, 1, 1),
            new TimeOnly(8, 0),
            new MonthlyCalendarSelector(31, false));
        Equal(
            new DateTimeOffset(2031, 1, 31, 0, 0, 0, TimeSpan.Zero),
            CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                monthlyThirtyFirst,
                afterExclusiveUtc: null,
                new DateTimeOffset(2031, 3, 15, 0, 0, 0, TimeSpan.Zero)),
            "不存在的二月三十一日被跳过而不夹取");

        var monthlyLastDay = monthlyThirtyFirst with
        {
            Id = "monthly-last",
            StartDate = new DateOnly(2032, 1, 1),
            Selector = new MonthlyCalendarSelector(null, true)
        };
        Equal(
            new DateTimeOffset(2032, 2, 29, 0, 0, 0, TimeSpan.Zero),
            CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                monthlyLastDay,
                afterExclusiveUtc: null,
                new DateTimeOffset(2032, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            "显式月末覆盖闰年二月");

        var yearlyLeapDay = new CalendarReminderRule(
            "yearly-leap",
            CalendarFrequency.Yearly,
            1,
            new DateOnly(2027, 1, 1),
            new TimeOnly(9, 30),
            new YearlyCalendarSelector(2, 29, false));
        Equal(
            new DateTimeOffset(2028, 2, 29, 1, 30, 0, TimeSpan.Zero),
            CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                yearlyLeapDay,
                afterExclusiveUtc: null,
                new DateTimeOffset(2031, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            "按年闰日跳过不存在年份");

        var yearlyLastDay = yearlyLeapDay with
        {
            Id = "yearly-last",
            Selector = new YearlyCalendarSelector(2, null, true)
        };
        Equal(
            new DateTimeOffset(2029, 2, 28, 1, 30, 0, TimeSpan.Zero),
            CalendarOccurrenceCalculator.GetLatestOccurrenceUtc(
                yearlyLastDay,
                afterExclusiveUtc: null,
                new DateTimeOffset(2029, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            "按年显式月末覆盖平年二月");
        Equal(
            true,
            CalendarOccurrenceCalculator.IsOccurrence(
                yearlyLastDay,
                new DateTimeOffset(2028, 2, 29, 1, 30, 0, TimeSpan.Zero)),
            "规范 UTC occurrence 属于日历规则");
    }

    private static void VerifyCalendarRulesAreStrict()
    {
        var basis = SettingsContract.Deserialize(SettingsFixtures.VersionTen).Settings;
        var rule = new CalendarReminderRule(
            "calendar-cursor",
            CalendarFrequency.Monthly,
            1,
            new DateOnly(2031, 1, 1),
            new TimeOnly(8, 0),
            new MonthlyCalendarSelector(31, false));
        var note = basis.Notes.Single() with { ReminderRules = [rule] };
        var validCursor = new DateTimeOffset(2031, 3, 31, 0, 0, 0, TimeSpan.Zero);
        var valid = basis with
        {
            Countdowns = [],
            Notes = [note],
            ReminderStates = new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase)
            {
                [rule.Id] = new(rule.Id, validCursor, null)
            }
        };
        _ = SettingsContract.SerializeCanonical(valid);

        var clippedCursor = new DateTimeOffset(2031, 2, 28, 0, 0, 0, TimeSpan.Zero);
        var invalid = valid with
        {
            ReminderStates = new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase)
            {
                [rule.Id] = new(rule.Id, clippedCursor, null)
            }
        };
        Throws<SettingsValidationException>(
            () => SettingsContract.SerializeCanonical(invalid),
            "不存在日期不能夹取为月末 cursor");

        var canonical = SettingsContract.SerializeCanonical(valid);
        var invalidJsonCases = new[]
        {
            canonical.Replace("\"frequency\": \"monthly\"", "\"frequency\": \"hourly\"", StringComparison.Ordinal),
            canonical.Replace("\"interval\": 1", "\"interval\": 0", StringComparison.Ordinal),
            canonical.Replace("\"interval\": 1", "\"interval\": 1000", StringComparison.Ordinal),
            canonical.Replace("\"localTime\": \"08:00\"", "\"localTime\": \"08:00\",\n          \"count\": 2", StringComparison.Ordinal),
            canonical.Replace("\"day\": 31", "\"day\": 31,\n          \"lastDay\": true", StringComparison.Ordinal)
        };
        foreach (var invalidJson in invalidJsonCases)
        {
            Throws<SettingsValidationException>(
                () => SettingsContract.Deserialize(invalidJson),
                "日历规则严格类型化子集");
        }

        var weeklyEmpty = valid with
        {
            Notes =
            [
                note with
                {
                    ReminderRules =
                    [
                        rule with
                        {
                            Frequency = CalendarFrequency.Weekly,
                            Selector = new WeeklyCalendarSelector([])
                        }
                    ]
                }
            ],
            ReminderStates = new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase)
        };
        Throws<SettingsValidationException>(
            () => SettingsContract.SerializeCanonical(weeklyEmpty),
            "按周规则至少选择一个星期");
    }

    private static void VerifyVersionTenDispositionAndSnoozeRoundTrip()
    {
        var settings = SettingsContract.Deserialize(SettingsFixtures.VersionTen).Settings;
        var rule = settings.Notes.Single().ReminderRules.OfType<FixedIntervalReminderRule>().Single();
        var scheduledUtc = rule.FirstDueUtc.AddHours(2);
        var occurrenceKey = ReminderOccurrenceKey.Create(rule, scheduledUtc).ToString();
        var snoozeUntilUtc = scheduledUtc.AddMinutes(30);
        var states = settings.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        states[rule.Id] = new ReminderRuleState(
            rule.Id,
            scheduledUtc,
            occurrenceKey,
            LastDispositionOccurrenceKey: null,
            new ReminderSnoozeState(occurrenceKey, scheduledUtc, snoozeUntilUtc));

        var canonical = SettingsContract.SerializeCanonical(settings with { ReminderStates = states });
        var reloaded = SettingsContract.Deserialize(canonical).Settings.ReminderStates[rule.Id];
        Equal(null, reloaded.LastDispositionOccurrenceKey, "活动稍后 occurrence 尚未处置");
        Equal(occurrenceKey, reloaded.ActiveSnooze?.OccurrenceKey, "活动稍后 occurrence key");
        Equal(scheduledUtc, reloaded.ActiveSnooze?.OriginalScheduledUtc, "活动稍后原计划 UTC");
        Equal(snoozeUntilUtc, reloaded.ActiveSnooze?.SnoozeUntilUtc, "活动稍后覆盖 UTC");
    }

    private static void VerifyVersionNineRepeatMigrationClock()
    {
        var migrationUtc = new DateTimeOffset(2031, 9, 10, 2, 16, 0, TimeSpan.Zero);
        var first = SettingsContract.Deserialize(
            SettingsFixtures.VersionNine,
            clock: new FixedClock(migrationUtc));
        var second = SettingsContract.Deserialize(
            SettingsFixtures.VersionNine,
            clock: new FixedClock(migrationUtc));
        var repeatRule = (FixedIntervalReminderRule)first.Settings.Notes[3].ReminderRules.Single();
        Equal(
            "legacy-d71c35d11bb6b27f66e7c582ef71e036a4f36314a86250d45318b5d32763ca75",
            repeatRule.Id,
            "repeat 稳定迁移 ID");
        Equal(
            repeatRule.Id,
            second.Settings.Notes[3].ReminderRules.Single().Id,
            "失败重试迁移 ID");
        Equal(
            new DateTimeOffset(2031, 9, 10, 2, 0, 0, TimeSpan.Zero),
            first.Settings.ReminderStates[repeatRule.Id].HandledThroughUtc,
            "repeat 最新已开始周期 cursor");
        Equal(
            null,
            first.Settings.ReminderStates[repeatRule.Id].LastDeliveredOccurrenceKey,
            "repeat 迁移不伪造送达 occurrence");
    }

    private static void VerifyVersionTenStrictNegativeCases()
    {
        var cases = new[]
        {
            (
                "duplicate-global-rule-id",
                SettingsFixtures.VersionTen.Replace(
                    "\"id\": \"rule-countdown-once\"",
                    "\"id\": \"rule-note-once\"",
                    StringComparison.Ordinal)),
            (
                "unknown-rule-branch",
                SettingsFixtures.VersionTen.Replace(
                    "\"type\": \"absoluteOnce\"",
                    "\"type\": \"calendar\"",
                    StringComparison.Ordinal)),
            (
                "dangling-state",
                SettingsFixtures.VersionTen.Replace(
                    "\"rule-note-once\": {",
                    "\"missing-rule\": {",
                    StringComparison.Ordinal)),
            (
                "occurrence-after-cursor",
                SettingsFixtures.VersionTen.Replace(
                    "\"handledThroughUtc\": \"2032-10-11T03:00:00.0000000+00:00\"",
                    "\"handledThroughUtc\": \"2032-10-11T02:30:00.0000000+00:00\"",
                    StringComparison.Ordinal)),
            (
                "missing-disposition-field",
                SettingsFixtures.VersionTen.Replace(
                    "\"lastDispositionOccurrenceKey\":",
                    "\"unexpectedDispositionKey\":",
                    StringComparison.Ordinal)),
            (
                "disposition-for-different-rule",
                SettingsFixtures.VersionTen.Replace(
                    "\"lastDispositionOccurrenceKey\": \"rule-note-once|2032-10-11T01:30:00.0000000+00:00\"",
                    "\"lastDispositionOccurrenceKey\": \"rule-note-repeat|2032-10-11T01:30:00.0000000+00:00\"",
                    StringComparison.Ordinal)),
            (
                "disposition-non-utc",
                SettingsFixtures.VersionTen.Replace(
                    "\"lastDispositionOccurrenceKey\": \"rule-note-once|2032-10-11T01:30:00.0000000+00:00\"",
                    "\"lastDispositionOccurrenceKey\": \"rule-note-once|2032-10-11T09:30:00.0000000+08:00\"",
                    StringComparison.Ordinal)),
            (
                "disposition-after-last-delivery",
                SettingsFixtures.VersionTen
                    .Replace(
                        "\"handledThroughUtc\": \"2032-10-11T03:00:00.0000000+00:00\"",
                        "\"handledThroughUtc\": \"2032-10-11T03:30:00.0000000+00:00\"",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"lastDispositionOccurrenceKey\": null",
                        "\"lastDispositionOccurrenceKey\": \"rule-note-repeat|2032-10-11T03:30:00.0000000+00:00\"",
                        StringComparison.Ordinal))
        };

        foreach (var item in cases)
        {
            Throws<SettingsValidationException>(
                () => SettingsContract.Deserialize(item.Item2),
                item.Item1);
        }

        var canonical = SettingsContract.Deserialize(SettingsFixtures.VersionTen).Settings;
        var note = canonical.Notes.Single();
        var once = (AbsoluteOnceReminderRule)note.ReminderRules[0];
        var duplicateSchedule = note with
        {
            ReminderRules =
            [
                once,
                new AbsoluteOnceReminderRule("different-id-same-schedule", once.ScheduledUtc)
            ]
        };
        Throws<SettingsValidationException>(
            () => SettingsContract.SerializeCanonical(canonical with { Notes = [duplicateSchedule] }),
            "duplicate-schedule");

        var countdown = canonical.Countdowns.Single();
        var nineRules = countdown with
        {
            ReminderRules = Enumerable.Range(1, 9)
                .Select(index => (ReminderRule)new AbsoluteOnceReminderRule(
                    $"rule-nine-{index}",
                    new DateTimeOffset(2032, 10, 11, 9, index, 0, TimeSpan.Zero)))
                .ToArray()
        };
        Throws<SettingsValidationException>(
            () => SettingsContract.SerializeCanonical(canonical with { Countdowns = [nineRules] }),
            "超过 8 条规则");

        foreach (var offset in new[] { -1, 5_256_001 })
        {
            var invalidRelative = countdown with
            {
                ReminderRules = [new CountdownRelativeReminderRule($"invalid-relative-{offset}", offset)]
            };
            Throws<SettingsValidationException>(
                () => SettingsContract.SerializeCanonical(canonical with { Countdowns = [invalidRelative] }),
                $"相对偏移越界 {offset}");
        }

        var relativeOnNote = note with
        {
            ReminderRules = [new CountdownRelativeReminderRule("relative-on-note", 0)]
        };
        Throws<SettingsValidationException>(
            () => SettingsContract.SerializeCanonical(canonical with { Notes = [relativeOnNote] }),
            "便签拒绝相对规则");
    }

    private static void VerifyVersionTenCursorsAlignWithOccurrences()
    {
        var rawCases = new[]
        {
            (
                "absolute-once-cursor-after-unique-occurrence",
                SettingsFixtures.VersionTen.Replace(
                    "\"handledThroughUtc\": \"2032-10-11T01:30:00.0000000+00:00\"",
                    "\"handledThroughUtc\": \"2032-10-11T01:31:00.0000000+00:00\"",
                    StringComparison.Ordinal)),
            (
                "fixed-interval-cursor-between-occurrences",
                SettingsFixtures.VersionTen.Replace(
                    "\"handledThroughUtc\": \"2032-10-11T03:00:00.0000000+00:00\"",
                    "\"handledThroughUtc\": \"2032-10-11T03:01:00.0000000+00:00\"",
                    StringComparison.Ordinal))
        };
        foreach (var item in rawCases)
        {
            Throws<SettingsValidationException>(
                () => SettingsContract.Deserialize(item.Item2),
                item.Item1);
        }

        var settings = SettingsContract.Deserialize(SettingsFixtures.VersionTen).Settings;
        var note = settings.Notes.Single();
        var once = (AbsoluteOnceReminderRule)note.ReminderRules[0];
        var repeating = (FixedIntervalReminderRule)note.ReminderRules[1];
        foreach (var item in new[]
                 {
                     (Rule: (ReminderRule)once, Cursor: once.ScheduledUtc.AddMinutes(1)),
                     (Rule: (ReminderRule)repeating, Cursor: repeating.FirstDueUtc.AddMinutes(1))
                 })
        {
            var states = settings.ReminderStates.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            states[item.Rule.Id] = new ReminderRuleState(item.Rule.Id, item.Cursor, null);
            Throws<SettingsValidationException>(
                () => SettingsContract.SerializeCanonical(settings with { ReminderStates = states }),
                $"canonical-{item.Rule.Id}-cursor-between-occurrences");
        }

        var alignedStates = settings.ReminderStates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        alignedStates[once.Id] = new ReminderRuleState(once.Id, once.ScheduledUtc, null);
        alignedStates[repeating.Id] = new ReminderRuleState(
            repeating.Id,
            repeating.FirstDueUtc.AddMinutes(repeating.Interval.Minutes),
            null);
        var canonical = SettingsContract.SerializeCanonical(settings with { ReminderStates = alignedStates });
        var reloaded = SettingsContract.Deserialize(canonical).Settings;
        Equal(once.ScheduledUtc, reloaded.ReminderStates[once.Id].HandledThroughUtc, "absolute once 对齐 cursor");
        Equal(
            repeating.FirstDueUtc.AddMinutes(repeating.Interval.Minutes),
            reloaded.ReminderStates[repeating.Id].HandledThroughUtc,
            "fixed interval 对齐 cursor");
    }

    private static void VerifyVersionTenIntervalJsonNumbersAreStrict()
    {
        foreach (var token in new[] { "1.0", "1e0" })
        {
            var invalid = SettingsFixtures.VersionTen.Replace(
                "\"intervalMinutes\": 60",
                $"\"intervalMinutes\": {token}",
                StringComparison.Ordinal);
            Throws<SettingsValidationException>(
                () => SettingsContract.Deserialize(invalid),
                $"intervalMinutes-{token}");
        }

        var valid = SettingsFixtures.VersionTen.Replace(
            "\"intervalMinutes\": 60",
            "\"intervalMinutes\": 1",
            StringComparison.Ordinal);
        var loaded = SettingsContract.Deserialize(valid).Settings;
        Equal(
            1,
            ((FixedIntervalReminderRule)loaded.Countdowns.Single().ReminderRules[1]).Interval.Minutes,
            "integer intervalMinutes");
    }

    private static void VerifyOccurrenceKeyRoundTrip()
    {
        var rule = new FixedIntervalReminderRule(
            "rule-round-trip",
            new DateTimeOffset(2033, 1, 2, 3, 0, 0, TimeSpan.Zero),
            new ReminderInterval(15));
        var key = ReminderOccurrenceKey.Create(
            rule,
            new DateTimeOffset(2033, 1, 2, 4, 15, 0, TimeSpan.Zero));
        Equal(
            "rule-round-trip|2033-01-02T04:15:00.0000000+00:00",
            key.ToString(),
            "occurrence key 规范文本");
        Equal(true, ReminderOccurrenceKey.TryParse(key.ToString(), out var parsed), "occurrence key 可解析");
        Equal(key, parsed, "occurrence key 往返");
        Equal(false, ReminderOccurrenceKey.TryParse("rule-round-trip|2033-01-02T12:15:00.0000000+08:00", out _), "occurrence key 拒绝非 UTC 文本");
    }

    private static void VerifyAbsoluteOnceCursorBeforeScheduleIsRejected()
    {
        const string invalid =
            """
            {
              "schemaVersion": 10,
              "timeZoneId": "China Standard Time",
              "displayPrecision": "Second",
              "monthlySalary": 0,
              "workStart": "09:00",
              "workEnd": "18:00",
              "themePreference": "Auto",
              "tags": [],
              "countdowns": [],
              "notes": [
                {
                  "id": "note-cursor",
                  "text": "游标不能早于一次性计划",
                  "date": "2031-09-10",
                  "isCompleted": false,
                  "tagIds": [],
                  "reminderRules": [
                    {
                      "id": "rule-cursor",
                      "type": "absoluteOnce",
                      "scheduledUtc": "2031-09-10T02:30:00.0000000+00:00"
                    }
                  ]
                }
              ],
              "reminderStates": {
                "rule-cursor": {
                  "handledThroughUtc": "2031-09-10T02:29:00.0000000+00:00",
                  "lastDeliveredOccurrenceKey": null,
                  "lastDispositionOccurrenceKey": null,
                  "activeSnooze": null
                }
              }
            }
            """;

        Throws<SettingsValidationException>(
            () => SettingsContract.Deserialize(invalid),
            "absoluteOnce 规则游标早于计划时刻");
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
            ("unsupported-schema", """{"schemaVersion":11,"timeZoneId":"China Standard Time"}"""),
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
            Equal(10, document.RootElement.GetProperty("schemaVersion").GetInt32(), "保存后的 schemaVersion");
            Equal("版本七便签", document.RootElement.GetProperty("notes")[0].GetProperty("text").GetString(), "便签正文");
        });
    }

    private static void VerifySaveKeepsTwoPriorLegalGenerations()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var store = new SettingsStore();
            var first = CountdownSettings.Empty with
            {
                Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 1000m }
            };
            var second = CountdownSettings.Empty with
            {
                Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 2000m }
            };
            var third = CountdownSettings.Empty with
            {
                Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 3000m }
            };

            store.Save(settingsPath, first);
            store.Save(settingsPath, second);
            store.Save(settingsPath, third);

            Equal(3000m, store.Load(settingsPath).Settings.Preferences.MonthlySalary, "当前主设置");
            Equal(
                2000m,
                store.Load(settingsPath + ".backup1").Settings.Preferences.MonthlySalary,
                "backup 1");
            Equal(
                1000m,
                store.Load(settingsPath + ".backup2").Settings.Preferences.MonthlySalary,
                "backup 2");
        });
    }

    private static void VerifyStartupPrefersMissingOrStrictValidMain()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var store = new SettingsStore();
            var promptCount = 0;
            var firstRun = store.LoadForStartup(
                settingsPath,
                _ =>
                {
                    promptCount++;
                    throw new VerificationException("首次运行不应提供恢复候选。");
                });
            Equal(0, firstRun.Settings.Countdowns.Count, "首次运行空倒计时");
            Equal(0, firstRun.Settings.Notes.Count, "首次运行空便签");

            File.WriteAllText(
                settingsPath + ".backup1",
                SettingsContract.SerializeCanonical(SettingsContract.Deserialize(SettingsFixtures.VersionSeven).Settings),
                new UTF8Encoding(false));
            store.Save(settingsPath, CountdownSettings.Empty);
            var loaded = store.LoadForStartup(
                settingsPath,
                _ =>
                {
                    promptCount++;
                    throw new VerificationException("合法主设置不应提供恢复候选。");
                });

            Equal(0, promptCount, "恢复提示次数");
            Equal(0, loaded.Settings.Countdowns.Count, "合法空主设置优先于非空备份");
            Equal(0, loaded.Settings.Notes.Count, "合法空主设置便签");
        });
    }

    private static void VerifyStartupRestoresLatestLegalBackupAfterConfirmation()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var damagedBytes = Encoding.UTF8.GetBytes("{invalid-json");
            File.WriteAllBytes(settingsPath, damagedBytes);
            File.WriteAllText(
                settingsPath + ".backup1",
                SettingsFixtures.VersionTen.Replace(
                    "\"schemaVersion\": 10",
                    "\"schemaVersion\": 10, \"foreign\": true",
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            var backupSettings = CountdownSettings.Empty with
            {
                Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 2468m }
            };
            File.WriteAllText(
                settingsPath + ".backup2",
                SettingsContract.SerializeCanonical(backupSettings),
                new UTF8Encoding(false));

            var promptCount = 0;
            var loaded = new SettingsStore().LoadForStartup(
                settingsPath,
                prompt =>
                {
                    promptCount++;
                    Equal(2, prompt.BackupGeneration, "提供的最新合法备份代次");
                    return SettingsRecoveryDecision.RecoverBackup;
                });

            Equal(1, promptCount, "恢复提示次数");
            Equal(2468m, loaded.Settings.Preferences.MonthlySalary, "恢复返回设置");
            Equal(
                2468m,
                new SettingsStore().Load(settingsPath).Settings.Preferences.MonthlySalary,
                "从主路径回读的恢复设置");
            var evidenceFiles = Directory.GetFiles(
                Path.Combine(directory, "settings-recovery-evidence"),
                "settings.corrupt.*.json");
            Equal(1, evidenceFiles.Length, "损坏主设置证据数量");
            Equal(
                true,
                damagedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(evidenceFiles[0])),
                "损坏主设置逐字节证据");
        });
    }

    private static void VerifyStartupReadbackFailureRollsBackRecovery()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var damagedBytes = Encoding.UTF8.GetBytes("{still-damaged");
            File.WriteAllBytes(settingsPath, damagedBytes);
            File.WriteAllText(
                settingsPath + ".backup1",
                SettingsContract.SerializeCanonical(CountdownSettings.Empty),
                new UTF8Encoding(false));
            var operations = new ReadbackFailingSettingsFileOperations(settingsPath);
            var store = new SettingsStore(fileOperations: operations);

            Throws<SettingsRecoveryException>(
                () => store.LoadForStartup(
                    settingsPath,
                    _ => SettingsRecoveryDecision.RecoverBackup),
                "恢复回读失败");

            Equal(
                true,
                damagedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath)),
                "恢复回读失败后的原主设置字节");
            Equal(true, operations.ReadbackFailureInjected, "恢复回读故障注入");
        });
    }

    private static void VerifyStartupRecoveryWritesRequireExplicitAuthorization()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var damagedBytes = Encoding.UTF8.GetBytes("{active-acceptance-damaged-main");
            var backupBytes = CanonicalSettingsBytesWithSalary(2468m);
            File.WriteAllBytes(settingsPath, damagedBytes);
            File.WriteAllBytes(settingsPath + ".backup1", backupBytes);
            var promptCount = 0;
            var store = new SettingsStore();

            Throws<SettingsRecoveryException>(
                () => store.LoadForStartup(
                    settingsPath,
                    _ =>
                    {
                        promptCount++;
                        return SettingsRecoveryDecision.RecoverBackup;
                    },
                    recoveryAllowed: false),
                "active acceptance 恢复写授权");

            Equal(0, promptCount, "active acceptance 恢复提示次数");
            Equal(true, damagedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath)), "active acceptance 主设置字节");
            Equal(true, backupBytes.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath + ".backup1")), "active acceptance backup 1 字节");
            Equal(false, Directory.Exists(Path.Combine(directory, "settings-recovery-evidence")), "active acceptance 恢复证据目录");
            Equal(false, File.Exists(settingsPath + ".tmp"), "active acceptance 临时设置");

            var loaded = store.LoadForStartup(
                settingsPath,
                _ =>
                {
                    promptCount++;
                    return SettingsRecoveryDecision.RecoverBackup;
                },
                recoveryAllowed: true);

            Equal(1, promptCount, "released acceptance 恢复提示次数");
            Equal(2468m, loaded.Settings.Preferences.MonthlySalary, "released acceptance 恢复设置");
            Equal(2468m, store.Load(settingsPath).Settings.Preferences.MonthlySalary, "released acceptance 主路径回读");
        });
    }

    private static void VerifyStartupOccupiedSettingsPathNeverStartsEmpty()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            Directory.CreateDirectory(settingsPath);
            var promptCount = 0;

            Throws<SettingsRecoveryException>(
                () => new SettingsStore().LoadForStartup(
                    settingsPath,
                    _ =>
                    {
                        promptCount++;
                        return SettingsRecoveryDecision.RecoverBackup;
                    }),
                "目录占用 settingsPath");

            Equal(0, promptCount, "目录占用 settingsPath 的恢复提示次数");
            Equal(true, Directory.Exists(settingsPath), "目录占用 settingsPath 保持原样");

            Throws<SettingsRecoveryUnavailableException>(
                () => new SettingsStore(fileOperations: new AccessDeniedPathStateSettingsFileOperations())
                    .LoadForStartup(
                        Path.Combine(directory, "inaccessible-settings.json"),
                        _ =>
                        {
                            promptCount++;
                            return SettingsRecoveryDecision.RecoverBackup;
                        }),
                "settingsPath 状态探测 access denied 进入无合法备份退出");
            Equal(0, promptCount, "settingsPath access denied 的恢复提示次数");
        });
    }

    private static void VerifyStartupAclDeniedMainEntersBackupRecovery()
    {
        WithTemporaryDirectory(directory =>
        {
            var deniedMainBytes = Encoding.UTF8.GetBytes("{acl-denied-main");
            var backupBytes = CanonicalSettingsBytesWithSalary(2468m);

            var approvePath = Path.Combine(directory, "settings-approve.json");
            File.WriteAllBytes(approvePath, deniedMainBytes);
            File.WriteAllBytes(approvePath + ".backup1", backupBytes);
            var approveOperations = new AclDeniedSettingsFileOperations(
                approvePath,
                denyWholeDirectory: false);
            var approveStore = new SettingsStore(fileOperations: approveOperations);
            var approvePromptCount = 0;
            var loaded = approveStore.LoadForStartup(
                approvePath,
                prompt =>
                {
                    approvePromptCount++;
                    Equal(1, prompt.BackupGeneration, "ACL 拒绝主设置后提供的备份代次");
                    return SettingsRecoveryDecision.RecoverBackup;
                });

            Equal(1, approvePromptCount, "ACL 拒绝主设置的恢复提示次数");
            Equal(2468m, loaded.Settings.Preferences.MonthlySalary, "ACL 拒绝后恢复返回设置");
            Equal(
                2468m,
                approveStore.Load(approvePath).Settings.Preferences.MonthlySalary,
                "ACL 拒绝恢复后的主路径回读");
            Equal(
                true,
                backupBytes.AsSpan().SequenceEqual(File.ReadAllBytes(approvePath + ".backup1")),
                "ACL 恢复不改写 backup 1");
            Equal(
                false,
                Directory.Exists(Path.Combine(directory, "settings-recovery-evidence")),
                "ACL 拒绝主字节不可读时不产生恢复证据");
            Equal(true, approveOperations.DenialClearedByReplace, "ACL 恢复替换完成后解除拒绝");

            var declinePath = Path.Combine(directory, "settings-decline.json");
            File.WriteAllBytes(declinePath, deniedMainBytes);
            File.WriteAllBytes(declinePath + ".backup1", backupBytes);
            var declineStore = new SettingsStore(fileOperations: new AclDeniedSettingsFileOperations(
                declinePath,
                denyWholeDirectory: false));
            var declinePromptCount = 0;
            Throws<SettingsRecoveryDeclinedException>(
                () => declineStore.LoadForStartup(
                    declinePath,
                    _ =>
                    {
                        declinePromptCount++;
                        return SettingsRecoveryDecision.Exit;
                    }),
                "ACL 拒绝主设置后拒绝恢复");
            Equal(1, declinePromptCount, "ACL 拒绝后拒绝恢复的提示次数");
            Equal(
                true,
                deniedMainBytes.AsSpan().SequenceEqual(File.ReadAllBytes(declinePath)),
                "拒绝恢复后原主设置字节保持");
            Equal(
                true,
                backupBytes.AsSpan().SequenceEqual(File.ReadAllBytes(declinePath + ".backup1")),
                "拒绝恢复后 backup 1 字节保持");

            var deniedDirectory = Path.Combine(directory, "denied-parent");
            Directory.CreateDirectory(deniedDirectory);
            var deniedPath = Path.Combine(deniedDirectory, "settings.json");
            File.WriteAllBytes(deniedPath, deniedMainBytes);
            File.WriteAllBytes(deniedPath + ".backup1", backupBytes);
            var noBackupStore = new SettingsStore(fileOperations: new AclDeniedSettingsFileOperations(
                deniedPath,
                denyWholeDirectory: true));
            var noBackupPromptCount = 0;
            Throws<SettingsRecoveryUnavailableException>(
                () => noBackupStore.LoadForStartup(
                    deniedPath,
                    _ =>
                    {
                        noBackupPromptCount++;
                        return SettingsRecoveryDecision.RecoverBackup;
                    }),
                "ACL 父目录拒绝且无合法备份");
            Equal(0, noBackupPromptCount, "ACL 父目录拒绝时不提供恢复提示");
            Equal(
                true,
                deniedMainBytes.AsSpan().SequenceEqual(File.ReadAllBytes(deniedPath)),
                "无合法备份时原主设置字节保持");
        });
    }

    private static void VerifyStartupRealFilesystemAclDenialEntersRecovery()
    {
        WithTemporaryDirectory(directory =>
        {
            var backupBytes = CanonicalSettingsBytesWithSalary(1357m);

            var fileDeniedPath = Path.Combine(directory, "file-denied-settings.json");
            File.WriteAllBytes(fileDeniedPath, Encoding.UTF8.GetBytes("{real-acl-denied-main"));
            File.WriteAllBytes(fileDeniedPath + ".backup1", backupBytes);
            var fileDenyRule = DenyCurrentUserRead(fileDeniedPath);
            try
            {
                var filePromptCount = 0;
                Throws<SettingsRecoveryDeclinedException>(
                    () => new SettingsStore().LoadForStartup(
                        fileDeniedPath,
                        prompt =>
                        {
                            filePromptCount++;
                            Equal(1, prompt.BackupGeneration, "真实 ACL 文件拒绝后提供的备份代次");
                            return SettingsRecoveryDecision.Exit;
                        }),
                    "真实 ACL 文件拒绝进入恢复提示而非穿透退出");
                Equal(1, filePromptCount, "真实 ACL 文件拒绝的恢复提示次数");
            }
            finally
            {
                RestoreCurrentUserAccess(fileDeniedPath, fileDenyRule);
            }

            var deniedDirectory = Path.Combine(directory, "real-denied-parent");
            Directory.CreateDirectory(deniedDirectory);
            var directoryDenyRule = DenyCurrentUserRead(deniedDirectory);
            var dirDeniedPath = Path.Combine(deniedDirectory, "settings.json");
            File.WriteAllBytes(dirDeniedPath, Encoding.UTF8.GetBytes("{real-acl-denied-main"));
            File.WriteAllBytes(dirDeniedPath + ".backup1", backupBytes);
            try
            {
                var directoryPromptCount = 0;
                Throws<SettingsRecoveryUnavailableException>(
                    () => new SettingsStore().LoadForStartup(
                        dirDeniedPath,
                        _ =>
                        {
                            directoryPromptCount++;
                            return SettingsRecoveryDecision.RecoverBackup;
                        }),
                    "真实 ACL 父目录拒绝进入无合法备份退出而非穿透");
                Equal(0, directoryPromptCount, "真实 ACL 父目录拒绝的恢复提示次数");
            }
            finally
            {
                RestoreCurrentUserAccess(deniedDirectory, directoryDenyRule);
            }
        });
    }

    private static System.Security.AccessControl.FileSystemAccessRule DenyCurrentUserRead(string path)
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        if (File.Exists(path))
        {
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                identity,
                System.Security.AccessControl.FileSystemRights.Read,
                System.Security.AccessControl.AccessControlType.Deny);
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            security.AddAccessRule(rule);
            fileInfo.SetAccessControl(security);
            return rule;
        }

        var directoryRule = new System.Security.AccessControl.FileSystemAccessRule(
            identity,
            System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Deny);
        var directoryInfo = new DirectoryInfo(path);
        var directorySecurity = directoryInfo.GetAccessControl();
        directorySecurity.AddAccessRule(directoryRule);
        directoryInfo.SetAccessControl(directorySecurity);
        return directoryRule;
    }

    private static void RestoreCurrentUserAccess(
        string path,
        System.Security.AccessControl.FileSystemAccessRule rule)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            security.RemoveAccessRuleSpecific(rule);
            fileInfo.SetAccessControl(security);
            return;
        }

        var directoryInfo = new DirectoryInfo(path);
        var directorySecurity = directoryInfo.GetAccessControl();
        directorySecurity.RemoveAccessRuleSpecific(rule);
        directoryInfo.SetAccessControl(directorySecurity);
    }

    private static void VerifyStartupFailedReadbackAndRollbackPreservesOriginalMainArtifact()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var damagedBytes = Encoding.UTF8.GetBytes("{original-main-without-readable-evidence");
            File.WriteAllBytes(settingsPath, damagedBytes);
            File.WriteAllText(
                settingsPath + ".backup1",
                SettingsContract.SerializeCanonical(CountdownSettings.Empty),
                new UTF8Encoding(false));
            var operations = new RecoveryRollbackFailingSettingsFileOperations(settingsPath);
            var store = new SettingsStore(fileOperations: operations);

            Throws<SettingsRecoveryException>(
                () => store.LoadForStartup(
                    settingsPath,
                    _ => SettingsRecoveryDecision.RecoverBackup),
                "恢复回读和原主回滚同时失败");

            Equal(true, operations.EvidenceReadFailureInjected, "恢复证据读取故障注入");
            Equal(true, operations.ReadbackFailureInjected, "恢复主路径回读故障注入");
            Equal(true, operations.RollbackFailureInjected, "原主回滚故障注入");
            var rollbackFiles = Directory.GetFiles(directory, "settings.json.recovery-rollback-*");
            Equal(1, rollbackFiles.Length, "未决 recovery rollback artifact 数量");
            Equal(
                true,
                damagedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(rollbackFiles[0])),
                "未决 recovery rollback artifact 原主字节");
        });
    }

    private static void VerifyStartupMainDisappearanceAfterPromptNeverStartsEmpty()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, "{damaged-before-prompt", new UTF8Encoding(false));
            File.WriteAllText(
                settingsPath + ".backup1",
                SettingsContract.SerializeCanonical(CountdownSettings.Empty),
                new UTF8Encoding(false));

            Throws<SettingsRecoveryException>(
                () => new SettingsStore().LoadForStartup(
                    settingsPath,
                    _ =>
                    {
                        File.Delete(settingsPath);
                        return SettingsRecoveryDecision.RecoverBackup;
                    }),
                "恢复确认后主文件消失");

            Equal(false, File.Exists(settingsPath), "恢复确认后不重建已消失的主设置");
        });
    }

    private static void VerifyStartupRefusalOrNoBackupNeverWrites()
    {
        WithTemporaryDirectory(directory =>
        {
            var refusedPath = Path.Combine(directory, "refused.json");
            var refusedBytes = Encoding.UTF8.GetBytes("{refused-damaged");
            File.WriteAllBytes(refusedPath, refusedBytes);
            File.WriteAllText(
                refusedPath + ".backup1",
                SettingsContract.SerializeCanonical(CountdownSettings.Empty),
                new UTF8Encoding(false));
            Throws<SettingsRecoveryDeclinedException>(
                () => new SettingsStore().LoadForStartup(
                    refusedPath,
                    _ => SettingsRecoveryDecision.Exit),
                "用户拒绝恢复");
            Equal(
                true,
                refusedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(refusedPath)),
                "用户拒绝后的主设置字节");
            Equal(
                false,
                Directory.Exists(Path.Combine(directory, "settings-recovery-evidence")),
                "用户拒绝前不写恢复证据");

            var unavailablePath = Path.Combine(directory, "unavailable.json");
            var unavailableBytes = Encoding.UTF8.GetBytes("{no-backup");
            File.WriteAllBytes(unavailablePath, unavailableBytes);
            File.WriteAllText(unavailablePath + ".backup1", "{}", new UTF8Encoding(false));
            var promptCount = 0;
            Throws<SettingsRecoveryUnavailableException>(
                () => new SettingsStore().LoadForStartup(
                    unavailablePath,
                    _ =>
                    {
                        promptCount++;
                        return SettingsRecoveryDecision.RecoverBackup;
                    }),
                "无合法备份");
            Equal(0, promptCount, "无合法备份提示次数");
            Equal(
                true,
                unavailableBytes.AsSpan().SequenceEqual(File.ReadAllBytes(unavailablePath)),
                "无合法备份时的主设置字节");
        });
    }

    private static void VerifyStartupReadOnlyOrUnreadableMainNeverOverwrites()
    {
        WithTemporaryDirectory(directory =>
        {
            var readOnlyPath = Path.Combine(directory, "readonly.json");
            var readOnlyBytes = Encoding.UTF8.GetBytes("{readonly-damaged");
            File.WriteAllBytes(readOnlyPath, readOnlyBytes);
            File.WriteAllText(
                readOnlyPath + ".backup1",
                SettingsContract.SerializeCanonical(CountdownSettings.Empty),
                new UTF8Encoding(false));
            File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);
            try
            {
                ThrowsAny<IOException, UnauthorizedAccessException>(
                    () => new SettingsStore().LoadForStartup(
                        readOnlyPath,
                        _ => SettingsRecoveryDecision.RecoverBackup),
                    "只读主设置恢复替换");
            }
            finally
            {
                File.SetAttributes(readOnlyPath, FileAttributes.Normal);
            }

            Equal(
                true,
                readOnlyBytes.AsSpan().SequenceEqual(File.ReadAllBytes(readOnlyPath)),
                "只读主设置字节");

            var unreadablePath = Path.Combine(directory, "unreadable.json");
            var unreadableBytes = Encoding.UTF8.GetBytes("{unreadable-damaged");
            File.WriteAllBytes(unreadablePath, unreadableBytes);
            File.WriteAllText(
                unreadablePath + ".backup1",
                SettingsContract.SerializeCanonical(CountdownSettings.Empty),
                new UTF8Encoding(false));
            var promptCount = 0;
            using (File.Open(unreadablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                ThrowsAny<IOException, UnauthorizedAccessException>(
                    () => new SettingsStore().LoadForStartup(
                        unreadablePath,
                        _ =>
                        {
                            promptCount++;
                            return SettingsRecoveryDecision.RecoverBackup;
                        }),
                    "独占不可读主设置恢复替换");
            }

            Equal(1, promptCount, "不可读主设置恢复提示次数");
            Equal(
                true,
                unreadableBytes.AsSpan().SequenceEqual(File.ReadAllBytes(unreadablePath)),
                "不可读主设置字节");
        });
    }

    private static void VerifyInvalidMainNeverEntersBackupHistory()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var invalidBytes = Encoding.UTF8.GetBytes("{invalid-main");
            File.WriteAllBytes(settingsPath, invalidBytes);
            Throws<SettingsValidationException>(
                () => new SettingsStore().Save(settingsPath, CountdownSettings.Empty),
                "无效主设置保存");
            Equal(false, File.Exists(settingsPath + ".backup1"), "无效主设置 backup 1");
            Equal(false, File.Exists(settingsPath + ".backup2"), "无效主设置 backup 2");
            Equal(
                true,
                invalidBytes.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath)),
                "无效主设置原字节");
        });
    }

    private static void VerifySaveHistoryPreflightDoesNotConsumeDomainClock()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllBytes(settingsPath, CanonicalSettingsBytesWithSalary(200m));
            File.WriteAllBytes(settingsPath + ".backup1", CanonicalSettingsBytesWithSalary(100m));
            var clock = new CountingClock(new DateTimeOffset(2032, 4, 15, 4, 0, 0, TimeSpan.Zero));

            new SettingsStore(clock: clock).Save(
                settingsPath,
                CountdownSettings.Empty with
                {
                    Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 300m }
                });

            Equal(0, clock.ReadCount, "保存历史预检业务时钟读取次数");
        });
    }

    private static void VerifyReplacementFailurePreservesMainAndBackupGenerations()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var backupOnePath = settingsPath + ".backup1";
            var backupTwoPath = settingsPath + ".backup2";
            var mainBytes = CanonicalSettingsBytesWithSalary(300m);
            var backupOneBytes = CanonicalSettingsBytesWithSalary(200m);
            var backupTwoBytes = CanonicalSettingsBytesWithSalary(100m);
            File.WriteAllBytes(settingsPath, mainBytes);
            File.WriteAllBytes(backupOnePath, backupOneBytes);
            File.WriteAllBytes(backupTwoPath, backupTwoBytes);
            File.SetAttributes(settingsPath, FileAttributes.ReadOnly);
            try
            {
                ThrowsAny<IOException, UnauthorizedAccessException>(
                    () => new SettingsStore().Save(
                        settingsPath,
                        CountdownSettings.Empty with
                        {
                            Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 400m }
                        }),
                    "主设置替换失败");
            }
            finally
            {
                File.SetAttributes(settingsPath, FileAttributes.Normal);
            }

            Equal(true, mainBytes.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath)), "替换失败主设置");
            Equal(true, backupOneBytes.AsSpan().SequenceEqual(File.ReadAllBytes(backupOnePath)), "替换失败 backup 1");
            Equal(true, backupTwoBytes.AsSpan().SequenceEqual(File.ReadAllBytes(backupTwoPath)), "替换失败 backup 2");
        });
    }

    private static void VerifyFailedBackupRollbackPreservesArtifactAndBlocksNextSave()
    {
        WithTemporaryDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var backupOnePath = settingsPath + ".backup1";
            var backupTwoPath = settingsPath + ".backup2";
            var rollbackPath = backupTwoPath + ".rollback";
            var mainBytes = CanonicalSettingsBytesWithSalary(300m);
            var backupOneBytes = CanonicalSettingsBytesWithSalary(200m);
            var backupTwoBytes = CanonicalSettingsBytesWithSalary(100m);
            File.WriteAllBytes(settingsPath, mainBytes);
            File.WriteAllBytes(backupOnePath, backupOneBytes);
            File.WriteAllBytes(backupTwoPath, backupTwoBytes);
            var operations = new SaveRollbackFailingSettingsFileOperations(settingsPath, backupTwoPath);

            Throws<IOException>(
                () => new SettingsStore(fileOperations: operations).Save(
                    settingsPath,
                    CountdownSettings.Empty with
                    {
                        Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 400m }
                    }),
                "backup 2 回滚失败");

            Equal(true, operations.CommitFailureInjected, "主设置提交故障注入");
            Equal(true, operations.RollbackFailureInjected, "backup 2 回滚故障注入");
            Equal(true, File.Exists(rollbackPath), "未决 backup 2 rollback artifact");
            Equal(true, mainBytes.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath)), "回滚失败主设置");
            Equal(true, backupOneBytes.AsSpan().SequenceEqual(File.ReadAllBytes(backupOnePath)), "回滚失败 backup 1");
            Equal(true, backupOneBytes.AsSpan().SequenceEqual(File.ReadAllBytes(backupTwoPath)), "回滚失败当前 backup 2");
            Equal(true, backupTwoBytes.AsSpan().SequenceEqual(File.ReadAllBytes(rollbackPath)), "回滚失败原 backup 2 artifact");

            Throws<IOException>(
                () => new SettingsStore().Save(
                    settingsPath,
                    CountdownSettings.Empty with
                    {
                        Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 500m }
                    }),
                "存在未决 backup 2 rollback artifact 时继续保存");

            Equal(true, mainBytes.AsSpan().SequenceEqual(File.ReadAllBytes(settingsPath)), "闭锁后主设置");
            Equal(true, backupOneBytes.AsSpan().SequenceEqual(File.ReadAllBytes(backupOnePath)), "闭锁后 backup 1");
            Equal(true, backupOneBytes.AsSpan().SequenceEqual(File.ReadAllBytes(backupTwoPath)), "闭锁后当前 backup 2");
            Equal(true, backupTwoBytes.AsSpan().SequenceEqual(File.ReadAllBytes(rollbackPath)), "闭锁后原 backup 2 artifact");
        });
    }

    private static byte[] CanonicalSettingsBytesWithSalary(decimal salary) =>
        Encoding.UTF8.GetBytes(SettingsContract.SerializeCanonical(
            CountdownSettings.Empty with
            {
                Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = salary }
            }));

    private static void VerifyDanglingReminderStateIsRejected()
    {
        var settings = CountdownSettings.Empty with
        {
            ReminderStates = new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase)
            {
                ["missing-rule"] = new(
                    "missing-rule",
                    new DateTimeOffset(2030, 1, 2, 3, 4, 0, TimeSpan.Zero),
                    null)
            }
        };

        Throws<SettingsValidationException>(
            () => SettingsContract.SerializeCanonical(settings),
            "悬空提醒状态");
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

internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; }
}

internal sealed class CountingClock : IClock
{
    private readonly DateTimeOffset _utcNow;

    public CountingClock(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
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

internal sealed class ReadbackFailingSettingsFileOperations : ISettingsFileOperations
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _settingsPath;
    private bool _mainWasReplaced;

    public ReadbackFailingSettingsFileOperations(string settingsPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public bool ReadbackFailureInjected { get; private set; }

    public SettingsPathState GetPathState(string path) => GetTestPathState(path);

    public bool FileExists(string path) => File.Exists(path);

    public string ReadUtf8Text(string path)
    {
        if (_mainWasReplaced &&
            !ReadbackFailureInjected &&
            string.Equals(Path.GetFullPath(path), _settingsPath, StringComparison.OrdinalIgnoreCase))
        {
            ReadbackFailureInjected = true;
            throw new IOException("Injected recovery readback failure.");
        }

        return File.ReadAllText(path, Utf8NoBom);
    }

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
        bool ignoreMetadataErrors)
    {
        File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);
        if (string.Equals(Path.GetFullPath(destinationPath), _settingsPath, StringComparison.OrdinalIgnoreCase) &&
            backupPath is not null &&
            backupPath.Contains(".recovery-rollback-", StringComparison.Ordinal))
        {
            _mainWasReplaced = true;
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

    private static SettingsPathState GetTestPathState(string path) =>
        File.Exists(path)
            ? SettingsPathState.RegularFile
            : Directory.Exists(path)
                ? SettingsPathState.Occupied
                : SettingsPathState.Absent;
}

internal sealed class SaveRollbackFailingSettingsFileOperations : ISettingsFileOperations
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _settingsPath;
    private readonly string _backupTwoPath;

    public SaveRollbackFailingSettingsFileOperations(string settingsPath, string backupTwoPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
        _backupTwoPath = Path.GetFullPath(backupTwoPath);
    }

    public bool CommitFailureInjected { get; private set; }

    public bool RollbackFailureInjected { get; private set; }

    public SettingsPathState GetPathState(string path) => GetTestPathState(path);

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
        bool ignoreMetadataErrors)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (!CommitFailureInjected &&
            string.Equals(fullDestinationPath, _settingsPath, StringComparison.OrdinalIgnoreCase))
        {
            CommitFailureInjected = true;
            throw new IOException("Injected main settings commit failure.");
        }

        if (CommitFailureInjected &&
            !RollbackFailureInjected &&
            string.Equals(fullDestinationPath, _backupTwoPath, StringComparison.OrdinalIgnoreCase) &&
            fullSourcePath.EndsWith(".backup2.rollback", StringComparison.OrdinalIgnoreCase))
        {
            RollbackFailureInjected = true;
            throw new IOException("Injected backup 2 rollback failure.");
        }

        File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);
    }

    public void DeleteFile(string path) => File.Delete(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteNewFileAndFlush(string path, ReadOnlySpan<byte> content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    private static SettingsPathState GetTestPathState(string path) =>
        File.Exists(path)
            ? SettingsPathState.RegularFile
            : Directory.Exists(path)
                ? SettingsPathState.Occupied
                : SettingsPathState.Absent;
}

internal sealed class RecoveryRollbackFailingSettingsFileOperations : ISettingsFileOperations
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _settingsPath;
    private bool _mainWasReplaced;

    public RecoveryRollbackFailingSettingsFileOperations(string settingsPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public bool EvidenceReadFailureInjected { get; private set; }

    public bool ReadbackFailureInjected { get; private set; }

    public bool RollbackFailureInjected { get; private set; }

    public SettingsPathState GetPathState(string path) => GetTestPathState(path);

    public bool FileExists(string path) => File.Exists(path);

    public string ReadUtf8Text(string path)
    {
        if (_mainWasReplaced &&
            !ReadbackFailureInjected &&
            string.Equals(Path.GetFullPath(path), _settingsPath, StringComparison.OrdinalIgnoreCase))
        {
            ReadbackFailureInjected = true;
            throw new IOException("Injected recovery readback failure.");
        }

        return File.ReadAllText(path, Utf8NoBom);
    }

    public byte[] ReadFileBytes(string path)
    {
        if (!_mainWasReplaced &&
            !EvidenceReadFailureInjected &&
            string.Equals(Path.GetFullPath(path), _settingsPath, StringComparison.OrdinalIgnoreCase))
        {
            EvidenceReadFailureInjected = true;
            throw new IOException("Injected damaged-main evidence read failure.");
        }

        return File.ReadAllBytes(path);
    }

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
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (_mainWasReplaced &&
            !RollbackFailureInjected &&
            string.Equals(fullDestinationPath, _settingsPath, StringComparison.OrdinalIgnoreCase) &&
            fullSourcePath.Contains(".recovery-rollback-", StringComparison.Ordinal))
        {
            RollbackFailureInjected = true;
            throw new IOException("Injected original-main rollback failure.");
        }

        File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);
        if (string.Equals(fullDestinationPath, _settingsPath, StringComparison.OrdinalIgnoreCase) &&
            backupPath is not null &&
            backupPath.Contains(".recovery-rollback-", StringComparison.Ordinal))
        {
            _mainWasReplaced = true;
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

    private static SettingsPathState GetTestPathState(string path) =>
        File.Exists(path)
            ? SettingsPathState.RegularFile
            : Directory.Exists(path)
                ? SettingsPathState.Occupied
                : SettingsPathState.Absent;
}

internal sealed class AclDeniedSettingsFileOperations : ISettingsFileOperations
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _settingsPath;
    private readonly string _deniedDirectoryPrefix;
    private readonly bool _denyWholeDirectory;
    private bool _denialActive = true;

    public AclDeniedSettingsFileOperations(string settingsPath, bool denyWholeDirectory)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
        _denyWholeDirectory = denyWholeDirectory;
        _deniedDirectoryPrefix =
            (Path.GetDirectoryName(_settingsPath)
                ?? throw new VerificationException("ACL 拒绝替身缺少设置父目录。")) +
            Path.DirectorySeparatorChar;
    }

    public bool DenialClearedByReplace { get; private set; }

    public SettingsPathState GetPathState(string path) =>
        IsDenied(path)
            ? throw new UnauthorizedAccessException("Injected ACL denial during settings path state probe.")
            : GetTestPathState(path);

    public bool FileExists(string path) => File.Exists(path);

    public string ReadUtf8Text(string path) =>
        IsDenied(path)
            ? throw new UnauthorizedAccessException("Injected ACL denial during settings read.")
            : File.ReadAllText(path, Utf8NoBom);

    public byte[] ReadFileBytes(string path) =>
        IsDenied(path)
            ? throw new UnauthorizedAccessException("Injected ACL denial during settings bytes read.")
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
        File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);
        if (string.Equals(Path.GetFullPath(destinationPath), _settingsPath, StringComparison.OrdinalIgnoreCase) &&
            backupPath is not null &&
            backupPath.Contains(".recovery-rollback-", StringComparison.Ordinal))
        {
            // 替换生成的新主文件继承父目录 ACL，恢复后原文件级拒绝不再存在。
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

    private bool IsDenied(string path)
    {
        if (!_denialActive)
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        if (_denyWholeDirectory)
        {
            return fullPath.StartsWith(_deniedDirectoryPrefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(fullPath, _settingsPath, StringComparison.OrdinalIgnoreCase);
    }

    private static SettingsPathState GetTestPathState(string path) =>
        File.Exists(path)
            ? SettingsPathState.RegularFile
            : Directory.Exists(path)
                ? SettingsPathState.Occupied
                : SettingsPathState.Absent;
}

internal sealed class AccessDeniedPathStateSettingsFileOperations : ISettingsFileOperations
{
    public SettingsPathState GetPathState(string path) =>
        throw new UnauthorizedAccessException("Injected settings path access denial.");

    public bool FileExists(string path) => File.Exists(path);

    public string ReadUtf8Text(string path) =>
        throw new UnauthorizedAccessException("Injected settings read access denial.");

    public byte[] ReadFileBytes(string path) =>
        throw new UnauthorizedAccessException("Injected settings bytes access denial.");

    public DateTime GetLastWriteTimeUtc(string path) => throw Unexpected();

    public void WriteUtf8NoBomText(string path, string content) => throw Unexpected();

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => throw Unexpected();

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) => throw Unexpected();

    public void ReplaceFile(
        string sourcePath,
        string destinationPath,
        string? backupPath,
        bool ignoreMetadataErrors) => throw Unexpected();

    public void DeleteFile(string path) => throw Unexpected();

    public void CreateDirectory(string path) => throw Unexpected();

    public void WriteNewFileAndFlush(string path, ReadOnlySpan<byte> content) => throw Unexpected();

    private static VerificationException Unexpected() =>
        new("Access denied path-state probe unexpectedly continued into file operations.");
}

internal sealed class VerificationException : Exception
{
    public VerificationException(string message)
        : base(message)
    {
    }
}
