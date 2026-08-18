using System.Windows;

namespace MinminCountdown;

internal static class ApplicationValidation
{
    public static IReadOnlyDictionary<string, object?> Run(
        HostOptions options,
        string applicationRoot,
        DiagnosticLogger logger,
        ResourceDictionary resources)
    {
        var settingsPath = ResolveSettingsPath(options, applicationRoot);
        MainWindow? mainWindow = null;
        FloatingWindow? floatingWindow = null;
        FloatingTrayController? floatingTrayController = null;
        HolidayCalendar? calendar = null;
        EventHandler? themePreferenceChanged = null;

        try
        {
            if (!ValidationExercises.RunSettingsRecoveryWindowBehavior())
            {
                throw new InvalidOperationException("设置恢复窗口或失败呈现路由验证失败。");
            }

            if (!ValidationExercises.RunLazyManagerLifecycleBehavior())
            {
                throw new InvalidOperationException("懒加载管理窗口生命周期验证失败。");
            }

            var desktopWorkspace = DesktopWorkspace.Load(settingsPath);
            var initialSnapshot = desktopWorkspace.Snapshot;
            var theme = new ApplicationTheme(resources);
            theme.Apply(
                initialSnapshot.UtcNow,
                initialSnapshot.Settings.Preferences.ThemePreference);
            calendar = new HolidayCalendar(applicationRoot);
            mainWindow = new MainWindow(
                desktopWorkspace,
                calendar,
                logger,
                AppContext.BaseDirectory);
            mainWindow.ApplyTheme(theme.ActiveMode);
            floatingWindow = new FloatingWindow(
                desktopWorkspace,
                calendar,
                mainWindow,
                AppContext.BaseDirectory);
            floatingTrayController = new FloatingTrayController(
                floatingWindow,
                Path.Combine(applicationRoot, "countdown-icon.ico"),
                // Validate-only must be allowed to exercise tray transitions; this is not an acceptance gate.
                static () => true);
            mainWindow.MinimizeToTrayRequested += (_, _) =>
            {
                mainWindow.Hide();
                floatingTrayController.MinimizeToTray();
            };

            void ApplyCurrentTheme()
            {
                var snapshot = desktopWorkspace.Snapshot;
                var mode = theme.Apply(
                    snapshot.UtcNow,
                    snapshot.Settings.Preferences.ThemePreference);
                mainWindow.ApplyTheme(mode);
                floatingWindow.ApplyTheme(mode);
            }

            themePreferenceChanged = (_, _) => ApplyCurrentTheme();
            mainWindow.ThemePreferenceChanged += themePreferenceChanged;
            ApplyCurrentTheme();

            var report = ValidationReport.Create(
                mainWindow,
                floatingWindow,
                floatingTrayController,
                desktopWorkspace,
                theme,
                options,
                AppContext.BaseDirectory);
            return report;
        }
        finally
        {
            if (mainWindow is not null && themePreferenceChanged is not null)
            {
                mainWindow.ThemePreferenceChanged -= themePreferenceChanged;
            }

            floatingTrayController?.Dispose();
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                window.Close();
            }

            calendar?.Dispose();
            logger.Info("ValidationResourcesReleased");
        }
    }

    internal static string ResolveSettingsPath(HostOptions options, string applicationRoot)
    {
        var settingsPath = options.SettingsFile ??
                           throw new ArgumentException(
                               "--validate-only 必须显式指定 --settings-file。");
        var resolvedSettingsPath = Path.GetFullPath(settingsPath);
        var rootSettingsPath = Path.GetFullPath(Path.Combine(applicationRoot, "settings.json"));
        if (string.Equals(
                resolvedSettingsPath,
                rootSettingsPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "--validate-only 拒绝使用应用根 settings.json；请指定唯一临时设置文件。");
        }

        return resolvedSettingsPath;
    }
}
