#requires -Version 7.0
# 视觉基线生成后的留存边界：只移除两个窗口共同的上一版，保留现役与更早存量。
# 不开窗口，不读取或修改真实 assets，只在独立的临时 fixture 中观察文件系统结果。
[CmdletBinding()]
param(
    [string] $FixtureParent = [System.IO.Path]::GetTempPath()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Countdown.VisualBaselineRetention.psm1') -Force
$workRoot = Join-Path $FixtureParent ('minmin-visual-baseline-retention-' + [guid]::NewGuid().ToString('N'))
$currentVersion = '2026-11-11-v78'
$previousVersion = '2026-11-10-v77'
$olderVersion = '2026-11-09-v76'
$currentManifestPaths = [System.Collections.Generic.List[string]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

try {
    foreach ($surfaceRoot in @('main-window/864x1000', 'floating-window/520x390')) {
        foreach ($version in @($olderVersion, $previousVersion, $currentVersion)) {
            $directory = Join-Path (Join-Path $workRoot $surfaceRoot) $version
            [void][System.IO.Directory]::CreateDirectory($directory)
            $manifestPath = Join-Path $directory 'manifest.json'
            [System.IO.File]::WriteAllText($manifestPath, '{"baselineVersion":"' + $version + '"}')
            [System.IO.File]::WriteAllBytes((Join-Path $directory 'frame.png'), [byte[]] @(1, 2, 3))
            if ($version -ceq $currentVersion) {
                $currentManifestPaths.Add($manifestPath)
            }
        }
    }
    [System.IO.File]::WriteAllText((Join-Path $workRoot 'unrelated.txt'), 'keep')

    $removed = @(Remove-CountdownPreviousVisualBaseline -ManifestPath $currentManifestPaths)
    $removedNames = @($removed | ForEach-Object { Split-Path -Leaf $_ } | Sort-Object -Unique)
    if ($removed.Count -ne 2 -or @($removedNames).Count -ne 1 -or $removedNames[0] -cne $previousVersion) {
        $failures.Add("应只移除主窗与悬浮窗的上一版，实际：$($removed -join '、')")
    }
    foreach ($surfaceRoot in @('main-window/864x1000', 'floating-window/520x390')) {
        if (Test-Path -LiteralPath (Join-Path (Join-Path $workRoot $surfaceRoot) $previousVersion)) {
            $failures.Add("$surfaceRoot 的上一版仍存在")
        }
        foreach ($keptVersion in @($olderVersion, $currentVersion)) {
            if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $workRoot $surfaceRoot) $keptVersion) -PathType Container)) {
                $failures.Add("$surfaceRoot 不应移除 $keptVersion")
            }
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $workRoot 'unrelated.txt') -PathType Leaf)) {
        $failures.Add('版本目录外的文件不应受影响')
    }
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        [System.IO.Directory]::Delete($workRoot, $true)
    }
}

if ($failures.Count -gt 0) {
    throw "visual-baseline-retention 失败：$($failures -join '；')"
}
Write-Output 'visual-baseline-retention: PASS（仅临时 fixture 的上一版被移除，现役、更早存量与目录外文件保留）'
