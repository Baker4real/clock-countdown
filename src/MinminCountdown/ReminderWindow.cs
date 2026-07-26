using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class ReminderWindow : Window
{
    internal bool SupportsKeyboardDismissal => true;

    public ReminderWindow(NoteRecord note)
    {
        Title = "便签提醒";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        var dismiss = UiAccessibility.Name(
            new Button
            {
                Content = "知道了",
                MinWidth = 82,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = (Style)FindResource("PrimaryButton")
            },
            "关闭便签提醒");
        dismiss.Click += (_, _) => Close();

        var stack = new StackPanel { Margin = new Thickness(20) };
        var heading = new TextBlock
        {
            Text = "便签提醒",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        stack.Children.Add(heading);
        var body = new TextBlock
        {
            Text = note.Text,
            Margin = new Thickness(0, 10, 0, 16),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "BodyBrush");
        stack.Children.Add(body);
        stack.Children.Add(dismiss);
        var surface = new Border
        {
            Child = stack,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        surface.SetResourceReference(Border.BackgroundProperty, "NoteBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "NoteLineBrush");
        Content = surface;
        AutomationProperties.SetName(this, $"便签提醒：{note.Text}");
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Close();
                args.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 18;
            Top = workArea.Top + 18;
        };
    }
}
