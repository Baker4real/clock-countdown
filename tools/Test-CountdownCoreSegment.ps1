#requires -Version 7.0
# Invoke-CountdownCoreSegment.ps1 的无窗口自检：绿色合成段退出 0，红色合成段必须退出 1，不能仅打印失败。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Invoke-CountdownCoreSegment.ps1'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('countdown-core-segment-self-test-' + [guid]::NewGuid().ToString('N'))
[void] [System.IO.Directory]::CreateDirectory($root)
$utf8 = [System.Text.UTF8Encoding]::new($false)
$core = @'
function Assert-Equal {
    param($Expected, $Actual, [string] $Name)
    $script:assertions++
    if ($Expected -ne $Actual) { $failures.Add("${Name}: expected=$Expected actual=$Actual") }
}
function Write-TestSegmentTiming { param([string] $Name) }
'@
[IO.File]::WriteAllText((Join-Path $root 'Countdown.Core.Tests.ps1'), $core, $utf8)
$segmentPath = Join-Path $root 'Countdown.Core.Tests.Installer.ps1'
$PSNativeCommandUseErrorActionPreference = $false
try {
    [IO.File]::WriteAllText($segmentPath, "Assert-Equal 1 1 'green control'", $utf8)
    $green = @(& pwsh -NoLogo -NoProfile -NonInteractive -File $runner -RepositoryRoot $root -Segment Installer 2>&1 | ForEach-Object { "$_" })
    $greenExit = $LASTEXITCODE
    if ($greenExit -ne 0) { throw "绿色合成段应退出 0，实际 $greenExit：$($green -join ' ')" }
    $greenJson = ($green -join "`n" | ConvertFrom-Json)
    if ($greenJson.Failed -ne 0 -or $greenJson.Assertions -ne 1 -or $greenJson.OfficialGateRequired -ne $true) {
        throw "绿色合成段 JSON 合同不对：$($green -join ' ')"
    }

    [IO.File]::WriteAllText($segmentPath, "Assert-Equal 1 2 'intentional red control'", $utf8)
    $red = @(& pwsh -NoLogo -NoProfile -NonInteractive -File $runner -RepositoryRoot $root -Segment Installer 2>&1 | ForEach-Object { "$_" })
    $redExit = $LASTEXITCODE
    if ($redExit -eq 0) { throw "红色合成段必须非零退出：$($red -join ' ')" }
    if (-not ($red -join ' ').Contains('intentional red control')) { throw "红色合成段缺少原始断言：$($red -join ' ')" }
    [pscustomobject]@{ Valid = $true; GreenExit = $greenExit; RedExit = $redExit; Assertions = 3 } | ConvertTo-Json -Compress
}
finally {
    if ($root.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        [IO.Directory]::Delete($root, $true)
    }
}
