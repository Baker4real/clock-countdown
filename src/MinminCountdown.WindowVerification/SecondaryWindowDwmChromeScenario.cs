using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #324：提醒中心与规则编辑器改为不透明无边框窗口，支持与不支持 DWM 系统圆角/投影/边框色两种环境的独立窗口验证。
internal static class SecondaryWindowDwmChromeScenario
{
    private const int GwlExStyle = -20;
    private const uint WsExLayered = 0x00080000;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaVisibleFrameBorderThickness = 37;
    private const uint DwmNcrpEnabled = 2;
    private const uint DwmwcpRound = 2;
    private const int DwmCompositionDisabledHr = unchecked((int)0x80263001);
    private const int InvalidArgHr = unchecked((int)0x80070057);
    private const double WorkAreaMargin = 18;
    private const double MaximumInitialRuleEditorHeight = 790;

    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 12, 1, 0, 0, TimeSpan.Zero);

    private static readonly Lazy<string?> SharedCaptureDirectory = new(CreateCaptureDirectory);

    internal static (bool Passed, string Explanation) RunSupportedDwmChrome(string meaning)
    {
        var failures = new List<string>();
        var captureEvidence = CreateCaptureWriter();

        CheckNativeSupportedReminderCenter(failures, captureEvidence);
        CheckNativeSupportedRulesEditor(failures, captureEvidence);
        CheckBorderColorAndPaletteUpdates(failures, captureEvidence);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunUnsupportedDwmChromeFallback(string meaning)
    {
        var failures = new List<string>();
        var captureEvidence = CreateCaptureWriter();

        CheckOlderWindowsFallback(failures, captureEvidence);
        CheckDwmShadowFailureFallback(failures);
        CheckDwmRoundedCornersFailureFallback(failures);
        CheckDwmExceptionFallback(failures);
        CheckBorderColorOnlyFallbackAndRuntimeDegrade(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    private static void CheckNativeSupportedReminderCenter(
        List<string> failures,
        Action<Window, string>? captureEvidence)
    {
        var logPath = CreateTempLogPath("supported-reminder");
        var logger = new DiagnosticLogger(logPath);
        var sourceMonitor = new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1.0, 1.0);
        var hostWindow = new Window
        {
            Width = 520,
            Height = 390,
            Left = 120,
            Top = 140,
            ShowInTaskbar = false,
            ShowActivated = true,
            Opacity = 0
        };
        ReminderCenterWindow? center = null;
        try
        {
            using var monitorScope = MonitorWorkAreaResolver.OverrideForValidation(sourceMonitor);
            hostWindow.Show();
            hostWindow.Activate();
            Settle(hostWindow);

            var noteKey = new ActiveReminderOccurrenceKey("dwm-supported-note-rule", FixtureUtc.AddMinutes(-3));
            center = new ReminderCenterWindow(hostWindow, logger);
            center.AddEntry(new ReminderCenterItem(
                noteKey,
                new NoteRecord(
                    "dwm-supported-note",
                    "核对提醒中心系统圆角、投影与 1 DIP 暖色边框",
                    "2038-07-12",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                0,
                0));
            center.Show();
            center.SetDeliveryReady(noteKey);
            center.UpdateTime(FixtureUtc);
            Settle(center);

            AssertOpaqueBorderlessDwmSupported(
                center,
                "提醒中心",
                expectedBackgroundResourceKey: "ReminderPageBrush",
                failures);

            var entries = ReadDiagnosticEntries(logPath);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                if (!entries.Any(entry =>
                        entry.Level == "Info" &&
                        entry.Event == DwmWindowChrome.ReminderCenterEvents.Applied))
                {
                    failures.Add("提醒中心在 Windows 11 真实环境未写出 ReminderCenterWindowDwmRoundedCornersApplied 诊断事件");
                }
            }

            // 提醒中心既有契约：不抢焦点、不持续置顶、任务栏可见、首次贴齐右上。
            if (center.ShowActivated || center.IsActive || center.Topmost || !center.ShowInTaskbar)
            {
                failures.Add(
                    $"提醒中心焦点/置顶/任务栏契约退化：ShowActivated={center.ShowActivated}, IsActive={center.IsActive}, " +
                    $"Topmost={center.Topmost}, ShowInTaskbar={center.ShowInTaskbar}");
            }

            var expectedLeft = sourceMonitor.WorkAreaDip.Right - center.ActualWidth - WorkAreaMargin;
            var expectedTop = sourceMonitor.WorkAreaDip.Top + WorkAreaMargin;
            if (Math.Abs(center.Left - expectedLeft) > 1.5 || Math.Abs(center.Top - expectedTop) > 1.5)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"提醒中心首次未贴齐悬浮窗所在屏右上：actual=({center.Left:0.##},{center.Top:0.##}), expected=({expectedLeft:0.##},{expectedTop:0.##})"));
            }

            captureEvidence?.Invoke(center, "reminder-center-dwm-supported-day");

            // 头部可拖动，交互按钮不触发拖动，手动位置在 ReapplyPlacement 后保留。
            var dragBefore = center.NativeWindowDragRequestCount;
            center.DragHeader.RaiseEvent(CreateLeftMouseDown());
            Settle(center);
            if (center.NativeWindowDragRequestCount != dragBefore + 1)
            {
                failures.Add($"提醒中心头部拖动未发起原生拖动请求（before={dragBefore}, after={center.NativeWindowDragRequestCount}）");
            }

            var minimizeButton = FindByAutomationId<Button>(center, "ReminderCenterMinimizeToTaskbar");
            minimizeButton.RaiseEvent(CreateLeftMouseDown());
            Settle(center);
            if (center.NativeWindowDragRequestCount != dragBefore + 1)
            {
                failures.Add("提醒中心头部按钮误触发窗口拖动");
            }

            const double manualLeft = 180;
            const double manualTop = 160;
            center.Left = manualLeft;
            center.Top = manualTop;
            center.ReapplyPlacement();
            Settle(center);
            if (!center.HasManualWindowPlacement ||
                Math.Abs(center.Left - manualLeft) > 0.5 ||
                Math.Abs(center.Top - manualTop) > 0.5)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"提醒中心手动位置未保留：manual={center.HasManualWindowPlacement}, actual=({center.Left:0.##},{center.Top:0.##})"));
            }
        }
        finally
        {
            CloseReminderCenter(center);
            CloseWindow(hostWindow);
            DeleteFileIfPresent(logPath);
        }
    }

    private static void CheckNativeSupportedRulesEditor(
        List<string> failures,
        Action<Window, string>? captureEvidence)
    {
        var logPath = CreateTempLogPath("supported-editor");
        var logger = new DiagnosticLogger(logPath);
        var primary = new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1.0, 1.0);
        var secondary = new MonitorWorkArea(new Rect(-1536, 0, 1536, 688), 1.5, 1.5);
        var owner = new Window
        {
            Width = 600,
            Height = 400,
            Left = -1400,
            Top = 80,
            ShowInTaskbar = false,
            ShowActivated = false,
            Opacity = 0
        };
        ReminderRulesEditorWindow? editor = null;
        try
        {
            using var monitorScope = MonitorWorkAreaResolver.OverrideForValidation(primary, secondary);
            owner.Show();
            Settle(owner);

            var rules = new ReminderRule[]
            {
                new AbsoluteOnceReminderRule("dwm-supported-rule-1", FixtureUtc.AddHours(2)),
                new CalendarReminderRule(
                    "dwm-supported-rule-2",
                    CalendarFrequency.Weekly,
                    1,
                    new DateOnly(2038, 7, 12),
                    new TimeOnly(9, 30),
                    new WeeklyCalendarSelector([DayOfWeek.Monday, DayOfWeek.Wednesday]))
            };
            editor = new ReminderRulesEditorWindow(rules, new FixedClock(FixtureUtc), logger: logger)
            {
                Owner = owner,
                ShowActivated = false
            };
            editor.Show();
            Settle(editor);

            AssertOpaqueBorderlessDwmSupported(
                editor,
                "规则编辑器",
                expectedBackgroundResourceKey: "CanvasBrush",
                failures);

            var entries = ReadDiagnosticEntries(logPath);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                if (!entries.Any(entry =>
                        entry.Level == "Info" &&
                        entry.Event == DwmWindowChrome.ReminderRulesEditorEvents.Applied))
                {
                    failures.Add("规则编辑器在 Windows 11 真实环境未写出 ReminderRulesEditorWindowDwmRoundedCornersApplied 诊断事件");
                }
            }

            // 规则编辑器既有契约：居中于所有者所在屏、夹取、高度不超过 min(790, workAreaHeight - 36)、ResizeGrip 可达。
            var maxExpectedHeight = Math.Min(MaximumInitialRuleEditorHeight, secondary.WorkAreaDip.Height - 36);
            if (editor.SizeToContent != SizeToContent.Height ||
                editor.ActualHeight > maxExpectedHeight + 0.5 ||
                editor.Left >= 0 ||
                !WindowInsideRect(secondary.WorkAreaDip, editor))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"规则编辑器居中/夹取/高度规则退化：sizeToContent={editor.SizeToContent}, bounds=({editor.Left:0.##},{editor.Top:0.##},{editor.ActualWidth:0.##}x{editor.ActualHeight:0.##}), maxH={maxExpectedHeight:0.##}"));
            }

            if (editor.ResizeMode != ResizeMode.CanResizeWithGrip)
            {
                failures.Add($"规则编辑器 ResizeMode 变化：{editor.ResizeMode}");
            }

            var grip = VisualDescendants<ResizeGrip>(editor).FirstOrDefault();
            if (grip is null || !IsReachable(editor, grip))
            {
                failures.Add("规则编辑器在启用不透明 WindowChrome 后缩放手柄 ResizeGrip 不可达");
            }

            var dragBefore = editor.NativeWindowDragRequestCount;
            editor.DragHeader.RaiseEvent(CreateLeftMouseDown());
            Settle(editor);
            if (editor.NativeWindowDragRequestCount != dragBefore + 1)
            {
                failures.Add($"规则编辑器头部拖动未发起原生拖动请求（before={dragBefore}, after={editor.NativeWindowDragRequestCount}）");
            }

            const double manualLeft = -1480;
            const double manualTop = 48;
            editor.Left = manualLeft;
            editor.Top = manualTop;
            editor.ReapplyPlacement();
            Settle(editor);
            if (!editor.HasManualWindowPlacement ||
                Math.Abs(editor.Left - manualLeft) > 0.5 ||
                Math.Abs(editor.Top - manualTop) > 0.5)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"规则编辑器手动位置未保留：manual={editor.HasManualWindowPlacement}, actual=({editor.Left:0.##},{editor.Top:0.##})"));
            }

            captureEvidence?.Invoke(editor, "rules-editor-dwm-supported-day");
        }
        finally
        {
            CloseWindow(editor);
            CloseWindow(owner);
            DeleteFileIfPresent(logPath);
        }
    }

    private static void CheckBorderColorAndPaletteUpdates(
        List<string> failures,
        Action<Window, string>? captureEvidence)
    {
        var logPath = CreateTempLogPath("supported-border-color");
        var logger = new DiagnosticLogger(logPath);
        var centerHost = new RecordingDwmHost(isSupportedOperatingSystem: true);
        var editorHost = new RecordingDwmHost(isSupportedOperatingSystem: true);
        var expectedDayColorRef = DwmWindowChrome.ToColorRef(ApplicationTheme.DayColorFor("PeachLineBrush"));
        var customContrastWindowText = Color.FromRgb(0x00, 0xFF, 0x80);
        var expectedContrastColorRef = DwmWindowChrome.ToColorRef(customContrastWindowText);
        ReminderCenterWindow? center = null;
        ReminderRulesEditorWindow? editor = null;
        try
        {
            var noteKey = new ActiveReminderOccurrenceKey("dwm-border-color-note-rule", FixtureUtc.AddMinutes(-1));
            center = new ReminderCenterWindow(logger: logger, dwmChromeHost: centerHost);
            center.AddEntry(new ReminderCenterItem(
                noteKey,
                new NoteRecord(
                    "dwm-border-color-note",
                    "验证 DWMWA_BORDER_COLOR 随系统对比度同步刷新",
                    "2038-07-12",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                0,
                0));
            editor = new ReminderRulesEditorWindow(
                [new AbsoluteOnceReminderRule("dwm-border-color-rule", FixtureUtc.AddHours(3))],
                new FixedClock(FixtureUtc),
                logger: logger,
                dwmChromeHost: editorHost)
            {
                ShowActivated = false
            };

            center.Show();
            center.SetDeliveryReady(noteKey);
            center.UpdateTime(FixtureUtc);
            editor.Show();
            Settle(center);
            Settle(editor);

            AssertInitialSupportedDwmCalls(centerHost, "提醒中心", expectedDayColorRef, failures);
            AssertInitialSupportedDwmCalls(editorHost, "规则编辑器", expectedDayColorRef, failures);

            using (var contrastScope = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrastScope.ChangeColors(new ApplicationTheme.SystemContrastPalette(
                    Window: Color.FromRgb(0x00, 0x00, 0x00),
                    WindowText: customContrastWindowText,
                    Control: Color.FromRgb(0x10, 0x10, 0x10),
                    ControlText: customContrastWindowText,
                    Highlight: Color.FromRgb(0x1A, 0xEB, 0xFF),
                    HighlightText: Color.FromRgb(0x00, 0x00, 0x00),
                    GrayText: Color.FromRgb(0x3F, 0xF2, 0x3F)));
                Settle(center);
                Settle(editor);

                AssertLastBorderColor(centerHost, "提醒中心（系统对比度）", expectedContrastColorRef, failures);
                AssertLastBorderColor(editorHost, "规则编辑器（系统对比度）", expectedContrastColorRef, failures);
                captureEvidence?.Invoke(center, "reminder-center-dwm-supported-high-contrast");
                captureEvidence?.Invoke(editor, "rules-editor-dwm-supported-high-contrast");
            }

            Settle(center);
            Settle(editor);
            AssertLastBorderColor(centerHost, "提醒中心（恢复暖粉日间）", expectedDayColorRef, failures);
            AssertLastBorderColor(editorHost, "规则编辑器（恢复暖粉日间）", expectedDayColorRef, failures);

            var centerCallsBeforeClose = centerHost.Calls.Count;
            var editorCallsBeforeClose = editorHost.Calls.Count;
            CloseReminderCenter(center);
            center = null;
            CloseWindow(editor);
            editor = null;

            // 关闭窗口后触发配色变化，不得再向已关闭窗口调用 DWMWA_BORDER_COLOR。
            using (ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                WindowValidationInput.Pump(Application.Current.Dispatcher);
            }
            WindowValidationInput.Pump(Application.Current.Dispatcher);

            if (centerHost.Calls.Count != centerCallsBeforeClose)
            {
                failures.Add($"提醒中心关闭后仍收到 {centerHost.Calls.Count - centerCallsBeforeClose} 次 DWM 边框色更新（未注销 PaletteChanged）");
            }

            if (editorHost.Calls.Count != editorCallsBeforeClose)
            {
                failures.Add($"规则编辑器关闭后仍收到 {editorHost.Calls.Count - editorCallsBeforeClose} 次 DWM 边框色更新（未注销 PaletteChanged）");
            }
        }
        finally
        {
            CloseReminderCenter(center);
            CloseWindow(editor);
            DeleteFileIfPresent(logPath);
        }
    }

    private static void CheckOlderWindowsFallback(
        List<string> failures,
        Action<Window, string>? captureEvidence)
    {
        var logPath = CreateTempLogPath("fallback-older-os");
        var logger = new DiagnosticLogger(logPath);
        var centerHost = new RecordingDwmHost(isSupportedOperatingSystem: false);
        var editorHost = new RecordingDwmHost(isSupportedOperatingSystem: false);
        ReminderCenterWindow? center = null;
        ReminderRulesEditorWindow? editor = null;
        try
        {
            var noteKey = new ActiveReminderOccurrenceKey("dwm-older-os-note-rule", FixtureUtc.AddMinutes(-4));
            var acknowledged = false;
            center = new ReminderCenterWindow(logger: logger, dwmChromeHost: centerHost);
            center.AcknowledgeRequested += (_, args) => acknowledged = args.Key == noteKey;
            center.AddEntry(new ReminderCenterItem(
                noteKey,
                new NoteRecord(
                    "dwm-older-os-note",
                    "旧版 Windows 不支持系统圆角时回退为直角与 1 DIP 内容暖边",
                    "2038-07-12",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                0,
                0));
            center.Show();
            center.SetDeliveryReady(noteKey);
            center.UpdateTime(FixtureUtc);
            Settle(center);

            AssertSquareContentBorderFallback(center, "提醒中心（旧版 Windows）", failures);
            if (centerHost.Calls.Count != 0)
            {
                failures.Add($"旧版 Windows 上提醒中心不应调用 DwmSetWindowAttribute（actual calls={centerHost.Calls.Count}）");
            }

            // 验证回退态下提醒中心完整可操作（点击「知道了」触发领域事件 + 头部拖动正常）。
            var ackButton = VisualDescendants<Button>(center)
                .First(button => AutomationProperties.GetName(button).StartsWith("知道了：", StringComparison.Ordinal));
            InvokeButton(center, ackButton);
            if (!acknowledged)
            {
                failures.Add("旧版 Windows 回退态下提醒中心「知道了」按钮不可操作");
            }

            var centerDragBefore = center.NativeWindowDragRequestCount;
            center.DragHeader.RaiseEvent(CreateLeftMouseDown());
            Settle(center);
            if (center.NativeWindowDragRequestCount != centerDragBefore + 1)
            {
                failures.Add("旧版 Windows 回退态下提醒中心头部不可拖动");
            }

            // 验证回退态下内容边界随系统对比度自动更新。
            using (var contrastScope = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                var contrastBorderColor = Color.FromRgb(0xFF, 0xFF, 0x00);
                contrastScope.ChangeColors(new ApplicationTheme.SystemContrastPalette(
                    Window: Color.FromRgb(0x00, 0x00, 0x00),
                    WindowText: contrastBorderColor,
                    Control: Color.FromRgb(0x10, 0x10, 0x10),
                    ControlText: contrastBorderColor,
                    Highlight: Color.FromRgb(0x1A, 0xEB, 0xFF),
                    HighlightText: Color.FromRgb(0x00, 0x00, 0x00),
                    GrayText: Color.FromRgb(0x3F, 0xF2, 0x3F)));
                Settle(center);
                if (center.Content is not Border contrastShell ||
                    contrastShell.BorderBrush is not SolidColorBrush contrastBrush ||
                    contrastBrush.Color != contrastBorderColor)
                {
                    failures.Add("旧版 Windows 回退态下提醒中心内容边界未随系统对比度更新");
                }
            }
            Settle(center);
            captureEvidence?.Invoke(center, "reminder-center-dwm-fallback-square");

            editor = new ReminderRulesEditorWindow(
                Array.Empty<ReminderRule>(),
                new FixedClock(FixtureUtc),
                logger: logger,
                dwmChromeHost: editorHost)
            {
                ShowActivated = false
            };
            editor.Show();
            Settle(editor);

            AssertSquareContentBorderFallback(editor, "规则编辑器（旧版 Windows）", failures);
            if (editorHost.Calls.Count != 0)
            {
                failures.Add($"旧版 Windows 上规则编辑器不应调用 DwmSetWindowAttribute（actual calls={editorHost.Calls.Count}）");
            }

            // 验证回退态下规则编辑器完整可操作（通过真实按钮添加一条规则 + 缩放手柄可达 + 头部拖动正常）。
            var rulesCount = FindByAutomationId<TextBlock>(editor, "ReminderRulesCount");
            var countBefore = rulesCount.Text;
            var saveRuleButton = FindByAutomationId<Button>(editor, "SaveReminderRuleButton");
            InvokeButton(editor, saveRuleButton);
            if (!string.Equals(countBefore, "0 / 8 条", StringComparison.Ordinal) ||
                !string.Equals(rulesCount.Text, "1 / 8 条", StringComparison.Ordinal))
            {
                failures.Add($"旧版 Windows 回退态下规则编辑器添加规则失败（before={countBefore}, after={rulesCount.Text}）");
            }

            var grip = VisualDescendants<ResizeGrip>(editor).FirstOrDefault();
            if (grip is null || !IsReachable(editor, grip))
            {
                failures.Add("旧版 Windows 回退态下规则编辑器缩放手柄不可达");
            }

            var editorDragBefore = editor.NativeWindowDragRequestCount;
            editor.DragHeader.RaiseEvent(CreateLeftMouseDown());
            Settle(editor);
            if (editor.NativeWindowDragRequestCount != editorDragBefore + 1)
            {
                failures.Add("旧版 Windows 回退态下规则编辑器头部不可拖动");
            }

            captureEvidence?.Invoke(editor, "rules-editor-dwm-fallback-square");

            var entries = ReadDiagnosticEntries(logPath);
            RequireDiagnosticEvent(
                entries,
                "Warning",
                DwmWindowChrome.ReminderCenterEvents.RoundedCornersFallback,
                expectedHResult: null,
                expectedExceptionType: null,
                "旧版 Windows 提醒中心回退诊断",
                failures);
            RequireDiagnosticEvent(
                entries,
                "Warning",
                DwmWindowChrome.ReminderRulesEditorEvents.RoundedCornersFallback,
                expectedHResult: null,
                expectedExceptionType: null,
                "旧版 Windows 规则编辑器回退诊断",
                failures);
        }
        finally
        {
            CloseReminderCenter(center);
            CloseWindow(editor);
            DeleteFileIfPresent(logPath);
        }
    }

    private static void CheckDwmShadowFailureFallback(List<string> failures)
    {
        RunFailureMatrixCase(
            caseLabel: "DWM 投影/非客户区渲染不支持",
            hostFactory: () => new RecordingDwmHost(
                isSupportedOperatingSystem: true,
                handler: attribute => attribute == DwmWindowAttribute.DWMWA_NCRENDERING_POLICY
                    ? DwmCompositionDisabledHr
                    : 0),
            expectedReminderEvent: DwmWindowChrome.ReminderCenterEvents.ShadowFallback,
            expectedEditorEvent: DwmWindowChrome.ReminderRulesEditorEvents.ShadowFallback,
            expectedHResult: DwmCompositionDisabledHr,
            expectedExceptionType: null,
            failures);
    }

    private static void CheckDwmRoundedCornersFailureFallback(List<string> failures)
    {
        RunFailureMatrixCase(
            caseLabel: "DWM 系统圆角不支持",
            hostFactory: () => new RecordingDwmHost(
                isSupportedOperatingSystem: true,
                handler: attribute => attribute == DwmWindowAttribute.DWMWA_WINDOW_CORNER_PREFERENCE
                    ? InvalidArgHr
                    : 0),
            expectedReminderEvent: DwmWindowChrome.ReminderCenterEvents.RoundedCornersFallback,
            expectedEditorEvent: DwmWindowChrome.ReminderRulesEditorEvents.RoundedCornersFallback,
            expectedHResult: InvalidArgHr,
            expectedExceptionType: null,
            failures);
    }

    private static void CheckDwmExceptionFallback(List<string> failures)
    {
        RunFailureMatrixCase(
            caseLabel: "DWM 接口异常（模拟缺失 dwmapi 入口或非合成隔离桌面）",
            hostFactory: () => new RecordingDwmHost(
                isSupportedOperatingSystem: true,
                handler: _ => throw new DllNotFoundException("Simulated missing dwmapi.dll")),
            expectedReminderEvent: DwmWindowChrome.ReminderCenterEvents.RoundedCornersFallback,
            expectedEditorEvent: DwmWindowChrome.ReminderRulesEditorEvents.RoundedCornersFallback,
            expectedHResult: null,
            expectedExceptionType: typeof(DllNotFoundException).FullName,
            failures);
    }

    private static void CheckBorderColorOnlyFallbackAndRuntimeDegrade(List<string> failures)
    {
        var logPath = CreateTempLogPath("fallback-border-color-only");
        var logger = new DiagnosticLogger(logPath);
        var centerHost = new RecordingDwmHost(
            isSupportedOperatingSystem: true,
            handler: attribute => attribute == DwmWindowAttribute.DWMWA_BORDER_COLOR ? InvalidArgHr : 0);
        var editorBorderShouldFail = false;
        var editorHost = new RecordingDwmHost(
            isSupportedOperatingSystem: true,
            handler: attribute => attribute == DwmWindowAttribute.DWMWA_BORDER_COLOR && editorBorderShouldFail
                ? InvalidArgHr
                : 0);
        ReminderCenterWindow? center = null;
        ReminderRulesEditorWindow? editor = null;
        try
        {
            center = new ReminderCenterWindow(logger: logger, dwmChromeHost: centerHost);
            center.Show();
            Settle(center);

            // 圆角/投影成功但边框色失败：保留圆角与投影，由内容边界承担 1 DIP 暖边并记录 BorderColorFallback。
            AssertSquareContentBorderFallback(center, "提醒中心（仅边框色失败）", failures);

            editor = new ReminderRulesEditorWindow(
                Array.Empty<ReminderRule>(),
                new FixedClock(FixtureUtc),
                logger: logger,
                dwmChromeHost: editorHost)
            {
                ShowActivated = false
            };
            editor.Show();
            Settle(editor);

            // 规则编辑器首次成功 -> 内容边框为 0/Padding 1；运行中换色时边框色设置失败 -> 自动切回内容边界 1/Padding 0。
            if (editor.Content is not Border editorShell ||
                editorShell.BorderThickness != new Thickness(0) ||
                editorShell.Padding != new Thickness(1))
            {
                failures.Add("规则编辑器首次支持边框色时应关闭内容 BorderThickness 并保留 1 DIP Padding");
            }

            editorBorderShouldFail = true;
            using (ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                Settle(editor);
                AssertSquareContentBorderFallback(editor, "规则编辑器（运行中换色边框色失败降级）", failures);
            }
            Settle(editor);

            var entries = ReadDiagnosticEntries(logPath);
            RequireDiagnosticEvent(
                entries,
                "Info",
                DwmWindowChrome.ReminderCenterEvents.Applied,
                expectedHResult: null,
                expectedExceptionType: null,
                "提醒中心圆角应用诊断",
                failures);
            RequireDiagnosticEvent(
                entries,
                "Warning",
                DwmWindowChrome.ReminderCenterEvents.BorderColorFallback,
                expectedHResult: InvalidArgHr,
                expectedExceptionType: null,
                "提醒中心边框色回退诊断",
                failures);
            RequireDiagnosticEvent(
                entries,
                "Warning",
                DwmWindowChrome.ReminderRulesEditorEvents.BorderColorFallback,
                expectedHResult: InvalidArgHr,
                expectedExceptionType: null,
                "规则编辑器运行中边框色回退诊断",
                failures);
        }
        finally
        {
            CloseReminderCenter(center);
            CloseWindow(editor);
            DeleteFileIfPresent(logPath);
        }
    }

    private static void RunFailureMatrixCase(
        string caseLabel,
        Func<RecordingDwmHost> hostFactory,
        string expectedReminderEvent,
        string expectedEditorEvent,
        int? expectedHResult,
        string? expectedExceptionType,
        List<string> failures)
    {
        var logPath = CreateTempLogPath("fallback-matrix");
        var logger = new DiagnosticLogger(logPath);
        ReminderCenterWindow? center = null;
        ReminderRulesEditorWindow? editor = null;
        try
        {
            center = new ReminderCenterWindow(logger: logger, dwmChromeHost: hostFactory());
            center.Show();
            Settle(center);
            AssertSquareContentBorderFallback(center, $"提醒中心（{caseLabel}）", failures);

            editor = new ReminderRulesEditorWindow(
                Array.Empty<ReminderRule>(),
                new FixedClock(FixtureUtc),
                logger: logger,
                dwmChromeHost: hostFactory())
            {
                ShowActivated = false
            };
            editor.Show();
            Settle(editor);
            AssertSquareContentBorderFallback(editor, $"规则编辑器（{caseLabel}）", failures);

            var entries = ReadDiagnosticEntries(logPath);
            RequireDiagnosticEvent(
                entries,
                "Warning",
                expectedReminderEvent,
                expectedHResult,
                expectedExceptionType,
                $"提醒中心（{caseLabel}）",
                failures);
            RequireDiagnosticEvent(
                entries,
                "Warning",
                expectedEditorEvent,
                expectedHResult,
                expectedExceptionType,
                $"规则编辑器（{caseLabel}）",
                failures);
        }
        finally
        {
            CloseReminderCenter(center);
            CloseWindow(editor);
            DeleteFileIfPresent(logPath);
        }
    }

    private static void AssertOpaqueBorderlessDwmSupported(
        Window window,
        string label,
        string expectedBackgroundResourceKey,
        List<string> failures)
    {
        AssertCommonOpaqueBorderlessChrome(window, label, failures);

        var expectedBackground = window.TryFindResource(expectedBackgroundResourceKey) as SolidColorBrush;
        if (window.Background is not SolidColorBrush actualBackground ||
            actualBackground.Color.A != 255 ||
            (expectedBackground is not null && actualBackground.Color != expectedBackground.Color))
        {
            failures.Add($"{label}窗口背景不是不透明的 {expectedBackgroundResourceKey}（actual={window.Background}）");
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var cornerHr = DwmGetWindowAttribute(hwnd, DwmwaWindowCornerPreference, out var cornerPreference, sizeof(uint));
            if (cornerHr != 0 || cornerPreference != DwmwcpRound)
            {
                failures.Add($"{label}在 Windows 11 上未应用 DWMWCP_ROUND（hr=0x{cornerHr:X8}, value={cornerPreference}）");
            }

            var borderHr = DwmGetWindowAttribute(hwnd, DwmwaVisibleFrameBorderThickness, out var borderThicknessPx, sizeof(uint));
            if (borderHr != 0 || borderThicknessPx == 0)
            {
                failures.Add($"{label}未检测到 DWM 原生可见边框（hr=0x{borderHr:X8}, px={borderThicknessPx}）");
            }
        }

        if (window.Content is not Border shell)
        {
            failures.Add($"{label}内容根节点不是 Border");
            return;
        }

        if (shell.CornerRadius != new CornerRadius(0))
        {
            failures.Add($"{label}内容根 Border 仍保留 WPF 圆角（actual={shell.CornerRadius}，应由 DWM 承担圆角并保持 0）");
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
            (shell.BorderThickness != new Thickness(0) || shell.Padding != new Thickness(1)))
        {
            failures.Add(
                $"{label}在支持 DWM 边框色时应关闭内容 BorderThickness 并保留 1 DIP Padding 以避免双边与位移" +
                $"（actual thickness={shell.BorderThickness}, padding={shell.Padding}）");
        }
    }

    private static void AssertSquareContentBorderFallback(
        Window window,
        string label,
        List<string> failures)
    {
        AssertCommonOpaqueBorderlessChrome(window, label, failures);

        if (!window.IsVisible || window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            failures.Add($"{label}回退态窗口未正常显示（visible={window.IsVisible}, size={window.ActualWidth}x{window.ActualHeight}）");
        }

        if (window.Content is not Border shell)
        {
            failures.Add($"{label}内容根节点不是 Border");
            return;
        }

        if (shell.CornerRadius != new CornerRadius(0))
        {
            failures.Add($"{label}回退态未保持直角（actual CornerRadius={shell.CornerRadius}）");
        }

        if (shell.BorderThickness != new Thickness(1) || shell.Padding != new Thickness(0))
        {
            failures.Add(
                $"{label}回退态未由内容边界承担 1 DIP 暖边（actual thickness={shell.BorderThickness}, padding={shell.Padding}）");
        }

        var expectedBorderBrush = window.TryFindResource("PeachLineBrush") as SolidColorBrush;
        if (shell.BorderBrush is not SolidColorBrush actualBorderBrush ||
            (expectedBorderBrush is not null && actualBorderBrush.Color != expectedBorderBrush.Color))
        {
            failures.Add($"{label}回退态内容边界颜色与 PeachLineBrush 不一致（actual={shell.BorderBrush}）");
        }
    }

    private static void AssertCommonOpaqueBorderlessChrome(
        Window window,
        string label,
        List<string> failures)
    {
        if (window.AllowsTransparency)
        {
            failures.Add($"{label}仍开启 AllowsTransparency=true（应为不透明无边框窗口）");
        }

        if (window.WindowStyle != WindowStyle.None)
        {
            failures.Add($"{label}的 WindowStyle 不是 None（actual={window.WindowStyle}）");
        }

        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome is null ||
            chrome.CornerRadius != new CornerRadius(0) ||
            chrome.GlassFrameThickness != new Thickness(0) ||
            chrome.UseAeroCaptionButtons)
        {
            failures.Add($"{label}未配置与管理窗口一致的无边框 WindowChrome（CornerRadius=0、GlassFrameThickness=0）");
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            failures.Add($"{label}未创建 HWND");
            return;
        }

        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        if ((exStyle & WsExLayered) != 0)
        {
            failures.Add($"{label}仍带 Win32 WS_EX_LAYERED 分层透明样式（exStyle=0x{exStyle:X8}）");
        }
    }

    private static void AssertInitialSupportedDwmCalls(
        RecordingDwmHost host,
        string label,
        uint expectedDayColorRef,
        List<string> failures)
    {
        if (host.Calls.Count < 3 ||
            host.Calls[0].Attribute != DwmWindowAttribute.DWMWA_NCRENDERING_POLICY ||
            host.Calls[0].Value != DwmNcrpEnabled ||
            host.Calls[1].Attribute != DwmWindowAttribute.DWMWA_WINDOW_CORNER_PREFERENCE ||
            host.Calls[1].Value != DwmwcpRound ||
            host.Calls[2].Attribute != DwmWindowAttribute.DWMWA_BORDER_COLOR ||
            host.Calls[2].Value != expectedDayColorRef)
        {
            var actual = string.Join(", ", host.Calls.Select(call => $"{call.Attribute}=0x{call.Value:X8}"));
            failures.Add(
                $"{label}初始 DWM 属性请求序列不符（expected NCRENDERING=2, CORNER=2, BORDER=0x{expectedDayColorRef:X8}; actual=[{actual}]）");
        }
    }

    private static void AssertLastBorderColor(
        RecordingDwmHost host,
        string label,
        uint expectedColorRef,
        List<string> failures)
    {
        var borderCalls = host.Calls
            .Where(call => call.Attribute == DwmWindowAttribute.DWMWA_BORDER_COLOR)
            .ToArray();
        if (borderCalls.Length == 0 || borderCalls[^1].Value != expectedColorRef)
        {
            var actual = borderCalls.Length == 0 ? "无" : $"0x{borderCalls[^1].Value:X8}";
            failures.Add($"{label}的 DWMWA_BORDER_COLOR 未更新为期望值 0x{expectedColorRef:X8}（actual={actual}）");
        }
    }

    private static void RequireDiagnosticEvent(
        IReadOnlyList<DiagnosticLogRow> entries,
        string level,
        string eventName,
        int? expectedHResult,
        string? expectedExceptionType,
        string context,
        List<string> failures)
    {
        var match = entries.FirstOrDefault(entry =>
            string.Equals(entry.Level, level, StringComparison.Ordinal) &&
            string.Equals(entry.Event, eventName, StringComparison.Ordinal) &&
            (expectedHResult is null || entry.HResult == expectedHResult) &&
            (expectedExceptionType is null ||
             string.Equals(entry.ExceptionType, expectedExceptionType, StringComparison.Ordinal)));
        if (match is null)
        {
            var observed = string.Join(
                ", ",
                entries.Select(entry => $"{entry.Level}:{entry.Event}(hr={entry.HResult},ex={entry.ExceptionType})"));
            failures.Add(
                $"{context}未写出期望诊断事件 {level}:{eventName}（hr={expectedHResult}, ex={expectedExceptionType}; actual=[{observed}]）");
        }
    }

    private static IReadOnlyList<DiagnosticLogRow> ReadDiagnosticEntries(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return Array.Empty<DiagnosticLogRow>();
        }

        var rows = new List<DiagnosticLogRow>();
        foreach (var line in File.ReadAllLines(logPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            rows.Add(new DiagnosticLogRow(
                root.TryGetProperty("level", out var levelProp) ? levelProp.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("event", out var eventProp) ? eventProp.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("hResult", out var hrProp) && hrProp.ValueKind == JsonValueKind.Number
                    ? hrProp.GetInt32()
                    : null,
                root.TryGetProperty("exceptionType", out var exProp) ? exProp.GetString() : null));
        }

        return rows;
    }

    private static string CreateTempLogPath(string suffix) =>
        Path.Combine(Path.GetTempPath(), $"MinminCountdown-dwm-324-{suffix}-{Guid.NewGuid():N}.jsonl");

    private static void DeleteFileIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup best effort.
        }
    }

    private static T FindByAutomationId<T>(Window window, string automationId)
        where T : FrameworkElement =>
        VisualDescendants<T>(window)
            .Single(element => string.Equals(
                AutomationProperties.GetAutomationId(element),
                automationId,
                StringComparison.Ordinal));

    private static void InvokeButton(Window window, Button button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button)
            ?? throw new InvalidOperationException($"无法获取按钮 {button.Content} 的自动化 peer。");
        var provider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException($"按钮 {button.Content} 缺少 Invoke 模式。");
        provider.Invoke();
        Settle(window);
    }

    private static MouseButtonEventArgs CreateLeftMouseDown() =>
        new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent
        };

    private static bool WindowInsideRect(Rect workArea, Window window) =>
        window.Left >= workArea.Left - 0.5 &&
        window.Top >= workArea.Top - 0.5 &&
        window.Left + window.ActualWidth <= workArea.Right + 0.5 &&
        window.Top + window.ActualHeight <= workArea.Bottom + 0.5;

    private static bool IsReachable(Window window, FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var bounds = Bounds(element, window);
        return bounds.Left >= -0.5 &&
               bounds.Top >= -0.5 &&
               bounds.Right <= window.RenderSize.Width + 0.5 &&
               bounds.Bottom <= window.RenderSize.Height + 0.5 &&
               element.InputHitTest(new Point(element.ActualWidth / 2, element.ActualHeight / 2)) is not null;
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static void CloseReminderCenter(ReminderCenterWindow? window)
    {
        if (window is null)
        {
            return;
        }

        try
        {
            window.ForceClose();
            WindowValidationInput.Pump(window.Dispatcher);
        }
        catch
        {
            // Cleanup best effort.
        }
    }

    private static void CloseWindow(Window? window)
    {
        if (window is null)
        {
            return;
        }

        try
        {
            if (window.IsVisible)
            {
                window.Close();
            }
            WindowValidationInput.Pump(window.Dispatcher);
        }
        catch
        {
            // Cleanup best effort.
        }
    }

    private static string? CreateCaptureDirectory()
    {
        var explicitDirectory = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_DWM_CHROME_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            var resolved = Path.GetFullPath(explicitDirectory);
            Directory.CreateDirectory(resolved);
            return resolved;
        }

        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        var directory = Path.Combine(
            Path.GetFullPath(sourceRoot),
            "logs",
            "ui-captures",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-dwm-chrome-324");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static Action<Window, string>? CreateCaptureWriter()
    {
        var directory = SharedCaptureDirectory.Value;
        if (directory is null)
        {
            return null;
        }

        return (window, label) =>
        {
            if (window.Content is not FrameworkElement root)
            {
                return;
            }

            TextOptions.SetTextRenderingMode(root, TextRenderingMode.Aliased);
            TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
            Settle(window);
            var dpi = VisualTreeHelper.GetDpi(root);
            var dpiX = dpi.PixelsPerInchX > 0 ? dpi.PixelsPerInchX : 96;
            var dpiY = dpi.PixelsPerInchY > 0 ? dpi.PixelsPerInchY : 96;
            var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth * dpiX / 96.0));
            var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight * dpiY / 96.0));
            var bitmap = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
            bitmap.Render(root);
            bitmap.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(
                Path.Combine(directory, label + ".png"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            encoder.Save(stream);
        };
    }

    private sealed class RecordingDwmHost(
        bool isSupportedOperatingSystem,
        Func<DwmWindowAttribute, int>? handler = null) : IDwmWindowChromeHost
    {
        public bool IsSupportedOperatingSystem { get; } = isSupportedOperatingSystem;

        public List<RecordedDwmCall> Calls { get; } = new();

        public int SetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attribute, uint value)
        {
            Calls.Add(new RecordedDwmCall(hwnd, attribute, value));
            return handler?.Invoke(attribute) ?? 0;
        }
    }

    private sealed record RecordedDwmCall(IntPtr Handle, DwmWindowAttribute Attribute, uint Value);

    private sealed record DiagnosticLogRow(
        string Level,
        string Event,
        int? HResult,
        string? ExceptionType);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
    private static extern uint GetWindowLong(IntPtr hwnd, int index);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out uint value, uint valueSize);
}
