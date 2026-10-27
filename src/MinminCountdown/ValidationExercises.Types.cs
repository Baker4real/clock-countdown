using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed record PersistenceFailureExerciseResult(
    bool CountdownStatePreserved,
    bool PreferenceStatePreserved,
    bool CountdownDraftRetained,
    bool PreferenceDraftRetained,
    bool TagStatePreserved,
    bool TagDraftRetained,
    bool TagFilterRetained,
    bool TemporaryPathRemainsDirectory)
{
    public static PersistenceFailureExerciseResult NotRun { get; } =
        new(false, false, false, false, false, false, false, false);
}

internal sealed record TagInteractionExerciseResult(
    bool Executed,
    bool CreationAndAssociation,
    bool AnyAllFilter,
    bool EditorSummaryIsCompact,
    bool SearchCreateAvoidsDefaultSubmit,
    bool UntaggedAndCountdownStatus,
    bool NoteStatusSearchAndCompletion,
    bool NoteDateFilterControls,
    bool NoteDateFilterBehavior,
    bool FloatingNoteInheritsTags,
    bool FloatingNoteDateViaPicker,
    bool FloatingNotePopupOnDemand,
    bool FloatingNoteCanOverrideTags,
    bool NoteEditingPersistsTags,
    bool NoteEditingPersistsDate,
    bool RenameAndDeleteDetachOnly)
{
    public static TagInteractionExerciseResult NotRun { get; } =
        new(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
}

internal sealed record UiQualityGateInputs(
    bool MainReferenceDecorationScaling,
    bool MainReferenceDecorationLayerOrder,
    bool MainWorkspaceResourcesFollowTheme,
    int MainCountdownCardSampleCount,
    bool MainCountdownCardResourcesFollowTheme,
    int MainNoteCardSampleCount,
    bool MainNoteCardResourcesFollowTheme,
    bool FloatingContentSafeArea,
    bool FloatingTitleUsesSemiBoldWeight,
    bool FloatingNoteInputWraps,
    bool FloatingNoteInputScrolls);

internal sealed record FloatingPrimaryCountdownContrastGateInputs(
    IReadOnlyList<double> ContrastRatios,
    bool AccentSeparatedFromPlushGlow);

internal sealed record FloatingPrimaryCountdownContrastGateResults(
    bool TextContrast,
    bool AccentSeparatedFromPlushGlow);

internal sealed record AuditContrastGateInputs(
    IReadOnlyList<double> SourceLabelRatios,
    bool SourceUsesInkBrush,
    bool SourceEmphasisPreserved,
    IReadOnlyList<double> DialogRatios,
    IReadOnlyList<double> FocusRatios);

internal sealed record AuditContrastGateResults(
    bool SourceLabelContrast,
    bool SourceUsesInkBrush,
    bool SourceEmphasisPreserved,
    bool DialogContrast,
    bool FocusContrast)
{
    public bool AllPassed =>
        SourceLabelContrast &&
        SourceUsesInkBrush &&
        SourceEmphasisPreserved &&
        DialogContrast &&
        FocusContrast;
}

internal sealed record FloatingReferenceManifestGateInputs(
    bool ManifestStructureValid,
    bool ExpectedCaptureFilesPresent,
    bool CapturePathsUnique,
    bool NoOrphanPngFiles,
    bool CanvasDimensionsMatch,
    bool ScenarioThemePairsUnique,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool PngHashesMatch,
    bool AlphaMetricsMatch,
    bool ContentRoiMatches,
    bool PixelMetricsWithinThresholds);

internal sealed record FloatingReferenceManifestGateResults(
    bool ManifestStructureValid,
    bool ExpectedCaptureFilesPresent,
    bool CapturePathsUnique,
    bool NoOrphanPngFiles,
    bool CanvasDimensionsMatch,
    bool ScenarioThemePairsUnique,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool PngHashesMatch,
    bool AlphaMetricsMatch,
    bool ContentRoiMatches,
    bool PixelMetricsWithinThresholds)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(ManifestStructureValid)] = ManifestStructureValid,
            [nameof(ExpectedCaptureFilesPresent)] = ExpectedCaptureFilesPresent,
            [nameof(CapturePathsUnique)] = CapturePathsUnique,
            [nameof(NoOrphanPngFiles)] = NoOrphanPngFiles,
            [nameof(CanvasDimensionsMatch)] = CanvasDimensionsMatch,
            [nameof(ScenarioThemePairsUnique)] = ScenarioThemePairsUnique,
            [nameof(SourceHashesMatch)] = SourceHashesMatch,
            [nameof(ExecutableHashMatches)] = ExecutableHashMatches,
            [nameof(PngHashesMatch)] = PngHashesMatch,
            [nameof(AlphaMetricsMatch)] = AlphaMetricsMatch,
            [nameof(ContentRoiMatches)] = ContentRoiMatches,
            [nameof(PixelMetricsWithinThresholds)] = PixelMetricsWithinThresholds
        };
}

internal sealed record FloatingReferenceFreshnessGateInputs(
    bool ExpectedStateBindingsMatch,
    bool CaptureIdsUnique,
    bool StateFingerprintsMatch,
    bool StateFingerprintsUnique,
    bool PngHashesUnique,
    bool TopBottomPixelsDiffer,
    bool LongNoteTypographyMatches,
    bool LongNoteLayoutValid);

internal sealed record FloatingReferenceFreshnessGateResults(
    bool ExpectedStateBindingsMatch,
    bool CaptureIdsUnique,
    bool StateFingerprintsMatch,
    bool StateFingerprintsUnique,
    bool PngHashesUnique,
    bool TopBottomPixelsDiffer,
    bool LongNoteTypographyMatches,
    bool LongNoteLayoutValid)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(ExpectedStateBindingsMatch)] = ExpectedStateBindingsMatch,
            [nameof(CaptureIdsUnique)] = CaptureIdsUnique,
            [nameof(StateFingerprintsMatch)] = StateFingerprintsMatch,
            [nameof(StateFingerprintsUnique)] = StateFingerprintsUnique,
            [nameof(PngHashesUnique)] = PngHashesUnique,
            [nameof(TopBottomPixelsDiffer)] = TopBottomPixelsDiffer,
            [nameof(LongNoteTypographyMatches)] = LongNoteTypographyMatches,
            [nameof(LongNoteLayoutValid)] = LongNoteLayoutValid
        };
}

internal sealed record MainReferenceManifestGateInputs(
    bool ManifestStructureValid,
    bool ExpectedCaptureFilesPresent,
    bool NoOrphanPngFiles,
    bool CaptureIdsUnique,
    bool CapturePathsUnique,
    bool ScenarioBindingsUnique,
    bool CanvasDimensionsMatch,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool PngHashesMatch,
    bool StateFingerprintsMatch,
    bool AlphaMetricsMatch,
    bool ContentRoiMatches,
    bool PixelMetricsWithinThresholds,
    bool StateFingerprintsUnique,
    bool PngHashesUnique);

internal sealed record MainReferenceManifestGateResults(
    bool ManifestStructureValid,
    bool ExpectedCaptureFilesPresent,
    bool NoOrphanPngFiles,
    bool CaptureIdsUnique,
    bool CapturePathsUnique,
    bool ScenarioBindingsUnique,
    bool CanvasDimensionsMatch,
    bool SourceHashesMatch,
    bool ExecutableHashMatches,
    bool PngHashesMatch,
    bool StateFingerprintsMatch,
    bool AlphaMetricsMatch,
    bool ContentRoiMatches,
    bool PixelMetricsWithinThresholds,
    bool StateFingerprintsUnique,
    bool PngHashesUnique)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(ManifestStructureValid)] = ManifestStructureValid,
            [nameof(ExpectedCaptureFilesPresent)] = ExpectedCaptureFilesPresent,
            [nameof(NoOrphanPngFiles)] = NoOrphanPngFiles,
            [nameof(CaptureIdsUnique)] = CaptureIdsUnique,
            [nameof(CapturePathsUnique)] = CapturePathsUnique,
            [nameof(ScenarioBindingsUnique)] = ScenarioBindingsUnique,
            [nameof(CanvasDimensionsMatch)] = CanvasDimensionsMatch,
            [nameof(SourceHashesMatch)] = SourceHashesMatch,
            [nameof(ExecutableHashMatches)] = ExecutableHashMatches,
            [nameof(PngHashesMatch)] = PngHashesMatch,
            [nameof(StateFingerprintsMatch)] = StateFingerprintsMatch,
            [nameof(AlphaMetricsMatch)] = AlphaMetricsMatch,
            [nameof(ContentRoiMatches)] = ContentRoiMatches,
            [nameof(PixelMetricsWithinThresholds)] = PixelMetricsWithinThresholds,
            [nameof(StateFingerprintsUnique)] = StateFingerprintsUnique,
            [nameof(PngHashesUnique)] = PngHashesUnique
        };
}

internal sealed record MainStressCountdownTitleGateInputs(
    bool ExactTitleRendered,
    bool TextIsSingleLineAndUntrimmed,
    bool TextFits,
    bool FullyVisibleInViewport,
    bool SeparatedFromRemaining,
    bool SeparatedFromActions);

internal sealed record MainStressCountdownTitleGateResults(
    bool ExactTitleRendered,
    bool LayoutSafe)
{
    public bool AllPassed => ExactTitleRendered && LayoutSafe;
}

internal sealed record FloatingSecondaryRemainingGateInputs(
    IReadOnlyList<string> ExpectedTexts,
    IReadOnlyList<string> FormattedTexts,
    IReadOnlyList<string> RenderedTexts,
    bool HasExpectedSampleCount,
    bool ColumnsAre194Pixels,
    bool TextIsRightAligned,
    bool TextIsSingleLineAndUntrimmed,
    bool TextIsFullyVisible,
    bool UsesTabularNumerals,
    bool FontSizingMatches);

// #157：悬浮视口几何门禁输入（确定性视口右缘口径的隔离负向夹具与现役捕获共用）。
// null = 该表面不参与视口右缘比较（非便签工作区的卡/印记、隐藏的快速新增）。
internal sealed record FloatingViewportGeometryGateInputs(
    double ScrollerSlotRight,
    double DesignedScrollBarRail,
    bool ScrollBarVisible,
    double? MaximumCardRight,
    double? MaximumDateStampRight,
    double? NoteEntryRight);

internal sealed record FloatingSecondaryRemainingGateResults(
    bool UnitSpacingMatches,
    bool FixedColumnLayoutMatches);

internal sealed record MainCountdownRemainingGateInputs(
    IReadOnlyList<string> ExpectedTexts,
    IReadOnlyList<string> RenderedTexts,
    bool HasExpectedSampleCount,
    bool ColumnsAre300Pixels,
    bool ActualColumnsAre300Pixels,
    bool TextIsRightAligned,
    bool TextIsSingleLineAndUntrimmed,
    bool TextIsFullyVisible,
    bool UsesTabularNumerals,
    bool PrecisionSamplesAreSaved,
    bool FontSizingMatches);

internal sealed record MainCountdownRemainingGateResults(
    bool UnitSpacingMatches,
    bool FixedColumnLayoutMatches)
{
    public bool AllPassed => UnitSpacingMatches && FixedColumnLayoutMatches;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(UnitSpacingMatches)] = UnitSpacingMatches,
            [nameof(FixedColumnLayoutMatches)] = FixedColumnLayoutMatches
        };
}

// #151：倒计时编辑门禁输入与结果（隔离负向夹具用）。
internal sealed record CountdownEditingGateInputs(
    bool CountdownFieldErrorsAtSource,
    bool FocusFirstInvalidControl,
    bool CountdownFieldErrorsClearOnCorrection,
    bool SingleErrorRowNotClipped,
    bool PreferenceErrorsBelowFields,
    bool PreferenceErrorsClearOnCorrection,
    bool PrimarySecondaryRolesDistinct,
    bool InputSurfacesWhitePaper,
    bool SelectionHighlightDistinct,
    bool StatusBarSurfaceErrorContrast,
    bool ReminderRadioSelectedContrast,
    bool EditorTypographyTokenized);

internal sealed record CountdownEditingGateResults(
    bool FieldErrorsAtSourceWithFocus,
    bool FieldErrorsClear,
    bool SingleErrorRowNotClipped,
    bool PreferenceErrorsAtSource,
    bool PrimarySecondaryRolesDistinct,
    bool InputSurfacesWhitePaper,
    bool SelectionHighlightDistinct,
    bool StatusBarErrorContrast,
    bool ReminderRadioSelectedContrast,
    bool EditorTypographyTokenized)
{
    public bool AllPassed =>
        FieldErrorsAtSourceWithFocus &&
        FieldErrorsClear &&
        SingleErrorRowNotClipped &&
        PreferenceErrorsAtSource &&
        PrimarySecondaryRolesDistinct &&
        InputSurfacesWhitePaper &&
        SelectionHighlightDistinct &&
        StatusBarErrorContrast &&
        ReminderRadioSelectedContrast &&
        EditorTypographyTokenized;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(FieldErrorsAtSourceWithFocus)] = FieldErrorsAtSourceWithFocus,
            [nameof(FieldErrorsClear)] = FieldErrorsClear,
            [nameof(SingleErrorRowNotClipped)] = SingleErrorRowNotClipped,
            [nameof(PreferenceErrorsAtSource)] = PreferenceErrorsAtSource,
            [nameof(PrimarySecondaryRolesDistinct)] = PrimarySecondaryRolesDistinct,
            [nameof(InputSurfacesWhitePaper)] = InputSurfacesWhitePaper,
            [nameof(SelectionHighlightDistinct)] = SelectionHighlightDistinct,
            [nameof(StatusBarErrorContrast)] = StatusBarErrorContrast,
            [nameof(ReminderRadioSelectedContrast)] = ReminderRadioSelectedContrast,
            [nameof(EditorTypographyTokenized)] = EditorTypographyTokenized
        };
}

// #148：倒计时可读性门禁输入与结果（隔离负向夹具用）。
internal sealed record CountdownReadabilityGateInputs(
    bool BadgeUsesInkText,
    bool BadgeMeetsContrast,
    bool BadgeSizedAndCentered,
    bool ExpiredSecondaryUsesBodyColor,
    bool ExpiredPrimaryUsesNumberColor,
    bool BlocksVerticalRatioMatches,
    bool UnitFontSizeAtFloor,
    bool SegmentContrastGateOnly);

internal sealed record CountdownReadabilityGateResults(
    bool BadgeContractMatches,
    bool ExpiredTextMatches,
    bool BlocksGeometryMatches,
    bool SegmentContrastGateOnly)
{
    public bool AllPassed =>
        BadgeContractMatches && ExpiredTextMatches && BlocksGeometryMatches && SegmentContrastGateOnly;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(BadgeContractMatches)] = BadgeContractMatches,
            [nameof(ExpiredTextMatches)] = ExpiredTextMatches,
            [nameof(BlocksGeometryMatches)] = BlocksGeometryMatches,
            [nameof(SegmentContrastGateOnly)] = SegmentContrastGateOnly
        };
}

internal sealed record CountdownIncrementalRefreshGateResults(
    bool IdentityPreserved,
    bool FocusPreserved,
    bool NonZeroScrollPreserved,
    bool PointerStatePreserved,
    bool CollectionChangeRebuilt,
    bool FilterChangeRebuilt,
    bool PrecisionChangeRebuilt,
    bool ExpiryChangeRebuilt,
    bool ExpiryBackgroundFlipped,
    bool ExpiryRemainingTextFlipped)
{
    public bool ExpiryVisualsFlipTogether =>
        ExpiryBackgroundFlipped && ExpiryRemainingTextFlipped;

    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(IdentityPreserved), IdentityPreserved),
        (nameof(FocusPreserved), FocusPreserved),
        (nameof(NonZeroScrollPreserved), NonZeroScrollPreserved),
        (nameof(PointerStatePreserved), PointerStatePreserved),
        (nameof(CollectionChangeRebuilt), CollectionChangeRebuilt),
        (nameof(FilterChangeRebuilt), FilterChangeRebuilt),
        (nameof(PrecisionChangeRebuilt), PrecisionChangeRebuilt),
        (nameof(ExpiryChangeRebuilt), ExpiryChangeRebuilt),
        (nameof(ExpiryBackgroundFlipped), ExpiryBackgroundFlipped),
        (nameof(ExpiryRemainingTextFlipped), ExpiryRemainingTextFlipped)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record ConfirmationDialogGateInputs(
    bool ButtonsVisible,
    bool BodyReachable,
    bool CancelIsSafeDefault,
    bool ProductionMatrixComplete,
    bool BodyEndReachable,
    bool CenteredOnOwner,
    bool EnterIsSafe,
    bool CancelAndEscapeAreSafe);

internal sealed record ConfirmationDialogGateResults(
    bool ButtonsVisible,
    bool BodyReachable,
    bool CancelIsSafeDefault,
    bool ProductionMatrixComplete,
    bool BodyEndReachable,
    bool CenteredOnOwner,
    bool EnterIsSafe,
    bool CancelAndEscapeAreSafe)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(ButtonsVisible), ButtonsVisible),
        (nameof(BodyReachable), BodyReachable),
        (nameof(CancelIsSafeDefault), CancelIsSafeDefault),
        (nameof(ProductionMatrixComplete), ProductionMatrixComplete),
        (nameof(BodyEndReachable), BodyEndReachable),
        (nameof(CenteredOnOwner), CenteredOnOwner),
        (nameof(EnterIsSafe), EnterIsSafe),
        (nameof(CancelAndEscapeAreSafe), CancelAndEscapeAreSafe)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record SecondaryWindowTypographyGateInputs(
    bool RenderedScaleMatches,
    bool ContentRootsDeclareFontSize,
    bool FloatingTypographyUnchanged);

internal sealed record SecondaryWindowTypographyGateResults(
    bool RenderedScaleMatches,
    bool ContentRootsDeclareFontSize,
    bool FloatingTypographyUnchanged)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(RenderedScaleMatches), RenderedScaleMatches),
        (nameof(ContentRootsDeclareFontSize), ContentRootsDeclareFontSize),
        (nameof(FloatingTypographyUnchanged), FloatingTypographyUnchanged)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record ReminderCenterKeyboardGateInputs(
    bool TabTraversalWorks,
    bool CloseGuardWorks,
    bool ShowPreservesFocusAndAccessibleName,
    bool EscapeFocusesAcknowledge);

internal sealed record ReminderCenterKeyboardGateResults(
    bool TabTraversalWorks,
    bool CloseGuardWorks,
    bool ShowPreservesFocusAndAccessibleName,
    bool EscapeFocusesAcknowledge)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(TabTraversalWorks), TabTraversalWorks),
        (nameof(CloseGuardWorks), CloseGuardWorks),
        (nameof(ShowPreservesFocusAndAccessibleName), ShowPreservesFocusAndAccessibleName),
        (nameof(EscapeFocusesAcknowledge), EscapeFocusesAcknowledge)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record MainShortcutGateInputs(
    IReadOnlyList<string> ExpectedCommands,
    IReadOnlyDictionary<string, bool> ObservedCommands,
    bool TextInputPreserved,
    bool PopupGuarded,
    bool ModalOwnerGuarded);

internal sealed record MainShortcutGateResults(
    bool CommandsWork,
    bool TextInputPreserved,
    bool PopupGuarded,
    bool ModalOwnerGuarded)
{
    public bool AllPassed => CommandsWork && TextInputPreserved && PopupGuarded && ModalOwnerGuarded;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(CommandsWork), CommandsWork),
        (nameof(TextInputPreserved), TextInputPreserved),
        (nameof(PopupGuarded), PopupGuarded),
        (nameof(ModalOwnerGuarded), ModalOwnerGuarded)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record MainKeyboardNavigationGateInputs(
    bool InitialFocusSafe,
    bool CompleteTabSequence);

// #153：弹层 Tab 层内循环门禁输入/结果。
internal sealed record PopupFocusTabGateInputs(
    bool FilterCyclesInsideLayer,
    bool TagManagerCyclesInsideLayer,
    bool TagSelectorCyclesInsideLayer);

internal sealed record PopupFocusTabGateResults(
    bool FilterCyclesInsideLayer,
    bool TagManagerCyclesInsideLayer,
    bool TagSelectorCyclesInsideLayer)
{
    public bool AllCyclesInsideLayer =>
        FilterCyclesInsideLayer &&
        TagManagerCyclesInsideLayer &&
        TagSelectorCyclesInsideLayer;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(FilterCyclesInsideLayer), FilterCyclesInsideLayer),
        (nameof(TagManagerCyclesInsideLayer), TagManagerCyclesInsideLayer),
        (nameof(TagSelectorCyclesInsideLayer), TagSelectorCyclesInsideLayer)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record MainKeyboardNavigationGateResults(
    bool InitialFocusSafe,
    bool CompleteTabSequence)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(InitialFocusSafe), InitialFocusSafe),
        (nameof(CompleteTabSequence), CompleteTabSequence)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record MainStructuralAnnouncementGateInputs(
    bool WorkspaceSelectionWithoutRedundantStatus,
    bool FilterResultAnnouncement,
    bool EmptyStateAnnouncements);

internal sealed record MainStructuralAnnouncementGateResults(
    bool WorkspaceSelectionWithoutRedundantStatus,
    bool FilterResultAnnouncement,
    bool EmptyStateAnnouncements)
{
    public bool AllPassed => FailedChecks.Count == 0;

    public IReadOnlyList<string> FailedChecks => new[]
    {
        (nameof(WorkspaceSelectionWithoutRedundantStatus), WorkspaceSelectionWithoutRedundantStatus),
        (nameof(FilterResultAnnouncement), FilterResultAnnouncement),
        (nameof(EmptyStateAnnouncements), EmptyStateAnnouncements)
    }.Where(pair => !pair.Item2).Select(pair => pair.Item1).ToArray();
}

internal sealed record FloatingScrollBarGateInputs(
    bool LocalStylePresent,
    bool MainWindowStyleUnaffected,
    double HitTargetWidth,
    double NormalThumbWidth,
    double HoverThumbWidth,
    double DragThumbWidth,
    double NormalThumbOpacity,
    double HoverThumbOpacity,
    double DragThumbOpacity,
    bool ThemeTokensMatch,
    bool NormalDoesNotObscureContent,
    bool HoverDoesNotObscureContent,
    bool DragDoesNotObscureContent,
    bool ContentSafeAreaUnchanged,
    bool WheelChangesOffset,
    bool DragChangesOffset);

internal sealed record FloatingScrollBarGateResults(
    bool LocalStyleScoped,
    bool HitTargetUsable,
    bool VisualWeightProgression,
    bool UsesWarmThemeTokens,
    bool ContentRemainsSafe,
    bool InputPathsWork)
{
    public bool AllPassed =>
        LocalStyleScoped &&
        HitTargetUsable &&
        VisualWeightProgression &&
        UsesWarmThemeTokens &&
        ContentRemainsSafe &&
        InputPathsWork;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(LocalStyleScoped)] = LocalStyleScoped,
            [nameof(HitTargetUsable)] = HitTargetUsable,
            [nameof(VisualWeightProgression)] = VisualWeightProgression,
            [nameof(UsesWarmThemeTokens)] = UsesWarmThemeTokens,
            [nameof(ContentRemainsSafe)] = ContentRemainsSafe,
            [nameof(InputPathsWork)] = InputPathsWork
        };
}

internal sealed record UiQualityGateResults(
    bool MainReferenceDecorationScaling,
    bool MainReferenceDecorationLayerOrder,
    bool MainWorkspaceResourcesFollowTheme,
    bool MainCountdownCardResourcesFollowTheme,
    bool MainNoteCardResourcesFollowTheme,
    bool FloatingContentSafeArea,
    bool FloatingTitleUsesSemiBoldWeight,
    bool FloatingNoteInputWraps,
    bool FloatingNoteInputScrolls)
{
    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(MainReferenceDecorationScaling)] = MainReferenceDecorationScaling,
            [nameof(MainReferenceDecorationLayerOrder)] = MainReferenceDecorationLayerOrder,
            [nameof(MainWorkspaceResourcesFollowTheme)] = MainWorkspaceResourcesFollowTheme,
            [nameof(MainCountdownCardResourcesFollowTheme)] = MainCountdownCardResourcesFollowTheme,
            [nameof(MainNoteCardResourcesFollowTheme)] = MainNoteCardResourcesFollowTheme,
            [nameof(FloatingContentSafeArea)] = FloatingContentSafeArea,
            [nameof(FloatingTitleUsesSemiBoldWeight)] = FloatingTitleUsesSemiBoldWeight,
            [nameof(FloatingNoteInputWraps)] = FloatingNoteInputWraps,
            [nameof(FloatingNoteInputScrolls)] = FloatingNoteInputScrolls
        };
}

internal sealed record CountdownEmptyStateGateInputs(
    bool EmptyStatesDistinguished,
    bool PointerResetRestoresResults,
    bool KeyboardResetRestoresResults,
    bool DayThemeTextContrast);

internal sealed record CountdownEmptyStateGateResults(
    bool EmptyStatesDistinguished,
    bool PointerResetRestoresResults,
    bool KeyboardResetRestoresResults,
    bool DayThemeTextContrast)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(EmptyStatesDistinguished)] = EmptyStatesDistinguished,
            [nameof(PointerResetRestoresResults)] = PointerResetRestoresResults,
            [nameof(KeyboardResetRestoresResults)] = KeyboardResetRestoresResults,
            [nameof(DayThemeTextContrast)] = DayThemeTextContrast
        };
}

// #240：剩余时间分格「跳过 / 就地 / 重建」探针；两窗各自一组事实，数值字段只作诊断。
internal sealed record RemainingBlocksOnDemandProbe(
    bool MainSkipsUnchangedTicks,
    bool MainSecondsUpdateInPlace,
    bool MainInPlaceGeometryMatchesFreshFit,
    bool MainDayDigitsChangeRebuilds,
    bool MainDpiChangeRebuilds,
    bool FloatingSkipsUnchangedTicks,
    bool FloatingSecondsUpdateInPlace,
    bool FloatingInPlaceGeometryMatchesFreshFit,
    bool FloatingDayDigitsChangeRebuilds,
    bool FloatingDayBlockOmissionRebuilds,
    bool FloatingDpiChangeRebuilds,
    IReadOnlyList<long> MainBuildDeltas,
    IReadOnlyList<long> FloatingBuildDeltas,
    double DpiScaleBefore,
    double DpiScaleAfter);

// #240：悬浮窗便签「下次提醒」缓存探针；ComputationDeltas 按步骤记录实际计算次数。
internal sealed record FloatingNextReminderCacheProbe(
    bool ComputedOncePerNoteWithinMinute,
    bool RecomputedWhenCachedReminderIsReached,
    bool RecomputedOnMinuteRollover,
    bool RecomputedOnWorkspaceStateChange,
    bool RecomputedOnCalendarStateChange,
    bool DisplayedTextAlwaysCorrect,
    IReadOnlyList<long> ComputationDeltas);

internal sealed record NoteVisualTokenGateInputs(
    bool CompactInputPaddingAligned,
    bool BadgeTokensFollowState,
    bool EmptyStateTextContrast);

internal sealed record NoteVisualTokenGateResults(
    bool CompactInputPaddingAligned,
    bool BadgeTokensFollowState,
    bool EmptyStateTextContrast)
{
    public bool AllPassed => AsDictionary().Values.All(value => value);

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(CompactInputPaddingAligned)] = CompactInputPaddingAligned,
            [nameof(BadgeTokensFollowState)] = BadgeTokensFollowState,
            [nameof(EmptyStateTextContrast)] = EmptyStateTextContrast
        };
}

internal sealed record MainNoteCompletionGateInputs(
    bool DedicatedStyleApplied,
    bool MainBoxAtLeast24Pixels,
    bool MainTickScaled,
    bool WarmTokensReused,
    bool RoundedCornersPreserved,
    bool CheckAndUncheckWork,
    bool PersistenceFailureRollsBack,
    bool AccessibleNamesUpdate,
    bool TagSelectorSizeUnchanged,
    bool FloatingSizeUnchanged);

internal sealed record MainNoteCompletionGateResults(
    bool MainTargetSizeAndBehavior,
    bool OtherUserSizesUnchanged)
{
    public bool AllPassed => MainTargetSizeAndBehavior && OtherUserSizesUnchanged;

    public IReadOnlyDictionary<string, bool> AsDictionary() =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [nameof(MainTargetSizeAndBehavior)] = MainTargetSizeAndBehavior,
            [nameof(OtherUserSizesUnchanged)] = OtherUserSizesUnchanged
        };
}

internal sealed record ReminderCenterAcknowledgeAllExerciseResult(
    bool WritesAllDispositions,
    bool MatchesSingleSemantics,
    bool FailureRetainsAllAtomically,
    bool BackoffRetryCompletesBatch)
{
    public static ReminderCenterAcknowledgeAllExerciseResult Failed { get; } =
        new(false, false, false, false);
}

internal sealed class ValidationGuidSource(params Guid[] values) : IGuidSource
{
    private int _index;

    public Guid NewGuid()
    {
        if (_index >= values.Length)
        {
            throw new InvalidOperationException("验证 Guid 序列已耗尽。");
        }

        return values[_index++];
    }
}
