using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinminCountdown;

// 一条必须为真的验证事实：报告里仍是同名 bool 字段，Meaning 是失败时给人看的中文含义。
internal readonly record struct ValidationFact(bool Passed, string Meaning);

internal sealed class ValidationReportProjection
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _facts = new(StringComparer.Ordinal);
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

        var fact = value as ValidationFact?;
        if (!_values.TryAdd(key, fact is { } declared ? declared.Passed : value))
        {
            throw new InvalidOperationException($"validate-only report 包含重复字段：{key}");
        }

        if (fact is { } declaredFact)
        {
            _facts.Add(key, declaredFact.Meaning);
        }
    }

    public IReadOnlyDictionary<string, object?> Seal(ReferenceBaselineDeferral? deferral)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("validate-only report 不能重复封存。");
        }

        var snapshot = ValidationReportContract.Seal(_values, _facts, deferral);
        _sealed = true;
        return snapshot;
    }
}

// 改了视觉源码而尚未重拍时，参考基线必然过期；窗口套件要跑完其余断言，不能停在 validate-only。
// 只有套件脚本预先建好记录文件并经环境变量交给应用时才开启：过期项写进报告与记录文件，
// 套件收尾时据记录以失败退出。生产启动、安装验证和基线生成都不带这个变量，照旧严格失败。
internal sealed class ReferenceBaselineDeferral
{
    internal const string LogPathVariable = "MINMIN_COUNTDOWN_REFERENCE_BASELINE_DEFERRAL_LOG";
    private readonly Action<IReadOnlyList<string>> _record;

    private ReferenceBaselineDeferral(Action<IReadOnlyList<string>> record) => _record = record;

    internal static ReferenceBaselineDeferral? FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable(LogPathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new InvalidDataException($"{LogPathVariable} 必须指向套件预先建好的记录文件。");
        }

        return new(items => File.AppendAllLines(path, items, new UTF8Encoding(false)));
    }

    internal static ReferenceBaselineDeferral InMemory(List<string> sink) => new(sink.AddRange);

    internal void Record(IReadOnlyList<string> items) => _record(items);
}

internal static class ValidationReportContract
{
    internal const string ReferenceBaselinesCurrentFact = "CrossInterfaceReferenceBaselinesCurrent";
    private const string ValidField = "Valid";
    private const string ContractBehaviorField = "ValidationReportContractBehavior";
    private const string ModuleDepthField = "ValidationReportModuleDeep";
    private const string DeferredField = "DeferredReferenceBaselineFailures";
    private const int MaximumDiagnosticCount = 12;
    private const int MaximumDiagnosticLength = 240;
    private static readonly Regex DiagnosticPathPattern = new(
        @"(?i)(?:[a-z]:[\\/]|\\\\)[^\s,;|]+",
        RegexOptions.CultureInvariant);
    private static readonly Regex DiagnosticWhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant);

    private static readonly string[] RequiredPositiveIntegerFields =
    [
        "MainDynamicCountdownCardSampleCount",
        "MainDynamicNoteCardSampleCount"
    ];

    internal static IReadOnlyDictionary<string, object?> Seal(
        Dictionary<string, object?> report,
        IReadOnlyDictionary<string, string> facts,
        ReferenceBaselineDeferral? deferral)
    {
        RunBehaviorExercise(facts);
        return SealCore(report, facts, deferral);
    }

    private static IReadOnlyDictionary<string, object?> SealCore(
        Dictionary<string, object?> report,
        IReadOnlyDictionary<string, string> facts,
        ReferenceBaselineDeferral? deferral = null)
    {
        if (report.ContainsKey(ValidField) ||
            report.ContainsKey(ContractBehaviorField) ||
            report.ContainsKey(ModuleDepthField) ||
            report.ContainsKey(DeferredField))
        {
            throw new InvalidDataException(
                "validate-only report 的结论与契约结果字段只能由 Seal 追加。");
        }

        var deferred = Validate(report, facts, deferStaleReferenceBaselines: deferral is not null);
        // 走到这里说明除显式延后的参考基线过期项外，每条声明的验证事实都为真；Valid 是这一结论，不再单独赋值。
        report.Add(ValidField, true);
        report.Add(ContractBehaviorField, true);
        report.Add(ModuleDepthField, true);
        if (deferral is not null)
        {
            report.Add(DeferredField, deferred.ToArray());
        }

        var snapshot = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(report, StringComparer.Ordinal));
        EnsureStableJsonObject(snapshot);
        if (deferred.Count > 0)
        {
            // 记录失败即 validate-only 失败：延后项不能只留在一份可能没人读的报告里。
            deferral!.Record(deferred);
        }

        return snapshot;
    }

    private static List<string> Validate(
        IReadOnlyDictionary<string, object?> report,
        IReadOnlyDictionary<string, string> facts,
        bool deferStaleReferenceBaselines)
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

        if (facts.Count == 0)
        {
            throw new InvalidDataException("validate-only report 没有声明任何验证事实。");
        }

        var failedFacts = new List<string>();
        var deferred = new List<string>();
        foreach (var (field, meaning) in facts)
        {
            if (!report.TryGetValue(field, out var value) || value is not bool typedValue)
            {
                throw new InvalidDataException(
                    $"validate-only report 缺少 bool 字段或字段类型错误：{field}");
            }

            if (!typedValue)
            {
                var deferrable = deferStaleReferenceBaselines &&
                    string.Equals(field, ReferenceBaselinesCurrentFact, StringComparison.Ordinal);
                (deferrable ? deferred : failedFacts).Add($"{field}（{meaning}）");
            }
        }

        if (failedFacts.Count > 0)
        {
            throw new InvalidDataException(
                $"validate-only report 的行为字段未通过：{string.Join(", ", failedFacts)}");
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

        var mainMode = ReadManifestMode(report, root, "MainReferenceBaseline", deferStaleReferenceBaselines, deferred);
        var floatingMode = ReadManifestMode(report, root, "FloatingReferenceBaseline", deferStaleReferenceBaselines, deferred);
        if (mainMode != floatingMode)
        {
            throw new InvalidDataException(
                "主窗口与悬浮窗参考基线必须处于同一 manifest 模式。");
        }

        var stressVisuals = ReadBoolean(report, "MainStressCountdownTitleVisuals");
        var stressMeasurementCount = ReadArrayLength(
            root,
            "MainStressCountdownTitleMeasurements");
        var noteCardReminderMeasurementCount = ReadArrayLength(
            root,
            "MainNoteCardReminderFontMeasurements");
        var noteCardReminderMeetsContract = ReadBoolean(
            report,
            "MainNoteCardReminderFontSizeMeetsContract");
        if (mainMode.Requested)
        {
            if (!stressVisuals || stressMeasurementCount != 1)
            {
                throw new InvalidDataException(
                    "源码树模式必须包含单一日间主窗口 Stress 标题真实测量。");
            }

            if (noteCardReminderMeasurementCount == 0)
            {
                throw new InvalidDataException(
                    "源码树模式必须包含主窗口便签卡提醒摘要真实 DIP 测量。");
            }

            if (!noteCardReminderMeetsContract)
            {
                throw new InvalidDataException(
                    "源码树模式必须证明主窗口便签卡提醒摘要真实渲染达到 12 DIP。");
            }
        }
        else if (stressVisuals || stressMeasurementCount != 0)
        {
            throw new InvalidDataException(
                "发布包模式不能伪造主窗口 Stress 标题视觉结果或测量。");
        }
        else if (noteCardReminderMeetsContract || noteCardReminderMeasurementCount != 0)
        {
            throw new InvalidDataException(
                "发布包模式不能伪造主窗口便签卡提醒摘要测量。");
        }

        return deferred;
    }

    private static ManifestMode ReadManifestMode(
        IReadOnlyDictionary<string, object?> report,
        JsonElement root,
        string prefix,
        bool deferStale,
        List<string> deferred)
    {
        var requested = ReadBoolean(report, $"{prefix}Requested");
        var manifestAvailable = ReadBoolean(report, $"{prefix}ManifestAvailable");
        var validated = ReadBoolean(report, $"{prefix}Validated");
        var captureCount = ReadArrayLength(root, $"{prefix}Captures");

        if (requested)
        {
            if (!manifestAvailable || !validated || captureCount == 0)
            {
                var message = BuildManifestFailureMessage(
                    root,
                    prefix,
                    manifestAvailable,
                    validated,
                    captureCount);
                // 只有“manifest 在、实拍也写出了、比对没过”才是过期；缺 manifest 或没有实拍照旧失败。
                if (!(deferStale && manifestAvailable && captureCount > 0))
                {
                    throw new InvalidDataException(message);
                }

                deferred.Add(message);
            }
        }
        else if (manifestAvailable || validated || captureCount != 0)
        {
            throw new InvalidDataException(
                $"{prefix} 发布包模式不能声明 manifest/验证成功或伪造 capture。");
        }

        // 模式只看请求与 manifest 是否在场：延后时主窗口与悬浮窗可以只有一侧过期。
        return new ManifestMode(requested, manifestAvailable);
    }

    private static string BuildManifestFailureMessage(
        JsonElement root,
        string prefix,
        bool manifestAvailable,
        bool validated,
        int captureCount)
    {
        var falseGates = ReadFalseGateNames(root, $"{prefix}Gates");
        var diagnostics = ReadDiagnosticSummaries(root, $"{prefix}Diagnostics");
        var comparisons = ReadCaptureComparisons(root, $"{prefix}Captures");
        var comparisonSuffix = comparisons.Count == 0
            ? string.Empty
            : $" comparison=[{string.Join("; ", comparisons)}]";
        return $"{prefix} 源码树模式验证失败：" +
               $"manifestAvailable={manifestAvailable.ToString().ToLowerInvariant()}, " +
               $"validated={validated.ToString().ToLowerInvariant()}, " +
               $"captureCount={captureCount}, " +
               $"falseGates=[{FormatSummary(falseGates)}], " +
               $"diagnostics=[{FormatSummary(diagnostics)}]。" +
               comparisonSuffix;
    }

    private static IReadOnlyList<string> ReadCaptureComparisons(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var captures) ||
            captures.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var capture in captures.EnumerateArray())
        {
            if (capture.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryReadString(capture, "CaptureId", out var captureId) &&
                !TryReadString(capture, "captureId", out captureId))
            {
                continue;
            }

            if (!TryGetProperty(capture, "Comparison", out var comparison) &&
                !TryGetProperty(capture, "comparison", out comparison) ||
                comparison.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            lines.Add(
                $"{captureId}:" +
                $"max={ReadNumber(comparison, "MaxAbsoluteChannelDelta", "maxAbsoluteChannelDelta")}/" +
                $"mae={ReadNumber(comparison, "MeanAbsoluteError", "meanAbsoluteError")}/" +
                $"rmse={ReadNumber(comparison, "RootMeanSquareError", "rootMeanSquareError")}/" +
                $"changed={ReadNumber(comparison, "ChangedPixelCount", "changedPixelCount")}");
        }

        return lines;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value);

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static string ReadNumber(JsonElement element, string pascalName, string camelName)
    {
        if (element.TryGetProperty(pascalName, out var value) ||
            element.TryGetProperty(camelName, out value))
        {
            return value.ValueKind == JsonValueKind.Number ? value.ToString() : value.ValueKind.ToString();
        }

        return "?";
    }

    private static IReadOnlyList<string> ReadFalseGateNames(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var gates) || gates.ValueKind != JsonValueKind.Object)
        {
            return ["<unavailable>"];
        }

        return gates.EnumerateObject()
            .Where(property =>
                property.Value.ValueKind == JsonValueKind.False)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadDiagnosticSummaries(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Array)
        {
            return ["<unavailable>"];
        }

        return diagnostics.EnumerateArray()
            .Take(MaximumDiagnosticCount)
            .Select(element => element.ValueKind == JsonValueKind.String
                ? SanitizeDiagnostic(element.GetString() ?? string.Empty)
                : "<non-string>")
            .ToArray();
    }

    private static string SanitizeDiagnostic(string value)
    {
        var sanitized = DiagnosticWhitespacePattern.Replace(value, " ").Trim();
        sanitized = DiagnosticPathPattern.Replace(sanitized, "<path>");
        if (sanitized.Length <= MaximumDiagnosticLength)
        {
            return sanitized;
        }

        return sanitized[..(MaximumDiagnosticLength - 3)] + "...";
    }

    private static string FormatSummary(IReadOnlyList<string> values) =>
        values.Count == 0 ? "<none>" : string.Join(", ", values);

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

    private static void RunBehaviorExercise(IReadOnlyDictionary<string, string> facts)
    {
        MustReject(facts, report => report["FloatingWorkdayCompletionBehavior"] = false, false,
            "契约未拒绝 #236 下班收尾行为失败。");
        MustReject(facts, report => report["FloatingIncomePrivacyBehavior"] = false, false,
            "契约未拒绝 #229 金额隐私行为失败。");
        foreach (var field in new[]
                 {
                     "CountdownListFirstBehavior", "CountdownPreferencesPopupBehavior",
                     "CountdownEditorExpansionBehavior", "CountdownRowDeadlineAndSecondaryDeleteBehavior",
                     "MainTopmostStateSynchronization", "MainRedundancyCleanup",
                     "MainNoteEditorSeparation", "MainNoteRowsSimplified", "MainNoteRowActionsKeyboardWorks",
                     "NoteGroupingSelectorAbsent", "MainTextShortcutBehavior", "SingleLineEnterSubmission",
                     "FloatingNoteKeyboardSubmission", "ListDeleteKeyboardBehavior"
                 })
        {
            MustReject(facts, report => report[field] = false, false,
                $"契约未拒绝主窗口行为失败：{field}。");
        }
        var duplicateProjection = new ValidationReportProjection
        {
            ["DuplicateKey"] = true
        };
        MustThrow<InvalidOperationException>(
            () => duplicateProjection["DuplicateKey"] = false,
            "projection 未立即拒绝重复 key。");

        // 夹具来自生产报告声明的验证事实：上面的关键行为若不再声明为事实，置 false 也不会被拒绝。
        var probeFact = facts.Keys.First();
        MustReject(
            facts,
            report => report.Remove(probeFact),
            false,
            "契约未拒绝缺失的验证事实字段。");
        MustReject(
            facts,
            report => report[probeFact] = "true",
            false,
            "契约未拒绝验证事实字段的类型错误。");
        MustRejectWithMessage(
            facts,
            report => report[probeFact] = false,
            $"validate-only report 的行为字段未通过：{probeFact}（{facts[probeFact]}）",
            "验证事实失败未连同中文含义列入失败清单。");
        MustReject(
            facts,
            report => report[ValidField] = true,
            false,
            "契约未拒绝报告自带 Valid 结论。");
        var noFacts = new Dictionary<string, string>(StringComparer.Ordinal);
        MustThrow<InvalidDataException>(
            () => SealCore(CreateBehaviorFixture(noFacts, requested: false), noFacts),
            "契约未拒绝没有声明验证事实的报告。");
        MustReject(
            facts,
            report => report["MainDynamicCountdownCardSampleCount"] = 1L,
            false,
            "契约未拒绝 required int 字段的类型错误。");
        MustReject(
            facts,
            report => report["FloatingReferenceBaselineRequested"] = false,
            true,
            "契约未拒绝 Main/Floating manifest 模式不一致。");
        MustReject(
            facts,
            report => report["MainReferenceBaselineManifestAvailable"] = true,
            false,
            "契约未拒绝 Requested=false 但 ManifestAvailable=true。");
        MustReject(
            facts,
            report => report["FloatingReferenceBaselineValidated"] = true,
            false,
            "契约未拒绝 Requested=false 但 Validated=true。");
        MustReject(
            facts,
            report => report["MainReferenceBaselineCaptures"] = Array.Empty<object>(),
            true,
            "契约未拒绝源码树模式缺少主窗口 capture。");
        MustReject(
            facts,
            report => report["FloatingReferenceBaselineCaptures"] = Array.Empty<object>(),
            true,
            "契约未拒绝源码树模式缺少悬浮窗 capture。");
        MustRejectWithMessage(
            facts,
            report => report["MainReferenceBaselineManifestAvailable"] = false,
            "MainReferenceBaseline 源码树模式验证失败：" +
            "manifestAvailable=false, validated=true, captureCount=1, " +
            "falseGates=[<none>], diagnostics=[<none>]。",
            "manifest unavailable 异常未报告精确状态。");
        MustRejectWithMessage(
            facts,
            report => report["MainReferenceBaselineValidated"] = false,
            "MainReferenceBaseline 源码树模式验证失败：" +
            "manifestAvailable=true, validated=false, captureCount=1, " +
            "falseGates=[<none>], diagnostics=[<none>]。",
            "validated false 异常未报告精确状态。");
        MustRejectWithMessage(
            facts,
            report => report["MainReferenceBaselineCaptures"] = Array.Empty<object>(),
            "MainReferenceBaseline 源码树模式验证失败：" +
            "manifestAvailable=true, validated=true, captureCount=0, " +
            "falseGates=[<none>], diagnostics=[<none>]。",
            "capture empty 异常未报告精确状态。");
        MustRejectWithMessage(
            facts,
            report =>
            {
                report["MainReferenceBaselineValidated"] = false;
                report["MainReferenceBaselineGates"] = new Dictionary<string, bool>(StringComparer.Ordinal)
                {
                    ["SourceHashMatches"] = true,
                    ["ExecutableHashMatches"] = false,
                    ["PngHashMatches"] = false
                };
                report["MainReferenceBaselineDiagnostics"] = new[]
                {
                    "  first\r\ndiagnostic  ",
                    @"source C:\private\visual\manifest.json mismatch"
                };
            },
            "MainReferenceBaseline 源码树模式验证失败：" +
            "manifestAvailable=true, validated=false, captureCount=1, " +
            "falseGates=[ExecutableHashMatches, PngHashMatches], " +
            "diagnostics=[first diagnostic, source <path> mismatch]。",
            "false gate/diagnostic 异常未报告脱敏精确原因。");
        MustReject(
            facts,
            report => report["MainStressCountdownTitleMeasurements"] = Array.Empty<object>(),
            true,
            "契约未拒绝源码树模式缺少 Stress 标题测量。");
        MustReject(
            facts,
            report => report["MainReferenceBaselineCaptures"] = new object[] { new() },
            false,
            "契约未拒绝发布包伪造主窗口 capture。");
        MustReject(
            facts,
            report => report["FloatingReferenceBaselineCaptures"] = new object[] { new() },
            false,
            "契约未拒绝发布包伪造悬浮窗 capture。");
        MustReject(
            facts,
            report => report["MainStressCountdownTitleVisuals"] = true,
            false,
            "契约未拒绝发布包伪造 Stress 标题视觉结果。");
        MustReject(
            facts,
            report => report["MainNoteCardReminderFontMeasurements"] = Array.Empty<object>(),
            true,
            "契约未拒绝源码树模式缺少便签卡提醒摘要测量。");
        MustReject(
            facts,
            report => report["MainNoteCardReminderFontSizeMeetsContract"] = false,
            true,
            "契约未拒绝源码树模式便签卡提醒摘要未达 12 DIP。");
        MustReject(
            facts,
            report => report["MainNoteCardReminderFontMeasurements"] = new object[] { new() },
            false,
            "契约未拒绝发布包伪造便签卡提醒摘要测量。");

        // 参考基线延后：只在显式开启时生效，只放过基线过期这一类失败，并把延后项同时写进报告与记录。
        if (!facts.ContainsKey(ReferenceBaselinesCurrentFact))
        {
            throw new InvalidOperationException($"参考基线一致性未声明为验证事实：{ReferenceBaselinesCurrentFact}。");
        }

        MustReject(
            facts,
            report => report[ReferenceBaselinesCurrentFact] = false,
            true,
            "未开启延后时契约未拒绝参考基线过期。");
        var staleRecord = new List<string>();
        var staleReport = CreateBehaviorFixture(facts, requested: true);
        staleReport[ReferenceBaselinesCurrentFact] = false;
        staleReport["FloatingReferenceBaselineValidated"] = false;
        var staleSnapshot = SealCore(staleReport, facts, ReferenceBaselineDeferral.InMemory(staleRecord));
        var staleDeferred = staleSnapshot[DeferredField] as string[];
        if (staleSnapshot[ValidField] is not true ||
            staleDeferred is not { Length: 2 } ||
            !staleDeferred.SequenceEqual(staleRecord, StringComparer.Ordinal) ||
            !staleDeferred[0].StartsWith(ReferenceBaselinesCurrentFact, StringComparison.Ordinal) ||
            !staleDeferred[1].StartsWith("FloatingReferenceBaseline 源码树模式验证失败", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("开启延后时参考基线过期未同时记入报告与记录。");
        }

        var currentRecord = new List<string>();
        var currentSnapshot = SealCore(
            CreateBehaviorFixture(facts, requested: true),
            facts,
            ReferenceBaselineDeferral.InMemory(currentRecord));
        if (currentSnapshot[DeferredField] is not string[] { Length: 0 } || currentRecord.Count != 0)
        {
            throw new InvalidOperationException("开启延后但基线未过期时不应有延后项。");
        }

        var otherFact = facts.Keys.First(field =>
            !string.Equals(field, ReferenceBaselinesCurrentFact, StringComparison.Ordinal));
        MustRejectDeferred(facts, report => report[otherFact] = false, "开启延后时契约仍须拒绝其他验证事实失败。");
        MustRejectDeferred(
            facts,
            report =>
            {
                report["MainReferenceBaselineValidated"] = false;
                report["MainReferenceBaselineManifestAvailable"] = false;
            },
            "开启延后时契约仍须拒绝缺少 manifest。");
        MustRejectDeferred(
            facts,
            report =>
            {
                report["MainReferenceBaselineValidated"] = false;
                report["MainReferenceBaselineCaptures"] = Array.Empty<object>();
            },
            "开启延后时契约仍须拒绝没有实拍。");

        var sourceSnapshot = SealCore(CreateBehaviorFixture(facts, requested: true), facts);
        var firstJson = JsonSerializer.Serialize(sourceSnapshot);
        var secondJson = JsonSerializer.Serialize(sourceSnapshot);
        if (!string.Equals(firstJson, secondJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("同一封存报告的两次 JSON 输出不一致。");
        }

        var packageSnapshot = SealCore(CreateBehaviorFixture(facts, requested: false), facts);
        if (packageSnapshot[ValidField] is not true ||
            packageSnapshot[ContractBehaviorField] is not true ||
            packageSnapshot[ModuleDepthField] is not true)
        {
            throw new InvalidOperationException("Seal 未追加 Valid 结论与两个既有契约结果字段。");
        }

        MustThrow<NotSupportedException>(
            () => ((IDictionary<string, object?>)packageSnapshot).Add("Unexpected", true),
            "Seal 返回的报告快照仍可变。");
    }

    private static void MustReject(
        IReadOnlyDictionary<string, string> facts,
        Action<Dictionary<string, object?>> mutate,
        bool requested,
        string message)
    {
        var report = CreateBehaviorFixture(facts, requested);
        mutate(report);
        MustThrow<InvalidDataException>(() => SealCore(report, facts), message);
    }

    private static void MustRejectDeferred(
        IReadOnlyDictionary<string, string> facts,
        Action<Dictionary<string, object?>> mutate,
        string message)
    {
        var record = new List<string>();
        var report = CreateBehaviorFixture(facts, requested: true);
        report[ReferenceBaselinesCurrentFact] = false;
        mutate(report);
        MustThrow<InvalidDataException>(
            () => SealCore(report, facts, ReferenceBaselineDeferral.InMemory(record)),
            message);
        if (record.Count != 0)
        {
            throw new InvalidOperationException($"{message}被拒绝的报告不应留下延后记录。");
        }
    }

    private static Dictionary<string, object?> CreateBehaviorFixture(
        IReadOnlyDictionary<string, string> facts,
        bool requested)
    {
        var report = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in facts.Keys)
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
            report.Add(
                $"{prefix}Gates",
                new Dictionary<string, bool>(StringComparer.Ordinal)
                {
                    ["ManifestAvailable"] = requested
                });
            report.Add($"{prefix}Diagnostics", Array.Empty<string>());
        }

        report.Add("MainStressCountdownTitleVisuals", requested);
        report.Add(
            "MainStressCountdownTitleMeasurements",
            requested ? new object[] { new() } : Array.Empty<object>());
        report.Add("MainNoteCardReminderFontSizeMeetsContract", requested);
        report.Add(
            "MainNoteCardReminderFontMeasurements",
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

    private static void MustRejectWithMessage(
        IReadOnlyDictionary<string, string> facts,
        Action<Dictionary<string, object?>> mutate,
        string expectedMessage,
        string failureMessage)
    {
        var report = CreateBehaviorFixture(facts, requested: true);
        mutate(report);
        try
        {
            SealCore(report, facts);
        }
        catch (InvalidDataException exception)
        {
            if (string.Equals(exception.Message, expectedMessage, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"{failureMessage} expected=[{expectedMessage}] actual=[{exception.Message}]",
                exception);
        }

        throw new InvalidOperationException(failureMessage);
    }

    private sealed record ManifestMode(
        bool Requested,
        bool ManifestAvailable);
}
