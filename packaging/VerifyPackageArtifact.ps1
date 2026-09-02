param(
    [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
    [ValidateSet('Debug', 'Staging', 'Release')][string]$Configuration = 'Release',
    [string]$ExpectedVersion = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force

if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory = Join-Path $repositoryRoot $ArtifactDirectory }
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) { throw "Artifact directory '$ArtifactDirectory' does not exist." }

$packages = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Filter 'Icod.Host.*.nupkg' -File | Where-Object { -not $_.Name.EndsWith('.symbols.nupkg', [System.StringComparison]::OrdinalIgnoreCase) } | Sort-Object Name)
if (1 -ne $packages.Count) { throw "Expected exactly one Icod.Host .nupkg; found $($packages.Count)." }
$package = $packages[0]
$metadata = Get-PackageMetadata -PackagePath $package.FullName
if ('Icod.Host' -ne $metadata.Id) { throw "Unexpected package ID '$($metadata.Id)'." }
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $ExpectedVersion -ne $metadata.Version) { throw "Package version '$($metadata.Version)' does not match expected '$ExpectedVersion'." }

$symbols = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Filter "Icod.Host.$($metadata.Version).snupkg" -File)
if (1 -ne $symbols.Count) { throw "Expected matching Icod.Host symbol package; found $($symbols.Count)." }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\\','/') })
    foreach ($required in @('README.md','LICENSE','icon.png','lib/net10.0/Icod.Host.dll','lib/net10.0/Icod.Host.xml')) {
        if ($required -notin $entries) { throw "Package '$($package.Name)' is missing '$required'." }
    }
} finally { $archive.Dispose() }

$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($symbols[0].FullName)
try {
    $symbolEntries = @($symbolArchive.Entries | ForEach-Object { $_.FullName.Replace('\\','/') })
    if ('lib/net10.0/Icod.Host.pdb' -notin $symbolEntries -and 'Icod.Host.pdb' -notin $symbolEntries) {
        throw "Symbol package '$($symbols[0].Name)' does not contain Icod.Host.pdb."
    }
} finally { $symbolArchive.Dispose() }

Write-Host "Exact Icod.Host package verification completed successfully for $($metadata.Version) ($Configuration)."
