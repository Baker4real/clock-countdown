---
type: spec
title: adapter 引用清单
tags: [spec, adapter, launch, migration, ticket-277]
intent: 记录 #277 后现役测试与工具改为直接启动应用 EXE 的调用点，以及仓库里剩余的 adapter 引用各自随哪张票删除。
---

# adapter 引用清单

## 范围与判定口径

adapter 指仓库根的 `敏敏子倒计时.ps1`。“经 adapter 启动应用”指用 `pwsh -File 敏敏子倒计时.ps1 …` 拉起 `app\MinminCountdown.exe`。#277 之后，除专门验证 adapter 自身行为的测试外，现役阶段的测试与工具都直接启动 EXE，并显式传入 `--settings-file` 与 `--diagnostic-log-path`。

剩余引用按删除票归类：

- #291 已删除 adapter、桌面启动器与旧安装器，测试和工具不再复制或执行 adapter；
- #292 删除仍保留的启动验收协议与回执旧路径身份；
- #275 结束按 adapter 文件推断应用根：已完成，下表不再有它的行（规则见文末接口说明）。

Ui 阶段及 `Countdown.Ui.Tests.ps1` 已在 #265 退役删除，不再有引用。行号会随并行票漂移，下文按文件与符号、分段或断言名定位。

## #277 改为直接启动 EXE 的调用点

| 位置 | 现在的启动方式 |
| --- | --- |
| `Countdown.Validation.psm1` `Invoke-CountdownValidationSuite` 的“应用 validate-only”步骤 | `<Root>\app\MinminCountdown.exe --validate-only --settings-file <临时设置> --diagnostic-log-path <临时日志>`，仍进 `MinminSuiteIso-ValidateApp` 隔离桌面；EXE 缺失时明确失败 |
| `Countdown.CSharp.Tests.ps1` `ApplicationValidationRun`（`$applicationValidationResult`） | `--validate-only`，设置 `$themeExercisePath`，日志 `$applicationValidationDiagnosticPath` |
| `Countdown.CSharp.Tests.ps1` 旧 Night 主题（`$nightMigrationResult`） | `--validate-only --night-migration-only` |
| `Countdown.CSharp.Tests.ps1` `PersistenceFailureFixture`（`$failedValidationResult`、`$unblockedPersistenceResult`） | `--validate-only --exercise-persistence-failures`；未阻塞反例原先未传日志路径（会写仓库 `logs\application.jsonl`），现显式写进测试根 |
| `Countdown.CSharp.Tests.ps1` 致命诊断演练（`$fatalDiagnosticResult`） | `--validate-only --exercise-fatal-diagnostic` |
| `Countdown.CSharp.Tests.ps1` 跨天便签演练（`$noteExerciseOutput`） | `--validate-only --exercise-note-add --exercise-tag-interactions` |
| `tools/Test-SuiteIsolatedAppProbe.ps1` | 隔离桌面直接启动 EXE，`--exercise-note-add`，日志写 TEMP |
| `tools/Test-FloatingBaselineRepro.ps1` | 两种模式都直接启动 EXE；非隔离模式不再传 `-WindowStyle Hidden`（原本只为隐藏 pwsh 控制台） |

配套改动：

- **验证套件夹具**：`Countdown.Core.Tests.Validation.ps1` 与 `tools/Test-CountdownValidationDiagnostics.ps1` 的假 adapter 换成编译出的同名假 `MinminCountdown.exe`。Core 夹具新增一条断言，钉住被启动的进程路径与五个参数。
- **删除的 C# 测试 helper**：`Write-AdapterFailureDiagnosticEvidence`、`Protect-CountdownDiagnosticText`、`Get-FalseValidationGateNames`。它们只服务于“adapter 失败时再直跑 EXE 对照”，直接启动后没有对照对象。失败时 stdout 中的完整报告和 stderr 仍随异常抛出。

## 剩余引用与删除票

| 引用 | 性质 | 删除票 |
| --- | --- | --- |
| `Countdown.Runtime.psm1`（按 adapter 路径核对回执与进程身份） | 启动回执与验收协议 | #292 |
| `src/MinminCountdown/RuntimeProtocol.cs` 回执 `AdapterPath` | 启动回执身份 | #292 |
| `src/MinminCountdown/ValidationExercises.CalendarAndStartup.cs`（回执 `ApplicationPath` 等于 adapter 路径） | 启动验收演练 | #292 |
| `Countdown.Core.Tests.MainDomain.ps1` `$runtimeApplicationPath` | 回执身份夹具 | #292 |
| `src/MinminCountdown/RuntimeProtocol.cs` `ApplicationPaths.AdapterFileName` | 只剩回执 `AdapterPath` 与启动验收演练在用；#275 起根推断不再用它 | #292 |
| `README.md` 与 `CONTEXT.md` 的启动请求/回执身份说明、`docs/架构与语言迁移.md` 的过渡验收协议段 | #292 待退役的启动协议 | #292 |
| `docs/specs/validate-only-dedup-assertion-map.md` | 2026-09-14 调查快照，不随代码更新 | 不删除 |

## 给后续票的接口说明

- **InteractiveLaunch 已直接启动 EXE**：#291 删除了 pwsh 与 adapter 调用层，直接传 `--require-startup-request`、`--exit-after-ready-for-test*`、`--settings-file` 与 `--diagnostic-log-path`；启动请求、回执旧路径身份和验收行为仍由 #292 处理。
- **工作目录不再由 adapter 设置**：adapter 原本把 EXE 的工作目录设为自身目录；直接启动后不再如此：
  - 验证套件沿用调用方进程的当前目录；
  - CSharp 测试中经 `&` 启动的调用点沿用测试进程的当前位置。

  #275 起 `ResolveApplicationRoot` 不看 adapter：程序目录（`AppContext.BaseDirectory`）名为 `app` 时，应用根是它的上一级，与工作目录无关，上面这些直接启动 `<Root>\app\MinminCountdown.exe` 的调用点都属此类。其他布局（经 `dotnet` 运行源码构建输出：InstanceActivation 进程测试、`Invoke-CountdownSourceValidation.ps1`）以调用方给的工作目录为应用根：前者设为各自的测试根，后者设为仓库根。
