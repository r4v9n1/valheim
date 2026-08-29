param([switch]$ReplaceProductionAsset)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$UnityProject = Join-Path $ProjectRoot "unity\LightMyFireModels"
$UnityEditor = "C:\Program Files\Unity\Hub\Editor\6000.0.61f1\Editor\Unity.exe"
$OutputDir = if ($ReplaceProductionAsset) { Join-Path $ProjectRoot "assets" } else { Join-Path $ProjectRoot "artifacts\generated-assets" }
$PreviewPath = Join-Path $ProjectRoot "artifacts\LightMyFire-model-preview.png"
$LogPath = Join-Path $ProjectRoot "artifacts\LightMyFire-unity-build.log"

if (!$ReplaceProductionAsset) {
    Write-Host "SAFE MODE: generated AssetBundle goes to artifacts\\generated-assets and will NOT replace the verified 0.5.1 production bundle." -ForegroundColor Yellow
    Write-Host "BUILD-AND-INSTALL.bat does not run build-assets.ps1 for the 0.5.1 correction." -ForegroundColor Yellow
}

if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) {
    throw "Unity 6000.0.61f1 was not found at $UnityEditor"
}

New-Item -ItemType Directory -Force -Path $OutputDir,(Split-Path -Parent $PreviewPath) | Out-Null

$arguments = @(
    "-batchmode",
    "-quit",
    "-projectPath", $UnityProject,
    "-executeMethod", "BuildLightMyFireModels.BuildWindows",
    "-lightMyFireOutput", $OutputDir,
    "-lightMyFirePreview", $PreviewPath,
    "-logFile", $LogPath
)
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden

if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $LogPath) { Get-Content -LiteralPath $LogPath -Tail 200 }
    throw "Unity model build failed with exit code $($process.ExitCode)"
}

if (-not (Select-String -LiteralPath $LogPath -SimpleMatch "LIGHTMYFIRE_BUNDLE_VALIDATED" -Quiet)) {
    Get-Content -LiteralPath $LogPath -Tail 200
    throw "Unity finished without the LightMyFire bundle validation marker."
}

Get-Content -LiteralPath $LogPath -Tail 40
Write-Host "AssetBundle: $(Join-Path $OutputDir 'lightmyfire_assets')"
Write-Host "Preview: $PreviewPath"
