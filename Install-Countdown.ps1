#requires -Version 7.0

param(
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'

$staging = $PSScriptRoot
$install = 'D:\敏敏子倒计时'
$taskName = 'Minmin Countdown'
$legacyTaskName = '敏敏子倒计时'
$expected = @{
    'Countdown.Core.psm1' = 'E1A639B06D404F7F4638CAB4DE6392FB27E5C324D77A2AC44D1B226C8375410B'
    'Countdown.Notes.psm1' = 'A87FC67C86576699F1C9ADE67C228744A8168CEE4402B08A917E16AADA4B9405'
    'Countdown.Settings.psm1' = '410499207842F48FDF1E55B5677E622045CC06A080330FC989A5CCACEFE5F980'
    'Countdown.Core.Tests.ps1' = '922A8CEC8E37EEFDC96F058A8D246A479AC69767DA01874A523849CB689AFCBE'
    'Countdown.Ui.Tests.ps1' = '53E21FB34612A571BA8CB27A503E011CD2F673A490409D2A47DE674124207A1E'
    '敏敏子倒计时.ps1' = 'D796A5B602D4AC20AF253FC786CF86DCAAA90EA2DF804A7031BF80227C264FD3'
    'CountdownLauncher.exe' = '195A228F647E4541AE4EEF7358BF2BDD478F705FA23D1981C0D96E8DD835CC00'
    'countdown-icon.ico' = 'C5B7CB0A09524AEFE4488291078F61B0AF54958582DA98B99B25D0F4DC6976C5'
}

foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path $staging $entry.Key
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing staging file: $($entry.Key)"
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actual -ne $entry.Value) {
        throw "Hash mismatch: $($entry.Key)"
    }
}

if ($ValidateOnly) {
    [pscustomobject]@{
        Valid = $true
        SourceRoot = $staging
        VerifiedFiles = $expected.Count
    } | ConvertTo-Json
    exit 0
}

$installResolved = [System.IO.Path]::GetFullPath($install)
if (-not $installResolved.StartsWith('D:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Install path is outside D drive: $installResolved"
}
if (-not (Test-Path -LiteralPath $installResolved)) {
    New-Item -ItemType Directory -Path $installResolved -Force | Out-Null
}

Set-Service -Name W32Time -StartupType Automatic
Start-Service -Name W32Time
$resync = (w32tm.exe /resync /rediscover | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Windows Time resync failed: $resync"
}
Start-Sleep -Seconds 3
$timeSource = (w32tm.exe /query /source | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $timeSource -match 'Local CMOS|Free-running|VM IC') {
    throw "Windows Time is not using a network source: $timeSource"
}
$timeStatus = (w32tm.exe /query /status | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Windows Time status query failed.'
}

$explorer = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | Where-Object { $_.SessionId -gt 0 })
if ($explorer.Count -ne 1) {
    throw "Expected exactly one interactive Explorer session, found $($explorer.Count)."
}
$owner = Invoke-CimMethod -InputObject $explorer[0] -MethodName GetOwner
if ($owner.ReturnValue -ne 0) {
    throw "Could not resolve Explorer owner, return value $($owner.ReturnValue)."
}
$interactiveUser = "$($owner.Domain)\$($owner.User)"
$account = New-Object System.Security.Principal.NTAccount($interactiveUser)
$sid = $account.Translate([System.Security.Principal.SecurityIdentifier]).Value
$userShellFolders = Get-ItemProperty -LiteralPath "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"
$desktop = [Environment]::ExpandEnvironmentVariables($userShellFolders.Desktop)
if (-not ([System.IO.Path]::GetFullPath($desktop)).StartsWith('D:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Interactive user's desktop is outside D drive: $desktop"
}
if (-not (Test-Path -LiteralPath $desktop)) {
    throw "Interactive user's desktop does not exist: $desktop"
}

$powerShellEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if ([string]::IsNullOrWhiteSpace($powerShellEngine)) {
    throw '未找到 pwsh.exe；本项目要求 PowerShell 7+，不支持 Windows PowerShell 5.1。'
}
$powerShellProcessName = [System.IO.Path]::GetFileName($powerShellEngine)

$testOutput = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -File (Join-Path $staging 'Countdown.Core.Tests.ps1') | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Remote tests failed: $testOutput"
}
$uiTestOutput = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $staging 'Countdown.Ui.Tests.ps1') | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Remote UI regression test failed: $uiTestOutput"
}
$uiValidationOutput = & $powerShellEngine `
    -NoLogo -NoProfile -NonInteractive -STA -File (Join-Path $staging '敏敏子倒计时.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Remote WPF validation failed: $uiValidationOutput"
}

$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask -and $existingTask.State -eq 'Running') {
    Stop-ScheduledTask -TaskName $taskName
    Start-Sleep -Seconds 2
}
$legacyTask = Get-ScheduledTask -TaskName $legacyTaskName -ErrorAction SilentlyContinue
if ($null -ne $legacyTask) {
    if ($legacyTask.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $legacyTaskName
        Start-Sleep -Seconds 2
    }
    Unregister-ScheduledTask -TaskName $legacyTaskName -Confirm:$false
}

Copy-Item -LiteralPath (Join-Path $staging 'Countdown.Core.psm1') -Destination (Join-Path $install 'Countdown.Core.psm1') -Force
Copy-Item -LiteralPath (Join-Path $staging 'Countdown.Notes.psm1') -Destination (Join-Path $install 'Countdown.Notes.psm1') -Force
Copy-Item -LiteralPath (Join-Path $staging 'Countdown.Settings.psm1') -Destination (Join-Path $install 'Countdown.Settings.psm1') -Force
Copy-Item -LiteralPath (Join-Path $staging '敏敏子倒计时.ps1') -Destination (Join-Path $install '敏敏子倒计时.ps1') -Force
Copy-Item -LiteralPath (Join-Path $staging 'CountdownLauncher.exe') -Destination (Join-Path $install 'CountdownLauncher.exe') -Force
Copy-Item -LiteralPath (Join-Path $staging 'countdown-icon.ico') -Destination (Join-Path $install 'countdown-icon.ico') -Force

$appPath = Join-Path $install '敏敏子倒计时.ps1'
$powershell = $powerShellEngine
$arguments = "-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -STA -File `"$appPath`""
$launcherPath = Join-Path $desktop '敏敏子倒计时.exe'
$launcherStatePath = Join-Path $install 'launcher-state.json'
if (Test-Path -LiteralPath $launcherPath) {
    $currentLauncherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $launcherPath).Hash
    $managedLauncher = $false
    if (Test-Path -LiteralPath $launcherStatePath) {
        $launcherStateJson = [System.IO.File]::ReadAllText($launcherStatePath, [System.Text.Encoding]::UTF8)
        $previousLauncherState = $launcherStateJson | ConvertFrom-Json
        $pathMatches = [string]::Equals($previousLauncherState.desktopPath, $launcherPath, [System.StringComparison]::OrdinalIgnoreCase)
        $hashMatches = $currentLauncherHash -eq $previousLauncherState.desktopSha256 -or $currentLauncherHash -eq $expected['CountdownLauncher.exe']
        $managedLauncher = $pathMatches -and $hashMatches
    }
    elseif ($currentLauncherHash -eq 'E5E37A9D20669E4A888969F0DB95560D45D581913B68EA7600055DE50E20AC14') {
        $managedLauncher = $true
    }
    if (-not $managedLauncher) {
        throw "Refusing to overwrite an unmanaged desktop launcher: $launcherPath"
    }
}
$launcherTempPath = "$launcherPath.new"
Copy-Item -LiteralPath (Join-Path $install 'CountdownLauncher.exe') -Destination $launcherTempPath -Force
$launcherTempHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $launcherTempPath).Hash
if ($launcherTempHash -ne $expected['CountdownLauncher.exe']) {
    Remove-Item -LiteralPath $launcherTempPath -Force
    throw 'Desktop launcher staging hash mismatch.'
}
Move-Item -LiteralPath $launcherTempPath -Destination $launcherPath -Force
if (-not (Test-Path -LiteralPath $launcherPath)) {
    throw 'Desktop launcher was not created.'
}
$launcherState = [ordered]@{ schemaVersion = 1; desktopPath = $launcherPath; desktopSha256 = $launcherTempHash } | ConvertTo-Json
[System.IO.File]::WriteAllText("$launcherStatePath.tmp", $launcherState, (New-Object System.Text.UTF8Encoding($false)))
Move-Item -LiteralPath "$launcherStatePath.tmp" -Destination $launcherStatePath -Force
$legacyLauncherPath = Join-Path $desktop '敏敏子倒计时.cmd'
if (Test-Path -LiteralPath $legacyLauncherPath) {
    $legacyExpected = "@echo off`r`nC:\Windows\System32\schtasks.exe /Run /TN `"$taskName`" >nul 2>&1`r`nexit /b %errorlevel%`r`n"
    $legacyActual = [System.IO.File]::ReadAllText($legacyLauncherPath, [System.Text.Encoding]::ASCII)
    if ($legacyActual -eq $legacyExpected) {
        Remove-Item -LiteralPath $legacyLauncherPath -Force
    }
}
& "$env:SystemRoot\System32\ie4uinit.exe" -show 2>$null

$action = New-ScheduledTaskAction -Execute $powershell -Argument $arguments -WorkingDirectory $install
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $interactiveUser
$principal = New-ScheduledTaskPrincipal -UserId $interactiveUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description '登录后启动敏敏子倒计时，按北京时间计算。' -Force | Out-Null

$registeredTask = Get-ScheduledTask -TaskName $taskName
if (-not [string]::Equals(
        [System.IO.Path]::GetFullPath([string] $registeredTask.Actions.Execute),
        [System.IO.Path]::GetFullPath($powerShellEngine),
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
    throw "计划任务未使用 PowerShell 7：$($registeredTask.Actions.Execute)"
}

Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 4

[ordered]@{
    TestOutput = $testOutput.Trim()
    UiTestOutput = $uiTestOutput.Trim()
    UiValidationOutput = $uiValidationOutput.Trim()
    InstalledFiles = @(Get-ChildItem -LiteralPath $install -File | Select-Object Name, Length, @{n='Hash';e={(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash}})
    Launcher = Get-Item -LiteralPath $launcherPath | Select-Object FullName, Length
    Task = Get-ScheduledTask -TaskName $taskName | Select-Object TaskName, State, @{n='Execute';e={$_.Actions.Execute}}, @{n='Arguments';e={$_.Actions.Arguments}}, @{n='LogonType';e={$_.Principal.LogonType}}
    TaskInfo = Get-ScheduledTaskInfo -TaskName $taskName | Select-Object LastRunTime, LastTaskResult, NextRunTime
    Process = @(Get-CimInstance Win32_Process -Filter "Name='$powerShellProcessName'" | Where-Object { $_.CommandLine -like '*敏敏子倒计时.ps1*' } | Select-Object ProcessId, SessionId, CommandLine)
    TimeSource = $timeSource
    TimeStatus = $timeStatus
} | ConvertTo-Json -Depth 6
