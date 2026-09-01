param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

# Derive the shared workspace from:
#   <workspace>\Valheim\source\PhysicalWater
$SourceRoot = Split-Path -Parent $ProjectRoot
$ValheimSourceRoot = Split-Path -Parent $SourceRoot
$WorkspaceRoot = Split-Path -Parent $ValheimSourceRoot

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.61f1\Editor\Unity.exe"
$UnityProject = Join-Path $WorkspaceRoot "UnityPhysicalOceanValidation"
$UnityPackage = Join-Path $WorkspaceRoot "UnityPhysicalOcean"
$BundlePath = Join-Path $ProjectRoot "dist\physicalwater_assets"

# Promote the versioned changelog snapshot into the canonical project changelog.
$VersionedChangelogPath = Join-Path $ProjectRoot "CHANGELOG-0.4.2.md"
$CanonicalChangelogPath = Join-Path $ProjectRoot "CHANGELOG.md"
if (!(Test-Path -LiteralPath $VersionedChangelogPath -PathType Leaf)) {
    throw "Missing mandatory 0.4.2 changelog: $VersionedChangelogPath"
}
Copy-Item -LiteralPath $VersionedChangelogPath -Destination $CanonicalChangelogPath -Force
Write-Host "Applied CHANGELOG 0.4.2 to canonical project changelog." -ForegroundColor DarkGray

# Unity's AssetDatabase uses LMDB files under Library. Cloud-backed virtual drives
# can crash Unity while LMDB initializes/writes, so keep all editable source on G:
# but perform Unity's disposable import/build work on the local NTFS drive.
$LocalBuildRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\PhysicalWaterUnityBuild"
$LocalUnityProject = Join-Path $LocalBuildRoot "UnityPhysicalOceanValidation"
$LocalUnityPackage = Join-Path $LocalBuildRoot "UnityPhysicalOcean"
$LocalOutputDir = Join-Path $LocalBuildRoot "Valheim\source\PhysicalWater\dist"
$LocalBundlePath = Join-Path $LocalOutputDir "physicalwater_assets"
$LocalUnityLog = Join-Path $LocalUnityProject "BuildPhysicalWaterBundle.log"
$DriveUnityLog = Join-Path $UnityProject "BuildPhysicalWaterBundle.log"

if (!(Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Missing Unity 6000.0.61f1 editor: $UnityExe"
}
if (!(Test-Path -LiteralPath $UnityProject -PathType Container)) {
    throw "Missing UnityPhysicalOceanValidation project: $UnityProject"
}
if (!(Test-Path -LiteralPath $UnityPackage -PathType Container)) {
    throw "Missing UnityPhysicalOcean package: $UnityPackage"
}

# Apply connector-owned shader correction to the canonical G: source before
# refreshing the local Unity build workspace. The original Drive shader cannot
# be overwritten directly by the connector, but this installer runs locally and can.
$FixedShaderPath = Join-Path $UnityPackage "Runtime\Shaders\PhysicalOceanSurface-0.4.2.shader.fixed"
$CanonicalShaderPath = Join-Path $UnityPackage "Runtime\Shaders\PhysicalOceanSurface.shader"
if (!(Test-Path -LiteralPath $FixedShaderPath -PathType Leaf)) {
    throw "Missing mandatory 0.4.2 shader: $FixedShaderPath"
}
Copy-Item -LiteralPath $FixedShaderPath -Destination $CanonicalShaderPath -Force
Write-Host "Applied PhysicalOceanSurface 0.4.2 concentric LOD spectral ocean shader to canonical G: source." -ForegroundColor DarkGray

# Force a true AssetBundle rebuild when Library is preserved locally. Without this,
# Unity can reuse cached bundle state after the previous output file was deleted and
# emit only the output-directory manifest bundle (dist), leaving physicalwater_assets absent.
$FixedBundleBuilderPath = Join-Path $ProjectRoot "BuildPhysicalWaterBundle-0.4.2.cs.fixed"
$CanonicalBundleBuilderPath = Join-Path $UnityProject "Assets\Editor\BuildPhysicalWaterBundle.cs"
if (!(Test-Path -LiteralPath $FixedBundleBuilderPath -PathType Leaf)) {
    throw "Missing mandatory 0.4.2 AssetBundle builder: $FixedBundleBuilderPath"
}
Copy-Item -LiteralPath $FixedBundleBuilderPath -Destination $CanonicalBundleBuilderPath -Force
Write-Host "Applied force-rebuild AssetBundle builder to canonical G: validation project." -ForegroundColor DarkGray

Write-Host "Workspace root: $WorkspaceRoot" -ForegroundColor DarkGray
Write-Host "Unity validation source: $UnityProject" -ForegroundColor DarkGray
Write-Host "Unity package source: $UnityPackage" -ForegroundColor DarkGray
Write-Host "Unity local build cache: $LocalBuildRoot" -ForegroundColor DarkGray
Write-Host "Preparing local Unity build workspace..." -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $LocalBuildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $LocalUnityProject | Out-Null
New-Item -ItemType Directory -Force -Path $LocalOutputDir | Out-Null

# Refresh source-controlled/build-input folders from the canonical G: workspace.
# Preserve LocalUnityProject\Library so Unity's AssetDatabase remains local and reusable.
foreach ($folder in @("Assets", "Packages", "ProjectSettings")) {
    $src = Join-Path $UnityProject $folder
    $dst = Join-Path $LocalUnityProject $folder
    if (!(Test-Path -LiteralPath $src -PathType Container)) {
        throw "Missing Unity validation folder: $src"
    }
    if (Test-Path -LiteralPath $dst) {
        Remove-Item -LiteralPath $dst -Recurse -Force
    }
    Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
}

if (Test-Path -LiteralPath $LocalUnityPackage) {
    Remove-Item -LiteralPath $LocalUnityPackage -Recurse -Force
}
Copy-Item -LiteralPath $UnityPackage -Destination $LocalUnityPackage -Recurse -Force

# Point the LOCAL validation project at the LOCAL package copy. Write strict UTF-8
# without BOM because Unity 6 rejects a BOM at the start of Packages\manifest.json.
$LocalManifestPath = Join-Path $LocalUnityProject "Packages\manifest.json"
$manifestText = [System.IO.File]::ReadAllText($LocalManifestPath).TrimStart([char]0xFEFF)
$manifest = $manifestText | ConvertFrom-Json
$packageUri = "file:" + ($LocalUnityPackage -replace "\\", "/")
$manifest.dependencies.'com.r4v9n1.physical-ocean' = $packageUri
$manifestJson = $manifest | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($LocalManifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))

# Clean only current-run evidence/output. Library intentionally remains local.
foreach ($file in @($LocalUnityLog, $DriveUnityLog, $LocalBundlePath, $BundlePath)) {
    if (Test-Path -LiteralPath $file -PathType Leaf) {
        Remove-Item -LiteralPath $file -Force
    }
}
$buildStartedUtc = [DateTime]::UtcNow

Write-Host "Rebuilding PhysicalWater Unity asset bundle on local NTFS cache..." -ForegroundColor Cyan

$unityStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$unityStartInfo.FileName = $UnityExe
$unityStartInfo.Arguments = "-batchmode -quit -projectPath `"$LocalUnityProject`" -executeMethod BuildPhysicalWaterBundle.Build -logFile `"$LocalUnityLog`""
$unityStartInfo.UseShellExecute = $false
$unityStartInfo.CreateNoWindow = $true

$unityProcess = New-Object System.Diagnostics.Process
$unityProcess.StartInfo = $unityStartInfo
if (!$unityProcess.Start()) {
    throw "Failed to launch Unity: $UnityExe"
}
$unityProcess.WaitForExit()
$unityExitCode = $unityProcess.ExitCode

# Mirror the current local Unity log back to the Drive project for easy diagnosis.
if (Test-Path -LiteralPath $LocalUnityLog -PathType Leaf) {
    Copy-Item -LiteralPath $LocalUnityLog -Destination $DriveUnityLog -Force
}

$localBundleFresh = Test-Path -LiteralPath $LocalBundlePath -PathType Leaf
if ($localBundleFresh) {
    $localBundleFresh = (Get-Item -LiteralPath $LocalBundlePath).LastWriteTimeUtc -ge $buildStartedUtc.AddSeconds(-5)
}

if (!$localBundleFresh) {
    $logHint = if (Test-Path -LiteralPath $DriveUnityLog -PathType Leaf) { " See $DriveUnityLog" } else { " (Unity did not create a fresh log.)" }
    throw "Unity AssetBundle build did not produce a fresh local bundle at $LocalBundlePath. Process exit code: $unityExitCode.$logHint"
}

# Unity succeeded locally. Publish only the final bundle back to canonical G: dist.
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $BundlePath) | Out-Null
Copy-Item -LiteralPath $LocalBundlePath -Destination $BundlePath -Force
if (!(Test-Path -LiteralPath $BundlePath -PathType Leaf)) {
    throw "Fresh local AssetBundle was built but could not be copied back to $BundlePath"
}

if ($unityExitCode -ne 0) {
    Write-Warning "Unity reported exit code $unityExitCode, but a fresh local AssetBundle was produced and copied back successfully; continuing."
}

Write-Host "Fresh AssetBundle: $BundlePath" -ForegroundColor Green

& (Join-Path $ProjectRoot "build-0.4.2.ps1") -ValheimDir $ValheimDir -Configuration $Configuration

$DllPath = Join-Path $ProjectRoot "dist\PhysicalWater.dll"
$PluginDir = Join-Path $ValheimDir "BepInEx\plugins\PhysicalWater"
$PluginDllPath = Join-Path $PluginDir "PhysicalWater.dll"
$PluginBundlePath = Join-Path $PluginDir "physicalwater_assets"

New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
Copy-Item -LiteralPath $DllPath -Destination $PluginDllPath -Force
if (Test-Path -LiteralPath $BundlePath -PathType Leaf) {
    Copy-Item -LiteralPath $BundlePath -Destination $PluginBundlePath -Force
}

$ConfigPath = Join-Path $ValheimDir "BepInEx\config\r4v9n1.physicalwater.cfg"

function Set-PhysicalWaterConfigValue {
    param(
        [string]$Path,
        [string]$Section,
        [string]$Key,
        [string]$Value
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        $configDir = Split-Path -Parent $Path
        New-Item -ItemType Directory -Force -Path $configDir | Out-Null
        Set-Content -LiteralPath $Path -Value "" -Encoding UTF8
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in Get-Content -LiteralPath $Path) {
        $lines.Add($line)
    }

    $sectionPattern = "^\[$([regex]::Escape($Section))\]\s*$"
    $sectionIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $sectionPattern) {
            $sectionIndex = $i
            break
        }
    }

    if ($sectionIndex -lt 0) {
        if ($lines.Count -gt 0 -and ![string]::IsNullOrWhiteSpace($lines[$lines.Count - 1])) {
            $lines.Add("")
        }
        $lines.Add("[$Section]")
        $lines.Add("$Key = $Value")
        Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
        return
    }

    $nextSectionIndex = $lines.Count
    for ($i = $sectionIndex + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "^\[.+\]\s*$") {
            $nextSectionIndex = $i
            break
        }
    }

    $keyPattern = "^\s*$([regex]::Escape($Key))\s*="
    for ($i = $sectionIndex + 1; $i -lt $nextSectionIndex; $i++) {
        if ($lines[$i] -match $keyPattern) {
            $lines[$i] = "$Key = $Value"
            Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
            return
        }
    }

    $lines.Insert($nextSectionIndex, "$Key = $Value")
    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
}

Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "General" -Key "Enabled" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "General" -Key "DryOceanFloorBaseline" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "General" -Key "RenderPreviewSurface" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "OverrideWaterQueries" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "PhysicalWaterInteractionEnabled" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "FeedFloatingLiquidLevel" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "FeedCharactersLiquidLevel" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "HideVanillaWaterRenderers" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "SuppressVanillaWaterVolumeFloaters" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "ReactToFloatingObjects" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Camera" -Key "DisableUnderwaterCameraClamp" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Camera" -Key "UnderwaterCameraFreeDepth" -Value "0.65"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "GridResolution" -Value "161"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "FollowRadius" -Value "120"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "OriginSnapMeters" -Value "48"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "TerrainMaskEnabled" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "RequireOceanConnection" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "ShorelineDryMargin" -Value "0.04"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "ShorelineVisualBand" -Value "2.25"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "VisualBoundarySinkMeters" -Value "96"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "VisualBoundarySinkDepth" -Value "18"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "FarOceanEnabled" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "FarOceanRadius" -Value "2200"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "FarOceanInnerRadius" -Value "300"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "FarOceanResolution" -Value "65"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Visuals" -Key "UseVanillaWaterMaterial" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Visuals" -Key "DebugOpaqueWater" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Visuals" -Key "ShorelineEffectsEnabled" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Visuals" -Key "ShorelineEffectsInterval" -Value "0.32"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Visuals" -Key "ShorelineEffectsBudget" -Value "28"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Simulation" -Key "WaveSpeed" -Value "5.8"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Simulation" -Key "Damping" -Value "0.942"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Simulation" -Key "WindWaveAmplitude" -Value "0.72"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Simulation" -Key "WindWaveLength" -Value "38"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Simulation" -Key "MaxSimulatedDisplacement" -Value "0.16"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Simulation" -Key "ObjectDisturbanceScale" -Value "0.16"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Simulation" -Key "CharacterDisturbanceScale" -Value "0.18"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipBuoyancyAssist" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipBuoyancyAcceleration" -Value "48.0"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipSurfaceLift" -Value "0.15"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipMaxLiftDepth" -Value "4.0"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipVerticalDamping" -Value "18.0"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipWaterDrag" -Value "0.16"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipWakeStrength" -Value "0.028"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipUprightAssist" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipUprightTorque" -Value "8.0"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipAngularDamping" -Value "1.5"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipMaxAngularVelocity" -Value "1.8"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Physics" -Key "FloatingProbeFallbackRadius" -Value "8.0"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "CreatureWater" -Key "SmallCreatureSwimAssist" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "CreatureWater" -Key "SmallCreatureMaxHeight" -Value "1.8"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "CreatureWater" -Key "SmallCreatureSwimDepthFactor" -Value "0.45"

Write-Host "Installed PhysicalWater locally:" -ForegroundColor Green
Write-Host $PluginDllPath
if (Test-Path -LiteralPath $PluginBundlePath -PathType Leaf) {
    Write-Host "Installed PhysicalWater Unity asset bundle:" -ForegroundColor Green
    Write-Host $PluginBundlePath
}
Write-Host ""
Write-Host "Replacement test config written:" -ForegroundColor Yellow
Write-Host $ConfigPath
Write-Host ""
Write-Host "PhysicalWater 0.4.2 milestone: Visual Fidelity - preserved LOD ocean core, depth-aware absorption/refraction, Valheim fog and precipitation response, real scene lighting, GPU shoreline/ripple foam, and throttled CPU effects."
