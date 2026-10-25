using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    // #216：讲述人只在直播区文本真正变化时收到 LiveRegionChanged，级别与元素声明一致；
    // 分钟级刷新且数量句子不变时不播报。鼓励语退出自动化树，金额与日历状态仍可读且名称正确。
    public static bool RunNarratorLiveRegionBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-narrator-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var deliverySettingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-narrator-delivery-{Guid.NewGuid():N}.settings.json");
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        MustApply(workspace, new SaveNoteChange(
            null,
            "讲述人探针便签甲",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        MustApply(workspace, new SaveNoteChange(
            null,
            "讲述人探针便签乙",
            new DateTime(2038, 7, 9),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var lifecycle = new ManagerWindowLifecycle(
            () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
            PrepareOffscreenValidationWindow);
        var floating = new FloatingWindow(workspace, calendar, lifecycle, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(floating);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        ReminderDeliveryCoordinator? coordinator = null;
        var announced = new List<(string Id, string Name, AutomationLiveSetting Live)>();
        void RememberAnnouncement(TextBlock block) => announced.Add((
            AutomationProperties.GetAutomationId(block) ?? string.Empty,
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));
        UiAccessibility.Announced += RememberAnnouncement;
        try
        {
            floating.Show();
            main.Show();
            FlushNarrator(main.Dispatcher);

            main.ShowWorkspace(WorkspaceKind.Countdowns);
            FlushNarrator(main.Dispatcher);
            ((Button)main.FindName("SaveCountdownButton")).RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent));
            FlushNarrator(main.Dispatcher);
            var nameError = (TextBlock)main.FindName("CountdownNameError");
            Must(
                SawNarratorNotice(announced, "CountdownNameError", "请先填写倒计时名称。", AutomationLiveSetting.Polite),
                "字段错误更新后必须触发礼貌 LiveRegionChanged。" +
                $" errorText={nameError.Text} live={AutomationProperties.GetLiveSetting(nameError)} 已收到：" +
                DescribeAnnouncements(announced));

            ((TextBox)main.FindName("CountdownName")).Text = "讲述人保存探针";
            ((DatePicker)main.FindName("CountdownDate")).SelectedDate = new DateTime(2038, 8, 1);
            ((ComboBox)main.FindName("CountdownHour")).SelectedIndex = 9;
            ((ComboBox)main.FindName("CountdownMinute")).SelectedIndex = 0;
            ((ComboBox)main.FindName("CountdownSecond")).SelectedIndex = 0;
            ((Button)main.FindName("SaveCountdownButton")).RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent));
            FlushNarrator(main.Dispatcher);
            Must(
                SawNarratorNotice(announced, "StatusText", "倒计时已保存。", AutomationLiveSetting.Polite),
                "状态消息更新后必须触发礼貌 LiveRegionChanged。已收到：" + DescribeAnnouncements(announced));

            main.ShowWorkspace(WorkspaceKind.Notes);
            FlushNarrator(main.Dispatcher);
            var completion = VisualDescendants<CheckBox>(main).FirstOrDefault(checkBox =>
                checkBox.Tag is string tagId &&
                workspace.Snapshot.Settings.Notes.Any(note =>
                    string.Equals(note.Id, tagId, StringComparison.OrdinalIgnoreCase) &&
                    !note.IsCompleted));
            Must(completion is not null, "无法定位讲述人探针便签的完成勾选。");
            completion!.IsChecked = true;
            FlushNarrator(main.Dispatcher);
            Must(
                SawNarratorNotice(announced, "RecordCountText", "1 个待办", AutomationLiveSetting.Polite),
                "数量变化后必须触发礼貌 LiveRegionChanged。已收到：" + DescribeAnnouncements(announced));

            var countNoticesBeforeMinute = announced.Count(notice =>
                string.Equals(notice.Id, "RecordCountText", StringComparison.Ordinal));
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(workspace.Snapshot.UtcNow).AddMinutes(1));
            FlushNarrator(main.Dispatcher);
            var countNoticesAfterMinute = announced.Count(notice =>
                string.Equals(notice.Id, "RecordCountText", StringComparison.Ordinal));
            Must(
                countNoticesAfterMinute == countNoticesBeforeMinute,
                "分钟级刷新且数量没变时不得再次播报数量直播区。");

            var dueUtc = new DateTimeOffset(2036, 5, 13, 1, 0, 0, TimeSpan.Zero);
            var deliveryClock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var deliveryWorkspace = DesktopWorkspace.Load(deliverySettingsPath, new SettingsStore(clock: deliveryClock));
            Must(deliveryWorkspace.Apply(new SaveNoteChange(
                    null,
                    "讲述人送达失败探针",
                    new DateTime(2036, 5, 13),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立讲述人送达失败探针。");
            Directory.CreateDirectory(deliverySettingsPath + ".tmp");
            deliveryClock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(deliveryWorkspace, logger: null);
            coordinator.Tick(dueUtc);
            FlushNarrator(Dispatcher.CurrentDispatcher);
            Must(
                SawNarratorNotice(
                    announced,
                    "ReminderEntryDeliveryError",
                    "送达状态保存失败，正在自动重试。打开来源仍然可用。",
                    AutomationLiveSetting.Assertive),
                "提醒送达失败必须触发强制 LiveRegionChanged。已收到：" + DescribeAnnouncements(announced));

            var income = (AutomationOptionalTextBlock)floating.FindName("FloatingIncome");
            var statusBrushKey = FloatingThemePalette.Daylight.MutedBrushKey;
            const string encouragementBrushKey = "DecoTextSoftBrush";
            var encouragementFont = (FontFamily)floating.FindResource("HandwrittenDecoFontFamily");
            const string amountName = "今日已赚 ￥41.13";
            const string encouragementText = "加油！美好的事情正在靠近 ~";
            const string calendarStatusName = "节假日安排未就绪：距离下班和今日已赚暂不可用，联网后自动恢复";
            var resolvedAmount = FloatingBottomStatus.Resolve(
                41.13m,
                8000m,
                "Ready",
                "unused",
                encouragementText);
            Must(
                resolvedAmount.Kind == FloatingBottomStatusKind.Earnings &&
                string.Equals(resolvedAmount.Text, amountName, StringComparison.Ordinal),
                "金额模式的底部文案必须是今日已赚金额。");
            FloatingBottomStatus.Apply(
                income,
                resolvedAmount,
                statusBrushKey,
                encouragementBrushKey,
                encouragementFont);
            floating.UpdateLayout();
            FlushNarrator(floating.Dispatcher);
            var floatingRoot = UIElementAutomationPeer.CreatePeerForElement(floating)
                ?? new UIElementAutomationPeer(floating);
            var amountPeer = FrameworkElementAutomationPeer.CreatePeerForElement(income);
            Must(
                amountPeer is not null &&
                string.Equals(amountPeer.GetName(), amountName, StringComparison.Ordinal) &&
                AutomationRawTreeContains(floatingRoot, new[] { income }),
                "金额模式下悬浮窗底部必须留在自动化树中，名称是今日已赚金额。");

            var resolvedEncouragement = FloatingBottomStatus.Resolve(
                null,
                0m,
                "Ready",
                "unused",
                encouragementText);
            Must(
                resolvedEncouragement.Kind == FloatingBottomStatusKind.Encouragement &&
                string.Equals(resolvedEncouragement.Text, encouragementText, StringComparison.Ordinal),
                "月薪为 0 且日历就绪时底部必须是鼓励语。");
            FloatingBottomStatus.Apply(
                income,
                resolvedEncouragement,
                statusBrushKey,
                encouragementBrushKey,
                encouragementFont);
            floating.UpdateLayout();
            FlushNarrator(floating.Dispatcher);
            Must(
                string.Equals(income.Text, encouragementText, StringComparison.Ordinal) &&
                income.Visibility == Visibility.Visible &&
                FrameworkElementAutomationPeer.CreatePeerForElement(income) is null &&
                !AutomationRawTreeContains(floatingRoot, new[] { income }),
                "鼓励语模式下悬浮窗底部文本必须退出自动化树，可见文案仍在。");

            var resolvedStatus = FloatingBottomStatus.Resolve(
                null,
                8000m,
                "Unavailable",
                "unused",
                encouragementText);
            Must(
                resolvedStatus.Kind == FloatingBottomStatusKind.Status &&
                string.Equals(resolvedStatus.Text, calendarStatusName, StringComparison.Ordinal),
                "日历未就绪时底部必须是日历状态文案。");
            FloatingBottomStatus.Apply(
                income,
                resolvedStatus,
                statusBrushKey,
                encouragementBrushKey,
                encouragementFont);
            floating.UpdateLayout();
            FlushNarrator(floating.Dispatcher);
            var statusPeer = FrameworkElementAutomationPeer.CreatePeerForElement(income);
            Must(
                statusPeer is not null &&
                string.Equals(statusPeer.GetName(), calendarStatusName, StringComparison.Ordinal) &&
                AutomationRawTreeContains(floatingRoot, new[] { income }),
                "日历状态模式下悬浮窗底部必须可以读到，名称是当前状态文案。");
            return true;
        }
        finally
        {
            UiAccessibility.Announced -= RememberAnnouncement;
            coordinator?.Dispose();
            if (main.IsLoaded)
            {
                main.Close();
            }

            if (floating.IsLoaded)
            {
                floating.Close();
            }

            DeleteDirectoryIfEmpty(deliverySettingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteSettingsFiles(deliverySettingsPath);
            DeleteFileIfPresent(diagnosticPath);
            DeleteFileIfPresent(deliverySettingsPath + ".tmp");
        }
    }

    private static bool WaitForLiveRegionRegistration(Dispatcher dispatcher)
    {
        var map = typeof(UIElement).Assembly.GetType("MS.Internal.Automation.EventMap");
        var method = map?.GetMethod(
            "GetRegisteredEvent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (method?.Invoke(null, new object[] { AutomationEvents.LiveRegionChanged }) is not null)
            {
                return true;
            }

            FlushNarrator(dispatcher);
        }

        return method?.Invoke(null, new object[] { AutomationEvents.LiveRegionChanged }) is not null;
    }

    private static void ConnectNarratorElement(Window window, string automationId)
    {
        var root = AutomationElement.FromHandle(new WindowInteropHelper(window).EnsureHandle());
        root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
    }

    private static void FlushNarrator(Dispatcher dispatcher)
    {
        WindowValidationInput.Pump(dispatcher);
    }

    private static AutomationLiveSetting ReadLiveSetting(object? setting)
    {
        var raw = setting switch
        {
            AutomationLiveSetting live => (int)live,
            int number => number,
            _ => Convert.ToInt32(setting, CultureInfo.InvariantCulture)
        };
        return raw switch
        {
            1 => AutomationLiveSetting.Polite,
            2 => AutomationLiveSetting.Assertive,
            _ => AutomationLiveSetting.Off
        };
    }

    private static string DescribeAnnouncements(
        IReadOnlyList<(string Id, string Name, AutomationLiveSetting Live)> notices)
    {
        if (notices.Count == 0)
        {
            return "（无）";
        }

        return string.Join(
            " | ",
            notices.Take(12).Select(notice => $"{notice.Id}/{notice.Live}/{notice.Name}"));
    }

    private static bool SawNarratorNotice(
        NarratorLiveRegionLog log,
        string automationId,
        string name,
        AutomationLiveSetting liveSetting) =>
        SawNarratorNotice(
            log.Notices.Select(notice => (notice.AutomationId, notice.Name, notice.LiveSetting)).ToArray(),
            automationId,
            name,
            liveSetting);

    private static bool SawNarratorNotice(
        IReadOnlyList<(string Id, string Name, AutomationLiveSetting Live)> notices,
        string automationId,
        string name,
        AutomationLiveSetting liveSetting) =>
        notices.Any(notice =>
            string.Equals(notice.Id, automationId, StringComparison.Ordinal) &&
            string.Equals(notice.Name, name, StringComparison.Ordinal) &&
            notice.Live == liveSetting);

    private static string DescribeNarratorNotices(NarratorLiveRegionLog log)
    {
        var notices = log.Notices;
        if (notices.Count == 0)
        {
            return "（无）";
        }

        return string.Join(
            " | ",
            notices.Take(12).Select(notice => $"{notice.AutomationId}/{notice.LiveSetting}/{notice.Name}"));
    }

    private sealed class NarratorLiveRegionLog : IDisposable
    {
        // 本机 UIAutomationClient 没有 LiveRegionChangedEvent / LiveSettingProperty 具名字段。
        // 数值来自 Windows SDK UIAutomationClient.h。
        private static readonly AutomationEvent LiveRegionChangedEvent = AutomationEvent.LookupById(20024);
        private static readonly AutomationProperty LiveSettingProperty = AutomationProperty.LookupById(30135);

        private readonly List<AutomationElement> _pending = new();
        private readonly List<AutomationElement> _scopes = new();
        private readonly List<NarratorLiveRegionNotice> _notices = new();
        private int _rawCount;

        public int RawCount => Volatile.Read(ref _rawCount);

        public void Listen(Window window)
        {
            var element = AutomationElement.FromHandle(new WindowInteropHelper(window).EnsureHandle());
            Automation.AddAutomationEventHandler(
                LiveRegionChangedEvent,
                element,
                TreeScope.Subtree,
                OnChanged);
            _scopes.Add(element);
        }

        public NarratorLiveRegionLog()
        {
        }

        public IReadOnlyList<NarratorLiveRegionNotice> Notices
        {
            get
            {
                Drain();
                return _notices;
            }
        }

        public void Dispose()
        {
            foreach (var scope in _scopes)
            {
                Automation.RemoveAutomationEventHandler(
                    LiveRegionChangedEvent,
                    scope,
                    OnChanged);
            }
        }

        private void OnChanged(object sender, AutomationEventArgs args)
        {
            Interlocked.Increment(ref _rawCount);
            if (sender is AutomationElement element)
            {
                lock (_pending)
                {
                    _pending.Add(element);
                }
            }
        }

        private void Drain()
        {
            AutomationElement[] batch;
            lock (_pending)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                batch = _pending.ToArray();
                _pending.Clear();
            }

            foreach (var element in batch)
            {
                try
                {
                    if (element.Current.ProcessId != Environment.ProcessId)
                    {
                        continue;
                    }

                    var setting = element.GetCurrentPropertyValue(LiveSettingProperty);
                    _notices.Add(new NarratorLiveRegionNotice(
                        element.Current.AutomationId ?? string.Empty,
                        element.Current.Name ?? string.Empty,
                        ReadLiveSetting(setting)));
                }
                catch (ElementNotAvailableException)
                {
                }
            }
        }
    }

    private readonly record struct NarratorLiveRegionNotice(
        string AutomationId,
        string Name,
        AutomationLiveSetting LiveSetting);

    // #156：辅助技术朗读映射——装饰手写体与分段裸数字退出朗读（SilentTextBlock 无自动化
    // peer，也不出现在窗口原始自动化树中）；有可见文字的字段经标签关系（LabeledBy）指向
    // 输入且不携带重复显式名；数量变化由唯一的命令栏数量直播区播完整句；筛选「清除」的
    // 可访问名说明清除的是便签日期筛选；图标按钮继续有可访问名。
    public static bool RunAssistiveNarrationBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-assistive-narration-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        MustApply(workspace, new SaveNoteChange(
            null,
            "朗读探针便签甲",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        MustApply(workspace, new SaveNoteChange(
            null,
            "朗读探针便签乙",
            new DateTime(2038, 7, 9),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var lifecycle = new ManagerWindowLifecycle(
            () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
            PrepareOffscreenValidationWindow);
        var floating = new FloatingWindow(workspace, calendar, lifecycle, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(floating);
        floating.Show();
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            WindowValidationInput.Pump(floating.Dispatcher);

            var mainDecoTexts = new SilentTextBlock[]
            {
                (SilentTextBlock)main.FindName("CountdownDecoTextTop"),
                (SilentTextBlock)main.FindName("CountdownDecoTextBottom"),
                (SilentTextBlock)main.FindName("NotesDecoText")
            };
            var floatingDecoTexts = new SilentTextBlock[]
            {
                (SilentTextBlock)floating.FindName("FloatingNotesDecoTextLeft"),
                (SilentTextBlock)floating.FindName("FloatingNotesDecoText")
            };
            var rootPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(main) ??
                           new System.Windows.Automation.Peers.UIElementAutomationPeer(main);
            Must(
                mainDecoTexts.Concat<FrameworkElement>(floatingDecoTexts).All(deco =>
                    System.Windows.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(deco) is null) &&
                !AutomationRawTreeContains(rootPeer, mainDecoTexts.Concat<FrameworkElement>(floatingDecoTexts).ToList()),
                "装饰手写体必须退出控件朗读：无自动化 peer 且不出现在窗口自动化树中。");

            // 便签分段、数量文字只随便签工作区渲染（RenderVisibleWorkspace），先切换工作区。
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);

            var activeNotesCount = (SilentTextBlock)main.FindName("ActiveNotesCount");
            var completedNotesCount = (SilentTextBlock)main.FindName("CompletedNotesCount");
            Must(
                System.Windows.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(activeNotesCount) is null &&
                System.Windows.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(completedNotesCount) is null,
                "分段内部裸数字不再作为独立内容控件朗读。");

            var labeledFields = new (string InputName, string LabelName, string? HelpText)[]
            {
                ("CountdownName", "CountdownNameLabel", null),
                ("CountdownDate", "CountdownDateLabel", null),
                ("CountdownHour", "CountdownHourLabel", null),
                ("CountdownMinute", "CountdownMinuteLabel", null),
                ("CountdownSecond", "CountdownSecondLabel", null),
                ("DisplayPrecision", "DisplayPrecisionLabel", "悬浮窗显示精度"),
                ("MonthlySalary", "MonthlySalaryLabel", null),
                ("WorkStart", "WorkStartLabel", null),
                ("WorkEnd", "WorkEndLabel", null),
                ("NoteText", "NoteTextLabel", null),
                ("NoteDate", "NoteDateLabel", null),
                ("NoteSearch", "NoteSearchLabel", null),
                ("NoteDateFilter", "NoteDateFilterLabel", "按便签日期筛选"),
                ("NewTagInput", "NewTagInputLabel", null)
            };
            foreach (var (inputName, labelName, helpText) in labeledFields)
            {
                var input = (FrameworkElement)main.FindName(inputName);
                var label = (TextBlock)main.FindName(labelName);
                Must(
                    input is not null && label is not null &&
                    ReferenceEquals(AutomationProperties.GetLabeledBy(input), label) &&
                    label.Text.Length > 0 &&
                    string.IsNullOrEmpty(AutomationProperties.GetName(input)) &&
                    (helpText is null ||
                     string.Equals(AutomationProperties.GetHelpText(input), helpText, StringComparison.Ordinal)),
                    $"字段「{inputName}」必须经标签关系指向可见标签且不携带重复显式名（不读两遍）。");
            }

            var activeSegment = (ToggleButton)main.FindName("ActiveNotesSegment");
            var completedSegment = (ToggleButton)main.FindName("CompletedNotesSegment");
            var recordCount = (TextBlock)main.FindName("RecordCountText");
            Must(
                string.Equals(
                    AutomationProperties.GetName(activeSegment),
                    "查看未完成便签，共 2 条",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(completedSegment),
                    "查看已完成便签，共 0 条",
                    StringComparison.Ordinal) &&
                AutomationProperties.GetLiveSetting(recordCount) == AutomationLiveSetting.Polite &&
                string.Equals(recordCount.Text, "2 个待办", StringComparison.Ordinal),
                "分段数量由分段可访问名承担、数量变化由唯一的命令栏数量直播区播完整句。");

            var clearNoteDateFilter = (Button)main.FindName("ClearNoteDateFilter");
            Must(
                string.Equals(
                    AutomationProperties.GetName(clearNoteDateFilter),
                    "清除便签日期筛选",
                    StringComparison.Ordinal),
                "筛选「清除」的可访问名必须说明清除的是便签日期筛选。");

            Must(
                main.HasAccessibleCoreControls,
                "图标按钮与核心控件继续拥有可访问名（显式名或标签关系）。");

            // 完成一条便签：数量直播区文字随真实数量变化为完整句，分段名同步。
            var completion = VisualDescendants<CheckBox>(main).FirstOrDefault(checkBox =>
                checkBox.Tag is string tagId &&
                workspace.Snapshot.Settings.Notes.Any(note =>
                    string.Equals(note.Id, tagId, StringComparison.OrdinalIgnoreCase)));
            Must(completion is not null, "无法定位朗读探针便签的完成勾选。");
            completion!.IsChecked = true;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                string.Equals(recordCount.Text, "1 个待办", StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(activeSegment),
                    "查看未完成便签，共 1 条",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(completedSegment),
                    "查看已完成便签，共 1 条",
                    StringComparison.Ordinal),
                "便签完成后的数量变化只由一处状态区域以完整句更新。");
            return true;
        }
        finally
        {
            main.Close();
            floating.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }
}
