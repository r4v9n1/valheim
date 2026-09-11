param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$LocalProjectRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\BrennivinProtection"
$DllPath = Join-Path $LocalProjectRoot "dist\BrennivinProtection.dll"
$PluginDir = Join-Path $ValheimDir "BepInEx\plugins\BrennivinProtection"

& (Join-Path $ProjectRoot "build.ps1") -ValheimDir $ValheimDir
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
Copy-Item -LiteralPath $DllPath -Destination (Join-Path $PluginDir "BrennivinProtection.dll") -Force

Write-Host "Installed BrennivinProtection to $PluginDir" -ForegroundColor Green
