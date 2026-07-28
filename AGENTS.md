# 项目协作规则

- 用户可见内容使用简体中文；代码标识符和 Windows API 名称保留原文。
- **硬约束：界面改动必须延续既定的温暖可爱视觉体系。** 日间必须沿用旧版暖白、蜜桃、便签黄、珊瑚强调色；夜间只能使用同一体系的深琥珀/暖棕对应色调整明度与对比度。默认标签色也必须属于此组，标签文字保持深暖墨色；不得迁移既有用户保存的标签颜色。不得引入粉紫、冷蓝、霓虹紫、纯黑灰或通用系统深色风格。除非用户明确要求，不大改整体布局、主要工作流或视觉语言，标签区域优先保持紧凑、清晰。
- **硬约束：悬浮窗唯一支持的视觉基线是 2026-07-28 完成的 `520 x 390` 参考图复刻版。** 日间无文字 raster 层、夜间暖棕主题变体及其真实 WPF 动态控件共同组成同一现役版本；不得恢复旧 PowerShell 悬浮窗布局、旧商品姿态或把它们重新发布到 `app`。`Countdown.App.psm1` 与旧素材只允许作为仓库/部署包内的兼容回归夹具，不得安装到运行目录。
- 所有用户可见 WPF 控件与动态表面必须使用项目主题模板；`ComboBox`、`DatePicker` / `Calendar`、`ScrollViewer` / `ScrollBar`、`Popup`、悬浮窗和确认窗不得回退到默认 WPF 或系统外观。
- **硬约束：项目的 PowerShell 脚本入口、测试、安装和办公室部署必须使用 `pwsh.exe`（PowerShell 7+）；不得以 `powershell.exe` 或 Windows PowerShell 5.1 作为 fallback。** C# WPF 应用宿主使用受管的 `MinminCountdown.exe`；计划任务使用 `pwsh.exe -STA` 的薄 adapter 启动它。除非明确改变这条兼容 seam 并通过同等验收，不要把任务直接改为启动 EXE。
- 所有日期计算统一使用 `China Standard Time`，不要依赖设备当前时区推断北京时间。
- 修改倒计时逻辑后运行 `Countdown.Core.Tests.ps1` 与 `Countdown.CSharp.Tests.ps1`；修改界面后同时运行 C# 验证 module、应用的 `--validate-only` 检查，并保留 PowerShell 回归测试作为兼容性基线。
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
