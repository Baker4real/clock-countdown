using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MinminCountdown;

// Small real WPF fixtures at the production policy seam. The application Tab/image probes remain separate.
internal static class KeyboardFocusPolicyFixtures
{
    internal static IReadOnlyDictionary<string, bool> Run()
    {
        var results = new Dictionary<string, bool>(StringComparer.Ordinal);
        var button = new Button { Content = "Action" };
        var check = new CheckBox { Content = "Option" };
        var text = new TextBox { Text = "Input" };
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
        var date = new DatePicker { SelectedDate = new DateTime(2031, 12, 31) };
        var income = new AutomationOptionalTextBlock { Text = "Income", IsInvokable = true };
        var list = new ScrollViewer { Height = 40, Content = new TextBlock { Text = "List" } };
        var status = new TextBlock { Text = "Status", Focusable = true };
        var panel = new StackPanel();
        FrameworkElement[] elements = [button, check, text, combo, date, income, list, status];
        foreach (var element in elements) panel.Children.Add(element);
        var window = new Window
        {
            Content = panel, Width = 360, Height = 600, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000, Top = -20_000, Opacity = 0
        };
        var ambientFocused = Keyboard.FocusedElement;
        var focusAnchor = new TextBox { Text = "Previous focus" };
        var anchorWindow = new Window
        {
            Content = focusAnchor, Width = 180, Height = 80, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000, Top = -20_000, Opacity = 0
        };
        IInputElement? previousFocused = null;
        try
        {
            // A deterministic real WPF focus anchor proves restoration instead of accepting a null focus start.
            anchorWindow.Show();
            anchorWindow.Activate();
            Keyboard.Focus(focusAnchor);
            WindowValidationInput.Pump(focusAnchor.Dispatcher);
            previousFocused = Keyboard.FocusedElement;
            results["PreviousKeyboardFocusPresent"] = ReferenceEquals(previousFocused, focusAnchor);
            try
            {
                window.Show();
                window.Activate();
                WindowValidationInput.Pump(window.Dispatcher);
                foreach (var element in elements) KeyboardFocusPolicy.Apply(element);
                var approvedAdorner = (Style)window.FindResource("MainKeyboardFocusVisual");
                var systemAdorner = (Style)window.FindResource(SystemParameters.FocusVisualStyleKey);
                results["TemplateAndInputControlsDoNotUseAdorners"] =
                    new FrameworkElement[] { button, check, text, combo, date, income }.All(element =>
                        KeyboardFocusPolicy.UsesTemplateOrHostStroke(element) && element.FocusVisualStyle is null);
                results["ListAndStatusUseSharedAdorner"] =
                    ReferenceEquals(list.FocusVisualStyle, approvedAdorner) &&
                    ReferenceEquals(status.FocusVisualStyle, approvedAdorner) &&
                    KeyboardFocusPolicy.HasValidAdornment(list) && KeyboardFocusPolicy.HasValidAdornment(status);

                Focus(button);
                results["KeyboardTransferLightsTemplateStroke"] = button.IsKeyboardFocused &&
                    FocusVisualState.GetIsKeyboardFocusVisible(button) && KeyboardFocusPolicy.KeyboardFocusDrawnOnce(button);
                Focus(text);
                button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                Focus(button);
                results["PointerTransferDoesNotLightTemplateStroke"] = button.IsKeyboardFocused &&
                    !FocusVisualState.GetIsKeyboardFocusVisible(button) && !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(button);
                Focus(text);
                Focus(button);
                results["KeyboardTransferAfterPointerLightsStroke"] = KeyboardFocusPolicy.KeyboardFocusDrawnOnce(button);

                foreach (var control in new Control[] { check, text, combo })
                {
                    Focus(control);
                    results[$"{control.GetType().Name}HasOneStroke"] = KeyboardFocusPolicy.KeyboardFocusDrawnOnce(control);
                    control.FocusVisualStyle = approvedAdorner;
                    results[$"{control.GetType().Name}RejectsSecondStroke"] = !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(control);
                    KeyboardFocusPolicy.Apply(control);
                }
                button.FocusVisualStyle = systemAdorner;
                results["SystemDottedButtonRejected"] = !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(button);
                KeyboardFocusPolicy.Apply(button);
                list.FocusVisualStyle = systemAdorner;
                results["SystemDottedListRejected"] = !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(list);
                KeyboardFocusPolicy.Apply(list);

                date.ApplyTemplate();
                var dateButton = date.Template?.FindName("PART_Button", date) as Control
                    ?? throw new InvalidOperationException("Date input fixture has no focusable calendar button.");
                Focus(dateButton);
                results["DateInputOwnsItsButtonStroke"] = dateButton.IsKeyboardFocused &&
                    !FocusVisualState.GetIsKeyboardFocusVisible(dateButton) &&
                    KeyboardFocusPolicy.UsesTemplateOrHostStroke(dateButton) &&
                    KeyboardFocusPolicy.KeyboardFocusDrawnOnce(dateButton);
                dateButton.FocusVisualStyle = approvedAdorner;
                results["DateInputRejectsButtonAdorner"] = !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(dateButton);
                KeyboardFocusPolicy.Apply(dateButton);
                FocusVisualState.SetIsKeyboardFocusVisible(dateButton, true);
                results["DateInputRejectsButtonTemplateStroke"] = !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(dateButton);
                KeyboardFocusPolicy.Clear(dateButton);

                Focus(income);
                results["IncomeKeyboardTransferLightsHostStroke"] = income.IsKeyboardFocused &&
                    KeyboardFocusPolicy.KeyboardFocusDrawnOnce(income);
                income.FocusVisualStyle = approvedAdorner;
                results["IncomeRejectsSecondStroke"] = !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(income);
                KeyboardFocusPolicy.Apply(income);
                Focus(button);
                KeyboardFocusPolicy.FocusFromPointer(income);
                WindowValidationInput.Pump(window.Dispatcher);
                results["IncomePointerTransferDoesNotLightStroke"] = income.IsKeyboardFocused &&
                    !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(income);
                var invocations = 0;
                income.Invoked += (_, _) => invocations++;
                WindowValidationInput.RaiseKey(income, Key.Space);
                results["IncomeKeyboardActionKeepsInvocationAndFocus"] = invocations == 1 &&
                    KeyboardFocusPolicy.KeyboardFocusDrawnOnce(income);
                Focus(button);
                results["IncomeFocusLossClearsStroke"] = !FocusVisualState.GetIsKeyboardFocusVisible(income);
                return results;
            }
            finally
            {
                window.Close();
                WindowValidationInput.Pump(window.Dispatcher);
                if (previousFocused is FrameworkElement { IsLoaded: true } previousElement)
                {
                    Keyboard.Focus(previousElement);
                    WindowValidationInput.Pump(previousElement.Dispatcher);
                }
                results["PreviousKeyboardFocusRestored"] = ReferenceEquals(Keyboard.FocusedElement, previousFocused);
            }
        }
        finally
        {
            anchorWindow.Close();
            WindowValidationInput.Pump(anchorWindow.Dispatcher);
            if (ambientFocused is FrameworkElement { IsLoaded: true } ambientElement)
            {
                Keyboard.Focus(ambientElement);
                WindowValidationInput.Pump(ambientElement.Dispatcher);
            }
            else if (ambientFocused is null)
            {
                Keyboard.ClearFocus();
                WindowValidationInput.Pump(anchorWindow.Dispatcher);
            }
            results["AmbientKeyboardFocusRestored"] = ReferenceEquals(Keyboard.FocusedElement, ambientFocused);
        }

        void Focus(FrameworkElement element)
        {
            Keyboard.Focus(element);
            WindowValidationInput.Pump(window.Dispatcher);
        }
    }
}
