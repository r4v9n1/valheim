param([string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim")
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $Root "rebuild-all.ps1") -ValheimDir $ValheimDir
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

$Dll = Join-Path $Root "dist\LightMyFire.dll"
$TargetDir = Join-Path $ValheimDir "BepInEx\plugins\LightMyFire"
$Target = Join-Path $TargetDir "LightMyFire.dll"

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
if (Test-Path -LiteralPath $Target) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    Copy-Item -LiteralPath $Target -Destination "$Target.bak-$stamp" -Force
    Write-Host "Backed up existing client DLL." -ForegroundColor Yellow
}

Copy-Item -LiteralPath $Dll -Destination $Target -Force
$builtHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Dll).Hash
$installedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Target).Hash
if ($builtHash -ne $installedHash) { throw "Installed client DLL does not match freshly built DLL." }

Write-Host "Installed LightMyFire 0.5.1: $Target" -ForegroundColor Green
Write-Host "DLL SHA256: $installedHash"
Write-Host "Install the SAME 0.5.1 build on the dedicated server before connecting." -ForegroundColor Cyan
