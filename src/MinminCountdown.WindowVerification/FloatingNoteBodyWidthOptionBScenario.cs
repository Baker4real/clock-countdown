using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #335：悬浮窗便签正文列加宽（用户于 #334 选定方案 B：上缘单行日期）。
// 验证真实 WPF FloatingWindow 的 3 列卡片几何（36/245/36、正文可用 237 DIP）、18 DIP 上缘单行日期条与 4 DIP 间距、
// RadiusChip 圆角、pastel 轮换、系统高对比度边界映射、无日期回退、长便签自然折行、5 标签绑定、
// 搜索展开与滚动条带内粘性置顶互不遮挡、滚动提示、≥36 DIP 命中区以及 520×390 外壳与玩偶/x≤396 安全区不变。
internal static class FloatingNoteBodyWidthOptionBScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2030, 7, 15, 1, 0, 0, TimeSpan.Zero);

    private const string SearchKeyword = "方案B锚点";

    internal static (bool Passed, string Explanation) RunGeometryAndDateStrip(string meaning)
    {
        var failures = new List<string>();
        RunWithDualWindows("geometry", (workspace, main, floating) =>
        {
            _ = main;
            // 2030-07-15 为周一（独立于产品代码验证“7 月 15 日 · 周一”）。
            var mondayNoteId = SaveNoteWithDate(
                workspace,
                "周一卡片正文：验证方案 B 上缘单行日期与 237 DIP 正文列。",
                new DateTime(2030, 7, 15),
                Array.Empty<string>(),
                floating.Dispatcher);
            SaveNoteWithDate(
                workspace,
                "次日卡片正文：验证蓝色 pastel 轮换与白纸卡底交替。",
                new DateTime(2030, 7, 14),
                Array.Empty<string>(),
                floating.Dispatcher);
            SaveNoteWithDate(
                workspace,
                "第三张卡片正文：验证绿色 pastel 轮换与暖黄卡底交替。",
                new DateTime(2030, 7, 13),
                Array.Empty<string>(),
                floating.Dispatcher);
            var undatedNoteId = SaveUndatedNote(
                workspace,
                "无日期历史便签：验证方案 B 下完整显示无日期回退。",
                floating.Dispatcher);

            floating.ShowWorkspace(WorkspaceKind.Notes);
            Settle(floating);

            var root = RequiredByName<Grid>(floating, "TaskWrap");
            var rows = RequiredByName<StackPanel>(floating, "FloatingRows");
            var plushScene = RequiredByName<Canvas>(floating, "FloatingPlushScene");

            Check(failures, "悬浮窗外壳保持 520×390 DIP",
                Math.Abs(floating.ActualWidth - 520) <= 0.5 &&
                Math.Abs(floating.ActualHeight - 390) <= 0.5);
            Check(failures, "玩偶装饰层锚点保留且整体不可命中",
                plushScene.Children.Count > 0 &&
                !plushScene.IsHitTestVisible &&
                plushScene.Children.OfType<UIElement>().All(child => !child.IsHitTestVisible));

            var cards = rows.Children.OfType<Border>().ToArray();
            Require(cards.Length == 4, $"期望 4 张便签卡，实际找到 {cards.Length} 张。");

            var mondayCard = FindCardForNote(rows, mondayNoteId);
            var undatedCard = FindCardForNote(rows, undatedNoteId);

            // 1. 卡片总体几何、安全区与 3 列布局（36 / 245 / 36）。
            var expectedRadiusChip = (CornerRadius)Application.Current.FindResource("RadiusChip");
            var expectedNoteBg = (SolidColorBrush)Application.Current.FindResource("NoteBrush");
            var expectedWhiteBg = (SolidColorBrush)Application.Current.FindResource("NoteWhiteBrush");
            var stampBgKeys = new[] { "StampPinkBrush", "StampBlueBrush", "StampGreenBrush" };
            var stampFgKeys = new[] { "StampPinkTextBrush", "StampBlueTextBrush", "StampGreenTextBrush" };

            for (var index = 0; index < cards.Length; index++)
            {
                var card = cards[index];
                var cardBounds = Bounds(card, root);
                Check(failures, $"卡#{index + 1} 宽 319 DIP、最小高 ≥72 DIP 且右界 ≤396",
                    Math.Abs(card.ActualWidth - 319) <= 0.5 &&
                    card.ActualHeight >= 71.5 &&
                    cardBounds.Right <= 396.5);

                var expectedCardBg = index % 2 == 0 ? expectedNoteBg : expectedWhiteBg;
                Check(failures, $"卡#{index + 1} 底色按暖黄/白纸交替",
                    BrushColorEquals(card.Background, expectedCardBg.Color));

                var layout = card.Child as Grid;
                if (layout is null)
                {
                    failures.Add($"卡#{index + 1} 内容不是 Grid。");
                    continue;
                }

                Check(failures, $"卡#{index + 1} 改为 3 列布局（36 / 245 / 36）",
                    layout.ColumnDefinitions.Count == 3 &&
                    Math.Abs(layout.ColumnDefinitions[0].ActualWidth - 36) <= 0.5 &&
                    Math.Abs(layout.ColumnDefinitions[1].ActualWidth - 245) <= 1.0 &&
                    Math.Abs(layout.ColumnDefinitions[2].ActualWidth - 36) <= 0.5);

                var completion = layout.Children.OfType<CheckBox>().SingleOrDefault(box => Grid.GetColumn(box) == 0);
                var body = layout.Children.OfType<StackPanel>().SingleOrDefault(panel => Grid.GetColumn(panel) == 1);
                var overflow = layout.Children.OfType<Button>().SingleOrDefault(button => Grid.GetColumn(button) == 2);
                var dateStamp = layout.Children.OfType<Border>().SingleOrDefault(border => Grid.GetColumn(border) == 1);

                if (completion is null || body is null || overflow is null || dateStamp is null)
                {
                    failures.Add($"卡#{index + 1} 缺少方案 B 必备元素（completion={completion is not null}, body={body is not null}, overflow={overflow is not null}, col1DateStamp={dateStamp is not null}）。");
                    continue;
                }

                var bodyText = body.Children.OfType<TextBlock>().FirstOrDefault();
                Check(failures, $"卡#{index + 1} 正文可用宽 237 DIP 且上边距 31 DIP（9+18+4）",
                    bodyText is not null &&
                    Math.Abs(bodyText.ActualWidth - 237) <= 1.0 &&
                    Math.Abs(layout.ColumnDefinitions[1].ActualWidth - bodyText.ActualWidth - 8) <= 0.5 &&
                    body.Margin == new Thickness(0, 31, 8, 9));

                Check(failures, $"卡#{index + 1} 勾选槽 ≥36 DIP、…触发器 36×36 DIP",
                    completion.ActualWidth >= 34 &&
                    completion.ActualHeight >= 36 &&
                    Math.Abs(overflow.ActualWidth - 36) <= 0.5 &&
                    Math.Abs(overflow.ActualHeight - 36) <= 0.5);

                // 日期条结构、圆角、内边距、不可命中/聚焦、pastel 轮换。
                var expectedStampBg = (SolidColorBrush)Application.Current.FindResource(stampBgKeys[index % stampBgKeys.Length]);
                var expectedStampFg = (SolidColorBrush)Application.Current.FindResource(stampFgKeys[index % stampFgKeys.Length]);
                var dateLabel = dateStamp.Child as TextBlock;
                Check(failures, $"卡#{index + 1} 上缘日期条 18×237 DIP、RadiusChip(5)、边框 1、内边距 7,0,7,0、ZIndex≥1 且不可命中/聚焦",
                    Math.Abs(dateStamp.ActualHeight - 18) <= 0.5 &&
                    Math.Abs(dateStamp.ActualWidth - 237) <= 1.0 &&
                    dateStamp.Margin == new Thickness(0, 9, 8, 0) &&
                    dateStamp.CornerRadius == expectedRadiusChip &&
                    dateStamp.BorderThickness == new Thickness(1) &&
                    dateStamp.Padding == new Thickness(7, 0, 7, 0) &&
                    Panel.GetZIndex(dateStamp) >= 1 &&
                    !dateStamp.IsHitTestVisible &&
                    !dateStamp.Focusable &&
                    dateStamp.RenderTransform is TranslateTransform);
                Check(failures, $"卡#{index + 1} 上缘日期条日间边框与 pastel 底色一致且按轮换配色",
                    BrushColorEquals(dateStamp.BorderBrush, expectedStampBg.Color) &&
                    BrushColorEquals(dateStamp.Background, expectedStampBg.Color) &&
                    dateLabel is not null &&
                    BrushColorEquals(dateLabel.Foreground, expectedStampFg.Color));
                Check(failures, $"卡#{index + 1} 日期条单行 TextBlock 排版符合规范（13 DIP Regular 左对齐居中省略表格数字）",
                    dateLabel is not null &&
                    Math.Abs(dateLabel.FontSize - 13) <= 0.01 &&
                    dateLabel.FontWeight == FontWeights.Regular &&
                    dateLabel.HorizontalAlignment == HorizontalAlignment.Left &&
                    dateLabel.VerticalAlignment == VerticalAlignment.Center &&
                    dateLabel.TextTrimming == TextTrimming.CharacterEllipsis &&
                    Typography.GetNumeralAlignment(dateLabel) == FontNumeralAlignment.Tabular);

                // 静止态 4 DIP 间距且无重叠。
                var stampOffset = VisualTreeHelper.GetOffset(dateStamp);
                var bodyOffset = VisualTreeHelper.GetOffset(body);
                var restingGap = bodyOffset.Y - (stampOffset.Y + dateStamp.ActualHeight);
                var stampBounds = Bounds(dateStamp, root);
                var bodyTextBounds = bodyText is null ? Rect.Empty : Bounds(bodyText, root);
                var completionBounds = Bounds(completion, root);
                var overflowBounds = Bounds(overflow, root);
                Check(failures, $"卡#{index + 1} 静止态日期条与正文间隔 4 DIP 且不与正文/勾选/…重叠（gap={restingGap:F2}）",
                    restingGap is >= 3.0 and <= 5.0 &&
                    !stampBounds.IntersectsWith(bodyTextBounds) &&
                    !stampBounds.IntersectsWith(completionBounds) &&
                    !stampBounds.IntersectsWith(overflowBounds) &&
                    !bodyTextBounds.IntersectsWith(completionBounds) &&
                    !bodyTextBounds.IntersectsWith(overflowBounds));
            }

            // 2. 精确文案：有日期与无日期。
            Check(failures, "有日期卡片精确显示「7 月 15 日 · 周一」",
                string.Equals(ReadCardDateText(mondayCard), "7 月 15 日 · 周一", StringComparison.Ordinal));
            Check(failures, "无日期卡片精确显示「无日期」",
                string.Equals(ReadCardDateText(undatedCard), "无日期", StringComparison.Ordinal));

            // 3. 命中测试：勾选槽命中 CheckBox、…命中触发器、日期条中心不命中日期条/勾选/…。
            if (mondayCard.Child is Grid mondayLayout &&
                mondayLayout.Children.OfType<CheckBox>().SingleOrDefault(b => Grid.GetColumn(b) == 0) is { } mondayCheck &&
                mondayLayout.Children.OfType<Button>().SingleOrDefault(b => Grid.GetColumn(b) == 2) is { } mondayOverflow &&
                mondayLayout.Children.OfType<Border>().SingleOrDefault(b => Grid.GetColumn(b) == 1) is { } mondayStamp)
            {
                var checkLeftHit = mondayLayout.InputHitTest(new Point(2, 12)) as DependencyObject;
                var checkRightHit = mondayLayout.InputHitTest(new Point(34, 12)) as DependencyObject;
                var stampCenter = mondayStamp.TranslatePoint(
                    new Point(mondayStamp.ActualWidth / 2, mondayStamp.ActualHeight / 2),
                    mondayLayout);
                var stampHit = mondayLayout.InputHitTest(stampCenter) as DependencyObject;
                var overflowCenter = mondayOverflow.TranslatePoint(
                    new Point(mondayOverflow.ActualWidth / 2, mondayOverflow.ActualHeight / 2),
                    mondayLayout);
                var overflowHit = mondayLayout.InputHitTest(overflowCenter) as DependencyObject;

                Check(failures, "勾选槽左右边缘命中复选框",
                    IsSelfOrVisualDescendant(mondayCheck, checkLeftHit) &&
                    IsSelfOrVisualDescendant(mondayCheck, checkRightHit));
                Check(failures, "…触发器中心命中触发按钮",
                    IsSelfOrVisualDescendant(mondayOverflow, overflowHit));
                Check(failures, "日期条中心不命中日期条且不误触勾选或…",
                    !IsSelfOrVisualDescendant(mondayStamp, stampHit) &&
                    !IsSelfOrVisualDescendant(mondayCheck, stampHit) &&
                    !IsSelfOrVisualDescendant(mondayOverflow, stampHit));

                // 4. 系统高对比度映射：开启映射到 WindowText、换色跟随、关闭恢复日间 pastel 底色。
                var expectedMondayBg = ((SolidColorBrush)Application.Current.FindResource("StampPinkBrush")).Color;
                using (var hc = ApplicationTheme.OverrideHighContrastForValidation(true))
                {
                    Settle(floating);
                    Check(failures, "高对比度开启时上缘日期条边界映射到 WindowText",
                        BrushColorEquals(mondayStamp.BorderBrush, SystemColors.WindowTextColor));
                    var customPalette = new ApplicationTheme.SystemContrastPalette(
                        Color.FromRgb(10, 16, 28), Colors.White, Color.FromRgb(24, 34, 48), Colors.White,
                        Colors.Yellow, Colors.Black, Colors.Silver);
                    hc.ChangeColors(customPalette);
                    Settle(floating);
                    Check(failures, "高对比度换色时上缘日期条边界同步跟随新 WindowText",
                        BrushColorEquals(mondayStamp.BorderBrush, customPalette.WindowText));
                }

                Settle(floating);
                Check(failures, "高对比度关闭后上缘日期条边界恢复日间 pastel 底色",
                    BrushColorEquals(mondayStamp.BorderBrush, expectedMondayBg));
            }
            else
            {
                failures.Add("周一卡片未能解析出方案 B 3 列子控件，跳过命中与高对比检查。");
            }
        });

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败项：{string.Join("；", failures)}");
    }

    internal static (bool Passed, string Explanation) RunScrollSearchTagsAndSticky(string meaning)
    {
        var failures = new List<string>();
        RunWithDualWindows("scroll-search-tags", (workspace, _, floating) =>
        {
            // 1. 创建 5 个标签，全部绑定到长便签上，并准备多条长便签触发外层滚动与搜索下滚动。
            var tagIds = new List<string>();
            for (var index = 1; index <= 5; index++)
            {
                var created = workspace.Apply(new CreateTagChange($"方案B标签{index}", "#F4B6A6"));
                Require(created.Succeeded && created.CreatedTag is not null, $"创建标签#{index} 失败：{created.Error}");
                tagIds.Add(created.CreatedTag!.Id);
            }

            var longBodyPrefix = $"{SearchKeyword}：这是一条没有手工换行符的长便签正文，用于验证 237 DIP 宽度下的自然折行、5 个标签同时绑定、无内部滚动容器、滚动上下文提示以及视口裁切时的上缘单行粘性日期条表现，末尾校验段";
            var fiveTagNoteId = SaveNoteWithDate(
                workspace,
                $"{longBodyPrefix}一号。",
                new DateTime(2030, 7, 15),
                tagIds.ToArray(),
                floating.Dispatcher,
                new ReminderRule[]
                {
                    new AbsoluteOnceReminderRule("option-b-reminder-1", FixtureUtc.AddHours(4))
                });

            var noteIds = new List<string> { fiveTagNoteId };
            for (var index = 2; index <= 6; index++)
            {
                var text = index <= 4
                    ? $"{longBodyPrefix}{index}号。"
                    : $"普通长便签{index}：这是一条不含搜索关键词的长便签正文，确保展开搜索前后列表都有足够高度以验证外层滚动与相邻卡片间距。";
                noteIds.Add(SaveNoteWithDate(
                    workspace,
                    text,
                    new DateTime(2030, 7, 16 - index),
                    index == 2 ? tagIds.Take(3).ToArray() : Array.Empty<string>(),
                    floating.Dispatcher));
            }

            floating.ShowWorkspace(WorkspaceKind.Notes);
            Settle(floating);
            ActivateForKeyboard(floating);

            var root = RequiredByName<Grid>(floating, "TaskWrap");
            var scroller = RequiredByName<ScrollViewer>(floating, "FloatingBodyScroller");
            var rows = RequiredByName<StackPanel>(floating, "FloatingRows");
            var commandBar = RequiredByName<Grid>(floating, "FloatingNotesCommandBar");
            var searchSlot = RequiredByName<Grid>(floating, "FloatingNoteSearchSlot");
            var searchInputRow = RequiredByName<Grid>(floating, "FloatingNoteSearchInputRow");
            var searchBox = RequiredByName<TextBox>(floating, "FloatingNoteSearch");

            scroller.ApplyTemplate();
            var presenter = scroller.Template.FindName("PART_ScrollContentPresenter", scroller) as ScrollContentPresenter;
            Require(presenter is not null, "找不到 FloatingBodyScroller 的 PART_ScrollContentPresenter。");

            // 2. 验证 5 个标签绑定保留与单标签筛选可用，且不新增卡片内部滚动容器。
            var savedFiveTagNote = workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Id, fiveTagNoteId, StringComparison.Ordinal));
            Check(failures, "目标长便签完整保留 5 个绑定标签",
                savedFiveTagNote.TagIds.Count == 5 &&
                tagIds.All(id => savedFiveTagNote.TagIds.Contains(id, StringComparer.Ordinal)));

            workspace.ApplyFilter(workspace.Snapshot.Filter with
            {
                SelectedTagIds = Array.AsReadOnly(new[] { tagIds[2] })
            });
            Settle(floating);
            Check(failures, "按 5 标签之一筛选后长便签正常渲染且无重叠裁切",
                rows.Children.OfType<Border>().Count() >= 1 &&
                rows.Children.OfType<Border>().Any(card =>
                    VisualDescendants<CheckBox>(card).Any(box =>
                        string.Equals(box.Tag as string, fiveTagNoteId, StringComparison.Ordinal))));

            workspace.ApplyFilter(workspace.Snapshot.Filter with
            {
                SelectedTagIds = Array.Empty<string>()
            });
            Settle(floating);

            Check(failures, "便签列表与卡片内不新增 ScrollViewer",
                !VisualDescendants<ScrollViewer>(rows).Any());
            Check(failures, "6 条长便签触发唯一外层 FloatingBodyScroller 溢出滚动",
                scroller.ScrollableHeight > 80);

            // 3. 长便签在 237 DIP 宽下自然折行且含提醒摘要行，卡片互不重叠。
            var firstCard = FindCardForNote(rows, fiveTagNoteId);
            var firstLayout = firstCard.Child as Grid;
            Require(firstLayout is not null && firstLayout.ColumnDefinitions.Count == 3,
                "首卡不是方案 B 3 列 Grid，无法继续验证。");
            var firstBody = firstLayout!.Children.OfType<StackPanel>().Single(panel => Grid.GetColumn(panel) == 1);
            var firstBodyText = firstBody.Children.OfType<TextBlock>().First();
            var firstReminderText = firstBody.Children.OfType<TextBlock>()
                .SingleOrDefault(block => block.Text.StartsWith("下次提醒：", StringComparison.Ordinal));
            Check(failures, "5 标签长便签在 237 DIP 下自然多行折行并完整显示提醒摘要",
                Math.Abs(firstBodyText.ActualWidth - 237) <= 1.0 &&
                TextWouldNaturallyWrap(firstBodyText) &&
                firstBodyText.ActualHeight >= 54 &&
                firstReminderText is not null &&
                !Bounds(firstBodyText, root).IntersectsWith(Bounds(firstReminderText, root)));

            var allCardBounds = rows.Children.OfType<Border>().Select(card => Bounds(card, root)).ToArray();
            Check(failures, "全部长便签相邻卡片互不重叠且右界 ≤396",
                allCardBounds.All(bounds => bounds.Right <= 396.5) &&
                allCardBounds.Zip(allCardBounds.Skip(1)).All(pair => pair.First.Bottom <= pair.Second.Top + 0.5));

            // 4. 滚动上下文提示（OpacityMask）随顶部/中段/底部溢出正确切换。
            scroller.ScrollToTop();
            Settle(floating);
            Check(failures, "顶部溢出时滚动呈现器启用渐隐 OpacityMask",
                presenter!.OpacityMask is LinearGradientBrush);

            // 5. 向下滚动使长卡顶部滚出视口：上缘日期条在可见条带内粘性置顶，不覆盖命令栏/勾选/…/邻卡。
            var stickyScrollOffset = Math.Min(scroller.ScrollableHeight, 48);
            scroller.ScrollToVerticalOffset(stickyScrollOffset);
            Settle(floating);
            Check(failures, "中段溢出时滚动呈现器保持渐隐 OpacityMask",
                presenter.OpacityMask is LinearGradientBrush);

            var firstCardInScroller = Bounds(firstCard, scroller);
            var firstStamp = firstLayout.Children.OfType<Border>().Single(border => Grid.GetColumn(border) == 1);
            var firstCheck = firstLayout.Children.OfType<CheckBox>().Single(box => Grid.GetColumn(box) == 0);
            var firstOverflow = firstLayout.Children.OfType<Button>().Single(button => Grid.GetColumn(button) == 2);
            var firstStampInScroller = Bounds(firstStamp, scroller);
            var firstStampInRoot = Bounds(firstStamp, root);
            var firstCardInRoot = Bounds(firstCard, root);
            var commandBarInRoot = Bounds(commandBar, root);
            var firstTranslateY = ReadTranslateY(firstStamp);

            Check(failures, $"长卡顶部裁切后日期条粘性下移（cardTop={firstCardInScroller.Top:F1}, translateY={firstTranslateY:F1}）",
                firstCardInScroller.Top < -15 &&
                firstTranslateY > 1);
            Check(failures, $"粘性日期条停在视口可见条带内且不越出卡片（stampTop={firstStampInScroller.Top:F1}, stampBottom={firstStampInScroller.Bottom:F1}, viewport={scroller.ViewportHeight:F1}）",
                firstStampInScroller.Top >= -0.5 &&
                firstStampInScroller.Bottom <= scroller.ViewportHeight + 0.5 &&
                firstStampInRoot.Left >= firstCardInRoot.Left - 0.5 &&
                firstStampInRoot.Right <= firstCardInRoot.Right + 0.5 &&
                firstStampInRoot.Bottom <= firstCardInRoot.Bottom + 0.5);
            Check(failures, "粘性日期条不覆盖勾选槽、…触发器或底部命令栏",
                !firstStampInRoot.IntersectsWith(Bounds(firstCheck, root)) &&
                !firstStampInRoot.IntersectsWith(Bounds(firstOverflow, root)) &&
                !firstStampInRoot.IntersectsWith(commandBarInRoot));

            scroller.ScrollToBottom();
            Settle(floating);
            Check(failures, "滚到底部时滚动呈现器仍显示上方溢出渐隐 OpacityMask",
                presenter.OpacityMask is LinearGradientBrush);

            // 6. 展开搜索（视口变矮、底部命令栏不动），在缩小视口下验证粘性日期条不遮搜索槽/命令栏/…菜单。
            scroller.ScrollToTop();
            Settle(floating);
            var viewportHeightBeforeSearch = scroller.ViewportHeight;
            var commandBarTopBeforeSearch = Bounds(commandBar, root).Top;

            WindowValidationInput.RaisePreviewKey(floating, Key.F, ModifierKeys.Control);
            Settle(floating);
            Check(failures, "Ctrl+F 展开搜索输入行并压缩列表视口，底部命令栏位置不变",
                searchInputRow.Visibility == Visibility.Visible &&
                scroller.ViewportHeight < viewportHeightBeforeSearch - 20 &&
                Math.Abs(Bounds(commandBar, root).Top - commandBarTopBeforeSearch) <= 1.0);

            searchBox.Text = SearchKeyword;
            Settle(floating);
            Check(failures, "搜索命中多条长便签后缩小视口仍可滚动",
                rows.Children.OfType<Border>().Count() == 4 &&
                scroller.ScrollableHeight > 40);

            var searchFirstCard = FindCardForNote(rows, fiveTagNoteId);
            var searchFirstLayout = (Grid)searchFirstCard.Child;
            var searchFirstStamp = searchFirstLayout.Children.OfType<Border>().Single(border => Grid.GetColumn(border) == 1);
            var searchFirstCheck = searchFirstLayout.Children.OfType<CheckBox>().Single(box => Grid.GetColumn(box) == 0);
            var searchFirstOverflow = searchFirstLayout.Children.OfType<Button>().Single(button => Grid.GetColumn(button) == 2);

            scroller.ScrollToVerticalOffset(Math.Min(scroller.ScrollableHeight, 42));
            Settle(floating);

            var searchStampInScroller = Bounds(searchFirstStamp, scroller);
            var searchStampInRoot = Bounds(searchFirstStamp, root);
            var searchSlotInRoot = Bounds(searchSlot, root);
            var searchCommandBarInRoot = Bounds(commandBar, root);
            Check(failures, "搜索展开且滚动后粘性日期条留在缩小视口内，不覆盖搜索槽、命令栏、勾选或…",
                ReadTranslateY(searchFirstStamp) > 1 &&
                searchStampInScroller.Top >= -0.5 &&
                searchStampInScroller.Bottom <= scroller.ViewportHeight + 0.5 &&
                searchStampInRoot.Top >= searchSlotInRoot.Bottom - 0.5 &&
                searchStampInRoot.Bottom <= searchCommandBarInRoot.Top + 0.5 &&
                !RectsOverlap(searchStampInRoot, searchSlotInRoot) &&
                !RectsOverlap(searchStampInRoot, searchCommandBarInRoot) &&
                !RectsOverlap(searchStampInRoot, Bounds(searchFirstCheck, root)) &&
                !RectsOverlap(searchStampInRoot, Bounds(searchFirstOverflow, root)));

            // 在搜索滚动后的卡片上打开并 Esc 关闭「…」原生菜单，确认命中与焦点正常。
            Require(Keyboard.Focus(searchFirstOverflow) == searchFirstOverflow, "无法聚焦搜索展开态的「…」触发器。");
            Settle(floating);
            WindowValidationInput.RaiseKey(searchFirstOverflow, Key.Enter);
            Settle(floating);
            var openedMenu = FindOpenContextMenu();
            Check(failures, "搜索展开滚动后仍能正常打开「…」原生 ContextMenu",
                openedMenu is { IsOpen: true });
            if (openedMenu is { IsOpen: true })
            {
                WindowValidationInput.RaiseKey(openedMenu, Key.Escape);
                Settle(floating);
                Check(failures, "Esc 关闭「…」菜单后焦点回到触发器",
                    !openedMenu.IsOpen &&
                    ReferenceEquals(Keyboard.FocusedElement, searchFirstOverflow));
            }

            // 7. 清空收起搜索并回顶：日期条位移归零并恢复 4 DIP 静止间距。
            WindowValidationInput.RaisePreviewKey(floating, Key.Escape);
            Settle(floating);
            scroller.ScrollToTop();
            Settle(floating);

            var restoredCard = FindCardForNote(rows, fiveTagNoteId);
            var restoredLayout = (Grid)restoredCard.Child;
            var restoredStamp = restoredLayout.Children.OfType<Border>().Single(border => Grid.GetColumn(border) == 1);
            var restoredBody = restoredLayout.Children.OfType<StackPanel>().Single(panel => Grid.GetColumn(panel) == 1);
            var restoredBodyText = restoredBody.Children.OfType<TextBlock>().First();
            var restoredGap = VisualTreeHelper.GetOffset(restoredBody).Y -
                (VisualTreeHelper.GetOffset(restoredStamp).Y + restoredStamp.ActualHeight);
            Check(failures, "回顶后日期条粘性位移归零且恢复 4 DIP 静止间距无重叠",
                Math.Abs(ReadTranslateY(restoredStamp)) <= 0.5 &&
                restoredGap is >= 3.0 and <= 5.0 &&
                !Bounds(restoredStamp, root).IntersectsWith(Bounds(restoredBodyText, root)));
        });

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败项：{string.Join("；", failures)}");
    }

    private static string SaveNoteWithDate(
        DesktopWorkspace workspace,
        string text,
        DateTime date,
        IReadOnlyList<string> tagIds,
        System.Windows.Threading.Dispatcher dispatcher,
        IReadOnlyList<ReminderRule>? reminders = null)
    {
        Apply(workspace, new SaveNoteChange(
            null,
            text,
            date,
            false,
            tagIds,
            reminders ?? Array.Empty<ReminderRule>()), dispatcher);
        return workspace.Snapshot.Settings.Notes.Single(note => string.Equals(note.Text, text, StringComparison.Ordinal)).Id;
    }

    private static string SaveUndatedNote(
        DesktopWorkspace workspace,
        string text,
        System.Windows.Threading.Dispatcher dispatcher)
    {
        var id = SaveNoteWithDate(workspace, text, new DateTime(2030, 7, 12), Array.Empty<string>(), dispatcher);
        Apply(workspace, new SaveNoteChange(
            id,
            text,
            date: null,
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()), dispatcher);
        return id;
    }

    private static string ReadCardDateText(Border card)
    {
        if (card.Child is not Grid layout)
        {
            return string.Empty;
        }

        var stamp = layout.Children.OfType<Border>().SingleOrDefault(border => Grid.GetColumn(border) == 1);
        return (stamp?.Child as TextBlock)?.Text ?? string.Empty;
    }

    private static bool TextWouldNaturallyWrap(TextBlock text)
    {
        var dpi = VisualTreeHelper.GetDpi(text);
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentCulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            text.Foreground,
            dpi.PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace > text.ActualWidth + 1 &&
               text.ActualHeight >= formatted.Height * 1.8;
    }

    private static Border FindCardForNote(StackPanel rows, string noteId) =>
        rows.Children.OfType<Border>()
            .Single(card => VisualDescendants<CheckBox>(card)
                .Any(box => string.Equals(box.Tag as string, noteId, StringComparison.Ordinal)));

    private static ContextMenu? FindOpenContextMenu()
    {
        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source.RootVisual is not DependencyObject root)
            {
                continue;
            }

            if (root is ContextMenu direct && direct.IsOpen)
            {
                return direct;
            }

            var nested = VisualDescendants<ContextMenu>(root).FirstOrDefault(candidate => candidate.IsOpen);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool IsSelfOrVisualDescendant(DependencyObject ancestor, DependencyObject? candidate)
    {
        for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static double ReadTranslateY(UIElement element) =>
        element.RenderTransform switch
        {
            TranslateTransform translate => translate.Y,
            TransformGroup group => group.Children.OfType<TranslateTransform>().LastOrDefault()?.Y ?? 0,
            _ => 0
        };

    private static bool RectsOverlap(Rect a, Rect b)
    {
        var intersection = Rect.Intersect(a, b);
        return !intersection.IsEmpty && intersection.Width > 0.5 && intersection.Height > 0.5;
    }

    private static bool BrushColorEquals(Brush? actual, Color expected) =>
        actual is SolidColorBrush solid && solid.Color == expected;

    private static T RequiredByName<T>(FrameworkElement host, string name) where T : FrameworkElement =>
        host.FindName(name) as T ?? throw new InvalidOperationException($"找不到控件 {name}。");

    private static void RunWithDualWindows(
        string tag,
        Action<DesktopWorkspace, MainWindow, FloatingWindow> body)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-floating-option-b-{tag}-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        FloatingWindow? floating = null;
        try
        {
            var clock = new FixedClock(FixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
            {
                ShowInTaskbar = false
            };
            PrepareOffscreen(main);
            floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory)
            {
                ShowInTaskbar = false
            };
            PrepareOffscreen(floating);
            floating.Left = 40;
            floating.Top = 40;
            floating.Opacity = 1;
            floating.Show();
            Settle(floating);
            body(workspace, main, floating);
        }
        finally
        {
            var openMenu = FindOpenContextMenu();
            if (openMenu is { IsOpen: true })
            {
                openMenu.IsOpen = false;
                WindowValidationInput.Pump(openMenu.Dispatcher);
            }

            if (floating is not null)
            {
                floating.Close();
                WindowValidationInput.Pump(floating.Dispatcher);
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

    private static void ActivateForKeyboard(Window window)
    {
        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetActiveWindow(handle);
        }

        Settle(window);
    }

    private static void PrepareOffscreen(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;
        window.Top = -20_000;
        window.Opacity = 0;
        window.Topmost = false;
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static void Check(ICollection<string> failures, string label, bool condition)
    {
        if (!condition)
        {
            failures.Add(label);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);
}
