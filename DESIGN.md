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
| `SelectionBrush` | `#D4526A` | 文本选区高亮（独立语义色，#151）：按 `0.4` SelectionOpacity 合成在白纸输入面上肉眼可辨；不得复用 1px 结构线色 |
| `WorkspacePanelBrush` | `#00FFFDFC` | 主窗口装饰带以外的透明面板底（不遮挡装饰图） |
| `WorkspacePanelLineBrush` | `#00F6DFD3` | 同上，透明面板边界 |

### 强调与控件

| 角色 token | 值 | 用途 |
| --- | --- | --- |
| `MainActionBrush` | `#C23E68` | 主行动实心按钮（主窗口、悬浮窗和提醒中心“知道了”等）；与悬浮窗玫粉主按钮统一，白字对比度约 4.95:1 |
| `MainActionHoverBrush` | `#B0355D` | 主行动按钮悬停；白字对比度约 5.89:1 |
| `MainActionPressedBrush` | `#9B2D50` | 主行动按钮按压；白字对比度约 7.18:1 |
| `MainActionTextBrush` | `#FFFDFC` | 主行动按钮文字与图标 |
| `CoralActionBrush` | `#D68481` | 主窗口次级实心按钮（「保存设置」），比主行动更浅；文字用 `InkBrush` 主文字色（白字实测 2.77 不达 4.5，#151 禁止浅珊瑚配白字） |
| `PinkAccentBrush` | `#DE5863` | 主窗口顶部激活页签文字、粉色强调文字 |
| `PinkAccentStrongBrush` | `#C23E68` | 悬浮窗粉色实心主按钮（“＋ 添加便签”）与悬浮窗激活分段胶囊；与 `MainActionBrush` 共用玫粉主行动底 |
| `IconAccentBrush` | `#F28995` | 区块标题图标、粉色圆形加号、日历/标签/时钟等字段图标 |
| `PinkBadgeBrush` | `#FD97A1` | “超期待！”徽章底、粉色小胶囊徽章底 |
| `PinkBadgeTextBrush` | `#FFFDFC` | 主行动白字的值级 alias（#148 起“超期待！”文字改绑 `InkBrush`，该 token 不再绑到徽章文字） |
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
| `DangerBrush` | `#B94D3A` | 删除文字与图标；砖红仅保留给 Danger 操作，与玫粉主行动区分 |
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

- 中文 UI 使用 `UiFontFamily`，按序 fallback：`Microsoft YaHei UI` → `Microsoft YaHei` → `DengXian` → `Segoe UI` → `Arial` → `sans-serif`。手写体装饰文案使用 `HandwrittenDecoFontFamily`，在手写字族之后复用同一尾部回退链；渲染不得依赖 webfont 或联网字体。
- 数字使用表格数字（`Typography.NumeralAlignment=Tabular`）并保持半角；中西文与数字之间留自然空格。剩余时间、北京时间、截止时刻、月薪、今日已赚与数量文字均表格对齐；月薪与今日已赚使用同一套本地数字格式（N2，当前区域千位分隔 + 两位小数）。
- 表格数字专用字族例外（#148/#197）：数字色块数值和日期印记的数字统一使用 `NumeralFontFamily`（`Bahnschrift SemiCondensed` → `Bahnschrift` → `Microsoft YaHei UI` → `Microsoft YaHei` → `DengXian` → `Segoe UI` → `Arial` → `sans-serif`）并保持表格对齐；其余界面文字继续使用 `UiFontFamily`，单位字不使用该例外。
- 中文截止与日期在数字与「月」「日」之间留空格（如 `7 月 15 日 18:00`），两窗同一格式；本合同由倒计时截止行先行消费，便签日期由便签列表票消费同一格式（#153：日期选择器选中日期文本 `yyyy 年 M 月 d 日` 与便签列表组头、提醒摘要同套空格）。
- 设计坐标字号统一收敛为约 `1.25` 比例的 `13 / 16 / 20 / 26 / 44` 阶梯：`13px` caption、`16px` body、`20px` H2、`26px` H1、`44px` display。原 `17px` 装饰与 `18px` 字段标签并入 `16px`；字段标签保持 SemiBold；区块标题消费 `26px`，列表条目和输入内容消费 `20px`。
- 主窗口使用 `1165 x 1350` 的真实 DIP 布局；字号阶梯直接以 `13 / 16 / 20 / 26 / 44 DIP` 渲染，不再经过整窗非整数 Viewbox 缩放。日期与状态辅助文案只承担辅助扫读，关键状态不得再通过额外字号台阶挤入布局。
- 字重只用 Regular、SemiBold；层级主要靠位置、颜色和间距建立。区块标题、列表标题与选中态使用 SemiBold。
- 正文允许换行，标题和数量不换行；长便签由正文区域吸收宽度，操作区保持稳定尺寸。

## 4. 组件样式

### 数字色块分格

剩余时间由“数值圆角色块 + 单位字”成对构成：

- 每个分格是圆角矩形色块，内部只放数值，色块外侧紧跟小号单位字（`天 / 时 / 分 / 秒`）。
- 分格宽高比近似 `1:1` 偏竖长（AI 稿实测约 `64 x 96` px，宽高比约 `2:3`），圆角约为短边的 `1/4`；四个分格等距排布，分格横向间距约等于分格宽度的三分之一。
- 管理窗口与悬浮窗共用同一分格几何口径，各自保留坐标口径：悬浮窗按 `1:1` DIP（主项 `28 x 42`、次项 `20 x 30`），管理窗口按真实 DIP（主项 `52 x 78`、次项 `46 x 69`，分格间距主项 `14`、次项 `13`，均约块宽三分之一）；长数值只加宽色块，不改竖长比例。
- 主项（列表第一项，含“距离下班”）的分格底为 `PinkBlockBrush`，数值为 `PrimaryCountdownNumberBrush`。
- 次项分格底为 `SecondaryBlockBrush`，数值为 `InkBrush`。
- 单位字始终为 `MutedBrush`，字号小于数值约 `1/3`，不进入色块。悬浮窗单位字使用 `TypeCaptionFontSize`；管理窗口单位字固定 `13 DIP`，不得靠缩小字号硬塞进剩余时间列；长剩余时间（如 `999 天`）先压缩分格内边距与分格间距，最后才在辅助下限档之上收缩数值字号。
- 倒计时表面为固定 `194 DIP` 右对齐列；分格后仍需完整单行显示，不得省略。
- 数值不得换行、不得裁切；管理窗口与悬浮窗的数值都允许压缩分格内边距而不是压缩字号到不可读。

### 「超期待！」徽章

- 形态：圆角胶囊，底 `PinkBadgeBrush`，文字 `InkBrush` 字重 SemiBold（#148 换绑：白字 `PinkBadgeTextBrush` 对 `PinkBadgeBrush` 实测 2.05 不达信息文本门槛，`InkBrush` 实测 7.60）。
- 字号：悬浮窗使用 `TypeCaptionFontSize`（当前 `13 DIP`），仍满足 `12 DIP` 关键状态下限；空间不够先减内边距，不再缩字号到下限以下。
- 位置：紧贴主项标题右侧，垂直居中于标题行，与标题左缘间距约 `12 DIP`。
- 触发范围：两窗都只挂在“距离下班”主项上。主窗口不渲染“距离下班”条目，因此管理窗口倒计时列表不显示该徽章；普通倒计时即便承担视觉主项也不挂徽章；筛选激活时“距离下班”不参与悬浮窗列表，徽章随之不显示。
- 不可点击、不可聚焦、不参与命中测试。

### 并排分段切换控件

用于主窗口便签页的未完成/已完成视图切换。形态为容器胶囊 + 激活胶囊 + 数量徽章。

- 容器：圆角胶囊，底为透明或 `CardAltBrush`，内部左侧留 `2 DIP` 内边距，使激活胶囊悬浮在容器内。
- 激活段：圆角胶囊，使用 `PeachTintBrush` 底（或 `PinkTintBrush`，同一界面内二选一，不得混用），左缘带 `4 DIP` 宽 `PeachRailBrush` 强调轨，文字 `InkBrush` SemiBold。
- 未激活段：无底色，文字 `MutedBrush`。
- 数量徽章：激活段徽章底 `PeachBadgeBrush`、文字 `InkBrush`（#148 换绑，实测 8.69）；未激活段徽章底 `PeachBadgeMutedBrush`、文字 `BodyBrush`（#148 换绑，实测 6.09）。徽章文字不换行。
- 两段必须并排同屏可见、同时可点击；不再使用“点击标题切换、另一段隐藏”的单标题形态。
- 该控件只切换列表视图，不改变未完成/已完成两个平行章节彼此独立收放的语义（见 §5）。
- 主窗口顶部页签（倒计时/便签）是另一处并排分段控件：激活段底 `PinkTintBrush`、文字 `InkBrush` SemiBold（便签页激活段底 `PeachTintBrush`、文字 `InkBrush`；两处激活文字已按 §13.4 对比度基线由 `PinkAccentBrush`/`PeachRailBrush` 换绑，issue #133），未激活段无底、文字 `InkBrush`。页签右侧稳定显示 `筛选`、`标签` 命令胶囊与一个数量文字（`N 个倒计时` / `N 个待办`），数量不换行。
- 悬浮窗工作区切换（倒计时/便签）也使用该形态：激活段底 `PinkAccentStrongBrush`、文字白 SemiBold，未激活段无底、文字 `InkBrush`；以 AI 便签稿为准，AI 倒计时稿中较浅的胶囊底不采用（见 §12）。
- 激活胶囊全圆角（#154 写入合同）：页签与分段激活胶囊圆角取**自身短边一半**，落实 §5 结构基因 2 的「胶囊全圆角」——主窗页签模板 Surface 高 54 → 圆角 27（外环 60 → 30）、便签状态分段 Surface 高 44 → 圆角 22（外环 48 → 24）、悬浮工作区分段高 36 → 圆角 18；`RadiusPill`（19）继续专指 38 高的底部命令胶囊，不为每个高度增设新 token。容器底胶囊保持既有圆角（主窗页签容器 14、状态分段容器 17），只约束激活胶囊。

### 提醒规则单选组

倒计时页的提醒规则区改为一组三选一单选（`RadioButton`），标签依次为 `不提醒`、`按时间提醒`、`自定义提醒`。

- 组左侧是 `提醒规则` 标签与一个 `?` 帮助图标；帮助图标可聚焦并在悬停/聚焦时给出说明。
- 选中单选圈为 `ActiveBrush` 实心圆点带同色描边（#151 换绑：对编辑卡画布实测 3.27 ≥ 3:1 非文本门槛；装饰浅粉 `IconAccentBrush` 实测 2.13，不得承载选中态）；未选中为 `PeachLineBrush` 描边空心圈。
- 语义映射（照既有语义执行，不新增解释）：`不提醒` = 清空规则；`按时间提醒` = 创建 `absoluteOnce` 到点规则（offset 0）；`自定义提醒` = 打开现有 0~8 条规则次级编辑器。
- 该组不再包含“设为现在”控件。

### 输入框图标与占位文

- 每个输入框左缘内嵌一个 `20 DIP` 图标，图标与文字间距约 `12 DIP`；图标为单色描边风格，颜色 `MutedBrush`。
- 占位文一律 `MutedBrush`，常态文字 `InkBrush`；输入框底为 `PanelBrush`，边界 `PeachLineBrush`，圆角与控件同等。文本选区高亮使用独立语义色 `SelectionBrush`（#151），不复用 1px 结构线色，选中后肉眼可辨。
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

- 图标描边语言统一（#154 写入合同）：输入框内图标一律单色描边（`MutedBrush`、1.6、圆头），放大镜为描边圆环 + 右下手柄，与铅笔/日历同规格，不得用实心填充；字段标签旁不再放第二枚搜索镜（搜索便签字段的图标只在输入框左缘内嵌一处）。筛选漏斗是命令条图标中唯一实心字形：`InkBrush` 填充 + 同色描边平滑边缘——1.8px 纯描边漏斗在现役 Aliased 确定性捕获口径下对角笔画消失（v22 实证「筛选」只剩两字而「标签」图标可见），实心字形在任何栅格化下可识别。筛选摘要（`筛选 · N`）只更新具名文字 `FilterButtonText`，不得整体替换按钮内容销毁图标。

### 手写体装饰文案

- 使用 §3 的手写字族：主窗口文案用 `DecoTextBrush`，悬浮窗文案用 `DecoTextSoftBrush`，主窗口消费 `TypeBodyFontSize`（16px）设计坐标，可带轻微旋转（不超过 `±3°`）。
- 悬浮窗外壳例外（#149 写入合同）：悬浮窗按 `1:1` DIP 渲染，其手写体装饰文案在 `520 x 390` 外壳上保持 `TypeCaptionFontSize`（当前 13 DIP，如便签工作区左右下角装饰），这是相对主窗口 16px 设计坐标的外壳例外，不按主窗口设计坐标放大；字族、颜色与不可命中规则两窗一致。
- 位置为固定装饰层，`IsHitTestVisible=False`，不参与布局测量，不遮挡任何控件或可点击热区。
- 已确认落点：主窗口倒计时页区块标题右侧（`每一个期待，都值得被认真对待 ♡`）；主窗口页面右下角（`生活很忙，但别忘了可爱 ♡`）；悬浮窗倒计时工作区左下（`加油！美好的事情正在靠近 ~`）；悬浮窗便签工作区左下（`Have a nice day!`）与右下（`Live a sweeter day ♡`）。
- 倒计时页手写体装饰层落点（#154 写入合同）：两处文案都是 `CountdownWorkspace` Canvas 的直接子级（绝对设计坐标 `Canvas.Left/Top`，右上 `(640,149)` 右对齐宽 412、右下 `(880,970)` 右对齐宽 245），不是编辑卡或正文 Grid 的布局成员——不参与表单测量、不挤字段、不可点、不挡命中（用户故事 #147-55「只是情绪，不是布局成员」）。与便签页装饰带落点（`NotesWorkspace` Canvas 直接子级）同一层级语言。
- 文案本身不是契约，可随实现微调；字族、颜色、落点层级与不可命中规则是契约。

### 提醒中心两行按钮布局

提醒中心每条目的操作区固定为两行：

- 第一行：`知道了`（主行动，实心 `MainActionBrush` 底、白字、左侧白圈对勾图标）、`稍后提醒` 时间选择（次要按钮，`PanelBrush` 底、`PeachLineBrush` 边、左侧时钟图标、右侧下拉箭头，显示当前选定档位文本，例如 `10 分钟后`）、`稍后提醒`（次要按钮，左侧铃铛图标）。
- 第二行：`完成便签`（仅便签来源，左侧圆圈对勾图标）、`打开来源`（左侧外链图标）。
- 第二行整行居中且不拉伸（#150）：按钮行在条目卡内容区内水平居中，行宽等于子项期望宽之和，不吸满整行；只有 `打开来源` 的倒计时来源同样居中、不拉伸填满整行。
- 两行只包含该来源实际存在的动作；所有按钮图标化：图标在左、文字在右，图标与文字间距约 `8 DIP`。
- 稍后提醒档位选项保持 10、30、60 分钟、明天 09:00 与自定义；仅自定义模式展开日期、小时和分钟控件。
- 头部右上为 `全部已读` 次要按钮，左侧铃铛图标；其语义是对当前全部条目批量执行 `知道了`（见 §5）。
- 条目右侧 `待处理` 徽章：底 `WarningTintBrush`，`WarningAccentBrush` 的感叹号圆图标与文字；感叹号圆图标是矢量图形（描边竖条 + 圆点，#150），不引入 Bold 字重——全窗口字重只用 Regular/SemiBold。
- 条目左侧为来源图标方块：便签来源用暖黄便签图标（`SourceNoteIconBrush`），倒计时来源用粉底时钟图标；图标块不可点击，且是纯装饰（#150）——不携带可访问名、不作为独立控件朗读，来源名称留在整行可访问名与行内来源标签上。
- 条目只有一层卡片（`ReminderCardBrush` 底 + 1px 暖边），不再嵌套外层容器。
- 提醒中心保持无关闭按钮；有条目时拒绝直接关闭，Esc 不关闭有条目的中心，只把焦点移到首条目的 `知道了`（`知道了` 被失败态禁用时落到始终可用的 `打开来源`，#150）。

### 日期披露

每个日期实例拥有独立的 `Button` 和内容面板。标题展示相对日期或完整日期、数量和箭头；箭头向下表示展开，向右表示收起。日期轨道使用 1px 暖色线与圆点，属于时间语义而不是选中装饰。

- 分组标题卡左侧为 `PeachRailBrush` 圆点，标题 `M月d日 · 周X` 使用 `InkBrush` SemiBold。
- 右侧 `N 条` 数量使用 `MutedBrush`，紧跟展开箭头。
- 每个日期分组的收放状态独立持久化，互不联动。

### 便签卡与行内命令

- 便签卡圆角 `8px`，1px 边界；黄卡底 `NoteBrush`、边界 `NoteLineBrush`，白卡底 `NoteWhiteBrush`、边界 `PeachLineBrush`。内边距 14px，卡间距 8px（#153：两者分别消费 `SpacingCardPadding` 与 `SpacingUnit`，日期章节间距消费 `SpacingBlockGap`，不再写字面量）。
- 便签正文消费 `TypeBodyFontSize`，带显式行高 28（`BlockLineHeight`，与悬浮窗 13/18 同比例，#153）；多行正文与「下次提醒」摘要之间保持可扫读的间隔，摘要消费 `TypeH2FontSize`（主窗口真实 DIP 直接渲染）。
- 标签芯片（#153）：芯片文字单行不折字（`NoWrap`），芯片集合由 `WrapPanel` 先折行；单枚芯片最大宽 200 设计坐标，超长以省略号收尾并通过 `ToolTip` 提供完整标签名；芯片文字对比与配色维持 §2 `TagChipTextBrush` 合同。
- 行内命令固定为 `编辑`、`删除`、`…` 三个等距按钮，均有 1px 边界与 `8px` 圆角：`编辑` 文字与铅笔图标用 `InkBrush`，`删除` 文字与垃圾桶图标用 `DangerBrush`，`…` 用 `MutedBrush`。
- `…` 菜单内容与既有 `编辑` / `删除` 直排按钮一致（此为本文件记录的假设，见 §12）。三者并存是 AI 稿的明确画面。
- `…` 菜单键盘生命周期（#153）：管理窗口便签 `…` 与悬浮窗、筛选、标签管理、标签选择器共用同一套弹层焦点生命周期——打开聚焦首项「编辑」、Tab 在层内循环走完「编辑 / 删除」（焦点不得逃到被挡住的宿主窗口而层仍开着）、Esc 关闭并把焦点归还触发按钮。`PopupFocusLifecycle` 在弹层表面统一启用层内 Tab 循环。
- 状态与日期已经由上级章节表达，不在无提醒卡片内重复；已完成正文降低到 `MutedBrush` 并保留删除线。
- 便签页状态区右侧保留 `按日期分组` 下拉，当前只有 `按日期分组` 一个选项，属于占位单选项，不引入新的分组维度。
- 状态区分组标题卡使用 `NoteBrush` 暖黄底，左侧 `PeachRailBrush` 圆点，`M月d日 · 周X` 标题与右侧 `N 条` 数量加展开箭头。
- 空态与筛选弹层（#153）：倒计时与便签的空态文案统一允许换行（`TextWrapping=Wrap`），长文案不裁切；筛选弹层的便签日期筛选带可见字段标签 `日期`（`BodyBrush`），不是无名空下拉。
- 列表条目标题层级（#153/#197）：管理窗口倒计时列表条目标题消费 `TypeH2FontSize`（20px 设计坐标）；「新增倒计时」「显示设置」区块标题消费 `TypeH1FontSize`（26px），便签列表组头同样落 H2 阶梯。

### 悬浮窗底部命令区

便签工作区的底部命令区固定为：

- 居中主按钮 `＋ 添加便签`：`PinkAccentStrongBrush` 实心胶囊、白字、左侧白色加号圆圈图标。
- 展开态单一实心主行动（#149）：点击 `＋ 添加便签` 原地展开表单后，该切换钮降级为次级「收起」（次级样式、可见文字「收起」、可访问名「收起快速新增便签表单」），表单内「添加」是展开态唯一实心粉钮；收起态切换钮恢复实心胶囊「＋ 添加便签」。两态命中高度均保持 38px。
- 其右侧次级按钮 `设置`：`PanelBrush` 底、`PeachLineBrush` 边、齿轮图标加文字，点击打开主窗口。
- 搜索按钮（可访问名称“搜索便签”，仅便签工作区可见）常驻该命令区，不随快速新增表单收起而隐藏；其形态、三态搜索槽与 Tab 契约见 §10「便签：pastel 分格索引」。
- 内嵌快速新增表单默认收起，从列表底部移除；点击 `＋ 添加便签` 后**原地展开**该表单，再次点击或提交后收起。展开时不改变列表滚动偏移。展开后表单与底部命令行作为整体收进限高 170 的命令区滚动容器 `FloatingNotesCommandScroller`（滚动热区不越出右侧安全区），内容超高时在该容器内滚动；收起态内容低于限高，布局与既有基线一致。
- 表单保留正文、日期、标签、添加按钮以及紧凑“提醒规则”入口；复杂规则表单只在共享次级窗口展开。

### 提醒中心的条目与处置

提醒中心是单一滚动列表，不再嵌套外层卡片。每项稳定显示“便签”或“倒计时”来源、来源正文/名称和原计划北京时间；两类都提供“知道了 / 稍后提醒 / 打开来源”，只有便签提供“完成便签”，**任何条目都不提供删除**。稍后提醒使用选项菜单承载 10、30、60 分钟、明天 09:00 和自定义模式；仅自定义模式展开日期、小时和分钟控件，无效未来时间的错误紧邻这些来源控件。真实渲染和送达写盘完成前禁用处置，失败信息在条目内可见，但**打开来源始终可用**；Tab 导航必须覆盖该来源实际存在的动作和自定义控件，不能因失败态或长正文跳出当前条目。来源标签使用主文字 token（`InkBrush`），强调层级由半粗字重与章节字号承担。提醒中心与规则编辑器可从窗口头部拖动；提醒中心首次显示贴齐工作区右上，之后保留手动位置。

“全部已读”是对当前全部条目逐条执行“知道了”的批量封装，领域语义与逐条处置完全一致：批量写入处置 key，任一条失败则整体保留原条目并按既有退避节奏重试，不引入新的条目状态字段。“待处理”徽章是纯视觉标识。

### 删除撤销、完成确认与脏退出（#155）

破坏性动作的退路合同，五界面统一「确认框以取消为安全默认」：

- **删除确认**：主窗口倒计时/便签行与悬浮窗便签 `…` 菜单的删除共用 `ConfirmationDialog`（取消为 `IsDefault` + `IsCancel`，Enter/Esc/取消都不删除）；确认按钮文案「确认删除」配 `DangerButton`。#155 起该对话框参数化 `confirmText` / `confirmStyleKey`，但删除场景默认值不变。
- **短时撤销**：删除成功后在主窗口底部状态条（#151 `MainStatusBarSurface` 表面）提供短时「撤销」入口：状态条消息为「已删除倒计时/便签「X」。」，按钮可访问名「撤销删除倒计时/便签「X」」，10 秒后自动收起；撤销在单一候选事务中恢复被删记录与其全部规则提醒状态（cursor、送达/处置 key、活动稍后原样），只保留最近一次删除的载荷，不引入通用撤销栈；撤销成功后入口收起并显示「已撤销删除…」。主窗口与悬浮窗的删除成功都汇聚到该入口（悬浮窗经 `NoteDeleted` 事件由宿主转交主窗）。
- **完成便签确认**：提醒中心「完成便签」先弹确认框（标题「完成便签」，确认按钮「确认完成」配 `SecondaryButton`，取消为安全默认）再提交；取消时便签、条目与可完成状态全量保留。送达失败后的退避重试路径不经确认。「全部已读」保持批量「知道了」，不另加确认。
- **脏退出确认**：悬浮退出、托盘退出与悬浮窗关闭在进程退出前咨询未保存内容——显示设置脏（沿用 `HasUnsavedSettingsEdits` 语义）、倒计时/便签编辑器脏、悬浮快速新增正文非空；有未保存内容时弹「退出敏敏子倒计时」确认框（确认按钮「退出」配 `DangerButton`，取消为安全默认），取消即留在应用。管理窗口关闭/隐藏继续只 `Hide` 并保留草稿，不经过该确认。

### WPF-UI 局部主题边界（#205）

`ConfirmationDialog` 以 `Wpf.Ui.Controls.MessageBox` 承载确认框，但 WPF-UI 只在该窗口的 `Window.Resources` 中合并 `ThemesDictionary`（固定 `Light`）和 `ControlsDictionary`。WPF-UI 的 `Application.Current.Resources` 不得被修改，也不得把其隐式样式提升到主窗口、悬浮窗或其他次级窗口。

确认框在窗口资源作用域内把 WPF-UI 的 MessageBox、按钮和危险色资源键映射回本设计系统的暖粉 token（`BackgroundBrush`、`InkBrush`、`CardBrush`、`PeachLineBrush`、`MainAction*`、`DangerColor` 等），因此不会引入默认 WPF 蓝色或独立色板。WPF-UI 的 Primary 按钮承载「取消」，并在真实模板加载后设置 `IsDefault` + `IsCancel`；Secondary 按钮承载参数化的确认文案。`ConfirmationCancelButton`、`ConfirmationConfirmButton`、正文和滚动容器的自动化 ID 保持既有契约，调用方继续使用同步 `Show` / `ShowDialog` / `Close` seam。

### 辅助技术与快捷键（#156）

- **装饰退出朗读**：手写体装饰文案（倒计时页两处、便签页装饰带一处、悬浮窗两处角落文案）与便签分段数量徽章内的裸数字一律使用 `SilentTextBlock`——不产生自动化 peer，元素整体退出自动化树，是 Web aria-hidden 的 WPF 等价映射；不再被当作独立内容控件朗读（旧「装饰文案」可访问名废除）。纯装饰图标（描边 Path、提醒中心来源图标块）继续不携带可访问名。数量语义由分段按钮可访问名「查看未完成/已完成便签，共 N 条」与命令栏数量文字承担，不因退出朗读而丢失。
- **标签关系**：主窗口全部带可见文字的字段（名称、截止日期、时/分/秒、显示精度、月薪（元）、上班时间、下班时间、便签内容、便签日期、搜索便签、筛选弹层日期、新标签名称）通过 `AutomationProperties.LabeledBy` 指向可见标签，输入不再携带与标签重复的显式自动化名——同一字段只被读一遍；原更丰富的语境（「悬浮窗显示精度」「按便签日期筛选」）降级为该输入的帮助文本。不把 Web aria 属性原样搬到 WPF：一律映射为自动化名、帮助文本与标签关系。图标按钮与提醒规则单选继续有显式可访问名。
- **一处数量状态区域**：命令栏数量文字（`N 个待办` / `N 个倒计时`）是唯一的数量 polite 直播区（`AutomationLiveSetting.Polite`），文字只在数值真正变化时更新，分钟级刷新不空播；分段徽章数字、分段名与筛选结果消息不再各自承担数量播报。
- **Ctrl+F 分工**：主窗口便签页 Ctrl+F 聚焦便签搜索（与悬浮窗「Ctrl+F 搜索便签」的记忆一致）；倒计时页没有正文搜索，Ctrl+F 仍打开筛选弹层；悬浮窗 Ctrl+F 保持既有行为（切到便签工作区、展开并聚焦搜索）。
- **筛选清除的可访问名**：筛选弹层便签日期「清除」按钮的可访问名为「清除便签日期筛选」，不是只读一个「清除」。
- **窗口复位/贴边入口**：悬浮窗提供两个非拖动入口——托盘菜单「复位窗口位置」与悬浮窗内 `Ctrl+R`；两者共用同一复位实现（隐藏或最小化先恢复为可见正常态，再把窗口夹回工作区；`Reanchor` 只在越界时移动，未越界的手动位置保持不变）。完成任务不依赖拖无边框窗，不做方向键微移。

### 次级窗口与共享控件

次级提醒规则编辑器沿用 `CanvasBrush`、`NoteBrush`、`PeachLineBrush`、`InkBrush`、`MutedBrush` 和 `DangerBrush`，取消时不改变主表单草稿。日历模式使用频率、间隔、开始日期、本地时刻和类型化选择器：周选择星期，月选择日期或最后一天，年选择月份及日期或最后一天；无效间隔与选择器分别归位到对应控件；所有日历控件保持连续 Tab 顺序；不显示自由格式 RRULE、例外日期、次数或截止日期。主题化日历的翻月按钮可访问名称固定为「上一个月」和「下一个月」。

便签编辑区只显示“管理提醒规则”命令和规则数量摘要；倒计时编辑区改用「提醒规则单选组」（见 §4），不再提供独立的“管理提醒规则”入口，规则数量摘要也不显示。绝对一次、固定间隔、日历重复、新增、编辑、复制与删除集中在共享次级窗口；由倒计时进入时额外显示“倒计时相对”和提前分钟输入，错误紧邻该输入。

设置恢复与激活失败这类可能先于任何窗口初始化出现的系统对话框，同样是无边框暖粉壳（#150）：`WindowStyle.None`、圆角 `8px` 表面 + 1px `PeachLineBrush` 暖边、透明窗底，不掉回系统默认窗口铬。静态色是现役主画布 `#FDF3EB` / 主文字 `#3B190D` / 主行动 `#C23E68` 与主行动文字 `#FFFDFC` 的静态副本（允许在主题尚未 Apply 时渲染，不得保留旧色板），边界用 `PeachLineBrush` 的静态副本；按钮已吃应用隐式模板，悬停/按压/焦点沿用 `CardBrush`/`ActiveBrush`/`MainActionFocusBrush` 的暖色状态语言，禁止系统默认蓝。字号走与无 Viewbox 窗口一致的 `SecondaryWindow*FontSize` 层级 token，不再各写一套字面量（#150）。

可访问名称必须包含动作、状态和对象；分段控件与状态标题的可见文本不随动作改变，辅助技术名称可以准确说出“未完成/已完成”和数量。

### 倒计时紧凑输入

主窗口倒计时编辑区与设置区的文本框、日期框和下拉框统一为设计坐标 `56px` 高，字段标签消费 `TypeBodyFontSize`（16px SemiBold），内部横向留白为设计坐标 `16px`（本节数值均为设计坐标，不与 §3 渲染 DIP 混用）。名称和日期继续按内容职责分配宽度，时、分、秒保持可并排比较；相邻的标签选择器、保存与取消动作与输入行同高，不制造额外视觉台阶。

输入控件只使用 §2 的角色：`PanelBrush` 作为表面（#151 收口：主窗倒计时页紧凑输入面由画布奶油色改为白纸面板色，与页面画布分开）、`PeachLineBrush` 作为常态边界、`InkBrush` 作为文字、`MutedBrush` 作为图标与占位文。悬停与按压反馈使用 `ActiveBrush`；键盘焦点使用 `ActiveBrush` 边界，不依赖默认系统蓝。时、分、秒下拉点击文字区域即可打开，命中规则与日期选择器一致。编辑区顶部到列表底部使用连续的不透明 `BackgroundBrush` 底层，编辑区与设置区再叠加各自边界，保证真实 WPF 控件及区块间距都不依赖装饰 raster 中历史控件位置；不得引入默认系统蓝、独立灰阶或新的输入框专属色板。

### 倒计时编辑错误反馈与主次行动（#151）

- 字段级校验按字段拆开：名称、截止日期（含时/分/秒不完整）的校验错误以独立错误行出现在对应字段下方，文字 `DangerBrush`、字号消费 `TypeBodyFontSize`（16px 设计坐标）、可换行、不裁切；提交失败聚焦第一个无效控件；字段值修正后对应错误立即清除。错误行的名称与帮助文本携带完整消息，来源控件帮助文本同步，错误行以礼貌直播区播报（与悬浮窗快速新增同一套语义）。
- 唯一错误行（保存失败等工作区级错误）不设固定高度、不使用省略号，长错误完整换行可见；错误出现时同步在唯一错误行镜像首个字段错误消息。
- 偏好（月薪、上下班时间）格式错误出现在对应字段下方，并同时镜像到底部状态栏；不再只出现在窗口底栏。错误同样在字段修正后清除。
- 每页一枚实心主行动：倒计时页是「＋ 添加倒计时」（`MainActionBrush` 实心 + 白字）；「保存设置」是该页次级实心（`CoralActionBrush` 底 + `InkBrush` 主文字色，悬停/按压/焦点走 `ActiveBrush` 边的常规状态语言，不是主行动角色）；便签页「＋ 添加」仍是该页唯一实心主按钮（`MainActionBrush`）。
- 底部状态栏消费状态条表面：`MainStatusBarSurface` 以 `StatusBarBrush` 为底、1px `PeachLineBrush` 边、圆角消费 `RadiusOverlay`、内边距消费间距节奏（`14,8`）；错误字 `DangerBrush` 在该表面实测 4.83 ≥ 4.5 信息文本门槛，不达标时改用更深文字色而不放宽门槛。
- 排版与间距：编辑卡与偏好卡区块标题落 `TypeH1FontSize`（26px 设计坐标，#197 收敛标题阶梯）；字段行组间距消费 `SpacingFieldRow`（10px），错误行上间距消费 `SpacingUnit`（8px），不再使用与节奏无关的随机边距。

### 基础控件状态契约（#133）

App.xaml 控件模板段对基础 WPF 控件提供统一状态语言，全部引用 §13 token：

| 状态 | 统一口径 | 说明 |
| --- | --- | --- |
| 常态 | 表面按控件既有角色（`CanvasBrush` / 透明 / 分段选中态） | 1px `PeachLineBrush` 边或无边 |
| 悬停 | 常规按钮表面换 `CardAltBrush`，桃色结构线换更深的 `ActiveHoverBrush`；主行动使用同族 hover 底 | 亮度差 ≥ 4%，`MotionHoverDuration` 120ms ease-out |
| 按压 | 常驻 `FocusBorderThickness`（2px）透明环换 `ActiveBrush`，内容以 `RenderTransform` 缩至 0.97 | `MotionPressDuration` 100ms ease-out；外边界与兄弟元素位置不变，不改边厚 |
| 键盘焦点 | 仅键盘导航设置自定义 `FocusVisualState.IsKeyboardFocusVisible` 后显示 `ActiveBrush` 边；主操作按钮与置顶开关用 `MainActionFocusBrush`（§6 唯一焦点例外） | 鼠标点击取得焦点不残留环 |
| 禁用 | 整体 `Opacity` 0.45 | 全控件统一取值 |

- 覆盖范围：`Button`（含 Primary/Secondary/FloatingAccent/Danger/IconButton/CalendarNavigationButton）、`ToggleButton`（SegmentButton 族、FloatingHeaderSegmentButton、FilterOptionButton、TagToggleButton）、`CheckBox`、`RadioButton`（#133 新增隐式样式，视觉语言按 §4 提醒规则单选组；未显式套样的单选钮不再落系统默认外观）、`ComboBox`（展开态边 `ActiveBrush`）、`DatePicker`（悬停/展开/键盘焦点/禁用态；焦点环用 `FocusBorderThickness`）、`TextBox`、`ToolTip`（非交互控件，只承担主题化常态，无悬停/按压/焦点/禁用态）。Calendar 系列维持既有 token 化状态。
- 反馈节奏：hover 颜色使用 `MotionHoverDuration`（120ms）ease-out，按压缩放使用 `MotionPressDuration`（100ms）ease-out；焦点反馈仍即时生效。所有过渡只作用于颜色、透明度或 `RenderTransform`，不改布局属性。颜色对比与状态几何由 `MainHoverStateContrastIsPerceivable`、`MainPressedStateGeometryStable`、`MainMouseClickDoesNotLeaveFocusRing`、`MainKeyboardFocusRingShowsOnTab` 和 `MainSelectedTabWeightDominates` 五项验证字段钉住。
- MainWindow 本地样式（`MainSegmentButton`、`MainNotesSegmentButton`、`MainReminderModeRadio`、`MainTopmostCheck`、`MainNoteCompletionCheck`、`MainReminderHelpButton`、`MainNoteDateDisclosureButton`）沿用窗口内模板：#133 仅对其中两处激活页签文字执行 §13.4 换绑。#152 已补齐遗留缺口：`MainSegmentButton`/`MainNotesSegmentButton`/`MainReminderHelpButton`/`MainReminderModeRadio`/`MainNoteDateDisclosureButton` 具备按压（环换 `ActiveBrush`，与键盘焦点同语言）与禁用（整体 `Opacity` 0.45）外观；`MainPrimaryButton`/`MainSecondarySolidButton`/`MainTopmostCheck`/`MainNoteCompletionCheck` 的焦点/按压改为常驻环变色（见下方「主窗可操作性」）。
- 图标按钮必须具备 `AutomationProperties.Name`；动态列表行、标签芯片与分段按钮通过既有 `UiAccessibility`/`NameControl` 命名并经 `ToggleButton`/`CheckBox` 的 Toggle 语义暴露选中状态（MainWindow.xaml.cs / FloatingWindow*.cs 既有接线，#133 复核无缺口）。

### 日期与下拉接入（#206）

日期框与下拉继承 WPF-UI 4.3.0 对原生 WPF 控件提供的样式，暖粉模板继续使用本节的现役色彩、状态和箭头几何。文字区释放时打开、箭头打开、只读中文日期、独立时分秒、日历键盘及 Esc 返回触发按钮均保持；实现不再依赖窗口逐个挂接日期/下拉 Behavior。历史基线条目描述当时实现，当前源码与重新捕获的视觉基线分别验收。

### 主窗可操作性与紧凑命中（#152）

- **常驻焦点环**：主窗口本地控件（`MainPrimaryButton`、`MainSecondarySolidButton`、`MainSegmentButton`、`MainNotesSegmentButton`、`MainReminderHelpButton`、`MainReminderModeRadio`、`MainNoteDateDisclosureButton`）的键盘焦点一律使用常驻 `FocusBorderThickness`（2px）透明环变色实现，悬停/按压/焦点均不得改写 `BorderThickness` 挤压布局。主按钮（`＋ 添加倒计时`/`＋ 添加`）与 `窗口置顶` 继续用 `MainActionFocusBrush` 暖粉焦点例外（唯一焦点例外，§6），且不得再套 `FocusVisualStyle` 第二套焦点描边；其余键盘路径控件继续消费 2px 暖边 `MainKeyboardFocusVisual`。
- **选中粉底/实心粉邻色**：选中页签（`PinkTintBrush`）的焦点环与粉底之间常驻 1px 间隙（Surface 外缩 3），环的实际邻色是容器 `CardAltBrush`（实测 3.50 ≥ 3:1），不直接贴粉底（`ActiveBrush`×`PinkTintBrush` 实测 2.90 不达标）。悬浮窗选中实心粉工作区胶囊（`PinkAccentStrongBrush`）按裁决用 `MainActionFocusBrush` 暖金做外环（实测 3.62 ≥ 3:1）。键盘焦点环验收按上下文钉唯一期望 token（常规 `ActiveBrush`/实心粉邻色暖金），不存在「不达标但匹配旧强调色则放过」的分支。
- **悬停区分**：未选中页签悬停使用 `CardAltBrush` + `ActiveHoverBrush`，不得复用选中实心底；选中页签统一使用 `PinkTintBrush` 底和 `SemiBold` 字重，悬停不得反转层级。
- **帮助聚焦**：提醒规则 `?` 帮助在键盘聚焦时即时打开与悬停相同的 ToolTip 说明，并以 `AutomationProperties.HelpText` 提供同一份帮助文本。
- **紧凑命中下限（24 DIP）**：主窗口真实 DIP 布局中，便签完成勾选与提醒规则帮助的命中区不得低于桌面紧凑下限 24 DIP——两者命中槽位为 38 DIP，勾选可见框保持 24 DIP 居中。悬浮窗既有 36 下限与头部 3px、滚动条 10px 例外不变、不借此放宽。筛选芯片（`FilterOptionButton`/`TagToggleButton`）高度与日历翻月按钮（`CalendarNavigationButton`）统一为 36，筛选芯片相邻间距 8（含筛选标签芯片 0,0,8,8）。
- **动效入口**：`ApplicationTheme.ClientAreaAnimationEnabled` 读取系统「客户区动画」开关；开启动效时消费 §9 的 token，关闭时所有有效时长为 0，直接到达目标值。

### 视觉铬与窗铬合同（#154）

- **管理窗口原生 DWM 圆角与系统阴影**：管理窗口继续使用不透明的无边框 OS 窗（`WindowStyle=None`），`WindowChrome` 保持 `CornerRadius=0`、`GlassFrameThickness=0`，根元素不开 `AllowsTransparency`、不为此打开整窗透明。首个 HWND 创建后，主窗口通过 `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)` 请求 Windows 11 DWM 绘制系统圆角与投影；DWM 不可用、系统低于 Windows 11 或隔离桌面不支持时保持现有窗铬，写入 `MainWindowDwmRoundedCornersFallback` 诊断事件且不抛异常。装饰 raster（`MainReferenceDecorationLayer`）只承载内容、仍 `IsHitTestVisible=False`，不模拟窗体外壳圆角。兼容字段 `MainWindowSquareChromeNoTransparency` 现用于钉住上述无透明窗铬、DWM 请求或安全降级状态。
- **页签在装饰带之上（#135 工作台骨架裁决）**：倒计时方向稿中「装饰带在页签之上」的构图不作为实现目标；实现保持 §5 工作台骨架——命令栏（倒计时/便签页签、筛选、标签、数量）位于内容投影层（`MainContentProjection`，ZIndex 2），装饰 raster 层（`MainReferenceDecorationLayer`，ZIndex 1）在其下且不可命中。运行态由 `TabsRenderAboveDecorationBand` 探针钉住。
- **矢量窗铬**：管理窗口最小化/关闭按钮与悬浮窗头部命令使用同一套矢量路径语言（12x12 坐标系、2px 圆头描边、描边绑定按钮前景——关闭为 `DangerBrush`）：最小化 `M 2,10 L 12,10`，关闭 `M 2,2 L 12,12 M 12,2 L 2,12`；不再是 `−`/`×` 文本字形。运行态由 `MainWindowChromeButtonsUseVectorPaths` 探针钉住。
- **悬浮外壳深度**：悬浮窗外壳不用字面色大面积投影（原 `TaskCard` 上的 `DropShadowEffect #6F4A3C/Blur24/ShadowDepth4/Opacity0.16` 已移除）；深度继续由 1px 暖边、背景角色、留白与 raster 表达（§5 结构基因 4）。现役主行动按钮无投影，不引入「主按钮级极轻投影」的第二种深度语言；未来若引入，投影颜色必须走 token 且达到主按钮焦点色那一级的极轻存在感。运行态由 `FloatingShellLargeShadowRemoved` 探针钉住。

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
- 层次固定（#154 重申）：装饰 raster 层（`MainReferenceDecorationLayer`，ZIndex 1）在最下，业务内容投影层（`MainContentProjection`，ZIndex 2）在其上——命令栏页签、区块与列表都在装饰带之上；倒计时方向稿「装饰带在页签上」的相反次序不作为实现目标。
- 每个页面保留各自的装饰带与手写体文案落点；装饰层必须 `IsHitTestVisible=False`、不参与命中（§4、§13.5）。#136 便签页试点在该骨架上完成最终视觉，不重排区段顺序。

暖粉日间的结构基因：

1. 使用 1px 的 whisper 边界，映射为 `PeachLineBrush` / `NoteLineBrush`，让分隔线只承担结构职责。
2. 使用 `8px` 卡片刻度与 `4px` 功能控件刻度；分段胶囊、数字色块、按钮胶囊使用全圆角（短边一半），卡片使用 `8px` 圆角。
3. 使用 `8px` 基准间距与暖中性色层级；状态章节 10px、日期章节 12px、卡间 8px 都从该节奏派生。
4. 采用低存在感的多层暖色深度：不堆灰阴影，优先用 1px 边界、背景角色和留白表达层级；主行动按钮允许一层极轻的暖色投影以表达可点击性。

## 6. 深度层级

页面画布为 Level 0；内容区块为 Level 1（`CardBrush` 表面 + 1px `PeachLineBrush`）；便签卡与提醒条目卡为 Level 2（棕黄或白纸底 + 1px 边界）。不使用外发光、霓虹阴影或浮动大卡片。常规焦点使用 `ActiveBrush` 边界，不能依赖默认系统蓝。主操作按钮与置顶开关共用同一暖粉焦点资源，这是全应用唯一的焦点例外。按压与悬停反馈仍使用既有 `ActiveBrush` 语义。#152 起主窗口焦点一律以常驻 `FocusBorderThickness` 透明环变色表达（键盘聚焦只换色、不加厚、不挤布局），主窗口不再给主按钮/置顶叠 `FocusVisualStyle` 第二套更粗描边。

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

主窗口去掉整窗 `Viewbox`，默认与最小可支持尺寸统一为 `1165 x 1350` DIP；业务控件在现有 Grid 骨架中直接排布，关键文字不低于 `13 DIP`，数字分格不因缩放发虚。分段控件采用三列稳定结构（强调轨、文字、数量徽章）；文字列可压缩，徽章不被挤压。日期轨道保持 1px 线和 30px 槽位，便签正文换行，操作区不换行。必须检查标题、数量、辅助文字、数字色块和长便签没有裁切或重叠。

## 9. Motion 哲学

高频切换与收放保持短促反馈：按钮 hover 颜色以 120ms ease-out 过渡，按下的 `RenderTransform` 缩放到 `0.97` 以 100ms ease-out 完成，不触发重新布局。日历、下拉框、标签选择器和提示框由 `PopupMotionBehavior` 统一消费进入 140ms、退出 90ms token：进入从透明且向下 4px 移入，退出向下 4px 淡出；退出期间保留弹层打开，使打开中的关闭可以从当前值反向。所有动效时长和缓动集中在资源 token，并由 `ApplicationTheme.ClientAreaAnimationEnabled` 控制；系统关闭动画时 token 有效时长为 0，状态立即到达目标值。允许的属性只有颜色、透明度和 `RenderTransform`，单段上限 200ms；动画使用 `SnapshotAndReplace` 从当前值继续，快速 hover / 按下或弹层反向可被打断而不会闪回起点。键盘焦点反馈不增加额外动画。

现行动效 token：

| 资源 token | 开启动效时长 | 缓动 | 使用落点 |
| --- | ---: | --- | --- |
| `MotionHoverDuration` | 120ms | `MotionEaseOut`（quartic） | 按钮与分段控件 hover 颜色 |
| `MotionPressDuration` | 100ms | `MotionEaseOut`（quartic） | 按钮与分段控件按下缩放 |
| `MotionPopupEnterDuration` | 140ms | `MotionEaseOut`（quartic） | Popup / ToolTip 进入 |
| `MotionPopupExitDuration` | 90ms | `MotionEaseOut`（quartic） | Popup / ToolTip 退出 |
| `MotionIndicatorDuration` | 160ms | `MotionEaseOut`（quartic） | 页签/分段指示条 |
| `MotionCountdownDuration` | 90ms | `MotionEaseOut`（quartic） | 倒计时数字轻量切换 |

`MotionCubicEaseOut` 同时作为可复用的 cubic ease-out 资源；新增落点只能引用上述 token，不得在控件模板中写字面时长或第二套缓动。

主窗口工作区页签与便签“未完成 / 已完成”分段共用选中指示条。指示条的最终左、右边界与选中控件的实际几何对齐；切换时只对位移和横向缩放做 `MotionIndicatorDuration` 过渡，使用 `SnapshotAndReplace` 从当前呈现值继续。快速连续切换以最后一次选中状态为准，系统关闭客户区动画时直接到达同一目标几何。

## 10. 悬浮窗双工作区附录

本节只约束现役 `520 x 390` 悬浮窗。外壳、标题栏 raster、三只玩偶及其锚点属于不可变基线：标题栏固定为 `x=51, y=72, w=422, h=63`；动态内容起点以现役行距为准（约 `x=60, y=147 ± 2`）。`x=396` 是便签工作区卡片、日期印记、快速新增控件、滚动条以及倒计时**文本内容**的正文右边界；倒计时装饰背景、强调轨和规则线由右侧 `54px` 角色安全区约束，允许其底面延伸到文本列右侧，但不得命中测试、不得覆盖倒计时文本。标题栏命令不受该正文安全区约束。玩偶层和装饰层必须 `IsHitTestVisible=False`；拖拽、工作区切换、托盘、管理窗口、退出、滚动、完成勾选、日期、提醒、标签和添加动作保持原命令与顺序。

标题栏中间命令固定为房子（打开管理窗口）：以 AI 便签稿为准；AI 倒计时稿中画出的最大化方块为误画，不采用。

首次显示必须让倒计时导航处于选中态并与实际工作区一致；之后每次切换始终恰有一个工作区选中。工作区、标题、输入区和正文必须通过同一切换入口同步，不保留第二套初始化状态。

默认贴齐工作区右下并留 `12 DIP` 边距；用户拖动后保留手动位置，重锚定时只夹入工作区。搜索击键、筛选和内容 `Refresh` 不得改 `Left`/`Top`。只在 `SizeChanged`、`Loaded`、窗口变为可见、以及拖拽结束后重锚定。#156 起另有两个非拖动复位入口：托盘菜单「复位窗口位置」与悬浮窗内 `Ctrl+R`，共用「隐藏先恢复、正常态、夹回工作区」实现；复位不是托盘切换，不受其守卫约束。

### 倒计时：暖粉分格

- 列表第一项始终是唯一主项，使用 `PinkTintSoftBrush` 底、`FloatingDayPrimaryCountdownAccentBrush` 粉红窄强调轨和 §4 的数字色块分格；“距离下班”在无筛选约束时存在则固定排在首行并完整继承主项的字体、强调色、色块和效果，筛选激活时不参与悬浮窗列表，**距离下班不存在时，第一个普通倒计时仍是唯一视觉主项**。主项标题下方的截止行只显示 `M 月 d 日 HH:mm`（数字与「月」「日」之间留空格、表格数字），不追加“北京时间”标签；无论列表是否出现滚动条，截止时间都必须完整显示。主项标题最多两行；主项同行右侧可显示一枚粉色心形装饰，属装饰层。
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
- 右侧 `48px` 日期印记按条目 pastel 轮换：粉 `#FEEFF1`（数字 `#D41F57`）→ 蓝 `#EEF5FE`（数字 `#2F86DB`）→ 绿 `#EDF6E8`（数字 `#318939`），按列表顺序循环。印记显示 `M月`、`dd`、`周X` 三行，只有 `dd` 是当日数字并与印记同色。长卡被视口裁切时，印记三行以「卡片可见区域内居中」的粘性方式整体保持可见（#149），不得只剩「日 / 周」；当卡片可见条带本身低于印记高度时（如搜索展开态压缩视口），印记按可达的最大可见度对齐可见条带底部，不再是空白列。pastel 背景列仍铺满卡片高度，仅三行文字位移，印记继续不可点击、不可聚焦。
- 便签列表滚动内容以 `12 DIP` 尾随内边距收尾（#149）：滚到底时末卡与底部状态行、命令区之间保留稳定空隙，不视觉顶住。
- 便签卡底也参与轮换：暖黄 `NoteBrush` 与白纸 `NoteWhiteBrush` 交替，形成条带节奏。
- 日期印记只帮助扫读，不是侧栏、筛选器或新导航；它不可点击、不可聚焦，也不能覆盖复选框、正文、提醒或 `…` 菜单。
- 每条便签右侧独立 `…` 菜单，提供 `编辑` / `删除`，复用主窗口编辑流程。菜单接入与筛选 / 标签选择器同一套弹层焦点生命周期（#149）：打开聚焦首项「编辑」，Tab 在层内循环走完「编辑 / 删除」，Esc 关闭并把焦点归还触发按钮。
- 正文和提醒允许自然换行；列表与便签正文继续只使用既有 `FloatingBodyScroller`，不新增内部滚动容器。快速新增输入框允许内部滚动；快速新增展开态底部命令区的整体滚动容器（`FloatingNotesCommandScroller`）同为该条例外。
- 悬浮便签正文和提醒摘要均消费 `TypeCaptionFontSize`（当前 `13 DIP`）；正文行高为 `18 DIP`、提醒摘要行高为 `17 DIP`，均使用显式 `BlockLineHeight`，不得继承主窗口的正文排版尺寸。
- 快速新增区使用便签黄纸面、`6px` 圆角和珊瑚短标，保留正文、日期、标签、添加、搜索按钮以及紧凑“提醒规则”入口；可见性与展开行为见 §4「悬浮窗底部命令区」。
- 搜索按钮位于底部命令区，可访问名称“搜索便签”，仅便签工作区可见；可见内容是闭合圆环加右下手柄的放大镜，不得渲染成残缺 C 或左括号，也不得被按钮内边距裁切。列表上方槽位三态互斥：无词且未展开时无任何行；收起且有词时显示“搜索:{词} ✕”（点词展开、点 ✕ 清除）；展开时显示输入行（MaxLength=140，占位“搜索便签”），列表视口变矮、底部命令区位置不动。Esc 清空并收起；Ctrl+F 在任意工作区切到便签、展开并聚焦。无匹配时列表区显示“没有匹配的便签。”与“清除搜索”，底部命令区保持可用。筛选约束（标签 / 无标签 / 状态 / 日期）导致的空态提供“清除筛选”，点击还原到无约束便签列表（#149）；搜索空态只出现“清除搜索”、约束空态只出现“清除筛选”，两者互斥。
- 快速新增正文输入框带与管理窗口一致的占位文「写下要记住的事情吧…」（#149）：空白时可见、输入后隐藏，避免空白框不像输入区。
- Tab 顺序：搜索按钮在 `＋ 添加便签` 之后；输入框展开时位于列表首卡复选框之前，收起时移出链。视觉顺序与该 Tab 合同一致。
- 底部命令区的 `DatePicker`、`ComboBox`、标签选择器、复选框、Popup、`ScrollViewer` 和 `ScrollBar` 必须从悬浮窗局部资源解析日间主题，不得回退应用级旧主题或默认 WPF 样式。
- 快速新增区的输入背景、输入文字、提醒标签和错误文字必须分别绑定局部 `NoteInputBrushKey`、`NoteInputTextBrushKey` 和 `ErrorBrushKey`；窗口级资源可覆盖同名应用资源，但不得改变这些输入/错误语义 token 的局部归属。
- 完成复选框继续通过 `Tag=note.Id` 提交；失败时取消勾选并显示既有错误信息。

### 局部滚动条

- 悬浮正文滚动条使用局部样式，不改变主窗口滚动条；垂直滚动条保留 `10px` 命中热区，常态、悬停和拖动状态的可见 thumb 均为 `4px`，只通过不透明度渐进增强视觉反馈。
- 命中轨与滚动条列的区分（#157 写入合同）：模板 `FloatingScrollBarHitRail`（`Width=10`）是设计的命中热区几何；WPF 滚动条列的实际宽度可被系统滚动条度量撑得更宽（100% 缩放 17 DIP、125% 缩放 17.6 DIP），超出的部分透明、不可见、不参与可视构图。视口右缘的探针比较以「滚动内容槽右缘 − 设计命中轨宽」为确定性基准，不以随系统度量漂移的列宽为基准；可见 thumb 仍右缘对齐、不得覆盖正文。
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

#146 收口重拍（2026-09-17）：v22 已于 2026-09-17 按 #131 线终态（#135/#136/#138/#139/#140/#143 及 #146 收口修正之后的工作台骨架、便签试点、倒计时页重构、悬浮窗命中区几何与滚动条 Focusable 语义）原目录重拍，主窗口 8 帧 + 悬浮窗 4 帧与双 manifest 均在库；manifest 的 `sourceBinding`（主窗 15 个、悬浮窗 9 个视觉源文件）与工作树逐字节一致，主窗聚合 SHA-256 为 `4E256D3988D44CD883BCC1AFB07B6C83504F71352BCAD8246400D5B96DBC12E2`，悬浮窗聚合为 `775A78672BEE050AF83A0B8A8FC25C15A8FE55A66230BF03716C35A4367FEFCC`。2026-09-17 源码树 validate-only run 与六阶段并行套件 run（20260917T153158，全绿）中主窗口十六项基线门禁与悬浮窗基线门禁全部通过，v22 自此继续担任现役验收基线，v21 及更早目录仅归档自指（现役代码与脚本零引用）。历史帧归档：2026-09-14 版于 `run-evidence\clock-countdown-issue-146-final-closure\v22-prev-20260914\`；2026-09-17 第一/二次重拍帧与最终版逐字节相同（12/12 PNG SHA 一致，`...-152513\` 归档即最终帧副本）。

2026-09-17 深夜补拍（捕获口径环境解耦）：当晚 21:09 GameViewer 虚拟显示适配器加入显示拓扑后，WPF 在无 `Avalon.Graphics` 覆盖键时按显示环境推导的文本渲染参数整体漂移——灰阶抗锯齿的 gamma/对比度变化（细笔画出现/消失）叠加 Display 格式化像素对齐度量变化（长便签列表 `ScrollableHeight` 871.4→879.67）——v22 双基线自 21:49 起在 SourceTree validate 中持续误红（`PngHashesMatch`/`PixelMetricsWithinThresholds` 超阈值 2-3 个数量级，渲染本身逐次确定）。根因修复：`ValidationReport.PinDeterministicTextOptions` 由「灰阶抗锯齿 + Display 格式化」改为 **Aliased + Ideal**（1-bit 光栅化不经灰阶 gamma/对比度管线，浮点格式化无像素网格 hinting，捕获像素与布局不再含显示推导参数入口），并按该口径原目录重拍 v22 全部 12 帧与双 manifest；灰阶/Display 旧帧归档于 `run-evidence\clock-countdown-issue-146-final-closure\v22-prev-20260917-234056\`。基线像素门禁自此对显示拓扑/时段变化免疫。仍保留的注意事项：显示器链路休眠的无人值守时段（`GetDeviceGammaRamp` 失败）历史上还伴随「捕获整幅透明」类 flake（与文本渲染参数无关的窗口合成路径问题）；该类 flake 未被本次口径钉死，视觉复验遇整幅透明时仍应在显示器唤醒的会话时段重试。

当前状态（2026-09-18，#148 重拍）：#148 倒计时列表可读收口改动了全部 12 帧的可见构成——管理窗口倒计时行分格改为竖长 2:3（主项 42×63、次项 38×57 设计坐标）、移除管理窗口「超期待！」徽章、到期剩余文字换绑主项数字色/正文色、NowText 与分格数值表格数字、月薪输入改 N2；悬浮窗「超期待！」徽章字号 9→12 DIP 并垂直居中、文字换绑 `InkBrush`、主项截止行改「M 月 d 日 HH:mm」空格与表格数字、`FloatingIncome` 表格数字；便签分段数量徽章文字换绑 `InkBrush`/`BodyBrush`——故按现役 Aliased+Ideal 口径原目录重拍 v22 全部 12 帧与双 manifest，主窗聚合 SHA-256 为 `BB0BE1DCBB2D96A2192D2DC400404A711701D1E201FC4F8D4500599C404FC5EF`，悬浮窗聚合为 `D26B0026A7620389D564CA61F50BFFD0C457ADEC0C12FE74039CBB628605EA0D`；`sourceBinding` 与工作树逐字节一致（并顺带修正 77b4a33 遥测改动后悬浮 sourceBinding 中 `FloatingWindow.xaml.cs` 的陈旧哈希记录，该改动视觉等价）。悬浮参考构图探针 `RunFloatingReferenceExercise` 的主卡归一化高度 0.1742→0.1865、次卡 Y 0.5533→0.5656 随新构成同步（外壳/面板/标题栏矩形不变）。2026-09-18 套件 run（20260918T162458，Build/CSharp/SourceValidation/InstallValidate 全绿）中双基线门禁、悬浮参考构图与 #148 新增可读性探针全部通过，v22 继续担任现役验收基线。

当前状态（2026-09-18，#149 重拍）：#149 悬浮便签工作区收口改动了悬浮窗 3 个便签帧的可见构成——长便签滚到底时日期印记三行以「卡片可见区域内居中」的粘性方式整体可见（`long-notes-day-bottom`、`notes-search-expanded-day` 中被裁长卡的印记不再是空列或只剩「日 / 周」）、便签列表滚动内容以 12 DIP 尾随内边距收尾（`long-notes-day-bottom` 末卡下移）——`floating-countdowns-day` 与 #148 版逐字节一致（改动严格限定便签工作区）。悬浮窗 manifest 按现役 Aliased+Ideal 口径原目录重拍 4 帧并更新，悬浮窗 `sourceBinding` 聚合 SHA-256 为 `B37B970B8BC3FF87609ACCA7966CA55F6A90C875269F03447249C205C1B64D51`，与工作树逐字节一致；主窗 8 帧与主窗 manifest 不变（聚合仍为 `BB0BE1DCBB2D96A2192D2DC400404A711701D1E201FC4F8D4500599C404FC5EF`）。#148 版悬浮 4 帧归档于 `run-evidence\clock-countdown-issue-149-closure\v22-prev-20260918-floating\`。

当前状态（2026-09-18，#150）：#150 提醒中心逃逸路径与设置恢复/激活失败窗暖粉壳收口只改动提醒中心与次级窗源码、探针与合同条款，未改动 v22 双 manifest `sourceBinding` 覆盖的任何视觉源文件，v22 全部 12 帧与双 manifest 原样有效（主窗聚合 `BB0BE1DC…`、悬浮窗聚合 `B37B970B…`）。提醒中心继续无 v22 像素基线帧，其逃逸路径、两行按钮居中不拉伸、来源图标退出朗读与字重白名单由代码探针钉住（`ReminderCenterEscapeFocusesAcknowledge`、`ReminderCenterTypographyWeightWhitelist` 与既有 `ReminderCenterTwoRowActionsLayout`/`ReminderCenterEntryVisualIdentity` 扩展），设置恢复/激活失败窗的无边框暖粉壳与现役静态色副本由 `SystemDialogPrimaryButtonContrast.UsesActiveStaticPalette` 探针钉住；排版报告扩展为覆盖五种次级窗口。

当前状态（2026-09-18，#151 重拍）：#151 倒计时编辑收口改动了主窗全部 8 帧的可见构成——底部状态栏新增 `MainStatusBarSurface` 状态条表面（`StatusBarBrush` 底 + 1px `PeachLineBrush` 边）、倒计时页紧凑输入面改白纸 `PanelBrush`（名称/月薪/上下班文本框、时/分/秒与显示精度下拉；日期框内嵌文字区经 `DatePickerTextBoxStyle` 同步改白纸，主窗便签编辑卡日期框同享该 §4 合同语义）、「保存设置」换珊瑚次级实心（`CoralActionBrush` + `InkBrush` 文字）、提醒规则选中圆点/描边换绑 `ActiveBrush`、编辑卡与偏好卡区块标题 27→`TypeH2FontSize`（22px 设计坐标）、字段行距 20→`SpacingFieldRow`、错误行去除固定高度裁切（常态不可见不影响帧构成，构成变化主要来自前三项与排版收紧）。App.xaml/ApplicationTheme.cs（新增 `CoralActionColor/Brush`、`SelectionColor/Brush`）同入双 manifest `sourceBinding`，故主窗 8 帧与悬浮窗 4 帧按现役 Aliased+Ideal 口径原目录重拍并重写双 manifest：主窗聚合 SHA-256 `ED7C310A6FA3B353103BE54D34435FC10C75FB2C4C0B4C3B207FAA098F7C143B`，悬浮窗聚合 `47B08A3348A3547525A45862344D190B67AF1D5B9F5DE96D2F63F7BEBDF5AFA6`。悬浮 4 帧的可见构图（卡片、印记、命令区、搜索槽）与 #149 版一致，最终重拍钉住的布局度量与 #149 版同值（`ScrollableHeight` 883.19 / 927.19）；期间一次中间重拍会话实测到 890.52/934.52 的显示拓扑漂移档位（同 2026-09-17 深夜补拍记录的 871.4→879.67 一类，悬浮源文件零改动下出现），证实该漂移为会话级环境档位而非源码变化，最终以稳定档位重拍收口。#151 前的 12 帧与中间档位帧分别归档于 `run-evidence\clock-countdown-issue-151-closure\`（`v22-prev-20260918-main` / `v22-prev-20260918-floating` / `v22-epochA-main` / `v22-epochA-floating`）。

当前状态（2026-09-19，#152 重拍）：#152 主窗可操作收口改动了主窗 8 帧的可见构成——主按钮/次级实心/两枚页签模板改为常驻 2px 透明环 + 内缩 Surface（常态静置即可见的外缩边距变化）、置顶/完成勾选框改常驻 2px 结构线边、完成勾选命中槽位 30→38 设计坐标（可见框仍 24）、提醒帮助按钮 34→38 设计坐标、筛选芯片高度 34→36 与相邻间距 6→8（`main-day-notes-filter-open`）。悬浮 4 帧预期构图不变（胶囊焦点色只在聚焦出现、捕获前清焦；App.xaml/ApplicationTheme.cs 同入悬浮 sourceBinding 故随双 manifest 一并重写）。双 manifest 按现役 Aliased+Ideal 口径原目录重拍：主窗聚合 SHA-256 `260ACD5A829D30B738342855ED775CE31D544D6004372BAD5D6743607DCEC0EC`，悬浮窗聚合 `C777A2E53B8A5457E38E5F549C83866DD83C82BA21060537D6F80EE318917C5F`。#152 前的 12 帧归档于 `run-evidence\clock-countdown-issue-152-closure\`（`v22-prev-20260919-main` / `v22-prev-20260919-floating`）。

当前状态（2026-09-18，#153 重拍）：#153 便签列表收口改动了主窗 6 帧的可见构成——倒计时列表条目标题 25→`TypeH2FontSize`（22px 设计坐标，`main-day-countdowns-representative/-stress`）、便签正文补行高 28（BlockLineHeight）与提醒摘要分开、标签芯片单行省略+MaxWidth 200+全文 ToolTip、便签卡内边距/卡间距/日期章节间距消费间距 token、日期选择器选中日期文本改「`yyyy 年 M 月 d 日`」空格中文格式（`main-day-notes-edit`、`main-day-notes-filter-open` 加日期筛选可见标签；`main-day-notes-empty` 无卡片构成不变、`main-day-countdowns-empty` 无列表行不变，两帧与 #152 版逐字节一致）。`UiControls.cs` 弹层 Tab 层内循环、`ThemedDatePickerBehavior` 写回合同日期文本属行为改动，帧构图除上述日期文本外不变。悬浮 4 帧与 #152 版逐字节一致（悬浮无可见日期框；App.xaml/UiControls.cs 同入悬浮 sourceBinding 故随 manifest 重写）。双 manifest 按现役 Aliased+Ideal 口径原目录重拍：主窗聚合 SHA-256 `4A6C432FEE63391C77FF3D96EC29C33668968EE788BF4B27392830A613E0E2BA`，悬浮窗聚合 `6A14228B111C8A60630AE636B07F265CB193A74CFBC3BFAF80815422A67FA3A9`。#153 前的 12 帧归档于 `run-evidence\clock-countdown-issue-153-closure\v22-prev-20260918\`。

当前状态（2026-09-19，#154 重拍）：#154 视觉铬收口改动了全部 12 帧的可见构成与 sourceBinding——管理窗口：筛选漏斗改 `InkBrush` 实心填充（运行时筛选摘要改写 `FilterButtonText`，不再整体替换 Button.Content 销毁图标；v22 旧帧实证描边漏斗在 Aliased 口径下只剩「筛选」两字）、便签页标签旁重复搜索镜移除且输入框内搜索镜统一描边圆环+手柄、最小/关闭改与悬浮窗同几何的矢量路径（12x12 坐标、2px 圆头描边）、页签与状态分段激活胶囊短边一半全圆角（27/22）、倒计时页两处手写体迁至 `CountdownWorkspace` Canvas 装饰层（视觉位置与迁移前一致）；悬浮窗：`TaskCard` 字面色大面积投影移除（旧帧捕获中该投影实际不可见，帧像素仅胶囊区变化）与工作区分段激活胶囊 18（`FloatingHeaderSegmentButton` 模板不再 `RadiusControl`）。`App.xaml`/`FloatingWindow.xaml`/`MainWindow.xaml(.cs)` 同入双 manifest `sourceBinding`，按现役 Aliased+Ideal 口径原目录重拍 12 帧并重写双 manifest：主窗聚合 SHA-256 `8BAA6EBA0A40D788625DE55005C855DF340DACFAE223F10BED0F17251B5C4401`，悬浮窗聚合 `17F08064EE85399862887B2851643C3240A4EC38CF07E639E4599F67E5C5C6A1`。#154 前的 12 帧归档于 `run-evidence\clock-countdown-issue-154-closure\v22-prev-20260919\`。

当前状态（2026-09-19，#155 重拍）：#155 删除撤销、完成确认与脏退出收口为行为流改动——状态条新增常驻 `MainStatusUndoButton`（常态 `Collapsed`，撤销入口只在删除成功后短时出现，捕获帧不含该状态）与 `ConfirmationDialog` 参数化（删除场景默认「确认删除」+ `DangerButton` 不变），运行态 v22 帧可见构成零变化。sourceBinding 覆盖的 `MainWindow.xaml(.cs)`、`UiControls.cs`、`DesktopWorkspace.cs`、`FloatingWindow.Notes.cs(.xaml.cs)` 哈希随本次改动更新（`App.xaml.cs` 等其余行为源由发布契约 IntegrityHashes 钉住，不入 sourceBinding），按现役 Aliased+Ideal 口径原目录重拍 12 帧（与改动前帧逐字节一致，归档于 `run-evidence\clock-countdown-issue-155-closure\v22-prev-20260919\`）并重写双 manifest：主窗聚合 SHA-256 `3D973FAE0D7B5E4F9F6FBDAEB107163A1CBA679087AFA179D3209BB9E1FA7F3E`，悬浮窗聚合 `BFC7ACF1302EFBFABD9E084BDA33C2E2D57908208C704A4ED3B25152CF78C881`。

当前状态（2026-09-19，#156 重拍）：#156 辅助技术与快捷键收口为自动化语义改动——装饰手写体与分段裸数字改 `SilentTextBlock`（无自动化 peer，渲染与 `TextBlock` 完全一致，旧「装饰文案」可访问名废除）、可见标签字段改 `LabeledBy` 标签关系、命令栏数量文字挂 polite 直播区、筛选「清除」改「清除便签日期筛选」、托盘菜单与 `Ctrl+R` 复位入口均为非可见语义，v22 帧可见构成零变化。sourceBinding 覆盖的 `MainWindow.xaml(.cs)`、`FloatingWindow.xaml(.xaml.cs)`、`UiControls.cs` 哈希随本次改动更新，按现役 Aliased+Ideal 口径原目录重拍 12 帧（与改动前帧逐字节一致，归档于 `run-evidence\clock-countdown-issue-156-closure\v22-prev-20260919\`）并重写双 manifest：主窗聚合 SHA-256 `3C6480EB2FE83ACC4BE4383F6093AFBBECFDBF5E55878DB930A011521D265B4D`，悬浮窗聚合 `AB30954E10800E46B863DEC0FBC93DA9E345CA0DEB1DEDF364910440550A2686`。

当前状态（2026-09-19，#157 重拍与探针确定性收口）：#157 把 #151–#156 遗留的 CSharp 套件夹具红 `FloatingReferenceViewportStatesMatch` 定性为探针缺陷并按根因修复：悬浮视口右缘的旧比较口径 `bodyViewport.X + ViewportWidth` 把 WPF 滚动条列宽计入视口，而该列宽在本地样式 `Width=10` 之外仍被系统滚动条度量撑大（Windows 滚动条度量：100% 缩放 17 DIP、125% 缩放 17.6 DIP），使「卡右缘 ≤ 视口右缘 + 0.25」成为会话显示缩放的函数（#157 插桩实测：同一几何下 125% 缩放红 0.55 DIP、100% 缩放仅过 0.05 DIP，§11 漂移族），与产品几何无关。修复后该比较改为确定性口径——滚动内容槽右缘（`bodyViewport.X + ActualWidth`，只由 520 外壳固定边距决定）扣除 §10 设计命中轨宽（模板 `FloatingScrollBarHitRail`，应用自有几何），滚动条隐藏时零扣除；新增 `FloatingViewportGeometryGateIsolationBehavior` 反例门禁钉住该边界语义（卡/印记/快速新增越界即红、滚动条隐藏零扣除、边界容差精确到 0.25），并以源码契约钉住捕获方法不得回到 `ViewportWidth` 口径。真实滚动条列宽可宽于 10 DIP 命中轨（系统度量、透明不可见），属观察事实记录，不改变可视基线几何。v22 全部 12 帧与双 manifest 按现役 Aliased+Ideal 口径在独立隔离会话整套重拍（归档于 `run-evidence\clock-countdown-issue-157-closure\v22-prev-20260919\`）：12 帧与重拍前逐字节一致、双 manifest 内容零差异（主窗聚合 `3C6480EB2FE83ACC4BE4383F6093AFBBECFDBF5E55878DB930A011521D265B4D`、悬浮窗聚合 `AB30954E10800E46B863DEC0FBC93DA9E345CA0DEB1DEDF364910440550A2686`），证明该修复渲染中性、基线稳定档位可复现。提醒中心继续无像素帧，父票对该表面的验收由代码探针覆盖且全真（`ReminderCenterEscapeFocusesAcknowledge`、`ReminderCenterTwoRowActionsLayout`、`ReminderCenterEntryVisualIdentity`、`ReminderCenterTypographyWeightWhitelist`、`ReminderCenterRepresentativeCaptureRendered` 与设置恢复壳 `SystemDialogPrimaryButtonContrast.UsesActiveStaticPalette`），未静默跳过。

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
- **假设**：`完成便签` 与 `打开来源` 在第二行居中并排或按来源动作数量单枚居中，二者都满足本文件契约；实现以居中且不拉伸填满整行为准（#150 收口，不再写左对齐）。
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
- 允许存在的值级重复只有以下 alias（同一原色值挂多个语义角色），不得扩充：`#B84A36` → `ActiveHover` / `MainActionHover`；`#C23E68` → `MainAction` / `PinkAccentStrong`（#199 统一主行动家族）；`#3B190D` → `Ink` / `TagChipText` / `SegmentSelectedForeground`；`#FFFDFC` → `MainActionText` / `PinkBadgeText`；`#FEEFE1` → `SegmentSelected` / `PeachTint`；`#CD623E` → `NoteActiveRail` / `PeachRail`；`#FFF7E8` → `Tag` / `CharacterPanel`；`#F6DFD3` → `NoteTimeline` / `PeachLine`（#146 收口补录：该值级重复在 #131 线工作树演化中已存在（App.xaml 与 `ApplicationTheme.Tokens` 两处同步），清单此前漏记，本条只补记录、不新增重复）。
- XAML 允许 6 位与 8 位十六进制写法并存（如 `#FEE0E4` 与 `#FFFEE0E4`），同步校验按解析后 ARGB 相等判断，不强制统一记法。

### 13.2 排版层级 token

| token | 值（设计坐标 px） | 规格 band | 对应现状 |
| --- | --- | --- | --- |
| `TypeDisplayFontSize` | 44 | 40–48 | 页面主标题层（待界面节点消费） |
| `TypeH1FontSize` | 26 | 24–28 | 区块/章节标题层 |
| `TypeH2FontSize` | 20 | 18–22 | 列表条目、输入内容和紧凑标题层 |
| `TypeBodyFontSize` | 16 | 15–17 | 字段标签（SemiBold）、辅助/摘要和装饰 |
| `TypeCaptionFontSize` | 13 | 12–14 | 紧凑正文、徽章和日期辅助信息 |

- 阶梯值由 #197 固定，主窗口和悬浮窗都按 1:1 DIP 消费；三类窗口都只能从这五个 token 取常态字号，长数值只允许在既有可读下限内收缩。#208 移除了主窗口的整窗 Viewbox，字号和命中区不再乘缩放系数。
- `12px` 以下不得承载关键状态；caption 只承担紧凑正文、徽章和辅助摘要，不单独承载唯一关键状态。
- 字族：`UiFontFamily` 与 `NumeralFontFamily` 统一由 App.xaml 资源提供；数字保持 `Typography.NumeralAlignment=Tabular`；字重只用 Regular/SemiBold（§3）。

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

### 13.3.1 动效 token 与系统开关

动效 token 与控件状态共享应用级资源字典。`ApplicationTheme.Apply` 在系统 `SystemParameters.ClientAreaAnimation` 开启时写入 §9 的六个时长；关闭时写入 `0ms`，并保持相同的目标状态。悬停颜色、按下缩放和 `PopupMotionBehavior` 的透明度/位移都通过动画时钟执行，按压缩放与弹层位移只改 `RenderTransform`，不改 `Width`、`Height`、`Margin` 或边框厚度。每次状态变化都取消旧时钟、读取当前呈现值，再以 `SnapshotAndReplace` 启动新时钟，因此 hover、按压和弹层的连续反转从当前值继续。

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
| `MainActionTextColor` | `#FFFDFC` | `MainActionBrush` | 4.95 | 4.5 | text | PASS | 主行动按钮；同族 hover 5.89、pressed 7.18，悬浮粉按钮 4.95 |
| `PrimaryCountdownNumberColor` | `#C0102F` | `PinkBlockBrush` | 5.06 | 4.5 | text | PASS | 主项分格数字 |
| `InkColor` | `#3B190D` | `SecondaryBlockBrush` | 14.06 | 4.5 | text | PASS | 次项分格数字 |
| `StampPinkTextColor` | `#D41F57` | `StampPinkBrush` | 4.56 | 4.5 | text | PASS | 粉印记 |
| `StampBlueTextColor` | `#1D6BB0` | `StampBlueBrush` | 5.06 | 4.5 | text | PASS | #132 由 `#2F86DB`（3.44）调深 |
| `StampGreenTextColor` | `#26732D` | `StampGreenBrush` | 5.30 | 4.5 | text | PASS | #132 由 `#318939`（3.97）调深 |
| `FloatingDayPrimaryCountdownAccentColor` | `#EE2B46` | `PinkTintSoftBrush` | 3.74 | 3.0 | graphic | PASS | 主项强调轨（非文本）；分格文字由 `PrimaryCountdownNumberColor` 承担（5.06） |
| `InkColor` | `#3B190D` | `PinkBadgeBrush` | 7.60 | 4.5 | text | PASS | “超期待！”徽章文字（#148 换绑，替代白字 2.05） |
| `InkColor` | `#3B190D` | `PeachBadgeBrush` | 8.69 | 4.5 | text | PASS | 激活数量徽章文字（#148 换绑，替代白字 1.79） |
| `BodyColor` | `#6B4A3C` | `PeachBadgeMutedBrush` | 6.09 | 4.5 | text | PASS | 未激活数量徽章文字（#148 换绑，替代 Muted 4.05） |
| `BodyColor` | `#6B4A3C` | `ExpiredBrush` | 6.22 | 4.5 | text | PASS | 到期剩余/辅助文字（#148 换绑，替代删除红 3.97） |
| `PrimaryCountdownNumberColor` | `#C0102F` | `ExpiredBrush` | 4.95 | 4.5 | text | PASS | 到期主项剩余文字（#148） |
| `InkColor` | `#3B190D` | `CoralActionBrush` | 5.62 | 4.5 | text | PASS | 「保存设置」次级实心文字（#151 新增；白字 2.77 不达标故禁用浅珊瑚配白字） |
| `DangerColor` | `#B94D3A` | `StatusBarBrush` | 4.83 | 4.5 | text | PASS | 状态条表面上的错误字（#151；旧画布奶油底上 4.48 不达标） |
| `ActiveColor` | `#E05A4A` | `PanelBrush` | 3.61 | 3.0 | graphic | PASS | 焦点边/复选图形；`PrimaryButton` 底已改绑 `MainActionBrush`（3.61→4.95） |
| `ActiveColor` | `#E05A4A` | `BackgroundBrush` | 3.27 | 3.0 | graphic | PASS | 提醒规则选中圆点/描边（#151 换绑；`IconAccentBrush` 2.13 属装饰不承载选中态） |
| `ActiveColor` | `#E05A4A` | `CardAltBrush` | 3.50 | 3.0 | graphic | PASS | 选中粉底页签焦点环实际邻色（#152；环与粉底间常驻 1px 间隙，`PinkTintBrush` 直贴 2.90 不达标被间隙规避） |
| `MainActionFocusColor` | `#FFD49A` | `PinkAccentStrongBrush` | 3.62 | 3.0 | graphic | PASS | 悬浮选中实心粉工作区胶囊焦点环（#152 换绑暖金；`ActiveBrush` 直贴深粉 1.37 不达标） |
| `DecoTextColor` | `#D9B7B1` | `CanvasBrush` | 1.69 | 豁免 | decorative | 记录 | 手写装饰 |
| `DecoTextSoftColor` | `#E5AE9C` | `BackgroundBrush` | 1.73 | 豁免 | decorative | 记录 | 悬浮手写装饰 |
| `IconAccentColor` | `#F28995` | `CardBrush` | 2.34 | 豁免 | decorative | 记录 | 伴随 labeled 字段的装饰图标 |

本节点完成的修正（新值仍在 §2 采样方向内）：

- `SuccessColor` `#529F8C → #266B5E`；`WarningAccentColor` `#DC6922 → #AD4A10`；`StampBlueTextColor` `#2F86DB → #1D6BB0`；`StampGreenTextColor` `#318939 → #26732D`。
- `PrimaryButton` 底/边 `ActiveBrush → MainActionBrush`（白字对 `#E05A4A` 3.61 不达 4.5）；日历选中日与含选区月份底同步改绑 `MainActionBrush`。`ActiveBrush` 继续承担悬停、按压、焦点边与复选图形（≥3:1）。

#133 换绑收口（消费侧文字换绑，未新增/修改 token 值）：

- 主窗口激活页签文字：`PinkAccentBrush` → `InkBrush`（`PinkTintBrush` 底实测 12.5，达标）；便签页激活分段文字：`PeachRailBrush` → `InkBrush`（`PeachTintBrush` 底实测 14.0，达标）。`ValidationReport.HasSelectedSegmentContrast` 以 4.5:1 达标即过，换绑后保持绿。

#148 收口（消费侧文字换绑与门禁改写，未新增/修改 token 值）：

- “超期待！”徽章文字（悬浮窗）：白 `PinkBadgeTextBrush` → `InkBrush`（`PinkBadgeBrush` 底实测 7.60）；字号 9 → 12 DIP（关键状态下限），并垂直居中于标题行。徽章只挂“距离下班”主项；管理窗口不渲染“距离下班”，列表不再出现该徽章。
- 激活数量徽章文字：白 `MainActionTextBrush` → `InkBrush`（`PeachBadgeBrush` 底实测 8.69）；未激活数量徽章文字：`MutedBrush` → `BodyBrush`（`PeachBadgeMutedBrush` 底实测 6.09）。`ValidationReport.BadgeFollowsSegmentState` 同步改为钉住 `InkBrush`/`BodyBrush` 并用主题公式核对 ≥4.5。
- 到期剩余文字（管理窗口）：次项删除红 `DangerBrush` → `BodyBrush`（`ExpiredBrush` 底实测 6.22；旧红实测 3.97 不达标），主项到期回落 `PrimaryCountdownNumberBrush`（`ExpiredBrush` 底实测 4.95）；「已到期」文案继续显示。悬浮窗到期回落路径同样不再用强调轨色当字。
- 到期卡单位字/辅助文字对比统一由上述语义色承担；装饰手写体与伴随带标签字段的浅粉图标维持 §13.5 装饰豁免，不得拿去当状态。
- `ValidationReport.HasSelectedSegmentContrast` 删除「不达标但匹配方向稿采样对则放过」的旧分支：选中段文字必须以主题公式对实际邻色达到 4.5:1。
- 分格几何：管理窗口与悬浮窗同一套竖长比例（约 2:3）、单位在块外、间距约块宽三分之一；管理窗口单位字固定设计坐标 13px（≈8.5 DIP，落在 caption 阶梯），数值字族例外见 §3（Bahnschrift + 中文回退）。

#152 收口（主窗可操作：焦点、命中与邻色门槛，未新增/修改 token 值）：

- 常规键盘焦点 2px 暖边：`MainKeyboardFocusVisual` 由 3px 收到 `FocusBorderThickness`（2px）；主按钮/置顶只保留模板内 `MainActionFocusBrush` 暖粉焦点环，`ConfigureControls` 不再对其套 `FocusVisualStyle` 第二套更粗描边。
- 主窗本地控件焦点/按压全部改为常驻透明环变色（环厚恒为 `FocusBorderThickness`），悬停/按压/聚焦不再改写边厚挤布局。选中粉底页签环对实际邻色（1px 间隙后的 `CardAltBrush`，3.50）达标；悬浮选中实心粉胶囊环换绑 `MainActionFocusBrush` 暖金（对 `PinkAccentStrongBrush` 3.62）达标；`ValidateKeyboardFocusRingContrast` 扩展覆盖两个邻色场景并按上下文钉唯一期望 token，无旧强调色放过分支。
- 便签完成勾选与提醒帮助命中槽位 38 设计坐标（默认缩放 ≈24.8 DIP ≥ 桌面紧凑下限 24）；筛选芯片高度 36、相邻间距 8，日历翻月按钮 36。悬浮窗 36 下限与头部 3px、滚动条 10px 例外未放宽。
- 接入 `ApplicationTheme.ClientAreaAnimationEnabled`（系统「客户区动画」开关）读取点；#194 将悬停颜色和按下缩放接入 §9 token，关闭系统动画时有效时长为 0。

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
