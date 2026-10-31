using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MinminCountdown;

internal static class WindowValidationInput
{
    public static bool ClickForFocusValidation(Control target)
    {
        target.BringIntoView();
        Pump(target.Dispatcher);
        using var pointer = new PickerPointerState();
        var pressed = RaisePickerPointer(target, UIElement.PreviewMouseLeftButtonDownEvent);
        Pump(target.Dispatcher);
        var focusedOnPress = target.IsKeyboardFocusWithin;
        var released = RaisePickerPointer(target, UIElement.PreviewMouseLeftButtonUpEvent);
        Pump(target.Dispatcher);
        return pressed && released && focusedOnPress;
    }

    // Hygiene bound for native popup HWND teardown only. The logical close keeps
    // the interactive 300 ms budget; the compositor-synchronized hide that trails
    // it is environment-dominated and must not race that budget.
    private const int TeardownTimeoutMilliseconds = 1000;

    public static bool WaitUntil(Dispatcher dispatcher, Func<bool> condition, int timeoutMilliseconds = 300)
    {
        dispatcher.VerifyAccess();
        if (timeoutMilliseconds <= 0)
        {
            return condition();
        }

        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var observed = false;
        Exception? failure = null;
        var frameRequestCount = 0;
        var renderingCount = 0;
        var observedMilliseconds = -1d;
        var operationStarts = new Dictionary<DispatcherOperation, double>();
        var longestOperationMilliseconds = 0d;
        var longestOperationStartedMilliseconds = 0d;
        var longestOperationPriority = DispatcherPriority.Inactive;
        void OnOperationStarted(object? sender, DispatcherHookEventArgs args) =>
            operationStarts[args.Operation] = watch.Elapsed.TotalMilliseconds;
        void OnOperationCompleted(object? sender, DispatcherHookEventArgs args)
        {
            if (!operationStarts.Remove(args.Operation, out var started)) return;
            var elapsed = watch.Elapsed.TotalMilliseconds - started;
            if (elapsed <= longestOperationMilliseconds) return;
            longestOperationMilliseconds = elapsed;
            longestOperationStartedMilliseconds = started;
            longestOperationPriority = args.Operation.Priority;
        }
        var timeout = new DispatcherTimer(DispatcherPriority.Send, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(timeoutMilliseconds)
        };
        // Observe at the same Send priority as the deadline: Background ticks
        // can starve behind a long Normal-priority popup teardown and never
        // witness a condition that became true inside the window.
        var frameRequests = new DispatcherTimer(DispatcherPriority.Send, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        void Observe(bool final = false)
        {
            // A tick executing at or past the deadline is the last observation
            // opportunity, so check the condition instead of stopping blind;
            // a genuinely late condition still fails that final check.
            if (!final && watch.Elapsed.TotalMilliseconds > timeoutMilliseconds)
            {
                Observe(final: true);
                frame.Continue = false;
                return;
            }

            try
            {
                if (condition())
                {
                    observed = true;
                    observedMilliseconds = watch.Elapsed.TotalMilliseconds;
                    frame.Continue = false;
                }
            }
            catch (Exception error)
            {
                failure = error;
                frame.Continue = false;
            }
        }

        void OnRendering(object? sender, EventArgs args)
        {
            // Keep the observer for this bounded wait. Removing it inside the
            // last animation tick makes MediaContext leave presentation and
            // synchronously wait before queued native popup cleanup can run.
            renderingCount++;
            Observe();
        }
        EventHandler rendering = OnRendering;
        EventHandler requestFrame = (_, _) =>
        {
            // Also observe between frames: WPF defers popup HWND destruction
            // until after IsOpen=false, and it need not produce another frame.
            Observe();
            if (!frame.Continue) return;

            // Adding Rendering posts a real WPF render request. Invalidations
            // can merge on the isolated desktop; a bitmap does not tick clocks.
            frameRequestCount++;
            CompositionTarget.Rendering -= rendering;
            CompositionTarget.Rendering += rendering;
        };
        EventHandler expired = (_, _) =>
        {
            Observe(final: true);
            frame.Continue = false;
        };
        try
        {
            dispatcher.Hooks.OperationStarted += OnOperationStarted;
            dispatcher.Hooks.OperationCompleted += OnOperationCompleted;
            dispatcher.Hooks.OperationAborted += OnOperationCompleted;
            timeout.Tick += expired;
            frameRequests.Tick += requestFrame;
            timeout.Start();
            frameRequests.Start();
            requestFrame(null, EventArgs.Empty);
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            dispatcher.Hooks.OperationStarted -= OnOperationStarted;
            dispatcher.Hooks.OperationCompleted -= OnOperationCompleted;
            dispatcher.Hooks.OperationAborted -= OnOperationCompleted;
            timeout.Stop();
            timeout.Tick -= expired;
            frameRequests.Stop();
            frameRequests.Tick -= requestFrame;
            CompositionTarget.Rendering -= rendering;
        }

        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        // The Send-priority deadline tick is the operative deadline: when
        // dispatcher starvation delays observers, the final check at that tick
        // decides, and observedMilliseconds keeps the honest witness time.
        var passed = observed;
        if (!passed)
        {
            Console.Error.WriteLine("VALIDATION_RENDER_WAIT_DIAGNOSTIC " + System.Text.Json.JsonSerializer.Serialize(new
            {
                timeoutMilliseconds, observed, observedMilliseconds,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                frameRequestCount, renderingCount,
                longestOperationMilliseconds, longestOperationStartedMilliseconds,
                longestOperationPriority = longestOperationPriority.ToString()
            }));
        }
        return passed;
    }

    // 推进真实 WPF 渲染帧：每次 CompositionTarget.Rendering 回调调用一次 onFrame，返回 true 即停；
    // 超时之后的回调和 Send 优先级的超时计时器也会停。订阅 Rendering 本身会请求一帧，位图捕获不推进
    // 动画时钟，隔离桌面上的失效请求又可能被合并，所以每 10ms 重订一次。onFrame 抛出的异常在清理后原样
    // 抛回。Completed 表示有一帧在超时内让 onFrame 返回 true；ElapsedMilliseconds 是泵结束时的耗时。
    public static (bool Completed, double ElapsedMilliseconds) PumpRenderedFrames(
        Dispatcher dispatcher, int timeoutMilliseconds, Func<bool> onFrame)
    {
        dispatcher.VerifyAccess();
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var completed = false;
        Exception? failure = null;
        var timeout = new DispatcherTimer(DispatcherPriority.Send, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(timeoutMilliseconds)
        };
        var frameRequests = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        void OnRendering(object? sender, EventArgs args)
        {
            if (watch.Elapsed.TotalMilliseconds > timeoutMilliseconds)
            {
                frame.Continue = false;
                return;
            }

            try
            {
                if (onFrame())
                {
                    completed = true;
                    frame.Continue = false;
                }
            }
            catch (Exception error) { failure = error; frame.Continue = false; }
        }
        EventHandler rendering = OnRendering;
        EventHandler requestFrame = (_, _) =>
        {
            CompositionTarget.Rendering -= rendering;
            CompositionTarget.Rendering += rendering;
        };
        EventHandler expired = (_, _) => frame.Continue = false;
        try
        {
            timeout.Tick += expired;
            frameRequests.Tick += requestFrame;
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
        }

        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return (completed, watch.Elapsed.TotalMilliseconds);
    }

    // #205 regression: raising Click alone bypasses ButtonBase.OnClick and its
    // command source. UIA Invoke follows the same command path as a real click.
    public static bool InvokeButton(Button button)
    {
        if (UIElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke)
            is not IInvokeProvider provider)
        {
            return false;
        }

        provider.Invoke();
        return true;
    }

    /// <summary>
    /// Waits for the popup's own close path to complete without changing its state.
    /// Callers use this only as an observation before an assertion; fixture cleanup
    /// belongs in <c>ClosePopupForCleanup</c> from setup/finally blocks.
    /// </summary>
    public static bool WaitForPopupClosed(Popup popup, int timeoutMilliseconds = 300)
    {
        // The interactive contract is the logical close. Native HWND teardown
        // trails it: Popup's hide synchronizes with the compositor, and on the
        // isolated BelowNormal desktop that sync alone can approach the
        // interactive budget. Resource reclamation therefore gets its own
        // hygiene bound, which still fails a genuinely stuck teardown (the
        // close-after-deadline fixture never reaches it).
        var logicallyClosed = WaitUntil(popup.Dispatcher, () => !popup.IsOpen, timeoutMilliseconds);
        var detached = logicallyClosed && WaitUntil(popup.Dispatcher, IsSourceDetached, TeardownTimeoutMilliseconds);
        var closed = logicallyClosed && detached;
        if (!closed)
        {
            Console.Error.WriteLine("POPUP_CLOSE_WAIT_DIAGNOSTIC " + System.Text.Json.JsonSerializer.Serialize(new
            {
                popup.IsOpen,
                sourceAttached = popup.Child is Visual child && PresentationSource.FromVisual(child) is not null,
                childType = popup.Child?.GetType().Name,
                transition = PopupMotionBehavior.DescribeTransitionForValidation(popup)
            }));
        }
        return closed;

        bool IsSourceDetached() =>
            popup.Child is not Visual visual || PresentationSource.FromVisual(visual) is null;
    }

    /// <summary>
    /// Restores a popup fixture after a scenario. This is deliberately separate
    /// from <see cref="WaitForPopupClosed"/> so cleanup cannot turn a failed
    /// close assertion into a passing result.
    /// </summary>
    public static void ClosePopupForCleanup(Popup popup)
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
        }

        if (!WaitForPopupClosed(popup))
        {
            throw new InvalidOperationException(
                "Popup fixture did not finish closing within its close budget (300 ms interactive + teardown).");
        }
    }

    public static void Pump(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(
            DispatcherPriority.SystemIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    // 键盘焦点只画一套（DESIGN.md §4 常驻焦点环），判定读实际获焦的元素。模板不画焦点的停靠点（列表滚动区、
    // 状态文字）只认统一的 2px 暖边装饰层 MainKeyboardFocusVisual；系统默认虚线框、模板焦点上再挂装饰层都不算。
    // 其余停靠点不挂装饰层：按钮、分段、勾选与单选的焦点环要 FocusVisualState 已点亮；文本框、下拉框与日期框
    // 获焦时由模板换成 2px 输入面边界。日期框的 Tab 焦点落在模板里的打开日历按钮上，描边画在包住它的日期框上，
    // 按钮自己的焦点环再亮就叠成两层。
    public static bool KeyboardFocusDrawnOnce(DependencyObject focused)
    {
        if (focused is not FrameworkElement element)
        {
            return false;
        }

        if (element.FocusVisualStyle is { } focusVisualStyle)
        {
            return ReferenceEquals(focusVisualStyle, element.TryFindResource("MainKeyboardFocusVisual")) &&
                !(element is Control styledControl && FocusVisualState.DrawsOwnKeyboardFocus(styledControl));
        }

        // 悬浮窗的金额文字不是 Control，焦点环是旁边的 FloatingIncomeFocusRing，跟随它自己维护的 IsKeyboardFocusVisible。
        if (element is AutomationOptionalTextBlock)
        {
            return FocusVisualState.GetIsKeyboardFocusVisible(element);
        }

        if (element is not Control control)
        {
            return false;
        }

        Control? inputHost = null;
        for (DependencyObject? node = control; node is not null && inputHost is null; node = VisualTreeHelper.GetParent(node))
        {
            inputHost = node as DatePicker ?? (Control?)(node as ComboBox);
        }

        inputHost ??= control as TextBox;
        return inputHost is not null
            ? TemplateDrawsFocusStroke(inputHost) &&
                (ReferenceEquals(inputHost, control) || !TemplatePartsDrawFocusStroke(control, "FocusRing"))
            : FocusVisualState.GetIsKeyboardFocusVisible(control) && TemplateDrawsFocusStroke(control);
    }

    // 模板里画焦点的部件（常驻焦点环 FocusRing、勾选框 Box、输入面 Surface、日期框 PART_TextBox）至少一个可见，
    // 而且边宽不小于 2px。
    public static bool TemplateDrawsFocusStroke(Control control) =>
        TemplatePartsDrawFocusStroke(control, "FocusRing", "Box", "Surface", "PART_TextBox");

    private static bool TemplatePartsDrawFocusStroke(Control control, params string[] partNames)
    {
        if (control.Template is null)
        {
            return false;
        }

        control.ApplyTemplate();
        return partNames
            .Select(name => control.Template.FindName(name, control))
            .Any(part => part switch
            {
                Border border => IsFocusStroke(border.IsVisible, border.BorderBrush, border.BorderThickness),
                Control inner => IsFocusStroke(inner.IsVisible, inner.BorderBrush, inner.BorderThickness),
                _ => false
            });

        static bool IsFocusStroke(bool visible, Brush? brush, Thickness thickness) =>
            visible &&
            brush is not (null or SolidColorBrush { Color.A: 0 }) &&
            new[] { thickness.Left, thickness.Top, thickness.Right, thickness.Bottom }.Max() >= 2;
    }

    // 激活窗口后从第一个 Tab 停靠点出发走一圈，见 KeyboardFocusFailuresAlongTabCycle。
    public static IReadOnlyList<string> KeyboardFocusFailuresInWindow(Window window)
    {
        window.Activate();
        Pump(window.Dispatcher);
        window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Pump(window.Dispatcher);
        return Keyboard.FocusedElement is UIElement first && !ReferenceEquals(first, window)
            ? KeyboardFocusFailuresAlongTabCycle(first)
            : ["窗口里没有可 Tab 到的控件"];
    }

    // 从起点沿 Tab 顺序走一圈（窗口与弹层的 Tab 都循环），记下不满足 KeyboardFocusDrawnOnce 的停靠点；
    // 走不回起点或停在原地也记一条。焦点由程序移交，FocusVisualState 照样点亮，与真实按 Tab 一致。
    public static IReadOnlyList<string> KeyboardFocusFailuresAlongTabCycle(UIElement start, int maxStops = 80)
    {
        Keyboard.Focus(start);
        Pump(start.Dispatcher);
        if (Keyboard.FocusedElement is not UIElement first)
        {
            return ["起点拿不到键盘焦点"];
        }

        var failures = new List<string>();
        var current = first;
        for (var stop = 0; stop < maxStops; stop++)
        {
            if (!KeyboardFocusDrawnOnce(current))
            {
                failures.Add(DescribeFocusStop(current));
            }

            current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Pump(start.Dispatcher);
            if (Keyboard.FocusedElement is not UIElement next)
            {
                failures.Add($"Tab 离开 {DescribeFocusStop(current)} 后焦点落空");
                return failures;
            }

            if (ReferenceEquals(next, first))
            {
                return failures;
            }

            if (ReferenceEquals(next, current))
            {
                failures.Add($"Tab 停在 {DescribeFocusStop(current)}");
                return failures;
            }

            current = next;
        }

        failures.Add($"Tab 走了 {maxStops} 步没有回到起点");
        return failures;
    }

    private static string DescribeFocusStop(UIElement element) =>
        $"{element.GetType().Name}「{AutomationProperties.GetName(element)}」" +
        (element is FrameworkElement { FocusVisualStyle: not null } ? "挂着装饰层" : string.Empty) +
        (element is Control control && !FocusVisualState.GetIsKeyboardFocusVisible(control) ? "焦点环未点亮" : string.Empty);

    // #239/#240：两窗剩余时间分格里秒位的数值文字。分格是「数值格（Border 包 TextBlock）＋单位」
    // 两个子元素的 StackPanel，单位为「秒」的那一对即秒位。
    public static TextBlock[] RemainingSecondsValues(DependencyObject root)
    {
        var values = new List<TextBlock>();
        Collect(root);
        return values.ToArray();

        void Collect(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is StackPanel { Children.Count: 2 } pair &&
                    pair.Children[0] is Border { Child: TextBlock value } &&
                    pair.Children[1] is TextBlock { Text: "秒" })
                {
                    values.Add(value);
                }

                Collect(child);
            }
        }
    }

    public static PopupFocusExerciseResult ExercisePopup(
        Popup popup,
        ButtonBase trigger,
        IInputElement initialFocus,
        UIElement outsideFocus)
    {
        var escapeReturnsFocus = false;
        var outsideFocusPreserved = false;
        var triggerSecondClickCloses = false;
        var escapeFromTriggerClosesAndKeepsFocus = false;
        var popupOpened = false;
        var focusEntered = false;
        var escapeHandled = false;
        var popupClosedAfterEscape = false;
        var triggerFocused = false;
        try
        {
            ClosePopupForCleanup(popup);
            Window.GetWindow(trigger)?.Activate();
            Keyboard.Focus(trigger);
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            popupOpened = popup.IsOpen;
            focusEntered = OwnsKeyboardFocus(initialFocus);
            escapeHandled = initialFocus is UIElement input &&
                RaisePreviewKey(input, Key.Escape);
            Pump(popup.Dispatcher);
            popupClosedAfterEscape = WaitForPopupClosed(popup);
            triggerFocused = trigger.IsKeyboardFocusWithin ||
                (Window.GetWindow(trigger) is { } triggerWindow &&
                 ReferenceEquals(FocusManager.GetFocusedElement(triggerWindow), trigger));
            escapeReturnsFocus =
                popupOpened &&
                focusEntered &&
                escapeHandled &&
                popupClosedAfterEscape &&
                triggerFocused;

            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            var openedForSecondClick = popup.IsOpen;
            Keyboard.Focus(trigger);
            Pump(popup.Dispatcher);
            trigger.RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                Environment.TickCount,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
            });
            Pump(popup.Dispatcher);
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            var closeBefore = PopupMotionBehavior.DescribeTransitionForValidation(popup);
            triggerSecondClickCloses = openedForSecondClick && WaitForPopupClosed(popup);
            if (!triggerSecondClickCloses)
            {
                Console.Error.WriteLine($"POPUP_SECOND_CLICK_DIAGNOSTIC trigger={trigger.Name}, opened={openedForSecondClick}, before={closeBefore}, after={PopupMotionBehavior.DescribeTransitionForValidation(popup)}");
            }

            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            var openedForTriggerEscape = popup.IsOpen;
            Keyboard.Focus(trigger);
            Pump(popup.Dispatcher);
            var triggerEscapeHandled = RaisePreviewKey(trigger, Key.Escape);
            Pump(popup.Dispatcher);
            var triggerEscapeClosed = WaitForPopupClosed(popup);
            escapeFromTriggerClosesAndKeepsFocus =
                openedForTriggerEscape &&
                triggerEscapeHandled &&
                triggerEscapeClosed &&
                (trigger.IsKeyboardFocusWithin ||
                 (Window.GetWindow(trigger) is { } triggerWindowAfterEscape &&
                  ReferenceEquals(FocusManager.GetFocusedElement(triggerWindowAfterEscape), trigger)));

            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(popup.Dispatcher);
            Keyboard.Focus(outsideFocus);
            Pump(popup.Dispatcher);
            outsideFocusPreserved =
                popup.IsOpen && outsideFocus.IsKeyboardFocusWithin;
        }
        finally
        {
            ClosePopupForCleanup(popup);
        }

        return new PopupFocusExerciseResult(
            escapeReturnsFocus,
            outsideFocusPreserved,
            triggerSecondClickCloses,
            escapeFromTriggerClosesAndKeepsFocus);
    }

    public static bool ExerciseDatePicker(DatePicker picker) => ExerciseDatePicker(picker, out _);

    public static bool ExerciseDatePicker(DatePicker picker, out string diagnostic)
    {
        diagnostic = "not-exercised";
        var owner = Window.GetWindow(picker);
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            ClosePickerForFixture(picker);
            picker.ApplyTemplate();
            var triggerButton = picker.Template.FindName("PART_Button", picker) as Button;
            owner?.Activate();
            owner?.Focus();
            triggerButton?.Focus();
            Pump(picker.Dispatcher);
            picker.IsDropDownOpen = true;
            Pump(picker.Dispatcher);
            picker.ApplyTemplate();
            var calendar = PickerTemplateParts.GetCalendar(picker);
            var focusedVisual = Keyboard.FocusedElement as DependencyObject;
            var focusedWithinCalendarVisual =
                calendar is not null &&
                focusedVisual is not null &&
                (ReferenceEquals(focusedVisual, calendar) || calendar.IsAncestorOf(focusedVisual));
            var focusEntered = calendar?.IsKeyboardFocusWithin == true || focusedWithinCalendarVisual;
            var escapeHandled = calendar is not null &&
                RaisePreviewKey(calendar, Key.Escape);
            var popupClosed = picker.Template.FindName("PART_Popup", picker) is Popup popup &&
                WaitForPopupClosed(popup);
            Pump(picker.Dispatcher);
            picker.ApplyTemplate();
            triggerButton = picker.Template.FindName("PART_Button", picker) as Button;
            var focusReturned = triggerButton?.IsKeyboardFocusWithin == true ||
                ReferenceEquals(Keyboard.FocusedElement, triggerButton) ||
                (owner is not null &&
                 ReferenceEquals(FocusManager.GetFocusedElement(owner), triggerButton));
            var works =
                focusEntered &&
                escapeHandled &&
                popupClosed &&
                !picker.IsDropDownOpen &&
                focusReturned &&
                picker.CalendarStyle is not null &&
                ReferenceEquals(calendar?.Style, picker.CalendarStyle);
            diagnostic = $"FocusEntered={focusEntered}; EscapeHandled={escapeHandled}; " +
                $"Closed={!picker.IsDropDownOpen}; PopupClosed={popupClosed}; FocusReturned={focusReturned}; " +
                $"CalendarStyleMatches={ReferenceEquals(calendar?.Style, picker.CalendarStyle)}; " +
                $"TriggerFocusable={triggerButton?.Focusable}; FocusedType={Keyboard.FocusedElement?.GetType().Name}";
            return works;
        }
        finally
        {
            ClosePickerForFixture(picker);
            Pump(picker.Dispatcher);
        }
    }

    public static DatePickerPointerExerciseResult ExerciseDatePickerTextPointer(DatePicker picker)
    {
        using var pointerState = new PickerPointerState();
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            ClosePickerForFixture(picker);
            picker.ApplyTemplate();
            var textBox = picker.Template.FindName("PART_TextBox", picker) as DatePickerTextBox;
            var textToggle = PickerTemplateParts.GetTextToggle(picker);
            var textSurface = textToggle?.IsVisible == true ? (UIElement)textToggle : textBox;
            if (textBox is null ||
                textSurface is null ||
                PresentationSource.FromVisual(textSurface) is null)
            {
                return new DatePickerPointerExerciseResult(
                    false, false, false, false, false, false, false);
            }

            RaiseDateTextPointer(
                picker,
                textSurface,
                UIElement.PreviewMouseLeftButtonDownEvent);
            Pump(picker.Dispatcher);
            var pressDidNotOpen = !picker.IsDropDownOpen;
            var releaseHandled = RaiseDateTextPointer(
                picker,
                textSurface,
                UIElement.PreviewMouseLeftButtonUpEvent);
            Pump(picker.Dispatcher);
            var calendar = PickerTemplateParts.GetCalendar(picker);
            var releaseOpened = picker.IsDropDownOpen;
            var themedPopupOpened =
                releaseOpened &&
                calendar?.Template is not null &&
                ReferenceEquals(calendar.Style, picker.CalendarStyle);
            return new DatePickerPointerExerciseResult(
                true,
                true,
                pressDidNotOpen,
                true,
                releaseOpened,
                releaseHandled,
                themedPopupOpened);
        }
        finally
        {
            ClosePickerForFixture(picker);
            Pump(picker.Dispatcher);
        }
    }

    public static bool ExerciseDatePickerButton(DatePicker picker)
    {
        try
        {
            picker.BringIntoView();
            Pump(picker.Dispatcher);
            ClosePickerForFixture(picker);
            picker.ApplyTemplate();
            if (picker.Template.FindName("PART_Button", picker) is not Button button ||
                PresentationSource.FromVisual(button) is null)
            {
                return false;
            }

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(picker.Dispatcher);
            return picker.IsDropDownOpen;
        }
        finally
        {
            ClosePickerForFixture(picker);
            Pump(picker.Dispatcher);
        }
    }

    public static bool ExerciseDatePickerSelectedDateText(DatePicker picker)
    {
        var original = picker.SelectedDate;
        try
        {
            // Independent literals include the widest date and a leap day.
            foreach (var (date, expected) in new (DateTime? Date, string Text)[]
            {
                (new DateTime(2031, 12, 31), "2031 年 12 月 31 日"),
                (new DateTime(2032, 2, 29), "2032 年 2 月 29 日"),
                (null, string.Empty)
            })
            {
                picker.SetCurrentValue(DatePicker.SelectedDateProperty, date);
                Pump(picker.Dispatcher);
                picker.ApplyTemplate();
                if (picker.Template.FindName("PART_TextBox", picker) is not DatePickerTextBox text ||
                    text.Text != expected || !text.IsReadOnly || text.Focusable)
                {
                    return false;
                }

                picker.IsDropDownOpen = true;
                Pump(picker.Dispatcher);
                ClosePickerForFixture(picker);
                Pump(picker.Dispatcher);
                if (text.Text != expected)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            ClosePickerForFixture(picker);
            picker.SetCurrentValue(DatePicker.SelectedDateProperty, original);
            Pump(picker.Dispatcher);
        }
    }

    public static bool RaisePreviewKey(
        UIElement target,
        Key key,
        ModifierKeys modifiers = ModifierKeys.None)
    {
        var source = PresentationSource.FromVisual(target);
        if (source is null)
        {
            return false;
        }

        var eventArgs = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        using (MainShortcutInput.InjectModifiers(eventArgs, modifiers))
        {
            target.RaiseEvent(eventArgs);
        }
        return eventArgs.Handled;
    }

    public static bool RaiseKey(UIElement target, Key key) =>
        RaiseKey(target, key, ModifierKeys.None);

    public static bool RaiseKey(UIElement target, Key key, ModifierKeys modifiers)
    {
        var source = PresentationSource.FromVisual(target);
        if (source is null)
        {
            return false;
        }

        var keyDown = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        var keyUp = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.KeyUpEvent
        };
        if (modifiers == ModifierKeys.None)
        {
            target.RaiseEvent(keyDown);
            target.RaiseEvent(keyUp);
        }
        else
        {
            using (MainShortcutInput.InjectModifiers(keyDown, modifiers))
            {
                target.RaiseEvent(keyDown);
            }

            using (MainShortcutInput.InjectModifiers(keyUp, modifiers))
            {
                target.RaiseEvent(keyUp);
            }
        }

        return keyDown.Handled && keyUp.Handled;
    }

    public static ComboBoxPointerExerciseResult ExerciseComboBox(ComboBox comboBox, Popup? containingPopup = null)
    {
        using var pointerState = new PickerPointerState();
        var attached = PickerTemplateParts.HasComboBoxContract(comboBox);
        var arrowColumnUnchanged = PickerTemplateParts.HasUnchangedArrowColumn(comboBox);
        var presenter = PickerTemplateParts.GetSelectionPresenter(comboBox);
        var textToggle = PickerTemplateParts.GetTextToggle(comboBox);
        var arrow = PickerTemplateParts.GetArrowToggle(comboBox);
        if (presenter is null || textToggle is null || PresentationSource.FromVisual(comboBox) is null)
        {
            return new ComboBoxPointerExerciseResult(
                false, false, false, false, false, false, false, false, false, false, false, false,
                attached,
                arrowColumnUnchanged,
                false);
        }

        comboBox.BringIntoView();
        Pump(comboBox.Dispatcher);
        var originalEnabled = comboBox.IsEnabled;
        var originalIndex = comboBox.SelectedIndex;
        try
        {
            comboBox.IsEnabled = true;
            ClosePickerForFixture(comboBox, containingPopup);
            Pump(comboBox.Dispatcher);
            RaiseComboTextPointer(comboBox, textToggle, UIElement.PreviewMouseLeftButtonDownEvent);
            Pump(comboBox.Dispatcher);
            var pressDidNotOpen = !comboBox.IsDropDownOpen;
            var releaseHandled = RaiseComboTextPointer(
                comboBox,
                textToggle,
                UIElement.PreviewMouseLeftButtonUpEvent);
            Pump(comboBox.Dispatcher);
            var releaseOpened = comboBox.IsDropDownOpen;

            ClosePickerForFixture(comboBox, containingPopup);
            Pump(comboBox.Dispatcher);
            var arrowPressOpens = false;
            if (arrow is not null && PresentationSource.FromVisual(arrow) is not null)
            {
                RaisePickerPointer(arrow, UIElement.PreviewMouseLeftButtonDownEvent);
                Pump(comboBox.Dispatcher);
                arrowPressOpens = comboBox.IsDropDownOpen;
                RaisePickerPointer(arrow, UIElement.PreviewMouseLeftButtonUpEvent);
                Pump(comboBox.Dispatcher);
            }

            ClosePickerForFixture(comboBox, containingPopup);
            Pump(comboBox.Dispatcher);
            Keyboard.Focus(comboBox);
            Pump(comboBox.Dispatcher);
            RaiseKey(comboBox, Key.F4);
            Pump(comboBox.Dispatcher);
            var f4Opened = comboBox.IsDropDownOpen;

            ClosePickerForFixture(comboBox, containingPopup);
            Pump(comboBox.Dispatcher);
            Keyboard.Focus(comboBox);
            Pump(comboBox.Dispatcher);
            RaiseComboBoxAltDown(comboBox);
            Pump(comboBox.Dispatcher);
            var altDownOpened = comboBox.IsDropDownOpen;

            comboBox.IsDropDownOpen = true;
            Pump(comboBox.Dispatcher);
            RaiseKey(comboBox, Key.Down);
            Pump(comboBox.Dispatcher);
            var arrowsKeepOpen = comboBox.IsDropDownOpen;
            RaiseKey(comboBox, Key.Escape);
            var escapePopupClosed = comboBox.Template.FindName("PART_Popup", comboBox) is Popup escapePopup &&
                WaitForPopupClosed(escapePopup);
            var escapeCloses = escapePopupClosed && !comboBox.IsDropDownOpen;

            ClosePickerForFixture(comboBox, containingPopup);
            Pump(comboBox.Dispatcher);
            comboBox.IsEnabled = false;
            RaiseComboTextPointer(comboBox, textToggle, UIElement.PreviewMouseLeftButtonDownEvent);
            Pump(comboBox.Dispatcher);
            var disabledHandled = RaiseComboTextPointer(
                comboBox,
                textToggle,
                UIElement.PreviewMouseLeftButtonUpEvent);
            Pump(comboBox.Dispatcher);
            var disabledClickDoesNotOpen = !comboBox.IsDropDownOpen && !disabledHandled;
            comboBox.IsEnabled = true;

            comboBox.IsDropDownOpen = true;
            Pump(comboBox.Dispatcher);
            comboBox.ApplyTemplate();
            comboBox.UpdateLayout();
            Pump(comboBox.Dispatcher);
            var itemIndex = comboBox.Items.Count > 1 ? 1 : 0;
            var item = comboBox.ItemContainerGenerator.ContainerFromIndex(itemIndex) as ComboBoxItem;
            var popupItemClickSelects = false;
            if (item is not null)
            {
                item.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent
                });
                item.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent
                });
                Pump(comboBox.Dispatcher);
                popupItemClickSelects =
                    comboBox.SelectedIndex == itemIndex ||
                    Equals(comboBox.SelectedItem, comboBox.Items[itemIndex]);
            }

            ClosePickerForFixture(comboBox, containingPopup);
            comboBox.IsEnabled = true;
            Pump(comboBox.Dispatcher);
            var arrowIsNotTabStop = MeasureComboBoxArrowIsNotTabStop(comboBox, arrow);

            return new ComboBoxPointerExerciseResult(
                true,
                true,
                pressDidNotOpen,
                true,
                releaseOpened,
                releaseHandled,
                arrowPressOpens,
                f4Opened,
                altDownOpened,
                arrowsKeepOpen,
                disabledClickDoesNotOpen,
                popupItemClickSelects,
                attached,
                arrowColumnUnchanged,
                arrowIsNotTabStop,
                escapeCloses);
        }
        finally
        {
            comboBox.IsEnabled = originalEnabled;
            ClosePickerForFixture(comboBox, containingPopup);
            if (originalIndex >= 0 && originalIndex < comboBox.Items.Count)
            {
                comboBox.SelectedIndex = originalIndex;
            }

            Pump(comboBox.Dispatcher);
        }
    }

    private static bool MeasureComboBoxArrowIsNotTabStop(ComboBox comboBox, ToggleButton? arrow)
    {
        if (arrow is null || PresentationSource.FromVisual(arrow) is null)
        {
            return false;
        }

        var owner = Window.GetWindow(comboBox);
        owner?.Activate();
        Keyboard.Focus(comboBox);
        Pump(comboBox.Dispatcher);
        comboBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Pump(comboBox.Dispatcher);
        var nextFocused = Keyboard.FocusedElement as DependencyObject;
        var nextIsArrow =
            nextFocused is not null &&
            (ReferenceEquals(nextFocused, arrow) || arrow.IsAncestorOf(nextFocused));
        Keyboard.Focus(comboBox);
        Pump(comboBox.Dispatcher);

        return !arrow.Focusable &&
               !arrow.IsTabStop &&
               !nextIsArrow;
    }

    public static (string Previous, string Next) ReadCalendarNavigationNames(DatePicker picker)
    {
        picker.ApplyTemplate();
        var originalOpen = picker.IsDropDownOpen;
        try
        {
            ClosePickerForFixture(picker);
            picker.BringIntoView();
            Window.GetWindow(picker)?.Activate();
            Pump(picker.Dispatcher);
            picker.IsDropDownOpen = true;
            Pump(picker.Dispatcher);
            picker.ApplyTemplate();
            picker.UpdateLayout();
            Pump(picker.Dispatcher);
            if (!picker.IsDropDownOpen &&
                picker.Template.FindName("PART_Button", picker) is Button button)
            {
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(picker.Dispatcher);
            }

            var calendar = PickerTemplateParts.GetCalendar(picker);
            calendar?.ApplyTemplate();
            calendar?.UpdateLayout();
            Pump(picker.Dispatcher);
            Pump(picker.Dispatcher);
            var item = calendar?.Template.FindName("PART_CalendarItem", calendar) as CalendarItem;
            item ??= calendar is null ? null : FindVisualDescendant<CalendarItem>(calendar);
            item?.ApplyTemplate();
            item?.UpdateLayout();
            var previous = item?.Template.FindName("PART_PreviousButton", item) as Button
                ?? FindCalendarNavigationButton(calendar, "上一个月");
            var next = item?.Template.FindName("PART_NextButton", item) as Button
                ?? FindCalendarNavigationButton(calendar, "下一个月");
            var names = (
                previous is null ? string.Empty : AutomationProperties.GetName(previous),
                next is null ? string.Empty : AutomationProperties.GetName(next));
            if (string.IsNullOrEmpty(names.Item1) || string.IsNullOrEmpty(names.Item2))
            {
                var popup = picker.Template.FindName("PART_Popup", picker) as Popup;
                Console.Error.WriteLine(
                    $"[calendar-navigation] pickerVisible={picker.IsVisible};pickerOpen={picker.IsDropDownOpen};" +
                    $"popupOpen={popup?.IsOpen};child={popup?.Child?.GetType().Name};" +
                    $"calendarVisible={calendar?.IsVisible};item={item?.GetType().Name};" +
                    $"calendarItemStyleIsProject={ReferenceEquals(calendar?.CalendarItemStyle, Application.Current.FindResource(typeof(CalendarItem)))};" +
                    $"itemStyleIsProject={ReferenceEquals(item?.Style, Application.Current.FindResource(typeof(CalendarItem)))};" +
                    $"previous={previous?.Name}/{previous?.IsVisible};next={next?.Name}/{next?.IsVisible}");
            }

            return names;
        }
        finally
        {
            ClosePickerForFixture(picker);
            if (originalOpen)
            {
                picker.IsDropDownOpen = true;
                Pump(picker.Dispatcher);
            }
        }
    }

    private static Button? FindCalendarNavigationButton(DependencyObject? root, string name)
    {
        if (root is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                (string.Equals(AutomationProperties.GetName(button), name, StringComparison.Ordinal) ||
                 string.Equals(button.ToolTip as string, name, StringComparison.Ordinal)))
            {
                return button;
            }

            var nested = FindCalendarNavigationButton(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void ClosePickerForFixture(Control picker, Popup? containingPopup = null)
    {
        var containingPopupStaysOpen = containingPopup?.StaysOpen;
        // Fixture resets release native ComboBox capture without a real pointer
        // event. In an off-screen nested popup that looks like an outside click
        // to WPF. Suspend only that cleanup-time dismissal, never user gestures.
        if (containingPopup is { IsOpen: true, StaysOpen: false })
            containingPopup.SetCurrentValue(Popup.StaysOpenProperty, true);
        picker.ApplyTemplate();
        var popup = picker.Template?.FindName("PART_Popup", picker) as Popup;
        var motionEnabled = popup is not null && PopupMotionBehavior.GetIsEnabled(popup);
        // Only setup/finally calls this helper. User-close assertions observe
        // WaitForPopupClosed before cleanup, with animation still enabled.
        // Stop fixture animation clocks so their two-way binding cannot reopen
        // a popup in the middle of the next independent gesture.
        if (motionEnabled)
        {
            PopupMotionBehavior.SetIsEnabled(popup!, false);
        }

        try
        {
            if (picker is DatePicker date)
            {
                date.IsDropDownOpen = false;
            }
            else if (picker is ComboBox combo)
            {
                combo.IsDropDownOpen = false;
            }

            if (popup is not null)
            {
                popup.SetCurrentValue(Popup.IsOpenProperty, false);
            }

            Pump(picker.Dispatcher);
        }
        finally
        {
            if (motionEnabled)
            {
                PopupMotionBehavior.SetIsEnabled(popup!, true);
            }
            if (containingPopup is not null && containingPopupStaysOpen is bool staysOpen)
                containingPopup.SetCurrentValue(Popup.StaysOpenProperty, staysOpen);
        }
    }

    private static bool RaiseComboTextPointer(
        ComboBox comboBox,
        UIElement target,
        RoutedEvent routedEvent) => RaisePickerPointer(target, routedEvent);

    private static bool RaiseDateTextPointer(
        DatePicker picker,
        UIElement target,
        RoutedEvent routedEvent) => RaisePickerPointer(target, routedEvent);

    public static IDisposable AcquireContextMenuInputForValidation(ContextMenu menu)
    {
        // Reuse the isolated pointer seam's lifetime guard, without changing
        // button state, moving the physical cursor, or registering a Shell icon.
        var state = new PickerPointerState();
        try
        {
            if (!menu.IsOpen || !menu.IsLoaded ||
                PresentationSource.FromVisual(menu) is not System.Windows.Interop.HwndSource source ||
                source.RootVisual is not Visual root)
                throw new InvalidOperationException("The isolated menu has no loaded input source.");
            var point = menu.TransformToAncestor(root).Transform(new Point(1, 1));
            point = source.CompositionTarget.TransformToDevice.Transform(point);
            var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
            PickerNativeInput.SetCapture(source.Handle);
            var nativeCaptured = PickerNativeInput.GetCapture() == source.Handle;
            if (!nativeCaptured)
                throw new InvalidOperationException("The isolated menu HWND could not capture pointer input.");
            // Prime HwndMouseInputProvider through its normal Win32 input path.
            // With HWND capture established it accepts this move without
            // checking the physical cursor on the non-interactive desktop.
            PickerNativeInput.SendMessage(source.Handle, 0x0200, IntPtr.Zero, coordinates);
            var captured = Mouse.Capture(menu, CaptureMode.SubTree);
            if (!captured || !ReferenceEquals(Mouse.Captured, menu))
                throw new InvalidOperationException("The isolated menu could not acquire WPF subtree capture.");
            return state;
        }
        catch
        {
            state.Dispose();
            throw;
        }
    }

    private static bool RaisePickerPointer(UIElement target, RoutedEvent routedEvent)
    {
        // Native ButtonBase needs both routed phases and pressed-device state.
        // PickerPointerState keeps the press down across dispatcher turns and
        // restores the isolated test thread's original state after the gesture.
        if (!target.IsEnabled || !target.IsVisible ||
            PresentationSource.FromVisual(target) is not System.Windows.Interop.HwndSource source ||
            source.RootVisual is not Visual root)
        {
            return false;
        }

        var pressed = routedEvent == UIElement.PreviewMouseLeftButtonDownEvent;
        var keys = new byte[256];
        if (!PickerNativeInput.GetKeyboardState(keys))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        keys[1] = pressed ? (byte)0x80 : (byte)0;
        if (!PickerNativeInput.SetKeyboardState(keys))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        // Off-screen HWNDs cannot pass HwndMouseInputProvider's cursor-over-
        // window check. Activate its input source through Win32 capture before
        // routing the existing test seam's preview and bubbling events.
        // ButtonBase still owns its element capture and pressed/click state.
        // No physical cursor movement or global input is sent.
        Window.GetWindow(target)?.Activate();
        var point = target.TransformToAncestor(root).Transform(
            new Point(target.RenderSize.Width / 2, target.RenderSize.Height / 2));
        point = source.CompositionTarget.TransformToDevice.Transform(point);
        var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
        if (pressed)
        {
            PickerNativeInput.SetCapture(source.Handle);
            if (PickerNativeInput.GetCapture() != source.Handle)
            {
                throw new InvalidOperationException("The isolated picker HWND could not capture pointer input.");
            }
        }

        PickerNativeInput.SendMessage(source.Handle, 0x0200, pressed ? new IntPtr(1) : IntPtr.Zero, coordinates);
        // TextEditor listens to the generic mouse-button events. Raising only
        // the button-specific events bypasses TextBox focus and selection.
        var textInput = target is TextBoxBase;
        var previewEvent = textInput
            ? (pressed ? Mouse.PreviewMouseDownEvent : Mouse.PreviewMouseUpEvent)
            : routedEvent;
        var eventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = previewEvent,
            Source = target
        };
        try
        {
            target.RaiseEvent(eventArgs);
            if (!eventArgs.Handled)
            {
                eventArgs.RoutedEvent = textInput
                    ? (pressed ? Mouse.MouseDownEvent : Mouse.MouseUpEvent)
                    : (pressed ? UIElement.MouseLeftButtonDownEvent : UIElement.MouseLeftButtonUpEvent);
                target.RaiseEvent(eventArgs);
            }
        }
        finally
        {
            if (!pressed && Mouse.Captured is null && PickerNativeInput.GetCapture() == source.Handle)
            {
                PickerNativeInput.ReleaseCapture();
            }
        }

        return eventArgs.Handled;
    }

    private static void RaiseComboBoxAltDown(ComboBox comboBox)
    {
        var source = PresentationSource.FromVisual(comboBox);
        if (source is null)
        {
            return;
        }

        // ComboBox consumes the actual keyboard device's modifiers. Keep this
        // state local to the isolated validation thread and restore it before
        // yielding; creating another KeyboardDevice registers another IME.
        var originalKeys = new byte[256];
        if (!PickerNativeInput.GetKeyboardState(originalKeys))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        var keys = (byte[])originalKeys.Clone();
        keys[0x12] = 0x80;
        keys[0xA4] = 0x80;
        try
        {
            if (!PickerNativeInput.SetKeyboardState(keys))
            {
                throw new System.ComponentModel.Win32Exception();
            }

            var keyDown = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Down)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            comboBox.RaiseEvent(keyDown);
            if (!keyDown.Handled)
            {
                keyDown.RoutedEvent = Keyboard.KeyDownEvent;
                comboBox.RaiseEvent(keyDown);
            }
        }
        finally
        {
            if (!PickerNativeInput.SetKeyboardState(originalKeys))
            {
                throw new System.ComponentModel.Win32Exception();
            }
        }
    }

    private sealed class PickerPointerState : IDisposable
    {
        private readonly byte[] _original = new byte[256];

        public PickerPointerState()
        {
            if (!PickerNativeInput.GetKeyboardState(_original))
            {
                throw new System.ComponentModel.Win32Exception();
            }
        }

        public void Dispose()
        {
            Mouse.Capture(null);
            if (PickerNativeInput.GetCapture() != IntPtr.Zero)
            {
                PickerNativeInput.ReleaseCapture();
            }
            if (!PickerNativeInput.SetKeyboardState(_original))
            {
                throw new System.ComponentModel.Win32Exception();
            }
        }
    }

    private static class PickerNativeInput
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool GetKeyboardState(byte[] state);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool SetKeyboardState(byte[] state);

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

    private static bool OwnsKeyboardFocus(IInputElement target) =>
        target is UIElement element
            ? element.IsKeyboardFocusWithin
            : ReferenceEquals(Keyboard.FocusedElement, target);
}
