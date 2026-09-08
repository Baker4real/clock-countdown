#requires -Version 7.0
<#
.SYNOPSIS
    低影响官方套件启动器：前提假设是「用户正在使用这台电脑」。
    整棵套件进程树以 BelowNormal 优先级运行（子进程自动继承），
    含窗口的套件（CSharp/Ui）可经 -IsolatedDesktop 放入不可见 Win32 桌面，
    窗口不出现在用户屏幕上、不抢焦点。官方命令行本身逐字不变。
#>
[CmdletBinding()]
param(
    # 原生 argv 传入时逗号串是单个元素，故在函数体内自行拆分并校验。
    [Parameter(Mandatory)]
    [string[]] $Stage,

    [switch] $IsolatedDesktop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$allowedStages = @('Build', 'Core', 'CSharp', 'Ui', 'SourceValidation', 'InstallValidate')
$Stage = @($Stage | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$invalid = @($Stage | Where-Object { $_ -notin $allowedStages })
if ($Stage.Count -eq 0 -or $invalid.Count -gt 0) {
    throw "无效 Stage：$($invalid -join ', ')。允许值：$($allowedStages -join ', ')"
}

$suiteMutex = [System.Threading.Mutex]::new($false, 'Local\MinminCountdown.SuiteLowImpact')
if (-not $suiteMutex.WaitOne(0)) {
    throw '已有低影响套件启动器实例在运行，拒绝并发。'
}

try {
    try {
        [System.Diagnostics.Process]::GetCurrentProcess().PriorityClass =
            [System.Diagnostics.ProcessPriorityClass]::BelowNormal
    }
    catch {
        Write-Warning "无法设置 BelowNormal 优先级：$($_.Exception.Message)"
    }

    $repoRoot = Split-Path -Parent $PSScriptRoot
    $pwsh = (Get-Command pwsh).Source
    $logDir = Join-Path $repoRoot 'logs\suite-runs'
    [void] [System.IO.Directory]::CreateDirectory($logDir)
    $stamp = Get-Date -Format 'yyyyMMddTHHmmss'

    $stageTable = @{
        Build = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-File', (Join-Path $repoRoot 'Build-CountdownApplication.ps1'), '-Clean')
            Windowed = $false
        }
        Core = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-File', (Join-Path $repoRoot 'Countdown.Core.Tests.ps1'))
            Windowed = $false
        }
        CSharp = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-STA', '-File', (Join-Path $repoRoot 'Countdown.CSharp.Tests.ps1'))
            Windowed = $true
        }
        Ui = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-STA', '-File', (Join-Path $repoRoot 'Countdown.Ui.Tests.ps1'))
            Windowed = $true
        }
        SourceValidation = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-STA', '-File', (Join-Path $repoRoot 'Invoke-CountdownSourceValidation.ps1'))
            Windowed = $false
        }
        InstallValidate = @{
            ArgumentList = @('-NoLogo', '-NoProfile', '-File', (Join-Path $repoRoot 'Install-Countdown.ps1'), '-ValidateOnly')
            Windowed = $false
        }
    }

    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($name in $Stage) {
        $spec = $stageTable[$name]
        $stdoutPath = Join-Path $logDir "$stamp-$name.out.log"
        $stderrPath = Join-Path $logDir "$stamp-$name.err.log"
        # 隔离桌面同时承接控制台与 WPF 窗口；SourceValidation 等阶段也会启动应用渲染窗口，一律隔离。
        $isolated = [bool] $IsolatedDesktop
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        if ($isolated) {
            $launcher = Join-Path $PSScriptRoot 'Start-IsolatedSuiteProcess.ps1'
            $exitCode = [int] (& $launcher `
                -FilePath $pwsh `
                -ArgumentList $spec.ArgumentList `
                -WorkingDirectory $repoRoot `
                -StdoutPath $stdoutPath `
                -StderrPath $stderrPath |
                Select-Object -Last 1)
        }
        else {
            $process = Start-Process -FilePath $pwsh `
                -ArgumentList $spec.ArgumentList `
                -WorkingDirectory $repoRoot `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath `
                -WindowStyle Hidden -PassThru
            try {
                $process.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal
            }
            catch {
                Write-Warning "无法为子进程设置 BelowNormal：$($_.Exception.Message)"
            }
            $process.WaitForExit()
            $exitCode = $process.ExitCode
        }

        $stopwatch.Stop()
        $results.Add([pscustomobject]@{
            Stage = $name
            ExitCode = $exitCode
            ElapsedSeconds = [int] $stopwatch.Elapsed.TotalSeconds
            Isolated = $isolated
            Stdout = $stdoutPath
            Stderr = $stderrPath
        })
        if ($exitCode -ne 0) {
            break
        }
    }

    $summaryPath = Join-Path $logDir "$stamp-summary.json"
    $results | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    $results | Format-Table Stage, ExitCode, ElapsedSeconds, Isolated -AutoSize

    $failed = @($results | Where-Object { $_.ExitCode -ne 0 })
    if ($failed.Count -gt 0) {
        exit 1
    }
    exit 0
}
finally {
    $suiteMutex.ReleaseMutex()
    $suiteMutex.Dispose()
}
