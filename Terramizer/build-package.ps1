param(
    [string]$ValheimDir = $env:VALHEIM_DIR,
    [string]$GameManagedDir = $env:VALHEIM_MANAGED_DIR,
    [string]$BepInExCoreDir = $env:BEPINEX_CORE_DIR,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$LocalProjectRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\Terramizer"
$DistDir = Join-Path $LocalProjectRoot "dist"
$PackageStageRoot = Join-Path $LocalProjectRoot "package-stage"
$ReleaseDir = "G:\My Drive\build\Valheim\releases\Terramizer"
$Owner = "R4V9N1"
$PackageVersion = "1.0.3"
$PackageName = "Terramizer"
$TemplateDir = Join-Path $ProjectRoot "thunderstore"
$DllPath = Join-Path $DistDir "Terramizer.dll"
$ArchivePath = Join-Path $ReleaseDir "$Owner-$PackageName-$PackageVersion.zip"
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
if ($manifest.description -notmatch "I use AI" -or $manifest.description -notmatch "manually test" -or $manifest.description -notmatch "remain my own") {
    throw "Manifest description must include the AI-assistance development disclosure."
}
if ($manifest.dependencies -notcontains "denikson-BepInExPack_Valheim-5.4.2350") {
    throw "Manifest is missing the pinned BepInExPack_Valheim dependency."
}

$sourceText = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\TerramizerPlugin.cs") -Raw
$patchText = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\PerformancePatches.cs") -Raw
$dictionaryText = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\BinarySearchDictionarySetValuePatch.cs") -Raw
$terrainText = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\TerramizerTerrainCompatibility.cs") -Raw
if ($sourceText -notmatch 'public const string PluginGuid\s*=\s*"r4v9n1\.terramizer"') {
    throw "BepInEx PluginGuid must be r4v9n1.terramizer."
}
if ($sourceText -notmatch [regex]::Escape("Created by $Owner")) {
    throw "Creator metadata does not identify $Owner."
}
if ($sourceText -notmatch 'public const string PluginVersion\s*=\s*"1\.0\.3"') {
    throw "Terramizer source version must be 1.0.3."
}
$requiredPatchRegistrations = @(
    "PatchFeature(typeof(PiecePlacementEffectScopePatch)",
    "PatchFeature(typeof(PlayerPlacePieceEffectScopePatch)",
    "PatchFeature(typeof(PlacementEffectCleanupPatch)"
)
foreach ($hook in $requiredPatchRegistrations) {
    if ($sourceText -notmatch [regex]::Escape($hook)) {
        throw "Terramizer startup is missing required patch registration: $hook."
    }
}
$requiredCompanionProtocol = @("CompanionMetadataV2", "RequestCompanionMetadataV2", "ServerTerrainLimitsEnabledKey", "ServerTerrainRaiseLimitKey", "ServerTerrainDigLimitKey", "Register<string, bool, bool>(RpcCompanionMetadata")
foreach ($protocolItem in $requiredCompanionProtocol) {
    if ($sourceText -notmatch [regex]::Escape($protocolItem)) {
        throw "Terramizer companion protocol is missing compatibility item: $protocolItem."
    }
}
if ($sourceText -notmatch '(?s)if\s*\(\s*_enabled\.Value\s*\)\s*\{.*PatchFeature\(typeof\(PlayerPlacePieceEffectScopePatch\)') {
    throw "Terramizer placement patches must remain disabled when the Enabled setting is false."
}
if ($patchText -notmatch [regex]::Escape('[HarmonyPatch(typeof(Player), "PlacePiece", new[] { typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(bool) })]')) {
    throw "Terramizer placement patch must target the verified Player.PlacePiece signature."
}
foreach ($placementSafetyItem in @("RemovePlacementVisuals", "ParticleSystemStopBehavior.StopEmittingAndClear", "GetComponentsInChildren<Smoke>", "SuppressSmokeObject")) {
    if ($patchText -notmatch [regex]::Escape($placementSafetyItem)) {
        throw "Terramizer placement effect safeguard is missing: $placementSafetyItem."
    }
}
if ($patchText -notmatch [regex]::Escape('StartsWith("vfx_Place_"')) {
    throw "Terramizer placement cleanup must suppress particle-only vfx_Place_* effects."
}
if ($sourceText -notmatch '(?s)ShouldRunWearNTearUpdate\(\).*?return\s+true\s*;') {
    throw "Terramizer must keep WearNTear on the vanilla visible-update cadence."
}
foreach ($handshakeItem in @("_serverCompanionHandshakeValid", "Dedicated remote worlds must prove", "_serverCompanionHandshakeValid && _serverExtendedTerrain", "IsCurrentServerSender(sender)")) {
    if ($sourceText -notmatch [regex]::Escape($handshakeItem) -and $terrainText -notmatch [regex]::Escape($handshakeItem)) {
        throw "Terramizer dedicated-server terrain handshake guard is missing: $handshakeItem."
    }
}
if ($sourceText -notmatch [regex]::Escape("_autoDetectTerramizerServer.Value")) {
    throw "Terramizer must clear remote terrain authority when automatic server validation is disabled."
}
foreach ($networkGuard in @("_serverCompanionLegacyRequestSent", "_serverCompanionV2RequestAttempts", "_serverCompanionV2RequestAttempts >= 3")) {
    if ($sourceText -notmatch [regex]::Escape($networkGuard)) {
        throw "Terramizer companion discovery traffic guard is missing: $networkGuard."
    }
}
if ($sourceText -notmatch [regex]::Escape("BinarySearchDictionarySetValuePatch.Install")) {
    throw "Terramizer is missing the allocation-free BinarySearchDictionary.SetValue optimization."
}
if ($dictionaryText -notmatch [regex]::Escape("SetValuePrefix<TValue>(BinarySearchDictionary<int, TValue> __instance")) {
    throw "Terramizer's BinarySearchDictionary prefix must expose the Harmony __instance parameter."
}
if ($sourceText -notmatch [regex]::Escape("VisEquipmentIntCachePatch")) {
    throw "Terramizer is missing the VisEquipment ZDO integer lookup optimization."
}
if ($sourceText -notmatch [regex]::Escape("ZPackageWritePackagePatch")) {
    throw "Terramizer is missing the allocation-free nested ZPackage optimization."
}
if ($sourceText -notmatch [regex]::Escape("ReuseCollisionCallbacks")) {
    throw "Terramizer is missing the physics collision-callback reuse setting."
}
foreach ($retiredPacingHook in @(
    'typeof(SmokeRenderer)',
    'typeof(ClutterSystem)',
    'typeof(WearNTear)'
)) {
    if ($sourceText -match [regex]::Escape($retiredPacingHook) -or $patchText -match [regex]::Escape($retiredPacingHook)) {
        throw "Terramizer contains a retired visual/update pacing hook: $retiredPacingHook."
    }
}
foreach ($retiredPacingHook in @('PaceSmokeUpdates', 'PaceWearNTearUpdates', 'GrassResetCoalesceSeconds', 'DeferGrassRebuildAfterTerrain')) {
    if ($patchText -match [regex]::Escape($retiredPacingHook)) {
        throw "Terramizer contains a retired visual/update pacing hook: $retiredPacingHook."
    }
}
$requiredTerrainHooks = @(
    '[HarmonyPatch(typeof(Heightmap), "AtMaxWorldLevelDepth")]',
    '[HarmonyPatch(typeof(Heightmap), "LevelTerrain")]',
    '[HarmonyPatch(typeof(TerrainComp), "LevelTerrain")]',
    '[HarmonyPatch(typeof(TerrainComp), "RaiseTerrain")]',
    '[HarmonyPatch(typeof(TerrainComp), "ApplyToHeightmap")]'
)
foreach ($hook in $requiredTerrainHooks) {
    if ($terrainText -notmatch [regex]::Escape($hook)) {
        throw "Terramizer local terrain compatibility is missing required hook: $hook."
    }
}
if ($terrainText -match [regex]::Escape("typeof(TerrainModifier)")) {
    throw "Terramizer local terrain compatibility must not patch TerrainModifier placement behavior."
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
foreach ($reference in @("0Harmony", "assembly_valheim", "assembly_utils")) {
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

New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
$stagingParent = $PackageStageRoot
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
        if ($entryNames.Count -ne $requiredEntries.Count -or @($entryNames | Sort-Object -Unique).Count -ne $entryNames.Count) {
            throw "Generated archive must contain exactly the five required entries and no duplicates."
        }
        $dllEntry = $archive.GetEntry("plugins/Terramizer/Terramizer.dll")
        $dllHash = [Security.Cryptography.SHA256]::Create()
        try {
            $archiveDllHash = ([BitConverter]::ToString($dllHash.ComputeHash($dllEntry.Open()))).Replace('-', '')
        }
        finally {
            $dllHash.Dispose()
        }
        $distHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath).Hash
        if ($archiveDllHash -ne $distHash) {
            throw "Generated archive DLL does not match dist DLL."
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
        try {
            [IO.Directory]::Delete($resolvedStage, $true)
        }
        catch {
            Start-Sleep -Milliseconds 100
            try { [IO.Directory]::Delete($resolvedStage, $true) }
            catch { Write-Warning "Could not remove temporary package staging directory; package output remains valid: $resolvedStage" }
        }
    }
}
