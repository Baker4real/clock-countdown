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

    [string[]] @('Build', 'Core', 'CSharp', 'Ui', 'SourceValidation', 'InstallValidate')
}

Export-ModuleMember -Function Get-CountdownRequiredSuiteStage
