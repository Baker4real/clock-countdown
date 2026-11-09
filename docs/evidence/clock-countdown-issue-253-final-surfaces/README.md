---
type: report
title: "PRD #246 / #253 全窗口精致化最终实拍与验收"
tags: [evidence, visual, accessibility, motion, acceptance]
intent: 将全窗口实拍、源码绑定、行为门禁与本地交付状态分开记录，供复核界面精致化结果。
---

# 全窗口精致化最终实拍与验收

本目录记录界面精致化 PRD #246 的收口，基础设施 PRD #252 留到后续。管理窗口、悬浮窗、提醒中心、提醒规则编辑器、共享确认框及五个共享弹层共 41 张实拍已经逐张人工复核，图片的字节哈希与复制结果记录在 [review-manifest.json](review-manifest.json)，原捕获元数据在 [surfaces.json](surfaces.json)。同一最终候选的全部适用本机验证已通过；[最终验收状态](acceptance-status.json)、[独立行为事实](independent-behavior-facts.json)和[正式回归回执](final-regression-summary.json)分别记录结果、行为与进程终态。

设计方法：`Method: design review only (detector not applicable, native WPF)`。

## 交付行为

- 倒计时行与便签卡保持名称/正文和剩余时间的阅读主轴，编辑、删除是稳定次级动作；列表空态按真实视口居中，没有隐藏真实溢出。
- 管理窗口回收创建表单与区块空白，便签状态、搜索与多选工具条固定在列表上方。首选尺寸、最小窗口、工作区缩小与 DPI 变化均使用实际几何探针，未缩小字体、命中区或整窗比例。
- 悬浮窗只在该方向仍有真实内容时对正文边缘施加短渐隐，18 个状态覆盖边界、留白、滚轮、thumb、键盘、搜索、快速新增和菜单；滚动条、命令区、玩偶与弹层不受遮罩。
- 两处展开箭头共用既有 100ms 旋转，面板和窗口不执行布局动画。键盘、程序移交、系统已关闭动画以及旋转中途关闭动画都立即到达终态；反向从当前可见角度继续。稳定刷新不重建按钮、不重启动画。
- 提醒中心的完整「（北京时间）」标记位于日期时间之后的下一行。常规按钮保持 `RadiusControl` 9 DIP；既定分段/页签激活胶囊、数字色块和底部命令胶囊按设计使用全圆角。

#251 的尺寸、DPI 与滚动实拍另见 [工作台密度与滚动证据](../clock-countdown-issue-251-density-scroll-context/README.md)。

## 当前图片的来源

这 41 张图片来自同一候选：集成分支 `feat/246-ui-polish` 的 `fb1b1477` 加 #253 本地补丁，官方隔离 BaselineGenerate run `20261109T164950804-1444e87e`，捕获目录 `logs/ui-captures/20261109T085118921-validate-only/surfaces/`。候选 DLL SHA-256 为 `F4A68D157E7B551A92DE155ABEB0AD2082A2BB0A9D4268569D7F0361FC43F25D`；EXE 为 `FE8A141A1F896F9CB3ACC814BB5A13628A9691830D8F4235952E28C0E4086EF5`。图片按 96 DPI 渲染，1 px = 1 DIP；所有窗口都在官方隔离桌面里创建。

评审修复后的 v74 候选重新进行了捕获：BaselineGenerate `20261109T200515625-1e237c47` 严格退出 0，候选 DLL SHA-256 `1EF81F42A561C9BC39FC7C7DC71F019BD13B61394E4DD784E30D2FB96783A0C2`。41 张新捕获与本目录已逐张人工复核的 PNG 全部逐字节一致，因此沿用人工判定，不重复伪称又看了一轮新画面；新原始元数据在 [surfaces-v74.json](surfaces-v74.json)，主窗 22 项和悬浮窗 27 项当前源码绑定核对、生成报告哈希及跨版本关联在 [final-source-binding.json](final-source-binding.json)。14 张正式基线帧与状态指纹也全部和 v73 一致。

坐标校准后的当前最终候选为 **v75**，DLL SHA-256 `FC68217948ACE2FA85E30FC4ED0A3A55DD964A130F4D242B2E9DDAE534A4031C`。BaselineGenerate `20261109T203340606-a12d0d4a` 退出 0，完整单边框检查与负例门均通过，GeometryPreflight `20261109T203724144-7ef21308` 为 9/9。v74→v75 没有任何界面源码绑定、PNG 或状态指纹变化；41 张重新捕获的跨窗帧也逐字节一致。当前元数据为 [surfaces-v75.json](surfaces-v75.json)，当前来源仍由 [final-source-binding.json](final-source-binding.json) 指向，旧 v74 来源保留于 [source-binding-v74.json](source-binding-v74.json)。完整七阶段回归 `20261109T203840236-4ac76637` 已全部退出 0。来源文件保留捕获时的状态快照；其独立验证待办现在均由 [最终验收状态](acceptance-status.json)闭环。

人工逐张核对了文字换行、可读性、边界、单层焦点、高对比度和操作可见性，判定本批批准改动的结果与回归，不将既有外观的逐字节一致解释成所有历史问题已经修复。[#250 复核记录](../clock-countdown-issue-250-surfaces/README.md#复核)中的确认框危险按钮、极长确认文案、禁用提醒的下拉外观、自定义时间折行和重复错误提示仍按原范围保留；本次明确批准的时区换行与圆角文案裁决已落实。滚动视口外内容的自然裁切保持既有规则；动作可达性、字段验证、焦点生命周期和持久化结果以运行门禁为准。共享弹层按自身尺寸单独捕获，避免将其叠回宿主时被窗口边缘裁掉。

| 界面 | 普通 / 长内容 | 错误 / 禁用 | 焦点 / 高对比度 |
| --- | --- | --- | --- |
| 管理窗口 | [倒计时](main-countdowns-normal.png)、[便签](main-notes-normal.png)、[倒计时长内容](main-countdowns-long.png)、[便签长内容](main-notes-long.png) | [空名称](main-countdowns-error.png) | [键盘焦点](main-countdowns-focus.png)、[倒计时高对比](main-countdowns-high-contrast.png)、[便签高对比](main-notes-high-contrast.png) |
| 悬浮窗 | [倒计时](floating-countdowns-normal.png)、[便签](floating-notes-normal.png)、[倒计时长内容](floating-countdowns-long.png)、[便签长内容](floating-notes-long.png) | [快速新增错误](floating-quick-add-error.png) | [键盘焦点](floating-focus.png)、[倒计时高对比](floating-countdowns-high-contrast.png)、[便签高对比](floating-notes-high-contrast.png) |
| 提醒中心 | [普通](reminder-center-normal.png)、[长内容](reminder-center-long.png) | [自定义稍后错误](reminder-center-error.png)、[未落盘禁用](reminder-center-disabled.png)、[送达失败](reminder-center-delivery-failure.png) | [焦点](reminder-center-focus.png)、[高对比](reminder-center-high-contrast.png) |
| 规则编辑器 | [普通](rules-editor-normal.png)、[长列表](rules-editor-long.png)、[每周](rules-editor-calendar-weekly.png)、[每年](rules-editor-calendar-yearly.png) | [无效间隔](rules-editor-error.png) | [焦点](rules-editor-focus.png)、[高对比](rules-editor-high-contrast.png) |
| 共享确认框 | [长正文](confirmation-delete-long.png) | 安全默认与取消由行为探针覆盖，没有新增错误态 | [焦点](confirmation-focus.png)、[高对比](confirmation-delete-high-contrast.png) |
| 共享弹层 | [筛选](popup-filter.png)、[设置](popup-preferences.png)、[标签管理](popup-tag-manager.png)、[标签选择](popup-tag-selector.png)、[便签菜单](floating-note-overflow-menu.png) | [设置错误](popup-preferences-error.png) | [筛选高对比](popup-filter-high-contrast.png)、[设置高对比](popup-preferences-high-contrast.png) |

## 验证记录

首次 v73 生成曾因同一次底部锚定采样的两个投影字段失败而退出 1。失败及半成品已保留；没有修改定位算法或放宽 2.5 DIP 容差。添加只读位置观测后，源码 validate-only 与 EXE 路径均通过，各采样点底部间距 12 DIP；这证明后续运行正确，不等于已经证实首次失败的根因。

双轴评审发现的「旋转中途关闭动画」边界已补活动时钟的通知处理，并补键盘先操作、Space/Enter、稳定分钟刷新、自然结束、旋转中关闭动画和卸载回归。修复后的 GeometryPreflight `20261109T195840850-de64bf3c` 全部通过。随后只断开通知订阅的负例 `20261109T200227494-99daeb7d` 为 8/9，唯一错误为旋转中途关闭动画未即时到终态；恢复订阅后，最终 v74 候选 GeometryPreflight `20261109T200810855-0eb4f81e` 为 9/9。第一次完整回归在 Core 通过后由 CSharp 报出单边框探针失败，其余阶段按规则停止，原始回执见 [校准前回归](pre-calibration-regression-summary.json)。已确认这是 #251 将字段上移 10 DIP 后，#237 探针仍使用旧坐标；v71 与 v74 的对应扫描行在平移后每个 RGB 像素完全一致，度量见 [单边框坐标校准](border-probe-calibration.json)。本次只校准验证坐标和精确期望，保留通道差阈值 30、完整边线序列、标签隔离、声明装饰带和三个负例。界面像素没有为此改变，随后 v75 新候选的独立完整回归已全部通过，完整 CSharp 为退出 0，确认该精确边线断言通过，未用基线延后退出码代替成功。

## 最终本机验收

GeometryPreflight 与完整相关组合均通过，全部是退出码 0，没有将参考基线延后视作成功。窗口阶段均在不可见隔离桌面中执行，Tooling 不创建产品窗口，整轮 MaxParallel=1。

| 阶段 | 结果 | 正式运行耗时 |
| --- | --- | --- |
| Build（全量 Clean + R2R） | 通过 | 248 秒 |
| GeometryPreflight | 通过，9/9 | 36 秒 |
| Core | 通过 | 321 秒 |
| CSharp | 通过 | 667 秒 |
| Ui | 通过 | 451 秒 |
| SourceValidation | 通过 | 95 秒 |
| InstallValidate | 通过 | 10 秒 |
| ValidationHarness | 通过 | 26 秒 |
| Tooling | 通过 | 65 秒 |

[验收记录](issue-acceptance-records.json)覆盖 #247–#251、#253，契约校验 Valid=true，共 6 票；各票引用实际独立报告中的行为名。已通过本地回写预演，`Applied=false`，见 [预演结果](writeback-preview-result.json)，没有发送评论或修改状态。23 个静态完整性钉值与暂存 blob 相符，最终本地链接和 diff 检查通过。

双轴评审的动态禁动画与键盘/稳定刷新覆盖发现已完成修复。Standards 最终复核无确认缺陷，保留一条非阻断测试建议：给 KeyboardClick 补充实际焦点断言。最终 Spec 子代理未完成时，由主代理根据原发现、源码、真实负例与正式回归逐项复核，未把缺失的子代理答复冒称为通过。

## 状态边界

本目录保留本机验收完成时（2026-11-09 21:40）的源码候选快照；该时间点尚未发布、部署或修改远端 issue。后续经用户授权进行的 main 发布、关票与分支收尾，以 GitHub 的提交、CI 和 issue 回写记录为准；办公室 live verified 仍须另外执行。最后已补齐仓库正式受管 **Clean + R2R** 构建 `20261109T213159664-abb33ca5`，退出 0；[全量构建核对](final-clean-build.json)证明产品 EXE、应用 DLL、核心 DLL、发布清单和验证程序五项与完整回归候选逐字节相同，所以继续采用该同一候选的正式回归证据。全量构建通过前，曾明确将状态标为待补，未把增量构建当作全量构建。

用户已批准本机安全清理策略新增当前集成目录的八个精确可重建产物根（`app`、`verification` 与三个 C# 项目的 `bin/obj`），原有条目未变；只为满足这次全量构建要求。登记工具、规定的安全删除自检与构建预检均通过，变更前快照和撤销变更集保留在本次主机运行证据目录。没有将许可扩大到工作树根，也没有启动 PRD #252 的基础设施深化。
