using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MinminCountdown;

internal static class ApplicationValidation
{
    public static IReadOnlyDictionary<string, object?> Run(
        HostOptions options,
        string applicationRoot,
        DiagnosticLogger logger,
        ResourceDictionary resources)
    {
        // Isolated Win32 desktops do not provide a composited DWM surface for
        // WPF's default hardware renderer. Validation captures must still
        // produce pixels, while normal application startup keeps its default.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        // Unattended or locked sessions can report zero display devices, in
        // which case WPF skips rendering entirely; captures must still render.
        // The switch must be set before the first MediaContext is created.
        AppContext.SetSwitch(RenderEvenWhenNoDisplayDevicesSwitch, true);
        EnsureIsolatedValidationDesktop();
        // #211：窗口几何与工作区解析在验证全程注入确定性大工作区（96 DPI），
        // 使 1165×1350 首选构图、窗口夹取与基线捕获与机器真实显示拓扑解耦；
        // 几何探针在其内再注入小工作区并以栈式恢复收口。
        using var validationWorkArea = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(new Rect(0, 0, 2560, 1600), 1, 1));
        logger.Info(
            "ValidationRenderMode " +
            $"tier=0x{RenderCapability.Tier:X} " +
            $"renderEvenWhenNoDisplayDevices={IsRenderEvenWhenNoDisplayDevicesEnabled()}");

        var settingsPath = ResolveSettingsPath(options, applicationRoot);
        MainWindow? mainWindow = null;
        FloatingWindow? floatingWindow = null;
        FloatingTrayController? floatingTrayController = null;
        HolidayCalendar? calendar = null;

        try
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_EMPTY_DAY_PROBE"),
                    "1",
                    StringComparison.Ordinal))
            {
                ValidationReport.RunEmptyDayCaptureProbe(resources, AppContext.BaseDirectory);
                return new Dictionary<string, object?>
                {
                    ["EmptyDayProbeAligned"] = true
                };
            }

            if (!ValidationExercises.RunSettingsRecoveryWindowBehavior())
            {
                throw new InvalidOperationException("设置恢复窗口或失败呈现路由验证失败。");
            }

            if (!ValidationExercises.RunInstanceActivationFailurePromptBehavior())
            {
                throw new InvalidOperationException("实例激活失败提示窗口验证失败。");
            }

            if (!ValidationExercises.RunInstanceActivationStateEqualityBehavior())
            {
                throw new InvalidOperationException("实例激活状态相等性验证失败。");
            }

            if (!ValidationExercises.RunLazyManagerLifecycleBehavior())
            {
                throw new InvalidOperationException("懒加载管理窗口生命周期验证失败。");
            }

            if (!ValidationExercises.RunMainDecorationOnDemandDecodeBehavior())
            {
                throw new InvalidOperationException("管理窗口装饰图按需与按显示尺寸解码验证失败（#231）。");
            }

            var desktopWorkspace = DesktopWorkspace.Load(settingsPath);
            var theme = new ApplicationTheme(resources);
            theme.Apply();
            calendar = new HolidayCalendar(applicationRoot);
            mainWindow = new MainWindow(
                desktopWorkspace,
                calendar,
                logger,
                AppContext.BaseDirectory);
            mainWindow.ApplyTheme();
            floatingWindow = new FloatingWindow(
                desktopWorkspace,
                calendar,
                mainWindow,
                AppContext.BaseDirectory);
            floatingTrayController = new FloatingTrayController(
                floatingWindow,
                Path.Combine(applicationRoot, "countdown-icon.ico"),
                // Validate-only must be allowed to exercise tray transitions; this is not an acceptance gate.
                static () => true,
                foregroundInterop: null,
                attachShellNotifyIcon: false);
            mainWindow.MinimizeToTrayRequested += (_, _) =>
            {
                mainWindow.Hide();
                floatingTrayController.MinimizeToTray();
            };

            floatingWindow.ApplyTheme();

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
            floatingTrayController?.Dispose();
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                window.Close();
            }

            calendar?.Dispose();
            logger.Info("ValidationResourcesReleased");
        }
    }

    private const string RenderEvenWhenNoDisplayDevicesSwitch =
        "Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable";

    internal static void EnsureIsolatedValidationDesktop()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("MINMIN_SUITE_ALLOW_UNISOLATED"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var desktopName = IsolatedValidationDesktop.GetCurrentName();
        if (desktopName.Equals("MinminSuiteIso", StringComparison.Ordinal) ||
            (desktopName.StartsWith("MinminSuiteIso-", StringComparison.Ordinal) &&
             desktopName.Length > "MinminSuiteIso-".Length &&
             char.IsLetter(desktopName["MinminSuiteIso-".Length])))
        {
            return;
        }

        throw new InvalidOperationException(
            "拒绝在用户桌面直跑窗口验证（当前桌面='" + desktopName +
            "'）。请经 tools/Invoke-CountdownSuiteLowImpact.ps1 运行；用户正在用电脑时禁止 -AllowUnisolatedDesktop。");
    }

    private static class IsolatedValidationDesktop
    {
        private const int UoiName = 2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserObjectInformationW(
            IntPtr handle,
            int index,
            StringBuilder info,
            int maxLength,
            out uint lengthNeeded);

        internal static string GetCurrentName()
        {
            var handle = GetThreadDesktop(GetCurrentThreadId());
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "GetThreadDesktop 失败 error=" + Marshal.GetLastWin32Error());
            }

            var builder = new StringBuilder(256);
            if (!GetUserObjectInformationW(handle, UoiName, builder, 256, out _))
            {
                throw new InvalidOperationException(
                    "GetUserObjectInformationW 失败 error=" + Marshal.GetLastWin32Error());
            }

            return builder.ToString();
        }
    }

    internal static bool IsRenderEvenWhenNoDisplayDevicesEnabled() =>
        AppContext.TryGetSwitch(RenderEvenWhenNoDisplayDevicesSwitch, out var enabled) && enabled;

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
