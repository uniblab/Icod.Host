param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$DestinationDirectory,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
foreach ($name in @('SourceDirectory','DestinationDirectory')) {
    $value = Get-Variable -Name $name -ValueOnly
    if (-not [System.IO.Path]::IsPathRooted($value)) { $value = Join-Path $repositoryRoot $value }
    Set-Variable -Name $name -Value ([System.IO.Path]::GetFullPath($value))
}
if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) { throw "Source package directory '$SourceDirectory' does not exist." }
if (Test-Path -LiteralPath $DestinationDirectory) { Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
$packages = @(Get-ChildItem -LiteralPath $SourceDirectory -Filter 'Icod.Host.*.nupkg' -File | Where-Object { -not $_.Name.EndsWith('.symbols.nupkg', [System.StringComparison]::OrdinalIgnoreCase) })
$selected = @()
foreach ($package in $packages) {
    $metadata = Get-PackageMetadata -PackagePath $package.FullName
    if ($metadata.Version -eq $ExpectedVersion) {
        Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $DestinationDirectory $package.Name)
        $symbolPath = Join-Path $SourceDirectory "Icod.Host.$ExpectedVersion.snupkg"
        if (-not (Test-Path -LiteralPath $symbolPath -PathType Leaf)) { throw "Expected symbol package '$symbolPath'." }
        Copy-Item -LiteralPath $symbolPath -Destination (Join-Path $DestinationDirectory (Split-Path $symbolPath -Leaf))
        $selected += $package.Name
    }
}
if (1 -ne $selected.Count) { throw "Expected exactly one Icod.Host package matching '$ExpectedVersion'; found $($selected.Count)." }
Write-Host "Selected Icod.Host $ExpectedVersion for release."
