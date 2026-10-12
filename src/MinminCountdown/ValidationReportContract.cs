using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    private const int MaximumDiagnosticCount = 12;
    private const int MaximumDiagnosticLength = 240;
    private static readonly Regex DiagnosticPathPattern = new(
        @"(?i)(?:[a-z]:[\\/]|\\\\)[^\s,;|]+",
        RegexOptions.CultureInvariant);
    private static readonly Regex DiagnosticWhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant);

    private static readonly string[] RequiredTrueBooleanFields =
    [
        "Valid",
        "NoteBatchKeyboardCompletion",
        "NoteBatchDeleteUndo",
        "NoteSelectionAccessibility",
        "DesktopWorkspaceInterfaceBehavior",
        "ReminderEditingInterfaceBehavior",
        "CalendarReminderBehavior",
        "StartupAcceptanceProtocolInterfaceBehavior",
        "HolidayCalendarInterfaceBehavior",
        "MainReferenceDecorationsShareScaleAtMinimumAndWideSizes",
        "MainReferenceDecorationLayerOrderMatches",
        "MainDecorationOnDemandDecodeBehavior",
        "MainWorkspaceResourcesFollowDayTheme",
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
        "FloatingCountdownSelectionSurvivesRefresh",
        "FloatingNoteSearchScenariosPass",
        "FloatingBottomCommandScenariosPass",
        "FloatingQuickAddToggleScenariosPass",
        "FloatingNoteOverflowMenuScenariosPass",
        "FloatingDateStampPastelRotationMatches",
        "FloatingNotesHandwrittenDecorationPass",
        "FloatingSettingsButtonOpensManager",
        "FloatingEmptyStateTextIsolationBehavior",
        "FloatingIncomePrivacyBehavior",
        "NoteSearchSyncsFromSharedFilter",
        "NoteDateUsesSpacedChineseFormat",
        "NoteCardBodyLineHeightSeparatesSummary",
        "NoteRowTitleFallsToBlockTitleLevel",
        "MainNoteRowActionsKeyboardWorks",
        "NoteCardSpacingConsumesTokens",
        "TagChipSingleLineEllipsisWithFullText",
        "EmptyStateTextAllowsWrap",
        "NoteDateFilterHasVisibleLabel",
        "PopupTabNavigationCyclesInsideLayer",
        "PopupTabGateIsolationBehavior",
        "FilterFunnelIconSurvivesAliasedCapture",
        "NotesSearchDuplicateMagnifierRemoved",
        "MainWindowChromeButtonsUseVectorPaths",
        "SegmentActivePillsUseFullRoundedCorners",
        "CountdownHandwrittenDecoOutOfFormMeasurement",
        "FloatingShellLargeShadowRemoved",
        "MainWindowSquareChromeNoTransparency",
        "MainWindowFitsInjectedWorkAreas",
        "FloatingWindowStaysOnSecondaryMonitor",
        "OwnerDialogsFitSecondaryMonitor",
        "ReminderUsesSourceMonitor",
        "OpenWindowsFollowMonitorChanges",
        "MixedDpiUsesPhysicalMonitorGeometry",
        "ProductManifestDeclaresPerMonitorV2",
        "RuntimeDpiChangeUpdatesWindowLayout",
        "MainWindowIdentityZoneDragRegion",
        "MainWindowHiddenTickSkipsRenderAndShowRefreshes",
        "MainWindowNoteMinutePreservesFocusAndScroll",
        "CountdownListFirstBehavior",
        "MainTopmostStateSynchronization",
        "MainRedundancyCleanup",
        "MainNoteEditorSeparation",
        "MainTextShortcutBehavior",
        "SingleLineEnterSubmission",
        "FloatingNoteKeyboardSubmission",
        "ListDeleteKeyboardBehavior",
        "MainNoteRowsSimplified",
        "NoteGroupingSelectorAbsent",
        "CountdownPreferencesPopupBehavior",
        "CountdownEditorExpansionBehavior",
        "CountdownRowDeadlineAndSecondaryDeleteBehavior",
        "TabsRenderAboveDecorationBand",
        "FloatingQuickAddDayResourcesFollowLocalTheme",
        "FloatingQuickAddIndependentFromApplicationTheme",
        "FloatingQuickAddThemeResourceIsolationBehavior",
        "FloatingQuickAddDayExpandedScrolledCaptureWritten",
        "FloatingPrimaryCountdownTextContrast",
        "FloatingPrimaryCountdownAccentSeparatedFromPlushGlow",
        "FloatingPrimaryCountdownContrastGateIsolationBehavior",
        "FloatingSecondaryRemainingUnitSpacingMatches",
        "FloatingSecondaryRemainingLayoutMatches",
        "FloatingSecondaryRemainingGateIsolationBehavior",
        "FloatingViewportGeometryGateIsolationBehavior",
        "FloatingScrollBarLocalStyleScoped",
        "FloatingScrollBarHitTargetUsable",
        "FloatingScrollBarVisualWeightProgression",
        "FloatingScrollBarUsesWarmThemeTokens",
        "FloatingScrollBarContentRemainsSafe",
        "FloatingScrollBarInputPathsWork",
        "FloatingScrollBarGateIsolationBehavior",
        "FloatingScrollBarStateCapturesReady",
        "UiQualityGateIsolationBehavior",
        "ComboBoxArrowIsNotTabStop",
        "DatePickerOpenCalendarAccessibleNames",
        "DatePickerTextOpensCalendar",
        "CountdownCardActionsIncludeRecordName",
        "SettingsRecoveryFailureExitIsDefault",
        "PopupTriggerSecondClickCloses",
        "PopupEscapeFromTriggerClosesAndKeepsFocus",
        "RuleEditorInlineDeleteUsesDangerButton",
        "SettingsRecoverySecondaryUsesCanvas",
        "ReminderCenterTwoRowActionsLayout",
        "ReminderCenterEntryVisualIdentity",
        "ReminderCenterContentHierarchy",
        "ReminderCenterRelativeTimeUpdatesInPlace",
        "ReminderCenterAcknowledgeAllWritesAllDispositions",
        "ReminderCenterAcknowledgeAllMatchesSingleSemantics",
        "ReminderCenterAcknowledgeAllFailureRetainsAllAtomically",
        "ReminderCenterAcknowledgeAllBackoffRetryCompletesBatch",
        "ReminderCenterEscapeFocusesAcknowledge",
        "ReminderCenterTypographyWeightWhitelist",
        "ReminderCenterRepresentativeCaptureRendered",
        "ReminderCenterShowPreservesFocusAndAccessibleName",
        "ReminderCenterArrivalKeepsForegroundAndFlashesTaskbar",
        "ReminderCenterMinimizeToTaskbarPreservesEntries",
        "MainCountdownFieldErrorsAtSource",
        "MainCountdownFieldErrorsClearOnCorrection",
        "MainCountdownSingleErrorRowUnclipped",
        "MainPreferenceErrorsBelowFields",
        "MainPrimarySecondaryButtonRolesDistinct",
        "MainInputSurfaceWhitePaper",
        "MainIdleTextInputBoundaryContrast",
        "IdleControlBoundariesVisible",
        "NoteCompletionUndoFeedback",
        "NoteCompletionExitTransition",
        "SystemContrastUpdatesOpenWindows",
        "SystemContrastAccessibleStates",
        "SystemContrastColorChangesAndCustomSurfaces",
        "FloatingThumbBoundaryContrast",
        "SelectedControlSignalsVisible",
        "DangerSurfacesMeetContrast",
        "FocusOuterNeighborsMeetContrast",
        "MainTextSelectionHighlightDistinct",
        "MainStatusBarSurfaceErrorContrast",
        "MainReminderRadioSelectedContrast",
        "MainEditorTypographyTokens",
        "MainCountdownEditingGateIsolationBehavior",
        "MainFocusRingConstantThicknessNoSqueeze",
        "MainPrimaryAndPinSingleFocusStroke",
        "MainNotesTabHoverDiffersFromSelected",
        "MainLocalControlsPressAndDisabled",
        "MainReminderHelpFocusShowsHelp",
        "MainCompactHitTargetsMeetScaledFloor",
        "MainFilterChipsAndCalendarNavCompact",
        "MainHoverStateContrastIsPerceivable",
        "MainPressedStateGeometryStable",
        "MainMouseClickDoesNotLeaveFocusRing",
        "MainKeyboardFocusRingShowsOnTab",
        "MainSelectedTabWeightDominates",
        "MainSegmentIndicatorGeometryMatches",
        "MainSegmentIndicatorReducedMotionIsImmediate",
        "MainSegmentIndicatorRapidSwitchStaysAligned",
        "MotionClientAreaAnimationSwitchReadable",
        "MotionTokensMatch",
        "KeyboardFocusRingTabGapAvoidsSelectedFill",
        "KeyboardFocusRingFloatingCapsuleMeetsContrast",
        "KeyboardFocusRingFloatingCapsuleUsesWarmGold",
        "PopupAnimationIsInstant",
        "ActionForegroundsUseMainActionText",
        "DisabledSelectionControlsUseButtonOpacity",
        "FloatingDateStampTypographyMatches",
        "FloatingTypographyWeightWhitelist",
        "FloatingDateStampDensityMatches",
        "NoteCancelFitsEditorPanel",
        "PopupRightInsetIsEightFromCanvas",
        "FilterPopupScrollsLongTagList",
        "MainCheckboxHitCoversSlot",
        "FloatingSearchSlotRightIs396",
        "FloatingSearchHintMatchesInputHeight",
        "FloatingQuickAddAlignsWithNoteCards",
        "FloatingCheckboxHitCoversSlot",
        "CrossInterfaceAiReferencePriority",
        "CrossInterfacePageSpecificComposition",
        "CrossInterfaceSharedWarmPinkTokens",
        "CrossInterfaceStaticDayThemeInfo",
        "CrossInterfacePlushDecorationLayering",
        "CrossInterfaceFilterSharing",
        "CrossInterfaceKeyboardOrder",
        "CrossInterfaceHitTesting",
        "CrossInterfaceDeterministicFixtureIsolation",
        "CrossInterfaceRealSettingsConserved",
        "CrossInterfaceIntegration"
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

        var failedBooleanFields = new List<string>();
        foreach (var field in RequiredTrueBooleanFields)
        {
            if (!report.TryGetValue(field, out var value) || value is not bool typedValue)
            {
                throw new InvalidDataException(
                    $"validate-only report 缺少 bool 字段或字段类型错误：{field}");
            }

            if (!typedValue)
            {
                failedBooleanFields.Add(field);
            }
        }

        if (failedBooleanFields.Count > 0)
        {
            throw new InvalidDataException(
                $"validate-only report 的行为字段未通过：{string.Join(", ", failedBooleanFields)}");
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
                    BuildManifestFailureMessage(
                        root,
                        prefix,
                        manifestAvailable,
                        validated,
                        captureCount));
            }
        }
        else if (manifestAvailable || validated || captureCount != 0)
        {
            throw new InvalidDataException(
                $"{prefix} 发布包模式不能声明 manifest/验证成功或伪造 capture。");
        }

        return new ManifestMode(requested, manifestAvailable, validated);
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

    private static void RunBehaviorExercise()
    {
        MustReject(report => report["FloatingIncomePrivacyBehavior"] = false, false,
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
            MustReject(report => report[field] = false, false,
                $"契约未拒绝主窗口行为失败：{field}。");
        }
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
        MustRejectWithMessage(
            report => report["MainReferenceBaselineManifestAvailable"] = false,
            "MainReferenceBaseline 源码树模式验证失败：" +
            "manifestAvailable=false, validated=true, captureCount=1, " +
            "falseGates=[<none>], diagnostics=[<none>]。",
            "manifest unavailable 异常未报告精确状态。");
        MustRejectWithMessage(
            report => report["MainReferenceBaselineValidated"] = false,
            "MainReferenceBaseline 源码树模式验证失败：" +
            "manifestAvailable=true, validated=false, captureCount=1, " +
            "falseGates=[<none>], diagnostics=[<none>]。",
            "validated false 异常未报告精确状态。");
        MustRejectWithMessage(
            report => report["MainReferenceBaselineCaptures"] = Array.Empty<object>(),
            "MainReferenceBaseline 源码树模式验证失败：" +
            "manifestAvailable=true, validated=true, captureCount=0, " +
            "falseGates=[<none>], diagnostics=[<none>]。",
            "capture empty 异常未报告精确状态。");
        MustRejectWithMessage(
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
        MustReject(
            report => report["MainNoteCardReminderFontMeasurements"] = Array.Empty<object>(),
            true,
            "契约未拒绝源码树模式缺少便签卡提醒摘要测量。");
        MustReject(
            report => report["MainNoteCardReminderFontSizeMeetsContract"] = false,
            true,
            "契约未拒绝源码树模式便签卡提醒摘要未达 12 DIP。");
        MustReject(
            report => report["MainNoteCardReminderFontMeasurements"] = new object[] { new() },
            false,
            "契约未拒绝发布包伪造便签卡提醒摘要测量。");

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
        Action<Dictionary<string, object?>> mutate,
        string expectedMessage,
        string failureMessage)
    {
        var report = CreateBehaviorFixture(requested: true);
        mutate(report);
        try
        {
            SealCore(report);
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
        bool ManifestAvailable,
        bool Validated);
}
