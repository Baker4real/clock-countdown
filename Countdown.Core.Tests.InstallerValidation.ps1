# Countdown.Core.Tests.ps1 的 InstallerValidation 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$installerValidationEngine = (Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$installerValidationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('clock-countdown-install-validation-' + [guid]::NewGuid().ToString('N'))
$installerValidationInstallRoot = Join-Path $installerValidationRoot 'install'
$installerValidationTask = 'Minmin Countdown Install Validation ' + [guid]::NewGuid().ToString('N')
$installerValidationLock = 'Local\MinminCountdownInstallValidation-' + [guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $installerValidationRoot -Force | Out-Null
try {
    $installerValidationJson = & $installerValidationEngine -NoLogo -NoProfile -NonInteractive `
        -File (Join-Path $PSScriptRoot 'Install-CountdownProgram.ps1') `
        -PackageRoot $PSScriptRoot -InstallRoot $installerValidationInstallRoot `
        -TaskName $installerValidationTask -CheckOnly -LockName $installerValidationLock | Out-String
    $installerValidationExit = $LASTEXITCODE
    $installerValidation = $installerValidationJson | ConvertFrom-Json -DateKind String
    Assert-Equal 0 $installerValidationExit 'Installer CheckOnly returns zero for a valid package and environment'
    Assert-Equal $true ([bool] $installerValidation.succeeded) 'Installer CheckOnly succeeds'
    Assert-Equal $true ([bool] $installerValidation.checkOnly) 'Installer reports CheckOnly mode'
    Assert-Equal $true ($null -eq $installerValidation.failedStage -and $null -eq $installerValidation.reason -and $null -eq $installerValidation.error) 'Installer CheckOnly has no failure fields'

    $installerValidationManifest = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'app\published-payload.json')) | ConvertFrom-Json -DateKind String
    Assert-Equal ([string] $installerValidationManifest.commit) ([string] $installerValidation.package.commit) 'Installer CheckOnly reports the package commit'
    Assert-Equal ([string] $installerValidationManifest.aggregateSha256) ([string] $installerValidation.package.aggregateSha256) 'Installer CheckOnly verifies the package aggregate hash'
    Assert-Equal 'place-candidate,stop-application,write-transaction-marker,snapshot-settings,replace-program-directory,create-login-task,register-desktop-shortcut,start-and-wait-ready,commit' (@($installerValidation.plannedSteps) -join ',') 'Installer CheckOnly reports the planned install without executing it'
    Assert-Equal $false (Test-Path -LiteralPath $installerValidationInstallRoot) 'Installer CheckOnly creates no install root'

    $installerCommand = Get-Command (Join-Path $PSScriptRoot 'Install-CountdownProgram.ps1')
    Assert-Equal $true $installerCommand.Parameters.ContainsKey('CheckOnly') 'New installer exposes the CheckOnly public switch'
    Assert-Equal $false $installerCommand.Parameters.ContainsKey('ValidateOnly') 'New installer has no legacy ValidateOnly switch'
    Assert-Equal $false $installerCommand.Parameters.ContainsKey('DeferCommit') 'New installer has no deferred-commit switch'
}
finally {
    Remove-CountdownIsolatedTestTree -Path $installerValidationRoot -AllowedRoot ([System.IO.Path]::GetTempPath())
}
