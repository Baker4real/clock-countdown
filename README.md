---
type: readme
title: 敏敏子倒计时
tags: [overview, wpf, countdown]
intent: 说明敏敏子倒计时的功能、运行方式和维护入口。
---

# 敏敏子倒计时

基于 C# / .NET 10 WPF 的 Windows 桌面多倒计时工具。用户可以为不同目标命名并分别设置截止时刻，程序统一按北京时间（UTC+8）计算、排序和显示；PowerShell 7 继续负责安装、计划任务和办公室部署。

## 功能

- 悬浮窗可选择精确到天、小时、分钟或秒，统一显示“天 / 小时 / 分钟 / 秒”单位
- 截止时间统一按 `China Standard Time` 处理
- 主窗口提供紧凑的三态主题按钮，按“自动→日间→夜间→自动”循环并保留选择；自动模式仍以北京时间 07:00（含）至 19:00（不含）为暖白、蜜桃与便签黄色，其他时段为同一体系的深琥珀/暖棕；主窗口与 `520 x 390` 悬浮窗在同一 tick 使用同一生效主题，不改变现役 raster/暖棕参考图基线
- 同时保存和展示多个具名倒计时
- 倒计时与便签共用同一管理窗口和桌面悬浮入口；便签可新增、编辑正文、年月日日期和标签、勾选完成、删除。完整管理窗口把未完成与已完成项目各自收拢为独立分组，组内按较新日期和同日较新添加项优先显示；状态分组及每个日期分组都可独立收起、展开，未完成默认展开，已完成默认折叠
- 每条便签和用户倒计时可在共享次级编辑器中管理最多 8 条绝对一次、固定间隔或类型化日/周/月/年日历规则；用户倒计时还支持到点或提前 `0..5,256,000` 分钟的相对目标规则。日历规则按北京时间解释、以规范 UTC occurrence 持久化，不存在日期跳过，显式“最后一天”按月长和闰年计算；冷启动和长时间延迟只补发每条规则最新的错过 occurrence。编辑倒计时目标会用相同 rule ID 安全重算未送达相对 occurrence，派生时刻已过去时整笔倒计时编辑不提交
- 到期便签与用户倒计时统一进入单一提醒中心；同一时刻的条目批量加入，后续条目追加并稳定排序，但每条规则最多保留一个未解决发生项，处于稍后或送达失败状态时不会再展示同规则的新基础周期。两类都提供“知道了 / 稍后提醒 / 打开来源”，只有便签提供“完成便签”，均不提供删除；打开来源会恢复管理窗口，保留既有草稿与筛选，临时揭示准确来源卡片并聚焦其编辑入口。知道了、稍后、完成便签及送达写盘失败都保留原状态和条目并按 5、10、20、30 秒退避重试。提醒中心与规则编辑器可从窗口头部拖动；提醒中心首次显示贴齐工作区右上，之后保留手动位置
- 倒计时与便签共用一套多选标签：可新建、改名、删除和关联；新建标签沿用暖白、蜜桃、便签黄和珊瑚色板，删除标签只解除关联，不删除任何倒计时或便签
- 标签筛选只作用于当前工作区，支持“全部 / 无标签 / 已选标签”；多选标签可按“任一 / 全部”匹配
- 倒计时可将标签条件与“进行中 / 已到期”叠加筛选；便签可继续叠加“未完成 / 已完成”、正文搜索，以及精确日期或“未设置日期”
- 管理窗口与桌面悬浮窗共享筛选条件；从悬浮窗打开管理窗口时沿用悬浮窗当前工作区。悬浮倒计时始终只显示进行中项目，便签快速新增在进入新增状态时以当时的筛选标签初始化；标签选择器未选择时显示“选择标签”，已选择时显示标签名称（两个以顿号并列，三个及以上显示“首个名称 +N”，超长截断为省略号），点击弹出复选列表可多选或逐项取消勾选
- 悬浮窗便签工作区提供折叠式正文搜索：“搜索便签”按钮或 `Ctrl+F` 切换到便签工作区、展开输入行并聚焦（占位“搜索便签”，最长 140 字符）；`Esc` 或“✕”清除钮清空搜索词并收起。提示条三态：无词且收起时不显示搜索行；收起且有词时显示“搜索:{词}”与“✕”，点击词钮重新展开聚焦；展开时显示输入行。搜索词与管理窗口共享同一筛选态，只忽略大小写匹配未完成便签正文，不写入设置
- 倒计时和便签日期都通过主题化日历选择，点击日期文字或下拉按钮都可打开日历；倒计时的时、分、秒下拉点击文字区域即可打开，不要求点到箭头
- “设为现在”可将编辑器恢复到当前北京时间
- 新增、编辑和删除后自动保存，启动时自动恢复
- 每次合法保存维护 `settings.json.backup1` / `.backup2` 两代设备本地备份；主设置损坏或不可读时只提供“恢复备份 / 退出”，不会静默恢复或以空设置继续。恢复前保留可读的原始损坏字节，且只有从主路径回读校验成功后才启动窗口
- 旧版单倒计时设置自动迁移为“我的倒计时”
- 到期项目保留在管理列表并显示“已到期”；悬浮窗仅显示进行中的项目，按截止时间排序
- 可输入月薪和上下班时间，实时计算“今日已赚”
- 工资按当月实际工作日计算，并同步法定带薪休假和调休补班日历；日历未就绪时不会显示估算金额
- 节假日日历已就绪时，悬浮窗会在实际工作日、下班前把“距离下班”倒计时固定显示为首行唯一主项，使用主项的暖纸底、珊瑚强调、字体和剩余时间效果；它不是用户记录，没有规则集合、编辑入口或提醒 occurrence。没有“距离下班”时，第一个普通倒计时仍是主项
- 登录 Windows 后自动启动，默认以小型悬浮框显示所有进行中的倒计时
- 悬浮框除按钮、输入框和滚动区等交互控件外，整个背景区域都可拖动；手动位置在内容刷新后保留，并按当前工作区边界收紧，点击“管理”打开完整编辑窗口，关闭管理窗口不会停止悬浮显示
- 点击悬浮框标题栏的“缩小”按钮会隐藏窗口并显示系统托盘图标；双击托盘图标或选择“打开悬浮窗”可恢复，托盘菜单也可退出应用
- 悬浮框固定为 `520 x 390`；首次显示自动贴齐工作区右下方并保留任务栏安全间距；手动移动后会继续保持可见，内容较多时列表滚动显示。现役双工作区的精确布局、安全区和排版合同见 [DESIGN.md](DESIGN.md)
- 普通交互模式按当前用户、应用根和设置路径单实例运行；第二次普通启动通过有界命名管道请求恢复并聚焦现有悬浮窗，只有收到匹配协议、scope 和 request identity 的确认才成功退出。托盘、隐藏或最小化状态都会恢复，已可见时只聚焦；该动作不显示管理窗口、不改变筛选或草稿，也不写设置
- 主窗口和悬浮窗沿用暖色日夜主题；装饰 raster 与真实 WPF 动态控件保持分层，具体视觉和命中测试合同以 [DESIGN.md](DESIGN.md) 为准
- 主窗口立绘的来源与私人使用边界见 [三丽鸥官方背景素材](docs/三丽鸥官方背景素材.md)；悬浮窗玩偶的来源与非生成式处理说明见 [悬浮窗玩偶素材来源](assets/floating-plush/SOURCES.md)
- 本机滚动诊断日志记录启动、正常退出、窗口就绪、单实例激活结果、节假日日历状态和致命异常；激活诊断只保存非内容身份与窗口状态，不记录筛选值、草稿、便签正文、倒计时名称或设置 JSON

## 文件说明

- `敏敏子倒计时.ps1`：C# 应用的薄启动 adapter；映射既有参数。生产启动用 `Win32_Process.Create` 脱离计划任务作业，确认 C# 仍存活后退出 0；validate/exercise 仍等待并传播退出码。不回退旧 PowerShell WPF
- `src/MinminCountdown`：C# WPF 应用宿主源码
- `src/MinminCountdown.Core`：设置 schema、校验、原子写入、跨进程 mutex，以及基于固定 `Ical.Net 5.2.3` 的类型化日历 occurrence 计算
- `src/MinminCountdown.Verification`：C# 设置契约与互操作验证程序
- `Build-CountdownApplication.ps1`：生成 self-contained 的应用与验证 EXE，并复制、校验 14 个现役应用资产
- `app/MinminCountdown.exe`：生成的 C# WPF 应用
- `verification/MinminCountdown.Verification.exe`：生成的设置契约验证程序
- `Countdown.App.psm1`：旧 PowerShell WPF 兼容回归夹具；只保留在仓库和部署验证包中，不安装到运行目录，生产入口不调用
- `Countdown.Core.psm1`：北京时间换算与倒计时核心逻辑
- `Countdown.Notes.psm1`：便签候选状态变更与持久化事务
- `Countdown.Tags.psm1`：共享标签的领域模型、筛选语义与显示名称解析
- `Countdown.Runtime.psm1`：每次启动的令牌、窗口首帧就绪回执、有界等待与滚动诊断日志
- `Countdown.Settings.psm1`：设置 schema 迁移、校验、原子写入与部署等价校验
- `Countdown.InstallTransaction.psm1`：带全机互斥锁的可回滚安装事务；覆盖安装目录、启动器、两份计划任务与 Windows 时间服务状态，并为办公室验收原子写出独立回执
- `Countdown.TransactionAcceptance.psm1`：安装与办公室部署共用的事务验收状态机
- `Countdown.Validation.psm1`：安装器共用的 Core、C#、兼容 UI 与应用 validate-only 验证 module
- `Countdown.RemoteDeployment.psm1`：办公室候选安装、验证门禁、事务验收、回滚、提交后收尾，以及成功/失败回执、部署 binding、独立回读与回滚前诊断的严格契约 module
- `Countdown.TransferLedger.psm1`：办公室全部 SCP 与 SSH 证据回读共用的计时、吞吐和失败尝试账本 module
- `Countdown.ReleaseContract.psm1`：构建、安装、部署和 Core tests 共用的发布资产分类与 SHA-256 契约
- `CountdownLauncher.cs`：无控制台桌面启动器源码；仓库编译产物为 `CountdownLauncher.exe`
- `CountdownLauncher.exe`：仓库中的启动器文件；安装后桌面名为 `敏敏子倒计时.exe`，双击直接启动宿主
- `countdown-icon.ico`：窗口和桌面启动器图标
- `sanrio-official-mymelody.png`、`sanrio-official-cinnamoroll.png`、`sanrio-official-kuromi.png`：主窗口兼容回退与来源校验使用的三张官方透明角色立绘
- `assets/floating-plush/*.png`：悬浮窗现役视觉资产与旧 PowerShell 回归夹具；来源、处理边界、哈希和用途见 [悬浮窗玩偶素材来源](assets/floating-plush/SOURCES.md)
- `assets/main-decorations/*.png`：主窗口暖色日夜装饰资产；现役用法见 [DESIGN.md](DESIGN.md)
- `assets/ui-references/`：用户生成的页面与悬浮窗参考图及机器可读 `manifest.json` 的固定位置，作为视觉复刻比对依据随仓库版本化；不会进入 `app` 的运行时资产清单
- [DESIGN.md](DESIGN.md)：主窗口便签“暖纸时间轴”方向 B，以及悬浮窗“倒计时 B 暖纸层级 + 便签 B 台历索引”的现役视觉、交互和日夜主题合同
- `tools/Build-MainNightTopScene.ps1`：从日间参考构图生成主窗口夜间暖琥珀顶部场景的维护脚本
- `Install-Countdown.ps1`：办公室 Windows 设备安装与更新脚本
- `Deploy-OfficeCountdown.ps1`：办公室设备的受校验部署入口
- `Countdown.Core.Tests.ps1`、`Countdown.CSharp.Tests.ps1`、`Countdown.Ui.Tests.ps1`：核心、C# 互操作和旧 UI 兼容回归测试
- `Invoke-CountdownSourceValidation.ps1`：本机验证组合中的 C# validate-only 源码契约验证入口
- `tools/Invoke-CountdownVerificationRun.ps1`、`tools/Invoke-DesktopLauncherLatencyProbe.ps1`：标准化收口验证 runner 与桌面启动 T0–T4 分段时延探针；探针样本只用于归因，不汇入门槛样本池

设置格式、迁移和持久化事务见 [设置与持久化](docs/设置与持久化.md)。
迁移决策、module/seam 划分和候选语言比较见 [架构与语言迁移](docs/架构与语言迁移.md)。
倒计时、便签与共享标签的产品证据、现役边界和后续候选见 [倒计时、便签与标签协同调研](docs/标签与倒计时应用调研.md)。
悬浮窗参考图复刻的 GitHub 候选、采用路线、数值边界和明确不采用项见 [悬浮窗视觉复刻 GitHub 方案调研](docs/悬浮窗视觉复刻%20GitHub%20方案调研.md)。

## 本机验证

硬约束：本项目的 PowerShell adapter、测试、安装和办公室部署只支持 PowerShell 7+（`pwsh.exe`），不支持 Windows PowerShell 5.1（`powershell.exe`）。WPF adapter 入口仍必须使用 `-STA`。

```powershell
pwsh.exe -NoLogo -NoProfile -File .\Build-CountdownApplication.ps1 -Clean
.\verification\MinminCountdown.Verification.exe
pwsh.exe -NoLogo -NoProfile -STA -File .\Countdown.CSharp.Tests.ps1
pwsh.exe -NoLogo -NoProfile -STA -File .\Countdown.CSharp.Tests.ps1 -DesktopLauncherOnly  # 2026-08-23 起冷≤4.7s / 暖≤4.5s
& 'D:\Codex\tools\PowerShell\7.6.4\pwsh.exe' -NoLogo -NoProfile -STA -File .\Countdown.CSharp.Tests.ps1 -InstanceActivationMatrix  # 诊断并发 probe 共享截止 38900ms（真实矩阵 8×100 强制 1MB 滚动写最差外推 32381ms +20%）
pwsh.exe -NoLogo -NoProfile -File .\Countdown.Core.Tests.ps1
pwsh.exe -NoLogo -NoProfile -STA -File .\Countdown.Ui.Tests.ps1
pwsh.exe -NoLogo -NoProfile -STA -File .\Invoke-CountdownSourceValidation.ps1
pwsh.exe -NoLogo -NoProfile -File .\Install-Countdown.ps1 -ValidateOnly
pwsh.exe -NoLogo -NoProfile -File .\Deploy-OfficeCountdown.ps1 -ValidateOnly
```

`-DesktopLauncherOnly` 的冷/暖门槛按 2026-08-23 现役探针五对 runId `ca1179d53398413abb236fc0f4d7b490`、`58c35833a6bf4b5ebbab9e3dab973c2a`、`eb12765e030d413a8520bcbf78c4938c`、`9d6ff40ee86745aab70f4dd96522ce60`、`9cf91f34b4f242b5a93c3b04a2fe1f23` 校准。口径是真实首帧 WPF `ContentRendered`（命名事件 `Local\CountdownDesktopLauncherWindowReady`）；测量钟在首次 window-ready 之后才查询 `Win32_Process`。探针 stopwatch T0→T4 冷 p95/max=3776ms、暖 p95/max=3439ms，分段 Σp95+20% 取整为冷 4700ms、暖 4500ms。机器：BAKER / i5-14600KF / 可用内存约 7.3GiB / CPU 中低 / 2026-08-23 16:46–16:49 +08。样本只用于归因，不汇入门槛样本池。`-InstanceActivationMatrix` 的 38900ms 共享截止按真实矩阵 8×100 强制滚动写标定：2026-08-24 五次 Wait 完成 [1035, 1037, 1101, 1160, 1248]；2026-08-23 超时证据 run `20260823T1510260920479Z` 最慢写手 21/100 @ 6800ms，线性外推 32381ms，取最差 +20% 取整。样本只用于归因，不汇入门槛样本池。2026-08-23 连续三轮完整本机验证组合中诊断并发 probe 曾在 6800ms 内只完成约 43–86/100；不能把隔离绿灯当成矩阵或连续全套件绿灯。
以上命令只证明当前机器上的源码行为、发布包、安装与部署预检合同。`Deploy-OfficeCountdown.ps1 -ValidateOnly` 校验当前工作树候选的发布清单、`IntegrityHashes` 静态哈希和临时 ZIP，不要求工作树干净，也不执行 Git fetch；它不连接办公室设备，不上传或安装产物，更不构成办公室远端部署或验收。正式无开关部署仍只接受干净、已推送且与 `origin/main` 一致的 `main` 提交，办公室完成态仍需正式部署成功回执、传输账本和两次独立远端回读。正式部署在首次传输前会解析 Windows 内置 OpenSSH 或 PATH 中成对的 ssh/scp 绝对路径，并用选定 ssh 做约 32KB 只读 echo 行为探针；截断或找不到候选即闭锁。`-ValidateOnly` 不跑该解析和探针。构建产物 `verification\MinminCountdown.Verification.exe` 的字节由正式部署按本次构建锁定，不在 `-ValidateOnly` 里对照跨环境静态钉。

## 运行应用

```powershell
pwsh.exe -NoLogo -NoProfile -STA -File .\敏敏子倒计时.ps1
```

## 安装

安装前先运行 `Build-CountdownApplication.ps1 -Clean`。改了发布契约 `IntegrityHashes` 覆盖的源文件、测试或静态资产后，按当前提交 blob 更新 `Countdown.ReleaseContract.psm1` 里对应的 SHA-256；`verification\MinminCountdown.Verification.exe` 不进静态钉死，由构建和正式部署按本次产物字节锁定。安装器要求已安装 PowerShell 7+（`pwsh.exe`），缺失时会拒绝运行，不会回退到 Windows PowerShell 5.1。它会在全部预检通过后才取得全机安装事务锁：先记录计划任务和 Windows 时间服务状态，再冻结两份旧登录任务以阻止旧程序复活；旧程序静止后才快照安装目录与桌面启动器（含 `.new` 暂存文件），随后校准网络时间、运行测试、复制程序、把启动器安装到桌面，并另行注册登录任务。桌面双击 `敏敏子倒计时.exe` 直接启动宿主，不经过 schtasks、pwsh 或 adapter；登录任务仍执行 `pwsh -STA -File 敏敏子倒计时.ps1`。登录任务必须用本次启动令牌先写出“窗口首帧已渲染”的 `window-ready` 回执，安装器最多等待 30 秒；设置等价和提交前验收通过、事务进入仍可回滚的 `accepted` 后，应用还必须以同一令牌、进程、会话和应用路径写出 `finalized-ready`，安装器复核计划任务、受管 EXE 身份与运行环境后才把事务标记为 `committed`。正常失败会恢复快照（时间校准本身不可逆）。损坏 journal、缺少备份或用户在崩溃后替换桌面启动器时，安装器会保留证据并拒绝覆盖用户文件，而不是进行破坏性回滚。请在管理员 PowerShell 7 中从仓库根目录运行：

```powershell
pwsh.exe -NoLogo -NoProfile -File .\Install-Countdown.ps1
```

安装器只支持安装目录和交互用户桌面均位于 `D:\` 的设备，这是当前办公室设备的安全边界。

办公室设备的稳定部署方式见 [办公室部署](docs/办公室部署.md)。

## 重新编译启动器

```powershell
pwsh.exe -NoLogo -NoProfile -File .\Build-Launcher.ps1
```

重新生成 `CountdownLauncher.exe` 后，需要同步更新 `Countdown.ReleaseContract.psm1` 中对应的 SHA-256。

重新生成图标需要 Python 和 Pillow：

```powershell
python -m pip install Pillow
python .\make_countdown_icon.py
```
