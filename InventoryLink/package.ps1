param(
    [switch]$NoBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Owner = "R4V9N1"
$PackageName = "InventoryLink"
$PackageSource = Join-Path $ProjectRoot "thunderstore"
$ManifestPath = Join-Path $PackageSource "manifest.json"

if (!(Test-Path -LiteralPath $ManifestPath)) {
    throw "Missing Thunderstore manifest: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.version_number
if (!$version) {
    throw "Thunderstore manifest has no version_number."
}
if (!$manifest.description -or $manifest.description.Length -gt 250) {
    throw "Manifest description must contain 1-250 characters."
}
if ($manifest.description -notmatch "Human-directed" -or $manifest.description -notmatch "AI-assisted") {
    throw "Manifest description must include the human-directed, AI-assisted development disclosure."
}

$sourceText = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot "src") -Filter "*.cs" -File |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
$guids = [regex]::Matches(($sourceText -join "`n"), 'public const string PluginGuid\s*=\s*"([^"]+)"') |
    ForEach-Object { $_.Groups[1].Value }
if (!$guids -or ($guids | Where-Object { -not $_.StartsWith("r4v9n1.", [System.StringComparison]::OrdinalIgnoreCase) })) {
    throw "Every BepInEx PluginGuid must start with r4v9n1. Found: $($guids -join ', ')"
}
if (($sourceText -join "`n") -notmatch [regex]::Escape("Created by $Owner")) {
    throw "Creator metadata does not identify $Owner."
}
$mainPluginText = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\InventoryLinkPlugin.cs") -Raw
$versionMatch = [regex]::Match($mainPluginText, 'public const string PluginVersion\s*=\s*"([^"]+)"')
if (!$versionMatch.Success -or $versionMatch.Groups[1].Value -ne $version) {
    throw "Source plugin version must match manifest version $version."
}

if (!$NoBuild) {
    & (Join-Path $ProjectRoot "build.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }
}

$dllPath = Join-Path $ProjectRoot "dist\InventoryLink.dll"
$requiredFiles = @(
    $dllPath,
    (Join-Path $PackageSource "manifest.json"),
    (Join-Path $PackageSource "README.md"),
    (Join-Path $PackageSource "CHANGELOG.md"),
    (Join-Path $PackageSource "icon.png")
)
foreach ($path in $requiredFiles) {
    if (!(Test-Path -LiteralPath $path)) {
        throw "Missing package file: $path"
    }
}

$dllVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion
if (!$dllVersion -or !($dllVersion.StartsWith($version))) {
    throw "DLL file version $dllVersion does not match package version $version."
}

$readmeText = Get-Content -LiteralPath (Join-Path $PackageSource "README.md") -Raw
$developmentNoteIndex = $readmeText.IndexOf("## Development note", [System.StringComparison]::Ordinal)
$featuresIndex = $readmeText.IndexOf("## Features", [System.StringComparison]::Ordinal)
if ($developmentNoteIndex -lt 0 -or $featuresIndex -lt 0 -or $developmentNoteIndex -gt $featuresIndex) {
    throw "Thunderstore README must show the development note before the feature list."
}

$packageId = "$Owner-$PackageName-$version"
$stageRoot = Join-Path $ProjectRoot "dist\thunderstore\$packageId"
$zipPath = Join-Path $ProjectRoot "dist\$packageId.zip"

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
$pluginDir = Join-Path $stageRoot "plugins\InventoryLink"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

Copy-Item -LiteralPath $dllPath -Destination (Join-Path $pluginDir "InventoryLink.dll")
Copy-Item -LiteralPath (Join-Path $PackageSource "manifest.json") -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $PackageSource "README.md") -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $PackageSource "CHANGELOG.md") -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $PackageSource "icon.png") -Destination $stageRoot

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stageRoot,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName.Replace("\", "/") })
    $requiredEntries = @(
        "manifest.json",
        "README.md",
        "CHANGELOG.md",
        "icon.png",
        "plugins/InventoryLink/InventoryLink.dll"
    )
    foreach ($entry in $requiredEntries) {
        if ($entryNames -notcontains $entry) {
            throw "Generated archive is missing required entry: $entry"
        }
    }
    if ($entryNames -contains "InventoryLink.dll") {
        throw "Generated archive must not put InventoryLink.dll at the zip root."
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Owner/team: $Owner"
Write-Host "Config GUIDs: $($guids -join ', ')"
Write-Host "Packaged: $zipPath"
Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath, $dllPath |
    ForEach-Object { Write-Host "$($_.Hash)  $($_.Path)" }
