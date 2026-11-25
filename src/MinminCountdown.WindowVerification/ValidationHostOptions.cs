namespace MinminCountdown;

internal sealed record ValidationHostOptions(
    bool ValidateOnly,
    bool ExerciseNoteAdd,
    bool ExercisePersistenceFailures,
    bool ExerciseTagInteractions,
    bool ExerciseFatalDiagnostic,
    bool ExerciseRestoreWriteFailure,
    bool ExerciseDispatcherFatalDiagnostic,
    string? SettingsFile,
    string? DiagnosticLogPath,
    bool NightMigrationOnly)
{
    public static ValidationHostOptions Parse(IReadOnlyList<string> arguments)
    {
        var validateOnly = false;
        var exerciseNoteAdd = false;
        var exercisePersistenceFailures = false;
        var exerciseTagInteractions = false;
        var exerciseFatalDiagnostic = false;
        var exerciseRestoreWriteFailure = false;
        var exerciseDispatcherFatalDiagnostic = false;
        string? settingsFile = null;
        string? diagnosticLogPath = null;
        var nightMigrationOnly = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var option = NormalizeOption(arguments[index]);
            switch (option)
            {
                case "validateonly":
                    validateOnly = true;
                    break;
                case "nightmigrationonly":
                    nightMigrationOnly = true;
                    break;
                case "exercisenoteadd":
                    exerciseNoteAdd = true;
                    break;
                case "exercisepersistencefailures":
                    exercisePersistenceFailures = true;
                    break;
                case "exercisetaginteractions":
                    exerciseTagInteractions = true;
                    break;
                case "exercisefataldiagnostic":
                    exerciseFatalDiagnostic = true;
                    break;
                case "exerciserestorewritefailure":
                    exerciseRestoreWriteFailure = true;
                    break;
                case "exercisedispatcherfataldiagnostic":
                    exerciseDispatcherFatalDiagnostic = true;
                    break;
                case "settingsfile":
                    settingsFile = ReadValue(arguments, ref index, arguments[index]);
                    break;
                case "diagnosticlogpath":
                    diagnosticLogPath = ReadValue(arguments, ref index, arguments[index]);
                    break;
                default:
                    throw new ArgumentException($"不支持的启动参数：{arguments[index]}");
            }
        }

        if (nightMigrationOnly &&
            (!validateOnly || exerciseNoteAdd || exercisePersistenceFailures || exerciseTagInteractions || exerciseFatalDiagnostic || exerciseRestoreWriteFailure || exerciseDispatcherFatalDiagnostic))
        {
            throw new ArgumentException("--night-migration-only 只能单独与 --validate-only 一起使用。");
        }

        if ((exerciseNoteAdd || exercisePersistenceFailures || exerciseTagInteractions || exerciseFatalDiagnostic || exerciseRestoreWriteFailure || exerciseDispatcherFatalDiagnostic) &&
            !validateOnly)
        {
            throw new ArgumentException("exercise 参数只能与 --validate-only 一起使用。");
        }

        if (validateOnly && string.IsNullOrWhiteSpace(settingsFile))
        {
            throw new ArgumentException("--validate-only 必须显式指定 --settings-file。");
        }

        return new ValidationHostOptions(
            validateOnly,
            exerciseNoteAdd,
            exercisePersistenceFailures,
            exerciseTagInteractions,
            exerciseFatalDiagnostic,
            exerciseRestoreWriteFailure,
            exerciseDispatcherFatalDiagnostic,
            settingsFile,
            diagnosticLogPath,
            nightMigrationOnly);
    }

    private static string NormalizeOption(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '-')
        {
            throw new ArgumentException($"启动参数格式无效：{value}");
        }

        return new string(value.TrimStart('-').Where(static character => character != '-').ToArray())
            .ToLowerInvariant();
    }

    private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        return Path.GetFullPath(ReadTextValue(arguments, ref index, option));
    }

    private static string ReadTextValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
        {
            throw new ArgumentException($"{option} 缺少路径值。");
        }

        return arguments[index];
    }
}
