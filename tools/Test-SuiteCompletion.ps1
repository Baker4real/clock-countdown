#requires -Version 7.0
[CmdletBinding()]
param([switch]$Json)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteRun.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.SuiteStages.psm1') -Force
$required=@(Get-CountdownRequiredSuiteStage)
$stages=@($required | ForEach-Object { [pscustomobject]@{Stage=$_;Status='Completed';ExitCode=0;BuildReceipt=$null} })
$run=[pscustomobject]@{Status='Completed';ExitCode=0;Stages=$stages}
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$Value,[string]$Name) { if (-not $Value) { throw "Suite completion regression: $Name" }; $checks.Add($Name) }
Check (($required -join ',') -ceq 'Build,Core,CSharp,SourceValidation,InstallValidate') 'required-stage-list-is-five-without-ui'
$launcherPath=Join-Path $PSScriptRoot 'Invoke-CountdownSuiteLowImpact.ps1'
$priorNativePreference=$PSNativeCommandUseErrorActionPreference
$PSNativeCommandUseErrorActionPreference=$false
$uiRejection=@(& pwsh -NoLogo -NoProfile -NonInteractive -File $launcherPath -Stage Ui -Closeout 2>&1)
$uiRejectionCode=$LASTEXITCODE
$PSNativeCommandUseErrorActionPreference=$priorNativePreference
Check ($uiRejectionCode -ne 0 -and ($uiRejection -join ' ') -match '无效 Stage：Ui') 'ui-stage-is-rejected-before-any-test-execution'
$stages[0].BuildReceipt=[pscustomobject]@{SchemaVersion=1;Mode='Incremental';Clean=$false;ReadyToRun=$true}
Check (-not (Test-CountdownSuiteCompletion -Run $run).Valid) 'incremental-build-zero-cannot-complete-closeout'
$stages[0].BuildReceipt=[pscustomobject]@{SchemaVersion=1;Mode='CleanR2R';Clean=$true;ReadyToRun=$true}
Check (Test-CountdownSuiteCompletion -Run $run).Valid 'clean-r2r-and-required-stages-complete-closeout'
$stages[0].BuildReceipt=$null
Check (-not (Test-CountdownSuiteCompletion -Run $run).Valid) 'build-zero-without-mode-receipt-is-insufficient'
$stages[0].BuildReceipt=[pscustomobject]@{Mode='CleanR2R'}
Check (-not (Test-CountdownSuiteCompletion -Run $run).Valid) 'incomplete-build-mode-receipt-fails-closed'
$stages[0].BuildReceipt=[pscustomobject]@{SchemaVersion=1;Mode='CleanR2R';Clean=$true;ReadyToRun=$false}
Check (-not (Test-CountdownSuiteCompletion -Run $run).Valid) 'clean-without-r2r-is-insufficient'
$stages[0].BuildReceipt=[pscustomobject]@{SchemaVersion=1;Mode='CleanR2R';Clean=$true;ReadyToRun=$true}
$stages[1].Status='Failed';$stages[1].ExitCode=1
Check (-not (Test-CountdownSuiteCompletion -Run $run).Valid) 'failed-required-stage-cannot-be-masked-by-build'
$stages[1].Status='Completed';$stages[1].ExitCode=0
$missingRequired=$required[-1]
$run.Stages=@($stages | Where-Object Stage -ne $missingRequired)
$missingResult=Test-CountdownSuiteCompletion -Run $run
Check (-not $missingResult.Valid -and ($missingResult.Failures -join ';') -match [regex]::Escape($missingRequired)) 'missing-required-stage-cannot-complete-closeout'
# 经公开调度器执行真实子进程，验证解析的是构建实际输出，不能只信请求标签。
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('countdown-suite-completion-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$pwsh=(Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
try {
    $makeSpecs={
        param([string]$ActualMode)
        foreach ($name in $required) {
            $receipt=@{SchemaVersion=1;Mode=$ActualMode;Clean=$true;ReadyToRun=$true} | ConvertTo-Json -Compress
            $code=if ($name -eq 'Build') { "Write-Output 'COUNTDOWN_BUILD_RECEIPT=$receipt'; exit 0" } else { 'exit 0' }
            $spec=@{Stage=$name;FilePath=$pwsh;ArgumentList=@('-NoLogo','-NoProfile','-NonInteractive','-Command',$code);Isolated=$false}
            if ($name -eq 'Build') { $spec.BuildMode='CleanR2R' }
            $spec
        }
    }
    $good=Invoke-SuiteRun -StageSpecs @(& $makeSpecs 'CleanR2R') -RepositoryRoot $fixture -LogDirectory (Join-Path $fixture 'good') -MaxParallel 1 -Closeout 6>$null
    if ($good.Status -cne 'Completed') { Write-Host ($good | Select-Object Status,Failure,@{Name='Stages';Expression={@($_.Stages | Select-Object Stage,Failure,Status)}} | ConvertTo-Json -Depth 5 -Compress) }
    Check ($good.Status -ceq 'Completed' -and $good.Closeout.Valid -and $good.Stages[0].BuildReceipt.Mode -ceq 'CleanR2R') 'real-clean-receipt-is-persisted-and-completes-closeout'
    $wrong=Invoke-SuiteRun -StageSpecs @(& $makeSpecs 'Incremental') -RepositoryRoot $fixture -LogDirectory (Join-Path $fixture 'wrong') -MaxParallel 1 -Closeout 6>$null
    Check ($wrong.ExitCode -ne 0 -and -not $wrong.Closeout.Valid) 'actual-incremental-receipt-cannot-be-labeled-clean'
    # 前置预检使用真实入口；缺口在导入窗口隔离工具前即失败，不启动任何阶段。
    $tools=Join-Path $fixture 'tools';[void][IO.Directory]::CreateDirectory($tools)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-CountdownSuiteLowImpact.ps1') -Destination $tools
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Countdown.SuiteStages.psm1') -Destination $tools
    @'
param([string]$RepositoryRoot,[switch]$RequireClean,[switch]$Json)
[pscustomobject]@{Valid=$false;Failures=@('unregistered-clean-root');RequireClean=[bool]$RequireClean}|ConvertTo-Json
exit 1
'@ | Set-Content -LiteralPath (Join-Path $tools 'Test-CountdownBuildPreflight.ps1') -Encoding utf8
    $prior=$PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference=$false
    $rejection=@(& $pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $tools 'Invoke-CountdownSuiteLowImpact.ps1') -Stage ($required -join ',') -Closeout 2>&1)
    $rejectionCode=$LASTEXITCODE
    $PSNativeCommandUseErrorActionPreference=$prior
    Check ($rejectionCode -ne 0 -and ($rejection -join ' ') -match 'Closeout preflight failed.*unregistered-clean-root' -and -not (Test-Path -LiteralPath (Join-Path $fixture 'logs'))) 'unregistered-clean-root-rejected-before-long-stages'
} finally {
    $absolute=[IO.Path]::GetFullPath($fixture)
    if (-not $absolute.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'countdown-suite-completion-'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected suite fixture cleanup path.' }
    [IO.Directory]::Delete($absolute,$true)
}
[pscustomobject]@{Valid=$true;Checks=@($checks)} | ConvertTo-Json -Compress
exit 0
