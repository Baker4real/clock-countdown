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
- 应用固定为单一日间暖色主题，不再提供主题切换入口；旧设置中的任何主题值（Auto / Day / Night / 未知值）启动时一律按日间解释，不报错、不丢数据
- 同时保存和展示多个具名倒计时
- 倒计时与便签共用同一管理窗口和桌面悬浮入口；便签可新增、编辑正文、年月日日期和标签、勾选完成、删除。完整管理窗口用并排的“未完成 / 已完成”分段切换控件选择显示哪个状态列表：两段同屏可点、各带数量徽章，两个状态列表不同时显示，默认选中未完成；列表内按较新日期和同日较新添加项优先显示，每个日期分组都可独立收起、展开
- 每条便签和用户倒计时可在共享次级编辑器中管理最多 8 条绝对一次、固定间隔或类型化日/周/月/年日历规则；用户倒计时还支持到点或提前 `0..5,256,000` 分钟的相对目标规则。日历规则按北京时间解释、以规范 UTC occurrence 持久化，不存在日期跳过，显式“最后一天”按月长和闰年计算；冷启动和长时间延迟只补发每条规则最新的错过 occurrence。编辑倒计时目标会用相同 rule ID 安全重算未送达相对 occurrence，派生时刻已过去时整笔倒计时编辑不提交
- 到期便签与用户倒计时统一进入单一提醒中心；同一时刻的条目批量加入，后续条目追加并稳定排序，但每条规则最多保留一个未解决发生项，处于稍后或送达失败状态时不会再展示同规则的新基础周期。两类都提供“知道了 / 稍后提醒 / 打开来源”，只有便签提供“完成便签”，均不提供删除；打开来源会恢复管理窗口，保留既有草稿与筛选，临时揭示准确来源卡片并聚焦其编辑入口。知道了、稍后、完成便签及送达写盘失败都保留原状态和条目并按 5、10、20、30 秒退避重试。提醒中心与规则编辑器可从窗口头部拖动；提醒中心首次显示贴齐工作区右上，之后保留手动位置
- 倒计时与便签共用一套多选标签：可新建、改名、删除和关联；新建标签沿用暖白、蜜桃、便签黄和珊瑚色板，删除标签只解除关联，不删除任何倒计时或便签
- 标签筛选只作用于当前工作区，支持“全部 / 无标签 / 已选标签”；多选标签可按“任一 / 全部”匹配
- 倒计时可将标签条件与“进行中 / 已到期”叠加筛选；便签可继续叠加“未完成 / 已完成”、正文搜索，以及精确日期或“未设置日期”
- 管理窗口与桌面悬浮窗共享筛选条件；从悬浮窗打开管理窗口时沿用悬浮窗当前工作区。悬浮倒计时始终只显示进行中项目，便签快速新增在进入新增状态时以当时的筛选标签初始化；标签选择器未选择时显示“选择标签”，已选择时显示标签名称（两个以顿号并列，三个及以上显示“首个名称 +N”，超长截断为省略号），点击弹出复选列表可多选或逐项取消勾选
- 悬浮窗便签工作区提供折叠式正文搜索：“搜索便签”按钮或 `Ctrl+F` 切换到便签工作区、展开输入行并聚焦（占位“搜索便签”，最长 140 字符）；`Esc` 或“✕”清除钮清空搜索词并收起。提示条三态：无词且收起时不显示搜索行；收起且有词时显示“搜索:{词}”与“✕”，点击词钮重新展开聚焦；展开时显示输入行。搜索词与管理窗口共享同一筛选态，只忽略大小写匹配未完成便签正文，不写入设置
- 管理窗口便签页 `Ctrl+F` 直接聚焦便签搜索；倒计时页没有正文搜索，`Ctrl+F` 仍打开倒计时筛选。悬浮窗 `Ctrl+R` 或托盘菜单「复位窗口位置」把悬浮窗夹回当前工作区，完成窗口归位不依赖拖动
- 管理窗输入时可用 `Ctrl+1/2` 切换工作区、`Ctrl+T` 标签管理、`Ctrl+M` 收起、`Ctrl+S` 保存当前倒计时或便签；弹层打开时全局快捷键暂停。名称、标签名、月薪和工时字段 `Enter` 提交所在表单；两窗便签正文 `Enter` 换行、`Ctrl+Enter` 提交。列表行的 `Delete` 打开安全默认取消的删除确认；文本输入中的 `Ctrl+Z` 保留编辑撤销，离开输入后可撤销最近删除（#224）。按钮提示中可查看对应快捷键
- 倒计时和便签日期都通过主题化日历选择，点击日期文字或下拉按钮都可打开日历；倒计时的时、分、秒下拉点击文字区域即可打开，不要求点到箭头
- 倒计时编辑器通过提醒规则单选组选择“不提醒 / 按时间提醒 / 自定义提醒”，新增时默认不提醒；按时间提醒会在截止时刻提醒一次，自定义提醒可配置多条提醒规则
- 新增、编辑和删除后自动保存，启动时自动恢复
- 删除倒计时或便签前弹确认框且以取消为安全默认，并写明删除后 10 秒内可撤销；删除成功后，撤销出现在发起删除的窗口（悬浮窗底部状态行或管理窗口状态区），10 秒内可点击或按 Ctrl+Z，焦点或悬停在撤销按钮上时暂停计时，只保留最近一次删除（#214）。提醒中心「完成便签」先确认再提交；从悬浮窗、托盘或 Alt+F4 退出时都会先确认，并写明退出后不再提醒；有未保存的编辑器草稿或显示设置时，确认框会列出这些项。取消则留在应用。只隐藏管理窗口则继续保留草稿
- 图标按钮提供自动化名，装饰手写体与纯装饰图标不参与屏幕阅读器朗读；可见字段标签通过标签关系指向输入；数量变化由单一状态区整句播报
- 每次合法保存维护 `settings.json.backup1` / `.backup2` 两代设备本地备份；主设置损坏或不可读时只提供“恢复备份 / 退出”，不会静默恢复或以空设置继续。恢复前保留可读的原始损坏字节，且只有从主路径回读校验成功后才启动窗口
- 旧版单倒计时设置自动迁移为“我的倒计时”
- 到期项目保留在管理列表并显示“已到期”；悬浮窗仅显示进行中的项目，按截止时间排序
- 可输入月薪和上下班时间，实时计算“今日已赚”
- 工资按当月实际工作日计算，并同步法定带薪休假和调休补班日历；日历按本地缓存、联网结果、随包年度数据的顺序取值，日历未就绪时不会显示估算金额，悬浮窗底部状态会说明影响范围与恢复方式（「节假日安排未就绪：距离下班和今日已赚暂不可用，联网后自动恢复」）。随包数据的来源、覆盖年份、许可证和更新方式见 [holiday-data/README.md](holiday-data/README.md)；北京时间进入 12 月后若随包数据尚未覆盖下一年，打包、真实安装和部署入口会明确失败并提示补数据；发布契约导入、构建、测试和安装验证不执行该守卫（#218、#261）
- 节假日日历已就绪时，悬浮窗会在实际工作日、下班前把“距离下班”倒计时固定显示为首行唯一主项，使用主项的粉色底、红色强调、字体和数字色块分格的剩余时间效果，并显示“超期待！”徽章；它不是用户记录，没有规则集合、编辑入口或提醒 occurrence。没有“距离下班”时，第一个普通倒计时仍是主项
- 登录 Windows 后自动启动，默认以小型悬浮框显示所有进行中的倒计时
- 悬浮框除按钮、输入框和滚动区等交互控件外，整个背景区域都可拖动；手动位置在内容刷新后保留，并按当前工作区边界收紧，点击“管理”打开完整编辑窗口，关闭管理窗口不会停止悬浮显示
- 点击悬浮框标题栏的“缩小”按钮会隐藏悬浮窗；应用运行期间系统托盘图标常驻，单击图标或选择“打开悬浮窗”可恢复悬浮窗。托盘菜单为“打开管理窗口 / 打开悬浮窗 / 复位窗口位置 / 退出”；“打开管理窗口”会恢复管理窗口并保留草稿与筛选。管理窗口的“最小化”和 Ctrl+M 只收起管理窗口，悬浮窗保持可见
- 悬浮框固定为 `520 x 390`；首次显示自动贴齐工作区右下方并保留任务栏安全间距；手动移动后会继续保持可见，内容较多时列表滚动显示。现役双工作区的精确布局、安全区和排版合同见 [DESIGN.md](DESIGN.md)
- 多显示器与每显示器 DPI（#219）：悬浮窗保持当前屏上的手动位置，越界、`Ctrl+R` 或托盘复位只夹回当前屏；提醒中心首次放在悬浮窗所在屏右上，手动移动后保持。规则编辑器和确认框在所有者所在屏居中并夹取；规则编辑器高度为 `min(790 DIP, 工作区高度 - 36 DIP)`，底部按钮和缩放手柄保持可达。应用 EXE 清单声明 `PerMonitorV2`，显示器、工作区或 DPI 变化后已打开窗口重新夹回可见区域。
- 普通交互模式按当前用户、应用根和设置路径单实例运行；第二次普通启动通过有界命名管道请求恢复并聚焦现有悬浮窗，只有收到匹配协议、scope 和 request identity 的确认才成功退出。托盘、隐藏或最小化状态都会恢复，已可见时只聚焦；该动作不显示管理窗口、不改变筛选或草稿，也不写设置
- 主窗口和悬浮窗沿用暖色日间主题；装饰 raster 与真实 WPF 动态控件保持分层，具体视觉和命中测试合同以 [DESIGN.md](DESIGN.md) 为准
- 主窗口立绘的来源与私人使用边界见 [三丽鸥官方背景素材](docs/三丽鸥官方背景素材.md)；悬浮窗玩偶的来源与非生成式处理说明见 [悬浮窗玩偶素材来源](assets/floating-plush/SOURCES.md)
- 本机滚动诊断日志记录启动、正常退出、窗口就绪、单实例激活结果、节假日日历状态和致命异常；激活诊断只保存非内容身份与窗口状态，不记录筛选值、草稿、便签正文、倒计时名称或设置 JSON

## 文件说明

- `敏敏子倒计时.ps1`：C# 应用的薄启动 adapter；映射既有参数。生产启动用 `Win32_Process.Create` 脱离计划任务作业，确认 C# 仍存活后退出 0；validate/exercise 仍等待并传播退出码。不回退旧 PowerShell WPF
- `src/MinminCountdown`：C# WPF 应用宿主源码
- `src/MinminCountdown.Core`：设置 schema、校验、原子写入、跨进程 mutex、部署设置等价规则，以及基于固定 `Ical.Net 5.2.3` 的类型化日历 occurrence 计算
- `src/MinminCountdown.Verification`：C# 设置契约、PowerShell 部署等价夹具快照与互操作验证程序；只引用 Core，保持无窗口
- `src/MinminCountdown.WindowVerification`：#273 独立 WPF 窗口验证项目，引用产品程序集并产出第一条迁出的空态事实；仅由低影响 CSharp 阶段在隔离桌面启动，不并入纯 C# runner，也不进入办公室载荷
- `Build-CountdownApplication.ps1`：生成 self-contained 的应用、纯 C# 验证与独立窗口验证 EXE，并复制、校验发布契约声明的 14 个程序目录随包资源：9 个现役图片资产，以及 #275 起的随包节假日数据 `app\holiday-data\` 与图标 `app\countdown-icon.ico`（#231 起三张 floating-plush 派生姿态参考图只留在仓库作视觉基线 sourceBinding 输入，不再装进 `app`）
- `app/MinminCountdown.exe`：生成的 C# WPF 应用
- `verification/MinminCountdown.Verification.exe`：生成的设置契约验证程序
- `window-verification/MinminCountdown.WindowVerification.exe`：生成的独立窗口验证程序；不是生产入口，不提供直跑方式
- `Countdown.Notes.psm1`：便签候选状态变更与持久化事务
- `Countdown.Tags.psm1`：共享标签的领域模型、筛选语义与显示名称解析
- `Countdown.Runtime.psm1`：每次启动的令牌、窗口首帧就绪回执、有界等待与滚动诊断日志
- `Countdown.Settings.psm1`：设置 schema 迁移、校验、原子写入与部署等价校验
- `Countdown.InstallTransaction.psm1`：带全机互斥锁的可回滚安装事务；覆盖安装目录、启动器、两份计划任务与 Windows 时间服务状态，并为办公室验收原子写出独立回执；`Get-CountdownInstallLayout` 是安装布局的唯一定义
- `Countdown.InstallLifecycle.psm1`：安装事务生命周期；本机安装器与办公室部署 adapter 共用的装入候选、事务验收与回滚，计划任务、时间服务、进程与桌面图标经 port 注入（生产用 Windows port）
- `Countdown.Validation.psm1`：本机正式安装运行的 Core、C# 与应用 validate-only 验证 module；安装器只在本机直接安装时从仓库加载，办公室延迟提交路径不调用，也不进办公室发布包
- `Countdown.RemoteDeployment.psm1`：办公室候选安装（不跑测试）、事务验收、回滚、提交后收尾，以及成功/失败回执、部署 binding、独立回读与回滚前诊断的严格契约 module
- `Countdown.TransferLedger.psm1`：办公室全部 SCP 与 SSH 证据回读共用的计时、吞吐和失败尝试账本 module
- `Countdown.ReleaseContract.psm1`：构建、安装、部署和 Core tests 共用的发布资产分类与 SHA-256 契约
- `tools/Countdown.BuildPreflight.psm1`：构建前只读解析 SDK、构建目录、safe-tree-delete 能力和载荷清单记录的源码提交标识；`Test-CountdownBuildPreflight.ps1` 用于从任意 cwd 做预检
- `CountdownLauncher.cs`：无控制台桌面启动器源码；仓库编译产物为 `CountdownLauncher.exe`
- `CountdownLauncher.exe`：仓库中的启动器文件；安装后桌面名为 `敏敏子倒计时.exe`，双击直接启动宿主
- `countdown-icon.ico`：窗口、托盘和桌面启动器图标；构建另复制一份进程序目录 `app\`，应用托盘从那里读取（#275）
- `sanrio-official-mymelody.png`、`sanrio-official-cinnamoroll.png`、`sanrio-official-kuromi.png`：主窗口兼容回退与来源校验使用的三张官方透明角色立绘
- `assets/floating-plush/*.png`：悬浮窗现役视觉资产；#231 起 `floating-plush-*-reference.png` 三张派生姿态参考图仅作视觉基线 sourceBinding 输入留在仓库，不再安装到 `app`；来源、处理边界、哈希和用途见 [悬浮窗玩偶素材来源](assets/floating-plush/SOURCES.md)
- `assets/main-decorations/*.png`：主窗口暖色日间装饰资产；现役用法见 [DESIGN.md](DESIGN.md)
- `assets/ui-references/`：用户生成的页面与悬浮窗参考图及机器可读 `manifest.json` 的固定位置，作为视觉复刻比对依据随仓库版本化；不会进入 `app` 的运行时资产清单
- [DESIGN.md](DESIGN.md)：主窗口两页、悬浮窗两工作区与提醒中心的「暖粉日间」现役视觉、交互和单一日间主题合同
- `Install-Countdown.ps1`：安装与更新入口；只做发布包校验、预检、本机直接安装时的验证套件与结果输出，安装本身交给安装事务生命周期
- `Deploy-OfficeCountdown.ps1`：办公室设备的受校验部署入口
- `Get-CountdownInstallStatus.ps1`：只读状态命令，默认读 `D:\敏敏子倒计时` 与登录任务 `Minmin Countdown`，输出一份 ASCII JSON：载荷清单状态（`present`/`missing`/`invalid`）、清单记录的提交与聚合哈希（清单缺失或没记提交时为 `null`，即未知；构建时工作树有改动则提交带 `-dirty`）、已安装 EXE 是否与清单一致、映像路径是已安装 EXE 的进程（路径、会话、启动时间）、登录任务与期望定义不符的项、设置文件的长度/修改时间/SHA-256，以及安装根下是否留有未完成安装的事务标记 `install-transaction.json`、此刻是否有安装、切换或回退正在运行（`installInProgress`：只打开已有的全机安装锁看它在不在，不取锁、不等待）。不写文件、不启停进程，输出不含设置内容、凭据、主机名或账号；脚本自包含，可交给远端执行入口直接运行，`-Port`、`-LockName` 只供测试
- `Install-CountdownProgram.ps1`、`Countdown.Installer.psm1`：新安装器（#259）的入口与安装事务 module，本机与远端同一个安装器，首次安装与升级同一流程：取得全机安装锁（与旧安装器同名，互斥）；中断恢复（#284，见下）；前置检查（安装路径、唯一的交互会话及其桌面目录（按交互用户 SID 的注册表环境解析，不用当前进程的环境）、候选程序目录 `app.candidate` 按载荷清单逐项核对）；停止应用（结束映像路径是已安装 EXE 的进程，结束前按 PID 重读路径，不经计划任务停止，并等登录任务没有运行中的实例，见 [#279 实测](docs/evidence/clock-countdown-issue-279/README.md)）；写事务标记 `install-transaction.json`（记下候选的提交与聚合哈希，先写临时文件 `install-transaction.json.tmp` 再改名）；把 `settings.json` 及两代备份逐字节快照到事务目录 `install-transaction\settings-snapshot\`（快照里没有的文件安装前就不存在），有设置文件时用候选 EXE 的 `--settings-migration` 在 `install-transaction\settings-rehearsal\` 的副本上演练迁移，再用候选 EXE 的 `--settings-equivalence`（传入迁移命令报告的 `migrationUtc`）核对快照与迁移结果（#282）：不等价或演练没能完成时不换程序，按下面的回滚重新启动旧版本并报告失败，程序目录与现役设置都没被动过；首次安装没有设置文件时跳过演练；现役 `app` 改名为 `app.previous`、候选改名为 `app`（被短暂占用时各自有界重试，模块参数 `-FileLockRetrySeconds` 默认 15 秒，仍被占用时原因为 `program-directory-in-use`）；登录任务不存在时按 #279 的最终定义创建（优先级 4，电池供电也启动、拔电不停），已存在时逐项比较（启用、动作、触发器、主体用户、登录类型、运行级别、优先级、运行时限、多实例、两项电池设置、按需启动、强制结束；用户按 SID 比较，结果只写“交互用户/别的用户/不限用户”），一致时不动，不一致时整份重新注册，结果 `loginTask.changes` 逐项写明改前改后；登记交互用户桌面上的快捷方式 `敏敏子倒计时.lnk`（目标是已安装 EXE，无参数，工作目录是 `app`，图标取 EXE）：缺失时创建；“属于安装器”指同名项是 Shell Link（按文件头认）且目标是本安装根的已安装 EXE，属于时参数、工作目录或图标不一致才改回并写明改动；不属于（目标指向别处的快捷方式、碰巧同名的普通文件或目录）时原样保留，结果写 `kept-foreign` 与原因 `other-target` 或 `not-a-shell-link`，不写它指向哪里；旧安装器的桌面启动器 `敏敏子倒计时.exe` 不碰；删除旧的运行状态回执，经登录任务启动，30 秒内等到启动就绪（回执由存活进程写出，其映像路径是已安装 EXE、会话是交互会话、EXE 哈希与载荷清单一致，不用启动令牌），启动的进程在启动就绪之前就退出时（登录任务的那次运行已经结束，已安装 EXE 也没有进程在运行）不等满时限，失败原因为 `startup-exited`（#288）；先清除事务标记，再删除旧版本目录与事务目录。写下事务标记之后、新版本达到启动就绪之前任何一步失败都回滚（#283）：停止已安装 EXE 的进程；按目录现状换回程序目录（`app` 是新版本时先改回 `app.candidate`，再把 `app.previous` 改回 `app`；首次安装把新版本改回 `app.candidate`）；程序换过时用本次安装的快照恢复 `settings.json` 及两代备份（与快照逐字节相同的不动，不同的先复制到 `install-transaction\settings-restore\` 再整份替换，快照里没有的删除）；本次更新过的登录任务按改之前导出的整份定义恢复，本次创建的登录任务升级时保留（它指向同一个 EXE 路径，旧版本要经它重新启动）、首次安装时删除；首次安装删除本次创建的桌面快捷方式，升级不动快捷方式（新旧版本是同一个 EXE 路径，快捷方式与版本无关）；有旧版本时经登录任务重新启动它并等到启动就绪；这些都做到了才清除事务标记，再删除候选与事务目录。首次安装回滚后安装根里没有程序目录，也不留本次创建的登录任务与快捷方式。回滚任何一步没做到时结论为 `rollback-failed`，事务标记、事务目录（含快照）与候选原样留下，交给中断恢复（#284）。新版本达到启动就绪之后不再回滚，也不再用快照覆盖设置（用户可能已经在编辑），提交没走完时事务标记留给下一次安装的中断恢复。中断恢复（#284）是取得安装锁之后的第一步，不设多阶段的事务日志，按事务标记与目录状态（`app` 是不是事务标记记下的那一版，按载荷清单的聚合哈希认）把上一次没走完的安装收拾好，同一次调用再继续本次安装：没有事务标记却有残留（`app.candidate`、`app.previous`、事务目录或 `install-transaction.json.tmp`）时视为已提交，删掉残留，现役 `app` 与在运行的应用不动；候选还在（换程序之前）时删除候选与事务目录、清除标记；`app` 不在而 `app.previous` 在（两次改名之间）时把旧版本改名换回；`app` 已是新版本时补做启动验收：它已在运行就按回执核对（不再经登录任务启动），没在运行就经登录任务启动并等到启动就绪，达到就提交；达不到时设置及两代备份与快照逐字节相同就停下新版本、换回旧版本（首次安装回到没有程序目录），旧版本不重新启动，随后的安装启动新包；设置已经变化时什么都不改（`needs-maintainer`，原因 `settings-changed`，交给维护者决定），没有或有多个交互会话时什么都不改（`left-as-is`，原因同前置检查）；程序目录的组合不是任何一步安装能留下的（如三个程序目录都在）时什么都不改（`needs-maintainer`，原因 `unrecognized-state`）。只有补做启动验收要交互会话，其余收拾不启动任何东西，没人登录也照做。每个状态都先把 `app` 放成要留下的那一版，再清除事务标记，最后删除其余残留：做到一半失败（`failed`，原因 `recovery-failed`）或被打断时留下的仍是上面的某个状态，下一次安装接着恢复。恢复从不用快照写设置，也不动登录任务与桌面快捷方式（新旧版本是同一个 EXE 路径；被打断的安装改登录任务前导出的定义只在那次安装的进程里，没有落盘），随后的安装照常登记两者。收拾不了时安装停在 `recovery` 阶段、不继续。目录改名、设置写回与安装器自己的删除被短暂占用时都有界重试。只替换程序目录，恢复证据、诊断日志与节假日缓存不被移动或改写，设置及两代备份只复制进事务目录、只在上述回滚里写回快照；前置检查失败时只删除候选。`-CheckOnly` 执行到前置检查为止，报告将要执行的步骤（不读桌面上的快捷方式，计划里总有 `register-desktop-shortcut`；不做中断恢复，只在 `recovery` 里报告看到的状态，`outcome` 为 `planned`，计划最前面是 `recover-unfinished-install`）。输出一份 ASCII 结果 JSON：成败 `succeeded`、失败阶段 `failedStage` 与原因 `reason`、截短到 400 字的错误摘要 `error`、中断恢复 `recovery`（`state` 为 `clean`、`leftovers`、`before-replace`、`mid-replace`、`after-replace` 或 `unrecognized`；`action` 为 `removed-leftovers`、`discarded-candidate`、`renamed-back`、`committed` 或 `swapped-back`，没做时为空；`outcome` 为 `not-needed`、`recovered`、`planned`、`left-as-is`、`needs-maintainer` 或 `failed`；`reason` 是停下的原因；安装在安装锁之前就停下时整个为空）、设置迁移演练的结论 `settingsRehearsal`（`outcome` 为 `equivalent`、`not-equivalent`、`failed` 或 `skipped-no-settings`，另带两条命令的原因类别）、回滚结果 `rollback`（`outcome` 为 `not-needed`（没有要撤回的改动，或新版本已达到启动就绪）、`rolled-back` 或 `rollback-failed`；`settings` 为 `restored` 或 `unchanged`，`loginTask` 为 `restored`、`removed` 或 `kept`，`desktopShortcut` 为 `removed`，没做这一步时为空；`problems` 逐条列出没做到的步骤，每条截短）、回滚后重新启动的旧版本进程 `restartedProcess`、新版本在启动就绪之前退出（`startup-exited`）时它的退出码 `startupExitCode`（登录任务的上次结果，直接运行 EXE 的任务就是进程的退出码，#279；其余情况为空）、安装结束时事务标记是否还在 `transactionMarkerPresent`、应用诊断日志的位置 `diagnosticLogPath`（`<安装根>\logs\application.jsonl`，新版本起不来的原因记在这里）、登录任务 `loginTask` 与桌面快捷方式 `desktopShortcut` 各做了什么、设置在安装前后的指纹，不含设置内容、凭据或账号。不运行测试套件、不检查构建工具链、不碰 Windows 时间服务。计划任务、进程、交互会话、桌面目录、快捷方式、目录改名与候选 EXE 的无界面命令经 port 注入，`-Port`、`-LockName` 只供测试。尚未接入发布包与办公室部署
- `Deploy-CountdownProgram.ps1`、`Countdown.Deployer.psm1`：新部署入口（#259 / #285 / #286）与编排 module，日常两种模式：部署，和 `-StatusOnly`（一次只读状态读取，不做任何改动、不写记录）；另有一次性的 `-LayoutSwitch`（走部署流程，包里另带切换脚本，安装一步运行它，探测读到当前提交已在运行也照常切换）与 `-LayoutRollback`（一次远端写操作运行办公室备份里的回退，结论 `rolled-back`），见下一条与 [办公室布局切换](docs/办公室布局切换.md)；三种模式一次只能选一种，同时给出时 `rejected`、原因 `conflicting-modes`。部署：本机预检（main、工作树干净、与 `origin/main` 一致；`logs\suite-runs` 里有同一提交、本 checkout、开跑与收尾都干净的套件记录，必过阶段取自 `tools/Countdown.SuiteStages.psm1`，Build 回执是 `CleanR2R` 并带 `PayloadAggregateSha256`；`app\published-payload.json` 记的提交是 HEAD，聚合哈希等于该回执）；把载荷清单列出的程序目录文件、载荷清单和新安装器打成一个 ZIP（边写边核对逐文件 SHA-256，不重新导出、不重新构建）；经远端执行入口做只读连通性探测，探测就是一次状态读取（结果的 `statusBefore`）：读到 HEAD 已在运行（状态的提交是 HEAD、已安装 EXE 与办公室的载荷清单一致、应用在运行、没有事务标记）就报告 `already-running` 并退出，不传输、不重复安装；经传输入口送到 `<暂存根>\clock-countdown-<运行编号>\package.zip` 并核对远端哈希；经远端执行入口以写操作核对包哈希、解包、运行包内 `Install-CountdownProgram.ps1` 并取回结果 JSON，随后删除暂存目录；再用 `Get-CountdownInstallStatus.ps1` 只读读状态，提交、EXE 哈希与达到启动就绪的进程都与预期一致才报告成功；最后写 `<记录根>\<运行编号>\deployment-record.json`（提交、包哈希、安装结果、状态快照、传输账本引用；预检通过后无论结论如何都写，写不成时本该成功的结论改报 `failed`、原因 `record-write-failed`，其余结论原样保留并在 `error` 里补一句），本机的包不保留。进度写标准错误，最后一行是带 `outcome` 的结论；结果 ASCII JSON 写标准输出，退出码总是 0。`outcome` 有五种：`deployed` 与 `already-running`（`succeeded` 为 `true`）；`rejected`（连接办公室之前停下，`succeeded` 为 `false`，`reason` 为 `not-main`、`dirty-work-tree`、`not-pushed`、`suite-evidence-missing`、`required-stages-incomplete`、`build-receipt-not-clean`、`build-output-missing`、`build-output-invalid`、`aggregate-mismatch` 或打包时的 `build-output-modified`，`error` 写明缺什么；另有办公室上另一个安装、切换或回退正在运行、包内入口什么都没改就停下的拒绝，`reason` 为安装器的 `install-in-progress` 或切换、回退的 `transaction-in-progress`，`nextStep` 是等它结束、先用 `-StatusOnly` 读状态）；`failed`（`succeeded` 为 `false`：`probe-failed` 时不传输，`transfer-failed` 时不安装，`install-failed` 时 `rollback` 是回滚结果：包没能交给安装器为 `not-needed`，安装器报告失败时取它结果里的 `rollback.outcome`（#283：`not-needed`、`rolled-back`、`rollback-failed`），没报为 `null`；`status-mismatch` 是安装器报告成功、安装后的状态在提交、EXE 哈希或进程上与预期不一致）；`unknown`（`succeeded` 为 `null`，不知道办公室上的结果：连接在安装途中断开或安装器开始运行却没给结果为 `install-result-unknown`，安装后读不到状态为 `status-unreadable`）；意外错误的原因类别是 `unexpected-error`。结果未知、回滚失败或安装器没报回滚结果时，`nextStep` 提示先用 `-StatusOnly` 读状态：有事务标记 `transactionMarkerPresent` 就重新部署，安装器先恢复再安装（#284；恢复不了时安装停在 `recovery` 阶段，部署结果的 `install.recovery` 写明看到的状态与原因）。远端执行与传输经 port：生产接主机的 `Invoke-RemoteStep.ps1` 与 ssh-transfer-ledger 的 `Invoke-OfficeScpTransfer.ps1`，传输账本放在主机传输记账根下而不在部署记录目录里；输出与记录不含主机名、地址、账号或凭据，`-Port` 只供测试。尚未在办公室实跑；旧入口 `Deploy-OfficeCountdown.ps1` 照常可用
- `Switch-CountdownInstallLayout.ps1`：一次性布局切换（#287），把办公室安装从现有布局（登录任务经 pwsh 运行 adapter，桌面上是启动器 EXE）切到新布局（登录任务直接启动 `app\MinminCountdown.exe`，桌面上是快捷方式），经新部署入口的 `-LayoutSwitch` 随部署包运行。切换与回退整个运行期间持有全机安装锁（与新旧安装器同一把，新安装器在同一线程里重入），另一个安装、切换或回退正在运行时停在前置检查（`transaction-in-progress`），什么都不改。前置检查只读地看当前进程是否提权，并按两个现有任务的 DACL 判断能否替换、删除它们，不能就停下（`task-access-denied`，要提权运行）；按映像路径停下旧版本；把安装根、现役与遗留任务导出的定义、桌面启动器文件、本脚本与新安装器 module 备份到 `<安装根>.layout-switch-backup`，最后写 `backup.json`（备份不完整时切换不开始；备份途中失败时删掉写了一半的备份，重新启动旧版本）；经新安装器换布局；移除遗留任务、属于旧安装器的桌面启动器与安装根里的旧文件。用户数据不被移动或改写，认不出的条目原样保留并列出。备份之后任何一步失败都停下报告，备份保留，再次运行沿用这份备份接着做。`-Rollback` 从备份的 `tools\` 运行（新部署入口的 `-LayoutRollback`），让安装根、两个任务与桌面启动器回到切换前，被覆盖或删除的文件先留在备份目录的 `after-switch-<时刻>\`。输出一份 ASCII 结果 JSON；`-Port`、`-LockName` 只供测试。步骤与判读见 [办公室布局切换](docs/办公室布局切换.md)
- `Countdown.Core.Tests.ps1`：核心与发布契约测试可直跑；含 `-ReleaseContractOnly` 时会嵌套验证套件窗口步骤（自行隔离），不要当成全程无窗口。入口只留参数、公共断言、分段开关、计时与收尾，各分段正文在同目录 `Countdown.Core.Tests.<分段>.ps1`，由入口点源执行，不单独运行
- `Countdown.CSharp.Tests.ps1`、`Invoke-CountdownSourceValidation.ps1`：窗口套件内层实现，仅由低影响启动器转发，直跑 fail-closed
- `tools/Invoke-CountdownVerificationRun.ps1`、`tools/Invoke-DesktopLauncherLatencyProbe.ps1`：标准化 12 步收口验证 runner 与桌面启动 T0–T4 分段时延探针；收口包含 `Countdown.TransferLedger.Tests.ps1`，探针样本只用于归因，不汇入门槛样本池
- `tools/Invoke-CountdownSuiteLowImpact.ps1`：低影响官方套件启动器（BelowNormal + 默认不可见 Win32 桌面隔离 + 用户桌面泄漏监测 + 红即停），五阶段套件默认入口；Build 绿后 Core/CSharp/SourceValidation/InstallValidate 四阶段并行调度（`-MaxParallel` 默认 2，范围 1-5；每阶段唯一隔离桌面），任一阶段红即取消在跑阶段并保留已完成日志与 summary（参考基线待重拍的退出码 75 除外，见「本机验证」）。`-IsolatedDesktop` 可显式传入（兼容旧命令）；关闭隔离必须 `-AllowUnisolatedDesktop`（用户正在用电脑时禁止）。`tools/Countdown.SuiteStages.psm1` 是当前必过五阶段（Build / Core / CSharp / SourceValidation / InstallValidate）的唯一来源，启动器、运行记录与办公室部署门都读取它；`Countdown.SuiteRun.psm1` 负责原子 run/summary（替换目标被杀毒扫描等瞬时占用时重试约 2 秒）、阶段超时、取消和失效 owner 的 `Interrupted` 回读；run.json 的 `Source` 记录开跑与收尾时的 HEAD 和工作树是否干净，办公室部署据此核对同一提交的本机验收证据；配套 `tools/Start-IsolatedSuiteProcess.ps1`（隔离桌面进程启动）与 `tools/Watch-DesktopWindows.ps1`（用户桌面窗口泄漏监测）
- `tools/Test-CountdownTooling.ps1`：不在用户桌面开窗的自检：issue 验收 schema、回写 dry-run、PowerShell 解析、弹层验证夹具 C# 只编译、日间纯装饰图生成契约（`tools/Test-MainDayOrnamentScene.ps1`）、隔离启动器崩溃对话框看门狗（`tools/Test-IsolatedCrashDialogWatchdog.ps1`，对话框只弹在自建的隔离桌面上）、套件状态原子写的瞬时占用重试（`tools/Test-SuiteRunStateWrite.ps1`）、CI 单独成步的 `Countdown.TransferLedger.Tests.ps1` 与 `tools/Test-OfficeDeploymentStatus.ps1`、CI 工作流脚本都有本机阶段跑到的对等检查、生产脚本 `Move-Item -Force` 只减不增、通过的子测试不在 `logs\` 与仓库根留下 `*-test-*` 测试根和 `git diff --check`；`tools/Test-PopupValidationContract.ps1` 由低影响启动器的 `ValidationHarness` 阶段执行弹层真实动作与反例，夹具 C# 在 `tools/PopupValidationContract.cs`
- `docs/agents/issue-acceptance.md`、`docs/agents/issue-acceptance.json`：按 docs / behavior / visual / release / office / gate 区分检查、证据和明确的 N/A 理由；`tools/Invoke-IssueWriteback.ps1` 默认只生成可回读的评论计划，只有显式 `-Apply` 才写 GitHub

设置格式、迁移和持久化事务见 [设置与持久化](docs/设置与持久化.md)；产品 EXE 的 `--settings-equivalence` 只读判定、`--settings-migration` 无界面迁移，以及两者的 JSON 原因类别和退出码见同文「办公室部署边界」。
当前迁移决策、WPF module/seam 划分和 Tauri 闸门边界见 [架构与语言迁移](docs/架构与语言迁移.md)；父级 PRD 为 [GitHub #189](https://github.com/Baker4real/clock-countdown/issues/189)。
WPF 渐进升级批次、完成门和后置切片方案见 [现代化重构方案](docs/现代化重构方案.md)；现行栈状态与候选闸门范围见 [技术栈决策](docs/stack.md)。Tauri/React/Rust 文档只描述可回滚的候选切片，不代表已批准的目标栈。
倒计时、便签与共享标签的产品证据、现役边界和后续候选见 [倒计时、便签与标签协同调研](docs/标签与倒计时应用调研.md)。
悬浮窗参考图复刻的 GitHub 候选、采用路线、数值边界和明确不采用项见 [悬浮窗视觉复刻 GitHub 方案调研](docs/悬浮窗视觉复刻%20GitHub%20方案调研.md)。

## 本机验证

界面源码导航：管理窗口布局集中在 `src/MinminCountdown/MainWindow.xaml.cs`，弹层动效集中在 `src/MinminCountdown/MotionBehavior.cs` 的 `PopupMotionBehavior`。

硬约束：本项目的 PowerShell adapter、测试、安装和办公室部署只支持 PowerShell 7+（`pwsh.exe`），不支持 Windows PowerShell 5.1（`powershell.exe`）。WPF adapter 入口仍必须使用 `-STA`。

硬约束：套件的默认运行前提是用户正在使用这台电脑。Build / Core / CSharp / SourceValidation / InstallValidate 五个阶段**仅启动器可跑**，禁止把内层命令当复制模板直跑。启动器默认把整棵进程树放到 BelowNormal，并把控制台与 WPF 窗口放进不可见 Win32 桌面（窗口不上用户屏幕、不抢焦点）；用户 Default 桌面若出现敏敏子 / MinminCountdown 可见顶层窗即红。任一阶段红即停（参考基线待重拍除外，见下文），日志落在 `logs\suite-runs\`，内容是原始字节，编码由写入的进程决定：多数为 UTF-8，未自设编码的进程（如 InstallValidate 的安装器）按控制台代码页写出；中文 Windows 上按 UTF-8 读出乱码时改用 `Get-Content -Encoding 936`。CSharp / SourceValidation 若检测到自己不在 `MinminSuiteIso-*` 桌面会 fail-closed。关闭隔离必须显式 `-AllowUnisolatedDesktop`（用户正在用电脑时禁止）。隔离桌面上没人能点掉崩溃对话框：套件进程树里有进程弹出“has stopped working / 已停止工作”对话框时，隔离启动器以退出码 126 结束该进程，并把对话框标题与正文写进该阶段的 err 日志。本机微信输入法会把 CrashRpt 注入 GUI 进程，它缺 CrashSender.exe，任何 GUI 进程崩溃都会弹 “Error launching CrashSender.exe”。

截图留存：每次运行收口时按 `logs\suite-runs\*-run.json` 的 `StartedAtUtc` 排序（当前运行计入），只保留最近 30 次运行开始之后（含边界）的 `logs\ui-captures\` 时间戳截图目录；无法解析时间的目录保守保留。所有运行记录和 `*-summary.json` 摘要长期保留，部署记录引用的摘要也不会清理。实际目录树经 `Invoke-ManagedRecycle` 的 Check / Plan / Apply 移入回收站，不做永久 Purge。清理失败不改变既定阶段或整轮结果，并在 `run.json` 的 `ScreenshotRetention` 中记录 `Failed` 与 `Error`。新规则第一次在真实日志目录生效前，必须先列出精确删除目标和体积并取得用户当场确认：未确认时记录 `ConfirmationRequired` 且不执行 Apply；交互启动时显式加 `-ConfirmFirstScreenshotRetention`，收口会列出逐目录路径、字节数与计划 SHA-256，只有输入对应的 `RECYCLE <PlanSha256>` 且复核计划未变化才放行。首次确认及成功清理的回执写入本轮新 `run.json`，后续运行按常规留存自动清理，不改写历史运行。临时目录聚焦测试用 `pwsh.exe -NoLogo -NoProfile -File ./tools/Test-SuiteScreenshotRetention.ps1`，不会触碰真实 `logs`。

```powershell
pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage Build,Core,CSharp,SourceValidation,InstallValidate
```

启动器的单实例锁按登录会话共享，别的 worktree 里的启动器也算在内。默认被占即拒绝，并列出疑似持有者 PID；加 `-WaitForLockSeconds <秒>` 则最多排队这么久。阶段失败时，启动器在结尾打印该阶段 err 日志的摘要：取最后一个 PowerShell 错误块，UTF-8 与 936 逐行解码。完整日志仍在 `logs\suite-runs\`。Build 的受管清理遇到删除门锁被别的会话占用（`deployment-mutex-acquire-timeout`，门每次等 2 分钟）时最多尝试 5 次；其余清理失败立即报错。含 Build、Tooling 以外阶段的运行，开跑前先采样三次后台负载：CPU 总占用不低于 80%，或 GPU 最忙的引擎类型不低于 50% 时，列出最忙的进程并拒绝开跑，因为挖矿或游戏吃满资源时参考帧 300ms 超时会被误报成回归。启动器只报告、不结束任何进程，CI 不做这项检查；确认要照跑时加 `-SkipLoadPreflight`。进程起落时（例如上一轮启动器的子进程刚退出），个别实例读不到只丢那几条；缺了 CPU 总量、GPU 引擎或进程整类计数器就隔一秒重取，同一次采样连取五次仍缺才跳过预检，并打印 `Get-Counter` 的错误原文。采样在 `tools/Countdown.SuiteBackgroundLoad.psm1`，启动器只决定哪些阶段要预检、阈值、跳过与报告；采样行为由 Tooling 跑的 `tools/Test-SuiteLoadPreflight.ps1` 守住：它正常导入这个 module，注入假计数器与假等待，不读真实计数器。

源码树模式下改了视觉源码而尚未重拍时，参考基线必然失配。CSharp 不在第一个基线断言处中止：应用把失配项记进套件给的延后记录、照常输出报告，套件跑完其余断言后在 err 日志末尾列出延后项，并以 75 退出。启动器把 75 记为该阶段失败，但不触发红即停，其余阶段照常跑完；整轮仍判失败（“参考基线待重拍：…”），办公室部署要求的同一提交全绿证据不变。安装验证和基线生成后的自检始终严格。视觉源码没改却失配时，先按 [DESIGN.md](DESIGN.md) 视觉基线节核对显示缩放，不为过门禁重拍。

参考帧捕获前会清掉键盘焦点，焦点描边因此不在任何基线里。源码树模式下 validate-only 另开一个一次性主窗口，沿两个工作区的 Tab 路径逐个停靠点截取放大裁图，连同 `focus-evidence.json` 写进本次运行实拍证据目录的 `focus\` 子目录（报告字段 `MainFocusEvidenceManifestPath`，失败原因在 `MainFocusEvidenceError`）。截取时最近输入设备临时设为键盘，裁图含焦点装饰层，和真实按 Tab 时一致。它们不进门禁，只供核对单层描边，结论以裁图为准。`focusVisualAdorner` 表示挂着 FocusVisualStyle 装饰层；`templateDrawsFocus` 保留既有字段名以兼容实拍证据消费者，语义为“模板或宿主负责焦点描边”，取自 `KeyboardFocusPolicy.UsesTemplateOrHostStroke`，包含模板焦点环、勾选框、输入与日历控件、日期框内部的宿主绘制按钮以及金额文字；两项同时为真，说明本应由模板或宿主画焦点的控件又挂了装饰层。`singleFocusStroke` 由同一策略检查当前焦点状态、装饰层资格与实际模板描边。直接 WPF 规则夹具 `KeyboardFocusPolicyBehavior` 补充验证输入来源与重复描边反例，不能替代两窗真实 Tab 遍历和裁图核对。

倒计时行的关键状态同样另开一次性主窗口实拍（#247），不进门禁：载入行几何探针 `CountdownRowDeadlineAndSecondaryDeleteBehavior` 的同一份夹具（普通主项、30 字名称、五枚标签、999 天、截止行、百万天、已到期），按 864×1000 截取七行时滚动条在顶部与底部、百万天主项、到期主项四张倒计时页和一张便签页，连同 `countdown-rows.json` 写进实拍证据目录的 `countdown-rows\` 子目录（报告字段 `CountdownRowEvidenceManifestPath`，失败原因在 `CountdownRowEvidenceError`）。几何结论以探针为准，截图供人工核对主阅读轴、辅助带与动作。

便签卡的关键状态也这样实拍（#249），不进门禁：载入卡片几何探针 `NoteRowContentGeometryBehavior` 的同一份夹具（无标签短正文、140 字长正文、五枚标签与 30 字长标签、两条提醒、每 15 分钟提醒的来源卡、已完成），按 864×1000 截取未完成五张时滚动条在顶部与底部、「复盘」搜索、多选两张与批量操作条、提醒来源强调、已完成段六张便签页，连同 `note-rows.json` 写进实拍证据目录的 `note-rows\` 子目录（报告字段 `NoteRowEvidenceManifestPath`，失败原因在 `NoteRowEvidenceError`）。几何结论同样以探针为准，截图供人工核对内容列、标签邻近与右上动作槽。

共享表面与次级窗口的同版实拍（#250）同样不进门禁：在一次性管理窗口、悬浮窗、提醒中心、规则编辑器与删除确认框里按 96 DPI（1 px = 1 DIP）渲染普通、长内容、错误/禁用、键盘焦点与系统高对比度五类状态，并单独渲染筛选、标签管理、设置、标签选择与悬浮便签 `…` 菜单弹层（叠回宿主会被窗口右缘裁掉），连同 `surfaces.json` 写进实拍证据目录的 `surfaces\` 子目录（报告字段 `SurfaceEvidenceManifestPath`，失败原因在 `SurfaceEvidenceError`；单帧失败写 `<帧名>.error.txt`，其余帧照常落盘）。错误与禁用走真实入口（空名称添加、负月薪保存、空正文快速新增、送达失败、自定义稍后缺日期时间、规则间隔为 0 时保存、送达未落盘），高对比度在验证作用域里注入经典「高对比黑」系统色，不读本机设置；节假日日历读临时缓存里的参考年份工作日表，不联网。圆角结论以 `SharedSurfaceRadiiFollowTokens` 为准，截图供人工核对层级、边界与单层焦点。

工作台密度与悬浮窗滚动提示（#251）也这样实拍，不进门禁：密度探针 `MainWorkbenchDensityReclaimed` 在首选 864×1000、最小 720×560 与真实 WM_DPICHANGED 三个窗口里，各拍倒计时首屏、便签顶部、便签滚到底、搜索与多选五张 `<窗口档>-<工作区>[-<状态>].png`（前两档 96 DPI，DPI 档按注入后的窗口 DPI 光栅化），写进 `logs\ui-captures\<时间戳>-density\`；滚动提示探针 `FloatingScrollContextHintFollowsOverflow` 每个状态一张 520×390 的 `<状态>.png`，菜单态另拍弹层 `<状态>-popup.png`，写进 `logs\ui-captures\<时间戳>-scroll-hint\`。几何与提示方向以两项事实为准，截图供人工核对。

候选构建完成后可先运行窗口短验证。`GeometryPreflight` 单独使用同一隔离启动器，复用主窗口工作区、倒计时行与便签卡几何、共享表面圆角（#250）、工作台密度与悬浮窗滚动提示（#251）、展开箭头真实中间帧与键盘/减弱动画即时等价、提醒中心完整时区标记换行（#253）和确认框的正式断言，并报告候选 DLL 哈希、会话、桌面与原生工作区。失败后先修复候选或环境，再启动完整回归；它不替代完整套件。

```powershell
$appPath = Join-Path $PWD 'app'
$dllHash = (Get-FileHash (Join-Path $appPath 'MinminCountdown.dll') -Algorithm SHA256).Hash
pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage GeometryPreflight -AppPath $appPath -ExpectedDllSha256 $dllHash -StageTimeoutSeconds 90
```

构建前先做只读环境预检和工具自检（不在用户桌面开窗）；`-RequireClean` 只在当前 safe-tree-delete policy 登记了该 checkout 的正式清理路径时使用：

```powershell
pwsh.exe -NoLogo -NoProfile -File ./tools/Test-CountdownBuildPreflight.ps1 -Json
pwsh.exe -NoLogo -NoProfile -File ./tools/Test-CountdownTooling.ps1 -Json
```

发布前先将本次拟提交文件加入暂存区，再运行 `tools/Test-CountdownPublicationEvidence.ps1 -Source Index -Json`。Tooling 默认按同一个暂存树核对 Markdown 本地链接和声明为 `git-blob` 的证据哈希，不接受磁盘上的未跟踪或忽略文件替代 Git 目标；CI 在构建前用 `-Source Commit -Commit <SHA>` 检查实际发布提交。版本化 JSON 的 UTF-8/LF 写法、原始运行哈希和外部产物边界见[发布证据检查](docs/agents/publication-evidence.md)。Tooling 失败时，启动器优先显示阶段最终 JSON 中的失败检查，stderr 仅作补充诊断；无有效 JSON 时保留原来的混合编码回退。

Tooling 的发布差异检查同时覆盖基点到候选提交、候选到暂存区、暂存区到工作树。默认使用 `origin/main` 和 `HEAD`；集成树或手动验收须显式提供 `-BaseCommit <发布基点>`、`-CandidateCommit <候选提交>`，或通过 `COUNTDOWN_DIFF_BASE`、`COUNTDOWN_DIFF_CANDIDATE` 传给启动器。基点必须是候选祖先；旧提交的错误不能由工作树修正掩盖。CI 按 push / PR / 手动事件解析并固定实际 SHA，手动运行须填写 `publication_base`。

弹层验证和工具自检也可单独经官方入口运行，入口会写出 `logs\suite-runs\*-run.json` 与 `*-summary.json`；启动器异常、取消、超时和失效 owner 都以非成功终态保存，不能把缺少 summary 当成通过：

```powershell
pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage ValidationHarness,Tooling
```

`ValidationHarness` 的生产动效检查必须在 300 ms 内取得真实的 10%–90% 中间帧，终态或缺帧都不算通过。只有某场景唯一的失败与实测卡顿重合时，才重跑该场景。卡顿指以下任一情况：采样窗口内最长无帧间隔不短于最短 90 ms token 的 10%–90% 区间（约 37 ms）；或反转后的首帧到达时，整段动效时长已经过去。每个场景最多重跑 2 次，每轮最多 4 次。每次因卡顿失败的尝试都记入 `Motion.Results[].StarvedAttempts`；阈值、实际用量、注入卡顿自检和判定对照见 `Motion.StallRetry`。最终判定仍取自一次完整的真实通过。

视觉改动需要新基线时，先用 `tools/Set-CountdownBaselineVersion.ps1 -Version <yyyy-MM-dd-vNN>` 更新现役版本（版本只在 `src/MinminCountdown/VisualReferenceBaseline.cs` 的 `Version` 常量声明一次，主窗与悬浮窗共用，脚本经 `Countdown.Validation.psm1` 的 `Get-CountdownVisualBaseline` 读取；编号跨分支全局分配，工具拒绝不大于已用最大编号的值，并列出仍需人工更新的文档；`-WhatIf` 只预览），按其提示重算 `IntegrityHashes` 并构建当前源码，再单独运行生成阶段。该阶段在同一隔离桌面与泄漏监测下执行 `app/MinminCountdown.exe`，将 9 张主窗口与 5 张悬浮窗实拍绑定到实际 EXE（#235 新增便签多选及批量操作条，#236 新增下班收尾）；已有基线目录拒绝覆盖；失败时把本次新建的版本目录移进 `logs/visual-baseline-generation/<guid>/partial/`，和诊断放在一起，重跑不用先清理。新版双 manifest 生成并验证通过后，生成器移除主窗与悬浮窗共同的上一版；更早既有存量不随生成自动清理，须按单独确认后的清单处理。生成完成后还须另行运行 CSharp 复验，生成过程本身不替代回归。生成后用 `tools/Compare-CountdownVisualBaseline.ps1 -From <上一版>` 比对两版的绑定文件、EXE、逐帧 PNG 与状态指纹；旧版默认从 `HEAD` 的 Git blob 读取，不要求旧目录仍在工作树，回看其他历史时加 `-FromCommit <固定提交>`。把结果与聚合哈希记进 DESIGN.md，并把其中指向现役基线的锚点改到新版本。从改版本号到写完 DESIGN.md，Tooling 的 `visual-baseline:fixture-shape` 与 `visual-baseline:design-anchors` 必然失败，这段时间的工具自检放到这两步之后。

```powershell
pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage BaselineGenerate
```

`TauriGate` 仅用于 #209 的既有 Tauri 原型实验，须在 WPF 改版验收后单独运行。通过同一启动器显式指定 `-TauriExecutable`（以 evidence feature 构建的 `minmin-countdown-slice.exe` 绝对路径）和 `-TauriEvidenceDirectory`（尚不存在的绝对路径），建议 `-StageTimeoutSeconds 240`。该阶段使用不可见桌面、BelowNormal、owned Job、取消/超时和桌面泄漏监测，拒绝关闭隔离；原型仅接触新目录内的 fixture state 与 WebView2 profile。退出后独立核对单个 `520 x 390` 窗口、四场景 PNG 尺寸/哈希和关键交互回执。该结果仅证明候选实验，不构成生产发布或离线 WebView2 部署验收。

兼容旧命令仍可显式传入 `-IsolatedDesktop`（与默认相同）。调试关隔离：

```powershell
pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -AllowUnisolatedDesktop -Stage Core
```

标注：本文档命令的脚本路径统一用 `./` 前缀：该写法在 PowerShell、cmd 与 Git Bash 下均可直接执行；`.\` 前缀会被 Git Bash 等 POSIX shell 当作转义符吃掉反斜杠，导致 `pwsh -File` 报找不到脚本。

标注：五阶段套件与 12 步收口 runner（`tools/Invoke-CountdownVerificationRun.ps1`）不在同一会话连跑：后者第 3/6/8/10/11 步与五阶段逐字相同，连跑等于整套跑两遍；第 9 步单独覆盖 `Countdown.TransferLedger.Tests.ps1`。收口 runner 的 CSharp/SourceValidation 走同一隔离启动器；`desktop-launcher` 与 `instance-activation-matrix` 默认拒绝前台跑，需要时显式 `-AllowForegroundWindowedSteps`（用户正在用电脑时禁止）。

标注：issue 关闭前按 [`docs/agents/issue-acceptance.md`](docs/agents/issue-acceptance.md) 生成结构化记录；`candidate`、`dry-run`、`deployed` 和 `live verified` 分开记录。`tools/Invoke-IssueWriteback.ps1` 不带 `-Apply` 时只生成评论和父票汇总计划，不发送 GitHub 请求。

本地迭代使用 `pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage IncrementalBuild`；它保留增量编译，仍走隔离桌面与 BelowNormal。`Build` 始终表示受管 `-Clean` + R2R。开始正式收尾时，使用 `pwsh.exe -NoLogo -NoProfile -File ./tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage Build,Core,CSharp,SourceValidation,InstallValidate -Closeout`：入口先用现有 `-RequireClean` 预检当前 checkout 的清理能力，失败就不启动长阶段；完成状态由五阶段结果及构建实际输出的 `CleanR2R` 回执共同判定。只有退出0但没有模式回执、增量构建或缺少任何必需阶段，都不能算正式收尾完成。回执另记程序目录的聚合哈希 `PayloadAggregateSha256`，新部署入口据此确认送出的就是套件测过的那份构建输出；这个字段之前跑出的套件记录没有它，部署前要用当前构建脚本重跑套件。

启动器未覆盖的运行（`verification\MinminCountdown.Verification.exe` 直跑、`-DesktopLauncherOnly`、`-InstanceActivationMatrix`、`Deploy-OfficeCountdown.ps1 -ValidateOnly`）遵循同一前提；其中会渲染窗口的变体只在用户明确授权时前台运行（`-AllowForegroundDesktopLauncher`）。下方为启动器转发的**实现细节**（不是复制模板；CSharp/SourceValidation 直跑会 fail-closed）：

```powershell
pwsh.exe -NoLogo -NoProfile -File ./Build-CountdownApplication.ps1 -Clean
./verification/MinminCountdown.Verification.exe
pwsh.exe -NoLogo -NoProfile -STA -File ./Countdown.CSharp.Tests.ps1
pwsh.exe -NoLogo -NoProfile -STA -File ./Countdown.CSharp.Tests.ps1 -DesktopLauncherOnly -AllowForegroundDesktopLauncher  # 2026-08-23 起冷≤4.7s / 暖≤4.5s；用户正在用电脑时禁止
& 'D:\Codex\tools\PowerShell\7.6.4\pwsh.exe' -NoLogo -NoProfile -STA -File ./Countdown.CSharp.Tests.ps1 -InstanceActivationMatrix -AllowForegroundDesktopLauncher  # 诊断并发 probe 共享截止 38900ms（真实矩阵 8×100 强制 1MB 滚动写最差外推 32381ms +20%）；用户正在用电脑时禁止
pwsh.exe -NoLogo -NoProfile -File ./Countdown.Core.Tests.ps1
pwsh.exe -NoLogo -NoProfile -STA -File ./Invoke-CountdownSourceValidation.ps1
pwsh.exe -NoLogo -NoProfile -File ./Install-Countdown.ps1 -ValidateOnly
pwsh.exe -NoLogo -NoProfile -File ./Deploy-OfficeCountdown.ps1 -ValidateOnly
```

正式安装结果默认只返回已安装文件数量和清单 evidence 路径；完整逐文件 SHA-256 清单写入该路径，避免把整份 inventory 回显到终端。
本机替换现有桌面版本继续使用 `Install-Countdown.ps1` 的正式事务入口；它返回 `InstalledFileCount`、`InstalledFilesEvidencePath`、启动器、计划任务和运行态回读字段，不需要临时替换脚本。

标注：InstallValidate 独立阶段与 Core 测试内联的安装器验证调用（`Countdown.Core.Tests.ps1` 与 `Install-Countdown.ps1 -ValidateOnly`）是有意双轨：前者是阶段级红即停门禁，后者带合同断言；重复成本约 10 秒，不合并。

标注：`Countdown.Core.Tests.ps1`（含 GitHub Actions「Test release contract」的 `-ReleaseContractOnly`）与本机 `Install-Countdown.ps1` 正式安装（不带 `-DeferCommit`）调用 `Invoke-CountdownValidationSuite`；办公室延迟提交路径不调用。Core 逻辑不隔离；C# 回归与应用 `--validate-only` 分别进 `MinminSuiteIso-ValidateCSharp` / `MinminSuiteIso-ValidateApp`，后者直接启动 `app\MinminCountdown.exe` 并显式传入临时设置与诊断日志路径，不经 adapter。`windows-latest` 与本机 Default 桌面一样会触发内层 fail-closed，不要在 Default 上关隔离直跑窗口步骤。

标注：应用 `--validate-only` 失败时，验证套件先把它的诊断日志（`*.application.jsonl` 及轮转文件）复制进该阶段的证据目录，再照常清理临时文件。CI 任一步失败时上传 `crash-evidence-<run_id>-<attempt>`，内容是 Application 日志里的崩溃事件（Application Error、Windows Error Reporting、.NET Runtime）、`MinminCountdown.exe` 的 WER 小型转储，以及 `D:\agents\run-evidence\clock-countdown-validation` 下的阶段证据。仓库公开，artifact 任何人都能下载，所以只收小型转储。

`-DesktopLauncherOnly` 的冷/暖门槛按 2026-08-23 现役探针五对 runId `ca1179d53398413abb236fc0f4d7b490`、`58c35833a6bf4b5ebbab9e3dab973c2a`、`eb12765e030d413a8520bcbf78c4938c`、`9d6ff40ee86745aab70f4dd96522ce60`、`9cf91f34b4f242b5a93c3b04a2fe1f23` 校准。口径是真实首帧 WPF `ContentRendered`（命名事件 `Local\CountdownDesktopLauncherWindowReady`）；测量钟在首次 window-ready 之后才查询 `Win32_Process`。探针 stopwatch T0→T4 冷 p95/max=3776ms、暖 p95/max=3439ms，分段 Σp95+20% 取整为冷 4700ms、暖 4500ms。机器：BAKER / i5-14600KF / 可用内存约 7.3GiB / CPU 中低 / 2026-08-23 16:46–16:49 +08。样本只用于归因，不汇入门槛样本池。`-InstanceActivationMatrix` 的 38900ms 共享截止按真实矩阵 8×100 强制滚动写标定：2026-08-24 五次 Wait 完成 [1035, 1037, 1101, 1160, 1248]；2026-08-23 超时证据 run `20260823T1510260920479Z` 最慢写手 21/100 @ 6800ms，线性外推 32381ms，取最差 +20% 取整。样本只用于归因，不汇入门槛样本池。2026-08-23 连续三轮完整本机验证组合中诊断并发 probe 曾在 6800ms 内只完成约 43–86/100；不能把隔离绿灯当成矩阵或连续全套件绿灯。
以上命令只证明当前机器上的源码行为、发布包、安装与部署预检合同。`Deploy-OfficeCountdown.ps1 -ValidateOnly` 校验当前工作树候选的发布清单、`IntegrityHashes` 静态哈希和临时 ZIP，不要求工作树干净，也不执行 Git fetch；它不连接办公室设备，不上传或安装产物，更不构成办公室远端部署或验收。无开关入口优先识别当前提交的完整历史部署记录并只读复核；进入安装路径时仍只接受干净、已推送且与 `origin/main` 一致的 `main` 提交，并要求本 checkout 有同一提交在干净工作树上跑通的五阶段套件记录：办公室端只安装、不跑任何测试，这份本机记录就是部署的验收门禁。历史查询、实时复核、显式重新安装和完成态合同见 [办公室部署](docs/办公室部署.md)。安装路径在首次传输前会解析 Windows 内置 OpenSSH 或 PATH 中成对的 ssh/scp 绝对路径，并用选定 ssh 做约 32KB 只读 echo 行为探针；截断或找不到候选即闭锁。`-ValidateOnly` 不跑该解析和探针。办公室发布包只含办公室安装会加载或安装的文件：C# 源码、测试、构建与验证工具和 `DESIGN.md` 留在仓库，正式部署在构建前按提交 blob 身份逐项核对构建输入（契约的 `BuildInputFiles`）。

## 运行应用

```powershell
./CountdownLauncher.exe
```

启动链：桌面 `敏敏子倒计时.exe`（安装器复制的 `CountdownLauncher.exe`）→ `app\MinminCountdown.exe`。启动器优先用自身目录下的 `app\MinminCountdown.exe`，没有时回退到 `D:\敏敏子倒计时\app\MinminCountdown.exe`；它不带参数启动宿主，宿主不读启动请求。登录任务执行 `pwsh -STA -File 敏敏子倒计时.ps1`，adapter 以 `--require-startup-request` 启动宿主；宿主从应用根目录（`app\` 的上一级）读取安装器写出的 `startup-request.json`，缺失或损坏即拒绝启动（fail-closed）。#275 起，宿主不再按 adapter 文件推断应用根目录：宿主位于 `app\` 时应用根目录总是它的上一级，不随工作目录变化；其他布局（如经 `dotnet` 运行源码构建输出）以调用方给的工作目录为应用根目录。随包节假日数据和图标从程序目录（安装布局里即 `app\`）读取，设置、诊断日志、节假日缓存与运行状态回执在应用根目录。不带参数直接启动 `app\MinminCountdown.exe` 可编辑，悬浮窗首帧后照常写出运行状态回执。构建不写启动请求，所以在仓库 checkout 里直接运行 adapter 会被拒绝。

## 安装

安装前先运行 `Build-CountdownApplication.ps1 -Clean`。改了发布契约 `IntegrityHashes` 覆盖的文件（办公室安装会加载的 PowerShell 模块与 adapter、启动器、图标、节假日数据、根目录官方立绘和 14 个程序目录随包资源）后，按当前提交 blob 更新 `Countdown.ReleaseContract.psm1` 里对应的 SHA-256：先 `git add` 改过的文件，再运行 `tools/Update-CountdownIntegrityHashes.ps1`（读暂存区 blob 改写钉值；`-Check` 只列出全部不一致项、不改文件）。C# 源码、测试、构建与验证工具和 `DESIGN.md` 不进静态钉值，改它们不用重算钉值。安装器要求已安装 PowerShell 7+（`pwsh.exe`），缺失时会拒绝运行，不会回退到 Windows PowerShell 5.1。它会在全部预检通过后才取得全机安装事务锁：先记录计划任务和 Windows 时间服务状态，再冻结两份旧登录任务以阻止旧程序复活；旧程序静止后才快照安装目录与桌面启动器（含 `.new` 暂存文件），随后校准网络时间、运行测试、复制程序、把启动器安装到桌面，并另行注册登录任务。桌面双击 `敏敏子倒计时.exe` 直接启动宿主，不经过 schtasks、pwsh 或 adapter；登录任务仍执行 `pwsh -STA -File 敏敏子倒计时.ps1`。登录任务必须用本次启动令牌先写出“窗口首帧已渲染”的 `window-ready` 回执，安装器最多等待 30 秒；设置等价和提交前验收通过、事务进入仍可回滚的 `accepted` 后，应用还必须以同一令牌、进程、会话和应用路径写出 `finalized-ready`，安装器复核计划任务、受管 EXE 身份与运行环境后才把事务标记为 `committed`。正常失败会恢复快照（时间校准本身不可逆）。损坏 journal、缺少备份或用户在崩溃后替换桌面启动器时，安装器会保留证据并拒绝覆盖用户文件，而不是进行破坏性回滚。请在管理员 PowerShell 7 中从仓库根目录运行：

```powershell
pwsh.exe -NoLogo -NoProfile -File ./Install-Countdown.ps1
```

安装器只支持安装目录和交互用户桌面均位于 `D:\` 的设备，这是当前办公室设备的安全边界。

办公室设备的稳定部署方式见 [办公室部署](docs/办公室部署.md)。

## 重新编译启动器

```powershell
pwsh.exe -NoLogo -NoProfile -File ./Build-Launcher.ps1
```

重新生成 `CountdownLauncher.exe` 后，需要同步更新 `Countdown.ReleaseContract.psm1` 中对应的 SHA-256。

重新生成图标需要 Python 和 Pillow：

```powershell
python -m pip install Pillow
python .\make_countdown_icon.py
```
