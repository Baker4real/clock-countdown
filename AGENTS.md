# 项目 Agent 入口

## 开始任务

1. 从当前仓库根检查 `git status --short`，把既有工作树改动视为用户工作并原样保留。
2. 按任务分支加载上下文：领域术语和时间语义见 `CONTEXT.md`；视觉、交互、主题、窗口尺寸或参考图工作必须先读 `DESIGN.md`，它一次 Read 读不完：先 `rg -n '^#{1,3} ' DESIGN.md` 取目录，读完跨界面的 §2、§7、§9、§13，再读所改界面的章节（悬浮窗在 §10）；办公室部署必须先读 `docs/办公室部署.md`。
3. 读取将要修改的源码及其现有测试：搜索限定在 `src/`、`tools/` 或仓库根 `-g 'Countdown*.ps*'`（无 `tests/` 目录；全仓 `rg` 加 `-g '!logs/**' -g '!assets/**'`），读超长文件前先按 `docs/agents/large-files-and-hotspots.md` 取符号行号。项目结构、运行入口和完整验证命令以 `README.md` 与实际脚本为准，不在本文件缓存文件清单或测试数量。窗口套件与本机验证只经 README「本机验证」的低影响启动器；禁止直跑会渲染窗口的 CSharp / SourceValidation。`Countdown.Core.Tests.ps1` 与安装器 `-ValidateOnly` 的嵌套窗口步骤由 README 约定自行隔离，仍禁止把内层窗口脚本当模板直跑。
4. 临时跑 `dotnet` 命令用 `D:\Codex\tools\dotnet\10.0\dotnet.exe`（设了 `COUNTDOWN_DOTNET_EXE` 时用它）；PATH 上的系统 SDK 未必满足 `global.json` 钉值，按 `tools/Test-CountdownBuildPreflight.ps1` 核实，构建脚本解析顺序见 `tools/Countdown.BuildPreflight.psm1` 的 `Resolve-CountdownBuildDotnet`。

## Git 边界

- 普通发布只提交并推送到用户拥有远端的 `main`，不创建或推送 `patch-stack/*`。
- 本地未提交改动本身不作为 issue 未完成或不可关闭的条件；只按 issue 验收标准及其可复核证据判断完成状态。

## 完成标准

- 按 `README.md` 的“本机验证”选择覆盖改动面的最窄检查；共享逻辑或发布边界变化时运行完整相关组合。
- UI、安装或部署变化同时满足 `DESIGN.md`、`README.md` 或 `docs/办公室部署.md` 中与该分支对应的验收合同。
- 包内源文件改动后直接经 README 的官方启动器构建和验证；Build 重新生成应用载荷清单，Deployer 在真实 ZIP 内生成全包 `package-manifest.json`，不要手算或同步静态哈希，也不要维护 `BuildInputFiles` / `SourceBuildFiles`。正式部署另核对提交 blob 原始字节、同一提交 CleanR2R 聚合、ZIP 与清单 SHA-256。逐项接替门见 `docs/agents/release-integrity-replacement.md`。
- 所有适用检查通过且 `git diff --check` 无错误后才声明完成；无法执行的检查必须明确报告原因和剩余风险。

## 工作流指针

- GitHub Issues、Spec 与拆票：`docs/agents/issue-tracker.md`
- Triage 标签：`docs/agents/triage-labels.md`
- 领域文档与 ADR 路由：`docs/agents/domain.md`
- 大文件索引、并行热点与实拍证据定位：`docs/agents/large-files-and-hotspots.md`
