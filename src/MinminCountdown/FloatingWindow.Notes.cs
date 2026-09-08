using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class FloatingWindow
{
    // Current row origins are x=60 by day and x=59 by night; card width 319 keeps x <= 396.
    private const double CalendarIndexNoteCardWidth = 319;
    private const double CalendarIndexDateStampWidth = 48;
    private const double CalendarIndexRuleSpacing = 18;

    private FrameworkElement BuildCalendarIndexNoteCard(NoteRecord note, DateTimeOffset nowUtc)
    {
        var noteError = new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = ThemeBrush((_activeTheme ??
                throw new InvalidOperationException("悬浮窗主题尚未初始化。")).ErrorBrushKey),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        UiAccessibility.MarkLive(noteError);

        var completion = UiAccessibility.Name(
            new CheckBox
            {
                Tag = note.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 1, 0),
                Foreground = _floatingPrimaryBrush
            },
            $"完成便签 {note.Text}");
        if (_noteCardErrors.TryGetValue(note.Id, out var savedError))
        {
            SetNoteCardError(noteError, completion, savedError);
        }

        completion.Checked += (_, _) =>
        {
            var result = _desktopWorkspace.Apply(new SetNoteCompletionChange(note.Id, true));
            if (!result.Succeeded)
            {
                completion.IsChecked = false;
                _noteCardErrors[note.Id] = result.Error;
                SetNoteCardError(noteError, completion, result.Error);
                SetNoteMessage(result.Error);
                completion.Focus();
            }
            else
            {
                var previousError = noteError.Text;
                _noteCardErrors.Remove(note.Id);
                SetNoteCardError(noteError, completion, string.Empty);
                if (string.Equals(FloatingNoteMessage.Text, previousError, StringComparison.Ordinal))
                {
                    SetNoteMessage(string.Empty);
                }
            }
        };

        var body = new StackPanel
        {
            Margin = new Thickness(0, 9, 8, 9)
        };
        body.Children.Add(new TextBlock
        {
            Text = note.Text,
            FontSize = 13,
            LineHeight = 18,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            Foreground = _floatingPrimaryBrush
        });

        var nextReminder = ReminderSchedule.GetNextDueUtc(
            _desktopWorkspace.Snapshot.Settings,
            note,
            nowUtc);
        if (nextReminder is not null)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"下次提醒：{ChinaStandardTime.ToBeijingTime(nextReminder.Value):M月d日 HH:mm}",
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12,
                LineHeight = 17,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.Wrap,
                Foreground = _floatingMutedBrush
            });
        }

        body.Children.Add(noteError);

        var layout = new Grid
        {
            Background = CreateCalendarIndexRuleBrush(_floatingBorderBrush),
            ClipToBounds = true
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(CalendarIndexDateStampWidth)
        });

        var dateStamp = CreateCalendarIndexDateStamp(note);
        Grid.SetColumn(body, 1);
        Grid.SetColumn(dateStamp, 2);
        layout.Children.Add(completion);
        layout.Children.Add(body);
        layout.Children.Add(dateStamp);

        return new Border
        {
            Child = layout,
            Width = CalendarIndexNoteCardWidth,
            MinHeight = 72,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = _floatingNoteCardBrush,
            BorderBrush = _floatingBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
    }

    private static void SetNoteCardError(TextBlock error, Control source, string message)
    {
        error.Text = message;
        error.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
        AutomationProperties.SetName(error, message);
        AutomationProperties.SetHelpText(error, message);
        AutomationProperties.SetHelpText(source, message);
    }

    private Border CreateCalendarIndexDateStamp(NoteRecord note)
    {
        var date = CalendarIndexDate(note);
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        var stamp = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stamp.Children.Add(CalendarIndexStampText(
            date is null ? "无日期" : $"{date.Value.Month} 月",
            10,
            FontWeights.Regular,
            _floatingMutedBrush));
        stamp.Children.Add(CalendarIndexStampText(
            date is null ? "--" : date.Value.Day.ToString("D2", CultureInfo.InvariantCulture),
            22,
            FontWeights.SemiBold,
            _floatingPrimaryBrush,
            new FontFamily("Bahnschrift SemiCondensed, Bahnschrift, Microsoft YaHei UI"),
            new Thickness(0, 2, 0, 2)));
        stamp.Children.Add(CalendarIndexStampText(
            date is null
                ? " "
                : culture.DateTimeFormat.GetAbbreviatedDayName(date.Value.DayOfWeek),
            10,
            FontWeights.Regular,
            _floatingMutedBrush));

        var dateStamp = new Border
        {
            Child = stamp,
            Background = _floatingSurfaceBrush,
            BorderBrush = _floatingBorderBrush,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(4, 8, 4, 8),
            IsHitTestVisible = false,
            Focusable = false
        };
        return dateStamp;
    }

    private static TextBlock CalendarIndexStampText(
        string text,
        double fontSize,
        FontWeight fontWeight,
        Brush foreground,
        FontFamily? fontFamily = null,
        Thickness? margin = null)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        if (margin is { } value)
        {
            label.Margin = value;
        }

        if (fontFamily is not null)
        {
            label.FontFamily = fontFamily;
            Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        }

        return label;
    }

    private static DateTime? CalendarIndexDate(NoteRecord note)
    {
        if (note.Date is not null &&
            DateTime.TryParseExact(
                note.Date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed.Date;
        }

        return null;
    }

    private static DrawingBrush CreateCalendarIndexRuleBrush(Brush lineBrush)
    {
        var tile = new Rect(
            0,
            0,
            CalendarIndexNoteCardWidth,
            CalendarIndexRuleSpacing);
        var rule = new GeometryDrawing(
            null,
            new Pen(lineBrush, 1),
            new LineGeometry(
                new Point(0, CalendarIndexRuleSpacing - 0.5),
                new Point(CalendarIndexNoteCardWidth, CalendarIndexRuleSpacing - 0.5)));
        return new DrawingBrush(rule)
        {
            Viewbox = tile,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = tile,
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            Opacity = 0.24
        };
    }
}
