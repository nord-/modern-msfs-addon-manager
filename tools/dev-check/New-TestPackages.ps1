<#
.SYNOPSIS
    Writes a test Packages root for the manual dev checks of plan 5b.

.DESCRIPTION
    Creates Packages\Community, Packages\Official\Steam and a Library with three Scenery Packages (pkg-a, pkg-b, pkg-c).
    Community gets two junctions, pkg-a and pkg-b, that point into the Library, so the app starts with two linked and one
    unlinked Package. Only -Root is written to. The folder must not exist, or must exist and be empty.

.EXAMPLE
    pwsh tools/dev-check/New-TestPackages.ps1 -Root "$env:TEMP\devcheck"
#>
param(
    [Parameter(Mandatory)] [string] $Root
)

$ErrorActionPreference = 'Stop'

$full = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Root)
if (Test-Path -LiteralPath $full) {
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "$full exists and is not a folder." }
    if (Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1) { throw "$full is not empty. Choose a new folder." }
}

$packages = Join-Path $full 'Packages'
$community = Join-Path $packages 'Community'
$library = Join-Path $full 'Library'

New-Item -ItemType Directory -Force -Path $community | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path (Join-Path $packages 'Official') 'Steam') | Out-Null
New-Item -ItemType Directory -Force -Path $library | Out-Null

foreach ($name in 'pkg-a', 'pkg-b', 'pkg-c') {
    $dir = New-Item -ItemType Directory -Path (Join-Path $library $name)
    $manifest = '{"title":"' + $name + '","content_type":"SCENERY","package_version":"1.0.0"}'
    Set-Content -LiteralPath (Join-Path $dir.FullName 'manifest.json') -Value $manifest -Encoding utf8NoBOM
}

foreach ($name in 'pkg-a', 'pkg-b') {
    New-Item -ItemType Junction -Path (Join-Path $community $name) -Target (Join-Path $library $name) | Out-Null
}

Write-Output "Packages root to pick in the app: $packages"
Write-Output 'Start the app against this test data (run from the repository root):'
Write-Output "  dotnet run --project src/AddonManager.App -- --data-dir `"$full\data`""
