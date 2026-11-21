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
    internal const string FloatingQuickAddErrorLayoutFactName = "FloatingQuickAddActionPinnedInErrorState";
    internal const string SecondaryDwmSupportedFactName = "SecondaryWindowsUseDwmRoundedChromeAndBorderColor";
    internal const string SecondaryDwmFallbackFactName = "SecondaryWindowsFallbackSquareChromeWhenDwmUnsupported";
    internal const string ConfirmationDialogTitleFactName = "ConfirmationDialogTitleAppearsOnce";
    internal const string ConfirmationDialogResourcesFactName = "ConfirmationDialogReusesLocalWpfUiResources";
    internal const string MainTagRenameSyncFactName = "MainTagRenameSyncsAcrossAllSurfaces";
    internal const string InputFocusAndErrorFactName = "MainCountdownInputFocusAndErrorStates";
    internal const string SharedScrollBarGeometryFactName = "SharedScrollBarRestingThinAndExpandBehavior";
    internal const string SharedScrollBarMotionFactName = "SharedScrollBarMotionAndInteractionsWork";
    internal const string FloatingBottomStatusAutomationFactName = "FloatingBottomStatusPublicAutomationTree";
    internal const string DisabledStateUnifiedFactName = "DisabledStateUnifiedAndPreserved";
    internal const string MainAlignmentAndNoteRowsFactName = "MainWindowCommandBarHeaderAndNoteRowsAligned";
    internal const string MainEmptyAndHighContrastRibbonFactName = "MainWindowEmptyStateAffordanceAndHighContrastRibbonCollapse";
    internal const string FloatingWorkdayCompletionFactName = "FloatingWorkdayCompletionBottomNoDuplicateAndCardMask";
    internal const string RuleEditorFixedIntervalNumericFactName = "ReminderRulesEditorFixedIntervalNumericInput";
    internal const string RuleEditorFixedIntervalVisualFactName = "ReminderRulesEditorFixedIntervalVisualAndIsolationContract";
    internal const string OverlaySurfaceBordersFactName = "OverlaySurfaceBordersMeetContrastWithoutShadow";
    internal const string FilterMatchModeAndFocusFactName = "FilterMatchModeDisclosureAndOverlayFocusLifecycle";
    internal const string DatePickerSingleBoxGeometryFactName = "DatePickerSingleBoxGeometryAndFocus";
    internal const string DatePickerSingleBoxInteractionFactName = "DatePickerSingleBoxInteractionAndLongestDateFit";
    internal const string MainNoteSearchCoalesceFactName = "MainNoteSearchCoalescesAndPreservesTagControls";
    internal const string MainHiddenDeferRefreshFactName = "MainWindowHiddenDefersRebuildsAndRefreshesFirstFrame";
    internal const string SpacingTokensWiredFactName = "SpacingTokensWiredAcrossWindows";
    internal const string TagManagerAutoSaveFactName = "TagManagerAutoSaveOnEnterAndBlur";
    internal const string TagManagerErrorsEscDeleteFactName = "TagManagerRenameErrorsEscDiscardAndDelete";
    internal const string FloatingNoteOverflowNativeAndLazyFactName = "FloatingNoteOverflowMenuNativeAndLazy";
    internal const string FloatingNoteOverflowInteractionsAndMotionFactName = "FloatingNoteOverflowMenuInteractionsAndMotion";
    internal const string CountdownDeadlineTimeTypingFactName = "MainCountdownDeadlineTimeTypingAndStepping";
    internal const string CountdownDeadlineTimeValidationFactName = "MainCountdownDeadlineTimeValidationAndAccessibility";
    internal const string RuleEditorRepeatAndLeadNumericFactName = "RuleEditorRepeatAndLeadNumericInputs";
    internal const string MonthlySalaryDecimalAndDownstreamFactName = "MonthlySalaryDecimalInputAndDownstreamContract";
    internal const string RuleEditorTimeInputFactName = "RuleEditorTimeInputTypingValidationAndPreview";
    internal const string ReminderCenterCustomSnoozeTimeInputFactName = "ReminderCenterCustomSnoozeTypeableTimeInput";
    internal const string MainWorkHoursTimeInputFactName = "MainWorkHoursTypeableTimeInputAndValidation";
    internal const string SharedIconGeometriesFactName = "SharedIconGeometriesFrozenAndDeduplicated";
    internal const string VectorizedTextGlyphIconsFactName = "VectorizedTextGlyphIconsPreserveAccessibilityAndGeometry";

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
    private const string FloatingQuickAddErrorLayoutFactMeaning =
        "悬浮窗快速新增便签的字段区在命令区限高内滚动，“添加”动作行固定在命令区底部不随字段滚动；正文留空或日期留空出错时“添加”完整位于视口内且可点击、错误文案可见，展开回顶、收起态几何与命中高度及 Tab 顺序均保持不变（#318）";
    private const string SecondaryDwmSupportedFactMeaning =
        "提醒中心与规则编辑器改为不透明无边框窗口，支持系统窗铬时请求系统圆角、投影与 1 DIP 暖色边框并随系统对比度切换同步更新；提醒中心不抢焦点/不置顶/头部拖动/右上首次放置保留手动位置，规则编辑器居中/夹取/高度与缩放手柄可达均保持不变（#324）";
    private const string SecondaryDwmFallbackFactMeaning =
        "系统不支持 DWM 圆角、投影或边框色时（较旧 Windows、DWM 返回失败或抛出异常），提醒中心与规则编辑器均照常显示并可操作，保持非分层不透明与直角回退，由内容边界承担 1 DIP 暖边，并写出与管理窗口同类的回退诊断事件（#324）";
    private const string ConfirmationDialogTitleFactMeaning =
        "确认框标题在可见树只出现一次，窗口名称不变，取消仍是唯一安全默认、Enter/Esc 不执行危险动作且自动化标识不变（#314）";
    private const string ConfirmationDialogResourcesFactMeaning =
        "连续打开多个确认框复用同一份局部 WPF-UI 资源字典，WPF-UI 资源不进入应用级资源，运行中切换系统对比度配色后已开与新开确认框配色正确（#314）";
    private const string MainTagRenameSyncFactMeaning =
        "标签管理改名后，管理窗口倒计时行、便签卡、筛选标签与标签选择器立即显示新名称，倒计时行同步更新标签名称与颜色且行的其他内容、键盘焦点和滚动位置不变（#298）";
    private const string InputFocusAndErrorFactMeaning =
        "文本框、下拉框与日期框使用非红色焦点和更深暖色悬停边；倒计时编辑器的有效焦点、错误无焦点与错误有焦点" +
        "分别为 2/1/2 DIP 且不挤布局，首错聚焦、修正即清、帮助文本与礼貌播报保持，系统对比度映射不同系统角色（#303）";
    private const string SharedScrollBarGeometryFactMeaning =
        "共享滚动条在管理窗口列表与窗口级滚动、提醒中心、规则编辑器与弹层内静止可见宽 4 DIP、悬停命中轨或拖动展开到 10 DIP、命中轨保持 10 DIP 且颜色对邻近表面对比度不低于 3:1，悬浮窗 4 DIP 滚动条不变（#306）";
    private const string SharedScrollBarMotionFactMeaning =
        "共享滚动条展开与收起仅以变换消费 MotionHoverDuration 缓出过渡并在系统关闭动画时直接到位，滚轮、拖动滑块、键盘翻页与点击轨道翻页照常可用（#306）";
    private const string FloatingBottomStatusAutomationFactMeaning =
        "悬浮窗底部状态以互斥的鼓励语无 peer 元素与可朗读/可调用状态元素进出自动化树；鼓励语不在树中，金额与日历状态名称同步，" +
        "遮罩态子树不泄密且指针与键盘均可切换，三模式往返同步，直播区播报正常工作（#316）";
    private const string DisabledStateUnifiedFactMeaning =
        "全部受影响控件的禁用透明度与系统对比度分支收为单一可复用定义；日间与系统对比度（含换色与恢复）下禁用外观、分段样式继承保留与退出确认死代码删除均通过（#310）";
    private const string MainAlignmentAndNoteRowsFactMeaning =
        "管理窗口命令栏三枚命令按钮等高居中并与数量文字共用垂直中线、右缘对齐内容区右缘且页签与选中指示条几何不变；" +
        "切页前后主标题水平位置不变并保留两页标题装饰；便签新增与编辑态正文列吸收剩余宽度、行尾留白等于行首留白且取消内边距与次级按钮一致（#304）";
    private const string MainEmptyAndHighContrastRibbonFactMeaning =
        "管理窗口首次空态填写引导常态可辨认为可点（暖粉底色、≥3:1 边框、≥4.5:1 文字与方向提示、悬停/焦点/系统对比度），点击只聚焦表单输入不新增不清草稿；" +
        "系统对比度开启时装饰带布局空间收起不留空白，关闭后恢复原样（#304）";
    private const string FloatingWorkdayCompletionFactMeaning =
        "下班收尾期间悬浮窗底部改为鼓励语并退出自动化树、不含金额文字；收尾卡内金额支持指针、键盘回车/空格与自动化 Invoke 切换遮罩且遮罩态子树与 ToolTip 不泄密、焦点环规范且状态不持久化；" +
        "月薪为零显示鼓励文案不伪造金额或遮罩按钮，下班起点、北京时间次日零点终点、筛选与日历未覆盖当年守卫均保持不变（#317）";
    private const string RuleEditorFixedIntervalNumericFactMeaning =
        "规则编辑器固定间隔支持上下键与滚轮在 1 到 1440 整数范围内增减、超出范围或非法输入当场提示且错误文案不变；ViewModel 属性为可空数值且对外保存、脏态与预览行为不变（#330）";
    private const string RuleEditorFixedIntervalVisualFactMeaning =
        "规则编辑器固定间隔输入满足单层焦点描边、暖金焦点与危险错误边框（不挤布局）、对比度基线、系统对比度主题、可见标签 LabeledBy 关系，且 WPF-UI 资源不进入应用级资源、其他窗口外观不受影响（#330）";
    private const string OverlaySurfaceBordersFactMeaning =
        "筛选、标签管理、显示设置与标签选择四个弹层（含主窗两处与浮窗一处标签选择器）表面边界均消费 ControlBorderBrush，" +
        "对自身底色与各邻近页面/卡片表面对比度均不低于 3:1，保持原底色与 RadiusOverlay 圆角且无投影，并在系统对比度主题开启、换色与恢复时正确映射（#305）";
    private const string FilterMatchModeAndFocusFactMeaning =
        "筛选弹层仅在已选不少于两个具体标签时显示并可聚焦匹配方式（默认任一匹配），0/1 个标签或「无标签」时完全折叠且不可聚焦；" +
        "「无标签」位于同标签芯片组并与具体标签互斥，筛选结果语义不变；四类弹层均保持打开聚焦首项、层内 Tab 循环与 Esc 关闭返焦生命周期（#305）";
    private const string DatePickerSingleBoxGeometryFactMeaning =
        "六处日期框均采用单一外框且与下拉框高度、圆角与箭头列宽一致，箭头位于边界之内、无内层双边与缝隙；" +
        "整框键盘焦点只画单层边界（有效暖金/错误危险色且不挤布局、系统对比度可区分、禁用态同步），箭头按钮仍为 Tab 停靠点（#312）";
    private const string DatePickerSingleBoxInteractionFactMeaning =
        "六处日期框均支持点击文字区与箭头打开主题化日历并渲染 7+6×7 月视图，保持只读中文日期格式、Esc 取消并还原旧日期、日历方向键导航与焦点归还，" +
        "且最长日期「2031 年 12 月 31 日」在全部六处完整无裁切（含规则编辑器最小宽与提醒中心最小宽）（#312）";
    private const string MainNoteSearchCoalesceFactMeaning =
        "管理窗口便签搜索连续输入合并约 150 毫秒后仅渲染一次，按 Enter 立即单次渲染无重复尾随，清除与悬浮窗共享筛选同步双向一致；" +
        "搜索不重建标签选择器、筛选标签和标签管理行，仅标签集合变化时更新（#302）";
    private const string MainHiddenDeferRefreshFactMeaning =
        "管理窗口隐藏期间多次工作区改动不重建行控件并保留未保存草稿与筛选条件，经提醒“打开来源”、悬浮窗入口和托盘菜单重新显示时在首帧前仅刷新一次且首帧内容与最新设置一致（#302）";
    private const string SpacingTokensWiredFactMeaning =
        "SpacingUnit/SpacingFieldRow/SpacingBlockGap/SpacingCardPadding 四个间距 token 在主窗口、悬浮窗、提醒中心、规则编辑器与标签选择器" +
        "的同值同义位置真正消费资源值，碰巧同值但语义不同的字面量保持不变，默认值几何与现役一致（#311）";
    private const string TagManagerAutoSaveFactMeaning =
        "标签管理行移除“保存”按钮并保留“删除”与新建流程；名称输入框按 Enter 与失焦各提交一次改名并同步四处界面，内容未变化（含仅首尾空白）不产生提交也不重建行（#308）";
    private const string TagManagerErrorsEscDeleteFactMeaning =
        "标签管理重名或空名失败时错误贴在该行、保留用户输入并把焦点放回该输入框，修正即清；Esc 放弃未提交改名并关闭弹层返焦，删除标签保持取消默认确认与只解除关联、不删记录语义（#308）";
    private const string FloatingNoteOverflowNativeAndLazyFactMeaning =
        "悬浮窗便签「…」不再为每张卡预创建弹层与按钮，打开时懒创建并跨卡复用共享暖粉原生 ContextMenu / MenuItem（#309），" +
        "自动化角色为 Menu / MenuItem 且名称包含动作与便签对象，打开期间跟随系统对比度切换并产出实拍（#319）";
    private const string FloatingNoteOverflowInteractionsAndMotionFactMeaning =
        "悬浮窗便签「…」支持方向键移动、Enter/空格激活编辑与删除（删除保持以取消为安全默认的确认框与 10 秒撤销，卡片 Delete 行为不变）、" +
        "Esc 立即关闭并把焦点还给「…」触发器、点外部关闭；指针打开消费既有弹层进入与退出动效，键盘打开与系统关闭动画均即时到位（#319）";
    private const string CountdownDeadlineTimeTypingFactMeaning =
        "管理窗口倒计时截止时刻改为单一可键入 HH:mm:ss 文本框：新增默认当前北京时间、直接键入与编辑保存保持精确领域值，" +
        "光标按时/分/秒分段支持上下键步进与边界回绕，复用 TimeInputBehavior 兼顾 HH:mm 格式（#328）";
    private const string CountdownDeadlineTimeValidationFactMeaning =
        "倒计时截止时刻在失焦与提交时校验空值、越界与非时刻文本，错误行贴字段下方且礼貌播报，提交失败聚焦时间框（含收起态重展开），" +
        "有效/出错无焦/出错有焦为 2/1/2 DIP 不挤布局且修正即清，保留可见标签 LabeledBy 与等价可访问名并适配系统对比度（#328）";
    private const string RuleEditorRepeatAndLeadNumericFactMeaning =
        "规则编辑器每隔几个周期（1..999）与提前分钟（0..5256000）均支持上下键/滚轮步进、范围校验、越界当场提示与不变错误文案，ViewModel 属性均为可空整数，且焦点、错误边、对比度、系统对比度与标签关系均符合契约（#331）";
    private const string MonthlySalaryDecimalAndDownstreamFactMeaning =
        "主窗口显示设置月薪支持两位小数与千位分隔显示、上下键与获焦滚轮步进、范围/格式校验及不变错误文案与位置、单层暖金焦点与 1/2 DIP 危险错误边（不挤布局）、对比度与系统对比度、LabeledBy 标签关系、零 WPF-UI 资源污染，且月薪为零时悬浮窗仍显示鼓励语等下游行为不变（#331）";
    private const string RuleEditorTimeInputFactMeaning =
        "规则编辑器首次提醒与日历本地时刻改为单一可键入 HH:mm 文本框：支持直接键入与上下键分段步进、失焦与提交校验、暖金/危险边框不挤布局、" +
        "实时规则预览同步与精确持久化领域值，保留 LabeledBy 标签关系并适配系统对比度（#329）";
    private const string ReminderCenterCustomSnoozeTimeInputFactMeaning =
        "提醒中心懒创建自定义稍后时间改为单一可键入 HH:mm 文本框：保留懒创建与默认值、支持直接键入与上下键步进、非法格式与不晚于当前北京时间错误紧贴下方且带错误边框、" +
        "合法稍后精确持久化，保留 LabeledBy 与系统对比度（#329）";
    private const string MainWorkHoursTimeInputFactMeaning =
        "管理窗口显示设置上下班时间接入可键入 HH:mm 与上下键分段步进：支持失焦与提交校验非法格式与下班不晚于上班、错误贴于对应字段下方并带错误边框、" +
        "保留 LabeledBy 标签关系，保存后精确持久化并驱动工作日计算（#329）";
    private const string SharedIconGeometriesFactMeaning =
        "全部图标几何集中为 App.xaml 冻结共享 Geometry 资源，同类图标跨窗口与代码复用同一冻结实例，管理窗实心齿轮与浮窗描边齿轮保留各自独立几何（#307）";
    private const string VectorizedTextGlyphIconsFactMeaning =
        "日历翻月、悬浮搜索清除、主窗添加按钮加号与悬浮便签「…」四处文本字形改为共享冻结矢量 Path，描边规格一致，可访问名、命中区、Tab 顺序、提示与 #319 原生菜单不变（#307）";

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
            [FloatingQuickAddErrorLayoutFactName] = false,
            [SecondaryDwmSupportedFactName] = false,
            [SecondaryDwmFallbackFactName] = false,
            [ConfirmationDialogTitleFactName] = false,
            [ConfirmationDialogResourcesFactName] = false,
            [MainTagRenameSyncFactName] = false,
            [InputFocusAndErrorFactName] = false,
            [SharedScrollBarGeometryFactName] = false,
            [SharedScrollBarMotionFactName] = false,
            [FloatingBottomStatusAutomationFactName] = false,
            [DisabledStateUnifiedFactName] = false,
            [MainAlignmentAndNoteRowsFactName] = false,
            [MainEmptyAndHighContrastRibbonFactName] = false,
            [FloatingWorkdayCompletionFactName] = false,
            [RuleEditorFixedIntervalNumericFactName] = false,
            [RuleEditorFixedIntervalVisualFactName] = false,
            [OverlaySurfaceBordersFactName] = false,
            [FilterMatchModeAndFocusFactName] = false,
            [DatePickerSingleBoxGeometryFactName] = false,
            [DatePickerSingleBoxInteractionFactName] = false,
            [MainNoteSearchCoalesceFactName] = false,
            [MainHiddenDeferRefreshFactName] = false,
            [SpacingTokensWiredFactName] = false,
            [TagManagerAutoSaveFactName] = false,
            [TagManagerErrorsEscDeleteFactName] = false,
            [FloatingNoteOverflowNativeAndLazyFactName] = false,
            [FloatingNoteOverflowInteractionsAndMotionFactName] = false,
            [CountdownDeadlineTimeTypingFactName] = false,
            [CountdownDeadlineTimeValidationFactName] = false,
            [RuleEditorRepeatAndLeadNumericFactName] = false,
            [MonthlySalaryDecimalAndDownstreamFactName] = false,
            [RuleEditorTimeInputFactName] = false,
            [ReminderCenterCustomSnoozeTimeInputFactName] = false,
            [MainWorkHoursTimeInputFactName] = false,
            [SharedIconGeometriesFactName] = false,
            [VectorizedTextGlyphIconsFactName] = false
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
                FloatingQuickAddErrorLayoutFactName,
                FloatingQuickAddErrorLayoutFactMeaning,
                () => FloatingQuickAddErrorLayoutScenario.Run(
                    FloatingQuickAddErrorLayoutFactMeaning));
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
            RunFact(
                MainTagRenameSyncFactName,
                MainTagRenameSyncFactMeaning,
                () => TagRenameSyncScenario.Run(MainTagRenameSyncFactMeaning));
            RunFact(InputFocusAndErrorFactName, InputFocusAndErrorFactMeaning, () =>
            {
                InputFocusAndErrorScenario.Run();
                return (true, InputFocusAndErrorFactMeaning);
            });
            RunFact(
                SharedScrollBarGeometryFactName,
                SharedScrollBarGeometryFactMeaning,
                () => SharedScrollBarScenario.RunRestingThinAndExpandBehavior(
                    theme,
                    SharedScrollBarGeometryFactMeaning));
            RunFact(
                SharedScrollBarMotionFactName,
                SharedScrollBarMotionFactMeaning,
                () => SharedScrollBarScenario.RunMotionAndInteractions(
                    theme,
                    SharedScrollBarMotionFactMeaning));
            RunFact(FloatingBottomStatusAutomationFactName, FloatingBottomStatusAutomationFactMeaning, () =>
            {
                FloatingBottomStatusAutomationScenario.Run();
                return (true, FloatingBottomStatusAutomationFactMeaning);
            });
            RunFact(
                DisabledStateUnifiedFactName,
                DisabledStateUnifiedFactMeaning,
                () => DisabledStateScenario.Run(
                    theme,
                    DisabledStateUnifiedFactMeaning));
            RunFact(
                MainAlignmentAndNoteRowsFactName,
                MainAlignmentAndNoteRowsFactMeaning,
                () => MainWindowAlignmentAndPolishScenario.RunCommandBarHeaderAndNoteRows(
                    MainAlignmentAndNoteRowsFactMeaning));
            RunFact(
                MainEmptyAndHighContrastRibbonFactName,
                MainEmptyAndHighContrastRibbonFactMeaning,
                () => MainWindowAlignmentAndPolishScenario.RunEmptyStateAndHighContrastRibbon(
                    theme,
                    MainEmptyAndHighContrastRibbonFactMeaning));
            RunFact(FloatingWorkdayCompletionFactName, FloatingWorkdayCompletionFactMeaning, () =>
            {
                FloatingWorkdayCompletionScenario.Run();
                return (true, FloatingWorkdayCompletionFactMeaning);
            });
            RunFact(
                RuleEditorFixedIntervalNumericFactName,
                RuleEditorFixedIntervalNumericFactMeaning,
                () => ReminderRulesFixedIntervalScenario.RunFixedIntervalNumericInput(
                    RuleEditorFixedIntervalNumericFactMeaning));
            RunFact(
                RuleEditorFixedIntervalVisualFactName,
                RuleEditorFixedIntervalVisualFactMeaning,
                () => ReminderRulesFixedIntervalScenario.RunFixedIntervalVisualAndIsolation(
                    RuleEditorFixedIntervalVisualFactMeaning));
            RunFact(
                OverlaySurfaceBordersFactName,
                OverlaySurfaceBordersFactMeaning,
                () => OverlayBorderAndFilterMatchModeScenario.RunOverlayBordersAndContrast(
                    theme,
                    OverlaySurfaceBordersFactMeaning));
            RunFact(
                FilterMatchModeAndFocusFactName,
                FilterMatchModeAndFocusFactMeaning,
                () => OverlayBorderAndFilterMatchModeScenario.RunFilterMatchModeAndFocusLifecycle(
                    theme,
                    FilterMatchModeAndFocusFactMeaning));
            RunFact(
                DatePickerSingleBoxGeometryFactName,
                DatePickerSingleBoxGeometryFactMeaning,
                () => DatePickerSingleBoxScenario.RunGeometryAndFocus(
                    theme,
                    DatePickerSingleBoxGeometryFactMeaning));
            RunFact(
                DatePickerSingleBoxInteractionFactName,
                DatePickerSingleBoxInteractionFactMeaning,
                () => DatePickerSingleBoxScenario.RunInteractionAndLongestDateFit(
                    theme,
                    DatePickerSingleBoxInteractionFactMeaning));
            RunFact(
                MainNoteSearchCoalesceFactName,
                MainNoteSearchCoalesceFactMeaning,
                () => MainWindowRefreshScenario.RunNoteSearchAndTagControls(
                    MainNoteSearchCoalesceFactMeaning));
            RunFact(
                MainHiddenDeferRefreshFactName,
                MainHiddenDeferRefreshFactMeaning,
                () => MainWindowRefreshScenario.RunHiddenDeferAndFirstFrameRefresh(
                    MainHiddenDeferRefreshFactMeaning));
            RunFact(
                SpacingTokensWiredFactName,
                SpacingTokensWiredFactMeaning,
                () => SpacingTokenWiringScenario.Run(SpacingTokensWiredFactMeaning));
            RunFact(
                TagManagerAutoSaveFactName,
                TagManagerAutoSaveFactMeaning,
                () => TagManagerAutoSaveScenario.RunAutoSaveOnEnterAndBlur(
                    TagManagerAutoSaveFactMeaning));
            RunFact(
                TagManagerErrorsEscDeleteFactName,
                TagManagerErrorsEscDeleteFactMeaning,
                () => TagManagerAutoSaveScenario.RunErrorsEscDiscardAndDelete(
                    TagManagerErrorsEscDeleteFactMeaning));
            RunFact(
                FloatingNoteOverflowNativeAndLazyFactName,
                FloatingNoteOverflowNativeAndLazyFactMeaning,
                () => FloatingNoteOverflowMenuScenario.RunNativeAndLazy(
                    theme,
                    FloatingNoteOverflowNativeAndLazyFactMeaning));
            RunFact(
                FloatingNoteOverflowInteractionsAndMotionFactName,
                FloatingNoteOverflowInteractionsAndMotionFactMeaning,
                () => FloatingNoteOverflowMenuScenario.RunInteractionsAndMotion(
                    theme,
                    FloatingNoteOverflowInteractionsAndMotionFactMeaning));
            RunFact(
                CountdownDeadlineTimeTypingFactName,
                CountdownDeadlineTimeTypingFactMeaning,
                () => CountdownDeadlineTimeInputScenario.RunTypingAndStepping(
                    CountdownDeadlineTimeTypingFactMeaning));
            RunFact(
                CountdownDeadlineTimeValidationFactName,
                CountdownDeadlineTimeValidationFactMeaning,
                () => CountdownDeadlineTimeInputScenario.RunValidationAndAccessibility(
                    CountdownDeadlineTimeValidationFactMeaning));
            RunFact(
                RuleEditorRepeatAndLeadNumericFactName,
                RuleEditorRepeatAndLeadNumericFactMeaning,
                () => ExtendedNumericInputsScenario.RunRuleEditorRepeatAndLeadInputs(
                    RuleEditorRepeatAndLeadNumericFactMeaning));
            RunFact(
                MonthlySalaryDecimalAndDownstreamFactName,
                MonthlySalaryDecimalAndDownstreamFactMeaning,
                () => ExtendedNumericInputsScenario.RunMonthlySalaryDecimalInputAndDownstream(
                    MonthlySalaryDecimalAndDownstreamFactMeaning));
            RunFact(
                RuleEditorTimeInputFactName,
                RuleEditorTimeInputFactMeaning,
                () => TimeInputSpreadScenario.RunRuleEditorTimeInput(
                    RuleEditorTimeInputFactMeaning));
            RunFact(
                ReminderCenterCustomSnoozeTimeInputFactName,
                ReminderCenterCustomSnoozeTimeInputFactMeaning,
                () => TimeInputSpreadScenario.RunReminderCenterCustomSnoozeTimeInput(
                    ReminderCenterCustomSnoozeTimeInputFactMeaning));
            RunFact(
                MainWorkHoursTimeInputFactName,
                MainWorkHoursTimeInputFactMeaning,
                () => TimeInputSpreadScenario.RunWorkHoursTimeInput(
                    MainWorkHoursTimeInputFactMeaning));
            RunFact(
                SharedIconGeometriesFactName,
                SharedIconGeometriesFactMeaning,
                () => IconGeometryAndVectorIconsScenario.RunSharedGeometries(
                    SharedIconGeometriesFactMeaning));
            RunFact(
                VectorizedTextGlyphIconsFactName,
                VectorizedTextGlyphIconsFactMeaning,
                () => IconGeometryAndVectorIconsScenario.RunVectorizedTextGlyphIcons(
                    VectorizedTextGlyphIconsFactMeaning));
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
