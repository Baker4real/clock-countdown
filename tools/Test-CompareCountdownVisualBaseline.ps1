#requires -Version 7.0
# 比对工具的旧版来源是 Git 提交而非工作树：临时仓库提交 v1 后删除 v1 工作树目录，v1 -> v2 仍应可比。
# 不开窗口，不读取或修改真实 assets；临时 Git 仓库、提交与删除都只存在于独立 fixture。
[CmdletBinding()]
param(
    [string] $FixtureParent = [System.IO.Path]::GetTempPath()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workRoot = Join-Path $FixtureParent ('minmin-visual-baseline-compare-' + [guid]::NewGuid().ToString('N'))
$utf8 = [System.Text.UTF8Encoding]::new($false)
$oldVersion = '2026-11-10-v1'
$currentVersion = '2026-11-11-v2'
$pngBytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=')
$pngSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($pngBytes))

function Write-Manifest {
    param([string] $Root, [string] $Version, [string] $RelativeRoot, [string] $CaptureId)
    $directory = Join-Path (Join-Path $Root $RelativeRoot) $Version
    [void][System.IO.Directory]::CreateDirectory($directory)
    $capturePath = "$RelativeRoot/$Version/$CaptureId.png"
    $manifest = [ordered]@{
        baselineVersion = $Version
        sourceBinding = [ordered]@{ aggregateSha256 = 'SOURCE'; files = @() }
        executable = [ordered]@{ sha256 = 'EXE' }
        captures = @([ordered]@{
            captureId = $CaptureId
            path = $capturePath.Replace('\', '/')
            pngSha256 = $pngSha256
            stateFingerprint = [ordered]@{ State = 'same' }
            width = 1
            height = 1
        })
    }
    [System.IO.File]::WriteAllText((Join-Path $directory 'manifest.json'), ($manifest | ConvertTo-Json -Depth 10), $utf8)
    [System.IO.File]::WriteAllBytes((Join-Path $directory "$CaptureId.png"), $pngBytes)
    return $directory
}

try {
    [void][System.IO.Directory]::CreateDirectory((Join-Path $workRoot 'tools'))
    [void][System.IO.Directory]::CreateDirectory((Join-Path $workRoot 'src/MinminCountdown'))
    [System.IO.File]::Copy((Join-Path $PSScriptRoot 'Compare-CountdownVisualBaseline.ps1'), (Join-Path $workRoot 'tools/Compare-CountdownVisualBaseline.ps1'))
    $validationStub = @'
function Get-CountdownVisualBaseline {
    param([string] $RepositoryRoot)
    $main = 'assets/ui-references/main-window/864x1000/2026-11-11-v2'
    $floating = 'assets/ui-references/floating-window/520x390/2026-11-11-v2'
    [pscustomobject]@{
        Version = '2026-11-11-v2'
        Main = [pscustomobject]@{ RelativeDirectory = $main; ManifestPath = (Join-Path $RepositoryRoot "$main/manifest.json") }
        Floating = [pscustomobject]@{ RelativeDirectory = $floating; ManifestPath = (Join-Path $RepositoryRoot "$floating/manifest.json") }
    }
}
Export-ModuleMember -Function Get-CountdownVisualBaseline
'@
    [System.IO.File]::WriteAllText((Join-Path $workRoot 'Countdown.Validation.psm1'), $validationStub, $utf8)
    [System.IO.File]::WriteAllText((Join-Path $workRoot 'src/MinminCountdown/VisualReferenceBaseline.cs'), 'public const string Version = "2026-11-11-v2";', $utf8)

    $oldDirectories = @(
        Write-Manifest -Root $workRoot -Version $oldVersion -RelativeRoot 'assets/ui-references/main-window/864x1000' -CaptureId 'main-frame'
        Write-Manifest -Root $workRoot -Version $oldVersion -RelativeRoot 'assets/ui-references/floating-window/520x390' -CaptureId 'floating-frame'
    )
    & git -C $workRoot init --quiet
    & git -C $workRoot config user.name 'Fixture'
    & git -C $workRoot config user.email 'fixture@example.invalid'
    & git -C $workRoot add .
    & git -C $workRoot commit --quiet -m 'Add old visual baseline fixture'
    if ($LASTEXITCODE -ne 0) { throw '临时 fixture 提交失败' }

    foreach ($directory in $oldDirectories) {
        [System.IO.Directory]::Delete($directory, $true)
    }
    Write-Manifest -Root $workRoot -Version $currentVersion -RelativeRoot 'assets/ui-references/main-window/864x1000' -CaptureId 'main-frame' | Out-Null
    Write-Manifest -Root $workRoot -Version $currentVersion -RelativeRoot 'assets/ui-references/floating-window/520x390' -CaptureId 'floating-frame' | Out-Null

    $PSNativeCommandUseErrorActionPreference = $false
    $output = @(& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $workRoot 'tools/Compare-CountdownVisualBaseline.ps1') -From $oldVersion 2>&1 | ForEach-Object { "$_" })
    $compareExit = $LASTEXITCODE
    $PSNativeCommandUseErrorActionPreference = $true
    if ($compareExit -ne 0) {
        throw "旧版工作树目录删除后比对应仍通过，exit=$compareExit：$($output -join ' ')"
    }
    if (($output -join "`n") -notmatch 'PNG 逐字节一致 1') {
        throw "比对输出未确认两窗帧一致：$($output -join ' ')"
    }
}
finally {
    $PSNativeCommandUseErrorActionPreference = $true
    if (Test-Path -LiteralPath $workRoot) {
        foreach ($file in Get-ChildItem -LiteralPath $workRoot -Recurse -Force -File) {
            $file.Attributes = [System.IO.FileAttributes]::Normal
        }
        [System.IO.Directory]::Delete($workRoot, $true)
    }
}

Write-Output 'visual-baseline-compare: PASS（旧版仅从临时 Git 提交恢复，工作树旧目录已删除，双窗比对仍通过）'
