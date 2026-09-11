param(
    [switch]$NoBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$LocalProjectRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\EquipmentSheet"
$DistDir = Join-Path $LocalProjectRoot "dist"
$PackageStageRoot = Join-Path $LocalProjectRoot "package-stage"
$ReleaseDir = "G:\My Drive\build\Valheim\releases\EquipmentSheet"
$Owner = "R4V9N1"
$PackageName = "EquipmentSheet"
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
if ($manifest.description -notmatch "I use AI" -or $manifest.description -notmatch "manually test" -or $manifest.description -notmatch "remain my own") {
    throw "Manifest description must include the human-directed, AI-assisted development disclosure."
}
if ($manifest.dependencies -notcontains "denikson-BepInExPack_Valheim-5.4.2350") {
    throw "Manifest is missing the pinned BepInExPack_Valheim dependency."
}

$sourceText = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\EquipmentSheetPlugin.cs") -Raw
$guidMatch = [regex]::Match($sourceText, 'public const string PluginGuid\s*=\s*"([^"]+)"')
if (!$guidMatch.Success) {
    throw "Could not find the BepInEx PluginGuid."
}
$pluginGuid = $guidMatch.Groups[1].Value
if (!$pluginGuid.StartsWith("r4v9n1.", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "BepInEx PluginGuid must start with r4v9n1. Found: $pluginGuid"
}
if ($sourceText -notmatch [regex]::Escape("Created by $Owner")) {
    throw "Creator metadata does not identify $Owner."
}
$versionMatch = [regex]::Match($sourceText, 'public const string PluginVersion\s*=\s*"([^"]+)"')
if (!$versionMatch.Success -or $versionMatch.Groups[1].Value -ne $version) {
    throw "Source plugin version must match manifest version $version."
}

if (!$NoBuild) {
    & (Join-Path $ProjectRoot "build.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }
}

$dllPath = Join-Path $DistDir "EquipmentSheet.dll"
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

$readmeText = Get-Content -LiteralPath (Join-Path $PackageSource "README.md") -Raw
$developmentNoteIndex = $readmeText.IndexOf("## Development note", [System.StringComparison]::Ordinal)
$featuresIndex = $readmeText.IndexOf("## Features", [System.StringComparison]::Ordinal)
if ($developmentNoteIndex -lt 0 -or $featuresIndex -lt 0 -or $developmentNoteIndex -gt $featuresIndex) {
    throw "Thunderstore README must show the development note before the feature list."
}

$dllVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion
if (!$dllVersion -or !($dllVersion.StartsWith($version))) {
    throw "DLL file version $dllVersion does not match package version $version."
}

$packageId = "$Owner-$PackageName-$version"
$stageRoot = Join-Path $PackageStageRoot $packageId
$zipPath = Join-Path $ReleaseDir "$packageId.zip"

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
$pluginDir = Join-Path $stageRoot "plugins\EquipmentSheet"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

Copy-Item -LiteralPath $dllPath -Destination (Join-Path $pluginDir "EquipmentSheet.dll")
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
        "plugins/EquipmentSheet/EquipmentSheet.dll"
    )
    foreach ($entry in $requiredEntries) {
        if ($entryNames -notcontains $entry) {
            throw "Generated archive is missing required entry: $entry"
        }
    }
    if ($entryNames -contains "EquipmentSheet.dll") {
        throw "Generated archive must not put EquipmentSheet.dll at the zip root."
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Owner/team: $Owner"
Write-Host "Config GUID: $pluginGuid"
Write-Host "Packaged: $zipPath"
Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath, $dllPath |
    ForEach-Object { Write-Host "$($_.Hash)  $($_.Path)" }
