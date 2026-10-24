#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $Json,
    [string] $BaseCommit = $env:COUNTDOWN_DIFF_BASE,
    [string] $CandidateCommit = $env:COUNTDOWN_DIFF_CANDIDATE
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$failures = [System.Collections.Generic.List[string]]::new()
$checks = [System.Collections.Generic.List[object]]::new()

function Add-ToolingCheck {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [bool] $Passed, [string] $Detail = '')
    $checks.Add([pscustomobject]@{ Name = $Name; Status = if ($Passed) { 'processed' } else { 'failed' }; Detail = $Detail })
    if (-not $Passed) { $failures.Add("$Name：$Detail") }
}

function Test-RequiredPath {
    param([Parameter(Mandatory)] [string] $RelativePath)
    $path = Join-Path $repoRoot $RelativePath
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    $detail = if ($exists) { 'exists' } else { 'missing' }
    Add-ToolingCheck -Name "path:$RelativePath" -Passed $exists -Detail $detail
}

foreach ($requiredPath in @(
        'global.json',
        'README.md',
        'docs/agents/issue-acceptance.json',
        'docs/agents/issue-acceptance.md',
        'tools/Test-IssueAcceptance.ps1',
        'tools/Invoke-IssueWriteback.ps1',
        'tools/Test-IssueWriteback.ps1',
        'tools/Test-CountdownPublicationDiff.ps1',
        'tools/Test-CountdownPublicationDiff.Tests.ps1',
        'tools/Test-DesignTokenDrift.ps1',
        'tools/Test-CountdownBuildPreflight.ps1',
        'tools/Countdown.BuildPreflight.psm1',
        'tools/Countdown.SuiteRun.psm1',
        'tools/Invoke-CountdownSuiteLowImpact.ps1',
        'tools/Get-CountdownDeploymentState.ps1',
        'tools/Test-CountdownWindowPreflight.ps1',
        'tools/PopupValidationContract.cs'
    )) { Test-RequiredPath -RelativePath $requiredPath }

$schemaTest = & (Join-Path $PSScriptRoot 'Test-IssueAcceptance.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'issue-acceptance:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $schemaTest.Valid) -Detail ($schemaTest | ConvertTo-Json -Compress)
$writebackTest = & (Join-Path $PSScriptRoot 'Invoke-IssueWriteback.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'issue-writeback:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $writebackTest.Valid) -Detail ($writebackTest | ConvertTo-Json -Compress)
$writebackRegression = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-IssueWriteback.ps1') -Json | ConvertFrom-Json -Depth 20
Add-ToolingCheck -Name 'issue-writeback:regression' -Passed ($LASTEXITCODE -eq 0 -and [bool] $writebackRegression.Valid) -Detail ($writebackRegression | ConvertTo-Json -Compress -Depth 20)
$diffRegression = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownPublicationDiff.Tests.ps1') -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'publication-diff:regression' -Passed ($LASTEXITCODE -eq 0 -and [bool] $diffRegression.Valid) -Detail ($diffRegression | ConvertTo-Json -Compress -Depth 10)
$designTokenSelfTest = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-DesignTokenDrift.ps1') -SelfTest -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'design-token:self-test' -Passed ($LASTEXITCODE -eq 0 -and [bool] $designTokenSelfTest.Valid) -Detail ($designTokenSelfTest | ConvertTo-Json -Compress -Depth 10)
$designTokenDrift = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-DesignTokenDrift.ps1') -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'design-token:drift' -Passed ($LASTEXITCODE -eq 0 -and [bool] $designTokenDrift.Valid) -Detail ($designTokenDrift | ConvertTo-Json -Compress -Depth 10)

foreach ($test in @('Test-CountdownValidationDiagnostics.ps1',
        'Test-CountdownWindowPreflightContract.ps1', 'Test-CountdownDeploymentState.ps1')) {
    $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot $test), '-Json')
    if ($test -eq 'Test-CountdownDeploymentState.ps1' -and $env:GITHUB_ACTIONS -eq 'true') {
        $arguments += '-SkipDiagnosticsWhenUnavailable'
    }
    $report = & pwsh @arguments | ConvertFrom-Json -Depth 12 -ErrorAction Stop
    Add-ToolingCheck -Name "deployment:$test" -Passed ($LASTEXITCODE -eq 0 -and [bool]$report.Valid) -Detail ($report | ConvertTo-Json -Compress -Depth 12)
}

$parseErrors = [System.Collections.Generic.List[string]]::new()
$scriptPaths = @(
    Get-ChildItem -LiteralPath $repoRoot -Filter '*.ps1' -File
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools') -Filter '*.ps1' -File
) | Sort-Object FullName -Unique
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
$capturePaths = @(
    Get-ChildItem -LiteralPath $repoRoot -Filter '*.Tests.ps1' -File
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools') -File | Where-Object Extension -in @('.ps1', '.psm1')
) | Sort-Object FullName -Unique
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

$docLinkFailures = [System.Collections.Generic.List[string]]::new()
$markdownFiles = Get-ChildItem -LiteralPath $repoRoot -Filter '*.md' -File -Recurse |
    Where-Object {
        $_.FullName -notmatch '\\(?:\.git|bin|obj|app|verification|logs)\\' -and
        $_.FullName -notmatch '\\release-contract-test-[^\\]+\\'
    }
$linkPattern = '\[[^\]]+\]\((?<target>[^)\s]+)\)'
foreach ($markdownFile in $markdownFiles) {
    $markdown = [System.IO.File]::ReadAllText($markdownFile.FullName)
    foreach ($match in [regex]::Matches($markdown, $linkPattern)) {
        $target = [string] $match.Groups['target'].Value
        if ($target -match '^(?:[a-z][a-z0-9+.-]*:|//)' -or $target.StartsWith('#')) { continue }
        $target = $target.Split('#')[0]
        if ([string]::IsNullOrWhiteSpace($target)) { continue }
        $target = [System.Uri]::UnescapeDataString($target).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $resolved = [System.IO.Path]::GetFullPath((Join-Path $markdownFile.DirectoryName $target))
        if (-not (Test-Path -LiteralPath $resolved)) {
            $docLinkFailures.Add("$($markdownFile.FullName): $target")
        }
    }
}
$docLinkDetail = if ($docLinkFailures.Count -eq 0) { "$($markdownFiles.Count) Markdown files" } else { $docLinkFailures -join ' | ' }
Add-ToolingCheck -Name 'docs:local-links' -Passed ($docLinkFailures.Count -eq 0) -Detail $docLinkDetail

# Keep the positive fixture contract aligned with the current visual baseline.
# The Core suite has a separate manifest-content check; this cheap source-tree
# check catches a stale 8/4 (or other hard-coded) fixture before any windowed
# stage starts. The manifests are located from the single C# version constant,
# so a missing current manifest fails here instead of matching nothing.
Import-Module (Join-Path $repoRoot 'Countdown.Validation.psm1') -Force
$visualBaseline = Get-CountdownVisualBaseline -RepositoryRoot $repoRoot
$coreTestSource = [System.IO.File]::ReadAllText((Join-Path $repoRoot 'Countdown.Core.Tests.ps1'))
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
        $baselineFixtureFailures.Add("missing current manifest: $relativeDirectory/manifest.json")
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

if ([string]::IsNullOrWhiteSpace($BaseCommit)) { $BaseCommit = 'origin/main' }
if ([string]::IsNullOrWhiteSpace($CandidateCommit)) { $CandidateCommit = 'HEAD' }
$diffCheck = & pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-CountdownPublicationDiff.ps1') -RepositoryRoot $repoRoot -BaseCommit $BaseCommit -CandidateCommit $CandidateCommit -Json | ConvertFrom-Json -Depth 10
Add-ToolingCheck -Name 'git:publication-diff-check' -Passed ($LASTEXITCODE -eq 0 -and [bool] $diffCheck.Valid) -Detail ($diffCheck | ConvertTo-Json -Compress -Depth 10)

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
