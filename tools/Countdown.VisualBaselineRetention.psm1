#requires -Version 7.0
Set-StrictMode -Version Latest

function Remove-CountdownPreviousVisualBaseline {
    # 只在新版双 manifest 已生成并验证后调用。这里的“上一版”是两个窗口现存目录中最新的较旧共同版本；
    # 更早存量不在新生成规则里顺手清理，既有目录的一次性删除仍需单独确认。
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string[]] $ManifestPath
    )

    $currentVersions = @($ManifestPath | ForEach-Object { Split-Path -Leaf (Split-Path -Parent $_) } | Sort-Object -Unique)
    if ($currentVersions.Count -ne 1 -or $currentVersions[0] -notmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}-v(?<number>[0-9]+)$') {
        throw "现役 manifest 必须同属一个规范版本目录：$($ManifestPath -join '、')"
    }
    $currentVersion = $currentVersions[0]
    $currentNumber = [int] $Matches['number']
    $surfaces = @(foreach ($path in $ManifestPath) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "现役基线 manifest 不存在，拒绝移除上一版：$path"
            }
            $currentDirectory = Split-Path -Parent $path
            $surfaceRoot = Split-Path -Parent $currentDirectory
            $previous = Get-ChildItem -LiteralPath $surfaceRoot -Directory |
                Where-Object {
                    $_.Name -match '^[0-9]{4}-[0-9]{2}-[0-9]{2}-v(?<number>[0-9]+)$' -and
                    [int] $Matches['number'] -lt $currentNumber
                } |
                Sort-Object { [int] ($_.Name -replace '^.*-v', '') } -Descending |
                Select-Object -First 1
            [pscustomobject]@{
                SurfaceRoot = $surfaceRoot
                PreviousVersion = if ($null -eq $previous) { $null } else { $previous.Name }
            }
        })

    $previousVersions = @($surfaces.PreviousVersion | Where-Object { $null -ne $_ } | Sort-Object -Unique)
    if ($previousVersions.Count -eq 0) {
        return
    }
    if ($previousVersions.Count -ne 1) {
        throw "两个窗口的上一版不一致，拒绝部分删除：$($surfaces | Out-String)"
    }
    $previousVersion = $previousVersions[0]

    foreach ($surface in $surfaces) {
        $previousPath = Join-Path $surface.SurfaceRoot $previousVersion
        if (-not (Test-Path -LiteralPath $previousPath)) {
            continue
        }
        $item = Get-Item -LiteralPath $previousPath -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw "上一版目标不是普通目录，拒绝删除：$previousPath"
        }
        [System.IO.Directory]::Delete($previousPath, $true)
        $previousPath
    }
}

Export-ModuleMember -Function Remove-CountdownPreviousVisualBaseline
