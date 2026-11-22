#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $Json,
    [ValidateSet('All', 'ValidationDiagnostics')] [string] $Check = 'All',
    [string] $BaseCommit = $env:COUNTDOWN_DIFF_BASE,
    [string] $CandidateCommit = $env:COUNTDOWN_DIFF_CANDIDATE,
    [ValidateSet('Index','Commit')][string] $EvidenceSource = $(if ($env:GITHUB_ACTIONS -eq 'true') { 'Commit' } else { 'Index' }),
    [string] $EvidenceCommit = 'HEAD'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$failures = [System.Collections.Generic.List[string]]::new()
$checks = [System.Collections.Generic.List[object]]::new()

# 子测试通过后不该在 logs\ 或仓库根留下 *-test-* 测试根（2026-10-25 清场时两处共堆了 93 个）。每登记一项检查就比一次两处的
# 目录，新出现的归给这一项：通过的算泄漏，由末尾的 tests:leftover-test-roots 判失败；失败的是留作现场，记进它自己的 Detail。
function Get-ToolingTestRoots {
    foreach ($parent in @($repoRoot, (Join-Path $repoRoot 'logs'))) {
        if (Test-Path -LiteralPath $parent -PathType Container) {
            Get-ChildItem -LiteralPath $parent -Directory -Filter '*-test-*' -Force | ForEach-Object { $_.FullName }
        }
    }
}
$testRootsSeen = [System.Collections.Generic.HashSet[string]]::new([string[]] @(Get-ToolingTestRoots), [System.StringComparer]::OrdinalIgnoreCase)
$testRootLeaks = [System.Collections.Generic.List[string]]::new()

function Add-ToolingCheck {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [bool] $Passed, [string] $Detail = '')
    $newRoots = @(Get-ToolingTestRoots | Where-Object { $testRootsSeen.Add($_) } | ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_) })
    if ($newRoots.Count -gt 0) {
        if ($Passed) { $testRootLeaks.Add("$Name 通过却留下 $($newRoots -join '、')") } else { $Detail = "$Detail；失败现场：$($newRoots -join '、')" }
    }
    $checks.Add([pscustomobject]@{ Name = $Name; Status = if ($Passed) { 'processed' } else { 'failed' }; Detail = $Detail })
    if (-not $Passed) { $failures.Add("$Name：$Detail") }
}

function Invoke-ToolingValidationDiagnostics {
    $pwsh = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($pwsh)
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Test-CountdownValidationDiagnostics.ps1'), '-Json')) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $exitCode = $process.ExitCode
        $text = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
    }
    finally {
        $process.Dispose()
    }
    $report = try { $text | ConvertFrom-Json -Depth 12 -ErrorAction Stop } catch { $null }
    $passed = $exitCode -eq 0 -and $null -ne $report -and [bool] $report.Valid
    $detail = if ($null -ne $report) {
        $report | ConvertTo-Json -Compress -Depth 12
    }
    else {
        $combined = if ([string]::IsNullOrWhiteSpace($stderr)) { $text } else { "$text | stderr: $stderr" }
        $combined -replace '\s+', ' '
    }
    if (-not $passed -and $null -ne $report -and -not [string]::IsNullOrWhiteSpace($stderr)) {
        $detail = "$detail | stderr: $($stderr -replace '\s+', ' ')"
    }
    [pscustomobject]@{
        Passed = $passed
        Detail = $detail
    }
}

if ($Check -eq 'ValidationDiagnostics') {
    $selected = Invoke-ToolingValidationDiagnostics
    $selectedResult = [pscustomobject]@{
        Valid = $selected.Passed
        Checks = @([pscustomobject]@{ Name = 'deployment:Test-CountdownValidationDiagnostics.ps1'; Status = if ($selected.Passed) { 'processed' } else { 'failed' }; Detail = $selected.Detail })
        Failures = @(if ($selected.Passed) { } else { "deployment:Test-CountdownValidationDiagnostics.ps1：$($selected.Detail)" })
    }
    $selectedResult | ConvertTo-Json -Depth 10
    if (-not $selected.Passed) { exit 1 }
    exit 0
}

function Test-RequiredPath {
    param([Parameter(Mandatory)] [string] $RelativePath)
    $path = Join-Path $repoRoot $RelativePath
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    $detail = if ($exists) { 'exists' } else { 'missing' }
    Add-ToolingCheck -Name "path:$RelativePath" -Passed $exists -Detail $detail
}

foreach ($requiredPath in @(
        'Countdown.CSharp.Tests.ExternalClose.ps1',
        'global.json',
        'README.md',
        'docs/agents/issue-acceptance.json',
        'docs/agents/issue-acceptance.md',
        'tools/Test-IssueAcceptance.ps1',
        'tools/Invoke-IssueWriteback.ps1',
        'tools/Test-IssueWriteback.ps1',
        'tools/Test-CountdownPublicationDiff.ps1',
        'tools/Test-CountdownPublicationDiff.Tests.ps1',
        'tools/Test-CountdownUtf8BomDrift.ps1',
        'tools/Invoke-CountdownPreCommit.ps1',
        'tools/Test-DesignTokenDrift.ps1',
        'tools/Test-CountdownBuildPreflight.ps1',
        'tools/Countdown.BuildPreflight.psm1',
        'tools/Invoke-CountdownCoreSegment.ps1',
        'tools/Test-CountdownCoreSegment.ps1',
        'tools/Countdown.SuiteRun.psm1',
        'tools/Countdown.SuiteStages.psm1',
        'tools/Invoke-CountdownSuiteLowImpact.ps1',
        'tools/Countdown.IdleProcessorMeasurement.psm1',
        'tools/Measure-CountdownIdleProcessor.ps1',
        'tools/Invoke-CountdownIdleMeasurementChild.ps1',
        'tools/Test-CountdownIdleProcessorMeasurement.ps1',
        'tools/Test-CountdownIdleMeasurementChild.ps1',
        'tools/Test-CountdownIdleMeasurementChildFixture.ps1',
        'tools/Get-CountdownDeploymentState.ps1',
        'tools/Test-CountdownWindowPreflight.ps1',
        'tools/PopupValidationContract.cs'
    )) { Test-RequiredPath -RelativePath $requiredPath }

$schemaTest = & (Join-Path $PSScriptRoot 'Test-IssueAcceptance.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
$schemaPathAliasOk = (Get-Command (Join-Path $PSScriptRoot 'Test-IssueAcceptance.ps1')).Parameters['RecordPath'].Aliases -contains 'Path'
Add-ToolingCheck -Name 'issue-acceptance:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $schemaTest.Valid -and $schemaPathAliasOk) -Detail ("$($schemaTest | ConvertTo-Json -Compress); pathAlias=$schemaPathAliasOk")
$writebackTest = & (Join-Path $PSScriptRoot 'Invoke-IssueWriteback.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
$writebackPathAliasOk = (Get-Command (Join-Path $PSScriptRoot 'Invoke-IssueWriteback.ps1')).Parameters['RecordPath'].Aliases -contains 'Path'
Add-ToolingCheck -Name 'issue-writeback:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $writebackTest.Valid -and $writebackPathAliasOk) -Detail ("$($writebackTest | ConvertTo-Json -Compress); pathAlias=$writebackPathAliasOk")
$writebackRegression = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-IssueWriteback.ps1') -Json | ConvertFrom-Json -Depth 20
Add-ToolingCheck -Name 'issue-writeback:regression' -Passed ($LASTEXITCODE -eq 0 -and [bool] $writebackRegression.Valid) -Detail ($writebackRegression | ConvertTo-Json -Compress -Depth 20)
$diffRegression = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownPublicationDiff.Tests.ps1') -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'publication-diff:regression' -Passed ($LASTEXITCODE -eq 0 -and [bool] $diffRegression.Valid) -Detail ($diffRegression | ConvertTo-Json -Compress -Depth 10)
$designTokenSelfTest = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-DesignTokenDrift.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'design-token:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $designTokenSelfTest.Valid) -Detail ($designTokenSelfTest | ConvertTo-Json -Compress -Depth 10)
$designTokenDrift = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-DesignTokenDrift.ps1') -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'design-token:drift' -Passed ($LASTEXITCODE -eq 0 -and [bool] $designTokenDrift.Valid) -Detail ($designTokenDrift | ConvertTo-Json -Compress -Depth 10)
$coreSegmentSelfTest = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownCoreSegment.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'core-segment:self-test' -Passed ($LASTEXITCODE -eq 0) -Detail (($coreSegmentSelfTest -join ' ') -replace '\s+', ' ')
$idleProcessorSelfTest = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownIdleProcessorMeasurement.ps1')) -join [Environment]::NewLine
$idleProcessorReport = $idleProcessorSelfTest | ConvertFrom-Json -Depth 8
Add-ToolingCheck -Name 'idle-processor:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $idleProcessorReport.Valid) -Detail (($idleProcessorReport.Failures -join '; '))

$validationDiagnostics = Invoke-ToolingValidationDiagnostics
Add-ToolingCheck -Name 'deployment:Test-CountdownValidationDiagnostics.ps1' -Passed $validationDiagnostics.Passed -Detail $validationDiagnostics.Detail
foreach ($test in @('Test-CountdownWindowPreflightContract.ps1', 'Test-CountdownDeploymentState.ps1')) {
    $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot $test), '-Json')
    if ($test -eq 'Test-CountdownDeploymentState.ps1' -and $env:GITHUB_ACTIONS -eq 'true') {
        $arguments += '-SkipDiagnosticsWhenUnavailable'
    }
    $report = & pwsh @arguments | ConvertFrom-Json -Depth 12 -ErrorAction Stop
    Add-ToolingCheck -Name "deployment:$test" -Passed ($LASTEXITCODE -eq 0 -and [bool]$report.Valid) -Detail ($report | ConvertTo-Json -Compress -Depth 12)
}

# CI 单独成步的测试也随 Tooling 跑，本机全绿才代表推上去 CI 不会因为只在 CI 跑的测试变红。
$transferLedgerRegression = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $repoRoot 'Countdown.TransferLedger.Tests.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'transfer-ledger:regression' -Passed ($LASTEXITCODE -eq 0) -Detail (($transferLedgerRegression -join ' ') -replace '\s+', ' ')
$officeDeploymentStatus = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-OfficeDeploymentStatus.ps1') -Json 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'deployment:Test-OfficeDeploymentStatus.ps1' -Passed ($LASTEXITCODE -eq 0) -Detail (($officeDeploymentStatus -join ' ') -replace '\s+', ' ')

# CI 用 -File 或 & 跑的每个脚本，本机都要有启动器阶段跑到：启动器的阶段脚本，加上本脚本用 Join-Path 跑的脚本。
function Get-JoinPathScriptLeaves {
    param([Parameter(Mandatory)] [string] $Path)
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref] $tokens, [ref] $errors)
    $ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -eq 'Join-Path' -and
            $node.CommandElements[-1] -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            $node.CommandElements[-1].Value -like '*.ps1'
        }, $true) | ForEach-Object { ($_.CommandElements[-1].Value -split '[\\/]')[-1] }
}

function Get-CiWorkflowScripts {
    param([Parameter(Mandatory)] [string] $WorkflowText)
    foreach ($match in [regex]::Matches($WorkflowText, '(?:-File\s+|&\s+)(?<path>[\w.\\/-]+\.ps1)\b')) {
        ($match.Groups['path'].Value -split '[\\/]')[-1]
    }
}

$ciParitySelfTestFailures = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(
        @{ Name = 'file-uncovered'; Gaps = 1; Text = '& pwsh.exe -NoLogo -File .\tools\Missing.ps1 -Json' },
        @{ Name = 'file-covered'; Gaps = 0; Text = '& pwsh.exe -NoLogo -File ./Covered.ps1 -Only' },
        @{ Name = 'call-uncovered'; Gaps = 1; Text = '$range = & .\tools\Missing.ps1 -Json' },
        @{ Name = 'native-exe'; Gaps = 0; Text = '& pwsh.exe -NoLogo -NoProfile' }
    )) {
    $caseGaps = @(Get-CiWorkflowScripts -WorkflowText $case.Text | Where-Object { $_ -ne 'Covered.ps1' }).Count
    if ($caseGaps -ne $case.Gaps) { $ciParitySelfTestFailures.Add("self-test $($case.Name): expected $($case.Gaps) gap(s), got $caseGaps") }
}
$locallyRunScripts = @(
    'Invoke-CountdownSuiteLowImpact.ps1'
    Get-JoinPathScriptLeaves -Path (Join-Path $PSScriptRoot 'Invoke-CountdownSuiteLowImpact.ps1')
    Get-JoinPathScriptLeaves -Path $PSCommandPath
) | Sort-Object -Unique
$ciScripts = @(Get-CiWorkflowScripts -WorkflowText ([System.IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/ci.yml'))) | Sort-Object -Unique)
$ciParityProblems = @($ciParitySelfTestFailures) + @($ciScripts | Where-Object { $_ -notin $locallyRunScripts } | ForEach-Object {
        "CI 跑 $_，本机没有启动器阶段跑它：加进启动器阶段，或在本脚本里随 Tooling 跑"
    })
$ciParityDetail = if ($ciParityProblems.Count -eq 0) { "CI 的 $($ciScripts.Count) 个脚本都有本机阶段跑到；自检 4 例通过" } else { $ciParityProblems -join ' | ' }
Add-ToolingCheck -Name 'ci:local-parity' -Passed ($ciParityProblems.Count -eq 0) -Detail $ciParityDetail

$parseErrors = [System.Collections.Generic.List[string]]::new()
$scriptPaths = @(@(
    Get-ChildItem -LiteralPath $repoRoot -Filter '*.ps1' -File
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools') -Filter '*.ps1' -File
) | Sort-Object FullName -Unique)
foreach ($scriptPath in $scriptPaths) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($scriptPath.FullName, [ref] $tokens, [ref] $errors) | Out-Null
    if ($errors.Count -gt 0) {
        $messages = @($errors | ForEach-Object { $_.Message }) -join '; '
        $parseErrors.Add("$($scriptPath.FullName): $messages")
    }
}
$parseDetail = if ($parseErrors.Count -eq 0) { "$($scriptPaths.Count) scripts" } else { $parseErrors -join ' | ' }
Add-ToolingCheck -Name 'powershell:parse' -Passed ($parseErrors.Count -eq 0) -Detail $parseDetail

# 弹层验证夹具的 C# 否则只在有窗口的 ValidationHarness 阶段才编译。这里在子 pwsh 里用夹具同一组引用只编译、
# 不加载被测程序集也不开窗口，让 C# 错误在任何窗口阶段之前暴露。
$harnessCompile = @(& pwsh -NoLogo -NoProfile -NonInteractive -Command {
        param([string] $SourcePath)
        Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
        $references = @(
            Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' |
                Where-Object Name -ne 'WindowsBase.dll' |
                Select-Object -ExpandProperty FullName
            [System.Windows.Window].Assembly.Location
            [System.Windows.UIElement].Assembly.Location
            [System.Windows.Threading.Dispatcher].Assembly.Location
            [System.Windows.Markup.IQueryAmbient].Assembly.Location
        )
        $compileErrors = @()
        try {
            Add-Type -ReferencedAssemblies $references -TypeDefinition ([System.IO.File]::ReadAllText($SourcePath)) -ErrorAction SilentlyContinue -ErrorVariable compileErrors
        }
        catch { $compileErrors += $_ }
        if ($compileErrors.Count -gt 0) { $compileErrors | ForEach-Object { "$_" } } else { 'compiled' }
    } -args (Join-Path $PSScriptRoot 'PopupValidationContract.cs') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'popup-harness:compile' -Passed ($LASTEXITCODE -eq 0 -and $harnessCompile.Count -eq 1 -and $harnessCompile[0] -eq 'compiled') -Detail ($harnessCompile -join ' | ')

# 按文本读子进程的重定向流时必须在同一作用域显式设置该流的编码：不设置就按控制台代码页（中文系统为 GBK）
# 解码，UTF-8 子进程的中文诊断会乱码并丢字节。只读 BaseStream 字节的流不受影响。范围是测试与 tools 脚本。
function Get-EnclosingFunctionAst {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node)
    $current = $Node.Parent
    while ($null -ne $current -and $current -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) {
        $current = $current.Parent
    }
    $current
}

function Get-ProcessCaptureEncodingGaps {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Ast)
    $encodingAssignments = @($Ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Left.Member.Extent.Text -in @('StandardOutputEncoding', 'StandardErrorEncoding')
            }, $true))
    $invocations = $Ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true)
    foreach ($invocation in $invocations) {
        $method = $invocation.Member.Extent.Text
        $stream = $null
        if ($method -eq 'BeginOutputReadLine') { $stream = 'StandardOutput' }
        elseif ($method -eq 'BeginErrorReadLine') { $stream = 'StandardError' }
        elseif ($method -match '^(?:ReadToEnd|ReadLine|ReadBlock|Read)(?:Async)?$' -and
            $invocation.Expression -is [System.Management.Automation.Language.MemberExpressionAst] -and
            $invocation.Expression.Member.Extent.Text -in @('StandardOutput', 'StandardError')) {
            $stream = $invocation.Expression.Member.Extent.Text
        }
        if ($null -eq $stream) { continue }
        $scope = Get-EnclosingFunctionAst $invocation
        $declared = @($encodingAssignments | Where-Object {
                $_.Left.Member.Extent.Text -eq "${stream}Encoding" -and
                [object]::ReferenceEquals((Get-EnclosingFunctionAst $_), $scope)
            }).Count -gt 0
        if (-not $declared) { "第 $($invocation.Extent.StartLineNumber) 行按文本读 $stream，同一作用域未设置 ${stream}Encoding" }
    }
}

$captureSelfTestFailures = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(
        @{ Name = 'missing'; Gaps = 1; Source = 'function F { $s = [Diagnostics.ProcessStartInfo]::new("x"); $s.RedirectStandardError = $true; $p = [Diagnostics.Process]::Start($s); $p.StandardError.ReadToEnd() }' },
        @{ Name = 'declared'; Gaps = 0; Source = 'function F { $s = [Diagnostics.ProcessStartInfo]::new("x"); $s.RedirectStandardError = $true; $s.StandardErrorEncoding = [Text.UTF8Encoding]::new($false); $p = [Diagnostics.Process]::Start($s); $p.StandardError.ReadToEnd() }' },
        @{ Name = 'bytes'; Gaps = 0; Source = 'function F { $s = [Diagnostics.ProcessStartInfo]::new("x"); $s.RedirectStandardOutput = $true; $p = [Diagnostics.Process]::Start($s); $p.StandardOutput.BaseStream.CopyTo([IO.MemoryStream]::new()) }' },
        @{ Name = 'other-scope'; Gaps = 1; Source = 'function A { $s.StandardErrorEncoding = $e }; function B { $p.StandardError.ReadToEndAsync() }' }
    )) {
    $tokens = $null
    $errors = $null
    $caseAst = [System.Management.Automation.Language.Parser]::ParseInput($case.Source, [ref] $tokens, [ref] $errors)
    $caseGaps = @(Get-ProcessCaptureEncodingGaps -Ast $caseAst).Count
    if ($caseGaps -ne $case.Gaps) { $captureSelfTestFailures.Add("self-test $($case.Name): expected $($case.Gaps) gap(s), got $caseGaps") }
}
$captureGaps = [System.Collections.Generic.List[string]]::new()
$capturePaths = @(@(
    Get-ChildItem -LiteralPath $repoRoot -File | Where-Object { $_.Name -like '*.Tests.ps1' -or $_.Name -like '*.Tests.*.ps1' }
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools') -File | Where-Object Extension -in @('.ps1', '.psm1')
) | Sort-Object FullName -Unique)
foreach ($capturePath in $capturePaths) {
    $tokens = $null
    $errors = $null
    $captureAst = [System.Management.Automation.Language.Parser]::ParseFile($capturePath.FullName, [ref] $tokens, [ref] $errors)
    foreach ($gap in @(Get-ProcessCaptureEncodingGaps -Ast $captureAst)) {
        $captureGaps.Add("$([System.IO.Path]::GetRelativePath($repoRoot, $capturePath.FullName))：$gap")
    }
}
$captureProblems = @($captureSelfTestFailures) + @($captureGaps)
$captureDetail = if ($captureProblems.Count -eq 0) { "$($capturePaths.Count) 个测试与 tools 脚本；自检 4 例通过" } else { $captureProblems -join ' | ' }
Add-ToolingCheck -Name 'process-capture:explicit-encoding' -Passed ($captureProblems.Count -eq 0) -Detail $captureDetail

# 日间纯装饰图的生成器与仓库 PNG 契约（纯净度、装饰带不变、重建不动点、确定性）只用 System.Drawing、
# 不开窗口；放在这里随每次 Tooling 与 CI 执行，避免它再次无人运行而静默失效。
$ornamentScene = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-MainDayOrnamentScene.ps1') -ProjectRoot $repoRoot 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'ornament:day-scene' -Passed ($LASTEXITCODE -eq 0) -Detail (($ornamentScene -join ' ') -replace '\s+', ' ')

# 隔离启动器遇到崩溃对话框要立即结束进程，不挂到阶段超时；对话框只弹在自建的隔离桌面上。
$crashDialogWatchdog = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-IsolatedCrashDialogWatchdog.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'isolation:crash-dialog-watchdog' -Passed ($LASTEXITCODE -eq 0) -Detail (($crashDialogWatchdog -join ' ') -replace '\s+', ' ')

# 套件状态原子写遇到瞬时占用要重试，一直被占要在有限时间内放弃；不开窗口。
$suiteRunStateWrite = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-SuiteRunStateWrite.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'suite-run:state-write-retry' -Passed ($LASTEXITCODE -eq 0) -Detail (($suiteRunStateWrite -join ' ') -replace '\s+', ' ')

# 套件截图留存只在临时日志树经受管回收移除测试夹具：30 次边界、摘要/部署引用保护、当前运行和清理失败隔离。
$suiteScreenshotRetention = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-SuiteScreenshotRetention.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'suite-run:screenshot-retention' -Passed ($LASTEXITCODE -eq 0) -Detail (($suiteScreenshotRetention -join ' ') -replace '\s+', ' ')

# 启动器打印的失败摘要：混合编码的 err 日志逐行解码、取最后一个错误块；不开窗口。
$suiteStageExcerpt = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-SuiteStageFailureExcerpt.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'suite-run:failure-excerpt' -Passed ($LASTEXITCODE -eq 0) -Detail (($suiteStageExcerpt -join ' ') -replace '\s+', ' ')

# 启动器后台负载预检用的采样 module：进程起落时只丢无效实例、缺整类样本才重取，读不全时带上错误原文；经 module 注入假计数器
# 与假等待，不读真实计数器，不开窗口。
$suiteLoadPreflight = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-SuiteLoadPreflight.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'suite-run:load-preflight' -Passed ($LASTEXITCODE -eq 0) -Detail (($suiteLoadPreflight -join ' ') -replace '\s+', ' ')

# 视觉基线留存与历史比对只在独立临时 fixture 中执行；不读真实 assets 做删除反例，不开窗口。
$visualBaselineRetention = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-VisualBaselineRetention.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'visual-baseline:previous-version-retention' -Passed ($LASTEXITCODE -eq 0) -Detail (($visualBaselineRetention -join ' ') -replace '\s+', ' ')
$visualBaselineCompare = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CompareCountdownVisualBaseline.ps1') 2>&1 | ForEach-Object { "$_" })
Add-ToolingCheck -Name 'visual-baseline:compare-from-commit' -Passed ($LASTEXITCODE -eq 0) -Detail (($visualBaselineCompare -join ' ') -replace '\s+', ' ')

$evidenceRegression = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownPublicationEvidence.Tests.ps1') -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'publication-evidence:regression' -Passed ($LASTEXITCODE -eq 0 -and $evidenceRegression.Valid) -Detail ($evidenceRegression | ConvertTo-Json -Compress -Depth 10)
$publicationEvidence = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownPublicationEvidence.ps1') -RepositoryRoot $repoRoot -Source $EvidenceSource -Commit $EvidenceCommit -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'docs:local-links' -Passed $publicationEvidence.Valid -Detail ($publicationEvidence | ConvertTo-Json -Compress -Depth 10)
$completionRegression = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-SuiteCompletion.ps1') -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'suite-run:closeout-contract' -Passed ($LASTEXITCODE -eq 0 -and $completionRegression.Valid) -Detail ($completionRegression | ConvertTo-Json -Compress -Depth 10)

# Keep the positive fixture contract aligned with the current visual baseline.
# The Core suite has a separate manifest-content check; this cheap source-tree
# check catches a stale 8/4 (or other hard-coded) fixture before any windowed
# stage starts. The manifests are located from the single C# version constant,
# so a missing current manifest fails here instead of matching nothing.
Import-Module (Join-Path $repoRoot 'Countdown.Validation.psm1') -Force
$visualBaseline = Get-CountdownVisualBaseline -RepositoryRoot $repoRoot
$coreTestSource = @(
    Get-ChildItem -LiteralPath $repoRoot -File |
        Where-Object { $_.Name -eq 'Countdown.Core.Tests.ps1' -or $_.Name -like 'Countdown.Core.Tests.*.ps1' } |
        ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }
) -join "`n"
$baselineFixtureFailures = [System.Collections.Generic.List[string]]::new()
$baselineFixtureAgreements = [System.Collections.Generic.List[string]]::new()
$fixtureDefinitions = [regex]::Matches(
    $coreTestSource,
    '(?m)(?<start>\d+)\.\.(?<end>\d+)\s*\|\s*ForEach-Object\s*\{\s*\[pscustomobject\]@\{\s*CaptureId\s*=\s*["''](?<fixtureSurface>main|floating)-\$_["'']'
)
foreach ($surface in @(
        [pscustomobject]@{ Fixture = 'floating'; Baseline = $visualBaseline.Floating },
        [pscustomobject]@{ Fixture = 'main'; Baseline = $visualBaseline.Main })) {
    $relativeDirectory = $surface.Baseline.RelativeDirectory
    if (-not (Test-Path -LiteralPath $surface.Baseline.ManifestPath -PathType Leaf)) {
        $baselineFixtureFailures.Add("missing current manifest: $relativeDirectory/manifest.json (after Set-CountdownBaselineVersion, run the launcher's BaselineGenerate stage before Tooling)")
        continue
    }
    $manifest = Get-Content -LiteralPath $surface.Baseline.ManifestPath -Raw | ConvertFrom-Json -Depth 100
    $expectedCount = @($manifest.captures).Count
    $fixtureMatches = @($fixtureDefinitions | Where-Object { $_.Groups['fixtureSurface'].Value -eq $surface.Fixture })
    if ($fixtureMatches.Count -ne 1) {
        $baselineFixtureFailures.Add("$relativeDirectory fixture definition count is $($fixtureMatches.Count); expected exactly one")
        continue
    }
    $fixtureStart = [int] $fixtureMatches[0].Groups['start'].Value
    $fixtureEnd = [int] $fixtureMatches[0].Groups['end'].Value
    $fixtureCount = if ($fixtureEnd -ge $fixtureStart) { $fixtureEnd - $fixtureStart + 1 } else { 0 }
    if ($fixtureStart -ne 1 -or $fixtureCount -ne $expectedCount) {
        $baselineFixtureFailures.Add("$relativeDirectory fixture range is $fixtureStart..$fixtureEnd ($fixtureCount captures); manifest declares $expectedCount")
    }
    else {
        $baselineFixtureAgreements.Add("${relativeDirectory}: manifest $expectedCount captures = Core $($surface.Fixture) $fixtureStart..$fixtureEnd")
    }
}
$baselineFixtureDetail = if ($baselineFixtureFailures.Count -eq 0) { $baselineFixtureAgreements -join '; ' } else { $baselineFixtureFailures -join ' | ' }
Add-ToolingCheck -Name 'visual-baseline:fixture-shape' -Passed ($baselineFixtureFailures.Count -eq 0) -Detail $baselineFixtureDetail

# DESIGN.md 里指向现役基线的叙述锚点。Set-CountdownBaselineVersion 只改代码钉点，这几行靠人工跟改：v65 重拍时 §11 只改了
# 第 1 条，悬浮窗现役帧表头和第 2–4 条仍写着 v64。每个标记在 DESIGN.md 恰好一行且写着现役版本；留存条目只核对规则，
# 不再扫描或要求上一版目录仍在工作树。改了这几行的措辞就同步改这里的标记。
$currentBaselineNumber = [int] ($visualBaseline.Version -replace '^.*-v', '')
$currentBaselineShort = "v$currentBaselineNumber"
$designAnchors = [ordered]@{
    '现役五帧与场景对应' = @("$currentBaselineShort 基线", "``$($visualBaseline.Floating.RelativeDirectory)/manifest.json``")
    '真实渲染系列，版本目录为' = @("**$currentBaselineShort**", "``$($visualBaseline.Version)``")
    '主窗口帧规模' = @("$currentBaselineShort 主窗口帧规模", "``$($visualBaseline.Main.RelativeDirectory)/manifest.json``")
    '悬浮窗帧规模' = @("$currentBaselineShort 悬浮窗帧规模")
    '工作树只保留现役版本' = @("只读取 $currentBaselineShort", '历史版本留在提交历史里')
}
$designLines = [System.IO.File]::ReadAllLines((Join-Path $repoRoot 'DESIGN.md'))
$designAnchorFailures = [System.Collections.Generic.List[string]]::new()
foreach ($anchor in $designAnchors.GetEnumerator()) {
    $lineNumbers = @(for ($index = 0; $index -lt $designLines.Count; $index++) {
            if ($designLines[$index].Contains($anchor.Key)) { $index + 1 }
        })
    if ($lineNumbers.Count -ne 1) {
        $designAnchorFailures.Add("「$($anchor.Key)」在 DESIGN.md 出现 $($lineNumbers.Count) 行，应恰好 1 行")
        continue
    }
    # 期望值后紧跟数字不算命中，避免 v6 误配 v66。
    $missing = @($anchor.Value | Where-Object { -not [regex]::IsMatch($designLines[$lineNumbers[0] - 1], [regex]::Escape($_) + '(?![0-9])') })
    if ($missing.Count -gt 0) {
        $designAnchorFailures.Add("DESIGN.md:$($lineNumbers[0])「$($anchor.Key)」缺少 $($missing -join '、')")
    }
}
$designAnchorDetail = if ($designAnchorFailures.Count -eq 0) { "DESIGN.md $($designAnchors.Count) 处锚点指向 $($visualBaseline.Version)，留存规则不依赖旧目录" } else { $designAnchorFailures -join ' | ' }
Add-ToolingCheck -Name 'visual-baseline:design-anchors' -Passed ($designAnchorFailures.Count -eq 0) -Detail $designAnchorDetail

# 窗口探针只走 RunValidationScenarios：控件名、模板部件和窗口字段留在各窗口的场景 partial 里。
# 验证代码里按字符串名取控件的存量只减不增；迁走一块区域后把上限改成实际数。每个类按它的全部 partial 文件合计。
$controlNameLookupPattern = [regex]'Required<[^>]+>\(\s*[A-Za-z_][\w\.]*\s*,\s*"|\.FindName\(\s*"'
$controlNameLookupCeilings = [ordered]@{
    'ValidationReport' = 341
    'ValidationExercises' = 204
}
$controlNameLookupFailures = [System.Collections.Generic.List[string]]::new()
$controlNameLookupCounts = foreach ($entry in $controlNameLookupCeilings.GetEnumerator()) {
    $count = 0
    foreach ($part in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/MinminCountdown') -File |
            Where-Object { $_.Name -eq "$($entry.Key).cs" -or $_.Name -like "$($entry.Key).*.cs" }) {
        $count += $controlNameLookupPattern.Matches([System.IO.File]::ReadAllText($part.FullName)).Count
    }
    if ($count -gt $entry.Value) {
        $controlNameLookupFailures.Add("$($entry.Key) 按名取控件 $count 处，超过上限 $($entry.Value)：新探针写进窗口的场景 partial")
    }
    elseif ($count -lt $entry.Value) {
        $controlNameLookupFailures.Add("$($entry.Key) 按名取控件已降到 $count 处：把上限从 $($entry.Value) 改成 $count")
    }
    "$($entry.Key)=$count"
}
$controlNameLookupDetail = if ($controlNameLookupFailures.Count -eq 0) { $controlNameLookupCounts -join '; ' } else { $controlNameLookupFailures -join ' | ' }
Add-ToolingCheck -Name 'validation:control-name-lookups' -Passed ($controlNameLookupFailures.Count -eq 0) -Detail $controlNameLookupDetail

# 模板自己画焦点的样式要在样式里清掉 FocusVisualStyle，否则键盘获焦时 WPF 另画一层系统默认虚线框（DESIGN.md §4 常驻焦点环）。
# 运行时判定只在本机窗口套件里跑，这里静态拦漏写的 setter：Template 里有 FocusRing、或按 FocusVisualState.IsKeyboardFocusVisible
# 触发的样式，本身或 BasedOn 链上要有 FocusVisualStyle={x:Null}。代码里建的样式不在扫描范围，仍靠运行时判定。
function Get-FocusVisualStyleLeaks {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $XamlByName)
    $presentation = 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    $xamlNamespace = 'http://schemas.microsoft.com/winfx/2006/xaml'
    $styles = @(foreach ($entry in $XamlByName.GetEnumerator()) {
            foreach ($style in ([xml] $entry.Value).GetElementsByTagName('Style', $presentation)) {
                [pscustomobject]@{ File = $entry.Key; Element = $style }
            }
        })
    # BasedOn 只能是 StaticResource：按 x:Key 解析，隐式样式按 TargetType。跨文件解析，主窗样式可以基于 App.xaml 的样式。
    $stylesByKey = @{}
    foreach ($style in $styles) {
        $key = $style.Element.GetAttribute('Key', $xamlNamespace)
        if (-not $key) { $key = $style.Element.GetAttribute('TargetType') -replace '^\{x:Type\s+|\}$', '' }
        if ($key) { $stylesByKey[$key] = $style.Element }
    }
    $setters = { param($element) @($element.ChildNodes | Where-Object { $_.LocalName -eq 'Setter' }) }
    $leaks = [System.Collections.Generic.List[string]]::new()
    $selfDrawing = 0
    foreach ($style in $styles) {
        $template = @(& $setters $style.Element | Where-Object { $_.GetAttribute('Property') -eq 'Template' } |
            ForEach-Object { $_.GetElementsByTagName('ControlTemplate', $presentation) } | Select-Object -First 1)
        if ($template.Count -eq 0) { continue }
        $drawsFocus = @($template[0].SelectNodes('.//*') | Where-Object {
                $_.GetAttribute('Name', $xamlNamespace) -eq 'FocusRing' -or
                $_.GetAttribute('Property') -match 'FocusVisualState\.IsKeyboardFocusVisible$'
            }).Count -gt 0
        if (-not $drawsFocus) { continue }
        $selfDrawing++
        $cleared = $false
        $current = $style.Element
        for ($depth = 0; $null -ne $current -and -not $cleared -and $depth -lt 16; $depth++) {
            $cleared = @(& $setters $current | Where-Object {
                    $_.GetAttribute('Property') -eq 'FocusVisualStyle' -and $_.GetAttribute('Value') -eq '{x:Null}'
                }).Count -gt 0
            $basedOn = [regex]::Match($current.GetAttribute('BasedOn'), '^\{StaticResource\s+(?:\{x:Type\s+)?([^\s{}]+)\}?\}$')
            $current = if ($basedOn.Success) { $stylesByKey[$basedOn.Groups[1].Value] } else { $null }
        }
        if (-not $cleared) {
            $name = $style.Element.GetAttribute('Key', $xamlNamespace)
            if (-not $name) { $name = "TargetType=$($style.Element.GetAttribute('TargetType'))" }
            $leaks.Add("$($style.File) 的样式 $name 模板自己画焦点，却没清掉 FocusVisualStyle")
        }
    }
    [pscustomobject]@{ Leaks = @($leaks); SelfDrawing = $selfDrawing }
}

$focusXamlRoot = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" ' +
    'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:focus="clr-namespace:MinminCountdown">{0}</ResourceDictionary>'
$focusRingTemplate = '<Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="FocusRing" /></ControlTemplate></Setter.Value></Setter>'
$focusTriggerTemplate = '<Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><Border x:Name="Box" />' +
    '<ControlTemplate.Triggers><MultiTrigger><MultiTrigger.Conditions><Condition Property="focus:FocusVisualState.IsKeyboardFocusVisible" Value="True" />' +
    '</MultiTrigger.Conditions></MultiTrigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>'
$focusNullSetter = '<Setter Property="FocusVisualStyle" Value="{x:Null}" />'
$focusVisualSelfTestFailures = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(
        @{ Name = 'missing-setter'; Leaks = 1; Body = "<Style x:Key=""A"" TargetType=""Button"">$focusRingTemplate</Style>" },
        @{ Name = 'has-setter'; Leaks = 0; Body = "<Style x:Key=""A"" TargetType=""Button"">$focusNullSetter$focusRingTemplate</Style>" },
        @{ Name = 'trigger-only'; Leaks = 1; Body = "<Style TargetType=""{x:Type CheckBox}"">$focusTriggerTemplate</Style>" },
        @{ Name = 'based-on'; Leaks = 0; Body = "<Style x:Key=""Base"" TargetType=""Button"">$focusNullSetter</Style><Style x:Key=""Derived"" TargetType=""Button"" BasedOn=""{StaticResource Base}"">$focusRingTemplate</Style>" },
        @{ Name = 'element-binding'; Leaks = 0; Body = '<Style TargetType="Border"><Style.Triggers><MultiDataTrigger><MultiDataTrigger.Conditions><Condition Binding="{Binding Path=(focus:FocusVisualState.IsKeyboardFocusVisible), ElementName=Income}" Value="True" /></MultiDataTrigger.Conditions></MultiDataTrigger></Style.Triggers></Style>' }
    )) {
    $caseLeaks = @((Get-FocusVisualStyleLeaks -XamlByName @{ 'case.xaml' = ($focusXamlRoot -f $case.Body) }).Leaks).Count
    if ($caseLeaks -ne $case.Leaks) { $focusVisualSelfTestFailures.Add("self-test $($case.Name): expected $($case.Leaks), got $caseLeaks") }
}
$focusXaml = [ordered]@{}
foreach ($xamlFile in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/MinminCountdown') -Filter '*.xaml' -File) {
    $focusXaml[$xamlFile.Name] = [System.IO.File]::ReadAllText($xamlFile.FullName)
}
$focusVisualScan = Get-FocusVisualStyleLeaks -XamlByName $focusXaml
$focusVisualProblems = @($focusVisualSelfTestFailures) + @($focusVisualScan.Leaks)
$focusVisualDetail = if ($focusVisualProblems.Count -eq 0) { "扫描 $($focusXaml.Count) 个 XAML，$($focusVisualScan.SelfDrawing) 个自画焦点的样式都清了 FocusVisualStyle；自检 5 例通过" } else { $focusVisualProblems -join ' | ' }
Add-ToolingCheck -Name 'xaml:focus-visual-style-null' -Passed ($focusVisualProblems.Count -eq 0) -Detail $focusVisualDetail

# App.xaml 的隐式 <Style TargetType="{x:Type Window}"> 不会应用到 Window 子类，
# 每个 XAML 窗口根和纯代码 : Window 子类都必须显式绑定 UiFontFamily，避免子类退回系统默认字体。
function Get-WindowUiFontFamilyLeaks {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $XamlByName,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $CodeByName
    )
    $xamlNamespace = 'http://schemas.microsoft.com/winfx/2006/xaml'
    $leaks = [System.Collections.Generic.List[string]]::new()
    $xamlWindowClasses = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $checkedWindows = 0
    foreach ($entry in $XamlByName.GetEnumerator()) {
        $doc = [xml] $entry.Value
        $root = $doc.DocumentElement
        if ($null -eq $root -or $root.LocalName -ne 'Window') { continue }
        $checkedWindows++
        $className = $root.GetAttribute('Class', $xamlNamespace)
        if (-not [string]::IsNullOrWhiteSpace($className)) {
            [void] $xamlWindowClasses.Add(($className -split '\.')[-1])
        }
        $fontFamily = $root.GetAttribute('FontFamily')
        if ($fontFamily -notmatch '^\{(DynamicResource|StaticResource)\s+UiFontFamily\}$') {
            $leaks.Add("$($entry.Key) 的 <Window> 根缺少 FontFamily=""{DynamicResource UiFontFamily}""")
        }
    }
    $classDeclPattern = [regex]'(?m)\bclass\s+(?<name>[A-Za-z_]\w*)\b'
    $windowSubclassPattern = [regex]'(?m)\bclass\s+(?<name>[A-Za-z_]\w*)\s*:\s*(?:System\.Windows\.)?Window\b'
    $fontBindingPattern = [regex]'SetResourceReference\s*\(\s*(?:(?:Window|Control|TextElement)\.)?FontFamilyProperty\s*,\s*"UiFontFamily"\s*\)|FontFamily\s*=\s*\(FontFamily\)FindResource\(\s*"UiFontFamily"\s*\)'
    foreach ($entry in $CodeByName.GetEnumerator()) {
        $source = [string] $entry.Value
        $allClasses = @($classDeclPattern.Matches($source))
        foreach ($match in $windowSubclassPattern.Matches($source)) {
            $name = $match.Groups['name'].Value
            if ($xamlWindowClasses.Contains($name)) { continue }
            $checkedWindows++
            $nextClass = $allClasses | Where-Object { $_.Index -gt $match.Index } | Select-Object -First 1
            $sliceLength = if ($null -ne $nextClass) { $nextClass.Index - $match.Index } else { $source.Length - $match.Index }
            $slice = $source.Substring($match.Index, $sliceLength)
            if (-not $fontBindingPattern.IsMatch($slice)) {
                $leaks.Add("$($entry.Key) 的 Window 子类 $name 缺少 UiFontFamily 资源绑定（SetResourceReference(Window.FontFamilyProperty, ""UiFontFamily"")）")
            }
        }
    }
    [pscustomobject]@{ Leaks = @($leaks); CheckedWindows = $checkedWindows }
}

$windowFontSelfTestFailures = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(
        @{ Name = 'xaml-missing'; Leaks = 1; Xaml = @{ 'A.xaml' = '<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="App.WinA" />' }; Code = @{ 'A.xaml.cs' = 'public sealed partial class WinA : Window { }' } },
        @{ Name = 'xaml-valid'; Leaks = 0; Xaml = @{ 'A.xaml' = '<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="App.WinA" FontFamily="{DynamicResource UiFontFamily}" />' }; Code = @{ 'A.xaml.cs' = 'public sealed partial class WinA : Window { }' } },
        @{ Name = 'code-missing'; Leaks = 1; Xaml = @{}; Code = @{ 'B.cs' = 'internal sealed class WinB : Window { public WinB() { Title = "B"; } }' } },
        @{ Name = 'code-valid'; Leaks = 0; Xaml = @{}; Code = @{ 'B.cs' = 'internal sealed class WinB : Window { public WinB() { SetResourceReference(Window.FontFamilyProperty, "UiFontFamily"); } }' } },
        @{ Name = 'two-classes-second-missing'; Leaks = 1; Xaml = @{}; Code = @{ 'Pair.cs' = 'internal sealed class Win1 : Window { public Win1() { SetResourceReference(Window.FontFamilyProperty, "UiFontFamily"); } } internal sealed class Win2 : Window { public Win2() { Title = "2"; } }' } }
    )) {
    $caseLeaks = @((Get-WindowUiFontFamilyLeaks -XamlByName $case.Xaml -CodeByName $case.Code).Leaks).Count
    if ($caseLeaks -ne $case.Leaks) { $windowFontSelfTestFailures.Add("self-test $($case.Name): expected $($case.Leaks), got $caseLeaks") }
}
$windowCodeByName = [ordered]@{}
foreach ($csFile in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/MinminCountdown') -Filter '*.cs' -File) {
    $windowCodeByName[$csFile.Name] = [System.IO.File]::ReadAllText($csFile.FullName)
}
$windowFontScan = Get-WindowUiFontFamilyLeaks -XamlByName $focusXaml -CodeByName $windowCodeByName
$windowFontProblems = @($windowFontSelfTestFailures) + @($windowFontScan.Leaks)
$windowFontDetail = if ($windowFontProblems.Count -eq 0) { "检查 $($windowFontScan.CheckedWindows) 个 Window 子类均显式绑定 UiFontFamily；自检 5 例通过" } else { $windowFontProblems -join ' | ' }
Add-ToolingCheck -Name 'xaml:window-uifont-family' -Passed ($windowFontProblems.Count -eq 0) -Detail $windowFontDetail

# Move-Item -Force 第一次改名撞上读句柄就先删目标再改名：失败时旧文件已经没了（安装 journal 与启动回执都这样坏过）。
# 生产脚本里的存量只减不增；替换文件用 [System.IO.File]::Move(源, 目标, $true) 一次改名，撞上占用时有界退避重试。
function Get-ForcedMoveItemCount {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Ast)
    @($Ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -in @('Move-Item', 'move', 'mi', 'mv') -and
                @($node.CommandElements | Where-Object {
                        $_ -is [System.Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -eq 'Force'
                    }).Count -gt 0
            }, $true)).Count
}

$forcedMoveSelfTestFailures = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(
        @{ Name = 'code'; Count = 1; Source = 'Move-Item -LiteralPath $a -Destination $b -Force' },
        @{ Name = 'alias'; Count = 1; Source = 'mv $a $b -Force' },
        @{ Name = 'no-force'; Count = 0; Source = 'Move-Item -LiteralPath $a -Destination $b -ErrorAction Stop' },
        @{ Name = 'comment'; Count = 0; Source = '# Move-Item -Force 会先删目标再改名' },
        @{ Name = 'string'; Count = 0; Source = '$text = ''Move-Item -LiteralPath $a -Destination $b -Force''' }
    )) {
    $tokens = $null
    $errors = $null
    $caseCount = Get-ForcedMoveItemCount -Ast ([System.Management.Automation.Language.Parser]::ParseInput($case.Source, [ref] $tokens, [ref] $errors))
    if ($caseCount -ne $case.Count) { $forcedMoveSelfTestFailures.Add("self-test $($case.Name): expected $($case.Count), got $caseCount") }
}
$forcedMoveCeilings = @{
    'Countdown.InstallLifecycle.psm1' = 2
    'Countdown.Runtime.psm1' = 1
    'Countdown.Settings.psm1' = 1
}
$forcedMoveFailures = [System.Collections.Generic.List[string]]::new()
$forcedMovePaths = @(@(
    Get-ChildItem -LiteralPath $repoRoot -File
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools') -File
) | Where-Object {
    $_.Extension -in @('.ps1', '.psm1') -and $_.Name -notlike '*.Tests.ps1' -and $_.Name -notlike '*.Tests.*.ps1' -and
    -not ($_.Directory.Name -eq 'tools' -and $_.Name -like 'Test-*.ps1')
})
$forcedMoveCounts = foreach ($forcedMovePath in $forcedMovePaths) {
    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $forcedMovePath.FullName).Replace('\', '/')
    $tokens = $null
    $errors = $null
    $count = Get-ForcedMoveItemCount -Ast ([System.Management.Automation.Language.Parser]::ParseFile($forcedMovePath.FullName, [ref] $tokens, [ref] $errors))
    $ceiling = if ($forcedMoveCeilings.ContainsKey($relativePath)) { $forcedMoveCeilings[$relativePath] } else { 0 }
    if ($count -gt $ceiling) {
        $forcedMoveFailures.Add("$relativePath 有 $count 处 Move-Item -Force，超过上限 ${ceiling}：改用 [System.IO.File]::Move(源, 目标, `$true) 并对占用做有界退避重试")
    }
    elseif ($count -lt $ceiling) {
        $forcedMoveFailures.Add("$relativePath 的 Move-Item -Force 已降到 $count 处：把上限从 $ceiling 改成 $count")
    }
    if ($count -gt 0) { "$relativePath=$count" }
}
foreach ($ceilingPath in $forcedMoveCeilings.Keys) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $ceilingPath) -PathType Leaf)) {
        $forcedMoveFailures.Add("上限表里的 $ceilingPath 已不存在：删掉这一项")
    }
}
$forcedMoveProblems = @($forcedMoveSelfTestFailures) + @($forcedMoveFailures)
$forcedMoveDetail = if ($forcedMoveProblems.Count -eq 0) { "$(@($forcedMoveCounts) -join '; ')；扫描 $($forcedMovePaths.Count) 个生产脚本；自检 5 例通过" } else { $forcedMoveProblems -join ' | ' }
Add-ToolingCheck -Name 'powershell:forced-move-item' -Passed ($forcedMoveProblems.Count -eq 0) -Detail $forcedMoveDetail

# 外部脚本与子进程调用守卫：禁止引用已退役的 invoke-safe-tree-delete 批处理 shim，也禁止把 *.ps1
# 直接传给 Start-Process -FilePath（Windows 下非可执行文件会报「%1 不是有效的 Win32 应用程序」）。
function Get-ExternalScriptInvocationIssues {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Ast)
    $retiredShimPattern = 'invoke-safe-tree-delete' + '\.cmd'
    $issues = [System.Collections.Generic.List[string]]::new()
    foreach ($strNode in $Ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
                $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]
            }, $true)) {
        if ($strNode.Value -match $retiredShimPattern) {
            $issues.Add("第 $($strNode.Extent.StartLineNumber) 行引用已退役 shim（改用 pwsh.exe -File Invoke-SafeTreeDelete.ps1）")
        }
    }
    foreach ($cmd in $Ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -in @('Start-Process', 'saps', 'start')
            }, $true)) {
        $elements = $cmd.CommandElements
        $filePathElement = $null
        for ($i = 1; $i -lt $elements.Count; $i++) {
            $el = $elements[$i]
            if ($el -is [System.Management.Automation.Language.CommandParameterAst]) {
                if ($el.ParameterName -in @('FilePath', 'File', 'Path')) {
                    if ($null -ne $el.Argument) { $filePathElement = $el.Argument; break }
                    elseif ($i + 1 -lt $elements.Count) { $filePathElement = $elements[$i + 1]; break }
                }
                elseif ($null -eq $el.Argument -and $el.ParameterName -notin @('NoNewWindow', 'PassThru', 'Wait', 'LoadUserProfile', 'UseNewEnvironment')) {
                    $i++
                }
            }
            elseif ($null -eq $filePathElement) {
                $filePathElement = $el
                break
            }
        }
        if ($filePathElement -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
            $filePathElement -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
            if ($filePathElement.Value.Trim() -match '\.ps1$') {
                $issues.Add("第 $($cmd.Extent.StartLineNumber) 行将 .ps1 直接传给 $($cmd.GetCommandName())（应改用 pwsh.exe -File）")
            }
        }
    }
    @($issues)
}

$retiredShimSample = 'invoke-safe-tree-delete' + '.cmd'
$externalInvocationSelfTestFailures = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(
        @{ Name = 'retired-cmd'; Issues = 1; Source = "`$p = 'D:\Codex\.codex\scripts\$retiredShimSample'" },
        @{ Name = 'active-ps1'; Issues = 0; Source = "`$p = 'D:\Codex\.codex\scripts\Invoke-SafeTreeDelete.ps1'" },
        @{ Name = 'start-process-named-ps1'; Issues = 1; Source = "Start-Process -FilePath '.\tools\Run.ps1'" },
        @{ Name = 'start-process-positional-ps1'; Issues = 1; Source = "saps '.\tools\Run.ps1' -NoNewWindow" },
        @{ Name = 'start-process-pwsh-file'; Issues = 0; Source = "Start-Process -FilePath `$pwsh -ArgumentList @('-File', '.\tools\Run.ps1')" },
        @{ Name = 'comment-only'; Issues = 0; Source = "# Start-Process -FilePath foo.ps1 $retiredShimSample" }
    )) {
    $tokens = $null
    $errors = $null
    $caseIssues = @(Get-ExternalScriptInvocationIssues -Ast ([System.Management.Automation.Language.Parser]::ParseInput($case.Source, [ref] $tokens, [ref] $errors))).Count
    if ($caseIssues -ne $case.Issues) { $externalInvocationSelfTestFailures.Add("self-test $($case.Name): expected $($case.Issues), got $caseIssues") }
}
$externalInvocationFailures = [System.Collections.Generic.List[string]]::new()
$externalInvocationPaths = @(@(
    Get-ChildItem -LiteralPath $repoRoot -File
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools') -File
) | Where-Object { $_.Extension -in @('.ps1', '.psm1') } | Sort-Object FullName -Unique)
foreach ($scriptFile in $externalInvocationPaths) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref] $tokens, [ref] $errors)
    foreach ($issue in @(Get-ExternalScriptInvocationIssues -Ast $ast)) {
        $externalInvocationFailures.Add("$([System.IO.Path]::GetRelativePath($repoRoot, $scriptFile.FullName))：$issue")
    }
}
$externalInvocationProblems = @($externalInvocationSelfTestFailures) + @($externalInvocationFailures)
$externalInvocationDetail = if ($externalInvocationProblems.Count -eq 0) { "扫描 $($externalInvocationPaths.Count) 个脚本无退役 shim 或裸 Start-Process *.ps1；自检 6 例通过" } else { $externalInvocationProblems -join ' | ' }
Add-ToolingCheck -Name 'powershell:external-script-invocation' -Passed ($externalInvocationProblems.Count -eq 0) -Detail $externalInvocationDetail

if ([string]::IsNullOrWhiteSpace($BaseCommit)) { $BaseCommit = 'origin/main' }
if ([string]::IsNullOrWhiteSpace($CandidateCommit)) { $CandidateCommit = 'HEAD' }
$diffCheck = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownPublicationDiff.ps1') -RepositoryRoot $repoRoot -BaseCommit $BaseCommit -CandidateCommit $CandidateCommit -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'git:publication-diff-check' -Passed ($LASTEXITCODE -eq 0 -and [bool] $diffCheck.Valid) -Detail ($diffCheck | ConvertTo-Json -Compress -Depth 10)
$bomSelfTest = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownUtf8BomDrift.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'utf8-bom-drift:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $bomSelfTest.Valid) -Detail ($bomSelfTest | ConvertTo-Json -Compress -Depth 10)
$bomDriftCheck = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownUtf8BomDrift.ps1') -RepositoryRoot $repoRoot -BaseCommit $BaseCommit -CandidateCommit $CandidateCommit -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'git:utf8-bom-drift' -Passed ($LASTEXITCODE -eq 0 -and [bool] $bomDriftCheck.Valid) -Detail ($bomDriftCheck | ConvertTo-Json -Compress -Depth 10)

$testRootDetail = if ($testRootLeaks.Count -eq 0) { '通过的子测试没在 logs\ 与仓库根留下 *-test-* 测试根' } else { "$($testRootLeaks -join ' | ')：断言全部通过后删掉测试根，断言失败时才留作现场" }
Add-ToolingCheck -Name 'tests:leftover-test-roots' -Passed ($testRootLeaks.Count -eq 0) -Detail $testRootDetail

$result = [pscustomobject]@{
    Valid = $failures.Count -eq 0
    Checks = @($checks)
    Failures = @($failures)
}
if ($Json) { $result | ConvertTo-Json -Depth 10 } else {
    $result | ConvertTo-Json -Depth 10
}
if ($failures.Count -gt 0) { exit 1 }
exit 0
