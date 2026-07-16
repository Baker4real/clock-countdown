# 敏敏子倒计时

Windows 桌面多倒计时工具。用户可以为不同目标命名并分别设置截止时刻，程序统一按北京时间（UTC+8）计算、排序和显示。

## 功能

- 悬浮窗可选择精确到天、小时、分钟或秒，统一显示“天 / 小时 / 分钟 / 秒”单位
- 截止时间统一按 `China Standard Time` 处理
- 同时保存和展示多个具名倒计时
- 倒计时与便签共用同一管理窗口和桌面悬浮入口；便签可新增、勾选完成、删除，已完成项目默认折叠
- 日期通过日历选择，时、分、秒通过固定下拉框选择
- “设为现在”可将编辑器恢复到当前北京时间
- 新增、编辑和删除后自动保存，启动时自动恢复
- 旧版单倒计时设置自动迁移为“我的倒计时”
- 到期项目保留在管理列表并显示“已到期”；悬浮窗仅显示进行中的项目，按截止时间排序
- 可输入月薪和上下班时间，实时计算“今日已赚”
- 工资按当月实际工作日计算，并同步法定带薪休假和调休补班日历；日历未就绪时不会显示估算金额
- 悬浮窗会在实际工作日、下班前默认显示“距离下班”倒计时，并使用设置的下班时间
- 登录 Windows 后自动启动，默认以小型悬浮框显示所有进行中的倒计时
- 悬浮框可拖动到桌面任意位置；点击“管理”打开完整编辑窗口，关闭管理窗口不会停止悬浮显示
- 悬浮框高度会随显示内容自动收紧；倒计时较多时，列表会滚动显示
- 单实例运行，避免重复窗口

## 文件说明

- `敏敏子倒计时.ps1`：WPF 桌面程序
- `Countdown.Core.psm1`：北京时间换算与倒计时核心逻辑
- `Countdown.Notes.psm1`：便签候选状态变更与持久化事务
- `Countdown.Settings.psm1`：设置 schema 迁移、校验、原子写入与部署等价校验
- `CountdownLauncher.cs`：无控制台桌面启动器源码
- `CountdownLauncher.exe`：安装器使用的已编译启动器
- `countdown-icon.ico`：窗口和桌面启动器图标
- `Install-Countdown.ps1`：办公室 Windows 设备安装与更新脚本
- `Deploy-OfficeCountdown.ps1`：办公室设备的受校验部署入口
- `Countdown.Core.Tests.ps1`、`Countdown.Ui.Tests.ps1`：回归测试

设置格式、迁移和持久化事务见 [设置与持久化](docs/设置与持久化.md)。

## 本机验证

硬约束：本项目的应用、测试、安装和办公室部署只支持 PowerShell 7+（`pwsh.exe`），不支持 Windows PowerShell 5.1（`powershell.exe`）。WPF 入口仍必须使用 `-STA`。

```powershell
pwsh.exe -NoLogo -NoProfile -File .\Countdown.Core.Tests.ps1
pwsh.exe -NoLogo -NoProfile -STA -File .\Countdown.Ui.Tests.ps1
pwsh.exe -NoLogo -NoProfile -STA -File .\敏敏子倒计时.ps1 -ValidateOnly
pwsh.exe -NoLogo -NoProfile -File .\Install-Countdown.ps1 -ValidateOnly
pwsh.exe -NoLogo -NoProfile -File .\Deploy-OfficeCountdown.ps1 -ValidateOnly
```

## 安装

安装器要求已安装 PowerShell 7+（`pwsh.exe`），缺失时会拒绝运行，不会回退到 Windows PowerShell 5.1。它会校准 Windows 网络时间、运行测试、复制程序到 `D:\敏敏子倒计时`、创建桌面启动器，并注册登录后自动启动的计划任务。请在管理员 PowerShell 7 中从仓库根目录运行：

```powershell
pwsh.exe -NoLogo -NoProfile -File .\Install-Countdown.ps1
```

安装器只支持安装目录和交互用户桌面均位于 `D:\` 的设备，这是当前办公室设备的安全边界。

办公室设备的稳定部署方式见 [办公室部署](docs/办公室部署.md)。

## 重新编译启动器

```powershell
pwsh.exe -NoLogo -NoProfile -File .\Build-Launcher.ps1
```

重新生成 `CountdownLauncher.exe` 后，需要同步更新 `Install-Countdown.ps1` 中对应的 SHA-256 清单值。

重新生成图标需要 Python 和 Pillow：

```powershell
python -m pip install Pillow
python .\make_countdown_icon.py
```
