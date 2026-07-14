# 敏敏子倒计时

Windows 桌面多倒计时工具。用户可以为不同目标命名并分别设置截止时刻，程序统一按北京时间（UTC+8）计算、排序和显示。

## 功能

- 精确显示剩余天、时、分、秒
- 截止时间统一按 `China Standard Time` 处理
- 同时保存和展示多个具名倒计时
- 日期通过日历选择，时、分、秒通过固定下拉框选择
- “设为现在”可将编辑器恢复到当前北京时间
- 新增、编辑和删除后自动保存，启动时自动恢复
- 旧版单倒计时设置自动迁移为“我的倒计时”
- 到期项目保留并显示“已到期”，未到期项目按截止时间排序
- 登录 Windows 后自动启动
- 单实例运行，避免重复窗口

## 文件说明

- `敏敏子倒计时.ps1`：WPF 桌面程序
- `Countdown.Core.psm1`：北京时间换算与倒计时核心逻辑
- `CountdownLauncher.cs`：无控制台桌面启动器源码
- `CountdownLauncher.exe`：安装器使用的已编译启动器
- `countdown-icon.ico`：窗口和桌面启动器图标
- `Install-Countdown.ps1`：办公室 Windows 设备安装与更新脚本
- `Countdown.Core.Tests.ps1`、`Countdown.Ui.Tests.ps1`：回归测试

## 本机验证

在 PowerShell 7 中运行：

```powershell
pwsh -NoLogo -NoProfile -File .\Countdown.Core.Tests.ps1
pwsh -NoLogo -NoProfile -STA -File .\Countdown.Ui.Tests.ps1
pwsh -NoLogo -NoProfile -STA -File .\敏敏子倒计时.ps1 -ValidateOnly
pwsh -NoLogo -NoProfile -File .\Install-Countdown.ps1 -ValidateOnly
```

## 安装

安装器会优先使用 PowerShell 7（`pwsh`），仅在未安装时回退到 Windows PowerShell 5.1。它会校准 Windows 网络时间、运行测试、复制程序到 `D:\敏敏子倒计时`、创建桌面启动器，并注册登录后自动启动的计划任务。请在管理员 PowerShell 中从仓库根目录运行：

```powershell
.\Install-Countdown.ps1
```

安装器只支持安装目录和交互用户桌面均位于 `D:\` 的设备，这是当前办公室设备的安全边界。

## 重新编译启动器

```powershell
.\Build-Launcher.ps1
```

重新生成 `CountdownLauncher.exe` 后，需要同步更新 `Install-Countdown.ps1` 中对应的 SHA-256 清单值。

重新生成图标需要 Python 和 Pillow：

```powershell
python -m pip install Pillow
python .\make_countdown_icon.py
```
