param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$LocalProjectRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\LiquidCore"
$ObjDir = Join-Path $LocalProjectRoot "obj"
$DistDir = Join-Path $LocalProjectRoot "dist"
$ManagedDir = Join-Path $ValheimDir "valheim_Data\Managed"
$BepInExCoreDir = Join-Path $ValheimDir "BepInEx\core"
$DllPath = Join-Path $DistDir "LiquidCore.dll"

foreach ($path in @(
    (Join-Path $ManagedDir "assembly_valheim.dll"),
    (Join-Path $ManagedDir "UnityEngine.dll"),
    (Join-Path $ManagedDir "UnityEngine.CoreModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.PhysicsModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.AssetBundleModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.ParticleSystemModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.AudioModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.AnimationModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.InputLegacyModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.JSONSerializeModule.dll"),
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

dotnet build (Join-Path $ProjectRoot "LiquidCore.csproj") `
    -c $Configuration `
    --no-incremental `
    -p:ValheimDir="$ValheimDir" `
    -p:GameManagedDir="$ManagedDir" `
    -p:BepInExCoreDir="$BepInExCoreDir" `
    -p:BaseIntermediateOutputPath="$ObjDir\" `
    -p:GenerateTargetFrameworkAttribute=false `
    -p:OutputPath="$DistDir\"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (!(Test-Path -LiteralPath $DllPath -PathType Leaf)) {
    throw "Fresh DLL was not produced: $DllPath"
}

$dllVersion = ([Reflection.AssemblyName]::GetAssemblyName($DllPath)).Version.ToString()
if ($dllVersion -ne "0.6.0.19") {
    throw "Fresh DLL version is $dllVersion, expected 0.6.0.19."
}

Write-Host "Built fresh LiquidCore.dll" -ForegroundColor Green
Write-Host "DLL version: $dllVersion"
Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath | ForEach-Object {
    Write-Host "DLL SHA256: $($_.Hash)"
}
