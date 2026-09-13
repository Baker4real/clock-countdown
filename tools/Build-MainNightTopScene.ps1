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

$definition = Import-PowerShellDataFile -LiteralPath (
    Join-Path $PSScriptRoot 'MainDecorationSceneBands.psd1'
)
$rasterDpi = [single] 96

function Add-TintedSubject {
    param(
        [Parameter(Mandatory)] [System.Drawing.Bitmap] $Source,
        [Parameter(Mandatory)] [System.Drawing.Bitmap] $Target,
        [Parameter(Mandatory)] [int] $X,
        [Parameter(Mandatory)] [int] $Y,
        [Parameter(Mandatory)] [int] $Width,
        [Parameter(Mandatory)] [int] $Height
    )

    $eligibleBackground = [bool[]]::new($Width * $Height)
    for ($localY = 0; $localY -lt $Height; $localY++) {
        for ($localX = 0; $localX -lt $Width; $localX++) {
            $color = $Source.GetPixel($X + $localX, $Y + $localY)
            $eligibleBackground[($localY * $Width) + $localX] =
                $color.R -ge 240 -and $color.G -ge 230 -and $color.B -ge 220
        }
    }

    $background = [bool[]]::new($Width * $Height)
    $queue = [System.Collections.Generic.Queue[int]]::new()
    for ($localX = 0; $localX -lt $Width; $localX++) {
        foreach ($localY in @(0, ($Height - 1))) {
            $index = ($localY * $Width) + $localX
            if ($eligibleBackground[$index] -and -not $background[$index]) {
                $background[$index] = $true
                $queue.Enqueue($index)
            }
        }
    }
    for ($localY = 0; $localY -lt $Height; $localY++) {
        foreach ($localX in @(0, ($Width - 1))) {
            $index = ($localY * $Width) + $localX
            if ($eligibleBackground[$index] -and -not $background[$index]) {
                $background[$index] = $true
                $queue.Enqueue($index)
            }
        }
    }

    while ($queue.Count -gt 0) {
        $index = $queue.Dequeue()
        $localX = $index % $Width
        $localY = [int] [Math]::Floor($index / $Width)
        foreach ($neighbor in @(
            @(($localX - 1), $localY),
            @(($localX + 1), $localY),
            @($localX, ($localY - 1)),
            @($localX, ($localY + 1)))) {
            $neighborX = $neighbor[0]
            $neighborY = $neighbor[1]
            if ($neighborX -lt 0 -or $neighborY -lt 0 -or
                $neighborX -ge $Width -or $neighborY -ge $Height) {
                continue
            }

            $neighborIndex = ($neighborY * $Width) + $neighborX
            if ($eligibleBackground[$neighborIndex] -and -not $background[$neighborIndex]) {
                $background[$neighborIndex] = $true
                $queue.Enqueue($neighborIndex)
            }
        }
    }

    for ($localY = 0; $localY -lt $Height; $localY++) {
        for ($localX = 0; $localX -lt $Width; $localX++) {
            $index = ($localY * $Width) + $localX
            if ($background[$index]) {
                continue
            }

            $sourceColor = $Source.GetPixel($X + $localX, $Y + $localY)
            $luminance =
                (0.2126 * $sourceColor.R) +
                (0.7152 * $sourceColor.G) +
                (0.0722 * $sourceColor.B)
            $tone = [Math]::Pow($luminance / 255.0, 0.9)
            $red = [int] [Math]::Round(48 + ((232 - 48) * $tone))
            $green = [int] [Math]::Round(33 + ((190 - 33) * $tone))
            $blue = [int] [Math]::Round(24 + ((132 - 24) * $tone))
            $Target.SetPixel(
                $X + $localX,
                $Y + $localY,
                [System.Drawing.Color]::FromArgb(255, $red, $green, $blue))
        }
    }
}

function Add-WaveEdges {
    param(
        [Parameter(Mandatory)] [System.Drawing.Bitmap] $Source,
        [Parameter(Mandatory)] [System.Drawing.Bitmap] $Target,
        [Parameter(Mandatory)] [int] $Top,
        [Parameter(Mandatory)] [int] $Bottom,
        [Parameter(Mandatory)] [object[]] $SubjectBoxes
    )

    for ($y = $Top; $y -le $Bottom; $y++) {
        for ($x = 35; $x -le 1130; $x++) {
            $insideSubject = $false
            foreach ($box in $SubjectBoxes) {
                if ($x -ge $box.X -and $x -lt ($box.X + $box.Width) -and
                    $y -ge $box.Y -and $y -lt ($box.Y + $box.Height)) {
                    $insideSubject = $true
                    break
                }
            }
            if ($insideSubject) {
                continue
            }

            $color = $Source.GetPixel($x, $y)
            $neighbors = @(
                $Source.GetPixel($x - 2, $y),
                $Source.GetPixel($x + 2, $y),
                $Source.GetPixel($x, $y - 2),
                $Source.GetPixel($x, $y + 2)
            )
            $maximumDelta = 0
            foreach ($neighbor in $neighbors) {
                $delta = [Math]::Max(
                    [Math]::Abs([int] $color.R - [int] $neighbor.R),
                    [Math]::Max(
                        [Math]::Abs([int] $color.G - [int] $neighbor.G),
                        [Math]::Abs([int] $color.B - [int] $neighbor.B)))
                $maximumDelta = [Math]::Max($maximumDelta, $delta)
            }

            if ($maximumDelta -lt 5 -or ([int] $color.R - [int] $color.B) -lt 10) {
                continue
            }

            $alpha = [Math]::Min(220, 80 + ($maximumDelta * 12))
            $Target.SetPixel(
                $x,
                $y,
                [System.Drawing.Color]::FromArgb($alpha, 202, 151, 88))
        }
    }
}

function New-NightTopScene {
    param(
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $OutputPath,
        [Parameter(Mandatory)] [object[]] $SubjectBoxes,
        [Parameter(Mandatory)] [int] $WaveTop,
        [Parameter(Mandatory)] [int] $WaveBottom
    )

    $source = [System.Drawing.Bitmap]::new($SourcePath)
    try {
        if ($source.Width -ne 1165 -or $source.Height -ne 1350) {
            throw "Night top scene source must be 1165x1350: $SourcePath"
        }

        $target = [System.Drawing.Bitmap]::new(
            1165,
            1350,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $target.SetResolution($rasterDpi, $rasterDpi)
            foreach ($box in $SubjectBoxes) {
                Add-TintedSubject `
                    -Source $source `
                    -Target $target `
                    -X $box.X `
                    -Y $box.Y `
                    -Width $box.Width `
                    -Height $box.Height
            }
            Add-WaveEdges `
                -Source $source `
                -Target $target `
                -Top $WaveTop `
                -Bottom $WaveBottom `
                -SubjectBoxes $SubjectBoxes

            $outputDirectory = Split-Path -Parent $OutputPath
            [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
            $target.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $target.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

$sceneRoot = Join-Path $ProjectRoot 'assets\main-decorations'
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = $sceneRoot
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = $sceneRoot
}
$countdownOutput = Join-Path $OutputRoot 'main-countdown-top-night.png'
$notesOutput = Join-Path $OutputRoot 'main-notes-top-night.png'

$countdownBoxes = @(
    [pscustomobject]@{ X = 65; Y = 250; Width = 125; Height = 125 },
    [pscustomobject]@{ X = 460; Y = 260; Width = 235; Height = 115 },
    [pscustomobject]@{ X = 950; Y = 250; Width = 150; Height = 130 }
)
$notesBoxes = @(
    [pscustomobject]@{ X = 70; Y = 255; Width = 160; Height = 145 },
    [pscustomobject]@{ X = 450; Y = 300; Width = 250; Height = 105 },
    [pscustomobject]@{ X = 940; Y = 250; Width = 170; Height = 150 }
)

New-NightTopScene `
    -SourcePath (Join-Path $SourceRoot 'main-countdown-scene-day.png') `
    -OutputPath $countdownOutput `
    -SubjectBoxes $countdownBoxes `
    -WaveTop ([int] $definition.Scenes.countdown.WaveTop) `
    -WaveBottom ([int] $definition.Scenes.countdown.TopBandEnd)
New-NightTopScene `
    -SourcePath (Join-Path $SourceRoot 'main-notes-scene-day.png') `
    -OutputPath $notesOutput `
    -SubjectBoxes $notesBoxes `
    -WaveTop ([int] $definition.Scenes.notes.WaveTop) `
    -WaveBottom ([int] $definition.Scenes.notes.TopBandEnd)

@(
    [pscustomobject]@{
        stateId = 'countdown-night'
        path = $countdownOutput
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $countdownOutput).Hash
        subjectBoxes = $countdownBoxes
        waveBand = @(
            [int] $definition.Scenes.countdown.WaveTop,
            [int] $definition.Scenes.countdown.TopBandEnd
        )
    },
    [pscustomobject]@{
        stateId = 'notes-night'
        path = $notesOutput
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $notesOutput).Hash
        subjectBoxes = $notesBoxes
        waveBand = @(
            [int] $definition.Scenes.notes.WaveTop,
            [int] $definition.Scenes.notes.TopBandEnd
        )
    }
) | ConvertTo-Json -Depth 5
