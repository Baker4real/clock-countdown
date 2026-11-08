using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    // 焦点态实拍证据，不进门禁。参考帧捕获前会清掉键盘焦点，焦点描边从不入画，焦点相关的修改只能靠状态断言核对。
    // 这里在一次性窗口里沿两个工作区的 Tab 路径停在每个停靠点，把该处的放大裁图写进本次运行实拍证据目录的 focus\，
    // 供人工核对单层描边，不做像素断言。WPF 只在键盘是最近输入设备时创建 FocusVisualStyle 装饰层
    // （KeyboardNavigation.ShowFocusVisual），程序移动焦点不满足这一条，所以这一趟临时把最近输入设备设为键盘；
    // 装饰层画在 AdornerLayer 上，裁图因此取自整个窗口视觉，而不是窗口内容。失败只写 focus-evidence.error.txt，
    // 并在报告里记下原因，不影响其余断言。
    private static MainFocusEvidenceResult CaptureMainFocusEvidence(string assetRoot, string referenceCapturePath)
    {
        // 发布包模式不写实拍证据，参考帧路径为空，这里同样不请求。
        if (string.IsNullOrEmpty(referenceCapturePath))
        {
            return new MainFocusEvidenceResult(string.Empty, string.Empty);
        }

        var evidenceDirectory = Path.Combine(Path.GetDirectoryName(referenceCapturePath)!, "focus");
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-focus-evidence-{Guid.NewGuid():N}.settings.json");
        var inputManager = InputManager.Current;
        var previousDevice = inputManager.MostRecentInputDevice;
        var setMostRecentDevice = typeof(InputManager)
            .GetProperty(nameof(InputManager.MostRecentInputDevice))?
            .GetSetMethod(nonPublic: true);
        MainWindow? window = null;
        try
        {
            if (setMostRecentDevice is null)
            {
                throw new InvalidOperationException(
                    "WPF 未公开 InputManager.MostRecentInputDevice 的 setter，无法按键盘输入截取焦点态。");
            }

            Directory.CreateDirectory(evidenceDirectory);
            var referenceUtc = new DateTimeOffset(2026, 7, 29, 9, 20, 17, TimeSpan.FromHours(8)).ToUniversalTime();
            var store = new SettingsStore(clock: new ValidationClock(referenceUtc));
            store.Save(settingsPath, CreateMainReferenceSettings("Representative", referenceUtc));
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            using var calendar = new HolidayCalendar(assetRoot);
            var probe = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(settingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                Width = 864,
                Height = 1000,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000
            };
            window = probe;
            probe.Show();
            PumpDispatcher();
            setMostRecentDevice.Invoke(inputManager, new object?[] { Keyboard.PrimaryDevice });

            var entries = new List<object>();
            foreach (var workspaceKind in new[] { WorkspaceKind.Countdowns, WorkspaceKind.Notes })
            {
                probe.ShowWorkspace(workspaceKind);
                if (workspaceKind == WorkspaceKind.Countdowns)
                {
                    probe.ExpandCountdownEditorForValidation();
                }

                probe.Activate();
                PumpDispatcher();
                var expected = ExpectedMainWorkspaceTabPath(probe, workspaceKind);
                var captured = new HashSet<string>(StringComparer.Ordinal);
                TraverseMainWorkspaceTabPath(
                    probe,
                    expected[0],
                    expected[^1],
                    FocusNavigationDirection.Next,
                    (target, focused) =>
                    {
                        var stop = DescribeMainFocusedTarget(probe, target, focused);
                        if (captured.Add(stop))
                        {
                            entries.Add(SaveMainFocusCrop(probe, evidenceDirectory, $"{workspaceKind}-{stop}", target, focused));
                        }
                    });
            }

            var manifestPath = Path.Combine(evidenceDirectory, "focus-evidence.json");
            WriteNewBytes(
                manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schemaVersion = 1,
                        captureKind = "main-window keyboard focus crops along the Tab path; not gated",
                        entries
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            return new MainFocusEvidenceResult(manifestPath, string.Empty);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(evidenceDirectory);
            WriteCaptureFailureEvidence(evidenceDirectory, "focus-evidence", exception);
            return new MainFocusEvidenceResult(string.Empty, $"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            setMostRecentDevice?.Invoke(inputManager, new object?[] { previousDevice });
            window?.Close();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".diagnostic.jsonl");
        }
    }

    // #247：倒计时行关键状态实拍，不进门禁。一次性主窗口载入行几何探针的同一份夹具，按首选 864×1000 截取倒计时页
    // 四种排布（七行时滚动条在顶部与底部、百万天主项、到期主项）和便签页（次级动作对照），写进本次运行实拍证据目录的
    // countdown-rows\，供人工核对主阅读轴、辅助带与动作。失败只写 countdown-rows.error.txt，并在报告里记下原因，
    // 不影响其余断言。
    private static CountdownRowEvidenceResult CaptureCountdownRowStateEvidence(string assetRoot, string referenceCapturePath)
    {
        // 发布包模式不写实拍证据，参考帧路径为空，这里同样不请求。
        if (string.IsNullOrEmpty(referenceCapturePath))
        {
            return new CountdownRowEvidenceResult(string.Empty, string.Empty);
        }

        const int captureWidth = 864;
        const int captureHeight = 1000;
        var evidenceDirectory = Path.Combine(Path.GetDirectoryName(referenceCapturePath)!, "countdown-rows");
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-countdown-row-evidence-{Guid.NewGuid():N}.settings.json");
        MainWindow? window = null;
        try
        {
            Directory.CreateDirectory(evidenceDirectory);
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new ValidationClock(ValidationExercises.CountdownRowFixtureUtc)));
            var fixture = ValidationExercises.SeedCountdownRowStates(workspace);
            using var calendar = new HolidayCalendar(assetRoot);
            var probe = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(settingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                Width = captureWidth,
                Height = captureHeight,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000
            };
            window = probe;
            probe.Show();
            PumpDispatcher();
            if (probe.Content is not FrameworkElement root)
            {
                throw new InvalidOperationException("主窗口根视觉不可用于捕获。");
            }

            var entries = new List<object>();
            void Capture(string captureId, string state)
            {
                PumpDispatcher();
                ArrangeForReferenceCapture(root, captureWidth, captureHeight);
                ClearCaptureKeyboardFocus(root);
                WaitForMainReferenceMotion(probe, root);
                var path = Path.Combine(evidenceDirectory, $"{captureId}.png");
                var sha256 = WriteNewPng(path, CaptureOpaqueMainSurface(root, captureWidth, captureHeight));
                entries.Add(new { captureId, state, path, sha256 });
            }

            probe.ShowWorkspace(WorkspaceKind.Countdowns);
            Capture("countdown-rows-top", "七行，纵向滚动条在顶部：普通主项、30 字名称、五枚标签、999 天");
            probe.CountdownListRegionForValidation.ScrollToEnd();
            Capture("countdown-rows-bottom", "七行滚到底部：北京时区截止行、百万天、已到期次项");
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = [fixture.LongestTagId] });
            Capture("countdown-rows-longest-primary", "「长期」筛选：百万天主项与已到期次项，无滚动条");
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = [fixture.ExpiredTagId] });
            Capture("countdown-rows-expired-primary", "「收尾」筛选：已到期主项");
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            probe.ShowWorkspace(WorkspaceKind.Notes);
            Capture("note-row-actions", "便签行：与倒计时行同一套编辑/删除");

            var manifestPath = Path.Combine(evidenceDirectory, "countdown-rows.json");
            WriteNewBytes(
                manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schemaVersion = 1,
                        captureKind = "main-window countdown row states at 864x1000 for #247; not gated",
                        referenceUtc = ValidationExercises.CountdownRowFixtureUtc,
                        entries
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            return new CountdownRowEvidenceResult(manifestPath, string.Empty);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(evidenceDirectory);
            WriteCaptureFailureEvidence(evidenceDirectory, "countdown-rows", exception);
            return new CountdownRowEvidenceResult(string.Empty, $"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            window?.Close();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".diagnostic.jsonl");
        }
    }

    // #249：便签卡关键状态实拍，不进门禁。一次性主窗口载入卡片几何探针的同一份夹具，按首选 864×1000 截取便签页
    // 六种排布（未完成五张时滚动条在顶部与底部、「复盘」搜索、多选两张与批量操作条、提醒来源强调、已完成段），
    // 写进本次运行实拍证据目录的 note-rows\，供人工核对内容列、标签邻近与右上动作槽。提醒来源一张保留打开来源后
    // 编辑按钮的真实焦点，其余清掉焦点。失败只写 note-rows.error.txt，并在报告里记下原因，不影响其余断言。
    private static NoteRowEvidenceResult CaptureNoteRowStateEvidence(string assetRoot, string referenceCapturePath)
    {
        // 发布包模式不写实拍证据，参考帧路径为空，这里同样不请求。
        if (string.IsNullOrEmpty(referenceCapturePath))
        {
            return new NoteRowEvidenceResult(string.Empty, string.Empty);
        }

        const int captureWidth = 864;
        const int captureHeight = 1000;
        var evidenceDirectory = Path.Combine(Path.GetDirectoryName(referenceCapturePath)!, "note-rows");
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-note-row-evidence-{Guid.NewGuid():N}.settings.json");
        MainWindow? window = null;
        try
        {
            Directory.CreateDirectory(evidenceDirectory);
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new ValidationClock(ValidationExercises.NoteRowFixtureUtc)));
            var fixture = ValidationExercises.SeedNoteRowStates(workspace);
            using var calendar = new HolidayCalendar(assetRoot);
            var probe = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(settingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                Width = captureWidth,
                Height = captureHeight,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000
            };
            window = probe;
            probe.Show();
            PumpDispatcher();
            if (probe.Content is not FrameworkElement root)
            {
                throw new InvalidOperationException("主窗口根视觉不可用于捕获。");
            }

            var entries = new List<object>();
            void Capture(string captureId, string state, bool keepFocus = false)
            {
                PumpDispatcher();
                ArrangeForReferenceCapture(root, captureWidth, captureHeight);
                if (!keepFocus)
                {
                    ClearCaptureKeyboardFocus(root);
                }

                WaitForMainReferenceMotion(probe, root);
                var path = Path.Combine(evidenceDirectory, $"{captureId}.png");
                var sha256 = WriteNewPng(path, CaptureOpaqueMainSurface(root, captureWidth, captureHeight));
                entries.Add(new { captureId, state, path, sha256 });
            }

            var parts = probe.NoteListPartsForValidation;
            probe.ShowWorkspace(WorkspaceKind.Notes);
            Capture("note-rows-top", "未完成五张，纵向滚动条在顶部：无标签短正文、140 字长正文、五枚标签与两条提醒");
            parts.ListRegion.ScrollToEnd();
            Capture("note-rows-bottom", "未完成滚到底部：每 15 分钟提醒的来源卡与单标签短卡");
            parts.ListRegion.ScrollToHome();
            parts.Search.Text = fixture.SearchTerm;
            Capture("note-rows-search", "「复盘」搜索：长正文与五标签两张");
            parts.Search.Clear();
            PumpDispatcher();
            var chosen = Descendants<NoteSelectionCard>(parts.ActiveRows).Where(card => card.IsVisible).Take(2).ToArray();
            foreach (var card in chosen)
            {
                Keyboard.Focus(card);
                WindowValidationInput.RaisePreviewKey(card, Key.Space, ModifierKeys.Control);
            }

            PumpDispatcher();
            if (parts.SelectedCount.Text != "已选择 2 条")
            {
                throw new InvalidOperationException("#249 多选实拍必须真实选中两张卡并显示批量操作条。");
            }

            Capture("note-rows-selected", "多选两张：批量操作条与选中卡底色，卡内几何不变");
            Keyboard.Focus(chosen[0]);
            WindowValidationInput.RaisePreviewKey(chosen[0], Key.Escape);
            if (!probe.OpenReminderSource(ReminderSourceKind.Note, fixture.ReminderSourceNoteId))
            {
                throw new InvalidOperationException("#249 提醒来源实拍必须真实打开来源便签。");
            }

            Capture("note-rows-reminder-source", "打开提醒来源：来源卡 2 DIP ActiveBrush 强调、编辑按钮获焦", keepFocus: true);
            parts.CompletedSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture("note-rows-completed", "已完成段：删除线、弱化正文与暂停的提醒摘要");

            var manifestPath = Path.Combine(evidenceDirectory, "note-rows.json");
            WriteNewBytes(
                manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schemaVersion = 1,
                        captureKind = "main-window note card states at 864x1000 for #249; not gated",
                        referenceUtc = ValidationExercises.NoteRowFixtureUtc,
                        entries
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            return new NoteRowEvidenceResult(manifestPath, string.Empty);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(evidenceDirectory);
            WriteCaptureFailureEvidence(evidenceDirectory, "note-rows", exception);
            return new NoteRowEvidenceResult(string.Empty, $"{exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            window?.Close();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".diagnostic.jsonl");
        }
    }

    // #250：共享表面与次级窗口实拍，不进门禁。一次性窗口载入主窗口参考夹具（Representative 为普通、Stress 为长内容），
    // 截取管理窗口、悬浮窗、提醒中心、规则编辑器与共享弹层（设置、筛选、标签管理、标签选择、便签「…」菜单、删除确认）
    // 在普通、长内容、错误/禁用、键盘焦点与系统高对比度下的同版画面，连同 surfaces.json 写进本次运行实拍证据目录的
    // surfaces\。错误与禁用走真实入口（空名称保存、负月薪保存、空正文快速新增、送达失败、自定义稍后缺时间、规则无星期
    // 且间隔为 0 时保存、未送达的提醒）；焦点态把键盘焦点交给目标控件并点亮模板焦点环；高对比度在验证作用域里注入
    // 经典「高对比黑」系统色，不读本机设置。全部按 96 DPI 渲染（1 px = 1 DIP）。单帧失败写 <captureId>.error.txt 并记进
    // SurfaceEvidenceError，其余帧照常落盘；圆角结论以 SharedSurfaceRadii 探针为准，截图供人工核对层级、边界与焦点。
    private static SurfaceEvidenceResult CaptureSurfaceStateEvidence(string assetRoot, string referenceCapturePath)
    {
        // 发布包模式不写实拍证据，参考帧路径为空，这里同样不请求。
        if (string.IsNullOrEmpty(referenceCapturePath))
        {
            return new SurfaceEvidenceResult(string.Empty, string.Empty);
        }

        const int mainWidth = 864;
        const int mainHeight = 1000;
        var evidenceDirectory = Path.Combine(Path.GetDirectoryName(referenceCapturePath)!, "surfaces");
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-surface-evidence-{Guid.NewGuid():N}.settings.json");
        var stressSettingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-surface-evidence-stress-{Guid.NewGuid():N}.settings.json");
        var holidayRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-surface-evidence-holidays-{Guid.NewGuid():N}");
        var holidayDirectory = Path.Combine(holidayRoot, "holiday-cache");
        var holidayPath = string.Empty;
        // 经典「高对比黑」：窗口与控件黑底白字，高亮青底黑字，禁用文字绿。
        var highContrast = new ApplicationTheme.SystemContrastPalette(
            Colors.Black,
            Colors.White,
            Colors.Black,
            Colors.White,
            Color.FromRgb(0x1A, 0xEB, 0xFF),
            Colors.Black,
            Color.FromRgb(0x3F, 0xF2, 0x3F));
        var entries = new List<object>();
        var failures = new List<string>();
        var windows = new List<Window>();

        void Capture(string captureId, string surface, string state, Func<BitmapSource> render, Func<DependencyObject>? errorRoot = null)
        {
            try
            {
                var bitmap = render();
                var path = Path.Combine(evidenceDirectory, $"{captureId}.png");
                var sha256 = WriteNewPng(path, bitmap);
                entries.Add(new
                {
                    captureId,
                    surface,
                    state,
                    highContrast = ApplicationTheme.HighContrastEnabled,
                    pixelWidth = bitmap.PixelWidth,
                    pixelHeight = bitmap.PixelHeight,
                    visibleErrors = errorRoot is null ? Array.Empty<string>() : VisibleErrorTexts(errorRoot()),
                    path,
                    sha256
                });
            }
            catch (Exception exception)
            {
                failures.Add($"{captureId}: {exception.GetType().Name}: {exception.Message}");
                WriteCaptureFailureEvidence(evidenceDirectory, captureId, exception);
            }
        }

        void HighContrast(string group, Action captures)
        {
            try
            {
                using var preference = ApplicationTheme.OverrideHighContrastForValidation(true);
                preference.ChangeColors(highContrast);
                PumpDispatcher();
                captures();
            }
            catch (Exception exception)
            {
                failures.Add($"{group}-high-contrast: {exception.GetType().Name}: {exception.Message}");
                WriteCaptureFailureEvidence(evidenceDirectory, $"{group}-high-contrast", exception);
            }
            finally
            {
                PumpDispatcher();
            }
        }

        T Open<T>(T window) where T : Window
        {
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20_000;
            window.Top = -20_000;
            windows.Add(window);
            window.Show();
            PumpDispatcher();
            return window;
        }

        BitmapSource MainFrame(MainWindow window, bool keepFocus = false)
        {
            if (window.Content is not FrameworkElement root)
            {
                throw new InvalidOperationException("主窗口根视觉不可用于捕获。");
            }

            PumpDispatcher();
            ArrangeForReferenceCapture(root, mainWidth, mainHeight);
            if (!keepFocus)
            {
                ClearCaptureKeyboardFocus(root);
            }

            WaitForMainReferenceMotion(window, root);
            return CaptureOpaqueMainSurface(root, mainWidth, mainHeight);
        }

        Color OpaqueBackdrop() =>
            Application.Current.FindResource("BackgroundBrush") is SolidColorBrush background &&
            background.Color.A == byte.MaxValue
                ? background.Color
                : throw new InvalidOperationException("实拍底色必须是不透明主题画刷。");

        BitmapSource WindowFrame(Window window, FrameworkElement? root = null)
        {
            PumpDispatcher();
            root ??= window.Content as FrameworkElement ??
                throw new InvalidOperationException($"{window.GetType().Name} 根视觉不可用于捕获。");
            return CompositeOverOpaqueBackground(CaptureVisual(root), OpaqueBackdrop());
        }

        // 弹层单独渲染整块表面：叠回宿主窗口时会被窗口右缘裁掉（真实弹层是独立顶层窗口，可越出宿主）。
        // 宿主上下文看同组的窗口帧与参考基线的筛选打开帧。
        BitmapSource PopupFrame(Popup? candidate)
        {
            PumpDispatcher();
            if (candidate is not { IsOpen: true, Child: FrameworkElement child } popup)
            {
                throw new InvalidOperationException("弹层实拍要求 Popup 已打开且有 Child。");
            }

            if (!WaitForRenderedCaptureState(child, () =>
                    popup.IsOpen && !PopupMotionBehavior.IsClosing(popup) &&
                    child.Opacity == 1 && child.RenderTransform.Value.IsIdentity))
            {
                throw new InvalidOperationException("弹层未在 300ms 内完成入场。");
            }

            return CompositeOverOpaqueBackground(CaptureVisual(child), OpaqueBackdrop());
        }

        void FocusFor(Window window, Control target)
        {
            window.Activate();
            Keyboard.Focus(target);
            FocusVisualState.SetIsKeyboardFocusVisible(target, true);
            PumpDispatcher();
            if (!target.IsKeyboardFocusWithin)
            {
                throw new InvalidOperationException(
                    $"焦点态实拍要求「{AutomationProperties.GetName(target)}」真实获得键盘焦点。");
            }
        }

        // 长内容帧滚到指定正文：上下各留一段上下文，让所在卡片的标签、提醒摘要与日期分组一同入画。
        // 先跑完窗口自己排队的渲染（悬浮窗重建行后在 Loaded 优先级把视口恢复到切换前的位置），再滚动。
        void BringTextIntoView(Window window, string text)
        {
            PumpDispatcher();
            var target = Descendants<TextBlock>(window).FirstOrDefault(block =>
                    block.IsVisible && string.Equals(block.Text, text, StringComparison.Ordinal)) ??
                throw new InvalidOperationException($"长内容实拍找不到正文「{text[..Math.Min(12, text.Length)]}…」。");
            target.BringIntoView(new Rect(0, -48, target.ActualWidth, target.ActualHeight + 96));
            PumpDispatcher();
        }

        try
        {
            Directory.CreateDirectory(evidenceDirectory);
            var referenceUtc = new DateTimeOffset(2026, 7, 29, 9, 20, 17, TimeSpan.FromHours(8)).ToUniversalTime();
            var store = new SettingsStore(clock: new ValidationClock(referenceUtc));
            var representative = CreateMainReferenceSettings("Representative", referenceUtc);
            var stress = CreateMainReferenceSettings("Stress", referenceUtc);
            store.Save(settingsPath, representative);
            store.Save(stressSettingsPath, stress);
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            var stressWorkspace = DesktopWorkspace.Load(stressSettingsPath, store);
            // 节假日日历与悬浮参考帧同法：临时缓存里放参考年份全年工作日表（周一到周五为工作日），
            // 不联网、不写应用目录；悬浮窗因此画出日历就绪后的「距离下班」与收入行，而不是加载中。
            var holidayYear = ChinaStandardTime.ToBeijingTime(referenceUtc).Year;
            var holidayDays = Enumerable.Range(0, DateTime.IsLeapYear(holidayYear) ? 366 : 365)
                .Select(offset => new DateTime(holidayYear, 1, 1).AddDays(offset))
                .Select(day => new
                {
                    date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    is_workday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                })
                .ToArray();
            Directory.CreateDirectory(holidayDirectory);
            holidayPath = Path.Combine(holidayDirectory, $"{holidayYear}.json");
            File.WriteAllText(holidayPath, JsonSerializer.Serialize(new { days = holidayDays }));
            using var calendar = new HolidayCalendar(holidayRoot);
            if (!calendar.RefreshAsync(referenceUtc, CancellationToken.None).GetAwaiter().GetResult())
            {
                throw new InvalidOperationException($"实拍节假日日历未能载入 {holidayYear} 年临时缓存：{calendar.StatusText}");
            }

            // 管理窗口与共享弹层（Representative：三条倒计时、三张便签、三枚标签）。
            var main = Open(new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(settingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                Width = mainWidth,
                Height = mainHeight
            });
            var parts = main.SurfaceEvidencePartsForValidation;
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            Capture("main-countdowns-normal", "管理窗口", "倒计时页：三条倒计时、标签芯片、收起的新增区与状态栏", () => MainFrame(main));
            main.ShowWorkspace(WorkspaceKind.Notes);
            Capture("main-notes-normal", "管理窗口", "便签页：三张便签、日期分组、完成勾选与新增区", () => MainFrame(main));
            parts.Filter.IsOpen = true;
            Capture("popup-filter", "筛选弹层", "便签页打开筛选：标签匹配、状态与日期", () => PopupFrame(parts.Filter));
            parts.Filter.IsOpen = false;
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            parts.TagManager.IsOpen = true;
            Capture("popup-tag-manager", "标签管理弹层", "倒计时页打开标签管理：三枚标签与新建", () => PopupFrame(parts.TagManager));
            parts.TagManager.IsOpen = false;
            parts.Preferences.IsOpen = true;
            Capture("popup-preferences", "设置弹层", "倒计时页打开设置：月薪、上下班时间、显示精度与保存", () => PopupFrame(parts.Preferences));
            parts.Preferences.IsOpen = false;
            PumpDispatcher();
            HighContrast("main", () =>
            {
                main.ShowWorkspace(WorkspaceKind.Countdowns);
                Capture("main-countdowns-high-contrast", "管理窗口", "系统高对比度：倒计时页", () => MainFrame(main));
                main.ShowWorkspace(WorkspaceKind.Notes);
                Capture("main-notes-high-contrast", "管理窗口", "系统高对比度：便签页", () => MainFrame(main));
                parts.Filter.IsOpen = true;
                Capture("popup-filter-high-contrast", "筛选弹层", "系统高对比度：打开筛选", () => PopupFrame(parts.Filter));
                parts.Filter.IsOpen = false;
                main.ShowWorkspace(WorkspaceKind.Countdowns);
                parts.Preferences.IsOpen = true;
                Capture("popup-preferences-high-contrast", "设置弹层", "系统高对比度：打开设置", () => PopupFrame(parts.Preferences));
                parts.Preferences.IsOpen = false;
                PumpDispatcher();
            });
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            main.ExpandCountdownEditorForValidation();
            parts.CountdownTags.IsPopupOpen = true;
            PumpDispatcher();
            Capture("popup-tag-selector", "标签选择弹层", "展开新增区后打开标签选择：搜索框与三枚标签", () => PopupFrame(parts.CountdownTags.Popup));
            parts.CountdownTags.IsPopupOpen = false;
            PumpDispatcher();
            Capture("main-countdowns-focus", "管理窗口", "展开新增区，键盘焦点在「＋ 添加倒计时」主按钮", () =>
            {
                FocusFor(main, parts.SaveCountdown);
                return MainFrame(main, keepFocus: true);
            });
            main.ShowCountdownFieldErrorForValidation();
            Capture(
                "main-countdowns-error",
                "管理窗口",
                "名称留空后添加：名称下方字段错误与表单汇总行，焦点回到名称输入框",
                () => MainFrame(main, keepFocus: true),
                () => main);
            // 设置错误放在主窗各帧之后：保存失败会把同一错误写进状态栏，排在前面会混进别的帧。
            main.ShowPreferencesSalaryErrorForValidation();
            Capture(
                "popup-preferences-error",
                "设置弹层",
                "月薪填 -500 后保存：字段下方错误，焦点回到月薪输入框",
                () => PopupFrame(parts.Preferences),
                () => parts.Preferences.Child ?? throw new InvalidOperationException("设置弹层没有表面。"));
            parts.Preferences.IsOpen = false;
            PumpDispatcher();

            // 共享删除确认：长便签正文。
            var dialog = Open(new ConfirmationDialog(main, "删除便签", DeleteConfirmationCopy.For(stress.Notes[0].Text)));
            FrameworkElement DialogRoot() => VisualTreeHelper.GetChildrenCount(dialog) > 0 &&
                VisualTreeHelper.GetChild(dialog, 0) is FrameworkElement dialogRoot
                    ? dialogRoot
                    : throw new InvalidOperationException("确认框模板根视觉不可用于捕获。");
            Capture("confirmation-delete-long", "删除确认", "长便签正文删除确认：标题、折行正文与「取消 / 确认删除」", () => WindowFrame(dialog, DialogRoot()));
            HighContrast("confirmation", () =>
                Capture("confirmation-delete-high-contrast", "删除确认", "系统高对比度：长便签删除确认", () => WindowFrame(dialog, DialogRoot())));
            Capture("confirmation-focus", "删除确认", "键盘焦点在「确认删除」", () =>
            {
                FocusFor(dialog, dialog.ConfirmButton);
                return WindowFrame(dialog, DialogRoot());
            });

            // 管理窗口长内容（Stress：九条倒计时含 30 字标题与已到期、四张未完成便签含长正文）。
            var stressMain = Open(new MainWindow(
                stressWorkspace,
                calendar,
                new DiagnosticLogger(stressSettingsPath + ".diagnostic.jsonl"),
                assetRoot)
            {
                Width = mainWidth,
                Height = mainHeight
            });
            stressMain.ShowWorkspace(WorkspaceKind.Countdowns);
            Capture("main-countdowns-long", "管理窗口", "长内容：九条倒计时（30 字标题、已到期），纵向滚动条", () => MainFrame(stressMain));
            stressMain.ShowWorkspace(WorkspaceKind.Notes);
            Capture("main-notes-long", "管理窗口", "长内容：四张未完成便签，滚到长正文便签（两枚标签、每 15 分钟提醒、昨天分组）", () =>
            {
                BringTextIntoView(stressMain, stress.Notes[0].Text);
                return MainFrame(stressMain);
            });

            // 悬浮窗（Representative）。
            var floating = Open(new FloatingWindow(workspace, calendar, main, assetRoot));
            floating.UpdateTime(referenceUtc);
            floating.ShowWorkspace(WorkspaceKind.Countdowns);
            Capture("floating-countdowns-normal", "悬浮窗", "倒计时：距离下班主项与三条倒计时", () => WindowFrame(floating));
            floating.ShowWorkspace(WorkspaceKind.Notes);
            Capture("floating-notes-normal", "悬浮窗", "便签：三张便签卡", () => WindowFrame(floating));
            HighContrast("floating", () =>
            {
                floating.ShowWorkspace(WorkspaceKind.Countdowns);
                Capture("floating-countdowns-high-contrast", "悬浮窗", "系统高对比度：倒计时", () => WindowFrame(floating));
                floating.ShowWorkspace(WorkspaceKind.Notes);
                Capture("floating-notes-high-contrast", "悬浮窗", "系统高对比度：便签", () => WindowFrame(floating));
            });
            var overflowMenu = floating.OpenFirstNoteOverflowMenuForValidation();
            Capture("floating-note-overflow-menu", "便签「…」菜单", "首张便签打开「…」：编辑 / 删除", () => PopupFrame(overflowMenu));
            if (overflowMenu is not null)
            {
                overflowMenu.IsOpen = false;
                PumpDispatcher();
            }

            Capture("floating-focus", "悬浮窗", "键盘焦点在快速新增入口", () =>
            {
                FocusFor(floating, floating.QuickAddToggleForValidation);
                return WindowFrame(floating);
            });
            floating.ShowQuickAddTextErrorForValidation();
            Capture(
                "floating-quick-add-error",
                "悬浮窗",
                "快速新增正文留空后添加：正文下方字段错误，焦点回到正文输入框",
                () => WindowFrame(floating),
                () => floating);
            floating.CollapseQuickAddForValidation();

            var stressFloating = Open(new FloatingWindow(stressWorkspace, calendar, stressMain, assetRoot));
            stressFloating.UpdateTime(referenceUtc);
            stressFloating.ShowWorkspace(WorkspaceKind.Countdowns);
            Capture("floating-countdowns-long", "悬浮窗", "长内容：九条倒计时（30 字标题、已到期）", () => WindowFrame(stressFloating));
            stressFloating.ShowWorkspace(WorkspaceKind.Notes);
            Capture("floating-notes-long", "悬浮窗", "长内容：四张便签，滚到长正文便签（折行、下次提醒、日期印记）", () =>
            {
                BringTextIntoView(stressFloating, stress.Notes[0].Text);
                return WindowFrame(stressFloating);
            });

            // 提醒中心：一条便签提醒、一条倒计时提醒。
            var noteKey = new ActiveReminderOccurrenceKey("surface-evidence-note-rule", referenceUtc.AddMinutes(-5));
            var countdownKey = new ActiveReminderOccurrenceKey("surface-evidence-countdown-rule", referenceUtc.AddMinutes(-2));
            var center = Open(new ReminderCenterWindow());
            center.AddEntry(new ReminderCenterItem(noteKey, representative.Notes[0], 0, 0));
            center.AddEntry(new ReminderCenterItem(countdownKey, new CountdownReminderSource(representative.Countdowns[0]), 1, 0));
            center.UpdateTime(referenceUtc);
            PumpDispatcher();
            Capture("reminder-center-disabled", "提醒中心", "送达状态未落盘：知道了 / 稍后提醒 / 完成便签禁用，打开来源可用", () => WindowFrame(center));
            center.SetDeliveryReady(noteKey);
            center.SetDeliveryReady(countdownKey);
            PumpDispatcher();
            Capture("reminder-center-normal", "提醒中心", "两条已送达提醒：来源、正文、相对时间与四个处置", () => WindowFrame(center));
            HighContrast("reminder-center", () =>
                Capture("reminder-center-high-contrast", "提醒中心", "系统高对比度：两条已送达提醒", () => WindowFrame(center)));
            Capture("reminder-center-focus", "提醒中心", "键盘焦点在首条「知道了」", () =>
            {
                FocusFor(center, Descendants<Button>(center).First(button =>
                    button.IsEnabled &&
                    AutomationProperties.GetName(button).StartsWith("知道了：", StringComparison.Ordinal)));
                return WindowFrame(center);
            });
            center.ShowDeliveryFailure(countdownKey);
            center.RequestSnooze(noteKey, ReminderSnoozeOption.Custom);
            Keyboard.ClearFocus();
            PumpDispatcher();
            Capture(
                "reminder-center-error",
                "提醒中心",
                "便签条目选自定义稍后但缺日期时间：自定义日期与时间控件展开，错误紧邻其下",
                () => WindowFrame(center),
                () => center);
            Capture(
                "reminder-center-delivery-failure",
                "提醒中心",
                "滚到倒计时条目：送达失败，知道了 / 稍后提醒禁用，打开来源仍可用",
                () =>
                {
                    Descendants<Border>(center)
                        .Single(border => border.Tag is ActiveReminderOccurrenceKey key && key == countdownKey)
                        .BringIntoView();
                    PumpDispatcher();
                    return WindowFrame(center);
                },
                () => center);

            var longCenter = Open(new ReminderCenterWindow());
            var longKeys = new[]
            {
                new ActiveReminderOccurrenceKey("surface-evidence-long-note", referenceUtc.AddMinutes(-30)),
                new ActiveReminderOccurrenceKey("surface-evidence-long-countdown", referenceUtc.AddMinutes(-12)),
                new ActiveReminderOccurrenceKey("surface-evidence-long-note-2", referenceUtc.AddMinutes(-1))
            };
            longCenter.AddEntry(new ReminderCenterItem(longKeys[0], stress.Notes[0], 0, 0));
            longCenter.AddEntry(new ReminderCenterItem(longKeys[1], new CountdownReminderSource(stress.Countdowns[0]), 1, 0));
            longCenter.AddEntry(new ReminderCenterItem(longKeys[2], stress.Notes[2], 2, 0));
            foreach (var key in longKeys)
            {
                longCenter.SetDeliveryReady(key);
            }

            longCenter.UpdateTime(referenceUtc);
            PumpDispatcher();
            Capture("reminder-center-long", "提醒中心", "长内容：三条提醒（长便签正文、30 字倒计时标题），纵向滚动", () => WindowFrame(longCenter));

            // 规则编辑器：提醒一次、固定间隔、每周、每年四条规则。
            var ruleStart = DateOnly.FromDateTime(ChinaStandardTime.ToBeijingTime(referenceUtc).Date).AddDays(1);
            var weekly = new CalendarReminderRule(
                "surface-evidence-weekly",
                CalendarFrequency.Weekly,
                1,
                ruleStart,
                new TimeOnly(9, 0),
                new WeeklyCalendarSelector([DayOfWeek.Monday, DayOfWeek.Thursday]));
            var yearly = new CalendarReminderRule(
                "surface-evidence-yearly",
                CalendarFrequency.Yearly,
                1,
                ruleStart,
                new TimeOnly(9, 30),
                new YearlyCalendarSelector(8, 15, false));
            ReminderRule[] rules =
            [
                new AbsoluteOnceReminderRule("surface-evidence-once", referenceUtc.AddDays(1)),
                new FixedIntervalReminderRule("surface-evidence-interval", referenceUtc.AddHours(2), new ReminderInterval(30)),
                weekly,
                yearly
            ];
            var editor = Open(new ReminderRulesEditorWindow(rules, new ValidationClock(referenceUtc)));
            Button EditButton(CalendarReminderRule rule) => Descendants<Button>(editor).Single(button =>
                string.Equals(
                    AutomationProperties.GetName(button),
                    $"编辑规则 {ReminderRulesEditorViewModel.Describe(rule)}",
                    StringComparison.Ordinal));
            void Invoke(Button button)
            {
                if (!WindowValidationInput.InvokeButton(button))
                {
                    throw new InvalidOperationException($"规则编辑器按钮「{AutomationProperties.GetName(button)}」不支持真实 Invoke。");
                }

                PumpDispatcher();
            }

            Capture("rules-editor-normal", "提醒规则编辑器", "四条规则列表与新增规则表单", () => WindowFrame(editor));
            Invoke(EditButton(weekly));
            Capture("rules-editor-calendar-weekly", "提醒规则编辑器", "编辑每周规则：日历频率、重复间隔与星期复选", () => WindowFrame(editor));
            HighContrast("rules-editor", () =>
                Capture("rules-editor-high-contrast", "提醒规则编辑器", "系统高对比度：编辑每周规则", () => WindowFrame(editor)));
            Capture("rules-editor-focus", "提醒规则编辑器", "键盘焦点在「保存规则」", () =>
            {
                FocusFor(editor, ElementByAutomationId<Button>(editor, "SaveReminderRuleButton"));
                return WindowFrame(editor);
            });
            foreach (var weekday in Descendants<CheckBox>(editor).Where(check =>
                         (AutomationProperties.GetAutomationId(check) ?? string.Empty)
                         .StartsWith("ReminderRuleCalendarWeekday", StringComparison.Ordinal)))
            {
                weekday.IsChecked = false;
            }

            ElementByAutomationId<TextBox>(editor, "ReminderRuleCalendarInterval").Text = "0";
            PumpDispatcher();
            Invoke(ElementByAutomationId<Button>(editor, "SaveReminderRuleButton"));
            Capture(
                "rules-editor-error",
                "提醒规则编辑器",
                "取消全部星期、重复间隔填 0 后保存：字段错误行",
                () => WindowFrame(editor),
                () => editor);
            Invoke(ElementByAutomationId<Button>(editor, "CancelReminderRuleEditButton"));
            Invoke(EditButton(yearly));
            Capture("rules-editor-calendar-yearly", "提醒规则编辑器", "编辑每年规则：月份与日期选择", () => WindowFrame(editor));

            // 长内容取规则上限八条：四条提醒一次，加固定间隔、每周、每月、每年各一条。
            var longRules = Enumerable.Range(0, 4)
                .Select(index => (ReminderRule)new AbsoluteOnceReminderRule(
                    $"surface-evidence-long-once-{index}",
                    referenceUtc.AddDays(index + 1).AddHours(index)))
                .Append(new FixedIntervalReminderRule("surface-evidence-long-interval", referenceUtc.AddHours(1), new ReminderInterval(15)))
                .Append(weekly with { Id = "surface-evidence-long-weekly" })
                .Append(new CalendarReminderRule(
                    "surface-evidence-long-monthly",
                    CalendarFrequency.Monthly,
                    1,
                    ruleStart,
                    new TimeOnly(18, 0),
                    new MonthlyCalendarSelector(null, true)))
                .Append(yearly with { Id = "surface-evidence-long-yearly" })
                .ToArray();
            var longEditor = Open(new ReminderRulesEditorWindow(longRules, new ValidationClock(referenceUtc)));
            Capture("rules-editor-long", "提醒规则编辑器", "长内容：八条规则达到上限，列表纵向滚动", () => WindowFrame(longEditor));

            var manifestPath = Path.Combine(evidenceDirectory, "surfaces.json");
            WriteNewBytes(
                manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schemaVersion = 1,
                        captureKind = "shared surfaces and secondary windows at 96 DPI for #250; not gated",
                        referenceUtc,
                        highContrastPalette = new
                        {
                            window = highContrast.Window.ToString(),
                            windowText = highContrast.WindowText.ToString(),
                            control = highContrast.Control.ToString(),
                            controlText = highContrast.ControlText.ToString(),
                            highlight = highContrast.Highlight.ToString(),
                            highlightText = highContrast.HighlightText.ToString(),
                            grayText = highContrast.GrayText.ToString()
                        },
                        entries,
                        failures
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            return new SurfaceEvidenceResult(manifestPath, string.Join("；", failures));
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(evidenceDirectory);
            WriteCaptureFailureEvidence(evidenceDirectory, "surfaces", exception);
            failures.Add($"{exception.GetType().Name}: {exception.Message}");
            return new SurfaceEvidenceResult(string.Empty, string.Join("；", failures));
        }
        finally
        {
            foreach (var window in Enumerable.Reverse(windows))
            {
                switch (window)
                {
                    case ReminderCenterWindow reminderCenter:
                        reminderCenter.ForceClose();
                        break;
                    case ConfirmationDialog confirmation:
                        confirmation.Close();
                        break;
                    default:
                        window.Close();
                        break;
                }
            }

            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteSettingsFiles(stressSettingsPath);
            TryDeleteFile(settingsPath + ".diagnostic.jsonl");
            TryDeleteFile(stressSettingsPath + ".diagnostic.jsonl");
            if (!string.IsNullOrEmpty(holidayPath))
            {
                TryDeleteFile(holidayPath);
            }

            foreach (var directory in new[] { holidayDirectory, holidayRoot })
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 临时目录清理失败不影响证据结论。
                }
            }
        }
    }

    private static T ElementByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject =>
        Descendants<T>(root).Single(element => string.Equals(
            AutomationProperties.GetAutomationId(element),
            automationId,
            StringComparison.Ordinal));

    // 实拍清单里记下窗口里未折叠的错误文字，供人工核对错误态确实走到了真实入口。按危险色取（悬浮窗快速新增的
    // 局部错误画刷与 DangerBrush 同色），按钮里的危险色文字（「删除」）不算错误。
    private static string[] VisibleErrorTexts(DependencyObject root)
    {
        var danger = (Application.Current.TryFindResource("DangerBrush") as SolidColorBrush)?.Color;
        return Descendants<TextBlock>(root)
            .Where(text => text.IsVisible &&
                !string.IsNullOrWhiteSpace(text.Text) &&
                text.Foreground is SolidColorBrush foreground &&
                foreground.Color == danger &&
                !HasButtonAncestor(text))
            .Select(text => text.Text)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasButtonAncestor(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private static object SaveMainFocusCrop(
        MainWindow window,
        string evidenceDirectory,
        string captureId,
        MainTabTarget target,
        DependencyObject focused)
    {
        // 列表区的焦点落在行内控件上，裁那个控件；其余停靠点裁整个控件（日期框的焦点在打开日历按钮上，裁整个日期框）。
        var owner = target.Id is "CountdownListRegion" or "NotesListRegion" && focused is FrameworkElement focusedElement
            ? focusedElement
            : target.Element;
        var dpi = VisualTreeHelper.GetDpi(window);
        var frame = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        frame.Render(window);
        var bounds = owner.TransformToAncestor(window).TransformBounds(new Rect(owner.RenderSize));
        bounds.Inflate(8, 8);
        var left = Math.Clamp((int)Math.Floor(bounds.Left * dpi.DpiScaleX), 0, frame.PixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(bounds.Top * dpi.DpiScaleY), 0, frame.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right * dpi.DpiScaleX), left + 1, frame.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * dpi.DpiScaleY), top + 1, frame.PixelHeight);
        var crop = new CroppedBitmap(frame, new Int32Rect(left, top, right - left, bottom - top));
        // 整数倍最近邻放大，1 px 描边保持原样像素；长边约束在 1600 px 内。
        var magnification = Math.Clamp(1600 / Math.Max(crop.PixelWidth, crop.PixelHeight), 1, 4);
        var path = Path.Combine(evidenceDirectory, $"{captureId}.png");
        var sha256 = WriteNewPng(path, MagnifyNearestNeighbor(crop, magnification));
        return new
        {
            captureId,
            focusedType = focused.GetType().Name,
            keyboardIsMostRecentInputDevice = InputManager.Current.MostRecentInputDevice is KeyboardDevice,
            // 两项同时为真：按约定自己画焦点的控件又挂了装饰层，可能叠成两层描边，也可能是装饰层顶替了没点亮的模板焦点。
            templateDrawsFocus = focused is Control focusedControl && FocusVisualState.DrawsOwnKeyboardFocus(focusedControl),
            focusVisualAdorner = focused is UIElement focusedVisual &&
                AdornerLayer.GetAdornerLayer(focusedVisual)?.GetAdorners(focusedVisual)?
                    .Any(adorner => adorner.GetType().Name == "FocusVisualAdorner") == true,
            cropDip = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height },
            dpiScale = new { x = dpi.DpiScaleX, y = dpi.DpiScaleY },
            magnification,
            path,
            sha256
        };
    }

    private static BitmapSource MagnifyNearestNeighbor(BitmapSource source, int factor)
    {
        var (pixels, stride) = GetBgra32Pixels(source);
        var width = source.PixelWidth * factor;
        var height = source.PixelHeight * factor;
        var magnified = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Buffer.BlockCopy(pixels, (y / factor * stride) + (x / factor * 4), magnified, ((y * width) + x) * 4, 4);
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, magnified, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
