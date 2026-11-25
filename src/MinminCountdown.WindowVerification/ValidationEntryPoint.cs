using System.Reflection;
using System.Text;
using System.Windows;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static class ValidationEntryPoint
{
    internal static bool IsRequested(IReadOnlyList<string> arguments) =>
        arguments.Any(argument => !string.Equals(argument, "--", StringComparison.Ordinal));

    internal static int Run(IReadOnlyList<string> arguments)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        ValidationHostOptions options;
        try
        {
            options = ValidationHostOptions.Parse(arguments);
        }
        catch (Exception exception)
        {
            CliOutput.WriteError($"{exception.GetType().FullName}: {exception.Message}");
            return 1;
        }

        if (!options.ValidateOnly)
        {
            CliOutput.WriteError("System.ArgumentException: 窗口验证报告必须显式使用 --validate-only。");
            return 1;
        }

        ApplicationValidation.PrepareRuntime();
        var app = new App();
        app.InitializeComponent();
        var productionStartup = typeof(App).GetMethod(
            "OnStartup",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?.CreateDelegate<StartupEventHandler>(app)
            ?? throw new MissingMethodException(typeof(App).FullName, "OnStartup");
        // The validation host loads the real application resources but never enters
        // the product Startup path or its interactive single-instance activation.
        app.Startup -= productionStartup;
        ApplicationTheme.WriteDayPalette(app.Resources);

        var applicationRoot = ApplicationPaths.ResolveApplicationRoot();
        var diagnosticPath = options.DiagnosticLogPath ??
                             Path.Combine(applicationRoot, "logs", "application.jsonl");
        var logger = new DiagnosticLogger(diagnosticPath);
        logger.Info("ProcessStarted");
        var settingsPath = options.SettingsFile ?? Path.Combine(applicationRoot, "settings.json");
        var settingsExisted = File.Exists(settingsPath);
        var settingsBytes = settingsExisted ? File.ReadAllBytes(settingsPath) : null;
        var settingsMigrated = false;
        try
        {
            if (options.ExerciseFatalDiagnostic)
            {
                throw new InvalidOperationException("Synthetic fatal diagnostic probe.");
            }

            var report = ApplicationValidation.Run(options, applicationRoot, logger, app.Resources);
            settingsMigrated =
                report.TryGetValue("SettingsWereMigrated", out var migratedValue) &&
                migratedValue is true;
            CliOutput.WriteJson(report);
            logger.Info("ValidationCompleted");
            return 0;
        }
        catch (SettingsRecoveryDeclinedException exception)
        {
            logger.Info("SettingsRecoveryDeclinedExit");
            CliOutput.WriteError($"{exception.GetType().FullName}: {exception.Message}");
            return 1;
        }
        catch (SettingsRecoveryException exception)
        {
            logger.Warning("SettingsRecoveryAborted");
            SettingsRecoveryFailurePresentation.TryPresent(
                exception,
                SettingsRecoveryWindow.ShowFailure);
            CliOutput.WriteError($"{exception.GetType().FullName}: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            logger.Error("FatalUnhandledException", exception);
            CliOutput.WriteError($"{exception.GetType().FullName}: {exception.Message}");
            return 1;
        }
        finally
        {
            // Ordinary reports restore the original bytes. Explicit note/tag
            // exercises keep their persistence writes, and migration keeps the
            // canonical rewritten settings instead of restoring legacy bytes.
            if (settingsBytes is not null &&
                !options.ExerciseNoteAdd &&
                !options.ExerciseTagInteractions &&
                !settingsMigrated)
            {
                File.WriteAllBytes(settingsPath, settingsBytes);
            }
        }
    }
}
