#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $AssemblyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app/MinminCountdown.dll')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteIsolation.psm1') -Force
if ([Environment]::GetEnvironmentVariable('MINMIN_SUITE_ALLOW_UNISOLATED') -eq '1') {
    throw '动效验证夹具拒绝非隔离绕过；必须经官方 ValidationHarness 隔离阶段执行。'
}
Assert-SuiteIsolatedDesktop
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw '弹出层验证反例必须由官方 ValidationHarness 阶段在 STA 隔离桌面中运行。'
}
if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
    throw "未找到被测应用程序集：$AssemblyPath"
}

$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$assemblyDirectory = Split-Path -Parent $resolvedAssembly
# PowerShell does not run the application's deps.json probing for pack URIs.
# Preload only these two collocated production dependencies before App resources.
$fixtureDependencies = @(
    foreach ($dependencyName in @('Wpf.Ui.Abstractions.dll', 'Wpf.Ui.dll')) {
        $dependencyPath = Join-Path $assemblyDirectory $dependencyName
        if (-not (Test-Path -LiteralPath $dependencyPath -PathType Leaf)) {
            throw "未找到夹具所需的生产依赖：$dependencyPath"
        }
        $dependency = [Reflection.Assembly]::LoadFrom($dependencyPath)
        [pscustomobject]@{ Path = $dependency.Location; Identity = $dependency.FullName }
    }
)
$references = @(
    Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' |
        Where-Object Name -ne 'WindowsBase.dll' |
        Select-Object -ExpandProperty FullName
    [System.Windows.Window].Assembly.Location
    [System.Windows.UIElement].Assembly.Location
    [System.Windows.Threading.Dispatcher].Assembly.Location
    [System.Windows.Markup.IQueryAmbient].Assembly.Location
)

# Fixtures replace only the target popup's event handling. All verdicts come
# from the currently built application's real ExercisePopup implementation.
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public sealed class PopupValidationContractResult
{
    public string Scenario { get; set; }
    public bool EscapeReturnsFocus { get; set; }
    public bool EscapeFromTriggerClosesAndKeepsFocus { get; set; }
    public bool TriggerSecondClickCloses { get; set; }
    public bool OutsideFocusPreserved { get; set; }
    public int HandledEscapeCount { get; set; }
    public bool CleanedAfterExercise { get; set; }
    public bool? ReopenedWithInitialFocus { get; set; }
}

public static class PopupValidationContractHarness
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeToolTipReference(string assemblyPath, bool enableMotion)
    {
        var tooltip = new ToolTip { Content = "Dynamic tooltip", PlacementTarget = new Button() };
        if (enableMotion)
            Assembly.LoadFrom(assemblyPath).GetType("MinminCountdown.PopupMotionBehavior", true)
                .GetMethod("SetIsToolTipEnabled", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { tooltip, true });
        return new WeakReference(tooltip);
    }

    public static object CheckToolTipCollectibility(string assemblyPath)
    {
        var plain = MakeToolTipReference(assemblyPath, false);
        var animated = MakeToolTipReference(assemblyPath, true);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return new { PlainCollected = !plain.IsAlive, AnimatedCollected = !animated.IsAlive };
    }

    public static object CheckClosedPopupClose(string assemblyPath, bool previouslyOpened, bool escape)
    {
        var application = Assembly.LoadFrom(assemblyPath);
        var input = application.GetType("MinminCountdown.WindowValidationInput", true);
        var motion = application.GetType("MinminCountdown.PopupMotionBehavior", true);
        var lifecycle = application.GetType("MinminCountdown.PopupFocusLifecycle", true);
        var pump = input.GetMethod("Pump", BindingFlags.Public | BindingFlags.Static);
        var wait = input.GetMethod("WaitForPopupClosed", BindingFlags.Public | BindingFlags.Static);
        var close = motion.GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(Popup) }, null);
        var trigger = new Button { Content = "Open popup" };
        var initial = new Button { Content = "Popup action" };
        var window = new Window { Content = trigger, Width = 220, Height = 120, ShowInTaskbar = false };
        var popup = new Popup { Child = initial, PlacementTarget = trigger, StaysOpen = true };
        int opened = 0;
        popup.Opened += (sender, args) => opened++;
        motion.GetMethod("Attach", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { popup });
        lifecycle.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static).Invoke(
            null, new object[] { popup, trigger, new Func<IInputElement>(() => initial) });
        try
        {
            window.Show();
            window.Activate();
            pump.Invoke(null, new object[] { window.Dispatcher });
            bool prerequisiteClosed = true;
            if (previouslyOpened)
            {
                popup.IsOpen = true;
                pump.Invoke(null, new object[] { popup.Dispatcher });
                close.Invoke(null, new object[] { popup });
                prerequisiteClosed = (bool)wait.Invoke(null, new object[] { popup, 300 });
            }
            var previousOpened = opened;
            if (escape)
                input.GetMethod("RaisePreviewKey", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { trigger, Key.Escape, ModifierKeys.None });
            else
                close.Invoke(null, new object[] { popup });
            pump.Invoke(null, new object[] { popup.Dispatcher });
            bool remainedClosed = !popup.IsOpen;
            bool finalClosed = (bool)wait.Invoke(null, new object[] { popup, 300 });
            return new { PreviouslyOpened = previouslyOpened, Action = escape ? "Escape" : "Close",
                PrerequisiteClosed = prerequisiteClosed, RemainedClosed = remainedClosed,
                FinalClosed = finalClosed, OpenedEvents = opened - previousOpened };
        }
        finally
        {
            input.GetMethod("ClosePopupForCleanup", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { popup });
            window.Close();
            pump.Invoke(null, new object[] { window.Dispatcher });
        }
    }

    public static object CheckCaptureComposite(string assemblyPath)
    {
        var method = Assembly.LoadFrom(assemblyPath).GetType("MinminCountdown.ValidationReport", true)
            .GetMethod("CompositeOverOpaqueBackground", BindingFlags.NonPublic | BindingFlags.Static);
        byte[] Composite(byte[] pixel, PixelFormat format, Color backdrop)
        {
            var source = BitmapSource.Create(1, 1, 96, 96, format, null, pixel, 4);
            var result = (BitmapSource)method.Invoke(null, new object[] { source, backdrop });
            var actual = new byte[4];
            result.CopyPixels(actual, 4, 0);
            return actual;
        }
        bool Matches(byte[] actual, byte b, byte g, byte r) =>
            actual[0] == b && actual[1] == g && actual[2] == r && actual[3] == 255;
        var backdrop = Color.FromRgb(20, 40, 60);
        var straight = Composite(new byte[] { 50, 100, 200, 128 }, PixelFormats.Bgra32, backdrop);
        var premultiplied = Composite(new byte[] { 128, 128, 128, 128 }, PixelFormats.Pbgra32, Colors.Black);
        var white = Composite(new byte[] { 255, 255, 255, 128 }, PixelFormats.Bgra32, Colors.White);
        var opaque = Composite(new byte[] { 30, 40, 50, 255 }, PixelFormats.Bgra32, backdrop);
        bool transparentRejected = false;
        try { Composite(new byte[] { 0, 0, 0, 0 }, PixelFormats.Bgra32, backdrop); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        { transparentRejected = true; }
        return new
        {
            StraightActualBgra = straight,
            PremultipliedActualBgra = premultiplied,
            WhiteActualBgra = white,
            StraightAlphaMatches = Matches(straight, 55, 70, 110),
            PremultipliedInputMatches = Matches(premultiplied, 128, 128, 128),
            HalfWhiteOverWhiteStaysWhite = Matches(white, 255, 255, 255),
            OpaqueSourcePreserved = Matches(opaque, 30, 40, 50),
            TransparentFrameRejected = transparentRejected
        };
    }

    public static PopupValidationContractResult Run(string assemblyPath, string scenario)
    {
        // Match ApplicationValidation's renderer on an isolated desktop, which
        // has no composited DWM surface for WPF's hardware animation clocks.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
        MotionValidationContractHarness.InitializeApplication(assemblyPath);
        Assembly application = Assembly.LoadFrom(assemblyPath);
        Type input = application.GetType("MinminCountdown.WindowValidationInput", true);
        MethodInfo exercise = input.GetMethod("ExercisePopup", BindingFlags.Public | BindingFlags.Static);
        MethodInfo pump = input.GetMethod("Pump", BindingFlags.Public | BindingFlags.Static);
        Type lifecycle = application.GetType("MinminCountdown.PopupFocusLifecycle", true);
        Type motion = application.GetType("MinminCountdown.PopupMotionBehavior", true);
        MethodInfo toggle = motion.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static);
        var trigger = new Button { Content = "Open popup", Width = 130, Height = 30 };
        var outside = new TextBox { Text = "Outside focus", Width = 160, Height = 30 };
        var initial = new Button { Content = "Popup action", Width = 130, Height = 30 };
        var surface = new StackPanel();
        surface.Children.Add(initial);
        var host = new StackPanel();
        host.Children.Add(trigger);
        host.Children.Add(outside);
        var window = new Window
        {
            Title = "MinminCountdown Popup Validation Contract",
            Width = 300,
            Height = 200,
            Content = host,
            ShowInTaskbar = false
        };
        var popup = new Popup
        {
            PlacementTarget = trigger,
            Placement = PlacementMode.Bottom,
            Child = surface,
            StaysOpen = true
        };
        var timers = new List<DispatcherTimer>();
        int handledEscapeCount = 0;
        if (scenario == "real-close" || scenario == "real-close-motion")
        {
            if (scenario == "real-close-motion")
            {
                motion.GetMethod("Attach", BindingFlags.NonPublic | BindingFlags.Static).Invoke(
                    null, new object[] { popup });
            }
            lifecycle.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static).Invoke(
                null, new object[] { popup, trigger, new Func<IInputElement>(() => initial) });
            KeyEventHandler recordEscape = (sender, args) =>
            {
                if (args.Key == Key.Escape && args.Handled) handledEscapeCount++;
            };
            surface.AddHandler(Keyboard.PreviewKeyDownEvent, recordEscape, true);
            trigger.AddHandler(Keyboard.PreviewKeyDownEvent, recordEscape, true);
        }
        else
        {
            popup.Opened += (sender, args) => popup.Dispatcher.BeginInvoke(
                DispatcherPriority.Input, new Action(() => Keyboard.Focus(initial)));
            KeyEventHandler brokenEscape = (sender, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                handledEscapeCount++;
                window.Activate();
                FocusManager.SetFocusedElement(window, trigger);
                Keyboard.Focus(trigger);
                if (scenario == "close-after-deadline")
                {
                    // The validator's 300ms deadline must fail before this close.
                    var timer = new DispatcherTimer(DispatcherPriority.Normal, popup.Dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(650)
                    };
                    timer.Tick += (timerSender, timerArgs) =>
                    {
                        timer.Stop();
                        popup.IsOpen = false;
                    };
                    timers.Add(timer);
                    timer.Start();
                }
            };
            surface.AddHandler(Keyboard.PreviewKeyDownEvent, brokenEscape, true);
            trigger.AddHandler(Keyboard.PreviewKeyDownEvent, brokenEscape, true);
        }
        trigger.Click += (sender, args) => toggle.Invoke(null, new object[] { popup });
        try
        {
            window.Show();
            window.Activate();
            pump.Invoke(null, new object[] { window.Dispatcher });
            object result = exercise.Invoke(null, new object[] { popup, trigger, initial, outside });
            Type resultType = result.GetType();
            bool cleanedAfterExercise = !popup.IsOpen;
            bool? reopenedWithInitialFocus = null;
            if (scenario == "real-close-motion")
            {
                reopenedWithInitialFocus = false;
                if (cleanedAfterExercise)
                {
                    Keyboard.Focus(outside);
                    popup.IsOpen = true;
                    pump.Invoke(null, new object[] { popup.Dispatcher });
                    reopenedWithInitialFocus = popup.IsOpen && ReferenceEquals(Keyboard.FocusedElement, initial);
                    input.GetMethod("ClosePopupForCleanup", BindingFlags.Public | BindingFlags.Static).Invoke(
                        null, new object[] { popup });
                }
            }
            return new PopupValidationContractResult
            {
                Scenario = scenario,
                EscapeReturnsFocus = (bool)resultType.GetProperty("EscapeReturnsFocus").GetValue(result),
                EscapeFromTriggerClosesAndKeepsFocus = (bool)resultType.GetProperty("EscapeFromTriggerClosesAndKeepsFocus").GetValue(result),
                TriggerSecondClickCloses = (bool)resultType.GetProperty("TriggerSecondClickCloses").GetValue(result),
                OutsideFocusPreserved = (bool)resultType.GetProperty("OutsideFocusPreserved").GetValue(result),
                HandledEscapeCount = handledEscapeCount,
                CleanedAfterExercise = cleanedAfterExercise,
                ReopenedWithInitialFocus = reopenedWithInitialFocus
            };
        }
        finally
        {
            foreach (DispatcherTimer timer in timers) timer.Stop();
            popup.IsOpen = false;
            window.Close();
            pump.Invoke(null, new object[] { window.Dispatcher });
        }
    }
}

public sealed class PopupInputObservation
{
    public string PlacementTargetType { get; set; }
    public string TemplatedParentType { get; set; }
    public string InputControlType { get; set; }
    public bool? IsDropDownOpen { get; set; }
    public string MouseCaptureType { get; set; }
    public bool MouseCaptureInPopupSource { get; set; }
    public bool? StaysOpen { get; set; }
}

public sealed class MotionObservation
{
    public string Phase { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public double[] Values { get; set; }
    public bool HasAnimation { get; set; }
    public bool? IsOpen { get; set; }
    public PopupInputObservation PopupInput { get; set; }
    public double? RenderingTimeMilliseconds { get; set; }
    public string ReusedFromPhase { get; set; }
}

public sealed class MotionSamplingWindow
{
    public string Phase { get; set; }
    public int FrameCount { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public double? FirstRenderingTimeMilliseconds { get; set; }
    public double? LastRenderingTimeMilliseconds { get; set; }
    public bool Matched { get; set; }
}

public sealed class MotionScenarioResult
{
    public string Scenario { get; set; }
    public bool Enabled { get; set; }
    public double[] TokenMilliseconds { get; set; }
    public double[] EffectiveMilliseconds { get; set; }
    public List<MotionObservation> Observations { get; } = new List<MotionObservation>();
    public List<MotionSamplingWindow> SamplingWindows { get; } = new List<MotionSamplingWindow>();
    public Dictionary<string, object> InputChecks { get; } = new Dictionary<string, object>();
    public List<string> Failures { get; } = new List<string>();
    public bool SystemPreferenceUnchanged { get; set; }
    public bool PreferenceRestored { get; set; }
    public bool ResourcesRestored { get; set; }
    public bool ExpectedExceptionObserved { get; set; }
    public PopupInputObservation PopupInputBeforeOpen { get; set; }
    public bool Passed => Failures.Count == 0;
    internal Stopwatch Clock = Stopwatch.StartNew();
    internal Func<PopupInputObservation> CapturePopupInput;
}

public static class MotionValidationContractHarness
{
    private const int DeadlineMilliseconds = 300;
    private static readonly string[] DurationKeys = {
        "MotionHoverDuration", "MotionPressDuration", "MotionPopupEnterDuration",
        "MotionPopupExitDuration", "MotionIndicatorDuration", "MotionCountdownDuration"
    };
    private static readonly double[] EnabledMilliseconds = { 120, 100, 140, 90, 160, 90 };
    private static readonly string[] ResourceKeys = DurationKeys.Concat(
        new[] { "MotionEaseOut", "MotionCubicEaseOut" }).ToArray();
    private static Assembly _assembly;
    private static Application _application;
    private static object _theme;
    private static bool _applicationExitObserved;

    private static Type AppType(string name) => _assembly.GetType("MinminCountdown." + name, true);

    private static object Invoke(Type type, object owner, string name, params object[] args)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic |
            (owner == null ? BindingFlags.Static : BindingFlags.Instance);
        var method = type.GetMethods(flags).Single(candidate =>
            candidate.Name == name && candidate.GetParameters().Length == args.Length &&
            candidate.GetParameters().Select((parameter, index) =>
                args[index] == null || parameter.ParameterType.IsInstanceOfType(args[index])).All(value => value));
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }

    private static object Call(string type, string name, params object[] args) =>
        Invoke(AppType(type), null, name, args);

    private static object Property(Type type, object owner, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic |
            (owner == null ? BindingFlags.Static : BindingFlags.Instance)).GetValue(owner);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void InitializeApplication(string assemblyPath)
    {
        if (_application != null)
        {
            Require(ReferenceEquals(Application.Current, _application), "验证 Application 被替换。");
            return;
        }

        Require(Application.Current == null, "不能把动效夹具接到已有的非夹具 Application。");
        _assembly = Assembly.LoadFrom(assemblyPath);
        var appType = AppType("App");
        var startup = appType.GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.DeclaredOnly, null, new[] { typeof(object), typeof(StartupEventArgs) }, null);
        Require(startup != null, "无法确认生产 Startup 处理器，拒绝初始化资源。");
        var initialize = appType.GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance);
        Require(initialize != null, "无法找到生产 App.xaml 资源入口。");
        var app = (Application)Activator.CreateInstance(appType);
        var handler = (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
        try { initialize.Invoke(app, null); }
        finally { app.Startup -= handler; }
        // No App.Run / Shutdown / OnExit calls. In particular, pump only after
        // removing the handler registered by generated InitializeComponent.
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Exit += (sender, args) => _applicationExitObserved = true;
        _application = app;
        _theme = Activator.CreateInstance(AppType("ApplicationTheme"),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object[] { app.Resources }, null);
        Invoke(_theme.GetType(), _theme, "Apply");
        Require(ProductionServicesAbsent(), "资源夹具意外启动了生产服务。");
    }

    private static bool ProductionServicesAbsent()
    {
        return new[] { "_options", "_logger", "_desktopWorkspace", "_floatingTrayController",
            "_instanceActivationServer", "_reminderDeliveryCoordinator", "_clockTimer" }
            .All(name => AppType("App").GetField(name, BindingFlags.NonPublic |
                BindingFlags.Instance).GetValue(_application) == null);
    }

    private static void Pump() => Call("WindowValidationInput", "Pump", _application.Dispatcher);

    // Rendering is raised after TimeManager.Tick and layout. A synchronous
    // RenderTargetBitmap capture is not an animation-clock tick source.
    private static MotionSamplingWindow SampleRendering(Visual visual, string phase,
        Func<RenderingEventArgs, bool> sample)
    {
        visual.Dispatcher.VerifyAccess();
        var sampling = new MotionSamplingWindow { Phase = phase };
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Send, visual.Dispatcher) {
            Interval = TimeSpan.FromMilliseconds(DeadlineMilliseconds)
        };
        // An isolated, idle HWND can wait for a channel commit longer than a
        // short transition. Request frames only while this observation is
        // active; animation values still come from real Rendering callbacks.
        var frameRequests = new DispatcherTimer(DispatcherPriority.Render, visual.Dispatcher) {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        Exception failure = null;
        EventHandler expired = (sender, args) => frame.Continue = false;
        EventHandler requestFrame = (sender, args) => {
            if (visual is UIElement surface) surface.InvalidateVisual();
        };
        EventHandler rendering = (sender, args) => {
            if (watch.Elapsed.TotalMilliseconds > DeadlineMilliseconds)
            {
                frame.Continue = false;
                return;
            }
            var rendered = (RenderingEventArgs)args;
            var time = rendered.RenderingTime.TotalMilliseconds;
            if (sampling.LastRenderingTimeMilliseconds == time) return;
            sampling.FirstRenderingTimeMilliseconds ??= time;
            sampling.LastRenderingTimeMilliseconds = time;
            sampling.FrameCount++;
            try
            {
                if (sample(rendered) && watch.Elapsed.TotalMilliseconds <= DeadlineMilliseconds)
                {
                    sampling.Matched = true;
                    frame.Continue = false;
                }
            }
            catch (Exception error) { failure = error; frame.Continue = false; }
        };
        try
        {
            timeout.Tick += expired;
            frameRequests.Tick += requestFrame;
            CompositionTarget.Rendering += rendering;
            timeout.Start();
            frameRequests.Start();
            requestFrame(null, EventArgs.Empty);
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timeout.Stop();
            timeout.Tick -= expired;
            frameRequests.Stop();
            frameRequests.Tick -= requestFrame;
            CompositionTarget.Rendering -= rendering;
            sampling.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
            sampling.Matched &= sampling.ElapsedMilliseconds <= DeadlineMilliseconds;
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return sampling;
    }

    private static void RenderTick(Visual visual)
    {
        Require(SampleRendering(visual, "fixture-first-frame", rendered => true).Matched,
            "300 ms 内没有真实 WPF Rendering 帧；不以位图捕获或 Dispatcher Pump 代替。");
    }

    private static Window ShowFixture(FrameworkElement content, double height = 260)
    {
        var window = new Window {
            Title = "MinminCountdown Motion Validation Contract",
            Width = 360, Height = height, ShowInTaskbar = false,
            ShowActivated = false, Content = content
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            RenderTick(content);
            Require(ProductionServicesAbsent(), "Dispatcher Pump 触发了生产启动流程。");
            return window;
        }
        catch
        {
            window.Close();
            Pump();
            throw;
        }
    }

    private static bool Animated(DependencyObject owner, DependencyProperty property) =>
        DependencyPropertyHelper.GetValueSource(owner, property).IsAnimated;

    private static TranslateTransform Translation(FrameworkElement visual)
    {
        if (visual.RenderTransform is TranslateTransform direct) return direct;
        if (visual.RenderTransform is TransformGroup group)
            return group.Children.OfType<TranslateTransform>().LastOrDefault();
        return null;
    }

    private static MotionObservation Observe(MotionScenarioResult result, string phase,
        Func<double[]> values, Func<bool> animated, Func<bool> isOpen = null,
        RenderingEventArgs rendering = null, bool record = true)
    {
        var observation = new MotionObservation {
            Phase = phase, ElapsedMilliseconds = result.Clock.Elapsed.TotalMilliseconds,
            Values = values(), HasAnimation = animated(), IsOpen = isOpen == null ? (bool?)null : isOpen(),
            PopupInput = result.CapturePopupInput?.Invoke(),
            RenderingTimeMilliseconds = rendering?.RenderingTime.TotalMilliseconds
        };
        if (record) result.Observations.Add(observation);
        return observation;
    }

    private static MotionObservation ObserveFrame(MotionScenarioResult result, string phase,
        Visual visual, Func<double[]> values, Func<bool> animated, Func<bool> isOpen = null) =>
        WaitFor(result, phase, visual, values, animated, current => true, isOpen);

    private static MotionObservation WaitFor(MotionScenarioResult result, string phase,
        Visual visual, Func<double[]> values, Func<bool> animated,
        Func<double[], bool> condition, Func<bool> isOpen = null, MotionObservation firstFrame = null)
    {
        // The caller already checked continuity. Its first real rendered frame
        // may also be the required intermediate sample; do not discard it.
        if (firstFrame?.RenderingTimeMilliseconds != null &&
            ReferenceEquals(result.Observations.LastOrDefault(), firstFrame) &&
            result.Clock.Elapsed.TotalMilliseconds - firstFrame.ElapsedMilliseconds <= DeadlineMilliseconds &&
            condition(firstFrame.Values))
        {
            var reused = new MotionObservation {
                Phase = phase, ElapsedMilliseconds = firstFrame.ElapsedMilliseconds,
                Values = firstFrame.Values.ToArray(), HasAnimation = firstFrame.HasAnimation,
                IsOpen = firstFrame.IsOpen, PopupInput = firstFrame.PopupInput,
                RenderingTimeMilliseconds = firstFrame.RenderingTimeMilliseconds,
                ReusedFromPhase = firstFrame.Phase
            };
            result.Observations.Add(reused);
            return reused;
        }
        MotionObservation observed = null;
        var sampling = SampleRendering(visual, phase, rendered => {
            var candidate = Observe(result, phase, values, animated, isOpen, rendered, record: false);
            if (!condition(candidate.Values)) return false;
            observed = candidate;
            return true;
        });
        result.SamplingWindows.Add(sampling);
        if (sampling.Matched)
        {
            result.Observations.Add(observed);
            return observed;
        }
        Observe(result, phase + "-unobserved", values, animated, isOpen);
        throw new InvalidOperationException(phase + "：300 ms 内未取得所需真实样本；renderedFrames=" +
            sampling.FrameCount + "；不把终态或缺帧当通过。");
    }

    private static bool Near(double[] actual, double[] expected, double tolerance) =>
        actual.Length == expected.Length && actual.Zip(expected, (left, right) =>
            !double.IsNaN(left) && Math.Abs(left - right) <= tolerance).All(value => value);

    private static bool Intermediate(double[] start, double[] current, double[] end)
    {
        if (start.Length != current.Length || current.Length != end.Length ||
            current.Any(value => double.IsNaN(value) || double.IsInfinity(value))) return false;
        var changed = false;
        for (int index = 0; index < start.Length; index++)
        {
            var span = end[index] - start[index];
            if (Math.Abs(span) < 0.000001) continue;
            var progress = (current[index] - start[index]) / span;
            if (progress < 0.1 || progress > 0.9) return false;
            changed = true;
        }
        return changed;
    }

    // Bound the first rendered observation by the real elapsed time and the
    // contract's quartic ease-out; a whole skipped transition is not evidence.
    private static bool Continues(MotionObservation before, MotionObservation after,
        double[] target, double durationMs, double tolerance)
    {
        var elapsed = after.ElapsedMilliseconds - before.ElapsedMilliseconds;
        if (elapsed < 0 || elapsed >= durationMs || before.Values.Length != after.Values.Length)
            return false;
        var maximum = 1 - Math.Pow(1 - Math.Min(1, (elapsed + 1) / durationMs), 4);
        var moved = false;
        for (int index = 0; index < target.Length; index++)
        {
            var span = target[index] - before.Values[index];
            var step = after.Values[index] - before.Values[index];
            if (double.IsNaN(step) || double.IsInfinity(step)) return false;
            if (Math.Abs(span) <= tolerance)
            {
                if (Math.Abs(step) > tolerance) return false;
                continue;
            }
            if (step * Math.Sign(span) < -tolerance ||
                Math.Abs(step) > Math.Abs(span) * maximum + tolerance) return false;
            if (Math.Abs(target[index] - after.Values[index]) > tolerance) moved = true;
        }
        return moved;
    }

    // Popup and digit controllers synchronously commit the captured current value.
    // WPF frame-clock progress is not the Stopwatch interval between these reads.
    // Require that snapshot, a live nonterminal frame in the target direction, and
    // leave each caller's subsequent real 10%-90% observation mandatory.
    private static bool ContinuesFromSnapshot(MotionObservation before, MotionObservation committed,
        MotionObservation after, double[] target, double durationMs, double tolerance)
    {
        var elapsed = after.ElapsedMilliseconds - before.ElapsedMilliseconds;
        if (committed.ElapsedMilliseconds < before.ElapsedMilliseconds ||
            after.ElapsedMilliseconds < committed.ElapsedMilliseconds ||
            elapsed < 0 || elapsed >= durationMs || !after.HasAnimation ||
            before.Values.Length != target.Length || after.Values.Length != target.Length ||
            !Near(committed.Values, before.Values, tolerance)) return false;
        var unfinished = false;
        for (int index = 0; index < target.Length; index++)
        {
            var span = target[index] - before.Values[index];
            var step = after.Values[index] - before.Values[index];
            if (double.IsNaN(span) || double.IsInfinity(span) ||
                double.IsNaN(step) || double.IsInfinity(step)) return false;
            if (Math.Abs(span) <= tolerance)
            {
                if (Math.Abs(step) > tolerance) return false;
                continue;
            }
            if (step * Math.Sign(span) < -tolerance || Math.Abs(step) > Math.Abs(span) + tolerance ||
                Math.Abs(target[index] - after.Values[index]) <= tolerance) return false;
            unfinished = true;
        }
        return unfinished;
    }

    private static bool Immediate(double[] values, double[] target, bool animated, double tolerance) =>
        !animated && Near(values, target, tolerance);

    private static MotionScenarioResult RunCase(string scenario, bool enabled,
        Action<MotionScenarioResult> exercise, bool expectedException = false)
    {
        var result = new MotionScenarioResult { Scenario = scenario, Enabled = enabled };
        var systemBefore = SystemParameters.ClientAreaAnimation;
        var saved = ResourceKeys.ToDictionary(key => key, key => _application.Resources[key]);
        IDisposable scope = null;
        try
        {
            scope = (IDisposable)Call("ApplicationTheme", "OverrideAnimationPreferenceForValidation", enabled);
            Invoke(_theme.GetType(), _theme, "Apply");
            result.TokenMilliseconds = DurationKeys.Select(key =>
                ((Duration)_application.Resources[key]).TimeSpan.TotalMilliseconds).ToArray();
            result.EffectiveMilliseconds = EnabledMilliseconds.Select(value =>
                ((TimeSpan)Call("ApplicationTheme", "GetEffectiveMotionDuration",
                    TimeSpan.FromMilliseconds(value))).TotalMilliseconds).ToArray();
            var expected = enabled ? EnabledMilliseconds : new double[6];
            Require(Near(result.TokenMilliseconds, expected, 0) &&
                Near(result.EffectiveMilliseconds, expected, 0), "token 或有效时长不符合注入模式。");
            Require(_application.Resources["MotionEaseOut"] is PowerEase easing &&
                easing.Power == 4 && easing.EasingMode == EasingMode.EaseOut,
                "生产缓动不是 quartic ease-out。");
            exercise(result);
            Require(!expectedException, "异常恢复负例没有实际抛出。");
        }
        catch (ExpectedFixtureException)
        {
            result.ExpectedExceptionObserved = expectedException;
            if (!expectedException) result.Failures.Add("意外夹具异常。");
        }
        catch (Exception error) { result.Failures.Add(error.GetType().Name + ": " + error.Message); }
        finally
        {
            try { scope?.Dispose(); }
            catch (Exception error) { result.Failures.Add("偏好恢复失败：" + error.Message); }
            foreach (var item in saved) _application.Resources[item.Key] = item.Value;
            result.SystemPreferenceUnchanged = SystemParameters.ClientAreaAnimation == systemBefore;
            result.PreferenceRestored = !(bool)Property(AppType("ApplicationTheme"), null,
                "HasAnimationPreferenceOverrideForValidation") &&
                (bool)Property(AppType("ApplicationTheme"), null, "ClientAreaAnimationEnabled") ==
                    SystemParameters.ClientAreaAnimation;
            result.ResourcesRestored = saved.All(item => Equals(_application.Resources[item.Key], item.Value));
            if (!result.SystemPreferenceUnchanged || !result.PreferenceRestored || !result.ResourcesRestored)
                result.Failures.Add("系统偏好、输入偏好或 token 资源没有恢复。");
        }
        return result;
    }

    private sealed class ExpectedFixtureException : Exception { }

    private static double[] ColorValues(Border surface)
    {
        var color = ((SolidColorBrush)surface.Background).Color;
        return new[] { (double)color.R, color.G, color.B };
    }

    private static void Buttons(MotionScenarioResult result)
    {
        var button = new Button { Content = "Motion fixture", Width = 140, Height = 40,
            Style = (Style)_application.FindResource("PrimaryButton") };
        var panel = new StackPanel { Children = { button } };
        Window window = null;
        FrameworkElement host = null;
        try
        {
            window = ShowFixture(panel);
            button.ApplyTemplate();
            host = (FrameworkElement)button.Template.FindName("InteractionSurface", button);
            var surface = (Border)button.Template.FindName("Surface", button);
            Require(host != null && surface != null, "PrimaryButton 生产模板缺少交互面。");
            Action<bool, bool> input = (hover, pressed) =>
                Call("MotionBehavior", "SetInputForValidation", host, hover, pressed);
            Action commit = () => _application.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            Func<double[]> color = () => ColorValues(surface);
            Func<bool> colorClock = () => Animated(surface, Border.BackgroundProperty);
            Func<double[]> scale = () => {
                var transform = (ScaleTransform)surface.RenderTransform;
                return new[] { transform.ScaleX, transform.ScaleY };
            };
            Func<bool> scaleClock = () => {
                var transform = (ScaleTransform)surface.RenderTransform;
                return Animated(transform, ScaleTransform.ScaleXProperty) ||
                    Animated(transform, ScaleTransform.ScaleYProperty);
            };
            Func<string, double[]> expectedColor = key => {
                var value = ((SolidColorBrush)_application.FindResource(key)).Color;
                return new[] { (double)value.R, value.G, value.B };
            };
            var normal = expectedColor("MainActionBrush");
            var hoverTarget = expectedColor("MainActionHoverBrush");
            var pressedTarget = expectedColor("MainActionPressedBrush");
            var size = button.RenderSize;
            input(false, false); commit();
            WaitFor(result, "button-normal", surface, color, colorClock, value => Near(value, normal, 1));
            input(true, false); commit();
            Observe(result, "hover-immediate", color, colorClock);
            if (!result.Enabled)
            {
                Require(Immediate(color(), hoverTarget, colorClock(), 0), "off hover 未直接到目标色。");
                input(true, true); commit();
                Observe(result, "press-off", scale, scaleClock);
                Require(Immediate(scale(), new[] { 0.97, 0.97 }, scaleClock(), 0.000001) &&
                    Immediate(color(), pressedTarget, colorClock(), 0), "off press 仍有动画或目标错误。");
                input(false, false); commit();
                Observe(result, "release-off", scale, scaleClock);
                Require(Immediate(scale(), new[] { 1.0, 1.0 }, scaleClock(), 0.000001) &&
                    Immediate(color(), normal, colorClock(), 0), "off release 未直接复位。");
            }
            else
            {
                var hoverMiddle = WaitFor(result, "hover-middle", surface, color, colorClock,
                    value => Intermediate(normal, value, hoverTarget));
                var before = Observe(result, "hover-reverse-before", color, colorClock);
                input(false, false); commit();
                Observe(result, "hover-reverse-committed", color, colorClock);
                var after = ObserveFrame(result, "hover-reverse-first-frame", surface, color, colorClock);
                Require(Continues(before, after, normal, 120, 1), "hover 反转首帧未从当前颜色续接。");
                WaitFor(result, "hover-reverse-middle", surface, color, colorClock,
                    value => Intermediate(before.Values, value, normal), firstFrame: after);
                input(true, true); commit();
                var pressed = WaitFor(result, "press-middle", surface, scale, scaleClock,
                    value => Intermediate(new[] { 1.0, 1.0 }, value, new[] { 0.97, 0.97 }));
                before = Observe(result, "release-before", scale, scaleClock);
                input(true, false); commit();
                Observe(result, "release-committed", scale, scaleClock);
                after = ObserveFrame(result, "release-first-frame", surface, scale, scaleClock);
                Require(Continues(before, after, new[] { 1.0, 1.0 }, 100, 0.0002),
                    "press 中断释放未从当前缩放续接。");
                WaitFor(result, "release-middle", surface, scale, scaleClock,
                    value => Intermediate(before.Values, value, new[] { 1.0, 1.0 }), firstFrame: after);
                before = Observe(result, "repress-before", scale, scaleClock);
                input(true, true); commit();
                after = ObserveFrame(result, "repress-first-frame", surface, scale, scaleClock);
                Require(Continues(before, after, new[] { 0.97, 0.97 }, 100, 0.0002),
                    "连续 press 反转未从当前缩放续接。");
                WaitFor(result, "press-final", surface, scale, scaleClock,
                    value => Near(value, new[] { 0.97, 0.97 }, 0.00001));
                input(false, false); commit();
                WaitFor(result, "release-final", surface, scale, scaleClock,
                    value => Near(value, new[] { 1.0, 1.0 }, 0.00001));
            }
            Require(button.RenderSize == size, "按钮动效改变了布局尺寸。");
        }
        finally
        {
            try
            {
                if (host != null) Call("MotionBehavior", "ClearInputForValidation", host);
                Pump();
            }
            finally { window?.Close(); Pump(); }
        }
    }

    private static void ExercisePopupMotion(MotionScenarioResult result, string category)
    {
        var panel = new StackPanel();
        var trigger = new Button { Content = category, Width = 160, Height = 38 };
        panel.Children.Add(trigger);
        DependencyObject owner = null;
        FrameworkElement visual = null;
        FrameworkElement inputControl = null;
        Window window = null;
        Action open = null;
        Action close = null;
        Func<bool> isOpen = null;
        try
        {
            if (category == "calendar")
            {
                var picker = (DatePicker)Activator.CreateInstance(AppType("WpfUiDatePicker"), true);
                picker.SelectedDate = new DateTime(2026, 10, 8);
                picker.Width = 220;
                inputControl = picker;
                panel.Children.Add(picker);
                window = ShowFixture(panel); picker.ApplyTemplate();
                var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
                owner = popup; visual = (FrameworkElement)popup.Child;
                open = () => picker.IsDropDownOpen = true;
                close = () => picker.IsDropDownOpen = false;
                isOpen = () => popup.IsOpen;
            }
            else if (category == "combobox")
            {
                var combo = new ComboBox { Width = 220, Items = { "One", "Two" }, SelectedIndex = 0 };
                inputControl = combo;
                panel.Children.Add(combo);
                window = ShowFixture(panel); combo.ApplyTemplate();
                var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
                owner = popup; visual = (FrameworkElement)popup.Child;
                open = () => combo.IsDropDownOpen = true;
                close = () => combo.IsDropDownOpen = false;
                isOpen = () => popup.IsOpen;
            }
            else if (category == "tags")
            {
                var tags = (FrameworkElement)Activator.CreateInstance(AppType("TagSelector"), true);
                inputControl = tags;
                panel.Children.Add(tags);
                window = ShowFixture(panel);
                var popup = (Popup)Property(tags.GetType(), tags, "Popup");
                owner = popup; visual = (FrameworkElement)popup.Child;
                var property = tags.GetType().GetProperty("IsPopupOpen");
                open = () => property.SetValue(tags, true);
                close = () => property.SetValue(tags, false);
                isOpen = () => popup.IsOpen;
            }
            else
            {
                var tooltip = new ToolTip { Content = "Production tooltip motion", PlacementTarget = trigger,
                    Style = (Style)_application.FindResource(typeof(ToolTip)) };
                inputControl = trigger;
                owner = tooltip; visual = tooltip;
                window = ShowFixture(panel);
                open = () => Call("PopupMotionBehavior", "Open", tooltip);
                close = () => Call("PopupMotionBehavior", "Close", tooltip);
                isOpen = () => tooltip.IsOpen;
                Require((bool)Call("PopupMotionBehavior", "GetIsToolTipEnabled", tooltip),
                    "ToolTip 没有生产动效样式。");
            }
            Require(owner != null && visual != null, category + " 没有真实弹层内容。");
            result.CapturePopupInput = () => {
                var placementTarget = owner is Popup popupState
                    ? popupState.PlacementTarget : (owner as ToolTip)?.PlacementTarget;
                return new PopupInputObservation {
                    PlacementTargetType = placementTarget?.GetType().FullName,
                    TemplatedParentType = (owner as FrameworkElement)?.TemplatedParent?.GetType().FullName,
                    InputControlType = inputControl?.GetType().FullName,
                    IsDropDownOpen = inputControl is ComboBox comboState ? (bool?)comboState.IsDropDownOpen :
                        inputControl is DatePicker pickerState ? (bool?)pickerState.IsDropDownOpen : null,
                    MouseCaptureType = Mouse.Captured?.GetType().FullName,
                    MouseCaptureInPopupSource = Mouse.Captured is Visual captured &&
                        PresentationSource.FromVisual(visual) is PresentationSource popupSource &&
                        ReferenceEquals(PresentationSource.FromVisual(captured), popupSource),
                    StaysOpen = owner is Popup capturedPopup ? (bool?)capturedPopup.StaysOpen : null
                };
            };
            result.PopupInputBeforeOpen = result.CapturePopupInput();
            if (owner is Popup actualPopup)
                Require((bool)Call("PopupMotionBehavior", "IsAttached", actualPopup),
                    category + " 未附加生产动效。");
            Func<double[]> values = () => new[] { visual.Opacity, Translation(visual)?.Y ?? double.NaN };
            Func<bool> animated = () => Animated(visual, UIElement.OpacityProperty) ||
                (Translation(visual) is TranslateTransform move && Animated(move, TranslateTransform.YProperty));
            open();
            var initial = Observe(result, category + "-open-immediate", values, animated, isOpen);
            if (!result.Enabled)
            {
                Require(isOpen() && Immediate(initial.Values, new[] { 1.0, 0.0 }, animated(), 0.000001),
                    category + " off 打开不是即时终态。");
                close();
                var closed = Observe(result, category + "-close-immediate", values, animated, isOpen);
                Require(!isOpen() && Immediate(closed.Values, new[] { 0.0, 4.0 }, animated(), 0.000001),
                    category + " off 关闭没有即时完成。");
                return;
            }
            WaitFor(result, category + "-opening-middle", visual, values, animated,
                value => Intermediate(new[] { 0.0, 4.0 }, value, new[] { 1.0, 0.0 }), isOpen);
            var before = Observe(result, category + "-close-before", values, animated, isOpen);
            close();
            var immediate = Observe(result, category + "-close-committed", values, animated, isOpen);
            Require(isOpen(), category + " 有动画关闭未保留当前可见弹层。");
            Require(immediate.PopupInput.IsDropDownOpen != true,
                category + " 退出期间把宿主关闭请求回写成了打开。");
            var after = ObserveFrame(result, category + "-close-first-frame", visual, values, animated, isOpen);
            Require(ContinuesFromSnapshot(before, immediate, after, new[] { 0.0, 4.0 }, 90, 0.002),
                category + " 打开中途关闭未从当前值续接。");
            WaitFor(result, category + "-closing-middle", visual, values, animated,
                value => Intermediate(before.Values, value, new[] { 0.0, 4.0 }), isOpen, after);
            before = Observe(result, category + "-reopen-before", values, animated, isOpen);
            open();
            immediate = Observe(result, category + "-reopen-committed", values, animated, isOpen);
            after = ObserveFrame(result, category + "-reopen-first-frame", visual, values, animated, isOpen);
            Require(ContinuesFromSnapshot(before, immediate, after, new[] { 1.0, 0.0 }, 140, 0.002),
                category + " 退出中途重开未从当前值续接。");
            WaitFor(result, category + "-reopening-middle", visual, values, animated,
                value => isOpen() && Intermediate(before.Values, value, new[] { 1.0, 0.0 }), isOpen, after);
            WaitFor(result, category + "-reopen-final", visual, values, animated,
                value => isOpen() && Near(value, new[] { 1.0, 0.0 }, 0.000001), isOpen);
            if (owner is Popup nativePopup && (inputControl is DatePicker || inputControl is ComboBox))
            {
                before = Observe(result, category + "-native-close-before", values, animated, isOpen);
                nativePopup.SetCurrentValue(Popup.IsOpenProperty, false);
                immediate = Observe(result, category + "-native-close-committed", values, animated, isOpen);
                Require(isOpen() && immediate.PopupInput.IsDropDownOpen == false,
                    category + " 原生关闭没有分离宿主请求与仍可见的退出表面。");
                after = ObserveFrame(result, category + "-native-close-first-frame", visual, values, animated, isOpen);
                Require(ContinuesFromSnapshot(before, immediate, after, new[] { 0.0, 4.0 }, 90, 0.002),
                    category + " 原生关闭没有从当前值退出。");
                WaitFor(result, category + "-native-closing-middle", visual, values, animated,
                    value => Intermediate(before.Values, value, new[] { 0.0, 4.0 }), isOpen, after);
                before = Observe(result, category + "-native-reopen-before", values, animated, isOpen);
                open();
                immediate = Observe(result, category + "-native-reopen-committed", values, animated, isOpen);
                after = ObserveFrame(result, category + "-native-reopen-first-frame", visual, values, animated, isOpen);
                Require(ContinuesFromSnapshot(before, immediate, after, new[] { 1.0, 0.0 }, 140, 0.002),
                    category + " 原生退出中途重开没有从当前值续接。");
                WaitFor(result, category + "-native-reopening-middle", visual, values, animated,
                    value => isOpen() && Intermediate(before.Values, value, new[] { 1.0, 0.0 }), isOpen, after);
                WaitFor(result, category + "-native-reopen-final", visual, values, animated,
                    value => isOpen() && Near(value, new[] { 1.0, 0.0 }, 0.000001), isOpen);
            }
            close();
            WaitFor(result, category + "-close-final", visual, values, animated,
                value => !isOpen() && Near(value, new[] { 0.0, 4.0 }, 0.000001), isOpen);
        }
        finally
        {
            result.CapturePopupInput = null;
            try
            {
                if (owner is Popup popup)
                    Call("WindowValidationInput", "ClosePopupForCleanup", popup);
                else if (owner is ToolTip tooltip)
                {
                    // Cleanup follows the recorded verdict; it is not a close observation.
                    Call("PopupMotionBehavior", "SetIsToolTipEnabled", tooltip, false);
                    tooltip.IsOpen = false;
                }
            }
            catch (Exception error) { result.Failures.Add("夹具清理失败：" + error.Message); }
            finally { window?.Close(); Pump(); }
        }
    }

    private static void DatePickerInterruptedInput(MotionScenarioResult result, bool textTrigger)
    {
        var picker = (DatePicker)Activator.CreateInstance(AppType("WpfUiDatePicker"), true);
        picker.SelectedDate = new DateTime(2026, 10, 8);
        picker.Width = 220;
        var outside = new TextBox { Name = "OutsideInput", Text = "External focus fixture", Margin = new Thickness(0, 330, 0, 0) };
        var panel = new StackPanel { Children = { picker, outside } };
        Window window = null;
        Popup popup = null;
        IDisposable pointerState = null;
        try
        {
            window = ShowFixture(panel, 500);
            window.Activate();
            picker.ApplyTemplate();
            popup = (Popup)picker.Template.FindName("PART_Popup", picker);
            var calendar = (Calendar)popup.Child;
            var trigger = (Control)picker.Template.FindName(textTrigger ? "PART_TextToggle" : "PART_Button", picker);
            int openedCount = 0, closedCount = 0;
            popup.Opened += (sender, args) => openedCount++;
            popup.Closed += (sender, args) => closedCount++;
            pointerState = (IDisposable)Activator.CreateInstance(AppType("WindowValidationInput+PickerPointerState"), true);
            var pointerActions = new List<object>();
            result.InputChecks["PointerActions"] = pointerActions;
            Action<Control> click = target => {
                var pressed = (bool)Call("WindowValidationInput", "RaisePickerPointer", target,
                    UIElement.PreviewMouseLeftButtonDownEvent);
                Pump();
                var focusedOnPress = target.IsKeyboardFocusWithin;
                var released = (bool)Call("WindowValidationInput", "RaisePickerPointer", target,
                    UIElement.PreviewMouseLeftButtonUpEvent);
                Pump();
                pointerActions.Add(new {
                    Target = target.Name, Type = target.GetType().FullName,
                    Visible = target.IsVisible, Enabled = target.IsEnabled,
                    PressHandled = pressed, ReleaseHandled = released,
                    FocusedOnPress = focusedOnPress, FocusedAfterRelease = target.IsKeyboardFocusWithin
                });
                if (target is TextBox)
                    Require(focusedOnPress && target.IsKeyboardFocusWithin, "隔离 pointer 未把焦点交给外部输入框。");
                else
                    Require(released, "隔离 pointer release 未被触发器处理：" + target.Name);
            };
            Func<double[]> values = () => new[] { calendar.Opacity, Translation(calendar)?.Y ?? double.NaN };
            Func<bool> animated = () => Animated(calendar, UIElement.OpacityProperty);
            Func<bool> isOpen = () => popup.IsOpen;
            Func<bool> popupCapture = () => Mouse.Captured is Visual captured &&
                PresentationSource.FromVisual(calendar) is PresentationSource popupSource &&
                ReferenceEquals(PresentationSource.FromVisual(captured), popupSource);
            click(outside);
            click(trigger);
            Require(picker.IsDropDownOpen && popup.IsOpen, "真实触发器 pointer 没有打开日历。");
            WaitFor(result, "pointer-open-final", calendar, values, animated,
                value => isOpen() && Near(value, new[] { 1.0, 0.0 }, 0.000001), isOpen);
            // The hidden desktop has no physical cursor over the popup HWND.
            // Activate that input source, then let WPF's StaysOpen callback
            // acquire its own PopupRoot capture. Never substitute child capture.
            var popupSource = PresentationSource.FromVisual(calendar) as HwndSource;
            Require(popupSource?.RootVisual is Visual, "日历没有可激活的真实 Popup HWND。");
            var selectedBeforeActivation = picker.SelectedDate;
            var openEventsBeforeActivation = openedCount;
            var closeEventsBeforeActivation = closedCount;
            result.InputChecks["CaptureBeforeSourceActivation"] = Mouse.Captured?.GetType().FullName ?? "none";
            Require(!popup.StaysOpen, "日历捕获前提要求原生外点关闭。");
            popup.SetCurrentValue(Popup.StaysOpenProperty, true);
            try
            {
                Call("WindowValidationInput+PickerNativeInput", "SetCapture", popupSource.Handle);
                Require((IntPtr)Call("WindowValidationInput+PickerNativeInput", "GetCapture") == popupSource.Handle,
                    "隔离日历 HWND 未能激活输入源。");
                var point = calendar.TransformToAncestor((Visual)popupSource.RootVisual).Transform(
                    new Point(calendar.ActualWidth / 2, 1));
                point = popupSource.CompositionTarget.TransformToDevice.Transform(point);
                var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
                Call("WindowValidationInput+PickerNativeInput", "SendMessage",
                    popupSource.Handle, (uint)0x0200, IntPtr.Zero, coordinates);
            }
            finally { popup.SetCurrentValue(Popup.StaysOpenProperty, false); }
            Pump();
            result.InputChecks["SourceActivationKeptPickerState"] = picker.IsDropDownOpen && popup.IsOpen &&
                picker.SelectedDate == selectedBeforeActivation && openedCount == openEventsBeforeActivation &&
                closedCount == closeEventsBeforeActivation;
            Require((bool)result.InputChecks["SourceActivationKeptPickerState"],
                "隔离输入源激活意外改变了日期或弹层开关。");
            result.InputChecks["CaptureBeforeEscape"] = Mouse.Captured?.GetType().FullName ?? "none";
            result.InputChecks["PopupCapturedBeforeEscape"] = popupCapture();
            Require(popupCapture(), "日历打开后未建立捕获，不能据此验证退出时释放捕获。");
            var handled = (bool)Call("WindowValidationInput", "RaisePreviewKey", calendar, Key.Escape, ModifierKeys.None);
            var closeStart = Observe(result, "pointer-escape-committed", values, animated, isOpen);
            result.InputChecks["EscapeHandled"] = handled;
            result.InputChecks["LogicalClosedWhileVisible"] = !picker.IsDropDownOpen && popup.IsOpen;
            result.InputChecks["CaptureDuringExit"] = Mouse.Captured?.GetType().FullName ?? "none";
            result.InputChecks["PopupCapturedDuringExit"] = popupCapture();
            Require(handled && !picker.IsDropDownOpen && popup.IsOpen, "Esc 未启动逻辑关闭与可见退出。");
            Require(!popupCapture(), "退出中的日历仍抓捕鼠标，下一次宿主点击不可达。");
            WaitFor(result, "pointer-escape-middle", calendar, values, animated,
                value => Intermediate(closeStart.Values, value, new[] { 0.0, 4.0 }), isOpen);
            Require(popup.IsOpen && (bool)Call("PopupMotionBehavior", "IsClosing", popup),
                "重开注入前日历已完成退出，未覆盖中途重开。");
            var openedBeforeReopen = openedCount;
            var closedBeforeReopen = closedCount;
            click(trigger);
            result.InputChecks["PointerReopenedDuringExit"] = picker.IsDropDownOpen && popup.IsOpen;
            result.InputChecks["EscapeReopenRetainedSurface"] =
                openedCount == openedBeforeReopen && closedCount == closedBeforeReopen;
            Require(picker.IsDropDownOpen && popup.IsOpen, "退出期间真实触发器 pointer 没有重新打开日历。");
            Require(openedCount == openedBeforeReopen && closedCount == closedBeforeReopen,
                "Esc 后的重开已重建弹层，未覆盖同一表面的中途反向。");
            result.InputChecks["DismissPolicyRestoredAfterReopen"] = !popup.StaysOpen;
            Require(!popup.StaysOpen, "重开日历后未恢复原生外点关闭策略。");
            WaitFor(result, "pointer-reopen-final", calendar, values, animated,
                value => isOpen() && Near(value, new[] { 1.0, 0.0 }, 0.000001), isOpen);
            click(outside);
            var outsideFocused = outside.IsKeyboardFocusWithin;
            result.InputChecks["OutsideFocusedBeforeClose"] = outsideFocused;
            WaitFor(result, "pointer-outside-close-final", calendar, values, animated,
                value => !isOpen() && Near(value, new[] { 0.0, 4.0 }, 0.000001), isOpen);
            Pump();
            result.InputChecks["OutsideFocusPreserved"] = outside.IsKeyboardFocusWithin;
            Require(outsideFocused && outside.IsKeyboardFocusWithin,
                "Esc 被重开打断后，旧关闭标记夺走了外部输入框的焦点。");

            click(trigger);
            WaitFor(result, "selection-open-final", calendar, values, animated,
                value => isOpen() && Near(value, new[] { 1.0, 0.0 }, 0.000001), isOpen);
            var selected = new DateTime(2026, 10, 12);
            calendar.SelectedDate = selected;
            popup.SetCurrentValue(Popup.IsOpenProperty, false);
            Require(picker.SelectedDate == selected && !picker.IsDropDownOpen && popup.IsOpen,
                "日期选择与原生关闭没有保留新日期及退出表面。");
            Require((bool)Call("PopupMotionBehavior", "IsClosing", popup),
                "日期选择后的重开注入前已完成退出。");
            openedBeforeReopen = openedCount;
            closedBeforeReopen = closedCount;
            click(trigger);
            Require(picker.IsDropDownOpen, "选择日期后的退出没有被真实触发器重新打开。");
            result.InputChecks["SelectionReopenRetainedSurface"] =
                openedCount == openedBeforeReopen && closedCount == closedBeforeReopen;
            Require(openedCount == openedBeforeReopen && closedCount == closedBeforeReopen,
                "选择日期后的重开已重建弹层，未覆盖同一表面的中途反向。");
            WaitFor(result, "selection-reopen-final", calendar, values, animated,
                value => isOpen() && Near(value, new[] { 1.0, 0.0 }, 0.000001), isOpen);
            Require((bool)Call("WindowValidationInput", "RaisePreviewKey", calendar, Key.Escape, ModifierKeys.None),
                "重新打开后 Esc 没有被生产控件处理。");
            WaitFor(result, "selection-escape-final", calendar, values, animated,
                value => !isOpen() && Near(value, new[] { 0.0, 4.0 }, 0.000001), isOpen);
            result.InputChecks["SelectedDateAfterReopenEscape"] = picker.SelectedDate?.ToString("yyyy-MM-dd");
            Require(picker.SelectedDate == selected, "重新打开后的 Esc 回滚到了上一轮打开前的日期。");
            result.InputChecks["DismissPolicyRestoredAfterClose"] = !popup.StaysOpen;
            Require(!popup.StaysOpen, "日历完成退出后未恢复外点关闭策略。");
        }
        finally
        {
            try
            {
                if (popup != null) Call("WindowValidationInput", "ClosePopupForCleanup", popup);
            }
            catch (Exception error) { result.Failures.Add("夹具清理失败：" + error.Message); }
            finally { pointerState?.Dispose(); window?.Close(); Pump(); }
        }
    }

    private static void Indicator(MotionScenarioResult result)
    {
        var root = new Grid { Width = 240, Height = 44 };
        var targets = new Canvas();
        var left = new Border { Width = 80, Height = 40, Background = Brushes.White };
        var right = new Border { Width = 100, Height = 40, Background = Brushes.White };
        Canvas.SetLeft(right, 120);
        targets.Children.Add(left); targets.Children.Add(right);
        var layer = new Canvas();
        var indicator = new Border { Width = 1, Height = 4, Background = Brushes.Pink };
        layer.Children.Add(indicator);
        root.Children.Add(targets); root.Children.Add(layer);
        var type = AppType("SegmentIndicator");
        var controller = Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance, null, new object[] { root, layer, indicator }, null);
        Window window = null;
        try
        {
            window = ShowFixture(root);
            Action<FrameworkElement> move = target => Invoke(type, controller, "MoveTo", target);
            Func<double[]> values = () => {
                var bounds = (Rect)Property(type, controller, "CurrentVisualBounds");
                return new[] { bounds.X, bounds.Width };
            };
            Func<bool> animated = () => ((TransformGroup)indicator.RenderTransform).Children.Any(value =>
                value.HasAnimatedProperties);
            move(left);
            WaitFor(result, "indicator-left", indicator, values, animated,
                value => Near(value, new[] { 0.0, 80.0 }, 0.00001));
            move(right);
            _application.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
            var immediate = Observe(result, "indicator-right-committed", values, animated);
            if (!result.Enabled)
            {
                Require(Immediate(immediate.Values, new[] { 120.0, 100.0 }, animated(), 0.00001),
                    "off 指示器未在生产提交后即时对齐。");
                return;
            }
            WaitFor(result, "indicator-middle", indicator, values, animated,
                value => Intermediate(new[] { 0.0, 80.0 }, value, new[] { 120.0, 100.0 }));
            var before = Observe(result, "indicator-reverse-before", values, animated);
            move(left);
            _application.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
            Observe(result, "indicator-reverse-committed", values, animated);
            var after = ObserveFrame(result, "indicator-reverse-first-frame", indicator, values, animated);
            Require(Continues(before, after, new[] { 0.0, 80.0 }, 160, 0.02),
                "指示器反转未沿当前几何继续。");
            WaitFor(result, "indicator-reverse-middle", indicator, values, animated,
                value => Intermediate(before.Values, value, new[] { 0.0, 80.0 }), firstFrame: after);
            move(right);
            WaitFor(result, "indicator-last-target", indicator, values, animated,
                value => Near(value, new[] { 120.0, 100.0 }, 0.00001));
            Require((bool)Invoke(type, controller, "IsAlignedTo", right, 0.75), "指示器最终目标错误。");
            Require(indicator.Width == 1 && indicator.Height == 4, "指示器动效改变了布局尺寸。");
        }
        finally { window?.Close(); Pump(); }
    }

    private static void Digit(MotionScenarioResult result)
    {
        var text = new TextBlock { Text = "59", FontSize = 26, Width = 80, Height = 42 };
        Window window = null;
        try
        {
            window = ShowFixture(text);
            Func<double[]> values = () => new[] { text.Opacity };
            Func<bool> animated = () => Animated(text, UIElement.OpacityProperty);
            Action pulse = () => Call("MotionBehavior", "FadeCountdownDigit", text);
            if (!result.Enabled)
            {
                // A real pre-existing WPF opacity clock must be removed too.
                text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.6, 1,
                    TimeSpan.FromMilliseconds(90)), HandoffBehavior.SnapshotAndReplace);
                text.Text = "58";
                pulse();
                var instant = Observe(result, "digit-off-immediate", values, animated);
                Require(text.Text == "58" && Immediate(instant.Values, new[] { 1.0 }, animated(), 0),
                    "off 秒位没有直接切换到无动画终态。");
                return;
            }
            pulse();
            Observe(result, "digit-first-committed", values, animated);
            WaitFor(result, "digit-middle", text, values, animated,
                value => Intermediate(new[] { 0.6 }, value, new[] { 1.0 }));
            var before = Observe(result, "digit-repeat-before", values, animated);
            text.Text = "58";
            pulse();
            var immediate = Observe(result, "digit-repeat-committed", values, animated);
            var after = ObserveFrame(result, "digit-repeat-first-frame", text, values, animated);
            Require(ContinuesFromSnapshot(before, immediate, after, new[] { 1.0 }, 90, 0.002),
                "秒位中断把 opacity 重置到 0.6 或终点。");
            WaitFor(result, "digit-repeat-middle", text, values, animated,
                value => Intermediate(before.Values, value, new[] { 1.0 }), firstFrame: after);
            WaitFor(result, "digit-final", text, values, animated,
                value => Near(value, new[] { 1.0 }, 0.000001));
        }
        finally { window?.Close(); Pump(); }
    }

    public static object Run(string assemblyPath)
    {
        InitializeApplication(assemblyPath);
        var systemBefore = SystemParameters.ClientAreaAnimation;
        var results = new List<MotionScenarioResult>();
        foreach (var enabled in new[] { false, true })
        {
            results.Add(RunCase("button-hover-press", enabled, Buttons));
            foreach (var category in new[] { "calendar", "combobox", "tags", "tooltip" })
                results.Add(RunCase("popup-" + category, enabled, result => ExercisePopupMotion(result, category)));
            results.Add(RunCase("segment-indicator", enabled, Indicator));
            results.Add(RunCase("countdown-digit", enabled, Digit));
        }
        results.Add(RunCase("datepicker-text-interrupted-input", true,
            result => DatePickerInterruptedInput(result, true)));
        results.Add(RunCase("datepicker-arrow-interrupted-input", true,
            result => DatePickerInterruptedInput(result, false)));
        results.Add(RunCase("override-restoration-after-exception", false,
            result => { throw new ExpectedFixtureException(); }, true));
        var before = new MotionObservation { ElapsedMilliseconds = 10, Values = new[] { 0.65 } };
        var committed = new MotionObservation { ElapsedMilliseconds = 10.2, Values = new[] { 0.65 } };
        var firstFrame = new MotionObservation {
            ElapsedMilliseconds = 10.8, Values = new[] { 0.75 }, HasAnimation = true };
        var snapshotClockAdvanceAccepted = ContinuesFromSnapshot(
            new MotionObservation { ElapsedMilliseconds = 57.582, Values = new[] { 0.8365065194954957 } },
            new MotionObservation { ElapsedMilliseconds = 57.7731, Values = new[] { 0.8365065194954957 } },
            new MotionObservation {
                ElapsedMilliseconds = 58.3934, Values = new[] { 0.8814214647463753 }, HasAnimation = true },
            new[] { 1.0 }, 90, 0.002);
        var negatives = new Dictionary<string, bool> {
            ["missing-intermediate"] = !Intermediate(new[] { 0.0 }, new[] { 1.0 }, new[] { 1.0 }),
            ["nonfinite-intermediate"] = !Intermediate(new[] { 0.0 }, new[] { double.NaN }, new[] { 1.0 }),
            ["restart-from-origin"] = !Continues(before,
                new MotionObservation { ElapsedMilliseconds = 11, Values = new[] { 0.0 } },
                new[] { 1.0 }, 90, 0.002),
            ["reverse-jumps-to-target"] = !Continues(before,
                new MotionObservation { ElapsedMilliseconds = 11, Values = new[] { 0.0 } },
                new[] { 0.0 }, 90, 0.002),
            ["whole-transition-missed"] = !Continues(before,
                new MotionObservation { ElapsedMilliseconds = 110, Values = new[] { 1.0 } },
                new[] { 1.0 }, 90, 0.002),
            ["snapshot-commit-restarts-at-origin"] = !ContinuesFromSnapshot(before,
                new MotionObservation { ElapsedMilliseconds = 10.2, Values = new[] { 0.0 } },
                firstFrame, new[] { 1.0 }, 90, 0.002),
            ["snapshot-commit-jumps-to-target"] = !ContinuesFromSnapshot(before,
                new MotionObservation { ElapsedMilliseconds = 10.2, Values = new[] { 1.0 } },
                firstFrame, new[] { 1.0 }, 90, 0.002),
            ["snapshot-frame-restarts-at-origin"] = !ContinuesFromSnapshot(before, committed,
                new MotionObservation { ElapsedMilliseconds = 11, Values = new[] { 0.0 }, HasAnimation = true },
                new[] { 1.0 }, 90, 0.002),
            ["snapshot-frame-jumps-to-target"] = !ContinuesFromSnapshot(before, committed,
                new MotionObservation { ElapsedMilliseconds = 11, Values = new[] { 1.0 }, HasAnimation = true },
                new[] { 1.0 }, 90, 0.002),
            ["snapshot-frame-nonfinite"] = !ContinuesFromSnapshot(before, committed,
                new MotionObservation { ElapsedMilliseconds = 11, Values = new[] { double.NaN }, HasAnimation = true },
                new[] { 1.0 }, 90, 0.002),
            ["snapshot-whole-transition-missed"] = !ContinuesFromSnapshot(before, committed,
                new MotionObservation { ElapsedMilliseconds = 110, Values = new[] { 0.75 }, HasAnimation = true },
                new[] { 1.0 }, 90, 0.002),
            ["off-retains-clock"] = !Immediate(new[] { 1.0 }, new[] { 1.0 }, true, 0),
            ["off-wrong-target"] = !Immediate(new[] { 0.6 }, new[] { 1.0 }, false, 0)
        };
        var startupAbsent = ProductionServicesAbsent();
        var systemUnchanged = SystemParameters.ClientAreaAnimation == systemBefore;
        return new {
            Scope = "production-WPF-rendering-events-with-fixture-inputs",
            SystemPreferenceBefore = systemBefore,
            SystemPreferenceAfter = SystemParameters.ClientAreaAnimation,
            ProductionStartupStayedInactive = startupAbsent,
            ApplicationExitNotInvoked = !_applicationExitObserved,
            DeadlineMilliseconds,
            Results = results,
            SnapshotClockAdvanceAccepted = snapshotClockAdvanceAccepted,
            NegativeControls = negatives,
            Passed = startupAbsent && !_applicationExitObserved && systemUnchanged && results.All(result => result.Passed) &&
                snapshotClockAdvanceAccepted && negatives.Values.All(value => value)
        };
    }
}
'@

$results = @(
    foreach ($scenario in @('real-close', 'real-close-motion', 'handled-without-close', 'close-after-deadline')) {
        [PopupValidationContractHarness]::Run($resolvedAssembly, $scenario)
    }
)
$failures = [Collections.Generic.List[string]]::new()
$tooltipCollectibility = [PopupValidationContractHarness]::CheckToolTipCollectibility($resolvedAssembly)
if (-not ($tooltipCollectibility.PlainCollected -and $tooltipCollectibility.AnimatedCollected)) {
    $failures.Add('临时 ToolTip 及其动效监听器应可回收；普通 ToolTip 是 GC 正向对照。')
}
$closedPopupResults = @(
    foreach ($previouslyOpened in @($false, $true)) {
        foreach ($escape in @($true, $false)) {
            [PopupValidationContractHarness]::CheckClosedPopupClose($resolvedAssembly, $previouslyOpened, $escape)
        }
    }
)
foreach ($closedPopup in $closedPopupResults) {
    if (-not ($closedPopup.PrerequisiteClosed -and $closedPopup.RemainedClosed -and $closedPopup.FinalClosed) -or
        $closedPopup.OpenedEvents -ne 0) {
        $failures.Add("关闭态 Popup 不应被 $($closedPopup.Action) 闪开；previouslyOpened=$($closedPopup.PreviouslyOpened)。")
    }
}
$captureComposite = [PopupValidationContractHarness]::CheckCaptureComposite($resolvedAssembly)
if (-not ($captureComposite.StraightAlphaMatches -and $captureComposite.PremultipliedInputMatches -and
          $captureComposite.HalfWhiteOverWhiteStaysWhite -and $captureComposite.OpaqueSourcePreserved -and
          $captureComposite.TransparentFrameRejected)) {
    $failures.Add('参考帧一像素透明合成或全透明拒绝合同未通过。')
}
$motion = [MotionValidationContractHarness]::Run($resolvedAssembly)
if (-not $motion.Passed) {
    $failures.Add('生产 WPF 动效模式、连续中断、负例或作用域恢复未通过；详见 Motion.Results。')
}
foreach ($result in $results) {
    if ($result.HandledEscapeCount -lt 1) {
        $failures.Add("$($result.Scenario)：未实际走到已处理的 Esc 事件。")
    }
    if (-not $result.CleanedAfterExercise) {
        $failures.Add("$($result.Scenario)：验证方法 finally 未清理弹层。")
    }
    if ($result.Scenario -eq 'real-close-motion' -and -not $result.ReopenedWithInitialFocus) {
        $failures.Add('带动画的弹层清理后重开未恢复初始键盘焦点。')
    }
    if ($result.Scenario -in @('real-close', 'real-close-motion')) {
        if (-not ($result.EscapeReturnsFocus -and $result.EscapeFromTriggerClosesAndKeepsFocus -and
                  $result.TriggerSecondClickCloses -and $result.OutsideFocusPreserved)) {
            $failures.Add('真实 PopupFocusLifecycle 关闭和焦点回归未通过。')
        }
    }
    elseif ($result.EscapeReturnsFocus) {
        $failures.Add("$($result.Scenario)：Esc 未按时关闭，却被验证器或后续清理误判为通过。")
    }
    if ($result.Scenario -eq 'handled-without-close' -and $result.EscapeFromTriggerClosesAndKeepsFocus) {
        $failures.Add('触发按钮的 Esc 仅设 Handled=true 而不关闭，被错误接受。')
    }
}

$trayMenu = $null
try {
    & (Join-Path $PSScriptRoot 'Test-CountdownTrayMenu.ps1') -AssemblyPath $resolvedAssembly |
        ForEach-Object { $trayMenu = $_ }
}
catch {
    $failures.Add($_.Exception.Message)
}
if ($null -eq $trayMenu) {
    $failures.Add('托盘菜单验证未返回可回读的报告。')
}

[pscustomobject]@{
    Suite = 'PopupValidationContract'
    AssemblyPath = $resolvedAssembly
    FixtureDependencies = $fixtureDependencies
    AssemblySHA256 = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash
    HarnessSHA256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    Results = $results
    CaptureComposite = $captureComposite
    ClosedPopupResults = $closedPopupResults
    ToolTipCollectibility = $tooltipCollectibility
    Motion = $motion
    TrayMenu = $trayMenu
    Failures = @($failures)
    Passed = $failures.Count -eq 0
} | ConvertTo-Json -Depth 10
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}
