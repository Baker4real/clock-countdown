#requires -Version 7.0
<#
.SYNOPSIS
逐窗口比对两版视觉基线，输出 DESIGN.md 记录新基线时要写的事实。

.DESCRIPTION
读取主窗与悬浮窗在两个版本下的 manifest.json，逐窗口输出：
- sourceBinding.aggregateSha256 的前后值，以及新增、删除或哈希变化的绑定文件；
- executable.sha256 的前后值；
- 逐帧 PNG 是否逐字节一致、状态指纹是否一致，以及其余字段里有哪些变了；PNG 不同的帧再给出差异像素数、差异框与差异行。
-To 默认取现役版本（src/MinminCountdown/VisualReferenceBaseline.cs 的 Version）。只读，不写文件。

.EXAMPLE
pwsh -NoLogo -NoProfile -File ./tools/Compare-CountdownVisualBaseline.ps1 -From 2026-10-29-v65

重拍后拿上一版对比现役版；-To 可显式指定另一版。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]{4}-[0-9]{2}-[0-9]{2}-v[0-9]+$')]
    [string] $From,
    [ValidatePattern('^[0-9]{4}-[0-9]{2}-[0-9]{2}-v[0-9]+$')]
    [string] $To
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $repoRoot 'Countdown.Validation.psm1') -Force
$visualBaseline = Get-CountdownVisualBaseline -RepositoryRoot $repoRoot
if (-not $To) { $To = $visualBaseline.Version }

function Read-BaselineManifest {
    param([Parameter(Mandatory)] [string] $SurfaceRoot, [Parameter(Mandatory)] [string] $Version)

    $path = Join-Path $SurfaceRoot "$Version\manifest.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "找不到 manifest：$path"
    }
    Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 100
}

# PSCustomObject 的 -eq 比的是引用，嵌套字段一律序列化成 JSON 再比。
function ConvertTo-ComparableJson {
    param([AllowNull()] $Value)
    ConvertTo-Json -InputObject $Value -Depth 100 -Compress
}

function Get-PixelDifference {
    param([Parameter(Mandatory)] [string] $OldPath, [Parameter(Mandatory)] [string] $NewPath)

    $old = [System.Drawing.Bitmap]::FromFile($OldPath)
    $new = [System.Drawing.Bitmap]::FromFile($NewPath)
    try {
        if ($old.Width -ne $new.Width -or $old.Height -ne $new.Height) {
            return "尺寸 $($old.Width)x$($old.Height) -> $($new.Width)x$($new.Height)"
        }
        $rectangle = [System.Drawing.Rectangle]::new(0, 0, $old.Width, $old.Height)
        $format = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
        $oldData = $old.LockBits($rectangle, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $format)
        $newData = $new.LockBits($rectangle, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $format)
        try {
            $stride = $oldData.Stride
            $oldBytes = [byte[]]::new($stride * $old.Height)
            $newBytes = [byte[]]::new($stride * $old.Height)
            [System.Runtime.InteropServices.Marshal]::Copy($oldData.Scan0, $oldBytes, 0, $oldBytes.Length)
            [System.Runtime.InteropServices.Marshal]::Copy($newData.Scan0, $newBytes, 0, $newBytes.Length)
        }
        finally {
            $old.UnlockBits($oldData)
            $new.UnlockBits($newData)
        }

        $count = 0
        $minX = [int]::MaxValue
        $maxX = -1
        $rows = [System.Collections.Generic.List[int]]::new()
        for ($y = 0; $y -lt $old.Height; $y++) {
            $offset = $y * $stride
            # 整行字节相同就跳过，逐像素只扫有差异的行。
            if ([System.Linq.Enumerable]::SequenceEqual[byte](
                    [System.ArraySegment[byte]]::new($oldBytes, $offset, $old.Width * 4),
                    [System.ArraySegment[byte]]::new($newBytes, $offset, $old.Width * 4))) {
                continue
            }
            $rows.Add($y)
            for ($x = 0; $x -lt $old.Width; $x++) {
                if ([BitConverter]::ToUInt32($oldBytes, $offset + $x * 4) -ne [BitConverter]::ToUInt32($newBytes, $offset + $x * 4)) {
                    $count++
                    if ($x -lt $minX) { $minX = $x }
                    if ($x -gt $maxX) { $maxX = $x }
                }
            }
        }
        if ($count -eq 0) {
            return '像素一致（PNG 字节不同）'
        }

        # 差异行压成连续区间，看得出改动落在哪几条横带。
        $bands = [System.Collections.Generic.List[string]]::new()
        $bandStart = $rows[0]
        for ($index = 1; $index -le $rows.Count; $index++) {
            if ($index -lt $rows.Count -and $rows[$index] -eq $rows[$index - 1] + 1) {
                continue
            }
            $bands.Add($(if ($rows[$index - 1] -eq $bandStart) { "$bandStart" } else { "$bandStart–$($rows[$index - 1])" }))
            if ($index -lt $rows.Count) { $bandStart = $rows[$index] }
        }
        "$count px，差异框 x $minX–$maxX、y $($rows[0])–$($rows[-1])，差异行 $($bands -join '、')"
    }
    finally {
        $old.Dispose()
        $new.Dispose()
    }
}

"$From -> $To"
foreach ($surface in @(
        [pscustomobject]@{ Name = '主窗'; Baseline = $visualBaseline.Main },
        [pscustomobject]@{ Name = '悬浮窗'; Baseline = $visualBaseline.Floating })) {
    # 版本目录的上一级是窗口尺寸目录，两个版本都按它定位。
    $surfaceRoot = Split-Path -Parent (Split-Path -Parent $surface.Baseline.ManifestPath)
    $old = Read-BaselineManifest -SurfaceRoot $surfaceRoot -Version $From
    $new = Read-BaselineManifest -SurfaceRoot $surfaceRoot -Version $To
    ''
    '[{0}] {1}' -f $surface.Name, [System.IO.Path]::GetRelativePath($repoRoot, $surfaceRoot).Replace('\', '/')
    '  源码绑定聚合 {0} -> {1}' -f $old.sourceBinding.aggregateSha256, $new.sourceBinding.aggregateSha256

    $oldFiles = @{}
    foreach ($file in $old.sourceBinding.files) { $oldFiles[$file.path] = $file.sha256 }
    $newFiles = @{}
    foreach ($file in $new.sourceBinding.files) { $newFiles[$file.path] = $file.sha256 }
    $bindingChanges = @(
        foreach ($path in $newFiles.Keys | Sort-Object) {
            if (-not $oldFiles.ContainsKey($path)) { "新增 $path" }
            elseif ($oldFiles[$path] -ne $newFiles[$path]) { "改动 $path" }
        }
        foreach ($path in $oldFiles.Keys | Sort-Object) {
            if (-not $newFiles.ContainsKey($path)) { "删除 $path" }
        })
    '  绑定文件变化：{0}' -f $(if ($bindingChanges.Count -gt 0) { $bindingChanges -join '；' } else { '无' })
    '  executable.sha256 {0} -> {1}' -f $old.executable.sha256, $new.executable.sha256

    $oldCaptures = @{}
    foreach ($capture in $old.captures) { $oldCaptures[$capture.captureId] = $capture }
    $pngSame = 0
    $fingerprintSame = 0
    foreach ($capture in $new.captures) {
        if (-not $oldCaptures.ContainsKey($capture.captureId)) {
            '  {0}：{1} 没有这一帧' -f $capture.captureId, $From
            continue
        }
        $previous = $oldCaptures[$capture.captureId]
        $oldPng = Join-Path $repoRoot $previous.path
        $newPng = Join-Path $repoRoot $capture.path
        $pngIdentical = (Get-FileHash -LiteralPath $oldPng -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $newPng -Algorithm SHA256).Hash
        if ($pngIdentical) { $pngSame++ }
        $fingerprintIdentical = (ConvertTo-ComparableJson $previous.stateFingerprint) -ceq (ConvertTo-ComparableJson $capture.stateFingerprint)
        if ($fingerprintIdentical) { $fingerprintSame++ }
        $changedFields = @(
            @($previous.PSObject.Properties.Name) + @($capture.PSObject.Properties.Name) |
                Sort-Object -Unique |
                Where-Object { $_ -notin @('captureId', 'path', 'pngSha256', 'stateFingerprint') } |
                Where-Object {
                    (ConvertTo-ComparableJson ($previous.PSObject.Properties[$_]?.Value)) -cne
                    (ConvertTo-ComparableJson ($capture.PSObject.Properties[$_]?.Value))
                })
        '  {0}：{1}；{2}{3}' -f $capture.captureId,
            $(if ($pngIdentical) { 'PNG 逐字节一致' } else { 'PNG ' + (Get-PixelDifference -OldPath $oldPng -NewPath $newPng) }),
            $(if ($fingerprintIdentical) { '状态指纹一致' } else { '状态指纹不同' }),
            $(if ($changedFields.Count -gt 0) { "；其余字段变化：$($changedFields -join '、')" } else { '' })
    }
    $newCaptureIds = @($new.captures | ForEach-Object captureId)
    foreach ($captureId in $oldCaptures.Keys | Where-Object { $_ -notin $newCaptureIds } | Sort-Object) {
        '  {0}：{1} 已没有这一帧' -f $captureId, $To
    }
    '  {0} 帧：PNG 逐字节一致 {1}，状态指纹一致 {2}' -f @($new.captures).Count, $pngSame, $fingerprintSame
}
