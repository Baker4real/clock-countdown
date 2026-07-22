#requires -Version 7.0

[CmdletBinding()]
param([switch] $ParentTransactionMutexHeld)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Notes.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Tags.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking

$failures = [System.Collections.Generic.List[string]]::new()
$script:assertions = 0
function Assert-Equal($Expected, $Actual, [string] $Name) {
    $script:assertions++
    if ($Expected -ne $Actual) {
        $failures.Add("$Name expected=[$Expected] actual=[$Actual]")
    }
}

$firstTag = New-CountdownTagRecord -Id ' work ' -Name ' 工作 ' -Color ' #6B8E23 '
Assert-Equal 'work' $firstTag.id 'Tag record normalizes ID'
Assert-Equal '工作' $firstTag.name 'Tag record normalizes name'
Assert-Equal '#6B8E23' $firstTag.color 'Tag record preserves optional color'
$invalidTagColorRejected = $false
try {
    New-CountdownTagRecord -Id 'invalid-color' -Name '无效颜色' -Color 'red' | Out-Null
}
catch {
    $invalidTagColorRejected = $true
}
Assert-Equal $true $invalidTagColorRejected 'Invalid tag color is rejected'
$tagLibrary = [System.Collections.ArrayList]::new()
[void] $tagLibrary.Add($firstTag)
$tagLibraryCopy = Copy-CountdownTags -Tags $tagLibrary
$tagLibraryCopy[0].name = '已改名'
Assert-Equal '工作' $tagLibrary[0].name 'Tag library copy does not mutate source tag'
$normalizedTagIds = ConvertTo-CountdownTagIds -TagIds @(' work ', 'home') -KnownTagIds @('work', 'home')
Assert-Equal 2 $normalizedTagIds.Count 'Tag IDs retain unique values'
Assert-Equal 'work' $normalizedTagIds[0] 'Tag IDs preserve first-known canonical value'
Assert-Equal 'home' $normalizedTagIds[1] 'Tag IDs retain additional values'
$emptyTagIds = ConvertTo-CountdownTagIds -TagIds $null -KnownTagIds @('work')
Assert-Equal 0 $emptyTagIds.Count 'Empty tag IDs are valid for untagged records'
$duplicateTagIdRejected = $false
try {
    ConvertTo-CountdownTagIds -TagIds @('work', 'work') -KnownTagIds @('work') | Out-Null
}
catch {
    $duplicateTagIdRejected = $true
}
Assert-Equal $true $duplicateTagIdRejected 'Duplicate tag ID is rejected'
$danglingTagRejected = $false
try {
    ConvertTo-CountdownTagIds -TagIds @('missing') -KnownTagIds @('work') | Out-Null
}
catch {
    $danglingTagRejected = $true
}
Assert-Equal $true $danglingTagRejected 'Unknown tag ID is rejected'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @() -MatchMode Any) 'Empty tag selection includes tagged records'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @('work') -MatchMode Any) 'Any tag filter matches selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('home') -SelectedTagIds @('work') -MatchMode Any) 'Any tag filter excludes unselected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('home') -MatchMode Any) 'Any tag filter evaluates a non-first selected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('work', 'home') -MatchMode All) 'All tag filter matches every selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @('work', 'home') -MatchMode All) 'All tag filter requires every selected tag'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work', 'home') -SelectedTagIds @('home', 'office') -MatchMode All) 'All tag filter excludes a missing selected tag'
Assert-Equal $true (Test-CountdownTagFilter -TagIds @() -SelectedTagIds @() -UntaggedOnly) 'Untagged filter includes untagged record'
Assert-Equal $false (Test-CountdownTagFilter -TagIds @('work') -SelectedTagIds @() -UntaggedOnly) 'Untagged filter excludes tagged record'

$target = ConvertTo-CountdownTargetUtc -DateText '2026-07-15' -TimeText '18:00:00'
Assert-Equal '2026-07-15T10:00:00.0000000+00:00' $target.ToString('o') 'Beijing to UTC'

$parts = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-17T11:02:03Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:00Z')
Assert-Equal $false $parts.IsElapsed 'Future target state'
Assert-Equal 2 $parts.Days 'Days'
Assert-Equal 1 $parts.Hours 'Hours'
Assert-Equal 2 $parts.Minutes 'Minutes'
Assert-Equal 3 $parts.Seconds 'Seconds'
Assert-Equal '2 天' (Format-CountdownParts -Parts $parts -Precision Day) 'Day precision format'
Assert-Equal '2 天 01 小时' (Format-CountdownParts -Parts $parts -Precision Hour) 'Hour precision format'
Assert-Equal '2 天 01 小时 02 分钟' (Format-CountdownParts -Parts $parts -Precision Minute) 'Minute precision format'
Assert-Equal '2 天 01 小时 02 分钟 03 秒' (Format-CountdownParts -Parts $parts -Precision Second) 'Second precision format'

$elapsed = Get-CountdownParts -TargetUtc ([DateTimeOffset]'2026-07-15T10:00:00Z') -NowUtc ([DateTimeOffset]'2026-07-15T10:00:01Z')
Assert-Equal $true $elapsed.IsElapsed 'Elapsed state'
Assert-Equal '已到期' (Format-CountdownParts -Parts $elapsed -Precision Second) 'Elapsed format'

$workdayMidpoint = Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-15T13:30:00+08:00')
Assert-Equal ([decimal]500) $workdayMidpoint 'Midpoint workday earnings'
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-15T08:59:59+08:00')) 'Before work earnings'
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]23000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-07-18T13:30:00+08:00')) 'Weekend earnings'
$statutoryCalendar = @{ '2026-01-01' = $false; '2026-01-04' = $true }
Assert-Equal ([decimal]0) (Get-WorkdayEarnings -MonthlySalary ([decimal]22000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-01-01T13:30:00+08:00') -WorkdayMap $statutoryCalendar) 'Statutory paid leave earnings'
Assert-Equal ([decimal]500) (Get-WorkdayEarnings -MonthlySalary ([decimal]22000) -WorkStart ([TimeSpan]'09:00') -WorkEnd ([TimeSpan]'18:00') -BeijingNow ([DateTimeOffset]'2026-01-04T13:30:00+08:00') -WorkdayMap $statutoryCalendar) 'Compensatory workday earnings'
$invalidWorkHoursRejected = $false
try { Get-WorkdayEarnings -MonthlySalary ([decimal]1) -WorkStart ([TimeSpan]'18:00') -WorkEnd ([TimeSpan]'09:00') -BeijingNow ([DateTimeOffset]'2026-07-15T13:30:00+08:00') | Out-Null }
catch { $invalidWorkHoursRejected = $true }
Assert-Equal $true $invalidWorkHoursRejected 'Invalid work hours rejected'

$invalidRejected = $false
try { ConvertTo-CountdownTargetUtc -DateText '2026-02-30' -TimeText '18:00:00' | Out-Null }
catch { $invalidRejected = $true }
Assert-Equal $true $invalidRejected 'Invalid date rejected'

$runtimeTestRoot = Join-Path $PSScriptRoot ("runtime-test-" + [guid]::NewGuid().ToString('N'))
$runtimeStatusTestPath = Join-Path $runtimeTestRoot 'runtime-status.json'
$startupRequestTestPath = Join-Path $runtimeTestRoot 'startup-request.json'
$runtimeApplicationPath = Join-Path $runtimeTestRoot '敏敏子倒计时.ps1'
$diagnosticLogTestPath = Join-Path $runtimeTestRoot 'logs\application.jsonl'
try {
    New-Item -ItemType Directory -Path $runtimeTestRoot -Force | Out-Null
    [System.IO.File]::WriteAllText($runtimeApplicationPath, '# test application', (New-Object System.Text.UTF8Encoding($false)))
    $startupRequest = New-CountdownStartupRequest -Path $startupRequestTestPath
    Assert-Equal 1 $startupRequest.schemaVersion 'Startup request schema'
    Assert-Equal $true (-not [string]::IsNullOrWhiteSpace([string] $startupRequest.launchToken)) 'Startup request has token'
    Assert-Equal $false (Test-Path -LiteralPath "$startupRequestTestPath.tmp") 'Startup request leaves no fixed temporary file'
    $receipt = Write-CountdownWindowReadyReceipt -Path $runtimeStatusTestPath -LaunchToken $startupRequest.launchToken -ProcessId 4242 -SessionId 7 -ApplicationPath $runtimeApplicationPath -CalendarState Pending
    $processProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -STA -File `"$runtimeApplicationPath`"" }
        }
    }
    $ready = Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe
    Assert-Equal $true $ready.Ready 'Window-ready receipt accepts current token, process, session and path'
    Assert-Equal 'Pending' $ready.Receipt.calendarState 'Window-ready does not wait for calendar'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken 'old-token' -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe).Ready 'Old startup token is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 8 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $processProbe).Ready 'Wrong interactive session is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath (Join-Path $runtimeTestRoot 'other.ps1') -ProcessProbe $processProbe).Ready 'Wrong application path is rejected'
    $embeddedPathProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -File `"$(Join-Path $runtimeTestRoot 'helper.ps1')`" -ArgumentList `"$runtimeApplicationPath`"" }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $embeddedPathProbe).Ready 'Command-line path substring outside -File is rejected'
    $commandModeProbe = {
        param([int] $ProcessId)
        if ($ProcessId -eq 4242) {
            [pscustomobject]@{ ProcessId = 4242; SessionId = 7; CommandLine = "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`"" }
        }
    }
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe $commandModeProbe).Ready 'Command-mode text containing -File is not accepted as the application process'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe { param([int] $ProcessId) $null }).Ready 'Missing process is rejected'
    Assert-Equal $false (Test-CountdownWindowReadyReceipt -Receipt $receipt -ExpectedLaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -ProcessProbe { param([int] $ProcessId) [pscustomobject]@{ ProcessId = 4243; SessionId = 7; CommandLine = "pwsh.exe -File `"$runtimeApplicationPath`"" } }).Ready 'Mismatched process identity is rejected'
    [System.IO.File]::WriteAllText($runtimeStatusTestPath, '{not-valid-json', (New-Object System.Text.UTF8Encoding($false)))
    Assert-Equal $true ($null -eq (Read-CountdownRuntimeReceipt -Path $runtimeStatusTestPath)) 'Malformed receipt is not accepted'
    $timeoutObserved = $false
    try {
        Wait-CountdownWindowReady -Path $runtimeStatusTestPath -LaunchToken $startupRequest.launchToken -ExpectedSessionId 7 -ExpectedApplicationPath $runtimeApplicationPath -TimeoutSeconds 0 -PollMilliseconds 1 -ProcessProbe { param([int] $ProcessId) $null } | Out-Null
    }
    catch { $timeoutObserved = $_.Exception.Message -match '启动就绪回执超时' }
    Assert-Equal $true $timeoutObserved 'Window-ready wait has a bounded timeout'
    $diagnosticStarted = Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Info -Event 'ProcessStarted'
    Assert-Equal $true $diagnosticStarted 'Diagnostic logging reports a successful append'
    $diagnosticStartEntry = Get-Content -LiteralPath $diagnosticLogTestPath -Encoding UTF8 | Select-Object -First 1 | ConvertFrom-Json
    Assert-Equal 1 $diagnosticStartEntry.schemaVersion 'Diagnostic log schema'
    Assert-Equal 'ProcessStarted' $diagnosticStartEntry.event 'Diagnostic log event name'
    Assert-Equal 'Info' $diagnosticStartEntry.level 'Diagnostic log level'
    Assert-Equal $PID $diagnosticStartEntry.processId 'Diagnostic log process identity'
    $diagnosticException = [System.InvalidOperationException]::new('Deferred theme template failed')
    $diagnosticFailed = Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Error -Event 'FatalUnhandledException' -Exception $diagnosticException -ScriptStackTrace 'Start-CountdownApplication line 1'
    Assert-Equal $true $diagnosticFailed 'Diagnostic logging records fatal exceptions'
    $diagnosticErrorEntry = Get-Content -LiteralPath $diagnosticLogTestPath -Encoding UTF8 | Select-Object -Last 1 | ConvertFrom-Json
    Assert-Equal 'System.InvalidOperationException' $diagnosticErrorEntry.exceptionType 'Diagnostic log exception type'
    Assert-Equal 'Deferred theme template failed' $diagnosticErrorEntry.exceptionMessage 'Diagnostic log exception message'
    Assert-Equal 'Start-CountdownApplication line 1' $diagnosticErrorEntry.scriptStackTrace 'Diagnostic log PowerShell stack'
    foreach ($index in 1..3) {
        Write-CountdownDiagnosticEvent -Path $diagnosticLogTestPath -Level Info -Event ("RotationProbe$index-" + ('x' * 180)) -MaxBytes 300 -MaxArchives 2 | Out-Null
    }
    Assert-Equal $true (Test-Path -LiteralPath "$diagnosticLogTestPath.1" -PathType Leaf) 'Diagnostic log rotates at the size bound'
    Assert-Equal $true ((Get-Item -LiteralPath $diagnosticLogTestPath).Length -lt 1024) 'Diagnostic current log remains bounded after rotation'
}
finally {
    Remove-Item -LiteralPath $runtimeTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$transactionModule = Get-Module -Name 'Countdown.InstallTransaction'
$installPathGuardCommand = Get-Command Assert-CountdownInstallPathOnDDrive -Module Countdown.InstallTransaction -ErrorAction SilentlyContinue
Assert-Equal $true ($null -ne $installPathGuardCommand) 'Installer-facing D-drive path guard is exported by the transaction module'
Assert-Equal 'D:\Codex\projects\时钟倒计时\expected-desktop' (Assert-CountdownInstallPathOnDDrive -Path 'D:\Codex\projects\时钟倒计时\expected-desktop') 'Installer-facing D-drive path guard normalizes a safe expected desktop path'
$outsideDDriveRejected = $false
try {
    Assert-CountdownInstallPathOnDDrive -Path 'C:\unexpected-desktop' | Out-Null
}
catch {
    $outsideDDriveRejected = $_.Exception.Message -match '只允许操作 D 盘路径'
}
Assert-Equal $true $outsideDDriveRejected 'Installer-facing D-drive path guard rejects a non-D expected desktop path'
$managedFilePath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -File `"$runtimeApplicationPath`""
Assert-Equal ([System.IO.Path]::GetFullPath($runtimeApplicationPath)) $managedFilePath 'Managed-process parser resolves exact -File application path'
$managedEmbeddedPath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -File `"$(Join-Path $PSScriptRoot 'helper.ps1')`" -ArgumentList `"$runtimeApplicationPath`""
Assert-Equal ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'helper.ps1'))) $managedEmbeddedPath 'Managed-process parser does not treat an argument substring as the script path'
$managedCommandEmbeddedPath = & $transactionModule { param($CommandLine) Get-CountdownManagedApplicationFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`""
Assert-Equal $true ($null -eq $managedCommandEmbeddedPath) 'Managed-process parser rejects -File text inside -Command argv'
$runtimeModule = Get-Module -Name 'Countdown.Runtime'
$runtimeCommandEmbeddedPath = & $runtimeModule { param($CommandLine) Get-CountdownRuntimeFileArgument -CommandLine $CommandLine } "pwsh.exe -NoProfile -Command `"Write-Output ' -File $runtimeApplicationPath '`""
Assert-Equal $true ($null -eq $runtimeCommandEmbeddedPath) 'Runtime-process parser rejects -File text inside -Command argv'
$resolvedInteractiveDesktop = & $transactionModule {
    Resolve-CountdownInteractiveDesktopPath -ProfileImagePath '%SystemDrive%\Profiles\OfficeUser' -DesktopTemplate '%OneDrive%\Desktop' -UserName 'OfficeUser' -EnvironmentVariables @{ SystemDrive = 'D:'; OneDrive = '%USERPROFILE%\OneDrive' }
}
Assert-Equal 'D:\Profiles\OfficeUser\OneDrive\Desktop' $resolvedInteractiveDesktop 'Desktop resolver uses the Explorer SID environment rather than the current pwsh environment'
$unknownDesktopVariableRejected = $false
try {
    & $transactionModule {
        Resolve-CountdownInteractiveDesktopPath -ProfileImagePath '%SystemDrive%\Profiles\OfficeUser' -DesktopTemplate '%UnmappedDesktop%\Desktop' -UserName 'OfficeUser' -EnvironmentVariables @{ SystemDrive = 'D:' }
    } | Out-Null
}
catch {
    $unknownDesktopVariableRejected = $_.Exception.Message -match '无法按交互用户环境解析'
}
Assert-Equal $true $unknownDesktopVariableRejected 'Desktop resolver rejects variables missing from the Explorer SID environment'
$sameSessionExplorerContext = & $transactionModule {
    Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(
        [pscustomobject]@{ ProcessId = 101; SessionId = 7 },
        [pscustomobject]@{ ProcessId = 102; SessionId = 7 }
    ) -OwnerResolver {
        param($process)
        [pscustomobject]@{ ReturnValue = 0; Domain = 'OFFICE'; User = 'Minmin' }
    }
}
Assert-Equal 7 $sameSessionExplorerContext.SessionId 'Multiple Explorer processes in the same interactive session are accepted'
Assert-Equal 'OFFICE\Minmin' $sameSessionExplorerContext.User 'Explorer grouping retains the interactive user identity'
$multipleExplorerContextsRejected = $false
try {
    & $transactionModule {
        Get-CountdownSingleInteractiveExplorerContext -ExplorerProcesses @(
            [pscustomobject]@{ ProcessId = 201; SessionId = 7 },
            [pscustomobject]@{ ProcessId = 202; SessionId = 8 }
        ) -OwnerResolver {
            param($process)
            [pscustomobject]@{ ReturnValue = 0; Domain = 'OFFICE'; User = 'Minmin' }
        } | Out-Null
    }
}
catch {
    $multipleExplorerContextsRejected = $_.Exception.Message -match 'exactly one interactive Explorer session and user'
}
Assert-Equal $true $multipleExplorerContextsRejected 'Multiple interactive Explorer sessions are rejected as ambiguous'

$stoppedWithoutProcesses = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Ready'; '敏敏子倒计时' = 'Missing' }
Assert-Equal $true $stoppedWithoutProcesses.Ready 'Managed application stop accepts non-running tasks with no process'
$taskStillRunning = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Running'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStillRunning.Ready 'Managed application stop waits for a running scheduled task even without a process'
Assert-Equal 'Minmin Countdown' (@($taskStillRunning.RunningTasks) -join ',') 'Managed application stop reports the running task'
$taskStillQueued = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Queued'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStillQueued.Ready 'Managed application stop waits for a queued scheduled task even without a process'
$taskStateUnknown = Test-CountdownManagedApplicationStopped -ProcessIds @() -TaskStates @{ 'Minmin Countdown' = 'Unknown'; '敏敏子倒计时' = 'Ready' }
Assert-Equal $false $taskStateUnknown.Ready 'Managed application stop treats an unknown scheduled-task state as still active'
$stopCommand = Get-Command Stop-CountdownManagedApplication -Module Countdown.InstallTransaction
Assert-Equal $true $stopCommand.Parameters.ContainsKey('FreezeFutureStarts') 'Managed application stop can freeze future scheduled-task starts'
$mutexProbeModulePath = (Join-Path $PSScriptRoot 'Countdown.InstallTransaction.psm1').Replace("'", "''")
$mutexProbeCommand = "Import-Module -Name '$mutexProbeModulePath' -Force -DisableNameChecking; try { `$childMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 1; Exit-CountdownInstallTransactionMutex -Mutex `$childMutex; 'unexpected acquisition' } catch { `$_.Exception.Message }"
$mutexProbeEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
if ($ParentTransactionMutexHeld) {
    # 办公室验收已由同一远端 pwsh 持锁：仅验证子进程确实被该锁排除，避免测试进程自锁。
    $mutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $mutexProbeCommand | Out-String
    Assert-Equal $true ($mutexProbeOutput -match '超时') 'Parent install transaction mutex excludes a child PowerShell process'
}
else {
    $transactionMutex = Enter-CountdownInstallTransactionMutex -TimeoutSeconds 1
    try {
        Assert-Equal $true ($null -ne $transactionMutex) 'Install transaction mutex can be acquired'
        $mutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $mutexProbeCommand | Out-String
        Assert-Equal $true ($mutexProbeOutput -match '超时') 'Install transaction mutex excludes a second PowerShell process'
    }
    finally {
        Exit-CountdownInstallTransactionMutex -Mutex $transactionMutex
    }
}
$settingsMutexEnterCommand = Get-Command Enter-CountdownSettingsWriteMutex -Module Countdown.Settings -ErrorAction SilentlyContinue
$settingsMutexExitCommand = Get-Command Exit-CountdownSettingsWriteMutex -Module Countdown.Settings -ErrorAction SilentlyContinue
Assert-Equal $true ($null -ne $settingsMutexEnterCommand -and $null -ne $settingsMutexExitCommand) 'Settings module exposes a cross-process write mutex'
$settingsModuleSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Raw
Assert-Equal $false ($settingsModuleSource.Contains('[System.Security.Cryptography.SHA256]::HashData') -or $settingsModuleSource.Contains('[Convert]::ToHexString')) 'Settings mutex hashing remains compatible with PowerShell 7.0 and .NET Core 3.1'
if ($null -ne $settingsMutexEnterCommand -and $null -ne $settingsMutexExitCommand) {
    $settingsMutexProbePath = Join-Path $PSScriptRoot ("settings-mutex-probe-" + [guid]::NewGuid().ToString('N') + '.json')
    $settingsMutex = Enter-CountdownSettingsWriteMutex -Path $settingsMutexProbePath -TimeoutSeconds 1
    try {
        $settingsMutexModulePath = (Join-Path $PSScriptRoot 'Countdown.Settings.psm1').Replace("'", "''")
        $escapedSettingsMutexProbePath = $settingsMutexProbePath.Replace("'", "''")
        $settingsMutexProbeCommand = "Import-Module -Name '$settingsMutexModulePath' -Force; try { `$childMutex = Enter-CountdownSettingsWriteMutex -Path '$escapedSettingsMutexProbePath' -TimeoutSeconds 1; Exit-CountdownSettingsWriteMutex -Mutex `$childMutex; 'unexpected acquisition' } catch { `$_.Exception.Message }"
        $settingsMutexProbeOutput = & $mutexProbeEngine -NoLogo -NoProfile -NonInteractive -Command $settingsMutexProbeCommand | Out-String
        Assert-Equal $true ($settingsMutexProbeOutput -match '超时') 'Settings write mutex excludes a second PowerShell process'
    }
    finally {
        Exit-CountdownSettingsWriteMutex -Mutex $settingsMutex
    }
}

$transactionTestRoot = Join-Path $PSScriptRoot ("transaction-test-" + [guid]::NewGuid().ToString('N'))
$recoveryConflictRoot = Join-Path $PSScriptRoot ("recovery-conflict-" + [guid]::NewGuid().ToString('N'))
$transactionInstallPath = Join-Path $transactionTestRoot 'install'
$transactionDesktopPath = Join-Path $transactionTestRoot 'desktop'
$transactionLauncherPath = Join-Path $transactionDesktopPath '敏敏子倒计时.exe'
$transactionLauncherTempPath = "$transactionLauncherPath.new"
$transactionLegacyLauncherPath = Join-Path $transactionDesktopPath '敏敏子倒计时.cmd'
$transactionSuffix = [guid]::NewGuid().ToString('N')
try {
    New-Item -ItemType Directory -Path $transactionInstallPath, $transactionDesktopPath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath '敏敏子倒计时.ps1'), '# old app', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'old launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherTempPath, 'old staged launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLegacyLauncherPath, 'old legacy launcher', (New-Object System.Text.UTF8Encoding($false)))
    $transactionPowerShell = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $preparedTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Prepared $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Prepared $transactionSuffix"
    Assert-Equal $false ([bool] $preparedTransaction.payloadSnapshotReady) 'Prepared transaction does not snapshot mutable files before the old application is quiesced'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"post-prepared":true}', (New-Object System.Text.UTF8Encoding($false)))
    Undo-CountdownInstallTransaction -TransactionPath $preparedTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Prepared $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Prepared $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal '{"post-prepared":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Prepared rollback preserves files not yet snapshotted after quiescence'

    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"quiesced":true}', (New-Object System.Text.UTF8Encoding($false)))
    $rollbackTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Test $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Test $transactionSuffix"
    $rollbackTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $rollbackTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $true ([bool] $rollbackTransaction.payloadSnapshotReady) 'Transaction snapshots mutable files only after the caller has quiesced the old application'
    Assert-Equal $true (Test-Path -LiteralPath $rollbackTransaction.transactionPath) 'Transaction retains backup until external acceptance or rollback'
    $deferredReceiptPath = Join-Path $transactionTestRoot 'deferred-install-receipt.json'
    $deferredReceipt = Write-CountdownDeferredInstallReceipt -Path $deferredReceiptPath -TransactionPath $rollbackTransaction.transactionPath -LaunchToken ([guid]::NewGuid().ToString('N')) -InteractiveSessionId 1
    $readDeferredReceipt = Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath
    Assert-Equal $rollbackTransaction.transactionPath $readDeferredReceipt.transactionPath 'Deferred install receipt preserves rollback transaction path'
    Assert-Equal $deferredReceipt.launchToken $readDeferredReceipt.launchToken 'Deferred install receipt preserves startup token'
    [System.IO.File]::WriteAllText($deferredReceiptPath, '{bad-json', (New-Object System.Text.UTF8Encoding($false)))
    Assert-Equal $true ($null -eq (Read-CountdownDeferredInstallReceipt -Path $deferredReceiptPath)) 'Malformed deferred install receipt is rejected'
    $candidateLauncherSourcePath = Join-Path $transactionTestRoot 'candidate-launcher.exe'
    [System.IO.File]::WriteAllText($candidateLauncherSourcePath, 'new launcher', (New-Object System.Text.UTF8Encoding($false)))
    $candidateLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidateLauncherSourcePath).Hash
    $rollbackTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $rollbackTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal 'launcher-candidate-staged' $rollbackTransaction.phase 'Transaction persists candidate launcher identity before replacement'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"after":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1') -Force
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'new-file.txt'), 'new payload', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'new launcher', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $transactionLauncherTempPath -Force
    Remove-Item -LiteralPath $transactionLegacyLauncherPath -Force
    Undo-CountdownInstallTransaction -TransactionPath $rollbackTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Test $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Test $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Transaction rollback restores the stable post-quiescence install tree'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1')) 'Transaction rollback restores application entry point'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $transactionInstallPath 'new-file.txt')) 'Transaction rollback removes new install files'
    Assert-Equal 'old launcher' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores desktop launcher'
    Assert-Equal 'old staged launcher' ([System.IO.File]::ReadAllText($transactionLauncherTempPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores desktop launcher staging file'
    Assert-Equal 'old legacy launcher' ([System.IO.File]::ReadAllText($transactionLegacyLauncherPath, [System.Text.Encoding]::UTF8)) 'Transaction rollback restores legacy launcher'
    Assert-Equal $false (Test-Path -LiteralPath $rollbackTransaction.transactionPath) 'Transaction rollback clears its journal only after restoration'
    $ownershipTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Ownership $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix"
    $ownershipTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $ownershipTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $ownershipTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $ownershipTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'user replacement', (New-Object System.Text.UTF8Encoding($false)))
    $ownershipRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $ownershipRejected = $_.Exception.Message -match '拒绝恢复 桌面启动器'
    }
    Assert-Equal $true $ownershipRejected 'Rollback refuses to overwrite a user-replaced desktop launcher after a crash'
    Assert-Equal 'user replacement' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Rejected launcher recovery preserves the user replacement'
    Assert-Equal $true (Test-Path -LiteralPath $ownershipTransaction.transactionPath) 'Rejected launcher recovery retains journal evidence'
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"partial-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1') -Force
    $partialRollback = Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $partialRollback.PreservedUnmanagedLaunchers) 'Conflict rollback restores non-launcher state without overwriting a user launcher'
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Conflict rollback restores the stable install tree'
    Assert-Equal $true (Test-Path -LiteralPath (Join-Path $transactionInstallPath '敏敏子倒计时.ps1')) 'Conflict rollback restores the application entry point'
    Assert-Equal 'user replacement' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Conflict rollback continues to preserve the user launcher'
    $partialRollbackJournal = [System.IO.File]::ReadAllText((Join-Path $ownershipTransaction.transactionPath 'journal.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 'launcher-ownership-conflict' $partialRollbackJournal.phase 'Conflict rollback retains a dedicated journal phase for safe later recovery'
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $transactionLauncherPath -Force
    Undo-CountdownInstallTransaction -TransactionPath $ownershipTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Ownership $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Ownership $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    $recoveryConflictInstallPath = Join-Path $recoveryConflictRoot 'install'
    $recoveryConflictDesktopPath = Join-Path $recoveryConflictRoot 'desktop'
    $recoveryConflictLauncherPath = Join-Path $recoveryConflictDesktopPath '敏敏子倒计时.exe'
    $recoveryConflictLauncherTempPath = "$recoveryConflictLauncherPath.new"
    $recoveryConflictLegacyLauncherPath = Join-Path $recoveryConflictDesktopPath '敏敏子倒计时.cmd'
    New-Item -ItemType Directory -Path $recoveryConflictInstallPath, $recoveryConflictDesktopPath -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath 'settings.json'), '{"isolated-before":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath '敏敏子倒计时.ps1'), '# isolated old app', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherPath, 'isolated old launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherTempPath, 'isolated old staged launcher', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLegacyLauncherPath, 'isolated old legacy launcher', (New-Object System.Text.UTF8Encoding($false)))
    $recoveryConflictTransaction = Start-CountdownInstallTransaction -InstallPath $recoveryConflictInstallPath -LauncherPath $recoveryConflictLauncherPath -LegacyLauncherPath $recoveryConflictLegacyLauncherPath -TaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix"
    $recoveryConflictTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $recoveryConflictTransaction.transactionPath -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    $recoveryConflictTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $recoveryConflictTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
    [System.IO.File]::WriteAllText((Join-Path $recoveryConflictInstallPath 'settings.json'), '{"isolated-new":true}', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($recoveryConflictLauncherPath, 'isolated user replacement', (New-Object System.Text.UTF8Encoding($false)))
    $recoveryConflictPartialRollback = Undo-CountdownInstallTransaction -TransactionPath $recoveryConflictTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $recoveryConflictInstallPath -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $recoveryConflictPartialRollback.PreservedUnmanagedLaunchers) 'Isolated conflict setup preserves its user launcher during partial rollback'
    if ($ParentTransactionMutexHeld) {
        # 办公室验收的父进程已持有全局 mutex；子进程只能验证它被排除，不能调用 Recover 并等待同一把锁超时。
        Assert-Equal $true $true 'Parent-held transaction mutex skips recovery execution that requires the parent lock'
    }
    else {
        $recoveryConflictStatus = Recover-CountdownInstallTransactions -InstallPath $recoveryConflictInstallPath -PowerShell7Path $transactionPowerShell -ExpectedTaskName "Minmin Countdown Recovery Conflict $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Recovery Conflict $transactionSuffix" -ExpectedLauncherPath $recoveryConflictLauncherPath -ExpectedLegacyLauncherPath $recoveryConflictLegacyLauncherPath -ExpectedLauncherTempPath $recoveryConflictLauncherTempPath
        Assert-Equal 0 @($recoveryConflictStatus.RecoveredTransactionPaths).Count 'Recovery does not falsely report a user-launcher conflict as complete'
        Assert-Equal 1 @($recoveryConflictStatus.UnresolvedLauncherOwnershipConflicts).Count 'Recovery returns a structured unresolved launcher conflict'
        Assert-Equal 'isolated user replacement' ([System.IO.File]::ReadAllText($recoveryConflictLauncherPath, [System.Text.Encoding]::UTF8)) 'Recovery status probing does not overwrite the isolated user launcher'
    }
    $strictBooleanTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Strict Boolean $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Strict Boolean $transactionSuffix"
    $strictBooleanJournalPath = Join-Path $strictBooleanTransaction.transactionPath 'journal.json'
    $strictBooleanJournal = [System.IO.File]::ReadAllText($strictBooleanJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $strictBooleanJournal.payloadSnapshotReady = 'false'
    [System.IO.File]::WriteAllText($strictBooleanJournalPath, ($strictBooleanJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $strictBooleanRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $strictBooleanTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Strict Boolean $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Strict Boolean $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $strictBooleanRejected = $_.Exception.Message -match '严格布尔字段 payloadSnapshotReady'
    }
    Assert-Equal $true $strictBooleanRejected 'Rollback rejects a string payloadSnapshotReady before deleting any files'
    Assert-Equal '{"quiesced":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Malformed prepared journal leaves current install tree intact'
    $missingBackupTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Missing Backup $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix"
    $missingBackupTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $missingBackupTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Missing Backup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"must-not-delete":true}', (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $missingBackupTransaction.backupInstallPath -Recurse -Force
    $missingBackupRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $missingBackupTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Missing Backup $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Missing Backup $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $missingBackupRejected = $_.Exception.Message -match '缺少安装目录备份'
    }
    Assert-Equal $true $missingBackupRejected 'Rollback verifies install backup before deleting the current install tree'
    Assert-Equal '{"must-not-delete":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Missing backup leaves current install tree intact'
    $invalidTaskXmlTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Invalid XML $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Invalid XML $transactionSuffix"
    $invalidTaskXmlJournalPath = Join-Path $invalidTaskXmlTransaction.transactionPath 'journal.json'
    $invalidTaskXmlJournal = [System.IO.File]::ReadAllText($invalidTaskXmlJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $invalidTaskXmlJournal.currentTask.Exists = $true
    $invalidTaskXmlJournal.currentTask.State = 'Ready'
    [System.IO.File]::WriteAllText([string] $invalidTaskXmlJournal.currentTask.XmlPath, '<Task>', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($invalidTaskXmlJournalPath, ($invalidTaskXmlJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $transactionInstallPath 'settings.json'), '{"task-xml-must-not-delete":true}', (New-Object System.Text.UTF8Encoding($false)))
    $invalidTaskXmlRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $invalidTaskXmlTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Invalid XML $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Invalid XML $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $invalidTaskXmlRejected = $_.Exception.Message -match 'currentTask XML 无法解析'
    }
    Assert-Equal $true $invalidTaskXmlRejected 'Rollback rejects a truncated task XML before any destructive action'
    Assert-Equal '{"task-xml-must-not-delete":true}' ([System.IO.File]::ReadAllText((Join-Path $transactionInstallPath 'settings.json'), [System.Text.Encoding]::UTF8)) 'Invalid task XML leaves the current install tree intact'
    Assert-Equal $true (Test-Path -LiteralPath $invalidTaskXmlTransaction.transactionPath) 'Invalid task XML keeps journal evidence for manual repair'
    $identityTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Identity $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Identity $transactionSuffix"
    $identityRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName 'unexpected task' -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $identityRejected = $_.Exception.Message -match 'taskName 与当前安装任务不一致'
    }
    Assert-Equal $true $identityRejected 'Transaction rollback binds its journal to the expected task identities'
    $launcherIdentityRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Identity $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath (Join-Path $transactionDesktopPath 'other-launcher.exe') -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $launcherIdentityRejected = $_.Exception.Message -match 'launcherPath 与当前安装目标不一致'
    }
    Assert-Equal $true $launcherIdentityRejected 'Transaction rollback binds its journal to the expected launcher paths'
    Undo-CountdownInstallTransaction -TransactionPath $identityTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Identity $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Identity $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -RestoreWindowsTimeAction { param($snapshot) }
    $commitTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Commit $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Commit $transactionSuffix"
    $commitTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $commitTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $commitTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Copy-Item -LiteralPath $candidateLauncherSourcePath -Destination $transactionLauncherPath -Force
    Complete-CountdownInstallTransaction -TransactionPath $commitTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    Assert-Equal $false (Test-Path -LiteralPath $commitTransaction.transactionPath) 'Transaction commit clears deferred backup only after acceptance'
    $commitReplacementTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Commit Replacement $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix"
    $commitReplacementTransaction = Capture-CountdownInstallPayloadSnapshot -TransactionPath $commitReplacementTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    $commitReplacementTransaction = Set-CountdownInstallTransactionCandidateLauncherHash -TransactionPath $commitReplacementTransaction.transactionPath -CandidateLauncherSha256 $candidateLauncherHash -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    [System.IO.File]::WriteAllText($transactionLauncherPath, 'user replacement before commit', (New-Object System.Text.UTF8Encoding($false)))
    $commitReplacementRejected = $false
    try {
        Complete-CountdownInstallTransaction -TransactionPath $commitReplacementTransaction.transactionPath -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath
    }
    catch {
        $commitReplacementRejected = $_.Exception.Message -match '候选桌面启动器在提交前已被替换'
    }
    Assert-Equal $true $commitReplacementRejected 'Transaction commit rejects a launcher replaced after acceptance'
    Assert-Equal $true (Test-Path -LiteralPath $commitReplacementTransaction.transactionPath) 'Rejected transaction commit retains rollback evidence'
    $commitReplacementRollback = Undo-CountdownInstallTransaction -TransactionPath $commitReplacementTransaction.transactionPath -PowerShell7Path $transactionPowerShell -ExpectedInstallPath $transactionInstallPath -ExpectedTaskName "Minmin Countdown Commit Replacement $transactionSuffix" -ExpectedLegacyTaskName "敏敏子倒计时 Commit Replacement $transactionSuffix" -ExpectedLauncherPath $transactionLauncherPath -ExpectedLegacyLauncherPath $transactionLegacyLauncherPath -ExpectedLauncherTempPath $transactionLauncherTempPath -PreserveUnmanagedLaunchers -RestoreWindowsTimeAction { param($snapshot) }
    Assert-Equal $true ([bool] $commitReplacementRollback.PreservedUnmanagedLaunchers) 'Rejected transaction commit can preserve the user-replaced launcher during rollback'
    Assert-Equal 'user replacement before commit' ([System.IO.File]::ReadAllText($transactionLauncherPath, [System.Text.Encoding]::UTF8)) 'Rejected transaction commit does not overwrite the user-replaced launcher'
    $tamperedTransaction = Start-CountdownInstallTransaction -InstallPath $transactionInstallPath -LauncherPath $transactionLauncherPath -LegacyLauncherPath $transactionLegacyLauncherPath -TaskName "Minmin Countdown Tamper $transactionSuffix" -LegacyTaskName "敏敏子倒计时 Tamper $transactionSuffix"
    $tamperedJournalPath = Join-Path $tamperedTransaction.transactionPath 'journal.json'
    $tamperedJournal = [System.IO.File]::ReadAllText($tamperedJournalPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $tamperedJournal.backupLauncherPath = 'D:\outside-transaction-backup.exe'
    [System.IO.File]::WriteAllText($tamperedJournalPath, ($tamperedJournal | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    $tamperedJournalRejected = $false
    try {
        Undo-CountdownInstallTransaction -TransactionPath $tamperedTransaction.transactionPath -PowerShell7Path (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source) -RestoreWindowsTimeAction { param($snapshot) }
    }
    catch {
        $tamperedJournalRejected = $_.Exception.Message -match '不在事务目录内'
    }
    Assert-Equal $true $tamperedJournalRejected 'Transaction rollback rejects backup paths outside its journal directory'
}
finally {
    Remove-Item -LiteralPath $transactionTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $recoveryConflictRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$installerValidationEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$installerValidationJson = & $installerValidationEngine -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Install-Countdown.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) { throw "Installer validation failed: $installerValidationJson" }
$installerValidation = $installerValidationJson | ConvertFrom-Json
Assert-Equal $true $installerValidation.Valid 'Installer package validation succeeds'
Assert-Equal 21 $installerValidation.VerifiedFiles 'Installer manifest includes runtime, application host, transaction, tag modules, three official character backgrounds, and six floating plush assets'
Assert-Equal 'sanrio-official-mymelody.png,sanrio-official-cinnamoroll.png,sanrio-official-kuromi.png' (@($installerValidation.OfficialCharacterAssets) -join ',') 'Installer includes only the requested official Sanrio character backgrounds'
Assert-Equal 'assets\floating-plush\floating-plush-mymelody-front.png,assets\floating-plush\floating-plush-mymelody-pose.png,assets\floating-plush\floating-plush-cinnamoroll-front.png,assets\floating-plush\floating-plush-cinnamoroll-pose.png,assets\floating-plush\floating-plush-kuromi-front.png,assets\floating-plush\floating-plush-kuromi-pose.png' (@($installerValidation.FloatingPlushAssets) -join ',') 'Installer includes all six floating plush assets'
Assert-Equal $true $installerValidation.Transactional 'Installer exposes transactional install boundary'
Assert-Equal $true $installerValidation.DeferredCommitSupported 'Installer exposes deferred commit for office acceptance'
$installerCommand = Get-Command (Join-Path $PSScriptRoot 'Install-Countdown.ps1')
Assert-Equal $false $installerCommand.Parameters.ContainsKey('TransactionMutexHeld') 'Installer does not expose a lock-bypass switch'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ParentTransactionMutexHeld') 'Installer exposes a parent-lock test context only for deferred office acceptance'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedInteractiveUser') 'Office installer validates the expected interactive user'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedInteractiveSessionId') 'Office installer validates the expected interactive session'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedDesktopPath') 'Office installer validates the expected desktop path'
$recoveryCommand = Get-Command Recover-CountdownInstallTransactions -Module Countdown.InstallTransaction
Assert-Equal $false $recoveryCommand.Parameters.ContainsKey('TransactionMutexHeld') 'Recovery does not expose a lock-bypass switch'
$candidateCommand = Get-Command Set-CountdownInstallTransactionCandidateLauncherHash -Module Countdown.InstallTransaction
Assert-Equal $true $candidateCommand.Parameters.ContainsKey('CandidateLauncherSha256') 'Transaction persists a candidate launcher hash before replacement'
$installerSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Install-Countdown.ps1'), [System.Text.Encoding]::UTF8)
$timeSourceWaitDefinitionIndex = $installerSource.IndexOf('function Wait-CountdownNetworkTimeSource')
$timeSourceWaitCallIndex = $installerSource.IndexOf('$timeSource = Wait-CountdownNetworkTimeSource')
Assert-Equal $true ($timeSourceWaitDefinitionIndex -ge 0 -and $timeSourceWaitCallIndex -gt $timeSourceWaitDefinitionIndex) 'Installer waits with a bounded network-time source probe'
Assert-Equal $true ($installerSource -notmatch 'Start-Sleep -Seconds 3') 'Installer does not rely on a fixed three-second time-service sleep'
$payloadCaptureIndex = $installerSource.IndexOf('$transaction = Capture-CountdownInstallPayloadSnapshot')
$candidatePathIndex = $installerSource.IndexOf("`$candidateLauncherPath = Join-Path `$transaction.transactionPath 'candidate-launcher.exe'")
$candidatePersistIndex = $installerSource.IndexOf('$transaction = Set-CountdownInstallTransactionCandidateLauncherHash')
$preparedLauncherGuardIndex = $installerSource.IndexOf('changed while installation was preparing')
$candidateHashCheckIndex = $installerSource.IndexOf('Transaction launcher candidate changed before desktop replacement.')
$launcherMoveIndex = $installerSource.IndexOf('Move-Item -LiteralPath $candidateLauncherPath -Destination $launcherPath -Force')
$installedLauncherHashCheckIndex = $installerSource.IndexOf('Desktop launcher hash mismatch after replacement.')
Assert-Equal $true ($payloadCaptureIndex -ge 0 -and $candidatePathIndex -gt $payloadCaptureIndex -and $candidatePersistIndex -gt $candidatePathIndex -and $preparedLauncherGuardIndex -gt $candidatePersistIndex -and $candidateHashCheckIndex -gt $preparedLauncherGuardIndex -and $launcherMoveIndex -gt $candidateHashCheckIndex -and $installedLauncherHashCheckIndex -gt $launcherMoveIndex) 'Installer keeps candidate under the transaction and verifies both candidate and replaced launcher immediately around the move'
$installerStartupRemovalIndex = $installerSource.LastIndexOf('Remove-CountdownStartupRequest')
$installerCommitContextCheckIndex = $installerSource.LastIndexOf('Assert-CountdownInstallInteractiveContextStable -ExpectedContext $interactiveContext')
$installerCompleteIndex = $installerSource.LastIndexOf('Complete-CountdownInstallTransaction')
Assert-Equal $true ($installerStartupRemovalIndex -ge 0 -and $installerCommitContextCheckIndex -gt $installerStartupRemovalIndex -and $installerCompleteIndex -gt $installerCommitContextCheckIndex) 'Installer rechecks the interactive desktop context immediately before committing'
$deploySource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Deploy-OfficeCountdown.ps1'), [System.Text.Encoding]::UTF8)
Assert-Equal $true ($deploySource.Contains('-PreserveUnmanagedLaunchers')) 'Office acceptance rollback preserves user-replaced launcher files while restoring system state'
Assert-Equal $true ($deploySource.Contains('UnresolvedLauncherOwnershipConflicts')) 'Office acceptance reports an unresolved pre-receipt launcher conflict'
Assert-Equal $true ($deploySource.Contains('已恢复应用、任务和时间服务')) 'Office acceptance reports a partial conflict recovery accurately'
$deployStartupRemovalIndex = $deploySource.LastIndexOf('Remove-CountdownStartupRequest')
$deployCommitContextCheckIndex = $deploySource.IndexOf('办公室验收提交前交互用户、会话或桌面路径已变化；拒绝提交事务。')
$deployCompleteIndex = $deploySource.LastIndexOf('Complete-CountdownInstallTransaction')
Assert-Equal $true ($deployStartupRemovalIndex -ge 0 -and $deployCommitContextCheckIndex -gt $deployStartupRemovalIndex -and $deployCompleteIndex -gt $deployCommitContextCheckIndex) 'Office acceptance rechecks the interactive desktop context immediately before committing'
$deployInstallerIndex = $deploySource.IndexOf('`$installerOutput = &')
$deploySettingsSnapshotIndex = $deploySource.IndexOf('`$settingsSnapshotPath = Join-Path `$rollbackTransactionPath ''install-backup\settings.json''')
$deploySettingsMutexIndex = $deploySource.IndexOf('`$settingsWriteMutex = Enter-CountdownSettingsWriteMutex')
$deploySettingsAfterIndex = $deploySource.IndexOf('`$settingsJsonAfter = if (Test-Path -LiteralPath `$settingsPath)')
$deploySettingsTransitionIndex = $deploySource.IndexOf('Test-CountdownSettingsDeploymentTransition')
$deploySettingsMutexReleaseIndex = $deploySource.LastIndexOf('Exit-CountdownSettingsWriteMutex')
Assert-Equal $true ($deploySettingsSnapshotIndex -gt $deployInstallerIndex -and $deploySettingsTransitionIndex -gt $deploySettingsSnapshotIndex) 'Office acceptance compares settings against the post-freeze transaction snapshot'
Assert-Equal $false ($deploySource.Contains('`$settingsJsonBefore = if (Test-Path -LiteralPath `$settingsPath)')) 'Office acceptance does not capture mutable settings before the installer freezes the old application'
Assert-Equal $true ($deploySettingsMutexIndex -gt $deploySettingsSnapshotIndex -and $deploySettingsAfterIndex -gt $deploySettingsMutexIndex -and $deploySettingsTransitionIndex -gt $deploySettingsAfterIndex -and $deployCompleteIndex -gt $deploySettingsTransitionIndex -and $deploySettingsMutexReleaseIndex -gt $deployCompleteIndex) 'Office acceptance holds the settings write mutex from the final read through transaction commit'

$clearTestPath = Join-Path $PSScriptRoot 'clear-test.settings.json'
[System.IO.File]::WriteAllText($clearTestPath, '{}')
[System.IO.File]::WriteAllText("$clearTestPath.tmp", '{}')
$removed = Remove-CountdownSettings -SettingsPath $clearTestPath
Assert-Equal $true $removed 'Settings clear reports removal'
Assert-Equal $false (Test-Path -LiteralPath $clearTestPath) 'Settings file removed'
Assert-Equal $false (Test-Path -LiteralPath "$clearTestPath.tmp") 'Temporary settings file removed'

$legacyV3Settings = [pscustomobject]@{
    schemaVersion = 3
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Minute'
    monthlySalary = 12345
    workStart = '08:30'
    workEnd = '17:30'
    countdowns = @([pscustomobject]@{ id = 'legacy'; name = '旧版倒计时'; targetUtc = '2099-01-01T00:00:00Z' })
}
$loadedLegacyV3 = ConvertFrom-CountdownSettingsObject -Settings $legacyV3Settings
Assert-Equal $true $loadedLegacyV3.Migrated 'Schema v3 settings migrate'
Assert-Equal 1 $loadedLegacyV3.Records.Count 'Schema v3 record count'
Assert-Equal 0 $loadedLegacyV3.Notes.Count 'Schema v3 creates empty notes'
Assert-Equal 0 $loadedLegacyV3.Tags.Count 'Schema v3 creates an empty tag library'
Assert-Equal 0 @($loadedLegacyV3.Records[0].tagIds).Count 'Schema v3 creates untagged countdowns'
Assert-Equal 'Minute' $loadedLegacyV3.Preferences.Precision 'Schema v3 preserves precision'
$canonicalLegacyV3 = ConvertTo-CountdownSettingsJson -State $loadedLegacyV3
$canonicalLegacyV3Object = $canonicalLegacyV3 | ConvertFrom-Json
Assert-Equal 7 $canonicalLegacyV3Object.schemaVersion 'Canonical settings schema'
Assert-Equal 0 @($canonicalLegacyV3Object.tags).Count 'Canonical settings tags'
Assert-Equal 0 @($canonicalLegacyV3Object.notes).Count 'Canonical settings notes'
Assert-Equal 0 @($canonicalLegacyV3Object.countdowns[0].tagIds).Count 'Canonical settings countdown tag IDs'
$settingsCandidate = Copy-CountdownSettingsState -State $loadedLegacyV3
$settingsCandidate.Records[0].name = '候选倒计时'
$settingsCandidate.Preferences.MonthlySalary = 1
Assert-Equal '旧版倒计时' $loadedLegacyV3.Records[0].name 'Settings candidate does not mutate source record'
Assert-Equal 12345 $loadedLegacyV3.Preferences.MonthlySalary 'Settings candidate does not mutate source preferences'
$candidateSaveFailureObserved = $false
try {
    $unwritableCandidatePath = Join-Path (Join-Path $PSScriptRoot ([guid]::NewGuid().ToString('N'))) 'settings.json'
    Save-CountdownSettings -Path $unwritableCandidatePath -State $settingsCandidate
}
catch {
    $candidateSaveFailureObserved = $true
}
Assert-Equal $true $candidateSaveFailureObserved 'Settings candidate persistence failure is observable'
Assert-Equal '旧版倒计时' $loadedLegacyV3.Records[0].name 'Settings candidate persistence failure preserves source record'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'same' -AfterHash 'same' -BeforeJson $null -AfterJson $null) 'Unchanged settings are accepted for deployment'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV3Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV3) 'Strict schema v3 to v7 migration is accepted for deployment'
$legacyV1Settings = [pscustomobject]@{ schemaVersion = 1; timeZoneId = 'China Standard Time'; targetUtc = '2099-01-01T00:00:00Z' }
$canonicalLegacyV1 = ConvertTo-CountdownSettingsJson -State (ConvertFrom-CountdownSettingsObject -Settings $legacyV1Settings)
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV1Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV1) 'Schema v1 migration remains rejected for deployment'
$settingsModuleTestPath = Join-Path $PSScriptRoot 'settings-module-test.json'
try {
    Save-CountdownSettings -Path $settingsModuleTestPath -State $loadedLegacyV3
    $savedSettings = [System.IO.File]::ReadAllText($settingsModuleTestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Equal 7 $savedSettings.schemaVersion 'Settings module writes canonical schema'
    Assert-Equal $false (Test-Path -LiteralPath "$settingsModuleTestPath.tmp") 'Settings module cleans temporary file'
}
finally {
    Remove-Item -LiteralPath $settingsModuleTestPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$settingsModuleTestPath.tmp" -Force -ErrorAction SilentlyContinue
}

$legacyV4Settings = [pscustomobject]@{
    schemaVersion = 4
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Second'
    monthlySalary = 23000
    workStart = '09:00'
    workEnd = '18:00'
    countdowns = @([pscustomobject]@{ id = 'legacy-v4-countdown'; name = '旧版带便签倒计时'; targetUtc = '2099-02-01T00:00:00Z' })
    notes = @([pscustomobject]@{ id = 'legacy-v4-note'; text = '保留的旧版便签'; isCompleted = $true })
}
$loadedLegacyV4 = ConvertFrom-CountdownSettingsObject -Settings $legacyV4Settings
Assert-Equal $true $loadedLegacyV4.Migrated 'Schema v4 settings migrate'
Assert-Equal 0 $loadedLegacyV4.Tags.Count 'Schema v4 creates an empty tag library'
Assert-Equal 0 @($loadedLegacyV4.Records[0].tagIds).Count 'Schema v4 creates untagged countdowns'
Assert-Equal 0 @($loadedLegacyV4.Notes[0].tagIds).Count 'Schema v4 creates untagged notes'
Assert-Equal '保留的旧版便签' $loadedLegacyV4.Notes[0].text 'Schema v4 preserves notes'
Assert-Equal $null $loadedLegacyV4.Notes[0].date 'Schema v4 does not invent a note date'
$canonicalLegacyV4 = ConvertTo-CountdownSettingsJson -State $loadedLegacyV4
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV4Settings | ConvertTo-Json -Depth 4) -AfterJson $canonicalLegacyV4) 'Strict schema v4 to v7 migration is accepted for deployment'
$invalidV4Transition = $canonicalLegacyV4 | ConvertFrom-Json
$invalidV4Transition.notes[0].text = '被篡改的便签'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($legacyV4Settings | ConvertTo-Json -Depth 4) -AfterJson ($invalidV4Transition | ConvertTo-Json -Depth 5)) 'Schema v4 migration rejects changed notes'

$v5Settings = [pscustomobject]@{
    schemaVersion = 5
    timeZoneId = 'China Standard Time'
    displayPrecision = 'Hour'
    monthlySalary = 8000
    workStart = '08:00'
    workEnd = '17:00'
    tags = @(
        [pscustomobject]@{ id = 'work'; name = '工作'; color = '#6B8E23' },
        [pscustomobject]@{ id = 'home'; name = '家庭'; color = $null }
    )
    countdowns = @([pscustomobject]@{ id = 'v5-countdown'; name = '带标签倒计时'; targetUtc = '2099-03-01T00:00:00Z'; tagIds = @(' WORK ') })
    notes = @([pscustomobject]@{ id = 'v5-note'; text = '带标签便签'; isCompleted = $false; tagIds = @('home') })
}
$loadedV5 = ConvertFrom-CountdownSettingsObject -Settings $v5Settings
Assert-Equal $true $loadedV5.Migrated 'Schema v5 settings migrate to add note dates'
Assert-Equal 2 $loadedV5.Tags.Count 'Schema v5 loads tags'
Assert-Equal 'work' $loadedV5.Records[0].tagIds[0] 'Schema v5 canonicalizes countdown tag IDs'
Assert-Equal 'home' $loadedV5.Notes[0].tagIds[0] 'Schema v5 preserves note tag IDs'
Assert-Equal $null $loadedV5.Notes[0].date 'Schema v5 migration does not invent a note date'
$v5Copy = Copy-CountdownSettingsState -State $loadedV5
$v5Copy.Tags[0].name = '已改名'
$v5Copy.Records[0].tagIds[0] = 'home'
$v5Copy.Notes[0].tagIds[0] = 'work'
Assert-Equal '工作' $loadedV5.Tags[0].name 'Settings state copy does not mutate source tags'
Assert-Equal 'work' $loadedV5.Records[0].tagIds[0] 'Settings state copy does not mutate source countdown tags'
Assert-Equal 'home' $loadedV5.Notes[0].tagIds[0] 'Settings state copy does not mutate source note tags'
$canonicalFromV5 = ConvertTo-CountdownSettingsJson -State $loadedV5 | ConvertFrom-Json
Assert-Equal 7 $canonicalFromV5.schemaVersion 'Schema v5 migrates to canonical schema v7'
Assert-Equal '#6B8E23' $canonicalFromV5.tags[0].color 'Schema v7 serializes tag color'
Assert-Equal $true ($null -eq $canonicalFromV5.tags[1].PSObject.Properties['color']) 'Schema v7 omits an unspecified tag color'
Assert-Equal 'work' $canonicalFromV5.countdowns[0].tagIds[0] 'Schema v7 serializes countdown tag IDs'
Assert-Equal 'home' $canonicalFromV5.notes[0].tagIds[0] 'Schema v7 serializes note tag IDs'
Assert-Equal $null $canonicalFromV5.notes[0].date 'Schema v7 preserves an unknown legacy note date as null'
Assert-Equal $null $canonicalFromV5.notes[0].reminderIntervalMinutes 'Schema v7 disables reminders for migrated notes'
Assert-Equal $null $canonicalFromV5.notes[0].reminderStartUtc 'Schema v7 does not invent a reminder start for migrated notes'
$loadedNullDateV7 = ConvertFrom-CountdownSettingsObject -Settings $canonicalFromV5
Assert-Equal $null $loadedNullDateV7.Notes[0].date 'Schema v7 accepts an explicitly unknown note date'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v5Settings | ConvertTo-Json -Depth 5) -AfterJson ($canonicalFromV5 | ConvertTo-Json -Depth 5)) 'Strict schema v5 to v7 migration is accepted for deployment'
$invalidV5Transition = $canonicalFromV5 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$invalidV5Transition.notes[0].date = '2026-07-18'
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v5Settings | ConvertTo-Json -Depth 5) -AfterJson ($invalidV5Transition | ConvertTo-Json -Depth 5)) 'Schema v5 migration rejects an invented note date'

$v6Settings = $canonicalFromV5 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$v6Settings.schemaVersion = 6
$v6Settings.notes[0].PSObject.Properties.Remove('reminderIntervalMinutes')
$v6Settings.notes[0].PSObject.Properties.Remove('reminderStartUtc')
$v6Settings.notes[0].date = '2026-07-18'
$loadedV6 = ConvertFrom-CountdownSettingsObject -Settings $v6Settings
Assert-Equal $true $loadedV6.Migrated 'Schema v6 settings migrate to add reminder fields'
Assert-Equal '2026-07-18' $loadedV6.Notes[0].date 'Schema v6 loads an exact note date'
Assert-Equal $null $loadedV6.Notes[0].reminderIntervalMinutes 'Schema v6 migration disables reminders'
$canonicalV6 = ConvertTo-CountdownSettingsJson -State $loadedV6 | ConvertFrom-Json
Assert-Equal 7 $canonicalV6.schemaVersion 'Schema v6 migrates to canonical schema v7'
Assert-Equal '2026-07-18' $canonicalV6.notes[0].date 'Schema v7 preserves an exact note date'
Assert-Equal $null $canonicalV6.notes[0].reminderIntervalMinutes 'Schema v7 persists a disabled reminder explicitly'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'before' -AfterHash 'after' -BeforeJson ($v6Settings | ConvertTo-Json -Depth 5) -AfterJson ($canonicalV6 | ConvertTo-Json -Depth 5)) 'Strict schema v6 to v7 migration is accepted for deployment'

$v7Settings = $canonicalV6 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$v7Settings.schemaVersion = 7
$v7Settings.notes[0] | Add-Member -NotePropertyName reminderIntervalMinutes -NotePropertyValue 30 -Force
$v7Settings.notes[0] | Add-Member -NotePropertyName reminderStartUtc -NotePropertyValue '2026-07-19T00:00:00.0000000+00:00' -Force
$loadedV7 = ConvertFrom-CountdownSettingsObject -Settings $v7Settings
Assert-Equal $false $loadedV7.Migrated 'Schema v7 settings do not migrate'
Assert-Equal 30 $loadedV7.Notes[0].reminderIntervalMinutes 'Schema v7 loads a reminder interval'
Assert-Equal ([DateTimeOffset]'2026-07-19T00:00:00Z') $loadedV7.Notes[0].reminderStartUtc 'Schema v7 loads a UTC reminder start'
$canonicalV7 = ConvertTo-CountdownSettingsJson -State $loadedV7 | ConvertFrom-Json
Assert-Equal 30 $canonicalV7.notes[0].reminderIntervalMinutes 'Schema v7 round-trips a reminder interval'
Assert-Equal ([DateTimeOffset]'2026-07-19T00:00:00Z') ([DateTimeOffset] $canonicalV7.notes[0].reminderStartUtc).ToUniversalTime() 'Schema v7 round-trips a canonical UTC reminder start'
$preFreezeV7 = $canonicalV7 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$preFreezeV7.monthlySalary = 100
$postFreezeV7 = $canonicalV7 | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$postFreezeV7.monthlySalary = 200
Assert-Equal $false (Test-CountdownSettingsDeploymentTransition -BeforeHash 'pre-freeze' -AfterHash 'post-freeze' -BeforeJson ($preFreezeV7 | ConvertTo-Json -Depth 5) -AfterJson ($postFreezeV7 | ConvertTo-Json -Depth 5)) 'Schema v7 changes after the selected deployment baseline are rejected'
Assert-Equal $true (Test-CountdownSettingsDeploymentTransition -BeforeHash 'post-freeze' -AfterHash 'post-freeze' -BeforeJson ($postFreezeV7 | ConvertTo-Json -Depth 5) -AfterJson ($postFreezeV7 | ConvertTo-Json -Depth 5)) 'A pre-freeze save preserved in the transaction snapshot remains deployable'
$reloadedCanonicalV7 = ConvertFrom-CountdownSettingsObject -Settings $canonicalV7
Assert-Equal 2 $reloadedCanonicalV7.Tags.Count 'Schema v7 reloads tags without optional colors'
Assert-Equal $null $reloadedCanonicalV7.Tags[1].color 'Schema v7 reload preserves an unspecified tag color'

$missingV6NoteDateRejected = $false
try {
    $missingV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $missingV6NoteDate.notes[0].PSObject.Properties.Remove('date')
    ConvertFrom-CountdownSettingsObject -Settings $missingV6NoteDate | Out-Null
}
catch {
    $missingV6NoteDateRejected = $true
}
Assert-Equal $true $missingV6NoteDateRejected 'Schema v6 requires a note date field'
$invalidV6NoteDateRejected = $false
try {
    $invalidV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidV6NoteDate.notes[0].date = '2026-7-18'
    ConvertFrom-CountdownSettingsObject -Settings $invalidV6NoteDate | Out-Null
}
catch {
    $invalidV6NoteDateRejected = $true
}
Assert-Equal $true $invalidV6NoteDateRejected 'Schema v6 rejects a non-canonical note date'
$blankV6NoteDateRejected = $false
try {
    $blankV6NoteDate = $v6Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $blankV6NoteDate.notes[0].date = '   '
    ConvertFrom-CountdownSettingsObject -Settings $blankV6NoteDate | Out-Null
}
catch {
    $blankV6NoteDateRejected = $true
}
Assert-Equal $true $blankV6NoteDateRejected 'Schema v6 rejects a blank note date'

$missingV7ReminderFieldsRejected = $false
try {
    $missingV7ReminderFields = $v7Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $missingV7ReminderFields.notes[0].PSObject.Properties.Remove('reminderIntervalMinutes')
    ConvertFrom-CountdownSettingsObject -Settings $missingV7ReminderFields | Out-Null
}
catch {
    $missingV7ReminderFieldsRejected = $true
}
Assert-Equal $true $missingV7ReminderFieldsRejected 'Schema v7 requires reminder fields'
$invalidV7ReminderTypeRejected = $false
try {
    $invalidV7ReminderType = $v7Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidV7ReminderType.notes[0].reminderIntervalMinutes = '30'
    ConvertFrom-CountdownSettingsObject -Settings $invalidV7ReminderType | Out-Null
}
catch {
    $invalidV7ReminderTypeRejected = $true
}
Assert-Equal $true $invalidV7ReminderTypeRejected 'Schema v7 rejects a string reminder interval'

$missingV5TagsRejected = $false
try {
    $missingV5Tags = $v5Settings | Select-Object * -ExcludeProperty tags
    ConvertFrom-CountdownSettingsObject -Settings $missingV5Tags | Out-Null
}
catch {
    $missingV5TagsRejected = $true
}
Assert-Equal $true $missingV5TagsRejected 'Schema v5 requires a tag library'
$duplicateTagIdRejected = $false
try {
    $duplicateTagIdSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateTagIdSettings.tags += [pscustomobject]@{ id = 'WORK'; name = '第二个工作'; color = $null }
    ConvertFrom-CountdownSettingsObject -Settings $duplicateTagIdSettings | Out-Null
}
catch {
    $duplicateTagIdRejected = $true
}
Assert-Equal $true $duplicateTagIdRejected 'Schema v5 rejects duplicate tag IDs without case sensitivity'
$duplicateTagNameRejected = $false
try {
    $duplicateTagNameSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateTagNameSettings.tags += [pscustomobject]@{ id = 'office'; name = '工作'; color = $null }
    ConvertFrom-CountdownSettingsObject -Settings $duplicateTagNameSettings | Out-Null
}
catch {
    $duplicateTagNameRejected = $true
}
Assert-Equal $true $duplicateTagNameRejected 'Schema v5 rejects duplicate tag names without case sensitivity'
$danglingCountdownTagRejected = $false
try {
    $danglingCountdownTagSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $danglingCountdownTagSettings.countdowns[0].tagIds = @('missing')
    ConvertFrom-CountdownSettingsObject -Settings $danglingCountdownTagSettings | Out-Null
}
catch {
    $danglingCountdownTagRejected = $true
}
Assert-Equal $true $danglingCountdownTagRejected 'Schema v5 rejects dangling countdown tag IDs'
$duplicateNoteTagRejected = $false
try {
    $duplicateNoteTagSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $duplicateNoteTagSettings.notes[0].tagIds = @('home', 'HOME')
    ConvertFrom-CountdownSettingsObject -Settings $duplicateNoteTagSettings | Out-Null
}
catch {
    $duplicateNoteTagRejected = $true
}
Assert-Equal $true $duplicateNoteTagRejected 'Schema v5 rejects duplicate note tag IDs without case sensitivity'
$invalidTagColorSettingsRejected = $false
try {
    $invalidTagColorSettings = $v5Settings | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $invalidTagColorSettings.tags[0].color = 'blue'
    ConvertFrom-CountdownSettingsObject -Settings $invalidTagColorSettings | Out-Null
}
catch {
    $invalidTagColorSettingsRejected = $true
}
Assert-Equal $true $invalidTagColorSettingsRejected 'Schema v5 validates tag colors through tag records'

$datedNote = New-CountdownNoteRecord -Id 'dated-note' -Text '带日期便签' -Date '2026-07-18' -TagIds @('work')
Assert-Equal '2026-07-18' $datedNote.date 'Note records preserve an exact calendar date'
$reminderStart = [DateTimeOffset]'2026-07-19T08:00:00+08:00'
$reminderNote = New-CountdownNoteRecord -Id 'reminder-note' -Text '每半小时站起来活动' -Date '2026-07-19' -TagIds @() -ReminderIntervalMinutes 30 -ReminderStartUtc $reminderStart
Assert-Equal 30 $reminderNote.reminderIntervalMinutes 'Note records preserve a reminder interval'
Assert-Equal ([DateTimeOffset]'2026-07-19T00:00:00Z') $reminderNote.reminderStartUtc 'Note records normalize reminder start to UTC'
$dueBeforeInterval = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:29:59+08:00') -LastShownCycles @{})
Assert-Equal 0 $dueBeforeInterval.Count 'Note reminder does not fire before its interval'
$dueAtInterval = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:30:00+08:00') -LastShownCycles @{})
Assert-Equal 1 $dueAtInterval.Count 'Note reminder fires when its interval elapses'
Assert-Equal 1 $dueAtInterval[0].Cycle 'First note reminder reports cycle one'
$dueAlreadyShown = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T08:30:00+08:00') -LastShownCycles @{ 'reminder-note' = 1 })
Assert-Equal 0 $dueAlreadyShown.Count 'Note reminder does not repeat within the same cycle'
$dueNextCycle = @(Get-DueCountdownNoteReminders -Notes @($reminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T09:00:00+08:00') -LastShownCycles @{ 'reminder-note' = 1 })
Assert-Equal 1 $dueNextCycle.Count 'Note reminder fires again at the next interval'
$completedReminderNote = New-CountdownNoteRecord -Id 'completed-reminder-note' -Text '已经完成' -Date '2026-07-19' -IsCompleted $true -TagIds @() -ReminderIntervalMinutes 30 -ReminderStartUtc $reminderStart
Assert-Equal 0 @(Get-DueCountdownNoteReminders -Notes @($completedReminderNote) -BeijingTime ([DateTimeOffset]'2026-07-19T09:00:00+08:00') -LastShownCycles @{}).Count 'Completed notes do not produce reminders'
$invalidReminderIntervalRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-reminder-note' -Text '无效提醒' -Date '2026-07-19' -TagIds @() -ReminderIntervalMinutes 0 -ReminderStartUtc $reminderStart | Out-Null
}
catch {
    $invalidReminderIntervalRejected = $true
}
Assert-Equal $true $invalidReminderIntervalRejected 'Note records reject reminder intervals below one minute'
$leapDayNote = New-CountdownNoteRecord -Id 'leap-day-note' -Text '闰日便签' -Date '2028-02-29' -TagIds @()
Assert-Equal '2028-02-29' $leapDayNote.date 'Note records accept a valid leap day'
$invalidNoteDateRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-date-note' -Text '无效日期便签' -Date '2026-7-18' -TagIds @() | Out-Null
}
catch {
    $invalidNoteDateRejected = $true
}
Assert-Equal $true $invalidNoteDateRejected 'Note records reject non-canonical calendar dates'
$invalidCalendarDateRejected = $false
try {
    New-CountdownNoteRecord -Id 'invalid-calendar-note' -Text '不存在日期便签' -Date '2026-02-29' -TagIds @() | Out-Null
}
catch {
    $invalidCalendarDateRejected = $true
}
Assert-Equal $true $invalidCalendarDateRejected 'Note records reject an impossible calendar date'

$originalNotes = [System.Collections.ArrayList]::new()
[void] $originalNotes.Add([pscustomobject]@{ id = 'existing'; text = '已有便签'; date = '2026-07-17'; isCompleted = $false; tagIds = @('work') })
$missingAddDateRejected = $false
try {
    Invoke-NoteMutation -Notes $originalNotes -Action Add -Text '缺少日期' -TagIds @() -PersistCandidate { param($candidate) } | Out-Null
}
catch {
    $missingAddDateRejected = $true
}
Assert-Equal $true $missingAddDateRejected 'Note add requires a calendar date'
$persistedCandidates = [System.Collections.ArrayList]::new()
$addResult = Invoke-NoteMutation -Notes $originalNotes -Action Add -Text '新增便签' -Date '2026-07-18' -TagIds @('home') -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal 1 $originalNotes.Count 'Note add does not mutate source before commit'
Assert-Equal 2 $addResult.Notes.Count 'Note add returns candidate notes'
Assert-Equal $true $addResult.Changed 'Note add reports change'
Assert-Equal 1 $persistedCandidates.Count 'Note add persists one candidate'
Assert-Equal 'home' $addResult.Notes[1].tagIds[0] 'Note add preserves selected tag IDs'
Assert-Equal '2026-07-18' $addResult.Notes[1].date 'Note add preserves the selected calendar date'
$updateResult = Invoke-NoteMutation -Notes $addResult.Notes -Action Update -Id 'existing' -Text '已有便签' -Date '2026-07-19' -TagIds @('work') -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal '2026-07-17' $addResult.Notes[0].date 'Note update does not mutate the source date before commit'
Assert-Equal '2026-07-19' $updateResult.Notes[0].date 'Note update preserves the selected calendar date'
$completionResult = Invoke-NoteMutation -Notes $updateResult.Notes -Action SetCompletion -Id 'existing' -IsCompleted $true -PersistCandidate {
    param($candidate)
    [void] $persistedCandidates.Add($candidate)
}
Assert-Equal $false ([bool] $updateResult.Notes[0].isCompleted) 'Note completion does not mutate source before commit'
Assert-Equal $true ([bool] $completionResult.Notes[0].isCompleted) 'Note completion returns changed candidate'
Assert-Equal 'work' $completionResult.Notes[0].tagIds[0] 'Note completion retains existing tag IDs'
Assert-Equal '2026-07-19' $completionResult.Notes[0].date 'Note completion retains the existing calendar date'
$noOpPersistCalls = [pscustomobject]@{ Count = 0 }
$noOpResult = Invoke-NoteMutation -Notes $completionResult.Notes -Action SetCompletion -Id 'existing' -IsCompleted $true -PersistCandidate {
    param($candidate)
    $noOpPersistCalls.Count++
}
Assert-Equal $false $noOpResult.Changed 'Repeated note completion is a no-op'
Assert-Equal 0 $noOpPersistCalls.Count 'Repeated note completion does not persist'
$failedPersistCalls = [pscustomobject]@{ Count = 0 }
$persistFailureObserved = $false
try {
    Invoke-NoteMutation -Notes $completionResult.Notes -Action Remove -Id 'existing' -PersistCandidate {
        param($candidate)
        $failedPersistCalls.Count++
        throw 'Simulated settings write failure.'
    } | Out-Null
}
catch {
    $persistFailureObserved = $true
}
Assert-Equal $true $persistFailureObserved 'Note persistence failure is observable'
Assert-Equal 2 $completionResult.Notes.Count 'Note persistence failure preserves source notes'
Assert-Equal $true ([bool] $completionResult.Notes[0].isCompleted) 'Note persistence failure preserves source record state'
Assert-Equal '2026-07-19' $completionResult.Notes[0].date 'Note persistence failure preserves the source calendar date'
Assert-Equal 1 $failedPersistCalls.Count 'Note persistence failure invokes persistence once'

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

[pscustomobject]@{ Passed = $script:assertions; Failed = 0; TimeZone = (Get-BeijingNow).Offset.ToString() } | ConvertTo-Json
