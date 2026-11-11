#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-PublicationGitBytes {
    param([string]$Root, [string[]]$Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $start.CreateNoWindow = $true
    foreach ($argument in @('-C',$Root) + $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $buffer = [IO.MemoryStream]::new()
    try {
        [void]$process.Start()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Git snapshot failed ($($process.ExitCode)): $errorText" }
        return ,$buffer.ToArray()
    } finally { $buffer.Dispose(); $process.Dispose() }
}

function New-PublicationSnapshot {
    param([string]$Root, [string]$Source, [string]$Commit)
    $utf8 = [Text.UTF8Encoding]::new($false,$true)
    $rootPath = $utf8.GetString((Invoke-PublicationGitBytes $Root @('rev-parse','--show-toplevel'))).Trim()
    $treeArgs = if ($Source -eq 'Index') { @('write-tree') } else { @('rev-parse','--verify','--end-of-options',"$Commit^{tree}") }
    $tree = $utf8.GetString((Invoke-PublicationGitBytes $rootPath $treeArgs)).Trim()
    $files = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $utf8.GetString((Invoke-PublicationGitBytes $rootPath @('ls-tree','-r','-z','--full-tree',$tree))).Split([char]0)) {
        if (-not $entry) { continue }
        $metadata,$path = $entry.Split("`t",2)
        $parts = $metadata.Split(' ')
        if ($parts[1] -ne 'blob') { continue }
        $files.Add($path,$parts[2])
        $parent = $path
        while ($parent.Contains('/')) { $parent = $parent.Substring(0,$parent.LastIndexOf('/')); [void]$directories.Add($parent) }
    }
    [pscustomobject]@{Root=$rootPath;Tree=$tree;Source=$Source;Files=$files;Directories=$directories;Cache=@{};Utf8=$utf8}
}

function Read-PublicationBlob {
    param([object]$Snapshot,[string]$Path)
    if (-not $Snapshot.Cache.ContainsKey($Path)) {
        $Snapshot.Cache[$Path] = Invoke-PublicationGitBytes $Snapshot.Root @('cat-file','blob',$Snapshot.Files[$Path])
    }
    return ,([byte[]]$Snapshot.Cache[$Path])
}

function Resolve-PublicationReference {
    param([object]$Snapshot,[string]$Document,[string]$Target,[switch]$RepositoryRelative)
    $targetPath = [Uri]::UnescapeDataString(($Target -split '[?#]',2)[0]).Replace('\','/')
    if (-not $targetPath) { return '' }
    $directory = if ($RepositoryRelative -or $targetPath.StartsWith('/')) { $Snapshot.Root } else { Join-Path $Snapshot.Root ([IO.Path]::GetDirectoryName($Document)) }
    $absolute = [IO.Path]::GetFullPath((Join-Path $directory $targetPath.TrimStart('/')))
    $relative = [IO.Path]::GetRelativePath($Snapshot.Root,$absolute).Replace('\','/')
    if ($relative -eq '..' -or $relative.StartsWith('../')) { throw "Reference leaves repository: $Document -> $Target" }
    return $relative.TrimEnd('/')
}

function Test-PublicationHashReference {
    param([object]$Context,[string]$Document,[string]$Target,[string]$Expected,[bool]$RepositoryRelative)
    # app、verification 和 logs 是既有未版本化运行产物；这些身份记录不声称属于Git树。
    if ($Target -match '^(?:[a-z][a-z0-9+.-]*:|//|(?:app|verification|logs)/)' -and -not $Context.Snapshot.Files.ContainsKey($Target)) {
        $Context.Result.ExternalReferences++
        return
    }
    $path = Resolve-PublicationReference $Context.Snapshot $Document $Target -RepositoryRelative:$RepositoryRelative
    $Context.Result.HashReferences++
    if (-not $Context.Snapshot.Files.ContainsKey($path)) {
        $Context.Failures.Add("MissingEvidence: $Document -> $Target [$($Context.Snapshot.Tree)]")
        return
    }
    if ($Expected -cnotmatch '^[0-9A-Fa-f]{64}$') { $Context.Failures.Add("InvalidHash: $Document -> $Target"); return }
    $bytes = Read-PublicationBlob $Context.Snapshot $path
    $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    if ($actual -cne $Expected.ToUpperInvariant()) { $Context.Failures.Add("HashMismatch: $Document -> $Target; expected=$Expected; actual=$actual"); return }
    if ($path.EndsWith('.json',[StringComparison]::OrdinalIgnoreCase)) { Visit-PublicationEvidenceDocument $Context $path }
}

function Visit-PublicationEvidenceObject {
    param([object]$Context,[string]$Document,[object]$Node,[bool]$RepositoryRelative=$false)
    if ($null -eq $Node -or $Node -is [string] -or $Node -is [ValueType]) { return }
    if ($Node -is [System.Collections.IDictionary]) {
        if ($Node.Contains('path') -and $Node['path'] -is [string]) {
            $hashKey = if ($Node.Contains('sha256')) { 'sha256' } elseif ($Node.Contains('pngSha256')) { 'pngSha256' } else { $null }
            if ($hashKey) { Test-PublicationHashReference $Context $Document $Node.path ([string]$Node[$hashKey]) $RepositoryRelative }
        }
        foreach ($key in @($Node.Keys)) {
            if ($key -notlike 'run_artifact_*' -and $key.EndsWith('_sha256',[StringComparison]::Ordinal)) {
                $pathKey = $key.Substring(0,$key.Length-7)
                if ($Node.Contains($pathKey) -and $Node[$pathKey] -is [string]) {
                    Test-PublicationHashReference $Context $Document $Node[$pathKey] ([string]$Node[$key]) $RepositoryRelative
                }
            }
            $rootRelative = $RepositoryRelative -or $key -in @('sourceBinding','current_source_bindings','captures')
            Visit-PublicationEvidenceObject $Context $Document $Node[$key] $rootRelative
        }
    } elseif ($Node -is [System.Collections.IEnumerable]) {
        foreach ($child in $Node) { Visit-PublicationEvidenceObject $Context $Document $child $RepositoryRelative }
    }
}

function Visit-PublicationEvidenceDocument {
    param([object]$Context,[string]$Path)
    if (-not $Context.Visited.Add($Path)) { return }
    try {
        $record = $Context.Snapshot.Utf8.GetString((Read-PublicationBlob $Context.Snapshot $Path)) | ConvertFrom-Json -AsHashtable -Depth 100
        Visit-PublicationEvidenceObject $Context $Path $record
    } catch { $Context.Failures.Add("InvalidEvidence: $Path; $($_.Exception.Message)") }
}

function Write-CountdownEvidenceJson {
    <# Versioned evidence uses UTF-8 without BOM and LF before callers compute hashes. This writer never repairs stored hash references. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][object]$Value)
    $json = (ConvertTo-Json -InputObject $Value -Depth 100).Replace("`r`n","`n").TrimEnd("`r","`n") + "`n"
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Path),$json,[Text.UTF8Encoding]::new($false))
}

function Test-CountdownPublicationEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot,
        [ValidateSet('Index','Commit')][string]$Source = 'Index', [string]$Commit = 'HEAD')
    $failures = [Collections.Generic.List[string]]::new()
    $result = [ordered]@{Valid=$false;Source=$Source;Tree=$null;MarkdownFiles=0;Links=0;HashReferences=0;EvidenceRoots=0;ExternalReferences=0;Failures=@()}
    try {
        $snapshot = New-PublicationSnapshot $RepositoryRoot $Source $Commit
        $result.Tree = $snapshot.Tree
        foreach ($document in @($snapshot.Files.Keys | Where-Object { $_.EndsWith('.md',[StringComparison]::OrdinalIgnoreCase) } | Sort-Object)) {
            $result.MarkdownFiles++
            $text = $snapshot.Utf8.GetString((Read-PublicationBlob $snapshot $document))
            foreach ($match in [regex]::Matches($text,'\[[^\]]*\]\((?:<(?<target>[^>]+)>|(?<target>[^)\s]+))(?:\s+"[^"]*")?\)')) {
                $target = $match.Groups['target'].Value
                if ($target -match '^(?:[a-z][a-z0-9+.-]*:|//)' -or $target.StartsWith('#')) { continue }
                $resolved = Resolve-PublicationReference $snapshot $document $target
                if (-not $resolved) { continue }
                $result.Links++
                if (-not $snapshot.Files.ContainsKey($resolved) -and -not $snapshot.Directories.Contains($resolved)) {
                    $failures.Add("MissingLink: $document -> $target [$Source@$($snapshot.Tree)]")
                }
            }
        }
        $context = [pscustomobject]@{Snapshot=$snapshot;Result=$result;Failures=$failures;Visited=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)}
        $requiredRoots = @()
        $registryPath = 'docs/agents/publication-evidence.json'
        if ($snapshot.Files.ContainsKey($registryPath)) {
            $registry = $snapshot.Utf8.GetString((Read-PublicationBlob $snapshot $registryPath)) | ConvertFrom-Json
            if ($registry.schemaVersion -ne 1 -or $registry.roots -isnot [array]) { throw 'Invalid publication evidence root registry.' }
            $requiredRoots = @($registry.roots)
            foreach ($root in $requiredRoots) {
                if ($root -notlike 'docs/evidence/*.json' -or -not $snapshot.Files.ContainsKey($root)) { $failures.Add("MissingEvidenceRoot: $root [$($snapshot.Tree)]") }
            }
        }
        foreach ($path in @($snapshot.Files.Keys | Where-Object { $_ -like 'docs/evidence/*.json' } | Sort-Object)) {
            $record = $snapshot.Utf8.GetString((Read-PublicationBlob $snapshot $path)) | ConvertFrom-Json -AsHashtable -Depth 100
            if ($record -is [System.Collections.IDictionary] -and $record.Contains('hash_scope')) {
                if ($record.hash_scope -cne 'git-blob') { $failures.Add("InvalidHashScope: $path"); continue }
                $result.EvidenceRoots++
                Visit-PublicationEvidenceDocument $context $path
            } elseif ($path -in $requiredRoots) { $failures.Add("MissingHashScope: $path") }
        }
    } catch { $failures.Add($_.Exception.Message) }
    $result.Valid = $failures.Count -eq 0
    $result.Failures = @($failures)
    [pscustomobject]$result
}

Export-ModuleMember -Function Test-CountdownPublicationEvidence, Write-CountdownEvidenceJson
