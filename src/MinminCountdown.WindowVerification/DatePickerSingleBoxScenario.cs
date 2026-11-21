using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;
using WpfCalendar = System.Windows.Controls.Calendar;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown.WindowVerification;

// #312：全应用六处日期框单框形态、与下拉框一致的几何、单层焦点边界、Tab 停靠、
// 文本/箭头双入口打开日历、只读中文日期、Esc 取消还原、日历键盘导航与最长日期完整显示的独立窗口验证。
internal static class DatePickerSingleBoxScenario
{
    private const double GeometryTolerance = 0.5;
    private const double ExpectedArrowColumnWidth = 36.0;
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTime LongestDate = new(2031, 12, 31);
    private static readonly string LongestDateText = DateDisplay.FullDate(DateOnly.FromDateTime(LongestDate));

    private sealed record DatePickerSite(
        string Name,
        Window HostWindow,
        DatePicker Picker,
        ComboBox? ReferenceCombo,
        double ExpectedHeight,
        FrameworkElement? LayoutShiftSibling,
        IInputElement? PreviousTabElement,
        Action PrepareVisible,
        Action CleanupVisible,
        Action<double>? ResizeHost = null,
        double? NarrowHostWidth = null,
        bool HasLeadingIcon = false,
        Visual? LayoutRoot = null);

    internal static (bool Passed, string Explanation) RunGeometryAndFocus(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        var metrics = new List<string>();
        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();
            try
            {
                WithAllSixSites((sites, defaultCombo) =>
                {
                    var expectedRadius = RequiredCornerRadius(sites[0].HostWindow, "RadiusControl");
                    var comboMetrics = ReadComboBoxReferenceGeometry(defaultCombo);
                    metrics.Add(
                        $"RefCombo:h={comboMetrics.Height:0.##},r={FormatRadius(comboMetrics.Radius)},arrowCol={comboMetrics.ArrowColumnWidth:0.##},arrowSurfaceR={FormatRadius(comboMetrics.ArrowSurfaceRadius)}");

                    foreach (var site in sites)
                    {
                        site.PrepareVisible();
                        try
                        {
                            VerifySingleBoxGeometry(site, comboMetrics, expectedRadius, failures, metrics);
                            VerifyFocusErrorAndTabStop(site, failures);
                        }
                        finally
                        {
                            site.CleanupVisible();
                        }
                    }

                    // 重点在主窗倒计时日期框验证系统高对比度下有效焦点 vs 错误态与禁用态。
                    var countdownSite = sites[0];
                    countdownSite.PrepareVisible();
                    try
                    {
                        VerifyHighContrastAndDisabled(countdownSite, theme, failures);
                    }
                    finally
                    {
                        countdownSite.CleanupVisible();
                    }
                });
            }
            finally
            {
                theme.Apply();
            }
        }

        Console.Error.WriteLine("DATEPICKER_SINGLE_BOX_GEOMETRY: " + string.Join(" | ", metrics));
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；未通过：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunInteractionAndLongestDateFit(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        var fitMetrics = new List<string>();
        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();
            try
            {
                WithAllSixSites((sites, _) =>
                {
                    foreach (var site in sites)
                    {
                        site.PrepareVisible();
                        try
                        {
                            VerifyInteractionsAndCalendar(site, failures);
                            VerifyLongestDateFits(site, "default", failures, fitMetrics);

                            if (site.ResizeHost is not null && site.NarrowHostWidth is { } narrowWidth)
                            {
                                var originalWidth = site.HostWindow.Width;
                                var originalSizeToContent = site.HostWindow.SizeToContent;
                                try
                                {
                                    site.ResizeHost(narrowWidth);
                                    SettleWindow(site.HostWindow);
                                    if (Math.Abs(site.HostWindow.ActualWidth - narrowWidth) > 2.0)
                                    {
                                        failures.Add(
                                            $"{site.Name} 缩小宿主窗口宽度未生效：target={narrowWidth:0}，actual={site.HostWindow.ActualWidth:0.##}。");
                                    }
                                    VerifyLongestDateFits(site, $"minWidth={narrowWidth:0}", failures, fitMetrics);
                                }
                                finally
                                {
                                    site.ResizeHost(originalWidth);
                                    site.HostWindow.SizeToContent = originalSizeToContent;
                                    SettleWindow(site.HostWindow);
                                }
                            }
                        }
                        finally
                        {
                            site.CleanupVisible();
                        }
                    }
                });
            }
            finally
            {
                theme.Apply();
            }
        }

        Console.Error.WriteLine("DATEPICKER_LONGEST_DATE_FIT: " + string.Join(" | ", fitMetrics));
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；未通过：{string.Join(" | ", failures)}");
    }

    private sealed record ComboBoxReferenceGeometry(
        double Height,
        CornerRadius Radius,
        double ArrowColumnWidth,
        CornerRadius ArrowSurfaceRadius);

    private static ComboBoxReferenceGeometry ReadComboBoxReferenceGeometry(ComboBox combo)
    {
        combo.ApplyTemplate();
        combo.UpdateLayout();
        var surface = combo.Template?.FindName("Surface", combo) as Border
            ?? throw new InvalidOperationException("参考 ComboBox 缺少 Surface。");
        var contentSlot = combo.Template?.FindName("ContentSlot", combo) as Border
            ?? throw new InvalidOperationException("参考 ComboBox 缺少 ContentSlot。");
        var grid = contentSlot.Child as Grid
            ?? throw new InvalidOperationException("参考 ComboBox ContentSlot 缺少内部 Grid。");
        var arrowCol = grid.ColumnDefinitions.Count == 2 && grid.ColumnDefinitions[1].Width.IsAbsolute
            ? grid.ColumnDefinitions[1].Width.Value
            : double.NaN;
        var arrowToggle = PickerTemplateParts.GetArrowToggle(combo)
            ?? throw new InvalidOperationException("参考 ComboBox 缺少箭头 ToggleButton。");
        arrowToggle.ApplyTemplate();
        var arrowSurface = arrowToggle.Template?.FindName("ArrowSurface", arrowToggle) as Border
            ?? throw new InvalidOperationException("参考 ComboBox 箭头按钮缺少 ArrowSurface。");
        return new ComboBoxReferenceGeometry(
            combo.ActualHeight,
            surface.CornerRadius,
            arrowCol,
            arrowSurface.CornerRadius);
    }

    private static void VerifySingleBoxGeometry(
        DatePickerSite site,
        ComboBoxReferenceGeometry defaultCombo,
        CornerRadius expectedRadius,
        ICollection<string> failures,
        ICollection<string> metrics)
    {
        var picker = site.Picker;
        picker.ApplyTemplate();
        SettleWindow(site.HostWindow);

        var refCombo = site.ReferenceCombo is not null
            ? ReadComboBoxReferenceGeometry(site.ReferenceCombo)
            : defaultCombo;

        // 1) 单一外框 Surface 与 ContentSlot 结构
        if (picker.Template?.FindName("Surface", picker) is not Border surface)
        {
            failures.Add($"{site.Name} 缺少统一外框 Border(Surface)。");
            return;
        }

        if (picker.Template.FindName("ContentSlot", picker) is not Border contentSlot)
        {
            failures.Add($"{site.Name} 缺少内容槽 Border(ContentSlot)。");
            return;
        }

        if (picker.Template.FindName("PART_TextBox", picker) is not DatePickerTextBox textBox)
        {
            failures.Add($"{site.Name} 缺少 PART_TextBox。");
            return;
        }

        if (picker.Template.FindName("PART_Button", picker) is not Wpf.Ui.Controls.Button button)
        {
            failures.Add($"{site.Name} 缺少 Wpf.Ui.Controls.Button 类型的 PART_Button。");
            return;
        }

        textBox.ApplyTemplate();
        button.ApplyTemplate();
        SettleWindow(site.HostWindow);

        if (surface.CornerRadius != expectedRadius || surface.CornerRadius != refCombo.Radius)
        {
            failures.Add(
                $"{site.Name} 外框圆角不等于 RadiusControl/ComboBox：actual={FormatRadius(surface.CornerRadius)}，expected={FormatRadius(expectedRadius)}，combo={FormatRadius(refCombo.Radius)}。");
        }

        if (Math.Abs(picker.ActualHeight - site.ExpectedHeight) > GeometryTolerance)
        {
            failures.Add(
                $"{site.Name} 高度不匹配预期：actual={picker.ActualHeight:0.##}，expected={site.ExpectedHeight:0.##}。");
        }

        if (site.ReferenceCombo is not null &&
            Math.Abs(picker.ActualHeight - refCombo.Height) > GeometryTolerance)
        {
            failures.Add(
                $"{site.Name} 高度与同屏 ComboBox 不一致：date={picker.ActualHeight:0.##}，combo={refCombo.Height:0.##}。");
        }

        if (Math.Abs(surface.ActualWidth - picker.ActualWidth) > GeometryTolerance ||
            Math.Abs(surface.ActualHeight - picker.ActualHeight) > GeometryTolerance)
        {
            failures.Add(
                $"{site.Name} 外框 Surface 未覆盖整框：picker={picker.ActualWidth:0.##}x{picker.ActualHeight:0.##}，surface={surface.ActualWidth:0.##}x{surface.ActualHeight:0.##}。");
        }

        // 2) 常态外框为 1 DIP ControlBorderBrush，内部文本框与箭头按钮均无内层边框与底色遮挡
        var expectedIdleBorder = RequiredBrush(picker, "ControlBorderBrush");
        if (!ThicknessEquals(surface.BorderThickness, 1) ||
            !BrushColorMatches(surface.BorderBrush, expectedIdleBorder))
        {
            failures.Add(
                $"{site.Name} 常态外框边界异常：thickness={surface.BorderThickness}，brush={surface.BorderBrush}，expected={expectedIdleBorder}。");
        }

        if (!ThicknessEquals(textBox.BorderThickness, 0) ||
            !IsTransparent(textBox.Background))
        {
            failures.Add(
                $"{site.Name} PART_TextBox 不应绘制独立内框或底色：thickness={textBox.BorderThickness}，bg={textBox.Background}。");
        }

        if (textBox.Template?.FindName("Surface", textBox) is Border innerTextSurface &&
            !ThicknessEquals(innerTextSurface.BorderThickness, 0))
        {
            failures.Add($"{site.Name} PART_TextBox 内部仍存在带边框的 Surface：{innerTextSurface.BorderThickness}。");
        }

        if (!ThicknessEquals(button.BorderThickness, 0) ||
            !IsTransparent(button.Background))
        {
            failures.Add(
                $"{site.Name} PART_Button 不应绘制独立内框或底色：thickness={button.BorderThickness}，bg={button.Background}。");
        }

        if (button.Template?.FindName("FocusRing", button) is not null)
        {
            failures.Add($"{site.Name} PART_Button 不应包含独立 FocusRing 导致内层焦框或缩高。");
        }

        // 3) 箭头列宽 36 DIP（与 ComboBox 一致），且箭头按钮紧邻文本区无缝隙、完全落于外框之内且等高
        if (!ThicknessEquals(contentSlot.BorderThickness, 1) ||
            contentSlot.Child is not Grid innerGrid ||
            innerGrid.ColumnDefinitions.Count != 2 ||
            !innerGrid.ColumnDefinitions[1].Width.IsAbsolute ||
            Math.Abs(innerGrid.ColumnDefinitions[1].Width.Value - ExpectedArrowColumnWidth) > 0.01 ||
            Math.Abs(innerGrid.ColumnDefinitions[1].Width.Value - refCombo.ArrowColumnWidth) > 0.01)
        {
            failures.Add($"{site.Name} 内部箭头列几何与 ComboBox(36 DIP) 不一致。");
            return;
        }

        var surfaceRect = Bounds(surface, picker);
        var textRect = Bounds(textBox, picker);
        var buttonRect = Bounds(button, picker);

        if (Math.Abs(textRect.Right - buttonRect.Left) > GeometryTolerance)
        {
            failures.Add(
                $"{site.Name} 文本区与箭头区之间存在缝隙或重叠：textRight={textRect.Right:0.##}，buttonLeft={buttonRect.Left:0.##}。");
        }

        if (buttonRect.Left < surfaceRect.Left + 0.5 ||
            buttonRect.Right > surfaceRect.Right - 0.5 ||
            buttonRect.Top < surfaceRect.Top + 0.5 ||
            buttonRect.Bottom > surfaceRect.Bottom - 0.5)
        {
            failures.Add(
                $"{site.Name} 箭头按钮未严格落在外框边界之内：button={buttonRect}，surface={surfaceRect}。");
        }

        var expectedInnerHeight = surfaceRect.Height - 2.0;
        if (Math.Abs(buttonRect.Height - expectedInnerHeight) > GeometryTolerance ||
            Math.Abs(textRect.Height - expectedInnerHeight) > GeometryTolerance)
        {
            failures.Add(
                $"{site.Name} 箭头按钮高度偏矮或与文本区不等高：textH={textRect.Height:0.##}，buttonH={buttonRect.Height:0.##}，expectedInnerH={expectedInnerHeight:0.##}。");
        }

        if (button.Template?.FindName("ArrowSurface", button) is not Border arrowSurface)
        {
            failures.Add($"{site.Name} PART_Button 缺少 ArrowSurface。");
            return;
        }

        if (arrowSurface.CornerRadius != refCombo.ArrowSurfaceRadius)
        {
            failures.Add(
                $"{site.Name} ArrowSurface 圆角与 ComboBox 不一致：actual={FormatRadius(arrowSurface.CornerRadius)}，combo={FormatRadius(refCombo.ArrowSurfaceRadius)}。");
        }

        var arrowSurfaceRect = Bounds(arrowSurface, picker);
        if (arrowSurfaceRect.Top < surfaceRect.Top + 1.5 ||
            arrowSurfaceRect.Bottom > surfaceRect.Bottom - 1.5 ||
            arrowSurfaceRect.Right > surfaceRect.Right - 1.5)
        {
            failures.Add(
                $"{site.Name} ArrowSurface 未给 2 DIP 焦点边留出内缩空间：arrowSurface={arrowSurfaceRect}，surface={surfaceRect}。");
        }

        var arrowGlyph = VisualDescendants<WpfPath>(arrowSurface).FirstOrDefault();
        if (arrowGlyph is null)
        {
            failures.Add($"{site.Name} ArrowSurface 内缺少下拉箭头 Path。");
        }
        else
        {
            var glyphRect = Bounds(arrowGlyph, picker);
            if (glyphRect.Width < 8 || glyphRect.Height < 4 ||
                glyphRect.Left < arrowSurfaceRect.Left ||
                glyphRect.Right > arrowSurfaceRect.Right ||
                glyphRect.Top < arrowSurfaceRect.Top ||
                glyphRect.Bottom > arrowSurfaceRect.Bottom)
            {
                failures.Add($"{site.Name} 下拉箭头字形越出箭头区域：glyph={glyphRect}，arrowSurface={arrowSurfaceRect}。");
            }
        }

        // 4) 规则编辑器日期框与紧邻时刻输入框之间有干净间距、不重叠
        if (site.Name == "ReminderRules.FirstDueDate" && site.LayoutShiftSibling is not null)
        {
            var root = (Visual)site.HostWindow.Content;
            var dateBounds = Bounds(picker, root);
            var timeBounds = Bounds(site.LayoutShiftSibling, root);
            var gap = timeBounds.Left - dateBounds.Right;
            if (gap < 4.0)
            {
                failures.Add($"ReminderRules.FirstDueDate 与时刻框间距过窄或碰撞：gap={gap:0.##}。");
            }
        }

        metrics.Add(
            $"{site.Name}:w={picker.ActualWidth:0.##},h={picker.ActualHeight:0.##}(refComboH={refCombo.Height:0.##}),r={FormatRadius(surface.CornerRadius)},arrowCol={innerGrid.ColumnDefinitions[1].Width.Value:0.##},btnH={buttonRect.Height:0.##},gap={Math.Abs(textRect.Right - buttonRect.Left):0.##}");
    }

    private static void VerifyFocusErrorAndTabStop(
        DatePickerSite site,
        ICollection<string> failures)
    {
        var picker = site.Picker;
        var host = site.HostWindow;
        var root = site.LayoutRoot ?? ResolveCoordinateRoot(picker, host);
        picker.ApplyTemplate();
        var surface = picker.Template?.FindName("Surface", picker) as Border;
        var button = picker.Template?.FindName("PART_Button", picker) as Wpf.Ui.Controls.Button;
        var textBox = picker.Template?.FindName("PART_TextBox", picker) as DatePickerTextBox;
        if (surface is null || button is null || textBox is null)
        {
            return;
        }

        // 箭头按钮必须是键盘 Tab 停靠点，且不挂系统/自定义装饰层，由宿主绘制单层焦点。
        if (!button.Focusable || !button.IsTabStop)
        {
            failures.Add($"{site.Name} PART_Button 必须是键盘 Tab 停靠点（Focusable={button.Focusable}, IsTabStop={button.IsTabStop}）。");
        }

        if (button.FocusVisualStyle is not null || picker.FocusVisualStyle is not null || textBox.FocusVisualStyle is not null)
        {
            failures.Add($"{site.Name} 日期框或内部部件残留 FocusVisualStyle，可能叠出第二层焦点框。");
        }

        if (!FocusVisualState.GetHostDrawsFocus(button))
        {
            failures.Add($"{site.Name} PART_Button 缺少 FocusVisualState.HostDrawsFocus=True。");
        }

        var originalError = InputErrorState.GetHasError(picker);
        var restingPickerBounds = Bounds(picker, root);
        var restingSiblingBounds = site.LayoutShiftSibling is not null ? Bounds(site.LayoutShiftSibling, root) : (Rect?)null;
        try
        {
            InputErrorState.SetHasError(picker, false);
            if (!host.IsActive)
            {
                host.Activate();
                SettleWindow(host);
            }

            // 若有前序停靠点，真实按 Tab 方向移动焦点进入日期框的 PART_Button。
            if (site.PreviousTabElement is FrameworkElement prev)
            {
                Keyboard.Focus(prev);
                SettleWindow(host);
                prev.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                SettleWindow(host);
                if (!ReferenceEquals(Keyboard.FocusedElement, button))
                {
                    failures.Add(
                        $"{site.Name} 从前序控件 Tab 未落到 PART_Button：actual={DescribeElement(Keyboard.FocusedElement)}。");
                }
            }
            else
            {
                Keyboard.Focus(button);
                SettleWindow(host);
            }

            if (!picker.IsKeyboardFocusWithin || !ReferenceEquals(Keyboard.FocusedElement, button))
            {
                failures.Add($"{site.Name} PART_Button 未能取得键盘焦点。");
            }

            // 有效焦点：仅外层 Surface 为 2 DIP 暖金 MainActionFocusBrush，单层焦点判定通过，无布局抖动。
            var focusBrush = RequiredBrush(picker, "MainActionFocusBrush");
            if (!ThicknessEquals(surface.BorderThickness, 2) ||
                !BrushColorMatches(surface.BorderBrush, focusBrush))
            {
                failures.Add(
                    $"{site.Name} 有效焦点外框应为 2 DIP MainActionFocusBrush：actual={surface.BorderThickness} / {surface.BorderBrush}。");
            }

            if (FocusVisualState.GetIsKeyboardFocusVisible(button) ||
                !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(button))
            {
                failures.Add($"{site.Name} 键盘聚焦日期框后未满足单层焦点合同（KeyboardFocusDrawnOnce）。");
            }

            if (!ThicknessEquals(textBox.BorderThickness, 0) || !ThicknessEquals(button.BorderThickness, 0))
            {
                failures.Add($"{site.Name} 聚焦时内部 PART_TextBox 或 PART_Button 出现额外边厚。");
            }

            CheckNoLayoutShift(site, root, restingPickerBounds, restingSiblingBounds, "有效聚焦", failures);

            // 错误有焦点：2 DIP DangerBrush，无布局抖动。
            var dangerBrush = RequiredBrush(picker, "DangerBrush");
            InputErrorState.SetHasError(picker, true);
            SettleWindow(host);
            if (!ThicknessEquals(surface.BorderThickness, 2) ||
                !BrushColorMatches(surface.BorderBrush, dangerBrush))
            {
                failures.Add(
                    $"{site.Name} 出错有焦点外框应为 2 DIP DangerBrush：actual={surface.BorderThickness} / {surface.BorderBrush}。");
            }

            CheckNoLayoutShift(site, root, restingPickerBounds, restingSiblingBounds, "出错聚焦", failures);

            // 错误无焦点：1 DIP DangerBrush，无布局抖动（将焦点移到同视觉根相邻控件，避免关闭 StaysOpen=False 弹层）。
            if (site.LayoutShiftSibling is IInputElement siblingFocus)
            {
                Keyboard.Focus(siblingFocus);
            }
            else
            {
                Keyboard.ClearFocus();
            }
            SettleWindow(host);
            if (!ThicknessEquals(surface.BorderThickness, 1) ||
                !BrushColorMatches(surface.BorderBrush, dangerBrush))
            {
                failures.Add(
                    $"{site.Name} 出错无焦点外框应为 1 DIP DangerBrush：actual={surface.BorderThickness} / {surface.BorderBrush}。");
            }

            CheckNoLayoutShift(site, root, restingPickerBounds, restingSiblingBounds, "出错无焦点", failures);
        }
        finally
        {
            InputErrorState.SetHasError(picker, originalError);
            Keyboard.ClearFocus();
            SettleWindow(host);
        }
    }

    private static void VerifyHighContrastAndDisabled(
        DatePickerSite site,
        ApplicationTheme theme,
        ICollection<string> failures)
    {
        var picker = site.Picker;
        var host = site.HostWindow;
        picker.ApplyTemplate();
        var surface = picker.Template?.FindName("Surface", picker) as Border;
        var contentSlot = picker.Template?.FindName("ContentSlot", picker) as Border;
        var button = picker.Template?.FindName("PART_Button", picker) as Wpf.Ui.Controls.Button;
        if (surface is null || contentSlot is null || button is null)
        {
            return;
        }

        var originalEnabled = picker.IsEnabled;
        var originalError = InputErrorState.GetHasError(picker);
        try
        {
            // 1) 日间禁用态：Surface 与 ContentSlot 均为 0.45 不透明度
            picker.IsEnabled = false;
            SettleWindow(host);
            if (Math.Abs(surface.Opacity - DisabledStateBehavior.DisabledOpacity) > 0.01 ||
                Math.Abs(contentSlot.Opacity - DisabledStateBehavior.DisabledOpacity) > 0.01)
            {
                failures.Add(
                    $"{site.Name} 日间禁用透明度异常：surface={surface.Opacity:0.##}，contentSlot={contentSlot.Opacity:0.##}。");
            }

            picker.IsEnabled = true;
            SettleWindow(host);

            // 2) 系统对比度：有效焦点 Highlight (2 DIP)、错误 WindowText (1/2 DIP)、禁用 GrayText/Control (1.0 不透明)
            var palette = new ApplicationTheme.SystemContrastPalette(
                Window: Colors.Black,
                WindowText: Colors.White,
                Control: Color.FromRgb(18, 22, 30),
                ControlText: Colors.White,
                Highlight: Colors.Yellow,
                HighlightText: Colors.Black,
                GrayText: Colors.Silver);
            using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrast.ChangeColors(palette);
                host.Activate();
                InputErrorState.SetHasError(picker, false);
                Keyboard.Focus(button);
                SettleWindow(host);
                if (!ThicknessEquals(surface.BorderThickness, 2) ||
                    !BrushIsColor(surface.BorderBrush, palette.Highlight))
                {
                    failures.Add($"{site.Name} 系统对比度有效焦点未映射到 2 DIP Highlight。");
                }

                InputErrorState.SetHasError(picker, true);
                SettleWindow(host);
                if (!ThicknessEquals(surface.BorderThickness, 2) ||
                    !BrushIsColor(surface.BorderBrush, palette.WindowText))
                {
                    failures.Add($"{site.Name} 系统对比度出错有焦点未映射到 2 DIP WindowText。");
                }

                Keyboard.ClearFocus();
                SettleWindow(host);
                if (!ThicknessEquals(surface.BorderThickness, 1) ||
                    !BrushIsColor(surface.BorderBrush, palette.WindowText))
                {
                    failures.Add($"{site.Name} 系统对比度出错无焦点未映射到 1 DIP WindowText。");
                }

                picker.IsEnabled = false;
                SettleWindow(host);
                if (Math.Abs(surface.Opacity - 1.0) > 0.01 ||
                    Math.Abs(contentSlot.Opacity - 1.0) > 0.01 ||
                    !BrushIsColor(surface.BorderBrush, palette.GrayText) ||
                    !BrushIsColor(picker.Foreground, palette.GrayText) ||
                    !BrushIsColor(picker.Background, palette.Control))
                {
                    failures.Add($"{site.Name} 系统对比度禁用态外观异常。");
                }
            }

            theme.Apply();
            picker.IsEnabled = true;
            InputErrorState.SetHasError(picker, false);
            SettleWindow(host);
            if (Math.Abs(surface.Opacity - 1.0) > 0.01 ||
                Math.Abs(contentSlot.Opacity - 1.0) > 0.01)
            {
                failures.Add($"{site.Name} 退出系统对比度并重新启用后未恢复常态不透明度。");
            }
        }
        finally
        {
            picker.IsEnabled = originalEnabled;
            InputErrorState.SetHasError(picker, originalError);
            Keyboard.ClearFocus();
            theme.Apply();
            SettleWindow(host);
        }
    }

    private static void VerifyInteractionsAndCalendar(
        DatePickerSite site,
        ICollection<string> failures)
    {
        var picker = site.Picker;
        var host = site.HostWindow;
        var originalDate = picker.SelectedDate;
        try
        {
            // 1) 只读中文日期格式（含最宽日期、闰日与空值）
            if (!WindowValidationInput.ExerciseDatePickerSelectedDateText(picker))
            {
                failures.Add($"{site.Name} 只读中文日期格式验证失败。");
            }

            // 2) 点击文字区释放打开主题化日历
            var pointer = WindowValidationInput.ExerciseDatePickerTextPointer(picker);
            if (!pointer.PressDidNotOpen || !pointer.ReleaseOpened || !pointer.ThemedPopupOpened)
            {
                failures.Add(
                    $"{site.Name} 点击文字区打开日历异常：pressDidNotOpen={pointer.PressDidNotOpen}，releaseOpened={pointer.ReleaseOpened}，themed={pointer.ThemedPopupOpened}。");
            }

            // 3) 点击箭头按钮打开日历
            if (!WindowValidationInput.ExerciseDatePickerButton(picker))
            {
                failures.Add($"{site.Name} 点击箭头按钮未能打开日历。");
            }

            // 4) 月视图网格（7 个星期标题 + 6×7 互不重叠、单格 ≥ 32×30 DIP）+ 日历键盘方向键 + Esc 取消并还原旧日期与焦点归还
            var initialDate = new DateTime(2031, 12, 15);
            picker.SelectedDate = initialDate;
            picker.DisplayDate = initialDate;
            SettleWindow(host);

            picker.ApplyTemplate();
            var trigger = picker.Template?.FindName("PART_Button", picker) as Button;
            var textBox = picker.Template?.FindName("PART_TextBox", picker) as DatePickerTextBox;
            var popup = picker.Template?.FindName("PART_Popup", picker) as Popup;
            if (trigger is null || textBox is null || popup is null)
            {
                failures.Add($"{site.Name} 缺少日历交互必需模板部件。");
                return;
            }

            host.Activate();
            Keyboard.Focus(trigger);
            SettleWindow(host);
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleWindow(host);

            var calendar = PickerTemplateParts.GetCalendar(picker);
            if (!picker.IsDropDownOpen || calendar is null)
            {
                failures.Add($"{site.Name} 点击箭头后日历未保持打开。");
                return;
            }

            calendar.ApplyTemplate();
            calendar.UpdateLayout();
            VerifyCalendarMonthGrid(site.Name, calendar, failures);

            // 键盘导航：在日历中把焦点移到选中日并按右方向键移动到次日
            var selectedDay = VisualDescendants<CalendarDayButton>(calendar)
                .FirstOrDefault(day => day.DataContext is DateTime d && d.Date == initialDate.Date);
            if (selectedDay is null)
            {
                failures.Add($"{site.Name} 打开的日历中找不到当前选中日期格（2031-12-15）。");
            }
            else
            {
                Keyboard.Focus(selectedDay);
                SettleWindow(host);
                WindowValidationInput.RaiseKey(selectedDay, Key.Right);
                SettleWindow(host);
                var movedDay = Keyboard.FocusedElement as CalendarDayButton;
                var movedDate = movedDay?.DataContext as DateTime?;
                if (movedDate?.Date != initialDate.AddDays(1).Date)
                {
                    failures.Add(
                        $"{site.Name} 日历方向键未移动到次日：focused={DescribeElement(Keyboard.FocusedElement)}，date={movedDate:yyyy-MM-dd}。");
                }
            }

            // 模拟日历选中变化后按 Esc：必须取消并还原为打开前的 initialDate，关闭弹层且把焦点归还 PART_Button。
            calendar.SelectedDate = initialDate.AddDays(1);
            SettleWindow(host);
            var escapeHandled = WindowValidationInput.RaisePreviewKey(calendar, Key.Escape);
            var closed = WindowValidationInput.WaitForPopupClosed(popup);
            SettleWindow(host);

            var expectedRestoredText = DateDisplay.FullDate(DateOnly.FromDateTime(initialDate));
            var focusReturned = trigger.IsKeyboardFocusWithin ||
                ReferenceEquals(Keyboard.FocusedElement, trigger) ||
                ReferenceEquals(FocusManager.GetFocusedElement(host), trigger);
            if (!escapeHandled || !closed || picker.IsDropDownOpen ||
                picker.SelectedDate?.Date != initialDate.Date ||
                !string.Equals(textBox.Text, expectedRestoredText, StringComparison.Ordinal) ||
                !focusReturned)
            {
                failures.Add(
                    $"{site.Name} Esc 取消还原或焦点归还失败：handled={escapeHandled}，closed={closed}，open={picker.IsDropDownOpen}，date={picker.SelectedDate:yyyy-MM-dd}，text=「{textBox.Text}」，focusReturned={focusReturned}，focused={DescribeElement(Keyboard.FocusedElement)}。");
            }
        }
        finally
        {
            picker.IsDropDownOpen = false;
            picker.SelectedDate = originalDate;
            SettleWindow(host);
        }
    }

    private static void VerifyCalendarMonthGrid(
        string siteName,
        WpfCalendar calendar,
        ICollection<string> failures)
    {
        if (calendar.Template?.FindName("PART_CalendarItem", calendar) is not CalendarItem item)
        {
            failures.Add($"{siteName} 日历缺少 PART_CalendarItem。");
            return;
        }

        item.ApplyTemplate();
        item.UpdateLayout();
        if (item.Template?.FindName("PART_MonthView", item) is not Grid monthView)
        {
            failures.Add($"{siteName} 日历缺少 PART_MonthView。");
            return;
        }

        var days = monthView.Children.OfType<CalendarDayButton>().ToArray();
        var bounds = days.Select(day => Bounds(day, monthView)).ToArray();
        var titles = monthView.Children.OfType<TextBlock>()
            .Where(title => Grid.GetRow(title) == 0 && !string.IsNullOrWhiteSpace(title.Text))
            .ToArray();
        var overlapping = bounds.Where((rect, index) => bounds.Skip(index + 1).Any(other =>
        {
            var shared = Rect.Intersect(rect, other);
            return !shared.IsEmpty && shared.Width > 0.5 && shared.Height > 0.5;
        })).Any();
        var cols = bounds.Select(rect => Math.Round(rect.Left)).Distinct().Count();
        var rows = bounds.Select(rect => Math.Round(rect.Top)).Distinct().Count();
        if (days.Length != 42 ||
            titles.Length != 7 ||
            cols != 7 ||
            rows != 6 ||
            overlapping ||
            bounds.Any(rect => rect.Width < 31.5 || rect.Height < 29.5))
        {
            failures.Add(
                $"{siteName} 日历月视图网格异常：days={days.Length}，titles={titles.Length}，grid={cols}x{rows}，overlap={overlapping}。");
        }
    }

    private static void VerifyLongestDateFits(
        DatePickerSite site,
        string widthLabel,
        ICollection<string> failures,
        ICollection<string> fitMetrics)
    {
        var picker = site.Picker;
        var host = site.HostWindow;
        var originalDate = picker.SelectedDate;
        try
        {
            picker.SelectedDate = LongestDate;
            SettleWindow(host);
            picker.ApplyTemplate();

            if (picker.Template?.FindName("PART_TextBox", picker) is not DatePickerTextBox textBox)
            {
                failures.Add($"{site.Name}({widthLabel}) 缺少 PART_TextBox。");
                return;
            }

            textBox.ApplyTemplate();
            SettleWindow(host);
            if (!string.Equals(textBox.Text, LongestDateText, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{site.Name}({widthLabel}) 最长日期显示文本不符：actual=「{textBox.Text}」，expected=「{LongestDateText}」。");
            }

            var dpi = VisualTreeHelper.GetDpi(textBox);
            var formatted = new FormattedText(
                LongestDateText,
                CultureInfo.CurrentCulture,
                textBox.FlowDirection,
                new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch),
                textBox.FontSize,
                Brushes.Black,
                dpi.PixelsPerDip);
            var required =
                formatted.WidthIncludingTrailingWhitespace +
                Math.Max(0, formatted.OverhangTrailing) +
                (2.0 / dpi.DpiScaleX);
            var textBoxAvailable = Math.Max(
                0,
                textBox.ActualWidth -
                textBox.Padding.Left -
                textBox.Padding.Right -
                textBox.BorderThickness.Left -
                textBox.BorderThickness.Right);
            var contentHost = textBox.Template?.FindName("PART_ContentHost", textBox) as ScrollViewer;
            var hostAvailable = contentHost?.ActualWidth ?? textBoxAvailable;
            var available = Math.Min(textBoxAvailable, hostAvailable);

            if (available < required)
            {
                failures.Add(
                    $"{site.Name}({widthLabel}) 最长日期「{LongestDateText}」被裁切：available={available:0.##}（textBoxAvail={textBoxAvailable:0.##}，hostAvail={hostAvailable:0.##}）< required={required:0.##}（pickerW={picker.ActualWidth:0.##}，fontSize={textBox.FontSize:0.##}）。");
            }

            if (contentHost is not null && contentHost.ScrollableWidth > 0.5)
            {
                failures.Add(
                    $"{site.Name}({widthLabel}) 最长日期出现横向溢出滚动量：scrollableWidth={contentHost.ScrollableWidth:0.##}。");
            }

            if (site.HasLeadingIcon && picker.Parent is Grid iconHost)
            {
                var leadingIcon = iconHost.Children.OfType<WpfPath>().FirstOrDefault();
                if (leadingIcon is not null && contentHost is not null)
                {
                    var iconRect = Bounds(leadingIcon, iconHost);
                    var hostRect = Bounds(contentHost, iconHost);
                    if (iconRect.Right > hostRect.Left + 0.5)
                    {
                        failures.Add(
                            $"{site.Name}({widthLabel}) 前导日历图标压到日期文字区：iconRight={iconRect.Right:0.##}，textLeft={hostRect.Left:0.##}。");
                    }
                }
            }

            fitMetrics.Add(
                $"{site.Name}[{widthLabel}]:w={picker.ActualWidth:0.##},font={textBox.FontSize:0.##},avail={available:0.##},req={required:0.##},margin={available - required:0.##}");
        }
        finally
        {
            picker.SelectedDate = originalDate;
            SettleWindow(host);
        }
    }

    private static void WithAllSixSites(Action<IReadOnlyList<DatePickerSite>, ComboBox> verify)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-datepicker-312-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        FloatingWindow? floating = null;
        ReminderRulesEditorWindow? rulesEditor = null;
        ReminderCenterWindow? reminderCenter = null;
        Window? comboRefHost = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);

            var defaultCombo = new ComboBox
            {
                Width = 140,
                ItemsSource = new[] { "00", "01" },
                SelectedIndex = 0
            };
            comboRefHost = new Window
            {
                Content = new StackPanel { Children = { defaultCombo } },
                Width = 220,
                Height = 100,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            ShowAndWaitFirstFrame(comboRefHost);

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
            ShowAndWaitFirstFrame(main);

            floating = new FloatingWindow(
                workspace,
                calendar,
                main,
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            ShowAndWaitFirstFrame(floating);

            rulesEditor = new ReminderRulesEditorWindow(
                Array.Empty<ReminderRule>(),
                new FixedClock(FixtureUtc))
            {
                Owner = main,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            ShowAndWaitFirstFrame(rulesEditor);

            var reminderKey = new ActiveReminderOccurrenceKey(
                "datepicker-312-rule",
                FixtureUtc.AddMinutes(-5));
            reminderCenter = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            reminderCenter.AddEntry(new ReminderCenterItem(
                reminderKey,
                new NoteRecord(
                    "datepicker-312-note",
                    "单框日期控件验收提醒",
                    "2038-07-08",
                    false,
                    Array.Empty<string>(),
                    NoReminder.Instance),
                0,
                0));
            ShowAndWaitFirstFrame(reminderCenter);
            reminderCenter.SetDeliveryReady(reminderKey);
            SettleWindow(reminderCenter);

            var snoozeButton = VisualDescendants<Button>(reminderCenter).Single(button =>
                (AutomationProperties.GetName(button) ?? string.Empty)
                    .StartsWith("稍后提醒：", StringComparison.Ordinal));
            var snoozeMenu = snoozeButton.ContextMenu
                ?? throw new InvalidOperationException("提醒中心缺少稍后提醒菜单。");
            ValidationExercises.SelectReminderSnoozeMenuItem(
                reminderCenter,
                snoozeButton,
                snoozeMenu,
                ReminderSnoozeOption.Custom);
            SettleWindow(reminderCenter);

            var probe = new MainWindowEmptyStateProbe(main);
            var countdownName = Required<TextBox>(main, "CountdownName");
            var countdownDate = Required<DatePicker>(main, "CountdownDate");
            var saveCountdown = Required<Button>(main, "SaveCountdownButton");

            var noteText = Required<TextBox>(main, "NoteText");
            var noteDate = Required<DatePicker>(main, "NoteDate");
            var saveNote = Required<Button>(main, "SaveNoteButton");

            var filterPopup = Required<Popup>(main, "FilterPopup");
            var noteDateFilter = Required<DatePicker>(main, "NoteDateFilter");
            var noteUndatedFilter = Required<ToggleButton>(main, "NoteUndatedFilter");
            var clearNoteDateFilter = Required<Button>(main, "ClearNoteDateFilter");

            var floatingNoteEntry = Required<Border>(floating, "FloatingNoteEntry");
            var floatingNoteText = Required<TextBox>(floating, "FloatingNoteText");
            var floatingNoteDate = Required<DatePicker>(floating, "FloatingNoteDate");
            var floatingReminderRulesButton = Required<Button>(floating, "FloatingManageReminderRules");

            var ruleMode = FindByAutomationId<ComboBox>(rulesEditor, "ReminderRuleMode");
            var ruleDate = FindByAutomationId<DatePicker>(rulesEditor, "ReminderRuleFirstDueDate");
            var ruleTime = FindByAutomationId<TextBox>(rulesEditor, "ReminderRuleFirstDueTime");

            var centerDate = VisualDescendants<DatePicker>(reminderCenter).Single();
            var centerConfirm = FindByAutomationId<Button>(reminderCenter, "ReminderEntryCustomSnoozeConfirm");

            var sites = new DatePickerSite[]
            {
                new(
                    "Main.CountdownDate",
                    main,
                    countdownDate,
                    ReferenceCombo: null,
                    ExpectedHeight: 41.0,
                    LayoutShiftSibling: saveCountdown,
                    PreviousTabElement: countdownName,
                    PrepareVisible: () =>
                    {
                        filterPopup.IsOpen = false;
                        probe.ShowWorkspace(WorkspaceKind.Countdowns);
                        SettleWindow(main);
                    },
                    CleanupVisible: () =>
                    {
                        countdownDate.IsDropDownOpen = false;
                        SettleWindow(main);
                    },
                    HasLeadingIcon: true),
                new(
                    "Main.NoteDate",
                    main,
                    noteDate,
                    ReferenceCombo: null,
                    ExpectedHeight: 50.0,
                    LayoutShiftSibling: saveNote,
                    PreviousTabElement: noteText,
                    PrepareVisible: () =>
                    {
                        filterPopup.IsOpen = false;
                        probe.ShowWorkspace(WorkspaceKind.Notes);
                        SettleWindow(main);
                    },
                    CleanupVisible: () =>
                    {
                        noteDate.IsDropDownOpen = false;
                        SettleWindow(main);
                    },
                    HasLeadingIcon: true),
                new(
                    "Main.NoteDateFilter",
                    main,
                    noteDateFilter,
                    ReferenceCombo: null,
                    ExpectedHeight: defaultCombo.ActualHeight,
                    LayoutShiftSibling: clearNoteDateFilter,
                    PreviousTabElement: noteUndatedFilter,
                    PrepareVisible: () =>
                    {
                        probe.ShowWorkspace(WorkspaceKind.Notes);
                        main.Activate();
                        SettleWindow(main);
                        filterPopup.IsOpen = true;
                        SettleWindow(main);
                    },
                    CleanupVisible: () =>
                    {
                        noteDateFilter.IsDropDownOpen = false;
                        noteDateFilter.SelectedDate = null;
                        filterPopup.IsOpen = false;
                        SettleWindow(main);
                    }),
                new(
                    "Floating.NoteDate",
                    floating,
                    floatingNoteDate,
                    ReferenceCombo: null,
                    ExpectedHeight: defaultCombo.ActualHeight,
                    LayoutShiftSibling: floatingReminderRulesButton,
                    PreviousTabElement: floatingNoteText,
                    PrepareVisible: () =>
                    {
                        floating.ShowWorkspace(WorkspaceKind.Notes);
                        floating.SetQuickAddExpanded(true);
                        SettleWindow(floating);
                    },
                    CleanupVisible: () =>
                    {
                        floatingNoteDate.IsDropDownOpen = false;
                        floating.SetQuickAddExpanded(false);
                        SettleWindow(floating);
                    },
                    LayoutRoot: floatingNoteEntry),
                new(
                    "ReminderRules.FirstDueDate",
                    rulesEditor,
                    ruleDate,
                    ruleMode,
                    ExpectedHeight: ruleMode.ActualHeight,
                    LayoutShiftSibling: ruleTime,
                    PreviousTabElement: ruleMode,
                    PrepareVisible: () => SettleWindow(rulesEditor),
                    CleanupVisible: () =>
                    {
                        ruleDate.IsDropDownOpen = false;
                        SettleWindow(rulesEditor);
                    },
                    ResizeHost: width =>
                    {
                        rulesEditor.SizeToContent = SizeToContent.Manual;
                        rulesEditor.Width = width;
                    },
                    NarrowHostWidth: 560),
                new(
                    "ReminderCenter.CustomSnoozeDate",
                    reminderCenter,
                    centerDate,
                    ReferenceCombo: null,
                    ExpectedHeight: defaultCombo.ActualHeight,
                    LayoutShiftSibling: centerConfirm,
                    PreviousTabElement: null,
                    PrepareVisible: () => SettleWindow(reminderCenter),
                    CleanupVisible: () =>
                    {
                        centerDate.IsDropDownOpen = false;
                        SettleWindow(reminderCenter);
                    },
                    ResizeHost: width => reminderCenter.Width = width,
                    NarrowHostWidth: 400)
            };

            verify(sites, defaultCombo);
        }
        finally
        {
            comboRefHost?.Close();
            rulesEditor?.Close();
            reminderCenter?.ForceClose();
            floating?.Close();
            main?.Close();
            if (main is not null)
            {
                WindowValidationInput.Pump(main.Dispatcher);
            }
            DeleteSettingsFiles(settingsPath);
            if (File.Exists(diagnosticPath))
            {
                File.Delete(diagnosticPath);
            }
        }
    }

    private static Visual ResolveCoordinateRoot(FrameworkElement element, Window fallbackHost) =>
        PresentationSource.FromVisual(element)?.RootVisual ?? (Visual)fallbackHost.Content;

    private static void CheckNoLayoutShift(
        DatePickerSite site,
        Visual root,
        Rect restingPickerBounds,
        Rect? restingSiblingBounds,
        string phase,
        ICollection<string> failures)
    {
        var currentPickerBounds = Bounds(site.Picker, root);
        if (!RectEquals(currentPickerBounds, restingPickerBounds))
        {
            failures.Add(
                $"{site.Name} {phase}时日期框几何发生位移：before={restingPickerBounds}，after={currentPickerBounds}。");
        }

        if (site.LayoutShiftSibling is not null && restingSiblingBounds is { } beforeSibling)
        {
            var afterSibling = Bounds(site.LayoutShiftSibling, root);
            if (!RectEquals(afterSibling, beforeSibling))
            {
                failures.Add(
                    $"{site.Name} {phase}时相邻控件几何发生位移：before={beforeSibling}，after={afterSibling}。");
            }
        }
    }

    private static void ShowAndWaitFirstFrame(Window window)
    {
        var rendered = false;
        window.ContentRendered += (_, _) => rendered = true;
        window.Show();
        var frame = WindowValidationInput.PumpRenderedFrames(window.Dispatcher, 2000, () => rendered);
        SettleWindow(window);
        if (!rendered || !frame.Completed)
        {
            throw new InvalidOperationException($"窗口 {window.GetType().Name} 未完成首帧渲染。");
        }
    }

    private static void SettleWindow(Window window)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static T Required<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T
        ?? throw new InvalidOperationException($"窗口缺少命名元素 {name}。");

    private static T FindByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject =>
        VisualDescendants<T>(root).Single(element =>
            string.Equals(AutomationProperties.GetAutomationId(element), automationId, StringComparison.Ordinal));

    private static CornerRadius RequiredCornerRadius(FrameworkElement scope, string key) =>
        scope.TryFindResource(key) is CornerRadius radius
            ? radius
            : throw new InvalidOperationException($"缺少圆角资源 {key}。");

    private static Brush RequiredBrush(FrameworkElement scope, string key) =>
        scope.TryFindResource(key) as Brush
        ?? Application.Current.TryFindResource(key) as Brush
        ?? throw new InvalidOperationException($"缺少画刷资源 {key}。");

    private static bool ThicknessEquals(Thickness value, double expected) =>
        Math.Abs(value.Left - expected) < 0.01 &&
        Math.Abs(value.Top - expected) < 0.01 &&
        Math.Abs(value.Right - expected) < 0.01 &&
        Math.Abs(value.Bottom - expected) < 0.01;

    private static bool RectEquals(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) <= GeometryTolerance &&
        Math.Abs(a.Y - b.Y) <= GeometryTolerance &&
        Math.Abs(a.Width - b.Width) <= GeometryTolerance &&
        Math.Abs(a.Height - b.Height) <= GeometryTolerance;

    private static bool IsTransparent(Brush? brush) =>
        brush is null || (brush is SolidColorBrush solid && solid.Color.A == 0);

    private static bool BrushColorMatches(Brush? actual, Brush expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush a && expected is SolidColorBrush b && a.Color == b.Color);

    private static bool BrushIsColor(Brush? actual, Color expected) =>
        actual is SolidColorBrush solid && solid.Color == expected;

    private static string FormatRadius(CornerRadius radius) =>
        $"{radius.TopLeft:0.##},{radius.TopRight:0.##},{radius.BottomRight:0.##},{radius.BottomLeft:0.##}";

    private static string DescribeElement(IInputElement? element) => element switch
    {
        FrameworkElement fe => $"{fe.GetType().Name}({fe.Name})",
        null => "null",
        _ => element.GetType().Name
    };
}
