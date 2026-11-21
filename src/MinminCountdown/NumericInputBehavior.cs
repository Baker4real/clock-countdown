using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace MinminCountdown;

// #330：在既有暖粉 TextBox 上提供可复用的整数步进、只允许整数交互输入与可空数值同步行为。
// 保留单层暖金焦点、1/2 DIP 错误边框、对比度基线与系统对比度语义，供后续 #331 复用到其他数值字段。
internal static class NumericInputBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(NumericInputBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.RegisterAttached(
            "Minimum",
            typeof(int),
            typeof(NumericInputBehavior),
            new PropertyMetadata(int.MinValue));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.RegisterAttached(
            "Maximum",
            typeof(int),
            typeof(NumericInputBehavior),
            new PropertyMetadata(int.MaxValue));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.RegisterAttached(
            "Step",
            typeof(int),
            typeof(NumericInputBehavior),
            new PropertyMetadata(1));

    public static readonly DependencyProperty AllowNegativeProperty =
        DependencyProperty.RegisterAttached(
            "AllowNegative",
            typeof(bool),
            typeof(NumericInputBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty RequireFocusForMouseWheelProperty =
        DependencyProperty.RegisterAttached(
            "RequireFocusForMouseWheel",
            typeof(bool),
            typeof(NumericInputBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.RegisterAttached(
            "Value",
            typeof(int?),
            typeof(NumericInputBehavior),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    public static readonly DependencyProperty HasInvalidTextProperty =
        DependencyProperty.RegisterAttached(
            "HasInvalidText",
            typeof(bool),
            typeof(NumericInputBehavior),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnHasInvalidTextChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(NumericInputController),
            typeof(NumericInputBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static int GetMinimum(DependencyObject element) =>
        (int)element.GetValue(MinimumProperty);

    public static void SetMinimum(DependencyObject element, int value) =>
        element.SetValue(MinimumProperty, value);

    public static int GetMaximum(DependencyObject element) =>
        (int)element.GetValue(MaximumProperty);

    public static void SetMaximum(DependencyObject element, int value) =>
        element.SetValue(MaximumProperty, value);

    public static int GetStep(DependencyObject element) =>
        (int)element.GetValue(StepProperty);

    public static void SetStep(DependencyObject element, int value) =>
        element.SetValue(StepProperty, value);

    public static bool GetAllowNegative(DependencyObject element) =>
        (bool)element.GetValue(AllowNegativeProperty);

    public static void SetAllowNegative(DependencyObject element, bool value) =>
        element.SetValue(AllowNegativeProperty, value);

    public static bool GetRequireFocusForMouseWheel(DependencyObject element) =>
        (bool)element.GetValue(RequireFocusForMouseWheelProperty);

    public static void SetRequireFocusForMouseWheel(DependencyObject element, bool value) =>
        element.SetValue(RequireFocusForMouseWheelProperty, value);

    public static int? GetValue(DependencyObject element) =>
        (int?)element.GetValue(ValueProperty);

    public static void SetValue(DependencyObject element, int? value) =>
        element.SetValue(ValueProperty, value);

    public static bool GetHasInvalidText(DependencyObject element) =>
        (bool)element.GetValue(HasInvalidTextProperty);

    public static void SetHasInvalidText(DependencyObject element, bool value) =>
        element.SetValue(HasInvalidTextProperty, value);

    internal static int ComputeSteppedValue(
        string? currentText,
        int? currentValue,
        int delta,
        int minimum,
        int maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }

        if (delta == 0)
        {
            return currentValue is int existing
                ? Math.Clamp(existing, minimum, maximum)
                : minimum;
        }

        int? seed = null;
        if (!string.IsNullOrEmpty(currentText) &&
            TryParseStrictInteger(currentText, out var parsedFromText))
        {
            seed = parsedFromText;
        }
        else if (currentValue is int existingValue)
        {
            seed = existingValue;
        }

        if (seed is null)
        {
            return minimum;
        }

        var next = (long)seed.Value + delta;
        if (next < minimum)
        {
            return minimum;
        }

        if (next > maximum)
        {
            return maximum;
        }

        return (int)next;
    }

    internal static bool TryParseStrictInteger(string text, out int value)
    {
        if (string.IsNullOrEmpty(text) ||
            char.IsWhiteSpace(text[0]) ||
            char.IsWhiteSpace(text[^1]) ||
            text[0] == '+')
        {
            value = 0;
            return false;
        }

        return int.TryParse(
            text,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
    }

    internal static bool IsAllowedIntegerInput(string candidate, bool allowNegative)
    {
        if (candidate.Length == 0)
        {
            return true;
        }

        var start = 0;
        if (candidate[0] == '-')
        {
            if (!allowNegative)
            {
                return false;
            }

            start = 1;
        }

        for (var index = start; index < candidate.Length; index++)
        {
            if (candidate[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static void OnIsEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not TextBox textBox)
        {
            return;
        }

        if (args.NewValue is true)
        {
            if (textBox.ReadLocalValue(ControllerProperty) is NumericInputController)
            {
                return;
            }

            var controller = new NumericInputController(textBox);
            textBox.SetValue(ControllerProperty, controller);
            controller.Attach();
        }
        else if (textBox.ReadLocalValue(ControllerProperty) is NumericInputController existing)
        {
            existing.Detach();
            textBox.ClearValue(ControllerProperty);
        }
    }

    private static void OnValueChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is TextBox textBox &&
            textBox.ReadLocalValue(ControllerProperty) is NumericInputController controller &&
            !controller.IsSyncing)
        {
            controller.SyncTextFromValue(args.NewValue as int?);
        }
    }

    private static void OnHasInvalidTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is TextBox textBox &&
            textBox.ReadLocalValue(ControllerProperty) is NumericInputController controller &&
            !controller.IsSyncing &&
            args.NewValue is false &&
            GetValue(textBox) is null &&
            !string.IsNullOrEmpty(textBox.Text))
        {
            controller.ClearInvalidText();
        }
    }

    private sealed class NumericInputController(TextBox textBox)
    {
        private readonly TextBox _textBox = textBox;
        private object? _previousInputMethodEnabled;
        private object? _previousNumeralAlignment;
        private bool _syncing;

        internal bool IsSyncing => _syncing;

        internal void Attach()
        {
            _previousInputMethodEnabled = _textBox.ReadLocalValue(InputMethod.IsInputMethodEnabledProperty);
            _previousNumeralAlignment = _textBox.ReadLocalValue(Typography.NumeralAlignmentProperty);
            InputMethod.SetIsInputMethodEnabled(_textBox, false);
            Typography.SetNumeralAlignment(_textBox, FontNumeralAlignment.Tabular);

            _textBox.PreviewKeyDown += OnPreviewKeyDown;
            _textBox.PreviewTextInput += OnPreviewTextInput;
            _textBox.PreviewMouseWheel += OnMouseWheel;
            _textBox.MouseWheel += OnMouseWheel;
            _textBox.TextChanged += OnTextChanged;
            DataObject.AddPastingHandler(_textBox, OnPasting);

            if (GetValue(_textBox) is int initialValue)
            {
                SyncTextFromValue(initialValue);
            }
            else if (!string.IsNullOrEmpty(_textBox.Text))
            {
                SyncValueFromText();
            }
        }

        internal void Detach()
        {
            _textBox.PreviewKeyDown -= OnPreviewKeyDown;
            _textBox.PreviewTextInput -= OnPreviewTextInput;
            _textBox.PreviewMouseWheel -= OnMouseWheel;
            _textBox.MouseWheel -= OnMouseWheel;
            _textBox.TextChanged -= OnTextChanged;
            DataObject.RemovePastingHandler(_textBox, OnPasting);

            RestoreLocalValue(InputMethod.IsInputMethodEnabledProperty, _previousInputMethodEnabled);
            RestoreLocalValue(Typography.NumeralAlignmentProperty, _previousNumeralAlignment);
        }

        internal void SyncTextFromValue(int? value)
        {
            _syncing = true;
            try
            {
                var desiredText = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.Equals(_textBox.Text, desiredText, StringComparison.Ordinal))
                {
                    _textBox.Text = desiredText;
                }

                if (GetHasInvalidText(_textBox))
                {
                    _textBox.SetCurrentValue(HasInvalidTextProperty, false);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        internal void ClearInvalidText()
        {
            _syncing = true;
            try
            {
                _textBox.Text = string.Empty;
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Space)
            {
                args.Handled = true;
                return;
            }

            if (_textBox.IsReadOnly || !_textBox.IsEnabled || Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (args.Key is not (Key.Up or Key.Down))
            {
                return;
            }

            var step = Math.Max(1, GetStep(_textBox));
            StepBy(args.Key == Key.Up ? step : -step);
            args.Handled = true;
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs args)
        {
            if (_textBox.IsReadOnly || !_textBox.IsEnabled || string.IsNullOrEmpty(args.Text))
            {
                return;
            }

            var candidate = BuildCandidateText(args.Text);
            if (!IsAllowedIntegerInput(candidate, AllowNegativeForControl()))
            {
                args.Handled = true;
            }
        }

        private void OnPasting(object sender, DataObjectPastingEventArgs args)
        {
            if (_textBox.IsReadOnly || !_textBox.IsEnabled)
            {
                return;
            }

            if (!args.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                args.CancelCommand();
                return;
            }

            var pasted = args.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            var candidate = BuildCandidateText(pasted);
            if (!IsAllowedIntegerInput(candidate, AllowNegativeForControl()))
            {
                args.CancelCommand();
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs args)
        {
            if (args.Handled || args.Delta == 0 || _textBox.IsReadOnly || !_textBox.IsEnabled)
            {
                return;
            }

            if (GetRequireFocusForMouseWheel(_textBox) && !_textBox.IsKeyboardFocused)
            {
                return;
            }

            var step = Math.Max(1, GetStep(_textBox));
            var notches = Math.Max(1, Math.Abs(args.Delta) / Mouse.MouseWheelDeltaForOneLine);
            var signedNotches = args.Delta > 0 ? notches : -notches;
            var rawDelta = (long)signedNotches * step;
            var delta = (int)Math.Clamp(rawDelta, int.MinValue, int.MaxValue);
            StepBy(delta);
            args.Handled = true;
        }

        private void OnTextChanged(object sender, TextChangedEventArgs args)
        {
            if (_syncing)
            {
                return;
            }

            SyncValueFromText();
        }

        private void SyncValueFromText()
        {
            _syncing = true;
            try
            {
                var text = _textBox.Text;
                if (string.IsNullOrEmpty(text))
                {
                    _textBox.SetCurrentValue(HasInvalidTextProperty, false);
                    _textBox.SetCurrentValue(ValueProperty, null);
                }
                else if (TryParseStrictInteger(text, out var parsed))
                {
                    _textBox.SetCurrentValue(HasInvalidTextProperty, false);
                    _textBox.SetCurrentValue(ValueProperty, (int?)parsed);
                }
                else
                {
                    _textBox.SetCurrentValue(HasInvalidTextProperty, true);
                    _textBox.SetCurrentValue(ValueProperty, null);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void StepBy(int delta)
        {
            var minimum = GetMinimum(_textBox);
            var maximum = GetMaximum(_textBox);
            if (minimum > maximum)
            {
                return;
            }

            var next = ComputeSteppedValue(_textBox.Text, GetValue(_textBox), delta, minimum, maximum);
            var nextText = next.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(_textBox.Text, nextText, StringComparison.Ordinal))
            {
                _textBox.Text = nextText;
            }
            else if (GetHasInvalidText(_textBox) || GetValue(_textBox) != next)
            {
                SyncValueFromText();
            }

            _textBox.CaretIndex = _textBox.Text.Length;
        }

        private bool AllowNegativeForControl() =>
            GetAllowNegative(_textBox) && GetMinimum(_textBox) < 0;

        private string BuildCandidateText(string inserted)
        {
            var current = _textBox.Text ?? string.Empty;
            var selectionStart = Math.Clamp(_textBox.SelectionStart, 0, current.Length);
            var selectionLength = Math.Clamp(_textBox.SelectionLength, 0, current.Length - selectionStart);
            return string.Concat(
                current.AsSpan(0, selectionStart),
                inserted,
                current.AsSpan(selectionStart + selectionLength));
        }

        private void RestoreLocalValue(DependencyProperty property, object? previousValue)
        {
            if (previousValue is null || ReferenceEquals(previousValue, DependencyProperty.UnsetValue))
            {
                _textBox.ClearValue(property);
            }
            else
            {
                _textBox.SetValue(property, previousValue);
            }
        }
    }
}
