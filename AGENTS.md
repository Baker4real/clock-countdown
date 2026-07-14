# 项目协作规则

- 用户可见内容使用简体中文；代码标识符和 Windows API 名称保留原文。
- 保持 Windows PowerShell 5.1 兼容性；WPF 入口必须以 `-STA` 运行。
- 所有日期计算统一使用 `China Standard Time`，不要依赖设备当前时区推断北京时间。
- 修改倒计时逻辑后运行 `Countdown.Core.Tests.ps1`；修改界面后同时运行 `Countdown.Ui.Tests.ps1` 和应用的 `-ValidateOnly` 检查。
- 修改安装包中的受校验文件或重新编译启动器后，同步更新 `Install-Countdown.ps1` 的 SHA-256 清单，并运行安装器的 `-ValidateOnly` 检查。
- 不提交 `settings.json`、`runtime-status.json`、`launcher-state.json`、本机登录状态或设备凭据。
- 普通发布只提交并推送到用户拥有远端的 `main`，不创建 `patch-stack/*`。
