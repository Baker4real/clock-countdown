#requires -Version 7.0
<#
.SYNOPSIS
新安装器入口（#259 / #280）：把包里的程序目录装到安装根，输出一份结果 JSON。
.DESCRIPTION
首次安装与升级走同一流程：先验证根级生成包清单、应用载荷清单与全包精确闭包（改动、多出、缺少均拒绝），
再取得全机安装锁；中断恢复（#284）：安装根留有上次没走完的安装时先按事务标记与目录状态收拾好，
收拾不了时停在恢复阶段、不继续安装；前置检查（安装路径、唯一的交互会话及其桌面目录、候选程序目录精确核对）；
停止应用；写下事务标记；把设置及其两代备份快照到事务目录，有设置文件时用候选 EXE 在副本上演练迁移并做等价判定（#282，
不等价就不换程序）；替换程序目录；登录任务不存在时创建、与期望定义不一致时更新；
登记桌面快捷方式 敏敏子倒计时.lnk（缺失时创建，属于安装器时改回期望定义，不属于时保留）；经登录任务启动并等到启动就绪
（启动的进程在启动就绪之前就退出时不等满时限，原因 startup-exited，#288）；
先清除事务标记，再删除旧版本目录与事务目录。
写下事务标记之后、新版本达到启动就绪之前任何一步失败都回滚（#283）：换回程序目录；程序换过时用本次的快照恢复设置及两代
备份；恢复本次改过的登录任务；首次安装删除本次创建的登录任务与桌面快捷方式；有旧版本时经登录任务重新启动它。新版本达到
启动就绪之后不再回滚，也不再用快照覆盖设置。
只替换程序目录：恢复证据、诊断日志和节假日缓存不被移动或改写，设置及其备份只在上述回滚里写回快照。本机与远端是同一个入口。
全包校验在安装锁、中断恢复、候选创建和任何 Windows port 之前完成。真实安装随后、取得锁之前执行节假日年度覆盖守卫；CheckOnly 按契约豁免。CheckOnly 只执行到前置检查，不产生任何改动
（也不做中断恢复，只报告看到的状态），结果的 plannedSteps 写明将要执行的步骤；失败时输出同一份失败 JSON 并以非零退出。
结果 JSON 写明成功或失败、失败阶段与原因、有界的错误摘要、中断恢复 recovery（看到的目录状态、做了什么、结论与停下的原因）、
设置迁移演练的结论、登录任务与桌面快捷方式各做了什么（改了哪些项）、
回滚结果 rollback（outcome 为 not-needed、rolled-back 或 rollback-failed）、新版本在启动就绪之前退出时它的退出码
startupExitCode、事务标记是否还在、应用诊断日志的位置，
以及设置在安装前后的指纹（长度、修改时间与 SHA-256），不含设置内容、凭据或账号。安装失败同样输出结果 JSON，
成败看 succeeded 与 failedStage。
-Port 供测试注入计划任务、进程、交互会话、桌面目录、快捷方式、目录改名与候选 EXE 的无界面命令，-LockName 供测试隔离安装锁，
-BeijingNow 供测试确定节假日年度覆盖守卫的当前北京时间；
不传时操作真实 Windows。
#>
[CmdletBinding()]
param(
    [string] $PackageRoot = $PSScriptRoot,
    [string] $InstallRoot = 'D:\敏敏子倒计时',
    [string] $TaskName = 'Minmin Countdown',
    [switch] $CheckOnly,
    [object] $Port,
    [string] $LockName,
    [DateTimeOffset] $BeijingNow = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'China Standard Time')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force

$arguments = @{ PackageRoot = $PackageRoot; InstallRoot = $InstallRoot; TaskName = $TaskName; CheckOnly = $CheckOnly; BeijingNow = $BeijingNow }
if ($null -ne $Port) { $arguments.Ports = $Port }
if (-not [string]::IsNullOrWhiteSpace($LockName)) { $arguments.LockName = $LockName }
$result = Install-CountdownProgram @arguments
$json = $result | ConvertTo-Json -Depth 6 -EscapeHandling EscapeNonAscii
if ($CheckOnly -and -not [bool] $result.succeeded) {
    # InstallValidate 以进程退出码判阶段；失败 JSON 必须同时非零闭锁，不能假绿。
    [Console]::Out.WriteLine($json)
    exit 1
}
$json
