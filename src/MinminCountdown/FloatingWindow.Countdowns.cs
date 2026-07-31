using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class FloatingWindow
{
    private const string WorkdayEndCountdownId = "__workday_end__";
    private const double CountdownMascotSafeInset = 54;

    private IReadOnlyList<FrameworkElement> BuildWarmPaperCountdownRows(
        IReadOnlyList<CountdownRecord> records,
        DateTimeOffset nowUtc,
        string displayPrecision)
    {
        var rows = new List<FrameworkElement>(records.Count);
        var primaryAssigned = false;
        foreach (var record in records)
        {
            var isPrimary = !primaryAssigned && record.Id != WorkdayEndCountdownId;
            primaryAssigned |= isPrimary;
            rows.Add(BuildWarmPaperCountdownRow(record, nowUtc, displayPrecision, isPrimary));
        }

        return rows;
    }

    private FrameworkElement BuildWarmPaperCountdownRow(
        CountdownRecord record,
        DateTimeOffset nowUtc,
        string displayPrecision,
        bool isPrimary)
    {
        var theme = _activeTheme ??
                    throw new InvalidOperationException("悬浮窗主题尚未初始化。");
        var remaining = CountdownFormatting.FormatRemaining(
            record.TargetUtc,
            nowUtc,
            displayPrecision);
        var root = new Grid
        {
            MinHeight = isPrimary ? 68 : 39,
            Margin = new Thickness(0),
            Background = Brushes.Transparent
        };
        AutomationProperties.SetName(
            root,
            isPrimary ? $"主要倒计时 {record.Name}，{remaining}" : $"倒计时 {record.Name}，{remaining}");

        if (isPrimary)
        {
            root.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, CountdownMascotSafeInset, 1),
                Background = ThemeBrush(theme.NoteCardBrushKey),
                CornerRadius = new CornerRadius(4),
                Opacity = theme.Mode == ApplicationThemeMode.Day ? 0.62 : 0.74,
                IsHitTestVisible = false
            });
            root.Children.Add(new Border
            {
                Width = 3,
                Height = 42,
                Margin = new Thickness(0, 0, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = WarmPaperAccentBrush(theme),
                CornerRadius = new CornerRadius(1.5),
                IsHitTestVisible = false
            });
        }

        root.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, CountdownMascotSafeInset, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = _floatingBorderBrush,
            Opacity = isPrimary ? 0.78 : 0.48,
            IsHitTestVisible = false
        });

        root.Children.Add(isPrimary
            ? BuildWarmPaperPrimaryContent(record, remaining)
            : BuildWarmPaperSecondaryContent(record, remaining));
        return new Border
        {
            Child = root,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
    }

    private FrameworkElement BuildWarmPaperPrimaryContent(
        CountdownRecord record,
        string remaining)
    {
        var layout = new Grid
        {
            Margin = new Thickness(12, 8, CountdownMascotSafeInset + 8, 8)
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel
        {
            MinWidth = 0,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        copy.Children.Add(new TextBlock
        {
            Text = record.Name,
            MaxHeight = 40,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = _floatingPrimaryBrush
        });
        copy.Children.Add(new TextBlock
        {
            Text = $"{ChinaStandardTime.ToBeijingTime(record.TargetUtc):M月d日 HH:mm} · 北京时间",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = _floatingMutedBrush
        });

        var remainingText = CountdownRemainingText(remaining, 20, FontWeights.SemiBold);
        remainingText.MaxWidth = 150;
        remainingText.MaxHeight = 48;
        remainingText.Margin = new Thickness(6, 0, 0, 0);
        remainingText.LineHeight = 22;
        remainingText.TextTrimming = TextTrimming.None;
        remainingText.TextWrapping = TextWrapping.Wrap;
        remainingText.Foreground = WarmPaperAccentBrush(
            _activeTheme ?? throw new InvalidOperationException("悬浮窗主题尚未初始化。"));

        Grid.SetColumn(remainingText, 1);
        layout.Children.Add(copy);
        layout.Children.Add(remainingText);
        return layout;
    }

    private FrameworkElement BuildWarmPaperSecondaryContent(
        CountdownRecord record,
        string remaining)
    {
        var layout = new Grid
        {
            Margin = new Thickness(7, 6, CountdownMascotSafeInset + 8, 6)
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = record.Name,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = _floatingPrimaryBrush
        };
        var remainingText = CountdownRemainingText(
            remaining.Replace(" ", string.Empty),
            13,
            FontWeights.SemiBold);
        remainingText.MaxWidth = 138;
        remainingText.Foreground = _floatingMutedBrush;

        Grid.SetColumn(remainingText, 1);
        layout.Children.Add(name);
        layout.Children.Add(remainingText);
        return layout;
    }

    private Brush WarmPaperAccentBrush(FloatingThemePalette theme) =>
        ThemeBrush(theme.Mode == ApplicationThemeMode.Day
            ? theme.HeaderDangerBrushKey
            : theme.PlushGlowBrushKey);

    private static TextBlock CountdownRemainingText(
        string text,
        double fontSize,
        FontWeight fontWeight)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontFamily = new FontFamily("Bahnschrift, Microsoft YaHei UI"),
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }
}
