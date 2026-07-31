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
        var settingsPath = options.SettingsFile ??
                           Path.Combine(applicationRoot, "settings.json");
        MainWindow? mainWindow = null;
        FloatingWindow? floatingWindow = null;
        FloatingTrayController? floatingTrayController = null;
        HolidayCalendar? calendar = null;
        EventHandler? themePreferenceChanged = null;

        try
        {
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
                static () => true);

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
}
