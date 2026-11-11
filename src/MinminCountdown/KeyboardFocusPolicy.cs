using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MinminCountdown;

/// <summary>
/// One focus contract for input state, drawing ownership, adorner assignment and WPF probes.
/// Templates retain their geometry and bind to FocusVisualState; callers do not classify controls.
/// </summary>
internal static class KeyboardFocusPolicy
{
    private static readonly DependencyProperty PointerFocusPendingProperty =
        DependencyProperty.RegisterAttached("PointerFocusPending", typeof(bool), typeof(KeyboardFocusPolicy),
            new FrameworkPropertyMetadata(false));

    internal static void RegisterClassHandlers()
    {
        EventManager.RegisterClassHandler(typeof(Control), UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPreviewPointerDown), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(Control), UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(AutomationOptionalTextBlock), UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), handledEventsToo: true);
    }

    internal static void Apply(FrameworkElement element) =>
        element.FocusVisualStyle = UsesTemplateOrHostStroke(element)
            ? null
            : (Style)element.FindResource("MainKeyboardFocusVisual");

    internal static bool UsesTemplateOrHostStroke(FrameworkElement element)
    {
        if (FocusVisualState.GetHostDrawsFocus(element) || element is AutomationOptionalTextBlock)
            return true;
        if (element is not Control control) return false;
        if (control is TextBox or ComboBox or DatePicker or CalendarDayButton or CalendarButton) return true;
        control.ApplyTemplate();
        return control.Template?.FindName("FocusRing", control) is Border ||
            control is CheckBox && control.Template?.FindName("Box", control) is Border;
    }

    internal static bool HasValidAdornment(FrameworkElement element) =>
        UsesTemplateOrHostStroke(element)
            ? element.FocusVisualStyle is null
            : element.FocusVisualStyle is { } style &&
                ReferenceEquals(style, element.TryFindResource("MainKeyboardFocusVisual"));

    internal static bool KeyboardFocusDrawnOnce(DependencyObject focused)
    {
        if (focused is not FrameworkElement element || !HasValidAdornment(element)) return false;
        if (element.FocusVisualStyle is not null) return true;

        // The income text is not a Control. Its sibling ring binds to the same visible state.
        if (element is AutomationOptionalTextBlock)
            return FocusVisualState.GetIsKeyboardFocusVisible(element);
        if (element is not Control control) return false;

        Control? inputHost = null;
        for (DependencyObject? node = control; node is not null && inputHost is null;
             node = VisualTreeHelper.GetParent(node))
            inputHost = node as DatePicker ?? (Control?)(node as ComboBox);
        inputHost ??= control as TextBox;

        // DatePicker focuses PART_Button, but PART_TextBox draws the surrounding input stroke.
        // Its button ring must stay unlit even when focus was transferred by the keyboard.
        return inputHost is not null
            ? TemplatePartsDrawFocusStroke(inputHost, "FocusRing", "Box", "Surface", "PART_TextBox") &&
                (ReferenceEquals(inputHost, control) || !TemplatePartsDrawFocusStroke(control, "FocusRing"))
            : FocusVisualState.GetIsKeyboardFocusVisible(control) &&
                TemplatePartsDrawFocusStroke(control, "FocusRing", "Box", "Surface", "PART_TextBox");
    }

    internal static void ShowForKeyboardInput(FrameworkElement element) =>
        FocusVisualState.SetIsKeyboardFocusVisible(element, !FocusVisualState.GetHostDrawsFocus(element));

    internal static void Clear(FrameworkElement element) =>
        FocusVisualState.SetIsKeyboardFocusVisible(element, false);

    // The income action acquires focus synchronously on press. Keep its pending scope synchronous too.
    internal static void FocusFromPointer(FrameworkElement element)
    {
        element.SetValue(PointerFocusPendingProperty, true);
        Clear(element);
        try { element.Focus(); }
        finally { element.ClearValue(PointerFocusPendingProperty); }
    }

    private static void OnPreviewPointerDown(object sender, MouseButtonEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.SetValue(PointerFocusPendingProperty, true);
        Clear(element);
        element.Dispatcher.BeginInvoke(DispatcherPriority.Input,
            new Action(() => element.ClearValue(PointerFocusPendingProperty)));
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        // Routed focus events also visit ancestors; only the actual focus target changes state.
        if (ReferenceEquals(sender, e.NewFocus) && !(bool)element.GetValue(PointerFocusPendingProperty))
            ShowForKeyboardInput(element);
    }

    private static bool TemplatePartsDrawFocusStroke(Control control, params string[] partNames)
    {
        if (control.Template is null) return false;
        control.ApplyTemplate();
        return partNames.Select(name => control.Template.FindName(name, control)).Any(part => part switch
        {
            Border border => IsFocusStroke(border.IsVisible, border.BorderBrush, border.BorderThickness),
            Control inner => IsFocusStroke(inner.IsVisible, inner.BorderBrush, inner.BorderThickness),
            _ => false
        });

        static bool IsFocusStroke(bool visible, Brush? brush, Thickness thickness) =>
            visible && brush is not (null or SolidColorBrush { Color.A: 0 }) &&
            new[] { thickness.Left, thickness.Top, thickness.Right, thickness.Bottom }.Max() >= 2;
    }
}

/// <summary>Presentation metadata consumed by existing XAML templates; policy lives above.</summary>
internal static class FocusVisualState
{
    internal static readonly DependencyProperty IsKeyboardFocusVisibleProperty =
        DependencyProperty.RegisterAttached("IsKeyboardFocusVisible", typeof(bool), typeof(FocusVisualState),
            new FrameworkPropertyMetadata(false));

    internal static void SetIsKeyboardFocusVisible(DependencyObject target, bool value) =>
        target.SetValue(IsKeyboardFocusVisibleProperty, value);

    internal static bool GetIsKeyboardFocusVisible(DependencyObject target) =>
        (bool)target.GetValue(IsKeyboardFocusVisibleProperty);

    public static readonly DependencyProperty HostDrawsFocusProperty =
        DependencyProperty.RegisterAttached("HostDrawsFocus", typeof(bool), typeof(FocusVisualState),
            new FrameworkPropertyMetadata(false));

    public static void SetHostDrawsFocus(DependencyObject target, bool value) =>
        target.SetValue(HostDrawsFocusProperty, value);

    public static bool GetHostDrawsFocus(DependencyObject target) =>
        (bool)target.GetValue(HostDrawsFocusProperty);
}
