#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CountdownRequiredSuiteStage {
    <#
    .SYNOPSIS
        返回低影响套件当前全部必过阶段；启动器、运行记录与办公室部署门共用这一份名单。
    #>
    [CmdletBinding()]
    param()

    [string[]] @('Build', 'Core', 'CSharp', 'SourceValidation', 'InstallValidate')
}

function Resolve-CountdownSuiteMaxParallel {
    <#
    .SYNOPSIS
        解析低影响套件并行度：正式 Closeout 默认串行（1）以免并行重阶段挤占 90–160ms WPF 动效采样窗口，
        显式传 -MaxParallel 时保留调用方取值。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidateRange(1, 5)] [int] $RequestedMaxParallel,
        [Parameter(Mandatory)] [bool] $MaxParallelBound,
        [Parameter(Mandatory)] [bool] $Closeout
    )

    if ($Closeout -and -not $MaxParallelBound) { return 1 }
    return $RequestedMaxParallel
}

Export-ModuleMember -Function Get-CountdownRequiredSuiteStage, Resolve-CountdownSuiteMaxParallel
