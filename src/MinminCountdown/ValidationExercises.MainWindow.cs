using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    // #156：非拖动的窗口复位/贴边入口——越界悬浮窗经 Ctrl+R 或托盘菜单「复位窗口位置」
    // 都能被夹回工作区；隐藏态复位先恢复可见再夹回；完成任务的窗口收纳不依赖拖无边框窗。
    public static bool RunWindowPositionResetBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-window-position-reset-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var lifecycle = new ManagerWindowLifecycle(
            () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
            PrepareOffscreenValidationWindow);
        var floating = new FloatingWindow(workspace, calendar, lifecycle, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(floating);
        var trayController = new FloatingTrayController(
            floating,
            Path.Combine(AppContext.BaseDirectory, "countdown-icon.ico"),
            canTransition: static () => true,
            foregroundInterop: null,
            attachShellNotifyIcon: false);
        floating.Show();
        try
        {
            WindowValidationInput.Pump(floating.Dispatcher);
            var workArea = MonitorWorkAreaResolver.ResolveForWindow(floating).WorkAreaDip;

            floating.Left = workArea.Right + 120;
            floating.Top = workArea.Top + 40;
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                !WindowIntersectsWorkArea(floating),
                "前置：悬浮窗应完全位于工作区右侧之外。");

            WindowValidationInput.RaisePreviewKey(floating, Key.R, ModifierKeys.Control);
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                WindowIntersectsWorkArea(floating),
                "Ctrl+R 必须把越界悬浮窗夹回工作区（非拖动复位）。");

            floating.Left = workArea.Right + 120;
            floating.Top = workArea.Bottom + 120;
            WindowValidationInput.Pump(floating.Dispatcher);
            trayController.InvokeResetPositionForValidation();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                WindowIntersectsWorkArea(floating),
                "托盘菜单「复位窗口位置」必须把越界悬浮窗夹回工作区。");

            floating.Hide();
            WindowValidationInput.Pump(floating.Dispatcher);
            trayController.InvokeResetPositionForValidation();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                WindowIntersectsWorkArea(floating),
                "隐藏态的托盘复位必须先恢复悬浮窗再夹回工作区。");
            return true;
        }
        finally
        {
            trayController.Dispose();
            floating.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #230：沿既有窗口 tick/显示入口验证隐藏期间不重建分格，恢复时立即追上时钟。
    public static bool RunMainWindowHiddenRefreshBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-hidden-refresh-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new AdvancingValidationClock(
                new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            MustApply(workspace, new SaveCountdownChange(
                null,
                "隐藏刷新倒计时",
                clock.UtcNow.AddMinutes(10),
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var main = new MainWindow(
                workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                main.UpdateLayout();
                var rows = (Panel)main.FindName("CountdownRows");
                var blocksBefore = VisualDescendants<TextBlock>(rows).ToArray();
                var remainingBefore = main.CountdownRemainingTextsForValidation.Values.Single().Text;
                var nowText = (TextBlock)main.FindName("NowText");
                var timeBefore = nowText.Text;
                Must(blocksBefore.Length > 0, "隐藏刷新探针必须先渲染真实倒计时分格。");

                main.Hide();
                clock.UtcNow = clock.UtcNow.AddMinutes(2);
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                Must(
                    !main.IsVisible &&
                    blocksBefore.SequenceEqual(VisualDescendants<TextBlock>(rows)) &&
                    main.CountdownRemainingTextsForValidation.Values.Single().Text == remainingBefore &&
                    nowText.Text == timeBefore,
                    "#230：管理窗口隐藏期间 tick 不得重建倒计时分格或更新时间文本。");

                // 不能再调用 UpdateTime；Show 本身必须补上隐藏期间跳过的刷新。
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                Must(
                    main.IsVisible &&
                    nowText.Text == "当前北京时间：2038 年 7 月 8 日 09:02:00" &&
                    main.CountdownRemainingTextsForValidation.Values.Single().Text != remainingBefore,
                    "#230：管理窗口重新显示后必须立即显示最新时刻与剩余时间，不等待下一次 tick。");
                return true;
            }
            finally
            {
                main.Close();
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #240：剩余时间分格按需更新。专用管理窗口与悬浮窗共用一条受控时间线：近期行（两窗主项）在 t0+40s
    // 由 1 天跨到 0 天，远期行（次项）在 t0+30s 由 100 天跨到 99 天。目标是整秒、剩余时间向下取整，
    // 所以跨界拍取整秒后 0.25 秒，同一文本的后续拍都落在下一个整秒之前（含）。
    // 分钟精度连续 9 拍文本不变须整拍跳过；秒位变化须就地改数值文字与可访问名称；天数位数变化、
    // #229 省略「天」分格与真实 WM_DPICHANGED（沿用 #219 注入）须重建，重建后字号下限事实仍为真。
    public static RemainingBlocksOnDemandProbe RunRemainingBlocksOnDemand()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-remaining-blocks-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var previousDpiContext = MonitorGeometryValidation.EnterPerMonitorV2ThreadDpiContextForValidation();
        try
        {
            var t0 = new DateTimeOffset(2038, 7, 8, 1, 0, 5, TimeSpan.Zero);
            var clock = new AdvancingValidationClock(t0);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var preferences = workspace.Snapshot.Settings.Preferences;
            MustApply(workspace, new SavePreferencesChange(
                "Minute", preferences.MonthlySalary, preferences.WorkStart, preferences.WorkEnd));
            const string nearName = "分格按需近期";
            const string farName = "分格按需远期";
            MustApply(workspace, new SaveCountdownChange(
                null, nearName, t0.AddDays(1).AddSeconds(40), Array.Empty<string>(), Array.Empty<ReminderRule>()));
            MustApply(workspace, new SaveCountdownChange(
                null, farName, t0.AddDays(100).AddSeconds(30), Array.Empty<string>(), Array.Empty<ReminderRule>()));
            var nearId = workspace.Snapshot.Settings.Countdowns.Single(record => record.Name == nearName).Id;
            var farId = workspace.Snapshot.Settings.Countdowns.Single(record => record.Name == farName).Id;
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var main = new MainWindow(
                workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory);
            var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            PrepareOffscreenValidationWindow(floating);
            try
            {
                main.Show();
                floating.Show();
                main.ShowWorkspace(WorkspaceKind.Countdowns);
                floating.ShowWorkspace(WorkspaceKind.Countdowns);
                WindowValidationInput.Pump(main.Dispatcher);
                var nowText = main.NowText;
                var mainDeltas = new List<long>();
                var floatingDeltas = new List<long>();

                void Tick(DateTimeOffset utc)
                {
                    clock.UtcNow = utc;
                    main.UpdateTime(ChinaStandardTime.ToBeijingTime(utc));
                    floating.UpdateTime(utc);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    floating.UpdateLayout();
                }

                TextBlock MainText(string id) => main.CountdownRemainingTextsForValidation[id];
                Grid? MainSurface(string id) => (MainText(id).Tag as MainCountdownBlocksHost)?.Surface;
                TextBlock[] MainValues(params string[] ids) => ids
                    .SelectMany(id => RemainingBlockPairs(MainSurface(id)).Select(item => item.Value))
                    .ToArray();
                Grid? Cluster(string name) => FloatingRemainingCluster(floating, name);
                Grid?[] Clusters() => [Cluster(nearName), Cluster(farName)];
                TextBlock[] FloatingValues(params string[] names) => names
                    .SelectMany(name => RemainingBlockPairs(Cluster(name)).Select(item => item.Value))
                    .ToArray();
                (long Main, long Floating) Builds() => (
                    main.CountdownRemainingBlocksBuildCountForValidation,
                    floating.FloatingRemainingBlocksBuildCountForValidation);
                (long Main, long Floating) RowGenerations() => (
                    main.CountdownRowRebuildGenerationForValidation,
                    floating.FloatingRowsRebuildGenerationForValidation);
                (long Main, long Floating) BuildDelta((long Main, long Floating) before)
                {
                    var after = Builds();
                    mainDeltas.Add(after.Main - before.Main);
                    floatingDeltas.Add(after.Floating - before.Floating);
                    return (after.Main - before.Main, after.Floating - before.Floating);
                }

                // 悬浮窗行根（卡片 Border 里的 Grid）的可访问名称须带当前剩余时间；每次检查记下实际名称供诊断。
                var floatingRowNames = new List<string>();
                bool FloatingRowsNamed(params string[] expected)
                {
                    var names = floating.FloatingRows.Children.OfType<Border>()
                        .Select(card => card.Child is Grid root ? AutomationProperties.GetName(root) : "")
                        .ToArray();
                    floatingRowNames.Add(string.Join(" | ", names));
                    return names.SequenceEqual(expected, StringComparer.Ordinal);
                }

                Tick(t0);
                Tick(t0.AddSeconds(1));

                // 1) 分钟精度：连续 9 拍文本不变，整拍跳过。
                var builds = Builds();
                var rows = RowGenerations();
                var floatingPasses = floating.FloatingRowsRenderPassCountForValidation;
                var mainValues = MainValues(nearId, farId);
                var clusters = Clusters();
                var floatingValues = FloatingValues(nearName, farName);
                for (var second = 2; second <= 10; second++)
                {
                    Tick(t0.AddSeconds(second));
                }

                var delta = BuildDelta(builds);
                var mainSkipsUnchangedTicks =
                    mainValues.Length == 6 &&
                    mainValues.SequenceEqual(MainValues(nearId, farId)) &&
                    delta.Main == 0 &&
                    RowGenerations().Main == rows.Main &&
                    MainText(nearId).Text == "1 天 00 小时 00 分钟" &&
                    MainText(farId).Text == "100 天 00 小时 00 分钟" &&
                    nowText.Text == "当前北京时间：2038 年 7 月 8 日 09:00:15";
                var floatingSkipsUnchangedTicks =
                    floatingValues.Length == 6 &&
                    clusters.All(cluster => cluster is not null) &&
                    clusters.SequenceEqual(Clusters()) &&
                    floatingValues.SequenceEqual(FloatingValues(nearName, farName)) &&
                    delta.Floating == 0 &&
                    RowGenerations().Floating == rows.Floating &&
                    floating.FloatingRowsRenderPassCountForValidation >= floatingPasses + 9;

                // 2) 秒精度：秒位变化就地改数值文字与可访问名称，分格不重建。
                clock.UtcNow = t0.AddSeconds(11);
                MustApply(workspace, new SavePreferencesChange(
                    "Second", preferences.MonthlySalary, preferences.WorkStart, preferences.WorkEnd));
                Tick(t0.AddSeconds(11));
                builds = Builds();
                rows = RowGenerations();
                mainValues = MainValues(nearId, farId);
                clusters = Clusters();
                floatingValues = FloatingValues(nearName, farName);
                Tick(t0.AddSeconds(11.25));
                delta = BuildDelta(builds);
                const string nearSecondsText = "1 天 00 小时 00 分钟 28 秒";
                const string farSecondsText = "100 天 00 小时 00 分钟 18 秒";
                var mainSecondsUpdateInPlace =
                    mainValues.Length == 8 &&
                    mainValues.SequenceEqual(MainValues(nearId, farId)) &&
                    mainValues[3].Text == "28" &&
                    mainValues[7].Text == "18" &&
                    RemainingTextNamed(MainText(nearId), nearSecondsText) &&
                    RemainingTextNamed(MainText(farId), farSecondsText) &&
                    delta.Main == 0 &&
                    RowGenerations().Main == rows.Main;
                var floatingSecondsUpdateInPlace =
                    floatingValues.Length == 8 &&
                    clusters.All(cluster => cluster is not null) &&
                    clusters.SequenceEqual(Clusters()) &&
                    floatingValues.SequenceEqual(FloatingValues(nearName, farName)) &&
                    floatingValues[3].Text == "28" &&
                    floatingValues[7].Text == "18" &&
                    AutomationProperties.GetName(clusters[0]!) == nearSecondsText &&
                    AutomationProperties.GetName(clusters[1]!) == farSecondsText &&
                    delta.Floating == 0 &&
                    RowGenerations().Floating == rows.Floating;
                var floatingRowNamesAfterInPlace = FloatingRowsNamed(
                    "主要倒计时 分格按需近期，1 天 00 小时 00 分钟 28 秒",
                    "倒计时 分格按需远期，100 天 00 小时 00 分钟 18 秒");

                // 就地更新后的分格须与按新文本重新排版的结果一致（等宽数字无宽度漂移），且数值不裁切。
                var mainInPlaceGeometryMatchesFreshFit =
                    RemainingBlocksMatchFreshMainFit(main, MainText(nearId), nearSecondsText, isPrimary: true) &&
                    RemainingBlocksMatchFreshMainFit(main, MainText(farId), farSecondsText, isPrimary: false) &&
                    RemainingValuesFitWithoutClipping(MainSurface(nearId)) &&
                    RemainingValuesFitWithoutClipping(MainSurface(farId));
                var floatingInPlaceGeometryMatchesFreshFit =
                    RemainingBlocksMatchFreshFloatingFit(floating, Cluster(nearName), nearSecondsText, isPrimary: true) &&
                    RemainingBlocksMatchFreshFloatingFit(floating, Cluster(farName), farSecondsText, isPrimary: false) &&
                    RemainingValuesFitWithoutClipping(Cluster(nearName)) &&
                    RemainingValuesFitWithoutClipping(Cluster(farName));

                // 3) 天数位数变化（100 → 99 天）：远期行重建，近期行仍就地。
                Tick(t0.AddSeconds(30));
                builds = Builds();
                rows = RowGenerations();
                var mainFarBefore = MainValues(farId);
                var floatingFarBefore = Cluster(farName);
                var floatingFarValuesBefore = FloatingValues(farName);
                Tick(t0.AddSeconds(30.25));
                delta = BuildDelta(builds);
                const string farRolloverText = "99 天 23 小时 59 分钟 59 秒";
                const string nearAtFarRolloverText = "1 天 00 小时 00 分钟 09 秒";
                var mainFarAfter = MainValues(farId);
                var mainDayDigitsChangeRebuilds =
                    delta.Main == 1 &&
                    mainFarBefore.Length == 4 &&
                    mainFarAfter.Length == 4 &&
                    !mainFarAfter.Intersect(mainFarBefore).Any() &&
                    mainFarAfter[0].Text == "99" &&
                    RemainingTextNamed(MainText(farId), farRolloverText) &&
                    RemainingTextNamed(MainText(nearId), nearAtFarRolloverText) &&
                    MainRemainingBlocksMeetFontFloor(MainText(farId)) &&
                    RowGenerations().Main == rows.Main;
                var floatingFarAfter = Cluster(farName);
                var floatingFarValuesAfter = FloatingValues(farName);
                var floatingDayDigitsChangeRebuilds =
                    delta.Floating == 1 &&
                    floatingFarBefore is not null &&
                    floatingFarAfter is not null &&
                    !ReferenceEquals(floatingFarBefore, floatingFarAfter) &&
                    floatingFarValuesAfter.Length == 4 &&
                    !floatingFarValuesAfter.Intersect(floatingFarValuesBefore).Any() &&
                    floatingFarValuesAfter[0].Text == "99" &&
                    AutomationProperties.GetName(floatingFarAfter) == farRolloverText &&
                    Cluster(nearName) is { } nearAtFarRollover &&
                    AutomationProperties.GetName(nearAtFarRollover) == nearAtFarRolloverText &&
                    FloatingRemainingBlocksMeetFontFloor(floatingFarAfter) &&
                    RowGenerations().Floating == rows.Floating;
                var floatingRowNamesAfterDayDigitsRebuild = FloatingRowsNamed(
                    "主要倒计时 分格按需近期，1 天 00 小时 00 分钟 09 秒",
                    "倒计时 分格按需远期，99 天 23 小时 59 分钟 59 秒");

                // 4) #229：近期行 1 → 0 天，悬浮窗省略「天」分格须重建；管理窗口不省略「天」分格，
                // 这一拍两行都就地更新（计数只记入诊断）。
                Tick(t0.AddSeconds(40));
                builds = Builds();
                rows = RowGenerations();
                var floatingNearBefore = Cluster(nearName);
                Tick(t0.AddSeconds(40.25));
                delta = BuildDelta(builds);
                const string nearZeroDayText = "0 天 23 小时 59 分钟 59 秒";
                const string farAtZeroDayText = "99 天 23 小时 59 分钟 49 秒";
                var floatingNearAfter = Cluster(nearName);
                var floatingDayBlockOmissionRebuilds =
                    delta.Floating == 1 &&
                    floatingNearBefore is not null &&
                    floatingNearAfter is not null &&
                    !ReferenceEquals(floatingNearBefore, floatingNearAfter) &&
                    RemainingBlockPairs(floatingNearAfter)
                        .Select(item => item.Value.Text + item.Unit.Text)
                        .SequenceEqual(["23时", "59分", "59秒"]) &&
                    AutomationProperties.GetName(floatingNearAfter) == nearZeroDayText &&
                    Cluster(farName) is { } farAtZeroDay &&
                    AutomationProperties.GetName(farAtZeroDay) == farAtZeroDayText &&
                    FloatingRemainingBlocksMeetFontFloor(floatingNearAfter) &&
                    RowGenerations().Floating == rows.Floating;
                var floatingRowNamesAfterDayBlockOmission = FloatingRowsNamed(
                    "主要倒计时 分格按需近期，0 天 23 小时 59 分钟 59 秒",
                    "倒计时 分格按需远期，99 天 23 小时 59 分钟 49 秒");

                // 5) DPI 变化（沿用 #219 的 WM_DPICHANGED 注入）：同文本的下一拍按新 DPI 重建，再下一拍跳过。
                bool InjectDpi(Window window)
                {
                    if (previousDpiContext == IntPtr.Zero) return false;
                    var injection = MonitorGeometryValidation.InjectDpiChangeForValidation(window, 1.5);
                    if (injection is null) return false;
                    Console.Error.WriteLine(
                        $"REMAINING_BLOCKS_DPI_PROBE: window={window.GetType().Name}; before={injection.BeforePixelsPerInch}; after={injection.AfterPixelsPerInch}; target={injection.Target}");
                    return injection.Reached;
                }

                var dpiScaleBefore = VisualTreeHelper.GetDpi(floating).PixelsPerDip;
                var mainDpiInjected = InjectDpi(main);
                var floatingDpiInjected = InjectDpi(floating);
                var dpiScaleAfter = VisualTreeHelper.GetDpi(floating).PixelsPerDip;
                builds = Builds();
                rows = RowGenerations();
                mainValues = MainValues(nearId, farId);
                clusters = Clusters();
                Tick(t0.AddSeconds(40.5));
                delta = BuildDelta(builds);
                var mainValuesAfterDpi = MainValues(nearId, farId);
                var clustersAfterDpi = Clusters();
                var mainRebuiltForDpi =
                    delta.Main == 2 &&
                    mainValuesAfterDpi.Length == 8 &&
                    !mainValuesAfterDpi.Intersect(mainValues).Any() &&
                    RemainingTextNamed(MainText(nearId), nearZeroDayText) &&
                    RemainingTextNamed(MainText(farId), farAtZeroDayText) &&
                    MainRemainingBlocksMeetFontFloor(MainText(nearId)) &&
                    MainRemainingBlocksMeetFontFloor(MainText(farId));
                var floatingRebuiltForDpi =
                    delta.Floating == 2 &&
                    clustersAfterDpi.All(cluster => cluster is not null) &&
                    !clustersAfterDpi.Intersect(clusters).Any() &&
                    AutomationProperties.GetName(clustersAfterDpi[0]!) == nearZeroDayText &&
                    AutomationProperties.GetName(clustersAfterDpi[1]!) == farAtZeroDayText &&
                    clustersAfterDpi.All(FloatingRemainingBlocksMeetFontFloor);
                builds = Builds();
                Tick(t0.AddSeconds(40.75));
                delta = BuildDelta(builds);
                var mainDpiChangeRebuilds =
                    mainDpiInjected &&
                    mainRebuiltForDpi &&
                    delta.Main == 0 &&
                    mainValuesAfterDpi.SequenceEqual(MainValues(nearId, farId)) &&
                    RowGenerations().Main == rows.Main;
                var floatingDpiChangeRebuilds =
                    floatingDpiInjected &&
                    floatingRebuiltForDpi &&
                    delta.Floating == 0 &&
                    clustersAfterDpi.SequenceEqual(Clusters()) &&
                    RowGenerations().Floating == rows.Floating;

                return new RemainingBlocksOnDemandProbe(
                    mainSkipsUnchangedTicks,
                    mainSecondsUpdateInPlace,
                    mainInPlaceGeometryMatchesFreshFit,
                    mainDayDigitsChangeRebuilds,
                    mainDpiChangeRebuilds,
                    floatingSkipsUnchangedTicks,
                    floatingSecondsUpdateInPlace,
                    floatingInPlaceGeometryMatchesFreshFit,
                    floatingDayDigitsChangeRebuilds,
                    floatingDayBlockOmissionRebuilds,
                    floatingDpiChangeRebuilds,
                    floatingRowNamesAfterInPlace &&
                        floatingRowNamesAfterDayDigitsRebuild &&
                        floatingRowNamesAfterDayBlockOmission,
                    floatingRowNames.ToArray(),
                    mainDeltas.ToArray(),
                    floatingDeltas.ToArray(),
                    dpiScaleBefore,
                    dpiScaleAfter);
            }
            finally
            {
                floating.Close();
                main.Close();
            }
        }
        finally
        {
            MonitorGeometryValidation.RestoreThreadDpiContextForValidation(previousDpiContext);
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #240：悬浮窗便签「下次提醒」按可见便签缓存。同一分钟内且无状态变化时每条只算一次；到达缓存的提醒时刻、
    // 整分钟翻转、工作区状态变化与节假日日历状态变化后重算；卡片上的「下次提醒」文字始终正确。
    public static FloatingNextReminderCacheProbe RunFloatingNextReminderCache()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-next-reminder-cache-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        // 不存在的应用根加合成离线下载：日历刷新同步经历「加载中 → 未就绪」两次状态变化，不触网也不写盘。
        var calendarRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-next-reminder-calendar-{Guid.NewGuid():N}");
        try
        {
            var t0 = new DateTimeOffset(2038, 7, 8, 1, 0, 5, TimeSpan.Zero);
            var clock = new AdvancingValidationClock(t0);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var repeatingFirstDue = new DateTimeOffset(2038, 7, 8, 1, 0, 30, TimeSpan.Zero);
            var repeatingSecondDue = repeatingFirstDue.AddMinutes(15);
            var onceDue = new DateTimeOffset(2038, 7, 9, 2, 0, 0, TimeSpan.Zero);
            const string plainText = "下次提醒缓存·无提醒";
            MustApply(workspace, new SaveNoteChange(
                null, "下次提醒缓存·周期", new DateTime(2038, 7, 8), false, Array.Empty<string>(),
                new ReminderRule[]
                {
                    new FixedIntervalReminderRule("next-reminder-cache-repeat", repeatingFirstDue, new ReminderInterval(15))
                }));
            MustApply(workspace, new SaveNoteChange(
                null, "下次提醒缓存·一次", new DateTime(2038, 7, 9), false, Array.Empty<string>(),
                new ReminderRule[] { new AbsoluteOnceReminderRule("next-reminder-cache-once", onceDue) }));
            MustApply(workspace, new SaveNoteChange(
                null, plainText, new DateTime(2038, 7, 10), false, Array.Empty<string>(), Array.Empty<ReminderRule>()));
            using var calendar = new HolidayCalendar(
                calendarRoot,
                static (_, _) => Task.FromException<string>(new HttpRequestException("synthetic offline")));
            var main = new MainWindow(
                workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory);
            var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            PrepareOffscreenValidationWindow(floating);
            var workspaceEvents = 0;
            var calendarEvents = 0;
            workspace.StateChanged += (_, _) => workspaceEvents++;
            calendar.StateChanged += (_, _) => calendarEvents++;
            try
            {
                main.Show();
                floating.Show();
                WindowValidationInput.Pump(floating.Dispatcher);
                var deltas = new List<long>();
                var textChecks = new List<bool>();

                long Measure(Action action)
                {
                    var before = floating.FloatingNextReminderComputationCountForValidation;
                    action();
                    var computed = floating.FloatingNextReminderComputationCountForValidation - before;
                    deltas.Add(computed);
                    return computed;
                }

                void Tick(DateTimeOffset utc)
                {
                    clock.UtcNow = utc;
                    floating.UpdateTime(utc);
                    WindowValidationInput.Pump(floating.Dispatcher);
                    floating.UpdateLayout();
                }

                void CheckTexts(DateTimeOffset repeatingNext) => textChecks.Add(
                    VisualDescendants<TextBlock>(floating.FloatingRows)
                        .Where(text => text.Text.StartsWith("下次提醒：", StringComparison.Ordinal))
                        .Select(text => text.Text)
                        .Order(StringComparer.Ordinal)
                        .SequenceEqual(new[]
                            {
                                $"下次提醒：{DateDisplay.Deadline(repeatingNext)}",
                                $"下次提醒：{DateDisplay.Deadline(onceDue)}"
                            }
                            .Order(StringComparer.Ordinal)));

                // 进入便签页：三条可见便签各算一次，签名与卡片共用同一份结果。
                var entered = Measure(() => floating.ShowWorkspace(WorkspaceKind.Notes));
                CheckTexts(repeatingFirstDue);
                var sameMinute = Measure(() =>
                {
                    for (var second = 1; second <= 19; second += 2)
                    {
                        Tick(t0.AddSeconds(second));
                    }
                });
                CheckTexts(repeatingFirstDue);

                // 同一分钟内到达周期便签缓存的提醒时刻（01:00:30）：只重算这一条，卡片改指下一次（01:15:30）。
                var reached = Measure(() => Tick(repeatingFirstDue));
                CheckTexts(repeatingSecondDue);
                var afterReached = Measure(() => Tick(repeatingFirstDue.AddSeconds(10)));

                // 整分钟翻转：三条各重算一次，同一分钟的后续 tick 不再计算。
                var rollover = Measure(() => Tick(new DateTimeOffset(2038, 7, 8, 1, 1, 0, TimeSpan.Zero)));
                var afterRollover = Measure(() => Tick(new DateTimeOffset(2038, 7, 8, 1, 1, 10, TimeSpan.Zero)));
                CheckTexts(repeatingSecondDue);

                // 工作区状态变化：与便签无关的倒计时新增、便签自身编辑都作废缓存，每次变化三条各算一次。
                clock.UtcNow = new DateTimeOffset(2038, 7, 8, 1, 1, 20, TimeSpan.Zero);
                var eventsBefore = workspaceEvents;
                var unrelated = Measure(() =>
                {
                    MustApply(workspace, new SaveCountdownChange(
                        null, "下次提醒缓存·无关倒计时", clock.UtcNow.AddDays(3),
                        Array.Empty<string>(), Array.Empty<ReminderRule>()));
                    WindowValidationInput.Pump(floating.Dispatcher);
                });
                var unrelatedEvents = workspaceEvents - eventsBefore;
                var afterUnrelated = Measure(() => Tick(new DateTimeOffset(2038, 7, 8, 1, 1, 25, TimeSpan.Zero)));
                var plain = workspace.Snapshot.Settings.Notes.Single(note => note.Text == plainText);
                clock.UtcNow = new DateTimeOffset(2038, 7, 8, 1, 1, 30, TimeSpan.Zero);
                eventsBefore = workspaceEvents;
                var edited = Measure(() =>
                {
                    MustApply(workspace, new SaveNoteChange(
                        plain.Id, plainText + "（已改）", new DateTime(2038, 7, 10), false,
                        Array.Empty<string>(), Array.Empty<ReminderRule>()));
                    WindowValidationInput.Pump(floating.Dispatcher);
                });
                var editedEvents = workspaceEvents - eventsBefore;
                var afterEdited = Measure(() => Tick(new DateTimeOffset(2038, 7, 8, 1, 1, 35, TimeSpan.Zero)));
                CheckTexts(repeatingSecondDue);

                // 节假日日历状态变化：每次变化都作废缓存，之后同一分钟的 tick 不再计算。
                clock.UtcNow = new DateTimeOffset(2038, 7, 8, 1, 1, 40, TimeSpan.Zero);
                var calendarEventsBefore = calendarEvents;
                var calendarChanged = Measure(() =>
                {
                    var refresh = calendar.RefreshAsync(clock.UtcNow, CancellationToken.None);
                    WindowValidationInput.WaitUntil(floating.Dispatcher, () => refresh.IsCompleted, 2000);
                    WindowValidationInput.Pump(floating.Dispatcher);
                });
                var calendarChanges = calendarEvents - calendarEventsBefore;
                var afterCalendar = Measure(() => Tick(new DateTimeOffset(2038, 7, 8, 1, 1, 45, TimeSpan.Zero)));
                CheckTexts(repeatingSecondDue);

                return new FloatingNextReminderCacheProbe(
                    entered == 3 && sameMinute == 0,
                    reached == 1 && afterReached == 0,
                    rollover == 3 && afterRollover == 0,
                    unrelatedEvents > 0 && unrelated == 3 * unrelatedEvents && afterUnrelated == 0 &&
                        editedEvents > 0 && edited == 3 * editedEvents && afterEdited == 0,
                    calendarChanges > 0 && calendarChanged == 3 * calendarChanges && afterCalendar == 0,
                    textChecks.Count == 6 && textChecks.All(passed => passed),
                    deltas.ToArray());
            }
            finally
            {
                floating.Close();
                main.Close();
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // 两窗分格同构：块容器 → 横向 StackPanel → 每段 StackPanel（色块 Border{数值} + 单位）。
    private static IReadOnlyList<(StackPanel Pair, Border Block, TextBlock Value, TextBlock Unit)> RemainingBlockPairs(
        Panel? blocksRoot) =>
        blocksRoot is null
            ? []
            : blocksRoot.Children.OfType<StackPanel>()
                .SelectMany(panel => panel.Children.OfType<StackPanel>())
                .Where(pair => pair.Children.Count == 2 &&
                    pair.Children[0] is Border { Child: TextBlock } &&
                    pair.Children[1] is TextBlock)
                .Select(pair => (
                    Pair: pair,
                    Block: (Border)pair.Children[0],
                    Value: (TextBlock)((Border)pair.Children[0]).Child,
                    Unit: (TextBlock)pair.Children[1]))
                .ToArray();

    private static string RemainingBlocksGeometry(Panel? blocksRoot) =>
        string.Join(" | ", RemainingBlockPairs(blocksRoot).Select(item => FormattableString.Invariant(
            $"{item.Value.Text}{item.Unit.Text} w={item.Block.Width} min={item.Block.MinWidth} h={item.Block.Height} pad={item.Block.Padding.Left}/{item.Block.Padding.Right} font={item.Value.FontSize} vpad={item.Value.Padding.Left}/{item.Value.Padding.Right} gap={item.Pair.Margin.Left} unit={item.Unit.FontSize}")));

    private static bool RemainingBlocksMatchFreshMainFit(
        MainWindow main,
        TextBlock remainingText,
        string text,
        bool isPrimary) =>
        remainingText.Tag is MainCountdownBlocksHost host &&
        RemainingBlockPairs(host.Surface).Count > 0 &&
        RemainingBlocksGeometry(host.Surface) ==
            RemainingBlocksGeometry(main.BuildCountdownBlocksForValidation(text, isPrimary));

    private static bool RemainingBlocksMatchFreshFloatingFit(
        FloatingWindow floating,
        Grid? cluster,
        string text,
        bool isPrimary) =>
        cluster is not null &&
        RemainingBlockPairs(cluster).Count > 0 &&
        floating.BuildRemainingBlocksForValidation(text, isPrimary) is Grid fresh &&
        RemainingBlocksGeometry(cluster) == RemainingBlocksGeometry(fresh) &&
        AutomationProperties.GetItemStatus(cluster) == AutomationProperties.GetItemStatus(fresh) &&
        AutomationProperties.GetName(cluster) == AutomationProperties.GetName(fresh);

    // 数值按自身字体、字号与等宽数字设置的自然宽度不得超出实际分到的宽度，且保持单行不省略。
    private static bool RemainingValuesFitWithoutClipping(Panel? blocksRoot)
    {
        var pairs = RemainingBlockPairs(blocksRoot);
        return pairs.Count > 0 && pairs.All(item =>
        {
            var natural = new TextBlock
            {
                Text = item.Value.Text,
                FontFamily = item.Value.FontFamily,
                FontSize = item.Value.FontSize,
                FontWeight = item.Value.FontWeight,
                Padding = item.Value.Padding
            };
            Typography.SetNumeralAlignment(natural, Typography.GetNumeralAlignment(item.Value));
            natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return item.Value.TextWrapping == TextWrapping.NoWrap &&
                   item.Value.TextTrimming == TextTrimming.None &&
                   natural.DesiredSize.Width <= item.Value.ActualWidth + 0.5;
        });
    }

    // 管理窗口分格字号下限（§8）：数值不低于 13，单位字固定在 13 档；承载可访问名称的文本同样不低于 13。
    private static bool MainRemainingBlocksMeetFontFloor(TextBlock remainingText)
    {
        var pairs = RemainingBlockPairs((remainingText.Tag as MainCountdownBlocksHost)?.Surface);
        return pairs.Count > 0 &&
               remainingText.FontSize >= 13 - 0.01 &&
               pairs.All(item => item.Value.FontSize >= 13 - 0.01 && item.Unit.FontSize >= 13 - 0.01);
    }

    // 悬浮窗分格字号下限（§10）：排版自报 KeyTextFontFloorSatisfied，数值不低于 12。
    private static bool FloatingRemainingBlocksMeetFontFloor(Grid? cluster) =>
        cluster is not null &&
        string.Equals(
            AutomationProperties.GetItemStatus(cluster),
            "KeyTextFontFloorSatisfied",
            StringComparison.Ordinal) &&
        RemainingBlockPairs(cluster).All(item => item.Value.FontSize >= 12 - 0.01);

    // 管理窗口分格的可访问名称由同列的剩余时间文本承载（TextBlock 的自动化名称即其文本）。
    private static bool RemainingTextNamed(TextBlock remainingText, string expected) =>
        string.Equals(remainingText.Text, expected, StringComparison.Ordinal) &&
        string.Equals(
            UIElementAutomationPeer.CreatePeerForElement(remainingText)?.GetName(),
            expected,
            StringComparison.Ordinal);

    private static Grid? FloatingRemainingCluster(FloatingWindow floating, string rowName) =>
        floating.FloatingRows.Children.OfType<FrameworkElement>()
            .Where(row => VisualDescendants<TextBlock>(row).Any(text => text.Text == rowName))
            .SelectMany(row => VisualDescendants<Grid>(row))
            .FirstOrDefault(grid => string.Equals(
                grid.Tag as string,
                FloatingWindow.RemainingBlocksTag,
                StringComparison.Ordinal));

    public static bool RunCountdownListFirstBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-list-first-{Guid.NewGuid():N}.json");
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(path, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        using var workArea = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(new Rect(0, 0, 2560, 2000), 1, 1));
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            for (var index = 0; index < 8; index++)
                MustApply(workspace, new SaveCountdownChange(null, $"列表优先 {index}",
                    clock.UtcNow.AddDays(index + 1), Array.Empty<string>(), Array.Empty<ReminderRule>()));
            PrepareOffscreenValidationWindow(main);
            using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            main.UpdateLayout();
            var list = (ScrollViewer)main.FindName("CountdownListRegion");
            var rows = (Panel)main.FindName("CountdownRows");
            Must(!((FrameworkElement)main.FindName("CountdownHour")).IsVisible &&
                 ((Button)main.FindName("SaveCountdownButton")).IsVisible &&
                 ((TextBox)main.FindName("CountdownName")).IsVisible &&
                 ((DatePicker)main.FindName("CountdownDate")).IsVisible &&
                 !((FrameworkElement)main.FindName("MonthlySalary")).IsVisible,
                "#221：默认新增必须收起，名称/日期/添加可达，偏好不占据列表页面。");
            var third = (FrameworkElement)rows.Children[2];
            var thirdBottom = third.TranslatePoint(new Point(0, third.ActualHeight), list).Y;
            Must(thirdBottom <= list.ViewportHeight + 1,
                $"#221：首选尺寸至少三行完整可见；thirdBottom={thirdBottom}; viewport={list.ViewportHeight}。");
            main.Height = 900;
            WindowValidationInput.Pump(main.Dispatcher);
            var shortHeight = list.ViewportHeight;
            var shortRows = FullyVisibleRows();
            main.Height = 1200;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(list.ViewportHeight > shortHeight + 100 && FullyVisibleRows() > shortRows,
                $"#221：窗口加高时列表须显示更多完整行；before={shortHeight}/{shortRows}; after={list.ViewportHeight}/{FullyVisibleRows()}。");
            return true;

            int FullyVisibleRows() => rows.Children.OfType<FrameworkElement>().Count(row =>
                row.TranslatePoint(new Point(), list).Y >= -1 &&
                row.TranslatePoint(new Point(0, row.ActualHeight), list).Y <= list.ViewportHeight + 1);
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainTextShortcutBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-text-shortcuts-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var name = (TextBox)main.FindName("CountdownName");
            name.Text = "名称输入焦点保存";
            ((DatePicker)main.FindName("CountdownDate")).SelectedDate = workspace.Snapshot.BeijingNow.AddDays(30).Date;
            Keyboard.Focus(name);
            Must(WindowValidationInput.RaisePreviewKey(name, Key.S, ModifierKeys.Control),
                "#224：倒计时名称输入焦点下Ctrl+S必须响应。");
            Must(DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Any(item => item.Name == "名称输入焦点保存"),
                "#224：名称输入中的Ctrl+S必须真实保存倒计时。");
            main.ShowWorkspace(WorkspaceKind.Notes);
            var body = (TextBox)main.FindName("NoteText");
            body.Text = "正文快捷添加";
            Keyboard.Focus(body);
            Must(WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control),
                "#224：便签正文Ctrl+Enter必须响应提交。");
            var added = DesktopWorkspace.Load(path).Snapshot.Settings.Notes.SingleOrDefault(note => note.Text == "正文快捷添加");
            Must(added is not null, "#224：Ctrl+Enter必须持久保存便签。");
            main.LoadNoteIntoEditor(added!);
            body.Text = "正文快捷修改";
            Keyboard.Focus(body);
            Must(WindowValidationInput.RaisePreviewKey(body, Key.S, ModifierKeys.Control) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Notes.Single().Text == "正文快捷修改",
                "#224：便签页Ctrl+S必须保存当前编辑，不能重复添加。");
            body.Text = "首行";
            body.CaretIndex = body.Text.Length;
            Keyboard.Focus(body);
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.Enter),
                "#224：便签正文普通Enter不得被提交快捷键消费。");
            WindowValidationInput.RaiseKey(body, Key.Enter);
            Must(body.Text.Contains('\n') && workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：便签正文普通Enter必须真实换行且不能保存。");
            var popup = (Popup)main.FindName("FilterPopup");
            popup.IsOpen = true;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.S, ModifierKeys.Control) &&
                 !WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control) &&
                 workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：弹层打开时Ctrl+S和Ctrl+Enter不允许穿透保存草稿。");
            WindowValidationInput.ClosePopupForCleanup(popup);
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            var countdownDate = (DatePicker)main.FindName("CountdownDate");
            countdownDate.IsDropDownOpen = true;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(countdownDate.IsDropDownOpen &&
                 !WindowValidationInput.RaisePreviewKey(countdownDate, Key.D2, ModifierKeys.Control) &&
                 main.CurrentWorkspace == WorkspaceKind.Countdowns,
                "#224：原生日历下拉打开时全局Ctrl快捷键仍被屏蔽。");
            countdownDate.IsDropDownOpen = false;
            ((Button)main.FindName("CountdownEditorExpandButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var hour = (ComboBox)main.FindName("CountdownHour");
            hour.IsDropDownOpen = true;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsDropDownOpen && !WindowValidationInput.RaisePreviewKey(hour, Key.D2, ModifierKeys.Control) &&
                 main.CurrentWorkspace == WorkspaceKind.Countdowns,
                "#224：时间下拉打开时全局Ctrl快捷键仍被屏蔽。");
            hour.IsDropDownOpen = false;
            foreach (var (id, shortcut) in new[]
            {
                ("CountdownTab", "Ctrl+1"), ("NotesTab", "Ctrl+2"), ("MinimizeButton", "Ctrl+M"),
                ("FilterButton", "Ctrl+F"), ("TagManagerButton", "Ctrl+T"),
                ("SaveCountdownButton", "Ctrl+S"), ("SaveNoteButton", "Ctrl+Enter"),
                ("SavePreferencesButton", "Ctrl+Shift+S"), ("CreateTagButton", "Enter")
            })
                Must(((FrameworkElement)main.FindName(id)).ToolTip?.ToString()?.Contains(shortcut, StringComparison.Ordinal) == true,
                    $"#224：{id} ToolTip必须提示{shortcut}。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunSingleLineEnterSubmission()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-enter-form-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var name = (TextBox)main.FindName("CountdownName");
            var date = (DatePicker)main.FindName("CountdownDate");
            name.Text = "名称回车保存";
            date.SelectedDate = workspace.Snapshot.BeijingNow.AddDays(30).Date;
            Keyboard.Focus(name);
            Must(WindowValidationInput.RaisePreviewKey(name, Key.Enter) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Any(item => item.Name == "名称回车保存"),
                "#224：单行倒计时名称Enter必须持久提交所在表单。");
            name.Clear();
            date.SelectedDate = null;
            Keyboard.Focus(name);
            Must(WindowValidationInput.RaisePreviewKey(name, Key.Enter) && name.IsKeyboardFocusWithin &&
                 ((TextBlock)main.FindName("CountdownNameError")).IsVisible && workspace.Snapshot.Settings.Countdowns.Count == 1,
                "#224：Enter校验失败必须保留草稿并聚焦第一个无效字段。");
            var preferences = (Popup)main.FindName("PreferencesPopup");
            preferences.IsOpen = true;
            WindowValidationInput.Pump(main.Dispatcher);
            var salary = (TextBox)main.FindName("MonthlySalary");
            var start = (TextBox)main.FindName("WorkStart");
            var end = (TextBox)main.FindName("WorkEnd");
            salary.Text = "56789"; start.Text = "08:15"; end.Text = "18:45";
            Keyboard.Focus(salary);
            Must(!WindowValidationInput.RaisePreviewKey(salary, Key.D2, ModifierKeys.Control) &&
                 main.CurrentWorkspace == WorkspaceKind.Countdowns,
                "#224：设置弹层文本焦点不允许全局Ctrl快捷键穿透。");
            Must(WindowValidationInput.RaisePreviewKey(salary, Key.Enter) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Preferences.MonthlySalary == 56789m,
                "#224：设置弹层单行字段Enter应提交设置。");
            salary.Text = "坏金额"; start.Text = "坏时间";
            Keyboard.Focus(end);
            Must(WindowValidationInput.RaisePreviewKey(end, Key.Enter) && salary.IsKeyboardFocusWithin &&
                 ((TextBlock)main.FindName("MonthlySalaryError")).IsVisible,
                "#224：工时Enter提交失败必须聚焦首个无效薪资字段。");
            WindowValidationInput.ClosePopupForCleanup(preferences);
            var tags = (Popup)main.FindName("TagManagerPopup");
            ((Button)main.FindName("TagManagerButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var tagName = (TextBox)main.FindName("NewTagInput");
            tagName.Text = "回车新标签";
            Keyboard.Focus(tagName);
            Must(WindowValidationInput.RaisePreviewKey(tagName, Key.Enter) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Tags.Any(tag => tag.Name == "回车新标签"),
                "#224：新标签名Enter必须在标签弹层提交。");
            tagName.Clear();
            Must(WindowValidationInput.RaisePreviewKey(tagName, Key.Enter) && tagName.IsKeyboardFocusWithin &&
                 workspace.Snapshot.Settings.Tags.Count == 1,
                "#224：空标签Enter不得保存且保留名称焦点。");
            WindowValidationInput.ClosePopupForCleanup(tags);
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunFloatingNoteKeyboardSubmission()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-floating-keyboard-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(floating);
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            ((Button)floating.FindName("FloatingAddNoteToggle")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            var body = (TextBox)floating.FindName("FloatingNoteText");
            body.Text = "浮窗正文";
            Keyboard.Focus(body);
            Must(WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Notes.Any(note => note.Text == "浮窗正文"),
                "#224：悬浮便签正文Ctrl+Enter必须持久提交。");
            ((Button)floating.FindName("FloatingAddNoteToggle")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            body.Clear();
            Keyboard.Focus(body);
            Must(WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control) && body.IsKeyboardFocusWithin &&
                 ((TextBlock)floating.FindName("FloatingNoteTextError")).IsVisible && workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：悬浮Ctrl+Enter校验失败必须聚焦正文且不保存。");
            body.Text = "浮窗首行";
            body.CaretIndex = body.Text.Length;
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.Enter),
                "#224：悬浮正文普通Enter不得提交。");
            WindowValidationInput.RaiseKey(body, Key.Enter);
            Must(body.Text.Contains('\n') && workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：悬浮正文Enter必须真实换行。");
            var tags = (TagSelector)floating.FindName("FloatingTags");
            tags.IsPopupOpen = true;
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control) &&
                 workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：悬浮标签弹层打开时不能由Ctrl+Enter保存草稿。");
            WindowValidationInput.ClosePopupForCleanup(tags.Popup);
            Must(((Button)floating.FindName("FloatingAddNote")).ToolTip?.ToString()?.Contains("Ctrl+Enter", StringComparison.Ordinal) == true &&
                 ((Button)floating.FindName("FloatingSearchNotes")).ToolTip?.ToString()?.Contains("Ctrl+F", StringComparison.Ordinal) == true,
                "#224：悬浮添加与搜索按钮必须显示对应快捷键提示。");
            return true;
        }
        finally
        {
            floating.Close();
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunListDeleteKeyboardBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-list-delete-key-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        MustApply(workspace, new SaveCountdownChange(null, "键盘删除倒计时", DateTimeOffset.UtcNow.AddDays(30),
            Array.Empty<string>(), Array.Empty<ReminderRule>()));
        MustApply(workspace, new SaveNoteChange(null, "键盘删除便签", workspace.Snapshot.BeijingNow.Date, false,
            Array.Empty<string>(), Array.Empty<ReminderRule>()));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        void DeleteFrom(Window owner, UIElement source, string expected, bool confirm)
        {
            owner.Activate(); Keyboard.Focus(source);
            var observed = false;
            QueueConfirmationDialogResponse(owner, confirm, dialog =>
            {
                observed = dialog.BodyText.Text.Contains(expected, StringComparison.Ordinal) &&
                    dialog.CancelButton.IsDefault && !dialog.ConfirmButton.IsDefault;
            });
            Must(WindowValidationInput.RaisePreviewKey(source, Key.Delete),
                "#224：列表行焦点Delete必须进入既有删除确认。");
            WindowValidationInput.Pump(owner.Dispatcher);
            Must(observed, "#224：Delete确认必须绑定当前行记录且默认取消。");
        }
        try
        {
            PrepareOffscreenValidationWindow(main); main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var rows = (Panel)main.FindName("CountdownRows");
            var edit = VisualDescendants<Button>(rows).Single(button => Equals(button.Content, "编辑"));
            DeleteFrom(main, edit, "键盘删除倒计时", false);
            Must(workspace.Snapshot.Settings.Countdowns.Count == 1 && edit.IsKeyboardFocusWithin,
                "#224：Delete取消保留倒计时并归还原行焦点。");
            DeleteFrom(main, edit, "键盘删除倒计时", true);
            Must(workspace.Snapshot.Settings.Countdowns.Count == 0 && main.DeleteUndoForValidation is not null,
                "#224：Delete确认仍提供既有撤销。");
            var name = (TextBox)main.FindName("CountdownName");
            name.Text = "输入撤销"; name.CaretIndex = name.Text.Length; name.SelectedText = "保留";
            Keyboard.Focus(name);
            Must(!WindowValidationInput.RaisePreviewKey(name, Key.Z, ModifierKeys.Control) && name.CanUndo &&
                 workspace.Snapshot.Settings.Countdowns.Count == 0,
                "#224：文本Ctrl+Z必须优先保留编辑撤销，不能消耗删除撤销。");
            // 合成Key的modifier只注入应用路由；WPF原生编辑命令经其公开RoutedCommand seam驱动。
            ApplicationCommands.Undo.Execute(null, name);
            Must(!name.Text.EndsWith("保留", StringComparison.Ordinal), "#224：文本Ctrl+Z必须真实撤销输入。");
            Keyboard.Focus((UIElement)main.FindName("CountdownTab"));
            Must(WindowValidationInput.RaisePreviewKey(main, Key.Z, ModifierKeys.Control) &&
                 workspace.Snapshot.Settings.Countdowns.Count == 1, "#224：离开输入框后Ctrl+Z仍能撤销删除。");
            main.ShowWorkspace(WorkspaceKind.Notes);
            rows = (Panel)main.FindName("ActiveNoteRows");
            var check = VisualDescendants<CheckBox>(rows).Single();
            DeleteFrom(main, check, "键盘删除便签", false);
            Must(workspace.Snapshot.Settings.Notes.Count == 1 && !workspace.Snapshot.Settings.Notes.Single().IsCompleted,
                "#224：便签行Delete取消不删除或勾选完成。");
            DeleteFrom(main, check, "键盘删除便签", true);
            Must(workspace.Snapshot.Settings.Notes.Count == 0, "#224：便签行Delete确认应删除目标记录。");
            ((Button)main.FindName("MainStatusUndoButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Must(workspace.Snapshot.Settings.Notes.Count == 1, "#224：便签键盘删除后可撤销。");
            PrepareOffscreenValidationWindow(floating); floating.Show(); floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);
            var floatingCheck = VisualDescendants<CheckBox>((Panel)floating.FindName("FloatingRows")).Single();
            DeleteFrom(floating, floatingCheck, "键盘删除便签", false);
            Must(workspace.Snapshot.Settings.Notes.Count == 1, "#224：悬浮行Delete取消应保留便签。");
            DeleteFrom(floating, floatingCheck, "键盘删除便签", true);
            Must(workspace.Snapshot.Settings.Notes.Count == 0 && floating.DeleteUndoForValidation is not null,
                "#224：悬浮行Delete确认仍提供撤销。");
            ((Button)floating.FindName("FloatingAddNoteToggle")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            var body = (TextBox)floating.FindName("FloatingNoteText");
            body.Text = "浮窗输入"; body.CaretIndex = body.Text.Length; body.SelectedText = "撤销";
            Keyboard.Focus(body);
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.Z, ModifierKeys.Control) &&
                 workspace.Snapshot.Settings.Notes.Count == 0, "#224：悬浮文本撤销不得恢复被删记录。");
            ApplicationCommands.Undo.Execute(null, body);
            Must(!body.Text.EndsWith("撤销", StringComparison.Ordinal), "#224：悬浮文本Ctrl+Z仍真实撤销编辑。");
            Keyboard.Focus((UIElement)floating.FindName("FloatingNotesButton"));
            Must(WindowValidationInput.RaisePreviewKey(floating, Key.Z, ModifierKeys.Control) && workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：悬浮非文本Ctrl+Z仍可恢复删除。");
            return true;
        }
        finally
        {
            floating.Close(); main.Close(); DeleteSettingsFiles(path); DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainNoteEditorSeparation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-note-separation-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var search = (TextBox)main.FindName("NoteSearch");
            var editor = (FrameworkElement)main.FindName("NoteEditorPanel");
            var list = (FrameworkElement)main.FindName("NotesListCard");
            Must(list.IsAncestorOf(search) && !editor.IsAncestorOf(search),
                "#227：搜索便签必须位于状态列表区域，不属于新增编辑表单。");
            var note = new NoteRecord("note-layout-probe", "编辑态便签", "2038-07-08", false,
                Array.Empty<string>(), Array.Empty<ReminderRule>());
            main.LoadNoteIntoEditor(note);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(main.FindName("NoteEditorTitle") is TextBlock { Text: "编辑便签", IsVisible: true },
                "#227：编辑态必须显示编辑便签标题。");
            ((Button)main.FindName("CancelNoteEdit")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(!((FrameworkElement)main.FindName("NoteEditorTitle")).IsVisible,
                "#227：取消编辑后不能继续显示编辑便签标题。");
            var summary = (FrameworkElement)main.FindName("NoteReminderRulesSummary");
            var field = (FrameworkElement)main.FindName("ManageNoteReminderRulesButton");
            Must(Math.Abs(summary.TransformToAncestor(main).Transform(new Point()).X -
                 field.TransformToAncestor(main).Transform(new Point()).X) <= 1,
                "#227：规则摘要应与字段列左对齐。");
            Keyboard.Focus((UIElement)main.FindName("NotesTab"));
            Must(WindowValidationInput.RaisePreviewKey((UIElement)main.FindName("NotesTab"), Key.F, ModifierKeys.Control) &&
                 search.IsKeyboardFocusWithin, "#227：Ctrl+F仍聚焦移动后的搜索便签。");
            Must(main.FindName("NoteGroupingSelector") is null,
                "#227：只有一个选项的按日期分组下拉应移除。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainNoteRowsSimplified()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-note-rows-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        for (var index = 0; index < 5; index++)
        {
            MustApply(workspace, new SaveNoteChange(null, $"交替便签 {index}",
                new DateTime(2038, 7, index < 3 ? 8 : 9), false,
                Array.Empty<string>(), new ReminderRule[] { new AbsoluteOnceReminderRule(
                    $"note-row-rule-{index}", new DateTimeOffset(2038, 7, 10, 1, 0, 0, TimeSpan.Zero)) }));
        }
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var rows = (Panel)main.FindName("ActiveNoteRows");
            void VerifyRows(int count, bool completed = false)
            {
                main.UpdateLayout();
                WindowValidationInput.Pump(main.Dispatcher);
                var bodies = VisualDescendants<TextBlock>(rows).Where(text => text.Uid == "NoteRowBodyText").ToArray();
                Must(bodies.Length == count, "#227：行样式探针必须建立预期非空便签列表。");
                for (var index = 0; index < bodies.Length; index++)
                {
                    var body = bodies[index];
                    var card = VisualDescendants<Border>(rows).Single(border => border.Child is Grid && border.IsAncestorOf(body));
                    var buttons = VisualDescendants<Button>(card).ToArray();
                    Must(buttons.Length == 2 && buttons.Any(button => Equals(button.Content, "编辑")) &&
                         buttons.Any(button => Equals(button.Content, "删除")),
                        "#227：主便签行只保留编辑和删除，不再重复显示更多菜单。");
                    var summaries = VisualDescendants<TextBlock>(card).Where(text => AutomationProperties.GetAutomationId(text) ==
                        MainWindow.NoteCardReminderSummaryAutomationId).ToArray();
                    Must(Math.Abs(body.FontSize - (double)main.FindResource("TypeCaptionFontSize")) <= 0.01 &&
                         summaries.Length == 1 && summaries.All(text => text.IsVisible && text.FontSize <= body.FontSize),
                        "#227/#237：便签正文使用管理窗降一档后的 caption 阶梯，提醒摘要不得大于正文。");
                    var delete = buttons.Single(button => Equals(button.Content, "删除"));
                    Must(delete.Foreground is SolidColorBrush foreground && foreground.Color == (Color)main.FindResource("DangerColor") &&
                         delete.BorderBrush is SolidColorBrush borderBrush && borderBrush.Color.A == 0 &&
                         VisualDescendants<System.Windows.Shapes.Path>(delete).Any(icon => icon.IsVisible),
                        "#227：便签删除应为无红边框的危险色文字图标。");
                    var expected = (SolidColorBrush)main.FindResource(completed || index % 2 == 1 ? "NoteWhiteBrush" : "NoteBrush");
                    Must(card.Background is SolidColorBrush actual && actual.Color == expected.Color,
                        "#227：便签底色必须按跨日期组的可见位置黄白交替，已完成全白。");
                }
            }
            VerifyRows(5);
            ((TextBox)main.FindName("NoteSearch")).Text = "交替便签 4";
            VerifyRows(1);
            ((TextBox)main.FindName("NoteSearch")).Clear();
            VerifyRows(5);
            var removed = workspace.Snapshot.Settings.Notes.First();
            MustApply(workspace, new DeleteNoteChange(removed.Id));
            VerifyRows(4);
            foreach (var note in workspace.Snapshot.Settings.Notes.ToArray())
                MustApply(workspace, new SaveNoteChange(note.Id, note.Text, DateTime.Parse(note.Date!), true,
                    note.TagIds, note.ReminderRules));
            ((ToggleButton)main.FindName("CompletedNotesSegment")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            rows = (Panel)main.FindName("CompletedNoteRows");
            VerifyRows(4, completed: true);
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainRedundancyCleanup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-redundancy-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var announcements = new List<TextBlock>();
        void Remember(TextBlock block) => announcements.Add(block);
        UiAccessibility.Announced += Remember;
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            FlushNarrator(main.Dispatcher);
            var count = (TextBlock)main.FindName("RecordCountText");
            Must(ReferenceEquals(count.Parent, main.FindName("MainCommandBarZone")) && count.IsVisible &&
                 count.ActualWidth > 0 && count.ActualHeight > 0,
                "#222：数量应为命令栏中的无框纯文字，不再嵌入卡片或图标容器。");
            Must(main.FindName("DayThemeBadge") is null && main.FindName("MainThemeCaption") is null &&
                 !VisualDescendants<TextBlock>(main).Any(text => text.IsVisible &&
                     (text.Text == "日间" || text.Text.Contains("当前主题：", StringComparison.Ordinal))),
                "#222：页头日间和页脚当前主题文案应删除。");
            var status = (TextBlock)main.FindName("StatusText");
            foreach (var kind in new[] { WorkspaceKind.Notes, WorkspaceKind.Countdowns })
            {
                announcements.Clear();
                main.ShowWorkspace(kind);
                FlushNarrator(main.Dispatcher);
                Must(!status.Text.Contains("当前工作区", StringComparison.Ordinal) &&
                     !announcements.Any(text => text.Text.Contains("当前工作区", StringComparison.Ordinal)),
                    "#222：切页不得显示或播报冗余的当前工作区文案。");
                var liveCounts = VisualDescendants<TextBlock>(main).Where(text => text.IsVisible &&
                    AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite &&
                    System.Text.RegularExpressions.Regex.IsMatch(text.Text, @"^\d+ 个(?:倒计时|待办)$")).ToArray();
                Must(liveCounts.Length == 1 && ReferenceEquals(liveCounts[0], count),
                    "#222：数量纯文字仍是唯一数量直播区。");
            }
            announcements.Clear();
            MustApply(workspace, new SaveCountdownChange(null, "数量变更", DateTimeOffset.UtcNow.AddDays(3),
                Array.Empty<string>(), Array.Empty<ReminderRule>()));
            FlushNarrator(main.Dispatcher);
            Must(count.Text == "1 个倒计时" && announcements.Count(block => ReferenceEquals(block, count)) == 1,
                "#222：数量改变仍准确播报一次。");
            announcements.Clear();
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(workspace.Snapshot.UtcNow).AddMinutes(1));
            FlushNarrator(main.Dispatcher);
            Must(!announcements.Contains(count), "#222：数量未变的分钟刷新不得重复播报。");
            return true;
        }
        finally
        {
            UiAccessibility.Announced -= Remember;
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainTopmostStateSynchronization()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-topmost-sync-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var check = (CheckBox)main.FindName("TopmostCheck");
            Must(main.Topmost == (check.IsChecked == true), "#222：首次打开时窗口置顶必须与勾选状态一致。");
            var peer = new CheckBoxAutomationPeer(check);
            var toggle = (System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
            var initial = check.IsChecked;
            toggle.Toggle();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(check.IsChecked != initial && main.Topmost == (check.IsChecked == true),
                "#222：切换置顶勾选必须同步窗口 Topmost。");
            toggle.Toggle();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(check.IsChecked == initial && main.Topmost == (check.IsChecked == true),
                "#222：再次切换必须恢复初始勾选与窗口 Topmost。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunCountdownPreferencesPopupBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-preferences-popup-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            main.Activate();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(main.FindName("PreferencesButton") is Button,
                "#221：命令栏应提供设置齿轮入口。");
            var trigger = (Button)main.FindName("PreferencesButton");
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var popup = (Popup)main.FindName("PreferencesPopup");
            var precision = (ComboBox)main.FindName("DisplayPrecision");
            var salary = (TextBox)main.FindName("MonthlySalary");
            var save = (Button)main.FindName("SavePreferencesButton");
            Must(popup.IsOpen && precision.IsKeyboardFocusWithin,
                "#221：设置弹层打开时应聚焦显示精度。");
            if (!WindowValidationInput.RaisePreviewKey(precision, Key.F4, ModifierKeys.None))
                WindowValidationInput.RaiseKey(precision, Key.F4);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(precision.IsDropDownOpen, "#221：设置中的显示精度可用 F4 打开嵌套下拉。");
            if (!WindowValidationInput.RaisePreviewKey(precision, Key.Escape, ModifierKeys.None))
                WindowValidationInput.RaiseKey(precision, Key.Escape);
            var nestedPopup = (Popup)precision.Template.FindName("PART_Popup", precision);
            Must(WindowValidationInput.WaitForPopupClosed(nestedPopup) &&
                 !precision.IsDropDownOpen && popup.IsOpen && precision.IsVisible && precision.IsKeyboardFocusWithin,
                "#221：嵌套下拉的首次 Esc 只关闭下拉，设置层仍可见且保留显示精度焦点。");
            save.Focus();
            save.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(precision.IsKeyboardFocusWithin, "#221：设置弹层 Tab 必须层内循环。");
            salary.Text = "12345";
            WindowValidationInput.RaisePreviewKey(salary, Key.Escape, ModifierKeys.None);
            Must(WindowValidationInput.WaitUntil(main.Dispatcher, () => !popup.IsOpen && trigger.IsKeyboardFocusWithin),
                "#221：设置弹层 Esc 关闭并归还齿轮焦点。");
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(salary.Text == "12345", "#221：设置弹层收起后保留未保存草稿。");
            salary.Text = "invalid";
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(((TextBlock)main.FindName("MonthlySalaryError")).IsVisible && salary.IsKeyboardFocusWithin &&
                 ((TextBlock)main.FindName("StatusText")).Text == "月薪格式无效。",
                "#221：设置字段错误必须在弹层中可见且镜像状态栏。");
            salary.Text = "12345";
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(((TextBlock)main.FindName("StatusText")).Text == "设置已保存" &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Preferences.MonthlySalary == 12345,
                "#221：设置保存应持久化并显示设置已保存。");
            WindowValidationInput.ClosePopupForCleanup(popup);
            main.Hide();
            var lifecycle = new ManagerWindowLifecycle(
                () => new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory),
                PrepareOffscreenValidationWindow);
            try
            {
                foreach (var host in new IManagerWorkspaceHost[] { main, lifecycle })
                {
                    var floating = new FloatingWindow(workspace, calendar, host, AppContext.BaseDirectory);
                    try
                    {
                        PrepareOffscreenValidationWindow(floating);
                        floating.Show();
                        floating.ShowWorkspace(WorkspaceKind.Notes);
                        WindowValidationInput.Pump(main.Dispatcher);
                        var settingsButton = (Button)floating.FindName("FloatingOpenSettings");
                        Must(settingsButton.IsVisible, "#221：悬浮便签页设置入口应可见。");
                        settingsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        WindowValidationInput.Pump(main.Dispatcher);
                        var revealed = host is MainWindow direct ? direct : lifecycle.Window!;
                        Must(WindowValidationInput.WaitUntil(revealed.Dispatcher, () => revealed.IsVisible && revealed.CurrentWorkspace == WorkspaceKind.Notes &&
                             ((Popup)revealed.FindName("PreferencesPopup")).IsOpen &&
                             ((ComboBox)revealed.FindName("DisplayPrecision")).IsKeyboardFocusWithin),
                            $"#221：悬浮设置应支持直接与懒加载宿主，并直接打开管理窗设置弹层；host={host.GetType().Name}; " +
                            $"visible={revealed.IsVisible}; workspace={revealed.CurrentWorkspace}; " +
                            $"popup={((Popup)revealed.FindName("PreferencesPopup")).IsOpen}; " +
                            $"focus={Keyboard.FocusedElement}; active={revealed.IsActive}; " +
                            $"precisionVisible={((ComboBox)revealed.FindName("DisplayPrecision")).IsVisible}。");
                        WindowValidationInput.ClosePopupForCleanup((Popup)revealed.FindName("PreferencesPopup"));
                        revealed.Hide();
                    }
                    finally { floating.Close(); }
                }
            }
            finally { lifecycle.Window?.Close(); }
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunCountdownEditorExpansionBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-editor-expansion-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        MustApply(workspace, new SaveCountdownChange(null, "编辑展开", DateTimeOffset.UtcNow.AddDays(3),
            Array.Empty<string>(), Array.Empty<ReminderRule>()));
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            main.Activate();
            WindowValidationInput.Pump(main.Dispatcher);
            var name = (TextBox)main.FindName("CountdownName");
            var hour = (ComboBox)main.FindName("CountdownHour");
            var toggle = (Button)main.FindName("CountdownEditorExpandButton");
            var rows = (Panel)main.FindName("CountdownRows");
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsVisible, "#221：展开新增应显示时分秒、标签及提醒规则。");
            name.Text = "未保存草稿";
            hour.SelectedIndex = 7;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            main.ShowWorkspace(WorkspaceKind.Notes);
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(name.Text == "未保存草稿" && hour.SelectedIndex == 7 && !hour.IsVisible,
                "#221：收起和切页须保留新增草稿。");
            var edit = VisualDescendants<Button>(rows).First(button => Equals(button.Content, "编辑"));
            edit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsVisible && ((TextBlock)main.FindName("CountdownEditorTitle")).Text == "编辑倒计时" &&
                 ((Button)main.FindName("CancelCountdownEdit")).IsVisible,
                "#221：编辑现有倒计时应自动展开并显示编辑标题和取消。");
            ((Button)main.FindName("CancelCountdownEdit")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(!hour.IsVisible && name.Text.Length == 0 && main.EditingCountdownId is null,
                "#221：取消编辑返回折叠新增，不写入或删除记录。");
            Must(DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Single().Name == "编辑展开",
                "#221：取消编辑不得改变持久记录。");
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            name.Text = "无效时间草稿";
            hour.SelectedIndex = -1;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ((Button)main.FindName("SaveCountdownButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsVisible && hour.IsKeyboardFocusWithin && name.Text == "无效时间草稿" &&
                 ((TextBlock)main.FindName("CountdownMessage")).IsVisible &&
                 !string.IsNullOrEmpty(((TextBlock)main.FindName("CountdownMessage")).Text) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Count == 1,
                "#221：收起后的非法时间草稿提交必须重新展开并聚焦无效时间，保留草稿且不写入。");
            hour.SelectedIndex = 7;
            ((DatePicker)main.FindName("CountdownDate")).SelectedDate = DateTime.Today.AddDays(-1);
            ((RadioButton)main.FindName("CountdownReminderTimeRadio")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var reminderErrorFocused = false;
            main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var editor = main.OwnedWindows.OfType<ReminderRulesEditorWindow>().SingleOrDefault();
                if (editor is null) return;
                try
                {
                    var firstDue = VisualDescendants<DatePicker>(editor).Single(control =>
                        AutomationProperties.GetAutomationId(control) == "ReminderRuleFirstDueDate");
                    var error = VisualDescendants<TextBlock>(editor).Single(control =>
                        AutomationProperties.GetAutomationId(control) == "ReminderRuleFirstDueError");
                    reminderErrorFocused = editor.IsVisible && firstDue.IsKeyboardFocusWithin &&
                        error.IsVisible && !string.IsNullOrEmpty(error.Text);
                }
                finally { editor.Close(); }
            }));
            ((Button)main.FindName("SaveCountdownButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsVisible && reminderErrorFocused && name.Text == "无效时间草稿" &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Count == 1,
                "#221：收起后的非法提醒提交必须展开提醒区域并在规则窗口聚焦错误，取消后保留草稿。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    // #247：倒计时行关键状态夹具。七行覆盖普通主项、30 字名称、五枚标签（含超长标签）、999 天、北京时区截止行、
    // 百万天与已到期，首选尺寸下必出纵向滚动条；「长期」筛选只留百万天主项与到期次项（无滚动条），「收尾」筛选
    // 只留到期主项。行几何探针与 validate-only 实拍共用这一份夹具，另带一条便签供次级动作对照。
    internal sealed record CountdownRowStateFixture(string LongestTagId, string ExpiredTagId);

    internal static readonly DateTimeOffset CountdownRowFixtureUtc = new(2032, 5, 18, 2, 15, 30, TimeSpan.Zero);

    private const string CountdownRowFixtureLongName = "极长倒计时名称验证主阅读轴在滚动条出现时仍完整显示不挤压动作";

    private static readonly string[] CountdownRowFixtureOrder =
        ["季度复盘", CountdownRowFixtureLongName, "多标签行", "九百九十九天", "截止行验证", "百万天", "已到期行"];

    internal static CountdownRowStateFixture SeedCountdownRowStates(DesktopWorkspace workspace)
    {
        var nowUtc = workspace.Snapshot.UtcNow;
        string Tag(string name, string? color) =>
            MustApply(workspace, new CreateTagChange(name, color)).CreatedTag?.Id ??
            throw new InvalidOperationException($"#247：未取得夹具标签「{name}」。");
        void Countdown(string name, DateTimeOffset targetUtc, params string[] tagIds) =>
            MustApply(workspace, new SaveCountdownChange(null, name, targetUtc, tagIds, Array.Empty<ReminderRule>()));

        var work = Tag("工作", "#F4B6A6");
        var life = Tag("生活", "#A8D5BA");
        var longTag = Tag("超长标签名称用于验证芯片先折行再省略", "#A8C7E8");
        var focus = Tag("专注", null);
        var family = Tag("家庭聚会安排", null);
        var longest = Tag("长期", null);
        var closing = Tag("收尾", null);
        Countdown("季度复盘", nowUtc.AddDays(2).AddHours(3).AddMinutes(4).AddSeconds(5), work);
        Countdown(CountdownRowFixtureLongName, nowUtc.AddDays(18).AddHours(7), work, life);
        Countdown("多标签行", nowUtc.AddDays(30).AddHours(6), work, life, longTag, focus, family);
        Countdown("九百九十九天", nowUtc.AddDays(999).AddHours(13).AddMinutes(18).AddSeconds(59));
        Countdown("截止行验证", new DateTimeOffset(2038, 7, 8, 1, 30, 0, TimeSpan.Zero));
        Countdown("百万天", nowUtc.AddDays(2_000_000), longest);
        Countdown("已到期行", nowUtc.AddHours(-5), life, longest, closing);
        MustApply(workspace, new SaveNoteChange(null, "动作对照便签", workspace.Snapshot.BeijingNow.Date, false,
            Array.Empty<string>(), Array.Empty<ReminderRule>()));
        return new CountdownRowStateFixture(longest, closing);
    }

    // #221/#247：截止行与危险色删除保持；名称与剩余分格同一主阅读轴，截止与标签成辅助带，
    // 七行滚动条、百万天主项与到期主项三种排布下文本、分格、芯片与动作都不裁切、不重叠、不挤压命中区，
    // 编辑与删除和便签行同一套次级动作几何与状态语言。违反项汇总后一次报出。
    public static bool RunCountdownRowDeadlineAndSecondaryDeleteBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-row-deadline-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(
            path,
            new SettingsStore(clock: new AdvancingValidationClock(CountdownRowFixtureUtc)));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var fixture = SeedCountdownRowStates(workspace);
        Must(CountdownRowFixtureLongName.Length == 30, "#247：极长名称夹具须顶到 30 字上限。");
        // 与列表优先探针同一口径固定首选 864×1000：七行必出纵向滚动条，两行以内不出。
        using var workArea = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(new Rect(0, 0, 2560, 2000), 1, 1));
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var rows = (Panel)main.FindName("CountdownRows");
            var list = VisualAncestor<ScrollViewer>(rows);
            var viewport = list is null ? null : VisualDescendants<ScrollContentPresenter>(list).FirstOrDefault();
            Must(list is not null && viewport is not null, "#247：倒计时列表须在滚动区域内。");
            var violations = new List<string>();
            string[] countdownActionSignatures = [];
            void VerifyPass(string pass, string[] expectedOrder, bool expectScrollBar)
            {
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                var snapshot = workspace.Snapshot;
                var cards = rows.Children.OfType<Border>().ToArray();
                var titles = cards
                    .Select(card => VisualDescendants<TextBlock>(card)
                        .FirstOrDefault(text => text.Uid == "CountdownRowTitleText")?.Text ?? "?")
                    .ToArray();
                if (!titles.SequenceEqual(expectedOrder, StringComparer.Ordinal))
                {
                    violations.Add($"{pass}：行序须未到期按截止升序、已到期在后，实为 {string.Join("/", titles)}");
                }

                if ((list!.ComputedVerticalScrollBarVisibility == Visibility.Visible) != expectScrollBar)
                {
                    violations.Add($"{pass}：夹具须{(expectScrollBar ? "出现" : "不出现")}纵向滚动条");
                }

                for (var index = 0; index < cards.Length && index < snapshot.VisibleCountdowns.Count; index++)
                {
                    var record = snapshot.VisibleCountdowns[index];
                    violations.AddRange(CountdownRowViolations(
                            main,
                            cards[index],
                            record,
                            isPrimary: index == 0,
                            isExpired: record.TargetUtc <= snapshot.UtcNow,
                            CountdownFormatting.FormatRemaining(record.TargetUtc, snapshot.UtcNow, "Second"),
                            viewport!)
                        .Select(item => $"{pass}·{item}"));
                }

                if (countdownActionSignatures.Length == 0 && cards.Length > 0)
                {
                    countdownActionSignatures = VisualDescendants<Button>(cards[0])
                        .Select(button => RowActionSignature(main, button))
                        .ToArray();
                }
            }

            VerifyPass("七行滚动", CountdownRowFixtureOrder, expectScrollBar: true);
            if (!VisualDescendants<TextBlock>(rows).Any(text => text.IsVisible && text.Text == "7 月 8 日 09:30 截止"))
            {
                violations.Add("#221：倒计时列表应显示北京时区的月日时分截止行");
            }

            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = [fixture.LongestTagId] });
            VerifyPass("百万天主项", ["百万天", "已到期行"], expectScrollBar: false);
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = [fixture.ExpiredTagId] });
            VerifyPass("到期主项", ["已到期行"], expectScrollBar: false);
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);

            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            main.UpdateLayout();
            var noteEdit = VisualDescendants<Button>(main).SingleOrDefault(button => button.Uid == "NoteRowEdit");
            var noteActions = noteEdit?.Parent is Panel panel
                ? panel.Children.OfType<Button>().ToArray()
                : Array.Empty<Button>();
            var noteActionSignatures = noteActions.Select(button => RowActionSignature(main, button)).ToArray();
            if (noteActionSignatures.Length != 2 ||
                !countdownActionSignatures.SequenceEqual(noteActionSignatures, StringComparer.Ordinal))
            {
                violations.Add(
                    "倒计时行与便签行的编辑/删除须同一几何与状态语言——倒计时 " +
                    string.Join(" ‖ ", countdownActionSignatures) + " / 便签 " + string.Join(" ‖ ", noteActionSignatures));
            }

            var failed = SummarizeRowViolations(violations);
            Must(failed.Length == 0, $"#247：倒计时行几何 {failed.Length} 类未通过：" + string.Join("；", failed));
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    // #247：单行合同。名称与剩余分格同一主阅读轴（中线差不超过 1 DIP）；截止与标签位于主轴下方的辅助带，
    // 与编辑、删除共用一条中线；各部件不出行界、不相互重叠，单行文字与分格数值不被裁切；
    // 编辑与删除按固有尺寸呈现、不低于紧凑命中下限且不被滚动条遮挡，文字垂直居中、基线一致；
    // 主项、到期与危险色删除的表达保持。
    private static IEnumerable<string> CountdownRowViolations(
        MainWindow main,
        Border card,
        CountdownRecord record,
        bool isPrimary,
        bool isExpired,
        string expectedRemaining,
        FrameworkElement viewport)
    {
        var label = record.Name.Length > 6 ? record.Name[..6] + "…" : record.Name;
        if (card.Child is not Grid layout)
        {
            yield return $"{label}：行根须为网格";
            yield break;
        }

        var texts = VisualDescendants<TextBlock>(card).ToArray();
        var title = texts.SingleOrDefault(text => text.Uid == "CountdownRowTitleText");
        var deadline = texts.SingleOrDefault(text => text.Uid == "CountdownRowDeadlineText");
        var remaining = layout.Children.OfType<TextBlock>().SingleOrDefault(text => text.Tag is MainCountdownBlocksHost);
        var surface = (remaining?.Tag as MainCountdownBlocksHost)?.Surface;
        var blocks = surface?.Children.OfType<FrameworkElement>().SingleOrDefault();
        var buttons = VisualDescendants<Button>(card).ToArray();
        var edit = buttons.SingleOrDefault(button => Equals(button.Content, "编辑"));
        var delete = buttons.SingleOrDefault(button => Equals(button.Content, "删除"));
        var chips = VisualDescendants<Border>(card)
            .Where(border => record.TagIds.Contains(border.Uid, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (title is null || deadline is null || remaining is null || surface is null || blocks is null ||
            edit is null || delete is null || buttons.Length != 2 || chips.Length != record.TagIds.Count)
        {
            yield return $"{label}：行内须有标题、截止、剩余分格、{record.TagIds.Count} 枚标签与编辑/删除两项动作";
            yield break;
        }

        static double Middle(Rect bounds) => (bounds.Top + bounds.Bottom) / 2;
        // DesiredSize 含外边距；固有尺寸扣掉外边距后才能与实测尺寸比较。
        static Size Intrinsic(FrameworkElement element) => new(
            Math.Max(0, element.DesiredSize.Width - element.Margin.Left - element.Margin.Right),
            Math.Max(0, element.DesiredSize.Height - element.Margin.Top - element.Margin.Bottom));
        static bool Inside(Rect outer, Rect inner) =>
            inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
            inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;
        static bool SingleLineFits(TextBlock text)
        {
            var glyphs = new FormattedText(text.Text, CultureInfo.CurrentCulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            return text.TextWrapping == TextWrapping.NoWrap && text.TextTrimming == TextTrimming.None &&
                glyphs.WidthIncludingTrailingWhitespace + Math.Max(0, glyphs.OverhangTrailing) <= text.ActualWidth + 0.5 &&
                glyphs.Height <= text.ActualHeight + 0.5;
        }

        var titleBounds = BoundsOn(title, layout);
        var blocksBounds = BoundsOn(blocks, layout);
        var deadlineBounds = BoundsOn(deadline, layout);
        var editBounds = BoundsOn(edit, layout);
        var deleteBounds = BoundsOn(delete, layout);
        var chipBounds = chips.Select(chip => BoundsOn(chip, layout)).ToArray();

        var axisOffset = Math.Abs(Middle(titleBounds) - Middle(blocksBounds));
        if (axisOffset > 1)
        {
            yield return $"{label}：名称与剩余分格须同一主阅读轴，中线相差 {axisOffset:0.#} DIP";
        }

        var axisBottom = Math.Max(titleBounds.Bottom, blocksBounds.Bottom);
        if (deadlineBounds.Top < axisBottom - 0.5 || chipBounds.Any(bounds => bounds.Top < axisBottom - 0.5))
        {
            yield return $"{label}：截止与标签须位于主阅读轴下方的辅助带";
        }

        if (chipBounds.Length > 0 &&
            Math.Abs((chipBounds.Min(bounds => bounds.Top) + chipBounds.Max(bounds => bounds.Bottom)) / 2 -
                     Middle(deadlineBounds)) > 1)
        {
            yield return $"{label}：截止与标签须同一辅助带中线";
        }

        if (Math.Abs(Middle(editBounds) - Middle(deadlineBounds)) > 1 ||
            Math.Abs(Middle(deleteBounds) - Middle(deadlineBounds)) > 1)
        {
            yield return $"{label}：编辑与删除须与辅助带同一中线";
        }

        (string Name, Rect Bounds)[] parts =
        [
            ("名称", titleBounds), ("剩余分格", blocksBounds), ("截止", deadlineBounds),
            ("编辑", editBounds), ("删除", deleteBounds),
            .. chipBounds.Select((bounds, index) => (Name: $"标签{index + 1}", Bounds: bounds))
        ];
        var layoutBounds = new Rect(layout.RenderSize);
        for (var first = 0; first < parts.Length; first++)
        {
            if (!Inside(layoutBounds, parts[first].Bounds))
            {
                yield return $"{label}：{parts[first].Name}超出行界";
            }

            for (var second = first + 1; second < parts.Length; second++)
            {
                var overlap = Rect.Intersect(parts[first].Bounds, parts[second].Bounds);
                if (!overlap.IsEmpty && overlap.Width > 0.5 && overlap.Height > 0.5)
                {
                    yield return $"{label}：{parts[first].Name}与{parts[second].Name}重叠";
                }
            }
        }

        foreach (var (text, name) in new[] { (title, "名称"), (deadline, "截止") })
        {
            if (!SingleLineFits(text) ||
                VisualTreeHelper.GetParent(text) is not FrameworkElement host ||
                !Inside(BoundsOn(host, layout), BoundsOn(text, layout)))
            {
                yield return $"{label}：{name}文字被裁切或折行";
            }
        }

        if (!Inside(BoundsOn(surface, layout), blocksBounds) ||
            VisualDescendants<TextBlock>(blocks).Prepend(blocks as TextBlock).OfType<TextBlock>()
                .Any(text => !SingleLineFits(text)))
        {
            yield return $"{label}：剩余分格或数值被裁切";
        }

        if (!string.Equals(remaining.Text, expectedRemaining, StringComparison.Ordinal))
        {
            yield return $"{label}：剩余时间应为「{expectedRemaining}」，实为「{remaining.Text}」";
        }

        if (chips.Any(chip => chip.ActualWidth > 148.5 || chip.ActualWidth < Intrinsic(chip).Width - 0.5))
        {
            yield return $"{label}：标签芯片超出 148 DIP 上限或被挤压";
        }

        if (!ReferenceEquals(card.Background, main.FindResource(isExpired ? "ExpiredBrush" : "CardBrush")))
        {
            yield return $"{label}：{(isExpired ? "到期行须用 ExpiredBrush" : "未到期行须用 CardBrush")}";
        }

        if (isExpired
                ? blocks is not TextBlock { Text: "已到期" } fallback ||
                  !ReferenceEquals(fallback.Foreground, main.FindResource(isPrimary ? "PrimaryCountdownNumberBrush" : "BodyBrush"))
                : !VisualDescendants<Border>(blocks).All(block =>
                    ReferenceEquals(block.Background, main.FindResource(isPrimary ? "PinkBlockBrush" : "SecondaryBlockBrush"))))
        {
            yield return $"{label}：{(isPrimary ? "主项" : "次项")}{(isExpired ? "到期" : "分格")}表达不符";
        }

        var dangerColor = (Color)main.FindResource("DangerColor");
        if (delete.Foreground is not SolidColorBrush { Color: var deleteColor } || deleteColor != dangerColor ||
            delete.BorderBrush is not SolidColorBrush { Color.A: 0 } ||
            !VisualDescendants<System.Windows.Shapes.Path>(delete).Any(icon =>
                icon.IsVisible && ReferenceEquals(icon.Stroke, main.FindResource("DangerBrush"))))
        {
            yield return $"{label}：删除须为危险色文字加垃圾桶图标且无红框";
        }

        if (!VisualDescendants<System.Windows.Shapes.Path>(edit).Any(icon =>
                icon.IsVisible && ReferenceEquals(icon.Stroke, main.FindResource("InkBrush"))))
        {
            yield return $"{label}：编辑须为 InkBrush 铅笔图标加文字";
        }

        var baselines = new List<double>();
        foreach (var (button, name) in new[] { (edit, "编辑"), (delete, "删除") })
        {
            var intrinsic = Intrinsic(button);
            if (button.ActualWidth < Math.Max(24, Math.Max(button.MinWidth, intrinsic.Width)) - 0.5 ||
                button.ActualHeight < Math.Max(24, Math.Max(button.MinHeight, intrinsic.Height)) - 0.5)
            {
                yield return $"{label}：{name}命中区被挤压（{button.ActualWidth:0.#}×{button.ActualHeight:0.#}）";
            }

            if (Math.Abs(button.ActualHeight - intrinsic.Height) > 1)
            {
                yield return $"{label}：{name}不得被跨行内容拉伸为竖长按钮";
            }

            var inViewport = BoundsOn(button, viewport);
            if (inViewport.Left < -0.5 || inViewport.Right > viewport.ActualWidth + 0.5)
            {
                yield return $"{label}：{name}越出列表视口（滚动条遮挡）";
            }

            var presenter = VisualDescendants<ContentPresenter>(button).SingleOrDefault();
            var caption = presenter is null ? null : VisualDescendants<TextBlock>(presenter).SingleOrDefault();
            if (caption is null)
            {
                yield return $"{label}：{name}缺少文字";
                continue;
            }

            var captionBounds = caption.TransformToAncestor(button).TransformBounds(new Rect(caption.RenderSize));
            if (Math.Abs((button.ActualHeight - captionBounds.Height) / 2 - captionBounds.Top) > 1)
            {
                yield return $"{label}：{name}文字须垂直居中";
            }

            baselines.Add(caption.TransformToAncestor(layout).Transform(new Point(0, caption.BaselineOffset)).Y);
        }

        if (baselines.Count == 2 && Math.Abs(baselines[0] - baselines[1]) > 1)
        {
            yield return $"{label}：编辑与删除的文字基线须一致";
        }
    }

    // #247：次级行内动作的几何与状态语言签名——样式、最小尺寸、外边距、内边距、字号、实测尺寸、
    // 图标描边、前景、边框与 Delete 提示；倒计时行与便签行逐项相同才算同一套。
    private static string RowActionSignature(FrameworkElement scope, Button button)
    {
        string Role(object? brush) =>
            new[] { "InkBrush", "BodyBrush", "MutedBrush", "DangerBrush" }
                .FirstOrDefault(key => ReferenceEquals(brush, scope.TryFindResource(key))) ??
            (brush is SolidColorBrush solid ? solid.Color.ToString(CultureInfo.InvariantCulture) : "无");
        var icon = VisualDescendants<System.Windows.Shapes.Path>(button).SingleOrDefault(path => path.IsVisible);
        var tip = button.ToolTip switch
        {
            null => "无提示",
            string text when text.EndsWith("（Delete）", StringComparison.Ordinal) => "Delete 提示",
            _ => "其他提示"
        };
        return string.Join(
            "|",
            $"{button.Content}",
            ReferenceEquals(button.Style, scope.TryFindResource("SecondaryButton")) ? "SecondaryButton" : "其他样式",
            FormattableString.Invariant(
                $"最小 {button.MinWidth:0.#}×{button.MinHeight:0.#} 外边距 {button.Margin} 内边距 {button.Padding} 字号 {button.FontSize:0.#}"),
            FormattableString.Invariant($"实测 {button.ActualWidth:0.#}×{button.ActualHeight:0.#}"),
            $"图标 {(icon is null ? "无" : Role(icon.Stroke))}",
            $"前景 {Role(button.Foreground)}",
            $"边框 {Role(button.BorderBrush)}",
            tip);
    }

    private static T? VisualAncestor<T>(DependencyObject node)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(node); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    // 违反项写成「排布·行（数值）：规则」；同一条规则在多行违反时合并成一项，并列出所在排布与行。
    private static string[] SummarizeRowViolations(IEnumerable<string> violations) =>
        violations
            .Select(item => item.Split('：', 2))
            .GroupBy(pieces => pieces[^1], StringComparer.Ordinal)
            .Select(group =>
            {
                var where = group.Where(pieces => pieces.Length == 2).Select(pieces => pieces[0]).Distinct().ToArray();
                return where.Length == 0
                    ? group.Key
                    : $"{group.Key}（{string.Join("、", where.Take(4))}{(where.Length > 4 ? $" 等 {where.Length} 处" : string.Empty)}）";
            })
            .ToArray();

    // #249：便签卡关键状态夹具。未完成两组五张卡：无标签无提醒的短正文、140 字长正文（一次提醒、两枚标签）、
    // 五枚标签（含 30 字长标签）加两条提醒、每 15 分钟提醒的来源卡、只带一枚标签的短卡，首选尺寸下必出纵向滚动条；
    // 已完成两张卡带删除线与暂停摘要；「复盘」搜索只留长正文与五标签两张。卡片几何探针与 validate-only 实拍共用这一份夹具。
    internal sealed record NoteRowStateFixture(string ReminderSourceNoteId, string SearchTerm);

    // 北京时间 2032 年 5 月 18 日 10:15:30；夹具提醒都落在它之后。
    internal static readonly DateTimeOffset NoteRowFixtureUtc = new(2032, 5, 18, 2, 15, 30, TimeSpan.Zero);

    private const string NoteRowFixtureLongBody =
        "周一上午先整理季度复盘材料，汇总各组进度与风险，再把会议纪要和待办同步到共享文档；下午和设计一起讨论便签卡排版，" +
        "确认正文、提醒摘要与标签的阅读顺序和间距，最后检查长正文在列表出现纵向滚动条时仍然按可用宽度自然换行，" +
        "不被独立的标签列挤成窄条，也不会遮挡卡片右侧的编辑和删除两个按钮。";

    private const string NoteRowFixtureLongTag = "超长标签名称用于验证便签卡芯片在省略之后仍然可以查看完整名称";

    internal static NoteRowStateFixture SeedNoteRowStates(DesktopWorkspace workspace)
    {
        var today = workspace.Snapshot.BeijingNow.Date;
        var yesterday = today.AddDays(-1);
        string Tag(string name, string? color) =>
            MustApply(workspace, new CreateTagChange(name, color)).CreatedTag?.Id ??
            throw new InvalidOperationException($"#249：未取得夹具标签「{name}」。");
        string Note(string text, DateTime date, string[] tagIds, params ReminderRule[] rules)
        {
            MustApply(workspace, new SaveNoteChange(null, text, date, false, tagIds, rules));
            return workspace.Snapshot.Settings.Notes
                .Single(note => string.Equals(note.Text, text, StringComparison.Ordinal)).Id;
        }

        var work = Tag("工作", "#F4B6A6");
        var life = Tag("生活", "#A8D5BA");
        var longTag = Tag(NoteRowFixtureLongTag, "#A8C7E8");
        var focus = Tag("专注", null);
        var family = Tag("家庭聚会安排", null);
        var onceUtc = new DateTimeOffset(2032, 5, 18, 3, 30, 0, TimeSpan.Zero);
        Note("买牛奶", today, []);
        Note(NoteRowFixtureLongBody, today, [work, life], new AbsoluteOnceReminderRule("note-row-once", onceUtc));
        Note("整理季度复盘材料并同步给团队", today, [work, life, longTag, focus, family],
            new AbsoluteOnceReminderRule("note-row-first", onceUtc),
            new AbsoluteOnceReminderRule("note-row-second", onceUtc.AddDays(1)));
        var source = Note("每隔一刻钟起身喝水", yesterday, [life], new FixedIntervalReminderRule(
            "note-row-interval", new DateTimeOffset(2032, 5, 18, 2, 30, 0, TimeSpan.Zero), new ReminderInterval(15)));
        Note("周报草稿", yesterday, [focus]);
        MustApply(workspace, new SetNotesCompletionChange(
            [
                Note("已完成的便签保留删除线与弱化颜色", yesterday, [work, longTag],
                    new AbsoluteOnceReminderRule("note-row-paused", onceUtc.AddDays(2))),
                Note("已完成短便签", yesterday, [])
            ],
            true));
        return new NoteRowStateFixture(source, "复盘");
    }

    // #249：便签卡围绕内容组织。完成勾选、正文首行与编辑/删除同一首行中线；正文与提醒摘要连成一个内容列，吸收动作槽
    // 以左的全部宽度；标签在内容下方就近换行、与正文左对齐，不伸进动作槽；编辑与删除停在每张卡同一个右上动作槽，不随
    // 正文高度、选择或提醒来源强调移动。未完成（出现滚动条）、搜索、多选、提醒来源与已完成五种排布逐卡检查，选择前后
    // 卡内几何逐项相同，Esc 收起批量操作条，打开提醒来源把整张来源卡滚进列表视口。违反项汇总后一次报出。
    public static bool RunNoteRowContentGeometryBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-note-row-geometry-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(
            path,
            new SettingsStore(clock: new AdvancingValidationClock(NoteRowFixtureUtc)));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var fixture = SeedNoteRowStates(workspace);
        Must(NoteRowFixtureLongBody.Length == 140 && NoteRowFixtureLongTag.Length == 30,
            "#249：长正文与长标签夹具须顶到 140 字与 30 字上限。");
        // 与倒计时行几何探针同一口径固定首选 864×1000。
        using var workArea = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(new Rect(0, 0, 2560, 2000), 1, 1));
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var parts = main.NoteListPartsForValidation;
            var viewport = VisualDescendants<ScrollContentPresenter>(parts.ListRegion).FirstOrDefault();
            Must(viewport is not null, "#249：便签列表须在滚动区域内。");
            var violations = new List<string>();
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Point? referenceSlot = null;
            Dictionary<string, string> VerifyPass(string pass, StackPanel rows, int expectedCards, bool? expectScrollBar)
            {
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                var cards = VisualDescendants<NoteSelectionCard>(rows).Where(card => card.IsVisible).ToArray();
                if (cards.Length != expectedCards)
                {
                    violations.Add($"{pass}：夹具须显示 {expectedCards} 张便签卡，实为 {cards.Length} 张");
                }

                if (expectScrollBar is bool expected &&
                    (parts.ListRegion.ComputedVerticalScrollBarVisibility == Visibility.Visible) != expected)
                {
                    violations.Add($"{pass}：夹具须{(expected ? "出现" : "不出现")}纵向滚动条");
                }

                var signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var card in cards)
                {
                    var note = workspace.Snapshot.AllNotes.Single(item =>
                        string.Equals(item.Id, card.Tag as string, StringComparison.OrdinalIgnoreCase));
                    var label = note.Text.Length > 6 ? note.Text[..6] + "…" : note.Text;
                    labels[note.Id] = label;
                    if (MeasureNoteRow(main, card, note, workspace.Snapshot.Settings, viewport!, $"{pass}·{label}", violations)
                        is not { } row)
                    {
                        continue;
                    }

                    signatures[note.Id] = row.Signature;
                    referenceSlot ??= row.Slot;
                    var drift = row.Slot - referenceSlot.Value;
                    if (Math.Abs(drift.X) > 0.5 || Math.Abs(drift.Y) > 0.5)
                    {
                        violations.Add(FormattableString.Invariant(
                            $"{pass}·{label}（右距差 {drift.X:0.#}、上距差 {drift.Y:0.#} DIP）：编辑与删除须停在每张卡同一右上动作槽"));
                    }
                }

                return signatures;
            }

            var unselected = VerifyPass("未完成", parts.ActiveRows, expectedCards: 5, expectScrollBar: true);
            parts.Search.Text = fixture.SearchTerm;
            VerifyPass("搜索", parts.ActiveRows, expectedCards: 2, expectScrollBar: null);
            parts.Search.Clear();
            WindowValidationInput.Pump(main.Dispatcher);

            var chosen = VisualDescendants<NoteSelectionCard>(parts.ActiveRows).Where(card => card.IsVisible).Take(2).ToArray();
            foreach (var card in chosen)
            {
                Keyboard.Focus(card);
                WindowValidationInput.RaisePreviewKey(card, Key.Space, ModifierKeys.Control);
            }

            var selected = VerifyPass("多选", parts.ActiveRows, expectedCards: 5, expectScrollBar: true);
            if (chosen.Length != 2 || chosen.Any(card => !card.IsSelected) ||
                parts.SelectedCount.Text != "已选择 2 条" || !parts.BatchActions.IsVisible)
            {
                violations.Add("多选：Ctrl+空格选中两张卡后须显示批量操作条与「已选择 2 条」");
            }

            foreach (var (id, signature) in unselected)
            {
                if (selected.TryGetValue(id, out var after) && !string.Equals(after, signature, StringComparison.Ordinal))
                {
                    violations.Add($"多选·{labels[id]}：选择不得改变卡内几何");
                }
            }

            if (Keyboard.FocusedElement is NoteSelectionCard focusedCard)
            {
                WindowValidationInput.RaisePreviewKey(focusedCard, Key.Escape);
                WindowValidationInput.Pump(main.Dispatcher);
            }

            if (parts.SelectedCount.Text != "已选择 0 条" || parts.BatchActions.IsVisible)
            {
                violations.Add("多选：Esc 须取消选择并收起批量操作条");
            }

            // 来源卡在列表末尾：先回到顶部，打开来源时必须真的把它滚进视口。
            parts.ListRegion.ScrollToTop();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(main.OpenReminderSource(ReminderSourceKind.Note, fixture.ReminderSourceNoteId),
                "#249：须真实打开提醒来源便签。");
            VerifyPass("提醒来源", parts.ActiveRows, expectedCards: 5, expectScrollBar: true);
            var sourceCard = VisualDescendants<NoteSelectionCard>(parts.ActiveRows).SingleOrDefault(card =>
                string.Equals(card.Tag as string, fixture.ReminderSourceNoteId, StringComparison.OrdinalIgnoreCase));
            if (sourceCard is null || sourceCard.BorderThickness.Left < 1.5 ||
                !ReferenceEquals(sourceCard.BorderBrush, main.FindResource("ActiveBrush")) ||
                AutomationProperties.GetName(sourceCard)?.StartsWith("当前提醒来源便签：", StringComparison.Ordinal) != true)
            {
                violations.Add("提醒来源：来源卡须以 2 DIP ActiveBrush 边界强调并报出来源身份");
            }

            // 来源卡比视口矮时，打开来源要让整张卡（正文、摘要与标签）都在视口里，而不只是获焦的编辑按钮所在的首行带。
            if (sourceCard is not null)
            {
                var listViewport = viewport!;
                var revealed = BoundsOn(sourceCard, listViewport);
                if (revealed.Height <= listViewport.ActualHeight &&
                    (revealed.Top < -0.5 || revealed.Bottom > listViewport.ActualHeight + 0.5))
                {
                    violations.Add(FormattableString.Invariant(
                        $"提醒来源（卡片 {revealed.Top:0.#}～{revealed.Bottom:0.#}，视口高 {listViewport.ActualHeight:0.#} DIP）：打开提醒来源须把整张来源卡滚进列表视口"));
                }
            }

            parts.CompletedSegment.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            VerifyPass("已完成", parts.CompletedRows, expectedCards: 2, expectScrollBar: null);

            var failed = SummarizeRowViolations(violations);
            Must(failed.Length == 0, $"#249：便签卡几何 {failed.Length} 类未通过：" + string.Join("；", failed));
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    // #249：单卡合同。卡根网格直接含完成勾选；勾选框、正文首行与编辑/删除中线相差不超过 1 DIP；正文右缘离编辑
    // 不超过 12.5 DIP（没有独立标签列）；提醒摘要紧接正文、与正文左对齐，换行完整显示，时钟图标对齐摘要首行；
    // 标签在内容下方 8.5 DIP 内起排、与正文左对齐，单行、不超过 148 DIP，省略时提示全文；内容不伸进动作槽、
    // 各部件不出卡片内容区、不相互重叠；编辑与删除按固有尺寸呈现且不被滚动条遮挡；完成与未完成正文的颜色与删除线
    // 保持。返回动作槽相对卡片外框的右距与上距，以及卡内各部件的几何签名。
    private static (Point Slot, string Signature)? MeasureNoteRow(
        MainWindow main,
        NoteSelectionCard card,
        NoteRecord note,
        CountdownSettings settings,
        FrameworkElement viewport,
        string where,
        List<string> violations)
    {
        void Fail(string rule, string? detail = null) =>
            violations.Add(detail is null ? $"{where}：{rule}" : $"{where}（{detail}）：{rule}");
        if (card.Child is not Grid layout)
        {
            Fail("卡根须为网格");
            return null;
        }

        var completion = layout.Children.OfType<CheckBox>().SingleOrDefault();
        var box = completion is null ? null : VisualDescendants<Border>(completion).FirstOrDefault();
        var texts = VisualDescendants<TextBlock>(card).ToArray();
        var body = texts.SingleOrDefault(text => text.Uid == "NoteRowBodyText");
        var summary = texts.SingleOrDefault(text =>
            AutomationProperties.GetAutomationId(text) == MainWindow.NoteCardReminderSummaryAutomationId);
        var glyph = summary is null
            ? null
            : (VisualTreeHelper.GetParent(summary) as Panel)?.Children.OfType<System.Windows.Shapes.Path>().SingleOrDefault();
        var buttons = VisualDescendants<Button>(card).ToArray();
        var edit = buttons.SingleOrDefault(button => Equals(button.Content, "编辑"));
        var delete = buttons.SingleOrDefault(button => Equals(button.Content, "删除"));
        var chips = VisualDescendants<Border>(card)
            .Where(border => note.TagIds.Contains(border.Uid, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var expectSummary = note.ReminderRules.Count > 0;
        if (completion is null || box is null || body is null || edit is null || delete is null || buttons.Length != 2 ||
            chips.Length != note.TagIds.Count || (expectSummary ? summary is null || glyph is null : summary is not null))
        {
            Fail($"卡内须有完成勾选、正文、{note.TagIds.Count} 枚标签{(expectSummary ? "、提醒摘要与时钟图标" : string.Empty)}与编辑/删除两项动作");
            return null;
        }

        static double Middle(Rect bounds) => (bounds.Top + bounds.Bottom) / 2;
        static double FirstLineHeight(TextBlock text) =>
            double.IsNaN(text.LineHeight) ? text.FontFamily.LineSpacing * text.FontSize : text.LineHeight;
        // DesiredSize 含外边距；固有尺寸扣掉外边距后才能与实测尺寸比较。
        static Size Intrinsic(FrameworkElement element) => new(
            Math.Max(0, element.DesiredSize.Width - element.Margin.Left - element.Margin.Right),
            Math.Max(0, element.DesiredSize.Height - element.Margin.Top - element.Margin.Bottom));
        static bool Inside(Rect outer, Rect inner) =>
            inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
            inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;
        static FormattedText Format(TextBlock text) => new(text.Text, CultureInfo.CurrentCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        // 按实测宽度重排一遍：行数与行高都放得下、且没有越出宿主容器（被布局裁切）才算完整显示。
        static bool WrappedTextFits(TextBlock text)
        {
            var formatted = Format(text);
            formatted.MaxTextWidth = Math.Max(1, text.ActualWidth);
            if (!double.IsNaN(text.LineHeight))
            {
                formatted.LineHeight = text.LineHeight;
            }

            return text.TextWrapping == TextWrapping.Wrap &&
                formatted.Height <= text.ActualHeight + 0.5 &&
                formatted.MinWidth <= text.ActualWidth + 0.5 &&
                (VisualTreeHelper.GetParent(text) is not FrameworkElement host ||
                 Inside(new Rect(host.RenderSize), BoundsOn(text, host)));
        }

        var completionBounds = BoundsOn(completion, layout);
        var boxBounds = BoundsOn(box, layout);
        var bodyBounds = BoundsOn(body, layout);
        var editBounds = BoundsOn(edit, layout);
        var deleteBounds = BoundsOn(delete, layout);
        var summaryBounds = summary is null ? Rect.Empty : BoundsOn(summary, layout);
        var glyphBounds = glyph is null ? Rect.Empty : BoundsOn(glyph, layout);
        var chipBounds = chips.Select(chip => BoundsOn(chip, layout)).ToArray();

        var firstLine = bodyBounds.Top + FirstLineHeight(body) / 2;
        var axisOffsets = new[] { ("勾选框", Middle(boxBounds)), ("编辑", Middle(editBounds)), ("删除", Middle(deleteBounds)) }
            .Where(item => Math.Abs(item.Item2 - firstLine) > 1)
            .Select(item => FormattableString.Invariant($"{item.Item1}差 {item.Item2 - firstLine:0.#}"))
            .ToArray();
        if (axisOffsets.Length > 0)
        {
            Fail("完成勾选、正文首行与编辑/删除须同一首行中线", string.Join("、", axisOffsets));
        }

        var bodyGap = editBounds.Left - bodyBounds.Right;
        if (bodyGap > 12.5)
        {
            Fail("正文须吸收动作槽以左的全部宽度，不再让出独立标签列",
                FormattableString.Invariant($"正文右缘离编辑 {bodyGap:0.#} DIP"));
        }

        var content = new List<(string Name, Rect Bounds)> { ("正文", bodyBounds) };
        if (summary is not null)
        {
            content.Add(("提醒摘要", summaryBounds));
            content.Add(("时钟图标", glyphBounds));
        }

        content.AddRange(chipBounds.Select((bounds, index) => ($"标签{index + 1}", bounds)));
        var intruding = content.Where(part => part.Bounds.Right > editBounds.Left - 11.5).Select(part => part.Name).ToArray();
        if (intruding.Length > 0)
        {
            Fail("正文、提醒摘要与标签不得伸进编辑/删除动作槽", string.Join("、", intruding));
        }

        if (!WrappedTextFits(body))
        {
            Fail("正文须按内容列宽度完整换行，不被裁切");
        }

        var struck = body.TextDecorations is { Count: > 0 } decorations &&
            decorations.All(decoration => decoration.Location == TextDecorationLocation.Strikethrough);
        if (!ReferenceEquals(body.Foreground, main.FindResource(note.IsCompleted ? "MutedBrush" : "InkBrush")) ||
            struck != note.IsCompleted)
        {
            Fail(note.IsCompleted ? "已完成正文须 MutedBrush 加删除线" : "未完成正文须 InkBrush 且无删除线");
        }

        if (summary is not null)
        {
            var rowTop = Math.Min(summaryBounds.Top, glyphBounds.Top);
            if (rowTop < bodyBounds.Bottom - 0.5 || rowTop > bodyBounds.Bottom + 4.5 ||
                Math.Abs(glyphBounds.Left - bodyBounds.Left) > 0.5 || glyphBounds.Right > summaryBounds.Left + 0.5)
            {
                Fail("提醒摘要须紧接正文下方、与正文左对齐，连成同一内容列");
            }

            if (!WrappedTextFits(summary))
            {
                Fail("提醒摘要须在内容列内换行完整显示，不被裁切");
            }

            var summaryFirstLine = summaryBounds.Top + FirstLineHeight(summary) / 2;
            if (Math.Abs(Middle(glyphBounds) - summaryFirstLine) > 1)
            {
                Fail("时钟图标须与提醒摘要首行同一中线",
                    FormattableString.Invariant($"差 {Middle(glyphBounds) - summaryFirstLine:0.#} DIP"));
            }

            if (summary.FontSize > body.FontSize + 0.01 ||
                !ReferenceEquals(summary.Foreground, main.FindResource("MutedBrush")))
            {
                Fail("提醒摘要须用 MutedBrush 且不大于正文");
            }
        }

        if (chipBounds.Length > 0)
        {
            var above = summary is null ? bodyBounds : Rect.Union(summaryBounds, glyphBounds);
            var gap = chipBounds.Min(bounds => bounds.Top) - above.Bottom;
            if (gap < -0.5 || gap > 8.5 || Math.Abs(chipBounds.Min(bounds => bounds.Left) - bodyBounds.Left) > 0.5)
            {
                Fail("标签须在正文与提醒摘要下方就近排列、与正文左对齐",
                    FormattableString.Invariant($"距上方内容 {gap:0.#} DIP"));
            }

            for (var index = 0; index < chips.Length; index++)
            {
                var chip = chips[index];
                var tag = settings.Tags.Single(item => string.Equals(item.Id, chip.Uid, StringComparison.OrdinalIgnoreCase));
                var chipText = chip.Child as TextBlock;
                var trimmed = chipText is not null &&
                    Format(chipText).WidthIncludingTrailingWhitespace > chipText.ActualWidth + 0.5;
                if (chipText is null || chipText.TextWrapping != TextWrapping.NoWrap ||
                    chip.ActualWidth > MainWindow.TagChipMaxWidth + 0.5 || chip.ActualWidth < Intrinsic(chip).Width - 0.5)
                {
                    Fail("标签芯片须单行、不超过 148 DIP 上限且不被挤压", $"标签{index + 1}");
                }

                if (trimmed && (chipText!.TextTrimming != TextTrimming.CharacterEllipsis ||
                                !string.Equals(chip.ToolTip as string, tag.Name, StringComparison.Ordinal)))
                {
                    Fail("被省略的标签须以省略号收尾并以提示给出完整名称", $"标签{index + 1}");
                }

                if (tag.Name.Length >= 30 && !trimmed)
                {
                    Fail("30 字长标签须在 148 DIP 上限内省略", $"标签{index + 1}");
                }
            }
        }

        var parts = new List<(string Name, Rect Bounds)> { ("完成勾选", completionBounds) };
        parts.AddRange(content);
        parts.Add(("编辑", editBounds));
        parts.Add(("删除", deleteBounds));
        var layoutBounds = new Rect(layout.RenderSize);
        for (var first = 0; first < parts.Count; first++)
        {
            if (!Inside(layoutBounds, parts[first].Bounds))
            {
                Fail($"{parts[first].Name}超出卡片内容区");
            }

            for (var second = first + 1; second < parts.Count; second++)
            {
                var overlap = Rect.Intersect(parts[first].Bounds, parts[second].Bounds);
                if (!overlap.IsEmpty && overlap.Width > 0.5 && overlap.Height > 0.5)
                {
                    Fail($"{parts[first].Name}与{parts[second].Name}重叠");
                }
            }
        }

        foreach (var (button, name) in new[] { (edit, "编辑"), (delete, "删除") })
        {
            var intrinsic = Intrinsic(button);
            if (button.ActualWidth < Math.Max(24, Math.Max(button.MinWidth, intrinsic.Width)) - 0.5 ||
                button.ActualHeight < Math.Max(24, Math.Max(button.MinHeight, intrinsic.Height)) - 0.5 ||
                Math.Abs(button.ActualHeight - intrinsic.Height) > 1)
            {
                Fail($"{name}须按固有尺寸呈现，不被挤压或纵向拉伸",
                    FormattableString.Invariant($"{button.ActualWidth:0.#}×{button.ActualHeight:0.#}"));
            }

            var inViewport = BoundsOn(button, viewport);
            if (inViewport.Left < -0.5 || inViewport.Right > viewport.ActualWidth + 0.5)
            {
                Fail($"{name}越出列表视口（被滚动条遮挡）");
            }
        }

        static string Rounded(Rect bounds) =>
            FormattableString.Invariant($"{bounds.Left:0.#},{bounds.Top:0.#},{bounds.Width:0.#},{bounds.Height:0.#}");
        var editOnCard = BoundsOn(edit, card);
        var deleteOnCard = BoundsOn(delete, card);
        return (new Point(card.ActualWidth - deleteOnCard.Right, editOnCard.Top),
            string.Join(";", parts.Select(part => part.Name + "=" + Rounded(part.Bounds))));
    }

    public static bool RunMainWindowNoteMinuteRefreshBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-note-minute-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new AdvancingValidationClock(
                new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            for (var index = 0; index < 12; index++)
            {
                MustApply(workspace, new SaveNoteChange(
                    null, $"分钟刷新便签 {index:D2}", new DateTime(2038, 7, 8), false,
                    Array.Empty<string>(), Array.Empty<ReminderRule>()));
            }

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var main = new MainWindow(
                workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                ((ButtonBase)main.FindName("NotesTab")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                Must(((TextBlock)main.FindName("NowText")).Text == "当前北京时间：2038 年 7 月 8 日 09:00:00",
                    "#225 状态栏应显示无前导零、带中文空格的北京时间。");
                var scroller = (ScrollViewer)main.FindName("NotesListRegion");
                var activeRows = (Panel)main.FindName("ActiveNoteRows");
                var trigger = VisualDescendants<Button>(activeRows)
                    .Last(button => button.Uid == "NoteRowEdit");
                Keyboard.Focus(trigger);
                WindowValidationInput.Pump(main.Dispatcher);
                scroller.ScrollToEnd();
                WindowValidationInput.Pump(main.Dispatcher);
                var offset = scroller.VerticalOffset;
                Must(ReferenceEquals(Keyboard.FocusedElement, trigger) && offset > 0,
                    "分钟刷新探针须先聚焦真实便签操作按钮并建立非零滚动偏移。");

                clock.UtcNow = clock.UtcNow.AddMinutes(1);
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                Must(ReferenceEquals(Keyboard.FocusedElement, trigger) &&
                     VisualDescendants<Button>(activeRows).Contains(trigger) &&
                     Math.Abs(scroller.VerticalOffset - offset) <= 0.1,
                    "#230：便签数据未变的分钟刷新必须保留行控件、键盘焦点与滚动位置。");

                var note = workspace.Snapshot.AllNotes.Single(item => item.Id == (string)trigger.Tag);
                var dueUtc = clock.UtcNow.AddMinutes(1);
                MustApply(workspace, new SaveNoteChange(note.Id, note.Text, new DateTime(2038, 7, 8), false,
                    note.TagIds, new ReminderRule[]
                    {
                        new FixedIntervalReminderRule("minute-focus-repeat", dueUtc, new ReminderInterval(30))
                    }));
                WindowValidationInput.Pump(main.Dispatcher);
                trigger = VisualDescendants<Button>(activeRows)
                    .Single(button => button.Uid == "NoteRowEdit" && (string)button.Tag == note.Id);
                Keyboard.Focus(trigger);
                WindowValidationInput.Pump(main.Dispatcher);
                scroller.ScrollToEnd();
                WindowValidationInput.Pump(main.Dispatcher);
                offset = scroller.VerticalOffset;
                var summary = VisualDescendants<TextBlock>(activeRows).Single(text =>
                    AutomationProperties.GetAutomationId(text) == MainWindow.NoteCardReminderSummaryAutomationId);
                Must(summary.Text.Contains("09:02", StringComparison.Ordinal), "周期便签初始摘要应指向首次提醒。");
                ProbeVisibleReminderDates(workspace, calendar, main, note, dueUtc);
                clock.UtcNow = dueUtc;
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                var summaryAfter = VisualDescendants<TextBlock>(activeRows).Single(text =>
                    AutomationProperties.GetAutomationId(text) == MainWindow.NoteCardReminderSummaryAutomationId);
                Must(summaryAfter.Text.Contains("09:32", StringComparison.Ordinal),
                    "周期提醒跨首次到期后须显示真实的下一次摘要。");
                Must(ReferenceEquals(Keyboard.FocusedElement, trigger) &&
                     VisualDescendants<Button>(activeRows).Contains(trigger) &&
                     Math.Abs(scroller.VerticalOffset - offset) <= 0.1,
                    "#230：周期提醒摘要变化的分钟刷新必须保留便签操作角色焦点与滚动位置。");
                clock.UtcNow = new DateTimeOffset(2038, 7, 8, 16, 0, 0, TimeSpan.Zero);
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                var dateLabel = VisualDescendants<TextBlock>(activeRows)
                    .Single(text => text.Uid == "NoteDateLabel");
                var dateToggle = VisualDescendants<Button>(activeRows)
                    .Single(button => button.Uid == "NoteDateDisclosure");
                Must(dateLabel.Text == "昨天 · 7 月 8 日" &&
                     AutomationProperties.GetName(dateToggle).Contains("昨天 · 7 月 8 日", StringComparison.Ordinal) &&
                     dateToggle.ToolTip is string dateTip && dateTip.Contains("昨天 · 7 月 8 日", StringComparison.Ordinal) &&
                     ReferenceEquals(Keyboard.FocusedElement, trigger) &&
                     Math.Abs(scroller.VerticalOffset - offset) <= 0.1,
                    "#230：跨北京时间午夜应就地更新便签日期与可访问名并保留焦点和滚动。");
                return true;
            }
            finally
            {
                main.Close();
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #211：管理窗口装进任意工作区。注入 1080p 在 100%/125%/150% 缩放下的三档
    // 工作区高度（1032/826/688 DIP），窗口必须取「首选 864×1000 与工作区−安全边距」
    // 的较小值并完整落在工作区内；100% 时首选尺寸一屏放下、无纵向滚动，其余档由外层
    // 纵向滚动兜底；顶端置顶/最小化/关闭免滚动可达；状态栏完整可见；倒计时前三行
    // 逐行滚动可达完整可见；内容不缩放。用户调整
    // 的720×560 位置尺寸在隐藏后再显示时保留；越界显示与 WM_DISPLAYCHANGE 工作区
    // 变化时夹回所在显示器工作区。全程经注入工作区，不依赖真实显示拓扑。
    public static bool RunMainWindowWorkAreaFitBehavior() =>
        RunMainWindowWorkAreaFitBehavior(AppContext.BaseDirectory);

    internal static bool RunMainWindowWorkAreaFitBehavior(string assetRoot)
    {
        // The injected monitor must not depend on the SSH desktop's 1024x768
        // native tracking limit. Verify both the constrained and roomy fixtures.
        foreach (var capacity in new[] { new Size(1044, 788), new Size(3840, 2160) })
        {
            var probe = new Window { Width = 1165, Height = 1008, WindowStyle = WindowStyle.None };
            PrepareOffscreenValidationWindow(probe);
            using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(probe, capacity);
            try
            {
                var preparedHandle = new WindowInteropHelper(probe).Handle;
                Must(preparedHandle != IntPtr.Zero && !probe.IsVisible,
                    "原生尺寸夹具应在首次显示前初始化不可见 HWND。");
                probe.Show();
                Must(new WindowInteropHelper(probe).Handle == preparedHandle,
                    "首次显示应复用已设置尺寸容量的 HWND。");
                probe.Width = 1165;
                probe.Height = 1008;
                WindowValidationInput.Pump(probe.Dispatcher);
                Must(
                    Math.Abs(probe.ActualWidth - Math.Min(1165, capacity.Width)) <= 0.5 &&
                    Math.Abs(probe.ActualHeight - Math.Min(1008, capacity.Height)) <= 0.5,
                    $"原生尺寸夹具未生效：容量 {capacity}，实际 {probe.ActualWidth}x{probe.ActualHeight}。");
            }
            finally
            {
                probe.Close();
            }
        }

        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-workarea-fit-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var seedBase = new DateTimeOffset(2038, 7, 9, 1, 0, 0, TimeSpan.Zero);
            for (var index = 0; index < 3; index++)
            {
                MustApply(workspace, new SaveCountdownChange(
                    null,
                    $"工作区适配倒计时 {index + 1}",
                    seedBase.AddDays(index + 1),
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
            }

            using var calendar = new HolidayCalendar(assetRoot);
            var logger = new DiagnosticLogger(diagnosticPath);
            var workAreaCases = new[]
            {
                (Width: 1920.0, Height: 1032.0, Dpi: 1.0),
                (Width: 1536.0, Height: 826.0, Dpi: 1.25),
                (Width: 1280.0, Height: 688.0, Dpi: 1.5)
            };
            foreach (var workAreaCase in workAreaCases)
            {
                using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                    new MonitorWorkArea(
                        new Rect(0, 0, workAreaCase.Width, workAreaCase.Height),
                        workAreaCase.Dpi,
                        workAreaCase.Dpi));
                Must(
                    Math.Abs(MonitorWorkAreaResolver.ResolveForScreenRect(new Rect(100, 100, 200, 200), 1, 1).DpiScaleX - workAreaCase.Dpi) <= 0.001,
                    "注入的工作区解析应按注入值返回 DIP 矩形与 DPI。");
                var main = new MainWindow(workspace, calendar, logger, assetRoot);
                PrepareOffscreenValidationWindow(main);
                using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
                try
                {
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var usableWidth = workAreaCase.Width - 24;
                    var usableHeight = workAreaCase.Height - 24;
                    var expectedWidth = Math.Min(MainWindow.PreferredWidth, usableWidth);
                    var expectedHeight = Math.Min(MainWindow.PreferredHeight, usableHeight);
                    Must(
                        Math.Abs(main.Width - expectedWidth) <= 0.5 &&
                        Math.Abs(main.Height - expectedHeight) <= 0.5,
                        $"注入 {workAreaCase.Width}x{workAreaCase.Height}@{workAreaCase.Dpi} 工作区时窗口应取首选与工作区−安全边距的较小值" +
                        $"（期望 {expectedWidth}x{expectedHeight}，实际 {main.Width}x{main.Height}）。");
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.Width <= workAreaCase.Width - 12 + 0.5 &&
                        main.Top + main.Height <= workAreaCase.Height - 12 + 0.5,
                        $"窗口四角应完全落在注入工作区安全边距内（实际 Left={main.Left} Top={main.Top} " +
                        $"Right={main.Left + main.Width} Bottom={main.Top + main.Height}）。");

                    foreach (var controlName in new[] { "TopmostCheck", "MinimizeButton", "CloseButton" })
                    {
                        var control = (FrameworkElement)main.FindName(controlName);
                        var controlBounds = control.TransformToAncestor(main).TransformBounds(
                            new Rect(new Point(0, 0), control.RenderSize));
                        Must(
                            controlBounds.X >= -0.5 &&
                            controlBounds.Y >= -0.5 &&
                            controlBounds.Right <= main.ActualWidth + 0.5 &&
                            controlBounds.Bottom <= main.ActualHeight + 0.5,
                            $"{controlName} 应无需滚动完整落在窗口可视区内（{controlBounds}，窗口 {main.ActualWidth}x{main.ActualHeight}）。");
                    }

                    var scrollHost = (ScrollViewer)main.FindName("MainWindowScrollHost");
                    var fitsPreferredHeight = MainWindow.PreferredHeight <= usableHeight + 0.5;
                    Must(
                        fitsPreferredHeight
                            ? scrollHost.ScrollableHeight <= 0.5
                            : scrollHost.ScrollableHeight > 0.5 ||
                                ((ScrollViewer)main.FindName("CountdownListRegion")).ViewportHeight >= 186,
                        fitsPreferredHeight
                            ? $"首选高度装得进工作区时窗口 {main.ActualWidth}x{main.ActualHeight} 应一屏放下、无纵向滚动（可滚 {scrollHost.ScrollableHeight}）。"
                            : $"窗口收缩时列表保有最小可读视口，必要时外层纵向滚动，实际 {main.ActualWidth}x{main.ActualHeight}。");
                    scrollHost.ScrollToBottom();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    var statusBar = (FrameworkElement)main.FindName("MainStatusBarSurface");
                    var statusBounds = statusBar.TransformToAncestor(main).TransformBounds(
                        new Rect(new Point(0, 0), statusBar.RenderSize));
                    Must(
                        statusBounds.Y >= -0.5 &&
                        statusBounds.Bottom <= main.ActualHeight + 0.5,
                        $"滚到底后状态栏应完整落入窗口可视区（{statusBounds}，窗口高 {main.ActualHeight}）。");
                    scrollHost.ScrollToTop();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var listRegion = (ScrollViewer)main.FindName("CountdownListRegion");
                    var rows = (Panel)main.FindName("CountdownRows");
                    var firstRows = rows.Children.OfType<FrameworkElement>().Take(3).ToArray();
                    Must(firstRows.Length == 3, "倒计时列表种子应提供至少 3 行。");
                    for (var rowIndex = 0; rowIndex < firstRows.Length; rowIndex++)
                    {
                        firstRows[rowIndex].BringIntoView();
                        WindowValidationInput.Pump(main.Dispatcher);
                        main.UpdateLayout();
                        var rowBounds = firstRows[rowIndex].TransformToAncestor(listRegion).TransformBounds(
                            new Rect(new Point(0, 0), firstRows[rowIndex].RenderSize));
                        Must(
                            rowBounds.Y >= -0.5 &&
                            rowBounds.Bottom <= listRegion.ViewportHeight + 0.5,
                            $"第 {rowIndex + 1} 行倒计时应能滚动到在内层视口完整可见" +
                            $"（行 {rowBounds}，内层视口高 {listRegion.ViewportHeight}）。");
                    }

                    var scaleViewport = (FrameworkElement)main.FindName("MainScaleViewport");
                    Must(
                        Math.Abs(scaleViewport.ActualWidth - MainWindow.PreferredWidth) <= 0.5 &&
                        scaleViewport.LayoutTransform.Value.IsIdentity &&
                        Math.Abs(((TextBox)main.FindName("CountdownName")).FontSize -
                            (double)main.FindResource("TypeBodyFontSize")) <= 0.01,
                        "窗口收缩时业务内容不得缩放：宽度保持 864 DIP、字号不变，列表使用剩余高度。");
                }
                finally
                {
                    main.Close();
                }
            }

            using (var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                       new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1)))
            {
                var main = new MainWindow(workspace, calendar, logger, assetRoot);
                PrepareOffscreenValidationWindow(main);
                using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
                try
                {
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var handle = new WindowInteropHelper(main).Handle;
                    Must(handle != IntPtr.Zero, "窗口应已创建 HWND。");
                    // 真实用户拖动/缩放手势走 WM_ENTERSIZEMOVE→几何变化→WM_EXITSIZEMOVE；
                    // 探针沿同一 Win32 路径标记「用户调整过位置和尺寸」。
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmEnterSizeMove,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    main.Width = 720;
                    main.Height = 560;
                    main.Left = 500;
                    main.Top = 300;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmExitSizeMove,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Hide();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        Math.Abs(main.Width - 720) <= 0.5 &&
                        Math.Abs(main.Height - 560) <= 0.5 &&
                        Math.Abs(main.Left - 500) <= 0.5 &&
                        Math.Abs(main.Top - 300) <= 0.5,
                        $"隐藏后再显示应保留本次运行内用户调整的位置和尺寸（实际 Left={main.Left} Top={main.Top} {main.Width}x{main.Height}）。");

                    var scrollHost = (ScrollViewer)main.FindName("MainWindowScrollHost");
                    Must(
                        scrollHost.ScrollableWidth > 0.5 && scrollHost.ScrollableHeight > 0.5,
                        "最小尺寸 720×560 下外层容器应纵横双向可滚动（横向兜底），内容不被裁切。");

                    main.Left = 5000;
                    main.Top = 5000;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Hide();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.ActualWidth <= 1920 - 12 + 0.5 &&
                        main.Top + main.ActualHeight <= 1032 - 12 + 0.5,
                        $"越界位置在显示时应夹回所在显示器工作区（实际 Left={main.Left} Top={main.Top}）。");

                    using var smallerOverride = MonitorWorkAreaResolver.OverrideForValidation(
                        new MonitorWorkArea(new Rect(0, 0, 1280, 688), 1.5, 1.5));
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmDisplayChange,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.ActualWidth <= 1280 - 12 + 0.5 &&
                        main.Top + main.ActualHeight <= 688 - 12 + 0.5 &&
                        Math.Abs(main.Width - 720) <= 0.5 &&
                        Math.Abs(main.Height - 560) <= 0.5,
                        $"显示器工作区变化后窗口应夹回新工作区且保留用户尺寸" +
                        $"（实际 Left={main.Left} Top={main.Top} {main.Width}x{main.Height}）。");
                }
                finally
                {
                    main.Close();
                }
            }

            return true;
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #211：标题拖动区覆盖整个身份区空白，身份区内控件仍可点击。经真实
    // WM_NCHITTEST 路径判定：身份区中缝空白返回 HTCAPTION，置顶控件中心返回 HTCLIENT。
    public static bool RunMainWindowIdentityZoneDragBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-identity-drag-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new AdvancingValidationClock(
                    new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero))));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 2560, 1600), 1, 1));
            var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();

                var identityZone = (FrameworkElement)main.FindName("MainIdentityZone");
                var titleZone = (FrameworkElement)main.FindName("MainTitleZone");
                var windowControls = (FrameworkElement)main.FindName("MainWindowControls");
                var topmostCheck = (FrameworkElement)main.FindName("TopmostCheck");

                Rect BoundsInMain(FrameworkElement element) =>
                    element.TransformToAncestor(main).TransformBounds(
                        new Rect(new Point(0, 0), element.RenderSize));
                var identityBounds = BoundsInMain(identityZone);
                var titleBounds = BoundsInMain(titleZone);
                var controlsBounds = BoundsInMain(windowControls);
                var emptyX = (titleBounds.Right + controlsBounds.Left) / 2;
                var emptyY = identityBounds.Top + (identityBounds.Height / 2);
                Must(
                    emptyX > titleBounds.Right + 8 &&
                    emptyX < controlsBounds.Left - 8 &&
                    emptyY > identityBounds.Top + 8 &&
                    emptyY < identityBounds.Bottom - 8,
                    $"探针空白点应落在身份区中缝（x={emptyX} 应位于标题右缘 {titleBounds.Right} 与控件左缘 {controlsBounds.Left} 之间）。");

                var emptyHit = SendWindowHitTest(main, new Point(emptyX, emptyY));
                Must(
                    emptyHit == NativeWorkAreaProbe.HitCaption,
                    $"身份区空白处应作为标题拖动区（HTCAPTION=2），实际命中 {emptyHit}。");

                var topmostCenter = topmostCheck.TransformToAncestor(main).Transform(
                    new Point(topmostCheck.RenderSize.Width / 2, topmostCheck.RenderSize.Height / 2));
                var controlHit = SendWindowHitTest(main, topmostCenter);
                Must(
                    controlHit == NativeWorkAreaProbe.HitClient,
                    $"身份区内置顶控件应保持可点击（HTCLIENT=1），实际命中 {controlHit}。");
                return true;
            }
            finally
            {
                main.Close();
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static int SendWindowHitTest(Window window, Point dipPoint)
    {
        var screenPoint = window.PointToScreen(dipPoint);
        var coordinates = new IntPtr(((int)screenPoint.Y << 16) | ((int)screenPoint.X & 0xffff));
        var handle = new WindowInteropHelper(window).Handle;
        return NativeWorkAreaProbe
            .SendMessage(handle, NativeWorkAreaProbe.WmNcHitTest, IntPtr.Zero, coordinates)
            .ToInt32();
    }

    internal static IDisposable OverrideWindowTrackingCapacityForValidation(Window window, Size capacity) =>
        NativeWorkAreaProbe.OverrideTrackingCapacity(window, capacity);

    private static class NativeWorkAreaProbe
    {
        private const int WmGetMinMaxInfo = 0x0024;
        internal const int WmNcHitTest = 0x0084;
        internal const int WmDisplayChange = 0x007E;
        internal const int WmEnterSizeMove = 0x0231;
        internal const int WmExitSizeMove = 0x0232;
        internal const int HitClient = 1;
        internal const int HitCaption = 2;

        internal static IDisposable OverrideTrackingCapacity(Window window, Size capacity) =>
            new TrackingCapacityScope(window, capacity);

        private sealed class TrackingCapacityScope : IDisposable
        {
            private readonly Window _window;
            private readonly Size _capacity;
            private HwndSource? _source;

            internal TrackingCapacityScope(Window window, Size capacity)
            {
                _window = window;
                _capacity = capacity;
                window.SourceInitialized += OnSourceInitialized;
                try
                {
                    // Show raises visibility before creating its HWND. Initialize
                    // the native limits before business visibility handlers run.
                    new WindowInteropHelper(window).EnsureHandle();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            private void OnSourceInitialized(object? sender, EventArgs args)
            {
                var handle = new WindowInteropHelper(_window).Handle;
                _source = HwndSource.FromHwnd(handle);
                _source.AddHook(ApplyCapacity);
                // WPF cached the real desktop limit during HWND creation, before
                // SourceInitialized. Feed the fixture limit through its normal
                // WM_GETMINMAXINFO path before validation applies its sizes.
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
                try
                {
                    Marshal.StructureToPtr(new MinMaxInfo(), pointer, false);
                    SendMessage(handle, WmGetMinMaxInfo, IntPtr.Zero, pointer);
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            private IntPtr ApplyCapacity(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message == WmGetMinMaxInfo)
                {
                    var dpi = VisualTreeHelper.GetDpi(_window);
                    var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    limits.MaxTrackX = (int)Math.Ceiling(_capacity.Width * dpi.DpiScaleX);
                    limits.MaxTrackY = (int)Math.Ceiling(_capacity.Height * dpi.DpiScaleY);
                    Marshal.StructureToPtr(limits, lParam, false);
                    // Leave unhandled: WPF must consume the injected limits.
                }
                return IntPtr.Zero;
            }

            public void Dispose()
            {
                _window.SourceInitialized -= OnSourceInitialized;
                _source?.RemoveHook(ApplyCapacity);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public int ReservedX, ReservedY;
            public int MaxSizeX, MaxSizeY;
            public int MaxPositionX, MaxPositionY;
            public int MinTrackX, MinTrackY;
            public int MaxTrackX, MaxTrackY;
        }

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "SendMessageW")]
        internal static extern IntPtr SendMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);
    }

    private static bool WindowIntersectsWorkArea(Window window)
    {
        var area = MonitorWorkAreaResolver.ResolveForWindow(window).WorkAreaDip;
        return window.Left < area.Right &&
            window.Left + window.ActualWidth > area.Left &&
            window.Top < area.Bottom &&
            window.Top + window.ActualHeight > area.Top;
    }

    // #156：沿原始视图递归窗口自动化树，断言给定元素不再可达（退出朗读的强证据）。
    private static bool AutomationRawTreeContains(
        System.Windows.Automation.Peers.AutomationPeer root,
        IReadOnlyList<FrameworkElement> targets)
    {
        var visited = 0;
        var queue = new Queue<System.Windows.Automation.Peers.AutomationPeer>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            if (++visited > 4_000)
            {
                // 超出遍历上限视为「不可证明缺席」，按包含处理让门禁红出来，而不是静默放过。
                return true;
            }

            var current = queue.Dequeue();
            var owner = (current as System.Windows.Automation.Peers.UIElementAutomationPeer)?.Owner;
            if (owner is not null && targets.Any(target => ReferenceEquals(target, owner)))
            {
                return true;
            }

            IReadOnlyList<System.Windows.Automation.Peers.AutomationPeer>? children;
            try
            {
                children = current?.GetChildren();
            }
            catch (System.Windows.Automation.ElementNotAvailableException)
            {
                continue;
            }

            if (children is null)
            {
                continue;
            }

            foreach (var child in children)
            {
                queue.Enqueue(child);
            }
        }

        return false;
    }

    private static void ProbeVisibleReminderDates(
        DesktopWorkspace workspace, HolidayCalendar calendar, MainWindow main, NoteRecord note, DateTimeOffset dueUtc)
    {
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        var center = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(floating);
        PrepareOffscreenValidationWindow(center);
        var key = new ActiveReminderOccurrenceKey("visible-date-probe", dueUtc);
        center.AddEntry(new ReminderCenterItem(key, note, 0, 0));
        try
        {
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            center.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(VisualDescendants<TextBlock>(floating).Any(text => text.IsVisible && text.Text == "下次提醒：7 月 8 日 09:02"),
                "#225 浮窗便签下次提醒应使用带中文空格的截止时刻格式。");
            Must(VisualDescendants<TextBlock>(center).Any(text => text.IsVisible && text.Text == "计划：2038 年 7 月 8 日 09:02（北京时间）") &&
                VisualDescendants<FrameworkElement>(center).Any(element =>
                    AutomationProperties.GetName(element).Contains("计划 2038 年 7 月 8 日 09:02 北京时间", StringComparison.Ordinal)),
                "#225 提醒中心可见计划与可访问名应使用同一完整北京时间。");
        }
        finally
        {
            center.RemoveEntry(key);
            center.Close();
            floating.Close();
        }
    }
}
