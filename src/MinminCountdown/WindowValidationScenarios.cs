namespace MinminCountdown;

public sealed partial class MainWindow
{
    internal MainWindowValidationScenarios RunValidationScenarios(bool exerciseNoteAdd)
    {
        var noteExercise = exerciseNoteAdd
            ? RunNoteAddExercise()
            : new NoteExerciseResult(false, false, false, false, false);
        var reminderModes = exerciseNoteAdd
            ? RunReminderModeClickExercises()
            : ReminderModeClickResult.NotRun;

        return new MainWindowValidationScenarios(
            noteExercise,
            reminderModes,
            ProbeWorkspaceDraftRetention(),
            ProbeNoteDateBehavior(),
            ProbeCrossDayReminderBehavior(),
            ProbeReminderSummaryAdvancesAfterFirstDue());
    }
}

public sealed partial class FloatingWindow
{
    internal FloatingWindowValidationScenarios RunValidationScenarios(bool exerciseNoteAdd)
    {
        var crossDayReminderAdded =
            exerciseNoteAdd && RunCrossDayReminderAddExercise();
        var reminderModes = exerciseNoteAdd
            ? RunReminderModeClickExercises()
            : ReminderModeClickResult.NotRun;

        return new FloatingWindowValidationScenarios(
            crossDayReminderAdded,
            reminderModes,
            RunDragInteractionExercise(),
            RunThemeBoundaryExercise(),
            RunManualPlacementExercise());
    }
}

internal sealed record MainWindowValidationScenarios(
    NoteExerciseResult NoteExercise,
    ReminderModeClickResult ReminderModes,
    bool WorkspaceDraftsRetained,
    (bool TracksBeijingDay, bool PreservesChosenDate) NoteDateBehavior,
    bool CrossDayReminderBehavior,
    bool ReminderSummaryAdvances);

internal sealed record FloatingWindowValidationScenarios(
    bool CrossDayReminderAdded,
    ReminderModeClickResult ReminderModes,
    FloatingDragInteractionExerciseResult DragInteraction,
    FloatingThemeExerciseResult ThemeExercise,
    FloatingManualPlacementExerciseResult ManualPlacement);
