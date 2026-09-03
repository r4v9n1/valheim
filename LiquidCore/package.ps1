param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$Configuration = "Release",
    [string]$Version = "0.6.0-devE3.2-probe2"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ValheimRoot = (Resolve-Path (Join-Path $ProjectRoot "..\..")).Path
$BuildRoot = (Resolve-Path (Join-Path $ValheimRoot "..\..")).Path
$LocalRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\LiquidCore"
$LocalDist = Join-Path $LocalRoot "dist"
$StageRoot = Join-Path $LocalRoot "package-stage"
$PackageName = "LiquidCore-$Version"
$PackageRoot = Join-Path $StageRoot $PackageName
$PluginRoot = Join-Path $PackageRoot "plugins\LiquidCore"
$ReleaseRoot = Join-Path $BuildRoot "Valheim\releases\LiquidCore\dev"
$ZipPath = Join-Path $ReleaseRoot "$PackageName.zip"

& (Join-Path $ProjectRoot "build.ps1") -ValheimDir $ValheimDir -Configuration $Configuration

if (Test-Path -LiteralPath $StageRoot) {
    Remove-Item -LiteralPath $StageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PluginRoot, $ReleaseRoot | Out-Null

$DllPath = Join-Path $LocalDist "LiquidCore.dll"
Copy-Item -LiteralPath $DllPath -Destination (Join-Path $PluginRoot "LiquidCore.dll") -Force

$bundleCandidates = @(
    (Join-Path $LocalDist "physicalwater_assets"),
    (Join-Path $ProjectRoot "dist\physicalwater_assets"),
    (Join-Path $ValheimDir "BepInEx\plugins\LiquidCore\physicalwater_assets")
)
$BundlePath = $bundleCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($BundlePath) {
    Copy-Item -LiteralPath $BundlePath -Destination (Join-Path $PluginRoot "physicalwater_assets") -Force
}

Copy-Item -LiteralPath (Join-Path $ProjectRoot "README.md") -Destination (Join-Path $PackageRoot "README.md") -Force
@(
    "Product: LiquidCore",
    "Version: $Version",
    "Assembly: LiquidCore.dll",
    "BepInEx GUID: r4v9n1.physicalwater (compatibility-preserved)",
    "Config: r4v9n1.physicalwater.cfg (compatibility-preserved)"
) | Set-Content -LiteralPath (Join-Path $PackageRoot "PACKAGE-MANIFEST.txt") -Encoding UTF8

if (Test-Path -LiteralPath $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}
Compress-Archive -LiteralPath $PackageRoot -DestinationPath $ZipPath -CompressionLevel Optimal

Write-Host "Packaged LiquidCore:" -ForegroundColor Green
Write-Host $ZipPath
Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath | ForEach-Object {
    Write-Host "Package SHA256: $($_.Hash)"
}
