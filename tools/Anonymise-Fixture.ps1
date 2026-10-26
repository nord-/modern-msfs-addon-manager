<#
.SYNOPSIS
    Hides the owner's own folder names in an exported fixture, in place.

.DESCRIPTION
    Package folder names are public product names and stay. What identifies one machine is how the owner
    organised the disk, so this rewrites:
      - the root of the games folder (-GamesRoot) to C:\Sim,
      - every first-level folder under it that is not a Library root ("Tool A", "Tool B" ...) to "Tool NN",
      - every folder between the Addons Library root and a Package ("Scenery\Europe" ...) to "Group NN".
    Tool-defined roots (Orbx Library, FSDT Addon Manager, Neofly) and the Addons root itself keep their names:
    the Library proposal depends on them. Numbering follows the sorted names, so the result is repeatable.
    Export-Fixture.ps1 runs this on its output; run it by hand on an older fixture.

.EXAMPLE
    pwsh tools/Anonymise-Fixture.ps1 -Fixture tests/AddonManager.Core.Tests/Fixtures/rickard-2026-10.json -GamesRoot '<games folder on the sim machine>'
#>
param(
    [Parameter(Mandatory)] [string] $Fixture,
    [Parameter(Mandatory)] [string] $GamesRoot,
    [string] $Replacement = 'C:\Sim'
)

$ErrorActionPreference = 'Stop'

$keepFirst = @('Addons', 'Orbx Library', 'FSDT Addon Manager', 'Neofly')
$groupedRoot = 'Addons'

$path = (Resolve-Path -LiteralPath $Fixture).Path
$data = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json

# Segments below the games root, or $null for any other path.
function Split-Games([string] $p) {
    if ([string]::IsNullOrEmpty($p)) { return $null }
    $prefix = $GamesRoot.TrimEnd('\') + '\'
    if (-not $p.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { return $null }
    return , @($p.Substring($prefix.Length).Split('\'))
}

$allPaths = @($data.community.target) + @($data.packages.path) + @($data.packages.root) + @($data.folders.path)

$tools = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$groups = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($p in $allPaths) {
    $s = Split-Games $p
    if ($null -eq $s) { continue }
    if ($keepFirst -notcontains $s[0]) { [void]$tools.Add($s[0]) }
    elseif ($s[0] -eq $groupedRoot -and $s.Count -gt 2) { foreach ($mid in $s[1..($s.Count - 2)]) { [void]$groups.Add($mid) } }
}

function Number-Names($set, [string] $label) {
    $map = @{}
    $i = 0
    foreach ($name in $set) { $i++; $map[$name.ToLowerInvariant()] = '{0} {1:00}' -f $label, $i }
    return $map
}
$toolMap = Number-Names $tools 'Tool'
$groupMap = Number-Names $groups 'Group'

function Anonymise([string] $p) {
    $s = Split-Games $p
    if ($null -eq $s) { return $p }
    $out = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $s.Count; $i++) {
        $seg = $s[$i]
        if ($i -eq 0 -and $keepFirst -notcontains $seg) { $seg = $toolMap[$seg.ToLowerInvariant()] }
        elseif ($i -gt 0 -and $i -lt $s.Count - 1 -and $s[0] -eq $groupedRoot) { $seg = $groupMap[$seg.ToLowerInvariant()] }
        $out.Add($seg)
    }
    return $Replacement.TrimEnd('\') + '\' + ($out -join '\')
}

foreach ($e in $data.community) { if ($e.target) { $e.target = Anonymise $e.target } }
foreach ($p in $data.packages) { $p.path = Anonymise $p.path; $p.root = Anonymise $p.root }
foreach ($f in $data.folders) { $f.path = Anonymise $f.path }

$json = ($data | ConvertTo-Json -Depth 5) -replace "`r?`n", "`r`n"
[System.IO.File]::WriteAllText($path, $json + "`r`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Anonymised $path ($($tools.Count) tool folders, $($groups.Count) groups)."
