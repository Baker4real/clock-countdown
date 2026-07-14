# 敏敏子倒计时

Windows 桌面倒计时小组件。用户可以自行设置截止日期与具体时间，程序统一按北京时间（UTC+8）计算和显示，并支持窗口置顶、停止倒计时和清除已保存日期。

## 功能

- 精确显示剩余天、时、分、秒
- 截止时间统一按 `China Standard Time` 处理
- 自定义日期选择器和桌面时钟图标
- 保存设置后自动恢复倒计时
- 可停止倒计时并清除设置
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

安装器会校准 Windows 网络时间、运行测试、复制程序到 `D:\敏敏子倒计时`、创建桌面启动器，并注册登录后自动启动的计划任务。请在管理员 PowerShell 中从仓库根目录运行：

```powershell
.\Install-Countdown.ps1
```

安装器只支持安装目录和交互用户桌面均位于 `D:\` 的设备，这是当前办公室设备的安全边界。

## 重新编译启动器

```powershell
.\Build-Launcher.ps1
```

重新生成 `CountdownLauncher.exe` 后，需要同步更新 `Install-Countdown.ps1` 中对应的 SHA-256 清单值。
