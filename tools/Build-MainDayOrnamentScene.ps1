#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $ProjectRoot = (Split-Path -Parent $PSScriptRoot),

    [Parameter(Mandatory = $false)]
    [string] $SourceRoot,

    [Parameter(Mandatory = $false)]
    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$definitionPath = Join-Path $PSScriptRoot 'MainDecorationSceneBands.psd1'
$definition = Import-PowerShellDataFile -LiteralPath $definitionPath
$sceneRoot = Join-Path $ProjectRoot 'assets\main-decorations'
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = $sceneRoot
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = $sceneRoot
}

function New-DayOrnamentScene {
    param(
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $OutputPath,
        [Parameter(Mandatory)] [hashtable] $Scene
    )

    $temporaryPath = $null
    $source = [System.Drawing.Bitmap]::new($SourcePath)
    try {
        if ($source.Width -ne [int] $definition.CanvasWidth -or
            $source.Height -ne [int] $definition.CanvasHeight) {
            throw "Day ornament source must be $($definition.CanvasWidth)x$($definition.CanvasHeight): $SourcePath"
        }

        $target = $source.Clone(
            [System.Drawing.Rectangle]::new(0, 0, $source.Width, $source.Height),
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            # Rows outside the ornament bands become one color across the full width, edge
            # columns included: DWM draws the window frame, so the raster keeps no frame of its own.
            $row = [int[]]::new($target.Width)
            $bits = $target.LockBits(
                [System.Drawing.Rectangle]::new(0, 0, $target.Width, $target.Height),
                [System.Drawing.Imaging.ImageLockMode]::ReadWrite,
                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                for ($y = 0; $y -lt $target.Height; $y++) {
                    $insideTopBand = $y -ge [int] $Scene.TopBandStart -and $y -le [int] $Scene.TopBandEnd
                    $insideBottomBand = $y -ge [int] $Scene.BottomBandStart
                    if ($insideTopBand -or $insideBottomBand) {
                        continue
                    }

                    # Sample from a safe row in the control region to avoid borders/decorations at top
                    $safeY = if ($y -lt 20) { 100 } else { $y }
                    $background = $source.GetPixel([int] $definition.BackgroundSampleX, $safeY)
                    [System.Array]::Fill($row, $background.ToArgb())
                    [System.Runtime.InteropServices.Marshal]::Copy(
                        $row, 0, [IntPtr]::Add($bits.Scan0, $y * $bits.Stride), $row.Length)
                }
            }
            finally {
                $target.UnlockBits($bits)
            }

            $outputDirectory = Split-Path -Parent $OutputPath
            [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
            $temporaryPath = Join-Path $outputDirectory (".{0}.{1}.tmp.png" -f (
                [System.IO.Path]::GetFileNameWithoutExtension($OutputPath)
            ), [guid]::NewGuid().ToString('N'))
            $target.SetResolution(96, 96)
            $target.Save($temporaryPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $target.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }

    try {
        [System.IO.File]::Move($temporaryPath, $OutputPath, $true)
    }
    finally {
        if ($null -ne $temporaryPath -and (Test-Path -LiteralPath $temporaryPath)) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$records = foreach ($sceneName in @('countdown', 'notes')) {
    $scene = $definition.Scenes[$sceneName]
    $fileName = "main-$sceneName-scene-day.png"
    $sourcePath = Join-Path $SourceRoot $fileName
    $outputPath = Join-Path $OutputRoot $fileName

    New-DayOrnamentScene -SourcePath $sourcePath -OutputPath $outputPath -Scene $scene

    [pscustomobject]@{
        stateId = "$sceneName-day"
        path = $outputPath
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath).Hash
        ornamentBands = @(
            @([int] $scene.TopBandStart, [int] $scene.TopBandEnd),
            @([int] $scene.BottomBandStart, ([int] $definition.CanvasHeight - 1))
        )
        waveBand = @([int] $scene.WaveTop, [int] $scene.TopBandEnd)
        backgroundSampleX = [int] $definition.BackgroundSampleX
    }
}

@($records) | ConvertTo-Json -Depth 5
