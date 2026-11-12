using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using MinminCountdown;

namespace MinminCountdown.WindowVerification;

internal static class Program
{
    internal const string OverflowFactName = "MainListOverflowScrollsAndReachesActions";
    private const string OverflowFactMeaning =
        "两个列表真实溢出时保留滚动条，滚轮、thumb 与 PageDown 能滚动并到底，末行与每行操作都能滚进视口（#248）";

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        MainListOverflowProbe probe;
        try
        {
            var app = new App();
            app.InitializeComponent();
            var productionStartup = typeof(App).GetMethod(
                "OnStartup",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?.CreateDelegate<StartupEventHandler>(app)
                ?? throw new MissingMethodException(typeof(App).FullName, "OnStartup");
            // App.xaml 的事件接线只服务生产宿主；独立 runner 加载资源后解除它，避免误进生产启动。
            app.Startup -= productionStartup;
            using var theme = new ApplicationTheme(app.Resources);
            theme.Apply();
            probe = EmptyStateOverflowScenario.Run();
        }
        catch (Exception exception)
        {
            probe = MainListOverflowProbe.Failure(exception.ToString());
        }

        var passed = probe.AllPassed;
        Console.Out.Write(JsonSerializer.Serialize(new Dictionary<string, bool>
        {
            [OverflowFactName] = passed
        }));
        if (!passed)
        {
            Console.Error.WriteLine(probe.Explain(OverflowFactMeaning));
        }

        return passed ? 0 : 1;
    }
}
