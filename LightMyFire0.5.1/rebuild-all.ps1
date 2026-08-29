param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$AssetBundlePath = Join-Path $Root "assets\lightmyfire_assets"
$ExpectedAssetSha256 = "c59d850aad2b444cf1dac1b2e700060cfb37182cbf4532372d619a06743a9138"

$pluginSource = Get-Content -LiteralPath (Join-Path $Root "src\LightMyFirePlugin.cs") -Raw
if ($pluginSource -match 'Object\.Instantiate\(baseBarrel\)') { throw "Unsafe raw barrel clone detected. 0.5.1 requires Jotunn native cloning." }
if ($pluginSource -notmatch 'new CustomPiece\(prefabName, baseBarrel\.name, config\)') { throw "Jotunn native barrel clone path is missing." }
if ($pluginSource -notmatch 'PluginVersion = "0\.5\.3"') { throw "Plugin source version is not 0.5.3." }

if (!(Test-Path -LiteralPath $AssetBundlePath -PathType Leaf)) { throw "Verified production AssetBundle is missing." }
$assetHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $AssetBundlePath).Hash.ToLowerInvariant()
if ($assetHash -ne $ExpectedAssetSha256) {
    throw "Production AssetBundle does not match the known-good 0.5.0 DLL."
}

Write-Host "[1/3] Verified exact production AssetBundle from known-good 0.5.0 DLL." -ForegroundColor Cyan
Write-Host "      SHA256: $assetHash"

Write-Host "[2/3] Building fresh LightMyFire 0.5.3 DLL..." -ForegroundColor Cyan
& (Join-Path $Root "build.ps1") -ValheimDir $ValheimDir
if ($LASTEXITCODE -ne 0) { throw "DLL build failed." }

Write-Host "[3/3] Packaging LightMyFire_Coal_Resin 0.5.3..." -ForegroundColor Cyan
& (Join-Path $Root "package.ps1") -NoBuild
if ($LASTEXITCODE -ne 0) { throw "Packaging failed." }

Write-Host "Done: artifacts\R4V9N1-LightMyFire_Coal_Resin-0.5.3.zip" -ForegroundColor Green
