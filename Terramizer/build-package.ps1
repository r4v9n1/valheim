param(
    [string]$ValheimDir = $env:VALHEIM_DIR,
    [string]$GameManagedDir = $env:VALHEIM_MANAGED_DIR,
    [string]$BepInExCoreDir = $env:BEPINEX_CORE_DIR,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Owner = "R4V9N1"
$PackageVersion = "0.9.5"
$PackageName = "Terramizer"
$TemplateDir = Join-Path $ProjectRoot "thunderstore"
$DllPath = Join-Path $ProjectRoot "dist\Terramizer.dll"
$ArtifactsDir = Join-Path $ProjectRoot "artifacts"
$ArchivePath = Join-Path $ArtifactsDir "$Owner-$PackageName-$PackageVersion.zip"
$ChecksumPath = "$ArchivePath.sha256"

if (!$SkipBuild) {
    $buildArgs = @{}
    if ($ValheimDir) { $buildArgs.ValheimDir = $ValheimDir }
    if ($GameManagedDir) { $buildArgs.GameManagedDir = $GameManagedDir }
    if ($BepInExCoreDir) { $buildArgs.BepInExCoreDir = $BepInExCoreDir }
    & (Join-Path $ProjectRoot "build.ps1") @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Terramizer build failed with exit code $LASTEXITCODE."
    }
}

$requiredTemplateFiles = @("manifest.json", "README.md", "CHANGELOG.md", "icon.png")
foreach ($name in $requiredTemplateFiles) {
    $path = Join-Path $TemplateDir $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Thunderstore package file: $path"
    }
}

if (!(Test-Path -LiteralPath $DllPath -PathType Leaf)) {
    throw "Missing built plugin: $DllPath"
}

$manifestPath = Join-Path $TemplateDir "manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.name -ne $PackageName) {
    throw "Manifest name must be $PackageName."
}
if ($manifest.version_number -ne $PackageVersion) {
    throw "Manifest version $($manifest.version_number) does not match package version $PackageVersion."
}
if (!$manifest.description -or $manifest.description.Length -gt 250) {
    throw "Manifest description must contain 1-250 characters."
}
if ($manifest.description -notmatch "Human-directed" -or $manifest.description -notmatch "AI-assisted") {
    throw "Manifest description must include the human-directed, AI-assisted development disclosure."
}
if ($manifest.dependencies -notcontains "denikson-BepInExPack_Valheim-5.4.2333") {
    throw "Manifest is missing the pinned BepInExPack_Valheim dependency."
}

$sourceText = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\TerramizerPlugin.cs") -Raw
$patchText = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\PerformancePatches.cs") -Raw
if ($sourceText -notmatch 'public const string PluginGuid\s*=\s*"r4v9n1\.terramizer"') {
    throw "BepInEx PluginGuid must be r4v9n1.terramizer."
}
if ($sourceText -notmatch [regex]::Escape("Created by $Owner")) {
    throw "Creator metadata does not identify $Owner."
}
if ($sourceText -notmatch 'public const string PluginVersion\s*=\s*"0\.9\.5"') {
    throw "Terramizer source version must be 0.9.5."
}
$forbiddenRuntimeHooks = @(
    "HeightmapApplyModifiers",
    "TerrainComp",
    "TerrainModifier",
    "ZNetScene",
    "typeof(WearNTear)",
    "typeof(ZDO",
    "typeof(ZNet",
    "ReuseHeightmapModifierBuffers",
    "CacheTerrainCompilerLookupsV2"
)
foreach ($hook in $forbiddenRuntimeHooks) {
    if ($patchText -match [regex]::Escape($hook)) {
        throw "Terramizer must not contain the retired terrain/world mutation hook $hook."
    }
}

$dllVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($DllPath).FileVersion
if (!$dllVersion -or !($dllVersion.StartsWith($PackageVersion))) {
    throw "DLL file version $dllVersion does not match package version $PackageVersion."
}
$readmeText = Get-Content -LiteralPath (Join-Path $TemplateDir "README.md") -Raw
$developmentNoteIndex = $readmeText.IndexOf("## Development note", [System.StringComparison]::Ordinal)
$featuresIndex = $readmeText.IndexOf("## Performance features", [System.StringComparison]::Ordinal)
if ($developmentNoteIndex -lt 0 -or $featuresIndex -lt 0 -or $developmentNoteIndex -gt $featuresIndex) {
    throw "Thunderstore README must show the development note before the performance feature list."
}
$dllAssembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($DllPath))
$dllReferences = @($dllAssembly.GetReferencedAssemblies() | ForEach-Object { $_.Name })
foreach ($reference in @("0Harmony", "assembly_valheim")) {
    if ($dllReferences -notcontains $reference) {
        throw "Restored client smoothing build is missing required reference $reference."
    }
}

Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $TemplateDir "icon.png"
$icon = [Drawing.Image]::FromFile($iconPath)
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256) {
        throw "Thunderstore icon must be exactly 256x256 pixels; found $($icon.Width)x$($icon.Height)."
    }
}
finally {
    $icon.Dispose()
}

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null
$stagingParent = Join-Path $ProjectRoot "obj\thunderstore-package"
New-Item -ItemType Directory -Force -Path $stagingParent | Out-Null
$stagingRoot = Join-Path $stagingParent ([Guid]::NewGuid().ToString("N"))
$pluginDir = Join-Path $stagingRoot "plugins\Terramizer"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

try {
    foreach ($name in $requiredTemplateFiles) {
        Copy-Item -LiteralPath (Join-Path $TemplateDir $name) -Destination (Join-Path $stagingRoot $name)
    }
    Copy-Item -LiteralPath $DllPath -Destination (Join-Path $pluginDir "Terramizer.dll")

    if (Test-Path -LiteralPath $ArchivePath) {
        Remove-Item -LiteralPath $ArchivePath -Force
    }
    if (Test-Path -LiteralPath $ChecksumPath) {
        Remove-Item -LiteralPath $ChecksumPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $createdArchive = [IO.Compression.ZipFile]::Open($ArchivePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $rootPrefix = [IO.Path]::GetFullPath($stagingRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $stagingRoot -File -Recurse) {
            $relativePath = $file.FullName.Substring($rootPrefix.Length).Replace("\", "/")
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $createdArchive,
                $file.FullName,
                $relativePath,
                [IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    }
    finally {
        $createdArchive.Dispose()
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName.Replace("\", "/") })
        $requiredEntries = @(
            "manifest.json",
            "README.md",
            "CHANGELOG.md",
            "icon.png",
            "plugins/Terramizer/Terramizer.dll"
        )
        foreach ($entry in $requiredEntries) {
            if ($entryNames -notcontains $entry) {
                throw "Generated archive is missing required entry: $entry"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ArchivePath).Hash
    [IO.File]::WriteAllText($ChecksumPath, "$hash  $([IO.Path]::GetFileName($ArchivePath))`r`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Owner/team: $Owner"
    Write-Host "Config GUID: r4v9n1.terramizer"
    Write-Host "Thunderstore package: $ArchivePath"
    Write-Host "SHA256: $hash"
}
finally {
    $resolvedParent = [IO.Path]::GetFullPath($stagingParent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedStage = [IO.Path]::GetFullPath($stagingRoot)
    if ($resolvedStage.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
