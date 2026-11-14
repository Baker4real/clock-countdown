#requires -Version 7.0
<#
.SYNOPSIS
新部署入口（#259 / #285）：把已推送的 main 当前提交部署到办公室，或只读查看办公室的安装状态，输出一份结果 JSON。
.DESCRIPTION
部署：本机预检（main、工作树干净、已推送；同一提交有全部必过阶段通过的套件记录，构建回执是完整的干净构建，
本机构建输出的聚合哈希等于该次套件运行的构建回执）；把程序目录、载荷清单和安装器打成一个包；经远端执行入口做只读
连通性探测；经传输入口送到按运行编号命名的暂存目录并核对远端哈希；经远端执行入口核对包哈希、解包、运行包内安装器
并取回结果 JSON；再做一次只读状态读取，提交、EXE 哈希和运行中的进程都与预期一致才报告成功；写一份本地部署记录。
送过去的就是本机套件测过的那份构建输出，不重新导出、不重新构建。包和暂存内容不保留。
-StatusOnly 只做一次只读状态读取，不做任何改动，也不写记录。
进度写到标准错误，结果 JSON 写到标准输出；成败看 succeeded 与 failedStep，退出码总是 0。输出和记录不含主机名、地址、
账号或凭据：连接由主机受管的两个入口负责。-Port 供测试注入假的远端执行与传输 port；不传时接主机入口。
#>
[CmdletBinding()]
param(
    [switch] $StatusOnly,
    [string] $RepositoryRoot = $PSScriptRoot,
    [string] $RecordRoot = 'D:\agents\run-evidence\clock-countdown-program-deploy',
    [string] $RemoteInstallRoot = 'D:\敏敏子倒计时',
    [string] $TaskName = 'Minmin Countdown',
    [string] $RemoteStagingRoot = 'D:\Codex\deploy',
    [object] $Port
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Deployer.psm1') -Force

if ($null -eq $Port) { $Port = New-CountdownDeployerHostPorts }
$arguments = @{
    StatusOnly = $StatusOnly
    RepositoryRoot = $RepositoryRoot
    RecordRoot = $RecordRoot
    RemoteInstallRoot = $RemoteInstallRoot
    TaskName = $TaskName
    RemoteStagingRoot = $RemoteStagingRoot
    Port = $Port
}
Invoke-CountdownDeployment @arguments | ConvertTo-Json -Depth 12 -EscapeHandling EscapeNonAscii
