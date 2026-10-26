<#
.SYNOPSIS
    Exports a sanitised, read-only snapshot of Community and the Library tree as a test fixture.

.DESCRIPTION
    Strictly read-only: nothing is created, changed or removed except the output file.
    Exports Community entries (name, kind, target, target exists, has manifest), every Package under the
    Library roots (path, root, title, creator, content type; no other content), the first-level folders
    under the games folder with whether each holds a Package, and the folders around symlink targets.
    The user profile path is replaced with %USERPROFILE%, and Anonymise-Fixture.ps1 hides the owner's own
    folder names, so the output is safe to commit. The real folder names exist only on the command line.

.EXAMPLE
    pwsh tools/Export-Fixture.ps1 -Out tests/AddonManager.Core.Tests/Fixtures/rickard-2026-10.json -GamesRoot 'D:\Games' -LibraryRoots 'D:\Games\Addons','D:\Games\Orbx Library\msfs'
#>
param(
    [Parameter(Mandatory)] [string] $Out,
    [string] $Community = "$env:APPDATA\Microsoft Flight Simulator\Packages\Community",
    [Parameter(Mandatory)] [string] $GamesRoot,
    [Parameter(Mandatory)] [string[]] $LibraryRoots
)

$ErrorActionPreference = 'Stop'

$walkOptions = [System.IO.EnumerationOptions]::new()
$walkOptions.AttributesToSkip = [System.IO.FileAttributes]'ReparsePoint, Hidden, System'
$walkOptions.IgnoreInaccessible = $true

function Sanitize([string] $path) {
    if ($null -eq $path) { return $null }
    return $path.Replace($env:USERPROFILE, '%USERPROFILE%')
}

function Normalize([string] $path, [string] $base) {
    $p = $path
    if ($p.StartsWith('\\?\') -or $p.StartsWith('\??\')) { $p = $p.Substring(4) }
    $p = [System.IO.Path]::GetFullPath($p, $base)
    if ($p.Length -gt 3) { $p = $p.TrimEnd('\') }
    return $p
}

function Has-Manifest([string] $folder) { [System.IO.File]::Exists([System.IO.Path]::Combine($folder, 'manifest.json')) }

function Has-PackageBeneath([string] $folder) {
    if (Has-Manifest $folder) { return $true }
    foreach ($sub in [System.IO.Directory]::EnumerateDirectories($folder, '*', $walkOptions)) {
        if (Has-PackageBeneath $sub) { return $true }
    }
    return $false
}

function Read-Manifest([string] $folder) {
    try {
        return Get-Content -LiteralPath (Join-Path $folder 'manifest.json') -Raw -Encoding utf8 | ConvertFrom-Json
    } catch {
        return $null
    }
}

# Community
$communityEntries = foreach ($item in Get-ChildItem -LiteralPath $Community -Force) {
    $kind = switch ($item.LinkType) {
        'Junction' { 'Junction' }
        'SymbolicLink' { 'Symlink' }
        default { if ($item.PSIsContainer) { 'Directory' } else { 'File' } }
    }
    $target = $null; $exists = $false; $manifest = $false
    if ($kind -in 'Junction', 'Symlink') {
        $target = Normalize $item.LinkTarget $Community
        $exists = [System.IO.Directory]::Exists($target)
        $manifest = $exists -and (Has-Manifest $target)
    } elseif ($kind -eq 'Directory') {
        $manifest = Has-Manifest $item.FullName
    }
    [ordered]@{ name = $item.Name; kind = $kind; target = (Sanitize $target); targetExists = $exists; hasManifest = $manifest }
}

# Library packages
$packages = foreach ($root in $LibraryRoots) {
    $stack = [System.Collections.Generic.Stack[string]]::new()
    $stack.Push($root)
    while ($stack.Count -gt 0) {
        $folder = $stack.Pop()
        if (Has-Manifest $folder) {
            $m = Read-Manifest $folder
            [ordered]@{
                path = (Sanitize $folder); root = (Sanitize $root)
                title = $m.title; creator = $m.creator; contentType = $m.content_type
            }
            continue
        }
        foreach ($sub in [System.IO.Directory]::EnumerateDirectories($folder, '*', $walkOptions)) { $stack.Push($sub) }
    }
}

# Folder shape: first level under the games folder, and around symlink targets.
$folderPaths = @([System.IO.Directory]::EnumerateDirectories($GamesRoot, '*', $walkOptions))
foreach ($e in $communityEntries | Where-Object { $_.kind -eq 'Symlink' -and $_.targetExists }) {
    $target = $e.target.Replace('%USERPROFILE%', $env:USERPROFILE)
    $folderPaths += [System.IO.Directory]::EnumerateDirectories([System.IO.Path]::GetDirectoryName($target), '*', $walkOptions)
}
$folders = foreach ($f in $folderPaths | Sort-Object -Unique) {
    [ordered]@{ path = (Sanitize $f); hasPackageBeneath = (Has-PackageBeneath $f) }
}

$fixture = [ordered]@{
    exported = (Get-Date -Format 'yyyy-MM-dd')
    community = @($communityEntries)
    packages = @($packages | Sort-Object { $_.path })
    folders = @($folders)
}

$json = ($fixture | ConvertTo-Json -Depth 5) -replace "`r?`n", "`r`n"
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $Out), $json + "`r`n", [System.Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Anonymise-Fixture.ps1') -Fixture $Out -GamesRoot (Sanitize $GamesRoot)
Write-Host "Wrote $Out with $($fixture.community.Count) Community entries, $($fixture.packages.Count) Packages and $($fixture.folders.Count) folders."
