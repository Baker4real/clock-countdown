namespace MinminCountdown;

internal sealed record HostOptions(
    bool ValidateOnly,
    bool ExerciseNoteAdd,
    bool ExerciseThemePreference,
    bool ExercisePersistenceFailures,
    bool ExerciseTagInteractions,
    bool ExerciseFatalDiagnostic,
    bool ExitAfterReadyForTest,
    bool RequireStartupRequest,
    string? SettingsFile,
    string? DiagnosticLogPath)
{
    public static HostOptions Parse(IReadOnlyList<string> arguments)
    {
        var validateOnly = false;
        var exerciseNoteAdd = false;
        var exerciseThemePreference = false;
        var exercisePersistenceFailures = false;
        var exerciseTagInteractions = false;
        var exerciseFatalDiagnostic = false;
        var exitAfterReadyForTest = false;
        var requireStartupRequest = false;
        string? settingsFile = null;
        string? diagnosticLogPath = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var option = NormalizeOption(arguments[index]);
            switch (option)
            {
                case "validateonly":
                    validateOnly = true;
                    break;
                case "exercisenoteadd":
                    exerciseNoteAdd = true;
                    break;
                case "exercisethemepreference":
                    exerciseThemePreference = true;
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
                case "exitafterreadyfortest":
                    exitAfterReadyForTest = true;
                    break;
                case "requirestartuprequest":
                    requireStartupRequest = true;
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

        if ((exerciseNoteAdd || exerciseThemePreference || exercisePersistenceFailures || exerciseTagInteractions || exerciseFatalDiagnostic) &&
            !validateOnly)
        {
            throw new ArgumentException("exercise 参数只能与 --validate-only 一起使用。");
        }

        return new HostOptions(
            validateOnly,
            exerciseNoteAdd,
            exerciseThemePreference,
            exercisePersistenceFailures,
            exerciseTagInteractions,
            exerciseFatalDiagnostic,
            exitAfterReadyForTest,
            requireStartupRequest,
            settingsFile,
            diagnosticLogPath);
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
        if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
        {
            throw new ArgumentException($"{option} 缺少路径值。");
        }

        return Path.GetFullPath(arguments[index]);
    }
}
