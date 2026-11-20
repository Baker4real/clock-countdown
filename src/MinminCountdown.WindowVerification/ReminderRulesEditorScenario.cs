using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #322：规则编辑器单一实心主行动、未提交输入不丢、列表按内容取高、各工作区高度可达性与既有契约回归。
internal static class ReminderRulesEditorScenario
{
    private const double MaximumInitialWindowHeight = 790;
    private const double WorkAreaMargin = 18;
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    private static readonly (string Label, Rect WorkArea, double Dpi)[] SupportedMonitors =
    [
        ("1032-100", new Rect(0, 0, 1920, 1032), 1.0),
        ("826-125", new Rect(0, 0, 1536, 826), 1.25),
        ("688-150", new Rect(0, 0, 1280, 688), 1.5)
    ];

    internal static (bool Passed, string Explanation) RunSinglePrimaryActionAndDirtyAccept(string meaning)
    {
        var failures = new List<string>();
        var captureEvidence = CreateEvidenceWriter();

        CheckSinglePrimaryAction(failures, captureEvidence);
        CheckUncommittedInputOnAccept(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunContentSizedListAndReachableActions(string meaning)
    {
        var failures = new List<string>();
        var captureEvidence = CreateEvidenceWriter();

        CheckContentSizedLayoutAndReachability(failures, captureEvidence);
        CheckSecondaryMonitorOwnerPlacement(failures);
        CheckCancelLimitAndValidationUnchanged(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    private static void CheckSinglePrimaryAction(
        List<string> failures,
        Action<Window, string>? captureEvidence)
    {
        using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(SupportedMonitors[0].WorkArea, SupportedMonitors[0].Dpi, SupportedMonitors[0].Dpi));
        var rule = new AbsoluteOnceReminderRule("single-primary-rule", FixtureUtc.AddHours(2));
        var editor = CreateOffscreenEditor([rule], new FixedClock(FixtureUtc));
        try
        {
            ShowAndSettle(editor);

            AssertPrimaryButtonHierarchy(editor, "新增态", "添加规则", failures);

            var editButton = VisualDescendants<Button>(editor)
                .Single(button => string.Equals(button.Content as string, "编辑", StringComparison.Ordinal));
            InvokeButton(editor, editButton);
            AssertPrimaryButtonHierarchy(editor, "编辑态", "保存修改", failures);
            captureEvidence?.Invoke(editor, "rules-editor-edit-mode");

            using (ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                SettleWindow(editor);
                AssertPrimaryButtonHierarchy(editor, "系统对比度编辑态", "保存修改", failures, checkDayBrushes: false);
            }
            SettleWindow(editor);

            var cancelEdit = FindByAutomationId<Button>(editor, "CancelReminderRuleEditButton");
            InvokeButton(editor, cancelEdit);
            var copyButton = VisualDescendants<Button>(editor)
                .Single(button => string.Equals(button.Content as string, "复制", StringComparison.Ordinal));
            InvokeButton(editor, copyButton);
            AssertPrimaryButtonHierarchy(editor, "复制态", "保存副本", failures);
        }
        finally
        {
            CloseWindow(editor);
        }
    }

    private static void AssertPrimaryButtonHierarchy(
        ReminderRulesEditorWindow editor,
        string stateLabel,
        string expectedSaveLabel,
        List<string> failures,
        bool checkDayBrushes = true)
    {
        var primaryStyle = (Style)Application.Current.FindResource("PrimaryButton");
        var secondaryStyle = (Style)Application.Current.FindResource("SecondaryButton");
        var mainActionBrush = (Brush)Application.Current.FindResource("MainActionBrush");
        var cardAltBrush = (Brush)Application.Current.FindResource("CardAltBrush");
        var controlBorderBrush = (Brush)Application.Current.FindResource("ControlBorderBrush");

        var visibleButtons = VisualDescendants<Button>(editor)
            .Where(button => button.IsVisible)
            .ToArray();
        foreach (var button in visibleButtons)
        {
            button.ApplyTemplate();
        }

        var solidPrimaryButtons = visibleButtons
            .Where(button =>
                ReferenceEquals(button.Style, primaryStyle) ||
                (checkDayBrushes &&
                 button.Template?.FindName("Surface", button) is Border surface &&
                 BrushColorEquals(surface.Background, mainActionBrush)))
            .ToArray();
        if (solidPrimaryButtons.Length != 1)
        {
            var ids = string.Join(
                ",",
                solidPrimaryButtons.Select(button =>
                    $"{AutomationProperties.GetAutomationId(button)}({button.Content})"));
            failures.Add($"{stateLabel}：期望恰有 1 个实心主行动按钮，实际 {solidPrimaryButtons.Length} 个 [{ids}]");
        }
        else
        {
            var solePrimary = solidPrimaryButtons[0];
            if (!string.Equals(
                    AutomationProperties.GetAutomationId(solePrimary),
                    "AcceptReminderRulesButton",
                    StringComparison.Ordinal) ||
                !string.Equals(solePrimary.Content as string, "完成", StringComparison.Ordinal))
            {
                failures.Add(
                    $"{stateLabel}：唯一实心主行动应为「完成」(AcceptReminderRulesButton)，" +
                    $"实际为 {AutomationProperties.GetAutomationId(solePrimary)}({solePrimary.Content})");
            }
        }

        var saveButton = FindByAutomationId<Button>(editor, "SaveReminderRuleButton");
        if (!saveButton.IsVisible || !saveButton.IsEnabled)
        {
            failures.Add($"{stateLabel}：表单提交按钮不可见或不可用");
        }

        if (!string.Equals(saveButton.Content as string, expectedSaveLabel, StringComparison.Ordinal))
        {
            failures.Add($"{stateLabel}：表单提交文案期望「{expectedSaveLabel}」，实际「{saveButton.Content}」");
        }

        if (!ReferenceEquals(saveButton.Style, secondaryStyle))
        {
            failures.Add($"{stateLabel}：表单提交按钮未使用 SecondaryButton 次级样式");
        }

        if (checkDayBrushes)
        {
            if (saveButton.Template?.FindName("Surface", saveButton) is not Border saveSurface ||
                !BrushColorEquals(saveSurface.Background, cardAltBrush) ||
                !BrushColorEquals(saveSurface.BorderBrush, controlBorderBrush))
            {
                failures.Add($"{stateLabel}：表单提交按钮渲染底色/边框未符合次级按钮样式");
            }
        }
    }

    private static void CheckUncommittedInputOnAccept(List<string> failures)
    {
        var clock = new FixedClock(FixtureUtc);
        var futureBeijing = ChinaStandardTime.ToBeijingTime(FixtureUtc).AddDays(1);
        var due1 = new DateTimeOffset(futureBeijing.Year, futureBeijing.Month, futureBeijing.Day, 10, 30, 0, futureBeijing.Offset);

        // 1. 新增态有有效未提交输入，直接点「完成」-> 自动提交该规则，不丢弃。
        var addResult = RunModal(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            editor =>
            {
                SetSchedule(editor, modeIndex: 1, due1, intervalIndex: 2, customInterval: string.Empty);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            });
        var dirtyAddCommitted = addResult.DialogResult == true &&
            addResult.Rules.Count == 1 &&
            addResult.Rules[0] is FixedIntervalReminderRule { Interval.Minutes: 60 } addedFixed &&
            addedFixed.FirstDueUtc == due1.ToUniversalTime();
        if (!dirtyAddCommitted)
        {
            failures.Add($"新增态未提交有效输入点「完成」未提交规则（dialog={addResult.DialogResult}, count={addResult.Rules.Count}）");
        }

        // 2. 编辑态有有效未提交修改，直接点「完成」-> 原位提交修改并保留 rule ID，不丢弃。
        var existingRule = new AbsoluteOnceReminderRule("dirty-edit-rule", due1.ToUniversalTime());
        var dueEdited = due1.AddHours(2);
        var editResult = RunModal(
            [existingRule],
            clock,
            null,
            editor =>
            {
                var editButton = VisualDescendants<Button>(editor)
                    .Single(button => string.Equals(button.Content as string, "编辑", StringComparison.Ordinal));
                InvokeButton(editor, editButton);
                SetSchedule(editor, modeIndex: 0, dueEdited, intervalIndex: 1, customInterval: string.Empty);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            });
        var dirtyEditCommitted = editResult.DialogResult == true &&
            editResult.Rules.Count == 1 &&
            editResult.Rules[0] is AbsoluteOnceReminderRule editedOnce &&
            string.Equals(editedOnce.Id, existingRule.Id, StringComparison.Ordinal) &&
            editedOnce.ScheduledUtc == dueEdited.ToUniversalTime();
        if (!dirtyEditCommitted)
        {
            failures.Add($"编辑态未提交有效修改点「完成」未保留原 ID 提交（dialog={editResult.DialogResult}, count={editResult.Rules.Count}）");
        }

        // 3. 复制态有有效未提交修改，直接点「完成」-> 提交新副本并保留原规则。
        var dueCopied = due1.AddHours(4);
        var copyResult = RunModal(
            [existingRule],
            clock,
            new ValidationGuidSource(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            editor =>
            {
                var copyButton = VisualDescendants<Button>(editor)
                    .Single(button => string.Equals(button.Content as string, "复制", StringComparison.Ordinal));
                InvokeButton(editor, copyButton);
                SetSchedule(editor, modeIndex: 0, dueCopied, intervalIndex: 1, customInterval: string.Empty);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            });
        var dirtyCopyCommitted = copyResult.DialogResult == true &&
            copyResult.Rules.Count == 2 &&
            copyResult.Rules.Any(rule => string.Equals(rule.Id, existingRule.Id, StringComparison.Ordinal)) &&
            copyResult.Rules.Any(rule =>
                !string.Equals(rule.Id, existingRule.Id, StringComparison.Ordinal) &&
                rule is AbsoluteOnceReminderRule copiedOnce &&
                copiedOnce.ScheduledUtc == dueCopied.ToUniversalTime());
        if (!dirtyCopyCommitted)
        {
            failures.Add($"复制态未提交有效修改点「完成」未保存副本（dialog={copyResult.DialogResult}, count={copyResult.Rules.Count}）");
        }

        // 4. 有无效未提交输入时点「完成」-> 拦住关闭、显示字段错误并保留未提交输入，修正后再点「完成」成功提交。
        var invalidBlocked = false;
        var invalidResult = RunModal(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            editor =>
            {
                SetSchedule(editor, modeIndex: 1, due1, intervalIndex: 3, customInterval: "0");
                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));

                var intervalError = FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError");
                var customInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval");
                invalidBlocked = editor.IsVisible &&
                    editor.ResultRules.Count == 0 &&
                    intervalError.IsVisible &&
                    string.Equals(intervalError.Text, "自定义提醒必须是 1 到 1440 分钟。", StringComparison.Ordinal) &&
                    string.Equals(customInterval.Text, "0", StringComparison.Ordinal);

                customInterval.Text = "25";
                SettleWindow(editor);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            });
        var invalidRecovered = invalidBlocked &&
            invalidResult.DialogResult == true &&
            invalidResult.Rules.Count == 1 &&
            invalidResult.Rules[0] is FixedIntervalReminderRule { Interval.Minutes: 25 };
        if (!invalidRecovered)
        {
            failures.Add(
                $"无效未提交输入点「完成」未拦住关闭或未保留输入（blocked={invalidBlocked}, dialog={invalidResult.DialogResult}, count={invalidResult.Rules.Count}）");
        }

        // 5. 未改表单任何输入时点「完成」-> 接受现有列表，不把默认预填误当成新规则。
        var cleanResult = RunModal(
            Array.Empty<ReminderRule>(),
            clock,
            null,
            editor => InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton")));
        var cleanNoPhantom = cleanResult.DialogResult == true && cleanResult.Rules.Count == 0;
        if (!cleanNoPhantom)
        {
            failures.Add($"未修改表单时点「完成」误生成了规则（dialog={cleanResult.DialogResult}, count={cleanResult.Rules.Count}）");
        }

        Console.Error.WriteLine(
            $"RULE_EDITOR_322_DIRTY_ACCEPT_PROBE: add={dirtyAddCommitted}; edit={dirtyEditCommitted}; copy={dirtyCopyCommitted}; invalidBlocked={invalidBlocked}; invalidRecovered={invalidRecovered}; cleanNoPhantom={cleanNoPhantom}");
    }

    private static void CheckContentSizedLayoutAndReachability(
        List<string> failures,
        Action<Window, string>? captureEvidence)
    {
        var blockGap = (double)Application.Current.FindResource("SpacingBlockGap");
        var clock = new FixedClock(FixtureUtc);
        var eightRules = CreateEightRules(FixtureUtc);

        // A. 标准 1032 DIP 工作区：0 / 1 / 2 / 8 条规则、手动拉高少规则窗、0 条切日历高表单。
        using (MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(SupportedMonitors[0].WorkArea, SupportedMonitors[0].Dpi, SupportedMonitors[0].Dpi)))
        {
            var zeroHeight = MeasureRulesLayout(
                Array.Empty<ReminderRule>(),
                clock,
                SupportedMonitors[0].WorkArea,
                "1032-0条",
                expectScroll: false,
                blockGap,
                failures,
                captureLabel: "rules-editor-0-rules",
                captureEvidence: captureEvidence,
                afterInitialCheck: editor =>
                {
                    if (editor.ActualHeight >= 500)
                    {
                        failures.Add($"1032-0条：初始窗口高度未随内容收紧（actual={editor.ActualHeight:0.##}，期望 < 500）");
                    }

                    // 0 条紧凑窗切换到日历每周高表单时，窗口自动增高且底部按钮和缩放手柄仍可达。
                    FindByAutomationId<ComboBox>(editor, "ReminderRuleMode").SelectedIndex = 2;
                    FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarFrequency").SelectedIndex = 1;
                    SettleWindow(editor);
                    if (editor.ActualHeight <= 350 || editor.ActualHeight > MaximumInitialWindowHeight + 0.5)
                    {
                        failures.Add($"1032-0条切日历：窗口高度异常（actual={editor.ActualHeight:0.##}）");
                    }

                    AssertBottomControlsAndGripReachable(editor, SupportedMonitors[0].WorkArea, "1032-0条切日历", failures);
                });

            var oneHeight = MeasureRulesLayout(
                eightRules.Take(1).ToArray(),
                clock,
                SupportedMonitors[0].WorkArea,
                "1032-1条",
                expectScroll: false,
                blockGap,
                failures,
                captureLabel: "rules-editor-1-rule",
                captureEvidence: captureEvidence,
                afterInitialCheck: editor =>
                {
                    if (editor.ActualHeight <= zeroHeight + 10 || editor.ActualHeight >= 560)
                    {
                        failures.Add(
                            $"1032-1条：初始高度未随 1 条内容合理增长（zero={zeroHeight:0.##}, one={editor.ActualHeight:0.##}）");
                    }

                    // 用户手动拉高少规则窗口时，列表与表单之间仍不出现大块空白，底部动作与缩放手柄仍可达。
                    editor.Height = 620;
                    SettleWindow(editor);
                    AssertGapWithinBlockGap(editor, "1032-1条手动拉高", expectScroll: false, blockGap, failures);
                    AssertBottomControlsAndGripReachable(editor, SupportedMonitors[0].WorkArea, "1032-1条手动拉高", failures);
                });

            var twoHeight = MeasureRulesLayout(
                eightRules.Take(2).ToArray(),
                clock,
                SupportedMonitors[0].WorkArea,
                "1032-2条",
                expectScroll: false,
                blockGap,
                failures);
            if (twoHeight <= oneHeight + 20)
            {
                failures.Add($"1032-2条：初始高度未随 2 条规则增长（one={oneHeight:0.##}, two={twoHeight:0.##}）");
            }

            MeasureRulesLayout(
                eightRules,
                clock,
                SupportedMonitors[0].WorkArea,
                "1032-8条",
                expectScroll: true,
                blockGap,
                failures,
                captureLabel: "rules-editor-8-rules-1032",
                captureEvidence: captureEvidence,
                afterInitialCheck: editor =>
                {
                    var scroller = FindRulesScroller(editor);
                    scroller.ScrollToEnd();
                    SettleWindow(editor);
                    var host = (Panel)scroller.Content;
                    var lastCard = (FrameworkElement)host.Children[host.Children.Count - 1];
                    var lastBounds = Bounds(lastCard, scroller);
                    if (lastBounds.Top < -1 || lastBounds.Bottom > scroller.ViewportHeight + 1.5)
                    {
                        failures.Add(
                            $"1032-8条：滚动到底后末条规则未落进列表视口（card=[{lastBounds.Top:0.##},{lastBounds.Bottom:0.##}], viewport={scroller.ViewportHeight:0.##}）");
                    }
                });
        }

        // B. 支持的工作区高度 826 DIP 与 688 DIP：少规则与多规则（含日历每周+双错误行）全部可达。
        foreach (var (label, workArea, dpi) in SupportedMonitors.Skip(1))
        {
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(workArea, dpi, dpi));
            _ = MeasureRulesLayout(
                eightRules.Take(1).ToArray(),
                clock,
                workArea,
                $"{label}-1条",
                expectScroll: false,
                blockGap,
                failures);

            _ = MeasureRulesLayout(
                eightRules,
                clock,
                workArea,
                $"{label}-8条",
                expectScroll: true,
                blockGap,
                failures,
                afterInitialCheck: editor =>
                {
                    // 编辑第 6 条每周规则并触发间隔+星期两个表单错误，验证短屏在最高表单下底部按钮与缩放手柄仍可达。
                    var editWeekly = VisualDescendants<Button>(editor)
                        .Single(button =>
                            string.Equals(button.Content as string, "编辑", StringComparison.Ordinal) &&
                            string.Equals(button.Tag as string, "rule-322-6", StringComparison.Ordinal));
                    InvokeButton(editor, editWeekly);
                    FindByAutomationId<TextBox>(editor, "ReminderRuleCalendarInterval").Text = "0";
                    foreach (var day in Enum.GetValues<DayOfWeek>())
                    {
                        FindByAutomationId<CheckBox>(editor, $"ReminderRuleCalendarWeekday{day}").IsChecked = false;
                    }
                    InvokeButton(editor, FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                    var expectedMaxHeight = Math.Min(MaximumInitialWindowHeight, workArea.Height - WorkAreaMargin * 2);
                    if (editor.ActualHeight > expectedMaxHeight + 0.5)
                    {
                        failures.Add(
                            $"{label}-8条日历错误：窗口高度越上限（actual={editor.ActualHeight:0.##}, max={expectedMaxHeight:0.##}）");
                    }

                    AssertGapWithinBlockGap(editor, $"{label}-8条日历错误", expectScroll: true, blockGap, failures);
                    AssertBottomControlsAndGripReachable(editor, workArea, $"{label}-8条日历错误", failures);
                    if (string.Equals(label, "688-150", StringComparison.Ordinal))
                    {
                        captureEvidence?.Invoke(editor, "rules-editor-8-rules-calendar-error-688");
                    }
                });
        }
    }

    private static double MeasureRulesLayout(
        IReadOnlyList<ReminderRule> rules,
        IClock clock,
        Rect workArea,
        string scenarioLabel,
        bool expectScroll,
        double blockGap,
        List<string> failures,
        string? captureLabel = null,
        Action<Window, string>? captureEvidence = null,
        Action<ReminderRulesEditorWindow>? afterInitialCheck = null)
    {
        var editor = CreateOffscreenEditor(rules, clock);
        try
        {
            ShowAndSettle(editor);
            var expectedMaxHeight = Math.Min(MaximumInitialWindowHeight, workArea.Height - WorkAreaMargin * 2);
            if (editor.ActualHeight > expectedMaxHeight + 0.5)
            {
                failures.Add(
                    $"{scenarioLabel}：窗口初始高度超过上限（actual={editor.ActualHeight:0.##}, max={expectedMaxHeight:0.##}）");
            }

            AssertGapWithinBlockGap(editor, scenarioLabel, expectScroll, blockGap, failures);
            AssertBottomControlsAndGripReachable(editor, workArea, scenarioLabel, failures);

            if (captureLabel is not null)
            {
                captureEvidence?.Invoke(editor, captureLabel);
            }

            var initialHeight = editor.ActualHeight;
            afterInitialCheck?.Invoke(editor);
            return initialHeight;
        }
        finally
        {
            CloseWindow(editor);
        }
    }

    private static void AssertGapWithinBlockGap(
        ReminderRulesEditorWindow editor,
        string scenarioLabel,
        bool expectScroll,
        double blockGap,
        List<string> failures)
    {
        var scroller = FindRulesScroller(editor);
        var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
        var formRow = mode.Parent as Grid
            ?? throw new InvalidOperationException("规则类型控件未位于表单行 Grid。");
        var form = formRow.Parent as Grid
            ?? throw new InvalidOperationException("规则表单行未位于表单 Grid。");

        var hasScroll = scroller.ScrollableHeight > 0.5;
        if (expectScroll && !hasScroll)
        {
            failures.Add(
                $"{scenarioLabel}：多规则时列表未进入内部滚动（scrollable={scroller.ScrollableHeight:0.##}, viewport={scroller.ViewportHeight:0.##}, extent={scroller.ExtentHeight:0.##}）");
        }
        else if (!expectScroll && hasScroll)
        {
            failures.Add(
                $"{scenarioLabel}：少规则时列表不应滚动（scrollable={scroller.ScrollableHeight:0.##}）");
        }

        if (expectScroll && scroller.ComputedVerticalScrollBarVisibility != Visibility.Visible)
        {
            failures.Add($"{scenarioLabel}：多规则滚动时纵向滚动条未显示");
        }

        double listVisualBottom;
        if (expectScroll)
        {
            listVisualBottom = Bounds(scroller, editor).Bottom;
        }
        else
        {
            var host = scroller.Content as Panel
                ?? throw new InvalidOperationException("规则列表容器不是 Panel。");
            var lastVisibleChild = host.Children
                .OfType<FrameworkElement>()
                .LastOrDefault(child => child.IsVisible);
            if (lastVisibleChild is null)
            {
                failures.Add($"{scenarioLabel}：规则列表没有可见内容");
                return;
            }

            listVisualBottom = Bounds(lastVisibleChild, editor).Bottom;
        }

        var formTop = Bounds(form, editor).Top;
        var gap = formTop - listVisualBottom;
        if (gap < -0.5 || gap > blockGap + 1.0)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{scenarioLabel}：列表与表单间距超出一个区块间距（gap={gap:0.##} DIP，期望 0..{blockGap:0.##} DIP）"));
        }
    }

    private static void AssertBottomControlsAndGripReachable(
        ReminderRulesEditorWindow editor,
        Rect workArea,
        string scenarioLabel,
        List<string> failures)
    {
        if (!WindowInsideRect(workArea, editor))
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{scenarioLabel}：窗口越出工作区（window={editor.Left:0.##},{editor.Top:0.##},{editor.ActualWidth:0.##}×{editor.ActualHeight:0.##}; workArea={workArea}）"));
        }

        foreach (var automationId in new[] { "SaveReminderRuleButton", "AcceptReminderRulesButton", "CancelReminderRulesButton" })
        {
            var button = FindByAutomationId<Button>(editor, automationId);
            if (!IsReachable(editor, button))
            {
                var bounds = Bounds(button, editor);
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{scenarioLabel}：按钮 {automationId} 不可达（visible={button.IsVisible}, bounds={bounds}, window={editor.RenderSize}）"));
            }
        }

        var grip = VisualDescendants<ResizeGrip>(editor).FirstOrDefault();
        if (grip is null || !IsReachable(editor, grip))
        {
            failures.Add($"{scenarioLabel}：窗口缩放手柄 ResizeGrip 不可达");
        }
    }

    private static void CheckSecondaryMonitorOwnerPlacement(List<string> failures)
    {
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
            using var monitors = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1),
                new MonitorWorkArea(new Rect(-1536, 0, 1536, 688), 1.5, 1.5));
            owner.Show();
            editor = new ReminderRulesEditorWindow(Array.Empty<ReminderRule>(), new FixedClock(FixtureUtc))
            {
                Owner = owner,
                ShowActivated = false,
                Opacity = 0
            };
            editor.Show();
            WindowValidationInput.Pump(editor.Dispatcher);

            var area = new Rect(-1536, 0, 1536, 688);
            var contains = WindowInsideRect(area, editor);
            var heightOk = editor.ActualHeight <= 652.5;
            var leftNegative = editor.Left < 0;
            var acceptOk = IsReachable(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            var cancelOk = IsReachable(editor, FindByAutomationId<Button>(editor, "CancelReminderRulesButton"));
            var grip = VisualDescendants<ResizeGrip>(editor).FirstOrDefault();
            var gripOk = grip is not null && IsReachable(editor, grip);
            if (!contains || !heightOk || !leftNegative || !acceptOk || !cancelOk || !gripOk)
            {
                var gripBounds = grip is null ? Rect.Empty : Bounds(grip, editor);
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"副屏所有者居中放置失败：contains={contains}, heightOk={heightOk}, leftNeg={leftNegative}, " +
                    $"accept={acceptOk}, cancel={cancelOk}, grip={gripOk}, " +
                    $"editor=({editor.Left:0.##},{editor.Top:0.##},{editor.ActualWidth:0.##}x{editor.ActualHeight:0.##}), " +
                    $"renderSize=({editor.RenderSize.Width:0.##}x{editor.RenderSize.Height:0.##}), " +
                    $"gripBounds=({gripBounds.Left:0.##},{gripBounds.Top:0.##},{gripBounds.Right:0.##},{gripBounds.Bottom:0.##}), " +
                    $"owner=({owner.Left:0.##},{owner.Top:0.##},{owner.ActualWidth:0.##}x{owner.ActualHeight:0.##})"));
            }
        }
        finally
        {
            if (editor is not null)
            {
                CloseWindow(editor);
            }
            CloseWindow(owner);
        }
    }

    private static void CheckCancelLimitAndValidationUnchanged(List<string> failures)
    {
        var clock = new FixedClock(FixtureUtc);
        var futureBeijing = ChinaStandardTime.ToBeijingTime(FixtureUtc).AddDays(1);
        var due1 = new DateTimeOffset(futureBeijing.Year, futureBeijing.Month, futureBeijing.Day, 10, 0, 0, futureBeijing.Offset);

        // 1. 取消不改变调用方草稿（即使已删规则且表单有未提交输入）。
        ReminderRule[] initialPair =
        [
            new AbsoluteOnceReminderRule("cancel-keep-1", due1.ToUniversalTime()),
            new AbsoluteOnceReminderRule("cancel-keep-2", due1.AddHours(1).ToUniversalTime())
        ];
        var cancelResult = RunModal(
            initialPair,
            clock,
            null,
            editor =>
            {
                var firstDelete = VisualDescendants<Button>(editor)
                    .First(button => string.Equals(button.Content as string, "删除", StringComparison.Ordinal));
                InvokeButton(editor, firstDelete);
                SetSchedule(editor, modeIndex: 1, due1.AddHours(3), intervalIndex: 0, customInterval: string.Empty);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "CancelReminderRulesButton"));
            });
        var cancelPreserved = cancelResult.DialogResult == false &&
            cancelResult.Rules.Count == 2 &&
            cancelResult.Rules.Select(rule => rule.Id).SequenceEqual(initialPair.Select(rule => rule.Id));
        if (!cancelPreserved)
        {
            failures.Add($"取消草稿未原样保留初始规则（dialog={cancelResult.DialogResult}, count={cancelResult.Rules.Count}）");
        }

        // 2. 最多 8 条规则：新增第 9 条无论点表单按钮还是点「完成」均在集合错误处拒绝。
        var eightRules = CreateEightRules(FixtureUtc);
        var ninthRejectedOnSave = false;
        var ninthRejectedOnAccept = false;
        var limitResult = RunModal(
            eightRules,
            clock,
            new ValidationGuidSource(Guid.Parse("99999999-9999-9999-9999-999999999999")),
            editor =>
            {
                SetSchedule(editor, modeIndex: 0, due1.AddDays(12), intervalIndex: 1, customInterval: string.Empty);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
                var collectionError = FindByAutomationId<TextBlock>(editor, "ReminderRuleCollectionError");
                var countText = FindByAutomationId<TextBlock>(editor, "ReminderRulesCount");
                ninthRejectedOnSave = collectionError.IsVisible &&
                    string.Equals(collectionError.Text, "每条记录最多只能包含 8 条提醒规则。", StringComparison.Ordinal) &&
                    string.Equals(countText.Text, "8 / 8 条", StringComparison.Ordinal);

                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
                ninthRejectedOnAccept = editor.IsVisible &&
                    collectionError.IsVisible &&
                    string.Equals(countText.Text, "8 / 8 条", StringComparison.Ordinal);

                InvokeButton(editor, FindByAutomationId<Button>(editor, "CancelReminderRulesButton"));
            });
        if (!ninthRejectedOnSave || !ninthRejectedOnAccept || limitResult.Rules.Count != 8)
        {
            failures.Add(
                $"8 条规则上限契约变更（saveRejected={ninthRejectedOnSave}, acceptRejected={ninthRejectedOnAccept}, count={limitResult.Rules.Count}）");
        }

        // 3. 各字段校验与错误位置不变（首次提醒、自定义间隔、倒计时提前分钟、日历间隔、日历选择器）。
        var validationEditor = CreateOffscreenEditor(
            Array.Empty<ReminderRule>(),
            clock,
            countdownTargetUtc: FixtureUtc.AddMinutes(30));
        try
        {
            ShowAndSettle(validationEditor);

            // 首次提醒过去时间
            var pastBeijing = ChinaStandardTime.ToBeijingTime(FixtureUtc.AddMinutes(-5));
            SetSchedule(validationEditor, modeIndex: 0, pastBeijing, intervalIndex: 1, customInterval: string.Empty);
            InvokeButton(validationEditor, FindByAutomationId<Button>(validationEditor, "SaveReminderRuleButton"));
            AssertFieldErrorAtInputRow(
                validationEditor,
                "ReminderRuleFirstDueError",
                "首次提醒时间必须晚于当前北京时间。",
                failures);

            // 固定间隔非法自定义值
            SetSchedule(validationEditor, modeIndex: 1, due1, intervalIndex: 3, customInterval: "0");
            InvokeButton(validationEditor, FindByAutomationId<Button>(validationEditor, "SaveReminderRuleButton"));
            AssertFieldErrorAtInputRow(
                validationEditor,
                "ReminderRuleIntervalError",
                "自定义提醒必须是 1 到 1440 分钟。",
                failures);

            // 倒计时相对偏移导致派生时刻过去
            FindByAutomationId<ComboBox>(validationEditor, "ReminderRuleMode").SelectedIndex = 2;
            FindByAutomationId<TextBox>(validationEditor, "ReminderRuleRelativeOffsetMinutes").Text = "60";
            SettleWindow(validationEditor);
            InvokeButton(validationEditor, FindByAutomationId<Button>(validationEditor, "SaveReminderRuleButton"));
            AssertFieldErrorAtInputRow(
                validationEditor,
                "ReminderRuleRelativeOffsetError",
                "派生时间晚于当前北京时间",
                failures,
                containsMatch: true);

            // 日历间隔非法
            FindByAutomationId<ComboBox>(validationEditor, "ReminderRuleMode").SelectedIndex = 3;
            FindByAutomationId<ComboBox>(validationEditor, "ReminderRuleCalendarFrequency").SelectedIndex = 0;
            FindByAutomationId<TextBox>(validationEditor, "ReminderRuleCalendarInterval").Text = "0";
            SettleWindow(validationEditor);
            InvokeButton(validationEditor, FindByAutomationId<Button>(validationEditor, "SaveReminderRuleButton"));
            AssertFieldErrorAtInputRow(
                validationEditor,
                "ReminderRuleCalendarIntervalError",
                "1 到 999",
                failures,
                containsMatch: true);

            // 日历每周未选星期
            FindByAutomationId<ComboBox>(validationEditor, "ReminderRuleCalendarFrequency").SelectedIndex = 1;
            FindByAutomationId<TextBox>(validationEditor, "ReminderRuleCalendarInterval").Text = "1";
            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                FindByAutomationId<CheckBox>(validationEditor, $"ReminderRuleCalendarWeekday{day}").IsChecked = false;
            }
            SettleWindow(validationEditor);
            InvokeButton(validationEditor, FindByAutomationId<Button>(validationEditor, "SaveReminderRuleButton"));
            AssertFieldErrorAtInputRow(
                validationEditor,
                "ReminderRuleCalendarSelectorError",
                "至少选择一个星期",
                failures,
                containsMatch: true);
        }
        finally
        {
            CloseWindow(validationEditor);
        }
    }

    private static void AssertFieldErrorAtInputRow(
        ReminderRulesEditorWindow editor,
        string errorAutomationId,
        string expectedText,
        List<string> failures,
        bool containsMatch = false)
    {
        var error = FindByAutomationId<TextBlock>(editor, errorAutomationId);
        var textMatches = containsMatch
            ? error.Text.Contains(expectedText, StringComparison.Ordinal)
            : string.Equals(error.Text, expectedText, StringComparison.Ordinal);
        if (!error.IsVisible || !textMatches)
        {
            failures.Add($"{errorAutomationId} 未按预期显示（visible={error.IsVisible}, text=「{error.Text}」）");
            return;
        }

        if (error.Parent is not Grid row ||
            Grid.GetRow(error) != 1 ||
            Grid.GetColumn(error) != 1 ||
            !row.Children.Cast<UIElement>().Any(child => Grid.GetRow(child) == 0 && Grid.GetColumn(child) == 1 && child.IsVisible))
        {
            failures.Add($"{errorAutomationId} 未紧贴对应输入控件下一行显示");
        }
    }

    private static ReminderRule[] CreateEightRules(DateTimeOffset nowUtc)
    {
        var startDate = DateOnly.FromDateTime(ChinaStandardTime.ToBeijingTime(nowUtc).Date).AddDays(1);
        return
        [
            new AbsoluteOnceReminderRule("rule-322-1", nowUtc.AddDays(1)),
            new AbsoluteOnceReminderRule("rule-322-2", nowUtc.AddDays(2).AddHours(1)),
            new AbsoluteOnceReminderRule("rule-322-3", nowUtc.AddDays(3).AddHours(2)),
            new AbsoluteOnceReminderRule("rule-322-4", nowUtc.AddDays(4).AddHours(3)),
            new FixedIntervalReminderRule("rule-322-5", nowUtc.AddHours(2), new ReminderInterval(30)),
            new CalendarReminderRule(
                "rule-322-6",
                CalendarFrequency.Weekly,
                1,
                startDate,
                new TimeOnly(9, 0),
                new WeeklyCalendarSelector([DayOfWeek.Monday, DayOfWeek.Thursday])),
            new CalendarReminderRule(
                "rule-322-7",
                CalendarFrequency.Monthly,
                1,
                startDate,
                new TimeOnly(18, 0),
                new MonthlyCalendarSelector(null, true)),
            new CalendarReminderRule(
                "rule-322-8",
                CalendarFrequency.Yearly,
                1,
                startDate,
                new TimeOnly(9, 30),
                new YearlyCalendarSelector(8, 15, false))
        ];
    }

    private static ModalEditorResult RunModal(
        IReadOnlyList<ReminderRule> initialRules,
        IClock clock,
        IGuidSource? guidSource,
        Action<ReminderRulesEditorWindow> interact)
    {
        var editor = CreateOffscreenEditor(initialRules, clock, guidSource);
        Exception? interactionException = null;
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    SettleWindow(editor);
                    interact(editor);
                }
                catch (Exception exception)
                {
                    interactionException = exception;
                    if (editor.IsVisible)
                    {
                        editor.Close();
                    }
                }
            }));
            var dialogResult = editor.ShowDialog();
            if (interactionException is not null)
            {
                throw new InvalidOperationException("规则编辑器模态交互抛出异常。", interactionException);
            }

            return new ModalEditorResult(dialogResult, editor.ResultRules);
        }
        finally
        {
            if (editor.IsVisible)
            {
                CloseWindow(editor);
            }
        }
    }

    private static ReminderRulesEditorWindow CreateOffscreenEditor(
        IReadOnlyList<ReminderRule> rules,
        IClock clock,
        IGuidSource? guidSource = null,
        DateTimeOffset? countdownTargetUtc = null)
    {
        return new ReminderRulesEditorWindow(
            rules,
            clock,
            guidSource,
            countdownTargetUtc: countdownTargetUtc)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0,
            Topmost = false
        };
    }

    private static void ShowAndSettle(ReminderRulesEditorWindow editor)
    {
        editor.Show();
        SettleWindow(editor);
    }

    private static void SettleWindow(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static void CloseWindow(Window window)
    {
        window.Close();
        WindowValidationInput.Pump(window.Dispatcher);
    }

    private static void InvokeButton(Window window, Button button)
    {
        if (!WindowValidationInput.InvokeButton(button))
        {
            throw new InvalidOperationException(
                $"按钮「{AutomationProperties.GetAutomationId(button)}/{button.Content}」不支持真实 Invoke。");
        }

        SettleWindow(window);
    }

    private static void SetSchedule(
        ReminderRulesEditorWindow editor,
        int modeIndex,
        DateTimeOffset dueBeijing,
        int intervalIndex,
        string customInterval)
    {
        FindByAutomationId<ComboBox>(editor, "ReminderRuleMode").SelectedIndex = modeIndex;
        FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate").SelectedDate = dueBeijing.Date;
        FindByAutomationId<ComboBox>(editor, "ReminderRuleFirstDueHour").SelectedIndex = dueBeijing.Hour;
        FindByAutomationId<ComboBox>(editor, "ReminderRuleFirstDueMinute").SelectedIndex = dueBeijing.Minute;
        if (modeIndex == 1)
        {
            FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval").SelectedIndex = intervalIndex;
            FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval").Text = customInterval;
        }

        SettleWindow(editor);
    }

    private static ScrollViewer FindRulesScroller(ReminderRulesEditorWindow editor) =>
        VisualDescendants<ScrollViewer>(editor)
            .Single(scroll => string.Equals(
                AutomationProperties.GetName(scroll),
                "已配置提醒规则",
                StringComparison.Ordinal));

    private static T FindByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        var matches = VisualDescendants<T>(root)
            .Where(element => string.Equals(
                AutomationProperties.GetAutomationId(element),
                automationId,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"期望找到 1 个 {typeof(T).Name}({automationId})，实际找到 {matches.Length} 个。");
        }

        return matches[0];
    }

    private static bool WindowInsideRect(Rect area, Window window) =>
        window.Left >= area.Left - 0.5 &&
        window.Top >= area.Top - 0.5 &&
        window.Left + window.ActualWidth <= area.Right + 0.5 &&
        window.Top + window.ActualHeight <= area.Bottom + 0.5;

    private static bool IsReachable(Window window, FrameworkElement element)
    {
        if (!element.IsVisible || !element.IsEnabled || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var bounds = Bounds(element, window);
        var windowBounds = new Rect(window.RenderSize);
        if (!windowBounds.Contains(bounds))
        {
            return false;
        }

        return element.InputHitTest(new Point(element.ActualWidth / 2, element.ActualHeight / 2)) is not null;
    }

    private static bool BrushColorEquals(Brush? actual, Brush? expected) =>
        actual is SolidColorBrush a &&
        expected is SolidColorBrush e &&
        a.Color == e.Color;

    private static readonly Lazy<string?> SharedEvidenceDirectory = new(() =>
    {
        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        var directory = Path.Combine(
            Path.GetFullPath(sourceRoot),
            "logs",
            "ui-captures",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-rules-editor-322");
        Directory.CreateDirectory(directory);
        return directory;
    });

    private static Action<Window, string>? CreateEvidenceWriter()
    {
        var directory = SharedEvidenceDirectory.Value;
        if (directory is null)
        {
            return null;
        }
        return (window, label) =>
        {
            if (window.Content is not FrameworkElement root)
            {
                throw new InvalidOperationException("规则编辑器实拍缺少根视觉。");
            }

            var originalOpacity = window.Opacity;
            try
            {
                TextOptions.SetTextRenderingMode(root, TextRenderingMode.Aliased);
                TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
                if (window.Opacity == 0)
                {
                    window.Opacity = 0.01;
                    SettleWindow(window);
                }

                var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
                var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                bitmap.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new FileStream(
                    Path.Combine(directory, label + ".png"),
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                encoder.Save(stream);
            }
            finally
            {
                window.Opacity = originalOpacity;
            }
        };
    }

    private sealed record ModalEditorResult(bool? DialogResult, IReadOnlyList<ReminderRule> Rules);

    private sealed class ValidationGuidSource(params Guid[] values) : IGuidSource
    {
        private readonly Queue<Guid> _values = new(values);

        public Guid NewGuid() => _values.Count > 0 ? _values.Dequeue() : Guid.NewGuid();
    }
}
