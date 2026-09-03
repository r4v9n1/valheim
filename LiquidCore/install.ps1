param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$Configuration = "Release",
    [switch]$EnableStageE1,
    [switch]$EnableStageE3
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$LocalProjectRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\LiquidCore"
$LocalDistDir = Join-Path $LocalProjectRoot "dist"
& (Join-Path $ProjectRoot "build.ps1") -ValheimDir $ValheimDir -Configuration $Configuration
& (Join-Path $ProjectRoot "build-assets.ps1")

$DllPath = Join-Path $LocalDistDir "LiquidCore.dll"
$BundleName = "physicalwater_assets"
$PluginRoot = Join-Path $ValheimDir "BepInEx\plugins"
$PluginDir = Join-Path $PluginRoot "LiquidCore"
$PluginDllPath = Join-Path $PluginDir "LiquidCore.dll"
$PluginBundlePath = Join-Path $PluginDir "physicalwater_assets"
$LegacyPluginDir = Join-Path $PluginRoot "PhysicalWater"
$EnableFiniteStreaming = $EnableStageE1.IsPresent -or $EnableStageE3.IsPresent
$EnableGeometryDiagnostics = (-not $EnableFiniteStreaming).ToString().ToLowerInvariant()

New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
Copy-Item -LiteralPath $DllPath -Destination $PluginDllPath -Force

$BundlePath = Join-Path $LocalDistDir $BundleName
if (!(Test-Path -LiteralPath $BundlePath -PathType Leaf)) {
    throw "Fresh validated AssetBundle is missing: $BundlePath"
}
Copy-Item -LiteralPath $BundlePath -Destination $PluginBundlePath -Force
if ((Get-FileHash -LiteralPath $BundlePath -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $PluginBundlePath -Algorithm SHA256).Hash) {
    throw "Installed AssetBundle hash does not match the validated build output."
}

# The BepInEx GUID and config remain legacy-compatible, but the old assembly must
# never coexist with LiquidCore.dll because BepInEx scans plugin subdirectories.
$obsoleteDlls = @(Get-ChildItem -LiteralPath $PluginRoot -Filter "PhysicalWater.dll" -File -Recurse -ErrorAction SilentlyContinue)
foreach ($obsoleteDll in $obsoleteDlls) {
    Remove-Item -LiteralPath $obsoleteDll.FullName -Force
    Write-Host "Removed obsolete plugin assembly: $($obsoleteDll.FullName)" -ForegroundColor Yellow
}
$duplicateLiquidCoreDlls = @(Get-ChildItem -LiteralPath $PluginRoot -Filter "LiquidCore.dll" -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -cne $PluginDllPath })
foreach ($duplicateDll in $duplicateLiquidCoreDlls) {
    Remove-Item -LiteralPath $duplicateDll.FullName -Force
    Write-Host "Removed duplicate LiquidCore assembly: $($duplicateDll.FullName)" -ForegroundColor Yellow
}

$ConfigPath = Join-Path $ValheimDir "BepInEx\config\r4v9n1.physicalwater.cfg"

function Set-LiquidCoreConfigValue {
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

Set-LiquidCoreConfigValue -Path $ConfigPath -Section "General" -Key "Enabled" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "General" -Key "DryOceanFloorBaseline" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "General" -Key "RenderPreviewSurface" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "Integration" -Key "OverrideWaterQueries" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "Integration" -Key "PhysicalWaterInteractionEnabled" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "Integration" -Key "FeedFloatingLiquidLevel" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "Integration" -Key "FeedCharactersLiquidLevel" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "Integration" -Key "HideVanillaWaterRenderers" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "Integration" -Key "SuppressVanillaWaterVolumeFloaters" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "Integration" -Key "ReactToFloatingObjects" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipBuoyancyAssist" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipUprightAssist" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "CreatureWater" -Key "SmallCreatureSwimAssist" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "Camera" -Key "DisableUnderwaterCameraClamp" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "WaterShape" -Key "FarOceanEnabled" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DiagnosticsEnabled" -Value $EnableGeometryDiagnostics
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "ScanRadius" -Value "64"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "ScanInterval" -Value "0.25"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "FullConsistencyInterval" -Value "900"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DiscoveryChunksPerScan" -Value "1"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "EstimatedVoxelCellSize" -Value "0.75"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DirtyPaddingCells" -Value "4"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "LogRejectedSamples" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DebugVisualization" -Value "false"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "ChunkSize" -Value "32"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DiscoveryTileSize" -Value "8"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "OriginSnapMeters" -Value "16"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "TerrainSampleSpacing" -Value "3"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "TerrainMaxSamplesPerScan" -Value "2048"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "FrameBudgetMilliseconds" -Value "16"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "IncrementalBudgetMilliseconds" -Value "4"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "StageE1" -Key "Enabled" -Value ($EnableFiniteStreaming.ToString().ToLowerInvariant())
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "StageE1" -Key "RenderSurface" -Value "true"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "StageE1" -Key "TelemetryInterval" -Value "2"
Set-LiquidCoreConfigValue -Path $ConfigPath -Section "StageE1" -Key "MaxSubstepsPerFrame" -Value "2"

Write-Host "Installed LiquidCore locally:" -ForegroundColor Green
Write-Host $PluginDllPath
if (Test-Path -LiteralPath $PluginBundlePath -PathType Leaf) {
    Write-Host "Installed LiquidCore Unity asset bundle (legacy asset identity preserved):" -ForegroundColor Green
    Write-Host $PluginBundlePath
}
Write-Host ""
Write-Host "devE3 safety config written (legacy replacement OFF, finite streaming=$EnableFiniteStreaming):" -ForegroundColor Yellow
Write-Host $ConfigPath
Write-Host ""
Write-Host "LiquidCore 0.6.0-devE3.2-probe3: causal-preparation polling and allocation checkpoint; legacy BepInEx/config identity preserved; global replacement, vanilla suppression, player, ship, fish, swimming and buoyancy hooks remain disabled."
