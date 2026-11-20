using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown.WindowVerification;

// #304：管理窗口五项对齐与空态细项的独立窗口验证。
// 1) 命令栏三枚命令按钮等高且垂直居中，数量文字共用同一中线，命令栏右缘对齐内容区右缘；页签容器、指示条与命中区不退化。
// 2) 切换倒计时/便签页签前后主标题水平位置不变，两页装饰图形（心形/小屋）均保留在标题之后。
// 3) 便签新增态与编辑态行尾留白等于行首留白，正文列吸收剩余宽度，「取消」内边距与其他次级按钮一致。
// 4) 首次空态填写引导常态可辨认为可点（暖粉底色、≥3:1 边框、≥4.5:1 文字与方向图标、悬停/焦点/系统对比度），点击只聚焦表单不新增不清草稿。
// 5) 系统对比度开启时装饰带布局空间收起不留空白（含运行中切换与对比度下新开窗口），关闭后恢复原样。
internal static class MainWindowAlignmentAndPolishScenario
{
    private const double GeometryTolerance = 0.5;
    private const double MinimumCompactHitTarget = 24.0;
    private const double MinimumSecondaryHitHeight = 28.0;
    private const double ExpectedBlockGap = 12.0;
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) RunCommandBarHeaderAndNoteRows(string meaning)
    {
        var failures = new List<string>();
        WithMainWindow(seedData: true, (main, workspace) =>
        {
            VerifyCommandBarAlignment(main, failures);
            VerifyTitleInvariantAcrossTabs(main, failures);
            VerifyNoteEditorAddAndEditRows(main, workspace, failures);
        });
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunEmptyStateAndHighContrastRibbon(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();
            try
            {
                WithMainWindow(seedData: false, (main, workspace) =>
                {
                    VerifyEmptyStateClickableAffordance(main, workspace, failures);
                    VerifyHighContrastDecorativeRibbonCollapse(main, theme, failures);
                });
            }
            catch (Exception exception)
            {
                failures.Add($"空态可点性与系统对比度装饰带收起验收异常：{exception.Message}");
            }
        }

        theme.Apply();
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    private static void VerifyCommandBarAlignment(MainWindow main, ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var surface = Required<Canvas>(main, "MainLayoutSurface");
        var commandBar = Required<Grid>(main, "MainCommandBarZone");
        var tabHost = Required<Grid>(main, "MainWorkspaceTabHost");
        var tabContainer = tabHost.Parent as Border
            ?? throw new InvalidOperationException("MainWorkspaceTabHost 缺少外层 Border 页签容器。");
        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        var notesTab = Required<ToggleButton>(main, "NotesTab");
        var gear = Required<Button>(main, "PreferencesButton");
        var filter = Required<Button>(main, "FilterButton");
        var tags = Required<Button>(main, "TagManagerButton");
        var count = Required<TextBlock>(main, "RecordCountText");

        Rect? initialTabContainerBounds = null;
        foreach (var (workspaceKind, contentSurfaceName, secondaryContentSurfaceName, label) in new[]
                 {
                     (WorkspaceKind.Countdowns, "CountdownEditorSurface", (string?)null, "倒计时页"),
                     (WorkspaceKind.Notes, "NoteEditorSurface", "NotesListSurface", "便签页")
                 })
        {
            probe.ShowWorkspace(workspaceKind);
            SettleLayout(main);
            _ = WindowValidationInput.PumpRenderedFrames(
                main.Dispatcher,
                500,
                () => main.VisibleSegmentIndicatorsSettledForCapture);
            SettleLayout(main);

            var commandBounds = Bounds(commandBar, surface);
            var tabContainerBounds = Bounds(tabContainer, surface);
            var gearBounds = Bounds(gear, surface);
            var filterBounds = Bounds(filter, surface);
            var tagsBounds = Bounds(tags, surface);
            var countBounds = Bounds(count, surface);
            var contentBounds = Bounds(Required<FrameworkElement>(main, contentSurfaceName), surface);

            if (initialTabContainerBounds is null)
            {
                initialTabContainerBounds = tabContainerBounds;
            }
            else if (!RectClose(initialTabContainerBounds.Value, tabContainerBounds))
            {
                failures.Add($"{label}：切页后页签容器几何变化（原 {FormatRect(initialTabContainerBounds.Value)}，现 {FormatRect(tabContainerBounds)}）");
            }

            if (Math.Abs(tabContainerBounds.Left - 30) > GeometryTolerance ||
                Math.Abs(tabContainerBounds.Width - 190) > GeometryTolerance ||
                Math.Abs(tabContainerBounds.Height - 51) > GeometryTolerance ||
                tabContainer.Padding != new Thickness(4, 3, 3, 4) ||
                Math.Abs(countdownTab.ActualWidth - 99) > GeometryTolerance ||
                Math.Abs(countdownTab.ActualHeight - 44) > GeometryTolerance ||
                Math.Abs(notesTab.ActualWidth - 82) > GeometryTolerance ||
                Math.Abs(notesTab.ActualHeight - 44) > GeometryTolerance)
            {
                failures.Add($"{label}：页签容器或页签尺寸偏离现役几何（container={FormatRect(tabContainerBounds)}, padding={tabContainer.Padding}）");
            }

            if (!main.VisibleSegmentIndicatorsSettledForCapture ||
                (workspaceKind == WorkspaceKind.Notes && !main.SegmentIndicatorGeometryMatchesForValidation))
            {
                failures.Add($"{label}：选中指示条未对齐当前激活页签/分段。");
            }

            if (Math.Abs(gearBounds.Height - filterBounds.Height) > GeometryTolerance ||
                Math.Abs(tagsBounds.Height - filterBounds.Height) > GeometryTolerance)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：三个命令按钮不等高（gear={gearBounds.Height:0.##}, filter={filterBounds.Height:0.##}, tags={tagsBounds.Height:0.##}）"));
            }

            if (gearBounds.Height < MinimumSecondaryHitHeight ||
                gearBounds.Width < 41 - GeometryTolerance ||
                filterBounds.Height < MinimumSecondaryHitHeight ||
                filterBounds.Width < 99 - GeometryTolerance ||
                tagsBounds.Height < MinimumSecondaryHitHeight ||
                tagsBounds.Width < 98 - GeometryTolerance ||
                gearBounds.Height < MinimumCompactHitTarget)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：命令按钮命中区低于下限（gear={gearBounds.Width:0.##}x{gearBounds.Height:0.##}, filter={filterBounds.Width:0.##}x{filterBounds.Height:0.##}, tags={tagsBounds.Width:0.##}x{tagsBounds.Height:0.##}）"));
            }

            var barCenterY = CenterY(commandBounds);
            var gearCenterY = CenterY(gearBounds);
            var filterCenterY = CenterY(filterBounds);
            var tagsCenterY = CenterY(tagsBounds);
            var countCenterY = CenterY(countBounds);
            if (Math.Abs(gearCenterY - barCenterY) > GeometryTolerance ||
                Math.Abs(filterCenterY - barCenterY) > GeometryTolerance ||
                Math.Abs(tagsCenterY - barCenterY) > GeometryTolerance)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：三个命令按钮未垂直居中或中线不一致（bar={barCenterY:0.##}, gear={gearCenterY:0.##}, filter={filterCenterY:0.##}, tags={tagsCenterY:0.##}）"));
            }

            if (Math.Abs(countCenterY - filterCenterY) > GeometryTolerance ||
                count.Padding != new Thickness(0))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：数量文字未与命令按钮共用垂直中线（count={countCenterY:0.##}, filter={filterCenterY:0.##}, padding={count.Padding}）"));
            }

            if (Math.Abs(commandBounds.Right - contentBounds.Right) > GeometryTolerance ||
                Math.Abs(countBounds.Right - contentBounds.Right) > GeometryTolerance)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：命令栏右缘未对齐内容区右缘（barRight={commandBounds.Right:0.##}, countRight={countBounds.Right:0.##}, contentRight={contentBounds.Right:0.##}）"));
            }

            if (secondaryContentSurfaceName is not null)
            {
                var secondaryBounds = Bounds(Required<FrameworkElement>(main, secondaryContentSurfaceName), surface);
                if (Math.Abs(commandBounds.Right - secondaryBounds.Right) > GeometryTolerance)
                {
                    failures.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{label}：命令栏右缘未对齐列表区右缘（barRight={commandBounds.Right:0.##}, listRight={secondaryBounds.Right:0.##}）"));
                }
            }
        }
    }

    private static void VerifyTitleInvariantAcrossTabs(MainWindow main, ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var surface = Required<Canvas>(main, "MainLayoutSurface");
        var titleZone = Required<StackPanel>(main, "MainTitleZone");
        var titleText = VisualDescendants<TextBlock>(titleZone)
            .Single(text => string.Equals(text.Text, "敏敏子的时光小屋", StringComparison.Ordinal));
        var subtitleText = VisualDescendants<TextBlock>(titleZone)
            .Single(text => string.Equals(text.Text, "重要时刻和今日努力，都在这里慢慢长大", StringComparison.Ordinal));
        var motifs = VisualDescendants<WpfPath>(titleZone).ToArray();
        if (motifs.Length != 2)
        {
            failures.Add($"主标题区应保留两枚页级装饰图形（心形与小屋），实际 {motifs.Length} 枚。");
            return;
        }

        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var countdownTitle = Bounds(titleText, surface);
        var countdownSubtitle = Bounds(subtitleText, surface);
        var visibleOnCountdowns = motifs.Where(path => path.IsVisible).ToArray();
        if (visibleOnCountdowns.Length != 1)
        {
            failures.Add($"倒计时页标题区应只显示一枚装饰图形，实际 {visibleOnCountdowns.Length} 枚。");
        }

        var countdownMotif = visibleOnCountdowns.FirstOrDefault();
        if (countdownMotif is not null)
        {
            var motifBounds = Bounds(countdownMotif, surface);
            if (countdownMotif.IsHitTestVisible || motifBounds.Left < countdownTitle.Right - GeometryTolerance)
            {
                failures.Add($"倒计时页标题装饰应位于标题之后且不可命中（motif={FormatRect(motifBounds)}, title={FormatRect(countdownTitle)}）。");
            }
        }

        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);
        var notesTitle = Bounds(titleText, surface);
        var notesSubtitle = Bounds(subtitleText, surface);
        var visibleOnNotes = motifs.Where(path => path.IsVisible).ToArray();
        if (visibleOnNotes.Length != 1)
        {
            failures.Add($"便签页标题区应只显示一枚装饰图形，实际 {visibleOnNotes.Length} 枚。");
        }

        var notesMotif = visibleOnNotes.FirstOrDefault();
        if (notesMotif is not null)
        {
            var motifBounds = Bounds(notesMotif, surface);
            if (notesMotif.IsHitTestVisible || motifBounds.Left < notesTitle.Right - GeometryTolerance)
            {
                failures.Add($"便签页标题装饰应位于标题之后且不可命中（motif={FormatRect(motifBounds)}, title={FormatRect(notesTitle)}）。");
            }
        }

        if (countdownMotif is not null && notesMotif is not null && ReferenceEquals(countdownMotif, notesMotif))
        {
            failures.Add("倒计时页与便签页未保留各自独立的标题装饰图形。");
        }

        if (Math.Abs(countdownTitle.Left - notesTitle.Left) > GeometryTolerance ||
            Math.Abs(countdownTitle.Top - notesTitle.Top) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"切页前后主标题位置变化（countdown={FormatRect(countdownTitle)}, notes={FormatRect(notesTitle)}）"));
        }

        if (Math.Abs(countdownTitle.Left - countdownSubtitle.Left) > GeometryTolerance ||
            Math.Abs(notesTitle.Left - notesSubtitle.Left) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"主标题与副标题左缘未对齐（countdownTitle={countdownTitle.Left:0.##}, subtitle={countdownSubtitle.Left:0.##}, notesTitle={notesTitle.Left:0.##}）"));
        }

        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var restoredTitle = Bounds(titleText, surface);
        if (Math.Abs(restoredTitle.Left - countdownTitle.Left) > GeometryTolerance)
        {
            failures.Add("切回倒计时页后主标题水平位置未保持不变。");
        }
    }

    private static void VerifyNoteEditorAddAndEditRows(
        MainWindow main,
        DesktopWorkspace workspace,
        ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var surface = Required<Canvas>(main, "MainLayoutSurface");
        var editorSurface = Required<Border>(main, "NoteEditorSurface");
        var noteText = Required<TextBox>(main, "NoteText");
        var noteDate = Required<DatePicker>(main, "NoteDate");
        var noteDateLabel = Required<TextBlock>(main, "NoteDateLabel");
        var saveButton = Required<Button>(main, "SaveNoteButton");
        var cancelButton = Required<Button>(main, "CancelNoteEdit");
        var secondaryReference = Required<Button>(main, "ManageNoteReminderRulesButton");

        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);

        if (cancelButton.Visibility != Visibility.Collapsed)
        {
            failures.Add("便签新增态不应显示取消编辑按钮。");
        }

        var cardAdd = Bounds(editorSurface, surface);
        var textAdd = Bounds(noteText, surface);
        var dateAdd = Bounds(noteDate, surface);
        var dateLabelAdd = Bounds(noteDateLabel, surface);
        var saveAdd = Bounds(saveButton, surface);
        var leadingAdd = textAdd.Left - cardAdd.Left;
        var trailingAdd = cardAdd.Right - saveAdd.Right;
        if (Math.Abs(trailingAdd - leadingAdd) > GeometryTolerance || leadingAdd < 16)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签新增态行尾留白不等于行首留白（leading={leadingAdd:0.##}, trailing={trailingAdd:0.##}）"));
        }

        if (Math.Abs(dateLabelAdd.Left - dateAdd.Left) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签新增态日期标签未与日期框左对齐（label={dateLabelAdd.Left:0.##}, date={dateAdd.Left:0.##}）"));
        }

        var targetNote = workspace.Snapshot.Settings.Notes.First();
        main.LoadNoteIntoEditor(targetNote);
        SettleLayout(main);

        if (!cancelButton.IsVisible)
        {
            failures.Add("便签编辑态未显示取消按钮。");
            return;
        }

        var cardEdit = Bounds(editorSurface, surface);
        var textEdit = Bounds(noteText, surface);
        var dateEdit = Bounds(noteDate, surface);
        var dateLabelEdit = Bounds(noteDateLabel, surface);
        var saveEdit = Bounds(saveButton, surface);
        var cancelEdit = Bounds(cancelButton, surface);
        var leadingEdit = textEdit.Left - cardEdit.Left;
        var trailingEdit = cardEdit.Right - cancelEdit.Right;
        if (Math.Abs(trailingEdit - leadingEdit) > GeometryTolerance ||
            Math.Abs(leadingEdit - leadingAdd) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签编辑态行尾留白不等于行首留白（leading={leadingEdit:0.##}, trailing={trailingEdit:0.##}, addLeading={leadingAdd:0.##}）"));
        }

        if (Math.Abs(dateLabelEdit.Left - dateEdit.Left) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签编辑态日期标签未与日期框左对齐（label={dateLabelEdit.Left:0.##}, date={dateEdit.Left:0.##}）"));
        }

        var expectedShrink = cancelEdit.Width + cancelButton.Margin.Left;
        var actualShrink = textAdd.Width - textEdit.Width;
        if (actualShrink <= 20 || Math.Abs(actualShrink - expectedShrink) > 1.0)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签编辑态正文列未吸收剩余宽度（addBody={textAdd.Width:0.##}, editBody={textEdit.Width:0.##}, expectedShrink={expectedShrink:0.##}）"));
        }

        if (cancelButton.Padding != secondaryReference.Padding)
        {
            failures.Add($"便签取消按钮内边距（{cancelButton.Padding}）与次级按钮（{secondaryReference.Padding}）不一致。");
        }

        if (cancelEdit.Left < saveEdit.Right + 4 - GeometryTolerance ||
            Math.Abs(cancelEdit.Height - saveEdit.Height) > GeometryTolerance ||
            Math.Abs(dateEdit.Width - 242) > GeometryTolerance)
        {
            failures.Add($"便签编辑态控件几何异常（dateW={dateEdit.Width:0.##}, save={FormatRect(saveEdit)}, cancel={FormatRect(cancelEdit)}）。");
        }

        cancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
        var textAfterCancel = Bounds(noteText, surface);
        var saveAfterCancel = Bounds(saveButton, surface);
        if (cancelButton.Visibility != Visibility.Collapsed ||
            Math.Abs(textAfterCancel.Width - textAdd.Width) > GeometryTolerance ||
            Math.Abs((cardAdd.Right - saveAfterCancel.Right) - leadingAdd) > GeometryTolerance)
        {
            failures.Add("取消便签编辑后未恢复新增态正文宽度与行尾留白。");
        }
    }

    private static void VerifyEmptyStateClickableAffordance(
        MainWindow main,
        DesktopWorkspace workspace,
        ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var primaryBrush = RequiredBrush(main, "MainActionBrush");
        var activeBrush = RequiredBrush(main, "ActiveBrush");
        var hoverBorderBrush = RequiredBrush(main, "ActiveHoverBrush");
        var hoverBgBrush = RequiredBrush(main, "PinkTintBrush");

        foreach (var (workspaceKind, guideName, editorName, outerBgKey, draft, countGetter, label) in new (
                     WorkspaceKind, string, string, string, string, Func<int>, string)[]
                 {
                     (WorkspaceKind.Countdowns, "AddEmptyCountdownButton", "CountdownName", "BackgroundBrush",
                         "空态点击不丢草稿-倒计时", () => workspace.Snapshot.Settings.Countdowns.Count, "倒计时首次空态引导"),
                     (WorkspaceKind.Notes, "AddEmptyNoteButton", "NoteText", "CardBrush",
                         "空态点击不丢草稿-便签", () => workspace.Snapshot.Settings.Notes.Count, "便签首次空态引导")
                 })
        {
            probe.ShowWorkspace(workspaceKind);
            main.Activate();
            Keyboard.Focus(Required<ToggleButton>(main, "CountdownTab"));
            SettleLayout(main);

            var guide = Required<Button>(main, guideName);
            var editor = Required<TextBox>(main, editorName);
            var outerBg = RequiredBrush(main, outerBgKey);
            guide.ApplyTemplate();
            var surface = guide.Template?.FindName("Surface", guide) as Border;
            var focusRing = guide.Template?.FindName("FocusRing", guide) as Border;
            if (!guide.IsVisible || surface is null || focusRing is null)
            {
                failures.Add($"{label}：按钮不可见或缺少模板部件 Surface/FocusRing。");
                continue;
            }

            if (guide.Cursor != Cursors.Hand || guide.ActualHeight < MinimumSecondaryHitHeight)
            {
                failures.Add($"{label}：常态手型指针或命中高度不足（cursor={guide.Cursor}, height={guide.ActualHeight:0.##}）。");
            }

            var restBg = surface.Background as SolidColorBrush;
            var restBorder = surface.BorderBrush as SolidColorBrush;
            var restFg = guide.Foreground as SolidColorBrush;
            if (restBg is null || restBg.Color.A != byte.MaxValue ||
                restBorder is null || restBorder.Color.A != byte.MaxValue ||
                surface.BorderThickness.Left < 1 || surface.BorderThickness.Top < 1 ||
                restFg is null || restFg.Color.A != byte.MaxValue)
            {
                failures.Add($"{label}：常态未呈现可辨认的不透明表面与边框（bg={surface.Background}, border={surface.BorderBrush}, thickness={surface.BorderThickness}）。");
            }
            else
            {
                if (restBg.Color == primaryBrush.Color)
                {
                    failures.Add($"{label}：空态引导不应冒充页面唯一实心主行动底色。");
                }

                if (!ApplicationTheme.MeetsContrast(outerBg.Color, restBorder.Color, 3.0) ||
                    !ApplicationTheme.MeetsContrast(restBg.Color, restBorder.Color, 3.0))
                {
                    failures.Add($"{label}：常态边框对比度不足 3:1（outer={outerBg.Color}, bg={restBg.Color}, border={restBorder.Color}）。");
                }

                if (!ApplicationTheme.MeetsContrast(restBg.Color, restFg.Color, 4.5))
                {
                    failures.Add($"{label}：常态文字对比度不足 4.5:1（bg={restBg.Color}, fg={restFg.Color}）。");
                }
            }

            var cuePath = VisualDescendants<WpfPath>(surface).FirstOrDefault(path => path.IsVisible);
            if (cuePath is null || cuePath.IsHitTestVisible)
            {
                failures.Add($"{label}：常态缺少可见且不可命中的矢量方向提示图标。");
            }

            // 悬停态反馈
            var restingBounds = Bounds(guide, main);
            WithPointerHover(main, guide, () =>
            {
                var hoverBg = surface.Background as SolidColorBrush;
                var hoverBorder = surface.BorderBrush as SolidColorBrush;
                if (!guide.IsMouseOver || hoverBg is null || hoverBorder is null ||
                    hoverBg.Color != hoverBgBrush.Color ||
                    hoverBorder.Color != hoverBorderBrush.Color ||
                    (restFg is not null && !ApplicationTheme.MeetsContrast(hoverBg.Color, restFg.Color, 4.5)) ||
                    !ApplicationTheme.MeetsContrast(outerBg.Color, hoverBorder.Color, 3.0))
                {
                    failures.Add($"{label}：悬停反馈色或对比度不符合合同（over={guide.IsMouseOver}, bg={surface.Background}, border={surface.BorderBrush}）。");
                }
            });

            // 键盘焦点环单层且不挤布局
            Keyboard.Focus(guide);
            FocusVisualState.SetIsKeyboardFocusVisible(guide, true);
            SettleLayout(main);
            var focusedBounds = Bounds(guide, main);
            var ringBrush = focusRing.BorderBrush as SolidColorBrush;
            if (!guide.IsKeyboardFocused ||
                ringBrush is null ||
                ringBrush.Color != activeBrush.Color ||
                !ApplicationTheme.MeetsContrast(outerBg.Color, ringBrush.Color, 3.0) ||
                !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(guide) ||
                !RectClose(restingBounds, focusedBounds))
            {
                failures.Add($"{label}：键盘焦点环颜色、单层描边或布局稳定性异常（focused={guide.IsKeyboardFocused}, ring={focusRing.BorderBrush}）。");
            }

            // 点击只把焦点交给表单输入，保留草稿且不新增记录
            editor.Text = draft;
            Keyboard.Focus(guide);
            SettleLayout(main);
            guide.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            if (!ReferenceEquals(Keyboard.FocusedElement, editor) ||
                !string.Equals(editor.Text, draft, StringComparison.Ordinal) ||
                countGetter() != 0)
            {
                failures.Add($"{label}：点击后未保持「只聚焦输入、保留草稿、不新增记录」（focused={Keyboard.FocusedElement}, text={editor.Text}, count={countGetter()}）。");
            }

            editor.Text = string.Empty;
            SettleLayout(main);
        }
    }

    private static void VerifyHighContrastDecorativeRibbonCollapse(
        MainWindow main,
        ApplicationTheme theme,
        ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var surface = Required<Canvas>(main, "MainLayoutSurface");
        var commandBar = Required<Grid>(main, "MainCommandBarZone");
        var statusBar = Required<Border>(main, "MainStatusBarSurface");
        var countdownEditor = Required<Border>(main, "CountdownEditorSurface");
        var countdownList = Required<ScrollViewer>(main, "CountdownListRegion");
        var noteEditor = Required<Border>(main, "NoteEditorSurface");
        var notesListSurface = Required<Border>(main, "NotesListSurface");
        var notesList = Required<ScrollViewer>(main, "NotesListRegion");
        var decorationLayer = Required<Canvas>(main, "MainReferenceDecorationLayer");

        // 1) 记录日间基线布局
        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var dayCommandBottom = Bounds(commandBar, surface).Bottom;
        var dayCountdownEditorTop = Bounds(countdownEditor, surface).Top;
        var dayCountdownListHeight = countdownList.ActualHeight;
        var dayCountdownGap = dayCountdownEditorTop - dayCommandBottom;

        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);
        var dayNotesEditorTop = Bounds(noteEditor, surface).Top;
        var dayNotesListHeight = notesList.ActualHeight;
        var dayNotesGap = dayNotesEditorTop - dayCommandBottom;

        if (dayCountdownGap < 100 || dayNotesGap < 110)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"日间装饰带高度异常（countdownGap={dayCountdownGap:0.##}, notesGap={dayNotesGap:0.##}）"));
        }

        // 2) 开启系统对比度：装饰带与布局空间全部收起，不留空白，且空态引导在系统对比度下保持可读可辨
        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            SettleLayout(main);

            if (decorationLayer.Visibility != Visibility.Collapsed)
            {
                failures.Add("系统对比度开启时主装饰图层未收起。");
            }

            foreach (var spacerName in new[]
                     {
                         "MainDecorationBandSpacer",
                         "CountdownDecorationSpacer",
                         "CountdownDecorationBottomSpacer",
                         "NotesDecorationSpacer"
                     })
            {
                var spacer = main.FindName(spacerName) as FrameworkElement;
                if (spacer is null || spacer.Visibility != Visibility.Collapsed || spacer.ActualHeight > 0.01)
                {
                    failures.Add($"系统对比度开启时装饰占位 {spacerName} 未 Collapsed（vis={spacer?.Visibility}, h={spacer?.ActualHeight:0.##}）。");
                }
            }

            probe.ShowWorkspace(WorkspaceKind.Countdowns);
            SettleLayout(main);
            var hcCommandBottom = Bounds(commandBar, surface).Bottom;
            var hcCountdownEditorTop = Bounds(countdownEditor, surface).Top;
            var hcCountdownGap = hcCountdownEditorTop - hcCommandBottom;
            if (hcCountdownGap < ExpectedBlockGap - GeometryTolerance ||
                hcCountdownGap > ExpectedBlockGap + GeometryTolerance ||
                countdownList.ActualHeight <= dayCountdownListHeight + 90)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"系统对比度下倒计时页装饰带仍留空白（gap={hcCountdownGap:0.##}, dayListH={dayCountdownListHeight:0.##}, hcListH={countdownList.ActualHeight:0.##}）"));
            }

            VerifyEmptyGuideHighContrast(main, "AddEmptyCountdownButton", "倒计时空态引导（高对比度）", failures);

            probe.ShowWorkspace(WorkspaceKind.Notes);
            SettleLayout(main);
            var hcNotesEditorTop = Bounds(noteEditor, surface).Top;
            var hcNotesGap = hcNotesEditorTop - hcCommandBottom;
            var hcNotesToStatus = Bounds(statusBar, surface).Top - Bounds(notesListSurface, surface).Bottom;
            if (hcNotesGap < ExpectedBlockGap - GeometryTolerance ||
                hcNotesGap > ExpectedBlockGap + GeometryTolerance ||
                Math.Abs(hcNotesToStatus - ExpectedBlockGap) > 1.0 ||
                notesList.ActualHeight <= dayNotesListHeight + 90)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"系统对比度下便签页装饰带仍留空白（gap={hcNotesGap:0.##}, toStatus={hcNotesToStatus:0.##}, dayListH={dayNotesListHeight:0.##}, hcListH={notesList.ActualHeight:0.##}）"));
            }

            VerifyEmptyGuideHighContrast(main, "AddEmptyNoteButton", "便签空态引导（高对比度）", failures);

            // 对比度已开启时新打开的管理窗口也必须直接收起装饰带空间
            WithMainWindow(seedData: false, (freshMain, _) =>
            {
                var freshProbe = new MainWindowEmptyStateProbe(freshMain);
                var freshSurface = Required<Canvas>(freshMain, "MainLayoutSurface");
                var freshBarBottom = Bounds(Required<Grid>(freshMain, "MainCommandBarZone"), freshSurface).Bottom;
                freshProbe.ShowWorkspace(WorkspaceKind.Countdowns);
                SettleLayout(freshMain);
                var freshCountdownGap = Bounds(Required<Border>(freshMain, "CountdownEditorSurface"), freshSurface).Top - freshBarBottom;
                freshProbe.ShowWorkspace(WorkspaceKind.Notes);
                SettleLayout(freshMain);
                var freshNotesGap = Bounds(Required<Border>(freshMain, "NoteEditorSurface"), freshSurface).Top - freshBarBottom;
                if (Math.Abs(freshCountdownGap - ExpectedBlockGap) > GeometryTolerance ||
                    Math.Abs(freshNotesGap - ExpectedBlockGap) > GeometryTolerance)
                {
                    failures.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"系统对比度已开启时新开主窗未直接收起装饰带（countdownGap={freshCountdownGap:0.##}, notesGap={freshNotesGap:0.##}）"));
                }
            });
        }

        // 3) 关闭系统对比度：完整恢复日间布局与装饰带
        theme.Apply();
        SettleLayout(main);
        if (decorationLayer.Visibility != Visibility.Visible)
        {
            failures.Add("关闭系统对比度后主装饰图层未恢复显示。");
        }

        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var restoredCountdownEditorTop = Bounds(countdownEditor, surface).Top;
        if (Math.Abs(restoredCountdownEditorTop - dayCountdownEditorTop) > GeometryTolerance ||
            Math.Abs(countdownList.ActualHeight - dayCountdownListHeight) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"关闭系统对比度后倒计时页未恢复原样（top={restoredCountdownEditorTop:0.##} vs {dayCountdownEditorTop:0.##}, listH={countdownList.ActualHeight:0.##} vs {dayCountdownListHeight:0.##}）"));
        }

        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);
        var restoredNotesEditorTop = Bounds(noteEditor, surface).Top;
        if (Math.Abs(restoredNotesEditorTop - dayNotesEditorTop) > GeometryTolerance ||
            Math.Abs(notesList.ActualHeight - dayNotesListHeight) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"关闭系统对比度后便签页未恢复原样（top={restoredNotesEditorTop:0.##} vs {dayNotesEditorTop:0.##}, listH={notesList.ActualHeight:0.##} vs {dayNotesListHeight:0.##}）"));
        }
    }

    private static void VerifyEmptyGuideHighContrast(
        MainWindow main,
        string guideName,
        string label,
        ICollection<string> failures)
    {
        var guide = Required<Button>(main, guideName);
        guide.ApplyTemplate();
        var surface = guide.Template?.FindName("Surface", guide) as Border;
        var focusRing = guide.Template?.FindName("FocusRing", guide) as Border;
        if (surface is null || focusRing is null)
        {
            failures.Add($"{label}：缺少模板部件 Surface/FocusRing。");
            return;
        }

        if (surface.Background is not SolidColorBrush bg || bg.Color != SystemColors.WindowColor ||
            surface.BorderBrush is not SolidColorBrush border || border.Color != SystemColors.WindowTextColor ||
            guide.Foreground is not SolidColorBrush fg || fg.Color != SystemColors.WindowTextColor)
        {
            failures.Add($"{label}：系统对比度常态映射不正确（bg={surface.Background}, border={surface.BorderBrush}, fg={guide.Foreground}）。");
        }

        main.Activate();
        Keyboard.Focus(guide);
        FocusVisualState.SetIsKeyboardFocusVisible(guide, true);
        SettleLayout(main);
        if (focusRing.BorderBrush is not SolidColorBrush ring || ring.Color != SystemColors.HighlightColor)
        {
            failures.Add($"{label}：系统对比度焦点环未映射系统高亮色（ring={focusRing.BorderBrush}）。");
        }
    }

    private static void WithMainWindow(bool seedData, Action<MainWindow, DesktopWorkspace> verify)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-align-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            if (seedData)
            {
                SaveCountdown(workspace, "命令栏对齐倒计时", 5, Application.Current.Dispatcher);
                SaveNote(workspace, "便签编辑行对齐验证正文", Application.Current.Dispatcher);
            }

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
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
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            var contentRendered = false;
            main.ContentRendered += (_, _) => contentRendered = true;
            main.Show();
            _ = WindowValidationInput.PumpRenderedFrames(main.Dispatcher, 2000, () => contentRendered);
            SettleLayout(main);
            verify(main, workspace);
        }
        finally
        {
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

    private static void WithPointerHover(MainWindow main, Control target, Action verify)
    {
        target.BringIntoView();
        SettleLayout(main);
        if (PresentationSource.FromVisual(target) is not HwndSource source ||
            source.RootVisual is not Visual root)
        {
            throw new InvalidOperationException($"{target.Name} 缺少用于悬停探针的 HWND。");
        }

        var point = target.TransformToAncestor(root).Transform(
            new Point(target.RenderSize.Width / 2, target.RenderSize.Height / 2));
        point = source.CompositionTarget.TransformToDevice.Transform(point);
        var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
        main.Activate();
        SetCapture(source.Handle);
        try
        {
            if (GetCapture() != source.Handle)
            {
                throw new InvalidOperationException("隔离验证窗口无法捕获鼠标悬停消息。");
            }

            SendMessage(source.Handle, 0x0200, IntPtr.Zero, coordinates);
            SettleLayout(main);
            verify();
        }
        finally
        {
            if (GetCapture() == source.Handle)
            {
                ReleaseCapture();
            }

            SendMessage(source.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            SettleLayout(main);
        }
    }

    private static double CenterY(Rect rect) => rect.Top + (rect.Height / 2);

    private static bool RectClose(Rect a, Rect b) =>
        Math.Abs(a.Left - b.Left) <= GeometryTolerance &&
        Math.Abs(a.Top - b.Top) <= GeometryTolerance &&
        Math.Abs(a.Width - b.Width) <= GeometryTolerance &&
        Math.Abs(a.Height - b.Height) <= GeometryTolerance;

    private static string FormatRect(Rect r) =>
        string.Create(CultureInfo.InvariantCulture, $"({r.Left:0.##},{r.Top:0.##},{r.Width:0.##}x{r.Height:0.##})");

    private static SolidColorBrush RequiredBrush(FrameworkElement element, string key) =>
        element.FindResource(key) as SolidColorBrush
        ?? throw new InvalidOperationException($"缺少画刷资源 {key}。");

    private static T Required<T>(MainWindow main, string name) where T : class =>
        main.FindName(name) as T ?? throw new InvalidOperationException($"管理窗口缺少控件 {name}（{typeof(T).Name}）。");

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
}
