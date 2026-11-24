#requires -Version 7.0
<#
.SYNOPSIS
新部署入口（#259 / #285 / #286）：把已推送的 main 当前提交部署到办公室，或只读查看办公室的安装状态，输出一份结果 JSON。
.DESCRIPTION
部署：本机预检（main、工作树干净、已推送；同一提交有全部必过阶段通过的套件记录，构建回执是完整的干净构建，
本机构建输出的聚合哈希等于该次套件运行的构建回执，Git 来源包文件匹配提交 blob，实际随包节假日数据通过年度覆盖守卫）；把程序目录、载荷清单和安装器打成一个包并生成根级全包清单；经远端执行入口做只读
连通性探测，探测就是一次状态读取：读到当前提交的这份构建输出（聚合哈希相同）已在运行就报告 already-running 并退出，
不传输、不重复安装；经传输入口
送到按运行编号命名的暂存目录并核对远端哈希；经远端执行入口核对包哈希、解包、核对生成清单自身哈希、运行包内安装器并取回结果 JSON；再做一次
只读状态读取，提交、EXE 哈希和运行中的进程都与预期一致才报告成功；写一份本地部署记录。送过去的就是本机套件测过的
那份构建输出，不重新导出、不重新构建。包和暂存内容不保留。
-StatusOnly 只做一次只读状态读取，不做任何改动，也不写记录。
办公室上的暂存目录建成只给 SYSTEM 与 Administrators 写，包与解出来的脚本别的账号改不了。
进度写到标准错误，最后一行是结论；结果 JSON 写到标准输出，退出码总是 0。结论看 outcome：deployed、already-running、
rejected（预检拒绝，没有连接办公室；或办公室上另一个安装正在运行，什么都没改）、
failed（succeeded 为 false）；unknown（succeeded 为 null，不知道办公室上的结果）。failedStep 是停下的步骤，reason 是原因类别，
error 写明缺什么或哪里不对；安装失败时 rollback 是
安装器报告的回滚结果；办公室现状不确定时 nextStep 提示先用 -StatusOnly 读状态。输出和记录不含主机名、地址、账号或凭据：
连接由主机受管的两个入口负责。-Port 供测试注入假的远端执行与传输 port；不传时接主机入口。
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
