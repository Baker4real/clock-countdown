---
type: reference
title: 便签暖粉日间设计系统
tags: [design-system, notes, wpf, warm-pink, single-day-theme]
intent: 固化单一日间暖粉方向的视觉、组件和主题契约，作为五个界面实现与视觉基线验收的唯一依据。
---

# 便签暖粉日间

## 1. 视觉主题与氛围

“暖粉日间”是应用全部五个界面（主窗口倒计时页、主窗口便签页、悬浮窗倒计时工作区、悬浮窗便签工作区、提醒中心）的唯一视觉方向：暖白奶油底、粉色主强调、圆润分格数字和手写体装饰文案。界面面向每天反复查看倒计时和便签的使用者，优先让人一眼读出“还剩多久”“还有什么没做完”，装饰只承担情绪价值，不抢走注意力。

设计读取：这是桌面时间与便签管理控件，面向高频工作台使用者，用暖粉奶油表面、粉红主强调、pastel 分格和圆角分段控件表达，倾向现代 iOS 式圆角分段控件与 pastel 徽章语义，但保留项目既有的三丽鸥玩偶与手写体装饰。

已退役方向：旧版“暖纸时间轴”方向 B 不再有效，其章节与原型链接一并失效。视觉依据为 5 张 AI 优化稿（见 §11）。

拨盘：视觉冒险度 7/10（有明确记忆点，但控件形态仍是标准分段/单选/按钮）；动效强度 3/10（只保留即时反馈，不给高频切换与收放增加戏剧动画）；信息密度 6/10（分格数字和徽章提高扫读密度，卡片间保留呼吸）。

## 2. 色板与角色

应用固定为单一日间主题：没有夜间色板、没有自动/夜间模式、没有主题切换入口。旧设置中的 `Auto` / `Night` 偏好一律按日间解释。色板取自 5 张 AI 优化稿的实际像素采样（采样口径见 §11）；它是实现选色依据，不是像素门禁的期望值。

### 画布与表面

| 角色 token | 值 | 用途 |
| --- | --- | --- |
| `CanvasBrush` | `#FDF3EB` | 主窗口页面画布暖奶油底；页面下缘由装饰 raster 渐深，采样到约 `#FDE8DC` |
| `CanvasDeepBrush` | `#FDE8DC` | 画布底部渐深端点 |
| `BackgroundBrush` | `#FDF0E4` | 悬浮窗外壳奶油底 |
| `CardBrush` | `#FEFCFB` | 主窗口区块卡表面（新增倒计时、新增便签、显示设置、状态区） |
| `PanelBrush` | `#FFFDFC` | 内嵌输入面、白纸便签卡 |
| `CardAltBrush` | `#FEF9F5` | 次级区块表面（倒计时列表条目底、次级按钮悬停面） |
| `StatusBarBrush` | `#FDFAF8` | 窗口底部状态条 |

### 文字与结构线

| 角色 token | 值 | 用途 |
| --- | --- | --- |
| `InkBrush` | `#3B190D` | 主文字、区块标题、列表标题、提醒中心标题与来源标签 |
| `BodyBrush` | `#6B4A3C` | 正文、卡片副标题、字段标签 |
| `MutedBrush` | `#8A7069` | 辅助文字、占位文、单位字、时间戳、数量 |
| `PeachLineBrush` | `#F6DFD3` | 1px 结构边界、区块分隔线、输入框常态边界 |
| `ActiveBrush` | `#E05A4A` | 悬停与按压反馈、键盘焦点边界 |
| `WorkspacePanelBrush` | `#00FFFDFC` | 主窗口装饰带以外的透明面板底（不遮挡装饰图） |
| `WorkspacePanelLineBrush` | `#00F6DFD3` | 同上，透明面板边界 |

### 强调与控件

| 角色 token | 值 | 用途 |
| --- | --- | --- |
| `MainActionBrush` | `#E7593B` | 主行动实心按钮（提醒中心“知道了”等） |
| `MainActionHoverBrush` | `#D24C30` | 主行动按钮悬停 |
| `MainActionPressedBrush` | `#BA4128` | 主行动按钮按压 |
| `MainActionTextBrush` | `#FFFDFC` | 主行动按钮文字与图标 |
| `CoralActionBrush` | `#D68481` | 主窗口次级实心按钮（“保存设置”“＋ 添加”），比主行动更浅 |
| `PinkAccentBrush` | `#DE5863` | 主窗口顶部激活页签文字、粉色强调文字 |
| `PinkAccentStrongBrush` | `#F06F9B` | 悬浮窗粉色实心主按钮（“＋ 添加便签”）与悬浮窗激活分段胶囊 |
| `IconAccentBrush` | `#F28995` | 区块标题图标、粉色圆形加号、日历/标签/时钟等字段图标 |
| `PinkBadgeBrush` | `#FD97A1` | “超期待！”徽章底、粉色小胶囊徽章底 |
| `PinkBadgeTextBrush` | `#FFFDFC` | “超期待！”徽章文字 |
| `PinkTintBrush` | `#FEDDDE` | 主窗口顶部激活页签胶囊底 |
| `PinkTintSoftBrush` | `#FEF0F1` | 悬浮窗主项卡片底、粉色淡底 |
| `PinkBlockBrush` | `#FEE0E4` | 主项数字色块底 |
| `SecondaryBlockBrush` | `#FAF0EA` | 次项数字色块底（暖米色空槽） |
| `PrimaryCountdownNumberBrush` | `#E01133` | 主项数字色块内的数值（高饱和红粉） |
| `FloatingDayPrimaryCountdownAccentBrush` | `#EE2B46` | 悬浮窗主项左强调轨；该 token 不得复用玩偶阴影资源，合成背景上文字对比度不低于 `4.5:1` |
| `PeachTintBrush` | `#FEEFE1` | 便签页激活分段胶囊底 |
| `PeachRailBrush` | `#CD623E` | 激活分段胶囊左强调轨、`#` 标签芯片暖橙描边、日期分组标题圆点 |
| `PeachBadgeBrush` | `#FBAE90` | 激活分段数量徽章底（文字白） |
| `PeachBadgeMutedBrush` | `#F3DED8` | 未激活分段数量徽章底（文字 `MutedBrush`） |

### 便签与提醒

| 角色 token | 值 | 用途 |
| --- | --- | --- |
| `NoteBrush` | `#FEF5DC` | 便签黄卡片（活跃/未完成条目）、分组标题卡暖黄底 |
| `NoteLineBrush` | `#F6D9B2` | 便签黄卡 1px 边界、复选框暖黄描边 |
| `NoteWhiteBrush` | `#FEFDFD` | 白纸便签卡（无需强调的条目），边界用 `PeachLineBrush` |
| `DangerBrush` | `#C8503F` | 删除文字与图标 |
| `SuccessBrush` | `#529F8C` | 完成/成功语义 |
| `ExpiredBrush` | `#F9DFDB` | 已到期倒计时弱化底 |
| `TagChipBrush` | `#FFF7E8` | 默认标签芯片底（无持久化颜色时） |
| `TagChipTextBrush` | `#3B190D` | 标签芯片文字 |
| `WarningAccentBrush` | `#DC6922` | 提醒中心“待处理”徽章文字与感叹号圆图标 |
| `WarningTintBrush` | `#FDEDD8` | 提醒中心“待处理”徽章底 |
| `ReminderCardBrush` | `#FEF9E8` | 提醒中心条目卡暖黄底 |
| `ReminderPageBrush` | `#FEFEFE` | 提醒中心页面白底 |
| `SourceNoteIconBrush` | `#FED67F` | 提醒中心便签来源图标底；倒计时来源图标用 `PinkTintSoftBrush` 底加粉边 |
| `DecoTextBrush` | `#D9B7B1` | 主窗口手写体装饰文案、角落文案 |
| `DecoTextSoftBrush` | `#E5AE9C` | 悬浮窗手写体装饰文案 |

### 日期印记 pastel 轮换

| 角色 token | 底 | 数字 |
| --- | --- | --- |
| `StampPinkBrush` / `StampPinkTextBrush` | `#FEEFF1` | `#D41F57` |
| `StampBlueBrush` / `StampBlueTextBrush` | `#EEF5FE` | `#2F86DB` |
| `StampGreenBrush` / `StampGreenTextBrush` | `#EDF6E8` | `#318939` |

装饰带与玩偶层的像素不受以上 token 约束；它们是随包版本化的 raster 资产，只改 `Brush` 归属不改资产本身。

标签颜色规则：默认标签仍来自暖色组；AI 稿中出现的 pastel 芯片（粉 `#FEE0E3`、淡紫 `#EFE8FE`、薄荷 `#DDF7ED`、暖黄 `#FEF5DC`）作为**标签芯片可选 pastel 组**记录，仅用于按条目轮换的展示；任何已持久化的标签颜色保持原值，不迁移、不强制改写。

## 3. 排版规则

- 中文 UI 使用 `Microsoft YaHei UI` 和系统 fallback。
- 手写体装饰文案使用手写感字族，按序 fallback：`Segoe Print` → `Ink Free` → `Segoe Script` → `楷体` → `Microsoft YaHei UI`。四者在 Windows 10/11 简体中文环境随系统安装（`segoepr.ttf`、`Inkfree.ttf`、`segoesc.ttf`、`simkai.ttf`），渲染不得依赖 webfont 或联网字体。
- 数字使用表格数字（`Typography.NumeralAlignment=Tabular`）并保持半角；中西文与数字之间留自然空格。
- 主窗口常态 `760 x 880` Viewbox 缩放（约 `0.652`）下，日期数量 `9.8 DIP`、状态辅助说明 `10.4 DIP`、日期标题 `11.7 DIP` 和列表便签正文 `13.0 DIP`；这些层级只承担辅助扫读或受当前绝对定位布局限制。
- 设计坐标字号与常态渲染值对应为：状态标题 `22px -> 14.3 DIP`、辅助说明 `16px -> 10.4 DIP`、数量 `22px -> 14.3 DIP`；日期标题 `18px -> 11.7 DIP`，日期数量 `15px -> 9.8 DIP`；便签正文继承 `20px -> 13.0 DIP`，便签卡提醒摘要 `18.5px -> 12 DIP`，仍按悬浮窗合同使用 `12 DIP`。
- 新增层级（设计坐标）：区块标题 `22px`、列表条目标题 `22px`、输入内容 `20px`、字段标签 `18px`、辅助/摘要 `16px`、徽章文字 `15px`、手写体装饰文案 `17px`。
- 字重只用 Regular、SemiBold；层级主要靠位置、颜色和间距建立。区块标题、列表标题与选中态使用 SemiBold。
- 正文允许换行，标题和数量不换行；长便签由正文区域吸收宽度，操作区保持稳定尺寸。

## 4. 组件样式

### 数字色块分格

剩余时间由“数值圆角色块 + 单位字”成对构成：

- 每个分格是圆角矩形色块，内部只放数值，色块外侧紧跟小号单位字（`天 / 时 / 分 / 秒`）。
- 分格宽高比近似 `1:1` 偏竖长（AI 稿实测约 `64 x 96` px，宽高比约 `2:3`），圆角约为短边的 `1/4`；四个分格等距排布，分格横向间距约等于分格宽度的三分之一。
- 主项（列表第一项，含“距离下班”）的分格底为 `PinkBlockBrush`，数值为 `PrimaryCountdownNumberBrush`。
- 次项分格底为 `SecondaryBlockBrush`，数值为 `InkBrush`。
- 单位字始终为 `MutedBrush`，字号小于数值约 `1/3`，不进入色块。
- 倒计时表面为固定 `194 DIP` 右对齐列；分格后仍需完整单行显示，不得省略。
- 数值不得换行、不得裁切；长剩余时间（如 `999 天`）允许压缩分格内边距而不是压缩字号到不可读。

### 「超期待！」徽章

- 形态：圆角胶囊，底 `PinkBadgeBrush`，文字 `PinkBadgeTextBrush` 字重 SemiBold。
- 位置：紧贴主项标题右侧，垂直居中于标题行，与标题左缘间距约 `12 DIP`。
- 触发范围：只在“距离下班”主项显示。主项由普通倒计时承担时也允许显示该徽章，是主项语义而非特定标题的装饰；筛选激活时不显示“距离下班”主项，因此也不显示该徽章。
- 不可点击、不可聚焦、不参与命中测试。

### 并排分段切换控件

用于主窗口便签页的未完成/已完成视图切换。形态为容器胶囊 + 激活胶囊 + 数量徽章。

- 容器：圆角胶囊，底为透明或 `CardAltBrush`，内部左侧留 `2 DIP` 内边距，使激活胶囊悬浮在容器内。
- 激活段：圆角胶囊，使用 `PeachTintBrush` 底（或 `PinkTintBrush`，同一界面内二选一，不得混用），左缘带 `4 DIP` 宽 `PeachRailBrush` 强调轨，文字 `InkBrush` SemiBold。
- 未激活段：无底色，文字 `MutedBrush`。
- 数量徽章：激活段徽章底 `PeachBadgeBrush`，文字白；未激活段徽章底 `PeachBadgeMutedBrush`，文字 `MutedBrush`。徽章文字不换行。
- 两段必须并排同屏可见、同时可点击；不再使用“点击标题切换、另一段隐藏”的单标题形态。
- 该控件只切换列表视图，不改变未完成/已完成两个平行章节彼此独立收放的语义（见 §5）。
- 主窗口顶部页签（倒计时/便签）是另一处并排分段控件：激活段底 `PinkTintBrush`、文字 `InkBrush` SemiBold（便签页激活段底 `PeachTintBrush`、文字 `InkBrush`；两处激活文字已按 §13.4 对比度基线由 `PinkAccentBrush`/`PeachRailBrush` 换绑，issue #133），未激活段无底、文字 `InkBrush`。页签右侧稳定显示 `筛选`、`标签` 命令胶囊与一个数量文字（`N 个倒计时` / `N 个待办`），数量不换行。
- 悬浮窗工作区切换（倒计时/便签）也使用该形态：激活段底 `PinkAccentStrongBrush`、文字白 SemiBold，未激活段无底、文字 `InkBrush`；以 AI 便签稿为准，AI 倒计时稿中较浅的胶囊底不采用（见 §12）。

### 提醒规则单选组

倒计时页的提醒规则区改为一组三选一单选（`RadioButton`），标签依次为 `不提醒`、`按时间提醒`、`自定义提醒`。

- 组左侧是 `提醒规则` 标签与一个 `?` 帮助图标；帮助图标可聚焦并在悬停/聚焦时给出说明。
- 选中单选圈为 `IconAccentBrush` 实心圆点带同色描边；未选中为 `PeachLineBrush` 描边空心圈。
- 语义映射（照既有语义执行，不新增解释）：`不提醒` = 清空规则；`按时间提醒` = 创建 `absoluteOnce` 到点规则（offset 0）；`自定义提醒` = 打开现有 0~8 条规则次级编辑器。
- 该组不再包含“设为现在”控件。

### 输入框图标与占位文

- 每个输入框左缘内嵌一个 `20 DIP` 图标，图标与文字间距约 `12 DIP`；图标为单色描边风格，颜色 `MutedBrush`。
- 占位文一律 `MutedBrush`，常态文字 `InkBrush`；输入框底为 `PanelBrush`，边界 `PeachLineBrush`，圆角与控件同等。
- 字段标签在输入框上方，文字 `BodyBrush`。
- 规定的图标与占位文：

| 字段 | 图标 | 占位文 |
| --- | --- | --- |
| 倒计时名称 | 铅笔 | `例如：生日、考试、旅行…` |
| 截止日期 | 日历 | 默认填入当前北京时间日期 |
| 设置标签 | 标签页 | `选择标签` |
| 便签内容 | 无图标 | `写下要记住的事情吧…` |
| 便签日期 | 日历 | 默认填入当前北京时间日期 |
| 搜索便签 | 放大镜 | `搜索关键词…` |

### 手写体装饰文案

- 使用 §3 的手写字族：主窗口文案用 `DecoTextBrush`，悬浮窗文案用 `DecoTextSoftBrush`，`17px` 设计坐标，可带轻微旋转（不超过 `±3°`）。
- 位置为固定装饰层，`IsHitTestVisible=False`，不参与布局测量，不遮挡任何控件或可点击热区。
- 已确认落点：主窗口倒计时页区块标题右侧（`每一个期待，都值得被认真对待 ♡`）；主窗口页面右下角（`生活很忙，但别忘了可爱 ♡`）；悬浮窗倒计时工作区左下（`加油！美好的事情正在靠近 ~`）；悬浮窗便签工作区左下（`Have a nice day!`）与右下（`Live a sweeter day ♡`）。
- 文案本身不是契约，可随实现微调；字族、颜色、落点层级与不可命中规则是契约。

### 提醒中心两行按钮布局

提醒中心每条目的操作区固定为两行：

- 第一行：`知道了`（主行动，实心 `MainActionBrush` 底、白字、左侧白圈对勾图标）、`稍后提醒` 时间选择（次要按钮，`PanelBrush` 底、`PeachLineBrush` 边、左侧时钟图标、右侧下拉箭头，显示当前选定档位文本，例如 `10 分钟后`）、`稍后提醒`（次要按钮，左侧铃铛图标）。
- 第二行：`完成便签`（仅便签来源，左侧圆圈对勾图标）、`打开来源`（左侧外链图标）。
- 两行只包含该来源实际存在的动作；倒计时来源没有 `完成便签`，该行只保留 `打开来源`，且按钮不拉伸填满整行。
- 所有按钮图标化：图标在左、文字在右，图标与文字间距约 `8 DIP`。
- 稍后提醒档位选项保持 10、30、60 分钟、明天 09:00 与自定义；仅自定义模式展开日期、小时和分钟控件。
- 头部右上为 `全部已读` 次要按钮，左侧铃铛图标；其语义是对当前全部条目批量执行 `知道了`（见 §5）。
- 条目右侧 `待处理` 徽章：底 `WarningTintBrush`，`WarningAccentBrush` 的感叹号圆图标与文字。
- 条目左侧为来源图标方块：便签来源用暖黄便签图标（`SourceNoteIconBrush`），倒计时来源用粉底时钟图标；图标块不可点击。
- 条目只有一层卡片（`ReminderCardBrush` 底 + 1px 暖边），不再嵌套外层容器。

### 日期披露

每个日期实例拥有独立的 `Button` 和内容面板。标题展示相对日期或完整日期、数量和箭头；箭头向下表示展开，向右表示收起。日期轨道使用 1px 暖色线与圆点，属于时间语义而不是选中装饰。

- 分组标题卡左侧为 `PeachRailBrush` 圆点，标题 `M月d日 · 周X` 使用 `InkBrush` SemiBold。
- 右侧 `N 条` 数量使用 `MutedBrush`，紧跟展开箭头。
- 每个日期分组的收放状态独立持久化，互不联动。

### 便签卡与行内命令

- 便签卡圆角 `8px`，1px 边界；黄卡底 `NoteBrush`、边界 `NoteLineBrush`，白卡底 `NoteWhiteBrush`、边界 `PeachLineBrush`。内边距 14px，卡间距 8px。
- 行内命令固定为 `编辑`、`删除`、`…` 三个等距按钮，均有 1px 边界与 `8px` 圆角：`编辑` 文字与铅笔图标用 `InkBrush`，`删除` 文字与垃圾桶图标用 `DangerBrush`，`…` 用 `MutedBrush`。
- `…` 菜单内容与既有 `编辑` / `删除` 直排按钮一致（此为本文件记录的假设，见 §12）。三者并存是 AI 稿的明确画面。
- 状态与日期已经由上级章节表达，不在无提醒卡片内重复；已完成正文降低到 `MutedBrush` 并保留删除线。
- 便签页状态区右侧保留 `按日期分组` 下拉，当前只有 `按日期分组` 一个选项，属于占位单选项，不引入新的分组维度。
- 状态区分组标题卡使用 `NoteBrush` 暖黄底，左侧 `PeachRailBrush` 圆点，`M月d日 · 周X` 标题与右侧 `N 条` 数量加展开箭头。

### 悬浮窗底部命令区

便签工作区的底部命令区固定为：

- 居中主按钮 `＋ 添加便签`：`PinkAccentStrongBrush` 实心胶囊、白字、左侧白色加号圆圈图标。
- 其右侧次级按钮 `设置`：`PanelBrush` 底、`PeachLineBrush` 边、齿轮图标加文字，点击打开主窗口。
- 搜索按钮（可访问名称“搜索便签”，仅便签工作区可见）常驻该命令区，不随快速新增表单收起而隐藏；其形态、三态搜索槽与 Tab 契约见 §10「便签：pastel 分格索引」。
- 内嵌快速新增表单默认收起，从列表底部移除；点击 `＋ 添加便签` 后**原地展开**该表单，再次点击或提交后收起。展开时不改变列表滚动偏移。展开后表单与底部命令行作为整体收进限高 170 的命令区滚动容器 `FloatingNotesCommandScroller`（滚动热区不越出右侧安全区），内容超高时在该容器内滚动；收起态内容低于限高，布局与既有基线一致。
- 表单保留正文、日期、标签、添加按钮以及紧凑“提醒规则”入口；复杂规则表单只在共享次级窗口展开。

### 提醒中心的条目与处置

提醒中心是单一滚动列表，不再嵌套外层卡片。每项稳定显示“便签”或“倒计时”来源、来源正文/名称和原计划北京时间；两类都提供“知道了 / 稍后提醒 / 打开来源”，只有便签提供“完成便签”，**任何条目都不提供删除**。稍后提醒使用选项菜单承载 10、30、60 分钟、明天 09:00 和自定义模式；仅自定义模式展开日期、小时和分钟控件，无效未来时间的错误紧邻这些来源控件。真实渲染和送达写盘完成前禁用处置，失败信息在条目内可见，但**打开来源始终可用**；Tab 导航必须覆盖该来源实际存在的动作和自定义控件，不能因失败态或长正文跳出当前条目。来源标签使用主文字 token（`InkBrush`），强调层级由半粗字重与章节字号承担。提醒中心与规则编辑器可从窗口头部拖动；提醒中心首次显示贴齐工作区右上，之后保留手动位置。

“全部已读”是对当前全部条目逐条执行“知道了”的批量封装，领域语义与逐条处置完全一致：批量写入处置 key，任一条失败则整体保留原条目并按既有退避节奏重试，不引入新的条目状态字段。“待处理”徽章是纯视觉标识。

### 次级窗口与共享控件

次级提醒规则编辑器沿用 `CanvasBrush`、`NoteBrush`、`PeachLineBrush`、`InkBrush`、`MutedBrush` 和 `DangerBrush`，取消时不改变主表单草稿。日历模式使用频率、间隔、开始日期、本地时刻和类型化选择器：周选择星期，月选择日期或最后一天，年选择月份及日期或最后一天；无效间隔与选择器分别归位到对应控件；所有日历控件保持连续 Tab 顺序；不显示自由格式 RRULE、例外日期、次数或截止日期。主题化日历的翻月按钮可访问名称固定为「上一个月」和「下一个月」。

便签编辑区只显示“管理提醒规则”命令和规则数量摘要；倒计时编辑区改用「提醒规则单选组」（见 §4），不再提供独立的“管理提醒规则”入口，规则数量摘要也不显示。绝对一次、固定间隔、日历重复、新增、编辑、复制与删除集中在共享次级窗口；由倒计时进入时额外显示“倒计时相对”和提前分钟输入，错误紧邻该输入。

设置恢复与激活失败这类可能先于任何窗口初始化出现的系统对话框，主按钮使用 `MainActionBrush` 与既有主按钮文字色 `#FFFDFC`，采用静态画刷，不绑定动态主题资源。

可访问名称必须包含动作、状态和对象；分段控件与状态标题的可见文本不随动作改变，辅助技术名称可以准确说出“未完成/已完成”和数量。

### 倒计时紧凑输入

主窗口倒计时编辑区与设置区的文本框、日期框和下拉框统一为设计坐标 `56px` 高，字段标签为设计坐标 `18px`，内部横向留白为设计坐标 `16px`（本节数值均为设计坐标，不与 §3 渲染 DIP 混用）。名称和日期继续按内容职责分配宽度，时、分、秒保持可并排比较；相邻的标签选择器、保存与取消动作与输入行同高，不制造额外视觉台阶。

输入控件只使用 §2 的角色：`PanelBrush` 作为表面、`PeachLineBrush` 作为常态边界、`InkBrush` 作为文字、`MutedBrush` 作为图标与占位文。悬停与按压反馈使用 `ActiveBrush`；键盘焦点使用 `ActiveBrush` 边界，不依赖默认系统蓝。时、分、秒下拉点击文字区域即可打开，命中规则与日期选择器一致。编辑区顶部到列表底部使用连续的不透明 `BackgroundBrush` 底层，编辑区与设置区再叠加各自边界，保证真实 WPF 控件及区块间距都不依赖装饰 raster 中历史控件位置；不得引入默认系统蓝、独立灰阶或新的输入框专属色板。

### 基础控件状态契约（#133）

App.xaml 控件模板段对基础 WPF 控件提供统一状态语言，全部引用 §13 token：

| 状态 | 统一口径 | 说明 |
| --- | --- | --- |
| 常态 | 表面按控件既有角色（`CanvasBrush` / 透明 / 分段选中态） | 1px `PeachLineBrush` 边或无边 |
| 悬停 | 表面换 `CardBrush` 系画刷或边换 `ActiveBrush` | 立即生效（见下方反馈节奏） |
| 按压 | 边换 `ActiveBrush` + `FocusBorderThickness`（2px），立即反馈 | 与键盘焦点同语言（§6） |
| 键盘焦点 | `ActiveBrush` 边 + `FocusBorderThickness`；主操作按钮与置顶开关用 `MainActionFocusBrush`（§6 唯一焦点例外） | 不得依赖鼠标悬停、不得只在特定事件隐藏 |
| 禁用 | 整体 `Opacity` 0.45 | 全控件统一取值 |

- 覆盖范围：`Button`（含 Primary/Secondary/FloatingAccent/Danger/IconButton/CalendarNavigationButton）、`ToggleButton`（SegmentButton 族、FloatingHeaderSegmentButton、FilterOptionButton、TagToggleButton）、`CheckBox`、`RadioButton`（#133 新增隐式样式，视觉语言按 §4 提醒规则单选组；未显式套样的单选钮不再落系统默认外观）、`ComboBox`（展开态边 `ActiveBrush`）、`DatePicker`（悬停/展开/键盘焦点/禁用态；焦点环用 `FocusBorderThickness`）、`TextBox`、`ToolTip`（非交互控件，只承担主题化常态，无悬停/按压/焦点/禁用态）。Calendar 系列维持既有 token 化状态。
- 反馈节奏：沿用 §9「高频切换默认即时完成」——现有按压、悬停与焦点反馈立即生效；本契约未引入过渡动画，#131 规格中的 hover 150–200ms 过渡与 §9 的 160ms 上限及「沿用现有反馈」的裁决冲突，且 `PrimaryButton` 悬停即时换底被验证合同（`ValidationReport.ValidatePrimaryButtonHoverTextContrast` 的 `Surface.Background` setter 检查）钉死，故暂以即时反馈收口；未来加过渡时只允许可中断 `transform` / `opacity`、上限 160ms，并按系统 ReduceMotion（`SystemParameters.ClientAreaAnimation`）提供等价即时行为。
- MainWindow 本地样式（`MainSegmentButton`、`MainNotesSegmentButton`、`MainReminderModeRadio`、`MainTopmostCheck`、`MainNoteCompletionCheck`、`MainReminderHelpButton`、`MainNoteDateDisclosureButton`）沿用窗口内模板：#133 仅对其中两处激活页签文字执行 §13.4 换绑；其各自的按压/禁用态缺口不在 #133 载体范围内，列为遗留。
- 图标按钮必须具备 `AutomationProperties.Name`；动态列表行、标签芯片与分段按钮通过既有 `UiAccessibility`/`NameControl` 命名并经 `ToggleButton`/`CheckBox` 的 Toggle 语义暴露选中状态（MainWindow.xaml.cs / FloatingWindow*.cs 既有接线，#133 复核无缺口）。

## 5. 布局原则

- 先状态、后日期、再便签；未完成与已完成是两个平行章节，不使用一个全局布尔值联动。分段切换控件只改变哪一个章节可见。
- 状态章节之间用 10px 纵向节奏；日期章节之间用 12px；轨道内容左侧保留 30px 稳定槽位。
- 主窗口页面自上而下的空间关系分别对齐各自 AI 参考图；标题区、标签栏、装饰带和内容区块的顺序以对应页面目标图为准，不强行把两个页面压成同一套排布。底部状态条仍保持在页面下缘。
- 状态标题带与日期轨道不做外层卡片嵌套，避免“卡片套卡片”；真正需要层级的只有便签卡。
- 内容区块之间使用 `12px` 纵向节奏，区块内字段行之间使用 `10px`。
- 排序、筛选、持久化、默认展开状态和刷新恢复均保持原契约。

### 主窗口工作台骨架（#135）

主窗口业务内容由四段自上而下的工作台结构承载，视觉优先级固定为：当前任务/剩余时间 → 主要动作 → 次要管理 → 装饰：

| 区段 | 载体 | 内容 |
| --- | --- | --- |
| 顶部身份区 | `MainIdentityZone`（Grid） | 标题、副标题、窗口置顶、最小化/关闭 |
| 命令栏 | `MainCommandBarZone`（Grid） | 倒计时/便签页签、筛选、标签、数量 |
| 当前工作区主卡 + 列表/管理区 | `MainWorkspaceZone` 内 `CountdownWorkspace` / `NotesWorkspace` | 编辑主卡（Grid 承载字段行，时/分/秒用 UniformGrid）、偏好卡、列表 ScrollViewer |
| 底部动作区 | `MainStatusBarZone`（Grid） | 北京时间与状态行 |

- 业务内容一律由 Grid、StackPanel、UniformGrid 和 ScrollViewer 承载并按行/列排布；字段行使用「标签 + 输入」的堆叠结构，文本容器不设固定高度，窗口放大、DPI 变化和文字放大时随行高增长，不得裁切主要标题、输入和动作。
- Canvas 只保留两类：装饰层（`MainReferenceDecorationLayer`、`CharacterBackgroundLayer`、`CharacterPanelLayer` 及全部手写体装饰落点）和不参与业务布局测量的锚点帧（`MainLayoutSurface` 设计坐标锚点、`MainContentSurface` 投影锚点、`CountdownWorkspace` / `NotesWorkspace` 工作区锚点）。锚点 Canvas 不再用 `Canvas.Left`/`Canvas.Top` 摆放任何业务控件，其内部一律是单一铺满的 Grid 骨架；两个工作区锚点的 Canvas 类型被 `ValidationReport` 工作区切换与空态对比合同引用，不得改型。
- 每个页面保留各自的装饰带与手写体文案落点；装饰层必须 `IsHitTestVisible=False`、不参与命中（§4、§13.5）。#136 便签页试点在该骨架上完成最终视觉，不重排区段顺序。

暖粉日间的结构基因：

1. 使用 1px 的 whisper 边界，映射为 `PeachLineBrush` / `NoteLineBrush`，让分隔线只承担结构职责。
2. 使用 `8px` 卡片刻度与 `4px` 功能控件刻度；分段胶囊、数字色块、按钮胶囊使用全圆角（短边一半），卡片使用 `8px` 圆角。
3. 使用 `8px` 基准间距与暖中性色层级；状态章节 10px、日期章节 12px、卡间 8px 都从该节奏派生。
4. 采用低存在感的多层暖色深度：不堆灰阴影，优先用 1px 边界、背景角色和留白表达层级；主行动按钮允许一层极轻的暖色投影以表达可点击性。

## 6. 深度层级

页面画布为 Level 0；内容区块为 Level 1（`CardBrush` 表面 + 1px `PeachLineBrush`）；便签卡与提醒条目卡为 Level 2（棕黄或白纸底 + 1px 边界）。不使用外发光、霓虹阴影或浮动大卡片。常规焦点使用 `ActiveBrush` 边界，不能依赖默认系统蓝。主操作按钮与置顶开关共用同一暖粉焦点资源，这是全应用唯一的焦点例外。按压与悬停反馈仍使用既有 `ActiveBrush` 语义。

## 7. Do's / Don'ts

### Do

- 固定写“未完成 / 已完成”，用分段控件表达当前视图、用箭头表达收放状态。
- 每个日期独立收放，并在刷新后保持其状态。
- 用数字色块分格和数量徽章帮助扫描，而不是装饰。
- 让手写体文案只出现在固定装饰层，永不可点击、永不遮挡控件。
- 只在单一日间色板内选色；粉色与 pastel 只做强调、分格与小型芯片，不做大面积背景。

### Don't

- 不把“收起未完成”或“展开 2026-07-31”写进可见标题。
- 不把状态和日期合成一个折叠开关。
- 不恢复默认 WPF 控件外观，不改动悬浮窗 520 x 390 视觉基线（基线定义与帧-场景映射见 §10、§11）。
- 不引入夜间色板、自动/夜间模式或主题切换入口。
- 不使用大面积阴影、冷蓝荧光或霓虹描边；pastel 粉/蓝/绿/紫只用于小型分格与芯片。

## 8. 响应式行为

主窗口继续使用现有 `Viewbox` 缩放；默认与最小可支持尺寸统一为 `760 x 880`，不再把缩放后的更小窗口视为支持范围。分段控件采用三列稳定结构（强调轨、文字、数量徽章）；文字列可压缩，徽章不被挤压。日期轨道在该支持尺寸内保持 1px 线和 30px 槽位，便签正文换行，操作区不换行。必须检查标题、数量、辅助文字、数字色块和长便签没有裁切或重叠。

## 9. Motion 哲学

高频切换与收放默认即时完成：可见内容、胶囊位置和箭头状态同步更新，不播放阻塞式展开动画。按钮沿用现有按下、悬停和焦点反馈；若未来加入过渡，只允许可中断的 `transform` / `opacity`，上限 160ms，并提供等价的 reduced-motion 行为。键盘操作不增加额外动画。

## 10. 悬浮窗双工作区附录

本节只约束现役 `520 x 390` 悬浮窗。外壳、标题栏 raster、三只玩偶及其锚点属于不可变基线：标题栏固定为 `x=51, y=72, w=422, h=63`；动态内容起点以现役行距为准（约 `x=60, y=147 ± 2`）。`x=396` 是便签工作区卡片、日期印记、快速新增控件、滚动条以及倒计时**文本内容**的正文右边界；倒计时装饰背景、强调轨和规则线由右侧 `54px` 角色安全区约束，允许其底面延伸到文本列右侧，但不得命中测试、不得覆盖倒计时文本。标题栏命令不受该正文安全区约束。玩偶层和装饰层必须 `IsHitTestVisible=False`；拖拽、工作区切换、托盘、管理窗口、退出、滚动、完成勾选、日期、提醒、标签和添加动作保持原命令与顺序。

标题栏中间命令固定为房子（打开管理窗口）：以 AI 便签稿为准；AI 倒计时稿中画出的最大化方块为误画，不采用。

首次显示必须让倒计时导航处于选中态并与实际工作区一致；之后每次切换始终恰有一个工作区选中。工作区、标题、输入区和正文必须通过同一切换入口同步，不保留第二套初始化状态。

默认贴齐工作区右下并留 `12 DIP` 边距；用户拖动后保留手动位置，重锚定时只夹入工作区。搜索击键、筛选和内容 `Refresh` 不得改 `Left`/`Top`。只在 `SizeChanged`、`Loaded`、窗口变为可见、以及拖拽结束后重锚定。

### 倒计时：暖粉分格

- 列表第一项始终是唯一主项，使用 `PinkTintSoftBrush` 底、`FloatingDayPrimaryCountdownAccentBrush` 粉红窄强调轨和 §4 的数字色块分格；“距离下班”在无筛选约束时存在则固定排在首行并完整继承主项的字体、强调色、色块和效果，筛选激活时不参与悬浮窗列表，**距离下班不存在时，第一个普通倒计时仍是唯一视觉主项**。主项标题下方的截止行只显示 `M月d日 HH:mm`，不追加“北京时间”标签；无论列表是否出现滚动条，截止时间都必须完整显示。主项标题最多两行；主项同行右侧可显示一枚粉色心形装饰，属装饰层。
- 主项标题右侧紧跟 `超期待！` 徽章，规则见 §4。
- 后续项目改为单列规则线，`CardAltBrush` 底、`InkBrush` 数字色块（`SecondaryBlockBrush`），不再重复独立卡片阴影；标题保持单行省略。
- 主项与次项的剩余时间都使用固定 `194px` 列右对齐，必须单行显示且不得省略。
- 动态内容右侧至少保留 `54px` 角色安全区，装饰背景、强调轨和规则线都不参与命中测试。
- 底部区域：月薪大于 0 时显示今日已赚金额；月薪等于 0 时显示 §4 的手写体鼓励文案。两个工作区共用同一规则。
- `520 x 390` 倒计时表面不增加提醒规则控件；现有标题栏“管理”入口继续打开主窗口，用户倒计时的规则入口位于主窗口编辑表单。“距离下班”仍无编辑入口或提醒动作。
- 主倒计时数字与粉红强调轨共用 §2 的 `PrimaryCountdownNumberBrush` 与 `FloatingDayPrimaryCountdownAccentBrush`；两者不得复用玩偶阴影资源，实际合成背景上的文字对比度不得低于 `4.5:1`。
- 只使用 §2 的暖粉日间角色。角色源图、尺寸、锚点和 alpha 边界保持不变。

### 便签：pastel 分格索引

- 每条便签是固定宽度 `319px` 的规则线纸面，该宽度**已内含**右侧 `48px` 日期印记列，不得再把 `58+319+48` 相加去核对 `x=396`。卡片必须完整收在滚动内容视口内；卡片、日期印记和滚动热区最右都不得超过 `x=396`。
- 右侧 `48px` 日期印记按条目 pastel 轮换：粉 `#FEEFF1`（数字 `#D41F57`）→ 蓝 `#EEF5FE`（数字 `#2F86DB`）→ 绿 `#EDF6E8`（数字 `#318939`），按列表顺序循环。印记显示 `M月`、`dd`、`周X` 三行，只有 `dd` 是当日数字并与印记同色。
- 便签卡底也参与轮换：暖黄 `NoteBrush` 与白纸 `NoteWhiteBrush` 交替，形成条带节奏。
- 日期印记只帮助扫读，不是侧栏、筛选器或新导航；它不可点击、不可聚焦，也不能覆盖复选框、正文、提醒或 `…` 菜单。
- 每条便签右侧独立 `…` 菜单，提供 `编辑` / `删除`，复用主窗口编辑流程。
- 正文和提醒允许自然换行；列表与便签正文继续只使用既有 `FloatingBodyScroller`，不新增内部滚动容器。快速新增输入框允许内部滚动；快速新增展开态底部命令区的整体滚动容器（`FloatingNotesCommandScroller`）同为该条例外。
- 悬浮便签正文固定为 `13 DIP` 字号和 `18 DIP` 行高；提醒摘要固定为 `12 DIP` 字号和 `17 DIP` 行高，均使用显式 `BlockLineHeight`，不得继承主窗口的正文排版尺寸。
- 快速新增区使用便签黄纸面、`6px` 圆角和珊瑚短标，保留正文、日期、标签、添加、搜索按钮以及紧凑“提醒规则”入口；可见性与展开行为见 §4「悬浮窗底部命令区」。
- 搜索按钮位于底部命令区，可访问名称“搜索便签”，仅便签工作区可见；可见内容是闭合圆环加右下手柄的放大镜，不得渲染成残缺 C 或左括号，也不得被按钮内边距裁切。列表上方槽位三态互斥：无词且未展开时无任何行；收起且有词时显示“搜索:{词} ✕”（点词展开、点 ✕ 清除）；展开时显示输入行（MaxLength=140，占位“搜索便签”），列表视口变矮、底部命令区位置不动。Esc 清空并收起；Ctrl+F 在任意工作区切到便签、展开并聚焦。无匹配时列表区显示“没有匹配的便签。”与“清除搜索”，底部命令区保持可用。
- Tab 顺序：搜索按钮在 `＋ 添加便签` 之后；输入框展开时位于列表首卡复选框之前，收起时移出链。视觉顺序与该 Tab 合同一致。
- 底部命令区的 `DatePicker`、`ComboBox`、标签选择器、复选框、Popup、`ScrollViewer` 和 `ScrollBar` 必须从悬浮窗局部资源解析日间主题，不得回退应用级旧主题或默认 WPF 样式。
- 快速新增区的输入背景、输入文字、提醒标签和错误文字必须分别绑定局部 `NoteInputBrushKey`、`NoteInputTextBrushKey` 和 `ErrorBrushKey`；窗口级资源可覆盖同名应用资源，但不得改变这些输入/错误语义 token 的局部归属。
- 完成复选框继续通过 `Tag=note.Id` 提交；失败时取消勾选并显示既有错误信息。

### 局部滚动条

- 悬浮正文滚动条使用局部样式，不改变主窗口滚动条；垂直滚动条保留 `10px` 命中热区，常态、悬停和拖动状态的可见 thumb 均为 `4px`，只通过不透明度渐进增强视觉反馈。
- 滚动条、卡片和日期印记最右不得超过 `x=396`，thumb 在任一状态都不能覆盖正文。
- 鼠标滚轮与真实 thumb 拖动必须改变同一个 `FloatingBodyScroller` 的偏移；便签列表与便签卡正文不得新增内部滚动容器，快速新增输入框内部滚动与快速新增展开态底部命令区整体滚动（`FloatingNotesCommandScroller`）除外。

### 小尺寸命中区合同（#143）

`520 x 390` 外壳内全部快速操作的命中区按 `36px` 下限执行，相邻命令按钮横向间距 `8px`：

- 头部：托盘/管理/退出图标按钮 `36 x 36`；倒计时/便签页签命中高度 `36`。头部三枚图标按钮之间保持既有紧凑 `3px` 间距例外：目标已达 `36 x 36` 尺寸口径（WCAG 2.5.8 尺寸口径达标），加宽间距会挤压标题列实测可用宽度（现仅约 `3px` 余量）造成标题裁切。
- 便签卡：完成复选框命中槽位 `36px`（仍不覆盖日期印记）；右侧 `…` 菜单触发器 `36 x 36`，该两列加宽由正文列（固定 `319px` 卡宽不变）吸收。滚动条 `10px` 命中热区维持 §10「局部滚动条」的例外口径。
- 底部命令区与搜索槽：`＋ 添加便签` / `搜索便签` / `设置` 保持 `38px` 命中高度，搜索输入行与提示行（词钮、清除钮）`36px` 且两态等高；`便签→托盘`、`搜索→设置`、`词钮→清除钮` 等相邻命令间距 `8px`。
- 几何值由 `Countdown.CSharp.Tests.ps1` 的 #143 源码契约断言钉住；改动上述任一数值须同步该断言块。

### 验收边界

必须检查空态、极长标题、长剩余时间、长便签、提醒规则入口与摘要、次级编辑器、Popup、滚动到底部，以及 `＋ 添加便签` 展开态与 `…` 菜单。任何视觉变化都不得移动角色锚点、扩大窗口、遮挡现有热区或引入默认 WPF、冷蓝荧光、霓虹样式。

多文件发布下 `executable.sha256` 只绑定 apphost `app/MinminCountdown.exe`，钉不住 `MinminCountdown.dll`；像素语义以本版 PNG 为准，不改该弱绑定算法。主窗口与本节共用这一条口径，不再各写一套。

现役四帧与场景对应（v22 基线 `assets/ui-references/floating-window/520x390/2026-09-11-v22/manifest.json`，实际路径与哈希以该 manifest 为唯一依据）：

| 帧 | 证明的场景 |
| --- | --- |
| `floating-countdowns-day` | 倒计时工作区：距离下班主项 + 一条次级倒计时、数字色块分格、`超期待！` 徽章、粉红主项底、角色安全区、底部已赚/鼓励文案 |
| `long-notes-day-top` | 长便签列表顶部：pastel 分格索引、319px 卡（内含 48px 印记）、正文 13/18 与提醒 12/17、`…` 菜单 |
| `long-notes-day-bottom` | 长便签列表底部：外层滚动可达、无卡片内滚动容器、底部命令区与 `设置` 按钮常驻 |
| `notes-search-expanded-day` | 便签搜索展开态：搜索槽右缘 x=396、`＋ 添加便签` 位置不动 |

## 11. 元素与状态清单

本节是主窗口与提醒中心的可见元素清单，供实现与验收逐项核对。

主窗口倒计时页：窗口标题 `敏敏子的时光小屋` 与副标题 `重要时刻和今日努力，都在这里慢慢长大`；右上 `日间` 静态标识与 `窗口置顶` 开关（不提供主题切换）；标签栏（`倒计时` 激活、`便签`、`筛选`、`标签`、`N 个倒计时`）；装饰带；`新增倒计时` 区块（名称、截止日期、时/分/秒、设置标签、提醒规则单选组、`＋ 添加倒计时` 主按钮、右侧手写体文案 `每一个期待，都值得被认真对待 ♡`）；`显示设置` 区块（显示精度、月薪（元）、上班时间、下班时间、`保存设置` 按钮）；倒计时列表条目（标题、`N 天 N 小时 N 分钟 N 秒`、`编辑`、`删除`）；底部状态条（`当前北京时间` + 时间戳 + 静态 `当前主题：日间`）；右下角手写体文案 `生活很忙，但别忘了可爱 ♡`。

主窗口便签页：头部同倒计时页；标签栏激活段为 `便签`，右侧显示 `N 个待办`；装饰带（含手写体 `今天也要加油呀！♡`）；新增便签区块（便签内容、便签日期、`＋ 添加`、提醒规则、标签、搜索便签）；状态区块（并排分段控件 `未完成 N` / `已完成 N`、`按日期分组` 下拉、日期分组标题、便签卡与 `编辑` / `删除` / `…`）；底部状态条。

提醒中心：标题 `提醒中心` 与 `N 条`；`全部已读` 按钮；条目卡（来源图标块、来源标签 `便签` 或 `倒计时`、来源正文或名称、`计划：YYYY年M月d日 HH:mm（北京时间）`、`待处理` 徽章、分隔线、两行按钮）。

### 方向参考图身份

5 张 AI 优化稿是本文件的**视觉构图目标图**：留在 `assets/ui-references/` 根级（`main-window-ai-optimized-20260910.png`、`main-window-notes-ai-optimized-20260910.png`、`floating-window-countdowns-ai-optimized-20260910.png`、`floating-window-notes-ai-optimized-20260910.png`、`reminder-center-ai-optimized-20260910.png`），**不进入任何 manifest、不作为像素验收基线、不参与哈希或像素门禁**。AI 图决定第一眼识别所需的可见几何和视觉关系；真实应用截图负责验证实现没有回退。

本文件色板取自这 5 张图的像素采样：对目标区域在去除文本与描边后取众数色，抗锯齿边缘像素不参与取值。采样值用于选定方向，不作为像素门禁的期望值。

### 视觉基线演进口径

“视觉基线”指 manifest 钉死的真实 WPF 渲染截图集合，是视觉变化的唯一验收依据。演进口径如下：

当前状态（2026-09-17，#146 收口重拍）：v22 已于 2026-09-17 按 #131 线终态（#135/#136/#138/#139/#140/#143 及 #146 收口修正之后的工作台骨架、便签试点、倒计时页重构、悬浮窗命中区几何与滚动条 Focusable 语义）原目录重拍，主窗口 8 帧 + 悬浮窗 4 帧与双 manifest 均在库；manifest 的 `sourceBinding`（主窗 15 个、悬浮窗 9 个视觉源文件）与工作树逐字节一致，主窗聚合 SHA-256 为 `4E256D3988D44CD883BCC1AFB07B6C83504F71352BCAD8246400D5B96DBC12E2`，悬浮窗聚合为 `775A78672BEE050AF83A0B8A8FC25C15A8FE55A66230BF03716C35A4367FEFCC`。2026-09-17 源码树 validate-only run 与六阶段并行套件 run（20260917T153158，全绿）中主窗口十六项基线门禁与悬浮窗基线门禁全部通过，v22 自此继续担任现役验收基线，v21 及更早目录仅归档自指（现役代码与脚本零引用）。历史帧归档：2026-09-14 版于 `run-evidence\clock-countdown-issue-146-final-closure\v22-prev-20260914\`；2026-09-17 第一/二次重拍帧与最终版逐字节相同（12/12 PNG SHA 一致，`...-152513\` 归档即最终帧副本）。注意：显示器链路休眠的无人值守时段（`GetDeviceGammaRamp` 失败）会让 DWrite 灰阶参数漂移，导致基线像素门禁误红且捕获整幅透明 flake 同族出现；视觉复验必须在显示器唤醒的会话时段运行。

1. 五个界面按本文件实现完成后，重拍纯日间 **v22** 真实渲染系列替换 v21 成为新基线；v22 只包含日间帧，不再有 Night 帧。
2. v22 主窗口帧规模 8 帧（基线 `assets/ui-references/main-window/1165x1350/2026-09-11-v22/manifest.json`）：`main-day-countdowns-empty / -representative / -stress` 与 `main-day-notes-empty / -representative / -stress` 共 6 帧（无主题维度），加常设便签编辑态 `main-day-notes-edit` 与筛选弹层打开态 `main-day-notes-filter-open` 2 帧。若某帧无法稳定复现，必须在 v22 manifest 说明并在 Testing Decisions 记录，不得静默减少。
3. v22 悬浮窗帧规模 4 帧：`floating-countdowns-day`、`long-notes-day-top`、`long-notes-day-bottom`、`notes-search-expanded-day`。
4. v21 目录作为归档保留（`assets/ui-references/main-window/1165x1350/2026-09-07-v21/` 与 `assets/ui-references/floating-window/520x390/2026-09-07-v21/`；目录名保持仓库既有形式），**解除引用**：不再出现在任何 manifest 的现役路径、不再被门禁脚本或测试读取。归档目录本身不被删除，以保留历史比对能力。
5. 旧设置中的 `Auto` / `Night` 偏好一律按日间解释，不报错、不丢数据、不回写强制修改。

## 12. 假设与已裁决项

以下条目由本文件明确记录，实现时不再另起讨论：

- **已裁决**：主窗口倒计时页和便签页分别对齐各自 AI 图的标题区、标签栏、装饰带和内容区块顺序，不强行复用同一套页面排布。
- **已裁决**：悬浮窗标题栏中间按钮以便签稿的“房子”为准（AI 倒计时稿的最大化方块为误画）。
- **已裁决**：520 x 390 外壳、三只玩偶锚点、标题栏三命令（托盘/打开管理窗口/退出）不变。
- **已裁决**：月薪 > 0 显示今日已赚、= 0 显示手写体鼓励语，两个工作区统一。
- **已裁决**：悬浮窗工作区切换的激活分段胶囊统一为便签稿的实心粉底 `PinkAccentStrongBrush` + 白字；倒计时稿画出的浅粉胶囊底为稿间差异，不采用。
- **已裁决**：AI 图中的 `日间` 主题标识与底部状态条 `当前主题：日间` 文字保留为静态展示；主题仍固定为单一日间，不新增主题切换入口。
- **假设**：主窗口便签行 `…` 菜单内容与既有 `编辑` / `删除` 直排按钮一致（AI 稿三者并存）。
- **假设**：倒计时页和便签页分别按各自 AI 图裁剪装饰带高度和锚点；两页可以共用同一份装饰 raster，但不得因此改变目标图中的页面顺序。
- **假设**：`完成便签` 在第二行左对齐单独成行或与 `打开来源` 并排取决于该来源动作数量，二者都满足本文件契约；实现选择以不拉伸填满整行为准。
- **假设**：主项同行右侧的心形装饰属装饰层，不参与命中测试，可随实现取舍。
- **假设**：便签工作区快速新增表单移到底部命令区后，Tab 合同保持既有语义：搜索按钮仍在“添加”按钮之后（该按钮现为底部 `＋ 添加便签`），输入框展开时仍位于列表首卡复选框之前、收起时移出链；仅“添加”按钮的位置随布局下移，不引入新的焦点顺序。
- **假设**：§2 的 token 名为本文件新引入的语义角色；实现可沿用既有应用级 token（`MainActionBrush`、`NoteBrush`、`CardBrush`、`CanvasBrush`、`BackgroundBrush`、`PeachLineBrush`、`InkBrush`、`BodyBrush`、`MutedBrush`、`ActiveBrush`、`DangerBrush`、`SuccessBrush`、`ExpiredBrush`、`TagChipTextBrush`、`WorkspacePanelBrush`、`WorkspacePanelLineBrush`）承载同义角色，新增角色按 §2 命名并同步到主题资源与验证报告。
- **未覆盖**：hover、键盘焦点、禁用态、其他断点的具体像素值不在 AI 稿画面内，本文件只给出角色 token 与层级规则，不给具体像素。

## 13. Token 分层与可读性基线

本节是暖粉日间 token 的实现基线（issue #132）：三层资源模型、排版/间距/圆角/焦点 token、对比度门槛与装饰限制。实现权威源是 `src/MinminCountdown/App.xaml` 与 `src/MinminCountdown/ApplicationTheme.cs`（两者逐键同步）；本节与 §2 的采样色值表冲突时，以实现文件为准并在本节记录差异。

### 13.1 三层 token 模型

| 层 | 资源形态 | 位置 | 规则 |
| --- | --- | --- | --- |
| Primitive 原色 | `*Color`（`<Color x:Key>`） | App.xaml 第 1 层 + `ApplicationTheme.Tokens` | 全部色值的唯一来源；新增颜色必须先落原色 |
| Semantic 语义角色 | `*Brush`（`SolidColorBrush`） | App.xaml 第 2 层 | 每支画刷绑定一个原色、只承载一个语义角色；控件只引用语义画刷 |
| Component 组件 | 悬浮窗画刷、控件样式、排版/间距/圆角/焦点 token | App.xaml 第 3 层 | 色值一律绑回 Primitive（`Color="{DynamicResource *Color}"`），不得自持字面色值 |

- 控件不得新增同义色值：新视觉需求先找既有 Semantic 角色；确需新角色时新增 Primitive + Semantic 两层，并同步 `ApplicationTheme.Tokens`。
- 允许存在的值级重复只有以下 alias（同一原色值挂多个语义角色），不得扩充：`#B84A36` → `ActiveHover` / `MainActionHover`；`#3B190D` → `Ink` / `TagChipText` / `SegmentSelectedForeground`；`#FFFDFC` → `MainActionText` / `PinkBadgeText`；`#FEEFE1` → `SegmentSelected` / `PeachTint`；`#CD623E` → `NoteActiveRail` / `PeachRail`；`#FFF7E8` → `Tag` / `CharacterPanel`；`#F6DFD3` → `NoteTimeline` / `PeachLine`（#146 收口补录：该值级重复在 #131 线工作树演化中已存在（App.xaml 与 `ApplicationTheme.Tokens` 两处同步），清单此前漏记，本条只补记录、不新增重复）。
- XAML 允许 6 位与 8 位十六进制写法并存（如 `#FEE0E4` 与 `#FFFEE0E4`），同步校验按解析后 ARGB 相等判断，不强制统一记法。

### 13.2 排版层级 token

| token | 值（设计坐标 px） | 规格 band | 对应现状 |
| --- | --- | --- | --- |
| `TypeDisplayFontSize` | 44 | 40–48 | 页面主标题层（待界面节点消费） |
| `TypeH1FontSize` | 30 | 28–32 | 章节大标题层（待界面节点消费） |
| `TypeH2FontSize` | 22 | 20–24 | 区块标题、列表条目标题（§3 现状 22px） |
| `TypeBodyFontSize` | 16 | 15–17 | 辅助/摘要 16px、徽章 15px、装饰 17px |
| `TypeCaptionFontSize` | 13 | 13–14 | 悬浮窗便签正文（§10 现状 13 DIP） |

- band 来自 #131 规格；每层取 band 内与现状最接近的值。主窗口经 Viewbox（约 `0.652`）缩放后与 §3 渲染值一致，悬浮窗按 1:1 DIP 消费；两窗口复用同一组 token，按各自渲染口径选择层级。
- `12px` 以下不得承载关键状态；提醒摘要 `12 DIP` 是 §10 钉死的下限值，只承担辅助摘要，不单独承载唯一关键状态。
- 字族：`UiFontFamily`（`Microsoft YaHei UI` + 系统 fallback）承担全部 UI 文本；`HandwrittenDecoFontFamily` 只用于装饰（13.5）；数字保持 `Typography.NumeralAlignment=Tabular`；字重只用 Regular/SemiBold（§3）。

### 13.3 间距、圆角与焦点 token

| token | 值 | 口径 |
| --- | --- | --- |
| `SpacingUnit` | 8 | 8px 基准网格（§5） |
| `SpacingFieldRow` | 10 | 区块内字段行距（§5） |
| `SpacingBlockGap` | 12 | 区块间纵向节奏（§5） |
| `SpacingCardPadding` | 14 | 卡片内边距（§4 便签卡） |
| `RadiusPill` | 19 | 全圆角胶囊（短边 38 的一半） |
| `RadiusOverlay` | 12 | Popup / 日历浮层 |
| `RadiusControl` | 9 | 按钮、输入框、分段控件 |
| `RadiusCard` | 8 | 卡片圆角（§5 卡片刻度） |
| `RadiusChip` | 5 | 复选框、日历日、小芯片、ToolTip |
| `RadiusThumb` | 4 | 滚动条 thumb |
| `FocusBorderThickness` | 2 | 键盘焦点/按压边粗细（`Thickness` 资源，可直接绑定 `BorderThickness`） |

间距 token 以 `sys:Double` 网格值提供（可绑定 `FontSize`/`Height` 或在代码中组装）；XAML 中直接用于 `Margin` 时需由消费方包装为 `Thickness`。焦点语义沿用 §6：常规键盘焦点用 `ActiveBrush` 边界；主操作按钮与置顶开关用 `MainActionFocusBrush`，是全应用唯一焦点例外；不得引入默认系统蓝。App.xaml 内模板圆角与焦点边已全部改为引用上述资源。

### 13.4 对比度基线

口径：WCAG 2.x 相对亮度（sRGB 线性化，`0.2126/0.7152/0.0722` 加权），对比度 `(L1+0.05)/(L2+0.05)`，与 `ApplicationTheme.MeetsContrast` 同一公式。门槛：

- 信息性文本（正常字号）≥ `4.5:1`；
- 重要小字号（正文、便签正文等高频阅读文本）目标 ≥ `7:1`（目标，非门禁）；
- 非文本图形（焦点边、强调轨、复选图形）≥ `3:1`；
- 装饰（13.5）豁免，只记录实测值。

可复核计算：`python <run-evidence>\clock-countdown-issue-132-contrast\compute_contrast.py src\MinminCountdown\App.xaml`，脚本解析 App.xaml 资源值并输出下表（证据：`run-evidence\clock-countdown-issue-132-contrast\baseline-after.md`，修改前审计 `audit-before.md`）。

| 前景 token | 前景值 | 背景 | 对比度 | 门槛 | 类别 | 结论 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `InkColor` | `#3B190D` | 全浅色表面最差（`PinkTintBrush`） | 12.48 | 4.5 / 7.0 | text | PASS | 主文字；7:1 目标同样满足 |
| `BodyColor` | `#6B4A3C` | 全浅色表面最差（`PinkTintBrush`） | 6.22 | 4.5 | text | PASS | 正文 |
| `BodyColor` | `#6B4A3C` | `CanvasBrush` | 7.19 | 7.0 | text-7.0 | PASS | 正文 7:1 目标（对 `NoteBrush` 7.23） |
| `MutedColor` | `#82665F` | `CanvasBrush` | 4.78 | 4.5 | text | PASS | 辅助文字（对 `NoteBrush` 4.81） |
| `DangerColor` | `#B94D3A` | `CanvasBrush` | 4.59 | 4.5 | text | PASS | 删除/错误（对 `CardBrush` 4.91、`NoteBrush` 4.61） |
| `SuccessColor` | `#266B5E` | `CanvasBrush` | 5.74 | 4.5 | text | PASS | #132 由 `#529F8C`（2.87）调深 |
| `WarningAccentColor` | `#AD4A10` | `WarningTintBrush` | 4.86 | 4.5 | text | PASS | #132 由 `#DC6922`（3.00）调深 |
| `MainActionTextColor` | `#FFFDFC` | `MainActionBrush` | 4.84 | 4.5 | text | PASS | 主行动按钮（hover 5.09、pressed 5.34、悬浮粉按钮 4.95） |
| `PrimaryCountdownNumberColor` | `#C0102F` | `PinkBlockBrush` | 5.06 | 4.5 | text | PASS | 主项分格数字 |
| `InkColor` | `#3B190D` | `SecondaryBlockBrush` | 14.06 | 4.5 | text | PASS | 次项分格数字 |
| `StampPinkTextColor` | `#D41F57` | `StampPinkBrush` | 4.56 | 4.5 | text | PASS | 粉印记 |
| `StampBlueTextColor` | `#1D6BB0` | `StampBlueBrush` | 5.06 | 4.5 | text | PASS | #132 由 `#2F86DB`（3.44）调深 |
| `StampGreenTextColor` | `#26732D` | `StampGreenBrush` | 5.30 | 4.5 | text | PASS | #132 由 `#318939`（3.97）调深 |
| `FloatingDayPrimaryCountdownAccentColor` | `#EE2B46` | `PinkTintSoftBrush` | 3.74 | 3.0 | graphic | PASS | 主项强调轨（非文本）；分格文字由 `PrimaryCountdownNumberColor` 承担（5.06） |
| `ActiveColor` | `#E05A4A` | `PanelBrush` | 3.61 | 3.0 | graphic | PASS | 焦点边/复选图形；`PrimaryButton` 底已改绑 `MainActionBrush`（3.61→4.84） |
| `DecoTextColor` | `#D9B7B1` | `CanvasBrush` | 1.69 | 豁免 | decorative | 记录 | 手写装饰 |
| `DecoTextSoftColor` | `#E5AE9C` | `BackgroundBrush` | 1.73 | 豁免 | decorative | 记录 | 悬浮手写装饰 |
| `IconAccentColor` | `#F28995` | `CardBrush` | 2.34 | 豁免 | decorative | 记录 | 伴随 labeled 字段的装饰图标 |

本节点完成的修正（新值仍在 §2 采样方向内）：

- `SuccessColor` `#529F8C → #266B5E`；`WarningAccentColor` `#DC6922 → #AD4A10`；`StampBlueTextColor` `#2F86DB → #1D6BB0`；`StampGreenTextColor` `#318939 → #26732D`。
- `PrimaryButton` 底/边 `ActiveBrush → MainActionBrush`（白字对 `#E05A4A` 3.61 不达 4.5）；日历选中日与含选区月份底同步改绑 `MainActionBrush`。`ActiveBrush` 继续承担悬停、按压、焦点边与复选图形（≥3:1）。

#133 换绑收口（消费侧文字换绑，未新增/修改 token 值）：

- 主窗口激活页签文字：`PinkAccentBrush` → `InkBrush`（`PinkTintBrush` 底实测 12.5，达标）；便签页激活分段文字：`PeachRailBrush` → `InkBrush`（`PeachTintBrush` 底实测 14.0，达标）。`ValidationReport.HasSelectedSegmentContrast` 以 4.5:1 达标即过，换绑后保持绿。

遗留例外（4 对；相关 token 值被验证门禁钉死，载体文件属后续界面节点 ownership）：

| 位置 | 现状绑定 | 实测 | 修正要求 |
| --- | --- | --- | --- |
| 「超期待！」徽章文字（悬浮窗） | 白 on `PinkBadgeBrush` | 2.05 | 文字换绑 `InkBrush`（7.60） |
| 激活数量徽章文字（`MainWindow.xaml`） | 白 on `PeachBadgeBrush` | 1.79 | 文字换绑 `InkBrush`（8.69）；被 `ValidationReport.BadgeFollowsSegmentState` 以 `MainActionTextBrush` ReferenceEquals 钉死，须与该合同同节点换绑 |
| 未激活数量徽章文字（`MainWindow.xaml`） | `MutedBrush` on `PeachBadgeMutedBrush` | 4.05 | 文字换绑 `BodyBrush`（6.09）；同上被钉死为 `MutedBrush` |
| 已到期卡辅助文字（`MainWindow.xaml.cs`） | `MutedBrush` on `ExpiredBrush` | 4.14 | 换绑 `BodyBrush`（6.21） |

### 13.5 装饰文字的非信息性限制

- `DecoTextBrush`（1.69:1）、`DecoTextSoftBrush`（1.73:1）、`IconAccentBrush`（2.34:1）是装饰类 token：只能用于非信息性内容（手写体文案、情绪价值、伴随带标签字段的图标），不得承载状态、数值、操作指示或任何"不看别处就读不懂"的信息。
- 装饰层必须固定落点、`IsHitTestVisible=False`、不参与布局测量、不遮挡控件（§4）。
- 装饰类 token 不参与 13.4 对比度门禁；一旦某处装饰文字开始承载信息，必须改用满足 4.5:1 的语义 token。
- 审计事实：弱装饰文字实测约 1.65–1.73:1，只能以非信息性装饰保留。

### 13.6 同步与验证接线

- `ApplicationTheme.Tokens` 是 App.xaml 第 1 层的运行时权威副本，键与顺序一一对应；新增或修改颜色必须两处同步，校验按解析后 ARGB 比较。
- 修改 `App.xaml` / `ApplicationTheme.cs` 会使 v22 视觉基线 `sourceBinding` 失配：按 §11 口径重拍 v22 并更新双 manifest 后基线门禁才恢复。本节 token 变更应与后续界面节点的消费面修正（13.4 例外表）在同一次重拍中收口。

## Agent Prompt Guide

继续修改界面时，先读取本文件。保留暖粉日间的固定分段控件、数字色块分格、徽章、手写体装饰层和单一日间 token；优先使用 §2 的既有角色，不要创建夜间替代品或新的粉紫色板。token 分层、排版/间距/圆角/焦点与对比度基线以 §13 为准：新增颜色先落 Primitive 再挂 Semantic 角色，控件不得自持字面色值或新增同义色值。行为改动必须落到对应的 PowerShell / C# 验证入口并按 §11 的 v22 口径更新视觉基线。任何视觉改动完成后都要重新拍 v22 帧，并检查空态、长文本、日期分组、搜索展开态、`＋ 添加便签` 展开态、`…` 菜单、提醒中心两行按钮和底部已赚/鼓励文案切换。
