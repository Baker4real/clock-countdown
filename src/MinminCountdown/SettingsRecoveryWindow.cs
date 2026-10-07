using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class SettingsRecoveryWindow : Window
{
    // #150：静态刷是现役主题色板的副本（Canvas #FDF3EB / Ink #3B190D / Muted #82665F /
    // MainAction #C23E68 / MainActionText #FFFDFC / PeachLine #F6DFD3），
    // 允许在主题尚未 Apply 时渲染；探针把它们与活体主题 token 逐值核对。
    private static readonly SolidColorBrush CanvasBrush = new(Color.FromRgb(0xFD, 0xF3, 0xEB));
    private static readonly SolidColorBrush InkBrush = new(Color.FromRgb(0x3B, 0x19, 0x0D));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x82, 0x66, 0x5F));
    private static readonly SolidColorBrush MainActionBrush = new(Color.FromRgb(0xC2, 0x3E, 0x68));
    private static readonly SolidColorBrush MainActionTextBrush = new(Color.FromRgb(0xFF, 0xFD, 0xFC));
    private static readonly SolidColorBrush ShellLineBrush = new(Color.FromRgb(0xF6, 0xDF, 0xD3));

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
        // #150：与提醒中心同一套无边框暖粉壳（圆角 + 1px 暖边），不掉回系统默认窗口铬。
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = InkBrush;
        SetResourceReference(Window.FontFamilyProperty, "UiFontFamily");

        var heading = new TextBlock
        {
            Text = "设置恢复",
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush
        };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowTitleFontSize");
        var messageText = new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 14, 0, 22),
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush
        };
        messageText.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowBodyFontSize");

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
        if (!canRecover)
        {
            exit.IsDefault = true;
        }
        exit.Click += (_, _) =>
        {
            _decision = SettingsRecoveryDecision.Exit;
            DialogResult = false;
        };
        AutomationProperties.SetName(exit, "退出");
        commands.Children.Add(exit);
        var body = new StackPanel
        {
            Margin = new Thickness(28)
        };
        AutomationProperties.SetAutomationId(body, "SettingsRecoveryContentRoot");
        body.SetResourceReference(TextElement.FontSizeProperty, "SecondaryWindowBodyFontSize");
        body.Children.Add(heading);
        body.Children.Add(messageText);
        body.Children.Add(commands);
        Content = CreateWarmShell(body);
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

    private static Border CreateWarmShell(StackPanel body) =>
        new()
        {
            Child = body,
            Background = CanvasBrush,
            BorderBrush = ShellLineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

    private static Button CreateButton(string text, bool isPrimary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 112,
            Height = 44,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(16, 0, 16, 0),
            FontWeight = FontWeights.SemiBold,
            Foreground = isPrimary ? MainActionTextBrush : InkBrush,
            Background = isPrimary ? MainActionBrush : CanvasBrush,
            BorderBrush = isPrimary ? MainActionBrush : MutedBrush,
            BorderThickness = new Thickness(1)
        };
        button.SetResourceReference(Control.FontSizeProperty, "SecondaryWindowMinorFontSize");
        return button;
    }
}

internal sealed class InstanceActivationFailureWindow : Window
{
    internal const string PromptTitle = "敏敏子倒计时";
    internal const string PromptHeading = "主程序未响应";
    internal const string PromptMessage = "请稍后重试，或检查已在运行的倒计时程序状态。";
    internal const string DismissButtonText = "知道了";

    private static readonly SolidColorBrush CanvasBrush = new(Color.FromRgb(0xFD, 0xF3, 0xEB));
    private static readonly SolidColorBrush InkBrush = new(Color.FromRgb(0x3B, 0x19, 0x0D));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x82, 0x66, 0x5F));
    private static readonly SolidColorBrush MainActionBrush = new(Color.FromRgb(0xC2, 0x3E, 0x68));
    private static readonly SolidColorBrush MainActionTextBrush = new(Color.FromRgb(0xFF, 0xFD, 0xFC));
    private static readonly SolidColorBrush ShellLineBrush = new(Color.FromRgb(0xF6, 0xDF, 0xD3));

    private InstanceActivationFailureWindow()
    {
        Title = PromptTitle;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinHeight = 200;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        // #150：与提醒中心同一套无边框暖粉壳（圆角 + 1px 暖边），不掉回系统默认窗口铬。
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = InkBrush;
        SetResourceReference(Window.FontFamilyProperty, "UiFontFamily");

        var heading = new TextBlock
        {
            Text = PromptHeading,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush
        };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowTitleFontSize");
        var messageText = new TextBlock
        {
            Text = PromptMessage,
            Margin = new Thickness(0, 14, 0, 22),
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush
        };
        messageText.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowBodyFontSize");

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
            FontWeight = FontWeights.SemiBold,
            Foreground = MainActionTextBrush,
            Background = MainActionBrush,
            BorderBrush = MainActionBrush,
            BorderThickness = new Thickness(1),
            IsDefault = true,
            IsCancel = true
        };
        dismiss.SetResourceReference(Control.FontSizeProperty, "SecondaryWindowMinorFontSize");
        dismiss.Click += (_, _) => Close();
        AutomationProperties.SetName(dismiss, DismissButtonText);
        commands.Children.Add(dismiss);
        var body = new StackPanel
        {
            Margin = new Thickness(28)
        };
        AutomationProperties.SetAutomationId(body, "ActivationFailureContentRoot");
        body.SetResourceReference(TextElement.FontSizeProperty, "SecondaryWindowBodyFontSize");
        body.Children.Add(heading);
        body.Children.Add(messageText);
        body.Children.Add(commands);
        Content = new Border
        {
            Child = body,
            Background = CanvasBrush,
            BorderBrush = ShellLineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
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
