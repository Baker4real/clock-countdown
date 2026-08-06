using System.Collections.ObjectModel;
using System.Text.Json;

namespace MinminCountdown;

internal sealed class ValidationReportProjection
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private bool _sealed;

    public object? this[string key]
    {
        set => Add(key, value);
    }

    public void Add(string key, object? value)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("validate-only report 已封存，不能继续追加字段。");
        }

        if (!_values.TryAdd(key, value))
        {
            throw new InvalidOperationException($"validate-only report 包含重复字段：{key}");
        }
    }

    public IReadOnlyDictionary<string, object?> Seal()
    {
        if (_sealed)
        {
            throw new InvalidOperationException("validate-only report 不能重复封存。");
        }

        var snapshot = ValidationReportContract.Seal(_values);
        _sealed = true;
        return snapshot;
    }
}

internal static class ValidationReportContract
{
    private const string ContractBehaviorField = "ValidationReportContractBehavior";
    private const string ModuleDepthField = "ValidationReportModuleDeep";

    private static readonly string[] RequiredTrueBooleanFields =
    [
        "Valid",
        "DesktopWorkspaceInterfaceBehavior",
        "ReminderEditingInterfaceBehavior",
        "StartupAcceptanceProtocolInterfaceBehavior",
        "HolidayCalendarInterfaceBehavior",
        "MainReferenceDecorationsShareScaleAtMinimumAndWideSizes",
        "MainReferenceDecorationLayerOrderMatches",
        "MainWorkspaceResourcesFollowDayNightTheme",
        "MainDynamicCountdownCardsUseThemeResources",
        "MainDynamicNoteCardsUseThemeResources",
        "FloatingReferenceContentSafeAreaMatches",
        "FloatingReferenceBaselineGateIsolationBehavior",
        "FloatingReferenceFreshnessGateIsolationBehavior",
        "FloatingLongNotesBodyHasNoManualLineBreaks",
        "FloatingLongNotesNaturalWraps",
        "FloatingLongNotesUsesOnlyOuterScroller",
        "FloatingLongNotesOuterScrollable",
        "FloatingLongNotesTopBottomReachable",
        "FloatingLongNotesSafeAreaMatches",
        "FloatingLongNotesTypographyMatches",
        "FloatingLongNotesLayoutValid",
        "FloatingTitleUsesSemiBoldWeight",
        "FloatingNoteInputWraps",
        "FloatingNoteInputScrolls",
        "FloatingInitialWorkspaceStateMatches",
        "FloatingWorkspaceSwitchingStaysSynchronized",
        "FloatingCountdownSelectionSurvivesDayNightThemes",
        "FloatingQuickAddDayResourcesFollowLocalTheme",
        "FloatingQuickAddNightResourcesFollowLocalTheme",
        "FloatingQuickAddIndependentFromApplicationAutoTheme",
        "FloatingQuickAddThemeResourceIsolationBehavior",
        "FloatingQuickAddDayExpandedScrolledCaptureWritten",
        "FloatingQuickAddNightExpandedScrolledCaptureWritten",
        "FloatingNightPlushSourceAlphaBoundsMatch",
        "FloatingNightPlushEdgeBrightnessWithinLimit",
        "FloatingNightPlushEdgeSaturationWithinLimit",
        "FloatingNightPlushEdgeGlowGateIsolationBehavior",
        "FloatingPrimaryCountdownTextContrast",
        "FloatingPrimaryCountdownAccentSeparatedFromPlushGlow",
        "FloatingPrimaryCountdownContrastGateIsolationBehavior",
        "FloatingSecondaryRemainingUnitSpacingMatches",
        "FloatingSecondaryRemainingLayoutMatches",
        "FloatingSecondaryRemainingGateIsolationBehavior",
        "FloatingScrollBarLocalStyleScoped",
        "FloatingScrollBarHitTargetUsable",
        "FloatingScrollBarVisualWeightProgression",
        "FloatingScrollBarUsesWarmThemeTokens",
        "FloatingScrollBarContentRemainsSafe",
        "FloatingScrollBarInputPathsWork",
        "FloatingScrollBarGateIsolationBehavior",
        "FloatingScrollBarDayNightStateCapturesReady",
        "UiQualityGateIsolationBehavior"
    ];

    private static readonly string[] RequiredPositiveIntegerFields =
    [
        "MainDynamicCountdownCardSampleCount",
        "MainDynamicNoteCardSampleCount"
    ];

    internal static IReadOnlyDictionary<string, object?> Seal(
        Dictionary<string, object?> report)
    {
        RunBehaviorExercise();
        return SealCore(report);
    }

    private static IReadOnlyDictionary<string, object?> SealCore(
        Dictionary<string, object?> report)
    {
        if (report.ContainsKey(ContractBehaviorField) || report.ContainsKey(ModuleDepthField))
        {
            throw new InvalidDataException(
                "validate-only report 的契约结果字段只能由 Seal 追加。");
        }

        Validate(report);
        report.Add(ContractBehaviorField, true);
        report.Add(ModuleDepthField, true);

        var snapshot = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(report, StringComparer.Ordinal));
        EnsureStableJsonObject(snapshot);
        return snapshot;
    }

    private static void Validate(IReadOnlyDictionary<string, object?> report)
    {
        var json = JsonSerializer.Serialize(report);
        var repeatedJson = JsonSerializer.Serialize(report);
        if (!string.Equals(json, repeatedJson, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "同一 validate-only report 的 JSON 序列化结果不稳定。");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        EnsureJsonObject(root, report.Count);

        foreach (var field in RequiredTrueBooleanFields)
        {
            if (!report.TryGetValue(field, out var value) || value is not bool typedValue)
            {
                throw new InvalidDataException(
                    $"validate-only report 缺少 bool 字段或字段类型错误：{field}");
            }

            if (!typedValue)
            {
                throw new InvalidDataException(
                    $"validate-only report 的行为字段未通过：{field}");
            }
        }

        foreach (var field in RequiredPositiveIntegerFields)
        {
            if (!report.TryGetValue(field, out var value) ||
                value is not int typedValue ||
                typedValue <= 0)
            {
                throw new InvalidDataException(
                    $"validate-only report 缺少正 int 字段、字段类型错误或未命中样本：{field}");
            }
        }

        var mainMode = ReadManifestMode(report, root, "MainReferenceBaseline");
        var floatingMode = ReadManifestMode(report, root, "FloatingReferenceBaseline");
        if (mainMode != floatingMode)
        {
            throw new InvalidDataException(
                "主窗口与悬浮窗参考基线必须处于同一 manifest 模式。");
        }

        var stressVisuals = ReadBoolean(report, "MainStressCountdownTitleVisuals");
        var stressMeasurementCount = ReadArrayLength(
            root,
            "MainStressCountdownTitleMeasurements");
        if (mainMode.Requested)
        {
            if (!stressVisuals || stressMeasurementCount != 2)
            {
                throw new InvalidDataException(
                    "源码树模式必须包含 Day/Night 两份主窗口 Stress 标题真实测量。");
            }
        }
        else if (stressVisuals || stressMeasurementCount != 0)
        {
            throw new InvalidDataException(
                "发布包模式不能伪造主窗口 Stress 标题视觉结果或测量。");
        }
    }

    private static ManifestMode ReadManifestMode(
        IReadOnlyDictionary<string, object?> report,
        JsonElement root,
        string prefix)
    {
        var requested = ReadBoolean(report, $"{prefix}Requested");
        var manifestAvailable = ReadBoolean(report, $"{prefix}ManifestAvailable");
        var validated = ReadBoolean(report, $"{prefix}Validated");
        var captureCount = ReadArrayLength(root, $"{prefix}Captures");

        if (requested)
        {
            if (!manifestAvailable || !validated || captureCount == 0)
            {
                throw new InvalidDataException(
                    $"{prefix} 源码树模式必须有 manifest、通过验证并包含真实 capture。");
            }
        }
        else if (manifestAvailable || validated || captureCount != 0)
        {
            throw new InvalidDataException(
                $"{prefix} 发布包模式不能声明 manifest/验证成功或伪造 capture。");
        }

        return new ManifestMode(requested, manifestAvailable, validated);
    }

    private static bool ReadBoolean(
        IReadOnlyDictionary<string, object?> report,
        string field)
    {
        if (!report.TryGetValue(field, out var value) || value is not bool typedValue)
        {
            throw new InvalidDataException(
                $"validate-only report 缺少 bool 字段或字段类型错误：{field}");
        }

        return typedValue;
    }

    private static int ReadArrayLength(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"validate-only report 缺少 array 字段或字段类型错误：{field}");
        }

        return value.GetArrayLength();
    }

    private static void EnsureJsonObject(JsonElement root, int expectedPropertyCount)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("validate-only report 未序列化为 JSON object。");
        }

        var propertyNames = root.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        if (propertyNames.Length != expectedPropertyCount ||
            propertyNames.Distinct(StringComparer.Ordinal).Count() != propertyNames.Length)
        {
            throw new InvalidDataException("validate-only report 的顶层字段不唯一。");
        }
    }

    private static void EnsureStableJsonObject(
        IReadOnlyDictionary<string, object?> report)
    {
        var json = JsonSerializer.Serialize(report);
        var repeatedJson = JsonSerializer.Serialize(report);
        if (!string.Equals(json, repeatedJson, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "封存后的 validate-only report JSON 序列化结果不稳定。");
        }

        using var document = JsonDocument.Parse(json);
        EnsureJsonObject(document.RootElement, report.Count);
    }

    private static void RunBehaviorExercise()
    {
        var duplicateProjection = new ValidationReportProjection
        {
            ["DuplicateKey"] = true
        };
        MustThrow<InvalidOperationException>(
            () => duplicateProjection["DuplicateKey"] = false,
            "projection 未立即拒绝重复 key。");

        MustReject(
            report => report.Remove("Valid"),
            false,
            "契约未拒绝缺失的 required bool 字段。");
        MustReject(
            report => report["Valid"] = "true",
            false,
            "契约未拒绝 required bool 字段的类型错误。");
        MustReject(
            report => report["MainDynamicCountdownCardSampleCount"] = 1L,
            false,
            "契约未拒绝 required int 字段的类型错误。");
        MustReject(
            report => report["FloatingReferenceBaselineRequested"] = false,
            true,
            "契约未拒绝 Main/Floating manifest 模式不一致。");
        MustReject(
            report => report["MainReferenceBaselineManifestAvailable"] = true,
            false,
            "契约未拒绝 Requested=false 但 ManifestAvailable=true。");
        MustReject(
            report => report["FloatingReferenceBaselineValidated"] = true,
            false,
            "契约未拒绝 Requested=false 但 Validated=true。");
        MustReject(
            report => report["MainReferenceBaselineCaptures"] = Array.Empty<object>(),
            true,
            "契约未拒绝源码树模式缺少主窗口 capture。");
        MustReject(
            report => report["FloatingReferenceBaselineCaptures"] = Array.Empty<object>(),
            true,
            "契约未拒绝源码树模式缺少悬浮窗 capture。");
        MustReject(
            report => report["MainStressCountdownTitleMeasurements"] = Array.Empty<object>(),
            true,
            "契约未拒绝源码树模式缺少 Stress 标题测量。");
        MustReject(
            report => report["MainReferenceBaselineCaptures"] = new object[] { new() },
            false,
            "契约未拒绝发布包伪造主窗口 capture。");
        MustReject(
            report => report["FloatingReferenceBaselineCaptures"] = new object[] { new() },
            false,
            "契约未拒绝发布包伪造悬浮窗 capture。");
        MustReject(
            report => report["MainStressCountdownTitleVisuals"] = true,
            false,
            "契约未拒绝发布包伪造 Stress 标题视觉结果。");

        var sourceSnapshot = SealCore(CreateBehaviorFixture(requested: true));
        var firstJson = JsonSerializer.Serialize(sourceSnapshot);
        var secondJson = JsonSerializer.Serialize(sourceSnapshot);
        if (!string.Equals(firstJson, secondJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("同一封存报告的两次 JSON 输出不一致。");
        }

        var packageSnapshot = SealCore(CreateBehaviorFixture(requested: false));
        if (packageSnapshot[ContractBehaviorField] is not true ||
            packageSnapshot[ModuleDepthField] is not true)
        {
            throw new InvalidOperationException("Seal 未追加两个既有契约结果字段。");
        }

        MustThrow<NotSupportedException>(
            () => ((IDictionary<string, object?>)packageSnapshot).Add("Unexpected", true),
            "Seal 返回的报告快照仍可变。");
    }

    private static void MustReject(
        Action<Dictionary<string, object?>> mutate,
        bool requested,
        string message)
    {
        var report = CreateBehaviorFixture(requested);
        mutate(report);
        MustThrow<InvalidDataException>(() => SealCore(report), message);
    }

    private static Dictionary<string, object?> CreateBehaviorFixture(bool requested)
    {
        var report = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in RequiredTrueBooleanFields)
        {
            report.Add(field, true);
        }

        foreach (var field in RequiredPositiveIntegerFields)
        {
            report.Add(field, 1);
        }

        foreach (var prefix in new[]
                 {
                     "MainReferenceBaseline",
                     "FloatingReferenceBaseline"
                 })
        {
            report.Add($"{prefix}Requested", requested);
            report.Add($"{prefix}ManifestAvailable", requested);
            report.Add($"{prefix}Validated", requested);
            report.Add(
                $"{prefix}Captures",
                requested ? new object[] { new() } : Array.Empty<object>());
        }

        report.Add("MainStressCountdownTitleVisuals", requested);
        report.Add(
            "MainStressCountdownTitleMeasurements",
            requested ? new object[] { new(), new() } : Array.Empty<object>());
        return report;
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

    private sealed record ManifestMode(
        bool Requested,
        bool ManifestAvailable,
        bool Validated);
}
