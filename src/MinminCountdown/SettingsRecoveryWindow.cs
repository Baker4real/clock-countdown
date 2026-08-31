using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class SettingsRecoveryWindow : Window
{
    private static readonly SolidColorBrush CanvasBrush = new(Color.FromRgb(0xFF, 0xFB, 0xF7));
    private static readonly SolidColorBrush InkBrush = new(Color.FromRgb(0x4A, 0x2D, 0x24));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x87, 0x66, 0x5A));
    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0xD5, 0x5C, 0x47));

    private SettingsRecoveryDecision _decision = SettingsRecoveryDecision.Exit;

    internal SettingsRecoveryDecision DecisionForValidation => _decision;

    private SettingsRecoveryWindow(string message, bool canRecover)
    {
        Title = "设置恢复";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinHeight = 230;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = CanvasBrush;
        Foreground = InkBrush;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var body = new StackPanel
        {
            Margin = new Thickness(28)
        };
        body.Children.Add(new TextBlock
        {
            Text = "设置恢复",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush
        });
        body.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 14, 0, 22),
            FontSize = 16,
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush
        });

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (canRecover)
        {
            var recover = CreateButton("恢复备份", isPrimary: true);
            recover.IsDefault = true;
            recover.Click += (_, _) =>
            {
                _decision = SettingsRecoveryDecision.RecoverBackup;
                DialogResult = true;
            };
            AutomationProperties.SetName(recover, "恢复备份");
            commands.Children.Add(recover);
        }

        var exit = CreateButton("退出", isPrimary: false);
        exit.IsCancel = true;
        exit.Click += (_, _) =>
        {
            _decision = SettingsRecoveryDecision.Exit;
            DialogResult = false;
        };
        AutomationProperties.SetName(exit, "退出");
        commands.Children.Add(exit);
        body.Children.Add(commands);
        Content = body;
    }

    public static SettingsRecoveryDecision Choose(SettingsRecoveryPrompt prompt)
    {
        var window = new SettingsRecoveryWindow(
            $"主设置无法读取或未通过严格校验。检测到第 {prompt.BackupGeneration} 代合法备份；请选择恢复备份或退出。",
            canRecover: true);
        window.ShowDialog();
        return window._decision;
    }

    internal static SettingsRecoveryWindow CreateForValidation(string message, bool canRecover) =>
        new(message, canRecover);

    public static void ShowFailure(string message)
    {
        var window = new SettingsRecoveryWindow(message, canRecover: false);
        window.ShowDialog();
    }

    private static Button CreateButton(string text, bool isPrimary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 112,
            Height = 44,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(16, 0, 16, 0),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = isPrimary ? Brushes.White : InkBrush,
            Background = isPrimary ? AccentBrush : Brushes.White,
            BorderBrush = isPrimary ? AccentBrush : MutedBrush,
            BorderThickness = new Thickness(1)
        };
    }
}

internal sealed class InstanceActivationFailureWindow : Window
{
    internal const string PromptTitle = "敏敏子倒计时";
    internal const string PromptHeading = "主程序未响应";
    internal const string PromptMessage = "请稍后重试，或检查已在运行的倒计时程序状态。";
    internal const string DismissButtonText = "知道了";

    private static readonly SolidColorBrush CanvasBrush = new(Color.FromRgb(0xFF, 0xFB, 0xF7));
    private static readonly SolidColorBrush InkBrush = new(Color.FromRgb(0x4A, 0x2D, 0x24));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x87, 0x66, 0x5A));
    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0xD5, 0x5C, 0x47));

    private InstanceActivationFailureWindow()
    {
        Title = PromptTitle;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinHeight = 200;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = CanvasBrush;
        Foreground = InkBrush;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var body = new StackPanel
        {
            Margin = new Thickness(28)
        };
        body.Children.Add(new TextBlock
        {
            Text = PromptHeading,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush
        });
        body.Children.Add(new TextBlock
        {
            Text = PromptMessage,
            Margin = new Thickness(0, 14, 0, 22),
            FontSize = 16,
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush
        });

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var dismiss = new Button
        {
            Content = DismissButtonText,
            MinWidth = 112,
            Height = 44,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(16, 0, 16, 0),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = AccentBrush,
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(1),
            IsDefault = true,
            IsCancel = true
        };
        dismiss.Click += (_, _) => Close();
        AutomationProperties.SetName(dismiss, DismissButtonText);
        commands.Children.Add(dismiss);
        body.Children.Add(commands);
        Content = body;
    }

    public static void ShowPrompt() => new InstanceActivationFailureWindow().ShowDialog();

    internal static InstanceActivationFailureWindow CreateForValidation() => new();
}

internal static class SettingsRecoveryFailurePresentation
{
    public static bool TryPresent(
        SettingsRecoveryException exception,
        Action<string> showFailure)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(showFailure);
        if (exception is SettingsRecoveryDeclinedException)
        {
            return false;
        }

        showFailure(exception.Message);
        return true;
    }
}
