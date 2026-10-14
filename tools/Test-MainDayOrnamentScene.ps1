#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$generatorPath = Join-Path $PSScriptRoot 'Build-MainDayOrnamentScene.ps1'
$definitionPath = Join-Path $PSScriptRoot 'MainDecorationSceneBands.psd1'
$definition = Import-PowerShellDataFile -LiteralPath $definitionPath
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("MinminCountdown-day-ornament-{0}" -f [guid]::NewGuid().ToString('N'))
$originalSourceRoot = Join-Path $testRoot 'original'
$firstOutputRoot = Join-Path $testRoot 'first'
$secondOutputRoot = Join-Path $testRoot 'second'

function Read-GitBlob {
    param([Parameter(Mandatory)] [string] $RepositoryRelativePath)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    [void] $startInfo.ArgumentList.Add('cat-file')
    [void] $startInfo.ArgumentList.Add('blob')
    [void] $startInfo.ArgumentList.Add("HEAD:$RepositoryRelativePath")
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Unable to start git for $RepositoryRelativePath"
        }
        $stream = [System.IO.MemoryStream]::new()
        try {
            $process.StandardOutput.BaseStream.CopyTo($stream)
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) {
                throw "git cat-file failed for $RepositoryRelativePath`: $($process.StandardError.ReadToEnd())"
            }
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-BitmapPixels {
    param([Parameter(Mandatory)] [string] $Path)

    $bitmap = [System.Drawing.Bitmap]::new($Path)
    $bits = $null
    try {
        if ($bitmap.Width -ne [int] $definition.CanvasWidth -or
            $bitmap.Height -ne [int] $definition.CanvasHeight -or
            $bitmap.PixelFormat -ne [System.Drawing.Imaging.PixelFormat]::Format32bppArgb) {
            throw "Unexpected bitmap contract: $Path"
        }

        $rectangle = [System.Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height)
        $bits = $bitmap.LockBits(
            $rectangle,
            [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $rowBytes = $bitmap.Width * 4
        $raw = [byte[]]::new($rowBytes * $bitmap.Height)
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            [System.Runtime.InteropServices.Marshal]::Copy(
                [IntPtr]::Add($bits.Scan0, $y * $bits.Stride),
                $raw,
                $y * $rowBytes,
                $rowBytes)
        }
        return ,$raw
    }
    finally {
        if ($null -ne $bits) {
            $bitmap.UnlockBits($bits)
        }
        $bitmap.Dispose()
    }
}

function Test-OrnamentPurity {
    param(
        [Parameter(Mandatory)] [byte[]] $Pixels,
        [Parameter(Mandatory)] [hashtable] $Scene
    )

    $width = [int] $definition.CanvasWidth
    $sampleX = [int] $definition.BackgroundSampleX
    for ($y = 0; $y -lt [int] $definition.CanvasHeight; $y++) {
        $insideTopBand = $y -ge [int] $Scene.TopBandStart -and $y -le [int] $Scene.TopBandEnd
        $insideBottomBand = $y -ge [int] $Scene.BottomBandStart
        if ($insideTopBand -or $insideBottomBand) {
            continue
        }

        $expectedOffset = (($y * $width) + $sampleX) * 4
        for ($x = 0; $x -lt $width; $x++) {
            $offset = (($y * $width) + $x) * 4
            for ($channel = 0; $channel -lt 4; $channel++) {
                if ($Pixels[$offset + $channel] -ne $Pixels[$expectedOffset + $channel]) {
                    return $false
                }
            }
        }
    }

    return $true
}

function Test-OrnamentBandsUnchanged {
    param(
        [Parameter(Mandatory)] [byte[]] $SourcePixels,
        [Parameter(Mandatory)] [byte[]] $OutputPixels,
        [Parameter(Mandatory)] [hashtable] $Scene
    )

    $width = [int] $definition.CanvasWidth
    foreach ($range in @(
        @([int] $Scene.TopBandStart, [int] $Scene.TopBandEnd),
        @([int] $Scene.BottomBandStart, ([int] $definition.CanvasHeight - 1)))) {
        for ($y = $range[0]; $y -le $range[1]; $y++) {
            $rowOffset = $y * $width * 4
            for ($x = 0; $x -lt $width; $x++) {
                $offset = $rowOffset + ($x * 4)
                for ($channel = 0; $channel -lt 4; $channel++) {
                    if ($SourcePixels[$offset + $channel] -ne $OutputPixels[$offset + $channel]) {
                        return $false
                    }
                }
            }
        }
    }

    return $true
}

function Test-PixelArraysEqual {
    param(
        [Parameter(Mandatory)] [byte[]] $Left,
        [Parameter(Mandatory)] [byte[]] $Right
    )

    if ($Left.Length -ne $Right.Length) {
        return $false
    }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }
    return $true
}

function Copy-PixelBytes {
    param([Parameter(Mandatory)] [byte[]] $Pixels)

    $copy = [byte[]]::new($Pixels.Length)
    [System.Array]::Copy($Pixels, $copy, $Pixels.Length)
    return ,$copy
}

function Remove-TestDirectory {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    foreach ($childPath in @(
        (Join-Path $Path 'original\main-countdown-scene-day.png'),
        (Join-Path $Path 'original\main-notes-scene-day.png'),
        (Join-Path $Path 'first\main-countdown-scene-day.png'),
        (Join-Path $Path 'first\main-notes-scene-day.png'),
        (Join-Path $Path 'second\main-countdown-scene-day.png'),
        (Join-Path $Path 'second\main-notes-scene-day.png')
    )) {
        Remove-Item -LiteralPath $childPath -Force -ErrorAction SilentlyContinue
    }
    foreach ($directoryPath in @(
        (Join-Path $Path 'original'),
        (Join-Path $Path 'first'),
        (Join-Path $Path 'second'),
        $Path
    )) {
        Remove-Item -LiteralPath $directoryPath -Force -ErrorAction SilentlyContinue
    }
}

try {
    [System.IO.Directory]::CreateDirectory($originalSourceRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($firstOutputRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($secondOutputRoot) | Out-Null

    foreach ($sceneName in @('countdown', 'notes')) {
        $fileName = "main-$sceneName-scene-day.png"
        [System.IO.File]::WriteAllBytes(
            (Join-Path $originalSourceRoot $fileName),
            (Read-GitBlob -RepositoryRelativePath "assets/main-decorations/$fileName"))
    }

    $firstJson = & $generatorPath -ProjectRoot $ProjectRoot -SourceRoot $originalSourceRoot -OutputRoot $firstOutputRoot | Out-String
    $firstRecords = @($firstJson | ConvertFrom-Json)

    $secondJson = & $generatorPath -ProjectRoot $ProjectRoot -SourceRoot $originalSourceRoot -OutputRoot $secondOutputRoot | Out-String
    $secondRecords = @($secondJson | ConvertFrom-Json)

    $measurements = foreach ($sceneName in @('countdown', 'notes')) {
        $scene = $definition.Scenes[$sceneName]
        $fileName = "main-$sceneName-scene-day.png"
        $sourcePath = Join-Path $originalSourceRoot $fileName
        $firstPath = Join-Path $firstOutputRoot $fileName
        $secondPath = Join-Path $secondOutputRoot $fileName
        $sourcePixels = Get-BitmapPixels -Path $sourcePath
        $firstPixels = Get-BitmapPixels -Path $firstPath
        $secondPixels = Get-BitmapPixels -Path $secondPath
        $purityNegativePixels = Copy-PixelBytes -Pixels $firstPixels
        $purityNegativePixels[0] = $purityNegativePixels[0] -bxor 1
        $bandNegativePixels = Copy-PixelBytes -Pixels $firstPixels
        $bandNegativeOffset = (([int] $scene.TopBandStart * [int] $definition.CanvasWidth) * 4)
        $bandNegativePixels[$bandNegativeOffset] = $bandNegativePixels[$bandNegativeOffset] -bxor 1

        [pscustomobject]@{
            stateId = "$sceneName-day"
            purityGate = Test-OrnamentPurity -Pixels $firstPixels -Scene $scene
            ornamentBandsUnchangedGate = Test-OrnamentBandsUnchanged `
                -SourcePixels $sourcePixels `
                -OutputPixels $firstPixels `
                -Scene $scene
            deterministicHashGate =
                (Get-FileHash -LiteralPath $firstPath -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $secondPath -Algorithm SHA256).Hash
            deterministicPixelsGate =
                (Test-PixelArraysEqual -Left $firstPixels -Right $secondPixels)
            purityGateIsolation = -not (Test-OrnamentPurity -Pixels $purityNegativePixels -Scene $scene)
            ornamentBandsUnchangedGateIsolation = -not (Test-OrnamentBandsUnchanged `
                -SourcePixels $sourcePixels `
                -OutputPixels $bandNegativePixels `
                -Scene $scene)
        }
    }

    if (@($measurements | Where-Object { -not $_.purityGate }).Count -ne 0) {
        throw '装饰图纯净度门失败。'
    }
    if (@($measurements | Where-Object { -not $_.ornamentBandsUnchangedGate }).Count -ne 0) {
        throw '装饰带像素不变门失败。'
    }
    if (@($measurements | Where-Object { -not $_.deterministicHashGate -or -not $_.deterministicPixelsGate }).Count -ne 0) {
        throw '日间纯装饰图生成结果不稳定。'
    }
    if (@($measurements | Where-Object { -not $_.purityGateIsolation }).Count -ne 0) {
        throw '装饰图纯净度门未能独立拒绝控件区像素损坏。'
    }
    if (@($measurements | Where-Object { -not $_.ornamentBandsUnchangedGateIsolation }).Count -ne 0) {
        throw '装饰带像素不变门未能独立拒绝装饰带像素损坏。'
    }
    if ((@($firstRecords.stateId) -join ',') -ne 'countdown-day,notes-day' -or
        (@($secondRecords.stateId) -join ',') -ne 'countdown-day,notes-day') {
        throw '日间纯装饰图生成器未输出预期的结构化场景记录。'
    }

    [pscustomobject]@{
        OrnamentPurityGate = $true
        OrnamentBandsUnchangedGate = $true
        DeterministicHashGate = $true
        Measurements = @($measurements)
    } | ConvertTo-Json -Depth 5
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-TestDirectory -Path $testRoot
    }
}
