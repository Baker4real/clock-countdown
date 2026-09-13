---
type: spec
title: 六阶段验证套件耗时压缩与可观测性
tags: [spec, verification, performance, test-suite]
intent: 将 2026-09-13 套件耗时调查的已确认事实、约束、改进决策与验收方式整合成可执行规格。
---

# 六阶段验证套件耗时压缩与可观测性

## Problem Statement

维护者在本机跑完整六阶段验证套件（Build / Core / CSharp / Ui / SourceValidation / InstallValidate）需要 19-22 分钟（三次全绿实测 1143s / 1147s / 1316s）。修复-验证循环被这个墙钟拖慢：2026-09-06 至 09-13 的 182 次套件运行累计约 18 小时，其中 09-11 单日约 8.9 小时，大部分是调试期的单阶段失败重跑。耗时不是单一瓶颈：Build 占约 35%（-Clean 受管清理 8 个目录 + 两次 R2R 自包含 publish + 487 文件载荷哈希），CSharp / Ui / Core 各占约 19-23%，且三份测试脚本内部完全没有分段计时，阶段内耗时不可见。调查同时发现结构性浪费：同一份 C# 596 字段完整验证在套件中执行约 13 次；两处 ExitAfterReadyForTest 集成段各固定等待约 30 秒的 DispatcherTimer 自退；InstallValidate 阶段与 Core 测试内联调用是同一条命令跑两遍。此外 Build 之后五个阶段互为只读，却被串行调度。

## Solution

在保持官方命令行形状、BelowNormal 低影响前提、不可见 Win32 桌面隔离与红即停语义不变的前提下，分四个可独立交付、独立验收、独立回退的增量：

1. 可观测性先行：构建脚本与 Core / CSharp / Ui 测试脚本输出分段计时（不改变任何验证行为），把阶段内耗时从推断变成实测。
2. 消除固定等待：ExitAfterReadyForTest 的自退间隔改为可配置的测试参数（生产默认 30 秒不变，套件传 2-3 秒），省约 55-60 秒。
3. 并行调度：Build 完成后 Core / CSharp / Ui / SourceValidation / InstallValidate 五路并行（隔离桌面按阶段唯一化），墙钟从约 1147 秒压到约 680 秒。
4. 去重：以逐条断言映射为前提，合并约 13 次完整 validate-only 中的重复副本（估计再省 60-120 秒），每类合同保留唯一 canonical 执行点。

全部落地后完整套件预期 9-11 分钟；发布收口路径（全量 -Clean + R2R）语义不变。

## User Stories

1. As a 维护者, I want 完整六阶段套件的墙钟时间从 19-22 分钟压到 9-11 分钟, so that 修复-验证循环不再被套件拖慢。
2. As a 维护者, I want 每个阶段内部的耗时分段可见, so that 后续优化由实测数据驱动而不是推断。
3. As a 维护者, I want 构建脚本输出每个重操作段的耗时, so that 能区分受管清理、两次 publish 和载荷哈希各自的开销。
4. As a 维护者, I want Core / CSharp / Ui 测试脚本输出分段计时, so that 阶段内热点可以被定位。
5. As a 维护者, I want 阶段日志与 summary.json 的结构保持不变, so that 现有日志聚合与历史对比继续可用。
6. As a 维护者, I want 两个 ExitAfterReadyForTest 集成段不再各等约 30 秒, so that 套件省下约一分钟的纯等待。
7. As a 维护者, I want 自退间隔是显式测试参数而不是改产品默认行为, so that 生产启动路径保持 30 秒默认。
8. As a 维护者, I want 新宿主选项被现有参数断言面覆盖, so that 参数解析契约不会静默漂移。
9. As a 维护者, I want Build 之后五个验证阶段并行运行, so that 套件墙钟接近最长阶段而不是阶段之和。
10. As a 维护者, I want 每个并行阶段使用独立的隔离桌面, so that 阶段间不会出现窗口或 UIA 串扰。
11. As a 维护者, I want 任一阶段失败即取消其余在跑阶段并保留全部已完成日志, so that 红即停语义在并行下仍然成立。
12. As a 维护者, I want 并行度可配置且默认保守, so that 用户同时使用电脑时套件仍然低影响。
13. As a 维护者, I want BelowNormal 优先级与逐字转发官方命令不变, so that 低影响前提与命令合同不受并行化影响。
14. As a 维护者, I want 隔离桌面与优先级传播有探针断言, so that 调度改动被行为测试锁住。
15. As a 维护者, I want 同一份 C# 完整 validate-only 不在套件里重复跑约 13 次, so that 覆盖不变而时间下降。
16. As a 维护者, I want 去重前先有逐条断言映射表, so that 没有任何独有断言被静默删除。
17. As a 维护者, I want CSharp 阶段成为完整 adapter validate-only 的唯一 canonical 执行点, so that 视觉基线抓帧与 shape SHA 只有一个归属。
18. As a 维护者, I want Ui 阶段保留旧 PowerShell WPF 宿主与 UIA 的独有验证, so that 兼容回归面不缩水。
19. As a 维护者, I want 持久化失败演练保留预期失败与预期通过各一遍, so that 失败与成功路径都仍有覆盖。
20. As a 维护者, I want 去重后各阶段 Pass 计数不低于去重前基线, so that 有效性有可核对的量化门禁。
21. As a 维护者, I want Ui 阶段的报告数反映实际断言而非硬编码, so that 计数本身可作为有效性证据。
22. As a 维护者, I want 六阶段套件与 12 步收口 runner 不在同一会话连跑, so that 不做无意识的整套双跑。
23. As a 维护者, I want 本地迭代可选增量构建模式, so that 非收口的验证循环更快；该模式以插桩实测数据为引入前提。
24. As a 发布者, I want 发布收口仍然走全量 -Clean + R2R, so that 发布产物的性能与完整性合同不变。
25. As a 发布者, I want 改动 IntegrityHashes 覆盖文件后同步更新静态哈希, so that 发布合同持续有效。
26. As a 维护者, I want 四个增量各自独立交付、独立验收、独立回退, so that 任一增量出问题不影响其他收益。
27. As a 维护者, I want 每个增量前后各有一次全绿基线对比, so that 耗时收益与有效性都有实测证据。
28. As a 维护者, I want InstallValidate 与 Core 内联安装器验证的双轨被文档标注为有意为之, so that 后来者不会误删其一。

## Implementation Decisions

- 低影响套件启动器：增加 Build 完成后五阶段并行调度；隔离桌面名按阶段唯一化；红即停改为任一阶段红即取消在跑阶段、保留已完成阶段日志与 summary；BelowNormal、逐字转发、官方命令行形状不变；summary.json schema 只增不破。
- 隔离桌面进程启动器：桌面名从固定值改为参数；维持同进程包装，不新增 spawn 层，不引入轮询或固定等待。
- 构建脚本：各重操作段输出 stopwatch 分段计时；-Clean 全量 + R2R 仍是默认与收口唯一路径；增量模式（不清理、保留增量编译）仅在插桩数据证明收益后作为显式非默认开关引入，并在 README 明确其产物不用于发布收口。
- C# 宿主选项与应用入口：ExitAfterReadyForTest 自退间隔成为宿主选项（新命令行参数，adapter 透传），未指定时保持 30 秒；套件两处集成段传 2-3 秒。有效性论证：被测对象是 window-ready 回执与自退路径，不是时长本身；实现前由维护者确认 30 秒不承载稳定性观察窗语义。
- CSharp / Ui 测试脚本：使用短自退间隔；按断言映射表去重——CSharp 阶段保留 canonical 完整 adapter validate-only（含视觉基线 12 帧抓帧与 shape SHA）；Ui 阶段保留旧 PowerShell WPF 宿主验证、UIA 段与独有断言，逐字重复副本删除前其独有断言必须先迁移归属；ExercisePersistenceFailures 三遍保一对（预期失败 + 预期通过），ExerciseNoteAdd 两遍保一遍；Ui 阶段报告数从硬编码改为实际计数。
- Core 测试脚本：加分段计时；内联安装器验证调用与 InstallValidate 独立阶段都保留——前者带合同断言、后者是阶段级红即停门禁，重复成本约 10 秒，标注为有意双轨。
- 发布合同 module：本 spec 触及的测试脚本与构建脚本在 IntegrityHashes 覆盖内，每次改动后按当前提交 blob 更新静态 SHA-256（既有合同义务，不新增机制）。
- 流程决策：六阶段套件与 12 步收口 runner 不在同一会话连跑（后者第 3/6/8/9/10/11 步与六阶段逐字相同，连跑等于整套两遍）；README 注明。
- 并行安全前置条件（调查已核实，实现时逐条守住）：五阶段测试数据根均为 GUID 唯一目录；CSharp / Ui 的 validate-only 均使用临时 settings 文件，不写仓库根 settings.json，SourceValidation 的根快照闭锁不受影响；CSharp 默认路径不含按可执行路径杀进程的逻辑（该逻辑仅存在于非默认 DesktopLauncherOnly 模式，目标为 fixture 与办公室安装路径）；Ui 的仓库内固定诊断目录与 CSharp / SourceValidation 的 GUID 诊断路径不相交；Core 的全局安装事务 mutex 探针为 1 秒有界且其余四阶段不取该锁。
- 依赖事实（已核实，含对调查中间结论的修正）：全部五个验证阶段都依赖 Build——Ui 经 adapter 间接启动 C# 宿主 exe，不能与 Build 并行；Build 是唯一必须先行的阶段，并行扇出在 Build 绿之后。

## Testing Decisions

好测试标准：只测外部可观察行为（阶段退出码、日志与 summary 结构、回执与自退路径、断言计数），不测实现细节；每个增量独立全绿后才进入下一个。

Seam 全部复用既有 seam，不新建：

1. 最高 seam 是六阶段套件官方入口：每个增量交付后以低影响启动器全绿为验收；logs\suite-runs 维持每阶段一组 out/err 日志与一份 summary.json（并行后结构不变）。
2. 启动器行为 seam：既有 BelowNormal 传播探针与隔离桌面探针扩展为按阶段断言独立桌面名与优先级传播，锁住并行调度的正确性。
3. 自退间隔参数化 seam：CSharp 交互段与 Ui 运行时集成 harness 原样继续通过（路径不变、只换时长）；新宿主选项的解析与默认值进 CSharp 现有参数断言面。
4. 去重 seam：断言映射表（被删副本每条断言的归属）+ 套件全绿 + 各阶段 Pass 计数不低于去重前基线（Core 1230 / CSharp 669；Ui 改为实际计数后以首次实测为基线）。
5. Build 插桩 seam：Build 阶段日志出现各分段耗时行，且分段之和不小于阶段 ElapsedSeconds 的 95%（容差留给进程启动与脚本收尾）。

Prior art：两个既有启动器探针脚本；summary.json 只读聚合（2026-09-13 调查的解析方法可直接作为耗时回归比对）；既有红即停语义。

## Out of Scope

- 不改变"用户正在使用这台电脑"的低影响前提：BelowNormal、不可见 Win32 桌面隔离、禁止非隔离直跑全部保留。
- 不改 12 步收口 runner 的步骤表，以及 desktop-launcher / instance-activation-matrix / deploy-validate 三个独立模式。
- 除 ExitAfterReadyForTest 间隔参数化外，不改任何产品功能代码。
- 不以砍断言总数为目标；去重只删有映射归属的重复副本。
- InstallValidate 与 Core 内联安装器验证的双轨保留，不做合并。
- 不动办公室部署链路（Deploy-OfficeCountdown.ps1、远程安装、回执与账本）。
- Countdown.TransferLedger.Tests.ps1 不在六阶段内，不纳入。
- 不做 CI 化：本仓无 CI，套件是本机验证设施。

## Further Notes

- 实测基线（logs\suite-runs，182 次运行，2026-09-06 至 09-13）：完整六阶段仅 3 次且全绿，合计 1143s / 1147s / 1316s；阶段中位数 Build 384s / CSharp 303s / Ui 295s / Core 220s / SourceValidation 37s / InstallValidate 11s；SourceValidation + InstallValidate 合计不足 4%。Ui 在 09-08 后从约 430s 降至约 221s，套件耗时是活基线，每个增量前后都要用全绿跑对比。
- 推断项（插桩落地后由实测替换）：单次 C# validate-only 约 15-25 秒；Core 的隔离树真实构建 + 真实 adapter 验证段估计 60-90 秒；Build 内受管清理与两次 R2R publish 的精确拆分未知（dotnet 自报编译仅 3-15 秒）。
- 根复核修正（相对调查中间结论，已回读源码核实）：（1）Ui 阶段经 adapter 启动 C# 宿主，硬依赖 Build 产物，不能与 Build 并行；（2）CSharp 默认路径不含按可执行路径杀进程的逻辑——全部调用点位于非默认 DesktopLauncherOnly 模式且目标是 fixture 与办公室安装路径，因此默认套件下 CSharp ∥ Ui 无进程杀伤冲突。
- 关键 failure modes 与对策：并行资源竞争（BelowNormal 下 CPU/内存互拖）→ 并行度可配、默认保守、按全绿墙钟实测调整；隔离桌面同名冲突 → 桌面名按阶段唯一化；去重误删独有断言 → 断言映射表先行、宁留勿删、Pass 计数门禁；增量构建产物过期误导 → 增量仅显式开关且 README 禁止用于收口；定时器缩短若实际承载稳定性观察窗语义 → 实现前维护者确认。
- 本 spec 基于 2026-09-13 只读调查（182 次运行日志聚合 + 全部套件脚本与测试源码定向阅读），调查证据的行号锚点以该日期工作树为准；调查全程零写入。
