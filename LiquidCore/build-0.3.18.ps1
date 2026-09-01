param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ManagedDir = Join-Path $ValheimDir "valheim_Data\Managed"
$BepInExCoreDir = Join-Path $ValheimDir "BepInEx\core"
$DistDir = Join-Path $ProjectRoot "dist"
$DllPath = Join-Path $DistDir "PhysicalWater.dll"
$PatchedPatchesSource = Join-Path $ProjectRoot "PhysicalWaterPatches-0.3.18.cs.fixed"
$ProjectPatchesPath = Join-Path $ProjectRoot "src\PhysicalWaterPatches.cs"
$PatchedSystemSource = Join-Path $ProjectRoot "PhysicalWaterSystem-0.3.18.cs.fixed"
$ProjectSystemPath = Join-Path $ProjectRoot "src\PhysicalWaterSystem.cs"
$PatchedPluginSource = Join-Path $ProjectRoot "PhysicalWaterPlugin-0.3.18.cs.fixed"
$ProjectPluginPath = Join-Path $ProjectRoot "src\PhysicalWaterPlugin.cs"
$PatchedProjectFile = Join-Path $ProjectRoot "PhysicalWater-0.3.18.csproj.fixed"
$ProjectFile = Join-Path $ProjectRoot "PhysicalWater.csproj"

if (Test-Path -LiteralPath $PatchedPatchesSource -PathType Leaf) {
    Copy-Item -LiteralPath $PatchedPatchesSource -Destination $ProjectPatchesPath -Force
    Write-Host "Applied PhysicalWaterPatches 0.3.18 authoritative no-vanilla water/ship solver." -ForegroundColor Cyan
}

if (Test-Path -LiteralPath $PatchedSystemSource -PathType Leaf) {
    Copy-Item -LiteralPath $PatchedSystemSource -Destination $ProjectSystemPath -Force
    Write-Host "Applied PhysicalWaterSystem 0.3.18 shared GPU/CPU wave + live ripple render bridge." -ForegroundColor Cyan
}

if (Test-Path -LiteralPath $PatchedPluginSource -PathType Leaf) {
    Copy-Item -LiteralPath $PatchedPluginSource -Destination $ProjectPluginPath -Force
    Write-Host "Applied PhysicalWaterPlugin 0.3.18 version bump." -ForegroundColor Cyan
}


if (Test-Path -LiteralPath $PatchedProjectFile -PathType Leaf) {
    Copy-Item -LiteralPath $PatchedProjectFile -Destination $ProjectFile -Force
    Write-Host "Applied PhysicalWater 0.3.18 net472 project reference fix (AudioModule reflection bridge)." -ForegroundColor Cyan
}

foreach ($path in @(
    (Join-Path $ManagedDir "assembly_valheim.dll"),
    (Join-Path $ManagedDir "UnityEngine.dll"),
    (Join-Path $ManagedDir "UnityEngine.CoreModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.PhysicsModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.AssetBundleModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.ParticleSystemModule.dll"),
    (Join-Path $BepInExCoreDir "BepInEx.dll"),
    (Join-Path $BepInExCoreDir "0Harmony.dll")
)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing build dependency: $path"
    }
}

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
if (Test-Path -LiteralPath $DllPath -PathType Leaf) {
    Remove-Item -LiteralPath $DllPath -Force
}

dotnet build (Join-Path $ProjectRoot "PhysicalWater.csproj") `
    -c $Configuration `
    --no-incremental `
    -p:ValheimDir="$ValheimDir" `
    -p:GameManagedDir="$ManagedDir" `
    -p:BepInExCoreDir="$BepInExCoreDir" `
    -p:OutputPath="$DistDir\"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (!(Test-Path -LiteralPath $DllPath -PathType Leaf)) {
    throw "Fresh DLL was not produced: $DllPath"
}

$dllVersion = ([Reflection.AssemblyName]::GetAssemblyName($DllPath)).Version.ToString()
if ($dllVersion -ne "0.3.18.0") {
    throw "Fresh DLL version is $dllVersion, expected 0.3.18.0."
}

Write-Host "Built fresh PhysicalWater.dll" -ForegroundColor Green
Write-Host "DLL version: $dllVersion"
Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath | ForEach-Object {
    Write-Host "DLL SHA256: $($_.Hash)"
}
