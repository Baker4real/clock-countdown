using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class FloatingWindow
{
    // Current row origins are x=60 in the single day theme; card width 319 keeps x <= 396.
    // #143：复选框槽位与“…”触发器命中区按 36px 下限收紧（DESIGN §10 小尺寸命中区合同）。
    private const double CalendarIndexNoteCardWidth = 319;
    private const double CalendarIndexDateStampWidth = 48;
    private const double CalendarIndexRuleSpacing = 18;
    private const double CalendarIndexOverflowMenuWidth = 36;
    private const double CalendarIndexCheckboxSlotWidth = 36;

    // DESIGN §2 日期印记 pastel 轮换：粉 → 蓝 → 绿，按列表顺序循环。
    private static readonly string[][] CalendarIndexStampPalette =
    [
        ["StampPinkBrush", "StampPinkTextBrush"],
        ["StampBlueBrush", "StampBlueTextBrush"],
        ["StampGreenBrush", "StampGreenTextBrush"]
    ];

    private FrameworkElement BuildCalendarIndexNoteCard(NoteRecord note, DateTimeOffset nowUtc, int rowIndex)
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
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CalendarIndexCheckboxSlotWidth) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(CalendarIndexDateStampWidth)
        });
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(CalendarIndexOverflowMenuWidth)
        });

        var overflowMenu = BuildNoteOverflowMenu(note);
        var dateStamp = CreateCalendarIndexDateStamp(note, rowIndex);
        Grid.SetColumn(body, 1);
        Grid.SetColumn(dateStamp, 2);
        Grid.SetColumn(overflowMenu, 3);
        layout.Children.Add(completion);
        layout.Children.Add(body);
        layout.Children.Add(overflowMenu);
        layout.Children.Add(dateStamp);

        // DESIGN §10：便签卡底按条目在暖黄与白纸之间交替，形成条带节奏。
        var card = new Border
        {
            Child = layout,
            Width = CalendarIndexNoteCardWidth,
            MinHeight = 72,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = _floatingBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        if (rowIndex % 2 == 0)
        {
            card.Background = _floatingNoteCardBrush;
        }
        else
        {
            card.SetResourceReference(Border.BackgroundProperty, "NoteWhiteBrush");
        }

        return card;
    }

    /// <summary>
    ///     每条便签右侧的“…”菜单：提供编辑/删除，编辑复用主窗口编辑流程（悬浮窗首次获得编辑能力）。
    /// </summary>
    private FrameworkElement BuildNoteOverflowMenu(NoteRecord note)
    {
        var trigger = UiAccessibility.Name(
            new Button
            {
                Tag = note.Id,
                Width = CalendarIndexOverflowMenuWidth,
                Height = CalendarIndexOverflowMenuWidth,
                MinWidth = CalendarIndexOverflowMenuWidth,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = _floatingMutedBrush,
                FocusVisualStyle = null,
                Style = (Style)FindResource("FloatingHeaderIconButton"),
                Content = new TextBlock
                {
                    Text = "…",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            },
            $"更多操作：编辑或删除便签 {note.Text}");

        var menuEdit = UiAccessibility.Name(
            new Button
            {
                Content = "编辑",
                Tag = note.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2),
                Style = (Style)FindResource("SecondaryButton")
            },
            $"编辑便签 {note.Text}");
        var menuDelete = UiAccessibility.Name(
            new Button
            {
                Content = "删除",
                Tag = note.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2),
                Style = (Style)FindResource("DangerButton")
            },
            $"删除便签 {note.Text}");

        var items = new StackPanel { Margin = new Thickness(4) };
        items.Children.Add(menuEdit);
        items.Children.Add(menuDelete);
        var surface = new Border
        {
            MinWidth = 132,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = items
        };
        surface.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        var popup = new Popup
        {
            PlacementTarget = trigger,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.None,
            Child = surface
        };
        NoteRowOverflowMenuBehavior.SetPopup(trigger, popup);
        var suppressToggle = false;
        trigger.PreviewMouseLeftButtonDown += (_, _) => suppressToggle = popup.IsOpen;
        trigger.Click += (_, _) =>
        {
            if (suppressToggle)
            {
                suppressToggle = false;
                popup.IsOpen = false;
                return;
            }

            popup.IsOpen = true;
        };
        surface.PreviewKeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Escape && popup.IsOpen)
            {
                popup.IsOpen = false;
                eventArgs.Handled = true;
                Keyboard.Focus(trigger);
            }
        };
        menuEdit.Click += (_, _) => popup.IsOpen = false;
        menuEdit.Click += (_, _) => RequestNoteEdit(note);
        menuDelete.Click += (_, _) => popup.IsOpen = false;
        menuDelete.Click += (_, _) => RequestNoteDelete(note);
        return trigger;
    }

    private void RequestNoteEdit(NoteRecord note)
    {
        ManagerWindowLifecycle.RevealNoteEditor(_manager, _desktopWorkspace, note.Id);
    }

    private void RequestNoteDelete(NoteRecord note)
    {
        if (new ConfirmationDialog(this, "删除便签", $"确定删除“{note.Text}”吗？").ShowDialog() != true)
        {
            return;
        }

        var result = _desktopWorkspace.Apply(new DeleteNoteChange(note.Id));
        if (!result.Succeeded)
        {
            SetNoteMessage($"删除失败：{result.Error}");
        }
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

    private Border CreateCalendarIndexDateStamp(NoteRecord note, int rowIndex)
    {
        var date = CalendarIndexDate(note);
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        var palette = CalendarIndexStampPalette[rowIndex % CalendarIndexStampPalette.Length];
        var stampBackground = ThemeBrush(palette[0]);
        var stampText = ThemeBrush(palette[1]);
        var stamp = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stamp.Children.Add(CalendarIndexStampText(
            date is null ? "无日期" : $"{date.Value.Month} 月",
            10,
            FontWeights.Regular,
            stampText));
        stamp.Children.Add(CalendarIndexStampText(
            date is null ? "--" : date.Value.Day.ToString("D2", CultureInfo.InvariantCulture),
            22,
            FontWeights.SemiBold,
            stampText,
            new FontFamily("Bahnschrift SemiCondensed, Bahnschrift, Microsoft YaHei UI"),
            new Thickness(0, 2, 0, 2)));
        stamp.Children.Add(CalendarIndexStampText(
            date is null
                ? " "
                : culture.DateTimeFormat.GetAbbreviatedDayName(date.Value.DayOfWeek),
            10,
            FontWeights.Regular,
            stampText));

        var dateStamp = new Border
        {
            Child = stamp,
            Background = stampBackground,
            BorderBrush = CreateCalendarIndexStampEdgeBrush(stampText),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(4, 8, 4, 8),
            IsHitTestVisible = false,
            Focusable = false
        };
        return dateStamp;
    }

    private static Brush CreateCalendarIndexStampEdgeBrush(Brush stampTextBrush)
    {
        if (stampTextBrush is SolidColorBrush solid)
        {
            var edge = new SolidColorBrush(Color.FromArgb(96, solid.Color.R, solid.Color.G, solid.Color.B));
            edge.Freeze();
            return edge;
        }

        return stampTextBrush;
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
