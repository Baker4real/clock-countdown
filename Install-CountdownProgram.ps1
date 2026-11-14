#requires -Version 7.0
<#
.SYNOPSIS
新安装器入口（#259 / #280）：把包里的程序目录装到安装根，输出一份结果 JSON。
.DESCRIPTION
首次安装与升级走同一流程：取得全机安装锁；前置检查（安装路径、唯一的交互会话及其桌面目录、候选程序目录按载荷清单逐项核对）；
停止应用；写下事务标记；把设置及其两代备份快照到事务目录，有设置文件时用候选 EXE 在副本上演练迁移并做等价判定（#282，
不等价就不换程序，撤回本次安装并重新启动旧版本）；替换程序目录；登录任务不存在时创建、与期望定义不一致时更新；
登记桌面快捷方式 敏敏子倒计时.lnk（缺失时创建，属于安装器时改回期望定义，不属于时保留）；经登录任务启动并等到启动就绪；
先清除事务标记，再删除旧版本目录与事务目录。
只替换程序目录：设置、设置备份、恢复证据、诊断日志和节假日缓存不被移动或改写。本机与远端是同一个入口。
-CheckOnly 只执行到前置检查，不产生任何改动，结果的 plannedSteps 写明将要执行的步骤。
结果 JSON 写明成功或失败、失败阶段与原因、设置迁移演练的结论、登录任务与桌面快捷方式各做了什么（改了哪些项），
以及设置在安装前后的指纹（长度、修改时间与 SHA-256），不含设置内容、凭据或账号。安装失败同样输出结果 JSON，
成败看 succeeded 与 failedStage。
-Port 供测试注入计划任务、进程、交互会话、桌面目录、快捷方式、目录改名与候选 EXE 的无界面命令，-LockName 供测试隔离安装锁；
不传时操作真实 Windows。
#>
[CmdletBinding()]
param(
    [string] $PackageRoot = $PSScriptRoot,
    [string] $InstallRoot = 'D:\敏敏子倒计时',
    [string] $TaskName = 'Minmin Countdown',
    [switch] $CheckOnly,
    [object] $Port,
    [string] $LockName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Installer.psm1') -Force

$arguments = @{ PackageRoot = $PackageRoot; InstallRoot = $InstallRoot; TaskName = $TaskName; CheckOnly = $CheckOnly }
if ($null -ne $Port) { $arguments.Ports = $Port }
if (-not [string]::IsNullOrWhiteSpace($LockName)) { $arguments.LockName = $LockName }
Install-CountdownProgram @arguments | ConvertTo-Json -Depth 6 -EscapeHandling EscapeNonAscii
