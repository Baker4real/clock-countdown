#requires -Version 7.0
[CmdletBinding()]
param([switch] $Json)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
$repo = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repo 'Countdown.Validation.psm1') -Force
$root = Join-Path $repo ('logs/validation-diagnostics-test-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
$corePath = Join-Path $root 'Countdown.Core.Tests.ps1'
[IO.File]::WriteAllText($corePath, @'
[Console]::Error.WriteLine('正在执行原始查询')
Write-Error '中文原始错误' -ErrorId 'Countdown.OriginalQuery' -ErrorAction Stop
'@, $utf8)
$failure = $null
try { Invoke-CountdownValidationSuite -Root $root -PowerShell7Path $pwsh -DotnetPath $pwsh -EvidenceDirectory $root | Out-Null }
catch { $failure = $_ }
if ($null -eq $failure) { throw '失败的子脚本不得返回成功。' }
$child = $failure.Exception.Data['ChildErrorRecord']
if ($null -eq $child -or $child.fullyQualifiedErrorId -notlike '*Countdown.OriginalQuery*' -or
    $child.exceptionMessage -ne '中文原始错误' -or $child.scriptStackTrace -notlike '*Countdown.Core.Tests.ps1: line 2*' -or
    [string]::IsNullOrWhiteSpace($child.positionMessage)) {
    throw "子进程失败必须保留原始中文、FQID、脚本位置与调用栈。实际：$($failure.Exception.Message)"
}
$evidence = [string]$failure.Exception.Data['ValidationEvidenceDirectory']
if (-not (Test-Path -LiteralPath (Join-Path $evidence 'stderr.log'))) { throw '失败后阶段日志必须保留。' }
if (-not [IO.Path]::GetFullPath($evidence).StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "自检的阶段证据必须留在测试根内，不得写入共享证据目录。实际：$evidence"
}
Import-Module (Join-Path $repo 'Countdown.InstallTransaction.psm1') -Force -DisableNameChecking
$receiptPath = Join-Path $root 'failure-receipt.json'
$null = Write-CountdownInstallFailureReceipt -Path $receiptPath -Stage 'Core 回归' -ErrorRecord $failure `
    -TransactionStarted $false -CommitState 'not-committed' -RollbackAttempted $false -RollbackSucceeded $false -PreservedUnmanagedLaunchers $false
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.fullyQualifiedErrorId -notlike '*Countdown.OriginalQuery*' -or
    $receipt.scriptStackTrace -notlike '*Countdown.Core.Tests.ps1: line 2*') { throw '正式失败回执必须使用子进程原始错误，而非父执行器调用点。' }
$assertions = 3
foreach ($case in @(
    @{ Source="'native stdout diagnostic'; exit 73"; Message='native stdout diagnostic' },
    @{ Source=''; Message='未返回 JSON' },
    @{ Source="'noise'"; Message='无效 JSON' }
)) {
    [IO.File]::WriteAllText($corePath, $case.Source, $utf8)
    $failure = $null
    try { Invoke-CountdownValidationSuite -Root $root -PowerShell7Path $pwsh -DotnetPath $pwsh -EvidenceDirectory $root | Out-Null }
    catch { $failure = $_ }
    if ($null -eq $failure -or $failure.Exception.Message -notlike "*$($case.Message)*") { throw '无效子进程结果必须明确失败。' }
    $statePath = Join-Path ([string]$failure.Exception.Data['ValidationEvidenceDirectory']) 'stage.json'
    if ((Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Status -ne 'Failed') { throw '无效结果必须留下失败阶段。' }
    $assertions++
}

# 套件会测试原生命令失败；脚本最终成功时，应与直接 pwsh -File 一样返回 0。
[IO.File]::WriteAllText($corePath, @"
& '$pwsh' -NoProfile -Command 'exit 42'
'{"Passed":1,"Failed":0}'
"@, $utf8)
$failure = $null
try { Invoke-CountdownValidationSuite -Root $root -PowerShell7Path $pwsh -DotnetPath $pwsh -EvidenceDirectory $root | Out-Null }
catch { $failure = $_ }
if ($null -eq $failure -or $failure.Exception.Message -notlike '源码应用 Release 构建*') {
    throw '成功脚本必须越过 Core；已处理的原生负例不得冒充脚本退出码。'
}
$assertions++

# 入口须保持 pwsh -File 的全局作用域：Core 测试的脚本级变量与函数要能从模块绑定脚本块和
# GetNewClosure 闭包里解析，否则只在办公室安装期的 Core 回归里报错（2026-10-21 部署回滚）。
[IO.File]::WriteAllText($corePath, @'
Set-StrictMode -Version Latest
$scopeModule = New-Module -Name CountdownEntryScopeProbe -ScriptBlock {
    Set-StrictMode -Version Latest
    function Get-CountdownEntryScopeValue { param($Value) $Value }
}
$scopeValue = 'script-level'
function Get-CountdownEntryScopeHelper { 'script-function' }
$null = & $scopeModule { Get-CountdownEntryScopeValue $scopeValue }
$scopeClosure = { Get-CountdownEntryScopeHelper }.GetNewClosure()
$null = & $scopeClosure
'{"Passed":1,"Failed":0}'
'@, $utf8)
$failure = $null
try { Invoke-CountdownValidationSuite -Root $root -PowerShell7Path $pwsh -DotnetPath $pwsh -EvidenceDirectory $root | Out-Null }
catch { $failure = $_ }
if ($null -eq $failure -or $failure.Exception.Message -notlike '源码应用 Release 构建*') {
    throw "入口必须让脚本级变量与函数在模块作用域和闭包里可见。实际：$(if ($null -ne $failure) { $failure.Exception.Message })"
}
$assertions++

# 子脚本写小段日志后等待释放；在释放前读取文件可证明不是退出后才落盘。
$release = Join-Path $root 'release'
[IO.File]::WriteAllText($corePath, @"
[Console]::Error.WriteLine('阶段已开始')
`$deadline = [datetime]::UtcNow.AddSeconds(15)
while (-not (Test-Path -LiteralPath '$release')) {
    if ([datetime]::UtcNow -gt `$deadline) { exit 99 }
    Start-Sleep -Milliseconds 50
}
exit 17
"@, $utf8)
$liveRoot = Join-Path $root 'live'
$driver = Join-Path $root 'driver.ps1'
[IO.File]::WriteAllText($driver, @"
Import-Module '$repo/Countdown.Validation.psm1' -Force
try { Invoke-CountdownValidationSuite -Root '$root' -PowerShell7Path '$pwsh' -DotnetPath '$pwsh' -EvidenceDirectory '$liveRoot' | Out-Null }
catch { exit 1 }
"@, $utf8)
$start = [Diagnostics.ProcessStartInfo]::new($pwsh)
$start.UseShellExecute=$false; $start.CreateNoWindow=$true
$start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
$start.StandardOutputEncoding=$utf8; $start.StandardErrorEncoding=$utf8
foreach ($arg in @('-NoLogo','-NoProfile','-File',$driver)) { $start.ArgumentList.Add($arg) }
$process = [Diagnostics.Process]::Start($start)
$outTask = $process.StandardOutput.ReadToEndAsync()
$errTask = $process.StandardError.ReadToEndAsync()
$observed = $false
try {
    $deadline = [datetime]::UtcNow.AddSeconds(10)
    do {
        $logs = @(Get-ChildItem -LiteralPath $liveRoot -Filter stderr.log -Recurse -ErrorAction SilentlyContinue)
        foreach ($log in $logs) {
            $stream = [IO.FileStream]::new($log.FullName,'Open','Read','ReadWrite')
            $reader = [IO.StreamReader]::new($stream, $utf8)
            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($text.Contains('阶段已开始')) { $observed = -not $process.HasExited }
        }
        if (-not $observed) { Start-Sleep -Milliseconds 50 }
    } while (-not $observed -and [datetime]::UtcNow -lt $deadline -and -not $process.HasExited)
    if (-not $observed) { throw '子进程运行期间必须可读取小段 UTF-8 阶段日志。' }
    [IO.File]::WriteAllText($release, '', $utf8)
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 1) { throw '子进程非零必须传播。' }
    [void]$outTask.GetAwaiter().GetResult(); [void]$errTask.GetAwaiter().GetResult()
    $assertions++
}
finally {
    if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
    $process.Dispose()
}

# CI 上应用 validate-only 偶发以 0xC0000409 崩溃：诊断日志须在清理前复制进该阶段证据目录。
# 夹具子进程都不开窗口，本进程临时设隔离 opt-out 让它们直接启动；假 dotnet 只让 Release 构建阶段放行。
$crashRoot = Join-Path $root 'application-crash'
[void][IO.Directory]::CreateDirectory($crashRoot)
$crashRecord = Join-Path $crashRoot 'diagnostic-path.txt'
[IO.File]::WriteAllText((Join-Path $crashRoot 'Countdown.Core.Tests.ps1'), "'{`"Passed`":1,`"Failed`":0}'", $utf8)
[IO.File]::WriteAllText((Join-Path $crashRoot 'Countdown.CSharp.Tests.ps1'), "'{`"Passed`":1,`"Failed`":0,`"AdapterUiValidated`":true}'", $utf8)
[IO.File]::WriteAllText((Join-Path $crashRoot 'Countdown.Ui.Tests.ps1'), "'{`"Passed`":1,`"Failed`":0}'", $utf8)
[IO.File]::WriteAllText((Join-Path $crashRoot '敏敏子倒计时.ps1'), @"
param([switch] `$ValidateOnly, [string] `$SettingsFile, [string] `$DiagnosticLogPath)
[IO.File]::WriteAllText('$crashRecord', `$DiagnosticLogPath)
[IO.File]::WriteAllText(`$DiagnosticLogPath, '{"event":"crash-breadcrumb"}')
[IO.File]::WriteAllText("`$DiagnosticLogPath.1", '{"event":"rotated"}')
exit -1073740791
"@, $utf8)
$fakeDotnet = Join-Path $crashRoot 'dotnet.cmd'
[IO.File]::WriteAllText($fakeDotnet, "@exit /b 0`r`n", $utf8)
$priorIsolation = $env:MINMIN_SUITE_ALLOW_UNISOLATED
$env:MINMIN_SUITE_ALLOW_UNISOLATED = '1'
$failure = $null
try { Invoke-CountdownValidationSuite -Root $crashRoot -PowerShell7Path $pwsh -DotnetPath $fakeDotnet -EvidenceDirectory $crashRoot | Out-Null }
catch { $failure = $_ }
finally { $env:MINMIN_SUITE_ALLOW_UNISOLATED = $priorIsolation }
if ($null -eq $failure -or $failure.Exception.Message -notlike '应用 validate-only 失败，退出码：-1073740791*') {
    throw "应用崩溃必须以原退出码失败。实际：$(if ($null -ne $failure) { $failure.Exception.Message })"
}
$diagnosticPath = [IO.File]::ReadAllText($crashRecord, $utf8)
$preserved = Join-Path ([string]$failure.Exception.Data['ValidationEvidenceDirectory']) ([IO.Path]::GetFileName($diagnosticPath))
if (-not (Test-Path -LiteralPath $preserved -PathType Leaf) -or [IO.File]::ReadAllText($preserved, $utf8) -ne '{"event":"crash-breadcrumb"}' -or
    -not (Test-Path -LiteralPath "$preserved.1" -PathType Leaf)) {
    throw "应用崩溃后诊断日志及其轮转文件必须复制进该阶段证据目录。实际：$preserved"
}
if ((Test-Path -LiteralPath $diagnosticPath) -or (Test-Path -LiteralPath "$diagnosticPath.1")) { throw '复制进证据目录后仍须清理临时诊断日志。' }
$assertions++
# 断言全部通过才清理测试根；任一断言失败时脚本已中止，测试根留作现场。清理失败只记 stderr，stdout 仍是单个 JSON。
try {
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse -Force) { Remove-Item -LiteralPath $file.FullName -Force }
    foreach ($directory in @(Get-ChildItem -LiteralPath $root -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending)) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath $root -Force
} catch { [Console]::Error.WriteLine("断言已全部通过，但测试根未能清理：$root；$($_.Exception.Message)") }
$result =[pscustomobject]@{ Valid=$true; Passed=$assertions; Failed=0 }
if ($Json) { $result | ConvertTo-Json -Compress } else { $result }
