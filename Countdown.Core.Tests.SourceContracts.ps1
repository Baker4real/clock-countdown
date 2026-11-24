# Countdown.Core.Tests.ps1 的 SourceContracts 分段：入口按开关点源执行，共用入口的参数、断言函数与已建立的状态，不单独运行。

$buildSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Build-CountdownApplication.ps1'), [System.Text.Encoding]::UTF8)
$buildPreflightSource = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tools\Countdown.BuildPreflight.psm1'), [System.Text.Encoding]::UTF8)
$buildPreflightScript = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tools\Test-CountdownBuildPreflight.ps1'), [System.Text.Encoding]::UTF8)
Assert-Equal $true ($buildSource.Contains("tools\Countdown.BuildPreflight.psm1") -and $buildSource.Contains('Resolve-CountdownBuildDotnet') -and $buildSource.Contains('Get-CountdownBuildPaths')) 'Build reuses the shared SDK and clean-path preflight module'
$managedCleanIndex = $buildSource.IndexOf('invoke-safe-tree-delete.ps1')
$releaseContractImportIndex = $buildSource.IndexOf("Import-Module (Join-Path `$PSScriptRoot 'Countdown.ReleaseContract.psm1')")
Assert-Equal $true ($managedCleanIndex -ge 0 -and $releaseContractImportIndex -gt $managedCleanIndex) 'Build -Clean removes app output before the release contract import reads a stale published payload'
Assert-Equal $true ($buildPreflightSource.Contains("`$env:COUNTDOWN_DOTNET_EXE") -and $buildPreflightSource.Contains("D:\Codex\tools\dotnet\10.0\dotnet.exe") -and $buildPreflightSource.Contains('Get-Command dotnet')) 'Shared preflight preserves parameter, environment, managed and PATH SDK resolution order'
Assert-Equal $true ($buildPreflightSource.Contains("Join-Path `$RepositoryRoot 'global.json'") -and $buildPreflightSource.Contains('Test-CountdownSdkVersionMatch')) 'Shared preflight resolves and checks the repository global.json SDK contract'
Assert-Equal $true ($buildPreflightScript.Contains('[switch]$RequireClean') -and $buildPreflightScript.Contains('[switch]$Json') -and $buildPreflightScript.Contains('Failures')) 'Build preflight entry point exposes clean gating and structured failure output'
Assert-Equal 3 ([regex]::Matches($buildSource, '-p:IncludeSourceRevisionInInformationalVersion=false').Count) 'All three self-contained publish commands exclude the Git revision so committed manifest hashes remain reproducible'
$windowVerificationPublishStart = $buildSource.IndexOf('& $dotnet publish $windowVerificationProject -c Release -r win-x64 --self-contained true')
$windowVerificationPublishEnd = if ($windowVerificationPublishStart -ge 0) {
    $buildSource.IndexOf('if ($LASTEXITCODE -ne 0)', $windowVerificationPublishStart)
}
else {
    -1
}
$windowVerificationPublishSource = if ($windowVerificationPublishEnd -gt $windowVerificationPublishStart) {
    $buildSource.Substring(
        $windowVerificationPublishStart,
        $windowVerificationPublishEnd - $windowVerificationPublishStart)
}
else {
    ''
}
Assert-Equal $true (
    $buildSource.Contains('src\MinminCountdown.WindowVerification\MinminCountdown.WindowVerification.csproj') -and
    $windowVerificationPublishSource.Contains('-p:PublishSingleFile=true') -and
    $windowVerificationPublishSource.Contains('-p:IncludeSourceRevisionInInformationalVersion=false')) `
    'Window verification project publish keeps its exact project path, win-x64 self-contained command and reproducible Git revision exclusion together'
Assert-Equal $true ($buildPreflightSource.Contains("Join-Path `$project 'bin'") -and $buildPreflightSource.Contains("Join-Path `$project 'obj'")) 'Shared build path contract includes managed bin and obj directories for each project'
Assert-Equal $true $buildSource.Contains('$buildPaths = Get-CountdownBuildPaths -RepositoryRoot $PSScriptRoot') 'Clean build derives candidates from the shared build path contract'
Assert-Equal $true $buildSource.Contains('$managedCleanCandidates = @($buildPaths.CleanCandidates)') 'Clean build passes the shared candidate list to the managed cleanup gate'
Assert-Equal $true ($buildPreflightSource.Contains("'MinminCountdown.Core'") -and $buildPreflightSource.Contains("'MinminCountdown'") -and $buildPreflightSource.Contains("'MinminCountdown.Verification'") -and $buildPreflightSource.Contains("'MinminCountdown.WindowVerification'")) 'Shared build path contract covers all four C# projects'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath.StartsWith($repositoryRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)') 'Clean build rejects absolute removal targets outside the repository root'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath -notin $expectedCleanPaths') 'Clean build validates every absolute removal target against its exact allowlist'
Assert-Equal $true $buildSource.Contains("`$runEvidenceCapabilityRoot = 'D:\agents\run-evidence'") 'Clean build uses the governed run-evidence capability root for isolated release clones'
Assert-Equal $true $buildSource.Contains('$resolvedCleanPath.StartsWith($runEvidenceRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)') 'Clean build detects output paths inside the governed run-evidence tree'
Assert-Equal $true $buildSource.Contains('-CapabilityRoot $cleanupCapabilityRoot') 'Clean build passes the resolved stable capability root to safe-delete'
Assert-Equal $false ('Build-CountdownApplication.ps1' -in @($releaseContract.PackageFiles)) 'Build script stays out of the office package'
