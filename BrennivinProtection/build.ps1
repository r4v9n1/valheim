param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ManagedDir = Join-Path $ValheimDir "valheim_Data\Managed"
$BepInExCoreDir = Join-Path $ValheimDir "BepInEx\core"
$JotunnCandidates = @(
    (Join-Path $ValheimDir "BepInEx\plugins\Jotunn\Jotunn.dll"),
    (Join-Path $ValheimDir "BepInEx\plugins\Jotunn.dll")
)
$JotunnPath = $JotunnCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$DistDir = Join-Path $ProjectRoot "dist"
$DllPath = Join-Path $DistDir "BrennivinProtection.dll"

if ([string]::IsNullOrWhiteSpace($JotunnPath)) {
    throw "Jotunn.dll was not found. Checked:`n$($JotunnCandidates -join "`n")"
}

foreach ($path in @(
    (Join-Path $ManagedDir "assembly_valheim.dll"),
    (Join-Path $ManagedDir "UnityEngine.dll"),
    (Join-Path $ManagedDir "UnityEngine.CoreModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.AnimationModule.dll"),
    (Join-Path $ManagedDir "UnityEngine.PhysicsModule.dll"),
    (Join-Path $BepInExCoreDir "BepInEx.dll"),
    (Join-Path $BepInExCoreDir "0Harmony.dll"),
    $JotunnPath
)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing build dependency: $path"
    }
}

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
if (Test-Path -LiteralPath $DllPath) { Remove-Item -LiteralPath $DllPath -Force }

dotnet build (Join-Path $ProjectRoot "BrennivinProtection.csproj") `
    -c $Configuration `
    --no-incremental `
    -p:ValheimDir="$ValheimDir" `
    -p:GameManagedDir="$ManagedDir" `
    -p:BepInExCoreDir="$BepInExCoreDir" `
    -p:JotunnPath="$JotunnPath" `
    -p:OutputPath="$DistDir\"

if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
if (!(Test-Path -LiteralPath $DllPath -PathType Leaf)) { throw "Fresh DLL was not produced: $DllPath" }

$dllVersion = ([Reflection.AssemblyName]::GetAssemblyName($DllPath)).Version.ToString()
if ($dllVersion -ne "0.1.3.0") { throw "Fresh DLL version is $dllVersion, expected 0.1.3.0." }

Write-Host "Built fresh BrennivinProtection.dll" -ForegroundColor Green
Write-Host "DLL version: $dllVersion"
Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath | ForEach-Object { Write-Host "DLL SHA256: $($_.Hash)" }
