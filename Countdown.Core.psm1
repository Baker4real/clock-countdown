Set-StrictMode -Version Latest

$script:BeijingTimeZone = [System.TimeZoneInfo]::FindSystemTimeZoneById('China Standard Time')

function Get-BeijingNow {
    [CmdletBinding()]
    param()

    [System.TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $script:BeijingTimeZone)
}

function ConvertTo-CountdownTargetUtc {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $DateText,
        [Parameter(Mandatory)] [string] $TimeText
    )

    [DateTime] $value = [DateTime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::None
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    if (-not [DateTime]::TryParseExact(
            "$DateText $TimeText",
            'yyyy-MM-dd HH:mm:ss',
            $culture,
            $styles,
            [ref] $value
        )) {
        throw '日期时间格式无效，请使用 yyyy-MM-dd HH:mm:ss。'
    }

    $unspecified = [DateTime]::SpecifyKind($value, [DateTimeKind]::Unspecified)
    [DateTimeOffset]::new([System.TimeZoneInfo]::ConvertTimeToUtc($unspecified, $script:BeijingTimeZone))
}

function Get-CountdownParts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [DateTimeOffset] $TargetUtc,
        [Parameter(Mandatory)] [DateTimeOffset] $NowUtc
    )

    $remaining = $TargetUtc - $NowUtc
    if ($remaining.TotalSeconds -le 0) {
        return [pscustomobject]@{
            IsElapsed = $true
            Days      = 0
            Hours     = 0
            Minutes   = 0
            Seconds   = 0
        }
    }

    [pscustomobject]@{
        IsElapsed = $false
        Days      = [Math]::Floor($remaining.TotalDays)
        Hours     = $remaining.Hours
        Minutes   = $remaining.Minutes
        Seconds   = $remaining.Seconds
    }
}

function ConvertTo-CountdownDateText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $SelectedDate
    )

    ([DateTime] $SelectedDate).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Remove-CountdownSettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $SettingsPath
    )

    $removed = $false
    foreach ($path in @("$SettingsPath.tmp", $SettingsPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
            $removed = $true
        }
    }
    $removed
}

Export-ModuleMember -Function Get-BeijingNow, ConvertTo-CountdownTargetUtc, Get-CountdownParts, ConvertTo-CountdownDateText, Remove-CountdownSettings
