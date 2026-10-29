using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    private sealed record TextFit(bool Fits, double Available, double Required);

    private sealed record MainKeyboardBehaviorResult(
        string InitialFocusedAutomationId,
        bool InitialFocusSafe,
        IReadOnlyList<string> TabSequence,
        MainWorkspaceTabPathResult CountdownTabPath,
        MainWorkspaceTabPathResult NotesTabPath,
        bool CommandsWork,
        IReadOnlyDictionary<string, bool> CommandResults,
        IReadOnlyDictionary<string, MainShortcutCommandDiagnostic> CommandDiagnostics,
        bool TextInputPreserved,
        MainShortcutInputGuardResult InputGuards,
        bool PopupGuarded,
        bool ModalOwnerGuarded);

    private sealed record MainShortcutCommandDiagnostic(
        bool Handled,
        bool EffectObserved,
        string StatusText);

    private sealed record MainShortcutInputGuardResult(
        bool TextBox,
        bool DatePicker,
        bool ComboBox);

    private sealed record MainWorkspaceTabPathResult(
        IReadOnlyList<string> ExpectedSequence,
        IReadOnlyList<string> ForwardSequence,
        IReadOnlyList<string> BackwardSequence,
        bool ForwardMatches,
        bool BackwardSymmetric,
        bool FocusVisualsVisible,
        IReadOnlyList<string> ForwardFocusedTargets,
        IReadOnlyList<string> BackwardFocusedTargets)
    {
        public bool AllPassed => ForwardMatches && BackwardSymmetric && FocusVisualsVisible;
    }

    private sealed record MainWorkspaceTabPathEvaluation(
        bool ForwardMatches,
        bool BackwardSymmetric,
        bool FocusVisualsVisible)
    {
        public bool AllPassed => ForwardMatches && BackwardSymmetric && FocusVisualsVisible;
    }

    private sealed record MainTabTraversalResult(
        IReadOnlyList<string> Sequence,
        IReadOnlyList<string> FocusedTargets,
        bool FocusVisualsVisible);

    private sealed record MainTabTarget(string Id, FrameworkElement Element);

    private sealed record MainStructuralAnnouncementsResult(
        bool CountdownWorkspaceSelectionIsAccessible,
        bool NotesWorkspaceSelectionIsAccessible,
        bool FilterResultAnnounced,
        bool CountdownFirstUseEmptyAnnounced,
        bool NotesFirstUseEmptyAnnounced,
        bool CountdownFilterEmptyAnnounced,
        bool NotesFilterEmptyAnnounced,
        MainEmptyStateGuidanceResult EmptyStateGuidance);

    private sealed record MainEmptyStateGuidanceResult(
        bool CountdownElementsVisible,
        bool NotesElementsVisible,
        bool AddActionsFocusEditors,
        bool DecorationsAreNonInteractive,
        bool PopulatedListsRestoreEmptyState);

    private sealed record ConfirmationDialogScenario(
        string ContentKind,
        string Title,
        string Subject,
        string Message);

    private sealed record ConfirmationDialogMeasurement(
        string StateId,
        string ContentKind,
        string Theme,
        int SubjectLength,
        int MessageLength,
        bool ProductionMessageExact,
        bool Themed,
        bool ButtonsVisible,
        bool BodyReachable,
        bool BodyEndReached,
        bool OwnerMatches,
        string StartupLocation,
        bool CenteredOnOwner,
        bool CancelIsSafeDefault,
        bool EnterDidNotConfirm,
        bool EscapeDidNotConfirm,
        bool CancelDidNotConfirm);

    private sealed record ConfirmationDialogValidation(
        bool Themed,
        IReadOnlyList<ConfirmationDialogMeasurement> Measurements,
        ConfirmationDialogGateResults Gates);

    private enum ConfirmationDialogAction
    {
        Enter,
        Escape,
        Cancel
    }

    private static class NativeMethods
    {
        // #134 PerMonitorV2 动态轴：WM_DPICHANGED 参数与 PMv2 awareness 上下文判定。
        public const int WmDpiChanged = 0x02E0;
        public static readonly IntPtr DpiAwarenessContextPerMonitorV2 = new(-4);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll")]
        public static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AreDpiAwarenessContextsEqual(IntPtr left, IntPtr right);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    private sealed record SecondaryWindowTypographyMeasurement(
        string Window,
        string ContentRootAutomationId,
        double TitleFontSize,
        double? SectionFontSize,
        double BodyFontSize,
        double? MinorFontSize,
        double ContentRootFontSize,
        bool ContentRootDeclaresFontSize)
    {
        public bool RenderedScaleMatches =>
            Math.Abs(TitleFontSize - 26) <= 0.1 &&
            (SectionFontSize is null || Math.Abs(SectionFontSize.Value - 20) <= 0.1) &&
            Math.Abs(BodyFontSize - 16) <= 0.1 &&
            (MinorFontSize is null || Math.Abs(MinorFontSize.Value - 13) <= 0.1) &&
            Math.Abs(ContentRootFontSize - 16) <= 0.1;
    }

    private sealed record SecondaryWindowTypographyResult(
        IReadOnlyList<SecondaryWindowTypographyMeasurement> Windows,
        SecondaryWindowTypographyGateResults Gates,
        double FloatingBodyFontSize,
        double FloatingReminderFontSize)
    {
        public bool RenderedScaleMatches => Gates.RenderedScaleMatches;

        public bool ContentRootsDeclareFontSize => Gates.ContentRootsDeclareFontSize;

        public bool FloatingTypographyUnchanged => Gates.FloatingTypographyUnchanged;
    }

    private sealed record FloatingTrayInteractionExerciseResult(
        bool ProductionControllerActive,
        bool MinimizeHidesWindowAndShowsIcon,
        bool RestoreShowsWindowAndHidesIcon,
        bool ExitRequestsApplicationExit,
        bool FailedRestoreKeepsTrayStateAndAllowsRetry);

    private sealed record DatePickerProductionMeasurement(
        string Name,
        bool Attached,
        bool TextBoxPresent,
        bool IsReadOnly,
        bool TextBoxIsNotFocusable,
        DatePickerPointerExerciseResult Pointer,
        bool ButtonOpensCalendar,
        bool EscapeClosesAndReturnsFocus,
        string AutomationName,
        string OpenCalendarButtonName,
        TextFit TextFit,
        bool SelectedDateTextMatchesContract);

    private sealed record ProductionDatePickerValidation(
        IReadOnlyList<DatePickerProductionMeasurement> Attached,
        DatePickerProductionMeasurement ReminderCenter,
        DatePickerProductionMeasurement UnattachedNegative,
        int ReminderHourOptionCount,
        int ReminderMinuteOptionCount,
        int ReminderCenterComboBoxCount,
        int ReminderCenterComboBoxThemedCount)
    {
        public int ProductionInputCount => Attached.Count + 1;

        public bool AllProductionInputsReadOnly =>
            ProductionInputCount == 6 &&
            Attached.All(input =>
                input.TextBoxPresent && input.IsReadOnly && input.TextBoxIsNotFocusable) &&
            ReminderCenter is
            {
                Attached: true,
                TextBoxPresent: true,
                IsReadOnly: true,
                TextBoxIsNotFocusable: true,
                ButtonOpensCalendar: true
            } &&
            !string.IsNullOrWhiteSpace(ReminderCenter.AutomationName);

        public bool ReminderCenterComboBoxesUseThemedDropDowns =>
            ReminderCenterComboBoxCount == 3 &&
            ReminderCenterComboBoxThemedCount == ReminderCenterComboBoxCount;

        public bool AllAttached =>
            Attached.Count == 5 && Attached.All(input => input.Attached);

        public bool AttachedTextPointerWorks =>
            AllAttached &&
            Attached.All(input =>
                input.Pointer.TextBoxPresent &&
                input.Pointer.PressInjected &&
                input.Pointer.ReleaseInjected &&
                input.Pointer.ReleaseHandled &&
                input.Pointer.ReleaseOpened);

        public bool AttachedTextDefersUntilRelease =>
            AllAttached && Attached.All(input => input.Pointer.PressDidNotOpen);

        public bool AttachedTextOpensThemedPopups =>
            AllAttached && Attached.All(input => input.Pointer.ThemedPopupOpened);

        public bool AttachedKeyboardLifecycleWorks =>
            AllAttached && Attached.All(input => input.EscapeClosesAndReturnsFocus);

        public bool AllOpenCalendarButtonsNamed =>
            AllAttached &&
            Attached.All(input =>
                string.Equals(input.OpenCalendarButtonName, "打开日历", StringComparison.Ordinal)) &&
            string.Equals(ReminderCenter.OpenCalendarButtonName, "打开日历", StringComparison.Ordinal);

        public bool UnattachedNegativeTextDoesNotOpen =>
            UnattachedNegative is
            {
                Attached: false,
                TextBoxPresent: true,
                IsReadOnly: true,
                ButtonOpensCalendar: true
            } &&
            UnattachedNegative.Pointer.PressInjected &&
            UnattachedNegative.Pointer.ReleaseInjected &&
            UnattachedNegative.Pointer.PressDidNotOpen &&
            !UnattachedNegative.Pointer.ReleaseOpened &&
            !UnattachedNegative.Pointer.ReleaseHandled;
    }

    private sealed record ReminderKeyboardExerciseResult(
        bool TabTraversalWorks,
        bool CloseGuardWorks,
        bool ShowPreservesFocusAndAccessibleName,
        bool EscapeFocusesAcknowledge);

    private sealed record ReminderDeliveryExerciseResult(
        bool RequiresContentRenderedConfirmation,
        bool PersistenceFailureStaysVisible,
        bool PersistenceRetryBackoffWorks,
        bool SingleFlightWorks,
        bool IndependentOccurrencesWork,
        bool PersistenceFailureCloseGuardWorks,
        bool DiagnosticsExcludePrivatePayload)
    {
        public static ReminderDeliveryExerciseResult Failed { get; } =
            new(false, false, false, false, false, false, false);
    }

    private sealed record ReminderCenterExerciseResult(
        bool SingleWindow,
        bool SameTickBatch,
        bool StableOrdering,
        bool EntriesRendered,
        bool AcknowledgementDistinctFromDelivery,
        bool AcknowledgementFailureRetainsEntry,
        bool AcknowledgementFailureIsAtomic,
        bool SingleItemDisposition,
        bool OpenSourcePreservesState,
        bool OpenSourceFindsExactNote,
        bool OpenSourceRestoresMinimizedWindow,
        bool ClosesWhenEmpty,
        bool Reopens,
        bool ScrollsLongContent,
        bool FollowsDayTheme,
        bool HasOnlySafeActions,
        bool DisplaysSourceContentAndTime,
        bool Accessible)
    {
        public static ReminderCenterExerciseResult Failed { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false);
    }

    private sealed record ReminderCenterCaptureValidation(
        bool Rendered,
        int Width,
        int Height,
        string Sha256,
        int PageWhitePixelCount,
        int WarmCardPixelCount,
        int PendingBadgePixelCount,
        int MainActionPixelCount,
        int SourceIconPixelCount,
        string EvidencePath)
    {
        public static ReminderCenterCaptureValidation Failed { get; } =
            new(false, 0, 0, string.Empty, 0, 0, 0, 0, 0, string.Empty);
    }

    private sealed record ReminderCenterSourceLifecycleExerciseResult(
        bool PersistenceFailureRetainsCurrentSource,
        bool RemovesDeletedSource,
        bool CancelsDeletedSourceAcknowledgementRetry,
        bool RemovesDisabledSource,
        bool RemovesCompletedSource,
        bool ReopensAfterSourceRemoval)
    {
        public static ReminderCenterSourceLifecycleExerciseResult Failed { get; } =
            new(false, false, false, false, false, false);
    }

    private sealed record RepeatingReminderDeliveryExerciseResult(
        bool RenderedLatchWorks,
        bool SingleUnresolvedOccurrenceWorks,
        bool DueBeforeShow,
        bool WindowOpened,
        bool ContentRendered,
        bool NoDuplicate,
        bool AcknowledgementMonotonicAcrossOccurrences)
    {
        public static RepeatingReminderDeliveryExerciseResult Failed { get; } =
            new(false, false, false, false, false, false, false);
    }

    private sealed record AlphaMetrics(bool IsFullyOpaque, int NonOpaquePixels, byte MinimumAlpha);

    private sealed record NoteDateSection(
        string Key,
        FrameworkElement Root,
        Button Toggle,
        StackPanel Rows);

    private sealed record NoteSectionExerciseResult(
        bool DateSectionsCanCollapse,
        bool DateSectionsAreIndependent,
        bool DateSectionStateSurvivesRefresh,
        bool SegmentsSwitchExclusively);

    private sealed record MainThemeExerciseResult(
        IReadOnlyList<string> BackgroundColors,
        IReadOnlyList<int> CharacterOpacities,
        IReadOnlyList<string> CharacterPanelColors,
        bool ReminderLabelFollowsTheme,
        bool SegmentSelectedTextContrast,
        bool CharacterPanelDocksFollowTheme,
        bool ReferenceDecorationsFollowTheme);

    private sealed record FloatingQuickAddThemeValidation(
        FloatingQuickAddThemeProbeResult Day,
        string CanvasColor,
        string ActiveColor,
        bool IndependentFromApplicationTheme,
        FloatingQuickAddPaletteBindingValidation DayPaletteBindings)
    {
        public bool DayPaletteBindingsMatch => DayPaletteBindings.Gates.Passed;
    }

    private sealed record MainReferenceDecorationValidation(
        bool ScalingMatches,
        bool LayerOrderMatches);

    private sealed record MainWorkspaceThemeValidation(
        bool StaticResourcesFollowTheme,
        int CountdownCardSampleCount,
        bool CountdownCardResourcesFollowTheme,
        int NoteCardSampleCount,
        bool NoteCardResourcesFollowTheme);

    private sealed record MainReferenceCaptureResult(
        bool Requested,
        bool CapturesWritten,
        bool UsesZeroState,
        bool CanvasMatches,
        bool AlphaIsOpaque,
        string CountdownCapturePath,
        string NotesCapturePath,
        string ManifestPath);

    // 焦点态实拍证据的落盘结果，不进门禁：未请求时两项都为空，失败时只有 Error。
    private sealed record MainFocusEvidenceResult(string ManifestPath, string Error);

    // #134 真实运行态 UI 捕获 seam 的单帧证据条目与 DPI 变更遥测。
    private sealed record MainUiCaptureSeamEntry(
        string CaptureId,
        string Mechanism,
        string Path,
        string Sha256,
        int DipWidth,
        int DipHeight,
        int PixelWidth,
        int PixelHeight,
        double DpiScaleX,
        double DpiScaleY);

    private sealed record MainUiDpiChangeTelemetry(
        bool AwarenessIsPerMonitorV2,
        bool DpiChangedEventObserved,
        double DpiScaleBeforeX,
        double DpiScaleBeforeY,
        double DpiScaleAfterX,
        double DpiScaleAfterY);

    // 主窗口参考场景：交给视觉基线的实拍帧，加上同一批场景里量到的 Stress 标题与提醒字号。
    private sealed record MainReferenceScenes(
        IReadOnlyList<MainReferenceActualCapture> Captures,
        IReadOnlyList<MainStressCountdownTitleMeasurement> StressCountdownTitleMeasurements,
        IReadOnlyList<MainNoteCardReminderFontMeasurement> NoteCardReminderFontMeasurements)
    {
        public static MainReferenceScenes NotCaptured { get; } = new(
            Array.Empty<MainReferenceActualCapture>(),
            Array.Empty<MainStressCountdownTitleMeasurement>(),
            Array.Empty<MainNoteCardReminderFontMeasurement>());

        public bool StressCountdownTitleVisuals =>
            StressCountdownTitleMeasurements.Count == 1 &&
            StressCountdownTitleMeasurements
                .Select(measurement => measurement.CaptureId)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    new[]
                    {
                        "main-day-countdowns-stress"
                    },
                    StringComparer.Ordinal) &&
            StressCountdownTitleMeasurements.All(measurement =>
                ValidationExercises.EvaluateMainStressCountdownTitleGates(
                    new MainStressCountdownTitleGateInputs(
                        measurement.ExactTitleRendered &&
                        string.Equals(
                            measurement.ExpectedText,
                            measurement.RenderedText,
                            StringComparison.Ordinal),
                        string.Equals(measurement.TextWrapping, "NoWrap", StringComparison.Ordinal) &&
                        string.Equals(measurement.TextTrimming, "None", StringComparison.Ordinal),
                        measurement.TextFits,
                        measurement.FullyVisibleInViewport,
                        measurement.SeparatedFromRemaining,
                        measurement.SeparatedFromActions)).AllPassed);

        public double NoteCardReminderFontSizeDip =>
            NoteCardReminderFontMeasurements.Count == 0
                ? 0
                : NoteCardReminderFontMeasurements.Min(measurement => measurement.FontSizeDip);

        public bool NoteCardReminderFontSizeMeetsContract =>
            NoteCardReminderFontMeasurements.Count > 0 &&
            NoteCardReminderFontMeasurements.All(measurement => measurement.MeetsContract);
    }

    private sealed record MainDaySingleBorderSpec(
        string StateId,
        string Scene,
        string FileName,
        int ScanTop,
        int ScanBottom,
        IReadOnlyList<int> ExpectedEdges,
        int LabelTop,
        int LabelBottom);

    private sealed record MainDaySingleBorderMeasurement(
        string StateId,
        string Scene,
        IReadOnlyList<int> EdgeRows,
        bool ExactControlEdges,
        bool LabelsClear,
        bool LowerDecorationPresent,
        int ControlScanTop,
        int DeclaredBottomBandStart,
        int LowerDecorationPixelCount);

    private sealed record MainDecorationBands(
        int CanvasWidth,
        int CanvasHeight,
        int BackgroundSampleX,
        IReadOnlyDictionary<string, MainDecorationBand> Scenes);

    private sealed record MainDecorationBand(int TopBandEnd, int BottomBandStart);

    private sealed record MainDaySingleBorderValidation(
        bool Requested,
        bool DaySingleBorder,
        bool LabelsClear,
        bool LowerDecorationConsistent,
        IReadOnlyList<MainDaySingleBorderMeasurement> Measurements)
    {
        public bool AllPassed => !Requested ||
            DaySingleBorder && LabelsClear && LowerDecorationConsistent;

        public static MainDaySingleBorderValidation NotRequested { get; } =
            new(false, false, false, false, Array.Empty<MainDaySingleBorderMeasurement>());
    }

    private sealed record MainStressCountdownTitleBounds(
        double X,
        double Y,
        double Width,
        double Height)
    {
        public static MainStressCountdownTitleBounds Empty { get; } = new(0, 0, 0, 0);

        public static MainStressCountdownTitleBounds From(Rect bounds) =>
            new(
                Math.Round(bounds.X, 2),
                Math.Round(bounds.Y, 2),
                Math.Round(bounds.Width, 2),
                Math.Round(bounds.Height, 2));
    }

    private sealed record MainNoteCardReminderFontMeasurement(
        string CaptureId,
        string Theme,
        string State,
        int SampleCount,
        double FontSizeDip,
        bool MeetsContract);

    private sealed record MainStressCountdownTitleMeasurement(
        string CaptureId,
        string Theme,
        string ExpectedText,
        string RenderedText,
        bool ExactTitleRendered,
        string TextWrapping,
        string TextTrimming,
        double FormattedWidth,
        double AvailableWidth,
        bool TextFits,
        MainStressCountdownTitleBounds TitleBounds,
        MainStressCountdownTitleBounds ViewportBounds,
        bool FullyVisibleInViewport,
        MainStressCountdownTitleBounds RemainingBounds,
        bool SeparatedFromRemaining,
        MainStressCountdownTitleBounds ActionsBounds,
        bool SeparatedFromActions)
    {
        public static MainStressCountdownTitleMeasurement Missing(
            MainReferenceCaptureSpec spec,
            string expectedText,
            string renderedText = "",
            bool exactTitleRendered = false,
            string textWrapping = "",
            string textTrimming = "") =>
            new(
                spec.CaptureId,
                spec.Theme,
                expectedText,
                renderedText,
                exactTitleRendered,
                textWrapping,
                textTrimming,
                0,
                0,
                false,
                MainStressCountdownTitleBounds.Empty,
                MainStressCountdownTitleBounds.Empty,
                false,
                MainStressCountdownTitleBounds.Empty,
                false,
                MainStressCountdownTitleBounds.Empty,
                false);
    }

    private sealed record VisualCaptureArtifactBinding(
        string Id,
        string Path,
        string Sha256);

    private sealed record FloatingNoteCardBinding(
        Border Card,
        Grid Layout,
        CheckBox Completion,
        StackPanel Body,
        TextBlock BodyText,
        TextBlock? ReminderText,
        Border DateStamp,
        NoteRecord Note);

    private sealed record FloatingLongNoteFrameMeasurement(
        string CaptureId,
        string Theme,
        string State,
        double VerticalOffset,
        double ScrollableHeight,
        string NoteId,
        string NoteTextSha256,
        string FirstVisibleAnchor,
        string LastVisibleAnchor,
        bool BodyHasNoManualLineBreaks,
        bool NaturalWraps,
        bool UsesOnlyOuterScroller,
        bool OuterScrollable,
        bool PrimaryAnchorReachable,
        bool SafeAreaMatches,
        double BodyFontSize,
        double BodyLineHeight,
        double ReminderFontSize,
        double ReminderLineHeight,
        bool TypographyMatches);

    private sealed record FloatingLongNotesValidation(
        bool BodyHasNoManualLineBreaks,
        bool NaturalWraps,
        bool UsesOnlyOuterScroller,
        bool OuterScrollable,
        bool TopBottomReachable,
        bool SafeAreaMatches,
        bool TypographyMatches,
        IReadOnlyList<FloatingLongNoteFrameMeasurement> Measurements,
        IReadOnlyList<FloatingReferenceActualCapture> Captures)
    {
        public bool LayoutValid =>
            BodyHasNoManualLineBreaks &&
            NaturalWraps &&
            UsesOnlyOuterScroller &&
            OuterScrollable &&
            TopBottomReachable &&
            SafeAreaMatches &&
            TypographyMatches &&
            Measurements.Count == 2 &&
            Captures.Count == 2;

        public static FloatingLongNotesValidation NotRun { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            Array.Empty<FloatingLongNoteFrameMeasurement>(),
            Array.Empty<FloatingReferenceActualCapture>());
    }

    private sealed record FloatingPrimaryCountdownContrastMeasurement(
        string Theme,
        string PrimaryCountdownAccentBrushKey,
        string PlushGlowBrushKey,
        string Foreground,
        string PrimarySurfaceBackground,
        double PrimarySurfaceOpacity,
        string TaskCardBackground,
        string EffectiveTaskCardBackground,
        string CompositeBackground,
        double ContrastRatio,
        double FontSize,
        bool UsesExpectedSemanticAccent,
        bool AccentTrackUsesAccentToken,
        bool AccentDiffersFromPlushGlow,
        string Scenario,
        string RemainingText,
        double TitleFontSize,
        bool IsSingleLineAndFullyVisible);

    private sealed record FloatingPrimaryCountdownContrastValidation(
        FloatingPrimaryCountdownContrastGateResults Gates,
        IReadOnlyList<FloatingPrimaryCountdownContrastMeasurement> Measurements);

    private sealed record CountdownEmptyStateContrastMeasurement(
        string Theme,
        string Foreground,
        string ActualBackground,
        double ContrastRatio,
        bool ForegroundUsesMutedBrush,
        bool ActualBackgroundMatchesBackdrop,
        bool TransparentAncestors,
        bool TextWithinBackdrop,
        string HeadingForeground,
        double HeadingContrastRatio,
        bool HeadingUsesInkBrush,
        bool HeadingTextWithinBackdrop);

    private sealed record CountdownEmptyStateContrastValidation(
        bool TextContrast,
        IReadOnlyList<CountdownEmptyStateContrastMeasurement> Measurements);

    private sealed record NoteEmptyStateContrastValidation(
        bool TextContrast,
        IReadOnlyList<CountdownEmptyStateContrastMeasurement> Measurements);

    private sealed record MainNoteCompletionMeasurement(
        string User,
        double BoxWidth,
        double BoxHeight,
        double TickWidth,
        double TickHeight,
        double CornerRadius,
        bool UsesDedicatedStyle,
        bool UsesWarmTokens,
        bool UsesCompletionSuccessStyle);

    private sealed record MainNoteCompletionInteractionValidation(
        bool Checked,
        bool Unchecked,
        bool PersistenceFailureRolledBack,
        bool AccessibleNamesUpdated,
        bool AccessibleNamesHaveNoLineBreaks = false);

    private sealed record MainNoteCompletionValidation(
        MainNoteCompletionMeasurement Main,
        MainNoteCompletionMeasurement TagSelector,
        MainNoteCompletionMeasurement Floating,
        MainNoteCompletionInteractionValidation Interaction)
    {
        public IReadOnlyList<MainNoteCompletionMeasurement> Measurements =>
            [Main, TagSelector, Floating];

        public static MainNoteCompletionValidation Failed { get; } = new(
            new("MainWindow", 0, 0, 0, 0, 0, false, false, false),
            new("TagSelector", 0, 0, 0, 0, 0, false, false, false),
            new("FloatingWindow", 0, 0, 0, 0, 0, false, false, false),
            new(false, false, false, false));
    }

    private sealed record FloatingReferenceExerciseResult(
        bool AspectMatches,
        bool PanelGeometryMatches,
        bool MascotGeometryMatches,
        FloatingReferenceBaselineValidation Baseline,
        FloatingLongNotesValidation LongNotes,
        FloatingPrimaryCountdownContrastValidation PrimaryCountdownContrast,
        bool ContentSafeAreaMatches,
        bool TitleUsesSemiBoldWeight,
        bool NoteInputWraps,
        bool NoteInputScrolls,
        bool QuickAddDayExpandedScrolledCaptureWritten,
        int ContentOverlapCount,
        IReadOnlyList<string> ContentOverlapTargets,
        bool CaptureWritten,
        bool WorkdayCountdownUsesPrimaryVisual,
        bool WorkdayPrimaryBadgeExclusive,
        bool ScenarioMatches,
        bool PrimaryDeadlineTextFullyVisible,
        bool PrimaryExtremeRemainingShrinks,
        bool ExpiredFallbackRemainingTextFullyVisible,
        IReadOnlyList<FloatingPrimaryDeadlineMeasurement> PrimaryDeadlineMeasurements,
        bool ViewportStatesMatch,
        bool RasterCardStatesMatch,
        bool DynamicCardContentRemainsWpf,
        bool ViewportScrollAndFocusRestored,
        bool NoteFailureAppearsInSourceCard,
        bool ResolvedNoteCardErrorDoesNotReturn,
        bool SecondaryRemainingTextFullyVisible,
        bool IncomeEncouragementWithoutSalary,
        bool IncomePrivacyBehavior,
        bool WorkdayCompletionBehavior,
        FloatingSecondaryRemainingGateResults SecondaryRemainingGates,
        IReadOnlyList<FloatingSecondaryRemainingMeasurement> SecondaryRemainingSamples,
        FloatingScrollBarValidation ScrollBar,
        IReadOnlyList<double> ActualSize,
        string CapturePath);

    private sealed record FloatingPrimaryDeadlineMeasurement(
        bool ScrollbarVisible,
        string Text,
        double ActualWidth,
        double RequiredWidth,
        bool FullyVisible);

    private sealed record FloatingSecondaryRemainingMeasurement(
        string Precision,
        string ExpectedText,
        string FormattedText,
        string RenderedText,
        double ColumnWidth,
        double ActualWidth,
        double FontSize,
        bool ColumnIs194Pixels,
        bool IsRightAligned,
        bool IsSingleLineAndUntrimmed,
        bool IsFullyVisible,
        bool UsesTabularNumerals,
        bool PreferenceSaved);

    private sealed record FloatingScrollBarParts(
        ScrollBar ScrollBar,
        Grid HitRail,
        Track Track,
        FloatingScrollBarThumb Thumb,
        Border ThumbVisual);

    private sealed record FloatingScrollBarStateMeasurement(
        string Theme,
        string State,
        double HitTargetWidth,
        double ThumbVisualWidth,
        double ThumbOpacity,
        double TrackRight,
        double ThumbHitLeft,
        double ThumbHitRight,
        double ScrollBarRight,
        double ThumbVisualLeft,
        double ThumbVisualRight,
        double MaximumVisibleCardRight,
        double BodyContentRight,
        bool CardSurfacesAreClear,
        bool ContentIsClear,
        bool DoesNotObscureContent,
        bool UsesThemeTokens,
        string ThumbColor,
        string BorderColor,
        double BoundaryContrast);

    private sealed record FloatingScrollBarValidation(
        FloatingScrollBarGateResults Gates,
        IReadOnlyList<FloatingScrollBarStateMeasurement> Measurements,
        double WheelOffsetBefore,
        double WheelOffsetAfter,
        double DragOffsetBefore,
        double DragOffsetAfter,
        IReadOnlyDictionary<string, byte[]> Captures)
    {
        public bool StateCapturesReady =>
            Captures.Count == 3 &&
            Captures.Values.All(bytes => bytes.Length > 0);

        public static FloatingScrollBarValidation NotRun { get; } = new(
            new FloatingScrollBarGateResults(false, false, false, false, false, false),
            Array.Empty<FloatingScrollBarStateMeasurement>(),
            0,
            0,
            0,
            0,
            new Dictionary<string, byte[]>(StringComparer.Ordinal));
    }

    // Private nested capture only; these fields are not validate-only top-level JSON keys.
    // Top-level shape grows when a new public validate-only field is added on purpose.
    private sealed record FloatingViewportState(
        string Name,
        double WindowWidth,
        double WindowHeight,
        double RootWidth,
        double RootHeight,
        int RenderedCardCount,
        int RasterCardCount,
        bool RasterCardsShareSource,
        int DynamicTextElementCount,
        int DynamicCheckBoxCount,
        int RenderedMultilineNoteCount,
        double ScrollableHeight,
        double VerticalOffset,
        bool VerticalScrollBarVisible,
        double VerticalScrollBarLeft,
        double VerticalScrollBarRight,
        bool VerticalScrollBarIsHitTestVisible,
        double BodyContentRight,
        double MaximumRenderedCardRight,
        double MinimumRenderedCardWidth,
        int CalendarIndexDateStampCount,
        double MaximumCalendarIndexDateStampRight,
        double NoteEntryWidth,
        double NoteEntryRight,
        bool NoteEntryFitsSafeArea,
        bool WorkspaceSurfaceGeometryMatches,
        IReadOnlyList<string> ContentOverlapTargets)
    {
        public bool WindowSizeMatches =>
            Math.Abs(WindowWidth - 520) <= 0.5 &&
            Math.Abs(WindowHeight - 390) <= 0.5 &&
            Math.Abs(RootWidth - 520) <= 0.5 &&
            Math.Abs(RootHeight - 390) <= 0.5;

        public bool ContentSafeAreaMatches => ContentOverlapTargets.Count == 0;

        public bool VerticalScrollBarIsSafe =>
            !VerticalScrollBarVisible ||
            (VerticalScrollBarIsHitTestVisible &&
             VerticalScrollBarLeft >= 0 &&
             VerticalScrollBarRight <= 396.5);
    }

    private sealed record NormalizedRect(double X, double Y, double Width, double Height)
    {
        public static NormalizedRect Empty { get; } = new(0, 0, 0, 0);

        public double Right => X + Width;

        public double Bottom => Y + Height;
    }

    private sealed record PlushSceneCapture(
        int Width,
        int Height,
        byte[] Pixels,
        int Stride,
        IReadOnlyList<Rect?> Bounds);

    private sealed record NamedBounds(string Name, Rect Bounds);

    private sealed class GatedTrayForegroundInterop : TrayForegroundInterop
    {
        private bool _allow;

        public void AllowRestores() => _allow = true;

        internal override bool Activate(FloatingWindow window) => _allow && base.Activate(window);

        internal override bool Focus(FloatingWindow window) => _allow && base.Focus(window);

        internal override bool TrySetForeground(IntPtr handle) => _allow && base.TrySetForeground(handle);

        internal override bool IsForegroundOrActive(FloatingWindow window, IntPtr handle) =>
            _allow && base.IsForegroundOrActive(window, handle);
    }

    private sealed record ReminderCenterSourceLabelContrastMeasurement(
        string Theme,
        string SourceKind,
        string Foreground,
        string Background,
        double ContrastRatio,
        bool UsesInkBrush,
        bool EmphasisPreserved,
        bool ReminderCardBackground);

    private sealed record ReminderCenterSourceLabelContrastValidation(
        bool TextContrast,
        bool UsesInkBrush,
        bool EmphasisPreserved,
        bool ReminderCardBackground,
        IReadOnlyList<ReminderCenterSourceLabelContrastMeasurement> Measurements,
        IReadOnlyList<double> RelativeTimeContrastRatios)
    {
        public bool RelativeTimeTextContrast => RelativeTimeContrastRatios.Count == 2 &&
            RelativeTimeContrastRatios.All(ratio => ratio >= 4.5);
    }

    private sealed record SystemDialogPrimaryButtonContrastMeasurement(
        string Window,
        string Foreground,
        string Background,
        double ContrastRatio);

    private sealed record SystemDialogPrimaryButtonContrastValidation(
        bool TextContrast,
        bool UsesDaytimeMainActionColors,
        bool UsesActiveStaticPalette,
        IReadOnlyList<SystemDialogPrimaryButtonContrastMeasurement> Measurements);

    private sealed record KeyboardFocusContrastMeasurement(
        string Surface,
        double ContrastRatio);

    private sealed record KeyboardFocusRingMeasurement(
        string Theme,
        string BorderColor,
        bool UsesExpectedToken,
        bool RingVisible);

    private sealed record KeyboardFocusRingContrastValidation(
        bool PairsMeetContrast,
        bool RingsUseExpectedTokens,
        bool PinKeepsWarmGold,
        bool HoverTokenDistinctFromFocus,
        bool TabRingGapAvoidsSelectedFill,
        bool FloatingCapsuleFocusRingMeetsContrast,
        bool FloatingCapsuleUsesWarmGoldToken,
        IReadOnlyList<KeyboardFocusContrastMeasurement> Measurements,
        IReadOnlyList<KeyboardFocusRingMeasurement> Rings);

    private sealed record ComboBoxTextClickValidation(
        bool TextClickOpens,
        bool ArrowPressOpens,
        bool KeyboardPathsWork,
        bool DisabledClickDoesNotOpen,
        bool PopupItemClickSelects,
        bool ArrowIsNotTabStop,
        int ComboCount,
        IReadOnlyList<ComboBoxPointerExerciseResult> Measurements);

    private sealed record SecondaryWindowHeaderDragValidation(
        bool HeaderRequestsDrag,
        bool ManualPositionPersists,
        bool InteractiveControlsDoNotDrag,
        bool ReminderCenterFirstAppearanceTopRight,
        bool CloseCancelledWithItems,
        bool ResizeGripUnchanged);

    private sealed class ValidationClock : IClock
    {
        public ValidationClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class MutableValidationClock : IClock
    {
        public MutableValidationClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
