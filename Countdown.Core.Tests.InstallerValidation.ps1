# Countdown.Core.Tests.ps1 的 InstallerValidation 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$installerValidationEngine =(Get-Command pwsh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$installerValidationJson = & $installerValidationEngine -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Install-Countdown.ps1') -ValidateOnly | Out-String
if ($LASTEXITCODE -ne 0) { throw "Installer validation failed: $installerValidationJson" }
$installerValidation = $installerValidationJson | ConvertFrom-Json
Assert-Equal $true $installerValidation.Valid 'Installer package validation succeeds'
Assert-Equal $releaseContract.IntegrityHashes.Count $installerValidation.VerifiedFiles 'Installer reports the canonical integrity count'
Assert-Equal (@($releaseContract.OfficialCharacterAssets) -join ',') (@($installerValidation.OfficialCharacterAssets) -join ',') 'Installer exposes canonical official character assets'
Assert-Equal (@($releaseContract.FloatingPlushAssets) -join ',') (@($installerValidation.FloatingPlushAssets) -join ',') 'Installer exposes canonical floating plush assets'
Assert-Equal (@($releaseContract.CurrentFloatingRuntimeAssets) -join ',') (@($installerValidation.CurrentFloatingRuntimeAssets) -join ',') 'Installer exposes canonical current floating runtime assets'
Assert-Equal (@($releaseContract.LegacyFloatingRegressionAssets) -join ',') (@($installerValidation.LegacyFloatingRegressionAssets) -join ',') 'Installer exposes canonical floating regression fixtures'
Assert-Equal (@($releaseContract.CompatibilityFixtureFiles) -join ',') (@($installerValidation.CompatibilityFixtureFiles) -join ',') 'Installer exposes canonical compatibility fixtures'
Assert-Equal (@($releaseContract.RetiredRuntimeFiles) -join ',') (@($installerValidation.RetiredRuntimeFiles) -join ',') 'Installer exposes canonical retired runtime files'
Assert-Equal $true $installerValidation.Transactional 'Installer exposes transactional install boundary'
Assert-Equal $true $installerValidation.DeferredCommitSupported 'Installer exposes deferred commit for office acceptance'
$installerCommand = Get-Command (Join-Path $PSScriptRoot 'Install-Countdown.ps1')
Assert-Equal $false $installerCommand.Parameters.ContainsKey('TransactionMutexHeld') 'Installer does not expose a lock-bypass switch'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ParentTransactionMutexHeld') 'Installer exposes a parent-held transaction lock context only for deferred office acceptance'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedInteractiveUser') 'Office installer validates the expected interactive user'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedInteractiveSessionId') 'Office installer validates the expected interactive session'
Assert-Equal $true $installerCommand.Parameters.ContainsKey('ExpectedDesktopPath') 'Office installer validates the expected desktop path'
$recoveryCommand = Get-Command Recover-CountdownInstallTransactions -Module Countdown.InstallTransaction
Assert-Equal $false $recoveryCommand.Parameters.ContainsKey('TransactionMutexHeld') 'Recovery does not expose a lock-bypass switch'
$candidateCommand = Get-Command Set-CountdownInstallTransactionCandidateLauncherHash -Module Countdown.InstallTransaction
Assert-Equal $true $candidateCommand.Parameters.ContainsKey('CandidateLauncherSha256') 'Transaction persists a candidate launcher hash before replacement'
$failureReceiptCommand = Get-Command Write-CountdownInstallFailureReceipt -Module Countdown.InstallTransaction
Assert-Equal $true ($failureReceiptCommand.Parameters.ContainsKey('ErrorRecord') -and $failureReceiptCommand.Parameters.ContainsKey('RollbackSucceeded')) 'Transaction module exposes a selected-field deployment failure receipt writer'
$failureReceiptParserCommand = Get-Command ConvertFrom-CountdownInstallFailureReceiptBytes -Module Countdown.InstallTransaction
Assert-Equal $true ($failureReceiptCommand.Parameters.ContainsKey('CommitState') -and $failureReceiptParserCommand.Parameters.ContainsKey('Bytes')) 'Transaction module exposes a strict deployment failure receipt contract'
