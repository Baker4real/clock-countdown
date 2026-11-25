---
type: spec
title: US34–US41 窗口验证终态抽取
tags: [spec, validation, assembly-boundary, ticket-258]
intent: 固定完整验证闭包的唯一生产者、59 条独立事实去向、旧报告对照和实测证据；最终 Clean 五阶段仍由 orchestrator 验收。
---

# US34–US41 窗口验证终态抽取

## 1. 终态边界

- 正常产品 `app/MinminCountdown.exe` 只保留正常启动、最小安装/测试启动支持与 `--settings-equivalence` / `--settings-migration`；传入 `--validate-only` 必须非零拒绝。产品程序集不得含 `ApplicationValidation`、`ValidationReport*`、`ValidationExercises*`、`WindowValidationScenarios*` 或验证 options。
- `src/MinminCountdown.WindowVerification` 承接 36 个迁移文件、两个窗口内联场景、验证 options、完整报告、exercise、诊断、设置字节恢复、资源释放、GeometryPreflight、视觉生成与旧报告字段。默认模式仍是 59 条独立事实的唯一 canonical 生产者，完整报告不与默认 JSON 合并。
- 产品与 runner 仅经 `InternalsVisibleTo("MinminCountdown.WindowVerification")` 共享 internal；没有新增公共 API。WPF 输入、视觉树/UIA、真实提醒编辑器与既有窄 probe 优先，新增 `FloatingDragProbe` 只承载原生拖拽计数和放置恢复。
- runner 不进入办公室载荷或产品 payload aggregate。`tools/Test-CountdownValidationExtraction.ps1`、Tooling source audit、CSharp 产品程序集反射和发布清单负向断言共同防止回流。
- 同一 `*.cs` / 非空行统计口径（排除 `bin/obj`）：产品由基点 72 文件、68,603 行降为 37 文件、22,679 行，净减少 **35 文件、45,924 行**（36 个移出文件与新增共享渲染器相抵）；runner 由 35 文件、27,113 行增至 75 文件、73,818 行。

## 2. 59 条独立事实旧/新去向

以下 59 行由基点 golden `window-verification.json` 生成。基点与当前 `Countdown.CSharp.Tests.ps1` 的 `$windowVerificationFacts` 已逐项比较，**字段名、顺序和中文含义字符串完全一致**；没有将独立事实并入完整报告。当前 `logs/us34-us41-current-v91-review/window-verification.json` 与 golden 的递归 JSON 对照为 **59 共同字段、0 缺失、0 额外、59 值完全相同、0 差异**，全部值为 true。

| # | 事实名（不变） | 基点 ee86 唯一生产者 / 值 | 当前唯一生产者 / 值 | 语义映射 |
| ---: | --- | --- | --- | --- |
| 1 | `MainEmptyStatesFitListViewport` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 2 | `MainEmptyStatesGuideToSingleAddEntry` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 3 | `MainListOverflowScrollsAndReachesActions` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 4 | `FloatingWindowRendersAndFindsManagerButton` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 5 | `FloatingNoteSearchExpandsAndFocusesWithoutReentry` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 6 | `ReminderCenterRendersAndFindsAcknowledgeAll` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 7 | `ReminderCenterLongTitleDisclosure` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 8 | `ReminderCenterTwoRowLayoutAndLazyCustomSnooze` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 9 | `ReminderCenterSnoozeMenuBehaviorAndDomainSemantics` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 10 | `ReminderRulesEditorRendersAndFindsMode` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 11 | `ConfirmationDialogRendersAndFindsCancel` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 12 | `ReminderRulesEditorSinglePrimaryActionAndDirtyAccept` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 13 | `ReminderRulesEditorContentSizedListAndReachableActions` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 14 | `ReminderRulesEditorPlainLanguageLabelsAndAccessibility` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 15 | `ReminderRulesEditorLivePreviewMatchesSavedSummary` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 16 | `FloatingQuickAddActionPinnedInErrorState` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 17 | `SecondaryWindowsUseDwmRoundedChromeAndBorderColor` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 18 | `SecondaryWindowsFallbackSquareChromeWhenDwmUnsupported` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 19 | `ConfirmationDialogTitleAppearsOnce` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 20 | `ConfirmationDialogReusesLocalWpfUiResources` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 21 | `MainTagRenameSyncsAcrossAllSurfaces` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 22 | `MainCountdownInputFocusAndErrorStates` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 23 | `SharedScrollBarRestingThinAndExpandBehavior` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 24 | `SharedScrollBarMotionAndInteractionsWork` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 25 | `FloatingBottomStatusPublicAutomationTree` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 26 | `DisabledStateUnifiedAndPreserved` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 27 | `MainWindowCommandBarHeaderAndNoteRowsAligned` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 28 | `MainWorkspaceSlidingPillGeometryFocusAndContrast` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 29 | `MainNotesSlidingPillGeometryBordersAndContrast` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 30 | `MainNotesSlidingPillDynamicCountGeometry` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 31 | `MainWindowEmptyStateAffordanceAndHighContrastRibbonCollapse` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 32 | `FloatingWorkdayCompletionBottomNoDuplicateAndCardMask` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 33 | `ReminderRulesEditorFixedIntervalNumericInput` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 34 | `ReminderRulesEditorFixedIntervalVisualAndIsolationContract` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 35 | `OverlaySurfaceBordersMeetContrastWithoutShadow` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 36 | `FilterMatchModeDisclosureAndOverlayFocusLifecycle` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 37 | `DatePickerSingleBoxGeometryAndFocus` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 38 | `DatePickerSingleBoxInteractionAndLongestDateFit` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 39 | `MainNoteSearchCoalescesAndPreservesTagControls` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 40 | `MainWindowHiddenDefersRebuildsAndRefreshesFirstFrame` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 41 | `SpacingTokensWiredAcrossWindows` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 42 | `TagManagerAutoSaveOnEnterAndBlur` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 43 | `TagManagerRenameErrorsEscDiscardAndDelete` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 44 | `FloatingNoteOverflowMenuNativeAndLazy` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 45 | `FloatingNoteOverflowMenuInteractionsAndMotion` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 46 | `MainCountdownDeadlineTimeTypingAndStepping` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 47 | `MainCountdownDeadlineTimeValidationAndAccessibility` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 48 | `RuleEditorRepeatAndLeadNumericInputs` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 49 | `MonthlySalaryDecimalInputAndDownstreamContract` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 50 | `RuleEditorTimeInputTypingValidationAndPreview` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 51 | `ReminderCenterCustomSnoozeTypeableTimeInput` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 52 | `MainWorkHoursTypeableTimeInputAndValidation` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 53 | `SharedIconGeometriesFrozenAndDeduplicated` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 54 | `VectorizedTextGlyphIconsPreserveAccessibilityAndGeometry` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 55 | `MainAndFloatingFormErrorBordersSpread` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 56 | `SecondaryWindowsFormErrorBordersSpread` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 57 | `ReminderViewModelsUseSourceGeneratorsWithoutBehaviorDrift` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 58 | `FloatingNoteBodyWidthOptionBGeometryAndDateStrip` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |
| 59 | `FloatingNoteBodyWidthOptionBScrollSearchTagsAndSticky` | `WindowVerification` 默认 JSON / true | `WindowVerification` 默认 JSON / true | 名称与中文含义逐字一致 |

## 3. 完整旧报告对照

- 基点：`ee86a6490d10f798a18fde7621b4ba332a8af171`，在当前工作树的忽略目录 `logs/us34-us41-golden-ee86-zip/` 由 `git archive` 重建；只有 CSharp 证据捕获插桩，不改产品或 runner。官方 run `20261125T085221343-42c3cafa`：Build 0（132 秒）、CSharp 0（498 秒）。
- 同 fixture golden：`logs/us34-us41-golden-ee86-zip/golden-report/application-validation.json`。当前同 fixture：`logs/us34-us41-current-v91-review/application-validation.json`。
- 对照口径：逐顶层字段比较字段名、JSON 类型和完整 JSON 值；缺字段、额外字段、语义差异均不得隐藏。证据路径、运行 GUID、生成器哈希等预期差异单独列出并附原因，不与行为差异混算。
- **结果（v91 `20261125T115332471-4b9348e7` 全绿 CSharp 报告）：788/788 字段，0 缺失、0 额外；769 个 JSON 类型与值完全相同，19 个顶层差异全部解释，31 个叶差异全部解释，0 个未解释差异。** `full-report-comparison.json` 保存全量字段摘要，`full-report-comparison-full.json` 保存每个旧/新值，`full-report-comparison-reviewed.json` 的 `valid=true`。
- 19 个差异的分类：7 个运行证据路径；8 个 v87→v91 source/EXE/manifest 身份字段（含 14 个 PNG 路径叶差异）；2 个版本说明文本；`FloatingCountdownTexts` 随两次捕获间隔变化（10657.929 秒，倒计时差 10658 秒，误差 0.071 秒）；`LoadedMonthlySalary` 仅为 JSON 数字 scale `23000`→`23000.00`，十进制数值均为 23000。没有行为事实差异，也没有嵌套在 captures 中的隐藏差异。早前 BaselineGenerate 空设置/严格模式与 golden populated/延后模式的混合比较不作为本结论依据。

## 4. 耗时对照（单次实测，不是无负载性能承诺）

| 证据 | Build | CSharp 总阶段 | 默认 59 事实段 | 完整报告段 | 说明 |
| --- | ---: | ---: | ---: | ---: | --- |
| 初始同工作树 `20261125T073647770-48e1d43a` | 134 秒 | 499 秒 | 118.784 秒 | 未单列于摘要 | 高后台负载，`-SkipLoadPreflight`；不可称无负载基线 |
| 基点 golden `20261125T085221343-42c3cafa` | 132 秒 | 498 秒 | 145.641 秒 | 110.171 秒 | 与后续抽取任务共享机器，CSharp 全绿 |
| 中间 v89 `20261125T094359227-f65a0a69` | 98 秒（`20261125T090402619-fe693f5f`） | 416 秒 | 63.986 秒 | 110.372 秒 | CSharp 全绿；59/59 与 golden 相同，完整报告对照 0 未解释差异；后续因 LF 发布身份由 v90 接替 |
| 上一版 v90 `20261125T101717345-d5664f78` | 110 秒（`20261125T100837512-6587eb60`） | 422 秒 | 67.311 秒 | 109.375 秒 | CSharp 全绿；59/59 与 golden 相同，788 字段对照 0 未解释差异 |
| 当前 v91 `20261125T115332471-4b9348e7` | 97 秒（`20261125T114645729-0a2c085f`） | 527 秒 | 65.663 秒 | 109.652 秒 | CSharp 全绿；59/59 与 golden 相同，788 字段对照 0 未解释差异；激活矩阵本轮较慢，不作性能承诺 |

失败记录：首次 v89 CSharp `20261125T090908107-f12b21bb` 在默认 59 事实段 440 秒无完成且 CPU 持续平坦，经官方 `cancel.request` 以 130 Cancelled 收口；未把它算作通过或性能样本。同构建重试 `20261125T091731827-bedb9949` 的默认段以 64.133 秒完成且 59/59 与 golden 相同，首次停滞未复现；该轮随后在旧 `CaptureFloatingViewportState` 源码正则处退出 1（368 秒），已按 US40 改为 `FloatingScrollBarGateIsolationBehavior` 行为断言。`20261125T092532214-fcc32811` 通过完整报告及其余主要断言后，在正常启动迁移等价判定处以 exit 1 / 517 秒结束；旧断言只保留退出码，已补充 stdout/stderr 诊断并另开最终重跑，不能将该轮记为 CSharp 全绿；同构建诊断重跑 `20261125T094359227-f65a0a69` 的交互等价与全套件均通过，CSharp exit 0 / 416 秒。v89 之后发布证据又发现 CRLF/Git blob 源身份不一致，故全部任务文本归一为 LF 并重拍 v90，未改写 v89 manifest。

## 5. 视觉证据

- 现役版本 `2026-11-25-v91`。Build `20261125T114645729-0a2c085f` 0（97 秒）；BaselineGenerate `20261125T114835946-e96a7e74` 0（116 秒），严格自检通过。v90 证据保留在提交 `ac7d892`。
- runner apphost SHA-256：`B7350BCEDD3392381C08EBCFBE23B9EA605CDB5D8665403E3511BC29C5E01F95`；主窗 source aggregate：`2C6E221BD35FD31B743CAD20DFE0CF930FBF012F550571746295509A98044273`；悬浮窗：`949E3C9B446018628E632AC2017C55EC8DBD0946A9E0144BD3A4A630AEDF28E9`。
- `Compare-CountdownVisualBaseline -From 2026-11-25-v90 -To 2026-11-25-v91`：主窗 9/9、悬浮窗 5/5，共 14/14 PNG 与状态指纹逐字节一致；双窗绑定文件均无变化，只有行为审查修复后的 runner EXE 身份变化。像素、alpha、ROI 阈值未改。

## 6. 验收边界

本轮只执行实施 worktree 获授权的最窄到完整检查并保存实际日志；最终提交的 Clean + R2R 五阶段 fresh gate **未由本实施代理执行，保持 unverified，由 orchestrator 所有**。不得用本文件的增量构建、聚焦 CSharp 或阶段组合冒称 fresh 全门。

## 7. 当前运行证据

| 检查 | run / 证据 | 当前结果 |
| --- | --- | --- |
| 源树终态审计 | `tools/Test-CountdownValidationExtraction.ps1` | 36 moved / 0 product residual / 0 runner missing / 0 failures |
| PowerShell AST | 本轮 7 个修改的 `*.ps1` / `*.psm1` | 0 syntax errors；`git diff HEAD --check` 0；发布证据 331 links / 153 hash references / 0 failures |
| IncrementalBuild | `20261125T114645729-0a2c085f` | 0，97 秒；v91 |
| BaselineGenerate | `20261125T114835946-e96a7e74` | 0，116 秒；v91 严格自检 |
| GeometryPreflight | v91 `20261125T120220490-1a28384d` | 0，18 秒，10/10；候选产品 DLL 与 runner 场景程序集身份一致，SHA-256 `3B7FA0AAB929913A0353597ED9897B8F88068ECC58AB7CF802BEBE5A1B5D853E` |
| SourceValidation | v91 `20261125T120239087-cd8f6e9c` | 0，82 秒；source-built runner DLL |
| 59 独立事实 | `logs/us34-us41-current-v91-review/independent-comparison.json` | 59 共同字段、59 相同 true 值、0 差异 |
| 完整报告 | `logs/us34-us41-current-v91-review/full-report-comparison-reviewed.json` | 788 字段、769 相同、19 已解释、0 未解释 |
| CSharp 全阶段 | v91 `20261125T115332471-4b9348e7` | 0，527 秒；默认段 65.663 秒，完整报告段 109.652 秒 |
| Core | `20261125T093716114-46d4e965` | 0，402 秒；fake runner host、runner build target、release closure、诊断 fixtures 全通过 |
| InstallValidate | v91 `20261125T120402061-492ad13f` | 0，17 秒；最小安装验证 |
| ValidationHarness | v91 `20261125T114123743-e41c0ed0` | 0，42 秒；原 popup/tray 合同及 4 个新入口/行为负例全通过 |
| Tooling | v91 `20261125T120527311-a01530f5` | 0，195 秒；递归负控制、诊断 fixture、发布证据、视觉/工具回归全通过 |
| Clean + R2R 五阶段 fresh gate | orchestrator 所有 | `ac7d892` 已全绿；本轮新提交仍待重跑，**unverified** |

## 8. 行为审查修复 RED → GREEN

| Finding | RED | GREEN 与关闭证据 |
| --- | --- | --- |
| 初始化/快照/恢复异常逃逸 | `20261125T110643202-ac99ce22` exit 1；快照只有 `ProcessStarted`，无 Fatal；恢复失败参数未识别 | `20261125T111109406-b996e335` exit 0；快照 IOException 有 Fatal/stderr/非零且设置不变；恢复 IOException 在 `ValidationResourcesReleased` 后由最外层捕获 |
| Dispatcher 等未处理异常 | `20261125T111228264-d83ab7c5` exit 1；异步负例未支持 | `20261125T112030925-bdd589d9` exit 0；实际顺序 `FatalUnhandledException → ValidationResourcesReleased`，stderr/退出码/设置恢复正确；首次 `app.Shutdown` 实现失败 `20261125T111721072-ef0587ed` 未记为通过 |
| 错误行只清文本、未折叠 | 有效精简 seam RED `20261125T113825435-7d5f3e63`：`NoteAddViaClick=false` | `20261125T114123743-e41c0ed0` exit 0；真实提交后两个错误行及 HelpText 清除，编辑器高度、日期和保存按钮位置恢复；完整报告沿用既有字段，无新增字段 |
| 非递归防回流与 denylist 缺口 | 合法夹具通过，嵌套 `MainWindowValidationScenariosRunner` 却 `FailureCount=0` | `-SelfTest` 后合法夹具通过、嵌套假类型被拒绝、bin/obj 不误报；实际产品审计仍 36 moved / 0 residual / 0 missing / 0 failures；Tooling 已接入负控制 |
