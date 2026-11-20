---
type: evidence
title: "#301 空闲处理器占用的可重复测量与基线"
tags: [issue-301, performance, baseline, isolated-desktop]
intent: 固定未优化应用的五档空闲处理器测量口径、源码绑定和可复核证据位置。
---

# #301 空闲处理器占用的可重复测量与基线

## 范围

本票只提供测量入口、固定 fixture、实测基线和证据，不实现 #325 的状态快照缓存或 #326 的提醒到期扫描降频。基线用于后续票决定范围，不用读码推测代替测量。

## 一条命令复测

先用官方 `IncrementalBuild` 或 `Build` 生成当前 `app`，然后在仓库根运行：

```powershell
pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage IdleProcessorMeasurement -MeasurementEvidenceRoot D:/agents/run-evidence/clock-countdown-implement-spec/prd-296-20261118/issue-301 -WaitForLockSeconds 1800
```

阶段复用官方不可见 Win32 桌面、BelowNormal、owned Job、用户桌面泄漏监测和共享启动器锁。它必须单独运行，拒绝 `-AllowUnisolatedDesktop`，且不在 `Get-CountdownRequiredSuiteStage` 返回的五个必过阶段中；测量结果不进门禁。

入口位于 `tools/Measure-CountdownIdleProcessor.ps1`，纯汇总函数位于 `tools/Countdown.IdleProcessorMeasurement.psm1`。每轮在证据根新建唯一 `run-*` 目录，不覆盖旧结果。默认每档要求 60 秒，可用 `-MeasurementSecondsPerTier <60..570>` 延长。

## 五档与 fixture

| 档位 | 悬浮窗 | 管理窗口 | 日历规则 | 固定输入 |
|---|---|---|---|---|
| `both-hidden` | 隐藏 | 隐藏、尚未创建 | 无 | [baseline.settings.json](fixtures/baseline.settings.json) |
| `floating-only` | 可见 | 隐藏、尚未创建 | 无 | [baseline.settings.json](fixtures/baseline.settings.json) |
| `main-only` | 隐藏 | 可见 | 无 | [baseline.settings.json](fixtures/baseline.settings.json) |
| `both-visible` | 可见 | 可见 | 无 | [baseline.settings.json](fixtures/baseline.settings.json) |
| `calendar-rule` | 可见 | 隐藏、尚未创建 | 一条 2032 年才开始、间隔 999 天的 daily 规则 | [calendar.settings.json](fixtures/calendar.settings.json) |

日历档与 `floating-only` 使用相同可见状态，便于比较一条日历规则带来的增量。规则起始日期远离测量窗口，不应送达提醒或打开提醒中心。

前四种可见状态直接使用现有 `--instance-activation-test-state`。`main-only` 先以 `MainVisible` 创建并验证两窗，再只对当前 owned PID 的悬浮窗句柄调用 `ShowWindow(SW_HIDE)`；采样前后都重新枚举该 PID 的顶层窗口并核对可见状态。

每档把固定 fixture 逐字节复制到该档独立临时应用根，显式传入 `--settings-file`。应用根通过证据目录内的 `app` junction 隔离，诊断日志、runtime receipt 和可能的日历缓存都落在该临时根。测量前后比较临时设置 SHA-256，变化即失败；不读取或写入真实用户设置。网络经一个已绑定但不监听的 loopback 端口拒绝，不访问外部服务。

## 计数器口径

每档 readiness 和窗口状态稳定后先预热 5 秒，再用 `\Process(*)\ID Process` 将 owned PID 唯一解析到一个计数器实例。解析最多五次；多个匹配、零匹配或应用在解析期间退出都失败。随后以 1 秒间隔只读取该实例的 `\Process(<绑定实例>)\% Processor Time`，不再展开全部进程。

默认取 63 组并丢弃首组。剩余样本必须不少于 60 个，首末有效样本必须相隔至少 60 秒，否则该档及整轮失败。采样结束后应用必须仍在该窗口内存活；中途退出会使整档失败，因此不会把原进程退出后的同名实例复用当成目标样本。预绑定同时保留同名既有进程隔离，并避免通配采样时 ID 计数器整类缺失导致无样本。

`MeanSingleCorePercent` 是这些 cooked value 的算术平均，100 表示占满一个逻辑处理器；`MeanSingleCoreRatio = MeanSingleCorePercent / 100`。不把数值除以整机逻辑处理器数。另用 owned Process 的 `TotalProcessorTime` 前后差值做独立交叉核对，但不替代 PDH 样本。

每档还有 `process.json`、`counter-diagnostics.json`、managed wrapper 的 `child-report.json`、wrapper 配置、应用及 wrapper stdout/stderr、诊断日志和临时 fixture 副本。协调器只启动工具侧 PowerShell wrapper；wrapper 实际计算 owner token 的 SHA-256，并分别回报 wrapper 与应用的 PID、启动时刻、EXE 路径/SHA-256 和父子关系。协调器独立回读并核对全部字段，错误 token、缺哈希或错误父进程均拒绝，不改产品程序集。

失败或超时时，协调器只写 `stop.request`。wrapper 先复核完整身份，再通过既有 WPF 外部关闭协议做 10 秒有界 graceful stop；仍未退出时再次核对 PID、启动时刻、EXE 路径/哈希和父进程，才对同一 owned Process handle 升级收尾并等待 5 秒。协调器不直接 Kill 应用。整轮 `launch-intent.json` 与 `measurement.json` 记录环境、Git/构建/脚本/fixture 哈希、owner token 的 SHA-256、readiness、wrapper 与应用身份、优先级、预期退出码和实际收尾记录。构建 manifest 的基点不等于当前 HEAD 时拒绝测量，防止把陈旧构建记成当前基线。

## 修复前基线（2026-11-18，保留）

此节保留修复前数字，原始 JSON 另存为 [baseline-prereview-20261118.json](baseline-prereview-20261118.json)，不随修复重写成新数字。当时 `OwnerTokenReportVerified` 只是父进程根据诊断路径自证，子进程没有实际回报 token，不能作为当前所有权合同证据；处理器样本和进程退出记录仍保留供对照。当前协议及修复后数字见下节与 [baseline.json](baseline.json)。

实测时间：2026-11-18 21:56:33 +08:00 至 22:03:53 +08:00。产品基点为 `73ad5f46440eb8b2bbe3118c141cf0e0792f8d29`，应用运行时 10.0.10，应用 EXE SHA-256 为 `FE8A141A1F896F9CB3ACC814BB5A13628A9691830D8F4235952E28C0E4086EF5`。构建 manifest 的 `-dirty` 来自当时尚未提交的本票测量文件；本票没有修改产品源码。

环境为 Windows build 26300 x64、Intel Core i5-14600KF（20 逻辑处理器）、平衡电源方案、.NET 10.0.10 应用、BelowNormal owned 进程。原始报告的 `Environment.DotNetRuntime` 是运行 PowerShell 7.7 的测量宿主 .NET 11.0.0，不是应用运行时；版本化摘要已分别记录。开跑前三次整机 CPU 平均为 41.501%，这一负载会影响复测可比性，必须随数字一起保留。

| 档位 | 平均单核占用 % | 单核比例 | 样本数 | 首末样本秒数 | P95 单核 % | TotalProcessorTime 交叉核对 % |
|---|---:|---:|---:|---:|---:|---:|
| `both-hidden` | 0.280512 | 0.00280512 | 62 | 67.389 | 1.500955 | 0.355976 |
| `floating-only` | 0.369353 | 0.00369353 | 62 | 64.528 | 3.016907 | 0.582271 |
| `main-only` | 1.952737 | 0.01952737 | 62 | 65.316 | 9.084405 | 2.028547 |
| `both-visible` | 2.287266 | 0.02287266 | 62 | 63.003 | 10.575947 | 2.315828 |
| `calendar-rule` | 0.927379 | 0.00927379 | 62 | 63.022 | 3.065025 | 1.003010 |

五档采样前后可见状态均符合定义，临时设置逐字节不变，五个 owned 应用全部经测试自动退出码 0 收尾，没有强杀或按名称结束进程。当时的 token verified 字段属于已废止的父进程自证，不代表当前 managed wrapper 合同。

管理窗口可见的两档平均约为一个逻辑处理器的 2%，`calendar-rule` 比同可见状态的 `floating-only` 高 0.558026 个百分点，约为该对照档的 2.51 倍。可见管理窗口档的 P95 也达到约 9% 至 11% 单核，分布不是均匀小负载。这些是同轮顺序测量得到的相关差异，不单独证明耗时必然来自状态快照或提醒扫描。

因此，本基线不支持“各档都小到可以忽略”以及 #325/#326 可以缩小或不做的建议。后续票应保留评估，并在各自改动后用同一入口、同 fixture 和相近环境复测；具体优化范围仍由后续归因和前后数字决定。

四个档位记录了 Windows 对其他瞬时计数器样本报告无效数据的非终止错误；汇总只接受 `Status=0` 且 PID 匹配的目标样本，每档仍完整取得 62 个有效目标样本并超过 60 秒。原始错误已保留。当前只有一轮顺序基线，且开跑整机负载为 41.501%，小幅差异不应过度解释；接近决策阈值时应按同口径再跑一轮。

## Managed wrapper 修复后基线

实测开始于 2026-11-19 11:11:52 +08:00，官方隔离阶段用时 443 秒。产品基点为合并提交 `e4020ffcfd08a3c73d4f596d70e73724be948bf8`，应用运行时 10.0.10，EXE SHA-256 仍为 `FE8A141A1F896F9CB3ACC814BB5A13628A9691830D8F4235952E28C0E4086EF5`。开跑前三次整机 CPU 平均为 12.265%。

| 档位 | 平均单核占用 % | 单核比例 | 样本数 | 首末样本秒数 | P95 单核 % | TotalProcessorTime 交叉核对 % |
|---|---:|---:|---:|---:|---:|---:|
| `both-hidden` | 0.321939 | 0.00321939 | 62 | 62.152 | 3.063833 | 0.316058 |
| `floating-only` | 1.781727 | 0.01781727 | 62 | 62.145 | 7.668219 | 1.872531 |
| `main-only` | 1.682833 | 0.01682833 | 62 | 62.066 | 7.628607 | 1.727974 |
| `both-visible` | 1.510142 | 0.01510142 | 62 | 62.101 | 6.186230 | 1.533025 |
| `calendar-rule` | 1.486459 | 0.01486459 | 62 | 62.127 | 7.675106 | 1.532155 |

五档均在首次尝试取得唯一 PID 对应实例，计数器错误为空；PDH 均值与 `TotalProcessorTime` 交叉核对相符。每档 launch 和最终 wrapper 报告都通过 token、PID、启动时刻、真实 EXE 路径/哈希及父子链核验，临时设置不变，五个应用全部自动退出码 0。协议修复前的数字仍只在上一节和独立保留文件中，不与本轮混合平均。

本轮所有可见档平均约为单核 1.49% 至 1.78%，仍不支持把各档统一视为可忽略。`calendar-rule` 本轮低于 `floating-only`，因此不再作出上一轮“日历规则增加 0.558026 个百分点”的当前态结论；不同日期的顺序测量和采样协议修复只用于分别陈述，不据其归因。后续 #325/#326 仍应保留评估，并以本轮以后同一 PID 预绑定的口径做前后复测，具体范围不由本票预定。

修复后的当前机器可读结果仍以 [baseline.json](baseline.json) 为准；其中的 `previousBaseline` 链接并钉住修复前文件哈希。新的验收记录见 [acceptance.json](acceptance.json)。本票不执行 GitHub 回写；issue 评论与五阶段 full gate 由 orchestrator 后续完成。

## 自测与边界

```powershell
pwsh.exe -NoLogo -NoProfile -File ./tools/Test-CountdownIdleProcessorMeasurement.ps1
```

自测使用独立已知样本验证单核比例、首组丢弃、样本数和时长，并用短时长、样本不足、错误 PID 三组反例证明检查能失败；另核对五档定义及 token、父进程、缺哈希反例。`Test-CountdownIdleMeasurementChild.ps1` 还启动三个无窗口的短生命周期 PowerShell fixture，分别证明真实 OS 身份回报、graceful 成功且不用升级、graceful 超时后身份复核升级的收尾证据。它由主自测调用；不会启动 WPF 应用或窗口，已由 `Tooling` 纳入检查。三条实际 fixture 最终报告及一次真实 WPF 测量失败的 graceful 收尾报告投影见 [wrapper-selftest.json](wrapper-selftest.json)。

数字只代表记录中的硬件、OS、电源方案、构建哈希与隔离桌面环境。不同机器、负载或后续源码必须按同口径复测，不把一次基线当成所有环境的固定占用。
