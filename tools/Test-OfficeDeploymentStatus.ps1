#requires -Version 7.0
[CmdletBinding()]
param([switch] $Json)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'Deploy-OfficeCountdown.ps1') -LoadFunctionsOnly
Import-Module (Join-Path $repo 'Countdown.RemoteDeployment.psm1') -Force
$testRoot = Join-Path $repo "logs/office-status-test-$([guid]::NewGuid().ToString('N'))"
[void] (New-Item -ItemType Directory -Path $testRoot)
$commit = 'a' * 40
$assertions = 0
function Assert-Status($Expected, $Actual, [string] $Name) {
    $script:assertions++
    if ($Expected -cne $Actual) { throw "$Name expected=$Expected actual=$Actual" }
}
$status = Get-OfficeDeploymentStatus -Commit $commit -EvidenceBase $testRoot
Assert-Status 'NotFound' $status.State 'No evidence is never success'
Assert-Status $false $status.LiveVerified 'Local summary never claims live verification'
$runId = '20261010003000000-12345678'
$runRoot = Join-Path $testRoot "$($commit.Substring(0,7))/$runId"
[void] (New-Item -ItemType Directory -Path $runRoot -Force)
$success = [ordered]@{
    SchemaVersion=1; Valid=$true; CoreTests=1248; CSharpTests=992; UiTests=94; UiValid=$true; AdapterUiValidated=$true
    TaskState='Ready'; LastTaskResult=0; RuntimeProcessId=123; RuntimeStage='window-ready'; CalendarState='Ready'; AcceptanceState='finalized-ready'
    SettingsHashBefore=('D'*64); SettingsHashAfter=('D'*64); SettingsLengthBefore=100; SettingsLengthAfter=100
    SettingsMtimeUtcBefore='2026-10-09T00:00:00.0000000Z'; SettingsMtimeUtcAfter='2026-10-09T00:00:00.0000000Z'; SettingsUnchanged=$true
}
$binding = [ordered]@{ SchemaVersion=1; DeploymentBindingSha256=('B'*64); TokenSha256=('A'*64); RuntimeProcessId=123 }
$readback = [ordered]@{
    SchemaVersion=1; Valid=$true; HostIdentityMatch=$true; UserIdentityMatch=$true; RemoteMachineNameSha256=('E'*64); RemoteUserNameSha256=('F'*64)
    TaskState='Ready'; LastTaskResult=0; TaskExecuteMatch=$true; TaskArgumentsMatch=$true; TaskWorkingDirectoryMatch=$true
    StartupSchemaVersion=1; StartupAcceptanceState='finalized'; RuntimeSchemaVersion=4; RuntimeStage='window-ready'; AcceptanceSchemaVersion=1; AcceptanceStage='finalized-ready'
    TokenSha256=('A'*64); TokenMatch=$true; RuntimeProcessId=123; AcceptanceProcessId=123; ProcessIdMatch=$true
    RuntimeSessionId=1; AcceptanceSessionId=1; ProcessSessionId=1; SessionIdMatch=$true; ApplicationPathMatch=$true; ProcessExecutablePathMatch=$true
    ExecutableSha256=('C'*64); ExecutableHashMatch=$true; SettingsSha256=('D'*64); SettingsHashMatch=$true
    SettingsLength=100; SettingsMtimeUtc='2026-10-09T00:00:00.0000000Z'; SettingsUnchanged=$true
}
function Write-FixtureJson([string] $Name, $Value) {
    [IO.File]::WriteAllText((Join-Path $runRoot $Name), ($Value | ConvertTo-Json -Depth 10 -Compress), [Text.UTF8Encoding]::new($false))
}
Write-FixtureJson 'remote-deployment-success.json' $success
Write-FixtureJson 'deployment-binding.json' $binding
Write-FixtureJson 'post-deployment-independent-readback.json' $readback
$ids = @('deployment-package','remote-install-script','remote-deployment-binding','post-deployment-independent-readback-script','post-deployment-independent-readback')
$methods = @('scp','scp','ssh','scp','ssh')
$attempts = @(for ($i=0; $i -lt $ids.Count; $i++) {
    [ordered]@{ id=$ids[$i]; method=$methods[$i]; status='succeeded'; exitCode=0; transferStarted=$true; bytesVerified=$true
        metrics=@{ bytes=100; elapsed_seconds=1; mib_per_second=0.000095; mbps=0.0008 } }
})
$ledger = @{ transferAttemptsVersion='1.0'; generatedAtUtc='2026-10-10T00:30:01.0000000Z'; attempts=$attempts }
Write-FixtureJson 'transfer-attempts.json' $ledger
$manifestBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((@{ 'app\MinminCountdown.exe'=('C'*64) } | ConvertTo-Json -Compress)))
[IO.File]::WriteAllText((Join-Path $runRoot 'remote-install.ps1'), "  DeploymentBindingSha256 = '$('B'*64)'`n    [Convert]::FromBase64String('$manifestBase64'))", [Text.UTF8Encoding]::new($false))
$zipPath = Join-Path $runRoot 'commit-source.zip'
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $writer = [IO.StreamWriter]::new($zip.CreateEntry('Countdown.ReleaseContract.psm1').Open())
    try { $writer.Write("'app\MinminCountdown.exe' = '$('C'*64)'") } finally { $writer.Dispose() }
} finally { $zip.Dispose() }
# git archive ZIP stores the full commit in its EOCD comment.
$bytes = [IO.File]::ReadAllBytes($zipPath)
$bytes[$bytes.Length-2] = 40
[IO.File]::WriteAllBytes($zipPath, [byte[]]($bytes + [Text.Encoding]::ASCII.GetBytes($commit)))
$status = Get-OfficeDeploymentStatus -Commit $commit -EvidenceBase $testRoot
Assert-Status 'HistoricallyVerified' $status.State 'All evidence cross-checks produce historical success'
Assert-Status $runId $status.LatestComplete.RunId 'Complete run is selected'
Assert-Status $false $status.LiveVerified 'Complete old evidence remains historical'
$readback.RuntimeProcessId = 999
Write-FixtureJson 'post-deployment-independent-readback.json' $readback
Assert-Status 'Incomplete' (Get-OfficeDeploymentStatus -Commit $commit -EvidenceBase $testRoot).State 'PID drift rejects success'
$readback.RuntimeProcessId = 123
$readback.SettingsLength = 101
Write-FixtureJson 'post-deployment-independent-readback.json' $readback
Assert-Status 'Incomplete' (Get-OfficeDeploymentStatus -Commit $commit -EvidenceBase $testRoot).State 'Settings metadata must match success receipt'
$readback.SettingsLength = 100
Write-FixtureJson 'post-deployment-independent-readback.json' $readback
$ledger.attempts[4].status = 'failed'
Write-FixtureJson 'transfer-attempts.json' $ledger
Assert-Status 'Incomplete' (Get-OfficeDeploymentStatus -Commit $commit -EvidenceBase $testRoot).State 'Failed ledger is not success'
$ledger.attempts[4].status = 'succeeded'
Write-FixtureJson 'transfer-attempts.json' $ledger
$otherCommit = 'aaaaaaa' + ('b'*33)
Assert-Status 'Incomplete' (Get-OfficeDeploymentStatus -Commit $otherCommit -EvidenceBase $testRoot).State 'Short prefix collision cannot bind another commit'
$validReadbackText = [IO.File]::ReadAllText((Join-Path $runRoot 'post-deployment-independent-readback.json'))
[IO.File]::WriteAllText((Join-Path $runRoot 'post-deployment-independent-readback.json'), $validReadbackText.Insert(1, '"Valid":true,'))
Assert-Status 'Incomplete' (Get-OfficeDeploymentStatus -Commit $commit -EvidenceBase $testRoot).State 'Duplicate receipt fields cannot pass'
[IO.File]::WriteAllText((Join-Path $runRoot 'post-deployment-independent-readback.json'), $validReadbackText)
$newerRun = Join-Path (Split-Path $runRoot) '20261010004000000-87654321'
[void] (New-Item -ItemType Directory -Path $newerRun)
$status = Get-OfficeDeploymentStatus -Commit $commit -EvidenceBase $testRoot
Assert-Status $runId $status.LatestComplete.RunId 'New incomplete run does not hide completed evidence'
Assert-Status 'Incomplete' $status.Runs[0].State 'Newer incomplete attempt stays visible'
Assert-Status 2 $status.Runs.Count 'Both runs are reported'
# A fresh Windows checkout with autocrlf=true must preserve the locked license bytes.
$fixture = Join-Path $testRoot 'source'
[void] (New-Item -ItemType Directory -Path "$fixture/holiday-data" -Force)
Copy-Item -LiteralPath (Join-Path $repo '.gitattributes') -Destination $fixture
Copy-Item -LiteralPath (Join-Path $repo 'holiday-data/LICENSE.txt') -Destination "$fixture/holiday-data/LICENSE.txt"
& git init --quiet $fixture
if ($LASTEXITCODE -ne 0) { throw 'Fixture init failed' }
& git -C $fixture -c core.autocrlf=true add .
if ($LASTEXITCODE -ne 0) { throw 'Fixture add failed' }
& git -C $fixture -c user.name=Test -c user.email=test@example.invalid -c core.hooksPath=NUL commit --quiet -m fixture
if ($LASTEXITCODE -ne 0) { throw 'Fixture commit failed' }
$clone = Join-Path $testRoot 'checkout'
& git -c core.autocrlf=true clone --quiet --no-local $fixture $clone
if ($LASTEXITCODE -ne 0) { throw 'Fixture clone failed' }
Assert-Status 'C1614A521EFA0BD3FC0D271D01E38E9450C8BA3A13825044C325E433B4870142' (Get-FileHash "$clone/holiday-data/LICENSE.txt").Hash 'Fresh autocrlf checkout matches the release license hash'
$priorError = [Console]::Error
$errorCapture = [IO.StringWriter]::new()
try {
    [Console]::SetError($errorCapture)
    $progressOutput = @(Write-OfficeDeploymentProgress '远端安装及事务验收中')
} finally { [Console]::SetError($priorError) }
Assert-Status 0 $progressOutput.Count 'Progress leaves success output clean'
Assert-Status $true ($errorCapture.ToString().Contains('远端安装及事务验收中')) 'Progress is observable on stderr'
$entryOutput = @(& pwsh -NoProfile -File (Join-Path $repo 'Deploy-OfficeCountdown.ps1') -StatusOnly)
Assert-Status 0 $LASTEXITCODE 'Status CLI runs with the current dirty working tree'
$entryStatus = ($entryOutput -join "`n") | ConvertFrom-Json
Assert-Status $false $entryStatus.LiveVerified 'Status CLI emits one historical JSON result'
[pscustomobject]@{ Valid=$true; Assertions=$assertions } | ConvertTo-Json -Compress
