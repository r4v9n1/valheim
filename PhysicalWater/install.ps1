param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$Configuration = "Release",
    [switch]$EnableStageE1,
    [switch]$EnableStageE3
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $ProjectRoot "build.ps1") -ValheimDir $ValheimDir -Configuration $Configuration

$DllPath = Join-Path $ProjectRoot "dist\PhysicalWater.dll"
$BundlePath = Join-Path $ProjectRoot "dist\physicalwater_assets"
$PluginDir = Join-Path $ValheimDir "BepInEx\plugins\PhysicalWater"
$PluginDllPath = Join-Path $PluginDir "PhysicalWater.dll"
$PluginBundlePath = Join-Path $PluginDir "physicalwater_assets"
$EnableFiniteStreaming = $EnableStageE1.IsPresent -or $EnableStageE3.IsPresent

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

Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "General" -Key "Enabled" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "General" -Key "DryOceanFloorBaseline" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "General" -Key "RenderPreviewSurface" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "OverrideWaterQueries" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "PhysicalWaterInteractionEnabled" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "FeedFloatingLiquidLevel" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "FeedCharactersLiquidLevel" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "HideVanillaWaterRenderers" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "SuppressVanillaWaterVolumeFloaters" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Integration" -Key "ReactToFloatingObjects" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipBuoyancyAssist" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ShipPhysics" -Key "ShipUprightAssist" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "CreatureWater" -Key "SmallCreatureSwimAssist" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "Camera" -Key "DisableUnderwaterCameraClamp" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "WaterShape" -Key "FarOceanEnabled" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DiagnosticsEnabled" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "ScanRadius" -Value "64"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "ScanInterval" -Value "0.25"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "FullConsistencyInterval" -Value "900"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DiscoveryChunksPerScan" -Value "1"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "EstimatedVoxelCellSize" -Value "0.75"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DirtyPaddingCells" -Value "4"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "LogRejectedSamples" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DebugVisualization" -Value "false"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "ChunkSize" -Value "32"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "DiscoveryTileSize" -Value "8"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "OriginSnapMeters" -Value "16"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "TerrainSampleSpacing" -Value "3"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "TerrainMaxSamplesPerScan" -Value "2048"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "FrameBudgetMilliseconds" -Value "16"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "ValheimGeometryAdapter" -Key "IncrementalBudgetMilliseconds" -Value "4"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "StageE1" -Key "Enabled" -Value ($EnableFiniteStreaming.ToString().ToLowerInvariant())
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "StageE1" -Key "RenderSurface" -Value "true"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "StageE1" -Key "TelemetryInterval" -Value "2"
Set-PhysicalWaterConfigValue -Path $ConfigPath -Section "StageE1" -Key "MaxSubstepsPerFrame" -Value "2"

Write-Host "Installed PhysicalWater locally:" -ForegroundColor Green
Write-Host $PluginDllPath
if (Test-Path -LiteralPath $PluginBundlePath -PathType Leaf) {
    Write-Host "Installed PhysicalWater Unity asset bundle:" -ForegroundColor Green
    Write-Host $PluginBundlePath
}
Write-Host ""
Write-Host "devE3 safety config written (legacy replacement OFF, finite streaming=$EnableFiniteStreaming):" -ForegroundColor Yellow
Write-Host $ConfigPath
Write-Host ""
Write-Host "PhysicalWater 0.6.0-devE3.2-probe1: diagnostic-only live field probe on the devE3.2 pressure candidate; global replacement, vanilla suppression, player, ship, fish, swimming and buoyancy hooks remain disabled."
