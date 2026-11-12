using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinminCountdown.Core;

public sealed record SettingsDeploymentEquivalenceResult(bool Equivalent, string ReasonCategory);

public static class SettingsDeploymentEquivalence
{
    public const string UnchangedReason = "unchanged";
    public const string MigratedReason = "migrated";
    public const string UnsupportedSchemaReason = "unsupported-schema";
    public const string InvalidSettingsReason = "invalid-settings";
    public const string DataMismatchReason = "data-mismatch";

    public static SettingsDeploymentEquivalenceResult Evaluate(
        ReadOnlyMemory<byte> before,
        ReadOnlyMemory<byte> after,
        DateTimeOffset migrationUtc)
    {
        if (SHA256.HashData(before.Span).AsSpan().SequenceEqual(SHA256.HashData(after.Span)))
        {
            return new SettingsDeploymentEquivalenceResult(true, UnchangedReason);
        }

        try
        {
            var beforeJson = Decode(before);
            var afterJson = Decode(after);
            var beforeVersion = ReadSchemaVersion(beforeJson);
            var afterVersion = ReadSchemaVersion(afterJson);
            if (beforeVersion is < 3 or > 9 || afterVersion != SettingsSchema.CurrentVersion)
            {
                return new SettingsDeploymentEquivalenceResult(false, UnsupportedSchemaReason);
            }

            var expected = SettingsContract.SerializeCanonical(
                SettingsContract.Deserialize(beforeJson, migrationUtc: migrationUtc).Settings);
            // The PowerShell implementation validates the actual v10 payload as well as
            // comparing it with the expected migration; invalid or foreign fields cannot
            // be hidden by the comparer.
            _ = SettingsContract.Deserialize(afterJson, migrationUtc: migrationUtc);
            using var expectedDocument = JsonDocument.Parse(expected);
            using var actualDocument = JsonDocument.Parse(afterJson);
            return JsonEquivalent(expectedDocument.RootElement, actualDocument.RootElement, propertyName: null)
                ? new SettingsDeploymentEquivalenceResult(true, MigratedReason)
                : new SettingsDeploymentEquivalenceResult(false, DataMismatchReason);
        }
        catch (Exception exception) when (
            exception is SettingsValidationException or
                JsonException or
                DecoderFallbackException or
                FormatException or
                InvalidDataException or
                OverflowException)
        {
            return new SettingsDeploymentEquivalenceResult(false, InvalidSettingsReason);
        }
    }

    private static string Decode(ReadOnlyMemory<byte> json)
    {
        using var stream = new MemoryStream(json.ToArray(), writable: false);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static int ReadSchemaVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("schemaVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var parsed))
        {
            throw new SettingsValidationException("设置文件缺少有效的 schemaVersion。");
        }

        return parsed;
    }

    private static bool JsonEquivalent(
        JsonElement expected,
        JsonElement actual,
        string? propertyName)
    {
        if (expected.ValueKind == JsonValueKind.Object || actual.ValueKind == JsonValueKind.Object)
        {
            if (expected.ValueKind != JsonValueKind.Object || actual.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var expectedProperties = expected.EnumerateObject().ToArray();
            var actualProperties = actual.EnumerateObject().ToArray();
            if (expectedProperties.Length != actualProperties.Length)
            {
                return false;
            }

            foreach (var expectedProperty in expectedProperties)
            {
                var matches = actualProperties
                    .Where(property => string.Equals(
                        property.Name,
                        expectedProperty.Name,
                        StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1 ||
                    !JsonEquivalent(expectedProperty.Value, matches[0].Value, expectedProperty.Name))
                {
                    return false;
                }
            }

            return true;
        }

        if (expected.ValueKind == JsonValueKind.Array || actual.ValueKind == JsonValueKind.Array)
        {
            if (expected.ValueKind != JsonValueKind.Array || actual.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var expectedItems = expected.EnumerateArray().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            if (expectedItems.Length != actualItems.Length)
            {
                return false;
            }

            for (var index = 0; index < expectedItems.Length; index++)
            {
                if (!JsonEquivalent(expectedItems[index], actualItems[index], propertyName))
                {
                    return false;
                }
            }

            return true;
        }

        if (string.Equals(propertyName, "targetUtc", StringComparison.Ordinal) &&
            expected.ValueKind == JsonValueKind.String &&
            actual.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                expected.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var expectedTarget) &&
            DateTimeOffset.TryParse(
                actual.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var actualTarget))
        {
            return expectedTarget.UtcTicks == actualTarget.UtcTicks;
        }

        if (expected.ValueKind == JsonValueKind.Number || actual.ValueKind == JsonValueKind.Number)
        {
            if (expected.ValueKind != JsonValueKind.Number || actual.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            if (expected.TryGetDecimal(out var expectedDecimal) &&
                actual.TryGetDecimal(out var actualDecimal))
            {
                return expectedDecimal == actualDecimal;
            }

            return expected.GetDouble() == actual.GetDouble();
        }

        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        return expected.ValueKind switch
        {
            JsonValueKind.String => string.Equals(
                expected.GetString(),
                actual.GetString(),
                StringComparison.Ordinal),
            JsonValueKind.True or JsonValueKind.False => expected.GetBoolean() == actual.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false
        };
    }
}
