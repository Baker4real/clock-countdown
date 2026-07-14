# 项目协作规则

- 用户可见内容使用简体中文；代码标识符和 Windows API 名称保留原文。
- 保持 Windows PowerShell 5.1 兼容性；WPF 入口必须以 `-STA` 运行。
- 所有日期计算统一使用 `China Standard Time`，不要依赖设备当前时区推断北京时间。
- 修改倒计时逻辑后运行 `Countdown.Core.Tests.ps1`；修改界面后同时运行 `Countdown.Ui.Tests.ps1` 和应用的 `-ValidateOnly` 检查。
- 修改安装包中的受校验文件或重新编译启动器后，同步更新 `Install-Countdown.ps1` 的 SHA-256 清单，并运行安装器的 `-ValidateOnly` 检查。
- 动态倒计时卡片的事件处理器通过控件 `Tag` 传递记录 ID；不要用 `GetNewClosure()` 捕获会被写入的 `$script:` 状态。
- 不提交 `settings.json`、`runtime-status.json`、`launcher-state.json`、本机登录状态或设备凭据。
- 普通发布只提交并推送到用户拥有远端的 `main`，不创建 `patch-stack/*`。

## Agent skills

### Issue tracker

本仓库使用 GitHub Issues 跟踪问题。详见 `docs/agents/issue-tracker.md`。

### Triage labels

使用 Matt 默认的五类 triage 标签。详见 `docs/agents/triage-labels.md`。

### Domain docs

采用 single-context 领域文档布局。详见 `docs/agents/domain.md`。
