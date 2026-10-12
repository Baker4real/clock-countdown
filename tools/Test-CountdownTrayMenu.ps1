#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $AssemblyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app/MinminCountdown.dll'),
    [string] $EvidenceDirectory,
    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
$desktopName = Get-SuiteThreadDesktopName
# This probe never accepts the foreground override used by other suite callers.
if (-not (Test-SuiteIsolatedDesktopName -DesktopName $desktopName)) {
    throw "托盘菜单验证只允许官方 ValidationHarness 隔离桌面；当前桌面='$desktopName'。"
}
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw '托盘菜单验证必须由官方 ValidationHarness 阶段在 STA 隔离桌面中运行。'
}
if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
    throw "未找到被测应用程序集：$AssemblyPath"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $captureName = 'tray-menu-{0}-{1}' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), [Guid]::NewGuid().ToString('N')
    $EvidenceDirectory = Join-Path $repoRoot "logs/ui-captures/$captureName"
}
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
if (-not [string]::Equals([IO.Path]::GetPathRoot($EvidenceDirectory), 'D:\', [StringComparison]::OrdinalIgnoreCase)) {
    throw '托盘菜单证据必须写入真实 D 盘路径。'
}
if (Test-Path -LiteralPath $EvidenceDirectory) {
    throw "证据目录已存在，拒绝覆盖：$EvidenceDirectory"
}
[void] [IO.Directory]::CreateDirectory($EvidenceDirectory)

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$references = @(
    Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' |
        Where-Object Name -ne 'WindowsBase.dll' |
        Select-Object -ExpandProperty FullName
    [System.Windows.Window].Assembly.Location
    [System.Windows.UIElement].Assembly.Location
    [System.Windows.Threading.Dispatcher].Assembly.Location
    [System.Windows.Markup.IQueryAmbient].Assembly.Location
)

# Only the production WPF menu factory is invoked. No notification host,
# application workspace, settings store, or Shell icon is constructed.
if (-not ('CountdownTrayMenuHarness' -as [type])) {
    Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public sealed class TrayMenuRowEvidence
{
    public string Header { get; set; }
    public bool Highlighted { get; set; }
    public double[] Bounds { get; set; }
    public string Foreground { get; set; }
    public string Background { get; set; }
    public double FontSize { get; set; }
    public double ContrastRatio { get; set; }
    public bool TextFits { get; set; }
    public bool TextUsesInk { get; set; }
    public int TextPixels { get; set; }
    public int WarmBackgroundPixels { get; set; }
    public bool WarmBackgroundRendered { get; set; }
    public bool Readable { get; set; }
}

public sealed class TrayMenuCaptureEvidence
{
    public string State { get; set; }
    public string Path { get; set; }
    public string SHA256 { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int NonTransparentPixels { get; set; }
    public int WarmSurfacePixels { get; set; }
    public string SurfaceForeground { get; set; }
    public string SurfaceBackground { get; set; }
    public List<TrayMenuRowEvidence> Rows { get; } = new List<TrayMenuRowEvidence>();
}

public sealed class TrayMenuActionEvidence
{
    public string Header { get; set; }
    public bool PointerPressHandled { get; set; }
    public bool PointerReleaseHandled { get; set; }
    public bool FocusedOnPress { get; set; }
    public bool PointerInputHandled { get; set; }
    public int[] CallbackDeltas { get; set; }
    public bool RoutedOnlyToExpectedCallback { get; set; }
    public bool MenuClosedByAction { get; set; }
    public bool Passed { get; set; }
}

public sealed class TrayMenuFocusEvidence
{
    public string Stage { get; set; }
    public string KeyboardFocusRequestReturned { get; set; }
    public bool? MoveFocusReturned { get; set; }
    public string FocusedElement { get; set; }
    public string FocusedElementHwnd { get; set; }
    public string MenuHwnd { get; set; }
    public string MouseOverElement { get; set; }
    public string MouseOverHwnd { get; set; }
    public string NativeCaptureHwnd { get; set; }
    public bool OwnerIsActive { get; set; }
    public bool MenuIsOpen { get; set; }
    public bool MenuIsKeyboardFocusWithin { get; set; }
    public bool FirstItemIsKeyboardFocused { get; set; }
    public bool FirstItemIsKeyboardFocusWithin { get; set; }
    public bool FirstItemIsHighlighted { get; set; }
}

public sealed class TrayMenuSourceActivationEvidence
{
    public string MenuHwnd { get; set; }
    public double[] MenuBlankPoint { get; set; }
    public int[] HwndPoint { get; set; }
    public bool BlankPointOutsideItems { get; set; }
    public string CaptureBefore { get; set; }
    public string CaptureAfterActivation { get; set; }
    public bool NativeCaptureAcquired { get; set; }
    public bool MouseMoveSent { get; set; }
    public int[] CallbackDeltas { get; set; }
    public string CaptureAfterCleanup { get; set; }
    public bool CaptureRestored { get; set; }
}

public sealed class TrayMenuVerificationEvidence
{
    public string Factory { get; } = "MinminCountdown.TrayMenuFactory.Create";
    public string InputPath { get; } = "WindowValidationInput.RaisePickerPointer: down/up, callback and close observed before input cleanup";
    public string HighlightInputPath { get; } = "Own menu HWND: SetCapture + WM_MOUSEMOVE(blank); DispatcherPriority.Input: Keyboard.Focus(menu), then menu.MoveFocus(First)";
    public bool ShellRegistrationPerformed { get; } = false;
    public bool ShellDoubleClickValidated { get; } = false;
    public bool ControllerBehaviorValidated { get; } = false;
    public bool SettingsAccessPerformed { get; } = false;
    public string CaptureScope { get; } = "WPF ContextMenu client visual; native Shell opening and system shadow are not covered";
    public bool KeyboardHighlightReached { get; set; }
    public TrayMenuSourceActivationEvidence SourceActivation { get; set; }
    public List<TrayMenuFocusEvidence> KeyboardFocusSteps { get; } = new List<TrayMenuFocusEvidence>();
    public List<TrayMenuCaptureEvidence> Captures { get; } = new List<TrayMenuCaptureEvidence>();
    public List<TrayMenuActionEvidence> Actions { get; } = new List<TrayMenuActionEvidence>();
    public List<string> Failures { get; } = new List<string>();
    public bool Passed => Failures.Count == 0 && Captures.Count == 2 && Actions.Count == 4;
}

public static class CountdownTrayMenuHarness
{
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Color Ink = Color.FromRgb(0x3B, 0x19, 0x0D);
    private static readonly Color Surface = Color.FromRgb(0xFF, 0xFD, 0xFC);
    private static readonly Color Highlight = Color.FromRgb(0xF6, 0xDF, 0xD3);

    public static TrayMenuVerificationEvidence Run(string assemblyPath, string evidenceDirectory)
    {
        var result = new TrayMenuVerificationEvidence();
        Window owner = null;
        ContextMenu menu = null;
        MethodInfo pump = null;
        try
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            var application = Assembly.LoadFrom(assemblyPath);
            var factory = application.GetType("MinminCountdown.TrayMenuFactory", true)
                .GetMethod("Create", StaticPrivate);
            var input = application.GetType("MinminCountdown.WindowValidationInput", true);
            pump = input.GetMethod("Pump", BindingFlags.Public | BindingFlags.Static);
            var pointer = input.GetMethod("RaisePickerPointer", StaticPrivate);
            var pointerState = input.GetNestedType("PickerPointerState", BindingFlags.NonPublic);
            var capture = application.GetType("MinminCountdown.ValidationReport", true)
                .GetMethod("CaptureVisual", StaticPrivate);
            var writePng = application.GetType("MinminCountdown.ValidationReport", true)
                .GetMethod("WriteNewPng", StaticPrivate);
            if (factory == null || pump == null || pointer == null || pointerState == null || capture == null || writePng == null)
                throw new InvalidOperationException("当前构建缺少生产菜单工厂或既有输入/截图接口。");

            var counts = new int[4];
            var built = (ValueTuple<ContextMenu, MenuItem, MenuItem, MenuItem, MenuItem>)factory.Invoke(null, new object[]
            {
                new Action(() => counts[0]++),
                new Action(() => counts[1]++),
                new Action(() => counts[2]++),
                new Action(() => counts[3]++)
            });
            menu = built.Item1;
            var items = new[] { built.Item2, built.Item3, built.Item4, built.Item5 };
            var expectedHeaders = new[] { "打开管理窗口", "打开悬浮窗", "复位窗口位置", "退出" };
            if (menu.Items.Count != 4 || !items.Select(item => item.Header as string).SequenceEqual(expectedHeaders) ||
                !menu.Items.Cast<object>().SequenceEqual(items.Cast<object>()))
                throw new InvalidOperationException("生产菜单必须按顺序包含打开管理窗口、打开悬浮窗、复位窗口位置、退出四个真实菜单项。");

            owner = new Window
            {
                Title = "MinminCountdown Tray Menu Validation",
                Width = 160,
                Height = 80,
                Left = 20,
                Top = 20,
                ShowInTaskbar = false,
                Content = new Border()
            };
            owner.Show();
            owner.Activate();
            Pump(pump, owner.Dispatcher);
            menu.PlacementTarget = owner;
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = 40;
            menu.VerticalOffset = 110;

            Open(menu, pump);
            TryCapture("tray-menu-open", menu, items, capture, writePng, evidenceDirectory, result);
            result.KeyboardFocusSteps.Add(ReadFocusState("after-open", owner, menu, items[0]));
            var captureBefore = MenuNativeInput.GetCapture();
            var countsBeforeKeyboard = (int[])counts.Clone();
            var activation = new TrayMenuSourceActivationEvidence { CaptureBefore = DescribeHwnd(captureBefore) };
            result.SourceActivation = activation;
            try
            {
                ActivateMenuInputSource(menu, items, activation);
                Pump(pump, menu.Dispatcher);
                result.KeyboardFocusSteps.Add(ReadFocusState("after-source-activation", owner, menu, items[0]));
                // WPF navigation owns selection/highlight; the fixture never assigns either state.
                IInputElement focusReturned = null;
                menu.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                    new Action(() => focusReturned = Keyboard.Focus(menu)));
                Pump(pump, menu.Dispatcher);
                var focusedMenu = ReadFocusState("after-menu-focus", owner, menu, items[0]);
                focusedMenu.KeyboardFocusRequestReturned = DescribeFocus(focusReturned);
                result.KeyboardFocusSteps.Add(focusedMenu);
                bool moveFocusReturned = false;
                menu.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                    new Action(() => moveFocusReturned = menu.MoveFocus(
                        new TraversalRequest(FocusNavigationDirection.First))));
                Pump(pump, menu.Dispatcher);
                var navigated = ReadFocusState("after-first-navigation", owner, menu, items[0]);
                navigated.MoveFocusReturned = moveFocusReturned;
                result.KeyboardFocusSteps.Add(navigated);
            }
            catch (Exception exception)
            {
                result.Failures.Add("菜单 HWND 输入源激活/键盘导航异常：" + Detail(exception));
            }
            finally
            {
                activation.CallbackDeltas = counts.Select((count, i) => count - countsBeforeKeyboard[i]).ToArray();
                if (activation.CallbackDeltas.Any(count => count != 0))
                    result.Failures.Add("菜单空白输入源激活/键盘导航意外触发了业务回调。");
                result.KeyboardHighlightReached = items[0].IsKeyboardFocusWithin && items[0].IsHighlighted;
                if (!result.KeyboardHighlightReached)
                    result.Failures.Add("通过隔离菜单的键盘焦点未进入首项高亮态。");
                // Save the observed frame before restoring capture or closing the fixture.
                TryCapture("tray-menu-keyboard-highlight", menu, items, capture, writePng, evidenceDirectory, result);
                if (MenuNativeInput.GetCapture() != captureBefore)
                {
                    if (captureBefore == IntPtr.Zero) MenuNativeInput.ReleaseCapture();
                    else MenuNativeInput.SetCapture(captureBefore);
                }
                activation.CaptureAfterCleanup = DescribeHwnd(MenuNativeInput.GetCapture());
                activation.CaptureRestored = MenuNativeInput.GetCapture() == captureBefore;
                if (!activation.CaptureRestored)
                    result.Failures.Add("菜单输入源激活后的原生捕获未恢复。");
            }
            menu.IsOpen = false;
            Pump(pump, menu.Dispatcher);

            for (int index = 0; index < items.Length; index++)
            {
                var action = new TrayMenuActionEvidence { Header = expectedHeaders[index] };
                var before = (int[])counts.Clone();
                try
                {
                    Open(menu, pump);
                    items[index].BringIntoView();
                    Pump(pump, menu.Dispatcher);
                    using ((IDisposable)Activator.CreateInstance(pointerState, true))
                    {
                        action.PointerPressHandled = (bool)pointer.Invoke(null,
                            new object[] { items[index], UIElement.PreviewMouseLeftButtonDownEvent });
                        Pump(pump, menu.Dispatcher);
                        action.FocusedOnPress = items[index].IsKeyboardFocusWithin;
                        action.PointerReleaseHandled = (bool)pointer.Invoke(null,
                            new object[] { items[index], UIElement.PreviewMouseLeftButtonUpEvent });
                        Pump(pump, menu.Dispatcher);
                        action.PointerInputHandled = action.PointerPressHandled && action.PointerReleaseHandled;
                        action.CallbackDeltas = counts.Select((count, i) => count - before[i]).ToArray();
                        action.RoutedOnlyToExpectedCallback = action.CallbackDeltas
                            .Select((count, i) => count == (i == index ? 1 : 0)).All(matches => matches);
                        action.MenuClosedByAction = !menu.IsOpen;
                    }
                    // A leaf MenuItem can invoke on release without taking
                    // keyboard focus on press. Keep focus as an observation;
                    // its pointer, exact callback and own close are required.
                    action.Passed = action.PointerInputHandled &&
                        action.RoutedOnlyToExpectedCallback && action.MenuClosedByAction;
                    if (!action.Passed)
                        result.Failures.Add("真实菜单点击路由或关闭失败：" + action.Header);
                }
                catch (Exception exception)
                {
                    result.Failures.Add("菜单点击异常 " + action.Header + "：" + Detail(exception));
                }
                finally
                {
                    result.Actions.Add(action);
                    // Cleanup is recorded after MenuClosedByAction; it cannot make that verdict pass.
                    menu.IsOpen = false;
                    Pump(pump, menu.Dispatcher);
                }
            }
        }
        catch (Exception exception)
        {
            result.Failures.Add("托盘菜单验证异常：" + Detail(exception));
        }
        finally
        {
            if (menu != null) menu.IsOpen = false;
            if (owner != null) owner.Close();
            if (pump != null) Pump(pump, Dispatcher.CurrentDispatcher);
        }
        return result;
    }

    private static void Pump(MethodInfo pump, Dispatcher dispatcher) =>
        pump.Invoke(null, new object[] { dispatcher });

    private static void ActivateMenuInputSource(
        ContextMenu menu, MenuItem[] items, TrayMenuSourceActivationEvidence evidence)
    {
        menu.Dispatcher.VerifyAccess();
        var source = PresentationSource.FromVisual(menu) as System.Windows.Interop.HwndSource;
        if (source == null || !(source.RootVisual is Visual root) || source.CompositionTarget == null)
            throw new InvalidOperationException("菜单输入源不是当前夹具的真实 HWND。");
        evidence.MenuHwnd = DescribeHwnd(source.Handle);
        var itemBounds = items.Select(item => item.TransformToAncestor(menu)
            .TransformBounds(new Rect(item.RenderSize))).ToArray();
        var topGap = itemBounds.Min(bounds => bounds.Top);
        var point = new Point(menu.RenderSize.Width / 2, topGap / 2);
        evidence.MenuBlankPoint = new[] { point.X, point.Y };
        evidence.BlankPointOutsideItems = topGap > 0 && new Rect(menu.RenderSize).Contains(point) &&
            itemBounds.All(bounds => !bounds.Contains(point));
        if (!evidence.BlankPointOutsideItems)
            throw new InvalidOperationException("菜单顶部没有可用于输入源激活的真实空白区。");
        var rootPoint = menu.TransformToAncestor(root).Transform(point);
        var devicePoint = source.CompositionTarget.TransformToDevice.Transform(rootPoint);
        var x = (int)Math.Round(devicePoint.X);
        var y = (int)Math.Round(devicePoint.Y);
        evidence.HwndPoint = new[] { x, y };
        // Match RaisePickerPointer's isolated input-source activation, without
        // a button press, a menu-item target, physical cursor movement, or global input.
        MenuNativeInput.SetCapture(source.Handle);
        evidence.CaptureAfterActivation = DescribeHwnd(MenuNativeInput.GetCapture());
        evidence.NativeCaptureAcquired = MenuNativeInput.GetCapture() == source.Handle;
        if (!evidence.NativeCaptureAcquired)
            throw new InvalidOperationException("隔离菜单 HWND 未能取得自身输入捕获。");
        MenuNativeInput.SendMessage(source.Handle, 0x0200, IntPtr.Zero,
            new IntPtr((y << 16) | (x & 0xffff)));
        evidence.MouseMoveSent = true;
    }

    private static TrayMenuFocusEvidence ReadFocusState(
        string stage, Window owner, ContextMenu menu, MenuItem firstItem)
    {
        var source = PresentationSource.FromVisual(menu) as System.Windows.Interop.HwndSource;
        return new TrayMenuFocusEvidence
        {
            Stage = stage,
            FocusedElement = DescribeFocus(Keyboard.FocusedElement),
            FocusedElementHwnd = InputElementHwnd(Keyboard.FocusedElement),
            MenuHwnd = source == null ? null : DescribeHwnd(source.Handle),
            MouseOverElement = DescribeFocus(Mouse.DirectlyOver),
            MouseOverHwnd = InputElementHwnd(Mouse.DirectlyOver),
            NativeCaptureHwnd = DescribeHwnd(MenuNativeInput.GetCapture()),
            OwnerIsActive = owner.IsActive,
            MenuIsOpen = menu.IsOpen,
            MenuIsKeyboardFocusWithin = menu.IsKeyboardFocusWithin,
            FirstItemIsKeyboardFocused = firstItem.IsKeyboardFocused,
            FirstItemIsKeyboardFocusWithin = firstItem.IsKeyboardFocusWithin,
            FirstItemIsHighlighted = firstItem.IsHighlighted
        };
    }

    private static string DescribeHwnd(IntPtr handle) => "0x" + handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);

    private static string InputElementHwnd(IInputElement element)
    {
        var source = element is Visual visual
            ? PresentationSource.FromVisual(visual) as System.Windows.Interop.HwndSource : null;
        return source == null ? null : DescribeHwnd(source.Handle);
    }

    private static string DescribeFocus(IInputElement element)
    {
        if (element == null) return "(null)";
        var detail = element is MenuItem item ? item.Header as string : (element as FrameworkElement)?.Name;
        return element.GetType().FullName + (string.IsNullOrEmpty(detail) ? "" : ":" + detail);
    }

    private static void Open(ContextMenu menu, MethodInfo pump)
    {
        menu.IsOpen = true;
        Pump(pump, menu.Dispatcher);
        menu.ApplyTemplate();
        menu.UpdateLayout();
        if (!menu.IsOpen || !menu.IsVisible || menu.ActualWidth <= 0 || menu.ActualHeight <= 0 ||
            PresentationSource.FromVisual(menu) == null)
            throw new InvalidOperationException("菜单未形成真实可见、正尺寸的隔离 HWND 内容。");
    }

    private static void TryCapture(
        string state, ContextMenu menu, MenuItem[] items, MethodInfo capture, MethodInfo writePng,
        string evidenceDirectory, TrayMenuVerificationEvidence result)
    {
        try
        {
            var bitmap = (BitmapSource)capture.Invoke(null, new object[] { menu });
            var path = Path.Combine(evidenceDirectory, state + ".png");
            var pixels = new Pixels(bitmap);
            var measured = new TrayMenuCaptureEvidence
            {
                State = state,
                Path = path,
                SHA256 = (string)writePng.Invoke(null, new object[] { path, bitmap }),
                Width = bitmap.PixelWidth,
                Height = bitmap.PixelHeight,
                NonTransparentPixels = pixels.NonTransparent,
                WarmSurfacePixels = pixels.Count(new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight), Surface),
                SurfaceForeground = BrushColor(menu.Foreground).ToString(),
                SurfaceBackground = BrushColor(menu.Background).ToString()
            };
            // Preserve the raw frame even when its subsequent geometry or colour checks fail.
            result.Captures.Add(measured);
            if (measured.NonTransparentPixels == 0 || measured.WarmSurfacePixels < 10 ||
                BrushColor(menu.Background) != Surface)
                result.Failures.Add(state + "：客户区未渲染出生产暖白背景。");

            foreach (var item in items)
            {
                var header = item.Header as string;
                var text = Descendants<TextBlock>(item)
                    .SingleOrDefault(block => block.IsVisible && block.Text == header);
                if (text == null)
                {
                    result.Failures.Add(state + "：菜单文案未形成唯一可见 TextBlock：" + header);
                    continue;
                }
                var itemBounds = item.TransformToAncestor(menu).TransformBounds(new Rect(item.RenderSize));
                var textBounds = text.TransformToAncestor(menu).TransformBounds(new Rect(text.RenderSize));
                var background = item.IsHighlighted ? Highlight : Surface;
                var foreground = BrushColor(text.Foreground);
                var formatted = new FormattedText(
                    text.Text, CultureInfo.InvariantCulture, text.FlowDirection,
                    new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                    text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
                var row = new TrayMenuRowEvidence
                {
                    Header = header,
                    Highlighted = item.IsHighlighted,
                    Bounds = new[] { itemBounds.X, itemBounds.Y, itemBounds.Width, itemBounds.Height },
                    Foreground = foreground.ToString(),
                    Background = BrushColor(item.Background).ToString(),
                    FontSize = text.FontSize,
                    ContrastRatio = Math.Round(Contrast(foreground, background), 6),
                    TextFits = text.ActualWidth > 0 && text.ActualHeight > 0 &&
                        text.ActualWidth + 0.5 >= formatted.WidthIncludingTrailingWhitespace &&
                        text.ActualHeight + 0.5 >= formatted.Height &&
                        itemBounds.Contains(textBounds) && new Rect(menu.RenderSize).Contains(itemBounds),
                    TextUsesInk = foreground == Ink,
                    TextPixels = pixels.Count(textBounds, foreground),
                    WarmBackgroundPixels = pixels.Count(itemBounds, background)
                };
                row.WarmBackgroundRendered = BrushColor(item.Background) == background &&
                    row.WarmBackgroundPixels >= Math.Max(10, itemBounds.Width * itemBounds.Height * 0.1);
                row.Readable = row.TextFits && row.TextPixels > 0 && row.ContrastRatio >= 4.5;
                measured.Rows.Add(row);
                if (!row.Readable || !row.WarmBackgroundRendered || !row.TextUsesInk)
                    result.Failures.Add(state + "：" + header +
                        " 可读性/暖粉实拍检查失败，contrast=" + row.ContrastRatio.ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (Exception exception)
        {
            result.Failures.Add(state + " 捕获异常：" + Detail(exception));
        }
    }

    private static Color BrushColor(Brush brush)
    {
        if (brush is SolidColorBrush solid) return solid.Color;
        throw new InvalidOperationException("菜单文字或背景不是预期的纯色画刷。");
    }

    private static double Contrast(Color first, Color second)
    {
        double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static string Detail(Exception exception) =>
        (exception is TargetInvocationException && exception.InnerException != null
            ? exception.InnerException : exception).Message;

    private static class MenuNativeInput
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr SetCapture(IntPtr window);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();
    }

    private sealed class Pixels
    {
        private readonly byte[] _bytes;
        private readonly int _width;
        private readonly int _height;
        public int NonTransparent { get; }

        public Pixels(BitmapSource bitmap)
        {
            if (bitmap.Format != PixelFormats.Pbgra32 && bitmap.Format != PixelFormats.Bgra32)
                throw new InvalidOperationException("菜单原帧必须为 32-bit BGRA。");
            _width = bitmap.PixelWidth;
            _height = bitmap.PixelHeight;
            _bytes = new byte[_width * _height * 4];
            bitmap.CopyPixels(_bytes, _width * 4, 0);
            for (int index = 3; index < _bytes.Length; index += 4)
                if (_bytes[index] != 0) NonTransparent++;
        }

        public int Count(Rect bounds, Color color)
        {
            int count = 0;
            for (int y = Math.Max(0, (int)Math.Floor(bounds.Top)); y < Math.Min(_height, (int)Math.Ceiling(bounds.Bottom)); y++)
            for (int x = Math.Max(0, (int)Math.Floor(bounds.Left)); x < Math.Min(_width, (int)Math.Ceiling(bounds.Right)); x++)
            {
                var offset = (y * _width + x) * 4;
                if (_bytes[offset + 3] == 255 &&
                    Math.Abs(_bytes[offset] - color.B) <= 3 &&
                    Math.Abs(_bytes[offset + 1] - color.G) <= 3 &&
                    Math.Abs(_bytes[offset + 2] - color.R) <= 3)
                    count++;
            }
            return count;
        }
    }
}
'@
}

$measured = [CountdownTrayMenuHarness]::Run($resolvedAssembly, $EvidenceDirectory)
$reportPath = Join-Path $EvidenceDirectory 'tray-menu-report.json'
$report = [pscustomobject]@{
    Suite = 'CountdownTrayMenu'
    DesktopName = $desktopName
    ApartmentState = [string] [Threading.Thread]::CurrentThread.ApartmentState
    AssemblyPath = $resolvedAssembly
    AssemblySHA256 = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash
    SourceSHA256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'src/MinminCountdown/TrayIconHost.cs') -Algorithm SHA256).Hash
    EvidenceDirectory = $EvidenceDirectory
    ReportPath = $reportPath
    ShellRegistrationPerformed = $measured.ShellRegistrationPerformed
    ShellDoubleClickValidated = $measured.ShellDoubleClickValidated
    ControllerBehaviorValidated = $measured.ControllerBehaviorValidated
    SettingsAccessPerformed = $measured.SettingsAccessPerformed
    CaptureScope = $measured.CaptureScope
    Factory = $measured.Factory
    InputPath = $measured.InputPath
    HighlightInputPath = $measured.HighlightInputPath
    KeyboardHighlightReached = $measured.KeyboardHighlightReached
    SourceActivation = $measured.SourceActivation
    KeyboardFocusSteps = @($measured.KeyboardFocusSteps)
    Captures = @($measured.Captures)
    Actions = @($measured.Actions)
    Failures = @($measured.Failures)
    Passed = $measured.Passed
}
$reportJson = $report | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($reportPath, $reportJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
if ($Json) { $reportJson } else { $report }
if (-not $measured.Passed) {
    throw "托盘菜单验证未通过；已保留全部结果和原帧：$reportPath"
}
