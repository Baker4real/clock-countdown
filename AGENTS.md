# 项目 Agent 入口

## 开始任务

1. 从当前仓库根检查 `git status --short`，把既有工作树改动视为用户工作并原样保留。
2. 按任务分支加载上下文：领域术语和时间语义见 `CONTEXT.md`；视觉、交互、主题、窗口尺寸或参考图工作必须先读 `DESIGN.md`；办公室部署必须先读 `docs/办公室部署.md`。
3. 读取将要修改的源码及其现有测试。项目结构、运行入口和完整验证命令以 `README.md` 与实际脚本为准，不在本文件缓存文件清单或测试数量。窗口套件与本机验证只经 README「本机验证」的低影响启动器；禁止直跑会渲染窗口的 CSharp / Ui / SourceValidation。`Countdown.Core.Tests.ps1` 与安装器 `-ValidateOnly` 的嵌套窗口步骤由 README 约定自行隔离，仍禁止把内层窗口脚本当模板直跑。

## Git 边界

- 普通发布只提交并推送到用户拥有远端的 `main`，不创建或推送 `patch-stack/*`。
- 本地未提交改动本身不作为 issue 未完成或不可关闭的条件；只按 issue 验收标准及其可复核证据判断完成状态。

## 完成标准

- 按 `README.md` 的“本机验证”选择覆盖改动面的最窄检查；共享逻辑或发布边界变化时运行完整相关组合。
- UI、安装或部署变化同时满足 `DESIGN.md`、`README.md` 或 `docs/办公室部署.md` 中与该分支对应的验收合同。
- 改了 `Countdown.ReleaseContract.psm1` 的 `IntegrityHashes` 所覆盖文件后，按当前提交 blob 更新对应静态 SHA-256；`verification\MinminCountdown.Verification.exe` 按本次构建字节锁定，不进跨环境静态钉死。细节见 `README.md` 安装节与 `docs/办公室部署.md`。
- 所有适用检查通过且 `git diff --check` 无错误后才声明完成；无法执行的检查必须明确报告原因和剩余风险。

## 工作流指针

- GitHub Issues：`docs/agents/issue-tracker.md`
- Triage 标签：`docs/agents/triage-labels.md`
- 领域文档与 ADR 路由：`docs/agents/domain.md`
