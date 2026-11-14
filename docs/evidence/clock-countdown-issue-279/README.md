# #279 真实计划任务直启探针

2026-11-14 10:41–10:48（北京时间）在本机用一个临时名称的真实计划任务和一个临时安装根，验证“登录任务直接启动 EXE”的几条假设。用户当场同意（编排会话转达的原话「可以，现在跑」）之后才注册和运行任务。探针没有碰现有登录任务、`D:\敏敏子倒计时` 和用户的应用实例：开始时本机没有任何 `MinminCountdown.exe` 进程，也没有 `kryptex_*` 进程。

原始数据见 [probe-measurements.json](probe-measurements.json)，按阶段保存每次测量的原始输出。注册时导出的任务定义见 [probe-task-registered.xml](probe-task-registered.xml)，已改存为 UTF-8。两份文件里的临时根路径、worktree 路径和账号 SID 都换成了占位符，不含账号名和主机名。

## 探针设置

- 程序：门禁通过的干净构建 `29860c2`（gate run `20261114T100443604-81df41a0`）的 `app\`，复制到 `%TEMP%\minmin-probe-279-e39103a6\app\`。载荷清单 429 项逐项哈希一致，EXE SHA-256 为 `FE8A141A…6EF5`。临时根里没有设置文件、启动请求和 adapter。
- 任务：`\MinminCountdown-Probe-279-e39103a6`，在非提升的 PowerShell 7.6.9 里注册成功。动作只有 EXE 本身，不带参数，也不设工作目录；交互式（`InteractiveToken`），非提升（`Limited`），优先级 5，运行时限 `PT0S`，多实例策略 `IgnoreNew`，电池供电可启动、拔电不停。按要求**不带任何触发器**，只按需启动，所以状态命令对它一直报 `trigger` 不符。这是预期的不符项。
- 环境：Windows 11 build 26300，唯一的交互会话是控制台会话 1。任务计划的 Operational 日志是关闭的，本探针没有打开它。六次启动如下：

| 次 | 启动方式 | 任务优先级 | 结束方式 |
|---|---|---|---|
| 1 | `Start-ScheduledTask` | 5 | 运行中再触发一次，然后 `Stop-ScheduledTask` |
| 2 | `Start-ScheduledTask` | 5 | `Process.CloseMainWindow()`，然后 `Stop-Process -Id` |
| 3 | 在任务之外启动（`Win32_Process.Create`，相当于从桌面入口启动） | 5 | 触发任务；任务为 `Ready` 时 `Stop-ScheduledTask`；`Stop-Process -Id` |
| 4 | `Start-ScheduledTask` | 7（任务计划默认值） | `Stop-ScheduledTask` |
| 5 | `Start-ScheduledTask` | 5 | `taskkill /PID`（不带 `/F`），然后 `Stop-Process -Id` |
| 6 | `Start-ScheduledTask` | 4 | `Stop-ScheduledTask` |

## 票面五项实测

### 1. 直启进程的优先级类

| 来源 | 优先级类 | 基础优先级 | I/O 优先级 | 内存优先级 |
|---|---|---|---|---|
| 任务优先级 7（第 4 次） | BelowNormal | 6 | Low | 2 |
| 任务优先级 5（第 1、2、5 次） | Normal | 8 | Normal | 4 |
| 任务优先级 4（第 6 次） | Normal | 8 | Normal | 5 |
| 在任务之外启动（第 3 次，对照） | Normal | 8 | Normal | 5 |

五次由任务启动的进程都在控制台会话 1，和资源管理器是同一个会话。它们的令牌非提升（`Limited`，中完整性级别），父进程是任务计划服务的 `svchost.exe`，并且都在任务计划的作业对象里。在任务之外启动的进程不在作业里。从调用 `Start-ScheduledTask` 到进程创建用了 10–55 ms，进程创建到 `WindowReady` 用了 834–931 ms。按 PRD 的口径（诊断日志 `ProcessStarted` 到 `WindowReady`）是 602–679 ms。这是本机的少量样本，不是办公室基线。

### 2. 应用运行期间任务的状态

应用由任务启动并在运行时，任务状态是 `Running`，上次结果 `0x00041301`（任务正在运行）。运行实例恰好一个，它的 `EnginePID` 就是应用 PID。应用如果是在任务之外启动的（第 3 次），任务一直是 `Ready`。

### 3. 停止任务是否结束应用

会结束。三次 `Stop-ScheduledTask`（第 1、4、6 次）之后，应用都在约 160–180 ms 内退出，任务回到 `Ready`，上次结果 `0x00041306`（被用户终止）。不过应用不是被静默结束的：每次诊断日志都先记一条 `ExitConfirmationOffered`，30–65 ms 后再记一条 `FatalUnhandledException`，异常是 `InvalidOperationException`“无法将 Owner 属性设置为已关闭的 Window。”，进程退出码为 1。退出确认对话框在构造时就抛了异常，没有显示出来。

`Stop-ScheduledTask` 结束不了在任务之外启动的实例。第 3 次在任务处于 `Ready` 时停止任务，3 秒后该实例仍在运行。

### 4. 应用已在运行时再次触发任务

- 应用由这个任务启动（第 1 次）：`Start-ScheduledTask` 不报错，6 秒内没有出现新进程，原实例照常运行。因为没有第二个进程，也就没有单实例激活，悬浮窗不会被激活。任务仍是 `Running`，但上次运行时间被改成这次被拒的触发时刻，上次结果变成 `0x800710E0`（`ERROR_REQUEST_REFUSED`）。
- 应用在任务之外启动（第 3 次）：任务会启动第二个进程。这个进程在 `ProcessStarted` 之后 88 ms 完成单实例激活：运行中的实例记 `InstanceActivationHandled`，结果 `Activated`；第二个进程记的 `ForegroundPermissionGranted` 是 false。随后第二个进程以 0 退出。任务在 764 ms 内回到 `Ready`，上次结果 `0x00000000`。50 ms 一次的进程枚举没赶上这个短命进程，它的 PID 和时间取自诊断日志。

### 5. 应用退出后任务的上次结果

| 退出方式 | 应用退出码 | 任务状态与上次结果 |
|---|---|---|
| `Stop-ScheduledTask`（第 1、4、6 次） | 1，走第 3 项的崩溃路径 | `Ready`，`0x00041306` |
| `Stop-Process -Id`（第 2、5 次） | `0xFFFFFFFF` | `Ready`，`0xFFFFFFFF` |
| 第二个进程激活已有实例后自行退出（第 3 次） | 0 | `Ready`，`0x00000000` |
| `Process.CloseMainWindow()`（第 2 次） | 不退出：返回 false，`MainWindowHandle` 为 0，4 秒内没有日志 | 保持 `Running` |
| `taskkill /PID`，不带 `/F`（第 5 次） | 不退出：taskkill 报成功，8 秒内应用没有反应，也没有日志 | 保持 `Running` |

任务的上次结果就是进程退出码；被任务计划停止时记为 `0x00041306`。悬浮窗设了 `ShowInTaskbar="False"`，WPF 为此给它挂了一个不可见的“Hidden Window”作所有者窗口，第 5 次枚举顶层窗口时看到了这个结构。所以 .NET 认为这个进程没有主窗口，外部也没有能让应用体面退出的办法。用户在退出确认里点“退出”这条路径需要有人点击，本次没有执行；按代码它以 `Shutdown(0)` 结束。

## 状态命令读回与实测对照

状态命令是仓库根的 [Get-CountdownInstallStatus.ps1](../../../Get-CountdownInstallStatus.ps1)，不在 `tools\` 下。每次都以 `pwsh -File Get-CountdownInstallStatus.ps1 -InstallRoot <临时根> -TaskName <探针任务>` 运行，然后与直接测得的值逐项比对：

| 字段 | 直接测量方式 | 结果 |
|---|---|---|
| `processes[].path` | `QueryFullProcessImageName`（状态命令用的是 CIM） | 4 次应用在运行时的读回全部一致 |
| `processes[].sessionId` | `ProcessIdToSessionId` | 一致，都是 1 |
| `processes[].processId`、`startTimeUtc` | 启动回执里的 `processId`（由任务启动时也等于运行实例的 `EnginePID`；第 3 次是 `Win32_Process.Create` 返回的 PID）、`Process.StartTime` | 一致，起始时间相差不到 0.001 ms |
| `exe.sha256`、`exe.matchesManifest` | 分别对已安装 EXE 和运行中进程的映像文件算 SHA-256，再对照载荷清单 | 一致，都是 `FE8A141A…6EF5`，与清单相符 |
| `commit`、`aggregateSha256` | 载荷清单 | 一致，`29860c2…`、`13464B67…4750` |
| `running`、`loginTask.state` | 进程枚举、`Get-ScheduledTask` | 一致；第 3 次是 `running=true` 而任务为 `Ready` |
| `settings.present`、`transactionMarkerPresent` | `Test-Path` | 一致，都不存在 |

逐项比对共做了 5 次：第 1 次运行中、第 1 次停止后、第 3 次（任务之外启动的实例在运行）、第 4 次、第 6 次，共 90 个字段，全部一致。第 1 次启动前另读回过一次，结果为未运行、任务 `Ready`。`loginTask.mismatches` 在优先级 5 和 4 时只有 `trigger`，在优先级 7 时是 `trigger,priority`，说明状态命令的优先级检查在真实任务上生效。

## 登录任务的最终定义

下表给 #281 作期望定义用。探针实际注册的是去掉触发器、优先级为 5 的版本。

| 项 | 值 | 依据 |
|---|---|---|
| 任务名与位置 | `\Minmin Countdown` | 沿用现行任务名，也是状态命令的默认值 |
| 触发器 | 一个登录触发器，限定为交互用户，启用 | PRD；本探针没有注册触发器，这一项未实测 |
| 主体 | 交互用户，`InteractiveToken`，`LeastPrivilege`（`Limited`） | 第 1–6 次：会话 1，非提升 |
| 动作 | 一个 Exec：`<安装根>\app\MinminCountdown.exe`，不带参数，不设工作目录 | 不设工作目录时应用仍以 `app\` 的上一级为应用根，日志、回执和节假日缓存都写在临时根里 |
| 优先级 | **4** | 4 和 5 都得到 Normal 优先级类，但只有 4 的内存优先级与正常启动一样是 5（任务优先级 5 时是 4） |
| 运行时限 | `PT0S` | |
| 多实例 | `IgnoreNew` | 第 1 次：不会起第二个实例 |
| 电池 | `DisallowStartIfOnBatteries=false`，`StopIfGoingOnBatteries=false` | 现行注册就是这样设的；`New-ScheduledTaskSettingsSet` 的默认值相反 |
| 其余 | `AllowDemandStart=true`，`AllowHardTerminate=true`，`Enabled=true`（均为默认值） | 安装器要按需启动 |

```powershell
$action = New-ScheduledTaskAction -Execute (Join-Path $InstallRoot 'app\MinminCountdown.exe')
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $InteractiveUser
$principal = New-ScheduledTaskPrincipal -UserId $InteractiveUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -Priority 4 -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'Minmin Countdown' -Action $action -Trigger $trigger -Principal $principal -Settings $settings
```

## 与 PRD 假设不符或 PRD 没写到的地方，及对后续票的影响

1. **“普通优先级”应取 4。** PRD 只写了普通优先级，#276 的状态命令接受 4–6，测试夹具用的是 5。实测优先级 5 的优先级类和 I/O 优先级都是 Normal，但内存优先级是 4，比正常启动的 5 低；优先级 4 的三项都与正常启动一致。影响：#281 的期望定义写 `Priority 4`；#288 验收时除了确认优先级类是 Normal，可以顺带确认内存优先级为 5。状态命令的 4–6 范围不需要改。
2. **经任务停止应用会走应用的崩溃路径。** PRD 安装步骤 3 的“结束任务实例”默认是一次普通的停止。实测应用确实在约 170 ms 内退出，但退出码是 1，并且在被停止版本的诊断日志里留下一条 `FatalUnhandledException`。原因是悬浮窗在应用发起退出确认之前已经被关闭，`ConfirmationDialog` 设置 `Owner` 时抛出 `InvalidOperationException`，`OnDispatcherUnhandledException` 随后以 `Shutdown(1)` 结束进程。直接 `Stop-Process`（TerminateProcess）则立即结束进程，不写日志，下一次启动照常取得单实例租约（第 2→3 次、第 5→6 次）。影响：#280 的“停止应用”和 #283 的“停止新版本”如果用 `Stop-ScheduledTask`，每次安装或回滚都会在日志里留一条致命异常；读日志的人或任何扫描 Fatal 事件的检查都会把它当成崩溃。建议安装器直接结束映像路径为已安装 EXE 的进程（PRD 的第二步本来就有这一步），或者另开一票修复应用对外部关闭的处理。#288 验收“真实任务可以停止应用”时，会看到同样的退出码 1 和这条日志。任务计划停止任务时具体向应用发了什么消息，本探针没有确定；`CloseMainWindow` 和 `taskkill` 都关不掉它（见第 5 项）。
3. **再次触发会改写上次结果。** PRD 只写了“已在运行时忽略新实例”。实测确实不起新实例，但上次运行时间被改写，上次结果变成 `0x800710E0`；原实例照常运行，悬浮窗也不会被激活。`Start-ScheduledTask` 对这种被拒的触发不报错。影响：#280 经登录任务启动之前，必须先确认任务没有在运行的实例，否则这次触发会被悄悄拒绝，等不到新回执。#284 的中断恢复遇到“新版本已在运行”时，不能靠重新触发任务来补做验收，要先从状态里读进程。#285、#286 和办公室部署说明不能再用“任务就绪且上次结果为 0”，也不能用任何上次结果判断成功：直启之后，应用运行期间任务本来就是 `Running`、`0x00041301`。#276 的状态命令不读上次结果，符合这一点。
4. **任务状态不能代表应用是否在运行。** 应用如果是从桌面入口（任务之外）启动的，它在运行，任务却一直是 `Ready`；再触发任务也只会把激活转交给它。`Stop-ScheduledTask` 也停不掉这种实例。影响：#280 和 #283 停止应用时，必须保留“按映像路径结束进程”这一步，实测证明它是必要的。#285 的成功判定只看进程、EXE 哈希和提交，不能要求任务为 `Running`。
5. **运行状态回执的 `applicationPath` 仍指向 adapter。** 直启时回执里的 `processId` 和 `sessionId` 都对，但 `applicationPath` 是 `<安装根>\敏敏子倒计时.ps1`，这个文件在新布局里不存在；`launchToken` 每次随机生成。影响：#280 的启动就绪判定按 PRD 的做法，先删旧回执，再用 `processId` 查映像路径、会话和 EXE 哈希，不能比较 `applicationPath`。#291 删除 adapter 时，要改掉或删掉这个字段。
6. **PRD 的任务定义没有写电池设置。** `New-ScheduledTaskSettingsSet` 默认电池供电时不启动、拔掉电源就停止任务；现行注册和本探针都显式关掉了这两项。影响：#281 的期望定义和注册代码都要带上这两项，否则笔记本在电池供电下登录时应用不会启动。可以考虑让状态命令也检查这两项，作为 #276 的可选后续。
7. **未验证：非提升时能否注册登录触发器。** 本探针按要求不带触发器，只证明在非提升的 PowerShell 里可以注册并按需启动交互式、非提升的任务；而现行安装器要求在管理员 PowerShell 里运行。影响：#288 用新安装器注册带登录触发器的任务时，要确认是否需要提升；#287、#289 的办公室切换按这个结论决定以什么身份运行。

已实测确认、与 PRD 一致的假设：默认优先级 7 对应 BelowNormal（同时 I/O 为 Low、内存优先级为 2），所以必须显式设置优先级；直启的进程在交互会话里，非提升；任务可以停止应用；正常启动不需要任何安装器文件，临时根里没有启动请求、adapter 和设置文件，六次启动都正常达到启动就绪；状态命令的读回与实际一致。

## 现有登录任务未被改动，临时任务与临时安装根已清理

- 探针前后各做了一次只读的 `Export-ScheduledTask`。`Minmin Countdown` 和遗留的 `敏敏子倒计时` 两次都是 `ObjectNotFound`，所有可见的任务文件夹里也没有同类名称的任务。本机本来就没有安装应用，`D:\敏敏子倒计时` 不存在。根文件夹 `\` 前后都是 19 个任务、全部可以导出，任务名加 XML 哈希的聚合值前后相同（`E1371342…B71F`），没有任务被增删或改动。
- 探针任务于 2026-11-14T02:47:54Z 注销，之后 `Get-ScheduledTask` 按名称和全路径都找不到它。
- 临时安装根经受管回收移入回收站：run `20261114T024821Z-2f725ea8`，状态 `recycled`，共 438 个文件、165,833,094 字节，原位已不存在。
- 探针只结束过映像路径在临时根内的进程，每次结束之前都按 PID 重新读取映像路径；没有碰过任何其他进程。

## 未覆盖

- 登录触发器在真实登录时的启动，以及非提升时注册登录触发器。
- 已有设置文件时的启动（迁移写回）和停止。本次临时根里始终没有设置文件。
- 任务计划停止任务时发给应用的具体消息；用户在退出确认里点“退出”的路径。
- 办公室设备。
