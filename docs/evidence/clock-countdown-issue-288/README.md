# #288 新安装器本机真实安装验收

2026-11-15 14:38–14:44（北京时间）在本机用一个临时名称的真实计划任务和一个临时安装根，实跑新安装器 `Install-CountdownProgram.ps1`。用户当场同意（编排会话转达的原话「可以，安排上」，约 14:00）之后才注册任务，真实验收 14:40:33 开始，在同意后两小时内。悬浮窗一共出现 5 次，资源管理器窗口开过 1 个；从临时构建到回收共约 13 分钟（14:31–14:44）。

没有碰现有登录任务、桌面上的 `敏敏子倒计时.lnk`（它指向主 checkout 的启动器）、主 checkout 和 `D:\敏敏子倒计时`。开始时本机没有任何 `MinminCountdown.exe` 进程，也没有 `kryptex_*` 进程。

原始数据见 [real-install-measurements.json](real-install-measurements.json)，按探针模式（prepare、dryrun、run、cleanup）和受管回收摘要（recycle）分节。每一步安装都保存了安装器的结果 JSON、50 ms 一次的采样时间线（安装根目录项、三份设置的修改时间、运行目录里的应用进程）、进程与任务事实（含导出的任务 XML）、设置指纹、状态命令读回和诊断日志事件。R1 由安装器创建的任务定义见 [installer-task-registered.xml](installer-task-registered.xml)，已改存为 UTF-8。路径换成了占位符：`<runDir>` 是临时运行目录 `%TEMP%\minmin-288-a2b5eae6`，另有 `<worktree>`、`<desktop>`、`<probeOut>`、`<pwsh>`；任务 XML 里的账号换成 `{InteractiveUser}`（XML 文件里主体是 `{InteractiveUserSid}`）。两份文件都不含账号名、SID 和主机名。

## 探针与设置

探针是 [tools/Invoke-CountdownRealInstallCheck.ps1](../../../tools/Invoke-CountdownRealInstallCheck.ps1)，有四种模式。`Prepare` 和 `DryRun` 不注册任务、不开窗口。`Run` 必须带用户同意的原话（`-Consent`）。`Cleanup` 注销临时任务，并关掉位置在运行目录里的资源管理器窗口。探针只结束映像路径在运行目录里的进程，结束前按 PID 重读映像路径；只改动名为 `MinminCountdown-Check-288-*` 的任务。桌面上的同名快捷方式不存在、不是 Shell Link 或指向运行目录时，`Run` 直接拒绝，因为那时安装器会在真实桌面上新建或改写它。

- 包：三个包都是 429 个文件，`MinminCountdown.exe` 三个版本逐字节相同（`FE8A141A…6EF5`，见不符项 1）。
  - v1：提交 `29860c2`，复制自门禁构建 gate-259-01，也就是 #279 用过的同一份构建。聚合哈希 `13464B67…4750`。
  - v2：提交 `aae31cf`（本分支起点），复制自门禁构建 gate-259-08。聚合哈希 `DC6A806C…52E5`。
  - v3：故障注入版，做法见验收项 2。在 v2 的程序目录上换掉唯一变了的 `MinminCountdown.dll`，按构建脚本同一算法重写载荷清单。提交记为 `aae31cf…-fault-injected-288`，聚合哈希 `714300DF…D619`。
- 安装根 `<runDir>\root`：安装前先放好 `settings.json` 与两代备份。三份内容互不相同，都是当前 schema，由 v2 的无界面迁移命令生成（`6855BCFB…`、`2D3B2E38…`、`7D8A6D74…`），作为全程逐字节比对的种子。
- 环境：Windows 11 build 26300，PowerShell 7.6.9，STA。shell 非提升：管理员账号的 UAC 筛选令牌，`Limited`，中完整性级别。唯一的交互会话是控制台会话 1。
- 调用方式：安装器经入口脚本在子 `pwsh` 里运行，与办公室走同一个入口；只换了 `-InstallRoot` 和 `-TaskName`，用生产 port（`New-CountdownInstallerWindowsPorts`），没有注入任何测试 port。
- DryRun（14:39:17–14:39:38）：覆盖不需要真实任务的每一步。
  - 现场指纹。
  - 三个包的 `-CheckOnly` 都通过：计划 10 步，含 `create-login-task`。用时 v1 9.4 s（首个运行）、v2 3.0 s、v3 2.9 s。
  - 三个候选 EXE 在设置副本上跑迁移与等价判定，结论都是 `current-schema` / `unchanged`。
  - 用生产 port 在运行目录里写、读、删一个真实 Shell Link（#283 的 `RemoveShortcut`）。
  - 进程事实读取自检和资源管理器窗口列表。
  - 跑完后设置仍与种子逐字节相同。

## 实跑步骤

| 步 | 做了什么 | 结果 | 安装用时 |
|---|---|---|---|
| R0 | 现场指纹与护栏：临时任务不存在，运行目录里没有应用进程，桌面快捷方式是 Shell Link 且不指向运行目录 | 通过 | — |
| R1 | 首次安装 v1 | 成功：创建登录任务；快捷方式 `kept-foreign` / `other-target` | 13.97 s |
| R2 | 临时任务优先级先改成 5，再升级到 v2 | 成功：任务优先级 5→4；v1 被结束；v2 经任务启动并就绪 | 17.03 s |
| R3 | `Stop-ScheduledTask` 停止 v2 | 126 ms 内退出，退出码 0 | — |
| R4 | 任务优先级改回 5，安装 v3（启动失败） | 失败于 `start-and-wait-ready` / `startup-not-ready`，自动回滚 `rolled-back` | 47.85 s |
| R5 | 资源管理器打开 `root\app` 后重装 v2 | 成功；资源管理器窗口仍开着 | 16.65 s |
| R6 | 经任务停止 v2，造出“两次改名之间中断”的状态（`app` 改名为 `app.previous`，写下候选为 v2 的事务标记），再安装 v2 | 中断恢复 `renamed-back` / `recovered`，同一次调用随后安装成功 | 14.00 s |
| R7 | `Stop-ScheduledTask` 停止 v2 | 98 ms 内退出，退出码 0 | — |

下文的时刻都从该次安装开始算起，取自 50 ms 采样，目录变化之间的时刻精确到一个采样间隔。

## 验收项

### 1. 成功升级，启动就绪经真实任务（R2）——满足

- 结果 JSON：`succeeded=true`，`rollback.outcome=not-needed`，`transactionMarkerPresent=false`。`loginTask.action=updated`，`changes` 只有优先级 5→4。`desktopShortcut` 为 `kept-foreign` / `other-target`。设置迁移演练 `equivalent`（`current-schema` / `unchanged`）。
- 时间线：
  - 2.7 s 出现 `app.candidate`。旧版本 v1（PID 73776）一直运行到 12.0 s，被安装器结束，退出码 `0xFFFFFFFF`。
  - 13.2–13.8 s 跑演练的两条无界面命令，各是一个从 `app.candidate` 起的短命进程，退出码都是 0。
  - 14.66 s 换上新程序目录。15.62 s 任务创建 v2 进程（PID 77612），16.48 s `WindowReady`。
  - 16.94 s 清除事务标记，17.00 s 删除 `app.previous` 与事务目录。
  - 应用中断 4.46 s，从 v1 被结束算到 v2 `WindowReady`。
- 启动就绪的进程确实由真实任务启动：任务 `Running`，运行实例的 `EnginePID` 就是 77612。进程映像是 `<runDir>\root\app\MinminCountdown.exe`，在会话 1，父进程是任务计划的 `svchost.exe`，在任务计划的作业对象里，令牌 `Limited`。**优先级类 Normal**，I/O 优先级 Normal，内存优先级 5。R1 的 v1（PID 73776）各项相同。
- 设置与两代备份都与种子逐字节相同，修改时间也没变：v2 不改写当前 schema 的设置。
- 状态命令读回：提交 `aae31cf`，`exe.matchesManifest=true`，`running=true`（PID 77612），`loginTask.matchesExpected=true`，`mismatches` 为空，没有事务标记。

### 2. 人为制造的启动失败，自动回滚（R4）——满足

**怎么制造。** 在临时目录里用 `git archive aae31cf` 取出源码，在 `App.xaml.cs` 的启动迁移之后加下面几行，按 `Build-CountdownApplication.ps1` 的发布参数构建（Release、win-x64、自包含、ReadyToRun、非单文件，用时 29 s）。与 v2 相比只有 `MinminCountdown.dll` 变了。这份构建从未提交。

```csharp
desktopWorkspace.Apply(new CreateTagChange("288 fault injection", null));
if (settingsPath.Length > 0)
{
    throw new InvalidOperationException("Induced startup failure for the #288 real-install check.");
}
```

**为什么这样制造。**

- 失败确定发生，不和安装器抢时序。失败发生在真实任务启动的真实应用里，安装器一行没改。
- 无界面命令没改，演练照常通过，和一个真坏掉的版本一样，失败只在启动就绪这一步出现。
- 失败的版本在死之前真的写了设置：加一个标签并保存，设置存储轮转两代备份，三份文件都变了。所以“回滚后设置与两代备份逐字节相同”是一次真实的恢复，不是什么都没动。
- v2 不改写当前 schema 的设置，回滚后恢复出来的文件会保持原样，可以逐字节比对。
- 没有选的做法有三种：从外面结束新进程，会和就绪检查抢时序，也不会改设置；篡改包里的文件，会在前置检查的载荷核对就被拒，到不了启动这一步；放一份新版本读不了的设置，会被演练拒绝，不换程序。

**实测过程**（从 06:41:26.69 开始算）：

1. 12.2 s 写下事务标记。12.3–12.8 s v3 的演练命令通过。12.9 s 换上 v3。安装器先导出任务定义，再把优先级 5→4 整份重新注册。
2. 14.67 s 任务启动 v3（PID 86176），14.89 s 记 `ProcessStarted`。
3. 15.24 s v3 改写了 `settings.json`：原 `settings.json` 成为 backup 1（修改时间仍是种子的），原 backup 1 成为 backup 2。
4. 15.25 s 记 `FatalUnhandledException`（`InvalidOperationException`，即上面那条消息）。15.28 s v3 以退出码 1 退出，没有显示过窗口。
5. 安装器一直等到 30 秒就绪时限（见不符项 2）。结论是 `failedStage=start-and-wait-ready`，`reason=startup-not-ready`，`error` 为“30 秒内没有达到启动就绪：运行状态回执尚未出现。”
6. 回滚：45.55 s 的采样里 v3 已改回 `app.candidate`，`app.previous` 已改回 `app`。45.61 s 三份设置恢复成种子的字节和修改时间。任务按导出的定义恢复。
7. 46.42 s 任务启动 v2（PID 65616），47.31 s `WindowReady`。47.81 s 清除事务标记，47.89 s 删除候选与事务目录。

**回滚后的状态。**

- 结果 JSON：`rollback.outcome=rolled-back`，`settings=restored`，`loginTask=restored`，`desktopShortcut=null`（升级不动快捷方式），`problems` 为空，`restartedProcess` 为 65616，`transactionMarkerPresent=false`，`diagnosticLogPath` 为 `<runDir>\root\logs\application.jsonl`。
- 设置及两代备份与种子逐字节相同，修改时间也相同。
- 任务定义恢复后再导出，与安装前的导出逐字节相同（`0AB69412…`，优先级 5）。
- 旧版本经真实任务重新运行：任务 `Running`，`EnginePID` 为 65616。进程父进程是 `svchost.exe`，在作业对象里，令牌 `Limited`，优先级类 Normal。恢复的任务优先级是 5，所以内存优先级是 4（见不符项 5）。
- `app` 的载荷清单是 v2（`DC6A806C…`）。状态命令读回：提交 `aae31cf`，EXE 与清单一致，在运行，没有事务标记。

### 3. 真实任务停止应用（R3、R6 之前、R7；#295 复核）——满足

| 次 | 进程 | 调用到退出 | 退出码 | 诊断日志 | 任务之后 |
|---|---|---|---|---|---|
| R3 | 77612 | 126 ms | 0 | `ExitConfirmationSkippedExternalClose`，然后 `ProcessExited` | `Ready`，`0x00041306` |
| R6 之前 | 28380 | 128 ms | 0 | 同上 | `Ready`，`0x00041306` |
| R7 | 19136 | 98 ms | 0 | 同上 | `Ready`，`0x00041306` |

三次都没有 `FatalUnhandledException`。整份诊断日志里唯一的一条致命异常，就是 v3 故意抛出的那条。停止后设置仍与种子逐字节相同。#279 时同一操作的退出码是 1，并且留下一条 `FatalUnhandledException`；#295 修复后不再走崩溃路径。

### 4. 资源管理器打开程序目录时改名（R5）——已记录

- 打开窗口：用 `Shell.Application.Open` 打开 `<runDir>\root\app`，窗口不到 0.4 s 就出现在 Shell 窗口列表里。探针再等 3 秒，然后重装 v2。
- 安装照常成功，用时 16.65 s，与没开窗口的 R2 相近。
  - 旧进程 65616 在 12.1 s 被结束。
  - 两次改名在 13.41 s 与 13.68 s 两次采样之间完成，没有出现 `program-directory-in-use`。
  - 时间线上看不出重试。不过结果 JSON 不记重试次数，一次 100 ms 的重试排除不了（见不符项 8）。
  - 应用中断 3.94 s。
- 窗口表现：提交时 `app.previous` 被删掉（16.64 s）之后，窗口仍开着，位置（`Document.Folder.Self.Path`）仍是 `<runDir>\root\app`，也就是新换上的目录。推断它没有跟着改名转到 `app.previous`：要是跟过去了，旧目录被删时它会关掉或退到上一级。
- 关闭：探针从 Shell 窗口列表里对这个窗口调 `Quit()` 关掉，之后运行目录里没有资源管理器窗口；没有结束 `explorer.exe`。
- 结论：资源管理器开着程序目录不妨碍换目录。推断原因是资源管理器监视目录用的句柄允许改名和删除。未实测：工作目录在 `app` 里的进程，不带删除共享打开 `app` 里文件的程序（如编辑器），改名时杀毒软件正在扫描。

### 5. 清理与现场——满足

- 清理（14:43:36–14:43:41）：
  - 运行目录里没有残留进程。
  - 临时任务经生产 `RestoreTask` port 传空定义注销（#283 的删除路径，06:43:37Z），之后按名称和全路径都找不到。
  - 运行目录里没有资源管理器窗口。
- 现有任务：`Minmin Countdown` 与遗留的 `敏敏子倒计时` 前后都是 `ObjectNotFound`。根文件夹 `\` 前后都是 19 个任务，任务名加 XML 哈希的聚合值前后相同（`F9FEC7BA…2282`）。
- 安装目录与进程：`D:\敏敏子倒计时` 前后都不存在。运行目录之外前后都没有应用进程。
- 桌面上的 `敏敏子倒计时.lnk`：
  - R0 与清理之后的指纹相同：长度 1022，SHA-256 `96FE7302…C204`，创建时间 2026-09-09T15:50:23.78Z，修改时间 2026-10-28T02:39:49.84Z，属性 `Archive`。
  - 访问时间从 06:40:35 变成 06:43:41，因为安装器读过它，探针也算过哈希（见不符项 7）。
  - 安装器在 R1、R2、R4、R5、R6 都报告 `kept-foreign` / `other-target`。
- 运行目录经受管回收移入回收站：run `20261115T064420Z-cbf2b3c1`，状态 `recycled`，共 2765 个文件、185 个目录、1,022,283,790 字节，原位已不存在。探针原始输出留在会话临时目录，脱敏汇编进本目录。

## 顺带确认

- **#280 非提升注册登录触发器**：R1 在非提升 shell 里由安装器创建任务。定义是：登录触发器限定交互用户；`InteractiveToken`，`LeastPrivilege`（导出里省略 `RunLevel`）；优先级 4，`PT0S`，`IgnoreNew`；电池供电可启动、拔电不停。真实登录时触发器能否启动没有测。
- **#281 逐项比较与整份替换**：R2、R5 把优先级 5→4 整份重新注册，重新导出的 XML 与 R1 创建时逐字节相同（`00C4AB0A…`）。R6 逐项比较后报告 `unchanged`。导出里主体用户是 SID，触发器用户是“域\用户名”，逐项比较按 SID 没有误报不一致。
- **#282 设置迁移演练**：R1、R2、R4、R5、R6 与 DryRun 在真实候选上都是 `equivalent`（`current-schema` / `unchanged`）。
- **#283 回滚的 port**：`ExportTask` 加 `RestoreTask`（XML）往返逐字节一致（R4）。`RestoreTask` 传空定义会删除任务（清理）。`RemoveShortcut` 删真实 Shell Link 可行（DryRun，在运行目录里做）。回滚后经真实任务重新启动旧版本（R4）。
- **#284 中断恢复**：在真实安装根上，`mid-replace` 0.42 s 内改名换回（`renamed-back` / `recovered`），同一次调用随后安装成功（R6）。
- **#276 状态命令**：R1、R2、R4、R5、R6 五次读回与直接测量一致，覆盖提交、聚合哈希、EXE 与清单、运行 PID、任务状态、设置指纹和事务标记。

## 与测试假设不符或测试没覆盖的地方

1. **版本不在 EXE 里。** 测试夹具的两个版本 EXE 内容不同（`installer exe v1` / `v2`）。[Countdown.Core.Tests.Installer.ps1](../../../Countdown.Core.Tests.Installer.ps1) 的 `Test-CountdownInstallerTestNewVersion`，以及约 10 处“旧版本回来了”“新版本装上了”的断言，都按 EXE 哈希认版本。实测三个真实构建的 apphost EXE 逐字节相同，版本只体现在程序集（如 `MinminCountdown.dll`）和载荷清单里。
   - 生产路径不靠 EXE 区分版本：就绪核对 EXE 与清单一致，恢复按聚合哈希，部署按提交。实跑中这几处都正常。
   - 影响：这些断言在真实世界里不成立；以后要是加一处按 EXE 哈希认版本的检查，测试会过，真机会错。
   - 处理：实跑时未改，留作发现。随后在本票修正：夹具两个版本共用同一个 EXE，测试 port 与“旧版本回来了”“新版本装上了”的断言改按 DLL 认版本。部署入口的夹具只有一个版本，不受影响。
2. **新版本启动即崩溃，安装器仍等满 30 秒。** 测试把“起不来”建模成到时限都没有回执，没有“进程早早退出”的用例。实测 v3 在 0.4 s 内以退出码 1 退出，安装器仍等到 30 s 时限才回滚。结论是 `startup-not-ready`，错误写“运行状态回执尚未出现”，退出码和异常只在 `diagnosticLogPath` 里。
   - 影响：每次失败的升级多等约 30 秒，旧版本约 47 s 后才回来；要是进程一退出就判定失败，估计约 17 s。办公室部署结果也说不出新版本是崩溃了。
   - 处理：实跑时未改，留作发现。随后在本票修正：经登录任务启动后，那次运行已经结束、已安装 EXE 也没有进程在运行，就不再等就绪时限，马上回滚。失败原因为 `startup-exited`，结果的 `startupExitCode` 是退出码，取自任务的上次结果。有界的 `error` 写明退出码，`diagnosticLogPath` 照旧。部署入口照样报告这种失败与回滚结果，部署记录保留退出码。修正后没有在真机上重跑。
3. **经任务停止已经干净，安装器仍结束进程。** [Countdown.Installer.psm1](../../../Countdown.Installer.psm1) 的注释说停止任务会走崩溃路径（#279），#295 之后已不成立（验收项 3）。安装器结束进程时退出码是 `0xFFFFFFFF`，日志里没有 `ProcessExited`，被结束的版本只有启动没有退出。
   - 处理：已更新注释。行为不变，因为停止任务仍停不掉在任务之外启动的实例。要不要先体面停止再兜底结束，另行决定。
4. **非提升注册已实测，提升时注册的任务没有测。** 模块注释原本把“非提升时能否注册登录触发器、能否替换提升时注册的任务”留给本票。实测非提升时对非提升注册的任务，创建、整份替换、按 XML 恢复和删除都可行；已把结果写进注释。
   - 没有测：替换或删除提升时注册的同名任务（旧安装器 `Install-Countdown.ps1` 在管理员 PowerShell 里注册 `Minmin Countdown`）；标准用户。
   - 影响：#289 办公室切换要是以非提升身份运行新安装器，覆盖办公室现有任务时可能被拒绝。#289 之前要决定以提升身份运行，或者先在办公室确认。
5. **回滚恢复的是安装前的旧定义。** 这是 #283 的设计。R4 回滚后任务优先级回到 5，重新启动的旧版本内存优先级是 4 而不是 5，直到下一次成功安装（R5）把它改回 4。只记录。
6. **状态命令和安装器对“一致”的口径不同。** 状态命令接受优先级 4–6，所以优先级 5 时 `matchesExpected=true`；安装器只认 4，会整份重新注册。这是 #279 第 1 项定下的。部署的 `already-running` 判定不看任务，办公室任务一旦偏离，要到部署新提交时才会修正。只记录。
7. **桌面快捷方式的访问时间会变。** 安装器的 `ReadShortcut` 和探针都读了它。内容、长度、创建时间、修改时间和属性都没变，这台机器的卷开着访问时间更新。只记录。
8. **改名重试不可观测。** 测试覆盖了短暂占用时的有界重试（`-FileLockRetrySeconds`）。实测资源管理器不会引发重试，但结果 JSON 不记有没有重试过，办公室上差点超时的情况看不出来。只记录。

## 未覆盖

- 真实登录时登录触发器的启动；重启和注销。
- 双击真实桌面快捷方式和托盘入口：真实桌面的快捷方式属于主 checkout，按范围没动。
- 首次安装回滚删除新建的任务和快捷方式：按范围不在真实桌面上新建快捷方式，只在测试里覆盖。
- 真实安装根上除 `mid-replace` 以外的恢复状态：`after-replace`、`before-replace` 和残留。
- 标准用户；提升时注册的任务（见不符项 4）。
- 资源管理器以外的占用；办公室设备。

## 复现

按顺序运行下面五步。前四步每次都要带同一组 `-RunDirectory` 和 `-OutputDirectory`，`Run` 还要带用户同意的原话。

1. `Prepare`：传 `-PreviousApp` 与 `-CurrentApp` 两份门禁构建的 `app`，以及 `-FaultInjectedPublish`（按上面的做法构建的发布目录）。
2. `DryRun`。
3. `Run`：传 `-Consent`。
4. `Cleanup`。
5. 用 `Invoke-ManagedRecycle` 的 Check / Plan / Apply 把运行目录移入回收站。

运行目录必须叫 `minmin-288-<8 位十六进制>`。
