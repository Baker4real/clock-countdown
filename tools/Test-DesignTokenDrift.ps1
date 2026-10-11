#requires -Version 7.0
<#
.SYNOPSIS
    核对 DESIGN.md 记录的色值与 App.xaml Primitive 层是否一致。

.DESCRIPTION
    App.xaml 的 `*Color` 原色是色值唯一真源。本检查只读两份文件，核对：
    1. DESIGN.md §13.4 对比度基线表里每个 `XxxColor` 行的前景值等于 App.xaml 的同名原色；
    2. DESIGN.md §13.1 的同值 alias 清单与 App.xaml 实际的同值原色分组双向一致
       （清单里的分组必须真实同值，App.xaml 里任何同值分组也必须登记在清单里）。
    `-SelfTest` 用内置夹具证明三类漂移都会变红。
#>
[CmdletBinding()]
param(
    [string] $DesignPath,
    [string] $AppXamlPath,
    [switch] $SelfTest,
    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $DesignPath) { $DesignPath = Join-Path $repoRoot 'DESIGN.md' }
if (-not $AppXamlPath) { $AppXamlPath = Join-Path $repoRoot 'src/MinminCountdown/App.xaml' }

function ConvertTo-Argb {
    param([Parameter(Mandatory)] [string] $Hex)
    $value = $Hex.TrimStart('#').ToUpperInvariant()
    if ($value.Length -eq 6) { return "FF$value" }
    return $value
}

function Get-PrimitiveColor {
    param([Parameter(Mandatory)] [string] $XamlText)
    $colors = [ordered]@{}
    foreach ($match in [regex]::Matches($XamlText, '<Color x:Key="(\w+)Color">#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})</Color>')) {
        $colors[$match.Groups[1].Value] = ConvertTo-Argb $match.Groups[2].Value
    }
    return $colors
}

function Get-ContrastRow {
    param([Parameter(Mandatory)] [string] $DesignText)
    foreach ($match in [regex]::Matches($DesignText, '(?m)^\| `(\w+)Color` \| `#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})` \|')) {
        [pscustomobject]@{ Token = $match.Groups[1].Value; Argb = ConvertTo-Argb $match.Groups[2].Value }
    }
}

function Get-DocumentedAliasGroup {
    param([Parameter(Mandatory)] [string] $DesignText)
    $line = ($DesignText -split "`n") | Where-Object { $_.StartsWith('- 允许存在的值级重复') } | Select-Object -First 1
    if (-not $line) { return }
    foreach ($match in [regex]::Matches($line, '`#([0-9A-Fa-f]{6})` → ((?:`\w+`(?: / )?)+)')) {
        $members = @([regex]::Matches($match.Groups[2].Value, '`(\w+)`') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
        [pscustomobject]@{ Argb = ConvertTo-Argb $match.Groups[1].Value; Members = $members }
    }
}

function Test-DesignTokenText {
    param([Parameter(Mandatory)] [string] $DesignText, [Parameter(Mandatory)] [string] $XamlText)

    $issues = [System.Collections.Generic.List[object]]::new()
    $colors = Get-PrimitiveColor -XamlText $XamlText
    if ($colors.Count -eq 0) { $issues.Add([pscustomobject]@{ Kind = 'no-primitive-colors'; Detail = 'App.xaml 未解析到任何 *Color 原色' }) }

    $rows = @(Get-ContrastRow -DesignText $DesignText)
    if ($rows.Count -eq 0) { $issues.Add([pscustomobject]@{ Kind = 'no-contrast-rows'; Detail = 'DESIGN.md 未解析到 §13.4 对比度基线行' }) }
    foreach ($row in $rows) {
        if (-not $colors.Contains($row.Token)) {
            $issues.Add([pscustomobject]@{ Kind = 'contrast-row-unknown-token'; Detail = "$($row.Token)Color 不在 App.xaml" })
        }
        elseif ($colors[$row.Token] -ne $row.Argb) {
            $issues.Add([pscustomobject]@{ Kind = 'contrast-row-drift'; Detail = "$($row.Token)Color：DESIGN #$($row.Argb) ≠ App.xaml #$($colors[$row.Token])" })
        }
    }

    $documented = @(Get-DocumentedAliasGroup -DesignText $DesignText)
    if ($documented.Count -eq 0) { $issues.Add([pscustomobject]@{ Kind = 'no-alias-list'; Detail = 'DESIGN.md 未解析到 §13.1 同值 alias 清单' }) }
    foreach ($group in $documented) {
        foreach ($member in $group.Members) {
            if (-not $colors.Contains($member)) {
                $issues.Add([pscustomobject]@{ Kind = 'alias-unknown-token'; Detail = "$member 不在 App.xaml" })
            }
            elseif ($colors[$member] -ne $group.Argb) {
                $issues.Add([pscustomobject]@{ Kind = 'alias-value-drift'; Detail = "$member：清单 #$($group.Argb) ≠ App.xaml #$($colors[$member])" })
            }
        }
    }

    $actualGroups = $colors.GetEnumerator() | Group-Object Value | Where-Object Count -gt 1
    foreach ($actual in $actualGroups) {
        $members = @($actual.Group | ForEach-Object Key | Sort-Object)
        $match = $documented | Where-Object { $_.Argb -eq $actual.Name } | Select-Object -First 1
        if (-not $match) {
            $issues.Add([pscustomobject]@{ Kind = 'undocumented-alias'; Detail = "#$($actual.Name)：$($members -join ' / ') 未登记" })
        }
        elseif (($match.Members -join '|') -ne ($members -join '|')) {
            $issues.Add([pscustomobject]@{ Kind = 'alias-membership-drift'; Detail = "#$($actual.Name)：清单 $($match.Members -join ' / ') ≠ App.xaml $($members -join ' / ')" })
        }
    }

    [pscustomobject]@{
        Valid = $issues.Count -eq 0
        PrimitiveColors = $colors.Count
        ContrastRows = $rows.Count
        AliasGroups = $documented.Count
        Issues = @($issues)
    }
}

if ($SelfTest) {
    $xaml = '<Color x:Key="InkColor">#FF3B190D</Color><Color x:Key="TagChipTextColor">#3B190D</Color><Color x:Key="CanvasColor">#FFFDF3EB</Color>'
    $design = @"
- 允许存在的值级重复只有以下 alias：``#3B190D`` → ``Ink`` / ``TagChipText``。
| ``InkColor`` | ``#3B190D`` | 背景 | 12.48 |
"@
    $cases = @(
        [pscustomobject]@{ Name = 'consistent'; Design = $design; Xaml = $xaml; ExpectValid = $true; ExpectKind = $null }
        [pscustomobject]@{ Name = 'contrast-row-drift'; Design = $design.Replace('| `InkColor` | `#3B190D`', '| `InkColor` | `#111111`'); Xaml = $xaml; ExpectValid = $false; ExpectKind = 'contrast-row-drift' }
        [pscustomobject]@{ Name = 'undocumented-alias'; Design = $design; Xaml = $xaml + '<Color x:Key="PanelColor">#FFFDF3EB</Color>'; ExpectValid = $false; ExpectKind = 'undocumented-alias' }
        [pscustomobject]@{ Name = 'alias-value-drift'; Design = $design; Xaml = $xaml.Replace('<Color x:Key="TagChipTextColor">#3B190D</Color>', '<Color x:Key="TagChipTextColor">#3B190E</Color>'); ExpectValid = $false; ExpectKind = 'alias-value-drift' }
    )
    $results = foreach ($case in $cases) {
        $outcome = Test-DesignTokenText -DesignText $case.Design -XamlText $case.Xaml
        $kinds = @($outcome.Issues | ForEach-Object Kind)
        $passed = ($outcome.Valid -eq $case.ExpectValid) -and ((-not $case.ExpectKind) -or ($kinds -contains $case.ExpectKind))
        [pscustomobject]@{ Name = $case.Name; Passed = $passed; Kinds = $kinds }
    }
    $report = [pscustomobject]@{ Valid = -not ($results | Where-Object { -not $_.Passed }); Cases = @($results) }
}
else {
    foreach ($path in $DesignPath, $AppXamlPath) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少输入文件：$path" }
    }
    $report = Test-DesignTokenText `
        -DesignText (Get-Content -LiteralPath $DesignPath -Raw -Encoding utf8) `
        -XamlText (Get-Content -LiteralPath $AppXamlPath -Raw -Encoding utf8)
}

if ($Json) {
    $report | ConvertTo-Json -Depth 6
}
else {
    $report | Format-List | Out-String | Write-Output
    foreach ($issue in @($report.PSObject.Properties['Issues'] | ForEach-Object Value)) { Write-Output "- [$($issue.Kind)] $($issue.Detail)" }
}
if (-not $report.Valid) { exit 1 }
exit 0
