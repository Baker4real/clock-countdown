using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace MinminCountdown;

internal enum TimeInputFormat
{
    HourMinuteSecond,
    HourMinute
}

internal enum TimeInputSegment
{
    Hour,
    Minute,
    Second
}

// #328：在既有暖粉 TextBox 上提供可复用的时刻键入与光标感知上下键分段步进（支持 HH:mm:ss 与 HH:mm）。
// 禁用 IME、启用表格数字、保留既有单层暖金焦点与 1/2 DIP 错误边框语义，供主窗截止时刻及 #329 其他时刻字段共用。
internal static class TimeInputBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TimeInputBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.RegisterAttached(
            "Format",
            typeof(TimeInputFormat),
            typeof(TimeInputBehavior),
            new PropertyMetadata(TimeInputFormat.HourMinuteSecond, OnFormatChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.RegisterAttached(
            "Value",
            typeof(TimeSpan?),
            typeof(TimeInputBehavior),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    public static readonly DependencyProperty HasInvalidTextProperty =
        DependencyProperty.RegisterAttached(
            "HasInvalidText",
            typeof(bool),
            typeof(TimeInputBehavior),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnHasInvalidTextChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(TimeInputController),
            typeof(TimeInputBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static TimeInputFormat GetFormat(DependencyObject element) =>
        (TimeInputFormat)element.GetValue(FormatProperty);

    public static void SetFormat(DependencyObject element, TimeInputFormat value) =>
        element.SetValue(FormatProperty, value);

    public static TimeSpan? GetValue(DependencyObject element) =>
        (TimeSpan?)element.GetValue(ValueProperty);

    public static void SetValue(DependencyObject element, TimeSpan? value) =>
        element.SetValue(ValueProperty, value);

    public static bool GetHasInvalidText(DependencyObject element) =>
        (bool)element.GetValue(HasInvalidTextProperty);

    public static void SetHasInvalidText(DependencyObject element, bool value) =>
        element.SetValue(HasInvalidTextProperty, value);

    internal static TimeSpan Normalize(TimeSpan value, TimeInputFormat format)
    {
        var totalSeconds = (long)Math.Floor(value.TotalSeconds);
        const long secondsPerDay = 24L * 60 * 60;
        var normalizedSeconds = ((totalSeconds % secondsPerDay) + secondsPerDay) % secondsPerDay;
        var hours = (int)(normalizedSeconds / 3600);
        var minutes = (int)((normalizedSeconds % 3600) / 60);
        var seconds = format == TimeInputFormat.HourMinuteSecond
            ? (int)(normalizedSeconds % 60)
            : 0;
        return new TimeSpan(hours, minutes, seconds);
    }

    internal static string FormatTime(TimeSpan value, TimeInputFormat format)
    {
        var normalized = Normalize(value, format);
        return format == TimeInputFormat.HourMinuteSecond
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{normalized.Hours:D2}:{normalized.Minutes:D2}:{normalized.Seconds:D2}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{normalized.Hours:D2}:{normalized.Minutes:D2}");
    }

    internal static bool TryParseStrict(string? text, TimeInputFormat format, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (format == TimeInputFormat.HourMinuteSecond)
        {
            if (text.Length != 8 || text[2] != ':' || text[5] != ':')
            {
                return false;
            }

            if (!TryParseTwoAsciiDigits(text, 0, out var hour) ||
                !TryParseTwoAsciiDigits(text, 3, out var minute) ||
                !TryParseTwoAsciiDigits(text, 6, out var second) ||
                hour > 23 || minute > 59 || second > 59)
            {
                return false;
            }

            value = new TimeSpan(hour, minute, second);
            return true;
        }

        if (text.Length != 5 || text[2] != ':')
        {
            return false;
        }

        if (!TryParseTwoAsciiDigits(text, 0, out var hmHour) ||
            !TryParseTwoAsciiDigits(text, 3, out var hmMinute) ||
            hmHour > 23 || hmMinute > 59)
        {
            return false;
        }

        value = new TimeSpan(hmHour, hmMinute, 0);
        return true;
    }

    internal static TimeInputSegment ResolveSegment(string? text, int caretIndex, TimeInputFormat format)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TimeInputSegment.Hour;
        }

        var clamped = Math.Clamp(caretIndex, 0, text.Length);
        var firstColon = text.IndexOf(':');
        if (firstColon < 0)
        {
            if (clamped <= 2)
            {
                return TimeInputSegment.Hour;
            }

            return format == TimeInputFormat.HourMinute || clamped <= 5
                ? TimeInputSegment.Minute
                : TimeInputSegment.Second;
        }

        if (clamped <= firstColon)
        {
            return TimeInputSegment.Hour;
        }

        if (format == TimeInputFormat.HourMinute)
        {
            return TimeInputSegment.Minute;
        }

        var secondColon = text.IndexOf(':', firstColon + 1);
        if (secondColon < 0)
        {
            return clamped <= firstColon + 3 ? TimeInputSegment.Minute : TimeInputSegment.Second;
        }

        return clamped <= secondColon ? TimeInputSegment.Minute : TimeInputSegment.Second;
    }

    internal static TimeSpan ComputeStepped(
        TimeSpan current,
        TimeInputSegment segment,
        int delta,
        TimeInputFormat format)
    {
        var normalized = Normalize(current, format);
        var hour = normalized.Hours;
        var minute = normalized.Minutes;
        var second = normalized.Seconds;

        switch (segment)
        {
            case TimeInputSegment.Hour:
                hour = PositiveMod(hour + delta, 24);
                break;
            case TimeInputSegment.Minute:
                minute = PositiveMod(minute + delta, 60);
                break;
            case TimeInputSegment.Second:
                if (format == TimeInputFormat.HourMinuteSecond)
                {
                    second = PositiveMod(second + delta, 60);
                }
                else
                {
                    minute = PositiveMod(minute + delta, 60);
                }
                break;
        }

        return new TimeSpan(hour, minute, second);
    }

    internal static (int Start, int Length) GetSegmentRange(TimeInputSegment segment, TimeInputFormat format) =>
        segment switch
        {
            TimeInputSegment.Hour => (0, 2),
            TimeInputSegment.Minute => (3, 2),
            TimeInputSegment.Second when format == TimeInputFormat.HourMinuteSecond => (6, 2),
            _ => (3, 2)
        };

    private static bool TryParseTwoAsciiDigits(string text, int start, out int value)
    {
        var tens = text[start] - '0';
        var ones = text[start + 1] - '0';
        if ((uint)tens > 9 || (uint)ones > 9)
        {
            value = 0;
            return false;
        }

        value = (tens * 10) + ones;
        return true;
    }

    private static int PositiveMod(int value, int modulus) =>
        ((value % modulus) + modulus) % modulus;

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
            if (textBox.ReadLocalValue(ControllerProperty) is TimeInputController)
            {
                return;
            }

            var controller = new TimeInputController(textBox);
            textBox.SetValue(ControllerProperty, controller);
            controller.Attach();
        }
        else if (textBox.ReadLocalValue(ControllerProperty) is TimeInputController existing)
        {
            existing.Detach();
            textBox.ClearValue(ControllerProperty);
        }
    }

    private static void OnFormatChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is TextBox textBox &&
            textBox.ReadLocalValue(ControllerProperty) is TimeInputController controller &&
            !controller.IsSyncing)
        {
            controller.OnFormatChanged();
        }
    }

    private static void OnValueChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is TextBox textBox &&
            textBox.ReadLocalValue(ControllerProperty) is TimeInputController controller &&
            !controller.IsSyncing)
        {
            controller.SyncTextFromValue(args.NewValue as TimeSpan?);
        }
    }

    private static void OnHasInvalidTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is TextBox textBox &&
            textBox.ReadLocalValue(ControllerProperty) is TimeInputController controller &&
            !controller.IsSyncing &&
            args.NewValue is false &&
            GetValue(textBox) is null &&
            !string.IsNullOrEmpty(textBox.Text))
        {
            controller.ClearInvalidText();
        }
    }

    private sealed class TimeInputController(TextBox textBox)
    {
        private readonly TextBox _textBox = textBox;
        private object? _previousInputMethodEnabled;
        private object? _previousNumeralAlignment;
        private TimeSpan? _lastValidValue;
        private bool _syncing;

        internal bool IsSyncing => _syncing;

        internal void Attach()
        {
            _previousInputMethodEnabled = _textBox.ReadLocalValue(InputMethod.IsInputMethodEnabledProperty);
            _previousNumeralAlignment = _textBox.ReadLocalValue(Typography.NumeralAlignmentProperty);
            InputMethod.SetIsInputMethodEnabled(_textBox, false);
            Typography.SetNumeralAlignment(_textBox, FontNumeralAlignment.Tabular);

            _textBox.PreviewKeyDown += OnPreviewKeyDown;
            _textBox.TextChanged += OnTextChanged;

            if (GetValue(_textBox) is TimeSpan initialValue)
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
            _textBox.TextChanged -= OnTextChanged;

            RestoreLocalValue(InputMethod.IsInputMethodEnabledProperty, _previousInputMethodEnabled);
            RestoreLocalValue(Typography.NumeralAlignmentProperty, _previousNumeralAlignment);
        }

        internal void OnFormatChanged()
        {
            if (GetValue(_textBox) is TimeSpan currentValue)
            {
                SyncTextFromValue(currentValue);
            }
            else
            {
                SyncValueFromText();
            }
        }

        internal void SyncTextFromValue(TimeSpan? value)
        {
            _syncing = true;
            try
            {
                var format = GetFormat(_textBox);
                string desiredText;
                if (value is TimeSpan timeOfDay)
                {
                    var normalized = Normalize(timeOfDay, format);
                    _lastValidValue = normalized;
                    desiredText = FormatTime(normalized, format);
                    if (normalized != timeOfDay)
                    {
                        _textBox.SetCurrentValue(ValueProperty, (TimeSpan?)normalized);
                    }
                }
                else
                {
                    desiredText = string.Empty;
                }

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
            if (args.Handled ||
                _textBox.IsReadOnly ||
                !_textBox.IsEnabled ||
                Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (args.Key is not (Key.Up or Key.Down))
            {
                return;
            }

            var format = GetFormat(_textBox);
            var anchor = _textBox.SelectionLength > 0 ? _textBox.SelectionStart : _textBox.CaretIndex;
            var segment = ResolveSegment(_textBox.Text, anchor, format);
            var seed = TryParseStrict(_textBox.Text, format, out var parsed)
                ? parsed
                : GetValue(_textBox) ?? _lastValidValue ?? TimeSpan.Zero;
            var stepped = ComputeStepped(seed, segment, args.Key == Key.Up ? 1 : -1, format);
            var formatted = FormatTime(stepped, format);

            if (!string.Equals(_textBox.Text, formatted, StringComparison.Ordinal))
            {
                _textBox.Text = formatted;
            }
            else if (GetHasInvalidText(_textBox) || GetValue(_textBox) != stepped)
            {
                SyncValueFromText();
            }

            var (start, length) = GetSegmentRange(segment, format);
            _textBox.Select(start, length);
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
                var format = GetFormat(_textBox);
                var text = _textBox.Text;
                if (string.IsNullOrEmpty(text))
                {
                    _textBox.SetCurrentValue(HasInvalidTextProperty, false);
                    _textBox.SetCurrentValue(ValueProperty, null);
                }
                else if (TryParseStrict(text, format, out var parsed))
                {
                    _lastValidValue = parsed;
                    _textBox.SetCurrentValue(HasInvalidTextProperty, false);
                    _textBox.SetCurrentValue(ValueProperty, (TimeSpan?)parsed);
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
