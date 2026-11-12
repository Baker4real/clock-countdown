using System.Text.Json;
using System.Text.Json.Nodes;
using MinminCountdown.Core;

namespace MinminCountdown.Verification;

// These outcomes and complete canonical after snapshots were produced by the existing PowerShell
// Test-CountdownSettingsDeploymentTransition / ConvertTo-CountdownSettingsJson
// implementation at integration/prd-258 1677a01b. They are independent pinned
// expectations, not values derived from the C# equivalence implementation.
internal static class DeploymentEquivalenceFixtures
{
    public static readonly DateTimeOffset MigrationUtc =
        new(2031, 9, 10, 2, 16, 0, TimeSpan.Zero);

    private const string VersionOneAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":0,"workStart":"09:00","workEnd":"18:00","themePreference":"Auto","tags":[],"countdowns":[{"id":"4ba69e89a35b48939073cee2759c791b","name":"我的倒计时","targetUtc":"2030-01-02T03:04:05.0000000+00:00","tagIds":[],"reminderRules":[]}],"notes":[],"reminderStates":{}}""";
    private const string VersionTwoAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":0,"workStart":"09:00","workEnd":"18:00","themePreference":"Auto","tags":[],"countdowns":[{"id":"countdown-v2","name":"版本二倒计时","targetUtc":"2030-02-03T04:05:06.0000000+00:00","tagIds":[],"reminderRules":[]}],"notes":[],"reminderStates":{}}""";
    private const string VersionThreeAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Minute","monthlySalary":12345.67,"workStart":"08:30","workEnd":"17:45","themePreference":"Auto","tags":[],"countdowns":[{"id":"countdown-v3","name":"版本三倒计时","targetUtc":"2030-03-04T05:06:07.0000000+00:00","tagIds":[],"reminderRules":[]}],"notes":[],"reminderStates":{}}""";
    private const string VersionFourAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Hour","monthlySalary":8800,"workStart":"09:00","workEnd":"18:00","themePreference":"Auto","tags":[],"countdowns":[{"id":"countdown-v4","name":"版本四倒计时","targetUtc":"2030-04-05T06:07:08.0000000+00:00","tagIds":[],"reminderRules":[]}],"notes":[{"id":"note-v4","text":"版本四便签","date":null,"isCompleted":true,"tagIds":[],"reminderRules":[]}],"reminderStates":{}}""";
    private const string VersionFiveAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":10000,"workStart":"09:15","workEnd":"18:15","themePreference":"Auto","tags":[{"id":"tag-work","name":"工作","color":"#FF8A70"}],"countdowns":[{"id":"countdown-v5","name":"版本五倒计时","targetUtc":"2030-05-06T07:08:09.0000000+00:00","tagIds":["tag-work"],"reminderRules":[]}],"notes":[{"id":"note-v5","text":"版本五便签","date":null,"isCompleted":false,"tagIds":["tag-work"],"reminderRules":[]}],"reminderStates":{}}""";
    private const string VersionSixAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Day","monthlySalary":15000,"workStart":"08:00","workEnd":"17:00","themePreference":"Auto","tags":[{"id":"tag-life","name":"生活"}],"countdowns":[{"id":"countdown-v6","name":"版本六倒计时","targetUtc":"2030-06-07T08:09:10.0000000+00:00","tagIds":[],"reminderRules":[]}],"notes":[{"id":"note-v6","text":"版本六便签","date":"2030-06-07","isCompleted":false,"tagIds":["tag-life"],"reminderRules":[]}],"reminderStates":{}}""";
    private const string VersionSevenAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":20000.5,"workStart":"09:00","workEnd":"18:30","themePreference":"Auto","tags":[{"id":"tag-focus","name":"专注","color":"#FFD36B"}],"countdowns":[{"id":"countdown-v7","name":"版本七倒计时","targetUtc":"2030-07-08T09:10:11.0000000+00:00","tagIds":["tag-focus"],"reminderRules":[]}],"notes":[{"id":"note-v7","text":"版本七便签","date":"2030-07-08","isCompleted":false,"tagIds":["tag-focus"],"reminderRules":[{"id":"legacy-e5a485295c012398f06c1cf94f8deb88d9e6402d6289a7f45e88a5bcaad17ff7","type":"fixedInterval","firstDueUtc":"2030-07-08T01:30:00.0000000+00:00","intervalMinutes":30}]}],"reminderStates":{"legacy-e5a485295c012398f06c1cf94f8deb88d9e6402d6289a7f45e88a5bcaad17ff7":{"handledThroughUtc":"2031-09-10T02:00:00.0000000+00:00","lastDeliveredOccurrenceKey":null,"lastDispositionOccurrenceKey":null,"activeSnooze":null}}}""";
    private const string VersionEightAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":20000.5,"workStart":"09:00","workEnd":"18:30","themePreference":"Auto","tags":[{"id":"tag-focus","name":"专注","color":"#FFD36B"}],"countdowns":[{"id":"countdown-v8","name":"版本八倒计时","targetUtc":"2030-08-09T10:11:12.0000000+00:00","tagIds":["tag-focus"],"reminderRules":[]}],"notes":[{"id":"note-v8","text":"版本八便签","date":"2030-08-09","isCompleted":false,"tagIds":["tag-focus"],"reminderRules":[]}],"reminderStates":{}}""";
    private const string VersionNineAfter = """{"schemaVersion":10,"timeZoneId":"China Standard Time","displayPrecision":"Second","monthlySalary":20000.5,"workStart":"09:00","workEnd":"18:30","themePreference":"Night","tags":[],"countdowns":[{"id":"countdown-v9","name":"版本九倒计时","targetUtc":"2031-09-10T11:12:13.0000000+00:00","tagIds":[],"reminderRules":[]}],"notes":[{"id":"note-v9-none","text":"不提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminderRules":[]},{"id":"note-v9-pending","text":"待送达一次提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminderRules":[{"id":"legacy-575ccb297c563230aa21b2051e9faf2264ab9bb4f9c023898010cd834c38ac16","type":"absoluteOnce","scheduledUtc":"2031-09-10T01:30:00.0000000+00:00"}]},{"id":"note-v9-delivered","text":"已送达一次提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminderRules":[{"id":"legacy-073133271e6686572573f41fcc4341d5a125faba8f6baddae03b60b79c70d1ab","type":"absoluteOnce","scheduledUtc":"2031-09-10T02:30:00.0000000+00:00"}]},{"id":"note-v9-repeat","text":"已开始循环提醒","date":"2031-09-10","isCompleted":false,"tagIds":[],"reminderRules":[{"id":"legacy-d71c35d11bb6b27f66e7c582ef71e036a4f36314a86250d45318b5d32763ca75","type":"fixedInterval","firstDueUtc":"2031-09-10T01:00:00.0000000+00:00","intervalMinutes":30}]}],"reminderStates":{"legacy-073133271e6686572573f41fcc4341d5a125faba8f6baddae03b60b79c70d1ab":{"handledThroughUtc":"2031-09-10T02:30:00.0000000+00:00","lastDeliveredOccurrenceKey":"legacy-073133271e6686572573f41fcc4341d5a125faba8f6baddae03b60b79c70d1ab|2031-09-10T02:30:00.0000000+00:00","lastDispositionOccurrenceKey":null,"activeSnooze":null},"legacy-d71c35d11bb6b27f66e7c582ef71e036a4f36314a86250d45318b5d32763ca75":{"handledThroughUtc":"2031-09-10T02:00:00.0000000+00:00","lastDeliveredOccurrenceKey":null,"lastDispositionOccurrenceKey":null,"activeSnooze":null}}}""";

    public static IReadOnlyList<DeploymentVersionFixture> Versions { get; } =
    [
        new(1, SettingsFixtures.VersionOne, VersionOneAfter, false, SettingsDeploymentEquivalence.UnsupportedSchemaReason),
        new(2, SettingsFixtures.VersionTwo, VersionTwoAfter, false, SettingsDeploymentEquivalence.UnsupportedSchemaReason),
        new(3, SettingsFixtures.VersionThree, VersionThreeAfter, true, SettingsDeploymentEquivalence.MigratedReason),
        new(4, SettingsFixtures.VersionFour, VersionFourAfter, true, SettingsDeploymentEquivalence.MigratedReason),
        new(5, SettingsFixtures.VersionFive, VersionFiveAfter, true, SettingsDeploymentEquivalence.MigratedReason),
        new(6, SettingsFixtures.VersionSix, VersionSixAfter, true, SettingsDeploymentEquivalence.MigratedReason),
        new(7, SettingsFixtures.VersionSeven, VersionSevenAfter, true, SettingsDeploymentEquivalence.MigratedReason),
        new(8, SettingsFixtures.VersionEight, VersionEightAfter, true, SettingsDeploymentEquivalence.MigratedReason),
        new(9, SettingsFixtures.VersionNine, VersionNineAfter, true, SettingsDeploymentEquivalence.MigratedReason)
    ];

    public static IReadOnlyList<DeploymentMutationFixture> Cases { get; } =
    [
        new("v3-added-countdown-reminder", 3, "add-countdown-reminder", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v9-reordered-notes", 9, "reverse-notes", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v5-forged-note-date", 5, "forge-note-date", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v8-invented-day-theme", 8, "invent-day-theme", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v5-added-note-reminder", 5, "add-note-reminder", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v9-forged-delivery", 9, "forge-delivery", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v9-forged-disposition", 9, "forge-disposition", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v9-cursor-beyond-frozen-migration", 9, "advance-repeat-cursor", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v3-different-target-instant", 3, "change-target", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v3-extra-property", 3, "extra-property", false, SettingsDeploymentEquivalence.InvalidSettingsReason),
        new("v3-missing-property", 3, "missing-property", false, SettingsDeploymentEquivalence.InvalidSettingsReason),
        new("v3-case-changed-property", 3, "case-changed-property", false, SettingsDeploymentEquivalence.DataMismatchReason),
        new("v3-after-not-v10", 3, "after-not-v10", false, SettingsDeploymentEquivalence.UnsupportedSchemaReason),
        new("v3-target-same-instant-offset", 3, "target-same-instant-offset", true, SettingsDeploymentEquivalence.MigratedReason),
        new("v3-reordered-root-properties", 3, "reordered-root-properties", true, SettingsDeploymentEquivalence.MigratedReason),
        new("v3-numeric-representation", 3, "numeric-representation", true, SettingsDeploymentEquivalence.MigratedReason),
        new("v3-malformed-after", 3, "malformed-after", false, SettingsDeploymentEquivalence.InvalidSettingsReason),
        new("v10-changed-canonical-value", 10, "change-v10-value", false, SettingsDeploymentEquivalence.UnsupportedSchemaReason)
    ];

    public static string AfterJson(int version) => version == 10
        ? SettingsFixtures.VersionTen
        : Versions.Single(fixture => fixture.Version == version).AfterJson;

    public static string BeforeJson(int version) => version switch
    {
        1 => SettingsFixtures.VersionOne,
        2 => SettingsFixtures.VersionTwo,
        3 => SettingsFixtures.VersionThree,
        4 => SettingsFixtures.VersionFour,
        5 => SettingsFixtures.VersionFive,
        6 => SettingsFixtures.VersionSix,
        7 => SettingsFixtures.VersionSeven,
        8 => SettingsFixtures.VersionEight,
        9 => SettingsFixtures.VersionNine,
        10 => SettingsFixtures.VersionTen,
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };

    public static string MutateAfter(string json, string mutation)
    {
        if (mutation == "malformed-after")
        {
            return "{not-json";
        }

        if (mutation == "numeric-representation")
        {
            return json.Replace("12345.67", "12345.670", StringComparison.Ordinal);
        }

        var root = JsonNode.Parse(json)!.AsObject();
        switch (mutation)
        {
            case "add-countdown-reminder":
                root["countdowns"]![0]!["reminderRules"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = "invented",
                        ["type"] = "absoluteOnce",
                        ["scheduledUtc"] = "2030-03-04T05:00:00.0000000+00:00"
                    });
                break;
            case "reverse-notes":
                var notes = root["notes"]!.AsArray();
                root["notes"] = new JsonArray(notes.Reverse().Select(node => node!.DeepClone()).ToArray());
                break;
            case "forge-note-date":
                root["notes"]![0]!["date"] = "2030-01-01";
                break;
            case "invent-day-theme":
                root["themePreference"] = "Day";
                break;
            case "add-note-reminder":
                root["notes"]![0]!["reminderRules"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = "invented-note",
                        ["type"] = "absoluteOnce",
                        ["scheduledUtc"] = "2030-05-06T07:00:00.0000000+00:00"
                    });
                break;
            case "forge-delivery":
            case "forge-disposition":
                var pendingRuleId = root["notes"]![1]!["reminderRules"]![0]!["id"]!.GetValue<string>();
                var pendingKey = $"{pendingRuleId}|2031-09-10T01:30:00.0000000+00:00";
                root["reminderStates"]![pendingRuleId] = new JsonObject
                {
                    ["handledThroughUtc"] = "2031-09-10T01:30:00.0000000+00:00",
                    ["lastDeliveredOccurrenceKey"] = pendingKey,
                    ["lastDispositionOccurrenceKey"] = mutation == "forge-disposition" ? pendingKey : null,
                    ["activeSnooze"] = null
                };
                break;
            case "advance-repeat-cursor":
                var repeatRuleId = root["notes"]![3]!["reminderRules"]![0]!["id"]!.GetValue<string>();
                root["reminderStates"]![repeatRuleId]!["handledThroughUtc"] = "2031-09-10T03:00:00.0000000+00:00";
                break;
            case "change-target":
                root["countdowns"]![0]!["targetUtc"] = "2030-03-04T05:06:08.0000000+00:00";
                break;
            case "extra-property":
                root["extra"] = true;
                break;
            case "missing-property":
                root.Remove("workEnd");
                break;
            case "case-changed-property":
                var workEnd = root["workEnd"]!.GetValue<string>();
                root.Remove("workEnd");
                root["WorkEnd"] = workEnd;
                break;
            case "after-not-v10":
                root["schemaVersion"] = 9;
                break;
            case "target-same-instant-offset":
                root["countdowns"]![0]!["targetUtc"] = "2030-03-04T13:06:07.0000000+08:00";
                break;
            case "reordered-root-properties":
                var reordered = new JsonObject();
                foreach (var property in root.Reverse())
                {
                    reordered[property.Key] = property.Value?.DeepClone();
                }

                root = reordered;
                break;
            case "change-v10-value":
                root["displayPrecision"] = "Day";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "未知部署等价反例。");
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}

internal sealed record DeploymentVersionFixture(
    int Version,
    string BeforeJson,
    string AfterJson,
    bool ExpectedEquivalent,
    string ExpectedReason);

internal sealed record DeploymentMutationFixture(
    string Name,
    int Version,
    string Mutation,
    bool ExpectedEquivalent,
    string ExpectedReason);
