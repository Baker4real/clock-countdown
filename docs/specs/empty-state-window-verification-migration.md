---
type: spec
title: 空态窗口验证事实去向表
tags: [spec, verification, empty-state, migration, ticket-274]
intent: 逐条记录 #248 空态区域验证事实、相邻断言与产品载体的保留、迁出和删除去向，并记录 #274 的实测收益。
---

# 空态窗口验证事实去向表

## 范围与判定口径

基线是 #273 已合入的 `integration/prd-258`；#273 的 `MainListOverflowScrollsAndReachesActions`、#271/#272/#265/#266 成果均保留。本票只处理管理窗口空态区域，不迁移其它区域，不改视觉基线，不新增产品验证专用 partial 或产品钩子。

独立 `MinminCountdown.WindowVerification` runner 的 stdout 是三条空态事实的唯一产物；它只输出三个同名 bool，不生成整份产品报告。CSharp 同时钉住精确字段集、每条事实为真和进程退出码 0，缺字段、假事实与非零退出都使阶段失败。产品 `--validate-only` 必须不再含这三个字段。

## 每条验证事实的去向

| 验证事实 | 去向 | 唯一产物与不变含义 |
| --- | --- | --- |
| `MainEmptyStatesFitListViewport` | **迁出** | 由窗口 runner 的 `EmptyStateScenario.RunViewport` 产出。仍在首选、125%/150% 工作区缩小、720×560 与真实 WM_DPICHANGED 五个窗口各量六种空态；没有虚假滚动范围，在列表实际视口居中，滚进窗口后完整可见。名称与中文含义逐字保留。 |
| `MainEmptyStatesGuideToSingleAddEntry` | **迁出** | 由窗口 runner 的 `EmptyStateScenario.RunGuidance` 产出。仍守首次说明、只聚焦且保留草稿的轻量引导、筛选重置、分段空态和唯一添加入口。名称与中文含义逐字保留。 |
| `MainListOverflowScrollsAndReachesActions` | **保留（#273 已迁出）** | 继续由 #273 的 `EmptyStateOverflowScenario.Run` 产出；首选与最小尺寸下两个列表的滚动条、滚轮、thumb、PageDown、末行与每行操作可达性及中文含义不变。 |

**删除的验证事实：无。** 删除的是产品内的重复场景、partial 成员、判定类型和报告字段，不是行为覆盖。

## 相关断言与相邻区域

| 项目 | 去向 | 说明 |
| --- | --- | --- |
| `EmptyNotesMessage` 的 `TextWrapping="Wrap"` XAML 正则 | **迁出并改为行为** | 删除 CSharp 源码文本正则；窗口 runner 在真实 `TextBlock.TextWrapping` 上判定，由 `MainEmptyStatesGuideToSingleAddEntry` 的首次空态检查承载，不新增第四条事实。 |
| 首选与最小尺寸的六种空态实拍 | **迁出** | 由窗口 runner 沿用 `logs\ui-captures\<时间戳>-empty-states\` 和原帧名写 PNG；仍只供核对，不进门禁、不改视觉基线。 |
| `MainEmptyStateGuidance`（`CountdownElementsVisible`、`NotesElementsVisible`、`AddActionsFocusEditors`、`DecorationsAreNonInteractive`、`PopulatedListsRestoreEmptyState`） | **保留** | 属于 `ValidationReport.KeyboardAndDialogs.cs` 的结构播报区域，不是本票迁出的两条事实；原报告字段和 CSharp 断言保留。 |
| `MainStructuralAnnouncements` 的首次空态播报字段 | **保留** | 结构播报区域不在本票范围。 |
| `CountdownNoResultsResetWorks`、`CountdownEmptyPointerResetRestoresResults`、`CountdownEmptyKeyboardResetRestoresResults` | **保留** | 倒计时筛选/指针/键盘区域不在本票范围。 |
| `FloatingEmptyStateTextIsolationBehavior`、`FloatingNoteSearch` 的空态子字段 | **保留** | 悬浮窗区域不在本票范围。 |
| `CountdownEmptyStateContrastMeasurements`、`NoteEmptyStateContrastMeasurements` | **保留** | 对比度测量区域不在本票范围；这些是随测量输入记录状态的字段，不新增为验证事实。 |
| 全局直播区源码 guard 中的空态控件名 | **保留** | 该 guard 守所有窗口的写路径，不属于已迁出的两个产品文件；行为播报由上述保留区域继续验证。 |

## 产品删除位置

| 产品位置 | 删除内容 | 去向 |
| --- | --- | --- |
| `src\MinminCountdown\WindowValidationScenarios.EmptyStates.cs` | `MainWindow` 空态验证 partial 的全部测量/场景成员及 `MainEmptyStateGeometry`、`MainEmptyStateView`、`MainEmptyStateGuidanceProbe` | 场景与判定迁入窗口项目；控件访问改走 #273 窄探针和公共视觉树。 |
| `src\MinminCountdown\ValidationExercises.EmptyStates.cs` | 空态五档编排、两个 `RunMainEmptyState*Behavior` 成员、窗口 case 与 `MainEmptyStateViewportProbe`；原共享宿主也从空态文件移除 | 空态编排与汇总迁入窗口项目；密度区域仍需要的宿主移入既有 Density 文件，见下行。 |
| `src\MinminCountdown\ValidationExercises.Density.cs`、`src\MinminCountdown\WindowValidationScenarios.Density.cs` | 无事实删除；承接原空态文件仍被 #251 消费的隔离宿主与四个私有辅助成员，并改为 Density 名称 | **保留其他区域**：密度场景、窗口档、DPI、实拍和判定仍由产品原区域执行；没有新增 partial 文件或公共 API。 |
| `src\MinminCountdown\ValidationReport.Create.cs` | `mainEmptyStateViewport` / `mainEmptyStateGuidance` 编排及两条同名报告字段 | 不再由产品 EXE 重复运行或输出。 |
| `src\MinminCountdown\ValidationReport.cs` | `CreateEmptyStateEvidenceWriter` | 非门禁实拍由窗口 runner 写出。 |
| `Countdown.ReleaseContract.psm1` 与 `Countdown.Core.Tests.ReleaseContract.ps1` | 两个产品源文件退出产品闭包；两个窗口验证源文件进入构建身份 | `SourceBuildFiles` 102→100；`BuildInputFiles` 保持 108。 |
| `Countdown.CSharp.Tests.ps1` | 旧的一字段曳光弹断言、单条产品缺字段断言、空态 XAML 正则 | 改为精确三字段、逐事实、退出码及三个产品缺字段断言；换行改行为判定。 |

## RED 证据

- `20261113T085052534-59679201`：IncrementalBuild 退出 0（81 秒）；CSharp 退出 1（26 秒）。旧 runner 的真实输出只有一个字段，精确字段集断言失败：`expected=[3] actual=[1]`。这同时证明缺失事实会使官方 CSharp 阶段失败。
- `20261113T090342778-4e6820a7`：IncrementalBuild 退出 0（82 秒）；CSharp 退出 1（201 秒）。新版窗口 runner 的精确三字段、逐事实为真和退出码 0 均已通过，随后产品仍含 `MainEmptyStatesFitListViewport` 的缺失断言按预期失败：`expected=[False] actual=[True]`。由此分别证明 runner 唯一产物和产品缺失事实的 RED。

## 三项实测数据与继续建议

- **产品程序集减少行数：** 净减少 **616 行**。两个空态文件、报告编排/字段和旧实拍写入器合计删除 761 行；既有 Density 文件为承接仍属 #251 的宿主与私有辅助成员，净增 145 行，因此净效果为 `761 - 145 = 616`。没有以新增产品验证文件抵消删减。
- **阶段耗时变化：** #273 同口径聚焦 CSharp 基线 `20261113T000309104-88c444f2` 为 168 秒，其中 `AdapterValidationRun=93.461s`、`WindowVerification=7.180s`。#274 最终 `20261113T091910327-88d6a559` 的 IncrementalBuild 退出 0（65 秒）、CSharp 退出 0（155 秒），总阶段减少 **13 秒（7.7%）**；产品段降至 `77.556s`（-15.905s），独立窗口段为 `11.665s`（+4.485s），两段合计净省 11.420s。单次耗时会受机器负载影响，只作为本次实测而非性能承诺；集成 full gate `20261113T083600803-0ccd384d` 的 CSharp 为 283 秒，不与聚焦结果直接混算。
- **投入改动量：** 本票 **16 个文件，+1243 / -818 行**：产品 6 文件 +171/-787，窗口验证 3 文件 +972/-11，测试与闭包 3 文件 +36/-18，术语/设计/迁移文档 4 文件 +64/-2。另合入 integration 的 `Countdown.Core.Tests.SourceContracts.ps1` 修复（+22/-2），最终合并差异为 17 文件 +1265/-820。
- **是否继续：建议继续逐区域迁移，但不批量一刀切。** 本票证明两条事实能搬走即删除、产品净减 616 行且同口径 CSharp 净省 13 秒，也没有新增产品验证 partial 文件或公共钩子。后续每票仍须先证明区域可完整删除、保留唯一事实产物并实测耗时；像本票 Density 的共享承载必须先拆清归属，不能为了行数删掉其他区域覆盖。
