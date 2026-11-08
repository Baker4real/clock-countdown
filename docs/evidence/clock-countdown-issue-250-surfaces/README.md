---
type: report
title: "#250 共享表面与次级窗口同版实拍"
tags: [evidence, visual, surfaces, high-contrast]
intent: 保存 #250 交付源码在管理窗口、悬浮窗、提醒中心、提醒规则编辑器与共享弹层上五类状态的真实截图，供人工核对层级、边界、错误行与单层焦点。
---

# #250 共享表面与次级窗口同版实拍

结论：四类窗口（管理窗口、悬浮窗、提醒中心、提醒规则编辑器）各自覆盖普通、长内容、错误/禁用、键盘焦点与系统高对比度五类状态；删除确认与筛选、设置、标签管理、标签选择、悬浮便签「…」菜单五个共享弹层按下表补拍。41 帧来自交付源码的同一次 validate-only，`surfaces.json` 的 `failures` 为空。圆角是否落在 token 上以门禁事实 `SharedSurfaceRadiiFollowTokens`（窗口短验证里的 `SharedSurfaceRadii`）为准；这些截图不进门禁，供人工核对。

## 来源

41 帧的来源相同：

- 提交：`cbbb34da`，即 `0dd21e8d`（圆角归并、提醒中心计划行按卡宽换行）加 v71 基线重拍。本目录所在的证据提交只新增本目录文件，不改源码。
- 构建：worktree 增量构建，`app\MinminCountdown.dll` SHA-256 `DC077963CD0010C667117F38A4CEDF0D106A9ACAB25B3BBDB1EA31FE04894991`。
- run：README「本机验证」的低影响启动器 run `20261109T003820982-f1c85819`（`Core,CSharp,Ui,SourceValidation,InstallValidate`，五个阶段都退出 0）的 CSharp 阶段，在隔离桌面里渲染；捕获目录 `logs/ui-captures/20261108T164840410-validate-only/surfaces/`。PNG 从该目录原样复制；`surfaces.json` 只按仓库 `.gitattributes` 改成 LF 行尾，其中的 `path` 仍指向原捕获位置，`sha256` 与本目录的 PNG 一致。同一构建上另外三次捕获（同一 run 的 `20261108T165145045-validate-only`，更早的 `20261108T163153392-validate-only` 与 `20261108T162752821-validate-only`）的 41 帧 SHA-256 与本组逐帧相同。
- DPI 与尺寸：96 DPI，1 px = 1 DIP。管理窗口 864×1000、悬浮窗 520×390、提醒中心 460×520、提醒规则编辑器 640×790、删除确认 390×343；弹层按自身尺寸单独渲染（叠回宿主会被窗口右缘裁掉）。
- 场景数据：参考时刻 `2026-07-29T01:20:17Z`（北京时间 09:20），种子与压力数据来自既有验证场景；节假日日历读临时缓存里的参考年份工作日表，不联网。
- 高对比度：在验证作用域里注入经典「高对比黑」系统色（窗口 `#000000`、文字 `#FFFFFF`、高亮 `#1AEBFF`、高亮文字 `#000000`、灰字 `#3FF23F`），不读本机设置。

## 覆盖

| 表面 | 普通 | 长内容 | 错误/禁用 | 键盘焦点 | 系统高对比度 |
| --- | --- | --- | --- | --- | --- |
| 管理窗口 | [倒计时页](main-countdowns-normal.png)、[便签页](main-notes-normal.png) | [倒计时](main-countdowns-long.png)、[便签](main-notes-long.png) | [空名称添加](main-countdowns-error.png) | [「＋ 添加倒计时」](main-countdowns-focus.png) | [倒计时页](main-countdowns-high-contrast.png)、[便签页](main-notes-high-contrast.png) |
| 悬浮窗 | [倒计时](floating-countdowns-normal.png)、[便签](floating-notes-normal.png) | [倒计时](floating-countdowns-long.png)、[便签](floating-notes-long.png) | [空正文快速新增](floating-quick-add-error.png) | [快速新增入口](floating-focus.png) | [倒计时](floating-countdowns-high-contrast.png)、[便签](floating-notes-high-contrast.png) |
| 提醒中心 | [两条提醒](reminder-center-normal.png) | [三条提醒](reminder-center-long.png) | [自定义稍后缺日期时间](reminder-center-error.png)、[送达失败](reminder-center-delivery-failure.png)、[送达未落盘](reminder-center-disabled.png) | [首条「知道了」](reminder-center-focus.png) | [两条提醒](reminder-center-high-contrast.png) |
| 提醒规则编辑器 | [规则列表与新增表单](rules-editor-normal.png)、[每周规则](rules-editor-calendar-weekly.png)、[每年规则](rules-editor-calendar-yearly.png) | [八条规则](rules-editor-long.png) | [间隔为 0 时保存](rules-editor-error.png) | [「保存规则」](rules-editor-focus.png) | [每周规则](rules-editor-high-contrast.png) |
| 删除确认 | 见长内容 | [长便签正文](confirmation-delete-long.png) | 无错误态 | [「确认删除」](confirmation-focus.png) | [长便签正文](confirmation-delete-high-contrast.png) |
| 设置弹层 | [打开设置](popup-preferences.png) | 未拍 | [负月薪保存](popup-preferences-error.png) | 未单独拍 | [打开设置](popup-preferences-high-contrast.png) |
| 筛选弹层 | [便签页筛选](popup-filter.png) | 未拍 | 无错误态 | 未单独拍 | [打开筛选](popup-filter-high-contrast.png) |
| 标签管理弹层 | [三枚标签与新建](popup-tag-manager.png) | 未拍 | 未拍 | 未单独拍 | 未拍 |
| 标签选择弹层 | [搜索框与三枚标签](popup-tag-selector.png) | 未拍 | 无错误态 | 未单独拍 | 未拍 |
| 悬浮便签「…」菜单 | [编辑 / 删除](floating-note-overflow-menu.png) | 无长内容 | 无错误态 | 未单独拍 | 未拍 |

证据边界：

- 弹层打开后焦点在首个可聚焦项上（筛选的「任一匹配」、标签管理的名称输入框等），这些帧顺带显示了键盘焦点外观，所以「键盘焦点」一栏写作未单独拍。焦点框是否出现取决于最近一次输入设备，验证进程里没有真实鼠标输入。
- 标签管理的长列表与重名错误、标签选择与「…」菜单的高对比度没有拍：它们用的输入、按钮、芯片模板与 token 在管理窗口和悬浮窗的对应帧里已经出现；需要逐个弹层的五类全覆盖时要另加场景。
- `surfaces.json` 的 `visibleErrors` 列的是可视树里可见的错误色文字，不限于视口内：`reminder-center-error` 也列出视口外第二张卡的送达失败。

## 各帧状态

| 帧 | 状态 | 尺寸（px） |
| --- | --- | --- |
| `main-countdowns-normal` | 倒计时页：三条倒计时、标签芯片、收起的新增区与状态栏 | 864×1000 |
| `main-notes-normal` | 便签页：三张便签、日期分组、完成勾选与新增区 | 864×1000 |
| `main-countdowns-long` | 九条倒计时（30 字标题、已到期），纵向滚动条 | 864×1000 |
| `main-notes-long` | 四张未完成便签，滚到长正文便签（两枚标签、每 15 分钟提醒、昨天分组） | 864×1000 |
| `main-countdowns-error` | 名称留空后添加：名称下方字段错误与表单汇总行，焦点回到名称输入框 | 864×1000 |
| `main-countdowns-focus` | 展开新增区，键盘焦点在「＋ 添加倒计时」主按钮 | 864×1000 |
| `main-countdowns-high-contrast` | 系统高对比度：倒计时页 | 864×1000 |
| `main-notes-high-contrast` | 系统高对比度：便签页 | 864×1000 |
| `floating-countdowns-normal` | 距离下班主项与三条倒计时 | 520×390 |
| `floating-notes-normal` | 三张便签卡 | 520×390 |
| `floating-countdowns-long` | 九条倒计时（30 字标题、已到期） | 520×390 |
| `floating-notes-long` | 四张便签，滚到长正文便签（折行、下次提醒、日期印记） | 520×390 |
| `floating-quick-add-error` | 快速新增正文留空后添加：正文下方字段错误，焦点回到正文输入框 | 520×390 |
| `floating-focus` | 键盘焦点在快速新增入口 | 520×390 |
| `floating-countdowns-high-contrast` | 系统高对比度：倒计时 | 520×390 |
| `floating-notes-high-contrast` | 系统高对比度：便签 | 520×390 |
| `floating-note-overflow-menu` | 首张便签打开「…」：编辑 / 删除 | 132×107 |
| `reminder-center-normal` | 两条已送达提醒：来源、正文、相对时间与四个处置 | 460×520 |
| `reminder-center-long` | 三条提醒（长便签正文、30 字倒计时标题），纵向滚动 | 460×520 |
| `reminder-center-error` | 便签条目选自定义稍后但缺日期时间：日期与时间控件展开，错误紧邻其下 | 460×520 |
| `reminder-center-delivery-failure` | 滚到倒计时条目：送达失败，「知道了 / 稍后提醒」禁用，「打开来源」可用 | 460×520 |
| `reminder-center-disabled` | 送达状态未落盘：「知道了 / 稍后提醒 / 完成便签」禁用，「打开来源」可用 | 460×520 |
| `reminder-center-focus` | 键盘焦点在首条「知道了」 | 460×520 |
| `reminder-center-high-contrast` | 系统高对比度：两条已送达提醒 | 460×520 |
| `rules-editor-normal` | 四条规则列表与新增规则表单 | 640×790 |
| `rules-editor-calendar-weekly` | 编辑每周规则：日历频率、重复间隔与星期复选 | 640×790 |
| `rules-editor-calendar-yearly` | 编辑每年规则：月份与日期选择 | 640×790 |
| `rules-editor-long` | 八条规则达到上限，列表纵向滚动 | 640×790 |
| `rules-editor-error` | 取消全部星期、重复间隔填 0 后保存：字段错误行 | 640×790 |
| `rules-editor-focus` | 键盘焦点在「保存规则」 | 640×790 |
| `rules-editor-high-contrast` | 系统高对比度：编辑每周规则 | 640×790 |
| `confirmation-delete-long` | 长便签正文删除确认：标题、折行正文与「取消 / 确认删除」 | 390×343 |
| `confirmation-focus` | 键盘焦点在「确认删除」 | 390×343 |
| `confirmation-delete-high-contrast` | 系统高对比度：长便签删除确认 | 390×343 |
| `popup-preferences` | 倒计时页打开设置：月薪、上下班时间、显示精度与保存 | 444×268 |
| `popup-preferences-error` | 月薪填 -500 后保存：字段下方错误，焦点回到月薪输入框 | 444×292 |
| `popup-preferences-high-contrast` | 系统高对比度：打开设置 | 444×268 |
| `popup-filter` | 便签页打开筛选：标签匹配、状态与日期 | 388×303 |
| `popup-filter-high-contrast` | 系统高对比度：打开筛选 | 388×303 |
| `popup-tag-manager` | 倒计时页打开标签管理：三枚标签与新建 | 319×261 |
| `popup-tag-selector` | 展开新增区后打开标签选择：搜索框与三枚标签 | 230×142 |

## 复核

- 圆角：区块表面与浮层是 `RadiusOverlay`，条目卡、提醒卡、规则行与次级窗口外壳是 `RadiusCard`，按钮与输入是 `RadiusControl`，芯片与勾选框是 `RadiusChip`；胶囊页签、徽章、强调轨、快速新增 6px 等保留项见 DESIGN.md §13.3。
- 焦点：各键盘焦点帧里只有目标控件带一层焦点环。
- 错误：字段错误贴在所属字段下方；提醒中心的送达失败写在条目内，「打开来源」保持可用。
- 提醒中心计划行按卡宽折行后整行留在卡片内，代价是「（北京时间）」会和正文一样逐字断开（见 `reminder-center-normal`）。

截图里看到、本票没有处理的既有问题：

- `confirmation-delete-long`：「确认删除」是 WPF-UI 实心 Danger 外观，`#B74C39` 底配深色字，对比度约 3.1:1，低于正文 4.5:1，也和 DESIGN.md 的描边 `DangerButton` 不一致。
- `confirmation-delete-high-contrast`：高对比度下页脚变高，长正文末行「10 秒内可撤销。」被裁掉，WPF-UI 的动态滚动条没有显示。
- `reminder-center-delivery-failure`、`reminder-center-disabled`：处置按钮禁用时，稍后时长下拉仍是可用外观。
- `reminder-center-error`：自定义稍后的日期与时间控件在 460 宽的提醒中心里折成两行。
- `main-countdowns-error`：字段下方错误与表单汇总行重复同一句。
- `rules-editor-normal`：同屏有「添加规则」与「完成」两个实心主按钮；规则行与 `popup-tag-manager` 的「删除」是红描边，倒计时行的删除是透明底次级动作。
