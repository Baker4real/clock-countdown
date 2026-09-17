namespace MinminCountdown;

internal sealed record HostOptions(
    bool ValidateOnly,
    bool ExerciseNoteAdd,
    bool ExercisePersistenceFailures,
    bool ExerciseTagInteractions,
    bool ExerciseFatalDiagnostic,
    bool ExitAfterReadyForTest,
    int ExitAfterReadyForTestSeconds,
    bool RequireStartupRequest,
    string? SettingsFile,
    string? DiagnosticLogPath,
    InstanceActivationTestState? ActivationTestState,
    InstanceActivationListenerBehavior ActivationListenerBehavior)
{
    // 测试自退间隔只是测试参数：生产默认保持 30 秒，套件可传更短值压缩纯等待。
    internal const int DefaultExitAfterReadyForTestSeconds = 30;
    internal const int MinExitAfterReadyForTestSeconds = 1;
    internal const int MaxExitAfterReadyForTestSeconds = 600;

    public static HostOptions Parse(IReadOnlyList<string> arguments)
    {
        var validateOnly = false;
        var exerciseNoteAdd = false;
        var exercisePersistenceFailures = false;
        var exerciseTagInteractions = false;
        var exerciseFatalDiagnostic = false;
        var exitAfterReadyForTest = false;
        var exitAfterReadyForTestSeconds = DefaultExitAfterReadyForTestSeconds;
        var requireStartupRequest = false;
        string? settingsFile = null;
        string? diagnosticLogPath = null;
        InstanceActivationTestState? activationTestState = null;
        var activationListenerBehavior = InstanceActivationListenerBehavior.Normal;

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
                case "exitafterreadyseconds":
                    if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                    {
                        throw new ArgumentException("--exit-after-ready-seconds 缺少秒数值。");
                    }

                    var exitSecondsValue = arguments[index];
                    if (!int.TryParse(exitSecondsValue, out var parsedExitSeconds) ||
                        parsedExitSeconds < MinExitAfterReadyForTestSeconds ||
                        parsedExitSeconds > MaxExitAfterReadyForTestSeconds)
                    {
                        throw new ArgumentException(
                            $"--exit-after-ready-seconds 必须是 {MinExitAfterReadyForTestSeconds}..{MaxExitAfterReadyForTestSeconds} 之间的整数：{exitSecondsValue}");
                    }

                    exitAfterReadyForTestSeconds = parsedExitSeconds;
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
                case "instanceactivationteststate":
                    var stateValue = ReadTextValue(arguments, ref index, arguments[index]);
                    if (!Enum.TryParse<InstanceActivationTestState>(stateValue, ignoreCase: true, out var parsedState) ||
                        parsedState is not (InstanceActivationTestState.FloatingVisible or
                                            InstanceActivationTestState.Tray or
                                            InstanceActivationTestState.Hidden or
                                            InstanceActivationTestState.Minimized or
                                            InstanceActivationTestState.MainVisible))
                    {
                        throw new ArgumentException($"不支持的实例激活测试状态：{stateValue}");
                    }

                    activationTestState = parsedState;
                    break;
                case "instanceactivationlistenerbehavior":
                    var behaviorValue = ReadTextValue(arguments, ref index, arguments[index]);
                    if (!Enum.TryParse<InstanceActivationListenerBehavior>(
                            behaviorValue,
                            ignoreCase: true,
                            out var parsedBehavior) ||
                        parsedBehavior is not (InstanceActivationListenerBehavior.Normal or
                                               InstanceActivationListenerBehavior.Timeout or
                                               InstanceActivationListenerBehavior.ProtocolMismatch or
                                               InstanceActivationListenerBehavior.Disconnect or
                                               InstanceActivationListenerBehavior.CallbackFailureOnce))
                    {
                        throw new ArgumentException($"不支持的实例激活监听器测试行为：{behaviorValue}");
                    }

                    activationListenerBehavior = parsedBehavior;
                    break;
                default:
                    throw new ArgumentException($"不支持的启动参数：{arguments[index]}");
            }
        }

        if (!exitAfterReadyForTest &&
            exitAfterReadyForTestSeconds != DefaultExitAfterReadyForTestSeconds)
        {
            throw new ArgumentException(
                "--exit-after-ready-seconds 只能与 --exit-after-ready-for-test 一起使用。");
        }

        if ((exerciseNoteAdd || exercisePersistenceFailures || exerciseTagInteractions || exerciseFatalDiagnostic) &&
            !validateOnly)
        {
            throw new ArgumentException("exercise 参数只能与 --validate-only 一起使用。");
        }

        if (validateOnly && string.IsNullOrWhiteSpace(settingsFile))
        {
            throw new ArgumentException("--validate-only 必须显式指定 --settings-file。");
        }

        if (requireStartupRequest &&
            (activationTestState is not null ||
             activationListenerBehavior != InstanceActivationListenerBehavior.Normal))
        {
            throw new ArgumentException(
                "实例激活测试参数不能与 --require-startup-request 组合。");
        }

        if (activationTestState is not null &&
            (validateOnly ||
             !exitAfterReadyForTest ||
             string.IsNullOrWhiteSpace(settingsFile) ||
             string.IsNullOrWhiteSpace(diagnosticLogPath)))
        {
            throw new ArgumentException(
                "实例激活测试状态只能用于带隔离设置和诊断路径的 --exit-after-ready-for-test 普通交互进程。");
        }

        if (activationListenerBehavior != InstanceActivationListenerBehavior.Normal &&
            (validateOnly ||
             !exitAfterReadyForTest ||
             string.IsNullOrWhiteSpace(settingsFile) ||
             string.IsNullOrWhiteSpace(diagnosticLogPath)))
        {
            throw new ArgumentException(
                "实例激活监听器测试行为只能用于带隔离设置和诊断路径的 --exit-after-ready-for-test 普通交互进程。");
        }

        return new HostOptions(
            validateOnly,
            exerciseNoteAdd,
            exercisePersistenceFailures,
            exerciseTagInteractions,
            exerciseFatalDiagnostic,
            exitAfterReadyForTest,
            exitAfterReadyForTestSeconds,
            requireStartupRequest,
            settingsFile,
            diagnosticLogPath,
            activationTestState,
            activationListenerBehavior);
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

internal enum InstanceActivationTestState
{
    FloatingVisible,
    Tray,
    Hidden,
    Minimized,
    MainVisible
}
