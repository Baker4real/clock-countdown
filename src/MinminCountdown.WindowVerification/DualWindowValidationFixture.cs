using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using MinminCountdown.Core;

namespace MinminCountdown;

// #256：只拥有独立双窗场景的资源边界；种子数据、用户动作、观察与断言仍由场景负责。
// Run 而非 using，确保清理失败不会覆盖原有行为失败，且构造未返回的窗口也能被关闭。
internal sealed class DualWindowValidationFixture
{
    private readonly string _scenario;
    private readonly List<Window> _windows = [];
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private HolidayCalendar? _calendar;
    private MainWindow? _main;
    private FloatingWindow? _floating;

    private DualWindowValidationFixture(string scenario)
    {
        _scenario = scenario;
        RootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"MinminCountdown-dual-window-{Guid.NewGuid():N}"));
        SettingsPath = Path.Combine(RootPath, "settings.json");
        _directories.Add(RootPath);
        _directories.Add(SettingsPath + ".tmp");
        foreach (var suffix in new[] { "", ".tmp", ".backup1", ".backup2", ".backup2.tmp", ".backup2.rollback" })
            _files.Add(SettingsPath + suffix);
        foreach (var suffix in new[] { "", ".1", ".2", ".3" })
            _files.Add(Path.Combine(RootPath, "application.jsonl") + suffix);
    }

    internal string RootPath { get; }
    internal string SettingsPath { get; }
    internal DesktopWorkspace Workspace { get; private set; } = null!;
    internal DiagnosticLogger Logger { get; private set; } = null!;
    internal HolidayCalendar Calendar => _calendar ?? throw new InvalidOperationException("fixture日历尚未创建。");
    internal MainWindow Main => _main ?? throw new InvalidOperationException("请先调用fixture.ShowWindows。");
    internal FloatingWindow Floating => _floating ?? throw new InvalidOperationException("请先调用fixture.ShowWindows。");

    internal static T Run<T>(
        string scenario,
        Func<DualWindowValidationFixture, T> exercise,
        SettingsStore? store = null,
        CountdownSettings? settings = null)
    {
        var fixture = new DualWindowValidationFixture(scenario);
        T result = default!;
        Exception? failure = null;
        try
        {
            fixture.Initialize(store ?? new SettingsStore(), settings);
            result = exercise(fixture);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        fixture.EnsureCleanup(failure);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private void Initialize(SettingsStore store, CountdownSettings? settings)
    {
        try
        {
            Directory.CreateDirectory(RootPath);
            if (settings is not null) store.Save(SettingsPath, settings);
            Workspace = DesktopWorkspace.Load(SettingsPath, store);
            Logger = new DiagnosticLogger(Path.Combine(RootPath, "application.jsonl"));
            // 初始状态仍为未就绪；需要刷新日历的场景走真实日历与私有缓存，但不会触网或共享应用缓存。
            _calendar = new HolidayCalendar(RootPath,
                static (_, _) => Task.FromException<string>(new HttpRequestException("fixture offline")));
        }
        catch (Exception exception)
        {
            throw new DualWindowFixtureException("setup", _scenario, RootPath, exception, []);
        }
    }

    internal void ShowWindows(WorkspaceKind workspace = WorkspaceKind.Countdowns, string? assetRoot = null)
    {
        if (_windows.Count != 0) throw new InvalidOperationException("双窗fixture只能构造一次窗口。");
        try
        {
            var assets = assetRoot ?? AppContext.BaseDirectory;
            _main = CreateWindow(() => new MainWindow(Workspace, Calendar, Logger, assets));
            _floating = CreateWindow(() => new FloatingWindow(Workspace, Calendar, Main, assets));
            foreach (var window in _windows)
            {
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20_000;
                window.Top = -20_000;
                window.Opacity = 0;
            }
            Main.Show();
            Floating.Show();
            Main.ShowWorkspace(workspace);
            Floating.ShowWorkspace(workspace);
            WindowValidationInput.Pump(Main.Dispatcher);
        }
        catch (Exception exception)
        {
            throw new DualWindowFixtureException("setup", _scenario, RootPath, exception, []);
        }
    }

    private T CreateWindow<T>(Func<T> create) where T : Window
    {
        var previous = Application.Current.Windows.Cast<Window>().ToHashSet();
        try { return create(); }
        finally
        {
            // WPF在Window基类构造时即登记实例；派生构造抛异常时没有返回值，仍属于本fixture。
            _windows.AddRange(Application.Current.Windows.Cast<Window>().Where(window => window is T && !previous.Contains(window)));
        }
    }

    // 场景声明它确实创建的额外输入；清理只访问这些确切文件与父目录，不枚举或递归删除未知残留。
    internal string WriteTemporaryFile(string relativePath, string contents)
    {
        var path = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        if (Path.IsPathRooted(relativePath) ||
            !path.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("fixture文件必须位于自己的临时根内。", nameof(relativePath));
        _files.Add(path);
        for (var parent = Path.GetDirectoryName(path);
             parent is not null && !string.Equals(parent, RootPath, StringComparison.OrdinalIgnoreCase);
             parent = Path.GetDirectoryName(parent))
            _directories.Add(parent);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private void EnsureCleanup(Exception? originalFailure)
    {
        var failures = Cleanup();
        if (failures.Count > 0)
            throw new DualWindowFixtureException("cleanup", _scenario, RootPath, originalFailure, failures,
                () => EnsureCleanup(originalFailure));
    }

    private List<Exception> Cleanup()
    {
        var failures = new List<Exception>();
        void Attempt(string resource, Action release)
        {
            try { release(); }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException($"{resource}清理失败。", exception));
            }
        }
        foreach (var window in _windows.AsEnumerable().Reverse())
        {
            Attempt(window.GetType().Name, () =>
            {
                if (!Application.Current.Windows.Cast<Window>().Contains(window)) return;
                window.Close();
                if (Application.Current.Windows.Cast<Window>().Contains(window))
                    throw new InvalidOperationException("窗口关闭被取消，窗口仍然存活。");
            });
        }
        Attempt("日历", () => _calendar?.Dispose());
        Attempt("Dispatcher卸载队列", () => WindowValidationInput.Pump(Application.Current.Dispatcher));
        foreach (var path in _files)
            Attempt(path, () => { if (File.Exists(path)) File.Delete(path); });
        foreach (var path in _directories.OrderByDescending(path => path.Length))
            Attempt(path, () => { if (Directory.Exists(path)) Directory.Delete(path, recursive: false); });
        return failures;
    }
}

internal sealed class DualWindowFixtureException : InvalidOperationException
{
    private readonly Action? _retryCleanup;

    internal DualWindowFixtureException(string phase, string scenario, string rootPath,
        Exception? scenarioFailure, IReadOnlyList<Exception> cleanupFailures, Action? retryCleanup = null)
        : base($"双窗fixture {phase}失败；scenario={scenario}; root={rootPath}; " +
               $"cleanupFailures={cleanupFailures.Count}; scenarioFailure={scenarioFailure?.Message ?? "none"}",
            cleanupFailures.Count == 0 ? scenarioFailure : new AggregateException(
                (scenarioFailure is null ? Array.Empty<Exception>() : new[] { scenarioFailure }).Concat(cleanupFailures)))
    {
        Phase = phase;
        RootPath = rootPath;
        ScenarioFailure = scenarioFailure;
        CleanupFailures = cleanupFailures;
        _retryCleanup = retryCleanup;
    }

    internal void RetryCleanup() => (_retryCleanup ?? throw new InvalidOperationException("此异常没有待重试的fixture清理。"))();

    internal string Phase { get; }
    internal string RootPath { get; }
    internal Exception? ScenarioFailure { get; }
    internal IReadOnlyList<Exception> CleanupFailures { get; }
}
