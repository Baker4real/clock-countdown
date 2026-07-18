# 敏敏子倒计时

Windows 桌面多倒计时工具。用户可以为不同目标命名并分别设置截止时刻，程序统一按北京时间（UTC+8）计算、排序和显示。

## 功能

- 悬浮窗可选择精确到天、小时、分钟或秒，统一显示“天 / 小时 / 分钟 / 秒”单位
- 截止时间统一按 `China Standard Time` 处理
- 同时保存和展示多个具名倒计时
- 倒计时与便签共用同一管理窗口和桌面悬浮入口；便签可新增、编辑正文、年月日日期和标签、勾选完成、删除，已完成项目默认折叠
- 倒计时与便签共用一套多选标签：可新建、改名、删除和关联；删除标签只解除关联，不删除任何倒计时或便签
- 标签筛选只作用于当前工作区，支持“全部 / 无标签 / 已选标签”；多选标签可按“任一 / 全部”匹配
- 倒计时可将标签条件与“进行中 / 已到期”叠加筛选；便签可继续叠加“未完成 / 已完成”、正文搜索，以及精确日期或“未设置日期”
- 管理窗口与桌面悬浮窗复用当前工作区及筛选条件；悬浮倒计时始终只显示进行中项目，便签快速新增会默认继承当前选中的标签，常态显示“标签（N）”，点击后可轻量多选或清空
- 倒计时和便签日期都通过主题化日历选择；只有倒计时再通过固定下拉框选择时、分、秒
- “设为现在”可将编辑器恢复到当前北京时间
- 新增、编辑和删除后自动保存，启动时自动恢复
- 旧版单倒计时设置自动迁移为“我的倒计时”
- 到期项目保留在管理列表并显示“已到期”；悬浮窗仅显示进行中的项目，按截止时间排序
- 可输入月薪和上下班时间，实时计算“今日已赚”
- 工资按当月实际工作日计算，并同步法定带薪休假和调休补班日历；日历未就绪时不会显示估算金额
- 节假日日历已就绪时，悬浮窗会在实际工作日、下班前默认显示“距离下班”倒计时，并使用设置的下班时间
- 登录 Windows 后自动启动，默认以小型悬浮框显示所有进行中的倒计时
- 悬浮框可拖动到桌面任意位置；点击“管理”打开完整编辑窗口，关闭管理窗口不会停止悬浮显示
- 悬浮框高度会随显示内容自动收紧；倒计时较多时，列表会滚动显示
- 单实例运行，避免重复窗口
- 本机滚动诊断日志记录启动、正常退出、窗口就绪、节假日日历状态和致命异常，便于定位隐藏窗口闪退

## 文件说明

- `敏敏子倒计时.ps1`：WPF 桌面程序
- `Countdown.App.psm1`：应用宿主；收拢 WPF 生命周期与保持兼容的 `-ValidateOnly` 验证入口
- `Countdown.Core.psm1`：北京时间换算与倒计时核心逻辑
- `Countdown.Notes.psm1`：便签候选状态变更与持久化事务
- `Countdown.Tags.psm1`：共享标签的领域模型、筛选语义与显示名称解析
- `Countdown.Runtime.psm1`：每次启动的令牌、窗口首帧就绪回执、有界等待与滚动诊断日志
- `Countdown.Settings.psm1`：设置 schema 迁移、校验、原子写入与部署等价校验
- `Countdown.InstallTransaction.psm1`：带全机互斥锁的可回滚安装事务；覆盖安装目录、启动器、两份计划任务与 Windows 时间服务状态，并为办公室验收原子写出独立回执
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

## 运行应用

```powershell
pwsh.exe -NoLogo -NoProfile -STA -File .\敏敏子倒计时.ps1
```

## 安装

安装器要求已安装 PowerShell 7+（`pwsh.exe`），缺失时会拒绝运行，不会回退到 Windows PowerShell 5.1。它会在全部预检通过后才取得全机安装事务锁：先记录计划任务和 Windows 时间服务状态，再冻结两份旧登录任务以阻止旧程序复活；旧程序静止后才快照安装目录与桌面启动器（含 `.new` 暂存文件），随后校准网络时间、运行测试、复制程序、创建启动器并注册登录任务。新任务必须用本次启动令牌写出“窗口首帧已渲染”的回执，安装器最多等待 30 秒；正常失败会恢复快照（时间校准本身不可逆）。损坏 journal、缺少备份或用户在崩溃后替换桌面启动器时，安装器会保留证据并拒绝覆盖用户文件，而不是进行破坏性回滚。请在管理员 PowerShell 7 中从仓库根目录运行：

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
