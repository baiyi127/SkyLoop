#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryManifestPath,
    [Parameter(Mandatory)][string]$ArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Valid([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-PublicMetadata($Value, [string]$Location) {
    if ($null -eq $Value) { return }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            Assert-Valid ($key -notmatch '(?i)^(WorkingPluginId|InstalledFromUrl|Disabled|Testing|ScheduledForDeletion)$') "Local installation field at $Location.$key"
            Assert-Valid ($key -notmatch '(?i)(password|passwd|secret|credential|private.?key|api.?key|token|authorization)') "Credential-like field at $Location.$key"
            Assert-PublicMetadata $Value[$key] "$Location.$key"
        }
        return
    }
    if ($Value -is [string]) {
        Assert-Valid ($Value -notmatch '(?i)(?<![a-z0-9])[a-z]:[\\/]|^\\\\|^/|file://') "Local absolute path at $Location"
        Assert-Valid ($Value -notmatch '(?i)\bgh[pousr]_[a-z0-9_]{20,}|\bgithub_pat_[a-z0-9_]{20,}|-----BEGIN[^\r\n]*PRIVATE KEY|\bBearer\s+[a-z0-9._~+/=-]{12,}') "Possible embedded credential at $Location"
        # Reject credentials in every URL, including optional marketing image links.
        foreach ($match in [regex]::Matches($Value, 'https?://[^\s"<>]+')) {
            $uri = [uri]$match.Value
            Assert-Valid ([string]::IsNullOrEmpty($uri.UserInfo)) "URL credentials at $Location"
            Assert-Valid ($uri.Query -notmatch '(?i)(token|key|password|secret|signature|credential|authorization)=') "URL credential parameter at $Location"
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $index = 0
        foreach ($child in $Value) { Assert-PublicMetadata $child "$Location[$index]"; $index++ }
    }
}

$repositoryFile = (Resolve-Path -LiteralPath $RepositoryManifestPath).Path
$archiveFile = (Resolve-Path -LiteralPath $ArchivePath).Path
$repositoryText = [System.IO.File]::ReadAllText($repositoryFile)
Assert-Valid ($repositoryText.TrimStart().StartsWith('[')) 'Repository manifest must be a top-level JSON array.'
$entries = ConvertFrom-Json -InputObject $repositoryText -AsHashtable -NoEnumerate
Assert-Valid ($entries -is [array] -and $entries.Count -eq 1) 'Expected exactly one SkyLoop repository entry.'
Assert-PublicMetadata $entries 'repository'
$entry = $entries[0]
Assert-Valid ($entry -is [System.Collections.IDictionary]) 'Repository entry must be a JSON object.'

foreach ($field in @('InternalName', 'Name', 'Author', 'Description', 'Punchline', 'AssemblyVersion')) {
    Assert-Valid ($entry.Contains($field) -and $entry[$field] -is [string] -and -not [string]::IsNullOrWhiteSpace($entry[$field])) "Missing or invalid repository field: $field"
}
Assert-Valid ($entry.InternalName -ceq 'SkyLoop') 'InternalName must be SkyLoop.'
Assert-Valid ($entry.AssemblyVersion -match '^\d+\.\d+\.\d+\.\d+$') 'AssemblyVersion must contain four numeric components.'
Assert-Valid ($entry.Contains('DalamudApiLevel') -and $entry.DalamudApiLevel -is [long] -and $entry.DalamudApiLevel -eq 15) 'SkyLoop currently targets CN Dalamud API 15.'
Assert-Valid ($entry.Contains('ApplicableVersion') -and $entry.ApplicableVersion -ceq 'any') 'Expected ApplicableVersion=any.'

foreach ($flag in @('IsHide', 'IsTestingExclusive')) {
    Assert-Valid ($entry.Contains($flag) -and $entry[$flag] -is [bool] -and -not $entry[$flag]) "Expected $flag=false as a JSON boolean for this public release."
}
foreach ($field in @('RepoUrl', 'DownloadLinkInstall', 'DownloadLinkUpdate', 'DownloadLinkTesting')) {
    Assert-Valid ($entry.Contains($field) -and $entry[$field] -is [string]) "Missing URL: $field"
    $uri = $null
    Assert-Valid ([uri]::TryCreate($entry[$field], [UriKind]::Absolute, [ref]$uri) -and $uri.Scheme -eq 'https') "Expected an absolute HTTPS URL: $field"
}
Assert-Valid ($entry.RepoUrl.TrimEnd('/') -ceq 'https://github.com/baiyi127/SkyLoop') 'RepoUrl must identify the published baiyi127/SkyLoop repository.'
foreach ($field in @('DownloadLinkInstall', 'DownloadLinkUpdate', 'DownloadLinkTesting')) {
    Assert-Valid ($entry[$field] -cmatch '^https://github\.com/baiyi127/SkyLoop/releases/download/[^/]+/[^/?#]+\.zip$') "$field must pin a ZIP asset to a specific GitHub release tag."
    Assert-Valid ($entry[$field] -ceq $entry.DownloadLinkInstall) 'The release uses the same ZIP for installation, updates and the optional testing download.'
}
Assert-Valid ($entry.Contains('LastUpdate') -and $entry.LastUpdate -is [long] -and $entry.LastUpdate -gt 0) 'LastUpdate must be a positive Unix timestamp JSON number.'

$zip = [System.IO.Compression.ZipFile]::OpenRead($archiveFile)
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
    Assert-Valid (@($names | Sort-Object -Unique).Count -eq $names.Count) 'Archive has duplicate entry paths.'
    foreach ($name in $names) {
        Assert-Valid ($name -notmatch '(^[\\/]|^[a-zA-Z]:|(^|[\\/])\.\.([\\/]|$))') 'Archive contains an absolute or traversing path.'
    }
    Assert-Valid ($names -ccontains 'SkyLoop.dll') 'Archive must contain SkyLoop.dll at its root.'
    Assert-Valid ($names -ccontains 'SkyLoop.json') 'Archive must contain SkyLoop.json at its root.'
    foreach ($name in $names) {
        Assert-Valid ($name -notmatch '(?i)(^|[\\/])(Dalamud|FFXIVClientStructs|InteropGenerator\.Runtime|Lumina(?:\.Excel)?|Dalamud\.Bindings\.ImGui)\.dll$') 'Archive must not bundle host runtime assemblies.'
    }

    $manifestReader = [System.IO.StreamReader]::new($zip.GetEntry('SkyLoop.json').Open())
    try { $embedded = ConvertFrom-Json -InputObject $manifestReader.ReadToEnd() -AsHashtable }
    finally { $manifestReader.Dispose() }
    Assert-PublicMetadata $embedded 'archive.SkyLoop.json'
    foreach ($field in @('InternalName', 'AssemblyVersion', 'DalamudApiLevel')) {
        Assert-Valid ($embedded.Contains($field) -and $embedded[$field] -ceq $entry[$field]) "Repository/package mismatch: $field"
    }

    # Read only PE metadata. Do not load or execute the plugin in the PowerShell process.
    $dllStream = $zip.GetEntry('SkyLoop.dll').Open()
    $dllBytes = [System.IO.MemoryStream]::new()
    try { $dllStream.CopyTo($dllBytes) }
    finally { $dllStream.Dispose() }
    $dllBytes.Position = 0
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($dllBytes)
    try {
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $assembly = $metadata.GetAssemblyDefinition()
        $assemblyName = $metadata.GetString($assembly.Name)
        $assemblyVersion = $assembly.Version.ToString()
        Assert-Valid ($assemblyName -ceq $entry.InternalName) 'Compiled assembly name differs from InternalName.'
        Assert-Valid ($assemblyVersion -ceq $entry.AssemblyVersion) 'Compiled assembly version differs from repository and package manifests.'
    }
    finally { $pe.Dispose(); $dllBytes.Dispose() }
}
finally { $zip.Dispose() }

Write-Output "PASS: SkyLoop $($entry.AssemblyVersion), API $($entry.DalamudApiLevel); repository, ZIP and compiled assembly agree."
