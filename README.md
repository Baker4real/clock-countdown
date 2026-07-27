# 敏敏子倒计时

基于 C# / .NET 10 WPF 的 Windows 桌面多倒计时工具。用户可以为不同目标命名并分别设置截止时刻，程序统一按北京时间（UTC+8）计算、排序和显示；PowerShell 7 继续负责安装、计划任务和办公室部署。

## 功能

- 悬浮窗可选择精确到天、小时、分钟或秒，统一显示“天 / 小时 / 分钟 / 秒”单位
- 截止时间统一按 `China Standard Time` 处理
- 主窗口和桌面悬浮窗使用统一的自动暖黄日夜主题：北京时间 07:00（含）至 19:00（不含）为旧版暖白、蜜桃与便签黄色，其他时段为同一体系的深琥珀/暖棕；工作区、弹层、提醒前景与悬浮面板会同步切换并保持可读对比度
- 同时保存和展示多个具名倒计时
- 倒计时与便签共用同一管理窗口和桌面悬浮入口；便签可新增、编辑正文、年月日日期和标签、勾选完成、删除，未完成与已完成项目各自收拢为独立分组并继续按日期（日）聚合，未完成默认展开且可收起，已完成默认折叠
- 每条便签可选择“不提醒 / 仅一次 / 重复提醒”，首次提醒日期可跨天；重复提醒支持常用间隔或自定义 1 到 1440 分钟，一次性提醒错过后会在下次启动补发
- 倒计时与便签共用一套多选标签：可新建、改名、删除和关联；新建标签沿用暖白、蜜桃、便签黄和珊瑚色板，删除标签只解除关联，不删除任何倒计时或便签
- 标签筛选只作用于当前工作区，支持“全部 / 无标签 / 已选标签”；多选标签可按“任一 / 全部”匹配
- 倒计时可将标签条件与“进行中 / 已到期”叠加筛选；便签可继续叠加“未完成 / 已完成”、正文搜索，以及精确日期或“未设置日期”
- 管理窗口与桌面悬浮窗复用当前工作区及筛选条件；悬浮倒计时始终只显示进行中项目，便签快速新增会默认继承当前选中的标签，常态显示“标签（N）”，点击后可轻量多选或清空
- 倒计时和便签日期都通过主题化日历选择，点击日期文字或下拉按钮都可打开日历；只有倒计时再通过固定下拉框选择时、分、秒
- “设为现在”可将编辑器恢复到当前北京时间
- 新增、编辑和删除后自动保存，启动时自动恢复
- 旧版单倒计时设置自动迁移为“我的倒计时”
- 到期项目保留在管理列表并显示“已到期”；悬浮窗仅显示进行中的项目，按截止时间排序
- 可输入月薪和上下班时间，实时计算“今日已赚”
- 工资按当月实际工作日计算，并同步法定带薪休假和调休补班日历；日历未就绪时不会显示估算金额
- 节假日日历已就绪时，悬浮窗会在实际工作日、下班前默认显示“距离下班”倒计时，并使用设置的下班时间
- 登录 Windows 后自动启动，默认以小型悬浮框显示所有进行中的倒计时
- 悬浮框可临时拖动到桌面任意位置；内容增减后会重新贴齐工作区右下方，点击“管理”打开完整编辑窗口，关闭管理窗口不会停止悬浮显示
- 悬浮框高度会随显示内容自动收紧；每次内容增减都会重新贴齐工作区右下方并保留任务栏安全间距；倒计时较多时，列表会滚动显示
- 单实例运行，避免重复窗口
- 主窗口以低透明度并列显示 Sanrio 官方角色页中的美乐蒂、玉桂狗和库洛米立绘，不混入其他角色；夜间适度提高角色可见度但不遮挡内容
- 悬浮窗将三只玩偶作为透明、非交互场景层嵌入左上、右上和右下：顶部两只压入标题栏背景而不遮住标题和操作控件，右下玩偶与卡片边缘融合；场景层随日夜主题切换暖色光晕
- 主窗口立绘的来源与私人使用边界见 [三丽鸥官方背景素材](docs/三丽鸥官方背景素材.md)；悬浮窗玩偶的来源与非生成式处理说明见 [悬浮窗玩偶素材来源](assets/floating-plush/SOURCES.md)
- 本机滚动诊断日志记录启动、正常退出、窗口就绪、节假日日历状态和致命异常，便于定位隐藏窗口闪退

## 文件说明

- `敏敏子倒计时.ps1`：C# 应用的薄启动 adapter；映射既有参数并传播真实退出码，不回退旧 PowerShell WPF
- `src/MinminCountdown`：C# WPF 应用宿主源码
- `src/MinminCountdown.Core`：设置 schema、校验、原子写入与跨进程 mutex
- `src/MinminCountdown.Verification`：C# 设置契约与互操作验证程序
- `Build-CountdownApplication.ps1`：生成 self-contained 的应用与验证 EXE，并复制、校验 9 个应用资产
- `app/MinminCountdown.exe`：生成的 C# WPF 应用
- `verification/MinminCountdown.Verification.exe`：生成的设置契约验证程序
- `Countdown.App.psm1`：旧 PowerShell WPF 兼容回归基线；生产入口不再调用
- `Countdown.Core.psm1`：北京时间换算与倒计时核心逻辑
- `Countdown.Notes.psm1`：便签候选状态变更与持久化事务
- `Countdown.Tags.psm1`：共享标签的领域模型、筛选语义与显示名称解析
- `Countdown.Runtime.psm1`：每次启动的令牌、窗口首帧就绪回执、有界等待与滚动诊断日志
- `Countdown.Settings.psm1`：设置 schema 迁移、校验、原子写入与部署等价校验
- `Countdown.InstallTransaction.psm1`：带全机互斥锁的可回滚安装事务；覆盖安装目录、启动器、两份计划任务与 Windows 时间服务状态，并为办公室验收原子写出独立回执
- `CountdownLauncher.cs`：无控制台桌面启动器源码
- `CountdownLauncher.exe`：安装器使用的已编译启动器
- `countdown-icon.ico`：窗口和桌面启动器图标
- `sanrio-official-mymelody.png`、`sanrio-official-cinnamoroll.png`、`sanrio-official-kuromi.png`：主窗口使用的三张官方透明角色立绘
- `assets/floating-plush/*.png`：桌面悬浮窗左上、右上、右下贴边使用的三维玩偶素材；来源与非生成式处理说明见 `assets/floating-plush/SOURCES.md`
- `Install-Countdown.ps1`：办公室 Windows 设备安装与更新脚本
- `Deploy-OfficeCountdown.ps1`：办公室设备的受校验部署入口
- `Countdown.Core.Tests.ps1`、`Countdown.CSharp.Tests.ps1`、`Countdown.Ui.Tests.ps1`：核心、C# 互操作和旧 UI 兼容回归测试

设置格式、迁移和持久化事务见 [设置与持久化](docs/设置与持久化.md)。
迁移决策、module/seam 划分和候选语言比较见 [架构与语言迁移](docs/架构与语言迁移.md)。

## 本机验证

硬约束：本项目的 PowerShell adapter、测试、安装和办公室部署只支持 PowerShell 7+（`pwsh.exe`），不支持 Windows PowerShell 5.1（`powershell.exe`）。WPF adapter 入口仍必须使用 `-STA`。

```powershell
pwsh.exe -NoLogo -NoProfile -File .\Build-CountdownApplication.ps1 -Clean
.\verification\MinminCountdown.Verification.exe
pwsh.exe -NoLogo -NoProfile -STA -File .\Countdown.CSharp.Tests.ps1
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

安装前先运行 `Build-CountdownApplication.ps1 -Clean` 并按发布产物更新 `Install-Countdown.ps1` 的 SHA-256 清单。安装器要求已安装 PowerShell 7+（`pwsh.exe`），缺失时会拒绝运行，不会回退到 Windows PowerShell 5.1。它会在全部预检通过后才取得全机安装事务锁：先记录计划任务和 Windows 时间服务状态，再冻结两份旧登录任务以阻止旧程序复活；旧程序静止后才快照安装目录与桌面启动器（含 `.new` 暂存文件），随后校准网络时间、运行测试、复制程序、创建启动器并注册登录任务。新任务必须用本次启动令牌写出“窗口首帧已渲染”的回执，安装器最多等待 30 秒；正常失败会恢复快照（时间校准本身不可逆）。损坏 journal、缺少备份或用户在崩溃后替换桌面启动器时，安装器会保留证据并拒绝覆盖用户文件，而不是进行破坏性回滚。请在管理员 PowerShell 7 中从仓库根目录运行：

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
