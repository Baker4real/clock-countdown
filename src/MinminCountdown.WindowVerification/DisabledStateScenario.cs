using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #310：统一禁用态透明度与系统对比度分支，删除 SegmentButton 无用模板与无使用者的 ExitConfirmation。
// 覆盖 App.xaml、MainWindow.xaml 本地样式与 SharedMenuStyles.xaml 全部 22 处受影响样式的日间/系统对比度禁用外观、
// 运行中换色与退出恢复、错误态禁用边界、本地属性保留，以及去重后的模板结构与死代码移除验收。
internal static class DisabledStateScenario
{
    private const double ExpectedDisabledOpacity = 0.45;
    private const double OpacityTolerance = 0.001;
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 10, 1, 0, 0, TimeSpan.Zero);

    private static readonly ApplicationTheme.SystemContrastPalette CaptureHighContrastPalette = new(
        Window: Color.FromRgb(0x08, 0x10, 0x18),
        WindowText: Colors.White,
        Control: Color.FromRgb(0x1B, 0x26, 0x38),
        ControlText: Color.FromRgb(0xF0, 0xF4, 0xF8),
        Highlight: Color.FromRgb(0xFF, 0xD7, 0x00),
        HighlightText: Colors.Black,
        GrayText: Color.FromRgb(0x7D, 0x8F, 0xA3));

    private static readonly ApplicationTheme.SystemContrastPalette AlternateHighContrastPalette = new(
        Window: Colors.White,
        WindowText: Colors.Black,
        Control: Color.FromRgb(0x20, 0x20, 0x20),
        ControlText: Colors.White,
        Highlight: Colors.Navy,
        HighlightText: Colors.White,
        GrayText: Color.FromRgb(0x66, 0x77, 0x88));

    internal static (bool Passed, string Explanation) Run(ApplicationTheme theme, string meaning)
    {
        var failures = new List<string>();
        var captureDirectory = ResolveCaptureDirectory();

        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();
            try
            {
                VerifyAppControlFamilies(theme, captureDirectory, failures);
                VerifyMainLocalControlFamilies(theme, captureDirectory, failures);
                VerifySharedMenuItemDisabledState(theme, captureDirectory, failures);
                VerifyConsolidatedTemplatesAndDeadCodeRemoved(failures);
            }
            catch (Exception exception)
            {
                failures.Add($"禁用态统一验收异常：{exception.GetType().Name}: {exception.Message}");
            }
        }

        theme.Apply();
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    private static void VerifyAppControlFamilies(
        ApplicationTheme theme,
        string? captureDirectory,
        ICollection<string> failures)
    {
        var button = new Button { Content = "默认按钮" };
        var secondary = new Button { Content = "次级按钮", Style = RequiredAppStyle("SecondaryButton") };
        var primary = new Button { Content = "主行动", Style = RequiredAppStyle("PrimaryButton") };
        var danger = new Button { Content = "危险按钮", Style = RequiredAppStyle("DangerButton") };
        var icon = new Button { Content = "⚙", Style = RequiredAppStyle("IconButton") };
        var floatingAccent = new Button { Content = "＋ 添加便签", Height = 38, Padding = new Thickness(16, 0, 16, 0), Style = RequiredAppStyle("FloatingAccentButton") };
        var floatingHeaderIcon = new Button { Content = "⌂", Style = RequiredAppStyle("FloatingHeaderIconButton") };
        var floatingHeaderSegment = new ToggleButton { Content = "倒计时", Width = 85, Height = 36, Style = RequiredAppStyle("FloatingHeaderSegmentButton") };
        var floatingHeaderSegmentChecked = new ToggleButton { Content = "便签", Width = 72, Height = 36, IsChecked = true, Style = RequiredAppStyle("FloatingHeaderSegmentButton") };
        var filterOption = new ToggleButton { Content = "未完成", Style = RequiredAppStyle("FilterOptionButton") };
        var filterOptionChecked = new ToggleButton { Content = "已完成", IsChecked = true, Style = RequiredAppStyle("FilterOptionButton") };
        var tagToggle = new ToggleButton { Content = "工作", IsChecked = true, Style = RequiredAppStyle("TagToggleButton") };
        var textBox = new TextBox { Text = "禁用文本示例", Width = 170 };
        var errorTextBox = new TextBox { Text = "错误态禁用文本", Width = 170 };
        InputErrorState.SetHasError(errorTextBox, true);
        var checkBox = new CheckBox { Content = "未勾选复选" };
        var checkBoxChecked = new CheckBox { Content = "已勾选复选", IsChecked = true };
        var completionCheck = new CheckBox { Content = "完成勾选", IsChecked = true, Style = RequiredAppStyle("CompletionCheckBox") };
        var radio = new RadioButton { Content = "未选单选", GroupName = "AppDisabledVerify" };
        var radioChecked = new RadioButton { Content = "已选单选", GroupName = "AppDisabledVerify", IsChecked = true };
        var combo = new ComboBox { Width = 140, ItemsSource = new[] { "选项一", "选项二" }, SelectedIndex = 0 };
        var errorCombo = new ComboBox { Width = 140, ItemsSource = new[] { "错误一", "错误二" }, SelectedIndex = 0 };
        InputErrorState.SetHasError(errorCombo, true);
        var datePicker = new WpfUiDatePicker { Width = 220, SelectedDate = new DateTime(2038, 7, 10) };
        var errorDatePicker = new WpfUiDatePicker { Width = 220, SelectedDate = new DateTime(2038, 7, 11) };
        InputErrorState.SetHasError(errorDatePicker, true);

        // 本地前景色保留验证（类似 FloatingWindow.CloseFloatingButton）
        var localForegroundButton = new Button
        {
            Content = "✕",
            Style = RequiredAppStyle("FloatingHeaderIconButton")
        };
        localForegroundButton.SetResourceReference(Control.ForegroundProperty, "FloatingHeaderDangerBrush");

        var specs = new ControlSpec[]
        {
            new("App.Button", button, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.SecondaryButton", secondary, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.PrimaryButton", primary, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.DangerButton", danger, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.IconButton", icon, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.FloatingAccentButton", floatingAccent, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.FloatingHeaderIconButton", floatingHeaderIcon, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.FloatingHeaderSegmentButton", floatingHeaderSegment, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.FloatingHeaderSegmentButton(Checked)", floatingHeaderSegmentChecked, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.FilterOptionButton", filterOption, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.FilterOptionButton(Checked)", filterOptionChecked, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.TagToggleButton(Checked)", tagToggle, ["InteractionSurface"], [], CheckMotionSurface: true),
            new("App.TextBox", textBox, ["Surface", "ContentSlot"], ["Surface"], CheckMotionSurface: false),
            new("App.TextBox(HasError)", errorTextBox, ["Surface", "ContentSlot"], ["Surface"], CheckMotionSurface: false),
            new("App.CheckBox", checkBox, ["Surface"], [], CheckMotionSurface: false),
            new("App.CheckBox(Checked)", checkBoxChecked, ["Surface"], [], CheckMotionSurface: false),
            new("App.CompletionCheckBox(Checked)", completionCheck, ["Surface"], [], CheckMotionSurface: false),
            new("App.RadioButton", radio, ["Surface"], [], CheckMotionSurface: false),
            new("App.RadioButton(Checked)", radioChecked, ["Surface"], [], CheckMotionSurface: false),
            new("App.ComboBox", combo, ["Surface", "ContentSlot"], ["Surface"], CheckMotionSurface: false),
            new("App.ComboBox(HasError)", errorCombo, ["Surface", "ContentSlot"], ["Surface"], CheckMotionSurface: false),
            new("App.DatePicker", datePicker, ["PART_Root"], ["PART_TextBox", "PART_Button"], CheckMotionSurface: false),
            new("App.DatePicker(HasError)", errorDatePicker, ["PART_Root"], ["PART_TextBox", "PART_Button"], CheckMotionSurface: false)
        };

        var panel = new WrapPanel { Margin = new Thickness(12), Orientation = Orientation.Horizontal };
        foreach (var spec in specs)
        {
            spec.Control.Margin = new Thickness(6);
            panel.Children.Add(spec.Control);
        }
        localForegroundButton.Margin = new Thickness(6);
        panel.Children.Add(localForegroundButton);

        var root = new Border
        {
            Width = 760,
            Height = 460,
            Padding = new Thickness(8),
            Child = panel
        };
        root.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");

        var host = CreateShowcaseWindow(root);
        try
        {
            host.Show();
            SettleWindow(host);

            var dayDangerColor = RequiredBrushColor("FloatingHeaderDangerBrush");
            var beforeColors = specs.ToDictionary(
                spec => spec.Name,
                spec => CaptureColors(spec.Control),
                StringComparer.Ordinal);

            VerifyControlSpecsMatrix(
                host,
                root,
                specs,
                beforeColors,
                captureDirectory,
                "app-controls",
                theme,
                failures);

            // 验证禁用/高对比往返不破坏控件本地 Foreground DynamicResource
            using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrast.ChangeColors(CaptureHighContrastPalette);
                SettleWindow(host);
                localForegroundButton.IsEnabled = false;
                SettleWindow(host);
                localForegroundButton.IsEnabled = true;
                SettleWindow(host);
            }
            SettleWindow(host);
            if (!BrushColorIs(localForegroundButton.Foreground, dayDangerColor))
            {
                failures.Add($"本地前景色保留失败：往返禁用/系统对比度后颜色变为 {localForegroundButton.Foreground}，预期 {dayDangerColor}");
            }
        }
        finally
        {
            host.Close();
            WindowValidationInput.Pump(host.Dispatcher);
        }
    }

    private static void VerifyMainLocalControlFamilies(
        ApplicationTheme theme,
        string? captureDirectory,
        ICollection<string> failures)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-disabled-main-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        Window? host = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            main.Show();
            SettleLayout(main);
            var projection = (FrameworkElement)main.FindName("MainContentProjection")
                ?? throw new InvalidOperationException("管理窗口缺少 MainContentProjection。");

            Style LocalStyle(string key) => projection.FindResource(key) as Style
                ?? throw new InvalidOperationException($"MainContentProjection 缺少样式 {key}。");

            var noteSegment = new ToggleButton { Content = "未完成", Width = 96, Height = 36, Style = LocalStyle("MainNoteSegmentButton") };
            var noteSegmentChecked = new ToggleButton { Content = "已完成", Width = 96, Height = 36, IsChecked = true, Style = LocalStyle("MainNoteSegmentButton") };
            var dateDisclosure = new Button { Content = "7月10日 · 周六", Width = 150, Style = LocalStyle("MainNoteDateDisclosureButton") };
            var mainPrimary = new Button { Content = "＋ 添加倒计时", Height = 41, Style = LocalStyle("MainPrimaryButton") };
            var mainSecondarySolid = new Button { Content = "保存设置", Height = 41, Style = LocalStyle("MainSecondarySolidButton") };
            var mainSegment = new ToggleButton { Content = "倒计时", Width = 96, Style = LocalStyle("MainSegmentButton") };
            var mainSegmentChecked = new ToggleButton { Content = "倒计时", Width = 96, IsChecked = true, Style = LocalStyle("MainSegmentButton") };
            var mainNotesSegment = new ToggleButton { Content = "便签", Width = 84, Style = LocalStyle("MainNotesSegmentButton") };
            var mainNotesSegmentChecked = new ToggleButton { Content = "便签", Width = 84, IsChecked = true, Style = LocalStyle("MainNotesSegmentButton") };
            var topmostCheck = new CheckBox { Content = "窗口置顶", Style = LocalStyle("MainTopmostCheck") };
            var topmostCheckChecked = new CheckBox { Content = "窗口置顶", IsChecked = true, Style = LocalStyle("MainTopmostCheck") };
            var noteCompletion = new CheckBox { Style = LocalStyle("MainNoteCompletionCheck") };
            var noteCompletionChecked = new CheckBox { IsChecked = true, Style = LocalStyle("MainNoteCompletionCheck") };
            var reminderRadio = new RadioButton { Content = "不提醒", GroupName = "MainDisabledVerify", Style = LocalStyle("MainReminderModeRadio") };
            var reminderRadioChecked = new RadioButton { Content = "按时间提醒", GroupName = "MainDisabledVerify", IsChecked = true, Style = LocalStyle("MainReminderModeRadio") };
            var reminderHelp = new Button { Width = 28, Height = 28, Style = LocalStyle("MainReminderHelpButton") };

            var specs = new ControlSpec[]
            {
                new("Main.MainNoteSegmentButton", noteSegment, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainNoteSegmentButton(Checked)", noteSegmentChecked, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainNoteDateDisclosureButton", dateDisclosure, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainPrimaryButton", mainPrimary, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainSecondarySolidButton", mainSecondarySolid, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainSegmentButton", mainSegment, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainSegmentButton(Checked)", mainSegmentChecked, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainNotesSegmentButton", mainNotesSegment, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainNotesSegmentButton(Checked)", mainNotesSegmentChecked, ["InteractionSurface"], [], CheckMotionSurface: true),
                new("Main.MainTopmostCheck", topmostCheck, ["Surface"], [], CheckMotionSurface: false),
                new("Main.MainTopmostCheck(Checked)", topmostCheckChecked, ["Surface"], [], CheckMotionSurface: false),
                new("Main.MainNoteCompletionCheck", noteCompletion, ["Surface"], [], CheckMotionSurface: false),
                new("Main.MainNoteCompletionCheck(Checked)", noteCompletionChecked, ["Surface"], [], CheckMotionSurface: false),
                new("Main.MainReminderModeRadio", reminderRadio, ["Surface"], [], CheckMotionSurface: false),
                new("Main.MainReminderModeRadio(Checked)", reminderRadioChecked, ["Surface"], [], CheckMotionSurface: false),
                new("Main.MainReminderHelpButton", reminderHelp, ["Surface"], [], CheckMotionSurface: false)
            };

            var panel = new WrapPanel { Margin = new Thickness(12), Orientation = Orientation.Horizontal };
            foreach (var spec in specs)
            {
                spec.Control.Margin = new Thickness(8);
                panel.Children.Add(spec.Control);
            }

            var root = new Border
            {
                Width = 720,
                Height = 320,
                Padding = new Thickness(8),
                Child = panel
            };
            root.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");

            host = CreateShowcaseWindow(root);
            host.Show();
            SettleWindow(host);

            var beforeColors = specs.ToDictionary(
                spec => spec.Name,
                spec => CaptureColors(spec.Control),
                StringComparer.Ordinal);

            VerifyControlSpecsMatrix(
                host,
                root,
                specs,
                beforeColors,
                captureDirectory,
                "main-local-controls",
                theme,
                failures);
        }
        finally
        {
            if (host is not null)
            {
                host.Close();
                WindowValidationInput.Pump(host.Dispatcher);
            }

            if (main is not null)
            {
                main.Close();
                WindowValidationInput.Pump(main.Dispatcher);
            }

            DeleteSettingsFiles(settingsPath);
            if (File.Exists(diagnosticPath))
            {
                File.Delete(diagnosticPath);
            }
        }
    }

    private static void VerifySharedMenuItemDisabledState(
        ApplicationTheme theme,
        string? captureDirectory,
        ICollection<string> failures)
    {
        var menu = new ContextMenu();
        SharedMenuStyles.Apply(menu);
        var enabledItem = new MenuItem { Header = "打开管理窗口" };
        var disabledItem = new MenuItem { Header = "禁用菜单项", IsEnabled = false };
        menu.Items.Add(enabledItem);
        menu.Items.Add(disabledItem);

        var dayInk = RequiredBrushColor("InkBrush");
        var dayCanvas = RequiredBrushColor("CanvasBrush");

        try
        {
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = -20_000;
            menu.VerticalOffset = -20_000;
            menu.IsOpen = true;
            SettleElement(menu);

            if (Math.Abs(disabledItem.Opacity - ExpectedDisabledOpacity) > OpacityTolerance)
            {
                failures.Add($"SharedMenuItem 普通禁用透明度异常：actual={disabledItem.Opacity}");
            }

            if (Math.Abs(enabledItem.Opacity - 1.0) > OpacityTolerance)
            {
                failures.Add($"SharedMenuItem 常态透明度异常：actual={enabledItem.Opacity}");
            }

            CaptureElement(menu, captureDirectory, "shared-menu-day-disabled.png");

            using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrast.ChangeColors(CaptureHighContrastPalette);
                SettleElement(menu);

                if (Math.Abs(disabledItem.Opacity - 1.0) > OpacityTolerance ||
                    !BrushColorIs(disabledItem.Foreground, CaptureHighContrastPalette.GrayText) ||
                    !BrushColorIs(disabledItem.Background, CaptureHighContrastPalette.Control))
                {
                    failures.Add(
                        $"SharedMenuItem 系统对比度禁用异常：opacity={disabledItem.Opacity}, fg={disabledItem.Foreground}, bg={disabledItem.Background}");
                }

                CaptureElement(menu, captureDirectory, "shared-menu-hc-disabled.png");

                contrast.ChangeColors(AlternateHighContrastPalette);
                SettleElement(menu);

                if (!BrushColorIs(disabledItem.Foreground, AlternateHighContrastPalette.GrayText) ||
                    !BrushColorIs(disabledItem.Background, AlternateHighContrastPalette.Control))
                {
                    failures.Add(
                        $"SharedMenuItem 系统对比度换色后禁用未更新：fg={disabledItem.Foreground}, bg={disabledItem.Background}");
                }
            }

            SettleElement(menu);
            if (Math.Abs(disabledItem.Opacity - ExpectedDisabledOpacity) > OpacityTolerance ||
                !BrushColorIs(disabledItem.Foreground, dayInk) ||
                !BrushColorIs(disabledItem.Background, dayCanvas))
            {
                failures.Add(
                    $"SharedMenuItem 退出系统对比度后未恢复日间禁用态：opacity={disabledItem.Opacity}, fg={disabledItem.Foreground}, bg={disabledItem.Background}");
            }

            disabledItem.IsEnabled = true;
            SettleElement(menu);
            if (Math.Abs(disabledItem.Opacity - 1.0) > OpacityTolerance ||
                !BrushColorIs(disabledItem.Foreground, dayInk) ||
                !BrushColorIs(disabledItem.Background, dayCanvas))
            {
                failures.Add(
                    $"SharedMenuItem 重新启用后未恢复常态：opacity={disabledItem.Opacity}, fg={disabledItem.Foreground}, bg={disabledItem.Background}");
            }

            // 关闭再打开菜单并重新禁用，验证 Popup/ContextMenu Unloaded->Loaded 周期正常
            menu.IsOpen = false;
            SettleElement(menu);
            disabledItem.IsEnabled = false;
            menu.IsOpen = true;
            SettleElement(menu);
            if (Math.Abs(disabledItem.Opacity - ExpectedDisabledOpacity) > OpacityTolerance)
            {
                failures.Add($"SharedMenuItem 重开菜单后禁用透明度异常：actual={disabledItem.Opacity}");
            }
        }
        finally
        {
            menu.IsOpen = false;
            SettleElement(menu);
            theme.Apply();
        }
    }

    private static void VerifyControlSpecsMatrix(
        Window host,
        FrameworkElement captureRoot,
        IReadOnlyList<ControlSpec> specs,
        IReadOnlyDictionary<string, ControlColors> beforeColors,
        string? captureDirectory,
        string capturePrefix,
        ApplicationTheme theme,
        ICollection<string> failures)
    {
        // 1) 日间禁用态
        foreach (var spec in specs)
        {
            spec.Control.IsEnabled = false;
        }
        SettleWindow(host);

        foreach (var spec in specs)
        {
            foreach (var partName in spec.OpacityParts)
            {
                if (spec.Control.Template?.FindName(partName, spec.Control) is not FrameworkElement part)
                {
                    failures.Add($"{spec.Name} 缺少模板部件 {partName}");
                    continue;
                }

                if (Math.Abs(part.Opacity - ExpectedDisabledOpacity) > OpacityTolerance)
                {
                    failures.Add($"{spec.Name}.{partName} 日间禁用透明度为 {part.Opacity:0.###}，预期 0.45");
                }
            }
        }

        CaptureElement(captureRoot, captureDirectory, $"{capturePrefix}-day-disabled.png");

        // 2) 系统对比度禁用态 + 运行中换色
        using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            contrast.ChangeColors(CaptureHighContrastPalette);
            SettleWindow(host);

            VerifyHighContrastDisabledSpecs(specs, CaptureHighContrastPalette, "HC1", failures);
            CaptureElement(captureRoot, captureDirectory, $"{capturePrefix}-hc-disabled.png");

            contrast.ChangeColors(AlternateHighContrastPalette);
            SettleWindow(host);

            VerifyHighContrastDisabledSpecs(specs, AlternateHighContrastPalette, "HC2", failures);

            // 3) 在系统对比度下重新启用再禁用，验证启禁用往返同步
            foreach (var spec in specs)
            {
                spec.Control.IsEnabled = true;
            }
            SettleWindow(host);

            foreach (var spec in specs)
            {
                if (BrushColorIs(spec.Control.Foreground, AlternateHighContrastPalette.GrayText))
                {
                    failures.Add($"{spec.Name} 在系统对比度下重新启用后仍残留 GrayText 前景");
                }
            }

            foreach (var spec in specs)
            {
                spec.Control.IsEnabled = false;
            }
            SettleWindow(host);
            VerifyHighContrastDisabledSpecs(specs, AlternateHighContrastPalette, "HC2-ReDisabled", failures);
        }

        // 4) 关闭系统对比度后（仍保持禁用），自动回到日间 0.45 禁用外观
        theme.Apply();
        SettleWindow(host);
        foreach (var spec in specs)
        {
            foreach (var partName in spec.OpacityParts)
            {
                if (spec.Control.Template?.FindName(partName, spec.Control) is FrameworkElement part &&
                    Math.Abs(part.Opacity - ExpectedDisabledOpacity) > OpacityTolerance)
                {
                    failures.Add($"{spec.Name}.{partName} 退出系统对比度后未回到日间 0.45 透明度（actual={part.Opacity:0.###}）");
                }
            }
        }

        // 5) 重新启用后完全恢复日间常态颜色与 1.0 透明度
        foreach (var spec in specs)
        {
            spec.Control.IsEnabled = true;
        }
        SettleWindow(host);
        foreach (var spec in specs)
        {
            foreach (var partName in spec.OpacityParts)
            {
                if (spec.Control.Template?.FindName(partName, spec.Control) is FrameworkElement part &&
                    Math.Abs(part.Opacity - 1.0) > OpacityTolerance)
                {
                    failures.Add($"{spec.Name}.{partName} 重新启用后透明度未恢复 1.0（actual={part.Opacity:0.###}）");
                }
            }

            var restored = CaptureColors(spec.Control);
            var original = beforeColors[spec.Name];
            if (restored != original)
            {
                failures.Add($"{spec.Name} 恢复常态后颜色漂移：before={original}，after={restored}");
            }
        }
    }

    private static void VerifyHighContrastDisabledSpecs(
        IReadOnlyList<ControlSpec> specs,
        ApplicationTheme.SystemContrastPalette palette,
        string phase,
        ICollection<string> failures)
    {
        foreach (var spec in specs)
        {
            foreach (var partName in spec.OpacityParts)
            {
                if (spec.Control.Template?.FindName(partName, spec.Control) is FrameworkElement part &&
                    Math.Abs(part.Opacity - 1.0) > OpacityTolerance)
                {
                    failures.Add($"{spec.Name}.{partName} ({phase}) 系统对比度禁用透明度应为 1.0，实际 {part.Opacity:0.###}");
                }
            }

            if (!BrushColorIs(spec.Control.Foreground, palette.GrayText))
            {
                failures.Add($"{spec.Name} ({phase}) Foreground={spec.Control.Foreground}，预期 GrayText({palette.GrayText})");
            }

            if (!BrushColorIs(spec.Control.Background, palette.Control))
            {
                failures.Add($"{spec.Name} ({phase}) Background={spec.Control.Background}，预期 Control({palette.Control})");
            }

            if (!BrushColorIs(spec.Control.BorderBrush, palette.GrayText))
            {
                failures.Add($"{spec.Name} ({phase}) BorderBrush={spec.Control.BorderBrush}，预期 GrayText({palette.GrayText})");
            }

            if (spec.CheckMotionSurface &&
                spec.Control.Template?.FindName("Surface", spec.Control) is Border motionSurface)
            {
                if (!BrushColorIs(motionSurface.Background, palette.Control))
                {
                    failures.Add($"{spec.Name}.Surface ({phase}) Background={motionSurface.Background}，预期 Control({palette.Control})");
                }

                if (!BrushColorIs(motionSurface.BorderBrush, palette.GrayText))
                {
                    failures.Add($"{spec.Name}.Surface ({phase}) BorderBrush={motionSurface.BorderBrush}，预期 GrayText({palette.GrayText})");
                }
            }

            foreach (var borderPartName in spec.ContrastBorderParts)
            {
                var borderTarget = spec.Control.Template?.FindName(borderPartName, spec.Control);
                var borderBrush = borderTarget switch
                {
                    Border border => border.BorderBrush,
                    Control nested => nested.BorderBrush,
                    _ => null
                };
                if (!BrushColorIs(borderBrush, palette.GrayText))
                {
                    failures.Add($"{spec.Name}.{borderPartName} ({phase}) BorderBrush={borderBrush}，预期 GrayText({palette.GrayText})");
                }
            }
        }
    }

    private static void VerifyConsolidatedTemplatesAndDeadCodeRemoved(ICollection<string> failures)
    {
        // 1) SegmentButton 仅保留被衍生样式 FilterOptionButton 继承的属性设置，且无 ControlTemplate
        var segmentStyle = RequiredAppStyle("SegmentButton");
        var filterStyle = RequiredAppStyle("FilterOptionButton");
        if (!ReferenceEquals(filterStyle.BasedOn, segmentStyle))
        {
            failures.Add("FilterOptionButton 必须继续 BasedOn SegmentButton");
        }

        var segmentProperties = segmentStyle.Setters
            .OfType<Setter>()
            .Select(setter => setter.Property)
            .ToHashSet();
        var expectedInheritedProperties = new HashSet<DependencyProperty>
        {
            Control.ForegroundProperty,
            Control.BorderThicknessProperty,
            Control.PaddingProperty,
            FrameworkElement.CursorProperty,
            FrameworkElement.FocusVisualStyleProperty
        };
        if (!segmentProperties.SetEquals(expectedInheritedProperties) || segmentStyle.Triggers.Count > 0)
        {
            var actualNames = string.Join(",", segmentProperties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
            failures.Add($"SegmentButton 应仅保留被继承的 5 个属性设置且无模板/触发器，实际=[{actualNames}], triggers={segmentStyle.Triggers.Count}");
        }

        // 2) 全部 22 处样式与模板均不再各自重复 IsEnabled=False 或 IsSystemContrast 禁用触发器
        var appStyleKeys = new object[]
        {
            typeof(Button),
            "PrimaryButton",
            "FloatingAccentButton",
            "FloatingHeaderIconButton",
            "FloatingHeaderSegmentButton",
            "FilterOptionButton",
            typeof(TextBox),
            typeof(CheckBox),
            typeof(RadioButton),
            typeof(ComboBox),
            typeof(DatePicker)
        };
        foreach (var key in appStyleKeys)
        {
            var style = Application.Current.FindResource(key) as Style
                ?? throw new InvalidOperationException($"App.xaml 缺少样式 {key}");
            AssertNoDuplicatedDisabledTriggers($"App:{key}", style, failures);
        }

        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-disabled-inspect-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory);
            var projection = (FrameworkElement)main.FindName("MainContentProjection")
                ?? throw new InvalidOperationException("管理窗口缺少 MainContentProjection。");
            var mainStyleKeys = new[]
            {
                "MainNoteSegmentButton",
                "MainNoteDateDisclosureButton",
                "MainPrimaryButton",
                "MainSecondarySolidButton",
                "MainSegmentButton",
                "MainNotesSegmentButton",
                "MainTopmostCheck",
                "MainNoteCompletionCheck",
                "MainReminderModeRadio",
                "MainReminderHelpButton"
            };
            foreach (var key in mainStyleKeys)
            {
                var style = projection.FindResource(key) as Style
                    ?? throw new InvalidOperationException($"MainWindow.xaml 缺少样式 {key}");
                AssertNoDuplicatedDisabledTriggers($"Main:{key}", style, failures);
            }
        }
        finally
        {
            main?.Close();
            DeleteSettingsFiles(settingsPath);
            if (File.Exists(diagnosticPath))
            {
                File.Delete(diagnosticPath);
            }
        }

        var menuStyles = new ResourceDictionary
        {
            Source = new Uri("/MinminCountdown;component/SharedMenuStyles.xaml", UriKind.Relative)
        };
        if (menuStyles["SharedMenuItemStyle"] is Style sharedMenuItemStyle)
        {
            AssertNoDuplicatedDisabledTriggers("SharedMenuStyles:SharedMenuItemStyle", sharedMenuItemStyle, failures);
        }
        else
        {
            failures.Add("SharedMenuStyles.xaml 缺少 SharedMenuItemStyle");
        }

        // 3) 已废弃的 ExitConfirmation 类已从产品程序集删除
        if (typeof(App).Assembly.GetType("MinminCountdown.ExitConfirmation") is not null)
        {
            failures.Add("无使用者的 ExitConfirmation 类仍存在于产品程序集中");
        }
    }

    private static void AssertNoDuplicatedDisabledTriggers(
        string label,
        Style style,
        ICollection<string> failures)
    {
        if (HasDisabledTrigger(style.Triggers))
        {
            failures.Add($"{label} 的 Style.Triggers 仍保留重复的禁用态触发器");
        }

        var template = style.Setters
            .OfType<Setter>()
            .Where(setter => setter.Property == Control.TemplateProperty)
            .Select(setter => setter.Value)
            .OfType<ControlTemplate>()
            .FirstOrDefault();
        if (template is not null && HasDisabledTrigger(template.Triggers))
        {
            failures.Add($"{label} 的 ControlTemplate.Triggers 仍保留重复的禁用态触发器");
        }
    }

    private static bool HasDisabledTrigger(TriggerCollection triggers)
    {
        foreach (var trigger in triggers)
        {
            switch (trigger)
            {
                case Trigger single when single.Property == UIElement.IsEnabledProperty && Equals(single.Value, false):
                    return true;
                case MultiTrigger multi when multi.Conditions.Any(condition =>
                    condition.Property == UIElement.IsEnabledProperty && Equals(condition.Value, false)):
                    return true;
            }
        }

        return false;
    }

    private static Window CreateShowcaseWindow(FrameworkElement root)
    {
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        return new Window
        {
            Width = root.Width,
            Height = root.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0.01,
            Content = root
        };
    }

    private static void SettleWindow(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static void SettleElement(FrameworkElement element)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(element.Dispatcher);
            element.UpdateLayout();
        }
    }

    private static string? ResolveCaptureDirectory()
    {
        var explicitDirectory = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_EVIDENCE_310_DIR");
        if (string.IsNullOrWhiteSpace(explicitDirectory))
        {
            return null;
        }

        var resolved = Path.GetFullPath(explicitDirectory);
        Directory.CreateDirectory(resolved);
        return resolved;
    }

    private static void CaptureElement(FrameworkElement element, string? directory, string fileName)
    {
        if (directory is null)
        {
            return;
        }

        TextOptions.SetTextRenderingMode(element, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);
        SettleElement(element);
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            Path.Combine(directory, fileName),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        encoder.Save(stream);
    }

    private static Style RequiredAppStyle(object key) =>
        Application.Current.FindResource(key) as Style
        ?? throw new InvalidOperationException($"App.xaml 缺少样式 {key}。");

    private static Color RequiredBrushColor(string key) =>
        Application.Current.FindResource(key) is SolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"缺少画刷资源 {key}。");

    private static bool BrushColorIs(Brush? brush, Color expected) =>
        brush is SolidColorBrush solid && solid.Color == expected;

    private static ControlColors CaptureColors(Control control) =>
        new(
            (control.Foreground as SolidColorBrush)?.Color,
            (control.Background as SolidColorBrush)?.Color,
            (control.BorderBrush as SolidColorBrush)?.Color);

    private sealed record ControlSpec(
        string Name,
        Control Control,
        string[] OpacityParts,
        string[] ContrastBorderParts,
        bool CheckMotionSurface);

    private readonly record struct ControlColors(Color? Foreground, Color? Background, Color? BorderBrush);
}
