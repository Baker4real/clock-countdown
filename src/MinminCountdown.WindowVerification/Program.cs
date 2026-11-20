using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using MinminCountdown;

namespace MinminCountdown.WindowVerification;

internal static class Program
{
    internal const string ViewportFactName = "MainEmptyStatesFitListViewport";
    internal const string GuidanceFactName = "MainEmptyStatesGuideToSingleAddEntry";
    internal const string OverflowFactName = "MainListOverflowScrollsAndReachesActions";
    internal const string FloatingSmokeFactName = "FloatingWindowRendersAndFindsManagerButton";
    internal const string FloatingNoteSearchFactName = "FloatingNoteSearchExpandsAndFocusesWithoutReentry";
    internal const string ReminderCenterSmokeFactName = "ReminderCenterRendersAndFindsAcknowledgeAll";
    internal const string ReminderCenterLongTitleFactName = "ReminderCenterLongTitleDisclosure";
    internal const string ReminderCenterTwoRowLayoutFactName = "ReminderCenterTwoRowLayoutAndLazyCustomSnooze";
    internal const string ReminderCenterSnoozeMenuFactName = "ReminderCenterSnoozeMenuBehaviorAndDomainSemantics";
    internal const string ReminderRulesEditorSmokeFactName = "ReminderRulesEditorRendersAndFindsMode";
    internal const string ConfirmationDialogSmokeFactName = "ConfirmationDialogRendersAndFindsCancel";
    internal const string RuleEditorPrimaryAndDirtyFactName = "ReminderRulesEditorSinglePrimaryActionAndDirtyAccept";
    internal const string RuleEditorLayoutAndReachabilityFactName = "ReminderRulesEditorContentSizedListAndReachableActions";
    internal const string RuleEditorPlainLabelsFactName = "ReminderRulesEditorPlainLanguageLabelsAndAccessibility";
    internal const string RuleEditorLivePreviewFactName = "ReminderRulesEditorLivePreviewMatchesSavedSummary";
    internal const string SecondaryDwmSupportedFactName = "SecondaryWindowsUseDwmRoundedChromeAndBorderColor";
    internal const string SecondaryDwmFallbackFactName = "SecondaryWindowsFallbackSquareChromeWhenDwmUnsupported";
    internal const string ConfirmationDialogTitleFactName = "ConfirmationDialogTitleAppearsOnce";
    internal const string ConfirmationDialogResourcesFactName = "ConfirmationDialogReusesLocalWpfUiResources";

    private const string ViewportFactMeaning =
        "两个列表的首次、筛选无结果与分段无内容空态在首选、工作区缩小、720×560 与 DPI 变化窗口里都没有虚假滚动范围，" +
        "在列表实际视口居中且滚进窗口后完整可见（#248）";
    private const string GuidanceFactMeaning =
        "首次空态给说明与只聚焦表单、保留草稿的轻量填写引导，筛选无结果只给重置且重置恢复结果，" +
        "分段无内容说明记录在另一分段，空态里没有写着「添加」的第二个入口（#248）";
    private const string OverflowFactMeaning =
        "两个列表真实溢出时保留滚动条，滚轮、thumb 与 PageDown 能滚动并到底，末行与每行操作都能滚进视口（#248）";
    private const string FloatingSmokeFactMeaning =
        "悬浮窗在隔离桌面完成首帧，并能按自动化标识找到“打开管理窗口”按钮（#297）";
    private const string FloatingNoteSearchFactMeaning =
        "悬浮窗从 Ctrl+F、搜索按钮和提示条词钮展开搜索都在布局后聚焦输入框，连续触发不在入口返回前重入；" +
        "共享搜索词、Esc 清空收起和提示条三态保持不变（#315）";
    private const string ReminderCenterSmokeFactMeaning =
        "提醒中心在隔离桌面完成首帧，并能按自动化标识找到“全部知道了”按钮（#297）";
    private const string ReminderCenterLongTitleFactMeaning =
        "长标题在真实提醒中心收起为两行省略，键盘展开/收起显示全文并同步可访问名，整行保留完整正文；" +
        "相对时间原位更新不改展开状态、焦点或滚动（#321）";
    private const string ReminderCenterTwoRowLayoutFactMeaning =
        "提醒中心在默认 460 与最小 400 DIP 宽度下动作区恰为两行、互不重叠不裁切且第二行居中不拉伸；" +
        "条目不预创建自定义稍后控件，首次选「自定义」才创建并展开日期、时分与确认按钮，无效自定义时间错误紧邻其下（#320）";
    private const string ReminderCenterSnoozeMenuFactMeaning =
        "提醒中心稍后提醒菜单应用共享暖粉样式、五项齐全且角色为 Menu/MenuItem，支持方向键/Enter/Esc 与 Esc 回焦菜单按钮，" +
        "选预设一次完成稍后；送达前禁用、失败保留重试、打开来源可用、不抢焦点与有条目拒绝关闭不变（#320）";
    private const string ReminderRulesEditorSmokeFactMeaning =
        "规则编辑器在隔离桌面完成首帧，并能按自动化标识找到“提醒规则类型”下拉框（#297）";
    private const string ConfirmationDialogSmokeFactMeaning =
        "确认框在隔离桌面完成首帧，并能按自动化标识找到“取消”按钮（#297）";
    private const string RuleEditorPrimaryAndDirtyFactMeaning =
        "规则编辑器仅窗口级「完成」使用实心主行动，表单提交为次级按钮；有未提交输入时点「完成」自动提交有效输入、拦住无效输入并保留草稿，不悄悄丢弃（#322）";
    private const string RuleEditorLayoutAndReachabilityFactMeaning =
        "规则编辑器在少规则与多规则下列表按内容取高、超上限后列表内滚动、列表与表单间距不超过区块间距、初始高度随内容且不超上限，688/826/1032 DIP 工作区高度下底部按钮与缩放手柄均可达，且取消、8 条上限与字段校验位置不变（#322）";
    private const string RuleEditorPlainLabelsFactMeaning =
        "规则编辑器表单不再出现“日历频率 / 重复间隔 / 日历选择”等实现用语，改用最终日常词表；字段可访问名与可见标签一致且 LabeledBy 关系保持完整；四个规则类型名、类型化领域记录、规则 ID 与持久化 JSON 不变（#323）";
    private const string RuleEditorLivePreviewFactMeaning =
        "规则编辑器表单中的一句话预览随所有输入实时更新，在四类规则与各日历周期下均与保存后规则行摘要完全一致，并在输入未补全或越界时给出清晰提示、适配系统高对比度（#323）";
    private const string SecondaryDwmSupportedFactMeaning =
        "提醒中心与规则编辑器改为不透明无边框窗口，支持系统窗铬时请求系统圆角、投影与 1 DIP 暖色边框并随系统对比度切换同步更新；提醒中心不抢焦点/不置顶/头部拖动/右上首次放置保留手动位置，规则编辑器居中/夹取/高度与缩放手柄可达均保持不变（#324）";
    private const string SecondaryDwmFallbackFactMeaning =
        "系统不支持 DWM 圆角、投影或边框色时（较旧 Windows、DWM 返回失败或抛出异常），提醒中心与规则编辑器均照常显示并可操作，保持非分层不透明与直角回退，由内容边界承担 1 DIP 暖边，并写出与管理窗口同类的回退诊断事件（#324）";
    private const string ConfirmationDialogTitleFactMeaning =
        "确认框标题在可见树只出现一次，窗口名称不变，取消仍是唯一安全默认、Enter/Esc 不执行危险动作且自动化标识不变（#314）";
    private const string ConfirmationDialogResourcesFactMeaning =
        "连续打开多个确认框复用同一份局部 WPF-UI 资源字典，WPF-UI 资源不进入应用级资源，运行中切换系统对比度配色后已开与新开确认框配色正确（#314）";

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var facts = new Dictionary<string, bool>
        {
            [ViewportFactName] = false,
            [GuidanceFactName] = false,
            [OverflowFactName] = false,
            [FloatingSmokeFactName] = false,
            [FloatingNoteSearchFactName] = false,
            [ReminderCenterSmokeFactName] = false,
            [ReminderCenterLongTitleFactName] = false,
            [ReminderCenterTwoRowLayoutFactName] = false,
            [ReminderCenterSnoozeMenuFactName] = false,
            [ReminderRulesEditorSmokeFactName] = false,
            [ConfirmationDialogSmokeFactName] = false,
            [RuleEditorPrimaryAndDirtyFactName] = false,
            [RuleEditorLayoutAndReachabilityFactName] = false,
            [RuleEditorPlainLabelsFactName] = false,
            [RuleEditorLivePreviewFactName] = false,
            [SecondaryDwmSupportedFactName] = false,
            [SecondaryDwmFallbackFactName] = false,
            [ConfirmationDialogTitleFactName] = false,
            [ConfirmationDialogResourcesFactName] = false
        };
        var failures = new List<string>();

        void RunFact(string name, string meaning, Func<(bool Passed, string Explanation)> run)
        {
            try
            {
                var result = run();
                facts[name] = result.Passed;
                if (!result.Passed)
                {
                    failures.Add(result.Explanation);
                }
            }
            catch (Exception exception)
            {
                facts[name] = false;
                failures.Add($"{meaning}；{exception}");
            }
        }

        try
        {
            var app = new App();
            app.InitializeComponent();
            var productionStartup = typeof(App).GetMethod(
                "OnStartup",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?.CreateDelegate<StartupEventHandler>(app)
                ?? throw new MissingMethodException(typeof(App).FullName, "OnStartup");
            // App.xaml 的事件接线只服务生产宿主；独立 runner 加载资源后解除它，避免误进生产启动。
            app.Startup -= productionStartup;
            using var theme = new ApplicationTheme(app.Resources);
            theme.Apply();

            RunFact(ViewportFactName, ViewportFactMeaning, () =>
            {
                var probe = EmptyStateScenario.RunViewport();
                return (probe.Passed, probe.Explain(ViewportFactMeaning));
            });
            RunFact(GuidanceFactName, GuidanceFactMeaning, () =>
            {
                var probe = EmptyStateScenario.RunGuidance();
                return (probe.AllPassed, probe.Explain(GuidanceFactMeaning));
            });
            RunFact(OverflowFactName, OverflowFactMeaning, () =>
            {
                var probe = EmptyStateOverflowScenario.Run();
                return (probe.AllPassed, probe.Explain(OverflowFactMeaning));
            });
            RunFact(FloatingSmokeFactName, FloatingSmokeFactMeaning, () =>
            {
                WindowSmokeScenario.RunFloating();
                return (true, FloatingSmokeFactMeaning);
            });
            RunFact(FloatingNoteSearchFactName, FloatingNoteSearchFactMeaning, () =>
            {
                FloatingNoteSearchScenario.Run();
                return (true, FloatingNoteSearchFactMeaning);
            });
            RunFact(ReminderCenterSmokeFactName, ReminderCenterSmokeFactMeaning, () =>
            {
                WindowSmokeScenario.RunReminderCenter();
                return (true, ReminderCenterSmokeFactMeaning);
            });
            RunFact(ReminderCenterLongTitleFactName, ReminderCenterLongTitleFactMeaning, () =>
            {
                ReminderCenterLongTitleScenario.Run();
                return (true, ReminderCenterLongTitleFactMeaning);
            });
            RunFact(ReminderCenterTwoRowLayoutFactName, ReminderCenterTwoRowLayoutFactMeaning, () =>
            {
                ReminderCenterSnoozeMenuScenario.RunTwoRowLayoutAndLazyCustomSnooze();
                return (true, ReminderCenterTwoRowLayoutFactMeaning);
            });
            RunFact(ReminderCenterSnoozeMenuFactName, ReminderCenterSnoozeMenuFactMeaning, () =>
            {
                ReminderCenterSnoozeMenuScenario.RunSnoozeMenuBehaviorAndDomainSemantics();
                return (true, ReminderCenterSnoozeMenuFactMeaning);
            });
            RunFact(ReminderRulesEditorSmokeFactName, ReminderRulesEditorSmokeFactMeaning, () =>
            {
                WindowSmokeScenario.RunReminderRulesEditor();
                return (true, ReminderRulesEditorSmokeFactMeaning);
            });
            RunFact(ConfirmationDialogSmokeFactName, ConfirmationDialogSmokeFactMeaning, () =>
            {
                WindowSmokeScenario.RunConfirmationDialog();
                return (true, ConfirmationDialogSmokeFactMeaning);
            });
            RunFact(
                RuleEditorPrimaryAndDirtyFactName,
                RuleEditorPrimaryAndDirtyFactMeaning,
                () => ReminderRulesEditorScenario.RunSinglePrimaryActionAndDirtyAccept(
                    RuleEditorPrimaryAndDirtyFactMeaning));
            RunFact(
                RuleEditorLayoutAndReachabilityFactName,
                RuleEditorLayoutAndReachabilityFactMeaning,
                () => ReminderRulesEditorScenario.RunContentSizedListAndReachableActions(
                    RuleEditorLayoutAndReachabilityFactMeaning));
            RunFact(
                RuleEditorPlainLabelsFactName,
                RuleEditorPlainLabelsFactMeaning,
                () => ReminderRulesEditorCopyAndPreviewScenario.RunPlainLanguageLabelsAndAccessibility(
                    RuleEditorPlainLabelsFactMeaning));
            RunFact(
                RuleEditorLivePreviewFactName,
                RuleEditorLivePreviewFactMeaning,
                () => ReminderRulesEditorCopyAndPreviewScenario.RunLivePreviewMatchesSavedSummary(
                    RuleEditorLivePreviewFactMeaning));
            RunFact(
                SecondaryDwmSupportedFactName,
                SecondaryDwmSupportedFactMeaning,
                () => SecondaryWindowDwmChromeScenario.RunSupportedDwmChrome(
                    SecondaryDwmSupportedFactMeaning));
            RunFact(
                SecondaryDwmFallbackFactName,
                SecondaryDwmFallbackFactMeaning,
                () => SecondaryWindowDwmChromeScenario.RunUnsupportedDwmChromeFallback(
                    SecondaryDwmFallbackFactMeaning));
            RunFact(ConfirmationDialogTitleFactName, ConfirmationDialogTitleFactMeaning, () =>
            {
                WindowSmokeScenario.RunConfirmationDialogTitle();
                return (true, ConfirmationDialogTitleFactMeaning);
            });
            RunFact(ConfirmationDialogResourcesFactName, ConfirmationDialogResourcesFactMeaning, () =>
            {
                WindowSmokeScenario.RunConfirmationDialogResources();
                return (true, ConfirmationDialogResourcesFactMeaning);
            });
        }
        catch (Exception exception)
        {
            failures.Add($"独立窗口验证宿主初始化失败：{exception}");
        }

        Console.Out.Write(JsonSerializer.Serialize(facts));
        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        return facts.Values.All(passed => passed) ? 0 : 1;
    }
}
