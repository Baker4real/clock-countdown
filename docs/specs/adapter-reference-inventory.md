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

- #291 删除 adapter、桌面启动器与旧安装器；
- #292 删除启动验收协议；
- #290 删除旧远端部署；
- #275 结束按 adapter 文件推断应用根：已完成，下表不再有它的行（规则见文末接口说明）。

Ui 阶段及 `Countdown.Ui.Tests.ps1` 已在 #265 退役删除，不再有引用。行号会随并行票漂移，下文按文件与符号、分段或断言名定位。

## #277 改为直接启动 EXE 的调用点

| 位置 | 现在的启动方式 |
| --- | --- |
| `Countdown.Validation.psm1` `Invoke-CountdownValidationSuite` 的“应用 validate-only”步骤 | `<Root>\app\MinminCountdown.exe --validate-only --settings-file <临时设置> --diagnostic-log-path <临时日志>`，仍进 `MinminSuiteIso-ValidateApp` 隔离桌面；EXE 缺失时明确失败 |
| `Countdown.CSharp.Tests.ps1` `AdapterValidationRun`（`$adapterResult`） | `--validate-only`，设置 `$themeExercisePath`，日志 `$adapterDiagnosticPath` |
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
| `敏敏子倒计时.ps1` | adapter 本体 | #291 |
| `Countdown.CSharp.Tests.ps1` InteractiveLaunch 段（复制 adapter 到 `interactive-application-root`，pwsh 带 `-ExitAfterReadyForTest -ExitAfterReadyForTestSeconds -SettingsFile -DiagnosticLogPath` 交互启动） | 专门验证 adapter：生产参数映射、等待 EXE、退出码透传（“window-ready 时 adapter 仍在等待 C# EXE”“交互启动 adapter 透传 C# 正常退出码”） | #291 |
| 同段断言“交互启动回执保持 adapter 路径身份” | 回执的应用身份 | #292 |
| `Countdown.CSharp.Tests.ps1` `$adapter` 定义与存在性检查 | InteractiveLaunch 的输入 | #291 |
| `Countdown.CSharp.Tests.ps1` DirectProgramLaunch 段把 adapter 复制进诱饵工作目录 | 反证应用根不看工作目录里的 adapter（#275）；删 adapter 时改用同名占位文件 | #291 |
| `Countdown.Core.Tests.SourceContracts.ps1` adapter 源码断言（`WaitForExit`、`$stayAttached`、`Win32_Process` 脱离作业） | 专门验证 adapter | #291 |
| `Countdown.CSharp.Tests.ps1` `-DesktopLauncherOnly` 段（`$repoAdapter`、`$fixtureAdapter`、`-ExpectedAdapterPath`、`Assert-DesktopLauncherCommandLineAvoidsAdapter`、“登录任务仍用 pwsh 执行 adapter，而不是桌面启动器”“启动器进程没有 schtasks 或 adapter 参数”） | 桌面启动器与登录任务旧启动链 | #291 |
| `tools/Invoke-DesktopLauncherLatencyProbe.ps1`（adapter 副本夹具） | 桌面启动器探针 | #291 |
| `Install-Countdown.ps1`、`Countdown.InstallTransaction.psm1`（安装目录 adapter 路径） | 旧安装器 | #291 |
| `Countdown.ReleaseContract.psm1` `IntegrityHashes` 钉值与发布清单中的 `敏敏子倒计时.ps1` | 发布包内容 | #291 |
| `Countdown.Core.Tests.ReleaseContract.ps1` 发布清单条目、“Isolated validation preserves adapter UI evidence” | 发布包合同与报告字段名 | #291 |
| `Countdown.Core.Tests.InstallLifecycle.ps1`（暂存 adapter、`InstalledFiles`） | 旧安装器事务 | #291 |
| `Countdown.Core.Tests.MainDomain.ps1` 事务安装与回滚夹具里作为应用入口的 adapter 文件 | 旧安装器事务 | #291 |
| `Countdown.Core.Tests.InstallStatus.ps1` 现有布局登录任务夹具（`pwsh … -STA -File <根>\敏敏子倒计时.ps1`） | 登录任务旧启动链的期望定义 | #291 |
| `Countdown.Core.Tests.Installer.ps1`（`applicationPath`、`legacyArguments` 与旧式 Exec 动作夹具） | 新安装器 CheckOnly 对现有布局登录任务的识别与转换期望；不是经 adapter 启动测试应用 | #291 |
| `Countdown.Core.Tests.LayoutSwitch.ps1`（现有布局 adapter 文件、旧任务命令行与 XML 夹具） | 布局切换测试的现有布局输入；不是经 adapter 启动测试应用 | #291 |
| `Countdown.Core.Tests.DeployEntry.ps1`（布局切换入口的现有布局 adapter 文件与旧任务夹具） | 新部署入口布局切换测试的现有布局输入；不是经 adapter 启动测试应用 | #291 |
| `AdapterUiValidated` 字段：`Countdown.Validation.psm1` 的 C# 报告检查与输出、`Install-Countdown.ps1` 回执、`Countdown.Core.Tests.Validation.ps1` 与 `tools/Test-CountdownValidationDiagnostics.ps1` 的假 C# 报告 | 只剩字段名，不再经 adapter 启动 | #291 |
| `Countdown.CSharp.Tests.ps1` 分段名 `AdapterValidationRun` / `AdapterAssertions`、`$adapter*` 变量名、输出 `AdapterUiValidated = $true` | 只剩命名；分段名被文档计时表引用，暂不改 | #291 |
| `src/MinminCountdown/ValidationReport.Create.cs` 中“adapter 验证…”诊断文案 | 只剩措辞 | #291 |
| `Countdown.Runtime.psm1`（按 adapter 路径核对回执与进程身份） | 启动回执与验收协议 | #292 |
| `src/MinminCountdown/RuntimeProtocol.cs` 回执 `AdapterPath` | 启动回执身份 | #292 |
| `src/MinminCountdown/ValidationExercises.CalendarAndStartup.cs`（回执 `ApplicationPath` 等于 adapter 路径） | 启动验收演练 | #292 |
| `Countdown.Core.Tests.MainDomain.ps1` `$runtimeApplicationPath`、`Countdown.Core.Tests.TransactionAcceptance.ps1` `$acceptanceApplicationPath` | 回执身份夹具 | #292 |
| `src/MinminCountdown/RuntimeProtocol.cs` `ApplicationPaths.AdapterFileName` | 只剩回执 `AdapterPath` 与启动验收演练在用；#275 起根推断不再用它 | #292 |
| `Countdown.RemoteDeployment.psm1`（adapter 路径、`AdapterUiValidated` 回执字段） | 旧远端部署 | #290 |
| `Countdown.Core.Tests.RemoteDeployment.ps1`（adapter 夹具、`-ValidateOnly` 令牌表、历史回执字段） | 旧远端部署 | #290 |
| `tools/Test-CountdownDeploymentState.ps1`、`tools/Test-OfficeDeploymentStatus.ps1`（历史回执 `AdapterUiValidated`） | 旧远端部署状态 | #290 |
| `README.md` 文件清单、`-STA` 约束与启动链段，`CONTEXT.md` 安装布局，`docs/架构与语言迁移.md` 的 adapter 与计划任务段 | 描述生产启动链与回执身份 | #291（回执身份段随 #292） |
| `docs/specs/validate-only-dedup-assertion-map.md` | 2026-09-14 调查快照，不随代码更新 | 不删除 |

## 给后续票的接口说明

- **adapter 开关已无调用方**：#277 之后，`-ValidateOnly`、`-NightMigrationOnly` 与各 `-Exercise*` 开关在仓库里没有调用方。InteractiveLaunch 仍覆盖以下映射与行为，#291 删除 adapter 时连同该段一起删除：
  - 生产参数映射；
  - `-ExitAfterReadyForTest*`、`-SettingsFile`、`-DiagnosticLogPath` 的映射；
  - 等待 EXE 退出与退出码透传。
- **工作目录不再由 adapter 设置**：adapter 原本把 EXE 的工作目录设为自身目录；直接启动后不再如此：
  - 验证套件沿用调用方进程的当前目录；
  - CSharp 测试中经 `&` 启动的调用点沿用测试进程的当前位置。

  #275 起 `ResolveApplicationRoot` 不看 adapter：程序目录（`AppContext.BaseDirectory`）名为 `app` 时，应用根是它的上一级，与工作目录无关，上面这些直接启动 `<Root>\app\MinminCountdown.exe` 的调用点都属此类。其他布局（经 `dotnet` 运行源码构建输出：InstanceActivation 进程测试、`Invoke-CountdownSourceValidation.ps1`）以调用方给的工作目录为应用根：前者设为各自的测试根，后者设为仓库根。
