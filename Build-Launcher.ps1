#requires -Version 7.0

$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "未找到 .NET Framework C# 编译器：$compiler"
}

$source = Join-Path $PSScriptRoot 'CountdownLauncher.cs'
$icon = Join-Path $PSScriptRoot 'countdown-icon.ico'
$output = Join-Path $PSScriptRoot 'CountdownLauncher.exe'

& $compiler /nologo /target:winexe "/win32icon:$icon" "/out:$output" $source
if ($LASTEXITCODE -ne 0) {
    throw "启动器编译失败，退出码：$LASTEXITCODE"
}

Get-Item -LiteralPath $output | Select-Object FullName, Length
