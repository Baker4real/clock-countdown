---
type: spec
title: validate-only 重复执行点断言映射表
tags: [spec, verification, test-suite, dedup, assertion-map]
intent: 逐条映射六阶段套件内 13 次 C# 完整 validate-only 与 Exercise 演练的断言覆盖、跨执行点逐字重复集与每个删除候选的独有断言归属，作为去重实施的唯一权威输入。
---

# validate-only 重复执行点断言映射表

## Problem Statement

父 PRD（`suite-runtime-compression.md`，#120）的第四个增量要求合并套件内约 13 次 C# 596 字段完整 validate-only 的重复副本，且去重不允许绕过逐条断言映射直接删。本表是唯一权威输入：每个执行点的断言清单、跨执行点的逐字重复集、每个删除候选的独有断言归属（迁移去处或标注有意双轨保留）。

本表为只读调查产物，不改任何代码。行号锚点以 2026-09-14 工作树为准（与 #124 给出的锚点一致）；工作树含未提交的 v22 视觉改动，行号随后续编辑漂移时以符号名复核。

> 2026-10-24 后续：C2 段对 `$adapterValidation` 的无条件 `Assert-Equal $true` 布尔断言、字面量字段循环、字段数与 `name:type` 形状 SHA 钉值已移入 C# 验证事实（`ValidationReport` 的 `Must(值, 含义)`，由 `Seal` 校验并派生 `Valid`），见 `docs/架构与语言迁移.md`。桌面工作区移入 `MinminCountdown.Core` 后，C2 的 DesktopWorkspace interface 行为与 `DesktopWorkspaceModuleDeep` 兼容字段退出报告，改由 `MinminCountdown.Verification` 进程内 runner 验证。本表其余映射与行号仍是 2026-09-14 的调查快照。

## 调查口径

- “完整 validate-only”指经 adapter（`敏敏子倒计时.ps1`，逐字透传 `--validate-only` 与 exercise 开关到 `app\MinminCountdown.exe`，见 adapter :29-66）或直接调 exe/DLL 跑出的同一份 596 字段 JSON 验证报告。
- 字段级映射用机械提取复核：对各执行点的断言变量抽取 `$<var>.<Field>` 顶层字段并合并字面量字段清单（`foreach ($fieldName in @(...))` 循环），再做集合交/差运算；计数为近似值（±2，形状字符串引用如 `FloatingTitleTextRequiredWidth:Number` 不按字段访问计入）。
- “逐字重复”指同一报告字段在两个执行点都被断言（断言写法因脚本风格不同：CSharp 用 `Assert-Equal`，Ui 用 `if (-not ...) { throw }`），被测行为由同一次 C# 内部演练产生，语义逐字等价。
- 核心事实：596 字段报告形状与输入 settings 无关（shape SHA-256 固定为 `0D583E94...BAF229`，CSharp :4171）；除显式标注“输入相关”的字段外，行为字段全部由 C# 宿主内部演练（`ValidationExercises.cs`）构造场景计算，因此同一字段在任何执行点的取值语义相同，断言可以跨执行点迁移。

## 执行点总览（13 次完整 validate-only）

| 编号 | 阶段 | 锚点 | 调用形态 | 输入 settings | 断言变量 |
| --- | --- | --- | --- | --- | --- |
| C1 | CSharp | `Countdown.CSharp.Tests.ps1:3841` | 直接调 exe `--validate-only` | `$roundTripPath`（C# round-trip 产物） | 无（仅文件哈希） |
| C2 | CSharp | `Countdown.CSharp.Tests.ps1:3852` | adapter `-ValidateOnly` | `$themeExercisePath`（旧 Auto 主题） | `$adapterValidation` |
| C3 | CSharp | `Countdown.CSharp.Tests.ps1:3979` | adapter `-ValidateOnly` | `$nightMigrationPath`（旧 Night 主题） | `$nightMigrationReport` |
| C4 | CSharp | `Countdown.CSharp.Tests.ps1:4004` | adapter `-ValidateOnly -ExercisePersistenceFailures` | `$persistenceExercisePath`（tmp 被目录阻塞） | `$persistenceFailureValidation` |
| C5 | CSharp | `Countdown.CSharp.Tests.ps1:4785` | adapter `-ValidateOnly -ExerciseNoteAdd` | `$noteExercisePath` | `$noteExercise` |
| U1 | Ui | `Countdown.Ui.Tests.ps1:132` | adapter `-ValidateOnly` | 全新临时文件（默认态） | `$validation` |
| U2 | Ui | `Countdown.Ui.Tests.ps1:1164` | adapter `-ValidateOnly` | `$testSettingsPath`（schema-v10  populated fixture：2 倒计时 + 1 标签 + 5 便签） | `$listValidation` |
| U3 | Ui | `Countdown.Ui.Tests.ps1:1243` | adapter `-ValidateOnly -ExerciseNoteAdd` | `$noteAddSettingsPath`（U2 fixture 副本） | `$noteAddValidation` |
| U4 | Ui | `Countdown.Ui.Tests.ps1:1277` | adapter `-ValidateOnly -ExerciseTagInteractions` | `$tagInteractionSettingsPath`（U2 fixture 副本） | `$tagInteractionValidation` |
| U5 | Ui | `Countdown.Ui.Tests.ps1:1306` | adapter `-ValidateOnly -ExercisePersistenceFailures` | `$persistenceFailureUnblockedSettingsPath`（tmp 未阻塞，预期失败） | 无（仅退出码与哈希） |
| U6 | Ui | `Countdown.Ui.Tests.ps1:1316` | adapter `-ValidateOnly -ExercisePersistenceFailures` | `$persistenceFailureSettingsPath`（tmp 被目录阻塞） | `$persistenceFailureValidation` |
| U7 | Ui | `Countdown.Ui.Tests.ps1:1346` | adapter `-ValidateOnly` | `$legacyV3SettingsPath`（schema-v3 旧设置） | `$legacyV3Validation` |
| S1 | SourceValidation | `Invoke-CountdownSourceValidation.ps1:57` | `dotnet` 直接调源码树 DLL `--validate-only` | 受管临时根新文件 | `$report` |

## 逐点断言映射

### C1 — CSharp :3841（直接 exe，roundTripPath）

断言清单（1 条）：

1. :3848 运行前后 `$roundTripPath` SHA-256 不变（“应用 validate-only 保持设置不变”）。

备注：套件主路径中唯一不经 adapter 直接调 exe 的 validate-only；同一文件的哈希不变断言在 :4782 再出现一次（措辞为“adapter validate-only 保持设置不变”）。

### C2 — CSharp :3852（adapter，themeExercisePath）★ canonical

断言分两段物理分布：:3894-3965（报告解析、JSON 类型清点、596 字段形状与 SHA、托盘事件不伪造、ValidationResourcesReleased 早于 ValidationCompleted、Auto→Day 主题迁移 4 条）与 :4060-4782（主断言面，约 460 行 `Assert-Equal`）。共断言约 350 个互不相同的顶层报告字段，外加嵌套测量数组与 evaluator 子门。覆盖族（C2 独有、其他执行点未断言的重点）：

- 596 字段形状：顶层键唯一、字段数 = 596、`name:type` 形状 SHA-256 钉死（:4072-4074, :4171）；不输出 DeepModuleCount/DeepModuleInterfaces（:4082）。
- 主窗口视觉基线 8 帧：manifest 请求/可用/十六门禁/负向夹具集合、8 帧 capture ID 与路径绑定、PNG/状态指纹唯一、alpha 全不透明、固定全画布 ROI、像素指标阈值、Empty/Representative/Edit/FilterOpen/Stress 五态数据指纹、Stress 标题单行测量、便签卡提醒摘要 12 DIP（:4094-4161）。
- 悬浮窗视觉基线 4 帧：十二门禁与负向夹具、4 帧 capture 与 manifest 逐帧绑定、像素阈值、长便签新鲜度 8 门禁与 8 负夹具、长便签 top/bottom 两态测量与 PNG/指纹唯一（:4247-4307）；长便签 8 字段循环断言（:4253-4264）。
- 悬浮窗滚动条 8 字段循环 + 三态测量 + 滚轮/拖动偏移 + 截图清单（:4358-4380）。
- 提醒中心 33 字段循环（:4742-4776）与持久化失败退避系列（:4733-4741）。
- 主窗口键盘 Tab 双工作区完整路径正反对称、7 快捷键真实 KeyEvent 诊断（:4536-4596）；结构变化播报三门与合成 FailedChecks 拒绝（:4597-4628）；次级窗口字号三门（:4629-4675）；删除确认八门与两态测量（:4677-4732）。
- interface/deep-module 行为：DesktopWorkspace / ReminderEditing / StartupAcceptanceProtocol / HolidayCalendar / ValidationReport 五个 interface 行为与四个 ModuleDeep 兼容字段（:4061-4062, :4067-4068, :4083-4086）。
- 合同行为字段：ValidationReportContractBehavior、ValidateOnlySettingsIsolationBehavior、UiQualityGateIsolationBehavior（:4069-4071）。
- 倒计时剩余时间 5 样本与增量刷新 10 子字段（:4195-4215）；主窗口便签完成 24px 模板与交互（:4216-4230）。
- 提醒规则：单选组三态映射、日历提醒 Ical.Net 闭锁、共享编辑器入口、悬浮紧凑入口、跨天选择、一次性生命周期、ContentRendered 确认（:4063-4066, :4530-4535）。
- 主题与 DESIGN token：悬浮/主窗口色板逐字值、快速新增 palette 绑定逐键、单边框门两态像素测量、对比度系列（:4413-4520）。
- 报告尾部再断言 `$roundTripPath` 哈希不变（:4782）。

### C3 — CSharp :3979（adapter，Night→Day 迁移）

断言清单（4 条，:3994-3997）：退出码非零即 throw（:3991-3993）；设置文件哈希不变；`FloatingThemeBoundaryModes` = Day；`MainThemeBackgroundColors` = `#FFFDF0E4`。独有性来自输入：旧 `themePreference=Night` 设置必须按日间解释且不强制回写。

### C4 — CSharp :4004（ExercisePersistenceFailures，预期通过）

断言清单（:4023-4059，约 13 条）：退出码 = 0；报告 `Valid`；8 个持久化失败字段逐一为真（`CountdownSaveFailurePreservesState`、`PreferenceSaveFailurePreservesState`、`CountdownSaveFailureRetainsDraft`、`PreferenceSaveFailureRetainsDraft`、`TagSaveFailurePreservesState`、`TagSaveFailureRetainsDraft`、`TagSaveFailureRetainsFilter`、`PersistenceFailureTempPathRemainsDirectory`）；诊断日志无 `FatalUnhandledException`；`ValidationResourcesReleased` 存在且早于 `ValidationCompleted`；设置文件哈希不变。

### C5 — CSharp :4785（ExerciseNoteAdd）

断言清单（:4798-4809，10 条）：`NoteAddViaClick`、`NoteReminderViaSelector`、`FloatingCrossDayReminderViaClick`、`MainNoReminderViaClick`、`MainOnceReminderViaClick`、`MainRepeatingReminderViaClick`、`FloatingNoReminderViaClick`、`FloatingOnceReminderViaClick`、`FloatingRepeatingReminderViaClick`；隔离设置中真实写入文本为“验证快速跨天提醒”的便签恰一条。

### U1 — Ui :132（adapter，默认态临时 settings）→ `$validation`

断言面 :144-1118（约 330 行 `if (-not ...) { throw }`）+ :1395 报告字段 `ApplicationValidation = $validation.Valid`。共断言 363 个互不相同的顶层报告字段：

- 与 C2 重复：254 字段（逐字重复集 DS-1，清单见附录 A）。
- U1 独有：109 字段（清单与迁移归属见附录 B），覆盖族：DatePicker 文本点击/主题弹层系列（:144, :354-358）、紧凑布局实测宽度系列（:359-367）、悬浮窗存在性与工作区底部锚定系列（:371-379）、悬浮页签文字完整性与管理按钮（:551-557）、控件存在性 `Has*` 系列（:557-562, :580-582, :588）、提醒派发与一次性语义（:565-566）、官方角色背景独占与低对比（:578-579）、旧版迁移/重名/非法便签/悬空标签拒绝（:584-587）、弹层与空态门禁的负向隔离（:606, :612）、草稿保留与设置保存回显（:641-643）、隐藏编辑器提交禁用与工作区页签语义（:646-647）、标签 UI 无障碍与创建流程（:648-654）、ComboBox 下拉与 DatePicker 日历主题（:658-664）、日历头部文字适配（:665-666）、原始色绕过禁令 `NoRawFloatingThemeBypass` 与默认系统表面禁令 `NoDefaultSystemSurfaces`（:670, 附录 B）、主题交互态与有效对比度（:787-788, :1104）、键盘焦点基线证据版本钉死（:912-917）、危险按钮对比度与破坏性确认安全默认（:919-920）、紧凑操作目标尺寸与表单可访问名/live region（:1106-1107, :1118）。
- 非报告断言：Ui 开头 :81-87 的 `ConvertTo-CountdownDateText` 直接格式化断言（不依赖任何 validate-only 运行，随脚本保留）。

### U2 — Ui :1164（adapter，populated schema-v10 fixture）→ `$listValidation`

断言清单（:1167-1236，约 50 条）。输入相关（独有，26 字段）：`LoadedCountdowns=2` 与 `SortedNames` 顺序（:1169-1170）、`PersistenceRoundTrip`/`TagPersistenceRoundTrip`/`NotePersistenceRoundTrip`（:1171-1173, :1194）、`LoadedTags=1`/`LoadedNotes=5`/`ActiveNoteCount=3`/`CompletedNoteCount=2`（:1172-1175）、便签动作/状态标签（:1195-1196）、`HasNoteDateControls`/`NoteDatesRendered`/`NotesGroupedByDay`（:1201-1203）、`SortedNoteTexts` 逐字顺序（:1204-1206）、`RenderedManagerNoteIds`/`RenderedFloatingNoteIds` 逐字 ID 序（:1207-1214）、`NoteDateDefaultsTrackBeijingDay`/`NoteDateChoicesSurviveBeijingDayChange`（:1219-1220）、`CompletedNotesCollapsed`（:1221）、设置哈希不变（:1222-1223）、`SettingsWereMigrated=false`（:1224）、`CardActionsBoundByTag`（:1225）、`FloatingCountdownCount=1` 与 `FloatingCountdownTexts` 单位格式（:1226-1227）、`FloatingIncomeText` 文案匹配（:1228）、`FloatingEmptyStateTextIsolationBehavior`（:1234）。另有动态行 Tab 遍历对称断言（:1176-1193，依赖 fixture 的真实行）。与 C2 重复 14 字段（DS-5：便签分段/时间轴/披露 token、折叠三字段、排序、`ManagerNoteRowsFollowSortOrder`、FloatingNoteSearch 五子字段、收入隐藏/恢复、`MainKeyboardBehavior` 路径语义）。

### U3 — Ui :1243（ExerciseNoteAdd）→ `$noteAddValidation`

断言清单（:1246-1274，16 条）。与 C5 逐字重复 8 字段（DS-3）：`NoteAddViaClick`、`NoteReminderViaSelector`、`Main/FloatingNo/Once/RepeatingReminderViaClick`（6 个，:1273-1274 两行断言）。U3 独有 8 项：5 个日期校验字段（`NoteDateRequired`、`NewNoteRejectsEmptyDate`、`UndatedNoteEditSavesWithoutDate`、`DatedNoteEditRejectsClearedDate`、`NoteDateValidationMessagesDistinguished`，:1248-1252）、`NoteDateViaPicker`（:1254）、`NoteStatusViaCheckbox`（:1256）、落库便签结构断言（文本“验证点击添加便签” + 日期为北京 +2 天 + 恰好一条 fixedInterval 30 分钟规则，:1257-1272）。

### U4 — Ui :1277（ExerciseTagInteractions）→ `$tagInteractionValidation`

全套件唯一执行点。断言清单（:1280-1302，19 条）：`TagInteractionsExecuted`、`TagCreationAndAssociation`、`TagAnyAllFilter`、`TagEditorSummaryIsCompact`、`TagSearchCreateAvoidsDefaultSubmit`、`TagUntaggedAndCountdownStatus`、`TagNoteStatusSearchAndCompletion`、`NoteDateFilterControls`、`NoteDateFilterBehavior`、`TagFloatingNoteInheritsTags`、`TagFloatingNoteDateViaPicker`、`TagFloatingNotePopupOnDemand`、`TagFloatingNoteCanOverrideTags`、`TagNoteEditingPersistsTags`、`TagNoteEditingPersistsDate`、`TagRenameAndDeleteDetachOnly`；落库证据：schema v10、Auto 主题保留、标签库 ≥2、记录数保留（:1298-1302）。

### U5 — Ui :1306（ExercisePersistenceFailures，tmp 未阻塞，预期失败）

断言清单（:1309-1311，3 条）：退出码必须非零（未阻塞的临时路径必须被拒绝）；设置文件前后哈希不变。全套件唯一预期失败路径。

### U6 — Ui :1316（ExercisePersistenceFailures，tmp 阻塞，预期通过）

断言清单（:1319-1331，10 条）：退出码 = 0；与 C4 逐字重复的 8 字段（DS-2，:1321-1328）；磁盘证据：阻塞 tmp 目录在运行后仍为目录（:1329）；设置文件哈希不变（:1330-1331）。

### U7 — Ui :1346（adapter，schema-v3 旧设置）

断言清单（:1349-1359，7 条）：`SettingsWereMigrated=true`；`LoadedCountdowns=1` 且 `SortedNames` 保留；`LoadedNotes=0`；三个 RoundTrip 均真（按 schema-v10 序列化）；v3 偏好逐字保留（Minute/12345/08:30/17:30）；执行副本落盘为 schema v10（:1356-1357）；v3 源 fixture 哈希不变（:1358-1359）。全套件唯一 v3 端到端迁移执行点。

### S1 — SourceValidation :57（dotnet 源码树 DLL）

断言清单（:62-73 + :40-51/:97）：退出码 = 0；报告 `Valid`；`CalendarReminderBehavior` 为真；仓库根 `settings.json` 快照（存在性/长度/LastWriteTimeUtc/SHA-256）前后不变（finally 闭锁）。独有性：唯一面向 `src\MinminCountdown\bin\Release\net10.0-windows\MinminCountdown.dll`（`dotnet build` 中间产物，非 R2R 单文件 exe）的执行点，且唯一带根设置闭锁。

## 跨执行点逐字重复集

- DS-1（U1 ∩ C2，254 字段）：完整字段清单见附录 A。这些是 596 字段报告行为字段在两个完整 validate-only 运行上的双重断言；删除任一副本时零迁移（另一副本已覆盖）。
- DS-2（ExercisePersistenceFailures 8 字段，C4 :4026-4037 ≡ U6 :1321-1328）：`CountdownSaveFailurePreservesState`、`PreferenceSaveFailurePreservesState`、`CountdownSaveFailureRetainsDraft`、`PreferenceSaveFailureRetainsDraft`、`TagSaveFailurePreservesState`、`TagSaveFailureRetainsDraft`、`TagSaveFailureRetainsFilter`、`PersistenceFailureTempPathRemainsDirectory`。
- DS-3（ExerciseNoteAdd 8 字段，C5 :4799-4807 ≡ U3 :1253-1255/:1273-1274）：`NoteAddViaClick`、`NoteReminderViaSelector`、`MainNoReminderViaClick`、`MainOnceReminderViaClick`、`MainRepeatingReminderViaClick`、`FloatingNoReminderViaClick`、`FloatingOnceReminderViaClick`、`FloatingRepeatingReminderViaClick`。
- DS-4（设置隔离哈希守卫，10 处）：C1 :3848、C2 :3962/:4782、C3 :3994、C4 :4059、U2 :1223、U5 :1311、U6 :1331、U7 :1359、S1 :40-51 根快照。语义相同但各自绑定本执行点的输入文件；随运行删除自然消失，无需迁移。
- DS-5（U2 ∩ C2，14 字段）：`NoteStatusSegmentSwitch`、`NoteDateTimelineVisuals`、`NoteDisclosureAccessibility`、`NoteDisclosureThemeTokens`、`NoteDateSectionsCanCollapse`、`NoteDateSectionsAreIndependent`、`NoteDateSectionStateSurvivesRefresh`、`NoteStatusSegmentsSwitchExclusively`、`ManagerNoteRowsFollowSortOrder`、`FloatingNotesKeepExistingSortOrder`、`FloatingNoteSearch`（5 子字段）、`FloatingIncomeHiddenWithoutSalary`、`FloatingIncomeRestoredWithSalary`、`MainKeyboardBehavior`（Tab 路径语义）。U2 的取值依赖其 populated fixture（如动态行遍历），保留 U2 时本集合自然双轨，不迁移。
- DS-6（主题日间边界与色板逐字值）：C2 :3964-3965/:4413-4416/:4469-4471、C3 :3996-3997、U1 :671-699/:761-762。同一期望常量（Day、`#FFFDF0E4`、`#FFFEFCFB`、`#FFB9794E`、DayRaster、`#FFFFF7E8`、opacity 8）在不同输入下断言；C3 的 Night 输入语义独有，不并入。

## 删除候选与独有断言归属

| 候选 | 裁决建议 | 独有断言与归属 |
| --- | --- | --- |
| C1（CSharp :3841） | 可删 | 唯一断言（哈希不变）已由 :4782 对同一文件逐字覆盖，零迁移。附带语义：套件主路径唯一“绕过 adapter 直接调 exe”的烟测随之消失；若维护者要保留该语义，标注“有意双轨保留”，否则删除。 |
| C3（CSharp :3979） | 保留 | 非逐字重复：Night 输入迁移语义（3 条断言）无其他执行点覆盖。 |
| C4 vs U6（持久化失败预期通过 ×2） | 删 U6，保 C4 | 三遍保一对（PRD user story 19）：U5 是唯一预期失败路径必须保留；预期通过副本中 C4 断言为 U6 超集（多退出码、`Valid`、无 `FatalUnhandledException`、资源释放事件序 4 条）。U6 独有的 1 条磁盘证据断言（:1329 阻塞 tmp 目录仍在）迁入 C4——在 C4 的 `finally` 移除 `$blockedTemporaryPath` 之前（:4018-4021）补一行 `Test-Path -PathType Container` 断言。DS-2 八字段随 U6 删除零迁移。 |
| C5 vs U3（ExerciseNoteAdd ×2） | 删 U3，保 C5 | 两遍保一遍（PRD）。U3 独有 8 项迁入 C5 断言面（CSharp :4798-4809 之后）：5 个日期校验字段、`NoteDateViaPicker`、`NoteStatusViaCheckbox`、落库便签结构断言（文本/日期/fixedInterval 30 分钟规则）。C5 独有的 `FloatingCrossDayReminderViaClick` 与“验证快速跨天提醒”落库断言随保留自然覆盖；DS-3 八字段零迁移。备选：删 C5 保 U3 只需迁移 2 项，但违背“CSharp 为 canonical 执行点”（PRD user story 17），不采用。 |
| U1（Ui :132） | 可删（主收益点） | DS-1 254 字段零迁移。109 个 U1 独有字段断言（附录 B）逐条迁入 CSharp 段 C2 断言面（:4060-4782 区间内同名字段补 `Assert-Equal`）；Ui 报告字段 `ApplicationValidation`（:1395）改由 C2 结果供给或随报告简化移除。迁移前提：109 字段均为 C# 内部演练计算字段、与输入 settings 无关（命名与语义证据见附录 B；实施期以一次试运行逐字段比对 U1 原值与 C2 取值确认，发现输入相关即回退该字段迁移并保留 U1）。 |
| U2（Ui :1164） | 保留（有意双轨） | 26 个输入相关字段断言（清单见附录 C）依赖 populated fixture 的数据形态；迁入 C2 需连 fixture 一起搬，成本高于收益。DS-5 与 C2 的重复随保留双轨。 |
| U4（Ui :1277） | 保留 | 全套件唯一 ExerciseTagInteractions 执行点。 |
| U5（Ui :1306） | 保留 | 唯一预期失败路径（未阻塞 tmp 必须拒绝）。 |
| U7（Ui :1346） | 保留 | 唯一 schema-v3 端到端迁移执行点（`SettingsWereMigrated=true` 只在 v3 输入下可达）。 |
| S1（SourceValidation :57） | 保留（有意双轨） | 唯一面向源码树 DLL 产物（非 R2R exe）且唯一带仓库根 `settings.json` 快照闭锁；`Valid`/`CalendarReminderBehavior` 与 C2 重复但阶段成本极小（SourceValidation 中位 37s，验证只是其中一部分）。 |

去重收益核算：删除 C1、U1、U6、U3 共 4 次完整 validate-only；按 PRD 推断单次 15-25 秒，预计省 60-100 秒，落在 PRD 估计区间（60-120 秒）内。

计数门禁对齐（PRD user story 20/21）：删除副本后 CSharp 阶段 Pass 计数只能上升（净增 109 + 8 + 1 条迁入断言），Core 不变；Ui 阶段因 :1392 `Passed = 152` 为硬编码，去重落地时必须同步改为实际计数，首次实测值即为新基线——Ui 计数不适用“不低于旧基线”解读，适用 user story 21。

## 不在去重范围的相邻执行（有意保留，防止误删）

- Ui :97-110 `-ExerciseFatalDiagnostic`：预期失败的致命诊断演练，不产出 596 字段断言面。
- Ui :115-130 `Start-CountdownApplication -ValidateOnly`（`Countdown.App.psm1` 模块直调）：旧 PowerShell WPF 宿主自身验证，PRD user story 18 明确保留；其报告不是 C# 596 字段报告。
- Ui :146-223 运行时集成 harness 与 :225-352 弹层 UIA 段：真实窗口回执与 UIA 驱动，非 validate-only（ExitAfterReadyForTest 等待属 #122 范围）。
- CSharp :4811-5097 交互段：启动请求 fail-closed、schema v9 迁移交互验收、ExitAfterReadyForTest（同属 #122 范围）。
- CSharp :1854 `--validate-only --exercise-fatal-diagnostic`：InstanceActivationOnly 的 ValidateExercise 探针模式，非默认套件路径。
- Core :5988 `Install-Countdown.ps1 -ValidateOnly`：安装器验证，与 InstallValidate 独立阶段为有意双轨（PRD user story 28，#125 范围）；Core 内其余 `validate-only` 命中均为 fixture 桩或报告合同单测，无真实 C# 完整 validate-only 运行。

## 附录 A：DS-1 逐字重复字段集（U1 ∩ C2，254 字段）

机械提取口径：两执行点断言变量顶层字段访问 + 字面量循环字段清单的交集，按字母序。

```
ActionForegroundsUseMainActionText, AuditContrastGateIsolationBehavior,
CalendarNavigationAccessibleNames, ComboBoxArrowIsNotTabStop, ComboBoxTextClick,
CompactInputHorizontalPaddingAligned, ConfirmationDialog,
ConfirmationDialogGateIsolationBehavior, ConfirmationDialogLongestBodyReachable,
ConfirmationDialogLongestButtonsVisible, ConfirmationDialogSafeDefaultActions,
CountdownCardActionsIncludeRecordName, CountdownEmptyKeyboardResetRestoresResults,
CountdownEmptyPointerResetRestoresResults, CountdownEmptyStateContrastMeasurements,
CountdownEmptyStateTextContrast, CountdownEmptyStatesDistinguished,
CountdownExpiryVisualsFlipTogether, CountdownIncrementalRefresh,
CountdownIncrementalRefreshGateIsolationBehavior,
CountdownIncrementalRefreshIdentityMatches, CountdownNoResultsResetWorks,
CountdownRemainingGateIsolationBehavior, CountdownRemainingLayoutMatches,
CountdownRemainingSamples, CountdownRemainingUnitSpacingMatches,
DatePickerOpenCalendarAccessibleName, DatePickerOpenCalendarAccessibleNames,
DisabledSelectionControlsUseButtonOpacity, FilterPopupScrollsLongTagList,
FloatingCheckboxHitCoversSlot, FloatingCorrectedFieldErrorsClearLiveMessage,
FloatingCountdownSelectionSurvivesRefresh, FloatingDatePickerKeyboardLifecycleWorks,
FloatingDateStampDensityMatches, FloatingDateStampTypographyMatches,
FloatingDayRasterCardStatesMatch, FloatingDayRasterComponentAssetNames,
FloatingDayRasterDecorationsAreNonInteractive, FloatingDayRasterShellAndHeaderLoaded,
FloatingDragAttemptClampsToWorkArea, FloatingDragHandleExcludesInteractiveControls,
FloatingDragHandleReceivesPointerGestures, FloatingDragHandleRequestsNativeWindowDrag,
FloatingDragSurfaceCoversWindowBackground, FloatingDynamicCardContentRemainsWpf,
FloatingDynamicHeaderControlsRemainWpf, FloatingFieldErrorsAppearAtSource,
FloatingHeaderIconAccessibility, FloatingHeaderIconClickCommandsWork,
FloatingInitialWorkspaceStateMatches, FloatingInteractiveControlsDoNotRequestWindowDrag,
FloatingLongNotesBodyHasNoManualLineBreaks, FloatingLongNotesCaptures,
FloatingLongNotesLayoutValid, FloatingLongNotesMeasurements,
FloatingLongNotesNaturalWraps, FloatingLongNotesOuterScrollable,
FloatingLongNotesSafeAreaMatches, FloatingLongNotesTopBottomReachable,
FloatingLongNotesTypographyMatches, FloatingLongNotesUsesOnlyOuterScroller,
FloatingMascotPresentationModes, FloatingNoteFailureAppearsInSourceCard,
FloatingNoteInputScrolls, FloatingNoteInputWraps, FloatingNoteSearch,
FloatingNoteSearchScenariosPass, FloatingPlushMascotsLoaded,
FloatingPlushSceneFollowsDayTheme, FloatingPlushSceneIntegratesWithPanel,
FloatingPrimaryCountdownAccentSeparatedFromPlushGlow,
FloatingPrimaryCountdownContrastGateIsolationBehavior,
FloatingPrimaryCountdownContrastMeasurements, FloatingPrimaryCountdownTextContrast,
FloatingPrimaryDeadlineTextFullyVisible, FloatingQuickAddAlignsWithNoteCards,
FloatingQuickAddDayExpandedScrolledCaptureWritten, FloatingQuickAddDayPaletteBindingsMatch,
FloatingQuickAddDayResourceChecks, FloatingQuickAddDayResourcesFollowLocalTheme,
FloatingQuickAddIndependentFromApplicationTheme, FloatingQuickAddLocalActiveColor,
FloatingQuickAddLocalCanvasColor, FloatingQuickAddPaletteBindingIsolationBehavior,
FloatingQuickAddPaletteBindings, FloatingQuickAddThemeResourceIsolationBehavior,
FloatingRasterMascotPresentationMatches, FloatingRasterSurfacesFollowTheme,
FloatingReferenceAspectMatches, FloatingReferenceBaselineCaptures,
FloatingReferenceBaselineDiagnostics, FloatingReferenceBaselineGateIsolationBehavior,
FloatingReferenceBaselineGates, FloatingReferenceBaselineManifestAvailable,
FloatingReferenceBaselineManifestPath, FloatingReferenceBaselineNegativeFixtures,
FloatingReferenceBaselineRequested, FloatingReferenceBaselineValidated,
FloatingReferenceCaptureWritten, FloatingReferenceContentOverlapCount,
FloatingReferenceContentSafeAreaMatches, FloatingReferenceFreshnessGateIsolationBehavior,
FloatingReferenceFreshnessGates, FloatingReferenceFreshnessNegativeFixtures,
FloatingReferenceMascotGeometryMatches, FloatingReferencePanelGeometryMatches,
FloatingReferenceScenarioMatches, FloatingReferenceViewportStatesMatch,
FloatingReminderRulesEntryStaysCompact, FloatingResolvedNoteCardErrorDoesNotReturn,
FloatingScrollBarContentRemainsSafe, FloatingScrollBarDragOffsets,
FloatingScrollBarGateIsolationBehavior, FloatingScrollBarHitTargetUsable,
FloatingScrollBarInputPathsWork, FloatingScrollBarLocalStyleScoped,
FloatingScrollBarStateCapturesReady, FloatingScrollBarStateMeasurements,
FloatingScrollBarUsesWarmThemeTokens, FloatingScrollBarVisualWeightProgression,
FloatingScrollBarWheelOffsets, FloatingSearchHintMatchesInputHeight,
FloatingSearchSlotRightIs396, FloatingSecondaryRemainingGateIsolationBehavior,
FloatingSecondaryRemainingLayoutMatches, FloatingSecondaryRemainingSamples,
FloatingSecondaryRemainingTextFullyVisible, FloatingSecondaryRemainingUnitSpacingMatches,
FloatingSyntheticDragDoesNotClaimPhysicalCompletion, FloatingThemeBoundaryModes,
FloatingThemePanelColors, FloatingThemePlushGlowColors, FloatingThemeTextContrast,
FloatingTitleTextFits, FloatingTitleTextHasVerticalClearance,
FloatingTitleUsesSemiBoldWeight, FloatingTrayButtonAccessibility,
FloatingTrayExitRequestsApplicationExit, FloatingTrayFailedRestoreKeepsTrayStateAndAllowsRetry,
FloatingTrayMinimizeHidesWindowAndShowsIcon, FloatingTrayProductionControllerActive,
FloatingTrayRestoreShowsWindowAndHidesIcon, FloatingTypographyWeightWhitelist,
FloatingViewportScrollAndFocusRestored,
FloatingWindowManualPositionPersistsAndRemainsVisibleAfterRefresh,
FloatingWorkdayCountdownUsesPrimaryVisual, FloatingWorkspaceSwitchingStaysSynchronized,
KeyboardFocusRingContrast, LiveDatePickerPopupIsThemed, MainActionTextContrast,
MainCharacterPanelDecorationsAreNonInteractive, MainCharacterPanelDockCount,
MainCharacterPanelDocksFollowDayTheme, MainCharacterPanelDocksIntegrateWithPanel,
MainCheckboxHitCoversSlot, MainDaySingleBorder, MainDaySingleBorderGate,
MainDaySingleBorderGateIsolationBehavior, MainDaySingleBorderNegativeFixtures,
MainDynamicCountdownCardSampleCount, MainDynamicCountdownCardsUseThemeResources,
MainDynamicNoteCardSampleCount, MainDynamicNoteCardsUseThemeResources,
MainInitialFocusIsSafeAndDeterministic, MainKeyboardBehavior,
MainKeyboardNavigationGateIsolationBehavior, MainKeyboardNavigationGates,
MainKeyboardPathNegativeFixtures, MainMinimumSizeMatchesDefault,
MainNoteCompletionGateIsolationBehavior, MainNoteCompletionInteractionBehavior,
MainNoteCompletionMeasurements, MainNoteCompletionOtherUserSizesUnchanged,
MainNoteCompletionStyleScoped, MainNoteCompletionTargetSizeAndBehavior,
MainNoteEditorFieldsDoNotOverlap, MainNoteEditorLabelsVisible,
MainReferenceDecorationAssetsLoaded, MainReferenceDecorationLayerOrderMatches,
MainReferenceDecorationsAreNonInteractive, MainReferenceDecorationsFollowDayTheme,
MainReferenceDecorationsShareScaleAtMinimumAndWideSizes,
MainShortcutGateIsolationBehavior, MainShortcutGates, MainShortcutScopeIsSafe,
MainStructuralAnnouncementGateIsolationBehavior, MainStructuralAnnouncementGates,
MainStructuralAnnouncements, MainStructuralLiveRegionsUsePoliteSemantics,
MainTabSequenceStartsWithWorkspaceControls, MainThemeBackgroundColors,
MainThemeCharacterOpacities, MainThemeCharacterPanelColors,
MainThemeReminderLabelFollowsTheme, MainWorkspaceResourcesFollowDayTheme,
NegativeMonthlySalaryRejectedKeepsDraft, NoteCancelFitsEditorPanel,
NoteEmptyStateContrastMeasurements, NoteEmptyStateTextContrast,
NoteEmptyStatesDistinguished, NoteNoResultsResetWorks, NoteSearchSyncsFromSharedFilter,
NoteStatusBadgeTokensFollowState, NoteVisualTokenGateIsolationBehavior,
OfficialCharacterBackgroundLoaded, PopupAnimationIsInstant,
PopupEscapeFromTriggerClosesAndKeepsFocus, PopupKeyboardLifecycleWorks,
PopupOutsideFocusPreserved, PopupRightInsetIsEightFromCanvas,
PopupTriggerSecondClickCloses, PrimaryButtonHoverTextContrast,
ReminderCenterAcknowledgeAllBackoffRetryCompletesBatch,
ReminderCenterAcknowledgeAllFailureRetainsAllAtomically,
ReminderCenterAcknowledgeAllMatchesSingleSemantics,
ReminderCenterAcknowledgeAllWritesAllDispositions,
ReminderCenterCustomSnoozeCalendarBehavior, ReminderCenterCustomSnoozeControlsVisibility,
ReminderCenterEntryVisualIdentity, ReminderCenterInitialFocusAndAccessibleNameWork,
ReminderCenterIsThemed, ReminderCenterKeyboardGateIsolationBehavior,
ReminderCenterShowFailureRollbackSurvivesEntryFailure, ReminderCenterSourceLabelContrast,
ReminderCenterSupportsKeyboardNavigation, ReminderCenterTwoRowActionsLayout,
ReminderDeliverySingleFlightWorks, ReminderDiagnosticsExcludePrivatePayload,
ReminderIndependentOccurrencesWork, ReminderPersistenceFailureCloseGuardWorks,
ReminderPersistenceFailureDisposeWorks, ReminderPersistenceFailureStaysVisible,
ReminderPersistenceRetryBackoffWorks, ReminderRequiresContentRenderedConfirmation,
ReminderRulesEditorSharedEntryWorks, RepeatingReminderDistinctCyclesCoexist,
RepeatingReminderRenderedLatchWorks, RuleEditorInlineDeleteUsesDangerButton,
SecondaryWindowHeaderDrag, SecondaryWindowTypography,
SecondaryWindowTypographyGateIsolationBehavior, SettingsRecoveryFailureExitIsDefault,
SettingsRecoverySecondaryUsesCanvas, SystemDialogPrimaryButtonContrast,
TagChipTextContrast, TagPopupFollowsDayTheme, ThemeControlInventoryCovered,
ThemedControlTemplatesApplied, ToolTipThemeTextContrast, UiQualityGateIsolationBehavior,
Valid, WarmYellowPaletteMatchesLegacy, ZeroMonthlySalaryStillSavesAsUnset
```

注：`FloatingTitleTextRequiredWidth` 在 C2 另以形状字符串 `FloatingTitleTextRequiredWidth:Number`（:4074）被引用，不计入本集合的字段访问口径；U1 的语义断言（标题可用/需求宽度，:545-549）使用它，迁移时随附录 B 的宽度字段一起处理。

## 附录 B：U1 独有字段（109 个，删除 U1 时逐条迁入 CSharp C2 断言面）

```
CalendarHeaderTextAvailableWidth, CalendarHeaderTextFits, CalendarHeaderTextRequiredWidth,
CalendarNextMonthAccessibleName, CalendarPreviousMonthAccessibleName,
ComboBoxesUseThemedDropDowns, CompactActionTargetsAreUsable, CompactFilterSummary,
CompactPreferenceFieldLayout, CompactSelectionFieldLayout,
CountdownEmptyStateGateIsolationBehavior, CountdownInputHeight,
CountdownInputsUseProjectTheme, CountdownNameWidth, CountdownPreferenceInputHeight,
DangerButtonContrast, DateInputReadOnly, DatePickerButtonOpensCalendar,
DatePickerCalendarIsThemed, DatePickerTextAvailableWidth, DatePickerTextClicksUseTheme,
DatePickerTextDefersCalendarUntilPointerRelease, DatePickerTextFits,
DatePickerTextOpensCalendar, DatePickerTextOpensThemedPopup, DatePickerTextRequiredWidth,
DestructiveConfirmationIsSafe, DisplayPrecisionWidth, DuplicateSettingsRejected,
EffectiveThemeTextContrast, ExpandedDisplayPrecisionWidth, ExpandedMonthlySalaryWidth,
ExpandedTargetDateWidth, ExpandedTargetHourWidth, ExpandedTargetMinuteWidth,
ExpandedTargetSecondWidth, ExpandedWorkEndWidth, ExpandedWorkStartWidth,
FilterOptionsUseSelectionSemantics, FloatingCountdownBottomMargin,
FloatingCountdownButtonTextAvailableWidth, FloatingCountdownButtonTextFits,
FloatingCountdownButtonTextRequiredWidth, FloatingManagerButtonPresent,
FloatingNotesBottomMargin, FloatingNotesButtonTextAvailableWidth,
FloatingNotesButtonTextFits, FloatingNotesButtonTextRequiredWidth,
FloatingPlushTaskWrapLoaded, FloatingReferenceContentOverlapTargets,
FloatingTitleTextAvailableHeight, FloatingTitleTextAvailableWidth,
FloatingTitleTextRequiredHeight, FloatingTitleTextRequiredWidth,
FloatingWindowBottomAnchored, FloatingWindowHeightConstrainedToWorkArea,
FloatingWindowPresent, FloatingWindowReanchorsAfterContentChanges,
FloatingWindowStaysAboveWorkAreaBottom, FormControlsHaveAccessibleNames,
HasCountdownList, HasCountdownReminderModeRadios, HasNameEditor, HasNoteControls,
HasNoteReminderControls, HasOpenNotesActions, HasPreferenceControls, HasTagControls,
HasUnifiedNoteEntry, HiddenCountdownSubmitDisabled, InvalidNotesRejected,
InvalidTagReferencesRejected, KeyboardFocusBaselineEvidence,
KeyboardFocusBaselineFramesAffected, LegacyMigrationSupported,
LiveDatePickerPopupVisuallyUnified, MonthlySalaryWidth, NoDefaultSystemSurfaces,
NoRawFloatingThemeBypass, NoteReminderControlsWork, NoteReminderDispatchWorks,
OfficialCharacterBackgroundCharactersExclusive, OfficialCharacterBackgroundLowContrast,
PopupSurfacesUseThemeTokens, SettingsDraftsRetainedOnRefresh,
SettingsSavedValuesReflectedAfterSave, StaticWorkspaceLayout,
StatusMessagesUseLiveRegions, TagChipsUseUid, TagCreateAppearsFromSearch,
TagCreateStaysCollapsedWithoutQuery, TagFilterSupportsAnyAll, TagUiAccessible,
TargetDateWidth, TargetHourWidth, TargetMinuteWidth, TargetSecondWidth,
ThemeTextContrast, ThemedInteractiveStates, TimeDropDowns, TimeOptionCounts,
ToolTipTemplateMaterializes, TransientPanelsOnDemand, UnifiedWorkspaceSwitches,
WorkEndWidth, WorkStartWidth, WorkspaceDraftsRetained,
WorkspaceSwitcherBeforeEditors, WorkspaceTabsUseSelectionSemantics
```

输入无关性证据：以上字段均为 C# 宿主内部演练计算的行为/几何/主题字段（窗口构造、控件测量、门禁负夹具、合成输入），无任何 `Loaded*`/`Sorted*`/`Rendered*` 数据形态字段；唯一与外部证据耦合的是 `KeyboardFocusBaselineEvidence`（:915 钉死字符串含 `2026-09-11-v22` 基线版本），迁移时连该版本字面量一起搬。实施期验证项：去重 PR 中以一次试运行逐字段比对 U1 原报告与 C2 报告的同名取值，全部一致才算迁移完成；任一字段取值随输入 settings 变化即停止该字段迁移并保留 U1。

## 附录 C：U2 独有字段（26 个，输入相关，U2 保留时无需迁移）

```
ActiveNoteCount, CardActionsBoundByTag, CompletedNoteCount, CompletedNotesCollapsed,
FloatingCountdownCount, FloatingCountdownTexts, FloatingEmptyStateTextIsolationBehavior,
FloatingIncomeText, HasNoteDateControls, LoadedCountdowns, LoadedNotes, LoadedTags,
NoteActionsBoundByTag, NoteDateChoicesSurviveBeijingDayChange,
NoteDateDefaultsTrackBeijingDay, NoteDatesRendered, NotePersistenceRoundTrip,
NoteStatusLabelsPresent, NotesGroupedByDay, PersistenceRoundTrip,
RenderedFloatingNoteIds, RenderedManagerNoteIds, SettingsWereMigrated, SortedNames,
SortedNoteTexts, TagPersistenceRoundTrip
```

## #271 四个 CSharp 夹具分段的字段与计时（2026-11-12）

### 断言字段与报告范围

字段按测试实际读取的报告顶层属性计数；嵌套门禁和测量值归入其顶层字段。基线为 `c40e838968465deba053714da1687d3cb0581fa6`。

- `AdapterValidationRun`：保留整份报告。本段及后续 `AdapterAssertions` 在基线显式消费 132 个顶层字段，合入 #264 后为 154 个（新增 26、移出到 Night 4），另断言 `DeepModuleCount` / `DeepModuleInterfaces` 缺席；`Seal` 还校验报告内全部已声明验证事实。它是其余事实、canonical JSON 形状和参考基线的唯一完整运行，不能按本段末尾两个主题字段裁剪。
- `NightMigration`：`FloatingThemeBoundaryModes`、`MainThemeBackgroundColors`、`CountdownNoResultsResetWorks`、`CountdownEmptyPointerResetRestoresResults`、`CountdownEmptyKeyboardResetRestoresResults`、`MainCountdownBlocksGeometryMatches`（6 个）。改动后只运行主/悬浮主题、倒计时无结果鼠标与键盘重置、分格几何子练习。
- `PersistenceFailureFixture`：`Valid`、`CountdownSaveFailurePreservesState`、`PreferenceSaveFailurePreservesState`、`CountdownSaveFailureRetainsDraft`、`PreferenceSaveFailureRetainsDraft`、`TagSaveFailurePreservesState`、`TagSaveFailureRetainsDraft`、`TagSaveFailureRetainsFilter`、`PersistenceFailureTempPathRemainsDirectory`（9 个）。改动后只运行 `RunPersistenceFailures`；此 fixture 的 `Valid` 由其余 8 个结果计算，不表示完整 canonical 报告已另行运行。
- `NoteExercise`：`NoteAddViaClick`、`NoteReminderViaSelector`、`FloatingCrossDayReminderViaClick`、`MainNoReminderViaClick`、`MainOnceReminderViaClick`、`MainRepeatingReminderViaClick`、`FloatingNoReminderViaClick`、`FloatingOnceReminderViaClick`、`FloatingRepeatingReminderViaClick`、`NoteDateRequired`、`NewNoteRejectsEmptyDate`、`UndatedNoteEditSavesWithoutDate`、`DatedNoteEditRejectsClearedDate`、`NoteDateValidationMessagesDistinguished`、`NoteDateViaPicker`、`NoteStatusViaCheckbox`、`TagInteractionsExecuted`、`TagCreationAndAssociation`、`TagAnyAllFilter`、`TagEditorSummaryIsCompact`、`TagSearchCreateAvoidsDefaultSubmit`、`TagUntaggedAndCountdownStatus`、`TagNoteStatusSearchAndCompletion`、`NoteDateFilterControls`、`NoteDateFilterBehavior`、`TagFloatingNoteInheritsTags`、`TagFloatingNoteDateViaPicker`、`TagFloatingNotePopupOnDemand`、`TagFloatingNoteCanOverrideTags`、`TagNoteEditingPersistsTags`、`TagNoteEditingPersistsDate`、`TagRenameAndDeleteDetachOnly`（32 个）。改动后只运行便签添加、提醒模式、无日期/有日期编辑及同次 `RunTagInteractions` 练习。

原有字段断言、设置哈希、诊断事件顺序和磁盘结果断言均保留；新增检查只比较运行时 JSON 的精确字段名集合，没有新增源码文本断言。完整报告里的验证事实名称与含义未改。

`AdapterValidationRun` 在基线的 132 个显式顶层字段：

```
ActiveNotesSegmentSelectedByDefault, CalendarHeaderTextAvailableWidth,
CalendarHeaderTextRequiredWidth, CalendarNextMonthAccessibleName,
CalendarPreviousMonthAccessibleName, ComboBoxTextClick, ConfirmationDialog,
CountdownEmptyKeyboardResetRestoresResults, CountdownEmptyPointerResetRestoresResults,
CountdownEmptyStateContrastMeasurements, CountdownExpiryVisualsFlipTogether,
CountdownIncrementalRefresh, CountdownIncrementalRefreshIdentityMatches,
CountdownInputHeight, CountdownNameWidth, CountdownNoResultsResetWorks,
CountdownPreferenceInputHeight, CountdownRemainingSamples,
DatePickerOpenCalendarAccessibleName, DatePickerProductionMeasurements,
DatePickerTextAvailableWidth, DatePickerTextRequiredWidth,
DeferredReferenceBaselineFailures, DisplayPrecisionWidth, ExpandedDisplayPrecisionWidth,
ExpandedMonthlySalaryWidth, ExpandedTargetDateWidth, ExpandedTargetHourWidth,
ExpandedTargetMinuteWidth, ExpandedTargetSecondWidth, ExpandedWorkEndWidth,
ExpandedWorkStartWidth, FloatingCountdownBottomMargin,
FloatingCountdownButtonTextAvailableWidth, FloatingCountdownButtonTextRequiredWidth,
FloatingDayRasterComponentAssetNames, FloatingLongNotesCaptures,
FloatingLongNotesMeasurements, FloatingMascotPresentationModes, FloatingNoteSearch,
FloatingNotesBottomMargin, FloatingNotesButtonTextAvailableWidth,
FloatingNotesButtonTextRequiredWidth, FloatingPrimaryCountdownContrastMeasurements,
FloatingPrimaryDeadlineMeasurements, FloatingQuickAddDayResourceChecks,
FloatingQuickAddDayResourceDiagnostics, FloatingQuickAddLocalActiveColor,
FloatingQuickAddLocalCanvasColor, FloatingQuickAddPaletteBindings,
FloatingReferenceBaselineCaptures, FloatingReferenceBaselineDiagnostics,
FloatingReferenceBaselineExecutableSha256, FloatingReferenceBaselineGates,
FloatingReferenceBaselineManifestAvailable, FloatingReferenceBaselineManifestPath,
FloatingReferenceBaselineNegativeFixtures, FloatingReferenceBaselineRequested,
FloatingReferenceBaselineSourceAggregateSha256, FloatingReferenceBaselineValidated,
FloatingReferenceContentOverlapCount, FloatingReferenceContentOverlapTargets,
FloatingReferenceFreshnessGates, FloatingReferenceFreshnessNegativeFixtures,
FloatingScrollBarCaptureNames, FloatingScrollBarDragOffsets,
FloatingScrollBarStateMeasurements, FloatingScrollBarWheelOffsets,
FloatingSecondaryRemainingSamples, FloatingThemeBoundaryModes,
FloatingThemePanelColors, FloatingThemePlushGlowColors,
FloatingTitleTextAvailableHeight, FloatingTitleTextAvailableWidth,
FloatingTitleTextRequiredHeight, FloatingTitleTextRequiredWidth,
KeyboardFocusBaselineFramesAffected, KeyboardFocusRingContrast, MainActionPalette,
MainCharacterPanelDockCount, MainCountdownBlocksGeometryMatches, MainDaySingleBorder,
MainDaySingleBorderGate, MainDaySingleBorderGateIsolationBehavior,
MainDaySingleBorderNegativeFixtures, MainDynamicCountdownCardSampleCount,
MainDynamicNoteCardSampleCount, MainEmptyStateGuidance, MainKeyboardBehavior,
MainKeyboardNavigationGates, MainNoteCardReminderFontMeasurements,
MainNoteCardReminderFontSizeDip, MainNoteCardReminderFontSizeMeetsContract,
MainNoteCompletionInteractionBehavior, MainNoteCompletionMeasurements,
MainReferenceBaselineCaptures, MainReferenceBaselineDiagnostics,
MainReferenceBaselineExecutableSha256, MainReferenceBaselineGates,
MainReferenceBaselineManifestAvailable, MainReferenceBaselineManifestPath,
MainReferenceBaselineNegativeFixtures, MainReferenceBaselineRequested,
MainReferenceBaselineSourceAggregateSha256, MainReferenceBaselineValidated,
MainShortcutGates, MainStressCountdownTitleMeasurements, MainStressCountdownTitleVisuals,
MainStructuralAnnouncementGates, MainStructuralAnnouncements,
MainThemeBackgroundColors, MainThemeCharacterOpacities, MainThemeCharacterPanelColors,
MonthlySalaryWidth, NoteEmptyStateContrastMeasurements, NoteReminderTimeOptionCounts,
NoteStatusSegmentSwitch, ReminderCenterSourceLabelContrast, SecondaryWindowHeaderDrag,
SecondaryWindowTypography, SystemDialogPrimaryButtonContrast, TargetDateWidth,
TargetHourWidth, TargetMinuteWidth, TargetSecondWidth, TimeDropDowns, TimeOptionCounts,
Valid, ValidationReportContractBehavior, ValidationReportModuleDeep, WorkEndWidth,
WorkStartWidth
```

合入 #264 后，canonical 新增以下 26 个显式字段；`CountdownNoResultsResetWorks`、`CountdownEmptyPointerResetRestoresResults`、`CountdownEmptyKeyboardResetRestoresResults`、`MainCountdownBlocksGeometryMatches` 移至 Night，因此当前显式字段总数为 `132 + 26 - 4 = 154`：

```
ActiveNoteCount, CardActionsBoundByTag, CompletedNoteCount, CompletedNotesCollapsed,
FloatingCountdownCount, FloatingCountdownTexts, FloatingEmptyStateTextIsolationBehavior,
FloatingIncomeText, HasNoteDateControls, LoadedCountdowns, LoadedNotes, LoadedTags,
NoteActionsBoundByTag, NoteDateChoicesSurviveBeijingDayChange,
NoteDateDefaultsTrackBeijingDay, NoteDatesRendered, NotePersistenceRoundTrip,
NoteStatusLabelsPresent, NotesGroupedByDay, PersistenceRoundTrip,
RenderedFloatingNoteIds, RenderedManagerNoteIds, SettingsWereMigrated, SortedNames,
SortedNoteTexts, TagPersistenceRoundTrip
```

### 改动前实测

官方入口：`tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage Build,CSharp`。运行目录 `logs/suite-runs/20261112T094857988-bc44acfe-*`，Build 与 CSharp 退出码均为 0。

| 阶段或分段 | 秒 |
| --- | ---: |
| CSharp（低影响套件阶段结果） | 828 |
| AdapterValidationRun | 154.007 |
| NightMigration | 154.799 |
| PersistenceFailureFixture | 163.492 |
| NoteExercise | 196.058 |

四段合计 668.356 秒。改动前四次调用都进入同一个 `ValidationReport.Create` 并返回经 `Seal` 的完整报告；canonical 运行枚举完整顶层键，未改应用的字段数负控也在 `NightMigration`、`PersistenceFailureFixture` 与 `NoteExercise` 分别观察到 800 个字段（预期 2 / 9 / 16，测试按预期失败）。因此“四个分段各自完整生成报告”由调用流、基线计时和运行时字段计数共同确认，不再只是按耗时推断。

### 当前基线的裁剪复验（尚未合入 #264）

官方入口：`tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage CSharp -WaitForLockSeconds 1800`。运行目录 `logs/suite-runs/20261112T152328616-2895bf6f-*`，CSharp 退出码 0。

| 阶段或分段 | 改动前秒 | 当前秒 |
| --- | ---: | ---: |
| CSharp（低影响套件阶段结果） | 828 | 197 |
| AdapterValidationRun | 154.007 | 130.971 |
| NightMigration | 154.799 | 1.758 |
| PersistenceFailureFixture | 163.492 | 0.807 |
| NoteExercise | 196.058 | 2.687 |

这次运行保留并通过完整 canonical 断言面；Night、Persistence、Note 分别只输出 2、9、16 个被断言字段。它不是最终集成证据：integration 纳入 #264 后还必须合并并复验 Night=6、Note=32 的同一提交。

### 最终集成复验（含 #264）

官方入口：`tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage CSharp -WaitForLockSeconds 1800`。运行目录 `logs/suite-runs/20261112T163814120-71955fdd-*`，CSharp 退出码 0，无 `SkipLoadPreflight`，未运行 full gate。

| 阶段或分段 | 改动前秒 | 最终秒 |
| --- | ---: | ---: |
| CSharp（低影响套件阶段结果） | 828 | 194 |
| AdapterValidationRun | 154.007 | 125.893 |
| NightMigration | 154.799 | 2.046 |
| PersistenceFailureFixture | 163.492 | 1.631 |
| NoteExercise | 196.058 | 3.963 |

最终四段合计 133.533 秒，改动前为 668.356 秒。该次运行同时通过完整 canonical 断言面、#264 的全部迁移覆盖与反例，以及 Night=6、Persistence=9、Note=32 的运行时精确字段集断言；没有新增源码文本断言，也没有删减原断言来缩短耗时。

### 与 #264 的合并结果

`integration/prd-258` 的 `81a412ffe979c0ca9d6de8fea0f0187b8b37b3bc` 已正式纳入 #264。本分支只 merge 该 integration，未直接 merge `ticket/258/264`；保留下方「2026-11-11 #264 Ui 退役前分段去向」全节和全部迁移断言，并按下列映射扩展本票 fixture：

- Night 由 2 字段扩为 6 字段：增加 `CountdownNoResultsResetWorks`、`CountdownEmptyPointerResetRestoresResults`、`CountdownEmptyKeyboardResetRestoresResults`、`MainCountdownBlocksGeometryMatches`。前三个复用共享的 `ProbeCountdownEmptyResetBehavior`，只执行倒计时无结果的鼠标与键盘重置；最后一个复用 `AddCountdownReadabilityFixtureRecords` 和 `RunCountdownBlocksGeometryFixture`，只建立几何所需记录并测量分格，不运行整组 `ProbeUiPolishScenarios` / `ProbeCountdownReadabilityScenarios`。
- Note 由 16 字段扩为 32 字段：增加 `TagInteractionsExecuted`、`TagCreationAndAssociation`、`TagAnyAllFilter`、`TagEditorSummaryIsCompact`、`TagSearchCreateAvoidsDefaultSubmit`、`TagUntaggedAndCountdownStatus`、`TagNoteStatusSearchAndCompletion`、`NoteDateFilterControls`、`NoteDateFilterBehavior`、`TagFloatingNoteInheritsTags`、`TagFloatingNoteDateViaPicker`、`TagFloatingNotePopupOnDemand`、`TagFloatingNoteCanOverrideTags`、`TagNoteEditingPersistsTags`、`TagNoteEditingPersistsDate`、`TagRenameAndDeleteDetachOnly`。这些结果只来自同次 fixture 内已有的 `ExerciseTagInteractions` / `RunTagInteractions`，不新增完整报告运行。

上述子练习拆分先落在既有 `WindowValidationScenarios.cs`，不改视觉基线覆盖的产品窗口源码，不形成对 `ticket/258/264` 的额外 merge。

## 2026-11-11 #264 Ui 退役前分段去向

本节是 Ui 阶段退役前的正式清点记录；上方行号和执行点编号仍是 2026-09-14 的调查快照。本票只迁移覆盖，不删除 `Countdown.Ui.Tests.ps1`、Ui 阶段或旧 PowerShell WPF 宿主。分段以 `Write-TestSegmentTiming -Name` 为准，共 9 段：旧宿主专属 4 段，C# 已有等价覆盖 1 段，C# 无覆盖而迁入现役 CSharp 4 段。

| Ui 分段 | 分类 | 去向与理由 |
| --- | --- | --- |
| `Setup` | 旧宿主专属 | 唯一产品断言是旧 `Countdown.Core.psm1` 的 `ConvertTo-CountdownDateText`；其余为旧 Ui 夹具准备。随 #265/#266 的 Ui/旧宿主删除，不迁入 CSharp。 |
| `FatalDiagnosticExercise` | C# 无覆盖 | adapter 经 `-ExerciseFatalDiagnostic` 启动 C# EXE。非默认 `InstanceActivationOnly/Matrix` 虽有相似 case，默认 CSharp 不执行，且没有 Ui 的 `exceptionType` 与 fatal 进程 `ProcessStarted` 断言。迁入默认 CSharp 的同名预期失败分段，复用 adapter、隔离设置与诊断日志 seam；不产出整份验证报告。 |
| `ApplicationHostValidation` | 旧宿主专属 | 直接导入 `Countdown.App.psm1` 并调用 `Start-CountdownApplication`，验证的是旧宿主自身 `ValidateOnly` 合同。随旧宿主删除。 |
| `RuntimeIntegration` | 旧宿主专属 | 复制并直接运行旧 `Countdown.App.psm1`，检查旧宿主 window-ready、日历引导和自退。随旧宿主删除。 |
| `PopupUiaGate` | 旧宿主专属 | UIA 操作由旧 `Countdown.App.psm1` 渲染的窗口。随旧宿主删除。 |
| `ListValidation` | C# 无覆盖 | U2 的 populated fixture、多行动态 Tab 路径和输入相关字段没有现役等价执行点。fixture 与缺口迁入既有 `AdapterValidationRun`，断言复用同一次 `$adapterValidation`。已有重复覆盖不另开运行，逐项见下表。 |
| `TagInteractionExercise` | C# 无覆盖 | U4 是全套件唯一 `ExerciseTagInteractions` 执行点。既有 `NoteExercise` 增加同一 exercise 开关，全部报告与落盘断言复用 `$noteExercise` 和 `$noteExerciseSettings`，不新增报告运行。 |
| `PersistenceFailureExercise` | C# 无覆盖 | U5 是全套件唯一“tmp 未阻塞必须拒绝”路径。紧接既有 `PersistenceFailureFixture` 的阻塞路径复用同一隔离设置，新增的只是非零退出且无报告的预期失败进程；保留设置哈希不变断言。 |
| `LegacyMigrationExercise` | C# 已有等价覆盖 | 默认 CSharp `FixtureRoundTrip` 调用 `MinminCountdown.Verification.exe verify`；`VerificationSuite.RunAll` 对 `SettingsFixtures.VersionThree` 执行 `VerifySchemaMigration`，覆盖迁移标志、canonical v10、倒计时、Minute/月薪/上下班偏好和空便签。`CliApplication.RunRoundTrip` 及 CSharp `FixtureRoundTrip` 另覆盖来源字节不变、显式输出 canonical v10 和稳定往返；adapter 参数映射由 `AdapterValidationRun` 覆盖。因此不新增一份 v3 整份报告。 |

### ListValidation 逐项归属

- 迁入既有 `$adapterValidation` 的输入/落盘断言：`LoadedCountdowns`、`SortedNames`、`PersistenceRoundTrip`、`LoadedTags`、`TagPersistenceRoundTrip`、`LoadedNotes`、`ActiveNoteCount`、`CompletedNoteCount`、`NotePersistenceRoundTrip`、`NoteActionsBoundByTag`、`NoteStatusLabelsPresent`、`HasNoteDateControls`、`NoteDatesRendered`、`NotesGroupedByDay`、`SortedNoteTexts`、`RenderedManagerNoteIds`、`RenderedFloatingNoteIds`、`NoteDateDefaultsTrackBeijingDay`、`NoteDateChoicesSurviveBeijingDayChange`、`CompletedNotesCollapsed`、`SettingsWereMigrated`、`CardActionsBoundByTag`、`FloatingCountdownCount`、`FloatingCountdownTexts`、`FloatingIncomeText`、`FloatingEmptyStateTextIsolationBehavior`。
- 动态 `CountdownTabPath` / `NotesTabPath` 改为 populated fixture 的真实多行 `ForwardFocusedTargets` / `BackwardFocusedTargets`，继续断言正反向严格对称与焦点可见；现役单行断言由本次迁移扩展，不新增执行点。
- 设置哈希不变已由 `AdapterValidationRun` 对同一 `$themeExercisePath` 的前后 SHA-256 断言覆盖。
- 既有 CSharp 直接等价断言：`NoteStatusSegmentSwitch`（`AdapterAssertions`）与 `FloatingNoteSearch` 五个子字段（同段悬浮搜索断言组）。
- 由 C# 验证事实及现役 `$adapterValidation.Valid` 等价覆盖：`NoteDateTimelineVisuals`、`NoteDisclosureAccessibility`、`NoteDisclosureThemeTokens`、`ManagerNoteRowsFollowSortOrder`、`FloatingNotesKeepExistingSortOrder`、`NoteDateSectionsCanCollapse`、`NoteDateSectionsAreIndependent`、`NoteDateSectionStateSurvivesRefresh`、`NoteStatusSegmentsSwitchExclusively`、`FloatingIncomeHiddenWithoutSalary`、`FloatingIncomeRestoredWithSalary`。这些字段在 `ValidationReport.Create` 以 `Must(...)` 登记，`Seal` 为假时拒绝有效报告。

### 运行与断言边界

CSharp 的完整 validate-only 报告运行数保持 4 次：Auto + populated List、Night、阻塞 tmp 的 PersistenceFailure、NoteAdd + TagInteractions。FatalDiagnostic 与未阻塞 tmp 都是预期失败且不产出报告。原 Auto 报告上的 `CountdownNoResultsResetWorks`、`CountdownEmptyPointerResetRestoresResults`、`CountdownEmptyKeyboardResetRestoresResults`、`MainCountdownBlocksGeometryMatches` 移挂到仍使用原单活动倒计时夹具的 Night 报告：populated 夹具含已到期记录，继续用 `Expired` 制造空态会正确失败，不能靠删除或放宽这些既有断言转绿。本票没有新增对源码文本的断言；迁入断言只读 C# 宿主报告、真实诊断日志、退出码和隔离设置落盘结果。

## 2026-11-12 #265 Ui 阶段退役结果

上表九个 Ui 分段已按既定去向处理：`Setup`、`ApplicationHostValidation`、`RuntimeIntegration`、`PopupUiaGate` 随旧兼容测试路径退役；`FatalDiagnosticExercise`、`ListValidation`、`TagInteractionExercise`、`PersistenceFailureExercise` 的现役 C# 覆盖保留在 CSharp；`LegacyMigrationExercise` 继续由 `FixtureRoundTrip` 与纯 C# 验证 runner 覆盖。`Countdown.Ui.Tests.ps1` 及低影响启动器、安装验证编排和收口 runner 中的 Ui 执行点已删除，canonical 报告与 #264/#271 的断言未删改。

当前必过阶段唯一清单为 `Build`、`Core`、`CSharp`、`SourceValidation`、`InstallValidate`。部署证据门仍接受额外已完成的 Ui 阶段，因此既有六阶段记录保持有效；缺少任一现行五阶段仍失败闭锁。旧 `Countdown.App.psm1`、`Countdown.Core.psm1` 及其独占资产不在本票删除范围，仍由 #266 按清单逐项处理。

## 2026-11-12 #266 旧 PowerShell 宿主退役结果

`Countdown.App.psm1`、`Countdown.Core.psm1` 及六张 `floating-plush-{mymelody|cinnamoroll|kuromi}-{front|pose}.png` 旧宿主独占资产已删除；Core 测试入口不再导入旧核心 module，旧核心的纯 PowerShell 领域测试与设置清理测试随之删除。`Countdown.Notes.psm1`、`Countdown.Tags.psm1` 仍被 `Countdown.Settings.psm1` 与现役设置验证使用，不在本票删除。

发布契约相关清单逐项处理如下；`PackageFiles`、`InstalledFiles`、`BuildAssets` 与静态 `IntegrityHashes` 的内容不因本次退役改变。

| 清单 | 处理结论 |
| --- | --- |
| `CompatibilityFixtureFiles` | 整个属性退役。两个旧 module 随源码删除；三张官方角色图不是旧宿主独占资产，继续由 `OfficialCharacterAssets`、`BuildAssets` 和运行时清单覆盖；六张旧悬浮商品图随源码删除。 |
| `LegacyFloatingRegressionAssets` | 整个属性退役，六项全部随旧宿主独占 PNG 删除。 |
| `FloatingPlushAssets` | 删除六项旧商品图；保留三张 `*-reference.png` 作为现役视觉基线 sourceBinding 输入，以及四张 `component-day-*.png` 现役运行时源资产。 |
| `RetiredRuntimeFiles` | 24 项全部保留且顺序不变：继续清理既有安装中的旧 module、旧源资产和旧 `app` 资产，并让旧宿主内容混入发布包的负例继续失败。保留清单不表示源文件仍存在。 |

现役 C# 源码、构建输入和视觉基线 manifest 均不引用六张旧商品图；`Countdown.CSharp.Tests.ps1` 只保留其不得出现在发布目录及单文件 EXE 的负例。
